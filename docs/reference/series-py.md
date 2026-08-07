---
layout: sentinel-ref
title: "series.py"
blurb: "Azimuth (Python) · unversioned · 219 lines"
---

# series.py

> `Sentinel/Azimuth/bars/series.py`

| | |
|---|---|
| **Family** | Azimuth (Python) |
| **Version** | — |
| **Size** | 219 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | [SENTINEL_AZIMUTH_SPEC](../../SENTINEL_AZIMUTH_SPEC.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
`BarSeries` -- what a ported bar type returns, and how it reaches the engine.

THE ONE INTERFACE
-----------------
`engine\\bars.py` already owns the seam: `bars_from_end_idx(tape, end_idx)` builds
an engine `Bars` from "which tape row closed each bar". A ported bar type only has
to produce that. This module adds the ONE thing that seam cannot express on its own:

    a Sentinel bar type's OHLC is not always a tape price.

A Renko brick's open and close are LEVELS on the tick grid, not prices that traded;
a Heikin-Ashi-derived type averages. So the port returns its own OHLCV alongside
`end_idx`, and `to_engine_bars` takes the interval geometry from the seam and the
prices from the port. The seam is used, not replaced.

    end_idx[k] = index of the LAST tape row whose volume was accumulated into bar k.

⚠ THAT INDEX CAN REPEAT. A bar type may emit several bars from ONE tape row --
Renko's gap-fill bricks are the canonical case: a price jump of n bricks emits n-1
bars that contain no tape rows at all. Such a bar has `start_idx == -1`,
`tick_count == 0`, and repeats the previous bar's `end_idx`.

`end_idx` is NON-DECREASING and the engine represents these natively (engine
README 2.2): a repeat means "these bars closed on the same tape row", and a
zero-row interval offers no fill opportunity because no market data occurred in
it. `to_engine_bars` therefore passes them straight through. It must NEVER merge
them away -- silently collapsing a bar renumbers every bar after it, which would
misalign the gate against NinjaTrader's own indices.
```

