---
layout: sentinel-ref
title: "corpus_probe.py"
blurb: "Lab (Python) · unversioned · 427 lines"
---

# corpus_probe.py

> `Sentinel/Lab/health/corpus_probe.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 427 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
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
```

