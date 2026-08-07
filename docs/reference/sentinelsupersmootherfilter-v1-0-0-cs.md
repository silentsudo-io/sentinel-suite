---
layout: sentinel-ref
title: "SentinelSuperSmootherFilter_v1_0_0.cs"
blurb: "Indicators · 1.0.0 · 223 lines"
---

# SentinelSuperSmootherFilter_v1_0_0.cs

> `bin/Custom/Indicators/SentinelSuperSmootherFilter_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 223 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelSuperSmootherFilter_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel.Smoothers` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 Sentinel SuperSmootherFilter — Ehlers 2-Pole Super Smoother (Sentinel smoother building block)   |   Version v1.0.0
 File: SentinelSuperSmootherFilter_v1_0_0.cs  |  namespace …Indicators.Sentinel.Smoothers  |  display "Sentinel SuperSmootherFilter"

 ⚠ NO ORDERS · NO STATE SEAM — a read-only SMOOTHER building block, not a Council voter. It draws a
 smoothed line + a Sentinel glass card; it publishes nothing.

 PROVENANCE / LICENSE: CLEAN-ROOM. Reimplemented from John F. Ehlers' public 2-pole Super Smoother filter
 ("Cybernetic Analysis for Stocks and Futures") — a mathematical method, not copyrightable. NO third-party
 code, variable names, or structure were copied; the "Au" filter pack was read ONLY to identify the variant.
 Canonical coefficients (Period P):
   a1 = exp(−1.414·π / P)
   b1 = 2·a1·cos(1.414·π / P)          // 1.414·180/P degrees expressed in radians
   c2 = b1 ; c3 = −a1² ; c1 = 1 − c2 − c3
   y  = c1·(x + x[1])/2 + c2·y[1] + c3·y[2]

 ASSUMPTIONS / NOTES:
   • The "Au" source exposed a 2-pole / 3-pole switch. This clean-room build implements the canonical
     2-POLE Super Smoother only (the specified variant); the 3-pole form is intentionally omitted.
   • cos() argument is in RADIANS: 1.414·180/P degrees == 1.414·π/P radians.
   • First two bars are seeded with the raw input (recursion needs y[1], y[2]).

 CHANGELOG
   v1.0.0 (2026-07-12) — clean-room 2-pole Super Smoother + Sentinel plumbing (naming law, glass card, label remover).
```

