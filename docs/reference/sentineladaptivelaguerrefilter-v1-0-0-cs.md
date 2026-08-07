# SentinelAdaptiveLaguerreFilter_v1_0_0.cs

> `bin/Custom/Indicators/SentinelAdaptiveLaguerreFilter_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 283 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelAdaptiveLaguerreFilter_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel.Smoothers` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 Sentinel Adaptive Laguerre Filter — Ehlers self-adjusting Laguerre (Sentinel smoother building block)  |  Version v1.0.0
 File: SentinelAdaptiveLaguerreFilter_v1_0_0.cs  |  namespace …Indicators.Sentinel.Smoothers  |  display "Sentinel AdaptiveLaguerreFilter"

 ⚠ NO ORDERS · NO STATE SEAM — a read-only SMOOTHER building block, not a Council voter. It draws a
 smoothed line + a Sentinel glass card; it publishes nothing (a moving average has no verdict).

 PROVENANCE / LICENSE: CLEAN-ROOM. The algorithm (John Ehlers' ADAPTIVE 4-element Laguerre filter — the
 same Laguerre polynomial IIR filter, but its damping factor self-adjusts each bar from the normalized
 recent tracking error) was IDENTIFIED from a GPL-3.0 LizardIndicators source (amaAdaptiveLaguerreFilter.cs),
 but NO GPL code was used — this is reimplemented FRESH from the canonical public formula (Ehlers, "Time
 Warp Without Space Travel"), a mathematical method which is not copyrightable. No third-party code,
 variable names, or structure copied.

 ADAPTATION (canonical Ehlers): each bar,  diff = |price − filt[1]| ;  over the last Length diffs find
 HH (max) and LL (min) ;  ratio = (diff − LL)/(HH − LL)  (carry prior alpha when HH == LL) ;  the adaptive
 alpha α = MEDIAN of the last 5 ratios ;  gamma = 1 − α ;  then the standard 4-element Laguerre recursion
 runs with that α:  L0 = α·price + γ·L0[1] ,  L1 = −γ·L0 + L0[1] + γ·L1[1] , … ,  Value = (L0+2L1+2L2+L3)/6.

 ASSUMPTIONS: the median window is fixed at 5 (Ehlers' canonical value; the GPL source used the same). The
 HH/LL search window is the last `Period` diffs. Early bars are seeded (diff=0, ratio=alpha=0.5, filter=input).

 CHANGELOG
   v1.0.0 (2026-07-12) — clean-room Ehlers Adaptive Laguerre filter (self-adjusting alpha via normalized-
            error median) + Sentinel plumbing (naming law, glass card, label remover). Member of
            …Indicators.Sentinel.Smoothers.
```

