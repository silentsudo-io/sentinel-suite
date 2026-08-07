---
layout: sentinel-ref
title: "SentinelTMA_v1_0_0.cs"
blurb: "Indicators · 1.0.0 · 231 lines"
---

# SentinelTMA_v1_0_0.cs

> `bin/Custom/Indicators/SentinelTMA_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 231 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelTMA_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel.Smoothers` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 Sentinel TMA — Triangular Moving Average (Sentinel smoother building block)   |   Version v1.0.0
 File: SentinelTMA_v1_0_0.cs  |  namespace …Indicators.Sentinel.Smoothers  |  display "Sentinel TMA"

 ⚠ NO ORDERS · NO STATE SEAM — a read-only SMOOTHER building block, not a Council voter. It draws a
 smoothed line + a Sentinel glass card; it publishes nothing (a moving average has no verdict). It is a
 baseline the signal tools (Sentinel SuperTrend) can consume, and a Sentinel-branded MA in its own right.

 PROVENANCE / LICENSE: CLEAN-ROOM. Written from the public Triangular Moving Average formula
 (a double-smoothed SMA that weights the middle of the window most): TMA = SMA( SMA(price, p1), p2 ).
 Window split — even N: p1 = N/2, p2 = N/2 + 1; odd N: p1 = p2 = (N+1)/2. A mathematical method,
 not copyrightable. No third-party code, variable names, or structure were copied. (Sentinel port of
 the "Au" MA/filter pack; NOT copied. See repo NOTICE.)

 CHANGELOG
   v1.0.0 (2026-07-12) — clean-room Triangular Moving Average + Sentinel plumbing (naming law, glass card, label remover).
```

