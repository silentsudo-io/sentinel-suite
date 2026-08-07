# SentinelButterworthFilter_v1_0_0.cs

> `bin/Custom/Indicators/SentinelButterworthFilter_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 272 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelButterworthFilter_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel.Smoothers` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 Sentinel Butterworth Filter — Ehlers Butterworth low-pass (smoother block)  |   Version v1.0.0
 File: SentinelButterworthFilter_v1_0_0.cs  |  namespace …Indicators.Sentinel.Smoothers  |  display "Sentinel Butterworth Filter"

 ⚠ NO ORDERS · NO STATE SEAM — a read-only SMOOTHER building block, not a Council voter. It draws a
 smoothed line + a Sentinel glass card and publishes nothing (a filter has no verdict). Signal tools may
 consume its plot; it is also a Sentinel-branded Butterworth in its own right.

 PROVENANCE / LICENSE: CLEAN-ROOM. Reimplemented from the PUBLIC John Ehlers Butterworth filter formula
 (2-pole & 3-pole IIR low-pass, published in "Cybernetic Analysis for Stocks and Futures") — a mathematical
 DSP method, not copyrightable. No third-party code, variable names, or structure were copied; the "Au"
 source was read ONLY to confirm the pole count (2/3). See repo NOTICE.

 MATH (Ehlers, radians; degree form 180/P° == π/P rad):
   2-pole: a=exp(-√2·π/P); b=2a·cos(√2·π/P); c1=(1-b+a²)/4; c2=b; c3=-a²
           y = c1·(x + 2·x[1] + x[2]) + c2·y[1] + c3·y[2]
   3-pole: a=exp(-π/P); b=2a·cos(1.738·π/P); c=a²; d1=(1-b+c)(1-c)/8; d2=b+c; d3=-(c+b·c); d4=c²
           y = d1·(x + 3·x[1] + 3·x[2] + x[3]) + d2·y[1] + d3·y[2] + d4·y[3]

 ASSUMPTIONS:
   • Poles is restricted to {2,3} (the two Ehlers Butterworth variants); other values clamp into range.
   • 3-pole cosine argument uses the canonical Ehlers coefficient 1.738 (the "Au" source used √3≈1.732 —
     a near-identical de-tuning; 1.738 is the published value).
   • Recursion is computed live each tick from the current bar's Input and PRIOR-bar filter outputs
     (Value[1..]); early bars (CurrentBar < Poles) seed to Input[0].

 CHANGELOG
   v1.0.0 (2026-07-12) — clean-room Butterworth filter + Sentinel plumbing (naming law, glass card, label remover).
```

