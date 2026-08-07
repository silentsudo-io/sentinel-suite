---
layout: sentinel-ref
title: "SentinelDSMA_v1_0_0.cs"
blurb: "Indicators · 1.0.0 · 225 lines"
---

# SentinelDSMA_v1_0_0.cs

> `bin/Custom/Indicators/SentinelDSMA_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 225 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelDSMA_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel.Smoothers` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 Sentinel DSMA — de-lagged Double Simple Moving Average (smoother block)     |   Version v1.0.0
 File: SentinelDSMA_v1_0_0.cs  |  namespace …Indicators.Sentinel.Smoothers  |  display "Sentinel DSMA"

 ⚠ NO ORDERS · NO STATE SEAM — a read-only SMOOTHER building block, not a Council voter. It draws a
 smoothed line + a Sentinel glass card and publishes nothing.

 ⚠ PROVENANCE NOTE — IDENTITY OF THE SOURCE (load-bearing): the "AuDSMA" source is NOT the Ehlers
 Deviation-Scaled Moving Average (DSMA, 2018). Its own Description reads "DSMA (Double Simple Moving
 Average)" and its math is  y = 2·SMA(x,P) − SMA(SMA(x,P),P)  — a "twicing" / de-lagged double SMA
 (the SMA analogue of DEMA), which REMOVES lag rather than deviation-scaling the smoothing constant.
 This port reimplements THE METHOD THE SOURCE ACTUALLY IMPLEMENTS (the Double SMA), not the similarly
 named Ehlers DSMA. If the Ehlers Deviation-Scaled MA is wanted, that is a different (future) tool.

 PROVENANCE / LICENSE: CLEAN-ROOM. Reimplemented from the PUBLIC "twicing" de-lag formula
 (double_MA = 2·MA − MA∘MA) — a standard mathematical construction, not copyrightable. No third-party
 code, variable names, or structure were copied; the "Au" source was read ONLY to identify the method.

 MATH:  s1[t] = mean of last min(t+1,P) inputs
        Value = 2·s1[t] − (mean of last min(t+1,P) values of s1)

 ASSUMPTIONS:
   • Warm-up uses shrinking windows (min(CurrentBar+1, Period)) so the line is defined from bar 0.
   • Inner SMA series (s1) is kept in a private Series so the outer SMA smooths the actual running s1.

 CHANGELOG
   v1.0.0 (2026-07-12) — clean-room Double SMA (de-lag) + Sentinel plumbing (naming law, glass card, label remover).
```

