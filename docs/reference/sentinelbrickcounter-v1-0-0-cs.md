# SentinelBrickCounter_v1_0_0.cs

> `bin/Custom/Indicators/SentinelBrickCounter_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 271 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelBrickCounter_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors` |
| **Consumes seams** | `BrickState` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
one at https://mozilla.org/MPL/2.0/.

Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
─────────────────────────────────────────────────────────────────────────────
═════════════════════════════════════════════════════════════════════════════
 SentinelBrickCounter — "ticks to the next brick" glass HUD (Sentinel Suite)
 File: SentinelBrickCounter_v1_0_0.cs   Class: SentinelBrickCounter_v1_0_0
─────────────────────────────────────────────────────────────────────────────
 WHAT THIS IS
   A generic on-chart countdown that reads SentinelCore.BrickState (v1.6.1) and
   shows how many ticks remain until the next brick prints. NOT tied to any one
   bars type — ANY Sentinel brick bars type that publishes BrickState feeds it
   (SentinelTBars, SentinelTbarsCount, …), even from a DIFFERENT chart of the same
   instrument. Supersedes TbarsCountRemainingCounter + its private feed.
   v1.19.0: BrickState is keyed by SCOPE (a chart = instrument × bartype), so this reads
   THIS chart's brick state first and falls back to a bare-instrument lookup — the fallback
   is what lets it sit on a minute chart and count a brick type running elsewhere. That
   lookup resolves only when exactly ONE brick scope exists for the instrument; with two it
   fails closed rather than showing an arbitrary chart's countdown.

 SENTINEL STYLE
   Drawn as a SentinelSkin.Painter GLASS CARD, auto-docked via SentinelSkin.CardLayout
   so it stacks with the other Sentinel cards (never overlaps). cyan = live/watching;
   the hero number + direction pill are green/red (direction). Name label hidden by
   default (label-remover standard). See Docs/SENTINEL_DESIGN_SYSTEM.md §4b.

 CHANGELOG
   v1.0.0 (2026-07-06) — first release; reads SentinelCore.BrickState. Glass-card
     readout (Painter + CardLayout) replacing the initial Draw.TextFixed corner text.
```

