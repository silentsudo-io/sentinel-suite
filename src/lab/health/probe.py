#!/usr/bin/env python3
"""
Sentinel HEALTH probe — samples the live health of NinjaTrader + the Sentinel suite into
Sentinel\\Lab\\db\\sentinel.db, where Grafana's SQLite datasource charts it.

READ-ONLY on the trading process. It samples files/process/ports only — state.json (the
StateService heartbeat), sentinel.log (the event stream), the Ledger, the trades DB, and OS
process/port state. It NEVER touches NinjaTrader internals or orders, so a crash here can never
affect trading (same discipline as the ingester, which OWNS the DB).

    python probe.py            # one sample, then exit
    python probe.py --watch    # sample every INTERVAL seconds forever (self-healing loop)
    python probe.py --init     # create/migrate the health schema only

Single-instance: binds 127.0.0.1:8502 on start; a second copy exits immediately. Feeds the
"Sentinel · Health" Grafana board. Spec: bin\\Custom\\Docs\\SENTINEL_DATA_PLATFORM_SPEC.md.
"""
from __future__ import annotations
import os, sys, re, json, sqlite3, socket, subprocess, argparse, time, shutil
import datetime as dt

try:
    import psutil
except ImportError:
    psutil = None

HERE = os.path.dirname(os.path.abspath(__file__))
LAB  = os.path.abspath(os.path.join(HERE, ".."))
SENT = os.path.abspath(os.path.join(HERE, "..", ".."))

# Friendly scope rendering ('GC.212201v6x24' -> 'GC · SentinelTBars Speed 12'). Standing rule: a scope
# is NEVER shown to a human raw — the board reads the *_friendly columns. Raw stays the machine key.
sys.path.insert(0, LAB)
from lab_faults import swallow
try:
    from sentinel_lab.bartag import friendly_scope
except Exception:
    def friendly_scope(s):
        return s
DB       = os.path.join(LAB, "db", "sentinel.db")
STATE    = os.path.join(SENT, "state.json")
LOG      = os.path.join(SENT, "sentinel.log")
LAB_FAULT_LOG = os.path.join(LAB, "logs", "lab-faults.log")   # written by lab_faults.swallow()
LEDGER   = os.path.join(SENT, "Ledger")
WAL      = DB + "-wal"
DATA_DRIVE = os.path.splitdrive(SENT)[0] + "\\"

INTERVAL   = 30
WINDOW_MIN = 5
QUIET_SEC  = 120           # a Council silent longer than this = "quiet" (stale/crashed vs standing down)
GUARD_PORT = 8502
PORTS = {"grafana_up": 3000, "streamlit_up": 8501}

SCHEMA = """
CREATE TABLE IF NOT EXISTS health(
  ts_ms INTEGER PRIMARY KEY, ts TEXT,
  nt_up INTEGER, nt_responding INTEGER,
  kill INTEGER, auto_kill INTEGER, kill_by_risk INTEGER, risk_running INTEGER,
  conn_ok INTEGER, conn_total INTEGER,
  state_age_s REAL, ingest_age_s REAL,
  grafana_up INTEGER, streamlit_up INTEGER, probe_up INTEGER,
  err_5m INTEGER, alertcrit_5m INTEGER, contention_5m INTEGER, naked_5m INTEGER,
  councils_5m INTEGER, fires_today INTEGER, last_conviction REAL,
  accts_connected INTEGER, accts_total INTEGER,
  arc_running INTEGER, arc_slots INTEGER, arc_naked INTEGER,
  db_mb REAL, wal_mb REAL);
CREATE TABLE IF NOT EXISTS governor_health(
  ts_ms INTEGER, account TEXT, day_pnl REAL, cap REAL, loss_stop REAL,
  allowed INTEGER, status TEXT, day_used_pct REAL, loss_used_pct REAL,
  PRIMARY KEY(ts_ms, account));
CREATE TABLE IF NOT EXISTS arc_slots(
  ts_ms INTEGER, instrument TEXT, strategy TEXT, health TEXT,
  pos_qty INTEGER, fills_today INTEGER, day_pnl REAL, in_session INTEGER, last_signal_age REAL,
  PRIMARY KEY(ts_ms, instrument, strategy));
CREATE TABLE IF NOT EXISTS roster_health(
  ts_ms INTEGER, scope TEXT, present INTEGER, declared INTEGER, missing TEXT, unexpected TEXT,
  scope_friendly TEXT,
  PRIMARY KEY(ts_ms, scope));
CREATE TABLE IF NOT EXISTS connection_health(
  ts_ms INTEGER, name TEXT, status TEXT, connected INTEGER, lag_s REAL, stall_s REAL,
  PRIMARY KEY(ts_ms, name));
CREATE TABLE IF NOT EXISTS feed_health(
  ts_ms INTEGER, instrument TEXT, lag_s REAL, stall_s REAL, got_tick INTEGER, healthy INTEGER,
  PRIMARY KEY(ts_ms, instrument));
CREATE TABLE IF NOT EXISTS eye_health(
  ts_ms INTEGER, instrument TEXT, score REAL, direction INTEGER, age_s REAL, source TEXT,
  PRIMARY KEY(ts_ms, instrument));
CREATE TABLE IF NOT EXISTS copier_health(
  ts_ms INTEGER PRIMARY KEY, running INTEGER, leader TEXT, policy TEXT, followers INTEGER);
CREATE TABLE IF NOT EXISTS scope_health(
  ts_ms INTEGER, scope TEXT, last_age_s REAL, conviction REAL, bias INTEGER, size REAL, quiet INTEGER,
  scope_friendly TEXT,
  PRIMARY KEY(ts_ms, scope));
CREATE TABLE IF NOT EXISTS veto_5m(
  ts_ms INTEGER, reason TEXT, cnt INTEGER, PRIMARY KEY(ts_ms, reason));
CREATE TABLE IF NOT EXISTS health_event(
  id INTEGER PRIMARY KEY AUTOINCREMENT, ts_ms INTEGER, ts TEXT, kind TEXT, severity TEXT, detail TEXT);
CREATE TABLE IF NOT EXISTS probe_meta(key TEXT PRIMARY KEY, value TEXT);
CREATE TABLE IF NOT EXISTS lab_faults(
  ts_ms INTEGER, tag TEXT, lines INTEGER, occurrences INTEGER, procs INTEGER,
  first_ts TEXT, last_ts TEXT, last_detail TEXT,
  PRIMARY KEY(ts_ms, tag));
"""

# columns added to `health` after its original 29 — applied idempotently via _migrate()
HEALTH_ADDED = [
    ("nt_cpu", "REAL"), ("nt_mem_mb", "REAL"), ("nt_uptime_s", "REAL"),
    ("disk_free_gb", "REAL"), ("disk_used_pct", "REAL"),
    ("day_pnl_total", "REAL"), ("winrate_today", "REAL"), ("trades_today", "INTEGER"),
    ("quiet_councils", "INTEGER"), ("last_volx", "REAL"),
    ("faults_24h", "INTEGER"), ("fault_tags", "INTEGER"),
]

LOG_TS  = re.compile(r"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3})")
# lab_faults.py line: "2026-07-25 19:36:00  [docs.audit._read] FileNotFoundError: ... at audit.py:81  (probe.py pid 17784)"
# The first bracket group is always the tag; the message may itself contain brackets ("[Errno 2]").
FAULT_LINE = re.compile(r"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})\s+\[([^\]]+)\]\s+(.*?)\s+\((\S+) pid (\d+)\)\s*$")
FAULT_XN   = re.compile(r"\[x(\d+) total for this tag\]")
# Written by lab_faults._flush_summary() at process exit -- authoritative per (tag, pid), because the
# rate limiter suppresses occurrences WITHOUT writing a line, so line-counting alone under-reports.
FAULT_SUM  = re.compile(r"^SUMMARY (\d+) occurrences this process")
ROSTER  = re.compile(r"(GC\.[0-9A-Za-z@.]+|[A-Z]{1,5}\.[0-9A-Za-z@.]+).*?roster (\d+)/(\d+)")
MISSING = re.compile(r"⚠([A-Z0-9,]+)")
UNEXP   = re.compile(r"\?([A-Z0-9,]+)")
CONV    = re.compile(r"conv=([0-9.]+)")
SIZE    = re.compile(r"size=([0-9.]+)")
VOLX    = re.compile(r"vol×([0-9.]+)")
VETO    = re.compile(r"VETO:([A-Za-z]+)")
SCOPETAG = re.compile(r"\] (GC\.[0-9A-Za-z@.]+|[A-Z]{1,5}\.[0-9A-Za-z@.]+) ")
BIAS    = re.compile(r"\] [A-Z0-9.@]+ (LONG|SHORT|FLAT)")


def now_ms() -> int:
    return int(dt.datetime.now().timestamp() * 1000)


def _conn():
    c = sqlite3.connect(DB, timeout=15)
    c.execute("PRAGMA journal_mode=WAL")
    c.execute("PRAGMA busy_timeout=8000")
    return c


def _migrate(conn):
    have = {r[1] for r in conn.execute("PRAGMA table_info(health)")}
    for col, typ in HEALTH_ADDED:
        if col not in have:
            conn.execute(f"ALTER TABLE health ADD COLUMN {col} {typ}")
    # friendly-scope display columns (standing rule: never show a raw scope to a human)
    for tbl in ("roster_health", "scope_health"):
        cols = {r[1] for r in conn.execute(f"PRAGMA table_info({tbl})")}
        if "scope_friendly" not in cols:
            conn.execute(f"ALTER TABLE {tbl} ADD COLUMN scope_friendly TEXT")
    conn.commit()


def tail(path, max_bytes=800_000):
    try:
        with open(path, "rb") as fh:
            fh.seek(0, os.SEEK_END)
            fh.seek(max(0, fh.tell() - max_bytes))
            return fh.read().decode("utf-8", "replace").splitlines()
    except OSError as _swex:
        swallow("health.probe.tail", _swex)
        return []


def port_up(port) -> int:
    try:
        with socket.create_connection(("127.0.0.1", port), timeout=0.6):
            return 1
    except OSError as _swex:
        swallow("health.probe.port_up", _swex)
        return 0


def nt_status():
    try:
        base = subprocess.run(["tasklist", "/fi", "imagename eq NinjaTrader.exe", "/nh"],
                              capture_output=True, text=True, timeout=8).stdout
        up = 1 if "NinjaTrader.exe" in base else 0
        if not up:
            return 0, 0
        run = subprocess.run(["tasklist", "/fi", "imagename eq NinjaTrader.exe",
                              "/fi", "status eq running", "/nh"],
                             capture_output=True, text=True, timeout=8).stdout
        return 1, (1 if "NinjaTrader.exe" in run else 0)
    except Exception as _swex:
        swallow("health.probe.nt_status", _swex)
        return None, None


def nt_resources():
    """(cpu%, mem_MB, uptime_s) for NinjaTrader.exe via psutil, or (None,None,None)."""
    if psutil is None:
        return None, None, None
    try:
        for p in psutil.process_iter(["name"]):
            if (p.info["name"] or "").lower() == "ninjatrader.exe":
                # normalize to 0-100% of TOTAL machine CPU (psutil sums across cores, so a busy
                # 2.5-core process reads 250% raw — misleading on a gauge capped at 100).
                cpu = round(p.cpu_percent(interval=0.4) / (psutil.cpu_count() or 1), 1)
                mem = round(p.memory_info().rss / 1e6, 0)
                up  = round(time.time() - p.create_time(), 0)
                return cpu, mem, up
    except Exception as _swex:
        swallow("health.probe.nt_resources", _swex)
    return None, None, None


def read_state():
    try:
        return json.load(open(STATE, encoding="utf-8-sig"))
    except Exception as _swex:
        swallow("health.probe.read_state", _swex)
        return None


def parse_log(lines, since, now):
    err = crit = cont = naked = 0
    scopes, rosters, scope_last = set(), {}, {}
    vetoes = {}
    last_conv = last_volx = None
    for ln in lines:
        m = LOG_TS.match(ln)
        if not m:
            continue
        try:
            t = dt.datetime.strptime(m.group(1), "%Y-%m-%d %H:%M:%S.%f")
        except ValueError as _swex:
            swallow("health.probe.parse_log", _swex)
            continue
        low = ln.lower()
        is_council = "sentinel:council" in low
        # per-scope last-seen tracked over the WHOLE tail (for quiet detection), counts only in-window
        if is_council:
            st = SCOPETAG.search(ln)
            if st:
                sc = st.group(1)
                cv = CONV.search(ln); sz = SIZE.search(ln); bi = BIAS.search(ln)
                scope_last[sc] = (t, float(cv.group(1)) if cv else None,
                                  {"LONG": 1, "SHORT": -1, "FLAT": 0}.get(bi.group(1) if bi else "", 0),
                                  float(sz.group(1)) if sz else None)
        if t < since:
            continue
        if "scope contention" in low: cont += 1
        if "alert-crit" in low:       crit += 1
        if "naked" in low:            naked += 1
        if ("error" in low or "exception" in low) and "fail-open" not in low and "fail-closed" not in low:
            err += 1
        if is_council:
            rm = ROSTER.search(ln)
            if rm:
                scopes.add(rm.group(1))
                miss = MISSING.search(ln); unex = UNEXP.search(ln)
                rosters[rm.group(1)] = (int(rm.group(2)), int(rm.group(3)),
                                        miss.group(1) if miss else "", unex.group(1) if unex else "")
            cm = CONV.search(ln)
            if cm: last_conv = float(cm.group(1))
            vm = VOLX.search(ln)
            if vm: last_volx = float(vm.group(1))
            for r in VETO.findall(ln):
                vetoes[r] = vetoes.get(r, 0) + 1
    # quiet-council: age since last verdict, only for RECENTLY-active scopes (a scope silent longer
    # than RECENT_SEC is a CLOSED chart — gone, not quiet — so drop it rather than cry wolf forever).
    RECENT_SEC = 1800
    scope_age = {}
    for sc, (t, cv, bi, sz) in scope_last.items():
        age = round((now - t).total_seconds(), 1)
        if age <= RECENT_SEC:
            scope_age[sc] = (age, cv, bi, sz)
    return dict(err=err, crit=crit, cont=cont, naked=naked, councils=len(scopes),
                rosters=rosters, last_conv=last_conv, last_volx=last_volx,
                vetoes=vetoes, scope_age=scope_age)


def ledger_today():
    fills = 0
    try:
        day = dt.datetime.now().strftime("%Y-%m-%d")
        path = os.path.join(LEDGER, f"ledger-{day}.jsonl")
        if os.path.exists(path):
            for ln in open(path, encoding="utf-8", errors="replace"):
                ln = ln.strip()
                if not ln: continue
                try:
                    if json.loads(ln).get("evt") == "fill": fills += 1
                except json.JSONDecodeError as _swex:
                    swallow("health.probe.ledger_today", _swex)
    except OSError as _swex:
        swallow("health.probe.ledger_today#2", _swex)
    return fills


def trades_today(conn):
    """(count, winrate) from the ingested trades table for today (UTC date)."""
    try:
        today = dt.datetime.utcnow().strftime("%Y-%m-%d")
        r = conn.execute(
            "SELECT COUNT(*), AVG(CASE WHEN pnl_ticks>0 THEN 1.0 ELSE 0 END) "
            "FROM trades WHERE substr(entry_utc,1,10)=?", (today,)).fetchone()
        return (r[0] or 0), (round(100 * r[1], 1) if r[1] is not None else None)
    except Exception as _swex:
        swallow("health.probe.trades_today", _swex)
        return None, None


def age_s(path):
    try:
        return round(dt.datetime.now().timestamp() - os.path.getmtime(path), 1)
    except OSError as _swex:
        swallow("health.probe.age_s", _swex)
        return None


def _ins(conn, table, d):
    cols = ",".join(d)
    conn.execute(f"INSERT OR REPLACE INTO {table} ({cols}) VALUES ({','.join('?' for _ in d)})",
                 tuple(d.values()))


def lab_faults(hours=24):
    """Roll up Lab\\logs\\lab-faults.log into per-tag counts for the last `hours`.

    Recomputed from the file every cycle, so it is idempotent and needs no watermark -- a probe
    that has to remember where it got to is a probe that silently double-counts or skips after a
    restart. Reads the live log plus one rotated generation, which comfortably covers a 24h window
    given swallow() is rate-limited per tag.

    Two counts, and the difference between them is the point:
      lines       -- how many fault lines were WRITTEN (throttled occurrences are not written)
      occurrences -- how many times the fault actually HAPPENED, reconstructed from swallow()'s
                     "[xN total for this tag]" marker. Reporting only `lines` would under-report a
                     hot fault by exactly the factor the rate limiter is suppressing, which is the
                     same shape of lie the whole silent-catch migration exists to end.
    """
    cutoff = dt.datetime.now() - dt.timedelta(hours=hours)
    agg = {}
    for path in (LAB_FAULT_LOG, LAB_FAULT_LOG + ".1"):
        # Skip a rotation generation that does not exist yet. Without this the probe tails a missing
        # file every cycle, swallow()s the FileNotFoundError, and manufactures a fault of its own --
        # a monitor generating the very signal it is supposed to report. (Found by this monitor, on
        # its first run, watching itself.)
        if not os.path.exists(path):
            continue
        for ln in tail(path, max_bytes=2_500_000):
            m = FAULT_LINE.match(ln)
            if not m:
                continue
            ts_s, tag, body, proc, pid = m.groups()
            try:
                ts = dt.datetime.strptime(ts_s, "%Y-%m-%d %H:%M:%S")
            except ValueError:
                continue
            if ts < cutoff:
                continue
            a = agg.setdefault(tag, dict(lines=0, per_pid={}, first=ts_s, last=ts_s, detail=body))
            key = (proc, pid)
            sm = FAULT_SUM.match(body)
            if sm:
                # Exit summary: authoritative for this pid. Not a fault line -- don't count it as one
                # or the detail column ends up showing bookkeeping instead of the actual error.
                a["per_pid"][key] = max(a["per_pid"].get(key, 0), int(sm.group(1)))
                continue
            a["lines"] += 1
            a["last"] = ts_s
            a["detail"] = body
            if ts_s < a["first"]:
                a["first"] = ts_s
            xm = FAULT_XN.search(body)
            n = int(xm.group(1)) if xm else 0
            prev = a["per_pid"].get(key, 0)
            # A pid's true total is the largest count it reported (xN marker or exit summary); with
            # neither, the process never exceeded the always-print threshold and line-count IS the total.
            a["per_pid"][key] = max(prev + (0 if xm else 1), n)

    out = []
    for tag, a in agg.items():
        out.append(dict(tag=tag, lines=a["lines"], occurrences=sum(a["per_pid"].values()),
                        procs=len(a["per_pid"]), first_ts=a["first"], last_ts=a["last"],
                        last_detail=a["detail"][:300]))
    out.sort(key=lambda r: -r["occurrences"])
    return out


def sample(conn):
    now = dt.datetime.now()
    ms, iso = now_ms(), now.strftime("%Y-%m-%d %H:%M:%S")
    since = now - dt.timedelta(minutes=WINDOW_MIN)

    nt_up, nt_resp = nt_status()
    cpu, mem, uptime = nt_resources()
    st = read_state() or {}
    risk = st.get("risk") or {}
    conns = risk.get("connections") or []
    conn_ok = sum(1 for c in conns if "connected" in str(c).lower())
    gov = st.get("governor") or []
    arc = st.get("arc") or {}
    slots = arc.get("slots") or []
    accts = st.get("accounts") or {}
    eye = st.get("eye") or []
    cop = st.get("copier") or {}

    lg = parse_log(tail(LOG), since, now)
    du = shutil.disk_usage(DATA_DRIVE)
    tt_cnt, tt_wr = trades_today(conn)
    quiet = sum(1 for (a, *_ ) in lg["scope_age"].values() if a > QUIET_SEC)

    # Computed BEFORE the health row so the counts ride on it. A stat panel reading `health` always
    # has a fresh row, so 0 means "measured zero just now" instead of "no rows" -- which is
    # indistinguishable from "the probe died", the exact ambiguity this whole facility removes.
    lf = lab_faults()
    lf_occ = sum(r["occurrences"] for r in lf)

    h = dict(
        ts_ms=ms, ts=iso, nt_up=nt_up, nt_responding=nt_resp,
        kill=1 if st.get("killSwitch") else 0, auto_kill=1 if risk.get("autoKill") else 0,
        kill_by_risk=1 if risk.get("killByRisk") else 0, risk_running=1 if risk.get("running") else 0,
        conn_ok=conn_ok, conn_total=len(conns),
        state_age_s=age_s(STATE), ingest_age_s=age_s(WAL) if os.path.exists(WAL) else age_s(DB),
        grafana_up=port_up(PORTS["grafana_up"]), streamlit_up=port_up(PORTS["streamlit_up"]), probe_up=1,
        err_5m=lg["err"], alertcrit_5m=lg["crit"], contention_5m=lg["cont"], naked_5m=lg["naked"],
        councils_5m=lg["councils"], fires_today=ledger_today(), last_conviction=lg["last_conv"],
        accts_connected=accts.get("connected"), accts_total=accts.get("total"),
        arc_running=1 if arc.get("running") else 0, arc_slots=len(slots),
        arc_naked=sum(1 for s in slots if abs(int(s.get("posQty") or 0)) > 0),
        db_mb=round(os.path.getsize(DB) / 1e6, 1) if os.path.exists(DB) else None,
        wal_mb=round(os.path.getsize(WAL) / 1e6, 1) if os.path.exists(WAL) else 0.0,
        nt_cpu=cpu, nt_mem_mb=mem, nt_uptime_s=uptime,
        disk_free_gb=round(du.free / 1e9, 1), disk_used_pct=round(100 * du.used / du.total, 1),
        day_pnl_total=round(sum((g.get("dailyPnl") or 0) for g in gov), 2),
        winrate_today=tt_wr, trades_today=tt_cnt, quiet_councils=quiet, last_volx=lg["last_volx"],
        faults_24h=lf_occ, fault_tags=len(lf))
    _ins(conn, "health", h)

    for g in gov:
        cap, ls, pnl = g.get("cap") or 0, g.get("lossStop") or 0, g.get("dailyPnl") or 0
        _ins(conn, "governor_health", dict(
            ts_ms=ms, account=g.get("account"), day_pnl=pnl, cap=cap, loss_stop=ls,
            allowed=1 if g.get("allowed") else 0, status=g.get("status"),
            day_used_pct=round(100 * pnl / cap, 1) if cap else None,
            loss_used_pct=round(100 * (-pnl) / ls, 1) if (ls and pnl < 0) else 0.0))

    for s in slots:
        _ins(conn, "arc_slots", dict(
            ts_ms=ms, instrument=s.get("instrument"), strategy=s.get("strategy"), health=s.get("health"),
            pos_qty=s.get("posQty"), fills_today=s.get("fillsToday"), day_pnl=s.get("dayPnl"),
            in_session=1 if s.get("inSession") else 0, last_signal_age=s.get("lastSignalAgeSec")))

    for scope, (p, d, miss, unex) in lg["rosters"].items():
        _ins(conn, "roster_health", dict(ts_ms=ms, scope=scope, present=p, declared=d,
                                          missing=miss, unexpected=unex,
                                          scope_friendly=friendly_scope(scope)))

    for c in conns:                                                   # "Lucid: Connected"
        name, _, status = str(c).partition(":")
        name, status = name.strip(), status.strip()
        _ins(conn, "connection_health", dict(
            ts_ms=ms, name=name, status=status,
            connected=1 if "connected" in status.lower() else 0, lag_s=None, stall_s=None))

    # per-INSTRUMENT feed lag (NT measures lag per subscribed instrument, not per connection).
    # risk.feeds is empty until a strategy/feed is active; then it fills live — no NT change needed.
    for f in (risk.get("feeds") or []):
        if isinstance(f, dict):
            _ins(conn, "feed_health", dict(
                ts_ms=ms, instrument=f.get("instrument"), lag_s=f.get("lagSec"), stall_s=f.get("stallSec"),
                got_tick=1 if f.get("gotTick") else 0, healthy=1 if f.get("healthy") else 0))

    for e in eye:
        _ins(conn, "eye_health", dict(ts_ms=ms, instrument=e.get("instrument"), score=e.get("score"),
                                      direction=e.get("direction"), age_s=e.get("ageSec"), source=e.get("source")))

    _ins(conn, "copier_health", dict(ts_ms=ms, running=1 if cop.get("running") else 0,
                                     leader=cop.get("leader"), policy=cop.get("policy"),
                                     followers=len(cop.get("followers") or [])))

    for scope, (a, cv, bi, sz) in lg["scope_age"].items():
        _ins(conn, "scope_health", dict(ts_ms=ms, scope=scope, last_age_s=a, conviction=cv,
                                        bias=bi, size=sz, quiet=1 if a > QUIET_SEC else 0,
                                        scope_friendly=friendly_scope(scope)))

    for reason, cnt in lg["vetoes"].items():
        _ins(conn, "veto_5m", dict(ts_ms=ms, reason=reason, cnt=cnt))

    for r in lf:
        _ins(conn, "lab_faults", dict(ts_ms=ms, **r))

    _emit_events(conn, ms, iso, nt_up, nt_resp, st, risk, quiet, lf)
    conn.commit()
    return dict(nt_up=nt_up, resp=nt_resp, cpu=cpu, mem=mem, kill=st.get("killSwitch"),
                conn=f"{conn_ok}/{len(conns)}", councils=lg["councils"], quiet=quiet,
                day_pnl=h["day_pnl_total"], wr=tt_wr, disk_gb=h["disk_free_gb"], faults=lf_occ)


def _emit_events(conn, ms, iso, nt_up, nt_resp, st, risk, quiet, lf=()):
    def last(k):
        r = conn.execute("SELECT value FROM probe_meta WHERE key=?", (k,)).fetchone()
        return r[0] if r else None
    def setk(k, v):
        conn.execute("INSERT OR REPLACE INTO probe_meta VALUES (?,?)", (k, str(v)))
    def ev(kind, sev, detail):
        conn.execute("INSERT INTO health_event(ts_ms,ts,kind,severity,detail) VALUES (?,?,?,?,?)",
                     (ms, iso, kind, sev, detail))
    checks = [
        ("nt", "CRIT" if not nt_up else ("WARN" if nt_resp == 0 else "OK"),
         "NT down" if not nt_up else ("NT not responding" if nt_resp == 0 else "NT healthy")),
        ("kill", "CRIT" if st.get("killSwitch") else "OK",
         "KILL SWITCH ENGAGED" if st.get("killSwitch") else "kill clear"),
        ("riskkill", "CRIT" if risk.get("killEngaged") else "OK",
         "Risk kill engaged" if risk.get("killEngaged") else "risk ok"),
        ("quiet", "WARN" if quiet else "OK",
         f"{quiet} Council(s) went quiet (>{QUIET_SEC}s)" if quiet else "councils live"),
        ("labfaults", "WARN" if lf else "OK",
         (f"{sum(r['occurrences'] for r in lf)} swallowed fault(s) in {len(lf)} tag(s), "
          f"top: {lf[0]['tag']} x{lf[0]['occurrences']}") if lf else "no swallowed faults in 24h"),
    ]
    for kind, sev, detail in checks:
        if last(f"lvl_{kind}") != sev:
            if sev != "OK" or last(f"lvl_{kind}") is not None:
                ev(kind, sev, detail)
            setk(f"lvl_{kind}", sev)

    # A fault tag appearing for the FIRST time is the signal worth waking someone for -- an already
    # known, steady fault is noise. Announce each new tag once, then never again.
    new = [r for r in lf if last(f"fault_seen_{r['tag']}") is None]
    for r in new[:5]:
        ev("labfault_new", "WARN", f"NEW swallowed fault {r['tag']} x{r['occurrences']}: {r['last_detail'][:160]}")
    if len(new) > 5:
        ev("labfault_new", "WARN", f"...and {len(new) - 5} more new fault tag(s) this cycle")
    for r in new:
        setk(f"fault_seen_{r['tag']}", r["last_ts"])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--watch", action="store_true")
    ap.add_argument("--init", action="store_true")
    ap.add_argument("--interval", type=int, default=INTERVAL)
    a = ap.parse_args()

    conn = _conn()
    conn.executescript(SCHEMA)
    _migrate(conn)
    conn.commit()
    if a.init:
        print("health schema created/migrated"); return

    # One-shot runs must NOT take the guard: the guard exists to keep a second WATCH loop from
    # double-writing, and blocking a diagnostic read while the daemon runs makes the probe
    # un-inspectable exactly when you most want to inspect it. Matches corpus_probe.py, which
    # already binds only under --loop.
    if not a.watch:
        print(sample(conn)); return

    guard = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    try:
        guard.bind(("127.0.0.1", GUARD_PORT)); guard.listen(1)
    except OSError:
        print(f"another probe holds :{GUARD_PORT} — exiting"); return

    print(f"health probe watching every {a.interval}s -> {DB}")
    while True:
        try:
            print(f"{dt.datetime.now():%H:%M:%S} {sample(conn)}")
        except Exception as e:
            print(f"sample error (continuing): {e}")
            try: conn.close()
            except Exception as _swex:
                swallow("health.probe.main", _swex)
            conn = _conn()
        time.sleep(a.interval)


if __name__ == "__main__":
    main()
