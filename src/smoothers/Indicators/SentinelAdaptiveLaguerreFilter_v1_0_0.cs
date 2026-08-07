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
//  Sentinel Adaptive Laguerre Filter — Ehlers self-adjusting Laguerre (Sentinel smoother building block)  |  Version v1.0.0
//  File: SentinelAdaptiveLaguerreFilter_v1_0_0.cs  |  namespace …Indicators.Sentinel.Smoothers  |  display "Sentinel AdaptiveLaguerreFilter"
//
//  ⚠ NO ORDERS · NO STATE SEAM — a read-only SMOOTHER building block, not a Council voter. It draws a
//  smoothed line + a Sentinel glass card; it publishes nothing (a moving average has no verdict).
//
//  PROVENANCE / LICENSE: CLEAN-ROOM. The algorithm (John Ehlers' ADAPTIVE 4-element Laguerre filter — the
//  same Laguerre polynomial IIR filter, but its damping factor self-adjusts each bar from the normalized
//  recent tracking error) was IDENTIFIED from a GPL-3.0 LizardIndicators source (amaAdaptiveLaguerreFilter.cs),
//  but NO GPL code was used — this is reimplemented FRESH from the canonical public formula (Ehlers, "Time
//  Warp Without Space Travel"), a mathematical method which is not copyrightable. No third-party code,
//  variable names, or structure copied.
//
//  ADAPTATION (canonical Ehlers): each bar,  diff = |price − filt[1]| ;  over the last Length diffs find
//  HH (max) and LL (min) ;  ratio = (diff − LL)/(HH − LL)  (carry prior alpha when HH == LL) ;  the adaptive
//  alpha α = MEDIAN of the last 5 ratios ;  gamma = 1 − α ;  then the standard 4-element Laguerre recursion
//  runs with that α:  L0 = α·price + γ·L0[1] ,  L1 = −γ·L0 + L0[1] + γ·L1[1] , … ,  Value = (L0+2L1+2L2+L3)/6.
//
//  ASSUMPTIONS: the median window is fixed at 5 (Ehlers' canonical value; the GPL source used the same). The
//  HH/LL search window is the last `Period` diffs. Early bars are seeded (diff=0, ratio=alpha=0.5, filter=input).
//
//  CHANGELOG
//    v1.0.0 (2026-07-12) — clean-room Ehlers Adaptive Laguerre filter (self-adjusting alpha via normalized-
//             error median) + Sentinel plumbing (naming law, glass card, label remover). Member of
//             …Indicators.Sentinel.Smoothers.
// ═════════════════════════════════════════════════════════════════════════════
namespace NinjaTrader.NinjaScript.Indicators.Sentinel.Smoothers
{
    public class SentinelAdaptiveLaguerreFilter_v1_0_0 : Indicator
    {
        private SentinelSkin.Painter _sp;
        // cached for the card (data thread) — OnRender must never touch Value[]
        private double _cardVal;
        private int    _cardSlope;
        private bool   _cardHasData;

        private const int MedianWindow = 5;

        private Series<double> diff, ratio, alpha, l0, l1, l2, l3;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description              = "Sentinel smoother library — Ehlers Adaptive Laguerre filter (clean-room). A Laguerre IIR filter whose damping factor self-adjusts from the normalized recent tracking error (fast in trends, smooth in chop) + a Sentinel glass card. A building block, not a Council voter (no State seam).";
                Name                     = "Sentinel AdaptiveLaguerreFilter v1.0.0";
                Calculate                = Calculate.OnPriceChange;
                IsOverlay                = true;
                IsSuspendedWhileInactive = true;
                DrawOnPricePanel         = true;

                Period                   = 20;

                ShowCard           = true;
                CardCorner         = SentinelCardCorner.TopRight;
                ShowIndicatorLabel = false;

                AddPlot(new Stroke(Brushes.MediumBlue, 2), PlotStyle.Line, "AdaptiveLaguerreFilter");
            }
            else if (State == State.Configure)
            {
                diff  = new Series<double>(this);
                ratio = new Series<double>(this);
                alpha = new Series<double>(this);
                l0    = new Series<double>(this);
                l1    = new Series<double>(this);
                l2    = new Series<double>(this);
                l3    = new Series<double>(this);
            }
            else if (State == State.DataLoaded)
            {
                if (!ShowIndicatorLabel) Name = string.Empty;   // Sentinel label remover
            }
            else if (State == State.Terminated)
            {
                if (_sp != null) { try { _sp.Dispose(); } catch (Exception _sx) { SentinelCore.Swallow("SentinelAdaptiveLaguerreFilter.OnStateChange", _sx); } _sp = null; }
                try { SentinelSkin.CardLayout.Release(this); } catch (Exception _sx) { SentinelCore.Swallow("SentinelAdaptiveLaguerreFilter.OnStateChange", _sx); }
            }
        }

        protected override void OnBarUpdate()
        {
            double x = Input[0];

            // Seed: first bar (or degenerate Period=1) → pass-through, neutral adaptation state.
            if (CurrentBar == 0 || Period == 1)
            {
                diff[0]  = 0.0;
                ratio[0] = 0.5;
                alpha[0] = 0.5;
                l0[0] = l1[0] = l2[0] = l3[0] = x;
                Value[0] = x;
                return;
            }

            // Tracking error vs. the previous filtered value.
            diff[0] = Math.Abs(x - Value[1]);

            // HH / LL of the error over the last `Period` bars.
            int win = Math.Min(CurrentBar + 1, Period);
            double hh = diff[0], ll = diff[0];
            for (int i = 0; i < win; i++)
            {
                double d = diff[i];
                if (d > hh) hh = d;
                if (d < ll) ll = d;
            }

            // Normalized error → adaptive alpha = median of the last 5 ratios.
            if (hh > ll)
            {
                ratio[0] = (diff[0] - ll) / (hh - ll);
                alpha[0] = MedianRatio(Math.Min(CurrentBar + 1, MedianWindow));
            }
            else
            {
                ratio[0] = ratio[1];
                alpha[0] = alpha[1];   // carry prior alpha when the error window is flat
            }

            double a = alpha[0];
            double g = 1.0 - a;

            l0[0] = a * x     + g * l0[1];
            l1[0] = -g * l0[0] + l0[1] + g * l1[1];
            l2[0] = -g * l1[0] + l1[1] + g * l2[1];
            l3[0] = -g * l2[0] + l2[1] + g * l3[1];

            Value[0] = (l0[0] + 2.0 * l1[0] + 2.0 * l2[0] + l3[0]) / 6.0;
            // cache the card readout here (data thread), so OnRender never touches Value[]
            _cardVal    = Value[0];
            _cardSlope  = (CurrentBar >= 1) ? (Value[0] > Value[1] ? 1 : (Value[0] < Value[1] ? -1 : 0)) : 0;
            _cardHasData = true;
        }

        // Median of the last `n` ratio values (clean-room; small window, sort a local copy).
        private double MedianRatio(int n)
        {
            if (n <= 1) return ratio[0];
            var buf = new double[n];
            for (int i = 0; i < n; i++) buf[i] = ratio[i];
            Array.Sort(buf);
            int mid = n / 2;
            return (n % 2 == 1) ? buf[mid] : 0.5 * (buf[mid - 1] + buf[mid]);
        }

        // ── Sentinel glass card ──
        protected override void OnRender(NinjaTrader.Gui.Chart.ChartControl chartControl, NinjaTrader.Gui.Chart.ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);
            if (!ShowCard || RenderTarget == null || ChartPanel == null) return;
            if (_sp == null) _sp = new SentinelSkin.Painter();
            _sp.Begin(RenderTarget);
            try { RenderCard(); } catch (Exception _sx) { SentinelCore.Swallow("SentinelAdaptiveLaguerreFilter.OnRender", _sx); }
            try { _sp.End(); } catch (Exception _sx) { SentinelCore.Swallow("SentinelAdaptiveLaguerreFilter.OnRender", _sx); }   // ALWAYS runs — a skipped End() would silently kill the card
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
            _sp.Text("AdaLaguerre", r.Left + 16f, r.Top, r.Width - 70f, 16f, SentinelSkin.CInk, 11f, true);
            _sp.Pill("p" + Period + (_cardHasData ? " α" + alpha[0].ToString("0.##") : ""), r.Right, r.Top - 1f, SentinelSkin.CMute);

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
        public Series<double> AdaptiveLaguerreFilter => Values[0];

        #region Properties
        [Range(1, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Period", Description = "Length of the error window (HH/LL search) that drives the self-adjusting alpha. Default 20.", Order = 1, GroupName = "Parameters")]
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
