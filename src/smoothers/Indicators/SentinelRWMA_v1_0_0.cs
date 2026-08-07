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
//  Sentinel RWMA — Range-Weighted Moving Average (Sentinel smoother building block)   |   Version v1.0.0
//  File: SentinelRWMA_v1_0_0.cs  |  namespace …Indicators.Sentinel.Smoothers  |  display "Sentinel RWMA"
//
//  ⚠ NO ORDERS · NO STATE SEAM — a read-only SMOOTHER building block, not a Council voter. It draws the
//  range-weighted average + a Sentinel glass card and publishes nothing (a moving average has no verdict).
//
//  PROVENANCE / LICENSE: CLEAN-ROOM. Algorithm identified from a GPL LizardIndicators source
//  (amaRWMA.cs, GPL-3.0) but NO GPL code was used — reimplemented from the general, publicly-documented
//  RANGE-WEIGHTED moving-average method: each bar contributes to the average in proportion to a function of
//  its high-low range, so wide (high-conviction) bars pull the average more than narrow ones. Weighting a
//  moving average by bar range/volatility is a public mathematical method, not copyrightable. No third-party
//  code/variable-names/structure were copied.
//
//    CANONICAL RANGE-WEIGHTED MA over a window of n = min(CurrentBar+1, Period) bars:
//        rᵢ    = (High[i] - Low[i]) / TickSize          (bar range measured in ticks)
//        wᵢ    = (1 + rᵢ)²                              (+1 keeps zero-range bars non-zero; squared emphasises range)
//        Value = Σ (wᵢ · Input[i]) / Σ wᵢ
//
//  ASSUMPTIONS: (1) Weight = (1 + tickRange)² — the +1 avoids a zero weight on doji/zero-range bars and the
//  square emphasises wide bars; this matches the confirmed source weighting and is the natural closed form of
//  the public method. (2) Requires price data (uses High/Low); on non-price input the range is undefined, so
//  the indicator falls back to the raw input for that bar. (3) Window shrinks during warm-up and stays
//  normalised by its own weight sum.
//
//  CHANGELOG
//    v1.0.0 (2026-07-12) — initial: clean-room range-weighted MA + Sentinel naming law, glass card, label remover.
// ═════════════════════════════════════════════════════════════════════════════
namespace NinjaTrader.NinjaScript.Indicators.Sentinel.Smoothers
{
    public class SentinelRWMA_v1_0_0 : Indicator
    {
        private SentinelSkin.Painter _sp;
        // cached for the card (data thread) — OnRender must never touch Value[]
        private double _cardVal;
        private int    _cardSlope;
        private bool   _cardHasData;
        private bool _isPrice;   // true when Input carries High/Low (price data)

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description              = "Sentinel smoother library — Range-Weighted Moving Average (clean-room). Each bar is weighted by (1 + tickRange)² so wide bars pull the average more. Draws the line + a Sentinel glass card. A building block, not a Council voter (no State seam).";
                Name                     = "Sentinel RWMA v1.0.0";
                Calculate                = Calculate.OnPriceChange;
                IsOverlay                = true;
                IsSuspendedWhileInactive = true;
                DrawOnPricePanel         = true;

                Period                   = 14;

                ShowCard           = true;
                CardCorner         = SentinelCardCorner.TopRight;
                ShowIndicatorLabel = false;

                AddPlot(new Stroke(Brushes.SkyBlue, 2), PlotStyle.Line, "RWMA");
            }
            else if (State == State.DataLoaded)
            {
                if (!ShowIndicatorLabel) Name = string.Empty;   // Sentinel label remover
                _isPrice = Input is PriceSeries;                // range-weighting needs bar High/Low
            }
            else if (State == State.Terminated)
            {
                if (_sp != null) { try { _sp.Dispose(); } catch (Exception _sx) { SentinelCore.Swallow("SentinelRWMA.OnStateChange", _sx); } _sp = null; }
                try { SentinelSkin.CardLayout.Release(this); } catch (Exception _sx) { SentinelCore.Swallow("SentinelRWMA.OnStateChange", _sx); }
            }
        }

        protected override void OnBarUpdate()
        {
            int n = Math.Min(CurrentBar + 1, Period);

            // Non-price input has no High/Low range → fall back to the raw input (unweighted).
            if (!_isPrice)
            {
                double s = 0.0;
                for (int i = 0; i < n; i++) s += Input[i];
                Value[0] = s / n;
                return;
            }

            double num = 0.0, den = 0.0;
            for (int i = 0; i < n; i++)
            {
                double rangeTicks = (High[i] - Low[i]) / TickSize;
                double w = 1.0 + rangeTicks;
                w *= w;                        // (1 + tickRange)²
                num += w * Input[i];
                den += w;
            }
            Value[0] = den > 0.0 ? num / den : Input[0];
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
            try { RenderCard(); } catch (Exception _sx) { SentinelCore.Swallow("SentinelRWMA.OnRender", _sx); }
            try { _sp.End(); } catch (Exception _sx) { SentinelCore.Swallow("SentinelRWMA.OnRender", _sx); }   // ALWAYS runs — a skipped End() would silently kill the card
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
            _sp.Text("RWMA", r.Left + 16f, r.Top, r.Width - 70f, 16f, SentinelSkin.CInk, 11f, true);
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
        public Series<double> RWMA => Values[0];

        #region Properties
        [Range(1, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Period", Description = "Number of bars in the range-weighted window.", Order = 1, GroupName = "Parameters")]
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
