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
//  Sentinel TMA — Triangular Moving Average (Sentinel smoother building block)   |   Version v1.0.0
//  File: SentinelTMA_v1_0_0.cs  |  namespace …Indicators.Sentinel.Smoothers  |  display "Sentinel TMA"
//
//  ⚠ NO ORDERS · NO STATE SEAM — a read-only SMOOTHER building block, not a Council voter. It draws a
//  smoothed line + a Sentinel glass card; it publishes nothing (a moving average has no verdict). It is a
//  baseline the signal tools (Sentinel SuperTrend) can consume, and a Sentinel-branded MA in its own right.
//
//  PROVENANCE / LICENSE: CLEAN-ROOM. Written from the public Triangular Moving Average formula
//  (a double-smoothed SMA that weights the middle of the window most): TMA = SMA( SMA(price, p1), p2 ).
//  Window split — even N: p1 = N/2, p2 = N/2 + 1; odd N: p1 = p2 = (N+1)/2. A mathematical method,
//  not copyrightable. No third-party code, variable names, or structure were copied. (Sentinel port of
//  the "Au" MA/filter pack; NOT copied. See repo NOTICE.)
//
//  CHANGELOG
//    v1.0.0 (2026-07-12) — clean-room Triangular Moving Average + Sentinel plumbing (naming law, glass card, label remover).
// ═════════════════════════════════════════════════════════════════════════════
namespace NinjaTrader.NinjaScript.Indicators.Sentinel.Smoothers
{
    public class SentinelTMA_v1_0_0 : Indicator
    {
        private SentinelSkin.Painter _sp;
        // cached for the card (data thread) — OnRender must never touch Value[]
        private double _cardVal;
        private int    _cardSlope;
        private bool   _cardHasData;
        private Series<double>       _inner;   // first-pass SMA(price, p1)
        private int                  _p1;
        private int                  _p2;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description              = "Sentinel smoother library — Triangular Moving Average (clean-room). A double-smoothed SMA that weights the middle of the window most = SMA(SMA(price, p1), p2). Draws a smoothed line + a Sentinel glass card. A building block, not a Council voter (no State seam).";
                Name                     = "Sentinel TMA v1.0.0";
                Calculate                = Calculate.OnPriceChange;
                IsOverlay                = true;
                IsSuspendedWhileInactive = true;
                DrawOnPricePanel         = true;

                Period                   = 15;

                ShowCard           = true;
                CardCorner         = SentinelCardCorner.TopRight;
                ShowIndicatorLabel = false;

                AddPlot(new Stroke(Brushes.SkyBlue, 2), PlotStyle.Line, "TMA");
            }
            else if (State == State.Configure)
            {
                _inner = new Series<double>(this);
                if ((Period & 1) == 0)     // even
                {
                    _p1 = Math.Max(1, Period / 2);
                    _p2 = _p1 + 1;
                }
                else                        // odd
                {
                    _p1 = Math.Max(1, (Period + 1) / 2);
                    _p2 = _p1;
                }
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

        // Simple moving average over a series, clamped to the available history during warmup.
        private double Sma(ISeries<double> src, int len)
        {
            int n = Math.Min(len, CurrentBar + 1);
            if (n < 1) return src[0];
            double sum = 0.0;
            for (int i = 0; i < n; i++) sum += src[i];
            return sum / n;
        }

        protected override void OnBarUpdate()
        {
            // Clean-room Triangular MA: SMA of an SMA (double smoothing).
            _inner[0] = Sma(Input, _p1);
            Value[0]  = Sma(_inner, _p2);
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
            _sp.Text("TMA", r.Left + 16f, r.Top, r.Width - 70f, 16f, SentinelSkin.CInk, 11f, true);
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
        public Series<double> TMA => Values[0];

        #region Properties
        [Range(1, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Period", Description = "Triangular MA lookback (N); split into two SMA passes p1/p2.", Order = 1, GroupName = "Parameters")]
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
		private Sentinel.Smoothers.SentinelTMA_v1_0_0[] cacheSentinelTMA_v1_0_0;
		public Sentinel.Smoothers.SentinelTMA_v1_0_0 SentinelTMA_v1_0_0(int period, bool showCard, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return SentinelTMA_v1_0_0(Input, period, showCard, cardCorner, showIndicatorLabel);
		}

		public Sentinel.Smoothers.SentinelTMA_v1_0_0 SentinelTMA_v1_0_0(ISeries<double> input, int period, bool showCard, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			if (cacheSentinelTMA_v1_0_0 != null)
				for (int idx = 0; idx < cacheSentinelTMA_v1_0_0.Length; idx++)
					if (cacheSentinelTMA_v1_0_0[idx] != null && cacheSentinelTMA_v1_0_0[idx].Period == period && cacheSentinelTMA_v1_0_0[idx].ShowCard == showCard && cacheSentinelTMA_v1_0_0[idx].CardCorner == cardCorner && cacheSentinelTMA_v1_0_0[idx].ShowIndicatorLabel == showIndicatorLabel && cacheSentinelTMA_v1_0_0[idx].EqualsInput(input))
						return cacheSentinelTMA_v1_0_0[idx];
			return CacheIndicator<Sentinel.Smoothers.SentinelTMA_v1_0_0>(new Sentinel.Smoothers.SentinelTMA_v1_0_0(){ Period = period, ShowCard = showCard, CardCorner = cardCorner, ShowIndicatorLabel = showIndicatorLabel }, input, ref cacheSentinelTMA_v1_0_0);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.Sentinel.Smoothers.SentinelTMA_v1_0_0 SentinelTMA_v1_0_0(int period, bool showCard, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return indicator.SentinelTMA_v1_0_0(Input, period, showCard, cardCorner, showIndicatorLabel);
		}

		public Indicators.Sentinel.Smoothers.SentinelTMA_v1_0_0 SentinelTMA_v1_0_0(ISeries<double> input , int period, bool showCard, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return indicator.SentinelTMA_v1_0_0(input, period, showCard, cardCorner, showIndicatorLabel);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.Sentinel.Smoothers.SentinelTMA_v1_0_0 SentinelTMA_v1_0_0(int period, bool showCard, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return indicator.SentinelTMA_v1_0_0(Input, period, showCard, cardCorner, showIndicatorLabel);
		}

		public Indicators.Sentinel.Smoothers.SentinelTMA_v1_0_0 SentinelTMA_v1_0_0(ISeries<double> input , int period, bool showCard, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return indicator.SentinelTMA_v1_0_0(input, period, showCard, cardCorner, showIndicatorLabel);
		}
	}
}

#endregion
