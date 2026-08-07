// ─────────────────────────────────────────────────────────────────────────────
// This Source Code Form is subject to the terms of the Mozilla Public License,
// v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
// one at https://mozilla.org/MPL/2.0/.
//
// Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
// ─────────────────────────────────────────────────────────────────────────────
// SentinelKeel_v0_1_0 — rung A of the Keel ladder
// =============================================================================================
//  Spec: Docs/SENTINEL_KEEL_SPEC.md §4 (rung A) · Docs/SENTINEL_STRATEGY_INTEGRATION_SPEC.md §2
//  Test plan: Docs/SENTINEL_KEEL_TEST_PLAN.md
//
//  WHAT THIS IS
//  ------------
//  `RangeFilterATRStrategy` with a measurement rig bolted on and NOTHING ELSE CHANGED. The signal,
//  the state machine, the order model, the parameter defaults and the bar-by-bar arithmetic are
//  transcribed from the control verbatim. Everything Sentinel adds is observation.
//
//  ⭐ THE EQUIVALENCE GATE — the acceptance test for this file.
//  With BracketMode = AtrBracket and default parameters, this must produce a trade list IDENTICAL
//  to `RangeFilterATRStrategy` on the same data: same bar, same direction, same size, same exits.
//  Any difference is a DEFECT IN KEEL, not a finding. Instrumentation that changes behaviour is not
//  instrumentation — it is a second strategy wearing the first one's name, and every number it
//  produces would be uninterpretable.
//
//  HOW THAT IS ENFORCED IN THE CODE, not merely intended:
//   1. Every Sentinel call sits inside try/catch → SentinelCore.Swallow. A Ledger write, a Gate
//      consult or a corpus fire that throws must leave the order path untouched. The instrumentation
//      is allowed to fail; it is not allowed to interfere.
//   2. No Sentinel call may `return`, `continue`, or mutate signal state. They are leaves.
//   3. The Gate is ADVISORY: consulted, logged, and its answer DISCARDED. A blocking gate would
//      change the trade set, which is precisely what the baseline cannot survive.
//   4. `BracketMode.AtrBracket` defers to the control's own UseFixedStop/UseFixedTarget toggles
//      rather than overriding them — so at defaults it is the control, and a user who set a fixed
//      stop on the control gets the same behaviour here.
//
//  ⛔ `RangeFilterATRStrategy.cs` IS THE FROZEN CONTROL. Do not edit it, do not port fixes into it,
//  do not "improve" it. Its whole value is that it cannot drift, because every claim of the form
//  "the instrumented version behaves the same" is only checkable against a fixed original.
//
//  KNOWN DEFECTS INHERITED ON PURPOSE (spec §8) — these are NOT bugs in this file:
//   • the pending-reversal state machine can wedge (no timeout, no re-arm) — [[conditions-vs-latches]]
//   • it fires on the first filter move after warm-up (a startup artifact — exclude the first fire
//     of each session from the corpus, or the baseline is contaminated)
//   • D2 / no re-entry inside a continuing trend: a stop that fills while the filter still rises
//     leaves the strategy flat with trendState unchanged, so it cannot re-enter until the filter
//     turns down AND back up — forfeiting the rest of the move. MEASURE IT (test plan Q5) before
//     touching it; a re-entry rule is a new strategy, not an instrumentation change.
//
//  Changelog
//    v0.1.0 (2026-07-30) — first cut. Rung A: Ledger (intended contract) + excursion fire via
//           SentinelCore.NoteSignalFire (Core ≥ v1.46.0) + advisory Gate + BracketMode incl. the
//           flip-to-flip `None` measurement mode. Orders unchanged from the control.
// =============================================================================================
#region Using declarations
using System;
using System.Globalization;
using System.Windows.Media;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.AddOns.Sentinel;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    /// <summary>Bracket policy. ⚠ `None` is a MEASUREMENT mode — see the property description.</summary>
    public enum KeelBracketMode
    {
        AtrBracket,
        FixedTicks,
        None
    }

    public class SentinelKeel_v0_1_0 : Strategy
    {
        private const string KeelVersion = "0.1.0";
        private const string Tag         = "SentinelKeel";   // Ledger tag + instance identity prefix

        // ── control state (transcribed verbatim) ──────────────────────────────────────────────
        private Series<double> absoluteCloseChange;
        private Series<double> rangeFilter;
        private Series<int> trendState;

        private EMA firstRangeEma;
        private EMA secondRangeEma;
        private ATR atr;

        //  1 = enter long after the opposing short has closed
        // -1 = enter short after the opposing long has closed
        //  0 = no reversal waiting
        private int pendingReversalDirection;
        private double pendingReversalAtr;

        // ── Sentinel instrumentation state (observation only) ─────────────────────────────────
        private string _scope, _instanceKey;
        private double _crossRefPx;          // quote on the side being crossed, stamped at submission
        private int    _nFires, _nGateWouldBlock;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Sentinel Keel — instrumented range-filter fresh-flip stop-and-reverse. Rung A: orders identical to RangeFilterATRStrategy; adds Ledger, excursion recording and an advisory Gate.";
                Name = "Sentinel Keel";

                // ⚠ EVERY VALUE BELOW IS THE CONTROL'S. Changing one breaks the equivalence gate.
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsFillLimitOnTouch = false;
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution = OrderFillResolution.Standard;
                Slippage = 0;
                StartBehavior = StartBehavior.WaitUntilFlat;
                TimeInForce = TimeInForce.Gtc;
                TraceOrders = false;
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 30;
                BarsRequiredToPlot = 0;
                AddPlot(Brushes.DodgerBlue, "RangeFilterLine");

                RangePeriod = 14;
                RangeMultiplier = 2.5;
                AtrPeriod = 14;
                StopAtrMultiplier = 1.5;
                TargetAtrMultiplier = 3.0;
                ContractQuantity = 1;
                EnableLongEntries = true;
                EnableShortEntries = true;
                UseFixedStop = false;
                StopLossTicks = 15;
                UseFixedTarget = false;
                ProfitTargetTicks = 15;

                // ── added by Keel; every default is the no-change position ──
                BracketMode = KeelBracketMode.AtrBracket;
                RecordToLedger = true;
                RecordExcursions = true;
                ConsultGate = true;      // advisory in v0.1.0 — logs, never blocks
                ScopeLane = "";
                LogFires = true;
            }
            else if (State == State.DataLoaded)
            {
                absoluteCloseChange = new Series<double>(this);
                rangeFilter = new Series<double>(this);
                trendState = new Series<int>(this);

                // Standard two-stage smoothing used by the range-filter formula:
                // EMA(abs(close - prior close), RangePeriod), then
                // EMA(first EMA, 2 * RangePeriod - 1).
                firstRangeEma = EMA(absoluteCloseChange, RangePeriod);
                secondRangeEma = EMA(firstRangeEma, Math.Max(1, 2 * RangePeriod - 1));
                atr = ATR(AtrPeriod);

                pendingReversalDirection = 0;
                pendingReversalAtr = 0.0;
            }
            else if (State == State.Realtime)
            {
                // Announce identity ONCE, so a corpus row can be traced back to a specific instance on a
                // specific chart. Cheap, and it is the difference between "some Keel produced this" and
                // "this Keel produced this" when two are running.
                try
                {
                    SentinelCore.Log(Tag, "armed v" + KeelVersion + " instance=" + InstanceKey()
                        + " bracket=" + BracketMode
                        + " gate=" + (ConsultGate ? "advisory" : "off")
                        + " ledger=" + RecordToLedger + " excursions=" + RecordExcursions);
                }
                catch (Exception _sx) { SentinelCore.Swallow("SentinelKeel.Announce", _sx); }
            }
        }

        // ═══ SIGNAL — transcribed from the control. Do not restructure. ═══════════════════════
        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            if (CurrentBar == 0)
            {
                absoluteCloseChange[0] = 0.0;
                rangeFilter[0] = Close[0];
                trendState[0] = 0;

                Values[0][0] = rangeFilter[0];

                return;
            }

            absoluteCloseChange[0] = Math.Abs(Close[0] - Close[1]);

            int minimumBars = Math.Max(
                AtrPeriod + 1,
                Math.Max(RangePeriod + 1, 2 * RangePeriod));

            // Seed the state while the EMA and ATR calculations warm up.
            if (CurrentBar < minimumBars)
            {
                rangeFilter[0] = Close[0];
                trendState[0] = 0;

                Values[0][0] = rangeFilter[0];

                return;
            }

            double smoothedRange = secondRangeEma[0] * RangeMultiplier;
            double previousFilter = rangeFilter[1];
            double currentFilter;

            // Range filter:
            // Rising price can only move the filter upward.
            // Falling price can only move the filter downward.
            if (Close[0] > previousFilter)
                currentFilter = Math.Max(previousFilter, Close[0] - smoothedRange);
            else
                currentFilter = Math.Min(previousFilter, Close[0] + smoothedRange);

            rangeFilter[0] = currentFilter;
            Values[0][0] = currentFilter;

            int previousTrend = trendState[1];
            int currentTrend = previousTrend;

            if (currentFilter > previousFilter)
                currentTrend = 1;
            else if (currentFilter < previousFilter)
                currentTrend = -1;

            trendState[0] = currentTrend;
            if (currentTrend == 1)
                PlotBrushes[0][0] = Brushes.LimeGreen;
            else if (currentTrend == -1)
                PlotBrushes[0][0] = Brushes.Red;
            else
                PlotBrushes[0][0] = Brushes.DodgerBlue;

            bool freshLong = currentTrend == 1 && previousTrend != 1;
            bool freshShort = currentTrend == -1 && previousTrend != -1;

            if (!freshLong && !freshShort)
                return;

            // Do not submit another reversal while the previous flatten order is working.
            if (pendingReversalDirection != 0)
                return;

            double signalAtr = atr[0];

            if (signalAtr <= 0.0 || double.IsNaN(signalAtr))
                return;

            if (freshLong)
            {
                if (Position.MarketPosition == MarketPosition.Long)
                    return;

                if (Position.MarketPosition == MarketPosition.Short)
                {
                    // The flip invalidates the short regardless of the toggles, so
                    // always close it. Only queue the reversal entry when longs
                    // are enabled; otherwise the strategy simply goes flat.
                    pendingReversalDirection = EnableLongEntries ? 1 : 0;
                    pendingReversalAtr = EnableLongEntries ? signalAtr : 0.0;
                    StampCross(-1);                       // closing a long-side reversal SELLS the short back: crossing the ask
                    ExitShort("ReverseExitShort", "Short");
                    return;
                }

                if (!EnableLongEntries)
                    return;

                SubmitLong(signalAtr);
                return;
            }

            if (freshShort)
            {
                if (Position.MarketPosition == MarketPosition.Short)
                    return;

                if (Position.MarketPosition == MarketPosition.Long)
                {
                    pendingReversalDirection = EnableShortEntries ? -1 : 0;
                    pendingReversalAtr = EnableShortEntries ? signalAtr : 0.0;
                    StampCross(1);
                    ExitLong("ReverseExitLong", "Long");
                    return;
                }

                if (!EnableShortEntries)
                    return;

                SubmitShort(signalAtr);
            }
        }

        protected override void OnExecutionUpdate(
            Execution execution,
            string executionId,
            double price,
            int quantity,
            MarketPosition marketPosition,
            string orderId,
            DateTime time)
        {
            // ── instrumentation FIRST, and it cannot alter what follows ──
            RecordFill(execution, price, quantity);

            // The MT5 version closes the opposing side first, then submits
            // the new entry. This waits for the flatten execution before
            // placing the reversal order.
            if (pendingReversalDirection == 0)
                return;

            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            int direction = pendingReversalDirection;
            double reversalAtr = pendingReversalAtr;

            pendingReversalDirection = 0;
            pendingReversalAtr = 0.0;

            if (direction == 1)
                SubmitLong(reversalAtr);
            else if (direction == -1)
                SubmitShort(reversalAtr);
        }

        private void SubmitLong(double signalAtr)
        {
            int stopTicks = StopTicksFor(signalAtr);
            int targetTicks = TargetTicksFor(signalAtr);

            StampCross(1);                                  // reference price BEFORE the cross
            ApplyBracket("Long", stopTicks, targetTicks);
            NoteEntry(1, stopTicks, targetTicks, signalAtr);

            EnterLong(ContractQuantity, "Long");
        }

        private void SubmitShort(double signalAtr)
        {
            int stopTicks = StopTicksFor(signalAtr);
            int targetTicks = TargetTicksFor(signalAtr);

            StampCross(-1);
            ApplyBracket("Short", stopTicks, targetTicks);
            NoteEntry(-1, stopTicks, targetTicks, signalAtr);

            EnterShort(ContractQuantity, "Short");
        }

        /// <summary>The ONLY behavioural fork Keel adds, and `AtrBracket` is the control exactly.</summary>
        private void ApplyBracket(string signalName, int stopTicks, int targetTicks)
        {
            // ⚠ `None` = flip-to-flip. No stop, no target: the position reverses only on the next fresh
            // flip. It exists to isolate the SIGNAL from the EXIT POLICY — every number we have conflates
            // them, because a stop-out is booked as a loss against the signal when it may be the bracket
            // cutting a trade the filter would have carried. This is the unmanaged expectancy CEILING that
            // any exit policy is then measured against.
            // ⚠ MEASUREMENT ONLY. Unbounded per-trade risk; never on a funded or evaluation account. In
            // replay it is safe, and it is the single most informative run in the matrix.
            if (BracketMode == KeelBracketMode.None)
                return;

            // Tick-based Set methods attach the distances to the actual fill price.
            SetStopLoss(signalName, CalculationMode.Ticks, stopTicks, false);
            SetProfitTarget(signalName, CalculationMode.Ticks, targetTicks);
        }

        // Each side picks its own sizing. In AtrBracket mode this is the control verbatim — including
        // honouring the control's own UseFixedStop/UseFixedTarget toggles, because equivalence means
        // equivalence at every setting, not only at the defaults. FixedTicks forces both sides fixed.
        private int StopTicksFor(double signalAtr)
        {
            if (BracketMode == KeelBracketMode.FixedTicks) return StopLossTicks;
            return UseFixedStop
                ? StopLossTicks
                : AtrDistanceToTicks(signalAtr, StopAtrMultiplier);
        }

        private int TargetTicksFor(double signalAtr)
        {
            if (BracketMode == KeelBracketMode.FixedTicks) return ProfitTargetTicks;
            return UseFixedTarget
                ? ProfitTargetTicks
                : AtrDistanceToTicks(signalAtr, TargetAtrMultiplier);
        }

        private int AtrDistanceToTicks(double atrValue, double multiplier)
        {
            if (TickSize <= 0.0)
                return 1;

            double rawTicks = atrValue * multiplier / TickSize;
            return Math.Max(1, (int)Math.Round(rawTicks, MidpointRounding.AwayFromZero));
        }

        // ═══ SENTINEL INSTRUMENTATION — every method below is a LEAF ══════════════════════════
        //  None of these may return a value the order path consults, mutate signal state, or throw.
        //  That is what makes the equivalence gate a property of the code rather than a hope.

        private string Scope()
        {
            if (_scope == null)
            {
                try { _scope = SentinelCore.ScopeOfLane(Instrument, BarsPeriod, ScopeLane); }
                catch (Exception _sx) { SentinelCore.Swallow("SentinelKeel.Scope", _sx); _scope = ""; }
            }
            return _scope;
        }

        /// <summary>&lt;class&gt;#&lt;scope&gt;@&lt;account&gt; — the identity a corpus row is traced back to.</summary>
        private string InstanceKey()
        {
            if (_instanceKey == null)
            {
                string s = Scope();
                if (string.IsNullOrEmpty(s))
                    s = Instrument != null && Instrument.MasterInstrument != null ? Instrument.MasterInstrument.Name : "?";
                string acct = Account != null ? Account.Name : "?";
                _instanceKey = Tag + "#" + s + "@" + acct;
            }
            return _instanceKey;
        }

        /// <summary>Stamp the live quote on the side about to be crossed. THE `intended` CONTRACT:
        /// a market order's reference price is the quote it crossed, captured at submission — not its
        /// own fill, which would make slip identically zero (fill − fill) and is a tautology, not a
        /// measurement. That is exactly why entry crossing cost had never once been recorded here.
        ///
        /// ⚠ Never Close[0]: on Tide/TBars/Flux/Renko that is the Heikin-Ashi synthetic average, a price
        /// that never traded, and it biased this project's entire corpus once already.
        /// Fails SOFT — 0 means "no defensible quote", and callers fall back rather than record a number
        /// they cannot stand behind.</summary>
        private void StampCross(int crossDir)
        {
            try
            {
                double q = crossDir > 0 ? GetCurrentAsk() : GetCurrentBid();
                _crossRefPx = q > 0 ? q : 0.0;
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelKeel.StampCross", _sx); _crossRefPx = 0.0; }
        }

        /// <summary>Record an entry: advisory Gate, Ledger order, corpus fire. Order of operations is
        /// deliberate — the Gate is consulted FIRST so its verdict is logged even if the Ledger fails.</summary>
        private void NoteEntry(int dir, int stopTicks, int targetTicks, double signalAtr)
        {
            double refPx = _crossRefPx > 0 ? _crossRefPx : Close[0];

            // ── 1. ADVISORY GATE ────────────────────────────────────────────────────────────────
            // Consulted, logged, and its answer DISCARDED. Blocking would change the trade set and
            // destroy the baseline the whole programme depends on. It ships blocking-capable so the
            // SAME build can be promoted to enforcing without a rebuild — but not in v0.1.0.
            // ⚠ riskDollars = 0 is load-bearing: pass a non-zero risk and GateEntry RE-SIZES from it and
            // can reject as "risk too small". This strategy has already sized itself, so the Gate is
            // asked to VALIDATE (kill / limits / session / feed), never to size. That flip is what
            // silently blocked the Bridge's first live trade.
            if (ConsultGate)
            {
                try
                {
                    var g = SentinelCore.GateEntry(Account, Instrument.FullName, ContractQuantity,
                                                   stopTicks, 0, Instrument);
                    // The Gate is TRI-state, and the distinction is what a future promotion turns on:
                    // Hard = a protective stop that WOULD have blocked this entry (kill switch, limits,
                    // session); Advisory = surfaced for a human, a manual trader may proceed. Counting
                    // only the Hard ones is what makes "how much would enforcement have cost us?"
                    // answerable from the bake instead of guessed at promotion time.
                    if (g != null && !g.IsClear)
                    {
                        if (g.IsHard) _nGateWouldBlock++;
                        SentinelCore.Log(Tag, "GATE-ADVISORY " + g.Level.ToString().ToUpperInvariant()
                            + (g.IsHard ? " — WOULD HAVE BLOCKED (not enforced in v" + KeelVersion + ")" : " — surfaced only")
                            + ": " + (g.Reason ?? "?") + " | " + (dir > 0 ? "LONG" : "SHORT")
                            + " qty=" + ContractQuantity + " stop=" + stopTicks + "t"
                            + " hardBlocks=" + _nGateWouldBlock);
                    }
                }
                catch (Exception _sx) { SentinelCore.Swallow("SentinelKeel.Gate", _sx); }
            }

            // ── 2. LEDGER ───────────────────────────────────────────────────────────────────────
            if (RecordToLedger)
            {
                try
                {
                    SentinelCore.Ledger.Order(Account.Name, Instrument.FullName,
                                              dir > 0 ? "Buy" : "SellShort", "Market",
                                              ContractQuantity, refPx, Tag, null, InstanceKey());
                }
                catch (Exception _sx) { SentinelCore.Swallow("SentinelKeel.Ledger", _sx); }
            }

            // ── 3. EXCURSION CORPUS ─────────────────────────────────────────────────────────────
            // The generic intake (Core ≥ v1.46.0). Before it existed this recorder could only ever see
            // COUNCIL verdicts, so a strategy was invisible to the corpus by construction.
            // ⚠ isHistorical is not politeness — a fire enqueued during a historical rebuild and drained
            // once the chart goes realtime is lookahead contamination.
            // ⚠ RECORD WHETHER THE CORPUS TOOK IT, not merely that we offered it. v0.1.0's first live
            // run logged 792 fires with zero corpus rows, and the log could not say whether that meant
            // "rejected as historical" (correct), "queue full" (a bug), or "the recorder never drained"
            // (a different bug). All three have the identical symptom. A fire log that omits the intake's
            // answer is the same defect as a market fill reporting intended = price: it records the
            // question and drops the answer.
            bool corpusTook = false;
            if (RecordExcursions)
            {
                try
                {
                    corpusTook = SentinelCore.NoteSignalFire(Scope(), dir, "KEEL", State != State.Realtime,
                                                refPx, InstanceKey(), Time[0].ToUniversalTime(),
                                                double.NaN, double.NaN,
                                                "atr=" + signalAtr.ToString("0.####", CultureInfo.InvariantCulture)
                                                + " stop=" + stopTicks + "t target=" + targetTicks + "t"
                                                + " bracket=" + BracketMode);
                }
                catch (Exception _sx) { SentinelCore.Swallow("SentinelKeel.NoteFire", _sx); }
            }

            _nFires++;
            if (LogFires)
            {
                try
                {
                    SentinelCore.Log(Tag, (dir > 0 ? "LONG " : "SHORT ") + ContractQuantity
                        + " @ref " + refPx.ToString("0.#####", CultureInfo.InvariantCulture)
                        + (_crossRefPx > 0 ? " (quote)" : " (barclose — tape silent, NOT a tradeable ref)")
                        + " stop=" + stopTicks + "t target=" + targetTicks + "t"
                        + " bracket=" + BracketMode
                        + " atr=" + signalAtr.ToString("0.####", CultureInfo.InvariantCulture)
                        + " fires=" + _nFires
                        + " corpus=" + (!RecordExcursions ? "off"
                                        : corpusTook ? "QUEUED"
                                        : State != State.Realtime ? "rejected(historical)"
                                        : "REFUSED"));   // REFUSED on a realtime fire = a real defect
                }
                catch (Exception _sx) { SentinelCore.Swallow("SentinelKeel.LogFire", _sx); }
            }
        }

        /// <summary>THE `intended` CONTRACT on the fill side: limit → LimitPrice, stop → StopPrice,
        /// market → the quote stamped at submission. A market fill that reports `intended = price`
        /// makes slip identically zero and is why entry crossing cost was never measured.</summary>
        private void RecordFill(Execution execution, double price, int quantity)
        {
            if (!RecordToLedger) return;
            if (execution == null || execution.Order == null) return;
            try
            {
                var o = execution.Order;
                double intended = price;
                if (o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit) intended = o.StopPrice;
                else if (o.OrderType == OrderType.Limit) intended = o.LimitPrice;
                else if (_crossRefPx > 0) intended = _crossRefPx;
                SentinelCore.Ledger.Fill(Account.Name, Instrument.FullName, o.OrderAction.ToString(),
                                         quantity, intended, price, TickSize, Tag, null, InstanceKey());
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelKeel.RecordFill", _sx); }
        }

        #region Properties

        [NinjaScriptProperty]
        [Range(1, 60)]
        [Display(Name = "Range Period", Description = "Sampling length for average close-to-close travel.", GroupName = "Range Filter", Order = 0)]
        public int RangePeriod
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 20.0)]
        [Display(Name = "Range Multiplier", Description = "Scales the smoothed close-to-close range.", GroupName = "Range Filter", Order = 1)]
        public double RangeMultiplier
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "ATR Period", Description = "ATR lookback used to size the stop and target.", GroupName = "Risk Management", Order = 0)]
        public int AtrPeriod
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 20.0)]
        [Display(Name = "Stop ATR Multiplier", Description = "Stop-loss distance as a multiple of ATR.", GroupName = "Risk Management", Order = 1)]
        public double StopAtrMultiplier
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 30.0)]
        [Display(Name = "Target ATR Multiplier", Description = "Profit-target distance as a multiple of ATR.", GroupName = "Risk Management", Order = 2)]
        public double TargetAtrMultiplier
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Fixed Stop (SL)", Description = "Use a fixed tick stop instead of the ATR multiple. Applies in AtrBracket mode (matching the control); FixedTicks forces it on and None ignores it.", GroupName = "Risk Management", Order = 3)]
        public bool UseFixedStop
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Stop Loss Ticks", Description = "Fixed stop-loss distance in ticks. Used when Use Fixed Stop is checked, or in FixedTicks bracket mode.", GroupName = "Risk Management", Order = 4)]
        public int StopLossTicks
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Fixed Target (TP)", Description = "Use a fixed tick profit target instead of the ATR multiple. Applies in AtrBracket mode (matching the control); FixedTicks forces it on and None ignores it.", GroupName = "Risk Management", Order = 5)]
        public bool UseFixedTarget
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Profit Target Ticks", Description = "Fixed profit-target distance in ticks. Used when Use Fixed Target is checked, or in FixedTicks bracket mode.", GroupName = "Risk Management", Order = 6)]
        public int ProfitTargetTicks
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Bracket Mode", Description = "AtrBracket = the control's behaviour exactly (honours the fixed-stop/target toggles above) — the equivalence baseline. FixedTicks = force both sides to the fixed tick distances. None = FLIP-TO-FLIP: no stop and no target, the position reverses only on the next fresh flip. ⚠ None is a MEASUREMENT mode that isolates the signal from the exit policy; it carries unbounded per-trade risk and must never run on a funded or evaluation account.", GroupName = "Keel", Order = 0)]
        public KeelBracketMode BracketMode
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "Contract Quantity", Description = "Number of futures contracts per entry.", GroupName = "Order Settings", Order = 0)]
        public int ContractQuantity
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Long Entries", Description = "Allow long entries. Uncheck to run short-only.", GroupName = "Order Settings", Order = 1)]
        public bool EnableLongEntries
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Short Entries", Description = "Allow short entries. Uncheck to run long-only.", GroupName = "Order Settings", Order = 2)]
        public bool EnableShortEntries
        { get; set; }

        // ── Sentinel (observation only) ───────────────────────────────────────────────────────
        // NOT [NinjaScriptProperty]: these serialize to the workspace and show in F6, but stay out of
        // the constructor so they can be added or removed without moving the strategy's identity.

        [Display(Name = "Record to Ledger", Description = "Write Ledger.Order at submission and Ledger.Fill from OnExecutionUpdate, obeying the `intended` contract — so entry crossing cost is measured from the first fill rather than assumed.", GroupName = "Sentinel", Order = 0)]
        public bool RecordToLedger
        { get; set; }

        [Display(Name = "Record excursions", Description = "Publish each entry to the excursion corpus via SentinelCore.NoteSignalFire, tagged KEEL. Requires a SentinelExcursionRecorder on a chart of the same scope, with 'Record external fires' ON. Needs SentinelCore ≥ v1.46.0.", GroupName = "Sentinel", Order = 1)]
        public bool RecordExcursions
        { get; set; }

        [Display(Name = "Consult Gate (advisory)", Description = "Consult SentinelCore.GateEntry at each entry and LOG what it would have decided, without acting on it. ⚠ Advisory only in v0.1.0 — a blocking gate would change the trade set and destroy the baseline the equivalence gate depends on.", GroupName = "Sentinel", Order = 2)]
        public bool ConsultGate
        { get; set; }

        [Display(Name = "Scope Lane", Description = "Lane tag appended to the scope, so two same-instrument same-bartype charts record into distinguishable lanes. Blank = bare scope.", GroupName = "Sentinel", Order = 3)]
        public string ScopeLane
        { get; set; }

        [Display(Name = "Log fires", Description = "Write one line per entry to sentinel.log (direction, reference price and whether it came from a real quote, bracket distances, ATR).", GroupName = "Sentinel", Order = 4)]
        public bool LogFires
        { get; set; }

        #endregion
    }
}
