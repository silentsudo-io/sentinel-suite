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
using System.Text;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.AddOns.Sentinel;   // SentinelSkin + SentinelCore (MtfState seam) + SentinelCardCorner
#endregion

// ═════════════════════════════════════════════════════════════════════════════
//  MTF — the Sentinel MULTI-TIMEFRAME ALIGNMENT axis                        |   Version v1.0.0
//  File: Mtf_v1_0_0.cs   |   namespace …Indicators.Sentinel   |   display Name "MTF"
//
//  ⚠ NO ORDERS — read-only advisory indicator. Safe to run anywhere.
//
//  WHAT THIS IS — the FOURTH orthogonal axis feeding the Council (Docs/ROADMAP.md · memory
//  signal-axes-plan). Is the entry-timeframe signal WITH or AGAINST the higher timeframes? MTF alignment
//  is one of the most reliable conviction multipliers, and it's cheap: add the ladder as data series,
//  read a trend direction on each, and publish the consensus so the Council can PENALISE a trade taken
//  against the higher-timeframe tide.
//
//  THE STATE (SentinelCore.MtfState, SentinelCore ≥ v1.10.0):
//    Bias (consensus -1/0/1) · AlignmentScore (-1..1 weighted net; higher TFs weighted more) ·
//    AlignedCount / TfCount · AllAgree · Dirs (compact per-TF summary, e.g. "5:+ 15:+ 60:- 240:+").
//
//  TREND PER TF — anchored to the suite's CANONICAL trend definition: MTF HOSTS SentinelTrend on each
//  ladder series (SentinelTrend_v1_0_0(BarsArray[i], …) with card/publish/signals OFF) and reads its
//  Direction, so a TF's "trend" means exactly what SentinelTrend means everywhere else (true ATR + CCI
//  hysteresis trailing line) — MTF and TrendState can never disagree by construction.
//
//  SENTINEL WIRING (Docs/SENTINEL_DESIGN_SYSTEM.md §6c/§6d):
//    • PUBLISH: SetMtfState(...) each update (default ON). No plots (consumed via the seam).
//    • Ladder = up to 5 minute timeframes (0 disables a slot); each AddDataSeries'd in Configure.
//    • A SentinelSkin.Painter glass card + Sentinel palette + label remover.
//
//  CHANGELOG
//    v1.0.0 (2026-07-07) — initial: per-TF trend over a 1/5/15/60/240 ladder → weighted consensus, published
//             as SentinelCore.MtfState; Sentinel card. Fourth Council axis.
//             + (same day) TREND now ANCHORED to SentinelTrend — hosts SentinelTrend_v1_0_0 on each ladder
//               series (card/publish/signals off) and reads its Direction, replacing the initial EMA-cross
//               proxy. New params CciPeriod/AtrPeriod/AtrMult/CciThreshold; EmaFast/EmaSlow removed.
// ═════════════════════════════════════════════════════════════════════════════
namespace NinjaTrader.NinjaScript.Indicators.Sentinel
{
    public class Mtf_v1_0_0 : Indicator
    {
        private SentinelSkin.Painter _sp;
        private bool _hasData;
        private readonly List<int> _tfMin = new List<int>();   // enabled ladder minutes (parallel to _tfIdx)
        private readonly List<int> _tfIdx = new List<int>();   // BarsArray index for each enabled TF
        // cached snapshot for the card
        private int    _bias, _aligned, _tfCount;
        private double _score;
        private bool   _allAgree;
        private string _dirs = "";

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description              = "Sentinel multi-timeframe alignment — reads a trend direction on a 1/5/15/60/240 ladder and publishes the consensus bias + alignment as SentinelCore.MtfState so the Council can penalise counter-higher-timeframe trades.";
                Name                     = "Sentinel MTF v1.0.0";
                Calculate                = Calculate.OnPriceChange;
                IsOverlay                = true;
                IsSuspendedWhileInactive = true;
                DisplayInDataBox         = false;
                DrawOnPricePanel         = true;

                Tf1Min = 1; Tf2Min = 5; Tf3Min = 15; Tf4Min = 60; Tf5Min = 240;   // ladder (0 = disable a slot)
                // SentinelTrend params (the CANONICAL trend def, hosted per ladder series) — match SentinelTrend's defaults
                CciPeriod = 20; AtrPeriod = 14; AtrMult = 1.5; CciThreshold = 15.0;
                BiasDeadband = 0.15;   // |score| must exceed this to call a consensus side

                PublishState = true;
                ShowCard     = true;
                CardCorner   = SentinelCardCorner.TopRight;
                ShowIndicatorLabel = false;
            }
            else if (State == State.Configure)
            {
                _tfMin.Clear(); _tfIdx.Clear();
                int idx = 1;   // primary = 0; added series start at 1
                foreach (int m in new[] { Tf1Min, Tf2Min, Tf3Min, Tf4Min, Tf5Min })
                {
                    if (m <= 0) continue;
                    // v1.0.1 (2026-07-23) — GUARDED (same class as Intermarket/Eye). A failed ladder row used to
                    // abort State.Configure and take MTF offline silently; now the row is dropped and logged.
                    // ⚠ idx must NOT advance on failure or the surviving rungs misalign with BarsArray.
                    try
                    {
                        AddDataSeries(BarsPeriodType.Minute, m);
                    }
                    catch (Exception ex)
                    {
                        try { SentinelCore.Log("MTF", string.Format(
                            "ladder {0}m DISABLED — AddDataSeries failed: {1}", m, ex.Message)); } catch (Exception _sx) { SentinelCore.Swallow("Mtf.OnStateChange", _sx); }
                        continue;
                    }
                    _tfMin.Add(m); _tfIdx.Add(idx); idx++;
                }
            }
            else if (State == State.DataLoaded)
            {
                if (!ShowIndicatorLabel) Name = string.Empty;
            }
            else if (State == State.Terminated)
            {
                if (_sp != null) { try { _sp.Dispose(); } catch (Exception _sx) { SentinelCore.Swallow("Mtf.OnStateChange", _sx); } _sp = null; }
                try { SentinelSkin.CardLayout.Release(this); } catch (Exception _sx) { SentinelCore.Swallow("Mtf.OnStateChange", _sx); }
                if (_scope != null) { try { SentinelCore.ClearMtfScope(_scope); } catch (Exception _sx) { SentinelCore.Swallow("Mtf.OnStateChange", _sx); } }
            }
        }

        // ── Sentinel scope (v1.20.0 — seam scope migration 1.4). OnPriceChange republishes per tick, so no heartbeat. ──
        private string _scope;
        /// <summary>This chart's SCOPE ("GC.69697v6x24") — instrument × primary bar type. Cached after first resolve.</summary>
        private string Scope()
        {
            if (_scope == null)
            {
                try { if (Instrument != null && BarsPeriod != null) _scope = SentinelCore.ScopeOf(Instrument, BarsPeriod); } catch (Exception _sx) { SentinelCore.Swallow("Mtf.Scope", _sx); }
            }
            return _scope;
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0) return;   // compute on the primary series
            if (Instrument == null || Instrument.MasterInstrument == null) return;

            int warmup = Math.Max(CciPeriod, AtrPeriod);
            double num = 0, den = 0;   // weighted net / total weight
            int warm = 0, firstDir = 0; bool allAgree = true, anyDir = false;
            var sb = new StringBuilder();
            var dirs = new List<int>(_tfIdx.Count);   // per-warm-TF direction (for the aligned tally)

            for (int k = 0; k < _tfIdx.Count; k++)
            {
                int si = _tfIdx[k], m = _tfMin[k];
                if (si >= CurrentBars.Length || CurrentBars[si] <= warmup) continue;   // not warm yet
                int dir;
                try
                {
                    // CANONICAL trend: host SentinelTrend on this ladder series (card/publish/signals OFF).
                    var st = SentinelTrend_v1_0_0(BarsArray[si], CciPeriod, AtrPeriod, AtrMult, CciThreshold,
                                                  false, 8, false, false, 12, false, SentinelCardCorner.TopRight,
                                                  false, 90.0, false, false);
                    dir = (int)st.Direction[0];
                }
                catch { continue; }

                warm++;
                double w = k + 1;                 // higher TFs (later slots) weigh more
                num += dir * w; den += w;
                dirs.Add(dir);
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(m).Append(dir > 0 ? ":+" : (dir < 0 ? ":-" : ":0"));

                if (dir != 0)
                {
                    if (!anyDir) { firstDir = dir; anyDir = true; }
                    else if (dir != firstDir) allAgree = false;
                }
                else allAgree = false;
            }

            double score = den > 0 ? num / den : 0.0;
            int bias = score > BiasDeadband ? 1 : (score < -BiasDeadband ? -1 : 0);
            int aligned = 0;
            if (bias != 0) foreach (int d in dirs) if (d == bias) aligned++;

            _bias = bias; _score = score; _aligned = aligned; _tfCount = warm;
            _allAgree = warm > 0 && allAgree && anyDir; _dirs = sb.ToString();
            _hasData = warm > 0;

            if (PublishState && warm > 0)
            {
                try
                {
                    SentinelCore.SetMtfState(new SentinelCore.MtfState
                    {
                        Scope = Scope(), Bartype = SentinelCore.BarTag(BarsPeriod),
                        Instrument = Instrument.MasterInstrument.Name,
                        Bias = bias, AlignmentScore = score, AlignedCount = aligned,
                        TfCount = warm, AllAgree = _allAgree, Dirs = _dirs, Source = "MTF"
                    });
                }
                catch (Exception _sx) { SentinelCore.Swallow("Mtf.OnBarUpdate", _sx); }
            }
        }

        // ── Sentinel glass card ──
        protected override void OnRender(NinjaTrader.Gui.Chart.ChartControl chartControl, NinjaTrader.Gui.Chart.ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);
            if (!ShowCard || RenderTarget == null || ChartPanel == null) return;
            try
            {
                if (_sp == null) _sp = new SentinelSkin.Painter();
                _sp.Begin(RenderTarget);

                const float cw = 228f, ch = 150f;
                var slot = SentinelSkin.CardLayout.Place(this, ChartPanel,
                    ChartPanel.X, ChartPanel.Y, ChartPanel.W, ChartPanel.H, CardCorner, cw, ch);

                if (!_hasData)
                {
                    var rw = _sp.Card(slot.X, slot.Y, cw, ch, SentinelSkin.CLine);
                    _sp.Dot(rw.Left + 5f, rw.Top + 8f, SentinelSkin.CMute, false);
                    _sp.Text("MTF", rw.Left + 16f, rw.Top, rw.Width - 20f, 16f, SentinelSkin.CInk, 11f, true);
                    _sp.Text("loading timeframes…", rw.Left, rw.Top + 26f, rw.Width, 14f, SentinelSkin.CMute, 10.5f);
                    _sp.End();
                    return;
                }

                var trail   = SharpDX.DirectWrite.TextAlignment.Trailing;
                var biasCol = _bias > 0 ? SentinelSkin.CUp : (_bias < 0 ? SentinelSkin.CDown : SentinelSkin.CMute);
                var edge    = _allAgree ? SentinelSkin.CAccent : SentinelSkin.CLine;
                var r = _sp.Card(slot.X, slot.Y, cw, ch, edge);

                // header — dot, title, consensus pill
                _sp.Dot(r.Left + 5f, r.Top + 8f, _bias != 0 ? biasCol : SentinelSkin.CMute, _bias != 0);
                _sp.Text("MTF", r.Left + 16f, r.Top, r.Width - 80f, 16f, SentinelSkin.CInk, 11f, true);
                _sp.Pill(_bias > 0 ? "UP" : (_bias < 0 ? "DOWN" : "MIXED"), r.Right, r.Top - 1f, biasCol);

                // hero — aligned tally + score
                _sp.Text("ALIGNMENT", r.Left, r.Top + 24f, 120f, 12f, SentinelSkin.CMute, 8.5f, true);
                _sp.Text(_aligned + "/" + _tfCount + (_allAgree ? " ✓" : ""), r.Left, r.Top + 34f, r.Width, 24f, biasCol, 18f, false);
                _sp.Text((_score >= 0 ? "+" : "") + _score.ToString("0.00"), r.Left, r.Top + 26f, r.Width, 16f, SentinelSkin.CInk2, 11f, true, trail);

                // meter — |score|
                _sp.Track(r.Left, r.Top + 60f, r.Width, (float)Math.Min(1.0, Math.Abs(_score)), biasCol);

                // footer — per-TF dirs
                _sp.Divider(r.Left, r.Top + 78f, r.Right);
                _sp.Text(_dirs, r.Left, r.Top + 84f, r.Width, 14f, SentinelSkin.CInk2, 9.5f);

                _sp.End();
            }
            catch (Exception _sx) { SentinelCore.Swallow("Mtf.OnRender", _sx); }
        }

        #region Properties
        [Range(0, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "TF 1 (min)", Description = "Ladder timeframe 1 in minutes (0 = disable this slot).", Order = 1, GroupName = "Ladder")]
        public int Tf1Min { get; set; }

        [Range(0, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "TF 2 (min)", Order = 2, GroupName = "Ladder")]
        public int Tf2Min { get; set; }

        [Range(0, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "TF 3 (min)", Order = 3, GroupName = "Ladder")]
        public int Tf3Min { get; set; }

        [Range(0, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "TF 4 (min)", Order = 4, GroupName = "Ladder")]
        public int Tf4Min { get; set; }

        [Range(0, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "TF 5 (min)", Order = 5, GroupName = "Ladder")]
        public int Tf5Min { get; set; }

        [Range(1, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "CCI Period", Description = "SentinelTrend CCI period (per-TF trend, hosted).", Order = 6, GroupName = "Trend (SentinelTrend)")]
        public int CciPeriod { get; set; }

        [Range(1, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "ATR Period", Description = "SentinelTrend ATR period (per-TF trend, hosted).", Order = 7, GroupName = "Trend (SentinelTrend)")]
        public int AtrPeriod { get; set; }

        [Range(0.00001, double.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "ATR Multiplier", Description = "SentinelTrend ATR band multiplier (per-TF trend, hosted).", Order = 8, GroupName = "Trend (SentinelTrend)")]
        public double AtrMult { get; set; }

        [Range(0.0, double.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "CCI Hysteresis Threshold", Description = "SentinelTrend CCI deadband half-width (per-TF trend, hosted).", Order = 9, GroupName = "Trend (SentinelTrend)")]
        public double CciThreshold { get; set; }

        [Range(0.0, 1.0)]
        [NinjaScriptProperty]
        [Display(Name = "Bias Deadband", Description = "|AlignmentScore| must exceed this to declare a consensus side (else MIXED).", Order = 8, GroupName = "Parameters")]
        public double BiasDeadband { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Publish to Sentinel", Description = "Publish as SentinelCore.MtfState so the Council/strategies can consult alignment. Needs SentinelCore ≥ v1.10.0.", Order = 10, GroupName = "Sentinel")]
        public bool PublishState { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Card", Order = 11, GroupName = "Sentinel")]
        public bool ShowCard { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Card Corner", Description = "Which chart corner the Sentinel card docks to. Cards in the same corner auto-stack.", Order = 12, GroupName = "Sentinel")]
        public SentinelCardCorner CardCorner { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show indicator label", Description = "Show NinjaTrader's chart name label. Sentinel default = OFF (clean chart); turn on to restore it.", GroupName = "Sentinel", Order = 100)]
        public bool ShowIndicatorLabel { get; set; }
        #endregion
    }
}
