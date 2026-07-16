// ─────────────────────────────────────────────────────────────────────────────
// This Source Code Form is subject to the terms of the Mozilla Public License,
// v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
// one at https://mozilla.org/MPL/2.0/.
//
// Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
// ─────────────────────────────────────────────────────────────────────────────
// ═════════════════════════════════════════════════════════════════════════════
//  SentinelTbarsCount — PLAIN HA / Renko-hybrid "T-brick" BARS TYPE (Sentinel Suite)
//  File: SentinelTbarsCount_v1_0_0.cs   Class/Type: SentinelTbarsCount_v1_0_0
//  Display Name: "SentinelTbarsCount v1.0.0"  ·  BarsPeriodType id: 212202 (reserved Sentinel bars block 212200–212299)
// ─────────────────────────────────────────────────────────────────────────────
//  WHAT THIS IS
//    The Sentinel-graded successor to TbarsCount (frozen). A PLAIN fixed-offset
//    renko brick with Heikin-Ashi bodies + real wicks — deliberately WITHOUT the
//    adaptive machinery of SentinelTBars (no ATR floor / confirmation / density /
//    hysteresis). Two knobs only, via "Speed Settings" (Base → trend = Base/2,
//    reversal = Base×2 in ticks). Its point of difference is the LIVE COUNTDOWN:
//    it publishes "ticks to the next brick" so SentinelBrickCounter can show it
//    on-chart — but that now travels through SentinelCore.BrickState (v1.6.1), so
//    ONE generic counter HUD works on ANY brick bars type, not a private feed.
//
//  RELATION TO TbarsCount (frozen — NOT edited)
//    Same brick core; reworked for correctness:
//      1. INIT/RESET BUG FIXED — TbarsCount seeded barMax/barMin as
//         `barOpen ± trendOffset * barDirection` with barDirection defaulting to 0
//         (→ a ZERO-WIDTH first brick) and, at a session reset where the prior
//         session ended SHORT (barDirection = -1), INVERTED boundaries
//         (barMax < barMin → a burst of garbage bricks at each session open).
//         Here boundaries seed symmetric (`barOpen ± trendOffset`) with
//         barDirection = 1.
//      2. BuiltFrom = Tick (was 0) — a renko brick must see every price.
//      3. NO STATIC-FEED LEAK — TbarsCountCounterFeed kept a dictionary keyed by
//         each Bars object's identity hash and never evicted (leaked one entry per
//         reload). Superseded by the SentinelCore.BrickState seam.
//      4. GAP CHAINING — a tick that jumps several brick-widths now prints all the
//         bricks it crossed (TbarsCount printed one per tick).
//      5. GetPercentComplete uses a consistent brick basis (TbarsCount used an odd
//         nearest/larger-remaining heuristic).
//    Data is durably logged per brick to SentinelCore.BrickLog (NT regenerates
//    custom bricks from ticks each load and stores nothing).
//
//  CHANGELOG
//    v1.0.0 (2026-07-06) — first Sentinel-graded release; supersedes TbarsCount.
//                          BarsPeriodType 69698 → 212202 (RESERVED Sentinel bars block 212200–212299).
// ═════════════════════════════════════════════════════════════════════════════

using System;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.AddOns.Sentinel;   // SentinelCore.BrickState + BrickLog

namespace NinjaTrader.NinjaScript.BarsTypes
{
    public class SentinelTbarsCount_v1_0_0 : BarsType
    {
        // RESERVED Sentinel bars BarsPeriodType block = 212200–212299 (see SentinelTBars header).
        //   212202 = SentinelTbarsCount. (Was 69698, next to the legacy TbarsCount block; moved into the block.)
        private const int CustomBarsPeriodTypeValue = 212202;

        // Realtime guards (skip historical rebuild for telemetry/logging)
        private const double RealtimePublishMinutes = 5.0;
        private const double BrickLogThrottleSeconds = 10.0;
        private DateTime _lastHeartbeat;

        // Fixed offsets (from the Speed Settings; computed once at init)
        private double tickSize = 0.01;
        private double trendOffset, reversalOffset, openOffset;

        // Brick state
        private double barOpen, brickBasis, barMax, barMin, syntheticOpen;
        private int    barDirection = 1;
        private int    sameDirCount = 1;
        private double haPrevOpen, haPrevClose;

        private DateTime sessionStart;
        private int barsThisSession;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "SentinelTbarsCount v1.0.0 — plain fixed-offset HA/Renko brick + live 'ticks to next brick' (SentinelCore.BrickState).";
                Name        = "SentinelTbarsCount v1.0.0";
                BarsPeriod  = new BarsPeriod { BarsPeriodType = (BarsPeriodType)CustomBarsPeriodTypeValue, BarsPeriodTypeName = Name };
                BuiltFrom   = BarsPeriodType.Tick;   // FIX #2
                DaysToLoad  = 5;
                IsIntraday  = true;
                return;
            }

            if (State == State.Configure)
            {
                SafeRemoveProperty("BaseBarsPeriodType");
                SafeRemoveProperty("PointAndFigurePriceType");
                SafeRemoveProperty("ReversalType");
                SafeRemoveProperty("Value");
                SafeRemoveProperty("Value2");
                SetPropertyName("BaseBarsPeriodValue", "Speed Settings");
                BarsPeriod.Value  = BarsPeriod.BaseBarsPeriodValue / 2;
                BarsPeriod.Value2 = BarsPeriod.BaseBarsPeriodValue * 2;
            }
        }

        public override int GetInitialLookBackDays(BarsPeriod barsPeriod, TradingHours tradingHours, int barsBack) => 3;

        protected override void OnDataPoint(Bars bars, double open, double high, double low, double close,
            DateTime time, long volume, bool isBar, double bid, double ask)
        {
            try
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
                    InitializeFirstBar(bars, open, high, low, close, time, volume);
                    bars.LastPrice = close;
                    return;
                }

                ChainWhileBeyond(bars, close, time, volume);   // print full bricks for the crossed distance
                UpdateExistingBar(bars, close, time, volume);  // apply the residual to the forming brick

                PublishBrickTick(bars, close, time);           // live BrickState (countdown HUD) — realtime only

                bars.LastPrice = close;
            }
            catch (Exception ex)
            {
                Print(ex.ToString());
            }
        }

        private void InitializeFirstBar(Bars bars, double open, double high, double low, double close, DateTime time, long volume)
        {
            tickSize       = bars.Instrument.MasterInstrument.TickSize;
            trendOffset    = BarsPeriod.Value  * tickSize;
            reversalOffset = BarsPeriod.Value2 * tickSize;
            openOffset     = BarsPeriod.BaseBarsPeriodValue * tickSize;

            barDirection = 1;                       // FIX #1: never seed boundaries with a 0/negative direction
            sameDirCount = 1;

            brickBasis = barOpen = open;
            barMax     = RoundToTick(barOpen + trendOffset, bars);
            barMin     = RoundToTick(barOpen - trendOffset, bars);

            double haOpenInitial  = (open + close) * 0.5;
            double haCloseInitial = GetHeikinAshiClose(open, high, low, close);
            AddBar(bars, haOpenInitial, high, low, haCloseInitial, time, volume);
            haPrevOpen  = haOpenInitial;
            haPrevClose = haCloseInitial;

            barsThisSession = 1;
            sessionStart    = time;
        }

        // ── Brick formation ──
        private void ChainWhileBeyond(Bars bars, double close, DateTime time, long volume)
        {
            for (int safety = 0; safety < 50; safety++)
            {
                bool overMax  = Cmp(bars, close, barMax) > 0;
                bool underMin = Cmp(bars, close, barMin) < 0;
                if (!overMax && !underMin) break;
                CreateBreakoutBar(bars, close, time, volume);
            }
        }

        private void UpdateExistingBar(Bars bars, double close, DateTime time, long volume)
        {
            int    last    = bars.Count - 1;
            double newHigh = Math.Max(close, bars.GetHigh(last));
            double newLow  = Math.Min(close, bars.GetLow(last));
            double haClose = GetHeikinAshiClose(bars.GetOpen(last), newHigh, newLow, close);
            UpdateBar(bars, newHigh, newLow, haClose, time, volume);
            haPrevClose = haClose;
            haPrevOpen  = bars.GetOpen(last);
        }

        private void CreateBreakoutBar(Bars bars, double close, DateTime time, long volume)
        {
            int  last     = bars.Count - 1;
            bool overMax  = Cmp(bars, close, barMax) > 0;
            bool underMin = Cmp(bars, close, barMin) < 0;

            double breakoutPrice = overMax ? Math.Min(close, barMax) : Math.Max(close, barMin);
            breakoutPrice        = RoundToTick(breakoutPrice, bars);

            double barHigh = overMax  ? breakoutPrice : bars.GetHigh(last);
            double barLow  = underMin ? breakoutPrice : bars.GetLow(last);

            double haCloseBreak = GetHeikinAshiClose(bars.GetOpen(last), barHigh, barLow, breakoutPrice);
            UpdateBar(bars, barHigh, barLow, haCloseBreak, time, volume);

            int newDir = overMax ? 1 : -1;
            if (newDir == barDirection) sameDirCount++;
            else { barDirection = newDir; sameDirCount = 1; }

            syntheticOpen = RoundToTick(breakoutPrice - openOffset * barDirection, bars);

            haPrevOpen  = GetHeikinAshiOpen(haPrevOpen, haPrevClose);
            haPrevClose = haCloseBreak;

            if (barDirection > 0)
            {
                barMax = RoundToTick(breakoutPrice + trendOffset, bars);
                barMin = RoundToTick(breakoutPrice - reversalOffset, bars);
            }
            else
            {
                barMax = RoundToTick(breakoutPrice + reversalOffset, bars);
                barMin = RoundToTick(breakoutPrice - trendOffset, bars);
            }

            brickBasis = breakoutPrice;
            barOpen    = close;

            double nextHaOpen  = GetHeikinAshiOpen(haPrevOpen, haPrevClose);
            double nextHigh    = overMax  ? breakoutPrice : syntheticOpen;
            double nextLow     = underMin ? breakoutPrice : syntheticOpen;
            double nextHaClose = GetHeikinAshiClose(nextHaOpen, nextHigh, nextLow, breakoutPrice);

            AddBar(bars, nextHaOpen, nextHigh, nextLow, nextHaClose, time, volume);

            haPrevOpen  = nextHaOpen;
            haPrevClose = nextHaClose;

            barsThisSession++;
            LogBrick(bars, time);
        }

        // ── SentinelCore.BrickState publish (per tick → live countdown HUD) ──
        private void PublishBrickTick(Bars bars, double close, DateTime time)
        {
            try
            {
                if (tickSize <= 0) return;
                if ((NinjaTrader.Core.Globals.Now - time).TotalMinutes > RealtimePublishMinutes) return;
                string inst = bars?.Instrument?.MasterInstrument?.Name;
                if (string.IsNullOrEmpty(inst)) return;

                // SentinelCore ≥ v1.19.0 — publish by SCOPE (a bars type IS the chart's bar type).
                string scope = SentinelCore.ScopeOf(bars.Instrument, bars.BarsPeriod);
                if (string.IsNullOrEmpty(scope)) return;

                double ticksToUpper = Math.Max(0.0, (barMax - close) / tickSize);
                double ticksToLower = Math.Max(0.0, (close - barMin) / tickSize);
                double nearest      = Math.Min(ticksToUpper, ticksToLower);

                // Plain mode: no ATR (0) and density fixed at 1.0.
                SentinelCore.SetBrickState(scope, SentinelCore.BarTag(bars.BarsPeriod), inst,
                                           barDirection, 0.0, trendOffset, reversalOffset,
                                           1.0, sameDirCount, barsThisSession, false,
                                           barMax, barMin, ticksToUpper, ticksToLower, nearest, "SentinelTbarsCount");

                if ((time - _lastHeartbeat).TotalSeconds >= BrickLogThrottleSeconds)
                {
                    _lastHeartbeat = time;
                    string arrow = barDirection > 0 ? "up" : "dn";
                    SentinelCore.Log("TbarsCount", string.Format(
                        "{0} {1} · trend {2:0.0}t rev {3:0.0}t · run {4} · {5} bricks · next {6:0}t",
                        inst, arrow, trendOffset / tickSize, reversalOffset / tickSize,
                        sameDirCount, barsThisSession, nearest));
                }
            }
            catch { }
        }

        // ── Durable per-brick DATA log (one JSONL record per completed brick, realtime only) ──
        private void LogBrick(Bars bars, DateTime time)
        {
            try
            {
                if (tickSize <= 0) return;
                if ((NinjaTrader.Core.Globals.Now - time).TotalMinutes > RealtimePublishMinutes) return;
                string inst = bars?.Instrument?.MasterInstrument?.Name;
                if (string.IsNullOrEmpty(inst)) return;

                int done = bars.Count - 2;   // the brick that just closed
                if (done < 0) return;

                var ci = System.Globalization.CultureInfo.InvariantCulture;
                double o = bars.GetOpen(done), h = bars.GetHigh(done), l = bars.GetLow(done), c = bars.GetClose(done);
                string fields = string.Format(ci,
                    "\"mode\":\"plain\",\"dir\":{0},\"o\":{1:0.#####},\"h\":{2:0.#####},\"l\":{3:0.#####},\"c\":{4:0.#####}," +
                    "\"sizeT\":{5:0.#},\"trendT\":{6:0.#},\"revT\":{7:0.#},\"run\":{8},\"n\":{9},\"vol\":{10}",
                    barDirection, o, h, l, c,
                    Math.Abs(h - l) / tickSize, trendOffset / tickSize, reversalOffset / tickSize,
                    sameDirCount, barsThisSession, bars.GetVolume(done));
                SentinelCore.BrickLog.Append("SentinelTbarsCount", inst, fields);
            }
            catch { }
        }

        // ── Overrides ──
        public override void ApplyDefaultBasePeriodValue(BarsPeriod period) => period.BaseBarsPeriodValue = 21;
        public override void ApplyDefaultValue(BarsPeriod period)
        {
            period.Value               = 1;
            period.Value2              = 4;
            period.BaseBarsPeriodValue = 21;
        }

        public override string ChartLabel(DateTime dateTime) => Name;

        public override double GetPercentComplete(Bars bars, DateTime now)
        {
            if (bars.Count == 0) return 0;
            double targetRange = barDirection > 0 ? (barMax - brickBasis) : (brickBasis - barMin);
            if (targetRange <= 0) return 0;
            double lastClose = bars.GetClose(bars.Count - 1);
            double progress  = barDirection > 0
                ? (lastClose - brickBasis) / targetRange
                : (brickBasis - lastClose) / targetRange;
            return Math.Max(0, Math.Min(1, progress));
        }

        // ── Utilities ──
        private int Cmp(Bars bars, double a, double b) => bars.Instrument.MasterInstrument.Compare(a, b);
        private double GetHeikinAshiOpen(double priorHAOpen, double priorHAClose) => (priorHAOpen + priorHAClose) * 0.5;
        private double GetHeikinAshiClose(double open, double high, double low, double close) => (open + high + low + close) * 0.25;
        private double RoundToTick(double price, Bars bars) => bars.Instrument.MasterInstrument.RoundToTickSize(price);
        private void SafeRemoveProperty(string name) { var p = Properties.Find(name, true); if (p != null) Properties.Remove(p); }
    }
}
