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
//  Sentinel LLMA — Low-Lag (Jurik-style) Moving Average (Sentinel smoother block) |  Version v1.0.0
//  File: SentinelLLMA_v1_0_0.cs  |  namespace …Indicators.Sentinel.Smoothers  |  display "Sentinel LLMA"
//
//  ⚠ NO ORDERS · NO STATE SEAM — a read-only SMOOTHER building block, not a Council voter. It draws a
//  smoothed low-lag line + a Sentinel glass card; it publishes nothing (a moving average has no verdict).
//
//  PROVENANCE / LICENSE: CLEAN-ROOM. The "AuLLMA" source ("Low Lag Moving Average", Length + Phase params)
//  is a JURIK-JMA-style adaptive filter: a 3-stage EMA cascade (preliminary EMA → detrend EMA → phase-
//  adjusted Kalman-style stage) whose smoothing factor is driven by a proprietary volatility-band estimator
//  (a sorted trimmed-mean "MidAvg" window). That volatility-adaptive stage is non-standard / proprietary.
//  This is a FRESH reimplementation from the CANONICAL public simplified JMA formula — no third-party code,
//  names, or structure were copied.
//
//  ASSUMPTION (noted per clean-room rule): I implemented the closest CANONICAL PUBLISHED form — the
//  simplified Jurik JMA with a FIXED smoothing factor (dynamic-volatility power = 1). I deliberately OMIT
//  the source's proprietary volatility-adaptive dynamic factor (its "MidAvg" trimmed-mean band + log/sqrt
//  scaling). Consequently output tracks the standard published JMA, not the exact Au/MidAvg curve. Length +
//  Phase are preserved with the standard JMA semantics: beta = 0.45·(Length−1) / (0.45·(Length−1) + 2);
//  phaseRatio = Phase<−100 ? 0.5 : Phase>100 ? 2.5 : Phase/100 + 1.5.
//
//  CHANGELOG
//    v1.0.0 (2026-07-12) — clean-room simplified Jurik (low-lag) MA + Sentinel plumbing (naming law, glass
//             card, label remover). Volatility-adaptive MidAvg stage omitted (see ASSUMPTION above).
// ═════════════════════════════════════════════════════════════════════════════
namespace NinjaTrader.NinjaScript.Indicators.Sentinel.Smoothers
{
    public class SentinelLLMA_v1_0_0 : Indicator
    {
        private SentinelSkin.Painter _sp;
        // cached for the card (data thread) — OnRender must never touch Value[]
        private double _cardVal;
        private int    _cardSlope;
        private bool   _cardHasData;
        private Series<double> _e0;   // preliminary EMA
        private Series<double> _e1;   // detrend EMA
        private Series<double> _e2;   // phase-adjusted stage

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description              = "Sentinel smoother library — Low-Lag (Jurik-style) Moving Average (clean-room, simplified JMA). Draws a low-lag smoothed line + a Sentinel glass card. A building block, not a Council voter (no State seam).";
                Name                     = "Sentinel LLMA v1.0.0";
                Calculate                = Calculate.OnPriceChange;
                IsOverlay                = true;
                IsSuspendedWhileInactive = true;
                DrawOnPricePanel         = true;

                Length                   = 14;
                Phase                    = 0;

                ShowCard           = true;
                CardCorner         = SentinelCardCorner.TopRight;
                ShowIndicatorLabel = false;

                AddPlot(new Stroke(Brushes.SkyBlue, 2), PlotStyle.Line, "LLMA");
            }
            else if (State == State.Configure)
            {
                _e0 = new Series<double>(this);
                _e1 = new Series<double>(this);
                _e2 = new Series<double>(this);
            }
            else if (State == State.DataLoaded)
            {
                if (!ShowIndicatorLabel) Name = string.Empty;   // Sentinel label remover
            }
            else if (State == State.Terminated)
            {
                if (_sp != null) { try { _sp.Dispose(); } catch (Exception _sx) { SentinelCore.Swallow("SentinelLLMA.OnStateChange", _sx); } _sp = null; }
                try { SentinelSkin.CardLayout.Release(this); } catch (Exception _sx) { SentinelCore.Swallow("SentinelLLMA.OnStateChange", _sx); }
            }
        }

        protected override void OnBarUpdate()
        {
            // Clean-room simplified Jurik JMA (canonical public form; volatility-adaptive stage omitted).
            double price = Input[0];

            if (CurrentBar < 1)
            {
                _e0[0]   = price;
                _e1[0]   = 0.0;
                _e2[0]   = 0.0;
                Value[0] = price;
                return;
            }

            double len1  = 0.45 * (Length - 1);
            double beta  = len1 / (len1 + 2.0);   // detrend / EMA smoothing factor
            double alpha = beta;                  // simplified JMA: fixed factor (no volatility adaptation)
            double phaseRatio = Phase < -100 ? 0.5 : (Phase > 100 ? 2.5 : Phase / 100.0 + 1.5);

            _e0[0] = (1.0 - alpha) * price + alpha * _e0[1];
            _e1[0] = (price - _e0[0]) * (1.0 - beta) + beta * _e1[1];
            _e2[0] = (_e0[0] + phaseRatio * _e1[0] - Value[1]) * (1.0 - alpha) * (1.0 - alpha)
                     + (alpha * alpha) * _e2[1];
            Value[0] = Value[1] + _e2[0];
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
            try { RenderCard(); } catch (Exception _sx) { SentinelCore.Swallow("SentinelLLMA.OnRender", _sx); }
            try { _sp.End(); } catch (Exception _sx) { SentinelCore.Swallow("SentinelLLMA.OnRender", _sx); }   // ALWAYS runs — a skipped End() would silently kill the card
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
            _sp.Text("LLMA", r.Left + 16f, r.Top, r.Width - 70f, 16f, SentinelSkin.CInk, 11f, true);
            _sp.Pill("L" + Length, r.Right, r.Top - 1f, SentinelSkin.CMute);

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
        public Series<double> LLMA => Values[0];

        #region Properties
        [Range(1, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Length", Description = "Lookback length of the low-lag filter.", Order = 1, GroupName = "Parameters")]
        public int Length { get; set; }

        [Range(-100.0, 100.0)]
        [NinjaScriptProperty]
        [Display(Name = "Phase", Description = "Phase (lead/lag bias), −100 to 100. 0 = neutral.", Order = 2, GroupName = "Parameters")]
        public double Phase { get; set; }

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
