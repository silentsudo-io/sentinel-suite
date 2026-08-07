---
layout: sentinel-ref
title: "SentinelTrend_v1_0_0.cs"
blurb: "Indicators · 1.0.0 · 493 lines"
---

# SentinelTrend_v1_0_0.cs

> `bin/Custom/Indicators/SentinelTrend_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 493 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelTrend_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors` |
| **Publishes seams** | `TrendState` |
| **Consumes seams** | `AdxState` |
| **Documented by** | _no doc tracks this artifact_ |
| **Depends on this** | [Council_v1_11_0.cs](council-v1-11-0-cs.md), [SentinelCockpit_v0_1_0.cs](sentinelcockpit-v0-1-0-cs.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 SentinelTrend — the corrected, unified ATR/CCI trailing-line indicator   |   Version v1.0.0
 File: SentinelTrend_v1_0_0.cs   |   namespace …Indicators.Sentinel

 ⚠ NO ORDERS — read-only trend/direction indicator. Safe to run anywhere.

 WHAT THIS IS — the definitive replacement for the old TrendMagic family (TrendMagic /
 TrendMagicOscillator / TrendMagicSignalMod + the TMEntry/TMEntry50/TMXEntryExit/TMSquared/
 TripleTM strategies). Same idea — an ATR band trailing line whose side is chosen by CCI —
 but it FIXES the four flaws that made the originals whipsaw, and homes into the Sentinel suite.

 WHY IT IS SUPERIOR (vs the original TrendMagic algorithm):
   1. TRUE ATR.  The originals call ATR(Close, n) — feeding the CLOSE SERIES into ATR, which
      collapses it to smoothed close-to-close change and IGNORES the high-low span + gaps, so it
      SYSTEMATICALLY UNDERSTATES volatility. This uses ATR(n) on the bar — real True Range.
   2. CCI HYSTERESIS.  The originals flip the trend on a naked CCI zero-cross (cci >= 0) — laggy and
      noisy, so in chop the line teleports between the up-floor and down-ceiling every few bars. This
      uses a DEADBAND: flip up only when CCI > +CciThreshold, down only when CCI < -CciThreshold,
      otherwise HOLD. That single change kills most of the whipsaw.
   3. DOT RENDER.  On a regime flip the line jumps discontinuously; drawn as PlotStyle.Line the
      originals streak a vertical connector across the jump (see memory ninjascript-plot-config-override).
      This renders the trailing line as Dots.
   4. SANE DEFAULTS.  The core TrendMagic default AtrMult = 0.01 makes the band ~1% of an already-
      understated ATR, so the line hugs price and flips constantly. Default here = 1.5 (a real band).

 SENTINEL WIRING (Docs/SENTINEL_DESIGN_SYSTEM.md):
   • Direction (+1/-1/0) + Trend plots for strategy consumption (SentinelTrendStrategy consumes them,
     exactly as the old strategies consumed TrendMagicSignalMod.Direction).
   • CONSULT: optional ADX-regime filter — reads SentinelCore.GetAdxState so signal markers can require
     "trend ON + bias agrees" (needs ADXPro publishing; SentinelCore ≥ v1.2.0). Fail-open when absent.
   • PUBLISH: optional SentinelCore.SetTrendState(...) so GTrader21 / Eye / strategies can consult this
     trend's direction + line + distance (needs SentinelCore ≥ v1.3.0).
   • A SentinelSkin.Painter glass card (CardLayout-docked) + Sentinel palette + label remover.

 CHANGELOG
   v1.0.0b (2026-07-07) — PublishState now DEFAULTS ON so SentinelTrend feeds the Council's TrendState voter
            out of the box. In-place patch (no rename); existing chart placements keep their serialized value.
   v1.0.0a (2026-07-06) — default CardCorner TopLeft → TopRight (Sentinel house default: cards dock right).
            In-place patch (no rename). Existing placements keep their serialized corner.
   v1.0.0 — initial: true ATR, CCI hysteresis deadband, Dot render, ADX consult + TrendState publish,
            Sentinel card/palette/label-remover. Supersedes the TrendMagic family (kept as fallbacks).
```

