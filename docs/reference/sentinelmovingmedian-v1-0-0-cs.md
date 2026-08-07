---
layout: sentinel-ref
title: "SentinelMovingMedian_v1_0_0.cs"
blurb: "Indicators · 1.0.0 · 216 lines"
---

# SentinelMovingMedian_v1_0_0.cs

> `bin/Custom/Indicators/SentinelMovingMedian_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 216 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelMovingMedian_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel.Smoothers` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 Sentinel MovingMedian — rolling median (Sentinel smoother building block)   |   Version v1.0.0
 File: SentinelMovingMedian_v1_0_0.cs  |  namespace …Indicators.Sentinel.Smoothers  |  display "Sentinel MovingMedian"

 ⚠ NO ORDERS · NO STATE SEAM — a read-only SMOOTHER building block, not a Council voter. It draws the
 rolling median of the last N inputs (an outlier-robust central-tendency line) + a Sentinel glass card;
 it publishes nothing. A building block the signal tools can consume; a Sentinel-branded filter in its own right.

 PROVENANCE / LICENSE: CLEAN-ROOM. Written from the public definition of the median (the middle order
 statistic of a sorted window; the mean of the two middles when the count is even) applied over a rolling
 window of the last min(CurrentBar+1, N) inputs. A mathematical method, not copyrightable. No third-party
 code, variable names, or structure were copied. (Sentinel port of the "Au" MA/filter pack; NOT copied.)

 CHANGELOG
   v1.0.0 (2026-07-12) — clean-room rolling median + Sentinel plumbing (naming law, glass card, label remover).
```

