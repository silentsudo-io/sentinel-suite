// ─────────────────────────────────────────────────────────────────────────────
// This Source Code Form is subject to the terms of the Mozilla Public License,
// v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
// one at https://mozilla.org/MPL/2.0/.
//
// Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
// ─────────────────────────────────────────────────────────────────────────────
// ═════════════════════════════════════════════════════════════════════════════
//  SentinelCore — shared infrastructure for the Sentinel Suite (NT8)
//  File: SentinelCore_v1_0_0.cs
//  Version: v1.0.0
// ─────────────────────────────────────────────────────────────────────────────
//  WHAT THIS IS  (see Docs/ROADMAP.md, memory: sentinel-suite-architecture)
//    The ONE intentional shared dependency of the Sentinel Suite. Every Sentinel
//    tool (Copy, Log, Risk, Lens, Arc, Eye) is an INDEPENDENT headless AddOnBase
//    service — they do NOT depend on each other. They depend only on THIS: a small,
//    stable core that carries the suite-wide plumbing that must be shared to be useful:
//      • the SHARED KILL-SWITCH  (one flip halts every tool that trades/acts)
//      • the FEED-HEALTH GATE    (a pluggable per-account "is this feed safe?" probe)
//      • the SETTINGS DIRECTORY  (one place tools persist their config)
//      • a common LOG helper      (tagged output to the NinjaScript Output window)
//
//  DELIBERATE VERSIONING EXCEPTION (flagged — deviates from the per-file policy):
//    The CLASS is named `SentinelCore` WITHOUT a version suffix, even though the FILE
//    is `SentinelCore_v1_0_0.cs`. Rationale: every tool references this class BY NAME;
//    version-suffixing the class (SentinelCore_v1_0_0) would break every consumer on
//    each bump. Shared infrastructure must have a STABLE symbol. So for THIS file only:
//    bump the file name + changelog on change, but keep the class name `SentinelCore`
//    stable and STRICTLY BACKWARD-COMPATIBLE (add members; don't remove/rename). If a
//    breaking change is ever unavoidable, that's the day it earns a V2 class name.
//    (Confirm this exception is acceptable — see chat 2026-07-01.)
//
//  SHARED-INFRA VERSIONING NOTE: because the class name `SentinelCore` is STABLE (unversioned,
//    so consumers never break), this file is EDITED IN PLACE — it can NOT have coexisting
//    versioned copies (two `class SentinelCore` = CS0101 duplicate, breaks the whole compile).
//    Bump the `Version` const + add a changelog entry on change; do NOT copy to a new file name.
//
//  CHANGELOG
//    v1.36.0 — cnclVer PROVENANCE (additive; A1 fast-follow). CouncilState gains `CouncilVersion` — the Council's OWN
//             version that produced the verdict — carried via a TRAILING OPTIONAL param on the richest SetCouncilState
//             overload (back-compat: every existing caller compiles unchanged, the field is null when not passed). The
//             recorder stamps it as `cnclVer`, naming the exact fusion LOGIC per row — finer than `coreVer` (which only
//             moves on a SentinelCore bump, missing a Council-only change). Closes the A1 provenance thread.
//    v1.35.0 — HUMAN-READABLE BARTAG WRAP (additive; display-only). The machine tag "212203v8" is the immutable
//             scope KEY (changing it orphans the corpus/saved charts/roster folders), but a human never dialed
//             "212203" — they picked SentinelFlux at scale 8. New `BartypeName(id)` (id → registered/enum/"Type<id>"
//             name), `FriendlyBartag("212203v8") → "SentinelFlux 8"`, `FriendlyScope("GC.212203v8@FooBoo") →
//             "GC · SentinelFlux 8 · FooBoo"`. DISPLAY ONLY — the key is unchanged; consult/join still use the raw
//             tag. Consumed by the Cockpit/cards/dashboards so humans see speed+interval, machines keep precision.
//    v1.34.0 — HELM seam (additive; Phase 5 — the INTERDICTION layer, memory helm-interdiction-layer). Helm lets a
//             human grab the wheel of a RUNNING automated actor without stopping it: it publishes an INTENT addressed
//             to an instanceKey and the owner executes it with its OWN order handles (Helm never touches an order —
//             the three managed-order lessons in CLAUDE.md converge on why). New: HelmVerb enum · HelmIntent (id +
//             expiry + verb + payload, with IsRiskReducing/IsRiskAdding for the asymmetric gate) · one-shot
//             SetHelmIntent/TakeHelmIntent (idempotent FIFO drain, expiry-guarded against replay) · PendingHelmIntents
//             peek · HelmState publish-back (SetHelmState/GetHelmState/AllHelmStates) so Helm's card renders reality ·
//             ClearHelm teardown. Keyed by instanceKey, exactly like the actor registry. Purely additive.
//    v1.31.0 — FluxState seam (additive). SentinelFlux (order-flow IMBALANCE bars type, id 212203) publishes its
//             net-flow direction / buy-sell pressure / flow-vs-price DIVERGENCE per closed bar. → the Council FLUX
//             voter (STATE, orthogonal ORDER-FLOW axis — not price-derived) + a divergence (absorption) size damp.
//             Scope-keyed like BrickState (a bars type's scope = ScopeOf(bars.Instrument, bars.BarsPeriod)).
//             VoterCatalog gains FLUX; Council fuses 22 voters. Spec: Docs/SENTINEL_FLUXBARS_SPEC.md.
//    v1.30.0 — TWO NEW VOTER SEAMS (additive; candidate-library novel-signals pass). VidyaState — SentinelVIDYA
//             Chande-CMO adaptive-MA trend → Council VDYA voter (STATE). HarmonicState — SentinelHarmonic XABCD
//             pattern completion → Council HARM voter (TRIGGER). VoterCatalog gains both; Council fuses 21 voters.
//    v1.29.0 — TrendArchitectState seam (additive). SentinelTrendArchitect (the MPL Pine port) publishes its
//             PRISM bias/signal + Trend-Regime-Gate → the Council ARCH voter (STATE). VoterCatalog gains ARCH;
//             Council fuses 19 voters. Backward-compatible (added members only).
//    v1.28.0 — FOUR NEW VOTER SEAMS (additive; the candidate-library Tier-2 voter pass, 2026-07-12). Each mirrors
//             the WaeState object-seam pattern (SeamStore + Set/Get/Touch/All, scope-keyed, auto-expiring):
//               • AdxvmaState     — SentinelADXVMA: ADX-volatility adaptive-MA trinary trend. Council AVMA voter (STATE).
//               • SuperTrendState — SentinelSuperTrend: ATR-band trailing-flip trend. Council SPRT voter (STATE).
//               • SarState        — SentinelParabolicSAR: Wilder SAR trend/stop. Council PSAR voter (STATE).
//               • ZScoreState     — SentinelZScore: (Close−SMA)/StdDev mean-reversion. Council ZSC voter (TRIGGER).
//             VoterCatalog gains the 4 rows; Council fuses 18 voters. Backward-compatible (added members only).
//    v1.27.0 — SYSTEM BUILDER substrate (additive; new partial SentinelCore.SystemBuilder.cs — spec
//             Docs/SENTINEL_SYSTEM_BUILDER_SPEC.md Phase 0). Nothing here changes existing behaviour.
//             (1) VoterCatalog — the tag → indicator-class / role / seam / default-weight+kind map (14
//             voters + 5 context axes), mirroring the Council's KnownVoters + SetDefaults. (2) RosterIO —
//             ONE reader/writer for Roster.conf: Read() reproduces the Council's exact cascade + parse,
//             Write() serialises a RosterDoc back atomically (temp + File.Replace), so the Council (reader)
//             and the System Builder (writer) can never drift on format. (3) Public VoterKind + SensorRole
//             enums + CatalogEntry / RosterLine / RosterDoc types. The Council will consume RosterIO next.
//    v1.26.0 — FIVE NEW SEAMS (additive; the installed-tree port harvest, 2026-07-12). Adds the two orthogonal
//             axes the suite lacked plus three new voices, each mirroring the WaeState object-seam pattern
//             (SeamStore + Set/Get/Touch/All, scope-keyed, auto-expiring, backward-compatible):
//               • FlowState        — SentinelFlow (FLOW axis): tick-rule CVD regime — Bias/Slope/RSquared/Strength,
//                                    price-vs-CVD Divergence, confirmed Signal. Council FLOW voter (STATE).
//               • ProfileState     — SentinelProfile (PROFILE axis): developing volume profile POC/VAH/VAL, value-area
//                                    Location (-1/0/+1), POC-reversion Signal, HVN/LVN proximity. Council context modulator.
//               • RegimeState      — SentinelRegime: K-means volatility-regime + Markov forward filter — Regime 0/1/2 +
//                                    per-regime probability. Council conviction/size MODULATOR (not directional).
//               • StructureState   — SentinelStructure: swing HH/HL·LH/LL structure Bias + break-of-structure. Council STRC voter (STATE).
//               • ExhaustionState  — SentinelExhaustion: Leledc consecutive-close exhaustion — reversal Signal/Dir pulse.
//                                    Council EXH voter (TRIGGER; mean-reversion voice, à la GREV).
//    v1.25.0 — IDENTITY (ML spec §10, additive). (1) CouncilState.EpisodeId — the episode primary key (a maximal
//             run of constant Bias), the join that closes fills → episode → verdict → excursion outcome; carried on
//             the full SetCouncilState overload. (2) ACTOR REGISTRY — RegisterActor/UnregisterActor/AllActors: "the
//             name is an interlock" — an armed actor REFUSES to run on an instanceKey collision (the same-scope+account
//             managed-position hazard), reference-checked release for the NT re-enable race. (3) Ledger.Order/Action/
//             Fill gain optional episode + instance context (§10.7) so Lens can join a fill to the verdict that caused
//             it. All additive — every prior caller compiles and behaves identically.
//    v1.24.0 — CouncilState DECISION VECTOR (ML spec §2.1, additive). CouncilState gains Votes/VoteW (per-voter
//             dir + effective weight), NetScore/ActiveW (signed, pre-normalization), and the orthogonal-axis
//             modulator context (ClockPhase/Rvol/MtfBias/LevelInPath/LevelName) — previously fused into the verdict
//             but invisible to every consumer. New SetCouncilState overload carries them; ALL prior overloads
//             delegate here with vector defaults (purely additive — behaviour-neutral). This is the input side the
//             offline Lab needs to FIT the Council's weights, not merely grade its output.
//    v1.23.0 - INTERNAL RUNTIME SPLIT (no API change; Docs/PRODUCT_LADDER.md sec 4-5). SentinelCore is now
//             a `partial class` spread across files for distribution tiering. EXTRACTED to
//             SentinelCore.Safety.cs: ALL of L2 (feed-health, CanAct/CanActInstrument/CanEnter, governor,
//             drawdown, profiles, session, sizing, order guards, GateEntry). EXTRACTED to
//             SentinelCore.Foundation.cs (so far): SettingsDir/SettingsFile/LogFile/Log/WriteLogFile.
//             Ledger + State stay Foundation (audit/persistence primitives). Same class, same call sites
//             -> ZERO consumer churn. VERIFIED bundle-clean: the F+L1 files have no call into Safety, so
//             the Skins/Sensors bundles compile with Safety.cs OMITTED. F5-verified per batch (2026-07-10).
//    v1.22.0 — NEW SEAM: StfState (SetStfState/GetStfState/TouchStfState/ClearStfScope/AllStfStates). Published by
//             SentinelStochasticTripleFilter_v1_0_0 (the Sentinel port of "Stochastic Triple Filter [ATP]"): the DonovanWall
//             Gaussian-Channel midline SLOPE (Bias, a non-CCI/ADX trend regime) + a Choppiness-Index REGIME flag
//             (Trending). SCOPE-keyed (a slope/chop reading varies with the chart's bar type). Wired into the
//             Council as a trend voter "STF" (enters at weight 0 — exploration) + a chop veto. Backward-compatible
//             (added members only).
//    v1.21.0 — SEAM SCOPE MIGRATION, BATCH 4 — COMPLETE (execution plan 1.4). The migration is DONE at
//             13 SCOPE-keyed + 2 INSTRUMENT-keyed BY DESIGN — not stalled at 12. Batch 4 was a DECISION, not the
//             pattern (per the plan). Principle: key a seam by whether its value varies with the CONSUMING chart's
//             bar type.
//               • ParticipationState → SCOPE. RVOL = this chart's bar volume ÷ typical, so a 150-tick RVOL ≠ a
//                 TBars RVOL — genuinely per-chart. Migrated (legacy instrument-arg Set delegates to the scope one);
//                 Council consults it by scope. ⇒ load a Participation indicator on each chart that runs a Council.
//               • ClockState → KEEP INSTRUMENT-KEYED. Session phase is bar-type-independent; two charts publish
//                 identical values to key "GC" (correct by construction). Documented in-seam; do NOT scope it.
//               • IntermarketState → KEEP INSTRUMENT-KEYED. A macro lean derived from OTHER instruments (ZN/ZB),
//                 independent of the consuming chart's bar type. Documented in-seam; do NOT scope it.
//             The Council still consults Clock + Intermarket by bare instrument, deliberately.
//    v1.20.0 — SEAM SCOPE MIGRATION, BATCH 3 (execution plan 1.4). 12 of 15 seams migrated.
//             MIGRATED: EyeVerdict · LiquidityState · LevelState · MtfState — all cleanly per-chart, so all key by
//             SCOPE. Each state class gained Scope/Bartype; each store is now a shared SeamStore<T> with the
//             scope→instrument shim + a heartbeat (Touch*/Clear*Scope). EyeVerdict + LiquidityState use the
//             batch-1 style (a legacy instrument-arg Set overload that delegates to the scope one); LevelState +
//             MtfState are object-form and key on `s.Scope ?? s.Instrument` (no signature change). Council now
//             consults all four by its own scope. REMAINING (3): ClockState (session-derived — identical for every
//             chart of an instrument; may belong instrument- or globally-keyed) · ParticipationState · IntermarketState
//             (derived from OTHER instruments — its scope is arguably the consuming chart). Those three need a
//             DECISION, not the pattern.
//    v1.19.1 — `CouncilState.HasEdge` gates on SizeMult, not Conviction. Council v1.2.0 separated conviction
//             (pure AGREEMENT) from context damping (which now lives in SizeMult), so a below-floor or hostile-context
//             verdict keeps a non-zero Conviction. The old test `Conviction > 0` therefore reported HasEdge TRUE with
//             SizeMult 0 — and SentinelBridge computes Math.Max(1, BaseQty × SizeMult), so it would have fired a
//             ONE-LOT on a stand-down. Anything asking "may I trade this" must consult the size: it is the only
//             number that can say no.
//    v1.19.0 — SEAM SCOPE MIGRATION, BATCH 2 + THE SENSOR HEARTBEAT (execution plan 1.4). 8 of 15 seams migrated.
//             MIGRATED: BrickState · CompressionState · WaeState · GodReversalState.
//             The three object-form seams (Compression/Wae/GodReversal) needed NO signature change — their key is
//             now `s.Scope ?? s.Instrument`, so an un-migrated publisher keeps working untouched. BrickState is
//             published by a BARS TYPE, whose scope is simply ScopeOf(bars.Instrument, bars.BarsPeriod); it is
//             driven per tick, so it alone needs no heartbeat.
//             NEW — `SeamStore<T>.Touch(scope)` + `TouchAdx/Trend/Cci/Envelope/Compression/Wae/GodReversalState`.
//             WHY: an OnBarClose sensor only refreshes its seam when a bar closes. In a quiet market bars close
//             slowly, the seam ages past the Council's StaleSec, and a perfectly healthy voter silently drops out
//             of the roster — observed live as a FULLY LOADED chart reporting "roster 3/10" while its indicators
//             were all present and correct. The Council has heartbeated its own verdict since v1.0.0; its sensors
//             never did. Seven publishers now re-stamp the cached reading on incoming quotes (throttled 5 s,
//             realtime-only, no recompute). Touch() takes the EXACT key and never the scope→instrument shim: a
//             heartbeat must refresh the reading it OWNS, never adopt another chart's.
//             ⚠ SentinelBrickCounter keeps a deliberate bare-instrument FALLBACK — reading a brick type running on
//             another chart is that tool's entire purpose; the fallback resolves only when exactly one brick scope
//             exists for the instrument, else fails closed.
//             STILL INSTRUMENT-KEYED (batches 3-4): Eye · Liquidity · Clock · Participation · Level · Mtf ·
//             Intermarket. (Clock is session-derived and may belong keyed by instrument — decide, don't assume.)
//    v1.18.0 — SENSOR SEAMS MIGRATE TO SCOPE KEYS (execution plan 1.4 — BATCH 1 of 4). v1.15.0 gave CouncilState
//             scope keys; the SENSORS it fuses stayed keyed by bare instrument, which is the half that actually
//             feeds the verdict. Two GC charts overwrote each other's ADX/Trend/CCI/Envelope readings every bar,
//             so a Council could fuse the OTHER chart's sensors and call it confluence.
//             New private `SeamStore<T>` — ONE keyed store replacing fifteen hand-written copies of dictionary +
//             lock + expiry + resolver (a bug fixed in one stayed broken in the other fourteen). Its Get() resolves:
//               1. exact key                     — a migrated publisher consulted by scope. The normal path.
//               2. scope asked, instrument stored — a publisher not yet migrated. THIS RUNG IS WHAT MAKES A BATCHED
//                  MIGRATION SAFE: a scope-aware consumer keeps finding a legacy sensor instead of going blind
//                  between F5s. Temporary; it disappears when the last publisher moves.
//               3. bare instrument asked, scopes stored — resolve only if EXACTLY ONE scope carries it, else null
//                  + a throttled log. FAIL-CLOSED: "I don't know which chart you mean" must never be answered with
//                  "here's whichever wrote last."
//             MIGRATED (batch 1, the four price-derived voters): AdxState · TrendState · CciState · EnvelopeState.
//             Each keeps a LEGACY instrument-keyed overload that delegates, so nothing breaks mid-migration.
//             Publishers moved: ADXPro · SentinelTrend · WoodiesCCIPro · VolEnvelope. Consumers moved: Council
//             (consults by its own scope) · SentinelTrend + SentinelTrendStrategy (ADX consult) · Cockpit (hands
//             every seam the SCOPE — the shim resolves the un-migrated ones, so one key works across a half-
//             migrated tree; passing the bare instrument would fail closed on every migrated seam).
//             STILL INSTRUMENT-KEYED (batches 2-4): Eye · Liquidity · Brick · Clock · Participation · Level · Mtf ·
//             Compression · Intermarket · Wae · GodReversal.
//    v1.17.0 — `Conditions` — the missing abstraction behind EVERY "warn once" bug in this suite. An audit found
//             three distinct kinds of thing all written as `if (set.Add(key)) Warn();`, and only one of them is
//             correctly a latch: an ACTION latch ("do this once", `_hardFlattened` — correct) · a TRANSITION log
//             ("say when it changed", `_govPrevStatus` — correct) · and a CONDITION ALERT ("something is wrong
//             NOW"), which must debounce transients, report, keep RE-STATING on a cooldown while it stays true,
//             and auto-clear on resolve. Every condition alert got that last part wrong, in all three possible
//             ways: naked-position had no debounce (a stop mid-modify looked naked → 160 false CRITICALs);
//             orphan-orders' latch was deleted every scan (an alert every 2 s); scope contention and ambiguous
//             scope latched forever (reported once, then permanently blind — while ambiguity fails CLOSED on
//             every call, so a Bridge stands down indefinitely on a reason logged hours ago).
//             `Conditions.ShouldReport(key, isTrue, debounceSec, cooldownSec)` + `Clear`/`ClearPrefix`/`IsActive`/
//             `ActiveFor`. Wired here: ambiguous scope and scope contention (both re-state every 600 s and end
//             their episode in `ClearCouncilScope`). The ambiguous path is guarded so the every-tick healthy path
//             allocates nothing and takes no lock. `ScopeWatch` keeps only the source HISTORY; Conditions owns
//             "when may this speak". NEXT: route the Risk service's naked/orphan alerts through it (needs a
//             debounce to stop the false CRITICALs) and give `Alerts` an account parameter.
//             RULE: a latch that never re-arms is indistinguishable from a detector that never fires.
//    v1.16.1 — SCOPE-CONTENTION DETECTOR, rebuilt. The old detector latched: `_councilContentionWarned` was a
//             HashSet that warned ONCE per scope for the life of the process. So after the first report it went
//             permanently blind — you fixed the contention and never learned whether it came back, and a
//             misconfiguration that appeared LATER was never reported at all. "Silence means healthy" is exactly
//             the failure mode the declared roster exists to kill; the detector must not embody it. (Found live:
//             two Council indicators on ONE NQ chart kept colliding for 20 minutes in total silence, detectable
//             only because their roster deviations logged as duplicate same-millisecond lines.) Now:
//               • A→B→A ALTERNATION within 300 s = PROOF, at any cadence. A replaced instance (F5, closed chart)
//                 never writes again, so only two LIVE publishers can alternate. This catches a slow publisher
//                 whose write gap exceeds the overlap window — which the old 5 s check silently missed.
//               • overlapping writes within 5 s = the fast path for the common case (kept).
//               • differing ROSTER MASKS corroborate independently and name the charts ("A sees 10/10, B sees
//                 2/10 — they are not the same chart's view"). A benign hand-off shares a mask; contention doesn't.
//               • RE-ARMS after a 600 s cooldown, so a persistent misconfiguration keeps saying so.
//             `ClearCouncilScope` drops the watch with the entry, so a new publisher never inherits a dead
//             chart's alternation history.
//    v1.16.0 — DECLARED ROSTER (execution plan 3.1 · ML spec §10.4). New `RosterInfo` + `CouncilState.Roster`
//             and a `SetCouncilState` overload carrying it. The Council's roster was EMERGENT — it fused whatever
//             seams happened to be fresh — so `Eye_v1_1_0` crashing on load meant `EYE` (weight 1.4, the heaviest
//             voter) abstained on all 332 recorded verdicts and NOTHING SAID SO. Under fail-open abstention a
//             crashed sensor is indistinguishable from a quiet one. Declaring the expected voter set makes the
//             absence reportable, and makes the model attributable: you can now tell THE MODEL apart from WHAT
//             HAPPENED TO BE LOADED ON THE CHART. The roster travels WITH the verdict (one overload, never a
//             separate setter) because "which voters spoke" is only meaningful about the verdict they produced.
//             Additive: the 15-param overload delegates with roster=null, and `CouncilState.RosterComplete`
//             reports true for a pre-roster publisher (nothing declared ⇒ nothing missing).
//    v1.15.0 — SCOPE KEYS (execution plan 1.1 — a CORRECTNESS fix, not a feature). Every seam was keyed by
//             MASTER INSTRUMENT NAME alone, so two charts on the SAME instrument but different bar types
//             (e.g. GC on TBars-6 and GC on 150-tick) each published into `_council["GC"]` — last writer
//             wins, every tick. A Bridge on chart A could read, and TRADE, the verdict computed on chart
//             B's bars. Nothing anywhere detected it.
//             FIX: a SCOPE = "<masterInstrument>.<barTag>" (e.g. "GC.69697v6"). New ScopeOf()/BarTag()
//             helpers; CouncilState is now keyed by scope and carries Scope/Bartype/BarTimeUtc/IsHistorical.
//             GetCouncilState() takes a scope OR (backward-compat) a bare instrument: an exact scope hit
//             wins; a bare instrument resolves ONLY when exactly one scope exists for it, else it returns
//             null and logs ONCE — fail-CLOSED, so an ambiguous fleet complains loudly instead of silently
//             trading the wrong chart. That shim is what lets publishers migrate ahead of consumers.
//             BarTag() also folds in BarsPeriod.Value2, which the Recorder's own tag omitted — so TBars 6-24
//             and TBars 6-48 no longer collapse to the same tag.
//             Backward-compatible (added members only; the legacy 11-arg SetCouncilState still compiles and
//             keys by instrument, exactly as before, until the Council migrates in step 1.2).
//    v1.14.0 — GOD REVERSAL publish/consult seam (SetGodReversalState/GetGodReversalState/AllGodReversalStates
//             + GodReversalState, w/ JustReversed/Aligned helpers). SentinelGodReversal publishes the candle-
//             grammar REVERSAL read (shaved/engulf/equal-high/doji-exhaustion/VI) gated at a Bollinger-band edge
//             — the reversal-timing axis the trend sensors don't carry — as a Signal PULSE + a HELD Dir + Quality
//             + named Setup. The Council gains a GREV voter (a MEAN-REVERSION voice; see Council header caveat).
//             Object-passing setter. Backward-compatible (added members only).
//    v1.13.0 — WAE publish/consult seam (SetWaeState/GetWaeState/AllWaeStates + WaeState, w/ Aligned helper).
//             Sentinel WAE (Waddah Attar Explosion) publishes its momentum histogram side (Bias), power, the
//             Bollinger-width Explosion line, the ATR DeadZone, IsExploding (the classic WAE trigger), and the
//             CONFIRMED directional breakout Signal (= IsExploding ? Bias : 0) — so the Council gains a
//             momentum-breakout VOTER. Object-passing setter. Backward-compatible (added members only).
//    v1.12.0 — INTERMARKET publish/consult seam (SetIntermarketState/GetIntermarketState/AllIntermarketStates
//             + IntermarketState, w/ Aligned helper). The Intermarket indicator reads a configurable set of
//             CORRELATED instruments (e.g. ZN/ZB for gold; another index for ES/NQ) with a per-ref polarity and
//             publishes a net directional LEAN for the chart instrument — genuinely orthogonal macro info the
//             price sensors don't carry. INSTRUMENT-AGNOSTIC: the reference symbols + correlation sign are the
//             indicator's config, so the seam just carries the resulting Lean. Object-passing setter. Backward-
//             compatible (added members only).
//    v1.11.0 — COMPRESSION publish/consult seam (SetCompressionState/GetCompressionState/AllCompressionStates
//             + CompressionState, w/ JustBroke/Aligned helpers). CompressionBase (a coil-base breakout detector)
//             publishes its breakout PULSE (+1/-1/0 this bar) + a HELD break direction (persists a few bars) +
//             the coil ratio / compressed / armed state — so the Council gains a breakout VOTER (it previously
//             only exposed a hidden Signal plot the Council couldn't see). Object-passing setter. Backward-
//             compatible (added members only).
//    v1.10.0 — LOCATION + MTF publish/consult seams (SetLevelState/GetLevelState/AllLevelStates + LevelState;
//             SetMtfState/GetMtfState/AllMtfStates + MtfState). Location (a chart indicator) publishes the key
//             STRUCTURAL LEVELS (VWAP + bands, prior-day H/L, opening range, initial balance, session H/L) +
//             the NEAREST level to price with an ATR-normalized distance — so the Council knows WHERE price is
//             (a breakout into PDH is a different trade than one in open air). MTF (a multi-series indicator)
//             publishes the higher-timeframe TREND ALIGNMENT (a bias per TF on a ladder → a consensus bias +
//             alignment score) — so the Council can penalise trading AGAINST the higher timeframes. Both are
//             orthogonal CONTEXT axes feeding the Council (the 3rd + 4th). Object-passing setters (like
//             FleetSlot/Rollover). Backward-compatible (added members only).
//    v1.9.0 — PARTICIPATION publish/consult seam (SetParticipationState/GetParticipationState/
//             AllParticipationStates + ParticipationState, w/ Backed helper). Participation (a chart
//             indicator) publishes per-instrument RELATIVE VOLUME (rvol vs a rolling/time-of-day typical),
//             a volume z-score + climax/dry-up flags — "is this move BACKED by participation?" The second
//             orthogonal axis feeding the Council: a MODULATOR (light volume damps conviction; it can only
//             penalise an unbacked move, never inflate). Volume is not purely price-derived, so it adds
//             genuinely independent information. Same publish/consult pattern; travels as double/bool so
//             the core never couples to the indicator's version. Backward-compatible (added members only).
//    v1.8.0 — CLOCK publish/consult seam (SetClockState/GetClockState/AllClockStates + ClockState, w/
//             IsOpenDrive/IsClose/InSession helpers). Clock (a chart SESSION-CONTEXT indicator) publishes
//             per-instrument session phase (Closed/OpenDrive/Midday/Close) + minutes-since-open +
//             minutes-to-close + dayOfWeek + an inKillWindow flag (near-close no-new-entries). The FIRST
//             ORTHOGONAL axis feeding the Council — a MODULATOR (scales conviction / gates the kill window),
//             not a directional voter. Same publish/consult pattern as every other seam; travels as
//             INT/bool so the core never couples to the Clock indicator's version. Backward-compatible.
//    v1.7.0 — COUNCIL publish/consult seam (SetCouncilState/GetCouncilState/AllCouncilStates + CouncilState,
//             w/ IsLong/IsShort/HasEdge/Aligned helpers). The Council (a chart CONFLUENCE ARBITER) FUSES
//             every published sensor seam (Trend + ADX + CCI + VolEnvelope + Liquidity + Brick + Eye) into
//             ONE per-instrument directional VERDICT: fused Bias (-1/0/1), Conviction (0..1 = how aligned
//             the FRESH voters are), suggested SizeMult (0 when vetoed), agree/disagree/voter tallies, and a
//             compact Reasons audit string. HARD VETOES (global/scoped kill + rollover + news-lockout + an
//             absorption wall blocking the intended side) zero the conviction. Consumers (GTrader21 / Bridge /
//             Deck / Copier / strategies) consult it instead of re-deriving confluence, and record the verdict
//             on each FIRE so Lens can grade which confluence actually paid. Same publish/consult pattern as
//             every other seam; everything travels as INT/double/string so the core never couples to the
//             Council indicator's version. Backward-compatible (added members only).
//    v1.6.1 — BrickState gains the LIVE COUNTDOWN fields (UpperPrice/LowerPrice + TicksToUpper/
//             TicksToLower/NearestTicksRemaining) so a generic on-chart counter HUD (SentinelBrickCounter)
//             can render "ticks to the next brick" off the seam for ANY brick bars type that publishes it
//             (SentinelTBars, SentinelTbarsCount, …) — replacing the per-bars-type TbarsCountCounterFeed.
//             Also adds SentinelCore.BrickLog — an async daily JSONL stream (<SettingsDir>\BrickLog\
//             brick-YYYY-MM-DD.jsonl) so the DATA these custom bars produce is durably logged (NT
//             regenerates custom bricks from ticks each load and never stores them, so without this there
//             is no brick record). Kept SEPARATE from the order Ledger (bricks are high-volume). REALTIME
//             callers only. Backward-compatible: SetBrickState signature extended (BrickState had one
//             same-session consumer). See [[sentinel-tbars-tool]].
//    v1.6.0 — BRICK / BAR-STATE publish/consult seam (SetBrickState/GetBrickState/AllBrickStates
//             + BrickState, w/ IsUp / AtrTicks / Aligned helpers). SentinelTBars (a Sentinel-graded
//             adaptive HA/Renko-hybrid BARS TYPE) publishes its per-instrument adaptive VOLATILITY
//             read — ATR (price units), the live with-trend / counter-trend brick offsets, brick
//             direction, density scale, and trend-persistence count — so consumers (GTrader21 / Eye /
//             strategies) can consult "what is the tape's current brick volatility + direction" without
//             re-deriving it. Same publish/consult pattern as Eye + ADX + Trend + CCI + Liquidity.
//             Direction travels as an INT (-1 Down / 1 Up) so the core never couples to the bars-type
//             version. Published only for NEAR-REALTIME bricks (a historical rebuild is skipped so a
//             stale brick is never stamped fresh). Backward-compatible (added members only).
//    v1.5.0 — CCI TREND publish/consult seam (SetCciState/GetCciState/AllCciStates + CciState, w/
//             Bias/Strong/TrendOn/Aligned helpers). WoodiesCCIPro (a Woodies CCI/Turbo trend-filter
//             oscillator, Sentinel-graded) publishes per-instrument persisted trend state (-2..+2) + CCI
//             values + slope + last entry signal so consumers (GTrader21 / Eye / strategies) can gate on
//             "Woodies trend is bull and not weakening." Same publish/consult pattern as Eye + VolEnvelope
//             + ADX + Trend + Liquidity. State travels as an INT so the core never couples to any indicator
//             version's enum. Backward-compatible (added members only).
//    v1.4.0 — LIQUIDITY WALLS publish/consult seam (SetLiquidityState/GetLiquidityState/AllLiquidityStates
//             + LiquidityState, w/ ResistanceAbove/SupportBelow/NearWall/BlocksEntry helpers). LiquidityWalls
//             (a chart order-flow ABSORPTION detector, ported from the TradingIQ "Liquidity Walls" study)
//             publishes per-instrument absorption z-score + side + the nearest active wall above/below price
//             so consumers (GTrader21 / Deck / Eye / strategies) can veto entries into a wall. Same publish/
//             consult pattern as Eye + VolEnvelope + ADX + Trend. Side travels as an INT (-1 support-below /
//             0 none / 1 resistance-above) so the core never couples to any indicator version's enum.
//             Backward-compatible (added members only).
//    v1.3.1 — TRAILING-DRAWDOWN publish/consult seam (SetDrawdownState/GetDrawdownState/AllDrawdownStates
//             + DrawdownState, and DrawdownAllowsEntry()). Completes the AccountProfile.DdAmount that was
//             flagged "(Risk trailing-DD, future)". The GOVERNOR tracks daily REALIZED P&L; THIS tracks
//             lifetime EQUITY (realized balance + OPEN P&L) vs the firm's trailing threshold — the #1 way
//             funded accounts die (giving back open profit and touching the trail by a tick). Risk (which
//             owns account P&L) computes peak-equity/floor/cushion and publishes here; CanEnter now also
//             consults it (blocks new entries when the cushion is thin) — so GTrader21/Deck/Copier get
//             trailing-DD protection FREE. Direction of enforcement travels as flags (Warn/EntryBlocked/
//             Breach) so the core never owns the firm-specific math. Fail-open (no state → allowed).
//             Backward-compatible (added members only).
//    v1.3.0 — TREND publish/consult seam (SetTrendState/GetTrendState/AllTrendStates + TrendState).
//             SentinelTrend (the unified ATR/CCI trailing-line indicator that SUPERSEDES the old
//             TrendMagic family) publishes per-instrument trailing direction + line price + signed
//             distance (ticks) + bars-in-trend so consumers (SentinelTrendStrategy / GTrader21 / Eye)
//             can consult "trend flipped up AND price is holding above the line." Same publish/consult
//             pattern as Eye + VolEnvelope + ADX. Direction travels as an INT (-1 Down / 0 Flat / 1 Up)
//             so the core never couples to any indicator version's enum. Backward-compatible (added only).
//    v1.2.0 — ADX REGIME publish/consult seam (SetAdxState/GetAdxState/AllAdxStates + AdxState).
//             ADXPro (a chart ADX/DI indicator) publishes per-instrument trend strength + directional
//             bias so consumers (GTrader21 / Eye / Copier / strategies) can gate on "trend ON + bias
//             agrees" or "ADX fading — don't add." Same publish/consult pattern as Eye + VolEnvelope.
//             Bias travels as an INT (-1 Bear / 0 Neutral / 1 Bull) so the core never couples to any
//             indicator version's enum. Backward-compatible (added members only).
//    v1.1.0 — HARDENING SUBSTRATES (Docs/SENTINEL_HARDENING_FRAMEWORK.md). Three additions, all
//             backward-compatible (added members only), all marked "(v1.1.0)" inline:
//               • ORDER GATE — the single pre-submit choke point: GateEntry() → GateDecision
//                 {Level,Reason,Size}, SizeForRisk()/TickValue() risk sizer, and a fat-finger rate
//                 guard (SetOrderGuards/NoteOrderSubmitted). Deck=fail-open, GTrader/Copier=fail-closed.
//               • STATE LEDGER (SentinelCore.Ledger) — one append-only daily JSONL event stream
//                 (<SettingsDir>\Ledger\ledger-YYYY-MM-DD.jsonl, async writes): Ledger.Order()/Action()/
//                 Fill() (fill carries intended-vs-actual price → adverse slip ticks).
//                 NOW READABLE: Ledger.ReadDay()/ReadRecent()/Parse() → typed Entry records (evt
//                 order|action|fill), so the dashboard Journal + Slippage tabs (and future audit views)
//                 are cheap VIEWS of this one stream — no parallel journal.
//               • INTENDED-STATE STORE (SentinelCore.State) — a tiny keyed atomic blob store
//                 (<SettingsDir>\State\<key>.json) so a tool's management state (trail high-water,
//                 breakeven-armed, active stop) SURVIVES A RESTART: Save/Load/Clear/Age + SaveMap/LoadMap.
//                 Consumer reconciles on restore (verify the account still holds the position).
//               • ALERTS (SentinelCore.Alerts) — two-tier Critical/Info channel + Recent()/Raised,
//                 audited to the Ledger.
//             Also: governor daily-reset CLOCK (resetHour/GovernorResetHour/Label) + AccountProfile
//             .HardEnforce (opt-in auto-flatten arm).
//    v1.0.9 — PROFILES now DO something: CanEnter also honors the account profile's SESSION window
//             (block entries outside it; exits always allowed) — so GTrader21 gets per-account session
//             gating FREE. New SizedQuantity(account, baseQty) = baseQty × profile.SizeScale ×
//             governor RecommendedSize, clamped to the profile's ContractLimit (≥1) — the one place
//             sizing math lives, for strategies to size entries. Fail-open (unprofiled → unchanged).
//    v1.0.8 — ACCOUNT PROFILES registry: the intuitive per-account config (firm preset OR custom —
//             size / DD type+amt / daily-loss / consistency-ratio / target / contract-limit / session).
//             Risk parses Sentinel\Profiles.conf → publishes here; the Governor derives its cap/loss
//             from the profile, so accounts are NOT hard-coded to TPT/Bulenox/Lucid. Consumers read
//             GetAccountProfile(). Backward-compatible (added members only).
//    v1.0.7 — CONSISTENCY GOVERNOR registry (Docs/CONSISTENCY_GOVERNOR_SPEC.md): per-ACCOUNT daily
//             prop-firm gate. A host (Risk) tracks each account's daily realized P&L vs its firm
//             cap (consistency) + loss-stop and publishes GovernorState; consumers consult
//             TradingAllowedToday(account) + RecommendedSize(account). CanEnter now also honors the
//             governor, so GTrader21 (Direct-EA) gets it FREE. Fail-open (no state → allowed).
//    v1.0.6 — CONFIG-USE registry: a running Sentinel-aware strategy that auto-read a lab .conf
//             publishes what it loaded (SetConfigUse) so the dashboard shows which INSTANCE is on
//             which config + TP/SL. Publish on apply, remove on Terminated. Backward-compatible.
//    v1.0.5 — PER-INSTRUMENT (SCOPED) KILL. A new root-keyed kill registry: killing one
//             instrument's root (e.g. "GC" on a lagging feed) blocks only actions on THAT
//             instrument, not the whole suite — so one bad feed no longer halts ES/NQ. Sentinel
//             Risk now engages these per-feed instead of the global switch. The GLOBAL kill-switch
//             is unchanged (manual "stop everything"). New: SetInstrumentKill/InstrumentKillEngaged/
//             InstrumentKillReason/AllInstrumentKills + InstrumentKillChanged event, and
//             CanActInstrument(instrument, acct) = CanAct + the scoped kill. CanEnter now routes
//             through CanActInstrument, so GTrader21 (already calling CanEnter) gets scoping for
//             FREE — no strategy change. Backward-compatible (added members only).
//    v1.0.4 — LIVE-PHASE ENTRY GATES (rollover + news) + a WATCH registry. Three additions, all
//             publish/consult like Eye + Fleet, all backward-compatible (added members only):
//               • ROLLOVER registry — a health tool (SentinelRisk) publishes days-to-roll + a
//                 "roll imminent" block per instrument; strategies/copier consult RolloverBlocked().
//               • NEWS-LOCKOUT registry — a health tool publishes an active news lockout window
//                 (FOMC/NFP/CPI…); consult NewsLockoutActive()/ActiveNewsLockoutFor().
//               • WATCH registry — a chart strategy REGISTERS the exact Instrument it trades so
//                 SentinelRisk watches that feed even when the account is FLAT (closes the
//                 "flat-leader stalled chart feed went uncaught" gap).
//             New CanEnter(instrument, acct, reason) = CanAct + rollover + news, the single
//             consult a Sentinel-aware strategy makes at entry. InstrumentRoot() helper added.
//    v1.0.3 — FLEET ORCHESTRATION registry: SentinelArc publishes a per-instrument "fleet plan"
//             (which strategies/instruments should trade on the leader, size, session window) +
//             live supervision status; Sentinel-aware strategies (GTrader21) consult SlotLive()
//             at entry time and gate their own entries on it. Same publish/consult pattern as Eye.
//             FAIL-OPEN for unmanaged instruments. Backward-compatible (added members only).
//    v1.0.2 — EYE QUALIFICATION registry: SentinelEye (a chart scanner) publishes a per-instrument
//             "qualified direction + score" verdict here; the Copier consults it (when its Eye-gate
//             is on) to mirror only Eye-qualified trades. Same publish/consult pattern as the
//             kill-switch + feed-health probe. Stale verdicts (closed chart) auto-expire by age.
//    v1.0.1 — OBSERVABILITY: Log() now also appends every line to a file I (Claude) can read:
//             <UserDataDir>\Sentinel\sentinel.log (timestamped). This closes the gap where our
//             Output-window text never reached NT's log/trace files — no more screenshotting the
//             Output window. Thread-safe append; rotates once at ~5 MB. Native NT order/execution/
//             position/exception data is already in NinjaTrader 8\log\log.*.txt.
//    v1.0.0 — initial core: shared kill-switch (+ change event), feed-health gate
//             (pluggable probe, default healthy), CanAct() combined gate, settings dir,
//             tagged Log helper. No NT UI, no account subscriptions of its own.
// ═════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.Sentinel
{
    /// <summary>
    /// Shared, stable suite infrastructure. Static by design — one process-wide instance
    /// of the kill-switch and health gate that all Sentinel tools consult. Keep SMALL.
    /// </summary>
    public static partial class SentinelCore
    {
        public const string Version = "1.36.0";   // v1.36.0 — CouncilState.CouncilVersion (cnclVer provenance) via optional SetCouncilState param; v1.35.0 bartag wrap; v1.34.0 HELM seam

        // ═════════════════════════════════════════════════════════════════════
        //  SCOPE (v1.15.0) — the coordinate every seam should be keyed by.
        //
        //  A SCOPE identifies ONE CHART's worth of context: "<masterInstrument>.<barTag>",
        //  e.g. "GC.69697v6". It is exactly the coordinate a model is defined over
        //  (instrument x bartype), so seam key == model scope == config path.
        //
        //  WHY: keying by instrument alone means two GC charts on different bar types
        //  overwrite each other's published state on every tick, and a consumer cannot
        //  tell which chart's bars produced the value it just read.
        //
        //  '.' is the separator because '|' is Profiles.conf's field separator and '#'/'@'
        //  are the instanceKey delimiters. Instrument names MAY contain '.' (e.g. "BRK.B");
        //  that is safe here because we never SPLIT a scope — CouncilState carries the
        //  Instrument and Bartype as their own fields, and lookup is by equality.
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>Stable bar-type tag for a BarsPeriod, e.g. "69697v6" or "0v150x24".</summary>
        /// <remarks>
        /// Uses the NUMERIC BarsPeriodType id, never <c>BarsPeriodType.ToString()</c>: for a CUSTOM bar type
        /// the name resolves inconsistently by load state (sometimes "TBC…", sometimes the numeric id), which
        /// once made two recorders on the SAME chart write two different tags. The (int) cast is always stable.
        /// Unlike the Recorder's older private BarTag(), this folds in <c>Value2</c> when non-zero — without it,
        /// TBars 6-24 and TBars 6-48 produce the same tag and would share a scope.
        /// </remarks>
        public static string BarTag(NinjaTrader.Data.BarsPeriod bp)
        {
            if (bp == null) return "unknown";
            try
            {
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                string s = ((int)bp.BarsPeriodType).ToString(ci) + "v" + bp.Value.ToString(ci);
                if (bp.Value2 != 0) s += "x" + bp.Value2.ToString(ci);
                var sb = new StringBuilder(s.Length);
                foreach (char c in s) if (char.IsLetterOrDigit(c)) sb.Append(c);
                return sb.Length > 0 ? sb.ToString() : "unknown";
            }
            catch { return "unknown"; }
        }

        /// <summary>The scope for a chart: "&lt;masterInstrument&gt;.&lt;barTag&gt;". Null/empty inputs yield null.</summary>
        public static string ScopeOf(NinjaTrader.Cbi.Instrument instr, NinjaTrader.Data.BarsPeriod bp)
        {
            try
            {
                if (instr == null || instr.MasterInstrument == null) return null;
                return ScopeOf(instr.MasterInstrument.Name, bp);
            }
            catch { return null; }
        }

        /// <summary>The scope for a master-instrument name + BarsPeriod.</summary>
        public static string ScopeOf(string masterInstrument, NinjaTrader.Data.BarsPeriod bp)
        {
            if (string.IsNullOrEmpty(masterInstrument)) return null;
            return masterInstrument + "." + BarTag(bp);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  PER-CHART LANE — the scope discriminator (v1.32.0)
        //  Two charts identical on instrument+bartype+size otherwise share ONE scope and clobber
        //  each other's seams (the SCOPE CONTENTION case). A LANE is an optional, human-named
        //  per-chart tag folded into the scope: "GC.212202v6x24@A". You set it once on a chart's
        //  Council (which calls RegisterLane keyed by its ChartControl); EVERY other seam
        //  publisher/consumer on that SAME ChartControl inherits it automatically through the
        //  ScopeOf(instr, bp, chartControl) overload — no per-indicator config. Empty lane ⇒ the
        //  bare scope (fully back-compat). Strategies/consumers that don't share a ChartControl
        //  target a lane explicitly via ScopeOfLane(instr, bp, laneString). Bar-type seams
        //  (BrickState/FluxState) intentionally stay BARE — a bars series is shared across charts,
        //  so its state is legitimately common; only chart-level seams (Council, sensors) lane.
        //  Keyed by `object` (the ChartControl reference) so Core needs no Gui/WPF dependency.
        // ─────────────────────────────────────────────────────────────────────
        private static readonly Dictionary<object,string> _lanes = new Dictionary<object,string>();
        private static readonly object _laneGate = new object();

        /// <summary>Register/update the lane for a chart, keyed by its ChartControl. Blank clears it.</summary>
        public static void RegisterLane(object chartControl, string lane)
        {
            if (chartControl == null) return;
            string clean = SanitizeLane(lane);
            lock (_laneGate)
            {
                if (string.IsNullOrEmpty(clean)) _lanes.Remove(chartControl);
                else _lanes[chartControl] = clean;
            }
        }

        /// <summary>Drop a chart's lane registration — call on Terminated so a stale ChartControl can't leak.</summary>
        public static void ClearLane(object chartControl)
        {
            if (chartControl == null) return;
            lock (_laneGate) { _lanes.Remove(chartControl); }
        }

        /// <summary>The lane registered for a chart's ChartControl, or "" if none.</summary>
        public static string LaneOf(object chartControl)
        {
            if (chartControl == null) return "";
            lock (_laneGate) { string v; return _lanes.TryGetValue(chartControl, out v) ? v : ""; }
        }

        /// <summary>Scope for a chart INCLUDING its registered lane ("GC.212202v6x24@A"; bare when no lane).
        /// Indicators pass their ChartControl so every seam on one chart shares one lane.</summary>
        public static string ScopeOf(NinjaTrader.Cbi.Instrument instr, NinjaTrader.Data.BarsPeriod bp, object chartControl)
        {
            return ComposeLane(ScopeOf(instr, bp), LaneOf(chartControl));
        }

        /// <summary>Scope with an EXPLICIT lane string — for strategies/consumers with no shared ChartControl.</summary>
        public static string ScopeOfLane(NinjaTrader.Cbi.Instrument instr, NinjaTrader.Data.BarsPeriod bp, string lane)
        {
            return ComposeLane(ScopeOf(instr, bp), SanitizeLane(lane));
        }

        private static string ComposeLane(string bareScope, string lane)
        {
            if (string.IsNullOrEmpty(bareScope)) return bareScope;
            return string.IsNullOrEmpty(lane) ? bareScope : bareScope + "@" + lane;
        }

        private static string SanitizeLane(string lane)
        {
            if (string.IsNullOrEmpty(lane)) return "";
            var sb = new StringBuilder(lane.Length);
            foreach (char c in lane) if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  HUMAN-READABLE BARTAG (v1.35.0) — the machine tag "212203v8" is the immutable scope KEY: it must
        //  stay numeric + stable so saved charts, corpus joins, and Models\<inst>\<bartag>\ folders never break
        //  (changing it orphans the corpus — the exact re-bake hazard). But a human never dialed "212203" — they
        //  picked SentinelFlux at scale 8. FriendlyBartag/FriendlyScope render the SAME tag as its human name for
        //  DISPLAY ONLY (Cockpit, cards, docs, dashboards). The key is unchanged; this is a presentation layer.
        //  Machine-precise underneath, human up front.
        // ─────────────────────────────────────────────────────────────────────
        private static readonly Dictionary<int, string> _bartypeNames = new Dictionary<int, string>
        {
            { 212201, "SentinelTBars" },       // adaptive HA/Renko brick engine (BrickState → BRK)
            { 212202, "SentinelTbarsCount" },  // plain brick + ticks-to-next HUD
            { 212203, "SentinelFlux" },        // order-flow imbalance bars (FluxState → FLUX)
            { 2016,   "ERP" },                 // legacy (ERP_Type_Bars)
            { 54321,  "EdsRetrace" },          // legacy (EdsRetraceBarsV2)
            { 69696,  "TBarsElse" },           // pre-Sentinel TBars lineage
            { 69697,  "TbarsCount" },          // pre-Sentinel TBars lineage
        };

        /// <summary>The human name for a BarsPeriodType id — a registered Sentinel/legacy name, else the built-in
        /// enum name, else "Type&lt;id&gt;". DISPLAY ONLY.</summary>
        public static string BartypeName(int id)
        {
            string n;
            if (_bartypeNames.TryGetValue(id, out n)) return n;
            try { if (Enum.IsDefined(typeof(NinjaTrader.Data.BarsPeriodType), id)) return ((NinjaTrader.Data.BarsPeriodType)id).ToString(); }
            catch { }
            return "Type" + id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>Render a machine bartag ("212203v8" / "212201v6x24") as a human label ("SentinelFlux 8" /
        /// "SentinelTBars 6/24"). Returns the raw tag if unparseable. DISPLAY ONLY — never a key.</summary>
        public static string FriendlyBartag(string bartag)
        {
            if (string.IsNullOrEmpty(bartag)) return bartag;
            try
            {
                int vi = bartag.IndexOf('v');
                if (vi <= 0) return bartag;
                int id;
                if (!int.TryParse(bartag.Substring(0, vi), out id)) return bartag;
                string rest = bartag.Substring(vi + 1);
                string val, val2 = null;
                int xi = rest.IndexOf('x');
                if (xi >= 0) { val = rest.Substring(0, xi); val2 = rest.Substring(xi + 1); }
                else val = rest;
                string label = BartypeName(id) + " " + val;
                if (!string.IsNullOrEmpty(val2)) label += "/" + val2;
                return label;
            }
            catch { return bartag; }
        }

        /// <summary>Render a machine scope ("GC.212203v8@FooBoo") as a human label ("GC · SentinelFlux 8 · FooBoo").
        /// Splits the @lane and the instrument (the bartag is the final '.'-segment, so a dotted instrument like
        /// "BRK.B" is safe), then friendly-names the bartag. DISPLAY ONLY — never a key.</summary>
        public static string FriendlyScope(string scope)
        {
            if (string.IsNullOrEmpty(scope)) return scope;
            try
            {
                string lane = "";
                string core = scope;
                int at = scope.LastIndexOf('@');
                if (at >= 0) { lane = scope.Substring(at + 1); core = scope.Substring(0, at); }
                int dot = core.LastIndexOf('.');   // bartag never contains '.', so this splits inst | bartag correctly
                if (dot <= 0) return scope;
                string inst   = core.Substring(0, dot);
                string bartag = core.Substring(dot + 1);
                string label  = inst + " · " + FriendlyBartag(bartag);
                if (!string.IsNullOrEmpty(lane)) label += " · " + lane;
                return label;
            }
            catch { return scope; }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  SHARED KILL-SWITCH — any tool (or the dashboard) flips it; every tool
        //  that acts must consult it (via CanAct or KillSwitchEngaged) before acting.
        // ─────────────────────────────────────────────────────────────────────
        private static volatile bool _kill;

        /// <summary>True = suite-wide halt. Tools must not place/mirror orders while set.</summary>
        public static bool KillSwitchEngaged { get { return _kill; } }

        /// <summary>Raised whenever the kill-switch toggles (dashboards refresh their indicator).</summary>
        public static event Action<bool> KillSwitchChanged;

        /// <summary>Set the shared kill-switch. Idempotent; fires KillSwitchChanged on a real change.</summary>
        public static void SetKillSwitch(bool engaged, string source)
        {
            if (_kill == engaged) return;
            _kill = engaged;
            Log("Core", "KILL-SWITCH " + (engaged ? "ENGAGED" : "released")
                + (source != null ? " by " + source : ""));
            try { Ledger.Action(engaged ? "kill-engaged" : "kill-released", null, source ?? ""); } catch { }
            try { Alerts.Critical("Kill-switch " + (engaged ? "ENGAGED" : "released"), source); } catch { }
            var h = KillSwitchChanged;
            if (h != null) { try { h(engaged); } catch { } }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  PER-INSTRUMENT (SCOPED) KILL (v1.0.5) — a halt scoped to ONE instrument root.
        //  Sentinel Risk engages this per-feed on a lag/stall breach, so a bad GC feed blocks
        //  only GC actions, not ES/NQ. The GLOBAL kill-switch above still halts everything.
        //  Keyed by instrument ROOT; value = the reason/source (for logs + dashboard).
        // ─────────────────────────────────────────────────────────────────────
        private static readonly Dictionary<string, string> _instrKill =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _instrKillLock = new object();

        /// <summary>Raised when a per-instrument kill toggles (root, engaged).</summary>
        public static event Action<string, bool> InstrumentKillChanged;

        /// <summary>Engage/release a SCOPED kill for one instrument's root. Idempotent.</summary>
        public static void SetInstrumentKill(string instrument, bool engaged, string source)
        {
            string root = InstrumentRoot(instrument);
            if (root.Length == 0) return;
            bool changed = false;
            lock (_instrKillLock)
            {
                bool had = _instrKill.ContainsKey(root);
                if (engaged && !had) { _instrKill[root] = source ?? "?"; changed = true; }
                else if (!engaged && had) { _instrKill.Remove(root); changed = true; }
            }
            if (changed)
            {
                Log("Core", "INSTRUMENT-KILL " + (engaged ? "ENGAGED" : "released") + " " + root
                    + (source != null ? " (" + source + ")" : ""));
                var h = InstrumentKillChanged;
                if (h != null) { try { h(root, engaged); } catch { } }
            }
        }

        /// <summary>True if this instrument's root is scoped-killed (does NOT include the global kill).</summary>
        public static bool InstrumentKillEngaged(string instrument)
        {
            string root = InstrumentRoot(instrument);
            if (root.Length == 0) return false;
            lock (_instrKillLock) { return _instrKill.ContainsKey(root); }
        }

        /// <summary>The scoped-kill reason for an instrument's root, or null if not killed.</summary>
        public static string InstrumentKillReason(string instrument)
        {
            string root = InstrumentRoot(instrument);
            if (root.Length == 0) return null;
            lock (_instrKillLock) { string s; return _instrKill.TryGetValue(root, out s) ? s : null; }
        }

        /// <summary>All currently scoped-killed roots → reason (for dashboard/state.json).</summary>
        public static List<KeyValuePair<string, string>> AllInstrumentKills()
        {
            lock (_instrKillLock) { return new List<KeyValuePair<string, string>>(_instrKill); }
        }


        /// <summary>Root symbol of a contract name: "ES 09-26" -> "ES", "MGC" -> "MGC". Upper-cased.</summary>
        public static string InstrumentRoot(string instrument)
        {
            if (string.IsNullOrEmpty(instrument)) return "";
            string s = instrument.Trim();
            int sp = s.IndexOf(' ');
            if (sp > 0) s = s.Substring(0, sp);
            return s.ToUpperInvariant();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  EYE QUALIFICATION — SentinelEye (a chart scanner indicator) PUBLISHES a per-instrument
        //  verdict ("is the GodTrades edge qualified, and which direction"); the Copier CONSULTS it
        //  (when its Eye-gate is enabled) so real followers copy only Eye-qualified trades. Publish/
        //  consult pattern mirrors the kill-switch + feed-health probe. Key = master instrument name
        //  (e.g. "GC"). Verdicts auto-expire by age so a closed chart's stale verdict never gates.
        // ─────────────────────────────────────────────────────────────────────
        public sealed class EyeVerdict
        {
            /// <summary>v1.20.0 — the CHART this verdict came from ("GC.69697v6x24").</summary>
            public string   Scope;
            public string   Bartype;
            public string   Instrument;
            public int      Direction;   // +1 long-qualified, -1 short-qualified, 0 none/neutral
            public double   Score;
            public string   Source;      // e.g. "Tick 300 BG"
            public DateTime UpdatedUtc;
        }

        private static readonly SeamStore<EyeVerdict> _eye =
            new SeamStore<EyeVerdict>("Eye", v => v.Instrument, v => v.UpdatedUtc, (v, t) => v.UpdatedUtc = t);

        /// <summary>LEGACY (pre-v1.20.0) publish — keys by bare instrument. Prefer the scope-aware overload.</summary>
        public static void SetEyeVerdict(string instrument, int direction, double score, string source)
            => SetEyeVerdict(instrument, null, instrument, direction, score, source);

        /// <summary>SentinelEye publishes its verdict for ONE SCOPE (one chart) — the Eye's simulated rows are
        /// chart-specific, so two charts on one instrument must not overwrite each other.</summary>
        public static void SetEyeVerdict(string scope, string bartype, string instrument,
                                         int direction, double score, string source)
        {
            if (string.IsNullOrEmpty(scope)) return;
            _eye.Set(scope, new EyeVerdict { Scope = scope, Bartype = bartype, Instrument = instrument,
                                             Direction = direction, Score = score,
                                             Source = source, UpdatedUtc = DateTime.UtcNow });
        }

        /// <summary>Latest verdict for a SCOPE (or a bare instrument, resolved only when unique).</summary>
        public static EyeVerdict GetEyeVerdict(string scopeOrInstrument, double maxAgeSec)
            => _eye.Get(scopeOrInstrument, maxAgeSec);

        /// <summary>Heartbeat: re-stamp the cached verdict so a quiet-market Eye doesn't age out of the roster.</summary>
        public static void TouchEyeVerdict(string scope) => _eye.Touch(scope);

        public static void ClearEyeScope(string scope) => _eye.ClearScope(scope);

        public static List<EyeVerdict> AllEyeVerdicts() => _eye.All();

        // ─────────────────────────────────────────────────────────────────────
        //  VOL-ENVELOPE REGIME — VolEnvelope (a chart envelope indicator) PUBLISHES its per-instrument
        //  volatility regime + stretch so consumers (Copier / Arc / strategies) can gate on it, e.g.
        //  "don't ADD in a squeeze" or "regime disagrees with the signal." Same publish/consult pattern
        //  as the Eye verdict. Key = master instrument name. Auto-expires by age.
        //  Regime travels as an INT (0=Squeeze 1=Range 2=TrendUp 3=TrendDown 4=Expansion) so SentinelCore
        //  never couples to any indicator version's enum type.
        // ─────────────────────────────────────────────────────────────────────
        public sealed class EnvelopeState
        {
            /// <summary>v1.18.0 — the CHART this reading came from ("GC.69697v6x24").</summary>
            public string   Scope;
            public string   Bartype;
            public string   Instrument;
            public int      Regime;            // 0=Squeeze 1=Range 2=TrendUp 3=TrendDown 4=Expansion
            public double   Stretch;           // σ beyond the near band (signed); 0 = inside the band
            public double   BandwidthPctile;   // 0..1, low = coiled / squeeze
            public double   MultUp, MultDown;  // per-side empirical multipliers (the asymmetry)
            public string   Source;            // e.g. "GC TBC50-1-0"
            public DateTime UpdatedUtc;

            public bool IsSqueeze => Regime == 0;
            public bool IsTrend   => Regime == 2 || Regime == 3;
        }

        private static readonly SeamStore<EnvelopeState> _env =
            new SeamStore<EnvelopeState>("Envelope", v => v.Instrument, v => v.UpdatedUtc, (v, t) => v.UpdatedUtc = t);

        /// <summary>LEGACY (pre-v1.18.0) publish — keys by bare instrument. Prefer the scope-aware overload.</summary>
        public static void SetEnvelopeState(string instrument, int regime, double stretch, double bandwidthPctile,
                                            double multUp, double multDown, string source)
            => SetEnvelopeState(instrument, null, instrument, regime, stretch, bandwidthPctile, multUp, multDown, source);

        /// <summary>VolEnvelope publishes its regime/stretch for ONE SCOPE (one chart: instrument x bartype).</summary>
        public static void SetEnvelopeState(string scope, string bartype, string instrument,
                                            int regime, double stretch, double bandwidthPctile,
                                            double multUp, double multDown, string source)
        {
            if (string.IsNullOrEmpty(scope)) return;
            _env.Set(scope, new EnvelopeState { Scope = scope, Bartype = bartype, Instrument = instrument,
                                                Regime = regime, Stretch = stretch,
                                                BandwidthPctile = bandwidthPctile, MultUp = multUp, MultDown = multDown,
                                                Source = source, UpdatedUtc = DateTime.UtcNow });
        }

        /// <summary>Latest envelope state for a SCOPE (or a bare instrument, resolved only when unique).</summary>
        public static EnvelopeState GetEnvelopeState(string scopeOrInstrument, double maxAgeSec)
            => _env.Get(scopeOrInstrument, maxAgeSec);

        /// <summary>Heartbeat: re-stamp the cached reading so an OnBarClose sensor doesn't go stale between bars.</summary>
        public static void TouchEnvelopeState(string scope) => _env.Touch(scope);

        public static List<EnvelopeState> AllEnvelopeStates() => _env.All();

        // ─────────────────────────────────────────────────────────────────────
        //  ADX REGIME (v1.2.0) — ADXPro (a chart ADX/DI indicator) PUBLISHES its per-instrument trend
        //  strength + directional bias so consumers (GTrader21 / Eye / Copier / strategies) can gate on
        //  it, e.g. "only enter when the trend is ON and bias agrees with the signal" or "don't ADD while
        //  ADX is fading." Same publish/consult pattern as the Eye verdict + VolEnvelope regime. Key =
        //  master instrument name. Auto-expires by age. Bias travels as an INT (-1=Bear 0=Neutral 1=Bull)
        //  so SentinelCore never couples to any indicator version's enum type.
        // ─────────────────────────────────────────────────────────────────────
        public sealed class AdxState
        {
            /// <summary>v1.18.0 — the CHART this reading came from ("GC.69697v6x24"). Equals Instrument for a
            /// legacy publisher that has not migrated.</summary>
            public string   Scope;
            public string   Bartype;
            public string   Instrument;
            public double   Adx;             // current ADX
            public double   DiPlus;
            public double   DiMinus;
            public int      Bias;            // -1=Bear 0=Neutral(weak) 1=Bull
            public double   Slope5;          // ADX change over the last 5 bars (>0 building, <0 fading)
            public bool     Strong;          // ADX >= the tool's strong-trend level
            public string   Source;
            public DateTime UpdatedUtc;

            public bool TrendOn  => Bias != 0;      // ADX >= trigger, with a direction
            public bool Building => Slope5 > 0;     // ADX rising
            /// <summary>True when this bias agrees with a signal direction (dir: +1 long, -1 short, 0 flat).</summary>
            public bool Aligned(int dir) => dir > 0 ? Bias > 0 : (dir < 0 ? Bias < 0 : Bias == 0);
        }

        private static readonly SeamStore<AdxState> _adx =
            new SeamStore<AdxState>("Adx", v => v.Instrument, v => v.UpdatedUtc, (v, t) => v.UpdatedUtc = t);

        /// <summary>LEGACY (pre-v1.18.0) publish — keys by bare instrument. Two charts on one instrument overwrite
        /// each other. Prefer the scope-aware overload.</summary>
        public static void SetAdxState(string instrument, double adx, double diPlus, double diMinus,
                                       int bias, double slope5, bool strong, string source)
            => SetAdxState(instrument, null, instrument, adx, diPlus, diMinus, bias, slope5, strong, source);

        /// <summary>ADXPro publishes its trend strength + bias for ONE SCOPE (one chart: instrument x bartype).
        /// Pass <paramref name="scope"/> from <see cref="ScopeOf(NinjaTrader.Cbi.Instrument, NinjaTrader.Data.BarsPeriod)"/>.</summary>
        public static void SetAdxState(string scope, string bartype, string instrument,
                                       double adx, double diPlus, double diMinus,
                                       int bias, double slope5, bool strong, string source)
        {
            if (string.IsNullOrEmpty(scope)) return;
            _adx.Set(scope, new AdxState { Scope = scope, Bartype = bartype, Instrument = instrument,
                                           Adx = adx, DiPlus = diPlus, DiMinus = diMinus,
                                           Bias = bias, Slope5 = slope5, Strong = strong,
                                           Source = source, UpdatedUtc = DateTime.UtcNow });
        }

        /// <summary>Latest ADX state for a SCOPE (or, for compatibility, a bare instrument — which resolves only
        /// when exactly one scope carries it). Null if none / stale beyond maxAgeSec (0 = never stale).</summary>
        public static AdxState GetAdxState(string scopeOrInstrument, double maxAgeSec)
            => _adx.Get(scopeOrInstrument, maxAgeSec);

        /// <summary>Heartbeat: re-stamp the cached reading so an OnBarClose sensor doesn't go stale between bars.</summary>
        public static void TouchAdxState(string scope) => _adx.Touch(scope);

        public static List<AdxState> AllAdxStates() => _adx.All();

        // ─────────────────────────────────────────────────────────────────────
        //  TREND (v1.3.0) — SentinelTrend (the unified ATR/CCI trailing-line indicator that SUPERSEDES
        //  the old TrendMagic family) PUBLISHES its per-instrument trailing direction + line price +
        //  signed distance so consumers (SentinelTrendStrategy / GTrader21 / Eye / strategies) can gate
        //  on it, e.g. "only enter when the trend just flipped up AND price is holding above the line"
        //  or "don't fade an established trend." Same publish/consult pattern as the Eye verdict +
        //  VolEnvelope regime + ADX. Key = master instrument name. Auto-expires by age. Direction travels
        //  as an INT (-1=Down 0=Flat 1=Up) so SentinelCore never couples to any indicator version's enum.
        // ─────────────────────────────────────────────────────────────────────
        public sealed class TrendState
        {
            /// <summary>v1.18.0 — the CHART this reading came from ("GC.69697v6x24").</summary>
            public string   Scope;
            public string   Bartype;
            public string   Instrument;
            public int      Direction;      // -1 Down / 0 Flat / 1 Up (the trailing-line regime)
            public double   TrendPrice;     // the trailing line value (support in an uptrend, resistance in a down)
            public double   DistanceTicks;  // signed: (price - line) in ticks; >0 = price above the line
            public int      BarsInTrend;    // bars since the last direction flip
            public bool     Flipped;        // true on the bar the direction just changed
            public double   Cci;            // the gating CCI value at publish time
            public bool     AdxAligned;     // an ADX bias was consulted AND agrees with Direction (false if unknown/off)
            public string   Source;
            public DateTime UpdatedUtc;

            public bool IsUp   => Direction > 0;
            public bool IsDown => Direction < 0;
            /// <summary>True when this trend direction agrees with a signal direction (dir: +1 long, -1 short, 0 flat).</summary>
            public bool Aligned(int dir) => dir > 0 ? Direction > 0 : (dir < 0 ? Direction < 0 : Direction == 0);
        }

        private static readonly SeamStore<TrendState> _trend =
            new SeamStore<TrendState>("Trend", v => v.Instrument, v => v.UpdatedUtc, (v, t) => v.UpdatedUtc = t);

        /// <summary>LEGACY (pre-v1.18.0) publish — keys by bare instrument. Prefer the scope-aware overload.</summary>
        public static void SetTrendState(string instrument, int direction, double trendPrice, double distanceTicks,
                                         int barsInTrend, bool flipped, double cci, bool adxAligned, string source)
            => SetTrendState(instrument, null, instrument, direction, trendPrice, distanceTicks,
                             barsInTrend, flipped, cci, adxAligned, source);

        /// <summary>SentinelTrend publishes its trailing direction/line for ONE SCOPE (one chart: instrument x bartype).</summary>
        public static void SetTrendState(string scope, string bartype, string instrument,
                                         int direction, double trendPrice, double distanceTicks,
                                         int barsInTrend, bool flipped, double cci, bool adxAligned, string source)
        {
            if (string.IsNullOrEmpty(scope)) return;
            _trend.Set(scope, new TrendState { Scope = scope, Bartype = bartype, Instrument = instrument,
                                               Direction = direction, TrendPrice = trendPrice,
                                               DistanceTicks = distanceTicks, BarsInTrend = barsInTrend, Flipped = flipped,
                                               Cci = cci, AdxAligned = adxAligned, Source = source, UpdatedUtc = DateTime.UtcNow });
        }

        /// <summary>Latest trend state for a SCOPE (or a bare instrument, resolved only when unique).</summary>
        public static TrendState GetTrendState(string scopeOrInstrument, double maxAgeSec)
            => _trend.Get(scopeOrInstrument, maxAgeSec);

        /// <summary>Heartbeat: re-stamp the cached reading so an OnBarClose sensor doesn't go stale between bars.</summary>
        public static void TouchTrendState(string scope) => _trend.Touch(scope);

        public static List<TrendState> AllTrendStates() => _trend.All();

        // ─────────────────────────────────────────────────────────────────────
        //  STOCH TRIPLE FILTER (v1.22.0) — SentinelStochasticTripleFilter (the Sentinel port of "Stochastic Triple
        //  Filter [ATP]") PUBLISHES two independent things the Council was missing: the DonovanWall
        //  Gaussian-Channel midline SLOPE (a smoothed-price TREND regime that does NOT echo CCI/ADX) and a
        //  Choppiness-Index REGIME flag ("is the tape trending or ranging"). The Council consumes Bias as a
        //  TREND VOTER ("STF") and Trending as a CHOP VETO. Same publish/consult pattern as Trend/ADX/CCI.
        //  Key = SCOPE (one chart's worth of context) — a GC-150tick slope ≠ a GC-TBars slope. Auto-expires by
        //  age. Bias/Zone/Signal travel as INTs so the core never couples to any indicator version's enum.
        // ─────────────────────────────────────────────────────────────────────
        public sealed class StfState
        {
            public string   Scope;
            public string   Bartype;
            public string   Instrument;
            public int      Bias;       // Gaussian-Channel midline slope: -1 falling / 0 flat / +1 rising (the TREND vote)
            public bool     Trending;   // Choppiness Index below threshold (regime OK; false = choppy ⇒ chop veto)
            public double   Chop;       // the Choppiness Index value (0..100)
            public int      Zone;       // Stochastic zone: -1 oversold / 0 mid / +1 overbought
            public int      Signal;     // fully-filtered discrete signal this bar: +1 long / -1 short / 0
            public string   Source;
            public DateTime UpdatedUtc;

            public bool IsUp   => Bias > 0;
            public bool IsDown => Bias < 0;
            /// <summary>True when the GC-slope trend agrees with a signal direction (dir: +1 long, -1 short, 0 flat).</summary>
            public bool Aligned(int dir) => dir > 0 ? Bias > 0 : (dir < 0 ? Bias < 0 : Bias == 0);
        }

        private static readonly SeamStore<StfState> _stf =
            new SeamStore<StfState>("Stf", v => v.Instrument, v => v.UpdatedUtc, (v, t) => v.UpdatedUtc = t);

        /// <summary>LEGACY publish — keys by bare instrument. Prefer the scope-aware overload.</summary>
        public static void SetStfState(string instrument, int bias, bool trending, double chop, int zone, int signal, string source)
            => SetStfState(instrument, null, instrument, bias, trending, chop, zone, signal, source);

        /// <summary>SentinelStochasticTripleFilter publishes its GC-slope trend + chop regime for ONE SCOPE (one chart).</summary>
        public static void SetStfState(string scope, string bartype, string instrument,
                                       int bias, bool trending, double chop, int zone, int signal, string source)
        {
            if (string.IsNullOrEmpty(scope)) return;
            _stf.Set(scope, new StfState { Scope = scope, Bartype = bartype, Instrument = instrument,
                                           Bias = bias, Trending = trending, Chop = chop, Zone = zone,
                                           Signal = signal, Source = source, UpdatedUtc = DateTime.UtcNow });
        }

        /// <summary>Latest STF state for a SCOPE (or a bare instrument, resolved only when unique).</summary>
        public static StfState GetStfState(string scopeOrInstrument, double maxAgeSec)
            => _stf.Get(scopeOrInstrument, maxAgeSec);

        /// <summary>Heartbeat: re-stamp the cached reading so an OnBarClose sensor doesn't go stale between bars.</summary>
        public static void TouchStfState(string scope) => _stf.Touch(scope);

        public static void ClearStfScope(string scope) => _stf.ClearScope(scope);

        public static List<StfState> AllStfStates() => _stf.All();

        // ─────────────────────────────────────────────────────────────────────
        //  LIQUIDITY WALLS (v1.4.0) — LiquidityWalls (a chart order-flow ABSORPTION detector) PUBLISHES its
        //  per-instrument absorption state + the nearest active liquidity WALL above/below price so consumers
        //  (GTrader21 / Deck / Eye / strategies) can gate on it, e.g. "don't enter long into a resistance wall
        //  a few ticks overhead" or "a fresh support wall just formed below — favor the long." Same publish/
        //  consult pattern as the Eye verdict + VolEnvelope + ADX + Trend. Key = master instrument name.
        //  Auto-expires by age. AbsorbSide travels as an INT (-1 support-below / 0 none / 1 resistance-above)
        //  so the core never couples to any indicator version's enum. A wall ABOVE = resistance (favors short);
        //  a wall BELOW = support (favors long). Wall prices are the NEAR edge (the level price must clear).
        // ─────────────────────────────────────────────────────────────────────
        public sealed class LiquidityState
        {
            /// <summary>v1.20.0 — the CHART this reading came from ("GC.69697v6x24").</summary>
            public string   Scope;
            public string   Bartype;
            public string   Instrument;
            public double   Price;           // last price at publish
            public double   Zscore;          // current absorption z-score (>= trigger = an absorption event fired this bar)
            public int      AbsorbSide;      // -1 support-below / 0 none / 1 resistance-above (the last event's side)
            public double   WallAbove;       // near edge of the nearest active resistance wall above price (NaN = none)
            public double   WallBelow;       // near edge of the nearest active support wall below price (NaN = none)
            public double   DistAboveTicks;  // ticks from price up to WallAbove (>= 0), NaN if none
            public double   DistBelowTicks;  // ticks from price down to WallBelow (>= 0), NaN if none
            public int      ActiveWalls;     // count of currently-active (unbroken) walls
            public string   Source;
            public DateTime UpdatedUtc;

            public bool ResistanceAbove => !double.IsNaN(WallAbove);
            public bool SupportBelow    => !double.IsNaN(WallBelow);
            /// <summary>True when an active wall sits within <paramref name="ticks"/> of price on either side.</summary>
            public bool NearWall(double ticks) =>
                (!double.IsNaN(DistAboveTicks) && DistAboveTicks <= ticks) ||
                (!double.IsNaN(DistBelowTicks) && DistBelowTicks <= ticks);
            /// <summary>True when a wall within <paramref name="ticks"/> OPPOSES a signal direction
            /// (dir: +1 long → resistance overhead; -1 short → support underneath). Use to veto entries into a wall.</summary>
            public bool BlocksEntry(int dir, double ticks) =>
                (dir > 0 && !double.IsNaN(DistAboveTicks) && DistAboveTicks <= ticks) ||
                (dir < 0 && !double.IsNaN(DistBelowTicks) && DistBelowTicks <= ticks);
        }

        private static readonly SeamStore<LiquidityState> _liq =
            new SeamStore<LiquidityState>("Liquidity", v => v.Instrument, v => v.UpdatedUtc, (v, t) => v.UpdatedUtc = t);

        /// <summary>LEGACY (pre-v1.20.0) publish — keys by bare instrument. Prefer the scope-aware overload.</summary>
        public static void SetLiquidityState(string instrument, double price, double zscore, int absorbSide,
                                             double wallAbove, double wallBelow, double distAboveTicks,
                                             double distBelowTicks, int activeWalls, string source)
            => SetLiquidityState(instrument, null, instrument, price, zscore, absorbSide, wallAbove, wallBelow,
                                 distAboveTicks, distBelowTicks, activeWalls, source);

        /// <summary>LiquidityWalls publishes its absorption state + nearest walls for ONE SCOPE (one chart).</summary>
        public static void SetLiquidityState(string scope, string bartype, string instrument,
                                             double price, double zscore, int absorbSide,
                                             double wallAbove, double wallBelow, double distAboveTicks,
                                             double distBelowTicks, int activeWalls, string source)
        {
            if (string.IsNullOrEmpty(scope)) return;
            _liq.Set(scope, new LiquidityState { Scope = scope, Bartype = bartype, Instrument = instrument,
                                         Price = price, Zscore = zscore, AbsorbSide = absorbSide,
                                         WallAbove = wallAbove, WallBelow = wallBelow, DistAboveTicks = distAboveTicks,
                                         DistBelowTicks = distBelowTicks, ActiveWalls = activeWalls,
                                         Source = source, UpdatedUtc = DateTime.UtcNow });
        }

        /// <summary>Latest liquidity state for a SCOPE (or a bare instrument, resolved only when unique).</summary>
        public static LiquidityState GetLiquidityState(string scopeOrInstrument, double maxAgeSec)
            => _liq.Get(scopeOrInstrument, maxAgeSec);

        /// <summary>Heartbeat: re-stamp the cached reading so a quiet-market wall map doesn't age out.</summary>
        public static void TouchLiquidityState(string scope) => _liq.Touch(scope);

        public static void ClearLiquidityScope(string scope) => _liq.ClearScope(scope);

        public static List<LiquidityState> AllLiquidityStates() => _liq.All();

        // ─────────────────────────────────────────────────────────────────────
        //  CCI TREND (v1.5.0) — WoodiesCCIPro (a Woodies CCI/Turbo-CCI trend-filter oscillator) PUBLISHES
        //  its per-instrument persisted trend state + CCI values so consumers (GTrader21 / Eye / Copier /
        //  strategies) can gate on it, e.g. "only enter long when the Woodies trend is bull and not weakening."
        //  Same publish/consult pattern as the Eye verdict + VolEnvelope + ADX + Trend + Liquidity. Key =
        //  master instrument name. Auto-expires by age. TrendState travels as an INT (-2 strong-bear .. +2
        //  strong-bull) so the core never couples to any indicator version's enum.
        // ─────────────────────────────────────────────────────────────────────
        public sealed class CciState
        {
            /// <summary>v1.18.0 — the CHART this reading came from ("GC.69697v6x24").</summary>
            public string   Scope;
            public string   Bartype;
            public string   Instrument;
            public int      TrendState;    // -2 strong-bear / -1 bear / 0 neutral / 1 bull / 2 strong-bull (persisted)
            public double   MainCci;
            public double   TurboCci;
            public double   MainSlope;     // main CCI change over the tool's slope lookback (>0 rising)
            public int      Signal;        // last discrete entry signal this bar: +1 long / -1 short / 0 (ZLR/Hook)
            public bool     Weakening;     // the trend is weakening (momentum fading against the trend)
            public string   Source;
            public DateTime UpdatedUtc;

            public int  Bias   => Math.Sign(TrendState);      // -1 / 0 / 1
            public bool Strong => Math.Abs(TrendState) == 2;
            public bool TrendOn => TrendState != 0;
            /// <summary>True when this trend agrees with a signal direction (dir: +1 long, -1 short, 0 flat).</summary>
            public bool Aligned(int dir) => dir > 0 ? TrendState > 0 : (dir < 0 ? TrendState < 0 : TrendState == 0);
        }

        private static readonly SeamStore<CciState> _cci =
            new SeamStore<CciState>("Cci", v => v.Instrument, v => v.UpdatedUtc, (v, t) => v.UpdatedUtc = t);

        /// <summary>LEGACY (pre-v1.18.0) publish — keys by bare instrument. Prefer the scope-aware overload.</summary>
        public static void SetCciState(string instrument, int trendState, double mainCci, double turboCci,
                                       double mainSlope, int signal, bool weakening, string source)
            => SetCciState(instrument, null, instrument, trendState, mainCci, turboCci, mainSlope, signal, weakening, source);

        /// <summary>WoodiesCCIPro publishes its persisted trend state + CCI values for ONE SCOPE (one chart).</summary>
        public static void SetCciState(string scope, string bartype, string instrument,
                                       int trendState, double mainCci, double turboCci,
                                       double mainSlope, int signal, bool weakening, string source)
        {
            if (string.IsNullOrEmpty(scope)) return;
            _cci.Set(scope, new CciState { Scope = scope, Bartype = bartype, Instrument = instrument,
                                           TrendState = trendState, MainCci = mainCci,
                                           TurboCci = turboCci, MainSlope = mainSlope, Signal = signal, Weakening = weakening,
                                           Source = source, UpdatedUtc = DateTime.UtcNow });
        }

        /// <summary>Latest CCI trend state for a SCOPE (or a bare instrument, resolved only when unique).</summary>
        public static CciState GetCciState(string scopeOrInstrument, double maxAgeSec)
            => _cci.Get(scopeOrInstrument, maxAgeSec);

        /// <summary>Heartbeat: re-stamp the cached reading so an OnBarClose sensor doesn't go stale between bars.</summary>
        public static void TouchCciState(string scope) => _cci.Touch(scope);

        public static List<CciState> AllCciStates() => _cci.All();

        // ─────────────────────────────────────────────────────────────────────
        //  BRICK / BAR STATE (v1.6.0) — SentinelTBars (a Sentinel-graded adaptive HA/Renko-hybrid BARS
        //  TYPE) PUBLISHES its per-instrument adaptive VOLATILITY read + last brick direction so consumers
        //  (GTrader21 / Eye / strategies) can consult "what is the tape's current brick volatility +
        //  direction" without re-deriving it — e.g. size a stop off the live brick ATR, or refuse to add
        //  when the brick just flipped against the signal. Same publish/consult pattern as the Eye verdict
        //  + VolEnvelope + ADX + Trend + CCI + Liquidity. Key = master instrument name. Auto-expires by age.
        //  Direction travels as an INT (-1 Down / 1 Up) so the core never couples to any bars-type version.
        //  NOTE: a bars type runs on the DATA thread during BOTH historical rebuild and realtime; the
        //  publisher only calls this for near-realtime bricks, so a historical rebuild never stamps a stale
        //  brick as fresh (consumers still freshness-gate on maxAgeSec).
        // ─────────────────────────────────────────────────────────────────────
        public sealed class BrickState
        {
            /// <summary>v1.19.0 — the CHART this reading came from ("GC.69697v6x24").</summary>
            public string   Scope;
            public string   Bartype;
            public string   Instrument;
            public int      Direction;       // -1 Down / 1 Up (the last completed brick's direction)
            public double   Atr;             // adaptive ATR in PRICE units (EMA of brick true range)
            public double   TrendOffset;     // current with-trend brick offset (price units)
            public double   ReversalOffset;  // current counter-trend brick offset (price units)
            public double   DensityScale;    // 0..n multiplier on the base offsets (<1 = compressing → more bars)
            public int      SameDirCount;    // consecutive same-direction bricks (trend persistence)
            public int      BarsThisSession; // bricks printed so far this session
            public bool     PendingBreakout; // a breakout was mid-confirmation at publish time
            // live countdown (v1.6.1) — updated per tick so a HUD can show "ticks to next brick"
            public double   UpperPrice;      // barMax (the up-break boundary)
            public double   LowerPrice;      // barMin (the down-break boundary)
            public double   TicksToUpper;    // ticks from last price up to UpperPrice (>= 0)
            public double   TicksToLower;    // ticks from last price down to LowerPrice (>= 0)
            public double   NearestTicksRemaining; // min(TicksToUpper, TicksToLower)
            public string   Source;
            public DateTime UpdatedUtc;

            public bool IsUp   => Direction > 0;
            public bool IsDown => Direction < 0;
            /// <summary>ATR expressed in ticks for a given tick size (0 if tickSize invalid).</summary>
            public double AtrTicks(double tickSize) => tickSize > 0 ? Atr / tickSize : 0;
            /// <summary>True when the brick direction agrees with a signal direction (dir: +1 long, -1 short, 0 = don't-care → true).</summary>
            public bool Aligned(int dir) => dir > 0 ? Direction > 0 : (dir < 0 ? Direction < 0 : true);
        }

        private static readonly SeamStore<BrickState> _brick =
            new SeamStore<BrickState>("Brick", v => v.Instrument, v => v.UpdatedUtc, (v, t) => v.UpdatedUtc = t);

        /// <summary>LEGACY (pre-v1.19.0) publish — keys by bare instrument. Prefer the scope-aware overload.</summary>
        public static void SetBrickState(string instrument, int direction, double atr, double trendOffset,
                                         double reversalOffset, double densityScale, int sameDirCount,
                                         int barsThisSession, bool pendingBreakout,
                                         double upperPrice, double lowerPrice, double ticksToUpper,
                                         double ticksToLower, double nearestTicksRemaining, string source)
            => SetBrickState(instrument, null, instrument, direction, atr, trendOffset, reversalOffset, densityScale,
                             sameDirCount, barsThisSession, pendingBreakout, upperPrice, lowerPrice,
                             ticksToUpper, ticksToLower, nearestTicksRemaining, source);

        /// <summary>A brick bars type publishes its state for ONE SCOPE (one chart). Adaptive tools fill ATR/offsets/
        /// density; a plain tool may pass 0 for those. Upper/Lower + ticks-remaining drive the live countdown
        /// HUD and should be refreshed per tick. Near-realtime callers only (skip historical rebuild).
        /// NOTE the bars type IS the chart's bar type, so its scope is simply <c>ScopeOf(bars.Instrument,
        /// bars.BarsPeriod)</c> — and because it is driven per tick it needs no heartbeat.</summary>
        public static void SetBrickState(string scope, string bartype, string instrument,
                                         int direction, double atr, double trendOffset,
                                         double reversalOffset, double densityScale, int sameDirCount,
                                         int barsThisSession, bool pendingBreakout,
                                         double upperPrice, double lowerPrice, double ticksToUpper,
                                         double ticksToLower, double nearestTicksRemaining, string source)
        {
            if (string.IsNullOrEmpty(scope)) return;
            _brick.Set(scope, new BrickState { Scope = scope, Bartype = bartype, Instrument = instrument,
                                     Direction = direction, Atr = atr,
                                     TrendOffset = trendOffset, ReversalOffset = reversalOffset,
                                     DensityScale = densityScale, SameDirCount = sameDirCount,
                                     BarsThisSession = barsThisSession, PendingBreakout = pendingBreakout,
                                     UpperPrice = upperPrice, LowerPrice = lowerPrice, TicksToUpper = ticksToUpper,
                                     TicksToLower = ticksToLower, NearestTicksRemaining = nearestTicksRemaining,
                                     Source = source, UpdatedUtc = DateTime.UtcNow });
        }

        /// <summary>Latest brick state for a SCOPE (or a bare instrument, resolved only when unique).</summary>
        public static BrickState GetBrickState(string scopeOrInstrument, double maxAgeSec)
            => _brick.Get(scopeOrInstrument, maxAgeSec);

        public static List<BrickState> AllBrickStates() => _brick.All();

        // ─────────────────────────────────────────────────────────────────────
        //  FLUX (v1.31.0) — ORDER-FLOW imbalance seam, published by the SentinelFlux bars type (id 212203).
        //  Unlike every other seam here (Trend/CCI/ADX/Envelope/Brick are all PRICE-derived — they echo the same
        //  OHLC), Flux is sourced from the SIGNED TAPE: the net buy/sell imbalance that drove each bar to close.
        //  That makes the FLUX voter genuinely ORTHOGONAL to the price bloc (the Council's load-bearing gap), and
        //  the FLOW-vs-PRICE DIVERGENCE field surfaces absorption natively (flow one way, price the other).
        //  Scope-keyed exactly like BrickState — a bars type's scope is ScopeOf(bars.Instrument, bars.BarsPeriod),
        //  so two charts on different Flux params never clobber each other. Direction/pressure travel as INT/double
        //  so the core never couples to the bars-type version. Realtime-publish only (no lookahead into the corpus).
        // ─────────────────────────────────────────────────────────────────────
        public sealed class FluxState
        {
            public string   Scope;
            public string   Bartype;
            public string   Instrument;
            public int      FlowDir;        // -1 / 0 / +1 — sign of the just-closed bar's net signed imbalance (θ)
            public int      PriceDir;       // -1 / 0 / +1 — sign of (close − open) of that bar (for divergence)
            public double   Pressure;       // 0..1 — buyVol / (buyVol+sellVol) over the bar (0.5 = balanced)
            public double   Theta;          // current live signed imbalance of the FORMING bar
            public double   Threshold;      // current adaptive close threshold θ* (same units as Theta)
            public double   PercentToClose; // |Theta| / Threshold, clamped 0..1 (live countdown)
            public double   Cvd;            // running session cumulative volume delta (bonus tape read)
            public double   Atr;            // adaptive ATR in PRICE units (shared brick ATR EMA)
            public int      Divergence;     // 1 when FlowDir opposes PriceDir with meaningful θ (absorption), else 0
            public int      BarsThisSession;
            public string   Source;
            public DateTime UpdatedUtc;

            public bool IsUp   => FlowDir > 0;
            public bool IsDown => FlowDir < 0;
            public double AtrTicks(double tickSize) => tickSize > 0 ? Atr / tickSize : 0;
            /// <summary>True when net flow agrees with a signal direction (0 = don't-care → true).</summary>
            public bool Aligned(int dir) => dir > 0 ? FlowDir > 0 : (dir < 0 ? FlowDir < 0 : true);
            /// <summary>Divergence AGAINST a would-be trade side (dir): flow absorbing that side. Used as a size damp.</summary>
            public bool Absorbing(int dir) => Divergence != 0 && (dir > 0 ? FlowDir < 0 : (dir < 0 ? FlowDir > 0 : false));
        }

        private static readonly SeamStore<FluxState> _flux =
            new SeamStore<FluxState>("Flux", v => v.Instrument, v => v.UpdatedUtc, (v, t) => v.UpdatedUtc = t);

        /// <summary>The SentinelFlux bars type publishes its order-flow read for ONE SCOPE (one chart), per tick
        /// (live countdown) and per closed bar. Near-realtime callers only (a historical rebuild must not stamp a
        /// stale flow as fresh — consumers freshness-gate on maxAgeSec). scope = ScopeOf(bars.Instrument, bars.BarsPeriod).</summary>
        public static void SetFluxState(string scope, string bartype, string instrument,
                                        int flowDir, int priceDir, double pressure, double theta, double threshold,
                                        double percentToClose, double cvd, double atr, int divergence,
                                        int barsThisSession, string source)
        {
            if (string.IsNullOrEmpty(scope)) return;
            _flux.Set(scope, new FluxState { Scope = scope, Bartype = bartype, Instrument = instrument,
                                    FlowDir = flowDir, PriceDir = priceDir, Pressure = pressure,
                                    Theta = theta, Threshold = threshold, PercentToClose = percentToClose,
                                    Cvd = cvd, Atr = atr, Divergence = divergence,
                                    BarsThisSession = barsThisSession, Source = source, UpdatedUtc = DateTime.UtcNow });
        }

        /// <summary>Latest flow state for a SCOPE (or a bare instrument, resolved only when unique).</summary>
        public static FluxState GetFluxState(string scopeOrInstrument, double maxAgeSec)
            => _flux.Get(scopeOrInstrument, maxAgeSec);

        public static List<FluxState> AllFluxStates() => _flux.All();

        // ─────────────────────────────────────────────────────────────────────
        //  COUNCIL (v1.7.0) — the Council (a chart CONFLUENCE ARBITER) FUSES every published sensor seam
        //  (Trend + ADX + CCI + VolEnvelope + Liquidity + Brick + Eye) into ONE per-instrument directional
        //  VERDICT: a fused Bias (-1/0/1), a Conviction (0..1 = how aligned the FRESH voters are), a suggested
        //  SizeMult (0..1, 0 when vetoed), the agree/disagree/voter tallies, and a compact human-readable
        //  Reasons string (the AUDIT — why it decided). HARD VETOES (global/scoped kill / rollover /
        //  news-lockout / an absorption wall blocking the intended side) zero the conviction and set
        //  Vetoed+VetoReason. Consumers (GTrader21 / Bridge / Deck / Copier / strategies) consult
        //  GetCouncilState instead of re-deriving confluence, and record the verdict on each FIRE (Ledger/Log
        //  ctx) so Lens can grade which confluence actually paid. Same publish/consult pattern as every other
        //  seam. Key = master instrument name. Auto-expires by age. Everything travels as INT/double/string
        //  so the core never couples to the Council indicator's version.
        // ─────────────────────────────────────────────────────────────────────
        /// <summary>v1.16.0 — the DECLARED roster resolved against reality for one verdict (ML spec §10.4).
        /// The Council's roster used to be EMERGENT: it fused whatever seams happened to be fresh, so a sensor that
        /// crashed on load simply never voted and nothing anywhere said so (the Eye did exactly that for 332
        /// verdicts). Declaring the expected voter set turns a silent absence into a reported one, and makes the
        /// model attributable — you can tell THE MODEL apart from WHAT HAPPENED TO BE LOADED ON THE CHART.</summary>
        public sealed class RosterInfo
        {
            /// <summary>Declared voters that actually spoke this update, in declaration order (the rosterMask).</summary>
            public string Mask;
            /// <summary>Declared but absent/stale this update — the voters whose silence is now on the record.</summary>
            public string Missing;
            /// <summary>Present but NOT declared. Flagged, and deliberately NOT folded into the fusion.</summary>
            public string Unexpected;
            /// <summary>How many voters the declaration expects (includes w=0 exploration voters).</summary>
            public int    Declared;
            /// <summary>How many of them spoke this update.</summary>
            public int    Present;
            /// <summary>True when every declared voter spoke. Training filters on this.</summary>
            public bool   Complete;
            /// <summary>Where the declaration came from — a Roster.conf path, or "default" when derived from weights.</summary>
            public string Source;

            /// <summary>"8/10 — EYE, BRK missing" — the Cockpit's roster line.</summary>
            public override string ToString()
            {
                string s = Present + "/" + Declared;
                if (!string.IsNullOrEmpty(Missing))    s += " — " + Missing + " missing";
                if (!string.IsNullOrEmpty(Unexpected)) s += " — " + Unexpected + " unexpected";
                return s;
            }
        }

        public sealed class CouncilState
        {
            // v1.15.0 — scope identity. Scope = "<Instrument>.<Bartype>" and is the dictionary key.
            // Legacy publishers (pre-1.15.0 signature) set Scope = Instrument and Bartype = null.
            public string   Scope;
            public string   Bartype;
            /// <summary>Timestamp of the BAR this verdict was computed on (default when unknown).</summary>
            public DateTime BarTimeUtc;
            /// <summary>True when computed while replaying historical bars. UpdatedUtc is wall-clock even then,
            /// so the freshness gate CANNOT distinguish these — consumers that record must check this.</summary>
            public bool     IsHistorical;

            public string   Instrument;
            public int      Bias;          // -1 short / 0 flat / 1 long — the FUSED verdict (0 = no edge / vetoed)
            public double   Conviction;    // 0..1 — how aligned the fresh voters are (0 = none / vetoed)
            public double   SizeMult;      // 0..1 — suggested size multiplier (0 when vetoed)
            public int      Agree;         // # fresh voters agreeing with Bias
            public int      Disagree;      // # fresh voters opposing Bias
            public int      Voters;        // # sensor seams that had a FRESH reading (abstentions excluded)
            public bool     Vetoed;        // a hard gate zeroed the conviction
            public string   VetoReason;    // human-readable veto cause (null when not vetoed)
            public string   Reasons;       // compact "why" — pipe-joined per-sensor contributions (the audit)
            public string   Source;
            public DateTime UpdatedUtc;
            /// <summary>v1.16.0 — the declaration resolved against reality. Null when the publisher predates rosters.</summary>
            public RosterInfo Roster;

            // v1.24.0 — the DECISION VECTOR (ML spec §2.1): the machine-readable counterpart to Reasons. What the
            // Council actually SAW this update, so the offline Lab can FIT the weights, not merely grade the output.
            // Null on a publisher that predates 1.24.0. The trainer treats an absent Votes key as ABSTAIN — which is
            // NOT a zero vote and must never be imputed as one.
            public Dictionary<string,int>    Votes;      // tag → -1/0/+1, fresh voters only (incl. w=0 explorers)
            public Dictionary<string,double> VoteW;      // tag → effective weight applied this update
            public double NetScore;                      // Σ(dir × w) — SIGNED, pre-normalization
            public double ActiveW;                       // Σ(w) over voters that cast a direction
            // modulator context (the orthogonal axes — previously folded into the verdict but invisible to consumers)
            public int    ClockPhase = -1;               // -1 unknown · 0 Closed · 1 OpenDrive · 2 Midday · 3 Close
            public double Rvol = double.NaN;             // ParticipationState rvol, NaN = none
            public int    MtfBias;                       // MtfState consensus (0 = none / agree)
            public bool   LevelInPath;                   // a structural level lies in the bias's path
            public string LevelName;                     // that level's name (audit)

            // v1.24.0 — the EPISODE primary key (ML spec §10.2). A maximal run of constant Bias is ONE episode; this id
            // is stable across its life and changes only on a bias flip (NOT per tick). It is the join that closes
            // fills → episode → verdict → excursion outcome: the Recorder writes it, the Bridge tags orders with it and
            // records it to the Ledger. Null on a publisher that predates 1.24.0. Format: "<inst>-<yyyymmdd>-<NNNN>".
            public string EpisodeId;

            // v1.36.0 — the Council's OWN version that produced this verdict (A1 provenance, cnclVer). Lets a recorded
            // row name the exact fusion LOGIC — finer than coreVer (which only moves when SentinelCore bumps). Null on
            // a publisher that predates 1.36.0 / doesn't pass it.
            public string CouncilVersion;

            public bool IsLong  => Bias > 0;
            public bool IsShort => Bias < 0;
            /// <summary>True when every declared voter spoke. A pre-roster publisher reports true (nothing declared,
            /// so nothing is missing) — callers that REQUIRE a declaration must test <c>Roster != null</c> themselves.</summary>
            public bool RosterComplete => Roster == null || Roster.Complete;
            /// <summary>True = a real, non-vetoed, ACTIONABLE directional edge exists right now.
            ///
            /// v1.19.1 — gates on <c>SizeMult</c>, not on <c>Conviction</c>. Council v1.2.0 separated the two:
            /// conviction is now pure AGREEMENT (undamped), and context damping lives in SizeMult. So a verdict
            /// below the conviction floor, or in a hostile context, keeps a non-zero Conviction — and the old test
            /// (<c>Conviction &gt; 0</c>) would report HasEdge TRUE with SizeMult 0. `SentinelBridge` computes
            /// <c>Math.Max(1, BaseQty × SizeMult)</c>, so it would have fired a ONE-LOT on a stand-down. Anything
            /// that means "may I trade this" must consult the size, which is the only number that says no.</summary>
            public bool HasEdge => !Vetoed && Bias != 0 && SizeMult > 0;
            /// <summary>True when the fused verdict agrees with a signal direction (dir: +1 long, -1 short, 0 flat).</summary>
            public bool Aligned(int dir) => dir > 0 ? Bias > 0 : (dir < 0 ? Bias < 0 : Bias == 0);
        }

        // v1.15.0 — keyed by SCOPE ("GC.69697v6"), not by bare instrument. Legacy publishers key by
        // instrument; the resolver below handles both so publishers can migrate ahead of consumers.
        private static readonly Dictionary<string, CouncilState> _council =
            new Dictionary<string, CouncilState>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _councilLock = new object();
        // v1.16.1 — per-scope contention watch: the SOURCE HISTORY the detector reasons over. The throttling that
        // used to live here (a one-shot HashSet latch) is gone — see Conditions, which owns "when may this speak".
        private sealed class ScopeWatch
        {
            public string   LastSource;    // who wrote last
            public DateTime LastUtc;       // when
            public string   PrevSource;    // who wrote before that (only updated when the writer CHANGES)
        }
        private static readonly Dictionary<string, ScopeWatch> _councilWatch =
            new Dictionary<string, ScopeWatch>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Overlapping writes from two sources inside this window = contention (catches the common case fast).</summary>
        private const double ContentionOverlapSec = 5.0;
        /// <summary>A→B→A alternation inside this window = PROOF of contention, at any cadence. A replaced instance
        /// (F5, closed chart) never writes again, so only two LIVE publishers can alternate. This is what catches a
        /// slow publisher whose gap exceeds the overlap window.</summary>
        private const double ContentionAlternateSec = 300.0;
        /// <summary>Re-state a live contention at most this often, so a persistent misconfiguration keeps saying so.</summary>
        private const double ContentionRestateSec = 600.0;
        /// <summary>Re-state an unresolvable bare-instrument lookup at most this often. It fails CLOSED every call,
        /// so a consumer is standing down the whole time — that must not be explained only once.</summary>
        private const double AmbiguityRestateSec = 600.0;

        private static string ContentionKey(string scope)   { return "council|contention|" + scope; }
        private static string AmbiguityKey(string instrument) { return "council|ambiguous|" + instrument; }

        /// <summary>LEGACY (pre-v1.15.0) publish — keys by bare instrument. Still compiles and behaves exactly as
        /// before, so nothing breaks until each publisher adopts the scope-aware overload. Prefer that one.</summary>
        public static void SetCouncilState(string instrument, int bias, double conviction, double sizeMult,
                                           int agree, int disagree, int voters, bool vetoed, string vetoReason,
                                           string reasons, string source)
            => SetCouncilState(instrument, null, instrument, bias, conviction, sizeMult, agree, disagree, voters,
                               vetoed, vetoReason, reasons, source, default(DateTime), false);

        /// <summary>The Council publishes its fused verdict for ONE SCOPE (one chart: instrument x bartype).
        /// Pass <paramref name="scope"/> from <see cref="ScopeOf(NinjaTrader.Cbi.Instrument, NinjaTrader.Data.BarsPeriod)"/>.
        /// <paramref name="isHistorical"/> must be true while replaying historical bars — <c>UpdatedUtc</c> is wall-clock
        /// regardless, so it is the ONLY way a consumer can tell a replayed verdict from a live one.</summary>
        public static void SetCouncilState(string scope, string bartype, string instrument,
                                           int bias, double conviction, double sizeMult,
                                           int agree, int disagree, int voters, bool vetoed, string vetoReason,
                                           string reasons, string source, DateTime barTimeUtc, bool isHistorical)
            => SetCouncilState(scope, bartype, instrument, bias, conviction, sizeMult, agree, disagree, voters,
                               vetoed, vetoReason, reasons, source, barTimeUtc, isHistorical, null);

        /// <summary>v1.16.0 — as above, plus the resolved <paramref name="roster"/> (ML spec §10.4). The roster travels
        /// WITH the verdict, never separately: "which voters spoke" is only meaningful about the verdict they produced.</summary>
        public static void SetCouncilState(string scope, string bartype, string instrument,
                                           int bias, double conviction, double sizeMult,
                                           int agree, int disagree, int voters, bool vetoed, string vetoReason,
                                           string reasons, string source, DateTime barTimeUtc, bool isHistorical,
                                           RosterInfo roster)
            => SetCouncilState(scope, bartype, instrument, bias, conviction, sizeMult, agree, disagree, voters,
                               vetoed, vetoReason, reasons, source, barTimeUtc, isHistorical, roster,
                               null, null, 0, 0, -1, double.NaN, 0, false, null, null);

        /// <summary>v1.24.0 — the FULL publish, carrying the DECISION VECTOR (ML spec §2.1): the per-voter direction +
        /// weight the Council fused, the signed netScore / activeW, and the orthogonal-axis modulator context. This is
        /// what the offline Lab needs to FIT the weights (not merely grade conviction). Every earlier overload delegates
        /// here with vector defaults, so all prior callers compile and behave identically — purely additive.
        /// The dictionaries are COPIED into the state object: the Council reuses its own each update, so handing out a
        /// live reference would publish a verdict that mutates under the reader's feet on the next tick.</summary>
        public static void SetCouncilState(string scope, string bartype, string instrument,
                                           int bias, double conviction, double sizeMult,
                                           int agree, int disagree, int voters, bool vetoed, string vetoReason,
                                           string reasons, string source, DateTime barTimeUtc, bool isHistorical,
                                           RosterInfo roster,
                                           Dictionary<string,int> votes, Dictionary<string,double> voteW,
                                           double netScore, double activeW,
                                           int clockPhase, double rvol, int mtfBias, bool levelInPath, string levelName,
                                           string episodeId, string councilVersion = null)   // v1.36.0 — cnclVer (optional, back-compat)
        {
            if (string.IsNullOrEmpty(scope)) return;
            var now = DateTime.UtcNow;
            var s = new CouncilState { Scope = scope, Bartype = bartype, Instrument = instrument,
                                       BarTimeUtc = barTimeUtc, IsHistorical = isHistorical,
                                       Bias = bias, Conviction = conviction,
                                       SizeMult = sizeMult, Agree = agree, Disagree = disagree, Voters = voters,
                                       Vetoed = vetoed, VetoReason = vetoReason, Reasons = reasons,
                                       Source = source, UpdatedUtc = now, Roster = roster,
                                       Votes = votes != null ? new Dictionary<string,int>(votes) : null,
                                       VoteW = voteW != null ? new Dictionary<string,double>(voteW) : null,
                                       NetScore = netScore, ActiveW = activeW,
                                       ClockPhase = clockPhase, Rvol = rvol, MtfBias = mtfBias,
                                       LevelInPath = levelInPath, LevelName = levelName, EpisodeId = episodeId,
                                       CouncilVersion = councilVersion };
            bool contention = false; string otherSource = null, evidence = null, rosterHint = null;
            lock (_councilLock)
            {
                // SCOPE CONTENTION — a scope may have exactly ONE live publisher. Scope separates GC from NQ,
                // and GC-TBars from GC-150tick; it CANNOT separate two charts that share instrument AND bartype,
                // nor two Council indicators on the SAME chart. Such publishers differ only in which sensors they
                // see, so they write different verdicts into one key and a consumer reads whichever wrote last.
                // That is a misconfiguration to SURFACE, never to silently permit.
                CouncilState prev;
                _council.TryGetValue(scope, out prev);

                ScopeWatch w;
                if (!_councilWatch.TryGetValue(scope, out w)) { w = new ScopeWatch(); _councilWatch[scope] = w; }

                if (w.LastSource != null && !string.Equals(w.LastSource, source, StringComparison.Ordinal))
                {
                    double gap = (now - w.LastUtc).TotalSeconds;

                    // A→B→A: the writer changed, and we are the source that wrote before the last one. A replaced
                    // instance (F5, closed chart) never writes again, so only two LIVE publishers can alternate.
                    // Cadence-independent — this is what catches a slow publisher the overlap window would miss.
                    if (string.Equals(source, w.PrevSource, StringComparison.Ordinal) && gap < ContentionAlternateSec)
                    {
                        contention = true; otherSource = w.LastSource;
                        evidence = "alternating writes";
                    }
                    // Two different sources writing within seconds of each other — the common, obvious case.
                    else if (gap < ContentionOverlapSec)
                    {
                        contention = true; otherSource = w.LastSource;
                        evidence = "overlapping writes " + gap.ToString("0.0") + "s apart";
                    }

                    w.PrevSource = w.LastSource;
                }
                w.LastSource = source; w.LastUtc = now;

                // Independent corroboration: two publishers on one scope almost never see the same sensors, so
                // differing roster masks distinguish real contention from a benign hand-off. Names the charts, too.
                if (contention && roster != null && prev != null && prev.Roster != null
                    && !string.Equals(prev.Roster.Mask, roster.Mask, StringComparison.Ordinal))
                {
                    rosterHint = " Their rosters differ (" + source + " sees " + roster.Present + "/" + roster.Declared
                               + ", " + otherSource + " sees " + prev.Roster.Present + "/" + prev.Roster.Declared
                               + ") — they are not the same chart's view.";
                }

                _council[scope] = s;
            }

            // Contention is EVENT-shaped: it is only ever observed at write time, so there is no "false" reading to
            // feed back. Report on detection and let Conditions re-state it on a cooldown; ClearCouncilScope ends
            // the episode when a publisher tears down. (Conditions is taken OUTSIDE _councilLock — it has its own.)
            if (contention) contention = Conditions.ShouldReport(ContentionKey(scope), true, 0, ContentionRestateSec);

            if (contention)
                Log("Core", "SCOPE CONTENTION: two live publishers for '" + scope + "' ('" + source + "' and '"
                          + otherSource + "'; " + evidence + "). They overwrite each other every update and consumers "
                          + "read whichever wrote last." + (rosterHint ?? "")
                          + " Give the charts different bar types, close one, or remove the duplicate Council indicator.");
        }

        /// <summary>A publisher releases its scope on teardown, so an F5 or a closed chart doesn't leave a stale
        /// verdict behind. Only the OWNER may clear it — a mismatched source is ignored.</summary>
        public static void ClearCouncilScope(string scope, string source)
        {
            if (string.IsNullOrEmpty(scope)) return;
            string releasedInstrument = null;
            lock (_councilLock)
            {
                CouncilState prev;
                if (_council.TryGetValue(scope, out prev) && prev != null
                    && string.Equals(prev.Source, source, StringComparison.Ordinal))
                {
                    releasedInstrument = prev.Instrument;
                    _council.Remove(scope);
                    // Drop the watch with the entry: the scope is now unowned, so the next publisher starts clean
                    // and cannot inherit a stale alternation history from a chart that has gone away.
                    _councilWatch.Remove(scope);
                }
            }
            if (releasedInstrument == null) return;

            // End both condition episodes this scope could have been sustaining. Removing a publisher is the ONLY
            // way either can resolve: contention needs two writers, ambiguity needs two scopes on one instrument.
            // Without this they would stay "already reported" and the NEXT occurrence would wait out a cooldown.
            Conditions.Clear(ContentionKey(scope));
            Conditions.Clear(AmbiguityKey(releasedInstrument));
        }

        /// <summary>Latest Council verdict, or null if none / stale beyond maxAgeSec (0 = never stale).</summary>
        /// <param name="scopeOrInstrument">A SCOPE ("GC.69697v6") for an exact, unambiguous read — or, for backward
        /// compatibility, a bare instrument ("GC"), which resolves ONLY when exactly one scope exists for it.</param>
        /// <remarks>
        /// FAIL-CLOSED on ambiguity. Two GC charts publishing two scopes make a bare "GC" lookup meaningless, so it
        /// returns null and logs once rather than silently handing back whichever chart wrote last. Callers already
        /// treat null as "no verdict → stand down", which is the correct response to "I don't know which chart you mean".
        /// </remarks>
        public static CouncilState GetCouncilState(string scopeOrInstrument, double maxAgeSec)
        {
            if (string.IsNullOrEmpty(scopeOrInstrument)) return null;
            CouncilState s;
            bool ambiguous = false;           // decide inside the lock; LOG outside it (Log touches disk)
            lock (_councilLock)
            {
                // 1) exact scope (or a legacy instrument-keyed entry)
                if (!_council.TryGetValue(scopeOrInstrument, out s) || s == null)
                {
                    // 2) bare instrument → resolve ONLY if exactly one scope carries it
                    s = null;
                    foreach (var v in _council.Values)
                    {
                        if (v == null || !string.Equals(v.Instrument, scopeOrInstrument, StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (s != null)                                   // a second match ⇒ ambiguous
                        {
                            s = null; ambiguous = true;
                            break;
                        }
                        s = v;
                    }
                }
            }
            // v1.17.0 — a CONDITION, not a one-shot. This used to latch in a HashSet and warn once per key for the
            // life of the process, while every call kept returning null (fail-CLOSED): a Bridge stands down forever
            // and the reason was logged once, possibly hours earlier. It now RE-STATES while it remains true.
            // Only touched on the ambiguous path — this method runs every tick for every consumer, so the healthy
            // path must not allocate a key or take the Conditions lock. The episode is ended by ClearCouncilScope,
            // which is the only way a scope (and therefore the ambiguity) can disappear.
            bool warnNow = ambiguous
                && Conditions.ShouldReport(AmbiguityKey(scopeOrInstrument), true, 0, AmbiguityRestateSec);
            if (warnNow)
                Log("Core", "AMBIGUOUS SCOPE: several Council scopes publish for '" + scopeOrInstrument
                          + "'. A bare-instrument lookup cannot pick one — returning null (fail-closed). "
                          + "Consult by scope (SentinelCore.ScopeOf) instead.");
            if (s == null) return null;
            if (maxAgeSec > 0 && (DateTime.UtcNow - s.UpdatedUtc).TotalSeconds > maxAgeSec) return null;
            return s;
        }

        public static List<CouncilState> AllCouncilStates()
        {
            lock (_councilLock) { return new List<CouncilState>(_council.Values); }
        }

        /// <summary>Every scope currently publishing a Council verdict for this instrument (empty if none).
        /// Use when you must enumerate a fleet rather than resolve a single chart.</summary>
        public static List<string> CouncilScopesFor(string instrument)
        {
            var outp = new List<string>();
            if (string.IsNullOrEmpty(instrument)) return outp;
            lock (_councilLock)
            {
                foreach (var v in _council.Values)
                    if (v != null && string.Equals(v.Instrument, instrument, StringComparison.OrdinalIgnoreCase))
                        outp.Add(v.Scope);
            }
            return outp;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  CLOCK (v1.8.0) — Clock (a chart SESSION-CONTEXT indicator) PUBLISHES its per-instrument session
        //  phase so consumers (the Council / strategies) can MODULATE on time-of-day: which phase the session
        //  is in (0 Closed/pre-open · 1 Open-drive · 2 Midday · 3 Close), minutes-since-open, minutes-to-close,
        //  day-of-week, whether we're in-session, and an inKillWindow flag (inside the near-close no-new-entries
        //  window). This is a MODULATOR, not a directional vote — every base rate is conditional on session
        //  phase. Same publish/consult pattern as every other seam. Phase travels as an INT so the core never
        //  couples to the Clock indicator's version.
        //  ⚠ KEYED BY INSTRUMENT, NOT SCOPE — BY DESIGN (exec-plan 1.4 batch 4 decision, 2026-07-10). Session
        //  phase is bar-type-INDEPENDENT: two GC charts (a 150-tick and a TBars) are in the SAME session at the
        //  SAME phase, so both publish IDENTICAL values to key "GC" — the shared key is correct by construction,
        //  and scope-keying would only store N identical copies. Do NOT "finish the migration" here. (The lone
        //  edge case — two charts with DIFFERENT session templates — is a deployment error; run one Clock per
        //  instrument.) Consumers consult by bare instrument, deliberately. Contrast ParticipationState, whose
        //  value IS bar-type-dependent → scope-keyed.
        // ─────────────────────────────────────────────────────────────────────
        public sealed class ClockState
        {
            public string   Instrument;
            public int      Phase;         // 0 Closed/pre-open · 1 Open-drive · 2 Midday · 3 Close
            public int      MinsSinceOpen; // minutes since the session opened (-1 if not in session)
            public int      MinsToClose;   // minutes until the session closes (-1 if not in session)
            public int      DayOfWeek;     // 0=Sunday .. 6=Saturday (DateTime.DayOfWeek)
            public bool     InSession;     // currently inside the trading session
            public bool     InKillWindow;  // inside the near-close no-new-entries window
            public string   Source;
            public DateTime UpdatedUtc;

            public bool IsOpenDrive => Phase == 1;
            public bool IsMidday    => Phase == 2;
            public bool IsClose     => Phase == 3;
        }

        private static readonly Dictionary<string, ClockState> _clock =
            new Dictionary<string, ClockState>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _clockLock = new object();

        /// <summary>Clock publishes its per-instrument session phase (called each update when PublishState is on).</summary>
        public static void SetClockState(string instrument, int phase, int minsSinceOpen, int minsToClose,
                                          int dayOfWeek, bool inSession, bool inKillWindow, string source)
        {
            if (string.IsNullOrEmpty(instrument)) return;
            var s = new ClockState { Instrument = instrument, Phase = phase, MinsSinceOpen = minsSinceOpen,
                                     MinsToClose = minsToClose, DayOfWeek = dayOfWeek, InSession = inSession,
                                     InKillWindow = inKillWindow, Source = source, UpdatedUtc = DateTime.UtcNow };
            lock (_clockLock) { _clock[instrument] = s; }
        }

        /// <summary>Latest session-clock state for an instrument, or null if none / stale beyond maxAgeSec (0 = never stale).</summary>
        public static ClockState GetClockState(string instrument, double maxAgeSec)
        {
            if (string.IsNullOrEmpty(instrument)) return null;
            lock (_clockLock)
            {
                ClockState s;
                if (!_clock.TryGetValue(instrument, out s) || s == null) return null;
                if (maxAgeSec > 0 && (DateTime.UtcNow - s.UpdatedUtc).TotalSeconds > maxAgeSec) return null;
                return s;
            }
        }

        public static List<ClockState> AllClockStates()
        {
            lock (_clockLock) { return new List<ClockState>(_clock.Values); }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  PARTICIPATION (v1.9.0) — Participation (a chart indicator) PUBLISHES its per-instrument RELATIVE
        //  VOLUME read so consumers (the Council / strategies) can ask "is this move BACKED?" Rvol is the
        //  latest completed bar's volume ÷ a typical volume (rolling average, or time-of-day-normalized on
        //  time-based charts); VolZ is the volume z-score (Climax = blow-off, DryUp = participation vacuum).
        //  The second ORTHOGONAL axis feeding the Council — a MODULATOR (light volume damps conviction; it
        //  can only penalise an unbacked move, never inflate). Volume carries information price alone does
        //  not, so it is genuinely independent of the price-derived voters. Same publish/consult pattern as
        //  every other seam. Key = master instrument name. Auto-expires by age.
        // ─────────────────────────────────────────────────────────────────────
        public sealed class ParticipationState
        {
            /// <summary>v1.21.0 — the CHART this reading came from ("GC.69697v6x24"). RVOL is BAR-TYPE-DEPENDENT
            /// (a 150-tick RVOL ≠ a TBars RVOL), so unlike Clock/Intermarket this seam is SCOPE-keyed.</summary>
            public string   Scope;
            public string   Bartype;
            public string   Instrument;
            public double   Rvol;         // relative volume vs typical (1.0 = normal, >1 heavy, <1 light)
            public double   VolZ;         // volume z-score vs the recent distribution (climax detector)
            public bool     Climax;       // VolZ >= the tool's climax threshold (blow-off participation)
            public bool     DryUp;        // Rvol <= the tool's dry-up threshold (participation vacuum)
            public double   TypicalVol;   // the typical volume used for Rvol (diagnostic)
            public string   Source;
            public DateTime UpdatedUtc;

            /// <summary>True = the move is backed by at-or-above-normal participation.</summary>
            public bool Backed => Rvol >= 1.0;
        }

        private static readonly SeamStore<ParticipationState> _partic =
            new SeamStore<ParticipationState>("Participation", v => v.Instrument, v => v.UpdatedUtc, (v, t) => v.UpdatedUtc = t);

        /// <summary>LEGACY (pre-v1.21.0) publish — keys by bare instrument. Prefer the scope-aware overload.</summary>
        public static void SetParticipationState(string instrument, double rvol, double volZ, bool climax,
                                                 bool dryUp, double typicalVol, string source)
            => SetParticipationState(instrument, null, instrument, rvol, volZ, climax, dryUp, typicalVol, source);

        /// <summary>Participation publishes its relative-volume read for ONE SCOPE (one chart). RVOL is bar-type-
        /// dependent, so two charts on one instrument must not overwrite each other on a shared key.</summary>
        public static void SetParticipationState(string scope, string bartype, string instrument,
                                                 double rvol, double volZ, bool climax,
                                                 bool dryUp, double typicalVol, string source)
        {
            if (string.IsNullOrEmpty(scope)) return;
            _partic.Set(scope, new ParticipationState { Scope = scope, Bartype = bartype, Instrument = instrument,
                                             Rvol = rvol, VolZ = volZ, Climax = climax,
                                             DryUp = dryUp, TypicalVol = typicalVol, Source = source,
                                             UpdatedUtc = DateTime.UtcNow });
        }

        /// <summary>Latest participation state for a SCOPE (or a bare instrument, resolved only when unique).</summary>
        public static ParticipationState GetParticipationState(string scopeOrInstrument, double maxAgeSec)
            => _partic.Get(scopeOrInstrument, maxAgeSec);

        /// <summary>Heartbeat: re-stamp the cached RVOL read (available for parity; Participation is OnPriceChange).</summary>
        public static void TouchParticipationState(string scope) => _partic.Touch(scope);

        public static void ClearParticipationScope(string scope) => _partic.ClearScope(scope);

        public static List<ParticipationState> AllParticipationStates() => _partic.All();

        // ─────────────────────────────────────────────────────────────────────
        //  LOCATION (v1.10.0) — Location (a chart indicator) PUBLISHES the key STRUCTURAL LEVELS + the nearest
        //  level to price so consumers (the Council / strategies) know WHERE price is trading. A breakout into
        //  prior-day-high or the session VWAP is a different trade than one in open air. All prices in the
        //  instrument's price units; NearestDistTicks is SIGNED (price − level; >0 price above the level);
        //  NearestDistAtr = |dist| ÷ ATR (scale-free proximity). NaN = a level not yet established. Object-
        //  passing setter (like FleetSlot). Key = master instrument name. Auto-expires by age.
        // ─────────────────────────────────────────────────────────────────────
        public sealed class LevelState
        {
            /// <summary>v1.20.0 — the CHART this reading came from ("GC.69697v6x24").</summary>
            public string   Scope;
            public string   Bartype;
            public string   Instrument;
            public double   Vwap, VwapUpper, VwapLower;   // session VWAP + volume-weighted std bands (NaN = none)
            public double   Pdh, Pdl;                     // prior-day high / low
            public double   Orh, Orl;                     // opening-range high / low
            public double   Ibh, Ibl;                     // initial-balance high / low
            public double   SessHigh, SessLow;            // current session high / low
            public double   NearestPrice;                 // the nearest significant level to price
            public string   NearestName;                  // which level ("VWAP" / "PDH" / "ORL" …)
            public double   NearestDistTicks;             // SIGNED price − level, in ticks (>0 = price above)
            public double   NearestDistAtr;               // |dist| ÷ ATR (scale-free), NaN if unknown
            public int      VwapSide;                     // +1 price above VWAP / -1 below / 0 unknown
            public string   Source;
            public DateTime UpdatedUtc;

            /// <summary>True = a significant level sits within <paramref name="atr"/> ATRs of price.</summary>
            public bool Near(double atr) => !double.IsNaN(NearestDistAtr) && NearestDistAtr <= atr;
            /// <summary>True = the nearest level lies in the PATH of a dir-trade (above for long / below for short) within atr.</summary>
            public bool InPath(int dir, double atr) =>
                Near(atr) && ((dir > 0 && NearestDistTicks < 0) || (dir < 0 && NearestDistTicks > 0));
        }

        private static readonly SeamStore<LevelState> _level =
            new SeamStore<LevelState>("Level", v => v.Instrument, v => v.UpdatedUtc, (v, t) => v.UpdatedUtc = t);

        /// <summary>Location publishes its structural levels for one CHART (object-passing; keys by s.Scope, else
        /// s.Instrument for a legacy publisher; stamps UpdatedUtc).</summary>
        public static void SetLevelState(LevelState s)
        {
            if (s == null) return;
            string key = !string.IsNullOrEmpty(s.Scope) ? s.Scope : s.Instrument;
            if (string.IsNullOrEmpty(key)) return;
            s.UpdatedUtc = DateTime.UtcNow;
            _level.Set(key, s);
        }

        /// <summary>Latest level state for a SCOPE (or a bare instrument, resolved only when unique).</summary>
        public static LevelState GetLevelState(string scopeOrInstrument, double maxAgeSec)
            => _level.Get(scopeOrInstrument, maxAgeSec);

        /// <summary>Heartbeat: re-stamp the cached levels so a quiet-market map doesn't age out of the roster.</summary>
        public static void TouchLevelState(string scope) => _level.Touch(scope);

        public static void ClearLevelScope(string scope) => _level.ClearScope(scope);

        public static List<LevelState> AllLevelStates() => _level.All();

        // ─────────────────────────────────────────────────────────────────────
        //  MTF (v1.10.0) — MTF (a multi-series indicator) PUBLISHES the higher-timeframe TREND ALIGNMENT so
        //  consumers (the Council / strategies) can consult "is the entry-TF signal WITH or AGAINST the higher
        //  timeframes." Bias = the consensus direction across the ladder; AlignmentScore = -1..1 weighted net;
        //  AllAgree = every warm TF agrees. Dirs is a compact human string ("5:+ 15:+ 60:- 240:+"). This is a
        //  MODULATOR (counter-higher-TF trades get damped). Object-passing setter. Key = master instrument name.
        // ─────────────────────────────────────────────────────────────────────
        public sealed class MtfState
        {
            /// <summary>v1.20.0 — the CHART this reading came from ("GC.69697v6x24").</summary>
            public string   Scope;
            public string   Bartype;
            public string   Instrument;
            public int      Bias;            // -1 / 0 / 1 — the consensus direction across the ladder
            public double   AlignmentScore;  // -1..1 — weighted net direction (magnitude = agreement strength)
            public int      AlignedCount;    // # warm TFs agreeing with Bias
            public int      TfCount;         // # warm TFs
            public bool     AllAgree;        // all warm TFs share one non-zero direction
            public string   Dirs;            // compact per-TF summary, e.g. "5:+ 15:+ 60:- 240:+"
            public string   Source;
            public DateTime UpdatedUtc;

            public bool Aligned(int dir) => dir > 0 ? Bias > 0 : (dir < 0 ? Bias < 0 : Bias == 0);
        }

        private static readonly SeamStore<MtfState> _mtf =
            new SeamStore<MtfState>("Mtf", v => v.Instrument, v => v.UpdatedUtc, (v, t) => v.UpdatedUtc = t);

        /// <summary>MTF publishes its higher-timeframe alignment for one CHART (object-passing; keys by s.Scope, else
        /// s.Instrument for a legacy publisher; stamps UpdatedUtc).</summary>
        public static void SetMtfState(MtfState s)
        {
            if (s == null) return;
            string key = !string.IsNullOrEmpty(s.Scope) ? s.Scope : s.Instrument;
            if (string.IsNullOrEmpty(key)) return;
            s.UpdatedUtc = DateTime.UtcNow;
            _mtf.Set(key, s);
        }

        /// <summary>Latest MTF alignment for a SCOPE (or a bare instrument, resolved only when unique).</summary>
        public static MtfState GetMtfState(string scopeOrInstrument, double maxAgeSec)
            => _mtf.Get(scopeOrInstrument, maxAgeSec);

        /// <summary>Heartbeat: re-stamp the cached alignment so a quiet-market ladder doesn't age out.</summary>
        public static void TouchMtfState(string scope) => _mtf.Touch(scope);

        public static void ClearMtfScope(string scope) => _mtf.ClearScope(scope);

        public static List<MtfState> AllMtfStates() => _mtf.All();

        // ─────────────────────────────────────────────────────────────────────
        //  COMPRESSION (v1.11.0) — CompressionBase (a coil-base breakout detector) PUBLISHES its breakout so the
        //  Council gains a breakout VOTER. Signal = the ±1 breakout PULSE on the exact break bar (0 otherwise);
        //  BreakDir = that direction HELD for a few bars after the break (0 = no recent breakout) — the Council
        //  votes on BreakDir (a one-bar pulse is too fleeting for bar-cadence fusion). Coil = the window coil
        //  ratio (low = tightly compressed); Compressed/Armed = base state. Object-passing setter. Key = master
        //  instrument name. Auto-expires by age. Everything travels as INT/double/bool.
        // ─────────────────────────────────────────────────────────────────────
        public sealed class CompressionState
        {
            /// <summary>v1.19.0 — the CHART this reading came from ("GC.69697v6x24").</summary>
            public string   Scope;
            public string   Bartype;
            public string   Instrument;
            public int      Signal;      // +1 BreakUp / -1 BreakDown / 0 — the breakout PULSE this bar
            public int      BreakDir;    // last breakout direction, HELD for a few bars (0 = none recent) — the voter
            public double   Coil;        // window coil ratio (low = tightly compressed)
            public bool     Compressed;  // coil <= the tool's threshold (in a tight base)
            public bool     Armed;       // base confirmed + armed for a breakout
            public string   Source;
            public DateTime UpdatedUtc;

            /// <summary>True on the exact bar a breakout fired.</summary>
            public bool JustBroke => Signal != 0;
            /// <summary>True when the held breakout direction agrees with a signal direction (dir: +1 long, -1 short, 0 flat).</summary>
            public bool Aligned(int dir) => dir > 0 ? BreakDir > 0 : (dir < 0 ? BreakDir < 0 : BreakDir == 0);
        }

        private static readonly SeamStore<CompressionState> _compression =
            new SeamStore<CompressionState>("Compression", v => v.Instrument, v => v.UpdatedUtc, (v, t) => v.UpdatedUtc = t);

        /// <summary>CompressionBase publishes its breakout/coil state (object-passing; stamps UpdatedUtc).
        /// v1.19.0 — keyed by <c>s.Scope</c>; a publisher that has not migrated leaves Scope null and keys by
        /// instrument exactly as before, so the object-form signature never had to change.</summary>
        public static void SetCompressionState(CompressionState s)
        {
            if (s == null) return;
            string key = !string.IsNullOrEmpty(s.Scope) ? s.Scope : s.Instrument;
            if (string.IsNullOrEmpty(key)) return;
            s.UpdatedUtc = DateTime.UtcNow;
            _compression.Set(key, s);
        }

        /// <summary>Latest compression state for a SCOPE (or a bare instrument, resolved only when unique).</summary>
        public static CompressionState GetCompressionState(string scopeOrInstrument, double maxAgeSec)
            => _compression.Get(scopeOrInstrument, maxAgeSec);

        /// <summary>Heartbeat: re-stamp the cached reading so an OnBarClose sensor doesn't go stale between bars.</summary>
        public static void TouchCompressionState(string scope) => _compression.Touch(scope);

        public static List<CompressionState> AllCompressionStates() => _compression.All();

        // ─────────────────────────────────────────────────────────────────────
        //  INTERMARKET (v1.12.0) — the Intermarket indicator reads CORRELATED instruments (e.g. ZN/ZB for gold;
        //  a sister index for ES/NQ) and PUBLISHES a net directional LEAN for the CHART instrument — orthogonal
        //  MACRO information the single-instrument price sensors can't see (e.g. bonds up / real-yields down is
        //  gold-supportive). The reference symbols + correlation polarity are the INDICATOR's config (the sign
        //  differs by instrument), so the core just carries the resulting Lean. Consumers (the Council) fuse it
        //  as a voter. Object-passing setter. Auto-expires by age.
        //  ⚠ KEYED BY INSTRUMENT, NOT SCOPE — BY DESIGN (exec-plan 1.4 batch 4 decision, 2026-07-10). The lean is
        //  a MACRO call for the chart instrument, derived from OTHER instruments (ZN/ZB for gold) — it does NOT
        //  depend on the consuming chart's bar type, so every GC chart wants the SAME lean. One Intermarket per
        //  instrument publishes "GC lean"; any GC Council reads it by bare instrument. Do NOT scope-key this.
        //  (Contrast ParticipationState — bar-type-dependent → scope-keyed.)
        // ─────────────────────────────────────────────────────────────────────
        public sealed class IntermarketState
        {
            public string   Instrument;    // the CHART instrument this lean is FOR (e.g. GC)
            public int      Lean;          // -1 / 0 / 1 — net intermarket directional lean for THIS instrument
            public double   Score;         // -1..1 — sign-adjusted weighted net across the reference instruments
            public int      RefCount;      // # reference instruments contributing (warm)
            public string   Refs;          // compact summary, e.g. "ZN:+ ZB:+"
            public string   Source;
            public DateTime UpdatedUtc;

            public bool Aligned(int dir) => dir > 0 ? Lean > 0 : (dir < 0 ? Lean < 0 : Lean == 0);
        }

        private static readonly Dictionary<string, IntermarketState> _intermarket =
            new Dictionary<string, IntermarketState>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _intermarketLock = new object();

        /// <summary>Intermarket publishes its net correlated-instrument lean for an instrument (object-passing; stamps UpdatedUtc).</summary>
        public static void SetIntermarketState(IntermarketState s)
        {
            if (s == null || string.IsNullOrEmpty(s.Instrument)) return;
            s.UpdatedUtc = DateTime.UtcNow;
            lock (_intermarketLock) { _intermarket[s.Instrument] = s; }
        }

        /// <summary>Latest intermarket lean for an instrument, or null if none / stale beyond maxAgeSec (0 = never stale).</summary>
        public static IntermarketState GetIntermarketState(string instrument, double maxAgeSec)
        {
            if (string.IsNullOrEmpty(instrument)) return null;
            lock (_intermarketLock)
            {
                IntermarketState s;
                if (!_intermarket.TryGetValue(instrument, out s) || s == null) return null;
                if (maxAgeSec > 0 && (DateTime.UtcNow - s.UpdatedUtc).TotalSeconds > maxAgeSec) return null;
                return s;
            }
        }

        public static List<IntermarketState> AllIntermarketStates()
        {
            lock (_intermarketLock) { return new List<IntermarketState>(_intermarket.Values); }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  WAE (v1.13.0) — Sentinel WAE (Waddah Attar Explosion) PUBLISHES its momentum+volatility breakout so
        //  the Council gains a directional momentum-BREAKOUT voter. Bias = the histogram side (always-on lean,
        //  ±1/0); Power = |MACD-diff histogram|; Explosion = Bollinger-band WIDTH; DeadZone = ATR dead zone.
        //  IsExploding = the classic WAE trigger (Power > Explosion AND Power > DeadZone). Signal = the CONFIRMED
        //  directional breakout (= IsExploding ? Bias : 0) — the Council votes on Signal (Bias alone is too
        //  noisy; a real WAE entry needs the explosion). Object-passing setter. Key = master instrument name.
        //  Auto-expires by age. Everything travels as INT/double/bool.
        // ─────────────────────────────────────────────────────────────────────
        public sealed class WaeState
        {
            /// <summary>v1.19.0 — the CHART this reading came from ("GC.69697v6x24").</summary>
            public string   Scope;
            public string   Bartype;
            public string   Instrument;
            public int      Bias;         // -1 / 0 / +1 — histogram side (always-on lean)
            public double   Power;        // |histogram| (momentum magnitude)
            public double   Explosion;    // Bollinger-band width (the "explosion" line)
            public double   DeadZone;     // ATR-based dead zone threshold
            public bool     IsExploding;  // Power > Explosion AND Power > DeadZone (classic WAE trigger)
            public int      Signal;       // +1 / -1 / 0 — CONFIRMED breakout direction (= IsExploding ? Bias : 0) — the voter
            public string   Source;
            public DateTime UpdatedUtc;

            /// <summary>True when a confirmed breakout direction agrees with a signal direction (dir: +1 long, -1 short, 0 flat).</summary>
            public bool Aligned(int dir) => dir > 0 ? Signal > 0 : (dir < 0 ? Signal < 0 : Signal == 0);
        }

        private static readonly SeamStore<WaeState> _wae =
            new SeamStore<WaeState>("Wae", v => v.Instrument, v => v.UpdatedUtc, (v, t) => v.UpdatedUtc = t);

        /// <summary>Sentinel WAE publishes its momentum/explosion state (object-passing; stamps UpdatedUtc).
        /// v1.19.0 — keyed by <c>s.Scope</c>, falling back to the instrument for an un-migrated publisher.</summary>
        public static void SetWaeState(WaeState s)
        {
            if (s == null) return;
            string key = !string.IsNullOrEmpty(s.Scope) ? s.Scope : s.Instrument;
            if (string.IsNullOrEmpty(key)) return;
            s.UpdatedUtc = DateTime.UtcNow;
            _wae.Set(key, s);
        }

        /// <summary>Latest WAE state for a SCOPE (or a bare instrument, resolved only when unique).</summary>
        public static WaeState GetWaeState(string scopeOrInstrument, double maxAgeSec)
            => _wae.Get(scopeOrInstrument, maxAgeSec);

        /// <summary>Heartbeat: re-stamp the cached reading so an OnBarClose sensor doesn't go stale between bars.</summary>
        public static void TouchWaeState(string scope) => _wae.Touch(scope);

        public static List<WaeState> AllWaeStates() => _wae.All();

        // ─────────────────────────────────────────────────────────────────────
        //  GOD REVERSAL (v1.14.0) — SentinelGodReversal PUBLISHES the candle-grammar REVERSAL read the price
        //  trend-sensors don't carry (shaved close/open · engulfing-at-level · equal high/low · doji-cluster
        //  exhaustion · VI-fill), gated at a Bollinger-band edge (the "predictable place"). Signal = the pulse on
        //  the exact reversal-candle close (+1/-1/0); Dir = that direction HELD for a few bars (0 = none recent) —
        //  the Council's GREV voter. Quality 0..1 = the confluence score; Setup = the named setup label
        //  ("equalLevel"/"lateBloomer"/"lineBounce"/"engulf"/"shaved"); AtBand/Exhausted = live context.
        //  ⚠ This is a MEAN-REVERSION voice (often COUNTER to the trend voters) — best used as an entry TRIGGER
        //  alongside the Council bias, not as a lone swing vote. See Docs/SENTINEL_GOD_REVERSAL_DOCTRINE.md.
        //  Object-passing setter. Key = master instrument name. Auto-expires by age.
        // ─────────────────────────────────────────────────────────────────────
        public sealed class GodReversalState
        {
            /// <summary>v1.19.0 — the CHART this reading came from ("GC.69697v6x24").</summary>
            public string   Scope;
            public string   Bartype;
            public string   Instrument;
            public int      Signal;      // +1 / -1 / 0 — reversal PULSE on the exact reversal-candle close
            public int      Dir;         // last reversal direction, HELD for a few bars (0 = none recent) — the voter
            public double   Quality;     // 0..1 confluence score of the last reversal
            public string   Setup;       // named setup ("equalLevel"/"lateBloomer"/"lineBounce"/"engulf"/"shaved")
            public bool     AtBand;      // price currently at a Bollinger-band edge (the location gate)
            public bool     Exhausted;   // doji-cluster / weak-counter exhaustion present
            public string   Source;
            public DateTime UpdatedUtc;

            /// <summary>True on the exact bar a reversal fired.</summary>
            public bool JustReversed => Signal != 0;
            /// <summary>True when the held reversal direction agrees with a signal direction (dir: +1 long, -1 short, 0 flat).</summary>
            public bool Aligned(int dir) => dir > 0 ? Dir > 0 : (dir < 0 ? Dir < 0 : Dir == 0);
        }

        private static readonly SeamStore<GodReversalState> _godRev =
            new SeamStore<GodReversalState>("GodReversal", v => v.Instrument, v => v.UpdatedUtc, (v, t) => v.UpdatedUtc = t);

        /// <summary>SentinelGodReversal publishes its reversal state (object-passing; stamps UpdatedUtc).
        /// v1.19.0 — keyed by <c>s.Scope</c>, falling back to the instrument for an un-migrated publisher.</summary>
        public static void SetGodReversalState(GodReversalState s)
        {
            if (s == null) return;
            string key = !string.IsNullOrEmpty(s.Scope) ? s.Scope : s.Instrument;
            if (string.IsNullOrEmpty(key)) return;
            s.UpdatedUtc = DateTime.UtcNow;
            _godRev.Set(key, s);
        }

        /// <summary>Latest god-reversal state for a SCOPE (or a bare instrument, resolved only when unique).</summary>
        public static GodReversalState GetGodReversalState(string scopeOrInstrument, double maxAgeSec)
            => _godRev.Get(scopeOrInstrument, maxAgeSec);

        /// <summary>Heartbeat: re-stamp the cached reading so an OnBarClose sensor doesn't go stale between bars.</summary>
        public static void TouchGodReversalState(string scope) => _godRev.Touch(scope);

        public static List<GodReversalState> AllGodReversalStates() => _godRev.All();

        // ─────────────────────────────────────────────────────────────────────
        //  FLOW (v1.26.0) — SentinelFlow publishes the tick-rule CUMULATIVE VOLUME DELTA regime: the one axis that
        //  is NOT a price echo. Bias = sign of the OLS slope of session CVD over the window (flow direction);
        //  RSquared = fit quality (the gate); Strength 0..1 = |slope z-score| × R² (how convincingly flow leans);
        //  Divergence = price-swing vs CVD-swing disagreement (+1 bullish / -1 bearish / 0); Signal = the CONFIRMED
        //  directional flow (= Bias once RSquared and Strength clear their thresholds, else 0) — the Council's FLOW
        //  voter. Object-passing setter; keyed by scope; auto-expires by age. Everything travels as INT/double/bool.
        // ─────────────────────────────────────────────────────────────────────
        public sealed class FlowState
        {
            /// <summary>v1.26.0 — the CHART this reading came from ("GC.69697v6x24").</summary>
            public string   Scope;
            public string   Bartype;
            public string   Instrument;
            public int      Bias;         // -1/0/+1 — sign of the CVD regression slope (always-on flow lean)
            public double   Cvd;          // current session cumulative volume delta
            public double   Slope;        // OLS slope of CVD over the window
            public double   RSquared;     // 0..1 regression fit quality (the gate)
            public double   Strength;     // 0..1 — |slope z-score| × R², how convincingly flow leans
            public int      Divergence;   // -1/0/+1 — price vs CVD swing divergence
            public int      Signal;       // CONFIRMED directional flow (= Bias when gated, else 0) — the voter
            public string   Source;
            public DateTime UpdatedUtc;

            /// <summary>True when the confirmed flow direction agrees with a signal direction (dir: +1 long, -1 short, 0 flat).</summary>
            public bool Aligned(int dir) => dir > 0 ? Signal > 0 : (dir < 0 ? Signal < 0 : Signal == 0);
        }

        private static readonly SeamStore<FlowState> _flow =
            new SeamStore<FlowState>("Flow", v => v.Instrument, v => v.UpdatedUtc, (v, t) => v.UpdatedUtc = t);

        /// <summary>SentinelFlow publishes its CVD/order-flow regime (object-passing; stamps UpdatedUtc).</summary>
        public static void SetFlowState(FlowState s)
        {
            if (s == null) return;
            string key = !string.IsNullOrEmpty(s.Scope) ? s.Scope : s.Instrument;
            if (string.IsNullOrEmpty(key)) return;
            s.UpdatedUtc = DateTime.UtcNow;
            _flow.Set(key, s);
        }

        /// <summary>Latest flow state for a SCOPE (or a bare instrument, resolved only when unique).</summary>
        public static FlowState GetFlowState(string scopeOrInstrument, double maxAgeSec)
            => _flow.Get(scopeOrInstrument, maxAgeSec);

        /// <summary>Heartbeat: re-stamp the cached reading so an OnBarClose sensor doesn't go stale between bars.</summary>
        public static void TouchFlowState(string scope) => _flow.Touch(scope);

        public static List<FlowState> AllFlowStates() => _flow.All();

        // ─────────────────────────────────────────────────────────────────────
        //  PROFILE (v1.26.0) — SentinelProfile publishes the developing VOLUME PROFILE the suite lacked: Poc (the
        //  point of control), Vah/Val (the value-area high/low bracketing ~70% of volume). Location = where price
        //  sits relative to the value area (-1 below Val / 0 accepted inside VA / +1 above Vah) — the acceptance
        //  context. Signal = the POC-reversion lean (+1 when price is stretched below Val, -1 above Vah, 0 inside)
        //  — a MEAN-REVERSION voice. NearHVN/NearLVN flag price sitting on a high/low-volume node. This is a CONTEXT
        //  MODULATOR (acceptance inside VA = chop → damp; at the edges → support), not a lone directional voter.
        //  Object-passing setter; keyed by scope; auto-expires by age.
        // ─────────────────────────────────────────────────────────────────────
        public sealed class ProfileState
        {
            /// <summary>v1.26.0 — the CHART this reading came from ("GC.69697v6x24").</summary>
            public string   Scope;
            public string   Bartype;
            public string   Instrument;
            public double   Poc;          // point of control (max-volume price)
            public double   Vah;          // value-area high
            public double   Val;          // value-area low
            public int      Location;     // -1 below Val / 0 inside VA / +1 above Vah
            public int      Signal;       // POC-reversion lean: +1 stretched below Val / -1 above Vah / 0
            public double   DistPocTicks; // signed distance from POC in ticks (price − POC)
            public bool     NearHVN;      // price near a high-volume node
            public bool     NearLVN;      // price near a low-volume node
            public string   Source;
            public DateTime UpdatedUtc;

            /// <summary>True when price is accepted inside the value area (the chop context).</summary>
            public bool InValue => Location == 0;
        }

        private static readonly SeamStore<ProfileState> _profile =
            new SeamStore<ProfileState>("Profile", v => v.Instrument, v => v.UpdatedUtc, (v, t) => v.UpdatedUtc = t);

        /// <summary>SentinelProfile publishes its developing volume-profile state (object-passing; stamps UpdatedUtc).</summary>
        public static void SetProfileState(ProfileState s)
        {
            if (s == null) return;
            string key = !string.IsNullOrEmpty(s.Scope) ? s.Scope : s.Instrument;
            if (string.IsNullOrEmpty(key)) return;
            s.UpdatedUtc = DateTime.UtcNow;
            _profile.Set(key, s);
        }

        /// <summary>Latest profile state for a SCOPE (or a bare instrument, resolved only when unique).</summary>
        public static ProfileState GetProfileState(string scopeOrInstrument, double maxAgeSec)
            => _profile.Get(scopeOrInstrument, maxAgeSec);

        /// <summary>Heartbeat: re-stamp the cached reading so an OnBarClose sensor doesn't go stale between bars.</summary>
        public static void TouchProfileState(string scope) => _profile.Touch(scope);

        public static List<ProfileState> AllProfileStates() => _profile.All();

        // ─────────────────────────────────────────────────────────────────────
        //  REGIME (v1.26.0) — SentinelRegime publishes a statistical VOLATILITY REGIME (K-means clusters rolling
        //  return-volatility into low/med/high, a Markov forward filter tracks the current-regime probability). This
        //  is orthogonal to ADX (trend/chop) and CompressionBase (squeeze): it says WHICH volatility world we are in
        //  and how confidently. Regime = 0 low / 1 med / 2 high; RegimeProb = confidence in that label; Low/Med/High
        //  Prob = the full posterior. Trending = derived hint (a stable, non-chaotic regime favors trend
        //  continuation). This is a CONVICTION/SIZE MODULATOR, never a directional voter. Object-passing setter.
        // ─────────────────────────────────────────────────────────────────────
        public sealed class RegimeState
        {
            /// <summary>v1.26.0 — the CHART this reading came from ("GC.69697v6x24").</summary>
            public string   Scope;
            public string   Bartype;
            public string   Instrument;
            public int      Regime;       // 0 low / 1 med / 2 high volatility
            public double   RegimeProb;   // 0..1 confidence in the current regime
            public double   LowProb;      // posterior — low-vol regime
            public double   MedProb;      // posterior — med-vol regime
            public double   HighProb;     // posterior — high-vol regime
            public bool     Trending;     // derived: current regime favors trend continuation
            public string   Source;
            public DateTime UpdatedUtc;
        }

        private static readonly SeamStore<RegimeState> _regime =
            new SeamStore<RegimeState>("Regime", v => v.Instrument, v => v.UpdatedUtc, (v, t) => v.UpdatedUtc = t);

        /// <summary>SentinelRegime publishes its volatility-regime posterior (object-passing; stamps UpdatedUtc).</summary>
        public static void SetRegimeState(RegimeState s)
        {
            if (s == null) return;
            string key = !string.IsNullOrEmpty(s.Scope) ? s.Scope : s.Instrument;
            if (string.IsNullOrEmpty(key)) return;
            s.UpdatedUtc = DateTime.UtcNow;
            _regime.Set(key, s);
        }

        /// <summary>Latest regime state for a SCOPE (or a bare instrument, resolved only when unique).</summary>
        public static RegimeState GetRegimeState(string scopeOrInstrument, double maxAgeSec)
            => _regime.Get(scopeOrInstrument, maxAgeSec);

        /// <summary>Heartbeat: re-stamp the cached reading so an OnBarClose sensor doesn't go stale between bars.</summary>
        public static void TouchRegimeState(string scope) => _regime.Touch(scope);

        public static List<RegimeState> AllRegimeStates() => _regime.All();

        // ─────────────────────────────────────────────────────────────────────
        //  STRUCTURE (v1.26.0) — SentinelStructure publishes MARKET STRUCTURE off confirmed swings: Bias = +1 while
        //  the swing sequence prints higher-highs & higher-lows, -1 on lower-highs & lower-lows, 0 when mixed/unclear.
        //  Bos = a break-of-structure PULSE (+1/-1) on the bar price closes beyond the last opposing swing pivot;
        //  Signal = the confirmed structure direction. SwingType carries the last swing classification (see the
        //  publisher). A STATE voter (structure always has a reading). Object-passing setter; keyed by scope.
        // ─────────────────────────────────────────────────────────────────────
        public sealed class StructureState
        {
            /// <summary>v1.26.0 — the CHART this reading came from ("GC.69697v6x24").</summary>
            public string   Scope;
            public string   Bartype;
            public string   Instrument;
            public int      Bias;       // +1 HH/HL up-structure / -1 LH/LL down-structure / 0 mixed
            public int      SwingType;  // last swing classification: +2 HH,+1 HL,-1 LH,-2 LL,0 none
            public int      Bos;        // break-of-structure PULSE (+1/-1/0)
            public int      Signal;     // confirmed structure direction (the voter)
            public string   Source;
            public DateTime UpdatedUtc;

            /// <summary>True when the structure direction agrees with a signal direction.</summary>
            public bool Aligned(int dir) => dir > 0 ? Signal > 0 : (dir < 0 ? Signal < 0 : Signal == 0);
        }

        private static readonly SeamStore<StructureState> _structure =
            new SeamStore<StructureState>("Structure", v => v.Instrument, v => v.UpdatedUtc, (v, t) => v.UpdatedUtc = t);

        /// <summary>SentinelStructure publishes its market-structure state (object-passing; stamps UpdatedUtc).</summary>
        public static void SetStructureState(StructureState s)
        {
            if (s == null) return;
            string key = !string.IsNullOrEmpty(s.Scope) ? s.Scope : s.Instrument;
            if (string.IsNullOrEmpty(key)) return;
            s.UpdatedUtc = DateTime.UtcNow;
            _structure.Set(key, s);
        }

        /// <summary>Latest structure state for a SCOPE (or a bare instrument, resolved only when unique).</summary>
        public static StructureState GetStructureState(string scopeOrInstrument, double maxAgeSec)
            => _structure.Get(scopeOrInstrument, maxAgeSec);

        /// <summary>Heartbeat: re-stamp the cached reading so an OnBarClose sensor doesn't go stale between bars.</summary>
        public static void TouchStructureState(string scope) => _structure.Touch(scope);

        public static List<StructureState> AllStructureStates() => _structure.All();

        // ─────────────────────────────────────────────────────────────────────
        //  EXHAUSTION (v1.26.0) — SentinelExhaustion publishes the Leledc consecutive-close EXHAUSTION read: after a
        //  run of closes pushing one way, a bar that closes back beyond the close N bars ago marks trend exhaustion
        //  and a likely reversal. Signal = the pulse on that bar (+1 bullish-exhaustion → expect up-reversal /
        //  -1 bearish / 0); Dir = that direction HELD for a few bars; Major flags the stronger (major vs minor)
        //  variant. A TRIGGER voter and a MEAN-REVERSION voice (often counter to the trend voters), like GREV.
        //  Object-passing setter; keyed by scope; auto-expires by age.
        // ─────────────────────────────────────────────────────────────────────
        public sealed class ExhaustionState
        {
            /// <summary>v1.26.0 — the CHART this reading came from ("GC.69697v6x24").</summary>
            public string   Scope;
            public string   Bartype;
            public string   Instrument;
            public int      Signal;   // +1 bullish / -1 bearish / 0 — exhaustion PULSE on the exact bar
            public int      Dir;      // last exhaustion direction HELD for a few bars (0 = none recent) — the voter
            public bool     Major;    // major (stronger) vs minor exhaustion
            public string   Source;
            public DateTime UpdatedUtc;

            /// <summary>True on the exact bar an exhaustion fired.</summary>
            public bool JustFired => Signal != 0;
            /// <summary>True when the held exhaustion direction agrees with a signal direction.</summary>
            public bool Aligned(int dir) => dir > 0 ? Dir > 0 : (dir < 0 ? Dir < 0 : Dir == 0);
        }

        private static readonly SeamStore<ExhaustionState> _exhaustion =
            new SeamStore<ExhaustionState>("Exhaustion", v => v.Instrument, v => v.UpdatedUtc, (v, t) => v.UpdatedUtc = t);

        /// <summary>SentinelExhaustion publishes its exhaustion/reversal state (object-passing; stamps UpdatedUtc).</summary>
        public static void SetExhaustionState(ExhaustionState s)
        {
            if (s == null) return;
            string key = !string.IsNullOrEmpty(s.Scope) ? s.Scope : s.Instrument;
            if (string.IsNullOrEmpty(key)) return;
            s.UpdatedUtc = DateTime.UtcNow;
            _exhaustion.Set(key, s);
        }

        /// <summary>Latest exhaustion state for a SCOPE (or a bare instrument, resolved only when unique).</summary>
        public static ExhaustionState GetExhaustionState(string scopeOrInstrument, double maxAgeSec)
            => _exhaustion.Get(scopeOrInstrument, maxAgeSec);

        /// <summary>Heartbeat: re-stamp the cached reading so an OnBarClose sensor doesn't go stale between bars.</summary>
        public static void TouchExhaustionState(string scope) => _exhaustion.Touch(scope);

        public static List<ExhaustionState> AllExhaustionStates() => _exhaustion.All();

        // ─────────────────────────────────────────────────────────────────────
        //  ADXVMA (v1.28.0) — SentinelADXVMA publishes an ADX-volatility adaptive-MA trend: Bias = trinary trend
        //  (+1 up / -1 down / 0 chop, ATR-deadband + hysteresis); Value = the adaptive MA. A STATE voter (neutral
        //  in chop). Object-passing setter; keyed by scope; auto-expires by age.
        // ─────────────────────────────────────────────────────────────────────
        public sealed class AdxvmaState
        {
            public string   Scope;
            public string   Bartype;
            public string   Instrument;
            public int      Bias;         // +1 up / -1 down / 0 chop
            public double   Value;        // the adaptive MA
            public int      Signal;       // = Bias (the voter)
            public string   Source;
            public DateTime UpdatedUtc;

            public bool Aligned(int dir) => dir > 0 ? Signal > 0 : (dir < 0 ? Signal < 0 : Signal == 0);
        }

        private static readonly SeamStore<AdxvmaState> _adxvma =
            new SeamStore<AdxvmaState>("Adxvma", v => v.Instrument, v => v.UpdatedUtc, (v, t) => v.UpdatedUtc = t);

        /// <summary>SentinelADXVMA publishes its adaptive-MA trend (object-passing; stamps UpdatedUtc).</summary>
        public static void SetAdxvmaState(AdxvmaState s)
        {
            if (s == null) return;
            string key = !string.IsNullOrEmpty(s.Scope) ? s.Scope : s.Instrument;
            if (string.IsNullOrEmpty(key)) return;
            s.UpdatedUtc = DateTime.UtcNow;
            _adxvma.Set(key, s);
        }

        /// <summary>Latest ADXVMA state for a SCOPE (or a bare instrument, resolved only when unique).</summary>
        public static AdxvmaState GetAdxvmaState(string scopeOrInstrument, double maxAgeSec)
            => _adxvma.Get(scopeOrInstrument, maxAgeSec);

        /// <summary>Heartbeat: re-stamp the cached reading so an OnBarClose sensor doesn't go stale between bars.</summary>
        public static void TouchAdxvmaState(string scope) => _adxvma.Touch(scope);

        public static List<AdxvmaState> AllAdxvmaStates() => _adxvma.All();

        // ─────────────────────────────────────────────────────────────────────
        //  SUPERTREND (v1.28.0) — SentinelSuperTrend publishes the classic ATR-band trailing-flip trend: Bias = the
        //  current side (+1/-1, always directional); Line = the trailing SuperTrend line; Flip = the ±1 pulse on the
        //  bar the trend flips. A STATE voter (always ±1). Object-passing setter; keyed by scope.
        // ─────────────────────────────────────────────────────────────────────
        public sealed class SuperTrendState
        {
            public string   Scope;
            public string   Bartype;
            public string   Instrument;
            public int      Bias;         // +1 / -1 (always directional)
            public double   Line;         // the trailing SuperTrend line
            public int      Flip;         // +1/-1 pulse on the flip bar, else 0
            public int      Signal;       // = Bias (the voter)
            public string   Source;
            public DateTime UpdatedUtc;

            public bool Aligned(int dir) => dir > 0 ? Signal > 0 : (dir < 0 ? Signal < 0 : Signal == 0);
        }

        private static readonly SeamStore<SuperTrendState> _superTrend =
            new SeamStore<SuperTrendState>("SuperTrend", v => v.Instrument, v => v.UpdatedUtc, (v, t) => v.UpdatedUtc = t);

        /// <summary>SentinelSuperTrend publishes its trailing-flip trend (object-passing; stamps UpdatedUtc).</summary>
        public static void SetSuperTrendState(SuperTrendState s)
        {
            if (s == null) return;
            string key = !string.IsNullOrEmpty(s.Scope) ? s.Scope : s.Instrument;
            if (string.IsNullOrEmpty(key)) return;
            s.UpdatedUtc = DateTime.UtcNow;
            _superTrend.Set(key, s);
        }

        /// <summary>Latest SuperTrend state for a SCOPE (or a bare instrument, resolved only when unique).</summary>
        public static SuperTrendState GetSuperTrendState(string scopeOrInstrument, double maxAgeSec)
            => _superTrend.Get(scopeOrInstrument, maxAgeSec);

        /// <summary>Heartbeat: re-stamp the cached reading so an OnBarClose sensor doesn't go stale between bars.</summary>
        public static void TouchSuperTrendState(string scope) => _superTrend.Touch(scope);

        public static List<SuperTrendState> AllSuperTrendStates() => _superTrend.All();

        // ─────────────────────────────────────────────────────────────────────
        //  PARABOLIC SAR (v1.28.0) — SentinelParabolicSAR publishes Wilder's SAR trend/stop: Bias = +1 price above
        //  SAR / -1 below (always directional); Sar = the stop price; Flip = the ±1 pulse on the reversal bar. A
        //  STATE voter (always ±1). Object-passing setter; keyed by scope.
        // ─────────────────────────────────────────────────────────────────────
        public sealed class SarState
        {
            public string   Scope;
            public string   Bartype;
            public string   Instrument;
            public int      Bias;         // +1 above SAR (up) / -1 below (down)
            public double   Sar;          // the SAR stop price
            public int      Flip;         // +1/-1 pulse on the reversal bar, else 0
            public int      Signal;       // = Bias (the voter)
            public string   Source;
            public DateTime UpdatedUtc;

            public bool Aligned(int dir) => dir > 0 ? Signal > 0 : (dir < 0 ? Signal < 0 : Signal == 0);
        }

        private static readonly SeamStore<SarState> _sar =
            new SeamStore<SarState>("Sar", v => v.Instrument, v => v.UpdatedUtc, (v, t) => v.UpdatedUtc = t);

        /// <summary>SentinelParabolicSAR publishes its SAR trend/stop (object-passing; stamps UpdatedUtc).</summary>
        public static void SetSarState(SarState s)
        {
            if (s == null) return;
            string key = !string.IsNullOrEmpty(s.Scope) ? s.Scope : s.Instrument;
            if (string.IsNullOrEmpty(key)) return;
            s.UpdatedUtc = DateTime.UtcNow;
            _sar.Set(key, s);
        }

        /// <summary>Latest SAR state for a SCOPE (or a bare instrument, resolved only when unique).</summary>
        public static SarState GetSarState(string scopeOrInstrument, double maxAgeSec)
            => _sar.Get(scopeOrInstrument, maxAgeSec);

        /// <summary>Heartbeat: re-stamp the cached reading so an OnBarClose sensor doesn't go stale between bars.</summary>
        public static void TouchSarState(string scope) => _sar.Touch(scope);

        public static List<SarState> AllSarStates() => _sar.All();

        // ─────────────────────────────────────────────────────────────────────
        //  Z-SCORE (v1.28.0) — SentinelZScore publishes the textbook standard score (Close − SMA)/StdDev as a
        //  MEAN-REVERSION read: Z = the value; Signal = +1 stretched LOW (expect up) / -1 stretched HIGH (expect
        //  down) / 0; Extreme = |Z| beyond the band. A TRIGGER voter (mean-reversion voice, à la EXH/GREV).
        //  Object-passing setter; keyed by scope.
        // ─────────────────────────────────────────────────────────────────────
        public sealed class ZScoreState
        {
            public string   Scope;
            public string   Bartype;
            public string   Instrument;
            public double   Z;            // the z value
            public int      Signal;       // +1 stretched low (fade up) / -1 stretched high (fade down) / 0
            public bool     Extreme;      // |Z| beyond the band
            public string   Source;
            public DateTime UpdatedUtc;

            public bool Aligned(int dir) => dir > 0 ? Signal > 0 : (dir < 0 ? Signal < 0 : Signal == 0);
        }

        private static readonly SeamStore<ZScoreState> _zscore =
            new SeamStore<ZScoreState>("ZScore", v => v.Instrument, v => v.UpdatedUtc, (v, t) => v.UpdatedUtc = t);

        /// <summary>SentinelZScore publishes its mean-reversion z-score (object-passing; stamps UpdatedUtc).</summary>
        public static void SetZScoreState(ZScoreState s)
        {
            if (s == null) return;
            string key = !string.IsNullOrEmpty(s.Scope) ? s.Scope : s.Instrument;
            if (string.IsNullOrEmpty(key)) return;
            s.UpdatedUtc = DateTime.UtcNow;
            _zscore.Set(key, s);
        }

        /// <summary>Latest z-score state for a SCOPE (or a bare instrument, resolved only when unique).</summary>
        public static ZScoreState GetZScoreState(string scopeOrInstrument, double maxAgeSec)
            => _zscore.Get(scopeOrInstrument, maxAgeSec);

        /// <summary>Heartbeat: re-stamp the cached reading so an OnBarClose sensor doesn't go stale between bars.</summary>
        public static void TouchZScoreState(string scope) => _zscore.Touch(scope);

        public static List<ZScoreState> AllZScoreStates() => _zscore.All();

        // ─────────────────────────────────────────────────────────────────────
        //  TREND ARCHITECT (v1.29.0) — SentinelTrendArchitect (the MPL Pine port) publishes its composite verdict:
        //  Bias = the normalized PRISM held trend direction (+1 bull / -1 bear / 0); Signal = the PRISM buy/sell
        //  signal (the voter); Regime = the Trend-Regime-Gate (+1 bull-regime / -1 bear-regime / 0 none). A rich
        //  composite trend read (fuses MFI/CCI/CVD/Hurst/KAMA-fan). A STATE voter. Object-passing setter; keyed by scope.
        // ─────────────────────────────────────────────────────────────────────
        public sealed class TrendArchitectState
        {
            public string   Scope;
            public string   Bartype;
            public string   Instrument;
            public int      Bias;         // +1 bull / -1 bear / 0 — normalized PRISM held direction
            public int      Signal;       // +1/-1/0 — PRISM buy/sell signal (the voter)
            public int      Regime;       // +1 bull-regime / -1 bear-regime / 0 none (Trend-Regime-Gate)
            public string   Source;
            public DateTime UpdatedUtc;

            public bool Aligned(int dir) => dir > 0 ? Signal > 0 : (dir < 0 ? Signal < 0 : Signal == 0);
        }

        private static readonly SeamStore<TrendArchitectState> _trendArch =
            new SeamStore<TrendArchitectState>("TrendArchitect", v => v.Instrument, v => v.UpdatedUtc, (v, t) => v.UpdatedUtc = t);

        /// <summary>SentinelTrendArchitect publishes its composite PRISM/regime verdict (object-passing; stamps UpdatedUtc).</summary>
        public static void SetTrendArchitectState(TrendArchitectState s)
        {
            if (s == null) return;
            string key = !string.IsNullOrEmpty(s.Scope) ? s.Scope : s.Instrument;
            if (string.IsNullOrEmpty(key)) return;
            s.UpdatedUtc = DateTime.UtcNow;
            _trendArch.Set(key, s);
        }

        /// <summary>Latest Trend Architect state for a SCOPE (or a bare instrument, resolved only when unique).</summary>
        public static TrendArchitectState GetTrendArchitectState(string scopeOrInstrument, double maxAgeSec)
            => _trendArch.Get(scopeOrInstrument, maxAgeSec);

        /// <summary>Heartbeat: re-stamp the cached reading so an OnBarClose sensor doesn't go stale between bars.</summary>
        public static void TouchTrendArchitectState(string scope) => _trendArch.Touch(scope);

        public static List<TrendArchitectState> AllTrendArchitectStates() => _trendArch.All();

        // ─────────────────────────────────────────────────────────────────────
        //  VIDYA (v1.30.0) — SentinelVIDYA publishes the Chande-CMO-modulated adaptive MA trend: Bias = slope
        //  direction (+1/-1/0 with deadband + hysteresis); Value = the VIDYA. A STATE voter. Keyed by scope.
        // ─────────────────────────────────────────────────────────────────────
        public sealed class VidyaState
        {
            public string   Scope;
            public string   Bartype;
            public string   Instrument;
            public int      Bias;         // +1 rising / -1 falling / 0 flat
            public double   Value;        // the VIDYA
            public int      Signal;       // = Bias (the voter)
            public string   Source;
            public DateTime UpdatedUtc;

            public bool Aligned(int dir) => dir > 0 ? Signal > 0 : (dir < 0 ? Signal < 0 : Signal == 0);
        }

        private static readonly SeamStore<VidyaState> _vidya =
            new SeamStore<VidyaState>("Vidya", v => v.Instrument, v => v.UpdatedUtc, (v, t) => v.UpdatedUtc = t);

        /// <summary>SentinelVIDYA publishes its adaptive-MA trend (object-passing; stamps UpdatedUtc).</summary>
        public static void SetVidyaState(VidyaState s)
        {
            if (s == null) return;
            string key = !string.IsNullOrEmpty(s.Scope) ? s.Scope : s.Instrument;
            if (string.IsNullOrEmpty(key)) return;
            s.UpdatedUtc = DateTime.UtcNow;
            _vidya.Set(key, s);
        }

        /// <summary>Latest VIDYA state for a SCOPE (or a bare instrument, resolved only when unique).</summary>
        public static VidyaState GetVidyaState(string scopeOrInstrument, double maxAgeSec)
            => _vidya.Get(scopeOrInstrument, maxAgeSec);

        /// <summary>Heartbeat: re-stamp the cached reading so an OnBarClose sensor doesn't go stale between bars.</summary>
        public static void TouchVidyaState(string scope) => _vidya.Touch(scope);

        public static List<VidyaState> AllVidyaStates() => _vidya.All();

        // ─────────────────────────────────────────────────────────────────────
        //  HARMONIC (v1.30.0) — SentinelHarmonic publishes harmonic XABCD pattern completions (Gartley/Bat/
        //  Butterfly/Crab): Signal = the ±1 PULSE on the completion bar (+1 bullish M-pattern / -1 bearish);
        //  Dir = that direction HELD for a few bars; Pattern = the matched pattern name. A TRIGGER voter (a
        //  reversal/mean-reversion voice). Object-passing setter; keyed by scope.
        // ─────────────────────────────────────────────────────────────────────
        public sealed class HarmonicState
        {
            public string   Scope;
            public string   Bartype;
            public string   Instrument;
            public int      Signal;   // +1 bullish / -1 bearish / 0 — completion PULSE
            public int      Dir;      // held completion direction (0 after the hold window)
            public string   Pattern;  // matched pattern name ("Gartley"/"Bat"/… or "")
            public string   Source;
            public DateTime UpdatedUtc;

            public bool JustFired => Signal != 0;
            public bool Aligned(int dir) => dir > 0 ? Dir > 0 : (dir < 0 ? Dir < 0 : Dir == 0);
        }

        private static readonly SeamStore<HarmonicState> _harmonic =
            new SeamStore<HarmonicState>("Harmonic", v => v.Instrument, v => v.UpdatedUtc, (v, t) => v.UpdatedUtc = t);

        /// <summary>SentinelHarmonic publishes its XABCD pattern-completion state (object-passing; stamps UpdatedUtc).</summary>
        public static void SetHarmonicState(HarmonicState s)
        {
            if (s == null) return;
            string key = !string.IsNullOrEmpty(s.Scope) ? s.Scope : s.Instrument;
            if (string.IsNullOrEmpty(key)) return;
            s.UpdatedUtc = DateTime.UtcNow;
            _harmonic.Set(key, s);
        }

        /// <summary>Latest harmonic state for a SCOPE (or a bare instrument, resolved only when unique).</summary>
        public static HarmonicState GetHarmonicState(string scopeOrInstrument, double maxAgeSec)
            => _harmonic.Get(scopeOrInstrument, maxAgeSec);

        /// <summary>Heartbeat: re-stamp the cached reading so an OnBarClose sensor doesn't go stale between bars.</summary>
        public static void TouchHarmonicState(string scope) => _harmonic.Touch(scope);

        public static List<HarmonicState> AllHarmonicStates() => _harmonic.All();

        // ─────────────────────────────────────────────────────────────────────
        //  BRICK DATA LOG (v1.6.1) — the durable record of what a custom brick bars type produced.
        //  NT regenerates custom bricks FROM TICKS on every load and never stores them, so without this
        //  there is no brick history to analyse/audit. One async daily JSONL at <SettingsDir>\BrickLog\
        //  brick-YYYY-MM-DD.jsonl — SEPARATE from the order Ledger (bricks are high-volume). Callers append
        //  one COMPLETED brick per record, and only when NEAR-REALTIME (a historical rebuild would duplicate
        //  every brick on every reload). Writes are async + swallow all exceptions (never touch the bar path).
        // ─────────────────────────────────────────────────────────────────────
        public static class BrickLog
        {
            private static readonly object _io = new object();

            /// <summary>The brick-log directory (&lt;SettingsDir&gt;\BrickLog). One JSONL per local calendar day.</summary>
            public static string Dir { get { return Path.Combine(SettingsDir, "BrickLog"); } }
            public static string FileFor(DateTime day) { return Path.Combine(Dir, "brick-" + day.ToString("yyyy-MM-dd") + ".jsonl"); }

            /// <summary>Append one completed-brick record. <paramref name="jsonFields"/> is the already-formatted
            /// inner JSON (no braces, no leading comma), e.g. "\"dir\":1,\"o\":4151.1,\"sizeT\":10". The wrapper
            /// adds ts/src/instr. REALTIME callers only.</summary>
            public static void Append(string source, string instrument, string jsonFields)
            {
                string line;
                try
                {
                    line = "{\"ts\":\"" + DateTime.UtcNow.ToString("o") + "\",\"src\":\"" + J(source)
                         + "\",\"instr\":\"" + J(instrument) + "\"," + (jsonFields ?? "") + "}";
                }
                catch { return; }
                string dir, file;
                try { dir = Dir; file = FileFor(DateTime.Now); }
                catch { return; }
                System.Threading.Tasks.Task.Run(() =>
                {
                    try { lock (_io) { Directory.CreateDirectory(dir); File.AppendAllText(file, line + "\r\n"); } } catch { }
                });
            }

            private static string J(string s) => s == null ? "" : s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        // ─────────────────────────────────────────────────────────────────────
        //  FLEET ORCHESTRATION — SentinelArc PUBLISHES a per-slot "fleet plan" (which
        //  instruments/strategies should trade on the leader, at what size, in which
        //  session window); Sentinel-aware STRATEGIES (e.g. GTrader21) CONSULT SlotLive()
        //  at entry time and only trade when their slot is live. Same publish/consult
        //  pattern as Eye + the kill-switch. This is how Arc "enables/disables" a chart
        //  strategy it can NOT start/stop directly: the strategy stays loaded 24/5 but
        //  gates its own entries on the plan. Key = master instrument name (e.g. "GC").
        //
        //  One FleetSlot carries BOTH the plan (Arc writes Enabled/Contracts/session) AND
        //  the live supervision status (Arc refreshes FillsToday/PositionQty/DayPnl/
        //  Health each tick) so the dashboard + state.json read one object. InSession is
        //  Arc-computed on its timer. SlotLive FAILS OPEN for unmanaged instruments (no
        //  slot => true) so adding Arc never silently kills a strategy it doesn't manage.
        // ─────────────────────────────────────────────────────────────────────
        public sealed class FleetSlot
        {
            // plan (Arc-authored)
            public string   Instrument;       // master name, e.g. "GC"
            public string   Strategy;         // label, e.g. "GTrader21"
            public bool     Enabled;          // Arc master on/off for this slot
            public int      Contracts;        // size hint (0 = let the strategy decide)
            public int      SessionStartMin;  // minutes-of-day session opens (-1 = 24h)
            public int      SessionEndMin;    // minutes-of-day session closes (-1 = 24h)
            public bool     InSession;        // Arc-computed each refresh (true if 24h)
            public DateTime UpdatedUtc;
            // live supervision status (Arc-refreshed)
            public int      FillsToday;        // total fills today (entries + exits + partials)
            public int      PositionQty;      // signed net position
            public double   DayPnl;           // realized+unrealized day PnL, USD
            public DateTime LastSignalUtc;
            public string   Health;           // "LIVE" / "IDLE" / "OFF" / "DARK"
        }

        private static readonly Dictionary<string, FleetSlot> _fleet =
            new Dictionary<string, FleetSlot>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, DateTime> _fleetConsult =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);   // instrument -> last SlotLive() call
        private static readonly object _fleetLock = new object();

        /// <summary>Arc upserts a slot (plan + status) keyed by master instrument name.</summary>
        public static void PublishFleetSlot(FleetSlot slot)
        {
            if (slot == null || string.IsNullOrEmpty(slot.Instrument)) return;
            slot.UpdatedUtc = DateTime.UtcNow;
            lock (_fleetLock) { _fleet[slot.Instrument] = slot; }
        }

        public static void RemoveFleetSlot(string instrument)
        {
            if (string.IsNullOrEmpty(instrument)) return;
            lock (_fleetLock) { _fleet.Remove(instrument); }
        }

        public static FleetSlot GetFleetSlot(string instrument)
        {
            if (string.IsNullOrEmpty(instrument)) return null;
            lock (_fleetLock) { FleetSlot s; return _fleet.TryGetValue(instrument, out s) ? s : null; }
        }

        public static List<FleetSlot> AllFleetSlots()
        {
            lock (_fleetLock) { return new List<FleetSlot>(_fleet.Values); }
        }

        /// <summary>
        /// The consult a Sentinel-aware strategy calls at entry time. FAIL-OPEN: an
        /// instrument with no published slot returns true (unmanaged → trade normally).
        /// A managed slot trades only when Enabled AND within its session window.
        /// (Strategies should separately honor the kill-switch via CanAct.)
        /// </summary>
        public static bool SlotLive(string instrument)
        {
            if (string.IsNullOrEmpty(instrument)) return true;
            lock (_fleetLock)
            {
                _fleetConsult[instrument] = DateTime.UtcNow;   // proof-of-life: a strategy is consulting the plan
                FleetSlot s;
                if (!_fleet.TryGetValue(instrument, out s) || s == null) return true; // unmanaged
                return s.Enabled && s.InSession;
            }
        }

        /// <summary>When a strategy last consulted SlotLive() for an instrument (UTC; MinValue if never).
        /// Arc uses this to tell "engine loaded but idle" from "engine not running" (DARK).</summary>
        public static DateTime LastConsultUtc(string instrument)
        {
            if (string.IsNullOrEmpty(instrument)) return DateTime.MinValue;
            lock (_fleetLock) { DateTime t; return _fleetConsult.TryGetValue(instrument, out t) ? t : DateTime.MinValue; }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  ACTOR REGISTRY (v1.25.0 · ML spec §10.10) — "the name is an INTERLOCK, not a label." An instanceKey names
        //  ONE running actor: <class>#<scope>@<account>. Two actors claiming ONE key is precisely the managed-position
        //  hazard — one account, two strategies, the account position moving underneath a managed strategy → desync →
        //  "position not flat" lockout on all new entries (see the managed-position lesson in CLAUDE.md). So an armed
        //  actor must REFUSE to run when it cannot claim its key: the ambiguous config IS the dangerous config, and the
        //  two fall out of one mechanism. Register in Realtime (never DataLoaded — a replay/historical instance must not
        //  claim it); release ONLY if the registry still holds THIS object (NT re-enables by constructing a NEW instance
        //  then Terminating the OLD one, and the terminate can land AFTER the new registration — a blind release would
        //  silently free a live actor's key; same shape as the "re-adopt refs, never fabricate" lesson).
        // ─────────────────────────────────────────────────────────────────────
        public sealed class ActorReg
        {
            public string   InstanceKey;
            public string   Account;
            public string   Instrument;
            public DateTime Utc;
            public object   Owner;        // reference identity for the checked release (never serialized)
        }
        private static readonly Dictionary<string, ActorReg> _actors =
            new Dictionary<string, ActorReg>(StringComparer.Ordinal);
        private static readonly object _actorsLock = new object();

        /// <summary>Claim <paramref name="instanceKey"/> for <paramref name="owner"/>. TRUE = the caller now owns it (it
        /// was free, or the caller already owned it — idempotent re-claim). FALSE = COLLISION: a DIFFERENT live object
        /// already holds the key (the dangerous same-scope+account config) → the caller must REFUSE to arm. Logs once.</summary>
        public static bool RegisterActor(string instanceKey, string account, string instrument, object owner)
        {
            if (string.IsNullOrEmpty(instanceKey) || owner == null) return false;
            bool collision = false; string otherAcct = null;
            lock (_actorsLock)
            {
                ActorReg cur;
                if (_actors.TryGetValue(instanceKey, out cur) && cur != null && !ReferenceEquals(cur.Owner, owner))
                {
                    collision = true; otherAcct = cur.Account;
                }
                else
                {
                    _actors[instanceKey] = new ActorReg { InstanceKey = instanceKey, Account = account,
                                                          Instrument = instrument, Utc = DateTime.UtcNow, Owner = owner };
                }
            }
            if (collision)
                Log("Core", "ACTOR COLLISION: '" + instanceKey + "' is already claimed (acct=" + (otherAcct ?? "?")
                          + "). Refusing the second claim — two actors on one scope+account desync the account position "
                          + "and block all new entries. Give them separate accounts, or run one in shadow-record.");
            return !collision;
        }

        /// <summary>Release <paramref name="instanceKey"/> — but ONLY if the registry still holds THIS
        /// <paramref name="owner"/> object (reference-checked, per the re-enable race above).</summary>
        public static void UnregisterActor(string instanceKey, object owner)
        {
            if (string.IsNullOrEmpty(instanceKey) || owner == null) return;
            lock (_actorsLock)
            {
                ActorReg cur;
                if (_actors.TryGetValue(instanceKey, out cur) && cur != null && ReferenceEquals(cur.Owner, owner))
                    _actors.Remove(instanceKey);
            }
        }

        /// <summary>Every currently-registered actor (the Cockpit/Dashboard fleet roster reads this).</summary>
        public static List<ActorReg> AllActors()
        {
            lock (_actorsLock) { return new List<ActorReg>(_actors.Values); }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  HELM (v1.34.0 · Phase 5) — the INTERDICTION seam. The trio: Deck = you drive · Bridge = it drives ·
        //  HELM = you grab the wheel WITHOUT stopping the car. Helm owns NOTHING; it publishes an INTENT addressed to
        //  ONE running actor (an instanceKey), and that actor executes it with its OWN order handles and stays the sole
        //  owner. This is the suite's publish/consult idiom pointed the OTHER way — HUMAN INPUT AS A SEAM.
        //
        //  WHY Helm must never touch an order (three CLAUDE.md lessons converge): a managed position closed by a panel
        //  desyncs Position→account ("position not flat" lockout, recovery = disable/re-enable); the managed framework
        //  RE-ASSERTS SetStopLoss so a panel's stop edit won't stick; Account.Change/Cancel on a strategy order throws
        //  "Unable to change order" and (with StopCancelClose) TERMINATES the strategy. ⇒ Helm commands the owner; the
        //  owner acts. An intent addresses a specific actor ⇒ Helm depends on the ACTOR REGISTRY's instanceKey (above).
        //
        //  ONE-SHOT: every intent carries an id + expiry and is CONSUMED by TakeHelmIntent (idempotent — a second take
        //  returns null). A stale intent replayed after a restart is exactly the class of bug that DUPLICATES A STOP, so
        //  consumption + expiry are load-bearing, not hygiene. Intents live in RAM only — a restart drops them (and the
        //  "armed/pending" idea is NEVER persisted; automation must never silently re-arm).
        //
        //  ASYMMETRIC GATE (the consumer applies it): risk-REDUCING intents are fail-OPEN (a human cutting exposure is
        //  never blocked by a stale seam); risk-ADDING intents pass GateEntry fail-CLOSED, exactly like an automated
        //  entry — the Gate does not care whose finger it was. IsRiskReducing/IsRiskAdding answer the UNAMBIGUOUS verbs;
        //  MoveStop/MoveTarget are context-dependent (tighten = reducing, widen = adding) so the OWNER classifies them
        //  by direction against its live position — the seam deliberately does not guess.
        //
        //  HelmState is published BACK by the owner (what it owns, its live stop/target, whether a human overrode) so
        //  Helm's card renders REALITY, not a guess. Keyed by instanceKey, like the actor registry.
        // ─────────────────────────────────────────────────────────────────────
        public enum HelmVerb { Pause, Resume, SkipNext, FlattenNow, MoveStop, MoveTarget, BreakevenNow, Scale, TakeOver, HandBack }

        public sealed class HelmIntent
        {
            public string    Id;           // unique per issue — the idempotency + Ledger key (auto-filled if empty)
            public string    InstanceKey;  // the addressed actor (<class>#<scope>@<account>); stamped by SetHelmIntent
            public HelmVerb   Verb;
            public double    Price;        // MoveStop/MoveTarget target price (0 = n/a)
            public int       QtyDelta;     // Scale: +add / −reduce contracts (0 = n/a)
            public string    Reason;       // free text for the Ledger/audit ("manual: news spike")
            public DateTime  IssuedUtc;
            public DateTime  ExpiryUtc;    // in the past ⇒ dead on arrival (the replay fail-safe)

            public bool IsExpired => DateTime.UtcNow > ExpiryUtc;

            /// <summary>TRUE only for verbs that ALWAYS reduce exposure ⇒ the owner may honor them fail-OPEN.
            /// MoveStop/MoveTarget return FALSE here because a stop can be tightened (reducing) or widened (adding) —
            /// the owner classifies those against its live position.</summary>
            public bool IsRiskReducing
            {
                get
                {
                    switch (Verb)
                    {
                        case HelmVerb.FlattenNow:
                        case HelmVerb.Pause:
                        case HelmVerb.SkipNext:
                        case HelmVerb.BreakevenNow:
                        case HelmVerb.HandBack:  return true;
                        case HelmVerb.Scale:     return QtyDelta < 0;   // scaling DOWN reduces
                        default:                 return false;
                    }
                }
            }

            /// <summary>TRUE only for verbs that ALWAYS add exposure ⇒ the owner must route them through GateEntry
            /// fail-CLOSED. (MoveStop/MoveTarget again excluded — context-dependent.)</summary>
            public bool IsRiskAdding
            {
                get
                {
                    switch (Verb)
                    {
                        case HelmVerb.Resume:
                        case HelmVerb.TakeOver:  return true;
                        case HelmVerb.Scale:     return QtyDelta > 0;   // scaling UP adds
                        default:                 return false;
                    }
                }
            }
        }

        /// <summary>Truth published BACK by the owning actor so Helm renders reality (never a guess).</summary>
        public sealed class HelmState
        {
            public string    InstanceKey;
            public string    Instrument;
            public string    Account;
            public string    Scope;
            public int       PositionQty;   // signed: + long / − short / 0 flat
            public double    AvgPrice;
            public double    StopPrice;      // live working stop (0 = none)
            public double    TargetPrice;    // live working target (0 = none)
            public bool      Paused;         // the owner is honoring a Pause
            public bool      SkipArmed;      // the owner will skip its next entry
            public bool      HumanOverride;  // a human moved something THIS episode → the sample is interdicted
            public string    LastIntentId;   // the last intent the owner consumed (echo — the card's idempotency proof)
            public string    Status;         // short human line ("running" / "paused" / "flat")
            public DateTime  UpdatedUtc;
        }

        private const double HelmIntentTtlSec = 120;   // default life of an intent if the issuer sets no expiry
        private static readonly Dictionary<string, Queue<HelmIntent>> _helmIntents =
            new Dictionary<string, Queue<HelmIntent>>(StringComparer.Ordinal);
        private static readonly object _helmLock = new object();
        private static readonly Dictionary<string, HelmState> _helmStates =
            new Dictionary<string, HelmState>(StringComparer.Ordinal);
        private static readonly object _helmStateLock = new object();

        /// <summary>PUBLISH an intent addressed to a running actor (the Helm card calls this). The owner drains it via
        /// TakeHelmIntent. Auto-fills Id/IssuedUtc/ExpiryUtc if unset. FIFO-queued so a MoveStop then FlattenNow are both
        /// delivered (a human's second click is never silently dropped).</summary>
        public static void SetHelmIntent(string instanceKey, HelmIntent intent)
        {
            if (string.IsNullOrEmpty(instanceKey) || intent == null) return;
            if (string.IsNullOrEmpty(intent.Id)) intent.Id = Guid.NewGuid().ToString("N").Substring(0, 12);
            intent.InstanceKey = instanceKey;
            if (intent.IssuedUtc == default(DateTime)) intent.IssuedUtc = DateTime.UtcNow;
            if (intent.ExpiryUtc == default(DateTime)) intent.ExpiryUtc = intent.IssuedUtc.AddSeconds(HelmIntentTtlSec);
            lock (_helmLock)
            {
                Queue<HelmIntent> q;
                if (!_helmIntents.TryGetValue(instanceKey, out q) || q == null)
                    _helmIntents[instanceKey] = q = new Queue<HelmIntent>();
                q.Enqueue(intent);
            }
            Log("Core", "HELM INTENT '" + intent.Verb + "' → " + instanceKey + " (id=" + intent.Id
                      + (string.IsNullOrEmpty(intent.Reason) ? "" : ", " + intent.Reason) + ")");
        }

        /// <summary>CONSUME the next live intent for this actor (FIFO), silently discarding any that expired while
        /// queued. Returns null when empty — idempotent: a taken intent is gone. Drain in a loop each pass:
        /// <c>while ((i = SentinelCore.TakeHelmIntent(key)) != null) Apply(i);</c></summary>
        public static HelmIntent TakeHelmIntent(string instanceKey)
        {
            if (string.IsNullOrEmpty(instanceKey)) return null;
            lock (_helmLock)
            {
                Queue<HelmIntent> q;
                if (!_helmIntents.TryGetValue(instanceKey, out q) || q == null) return null;
                while (q.Count > 0)
                {
                    HelmIntent it = q.Dequeue();
                    if (it != null && !it.IsExpired) return it;   // skip (drop) anything that expired in the queue
                }
                return null;
            }
        }

        /// <summary>Non-consuming count of live (unexpired) intents queued for this actor — the Helm card's "2 pending"
        /// badge, without draining them.</summary>
        public static int PendingHelmIntents(string instanceKey)
        {
            if (string.IsNullOrEmpty(instanceKey)) return 0;
            lock (_helmLock)
            {
                Queue<HelmIntent> q;
                if (!_helmIntents.TryGetValue(instanceKey, out q) || q == null) return 0;
                int n = 0;
                foreach (HelmIntent it in q) if (it != null && !it.IsExpired) n++;
                return n;
            }
        }

        /// <summary>The owning actor publishes its live truth back up. Stamps UpdatedUtc so Helm can render staleness.</summary>
        public static void SetHelmState(string instanceKey, HelmState state)
        {
            if (string.IsNullOrEmpty(instanceKey) || state == null) return;
            state.InstanceKey = instanceKey;
            state.UpdatedUtc  = DateTime.UtcNow;
            lock (_helmStateLock) { _helmStates[instanceKey] = state; }
        }

        /// <summary>Latest published truth for an actor, or null if absent / older than maxAgeSec (0 = no age gate).</summary>
        public static HelmState GetHelmState(string instanceKey, double maxAgeSec)
        {
            if (string.IsNullOrEmpty(instanceKey)) return null;
            lock (_helmStateLock)
            {
                HelmState s;
                if (!_helmStates.TryGetValue(instanceKey, out s) || s == null) return null;
                if (maxAgeSec > 0 && (DateTime.UtcNow - s.UpdatedUtc).TotalSeconds > maxAgeSec) return null;
                return s;
            }
        }

        public static List<HelmState> AllHelmStates()
        {
            lock (_helmStateLock) { return new List<HelmState>(_helmStates.Values); }
        }

        /// <summary>TEARDOWN — drop this actor's queued intents + published state so a card never renders a ghost.
        /// Call from the owner's Terminate, paired with UnregisterActor.</summary>
        public static void ClearHelm(string instanceKey)
        {
            if (string.IsNullOrEmpty(instanceKey)) return;
            lock (_helmLock)      { _helmIntents.Remove(instanceKey); }
            lock (_helmStateLock) { _helmStates.Remove(instanceKey); }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  MANUAL-ASSIST TICKETS — for prop firms that bar automated copy-trading (TPT eval/PRO,
        //  Bulenox), the Copier can run a follower in MANUAL mode: instead of auto-submitting, it
        //  PUBLISHES a "place this by hand" ticket here. The dashboard Assist tab + state.json read
        //  the recent queue; the user places the trade on their prop platform. Ring buffer (last N).
        // ─────────────────────────────────────────────────────────────────────
        public sealed class AssistTicket
        {
            public DateTime TimeUtc;
            public string   Account;      // follower label (need not be an NT account)
            public string   Action;       // "Buy" / "Sell" / "SellShort" / "BuyToCover"
            public int      Qty;
            public string   Instrument;   // mapped target, e.g. "MGC 08-26"
            public string   Context;      // leader context, e.g. "1 GC buy · Eye L72"
            public bool     IsEntry;
        }

        private static readonly List<AssistTicket> _assist = new List<AssistTicket>();
        private static readonly object _assistLock = new object();
        private const int AssistMax = 50;

        /// <summary>Copier (manual follower) publishes a "place by hand" ticket.</summary>
        public static void PublishAssistTicket(AssistTicket t)
        {
            if (t == null) return;
            lock (_assistLock) { _assist.Add(t); while (_assist.Count > AssistMax) _assist.RemoveAt(0); }
        }

        /// <summary>Most-recent tickets first (max &lt;= 0 = all).</summary>
        public static List<AssistTicket> RecentAssistTickets(int max)
        {
            lock (_assistLock)
            {
                int n = _assist.Count;
                int take = (max > 0 && max < n) ? max : n;
                var outl = new List<AssistTicket>(take);
                for (int i = n - take; i < n; i++) outl.Add(_assist[i]);
                outl.Reverse();   // newest first
                return outl;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  ROLLOVER registry — a health tool (SentinelRisk) computes each monitored
        //  instrument's days-to-roll and PUBLISHES it here; strategies/copier CONSULT
        //  RolloverBlocked() at entry (via CanEnter) to halt new entries N days before the
        //  contract rolls. Key = instrument ROOT (e.g. "ES"). Fail-open: unpublished => not blocked.
        // ─────────────────────────────────────────────────────────────────────
        public sealed class RolloverInfo
        {
            public string   Root;          // instrument root, e.g. "ES"
            public string   Contract;      // full front-month name that was measured, e.g. "ES 09-26"
            public double   DaysToRoll;    // days until the exchange rollover date (can be negative if past)
            public DateTime RollDateLocal; // the computed rollover date (platform-local)
            public bool     Blocked;       // true = within the block buffer (halt new entries)
            public bool     Warn;          // true = within the warn buffer (countdown, still trading)
            public DateTime UpdatedUtc;
        }

        private static readonly Dictionary<string, RolloverInfo> _roll =
            new Dictionary<string, RolloverInfo>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _rollLock = new object();

        public static void SetRollover(RolloverInfo r)
        {
            if (r == null || string.IsNullOrEmpty(r.Root)) return;
            r.UpdatedUtc = DateTime.UtcNow;
            lock (_rollLock) { _roll[r.Root] = r; }
        }

        public static void RemoveRollover(string root)
        {
            if (string.IsNullOrEmpty(root)) return;
            lock (_rollLock) { _roll.Remove(root); }
        }

        public static RolloverInfo GetRollover(string instrument)
        {
            string root = InstrumentRoot(instrument);
            if (root.Length == 0) return null;
            lock (_rollLock) { RolloverInfo r; return _roll.TryGetValue(root, out r) ? r : null; }
        }

        public static bool RolloverBlocked(string instrument)
        {
            var r = GetRollover(instrument);
            return r != null && r.Blocked;
        }

        public static List<RolloverInfo> AllRollovers()
        {
            lock (_rollLock) { return new List<RolloverInfo>(_roll.Values); }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  NEWS-LOCKOUT registry — a health tool publishes the CURRENTLY-ACTIVE news
        //  lockout window(s) (FOMC/NFP/CPI…). Strategies/copier consult NewsLockoutActive()
        //  at entry (via CanEnter) to block entries in a window around scheduled events.
        //  Publisher replaces the full active set each refresh. Scope = instrument roots the
        //  lockout applies to (null/empty = ALL instruments). Fail-open: nothing published => open.
        // ─────────────────────────────────────────────────────────────────────
        public sealed class NewsLockout
        {
            public string    Event;        // e.g. "FOMC"
            public DateTime  StartLocal;   // window open (platform-local)
            public DateTime  EndLocal;     // window close
            public string[]  Scope;        // instrument roots, or null/empty = all
            public DateTime  UpdatedUtc;
        }

        private static volatile List<NewsLockout> _newsActive = new List<NewsLockout>();

        /// <summary>Publisher sets the full set of CURRENTLY-active lockouts (empty/null = none active).</summary>
        public static void SetNewsLockouts(List<NewsLockout> active)
        {
            var list = active ?? new List<NewsLockout>();
            foreach (var n in list) if (n != null) n.UpdatedUtc = DateTime.UtcNow;
            _newsActive = list;   // atomic reference swap; readers never see a torn list
        }

        public static List<NewsLockout> ActiveNewsLockouts()
        {
            return new List<NewsLockout>(_newsActive);
        }

        /// <summary>The active lockout that applies to this instrument (root-scoped), or null.</summary>
        public static NewsLockout ActiveNewsLockoutFor(string instrument)
        {
            string root = InstrumentRoot(instrument);
            var list = _newsActive;
            foreach (var n in list)
            {
                if (n == null) continue;
                if (n.Scope == null || n.Scope.Length == 0) return n;   // applies to all
                if (root.Length == 0) continue;
                foreach (var sc in n.Scope)
                    if (string.Equals(sc, root, StringComparison.OrdinalIgnoreCase)) return n;
            }
            return null;
        }

        public static bool NewsLockoutActive(string instrument)
        {
            return ActiveNewsLockoutFor(instrument) != null;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  WATCH registry — a chart strategy/indicator REGISTERS the exact Instrument it
        //  trades so SentinelRisk monitors that feed's lag/stall even when the account is
        //  FLAT (a flat leader's stalled chart feed otherwise goes uncaught — real gap seen
        //  2026-07-02). Register in OnStateChange(Realtime/Terminated). Idempotent by FullName.
        // ─────────────────────────────────────────────────────────────────────
        private static readonly Dictionary<string, Instrument> _watch =
            new Dictionary<string, Instrument>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, int> _watchRefs =   // REF-COUNTED: many instances may
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);  // watch the same instrument (14+ seen)
        private static readonly object _watchLock = new object();

        public static void RegisterWatchInstrument(Instrument instr)
        {
            if (instr == null) return;
            string key; try { key = instr.FullName; } catch { return; }
            if (string.IsNullOrEmpty(key)) return;
            lock (_watchLock)
            {
                _watch[key] = instr;
                int n; _watchRefs.TryGetValue(key, out n);
                _watchRefs[key] = n + 1;
            }
        }

        public static void UnregisterWatchInstrument(Instrument instr)
        {
            if (instr == null) return;
            string key; try { key = instr.FullName; } catch { return; }
            if (string.IsNullOrEmpty(key)) return;
            lock (_watchLock)
            {
                int n;
                if (!_watchRefs.TryGetValue(key, out n)) { _watch.Remove(key); return; }
                if (n <= 1) { _watchRefs.Remove(key); _watch.Remove(key); }   // last owner → drop the watch
                else        { _watchRefs[key] = n - 1; }                       // others still watching
            }
        }

        public static List<Instrument> WatchedInstruments()
        {
            lock (_watchLock) { return new List<Instrument>(_watch.Values); }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  CONFIG-USE registry (v1.0.6) — a running strategy that auto-read a lab .conf publishes
        //  what it loaded so the dashboard shows which INSTANCE is on which config. Keyed by a
        //  caller-supplied id (e.g. strategy|instrument|account). Publish on apply; remove on Terminated.
        // ─────────────────────────────────────────────────────────────────────
        public sealed class ConfigUse
        {
            public string   Key;         // unique per running instance (strategy|instrument|account)
            public string   Strategy, Instrument, Account, ConfigName;
            public int      Tp, Sl;
            public DateTime UpdatedUtc;
        }

        private static readonly Dictionary<string, ConfigUse> _configUse =
            new Dictionary<string, ConfigUse>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _configUseLock = new object();

        public static void SetConfigUse(ConfigUse c)
        {
            if (c == null || string.IsNullOrEmpty(c.Key)) return;
            c.UpdatedUtc = DateTime.UtcNow;
            lock (_configUseLock) { _configUse[c.Key] = c; }
        }

        public static void RemoveConfigUse(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            lock (_configUseLock) { _configUse.Remove(key); }
        }

        public static List<ConfigUse> AllConfigUses()
        {
            lock (_configUseLock) { return new List<ConfigUse>(_configUse.Values); }
        }


        // ═════════════════════════════════════════════════════════════════════
        //  THE STATE LEDGER (v1.1.0) — Substrate 2. The single append-only event stream
        //  every tool writes to: orders, fills, and automated actions (kill / governor /
        //  gate-block / auto-flatten). One daily JSONL at <SettingsDir>\Ledger\ledger-YYYY-MM-DD.jsonl.
        //  The trade journal, action audit, and slippage analysis are all VIEWS of THIS stream
        //  (built next) — so nothing needs a second journal. Writes are ASYNC (never in the submit
        //  path). See Docs/SENTINEL_HARDENING_FRAMEWORK.md (Substrate 2).
        // ═════════════════════════════════════════════════════════════════════
        public static class Ledger
        {
            private static readonly object _io = new object();

            /// <summary>The Ledger directory (&lt;SettingsDir&gt;\Ledger). One JSONL per local calendar day.</summary>
            public static string Dir { get { return Path.Combine(SettingsDir, "Ledger"); } }
            /// <summary>The JSONL file for a given LOCAL date (files are keyed by local date; timestamps inside are UTC).</summary>
            public static string FileFor(DateTime day) { return Path.Combine(Dir, "ledger-" + day.ToString("yyyy-MM-dd") + ".jsonl"); }

            // v1.25.0 (ML spec §10.7) — the additive CONTEXT block: the episode join key + the actor instanceKey,
            // emitted on order/action/fill alike so Lens can finally join a FILL → its EPISODE → the verdict that
            // caused it. Optional params (default null) → every prior caller compiles and behaves identically; a row
            // from an actor that does not pass them simply carries no episode/instance field.
            private static string Ctx(string episode, string instance)
            {
                string s = "";
                if (!string.IsNullOrEmpty(episode))  s += ",\"episode\":\"" + J(episode) + "\"";
                if (!string.IsNullOrEmpty(instance)) s += ",\"instance\":\"" + J(instance) + "\"";
                return s;
            }

            /// <summary>Record an order submission (or exit). <paramref name="episode"/>/<paramref name="instance"/>
            /// are the ML-spec §10 join keys (optional; omitted from the row when null).</summary>
            public static void Order(string account, string instrument, string action, string type, int qty, double price, string tag,
                                     string episode = null, string instance = null)
                => Write("order", account,
                    "\"instr\":\"" + J(instrument) + "\",\"action\":\"" + J(action) + "\",\"type\":\"" + J(type)
                    + "\",\"qty\":" + qty + ",\"px\":" + price.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture)
                    + ",\"tag\":\"" + J(tag) + "\"" + Ctx(episode, instance));

            /// <summary>Record an automated action / state change (kill, governor halt, gate block, auto-flatten).</summary>
            public static void Action(string kind, string account, string detail, string episode = null, string instance = null)
                => Write("action", account, "\"kind\":\"" + J(kind) + "\",\"detail\":\"" + J(detail) + "\"" + Ctx(episode, instance));

            /// <summary>Record a FILL with execution quality: the <paramref name="intended"/> (order) price
            /// vs the actual <paramref name="fill"/> price. <c>slip</c> is stored in ADVERSE ticks
            /// (+ = worse than intended, − = price improvement), sign-corrected for side; it is OMITTED
            /// when intended/tickSize are unknown (e.g. a pure market order — nothing to compare to).
            /// The slippage / execution-quality view reads these. Async, like the others.</summary>
            public static void Fill(string account, string instrument, string action, int qty, double intended, double fill, double tickSize, string tag,
                                    string episode = null, string instance = null)
            {
                string slipJson = "";
                if (intended > 0 && tickSize > 0)
                {
                    double raw = (fill - intended) / tickSize;    // + = filled ABOVE the intended price
                    bool buy = action != null && action.IndexOf("Buy", StringComparison.OrdinalIgnoreCase) >= 0;
                    double adverse = buy ? raw : -raw;            // + = worse execution regardless of side
                    slipJson = ",\"slip\":" + adverse.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                }
                Write("fill", account,
                    "\"instr\":\"" + J(instrument) + "\",\"action\":\"" + J(action) + "\",\"qty\":" + qty
                    + ",\"intended\":" + intended.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture)
                    + ",\"fill\":" + fill.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture)
                    + slipJson + ",\"tag\":\"" + J(tag) + "\"" + Ctx(episode, instance));
            }

            private static void Write(string evt, string account, string data)
            {
                string line;
                try
                {
                    line = "{\"ts\":\"" + DateTime.UtcNow.ToString("o") + "\",\"evt\":\"" + evt
                         + "\",\"acct\":\"" + J(account) + "\"," + data + "}";
                }
                catch { return; }
                string dir, file;
                try { dir = Dir; file = FileFor(DateTime.Now); }
                catch { return; }
                System.Threading.Tasks.Task.Run(() =>
                {
                    try { lock (_io) { Directory.CreateDirectory(dir); File.AppendAllText(file, line + "\r\n"); } } catch { }
                });
            }
            private static string J(string s) => s == null ? "" : s.Replace("\\", "\\\\").Replace("\"", "\\\"");

            // ── THE READ SIDE (v1.1.0) — typed access so journal / audit / slippage are VIEWS ──
            /// <summary>One parsed ledger row. Order rows carry Instrument/Action/Type/Qty/Price/Tag;
            /// action rows carry Kind/Detail. All timestamps stored UTC (see <see cref="TimeLocal"/>).</summary>
            public sealed class Entry
            {
                public DateTime TimeUtc;
                public string   Evt;          // "order" | "action" | "fill"
                public string   Account;
                // order + fill fields
                public string   Instrument, Action, Type, Tag;
                public int      Qty;
                public double   Price;
                // fill fields
                public double   IntendedPrice, FillPrice, SlipTicks;
                public bool     HasSlip;      // slip was computable (intended + tickSize known)
                // action fields
                public string   Kind, Detail;
                public string   Raw;
                public DateTime TimeLocal { get { return TimeUtc.ToLocalTime(); } }
                public bool     IsOrder  { get { return Evt == "order"; } }
                public bool     IsFill   { get { return Evt == "fill"; } }
                public bool     IsAction { get { return Evt == "action"; } }
                /// <summary>An action row raised by SentinelCore.Alerts (kind "ALERT-CRIT" or "alert").</summary>
                public bool     IsAlert  { get { return Evt == "action" && (Kind == "ALERT-CRIT" || Kind == "alert"); } }
                public bool     IsCritical { get { return Kind == "ALERT-CRIT"; } }
            }

            /// <summary>Parse one JSONL line into an <see cref="Entry"/>. Returns null on malformed input
            /// (tolerant of a torn final line from the async writer). Reads the exact flat shape we write.</summary>
            public static Entry Parse(string line)
            {
                if (string.IsNullOrEmpty(line)) return null;
                Dictionary<string, string> m;
                try { m = ParseFlat(line); } catch { return null; }
                if (m == null) return null;
                string evt; if (!m.TryGetValue("evt", out evt) || string.IsNullOrEmpty(evt)) return null;
                var e = new Entry { Raw = line, Evt = evt };
                string ts; m.TryGetValue("ts", out ts);
                DateTime t;
                if (!string.IsNullOrEmpty(ts) && DateTime.TryParse(ts, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.RoundtripKind, out t))
                    e.TimeUtc = t.ToUniversalTime();
                m.TryGetValue("acct", out e.Account);
                if (evt == "order")
                {
                    m.TryGetValue("instr", out e.Instrument);
                    m.TryGetValue("action", out e.Action);
                    m.TryGetValue("type", out e.Type);
                    m.TryGetValue("tag", out e.Tag);
                    string q, px;
                    if (m.TryGetValue("qty", out q)) int.TryParse(q, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out e.Qty);
                    if (m.TryGetValue("px", out px)) double.TryParse(px, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out e.Price);
                }
                else if (evt == "fill")
                {
                    m.TryGetValue("instr", out e.Instrument);
                    m.TryGetValue("action", out e.Action);
                    m.TryGetValue("tag", out e.Tag);
                    string q, ip, fp, sl;
                    if (m.TryGetValue("qty", out q)) int.TryParse(q, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out e.Qty);
                    if (m.TryGetValue("intended", out ip)) double.TryParse(ip, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out e.IntendedPrice);
                    if (m.TryGetValue("fill", out fp)) double.TryParse(fp, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out e.FillPrice);
                    if (m.TryGetValue("slip", out sl) && double.TryParse(sl, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out e.SlipTicks)) e.HasSlip = true;
                }
                else
                {
                    m.TryGetValue("kind", out e.Kind);
                    m.TryGetValue("detail", out e.Detail);
                }
                return e;
            }

            /// <summary>All entries for one LOCAL calendar day, in write order. Empty if no file.</summary>
            public static List<Entry> ReadDay(DateTime day)
            {
                var list = new List<Entry>();
                ReadInto(FileFor(day), list);
                return list;
            }

            /// <summary>All entries for the last <paramref name="days"/> local days (incl. today),
            /// chronological (oldest first). Safe to call on the UI thread — files are small and reads are on-demand.</summary>
            public static List<Entry> ReadRecent(int days)
            {
                if (days < 1) days = 1;
                var list = new List<Entry>();
                DateTime today = DateTime.Now.Date;
                for (int k = days - 1; k >= 0; k--) ReadInto(FileFor(today.AddDays(-k)), list);
                return list;
            }

            private static void ReadInto(string file, List<Entry> into)
            {
                try
                {
                    if (!File.Exists(file)) return;
                    string[] lines;
                    lock (_io) { lines = File.ReadAllLines(file); }   // share the write lock → never read a torn append
                    foreach (var ln in lines)
                    {
                        if (string.IsNullOrWhiteSpace(ln)) continue;
                        var e = Parse(ln);
                        if (e != null) into.Add(e);
                    }
                }
                catch { }
            }

            // Minimal FLAT-json reader for our own single-level {"k":"v","k":n,...} lines (no nesting).
            // Unescapes the two sequences our writer emits (\\ and \") plus common whitespace escapes.
            private static Dictionary<string, string> ParseFlat(string s)
            {
                var d = new Dictionary<string, string>(StringComparer.Ordinal);
                int i = 0, n = s.Length;
                while (i < n && s[i] != '{') i++;
                if (i >= n) return null;
                i++; // past '{'
                while (i < n)
                {
                    while (i < n && (s[i] == ',' || s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) i++;
                    if (i >= n || s[i] == '}') break;
                    if (s[i] != '"') break; // malformed key
                    string key = ReadJsonString(s, ref i);
                    if (key == null) break;
                    while (i < n && s[i] != ':') i++;
                    if (i >= n) break;
                    i++; // past ':'
                    while (i < n && (s[i] == ' ' || s[i] == '\t')) i++;
                    string val = (i < n && s[i] == '"') ? ReadJsonString(s, ref i) : ReadBare(s, ref i);
                    d[key] = val;
                }
                return d;
            }
            private static string ReadJsonString(string s, ref int i)
            {
                if (i >= s.Length || s[i] != '"') return null;
                i++;
                var sb = new StringBuilder();
                while (i < s.Length)
                {
                    char c = s[i++];
                    if (c == '\\')
                    {
                        if (i >= s.Length) break;
                        char e = s[i++];
                        if (e == 'n') sb.Append('\n');
                        else if (e == 't') sb.Append('\t');
                        else if (e == 'r') sb.Append('\r');
                        else sb.Append(e);           // \" \\ \/ and anything else → literal
                    }
                    else if (c == '"') break;
                    else sb.Append(c);
                }
                return sb.ToString();
            }
            private static string ReadBare(string s, ref int i)
            {
                int start = i;
                while (i < s.Length && s[i] != ',' && s[i] != '}') i++;
                return s.Substring(start, i - start).Trim();
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  THE INTENDED-STATE STORE (v1.1.0) — Substrate 2's second half. A tiny KEYED, SYNCHRONOUS,
        //  atomic blob store so a tool's in-memory management state (trail high-water, breakeven-armed,
        //  active stop price) SURVIVES A RESTART. One file per key at <SettingsDir>\State\<safeKey>.json.
        //  Distinct from the async event Ledger: the Ledger is an append-only HISTORY; this is the
        //  latest SNAPSHOT of what we believe should be true, overwritten in place. Consumers key by
        //  their own identity (e.g. "GTrader21|<account>|<instrument>"), Save() on every state change,
        //  Load() on restart, Clear() when the position closes. See Docs/SENTINEL_HARDENING_FRAMEWORK.md
        //  (Substrate 2 — "intended-state store" + reconcile). Reconciliation is the CONSUMER's job:
        //  restore only after verifying the account actually still holds a matching position.
        // ═════════════════════════════════════════════════════════════════════
        public static class State
        {
            private static readonly object _io = new object();
            public static string Dir { get { return Path.Combine(SettingsDir, "State"); } }
            private static string FileFor(string key) { return Path.Combine(Dir, Safe(key) + ".json"); }
            private static string Safe(string key)
            {
                if (string.IsNullOrEmpty(key)) return "_";
                var sb = new StringBuilder(key.Length);
                foreach (char c in key) sb.Append((char.IsLetterOrDigit(c) || c == '-' || c == '_') ? c : '_');
                return sb.ToString();
            }

            /// <summary>Persist a blob under key (atomic tmp→move; overwrites). Safe to call often.</summary>
            public static void Save(string key, string json)
            {
                if (string.IsNullOrEmpty(key)) return;
                string dir = Dir, file = FileFor(key), tmp = file + ".tmp";
                try
                {
                    lock (_io)
                    {
                        Directory.CreateDirectory(dir);
                        File.WriteAllText(tmp, json ?? "", Encoding.UTF8);
                        if (File.Exists(file)) File.Delete(file);
                        File.Move(tmp, file);
                    }
                }
                catch { try { lock (_io) File.WriteAllText(file, json ?? "", Encoding.UTF8); } catch { } }
            }

            /// <summary>Read a persisted blob, or null if none / unreadable.</summary>
            public static string Load(string key)
            {
                if (string.IsNullOrEmpty(key)) return null;
                try { lock (_io) { string f = FileFor(key); return File.Exists(f) ? File.ReadAllText(f, Encoding.UTF8) : null; } }
                catch { return null; }
            }

            /// <summary>Delete a persisted blob (call when the position closes / state no longer applies).</summary>
            public static void Clear(string key)
            {
                if (string.IsNullOrEmpty(key)) return;
                try { lock (_io) { string f = FileFor(key); if (File.Exists(f)) File.Delete(f); } }
                catch { }
            }

            /// <summary>Age of a key's file since last write, or TimeSpan.MaxValue if none (stale-guard on restore).</summary>
            public static TimeSpan Age(string key)
            {
                try { string f = FileFor(key); if (File.Exists(f)) return DateTime.UtcNow - File.GetLastWriteTimeUtc(f); }
                catch { }
                return TimeSpan.MaxValue;
            }

            // ── convenience: store/read a flat string→string map so consumers need no JSON code ──
            /// <summary>Persist a flat string map (all values stored as JSON strings).</summary>
            public static void SaveMap(string key, System.Collections.Generic.IDictionary<string, string> map)
            {
                var sb = new StringBuilder("{");
                int n = 0;
                if (map != null) foreach (var kv in map)
                {
                    if (n++ > 0) sb.Append(',');
                    sb.Append('"').Append(Esc(kv.Key)).Append("\":\"").Append(Esc(kv.Value)).Append('"');
                }
                sb.Append('}');
                Save(key, sb.ToString());
            }

            /// <summary>Read a flat string map back (null if no file). Pairs with <see cref="SaveMap"/>.</summary>
            public static System.Collections.Generic.Dictionary<string, string> LoadMap(string key)
            {
                string s = Load(key);
                if (string.IsNullOrEmpty(s)) return null;
                var d = new Dictionary<string, string>(StringComparer.Ordinal);
                int i = 0, n = s.Length;
                while (i < n && s[i] != '{') i++;
                if (i >= n) return d;
                i++;
                while (i < n)
                {
                    while (i < n && (s[i] == ',' || s[i] == ' ' || s[i] == '\r' || s[i] == '\n' || s[i] == '\t')) i++;
                    if (i >= n || s[i] == '}') break;
                    if (s[i] != '"') break;
                    string k = ReadStr(s, ref i); if (k == null) break;
                    while (i < n && s[i] != ':') i++; if (i >= n) break; i++;
                    while (i < n && s[i] == ' ') i++;
                    string v = (i < n && s[i] == '"') ? ReadStr(s, ref i) : null;
                    d[k] = v;
                }
                return d;
            }

            private static string Esc(string s) => s == null ? "" : s.Replace("\\", "\\\\").Replace("\"", "\\\"");
            private static string ReadStr(string s, ref int i)
            {
                if (i >= s.Length || s[i] != '"') return null;
                i++;
                var sb = new StringBuilder();
                while (i < s.Length)
                {
                    char c = s[i++];
                    if (c == '\\') { if (i < s.Length) sb.Append(s[i++]); }
                    else if (c == '"') break;
                    else sb.Append(c);
                }
                return sb.ToString();
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  ALERTS (v1.1.0) — Substrate 3 (health views). A TWO-TIER notification channel so you
        //  learn about critical events when you're NOT staring at the screen. Over-alerting trains
        //  you to ignore alerts, so Critical is rare BY CONSTRUCTION (kill / auto-flatten / loss-stop
        //  / feed death / naked-position after reconnect); everything else is Info. Consumers (the
        //  dashboard banner, a future sound/push) read Recent()/subscribe to Raised. Also audited to
        //  the Ledger. See Docs/SENTINEL_HARDENING_FRAMEWORK.md.
        // ═════════════════════════════════════════════════════════════════════
        public enum AlertLevel { Info, Critical }
        public sealed class AlertItem
        {
            public DateTime TimeUtc; public AlertLevel Level; public string Title; public string Detail;
            /// <summary>v1.17.0 — which account this alert is ABOUT (null = system-wide, e.g. the kill switch).</summary>
            public string Account;
        }
        public static class Alerts
        {
            private static readonly List<AlertItem> _recent = new List<AlertItem>();
            private static readonly object _lock = new object();
            /// <summary>Fires on every alert (dashboard banner / sound subscribe here).</summary>
            public static event Action<AlertItem> Raised;

            /// <param name="account">The account this alert is ABOUT. v1.17.0: previously the Ledger row was written
            /// with a null account, so all 164 ALERT-CRIT rows — every one of them a NAKED POSITION — recorded
            /// `acct:""`. The most urgent alert in the system could not say which account it concerned.</param>
            public static void Raise(AlertLevel level, string title, string detail, string account = null)
            {
                var a = new AlertItem { TimeUtc = DateTime.UtcNow, Level = level, Title = title, Detail = detail, Account = account };
                lock (_lock) { _recent.Insert(0, a); if (_recent.Count > 100) _recent.RemoveAt(_recent.Count - 1); }
                try { Ledger.Action(level == AlertLevel.Critical ? "ALERT-CRIT" : "alert", account, title + (detail != null ? " — " + detail : "")); } catch { }
                try { Log("Alert", "[" + level + "] " + title + (detail != null ? " — " + detail : "")); } catch { }
                var h = Raised; if (h != null) { try { h(a); } catch { } }
            }
            public static void Critical(string title, string detail = null, string account = null) => Raise(AlertLevel.Critical, title, detail, account);
            public static void Info(string title, string detail = null, string account = null)     => Raise(AlertLevel.Info, title, detail, account);

            public static List<AlertItem> Recent(int n = 25)
            { lock (_lock) { return _recent.GetRange(0, Math.Min(n, _recent.Count)); } }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  CONDITIONS (v1.17.0) — the missing abstraction behind every "warn once" bug in this suite.
        //
        //  Three different things were all being written as `if (set.Add(key)) Warn();`, and only ONE of them
        //  is correctly a latch:
        //    • ACTION LATCH     "do this once"          → latch, clear on day roll.  (_hardFlattened: correct)
        //    • TRANSITION LOG   "say when it changed"   → fire on change.            (_govPrevStatus: correct)
        //    • CONDITION ALERT  "something is wrong NOW"→ THIS. It must debounce transients, report, keep
        //                                                  re-stating on a cooldown while it stays true, and
        //                                                  auto-clear when it resolves.
        //
        //  Every condition alert in the suite got that last row wrong, in all three possible ways:
        //    - naked-position    : no debounce  → a stop mid-modify looked naked → 160 false CRITICAL alerts
        //    - orphan-orders     : broken latch → re-added every scan → an alert every 2 s
        //    - scope contention  : latched forever → reported once, then permanently blind
        //    - ambiguous scope   : latched forever → fails CLOSED on every call, explains itself once
        //
        //  A latch that never re-arms is indistinguishable from a detector that never fires. Route condition
        //  alerts through here and the next one is correct by construction rather than by remembering.
        //
        //  USAGE — call EVERY evaluation, with the live truth value:
        //      if (SentinelCore.Conditions.ShouldReport(acct + "|naked|" + instr, isNaked, 10, 300))
        //          Alerts.Critical(...);          // suppressed <10s; then re-stated every 5 min while true
        //
        //  For EVENT-shaped detections that have no "false" observation to feed back (scope contention only
        //  observes itself at write time), call it with isTrue:true whenever detected and Clear(key) on teardown.
        // ─────────────────────────────────────────────────────────────────────
        public static class Conditions
        {
            private sealed class Cond
            {
                public DateTime FirstTrueUtc;    // when this episode began (the debounce basis)
                public DateTime LastReportUtc;   // when we last spoke (the cooldown basis)
                public bool     Reported;        // has this episode been reported at all
            }
            private static readonly Dictionary<string, Cond> _conds =
                new Dictionary<string, Cond>(StringComparer.OrdinalIgnoreCase);
            private static readonly object _condLock = new object();

            /// <summary>Should this condition be reported right now?</summary>
            /// <param name="key">Stable identity of the condition, e.g. "SimBURN-1|naked|GC 08-26".</param>
            /// <param name="isTrue">The condition's CURRENT truth. Passing false ends the episode and re-arms it.</param>
            /// <param name="debounceSec">Ignore the condition until it has been continuously true this long.
            /// This is what stops a stop-order mid-modify from being reported as a naked position.</param>
            /// <param name="cooldownSec">While it stays true, re-state it at most this often. 0 = report once
            /// per episode. Never leave a CRITICAL condition on 0 — silence would then mean "still broken".</param>
            public static bool ShouldReport(string key, bool isTrue, double debounceSec = 0, double cooldownSec = 0)
            {
                if (string.IsNullOrEmpty(key)) return false;
                var now = DateTime.UtcNow;
                lock (_condLock)
                {
                    if (!isTrue) { _conds.Remove(key); return false; }   // resolved → the episode ends, and re-arms

                    Cond c;
                    if (!_conds.TryGetValue(key, out c))
                    {
                        c = new Cond { FirstTrueUtc = now };
                        _conds[key] = c;
                    }

                    // Not yet continuously true for long enough — a transient, not a condition.
                    if (debounceSec > 0 && (now - c.FirstTrueUtc).TotalSeconds < debounceSec) return false;

                    if (!c.Reported) { c.Reported = true; c.LastReportUtc = now; return true; }

                    if (cooldownSec > 0 && (now - c.LastReportUtc).TotalSeconds >= cooldownSec)
                    { c.LastReportUtc = now; return true; }

                    return false;
                }
            }

            /// <summary>Forget a condition (e.g. its owner tore down), so the next occurrence reports afresh.</summary>
            public static void Clear(string key)
            {
                if (string.IsNullOrEmpty(key)) return;
                lock (_condLock) { _conds.Remove(key); }
            }

            /// <summary>Forget every condition whose key starts with this prefix — e.g. all of one account's.</summary>
            public static void ClearPrefix(string prefix)
            {
                if (string.IsNullOrEmpty(prefix)) return;
                lock (_condLock)
                {
                    var doomed = new List<string>();
                    foreach (var k in _conds.Keys)
                        if (k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) doomed.Add(k);
                    foreach (var k in doomed) _conds.Remove(k);
                }
            }

            /// <summary>Is this condition currently in an episode (true and past its debounce, or awaiting it)?</summary>
            public static bool IsActive(string key)
            {
                if (string.IsNullOrEmpty(key)) return false;
                lock (_condLock) { return _conds.ContainsKey(key); }
            }

            /// <summary>How long the condition has been continuously true. Zero when not active.</summary>
            public static TimeSpan ActiveFor(string key)
            {
                if (string.IsNullOrEmpty(key)) return TimeSpan.Zero;
                lock (_condLock)
                {
                    Cond c;
                    if (!_conds.TryGetValue(key, out c)) return TimeSpan.Zero;
                    return DateTime.UtcNow - c.FirstTrueUtc;
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  SEAM STORE (v1.18.0 · execution plan 1.4) — the one keyed store every sensor seam lives in.
        //
        //  Every seam was keyed by MASTER INSTRUMENT NAME alone, so two charts on one instrument overwrote each
        //  other's sensor readings and a Council could fuse the OTHER chart's ADX. CouncilState was migrated to
        //  SCOPE keys in v1.15.0; this migrates the sensors, which is the half that actually feeds the verdict.
        //
        //  Fifteen seams had fifteen hand-written copies of dictionary + lock + expiry + resolver. One bug fixed
        //  in one of them stayed broken in the other fourteen. They now share this.
        //
        //  RESOLUTION ORDER in Get(), and why each rung exists:
        //    1. exact key hit                       — a migrated publisher, consulted by scope. The normal path.
        //    2. key is a SCOPE, entry is by INSTRUMENT — a publisher that has NOT been migrated yet. This rung is
        //       what lets the migration proceed in BATCHES: a scope-aware consumer keeps finding a legacy sensor
        //       instead of going blind between F5s. It is a TEMPORARY shim; it disappears when the last publisher
        //       moves, and it is the reason a half-migrated tree still trades correctly.
        //    3. key is a bare INSTRUMENT, entries are by scope — resolve ONLY if exactly one scope carries it,
        //       else null + a throttled log. FAIL-CLOSED: "I don't know which chart you mean" must never be
        //       answered with "here's whichever wrote last."
        // ─────────────────────────────────────────────────────────────────────
        /// <summary>Re-state an unresolvable bare-instrument seam lookup at most this often.</summary>
        private const double SeamAmbiguityRestateSec = 600.0;

        /// <summary>The instrument part of a scope ("GC.69697v6x24" → "GC"), or null if this is not a scope.
        /// BarTag() is alphanumeric-only, so the separator is always the LAST '.'.</summary>
        private static string InstrumentOfScope(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            int i = key.LastIndexOf('.');
            return (i > 0 && i < key.Length - 1) ? key.Substring(0, i) : null;
        }

        private sealed class SeamStore<T> where T : class
        {
            private readonly Dictionary<string, T> _map = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
            private readonly object _lock = new object();
            private readonly string _seam;                  // "Adx" — for the ambiguity log
            private readonly Func<T, string> _instrumentOf;
            private readonly Func<T, DateTime> _updatedOf;
            private readonly Action<T, DateTime> _setUpdated;   // null ⇒ this seam cannot heartbeat

            public SeamStore(string seam, Func<T, string> instrumentOf, Func<T, DateTime> updatedOf,
                             Action<T, DateTime> setUpdated = null)
            { _seam = seam; _instrumentOf = instrumentOf; _updatedOf = updatedOf; _setUpdated = setUpdated; }

            /// <summary>HEARTBEAT — re-stamp the cached reading's freshness without recomputing it.
            ///
            /// An OnBarClose sensor only republishes when a bar closes. In a quiet market bars close slowly, so a
            /// perfectly healthy sensor's seam ages past the consumer's staleness gate and the voter silently drops
            /// out — the Council's declared roster made this visible as a fully-loaded chart reporting 3/10. The
            /// Council already heartbeats its own verdict; the sensors need the same.
            ///
            /// EXACT KEY ONLY, and never through the scope→instrument shim: a heartbeat must refresh the reading it
            /// owns, never adopt another chart's. Realtime-gated by the caller — re-stamping during historical
            /// replay would stamp wall-clock freshness onto a replayed bar.</summary>
            public void Touch(string scope)
            {
                if (_setUpdated == null || string.IsNullOrEmpty(scope)) return;
                lock (_lock)
                {
                    T s;
                    if (_map.TryGetValue(scope, out s) && s != null) _setUpdated(s, DateTime.UtcNow);
                }
            }

            /// <summary>Publish under <paramref name="key"/> — a scope for a migrated publisher, an instrument for
            /// a legacy one.</summary>
            public void Set(string key, T value)
            {
                if (string.IsNullOrEmpty(key) || value == null) return;
                lock (_lock) { _map[key] = value; }
            }

            public T Get(string scopeOrInstrument, double maxAgeSec)
            {
                if (string.IsNullOrEmpty(scopeOrInstrument)) return null;
                T s; bool ambiguous = false;
                lock (_lock)
                {
                    if (!_map.TryGetValue(scopeOrInstrument, out s) || s == null)
                    {
                        s = null;
                        string inst = InstrumentOfScope(scopeOrInstrument);
                        if (inst != null)
                        {
                            // (2) asked by scope; this publisher still keys by instrument. Migration shim.
                            _map.TryGetValue(inst, out s);
                        }
                        else
                        {
                            // (3) asked by bare instrument; entries are scope-keyed. Unique match, or fail closed.
                            foreach (var v in _map.Values)
                            {
                                if (v == null || !string.Equals(_instrumentOf(v), scopeOrInstrument, StringComparison.OrdinalIgnoreCase))
                                    continue;
                                if (s != null) { s = null; ambiguous = true; break; }
                                s = v;
                            }
                        }
                    }
                }
                if (ambiguous && Conditions.ShouldReport("seam|ambiguous|" + _seam + "|" + scopeOrInstrument,
                                                        true, 0, SeamAmbiguityRestateSec))
                    Log("Core", "AMBIGUOUS SEAM: several scopes publish " + _seam + "State for '" + scopeOrInstrument
                              + "'. A bare-instrument lookup cannot pick one — returning null (fail-closed). "
                              + "Consult by scope (SentinelCore.ScopeOf).");

                if (s == null) return null;
                if (maxAgeSec > 0 && (DateTime.UtcNow - _updatedOf(s)).TotalSeconds > maxAgeSec) return null;
                return s;
            }

            public List<T> All() { lock (_lock) { return new List<T>(_map.Values); } }

            public void ClearScope(string key)
            {
                if (string.IsNullOrEmpty(key)) return;
                lock (_lock) { _map.Remove(key); }
                Conditions.Clear("seam|ambiguous|" + _seam + "|" + key);
            }
        }

    }
}
