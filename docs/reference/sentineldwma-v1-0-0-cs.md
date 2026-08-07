# SentinelDWMA_v1_0_0.cs

> `bin/Custom/Indicators/SentinelDWMA_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 230 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelDWMA_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel.Smoothers` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 Sentinel DWMA — Double Weighted Moving Average (Sentinel smoother block)   |   Version v1.0.0
 File: SentinelDWMA_v1_0_0.cs  |  namespace …Indicators.Sentinel.Smoothers  |  display "Sentinel DWMA"

 ⚠ NO ORDERS · NO STATE SEAM — a read-only SMOOTHER building block, not a Council voter. It draws a
 smoothed line + a Sentinel glass card; it publishes nothing (a moving average has no verdict).

 PROVENANCE / LICENSE: CLEAN-ROOM. Written from the public double-cascade lag-reduction formula (the
 DEMA construction applied to a Weighted MA) — a mathematical method, not copyrightable. No third-party
 code, names, or structure copied. (Sentinel port of the "Au" MA pack; the Au code was NOT copied.)

 ALGORITHM (Double WMA — DEMA form over WMA, confirmed from source):
   w1 = WMA(price, Period)
   w2 = WMA(w1,    Period)
   Value = 2·w1 − w2
 WMA weights are linear (most-recent input weight = k, oldest = 1; denom = k(k+1)/2), over the available
 window k = min(CurrentBar+1, Period). An intermediate Series holds w1 so w2 = WMA(WMA(price)).

 CHANGELOG
   v1.0.0 (2026-07-12) — clean-room Double Weighted MA (2·WMA − WMA(WMA)) + Sentinel plumbing
            (naming law, glass card, label remover).
```

