---
layout: sentinel-ref
title: "SentinelGaussianFilter_v1_0_0.cs"
blurb: "Indicators · 1.0.0 · 267 lines"
---

# SentinelGaussianFilter_v1_0_0.cs

> `bin/Custom/Indicators/SentinelGaussianFilter_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 267 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelGaussianFilter_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel.Smoothers` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 Sentinel Gaussian Filter — Ehlers Gaussian low-pass (smoother block)        |   Version v1.0.0
 File: SentinelGaussianFilter_v1_0_0.cs  |  namespace …Indicators.Sentinel.Smoothers  |  display "Sentinel Gaussian Filter"

 ⚠ NO ORDERS · NO STATE SEAM — a read-only SMOOTHER building block, not a Council voter. It draws a
 smoothed line + a Sentinel glass card and publishes nothing (a filter has no verdict).

 PROVENANCE / LICENSE: CLEAN-ROOM. Reimplemented from the PUBLIC John Ehlers Gaussian filter formula
 (1..4 pole IIR low-pass with binomial recursion, published in "Gaussian and Other Low Lag Filters") —
 a mathematical DSP method, not copyrightable. No third-party code, variable names, or structure were
 copied; the "Au" source was read ONLY to confirm the pole range (1..4) and the β/α definition. See repo NOTICE.

 MATH (Ehlers; 360/P° == 2π/P rad):
   β = (1 − cos(2π/P)) / (2^(1/N) − 1)      (2^(1/N) == √2^(2/N))
   α = (P==1) ? 1 : −β + √(β² + 2β)          ( = −β + √(β(β+2)) )
   N-pole recursion, g = (1−α):
     1: y = α ·x + g·y[1]
     2: y = α²·x + 2g·y[1] − g²·y[2]
     3: y = α³·x + 3g·y[1] − 3g²·y[2] + g³·y[3]
     4: y = α⁴·x + 4g·y[1] − 6g²·y[2] + 4g³·y[3] − g⁴·y[4]
   (recursion coefficients are the signed binomial C(N,k), the standard N-fold single-pole cascade.)

 ASSUMPTIONS:
   • Poles is restricted to {1,2,3,4}; other values clamp into range.
   • Recursion is computed live each tick from the current bar's Input and PRIOR-bar filter outputs
     (Value[1..N]); early bars (CurrentBar < Poles) seed to Input[0].

 CHANGELOG
   v1.0.0 (2026-07-12) — clean-room Gaussian filter + Sentinel plumbing (naming law, glass card, label remover).
```

