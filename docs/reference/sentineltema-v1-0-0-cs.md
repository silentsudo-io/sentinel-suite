# SentinelTEMA_v1_0_0.cs

> `bin/Custom/Indicators/SentinelTEMA_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 218 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelTEMA_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel.Smoothers` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 Sentinel TEMA — Triple Exponential Moving Average (Sentinel smoother building block)   |   Version v1.0.0
 File: SentinelTEMA_v1_0_0.cs  |  namespace …Indicators.Sentinel.Smoothers  |  display "Sentinel TEMA"

 ⚠ NO ORDERS · NO STATE SEAM — a read-only SMOOTHER building block, not a Council voter. It draws a
 smoothed line + a Sentinel glass card; it publishes nothing (a moving average has no verdict). It is a
 baseline the signal tools can consume, and a Sentinel-branded MA in its own right.

 PROVENANCE / LICENSE: CLEAN-ROOM. Written from the public Triple Exponential Moving Average formula
 (Mulloy: TEMA = 3·e1 − 3·e2 + e3, cascaded EMAs) — a mathematical method, not copyrightable. No
 third-party code. (Sentinel smoother-library port of the "Au" MA pack — the Au code was NOT copied;
 the source was read only to identify the algorithm. See repo NOTICE.)

 CHANGELOG
   v1.0.0 (2026-07-12) — clean-room TEMA (3·e1 − 3·e2 + e3) + Sentinel naming/card/label-remover.
            Clean-room from the public triple-exponential-moving-average formula — no third-party code;
            Sentinel port of the "Au" MA pack.
```

