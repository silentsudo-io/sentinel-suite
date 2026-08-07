---
layout: sentinel-ref
title: "SentinelSWMA_v1_0_0.cs"
blurb: "Indicators · 1.0.0 · 248 lines"
---

# SentinelSWMA_v1_0_0.cs

> `bin/Custom/Indicators/SentinelSWMA_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 248 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelSWMA_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel.Smoothers` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 Sentinel SWMA — Sine-Weighted Moving Average (Sentinel smoother building block)   |   Version v1.0.0
 File: SentinelSWMA_v1_0_0.cs  |  namespace …Indicators.Sentinel.Smoothers  |  display "Sentinel SWMA"

 ⚠ NO ORDERS · NO STATE SEAM — a read-only SMOOTHER building block, not a Council voter. It draws the
 sine-weighted average + a Sentinel glass card and publishes nothing (a moving average has no verdict).

 PROVENANCE / LICENSE: CLEAN-ROOM. Algorithm identified from a GPL LizardIndicators source
 (amaSWMA.cs, GPL-3.0) but NO GPL code was used — reimplemented from the canonical PUBLIC Sine-Weighted
 Moving Average formula (a mathematical method, not copyrightable). No third-party code/variable-names/
 structure were copied.

   CANONICAL SWMA over a window of n = min(CurrentBar+1, Period) inputs:
       wᵢ    = sin( π · (i+1) / (n+1) )          for i = 0 … n-1   (i=0 = current bar)
       Value = Σ (wᵢ · Input[i]) / Σ wᵢ
   The sine weights are symmetric and peak at the middle of the window, so the SWMA is a smooth,
   low-noise average that de-emphasises the window edges.

 ASSUMPTIONS: (1) Weight indexing wᵢ = sin(π·(i+1)/(n+1)) with i=0 the most recent bar, matching the
 confirmed source indexing. (2) During warm-up (fewer than Period bars) the window shrinks to the bars
 available and the denominator (n+1) shrinks with it, so the average is always properly normalised.

 CHANGELOG
   v1.0.0 (2026-07-12) — initial: clean-room sine-weighted MA + Sentinel naming law, glass card, label remover.
```

