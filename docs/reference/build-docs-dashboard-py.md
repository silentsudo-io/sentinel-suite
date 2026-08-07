---
layout: sentinel-ref
title: "build_docs_dashboard.py"
blurb: "Lab (Python) · unversioned · 178 lines"
---

# build_docs_dashboard.py

> `Sentinel/Lab/grafana/build_docs_dashboard.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 178 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | [SENTINEL_DOCS_HEALTH_SPEC](../../SENTINEL_DOCS_HEALTH_SPEC.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
Generate Lab/grafana/dashboards/sentinel-docs.json — the Docs-Health board over the
docs_health / docs_finding / docs_facts tables the docs audit probe writes. Reproducible
(edit here, re-run; Grafana's provider auto-reloads within 30s).

⚠ frser-sqlite reads an integer time column as SECONDS -> time-series select `ts_ms/1000 AS time`.
Same Sentinel color law: cyan=live, green=good, red=bad, amber=warn.
```

