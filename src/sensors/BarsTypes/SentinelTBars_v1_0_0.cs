// ─────────────────────────────────────────────────────────────────────────────
// This Source Code Form is subject to the terms of the Mozilla Public License,
// v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
// one at https://mozilla.org/MPL/2.0/.
//
// Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
// ─────────────────────────────────────────────────────────────────────────────
// ═════════════════════════════════════════════════════════════════════════════
//  SentinelTBars — adaptive HA / Renko-hybrid "T-brick" BARS TYPE (Sentinel Suite)
//  File: SentinelTBars_v1_0_0.cs        Class/Type: SentinelTBars_v1_0_0
//  Display Name: "SentinelTBars v1.0.0"  ·  BarsPeriodType id: 212201 (reserved Sentinel bars block 212200–212299)
// ─────────────────────────────────────────────────────────────────────────────
//  WHAT THIS IS
//    The Sentinel-graded successor to the TbarsSudo family (V0001..V0003, frozen).
//    It builds Renko-style bricks with Heikin-Ashi-smoothed BODIES and REAL price
//    wicks, wrapped in adaptive machinery:
//      • ATR-adaptive brick floor  — bricks never shrink below AtrMult × ATR, so
//        size tracks volatility (the core you liked, now made correct + stable).
//      • Breakout confirmation      — a probe must survive time + penetration +
//        speed + wick-giveback (+ optional volume) before it prints a brick.
//      • Trend hysteresis           — after N same-way bricks the reversal
//        threshold widens (fewer whipsaws in a run).
//      • Density normalisation      — a per-BRICK controller nudges brick size
//        toward a target bars-per-session.
//      • Quiet-hours gating, forced time-bricks, micro-splits.
//    It PUBLISHES its adaptive read to SentinelCore.BrickState (v1.6.0 seam) so the
//    rest of the suite (GTrader21 / Eye / strategies) can consult the live brick
//    ATR + direction without re-deriving it.
//
//  RELATION TO TbarsSudoV0003 (frozen checkpoint — NOT edited)
//    Same feature set, but reworked for CORRECTNESS + REPRODUCIBILITY. Fixes applied
//    (each was a defect analysed in V0003):
//      1. DETERMINISM — V0003 re-polled TbarsSudoV3Registry every ~800ms of data
//         time and mutated brick geometry MID-STREAM, so bars repainted as ticks
//         arrived and a reload produced different bars. Here config is LATCHED ONCE
//         per session (first bar + each new session) and frozen for that session.
//         To apply new controller settings, reload the chart — the registry is a
//         session-static store, so values the controller published survive the
//         reload and are baked in at build time (conventional NT bar-param UX).
//      2. BuiltFrom = Tick — the ms / ticks-per-second confirmation gates need true
//         tick timestamps; V0003 left BuiltFrom = 0 (every other renko type uses Tick).
//      3. DENSITY CONTROLLER — V0003 nudged the scale on EVERY TICK, compounding to
//         the Min/Max rails within a few dozen ticks. Here it is a per-BRICK
//         proportional controller with a deadband + per-brick step cap, so it settles.
//      4. ATR — V0003 re-updated the EMA on every tick with the still-growing bar
//         range (over-weighting) and used the bar's OWN close as the TR "prev close".
//         Here ATR updates ONCE PER CLOSED BRICK with a correct true range (previous
//         brick's close), so the volatility floor is stable and meaningful.
//      5. CONFIRMATION CHAINING — V0003's confirmation path emitted at most one brick
//         per tick, needing a fresh confirmation wait per brick → bricks lagged price
//         on gaps. Here a confirmed breakout CHAINS the remaining full-brick distance
//         in the same tick.
//      6. GetPercentComplete — V0003 mixed barOpen and the breakout price; here a
//         single consistent brick basis is used.
//    The controller/registry (TbarsSudoV3Controller / TbarsSudoV3Config /
//    TbarsSudoV3Registry) are REUSED unchanged as the optional live-tuning surface.
//
//  CHANGELOG
//    v1.0.0 (2026-07-06) — first Sentinel-graded release; supersedes TbarsSudoV0003.
//                          Determinism latch, BuiltFrom=Tick, per-brick density,
//                          per-brick ATR, confirmation chaining, consistent %-complete,
//                          + SentinelCore.BrickState publish seam.
//    v1.0.0 (in-session)  — per-tick BrickState publish (live countdown fields) + per-brick BrickLog JSONL;
//                          BarsPeriodType 212124 → 212201 (RESERVED Sentinel bars block 212200–212299).
// ═════════════════════════════════════════════════════════════════════════════

using System;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.AddOns.Sentinel;   // SentinelCore.BrickState publish seam

namespace NinjaTrader.NinjaScript.BarsTypes
{
    public class SentinelTBars_v1_0_0 : BarsType
    {
        // RESERVED Sentinel bars BarsPeriodType block = 212200–212299 (one range for the whole family).
        //   212201 = SentinelTBars · 212202 = SentinelTbarsCount · 212203+ = future Sentinel bars.
        // (Was 212124, adjacent to the legacy TbarsSudo block 212121–212123; moved into the reserved block.)
        private const int CustomBarsPeriodTypeValue = 212201;

        // ── Tunables (defaults below; optionally LATCHED once per session from the controller registry) ──
        private bool   UseBreakoutConfirmation;
        private int    ConfirmTicksBeyond;
        private int    ConfirmMilliseconds;
        private double MinSpeedTicksPerSecond;
        private double MaxWickGivebackRatio;
        private long   MinVolumeInWindow;

        private int    AtrLength;
        private double AtrMultTrend;
        private double AtrMultReversal;
        private int    ConfirmTrendBricks;
        private double HysteresisReversalMult;

        private bool   EnableQuietHoursGating;
        private int    QuietStartHour;
        private int    QuietEndHour;
        private double QuietTicksAdd;
        private double QuietMsMult;
        private double QuietSpeedMult;

        private int    TargetBarsPerSession;
        private double AssumedSessionHours;
        private double MinScale;
        private double MaxScale;
        private double ScaleSmoothing;
        private int    ForceStagnationSeconds;
        private int    MinBarLifeSeconds;
        private double MicroSplitRatio;
        private bool   EnableMicroSplit;

        // Density controller (per-brick proportional; deadband + step cap keep it off the rails)
        private const double DensityDeadband = 0.10;   // |ratio-1| below this = no change
        private const double DensityGain     = 0.50;   // proportional gain on the density error
        private const double DensityMaxStep  = 0.05;   // max fractional scale move per brick

        // Only publish BrickState for near-realtime bricks (skip historical rebuild noise)
        private const double RealtimePublishMinutes = 5.0;
        // Throttle the human-readable BrickState log line (Output window + sentinel.log)
        private const double BrickLogThrottleSeconds = 10.0;
        private DateTime _lastBrickLog;

        // ── Dynamic state ──
        private double tickSize = 0.01;
        private double barOpen, brickBasis, barMax, barMin, syntheticOpen;
        private int    barDirection = 1;
        private double haPrevOpen, haPrevClose;

        // ATR EMA (over completed bricks)
        private double atrEma;
        private int    sameDirCount;
        private double AtrAlpha => 2.0 / (AtrLength + 1.0);

        // Base + live offsets
        private double baseOpenOffset, baseTrendOffset, baseReversalOffset;
        private double trendOffset, reversalOffset;

        // Density scaling
        private double dynScale = 1.0;
        private DateTime sessionStart;
        private int barsThisSession;
        private DateTime lastBoundaryTouch;
        private DateTime lastBarBirth;

        // Confirmation tracking
        private bool pendingBreakout;
        private int  pendingDirection;
        private double pendingBoundary, pendingFarthest;
        private DateTime pendingStartTime;
        private long pendingAccumVolume;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "SentinelTBars v1.0.0 — adaptive HA/Renko brick (ATR floor + confirmation + density). Publishes SentinelCore.BrickState.";
                Name        = "SentinelTBars v1.0.0";
                BarsPeriod  = new BarsPeriod { BarsPeriodType = (BarsPeriodType)CustomBarsPeriodTypeValue, BarsPeriodTypeName = Name };
                BuiltFrom   = BarsPeriodType.Tick;   // FIX #2: confirmation ms/speed gates need true tick timestamps
                DaysToLoad  = 5;
                IsIntraday  = true;

                // Sensible defaults (mirror the V0003 baseline; the controller can override at latch)
                UseBreakoutConfirmation = true;
                ConfirmTicksBeyond      = 1;
                ConfirmMilliseconds     = 120;
                MinSpeedTicksPerSecond  = 1.6;
                MaxWickGivebackRatio    = 0.65;
                MinVolumeInWindow       = 0;

                AtrLength              = 14;
                AtrMultTrend           = 0.80;
                AtrMultReversal        = 1.10;
                ConfirmTrendBricks     = 2;
                HysteresisReversalMult = 1.50;

                EnableQuietHoursGating = true;
                QuietStartHour         = 18;
                QuietEndHour           = 23;
                QuietTicksAdd          = 1.0;
                QuietMsMult            = 1.35;
                QuietSpeedMult         = 0.75;

                TargetBarsPerSession   = 1000;
                AssumedSessionHours    = 23.0;
                MinScale               = 0.25;
                MaxScale               = 2.5;
                ScaleSmoothing         = 0.15;

                ForceStagnationSeconds = 90;
                MinBarLifeSeconds      = 10;
                MicroSplitRatio        = 0.55;
                EnableMicroSplit       = true;
                return;
            }

            if (State == State.Configure)
            {
                // Keep the chart UI minimal: reuse the standard period fields only.
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
                InitializeFirstBar(bars, open, high, low, close, time, volume);   // sets tickSize + latches config
                bars.LastPrice = close;
                return;
            }

            // FIX #1: at a session boundary that does NOT reset the series, re-latch config so a
            // session is internally consistent (config is frozen WITHIN a session — no intrabar repaint).
            if (newSession)
                LatchConfig(bars);

            if (ShouldForceTimeBrick(time))
                ForceTimeBrick(bars, close, time, volume);

            if (UseBreakoutConfirmation)
                ProcessWithConfirmation(bars, close, time, volume);
            else
                ProcessNoConfirmation(bars, close, time, volume);

            if (EnableMicroSplit)
                MaybeMicroSplit(bars, close, time, volume);

            PublishBrickTick(bars, close, time);   // live BrickState (countdown HUD reads this) — realtime only

            bars.LastPrice = close;
        }

        // ── Config latch (FIX #1): read the controller registry ONCE per session, then freeze ──
        private void LatchConfig(Bars bars)
        {
            TbarsSudoV3Config cfg = null;
            try { TbarsSudoV3Registry.TryGetForInstrument(bars.Instrument?.FullName, out cfg); } catch { cfg = null; }

            if (cfg != null)
            {
                UseBreakoutConfirmation = cfg.UseBreakoutConfirmation;
                ConfirmTicksBeyond      = cfg.ConfirmTicksBeyond;
                ConfirmMilliseconds     = cfg.ConfirmMilliseconds;
                MinSpeedTicksPerSecond  = cfg.MinSpeedTicksPerSecond;
                MaxWickGivebackRatio    = cfg.MaxWickGivebackRatio;
                MinVolumeInWindow       = cfg.MinVolumeInWindow;

                AtrLength              = cfg.AtrLength;
                AtrMultTrend           = cfg.AtrMultTrend;
                AtrMultReversal        = cfg.AtrMultReversal;
                ConfirmTrendBricks     = cfg.ConfirmTrendBricks;
                HysteresisReversalMult = cfg.HysteresisReversalMult;

                EnableQuietHoursGating = cfg.EnableQuietHoursGating;
                QuietStartHour         = cfg.QuietStartHour;
                QuietEndHour           = cfg.QuietEndHour;
                QuietTicksAdd          = cfg.QuietTicksAdd;
                QuietMsMult            = cfg.QuietMsMult;
                QuietSpeedMult         = cfg.QuietSpeedMult;

                TargetBarsPerSession   = cfg.TargetBarsPerSession;
                AssumedSessionHours    = cfg.AssumedSessionHours;
                MinScale               = cfg.MinScale;
                MaxScale               = cfg.MaxScale;
                ScaleSmoothing         = cfg.ScaleSmoothing;
                ForceStagnationSeconds = cfg.ForceStagnationSeconds;
                MinBarLifeSeconds      = cfg.MinBarLifeSeconds;
                MicroSplitRatio        = cfg.MicroSplitRatio;
                EnableMicroSplit       = cfg.EnableMicroSplit;
            }

            // Base offsets from the effective speed values (controller overrides > BarsPeriod fields).
            int spTrend = BarsPeriod.Value, spRev = BarsPeriod.Value2, spBase = BarsPeriod.BaseBarsPeriodValue;
            if (cfg != null)
            {
                if (cfg.SpeedTrend    > 0) spTrend = cfg.SpeedTrend;
                if (cfg.SpeedReversal > 0) spRev   = cfg.SpeedReversal;
                if (cfg.SpeedBase     > 0) spBase  = cfg.SpeedBase;
            }
            baseTrendOffset    = spTrend * tickSize;
            baseReversalOffset = spRev   * tickSize;
            baseOpenOffset     = spBase  * tickSize;

            // Guard against degenerate config (AtrLength < 1 would make AtrAlpha explode).
            if (AtrLength < 1) AtrLength = 1;
            if (MinScale <= 0) MinScale = 0.01;
            if (MaxScale < MinScale) MaxScale = MinScale;
        }

        // ── Density scaling (FIX #3): PER-BRICK proportional controller, deadband + step cap ──
        private void AdjustScaleForDensityPerBrick(DateTime time)
        {
            double sessionSeconds = Math.Max(1.0, AssumedSessionHours * 3600.0);
            double elapsed        = Math.Max(1.0, (time - sessionStart).TotalSeconds);
            double progress       = Math.Min(1.0, elapsed / sessionSeconds);
            double expectedNow    = Math.Max(1.0, TargetBarsPerSession * progress);
            double ratio          = barsThisSession / expectedNow;   // >1 = too many bars → grow offsets

            double error = ratio - 1.0;
            if (Math.Abs(error) <= DensityDeadband)
                return;

            double step        = Math.Max(-DensityMaxStep, Math.Min(DensityMaxStep, error * DensityGain));
            double targetScale  = dynScale * (1.0 + step);
            targetScale         = Math.Max(MinScale, Math.Min(MaxScale, targetScale));
            dynScale           += (targetScale - dynScale) * ScaleSmoothing;
        }

        private void RefreshDynamicOffsets()
        {
            double floorTrend    = AtrMultTrend    * atrEma;
            double floorReversal = AtrMultReversal * atrEma;

            trendOffset    = Math.Max(baseTrendOffset    * dynScale, floorTrend);
            reversalOffset = Math.Max(baseReversalOffset * dynScale, floorReversal);

            if (trendOffset    < tickSize) trendOffset    = tickSize;
            if (reversalOffset < tickSize) reversalOffset = tickSize;
        }

        // ── Processing ──
        private void ProcessNoConfirmation(Bars bars, double close, DateTime time, long volume)
        {
            ChainWhileBeyond(bars, close, time, volume);        // print full bricks for the traversed distance
            UpdateExistingBar(bars, close, time, volume);       // apply the residual to the forming brick
        }

        private void ProcessWithConfirmation(Bars bars, double close, DateTime time, long volume)
        {
            bool overMax  = Cmp(bars, close, barMax) > 0;
            bool underMin = Cmp(bars, close, barMin) < 0;

            if (!overMax && !underMin)
            {
                if (pendingBreakout) ResetPendingBreakout();
                UpdateExistingBar(bars, close, time, volume);
                return;
            }

            double boundary = overMax ? barMax : barMin;
            int    dir      = overMax ? 1 : -1;

            if (!pendingBreakout || pendingDirection != dir)
            {
                StartPendingBreakout(dir, boundary, close, time, volume);
                UpdateExistingBar(bars, close, time, volume);
                return;
            }

            pendingAccumVolume += Math.Max(0L, volume);
            pendingFarthest     = dir > 0 ? Math.Max(pendingFarthest, close) : Math.Min(pendingFarthest, close);

            UpdateExistingBar(bars, close, time, volume);

            if (ShouldConfirmBreakout(time, close))
            {
                CreateBreakoutBar(bars, close, time, volume);
                ResetPendingBreakout();
                ChainWhileBeyond(bars, close, time, volume);    // FIX #5: chain the rest of a gap (already confirmed)
                UpdateExistingBar(bars, close, time, volume);
            }
            else
            {
                bool backInside = Cmp(bars, close, barMax) <= 0 && Cmp(bars, close, barMin) >= 0;
                if (backInside) ResetPendingBreakout();
            }
        }

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

            // Clean renko: the closing brick spans to the boundary in the breakout direction, keeps the
            // other extreme. Any overshoot beyond the boundary belongs to the NEXT brick (chained/updated).
            double barHigh = overMax  ? breakoutPrice : bars.GetHigh(last);
            double barLow  = underMin ? breakoutPrice : bars.GetLow(last);

            // FIX #4: update ATR ONCE per closed brick with a correct true range (previous brick's close).
            double prevBrickClose = last >= 1 ? bars.GetClose(last - 1) : bars.GetClose(last);
            UpdateAtrWithCandidate(prevBrickClose, barHigh, barLow);

            AdjustScaleForDensityPerBrick(time);   // FIX #3: per-brick density step
            RefreshDynamicOffsets();

            double haCloseBreak = GetHeikinAshiClose(bars.GetOpen(last), barHigh, barLow, breakoutPrice);
            UpdateBar(bars, barHigh, barLow, haCloseBreak, time, volume);

            int newDir = overMax ? 1 : -1;
            if (newDir == barDirection) sameDirCount++;
            else { barDirection = newDir; sameDirCount = 1; }

            syntheticOpen = RoundToTick(breakoutPrice - baseOpenOffset * barDirection, bars);

            haPrevOpen  = GetHeikinAshiOpen(haPrevOpen, haPrevClose);
            haPrevClose = haCloseBreak;

            double effectiveRev = reversalOffset * (sameDirCount >= ConfirmTrendBricks ? HysteresisReversalMult : 1.0);

            if (barDirection > 0)
            {
                barMax = RoundToTick(breakoutPrice + trendOffset, bars);
                barMin = RoundToTick(breakoutPrice - effectiveRev, bars);
            }
            else
            {
                barMax = RoundToTick(breakoutPrice + effectiveRev, bars);
                barMin = RoundToTick(breakoutPrice - trendOffset, bars);
            }

            brickBasis = breakoutPrice;    // FIX #6: single consistent reference for %-complete
            barOpen    = close;

            double nextHaOpen  = GetHeikinAshiOpen(haPrevOpen, haPrevClose);
            double nextHigh    = overMax  ? breakoutPrice : syntheticOpen;
            double nextLow     = underMin ? breakoutPrice : syntheticOpen;
            double nextHaClose = GetHeikinAshiClose(nextHaOpen, nextHigh, nextLow, breakoutPrice);

            AddBar(bars, nextHaOpen, nextHigh, nextLow, nextHaClose, time, volume);

            haPrevOpen  = nextHaOpen;
            haPrevClose = nextHaClose;

            barsThisSession++;
            lastBoundaryTouch = time;
            lastBarBirth      = time;

            LogBrick(bars, time);
        }

        // ── Time brick & micro split ──
        private bool ShouldForceTimeBrick(DateTime now)
        {
            if (lastBoundaryTouch == DateTime.MinValue || lastBarBirth == DateTime.MinValue)
                return false;
            double sinceTouch = (now - lastBoundaryTouch).TotalSeconds;
            double barAge     = (now - lastBarBirth).TotalSeconds;
            return sinceTouch > ForceStagnationSeconds && barAge > MinBarLifeSeconds;
        }

        private void ForceTimeBrick(Bars bars, double close, DateTime time, long volume)
        {
            int    last = bars.Count - 1;
            double high = bars.GetHigh(last);
            double low  = bars.GetLow(last);

            // FIX #4: ATR updates on this closed brick too, so a stagnation brick doesn't leave ATR stale.
            double prevBrickClose = last >= 1 ? bars.GetClose(last - 1) : bars.GetClose(last);
            UpdateAtrWithCandidate(prevBrickClose, high, low);

            AdjustScaleForDensityPerBrick(time);
            RefreshDynamicOffsets();

            double haClose = GetHeikinAshiClose(bars.GetOpen(last), high, low, close);
            UpdateBar(bars, high, low, haClose, time, volume);

            barDirection  = close >= brickBasis ? 1 : -1;
            syntheticOpen = close;

            haPrevOpen  = GetHeikinAshiOpen(haPrevOpen, haPrevClose);
            haPrevClose = haClose;

            brickBasis = barOpen = close;

            if (barDirection > 0)
            {
                barMax = RoundToTick(barOpen + trendOffset, bars);
                barMin = RoundToTick(barOpen - reversalOffset, bars);
            }
            else
            {
                barMax = RoundToTick(barOpen + reversalOffset, bars);
                barMin = RoundToTick(barOpen - trendOffset, bars);
            }

            double haOpen = GetHeikinAshiOpen(haPrevOpen, haPrevClose);
            AddBar(bars, haOpen, barOpen, barOpen, haOpen, time, volume);

            haPrevOpen  = haOpen;
            haPrevClose = haOpen;

            barsThisSession++;
            lastBoundaryTouch = time;
            lastBarBirth      = time;

            LogBrick(bars, time);
        }

        private void MaybeMicroSplit(Bars bars, double close, DateTime time, long volume)
        {
            int last = bars.Count - 1;
            if (last < 0) return;

            double rangeSoFar  = Math.Abs(bars.GetHigh(last) - bars.GetLow(last));
            double targetRange = Math.Abs(barMax - barMin);
            if (targetRange <= 0) return;

            double frac = rangeSoFar / targetRange;
            if (frac >= MicroSplitRatio && (time - lastBarBirth).TotalSeconds > MinBarLifeSeconds / 2.0)
                ForceTimeBrick(bars, close, time, volume);
        }

        // ── ATR & confirmation ──
        private void UpdateAtrWithCandidate(double prevClose, double h, double l)
        {
            double tr = Math.Max(h - l, Math.Max(Math.Abs(h - prevClose), Math.Abs(l - prevClose)));
            if (tr <= 0) tr = tickSize;
            atrEma = atrEma <= 0 ? tr : atrEma + AtrAlpha * (tr - atrEma);
        }

        private void StartPendingBreakout(int dir, double boundary, double price, DateTime time, long volume)
        {
            pendingBreakout    = true;
            pendingDirection   = dir;
            pendingBoundary    = boundary;
            pendingFarthest    = price;
            pendingStartTime   = time;
            pendingAccumVolume = Math.Max(0L, volume);
        }

        private void ResetPendingBreakout()
        {
            pendingBreakout    = false;
            pendingDirection   = 0;
            pendingBoundary    = 0;
            pendingFarthest    = 0;
            pendingAccumVolume = 0;
            pendingStartTime   = DateTime.MinValue;
        }

        private bool InQuietHours(DateTime t)
        {
            if (!EnableQuietHoursGating) return false;
            int hour = t.Hour;
            if (QuietStartHour <= QuietEndHour)
                return hour >= QuietStartHour && hour <= QuietEndHour;
            return hour >= QuietStartHour || hour <= QuietEndHour;
        }

        private bool ShouldConfirmBreakout(DateTime now, double currentPrice)
        {
            double ms       = (now - pendingStartTime).TotalMilliseconds;
            double msThresh = ConfirmMilliseconds * (InQuietHours(now) ? QuietMsMult : 1.0);
            if (ms < msThresh) return false;

            double penetrationTicks = Math.Abs((pendingFarthest - pendingBoundary) / tickSize);
            double ticksThresh      = ConfirmTicksBeyond + (InQuietHours(now) ? QuietTicksAdd : 0.0);
            if (penetrationTicks < ticksThresh) return false;

            double tps         = penetrationTicks / Math.Max(0.001, (now - pendingStartTime).TotalSeconds);
            double speedThresh = MinSpeedTicksPerSecond * (InQuietHours(now) ? QuietSpeedMult : 1.0);
            if (tps < speedThresh) return false;

            double givebackTicks = Math.Abs((pendingFarthest - currentPrice) / tickSize);
            double givebackRatio = penetrationTicks <= 0 ? 1.0 : givebackTicks / penetrationTicks;
            if (givebackRatio > MaxWickGivebackRatio) return false;

            if (MinVolumeInWindow > 0 && pendingAccumVolume < MinVolumeInWindow) return false;

            return true;
        }

        // ── Initialization ──
        private void InitializeFirstBar(Bars bars, double open, double high, double low, double close, DateTime time, long volume)
        {
            tickSize = bars.Instrument.MasterInstrument.TickSize;

            LatchConfig(bars);   // FIX #1: latch config now that tickSize is known; base offsets computed inside

            dynScale = 1.0;
            atrEma   = Math.Max(Math.Abs(high - low), tickSize);

            RefreshDynamicOffsets();

            brickBasis = barOpen = open;
            barMax     = barOpen + trendOffset;
            barMin     = barOpen - trendOffset;

            double haOpenInitial  = (open + close) * 0.5;
            double haCloseInitial = GetHeikinAshiClose(open, high, low, close);
            AddBar(bars, haOpenInitial, high, low, haCloseInitial, time, volume);
            haPrevOpen  = haOpenInitial;
            haPrevClose = haCloseInitial;

            barsThisSession   = 1;
            sessionStart      = time;
            lastBoundaryTouch = time;
            lastBarBirth      = time;
            barDirection      = 1;
            sameDirCount      = 1;
            pendingBreakout   = false;
        }

        // ── SentinelCore.BrickState publish (v1.6.0/1.6.1 seam) — PER TICK so the countdown HUD is live ──
        private void PublishBrickTick(Bars bars, double close, DateTime time)
        {
            // Realtime only — a historical rebuild must not stamp a stale brick as "fresh"
            // (consumers freshness-gate on UpdatedUtc). Fail-safe: never let telemetry throw into the bar path.
            try
            {
                if (tickSize <= 0) return;
                if ((NinjaTrader.Core.Globals.Now - time).TotalMinutes > RealtimePublishMinutes) return;
                string inst = bars?.Instrument?.MasterInstrument?.Name;
                if (string.IsNullOrEmpty(inst)) return;

                // SentinelCore ≥ v1.19.0 — publish by SCOPE. A bars type IS the chart's bar type, so its scope is
                // simply ScopeOf(bars.Instrument, bars.BarsPeriod): two GC charts on DIFFERENT brick settings now
                // publish distinct BrickStates instead of overwriting each other. Driven per tick, so no heartbeat.
                string scope = SentinelCore.ScopeOf(bars.Instrument, bars.BarsPeriod);
                if (string.IsNullOrEmpty(scope)) return;

                double ticksToUpper = Math.Max(0.0, (barMax - close) / tickSize);
                double ticksToLower = Math.Max(0.0, (close - barMin) / tickSize);
                double nearest      = Math.Min(ticksToUpper, ticksToLower);

                SentinelCore.SetBrickState(scope, SentinelCore.BarTag(bars.BarsPeriod), inst,
                                           barDirection, atrEma, trendOffset, reversalOffset,
                                           dynScale, sameDirCount, barsThisSession, pendingBreakout,
                                           barMax, barMin, ticksToUpper, ticksToLower, nearest, "SentinelTBars");

                // Throttled human-readable heartbeat (Output window live + sentinel.log for audit).
                if ((time - _lastBrickLog).TotalSeconds >= BrickLogThrottleSeconds)
                {
                    _lastBrickLog = time;
                    string arrow = barDirection > 0 ? "up" : "dn";
                    SentinelCore.Log("TBars", string.Format(
                        "{0} {1} · ATR {2:0.0}t · trend {3:0.0}t rev {4:0.0}t · dens {5:0.00} · run {6} · {7} bricks · next {8:0}t{9}",
                        inst, arrow, atrEma / tickSize, trendOffset / tickSize, reversalOffset / tickSize,
                        dynScale, sameDirCount, barsThisSession, nearest, pendingBreakout ? " · pending" : ""));
                }
            }
            catch { }
        }

        // ── Durable per-brick DATA log (v1.6.1) — one JSONL record per COMPLETED brick, realtime only ──
        private void LogBrick(Bars bars, DateTime time)
        {
            try
            {
                if (tickSize <= 0) return;
                if ((NinjaTrader.Core.Globals.Now - time).TotalMinutes > RealtimePublishMinutes) return;
                string inst = bars?.Instrument?.MasterInstrument?.Name;
                if (string.IsNullOrEmpty(inst)) return;

                int done = bars.Count - 2;   // the brick that just closed (a new forming brick was AddBar'd after it)
                if (done < 0) return;

                var ci = System.Globalization.CultureInfo.InvariantCulture;
                double o = bars.GetOpen(done), h = bars.GetHigh(done), l = bars.GetLow(done), c = bars.GetClose(done);
                string fields = string.Format(ci,
                    "\"mode\":\"adaptive\",\"dir\":{0},\"o\":{1:0.#####},\"h\":{2:0.#####},\"l\":{3:0.#####},\"c\":{4:0.#####}," +
                    "\"sizeT\":{5:0.#},\"atrT\":{6:0.#},\"trendT\":{7:0.#},\"revT\":{8:0.#},\"dens\":{9:0.###},\"run\":{10},\"n\":{11},\"vol\":{12}",
                    barDirection, o, h, l, c,
                    Math.Abs(h - l) / tickSize, atrEma / tickSize, trendOffset / tickSize, reversalOffset / tickSize,
                    dynScale, sameDirCount, barsThisSession, bars.GetVolume(done));
                SentinelCore.BrickLog.Append("SentinelTBars", inst, fields);
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
