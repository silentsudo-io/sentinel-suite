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
//  Sentinel Tillson T3 — 6-pole T3 smoother (Sentinel smoother building block)   |   Version v1.0.0
//  File: SentinelTillsonT3_v1_0_0.cs  |  namespace …Indicators.Sentinel.Smoothers  |  display "Sentinel TillsonT3"
//
//  ⚠ NO ORDERS · NO STATE SEAM — a read-only SMOOTHER building block, not a Council voter. It draws a
//  smoothed line + a Sentinel glass card; it publishes nothing (a moving average has no verdict). A baseline
//  the signal tools can consume, and a Sentinel-branded T3 in its own right.
//
//  PROVENANCE / LICENSE: CLEAN-ROOM. The algorithm (Tim Tillson's T3 — a 6-pole cascade of exponential
//  moving averages combined with a volume-factor weighting) was IDENTIFIED from a GPL-3.0 LizardIndicators
//  source (amaTillsonT3.cs), but NO GPL code was used — this is reimplemented FRESH from the canonical public
//  formula (Tillson, "Smoothing Techniques for More Accurate Signals", TASC Jan 1998), a mathematical method
//  which is not copyrightable. No third-party code, variable names, or structure were copied.
//
//  ASSUMPTIONS: implements the standard "Tillson" mode (all six EMAs use lookback = Period; α = 2/(Period+1)).
//  The source's optional "Fulks-Matulich" period-rescale mode + its CalcMode enum are intentionally omitted
//  (Sentinel prefers plain params over new enums). Early bars are seeded from the input value.
//
//  CHANGELOG
//    v1.0.0 (2026-07-12) — clean-room Tillson T3 (6 cascaded EMAs + volume-factor combination) + Sentinel
//             plumbing (naming law, glass card, label remover). Member of …Indicators.Sentinel.Smoothers.
// ═════════════════════════════════════════════════════════════════════════════
namespace NinjaTrader.NinjaScript.Indicators.Sentinel.Smoothers
{
    public class SentinelTillsonT3_v1_0_0 : Indicator
    {
        private SentinelSkin.Painter _sp;
        // cached for the card (data thread) — OnRender must never touch Value[]
        private double _cardVal;
        private int    _cardSlope;
        private bool   _cardHasData;

        private Series<double> e1, e2, e3, e4, e5, e6;
        private double alpha, c1, c2, c3, c4;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description              = "Sentinel smoother library — Tillson T3 (clean-room). A 6-pole smoother: six cascaded EMAs combined via a volume-factor weighting → a low-lag, low-noise moving average + a Sentinel glass card. A building block, not a Council voter (no State seam).";
                Name                     = "Sentinel TillsonT3 v1.0.0";
                Calculate                = Calculate.OnPriceChange;
                IsOverlay                = true;
                IsSuspendedWhileInactive = true;
                DrawOnPricePanel         = true;

                Period                   = 14;
                VFactor                  = 0.7;

                ShowCard           = true;
                CardCorner         = SentinelCardCorner.TopRight;
                ShowIndicatorLabel = false;

                AddPlot(new Stroke(Brushes.MediumBlue, 2), PlotStyle.Line, "TillsonT3");
            }
            else if (State == State.Configure)
            {
                e1 = new Series<double>(this);
                e2 = new Series<double>(this);
                e3 = new Series<double>(this);
                e4 = new Series<double>(this);
                e5 = new Series<double>(this);
                e6 = new Series<double>(this);

                alpha = 2.0 / (Period + 1.0);

                double b = VFactor, b2 = b * b, b3 = b2 * b;
                c1 = -b3;
                c2 = 3.0 * b2 + 3.0 * b3;
                c3 = -6.0 * b2 - 3.0 * b - 3.0 * b3;
                c4 = 1.0 + 3.0 * b + b3 + 3.0 * b2;
            }
            else if (State == State.DataLoaded)
            {
                if (!ShowIndicatorLabel) Name = string.Empty;   // Sentinel label remover
            }
            else if (State == State.Terminated)
            {
                if (_sp != null) { try { _sp.Dispose(); } catch (Exception _sx) { SentinelCore.Swallow("SentinelTillsonT3.OnStateChange", _sx); } _sp = null; }
                try { SentinelSkin.CardLayout.Release(this); } catch (Exception _sx) { SentinelCore.Swallow("SentinelTillsonT3.OnStateChange", _sx); }
            }
        }

        protected override void OnBarUpdate()
        {
            // Clean-room Tillson T3: six cascaded EMAs (α = 2/(Period+1)), then a volume-factor combination.
            if (CurrentBar == 0)
            {
                double x0 = Input[0];
                e1[0] = e2[0] = e3[0] = e4[0] = e5[0] = e6[0] = x0;
                Value[0] = c1 * e6[0] + c2 * e5[0] + c3 * e4[0] + c4 * e3[0];
                return;
            }

            e1[0] = e1[1] + alpha * (Input[0] - e1[1]);
            e2[0] = e2[1] + alpha * (e1[0]   - e2[1]);
            e3[0] = e3[1] + alpha * (e2[0]   - e3[1]);
            e4[0] = e4[1] + alpha * (e3[0]   - e4[1]);
            e5[0] = e5[1] + alpha * (e4[0]   - e5[1]);
            e6[0] = e6[1] + alpha * (e5[0]   - e6[1]);

            Value[0] = c1 * e6[0] + c2 * e5[0] + c3 * e4[0] + c4 * e3[0];
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
            try { RenderCard(); } catch (Exception _sx) { SentinelCore.Swallow("SentinelTillsonT3.OnRender", _sx); }
            try { _sp.End(); } catch (Exception _sx) { SentinelCore.Swallow("SentinelTillsonT3.OnRender", _sx); }   // ALWAYS runs — a skipped End() would silently kill the card
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
            _sp.Text("T3", r.Left + 16f, r.Top, r.Width - 70f, 16f, SentinelSkin.CInk, 11f, true);
            _sp.Pill("p" + Period + " b" + VFactor.ToString("0.##"), r.Right, r.Top - 1f, SentinelSkin.CMute);

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
        public Series<double> TillsonT3 => Values[0];

        #region Properties
        [Range(1, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Period", Description = "Lookback for each of the six cascaded EMAs (α = 2/(Period+1)).", Order = 1, GroupName = "Parameters")]
        public int Period { get; set; }

        [Range(0.0, 1.0)]
        [NinjaScriptProperty]
        [Display(Name = "V-Factor", Description = "Volume factor b (0..1). Controls the T3 responsiveness/overshoot trade-off; classic default 0.7.", Order = 2, GroupName = "Parameters")]
        public double VFactor { get; set; }

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
