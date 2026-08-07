---
layout: sentinel-ref
title: "regime_study.py"
blurb: "Lab (Python) · unversioned · 291 lines"
---

# regime_study.py

> `Sentinel/Lab/harness/regime_study.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 291 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
regime_study — run the whole label-free apparatus across contracts and see what is universal.

Attacks Threat 1 of the whitepaper: every number in it came from ONE contract, ONE window
(GC 02-26, Dec 2025). The apparatus took a day to build and costs minutes to re-run, so the only
reason it had not been re-run was that nobody asked.

Predictions are pre-registered in PREREGISTRATION_regime_study.md, written before any contract other
than GC 02-26 was converted. This module prints each result beside its prediction and marks HIT or
MISS. It is deliberately not able to "explain" a miss.

  P1  Hurst          every contract in 0.55-0.70, GC-family spread < 0.05
  P2  Sweep fraction MGC materially LOWER than GC on the same dates
  P3  Ladder slope   every contract 2.7x-3.3x per halving
  P4  Quote coverage tick-rule fallback < 1% everywhere
  P5  Format/tz      zero-trade hour lands on 16 (America/Chicago) everywhere

WHY THESE ARE SAFE TO SEARCH OVER
---------------------------------
All five are label-free: no outcomes, no holdout, nothing to overfit. They measure the geometry of
the tape, which is the one class of result cheap search cannot manufacture. Bar-equivalence is
deliberately NOT included -- it needs a SentinelBarDump answer key per contract, and mixing a
label-free sweep with a tuned comparison is how a study stops being trustworthy.

Usage:
    python -m harness.regime_study --contracts "GC 02-26" "GC 02-25" "MGC 02-25" "GC 08-26"
```

