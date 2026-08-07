// ─────────────────────────────────────────────────────────────────────────────
// This Source Code Form is subject to the terms of the Mozilla Public License,
// v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
// one at https://mozilla.org/MPL/2.0/.
//
// Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
// ─────────────────────────────────────────────────────────────────────────────
#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.AddOns.Sentinel;   // SentinelCore (gate/sizing/consult) + SentinelCardCorner
#endregion

// ═════════════════════════════════════════════════════════════════════════════
//  SentinelTrendStrategy — risk-managed trend-flip strategy on SentinelTrend   |   Version v1.0.0
//  File: SentinelTrendStrategy_v1_0_0.cs   |   namespace …Strategies
//
//  WHAT THIS IS — the single, risk-managed replacement for the five NT-Wizard TrendMagic strategies
//  (TMEntry / TMEntry50 / TMXEntryExit / TMSquared / TripleTM). Those either had NO stop (TMEntry),
//  a target with NO stop (TMEntry50 — tiny wins, unbounded losses), or only painted the background
//  and placed no trades at all (TMSquared / TripleTM). This one fixes the money side.
//
//  WHY IT IS SUPERIOR:
//    • Trades the CORRECTED SentinelTrend line (true ATR + CCI hysteresis) instead of the whipsawing
//      original — fewer, cleaner flips (see SentinelTrend_v1_0_0.cs header).
//    • REAL RISK MANAGEMENT: ATR-sized stop + R-multiple target, with an optional trail that rides the
//      SentinelTrend line. Every entry is bracketed; you can never sit in an unbounded loss.
//    • SENTINEL GATING: every entry asks SentinelCore.CanEnter (kill-switch + feed health + daily
//      governor + account-session + rollover + news) and is fail-CLOSED (automated → refuse on block).
//    • RISK-BASED SIZING: optionally size each entry to a fixed $ risk over the ATR stop
//      (SentinelCore.SizeForRisk), else profile-scaled base qty (SentinelCore.SizedQuantity).
//    • OPTIONAL ADX ALIGNMENT: require ADXPro to confirm trend ON + bias agrees before entering.
//    • Stop-and-reverse on the opposite flip, but ALWAYS flattens on a flip even if the reverse entry
//      is gated off — so a blocked reverse never strands you in a stale position.
//
//  Managed order framework (SetStopLoss / SetProfitTarget). Registers its traded instrument with
//  SentinelRisk's watch registry so a stalled feed is caught even while flat.
//
//  CHANGELOG
//    v1.0.0 — initial: SentinelTrend flip entries, ATR stop + R target + line trail, CanEnter gate,
//             risk sizing, ADX-align filter. Supersedes the TMEntry/TMEntry50/TMX/TMSquared/TripleTM set.
// ═════════════════════════════════════════════════════════════════════════════
namespace NinjaTrader.NinjaScript.Strategies
{
    public class SentinelTrendStrategy_v1_0_0 : Strategy
    {
        private NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors.SentinelTrend_v1_0_0 st;
        private ATR atr;

        private double _trailLong  = double.NaN;   // monotonic ratchet stops (price)
        private double _trailShort = double.NaN;
        private bool   _watchRegistered;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                 = "Risk-managed trend-flip strategy on the corrected SentinelTrend line (supersedes the TrendMagic strategy set): ATR stop + R target + line trail, SentinelCore entry gate + risk sizing + optional ADX alignment.";
                Name                        = "SentinelTrendStrategy_v1_0_0";
                Calculate                   = Calculate.OnBarClose;
                EntriesPerDirection         = 1;
                EntryHandling               = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy= true;
                ExitOnSessionCloseSeconds   = 30;
                StartBehavior               = StartBehavior.WaitUntilFlat;
                TimeInForce                 = TimeInForce.Gtc;
                RealtimeErrorHandling       = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling          = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade         = 20;
                IsInstantiatedOnEachOptimizationIteration = true;

                // trend engine (mirror SentinelTrend defaults)
                CciPeriod                   = 20;
                AtrPeriod                   = 14;
                AtrMult                     = 1.5;
                CciThreshold                = 15.0;

                // risk
                StopAtrMult                 = 1.5;
                MinStopTicks                = 8;
                RewardRisk                  = 2.0;
                UseLineTrail                = true;

                // sizing
                UseRiskSizing               = false;
                RiskDollars                 = 100.0;
                BaseQuantity                = 1;

                // gating
                RequireAdxAlign             = false;
                StaleSec                    = 90.0;
            }
            else if (State == State.DataLoaded)
            {
                // internal SentinelTrend instance (card/signals/publish OFF — this copy is headless compute only).
                st  = SentinelTrend_v1_0_0(CciPeriod, AtrPeriod, AtrMult, CciThreshold,
                                           false, 8, false, false, 12, false,
                                           SentinelCardCorner.TopLeft, false, StaleSec, false, false);
                atr = ATR(AtrPeriod);
            }
            else if (State == State.Realtime)
            {
                if (!_watchRegistered && Instrument != null)
                {
                    try { SentinelCore.RegisterWatchInstrument(Instrument); _watchRegistered = true; } catch (Exception _sx) { SentinelCore.Swallow("SentinelTrendStrategy.OnStateChange", _sx); }
                }
            }
            else if (State == State.Terminated)
            {
                if (_watchRegistered && Instrument != null)
                {
                    try { SentinelCore.UnregisterWatchInstrument(Instrument); } catch (Exception _sx) { SentinelCore.Swallow("SentinelTrendStrategy.OnStateChange", _sx); }
                    _watchRegistered = false;
                }
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0) return;
            if (CurrentBar < BarsRequiredToTrade) return;

            // reset trail ratchets whenever flat
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                _trailLong = double.NaN; _trailShort = double.NaN;
            }

            int dir     = (int)st.Direction[0];
            int prevDir = (int)st.Direction[1];
            bool flipUp = dir == 1  && prevDir == -1;
            bool flipDn = dir == -1 && prevDir == 1;

            // On a flip: try to reverse (a managed entry auto-closes the opposite position). If the
            // reverse is GATED OFF, still flatten explicitly so a blocked reverse never strands us.
            if (flipUp)
            {
                bool entered = TryEnter(1);
                if (!entered && Position.MarketPosition == MarketPosition.Short) ExitShort();
            }
            else if (flipDn)
            {
                bool entered = TryEnter(-1);
                if (!entered && Position.MarketPosition == MarketPosition.Long) ExitLong();
            }

            // optional trailing stop that rides the SentinelTrend line (monotonic — never loosens)
            if (UseLineTrail) TrailToLine();
        }

        /// <summary>Attempt a gated, risk-bracketed entry. Returns true only if an entry order was placed.</summary>
        private bool TryEnter(int dir)
        {
            // fail-CLOSED entry gate: kill-switch + feed health + governor + session + rollover + news
            string reason;
            if (Account != null && !SentinelCore.CanEnter(
                    Instrument != null ? Instrument.FullName : null, Account, out reason))
            {
                Print(Time[0] + "  entry blocked: " + reason);
                return false;
            }

            // optional ADX-regime confirmation
            if (RequireAdxAlign && Instrument != null && Instrument.MasterInstrument != null)
            {
                // Consult the ADX on THIS chart (scope), not whichever chart wrote last (SentinelCore v1.18.0).
                var a = SentinelCore.GetAdxState(SentinelCore.ScopeOf(Instrument, BarsPeriod)
                                                 ?? Instrument.MasterInstrument.Name, StaleSec);
                if (a == null || !a.TrendOn || !a.Aligned(dir))
                {
                    Print(Time[0] + "  entry skipped: ADX not aligned");
                    return false;
                }
            }

            // ATR-sized stop + R target
            double atrTicks = TickSize > 0 ? atr[0] / TickSize : 0.0;
            int stopTicks   = Math.Max(MinStopTicks, (int)Math.Round(atrTicks * StopAtrMult));
            int targetTicks = Math.Max(1, (int)Math.Round(stopTicks * RewardRisk));

            // sizing
            int qty;
            if (UseRiskSizing)
            {
                qty = SentinelCore.SizeForRisk(Account, Instrument, stopTicks, RiskDollars);
                if (qty < 1)
                {
                    Print(Time[0] + "  entry skipped: risk-size 0 (stop " + stopTicks + "t too wide for $" + RiskDollars + ")");
                    return false;
                }
            }
            else
            {
                qty = SentinelCore.SizedQuantity(Account, BaseQuantity);
                if (qty < 1) qty = 1;
            }

            SetStopLoss(CalculationMode.Ticks, stopTicks);
            SetProfitTarget(CalculationMode.Ticks, targetTicks);

            if (dir == 1) EnterLong(qty, "STL");
            else          EnterShort(qty, "STS");

            try { if (Account != null) SentinelCore.NoteOrderSubmitted(Account.Name); } catch (Exception _sx) { SentinelCore.Swallow("SentinelTrendStrategy.EnterShort", _sx); }
            return true;
        }

        private void TrailToLine()
        {
            double line = st.Trend[0];
            if (line <= 0) return;

            if (Position.MarketPosition == MarketPosition.Long && line < Close[0])
            {
                if (double.IsNaN(_trailLong) || line > _trailLong)   // only ever raise
                {
                    _trailLong = line;
                    try { SetStopLoss(CalculationMode.Price, _trailLong); } catch (Exception _sx) { SentinelCore.Swallow("SentinelTrendStrategy.TrailToLine", _sx); }
                }
            }
            else if (Position.MarketPosition == MarketPosition.Short && line > Close[0])
            {
                if (double.IsNaN(_trailShort) || line < _trailShort) // only ever lower
                {
                    _trailShort = line;
                    try { SetStopLoss(CalculationMode.Price, _trailShort); } catch (Exception _sx) { SentinelCore.Swallow("SentinelTrendStrategy.TrailToLine", _sx); }
                }
            }
        }

        #region Properties
        [Range(1, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "CCI Period", Order = 1, GroupName = "1. Trend Engine")]
        public int CciPeriod { get; set; }

        [Range(1, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "ATR Period", Order = 2, GroupName = "1. Trend Engine")]
        public int AtrPeriod { get; set; }

        [Range(0.00001, double.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "ATR Multiplier", Order = 3, GroupName = "1. Trend Engine")]
        public double AtrMult { get; set; }

        [Range(0.0, double.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "CCI Hysteresis Threshold", Order = 4, GroupName = "1. Trend Engine")]
        public double CciThreshold { get; set; }

        [Range(0.0, double.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Stop ATR Multiplier", Description = "Initial stop distance = this × ATR (in ticks).", Order = 10, GroupName = "2. Risk")]
        public double StopAtrMult { get; set; }

        [Range(1, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Min Stop (ticks)", Description = "Floor for the ATR stop so a quiet ATR can't place the stop on top of price.", Order = 11, GroupName = "2. Risk")]
        public int MinStopTicks { get; set; }

        [Range(0.1, double.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Reward : Risk", Description = "Profit target = this × the stop distance.", Order = 12, GroupName = "2. Risk")]
        public double RewardRisk { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trail Stop to Trend Line", Description = "Ride the stop up/down along the SentinelTrend line once in a trade (monotonic — never loosens).", Order = 13, GroupName = "2. Risk")]
        public bool UseLineTrail { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use $-Risk Sizing", Description = "Size each entry to RiskDollars over the ATR stop (SentinelCore.SizeForRisk). Off = profile-scaled base qty.", Order = 20, GroupName = "3. Sizing")]
        public bool UseRiskSizing { get; set; }

        [Range(1.0, double.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Risk $ per Trade", Order = 21, GroupName = "3. Sizing")]
        public double RiskDollars { get; set; }

        [Range(1, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Base Quantity", Description = "Used when $-risk sizing is off (profile-scaled by SentinelCore.SizedQuantity).", Order = 22, GroupName = "3. Sizing")]
        public int BaseQuantity { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Require ADX Alignment", Description = "Only enter when ADXPro publishes trend ON + bias agreeing with the flip (needs SentinelCore ≥ v1.2.0 + ADXPro publishing).", Order = 30, GroupName = "4. Gating")]
        public bool RequireAdxAlign { get; set; }

        [Range(0.0, double.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Consult Stale (sec)", Description = "Ignore a published ADX state older than this many seconds (0 = never stale).", Order = 31, GroupName = "4. Gating")]
        public double StaleSec { get; set; }
        #endregion
    }
}
