// ─────────────────────────────────────────────────────────────────────────────
// This Source Code Form is subject to the terms of the Mozilla Public License,
// v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
// one at https://mozilla.org/MPL/2.0/.
//
// Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
// ─────────────────────────────────────────────────────────────────────────────
// ═════════════════════════════════════════════════════════════════════════════
//  SentinelTide — bars clocked by CUMULATIVE ORDER FLOW, not by price or time
//  File: SentinelTide_v1_0_0.cs      Class/Type: SentinelTide_v1_0_0
//  Display Name: "SentinelTide v1.0.0"  ·  BarsPeriodType id: 212207 (Sentinel block 212200–212299)
// ─────────────────────────────────────────────────────────────────────────────
//  THE IDEA IN ONE LINE
//    Every bar contains EXACTLY the same quantum of net aggression — so the bar's HEIGHT
//    is a direct, readable measure of MARKET IMPACT.
//
//  HOW IT WORKS
//    Session cumulative volume delta (CVD) runs on a fixed lattice:
//
//        cvdLine(k) = k × deltaPerBrick        (k ∈ ℤ, anchored at session CVD = 0)
//
//    A bar closes the moment CVD crosses an adjacent line. Price is NOT the clock — price
//    is the OBSERVATION. Each bar answers one question: "the market just absorbed N
//    contracts of net aggressive buying (or selling) — how far did price actually go?"
//
//  ⭐ WHY THAT IS WORTH A NEW BAR TYPE — the thing you can SEE and nothing else shows
//    Bar HEIGHT ∝ price impact per unit flow. That is Kyle's lambda, rendered.
//      • A SHORT bar  = heavy flow moved price barely at all ⇒ someone is filling into it
//                       (absorption / a real participant defending a level).
//      • A TALL bar   = the same flow travelled a long way ⇒ a thin book; the move is cheap
//                       to push and cheap to fade.
//    On every other bar type in this suite (and every one retail uses) a bar's height is a
//    function of PRICE — so it tells you what already happened. Here it is a function of
//    price PER UNIT OF FLOW, which is a liquidity measurement.
//
//  ⭐ AND THE SECOND READ — direction and body can DISAGREE, on purpose
//    A bar's DIRECTION is the flow that closed it (which lattice line was crossed).
//    A bar's BODY is where price actually went.
//    They are independent, and the disagreement is the signal: a flow-UP bar that closes
//    DOWN is absorption made visual — aggressive buyers spent N contracts and finished
//    lower than they started. No conventional chart can render that, because on a
//    conventional chart the bar's direction IS its body by construction.
//
//  DELIBERATE REFUSALS (each is a feature; cf. SentinelLattice's three)
//    1. NO PRICE TERM IN THE CLOSE RULE. The instant price can close a bar, height stops
//       being a clean impact measure and the whole point is gone. Price only ever gets to
//       be the observation. (Physical backstops below are escapes, not price rules.)
//    2. NO ADAPTIVE BRICK SIZE. A moving quantum is a moving lattice — same argument that
//       keeps SentinelLattice rigid. Bars from different sessions must be comparable, and
//       they are only comparable if the flow quantum is identical.
//    3. NO SEAM PUBLISH — Tide is a PURE CLOCK. Every bar-type seam in this suite has been
//       a source of the F5-decoupling bug class ([[f5-decouples-bartype-seams]]): the
//       chart's bars-type instance survives a recompile on the OLD assembly and publishes
//       into an orphaned static store while consumers read the new one. Tide has nothing
//       to say that SentinelCVD — an ordinary indicator, running on top of it, immune to
//       that failure — cannot say better. One fewer publisher is one fewer silent seam.
//
//  ⚠ HONEST LIMITS — read before trusting a bar
//    • CVD is an ESTIMATOR. Quote rule where a real bid/ask exists, tick rule otherwise.
//      "Same tape ⇒ same bars" holds only for the SAME SIGNING RULE — this is weaker
//      determinism than SentinelLattice's price lattice, which needs no estimator at all.
//      Stated plainly because the difference matters when comparing the two.
//    • CVD measures WHO CROSSED THE SPREAD, not net positioning. Every contract has both.
//    • NEEDS TICK DATA. Without it, signing degrades to a bar-body proxy and the impact
//      read is materially weaker. `TideDbg` logs which path is live; do not judge a
//      no-tick chart by eye and conclude the bar type is broken.
//    • Block prints are winsorized (a single print capped at N× its EWMA). SentinelFlux
//      learned this the expensive way — one block spiked its threshold to 149 and left the
//      clock dormant for hours.
//    • A flat, balanced tape prints FEW bars. That is correct, not a stall: no net
//      aggression means nothing to measure. The time backstop exists so the chart still
//      advances, and a backstop-born bar is marked in the log as such.
//
//  BRING-UP GATES — run these before judging it (cf. LATTICEBARS_SPEC §9)
//    G1  Load on GC with tick replay ON. `[Sentinel:Tide]` must log `tape=quote`, not `bar-proxy`.
//    G2  Bar count per session should scale ~1/deltaPerBrick. Halve the size ⇒ ~2× the bars.
//    G3  Every bar's |ΔCVD| must equal deltaPerBrick (± one print). Logged per bar as `dCvd`.
//    G4  Find one SHORT bar and one TALL bar; confirm by eye on a time chart that the short
//        one sits at a level where price stalled. If height does not track absorption, the
//        premise is wrong and it should be said out loud rather than tuned around.
//    G5  Reload the chart from a different start date; bar boundaries within a session must
//        be identical (the lattice is session-anchored, so this is checkable).
//
//  CHANGELOG
//    v1.0.0 (2026-07-25) — initial. CVD-lattice clock, quote-rule signing with tick-rule
//             fallback + winsorized prints, flow-direction bars with independent price body,
//             time/tick backstops, no seam publish (pure clock by design).
// ═════════════════════════════════════════════════════════════════════════════
#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.AddOns.Sentinel;   // SentinelCore (log + ReplayMode)
#endregion

namespace NinjaTrader.NinjaScript.BarsTypes
{
    public class SentinelTide_v1_0_0 : BarsType
    {
        // RESERVED Sentinel bars block = 212200–212299. 212201 TBars · 212202 TbarsCount · 212203 Flux ·
        // 212204 Drift · 212205 Lattice · 212206 Effort · 212207 = SentinelTide (this).
        private const int CustomBarsPeriodTypeValue = 212207;

        private const int TideRefSize = 250;            // default net-delta contracts per bar (BaseBarsPeriodValue)

        // Physical backstops — escapes so a dead tape cannot freeze the chart. NOT price rules (refusal #1).
        private const double TimeBackstopMinutes = 30.0;
        private const long   TickBackstop        = 20000;
        private const double WinsorMult          = 4.0;   // clamp one print at N× its EWMA (block-trade guard)

        private const double RealtimeLogMinutes = 5.0;
        private const double LogThrottleSeconds = 10.0;
        private DateTime _lastLog;

        // ── config, latched per session ──
        private double tickSize      = 0.01;
        private double deltaPerBrick = TideRefSize;

        // ── tape state ──
        private double _cvd;              // session cumulative signed delta
        private double _barCvdOpen;       // CVD at the current bar's open (its lattice line)
        private long   _level;            // lattice index of the last confirmed CVD line
        private double _prevBid, _prevAsk;
        private double _lastPrice;
        private int    _lastTickSign;
        private double _volEwma;
        private bool   _sawTape;

        // ── forming-bar accumulators ──
        private double _open, _high, _low;
        private long   _nTicks;
        private DateTime _birthTime;
        private int    _barsThisSession;
        private int    _dir;              // flow direction that closed the last bar

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "SentinelTide v1.0.0 — bars clocked by CUMULATIVE ORDER FLOW. Every bar holds the same quantum of net aggression, so bar HEIGHT is a direct read on market impact: short = absorbed, tall = thin book. Bar direction is FLOW; the body is PRICE — and when they disagree, that is the signal. Pure clock: publishes no seam.";
                Name        = "SentinelTide v1.0.0";
                BarsPeriod  = new BarsPeriod { BarsPeriodType = (BarsPeriodType)CustomBarsPeriodTypeValue, BarsPeriodTypeName = Name };
                BuiltFrom   = BarsPeriodType.Tick;      // signing needs true ticks
                DaysToLoad  = 5;
                IsIntraday  = true;
            }
            else if (State == State.Configure)
            {
                // One knob, mirroring SentinelFlux's "Flux Size" and Lattice's "Brick Ticks".
                SafeRemoveProperty("BaseBarsPeriodType");
                SafeRemoveProperty("PointAndFigurePriceType");
                SafeRemoveProperty("ReversalType");
                SafeRemoveProperty("Value");
                SafeRemoveProperty("Value2");
                SetPropertyName("BaseBarsPeriodValue", "Net Delta Per Bar");
                // Encode size into Value (hidden) so the SCOPE tag separates a sweep (GC.212207v250 vs v500).
                BarsPeriod.Value  = BarsPeriod.BaseBarsPeriodValue;
                BarsPeriod.Value2 = 0;
            }
        }

        public override int GetInitialLookBackDays(BarsPeriod barsPeriod, TradingHours tradingHours, int barsBack) => 3;

        public override void ApplyDefaultBasePeriodValue(BarsPeriod period) => period.BaseBarsPeriodValue = TideRefSize;
        public override void ApplyDefaultValue(BarsPeriod period)           => period.Value = TideRefSize;

        public override string ChartLabel(DateTime time) => time.ToString("HH:mm:ss");

        public override double GetPercentComplete(Bars bars, DateTime now)
        {
            double span = Math.Abs(_cvd - _barCvdOpen);
            return deltaPerBrick > 0 ? Math.Max(0, Math.Min(1, span / deltaPerBrick)) : 0;
        }

        protected override void OnDataPoint(Bars bars, double open, double high, double low, double close,
            DateTime time, long volume, bool isBar, double bid, double ask)
        {
            if (SessionIterator == null)
                SessionIterator = new SessionIterator(bars);

            bool newSession = SessionIterator.IsNewSession(time, isBar);
            if (newSession)
            {
                SessionIterator.CalculateTradingDay(time, isBar);
                _barsThisSession = 0;
            }

            if (bars.Count == 0 || (newSession && bars.IsResetOnNewTradingDay))
            {
                LatchConfig(bars);
                ResetSession(close, time);
                AddBar(bars, RoundToTick(close, bars), RoundToTick(close, bars),
                             RoundToTick(close, bars), RoundToTick(close, bars), time, volume);
                _barsThisSession++;
                bars.LastPrice = close;
                return;
            }

            if (newSession) { LatchConfig(bars); ResetSession(close, time); }

            // ── sign this print into the session CVD ──
            AccumulateFlow(close, volume, bid, ask, isBar, open);

            if (close > _high) _high = close;
            if (close < _low)  _low  = close;
            _nTicks++;

            // ── THE TIDE RULE — one bar per CVD lattice line crossed, in order ──
            // A loop, not a single jump: a burst that carries CVD through three lines prints three
            // bars, so no bar ever contains more than one quantum of flow. That invariant is the
            // entire basis for comparing bar heights, so it is enforced structurally, not assumed.
            int guard = 0;
            while (guard++ < 10000)
            {
                double up   = LineAt(_level + 1);
                double down = LineAt(_level - 1);

                if (_cvd >= up)        { CloseBar(bars, close, time, volume, +1, "flow"); _level += 1; continue; }
                if (_cvd <= down)      { CloseBar(bars, close, time, volume, -1, "flow"); _level -= 1; continue; }
                break;
            }

            // ── physical backstops (escapes, never price rules) ──
            bool timeHit = (time - _birthTime).TotalMinutes >= TimeBackstopMinutes;
            bool tickHit = _nTicks >= TickBackstop;
            if (timeHit || tickHit)
            {
                // A backstop bar does NOT carry a full flow quantum, so its height is NOT a valid
                // impact reading. It is logged as such; the Lab must exclude it before grading.
                CloseBar(bars, close, time, volume, 0, timeHit ? "time" : "tick");
                _level = LatticeIndex(_cvd);
            }

            UpdateBar(bars, RoundToTick(_high, bars), RoundToTick(_low, bars), RoundToTick(close, bars), time, volume);
            bars.LastPrice = close;
        }

        private double LineAt(long k) => k * deltaPerBrick;
        private long LatticeIndex(double cvd) => (long)Math.Floor(cvd / deltaPerBrick + 1e-9);

        private void LatchConfig(Bars bars)
        {
            tickSize      = bars.Instrument.MasterInstrument.TickSize;
            deltaPerBrick = Math.Max(1, bars.BarsPeriod.BaseBarsPeriodValue);
        }

        private void ResetSession(double close, DateTime time)
        {
            _cvd = 0; _level = 0; _barCvdOpen = 0;
            _open = _high = _low = close;
            _nTicks = 0; _birthTime = time; _dir = 0;
            _sawTape = false;
        }

        /// <summary>Quote rule first (a real bid/ask is the better estimator), tick rule as fallback —
        /// the SAME signing SentinelFlux and SentinelDrift use. Three tools disagreeing about what a
        /// "buy" is would be worse than any one of them being slightly wrong.</summary>
        private void AccumulateFlow(double close, long volume, double bid, double ask, bool isBar, double open)
        {
            if (volume <= 0) return;

            if (bid > 0) _prevBid = bid;
            if (ask > 0) _prevAsk = ask;

            double vol = volume;
            _volEwma = _volEwma <= 0 ? vol : _volEwma + 0.02 * (vol - _volEwma);
            double cap = _volEwma * WinsorMult;
            if (cap > 0 && vol > cap) vol = cap;

            int sign;
            if (!isBar && _prevAsk > 0 && _prevBid > 0 && _prevAsk > _prevBid)
            {
                _sawTape = true;
                if (close >= _prevAsk)      sign =  1;
                else if (close <= _prevBid) sign = -1;
                else                        sign =  0;    // inside the spread — genuinely ambiguous, count neither
            }
            else if (!isBar)
            {
                _sawTape = true;
                if (_lastPrice > 0 && close > _lastPrice)      sign =  1;
                else if (_lastPrice > 0 && close < _lastPrice) sign = -1;
                else                                          sign = _lastTickSign;
            }
            else
            {
                // Degraded: a BAR, not a tick. Proxy the bar's delta from its body and say so in the log.
                double rng = Math.Max(_high - _low, tickSize);
                sign = 0;
                _cvd += vol * Math.Max(-1.0, Math.Min(1.0, (close - open) / rng));
            }

            if (sign != 0) { _lastTickSign = sign; _cvd += sign * vol; }
            _lastPrice = close;
        }

        private void CloseBar(Bars bars, double close, DateTime time, long volume, int flowDir, string reason)
        {
            UpdateBar(bars, RoundToTick(_high, bars), RoundToTick(_low, bars), RoundToTick(close, bars), time, volume);

            double dCvd    = _cvd - _barCvdOpen;
            double heightT = (_high - _low) / Math.Max(tickSize, 1e-9);
            double bodyT   = (close - _open) / Math.Max(tickSize, 1e-9);
            // IMPACT — the number this bar type exists to produce. Ticks of price per 1,000 contracts
            // of net aggression. Comparable across bars BECAUSE every bar carries the same flow quantum.
            double impact  = Math.Abs(dCvd) > 1 ? heightT / (Math.Abs(dCvd) / 1000.0) : 0;

            LogBar(bars, flowDir, bodyT, heightT, dCvd, impact, reason, time);

            _dir = flowDir != 0 ? flowDir : _dir;

            AddBar(bars, RoundToTick(close, bars), RoundToTick(close, bars),
                         RoundToTick(close, bars), RoundToTick(close, bars), time, volume);
            _barsThisSession++;

            _open = _high = _low = close;
            _barCvdOpen = _cvd;
            _nTicks = 0;
            _birthTime = time;
        }

        private void LogBar(Bars bars, int flowDir, double bodyT, double heightT, double dCvd,
                            double impact, string reason, DateTime time)
        {
            try
            {
                if (!SentinelCore.ReplayMode && (Core.Globals.Now - time).TotalMinutes > RealtimeLogMinutes) return;
                // WALL-CLOCK throttle, not bar time: bar time advances days per real second on a rebuild,
                // so a bar-time throttle throttles nothing (measured: 11,164 Flux lines in 27s).
                if ((DateTime.UtcNow - _lastLog).TotalSeconds < LogThrottleSeconds) return;
                _lastLog = DateTime.UtcNow;

                bool disagree = flowDir != 0 && bodyT != 0 && Math.Sign(bodyT) != flowDir;

                SentinelCore.Log("Tide", bars.Instrument.MasterInstrument.Name +
                    " " + (flowDir > 0 ? "flow▲" : flowDir < 0 ? "flow▼" : "flat") +
                    " body " + bodyT.ToString("F1") + "t · height " + heightT.ToString("F1") + "t" +
                    " · dCvd " + dCvd.ToString("F0") +
                    " · impact " + impact.ToString("F1") + "t/1k" +
                    (disagree ? " · ⚠ABSORPTION (flow and price disagree)" : "") +
                    " · " + reason + " · " + _barsThisSession + " bars" +
                    (_sawTape ? " · tape=quote" : " · tape=bar-proxy"));
            }
            catch (Exception ex) { SentinelCore.Swallow("Tide.LogBar", ex); }
        }

        // Same two helpers every Sentinel bars type defines privately (they are not on BarsType).
        private double RoundToTick(double price, Bars bars) => bars.Instrument.MasterInstrument.RoundToTickSize(price);
        private void SafeRemoveProperty(string name) { var p = Properties.Find(name, true); if (p != null) Properties.Remove(p); }
    }
}
