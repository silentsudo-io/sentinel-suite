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
//  Sentinel DWMA — Double Weighted Moving Average (Sentinel smoother block)   |   Version v1.0.0
//  File: SentinelDWMA_v1_0_0.cs  |  namespace …Indicators.Sentinel.Smoothers  |  display "Sentinel DWMA"
//
//  ⚠ NO ORDERS · NO STATE SEAM — a read-only SMOOTHER building block, not a Council voter. It draws a
//  smoothed line + a Sentinel glass card; it publishes nothing (a moving average has no verdict).
//
//  PROVENANCE / LICENSE: CLEAN-ROOM. Written from the public double-cascade lag-reduction formula (the
//  DEMA construction applied to a Weighted MA) — a mathematical method, not copyrightable. No third-party
//  code, names, or structure copied. (Sentinel port of the "Au" MA pack; the Au code was NOT copied.)
//
//  ALGORITHM (Double WMA — DEMA form over WMA, confirmed from source):
//    w1 = WMA(price, Period)
//    w2 = WMA(w1,    Period)
//    Value = 2·w1 − w2
//  WMA weights are linear (most-recent input weight = k, oldest = 1; denom = k(k+1)/2), over the available
//  window k = min(CurrentBar+1, Period). An intermediate Series holds w1 so w2 = WMA(WMA(price)).
//
//  CHANGELOG
//    v1.0.0 (2026-07-12) — clean-room Double Weighted MA (2·WMA − WMA(WMA)) + Sentinel plumbing
//             (naming law, glass card, label remover).
// ═════════════════════════════════════════════════════════════════════════════
namespace NinjaTrader.NinjaScript.Indicators.Sentinel.Smoothers
{
    public class SentinelDWMA_v1_0_0 : Indicator
    {
        private SentinelSkin.Painter _sp;
        // cached for the card (data thread) — OnRender must never touch Value[]
        private double _cardVal;
        private int    _cardSlope;
        private bool   _cardHasData;
        private Series<double> _w1;   // WMA(price) — kept so w2 = WMA(w1)

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description              = "Sentinel smoother library — Double Weighted Moving Average (clean-room, 2·WMA − WMA(WMA)). Draws a lag-reduced smoothed line + a Sentinel glass card. A building block, not a Council voter (no State seam).";
                Name                     = "Sentinel DWMA v1.0.0";
                Calculate                = Calculate.OnPriceChange;
                IsOverlay                = true;
                IsSuspendedWhileInactive = true;
                DrawOnPricePanel         = true;

                Period                   = 14;

                ShowCard           = true;
                CardCorner         = SentinelCardCorner.TopRight;
                ShowIndicatorLabel = false;

                AddPlot(new Stroke(Brushes.Orange, 2), PlotStyle.Line, "DWMA");
            }
            else if (State == State.Configure)
            {
                _w1 = new Series<double>(this);
            }
            else if (State == State.DataLoaded)
            {
                if (!ShowIndicatorLabel) Name = string.Empty;   // Sentinel label remover
            }
            else if (State == State.Terminated)
            {
                if (_sp != null) { try { _sp.Dispose(); } catch { } _sp = null; }
                try { SentinelSkin.CardLayout.Release(this); } catch { }
            }
        }

        // Clean-room linear WMA of the current bar of a series, over the available window.
        private double Wma(ISeries<double> src, int period)
        {
            int k = Math.Min(CurrentBar + 1, period);
            double num = 0.0;
            double den = 0.0;
            for (int i = 0; i < k; i++)
            {
                double w = k - i;          // most recent (i=0) gets the largest weight
                num += w * src[i];
                den += w;
            }
            return den > 0.0 ? num / den : src[0];
        }

        protected override void OnBarUpdate()
        {
            // Clean-room Double WMA: 2·WMA − WMA(WMA), with an intermediate WMA series.
            _w1[0] = Wma(Input, Period);
            double w2 = Wma(_w1, Period);
            Value[0] = 2.0 * _w1[0] - w2;
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
            _sp.Text("DWMA", r.Left + 16f, r.Top, r.Width - 70f, 16f, SentinelSkin.CInk, 11f, true);
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
        public Series<double> DWMA => Values[0];

        #region Properties
        [Range(1, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Period", Description = "Length of each WMA stage.", Order = 1, GroupName = "Parameters")]
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
		private Sentinel.Smoothers.SentinelDWMA_v1_0_0[] cacheSentinelDWMA_v1_0_0;
		public Sentinel.Smoothers.SentinelDWMA_v1_0_0 SentinelDWMA_v1_0_0(int period, bool showCard, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return SentinelDWMA_v1_0_0(Input, period, showCard, cardCorner, showIndicatorLabel);
		}

		public Sentinel.Smoothers.SentinelDWMA_v1_0_0 SentinelDWMA_v1_0_0(ISeries<double> input, int period, bool showCard, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			if (cacheSentinelDWMA_v1_0_0 != null)
				for (int idx = 0; idx < cacheSentinelDWMA_v1_0_0.Length; idx++)
					if (cacheSentinelDWMA_v1_0_0[idx] != null && cacheSentinelDWMA_v1_0_0[idx].Period == period && cacheSentinelDWMA_v1_0_0[idx].ShowCard == showCard && cacheSentinelDWMA_v1_0_0[idx].CardCorner == cardCorner && cacheSentinelDWMA_v1_0_0[idx].ShowIndicatorLabel == showIndicatorLabel && cacheSentinelDWMA_v1_0_0[idx].EqualsInput(input))
						return cacheSentinelDWMA_v1_0_0[idx];
			return CacheIndicator<Sentinel.Smoothers.SentinelDWMA_v1_0_0>(new Sentinel.Smoothers.SentinelDWMA_v1_0_0(){ Period = period, ShowCard = showCard, CardCorner = cardCorner, ShowIndicatorLabel = showIndicatorLabel }, input, ref cacheSentinelDWMA_v1_0_0);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.Sentinel.Smoothers.SentinelDWMA_v1_0_0 SentinelDWMA_v1_0_0(int period, bool showCard, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return indicator.SentinelDWMA_v1_0_0(Input, period, showCard, cardCorner, showIndicatorLabel);
		}

		public Indicators.Sentinel.Smoothers.SentinelDWMA_v1_0_0 SentinelDWMA_v1_0_0(ISeries<double> input , int period, bool showCard, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return indicator.SentinelDWMA_v1_0_0(input, period, showCard, cardCorner, showIndicatorLabel);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.Sentinel.Smoothers.SentinelDWMA_v1_0_0 SentinelDWMA_v1_0_0(int period, bool showCard, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return indicator.SentinelDWMA_v1_0_0(Input, period, showCard, cardCorner, showIndicatorLabel);
		}

		public Indicators.Sentinel.Smoothers.SentinelDWMA_v1_0_0 SentinelDWMA_v1_0_0(ISeries<double> input , int period, bool showCard, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return indicator.SentinelDWMA_v1_0_0(input, period, showCard, cardCorner, showIndicatorLabel);
		}
	}
}

#endregion
