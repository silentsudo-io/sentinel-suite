---
layout: sentinel-ref
title: "SentinelTSMA_v1_0_0.cs"
blurb: "Indicators · 1.0.0 · 227 lines"
---

# SentinelTSMA_v1_0_0.cs

> `bin/Custom/Indicators/SentinelTSMA_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 227 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelTSMA_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel.Smoothers` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 Sentinel TSMA — Triple Simple Moving Average (Sentinel smoother block)     |   Version v1.0.0
 File: SentinelTSMA_v1_0_0.cs  |  namespace …Indicators.Sentinel.Smoothers  |  display "Sentinel TSMA"

 ⚠ NO ORDERS · NO STATE SEAM — a read-only SMOOTHER building block, not a Council voter. It draws a
 smoothed line + a Sentinel glass card; it publishes nothing (a moving average has no verdict).

 PROVENANCE / LICENSE: CLEAN-ROOM. Written from the public triple-cascade lag-reduction formula (the
 TEMA construction applied to a Simple MA) — a mathematical method, not copyrightable. No third-party
 code, names, or structure copied. (Sentinel port of the "Au" MA pack; the Au code was NOT copied.)

 ALGORITHM (Triple SMA — TEMA form over SMA, confirmed from source):
   s1 = SMA(price, Period)
   s2 = SMA(s1,    Period)
   s3 = SMA(s2,    Period)
   Value = 3·s1 − 3·s2 + s3
 Each SMA = arithmetic mean over the available window k = min(CurrentBar+1, Period).

 CHANGELOG
   v1.0.0 (2026-07-12) — clean-room Triple Simple MA (3·SMA − 3·SMA² + SMA³) + Sentinel plumbing
            (naming law, glass card, label remover).
```

