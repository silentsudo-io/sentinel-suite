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
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.AddOns.Sentinel;             // SentinelSkin (glass card) + SentinelCardCorner
using NinjaTrader.NinjaScript.Indicators.Sentinel.Smoothers; // own namespace (bare-enum codegen resolves here)
#endregion

// ═════════════════════════════════════════════════════════════════════════════
//  Sentinel Gaussian Filter — Ehlers Gaussian low-pass (smoother block)        |   Version v1.0.0
//  File: SentinelGaussianFilter_v1_0_0.cs  |  namespace …Indicators.Sentinel.Smoothers  |  display "Sentinel Gaussian Filter"
//
//  ⚠ NO ORDERS · NO STATE SEAM — a read-only SMOOTHER building block, not a Council voter. It draws a
//  smoothed line + a Sentinel glass card and publishes nothing (a filter has no verdict).
//
//  PROVENANCE / LICENSE: CLEAN-ROOM. Reimplemented from the PUBLIC John Ehlers Gaussian filter formula
//  (1..4 pole IIR low-pass with binomial recursion, published in "Gaussian and Other Low Lag Filters") —
//  a mathematical DSP method, not copyrightable. No third-party code, variable names, or structure were
//  copied; the "Au" source was read ONLY to confirm the pole range (1..4) and the β/α definition. See repo NOTICE.
//
//  MATH (Ehlers; 360/P° == 2π/P rad):
//    β = (1 − cos(2π/P)) / (2^(1/N) − 1)      (2^(1/N) == √2^(2/N))
//    α = (P==1) ? 1 : −β + √(β² + 2β)          ( = −β + √(β(β+2)) )
//    N-pole recursion, g = (1−α):
//      1: y = α ·x + g·y[1]
//      2: y = α²·x + 2g·y[1] − g²·y[2]
//      3: y = α³·x + 3g·y[1] − 3g²·y[2] + g³·y[3]
//      4: y = α⁴·x + 4g·y[1] − 6g²·y[2] + 4g³·y[3] − g⁴·y[4]
//    (recursion coefficients are the signed binomial C(N,k), the standard N-fold single-pole cascade.)
//
//  ASSUMPTIONS:
//    • Poles is restricted to {1,2,3,4}; other values clamp into range.
//    • Recursion is computed live each tick from the current bar's Input and PRIOR-bar filter outputs
//      (Value[1..N]); early bars (CurrentBar < Poles) seed to Input[0].
//
//  CHANGELOG
//    v1.0.0 (2026-07-12) — clean-room Gaussian filter + Sentinel plumbing (naming law, glass card, label remover).
// ═════════════════════════════════════════════════════════════════════════════
namespace NinjaTrader.NinjaScript.Indicators.Sentinel.Smoothers
{
    public class SentinelGaussianFilter_v1_0_0 : Indicator
    {
        private SentinelSkin.Painter _sp;
        // cached for the card (data thread) — OnRender must never touch Value[]
        private double _cardVal;
        private int    _cardSlope;
        private bool   _cardHasData;
        private int    _poles = 3;
        private double _alpha, _a1, _a2, _a3, _a4;   // α and α^N
        private double _g1, _g2, _g3, _g4;           // (1−α)^k

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description              = "Sentinel smoother library — Ehlers Gaussian low-pass filter (clean-room, 1..4 pole IIR). Draws the smoothed line + a Sentinel glass card. A building block, not a Council voter (no State seam).";
                Name                     = "Sentinel Gaussian Filter v1.0.0";
                Calculate                = Calculate.OnPriceChange;
                IsOverlay                = true;
                IsSuspendedWhileInactive = true;
                DrawOnPricePanel         = true;

                Period                   = 20;
                Poles                    = 3;

                ShowCard           = true;
                CardCorner         = SentinelCardCorner.TopRight;
                ShowIndicatorLabel = false;

                AddPlot(new Stroke(Brushes.DeepSkyBlue, 2), PlotStyle.Line, "Gaussian");
            }
            else if (State == State.DataLoaded)
            {
                if (!ShowIndicatorLabel) Name = string.Empty;   // Sentinel label remover
                ComputeCoefficients();
            }
            else if (State == State.Terminated)
            {
                if (_sp != null) { try { _sp.Dispose(); } catch { } _sp = null; }
                try { SentinelSkin.CardLayout.Release(this); } catch { }
            }
        }

        private void ComputeCoefficients()
        {
            double beta = (1.0 - Math.Cos(2.0 * Math.PI / Period)) / (Math.Pow(2.0, 1.0 / Poles) - 1.0);
            _alpha = (Period == 1) ? 1.0 : (-beta + Math.Sqrt(beta * (beta + 2.0)));

            _a1 = _alpha;
            _a2 = _a1 * _alpha;
            _a3 = _a2 * _alpha;
            _a4 = _a3 * _alpha;

            double g = 1.0 - _alpha;
            _g1 = g;
            _g2 = _g1 * g;
            _g3 = _g2 * g;
            _g4 = _g3 * g;
        }

        protected override void OnBarUpdate()
        {
            // Seed early bars until the recursion has the lags it needs.
            if (CurrentBar < Poles)
            {
                Value[0] = Input[0];
                return;
            }

            switch (Poles)
            {
                case 1:
                    Value[0] = _a1 * Input[0] + _g1 * Value[1];
                    break;
                case 2:
                    Value[0] = _a2 * Input[0] + 2.0 * _g1 * Value[1] - _g2 * Value[2];
                    break;
                case 3:
                    Value[0] = _a3 * Input[0] + 3.0 * _g1 * Value[1] - 3.0 * _g2 * Value[2] + _g3 * Value[3];
                    break;
                default: // 4
                    Value[0] = _a4 * Input[0] + 4.0 * _g1 * Value[1] - 6.0 * _g2 * Value[2] + 4.0 * _g3 * Value[3] - _g4 * Value[4];
                    break;
            }
            // cache the card readout here (data thread), so OnRender never touches Value[]
            _cardVal    = Value[0];
            _cardSlope  = (CurrentBar >= 1) ? (Value[0] > Value[1] ? 1 : (Value[0] < Value[1] ? -1 : 0)) : 0;
            _cardHasData = true;
        }

        // ── Sentinel glass card ──
        protected override void OnRender(NinjaTrader.Gui.Chart.ChartControl chartControl, NinjaTrader.Gui.Chart.ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);
            if (!ShowCard || RenderTarget == null || ChartPanel == null) return;
            if (_sp == null) _sp = new SentinelSkin.Painter();
            _sp.Begin(RenderTarget);
            try { RenderCard(); } catch { }
            try { _sp.End(); } catch { }   // ALWAYS runs — a skipped End() would silently kill the card
        }

        private void RenderCard()
        {
            const float cw = 210f, ch = 92f;
            var slot = SentinelSkin.CardLayout.Place(this, ChartPanel,
                ChartPanel.X, ChartPanel.Y, ChartPanel.W, ChartPanel.H, CardCorner, cw, ch);

            var slopeCol = _cardSlope > 0 ? SentinelSkin.CUp : _cardSlope < 0 ? SentinelSkin.CDown : SentinelSkin.CMute;
            string arrow = _cardSlope > 0 ? "▲" : _cardSlope < 0 ? "▼" : "▬";

            var r = _sp.Card(slot.X, slot.Y, cw, ch, SentinelSkin.CLine);
            _sp.Dot(r.Left + 5f, r.Top + 8f, _cardHasData ? SentinelSkin.CAccent : SentinelSkin.CMute, _cardHasData);
            _sp.Text("Gaussian", r.Left + 16f, r.Top, r.Width - 70f, 16f, SentinelSkin.CInk, 11f, true);
            _sp.Pill(Poles + "p·" + Period, r.Right, r.Top - 1f, SentinelSkin.CMute);

            if (_cardHasData)
            {
                _sp.Text(arrow, r.Left, r.Top + 30f, 20f, 22f, slopeCol, 15f, false);
                _sp.Text(_cardVal.ToString("0.####"), r.Left + 22f, r.Top + 28f, r.Width - 22f, 24f, SentinelSkin.CInk, 17f, false);
            }
            else
            {
                _sp.Text("loading…", r.Left, r.Top + 30f, r.Width, 16f, SentinelSkin.CMute, 10.5f);
            }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> Gaussian => Values[0];

        #region Properties
        [Range(1, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Period", Description = "Filter period (cutoff).", Order = 1, GroupName = "Parameters")]
        public int Period { get; set; }

        [Range(1, 4)]
        [NinjaScriptProperty]
        [Display(Name = "Poles", Description = "Number of poles: 1..4 (Ehlers Gaussian variants).", Order = 2, GroupName = "Parameters")]
        public int Poles
        {
            get { return _poles; }
            set { _poles = Math.Min(4, Math.Max(1, value)); }
        }

        [NinjaScriptProperty]
        [Display(Name = "Show Card", Order = 12, GroupName = "Sentinel")]
        public bool ShowCard { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Card Corner", Description = "Which chart corner the Sentinel card docks to. Cards in the same corner auto-stack.", Order = 13, GroupName = "Sentinel")]
        public SentinelCardCorner CardCorner { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show indicator label", Description = "Show NinjaTrader's chart name label. Sentinel default = OFF (clean chart); turn on to restore it.", Order = 100, GroupName = "Sentinel")]
        public bool ShowIndicatorLabel { get; set; }
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private Sentinel.Smoothers.SentinelGaussianFilter_v1_0_0[] cacheSentinelGaussianFilter_v1_0_0;
		public Sentinel.Smoothers.SentinelGaussianFilter_v1_0_0 SentinelGaussianFilter_v1_0_0(int period, int poles, bool showCard, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return SentinelGaussianFilter_v1_0_0(Input, period, poles, showCard, cardCorner, showIndicatorLabel);
		}

		public Sentinel.Smoothers.SentinelGaussianFilter_v1_0_0 SentinelGaussianFilter_v1_0_0(ISeries<double> input, int period, int poles, bool showCard, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			if (cacheSentinelGaussianFilter_v1_0_0 != null)
				for (int idx = 0; idx < cacheSentinelGaussianFilter_v1_0_0.Length; idx++)
					if (cacheSentinelGaussianFilter_v1_0_0[idx] != null && cacheSentinelGaussianFilter_v1_0_0[idx].Period == period && cacheSentinelGaussianFilter_v1_0_0[idx].Poles == poles && cacheSentinelGaussianFilter_v1_0_0[idx].ShowCard == showCard && cacheSentinelGaussianFilter_v1_0_0[idx].CardCorner == cardCorner && cacheSentinelGaussianFilter_v1_0_0[idx].ShowIndicatorLabel == showIndicatorLabel && cacheSentinelGaussianFilter_v1_0_0[idx].EqualsInput(input))
						return cacheSentinelGaussianFilter_v1_0_0[idx];
			return CacheIndicator<Sentinel.Smoothers.SentinelGaussianFilter_v1_0_0>(new Sentinel.Smoothers.SentinelGaussianFilter_v1_0_0(){ Period = period, Poles = poles, ShowCard = showCard, CardCorner = cardCorner, ShowIndicatorLabel = showIndicatorLabel }, input, ref cacheSentinelGaussianFilter_v1_0_0);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.Sentinel.Smoothers.SentinelGaussianFilter_v1_0_0 SentinelGaussianFilter_v1_0_0(int period, int poles, bool showCard, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return indicator.SentinelGaussianFilter_v1_0_0(Input, period, poles, showCard, cardCorner, showIndicatorLabel);
		}

		public Indicators.Sentinel.Smoothers.SentinelGaussianFilter_v1_0_0 SentinelGaussianFilter_v1_0_0(ISeries<double> input , int period, int poles, bool showCard, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return indicator.SentinelGaussianFilter_v1_0_0(input, period, poles, showCard, cardCorner, showIndicatorLabel);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.Sentinel.Smoothers.SentinelGaussianFilter_v1_0_0 SentinelGaussianFilter_v1_0_0(int period, int poles, bool showCard, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return indicator.SentinelGaussianFilter_v1_0_0(Input, period, poles, showCard, cardCorner, showIndicatorLabel);
		}

		public Indicators.Sentinel.Smoothers.SentinelGaussianFilter_v1_0_0 SentinelGaussianFilter_v1_0_0(ISeries<double> input , int period, int poles, bool showCard, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return indicator.SentinelGaussianFilter_v1_0_0(input, period, poles, showCard, cardCorner, showIndicatorLabel);
		}
	}
}

#endregion
