---
layout: sentinel-ref
title: "SentinelZScore_v1_0_0.cs"
blurb: "Indicators · 1.0.0 · 346 lines"
---

# SentinelZScore_v1_0_0.cs

> `bin/Custom/Indicators/SentinelZScore_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 346 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelZScore_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors` |
| **Publishes seams** | `ZScoreState` |
| **Documented by** | _no doc tracks this artifact_ |
| **Depends on this** | [Council_v1_11_0.cs](council-v1-11-0-cs.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 Sentinel Z-Score — statistical standard score as a MEAN-REVERSION trigger    |   Version v1.0.0
 File: SentinelZScore_v1_0_0.cs   |   namespace …Indicators.Sentinel.Sensors   |   display Name "Sentinel Z-Score"

 ⚠ NO ORDERS — read-only advisory indicator. Safe to run anywhere.

 CLEAN-ROOM ORIGINAL. Written from scratch off the PUBLIC statistical z-score (the "standard score") — a
 textbook, non-copyrightable formula: how many standard deviations a value sits from its rolling mean. It
 uses NO third-party code; it computes off NinjaTrader's own SMA / StdDev. The installed amaZScore.cs
 (LizardIndicators, GPL) in the tree was surveyed as a design reference only — none of its code was copied.
 See the provenance audit + NOTICE.

 WHY IT MATTERS — this is a MEAN-REVERSION voter, orthogonal to the suite's trend/momentum sensors. When
 price stretches far from its own rolling mean (a large |z|), the statistical expectation is a snap BACK
 toward the mean, not a continuation. So Z-Score CONTRADICTS the trend axes at extremes — exactly the
 counter-weight the Council needs to avoid chasing an overextended move.

 THE PUBLIC FORMULA:
   • z = ( Price − SMA(Period) ) / StdDev(Period),  Price = the selected Input (Close by default).
   • guard StdDev > 0 (flat series ⇒ z = 0, no signal).
   • MEAN-REVERSION trigger:  z ≥ +Band  → price stretched HIGH → expect reversion DOWN → Signal = −1.
                              z ≤ −Band  → price stretched LOW  → expect reversion UP   → Signal = +1.
                              otherwise (|z| < Band)            →                         Signal =  0.
   • Extreme = |z| ≥ Band. (Signal is simply the mean-reversion sign while beyond the band, else 0.)

 THE SENTINEL PLUMBING (our own code — makes it a suite member):
   • PUBLISHES SentinelCore.ZScoreState (Z / Signal / Extreme).
   • Hidden ±1 "Signal" PLOT (Values[1], transparent) for the Deck SIGNAL ARM / generic consumers.
   • A SentinelSkin.Painter glass card + label remover + roster heartbeat + scope key.

 CHANGELOG
   v1.0.0 (2026-07-12) — NEW. Clean-room statistical z-score (standard score) as a mean-reversion TRIGGER
            voter. ZScoreState publish, visible ZScore line + hidden ±1 Signal plot, ± band + zero
            reference lines, glass card, scope key + heartbeat, label remover.
```

