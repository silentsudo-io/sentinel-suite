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
//  Sentinel CoralFilter — Tillson T3 triple-smoothed EMA ("Coral" trend filter)   |   Version v1.0.0
//  File: SentinelCoralFilter_v1_0_0.cs  |  namespace …Indicators.Sentinel.Smoothers  |  display "Sentinel CoralFilter"
//
//  ⚠ NO ORDERS · NO STATE SEAM — a read-only SMOOTHER building block, not a Council voter. It draws the
//  T3-smoothed line + a Sentinel glass card and publishes nothing (a moving average has no verdict). A
//  low-lag baseline the signal tools can consume, and a Sentinel-branded T3/Coral filter in its own right.
//
//  PROVENANCE / LICENSE: CLEAN-ROOM. Algorithm identified from a GPL LizardIndicators source
//  (amaCoralFilter.cs, GPL-3.0) but NO GPL code was used — reimplemented from the canonical PUBLIC
//  Tillson T3 formula (Tim Tillson, "Better Moving Averages", TASC 1998), a mathematical method that is
//  not copyrightable. The "Coral" filter is a T3 with volume-factor d and an EMA cascade whose smoothing
//  factor derives from the period. No third-party code/variable-names/structure were copied.
//
//    CANONICAL T3 (six cascaded EMAs e1..e6, α = 2/(1+k), k = 1 + (Period-1)/2, d = coefficient):
//        e1 = α·src + (1-α)·e1₋₁   … e6 = α·e5 + (1-α)·e6₋₁
//        T3 = c1·e6 + c2·e5 + c3·e4 + c4·e3
//        c1 = -d³ ,  c2 = 3d²+3d³ ,  c3 = -(3d+6d²+3d³) ,  c4 = 1+3d+3d²+d³
//
//  ASSUMPTIONS: (1) d default = 0.4 (the common Coral/T3 volume factor); exposed as CoefficientMultiplier.
//  (2) EMA smoothing factor derived via k = 1 + (Period-1)/2 → α = 2/(1+k) (the standard T3 period mapping).
//  (3) Dropped the source's trend-classification / neutral-threshold / paint-bar / sound-alert machinery —
//  this is a pure smoother, so only Period + CoefficientMultiplier survive. (4) Early bars seed the cascade
//  to the first input.
//
//  CHANGELOG
//    v1.0.0 (2026-07-12) — initial: clean-room T3/Coral filter + Sentinel naming law, glass card, label remover.
// ═════════════════════════════════════════════════════════════════════════════
namespace NinjaTrader.NinjaScript.Indicators.Sentinel.Smoothers
{
    public class SentinelCoralFilter_v1_0_0 : Indicator
    {
        private SentinelSkin.Painter _sp;
        // cached for the card (data thread) — OnRender must never touch Value[]
        private double _cardVal;
        private int    _cardSlope;
        private bool   _cardHasData;

        // T3 cascade state
        private double[] _e     = new double[6];   // current-bar cascade values e1..e6
        private double[] _ePrev = new double[6];   // committed previous-bar cascade values
        private double   _alpha, _beta;            // α and (1-α)
        private double   _c1, _c2, _c3, _c4;       // T3 combining coefficients

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description              = "Sentinel smoother library — Coral / Tillson-T3 triple-smoothed EMA (clean-room). Draws a low-lag T3 filter of the input + a Sentinel glass card. A building block, not a Council voter (no State seam).";
                Name                     = "Sentinel CoralFilter v1.0.0";
                Calculate                = Calculate.OnPriceChange;
                IsOverlay                = true;
                IsSuspendedWhileInactive = true;
                DrawOnPricePanel         = true;

                Period                   = 34;
                CoefficientMultiplier    = 0.4;

                ShowCard           = true;
                CardCorner         = SentinelCardCorner.TopRight;
                ShowIndicatorLabel = false;

                AddPlot(new Stroke(Brushes.SkyBlue, 2), PlotStyle.Line, "CoralFilter");
            }
            else if (State == State.Configure)
            {
                // Clean-room T3 intermediates (computed once; Period + CoefficientMultiplier are set by now).
                double k = 1.0 + (Period - 1.0) / 2.0;
                _alpha   = 2.0 / (1.0 + k);
                _beta    = 1.0 - _alpha;

                double d  = CoefficientMultiplier;
                double d2 = d * d;
                double d3 = d * d2;
                _c1 = -d3;
                _c2 = 3.0 * (d2 + d3);
                _c3 = -(3.0 * d + 6.0 * d2 + 3.0 * d3);
                _c4 = 1.0 + 3.0 * d + 3.0 * d2 + d3;
            }
            else if (State == State.DataLoaded)
            {
                if (!ShowIndicatorLabel) Name = string.Empty;   // Sentinel label remover
            }
            else if (State == State.Terminated)
            {
                if (_sp != null) { try { _sp.Dispose(); } catch (Exception _sx) { SentinelCore.Swallow("SentinelCoralFilter.OnStateChange", _sx); } _sp = null; }
                try { SentinelSkin.CardLayout.Release(this); } catch (Exception _sx) { SentinelCore.Swallow("SentinelCoralFilter.OnStateChange", _sx); }
            }
        }

        protected override void OnBarUpdate()
        {
            double src = Input[0];

            if (CurrentBar == 0)
            {
                for (int i = 0; i < 6; i++) { _e[i] = src; _ePrev[i] = src; }
                Value[0] = src;
                return;
            }

            // On the first tick of a new bar, freeze last bar's cascade as the recurrence base.
            if (IsFirstTickOfBar)
                Array.Copy(_e, _ePrev, 6);

            _e[0] = _alpha * src   + _beta * _ePrev[0];
            _e[1] = _alpha * _e[0] + _beta * _ePrev[1];
            _e[2] = _alpha * _e[1] + _beta * _ePrev[2];
            _e[3] = _alpha * _e[2] + _beta * _ePrev[3];
            _e[4] = _alpha * _e[3] + _beta * _ePrev[4];
            _e[5] = _alpha * _e[4] + _beta * _ePrev[5];

            Value[0] = _c1 * _e[5] + _c2 * _e[4] + _c3 * _e[3] + _c4 * _e[2];
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
            try { RenderCard(); } catch (Exception _sx) { SentinelCore.Swallow("SentinelCoralFilter.OnRender", _sx); }
            try { _sp.End(); } catch (Exception _sx) { SentinelCore.Swallow("SentinelCoralFilter.OnRender", _sx); }   // ALWAYS runs — a skipped End() would silently kill the card
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
            _sp.Text("Coral", r.Left + 16f, r.Top, r.Width - 70f, 16f, SentinelSkin.CInk, 11f, true);
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
        public Series<double> CoralFilter => Values[0];

        #region Properties
        [Range(1, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Period", Description = "Lookback that sets the T3 EMA smoothing factor.", Order = 1, GroupName = "Parameters")]
        public int Period { get; set; }

        [Range(0.0, 1.0)]
        [NinjaScriptProperty]
        [Display(Name = "Coefficient Multiplier", Description = "T3 volume factor d (0..1). Higher = more responsive / more overshoot. Coral default 0.4.", Order = 2, GroupName = "Parameters")]
        public double CoefficientMultiplier { get; set; }

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
