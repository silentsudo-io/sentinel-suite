---
layout: sentinel-ref
title: "Location_v1_0_0.cs"
blurb: "Indicators · 1.0.0 · 368 lines"
---

# Location_v1_0_0.cs

> `bin/Custom/Indicators/Location_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 368 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `Location_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel` |
| **Publishes seams** | `LevelState` |
| **Documented by** | _no doc tracks this artifact_ |
| **Depends on this** | [Council_v1_11_0.cs](council-v1-11-0-cs.md), [SentinelCockpit_v0_1_0.cs](sentinelcockpit-v0-1-0-cs.md), [SentinelGodReversal_v1_0_0.cs](sentinelgodreversal-v1-0-0-cs.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 Location — the Sentinel STRUCTURAL-LEVELS axis ("where are we?")         |   Version v1.0.0
 File: Location_v1_0_0.cs   |   namespace …Indicators.Sentinel   |   display Name "Location"

 ⚠ NO ORDERS — read-only advisory indicator. Safe to run anywhere.

 WHAT THIS IS — the THIRD orthogonal axis feeding the Council (Docs/ROADMAP.md · memory
 signal-axes-plan). Every other sensor answers "what is price doing"; NONE answered "WHERE is it doing
 it." A breakout into prior-day-high / the session VWAP is a different trade than one in open air.
 Location computes the key structural levels and publishes them + the NEAREST level (ATR-normalized
 distance) so the Council can damp a signal that would run straight into a wall of memory.

 THE STATE (SentinelCore.LevelState, SentinelCore ≥ v1.10.0):
   VWAP + volume-weighted std bands · prior-day H/L · opening range · initial balance · session H/L ·
   NearestPrice/NearestName/NearestDistTicks(signed)/NearestDistAtr · VwapSide.
   (Volume-profile POC/VAH/VAL is a future v1.1 add — it needs a volume-by-price histogram.)

 SENTINEL WIRING (Docs/SENTINEL_DESIGN_SYSTEM.md §6c/§6d):
   • PUBLISH: SetLevelState(...) each update (default ON). No plots (context consumed via the seam).
   • Prior-day H/L from an added daily series; VWAP/OR/IB/session H-L from the primary series with a
     SessionIterator/IsFirstBarOfSession reset. VWAP includes the live forming bar (volume-weighted std).
   • A SentinelSkin.Painter glass card + Sentinel palette + label remover.

 CHANGELOG
   v1.0.0 (2026-07-07) — initial: VWAP+bands / PDH-PDL / OR / IB / session H-L + nearest-level
            (ATR-normalized), published as SentinelCore.LevelState; Sentinel card. Third Council axis.
```

