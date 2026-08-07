---
layout: sentinel-ref
title: "SentinelExcursionRecorder_v2_0_0.cs"
blurb: "Indicators · 2.0.0 · 1112 lines"
---

# SentinelExcursionRecorder_v2_0_0.cs

> `bin/Custom/Indicators/SentinelExcursionRecorder_v2_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 2.0.0 |
| **Size** | 1112 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelExcursionRecorder_v2_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel` |
| **Consumes seams** | `BrickState`, `CouncilState` |
| **Documented by** | [SENTINEL_STRATEGY_INTEGRATION_SPEC](../../SENTINEL_STRATEGY_INTEGRATION_SPEC.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
one at https://mozilla.org/MPL/2.0/.

Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
─────────────────────────────────────────────────────────────────────────────
═════════════════════════════════════════════════════════════════════════════
 SentinelExcursionRecorder — pure signal characterization for the Sentinel Suite (NT8)
 File: SentinelExcursionRecorder_v2_0_0.cs   ·   Version v2.3.0   ·   Schema 1.5 (sidecar ctick.4)   ·   namespace …Indicators.Sentinel
─────────────────────────────────────────────────────────────────────────────
 WHAT THIS IS
   A pure, no-orders CHARACTERIZATION recorder. On each edge-detected COUNCIL verdict (an aligned
   bias-flip, mirroring the SentinelBridge entry trigger) it opens a record and tracks, fire → EOD,
   the max favourable/adverse excursion (MFE/MAE, ticks), 1/5/15/60-minute milestones, and the
   schema-1.3 FIRST-TOUCH label (which ATR-scaled barrier — target or stop — is crossed first).
   Rows are appended to Sentinel\Excursions\*.jsonl and consumed offline by the Observatory + the ML
   Lab to grade the Council and fit its ConvictionFloor.

 CLEAN-ROOM LINEAGE (2026-07-11) — this v2.0.0 is the OPEN-SOURCE cut of the recorder. It is the
   private SentinelExcursionRecorder_v1_4 (schema 1.3) with the optional GodTrades21 hosting REMOVED:
   v1_4 could also host JET's GodTrades21 as a "does the Council beat raw FC?" comparison baseline
   (RecordGodTrades / ShowUnderlyingIndicator). That baseline embedded a third-party engine that is
   not part of the open-source distribution, so this version records the COUNCIL verdict ONLY. The
   JSONL schema is UNCHANGED (still 1.3) — Council rows are byte-identical to v1_4's; there are simply
   no BG/FC/OBR rows. No SentinelCore change (CONSULTS CouncilState ≥ v1.7.0 + GetEyeVerdict, both seams).
   v1_4 stays as the user's private tool (frozen); this is the shippable.

 CHANGELOG
   v2.5.0 (2026-07-31) — 🔴 THE TICK CORPUS COULD NEVER ANSWER D2. Row schema UNCHANGED at 1.5.
          TickPathTailMs (how much path is kept AFTER a fire resolves) was a private const 30000 and is now a
          SETTING, default deliberately unchanged so this cannot alter an existing run.
          WHY: measured over the live Keel bake, 0 of 80 knockouts had a tick path reaching their own MFE bar
          (p50 path 127s, max 1413s, NONE past 30 min). The D2 defect — stopped out while the move kept going —
          lives entirely in the path AFTER the stop, i.e. exactly the segment this tail was discarding 30s in.
          So a re-entry rule could not be designed from the tick corpus at ANY bake length. Not a bug (v2.1.1
          deliberately wanted "the immediate post-R behavior"), but it silently bounded what the corpus can be
          asked. A D2 study bake now sets the tail to ~3600000 (60 min, matching the row's MFE/MAE window).
          ⚠ Costs memory + much larger sidecars, so it is a BAKE-TIME decision, never a new default.
   v2.4.0 (2026-07-30) — 🔴 THE CORPUS COULD ONLY EVER SEE THE COUNCIL. Row schema UNCHANGED at 1.5.
          This recorder's only intake was the `GetCouncilState` poll, so it opened on `Open("COUNCIL", …)`
          and nothing else — which meant EVERY STRATEGY WAS INVISIBLE TO THE CORPUS BY CONSTRUCTION. The
          system built to grade decisions could not see a strategy's decisions; you could run one for a
          month and have nothing to grade it with. That is the actual blocker behind SentinelKeel, and it
          was never an exit-policy problem.
          Intake is now GENERIC: `SentinelCore.NoteSignalFire(scope,dir,tag,isHistorical,…)` (Core v1.46.0)
          queues a decision, this drains it each bar and opens a fire under the publisher's own tag. The
          Council becomes one caller among several rather than the only one.
          ⚠ Schema deliberately NOT bumped. The row's `signal` field already carries the tag, so a KEEL row
          and a COUNCIL row differ only in a value the Lab already reads — no reader changes, no corpus
          split. The Council-specific columns record as NaN/null for an external fire, which is the honest
          value for a fire the Council did not produce. Carrying strategy-side context (its own conviction,
          its intended size) ONTO the row is a real schema change and is deliberately left for when there
          is a strategy actually producing it.
          ⚠ `Record external fires` is NOT a [NinjaScriptProperty] — a new constructor parameter would
          regenerate the region and drop this indicator off every chart that already carries it.
          Defaults ON: a fire only exists because a strategy deliberately published one, and defaulting OFF
          would mean an author instruments their code, sees nothing, and cannot tell that from a broken
          intake. Realtime-gated here AND rejected-if-historical inside Core — two locks on the same door,
          because a caller passing the wrong flag must still not contaminate the corpus.
   v2.3.0 (2026-07-23) — LATCHED BOUNDARIES AT FIRE (`brkUpper` / `brkLower`, from BrickState). Makes the
          LIMIT-vs-MARKET entry question gradeable OFFLINE, on every Sentinel bar type at once, with no new
          bar type. Why no new bar type: the limit-bar spec claimed TBars "re-derives its boundaries every
          tick", which would make a resting order impossible to maintain -- that was WRONG. `barMax`/`barMin`
          are assigned ONLY in CreateBreakoutBar / ForceTimeBrick / InitializeFirstBar, all bar-CREATION
          events, and RefreshDynamicOffsets() is called from those three and nowhere else. TBars boundaries
          are ALREADY immutable within a bar, and BrickState has been publishing them per tick all along.
          So the level a limit could rest at already existed; only the RECORD of it was missing.
          ⚠ Read on the BARE scope: bar-type seams are keyed by the shared bars series, so the laned Scope()
          returns null for them on a laned chart (new BareScope() helper; the Council already did this).
          ⚠ ACCEPTANCE: firePx must sit BETWEEN brkLower and brkUpper -- that proves the seam held the FORMING
          bar's boundaries at fire, not the closed one's. Verify it in the data; do not assume the call order.
          Schema stays 1.5 (purely additive fields); recVer separates the batches.
   v2.2.1 (2026-07-22) — ENTRY BACKFILL. v2.2.0 priced a fire from `_lastPx`, but OnMarketData runs AFTER
          OnBarUpdate for the tick that CLOSED the bar, so the latch held the trade BEFORE the triggering one.
          Verification measured the residual: ~0 on Sentinel bar types, but on a jump-driven CASCADE it is the
          whole jump (GC Renko printed 5 bricks off one 7-tick move — 5 fires sharing ONE stale entry price and
          ONE forward tick path; 96% of Renko fires shared a firePx with another fire). Now: when a fire's FIRST
          path tick lands in the SAME millisecond as the fire, it IS the triggering trade and the first
          transactable price -> adopt it (`pxSrc="firsttick"`). ⚠ NOT unconditional -- a next-trade seconds away
          would import real forward drift into the entry, which is lookahead; those keep `_lastPx`/`pxSrc="last"`.
          One-shot per Rec (`PxFixed`), placed BEFORE every read of FirePx in the loop. Schema stays 1.5 (both
          pxSrc values denote a real, fillable price — which is exactly what 1.5 asserts); recVer separates them.
          ⚠ SEPARATE, UNFIXED, and bigger: those cascade rows are NOT independent observations. Any fit must
          dedupe or cluster-weight them or it overstates n by ~5x on cascade-prone bar types.
   v2.2.0 (2026-07-22) — 🔴 THE HONEST ENTRY PRICE. Row schema 1.4 → 1.5, sidecar ctick.3 → ctick.4.
          `FirePx` was `Close[0]`. On EVERY Sentinel bars type (TBars/TbarsCount/Flux/Drift) that is the
          HEIKIN-ASHI SYNTHETIC close — an average that NEVER TRADED — while the tick path was always the real
          tape (`OnMarketData`/`Last`). This file's own v2.1.0 comment said so ("Close[0] is a synthetic brick
          close on HA/TBars"); nothing downstream acted on it.
          MEASURED 2026-07-22 over 3,710 replay sidecars, four independent ways:
            • gap dir*(firePx − px[0]) = −9.36t mean, SYMMETRIC by side (LONG −9.30 / SHORT −9.42) — the HA
              fingerprint (HA close sits below price on up bars, above on down bars ⇒ adverse BOTH ways)
            • px[0] reconciles to the real traded price within 1 tick 95.7% of the time; firePx only 6.9%
            • 99.6% of paths start at ms=0 ⇒ NO elapsed time for a "chase" — the gap is definitional, not cost
            • positive control: non-Sentinel bar types show NO bias (GC 2016v2x8 +0.05t, 50/50) while every HA
              Sentinel type is systematically negative ⇒ a property of OUR BAR TYPES, not of the market
          ⚠ WHY IT MATTERED: FirePx is the single reference for MFE / MAE / barrier / first-touch. Every label
          in the corpus was measured from an untradeable price — MFE +9.36t optimistic, MAE 9.36t understated,
          and the ML target label disagreed with truth on 44.6% of fires (recorded "target-first" 52.3% vs
          21.1% TRUE; of 1,940 rows labelled target-first only 36.9% were, 59.8% never resolved at all).
          The corpus claimed trades work ~2.5× more often than they do, and every fit inherited that.
          FIX: latch the true last trade in OnMarketData (BEFORE the tick-path guards — a fire must be priced
          even when path capture is off) and use it as FirePx. `pxSrc` records how it resolved ("last" |
          "barclose" fallback when the tape has not spoken), `barClosePx` keeps the HA close as its own field,
          and `entryBid`/`entryAsk` are recorded so the Lab can price the crossing cost offline instead of
          us guessing it now. ⚠ 1.5 rows land in `council\1.5\` and must NEVER be pooled with the 1.4 corpus;
          1.4 is kept (a valid record of what the old logic saw), not deleted. Same fix in
          SentinelCandidateRecorder v1.2.0. Full evidence: memory `firepx-is-synthetic-ha-close`.
   v2.1.6 (2026-07-17) — ENTRY CONTEXT on the TICK SIDECAR HEADER (sidecar schema ctick.2 → ctick.3; ROW schema
          UNCHANGED at 1.4). The offline path/exit analysis (Lab\pathlab.py) joins each tick-path back to its row
          corpus by `episodeId` to get regime/clock/vote context — but only ~⅓ of paths matched a row (the sidecar
          records more fires than the resolved-window row corpus retains), so two-thirds of paths were context-blind
          and the regime/conviction GATE analysis ran on a biased third. Fix: stamp the entry-context block
          (`regime, adx, clockPhase, rvol, mtfBias, netScore, activeW, voters, agree, disagree`) directly onto the
          sidecar header — every field is ALREADY captured at Open() on the Rec, so this is emit-only, no new capture,
          no order/Core change. Tick-paths are now SELF-DESCRIBING → 100% context coverage for the gate fit, no join.
          Old ctick.2 sidecars stay valid (readers fall back to the join); mixed dir is fine (schema field distinguishes).
   v2.1.5 (2026-07-16) — cnclVer (A1 provenance completed; Core ≥ v1.36.0 · Council ≥ v1.8.0). Each row + sidecar now
          also stamps `cnclVer` — the exact COUNCIL version that produced the verdict (from CouncilState.CouncilVersion)
          — finer than `coreVer`, which only moves on a SentinelCore bump and would miss a Council-only logic change.
          Stays schema 1.4 (cnclVer completes the same-session provenance schema; a pre-cnclVer 1.4 row simply carries
          it null). Closes the "no logic-version stamp" fidelity debt: recorder + core + council versions all on the row.
   v2.1.4 (2026-07-16) — SCHEMA 1.4 = PROVENANCE + FAIL-LOUD (fidelity audit A1/A2; Core ≥ v1.35.0). Two fixes from
          the "record truth" audit: (A1) every row + tick sidecar now carries a PROVENANCE block — `recVer` (this
          recorder), `coreVer` (SentinelCore.Version), and `barLabel` (the human bartag "SentinelFlux 8"). Before
          this, `schema:"1.3"` versioned only the row SHAPE, so a Council/recorder LOGIC change left old and new rows
          pooling indistinguishably — a fit over the blend measured two systems as one. Shape changed ⇒ version bumps:
          rows now land in `council\1.4\`, sidecars are `ctick.2`, and the pre-provenance `1.3` corpus stays frozen
          beside it. (A2) the recorder no longer dies SILENTLY — a failed writer setup logs "WRITER DEAD" + shows a
          red **NO REC** pill (it used to draw a healthy card while writing nothing), and a write exception logs ONCE
          ("WRITE FAILED — rows being LOST") instead of being swallowed per-row. The card footer now shows the human
          bartag. `cnclVer` (the exact Council version on the verdict) is a documented fast-follow — coreVer is the
          working logic fingerprint today. Class/file identity kept _v2_0_0 (F5-safe mid-collection).
   v2.1.3 (2026-07-14) — PER-CHART LANE (Core ≥ v1.32.0). Scope() now folds in the chart's lane via ChartControl,
          so on a laned chart the recorder reads the LANED CouncilState (the Council publishes @lane, not bare) and
          files the corpus under the laned scope → two same-bartype test lanes (A/B) record into SEPARATE corpora
          and the Lab grades each independently. Bare scope when no lane (back-compat). Schema UNCHANGED (still 1.3).
   v2.1.2 (2026-07-14) — RESOLUTION-BASED ROW flush (crash-safety). A row is now streamed to the 1.3 corpus the
          moment its excursion window is COMPLETE (past the last milestone, 60 min post-fire) and released from
          _open, instead of buffering the WHOLE session and flushing only at session-roll / Terminated. Before this,
          a hard NT kill (force-close, hang) lost every un-flushed row — i.e. the whole session's VOTE VECTORS, the
          ML gold — while the tick sidecars (OnMarketData, already resolution-flushed) survived. Now crash-loss is
          bounded to the in-flight (< 60 min old) fires; completed rows carry endReason="window". Censored/in-flight
          fires still flush "EOD"/"cutoff" at the session boundary exactly as before. Schema UNCHANGED (still 1.3).
   v2.1.1 (2026-07-14) — RESOLUTION-BASED tick-path flush. A fire's sidecar is now written + its buffer released
          ~30s after first-touch (or a 5-min hard cap for a censored fire), instead of only at the session-boundary
          FlushAll. Keeps tick buffers from piling up to EOD on a fast-firing chart (e.g. an STF-only surface) and
          lands sidecars within seconds of resolution. The ROW is unchanged (still tracks MFE/MAE to EOD via bar
          High/Low). WriteTickPath is idempotent (PathWritten guard) since OnMarketData + FlushAll both call it.
   v2.1.0 (2026-07-14) — RAW-TICK PATH capture (ML Phase 3). A new OnMarketData override records the true
          last-trade tick path of every Council fire → Sentinel\Excursions\council\ticks\<fireId>.jsonl,
          joined to the row by episodeId + fireTime (the JSONL ROW schema is UNCHANGED — still 1.3 — so the
          Lab's existing corpus reader is untouched; the sidecars live in a subfolder it globs past). Each
          sidecar carries tick-resolution MFE/MAE + a TICK-TRUE first-touch label (msToTargetR/msToStopR,
          firstTouchTick) so conviction can be graded vs PATH QUALITY, not just the coarse brick first-touch
          binary. Gated by RecordTickPath (default ON, non-NinjaScriptProperty → no codegen churn). Realtime/
          replay only (OnMarketData never fires historical — same as-of guard the row recorder already honors).
          Class/file identity kept _v2_0_0 so an F5 doesn't drop it off charts mid-collection.
   v2.0.0 (2026-07-11) — clean-room fork of v1_4: GodTrades21 host removed (field, instantiation,
          BG/FC/OBR signal path, RecordGodTrades + ShowUnderlyingIndicator properties, card tallies).
          Council-only. New type identity (SentinelExcursionRecorder_v2_0_0) → add to charts fresh.
   (schema 1.3 first-touch machinery + the Council/Eye seam capture are inherited verbatim from v1_4.)
```

