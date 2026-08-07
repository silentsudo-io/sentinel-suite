// ─────────────────────────────────────────────────────────────────────────────
// This Source Code Form is subject to the terms of the Mozilla Public License,
// v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
// one at https://mozilla.org/MPL/2.0/.
//
// Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
// ─────────────────────────────────────────────────────────────────────────────
// ═════════════════════════════════════════════════════════════════════════════
//  SentinelDrift — asymmetric, conviction-adaptive "drift brick" BARS TYPE  [PROTOTYPE]
//  File: SentinelDrift_v0_1_0.cs        Class/Type: SentinelDrift_v0_1_0
//  Display Name: "SentinelDrift v0.1.0"  ·  BarsPeriodType id: 212204 (reserved Sentinel bars block 212200–212299)
// ─────────────────────────────────────────────────────────────────────────────
//  WHAT THIS IS  (and why it exists)
//    A Renko-style brick whose TWO thresholds are FIRST-CLASS and asymmetric:
//      • continuation edge — cheap: how far price must go to PRINT-in-trend.
//      • reversal edge      — expensive: how far AGAINST the trend to flip.
//    "Drift" = the directional (trend) term of a diffusion. The brick drifts with
//    the trend easily and RESISTS reversal in proportion to conviction.
//
//    This idea already lives, HIDDEN, inside SentinelTBars: its "Speed Settings"
//    knob collapses to trend=SS/2, reversal=SS*2 — a fixed, un-tunable 4:1 bias,
//    plus a binary hysteresis latch (reversal ×1.5 after N same-way bricks).
//    SentinelDrift makes that asymmetry a DIAL and replaces the on/off latch with
//    a SMOOTH conviction curve:
//      • Trend Bias    (Value)              reversal ÷ continuation. 1 = symmetric
//                                           renko; 4 = TBars' default; higher = trend-rider.
//      • Conviction×10 (Value2)             ceiling on how much a persistent run
//                                           widens the reversal edge. 10 = OFF (pure
//                                           static bias); 15 = up to 1.5× at full run.
//      • Speed Settings(BaseBarsPeriodValue) overall brick size (continuation = SS/2 ticks).
//
//    It keeps the PROVEN TBars core (ATR-adaptive floor, breakout confirmation,
//    Heikin-Ashi bodies + real wicks, stagnation time-brick) and DROPS the parts
//    orthogonal to the asymmetry story (density controller, quiet-hours, micro-split,
//    the live registry latch) so the ONLY behavioural difference vs TBars is the bias
//    + conviction curve — which makes A/B measurement clean.
//
//    Publishes to SentinelCore.BrickState under its OWN scope (distinct BarsPeriodType
//    id ⇒ distinct bartag ⇒ no collision with a TBars chart), so nothing else changes.
//
//  ⚠⚠ CANDLE COLOUR IS NOT BRICK DIRECTION — Drift inherits TBars' HEIKIN-ASHI bodies,
//      so a body is coloured by the smoothed average, not by the brick that printed;
//      near a turn they routinely disagree. Authoritative direction is
//      SentinelCore.BrickState.Direction, never the pixel. Wicks are real prices; the
//      BODY is synthetic — never record an HA close as a fill. Full note in the
//      SentinelTBars_v1_0_0.cs header. (Reported by sneaky_zekey, who had to write this
//      warning into his own tool because ours did not carry it.)
//
//  ⏭ STAGE-2 HOOK (not in this prototype): fold ORDER-FLOW agreement into the
//    conviction multiplier — flow confirming the trend makes the brick stickier,
//    absorption against it makes it twitchier. See ApplyConviction() below.
//
//  ⚠ BARS TYPES ARE STICKY ACROSS A COMPILE. nt8bridge/F5 VALIDATES but does not
//    hot-swap a live bars-type instance — after changing this file you must
//    Editor-F5 AND reload the chart (see [[sentinel-flux-tool]] bring-up lesson).
//
//  CHANGELOG
//    v0.1.0 (2026-07-19) — first prototype. Trend Bias dial + smooth conviction
//                          hysteresis, on the TBars-proven construction core.
//    v0.1.0 (same day)    — DIAL-BUG FIX: the original stagnation ForceTimeBrick re-derived direction + reset the
//                          run, flipping the trend on a cheap move and MASKING the Trend Bias dial (bias 4 vs 20
//                          looked identical). Made it CONTINUATION-ONLY (keeps barDirection + run; reversal edge
//                          stays effRev away). Dial now scales as designed: bias 4 → 24t reversal, bias 20 → 120t.
//    v0.1.0 (Stage 2)     — FLOW-ADAPTIVE REVERSAL: sign the tape (quote rule → tick-rule) into per-brick delta;
//                          a flow factor (~0.6x..1.4x, self-calibrated) scales the reversal edge — flow confirming
//                          the trend widens it (sticky), absorption tightens it (twitchy). ⚠ needs TICK data; gated
//                          inert on isBar/no-tick rebuilds. NEXT: 2b = publish ConvictionBias → Council voter.
// ═════════════════════════════════════════════════════════════════════════════

using System;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.AddOns.Sentinel;   // SentinelCore.BrickState publish seam

namespace NinjaTrader.NinjaScript.BarsTypes
{
    public class SentinelDrift_v0_1_0 : BarsType
    {
        // RESERVED Sentinel bars block 212200–212299. 212201=TBars · 212202=TbarsCount · 212203=Flux · 212204=Drift.
        private const int CustomBarsPeriodTypeValue = 212204;

        // ── Asymmetry (latched once per session from the period fields) ──
        private int    trendBias;            // Value          — reversal ÷ continuation (≥1)
        private double maxConvictionBoost;   // Value2 / 10    — max reversal widening from a persistent run (≥1)
        private const double ConvictionTau = 3.0;   // run-length scale of the conviction curve (bricks)

        // ── STAGE 2: flow-adaptive reversal (order-flow makes the bar clock a conviction instrument) ──
        // Signed tape delta modulates the REVERSAL edge: flow confirming the trend → wider reversal (ride it);
        // flow absorbing against it → tighter reversal (exit fast). ⚠ needs real TICK data (bid/ask + per-trade
        // volume) — inert on bar-based/historical rebuilds (isBar), so it never corrupts a no-tick chart.
        private bool   EnableFlowAdaptive = true;
        private double FlowInfluence      = 0.40;   // reversal ranges ~0.6x .. 1.4x with full flow (dis)agreement

        // ── ATR-adaptive floor (from TBars; keeps bricks sane in changing vol) ──
        private int    AtrLength       = 14;
        private double AtrMultTrend    = 0.80;
        private double AtrMultReversal = 1.10;

        // ── Breakout confirmation (a probe must survive time + penetration + speed + wick-giveback) ──
        private bool   UseBreakoutConfirmation = true;
        private int    ConfirmTicksBeyond      = 1;
        private int    ConfirmMilliseconds     = 120;
        private double MinSpeedTicksPerSecond  = 1.6;
        private double MaxWickGivebackRatio    = 0.65;

        // ── Stagnation time-brick (don't hang forever in a dead tape) ──
        // v0.1.0 dial-bug fix: the ORIGINAL ForceTimeBrick re-derived direction (close vs brickBasis) + reset the run,
        // so on stagnation it flipped the trend on a cheap move and MASKED the Trend Bias dial (proven: bias 4 vs 20
        // were indistinguishable until this was gated off). ForceTimeBrick is now CONTINUATION-ONLY (keeps barDirection
        // + the run; reversal edge stays effRev away), so it can't flip — safe to re-enable.
        private bool EnableTimeBrick       = true;
        private int ForceStagnationSeconds = 90;
        private int MinBarLifeSeconds      = 10;

        // Only publish BrickState for near-realtime bricks (skip historical rebuild noise).
        private const double RealtimePublishMinutes  = 5.0;
        private const double BrickLogThrottleSeconds = 10.0;
        private DateTime _lastBrickLog;

        // ── Dynamic state ──
        private double tickSize = 0.01;
        private double barOpen, brickBasis, barMax, barMin, syntheticOpen;
        private int    barDirection = 1;
        private double haPrevOpen, haPrevClose;

        private double atrEma;
        private int    sameDirCount;
        private double AtrAlpha => 2.0 / (AtrLength + 1.0);

        private double baseContinuationOffset, baseReversalOffset, baseOpenOffset;
        private double trendOffset, reversalOffset;
        private double convictionMult = 1.0;   // current reversal-widening factor (telemetry)
        // STAGE 2 flow state
        private double deltaAccum;              // signed volume accumulated in the forming brick
        private double deltaNormEma;            // slow EMA of |brick delta| — self-calibrating normaliser
        private double flowFactor = 1.0;        // current reversal flow modulation (telemetry)
        private double _deltaLastPrice;         // prior trade price for the tick-rule fallback
        private int    _lastTickDir = 1;        // last inferred aggressor side (carry on flat)
        private bool   _flowHasTicks;           // did the forming brick see real tick data?
        // STAGE 2b — flow-confirmed conviction bias published to the Council (CVB voter)
        private int    _flowDir;                // sign of the last brick's net signed delta (net tape direction)
        private int    _cvbBias;                // flow-confirmed vote: brick direction when flow confirms it, else 0
        private double _cvbConviction;          // |flow agreement| 0..1
        private int    _cvbDivergence;          // 1 when flow absorbs against the brick direction
        private const double FlowConfirmThreshold = 0.15;   // min |agree| to confirm a vote / flag divergence

        private DateTime sessionStart, lastBoundaryTouch, lastBarBirth;
        private int      barsThisSession;

        // Confirmation tracking
        private bool     pendingBreakout;
        private int      pendingDirection;
        private double   pendingBoundary, pendingFarthest;
        private DateTime pendingStartTime;
        private long     pendingAccumVolume;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "SentinelDrift v0.1.0 [prototype] — asymmetric conviction brick: cheap continuation, "
                            + "expensive (conviction-scaled) reversal. Publishes SentinelCore.BrickState.";
                Name        = "SentinelDrift v0.1.0";
                BarsPeriod  = new BarsPeriod { BarsPeriodType = (BarsPeriodType)CustomBarsPeriodTypeValue, BarsPeriodTypeName = Name };
                BuiltFrom   = BarsPeriodType.Tick;   // confirmation ms/speed gates need true tick timestamps
                DaysToLoad  = 5;
                IsIntraday  = true;
                return;
            }

            if (State == State.Configure)
            {
                SafeRemoveProperty("BaseBarsPeriodType");
                SafeRemoveProperty("PointAndFigurePriceType");
                SafeRemoveProperty("ReversalType");
                SetPropertyName("BaseBarsPeriodValue", "Speed Settings");
                SetPropertyName("Value",  "Trend Bias");        // reversal ÷ continuation
                SetPropertyName("Value2", "Conviction x10");    // max run-widening (10 = off)
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
                InitializeFirstBar(bars, open, high, low, close, time, volume);
                bars.LastPrice = close;
                return;
            }

            // Re-latch at a session boundary that does NOT reset the series (config frozen within a session — no repaint).
            if (newSession)
                LatchConfig(bars);

            AccumulateDelta(close, bid, ask, volume, isBar);   // STAGE 2: signed order-flow delta for the forming brick

            if (EnableTimeBrick && ShouldForceTimeBrick(time))
                ForceTimeBrick(bars, close, time, volume);

            if (UseBreakoutConfirmation)
                ProcessWithConfirmation(bars, close, time, volume);
            else
                ProcessNoConfirmation(bars, close, time, volume);

            PublishBrickTick(bars, close, time);   // live BrickState — realtime only

            bars.LastPrice = close;
        }

        // ── Config latch: read the period fields ONCE per session, compute base offsets, then freeze ──
        private void LatchConfig(Bars bars)
        {
            int    ss      = Math.Max(2, BarsPeriod.BaseBarsPeriodValue);   // Speed Settings
            trendBias      = Math.Max(1, BarsPeriod.Value);                 // reversal ÷ continuation
            maxConvictionBoost = Math.Max(1.0, BarsPeriod.Value2 / 10.0);   // 10 → 1.0 (off)

            double continuationTicks = ss / 2.0;
            baseContinuationOffset   = continuationTicks * tickSize;
            baseReversalOffset       = baseContinuationOffset * trendBias;  // Bias=4 reproduces TBars' 4:1
            baseOpenOffset           = ss * tickSize;                       // synthetic-open back-shift (matches TBars)

            if (AtrLength < 1) AtrLength = 1;
        }

        // Smooth conviction curve — REPLACES TBars' binary hysteresis latch. A persistent same-direction run
        // widens the reversal edge, saturating toward maxConvictionBoost. A fresh reversal is never pre-boosted.
        //   ⏭ STAGE-2 HOOK: multiply in an order-flow agreement term here (>1 flow confirms → stickier;
        //      <1 absorption against the trend → twitchier) to make the bar CLOCK a conviction instrument.
        private double ApplyConviction()
        {
            double run  = Math.Max(0, sameDirCount - 1);
            convictionMult = 1.0 + (maxConvictionBoost - 1.0) * (1.0 - Math.Exp(-run / ConvictionTau));
            return convictionMult;
        }

        // STAGE 2: sign each trade (quote rule → tick-rule fallback) and accumulate delta for the forming brick.
        // Skipped for bar-based data (isBar) so a no-tick historical rebuild leaves flowFactor neutral (=1).
        private void AccumulateDelta(double close, double bid, double ask, long volume, bool isBar)
        {
            if (!EnableFlowAdaptive || isBar || volume <= 0) { _deltaLastPrice = close; return; }
            int sign;
            if      (ask > 0 && close >= ask) sign = 1;
            else if (bid > 0 && close <= bid) sign = -1;
            else if (close > _deltaLastPrice) sign = 1;
            else if (close < _deltaLastPrice) sign = -1;
            else                              sign = _lastTickDir;
            _lastTickDir    = sign;
            deltaAccum     += sign * volume;
            _deltaLastPrice = close;
            _flowHasTicks   = true;
        }

        // STAGE 2: convert the just-closed brick's signed delta into a reversal multiplier, then reset for the next
        // brick. agree>0 = flow confirms the (new) direction → wider reversal (sticky); agree<0 = absorption → tighter.
        private void UpdateFlowFactor()
        {
            if (!EnableFlowAdaptive || !_flowHasTicks)
            {
                // no tick flow → neutral reversal AND CVB abstains (no confirmation available)
                flowFactor = 1.0; _flowDir = 0; _cvbBias = 0; _cvbConviction = 0; _cvbDivergence = 0;
                deltaAccum = 0; _flowHasTicks = false; return;
            }
            double mag   = Math.Abs(deltaAccum);
            deltaNormEma = deltaNormEma <= 0 ? mag : deltaNormEma + 0.1 * (mag - deltaNormEma);
            double agree = (barDirection * deltaAccum) / Math.Max(1.0, deltaNormEma);
            agree        = Math.Max(-1.0, Math.Min(1.0, agree));
            flowFactor   = 1.0 + FlowInfluence * agree;
            // STAGE 2b — the flow-confirmed conviction bias published to the Council (CVB voter):
            _flowDir       = Math.Sign(deltaAccum);
            _cvbConviction = Math.Abs(agree);
            _cvbBias       = agree >=  FlowConfirmThreshold ? barDirection : 0;   // vote the trend only when flow confirms it
            _cvbDivergence = agree <= -FlowConfirmThreshold ? 1 : 0;              // flow absorbing against the brick direction
            deltaAccum   = 0;
            _flowHasTicks = false;
        }

        private void RefreshDynamicOffsets()
        {
            trendOffset    = Math.Max(baseContinuationOffset, AtrMultTrend    * atrEma);
            reversalOffset = Math.Max(baseReversalOffset,     AtrMultReversal * atrEma);
            if (trendOffset    < tickSize) trendOffset    = tickSize;
            if (reversalOffset < tickSize) reversalOffset = tickSize;
        }

        // ── Processing ──
        private void ProcessNoConfirmation(Bars bars, double close, DateTime time, long volume)
        {
            ChainWhileBeyond(bars, close, time, volume);
            UpdateExistingBar(bars, close, time, volume);
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
                ChainWhileBeyond(bars, close, time, volume);   // chain the rest of a gap (already confirmed)
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

            double barHigh = overMax  ? breakoutPrice : bars.GetHigh(last);
            double barLow  = underMin ? breakoutPrice : bars.GetLow(last);

            // ATR updates ONCE per closed brick with a correct true range (previous brick's close).
            double prevBrickClose = last >= 1 ? bars.GetClose(last - 1) : bars.GetClose(last);
            UpdateAtrWithCandidate(prevBrickClose, barHigh, barLow);
            RefreshDynamicOffsets();

            double haCloseBreak = GetHeikinAshiClose(bars.GetOpen(last), barHigh, barLow, breakoutPrice);
            UpdateBar(bars, barHigh, barLow, haCloseBreak, time, volume);

            int newDir = overMax ? 1 : -1;
            if (newDir == barDirection) sameDirCount++;
            else { barDirection = newDir; sameDirCount = 1; }

            syntheticOpen = RoundToTick(breakoutPrice - baseOpenOffset * barDirection, bars);

            haPrevOpen  = GetHeikinAshiOpen(haPrevOpen, haPrevClose);
            haPrevClose = haCloseBreak;

            // ── the asymmetry: cheap continuation edge, conviction- + FLOW-scaled reversal edge ──
            UpdateFlowFactor();   // STAGE 2: fold order-flow agreement into the reversal (confirm→sticky, absorb→twitchy)
            double effectiveRev = reversalOffset * ApplyConviction() * flowFactor;

            if (barDirection > 0)
            {
                barMax = RoundToTick(breakoutPrice + trendOffset,   bars);
                barMin = RoundToTick(breakoutPrice - effectiveRev,  bars);
            }
            else
            {
                barMax = RoundToTick(breakoutPrice + effectiveRev,  bars);
                barMin = RoundToTick(breakoutPrice - trendOffset,   bars);
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
            lastBoundaryTouch = time;
            lastBarBirth      = time;

            LogBrick(bars, time);
        }

        // ── Stagnation time-brick ──
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

            double prevBrickClose = last >= 1 ? bars.GetClose(last - 1) : bars.GetClose(last);
            UpdateAtrWithCandidate(prevBrickClose, high, low);
            RefreshDynamicOffsets();

            double haClose = GetHeikinAshiClose(bars.GetOpen(last), high, low, close);
            UpdateBar(bars, high, low, haClose, time, volume);

            syntheticOpen = close;   // continuation-only: KEEP barDirection (never re-derive → never a cheap flip)

            haPrevOpen  = GetHeikinAshiOpen(haPrevOpen, haPrevClose);
            haPrevClose = haClose;

            brickBasis = barOpen = close;

            // Continuation-only: KEEP the run (a stagnation pause is not a reversal), so the reversal edge stays
            // effRev away in the current direction — the time-brick can never flip the trend on a cheap move.
            UpdateFlowFactor();
            double effRev  = reversalOffset * ApplyConviction() * flowFactor;

            if (barDirection > 0)
            {
                barMax = RoundToTick(barOpen + trendOffset, bars);
                barMin = RoundToTick(barOpen - effRev,      bars);
            }
            else
            {
                barMax = RoundToTick(barOpen + effRev,      bars);
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

        private bool ShouldConfirmBreakout(DateTime now, double currentPrice)
        {
            double ms = (now - pendingStartTime).TotalMilliseconds;
            if (ms < ConfirmMilliseconds) return false;

            double penetrationTicks = Math.Abs((pendingFarthest - pendingBoundary) / tickSize);
            if (penetrationTicks < ConfirmTicksBeyond) return false;

            double tps = penetrationTicks / Math.Max(0.001, (now - pendingStartTime).TotalSeconds);
            if (tps < MinSpeedTicksPerSecond) return false;

            double givebackTicks = Math.Abs((pendingFarthest - currentPrice) / tickSize);
            double givebackRatio = penetrationTicks <= 0 ? 1.0 : givebackTicks / penetrationTicks;
            if (givebackRatio > MaxWickGivebackRatio) return false;

            return true;
        }

        // ── Initialization ──
        private void InitializeFirstBar(Bars bars, double open, double high, double low, double close, DateTime time, long volume)
        {
            tickSize = bars.Instrument.MasterInstrument.TickSize;

            LatchConfig(bars);   // base offsets now that tickSize is known

            atrEma = Math.Max(Math.Abs(high - low), tickSize);
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
            convictionMult    = 1.0;
            flowFactor        = 1.0;
            deltaAccum        = 0;
            _deltaLastPrice   = open;
            _flowHasTicks     = false;
            pendingBreakout   = false;
        }

        // ── SentinelCore.BrickState publish — realtime only (a rebuild must not stamp a stale brick as "fresh") ──
        private void PublishBrickTick(Bars bars, double close, DateTime time)
        {
            try
            {
                if (tickSize <= 0) return;
                if (!SentinelCore.ReplayMode && (NinjaTrader.Core.Globals.Now - time).TotalMinutes > RealtimePublishMinutes) return;
                // ^ v1.38.0 REPLAY MODE: Globals.Now is WALL-CLOCK even in Playback, so in a replay every bar
                //   reads weeks stale and this guard returned on EVERY tick -> the seam never published ->
                //   BRK/FLUX could not vote in ANY replay bake. Sentineleplay.on bypasses it on a bake node
                //   (no live consumers there). Guard itself is unchanged for live boxes.
                string inst = bars?.Instrument?.MasterInstrument?.Name;
                if (string.IsNullOrEmpty(inst)) return;

                // Distinct BarsPeriodType id ⇒ distinct scope/bartag ⇒ cannot collide with a TBars chart's BrickState.
                string scope = SentinelCore.ScopeOf(bars.Instrument, bars.BarsPeriod);
                if (string.IsNullOrEmpty(scope)) return;

                double ticksToUpper = Math.Max(0.0, (barMax - close) / tickSize);
                double ticksToLower = Math.Max(0.0, (close - barMin) / tickSize);
                double nearest      = Math.Min(ticksToUpper, ticksToLower);

                // convictionMult rides in the "scale" slot — the interesting adaptive read for this bar type.
                SentinelCore.SetBrickState(scope, SentinelCore.BarTag(bars.BarsPeriod), inst,
                                           barDirection, atrEma, trendOffset, reversalOffset,
                                           convictionMult, sameDirCount, barsThisSession, pendingBreakout,
                                           barMax, barMin, ticksToUpper, ticksToLower, nearest, "SentinelDrift");

                // STAGE 2b — publish the flow-confirmed conviction bias (Council CVB voter). Same scope + realtime gate.
                SentinelCore.SetConvictionState(scope, SentinelCore.BarTag(bars.BarsPeriod), inst,
                                                _cvbBias, _flowDir, barDirection, _cvbConviction,
                                                flowFactor, _cvbDivergence, barsThisSession, "SentinelDrift");

                // v1.40.0 BEACON — Drift publishes BOTH seams, so it beacons both. See SentinelCore.Beacon:
                // an F5 leaves this bars-type instance on the OLD assembly, publishing into a store the
                // rebuilt Council never reads. The beacon turns that silent split into a loud "restart NT".
                SentinelCore.Beacon(scope, "BRK");
                SentinelCore.Beacon(scope, "CVB");

                // v0.1.1 (2026-07-25) — throttle on WALL-CLOCK, not bar time (see SentinelTBars v1.0.1): a
                // bar-time throttle does not throttle at all during a historical rebuild/replay.
                if ((DateTime.UtcNow - _lastBrickLog).TotalSeconds >= BrickLogThrottleSeconds)
                {
                    _lastBrickLog = DateTime.UtcNow;
                    string arrow = barDirection > 0 ? "up" : "dn";
                    SentinelCore.Log("Drift", string.Format(
                        "{0} {1} · ATR {2:0.0}t · cont {3:0.0}t rev {4:0.0}t · bias {5} · conv {6:0.00}× · flow {7:0.00}× · cvb {8} · run {9} · {10} bricks · next {11:0}t{12}",
                        inst, arrow, atrEma / tickSize, trendOffset / tickSize, reversalOffset / tickSize,
                        trendBias, convictionMult, flowFactor, _cvbBias, sameDirCount, barsThisSession, nearest,
                        pendingBreakout ? " · pending" : ""));
                }
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelDrift.PublishBrickTick", _sx); }
        }

        // ── Durable per-brick DATA log — one JSONL record per COMPLETED brick, realtime only ──
        private void LogBrick(Bars bars, DateTime time)
        {
            try
            {
                if (tickSize <= 0) return;
                if (!SentinelCore.ReplayMode && (NinjaTrader.Core.Globals.Now - time).TotalMinutes > RealtimePublishMinutes) return;
                // ^ v1.38.0 REPLAY MODE: Globals.Now is WALL-CLOCK even in Playback, so in a replay every bar
                //   reads weeks stale and this guard returned on EVERY tick -> the seam never published ->
                //   BRK/FLUX could not vote in ANY replay bake. Sentineleplay.on bypasses it on a bake node
                //   (no live consumers there). Guard itself is unchanged for live boxes.
                string inst = bars?.Instrument?.MasterInstrument?.Name;
                if (string.IsNullOrEmpty(inst)) return;

                int done = bars.Count - 2;   // the brick that just closed (a new forming brick was AddBar'd after it)
                if (done < 0) return;

                var ci = System.Globalization.CultureInfo.InvariantCulture;
                double o = bars.GetOpen(done), h = bars.GetHigh(done), l = bars.GetLow(done), c = bars.GetClose(done);
                string fields = string.Format(ci,
                    "\"mode\":\"drift\",\"dir\":{0},\"o\":{1:0.#####},\"h\":{2:0.#####},\"l\":{3:0.#####},\"c\":{4:0.#####}," +
                    "\"sizeT\":{5:0.#},\"atrT\":{6:0.#},\"trendT\":{7:0.#},\"revT\":{8:0.#},\"bias\":{9},\"convMult\":{10:0.###},\"run\":{11},\"n\":{12},\"vol\":{13}",
                    barDirection, o, h, l, c,
                    Math.Abs(h - l) / tickSize, atrEma / tickSize, trendOffset / tickSize, reversalOffset / tickSize,
                    trendBias, convictionMult, sameDirCount, barsThisSession, bars.GetVolume(done));
                SentinelCore.BrickLog.Append("SentinelDrift", inst, fields);
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelDrift.LogBrick", _sx); }
        }

        // ── Overrides ──
        public override void ApplyDefaultBasePeriodValue(BarsPeriod period) => period.BaseBarsPeriodValue = 12;
        public override void ApplyDefaultValue(BarsPeriod period)
        {
            period.Value               = 10;   // Trend Bias — trend-rider by default (Drift's identity; ≠ TBars' 4:1). ~60t base reversal at Speed 12
            period.Value2              = 10;    // Conviction ×10 = 1.0 → run-widening OFF by default (avoid stacking on an already-wide bias + flow)
            period.BaseBarsPeriodValue = 12;    // Speed Settings — matches the suite's canonical TBars speed (the bake runs Speed 12)
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
