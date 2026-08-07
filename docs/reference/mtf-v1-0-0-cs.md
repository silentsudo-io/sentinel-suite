---
layout: sentinel-ref
title: "Mtf_v1_0_0.cs"
blurb: "Indicators · 1.0.0 · 378 lines"
---

# Mtf_v1_0_0.cs

> `bin/Custom/Indicators/Mtf_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 378 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `Mtf_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel` |
| **Publishes seams** | `MtfState` |
| **Documented by** | _no doc tracks this artifact_ |
| **Depends on this** | [Council_v1_11_0.cs](council-v1-11-0-cs.md), [SentinelCandidateRecorder_v1_0_0.cs](sentinelcandidaterecorder-v1-0-0-cs.md), [SentinelCockpit_v0_1_0.cs](sentinelcockpit-v0-1-0-cs.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 MTF — the Sentinel MULTI-TIMEFRAME ALIGNMENT axis                        |   Version v1.0.0
 File: Mtf_v1_0_0.cs   |   namespace …Indicators.Sentinel   |   display Name "MTF"

 ⚠ NO ORDERS — read-only advisory indicator. Safe to run anywhere.

 WHAT THIS IS — the FOURTH orthogonal axis feeding the Council (Docs/ROADMAP.md · memory
 signal-axes-plan). Is the entry-timeframe signal WITH or AGAINST the higher timeframes? MTF alignment
 is one of the most reliable conviction multipliers, and it's cheap: add the ladder as data series,
 read a trend direction on each, and publish the consensus so the Council can PENALISE a trade taken
 against the higher-timeframe tide.

 THE STATE (SentinelCore.MtfState, SentinelCore ≥ v1.10.0):
   Bias (consensus -1/0/1) · AlignmentScore (-1..1 weighted net; higher TFs weighted more) ·
   AlignedCount / TfCount · AllAgree · Dirs (compact per-TF summary, e.g. "5:+ 15:+ 60:- 240:+").

 TREND PER TF — anchored to the suite's CANONICAL trend definition: MTF HOSTS SentinelTrend on each
 ladder series (SentinelTrend_v1_0_0(BarsArray[i], …) with card/publish/signals OFF) and reads its
 Direction, so a TF's "trend" means exactly what SentinelTrend means everywhere else (true ATR + CCI
 hysteresis trailing line) — MTF and TrendState can never disagree by construction.

 SENTINEL WIRING (Docs/SENTINEL_DESIGN_SYSTEM.md §6c/§6d):
   • PUBLISH: SetMtfState(...) each update (default ON). No plots (consumed via the seam).
   • Ladder = up to 5 minute timeframes (0 disables a slot); each AddDataSeries'd in Configure.
   • A SentinelSkin.Painter glass card + Sentinel palette + label remover.

 CHANGELOG
   v1.0.0 (2026-07-07) — initial: per-TF trend over a 1/5/15/60/240 ladder → weighted consensus, published
            as SentinelCore.MtfState; Sentinel card. Fourth Council axis.
            + (same day) TREND now ANCHORED to SentinelTrend — hosts SentinelTrend_v1_0_0 on each ladder
              series (card/publish/signals off) and reads its Direction, replacing the initial EMA-cross
              proxy. New params CciPeriod/AtrPeriod/AtrMult/CciThreshold; EmaFast/EmaSlow removed.
```

