# SentinelFlux_v1_0_0.cs

> `bin/Custom/BarsTypes/SentinelFlux_v1_0_0.cs`

| | |
|---|---|
| **Family** | Bar types |
| **Version** | 1.0.0 |
| **Size** | 463 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelFlux_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.BarsTypes` |
| **Publishes seams** | `FluxState` |
| **Documented by** | [SENTINEL_BARTYPE_GRID](../../SENTINEL_BARTYPE_GRID.md) |
| **Depends on this** | [Council_v1_11_0.cs](council-v1-11-0-cs.md), [SentinelCandidateRecorder_v1_0_0.cs](sentinelcandidaterecorder-v1-0-0-cs.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
one at https://mozilla.org/MPL/2.0/.

Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
─────────────────────────────────────────────────────────────────────────────
═════════════════════════════════════════════════════════════════════════════
 SentinelFlux — ORDER-FLOW IMBALANCE bars type (Sentinel Suite)
 File: SentinelFlux_v1_0_0.cs        Class/Type: SentinelFlux_v1_0_0
 Display Name: "SentinelFlux v1.0.0"  ·  BarsPeriodType id: 212203 (reserved Sentinel bars block 212200–212299)
 Spec: Docs/SENTINEL_FLUXBARS_SPEC.md
─────────────────────────────────────────────────────────────────────────────
 WHAT THIS IS
   A bar closes on ACCUMULATED SIGNED ORDER-FLOW IMBALANCE, not on time / ticks /
   volume / range / price distance. This is López de Prado's information-driven
   "imbalance bar" (AFML ch. 2) — each bar carries ≈ constant INFORMATION, so bars
   are fine-grained exactly when one side dominates and coarse when the tape is
   balanced. Married to the SentinelTBars discipline so it survives production:
     • Quote-rule signing   — sign each trade with bid/ask (Lee–Ready), tick-rule
       fallback. Better than the pure tick rule, and free here (OnDataPoint has quotes).
     • Self-stabilising θ*   — the close threshold = FluxSize × ATR(ticks) × imbalance-
       per-price-tick (a bar-size-INVARIANT market intensity), so the classic
       imbalance-bar RUNAWAY (threshold explodes in a trend, collapses in chop) can't
       happen: θ* tracks a ratio, not a self-referential bar-size EWMA.
     • Physical BACKSTOPS    — a bar also force-closes on a price / time / tick cap, so
       it can neither balloon into one giant bar nor stall forever in a dead tape.
     • HA bodies + real wicks — Heikin-Ashi-smoothed body coloured by PRICE direction;
       net FLOW direction is carried in the seam (they can DIVERGE = absorption).
   PUBLISHES SentinelCore.FluxState (v1.31.0 seam) → the Council FLUX voter (a STATE
   voter and the suite's one ORDER-FLOW-substrate axis, orthogonal to the price bloc)
   + a flow-vs-price DIVERGENCE (absorption) size damp.

 WHY (design intent — see the spec)
   1. Orthogonality — every other Sentinel voter is price-derived (echoes the OHLC).
      A flow-SYNCHRONISED clock orthogonalises the WHOLE chart, and the seam adds a
      tape-sourced voter (complement to LiquidityWalls' book-sourced absorption).
   2. Label fidelity — the Council trains on first-touch triple-barrier labels measured
      IN BARS; information-driven bars make "N bars ahead" ≈ "constant information
      ahead", sharpening the exact label the ConvictionFloor / weight fit consume.

 DETERMINISM (non-negotiable for the training corpus)
   Config latched once per session; EWMAs updated once per CLOSED bar; realtime-publish
   only (a historical rebuild must not stamp a stale flow as fresh); scope-keyed publish
   (ScopeOf(bars.Instrument, bars.BarsPeriod)) so two Flux charts never clobber each other.

 CHANGELOG
   v1.0.0 (2026-07-14) — first release. Imbalance clock (Volume mode / quote-rule signing),
                         self-stabilising threshold, price/time/tick backstops, HA render,
                         SentinelCore.FluxState publish seam + Council FLUX voter.
   v1.0.0 (same day, hotfix) — THRESHOLD REWRITE. The first live GC load closed EVERY realtime bar on
                         the 90 s TIME backstop (θ ~38 vs θ* ~90) — the imbalance clock was dormant. Cause:
                         θ* = fluxScale × ATR(true-range) × (|θ|/net-displacement) DOUBLE-COUNTED chop
                         (large range × small displacement) and inflated θ* ~2.5×. Replaced with the
                         canonical López de Prado rule θ* = fluxScale × E[|θ|] (self-consistent EWMA of
                         realized |θ|), so imbalance is the primary close reason. No ATR in θ* (ATR still
                         drives the price backstop only).
   v1.0.0 (same day, live-validated) — WINSORIZE the E[|θ|] input (a ~2000-lot block trade spiked θ* to 149
                         live): cap a bar's EWMA contribution at WinsorMult(=4)× the running estimate so one
                         outlier can't redefine "typical". Removed the temp scope/readback/store diagnostic
                         from the heartbeat (FLUX confirmed voting in the Council; the seam was never the bug —
                         a stale-DLL/sticky-bars-type reload was). LIVE: full 10/10 Council roster on GC.212203v8.
```

