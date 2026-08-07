---
layout: sentinel-ref
title: "ADXPro_v1_2_0.cs"
blurb: "Indicators · 1.2.0 · 692 lines"
---

# ADXPro_v1_2_0.cs

> `bin/Custom/Indicators/ADXPro_v1_2_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.2.0 |
| **Size** | 692 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `ADXPro_v1_2_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors` |
| **Publishes seams** | `AdxState` |
| **Documented by** | _no doc tracks this artifact_ |
| **Depends on this** | [Council_v1_11_0.cs](council-v1-11-0-cs.md), [SentinelCockpit_v0_1_0.cs](sentinelcockpit-v0-1-0-cs.md), [SentinelTrendStrategy_v1_0_0.cs](sentineltrendstrategy-v1-0-0-cs.md), [SentinelTrend_v1_0_0.cs](sentineltrend-v1-0-0-cs.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 ADXPro — ADX / DI bias indicator with a Sentinel flight-instrument card   |   Version v1.2.0
 File: ADXPro_v1_2_0.cs   |   namespace …Indicators.Sentinel

 ⚠ NO ORDERS — read-only bias/regime indicator. Safe to run anywhere.

 v1.2.0 completes the Sentinel retrofit that v1.1.0 only half-did:
   • CardLayout-DOCKED glass card (+ a CardCorner property) — v1.1.0 hardcoded the top-right rect, so
     it COVERED CompressionBase/Eye/SignalExcursionRecorder (all default there). Now it auto-stacks.
   • Richer card via the SentinelSkin.Painter vocabulary: an ADX GAUGE hero (0–50 dial), +DI / −DI
     dual magnitude tracks, an ADX SPARKLINE (trend building vs fading at a glance), and a revived
     Strong / Building / Weakening bias label (the slope nuance v1.1.0 computed then threw away).
   • PLOT COLORS → the Sentinel palette (dataviz language): ADX = cyan (strength/magnitude),
     +DI = green, −DI = red (money/direction), Trigger = mute, Strong = amber (advisory threshold),
     bull/bear background = green/red tint. No more Gold/DeepSkyBlue/MediumPurple/Teal/Purple.
   • PublishRegime → SentinelCore.SetAdxState(instrument, adx, +DI, −DI, bias, slope5, strong) so
     GTrader21 / Eye / Copier can consult "trend ON + bias agrees" (needs SentinelCore ≥ v1.2.0).
   • Dropped dead pre-card cruft: BiasTablePosition / TableFontSize / TableTextBrush props + the
     unused BiasText()/SlopeDirection()/TrendText() methods.

 NEW TYPE IDENTITY (namespace+class+Name) → re-add on charts; ADXPro_v1_1_0 stays a FROZEN fallback.
 See Docs/SENTINEL_DESIGN_SYSTEM.md §4b (CardLayout/Painter) + §1 (palette) + memory sentinel-namespace-and-naming.
 CHANGELOG
   v1.2.0b (in-place 2026-07-07) — SENTINEL PLOT SKIN: OnRender now paints a glass PanelWash (covers stock
            plots) + refined PER-BAR trend-regime bands (CUp/CDown, low alpha — supersedes the muddy
            BackBrushes, now skipped when the skin is on) + themed trigger/strong reference lines + glowing
            ADX/+DI/−DI lines (ADX cyan when strong). Toggle: SentinelPlotSkin (default ON); grid off. §4c.
   v1.2.0a (2026-07-07) — PublishRegime now DEFAULTS ON so ADXPro feeds the Council's AdxState voter out of
            the box. In-place patch (no rename); existing chart placements keep their serialized value.
   v1.2.0 — CardLayout dock + CardCorner; gauge/DI-tracks/sparkline card; Sentinel plot colors;
            SentinelCore ADX-regime publish; removed dead table props/methods. (Prior: ADXPro_v1_1_0.)
```

