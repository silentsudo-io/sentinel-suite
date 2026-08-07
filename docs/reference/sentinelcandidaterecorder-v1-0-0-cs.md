# SentinelCandidateRecorder_v1_0_0.cs

> `bin/Custom/Indicators/SentinelCandidateRecorder_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 736 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelCandidateRecorder_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel` |
| **Consumes seams** | `BrickState`, `ClockState`, `FluxState`, `MtfState`, `ParticipationState` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
one at https://mozilla.org/MPL/2.0/.

Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
─────────────────────────────────────────────────────────────────────────────
═════════════════════════════════════════════════════════════════════════════
 SentinelCandidateRecorder — the CLOCK-native candidate corpus (the "second oven")
 File: SentinelCandidateRecorder_v1_0_0.cs   ·   Version v1.3.0   ·   Schema cand.2 (sidecar ctick.4)   ·   namespace …Indicators.Sentinel
─────────────────────────────────────────────────────────────────────────────
 WHAT THIS IS
   A pure, no-orders recorder that tests one hypothesis: **the edge lives in the CLOCK, not the fused
   voters.** It records the UNFILTERED, clock-native candidate population — **every brick close is one
   candidate**, direction = the brick's own direction (the simplest possible CONTINUATION primitive), no
   selection, no fusion, no gate. For each candidate it tracks (fire → EOD) the max fav/adv excursion
   (MFE/MAE, ticks), 1/5/15/60-min milestones, the ATR-scaled FIRST-TOUCH label, the raw TICK path, and
   **`runLength`** (consecutive same-direction bricks → makes EXHAUSTION a retroactive slice of the same
   corpus). It stamps CONTEXT-at-fire (regime/ADX/RVOL/clock-phase/MTF/Flux) by CONSULTING the published
   `…State` seams — read-only, never fused — so every gate is a Lab-side retroactive filter, never a
   hardcoded `if`. One bake tests the whole (rule × regime × exit) grid; we derive labels LATE.

   Why a NEW corpus (not the Council corpus): the Council excursion corpus is a FUSION-GATED, biased sample
   ("the Council chose to fire") → it structurally cannot measure whether the CLOCK itself has a base rate.
   Different question ⇒ different corpus. Rows land in Sentinel\Excursions\candidates\cand.2\ (signal tag
   "CONT"), sidecars in candidates\ticks\ — a separate root so the population NEVER pools with COUNCIL rows.
   Runs in PARALLEL with the Council 1.5 bake (two ovens, one apparatus).

   Hypothesis + design: Docs\SENTINEL_CLOCK_EDGE_HYPOTHESIS.md (candid version: private _sidebar Entry 3).
   Fidelity spine (realtime gate · tick-path capture · first-touch · window-streaming · provenance · scope)
   is inherited verbatim from SentinelExcursionRecorder_v2_0_0 — the same checklist that keeps it trustworthy.

 ✅ C6 BOUNDARY CLOSED in v1.2.0 (2026-07-22). It read: "firePx = Close[0] (a synthetic brick close on
   HA/TBars/Flux, not a tradeable print)". That caveat was CORRECT and was never quantified — measurement
   showed it put a ~9-tick optimistic offset on EVERY label. firePx is now the real last trade. What remains
   of C6: still no orders/fills/slippage — this corpus grades SIGNAL-PATH quality. Grading exit policies on
   the recorded tick path = Phase 1 (cheap, honest-enough). Real path-managed-EXIT validation = the
   Ledger/execution rail (Phase 2, separate build). Don't mistake a Phase-1 exit curve for a tradeable one.

 CHANGELOG
   v1.3.0 (2026-07-23) — LATCHED BOUNDARIES AT FIRE (`brkUpper`/`brkLower` from BrickState) so limit-vs-market
          entry is gradeable offline on every bar type at once — no new bar type needed (TBars boundaries are
          already immutable within a bar; BrickState has published them per tick all along).
          🐛 FIXED WHILE HERE: `GetFluxState` was read with the LANED `Scope()`. FluxState is a BAR-TYPE seam
          published by the shared bars series, so it is keyed BARE — on any laned chart the read returned null
          and fluxDir/fluxPressure/fluxDiverg silently stamped 0 forever. Same class of fault as the crashed-
          sensor pattern: absence was indistinguishable from a real reading. Now uses BareScope().
          Schema stays cand.2 (additive fields); recVer separates the batches.
   v1.2.1 (2026-07-22) — ENTRY BACKFILL (council-recorder v2.2.1 parity). `_lastPx` is the trade BEFORE the one
          that closed the brick (OnMarketData runs after OnBarUpdate), so a first path tick in the SAME
          millisecond is adopted as the entry (`pxSrc="firsttick"`); a later one is not, since that would be
          lookahead. This matters MOST here: brick CASCADES (measured on GC Renko 11v1x1) print several bricks
          off one jump, and every one of them was inheriting the same pre-jump entry.
   v1.2.0 (2026-07-22) — 🔴 THE HONEST ENTRY PRICE (parity with council recorder v2.2.0). Row cand.1 → cand.2,
          sidecar ctick.3 → ctick.4. `FirePx` was `Close[0]` = the HEIKIN-ASHI SYNTHETIC close, a price that
          NEVER TRADED — the C6 BOUNDARY above, flagged from day one and never measured. Measured 2026-07-22:
          the gap is −9.36t mean, symmetric by side (the HA fingerprint), and since FirePx is the reference for
          MFE/MAE/barrier/first-touch it made every label optimistic (council corpus: "target-first" 52.3%
          recorded vs 21.1% TRUE, labels disagreeing on 44.6% of fires). FIX: latch the true last trade in
          OnMarketData BEFORE the tick-path guards; `pxSrc` records how it resolved; `barClosePx` keeps the HA
          close; `entryBid`/`entryAsk` let the Lab price the crossing cost. cand.1 kept, never pooled with
          cand.2. Evidence: memory `firepx-is-synthetic-ha-close`.
   v1.1.0 (2026-07-18) — ENTRY CONTEXT on the TICK SIDECAR HEADER (sidecar ctick.2 → ctick.3; ROW schema
          UNCHANGED at cand.1). Parity with the council recorder v2.1.6 fix: the candidate sidecar now stamps the
          full entry-context block (regime/adx/rvol/volZ/climax/dryUp/clockPhase/minsToClose/mtfBias/fluxDir/
          fluxPressure/fluxDiverg) — all already captured on the Rec at fire, so emit-only. Makes every candidate
          tick-path SELF-DESCRIBING so Lab\pathlab.py can gate the candidate corpus without a (inst,bartype,dir,
          fireTime) join to cand.1 (that join lands ~97%, but the header is exact + join-free). Old ctick.2 sidecars
          stay valid (readers fall back to the join). No order/Core change.
   v1.0.0 (2026-07-17) — first cut. Every-brick-close CONT candidate; runLength + seam context; schema cand.1;
          candidates\ corpus + ticks sidecars; provenance (recVer/coreVer/barLabel) + realtime gate + tick
          path reused from the excursion recorder. No State seam (a recorder, not a sensor).
```

