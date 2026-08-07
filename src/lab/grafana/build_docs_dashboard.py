#!/usr/bin/env python3
"""Generate Lab/grafana/dashboards/sentinel-docs.json — the Docs-Health board over the
docs_health / docs_finding / docs_facts tables the docs audit probe writes. Reproducible
(edit here, re-run; Grafana's provider auto-reloads within 30s).

⚠ frser-sqlite reads an integer time column as SECONDS -> time-series select `ts_ms/1000 AS time`.
Same Sentinel color law: cyan=live, green=good, red=bad, amber=warn."""
import json, os

HERE = os.path.dirname(os.path.abspath(__file__))
OUT  = os.path.join(HERE, "dashboards", "sentinel-docs.json")
DS   = {"type": "frser-sqlite-datasource", "uid": "sentineldb"}
_id, Y = [0], [0]

C_ACCENT = "#3FD1E0"; C_UP = "#25D08B"; C_DOWN = "#FF5C6A"; C_WARN = "#F2B34C"
T = "docs_health"


def nid():
    _id[0] += 1
    return _id[0]


def tgt(sql, ts=False):
    t = {"refId": "A", "queryType": "time series" if ts else "table", "queryText": sql, "rawQueryText": sql}
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
def latest(col, t=T):
    return f"SELECT {col} AS value FROM {t} ORDER BY ts_ms DESC LIMIT 1"


def count(title, col, x, w=3, danger=1, good_high=False):
    p = P(title, "stat", x, w, 4, latest(col))
    steps = ([{"color": C_DOWN, "value": None}, {"color": C_UP, "value": danger}] if good_high
             else [{"color": C_UP, "value": None}, {"color": C_DOWN, "value": danger}])
    p["fieldConfig"] = fc({"thresholds": {"mode": "absolute", "steps": steps}, "color": {"mode": "thresholds"}})
    p["options"] = opts_stat()
    return p


def num(title, col, x, w=3, accent=False):
    p = P(title, "stat", x, w, 4, latest(col))
    p["fieldConfig"] = fc({"color": {"mode": "fixed", "fixedColor": C_ACCENT if accent else "text"}, "decimals": 0})
    p["options"] = opts_stat()
    return p


def warncount(title, col, x, w=3, danger=1):
    """amber-on-nonzero (warnings aren't red-critical)."""
    return warnq(title, latest(col), x, w, danger)


def warnq(title, sql, x, w=3, danger=1):
    """amber-on-nonzero stat from a custom query (value column)."""
    p = P(title, "stat", x, w, 4, sql)
    p["fieldConfig"] = fc({"thresholds": {"mode": "absolute", "steps": [
        {"color": C_UP, "value": None}, {"color": C_WARN, "value": danger}]}, "color": {"mode": "thresholds"}})
    p["options"] = opts_stat()
    return p


# actionable (WARN-level) count of a finding category — the number that should drive attention
def warn_findings(title, category, x, w=3):
    sql = ("SELECT COUNT(*) AS value FROM docs_finding "
           "WHERE ts_ms=(SELECT MAX(ts_ms) FROM docs_finding) "
           f"AND category='{category}' AND severity='WARN'")
    return warnq(title, sql, x, w)


def table(title, sql, x, w, h):
    p = P(title, "table", x, w, h, sql)
    p["fieldConfig"] = fc({"custom": {"align": "auto", "filterable": True}})
    p["options"] = {"showHeader": True}
    return p


def barchart(title, sql, x, w, h):
    p = P(title, "barchart", x, w, h, sql)
    p["fieldConfig"] = fc({"custom": {"orientation": "horizontal", "lineWidth": 1, "fillOpacity": 80}})
    p["options"] = {"showValue": "auto", "legend": {"showLegend": False}}
    return p


def series(title, sql, x, w, h, color=None):
    p = P(title, "timeseries", x, w, h, sql, ts=True)
    d = {"custom": {"drawStyle": "line", "lineWidth": 2, "fillOpacity": 10, "showPoints": "never"}}
    if color:
        d["color"] = {"mode": "fixed", "fixedColor": color}
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


# ══ 📚 AT A GLANCE ══════════════════════════════════════════════════════════════
add(row("📚  docs health — is the documentation telling the truth"))
add(count("Drift score", "drift_score", 0),
    count("Errors", "errors", 3),
    warncount("Warnings", "warns", 6),
    count("Broken links", "broken_links", 9),
    warncount("Missing HTML", "missing_html", 12),
    warncount("Stale HTML", "stale_html", 15),
    warn_findings("Uncontracted (load-bearing)", "uncontracted", 18),
    num("Orphans", "orphans", 21))
down(4)
add(num("Docs total", "docs_total", 0, accent=True),
    num("Contracted", "contracted", 3, accent=True),
    count("Dangling tokens", "dangling_tokens", 6),
    warncount("Code moved", "code_moved", 9),
    num("Stale-version", "stale_version", 12),
    num("Review-due", "review_due", 15),
    num("Unresolved [[wiki]]", "unresolved_wikilinks", 18),
    num("Infos", "infos", 21))
down(4)

# ══ 🔎 FINDINGS ═════════════════════════════════════════════════════════════════
add(row("🔎  findings — what needs a human"))
add(table("Findings (latest scan)",
          "SELECT severity, category, doc, detail FROM docs_finding "
          f"WHERE ts_ms={LATEST.format(t='docs_finding')} "
          "ORDER BY CASE severity WHEN 'ERROR' THEN 0 WHEN 'WARN' THEN 1 ELSE 2 END, category", 0, 16, 11),
    barchart("Findings per doc (worst first)",
             "SELECT doc, COUNT(*) AS findings FROM docs_finding "
             f"WHERE ts_ms={LATEST.format(t='docs_finding')} GROUP BY doc ORDER BY findings DESC LIMIT 15", 16, 8, 11))
down(11)

# ══ 📈 TREND ════════════════════════════════════════════════════════════════════
add(row("📈  trend — getting fresher or staler"))
add(series("Drift score over time", "SELECT ts_ms/1000 AS time, drift_score AS drift FROM docs_health ORDER BY time",
           0, 12, 7, color=C_ACCENT),
    series("Errors · warnings over time",
           "SELECT ts_ms/1000 AS time, errors, warns AS warnings FROM docs_health ORDER BY time", 12, 12, 7))
down(7)

# ══ 🔗 FACTS ════════════════════════════════════════════════════════════════════
add(row("🔗  generated facts — the ground truth tokens render from"))
add(table("Facts (facts.json — single-sourced from code)",
          "SELECT key, value, source FROM docs_facts ORDER BY key", 0, 24, 6))
down(6)

dash = {"uid": "sentinel-docs", "title": "Sentinel · Docs", "tags": ["sentinel", "docs", "ops"],
        "timezone": "browser", "schemaVersion": 39, "version": 1, "editable": True,
        "refresh": "30s", "time": {"from": "now-24h", "to": "now"}, "panels": panels}

os.makedirs(os.path.dirname(OUT), exist_ok=True)
with open(OUT, "w", encoding="utf-8") as fh:
    json.dump(dash, fh, indent=2)
print(f"wrote {OUT}  ({len(panels)} panels)")
