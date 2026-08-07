#!/usr/bin/env python3
"""
Sentinel CORPUS-INTEGRITY probe — audits whether the RECORDED corpus itself is trustworthy, and
writes the findings into Sentinel\\Lab\\db\\sentinel.db, where Grafana's SQLite datasource charts it.

This is the fidelity-audit companion to probe.py: probe.py watches the LIVE ops (is NT up, is the
brain alive), this probe watches the DATA-ON-DISK (is what we recorded clean, complete, and
provenance-stamped). It is strictly READ-ONLY on the corpus — it opens the excursion rows, the tick
sidecars and the Ledger for reading only and NEVER writes, moves, or deletes any corpus file. It
writes only its OWN tables (corpus_integrity / corpus_folder / corpus_events / corpus_meta) and never
touches the trades/ticks schema owned by the ingester.

    python corpus_probe.py             # one audit, then exit
    python corpus_probe.py --loop 300  # re-audit every 300s forever (self-healing loop)
    python corpus_probe.py --days 5    # window = today + previous 4 days (default 3)
    python corpus_probe.py --init      # create the schema only

Single-instance: binds 127.0.0.1:8503 on start; a second copy exits immediately. Mirrors probe.py's
DB pattern (WAL + busy_timeout) and emit-on-change events. Spec: bin\\Custom\\Docs\\SENTINEL_DATA_PLATFORM_SPEC.md.

Corpus layout audited:
    Sentinel\\Excursions\\council\\<schema>\\*.jsonl   e.g. 1.3\\ (frozen) / 1.4\\ (provenance-stamped)
    Sentinel\\Excursions\\council\\ticks\\<fireId>.jsonl   tick sidecars (schema ctick.1 / ctick.2)
    Sentinel\\Ledger\\ledger-<date>.jsonl                 append-only fire/fill/action log

Reconciliation join key = (inst, fireTime, firePx, dir) — present in BOTH excursion rows and tick
sidecars, near-unique per fire (episodeId is per-episode, not per-fire, so it is NOT used to join).
"""
from __future__ import annotations
import os, sys, json, sqlite3, socket, argparse, time, glob
import datetime as dt

HERE = os.path.dirname(os.path.abspath(__file__))
LAB  = os.path.abspath(os.path.join(HERE, ".."))
sys.path.insert(0, LAB)
from lab_faults import swallow
import verify_votes as _votes                     # vote-vector completeness (Lab\verify_votes.py)
SENT = os.path.abspath(os.path.join(HERE, "..", ".."))
DB      = os.path.join(LAB, "db", "sentinel.db")
COUNCIL = os.path.join(SENT, "Excursions", "council")
TICKS   = os.path.join(COUNCIL, "ticks")
LEDGER  = os.path.join(SENT, "Ledger")

WINDOW_DAYS = 3            # today + previous 2 days
LOOP_SEC    = 300
GUARD_PORT  = 8503
# fields that (if ever present) expose lookahead/historical contamination. The schema does NOT
# currently expose any such marker — the recorder gates on State.Realtime at WRITE time, so a
# historical row should never reach disk. We still scan for these defensively and report the count;
# an all-zero result means "no marker exposed in the schema", not "proven clean".
HIST_FIELDS = ("isHistorical", "IsHistorical", "historical", "isHist", "hist", "replay", "isReplay")

SCHEMA = """
CREATE TABLE IF NOT EXISTS corpus_integrity(
  ts_ms INTEGER PRIMARY KEY, ts TEXT, window_days INTEGER, cutoff TEXT,
  ledger_fires INTEGER, ex_rows INTEGER, tick_sidecars INTEGER,
  fires_with_sidecar INTEGER, fires_missing_sidecar INTEGER, sidecars_missing_row INTEGER,
  recon_gap INTEGER,
  rows_13 INTEGER, rows_14 INTEGER, unexpected_schema_rows INTEGER, folders_mixed INTEGER,
  prov_rows INTEGER, prov_coverage_pct REAL, core_vers TEXT, rec_vers TEXT,
  trunc_sidecars INTEGER, malformed_lines INTEGER,
  contamination INTEGER, contam_exposed INTEGER,
  stale_dated_rows INTEGER, oldest_fire TEXT,
  ex_files INTEGER, tick_files INTEGER);
CREATE TABLE IF NOT EXISTS corpus_folder(
  ts_ms INTEGER, folder TEXT, rows INTEGER, schemas TEXT, mixed INTEGER, unexpected INTEGER,
  prov_pct REAL, PRIMARY KEY(ts_ms, folder));
CREATE TABLE IF NOT EXISTS corpus_events(
  id INTEGER PRIMARY KEY AUTOINCREMENT, ts_ms INTEGER, ts TEXT, kind TEXT, severity TEXT, detail TEXT);
CREATE TABLE IF NOT EXISTS corpus_meta(key TEXT PRIMARY KEY, value TEXT);
CREATE TABLE IF NOT EXISTS vote_health(
  ts_ms INTEGER, lane TEXT, inst TEXT, bartype TEXT, rows INTEGER,
  expected INTEGER, present INTEGER, missing TEXT, partial TEXT, undeclared TEXT,
  brk_pct REAL, brick INTEGER, thin INTEGER, roster TEXT,
  PRIMARY KEY(ts_ms, lane));
"""


def now_ms() -> int:
    return int(dt.datetime.now().timestamp() * 1000)


def _conn():
    c = sqlite3.connect(DB, timeout=15)
    c.execute("PRAGMA journal_mode=WAL")
    # 30s, not 8s (raised 2026-07-24 after MEASURING it): with ingest.py --watch writing every 2s into a
    # 5.7 GB WAL database, an 8s budget lost the race often enough that the probe threw "database is
    # locked" and skipped whole sample cycles -- a monitor that silently stops sampling is the exact
    # failure mode it exists to catch. A verified write took 3.3s under the same contention.
    c.execute("PRAGMA busy_timeout=30000")
    return c


# columns added to corpus_integrity after its original set — applied idempotently via _migrate()
INTEGRITY_ADDED = [("stale_dated_rows", "INTEGER"), ("oldest_fire", "TEXT")]


def _migrate(conn):
    have = {r[1] for r in conn.execute("PRAGMA table_info(corpus_integrity)")}
    for col, typ in INTEGRITY_ADDED:
        if col not in have:
            conn.execute(f"ALTER TABLE corpus_integrity ADD COLUMN {col} {typ}")
    conn.commit()


def _ins(conn, table, d):
    conn.execute(f"INSERT OR REPLACE INTO {table} ({','.join(d)}) VALUES ({','.join('?' for _ in d)})",
                 tuple(d.values()))


def iter_json(path):
    """Yield (obj_or_None) per line, guarding every read. A partial last line mid-append yields None
    (counted malformed by the caller) but never raises."""
    try:
        with open(path, encoding="utf-8", errors="replace") as fh:
            for ln in fh:
                ln = ln.strip()
                if not ln:
                    continue
                try:
                    yield json.loads(ln)
                except (json.JSONDecodeError, ValueError):
                    yield None
    except OSError as _swex:
        swallow("health.corpus_probe.iter_json", _swex)
        return


def fire_key(o):
    return (o.get("inst"), o.get("fireTime"), o.get("firePx"), o.get("dir"))


def scan(days):
    """Read the corpus for the window and compute every integrity metric. Pure/read-only."""
    cutoff = (dt.date.today() - dt.timedelta(days=days - 1)).isoformat()   # "YYYY-MM-DD"
    # a row whose fireTime is older than this in a realtime-only corpus is a replay/backfill leak
    # (fireTime is a REAL schema field — not an invented marker). Reported separately from the
    # schema-level historical marker (which the schema does not currently expose).
    stale_cut = (dt.date.today() - dt.timedelta(days=30)).isoformat()

    def in_window(fireTime):
        return isinstance(fireTime, str) and fireTime[:10] >= cutoff

    malformed = 0
    contam = 0
    contam_exposed = False
    stale_dated = 0
    oldest_fire = None

    # ---- excursion schema folders (everything under council\ except ticks\) ----
    ex_fires = set()                 # fire keys within window
    ex_rows = 0
    rows_13 = rows_14 = 0
    unexpected = 0
    prov_rows = 0
    core_vers, rec_vers = set(), set()
    folders = {}                     # name -> dict(rows, schemas Counter, unexpected, prov)
    ex_files = 0

    for sub in sorted(glob.glob(os.path.join(COUNCIL, "*"))):
        if not os.path.isdir(sub):
            continue
        name = os.path.basename(sub)
        if name == "ticks":
            continue
        expected = name                                   # folder name IS the expected schema tag
        fstat = folders.setdefault(name, dict(rows=0, schemas={}, unexpected=0, prov=0))
        for path in glob.glob(os.path.join(sub, "*.jsonl")):
            ex_files += 1
            for o in iter_json(path):
                if o is None:
                    malformed += 1
                    continue
                ft = o.get("fireTime")
                if isinstance(ft, str):
                    if ft[:10] < stale_cut:
                        stale_dated += 1
                    if oldest_fire is None or ft < oldest_fire:
                        oldest_fire = ft
                if not in_window(ft):
                    continue
                ex_rows += 1
                fstat["rows"] += 1
                sch = str(o.get("schema"))
                fstat["schemas"][sch] = fstat["schemas"].get(sch, 0) + 1
                if sch != expected:
                    unexpected += 1
                    fstat["unexpected"] += 1
                if sch == "1.3":
                    rows_13 += 1
                elif sch == "1.4":
                    rows_14 += 1
                cv, rv = o.get("coreVer"), o.get("recVer")
                if cv is not None:
                    prov_rows += 1
                    fstat["prov"] += 1
                    core_vers.add(str(cv))
                if rv is not None:
                    rec_vers.add(str(rv))
                for hf in HIST_FIELDS:
                    if hf in o:
                        contam_exposed = True
                        if o.get(hf):
                            contam += 1
                ex_fires.add(fire_key(o))

    folders_mixed = sum(1 for f in folders.values() if len(f["schemas"]) > 1)

    # ---- tick sidecars ----
    tk_fires = set()
    tk_files = trunc = 0
    for path in glob.glob(os.path.join(TICKS, "*.jsonl")):
        header = None
        for o in iter_json(path):        # header row carries the fire metadata; body rows are {ms,px}
            if o is None:
                malformed += 1
                continue
            header = o
            break
        tk_files += 1
        if not header:
            continue
        if not in_window(header.get("fireTime")):
            continue
        if header.get("trunc") is True:
            trunc += 1
        for hf in HIST_FIELDS:
            if hf in header:
                contam_exposed = True
                if header.get(hf):
                    contam += 1
        tk_fires.add(fire_key(header))

    with_sidecar = len(ex_fires & tk_fires)
    missing_sidecar = len(ex_fires - tk_fires)
    sidecar_no_row = len(tk_fires - ex_fires)

    # ---- ledger fires (order-submission rows within window) ----
    ledger_fires = 0
    for path in glob.glob(os.path.join(LEDGER, "ledger-*.jsonl")):
        day = os.path.basename(path)[7:17]               # ledger-YYYY-MM-DD.jsonl
        if day < cutoff:
            continue
        for o in iter_json(path):
            if o is None:
                malformed += 1
                continue
            if o.get("evt") == "order":
                ledger_fires += 1

    prov_pct = round(100 * prov_rows / ex_rows, 1) if ex_rows else 0.0
    for f in folders.values():
        f["prov_pct"] = round(100 * f["prov"] / f["rows"], 1) if f["rows"] else 0.0

    return dict(
        cutoff=cutoff, window_days=days,
        ledger_fires=ledger_fires, ex_rows=ex_rows, tick_sidecars=len(tk_fires),
        fires_with_sidecar=with_sidecar, fires_missing_sidecar=missing_sidecar,
        sidecars_missing_row=sidecar_no_row, recon_gap=missing_sidecar + sidecar_no_row,
        rows_13=rows_13, rows_14=rows_14, unexpected_schema_rows=unexpected, folders_mixed=folders_mixed,
        prov_rows=prov_rows, prov_coverage_pct=prov_pct,
        core_vers=",".join(sorted(core_vers)) or None, rec_vers=",".join(sorted(rec_vers)) or None,
        trunc_sidecars=trunc, malformed_lines=malformed,
        contamination=contam, contam_exposed=1 if contam_exposed else 0,
        stale_dated_rows=stale_dated, oldest_fire=oldest_fire,
        ex_files=ex_files, tick_files=tk_files, folders=folders)


def sample(conn, days):
    now = dt.datetime.now()
    ms, iso = now_ms(), now.strftime("%Y-%m-%d %H:%M:%S")
    m = scan(days)

    row = {k: v for k, v in m.items() if k != "folders"}
    row["ts_ms"] = ms
    row["ts"] = iso
    _ins(conn, "corpus_integrity", row)

    for name, f in m["folders"].items():
        schemas = ";".join(f"{k}:{v}" for k, v in sorted(f["schemas"].items()))
        _ins(conn, "corpus_folder", dict(
            ts_ms=ms, folder=name, rows=f["rows"], schemas=schemas,
            mixed=1 if len(f["schemas"]) > 1 else 0, unexpected=f["unexpected"], prov_pct=f["prov_pct"]))

    # VOTE-VECTOR COMPLETENESS (verify_votes.py). Separate from the schema/provenance audit above
    # because it answers a different question: not "is this row well-formed" but "does it carry the
    # voters its lane declared". The 2026-07-23 audition bake passed every check above and was still
    # unusable -- 18 voters, never BRK/FLUX/CVB. Failing softly here would rebuild that exact blind
    # spot, so an import/scan error is reported as a WARN event rather than silently skipped.
    try:
        vf, vsum, _ = _votes.audit(days)
        for s in vsum:
            _ins(conn, "vote_health", dict(ts_ms=ms, **{k: s[k] for k in (
                "lane", "inst", "bartype", "rows", "expected", "present",
                "missing", "partial", "undeclared", "brk_pct", "brick", "thin", "roster")}))
        m["votes"], m["vote_summary"] = vf, vsum
        m["vote_lanes"] = len(vsum)
    except Exception as e:                       # never let the audit take the probe down
        m["votes"] = [dict(kind="vote_audit_error", severity="WARN", lane="", detail=str(e))]
        m["vote_summary"], m["vote_lanes"] = [], 0

    _emit_events(conn, ms, iso, m)
    conn.commit()
    return dict(cutoff=m["cutoff"], ledger_fires=m["ledger_fires"], ex_rows=m["ex_rows"],
                sidecars=m["tick_sidecars"], with_sc=m["fires_with_sidecar"],
                miss_sc=m["fires_missing_sidecar"], sc_no_row=m["sidecars_missing_row"],
                mixed=m["folders_mixed"], unexpected=m["unexpected_schema_rows"],
                prov_pct=m["prov_coverage_pct"], cores=m["core_vers"],
                trunc=m["trunc_sidecars"], malformed=m["malformed_lines"],
                contam=m["contamination"], contam_field=bool(m["contam_exposed"]),
                stale_dated=m["stale_dated_rows"], oldest_fire=m["oldest_fire"])


def _vote_checks(m):
    """Turn the verify_votes findings into (kind, severity, detail) triples, ONE PER LANE.

    Per-lane rather than one aggregate on purpose: a single "3 lanes incomplete" event flips to OK the
    moment two of them heal, and the third goes quiet while still broken. The de-dup in _emit_events is
    keyed on `kind`, so a lane keyed into the kind gets its own independent alert AND its own recovery.
    Lanes with no findings are emitted as OK so a heal is announced, not merely silent.
    """
    by_lane = {}
    for f in m.get("votes") or []:
        lane = f.get("lane") or "<all>"
        cur = by_lane.setdefault(lane, {"sev": "OK", "why": []})
        if f["severity"] == "CRIT" or (f["severity"] == "WARN" and cur["sev"] != "CRIT"):
            cur["sev"] = f["severity"]
        if f["severity"] in ("CRIT", "WARN"):
            cur["why"].append(f["detail"])

    out = []
    for s in m.get("vote_summary") or []:
        lane = s["lane"]
        hit = by_lane.pop(lane, None)
        if hit and hit["sev"] != "OK":
            out.append((f"votes::{lane}", hit["sev"],
                        f"{lane}: {'; '.join(hit['why'][:4])}" + (" …" if len(hit["why"]) > 4 else "")))
        else:
            out.append((f"votes::{lane}", "OK",
                        f"{lane}: all {s['expected']} declared voter(s) present on {s['rows']} row(s)"))
    for lane, hit in by_lane.items():             # findings with no matching summary (e.g. audit error)
        if hit["sev"] != "OK":
            out.append((f"votes::{lane}" if lane != "<all>" else "vote_audit", hit["sev"],
                        "; ".join(hit["why"][:4])))
    return out


def _emit_events(conn, ms, iso, m):
    """Emit a corpus_events row only when a condition CHANGES level (mirrors probe.py de-dup)."""
    def last(k):
        r = conn.execute("SELECT value FROM corpus_meta WHERE key=?", (k,)).fetchone()
        return r[0] if r else None
    def setk(k, v):
        conn.execute("INSERT OR REPLACE INTO corpus_meta VALUES (?,?)", (k, str(v)))
    def ev(kind, sev, detail):
        conn.execute("INSERT INTO corpus_events(ts_ms,ts,kind,severity,detail) VALUES (?,?,?,?,?)",
                     (ms, iso, kind, sev, detail))

    checks = [
        ("schema_mix", "CRIT" if m["folders_mixed"] else "OK",
         f"{m['folders_mixed']} folder(s) mixing schemas" if m["folders_mixed"] else "no schema mixing"),
        ("unexpected_schema", "WARN" if m["unexpected_schema_rows"] else "OK",
         f"{m['unexpected_schema_rows']} row(s) with wrong schema for their folder"
         if m["unexpected_schema_rows"] else "all rows match folder schema"),
        ("malformed", "WARN" if m["malformed_lines"] > 1 else "OK",
         f"{m['malformed_lines']} malformed line(s) in window" if m["malformed_lines"] > 1
         else "no malformed lines"),
        ("trunc", "WARN" if m["trunc_sidecars"] else "OK",
         f"{m['trunc_sidecars']} truncated tick sidecar(s)" if m["trunc_sidecars"] else "no truncated sidecars"),
        ("multi_core", "WARN" if (m["core_vers"] and "," in m["core_vers"]) else "OK",
         f"multiple coreVer in pool: {m['core_vers']}" if (m["core_vers"] and "," in m["core_vers"])
         else "single/no coreVer"),
        ("contamination", "CRIT" if m["contamination"] else "OK",
         f"{m['contamination']} historical/lookahead row(s)" if m["contamination"] else "no contamination markers"),
        ("stale_dated", "WARN" if m["stale_dated_rows"] else "OK",
         f"{m['stale_dated_rows']} row(s) dated >30d old (replay/backfill leak; oldest {m['oldest_fire']})"
         if m["stale_dated_rows"] else "no stale-dated rows"),
    ] + _vote_checks(m)

    for kind, sev, detail in checks:
        if last(f"lvl_{kind}") != sev:
            if sev != "OK" or last(f"lvl_{kind}") is not None:
                ev(kind, sev, detail)
            setk(f"lvl_{kind}", sev)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--loop", type=int, metavar="SEC", help="re-audit every SEC seconds forever")
    ap.add_argument("--days", type=int, default=WINDOW_DAYS, help="window = today + previous N-1 days")
    ap.add_argument("--init", action="store_true", help="create the schema only, then exit")
    a = ap.parse_args()

    conn = _conn()
    conn.executescript(SCHEMA)
    _migrate(conn)
    conn.commit()
    if a.init:
        print("corpus schema created"); return

    if not a.loop:
        s = sample(conn, a.days)
        print(json.dumps(s, indent=2))
        return

    guard = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    try:
        guard.bind(("127.0.0.1", GUARD_PORT)); guard.listen(1)
    except OSError:
        print(f"another corpus_probe holds :{GUARD_PORT} — exiting"); return

    print(f"corpus probe auditing every {a.loop}s (window {a.days}d) -> {DB}")
    while True:
        try:
            print(f"{dt.datetime.now():%H:%M:%S} {sample(conn, a.days)}")
        except Exception as e:
            print(f"audit error (continuing): {e}")
            try: conn.close()
            except Exception as _swex:
                swallow("health.corpus_probe.main", _swex)
            conn = _conn()
        time.sleep(a.loop)


if __name__ == "__main__":
    main()
