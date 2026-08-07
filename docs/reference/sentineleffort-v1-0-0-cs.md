# SentinelEffort_v1_0_0.cs

> `bin/Custom/BarsTypes/SentinelEffort_v1_0_0.cs`

| | |
|---|---|
| **Family** | Bar types |
| **Version** | 1.0.0 |
| **Size** | 351 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelEffort_v1_0_0` |
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
 SentinelEffort — EFFICIENCY bars type: distance vs the EFFORT it cost (Sentinel Suite)
 File: SentinelEffort_v1_0_0.cs       Class/Type: SentinelEffort_v1_0_0
 Display Name: "SentinelEffort v1.0.0"  ·  BarsPeriodType id: 212206 (Sentinel block 212200–212299)
─────────────────────────────────────────────────────────────────────────────
 WHAT THIS IS
   A brick closes on WHICHEVER COMES FIRST:
       • DISPLACEMENT — price travelled `Brick Ticks`            ⇒ a SWEEP, and
       • EFFORT       — `E*` contracts traded while it tried     ⇒ ABSORPTION.
   **Which condition fired IS the signal.** Price moved the same distance in both
   cases only in the first; in the second the tape spent the contracts and went
   nowhere. So the BRICK'S OWN SIZE becomes a readout of market efficiency:
       full-size brick  = cheap movement (thin book swept)
       stunted brick    = expensive movement (absorbed into resting size)
   Read a chart of these and absorption is visible as a run of short, dense bricks
   without needing the book.

 WHY IT IS A GENUINELY DIFFERENT AXIS
   SentinelTBars / SentinelDrift clock on PRICE DISTANCE. SentinelFlux clocks on
   SIGNED flow (which side is pressing). This clocks on the UNSIGNED COST of movement.
   Distance, direction, and cost are three different questions — and cost is the one
   no existing Sentinel bar type asks.

 ⚠ SCOPE CHANGE FROM THE ORIGINAL BRIEF (2026-07-25) — and why
   The brief said "a brick closes when price has CONSUMED N CONTRACTS OF RESTING
   LIQUIDITY". That is NOT OBSERVABLE from inside a bars type, and it was verified
   rather than assumed: `OnDataPoint(bars, o,h,l,c, time, volume, isBar, bid, ask)`
   is **L1 only**; NinjaTrader's BarsType API exposes no `OnMarketDepth`, and no bars
   type in this tree reaches L2. Resting liquidity is BOOK state — a bars type sees
   trades and the touch, never the ladder. Rather than fake it, the definition moved
   to what the tape genuinely shows: contracts SPENT versus ticks GAINED. The economic
   question (sweep or absorption?) survives intact; only the instrument changed.
   A true book-depth version belongs in an indicator with `OnMarketDepth`
   (cf. LiquidityWalls), publishing a seam — it cannot be a bar clock.

 SELF-CALIBRATING E*  (the Flux lesson, reused)
   E* = EffortMult × EWMA(contracts per completed brick), so "expensive" is defined
   relative to THIS instrument and THIS session, not a hardcoded lot count. The EWMA
   input is WINSORIZED at WinsorMult× the running estimate because a single block
   trade would otherwise redefine "typical" — Flux shipped that bug live (a ~2000-lot
   print spiked its threshold to 149) and it is not worth learning twice.

 DETERMINISM — HONEST STATEMENT
   This bar type is PATH-DEPENDENT, like TBars and Flux: E* is a carried EWMA, so the
   load point can shift brick boundaries. That is a real cost and it is stated rather
   than glossed. If you need provable replay ≡ live, use **SentinelLattice** (212205),
   which is built for exactly that and gives up adaptivity to get it. The two are
   deliberate opposites and the pair is the experiment.

 PUBLISHES SentinelCore.BrickState under its OWN scope (own bar-type id ⇒ own bartag ⇒
   no collision with TBars/Drift/Lattice) → the Council BRK voter. Slot mapping, in the
   Drift tradition of reusing the seam rather than growing Core:
       densityScale ← EFFICIENCY (1.0 = typical cost; <1 expensive/absorbing; >1 cheap/sweeping)
       pendingBreakout ← true when the LAST brick closed on EFFORT (absorption)
   No Core edit, no Core version bump.

 CHANGELOG
   v1.0.0 (2026-07-25) — first release. Dual-condition close (displacement | effort),
                         self-calibrating winsorized E*, efficiency on the seam,
                         real (non-HA) bodies, time/tick backstops.
```

