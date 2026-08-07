// ─────────────────────────────────────────────────────────────────────────────
// This Source Code Form is subject to the terms of the Mozilla Public License,
// v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
// one at https://mozilla.org/MPL/2.0/.
//
// Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
// ─────────────────────────────────────────────────────────────────────────────
// ═════════════════════════════════════════════════════════════════════════════
//  SentinelEffort — EFFICIENCY bars type: distance vs the EFFORT it cost (Sentinel Suite)
//  File: SentinelEffort_v1_0_0.cs       Class/Type: SentinelEffort_v1_0_0
//  Display Name: "SentinelEffort v1.0.0"  ·  BarsPeriodType id: 212206 (Sentinel block 212200–212299)
// ─────────────────────────────────────────────────────────────────────────────
//  WHAT THIS IS
//    A brick closes on WHICHEVER COMES FIRST:
//        • DISPLACEMENT — price travelled `Brick Ticks`            ⇒ a SWEEP, and
//        • EFFORT       — `E*` contracts traded while it tried     ⇒ ABSORPTION.
//    **Which condition fired IS the signal.** Price moved the same distance in both
//    cases only in the first; in the second the tape spent the contracts and went
//    nowhere. So the BRICK'S OWN SIZE becomes a readout of market efficiency:
//        full-size brick  = cheap movement (thin book swept)
//        stunted brick    = expensive movement (absorbed into resting size)
//    Read a chart of these and absorption is visible as a run of short, dense bricks
//    without needing the book.
//
//  WHY IT IS A GENUINELY DIFFERENT AXIS
//    SentinelTBars / SentinelDrift clock on PRICE DISTANCE. SentinelFlux clocks on
//    SIGNED flow (which side is pressing). This clocks on the UNSIGNED COST of movement.
//    Distance, direction, and cost are three different questions — and cost is the one
//    no existing Sentinel bar type asks.
//
//  ⚠ SCOPE CHANGE FROM THE ORIGINAL BRIEF (2026-07-25) — and why
//    The brief said "a brick closes when price has CONSUMED N CONTRACTS OF RESTING
//    LIQUIDITY". That is NOT OBSERVABLE from inside a bars type, and it was verified
//    rather than assumed: `OnDataPoint(bars, o,h,l,c, time, volume, isBar, bid, ask)`
//    is **L1 only**; NinjaTrader's BarsType API exposes no `OnMarketDepth`, and no bars
//    type in this tree reaches L2. Resting liquidity is BOOK state — a bars type sees
//    trades and the touch, never the ladder. Rather than fake it, the definition moved
//    to what the tape genuinely shows: contracts SPENT versus ticks GAINED. The economic
//    question (sweep or absorption?) survives intact; only the instrument changed.
//    A true book-depth version belongs in an indicator with `OnMarketDepth`
//    (cf. LiquidityWalls), publishing a seam — it cannot be a bar clock.
//
//  SELF-CALIBRATING E*  (the Flux lesson, reused)
//    E* = EffortMult × EWMA(contracts per completed brick), so "expensive" is defined
//    relative to THIS instrument and THIS session, not a hardcoded lot count. The EWMA
//    input is WINSORIZED at WinsorMult× the running estimate because a single block
//    trade would otherwise redefine "typical" — Flux shipped that bug live (a ~2000-lot
//    print spiked its threshold to 149) and it is not worth learning twice.
//
//  DETERMINISM — HONEST STATEMENT
//    This bar type is PATH-DEPENDENT, like TBars and Flux: E* is a carried EWMA, so the
//    load point can shift brick boundaries. That is a real cost and it is stated rather
//    than glossed. If you need provable replay ≡ live, use **SentinelLattice** (212205),
//    which is built for exactly that and gives up adaptivity to get it. The two are
//    deliberate opposites and the pair is the experiment.
//
//  PUBLISHES SentinelCore.BrickState under its OWN scope (own bar-type id ⇒ own bartag ⇒
//    no collision with TBars/Drift/Lattice) → the Council BRK voter. Slot mapping, in the
//    Drift tradition of reusing the seam rather than growing Core:
//        densityScale ← EFFICIENCY (1.0 = typical cost; <1 expensive/absorbing; >1 cheap/sweeping)
//        pendingBreakout ← true when the LAST brick closed on EFFORT (absorption)
//    No Core edit, no Core version bump.
//
//  CHANGELOG
//    v1.0.0 (2026-07-25) — first release. Dual-condition close (displacement | effort),
//                          self-calibrating winsorized E*, efficiency on the seam,
//                          real (non-HA) bodies, time/tick backstops.
// ═════════════════════════════════════════════════════════════════════════════

using System;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.AddOns.Sentinel;   // SentinelCore.BrickState publish seam

namespace NinjaTrader.NinjaScript.BarsTypes
{
    public class SentinelEffort_v1_0_0 : BarsType
    {
        // RESERVED Sentinel bars block = 212200–212299. 212201 TBars · 212202 TbarsCount · 212203 Flux ·
        // 212204 Drift · 212205 Lattice · 212206 = SentinelEffort (this).
        private const int CustomBarsPeriodTypeValue = 212206;

        private const int EffortRefSize = 10;           // default displacement target in TICKS

        // ── Threshold shaping ──
        private double EffortMult   = 1.0;              // E* = EffortMult × EWMA(contracts per brick)
        private int    EffortLen    = 50;               // EWMA length for E[contracts per brick]
        private double WinsorMult   = 4.0;              // block-trade guard on the EWMA input (Flux's lesson)
        private int    AtrLength    = 14;

        // ── Physical backstops (guarantee termination) ──
        // A dead tape trades nothing, so NEITHER close condition can fire — unlike a pure price clock this
        // one genuinely needs a time backstop for termination, not merely for tidiness.
        private int    ForceStagnationSecs = 120;
        private long   MaxTicksPerBar      = 5000;

        private const double RealtimePublishMinutes = 5.0;
        private const double LogThrottleSeconds     = 10.0;
        private DateTime _lastLog;

        // ── Dynamic state ──
        private double tickSize   = 0.01;
        private int    brickTicks = EffortRefSize;
        private double brickPrice = 0.10;

        // forming brick
        private double barOpen, wickHigh, wickLow, lastClose;
        private double effort;                          // contracts traded in the forming brick
        private long   nTicks;
        private DateTime birthTime;

        // EWMAs / carries (updated once per CLOSED brick)
        private double effortEwma;                      // E[contracts per completed brick]
        private double atrEma;
        private int    _dir;
        private int    sameDirCount;
        private bool   lastWasAbsorption;
        private double lastEfficiency = 1.0;
        private bool   warmup = true;

        private int    barsThisSession;
        private DateTime sessionStart;

        private double AtrAlpha    => 2.0 / (AtrLength + 1.0);
        private double EffortAlpha => 2.0 / (EffortLen + 1.0);

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "SentinelEffort v1.0.0 — EFFICIENCY bricks: a brick closes on displacement (sweep) OR on effort spent (absorption), whichever comes first. Brick size reads as cost of movement. Publishes SentinelCore.BrickState.";
                Name        = "SentinelEffort v1.0.0";
                BarsPeriod  = new BarsPeriod { BarsPeriodType = (BarsPeriodType)CustomBarsPeriodTypeValue, BarsPeriodTypeName = Name };
                BuiltFrom   = BarsPeriodType.Tick;      // per-trade volume + true timestamps
                DaysToLoad  = 5;
                IsIntraday  = true;
            }
            else if (State == State.Configure)
            {
                SafeRemoveProperty("BaseBarsPeriodType");
                SafeRemoveProperty("PointAndFigurePriceType");
                SafeRemoveProperty("ReversalType");
                SafeRemoveProperty("Value");
                SafeRemoveProperty("Value2");
                SetPropertyName("BaseBarsPeriodValue", "Brick Ticks");
                BarsPeriod.Value  = BarsPeriod.BaseBarsPeriodValue;
                BarsPeriod.Value2 = 0;
            }
        }

        public override int GetInitialLookBackDays(BarsPeriod barsPeriod, TradingHours tradingHours, int barsBack) => 3;

        protected override void OnDataPoint(Bars bars, double open, double high, double low, double close,
            DateTime time, long volume, bool isBar, double bid, double ask)
        {
            if (SessionIterator == null)
                SessionIterator = new SessionIterator(bars);

            bool newSession = SessionIterator.IsNewSession(time, isBar);
            if (newSession)
            {
                SessionIterator.CalculateTradingDay(time, isBar);
                sessionStart    = time;
                barsThisSession = 0;
            }

            if (bars.Count == 0 || (newSession && bars.IsResetOnNewTradingDay))
            {
                LatchConfig(bars);
                InitializeFirstBar(bars, close, time, volume);
                bars.LastPrice = close;
                return;
            }

            if (newSession) LatchConfig(bars);

            // ── accumulate ──
            effort += Math.Max(0, volume);
            nTicks++;
            if (close > wickHigh) wickHigh = close;
            if (close < wickLow)  wickLow  = close;
            lastClose = close;

            UpdateFormingBar(bars, time, volume);

            // ── the dual close test ──
            double dispTicks = tickSize > 0 ? Math.Abs(close - barOpen) / tickSize : 0;
            double eStar     = CurrentEffortThreshold();
            double barAge    = (time - birthTime).TotalSeconds;

            bool bySweep  = dispTicks >= brickTicks;
            bool byEffort = !warmup && effort >= eStar && dispTicks >= 1;   // needs SOME move: a 0-tick brick is not a brick
            bool byTime   = barAge >= ForceStagnationSecs && dispTicks >= 1;
            bool byTicks  = nTicks >= MaxTicksPerBar && dispTicks >= 1;

            // Gate the soft closes on ≥2 trades so a single tick can never mint a brick on coarse replay data.
            if ((bySweep || ((byEffort || byTime || byTicks) && nTicks >= 2)))
                CloseBrick(bars, close, time, volume, bySweep, byEffort);

            bars.LastPrice = close;
            PublishBrickTick(bars, close, time);
        }

        /// <summary>E* — the contracts a brick "should" cost, learned from this instrument's own tape.
        /// Returns MaxValue during warmup so the effort condition cannot fire before it means anything.</summary>
        private double CurrentEffortThreshold()
        {
            if (warmup || effortEwma <= 0) return double.MaxValue;
            return EffortMult * effortEwma;
        }

        private void LatchConfig(Bars bars)
        {
            tickSize   = bars.Instrument.MasterInstrument.TickSize;
            brickTicks = Math.Max(1, bars.BarsPeriod.BaseBarsPeriodValue);
            brickPrice = brickTicks * tickSize;
        }

        private void InitializeFirstBar(Bars bars, double close, DateTime time, long volume)
        {
            barOpen = wickHigh = wickLow = lastClose = close;
            effort  = Math.Max(0, volume);
            nTicks  = 1;
            birthTime = time;
            double seed = RoundToTick(close, bars);
            AddBar(bars, seed, seed, seed, seed, time, volume);
            barsThisSession++;
        }

        private void CloseBrick(Bars bars, double close, DateTime time, long volume, bool bySweep, bool byEffort)
        {
            int dir = close > barOpen ? 1 : (close < barOpen ? -1 : _dir);
            double dispTicks = tickSize > 0 ? Math.Abs(close - barOpen) / tickSize : 0;

            // EFFICIENCY: ticks gained per contract spent, normalised so 1.0 == typical for this instrument.
            // >1 cheap (swept) · <1 expensive (absorbed). Guarded against a zero-effort brick.
            double perTick = dispTicks > 0 && effort > 0 ? effort / dispTicks : 0;
            double refPerTick = effortEwma > 0 && brickTicks > 0 ? effortEwma / brickTicks : 0;
            lastEfficiency = (perTick > 0 && refPerTick > 0) ? refPerTick / perTick : 1.0;
            lastWasAbsorption = byEffort && !bySweep;

            // Real body — NOT Heikin-Ashi. An HA close is a price that never traded, and this suite has
            // already paid for that once ([[firepx-is-synthetic-ha-close]]).
            double hi = Math.Max(wickHigh, Math.Max(barOpen, close));
            double lo = Math.Min(wickLow,  Math.Min(barOpen, close));
            UpdateBar(bars, RoundToTick(hi, bars), RoundToTick(lo, bars), RoundToTick(close, bars), time, volume);

            // ── EWMAs, once per CLOSED brick ──
            double tr = Math.Abs(hi - lo);
            atrEma = atrEma <= 0 ? tr : atrEma + AtrAlpha * (tr - atrEma);

            // WINSORIZE the effort input: one block trade must not redefine "typical" (Flux shipped that bug).
            double contribution = effort;
            if (effortEwma > 0) contribution = Math.Min(contribution, WinsorMult * effortEwma);
            effortEwma = effortEwma <= 0 ? contribution : effortEwma + EffortAlpha * (contribution - effortEwma);
            if (warmup && barsThisSession >= 5 && effortEwma > 0) warmup = false;

            sameDirCount = (dir == _dir) ? sameDirCount + 1 : 1;
            _dir = dir;

            LogBrick(bars, dir, bySweep, byEffort, dispTicks, time);

            // open the next brick at the close — no gap
            double seed = RoundToTick(close, bars);
            AddBar(bars, seed, seed, seed, seed, time, volume);
            barsThisSession++;
            barOpen = wickHigh = wickLow = lastClose = close;
            effort  = 0;
            nTicks  = 0;
            birthTime = time;
        }

        private void UpdateFormingBar(Bars bars, DateTime time, long volume)
        {
            double hi = Math.Max(wickHigh, Math.Max(barOpen, lastClose));
            double lo = Math.Min(wickLow,  Math.Min(barOpen, lastClose));
            UpdateBar(bars, RoundToTick(hi, bars), RoundToTick(lo, bars), RoundToTick(lastClose, bars), time, volume);
        }

        private void PublishBrickTick(Bars bars, double close, DateTime time)
        {
            try
            {
                if (!SentinelCore.ReplayMode &&
                    (Core.Globals.Now - time).TotalMinutes > RealtimePublishMinutes) return;

                string scope = SentinelCore.ScopeOf(bars.Instrument, bars.BarsPeriod);
                string inst  = bars.Instrument.MasterInstrument.Name;

                double upper = barOpen + brickPrice, lower = barOpen - brickPrice;
                double toUp  = tickSize > 0 ? (upper - close) / tickSize : 0;
                double toDn  = tickSize > 0 ? (close - lower) / tickSize : 0;

                // Slot mapping (documented in the header): densityScale ← efficiency,
                // pendingBreakout ← last brick closed on EFFORT (absorption).
                SentinelCore.SetBrickState(scope, SentinelCore.BarTag(bars.BarsPeriod), inst,
                                           _dir, atrEma, brickPrice, brickPrice,
                                           lastEfficiency, sameDirCount, barsThisSession, lastWasAbsorption,
                                           upper, lower, toUp, toDn, Math.Min(toUp, toDn), "SentinelEffort");

                SentinelCore.Beacon(scope, "BRK");
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelEffort.PublishBrickTick", _sx); }
        }

        private void LogBrick(Bars bars, int dir, bool bySweep, bool byEffort, double dispTicks, DateTime time)
        {
            try
            {
                if (!SentinelCore.ReplayMode &&
                    (Core.Globals.Now - time).TotalMinutes > RealtimePublishMinutes) return;
                if ((DateTime.UtcNow - _lastLog).TotalSeconds < LogThrottleSeconds) return;
                _lastLog = DateTime.UtcNow;

                string why = bySweep ? "SWEEP" : (byEffort ? "ABSORB" : "backstop");
                SentinelCore.Log("Effort", string.Format(
                    "{0} {1} · {2} · {3:0.0}t on {4:0} lots · eff {5:0.00}x · E* {6:0} · run {7} · {8} bricks",
                    bars.Instrument.MasterInstrument.Name, dir > 0 ? "up" : "dn", why,
                    dispTicks, effort, lastEfficiency,
                    warmup ? 0 : EffortMult * effortEwma, sameDirCount, barsThisSession));
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelEffort.LogBrick", _sx); }
        }

        // ── Overrides ──
        public override void ApplyDefaultBasePeriodValue(BarsPeriod period) => period.BaseBarsPeriodValue = EffortRefSize;
        public override void ApplyDefaultValue(BarsPeriod period)
        {
            period.Value               = EffortRefSize;
            period.Value2              = 0;
            period.BaseBarsPeriodValue = EffortRefSize;
        }

        public override string ChartLabel(DateTime dateTime) => Name;

        /// <summary>Whichever close condition is closer — the bar really is that far along.</summary>
        public override double GetPercentComplete(Bars bars, DateTime now)
        {
            double byDisp = brickTicks > 0 && tickSize > 0
                          ? Math.Abs(lastClose - barOpen) / tickSize / brickTicks : 0;
            double eStar  = CurrentEffortThreshold();
            double byEff  = (eStar > 0 && eStar != double.MaxValue) ? effort / eStar : 0;
            return Math.Max(0.0, Math.Min(1.0, Math.Max(byDisp, byEff)));
        }

        // ── Utilities ──
        private double RoundToTick(double price, Bars bars) => bars.Instrument.MasterInstrument.RoundToTickSize(price);
        private void SafeRemoveProperty(string name) { var p = Properties.Find(name, true); if (p != null) Properties.Remove(p); }
    }
}
