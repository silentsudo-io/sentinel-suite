# build_health_dashboard.py

> `Sentinel/Lab/grafana/build_health_dashboard.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 335 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
Generate Lab/grafana/dashboards/sentinel-health.json — the comprehensive Sentinel + NT
health board over the tables the health probe writes. Reproducible (the skill re-runs it);
edit here, not the JSON. Grafana's provider auto-reloads within 30s (allowUiUpdates:true).

⚠ Time-series time column: frser-sqlite reads an integer time column as SECONDS, so every
time-series query selects `ts_ms/1000 AS time` (ms→s) — raw ms lands the points in the far
future and Grafana shows 'Data outside time range'.
```

