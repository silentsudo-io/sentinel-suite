# SentinelTide_v1_0_0.cs

> `bin/Custom/BarsTypes/SentinelTide_v1_0_0.cs`

| | |
|---|---|
| **Family** | Bar types |
| **Version** | 1.0.0 |
| **Size** | 354 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelTide_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.BarsTypes` |
| **Documented by** | [SENTINEL_BARTYPE_GRID](../../SENTINEL_BARTYPE_GRID.md), [SENTINEL_FLOWBARS_SPEC](../../SENTINEL_FLOWBARS_SPEC.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
one at https://mozilla.org/MPL/2.0/.

Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
─────────────────────────────────────────────────────────────────────────────
═════════════════════════════════════════════════════════════════════════════
 SentinelTide — bars clocked by CUMULATIVE ORDER FLOW, not by price or time
 File: SentinelTide_v1_0_0.cs      Class/Type: SentinelTide_v1_0_0
 Display Name: "SentinelTide v1.0.0"  ·  BarsPeriodType id: 212207 (Sentinel block 212200–212299)
─────────────────────────────────────────────────────────────────────────────
 THE IDEA IN ONE LINE
   Every bar contains EXACTLY the same quantum of net aggression — so the bar's HEIGHT
   is a direct, readable measure of MARKET IMPACT.

 HOW IT WORKS
   Session cumulative volume delta (CVD) runs on a fixed lattice:

       cvdLine(k) = k × deltaPerBrick        (k ∈ ℤ, anchored at session CVD = 0)

   A bar closes the moment CVD crosses an adjacent line. Price is NOT the clock — price
   is the OBSERVATION. Each bar answers one question: "the market just absorbed N
   contracts of net aggressive buying (or selling) — how far did price actually go?"

 ⭐ WHY THAT IS WORTH A NEW BAR TYPE — the thing you can SEE and nothing else shows
   Bar HEIGHT ∝ price impact per unit flow. That is Kyle's lambda, rendered.
     • A SHORT bar  = heavy flow moved price barely at all ⇒ someone is filling into it
                      (absorption / a real participant defending a level).
     • A TALL bar   = the same flow travelled a long way ⇒ a thin book; the move is cheap
                      to push and cheap to fade.
   On every other bar type in this suite (and every one retail uses) a bar's height is a
   function of PRICE — so it tells you what already happened. Here it is a function of
   price PER UNIT OF FLOW, which is a liquidity measurement.

 ⭐ AND THE SECOND READ — direction and body can DISAGREE, on purpose
   A bar's DIRECTION is the flow that closed it (which lattice line was crossed).
   A bar's BODY is where price actually went.
   They are independent, and the disagreement is the signal: a flow-UP bar that closes
   DOWN is absorption made visual — aggressive buyers spent N contracts and finished
   lower than they started. No conventional chart can render that, because on a
   conventional chart the bar's direction IS its body by construction.

 DELIBERATE REFUSALS (each is a feature; cf. SentinelLattice's three)
   1. NO PRICE TERM IN THE CLOSE RULE. The instant price can close a bar, height stops
      being a clean impact measure and the whole point is gone. Price only ever gets to
      be the observation. (Physical backstops below are escapes, not price rules.)
   2. NO ADAPTIVE BRICK SIZE. A moving quantum is a moving lattice — same argument that
      keeps SentinelLattice rigid. Bars from different sessions must be comparable, and
      they are only comparable if the flow quantum is identical.
   3. NO SEAM PUBLISH — Tide is a PURE CLOCK. Every bar-type seam in this suite has been
      a source of the F5-decoupling bug class ([[f5-decouples-bartype-seams]]): the
      chart's bars-type instance survives a recompile on the OLD assembly and publishes
      into an orphaned static store while consumers read the new one. Tide has nothing
      to say that SentinelCVD — an ordinary indicator, running on top of it, immune to
      that failure — cannot say better. One fewer publisher is one fewer silent seam.

 ⚠ HONEST LIMITS — read before trusting a bar
   • CVD is an ESTIMATOR. Quote rule where a real bid/ask exists, tick rule otherwise.
     "Same tape ⇒ same bars" holds only for the SAME SIGNING RULE — this is weaker
     determinism than SentinelLattice's price lattice, which needs no estimator at all.
     Stated plainly because the difference matters when comparing the two.
   • CVD measures WHO CROSSED THE SPREAD, not net positioning. Every contract has both.
   • NEEDS TICK DATA. Without it, signing degrades to a bar-body proxy and the impact
     read is materially weaker. `TideDbg` logs which path is live; do not judge a
     no-tick chart by eye and conclude the bar type is broken.
   • Block prints are winsorized (a single print capped at N× its EWMA). SentinelFlux
     learned this the expensive way — one block spiked its threshold to 149 and left the
     clock dormant for hours.
   • A flat, balanced tape prints FEW bars. That is correct, not a stall: no net
     aggression means nothing to measure. The time backstop exists so the chart still
     advances, and a backstop-born bar is marked in the log as such.

 BRING-UP GATES — run these before judging it (cf. LATTICEBARS_SPEC §9)
   G1  Load on GC with tick replay ON. `[Sentinel:Tide]` must log `tape=quote`, not `bar-proxy`.
   G2  Bar count per session should scale ~1/deltaPerBrick. Halve the size ⇒ ~2× the bars.
   G3  Every bar's |ΔCVD| must equal deltaPerBrick (± one print). Logged per bar as `dCvd`.
   G4  Find one SHORT bar and one TALL bar; confirm by eye on a time chart that the short
       one sits at a level where price stalled. If height does not track absorption, the
       premise is wrong and it should be said out loud rather than tuned around.
   G5  Reload the chart from a different start date; bar boundaries within a session must
       be identical (the lattice is session-anchored, so this is checkable).

 CHANGELOG
   v1.0.0 (2026-07-25) — initial. CVD-lattice clock, quote-rule signing with tick-rule
            fallback + winsorized prints, flow-direction bars with independent price body,
            time/tick backstops, no seam publish (pure clock by design).
```

