// ─────────────────────────────────────────────────────────────────────────────
// This Source Code Form is subject to the terms of the Mozilla Public License,
// v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
// one at https://mozilla.org/MPL/2.0/.
//
// Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
// ─────────────────────────────────────────────────────────────────────────────
#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;   // roster weight parsing (invariant)
using System.IO;              // Roster.conf
using System.Text;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.AddOns.Sentinel;   // SentinelSkin (glass card) + SentinelCore (every sensor seam) + SentinelCardCorner
#endregion

// ═════════════════════════════════════════════════════════════════════════════
//  Council — the Sentinel CONFLUENCE ARBITER ("the brain")                  |   Version v1.11.0
//  File: Council_v1_11_0.cs   |   namespace …Indicators.Sentinel   |   display Name "Council"
//
//  ⚠ NO ORDERS — read-only advisory indicator. It DECIDES, it never trades. Safe to run anywhere.
//
//  WHAT THIS IS — the missing brain of the suite. Every Sentinel sensor already PUBLISHES its own
//  opinion to SentinelCore as a "…State" seam (Trend, ADX, CCI, VolEnvelope, Liquidity, Brick) plus
//  every published sensor seam. Until now each consumer hand-consulted one or two of those ad hoc.
//  The Council FUSES them all into ONE explainable per-instrument verdict and publishes it back as
//  SentinelCore.CouncilState so ANY consumer (Bridge / Deck / Copier / strategies) reads
//  the SAME decision instead of re-deriving confluence.
//
//  THE VERDICT (SentinelCore.CouncilState, SentinelCore ≥ v1.7.0):
//    • Bias        -1/0/+1   the fused direction (0 = no edge / vetoed)
//    • Conviction  0..1      how ALIGNED the DECLARED voters are (1 = every declared voter unanimous; 0 = split /
//                            none / vetoed). ⚠ v1.1.0: the denominator is the DECLARED weight, not the weight that
//                            happened to show up — a MISSING or NEUTRAL voter now DILUTES conviction instead of
//                            vanishing from the denominator. A verdict fused from 2 of 10 voters can no longer
//                            read as near-unanimity.
//    • SizeMult    0..1      suggested size multiplier = Conviction × contextMult. 0 when vetoed, when Bias is 0,
//                            or when Conviction < ConvictionFloor. ⚠ v1.2.0: ALL context damping (Clock · MTF ·
//                            Participation · Location · squeeze · breadth) lives HERE, not in Conviction. The floor
//                            gates on AGREEMENT; a poor context makes the trade SMALLER, never silently absent.
//    • Agree/Disagree/Voters the tally, and a compact Reasons string — the AUDIT of WHY it decided
//
//  HOW IT FUSES (weighted vote — the weights ARE the edge; tune them, then let Lens grade them):
//    Each sensor with a FRESH reading casts a signed vote (+1/-1) with a weight; a stale/absent sensor
//    simply ABSTAINS (fail-open, matching the suite). netScore = Σ(vote × weight). Bias = sign(netScore)
//    past a deadband; Conviction = |netScore| / denomW, where denomW (v1.3.0) is KIND-AWARE: STATE voters
//    always count toward it (neutral is a real reading), a TRIGGER counts only when it fired or is absent —
//    a quiet trigger is absence of evidence, not evidence against. Breadth/squeeze/context damp the SIZE,
//    not the agreement (v1.2.0).
//    ⚠ These price-derived sensors are NOT independent — they largely echo the same OHLC. Conviction is
//    "agreement," which is not the same as "confirmation." The verdict gets genuinely smarter only as the
//    ORTHOGONAL axes land (Clock/Location/Participation/MTF/Internals/Event — see Docs/ROADMAP.md).
//
//  HARD VETOES (account-free; each zeroes conviction + names itself in VetoReason):
//    global kill · scoped (per-root) kill · rollover block · news lockout · an absorption WALL blocking
//    the intended side (LiquidityState.BlocksEntry). These mirror what CanEnter bundles so the published
//    verdict never reads "LONG 0.9" while news lockout is live. The Council is ADVISORY — a consuming
//    strategy STILL calls its own SentinelCore.GateEntry at submit; the veto here shapes the ADVICE.
//
//  SENTINEL WIRING (Docs/SENTINEL_DESIGN_SYSTEM.md):
//    • PUBLISH: SetCouncilState(...) each update (default ON — the Council exists to publish).
//      v1.15.0 SCOPE KEYING (in-place, exec-plan 1.2): publishes under a SCOPE ("GC.69697v6" =
//      instrument x bartype), NOT a bare instrument, so two GC charts on different bar types no longer
//      overwrite each other's verdict on every tick. Also stamps BarTimeUtc + IsHistorical — UpdatedUtc
//      is wall-clock even during historical replay, so IsHistorical is the ONLY way a consumer can tell
//      a replayed verdict from a live one. The fusion math is untouched.
//    • PLOTS: hidden transparent "Bias" (±1) + "Conviction" (0..1) so Deck SIGNAL ARM / strategies can
//      read the verdict as a generic PLOT (the suite signal-exposure convention), no drawing-scrape.
//    • A SentinelSkin.Painter glass card (CardLayout-docked) + Sentinel palette + label remover.
//    • Records verdict CHANGES to sentinel.log ("Council"); the per-FIRE record belongs to the strategy
//      (write CouncilState into the Ledger/Log ctx at fire time so Lens can grade the confluence).
//
//  CHANGELOG
//    v1.11.0 (REAL FORK, 2026-07-29) — ⭐ A CHART-DERIVED SENSOR MUST BE ON THIS CHART TO INFLUENCE IT.
//             THE BUG, as the user put it: "a standard OHLC sensor like Stochastic Triple Filter is voting on
//             Council without being on the chart. It is not weighted and poisoning the Council."
//             Sensor seams are process-global and keyed BARE (instrument + bar type). That is CORRECT for a value
//             that does not vary with the consuming chart — but an OHLC sensor's reading is computed from ITS OWN
//             chart's bars, so an STF on any other GC/TBars chart was feeding EVERY Council on that instrument+
//             bartype. And the bite was not the vote: `VetoOnChop` hangs off `stf != null` and is INDEPENDENT of
//             WeightStf, which defaults 0 — so a sensor you never attached, at weight zero, could zero your
//             conviction and your sizeMult. Stated intent had always been narrower: the v1.3.1 changelog says the
//             veto is live "only when the STF sensor is loaded", meaning *on this chart*; the code meant *anywhere*.
//             ⚠ AUDIT FIRST — IT WAS NEVER AN STF BUG. Every weight-independent path was checked, and the class is
//             SIX: VetoOnChop (STF) · VetoOnWall (LiquidityWalls) · VetoKillWindow (Clock) · DampenOnSqueeze
//             (VolEnvelope) · FluxAbsorbDamp (Flux) · the whole contextMult chain (Clock/MTF/Participation/
//             Location/Profile/Regime). Gating the veto on the weight — the obvious one-line fix — would have
//             repaired ONE ROW of that table and made the rest look deliberate.
//             THE GATE IS CLASSIFICATION, NOT PRESENCE-FOR-EVERYTHING (the user's distinction, and it is the right
//             one — "sure there is a use case for having a voter influence the Council and not be on that specific
//             chart, but indicators like STF should not be allowed to do it"):
//               • CHART-DERIVED (20: TRND CCI ADX ENV CMP WAE GREV STF STRC EXH AVMA SPRT PSAR ZSC VDYA HARM ARCH
//                 RGME PARTIC MTF) → must be ATTACHED to this chart. No attach ⇒ no vote, no veto, no damp.
//               • INSTRUMENT-LEVEL (Clock · Intermarket · News · LiquidityWalls · CVD · BSP · Level · Profile) →
//                 never gated. They have no chart of their own; requiring attachment would be meaningless.
//               • BAR-TYPE SEAMS (BRK / FLUX / CVB) → exempt. The chart IS that bars type.
//             ⭐ IMPLEMENTATION: the gate wraps the SEAM READ (`Local("STF", GetStfState(...))`), not each
//             modulator. The vote, the veto and the damp all hang off the same `!= null`, so nulling the read
//             closes all three — one mechanism for the whole class, which is exactly what the audit demanded.
//             Presence is scanned on the UI THREAD (ChartControl.Indicators off the data thread throws — memory
//             nt-consume-indicator-plots), async, 5 s throttle, matched by type-name PREFIX so a sensor version
//             bump does not silently un-gate it. ⚠ "SentinelTrend" needs its trailing underscore or it also
//             prefix-matches SentinelTrendArchitect.
//             FAIL DIRECTIONS, both deliberate: no ChartControl at all (headless / offline harness) ⇒ attachment
//             is UNDEFINED, not false ⇒ fail OPEN, because muting a Council that has no chart to be attached to is
//             the "crashed sensor is indistinguishable from a quiet one" failure — and it keeps the replay==live
//             parity gate intact. Scan not yet completed ⇒ fail CLOSED for a beat, so an off-chart sensor cannot
//             sneak one bar in; the scan lands within a bar and the skip is NAMED.
//             OBSERVABILITY: everything gated out is listed in the Reasons audit as `off-chart:STF,ENV` — without
//             it a declared-but-unattached voter looks identical to an attached one that is abstaining, and "why
//             didn't it fire" is unanswerable. New [Display]-only "Require sensor on chart" (default ON).
//             ⚠ REAL FORK, not in-place: file + class + Name + header all move to v1.11.0 together. This also ends
//             a standing lie — the file said Council_v1_0_0 and the card said "Sentinel Council v1.0.0" while the
//             code had been v1.10.0 since 2026-07-24. Council_v1_0_0.cs stays FROZEN as the fallback.
//             ⚠⚠ REMOVE the old Council from a chart BEFORE adding v1.11.0. Both publish CouncilState on the same
//             scope, so running them together is a SCOPE CONTENTION (last writer wins, alternating per bar).
//    v1.10.0 (in-place, 2026-07-24) — DECOUPLED vs ABSENT (Core >= v1.40.0 beacon). A missing BAR-TYPE voter
//             (BRK/FLUX/CVB) now asks SentinelCore.BeaconForeign whether another ASSEMBLY GENERATION is still
//             publishing it. If it is, the sensor is not absent — it is DECOUPLED, and the fix is an NT RESTART.
//             WHY: an F5 rebuilds every indicator but NOT a chart's bars-type instance, so the surviving bars
//             type publishes into the OLD assembly's static seam store while this rebuilt Council reads the NEW
//             one. The write succeeds into a store nothing reads: guards pass, scope resolves, nothing throws,
//             and BRK/FLUX/CVB simply never appear. That silently produced the 2026-07-23 audition corpus —
//             1,866 rows, ZERO bar-type voters, brkUpper/brkLower all 0 — and survived a chart reload, which
//             is why "reload the chart" was twice believed to be the fix and twice wrong (measured 07-24).
//             Reported via Conditions (debounce/re-state/auto-clear) and evaluated OUTSIDE the roster-change
//             gate, because a latch that never re-arms is indistinguishable from a detector that never fires.
//    v1.9.0 (in-place, 2026-07-23) — LIVE CONFIG RELOAD + Lane.conf CASCADE (Core >= v1.39.0). Roster.conf and
//             Lane.conf are now POLLED on write time (>=2s, same idiom SentinelSkin uses for cards.off/theme.txt)
//             and re-applied on the next bar, so changing a floor / veto / voter weight needs NO F5 and NO chart
//             reload. Both are re-applied together (a lane profile writes over F6 properties, so a roster-only
//             change must not leave a half-applied state) and the winning paths are LOGGED. Core's LaneIO also
//             gained the scope-instrument-global cascade RosterIO always had: a chart on a bar type nobody had
//             used before used to find no Lane.conf, silently keep its F6 floor and record almost nothing --
//             indistinguishable from every sensor being dead. That asymmetry is what made 'new chart, pick a bar
//             type, load the template, run' impossible. One Models\<INST>\Lane.conf now covers every bar type.
//    v1.8.4 (in-place, 2026-07-23) — VetoOnWall: an explicit ON/OFF for the liquidity-wall veto + the
//             `vetoonwall` Lane.conf key. v1.8.3 tried to disable that veto from the lane with
//             `wallnearticks = -1`; NT REJECTED IT AT LOAD -- WallNearTicks is [Range(0, double.MaxValue)],
//             so the chart threw "Value of property 'WallNearTicks' ... is -1 and not in valid range".
//             And 0 is not off either: BlocksEntry tests `dist <= ticks`, so 0 vetoes whenever a wall sits
//             exactly AT price. A distance knob simply HAS no disable value -- the other two hard vetoes
//             (VetoOnChop / VetoKillWindow) each carry a bool and this one did not. Now it matches them.
//             LESSON: check a property's Range/validation attributes before choosing a sentinel value; a
//             value that is meaningful in the comparison can still be illegal at the property boundary.
//    v1.8.3 (in-place, 2026-07-23) — HARD VETOES ARE LANE-SETTABLE. ApplyLaneProfile now also reads
//             `vetoonchop` / `vetokillwindow` / `wallnearticks` / `minvoters` from Lane.conf (same sparse
//             "absent ⇒ inherit F6" contract as every other key; MinVoters via TryDouble→(int) so SentinelCore
//             needs no new API). WHY: a modulator DAMP scales a recorded verdict, but a HARD VETO deletes it —
//             SentinelExcursionRecorder's gate is `aligned = Bias!=0 && (RecordBelowFloor ? !v.Vetoed : HasEdge)`,
//             so a vetoed bar never enters the corpus. With VetoOnChop on, the 22-voter sensor AUDITION was
//             silently dropping every chop-regime bar — it would have graded the arsenal on trend-only data and
//             reported nothing wrong. Lane.conf being sparse by design covered the damps but not the vetoes.
//             Found by reading a live roster line on legacy-node (a load burst, every verdict "VETO:chop 66") rather
//             than by reasoning about the config. Also bumps CouncilVer 1.8.1→1.8.3: the const had never been
//             bumped for v1.8.2, so recorded rows were stamped cnclVer "1.8.1" and the corpus provenance lied.
//    v1.8.2 (in-place, 2026-07-20) — new CVB voter (STATE, orthogonal/order-flow). Consumes SentinelCore.ConvictionState
//             (v1.37.0) — SentinelDrift's (bar type id 212204) FLOW-CONFIRMED trend direction: the brick direction voted
//             only when the aggregated tape confirms it. Bar-type seam → BARE scope. KnownVoters + AddVote + BaseWeight +
//             WeightConviction ([Display]-only, region-safe) + SetDefaults + Reasons (`cvb▲/▼`). No fusion/damp change.
//    v1.8.1 (in-place, 2026-07-18) — FUSION-CORE REWIRE. The inline fuse block (kind-aware denomW · deadband→bias ·
//             conviction · the full context-damp chain: breadth·squeeze·clock·participation·MTF·location·profile·
//             regime·flux-absorb) now DELEGATES to AddOns/CouncilFusion.Fuse — the single pure fusion truth shared
//             with the (coming) replay HARNESS, so a replay-baked verdict == the live verdict on that bar (the
//             correctness gate, Docs/SENTINEL_REPLAY_SPEC.md §4-5). BEHAVIOUR-PRESERVING: identical math, verified
//             bit-for-bit (AddVote accumulation ≡ Fuse's recompute; _declaredW ≡ Fuse's denom; damp chain identical;
//             squeeze damp = 0.6 as before). Modulator seams now read UNCONDITIONALLY (Fuse owns the bias-gating);
//             the bias-dependent liquidity-WALL veto is resolved AFTER Fuse (zeroing size+conviction, bit-identical
//             to Vetoed=true). CouncilFusion.cs brought from v1.3.x → v1.8.0 parity (added Profile/Regime/Flux-absorb
//             + their damps). VERIFY UNCHANGED via a Market-Replay verdict-diff before trusting a replay bake.
//    v1.8.0 (in-place, 2026-07-14) — PER-LANE SYSTEM PROFILE (Core ≥ v1.33.0). On load, applies the lane's
//             Sentinel\Models\<inst>\<bartag>@<lane>\Lane.conf OVER the F6 fusion knobs (ConvictionFloor, bias
//             deadband, the 6 context-consult toggles, the modulator damps) — SPARSE: only keys present override,
//             absent keys inherit F6. So an A/B test LANE pins its own decision knobs (not just its roster) without
//             hand-editing F6 per chart. Roster.conf = voters+weights+kind; Lane.conf = the rest. (System Builder §14.7.)
//    v1.7.0 (in-place, 2026-07-14) — PER-CHART LANE (Core ≥ v1.32.0). New "Scope Lane" property: set a distinct
//             value (A/B) on each of two charts that share instrument+bartype+size so their scopes diverge
//             (GC.212202v6x24@A / @B) instead of clobbering — fixes SCOPE CONTENTION for same-bartype test charts.
//             Registers the lane (keyed by ChartControl) in DataLoaded so every SENSOR on the chart inherits it;
//             publishes CouncilState + reads sensor seams on the LANED scope, but reads BAR-TYPE seams (BRK/FLUX)
//             on the BARE scope (a bars series is shared across charts). Blank lane = bare scope (back-compat).
//             ⚠ Needs the sensor batch (each sensor → ScopeOf(…,ChartControl)) for a laned chart to see its voters.
//    v1.6.3 (in-place, 2026-07-14) — FLUX voter + absorption modulator (Core ≥ v1.31.0). FLUX = SentinelFlux's net
//             ORDER-FLOW direction from the imbalance-driven bar close (a STATE voter, w0.7) — the suite's one
//             order-flow-SUBSTRATE axis: the whole chart clock is flow-synchronized, so it is orthogonal to the
//             price bloc (TRND/CCI/ADX/ENV all echo the OHLC). Its flow-vs-price DIVERGENCE drives a soft SIZE damp
//             (FluxAbsorbDamp, default 0.6) when the tape absorbs against the bias — the tape-sourced complement to
//             the LiquidityWalls book veto. KnownVoters + BaseWeight + AddVote + WeightFlux/FluxAbsorbDamp props +
//             audit (flux▲/▼/absorb). DefaultKind default already State. Council now fuses 22 voters. Additive only.
//    v1.6.2 (in-place, 2026-07-12) — TWO NEW VOTERS (candidate-library novel-signals pass; Core ≥ v1.30.0). VDYA
//             (SentinelVIDYA Chande-CMO adaptive-MA trend — STATE, w0.5) · HARM (SentinelHarmonic XABCD pattern
//             completion — TRIGGER, w0.4). KnownVoters + BaseWeight + DefaultKind(HARM=Trigger) + AddVote +
//             weight properties. Council now fuses 21 voters. Additive only.
//    v1.6.1 (in-place, 2026-07-12) — ARCH voter (SentinelTrendArchitect, the MPL Pine port; Core ≥ v1.29.0). Its
//             composite PRISM trend + Trend-Regime-Gate publishes TrendArchitectState → a STATE voter (w0.7).
//             KnownVoters + BaseWeight + AddVote + weight property. Council now fuses 19 voters. Additive only.
//    v1.6.0 (in-place, 2026-07-12) — FOUR NEW VOTERS (candidate-library Tier-2 pass; Core ≥ v1.28.0). AVMA
//             (SentinelADXVMA adaptive-MA trend — STATE, w0.6) · SPRT (SentinelSuperTrend ATR trailing flip —
//             STATE, w0.7) · PSAR (SentinelParabolicSAR Wilder trend/stop — STATE, w0.5) · ZSC (SentinelZScore
//             mean-reversion — TRIGGER, w0.4). New KnownVoters + BaseWeight + DefaultKind(ZSC=Trigger) + AddVote +
//             weight properties ([Display]-only). Council now fuses 18 voters + 8 modulators/vetoes. Additive only.
//    v1.5.1 (in-place, 2026-07-12) — ROSTER I/O EXTRACTED to SentinelCore.RosterIO (Core ≥ v1.27.0; no
//             behaviour change). LoadRoster now delegates the file cascade + parse to RosterIO.Read (same
//             scope▸instrument▸global cascade, same grammar, same first-wins dedup); the private ParseRoster
//             was removed. The DEFAULT declaration (KnownVoters where BaseWeight>0) and all fusion math are
//             UNCHANGED. Purpose: ONE format owner shared with the System Builder (writer) so reader/writer
//             can never drift — the substrate for the roster-editor UI (Docs/SENTINEL_SYSTEM_BUILDER_SPEC.md).
//    v1.5.0 (in-place, 2026-07-12) — THREE NEW VOTERS + TWO NEW MODULATORS (the installed-tree port harvest;
//             Core ≥ v1.26.0). VOTERS: FLOW (SentinelFlow tick-rule CVD regime — STATE, the one non-price-echo
//             axis, w 0.9) · STRC (SentinelStructure swing HH/HL·LH/LL — STATE, w 0.7) · EXH (SentinelExhaustion
//             Leledc reversal — TRIGGER/mean-reversion, w 0.5). MODULATORS (size, not agreement): Profile
//             (price accepted inside the value area = chop → InValueDamp) · Regime (high-volatility K-means regime
//             → HighVolRegimeDamp). New KnownVoters + BaseWeight + KindFor(EXH=Trigger) + Reasons audit
//             (in-value · hi-vol · ±flowDiv). Weights + consult toggles are [Display]-only (region-safe). The
//             Council now fuses 14 voters + 8 modulators/vetoes. Backward-compatible (added members only).
//    v1.4.0 (in-place, 2026-07-11) — PUBLISH THE DECISION VECTOR (ML spec §2.1/§2.2; Core ≥ v1.24.0). The publish
//             now carries the machine-readable inputs the fusion SAW — votes (tag→dir) · voteW (tag→effective weight)
//             · signed netScore · activeW · the orthogonal-axis modulator context (clockPhase/rvol/mtfBias/levelInPath
//             /levelName). BEHAVIOUR-NEUTRAL: nothing upstream of SetCouncilState moves — no change to netScore
//             arithmetic, the deadband, conviction, sizeMult, or any veto (diffable proof: the fuse block is untouched).
//             The vector + modulators are cached so the OnMarketData heartbeat republishes the SAME vector. This is the
//             input side the offline Lab fits weights on (the Recorder emits them into the schema-1.3 corpus).
//    v1.3.1 (in-place, 2026-07-10) — STF VOTER + CHOP VETO. Consumes SentinelCore.StfState (Core ≥ v1.22.0) from the
//             new SentinelStochasticTripleFilter sensor: its Gaussian-Channel midline SLOPE joins as trend voter "STF" (KnownVoters,
//             BaseWeight, DefaultKind STATE), and its Choppiness flag drives a new CHOP VETO (VetoOnChop, default ON —
//             "ranging tape → stand down", named in VetoReason + the Reasons audit). ⚠ WeightStf DEFAULTS 0 (the
//             exploration primitive): STF is RECORDED on every fire but adds nothing to the fusion until promoted (F6
//             "Weight — STF" or Roster.conf) — so this F5 does NOT perturb the fitted conviction math anywhere; only the
//             chop veto is immediately live (and only when the STF sensor is loaded; fail-open otherwise). WeightStf +
//             VetoOnChop are [Display]-only (region-safe, mirrors WeightGodRev).
//    v1.3.0 (in-place, 2026-07-10) — VOTER KIND. Builds the fix v1.2.1 only described. The conviction denominator
//             is now KIND-AWARE (state vs trigger), resolving the conflation the prior note flagged.
//               • STATE voter  → always in the denominator (present-directional, present-neutral, OR absent). A flat
//                                regime is a real opinion; its neutral dilutes, its absence dilutes.
//               • TRIGGER voter → in the denominator ONLY when it FIRED (Dir≠0) or is ABSENT (crashed/stale ⇒ unknown
//                                ⇒ dilute — the roster's whole purpose). A present-but-QUIET trigger ("watching,
//                                nothing to report") is an ABSENCE OF EVIDENCE, not evidence against, so it leaves the
//                                denominator untouched instead of dragging conviction toward 0.16.
//             Numerator (netScore) is UNCHANGED — only counted directional votes contribute. On the modal bar (all
//             triggers quiet, state voters present) the denominator drops 7.8 → ~4.1, so conviction ~doubles.
//             ⚠ CLASSIFICATION was VERIFIED against the published seams, not assumed. The v1.2.1 note called BRK a
//             trigger — WRONG: BrickState.Direction is documented "-1 Down / 1 Up" and is NEVER 0 (a brick always has
//             a direction), so BRK is STATE and can never go quiet. The real triggers are CMP·WAE·GREV (3.7/7.80,
//             still ~47%). STATE = TRND·CCI·ADX·ENV·IMKT·BRK.
//             CONFIG: a per-voter kind override in Roster.conf, mirroring the `w=` override —
//                 CMP   w=0.7 trigger        # bare word, or `kind=trigger`
//                 TRND  state
//             Code default = DefaultKind(tag), so it works with no conf. The effective per-bar denominator is written
//             into the Reasons audit ("denom 4.1/7.8"), which the Bridge records to the Ledger on fire — so the Lab
//             can build the NEW conviction histogram and FIT the floor against it.
//             ⚠ FLOOR LEFT AT 0.20 (operator decision, eyes-open): conviction ~doubling will ~double the trade rate,
//             but the downside is BOUNDED — the Bridge still fires 1-lot (SizeMult inert at BaseContracts=1), so this
//             is "more 1-lots", not bigger risk. Measure the new distribution live, THEN fit the floor. Do NOT nudge.
//
//    v1.2.1 (in-place, 2026-07-10) — INTERIM: ConvictionFloor 0.35 → 0.20. Operator decision, taken eyes-open.
//             MEASURED on 97 live verdicts after v1.2.0: mean conviction 0.160, max 0.36, and exactly ONE cleared
//             the 0.35 floor. Floor sensitivity — 0.30→5%, 0.25→11%, 0.20→41%, 0.15→60%. It sits on a CLIFF; ±0.05
//             swings the trade rate ~4×. Do not nudge it by feel.
//
//             ⚠ THE FLOOR IS NOT THE REAL PROBLEM. `declaredW` conflates two kinds of voter:
//               • STATE voters (TRND · ADX · ENV · IMKT) always carry a direction.
//               • TRIGGER voters (BRK · CMP · WAE · GREV) are ±1 only on the rare bar they fire, and read "~"
//                 the rest of the time.
//             About HALF the model's weight is therefore parked at zero on a typical bar, permanently dragging
//             conviction toward 0.16. A silent trigger is being scored as "I looked and saw no direction" — the same
//             as ENV~ genuinely reading a flat regime. Those are DIFFERENT statements: a trigger that has not fired
//             is an ABSENCE OF EVIDENCE, not evidence against. This is the same conflation bug as v1.2.0's
//             (one number doing two jobs), one level down.
//             THE REAL FIX (not built — needs a decision + fresh measurement): a voter KIND in Roster.conf —
//                 CMP  w=0.7  trigger      # weight joins declaredW only on the bar it fires
//             Absent voters still dilute (unknown). Neutral STATE voters still dilute (they looked, saw nothing).
//             Quiet triggers stop penalising. declaredW then means "the weight of the model that had something to say."
//
//             ⚠ AND THE SIZE SCALING IS CURRENTLY INERT: every verdict clearing 0.20 sizes to ~0.18-0.21, but
//             SentinelBridge computes Math.Max(1, BaseQty × SizeMult) — so 0.19 and 0.99 both fire a ONE-LOT. The
//             context damping v1.2.0 introduced has NO effect on the position until the Bridge routes through
//             SentinelCore.SizedQuantity(). Floor 0.20 therefore means "full 1-lot on 41% of verdicts", NOT "a small
//             position". Fix the Bridge before trusting this floor with real size.
//    v1.2.0 (in-place, 2026-07-10) — CONVICTION AND CONTEXT ARE NOW SEPARATE NUMBERS. Needs SentinelCore ≥ v1.19.1.
//             v1.1.0 made absence dilute conviction — correct, and it MUTED THE COUNCIL. Measured on the first F5:
//             every verdict came out size=0.00, max conviction 0.20 against a 0.35 floor. Four voters aligned with
//             ZERO dissent and nine awake still could not trade:
//                 netScore 2.9 / declaredW 7.80 = 0.372  × MiddayDamp 0.85 = 0.316  × MtfCounterDamp 0.60 = 0.19
//             MiddayDamp ALONE sank a perfect reading below the floor.
//             ROOT CAUSE — not the floor's value, but that CONVICTION WAS DOING TWO JOBS: it measured how much of
//             the model agreed, and then every context modulator (Clock · MTF · Participation · Location · squeeze ·
//             breadth) multiplied INTO it before the floor test. The floor was therefore asking "do my sensors agree
//             AND is the context good?" Once absence began diluting the base, both penalties compounded on the one
//             number and nothing survived. Lowering the floor would only have hidden this behind another hand-picked
//             constant.
//             NOW: `conviction` = AGREEMENT, pure and undamped (|netScore| / declaredW). The floor gates on
//             agreement alone. A new `contextMult` accumulates every context modulator and scales the POSITION:
//                 sizeMult = (vetoed || bias == 0 || conviction < floor) ? 0 : conviction × contextMult
//             A midday, counter-higher-timeframe trade with genuine agreement now takes a SMALLER SIZE instead of
//             silently becoming a non-trade. Each number means exactly one thing.
//             ⚠ `CouncilState.HasEdge` had to move with it (Core v1.19.1): it gated on `Conviction > 0`, and with a
//             pure conviction a below-floor verdict keeps Conviction > 0 ⇒ HasEdge TRUE with SizeMult 0 — and
//             SentinelBridge computes Math.Max(1, BaseQty × SizeMult), so it would have fired a ONE-LOT on a
//             stand-down. HasEdge now gates on SizeMult, the only number that can say no.
//             The verdict log gains `conv=x/floor ctx=y` so "below the floor" and "agreed but hostile context" are
//             finally distinguishable — both used to read size=0.00.
//    v1.1.0 (in-place, 2026-07-10) — ABSENCE MUST DILUTE. ⚠ BEHAVIOUR CHANGE — a minor bump, not a patch.
//             conviction was |netScore| / activeW, where activeW summed only the PRESENT, DIRECTIONAL voters. So a
//             missing voter did not dilute conviction — it VANISHED FROM THE DENOMINATOR. One awake sensor of
//             weight 0.6 gave 0.6/0.6 = 1.0: perfect unanimity. The fewer sensors awake, the MORE certain the
//             Council sounded. Caught the moment the declared roster made it visible, on a live GC chart:
//                 size=0.57 (1/0, 2v) | ENV▼ GREV~ · roster 2/10
//             — a TRADEABLE verdict (above the 0.35 floor) fused from ONE of ten declared voters.
//             Now conviction = |netScore| / Σ(BASE weight of the DECLARED voters), clamped to 1.0 (the per-sensor
//             strength multipliers, CCI ×1.5 / ADX ×1.25, can push |netScore| past the base sum). A present-but-
//             NEUTRAL voter now dilutes too — it saw the market and declined to take a side, which is information.
//             THE DEADBAND MOVES WITH IT, and that is load-bearing rather than cosmetic: had bias still been chosen
//             off activeW, a 1-of-10 roster would set Bias≠0 with Conviction≈0.07 ⇒ HasEdge TRUE and SizeMult 0 —
//             and SentinelBridge computes Math.Max(1, BaseQty × SizeMult), so it would STILL have fired a one-lot.
//             Diluting conviction alone would have left that hole open. Such a verdict is now simply FLAT.
//             ⚠ EXPECT MATERIALLY FEWER TRADES. The 0.35 floor now means roughly "a third of the model's total
//             weight agrees", not "the handful of awake sensors agree". The floor / deadband / MinVoters are all
//             unfitted hand-picked numbers and want refitting TOGETHER (ML spec) now that the scale has changed.
//             Breadth damping (MinVoters) is now partially redundant — absence already dilutes — but is kept: it
//             errs toward standing down, and the Lab will refit it alongside the rest.
//    v1.0.3 (in-place, 2026-07-09) — LOG BY SCOPE, not by instrument. Every sentinel.log line the Council wrote was
//             prefixed with the bare instrument, so two GC charts both logged "GC …" — and the first live roster
//             immediately proved why that's untenable: `GC roster COMPLETE 10/10` and `GC roster 3/10 ⚠CCI,ADX,…`
//             were DIFFERENT CHARTS, indistinguishable in the log. New LogTag() → the scope ("GC.69697v6x24"),
//             falling back to the instrument only before the scope resolves. Applied to the verdict-change line,
//             the roster-deviation line, and the declaration line.
//    v1.0.2 (in-place, 2026-07-09) — DECLARED ROSTER (exec plan 3.1 · ML spec §10.4; needs SentinelCore ≥ v1.16.0).
//             The roster was EMERGENT: the Council fused whatever seams happened to be fresh. So when a declared voter
//             threw on load it simply never voted, and across 332 verdicts NOTHING SAID SO — the heaviest voter
//             (1.4) was dead code. Under fail-open abstention a crashed sensor is indistinguishable from a quiet
//             one. Now the expected voter set is DECLARED and resolved against reality every update:
//               • declared + fresh      → votes
//               • declared + absent     → RosterInfo.Missing, Complete=false, logged on change, named in the audit
//               • present + undeclared  → RosterInfo.Unexpected, flagged and NOT folded into the fusion
//             The declaration comes from `Sentinel\Models\<INST>\<bartag>\Roster.conf` (▸ …\<INST>\ ▸ …\Models\),
//             else DEFAULTS to every known voter whose configured weight > 0. Deriving it by OBSERVING the live
//             seams would bake the outage into the "expected" set and the roster could never report it — the
//             declaration must be config-derived, never observation-derived.
//             `w=0` is the EXPLORATION PRIMITIVE: the voter votes and is recorded, but adds nothing to netScore,
//             activeW, the agree/disagree tally, or the breadth damping. A candidate sensor accrues its full
//             history before it can influence one trade. Adding/retiring a sensor becomes a config change.
//             Fusion math is UNCHANGED when every voter is declared with weight > 0 (today's default).
//    v1.0.0 (2026-07-07) — initial: fuse Trend/ADX/CCI/Envelope/Liquidity/Brick → CouncilState verdict;
//             weighted vote + breadth/squeeze damping; account-free hard vetoes; hidden Bias/Conviction
//             plots; Sentinel card/palette/label-remover; change-logged. Designed to pick up the orthogonal
//             axes (Clock/Location/Participation/MTF/Event) as they publish their own seams.
//             + (same day) CLOCK MODULATION — consults SentinelCore.ClockState (the first orthogonal axis):
//               conviction × MiddayDamp midday / × OffSessionDamp out-of-session, and the near-close KILL
//               WINDOW is a hard veto (VetoKillWindow). Fail-open if the Clock isn't loaded.
//             + (same day) PARTICIPATION MODULATION — consults SentinelCore.ParticipationState (2nd axis):
//               conviction × clamp(rvol, RvolDampFloor, 1) so light volume penalises an unbacked move and
//               heavy volume never inflates. Rvol shown in the Reasons audit. Fail-open if not loaded.
//             + (same day) MTF + LOCATION MODULATION (3rd + 4th axes) — SentinelCore.MtfState (conviction ×
//               MtfCounterDamp when the verdict opposes the higher-TF consensus) + SentinelCore.LevelState
//               (conviction × LevelDamp when a structural level within LevelNearAtr sits in the trade's path).
//             + (same day) COMPRESSION VOTER (CMP) — consults SentinelCore.CompressionState.BreakDir (held
//               breakout direction) as a directional voter (WeightComp).
//             + (same day) INTERMARKET VOTER (IMKT) — consults SentinelCore.IntermarketState.Lean (configurable
//               correlated-instrument macro lean, e.g. ZN for gold) as a directional voter (WeightIntermarket).
//               Council now fuses 8 voters. Also: veto now renders on its OWN card line (no tally overlap).
//             + (2026-07-07) WAE VOTER (WAE) — consults SentinelCore.WaeState.Signal (Sentinel WAE's CONFIRMED
//               momentum-explosion breakout direction, ±1 only when Power > Explosion > DeadZone) as a
//               directional voter (WeightWae). Council now fuses 9 voters.
//    v1.0.1 (in-place, additive 2026-07-08) — GOD REVERSAL VOTER (GREV) — consults
//               SentinelCore.GodReversalState.Dir (SentinelGodReversal's held candle-grammar reversal
//               direction) as a directional voter (WeightGodRev, default 0.9). Council now fuses 10 voters.
//               ⚠ GREV is a MEAN-REVERSION voice (often COUNTER to the trend voters) — expect it to be
//               out-voted/damped in strong trends; that's arguably correct. Best consumed as an entry TRIGGER
//               alongside the Council bias (the Bridge's job), not as a lone swing vote. Needs SentinelCore
//               ≥ v1.14.0. WeightGodRev is a [Display]-only prop (no generated-region regen). Needs SentinelCore ≥ v1.14.0.
//             + (in-place 2026-07-08) HEARTBEAT republish — OnMarketData re-stamps the SAME cached verdict on any
//               incoming quote (throttled to HeartbeatSec=5s). Calculate.OnPriceChange only refreshes when PRICE
//               moves, so in thin/dry-up markets the published CouncilState went stale (consumers read "no verdict"
//               while the card still showed one). No recompute, no extra logging — pure freshness. Fixes the
//               "there is a verdict but the Bridge says none" dry-up flicker.
// ═════════════════════════════════════════════════════════════════════════════
namespace NinjaTrader.NinjaScript.Indicators.Sentinel
{
    public class Council_v1_11_0 : Indicator
    {
        // one lightweight vote record for rendering + tally
        private struct Vote
        {
            public string Tag;     // short chip label (TRND / CCI / ADX / ENV / BRK)
            public int    Dir;     // -1 / 0 / +1 (0 = present but neutral / abstains from direction)
            public double W;       // effective weight this update
            public bool   Fresh;   // a non-stale reading existed
            public bool   Counted; // folded into netScore/activeW (false = w=0 explorer, or undeclared)
        }

        // ── DECLARED ROSTER (SentinelCore v1.16.0 · ML spec §10.4) ────────────────────────────────
        // The roster used to be EMERGENT — the Council fused whatever seams happened to be fresh. So when the
        // crashed on load it simply never voted, and across 332 verdicts nothing anywhere said so. Declaring
        // the expected voter set converts a silent absence into a reported one, and separates THE MODEL from
        // WHAT HAPPENED TO BE LOADED ON THE CHART.
        private static readonly string[] KnownVoters = { "TRND", "CCI", "ADX", "ENV", "BRK", "CMP", "IMKT", "WAE", "GREV", "STF", "FLOW", "STRC", "EXH", "AVMA", "SPRT", "PSAR", "ZSC", "ARCH", "VDYA", "HARM", "FLUX", "CVB", "CVD", "BSP" };
        private List<string> _declared;                  // expected voters, in declaration order
        private Dictionary<string, double> _rosterW;     // per-tag weight override from Roster.conf (null ⇒ use the property)
        private Dictionary<string, VoterKind> _rosterKind; // per-tag kind override from Roster.conf (null ⇒ DefaultKind) — v1.3.0
        private string _rosterSource;                    // the Roster.conf path, or "default (configured weights > 0)"
        private double _declaredW;                       // Σ BASE weight of the declared voters — the STATIC denominator / fallback
        private double _effDenomW;                       // v1.3.0 — the KIND-AWARE effective denominator this update (for the audit)
        private readonly HashSet<string> _spoke = new HashSet<string>(StringComparer.Ordinal);  // declared tags that voted this update
        private readonly List<string> _unexpected = new List<string>();                          // present but undeclared
        private SentinelCore.RosterInfo _roster;         // cached this update; the heartbeat republishes it
        private string _lastLoggedMissing;    // change-detection so an incomplete roster logs once, not per bar

        private SentinelSkin.Painter _sp;
        private bool   _hasData;
        // published verdict (cached in OnBarUpdate; drawn in OnRender)
        private int    _bias;
        private double _conviction, _sizeMult;
        private int    _agree, _disagree, _voters;
        private bool   _vetoed;
        private string _vetoReason;
        private int    _clockPhase = -1;   // ClockState phase this update (-1 = none/unknown); modulator, not a voter
        private double _pRvol = double.NaN; // ParticipationState rvol this update (NaN = none); modulator, not a voter
        private int    _mtfBias;            // MtfState consensus this update (0 = none/agree); modulator
        private bool   _lvlInPath;          // a structural level lies in the bias's path; modulator
        private string _lvlName;            // that level's name (for the audit)
        private bool   _stfChoppy;          // StfState reported CHOPPY this update (drove the chop veto); for the audit
        private double _stfChop;            // the Choppiness Index value (for the audit)
        private int    _flowDiverge;        // FlowState price-vs-CVD divergence this update (for the audit)
        private int    _fluxDiverge;        // FluxState flow-vs-price divergence (absorption) this update; for the audit
        private int    _fluxFlowDir;        // FluxState net order-flow direction this update (for the absorption damp)
        private bool   _fluxAbsorb;         // flux tape absorbing AGAINST the bias this update (drove the size damp)
        private int    _cvbBias;            // ConvictionState flow-confirmed bias this update (CVB voter, for the audit)
        private int    _cvdDir;             // CvdState session flow direction this update (CVD voter, for the audit)
        private int    _cvdDiverge;         // CvdState flow-vs-price divergence (+1 bull / -1 bear) — absorption tell
        private int    _bspDir;             // PressureState dominant side this update (BSP voter, for the audit)
        private bool   _bspTick;            // BSP read came from REAL bid/ask classification, not the OHLC proxy
        private int    _bspDiv;             // BSP price-vs-pressure divergence (+1 bull / -1 bear)
        private double _cvdEffZ;            // CvdState efficiency z — <0 absorbing (heavy flow, little price), >0 thin
        private bool   _profInValue;        // ProfileState reported price accepted inside the value area; modulator
        private int    _regimeLbl = -1;     // RegimeState label this update (-1 none/0 low/1 med/2 high); modulator
        // v1.4.0 — cached DECISION VECTOR (ML spec §2.1), built in OnBarUpdate's publish, republished by the heartbeat
        private Dictionary<string,int>    _lastVotes;
        private Dictionary<string,double> _lastVoteW;
        private double _lastNetScore, _lastActiveW;
        // v1.4.0 — EPISODE key (ML spec §10.2): stable across a constant-Bias run, bumped only on a bias flip
        private int    _episodeBias = int.MinValue;   // MinValue = no episode yet → the first bar opens episode 1
        private long   _episodeSeq;
        private string _episodeId;
        private static readonly string[] ClockPhaseName = { "Closed", "Open Drive", "Midday", "Close" };
        private static bool _catalogExported;   // one shared-catalog emit per session (first Council to load)
        private readonly List<Vote>   _votes    = new List<Vote>(8);
        private readonly List<double> _convHist = new List<double>();
        // ⚠ BUMP THIS WITH THE HEADER, EVERY TIME. It is stamped as cnclVer on every recorded verdict, so it is
        // the ONLY thing that lets the corpus separate one Council's behaviour from another's.
        // 🔴 It read "1.9.0" while the header read v1.11.0 (found 2026-08-08 by Lab\docs\version_check.py). The
        // corpus therefore holds 8,547 rows stamped 1.9.0 that are actually a MIXTURE of v1.9.0, v1.10.0 and
        // v1.11.0 — and v1.11.0 was a real behavioural fork (off-chart STF no longer votes or vetoes). Those rows
        // cannot be separated by this column; they can only be split by fire TIME against the version dates.
        // This has drifted before: the v1.36.0 changelog records bumping it 1.8.1→1.8.3 after the same lapse.
        private const string CouncilVer = "1.11.0";         // v1.36.0 — the authoritative version, stamped as cnclVer on recorded verdicts (A1 provenance)
        private const int HistMax = 48;
        private int _lastHistBar = -1;
        // change detection for the log
        private int    _lastLoggedBias = -999;
        private bool   _lastLoggedVeto;
        private DateTime _lastHeartbeatUtc;                 // v1.0.0 in-place: throttle the OnMarketData republish
        private const double HeartbeatSec = 5.0;            // re-stamp the cached verdict at most this often on quotes
        // ── scope keying (SentinelCore v1.15.0 · execution plan 1.2) ──
        private string   _scope;                            // "<masterInstrument>.<barTag>" — resolved lazily, then cached
        private string   _laneTag = "";                     // v1.8.0 — this chart's lane, cached for the card (render-thread safe)
        private string   _barTag;
        private DateTime _lastBarTimeUtc;                   // bar time of the CACHED verdict, for the heartbeat republish
        // Per-INSTANCE publisher id. Scope cannot separate two charts sharing instrument AND bartype; this lets
        // SentinelCore detect that contention (two live publishers, one key) and say so instead of silently
        // letting them overwrite each other. Also lets us release only OUR OWN scope entry on teardown.
        private readonly string _pubId = "Council#" + Guid.NewGuid().ToString("N").Substring(0, 4);

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description              = "The Sentinel confluence arbiter — fuses every published sensor seam (Trend/ADX/CCI/Envelope/Liquidity/Brick) into ONE explainable directional verdict (bias + conviction + size), applies hard vetoes, and publishes SentinelCore.CouncilState for any strategy to consult.";
                Name                     = "Sentinel Council v1.11.0";
                Calculate                = Calculate.OnPriceChange;
                IsOverlay                = true;
                IsSuspendedWhileInactive = true;
                DisplayInDataBox         = false;
                DrawOnPricePanel         = true;

                // ── vote weights (the edge lives here) ──
                // ⛔ THE EYE VOTER WAS DELETED 2026-08-11 (operator's call), not benched.
                //    It was settled dead 2026-07-23 and still fused at 1.4 — the highest weight of any
                //    voter — for eleven days after, so the ledger read "not a voter" while the live
                //    Council leaned on it hardest. Benching at 0 (2026-08-03) fixed the arithmetic but
                //    left the name in an order path, which is how the copier's Eye-gate survived to
                //    silently mirror EXITS ONLY for 19 days.
                //    ⭐ THE OLD RATIONALE FOR BENCHING WAS REAL AND IS ANSWERED, NOT IGNORED: deleting a
                //    voter moves netScore/activeW AND the roster denominator at once, which would break
                //    comparison against the 8,875 historical rows that carry EYE. The objection was to a
                //    SILENT break — so the excursion schema is bumped in the same change. The
                //    discontinuity is now declared by the data itself instead of inferred.
                WeightTrend    = 1.0;   // structural trailing-line trend
                WeightCci      = 0.8;   // Woodies trend (× strong)
                WeightAdx      = 0.6;   // regime/strength confirmer (× strong)
                WeightEnv      = 0.6;   // VolEnvelope trend regime
                WeightBrick    = 0.5;   // adaptive brick micro-trend
                WeightComp     = 0.7;   // CompressionBase breakout (held direction)
                WeightIntermarket = 0.6; // Intermarket correlated-instrument lean (macro)
                WeightWae      = 0.7;   // Sentinel WAE confirmed momentum-explosion breakout
                WeightGodRev   = 0.9;   // Sentinel God Reversal — candle-grammar reversal (mean-reversion voice; see header caveat)
                WeightStf      = 0.0;   // Sentinel Stoch Filter — Gaussian-Channel SLOPE trend. Enters at 0 (exploration
                                        // primitive): recorded on every fire, zero fusion impact until promoted (F6 / Roster.conf).
                WeightFlow     = 0.9;   // Sentinel Flow — tick-rule CVD regime (the one non-price-echo axis; STATE voter)
                WeightStructure= 0.7;   // Sentinel Structure — swing HH/HL·LH/LL market structure (STATE voter)
                WeightExhaustion= 0.5;  // Sentinel Exhaustion — Leledc reversal (TRIGGER, mean-reversion voice; à la GREV)
                WeightAdxvma   = 0.6;   // Sentinel ADXVMA — ADX-vol adaptive-MA trinary trend (STATE, neutral in chop)
                WeightSuperTrend= 0.7;  // Sentinel SuperTrend — ATR-band trailing-flip trend (STATE, always ±1)
                WeightSar      = 0.5;   // Sentinel Parabolic SAR — Wilder trend/stop (STATE, always ±1)
                WeightZScore   = 0.4;   // Sentinel Z-Score — (Close−SMA)/StdDev mean-reversion (TRIGGER, fade voice)
                WeightArch     = 0.7;   // Sentinel Trend Architect — composite PRISM trend + regime gate (STATE voter)
                WeightVidya    = 0.5;   // Sentinel VIDYA — Chande-CMO adaptive-MA trend (STATE voter)
                WeightHarmonic = 0.4;   // Sentinel Harmonic — XABCD pattern completion (TRIGGER, reversal voice)
                WeightFlux     = 0.7;   // Sentinel Flux — net ORDER-FLOW direction of the imbalance-bar close (STATE, orthogonal to price)
                FluxAbsorbDamp = 0.6;   // conviction/size × this when the Flux tape is ABSORBING against the bias (soft veto, à la squeeze)
                WeightPressure   = 0.0; // BuySellVolumePressureMountain — classified buy/sell dominance (STATE, order-flow). AUDITION at 0; abstains unless TickBacked.
                WeightCvd        = 0.0; // SentinelCVD — session cumulative-delta direction (STATE, order-flow). AUDITION at 0: recorded + graded, cannot move the verdict. Fit it, do not nudge it.
                WeightConviction = 0.6; // SentinelDrift CVB — flow-confirmed brick direction (STATE, orthogonal/order-flow)
                VetoOnChop     = true;
                RequireSensorOnChart = true;   // v1.11.0 — a chart-derived sensor must be ON THIS CHART to vote/veto/damp  // when SentinelStochasticTripleFilter reports CHOPPY (Trending=false), veto the verdict (fail-open if absent)

                BiasDeadband   = 0.15;  // net must exceed this fraction of the DECLARED weight to pick a side
                // v1.2.1 INTERIM — 0.35 → 0.20. Measured across 97 live verdicts: at 0.35 exactly ONE cleared (1%).
                // The floor is not the real problem (see the state-vs-trigger note in the changelog); this restores
                // trading until the Lab fits it. ⚠ It sits ON A CLIFF: 0.25→11%, 0.20→41%, 0.15→60%. A ±0.05 move
                // changes the trade rate ~4×, so do NOT nudge this by feel — fit it against outcomes.
                ConvictionFloor= 0.20;  // below this, SizeMult = 0 (no actionable edge)
                MinVoters      = 3;     // fewer fresh voters than this damps conviction
                WallNearTicks  = 8.0;   // an absorption wall within this many ticks of the intended side vetoes
                VetoOnWall     = true;  // v1.8.4 — the wall veto's ON/OFF (WallNearTicks has no "disable" value)
                DampenOnSqueeze= true;  // a VolEnvelope squeeze reduces conviction (coiled ⇒ distrust direction)
                StaleSec       = 90.0;  // ignore any published state older than this

                // ── Clock modulation (session context; a modulator, not a voter) ──
                ConsultClock   = true;  // consult SentinelCore.ClockState (Clock indicator) to modulate by session phase
                MiddayDamp     = 0.85;  // conviction × this during the Midday phase (chop / drift)
                OffSessionDamp = 0.50;  // conviction × this when out of session (lower-quality tape)
                VetoKillWindow = true;  // veto the verdict inside the near-close kill window (no new entries)

                // ── Participation modulation (relative volume; a modulator, not a voter) ──
                ConsultParticipation = true;  // consult SentinelCore.ParticipationState (Participation indicator)
                RvolDampFloor        = 0.50;  // max damp — conviction × clamp(rvol, this, 1): light volume penalises, never inflates

                // ── MTF / Location modulation (higher-timeframe alignment + structural levels) ──
                ConsultMtf      = true;   // consult SentinelCore.MtfState (MTF indicator)
                MtfCounterDamp  = 0.60;   // conviction × this when the verdict opposes the MTF consensus
                ConsultLocation = true;   // consult SentinelCore.LevelState (Location indicator)
                LevelNearAtr    = 0.25;   // a level within this many ATRs on the path counts as "in the way"
                LevelDamp       = 0.70;   // conviction × this when running into a structural level

                // ── Profile / Regime modulation (value-area acceptance + volatility regime; modulators, not voters) ──
                ConsultProfile  = true;   // consult SentinelCore.ProfileState (Profile axis)
                InValueDamp     = 0.75;   // conviction × this when price is ACCEPTED inside the value area (chop context)
                ConsultRegime   = true;   // consult SentinelCore.RegimeState (Regime sensor)
                HighVolRegimeDamp = 0.70; // conviction × this in the high-volatility (chaotic) regime

                PublishState   = true;  // the Council exists to publish
                LogChanges     = true;  // log bias flips + veto toggles to sentinel.log
                ShowCard       = true;
                CardCorner     = SentinelCardCorner.TopRight;   // house default: cards dock right
                ShowIndicatorLabel = false;                     // Sentinel standard: clean chart
                ScopeLane      = "";                             // v1.7.x — per-chart lane: "" = bare scope; set to run two same-bartype charts

                AddPlot(Brushes.Transparent, "Bias");           // ±1 fused direction (generic consumption)
                AddPlot(Brushes.Transparent, "Conviction");     // 0..1 alignment
                IsAutoScale = false;                            // the ±1 / 0..1 plot values must not squash the price panel
            }
            else if (State == State.DataLoaded)
            {
                if (!ShowIndicatorLabel) Name = string.Empty;   // Sentinel label remover (LabelRemover.cs pattern)

                // Emit the shared voter-catalog file (Sentinel\Models\catalog.conf) so the Python Lab builds its
                // fit's feature columns from the SAME source of truth as the Council — kills the train.py drift.
                // Once per session (write-if-changed inside), best-effort; first Council to load does it.
                if (!_catalogExported) { _catalogExported = true; try { SentinelCore.VoterCatalog.Export(); } catch (Exception _sx) { SentinelCore.Swallow("Council.OnStateChange", _sx); } }

                // v1.7.x — PER-CHART LANE. Register this chart's lane (keyed by its ChartControl) BEFORE any
                // sensor on the chart computes its scope, so every seam on this ChartControl inherits the lane
                // (SentinelCore v1.32.0). "" clears it → bare scope (back-compat). Two charts identical on
                // instrument+bartype+size get distinct scopes (GC.212202v6x24@A / @B) → no more SCOPE CONTENTION.
                // v1.9.0 - the lane may be assigned from disk (Sentinel\Lanes.conf) so a bake can be
                // configured headlessly. ResolveLane returns the F6 value unchanged when there is no
                // file and no matching entry, and LOGS when the file overrides F6 - never silent.
                try
                {
                    string _laneInst = (Instrument != null && Instrument.MasterInstrument != null)
                                     ? Instrument.MasterInstrument.Name : "";
                    string _laneBarTag = SentinelCore.BarTag(BarsPeriod);
                    string _effLane = SentinelCore.ResolveLane(_laneInst, _laneBarTag, ScopeLane);
                    SentinelCore.RegisterLane(ChartControl, _effLane);
                    _laneTag = SentinelCore.LaneOf(ChartControl) ?? "";
                }
                catch (Exception _sx) { SentinelCore.Swallow("Council.OnStateChange", _sx); }

                // Declare the roster once, here: Instrument/BarsPeriod are live and the F6 weights are set, so the
                // declaration is fixed BEFORE the first vote is ever counted.
                // v1.7.0 — LANE-AWARE roster: pass the laned tag ("212202v6x24@A") so RosterIO's scope▸instrument▸global
                // cascade reads Models\GC\212202v6x24@A\Roster.conf → two lanes on identical bars run DIFFERENT rosters
                // (the point of a test lane). No lane / no lane-roster file ⇒ falls back to the instrument default (bare).
                try
                {
                    string instName = Instrument != null && Instrument.MasterInstrument != null
                                          ? Instrument.MasterInstrument.Name : null;
                    string rosterTag = BarTagOf();
                    try { string ln = SentinelCore.LaneOf(ChartControl); if (!string.IsNullOrEmpty(ln)) rosterTag += "@" + ln; } catch (Exception _sx) { SentinelCore.Swallow("Council.OnStateChange", _sx); }
                    _cfgInst = instName; _cfgTag = rosterTag;   // v1.9.0 — remembered for the live config poll
                    LoadRoster(instName, rosterTag);
                    ApplyLaneProfile(instName, rosterTag);   // v1.8.0 — Lane.conf fusion-knob overrides (absent ⇒ inherit F6)
                    StampConfigMtimes();
                }
                catch (Exception _sx) { SentinelCore.Swallow("Council.OnStateChange", _sx); }
            }
            else if (State == State.Terminated)
            {
                if (_sp != null) { try { _sp.Dispose(); } catch (Exception _sx) { SentinelCore.Swallow("Council.OnStateChange", _sx); } _sp = null; }
                try { SentinelSkin.CardLayout.Release(this); } catch (Exception _sx) { SentinelCore.Swallow("Council.OnStateChange", _sx); }
                // Release OUR OWN scope entry (source-matched), so a closed chart or an F5 never leaves a stale
                // verdict for a consumer to read, and the contention detector doesn't trip on our own replacement.
                if (_scope != null) { try { SentinelCore.ClearCouncilScope(_scope, _pubId); } catch (Exception _sx) { SentinelCore.Swallow("Council.OnStateChange", _sx); } }
                try { SentinelCore.ClearLane(ChartControl); } catch (Exception _sx) { SentinelCore.Swallow("Council.OnStateChange", _sx); }   // v1.7.x — drop the lane so a stale ChartControl can't leak
            }
        }

        protected override void OnBarUpdate()
        {
            BiasPlot[0]       = 0;
            ConvictionPlot[0] = 0;

            if (Instrument == null || Instrument.MasterInstrument == null) return;
            MaybeReloadConfig();   // v1.9.0 — Roster.conf / Lane.conf are POLLED; edit a conf, no F5, no chart reload
            RefreshOnChartTypes(); // v1.11.0 — refresh which sensors are actually attached (async, UI thread, 5s throttle)
            lock (_onChartGate) _skippedOffChart.Clear();   // v1.11.0 — per-bar audit of what was gated out
            string inst = Instrument.MasterInstrument.Name;

            _votes.Clear();
            _spoke.Clear();
            _unexpected.Clear();
            double netScore = 0, activeW = 0;
            int agree = 0, disagree = 0, voters = 0;
            bool squeeze = false;
            bool stfChoppy = false; double stfChop = 0;   // STF regime (for the chop veto + the audit)
            _bspDir = 0; _bspTick = false; _bspDiv = 0;   // stale pressure must not colour the audit
            _cvdDir = 0; _cvdDiverge = 0; _cvdEffZ = 0;   // stale CVD must not colour the audit
            _fluxDiverge = 0; _fluxFlowDir = 0;           // reset per update — a stale Flux read must not damp size

            // ── gather each sensor's fresh vote (absent/stale ⇒ abstain, fail-open) ──
            // Weights come through WeightFor(tag, …) so a Roster.conf `w=` override applies BEFORE any per-sensor
            // strength multiplier. A declared voter that abstains here is recorded as MISSING by ResolveRoster().
            // v1.18.0 (exec plan 1.4) — consult the sensors by OUR SCOPE, i.e. the ones on THIS chart. A bare
            // instrument could resolve to the other GC chart's reading. Seams not yet migrated still answer a scope
            // through Core's scope→instrument shim, so the batches can land one F5 at a time.
            // v1.7.0 — READ sensors on the BARE scope (sensors are SHARED across lanes: two same-bartype charts run
            // the same sensors, so their seams are legitimately common). The Council PUBLISHES on the LANED scope
            // (Scope(), used at SetCouncilState) so two charts' verdicts don't clobber, and loads a per-LANE roster
            // below — so lanes differ by ROSTER/fusion, not by re-reading identical sensors. (Lane an individual
            // sensor later via ScopeOf(…,ChartControl) only if a test needs per-chart sensor settings.)
            string seamKey = BareScope() ?? inst;
            try
            {
                var tr = Local("TRND", SentinelCore.GetTrendState(seamKey, StaleSec));
                if (tr != null) AddVote("TRND", tr.Direction, WeightFor("TRND", WeightTrend), ref netScore, ref activeW, ref voters);

                var cci = Local("CCI", SentinelCore.GetCciState(seamKey, StaleSec));
                if (cci != null) AddVote("CCI", cci.Bias, WeightFor("CCI", WeightCci) * (cci.Strong ? 1.5 : 1.0), ref netScore, ref activeW, ref voters);

                var adx = Local("ADX", SentinelCore.GetAdxState(seamKey, StaleSec));
                if (adx != null) AddVote("ADX", adx.TrendOn ? adx.Bias : 0, WeightFor("ADX", WeightAdx) * (adx.Strong ? 1.25 : 1.0), ref netScore, ref activeW, ref voters);

                var env = Local("ENV", SentinelCore.GetEnvelopeState(seamKey, StaleSec));
                if (env != null)
                {
                    squeeze = env.IsSqueeze;
                    int ev = env.Regime == 2 ? 1 : (env.Regime == 3 ? -1 : 0);   // TrendUp=+1 / TrendDown=-1 / else flat
                    AddVote("ENV", ev, WeightFor("ENV", WeightEnv), ref netScore, ref activeW, ref voters);
                }

                var br = SentinelCore.GetBrickState(BareScope() ?? inst, StaleSec);   // bar-type seam → BARE scope (shared bars)
                if (br != null) AddVote("BRK", br.Direction, WeightFor("BRK", WeightBrick), ref netScore, ref activeW, ref voters);

                var cmp = Local("CMP", SentinelCore.GetCompressionState(seamKey, StaleSec));
                if (cmp != null) AddVote("CMP", cmp.BreakDir, WeightFor("CMP", WeightComp), ref netScore, ref activeW, ref voters);

                var imk = SentinelCore.GetIntermarketState(inst, StaleSec);
                if (imk != null) AddVote("IMKT", imk.Lean, WeightFor("IMKT", WeightIntermarket), ref netScore, ref activeW, ref voters);

                var wae = Local("WAE", SentinelCore.GetWaeState(seamKey, StaleSec));
                if (wae != null) AddVote("WAE", wae.Signal, WeightFor("WAE", WeightWae), ref netScore, ref activeW, ref voters);

                // GREV — SentinelGodReversal's held candle-grammar reversal direction (mean-reversion voice; see header caveat)
                var grev = Local("GREV", SentinelCore.GetGodReversalState(seamKey, StaleSec));
                if (grev != null) AddVote("GREV", grev.Dir, WeightFor("GREV", WeightGodRev), ref netScore, ref activeW, ref voters);

                // STF — SentinelStochasticTripleFilter's Gaussian-Channel midline SLOPE (a non-CCI/ADX trend regime) as a
                // directional voter; its Choppiness flag drives the CHOP VETO below. A STATE voter (slope always has a
                // reading). WeightStf defaults 0 (exploration): counted only once promoted via F6 / Roster.conf.
                var stf = Local("STF", SentinelCore.GetStfState(seamKey, StaleSec));
                if (stf != null)
                {
                    AddVote("STF", stf.Bias, WeightFor("STF", WeightStf), ref netScore, ref activeW, ref voters);
                    stfChoppy = !stf.Trending; stfChop = stf.Chop;
                }

                // FLOW — SentinelFlow's tick-rule CVD regime (the one axis NOT derived from price). A STATE voter on the
                // CONFIRMED flow direction (Signal = Bias once R²/strength clear their gates, else 0).
                var flow = SentinelCore.GetFlowState(seamKey, StaleSec);
                if (flow != null)
                {
                    AddVote("FLOW", flow.Signal, WeightFor("FLOW", WeightFlow), ref netScore, ref activeW, ref voters);
                    _flowDiverge = flow.Divergence;
                }

                // STRC — SentinelStructure's swing HH/HL·LH/LL market structure. A STATE voter (structure always reads).
                var strc = Local("STRC", SentinelCore.GetStructureState(seamKey, StaleSec));
                if (strc != null) AddVote("STRC", strc.Signal, WeightFor("STRC", WeightStructure), ref netScore, ref activeW, ref voters);

                // EXH — SentinelExhaustion's Leledc reversal (held Dir). A TRIGGER voter + MEAN-REVERSION voice (à la GREV)
                // — often COUNTER to the trend voters; best as an entry trigger, per the seam doctrine.
                var exh = Local("EXH", SentinelCore.GetExhaustionState(seamKey, StaleSec));
                if (exh != null) AddVote("EXH", exh.Dir, WeightFor("EXH", WeightExhaustion), ref netScore, ref activeW, ref voters);

                // AVMA — SentinelADXVMA's ADX-volatility adaptive-MA trinary trend. A STATE voter (neutral in chop).
                var avma = Local("AVMA", SentinelCore.GetAdxvmaState(seamKey, StaleSec));
                if (avma != null) AddVote("AVMA", avma.Signal, WeightFor("AVMA", WeightAdxvma), ref netScore, ref activeW, ref voters);

                // SPRT — SentinelSuperTrend's ATR-band trailing-flip trend. A STATE voter (always ±1).
                var sprt = Local("SPRT", SentinelCore.GetSuperTrendState(seamKey, StaleSec));
                if (sprt != null) AddVote("SPRT", sprt.Signal, WeightFor("SPRT", WeightSuperTrend), ref netScore, ref activeW, ref voters);

                // PSAR — SentinelParabolicSAR's Wilder trend/stop. A STATE voter (always ±1).
                var psar = Local("PSAR", SentinelCore.GetSarState(seamKey, StaleSec));
                if (psar != null) AddVote("PSAR", psar.Signal, WeightFor("PSAR", WeightSar), ref netScore, ref activeW, ref voters);

                // ZSC — SentinelZScore's (Close−SMA)/StdDev mean-reversion. A TRIGGER voter (fade voice, à la EXH/GREV).
                var zsc = Local("ZSC", SentinelCore.GetZScoreState(seamKey, StaleSec));
                if (zsc != null) AddVote("ZSC", zsc.Signal, WeightFor("ZSC", WeightZScore), ref netScore, ref activeW, ref voters);

                // ARCH — SentinelTrendArchitect's composite PRISM trend + Trend-Regime-Gate. A STATE voter.
                var arch = Local("ARCH", SentinelCore.GetTrendArchitectState(seamKey, StaleSec));
                if (arch != null) AddVote("ARCH", arch.Signal, WeightFor("ARCH", WeightArch), ref netScore, ref activeW, ref voters);

                // VDYA — SentinelVIDYA's Chande-CMO adaptive-MA slope trend. A STATE voter.
                var vdya = Local("VDYA", SentinelCore.GetVidyaState(seamKey, StaleSec));
                if (vdya != null) AddVote("VDYA", vdya.Signal, WeightFor("VDYA", WeightVidya), ref netScore, ref activeW, ref voters);

                // HARM — SentinelHarmonic's XABCD pattern completion (held Dir). A TRIGGER voter (reversal voice).
                var harm = Local("HARM", SentinelCore.GetHarmonicState(seamKey, StaleSec));
                if (harm != null) AddVote("HARM", harm.Dir, WeightFor("HARM", WeightHarmonic), ref netScore, ref activeW, ref voters);

                // FLUX — SentinelFlux's net ORDER-FLOW direction from the imbalance-driven bar close. A STATE voter and
                // the suite's one order-flow-SUBSTRATE axis: the whole chart clock is flow-synchronized, so this is
                // genuinely orthogonal to the price bloc (TRND/CCI/ADX/ENV all echo the OHLC). Its flow-vs-price
                // DIVERGENCE (absorption) drives a size damp in the modulator block below — it is not itself a vote.
                var flux = SentinelCore.GetFluxState(BareScope() ?? inst, StaleSec);   // bar-type seam → BARE scope (shared bars)
                if (flux != null)
                {
                    AddVote("FLUX", flux.FlowDir, WeightFor("FLUX", WeightFlux), ref netScore, ref activeW, ref voters);
                    _fluxDiverge = flux.Divergence; _fluxFlowDir = flux.FlowDir;
                }

                // CVB — SentinelDrift's FLOW-CONFIRMED trend direction (bar type id 212204). Votes the brick direction
                // ONLY when the aggregated tape confirms it (per-brick signed delta), else abstains. Orthogonal — its
                // conviction is order-flow-sourced. Bar-type seam → BARE scope (shared bars). STATE voter.
                var conv = SentinelCore.GetConvictionState(BareScope() ?? inst, StaleSec);
                if (conv != null)
                {
                    AddVote("CVB", conv.Bias, WeightFor("CVB", WeightConviction), ref netScore, ref activeW, ref voters);
                    _cvbBias = conv.Bias;
                }

                // CVD — SentinelCVD's SESSION-scale order-flow direction (Core ≥ v1.43.0). Distinct from FLUX on both
                // axes that matter: HORIZON (Flux's θ is one forming bar and resets at every close; this is the whole
                // session's accumulation) and AVAILABILITY (Flux only exists where the Flux BARS TYPE is the chart's
                // clock — CVD is an indicator, so it works on any bar type). Scope-keyed: CVD accrues per bar, so it
                // is bar-type dependent. Its flow-vs-price DIVERGENCE feeds the absorption damp alongside Flux's.
                var cvd = SentinelCore.GetCvdState(Scope() ?? inst, StaleSec);
                if (cvd != null)
                {
                    AddVote("CVD", cvd.Dir, WeightFor("CVD", WeightCvd), ref netScore, ref activeW, ref voters);
                    _cvdDir = cvd.Dir; _cvdDiverge = cvd.Divergence; _cvdEffZ = cvd.EfficiencyZ;
                }

                // BSP (Core v1.45.0) — BuySellVolumePressureMountain's classified buy-vs-sell dominance.
                // ⚠ TickBacked GATES THE VOTE. OnMarketData is realtime-only, so on a historical rebuild the
                // sensor silently falls back to an OHLC candle-shape proxy — which is PRICE-derived, i.e. the
                // family the 2026-07-26 re-test found worthless. Voting the proxy would launder a price signal
                // in as order flow and quietly corrupt the audition it exists to serve. Abstain instead.
                var bsp = SentinelCore.GetPressureState(Scope() ?? inst, StaleSec);
                if (bsp != null)
                {
                    AddVote("BSP", bsp.TickBacked ? bsp.Dir : 0, WeightFor("BSP", WeightPressure), ref netScore, ref activeW, ref voters);
                    _bspDir = bsp.Dir; _bspTick = bsp.TickBacked; _bspDiv = bsp.Divergence;
                }
            }
            catch (Exception _sx) { SentinelCore.Swallow("Council.OnBarUpdate", _sx); }

            _roster = ResolveRoster();

            // ── fuse ──
            // v1.1.0: the denominator is the DECLARED weight, not the weight that happened to show up.
            //
            // It used to be `activeW` — the summed weight of present, directional voters. So a MISSING voter did
            // not dilute conviction, it VANISHED FROM THE DENOMINATOR: one awake sensor of weight 0.6 gave
            // 0.6/0.6 = 1.0, perfect unanimity. Observed live — `size=0.57 (1/0, 2v) | ENV▼ GREV~ · roster 2/10`,
            // a tradeable verdict fused from ONE of ten declared voters. The fewer sensors awake, the MORE certain
            // the Council sounded. Absence must dilute: conviction is now agreement among those EXPECTED, not
            // among those merely awake. A present-but-neutral voter (ENV~) now dilutes too, which is right — it
            // saw the market and declined to take a side.
            //
            // The DEADBAND moves with it, and that is load-bearing, not cosmetic: if bias were still chosen off
            // activeW, a 1-of-10 roster would set Bias≠0 with Conviction≈0.07 ⇒ HasEdge TRUE and SizeMult 0 — and
            // SentinelBridge computes Math.Max(1, BaseQty × SizeMult), so it would still fire a ONE-LOT. Diluting
            // conviction alone would have left the hole open. Now such a verdict is simply FLAT.
            //
            // v1.2.0 — CONVICTION AND CONTEXT ARE SEPARATED. Conviction was doing two jobs: it measured how much of
            // the model AGREED, and then every context modulator (Clock, MTF, Participation, Location, squeeze,
            // breadth) multiplied INTO it before the floor test. So the floor asked "do my sensors agree AND is the
            // context good?" — and once absence began diluting the base (v1.1.0), the two penalties compounded and
            // NOTHING could clear it. Measured live: four voters aligned, ZERO dissent, nine awake →
            //     netScore 2.9 / declaredW 7.80 = 0.372  × MiddayDamp 0.85 = 0.316  × MtfCounterDamp 0.60 = 0.19
            // against a 0.35 floor. MiddayDamp ALONE sank a perfect reading. Every verdict came out size=0.00.
            //
            // Now CONVICTION = agreement, pure. The floor tests agreement only. CONTEXT shrinks the POSITION: a
            // midday, counter-higher-timeframe trade with genuine agreement takes a SMALLER SIZE — it does not
            // silently become a non-trade. Each number now means exactly one thing.
            // ══ FUSE via the ONE shared decision core (AddOns/CouncilFusion.cs) ══════════════════════════════════
            //  v1.8.1 — the ~200-line inline fuse block MOVED to CouncilFusion.Fuse so the live Council and the
            //  replay HARNESS produce bit-identical verdicts (Docs/SENTINEL_REPLAY_SPEC.md §5 step 2 — the correctness
            //  gate that makes a replay-baked corpus trainable). The seam READS + the account/seam VETO stay HERE (they
            //  need seams/account); ALL the math — kind-aware denomW, deadband→bias, conviction, the full context-damp
            //  chain (breadth·squeeze·clock·participation·MTF·location·profile·regime·flux-absorb) — lives in Fuse.
            //  Modulator seams are read UNCONDITIONALLY (Fuse applies each damp with its OWN bias-gating), and the one
            //  bias-dependent VETO (liquidity wall) is resolved AFTER Fuse using the fused bias.

            // clock — session context (a MODULATOR, not a directional voter)
            int clockPhase = -1; bool clockKill = false; bool inSession = true;
            if (ConsultClock)
            {
                try { var clk = SentinelCore.GetClockState(inst, StaleSec);
                      if (clk != null) { clockPhase = clk.Phase; clockKill = clk.InKillWindow; inSession = clk.InSession; } }
                catch (Exception _sx) { SentinelCore.Swallow("Council.OnBarUpdate", _sx); }
            }
            // participation — relative volume (light volume can only penalise)
            double pRvol = double.NaN;
            if (ConsultParticipation)
            {
                try { var p = Local("PARTIC", SentinelCore.GetParticipationState(seamKey, StaleSec)); if (p != null) pRvol = p.Rvol; }   // scope-keyed (v1.21.0)
                catch (Exception _sx) { SentinelCore.Swallow("Council.OnBarUpdate", _sx); }
            }
            // MTF — the higher-timeframe consensus (Fuse damps only a COUNTER-HTF verdict)
            int mtfBias = 0;
            if (ConsultMtf)
            {
                try { var mtf = Local("MTF", SentinelCore.GetMtfState(seamKey, StaleSec)); if (mtf != null) mtfBias = mtf.Bias; }
                catch (Exception _sx) { SentinelCore.Swallow("Council.OnBarUpdate", _sx); }
            }
            // Location — a structural level in the path (read BOTH sides; Fuse picks by the fused bias)
            bool lvlLong = false, lvlShort = false; string lvlName = null;
            if (ConsultLocation)
            {
                try { var lv = SentinelCore.GetLevelState(seamKey, StaleSec);
                      if (lv != null) { lvlLong = lv.InPath(1, LevelNearAtr); lvlShort = lv.InPath(-1, LevelNearAtr);
                                        if (lvlLong || lvlShort) lvlName = lv.NearestName; } }
                catch (Exception _sx) { SentinelCore.Swallow("Council.OnBarUpdate", _sx); }
            }
            // Profile — price ACCEPTED inside the value area (chop context)
            bool profInValue = false;
            if (ConsultProfile)
            {
                try { var pr = SentinelCore.GetProfileState(seamKey, StaleSec); if (pr != null && pr.InValue) profInValue = true; }
                catch (Exception _sx) { SentinelCore.Swallow("Council.OnBarUpdate", _sx); }
            }
            // Regime — high-volatility / chaotic
            int regimeLbl = -1;
            if (ConsultRegime)
            {
                try { var rg = Local("RGME", SentinelCore.GetRegimeState(seamKey, StaleSec)); if (rg != null) regimeLbl = rg.Regime; }
                catch (Exception _sx) { SentinelCore.Swallow("Council.OnBarUpdate", _sx); }
            }

            // ── build the fuse inputs + policy, then call the shared core ──
            var fx = new CouncilFusion.Inputs();
            for (int i = 0; i < _votes.Count; i++)
                fx.Votes.Add(new CouncilFusion.Vote(_votes[i].Tag, _votes[i].Dir, _votes[i].W));   // ctor recomputes Counted = w>0 identically
            fx.Declared = _declared;
            fx.Spoke    = _spoke;
            if (_declared != null)
                foreach (string t in _declared)
                {
                    fx.WeightOf[t] = WeightFor(t, BaseWeight(t));   // same source as _declaredW ⇒ denomW recompute is bit-identical
                    fx.KindOf[t]   = KindFor(t) == VoterKind.State ? CouncilFusion.Kind.State : CouncilFusion.Kind.Trigger;
                }
            fx.Squeeze        = squeeze;
            fx.InSession      = inSession;
            fx.ClockPhase     = clockPhase;
            fx.Rvol           = pRvol;
            fx.MtfBias        = mtfBias;
            fx.LvlInPathLong  = lvlLong;
            fx.LvlInPathShort = lvlShort;
            fx.InValue        = profInValue;
            fx.HighVolRegime  = (regimeLbl == 2);
            fx.FluxFlowDir    = _fluxFlowDir;
            fx.FluxDiverge    = _fluxDiverge;
            fx.Vetoed         = false;   // account/seam vetoes applied AFTER fuse (the wall veto needs the fused bias)

            var cfg = new CouncilFusion.Config
            {
                BiasDeadband      = BiasDeadband,
                ConvictionFloor   = ConvictionFloor,
                MinVoters         = MinVoters,
                DampenOnSqueeze   = DampenOnSqueeze,
                SqueezeDamp       = 0.6,               // matches the Council's historical hardcoded squeeze damp
                OffSessionDamp    = OffSessionDamp,
                MiddayDamp        = MiddayDamp,
                RvolDampFloor     = RvolDampFloor,
                MtfCounterDamp    = MtfCounterDamp,
                LevelDamp         = LevelDamp,
                InValueDamp       = InValueDamp,
                HighVolRegimeDamp = HighVolRegimeDamp,
                FluxAbsorbDamp    = FluxAbsorbDamp,
            };

            var fr = CouncilFusion.Fuse(fx, cfg);
            int    bias        = fr.Bias;
            double conviction  = fr.Conviction;
            double sizeMult    = fr.SizeMult;
            double contextMult = fr.ContextMult;
            agree      = fr.Agree;
            disagree   = fr.Disagree;
            voters     = fr.Voters;
            _effDenomW = fr.DenomW;                                  // cache for the Reasons audit / Ledger / Lab

            // Normalise the CACHED/PUBLISHED context to how the Council historically exposed it (MTF/Location only
            // when directional), and recompute the Flux-absorption audit flag from the fused bias (Fuse already
            // applied the damp itself). These affect only the published context/reasons, never the verdict.
            if (bias == 0) mtfBias = 0;
            bool lvlInPath = (bias > 0 && lvlLong) || (bias < 0 && lvlShort);
            if (!lvlInPath) lvlName = null;
            _fluxAbsorb = bias != 0 && _fluxDiverge != 0 && _fluxFlowDir != 0 &&
                          ((bias > 0 && _fluxFlowDir < 0) || (bias < 0 && _fluxFlowDir > 0));

            // ── HARD VETOES (account-free) — resolved AFTER fuse so the liquidity WALL can use the fused bias.
            //    Applying a veto = zeroing sizeMult + conviction, bit-identical to passing Vetoed=true into Fuse.
            bool vetoed = false;
            string veto = null;
            try
            {
                if (SentinelCore.KillSwitchEngaged) { vetoed = true; veto = "kill-switch"; }
                else if (SentinelCore.InstrumentKillEngaged(inst)) { vetoed = true; veto = "kill: " + (SentinelCore.InstrumentKillReason(inst) ?? inst); }
                else if (SentinelCore.RolloverBlocked(inst)) { vetoed = true; veto = "rollover"; }
                else if (SentinelCore.NewsLockoutActive(inst))
                {
                    var nl = SentinelCore.ActiveNewsLockoutFor(inst);
                    vetoed = true; veto = "news" + (nl != null && !string.IsNullOrEmpty(nl.Event) ? ": " + nl.Event : "");
                }
                else if (VetoKillWindow && clockKill) { vetoed = true; veto = "kill window"; }
                else if (VetoOnChop && stfChoppy) { vetoed = true; veto = "chop " + stfChop.ToString("0"); }   // STF: ranging tape → stand down
                else if (VetoOnWall && bias != 0)   // only a directional verdict can be blocked by a wall on its side
                {
                    var liq = SentinelCore.GetLiquidityState(seamKey, StaleSec);
                    if (liq != null && liq.BlocksEntry(bias, WallNearTicks))
                    {
                        vetoed = true;
                        veto = (bias > 0 ? "resistance wall " : "support wall ") +
                               (bias > 0 ? liq.DistAboveTicks : liq.DistBelowTicks).ToString("0") + "t";
                    }
                }
            }
            catch (Exception _sx) { SentinelCore.Swallow("Council.OnBarUpdate", _sx); }
            if (vetoed) { sizeMult = 0.0; conviction = 0.0; }

            // ── cache for render ──
            _bias = bias; _conviction = conviction; _sizeMult = sizeMult;
            _agree = agree; _disagree = disagree; _voters = voters;
            _vetoed = vetoed; _vetoReason = veto;
            _clockPhase = clockPhase;
            _pRvol = pRvol;
            _mtfBias = mtfBias; _lvlInPath = lvlInPath; _lvlName = lvlName;
            _stfChoppy = stfChoppy; _stfChop = stfChop;
            _profInValue = profInValue; _regimeLbl = regimeLbl;
            _hasData = true;

            // v1.4.0 — EPISODE key (ML spec §10.2): a maximal run of constant Bias is ONE episode. Bump the sequence
            // on a bias FLIP only (never per tick), so the id is stable across the episode's life and is the join key
            // the Recorder/Bridge/Ledger share. Format "<inst>-<yyyymmdd>-<NNNN>"; date from the bar (chart tz).
            if (bias != _episodeBias)
            {
                _episodeBias = bias;
                _episodeSeq++;
                _episodeId = inst + "-" + Time[0].ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture)
                           + "-" + _episodeSeq.ToString("D4", System.Globalization.CultureInfo.InvariantCulture);
            }

            BiasPlot[0]       = bias;
            ConvictionPlot[0] = conviction;

            if (CurrentBar != _lastHistBar)
            {
                _convHist.Add(bias >= 0 ? conviction : -conviction);   // signed so the sparkline shows side
                if (_convHist.Count > HistMax) _convHist.RemoveAt(0);
                _lastHistBar = CurrentBar;
            }

            // ── publish the verdict for the fleet to consult (SentinelCore ≥ v1.15.0 — keyed by SCOPE) ──
            if (PublishState)
            {
                try
                {
                    // Time[0] is chart-timezone; ToUniversalTime() matches how the Recorder already stamps fireTime.
                    _lastBarTimeUtc = Time[0].ToUniversalTime();
                    // v1.4.0 — snapshot the decision vector (ML spec §2.2) and cache it so the heartbeat republishes
                    // the SAME vector. netScore/activeW are the fuse-block locals; nothing upstream of here moves.
                    SnapshotVotes(netScore, activeW);
                    SentinelCore.SetCouncilState(Scope(), BarTagOf(), inst, bias, conviction, sizeMult,
                                                 agree, disagree, voters, vetoed, veto, BuildReasons(), _pubId,
                                                 _lastBarTimeUtc, State == State.Historical, _roster,
                                                 _lastVotes, _lastVoteW, _lastNetScore, _lastActiveW,
                                                 _clockPhase, _pRvol, _mtfBias, _lvlInPath, _lvlName, _episodeId, CouncilVer);
                }
                catch (Exception _sx) { SentinelCore.Swallow("Council.OnBarUpdate", _sx); }
            }

            // ── the roster's deviation, logged on CHANGE (a silent sensor is the thing this exists to break) ──
            if (_roster != null && State != State.Historical)
            {
                string dev = (_roster.Missing ?? "") + "|" + (_roster.Unexpected ?? "");
                if (dev != _lastLoggedMissing)
                {
                    _lastLoggedMissing = dev;
                    try
                    {
                        if (_roster.Complete && string.IsNullOrEmpty(_roster.Unexpected))
                            SentinelCore.Log("Council", LogTag() + " roster COMPLETE " + _roster.Present + "/" + _roster.Declared);
                        else
                            SentinelCore.Log("Council", LogTag() + " roster " + _roster.ToString() +
                                " — the verdict is fused from a PARTIAL declaration; check the missing sensors loaded.");

                    }
                    catch (Exception _sx) { SentinelCore.Swallow("Council.OnBarUpdate", _sx); }
                }

                // v1.10.0 — DECOUPLED vs ABSENT. Deliberately OUTSIDE the change-gate above: this must be
                // evaluated every bar so the condition can RE-STATE and AUTO-CLEAR (a latch that never
                // re-arms is indistinguishable from a detector that never fires). Cost is 3 hashtable reads.
                //   ABSENT    → the bar type is not on this chart. Load it.
                //   DECOUPLED → it IS publishing, but from a PREVIOUS assembly generation, so the publish
                //               lands in an orphaned static store. ⇒ RESTART NT; a chart reload will NOT fix it.
                ReportDecoupledSeams();
            }

            // ── log only material CHANGES (bias flip or veto toggle) — no per-tick spam ──
            if (LogChanges && State == State.Realtime && (bias != _lastLoggedBias || vetoed != _lastLoggedVeto))
            {
                _lastLoggedBias = bias; _lastLoggedVeto = vetoed;
                try
                {
                    // conv = agreement (vs the floor) · ctx = context multiplier · size = conv × ctx.
                    // Logging ctx separately is what makes a stand-down diagnosable: "below the floor" and
                    // "agreed but the context is hostile" used to look identical (size=0.00).
                    string side = bias > 0 ? "LONG" : (bias < 0 ? "SHORT" : "FLAT");
                    SentinelCore.Log("Council", LogTag() + " " + side + " conv=" + conviction.ToString("0.00") +
                        "/" + ConvictionFloor.ToString("0.00") + " ctx=" + contextMult.ToString("0.00") +
                        " size=" + sizeMult.ToString("0.00") + " (" + agree + "/" + disagree + ", " + voters + "v)" +
                        (vetoed ? " VETO:" + veto : "") + " | " + BuildReasons());
                }
                catch (Exception _sx) { SentinelCore.Swallow("Council.OnBarUpdate", _sx); }
            }
        }

        // ── HEARTBEAT republish (v1.0.0 in-place, 2026-07-08) ──
        // Calculate.OnPriceChange re-publishes the verdict only when PRICE moves. In thin / dry-up markets the
        // price can sit still for minutes, so the published CouncilState goes stale and consumers (SentinelBridge)
        // read "no verdict" even though the on-chart card still shows the last one. This re-stamps the SAME cached
        // verdict on any incoming quote (throttled to HeartbeatSec), keeping it fresh between price changes.
        // No recompute, no log — pure timestamp refresh.
        protected override void OnMarketData(NinjaTrader.Data.MarketDataEventArgs marketDataUpdate)
        {
            if (!PublishState || !_hasData || State != State.Realtime) return;
            DateTime now = DateTime.UtcNow;
            if ((now - _lastHeartbeatUtc).TotalSeconds < HeartbeatSec) return;
            _lastHeartbeatUtc = now;
            try
            {
                // Re-stamp the SAME cached verdict, carrying its ORIGINAL bar time — a heartbeat must never
                // pretend a new bar produced the verdict. Realtime-only, so isHistorical is false by construction.
                SentinelCore.SetCouncilState(Scope(), BarTagOf(), Instrument.MasterInstrument.Name,
                                             _bias, _conviction, _sizeMult, _agree, _disagree, _voters,
                                             _vetoed, _vetoReason, BuildReasons(), _pubId,
                                             _lastBarTimeUtc, false, _roster,
                                             _lastVotes, _lastVoteW, _lastNetScore, _lastActiveW,
                                             _clockPhase, _pRvol, _mtfBias, _lvlInPath, _lvlName, _episodeId, CouncilVer);
            }
            catch (Exception _sx) { SentinelCore.Swallow("Council.OnMarketData", _sx); }
        }

        // ── scope resolution (SentinelCore v1.15.0 · execution plan 1.2) ────────────────────────────
        // The seam key is "<masterInstrument>.<barTag>" — ONE CHART's worth of context. Two GC charts on
        // different bar types used to overwrite each other's verdict on every tick, and a Bridge on either
        // could trade the other's brain. Resolved lazily (Instrument/BarsPeriod are live from DataLoaded on)
        // and cached. A null scope makes SetCouncilState a no-op — the right fail-silent for an
        // indicator that is not yet configured.
        private string Scope()
        {
            if (_scope == null)
            {
                // v1.7.x — LANED scope: fold in this chart's lane (registered in DataLoaded, keyed by ChartControl).
                // Two same-bartype charts now publish/consume distinct scopes (…@A / …@B). Bare when no lane.
                try { _scope = SentinelCore.ScopeOf(Instrument, BarsPeriod, ChartControl); } catch (Exception _sx) { SentinelCore.Swallow("Council.Scope", _sx); }
                // Announce the scope ONCE per instance. Two Council instances on one instrument now announce
                // two DIFFERENT scopes — which is simultaneously the proof the collision is fixed and the
                // answer to an operator asking "which chart produced this verdict?".
                if (_scope != null) { try { SentinelCore.Log("Council", _pubId + " publishing scope " + _scope); } catch (Exception _sx) { SentinelCore.Swallow("Council.Scope", _sx); } }
            }
            return _scope;
        }

        // v1.7.x — the BARE (un-laned) scope, for BAR-TYPE seams (BrickState/FluxState). A bars series is SHARED
        // across every chart on the same instrument+bartype+size, so its published state is legitimately common —
        // it must be read on the bare scope, NOT the lane, or the Council would never find it on a laned chart.
        // ══ v1.11.0 — CHART-DERIVED SENSORS MUST BE ON THIS CHART ═══════════════════════════════════
        // A sensor seam is process-global and keyed BARE (instrument + bar type), which is right for a
        // value that does not vary with the consuming chart. But an OHLC sensor's reading is computed
        // from ITS OWN chart's bars, so an off-chart STF was describing SOMEBODY ELSE'S chart and still
        // voting here — and worse, still VETOING here, because VetoOnChop hangs off `stf != null` and is
        // independent of WeightStf (which defaults 0, so it read as inert). A sensor you never attached,
        // at weight zero, could zero your conviction and your size.
        //
        // The gate is therefore CLASSIFICATION, not presence-for-everything:
        //   CHART-DERIVED  (below)  → must be attached to THIS chart, else no vote, no veto, no damp.
        //   INSTRUMENT-LEVEL        → Clock (wall clock), Intermarket (other instruments), News,
        //                             LiquidityWalls (book), CVD / BSP (tape), Level, Profile. These have
        //                             no chart of their own; requiring attachment would be meaningless.
        //   BAR-TYPE SEAMS          → BRK / FLUX / CVB are the chart's OWN bars type. Exempt by definition.
        //
        // ⭐ Gating the SEAM READ (not each modulator) closes the whole class in one move: the vote, the
        // veto and the damp all hang off the same `!= null`, so nulling the read kills all three. Gating
        // only VetoOnChop would have fixed one row of a table with six.
        private static readonly Dictionary<string, string> ChartDerivedPublisher = new Dictionary<string, string>
        {
            { "TRND",   "SentinelTrend_" },                      // ⚠ trailing _ : "SentinelTrend" alone also
            { "CCI",    "WoodiesCCIPro" },                       //   prefix-matches SentinelTrendArchitect
            { "ADX",    "ADXPro" },
            { "ENV",    "VolEnvelope" },                         // also drives DampenOnSqueeze
            { "CMP",    "CompressionBase" },
            { "WAE",    "SentinelWAE" },
            { "GREV",   "SentinelGodReversal" },
            { "STF",    "SentinelStochasticTripleFilter" },      // also drives VetoOnChop — the reported bug
            { "STRC",   "SentinelStructure" },
            { "EXH",    "SentinelExhaustion" },
            { "AVMA",   "SentinelADXVMA" },
            { "SPRT",   "SentinelSuperTrend" },
            { "PSAR",   "SentinelParabolicSAR" },
            { "ZSC",    "SentinelZScore" },
            { "VDYA",   "SentinelVIDYA" },
            { "HARM",   "SentinelHarmonic" },
            { "ARCH",   "SentinelTrendArchitect" },
            { "RGME",   "SentinelRegime" },
            { "PARTIC", "Participation_" },
            { "MTF",    "Mtf_" },
        };

        private readonly HashSet<string> _onChartTypes = new HashSet<string>(StringComparer.Ordinal);
        private readonly object _onChartGate = new object();
        private bool     _onChartResolved;          // has a scan ever completed? (before that we cannot judge)
        private DateTime _onChartScanUtc = DateTime.MinValue;
        private readonly HashSet<string> _skippedOffChart = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Rebuild the set of indicator TYPE names attached to this chart. UI thread only —
        /// enumerating ChartControl.Indicators off the UI thread is the documented way to throw here
        /// (memory nt-consume-indicator-plots). Fired async from the data thread; never blocks a bar.</summary>
        private void RefreshOnChartTypes()
        {
            var cc = ChartControl;
            if (cc == null) return;
            if ((DateTime.UtcNow - _onChartScanUtc).TotalSeconds < 5.0) return;
            _onChartScanUtc = DateTime.UtcNow;
            try
            {
                cc.Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        var found = new HashSet<string>(StringComparer.Ordinal);
                        foreach (var ind in cc.Indicators)
                        {
                            if (ind == null) continue;
                            found.Add(ind.GetType().Name);
                        }
                        lock (_onChartGate)
                        {
                            _onChartTypes.Clear();
                            foreach (var s in found) _onChartTypes.Add(s);
                            _onChartResolved = true;
                        }
                    }
                    catch (Exception _sx) { SentinelCore.Swallow("Council.RefreshOnChartTypes.ui", _sx); }
                });
            }
            catch (Exception _sx) { SentinelCore.Swallow("Council.RefreshOnChartTypes", _sx); }
        }

        /// <summary>Is the publisher of this chart-derived voter actually attached to THIS chart?</summary>
        private bool OnChart(string tag)
        {
            if (!RequireSensorOnChart) return true;              // opt-out (default OFF = gate ON)
            string prefix;
            if (!ChartDerivedPublisher.TryGetValue(tag, out prefix)) return true;   // instrument-level / bar-type
            // No chart at all (headless / harness): attachment is UNDEFINED, not false. Falling closed here
            // would silently mute a Council that has no chart to be attached to, which is the
            // "a crashed sensor is indistinguishable from a quiet one" failure. Fail OPEN and say so.
            if (ChartControl == null) return true;
            lock (_onChartGate)
            {
                // Not scanned yet: fail CLOSED for a beat rather than let an off-chart sensor vote once.
                // The scan lands within a bar, and the skip is named in the Reasons audit.
                if (!_onChartResolved) { _skippedOffChart.Add(tag); return false; }
                foreach (var t in _onChartTypes)
                    if (t.StartsWith(prefix, StringComparison.Ordinal)) return true;
                _skippedOffChart.Add(tag);
                return false;
            }
        }

        /// <summary>Wrap a seam read: hand back the state only if its publisher is on this chart.
        /// Nulling it here suppresses the vote AND every veto/damp that hangs off the same null check.</summary>
        private T Local<T>(string tag, T state) where T : class
        {
            return OnChart(tag) ? state : null;
        }

        private string BareScope()
        {
            try { return SentinelCore.ScopeOf(Instrument, BarsPeriod); } catch { return null; }
        }

        // The voters published by a BAR TYPE rather than an indicator — the only ones that can be DECOUPLED
        // by an F5 (indicators are rebuilt on reload; a chart's bars-type instance is not).
        private static readonly string[] BarTypeVoters = { "BRK", "FLUX", "CVB" };

        /// <summary>For each missing BAR-TYPE voter, ask the AppDomain beacon whether some OTHER assembly
        /// generation is still publishing it. If so the sensor is not absent, it is DECOUPLED — a distinction
        /// that did not exist before 2026-07-24 and that cost an entire audition bake (1,866 rows, zero
        /// bar-type voters, every guard reading healthy). Reported through Conditions so it debounces,
        /// re-states, and auto-clears rather than latching once and going quiet.</summary>
        private void ReportDecoupledSeams()
        {
            try
            {
                string scope = BareScope();
                if (string.IsNullOrEmpty(scope) || _roster == null) return;

                var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string t in (_roster.Missing ?? "").Split(new[] { ',', ' ', '⚠' }, StringSplitOptions.RemoveEmptyEntries))
                    missing.Add(t.Trim());

                foreach (string kind in BarTypeVoters)
                {
                    string key = "seam|decoupled|" + scope + "|" + kind;
                    string foreign = missing.Contains(kind) ? SentinelCore.BeaconForeign(scope, kind) : null;
                    if (foreign == null) { SentinelCore.Conditions.Clear(key); continue; }

                    // cooldown 300s: re-state every 5 min while it persists, so a long bake cannot drift on
                    // in a decoupled state after the operator scrolled past the first line.
                    if (SentinelCore.Conditions.ShouldReport(key, true, 0, 300))
                        SentinelCore.Log("Council", LogTag() + " ⛔ " + kind + " SEAM DECOUPLED — a bars type IS "
                            + "publishing it (" + foreign + ") but from a PREVIOUS assembly generation (mine "
                            + SentinelCore.Generation + "). An F5 rebuilds indicators, NOT the chart's bars-type "
                            + "instance, so its publish lands in a store nothing reads. ⇒ RESTART NINJATRADER. "
                            + "A chart reload will NOT fix this. Do not bake until this line is gone.");
                }
            }
            catch (Exception _sx) { SentinelCore.Swallow("Council.ReportDecoupledSeams", _sx); }   // a diagnostic must never break the verdict path
        }

        private string BarTagOf()
        {
            if (_barTag == null) { try { _barTag = SentinelCore.BarTag(BarsPeriod); } catch (Exception _sx) { SentinelCore.Swallow("Council.BarTagOf", _sx); } }
            return _barTag;
        }

        /// <summary>How this Council names itself in sentinel.log. Every line must carry the SCOPE, not the bare
        /// instrument: two GC charts each logged "GC …", so a COMPLETE 10/10 roster and a 3/10 one on a near-blind
        /// chart were indistinguishable in the log. Falls back to the instrument only before the scope resolves.</summary>
        private string LogTag()
        {
            string s = Scope();
            if (!string.IsNullOrEmpty(s)) return s;
            return (Instrument != null && Instrument.MasterInstrument != null) ? Instrument.MasterInstrument.Name : "?";
        }

        // v1.4.0 — project the vote list into the two machine-readable dictionaries the Lab fits on, and cache them
        // (with the signed netScore / activeW) for the heartbeat republish. FRESH voters only: the roster's absent
        // sensors are reported via _roster, never as phantom zero votes. A w=0 EXPLORER is included (dir + w=0) — that
        // is exactly the exploration history we want in the corpus (measure its contribution before it ever counts).
        // Fresh dicts each time (never handed a live reference): Core copies them again, but the cache the heartbeat
        // reuses must be a stable snapshot since _votes is cleared at the top of the next OnBarUpdate.
        private void SnapshotVotes(double netScore, double activeW)
        {
            var votes = new Dictionary<string, int>(_votes.Count, StringComparer.Ordinal);
            var voteW = new Dictionary<string, double>(_votes.Count, StringComparer.Ordinal);
            for (int i = 0; i < _votes.Count; i++)
            {
                Vote v = _votes[i];
                if (!v.Fresh) continue;
                votes[v.Tag] = v.Dir;
                voteW[v.Tag] = v.W;
            }
            _lastVotes = votes; _lastVoteW = voteW;
            _lastNetScore = netScore; _lastActiveW = activeW;
        }

        private void AddVote(string tag, int dir, double weight, ref double netScore, ref double activeW, ref int voters)
        {
            if (_declared != null && !_declared.Contains(tag))
            {
                // Present but NOT declared. Flag it; never silently fold it in — an undeclared voter would make the
                // verdict depend on chart furniture rather than on the model (ML spec §10.4).
                if (!_unexpected.Contains(tag)) _unexpected.Add(tag);
                _votes.Add(new Vote { Tag = tag, Dir = Math.Sign(dir), W = 0, Fresh = true, Counted = false });
                return;
            }

            _spoke.Add(tag);
            // w = 0 is the EXPLORATION PRIMITIVE: the voter votes and is recorded, but contributes nothing to
            // netScore, to activeW, or to the breadth damping via `voters`. Its full history accumulates, so what it
            // WOULD have contributed is measurable before it ever influences a single trade.
            bool counted = weight > 0;
            _votes.Add(new Vote { Tag = tag, Dir = Math.Sign(dir), W = weight, Fresh = true, Counted = counted });
            if (!counted) return;

            voters++;                                  // a fresh, counting reading existed (even if neutral)
            if (dir != 0)
            {
                netScore += Math.Sign(dir) * weight;
                activeW  += weight;
            }
        }

        /// <summary>The weight a voter carries — a Roster.conf `w=` override, else its F6 property.</summary>
        private double WeightFor(string tag, double fromProperty)
        {
            double w;
            if (_rosterW != null && _rosterW.TryGetValue(tag, out w)) return w;
            return fromProperty;
        }

        /// <summary>The property weight a tag carries with no override — the basis of the DEFAULT declaration.</summary>
        private double BaseWeight(string tag)
        {
            switch (tag)
            {
                case "TRND": return WeightTrend;
                case "CCI":  return WeightCci;
                case "ADX":  return WeightAdx;
                case "ENV":  return WeightEnv;
                case "BRK":  return WeightBrick;
                case "CMP":  return WeightComp;
                case "IMKT": return WeightIntermarket;
                case "WAE":  return WeightWae;
                case "GREV": return WeightGodRev;
                case "STF":  return WeightStf;
                case "FLOW": return WeightFlow;
                case "STRC": return WeightStructure;
                case "EXH":  return WeightExhaustion;
                case "AVMA": return WeightAdxvma;
                case "SPRT": return WeightSuperTrend;
                case "PSAR": return WeightSar;
                case "ZSC":  return WeightZScore;
                case "ARCH": return WeightArch;
                case "VDYA": return WeightVidya;
                case "HARM": return WeightHarmonic;
                case "FLUX": return WeightFlux;
                case "CVB": return WeightConviction;
                case "CVD": return WeightCvd;
                case "BSP": return WeightPressure;
                default:     return 0;
            }
        }

        // ── voter KIND (v1.3.0) ─────────────────────────────────────────────────────────────────────
        // The conviction denominator conflated two kinds of voter. A STATE voter always carries a reading (trend
        // up/down/flat) — its neutral is a real opinion, so it always dilutes. A TRIGGER voter is ±1 only on the rare
        // bar it fires and reads 0 ("nothing to report") the rest of the time; scoring that silence as "looked, saw
        // no direction" pinned conviction near 0.16, and one verdict in 97 cleared the floor. A quiet trigger is an
        // ABSENCE OF EVIDENCE, not evidence against, so it now leaves the denominator alone; an ABSENT (crashed/stale)
        // trigger still dilutes, because we don't know what it would have said — that is the roster's whole purpose.
        // Classification VERIFIED against the published seams: BrickState.Direction is -1/1 and NEVER 0 (a brick
        // always has a direction), so BRK is STATE, not the trigger the earlier changelog assumed.
        private enum VoterKind { State, Trigger }
        private static VoterKind DefaultKind(string tag)
        {
            switch (tag)
            {
                case "CMP": case "WAE": case "GREV": case "EXH": case "ZSC": case "HARM": return VoterKind.Trigger;
                default:                                          return VoterKind.State;  // TRND CCI ADX ENV IMKT BRK FLOW STRC AVMA SPRT PSAR ARCH VDYA FLUX
            }
        }
        /// <summary>The kind a voter carries — a Roster.conf `state`/`trigger` override, else DefaultKind.</summary>
        private VoterKind KindFor(string tag)
        {
            VoterKind k;
            if (_rosterKind != null && _rosterKind.TryGetValue(tag, out k)) return k;
            return DefaultKind(tag);
        }

        // ── the declaration ───────────────────────────────────────────────────────────────────────
        // Sentinel\Models\<INST>\<bartag>\Roster.conf  ▸  …\<INST>\Roster.conf  ▸  …\Models\Roster.conf
        //   CMP   w=0.7 trigger  # declared voter, weight override + KIND override (v1.3.0; bare word or `kind=trigger`)
        //   TRND  state          # declared voter, weight from the F6 property, kind forced to STATE
        //   NEWSENSOR w=0        # exploration: votes and is recorded, contributes nothing
        // v1.8.0 — PER-LANE SYSTEM PROFILE. Apply Sentinel\Models\<inst>\<ladedTag>\Lane.conf OVER the F6 fusion
        // knobs: only keys PRESENT in the file override; an absent key keeps the chart's F6 value (SSentinelCore.LaneIO
        // enforces "absent ⇒ inherit" — Try* returns false and we don't touch the property). Lets an A/B test LANE pin
        // its own ConvictionFloor / bias deadband / context-consult toggles / modulator damps without hand-editing F6
        // on every chart (System Builder spec §14.7). Roster.conf holds voters+weights+kind; Lane.conf holds the rest.
        // ── v1.9.0 — LIVE CONFIG RELOAD ────────────────────────────────────────────────────────────────────────
        // Roster.conf / Lane.conf used to be read ONCE at DataLoaded, so changing a floor, a veto or a voter weight
        // cost an F5 *and* a chart reload. That ritual is most of what made iterating unbearable. These files are
        // now POLLED on their write time — the same idiom SentinelSkin already uses for `cards.off` / `theme.txt`
        // (drop a file, it takes effect in ≤2s, no compile, no reload). Edit a conf, the next bar picks it up.
        // Cheap: two File.GetLastWriteTimeUtc calls at most once per ConfigPollSec, on the data thread, no alloc.
        private string   _cfgInst, _cfgTag;
        private DateTime _cfgChecked, _rosterMtime, _laneMtime;
        private const double ConfigPollSec = 2.0;

        private void StampConfigMtimes()
        {
            try
            {
                string rp = SentinelCore.RosterIO.Resolve(_cfgInst, _cfgTag);
                string lp = SentinelCore.LaneIO.Resolve(_cfgInst, _cfgTag);
                _rosterMtime = rp != null ? System.IO.File.GetLastWriteTimeUtc(rp) : DateTime.MinValue;
                _laneMtime   = lp != null ? System.IO.File.GetLastWriteTimeUtc(lp) : DateTime.MinValue;
            }
            catch (Exception _sx) { SentinelCore.Swallow("Council.StampConfigMtimes", _sx); }
        }

        private void MaybeReloadConfig()
        {
            if (_cfgInst == null) return;
            DateTime now = DateTime.UtcNow;
            if ((now - _cfgChecked).TotalSeconds < ConfigPollSec) return;
            _cfgChecked = now;
            try
            {
                string rp = SentinelCore.RosterIO.Resolve(_cfgInst, _cfgTag);
                string lp = SentinelCore.LaneIO.Resolve(_cfgInst, _cfgTag);
                DateTime rm = rp != null ? System.IO.File.GetLastWriteTimeUtc(rp) : DateTime.MinValue;
                DateTime lm = lp != null ? System.IO.File.GetLastWriteTimeUtc(lp) : DateTime.MinValue;
                if (rm == _rosterMtime && lm == _laneMtime) return;

                // Re-apply BOTH: the lane profile writes over F6 properties, so a roster-only change must not
                // leave a half-applied state. Order matches DataLoaded exactly (roster, then lane).
                LoadRoster(_cfgInst, _cfgTag);
                ApplyLaneProfile(_cfgInst, _cfgTag);
                _rosterMtime = rm; _laneMtime = lm;
                try { SentinelCore.Log("Council", _pubId + " CONFIG RELOADED (live) — roster=" + (rp ?? "<none>")
                                                + "  lane=" + (lp ?? "<none>")); } catch (Exception _sx) { SentinelCore.Swallow("Council.MaybeReloadConfig", _sx); }
            }
            catch (Exception _sx) { SentinelCore.Swallow("Council.MaybeReloadConfig", _sx); }
        }

        private void ApplyLaneProfile(string inst, string ladedTag)
        {
            try
            {
                var m = SentinelCore.LaneIO.Read(inst, ladedTag);
                if (m == null || m.Count == 0) return;
                double d; bool b;
                if (SentinelCore.LaneIO.TryDouble(m, "floor", out d))                 ConvictionFloor      = d;
                if (SentinelCore.LaneIO.TryDouble(m, "deadband", out d))              BiasDeadband         = d;
                if (SentinelCore.LaneIO.TryBool  (m, "consultclock", out b))          ConsultClock         = b;
                if (SentinelCore.LaneIO.TryBool  (m, "consultparticipation", out b))  ConsultParticipation = b;
                if (SentinelCore.LaneIO.TryBool  (m, "consultmtf", out b))            ConsultMtf           = b;
                if (SentinelCore.LaneIO.TryBool  (m, "consultlocation", out b))       ConsultLocation      = b;
                if (SentinelCore.LaneIO.TryBool  (m, "consultprofile", out b))        ConsultProfile       = b;
                if (SentinelCore.LaneIO.TryBool  (m, "consultregime", out b))         ConsultRegime        = b;
                if (SentinelCore.LaneIO.TryDouble(m, "fluxabsorbdamp", out d))        FluxAbsorbDamp       = d;
                if (SentinelCore.LaneIO.TryDouble(m, "leveldamp", out d))             LevelDamp            = d;
                if (SentinelCore.LaneIO.TryDouble(m, "invaluedamp", out d))           InValueDamp          = d;
                if (SentinelCore.LaneIO.TryDouble(m, "highvolregimedamp", out d))     HighVolRegimeDamp    = d;
                if (SentinelCore.LaneIO.TryDouble(m, "mtfcounterdamp", out d))        MtfCounterDamp       = d;
                if (SentinelCore.LaneIO.TryDouble(m, "middaydamp", out d))            MiddayDamp           = d;
                if (SentinelCore.LaneIO.TryDouble(m, "offsessiondamp", out d))        OffSessionDamp       = d;
                if (SentinelCore.LaneIO.TryDouble(m, "rvoldampfloor", out d))         RvolDampFloor        = d;
                // v1.8.3 — HARD-VETO knobs are lane-settable too. A damp SCALES a recorded verdict; a hard veto
                // DELETES it: SentinelExcursionRecorder gates on `!v.Vetoed`, so a vetoed bar never reaches the
                // corpus at all. Leaving these F6-only meant an AUDITION lane silently censored whole regimes
                // (chop especially) with nothing in the log to say so — and required remembering a checkbox on
                // every new bar-type chart. Now the audition Lane.conf turns them off once, for every chart.
                if (SentinelCore.LaneIO.TryBool  (m, "vetoonchop", out b))            VetoOnChop           = b;
                if (SentinelCore.LaneIO.TryBool  (m, "vetokillwindow", out b))        VetoKillWindow       = b;
                if (SentinelCore.LaneIO.TryDouble(m, "wallnearticks", out d))         WallNearTicks        = d;
                if (SentinelCore.LaneIO.TryBool  (m, "vetoonwall", out b))            VetoOnWall           = b;
                if (SentinelCore.LaneIO.TryDouble(m, "minvoters", out d))             MinVoters            = (int)d;
                try { SentinelCore.Log("Council", _pubId + " Lane.conf profile applied (" + m.Count + " override(s))"); } catch (Exception _sx) { SentinelCore.Swallow("Council.ApplyLaneProfile", _sx); }
            }
            catch (Exception _sx) { SentinelCore.Swallow("Council.ApplyLaneProfile", _sx); }
        }

        private void LoadRoster(string inst, string barTag)
        {
            _declared = null; _rosterW = null; _rosterKind = null; _rosterSource = null;
            try
            {
                // Roster read + parse is owned by SentinelCore.RosterIO (Core v1.27.0) — ONE format the
                // Council (reader) and the System Builder (writer) share, so they can never drift. Same
                // cascade (scope ▸ instrument ▸ global), same grammar, same first-wins dedup as the old
                // in-class ParseRoster. The DEFAULT declaration below stays here (it needs KnownVoters/BaseWeight).
                var doc = SentinelCore.RosterIO.Read(inst, barTag);
                if (doc != null && doc.HasDeclarations)
                {
                    var decl    = new List<string>();
                    var weights = new Dictionary<string, double>(StringComparer.Ordinal);
                    var kinds   = new Dictionary<string, VoterKind>(StringComparer.Ordinal);
                    foreach (var l in doc.Lines)
                    {
                        if (l == null || string.IsNullOrEmpty(l.Tag) || decl.Contains(l.Tag)) continue;
                        decl.Add(l.Tag);
                        if (l.Weight.HasValue) weights[l.Tag] = l.Weight.Value;
                        if (l.Kind.HasValue)   kinds[l.Tag]   = l.Kind.Value == SentinelCore.VoterKind.Trigger ? VoterKind.Trigger : VoterKind.State;
                    }
                    if (decl.Count > 0)
                    {
                        _declared     = decl;
                        _rosterW      = weights.Count > 0 ? weights : null;
                        _rosterKind   = kinds.Count   > 0 ? kinds   : null;
                        _rosterSource = doc.Source;
                    }
                }
            }
            catch { _declared = null; _rosterW = null; _rosterKind = null; }

            if (_declared == null || _declared.Count == 0)
            {
                // DEFAULT DECLARATION — derived from the CONFIGURED weights, never from which seams are live.
                // Deriving it by observation would bake today's outage into the "expected" set, and the roster
                // could never report it. That is precisely the bug this exists to catch.
                _declared = new List<string>();
                foreach (string t in KnownVoters) if (BaseWeight(t) > 0) _declared.Add(t);
                _rosterW = null;
                _rosterSource = "default (configured weights > 0)";
            }

            // The conviction denominator: the summed BASE weight of every declared voter (a w=0 explorer adds 0, so
            // it can never inflate it). BASE, not effective — the per-sensor strength multipliers are situational,
            // and folding them in would make the denominator move with the very readings it is meant to normalise.
            _declaredW = 0;
            double stateW = 0;                        // v1.3.0 — the quiet-bar FLOOR of the denominator (STATE voters only)
            foreach (string t in _declared)
            {
                double w = Math.Max(0.0, WeightFor(t, BaseWeight(t)));
                _declaredW += w;
                if (KindFor(t) == VoterKind.State) stateW += w;
            }

            try
            {
                SentinelCore.Log("Council", LogTag() + " roster declares " + _declared.Count + " voters [" +
                    string.Join(",", _declared.ToArray()) + "] from " + _rosterSource +
                    "  (declaredW=" + _declaredW.ToString("0.00") + ", stateW=" + stateW.ToString("0.00") +
                    " — the denominator on a quiet-trigger bar)");
            }
            catch (Exception _sx) { SentinelCore.Swallow("Council.LoadRoster", _sx); }
        }

        // ParseRoster moved to SentinelCore.RosterIO.Parse (Core v1.27.0) — the single format owner
        // shared with the System Builder. LoadRoster above consumes RosterIO.Read.

        /// <summary>Resolve the declaration against what actually spoke this update.</summary>
        private SentinelCore.RosterInfo ResolveRoster()
        {
            if (_declared == null) return null;
            var mask    = new List<string>();
            var missing = new List<string>();
            foreach (string t in _declared)
            {
                if (_spoke.Contains(t)) mask.Add(t);
                else missing.Add(t);
            }
            return new SentinelCore.RosterInfo
            {
                Mask       = string.Join(",", mask.ToArray()),
                Missing    = missing.Count > 0 ? string.Join(",", missing.ToArray()) : null,
                Unexpected = _unexpected.Count > 0 ? string.Join(",", _unexpected.ToArray()) : null,
                Declared   = _declared.Count,
                Present    = mask.Count,
                Complete   = missing.Count == 0,
                Source     = _rosterSource
            };
        }

        private string BuildReasons()
        {
            var sb = new StringBuilder();
            foreach (var v in _votes)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(v.Tag).Append(v.Dir > 0 ? "▲" : (v.Dir < 0 ? "▼" : "~"));
                if (!v.Counted) sb.Append('*');     // heard but not counted (w=0 explorer, or undeclared)
            }
            if (_votes.Count == 0) sb.Append("no fresh signals");
            if (_clockPhase >= 0) sb.Append(" · clk:").Append(ClockPhaseName[_clockPhase]);
            if (!double.IsNaN(_pRvol)) sb.Append(" · vol×").Append(_pRvol.ToString("0.0"));
            if (_mtfBias != 0 && _mtfBias != _bias) sb.Append(" · vs-MTF");
            if (_lvlInPath) sb.Append(" · into ").Append(_lvlName ?? "level");
            if (_stfChoppy) sb.Append(" · chop ").Append(_stfChop.ToString("0"));
            if (_profInValue) sb.Append(" · in-value");
            if (_regimeLbl == 2) sb.Append(" · hi-vol");
            if (_flowDiverge != 0) sb.Append(_flowDiverge > 0 ? " · +flowDiv" : " · -flowDiv");
            if (_fluxAbsorb) sb.Append(" · flux:absorb");
            else if (_fluxFlowDir != 0) sb.Append(_fluxFlowDir > 0 ? " · flux▲" : " · flux▼");
            if (_cvbBias != 0) sb.Append(_cvbBias > 0 ? " · cvb▲" : " · cvb▼");
            if (_bspDir != 0) sb.Append(_bspDir > 0 ? " · bsp▲" : " · bsp▼").Append(_bspTick ? "" : "(proxy)");
            if (_bspDiv != 0) sb.Append(_bspDiv > 0 ? " · bspDiv(bull)" : " · bspDiv(bear)");
            if (_cvdDir != 0) sb.Append(_cvdDir > 0 ? " · cvd▲" : " · cvd▼");
            if (_cvdDiverge != 0) sb.Append(_cvdDiverge > 0 ? " · cvdDiv(bull)" : " · cvdDiv(bear)");
            if (_cvdEffZ < -1.0) sb.Append(" · absorbing");
            else if (_cvdEffZ > 1.0) sb.Append(" · thin");
            // v1.11.0 — name what the on-chart gate EXCLUDED. Without this the gate is invisible: a declared
            // voter that is simply not attached looks identical to one that is attached and abstaining, and
            // "why didn't it fire" becomes unanswerable. This is the off-chart analogue of the roster
            // deviation below, and it is why the gate ships WITH an audit line rather than silently.
            try
            {
                string[] off = null;
                // HashSet.CopyTo, not LINQ's ToArray — this file does not import System.Linq and an
                // instrument-wide `using` is not worth adding for one audit line.
                lock (_onChartGate)
                {
                    if (_skippedOffChart.Count > 0)
                    {
                        off = new string[_skippedOffChart.Count];
                        _skippedOffChart.CopyTo(off);
                    }
                }
                if (off != null && off.Length > 0)
                {
                    Array.Sort(off, StringComparer.Ordinal);
                    sb.Append(" · off-chart:").Append(string.Join(",", off));
                }
            }
            catch (Exception _sx) { SentinelCore.Swallow("Council.Reasons.offchart", _sx); }

            // The roster's DEVIATION belongs in the audit string: a verdict fused from 8 of 10 declared voters is
            // not the same verdict as one fused from all 10, and the record must say which it was.
            if (_roster != null)
            {
                sb.Append(" · roster ").Append(_roster.Present).Append('/').Append(_roster.Declared);
                if (!string.IsNullOrEmpty(_roster.Missing))    sb.Append(" ⚠").Append(_roster.Missing);
                if (!string.IsNullOrEmpty(_roster.Unexpected)) sb.Append(" ?").Append(_roster.Unexpected);
            }
            // v1.3.0 — the KIND-AWARE effective denominator vs the static declared weight. This is what conviction was
            // divided by; recording it lets the Lab reconstruct the new histogram and FIT the floor against it.
            if (_effDenomW > 0)
                sb.Append(" · denom ").Append(_effDenomW.ToString("0.0")).Append('/').Append(_declaredW.ToString("0.0"));
            return sb.ToString();
        }

        // ── Sentinel "flight-instrument" glass card (SharpDX / SentinelSkin.Painter) ──
        protected override void OnRender(NinjaTrader.Gui.Chart.ChartControl chartControl, NinjaTrader.Gui.Chart.ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);
            if (!ShowCard || RenderTarget == null || ChartPanel == null) return;
            try
            {
                if (_sp == null) _sp = new SentinelSkin.Painter();
                _sp.Begin(RenderTarget);

                const float cw = 244f, ch = 198f;
                // pinned: this is the DECISION card. The suite protects exactly three surfaces from an overflow
                // layout pass — decision (Council), risk (the Deck's P&L) and control (the Bridge's ARM button).
                // Collapse takes the bottom-most card first, and the Council sits at the bottom of the left column,
                // so without this the brain would be the first thing chipped. Sensors may shrink; the verdict may not.
                var slot = SentinelSkin.CardLayout.Place(this, ChartPanel,
                    ChartPanel.X, ChartPanel.Y, ChartPanel.W, ChartPanel.H, CardCorner, cw, ch, pinned: true);

                if (!_hasData)
                {
                    var rw = _sp.Card(slot.X, slot.Y, cw, ch, SentinelSkin.CLine);
                    _sp.Dot(rw.Left + 5f, rw.Top + 8f, SentinelSkin.CMute, false);
                    _sp.Text("COUNCIL", rw.Left + 16f, rw.Top, rw.Width - 20f, 16f, SentinelSkin.CInk, 11f, true);
                    if (!string.IsNullOrEmpty(_laneTag))
                        _sp.Text("· " + _laneTag, rw.Left + 84f, rw.Top, 80f, 16f, SentinelSkin.CAccent, 11f, true);
                    _sp.Text("waiting for signals…", rw.Left, rw.Top + 26f, rw.Width, 14f, SentinelSkin.CMute, 10.5f);
                    _sp.End();
                    return;
                }

                var trail   = SharpDX.DirectWrite.TextAlignment.Trailing;
                var biasCol = _bias > 0 ? SentinelSkin.CUp : (_bias < 0 ? SentinelSkin.CDown : SentinelSkin.CMute);
                var edge    = _vetoed ? SentinelSkin.CWarn : (_bias != 0 && _sizeMult > 0 ? SentinelSkin.CAccent : SentinelSkin.CLine);
                var r = _sp.Card(slot.X, slot.Y, cw, ch, edge);

                // header — live/veto dot, title, bias pill
                var dotCol = _vetoed ? SentinelSkin.CWarn : (_voters > 0 ? SentinelSkin.CAccent : SentinelSkin.CMute);
                _sp.Dot(r.Left + 5f, r.Top + 8f, dotCol, _voters > 0);
                _sp.Text("COUNCIL", r.Left + 16f, r.Top, r.Width - 90f, 16f, SentinelSkin.CInk, 11f, true);
                // v1.8.0 — LANE badge: identify the chart by its lane at a glance (cyan accent, only when set)
                if (!string.IsNullOrEmpty(_laneTag))
                    _sp.Text("· " + _laneTag, r.Left + 84f, r.Top, 80f, 16f, SentinelSkin.CAccent, 11f, true);
                _sp.Pill(_bias > 0 ? "LONG" : (_bias < 0 ? "SHORT" : "FLAT"), r.Right, r.Top - 1f, biasCol);

                // hero — conviction % + size multiplier, then the meter
                _sp.Text("CONVICTION", r.Left, r.Top + 24f, 120f, 12f, SentinelSkin.CMute, 8.5f, true);
                _sp.Text((_conviction * 100.0).ToString("0") + "%", r.Left, r.Top + 34f, r.Width, 24f, SentinelSkin.CInk, 18f, false);
                _sp.Text("size ×" + _sizeMult.ToString("0.00"), r.Left, r.Top + 26f, r.Width, 16f, _sizeMult > 0 ? biasCol : SentinelSkin.CMute, 11f, true, trail);
                _sp.Track(r.Left, r.Top + 60f, r.Width, (float)_conviction, biasCol);

                // voter chips — up to three rows of three (present sensors only; color = their vote)
                float slotW = r.Width / 3f;
                for (int i = 0; i < _votes.Count && i < 9; i++)
                {
                    var v = _votes[i];
                    var vc = v.Dir > 0 ? SentinelSkin.CUp : (v.Dir < 0 ? SentinelSkin.CDown : SentinelSkin.CMute);
                    float cx = r.Left + (i % 3) * slotW;
                    float cy = r.Top + 74f + (i / 3) * 15f;
                    _sp.Text(v.Tag + (v.Dir > 0 ? " ▲" : (v.Dir < 0 ? " ▼" : " ~")), cx, cy, slotW, 14f, vc, 9f, true);
                }

                // footer — tally (+ phase when clear); the VETO gets its OWN line so a long reason can't overlap
                _sp.Divider(r.Left, r.Top + 122f, r.Right);
                _sp.Text("▲" + _agree + "  ▼" + _disagree + "  ·  " + _voters + " voters", r.Left, r.Top + 126f, r.Width, 14f, SentinelSkin.CInk2, 10f);
                if (_vetoed)
                {
                    string vr = _vetoReason ?? "";
                    if (vr.Length > 30) vr = vr.Substring(0, 30) + "…";
                    _sp.Text("VETO: " + vr, r.Left, r.Top + 142f, r.Width, 14f, SentinelSkin.CWarn, 10f, true);
                }
                else
                {
                    if (_clockPhase >= 0)
                        _sp.Text(ClockPhaseName[_clockPhase], r.Left, r.Top + 126f, r.Width, 14f, SentinelSkin.CMute, 10f, true, trail);
                    // signed-conviction sparkline (green above / red below the mid)
                    _sp.Sparkline(r.Left, r.Top + 142f, r.Width, 20f, _convHist, biasCol);
                }

                _sp.End();
            }
            catch (Exception _sx) { SentinelCore.Swallow("Council.OnRender", _sx); }
        }

        #region Properties
        [Range(0.0, double.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Weight — Trend", Description = "Vote weight for SentinelTrend's trailing-line direction.", Order = 2, GroupName = "Weights")]
        public double WeightTrend { get; set; }

        [Range(0.0, double.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Weight — CCI", Description = "Vote weight for WoodiesCCIPro trend bias (×1.5 when strong).", Order = 3, GroupName = "Weights")]
        public double WeightCci { get; set; }

        [Range(0.0, double.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Weight — ADX", Description = "Vote weight for ADXPro regime bias when trend is ON (×1.25 when strong).", Order = 4, GroupName = "Weights")]
        public double WeightAdx { get; set; }

        [Range(0.0, double.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Weight — Envelope", Description = "Vote weight for VolEnvelope trend regime (TrendUp/TrendDown).", Order = 5, GroupName = "Weights")]
        public double WeightEnv { get; set; }

        [Range(0.0, double.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Weight — Brick", Description = "Vote weight for the adaptive brick micro-trend direction.", Order = 6, GroupName = "Weights")]
        public double WeightBrick { get; set; }

        [Range(0.0, double.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Weight — Compression", Description = "Vote weight for CompressionBase's held breakout direction.", Order = 7, GroupName = "Weights")]
        public double WeightComp { get; set; }

        [Range(0.0, double.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Weight — Intermarket", Description = "Vote weight for the Intermarket correlated-instrument lean (macro).", Order = 8, GroupName = "Weights")]
        public double WeightIntermarket { get; set; }

        [Range(0.0, double.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Weight — WAE", Description = "Vote weight for Sentinel WAE's confirmed momentum-explosion breakout direction.", Order = 9, GroupName = "Weights")]
        public double WeightWae { get; set; }

        // [Display]-only (NOT [NinjaScriptProperty]) — added post-hoc so it serializes + shows in F6 WITHOUT changing the
        // generated host-ctor signature (avoids a generated-region regen; see CLAUDE.md "serialize without the region" lesson).
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Weight — God Reversal", Description = "Vote weight for SentinelGodReversal's held reversal direction. A MEAN-REVERSION voice — often counter to the trend voters; best used as a trigger alongside the Council bias.", Order = 10, GroupName = "Weights")]
        public double WeightGodRev { get; set; }

        // [Display]-only (NOT [NinjaScriptProperty]) — region-safe, mirrors WeightGodRev.
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Weight — STF", Description = "Vote weight for SentinelStochasticTripleFilter's Gaussian-Channel midline SLOPE (an independent trend regime). DEFAULT 0 = the exploration primitive: the vote is recorded on every fire but contributes nothing to the fusion until you raise it here (or in Roster.conf). Promote once Lens has graded it.", Order = 11, GroupName = "Weights")]
        public double WeightStf { get; set; }

        // [Display]-only (NOT [NinjaScriptProperty]) — region-safe, mirrors WeightStf/WeightGodRev.
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Weight — FLOW", Description = "Vote weight for SentinelFlow's tick-rule CVD regime (the one axis not derived from price). A STATE voter on the confirmed flow direction.", Order = 12, GroupName = "Weights")]
        public double WeightFlow { get; set; }

        [Range(0.0, double.MaxValue)]
        [Display(Name = "Weight — STRC", Description = "Vote weight for SentinelStructure's swing HH/HL·LH/LL market structure. A STATE voter on the structure direction.", Order = 13, GroupName = "Weights")]
        public double WeightStructure { get; set; }

        [Range(0.0, double.MaxValue)]
        [Display(Name = "Weight — EXH", Description = "Vote weight for SentinelExhaustion's Leledc reversal (held direction). A TRIGGER voter and a mean-reversion voice — often counter to the trend voters.", Order = 14, GroupName = "Weights")]
        public double WeightExhaustion { get; set; }

        [Range(0.0, double.MaxValue)]
        [Display(Name = "Weight — AVMA", Description = "Vote weight for SentinelADXVMA's ADX-volatility adaptive-MA trinary trend (STATE voter, neutral in chop).", Order = 15, GroupName = "Weights")]
        public double WeightAdxvma { get; set; }

        [Range(0.0, double.MaxValue)]
        [Display(Name = "Weight — SPRT", Description = "Vote weight for SentinelSuperTrend's ATR-band trailing-flip trend (STATE voter, always ±1).", Order = 16, GroupName = "Weights")]
        public double WeightSuperTrend { get; set; }

        [Range(0.0, double.MaxValue)]
        [Display(Name = "Weight — PSAR", Description = "Vote weight for SentinelParabolicSAR's Wilder trend/stop (STATE voter, always ±1).", Order = 17, GroupName = "Weights")]
        public double WeightSar { get; set; }

        [Range(0.0, double.MaxValue)]
        [Display(Name = "Weight — ZSC", Description = "Vote weight for SentinelZScore's (Close−SMA)/StdDev mean-reversion (TRIGGER voter, a fade voice).", Order = 18, GroupName = "Weights")]
        public double WeightZScore { get; set; }

        [Range(0.0, double.MaxValue)]
        [Display(Name = "Weight — ARCH", Description = "Vote weight for SentinelTrendArchitect's composite PRISM trend + Trend-Regime-Gate (STATE voter; a rich MFI/CCI/CVD/Hurst/KAMA fusion).", Order = 19, GroupName = "Weights")]
        public double WeightArch { get; set; }

        [Range(0.0, double.MaxValue)]
        [Display(Name = "Weight — VDYA", Description = "Vote weight for SentinelVIDYA's Chande-CMO-modulated adaptive-MA trend (STATE voter).", Order = 20, GroupName = "Weights")]
        public double WeightVidya { get; set; }

        [Range(0.0, double.MaxValue)]
        [Display(Name = "Weight — HARM", Description = "Vote weight for SentinelHarmonic's XABCD pattern completion (TRIGGER voter, a reversal voice).", Order = 21, GroupName = "Weights")]
        public double WeightHarmonic { get; set; }

        // [Display]-only (NOT [NinjaScriptProperty]) — region-safe, mirrors WeightStf/WeightFlow.
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Weight — FLUX", Description = "Vote weight for SentinelFlux's net ORDER-FLOW direction (the imbalance-bar close). A STATE voter and the suite's one order-flow-substrate axis — orthogonal to the price bloc. Published by the SentinelFlux BAR TYPE (load it as the chart's bars type; there is no Flux study to add).", Order = 22, GroupName = "Weights")]
        public double WeightFlux { get; set; }

        // [Display]-only (NOT [NinjaScriptProperty]) — region-safe, mirrors WeightFlux. Keeps it OUT of the generated ctor.
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Weight — CVB", Description = "Vote weight for SentinelDrift's FLOW-CONFIRMED trend direction (bar type id 212204): the brick direction, voted only when the aggregated tape (per-brick signed delta) confirms it, else abstains. A STATE voter, orthogonal — its conviction is order-flow-sourced. Published by the SentinelDrift BAR TYPE (load it as the chart's bars type).", Order = 23, GroupName = "Weights")]
        public double WeightConviction { get; set; }

        // [Display]-only (NOT [NinjaScriptProperty]) — region-safe, mirrors WeightConviction.
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Weight — CVD", Description = "Vote weight for SentinelCVD's SESSION-scale cumulative-delta direction (Core >= v1.43.0). Distinct from FLUX in horizon (Flux's theta is ONE forming bar and resets each close; this is the whole session) and in availability (Flux needs the Flux BARS TYPE; CVD is an indicator, so it works on any bar type). STATE voter. Ships at 0.0 = AUDITION: it is recorded in the vote vector and graded, but cannot move the verdict until measured. Do not raise it by hand -- fit it.", Order = 24, GroupName = "Weights")]
        public double WeightCvd { get; set; }

        // [Display]-only (NOT [NinjaScriptProperty]) — region-safe, mirrors WeightCvd.
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Weight — Buy/Sell Pressure", Description = "Vote weight for BuySellVolumePressureMountain's classified buy-vs-sell dominance (Core >= v1.45.0). STATE voter. Ships at 0.0 = AUDITION: recorded in the vote vector and graded, but cannot move the verdict. It is here because the 2026-07-26 re-test killed all 19 voters and every one was PRICE-derived, while this one classifies TRUE bid/ask volume -- the untested family. The vote ABSTAINS whenever TickBacked is false (historical rebuild falls back to an OHLC proxy). Do not raise it by hand -- fit it.", Order = 25, GroupName = "Weights")]
        public double WeightPressure { get; set; }

        [Range(0.0, 1.0)]
        [Display(Name = "Flux Absorb Damp", Description = "Conviction/size × this when the SentinelFlux tape is ABSORBING against the Council's bias (net order flow one way, bias the other, meaningful imbalance). A soft veto on SIZE — the tape-sourced complement to the LiquidityWalls book veto. Fail-open (no damp) when no Flux bar type is on the chart.", Order = 23, GroupName = "Fusion")]
        public double FluxAbsorbDamp { get; set; }

        [Display(Name = "Veto On Chop", Description = "When SentinelStochasticTripleFilter reports a CHOPPY tape (Choppiness Index above its threshold), veto the verdict — no new entries in a ranging market. Fail-open when the STF sensor isn't ON THIS CHART (v1.11.0). Independent of Weight — STF, so an attached STF at weight 0 still vetoes: that is the deliberate 'regime filter without a directional vote' capability.", Order = 16, GroupName = "Fusion")]
        public bool VetoOnChop { get; set; }

        [Display(Name = "Require sensor on chart", Description = "Chart-derived (OHLC) sensors must be ATTACHED TO THIS CHART to vote, veto or damp. Their seams are shared process-wide by instrument+bar type, so with this OFF an identical sensor on ANOTHER chart feeds this Council — including its vetoes, which are independent of weight. Instrument-level signals (Clock, Intermarket, News, LiquidityWalls, CVD, BSP, Level, Profile) and the bar-type seams (BRK/FLUX/CVB) are never gated. Keep ON.", Order = 17, GroupName = "Fusion")]
        public bool RequireSensorOnChart { get; set; }

        // v1.8.4 — the ON/OFF for the wall veto. WallNearTicks is [Range(0, double.MaxValue)], so there is NO
        // "disable" value: 0 does not mean off, it means "veto when a wall sits exactly AT price" (BlocksEntry
        // tests dist <= ticks), and a negative is REJECTED by NT's property validation at load. The other two
        // hard vetoes each have a bool; this one only had a distance. Now it matches them. [Display]-only =
        // region-safe (mirrors VetoOnChop), so no generated-region churn.
        [Display(Name = "Veto On Wall", Description = "When LiquidityWalls reports an active absorption wall within Wall-Near Ticks of the intended side, veto the verdict. Turn OFF for a measurement/audition lane: a hard veto DELETES the row from the corpus rather than scaling it. Fail-open when the sensor isn't loaded.", Order = 17, GroupName = "Fusion")]
        public bool VetoOnWall { get; set; }

        [Range(0.0, 1.0)]
        [NinjaScriptProperty]
        [Display(Name = "Bias Deadband", Description = "Net vote must exceed this fraction of active weight to pick a side (else FLAT).", Order = 10, GroupName = "Fusion")]
        public double BiasDeadband { get; set; }

        [Range(0.0, 1.0)]
        [NinjaScriptProperty]
        [Display(Name = "Conviction Floor", Description = "Below this conviction, SizeMult = 0 (no actionable edge).", Order = 11, GroupName = "Fusion")]
        public double ConvictionFloor { get; set; }

        [Range(1, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Min Voters", Description = "Fewer fresh voters than this proportionally damps conviction.", Order = 12, GroupName = "Fusion")]
        public int MinVoters { get; set; }

        [Range(0.0, double.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Wall-Near Ticks", Description = "An active absorption wall within this many ticks of the intended side vetoes the verdict.", Order = 13, GroupName = "Fusion")]
        public double WallNearTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Dampen On Squeeze", Description = "Reduce conviction when VolEnvelope reports a squeeze (coiled market — distrust direction).", Order = 14, GroupName = "Fusion")]
        public bool DampenOnSqueeze { get; set; }

        [Range(0.0, double.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Consult Stale (sec)", Description = "Ignore any published sensor state older than this many seconds (0 = never stale).", Order = 15, GroupName = "Fusion")]
        public double StaleSec { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Consult Clock", Description = "Consult SentinelCore.ClockState (the Clock indicator) to modulate conviction by session phase + gate the kill window. Fail-open if absent. Needs SentinelCore ≥ v1.8.0.", Order = 16, GroupName = "Clock")]
        public bool ConsultClock { get; set; }

        [Range(0.0, 1.0)]
        [NinjaScriptProperty]
        [Display(Name = "Midday Damp", Description = "Conviction is multiplied by this during the Midday session phase (chop / drift).", Order = 17, GroupName = "Clock")]
        public double MiddayDamp { get; set; }

        [Range(0.0, 1.0)]
        [NinjaScriptProperty]
        [Display(Name = "Off-Session Damp", Description = "Conviction is multiplied by this when the Clock reports we are out of session.", Order = 18, GroupName = "Clock")]
        public double OffSessionDamp { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Veto Kill Window", Description = "Veto the verdict inside the Clock's near-close kill window (no new entries).", Order = 19, GroupName = "Clock")]
        public bool VetoKillWindow { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Consult Participation", Description = "Consult SentinelCore.ParticipationState (the Participation indicator) to damp conviction on moves not backed by volume. Fail-open if absent. Needs SentinelCore ≥ v1.9.0.", Order = 25, GroupName = "Participation")]
        public bool ConsultParticipation { get; set; }

        [Range(0.0, 1.0)]
        [NinjaScriptProperty]
        [Display(Name = "RVOL Damp Floor", Description = "Conviction × clamp(rvol, this, 1) — the most participation can penalise (e.g. 0.5 = light volume can cut conviction in half at worst; heavy volume never inflates).", Order = 26, GroupName = "Participation")]
        public double RvolDampFloor { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Consult MTF", Description = "Consult SentinelCore.MtfState (the MTF indicator) to damp conviction when the verdict opposes the higher-timeframe consensus. Fail-open if absent. Needs SentinelCore ≥ v1.10.0.", Order = 30, GroupName = "MTF / Location")]
        public bool ConsultMtf { get; set; }

        [Range(0.0, 1.0)]
        [NinjaScriptProperty]
        [Display(Name = "MTF Counter Damp", Description = "Conviction × this when the verdict is AGAINST the MTF consensus (counter-higher-timeframe penalty).", Order = 31, GroupName = "MTF / Location")]
        public double MtfCounterDamp { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Consult Location", Description = "Consult SentinelCore.LevelState (the Location indicator) to damp conviction when a structural level sits in the trade's path. Fail-open if absent. Needs SentinelCore ≥ v1.10.0.", Order = 32, GroupName = "MTF / Location")]
        public bool ConsultLocation { get; set; }

        [Range(0.0, double.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Level-Near ATR", Description = "A structural level within this many ATRs of price, on the trade's path, damps conviction.", Order = 33, GroupName = "MTF / Location")]
        public double LevelNearAtr { get; set; }

        [Range(0.0, 1.0)]
        [NinjaScriptProperty]
        [Display(Name = "Level Damp", Description = "Conviction × this when a structural level lies in the trade's path (running into memory).", Order = 34, GroupName = "MTF / Location")]
        public double LevelDamp { get; set; }

        // Profile / Regime modulators — [Display]-only (region-safe; serialize + show in F6 without a constructor param).
        [Display(Name = "Consult Profile", Description = "Consult SentinelCore.ProfileState (the Profile axis) to damp conviction when price is accepted inside the value area (chop). Fail-open if absent. Needs SentinelCore ≥ v1.26.0.", Order = 40, GroupName = "Profile / Regime")]
        public bool ConsultProfile { get; set; }

        [Range(0.0, 1.0)]
        [Display(Name = "In-Value Damp", Description = "Conviction × this when price is accepted inside the volume-profile value area (mean-reversion / chop context).", Order = 41, GroupName = "Profile / Regime")]
        public double InValueDamp { get; set; }

        [Display(Name = "Consult Regime", Description = "Consult SentinelCore.RegimeState (the Regime sensor) to damp conviction in the high-volatility regime. Fail-open if absent. Needs SentinelCore ≥ v1.26.0.", Order = 42, GroupName = "Profile / Regime")]
        public bool ConsultRegime { get; set; }

        [Range(0.0, 1.0)]
        [Display(Name = "High-Vol Regime Damp", Description = "Conviction × this in the high-volatility (chaotic) K-means regime, where directional edges decay.", Order = 43, GroupName = "Profile / Regime")]
        public double HighVolRegimeDamp { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Publish Verdict to Sentinel", Description = "Publish the fused verdict as SentinelCore.CouncilState so strategies/Bridge/Deck can consult it. Needs SentinelCore ≥ v1.7.0.", Order = 20, GroupName = "Sentinel")]
        public bool PublishState { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Log Verdict Changes", Description = "Write bias flips + veto toggles to sentinel.log (no per-tick spam).", Order = 21, GroupName = "Sentinel")]
        public bool LogChanges { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Card", Order = 22, GroupName = "Sentinel")]
        public bool ShowCard { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Card Corner", Description = "Which chart corner the Sentinel card docks to. Cards in the same corner auto-stack.", Order = 23, GroupName = "Sentinel")]
        public SentinelCardCorner CardCorner { get; set; }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> BiasPlot { get { return Values[0]; } }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> ConvictionPlot { get { return Values[1]; } }

        [NinjaScriptProperty]
        [Display(Name = "Show indicator label", Description = "Show NinjaTrader's chart name label. Sentinel default = OFF (clean chart); turn on to restore it.", GroupName = "Sentinel", Order = 100)]
        public bool ShowIndicatorLabel { get; set; }

        // v1.7.x — NOT [NinjaScriptProperty] (no codegen churn): serializes to the workspace + shows in F6 (Deck pattern).
        [Display(Name = "Scope Lane", Description = "Per-chart lane folded into the scope (GC.212202v6x24@<lane>). BLANK = bare scope (default). Set a DISTINCT value (e.g. A / B) on each chart when running two charts with the SAME instrument + bar type + size, so their Council + sensors get separate scopes instead of clobbering each other. Letters/digits only.", GroupName = "0. Scope Lane", Order = 0)]
        public string ScopeLane { get; set; }
        #endregion
    }
}
