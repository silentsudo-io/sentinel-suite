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
//  Sentinel ZeroLagHATEMA — Zero-Lag TEMA on Heikin-Ashi price (Sentinel smoother building block)   |   Version v1.0.0
//  File: SentinelZeroLagHATEMA_v1_0_0.cs  |  namespace …Indicators.Sentinel.Smoothers  |  display "Sentinel ZeroLagHATEMA"
//
//  ⚠ NO ORDERS · NO STATE SEAM — a read-only SMOOTHER building block, not a Council voter. It draws a
//  smoothed line + a Sentinel glass card; it publishes nothing.
//
//  PROVENANCE / LICENSE: CLEAN-ROOM. Reimplemented from the public Heikin-Ashi transform + TEMA (Patrick
//  Mulloy) + zero-lag error-correction construction — mathematical methods, not copyrightable. NO third-party
//  code, variable names, or structure were copied; the "Au" filter pack was read ONLY to identify the variant.
//  Method:
//    Heikin-Ashi close:  haClose = (Open + High + Low + Close) / 4
//    Heikin-Ashi open:   haOpen  = (haOpen[1] + haClose[1]) / 2      (seeded (Open+Close)/2 on bar 0)
//    Then the same zero-lag TEMA as SentinelZeroLagTEMA, applied to the haClose series:
//      α = 2/(Period+1) ; tema = 3·EMA1 − 3·EMA2 + EMA3 of haClose ; then TEMA the tema →
//      zl = tema + (tema − tema_of_tema)  ==  2·tema − tema_of_tema
//
//  ASSUMPTIONS / NOTES:
//    • The "Au" source used a NON-canonical HA close (a 4-component smoothed haClose incorporating haOpen and
//      Max/Min guards) and derived haOpen from the PRIOR bar's OHLC average. This clean-room build uses the
//      CANONICAL Heikin-Ashi definitions above (haClose=(O+H+L+C)/4, haOpen=(haOpen[1]+haClose[1])/2) per the
//      specified form — HA values therefore differ slightly from the Au variant by design.
//    • The smoother is applied to the HA CLOSE series (haOpen is computed for HA correctness but not smoothed).
//    • Operates on the chart's OHLC price bars (not an arbitrary Input series).
//    • EMAs are seeded with haClose on bar 0.
//
//  CHANGELOG
//    v1.0.0 (2026-07-12) — clean-room Zero-Lag TEMA on canonical Heikin-Ashi close + Sentinel plumbing (naming law, glass card, label remover).
// ═════════════════════════════════════════════════════════════════════════════
namespace NinjaTrader.NinjaScript.Indicators.Sentinel.Smoothers
{
    public class SentinelZeroLagHATEMA_v1_0_0 : Indicator
    {
        private SentinelSkin.Painter _sp;
        // cached for the card (data thread) — OnRender must never touch Value[]
        private double _cardVal;
        private int    _cardSlope;
        private bool   _cardHasData;

        // Heikin-Ashi series + intermediate EMA / TEMA chains
        private Series<double> _haC, _haO;
        private Series<double> _e1, _e2, _e3;   // EMA cascade of haClose → tema
        private Series<double> _t;              // tema series
        private Series<double> _f1, _f2, _f3;   // EMA cascade of tema → tema-of-tema

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description              = "Sentinel smoother library — Zero-Lag TEMA computed on canonical Heikin-Ashi close (clean-room). A building block, not a Council voter (no State seam).";
                Name                     = "Sentinel ZeroLagHATEMA v1.0.0";
                Calculate                = Calculate.OnPriceChange;
                IsOverlay                = true;
                IsSuspendedWhileInactive = true;
                DrawOnPricePanel         = true;

                Period                   = 14;

                ShowCard           = true;
                CardCorner         = SentinelCardCorner.TopRight;
                ShowIndicatorLabel = false;

                AddPlot(new Stroke(Brushes.DeepSkyBlue, 2), PlotStyle.Line, "ZeroLagHATEMA");
            }
            else if (State == State.Configure)
            {
                _haC = new Series<double>(this);
                _haO = new Series<double>(this);
                _e1  = new Series<double>(this);
                _e2  = new Series<double>(this);
                _e3  = new Series<double>(this);
                _t   = new Series<double>(this);
                _f1  = new Series<double>(this);
                _f2  = new Series<double>(this);
                _f3  = new Series<double>(this);
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

        protected override void OnBarUpdate()
        {
            double a = 2.0 / (Period + 1);

            // canonical Heikin-Ashi
            _haC[0] = (Open[0] + High[0] + Low[0] + Close[0]) / 4.0;

            if (CurrentBar == 0)
            {
                _haO[0] = (Open[0] + Close[0]) / 2.0;   // HA-open seed
                double s = _haC[0];
                _e1[0] = _e2[0] = _e3[0] = s;
                _t[0]  = s;
                _f1[0] = _f2[0] = _f3[0] = s;
                Value[0] = s;
                return;
            }

            _haO[0] = (_haO[1] + _haC[1]) / 2.0;

            // first TEMA — EMA cascade over the HA close
            _e1[0] = a * _haC[0] + (1 - a) * _e1[1];
            _e2[0] = a * _e1[0]  + (1 - a) * _e2[1];
            _e3[0] = a * _e2[0]  + (1 - a) * _e3[1];
            double tema = 3.0 * _e1[0] - 3.0 * _e2[0] + _e3[0];
            _t[0] = tema;

            // second TEMA — EMA cascade over the tema series
            _f1[0] = a * _t[0]  + (1 - a) * _f1[1];
            _f2[0] = a * _f1[0] + (1 - a) * _f2[1];
            _f3[0] = a * _f2[0] + (1 - a) * _f3[1];
            double temaOfTema = 3.0 * _f1[0] - 3.0 * _f2[0] + _f3[0];

            // zero-lag: echo the residual lag back onto tema
            Value[0] = 2.0 * tema - temaOfTema;
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
            _sp.Text("ZL-HATEMA", r.Left + 16f, r.Top, r.Width - 70f, 16f, SentinelSkin.CInk, 11f, true);
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
        public Series<double> ZeroLagHATEMA => Values[0];

        #region Properties
        [Range(1, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Period", Description = "EMA period of each stage in the TEMA cascade (applied to Heikin-Ashi close).", Order = 1, GroupName = "Parameters")]
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
		private Sentinel.Smoothers.SentinelZeroLagHATEMA_v1_0_0[] cacheSentinelZeroLagHATEMA_v1_0_0;
		public Sentinel.Smoothers.SentinelZeroLagHATEMA_v1_0_0 SentinelZeroLagHATEMA_v1_0_0(int period, bool showCard, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return SentinelZeroLagHATEMA_v1_0_0(Input, period, showCard, cardCorner, showIndicatorLabel);
		}

		public Sentinel.Smoothers.SentinelZeroLagHATEMA_v1_0_0 SentinelZeroLagHATEMA_v1_0_0(ISeries<double> input, int period, bool showCard, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			if (cacheSentinelZeroLagHATEMA_v1_0_0 != null)
				for (int idx = 0; idx < cacheSentinelZeroLagHATEMA_v1_0_0.Length; idx++)
					if (cacheSentinelZeroLagHATEMA_v1_0_0[idx] != null && cacheSentinelZeroLagHATEMA_v1_0_0[idx].Period == period && cacheSentinelZeroLagHATEMA_v1_0_0[idx].ShowCard == showCard && cacheSentinelZeroLagHATEMA_v1_0_0[idx].CardCorner == cardCorner && cacheSentinelZeroLagHATEMA_v1_0_0[idx].ShowIndicatorLabel == showIndicatorLabel && cacheSentinelZeroLagHATEMA_v1_0_0[idx].EqualsInput(input))
						return cacheSentinelZeroLagHATEMA_v1_0_0[idx];
			return CacheIndicator<Sentinel.Smoothers.SentinelZeroLagHATEMA_v1_0_0>(new Sentinel.Smoothers.SentinelZeroLagHATEMA_v1_0_0(){ Period = period, ShowCard = showCard, CardCorner = cardCorner, ShowIndicatorLabel = showIndicatorLabel }, input, ref cacheSentinelZeroLagHATEMA_v1_0_0);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.Sentinel.Smoothers.SentinelZeroLagHATEMA_v1_0_0 SentinelZeroLagHATEMA_v1_0_0(int period, bool showCard, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return indicator.SentinelZeroLagHATEMA_v1_0_0(Input, period, showCard, cardCorner, showIndicatorLabel);
		}

		public Indicators.Sentinel.Smoothers.SentinelZeroLagHATEMA_v1_0_0 SentinelZeroLagHATEMA_v1_0_0(ISeries<double> input , int period, bool showCard, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return indicator.SentinelZeroLagHATEMA_v1_0_0(input, period, showCard, cardCorner, showIndicatorLabel);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.Sentinel.Smoothers.SentinelZeroLagHATEMA_v1_0_0 SentinelZeroLagHATEMA_v1_0_0(int period, bool showCard, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return indicator.SentinelZeroLagHATEMA_v1_0_0(Input, period, showCard, cardCorner, showIndicatorLabel);
		}

		public Indicators.Sentinel.Smoothers.SentinelZeroLagHATEMA_v1_0_0 SentinelZeroLagHATEMA_v1_0_0(ISeries<double> input , int period, bool showCard, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return indicator.SentinelZeroLagHATEMA_v1_0_0(input, period, showCard, cardCorner, showIndicatorLabel);
		}
	}
}

#endregion
