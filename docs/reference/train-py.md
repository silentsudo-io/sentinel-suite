---
layout: sentinel-ref
title: "train.py"
blurb: "Lab (Python) · unversioned · 338 lines"
---

# train.py

> `Sentinel/Lab/train.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 338 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | [SENTINEL_ML_SPEC](../../SENTINEL_ML_SPEC.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
Sentinel offline trainer.

    python train.py --inst GC --bartype SentinelTBars --barrier 20 --cost 1.5

Phase 1 (calibration) runs on schema 1.2 -- start it today, no NinjaTrader change.
Phase 2 (weights) needs the schema-1.3 decision vector; it is skipped with a loud
notice until those rows exist.

Emits Sentinel\\Model.conf. Nothing here ever touches bin\\Custom.
```

