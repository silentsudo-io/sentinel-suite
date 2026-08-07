// ─────────────────────────────────────────────────────────────────────────────
// This Source Code Form is subject to the terms of the Mozilla Public License,
// v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
// one at https://mozilla.org/MPL/2.0/.
//
// Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
//
// PROVENANCE: the author's own work (DD / GodTrades) — self-derived, original.
// A standalone indicator-only "test rig" analysis tool. NOT a Council signal:
// no SentinelCore State seam, no Council voter, no hidden Signal plot.
// ─────────────────────────────────────────────────────────────────────────────
#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.AddOns.Sentinel;
using NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors;
#endregion

// AdaptiveStrategyPerformanceGridGodTradesV002
// Purpose:
//  - Indicator-only adaptive strategy-performance grid.
//  - Adds the same style of alternate bar rows used by AdaptiveConfluenceGridV002.
//  - Runs simulated GodTradesStrategy-style entries/exits on each enabled row.
//  - No live orders. No AddOn. No account interaction.
//  - Ranks rows by rolling simulated performance.
//  - V002 adds longer rolling bar lookback/freshness controls for sparse strategy signals.
//
// Requirements:
//  - GodTrades21 indicator must be installed/compiled.
//  - Custom TBars and NinzaRenko BarsPeriodType IDs must be correct for your machine.

namespace NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors
{
    public enum AdaptiveStrategyGridEntryMode
    {
        Market,
        Limit,
        StopMarket
    }

    public enum AdaptiveStrategyGridDirectionMode
    {
        Both,
        LongOnly,
        ShortOnly
    }
}

namespace NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors
{
    public class SentinelAdaptivePerformanceGrid_v1_0_0 : Indicator
    {
        private class RowModel
        {
            public string Name;
            public string Group;
            public int Bip;
            public bool Enabled;

            public GodTrades21 God;
            public SimPosition Position;
            public SimOrder WorkingOrder;
            public Queue<SimTrade> Trades = new Queue<SimTrade>();

            public int LastProcessedSignalBar = -1;
            public int LastSignalDirection;
            public string LastSignalSource = "None";
            public int LastSignalAge = 999999;
            public int LastTradeAge = 999999;
            public int ConsecutiveLosses;
            public double Equity;
            public double PeakEquity;
            public double MaxDrawdown;

            public int StatusDirection;
            public double RecentPnl;
            public double WinRate;
            public double ProfitFactor;
            public double AverageTrade;
            public int TradeCount;
            public double Score;
        }

        private class SimOrder
        {
            public int Direction;
            public int SubmitBar;
            public double Price;
            public string Source;
        }

        private class SimPosition
        {
            public int Direction;
            public int EntryBar;
            public double EntryPrice;
            public string Source;
            public double StopPrice;
            public double TargetPrice;
            public double HighestSinceEntry;
            public double LowestSinceEntry;
            public bool BreakEvenMoved;
        }

        private class SimTrade
        {
            public int Direction;
            public int EntryBar;
            public int ExitBar;
            public double EntryPrice;
            public double ExitPrice;
            public double PnlTicks;
            public string Source;
            public string ExitReason;
        }

        private List<RowModel> rows;
        private Dictionary<int, RowModel> rowsByBip;
        private SimpleFont gridFont;
        private int nextBip;

        private string bestModel;
        private int bestDirection;
        private double bestScore;
        private double bestPnl;
        private double bestWinRate;
        private double bestProfitFactor;
        private double bestAverageTrade;
        private double bestDrawdown;
        private int bestConsecutiveLosses;
        private int bestTradeCount;

        #region OnStateChange
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                                = "Sentinel Adaptive Performance Grid v1.0.0";
                Description                         = "Indicator-only adaptive strategy-performance grid. Simulates GodTradesStrategy logic across multiple bar types/timeframes and ranks the current best row.";
                ShowIndicatorLabel                  = false;
                Calculate                           = Calculate.OnBarClose;
                IsOverlay                           = true;
                DrawOnPricePanel                    = true;
                DisplayInDataBox                    = true;
                PaintPriceMarkers                   = false;
                IsSuspendedWhileInactive            = true;
                MaximumBarsLookBack                 = MaximumBarsLookBack.Infinite;

                TBarsBarsPeriodTypeId               = 98765;
                NinzaRenkoBarsPeriodTypeId          = 12345;

                UseTBars4                           = true;
                UseTBars12                          = true;
                UseTBars21                          = true;

                UseMinute1                          = true;
                UseMinute3                          = true;
                UseMinute5                          = true;
                UseMinute15                         = true;

                UseHeikenAshiMinute1                = true;
                UseHeikenAshiMinute3                = true;
                UseHeikenAshiMinute5                = true;

                UseTick50                           = true;
                UseTick90                           = true;
                UseTick150                          = true;
                UseTick300                          = true;
                UseTick500                          = true;
                UseTick1000                         = true;

                UseNinzaRenko10_1                   = true;
                UseNinzaRenko18_3                   = true;
                UseNinzaRenko32_5                   = true;
                UseNinzaRenko64_15                  = true;

                DirectionMode                       = AdaptiveStrategyGridDirectionMode.Both;
                EntryMode                           = AdaptiveStrategyGridEntryMode.Market;
                EntryOffsetTicks                    = 0;
                CancelUnfilledEntryAfterBars        = 1;
                TradeOnlyWhenFlat                   = true;
                ReverseOnOppositeSignal             = false;
                IgnoreConflictingSignals            = true;

                EnableBGTrades                      = true;
                EnableFCTrades                      = true;
                EnableOBRTrades                     = true;

                UseProfitTarget                     = true;
                ProfitTargetTicks                   = 40;
                UseStopLoss                         = true;
                StopLossTicks                       = 30;

                UseBreakEven                        = false;
                BreakEvenTriggerTicks               = 20;
                BreakEvenPlusTicks                  = 1;

                UseTrailingStop                     = false;
                TrailingTriggerTicks                = 30;
                TrailingDistanceTicks               = 20;
                TrailingStepTicks                   = 4;

                UseTradeWindow1                     = true;
                TradeWindow1Start                   = 0;
                TradeWindow1End                     = 235959;
                UseTradeWindow2                     = false;
                TradeWindow2Start                   = 0;
                TradeWindow2End                     = 235959;
                UseTradeWindow3                     = false;
                TradeWindow3Start                   = 0;
                TradeWindow3End                     = 235959;

                UseSkipWindow1                      = false;
                SkipWindow1Start                    = 74000;
                SkipWindow1End                      = 84000;
                UseSkipWindow2                      = false;
                SkipWindow2Start                    = 110000;
                SkipWindow2End                      = 122500;
                UseSkipWindow3                      = false;
                SkipWindow3Start                    = 0;
                SkipWindow3End                      = 0;

                MaxStoredTrades                     = 200;
                PerformanceLookbackBars            = 300;
                DirectionFreshBars                  = 120;
                UseStaleTradeScorePenalty           = true;
                StaleTradePenaltyStartBars          = 80;
                MaxStaleTradeScorePenalty           = 15;
                MinTradesForTrust                   = 5;
                ProfitFactorCap                     = 5.0;
                ScoreWeightPnl                      = 0.35;
                ScoreWeightWinRate                  = 0.25;
                ScoreWeightProfitFactor             = 0.20;
                ScoreWeightAverageTrade             = 0.10;
                ScoreWeightDrawdown                 = 0.10;
                QualificationScore                  = 60;
                QualificationMinTrades              = 5;
                QualificationMaxConsecutiveLosses   = 3;

                MinimumGapSizeTicks                 = 1;
                MinimumBarsBeforeValid              = 3;
                MinimumBodyTicks                    = 0;
                MaximumGapBarRangeTicks             = 0;
                MaximumActiveGapsToTrack            = 300;
                EarlyTouchHandling                  = GodTrades21EarlyTouchHandling.StopLineImmediately;
                ValidTouchBehavior                  = GodTrades21ValidTouchBehavior.StopLineAndMarkContinuation;

                UseBollingerMidpointFilterForContinuation = true;
                FcBollingerLocationSource           = GodTrades21FcBollingerLocationSource.WickExtreme;
                FcLongBelowMidpointPercent          = 50.0;
                FcShortAboveMidpointPercent         = 50.0;
                ContinuationConfirmationMode        = GodTrades21ContinuationConfirmationMode.RequireCloseBeyondFullZone;
                ConfirmationBarsAfterTouch          = 2;
                RequireSignalCandleDirection        = true;
                RequireCorrectContinuationApproach  = true;

                UseBollingerMidpointFilterForOutsideBarReversal = true;
                AllowObrBarOutsideBollingerBand     = true;
                BearishObrUpperBandTouchToleranceTicks = 4;
                BullishObrLowerBandTouchToleranceTicks = 4;

                UseIndicatorSignalTimeFilter        = false;
                IndicatorSignalStartTime            = 101500;
                IndicatorSignalEndTime              = 150000;

                BollingerPeriod                     = 20;
                BollingerStdDev                     = 2.0;
                BollingerBandProximityTicks         = 8;

                EnableSpiderwebWarning              = true;
                ShowSpiderwebWarningText            = false;
                SpiderwebDistanceTicks              = 100;
                SpiderwebLineCount                  = 5;
                SpiderwebTextFontSize               = 15;

                ShowGrid                            = true;
                MaxRowsToDisplay                    = 25;
                DisplayCorner                       = TextPosition.TopLeft;
                ShowDisabledRows                    = false;
                GridOpacity                         = 70;
                GridFontSize                        = 12;

                StrongBullBrush                     = Brushes.LimeGreen;
                BullBrush                           = Brushes.Green;
                NeutralBrush                        = Brushes.Gray;
                BearBrush                           = Brushes.IndianRed;
                StrongBearBrush                     = Brushes.Magenta;
                TextBrush                           = Brushes.White;
                BackgroundBrush                     = Brushes.Black;

                AddPlot(new Stroke(Brushes.Transparent, 1), PlotStyle.Line, "BestDirection");
                AddPlot(new Stroke(Brushes.Transparent, 1), PlotStyle.Line, "BestScore");
                AddPlot(new Stroke(Brushes.Transparent, 1), PlotStyle.Line, "BestRecentPnlTicks");
                AddPlot(new Stroke(Brushes.Transparent, 1), PlotStyle.Line, "BestWinRate");
                AddPlot(new Stroke(Brushes.Transparent, 1), PlotStyle.Line, "BestProfitFactor");
                AddPlot(new Stroke(Brushes.Transparent, 1), PlotStyle.Line, "BestAverageTradeTicks");
                AddPlot(new Stroke(Brushes.Transparent, 1), PlotStyle.Line, "BestDrawdownTicks");
                AddPlot(new Stroke(Brushes.Transparent, 1), PlotStyle.Line, "BestConsecutiveLosses");
                AddPlot(new Stroke(Brushes.Transparent, 1), PlotStyle.Line, "BestTradeCount");
                AddPlot(new Stroke(Brushes.Transparent, 1), PlotStyle.Line, "LongQualified");
                AddPlot(new Stroke(Brushes.Transparent, 1), PlotStyle.Line, "ShortQualified");
            }
            else if (State == State.Configure)
            {
                rows = new List<RowModel>();
                rowsByBip = new Dictionary<int, RowModel>();
                nextBip = 1;

                if (UseTBars4)  AddTBarsRow("TBars 4",  4);
                if (UseTBars12) AddTBarsRow("TBars 12", 12);
                if (UseTBars21) AddTBarsRow("TBars 21", 21);

                if (UseMinute1)  AddStandardRow("1 Min",  "Minute", BarsPeriodType.Minute, 1);
                if (UseMinute3)  AddStandardRow("3 Min",  "Minute", BarsPeriodType.Minute, 3);
                if (UseMinute5)  AddStandardRow("5 Min",  "Minute", BarsPeriodType.Minute, 5);
                if (UseMinute15) AddStandardRow("15 Min", "Minute", BarsPeriodType.Minute, 15);

                if (UseHeikenAshiMinute1) AddHeikenAshiRow("HA 1 Min", "Heiken Ashi", BarsPeriodType.Minute, 1);
                if (UseHeikenAshiMinute3) AddHeikenAshiRow("HA 3 Min", "Heiken Ashi", BarsPeriodType.Minute, 3);
                if (UseHeikenAshiMinute5) AddHeikenAshiRow("HA 5 Min", "Heiken Ashi", BarsPeriodType.Minute, 5);

                if (UseTick50)   AddStandardRow("Tick 50",   "Tick", BarsPeriodType.Tick, 50);
                if (UseTick90)   AddStandardRow("Tick 90",   "Tick", BarsPeriodType.Tick, 90);
                if (UseTick150)  AddStandardRow("Tick 150",  "Tick", BarsPeriodType.Tick, 150);
                if (UseTick300)  AddStandardRow("Tick 300",  "Tick", BarsPeriodType.Tick, 300);
                if (UseTick500)  AddStandardRow("Tick 500",  "Tick", BarsPeriodType.Tick, 500);
                if (UseTick1000) AddStandardRow("Tick 1000", "Tick", BarsPeriodType.Tick, 1000);

                if (UseNinzaRenko10_1)  AddNinzaRenkoRow("NinzaRenko 10/1",  10, 1);
                if (UseNinzaRenko18_3)  AddNinzaRenkoRow("NinzaRenko 18/3",  18, 3);
                if (UseNinzaRenko32_5)  AddNinzaRenkoRow("NinzaRenko 32/5",  32, 5);
                if (UseNinzaRenko64_15) AddNinzaRenkoRow("NinzaRenko 64/15", 64, 15);
            }
            else if (State == State.DataLoaded)
            {
                if (!ShowIndicatorLabel) Name = string.Empty;

                gridFont = new SimpleFont("Consolas", GridFontSize);

                foreach (RowModel row in rows)
                {
                    row.God = GodTrades21(
                        Closes[row.Bip],
                        MinimumGapSizeTicks,
                        MinimumBarsBeforeValid,
                        MinimumBodyTicks,
                        MaximumGapBarRangeTicks,
                        MaximumActiveGapsToTrack,
                        EarlyTouchHandling,
                        ValidTouchBehavior,
                        true,
                        UseBollingerMidpointFilterForContinuation,
                        FcBollingerLocationSource,
                        FcLongBelowMidpointPercent,
                        FcShortAboveMidpointPercent,
                        ContinuationConfirmationMode,
                        ConfirmationBarsAfterTouch,
                        RequireSignalCandleDirection,
                        RequireCorrectContinuationApproach,
                        true,
                        false,
                        false,
                        UseBollingerMidpointFilterForOutsideBarReversal,
                        AllowObrBarOutsideBollingerBand,
                        BearishObrUpperBandTouchToleranceTicks,
                        BullishObrLowerBandTouchToleranceTicks,
                        UseIndicatorSignalTimeFilter,
                        IndicatorSignalStartTime,
                        IndicatorSignalEndTime,
                        true,
                        BollingerPeriod,
                        BollingerStdDev,
                        BollingerBandProximityTicks,
                        EnableSpiderwebWarning,
                        ShowSpiderwebWarningText,
                        SpiderwebDistanceTicks,
                        SpiderwebLineCount,
                        SpiderwebTextFontSize,
                        0,
                        GodTrades21TargetMode.None,
                        ProfitTargetTicks,
                        GodTrades21LinePriceMode.Midpoint,
                        false,
                        false,
                        false,
                        false,
                        false,
                        false,
                        false,
                        2,
                        DashStyleHelper.Solid,
                        12,
                        3,
                        7);
                }
            }
            else if (State == State.Terminated)
            {
                RemoveDrawObject("ASPG_GT_V002_GRID");
            }
        }
        #endregion

        #region Add row helpers
        private void AddStandardRow(string name, string group, BarsPeriodType type, int value)
        {
            AddDataSeries(type, value);
            RegisterRow(name, group);
        }

        private void AddHeikenAshiRow(string name, string group, BarsPeriodType baseType, int value)
        {
            AddHeikenAshi(Instrument.FullName, baseType, value, MarketDataType.Last);
            RegisterRow(name, group);
        }

        private void AddTBarsRow(string name, int speed)
        {
            AddDataSeries(new BarsPeriod()
            {
                BarsPeriodType = (BarsPeriodType)TBarsBarsPeriodTypeId,
                BarsPeriodTypeName = "TBars",
                BaseBarsPeriodValue = speed
            });
            RegisterRow(name, "TBars");
        }

        private void AddNinzaRenkoRow(string name, int brickSize, int trendThreshold)
        {
            AddDataSeries(new BarsPeriod()
            {
                BarsPeriodType = (BarsPeriodType)NinzaRenkoBarsPeriodTypeId,
                Value = brickSize,
                Value2 = trendThreshold
            });
            RegisterRow(name, "NinzaRenko");
        }

        private void RegisterRow(string name, string group)
        {
            RowModel row = new RowModel()
            {
                Name = name,
                Group = group,
                Bip = nextBip,
                Enabled = true,
                Equity = 0,
                PeakEquity = 0,
                MaxDrawdown = 0
            };
            rows.Add(row);
            rowsByBip[nextBip] = row;
            nextBip++;
        }
        #endregion

        #region OnBarUpdate
        protected override void OnBarUpdate()
        {
            if (BarsInProgress == 0)
            {
                UpdateBestModelAndPlots();
                if (ShowGrid)
                    DrawGrid();
                return;
            }

            RowModel row;
            if (rowsByBip == null || !rowsByBip.TryGetValue(BarsInProgress, out row) || row == null || !row.Enabled)
                return;

            int minBars = Math.Max(BarsRequiredToPlot, BollingerPeriod + 3);
            if (CurrentBars[BarsInProgress] < minBars)
                return;

            UpdateRow(row);
        }

        private void UpdateRow(RowModel row)
        {
            int bip = row.Bip;
            int currentBar = CurrentBars[bip];

            ManageWorkingOrder(row);
            ManageOpenPosition(row);
            ProcessSignals(row);

            if (row.LastProcessedSignalBar >= 0)
                row.LastSignalAge = currentBar - row.LastProcessedSignalBar;

            if (row.Trades.Count > 0)
                row.LastTradeAge = currentBar - row.Trades.Last().ExitBar;

            row.StatusDirection = GetEffectiveStatusDirection(row, currentBar);

            CalculateMetrics(row);
        }
        private int GetEffectiveStatusDirection(RowModel row, int currentBar)
        {
            if (row.Position != null)
                return row.Position.Direction;

            if (row.WorkingOrder != null)
                return row.WorkingOrder.Direction;

            if (row.LastProcessedSignalBar >= 0 && currentBar - row.LastProcessedSignalBar <= DirectionFreshBars)
                return row.LastSignalDirection;

            return 0;
        }

        #endregion

        #region Signal processing
        private void ProcessSignals(RowModel row)
        {
            if (row.God == null)
                return;

            int bip = row.Bip;
            int currentBar = CurrentBars[bip];

            bool bgLong    = EnableBGTrades  && IsPositive(row.God.BollingerGapLong[0]);
            bool bgShort   = EnableBGTrades  && IsNegative(row.God.BollingerGapShort[0]);
            bool fcLong    = EnableFCTrades  && IsPositive(row.God.ContinuationLong[0]);
            bool fcShort   = EnableFCTrades  && IsNegative(row.God.ContinuationShort[0]);
            bool obrLong   = EnableOBRTrades && IsPositive(row.God.OutsideBarReversalSignal[0]);
            bool obrShort  = EnableOBRTrades && IsNegative(row.God.OutsideBarReversalSignal[0]);

            bool longSignal = bgLong || fcLong || obrLong;
            bool shortSignal = bgShort || fcShort || obrShort;

            if (!longSignal && !shortSignal)
                return;

            if (currentBar == row.LastProcessedSignalBar)
                return;

            row.LastProcessedSignalBar = currentBar;

            string longSource = BuildSignalSource(bgLong, fcLong, obrLong);
            string shortSource = BuildSignalSource(bgShort, fcShort, obrShort);

            if (longSignal && shortSignal && IgnoreConflictingSignals)
            {
                row.StatusDirection = 0;
                row.LastSignalDirection = 0;
                row.LastSignalSource = "Conflict: " + longSource + "/" + shortSource;
                return;
            }

            int direction = 0;
            string source = string.Empty;

            if (longSignal && !shortSignal)
            {
                direction = 1;
                source = longSource;
            }
            else if (shortSignal && !longSignal)
            {
                direction = -1;
                source = shortSource;
            }
            else
            {
                int masterDirection = NormalizeSignal(row.God.SignalDirection[0]);
                if (masterDirection > 0 && longSignal)
                {
                    direction = 1;
                    source = longSource;
                }
                else if (masterDirection < 0 && shortSignal)
                {
                    direction = -1;
                    source = shortSource;
                }
                else
                    return;
            }

            row.LastSignalDirection = direction;
            row.LastSignalSource = source;
            row.LastSignalAge = 0;
            row.StatusDirection = direction;

            if (!IsDirectionEnabled(direction))
                return;

            if (!IsTradingTimeAllowedForBip(bip))
                return;

            if (row.WorkingOrder != null)
                return;

            if (row.Position == null)
            {
                SubmitSimEntry(row, direction, source);
                return;
            }

            bool oppositeSignal = row.Position.Direction != 0 && row.Position.Direction != direction;

            if (oppositeSignal && ReverseOnOppositeSignal)
            {
                ExitPosition(row, Closes[bip][0], "Reverse", true);
                SubmitSimEntry(row, direction, source);
                return;
            }

            if (!TradeOnlyWhenFlat && row.Position.Direction == direction)
            {
                // V001 intentionally keeps one simulated open position per row.
                // Future versions can add pyramiding.
            }
        }

        private void SubmitSimEntry(RowModel row, int direction, string source)
        {
            int bip = row.Bip;
            int currentBar = CurrentBars[bip];
            double price;

            if (EntryMode == AdaptiveStrategyGridEntryMode.Market)
            {
                price = Closes[bip][0];
                OpenPosition(row, direction, price, source);
                return;
            }

            if (EntryMode == AdaptiveStrategyGridEntryMode.Limit)
            {
                price = direction > 0
                    ? Closes[bip][0] - EntryOffsetTicks * TickSize
                    : Closes[bip][0] + EntryOffsetTicks * TickSize;
            }
            else
            {
                price = direction > 0
                    ? Highs[bip][0] + EntryOffsetTicks * TickSize
                    : Lows[bip][0] - EntryOffsetTicks * TickSize;
            }

            price = Instrument.MasterInstrument.RoundToTickSize(price);

            row.WorkingOrder = new SimOrder()
            {
                Direction = direction,
                SubmitBar = currentBar,
                Price = price,
                Source = source
            };
        }

        private void ManageWorkingOrder(RowModel row)
        {
            if (row.WorkingOrder == null)
                return;

            int bip = row.Bip;
            int currentBar = CurrentBars[bip];
            SimOrder order = row.WorkingOrder;

            bool fill = false;

            if (EntryMode == AdaptiveStrategyGridEntryMode.Limit)
            {
                if (order.Direction > 0 && Lows[bip][0] <= order.Price)
                    fill = true;
                else if (order.Direction < 0 && Highs[bip][0] >= order.Price)
                    fill = true;
            }
            else if (EntryMode == AdaptiveStrategyGridEntryMode.StopMarket)
            {
                if (order.Direction > 0 && Highs[bip][0] >= order.Price)
                    fill = true;
                else if (order.Direction < 0 && Lows[bip][0] <= order.Price)
                    fill = true;
            }

            if (fill)
            {
                OpenPosition(row, order.Direction, order.Price, order.Source);
                row.WorkingOrder = null;
                return;
            }

            if (CancelUnfilledEntryAfterBars > 0 && currentBar - order.SubmitBar >= CancelUnfilledEntryAfterBars)
                row.WorkingOrder = null;

            if (!IsTradingTimeAllowedForBip(bip))
                row.WorkingOrder = null;
        }
        #endregion

        #region Position management
        private void OpenPosition(RowModel row, int direction, double entryPrice, string source)
        {
            int bip = row.Bip;
            entryPrice = Instrument.MasterInstrument.RoundToTickSize(entryPrice);

            SimPosition pos = new SimPosition()
            {
                Direction = direction,
                EntryBar = CurrentBars[bip],
                EntryPrice = entryPrice,
                Source = source,
                HighestSinceEntry = Math.Max(entryPrice, Highs[bip][0]),
                LowestSinceEntry = Math.Min(entryPrice, Lows[bip][0]),
                BreakEvenMoved = false
            };

            if (UseStopLoss)
                pos.StopPrice = direction > 0
                    ? Instrument.MasterInstrument.RoundToTickSize(entryPrice - StopLossTicks * TickSize)
                    : Instrument.MasterInstrument.RoundToTickSize(entryPrice + StopLossTicks * TickSize);
            else
                pos.StopPrice = double.NaN;

            if (UseProfitTarget)
                pos.TargetPrice = direction > 0
                    ? Instrument.MasterInstrument.RoundToTickSize(entryPrice + ProfitTargetTicks * TickSize)
                    : Instrument.MasterInstrument.RoundToTickSize(entryPrice - ProfitTargetTicks * TickSize);
            else
                pos.TargetPrice = double.NaN;

            row.Position = pos;
        }

        private void ManageOpenPosition(RowModel row)
        {
            if (row.Position == null)
                return;

            int bip = row.Bip;
            SimPosition pos = row.Position;

            pos.HighestSinceEntry = Math.Max(pos.HighestSinceEntry, Highs[bip][0]);
            pos.LowestSinceEntry = Math.Min(pos.LowestSinceEntry, Lows[bip][0]);

            ApplyBreakEvenAndTrailing(row);

            if (pos.Direction > 0)
            {
                bool stopHit = UseStopLoss && !double.IsNaN(pos.StopPrice) && Lows[bip][0] <= pos.StopPrice;
                bool targetHit = UseProfitTarget && !double.IsNaN(pos.TargetPrice) && Highs[bip][0] >= pos.TargetPrice;

                // Conservative same-bar handling: if stop and target touch in the same bar, count the stop first.
                if (stopHit)
                {
                    ExitPosition(row, pos.StopPrice, "Stop", false);
                    return;
                }
                if (targetHit)
                {
                    ExitPosition(row, pos.TargetPrice, "Target", false);
                    return;
                }
            }
            else
            {
                bool stopHit = UseStopLoss && !double.IsNaN(pos.StopPrice) && Highs[bip][0] >= pos.StopPrice;
                bool targetHit = UseProfitTarget && !double.IsNaN(pos.TargetPrice) && Lows[bip][0] <= pos.TargetPrice;

                if (stopHit)
                {
                    ExitPosition(row, pos.StopPrice, "Stop", false);
                    return;
                }
                if (targetHit)
                {
                    ExitPosition(row, pos.TargetPrice, "Target", false);
                    return;
                }
            }
        }

        private void ApplyBreakEvenAndTrailing(RowModel row)
        {
            SimPosition pos = row.Position;
            if (pos == null)
                return;

            if (pos.Direction > 0)
            {
                double favorableTicks = (pos.HighestSinceEntry - pos.EntryPrice) / TickSize;
                double proposedStop = pos.StopPrice;

                if (UseBreakEven && !pos.BreakEvenMoved && favorableTicks >= BreakEvenTriggerTicks)
                {
                    double be = pos.EntryPrice + BreakEvenPlusTicks * TickSize;
                    proposedStop = double.IsNaN(proposedStop) ? be : Math.Max(proposedStop, be);
                    pos.BreakEvenMoved = true;
                }

                if (UseTrailingStop && favorableTicks >= TrailingTriggerTicks)
                {
                    double trail = pos.HighestSinceEntry - TrailingDistanceTicks * TickSize;
                    if (double.IsNaN(proposedStop) || trail >= proposedStop + TrailingStepTicks * TickSize)
                        proposedStop = trail;
                }

                if (!double.IsNaN(proposedStop))
                    pos.StopPrice = Instrument.MasterInstrument.RoundToTickSize(proposedStop);
            }
            else
            {
                double favorableTicks = (pos.EntryPrice - pos.LowestSinceEntry) / TickSize;
                double proposedStop = pos.StopPrice;

                if (UseBreakEven && !pos.BreakEvenMoved && favorableTicks >= BreakEvenTriggerTicks)
                {
                    double be = pos.EntryPrice - BreakEvenPlusTicks * TickSize;
                    proposedStop = double.IsNaN(proposedStop) ? be : Math.Min(proposedStop, be);
                    pos.BreakEvenMoved = true;
                }

                if (UseTrailingStop && favorableTicks >= TrailingTriggerTicks)
                {
                    double trail = pos.LowestSinceEntry + TrailingDistanceTicks * TickSize;
                    if (double.IsNaN(proposedStop) || trail <= proposedStop - TrailingStepTicks * TickSize)
                        proposedStop = trail;
                }

                if (!double.IsNaN(proposedStop))
                    pos.StopPrice = Instrument.MasterInstrument.RoundToTickSize(proposedStop);
            }
        }

        private void ExitPosition(RowModel row, double exitPrice, string reason, bool useClosePrice)
        {
            if (row.Position == null)
                return;

            int bip = row.Bip;
            SimPosition pos = row.Position;
            exitPrice = useClosePrice ? Closes[bip][0] : exitPrice;
            exitPrice = Instrument.MasterInstrument.RoundToTickSize(exitPrice);

            double pnlTicks = pos.Direction > 0
                ? (exitPrice - pos.EntryPrice) / TickSize
                : (pos.EntryPrice - exitPrice) / TickSize;

            SimTrade trade = new SimTrade()
            {
                Direction = pos.Direction,
                EntryBar = pos.EntryBar,
                ExitBar = CurrentBars[bip],
                EntryPrice = pos.EntryPrice,
                ExitPrice = exitPrice,
                PnlTicks = pnlTicks,
                Source = pos.Source,
                ExitReason = reason
            };

            row.Trades.Enqueue(trade);
            while (row.Trades.Count > MaxStoredTrades)
                row.Trades.Dequeue();

            row.Equity += pnlTicks;
            row.PeakEquity = Math.Max(row.PeakEquity, row.Equity);
            row.MaxDrawdown = Math.Max(row.MaxDrawdown, row.PeakEquity - row.Equity);
            row.ConsecutiveLosses = pnlTicks < 0 ? row.ConsecutiveLosses + 1 : 0;
            row.LastTradeAge = 0;
            row.Position = null;
        }
        #endregion

        #region Metrics and scoring
        private void CalculateMetrics(RowModel row)
        {
            int currentBar = CurrentBars[row.Bip];
            List<SimTrade> trades = row.Trades
                .Where(t => PerformanceLookbackBars <= 0 || currentBar - t.ExitBar <= PerformanceLookbackBars)
                .ToList();

            row.TradeCount = trades.Count;

            if (trades.Count == 0)
            {
                row.RecentPnl = 0;
                row.WinRate = 0;
                row.ProfitFactor = 0;
                row.AverageTrade = 0;
                row.Score = 0;
                return;
            }
            row.RecentPnl = trades.Sum(t => t.PnlTicks);
            row.MaxDrawdown = CalculateRollingDrawdown(trades);
            row.WinRate = trades.Count(t => t.PnlTicks > 0) / Math.Max(1.0, trades.Count) * 100.0;
            row.AverageTrade = trades.Average(t => t.PnlTicks);

            double grossProfit = trades.Where(t => t.PnlTicks > 0).Sum(t => t.PnlTicks);
            double grossLoss = Math.Abs(trades.Where(t => t.PnlTicks < 0).Sum(t => t.PnlTicks));

            if (grossLoss <= 0 && grossProfit > 0)
                row.ProfitFactor = ProfitFactorCap;
            else if (grossLoss <= 0)
                row.ProfitFactor = 0;
            else
                row.ProfitFactor = Math.Min(ProfitFactorCap, grossProfit / grossLoss);

            double sampleFactor = Math.Min(1.0, row.TradeCount / Math.Max(1.0, MinTradesForTrust));
            double pnlNorm = NormalizeTo100(row.RecentPnl, -StopLossTicks * MaxStoredTrades * 0.25, ProfitTargetTicks * MaxStoredTrades * 0.25);
            double wrNorm = row.WinRate;
            double pfNorm = Math.Min(100.0, row.ProfitFactor / Math.Max(0.1, ProfitFactorCap) * 100.0);
            double avgNorm = NormalizeTo100(row.AverageTrade, -StopLossTicks, ProfitTargetTicks);
            double ddPenalty = NormalizeTo100(row.MaxDrawdown, 0, StopLossTicks * 5.0);

            double raw =
                pnlNorm * ScoreWeightPnl +
                wrNorm * ScoreWeightWinRate +
                pfNorm * ScoreWeightProfitFactor +
                avgNorm * ScoreWeightAverageTrade -
                ddPenalty * ScoreWeightDrawdown;

            if (row.ConsecutiveLosses > 0)
                raw -= Math.Min(25.0, row.ConsecutiveLosses * 5.0);

            if (UseStaleTradeScorePenalty && PerformanceLookbackBars > StaleTradePenaltyStartBars && row.LastTradeAge > StaleTradePenaltyStartBars)
            {
                double staleSpan = Math.Max(1.0, PerformanceLookbackBars - StaleTradePenaltyStartBars);
                double staleRatio = Math.Min(1.0, (row.LastTradeAge - StaleTradePenaltyStartBars) / staleSpan);
                raw -= MaxStaleTradeScorePenalty * staleRatio;
            }

            row.Score = Math.Max(0, Math.Min(100, raw * sampleFactor));
        }

        private double CalculateRollingDrawdown(List<SimTrade> trades)
        {
            double equity = 0;
            double peak = 0;
            double maxDd = 0;

            foreach (SimTrade trade in trades.OrderBy(t => t.ExitBar))
            {
                equity += trade.PnlTicks;
                peak = Math.Max(peak, equity);
                maxDd = Math.Max(maxDd, peak - equity);
            }

            return maxDd;
        }

        private double NormalizeTo100(double value, double low, double high)
        {
            if (Math.Abs(high - low) < double.Epsilon)
                return 50;

            double norm = (value - low) / (high - low) * 100.0;
            return Math.Max(0, Math.Min(100, norm));
        }

        private void UpdateBestModelAndPlots()
        {
            bestModel = "None";
            bestDirection = 0;
            bestScore = 0;
            bestPnl = 0;
            bestWinRate = 0;
            bestProfitFactor = 0;
            bestAverageTrade = 0;
            bestDrawdown = 0;
            bestConsecutiveLosses = 0;
            bestTradeCount = 0;

            if (rows != null)
            {
                foreach (RowModel row in rows.Where(r => r.Enabled))
                {
                    if (row.Score > bestScore)
                    {
                        bestModel = row.Name;
                        bestDirection = row.StatusDirection;
                        bestScore = row.Score;
                        bestPnl = row.RecentPnl;
                        bestWinRate = row.WinRate;
                        bestProfitFactor = row.ProfitFactor;
                        bestAverageTrade = row.AverageTrade;
                        bestDrawdown = row.MaxDrawdown;
                        bestConsecutiveLosses = row.ConsecutiveLosses;
                        bestTradeCount = row.TradeCount;
                    }
                }
            }

            Values[0][0] = bestDirection;
            Values[1][0] = bestScore;
            Values[2][0] = bestPnl;
            Values[3][0] = bestWinRate;
            Values[4][0] = bestProfitFactor;
            Values[5][0] = bestAverageTrade;
            Values[6][0] = bestDrawdown;
            Values[7][0] = bestConsecutiveLosses;
            Values[8][0] = bestTradeCount;
            Values[9][0] = bestDirection > 0 && bestScore >= QualificationScore && bestTradeCount >= QualificationMinTrades && bestConsecutiveLosses <= QualificationMaxConsecutiveLosses ? 1 : 0;
            Values[10][0] = bestDirection < 0 && bestScore >= QualificationScore && bestTradeCount >= QualificationMinTrades && bestConsecutiveLosses <= QualificationMaxConsecutiveLosses ? 1 : 0;
        }
        #endregion

        #region Display
        private void DrawGrid()
        {
            if (rows == null)
                return;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Adaptive Strategy Performance Grid - GodTrades V002");
            sb.AppendLine("Best: " + DirectionText(bestDirection) + "  " + bestModel + "  Score " + bestScore.ToString("0") + "  PnL " + bestPnl.ToString("0") + "t");
            sb.AppendLine("Mode: " + EntryMode + "  TP/SL: " + (UseProfitTarget ? ProfitTargetTicks.ToString() : "Off") + "/" + (UseStopLoss ? StopLossTicks.ToString() : "Off") + " ticks" + "  Lookback: " + (PerformanceLookbackBars <= 0 ? "All" : PerformanceLookbackBars.ToString()) + " bars");
            sb.AppendLine("");
            sb.AppendLine("Model              Stat  PnL(t) Win%  PF   Avg  DD   CL Age  N  Last");
            sb.AppendLine("--------------------------------------------------------------------");

            IEnumerable<RowModel> displayRows = rows
                .Where(r => ShowDisabledRows || r.Enabled)
                .OrderByDescending(r => r.Score)
                .ThenBy(r => r.Name)
                .Take(MaxRowsToDisplay);

            foreach (RowModel row in displayRows)
            {
                sb.Append(Pad(row.Name, 18));
                sb.Append(Pad(DirectionShort(row.StatusDirection), 6));
                sb.Append(Pad(row.RecentPnl.ToString("0"), 7));
                sb.Append(Pad(row.WinRate.ToString("0"), 6));
                sb.Append(Pad(row.ProfitFactor.ToString("0.0"), 5));
                sb.Append(Pad(row.AverageTrade.ToString("0"), 5));
                sb.Append(Pad(row.MaxDrawdown.ToString("0"), 5));
                sb.Append(Pad(row.ConsecutiveLosses.ToString("0"), 3));
                sb.Append(Pad(row.LastTradeAge >= 999999 ? "-" : row.LastTradeAge.ToString(), 5));
                sb.Append(Pad(row.TradeCount.ToString("0"), 3));
                sb.Append(Pad(row.LastSignalSource, 8));
                sb.Append("Score ");
                sb.Append(row.Score.ToString("0"));
                if (row.Position != null)
                    sb.Append("  OPEN");
                else if (row.WorkingOrder != null)
                    sb.Append("  WORK");
                sb.AppendLine();
            }

            Draw.TextFixed(
                this,
                "ASPG_GT_V002_GRID",
                sb.ToString(),
                DisplayCorner,
                TextBrush ?? Brushes.White,
                gridFont,
                Brushes.Transparent,
                BackgroundBrush ?? Brushes.Black,
                GridOpacity);
        }

        private string DirectionText(int dir)
        {
            if (dir > 0) return "Long Bias";
            if (dir < 0) return "Short Bias";
            return "Neutral";
        }

        private string DirectionShort(int dir)
        {
            if (dir > 0) return "LONG";
            if (dir < 0) return "SHORT";
            return "NEUT";
        }

        private string Pad(string s, int width)
        {
            if (s == null)
                s = string.Empty;
            if (s.Length >= width)
                return s.Substring(0, Math.Max(0, width - 1)) + " ";
            return s.PadRight(width);
        }
        #endregion

        #region Utility
        private bool IsDirectionEnabled(int direction)
        {
            if (direction > 0 && DirectionMode == AdaptiveStrategyGridDirectionMode.ShortOnly)
                return false;
            if (direction < 0 && DirectionMode == AdaptiveStrategyGridDirectionMode.LongOnly)
                return false;
            return direction != 0;
        }

        private bool IsTradingTimeAllowedForBip(int bip)
        {
            int t = ToTime(Times[bip][0]);

            bool inWindow = false;
            if (UseTradeWindow1 && IsTimeBetween(t, TradeWindow1Start, TradeWindow1End)) inWindow = true;
            if (UseTradeWindow2 && IsTimeBetween(t, TradeWindow2Start, TradeWindow2End)) inWindow = true;
            if (UseTradeWindow3 && IsTimeBetween(t, TradeWindow3Start, TradeWindow3End)) inWindow = true;
            if (!UseTradeWindow1 && !UseTradeWindow2 && !UseTradeWindow3) inWindow = true;

            if (!inWindow)
                return false;

            if (UseSkipWindow1 && IsTimeBetween(t, SkipWindow1Start, SkipWindow1End)) return false;
            if (UseSkipWindow2 && IsTimeBetween(t, SkipWindow2Start, SkipWindow2End)) return false;
            if (UseSkipWindow3 && IsTimeBetween(t, SkipWindow3Start, SkipWindow3End)) return false;

            return true;
        }

        private bool IsTimeBetween(int time, int start, int end)
        {
            if (start <= end)
                return time >= start && time <= end;

            return time >= start || time <= end;
        }

        private bool IsPositive(double value)
        {
            return !double.IsNaN(value) && value > 0.5;
        }

        private bool IsNegative(double value)
        {
            return !double.IsNaN(value) && value < -0.5;
        }

        private int NormalizeSignal(double value)
        {
            if (double.IsNaN(value)) return 0;
            if (value > 0.5) return 1;
            if (value < -0.5) return -1;
            return 0;
        }

        private string BuildSignalSource(bool bg, bool fc, bool obr)
        {
            string source = string.Empty;
            if (bg) source = "BG";
            if (fc) source += (source.Length > 0 ? "+" : string.Empty) + "FC";
            if (obr) source += (source.Length > 0 ? "+" : string.Empty) + "OBR";
            return source.Length == 0 ? "None" : source;
        }
        #endregion

        #region Public plot accessors
        [Browsable(false), XmlIgnore] public Series<double> BestDirection            { get { return Values[0]; } }
        [Browsable(false), XmlIgnore] public Series<double> BestScore                { get { return Values[1]; } }
        [Browsable(false), XmlIgnore] public Series<double> BestRecentPnlTicks       { get { return Values[2]; } }
        [Browsable(false), XmlIgnore] public Series<double> BestWinRate              { get { return Values[3]; } }
        [Browsable(false), XmlIgnore] public Series<double> BestProfitFactor         { get { return Values[4]; } }
        [Browsable(false), XmlIgnore] public Series<double> BestAverageTradeTicks    { get { return Values[5]; } }
        [Browsable(false), XmlIgnore] public Series<double> BestDrawdownTicks        { get { return Values[6]; } }
        [Browsable(false), XmlIgnore] public Series<double> BestConsecutiveLosses    { get { return Values[7]; } }
        [Browsable(false), XmlIgnore] public Series<double> BestTradeCount           { get { return Values[8]; } }
        [Browsable(false), XmlIgnore] public Series<double> LongQualified            { get { return Values[9]; } }
        [Browsable(false), XmlIgnore] public Series<double> ShortQualified           { get { return Values[10]; } }
        #endregion

        #region Properties
        [NinjaScriptProperty]
        [Display(Name = "Show Indicator Label", Order = 0, GroupName = "Sentinel")]
        public bool ShowIndicatorLabel { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "TBars BarsPeriodType ID", Order = 1, GroupName = "01. Custom Bar Type IDs")]
        public int TBarsBarsPeriodTypeId { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "NinzaRenko BarsPeriodType ID", Order = 2, GroupName = "01. Custom Bar Type IDs")]
        public int NinzaRenkoBarsPeriodTypeId { get; set; }

        [NinjaScriptProperty] [Display(Name = "Use TBars 4", Order = 1, GroupName = "02. Rows - TBars")] public bool UseTBars4 { get; set; }
        [NinjaScriptProperty] [Display(Name = "Use TBars 12", Order = 2, GroupName = "02. Rows - TBars")] public bool UseTBars12 { get; set; }
        [NinjaScriptProperty] [Display(Name = "Use TBars 21", Order = 3, GroupName = "02. Rows - TBars")] public bool UseTBars21 { get; set; }

        [NinjaScriptProperty] [Display(Name = "Use 1 Minute", Order = 1, GroupName = "03. Rows - Minute")] public bool UseMinute1 { get; set; }
        [NinjaScriptProperty] [Display(Name = "Use 3 Minute", Order = 2, GroupName = "03. Rows - Minute")] public bool UseMinute3 { get; set; }
        [NinjaScriptProperty] [Display(Name = "Use 5 Minute", Order = 3, GroupName = "03. Rows - Minute")] public bool UseMinute5 { get; set; }
        [NinjaScriptProperty] [Display(Name = "Use 15 Minute", Order = 4, GroupName = "03. Rows - Minute")] public bool UseMinute15 { get; set; }

        [NinjaScriptProperty] [Display(Name = "Use HA 1 Minute", Order = 1, GroupName = "04. Rows - Heiken Ashi")] public bool UseHeikenAshiMinute1 { get; set; }
        [NinjaScriptProperty] [Display(Name = "Use HA 3 Minute", Order = 2, GroupName = "04. Rows - Heiken Ashi")] public bool UseHeikenAshiMinute3 { get; set; }
        [NinjaScriptProperty] [Display(Name = "Use HA 5 Minute", Order = 3, GroupName = "04. Rows - Heiken Ashi")] public bool UseHeikenAshiMinute5 { get; set; }

        [NinjaScriptProperty] [Display(Name = "Use Tick 50", Order = 1, GroupName = "05. Rows - Tick")] public bool UseTick50 { get; set; }
        [NinjaScriptProperty] [Display(Name = "Use Tick 90", Order = 2, GroupName = "05. Rows - Tick")] public bool UseTick90 { get; set; }
        [NinjaScriptProperty] [Display(Name = "Use Tick 150", Order = 3, GroupName = "05. Rows - Tick")] public bool UseTick150 { get; set; }
        [NinjaScriptProperty] [Display(Name = "Use Tick 300", Order = 4, GroupName = "05. Rows - Tick")] public bool UseTick300 { get; set; }
        [NinjaScriptProperty] [Display(Name = "Use Tick 500", Order = 5, GroupName = "05. Rows - Tick")] public bool UseTick500 { get; set; }
        [NinjaScriptProperty] [Display(Name = "Use Tick 1000", Order = 6, GroupName = "05. Rows - Tick")] public bool UseTick1000 { get; set; }

        [NinjaScriptProperty] [Display(Name = "Use NinzaRenko 10/1", Order = 1, GroupName = "06. Rows - NinzaRenko")] public bool UseNinzaRenko10_1 { get; set; }
        [NinjaScriptProperty] [Display(Name = "Use NinzaRenko 18/3", Order = 2, GroupName = "06. Rows - NinzaRenko")] public bool UseNinzaRenko18_3 { get; set; }
        [NinjaScriptProperty] [Display(Name = "Use NinzaRenko 32/5", Order = 3, GroupName = "06. Rows - NinzaRenko")] public bool UseNinzaRenko32_5 { get; set; }
        [NinjaScriptProperty] [Display(Name = "Use NinzaRenko 64/15", Order = 4, GroupName = "06. Rows - NinzaRenko")] public bool UseNinzaRenko64_15 { get; set; }

        [NinjaScriptProperty] [Display(Name = "Direction Mode", Order = 1, GroupName = "07. Sim Strategy")]
        public AdaptiveStrategyGridDirectionMode DirectionMode { get; set; }

        [NinjaScriptProperty] [Display(Name = "Entry Mode", Order = 2, GroupName = "07. Sim Strategy")]
        public AdaptiveStrategyGridEntryMode EntryMode { get; set; }

        [NinjaScriptProperty] [Range(0, int.MaxValue)] [Display(Name = "Entry Offset Ticks", Order = 3, GroupName = "07. Sim Strategy")]
        public int EntryOffsetTicks { get; set; }

        [NinjaScriptProperty] [Range(0, int.MaxValue)] [Display(Name = "Cancel Unfilled Entry After Bars", Order = 4, GroupName = "07. Sim Strategy")]
        public int CancelUnfilledEntryAfterBars { get; set; }

        [NinjaScriptProperty] [Display(Name = "Trade Only When Flat", Order = 5, GroupName = "07. Sim Strategy")]
        public bool TradeOnlyWhenFlat { get; set; }

        [NinjaScriptProperty] [Display(Name = "Reverse On Opposite Signal", Order = 6, GroupName = "07. Sim Strategy")]
        public bool ReverseOnOppositeSignal { get; set; }

        [NinjaScriptProperty] [Display(Name = "Ignore Conflicting Signals", Order = 7, GroupName = "07. Sim Strategy")]
        public bool IgnoreConflictingSignals { get; set; }

        [NinjaScriptProperty] [Display(Name = "Enable BG Trades", Order = 1, GroupName = "08. GodTrades Signals")] public bool EnableBGTrades { get; set; }
        [NinjaScriptProperty] [Display(Name = "Enable FC Trades", Order = 2, GroupName = "08. GodTrades Signals")] public bool EnableFCTrades { get; set; }
        [NinjaScriptProperty] [Display(Name = "Enable OBR Trades", Order = 3, GroupName = "08. GodTrades Signals")] public bool EnableOBRTrades { get; set; }

        [NinjaScriptProperty] [Display(Name = "Use Profit Target", Order = 1, GroupName = "09. Exits")] public bool UseProfitTarget { get; set; }
        [NinjaScriptProperty] [Range(1, int.MaxValue)] [Display(Name = "Profit Target Ticks", Order = 2, GroupName = "09. Exits")] public int ProfitTargetTicks { get; set; }
        [NinjaScriptProperty] [Display(Name = "Use Stop Loss", Order = 3, GroupName = "09. Exits")] public bool UseStopLoss { get; set; }
        [NinjaScriptProperty] [Range(1, int.MaxValue)] [Display(Name = "Stop Loss Ticks", Order = 4, GroupName = "09. Exits")] public int StopLossTicks { get; set; }
        [NinjaScriptProperty] [Display(Name = "Use Break Even", Order = 5, GroupName = "09. Exits")] public bool UseBreakEven { get; set; }
        [NinjaScriptProperty] [Range(1, int.MaxValue)] [Display(Name = "Break Even Trigger Ticks", Order = 6, GroupName = "09. Exits")] public int BreakEvenTriggerTicks { get; set; }
        [NinjaScriptProperty] [Range(0, int.MaxValue)] [Display(Name = "Break Even Plus Ticks", Order = 7, GroupName = "09. Exits")] public int BreakEvenPlusTicks { get; set; }
        [NinjaScriptProperty] [Display(Name = "Use Trailing Stop", Order = 8, GroupName = "09. Exits")] public bool UseTrailingStop { get; set; }
        [NinjaScriptProperty] [Range(1, int.MaxValue)] [Display(Name = "Trailing Trigger Ticks", Order = 9, GroupName = "09. Exits")] public int TrailingTriggerTicks { get; set; }
        [NinjaScriptProperty] [Range(1, int.MaxValue)] [Display(Name = "Trailing Distance Ticks", Order = 10, GroupName = "09. Exits")] public int TrailingDistanceTicks { get; set; }
        [NinjaScriptProperty] [Range(1, int.MaxValue)] [Display(Name = "Trailing Step Ticks", Order = 11, GroupName = "09. Exits")] public int TrailingStepTicks { get; set; }

        [NinjaScriptProperty] [Display(Name = "Use Trade Window 1", Order = 1, GroupName = "10. Time Filters")] public bool UseTradeWindow1 { get; set; }
        [NinjaScriptProperty] [Range(0, 235959)] [Display(Name = "Trade Window 1 Start", Order = 2, GroupName = "10. Time Filters")] public int TradeWindow1Start { get; set; }
        [NinjaScriptProperty] [Range(0, 235959)] [Display(Name = "Trade Window 1 End", Order = 3, GroupName = "10. Time Filters")] public int TradeWindow1End { get; set; }
        [NinjaScriptProperty] [Display(Name = "Use Trade Window 2", Order = 4, GroupName = "10. Time Filters")] public bool UseTradeWindow2 { get; set; }
        [NinjaScriptProperty] [Range(0, 235959)] [Display(Name = "Trade Window 2 Start", Order = 5, GroupName = "10. Time Filters")] public int TradeWindow2Start { get; set; }
        [NinjaScriptProperty] [Range(0, 235959)] [Display(Name = "Trade Window 2 End", Order = 6, GroupName = "10. Time Filters")] public int TradeWindow2End { get; set; }
        [NinjaScriptProperty] [Display(Name = "Use Trade Window 3", Order = 7, GroupName = "10. Time Filters")] public bool UseTradeWindow3 { get; set; }
        [NinjaScriptProperty] [Range(0, 235959)] [Display(Name = "Trade Window 3 Start", Order = 8, GroupName = "10. Time Filters")] public int TradeWindow3Start { get; set; }
        [NinjaScriptProperty] [Range(0, 235959)] [Display(Name = "Trade Window 3 End", Order = 9, GroupName = "10. Time Filters")] public int TradeWindow3End { get; set; }
        [NinjaScriptProperty] [Display(Name = "Use Skip Window 1", Order = 10, GroupName = "10. Time Filters")] public bool UseSkipWindow1 { get; set; }
        [NinjaScriptProperty] [Range(0, 235959)] [Display(Name = "Skip Window 1 Start", Order = 11, GroupName = "10. Time Filters")] public int SkipWindow1Start { get; set; }
        [NinjaScriptProperty] [Range(0, 235959)] [Display(Name = "Skip Window 1 End", Order = 12, GroupName = "10. Time Filters")] public int SkipWindow1End { get; set; }
        [NinjaScriptProperty] [Display(Name = "Use Skip Window 2", Order = 13, GroupName = "10. Time Filters")] public bool UseSkipWindow2 { get; set; }
        [NinjaScriptProperty] [Range(0, 235959)] [Display(Name = "Skip Window 2 Start", Order = 14, GroupName = "10. Time Filters")] public int SkipWindow2Start { get; set; }
        [NinjaScriptProperty] [Range(0, 235959)] [Display(Name = "Skip Window 2 End", Order = 15, GroupName = "10. Time Filters")] public int SkipWindow2End { get; set; }
        [NinjaScriptProperty] [Display(Name = "Use Skip Window 3", Order = 16, GroupName = "10. Time Filters")] public bool UseSkipWindow3 { get; set; }
        [NinjaScriptProperty] [Range(0, 235959)] [Display(Name = "Skip Window 3 Start", Order = 17, GroupName = "10. Time Filters")] public int SkipWindow3Start { get; set; }
        [NinjaScriptProperty] [Range(0, 235959)] [Display(Name = "Skip Window 3 End", Order = 18, GroupName = "10. Time Filters")] public int SkipWindow3End { get; set; }

        [NinjaScriptProperty] [Range(1, int.MaxValue)] [Display(Name = "Max Stored Trades", Order = 1, GroupName = "11. Scoring")] public int MaxStoredTrades { get; set; }
        [NinjaScriptProperty] [Range(0, int.MaxValue)] [Display(Name = "Performance Lookback Bars", Order = 2, GroupName = "11. Scoring")] public int PerformanceLookbackBars { get; set; }
        [NinjaScriptProperty] [Range(1, int.MaxValue)] [Display(Name = "Direction Fresh Bars", Order = 3, GroupName = "11. Scoring")] public int DirectionFreshBars { get; set; }
        [NinjaScriptProperty] [Display(Name = "Use Stale Trade Score Penalty", Order = 4, GroupName = "11. Scoring")] public bool UseStaleTradeScorePenalty { get; set; }
        [NinjaScriptProperty] [Range(0, int.MaxValue)] [Display(Name = "Stale Trade Penalty Start Bars", Order = 5, GroupName = "11. Scoring")] public int StaleTradePenaltyStartBars { get; set; }
        [NinjaScriptProperty] [Range(0, 100)] [Display(Name = "Max Stale Trade Score Penalty", Order = 6, GroupName = "11. Scoring")] public double MaxStaleTradeScorePenalty { get; set; }
        [NinjaScriptProperty] [Range(1, int.MaxValue)] [Display(Name = "Min Trades For Trust", Order = 7, GroupName = "11. Scoring")] public int MinTradesForTrust { get; set; }
        [NinjaScriptProperty] [Range(0.1, 20.0)] [Display(Name = "Profit Factor Cap", Order = 3, GroupName = "11. Scoring")] public double ProfitFactorCap { get; set; }
        [NinjaScriptProperty] [Range(0, 1)] [Display(Name = "Score Weight PnL", Order = 4, GroupName = "11. Scoring")] public double ScoreWeightPnl { get; set; }
        [NinjaScriptProperty] [Range(0, 1)] [Display(Name = "Score Weight Win Rate", Order = 5, GroupName = "11. Scoring")] public double ScoreWeightWinRate { get; set; }
        [NinjaScriptProperty] [Range(0, 1)] [Display(Name = "Score Weight Profit Factor", Order = 6, GroupName = "11. Scoring")] public double ScoreWeightProfitFactor { get; set; }
        [NinjaScriptProperty] [Range(0, 1)] [Display(Name = "Score Weight Average Trade", Order = 7, GroupName = "11. Scoring")] public double ScoreWeightAverageTrade { get; set; }
        [NinjaScriptProperty] [Range(0, 1)] [Display(Name = "Score Weight Drawdown", Order = 8, GroupName = "11. Scoring")] public double ScoreWeightDrawdown { get; set; }
        [NinjaScriptProperty] [Range(0, 100)] [Display(Name = "Qualification Score", Order = 9, GroupName = "11. Scoring")] public double QualificationScore { get; set; }
        [NinjaScriptProperty] [Range(1, int.MaxValue)] [Display(Name = "Qualification Min Trades", Order = 10, GroupName = "11. Scoring")] public int QualificationMinTrades { get; set; }
        [NinjaScriptProperty] [Range(0, int.MaxValue)] [Display(Name = "Qualification Max Consecutive Losses", Order = 11, GroupName = "11. Scoring")] public int QualificationMaxConsecutiveLosses { get; set; }

        [NinjaScriptProperty] [Range(0, int.MaxValue)] [Display(Name = "Bars Required To Plot", Order = 1, GroupName = "12. GodTrades Parameters")] public int BarsRequiredToPlot { get; set; }
        [NinjaScriptProperty] [Range(1, int.MaxValue)] [Display(Name = "Minimum Gap Size Ticks", Order = 2, GroupName = "12. GodTrades Parameters")] public int MinimumGapSizeTicks { get; set; }
        [NinjaScriptProperty] [Range(0, int.MaxValue)] [Display(Name = "Minimum Bars Before Valid", Order = 3, GroupName = "12. GodTrades Parameters")] public int MinimumBarsBeforeValid { get; set; }
        [NinjaScriptProperty] [Range(0, int.MaxValue)] [Display(Name = "Minimum Body Ticks", Order = 4, GroupName = "12. GodTrades Parameters")] public int MinimumBodyTicks { get; set; }
        [NinjaScriptProperty] [Range(0, int.MaxValue)] [Display(Name = "Maximum Gap Bar Range Ticks", Order = 5, GroupName = "12. GodTrades Parameters")] public int MaximumGapBarRangeTicks { get; set; }
        [NinjaScriptProperty] [Range(1, int.MaxValue)] [Display(Name = "Maximum Active Gaps To Track", Order = 6, GroupName = "12. GodTrades Parameters")] public int MaximumActiveGapsToTrack { get; set; }
        [NinjaScriptProperty] [Display(Name = "Early Touch Handling", Order = 7, GroupName = "12. GodTrades Parameters")] public GodTrades21EarlyTouchHandling EarlyTouchHandling { get; set; }
        [NinjaScriptProperty] [Display(Name = "Valid Touch Behavior", Order = 8, GroupName = "12. GodTrades Parameters")] public GodTrades21ValidTouchBehavior ValidTouchBehavior { get; set; }
        [NinjaScriptProperty] [Display(Name = "Use Bollinger Midpoint Filter For Continuation", Order = 9, GroupName = "12. GodTrades Parameters")] public bool UseBollingerMidpointFilterForContinuation { get; set; }
        [NinjaScriptProperty] [Display(Name = "FC Bollinger Location Source", Order = 10, GroupName = "12. GodTrades Parameters")] public GodTrades21FcBollingerLocationSource FcBollingerLocationSource { get; set; }
        [NinjaScriptProperty] [Range(0, 100)] [Display(Name = "FC Long Below Midpoint Percent", Order = 11, GroupName = "12. GodTrades Parameters")] public double FcLongBelowMidpointPercent { get; set; }
        [NinjaScriptProperty] [Range(0, 100)] [Display(Name = "FC Short Above Midpoint Percent", Order = 12, GroupName = "12. GodTrades Parameters")] public double FcShortAboveMidpointPercent { get; set; }
        [NinjaScriptProperty] [Display(Name = "Continuation Confirmation Mode", Order = 13, GroupName = "12. GodTrades Parameters")] public GodTrades21ContinuationConfirmationMode ContinuationConfirmationMode { get; set; }
        [NinjaScriptProperty] [Range(0, int.MaxValue)] [Display(Name = "Confirmation Bars After Touch", Order = 14, GroupName = "12. GodTrades Parameters")] public int ConfirmationBarsAfterTouch { get; set; }
        [NinjaScriptProperty] [Display(Name = "Require Signal Candle Direction", Order = 15, GroupName = "12. GodTrades Parameters")] public bool RequireSignalCandleDirection { get; set; }
        [NinjaScriptProperty] [Display(Name = "Require Correct Continuation Approach", Order = 16, GroupName = "12. GodTrades Parameters")] public bool RequireCorrectContinuationApproach { get; set; }
        [NinjaScriptProperty] [Display(Name = "Use Bollinger Midpoint Filter For OBR", Order = 17, GroupName = "12. GodTrades Parameters")] public bool UseBollingerMidpointFilterForOutsideBarReversal { get; set; }
        [NinjaScriptProperty] [Display(Name = "Allow OBR Bar Outside Bollinger Band", Order = 18, GroupName = "12. GodTrades Parameters")] public bool AllowObrBarOutsideBollingerBand { get; set; }
        [NinjaScriptProperty] [Range(0, int.MaxValue)] [Display(Name = "Bearish OBR Upper Tolerance Ticks", Order = 19, GroupName = "12. GodTrades Parameters")] public int BearishObrUpperBandTouchToleranceTicks { get; set; }
        [NinjaScriptProperty] [Range(0, int.MaxValue)] [Display(Name = "Bullish OBR Lower Tolerance Ticks", Order = 20, GroupName = "12. GodTrades Parameters")] public int BullishObrLowerBandTouchToleranceTicks { get; set; }
        [NinjaScriptProperty] [Display(Name = "Use Indicator Signal Time Filter", Order = 21, GroupName = "12. GodTrades Parameters")] public bool UseIndicatorSignalTimeFilter { get; set; }
        [NinjaScriptProperty] [Range(0, 235959)] [Display(Name = "Indicator Signal Start Time", Order = 22, GroupName = "12. GodTrades Parameters")] public int IndicatorSignalStartTime { get; set; }
        [NinjaScriptProperty] [Range(0, 235959)] [Display(Name = "Indicator Signal End Time", Order = 23, GroupName = "12. GodTrades Parameters")] public int IndicatorSignalEndTime { get; set; }
        [NinjaScriptProperty] [Range(1, int.MaxValue)] [Display(Name = "Bollinger Period", Order = 24, GroupName = "12. GodTrades Parameters")] public int BollingerPeriod { get; set; }
        [NinjaScriptProperty] [Range(0.1, double.MaxValue)] [Display(Name = "Bollinger Std Dev", Order = 25, GroupName = "12. GodTrades Parameters")] public double BollingerStdDev { get; set; }
        [NinjaScriptProperty] [Range(0, int.MaxValue)] [Display(Name = "Bollinger Band Proximity Ticks", Order = 26, GroupName = "12. GodTrades Parameters")] public int BollingerBandProximityTicks { get; set; }
        [NinjaScriptProperty] [Display(Name = "Enable Spiderweb Warning", Order = 27, GroupName = "12. GodTrades Parameters")] public bool EnableSpiderwebWarning { get; set; }
        [NinjaScriptProperty] [Display(Name = "Show Spiderweb Warning Text", Order = 28, GroupName = "12. GodTrades Parameters")] public bool ShowSpiderwebWarningText { get; set; }
        [NinjaScriptProperty] [Range(1, int.MaxValue)] [Display(Name = "Spiderweb Distance Ticks", Order = 29, GroupName = "12. GodTrades Parameters")] public int SpiderwebDistanceTicks { get; set; }
        [NinjaScriptProperty] [Range(1, int.MaxValue)] [Display(Name = "Spiderweb Line Count", Order = 30, GroupName = "12. GodTrades Parameters")] public int SpiderwebLineCount { get; set; }
        [NinjaScriptProperty] [Range(1, int.MaxValue)] [Display(Name = "Spiderweb Text Font Size", Order = 31, GroupName = "12. GodTrades Parameters")] public int SpiderwebTextFontSize { get; set; }

        [NinjaScriptProperty] [Display(Name = "Show Grid", Order = 1, GroupName = "13. Display")] public bool ShowGrid { get; set; }
        [NinjaScriptProperty] [Range(1, 100)] [Display(Name = "Max Rows To Display", Order = 2, GroupName = "13. Display")] public int MaxRowsToDisplay { get; set; }
        [NinjaScriptProperty] [Display(Name = "Display Corner", Order = 3, GroupName = "13. Display")] public TextPosition DisplayCorner { get; set; }
        [NinjaScriptProperty] [Display(Name = "Show Disabled Rows", Order = 4, GroupName = "13. Display")] public bool ShowDisabledRows { get; set; }
        [NinjaScriptProperty] [Range(0, 100)] [Display(Name = "Grid Opacity", Order = 5, GroupName = "13. Display")] public int GridOpacity { get; set; }
        [NinjaScriptProperty] [Range(6, 40)] [Display(Name = "Grid Font Size", Order = 6, GroupName = "13. Display")] public int GridFontSize { get; set; }

        [XmlIgnore] [Display(Name = "Strong Bull Brush", Order = 1, GroupName = "14. Brushes")] public Brush StrongBullBrush { get; set; }
        [Browsable(false)] public string StrongBullBrushSerializable { get { return Serialize.BrushToString(StrongBullBrush); } set { StrongBullBrush = Serialize.StringToBrush(value); } }
        [XmlIgnore] [Display(Name = "Bull Brush", Order = 2, GroupName = "14. Brushes")] public Brush BullBrush { get; set; }
        [Browsable(false)] public string BullBrushSerializable { get { return Serialize.BrushToString(BullBrush); } set { BullBrush = Serialize.StringToBrush(value); } }
        [XmlIgnore] [Display(Name = "Neutral Brush", Order = 3, GroupName = "14. Brushes")] public Brush NeutralBrush { get; set; }
        [Browsable(false)] public string NeutralBrushSerializable { get { return Serialize.BrushToString(NeutralBrush); } set { NeutralBrush = Serialize.StringToBrush(value); } }
        [XmlIgnore] [Display(Name = "Bear Brush", Order = 4, GroupName = "14. Brushes")] public Brush BearBrush { get; set; }
        [Browsable(false)] public string BearBrushSerializable { get { return Serialize.BrushToString(BearBrush); } set { BearBrush = Serialize.StringToBrush(value); } }
        [XmlIgnore] [Display(Name = "Strong Bear Brush", Order = 5, GroupName = "14. Brushes")] public Brush StrongBearBrush { get; set; }
        [Browsable(false)] public string StrongBearBrushSerializable { get { return Serialize.BrushToString(StrongBearBrush); } set { StrongBearBrush = Serialize.StringToBrush(value); } }
        [XmlIgnore] [Display(Name = "Text Brush", Order = 6, GroupName = "14. Brushes")] public Brush TextBrush { get; set; }
        [Browsable(false)] public string TextBrushSerializable { get { return Serialize.BrushToString(TextBrush); } set { TextBrush = Serialize.StringToBrush(value); } }
        [XmlIgnore] [Display(Name = "Background Brush", Order = 7, GroupName = "14. Brushes")] public Brush BackgroundBrush { get; set; }
        [Browsable(false)] public string BackgroundBrushSerializable { get { return Serialize.BrushToString(BackgroundBrush); } set { BackgroundBrush = Serialize.StringToBrush(value); } }
        #endregion
    }
}
