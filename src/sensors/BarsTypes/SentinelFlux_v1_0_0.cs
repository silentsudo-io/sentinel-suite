// ─────────────────────────────────────────────────────────────────────────────
// This Source Code Form is subject to the terms of the Mozilla Public License,
// v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
// one at https://mozilla.org/MPL/2.0/.
//
// Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
// ─────────────────────────────────────────────────────────────────────────────
// ═════════════════════════════════════════════════════════════════════════════
//  SentinelFlux — ORDER-FLOW IMBALANCE bars type (Sentinel Suite)
//  File: SentinelFlux_v1_0_0.cs        Class/Type: SentinelFlux_v1_0_0
//  Display Name: "SentinelFlux v1.0.0"  ·  BarsPeriodType id: 212203 (reserved Sentinel bars block 212200–212299)
//  Spec: Docs/SENTINEL_FLUXBARS_SPEC.md
// ─────────────────────────────────────────────────────────────────────────────
//  WHAT THIS IS
//    A bar closes on ACCUMULATED SIGNED ORDER-FLOW IMBALANCE, not on time / ticks /
//    volume / range / price distance. This is López de Prado's information-driven
//    "imbalance bar" (AFML ch. 2) — each bar carries ≈ constant INFORMATION, so bars
//    are fine-grained exactly when one side dominates and coarse when the tape is
//    balanced. Married to the SentinelTBars discipline so it survives production:
//      • Quote-rule signing   — sign each trade with bid/ask (Lee–Ready), tick-rule
//        fallback. Better than the pure tick rule, and free here (OnDataPoint has quotes).
//      • Self-stabilising θ*   — the close threshold = FluxSize × ATR(ticks) × imbalance-
//        per-price-tick (a bar-size-INVARIANT market intensity), so the classic
//        imbalance-bar RUNAWAY (threshold explodes in a trend, collapses in chop) can't
//        happen: θ* tracks a ratio, not a self-referential bar-size EWMA.
//      • Physical BACKSTOPS    — a bar also force-closes on a price / time / tick cap, so
//        it can neither balloon into one giant bar nor stall forever in a dead tape.
//      • HA bodies + real wicks — Heikin-Ashi-smoothed body coloured by PRICE direction;
//        net FLOW direction is carried in the seam (they can DIVERGE = absorption).
//    PUBLISHES SentinelCore.FluxState (v1.31.0 seam) → the Council FLUX voter (a STATE
//    voter and the suite's one ORDER-FLOW-substrate axis, orthogonal to the price bloc)
//    + a flow-vs-price DIVERGENCE (absorption) size damp.
//
//  WHY (design intent — see the spec)
//    1. Orthogonality — every other Sentinel voter is price-derived (echoes the OHLC).
//       A flow-SYNCHRONISED clock orthogonalises the WHOLE chart, and the seam adds a
//       tape-sourced voter (complement to LiquidityWalls' book-sourced absorption).
//    2. Label fidelity — the Council trains on first-touch triple-barrier labels measured
//       IN BARS; information-driven bars make "N bars ahead" ≈ "constant information
//       ahead", sharpening the exact label the ConvictionFloor / weight fit consume.
//
//  DETERMINISM (non-negotiable for the training corpus)
//    Config latched once per session; EWMAs updated once per CLOSED bar; realtime-publish
//    only (a historical rebuild must not stamp a stale flow as fresh); scope-keyed publish
//    (ScopeOf(bars.Instrument, bars.BarsPeriod)) so two Flux charts never clobber each other.
//
//  CHANGELOG
//    v1.0.0 (2026-07-14) — first release. Imbalance clock (Volume mode / quote-rule signing),
//                          self-stabilising threshold, price/time/tick backstops, HA render,
//                          SentinelCore.FluxState publish seam + Council FLUX voter.
//    v1.0.0 (same day, hotfix) — THRESHOLD REWRITE. The first live GC load closed EVERY realtime bar on
//                          the 90 s TIME backstop (θ ~38 vs θ* ~90) — the imbalance clock was dormant. Cause:
//                          θ* = fluxScale × ATR(true-range) × (|θ|/net-displacement) DOUBLE-COUNTED chop
//                          (large range × small displacement) and inflated θ* ~2.5×. Replaced with the
//                          canonical López de Prado rule θ* = fluxScale × E[|θ|] (self-consistent EWMA of
//                          realized |θ|), so imbalance is the primary close reason. No ATR in θ* (ATR still
//                          drives the price backstop only).
//    v1.0.0 (same day, live-validated) — WINSORIZE the E[|θ|] input (a ~2000-lot block trade spiked θ* to 149
//                          live): cap a bar's EWMA contribution at WinsorMult(=4)× the running estimate so one
//                          outlier can't redefine "typical". Removed the temp scope/readback/store diagnostic
//                          from the heartbeat (FLUX confirmed voting in the Council; the seam was never the bug —
//                          a stale-DLL/sticky-bars-type reload was). LIVE: full 10/10 Council roster on GC.212203v8.
// ═════════════════════════════════════════════════════════════════════════════

using System;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.AddOns.Sentinel;   // SentinelCore.FluxState publish seam

namespace NinjaTrader.NinjaScript.BarsTypes
{
    public class SentinelFlux_v1_0_0 : BarsType
    {
        // RESERVED Sentinel bars block = 212200–212299. 212201 = SentinelTBars · 212202 = SentinelTbarsCount ·
        // 212203 = SentinelFlux (this) · 212204+ = future Sentinel bars.
        private const int CustomBarsPeriodTypeValue = 212203;

        // ── Imbalance / signing mode (internal fields; sensible defaults, easy to flip / wire to a registry later) ──
        private enum ImbMode { Tick = 0, Volume = 1, Dollar = 2 }
        private ImbMode _mode = ImbMode.Volume;   // VIB — net CONTRACT pressure (the futures sweet spot)
        private bool    _useQuoteRule = true;      // Lee–Ready quote rule, tick-rule fallback

        // ── Threshold shaping ──
        private const int    FluxRefSize      = 8;      // BaseBarsPeriodValue at which fluxScale == 1.0
        private int    AtrLength      = 14;             // EMA length for the brick ATR (price true range)
        private int    IntensityLen   = 50;             // EWMA length for E[|θ|] (the self-consistent threshold)
        private double WinsorMult     = 4.0;            // block-trade guard: cap a bar's E[|θ|] contribution at ×the running estimate
        private double DivergenceFrac = 0.5;            // |θ| ≥ this × θ* AND flow≠price ⇒ divergence (absorption)

        // ── Physical backstops (guarantee termination + bound size) ──
        private double PriceBackstopMult   = 2.5;       // force-close at ×ATR of price displacement
        private int    ForceStagnationSecs = 90;        // force-close after this many seconds
        private long   MaxTicksPerBar      = 5000;       // hard tick cap (belt-and-suspenders + warmup seeding)

        // Only publish FluxState for near-realtime bars (skip historical rebuild noise); throttle the human log.
        private const double RealtimePublishMinutes = 5.0;
        private const double FluxLogThrottleSeconds  = 10.0;
        private DateTime _lastFluxLog;

        // ── Dynamic state ──
        private double tickSize = 0.01;
        private double fluxScale = 1.0;                  // BaseBarsPeriodValue / FluxRefSize (latched per session)

        // forming-bar accumulators
        private double theta;                           // signed imbalance Σ b·w of the FORMING bar
        private double buyVol, sellVol;                 // signed weight split (for Pressure)
        private long   nTicks;                          // trades in the forming bar
        private double rawBarOpen, rawHigh, rawLow, rawClose;
        private DateTime birthTime;

        // EWMAs / carries (updated once per closed bar)
        private double atrEma;                          // brick ATR (price units) — drives the price backstop
        private double imbEwma;                         // EWMA of realized |θ| per closed bar — the expected imbalance E[|θ|]
        private int    lastTradeSign = 1;               // tick-rule carry (sign on Δp==0)
        private double prevBarClose;                    // previous CLOSED bar's raw close (for true range)
        private double haPrevOpen, haPrevClose;

        // session
        private double cvd;                             // running session cumulative volume delta (signed weight)
        private int    barsThisSession;
        private DateTime sessionStart;
        private bool   warmup = true;                   // true until imbEwma (E[|θ|]) is seeded

        private double AtrAlpha => 2.0 / (AtrLength + 1.0);
        private double ImbAlpha => 2.0 / (IntensityLen + 1.0);

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "SentinelFlux v1.0.0 — order-flow IMBALANCE bars (quote-rule signed, ATR-stabilised, HA render). Publishes SentinelCore.FluxState.";
                Name        = "SentinelFlux v1.0.0";
                BarsPeriod  = new BarsPeriod { BarsPeriodType = (BarsPeriodType)CustomBarsPeriodTypeValue, BarsPeriodTypeName = Name };
                BuiltFrom   = BarsPeriodType.Tick;   // signing + backstops need true tick timestamps + quotes
                DaysToLoad  = 5;
                IsIntraday  = true;
            }
            else if (State == State.Configure)
            {
                // Minimal chart UI — one size knob, mirroring SentinelTBars' "Speed Settings".
                SafeRemoveProperty("BaseBarsPeriodType");
                SafeRemoveProperty("PointAndFigurePriceType");
                SafeRemoveProperty("ReversalType");
                SafeRemoveProperty("Value");
                SafeRemoveProperty("Value2");
                SetPropertyName("BaseBarsPeriodValue", "Flux Size");
                // Encode the size into Value (hidden) so the SCOPE tag separates a sweep (GC.212203v8 vs v12) automatically.
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
                cvd             = 0;
            }

            if (bars.Count == 0 || (newSession && bars.IsResetOnNewTradingDay))
            {
                InitializeFirstBar(bars, open, high, low, close, time, volume, bid, ask);
                bars.LastPrice = close;
                return;
            }

            if (newSession)
                LatchConfig(bars);   // re-latch at a soft session boundary so a session is internally consistent

            // ── 1. sign + accumulate this trade into the forming bar ──
            int sign = SignTrade(close, bid, ask);
            double w = TradeWeight(volume, close);
            double dw = sign * w;

            theta += dw;
            if (sign > 0) buyVol += w; else if (sign < 0) sellVol += w;
            cvd   += dw;
            nTicks++;

            if (close > rawHigh) rawHigh = close;
            if (close < rawLow)  rawLow  = close;
            rawClose = close;

            UpdateFormingBar(bars, time, volume);

            // ── 2. close the bar if the imbalance target OR any physical backstop is hit ──
            // Gate the "soft" closes (imbalance / price) on ≥2 trades so a single tick can never mint a bar (anti-
            // machine-gun on coarse-timestamp replay data), while still letting bars form fast in a live burst.
            double threshold = CurrentThreshold();
            double barAge    = (time - birthTime).TotalSeconds;
            bool   formed    = nTicks >= 2;

            bool imbHit   = !warmup && formed && Math.Abs(theta) >= threshold;
            bool priceHit = formed && Math.Abs(rawClose - rawBarOpen) >= PriceBackstopMult * Math.Max(atrEma, tickSize);
            bool timeHit  = barAge >= ForceStagnationSecs && nTicks > 0;
            bool tickHit  = nTicks >= MaxTicksPerBar;

            if (imbHit || priceHit || timeHit || tickHit)
                CloseBar(bars, time, volume, threshold, imbHit ? "imb" : (priceHit ? "price" : (timeHit ? "time" : "tick")));

            PublishFluxTick(bars, time);   // live FluxState countdown (realtime only)
            bars.LastPrice = close;
        }

        // ── Trade signing — Lee–Ready quote rule, tick-rule fallback ──
        private int SignTrade(double price, double bid, double ask)
        {
            if (_useQuoteRule && ask > 0 && bid > 0)
            {
                if (price >= ask) { lastTradeSign = 1;  return 1; }
                if (price <= bid) { lastTradeSign = -1; return -1; }
            }
            // tick rule — sign of the price change, carrying the prior sign on an unchanged price
            int cmp = price.CompareTo(rawClose);
            if (cmp > 0) lastTradeSign = 1;
            else if (cmp < 0) lastTradeSign = -1;
            // cmp == 0 → keep lastTradeSign
            return lastTradeSign;
        }

        private double TradeWeight(long volume, double price)
        {
            double v = Math.Max(1L, volume);
            switch (_mode)
            {
                case ImbMode.Tick:   return 1.0;
                case ImbMode.Dollar: return v * price;
                default:             return v;          // Volume (VIB)
            }
        }

        // Threshold θ* = fluxScale × E[|θ|], the EXPECTED imbalance per bar (López de Prado's imbalance-bar rule: a
        // bar closes when accumulated imbalance reaches its expectation). This is SELF-CONSISTENT — bars close AT θ*,
        // so E[|θ|] EWMAs toward θ* and the target is stable; bars that close early on a backstop carry |θ|<θ* and
        // pull it DOWN, which makes imbalance the primary close reason (self-correcting). No ATR term — an earlier
        // build multiplied ATR(true-range) by |θ|/net-displacement, double-counting chop and inflating θ* ~2.5× so
        // the time backstop always won. During warmup (E[|θ|] unseeded) θ* is huge so early bars close on a physical
        // backstop and seed E[|θ|] from real data. fluxScale = FluxSize/8 sets the INFORMATION per bar (the one knob).
        private double CurrentThreshold()
        {
            if (warmup || imbEwma <= 0) return double.MaxValue;
            return Math.Max(1.0, fluxScale * imbEwma);
        }

        // ── Close the forming bar, update EWMAs, seed the next bar ──
        private void CloseBar(Bars bars, DateTime time, long volume, double threshold, string reason)
        {
            int last = bars.Count - 1;

            // final geometry of the closing bar (real wicks + HA-smoothed body)
            double haClose = HaClose(rawBarOpen, rawHigh, rawLow, rawClose);
            double haOpen  = bars.GetOpen(last);       // fixed at this bar's birth (an HA open)
            double dispHigh = Math.Max(rawHigh, Math.Max(haOpen, haClose));
            double dispLow  = Math.Min(rawLow,  Math.Min(haOpen, haClose));
            UpdateBar(bars, RoundToTick(dispHigh, bars), RoundToTick(dispLow, bars), RoundToTick(haClose, bars), time, volume);

            // ATR over the closed bar (true range vs the previous closed bar's raw close)
            double tr = Math.Max(rawHigh - rawLow,
                        Math.Max(Math.Abs(rawHigh - prevBarClose), Math.Abs(rawLow - prevBarClose)));
            if (tr <= 0) tr = tickSize;
            atrEma = atrEma <= 0 ? tr : atrEma + AtrAlpha * (tr - atrEma);

            // expected-imbalance update — EWMA of realized |θ| at close (E[|θ|], the López de Prado bar target).
            // WINSORIZE the input: a single block trade / sweep can push one bar's |θ| far past θ* (a ~2000-lot print
            // spiked θ* to 149 live). Cap the EWMA input at WinsorMult × the running estimate so one outlier bar can't
            // poison the threshold — the outlier bar itself still closed correctly on imbalance; it just doesn't get to
            // redefine "typical". No cap while warming up (imbEwma unseeded — the first bar IS the estimate).
            double absTheta  = Math.Abs(theta);
            if (absTheta > 0)
            {
                double ewmaInput = imbEwma > 0 ? Math.Min(absTheta, imbEwma * WinsorMult) : absTheta;
                imbEwma = imbEwma <= 0 ? ewmaInput : imbEwma + ImbAlpha * (ewmaInput - imbEwma);
                warmup = false;
            }

            int flowDir  = Math.Sign(theta);
            int priceDir = Math.Sign(rawClose - rawBarOpen);
            int diverge  = (threshold != double.MaxValue && flowDir != 0 && priceDir != 0 && flowDir != priceDir
                            && Math.Abs(theta) >= DivergenceFrac * threshold) ? 1 : 0;

            LogFluxBar(bars, time, flowDir, priceDir, threshold, reason);

            // HA carry for the next bar
            haPrevOpen  = haOpen;
            haPrevClose = haClose;
            prevBarClose = rawClose;
            barsThisSession++;

            // ── seed the next forming bar at the close price ──
            double nextHaOpen = HaOpen(haPrevOpen, haPrevClose);
            double seed = RoundToTick(rawClose, bars);
            AddBar(bars, RoundToTick(nextHaOpen, bars), seed, seed, seed, time, volume);
            haPrevOpen = nextHaOpen;   // the new forming bar's stored open

            theta = 0; buyVol = 0; sellVol = 0; nTicks = 0;
            rawBarOpen = rawHigh = rawLow = rawClose = rawClose;   // rawClose unchanged = the seed price
            birthTime = time;
        }

        private void UpdateFormingBar(Bars bars, DateTime time, long volume)
        {
            int last = bars.Count - 1;
            double haClose = HaClose(rawBarOpen, rawHigh, rawLow, rawClose);
            double haOpen  = bars.GetOpen(last);
            double dispHigh = Math.Max(rawHigh, Math.Max(haOpen, haClose));
            double dispLow  = Math.Min(rawLow,  Math.Min(haOpen, haClose));
            UpdateBar(bars, RoundToTick(dispHigh, bars), RoundToTick(dispLow, bars), RoundToTick(haClose, bars), time, volume);
        }

        private void InitializeFirstBar(Bars bars, double open, double high, double low, double close,
                                        DateTime time, long volume, double bid, double ask)
        {
            tickSize = bars.Instrument.MasterInstrument.TickSize;
            LatchConfig(bars);

            atrEma      = Math.Max(Math.Abs(high - low), tickSize);
            imbEwma     = 0;                 // seeded on the first backstop close
            warmup      = true;
            lastTradeSign = 1;

            theta = 0; buyVol = 0; sellVol = 0; nTicks = 0; cvd = 0;
            rawBarOpen = rawHigh = rawLow = rawClose = close;
            prevBarClose = close;
            birthTime = time;

            double haOpen  = (open + close) * 0.5;
            double haClose = HaClose(open, high, low, close);
            AddBar(bars, RoundToTick(haOpen, bars), RoundToTick(high, bars), RoundToTick(low, bars), RoundToTick(haClose, bars), time, volume);
            haPrevOpen  = haOpen;
            haPrevClose = haClose;

            barsThisSession = 1;
            sessionStart    = time;
        }

        private void LatchConfig(Bars bars)
        {
            int size = BarsPeriod.BaseBarsPeriodValue > 0 ? BarsPeriod.BaseBarsPeriodValue : FluxRefSize;
            fluxScale = (double)size / FluxRefSize;
            if (fluxScale <= 0) fluxScale = 1.0;
            if (AtrLength    < 1) AtrLength = 1;
            if (IntensityLen < 1) IntensityLen = 1;
        }

        // ── SentinelCore.FluxState publish (v1.31.0 seam) — per tick so the countdown HUD is live, realtime only ──
        private void PublishFluxTick(Bars bars, DateTime time)
        {
            try
            {
                if (tickSize <= 0) return;
                if ((NinjaTrader.Core.Globals.Now - time).TotalMinutes > RealtimePublishMinutes) return;
                string inst = bars?.Instrument?.MasterInstrument?.Name;
                if (string.IsNullOrEmpty(inst)) return;
                string scope = SentinelCore.ScopeOf(bars.Instrument, bars.BarsPeriod);
                if (string.IsNullOrEmpty(scope)) return;

                double threshold = CurrentThreshold();
                double pct = (threshold <= 0 || threshold == double.MaxValue) ? 0.0
                             : Math.Min(1.0, Math.Abs(theta) / threshold);
                int flowDir  = Math.Sign(theta);
                int priceDir = Math.Sign(rawClose - rawBarOpen);
                double tot   = buyVol + sellVol;
                double pressure = tot > 0 ? buyVol / tot : 0.5;
                int diverge = (flowDir != 0 && priceDir != 0 && flowDir != priceDir
                               && threshold != double.MaxValue && Math.Abs(theta) >= DivergenceFrac * threshold) ? 1 : 0;

                SentinelCore.SetFluxState(scope, SentinelCore.BarTag(bars.BarsPeriod), inst,
                    flowDir, priceDir, pressure, theta, threshold == double.MaxValue ? 0.0 : threshold,
                    pct, cvd, atrEma, diverge, barsThisSession, "SentinelFlux");

                if ((time - _lastFluxLog).TotalSeconds >= FluxLogThrottleSeconds)
                {
                    _lastFluxLog = time;
                    string arrow = flowDir > 0 ? "buy" : (flowDir < 0 ? "sell" : "flat");
                    SentinelCore.Log("Flux", string.Format(
                        "{0} {1} · θ {2:0} / θ* {3:0} ({4:0%}) · pres {5:0.00} · ATR {6:0.0}t · cvd {7:0} · {8} bars{9}",
                        inst, arrow, theta, threshold == double.MaxValue ? 0.0 : threshold, pct, pressure,
                        atrEma / tickSize, cvd, barsThisSession, diverge != 0 ? " · absorb" : ""));
                }
            }
            catch { }
        }

        // ── Durable per-bar DATA log — one JSONL record per COMPLETED bar, realtime only ──
        private void LogFluxBar(Bars bars, DateTime time, int flowDir, int priceDir, double threshold, string reason)
        {
            try
            {
                if (tickSize <= 0) return;
                if ((NinjaTrader.Core.Globals.Now - time).TotalMinutes > RealtimePublishMinutes) return;
                string inst = bars?.Instrument?.MasterInstrument?.Name;
                if (string.IsNullOrEmpty(inst)) return;

                int done = bars.Count - 1;   // the bar we just finalised (the fresh forming bar is AddBar'd AFTER this call)
                if (done < 0) return;
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                double tot = buyVol + sellVol;
                double pressure = tot > 0 ? buyVol / tot : 0.5;
                string fields = string.Format(ci,
                    "\"mode\":\"flux\",\"reason\":\"{0}\",\"flowDir\":{1},\"priceDir\":{2},\"theta\":{3:0.#},\"thr\":{4:0.#}," +
                    "\"pres\":{5:0.###},\"atrT\":{6:0.#},\"cvd\":{7:0.#},\"nTicks\":{8},\"n\":{9}{10}",
                    reason, flowDir, priceDir, theta, threshold == double.MaxValue ? 0.0 : threshold,
                    pressure, atrEma / tickSize, cvd, nTicks, barsThisSession,
                    (flowDir != priceDir && flowDir != 0 && priceDir != 0) ? ",\"absorb\":1" : "");
                SentinelCore.BrickLog.Append("SentinelFlux", inst, fields);
            }
            catch { }
        }

        // ── Overrides ──
        public override void ApplyDefaultBasePeriodValue(BarsPeriod period) => period.BaseBarsPeriodValue = FluxRefSize;
        public override void ApplyDefaultValue(BarsPeriod period)
        {
            period.Value               = FluxRefSize;
            period.Value2              = 0;
            period.BaseBarsPeriodValue = FluxRefSize;
        }

        public override string ChartLabel(DateTime dateTime) => Name;

        public override double GetPercentComplete(Bars bars, DateTime now)
        {
            double threshold = CurrentThreshold();
            if (threshold <= 0 || threshold == double.MaxValue) return 0;
            return Math.Max(0.0, Math.Min(1.0, Math.Abs(theta) / threshold));
        }

        // ── Utilities ──
        private double HaOpen(double priorHAOpen, double priorHAClose) => (priorHAOpen + priorHAClose) * 0.5;
        private double HaClose(double open, double high, double low, double close) => (open + high + low + close) * 0.25;
        private double RoundToTick(double price, Bars bars) => bars.Instrument.MasterInstrument.RoundToTickSize(price);
        private void SafeRemoveProperty(string name) { var p = Properties.Find(name, true); if (p != null) Properties.Remove(p); }
    }
}
