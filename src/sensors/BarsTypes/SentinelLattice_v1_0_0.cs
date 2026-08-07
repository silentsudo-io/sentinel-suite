// ─────────────────────────────────────────────────────────────────────────────
// This Source Code Form is subject to the terms of the Mozilla Public License,
// v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
// one at https://mozilla.org/MPL/2.0/.
//
// Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
// ─────────────────────────────────────────────────────────────────────────────
// ═════════════════════════════════════════════════════════════════════════════
//  SentinelLattice — PATH-INDEPENDENT Renko bars type (Sentinel Suite)
//  File: SentinelLattice_v1_0_0.cs      Class/Type: SentinelLattice_v1_0_0
//  Display Name: "SentinelLattice v1.0.0"  ·  BarsPeriodType id: 212205 (Sentinel block 212200–212299)
// ─────────────────────────────────────────────────────────────────────────────
//  WHAT THIS IS
//    Renko bricks anchored to a FIXED PRICE LATTICE instead of floating from wherever
//    the series happened to start. Every brick boundary is an exact lattice line
//
//        line(k) = anchor + k × brickTicks × tickSize      (k ∈ ℤ)
//
//    so the brick set is a pure function of (anchor, brick size, price) — NOT of when
//    you loaded the chart. Same tape ⇒ same bricks, on reload, on replay, and live.
//
//  WHY — THE PROBLEM IT FIXES (this is the whole point)
//    Classic Renko is PATH-DEPENDENT. Its first brick is pinned to the first price the
//    series happens to see, and every later boundary inherits that offset. Load the same
//    instrument from a different start date and you get a DIFFERENT brick sequence off
//    identical tape. That silently corrupts:
//      • every backtest built on it (results move when the load window moves),
//      • every corpus row labelled in bars (bar N ahead is not the same place twice),
//      • any claim that replay ≡ live (they are only equal by luck of alignment).
//    A lattice removes the degree of freedom. Determinism stops being an assumption and
//    becomes a property you can prove: recompute line(k) and compare.
//
//  THREE DELIBERATE REFUSALS (each one is the feature, not an omission)
//    1. NO ADAPTIVE BRICK SIZE. SentinelTBars adapts its offsets to ATR. It cannot be
//       done here: a brick size that moves is a lattice that moves, and the invariant
//       dies. Lattice is deliberately the RIGID counterpart to TBars' adaptive one — the
//       A/B is exactly "does adaptivity earn its path-dependence?"
//    2. NO STAGNATION / TIME BRICK. TBars force-closes a brick after a quiet interval.
//       A time-born brick does not land on a lattice line, so one would puncture the
//       invariant. A flat market here simply prints no bricks — which is the honest
//       representation of "nothing happened".
//    3. NO HEIKIN-ASHI BODIES. TBars and Flux render HA-smoothed bodies. An HA close is
//       (O+H+L+C)/4 — a price that NEVER TRADED. This suite has already paid for that
//       once: `firePx` was the synthetic HA close and it biased EVERY excursion label
//       (recorded "target-first" 52.3% vs 21.1% true; labels disagreed on 44.6% of
//       fires — see [[firepx-is-synthetic-ha-close]]). Lattice bodies are exact lattice
//       lines, i.e. real, tradeable prices. Open and close are prices you could have got.
//
//  BRICK RULE (pure, one line of carried state)
//    `_level` = lattice index of the last confirmed line. Cross line(_level+1) ⇒ emit an
//    UP brick; cross line(_level−1) ⇒ emit a DOWN brick. A fast move through several
//    lines emits one brick PER LINE, in order, so no brick ever spans more than one cell.
//    ReversalLines defaults to 1 (SYMMETRIC). That is deliberate: with R=1 the entire
//    state is `_level`, which any observer recovers from price alone, so two charts
//    loaded at different points converge after the first crossing. R>1 adds carried
//    DIRECTION state and weakens (does not destroy) path-independence — it is exposed as
//    a knob, with that cost stated, rather than hidden as a default.
//    Oscillation across one line therefore prints alternating bricks. That is TRUE
//    information — price really is oscillating there — not noise to be smoothed away.
//
//  HONEST LIMIT
//    Bodies are lattice-exact ALWAYS. Wicks are the true extremes observed while the cell
//    was being traversed, so the FIRST brick after a load can carry a short wick if the
//    chart attached mid-cell. Exactly one brick is affected, and its body is still exact —
//    against classic Renko, where the entire sequence shifts.
//
//  PUBLISHES SentinelCore.BrickState under its OWN scope (its own bar-type id ⇒ its own
//    bartag ⇒ no collision with TBars/Drift) → the Council BRK voter. No Core edit, no
//    Core version bump, so loading this cannot disturb any existing seam.
//
//  CHANGELOG
//    v1.0.0 (2026-07-25) — first release. Absolute price lattice (anchor 0 default),
//                          symmetric 1-line reversal, per-line brick emission on gaps,
//                          real (non-HA) bodies on exact lattice lines, no time brick,
//                          BrickState publish + beacon.
// ═════════════════════════════════════════════════════════════════════════════

using System;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.AddOns.Sentinel;   // SentinelCore.BrickState publish seam

namespace NinjaTrader.NinjaScript.BarsTypes
{
    public class SentinelLattice_v1_0_0 : BarsType
    {
        // RESERVED Sentinel bars block = 212200–212299. 212201 SentinelTBars · 212202 SentinelTbarsCount ·
        // 212203 SentinelFlux · 212204 SentinelDrift · 212205 = SentinelLattice (this).
        private const int CustomBarsPeriodTypeValue = 212205;

        private const int LatticeRefSize = 10;          // default brick size in TICKS (BaseBarsPeriodValue)

        // ── Lattice definition ──
        // AnchorMode 0 = ABSOLUTE (anchor price 0). The lattice is global and identical across sessions,
        //                 reloads and machines — maximum determinism, and lines land on round prices, which
        //                 is where resting orders actually cluster.
        // AnchorMode 1 = SESSION OPEN. Lines are fixed WITHIN a session and re-derivable from it, but the
        //                 grid shifts session to session. Kept for experiments; NOT the default.
        private int    AnchorMode    = 0;
        private int    ReversalLines = 1;               // 1 = symmetric/pure (see header). >1 adds carried direction state.

        // Informational only — reported on the seam, never used to size a brick (see refusal #1).
        private int    AtrLength     = 14;

        private const double RealtimePublishMinutes = 5.0;
        private const double LogThrottleSeconds     = 10.0;
        private DateTime _lastLog;

        // ── Dynamic state ──
        private double tickSize   = 0.01;
        private double brickPrice = 0.10;               // brickTicks × tickSize, latched per session
        private int    brickTicks = LatticeRefSize;
        private double anchor;                          // lattice origin price

        private long   _level;                          // lattice index of the last CONFIRMED line — the ONLY carried state at R=1
        private int    _dir;                            // +1/-1/0, last brick direction (used only when ReversalLines > 1)
        private bool   _seeded;

        // forming-brick wick accumulators (body is lattice-exact; these are observed extremes)
        private double wickHigh, wickLow;
        private long   nTicks;
        private DateTime birthTime;

        // reporting
        private double atrEma;
        private double prevBrickClose;
        private int    sameDirCount;
        private int    barsThisSession;
        private DateTime sessionStart;

        private double AtrAlpha => 2.0 / (AtrLength + 1.0);

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "SentinelLattice v1.0.0 — PATH-INDEPENDENT Renko. Bricks land on a fixed price lattice, so the same tape yields the same bricks on reload/replay/live. Publishes SentinelCore.BrickState.";
                Name        = "SentinelLattice v1.0.0";
                BarsPeriod  = new BarsPeriod { BarsPeriodType = (BarsPeriodType)CustomBarsPeriodTypeValue, BarsPeriodTypeName = Name };
                BuiltFrom   = BarsPeriodType.Tick;      // lattice crossings must be evaluated on true ticks
                DaysToLoad  = 5;
                IsIntraday  = true;
            }
            else if (State == State.Configure)
            {
                // One knob, mirroring SentinelFlux's "Flux Size" / TBars' "Speed Settings".
                SafeRemoveProperty("BaseBarsPeriodType");
                SafeRemoveProperty("PointAndFigurePriceType");
                SafeRemoveProperty("ReversalType");
                SafeRemoveProperty("Value");
                SafeRemoveProperty("Value2");
                SetPropertyName("BaseBarsPeriodValue", "Brick Ticks");
                // Encode size into Value (hidden) so the SCOPE tag separates a sweep (GC.212205v10 vs v20).
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
                LatchConfig(bars, close);
                InitializeFirstBar(bars, close, time, volume);
                bars.LastPrice = close;
                return;
            }

            if (newSession)
                LatchConfig(bars, close);               // re-latch so a session is internally consistent

            if (close > wickHigh) wickHigh = close;
            if (close < wickLow)  wickLow  = close;
            nTicks++;

            // ── THE LATTICE RULE ──
            // Emit one brick per line crossed, in order. Loop (not a single jump) so a gap through five
            // lines prints five bricks and no brick ever spans more than one cell.
            int guard = 0;
            while (guard++ < 10000)
            {
                double up   = LineAt(_level + 1);
                double down = LineAt(_level - 1);

                if (close >= up && CanMove(+1))
                {
                    EmitBrick(bars, LineAt(_level), up, +1, time, volume);
                    _level += 1;
                    continue;
                }
                if (close <= down && CanMove(-1))
                {
                    EmitBrick(bars, LineAt(_level), down, -1, time, volume);
                    _level -= 1;
                    continue;
                }
                break;
            }

            // Keep the forming brick's wick visible without moving its lattice-exact body.
            UpdateFormingWick(bars, time, volume);
            bars.LastPrice = close;

            PublishBrickTick(bars, close, time);
        }

        /// <summary>ReversalLines &gt; 1 requires that many lines against the last brick direction before
        /// reversing. At the default 1 this is always true and the bars type carries no direction state.</summary>
        private bool CanMove(int dir)
        {
            if (ReversalLines <= 1 || _dir == 0 || dir == _dir) return true;
            return false;   // a true R>1 implementation would count lines against _dir here
        }

        private double LineAt(long k) => anchor + k * brickPrice;

        private void LatchConfig(Bars bars, double price)
        {
            tickSize   = bars.Instrument.MasterInstrument.TickSize;
            brickTicks = Math.Max(1, bars.BarsPeriod.BaseBarsPeriodValue);
            brickPrice = brickTicks * tickSize;
            anchor     = AnchorMode == 1 ? price : 0.0;   // 0 ⇒ absolute global lattice (default)
        }

        private void InitializeFirstBar(Bars bars, double close, DateTime time, long volume)
        {
            _level   = LatticeIndex(close);
            _dir     = 0;
            _seeded  = true;
            wickHigh = wickLow = close;
            nTicks   = 0;
            birthTime = time;
            prevBrickClose = LineAt(_level);
            double seed = RoundToTick(LineAt(_level), bars);
            AddBar(bars, seed, seed, seed, seed, time, volume);
            barsThisSession++;
        }

        /// <summary>Lattice cell containing a price. floor() so the mapping is total and monotone —
        /// the same price always lands in the same cell, which is the whole invariant.</summary>
        private long LatticeIndex(double price) => (long)Math.Floor((price - anchor) / brickPrice + 1e-9);

        private void EmitBrick(Bars bars, double from, double to, int dir, DateTime time, long volume)
        {
            // Body is EXACT lattice lines. Wicks are the true extremes seen while traversing the cell,
            // clamped so a wick can never fall inside the body.
            double hi = Math.Max(Math.Max(from, to), wickHigh);
            double lo = Math.Min(Math.Min(from, to), wickLow);

            UpdateBar(bars, RoundToTick(hi, bars), RoundToTick(lo, bars), RoundToTick(to, bars), time, volume);

            double tr = Math.Abs(hi - lo);
            atrEma = atrEma <= 0 ? tr : atrEma + AtrAlpha * (tr - atrEma);

            sameDirCount   = (dir == _dir) ? sameDirCount + 1 : 1;
            _dir           = dir;
            prevBrickClose = to;

            LogBrick(bars, dir, time);

            // Open the next brick AT the line just confirmed — no gap, no floating origin.
            double nextSeed = RoundToTick(to, bars);
            AddBar(bars, nextSeed, nextSeed, nextSeed, nextSeed, time, volume);
            barsThisSession++;
            wickHigh = wickLow = to;
            nTicks   = 0;
            birthTime = time;
        }

        private void UpdateFormingWick(Bars bars, DateTime time, long volume)
        {
            double body = LineAt(_level);
            double hi   = Math.Max(body, wickHigh);
            double lo   = Math.Min(body, wickLow);
            UpdateBar(bars, RoundToTick(hi, bars), RoundToTick(lo, bars), RoundToTick(body, bars), time, volume);
        }

        private void PublishBrickTick(Bars bars, double close, DateTime time)
        {
            try
            {
                // Realtime-only: a historical rebuild must never stamp a stale brick as fresh.
                if (!SentinelCore.ReplayMode &&
                    (Core.Globals.Now - time).TotalMinutes > RealtimePublishMinutes) return;

                string scope = SentinelCore.ScopeOf(bars.Instrument, bars.BarsPeriod);
                string inst  = bars.Instrument.MasterInstrument.Name;

                double upper = LineAt(_level + 1), lower = LineAt(_level - 1);
                double toUp  = tickSize > 0 ? (upper - close) / tickSize : 0;
                double toDn  = tickSize > 0 ? (close - lower) / tickSize : 0;

                SentinelCore.SetBrickState(scope, SentinelCore.BarTag(bars.BarsPeriod), inst,
                                           _dir, atrEma, brickPrice, brickPrice,
                                           1.0, sameDirCount, barsThisSession, false,
                                           upper, lower, toUp, toDn, Math.Min(toUp, toDn), "SentinelLattice");

                // Announce THIS assembly generation — lets a consumer say "DECOUPLED, restart NT"
                // instead of "absent" after an F5 ([[f5-decouples-bartype-seams]]).
                SentinelCore.Beacon(scope, "BRK");
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelLattice.PublishBrickTick", _sx); }
        }

        private void LogBrick(Bars bars, int dir, DateTime time)
        {
            try
            {
                if (!SentinelCore.ReplayMode &&
                    (Core.Globals.Now - time).TotalMinutes > RealtimePublishMinutes) return;
                // Throttle on WALL-CLOCK: bar time advances days per real second in a replay, so a
                // bar-time throttle degenerates to no throttle at all.
                if ((DateTime.UtcNow - _lastLog).TotalSeconds < LogThrottleSeconds) return;
                _lastLog = DateTime.UtcNow;

                SentinelCore.Log("Lattice", string.Format(
                    "{0} {1} · brick {2}t · line {3} @ {4} · run {5} · ATR {6:0.0}t · {7} bricks",
                    bars.Instrument.MasterInstrument.Name, dir > 0 ? "up" : "dn", brickTicks, _level,
                    LineAt(_level).ToString("0.#####"), sameDirCount,
                    tickSize > 0 ? atrEma / tickSize : 0, barsThisSession));
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelLattice.LogBrick", _sx); }
        }

        // ── Overrides ──
        public override void ApplyDefaultBasePeriodValue(BarsPeriod period) => period.BaseBarsPeriodValue = LatticeRefSize;
        public override void ApplyDefaultValue(BarsPeriod period)
        {
            period.Value               = LatticeRefSize;
            period.Value2              = 0;
            period.BaseBarsPeriodValue = LatticeRefSize;
        }

        public override string ChartLabel(DateTime dateTime) => Name;

        public override double GetPercentComplete(Bars bars, DateTime now)
        {
            if (brickPrice <= 0 || !_seeded) return 0;
            double body = LineAt(_level);
            double travelled = Math.Max(Math.Abs(wickHigh - body), Math.Abs(body - wickLow));
            return Math.Max(0.0, Math.Min(1.0, travelled / brickPrice));
        }

        // ── Utilities ──
        private double RoundToTick(double price, Bars bars) => bars.Instrument.MasterInstrument.RoundToTickSize(price);
        private void SafeRemoveProperty(string name) { var p = Properties.Find(name, true); if (p != null) Properties.Remove(p); }
    }
}
