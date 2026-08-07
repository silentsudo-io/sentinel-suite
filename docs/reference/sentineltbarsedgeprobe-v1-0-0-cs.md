# SentinelTBarsEdgeProbe_v1_0_0.cs

> `bin/Custom/Strategies/SentinelTBarsEdgeProbe_v1_0_0.cs`

| | |
|---|---|
| **Family** | Strategies |
| **Version** | 1.0.0 |
| **Size** | 121 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelTBarsEdgeProbe_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Strategies` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 Sentinel TBars Edge Probe — the PHASE-A honest-fill test        |  Version v1.0.0
 File: SentinelTBarsEdgeProbe_v1_0_0.cs  |  namespace …Strategies (base — strategies do NOT sub-namespace)

 THE ONE QUESTION THIS ANSWERS: is the ~66% first-touch win rate on TBars 6/24 a REAL,
 TRADEABLE edge, or an artifact of the excursion recorder's bar-level label?

 METHOD — deliberately DUMB + COUNCIL-FREE, so it isolates the BARS (no 21-voter confound):
   • Signal   = the TBars brick direction (sign of Close-Open). Enter on a FLIP (default) or every brick.
   • Barrier  = a SYMMETRIC R target/stop, R = max(MinTicks, AtrMult × ATR(AtrPeriod)) in ticks —
                this MIRRORS the recorder's ATR-scaled first-touch barrier, so a win here == firstTouch=+1 there.
   • One position at a time (act only when flat) → each trade is an independent first-touch barrier test.
   • Managed SetProfitTarget/SetStopLoss so NT's own fill engine resolves the barrier.

 ⚠ HOW TO RUN IT HONESTLY (the whole point): Strategy Analyzer ▸ your GC SentinelTBars 6/24 series ▸
   **Order Fill Resolution = High, 1 Tick** (NOT the default bar-close fill — that would just reproduce the
   optimistic bar-level label). Set Slippage to your real cost. Compare the win rate + expectancy to the
   corpus 66%. If it SURVIVES → the bars carry the edge (advance to Phase B: variants + session overlay).
   If it CRATERS (à la CompressionBase 81%→37.5%) → the 66% was label optimism, and we've saved months.

 NO Sentinel plumbing on purpose: no SentinelCore, no card, no seam — a clean, independent instrument.

 CHANGELOG
   v1.0.0 (2026-07-13) — NEW. Phase-A honest-fill probe of the TBars edge.
```

