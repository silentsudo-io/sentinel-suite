---
layout: sentinel-ref
title: "SentinelHMA_v1_0_0.cs"
blurb: "Indicators · 1.0.0 · 227 lines"
---

# SentinelHMA_v1_0_0.cs

> `bin/Custom/Indicators/SentinelHMA_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 227 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelHMA_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel.Smoothers` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 Sentinel HMA — Hull Moving Average (Sentinel smoother building block)   |   Version v1.0.0
 File: SentinelHMA_v1_0_0.cs  |  namespace …Indicators.Sentinel.Smoothers  |  display "Sentinel HMA"

 ⚠ NO ORDERS · NO STATE SEAM — a read-only SMOOTHER building block, not a Council voter. It draws a
 smoothed line + a Sentinel glass card; it publishes nothing (a moving average has no verdict). It is a
 baseline the signal tools (Sentinel SuperTrend) can consume, and a Sentinel-branded MA in its own right.

 PROVENANCE / LICENSE: CLEAN-ROOM. Written from the public Hull Moving Average formula
 (Alan Hull, 2005) — HMA = WMA( 2·WMA(price, ⌊N/2⌋) − WMA(price, N), round(√N) ) — a mathematical
 method, not copyrightable. No third-party code, variable names, or structure were copied.
 (Sentinel smoother-library port of the "Au" MA/filter pack; the Au code was NOT copied. See repo NOTICE.)

 CHANGELOG
   v1.0.0 (2026-07-12) — clean-room Hull Moving Average + Sentinel plumbing (naming law, glass card, label remover).
```

