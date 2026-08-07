---
layout: sentinel-ref
title: "sweep.py"
blurb: "Azimuth (Python) · unversioned · 217 lines"
---

# sweep.py

> `Sentinel/Azimuth/engine/sweep.py`

| | |
|---|---|
| **Family** | Azimuth (Python) |
| **Version** | — |
| **Size** | 217 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | [SENTINEL_AZIMUTH_SPEC](../../SENTINEL_AZIMUTH_SPEC.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
Parameter sweeps + the THROUGHPUT MEASUREMENT that sets the Rust bar.

    "Python + NumPy first. Rust only if measurement proves it necessary --
     650 combos in 8.9 s is *their* Rust number; find ours before assuming we
     need theirs. Set the bar before writing the Rust." (spec §6, §10.3)

Run it:

    C:\\ntbv\\Scripts\\python.exe -m engine.sweep --sessions 5 --combos 648

The number it prints is the bar. Rust is justified only when a real workload
misses it, and the workload has to be named when that claim is made.
```

