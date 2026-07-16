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
//  Sentinel HoltEMA — Holt double-exponential smoothing (Sentinel smoother block) |  Version v1.0.0
//  File: SentinelHoltEMA_v1_0_0.cs  |  namespace …Indicators.Sentinel.Smoothers  |  display "Sentinel HoltEMA"
//
//  ⚠ NO ORDERS · NO STATE SEAM — a read-only SMOOTHER building block, not a Council voter. It draws a
//  smoothed line + a Sentinel glass card; it publishes nothing (a moving average has no verdict).
//
//  PROVENANCE / LICENSE: CLEAN-ROOM. Written from the public Holt linear (double-exponential) smoothing
//  formula — a textbook forecasting method, not copyrightable. No third-party code, names, or structure
//  copied. (Sentinel smoother-library port of the "Au" MA pack; the Au code was NOT copied. See repo NOTICE.)
//
//  ALGORITHM (Holt, two smoothing constants derived from periods):
//    alpha = 2 / (1 + Period)          (level smoothing)
//    gamma = 2 / (1 + TrendPeriod)     (trend smoothing)
//    L[t]  = alpha·x[t] + (1 − alpha)·(L[t−1] + T[t−1])     (level)
//    T[t]  = gamma·(L[t] − L[t−1]) + (1 − gamma)·T[t−1]     (trend)
//    Value = L[t]
//
//  ASSUMPTION (noted per clean-room rule): the "AuHoltEMA" source PLOTS THE LEVEL L (its HoltEMA[0] = L),
//  NOT the one-step-ahead Holt forecast L+T. This port MATCHES THE SOURCE and outputs the level L (the
//  trend component T is still computed and used inside the recursion). The classic Holt forecast would be
//  L+T; use that if a forecast line is wanted instead of the smoothed level.
//
//  CHANGELOG
//    v1.0.0 (2026-07-12) — clean-room Holt double-exponential smoothing + Sentinel plumbing (naming law,
//             glass card, label remover). Output = level L to match source (see ASSUMPTION above).
// ═════════════════════════════════════════════════════════════════════════════
namespace NinjaTrader.NinjaScript.Indicators.Sentinel.Smoothers
{
    public class SentinelHoltEMA_v1_0_0 : Indicator
    {
        private SentinelSkin.Painter _sp;
        private Series<double> _trend;   // Holt trend component T
        // cached for the card (computed on the data thread; read on the render thread — never touch Value[] in OnRender)
        private double _cardVal;
        private int    _cardSlope;
        private bool   _cardHasData;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description              = "Sentinel smoother library — Holt double-exponential smoothing (clean-room, level + trend). Draws the smoothed level L + a Sentinel glass card. A building block, not a Council voter (no State seam).";
                Name                     = "Sentinel HoltEMA v1.0.0";
                Calculate                = Calculate.OnPriceChange;
                IsOverlay                = true;
                IsSuspendedWhileInactive = true;
                DrawOnPricePanel         = true;

                Period                   = 89;
                TrendPeriod              = 144;

                ShowCard           = true;
                CardCorner         = SentinelCardCorner.TopRight;
                ShowIndicatorLabel = false;

                AddPlot(new Stroke(Brushes.DodgerBlue, 2), PlotStyle.Line, "HoltEMA");
            }
            else if (State == State.Configure)
            {
                _trend = new Series<double>(this);
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
            // Clean-room Holt double-exponential smoothing. Output = level L (matches source).
            double alpha = 2.0 / (1.0 + Period);
            double gamma = 2.0 / (1.0 + TrendPeriod);

            if (CurrentBar < 1)
            {
                Value[0]  = Input[0];
                _trend[0] = 0.0;
            }
            else
            {
                double level = alpha * Input[0] + (1.0 - alpha) * (Value[1] + _trend[1]);
                Value[0]  = level;
                _trend[0] = gamma * (level - Value[1]) + (1.0 - gamma) * _trend[1];
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
            _sp.Text("HoltEMA", r.Left + 16f, r.Top, r.Width - 70f, 16f, SentinelSkin.CInk, 11f, true);
            _sp.Pill(Period + "/" + TrendPeriod, r.Right, r.Top - 1f, SentinelSkin.CMute);   // level/trend

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
        public Series<double> HoltEMA => Values[0];

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> HoltTrend => _trend;

        #region Properties
        [Range(1, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Period", Description = "Level smoothing period (alpha = 2 / (1 + Period)).", Order = 1, GroupName = "Parameters")]
        public int Period { get; set; }

        [Range(1, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Trend Period", Description = "Trend smoothing period (gamma = 2 / (1 + Trend Period)).", Order = 2, GroupName = "Parameters")]
        public int TrendPeriod { get; set; }

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
