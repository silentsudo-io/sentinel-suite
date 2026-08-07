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
//  Sentinel SWMA — Sine-Weighted Moving Average (Sentinel smoother building block)   |   Version v1.0.0
//  File: SentinelSWMA_v1_0_0.cs  |  namespace …Indicators.Sentinel.Smoothers  |  display "Sentinel SWMA"
//
//  ⚠ NO ORDERS · NO STATE SEAM — a read-only SMOOTHER building block, not a Council voter. It draws the
//  sine-weighted average + a Sentinel glass card and publishes nothing (a moving average has no verdict).
//
//  PROVENANCE / LICENSE: CLEAN-ROOM. Algorithm identified from a GPL LizardIndicators source
//  (amaSWMA.cs, GPL-3.0) but NO GPL code was used — reimplemented from the canonical PUBLIC Sine-Weighted
//  Moving Average formula (a mathematical method, not copyrightable). No third-party code/variable-names/
//  structure were copied.
//
//    CANONICAL SWMA over a window of n = min(CurrentBar+1, Period) inputs:
//        wᵢ    = sin( π · (i+1) / (n+1) )          for i = 0 … n-1   (i=0 = current bar)
//        Value = Σ (wᵢ · Input[i]) / Σ wᵢ
//    The sine weights are symmetric and peak at the middle of the window, so the SWMA is a smooth,
//    low-noise average that de-emphasises the window edges.
//
//  ASSUMPTIONS: (1) Weight indexing wᵢ = sin(π·(i+1)/(n+1)) with i=0 the most recent bar, matching the
//  confirmed source indexing. (2) During warm-up (fewer than Period bars) the window shrinks to the bars
//  available and the denominator (n+1) shrinks with it, so the average is always properly normalised.
//
//  CHANGELOG
//    v1.0.0 (2026-07-12) — initial: clean-room sine-weighted MA + Sentinel naming law, glass card, label remover.
// ═════════════════════════════════════════════════════════════════════════════
namespace NinjaTrader.NinjaScript.Indicators.Sentinel.Smoothers
{
    public class SentinelSWMA_v1_0_0 : Indicator
    {
        private SentinelSkin.Painter _sp;
        // cached for the card (data thread) — OnRender must never touch Value[]
        private double _cardVal;
        private int    _cardSlope;
        private bool   _cardHasData;

        // Precomputed full-window sine weights + their sum (used once CurrentBar+1 >= Period).
        private double[] _w;
        private double   _wSum;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description              = "Sentinel smoother library — Sine-Weighted Moving Average (clean-room). Draws a sine-weighted average of the last N inputs + a Sentinel glass card. A building block, not a Council voter (no State seam).";
                Name                     = "Sentinel SWMA v1.0.0";
                Calculate                = Calculate.OnPriceChange;
                IsOverlay                = true;
                IsSuspendedWhileInactive = true;
                DrawOnPricePanel         = true;

                Period                   = 15;

                ShowCard           = true;
                CardCorner         = SentinelCardCorner.TopRight;
                ShowIndicatorLabel = false;

                AddPlot(new Stroke(Brushes.SkyBlue, 2), PlotStyle.Line, "SWMA");
            }
            else if (State == State.Configure)
            {
                // Clean-room intermediate: the full-window sine-weight table wᵢ = sin(π(i+1)/(Period+1)).
                _w   = new double[Period];
                _wSum = 0.0;
                double f = Math.PI / (Period + 1.0);
                for (int i = 0; i < Period; i++)
                {
                    _w[i]  = Math.Sin((i + 1) * f);
                    _wSum += _w[i];
                }
            }
            else if (State == State.DataLoaded)
            {
                if (!ShowIndicatorLabel) Name = string.Empty;   // Sentinel label remover
            }
            else if (State == State.Terminated)
            {
                if (_sp != null) { try { _sp.Dispose(); } catch (Exception _sx) { SentinelCore.Swallow("SentinelSWMA.OnStateChange", _sx); } _sp = null; }
                try { SentinelSkin.CardLayout.Release(this); } catch (Exception _sx) { SentinelCore.Swallow("SentinelSWMA.OnStateChange", _sx); }
            }
        }

        protected override void OnBarUpdate()
        {
            int n = Math.Min(CurrentBar + 1, Period);

            double num = 0.0, den;
            if (n == Period)
            {
                // Full window — use the precomputed weight table.
                for (int i = 0; i < n; i++) num += _w[i] * Input[i];
                den = _wSum;
            }
            else
            {
                // Warm-up — recompute sine weights for the shrunken window so it stays normalised.
                den = 0.0;
                double f = Math.PI / (n + 1.0);
                for (int i = 0; i < n; i++)
                {
                    double w = Math.Sin((i + 1) * f);
                    num += w * Input[i];
                    den += w;
                }
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
            try { RenderCard(); } catch (Exception _sx) { SentinelCore.Swallow("SentinelSWMA.OnRender", _sx); }
            try { _sp.End(); } catch (Exception _sx) { SentinelCore.Swallow("SentinelSWMA.OnRender", _sx); }   // ALWAYS runs — a skipped End() would silently kill the card
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
            _sp.Text("SWMA", r.Left + 16f, r.Top, r.Width - 70f, 16f, SentinelSkin.CInk, 11f, true);
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
        public Series<double> SWMA => Values[0];

        #region Properties
        [Range(1, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Period", Description = "Number of inputs in the sine-weighted window.", Order = 1, GroupName = "Parameters")]
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
