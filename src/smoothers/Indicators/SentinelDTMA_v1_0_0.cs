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
//  Sentinel DTMA — de-lagged Double Triangular Moving Average (smoother block) |   Version v1.0.0
//  File: SentinelDTMA_v1_0_0.cs  |  namespace …Indicators.Sentinel.Smoothers  |  display "Sentinel DTMA"
//
//  ⚠ NO ORDERS · NO STATE SEAM — a read-only SMOOTHER building block, not a Council voter. It draws a
//  smoothed line + a Sentinel glass card and publishes nothing.
//
//  IDENTITY OF THE SOURCE: the "AuDTMA" source (Description "DTMA — Double Triangular Moving Average")
//  computes  y = 2·TMA(x,P) − TMA(TMA(x,P),P)  — a "twicing" / de-lagged Triangular MA (the TMA analogue
//  of DEMA), where TMA (Triangular MA) is itself a double SMA. This port reimplements THAT method.
//
//  PROVENANCE / LICENSE: CLEAN-ROOM. Reimplemented from PUBLIC, standard constructions — the Triangular
//  Moving Average (TMA = SMA∘SMA, a triangular-weighted window) and the "twicing" de-lag
//  (double_MA = 2·MA − MA∘MA). Mathematical methods, not copyrightable. No third-party code, variable
//  names, or structure were copied; the "Au" source was read ONLY to identify the method (Double TMA).
//
//  MATH:  m   = (P+1)/2  (integer sub-window)
//         TMA(z) = SMA( SMA(z, m), m )          (triangular weighting)
//         t1  = TMA(input);  t2 = TMA(t1)
//         Value = 2·t1 − t2                      (de-lagged double TMA)
//
//  ASSUMPTIONS:
//    • TMA is implemented as the canonical double-SMA with sub-window m = (P+1)/2. Different platforms round
//      the even-P sub-window slightly differently (some use ceil(P/2) vs floor(P/2)+1); (P+1)/2 is the
//      common, symmetric choice and reproduces the intended triangular smoothing. Choice noted here.
//    • Warm-up uses shrinking windows (min(CurrentBar+1, m)) so the line is defined from bar 0.
//    • Intermediate SMA passes are held in private Series so each pass smooths the actual running prior pass.
//
//  CHANGELOG
//    v1.0.0 (2026-07-12) — clean-room Double TMA (de-lag) + Sentinel plumbing (naming law, glass card, label remover).
// ═════════════════════════════════════════════════════════════════════════════
namespace NinjaTrader.NinjaScript.Indicators.Sentinel.Smoothers
{
    public class SentinelDTMA_v1_0_0 : Indicator
    {
        private SentinelSkin.Painter _sp;
        // cached for the card (data thread) — OnRender must never touch Value[]
        private double _cardVal;
        private int    _cardSlope;
        private bool   _cardHasData;
        private Series<double> _i1;   // SMA(input, m)
        private Series<double> _t1;   // TMA(input)      = SMA(_i1, m)
        private Series<double> _i2;   // SMA(_t1, m)
        private Series<double> _t2;   // TMA(_t1)        = SMA(_i2, m)

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description              = "Sentinel smoother library — de-lagged Double Triangular Moving Average (clean-room): y = 2·TMA − TMA(TMA), TMA = SMA∘SMA. A building block, not a Council voter (no State seam).";
                Name                     = "Sentinel DTMA v1.0.0";
                Calculate                = Calculate.OnPriceChange;
                IsOverlay                = true;
                IsSuspendedWhileInactive = true;
                DrawOnPricePanel         = true;

                Period                   = 14;

                ShowCard           = true;
                CardCorner         = SentinelCardCorner.TopRight;
                ShowIndicatorLabel = false;

                AddPlot(new Stroke(Brushes.Orange, 2), PlotStyle.Line, "DTMA");
            }
            else if (State == State.DataLoaded)
            {
                if (!ShowIndicatorLabel) Name = string.Empty;   // Sentinel label remover
                _i1 = new Series<double>(this);
                _t1 = new Series<double>(this);
                _i2 = new Series<double>(this);
                _t2 = new Series<double>(this);
            }
            else if (State == State.Terminated)
            {
                if (_sp != null) { try { _sp.Dispose(); } catch { } _sp = null; }
                try { SentinelSkin.CardLayout.Release(this); } catch { }
            }
        }

        // Trailing arithmetic mean of the last min(CurrentBar+1, len) values of src.
        private double Sma(ISeries<double> src, int len)
        {
            int n = Math.Min(CurrentBar + 1, len);
            double sum = 0.0;
            for (int i = 0; i < n; i++) sum += src[i];
            return sum / n;
        }

        protected override void OnBarUpdate()
        {
            int m = Math.Max(1, (Period + 1) / 2);   // triangular sub-window

            _i1[0] = Sma(Input, m);   // inner SMA of price
            _t1[0] = Sma(_i1, m);     // TMA(input)
            _i2[0] = Sma(_t1, m);     // inner SMA of TMA(input)
            _t2[0] = Sma(_i2, m);     // TMA(TMA(input))

            Value[0] = 2.0 * _t1[0] - _t2[0];   // de-lagged double TMA
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
            _sp.Text("DTMA", r.Left + 16f, r.Top, r.Width - 70f, 16f, SentinelSkin.CInk, 11f, true);
            _sp.Pill("p" + Period, r.Right, r.Top - 1f, SentinelSkin.CMute);

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
        public Series<double> DTMA => Values[0];

        #region Properties
        [Range(1, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Period", Description = "Triangular-MA period (sub-window m = (P+1)/2).", Order = 1, GroupName = "Parameters")]
        public int Period { get; set; }

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
		private Sentinel.Smoothers.SentinelDTMA_v1_0_0[] cacheSentinelDTMA_v1_0_0;
		public Sentinel.Smoothers.SentinelDTMA_v1_0_0 SentinelDTMA_v1_0_0(int period, bool showCard, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return SentinelDTMA_v1_0_0(Input, period, showCard, cardCorner, showIndicatorLabel);
		}

		public Sentinel.Smoothers.SentinelDTMA_v1_0_0 SentinelDTMA_v1_0_0(ISeries<double> input, int period, bool showCard, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			if (cacheSentinelDTMA_v1_0_0 != null)
				for (int idx = 0; idx < cacheSentinelDTMA_v1_0_0.Length; idx++)
					if (cacheSentinelDTMA_v1_0_0[idx] != null && cacheSentinelDTMA_v1_0_0[idx].Period == period && cacheSentinelDTMA_v1_0_0[idx].ShowCard == showCard && cacheSentinelDTMA_v1_0_0[idx].CardCorner == cardCorner && cacheSentinelDTMA_v1_0_0[idx].ShowIndicatorLabel == showIndicatorLabel && cacheSentinelDTMA_v1_0_0[idx].EqualsInput(input))
						return cacheSentinelDTMA_v1_0_0[idx];
			return CacheIndicator<Sentinel.Smoothers.SentinelDTMA_v1_0_0>(new Sentinel.Smoothers.SentinelDTMA_v1_0_0(){ Period = period, ShowCard = showCard, CardCorner = cardCorner, ShowIndicatorLabel = showIndicatorLabel }, input, ref cacheSentinelDTMA_v1_0_0);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.Sentinel.Smoothers.SentinelDTMA_v1_0_0 SentinelDTMA_v1_0_0(int period, bool showCard, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return indicator.SentinelDTMA_v1_0_0(Input, period, showCard, cardCorner, showIndicatorLabel);
		}

		public Indicators.Sentinel.Smoothers.SentinelDTMA_v1_0_0 SentinelDTMA_v1_0_0(ISeries<double> input , int period, bool showCard, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return indicator.SentinelDTMA_v1_0_0(input, period, showCard, cardCorner, showIndicatorLabel);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.Sentinel.Smoothers.SentinelDTMA_v1_0_0 SentinelDTMA_v1_0_0(int period, bool showCard, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return indicator.SentinelDTMA_v1_0_0(Input, period, showCard, cardCorner, showIndicatorLabel);
		}

		public Indicators.Sentinel.Smoothers.SentinelDTMA_v1_0_0 SentinelDTMA_v1_0_0(ISeries<double> input , int period, bool showCard, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return indicator.SentinelDTMA_v1_0_0(input, period, showCard, cardCorner, showIndicatorLabel);
		}
	}
}

#endregion
