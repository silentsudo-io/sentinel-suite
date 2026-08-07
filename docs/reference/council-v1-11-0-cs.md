# Council_v1_11_0.cs

> `bin/Custom/Indicators/Council_v1_11_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.11.0 |
| **Size** | 2067 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `Council_v1_11_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel` |
| **Publishes seams** | `CouncilState` |
| **Consumes seams** | `AdxState`, `AdxvmaState`, `BrickState`, `CciState`, `ClockState`, `CompressionState`, `ConvictionState`, `CvdState`, `EnvelopeState`, `ExhaustionState`, `FlowState`, `FluxState`, `GodReversalState`, `HarmonicState`, `IntermarketState`, `LevelState`, `LiquidityState`, `MtfState`, `ParticipationState`, `PressureState`, `ProfileState`, `RegimeState`, `SarState`, `StfState`, `StructureState`, `SuperTrendState`, `TrendState`, `TrendArchitectState`, `VidyaState`, `WaeState`, `ZScoreState` |
| **Documented by** | [SENTINEL_ML_SPEC](../../SENTINEL_ML_SPEC.md), [SENTINEL_THESIS](../../SENTINEL_THESIS.md) |
| **Depends on this** | [SentinelBridge_v0_2_0.cs](sentinelbridge-v0-2-0-cs.md), [SentinelCockpit_v0_1_0.cs](sentinelcockpit-v0-1-0-cs.md), [SentinelExcursionRecorder_v2_0_0.cs](sentinelexcursionrecorder-v2-0-0-cs.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 Council — the Sentinel CONFLUENCE ARBITER ("the brain")                  |   Version v1.11.0
 File: Council_v1_11_0.cs   |   namespace …Indicators.Sentinel   |   display Name "Council"

 ⚠ NO ORDERS — read-only advisory indicator. It DECIDES, it never trades. Safe to run anywhere.

 WHAT THIS IS — the missing brain of the suite. Every Sentinel sensor already PUBLISHES its own
 opinion to SentinelCore as a "…State" seam (Trend, ADX, CCI, VolEnvelope, Liquidity, Brick) plus
 the Eye's qualification verdict. Until now each consumer hand-consulted one or two of those ad hoc.
 The Council FUSES them all into ONE explainable per-instrument verdict and publishes it back as
 SentinelCore.CouncilState so ANY consumer (GTrader21 / Bridge / Deck / Copier / strategies) reads
 the SAME decision instead of re-deriving confluence.

 THE VERDICT (SentinelCore.CouncilState, SentinelCore ≥ v1.7.0):
   • Bias        -1/0/+1   the fused direction (0 = no edge / vetoed)
   • Conviction  0..1      how ALIGNED the DECLARED voters are (1 = every declared voter unanimous; 0 = split /
                           none / vetoed). ⚠ v1.1.0: the denominator is the DECLARED weight, not the weight that
                           happened to show up — a MISSING or NEUTRAL voter now DILUTES conviction instead of
                           vanishing from the denominator. A verdict fused from 2 of 10 voters can no longer
                           read as near-unanimity.
   • SizeMult    0..1      suggested size multiplier = Conviction × contextMult. 0 when vetoed, when Bias is 0,
                           or when Conviction < ConvictionFloor. ⚠ v1.2.0: ALL context damping (Clock · MTF ·
                           Participation · Location · squeeze · breadth) lives HERE, not in Conviction. The floor
                           gates on AGREEMENT; a poor context makes the trade SMALLER, never silently absent.
   • Agree/Disagree/Voters the tally, and a compact Reasons string — the AUDIT of WHY it decided

 HOW IT FUSES (weighted vote — the weights ARE the edge; tune them, then let Lens grade them):
   Each sensor with a FRESH reading casts a signed vote (+1/-1) with a weight; a stale/absent sensor
   simply ABSTAINS (fail-open, matching the suite). netScore = Σ(vote × weight). Bias = sign(netScore)
   past a deadband; Conviction = |netScore| / denomW, where denomW (v1.3.0) is KIND-AWARE: STATE voters
   always count toward it (neutral is a real reading), a TRIGGER counts only when it fired or is absent —
   a quiet trigger is absence of evidence, not evidence against. Breadth/squeeze/context damp the SIZE,
   not the agreement (v1.2.0).
   ⚠ These price-derived sensors are NOT independent — they largely echo the same OHLC. Conviction is
   "agreement," which is not the same as "confirmation." The verdict gets genuinely smarter only as the
   ORTHOGONAL axes land (Clock/Location/Participation/MTF/Internals/Event — see Docs/ROADMAP.md).

 HARD VETOES (account-free; each zeroes conviction + names itself in VetoReason):
   global kill · scoped (per-root) kill · rollover block · news lockout · an absorption WALL blocking
   the intended side (LiquidityState.BlocksEntry). These mirror what CanEnter bundles so the published
   verdict never reads "LONG 0.9" while news lockout is live. The Council is ADVISORY — a consuming
   strategy STILL calls its own SentinelCore.GateEntry at submit; the veto here shapes the ADVICE.

 SENTINEL WIRING (Docs/SENTINEL_DESIGN_SYSTEM.md):
   • PUBLISH: SetCouncilState(...) each update (default ON — the Council exists to publish).
     v1.15.0 SCOPE KEYING (in-place, exec-plan 1.2): publishes under a SCOPE ("GC.69697v6" =
     instrument x bartype), NOT a bare instrument, so two GC charts on different bar types no longer
     overwrite each other's verdict on every tick. Also stamps BarTimeUtc + IsHistorical — UpdatedUtc
     is wall-clock even during historical replay, so IsHistorical is the ONLY way a consumer can tell
     a replayed verdict from a live one. The fusion math is untouched.
   • PLOTS: hidden transparent "Bias" (±1) + "Conviction" (0..1) so Deck SIGNAL ARM / strategies can
     read the verdict as a generic PLOT (the suite signal-exposure convention), no drawing-scrape.
   • A SentinelSkin.Painter glass card (CardLayout-docked) + Sentinel palette + label remover.
   • Records verdict CHANGES to sentinel.log ("Council"); the per-FIRE record belongs to the strategy
     (write CouncilState into the Ledger/Log ctx at fire time so Lens can grade the confluence).

 CHANGELOG
   v1.11.0 (REAL FORK, 2026-07-29) — ⭐ A CHART-DERIVED SENSOR MUST BE ON THIS CHART TO INFLUENCE IT.
            THE BUG, as the user put it: "a standard OHLC sensor like Stochastic Triple Filter is voting on
            Council without being on the chart. It is not weighted and poisoning the Council."
            Sensor seams are process-global and keyed BARE (instrument + bar type). That is CORRECT for a value
            that does not vary with the consuming chart — but an OHLC sensor's reading is computed from ITS OWN
            chart's bars, so an STF on any other GC/TBars chart was feeding EVERY Council on that instrument+
            bartype. And the bite was not the vote: `VetoOnChop` hangs off `stf != null` and is INDEPENDENT of
            WeightStf, which defaults 0 — so a sensor you never attached, at weight zero, could zero your
            conviction and your sizeMult. Stated intent had always been narrower: the v1.3.1 changelog says the
            veto is live "only when the STF sensor is loaded", meaning *on this chart*; the code meant *anywhere*.
            ⚠ AUDIT FIRST — IT WAS NEVER AN STF BUG. Every weight-independent path was checked, and the class is
            SIX: VetoOnChop (STF) · VetoOnWall (LiquidityWalls) · VetoKillWindow (Clock) · DampenOnSqueeze
            (VolEnvelope) · FluxAbsorbDamp (Flux) · the whole contextMult chain (Clock/MTF/Participation/
            Location/Profile/Regime). Gating the veto on the weight — the obvious one-line fix — would have
            repaired ONE ROW of that table and made the rest look deliberate.
            THE GATE IS CLASSIFICATION, NOT PRESENCE-FOR-EVERYTHING (the user's distinction, and it is the right
            one — "sure there is a use case for having a voter influence the Council and not be on that specific
            chart, but indicators like STF should not be allowed to do it"):
              • CHART-DERIVED (20: TRND CCI ADX ENV CMP WAE GREV STF STRC EXH AVMA SPRT PSAR ZSC VDYA HARM ARCH
                RGME PARTIC MTF) → must be ATTACHED to this chart. No attach ⇒ no vote, no veto, no damp.
              • INSTRUMENT-LEVEL (Clock · Intermarket · News · LiquidityWalls · CVD · BSP · Level · Profile) →
                never gated. They have no chart of their own; requiring attachment would be meaningless.
              • BAR-TYPE SEAMS (BRK / FLUX / CVB) → exempt. The chart IS that bars type.
            ⭐ IMPLEMENTATION: the gate wraps the SEAM READ (`Local("STF", GetStfState(...))`), not each
            modulator. The vote, the veto and the damp all hang off the same `!= null`, so nulling the read
            closes all three — one mechanism for the whole class, which is exactly what the audit demanded.
            Presence is scanned on the UI THREAD (ChartControl.Indicators off the data thread throws — memory
            nt-consume-indicator-plots), async, 5 s throttle, matched by type-name PREFIX so a sensor version
            bump does not silently un-gate it. ⚠ "SentinelTrend" needs its trailing underscore or it also
            prefix-matches SentinelTrendArchitect.
            FAIL DIRECTIONS, both deliberate: no ChartControl at all (headless / offline harness) ⇒ attachment
            is UNDEFINED, not false ⇒ fail OPEN, because muting a Council that has no chart to be attached to is
            the "crashed sensor is indistinguishable from a quiet one" failure — and it keeps the replay==live
            parity gate intact. Scan not yet completed ⇒ fail CLOSED for a beat, so an off-chart sensor cannot
            sneak one bar in; the scan lands within a bar and the skip is NAMED.
            OBSERVABILITY: everything gated out is listed in the Reasons audit as `off-chart:STF,ENV` — without
            it a declared-but-unattached voter looks identical to an attached one that is abstaining, and "why
            didn't it fire" is unanswerable. New [Display]-only "Require sensor on chart" (default ON).
            ⚠ REAL FORK, not in-place: file + class + Name + header all move to v1.11.0 together. This also ends
            a standing lie — the file said Council_v1_0_0 and the card said "Sentinel Council v1.0.0" while the
            code had been v1.10.0 since 2026-07-24. Council_v1_0_0.cs stays FROZEN as the fallback.
            ⚠⚠ REMOVE the old Council from a chart BEFORE adding v1.11.0. Both publish CouncilState on the same
            scope, so running them together is a SCOPE CONTENTION (last writer wins, alternating per bar).
   v1.10.0 (in-place, 2026-07-24) — DECOUPLED vs ABSENT (Core >= v1.40.0 beacon). A missing BAR-TYPE voter
            (BRK/FLUX/CVB) now asks SentinelCore.BeaconForeign whether another ASSEMBLY GENERATION is still
            publishing it. If it is, the sensor is not absent — it is DECOUPLED, and the fix is an NT RESTART.
            WHY: an F5 rebuilds every indicator but NOT a chart's bars-type instance, so the surviving bars
            type publishes into the OLD assembly's static seam store while this rebuilt Council reads the NEW
            one. The write succeeds into a store nothing reads: guards pass, scope resolves, nothing throws,
            and BRK/FLUX/CVB simply never appear. That silently produced the 2026-07-23 audition corpus —
            1,866 rows, ZERO bar-type voters, brkUpper/brkLower all 0 — and survived a chart reload, which
            is why "reload the chart" was twice believed to be the fix and twice wrong (measured 07-24).
            Reported via Conditions (debounce/re-state/auto-clear) and evaluated OUTSIDE the roster-change
            gate, because a latch that never re-arms is indistinguishable from a detector that never fires.
   v1.9.0 (in-place, 2026-07-23) — LIVE CONFIG RELOAD + Lane.conf CASCADE (Core >= v1.39.0). Roster.conf and
            Lane.conf are now POLLED on write time (>=2s, same idiom SentinelSkin uses for cards.off/theme.txt)
            and re-applied on the next bar, so changing a floor / veto / voter weight needs NO F5 and NO chart
            reload. Both are re-applied together (a lane profile writes over F6 properties, so a roster-only
            change must not leave a half-applied state) and the winning paths are LOGGED. Core's LaneIO also
            gained the scope-instrument-global cascade RosterIO always had: a chart on a bar type nobody had
            used before used to find no Lane.conf, silently keep its F6 floor and record almost nothing --
            indistinguishable from every sensor being dead. That asymmetry is what made 'new chart, pick a bar
            type, load the template, run' impossible. One Models\<INST>\Lane.conf now covers every bar type.
   v1.8.4 (in-place, 2026-07-23) — VetoOnWall: an explicit ON/OFF for the liquidity-wall veto + the
            `vetoonwall` Lane.conf key. v1.8.3 tried to disable that veto from the lane with
            `wallnearticks = -1`; NT REJECTED IT AT LOAD -- WallNearTicks is [Range(0, double.MaxValue)],
            so the chart threw "Value of property 'WallNearTicks' ... is -1 and not in valid range".
            And 0 is not off either: BlocksEntry tests `dist <= ticks`, so 0 vetoes whenever a wall sits
            exactly AT price. A distance knob simply HAS no disable value -- the other two hard vetoes
            (VetoOnChop / VetoKillWindow) each carry a bool and this one did not. Now it matches them.
            LESSON: check a property's Range/validation attributes before choosing a sentinel value; a
            value that is meaningful in the comparison can still be illegal at the property boundary.
   v1.8.3 (in-place, 2026-07-23) — HARD VETOES ARE LANE-SETTABLE. ApplyLaneProfile now also reads
            `vetoonchop` / `vetokillwindow` / `wallnearticks` / `minvoters` from Lane.conf (same sparse
            "absent ⇒ inherit F6" contract as every other key; MinVoters via TryDouble→(int) so SentinelCore
            needs no new API). WHY: a modulator DAMP scales a recorded verdict, but a HARD VETO deletes it —
            SentinelExcursionRecorder's gate is `aligned = Bias!=0 && (RecordBelowFloor ? !v.Vetoed : HasEdge)`,
            so a vetoed bar never enters the corpus. With VetoOnChop on, the 22-voter sensor AUDITION was
            silently dropping every chop-regime bar — it would have graded the arsenal on trend-only data and
            reported nothing wrong. Lane.conf being sparse by design covered the damps but not the vetoes.
            Found by reading a live roster line on legacy-node (a load burst, every verdict "VETO:chop 66") rather
            than by reasoning about the config. Also bumps CouncilVer 1.8.1→1.8.3: the const had never been
            bumped for v1.8.2, so recorded rows were stamped cnclVer "1.8.1" and the corpus provenance lied.
   v1.8.2 (in-place, 2026-07-20) — new CVB voter (STATE, orthogonal/order-flow). Consumes SentinelCore.ConvictionState
            (v1.37.0) — SentinelDrift's (bar type id 212204) FLOW-CONFIRMED trend direction: the brick direction voted
            only when the aggregated tape confirms it. Bar-type seam → BARE scope. KnownVoters + AddVote + BaseWeight +
            WeightConviction ([Display]-only, region-safe) + SetDefaults + Reasons (`cvb▲/▼`). No fusion/damp change.
   v1.8.1 (in-place, 2026-07-18) — FUSION-CORE REWIRE. The inline fuse block (kind-aware denomW · deadband→bias ·
            conviction · the full context-damp chain: breadth·squeeze·clock·participation·MTF·location·profile·
            regime·flux-absorb) now DELEGATES to AddOns/CouncilFusion.Fuse — the single pure fusion truth shared
            with the (coming) replay HARNESS, so a replay-baked verdict == the live verdict on that bar (the
            correctness gate, Docs/SENTINEL_REPLAY_SPEC.md §4-5). BEHAVIOUR-PRESERVING: identical math, verified
            bit-for-bit (AddVote accumulation ≡ Fuse's recompute; _declaredW ≡ Fuse's denom; damp chain identical;
            squeeze damp = 0.6 as before). Modulator seams now read UNCONDITIONALLY (Fuse owns the bias-gating);
            the bias-dependent liquidity-WALL veto is resolved AFTER Fuse (zeroing size+conviction, bit-identical
            to Vetoed=true). CouncilFusion.cs brought from v1.3.x → v1.8.0 parity (added Profile/Regime/Flux-absorb
            + their damps). VERIFY UNCHANGED via a Market-Replay verdict-diff before trusting a replay bake.
   v1.8.0 (in-place, 2026-07-14) — PER-LANE SYSTEM PROFILE (Core ≥ v1.33.0). On load, applies the lane's
            Sentinel\Models\<inst>\<bartag>@<lane>\Lane.conf OVER the F6 fusion knobs (ConvictionFloor, bias
            deadband, the 6 context-consult toggles, the modulator damps) — SPARSE: only keys present override,
            absent keys inherit F6. So an A/B test LANE pins its own decision knobs (not just its roster) without
            hand-editing F6 per chart. Roster.conf = voters+weights+kind; Lane.conf = the rest. (System Builder §14.7.)
   v1.7.0 (in-place, 2026-07-14) — PER-CHART LANE (Core ≥ v1.32.0). New "Scope Lane" property: set a distinct
            value (A/B) on each of two charts that share instrument+bartype+size so their scopes diverge
            (GC.212202v6x24@A / @B) instead of clobbering — fixes SCOPE CONTENTION for same-bartype test charts.
            Registers the lane (keyed by ChartControl) in DataLoaded so every SENSOR on the chart inherits it;
            publishes CouncilState + reads sensor seams on the LANED scope, but reads BAR-TYPE seams (BRK/FLUX)
            on the BARE scope (a bars series is shared across charts). Blank lane = bare scope (back-compat).
            ⚠ Needs the sensor batch (each sensor → ScopeOf(…,ChartControl)) for a laned chart to see its voters.
   v1.6.3 (in-place, 2026-07-14) — FLUX voter + absorption modulator (Core ≥ v1.31.0). FLUX = SentinelFlux's net
            ORDER-FLOW direction from the imbalance-driven bar close (a STATE voter, w0.7) — the suite's one
            order-flow-SUBSTRATE axis: the whole chart clock is flow-synchronized, so it is orthogonal to the
            price bloc (TRND/CCI/ADX/ENV all echo the OHLC). Its flow-vs-price DIVERGENCE drives a soft SIZE damp
            (FluxAbsorbDamp, default 0.6) when the tape absorbs against the bias — the tape-sourced complement to
            the LiquidityWalls book veto. KnownVoters + BaseWeight + AddVote + WeightFlux/FluxAbsorbDamp props +
            audit (flux▲/▼/absorb). DefaultKind default already State. Council now fuses 22 voters. Additive only.
   v1.6.2 (in-place, 2026-07-12) — TWO NEW VOTERS (candidate-library novel-signals pass; Core ≥ v1.30.0). VDYA
            (SentinelVIDYA Chande-CMO adaptive-MA trend — STATE, w0.5) · HARM (SentinelHarmonic XABCD pattern
            completion — TRIGGER, w0.4). KnownVoters + BaseWeight + DefaultKind(HARM=Trigger) + AddVote +
            weight properties. Council now fuses 21 voters. Additive only.
   v1.6.1 (in-place, 2026-07-12) — ARCH voter (SentinelTrendArchitect, the MPL Pine port; Core ≥ v1.29.0). Its
            composite PRISM trend + Trend-Regime-Gate publishes TrendArchitectState → a STATE voter (w0.7).
            KnownVoters + BaseWeight + AddVote + weight property. Council now fuses 19 voters. Additive only.
   v1.6.0 (in-place, 2026-07-12) — FOUR NEW VOTERS (candidate-library Tier-2 pass; Core ≥ v1.28.0). AVMA
            (SentinelADXVMA adaptive-MA trend — STATE, w0.6) · SPRT (SentinelSuperTrend ATR trailing flip —
            STATE, w0.7) · PSAR (SentinelParabolicSAR Wilder trend/stop — STATE, w0.5) · ZSC (SentinelZScore
            mean-reversion — TRIGGER, w0.4). New KnownVoters + BaseWeight + DefaultKind(ZSC=Trigger) + AddVote +
            weight properties ([Display]-only). Council now fuses 18 voters + 8 modulators/vetoes. Additive only.
   v1.5.1 (in-place, 2026-07-12) — ROSTER I/O EXTRACTED to SentinelCore.RosterIO (Core ≥ v1.27.0; no
            behaviour change). LoadRoster now delegates the file cascade + parse to RosterIO.Read (same
            scope▸instrument▸global cascade, same grammar, same first-wins dedup); the private ParseRoster
            was removed. The DEFAULT declaration (KnownVoters where BaseWeight>0) and all fusion math are
            UNCHANGED. Purpose: ONE format owner shared with the System Builder (writer) so reader/writer
            can never drift — the substrate for the roster-editor UI (Docs/SENTINEL_SYSTEM_BUILDER_SPEC.md).
   v1.5.0 (in-place, 2026-07-12) — THREE NEW VOTERS + TWO NEW MODULATORS (the installed-tree port harvest;
            Core ≥ v1.26.0). VOTERS: FLOW (SentinelFlow tick-rule CVD regime — STATE, the one non-price-echo
            axis, w 0.9) · STRC (SentinelStructure swing HH/HL·LH/LL — STATE, w 0.7) · EXH (SentinelExhaustion
            Leledc reversal — TRIGGER/mean-reversion, w 0.5). MODULATORS (size, not agreement): Profile
            (price accepted inside the value area = chop → InValueDamp) · Regime (high-volatility K-means regime
            → HighVolRegimeDamp). New KnownVoters + BaseWeight + KindFor(EXH=Trigger) + Reasons audit
            (in-value · hi-vol · ±flowDiv). Weights + consult toggles are [Display]-only (region-safe). The
            Council now fuses 14 voters + 8 modulators/vetoes. Backward-compatible (added members only).
   v1.4.0 (in-place, 2026-07-11) — PUBLISH THE DECISION VECTOR (ML spec §2.1/§2.2; Core ≥ v1.24.0). The publish
            now carries the machine-rea
```

