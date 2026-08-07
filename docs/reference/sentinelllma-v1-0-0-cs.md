# SentinelLLMA_v1_0_0.cs

> `bin/Custom/Indicators/SentinelLLMA_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 246 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelLLMA_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel.Smoothers` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 Sentinel LLMA — Low-Lag (Jurik-style) Moving Average (Sentinel smoother block) |  Version v1.0.0
 File: SentinelLLMA_v1_0_0.cs  |  namespace …Indicators.Sentinel.Smoothers  |  display "Sentinel LLMA"

 ⚠ NO ORDERS · NO STATE SEAM — a read-only SMOOTHER building block, not a Council voter. It draws a
 smoothed low-lag line + a Sentinel glass card; it publishes nothing (a moving average has no verdict).

 PROVENANCE / LICENSE: CLEAN-ROOM. The "AuLLMA" source ("Low Lag Moving Average", Length + Phase params)
 is a JURIK-JMA-style adaptive filter: a 3-stage EMA cascade (preliminary EMA → detrend EMA → phase-
 adjusted Kalman-style stage) whose smoothing factor is driven by a proprietary volatility-band estimator
 (a sorted trimmed-mean "MidAvg" window). That volatility-adaptive stage is non-standard / proprietary.
 This is a FRESH reimplementation from the CANONICAL public simplified JMA formula — no third-party code,
 names, or structure were copied.

 ASSUMPTION (noted per clean-room rule): I implemented the closest CANONICAL PUBLISHED form — the
 simplified Jurik JMA with a FIXED smoothing factor (dynamic-volatility power = 1). I deliberately OMIT
 the source's proprietary volatility-adaptive dynamic factor (its "MidAvg" trimmed-mean band + log/sqrt
 scaling). Consequently output tracks the standard published JMA, not the exact Au/MidAvg curve. Length +
 Phase are preserved with the standard JMA semantics: beta = 0.45·(Length−1) / (0.45·(Length−1) + 2);
 phaseRatio = Phase<−100 ? 0.5 : Phase>100 ? 2.5 : Phase/100 + 1.5.

 CHANGELOG
   v1.0.0 (2026-07-12) — clean-room simplified Jurik (low-lag) MA + Sentinel plumbing (naming law, glass
            card, label remover). Volatility-adaptive MidAvg stage omitted (see ASSUMPTION above).
```

