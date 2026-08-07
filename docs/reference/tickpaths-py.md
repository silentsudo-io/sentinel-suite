---
layout: sentinel-ref
title: "tickpaths.py"
blurb: "Lab (Python) · unversioned · 129 lines"
---

# tickpaths.py

> `Sentinel/Lab/viz/tickpaths.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 129 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | [SENTINEL_AZIMUTH_SPEC](../../SENTINEL_AZIMUTH_SPEC.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
Sentinel Tick-Path Viewer — graphical browser for the Deck's manual tick-path records.

    cd Sentinel\\Lab
    .\\.venv\\Scripts\\streamlit run viz\\tickpaths.py
    → http://localhost:8501

Reads the sidecars the Deck writes to  Sentinel\\Excursions\\ticks\\<id>.jsonl :
  line 1  = JSON header (schema "tick.1"): tradeId, inst, bartype, dir, entry/exit, MFE/MAE, partial, ticks
  line 2+ = {"ms": <ms-from-entry>, "px": <price>}  per tick

The point of the chart is to READ THE ENTRY SHAPE — did the trade go favorable first (early/good entry),
or eat adverse heat first (late)? So the primary y-axis is FAVORABLE EXCURSION in ticks (entry = 0,
positive = in your favor, negative = heat), which makes the early-vs-late fingerprint jump out.
```

