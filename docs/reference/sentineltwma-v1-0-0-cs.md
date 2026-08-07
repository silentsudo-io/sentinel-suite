# SentinelTWMA_v1_0_0.cs

> `bin/Custom/Indicators/SentinelTWMA_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 238 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelTWMA_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel.Smoothers` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 Sentinel TWMA — Triple Weighted Moving Average (Sentinel smoother block)   |   Version v1.0.0
 File: SentinelTWMA_v1_0_0.cs  |  namespace …Indicators.Sentinel.Smoothers  |  display "Sentinel TWMA"

 ⚠ NO ORDERS · NO STATE SEAM — a read-only SMOOTHER building block, not a Council voter. It draws a
 smoothed line + a Sentinel glass card; it publishes nothing (a moving average has no verdict).

 PROVENANCE / LICENSE: CLEAN-ROOM. Written from the public triple-cascade lag-reduction formula (the
 TEMA construction applied to a Weighted MA) — a mathematical method, not copyrightable. No third-party
 code, names, or structure copied. (Sentinel port of the "Au" MA pack; the Au code was NOT copied.)

 ALGORITHM (Triple WMA — TEMA form over WMA, confirmed from source):
   w1 = WMA(price, Period)
   w2 = WMA(w1,    Period)
   w3 = WMA(w2,    Period)
   Value = 3·w1 − 3·w2 + w3
 WMA weights are linear (most-recent input weight = k, oldest = 1; denom = k(k+1)/2), over the available
 window k = min(CurrentBar+1, Period).

 NOTE: the port task labelled this "Triangular Weighted MA", but the source Description AND formula are
 the TRIPLE Weighted MA (3·w1 − 3·w2 + w3, cascaded WMA). This port implements the source's actual
 triple-WMA method (not a triangular kernel).

 CHANGELOG
   v1.0.0 (2026-07-12) — clean-room Triple Weighted MA (3·WMA − 3·WMA² + WMA³) + Sentinel plumbing
            (naming law, glass card, label remover). See NOTE re: "triangular" mislabel.
```

