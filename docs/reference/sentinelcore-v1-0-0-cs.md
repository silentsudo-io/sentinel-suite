---
layout: sentinel-ref
title: "SentinelCore_v1_0_0.cs"
blurb: "AddOns / runtime · 1.47.0 · 4322 lines"
---

# SentinelCore_v1_0_0.cs

> `bin/Custom/AddOns/SentinelCore_v1_0_0.cs`

| | |
|---|---|
| **Family** | AddOns / runtime |
| **Version** | 1.47.0 |
| **Size** | 4322 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `EyeVerdict` |
| **Namespace** | `NinjaTrader.NinjaScript.AddOns.Sentinel` |
| **Documented by** | [ROADMAP](../../ROADMAP.md), [SENTINEL_DESIGN_SYSTEM](../../SENTINEL_DESIGN_SYSTEM.md), [SENTINEL_FLOWBARS_SPEC](../../SENTINEL_FLOWBARS_SPEC.md), [SENTINEL_RUNBOOK](../../SENTINEL_RUNBOOK.md), [SENTINEL_STRATEGY_INTEGRATION_SPEC](../../SENTINEL_STRATEGY_INTEGRATION_SPEC.md), [SENTINEL_THESIS](../../SENTINEL_THESIS.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
one at https://mozilla.org/MPL/2.0/.

Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
─────────────────────────────────────────────────────────────────────────────
═════════════════════════════════════════════════════════════════════════════
 SentinelCore — shared infrastructure for the Sentinel Suite (NT8)
 File: SentinelCore_v1_0_0.cs
 Version: v1.0.0
─────────────────────────────────────────────────────────────────────────────
 WHAT THIS IS  (see Docs/ROADMAP.md, memory: sentinel-suite-architecture)
   The ONE intentional shared dependency of the Sentinel Suite. Every Sentinel
   tool (Copy, Log, Risk, Lens, Arc, Eye) is an INDEPENDENT headless AddOnBase
   service — they do NOT depend on each other. They depend only on THIS: a small,
   stable core that carries the suite-wide plumbing that must be shared to be useful:
     • the SHARED KILL-SWITCH  (one flip halts every tool that trades/acts)
     • the FEED-HEALTH GATE    (a pluggable per-account "is this feed safe?" probe)
     • the SETTINGS DIRECTORY  (one place tools persist their config)
     • a common LOG helper      (tagged output to the NinjaScript Output window)

 DELIBERATE VERSIONING EXCEPTION (flagged — deviates from the per-file policy):
   The CLASS is named `SentinelCore` WITHOUT a version suffix, even though the FILE
   is `SentinelCore_v1_0_0.cs`. Rationale: every tool references this class BY NAME;
   version-suffixing the class (SentinelCore_v1_0_0) would break every consumer on
   each bump. Shared infrastructure must have a STABLE symbol. So for THIS file only:
   bump the file name + changelog on change, but keep the class name `SentinelCore`
   stable and STRICTLY BACKWARD-COMPATIBLE (add members; don't remove/rename). If a
   breaking change is ever unavoidable, that's the day it earns a V2 class name.
   (Confirm this exception is acceptable — see chat 2026-07-01.)

 SHARED-INFRA VERSIONING NOTE: because the class name `SentinelCore` is STABLE (unversioned,
   so consumers never break), this file is EDITED IN PLACE — it can NOT have coexisting
   versioned copies (two `class SentinelCore` = CS0101 duplicate, breaks the whole compile).
   Bump the `Version` const + add a changelog entry on change; do NOT copy to a new file name.

 CHANGELOG
   v1.37.0 — new `ConvictionState` seam (additive). The SentinelDrift bar type (id 212204) publishes a FLOW-CONFIRMED
            directional bias (structural brick direction, gated by whether the aggregated tape confirms it) → consumed
            by the Council `CVB` voter (STATE, orthogonal/order-flow). Mirrors the FluxState seam; scope-keyed SeamStore.
   v1.36.0 — cnclVer PROVENANCE (additive; A1 fast-follow). CouncilState gains `CouncilVersion` — the Council's OWN
            version that produced the verdict — carried via a TRAILING OPTIONAL param on the richest SetCouncilState
            overload (back-compat: every existing caller compiles unchanged, the field is null when not passed). The
            recorder stamps it as `cnclVer`, naming the exact fusion LOGIC per row — finer than `coreVer` (which only
            moves on a SentinelCore bump, missing a Council-only change). Closes the A1 provenance thread.
   v1.35.0 — HUMAN-READABLE BARTAG WRAP (additive; display-only). The machine tag "212203v8" is the immutable
            scope KEY (changing it orphans the corpus/saved charts/roster folders), but a human never dialed
            "212203" — they picked SentinelFlux at scale 8. New `BartypeName(id)` (id → registered/enum/"Type<id>"
            name), `FriendlyBartag("212203v8") → "SentinelFlux 8"`, `FriendlyScope("GC.212203v8@FooBoo") →
            "GC · SentinelFlux 8 · FooBoo"`. DISPLAY ONLY — the key is unchanged; consult/join still use the raw
            tag. Consumed by the Cockpit/cards/dashboards so humans see speed+interval, machines keep precision.
   v1.34.0 — HELM seam (additive; Phase 5 — the INTERDICTION layer, memory helm-interdiction-layer). Helm lets a
            human grab the wheel of a RUNNING automated actor without stopping it: it publishes an INTENT addressed
            to an instanceKey and the owner executes it with its OWN order handles (Helm never touches an order —
            the three managed-order lessons in CLAUDE.md converge on why). New: HelmVerb enum · HelmIntent (id +
            expiry + verb + payload, with IsRiskReducing/IsRiskAdding for the asymmetric gate) · one-shot
            SetHelmIntent/TakeHelmIntent (idempotent FIFO drain, expiry-guarded against replay) · PendingHelmIntents
            peek · HelmState publish-back (SetHelmState/GetHelmState/AllHelmStates) so Helm's card renders reality ·
            ClearHelm teardown. Keyed by instanceKey, exactly like the actor registry. Purely additive.
   v1.31.0 — FluxState seam (additive). SentinelFlux (order-flow IMBALANCE bars type, id 212203) publishes its
            net-flow direction / buy-sell pressure / flow-vs-price DIVERGENCE per closed bar. → the Council FLUX
            voter (STATE, orthogonal ORDER-FLOW axis — not price-derived) + a divergence (absorption) size damp.
            Scope-keyed like BrickState (a bars type's scope = ScopeOf(bars.Instrument, bars.BarsPeriod)).
            VoterCatalog gains FLUX; Council fuses 22 voters. Spec: Docs/SENTINEL_FLUXBARS_SPEC.md.
   v1.30.0 — TWO NEW VOTER SEAMS (additive; candidate-library novel-signals pass). VidyaState — SentinelVIDYA
            Chande-CMO adaptive-MA trend → Council VDYA voter (STATE). HarmonicState — SentinelHarmonic XABCD
            pattern completion → Council HARM voter (TRIGGER). VoterCatalog gains both; Council fuses 21 voters.
   v1.29.0 — TrendArchitectState seam (additive). SentinelTrendArchitect (the MPL Pine port) publishes its
            PRISM bias/signal + Trend-Regime-Gate → the Council ARCH voter (STATE). VoterCatalog gains ARCH;
            Council fuses 19 voters. Backward-compatible (added members only).
   v1.28.0 — FOUR NEW VOTER SEAMS (additive; the candidate-library Tier-2 voter pass, 2026-07-12). Each mirrors
            the WaeState object-seam pattern (SeamStore + Set/Get/Touch/All, scope-keyed, auto-expiring):
              • AdxvmaState     — SentinelADXVMA: ADX-volatility adaptive-MA trinary trend. Council AVMA voter (STATE).
              • SuperTrendState — SentinelSuperTrend: ATR-band trailing-flip trend. Council SPRT voter (STATE).
              • SarState        — SentinelParabolicSAR: Wilder SAR trend/stop. Council PSAR voter (STATE).
              • ZScoreState     — SentinelZScore: (Close−SMA)/StdDev mean-reversion. Council ZSC voter (TRIGGER).
            VoterCatalog gains the 4 rows; Council fuses 18 voters. Backward-compatible (added members only).
   v1.27.0 — SYSTEM BUILDER substrate (additive; new partial SentinelCore.SystemBuilder.cs — spec
            Docs/SENTINEL_SYSTEM_BUILDER_SPEC.md Phase 0). Nothing here changes existing behaviour.
            (1) VoterCatalog — the tag → indicator-class / role / seam / default-weight+kind map (14
            voters + 5 context axes), mirroring the Council's KnownVoters + SetDefaults. (2) RosterIO —
            ONE reader/writer for Roster.conf: Read() reproduces the Council's exact cascade + parse,
            Write() serialises a RosterDoc back atomically (temp + File.Replace), so the Council (reader)
            and the System Builder (writer) can never drift on format. (3) Public VoterKind + SensorRole
            enums + CatalogEntry / RosterLine / RosterDoc types. The Council will consume RosterIO next.
   v1.26.0 — FIVE NEW SEAMS (additive; the installed-tree port harvest, 2026-07-12). Adds the two orthogonal
            axes the suite lacked plus three new voices, each mirroring the WaeState object-seam pattern
            (SeamStore + Set/Get/Touch/All, scope-keyed, auto-expiring, backward-compatible):
              • FlowState        — SentinelFlow (FLOW axis): tick-rule CVD regime — Bias/Slope/RSquared/Strength,
                                   price-vs-CVD Divergence, confirmed Signal. Council FLOW voter (STATE).
              • ProfileState     — SentinelProfile (PROFILE axis): developing volume profile POC/VAH/VAL, value-area
                                   Location (-1/0/+1), POC-reversion Signal, HVN/LVN proximity. Council context modulator.
              • RegimeState      — SentinelRegime: K-means volatility-regime + Markov forward filter — Regime 0/1/2 +
                                   per-regime probability. Council conviction/size MODULATOR (not directional).
              • StructureState   — SentinelStructure: swing HH/HL·LH/LL structure Bias + break-of-structure. Council STRC voter (STATE).
              • ExhaustionState  — SentinelExhaustion: Leledc consecutive-close exhaustion — reversal Signal/Dir pulse.
                                   Council EXH voter (TRIGGER; mean-reversion voice, à la GREV).
   v1.25.0 — IDENTITY (ML spec §10, additive). (1) CouncilState.EpisodeId — the episode primary key (a maximal
            run of constant Bias), the join that closes fills → episode → verdict → excursion outcome; carried on
            the full SetCouncilState overload. (2) ACTOR REGISTRY — RegisterActor/UnregisterActor/AllActors: "the
            name is an interlock" — an armed actor REFUSES to run on an instanceKey collision (the same-scope+account
            managed-position hazard), reference-checked release for the NT re-enable race. (3) Ledger.Order/Action/
            Fill gain optional episode + instance context (§10.7) so Lens can join a fill to the verdict that caused
            it. All additive — every prior caller compiles and behaves identically.
   v1.24.0 — CouncilState DECISION VECTOR (ML spec §2.1, additive). CouncilState gains Votes/VoteW (per-voter
            dir + effective weight), NetScore/ActiveW (signed, pre-normalization), and the orthogonal-axis
            modulator context (ClockPhase/Rvol/MtfBias/LevelInPath/LevelName) — previously fused into the verdict
            but invisible to every consumer. New SetCouncilState overload carries them; ALL prior overloads
            delegate here with vector defaults (purely additive — behaviour-neutral). This is the input side the
            offline Lab needs to FIT the Council's weights, not merely grade its output.
   v1.23.0 - INTERNAL RUNTIME SPLIT (no API change; Docs/PRODUCT_LADDER.md sec 4-5). SentinelCore is now
            a `partial class` spread across files for distribution tiering. EXTRACTED to
            SentinelCore.Safety.cs: ALL of L2 (feed-health, CanAct/CanActInstrument/CanEnter, governor,
            drawdown, profiles, session, sizing, order guards, GateEntry). EXTRACTED to
            SentinelCore.Foundation.cs (so far): SettingsDir/SettingsFile/LogFile/Log/WriteLogFile.
            Ledger + State stay Foundation (audit/persistence primitives). Same class, same call sites
            -> ZERO consumer churn. VERIFIED bundle-clean: the F+L1 files have no call into Safety, so
            the Skins/Sensors bundles compile with Safety.cs OMITTED. F5-verified per batch (2026-07-10).
   v1.22.0 — NEW SEAM: StfState (SetStfState/GetStfState/TouchStfState/ClearStfScope/AllStfStates). Published by
            SentinelStochasticTripleFilter_v1_0_0 (the Sentinel port of "Stochastic Triple Filter [ATP]"): the DonovanWall
            Gaussian-Channel midline SLOPE (Bias, a non-CCI/ADX trend regime) + a Choppiness-Index REGIME flag
            (Trending). SCOPE-keyed (a slope/chop reading varies with the chart's bar type). Wired into the
            Council as a trend voter "STF" (enters at weight 0 — exploration) + a chop veto. Backward-compatible
            (added members only).
   v1.21.0 — SEAM SCOPE MIGRATION, BATCH 4 — COMPLETE (execution plan 1.4). The migration is DONE at
            13 SCOPE-keyed + 2 INSTRUMENT-keyed BY DESIGN — not stalled at 12. Batch 4 was a DECISION, not the
            pattern (per the plan). Principle: key a seam by whether its value varies with the CONSUMING chart's
            bar type.
              • ParticipationState → SCOPE. RVOL = this chart's bar volume ÷ typical, so a 150-tick RVOL ≠ a
                TBars RVOL — genuinely per-chart. Migrated (legacy instrument-arg Set delegates to the scope one);
                Council consults it by scope. ⇒ load a Participation indicator on each chart that runs a Council.
              • ClockState → KEEP INSTRUMENT-KEYED. Session phase is bar-type-independent; two charts publish
                identical values to key "GC" (correct by construction). Documented in-seam; do NOT scope it.
              • IntermarketState → KEEP INSTRUMENT-KEYED. A macro lean derived from OTHER instruments (ZN/ZB),
                independent of the consuming chart's bar type. Documented in-seam; do NOT scope it.
            The Council still consults Clock + Intermarket by bare instrument, deliberately.
   v1.20.0 — SEAM SCOPE MIGRATION, BATCH 3 (execution plan 1.4). 12 of 15 seams migrated.
            MIGRATED: EyeVerdict · LiquidityState · LevelState · MtfState — all cleanly per-chart, so all key by
            SCOPE. Each state class gained Scope/Bartype; each store is now a shared SeamStore<T> with the
            scope→instrument shim + a heartbeat (Touch*/Clear*Scope). EyeVerdict + LiquidityState use the
            batch-1 style (a legacy instrument-arg Set overload that delegates to the scope one); LevelState +
            MtfState are object-form and key on `s.Scope ?? s.Instrument` (no signature change). Council now
            consults all four by its own scope. REMAINING (3): ClockState (session-derived — identical for every
            chart of an instrument; may belong instrument- or globally-keyed) · ParticipationState · IntermarketState
            (derived from OTHER instruments — its scope is arguably the consuming chart). Those three need a
            DECISION, not the pattern.
   v1.19.1 — `CouncilState.HasEdge` gates on SizeMult, not Conviction. Council v1.2.0 separated conviction
            (pure AGREEMENT) from context damping (which now lives in SizeMult), so a below-floor or hostile-context
            verdict keeps a non-zero Conviction. The old test `Conviction > 0` therefore reported HasEdge TRUE with
            SizeMult 0 — and SentinelBridge computes Math.Max(1, BaseQty × SizeMult), so it would have fired a
            ONE-LOT on a stand-down. Anything asking "may I trade this" must consult the size: it is the only
            number that can say no.
   v1.19.0 — SEAM SCOPE MIGRATION, BATCH 2 + THE SENSOR HEARTBEAT (execution plan 1.4). 8 of 15 seams migrated.
            MIGRATED: BrickState · CompressionState · WaeState · GodReversalState.
            The three object-form seams (Compression/Wae/GodReversal) needed NO signature change — their key is
            now `s.Scope ?? s.Instrument`, so an un-migrated publisher keeps working untouched. BrickState is
            published by a BARS TYPE, whose scope is simply ScopeOf(bars.Instrument, bars.BarsPeriod); it is
            driven per tick, so it alone needs no heartbeat.
            NEW — `SeamStore<T>.Touch(scope)` + `TouchAdx/Trend/Cci/Envelope/Compression/Wae/GodReversalState`.
            WHY: an OnBarClose sensor only refreshes its seam when a bar closes. In a quiet market bars close
            slowly, the seam ages past the Council's StaleSec, and a perfectly healthy voter silently drops out
            of the roster — observed live as a FULLY LOADED chart reporting "roster 3/10" while its indicators
            were all present and correct. The Council has heartbeated its own verdict since v1.0.0; its sensors
            never did. Seven publishers now re-stamp the cached reading on incoming quotes (throttled 5 s,
            realtime-only, no recompute). Touch() takes the EXACT key and never the scope→instrument shim: a
            heartbeat must refresh the reading it OWNS, never adopt another chart's.
            ⚠ SentinelBrickCounter keeps a deliberate bare-instrument FALLBACK — reading a brick type running on
            another chart is that tool's entire purpose; the fallback resolves only when exactly one brick scope
            exists for the instrument, else fails closed.
            STILL INSTRUMENT-KEYED (batches 3-4): Eye · Liquidity · Clock · Participation · Level · Mtf ·
            Intermarket. (Clock is session-derived and may belong keyed by instrument — decide, don't assume.)
   v1.18.0 — SENSOR SEAMS MIGRATE TO SCOPE KEYS (execution plan 1.4 — BATCH 1 of 4). v1.15.0 gave CouncilState
            scope keys; the SENSORS it fuses stayed keyed by bare instrument, which is the half that actually
            feeds the verdict. Two GC charts overwrote each other's ADX/Trend/CCI/Envelope readings every bar,
            so a Council could fuse the OTHER chart's sensors and call it confluence.
            New private `SeamStore<T>` — ONE keyed store replacing fifteen hand-written copies of dictionary +
            lock + expiry + resolver (a bug fixed in one stayed broken in the other fourteen). Its Get() resolves:
              1. exact key                     — a migrated publisher consulted by scope. The normal path.
              2. scope asked, instrument stored — a publisher not yet migrated. THIS RUNG IS WHAT MAKES A BATCHED
                 MIGRATION SAFE: a scope-aware consumer keeps finding a legacy sensor instead of going blind
                 between F5s. Temporary; it disappears when the last publisher moves.
              3. bare instrument asked, scopes stored — resolve only if EXACTLY ONE scope carries it, else null
                 + a throttled log. FAIL-CLOSED: "I don't know which chart you mean" must never be answered with
                 "here's whichever wrote last."
            MIGRATED (batch 1, the four price-derived voters): AdxState · TrendState · CciState · EnvelopeState.
            Each keeps a LEGACY instrument-keyed overload that delegates, so nothing breaks mid-migration.
            Publishers moved: ADXPro · SentinelTrend · WoodiesCCIPro · VolEnvelope. Consumers moved: Council
            (consults by its own scope) · SentinelTrend + SentinelTrendStrategy (ADX consult) · Cockpit (hands
            every seam the SCOPE — the shim resolves the un-migrated ones, so one key works across a half-
            migrated tree; passing the bare instrument would fail closed on every migrated seam).
            STILL INSTRUMENT-KEYED (batches 2-4): Eye · Liquidity · Brick · Clock · Participation · Level · Mtf ·
            Compression · Intermarket · Wae · GodReversal.
   v1.17.0 — `Conditions` — the missing abstraction behind EVERY "warn once" bug in this suite. An audit found
            three distinct kinds of thing all written as `if (set.Add(key)) Warn();`, and only one of them is
            correctly a latch: an ACTION latch ("do this once", `_hardFlattened` — correct) · a TRANSITION log
            ("say when it changed", `_govPrevStatus` — correct) · and a CONDITION ALERT ("something is wrong
            NOW"), which must debounce transients, report, keep RE-STATING on a cooldown while it stays true,
            and auto-clear on resolve. Every condition alert got that last part wrong, in all three possible
            ways: naked-position had no debounce (a stop mid-modify looked naked → 160 false CRITICALs);
            orphan-orders' latch was deleted every scan (an alert every 2 s); scope contention and ambiguous
            scope latched forever (reported once, then permanently blind — while ambiguity fails CLOSED on
            every call, so a Bridge stands down indefinitely on a reason logged hours ago).
            `Conditions.ShouldReport(key, isTrue, debounceSec, cooldownSec)` + `Clear`/`ClearPrefix`/`IsActive`/
            `ActiveFor`. Wired here: ambiguous scope and scope contention (both re-state ever
```

