---
layout: sentinel-ref
title: "SentinelHoltEMA_v1_0_0.cs"
blurb: "Indicators · 1.0.0 · 242 lines"
---

# SentinelHoltEMA_v1_0_0.cs

> `bin/Custom/Indicators/SentinelHoltEMA_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 242 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelHoltEMA_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel.Smoothers` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 Sentinel HoltEMA — Holt double-exponential smoothing (Sentinel smoother block) |  Version v1.0.0
 File: SentinelHoltEMA_v1_0_0.cs  |  namespace …Indicators.Sentinel.Smoothers  |  display "Sentinel HoltEMA"

 ⚠ NO ORDERS · NO STATE SEAM — a read-only SMOOTHER building block, not a Council voter. It draws a
 smoothed line + a Sentinel glass card; it publishes nothing (a moving average has no verdict).

 PROVENANCE / LICENSE: CLEAN-ROOM. Written from the public Holt linear (double-exponential) smoothing
 formula — a textbook forecasting method, not copyrightable. No third-party code, names, or structure
 copied. (Sentinel smoother-library port of the "Au" MA pack; the Au code was NOT copied. See repo NOTICE.)

 ALGORITHM (Holt, two smoothing constants derived from periods):
   alpha = 2 / (1 + Period)          (level smoothing)
   gamma = 2 / (1 + TrendPeriod)     (trend smoothing)
   L[t]  = alpha·x[t] + (1 − alpha)·(L[t−1] + T[t−1])     (level)
   T[t]  = gamma·(L[t] − L[t−1]) + (1 − gamma)·T[t−1]     (trend)
   Value = L[t]

 ASSUMPTION (noted per clean-room rule): the "AuHoltEMA" source PLOTS THE LEVEL L (its HoltEMA[0] = L),
 NOT the one-step-ahead Holt forecast L+T. This port MATCHES THE SOURCE and outputs the level L (the
 trend component T is still computed and used inside the recursion). The classic Holt forecast would be
 L+T; use that if a forecast line is wanted instead of the smoothed level.

 CHANGELOG
   v1.0.0 (2026-07-12) — clean-room Holt double-exponential smoothing + Sentinel plumbing (naming law,
            glass card, label remover). Output = level L to match source (see ASSUMPTION above).
```

