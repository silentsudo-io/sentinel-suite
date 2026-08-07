---
layout: sentinel-ref
title: "tapeio.py"
blurb: "Azimuth (Python) · unversioned · 150 lines"
---

# tapeio.py

> `Sentinel/Azimuth/bars/tapeio.py`

| | |
|---|---|
| **Family** | Azimuth (Python) |
| **Version** | — |
| **Size** | 150 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | [SENTINEL_AZIMUTH_SPEC](../../SENTINEL_AZIMUTH_SPEC.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
Loading the real tape for bar-type work -- including the one clause it violates.

`engine.contract.load_session` REFUSES the shipped `tape\\GC 02-26` files. Measured
2026-08-05: `2025-12-09.parquet` carries 140 rows with `bid > ask` and validation
stops at the first one. That is SPEC §3.2's open defect -- crossed quotes are real L1
event granularity, the population is not yet characterised (two measurements disagree
by ~5x), and no policy has been agreed.

⛔ This module does NOT decide that policy and does not touch `engine\\` or `tape\\`.
It does the narrow thing a bar-type port needs:

  * every other contract clause is still enforced HARD -- dtypes, monotonic ts_ms,
    finite book, legal `kind`, zero size on quote rows, the bar-snap check -- by
    running `contract.validate` on a PROBE copy whose ask is widened to the bid;
  * the crossed rows are COUNTED and returned, never dropped and never repaired;
  * the tape handed back is the file, unmodified.

⭐ Why that is sound for THIS track and not a general licence: the tape must reach a
bar type as NinjaTrader saw it, crossed rows included, because the gate compares
against what NT actually produced -- not against a cleaner book NT never had.

⛔ CORRECTED 2026-08-05. This note previously claimed "Renko, TBars and Flux never
touch bid/ask, so a crossed quote cannot move a bar boundary." **That is FALSE for
Flux.** SentinelFlux classifies every trade by the Lee-Ready quote rule, which reads
bid and ask on each print; measured, a quote-less rebuild yields 3,261 vs 3,171 bars
on 2025-12-09 sharing only 15.6% of boundaries. The BEHAVIOUR here was right for the
wrong reason, and the wrong reason is the dangerous half: it invites a later
"simplification" that repairs the book on the way in and silently changes every Flux
boundary. Renko and TBars do read `last`/`size` only; Flux does not.

The fill model (§4.3) is a separate place the same defect bites, and that is the
engine's problem to resolve, not something to paper over here.
A silently dropped row is a silently changed fill.
```

