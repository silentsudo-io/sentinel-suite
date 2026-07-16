// ─────────────────────────────────────────────────────────────────────────────
// This Source Code Form is subject to the terms of the Mozilla Public License,
// v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
// one at https://mozilla.org/MPL/2.0/.
//
// Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
//
// PROVENANCE: the author's own work (DD / GodTrades) — self-derived, original.
// A standalone indicator-only "test rig" analysis tool (bars-per-session advisor).
// NOT a Council signal: no SentinelCore State seam, no Council voter, no hidden Signal plot.
// ─────────────────────────────────────────────────────────────────────────────
#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Xml.Serialization;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.AddOns.Sentinel;
using NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors
{
    public class SentinelBarsPerSessionAdvisor_v1_0_0 : Indicator
    {
        private int    sessionBarCount;
        private int    lastCompletedSessionBars;
        private int    sessionsSeen;
        private double recommendedBarSize;
        private double pctChange;
        private string recommendationText;
        private Brush  recommendationBrush;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Bars-per-session advisor for choosing Tick chart size. Shows whether current bar size is too small, too large, or OK.";
                Name = "Sentinel Bars Per Session Advisor v1.0.0";
                ShowIndicatorLabel = false;
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = false;
                DrawOnPricePanel = true;
                PaintPriceMarkers = false;
                IsSuspendedWhileInactive = false;

                TargetMinBars = 150;
                TargetMaxBars = 400;
                TargetBars = 275;
                RoundToNearest = 50;
                UseCurrentSessionBeforeComplete = true;
                MinBarsBeforeLiveEstimate = 50;

                TextPosition = TextPosition.TopRight;
                TextFont = new SimpleFont("Consolas", 13);
                NormalBrush = Brushes.LimeGreen;
                WarningBrush = Brushes.Gold;
                BadBrush = Brushes.OrangeRed;
                TextBrush = Brushes.White;
                BackgroundBrush = Brushes.Black;
                BackgroundOpacity = 65;
            }
            else if (State == State.DataLoaded)
            {
                if (!ShowIndicatorLabel) Name = string.Empty;
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1)
                return;

            if (Bars.IsFirstBarOfSession)
            {
                if (sessionBarCount > 0)
                {
                    lastCompletedSessionBars = sessionBarCount;
                    sessionsSeen++;
                }

                sessionBarCount = 1;
            }
            else
            {
                sessionBarCount++;
            }

            UpdateRecommendation();
            DrawPanel();
        }

        private void UpdateRecommendation()
        {
            int measuredBars = lastCompletedSessionBars;
            string source = "prev session";

            if ((measuredBars <= 0 || UseCurrentSessionBeforeComplete) && sessionBarCount >= MinBarsBeforeLiveEstimate)
            {
                measuredBars = sessionBarCount;
                source = "current partial";
            }

            if (measuredBars <= 0)
            {
                recommendationText = "Collecting session data...";
                recommendationBrush = WarningBrush;
                recommendedBarSize = CurrentBarSize();
                pctChange = 0;
                return;
            }

            double currentSize = CurrentBarSize();
            bool isTickChart = BarsPeriod.BarsPeriodType == BarsPeriodType.Tick;

            if (currentSize <= 0)
                currentSize = 1;

            // Core formula:
            // If measured bars are too many, increase bar size.
            // If measured bars are too few, reduce bar size.
            // RecommendedSize = CurrentSize * MeasuredBars / TargetBars
            double rawRecommended = currentSize * measuredBars / Math.Max(1.0, TargetBars);
            recommendedBarSize = RoundToStep(rawRecommended, Math.Max(1, RoundToNearest));
            recommendedBarSize = Math.Max(1, recommendedBarSize);
            pctChange = ((recommendedBarSize - currentSize) / currentSize) * 100.0;

            if (!isTickChart)
            {
                recommendationText = "Not a Tick chart - formula is for Tick bars";
                recommendationBrush = BadBrush;
                return;
            }

            if (measuredBars > TargetMaxBars)
            {
                recommendationText = string.Format("TOO MANY bars ({0}) - go BIGGER", measuredBars);
                recommendationBrush = BadBrush;
            }
            else if (measuredBars < TargetMinBars)
            {
                recommendationText = string.Format("TOO FEW bars ({0}) - go SMALLER", measuredBars);
                recommendationBrush = WarningBrush;
            }
            else
            {
                recommendationText = string.Format("OK range ({0} bars) - keep similar", measuredBars);
                recommendationBrush = NormalBrush;
            }

            recommendationText += " | " + source;
        }

        private double CurrentBarSize()
        {
            // For Tick charts this is the tick count, e.g. 500.
            return BarsPeriod != null ? BarsPeriod.Value : 0;
        }

        private double RoundToStep(double value, int step)
        {
            if (step <= 1)
                return Math.Round(value);

            return Math.Max(step, Math.Round(value / step) * step);
        }

        private void DrawPanel()
        {
            double currentSize = CurrentBarSize();
            int measuredBars = lastCompletedSessionBars > 0 ? lastCompletedSessionBars : sessionBarCount;

            string pct = pctChange >= 0 ? "+" + pctChange.ToString("0") + "%" : pctChange.ToString("0") + "%";
            string action = pctChange > 5 ? "Increase" : pctChange < -5 ? "Reduce" : "No major change";

            string text =
                "Bars/Session Advisor" + Environment.NewLine +
                Instrument.FullName + " | " + BarsPeriod.BarsPeriodType + " " + BarsPeriod.Value + Environment.NewLine +
                "Target: " + TargetMinBars + "-" + TargetMaxBars + " bars  Mid: " + TargetBars + Environment.NewLine +
                "Current session bars: " + sessionBarCount + Environment.NewLine +
                "Previous session bars: " + (lastCompletedSessionBars > 0 ? lastCompletedSessionBars.ToString() : "n/a") + Environment.NewLine +
                recommendationText + Environment.NewLine +
                "Recommended: " + recommendedBarSize.ToString("0") + " ticks" + Environment.NewLine +
                action + ": " + pct;

            Draw.TextFixed(
                this,
                "DDBarsPerSessionAdvisorPanel",
                text,
                TextPosition,
                recommendationBrush,
                TextFont,
                Brushes.Transparent,
                BackgroundBrush,
                BackgroundOpacity);
        }

        #region Properties

        [NinjaScriptProperty]
        [Display(Name = "Show Indicator Label", Order = 0, GroupName = "Sentinel")]
        public bool ShowIndicatorLabel { get; set; }

        [NinjaScriptProperty]
        [Range(20, 2000)]
        [Display(Name = "Target Min Bars", Order = 1, GroupName = "01. Bars Per Session")]
        public int TargetMinBars { get; set; }

        [NinjaScriptProperty]
        [Range(20, 3000)]
        [Display(Name = "Target Max Bars", Order = 2, GroupName = "01. Bars Per Session")]
        public int TargetMaxBars { get; set; }

        [NinjaScriptProperty]
        [Range(20, 3000)]
        [Display(Name = "Target Bars Midpoint", Order = 3, GroupName = "01. Bars Per Session")]
        public int TargetBars { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "Round To Nearest", Description = "Recommendation rounding step, e.g. 25, 50, 100.", Order = 4, GroupName = "01. Bars Per Session")]
        public int RoundToNearest { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Current Session Before Complete", Order = 5, GroupName = "01. Bars Per Session")]
        public bool UseCurrentSessionBeforeComplete { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "Min Bars Before Live Estimate", Order = 6, GroupName = "01. Bars Per Session")]
        public int MinBarsBeforeLiveEstimate { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Text Position", Order = 1, GroupName = "02. Visual")]
        public TextPosition TextPosition { get; set; }

        [XmlIgnore]
        [Display(Name = "Text Font", Order = 2, GroupName = "02. Visual")]
        public SimpleFont TextFont { get; set; }

        [XmlIgnore]
        [Display(Name = "OK Brush", Order = 3, GroupName = "02. Visual")]
        public Brush NormalBrush { get; set; }

        [Browsable(false)]
        public string NormalBrushSerialize
        {
            get { return Serialize.BrushToString(NormalBrush); }
            set { NormalBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Warning Brush", Order = 4, GroupName = "02. Visual")]
        public Brush WarningBrush { get; set; }

        [Browsable(false)]
        public string WarningBrushSerialize
        {
            get { return Serialize.BrushToString(WarningBrush); }
            set { WarningBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Bad Brush", Order = 5, GroupName = "02. Visual")]
        public Brush BadBrush { get; set; }

        [Browsable(false)]
        public string BadBrushSerialize
        {
            get { return Serialize.BrushToString(BadBrush); }
            set { BadBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Text Brush", Order = 6, GroupName = "02. Visual")]
        public Brush TextBrush { get; set; }

        [Browsable(false)]
        public string TextBrushSerialize
        {
            get { return Serialize.BrushToString(TextBrush); }
            set { TextBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Background Brush", Order = 7, GroupName = "02. Visual")]
        public Brush BackgroundBrush { get; set; }

        [Browsable(false)]
        public string BackgroundBrushSerialize
        {
            get { return Serialize.BrushToString(BackgroundBrush); }
            set { BackgroundBrush = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Background Opacity", Order = 8, GroupName = "02. Visual")]
        public int BackgroundOpacity { get; set; }

        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private Sentinel.Sensors.SentinelBarsPerSessionAdvisor_v1_0_0[] cacheSentinelBarsPerSessionAdvisor_v1_0_0;
		public Sentinel.Sensors.SentinelBarsPerSessionAdvisor_v1_0_0 SentinelBarsPerSessionAdvisor_v1_0_0(bool showIndicatorLabel, int targetMinBars, int targetMaxBars, int targetBars, int roundToNearest, bool useCurrentSessionBeforeComplete, int minBarsBeforeLiveEstimate, TextPosition textPosition, int backgroundOpacity)
		{
			return SentinelBarsPerSessionAdvisor_v1_0_0(Input, showIndicatorLabel, targetMinBars, targetMaxBars, targetBars, roundToNearest, useCurrentSessionBeforeComplete, minBarsBeforeLiveEstimate, textPosition, backgroundOpacity);
		}

		public Sentinel.Sensors.SentinelBarsPerSessionAdvisor_v1_0_0 SentinelBarsPerSessionAdvisor_v1_0_0(ISeries<double> input, bool showIndicatorLabel, int targetMinBars, int targetMaxBars, int targetBars, int roundToNearest, bool useCurrentSessionBeforeComplete, int minBarsBeforeLiveEstimate, TextPosition textPosition, int backgroundOpacity)
		{
			if (cacheSentinelBarsPerSessionAdvisor_v1_0_0 != null)
				for (int idx = 0; idx < cacheSentinelBarsPerSessionAdvisor_v1_0_0.Length; idx++)
					if (cacheSentinelBarsPerSessionAdvisor_v1_0_0[idx] != null && cacheSentinelBarsPerSessionAdvisor_v1_0_0[idx].ShowIndicatorLabel == showIndicatorLabel && cacheSentinelBarsPerSessionAdvisor_v1_0_0[idx].TargetMinBars == targetMinBars && cacheSentinelBarsPerSessionAdvisor_v1_0_0[idx].TargetMaxBars == targetMaxBars && cacheSentinelBarsPerSessionAdvisor_v1_0_0[idx].TargetBars == targetBars && cacheSentinelBarsPerSessionAdvisor_v1_0_0[idx].RoundToNearest == roundToNearest && cacheSentinelBarsPerSessionAdvisor_v1_0_0[idx].UseCurrentSessionBeforeComplete == useCurrentSessionBeforeComplete && cacheSentinelBarsPerSessionAdvisor_v1_0_0[idx].MinBarsBeforeLiveEstimate == minBarsBeforeLiveEstimate && cacheSentinelBarsPerSessionAdvisor_v1_0_0[idx].TextPosition == textPosition && cacheSentinelBarsPerSessionAdvisor_v1_0_0[idx].BackgroundOpacity == backgroundOpacity && cacheSentinelBarsPerSessionAdvisor_v1_0_0[idx].EqualsInput(input))
						return cacheSentinelBarsPerSessionAdvisor_v1_0_0[idx];
			return CacheIndicator<Sentinel.Sensors.SentinelBarsPerSessionAdvisor_v1_0_0>(new Sentinel.Sensors.SentinelBarsPerSessionAdvisor_v1_0_0(){ ShowIndicatorLabel = showIndicatorLabel, TargetMinBars = targetMinBars, TargetMaxBars = targetMaxBars, TargetBars = targetBars, RoundToNearest = roundToNearest, UseCurrentSessionBeforeComplete = useCurrentSessionBeforeComplete, MinBarsBeforeLiveEstimate = minBarsBeforeLiveEstimate, TextPosition = textPosition, BackgroundOpacity = backgroundOpacity }, input, ref cacheSentinelBarsPerSessionAdvisor_v1_0_0);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.Sentinel.Sensors.SentinelBarsPerSessionAdvisor_v1_0_0 SentinelBarsPerSessionAdvisor_v1_0_0(bool showIndicatorLabel, int targetMinBars, int targetMaxBars, int targetBars, int roundToNearest, bool useCurrentSessionBeforeComplete, int minBarsBeforeLiveEstimate, TextPosition textPosition, int backgroundOpacity)
		{
			return indicator.SentinelBarsPerSessionAdvisor_v1_0_0(Input, showIndicatorLabel, targetMinBars, targetMaxBars, targetBars, roundToNearest, useCurrentSessionBeforeComplete, minBarsBeforeLiveEstimate, textPosition, backgroundOpacity);
		}

		public Indicators.Sentinel.Sensors.SentinelBarsPerSessionAdvisor_v1_0_0 SentinelBarsPerSessionAdvisor_v1_0_0(ISeries<double> input , bool showIndicatorLabel, int targetMinBars, int targetMaxBars, int targetBars, int roundToNearest, bool useCurrentSessionBeforeComplete, int minBarsBeforeLiveEstimate, TextPosition textPosition, int backgroundOpacity)
		{
			return indicator.SentinelBarsPerSessionAdvisor_v1_0_0(input, showIndicatorLabel, targetMinBars, targetMaxBars, targetBars, roundToNearest, useCurrentSessionBeforeComplete, minBarsBeforeLiveEstimate, textPosition, backgroundOpacity);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.Sentinel.Sensors.SentinelBarsPerSessionAdvisor_v1_0_0 SentinelBarsPerSessionAdvisor_v1_0_0(bool showIndicatorLabel, int targetMinBars, int targetMaxBars, int targetBars, int roundToNearest, bool useCurrentSessionBeforeComplete, int minBarsBeforeLiveEstimate, TextPosition textPosition, int backgroundOpacity)
		{
			return indicator.SentinelBarsPerSessionAdvisor_v1_0_0(Input, showIndicatorLabel, targetMinBars, targetMaxBars, targetBars, roundToNearest, useCurrentSessionBeforeComplete, minBarsBeforeLiveEstimate, textPosition, backgroundOpacity);
		}

		public Indicators.Sentinel.Sensors.SentinelBarsPerSessionAdvisor_v1_0_0 SentinelBarsPerSessionAdvisor_v1_0_0(ISeries<double> input , bool showIndicatorLabel, int targetMinBars, int targetMaxBars, int targetBars, int roundToNearest, bool useCurrentSessionBeforeComplete, int minBarsBeforeLiveEstimate, TextPosition textPosition, int backgroundOpacity)
		{
			return indicator.SentinelBarsPerSessionAdvisor_v1_0_0(input, showIndicatorLabel, targetMinBars, targetMaxBars, targetBars, roundToNearest, useCurrentSessionBeforeComplete, minBarsBeforeLiveEstimate, textPosition, backgroundOpacity);
		}
	}
}

#endregion
