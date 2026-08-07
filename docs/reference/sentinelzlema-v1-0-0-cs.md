# SentinelZLEMA_v1_0_0.cs

> `bin/Custom/Indicators/SentinelZLEMA_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 215 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelZLEMA_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel.Smoothers` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 Sentinel ZLEMA — Zero-Lag Exponential Moving Average (Sentinel smoother block)   |   Version v1.0.0
 File: SentinelZLEMA_v1_0_0.cs  |  namespace …Indicators.Sentinel.Smoothers  |  display "Sentinel ZLEMA"

 ⚠ NO ORDERS · NO STATE SEAM — a read-only SMOOTHER building block, not a Council voter. It draws a
 smoothed line + a Sentinel glass card; it publishes nothing (a moving average has no verdict). It is a
 baseline the signal tools (Sentinel SuperTrend) can consume, and a Sentinel-branded MA in its own right.

 PROVENANCE / LICENSE: CLEAN-ROOM. Written from the public Zero-Lag EMA formula (Ehlers/Way):
 de-lag the input by d = 2·price[0] − price[lag] (lag = (N−1)/2), then feed d into a standard
 EMA recurrence with α = 2/(N+1) — a mathematical method, not copyrightable. No third-party code,
 variable names, or structure were copied. (Sentinel port of the "Au" MA/filter pack; NOT copied.)

 CHANGELOG
   v1.0.0 (2026-07-12) — clean-room Zero-Lag EMA + Sentinel plumbing (naming law, glass card, label remover).
```

