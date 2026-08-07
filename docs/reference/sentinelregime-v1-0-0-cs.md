---
layout: sentinel-ref
title: "SentinelRegime_v1_0_0.cs"
blurb: "Indicators · 1.0.0 · 536 lines"
---

# SentinelRegime_v1_0_0.cs

> `bin/Custom/Indicators/SentinelRegime_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 536 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelRegime_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors` |
| **Publishes seams** | `RegimeState` |
| **Documented by** | _no doc tracks this artifact_ |
| **Depends on this** | [Council_v1_11_0.cs](council-v1-11-0-cs.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 Sentinel Regime — the VOLATILITY-REGIME modulator (CLEAN-ROOM)            |   Version v1.0.0
 File: SentinelRegime_v1_0_0.cs   |   namespace …Indicators.Sentinel.Sensors (Tier-③ SENSOR)   |   Name "Sentinel Regime"

 ⚠ NO ORDERS — read-only advisory indicator. Safe to run anywhere.

 CLEAN-ROOM ORIGINAL. Written from scratch off PUBLIC, non-copyrightable statistics — 1-D K-means
 clustering of rolling return-volatility into three regimes, and a first-order Markov forward filter
 over the cluster posterior. It uses NO third-party code. The installed MarkovRegimeSwitching.cs was
 surveyed as a design reference ONLY — none of its code was copied. See the provenance audit + NOTICE.

 WHY IT MATTERS — this is NOT a directional voter; it is a CONTEXT MODULATOR. It answers "what kind of
 market is this right now — calm, normal, or chaotic?" so the Council can DAMPEN conviction in a
 high-volatility (regime 2) tape and let orderly low/med-vol (regime 0/1) trends run.

 THE PUBLIC METHOD:
   • volatility     = stddev of the last VolWindow log-returns (r = ln(Close[0]/Close[1])).
   • sample buffer  = the last SampleWindow volatility values.
   • K-means (k=3)  = a few Lloyd iterations over that buffer, centers init at min/median/max; the 3
                      centers are then SORTED ASCENDING (0=low, 1=med, 2=high) — label-stabilization is
                      REQUIRED, else the cluster labels permute between recomputes. K-means is refit only
                      every RecomputeEvery bars for cost; the sorted centers are cached between refits.
   • transitions    = a 3×3 count of consecutive raw-regime labels across the buffer, Laplace-smoothed
                      (+1) and row-normalized → the Markov transition matrix T.
   • Markov filter  = belief b=[pLow,pMed,pHigh]; each bar predict b'=b·T, multiply by a Gaussian
                      emission likelihood of the current vol under each (center, spread), then normalize.
   • Regime         = argmax(b'); RegimeProb = max(b'); Trending = (Regime ≤ 1).

 THE SENTINEL PLUMBING (our own code — makes it a suite member):
   • PUBLISHES SentinelCore.RegimeState (Regime / RegimeProb / Low·Med·HighProb / Trending).
   • Consumed by the Council as a CONTEXT MODULATOR (not a directional voter → no hidden Signal plot).
   • CARD-ONLY readout: both plots are hidden (transparent). A 0..1 modulator plot cannot coexist on a chart
     panel shared with a big-range series (Flow's ±2000 CVD), so the glass card is the sole readout.
   • A SentinelSkin.Painter glass card + label remover + roster heartbeat.

 CHANGELOG
   v1.0.0 (2026-07-13b) — CARD-ONLY. The visible panel plots collided with Flow's CVD when the workspace put both
            on one shared panel (Regime 0..1 collapsed to a flat row). Both plots hidden (transparent); the card is
            the readout. Values[]/DataBox + RegimeState seam unchanged.
   v1.0.0 (2026-07-13a) — plot attempt: normalized Regime to 0/0.5/1 (regime/2) + Dot markers. Superseded same day
            once the live shared-panel collision with Flow made any visible 0..1 plot unviewable.
   v1.0.0 (2026-07-12) — NEW. Clean-room volatility-regime modulator (rolling-vol K-means + Markov
            forward filter). RegimeState publish, two visible plots, glass card, scope key + heartbeat.
```

