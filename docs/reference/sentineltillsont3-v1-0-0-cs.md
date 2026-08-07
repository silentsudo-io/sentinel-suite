# SentinelTillsonT3_v1_0_0.cs

> `bin/Custom/Indicators/SentinelTillsonT3_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 249 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelTillsonT3_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel.Smoothers` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 Sentinel Tillson T3 — 6-pole T3 smoother (Sentinel smoother building block)   |   Version v1.0.0
 File: SentinelTillsonT3_v1_0_0.cs  |  namespace …Indicators.Sentinel.Smoothers  |  display "Sentinel TillsonT3"

 ⚠ NO ORDERS · NO STATE SEAM — a read-only SMOOTHER building block, not a Council voter. It draws a
 smoothed line + a Sentinel glass card; it publishes nothing (a moving average has no verdict). A baseline
 the signal tools can consume, and a Sentinel-branded T3 in its own right.

 PROVENANCE / LICENSE: CLEAN-ROOM. The algorithm (Tim Tillson's T3 — a 6-pole cascade of exponential
 moving averages combined with a volume-factor weighting) was IDENTIFIED from a GPL-3.0 LizardIndicators
 source (amaTillsonT3.cs), but NO GPL code was used — this is reimplemented FRESH from the canonical public
 formula (Tillson, "Smoothing Techniques for More Accurate Signals", TASC Jan 1998), a mathematical method
 which is not copyrightable. No third-party code, variable names, or structure were copied.

 ASSUMPTIONS: implements the standard "Tillson" mode (all six EMAs use lookback = Period; α = 2/(Period+1)).
 The source's optional "Fulks-Matulich" period-rescale mode + its CalcMode enum are intentionally omitted
 (Sentinel prefers plain params over new enums). Early bars are seeded from the input value.

 CHANGELOG
   v1.0.0 (2026-07-12) — clean-room Tillson T3 (6 cascaded EMAs + volume-factor combination) + Sentinel
            plumbing (naming law, glass card, label remover). Member of …Indicators.Sentinel.Smoothers.
```

