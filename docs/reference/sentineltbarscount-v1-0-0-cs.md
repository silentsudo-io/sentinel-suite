# SentinelTbarsCount_v1_0_0.cs

> `bin/Custom/BarsTypes/SentinelTbarsCount_v1_0_0.cs`

| | |
|---|---|
| **Family** | Bar types |
| **Version** | 1.0.0 |
| **Size** | 350 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelTbarsCount_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.BarsTypes` |
| **Publishes seams** | `BrickState` |
| **Documented by** | _no doc tracks this artifact_ |
| **Depends on this** | [Council_v1_11_0.cs](council-v1-11-0-cs.md), [SentinelBrickCounter_v1_0_0.cs](sentinelbrickcounter-v1-0-0-cs.md), [SentinelCandidateRecorder_v1_0_0.cs](sentinelcandidaterecorder-v1-0-0-cs.md), [SentinelCockpit_v0_1_0.cs](sentinelcockpit-v0-1-0-cs.md), [SentinelExcursionRecorder_v2_0_0.cs](sentinelexcursionrecorder-v2-0-0-cs.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
one at https://mozilla.org/MPL/2.0/.

Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
─────────────────────────────────────────────────────────────────────────────
═════════════════════════════════════════════════════════════════════════════
 SentinelTbarsCount — PLAIN HA / Renko-hybrid "T-brick" BARS TYPE (Sentinel Suite)
 File: SentinelTbarsCount_v1_0_0.cs   Class/Type: SentinelTbarsCount_v1_0_0
 Display Name: "SentinelTbarsCount v1.0.0"  ·  BarsPeriodType id: 212202 (reserved Sentinel bars block 212200–212299)
─────────────────────────────────────────────────────────────────────────────
 WHAT THIS IS
   The Sentinel-graded successor to TbarsCount (frozen). A PLAIN fixed-offset
   renko brick with Heikin-Ashi bodies + real wicks — deliberately WITHOUT the
   adaptive machinery of SentinelTBars (no ATR floor / confirmation / density /
   hysteresis). Two knobs only, via "Speed Settings" (Base → trend = Base/2,
   reversal = Base×2 in ticks). Its point of difference is the LIVE COUNTDOWN:
   it publishes "ticks to the next brick" so SentinelBrickCounter can show it
   on-chart — but that now travels through SentinelCore.BrickState (v1.6.1), so
   ONE generic counter HUD works on ANY brick bars type, not a private feed.

 RELATION TO TbarsCount (frozen — NOT edited)
   Same brick core; reworked for correctness:
     1. INIT/RESET BUG FIXED — TbarsCount seeded barMax/barMin as
        `barOpen ± trendOffset * barDirection` with barDirection defaulting to 0
        (→ a ZERO-WIDTH first brick) and, at a session reset where the prior
        session ended SHORT (barDirection = -1), INVERTED boundaries
        (barMax < barMin → a burst of garbage bricks at each session open).
        Here boundaries seed symmetric (`barOpen ± trendOffset`) with
        barDirection = 1.
     2. BuiltFrom = Tick (was 0) — a renko brick must see every price.
     3. NO STATIC-FEED LEAK — TbarsCountCounterFeed kept a dictionary keyed by
        each Bars object's identity hash and never evicted (leaked one entry per
        reload). Superseded by the SentinelCore.BrickState seam.
     4. GAP CHAINING — a tick that jumps several brick-widths now prints all the
        bricks it crossed (TbarsCount printed one per tick).
     5. GetPercentComplete uses a consistent brick basis (TbarsCount used an odd
        nearest/larger-remaining heuristic).
   Data is durably logged per brick to SentinelCore.BrickLog (NT regenerates
   custom bricks from ticks each load and stores nothing).

 CHANGELOG
   v1.0.0 (2026-07-06) — first Sentinel-graded release; supersedes TbarsCount.
                         BarsPeriodType 69698 → 212202 (RESERVED Sentinel bars block 212200–212299).
```

