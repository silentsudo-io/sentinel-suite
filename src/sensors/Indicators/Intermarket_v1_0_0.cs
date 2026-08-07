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
using NinjaTrader.NinjaScript.AddOns.Sentinel;   // SentinelSkin + SentinelCore (IntermarketState seam) + SentinelCardCorner
#endregion

// ═════════════════════════════════════════════════════════════════════════════
//  Intermarket — the Sentinel CORRELATED-INSTRUMENT axis                    |   Version v1.0.0
//  File: Intermarket_v1_0_0.cs   |   namespace …Indicators.Sentinel   |   display Name "Intermarket"
//
//  ⚠ NO ORDERS — read-only advisory indicator. Safe to run anywhere.
//
//  WHAT THIS IS — the FIFTH orthogonal axis feeding the Council (Docs/ROADMAP.md · memory signal-axes-plan).
//  The single-instrument price sensors can't see the MACRO cross-currents that drive a market. Intermarket
//  reads a configurable set of CORRELATED instruments and publishes a net directional LEAN for the chart
//  instrument — genuinely independent information (e.g. bonds up / real-yields down is gold-supportive).
//
//  INSTRUMENT-AGNOSTIC BY DESIGN — the correlation SIGN differs by market, so it's config, not hardcoded:
//    • GOLD (GC/MGC): Ref = ZN (10y note), positive — bonds up (yields down) ⇒ gold up. (ZB works too.)
//    • ES/NQ: the bond↔equity sign is regime-dependent — set your own partner + polarity (e.g. the sister
//      index for lead/lag), or leave the ref blank to disable the axis on that chart.
//  Two reference slots, each with an INVERSE toggle (positive vs negative correlation). Empty slot = off.
//
//  THE STATE (SentinelCore.IntermarketState, SentinelCore ≥ v1.12.0):
//    Lean (-1/0/1 for the chart instrument) · Score (-1..1 sign-adjusted) · RefCount · Refs ("ZN:+ ZB:+").
//
//  TREND PER REF — anchored to the canonical trend def: hosts SentinelTrend on each reference series
//  (SentinelTrend_v1_0_0(BarsArray[i], …) card/publish/signals OFF), reads its Direction, applies the ref's
//  sign, and averages. Reference series are added at RefMinutes (a higher TF so macro noise doesn't whipsaw).
//
//  SENTINEL WIRING (Docs/SENTINEL_DESIGN_SYSTEM.md §6c/§6d + §9 Council protocol):
//    • PUBLISH: SetIntermarketState(...) each update (default ON). No plots (consumed via the seam).
//    • Council fuses it as a directional VOTER (IMKT). A SentinelSkin.Painter glass card + label remover.
//
//  CHANGELOG
//    v1.0.0 (2026-07-07) — initial: configurable correlated-instrument lean (default ZN+ for gold), hosted
//             SentinelTrend per ref, published as SentinelCore.IntermarketState; Sentinel card. Fifth Council axis.
// ═════════════════════════════════════════════════════════════════════════════
namespace NinjaTrader.NinjaScript.Indicators.Sentinel
{
    public class Intermarket_v1_0_0 : Indicator
    {
        private SentinelSkin.Painter _sp;
        private bool _hasData;
        private readonly List<int>    _refIdx   = new List<int>();    // BarsArray index per enabled ref
        private readonly List<int>    _refSign  = new List<int>();    // +1 / -1 per enabled ref
        private readonly List<string> _refLabel = new List<string>(); // short label per enabled ref
        // cached snapshot for the card
        private int    _lean, _refCount;
        private double _score;
        private string _refs = "";

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description              = "Sentinel intermarket axis — reads correlated instruments (default ZN for gold) with a per-ref polarity and publishes a net directional lean as SentinelCore.IntermarketState so the Council can fuse macro cross-market context.";
                Name                     = "Sentinel Intermarket v1.0.0";
                Calculate                = Calculate.OnPriceChange;
                IsOverlay                = true;
                IsSuspendedWhileInactive = true;
                DisplayInDataBox         = false;
                DrawOnPricePanel         = true;

                Ref1Symbol = "ZN"; Ref1Inverse = false;   // gold default: bonds up ⇒ gold up (positive)
                Ref2Symbol = "";   Ref2Inverse = false;   // 2nd ref off by default (e.g. "ZB" for gold)
                RefMinutes = 5;                            // add each ref at this timeframe (macro, not noisy)

                // SentinelTrend params (canonical trend def, hosted per ref) — match SentinelTrend's defaults
                CciPeriod = 20; AtrPeriod = 14; AtrMult = 1.5; CciThreshold = 15.0;
                BiasDeadband = 0.15;

                PublishState = true;
                ShowCard     = true;
                CardCorner   = SentinelCardCorner.TopRight;
                ShowIndicatorLabel = false;
            }
            else if (State == State.Configure)
            {
                _refIdx.Clear(); _refSign.Clear(); _refLabel.Clear();
                int idx = 1;   // primary = 0; added series start at 1
                foreach (var pair in new[] { new { Sym = Ref1Symbol, Inv = Ref1Inverse },
                                             new { Sym = Ref2Symbol, Inv = Ref2Inverse } })
                {
                    string sym = (pair.Sym ?? "").Trim();
                    if (sym.Length == 0) continue;
                    // v1.0.1 (2026-07-23) — GUARDED. An unresolvable symbol (ZN is absent on legacy-node) threw
                    // "only accepts valid instrument" out of State.Configure, which aborted the WHOLE
                    // OnStateChange: no DataLoaded, no SetIntermarketState, IMKT silently absent FOREVER with
                    // nothing anywhere saying so. Third occurrence of the eye-never-loads pattern — a crashed
                    // sensor must be indistinguishable from nothing, not from a quiet one.
                    // ⚠ idx must NOT advance on failure, or every surviving ref misaligns with its BarsArray index.
                    try
                    {
                        AddDataSeries(sym, BarsPeriodType.Minute, RefMinutes);
                    }
                    catch (Exception ex)
                    {
                        try { SentinelCore.Log("Intermarket", string.Format(
                            "ref '{0}' DISABLED — AddDataSeries failed: {1}", sym, ex.Message)); } catch (Exception _sx) { SentinelCore.Swallow("Intermarket.OnStateChange", _sx); }
                        continue;
                    }
                    _refIdx.Add(idx); _refSign.Add(pair.Inv ? -1 : 1); _refLabel.Add(sym.Split(' ')[0]);
                    idx++;
                }
            }
            else if (State == State.DataLoaded)
            {
                if (!ShowIndicatorLabel) Name = string.Empty;
            }
            else if (State == State.Terminated)
            {
                if (_sp != null) { try { _sp.Dispose(); } catch (Exception _sx) { SentinelCore.Swallow("Intermarket.OnStateChange", _sx); } _sp = null; }
                try { SentinelSkin.CardLayout.Release(this); } catch (Exception _sx) { SentinelCore.Swallow("Intermarket.OnStateChange", _sx); }
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0) return;   // compute on the primary series
            if (Instrument == null || Instrument.MasterInstrument == null) return;

            int warmup = Math.Max(CciPeriod, AtrPeriod);
            double num = 0; int warm = 0;
            var sb = new StringBuilder();

            for (int k = 0; k < _refIdx.Count; k++)
            {
                int si = _refIdx[k];
                if (si >= CurrentBars.Length || CurrentBars[si] <= warmup) continue;   // ref not warm yet
                int dir;
                try
                {
                    var st = SentinelTrend_v1_0_0(BarsArray[si], CciPeriod, AtrPeriod, AtrMult, CciThreshold,
                                                  false, 8, false, false, 12, false, SentinelCardCorner.TopRight,
                                                  false, 90.0, false, false);
                    dir = (int)st.Direction[0];
                }
                catch { continue; }

                int adj = dir * _refSign[k];   // apply the correlation polarity
                num += adj; warm++;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(_refLabel[k]).Append(adj > 0 ? ":+" : (adj < 0 ? ":-" : ":0"));
            }

            double score = warm > 0 ? num / warm : 0.0;
            int lean = score > BiasDeadband ? 1 : (score < -BiasDeadband ? -1 : 0);

            _lean = lean; _score = score; _refCount = warm; _refs = sb.ToString();
            _hasData = warm > 0;

            if (PublishState && warm > 0)
            {
                try
                {
                    SentinelCore.SetIntermarketState(new SentinelCore.IntermarketState
                    {
                        Instrument = Instrument.MasterInstrument.Name,
                        Lean = lean, Score = score, RefCount = warm, Refs = _refs, Source = "Intermarket"
                    });
                }
                catch (Exception _sx) { SentinelCore.Swallow("Intermarket.OnBarUpdate", _sx); }
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
                    _sp.Text("INTERMARKET", rw.Left + 16f, rw.Top, rw.Width - 20f, 16f, SentinelSkin.CInk, 11f, true);
                    _sp.Text("loading refs…", rw.Left, rw.Top + 26f, rw.Width, 14f, SentinelSkin.CMute, 10.5f);
                    _sp.End();
                    return;
                }

                var trail   = SharpDX.DirectWrite.TextAlignment.Trailing;
                var leanCol = _lean > 0 ? SentinelSkin.CUp : (_lean < 0 ? SentinelSkin.CDown : SentinelSkin.CMute);
                var r = _sp.Card(slot.X, slot.Y, cw, ch, _lean != 0 ? SentinelSkin.CAccent : SentinelSkin.CLine);

                // header — dot, title, lean pill
                _sp.Dot(r.Left + 5f, r.Top + 8f, _lean != 0 ? leanCol : SentinelSkin.CMute, _lean != 0);
                _sp.Text("INTERMARKET", r.Left + 16f, r.Top, r.Width - 76f, 16f, SentinelSkin.CInk, 11f, true);
                _sp.Pill(_lean > 0 ? "LONG" : (_lean < 0 ? "SHORT" : "FLAT"), r.Right, r.Top - 1f, leanCol);

                // hero — net lean + score
                _sp.Text("LEAN", r.Left, r.Top + 24f, 120f, 12f, SentinelSkin.CMute, 8.5f, true);
                _sp.Text(_lean > 0 ? "BULL" : (_lean < 0 ? "BEAR" : "NEUTRAL"), r.Left, r.Top + 34f, r.Width, 24f, leanCol, 17f, false);
                _sp.Text((_score >= 0 ? "+" : "") + _score.ToString("0.00"), r.Left, r.Top + 26f, r.Width, 16f, SentinelSkin.CInk2, 11f, true, trail);

                // meter — |score|
                _sp.Track(r.Left, r.Top + 60f, r.Width, (float)Math.Min(1.0, Math.Abs(_score)), leanCol);

                // footer — per-ref summary (sign already correlation-adjusted)
                _sp.Divider(r.Left, r.Top + 78f, r.Right);
                _sp.Text(_refCount + " ref" + (_refCount == 1 ? "" : "s") + "  " + _refs, r.Left, r.Top + 84f, r.Width, 14f, SentinelSkin.CInk2, 9.5f);

                _sp.End();
            }
            catch (Exception _sx) { SentinelCore.Swallow("Intermarket.OnRender", _sx); }
        }

        #region Properties
        [NinjaScriptProperty]
        [Display(Name = "Ref 1 Symbol", Description = "First correlated instrument (e.g. ZN for gold). Master name or full contract; blank = disabled.", Order = 1, GroupName = "References")]
        public string Ref1Symbol { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Ref 1 Inverse", Description = "OFF = positive correlation (ref up ⇒ this instrument up, e.g. bonds↑⇒gold↑). ON = negative correlation.", Order = 2, GroupName = "References")]
        public bool Ref1Inverse { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Ref 2 Symbol", Description = "Second correlated instrument (e.g. ZB); blank = disabled.", Order = 3, GroupName = "References")]
        public string Ref2Symbol { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Ref 2 Inverse", Description = "OFF = positive correlation. ON = negative correlation.", Order = 4, GroupName = "References")]
        public bool Ref2Inverse { get; set; }

        [Range(1, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Ref Timeframe (min)", Description = "Add each reference instrument at this minute timeframe (higher = less macro noise).", Order = 5, GroupName = "References")]
        public int RefMinutes { get; set; }

        [Range(1, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "CCI Period", Description = "SentinelTrend CCI period (per-ref trend, hosted).", Order = 10, GroupName = "Trend (SentinelTrend)")]
        public int CciPeriod { get; set; }

        [Range(1, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "ATR Period", Description = "SentinelTrend ATR period (per-ref trend, hosted).", Order = 11, GroupName = "Trend (SentinelTrend)")]
        public int AtrPeriod { get; set; }

        [Range(0.00001, double.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "ATR Multiplier", Description = "SentinelTrend ATR band multiplier (per-ref trend, hosted).", Order = 12, GroupName = "Trend (SentinelTrend)")]
        public double AtrMult { get; set; }

        [Range(0.0, double.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "CCI Hysteresis Threshold", Description = "SentinelTrend CCI deadband half-width (per-ref trend, hosted).", Order = 13, GroupName = "Trend (SentinelTrend)")]
        public double CciThreshold { get; set; }

        [Range(0.0, 1.0)]
        [NinjaScriptProperty]
        [Display(Name = "Bias Deadband", Description = "|Score| must exceed this to declare a directional lean (else NEUTRAL).", Order = 14, GroupName = "Trend (SentinelTrend)")]
        public double BiasDeadband { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Publish to Sentinel", Description = "Publish as SentinelCore.IntermarketState so the Council can fuse the intermarket lean. Needs SentinelCore ≥ v1.12.0.", Order = 20, GroupName = "Sentinel")]
        public bool PublishState { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Card", Order = 21, GroupName = "Sentinel")]
        public bool ShowCard { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Card Corner", Description = "Which chart corner the Sentinel card docks to. Cards in the same corner auto-stack.", Order = 22, GroupName = "Sentinel")]
        public SentinelCardCorner CardCorner { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show indicator label", Description = "Show NinjaTrader's chart name label. Sentinel default = OFF (clean chart); turn on to restore it.", GroupName = "Sentinel", Order = 100)]
        public bool ShowIndicatorLabel { get; set; }
        #endregion
    }
}
