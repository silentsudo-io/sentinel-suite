# SentinelCoralFilter_v1_0_0.cs

> `bin/Custom/Indicators/SentinelCoralFilter_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 260 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelCoralFilter_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel.Smoothers` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 Sentinel CoralFilter — Tillson T3 triple-smoothed EMA ("Coral" trend filter)   |   Version v1.0.0
 File: SentinelCoralFilter_v1_0_0.cs  |  namespace …Indicators.Sentinel.Smoothers  |  display "Sentinel CoralFilter"

 ⚠ NO ORDERS · NO STATE SEAM — a read-only SMOOTHER building block, not a Council voter. It draws the
 T3-smoothed line + a Sentinel glass card and publishes nothing (a moving average has no verdict). A
 low-lag baseline the signal tools can consume, and a Sentinel-branded T3/Coral filter in its own right.

 PROVENANCE / LICENSE: CLEAN-ROOM. Algorithm identified from a GPL LizardIndicators source
 (amaCoralFilter.cs, GPL-3.0) but NO GPL code was used — reimplemented from the canonical PUBLIC
 Tillson T3 formula (Tim Tillson, "Better Moving Averages", TASC 1998), a mathematical method that is
 not copyrightable. The "Coral" filter is a T3 with volume-factor d and an EMA cascade whose smoothing
 factor derives from the period. No third-party code/variable-names/structure were copied.

   CANONICAL T3 (six cascaded EMAs e1..e6, α = 2/(1+k), k = 1 + (Period-1)/2, d = coefficient):
       e1 = α·src + (1-α)·e1₋₁   … e6 = α·e5 + (1-α)·e6₋₁
       T3 = c1·e6 + c2·e5 + c3·e4 + c4·e3
       c1 = -d³ ,  c2 = 3d²+3d³ ,  c3 = -(3d+6d²+3d³) ,  c4 = 1+3d+3d²+d³

 ASSUMPTIONS: (1) d default = 0.4 (the common Coral/T3 volume factor); exposed as CoefficientMultiplier.
 (2) EMA smoothing factor derived via k = 1 + (Period-1)/2 → α = 2/(1+k) (the standard T3 period mapping).
 (3) Dropped the source's trend-classification / neutral-threshold / paint-bar / sound-alert machinery —
 this is a pure smoother, so only Period + CoefficientMultiplier survive. (4) Early bars seed the cascade
 to the first input.

 CHANGELOG
   v1.0.0 (2026-07-12) — initial: clean-room T3/Coral filter + Sentinel naming law, glass card, label remover.
```

