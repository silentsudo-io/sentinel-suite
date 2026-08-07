---
layout: sentinel-ref
title: "SentinelLattice_v1_0_0.cs"
blurb: "Bar types · 1.0.0 · 363 lines"
---

# SentinelLattice_v1_0_0.cs

> `bin/Custom/BarsTypes/SentinelLattice_v1_0_0.cs`

| | |
|---|---|
| **Family** | Bar types |
| **Version** | 1.0.0 |
| **Size** | 363 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelLattice_v1_0_0` |
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
 SentinelLattice — PATH-INDEPENDENT Renko bars type (Sentinel Suite)
 File: SentinelLattice_v1_0_0.cs      Class/Type: SentinelLattice_v1_0_0
 Display Name: "SentinelLattice v1.0.0"  ·  BarsPeriodType id: 212205 (Sentinel block 212200–212299)
─────────────────────────────────────────────────────────────────────────────
 WHAT THIS IS
   Renko bricks anchored to a FIXED PRICE LATTICE instead of floating from wherever
   the series happened to start. Every brick boundary is an exact lattice line

       line(k) = anchor + k × brickTicks × tickSize      (k ∈ ℤ)

   so the brick set is a pure function of (anchor, brick size, price) — NOT of when
   you loaded the chart. Same tape ⇒ same bricks, on reload, on replay, and live.

 WHY — THE PROBLEM IT FIXES (this is the whole point)
   Classic Renko is PATH-DEPENDENT. Its first brick is pinned to the first price the
   series happens to see, and every later boundary inherits that offset. Load the same
   instrument from a different start date and you get a DIFFERENT brick sequence off
   identical tape. That silently corrupts:
     • every backtest built on it (results move when the load window moves),
     • every corpus row labelled in bars (bar N ahead is not the same place twice),
     • any claim that replay ≡ live (they are only equal by luck of alignment).
   A lattice removes the degree of freedom. Determinism stops being an assumption and
   becomes a property you can prove: recompute line(k) and compare.

 THREE DELIBERATE REFUSALS (each one is the feature, not an omission)
   1. NO ADAPTIVE BRICK SIZE. SentinelTBars adapts its offsets to ATR. It cannot be
      done here: a brick size that moves is a lattice that moves, and the invariant
      dies. Lattice is deliberately the RIGID counterpart to TBars' adaptive one — the
      A/B is exactly "does adaptivity earn its path-dependence?"
   2. NO STAGNATION / TIME BRICK. TBars force-closes a brick after a quiet interval.
      A time-born brick does not land on a lattice line, so one would puncture the
      invariant. A flat market here simply prints no bricks — which is the honest
      representation of "nothing happened".
   3. NO HEIKIN-ASHI BODIES. TBars and Flux render HA-smoothed bodies. An HA close is
      (O+H+L+C)/4 — a price that NEVER TRADED. This suite has already paid for that
      once: `firePx` was the synthetic HA close and it biased EVERY excursion label
      (recorded "target-first" 52.3% vs 21.1% true; labels disagreed on 44.6% of
      fires — see [[firepx-is-synthetic-ha-close]]). Lattice bodies are exact lattice
      lines, i.e. real, tradeable prices. Open and close are prices you could have got.

 BRICK RULE (pure, one line of carried state)
   `_level` = lattice index of the last confirmed line. Cross line(_level+1) ⇒ emit an
   UP brick; cross line(_level−1) ⇒ emit a DOWN brick. A fast move through several
   lines emits one brick PER LINE, in order, so no brick ever spans more than one cell.
   ReversalLines defaults to 1 (SYMMETRIC). That is deliberate: with R=1 the entire
   state is `_level`, which any observer recovers from price alone, so two charts
   loaded at different points converge after the first crossing. R>1 adds carried
   DIRECTION state and weakens (does not destroy) path-independence — it is exposed as
   a knob, with that cost stated, rather than hidden as a default.
   Oscillation across one line therefore prints alternating bricks. That is TRUE
   information — price really is oscillating there — not noise to be smoothed away.

 HONEST LIMIT
   Bodies are lattice-exact ALWAYS. Wicks are the true extremes observed while the cell
   was being traversed, so the FIRST brick after a load can carry a short wick if the
   chart attached mid-cell. Exactly one brick is affected, and its body is still exact —
   against classic Renko, where the entire sequence shifts.

 PUBLISHES SentinelCore.BrickState under its OWN scope (its own bar-type id ⇒ its own
   bartag ⇒ no collision with TBars/Drift) → the Council BRK voter. No Core edit, no
   Core version bump, so loading this cannot disturb any existing seam.

 CHANGELOG
   v1.0.0 (2026-07-25) — first release. Absolute price lattice (anchor 0 default),
                         symmetric 1-line reversal, per-line brick emission on gaps,
                         real (non-HA) bodies on exact lattice lines, no time brick,
                         BrickState publish + beacon.
```

