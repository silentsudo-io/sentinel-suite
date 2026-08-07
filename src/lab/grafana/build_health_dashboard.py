#!/usr/bin/env python3
"""Generate Lab/grafana/dashboards/sentinel-health.json — the comprehensive Sentinel + NT
health board over the tables the health probe writes. Reproducible (the skill re-runs it);
edit here, not the JSON. Grafana's provider auto-reloads within 30s (allowUiUpdates:true).

⚠ Time-series time column: frser-sqlite reads an integer time column as SECONDS, so every
time-series query selects `ts_ms/1000 AS time` (ms→s) — raw ms lands the points in the far
future and Grafana shows 'Data outside time range'."""
import json, os

HERE = os.path.dirname(os.path.abspath(__file__))
OUT  = os.path.join(HERE, "dashboards", "sentinel-health.json")
DS   = {"type": "frser-sqlite-datasource", "uid": "sentineldb"}
_id, Y = [0], [0]

# ── Sentinel Dark palette (exact tokens from the landing page / SentinelSkin) ──
# The color law: ONE accent = cyan (live/watching); green = up/good, red = down/bad, amber = warn.
C_ACCENT = "#3FD1E0"  # cyan  — live / watching (the only accent)
C_UP     = "#25D08B"  # green — up / good / ok
C_DOWN   = "#FF5C6A"  # red   — down / bad
C_WARN   = "#F2B34C"  # amber — warn


def nid():
    _id[0] += 1
    return _id[0]


def tgt(sql, ts=False):
    t = {"refId": "A", "queryType": "time series" if ts else "table",
         "queryText": sql, "rawQueryText": sql}
    if ts:
        t["timeColumns"] = ["time"]
    return t


def P(title, typ, x, w, h, sql, ts=False):
    return {"id": nid(), "title": title, "type": typ, "datasource": DS,
            "gridPos": {"x": x, "y": Y[0], "w": w, "h": h}, "targets": [tgt(sql, ts)]}


def fc(d):
    return {"defaults": d, "overrides": []}


def opts_stat(mode="value"):
    return {"colorMode": "background", "graphMode": "none", "textMode": mode,
            "reduceOptions": {"calcs": ["lastNotNull"]}}


LATEST = "(SELECT MAX(ts_ms) FROM {t})"
def latest(col, t="health", where=""):
    return f"SELECT {col} AS value FROM {t} {where} ORDER BY ts_ms DESC LIMIT 1"


def updown(title, col, x, w=4, invert=False):
    on_t, off_t = ("ENGAGED", "CLEAR") if invert else ("UP", "DOWN")
    on_c, off_c = (C_DOWN, C_UP) if invert else (C_UP, C_DOWN)
    p = P(title, "stat", x, w, 4, latest(col))
    p["fieldConfig"] = fc({"mappings": [{"type": "value", "options": {
        "0": {"text": off_t, "color": off_c, "index": 0},
        "1": {"text": on_t, "color": on_c, "index": 1}}}],
        "color": {"mode": "fixed", "fixedColor": "text"}})
    p["options"] = opts_stat()
    return p


def count(title, col, x, w=4, danger=1, unit=None, dec=None, good_high=False):
    p = P(title, "stat", x, w, 4, latest(col))
    steps = ([{"color": C_DOWN, "value": None}, {"color": C_UP, "value": danger}] if good_high
             else [{"color": C_UP, "value": None}, {"color": C_DOWN, "value": danger}])
    d = {"thresholds": {"mode": "absolute", "steps": steps}, "color": {"mode": "thresholds"}}
    if unit: d["unit"] = unit
    if dec is not None: d["decimals"] = dec
    p["fieldConfig"] = fc(d)
    p["options"] = opts_stat()
    return p


def age(title, col, x, w=4, g=15, y=45):
    p = P(title, "stat", x, w, 4, latest(col))
    p["fieldConfig"] = fc({"unit": "s", "decimals": 0, "color": {"mode": "thresholds"},
                           "thresholds": {"mode": "absolute", "steps": [
                               {"color": C_UP, "value": None}, {"color": C_WARN, "value": g},
                               {"color": C_DOWN, "value": y}]}})
    p["options"] = opts_stat()
    return p


def gauge(title, col, x, w=4, unit="percent", maxv=100, warn=70, crit=90):
    p = P(title, "gauge", x, w, 5, latest(col))
    p["fieldConfig"] = fc({"unit": unit, "min": 0, "max": maxv, "color": {"mode": "thresholds"},
                           "thresholds": {"mode": "absolute", "steps": [
                               {"color": C_UP, "value": None}, {"color": C_WARN, "value": warn},
                               {"color": C_DOWN, "value": crit}]}})
    p["options"] = {"reduceOptions": {"calcs": ["lastNotNull"]}, "showThresholdLabels": False}
    return p


def num(title, col, x, w=4, unit=None, dec=0, accent=False):
    p = P(title, "stat", x, w, 4, latest(col))
    d = {"color": {"mode": "fixed", "fixedColor": C_ACCENT if accent else "text"}, "decimals": dec}
    if unit: d["unit"] = unit
    p["fieldConfig"] = fc(d)
    p["options"] = opts_stat()
    return p


def table(title, sql, x, w, h):
    p = P(title, "table", x, w, h, sql)
    p["fieldConfig"] = fc({"custom": {"align": "auto", "filterable": True}})
    p["options"] = {"showHeader": True}
    return p


def barchart(title, sql, x, w, h, unit=None):
    p = P(title, "barchart", x, w, h, sql)
    d = {"custom": {"orientation": "horizontal", "lineWidth": 1, "fillOpacity": 80}}
    if unit: d["unit"] = unit
    p["fieldConfig"] = fc(d)
    p["options"] = {"showValue": "auto", "legend": {"showLegend": False}}
    return p


def series(title, sql, x, w, h, unit=None, color=None):
    p = P(title, "timeseries", x, w, h, sql, ts=True)
    d = {"custom": {"drawStyle": "line", "lineWidth": 2, "fillOpacity": 8, "showPoints": "never"}}
    if unit: d["unit"] = unit
    if color: d["color"] = {"mode": "fixed", "fixedColor": color}
    p["fieldConfig"] = fc(d)
    p["options"] = {"legend": {"displayMode": "list", "placement": "bottom"}, "tooltip": {"mode": "multi"}}
    return p


def row(title):
    r = {"id": nid(), "title": title, "type": "row", "collapsed": False,
         "gridPos": {"x": 0, "y": Y[0], "w": 24, "h": 1}, "panels": []}
    Y[0] += 1
    return r


panels = []
def add(*ps): panels.extend(ps)
def down(h): Y[0] += h


# ══ 🛡 SAFETY ══════════════════════════════════════════════════════════════════
add(row("🛡  Safety — am I safe to trade right now"))
add(updown("NinjaTrader", "nt_up", 0), updown("Responding", "nt_responding", 4),
    updown("Kill switch", "kill", 8, invert=True), count("Naked positions", "arc_naked", 12),
    count("Crit alerts · 5m", "alertcrit_5m", 16),
    P("Day P&L (all acct)", "stat", 20, 4, 4, latest("day_pnl_total")))
panels[-1]["fieldConfig"] = fc({"unit": "currencyUSD", "color": {"mode": "thresholds"},
    "thresholds": {"mode": "absolute", "steps": [{"color": C_DOWN, "value": None}, {"color": C_UP, "value": 0}]}})
panels[-1]["options"] = opts_stat()
down(4)
add(table("Governor · per account (latest)",
          "SELECT account, status, day_pnl AS 'day P&L', cap, loss_stop AS 'loss stop', "
          "loss_used_pct AS 'loss used %', allowed FROM governor_health "
          f"WHERE ts_ms={LATEST.format(t='governor_health')} ORDER BY account", 0, 12, 7),
    table("Connections (latest)",
          "SELECT name, status FROM connection_health "
          f"WHERE ts_ms={LATEST.format(t='connection_health')} ORDER BY name", 12, 6, 7),
    table("Feeds · lag per instrument (latest)",
          "SELECT instrument AS instr, lag_s AS 'lag s', stall_s AS 'stall s', healthy FROM feed_health "
          f"WHERE ts_ms={LATEST.format(t='feed_health')} ORDER BY lag_s DESC", 18, 6, 7))
down(7)

# ══ 🧠 BRAIN ═══════════════════════════════════════════════════════════════════
add(row("🧠  Brain — is the Council alive & complete"))
add(count("Live Councils · 5m", "councils_5m", 0, danger=999, good_high=False),
    count("Quiet Councils", "quiet_councils", 4, danger=1),
    num("Last conviction", "last_conviction", 8, dec=2, accent=True),
    count("Fires today", "fires_today", 12, danger=999),
    count("Scope contention · 5m", "contention_5m", 16, danger=1),
    num("Last vol×", "last_volx", 20, dec=1, accent=True))
down(4)
add(table("Quiet-Council detector · per scope (age since last verdict)",
          "SELECT COALESCE(scope_friendly, scope) AS scope, last_age_s AS 'age s', conviction AS conv, "
          "bias, size, quiet FROM scope_health "
          f"WHERE ts_ms={LATEST.format(t='scope_health')} ORDER BY last_age_s DESC", 0, 12, 8),
    table("Arc slots (latest)",
          "SELECT instrument, strategy, health, pos_qty AS pos, fills_today AS fills, "
          "day_pnl AS 'day P&L', last_signal_age AS 'sig age s' FROM arc_slots "
          f"WHERE ts_ms={LATEST.format(t='arc_slots')} ORDER BY instrument", 12, 12, 8))
down(8)
add(table("Roster health · per scope",
          "SELECT COALESCE(scope_friendly, scope) AS scope, present||'/'||declared AS roster, "
          "missing, unexpected FROM roster_health "
          f"WHERE ts_ms={LATEST.format(t='roster_health')} ORDER BY scope", 0, 12, 7),
    barchart("Veto reasons · 5m",
             f"SELECT reason, cnt FROM veto_5m WHERE ts_ms={LATEST.format(t='veto_5m')} ORDER BY cnt DESC", 12, 6, 7),
    table("Eye · per instrument",
          "SELECT instrument, score, direction AS dir, age_s AS 'age s', source FROM eye_health "
          f"WHERE ts_ms={LATEST.format(t='eye_health')} ORDER BY instrument", 18, 6, 7))
down(7)
add(series("Council errors · alerts · contention · naked (5m rolling)",
           "SELECT ts_ms/1000 AS time, err_5m AS errors, alertcrit_5m AS 'crit alerts', "
           "contention_5m AS contention, naked_5m AS naked FROM health ORDER BY time", 0, 24, 6))
down(6)

# ══ 💰 P&L & ACCOUNTS ══════════════════════════════════════════════════════════
add(row("💰  P&L & accounts"))
add(count("Win-rate today", "winrate_today", 0, danger=50, unit="percent", dec=0, good_high=True),
    num("Trades today", "trades_today", 4),
    P("Day P&L total", "stat", 8, 4, 4, latest("day_pnl_total")),
    num("Accounts connected", "accts_connected", 12),
    num("Accounts total", "accts_total", 16),
    count("Arc naked", "arc_naked", 20, danger=1))
panels[-4]["fieldConfig"] = fc({"unit": "currencyUSD", "color": {"mode": "thresholds"},
    "thresholds": {"mode": "absolute", "steps": [{"color": C_DOWN, "value": None}, {"color": C_UP, "value": 0}]}})
panels[-4]["options"] = opts_stat()
down(4)
add(series("Day P&L total (session)", "SELECT ts_ms/1000 AS time, day_pnl_total AS 'day P&L' FROM health ORDER BY time",
           0, 12, 7, unit="currencyUSD"),
    barchart("Day P&L · per account (latest)",
             f"SELECT account, day_pnl FROM governor_health WHERE ts_ms={LATEST.format(t='governor_health')} "
             "ORDER BY day_pnl", 12, 12, 7, unit="currencyUSD"))
down(7)
add(barchart("Loss-limit used % · per account (latest)",
             f"SELECT account, loss_used_pct FROM governor_health WHERE ts_ms={LATEST.format(t='governor_health')} "
             "ORDER BY loss_used_pct DESC", 0, 12, 6, unit="percent"),
    series("Eye score (GC)", "SELECT ts_ms/1000 AS time, score FROM eye_health WHERE instrument='GC' ORDER BY time",
           12, 12, 6, color=C_ACCENT))
down(6)

# ══ 🔩 RESOURCES & INFRA ═══════════════════════════════════════════════════════
add(row("🔩  Resources & infra — is the plumbing flowing"))
add(gauge("NT CPU %", "nt_cpu", 0, unit="percent", warn=60, crit=85),
    num("NT RAM", "nt_mem_mb", 4, unit="decmbytes"),
    num("NT uptime", "nt_uptime_s", 8, unit="s"),
    num("Disk free", "disk_free_gb", 12, unit="decgbytes", dec=0),
    gauge("Disk used %", "disk_used_pct", 16, unit="percent", warn=80, crit=92),
    num("DB size", "db_mb", 20, unit="decmbytes"))
down(5)
add(age("StateService age", "state_age_s", 0), age("Ingester age", "ingest_age_s", 4, g=20, y=90),
    updown("Grafana", "grafana_up", 8), updown("Streamlit", "streamlit_up", 12),
    updown("Health probe", "probe_up", 16),
    count("Errors · 5m", "err_5m", 20, danger=1))
down(4)
add(series("NT CPU % & RAM (MB)",
           "SELECT ts_ms/1000 AS time, nt_cpu AS 'cpu %', nt_mem_mb AS 'ram MB' FROM health ORDER BY time", 0, 12, 6),
    series("DB / WAL size (MB)", "SELECT ts_ms/1000 AS time, db_mb AS db, wal_mb AS wal FROM health ORDER BY time",
           12, 12, 6, unit="decmbytes"))
down(6)
add(series("Max feed lag (s) — per-instrument (fills when a feed is active)",
           "SELECT ts_ms/1000 AS time, MAX(lag_s) AS 'max lag' FROM feed_health GROUP BY ts_ms ORDER BY time",
           0, 12, 6, unit="s"),
    series("Freshness (s) — state.json & ingest",
           "SELECT ts_ms/1000 AS time, state_age_s AS state, ingest_age_s AS ingest FROM health ORDER BY time",
           12, 12, 6, unit="s"))
down(6)
add(table("Recent critical events",
          "SELECT ts, kind, severity, detail FROM health_event ORDER BY id DESC LIMIT 25", 0, 24, 6))
down(6)

# ══ 🗃 CORPUS — is the DATA I am recording complete ════════════════════════════
# Distinct from everything above: those panels watch the LIVE system, these watch the ARTIFACT on
# disk. The 2026-07-23 audition bake was live-healthy the whole time (roster line read COMPLETE
# 20/20) while writing rows that permanently lacked BRK/FLUX/CVB — a live-only board cannot see
# that, because the discrepancy IS between the live claim and the recorded file. Written by
# health\corpus_probe.py + Lab\verify_votes.py.
add(row("🗃  Corpus — is the data I am recording complete"))
_VH = "(SELECT MAX(ts_ms) FROM vote_health)"
add(P("Lanes missing a voter", "stat", 0, 4, 4,
      f"SELECT COUNT(*) AS value FROM vote_health WHERE ts_ms={_VH} AND missing<>''"),
    P("Lanes w/ partial coverage", "stat", 4, 4, 4,
      f"SELECT COUNT(*) AS value FROM vote_health WHERE ts_ms={_VH} AND partial<>''"),
    P("Brick lanes w/o levels", "stat", 8, 4, 4,
      f"SELECT COUNT(*) AS value FROM vote_health WHERE ts_ms={_VH} AND brick=1 AND brk_pct<99"),
    P("Lanes audited", "stat", 12, 4, 4, f"SELECT COUNT(*) AS value FROM vote_health WHERE ts_ms={_VH}"),
    P("Corpus rows · 3d", "stat", 16, 4, 4, latest("ex_rows", "corpus_integrity")),
    P("Provenance %", "stat", 20, 4, 4, latest("prov_coverage_pct", "corpus_integrity")))
for _p, _danger in ((panels[-6], 1), (panels[-5], 1), (panels[-4], 1)):
    _p["fieldConfig"] = fc({"color": {"mode": "thresholds"}, "thresholds": {"mode": "absolute", "steps": [
        {"color": C_UP, "value": None}, {"color": C_DOWN, "value": _danger}]}})
    _p["options"] = opts_stat()
for _p in (panels[-3], panels[-2]):
    _p["fieldConfig"] = fc({"color": {"mode": "fixed", "fixedColor": C_ACCENT}, "decimals": 0})
    _p["options"] = opts_stat()
panels[-1]["fieldConfig"] = fc({"unit": "percent", "decimals": 0, "color": {"mode": "thresholds"},
    "thresholds": {"mode": "absolute", "steps": [{"color": C_DOWN, "value": None}, {"color": C_UP, "value": 99}]}})
panels[-1]["options"] = opts_stat()
down(4)
add(table("Vote-vector completeness · per lane (latest audit)",
          "SELECT lane, rows, present || '/' || expected AS voters, missing, partial, "
          "brk_pct AS 'brk %', CASE brick WHEN 1 THEN 'brick' ELSE '' END AS type, "
          "CASE thin WHEN 1 THEN 'thin' ELSE '' END AS note "
          f"FROM vote_health WHERE ts_ms={_VH} ORDER BY (missing<>'') DESC, (partial<>'') DESC, lane",
          0, 24, 8))
down(8)
add(table("Corpus events — completeness & integrity (change-only)",
          "SELECT ts, severity, kind, detail FROM corpus_events ORDER BY id DESC LIMIT 25", 0, 24, 7))
down(7)

# ---------------------------------------------------------------- Lab faults -----------------
# Swallowed exceptions in the Lab's Python (lab_faults.swallow()). The whole point of the row is
# that "nothing failed" and "something failed silently" used to look identical. 0 here is a real
# statement; before this row, the absence of a number was not.
add(row("🧯  Lab faults — what failed quietly in the Python"))
_LF = "(SELECT MAX(ts_ms) FROM lab_faults)"
# The two headline counts read `health`, NOT `lab_faults`: `health` gets a row every cycle whether or
# not anything failed, so 0 means "measured zero just now". Reading an empty lab_faults would show 0
# for a DEAD probe too -- the same ambiguity between "nothing happened" and "nothing is watching"
# that this row exists to abolish.
add(P("Swallowed faults · 24h", "stat", 0, 4, 4, latest("faults_24h")),
    P("Distinct fault tags", "stat", 4, 4, 4, latest("fault_tags")),
    P("Processes affected", "stat", 8, 4, 4,
      f"SELECT COALESCE(MAX(procs),0) AS value FROM lab_faults WHERE ts_ms={_LF}"),
    # Suppressed = occurrences the rate limiter did NOT write. A large gap means a fault is hot and
    # the log is showing you a fraction of it -- worth knowing, and invisible from the log alone.
    P("Suppressed (not logged)", "stat", 12, 4, 4,
      f"SELECT COALESCE(SUM(occurrences - lines),0) AS value FROM lab_faults WHERE ts_ms={_LF}"))
for _p in panels[-4:]:
    # Amber, not red: a swallowed fault is something to look at, not something that stops trading.
    # Reserving red for the safety row keeps the board's alarm vocabulary meaningful.
    _p["fieldConfig"] = fc({"color": {"mode": "thresholds"}, "thresholds": {"mode": "absolute", "steps": [
        {"color": C_UP, "value": None}, {"color": C_WARN, "value": 1}]}})
    _p["options"] = opts_stat()
down(4)
add(table("Swallowed faults · by tag (last 24h)",
          "SELECT tag, occurrences AS occ, lines AS logged, procs, first_ts AS first, last_ts AS last, "
          f"last_detail AS detail FROM lab_faults WHERE ts_ms={_LF} ORDER BY occurrences DESC LIMIT 25",
          0, 24, 7))
down(7)

dash = {"uid": "sentinel-health", "title": "Sentinel · Health", "tags": ["sentinel", "health", "ops"],
        "timezone": "browser", "schemaVersion": 39, "version": 1, "editable": True,
        "refresh": "10s", "time": {"from": "now-3h", "to": "now"}, "panels": panels}

os.makedirs(os.path.dirname(OUT), exist_ok=True)
with open(OUT, "w", encoding="utf-8") as fh:
    json.dump(dash, fh, indent=2)
print(f"wrote {OUT}  ({len(panels)} panels)")
