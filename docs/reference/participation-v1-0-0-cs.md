# Participation_v1_0_0.cs

> `bin/Custom/Indicators/Participation_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 332 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `Participation_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel` |
| **Publishes seams** | `ParticipationState` |
| **Documented by** | _no doc tracks this artifact_ |
| **Depends on this** | [Council_v1_11_0.cs](council-v1-11-0-cs.md), [SentinelCandidateRecorder_v1_0_0.cs](sentinelcandidaterecorder-v1-0-0-cs.md), [SentinelCockpit_v0_1_0.cs](sentinelcockpit-v0-1-0-cs.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 Participation — the Sentinel RELATIVE-VOLUME modulator                   |   Version v1.0.0
 File: Participation_v1_0_0.cs   |   namespace …Indicators.Sentinel   |   display Name "Participation"

 ⚠ NO ORDERS — read-only advisory indicator. Safe to run anywhere.

 WHAT THIS IS — the SECOND ORTHOGONAL axis feeding the Council (Docs/ROADMAP.md · memory
 signal-axes-plan). The Council's price-derived voters (Trend/ADX/CCI/Envelope/Brick) all echo the same
 OHLC; VOLUME carries information price alone does not — "is this move BACKED by participation, or is it
 drifting on air?" Participation publishes relative volume so the Council can MODULATE: a move on light
 volume gets its conviction damped; it can only PENALISE an unbacked move, never inflate a backed one.

 THE STATE (SentinelCore.ParticipationState, SentinelCore ≥ v1.9.0):
   • Rvol        relative volume vs a typical (1.0 = normal, >1 heavy, <1 light)
   • VolZ        volume z-score vs the recent distribution
   • Climax      VolZ ≥ ClimaxZ (blow-off participation)
   • DryUp       Rvol ≤ DryUpRvol (participation vacuum)
   • TypicalVol  the typical volume used (diagnostic)

 RVOL NORMALIZATION (default = BAR-normalized, universal):
   Rvol = last COMPLETED bar volume ÷ SMA(Volume, VolStatPeriod). This works on ANY bar type — critical
   because the suite runs on tick/renko/brick bars where clock-time buckets are meaningless (every bar a
   different timestamp). OPTIONAL: UseTimeOfDayRvol normalizes against the typical volume at the SAME
   minute-of-day over prior bars — the cleaner "orthogonal" RVOL, but only sensible on TIME-based charts.
   Computed on the just-CLOSED bar (barsAgo=1) for stability, republished each tick to stay fresh.

 SENTINEL WIRING (Docs/SENTINEL_DESIGN_SYSTEM.md §6c/§6d):
   • PUBLISH: SetParticipationState(...) each update (default ON). No plots (a modulator).
   • A SentinelSkin.Painter glass card + Sentinel palette + label remover.

 CHANGELOG
   v1.0.0 (2026-07-07) — initial: bar-normalized RVOL (+ optional time-of-day) + volume z-score +
            climax/dry-up, published as SentinelCore.ParticipationState; Sentinel card/palette/label-
            remover. Second orthogonal Council axis. (Cumulative-delta divergence = a future v1.1 add.)
```

