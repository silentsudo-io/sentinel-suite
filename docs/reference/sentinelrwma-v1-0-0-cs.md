# SentinelRWMA_v1_0_0.cs

> `bin/Custom/Indicators/SentinelRWMA_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 235 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelRWMA_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel.Smoothers` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 Sentinel RWMA — Range-Weighted Moving Average (Sentinel smoother building block)   |   Version v1.0.0
 File: SentinelRWMA_v1_0_0.cs  |  namespace …Indicators.Sentinel.Smoothers  |  display "Sentinel RWMA"

 ⚠ NO ORDERS · NO STATE SEAM — a read-only SMOOTHER building block, not a Council voter. It draws the
 range-weighted average + a Sentinel glass card and publishes nothing (a moving average has no verdict).

 PROVENANCE / LICENSE: CLEAN-ROOM. Algorithm identified from a GPL LizardIndicators source
 (amaRWMA.cs, GPL-3.0) but NO GPL code was used — reimplemented from the general, publicly-documented
 RANGE-WEIGHTED moving-average method: each bar contributes to the average in proportion to a function of
 its high-low range, so wide (high-conviction) bars pull the average more than narrow ones. Weighting a
 moving average by bar range/volatility is a public mathematical method, not copyrightable. No third-party
 code/variable-names/structure were copied.

   CANONICAL RANGE-WEIGHTED MA over a window of n = min(CurrentBar+1, Period) bars:
       rᵢ    = (High[i] - Low[i]) / TickSize          (bar range measured in ticks)
       wᵢ    = (1 + rᵢ)²                              (+1 keeps zero-range bars non-zero; squared emphasises range)
       Value = Σ (wᵢ · Input[i]) / Σ wᵢ

 ASSUMPTIONS: (1) Weight = (1 + tickRange)² — the +1 avoids a zero weight on doji/zero-range bars and the
 square emphasises wide bars; this matches the confirmed source weighting and is the natural closed form of
 the public method. (2) Requires price data (uses High/Low); on non-price input the range is undefined, so
 the indicator falls back to the raw input for that bar. (3) Window shrinks during warm-up and stays
 normalised by its own weight sum.

 CHANGELOG
   v1.0.0 (2026-07-12) — initial: clean-room range-weighted MA + Sentinel naming law, glass card, label remover.
```

