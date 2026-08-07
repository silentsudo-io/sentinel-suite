# SentinelSMA_v1_0_0.cs

> `bin/Custom/Indicators/SentinelSMA_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 205 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelSMA_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel.Smoothers` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 Sentinel SMA — Simple Moving Average (Sentinel smoother building block)   |   Version v1.0.0
 File: SentinelSMA_v1_0_0.cs  |  namespace …Indicators.Sentinel.Smoothers  |  display "Sentinel SMA"

 ⚠ NO ORDERS · NO STATE SEAM — a read-only SMOOTHER building block, not a Council voter. It draws a
 smoothed line + a Sentinel glass card; it publishes nothing (a moving average has no verdict). It is a
 baseline the signal tools (Sentinel SuperTrend) can consume, and a Sentinel-branded MA in its own right.

 PROVENANCE / LICENSE: CLEAN-ROOM. Written from the public Simple Moving Average formula
 (arithmetic mean of the last N inputs) — a mathematical method, not copyrightable. No third-party code.
 (Sentinel smoother-library port of the "Au" MA/filter pack; the Au code was NOT copied. See repo NOTICE.)

 CHANGELOG
   v1.0.0 (2026-07-12) — initial: clean-room SMA + Sentinel naming law, glass card, label remover.
            First member + golden template of the Sentinel smoother library (…Indicators.Sentinel.Smoothers).
```

