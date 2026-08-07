# SentinelLaguerreFilter_v1_0_0.cs

> `bin/Custom/Indicators/SentinelLaguerreFilter_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 230 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelLaguerreFilter_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel.Smoothers` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 Sentinel Laguerre Filter — Ehlers 4-element Laguerre (Sentinel smoother building block)  |  Version v1.0.0
 File: SentinelLaguerreFilter_v1_0_0.cs  |  namespace …Indicators.Sentinel.Smoothers  |  display "Sentinel LaguerreFilter"

 ⚠ NO ORDERS · NO STATE SEAM — a read-only SMOOTHER building block, not a Council voter. It draws a
 smoothed line + a Sentinel glass card; it publishes nothing (a moving average has no verdict).

 PROVENANCE / LICENSE: CLEAN-ROOM. The algorithm (John Ehlers' 4-element Laguerre polynomial IIR filter)
 was IDENTIFIED from a GPL-3.0 LizardIndicators source (amaLaguerreFilter.cs), but NO GPL code was used —
 this is reimplemented FRESH from the canonical public formula (Ehlers, "Time Warp Without Space Travel"),
 a mathematical method which is not copyrightable. No third-party code, variable names, or structure copied.

 ASSUMPTIONS: the GPL source exposed only a Period and DERIVED gamma from it (γ = 1 − 2/(1.9 + 0.1·Period)).
 Per the canonical Ehlers form (and this task's spec) the Sentinel version instead exposes the damping
 factor Gamma DIRECTLY as the parameter (0..1, default 0.8) — higher gamma = smoother/laggier. Early bars
 are seeded from the input value.

 CHANGELOG
   v1.0.0 (2026-07-12) — clean-room Ehlers 4-element Laguerre filter + Sentinel plumbing (naming law,
            glass card, label remover). Member of …Indicators.Sentinel.Smoothers.
```

