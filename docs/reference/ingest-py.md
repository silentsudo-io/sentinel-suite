---
layout: sentinel-ref
title: "ingest.py"
blurb: "Lab (Python) · unversioned · 632 lines"
---

# ingest.py

> `Sentinel/Lab/ingest/ingest.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 632 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | [SENTINEL_DATA_PLATFORM_SPEC](../../SENTINEL_DATA_PLATFORM_SPEC.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
Sentinel tick-corpus ingester — JSONL sidecars → SQLite.

Reads the Deck's tick-path files (Sentinel\\Excursions\\ticks\\*.jsonl) into
Sentinel\\Lab\\db\\sentinel.db  (trades + ticks). Idempotent — skips files unchanged by mtime,
re-ingests a file if it changed. NT writes the files; THIS owns the DB, so a crash here can never
touch the trading process.

    python ingest.py            # one full scan, then exit
    python ingest.py --watch    # scan, then poll every 2s for new/changed files (live)
    python ingest.py --init     # create schema only

Feeds the Streamlit explorer (viz\\explorer.py) and (Phase 1) Grafana's SQLite datasource.
Spec: bin\\Custom\\Docs\\SENTINEL_DATA_PLATFORM_SPEC.md.
```

