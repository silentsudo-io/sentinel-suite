---
layout: sentinel-ref
title: "SentinelTBars_v1_0_0.cs"
blurb: "Bar types · 1.0.0 · 781 lines"
---

# SentinelTBars_v1_0_0.cs

> `bin/Custom/BarsTypes/SentinelTBars_v1_0_0.cs`

| | |
|---|---|
| **Family** | Bar types |
| **Version** | 1.0.0 |
| **Size** | 781 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelTBars_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.BarsTypes` |
| **Publishes seams** | `BrickState` |
| **Documented by** | [SENTINEL_BARTYPE_GRID](../../SENTINEL_BARTYPE_GRID.md) |
| **Depends on this** | [Council_v1_11_0.cs](council-v1-11-0-cs.md), [SentinelBrickCounter_v1_0_0.cs](sentinelbrickcounter-v1-0-0-cs.md), [SentinelCandidateRecorder_v1_0_0.cs](sentinelcandidaterecorder-v1-0-0-cs.md), [SentinelCockpit_v0_1_0.cs](sentinelcockpit-v0-1-0-cs.md), [SentinelExcursionRecorder_v2_0_0.cs](sentinelexcursionrecorder-v2-0-0-cs.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
one at https://mozilla.org/MPL/2.0/.

Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
─────────────────────────────────────────────────────────────────────────────
═════════════════════════════════════════════════════════════════════════════
 SentinelTBars — adaptive HA / Renko-hybrid "T-brick" BARS TYPE (Sentinel Suite)
 File: SentinelTBars_v1_0_0.cs        Class/Type: SentinelTBars_v1_0_0
 Display Name: "SentinelTBars v1.0.0"  ·  BarsPeriodType id: 212201 (reserved Sentinel bars block 212200–212299)
─────────────────────────────────────────────────────────────────────────────
 WHAT THIS IS
   The Sentinel-graded successor to the TbarsSudo family (V0001..V0003, frozen).
   It builds Renko-style bricks with Heikin-Ashi-smoothed BODIES and REAL price
   wicks, wrapped in adaptive machinery:
     • ATR-adaptive brick floor  — bricks never shrink below AtrMult × ATR, so
       size tracks volatility (the core you liked, now made correct + stable).
     • Breakout confirmation      — a probe must survive time + penetration +
       speed + wick-giveback (+ optional volume) before it prints a brick.
     • Trend hysteresis           — after N same-way bricks the reversal
       threshold widens (fewer whipsaws in a run).
     • Density normalisation      — a per-BRICK controller nudges brick size
       toward a target bars-per-session.
     • Quiet-hours gating, forced time-bricks, micro-splits.
   It PUBLISHES its adaptive read to SentinelCore.BrickState (v1.6.0 seam) so the
   rest of the suite (GTrader21 / Eye / strategies) can consult the live brick
   ATR + direction without re-deriving it.

 ⚠⚠ READ THIS BEFORE YOU TRADE OFF THE CANDLES — **CANDLE COLOUR IS NOT BRICK
     DIRECTION.** Bodies are HEIKIN-ASHI (open = (prevHaOpen+prevHaClose)/2,
     close = (O+H+L+C)/4), so a body is coloured by the SMOOTHED average, not by
     the brick that actually printed. Near a turn they routinely disagree: the
     brick flips while the HA body is still the old colour, or the body flips a
     brick early. This is inherent to HA rendering and is NOT a bug.
       • The authoritative direction is `SentinelCore.BrickState.Direction`
         (what every Sentinel consumer votes on), never the pixel.
       • The HIGH/LOW wicks are REAL traded prices; only the BODY is synthetic.
       • ⚠ Corollary for anything that RECORDS: an HA close is a price that never
         traded (see the firePx incident — a synthetic close biased every label in
         the corpus). Record real prints, not the body.
     Reported by a public tester (sneaky_zekey), who had to write this warning into
     his own tool's setup output because ours did not carry it. Applies equally to
     SentinelDrift, which inherits the same HA rendering; SentinelLattice and
     SentinelEffort do not use HA bodies and are unaffected.

 RELATION TO TbarsSudoV0003 (frozen checkpoint — NOT edited)
   Same feature set, but reworked for CORRECTNESS + REPRODUCIBILITY. Fixes applied
   (each was a defect analysed in V0003):
     1. DETERMINISM — V0003 re-polled TbarsSudoV3Registry every ~800ms of data
        time and mutated brick geometry MID-STREAM, so bars repainted as ticks
        arrived and a reload produced different bars. Here config is LATCHED ONCE
        per session (first bar + each new session) and frozen for that session.
        To apply new controller settings, reload the chart — the registry is a
        session-static store, so values the controller published survive the
        reload and are baked in at build time (conventional NT bar-param UX).
     2. BuiltFrom = Tick — the ms / ticks-per-second confirmation gates need true
        tick timestamps; V0003 left BuiltFrom = 0 (every other renko type uses Tick).
     3. DENSITY CONTROLLER — V0003 nudged the scale on EVERY TICK, compounding to
        the Min/Max rails within a few dozen ticks. Here it is a per-BRICK
        proportional controller with a deadband + per-brick step cap, so it settles.
     4. ATR — V0003 re-updated the EMA on every tick with the still-growing bar
        range (over-weighting) and used the bar's OWN close as the TR "prev close".
        Here ATR updates ONCE PER CLOSED BRICK with a correct true range (previous
        brick's close), so the volatility floor is stable and meaningful.
     5. CONFIRMATION CHAINING — V0003's confirmation path emitted at most one brick
        per tick, needing a fresh confirmation wait per brick → bricks lagged price
        on gaps. Here a confirmed breakout CHAINS the remaining full-brick distance
        in the same tick.
     6. GetPercentComplete — V0003 mixed barOpen and the breakout price; here a
        single consistent brick basis is used.
   The controller/registry (TbarsSudoV3Controller / TbarsSudoV3Config /
   TbarsSudoV3Registry) are REUSED unchanged as the optional live-tuning surface.

 CHANGELOG
   v1.0.0 (2026-07-06) — first Sentinel-graded release; supersedes TbarsSudoV0003.
                         Determinism latch, BuiltFrom=Tick, per-brick density,
                         per-brick ATR, confirmation chaining, consistent %-complete,
                         + SentinelCore.BrickState publish seam.
   v1.0.0 (in-session)  — per-tick BrickState publish (live countdown fields) + per-brick BrickLog JSONL;
                         BarsPeriodType 212124 → 212201 (RESERVED Sentinel bars block 212200–212299).
```

