// ─────────────────────────────────────────────────────────────────────────────
// This Source Code Form is subject to the terms of the Mozilla Public License,
// v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
// one at https://mozilla.org/MPL/2.0/.
//
// Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
// ─────────────────────────────────────────────────────────────────────────────
// ═════════════════════════════════════════════════════════════════════════════
//  SentinelBridge — the automated Council-consumer + on-chart control surface (Sentinel Suite, NT8)
//  File: SentinelBridge_v0_2_0.cs   ·   Version v0.3.1   ·   namespace …Strategies (BASE — sub-ns hides strategies)
// ─────────────────────────────────────────────────────────────────────────────
//  WHAT  (see Docs/BRIDGE_SPEC.md — Phase 2, the control surface)
//    The autopilot counterpart to the manual Deck. Trades the fused SentinelCore.CouncilState verdict
//    (Council-as-signal) through the authoritative GateEntry (fail-CLOSED), managed bracket, and RECORDS
//    the verdict on every fire so Lens can grade the weights. v0.2.0 adds the ON-CHART CONTROL SURFACE:
//    a Sentinel glass card (live verdict + strategy state) with a clickable **ARM BRIDGE** button.
//
//    SAFETY — automation is OFF on load and can ONLY be armed by a deliberate CLICK on the card's ARM
//    BRIDGE button (never silently auto-arm — the Deck lesson; the armed flag is runtime-only, never
//    persisted). DISARMED = the card shows the live verdict but the engine places NO orders (behaves like
//    the headless v0.1.0). ARMED = it fires per the P1 logic. No chart (headless) = no button = stays
//    disarmed = safe no-trade (this is a CHART trader by design).
//
//    Engine (unchanged from v0.1.0): edge-detected bias flip into an aligned edge (one-shot per verdict
//    episode, flat-only), size baseQty × SizeMult, GateEntry (enter only on IsClear), managed bracket
//    (SetStopLoss/SetProfitTarget in ticks), record verdict (Ledger.Order + "bridge-fire" Action).
//    v0.2.0 adds an optional Council-flip EXIT (close if the Council stops supporting the open side).
//    TP/SL hand-set from the COUNCIL ◆ in the Excursion tab. MANAGED (never close a managed position with
//    raw orders — ExitLong/ExitShort is the managed-safe close).
//
//  CHANGELOG
//    v0.3.1 (in-place, INSTRUMENTATION 2026-07-28) — THE FILL-COST REFERENCE PRICE. Measurement only: the order
//             path, sizing, gate, bracket and Helm logic are byte-for-byte v0.3.0. Fixes the reason Question 1
//             ("what does a fill actually cost us?") could not be answered from the corpus we already own.
//             TWO defects, both of which recorded a number that LOOKED like a measurement and was not:
//               (1) `OnExecutionUpdate` set `intended = price` for a MARKET order, so `slip = fill − fill = 0`
//                   IDENTICALLY. All 39 market entries in the Ledger read slip 0 — an artifact of this line, not
//                   an observation of the market. Entry crossing cost had never once been measured.
//               (2) `RecordFire` logged `Close[0]` as the order's decision price. On a Heikin-Ashi bar type
//                   (SentinelTBars, which is what the Bridge runs on) Close[0] is the (O+H+L+C)/4 AVERAGE — a
//                   price that NEVER TRADED. Joining order→fill over the 39 live entries showed the signature
//                   exactly: longs "filled" a median +11.0 ticks above and shorts −11.0 ticks below the recorded
//                   price, symmetric by direction. Same defect, same magnitude as the excursion recorder's
//                   "9-tick bleed" (memory firepx-is-synthetic-ha-close), fixed there at schema 1.5 and still
//                   live here. So BOTH the fill row and the order row were unusable, for two different reasons.
//             FIX: capture the TRADEABLE quote on the side being crossed (buy → ASK, sell → BID) at the moment
//             of SUBMISSION, at all five market-order sites (entry · council-flip exit · Helm flatten · Helm
//             scale-down ×2), and use it as the reference for both the Ledger order row and `intended`.
//             Stop/limit exits are UNCHANGED — their `intended` was already the real trigger/limit price and is
//             the one honest execution number the Ledger has ever held (prop accounts: median +1.0 tick adverse).
//             Fails soft: no quote ⇒ 0 ⇒ falls back to the v0.3.0 behaviour rather than record an indefensible
//             number. Historical/replay returns the bar close, so the measurement is meaningful REALTIME only.
//             ⚠ FORWARD-MEASURING ONLY — this recovers nothing about past fills; those 39 entries stay unusable.
//             ⚠ KNOWN, DELIBERATELY NOT CHANGED: the `_liveStopPrice`/`_liveTargetPrice` seeds in TryEnter still
//             use Close[0] and so inherit the same HA offset. They are transient (recomputed exactly from
//             Position.AveragePrice once filled) and feed only the card + Helm's tighten/widen classify, so
//             correcting them would alter interdiction behaviour — out of scope for an instrumentation bump.
//    (in-place, 2026-07-25) — RECORDED CATCHES: 28 empty `catch {}` migrated to SentinelCore.Swallow
//             (Core >= v1.41.0). Runtime behaviour is IDENTICAL — Swallow never rethrows — but a fault in
//             the order path is now counted, rate-limited and logged instead of vanishing. Silent catches
//             are the proven mechanism of every expensive bug in this suite; see Docs/SENTINEL_ADVERSARIAL_REVIEW.md §2.
//    v0.3.0 (in-place, 2026-07-15) — HELM INTERDICTION CONSUMER (Phase 5; SentinelCore ≥ v1.34.0; memory
//             helm-interdiction-layer). The Bridge is now the FIRST owner that obeys Helm intents — a human can grab
//             the wheel of this running autopilot WITHOUT stopping it. It drains SentinelCore.TakeHelmIntent(InstanceKey())
//             on every tick (new OnMarketData, realtime) + a bar-close backstop, executes the intent with its OWN order
//             handles (never a raw order), publishes HelmState back so a Helm surface renders reality, and writes EVERY
//             intent to the Ledger ("helm-intent" Action, stamped with the Council EpisodeId + instanceKey) + marks the
//             episode HumanOverride so the Lab can exclude/model interdicted trades (recording the human keeps the model
//             honest). ASYMMETRIC GATE: risk-REDUCING verbs (FlattenNow/Pause/SkipNext/BreakevenNow/tighten-stop/
//             scale-down) are fail-OPEN; risk-ADDING (Resume/widen-stop/HandBack) validate through GateEntry fail-CLOSED.
//             MANAGED-MODE HONESTY: Scale-UP is REFUSED (single-entry managed can't scale-in without desync — use the
//             Deck); TakeOver/HandBack map to stand-down/resume (a managed position can't transfer order ownership
//             without disable/re-enable). New [Display]-only "Obey Helm intents" toggle (default ON). No ctor churn, no
//             change to the entry engine when no intent is pending ⇒ byte-for-byte the v0.2.4 trade path. Keeps the
//             v0_2_0 class/file identity (no serialization break for the live chart instance).
//    v0.2.4 (in-place, 2026-07-14) — PER-CHART LANE (Core ≥ v1.32.0). New [Display]-only "Scope Lane" param: set it to
//             match the "Scope Lane" on THIS chart's Council so the Bridge reads the correct lane's CouncilState
//             (via ScopeOfLane — a strategy has no shared ChartControl, so it targets the lane explicitly). Blank =
//             bare scope (back-compat). Lets the Bridge auto-trade one of two same-bartype A/B test lanes without
//             consuming the other's brain. No ctor churn (not [NinjaScriptProperty]).
//    v0.2.3 (in-place, IDENTITY 2026-07-11) — ACTOR IDENTITY + EPISODE JOIN (ML spec §10; SentinelCore ≥ v1.25.0).
//             (1) Derived `InstanceKey()` = "SentinelBridge#<scope>@<account>". (2) The ARM button is now an INTERLOCK:
//             it `RegisterActor`s the key and REFUSES to arm on a collision ("NAME TAKEN") — two live Bridges on one
//             scope+account is the managed-position desync hazard, so the ambiguous config is blocked, not merely
//             warned. Released (reference-checked) on disarm + Terminated. (3) Every fire now stamps the Council's
//             `EpisodeId` + the instanceKey onto the Ledger Order/Action/Fill, so Lens can join a FILL → its EPISODE →
//             the verdict that caused it. Behaviour of the trade path itself is UNCHANGED (sizing/gate/bracket as v0.2.2).
//    v0.2.2 (in-place, SAFETY 2026-07-10) — SIZING ROUTES THROUGH `SentinelCore.SizedQuantity()`. The Bridge stopped
//             at `Math.Max(1, baseQty × SizeMult)` and never called it, so THREE things were silently ignored:
//               • the account profile's `size=` (SizeScale) from Profiles.conf;
//               • the governor's `RecommendedSize()` — a governor telling this strategy to size DOWN was NOT OBEYED;
//               • `ContractLimit`, which hard-BLOCKED every entry when BaseContracts exceeded it, instead of clamping.
//             Now: baseQty → × SizeMult → SizedQuantity (SizeScale × RecommendedSize, clamped to ContractLimit,
//             never < 1) → GateEntry(riskDollars=0) VALIDATES. That is the order SentinelCore's own header
//             prescribes — "SizedQuantity is the one place sizing math lives." A resize is logged.
//             ⚠ RESOLUTION CAVEAT, now documented at the call site: with BaseContracts = 1 the Council's SizeMult
//             CANNOT scale a position down — 1 × 0.19 rounds to 0 and the Max(1,…) floor restores a 1-lot, because
//             a fifth of a contract does not exist. SizeMult only has resolution at BaseContracts ≥ 2 (≥ 4 to
//             resolve its typical 0.2–1.0 range). ConvictionFloor (SizeMult = 0) is what expresses "do not trade";
//             SizeMult is not a substitute for it. Pair this with Council v1.2.1's floor of 0.20.
//    v0.2.1 (in-place, SAFETY 2026-07-09) — the card is now `pinned: true` in CardLayout.Place. It carries the
//             ARM BRIDGE button (`_armBtn` is hit-tested against this rect), so if a crowded column overflowed and
//             the card were hidden, the ONLY on-chart arm/disarm control for an order-placing strategy would
//             silently vanish. CardLayout never hides a pinned card. No order-logic change.
//    v0.2.1 (in-place, COSMETIC 2026-07-09) — LABEL REMOVER. NT drew this strategy's top-left chart label from
//             Name, dumping the whole parameter list over the chart. Adopted the suite's indicator standard:
//             new "Show strategy label" toggle (default OFF) blanks Name in State.DataLoaded. Order routing is
//             UNAFFECTED — order tags come from the separate `_tag` const, never from Name. The toggle is
//             [Display]-only (not [NinjaScriptProperty]) so the generated-region ctor signature is unchanged and
//             saved workspaces keep loading. ⚠ If the Control Center Strategies grid ever shows a blank row,
//             flip it ON — you must always be able to find and disable a running strategy.
//    v0.2.1 (in-place, additive 2026-07-08) — GOD REVERSAL ENTRY TRIGGER (opt-in, default OFF; the Council-bias ×
//             reversal-trigger loop from the God Reversal doctrine §7). When UseGodReversalTrigger is ON, an entry
//             also requires a FRESH SentinelCore.GodReversalState (≤ GrevMaxAgeSec) whose held Dir is ALIGNED with
//             the Council bias and whose Quality ≥ GrevMinQuality — the Council supplies bias/edge/size, the reversal
//             supplies the entry TIMING (the doctrine's "reversal at a predictable place"). The trigger read (dir/
//             setup/quality) is appended to the Ledger fire record + sentinel.log so Lens can grade the reversal
//             weight, and shows on the card when armed+flat. Default OFF ⇒ byte-for-byte the v0.2.0 engine. Keeps
//             the v0_2_0 class/file identity (no serialization break for the live chart instance) — Council-GREV
//             in-place-additive precedent. Needs Sentinel God Reversal on the chart + SentinelCore ≥ v1.14.0.
//             Also (observability): the fire record now carries the BRACKET (tp=/sl= ticks) so a grader can
//             classify each trade TP-hit / SL-hit / flatten by comparing the exit fill to entry ± the bracket.
//             HONEST STALE MESSAGE (in-place 2026-07-08): the card now distinguishes a genuinely ABSENT Council
//             ("add the Council to this chart") from a STALE one ("Council verdict STALE — no fresh read for Ns —
//             slow bars"). Pairs with the Council's OnMarketData heartbeat: the old flat "no verdict" was misleading
//             in dry-up markets where the Council IS present but its published state aged past StaleSec.
//    v0.2.0 — on-chart glass card + clickable ARM BRIDGE button (ChartControl mouse hit-test); arming gates
//             all firing; optional ExitOnCouncilFlip; ShowCard/CardCorner. Supersedes v0.1.0 (archived).
//             FIXES (post first live render): (a) the card reads CouncilState LIVE every render (was bar-close only
//             → "no verdict" on slow bars); (b) gate on SizeMult>0 (SizeMult=0 = Council floor/veto = no trade),
//             else min a 1-lot; (c) pass riskDollars=0 to GateEntry — the Bridge sizes itself, the Gate only
//             VALIDATES; passing RiskDollars flipped it into risk-sizing → "risk too small for a 1-lot" Advisory →
//             fail-closed block (the first live test never fired). Base `…Strategies` ns (sub-ns hid the strategy).
//             FEATURE — .conf AUTO-READ: UseSentinelConfig reads <inst>_COUNCIL_<Long|Short>.conf (written by the
//             Excursion tab's Apply ◆, fed by SentinelExcursionRecorder_v1_4) for per-direction TP/SL, overriding
//             the manual ticks (re-reads on enable/recompile). Closes the lab → Bridge "real numbers" loop.
//    v0.1.0 — [archived] headless engine: consume CouncilState → GateEntry (fail-closed) → managed bracket
//             → record verdict on fire.
// ═════════════════════════════════════════════════════════════════════════════

using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Windows.Input;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.AddOns.Sentinel;

namespace NinjaTrader.NinjaScript.Strategies
{
    // NB: BASE Strategies namespace (NOT .Strategies.Sentinel). NT's Strategy selector does NOT surface
    // sub-namespaced strategies (only indicators fold into picker sub-folders) — a sub-namespaced strategy
    // compiles clean but never appears in the Strategies list. The "Sentinel" identity is carried by the
    // class-name prefix + display Name instead. (Federated naming law amended 2026-07-07; the Bridge strategy
    // stayed in base for the same reason.)
    public class SentinelBridge_v0_2_0 : Strategy
    {
        private const string _tag = "SentinelBridge";   // stable order-tag identity (decoupled from Name)
        private string _scope;                          // v1.15.0: "<inst>.<barTag>" — this chart's seam key

        /// <summary>This chart's Council scope. Lazily resolved (Instrument/BarsPeriod live from DataLoaded on),
        /// then cached. Null until configured — GetCouncilState(null) returns null, so the Bridge simply
        /// never fires, which is the correct fail-closed default.</summary>
        private string Scope()
        {
            // v0.2.4 — LANE-aware (Core ≥ v1.32.0). A strategy has no shared ChartControl with the chart's Council,
            // so it targets a lane EXPLICITLY: set ScopeLane to match the Council's "Scope Lane" on this chart.
            // Blank ⇒ bare scope (back-compat). This is how the Bridge reads the RIGHT lane's CouncilState when two
            // same-bartype charts run distinct Councils (GC.212202v6x24@A vs @B).
            if (_scope == null) { try { _scope = SentinelCore.ScopeOfLane(Instrument, BarsPeriod, ScopeLane); } catch (Exception _sx) { SentinelCore.Swallow("SentinelBridge.Scope", _sx); } }
            return _scope;
        }

        // v0.2.3 — this actor's IDENTITY (ML spec §10.9): <class>#<scope>@<account>. The delimiters '#' and '@' never
        // appear in a scope ('.') or a SIM-<LANE>-<SLOT> account, so the composite is unambiguous. Derived + cached;
        // it is the key the actor registry gates arming on and the 'instance' the Ledger records on every fire.
        private string _instanceKey;
        private string InstanceKey()
        {
            if (_instanceKey == null)
            {
                string scope = Scope();
                if (string.IsNullOrEmpty(scope))
                    scope = Instrument != null && Instrument.MasterInstrument != null ? Instrument.MasterInstrument.Name : "?";
                string acct = Account != null ? Account.Name : "?";
                _instanceKey = _tag + "#" + scope + "@" + acct;
            }
            return _instanceKey;
        }
        private int    _lastFiredBias;                   // one-shot per verdict episode (0 = none/flat)
        private int    _lastLoggedBlockBar = -1;
        private int    _cfgTpLong, _cfgSlLong, _cfgTpShort, _cfgSlShort;   // per-dir TP/SL from <inst>_COUNCIL_<dir>.conf (0 = not loaded)

        // — control surface (chart-only) —
        private volatile bool _armed;                    // runtime-only; NEVER persisted (no silent auto-arm)
        private SentinelSkin.Painter _sp;
        private SharpDX.RectangleF   _armBtn;            // ARM button hit-rect (ChartControl coords)
        private bool   _hooked;
        private string _lastBlock = "";
        private string _armBlocked = "";                 // v0.2.3 — set when an arm click was refused (instanceKey collision)

        // — verdict snapshot for the card (written on the data thread, read on the render thread; cosmetic) —
        private bool   _sHasData, _sHasEdge, _sVetoed;
        private int    _sBias, _sVoters, _sAgree, _sDisagree;
        private double _sConv, _sSize, _lastClose;
        private string _sReasons = "", _sVetoReason = "";
        private int    _lastFireDir;                     // last fired side for the card (0 = none)
        private bool     _sEverSeen;                     // have we EVER read a CouncilState? (distinguish absent vs stale)
        private DateTime _sLastSeenUtc;                  // when we last read a FRESH verdict (for the stale-age readout)
        private double   _crossRefPx;                    // v0.3.1 — tradeable quote on the side we crossed, stamped at submission (0 = none)

        // — God Reversal entry-trigger snapshot (opt-in; written data thread, read render thread; cosmetic) —
        private bool   _sGrevFresh;
        private int    _sGrevDir;
        private double _sGrevQ;
        private string _sGrevSetup = "";

        // — Helm interdiction (v0.3.0) — a human grabs the wheel WITHOUT stopping the car. State set on the data
        //   thread (OnMarketData/OnBarUpdate drain), read on the render thread for the card (cosmetic). volatile on
        //   the two that gate the OnBarClose entry path.
        private volatile bool _paused;          // Helm Pause/TakeOver: no NEW entries (open position + bracket kept)
        private volatile bool _skipNext;        // Helm SkipNext: skip exactly the next entry, then auto-re-arm
        private bool     _humanOverride;        // a Helm intent moved something THIS episode → the sample is interdicted
        private double   _liveStopPrice;        // tracked managed stop price (card + MoveStop tighten/widen classify)
        private double   _liveTargetPrice;      // tracked managed target price (card)
        private string   _sEpisodeId = "";      // current Council episode id (stamped on every helm-intent Ledger row)
        private string   _lastIntentId = "";    // last consumed intent id (echoed in HelmState — idempotency proof)
        private string   _lastHelmMsg = "";     // last interdiction outcome, for the card + log
        private DateTime _lastHelmPubUtc;       // throttle HelmState publish off the per-tick path

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name        = "Sentinel Bridge";
                Description = "Automated Council-consumer with an on-chart control surface: trades the fused "
                            + "CouncilState verdict through GateEntry (fail-closed), managed bracket, records "
                            + "the verdict on every fire (Lens grading). ARM via the on-chart button (off on "
                            + "load). Pairs with the manual Deck. Needs the Council on this chart + SentinelCore ≥ v1.7.0.";
                Calculate                     = Calculate.OnBarClose;
                EntriesPerDirection           = 1;
                EntryHandling                 = EntryHandling.AllEntries;
                IsUnmanaged                   = false;
                IsExitOnSessionCloseStrategy  = true;
                ExitOnSessionCloseSeconds     = 30;
                BarsRequiredToTrade           = 20;
                StartBehavior                 = StartBehavior.WaitUntilFlat;
                TimeInForce                   = TimeInForce.Gtc;

                BaseContracts     = 1;
                UseRiskSizing     = false;
                RiskDollars       = 200;
                ProfitTargetTicks = 40;
                StopLossTicks     = 30;
                UseSentinelConfig = false;
                StaleSec          = 90;
                MinConviction     = 0.0;
                ReverseOnFlip     = false;
                ExitOnCouncilFlip = false;
                UseGodReversalTrigger = false;
                GrevMaxAgeSec     = 90;
                GrevMinQuality    = 0.0;
                RecordVerdict     = true;
                LogChanges        = true;
                ShowCard          = true;
                CardCorner        = SentinelCardCorner.TopRight;
                ShowStrategyLabel = false;
                ScopeLane         = "";     // v0.2.4 — per-chart lane; blank = bare scope. Match the chart's Council lane.
                ObeyHelm          = true;   // v0.3.0 — obey Helm interdiction intents (a human grabbing the wheel)
            }
            else if (State == State.DataLoaded)
            {
                // Sentinel label-remover (indicator standard, applied to this strategy): NT draws the chart's
                // top-left label from Name, parameter list and all. Blanking it here keeps the chart clean; the
                // SetDefaults value is what identity/serialization use. Order tags are the separate _tag const,
                // so this can NEVER affect order routing. ⚠ If the Control Center's Strategies grid shows a blank
                // row, set "Show strategy label" = true — you must always be able to find and disable a strategy.
                // ⚠ A DISABLED strategy never reaches DataLoaded, so its "(D) Sentinel Bridge(…)" label persists
                // until it is ENABLED (verified 2026-07-09). That is correct and deliberate: the label is only on
                // screen while the strategy is inert. Blanking Name in SetDefaults would hide it — and would also
                // blank the Control Center row you need in order to find and stop it. Don't.
                if (!ShowStrategyLabel) Name = string.Empty;
                if (UseSentinelConfig) LoadCouncilConfigs();   // read the lab-derived COUNCIL ◆ TP/SL (re-reads on enable/recompile)
            }
            else if (State == State.Terminated)
            {
                if (_hooked && ChartControl != null) { try { ChartControl.PreviewMouseLeftButtonDown -= OnChartMouseDown; } catch (Exception _sx) { SentinelCore.Swallow("SentinelBridge.OnStateChange", _sx); } }
                _hooked = false;
                if (_sp != null) { try { _sp.Dispose(); } catch (Exception _sx) { SentinelCore.Swallow("SentinelBridge.OnStateChange", _sx); } _sp = null; }
                try { SentinelSkin.CardLayout.Release(this); } catch (Exception _sx) { SentinelCore.Swallow("SentinelBridge.OnStateChange", _sx); }
                // v0.2.3 — release the actor key (reference-checked in Core, so the NT re-enable race can't free a
                // freshly-registered replacement instance's claim).
                if (_instanceKey != null) { try { SentinelCore.UnregisterActor(_instanceKey, this); } catch (Exception _sx) { SentinelCore.Swallow("SentinelBridge.OnStateChange", _sx); } }
                // v0.3.0 — drop this actor's Helm intents + published state so a Helm surface never renders a ghost.
                if (_instanceKey != null) { try { SentinelCore.ClearHelm(_instanceKey); } catch (Exception _sx) { SentinelCore.Swallow("SentinelBridge.OnStateChange", _sx); } }
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0) return;
            if (State != State.Realtime) return;
            if (CurrentBar < BarsRequiredToTrade) return;
            if (Instrument == null || Instrument.MasterInstrument == null) return;

            _lastClose = Close[0];
            // v0.3.0 — drain Helm intents on the bar-close path too (a backstop to the per-tick OnMarketData drain);
            // both run on the one data thread, so the idempotent consume needs no lock. Also republishes HelmState.
            ServiceHelm();
            // v1.15.0: consult THIS CHART's scope, never the bare instrument. A bare-instrument lookup with
            // two charts on one instrument is ambiguous and now fails closed; a scope lookup is exact.
            var v = SentinelCore.GetCouncilState(Scope(), StaleSec);
            UpdateSnapshot(v);

            bool edge = v != null && v.HasEdge && v.Bias != 0;

            // optional Council-flip EXIT: close when the Council no longer supports the open side (armed only)
            if (_armed && ExitOnCouncilFlip && Position.MarketPosition != MarketPosition.Flat)
            {
                int cur = Position.MarketPosition == MarketPosition.Long ? 1 : -1;
                bool supports = edge && v.Bias == cur;
                if (!supports)
                {
                    StampCross(-cur);                                   // v0.3.1 — closing a long SELLS (bid), a short BUYS (ask)
                    if (Position.MarketPosition == MarketPosition.Long) ExitLong(_tag + "Flip", _tag);
                    else                                                ExitShort(_tag + "Flip", _tag);
                    if (LogChanges) { try { SentinelCore.Log("Bridge", "council-flip exit (" + (cur > 0 ? "LONG" : "SHORT") + ")"); } catch (Exception _sx) { SentinelCore.Swallow("SentinelBridge.ExitShort", _sx); } }
                    _lastFiredBias = 0;
                    return;
                }
            }

            if (!edge) { _lastFiredBias = 0; return; }
            int dir = v.Bias;
            if (dir == _lastFiredBias) return;
            if (!_armed) return;                                  // DISARMED: verdict shown, no trading
            if (_paused) { Block("Helm PAUSE — not entering (position + bracket kept)"); return; }   // v0.3.0

            var mp = Position.MarketPosition;
            if (mp == MarketPosition.Flat)
            {
                // v0.3.0 — Helm SkipNext: consume exactly this entry (mark the episode handled), then auto-re-arm.
                if (_skipNext) { _skipNext = false; _lastFiredBias = dir; Block("Helm SKIP-NEXT — skipped this entry"); PublishHelm(true); return; }
                if (TryEnter(dir, v)) _lastFiredBias = dir;
            }
            else if (ReverseOnFlip)
            {
                int cur = mp == MarketPosition.Long ? 1 : -1;
                if (dir == -cur && TryEnter(dir, v)) _lastFiredBias = dir;
            }
        }

        private bool TryEnter(int dir, SentinelCore.CouncilState v)
        {
            if (v.Conviction < MinConviction) { Block("conviction " + v.Conviction.ToString("0.00") + " < floor " + MinConviction.ToString("0.00")); return false; }
            if (v.SizeMult <= 0.0) { Block("size 0 — conviction below the Council floor / vetoed (no actionable size)"); return false; }

            // Optional God Reversal ENTRY TRIGGER: the Council gives bias/edge/size; the reversal gives the TIMING.
            // Require a fresh GodReversalState whose HELD dir is aligned with the Council bias (+ a quality floor).
            if (UseGodReversalTrigger)
            {
                var g = CurrentGrev();
                UpdateGrevSnapshot(g);
                if (g == null || g.Dir != dir)
                { Block("god-reversal: no fresh aligned trigger (need " + (dir > 0 ? "▲ long" : "▼ short") + ")"); return false; }
                if (g.Quality < GrevMinQuality)
                { Block("god-reversal q " + g.Quality.ToString("0.00") + " < floor " + GrevMinQuality.ToString("0.00")); return false; }
            }

            // Effective TP/SL: lab-derived <inst>_COUNCIL_<dir>.conf (when UseSentinelConfig + loaded), else the properties.
            int tpTicks = ProfitTargetTicks, slTicks = StopLossTicks;
            if (UseSentinelConfig)
            {
                if      (dir > 0 && _cfgTpLong  > 0 && _cfgSlLong  > 0) { tpTicks = _cfgTpLong;  slTicks = _cfgSlLong;  }
                else if (dir < 0 && _cfgTpShort > 0 && _cfgSlShort > 0) { tpTicks = _cfgTpShort; slTicks = _cfgSlShort; }
            }

            int baseQty = BaseContracts;
            if (UseRiskSizing)
            {
                try { baseQty = SentinelCore.SizeForRisk(Account, Instrument, slTicks, RiskDollars); } catch { baseQty = 0; }
                if (baseQty <= 0) { Block("risk-size = 0 ($" + RiskDollars.ToString("0") + " / " + slTicks + "t)"); return false; }
            }
            // ── SIZING, in the order Core's own header prescribes: size → SizedQuantity (clamps) → GateEntry (validates)
            //
            // v0.2.2: the Bridge used to stop at `Math.Max(1, baseQty × SizeMult)` and never call SizedQuantity(),
            // so the account profile's `size=` (SizeScale) AND the governor's RecommendedSize were both silently
            // ignored — a governor telling this strategy to size DOWN was not obeyed — and a BaseContracts above the
            // profile's ContractLimit hard-BLOCKED every entry instead of clamping to it. SizedQuantity is, in
            // SentinelCore's words, "the one place sizing math lives."
            int councilQty = Math.Max(1, (int)Math.Round(baseQty * v.SizeMult));

            // × SizeScale × governor RecommendedSize, clamped to ContractLimit, never below 1. Fail-open: an
            // unprofiled account returns councilQty unchanged.
            int wantQty = councilQty;
            try { wantQty = SentinelCore.SizedQuantity(Account, councilQty); } catch { wantQty = councilQty; }
            if (wantQty != councilQty)
                try { SentinelCore.Log("Bridge", "size " + councilQty + " → " + wantQty
                          + " (profile SizeScale / governor / ContractLimit) on " + Account.Name); } catch (Exception _sx) { SentinelCore.Swallow("SentinelBridge.TryEnter", _sx); }

            // ⚠ RESOLUTION: with BaseContracts = 1, SizeMult cannot scale a position DOWN — 1 × 0.19 rounds to 0 and
            // the Max(1,…) floor restores a 1-lot, because you cannot trade a fifth of a contract. SizeMult only has
            // resolution when BaseContracts ≥ 2 (≥ 4 to resolve the Council's typical 0.2-1.0 range). The floor
            // (SizeMult = 0) is what expresses "do not trade"; SizeMult is not a substitute for it.

            // The Bridge has now sized. The Gate only VALIDATES (kill/scoped/loss-stop/rate/qty/contract/
            // session/feed/rollover/news) — pass riskDollars=0 so it does NOT re-risk-size and reject our qty
            // as "risk too small" (that flip cost the first live test — GateEntry line ~1979/2007).
            SentinelCore.GateDecision gate;
            try { gate = SentinelCore.GateEntry(Account, Instrument.FullName, wantQty, slTicks, 0, Instrument); }
            catch { Block("gate threw — fail-closed"); return false; }
            if (gate == null || !gate.IsClear) { Block("gate " + (gate != null ? gate.Level + ": " + gate.Reason : "null")); return false; }

            int qty = gate.Size > 0 ? gate.Size : wantQty;

            SetStopLoss(CalculationMode.Ticks, slTicks);
            SetProfitTarget(CalculationMode.Ticks, tpTicks);

            StampCross(dir);                                            // v0.3.1 — reference price BEFORE the cross
            if (dir > 0) EnterLong(qty, _tag); else EnterShort(qty, _tag);
            try { SentinelCore.NoteOrderSubmitted(Account.Name); } catch (Exception _sx) { SentinelCore.Swallow("SentinelBridge.TryEnter", _sx); }

            // v0.3.0 — a fresh entry starts a clean episode: clear the per-episode interdiction flag and seed the
            // tracked bracket prices from the entry proxy (Close[0]) ± ticks. Helm MoveStop/BreakevenNow recompute
            // exactly from Position.AveragePrice once filled; these seed the card + the tighten/widen classify.
            _humanOverride = false;
            _liveStopPrice   = dir > 0 ? Close[0] - slTicks * TickSize : Close[0] + slTicks * TickSize;
            _liveTargetPrice = dir > 0 ? Close[0] + tpTicks * TickSize : Close[0] - tpTicks * TickSize;

            _lastFireDir = dir; _lastBlock = "";
            RecordFire(dir, qty, tpTicks, slTicks, v);
            return true;
        }

        // v0.3.1 — THE FILL-COST REFERENCE. Stamp the TRADEABLE price on the side we are about to cross, at the
        // moment of submission. This is the only reference an execution-cost measurement can use: `slip` is then
        // (fill − this), the real price of crossing, instead of the tautology `fill − fill = 0`.
        //   buying  (entry long / cover a short) lifts the ASK
        //   selling (entry short / close a long) hits the BID
        // ⚠ NEVER Close[0] here. The Bridge runs on SentinelTBars, a Heikin-Ashi type whose Close is the
        // (O+H+L+C)/4 average — a price that never traded — which is what made the recorded entry price wrong by
        // a direction-symmetric ~11 ticks (memory firepx-is-synthetic-ha-close).
        // Fails SOFT: 0 means "no defensible quote", and every caller falls back to the old behaviour rather than
        // record a number it cannot stand behind. Historical/replay returns the bar close ⇒ realtime-only meaning.
        private void StampCross(int crossDir)
        {
            try
            {
                double q = crossDir > 0 ? GetCurrentAsk() : GetCurrentBid();
                _crossRefPx = q > 0 ? q : 0.0;
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelBridge.StampCross", _sx); _crossRefPx = 0.0; }
        }

        private void RecordFire(int dir, int qty, int tpTicks, int slTicks, SentinelCore.CouncilState v)
        {
            string side = dir > 0 ? "LONG" : "SHORT";
            string detail = "inst=" + Instrument.MasterInstrument.Name + " " + side + " qty=" + qty
                          + " tp=" + tpTicks + " sl=" + slTicks
                          + " bias=" + v.Bias + " conv=" + v.Conviction.ToString("0.00", CultureInfo.InvariantCulture)
                          + " size=" + v.SizeMult.ToString("0.00", CultureInfo.InvariantCulture)
                          + " voters=" + v.Voters + " (" + v.Agree + "/" + v.Disagree + ")"
                          + " | " + (v.Reasons ?? "");
            if (UseGodReversalTrigger)
                detail += "  || GR " + (_sGrevDir > 0 ? "▲" : _sGrevDir < 0 ? "▼" : "·") + " "
                        + (string.IsNullOrEmpty(_sGrevSetup) ? "none" : _sGrevSetup)
                        + " q" + _sGrevQ.ToString("0.00", CultureInfo.InvariantCulture);
            try
            {
                // v0.2.3 — stamp the EPISODE join key (ML spec §10.2) + this actor's instanceKey on the fire, so Lens
                // can join the resulting FILL back to the exact Council verdict that caused it.
                // v0.3.1 — the DECISION price must be a price that could actually be traded. Close[0] is the
                // Heikin-Ashi average on this bar type and was wrong by a direction-symmetric ~11 ticks; the
                // stamped cross-side quote is the real one. Falls back to Close[0] only when no quote was available.
                double refPx = _crossRefPx > 0 ? _crossRefPx : Close[0];
                SentinelCore.Ledger.Order(Account.Name, Instrument.FullName, dir > 0 ? "Buy" : "SellShort", "Market", qty, refPx, _tag, v.EpisodeId, InstanceKey());
                if (RecordVerdict) SentinelCore.Ledger.Action("bridge-fire", Account.Name, detail, v.EpisodeId, InstanceKey());
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelBridge.RecordFire", _sx); }
            if (LogChanges) { try { SentinelCore.Log("Bridge", detail); } catch (Exception _sx) { SentinelCore.Swallow("SentinelBridge.RecordFire", _sx); } }
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity,
                                                  MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution == null || execution.Order == null) return;
            try
            {
                var o = execution.Order;
                // v0.3.1 — a MARKET fill used to set `intended = price`, making slip identically 0 (fill − fill).
                // That is not a measurement; it is a tautology, and it is why entry crossing cost had never once
                // been recorded. Use the quote we stamped on the side we crossed, at submission. Stop/limit are
                // UNCHANGED — their trigger/limit price was always the correct reference.
                double intended = price;
                if (o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit) intended = o.StopPrice;
                else if (o.OrderType == OrderType.Limit) intended = o.LimitPrice;
                else if (_crossRefPx > 0) intended = _crossRefPx;
                SentinelCore.Ledger.Fill(Account.Name, Instrument.FullName, o.OrderAction.ToString(),
                                         quantity, intended, price, TickSize, _tag, null, InstanceKey());
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelBridge.OnExecutionUpdate", _sx); }
        }

        // ── HELM interdiction (v0.3.0) ───────────────────────────────────────────────
        // A human grabs the wheel of this RUNNING autopilot without stopping it. Helm never touches an order — it
        // publishes an INTENT addressed to this actor's instanceKey; the Bridge executes it with its OWN managed
        // order handles and stays the sole owner (the three managed-order lessons in CLAUDE.md are why). Drained on
        // the data thread (OnMarketData for latency + an OnBarUpdate backstop); order calls therefore run in a valid
        // order context. Every intent is Ledgered with the episode + instanceKey and marks the episode interdicted.

        // Per-tick drain — FlattenNow/Pause must not wait for a slow bar to close. Realtime only (no historical/replay
        // interdiction), primary series only. OnMarketData is on the same data thread as OnBarUpdate, so the
        // idempotent TakeHelmIntent needs no lock.
        protected override void OnMarketData(NinjaTrader.Data.MarketDataEventArgs marketDataUpdate)
        {
            if (State != State.Realtime || BarsInProgress != 0) return;
            ServiceHelm();
        }

        // Drain the queue for THIS actor and apply each intent, then publish HelmState. Cheap when the queue is empty.
        private void ServiceHelm()
        {
            if (!ObeyHelm) { PublishHelm(false); return; }   // still publish truth so a Helm surface sees the Bridge
            string key = InstanceKey();
            if (string.IsNullOrEmpty(key)) return;
            SentinelCore.HelmIntent it;
            try { while ((it = SentinelCore.TakeHelmIntent(key)) != null) ApplyHelmIntent(it); }
            catch (Exception ex) { try { SentinelCore.Log("Bridge", "helm drain threw: " + ex.Message); } catch (Exception _sx) { SentinelCore.Swallow("SentinelBridge.ServiceHelm", _sx); } }
            PublishHelm(false);
        }

        // Execute one intent with the Bridge's own order handles. ASYMMETRIC GATE: risk-reducing = fail-OPEN;
        // risk-adding = validate through GateEntry fail-CLOSED. MoveStop is classified by direction vs the live stop.
        private void ApplyHelmIntent(SentinelCore.HelmIntent it)
        {
            if (it == null) return;
            _lastIntentId = it.Id ?? "";
            string why = "";
            bool applied = true;
            string gateReason = "";
            var mp = Position.MarketPosition;
            int posDir = mp == MarketPosition.Long ? 1 : (mp == MarketPosition.Short ? -1 : 0);

            switch (it.Verb)
            {
                case SentinelCore.HelmVerb.Pause:
                    _paused = true; why = "paused — no new entries (position + bracket kept)"; break;

                case SentinelCore.HelmVerb.Resume:
                    // risk-ADDING → the Gate must still allow trading (kill / governor / session). Fail-CLOSED.
                    if (HelmGateOk(out gateReason)) { _paused = false; why = "resumed"; }
                    else { applied = false; why = "resume REFUSED by gate — " + gateReason; }
                    break;

                case SentinelCore.HelmVerb.SkipNext:
                    _skipNext = true; why = "will skip the next entry, then auto-re-arm"; break;

                case SentinelCore.HelmVerb.FlattenNow:
                    if (posDir != 0)
                    {
                        StampCross(-posDir);                            // v0.3.1 — reference price BEFORE the cross
                        if (posDir > 0) ExitLong(_tag + "Helm", _tag); else ExitShort(_tag + "Helm", _tag);
                        // don't immediately jump back into the same side: hold this episode's bias as "already fired"
                        // so re-entry needs a genuine Council flip, not the still-standing verdict.
                        _lastFiredBias = _sBias != 0 ? _sBias : posDir;
                        why = "FLATTEN — closing " + (posDir > 0 ? "LONG" : "SHORT");
                    }
                    else why = "flatten: already flat";
                    break;

                case SentinelCore.HelmVerb.MoveStop:
                    if (posDir == 0)      { applied = false; why = "move-stop: flat, nothing to move"; }
                    else if (it.Price <= 0) { applied = false; why = "move-stop: no price given"; }
                    else
                    {
                        bool tightening = posDir > 0 ? it.Price > _liveStopPrice : it.Price < _liveStopPrice;
                        if (!tightening && !HelmGateOk(out gateReason))
                        { applied = false; why = "WIDEN-stop REFUSED by gate — " + gateReason; }
                        else
                        {
                            try { SetStopLoss(_tag, CalculationMode.Price, it.Price, false); _liveStopPrice = it.Price;
                                  why = (tightening ? "tightened" : "widened") + " stop → " + it.Price.ToString("0.#####", CultureInfo.InvariantCulture); }
                            catch (Exception ex) { applied = false; why = "move-stop failed: " + ex.Message; }
                        }
                    }
                    break;

                case SentinelCore.HelmVerb.MoveTarget:
                    // A target move never increases max loss (the stop bounds risk) → fail-OPEN.
                    if (posDir == 0)      { applied = false; why = "move-target: flat"; }
                    else if (it.Price <= 0) { applied = false; why = "move-target: no price given"; }
                    else
                    {
                        try { SetProfitTarget(_tag, CalculationMode.Price, it.Price); _liveTargetPrice = it.Price;
                              why = "target → " + it.Price.ToString("0.#####", CultureInfo.InvariantCulture); }
                        catch (Exception ex) { applied = false; why = "move-target failed: " + ex.Message; }
                    }
                    break;

                case SentinelCore.HelmVerb.BreakevenNow:
                    if (posDir == 0) { applied = false; why = "breakeven: flat"; }
                    else
                    {
                        double be = Position.AveragePrice;
                        try { SetStopLoss(_tag, CalculationMode.Price, be, false); _liveStopPrice = be;
                              why = "breakeven — stop → entry " + be.ToString("0.#####", CultureInfo.InvariantCulture); }
                        catch (Exception ex) { applied = false; why = "breakeven failed: " + ex.Message; }
                    }
                    break;

                case SentinelCore.HelmVerb.Scale:
                    if (it.QtyDelta < 0)
                    {
                        // scale DOWN — partial exit, risk-REDUCING → fail-OPEN.
                        if (posDir == 0) { applied = false; why = "scale-down: flat"; }
                        else
                        {
                            int reduce = Math.Min(-it.QtyDelta, Math.Abs(Position.Quantity));
                            if (reduce <= 0) { applied = false; why = "scale-down: nothing to reduce"; }
                            else
                            {
                                StampCross(-posDir);                    // v0.3.1 — reference price BEFORE the cross
                                if (posDir > 0) ExitLong(reduce, _tag + "HelmScale", _tag);
                                else            ExitShort(reduce, _tag + "HelmScale", _tag);
                                why = "scaled DOWN " + reduce + " contract" + (reduce == 1 ? "" : "s");
                            }
                        }
                    }
                    else if (it.QtyDelta > 0)
                    {
                        // scale UP adds risk AND a managed scale-in is blocked by EntriesPerDirection=1 — faking it
                        // risks the position→account desync this whole seam exists to avoid. Refuse honestly.
                        applied = false;
                        why = "scale-UP not supported in the single-entry managed Bridge — add discretionarily on the Deck";
                    }
                    else { applied = false; why = "scale: zero delta"; }
                    break;

                case SentinelCore.HelmVerb.TakeOver:
                    // A managed position cannot transfer ORDER OWNERSHIP to manual without disable/re-enable (the exact
                    // desync hazard). Honest interdiction: STAND DOWN from new entries + mark the episode interdicted;
                    // manage exits by hand via the Deck/Chart Trader. The Bridge keeps its managed bracket.
                    _paused = true;
                    why = "TAKE OVER — Bridge stood down (keeps its managed bracket; manage exits by hand)";
                    break;

                case SentinelCore.HelmVerb.HandBack:
                    // resume automated control — risk-ADDING, so it passes the Gate fail-CLOSED.
                    if (HelmGateOk(out gateReason)) { _paused = false; why = "HAND BACK — Bridge resumes control"; }
                    else { applied = false; why = "hand-back REFUSED by gate — " + gateReason; }
                    break;
            }

            if (applied) _humanOverride = true;
            _lastHelmMsg = (applied ? "" : "(refused) ") + it.Verb + " — " + why;

            // Ledger EVERY intent (applied or refused), stamped with the Council episode + instanceKey, so the Lab can
            // find interdicted episodes and exclude/model them — recording the human is what keeps the model honest.
            try
            {
                string detail = "verb=" + it.Verb + " applied=" + applied + " · " + why
                              + (string.IsNullOrEmpty(it.Reason) ? "" : " · " + it.Reason) + " · id=" + (it.Id ?? "");
                SentinelCore.Ledger.Action("helm-intent", Account != null ? Account.Name : "?", detail, _sEpisodeId, InstanceKey());
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelBridge.ExitShort", _sx); }
            try { SentinelCore.Log("Bridge", "HELM " + _lastHelmMsg); } catch (Exception _sx) { SentinelCore.Swallow("SentinelBridge.ExitShort", _sx); }
            PublishHelm(true);
        }

        // VALIDATE-ONLY gate probe for risk-adding intents (riskDollars=0 ⇒ no re-risk-sizing). Fail-CLOSED on throw.
        private bool HelmGateOk(out string reason)
        {
            reason = "";
            try
            {
                var g = SentinelCore.GateEntry(Account, Instrument.FullName, BaseContracts, StopLossTicks, 0, Instrument);
                if (g != null && g.IsClear) return true;
                reason = g != null ? (g.Level + ": " + g.Reason) : "null gate";
                return false;
            }
            catch (Exception ex) { reason = "gate threw: " + ex.Message; return false; }
        }

        // Publish this actor's live truth so a Helm surface renders reality, not a guess. Throttled off the per-tick
        // path (force=true bypasses the throttle — used right after an intent is applied).
        private void PublishHelm(bool force)
        {
            DateTime now = DateTime.UtcNow;
            if (!force && (now - _lastHelmPubUtc).TotalMilliseconds < 1000) return;
            _lastHelmPubUtc = now;
            try
            {
                var mp = Position.MarketPosition;
                int posQty = mp == MarketPosition.Long ? Position.Quantity : (mp == MarketPosition.Short ? -Position.Quantity : 0);
                bool flat = mp == MarketPosition.Flat;
                var hs = new SentinelCore.HelmState
                {
                    Instrument    = Instrument != null && Instrument.MasterInstrument != null ? Instrument.MasterInstrument.Name : "?",
                    Account       = Account != null ? Account.Name : "?",
                    Scope         = Scope(),
                    PositionQty   = posQty,
                    AvgPrice      = flat ? 0 : Position.AveragePrice,
                    StopPrice     = flat ? 0 : _liveStopPrice,
                    TargetPrice   = flat ? 0 : _liveTargetPrice,
                    Paused        = _paused,
                    SkipArmed     = _skipNext,
                    HumanOverride = _humanOverride,
                    LastIntentId  = _lastIntentId,
                    Status        = !_armed ? "disarmed" : (_paused ? "paused" : (flat ? "watching" : "in trade"))
                };
                SentinelCore.SetHelmState(InstanceKey(), hs);
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelBridge.PublishHelm", _sx); }
        }

        private void UpdateSnapshot(SentinelCore.CouncilState v)
        {
            if (v == null) { _sHasData = false; return; }
            _sHasData = true; _sEverSeen = true; _sLastSeenUtc = DateTime.UtcNow;
            _sHasEdge = v.HasEdge; _sVetoed = v.Vetoed;
            _sBias = v.Bias; _sConv = v.Conviction; _sSize = v.SizeMult;
            _sVoters = v.Voters; _sAgree = v.Agree; _sDisagree = v.Disagree;
            _sReasons = v.Reasons ?? ""; _sVetoReason = v.VetoReason ?? "";
            _sEpisodeId = v.EpisodeId ?? "";       // v0.3.0 — stamp Helm-intent Ledger rows with the live episode
        }

        // Latest God Reversal read for this instrument (fresh ≤ GrevMaxAgeSec), or null.
        private SentinelCore.GodReversalState CurrentGrev()
        {
            // Consult the GodReversal on THIS chart (scope), not whichever chart wrote last (SentinelCore v1.19.0).
            try { return SentinelCore.GetGodReversalState(Scope()
                ?? (Instrument != null && Instrument.MasterInstrument != null ? Instrument.MasterInstrument.Name : null),
                GrevMaxAgeSec); }
            catch { return null; }
        }

        private void UpdateGrevSnapshot(SentinelCore.GodReversalState g)
        {
            if (g == null) { _sGrevFresh = false; _sGrevDir = 0; _sGrevQ = 0; _sGrevSetup = ""; return; }
            _sGrevFresh = true; _sGrevDir = g.Dir; _sGrevQ = g.Quality; _sGrevSetup = g.Setup ?? "";
        }

        private void Block(string why)
        {
            _lastBlock = why;
            if (!LogChanges || CurrentBar == _lastLoggedBlockBar) return;
            _lastLoggedBlockBar = CurrentBar;
            try { SentinelCore.Log("Bridge", "blocked: " + why); } catch (Exception _sx) { SentinelCore.Swallow("SentinelBridge.Block", _sx); }
        }

        // Read the lab-derived <inst>_COUNCIL_<Long|Short>.conf (written by the Excursion tab's Apply ◆) for
        // per-direction TP/SL. 0 = not found → TryEnter falls back to the ProfitTargetTicks/StopLossTicks properties.
        private void LoadCouncilConfigs()
        {
            _cfgTpLong = _cfgSlLong = _cfgTpShort = _cfgSlShort = 0;
            if (Instrument == null || Instrument.MasterInstrument == null) return;
            string inst = Instrument.MasterInstrument.Name;
            ReadCouncilConf(inst, "Long",  ref _cfgTpLong,  ref _cfgSlLong);
            ReadCouncilConf(inst, "Short", ref _cfgTpShort, ref _cfgSlShort);
            if (LogChanges)
            {
                try { SentinelCore.Log("Bridge", "config " + inst + "_COUNCIL: Long TP" + _cfgTpLong + "/SL" + _cfgSlLong
                    + "  Short TP" + _cfgTpShort + "/SL" + _cfgSlShort + "  (0 = not found → manual ticks)"); } catch (Exception _sx) { SentinelCore.Swallow("SentinelBridge.LoadCouncilConfigs", _sx); }
            }
        }

        private void ReadCouncilConf(string inst, string dir, ref int tp, ref int sl)
        {
            try
            {
                string path = System.IO.Path.Combine(SentinelCore.SettingsDir, "SignalConfigs", inst + "_COUNCIL_" + dir + ".conf");
                if (!System.IO.File.Exists(path)) return;
                foreach (string raw in System.IO.File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string val = line.Substring(eq + 1).Trim();
                    int iv;
                    if (key == "takeprofitticks" && int.TryParse(val, out iv) && iv > 0) tp = iv;
                    else if (key == "stoplossticks" && int.TryParse(val, out iv) && iv > 0) sl = iv;
                }
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelBridge.ReadCouncilConf", _sx); }
        }

        // ── the on-chart control surface ─────────────────────────────────────────────
        private void OnChartMouseDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (!ShowCard || ChartControl == null) return;
                var p = e.GetPosition(ChartControl);
                if (_armBtn.Width > 0 && _armBtn.Contains((float)p.X, (float)p.Y))
                {
                    if (!_armed)
                    {
                        // v0.2.3 — the name is an INTERLOCK (ML spec §10.10): claim the instanceKey BEFORE arming. A
                        // collision is another live Bridge on this same scope+account — the exact managed-position
                        // hazard — so REFUSE to arm rather than desync the account. (Realtime-only: the button is live.)
                        if (!SentinelCore.RegisterActor(InstanceKey(),
                                                        Account != null ? Account.Name : null,
                                                        Instrument != null && Instrument.MasterInstrument != null ? Instrument.MasterInstrument.Name : null,
                                                        this))
                        {
                            _armBlocked = "NAME TAKEN — " + InstanceKey() + " is already armed";
                            try { SentinelCore.Log("Bridge", "ARM REFUSED — " + _armBlocked); } catch (Exception _sx) { SentinelCore.Swallow("SentinelBridge.OnChartMouseDown", _sx); }
                            try { ChartControl.InvalidateVisual(); } catch (Exception _sx) { SentinelCore.Swallow("SentinelBridge.OnChartMouseDown", _sx); }
                            e.Handled = true;
                            return;
                        }
                        _armed = true; _armBlocked = ""; _lastFiredBias = 0;   // arming re-fires the current verdict
                        try { SentinelCore.Log("Bridge", "ARMED by click (" + InstanceKey() + ")"); } catch (Exception _sx) { SentinelCore.Swallow("SentinelBridge.OnChartMouseDown", _sx); }
                    }
                    else
                    {
                        _armed = false;
                        SentinelCore.UnregisterActor(InstanceKey(), this);
                        try { SentinelCore.Log("Bridge", "DISARMED by click"); } catch (Exception _sx) { SentinelCore.Swallow("SentinelBridge.OnChartMouseDown", _sx); }
                    }
                    try { ChartControl.InvalidateVisual(); } catch (Exception _sx) { SentinelCore.Swallow("SentinelBridge.OnChartMouseDown", _sx); }
                    e.Handled = true;
                }
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelBridge.OnChartMouseDown", _sx); }
        }

        protected override void OnRender(NinjaTrader.Gui.Chart.ChartControl chartControl, NinjaTrader.Gui.Chart.ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);
            if (!ShowCard || RenderTarget == null || ChartPanel == null) return;
            // hook the click handler here (guaranteed UI thread + chart present); _hooked guards double-subscribe
            if (!_hooked && ChartControl != null)
            {
                ChartControl.PreviewMouseLeftButtonDown += OnChartMouseDown;
                _hooked = true;
            }
            try
            {
                if (_sp == null) _sp = new SentinelSkin.Painter();
                _sp.Begin(RenderTarget);

                // Refresh the verdict LIVE every render (the OnBarClose engine reads its own copy for firing;
                // the card must not wait for a bar to close to show the current Council verdict).
                try { UpdateSnapshot(SentinelCore.GetCouncilState(Scope(), StaleSec)); }
                catch (Exception _sx) { SentinelCore.Swallow("SentinelBridge.OnRender", _sx); }
                if (UseGodReversalTrigger) { try { UpdateGrevSnapshot(CurrentGrev()); } catch (Exception _sx) { SentinelCore.Swallow("SentinelBridge.OnRender", _sx); } }

                const float cw = 266f, ch = 178f;
                // pinned: this card carries the ARM BRIDGE button (_armBtn is hit-tested against this rect). If a
                // crowded column ever overflowed and CardLayout hid it, the ONLY on-chart arm/disarm control for an
                // order-placing strategy would silently vanish. Pinned cards are never hidden. See CardLayout policy.
                var slot = SentinelSkin.CardLayout.Place(this, ChartPanel,
                    ChartPanel.X, ChartPanel.Y, ChartPanel.W, ChartPanel.H, CardCorner, cw, ch, pinned: true);

                bool armed = _armed;
                var edgeCol = armed ? SentinelSkin.CAccent : SentinelSkin.CLine;
                var r = _sp.Card(slot.X, slot.Y, cw, ch, edgeCol);
                var lead = SharpDX.DirectWrite.TextAlignment.Leading;
                var ctr  = SharpDX.DirectWrite.TextAlignment.Center;

                // header — Spartan brand mark (accent when armed, mute when disarmed)
                _sp.Helmet(r.Left + 9f, r.Top + 8f, 16f, armed ? SentinelSkin.CAccent : SentinelSkin.CMute, armed);
                _sp.Text("SENTINEL BRIDGE", r.Left + 20f, r.Top, r.Width - 94f, 16f, SentinelSkin.CInk, 11f, true);
                _sp.Pill(armed ? "ARMED" : "DISARMED", r.Right, r.Top - 1f, armed ? SentinelSkin.CAccent : SentinelSkin.CMute);

                // ── ARM BRIDGE button (clickable) ──
                float by = r.Top + 22f, bh = 30f;
                var btnBorder = armed ? SentinelSkin.CAccent : SentinelSkin.CMute;
                _sp.Card(r.Left, by, r.Width, bh, btnBorder, 8f, 2f);           // glass button bg
                _armBtn = new SharpDX.RectangleF(r.Left, by, r.Width, bh);      // hit-rect (ChartControl coords)
                _sp.Text(armed ? "●  ARMED — click to DISARM" : "▶  ARM BRIDGE",
                    r.Left, by + 7f, r.Width, 18f, SentinelSkin.CAccent, 12.5f, true, ctr);

                float y = by + bh + 8f;
                _sp.Divider(r.Left, y - 4f, r.Right);

                // verdict
                if (!_sHasData)
                {
                    // Distinguish GENUINELY ABSENT (Council not on this chart) from STALE (Council present but its
                    // published state aged past StaleSec — normal in thin/dry-up markets where bars close slowly).
                    string msg;
                    if (_sEverSeen)
                    {
                        int ageSec = (int)Math.Max(0, (DateTime.UtcNow - _sLastSeenUtc).TotalSeconds);
                        msg = "— Council verdict STALE (no fresh read for " + ageSec.ToString(CultureInfo.InvariantCulture) + "s — slow bars)";
                    }
                    else
                        msg = "— no Council verdict (add the Council to this chart)";
                    _sp.Text(msg, r.Left, y + 6f, r.Width, 16f, SentinelSkin.CMute, 10f, false, lead, true);
                }
                else
                {
                    int b = _sBias;
                    string side = b > 0 ? "LONG" : (b < 0 ? "SHORT" : "FLAT");
                    var sideCol = b > 0 ? SentinelSkin.CUp : (b < 0 ? SentinelSkin.CDown : SentinelSkin.CMute);
                    _sp.Pill(side, r.Left + 62f, y, sideCol);
                    _sp.Text("VERDICT", r.Left, y + 2f, 60f, 12f, SentinelSkin.CMute, 9f, true);
                    _sp.Text("conv " + Math.Round(_sConv * 100).ToString(CultureInfo.InvariantCulture) + "%",
                        r.Left + 74f, y + 1f, 90f, 14f, SentinelSkin.CInk2, 10.5f, false, lead, true);
                    _sp.Text("size ×" + _sSize.ToString("0.00", CultureInfo.InvariantCulture),
                        r.Left + 160f, y + 1f, 80f, 14f, _sSize > 0 ? SentinelSkin.CAccent : SentinelSkin.CMute, 10.5f, false, lead, true);

                    // conviction track + tally
                    _sp.Track(r.Left, y + 20f, r.Width, (float)Math.Max(0, Math.Min(1, _sConv)), armed ? SentinelSkin.CAccent : SentinelSkin.CFaint, 5f);
                    string tally = "▲" + _sAgree + " ▼" + _sDisagree + " · " + _sVoters + "v";
                    if (_sVetoed) tally += "   VETO" + (string.IsNullOrEmpty(_sVetoReason) ? "" : ":" + Trunc(_sVetoReason, 14));
                    _sp.Text(tally, r.Left, y + 28f, r.Width, 14f, _sVetoed ? SentinelSkin.CWarn : SentinelSkin.CInk2, 10f, false, lead, true);
                    _sp.Text(Trunc(_sReasons, 40), r.Left, y + 44f, r.Width, 14f, SentinelSkin.CMute, 9f, false, lead, true);
                }

                // position + last fire
                string pos;
                var posCol = SentinelSkin.CMute;
                var mp = Position.MarketPosition;
                if (mp == MarketPosition.Flat) pos = "flat";
                else
                {
                    int d = mp == MarketPosition.Long ? 1 : -1;
                    double openTicks = TickSize > 0 ? (_lastClose - Position.AveragePrice) / TickSize * d : 0;
                    pos = (d > 0 ? "LONG " : "SHORT ") + Math.Abs(Position.Quantity) + "   " + (openTicks >= 0 ? "+" : "") + Math.Round(openTicks) + "t";
                    posCol = openTicks >= 0 ? SentinelSkin.CUp : SentinelSkin.CDown;
                }
                _sp.Text(pos, r.Left, r.Top + ch - 42f, r.Width, 14f, posCol, 10.5f, true, lead, true);

                // status footer
                string status = armed ? (mp == MarketPosition.Flat ? "watching — will fire on an aligned edge" : "in trade")
                                      : "disarmed — click ARM BRIDGE to trade";
                // when the God Reversal trigger gates entry, show its live read so you can see WHY it's waiting
                if (UseGodReversalTrigger && armed && mp == MarketPosition.Flat)
                    status = (_sGrevFresh && _sGrevDir != 0)
                        ? "GR " + (_sGrevDir > 0 ? "▲" : "▼") + " " + (string.IsNullOrEmpty(_sGrevSetup) ? "reversal" : _sGrevSetup)
                          + " q" + _sGrevQ.ToString("0.00") + (_sHasData && _sGrevDir == _sBias ? " · aligned" : " · waiting align")
                        : "GR trigger: waiting for a reversal";
                // v0.3.0 — Helm interdiction takes visual priority (a human is driving): show pause / last override.
                if (_paused) status = "PAUSED by Helm — no new entries";
                else if (_humanOverride && !string.IsNullOrEmpty(_lastHelmMsg)) status = "Helm: " + Trunc(_lastHelmMsg, 34);
                if (!string.IsNullOrEmpty(_lastBlock)) status = "blocked: " + Trunc(_lastBlock, 34);
                _sp.Text(status, r.Left, r.Top + ch - 26f, r.Width, 14f,
                    _paused ? SentinelSkin.CWarn : SentinelSkin.CMute, 9f, false, lead, true);

                _sp.End();
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelBridge.OnRender", _sx); }
        }

        private static string Trunc(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Length <= max ? s : s.Substring(0, max);
        }

        #region Properties
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Base contracts", Description = "Base position size before the Council SizeMult scales it (Fixed sizing).", GroupName = "1 Sizing", Order = 1)]
        public int BaseContracts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use $-risk sizing", Description = "Size from RiskDollars ÷ (StopLossTicks × $/tick) instead of Base contracts, then × SizeMult.", GroupName = "1 Sizing", Order = 2)]
        public bool UseRiskSizing { get; set; }

        [NinjaScriptProperty]
        [Range(1, double.MaxValue)]
        [Display(Name = "Risk $ (per trade)", Description = "Dollar risk per trade when 'Use $-risk sizing' is on.", GroupName = "1 Sizing", Order = 3)]
        public double RiskDollars { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Profit target (ticks)", Description = "Managed profit target. Hand-set from the ◆ of the COUNCIL group in the Excursion tab.", GroupName = "2 Bracket", Order = 1)]
        public int ProfitTargetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Stop loss (ticks)", Description = "Managed stop. Hand-set from the ◆ of the COUNCIL group in the Excursion tab.", GroupName = "2 Bracket", Order = 2)]
        public int StopLossTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Sentinel config (COUNCIL ◆)", Description = "Auto-read <inst>_COUNCIL_<dir>.conf (written by the Excursion tab's Apply ◆) for per-direction TP/SL, overriding the manual ticks above. Re-reads on enable/recompile.", GroupName = "2 Bracket", Order = 3)]
        public bool UseSentinelConfig { get; set; }

        [NinjaScriptProperty]
        [Range(1, double.MaxValue)]
        [Display(Name = "Council stale (sec)", Description = "Ignore a Council verdict older than this (a frozen Council can't fire a stale trade).", GroupName = "3 Council", Order = 1)]
        public double StaleSec { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 1.0)]
        [Display(Name = "Min conviction", Description = "Extra conviction floor ABOVE the Council's own (0 = trust the Council). Set from the ⑤ Conviction referee.", GroupName = "3 Council", Order = 2)]
        public double MinConviction { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Reverse on flip", Description = "If in a position and the Council flips to an aligned edge the other way, reverse. Default OFF.", GroupName = "3 Council", Order = 3)]
        public bool ReverseOnFlip { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Exit on Council flip", Description = "Close the position when the Council stops supporting the open side (loses edge / flips). Default OFF.", GroupName = "3 Council", Order = 4)]
        public bool ExitOnCouncilFlip { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use God Reversal trigger", Description = "Also require a FRESH SentinelGodReversal read aligned with the Council bias before entering — Council gives bias/size, the reversal gives entry timing (doctrine §7). Needs Sentinel God Reversal on the chart + SentinelCore ≥ v1.14.0. Default OFF.", GroupName = "3 Council", Order = 5)]
        public bool UseGodReversalTrigger { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, double.MaxValue)]
        [Display(Name = "God Reversal stale (sec)", Description = "Ignore a God Reversal read older than this when the trigger is on.", GroupName = "3 Council", Order = 6)]
        public double GrevMaxAgeSec { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 1.0)]
        [Display(Name = "God Reversal min quality", Description = "Minimum reversal confluence quality (0..1) required to trigger. 0 = any aligned reversal.", GroupName = "3 Council", Order = 7)]
        public double GrevMinQuality { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Record verdict on fire", Description = "Write the verdict to the Ledger on every fire so Lens can grade the weights. Keep ON.", GroupName = "4 Sentinel", Order = 1)]
        public bool RecordVerdict { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Log fires/blocks", Description = "Write fires and gate-blocks to sentinel.log (blocks rate-limited to once/bar).", GroupName = "4 Sentinel", Order = 2)]
        public bool LogChanges { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show card", Description = "Draw the on-chart Sentinel glass card + the clickable ARM BRIDGE button.", GroupName = "4 Sentinel", Order = 3)]
        public bool ShowCard { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Card corner", Description = "Which chart corner the card docks to. Cards in the same corner auto-stack.", GroupName = "4 Sentinel", Order = 4)]
        public SentinelCardCorner CardCorner { get; set; }

        // NOT [NinjaScriptProperty] on purpose: it still serializes to the workspace/template and shows in the F6
        // grid, but adds NO parameter to NT's generated-region constructor — so existing saved charts keep loading.
        [Display(Name = "Show strategy label", Description = "Show NT's top-left chart label (name + full parameter list). Off = clean Sentinel chart. Turn ON if the Control Center's Strategies grid shows a blank name.", GroupName = "4 Sentinel", Order = 5)]
        public bool ShowStrategyLabel { get; set; }

        // v0.2.4 — NOT [NinjaScriptProperty] (same reason as above): serializes + shows, no ctor churn.
        [Display(Name = "Scope Lane", Description = "Per-chart lane — match the 'Scope Lane' set on THIS chart's Council so the Bridge reads the right lane's verdict. BLANK = bare scope (default). Needed only when two charts share instrument + bar type + size (e.g. A/B test lanes). Letters/digits only.", GroupName = "0 Scope Lane", Order = 0)]
        public string ScopeLane { get; set; }

        // v0.3.0 — NOT [NinjaScriptProperty] (serializes + shows, no ctor churn). When ON, the Bridge obeys Helm
        // interdiction intents (a human grabbing the wheel via a Helm surface): Pause/SkipNext/FlattenNow/MoveStop/
        // MoveTarget/BreakevenNow/Scale-down, with risk-adding verbs (Resume/widen/HandBack) validated by the Gate.
        // OFF = the Bridge ignores all intents (still publishes HelmState so a surface can see it). Default ON.
        [Display(Name = "Obey Helm intents", Description = "Obey Helm interdiction intents addressed to this Bridge (a human grabbing the wheel without stopping it). Risk-reducing moves are fail-open; risk-adding moves pass GateEntry. Default ON.", GroupName = "5 Helm", Order = 1)]
        public bool ObeyHelm { get; set; }
        #endregion
    }
}
