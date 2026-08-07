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
using NinjaTrader.NinjaScript.AddOns.Sentinel;   // SentinelSkin + SentinelCore (ParticipationState seam) + SentinelCardCorner
#endregion

// ═════════════════════════════════════════════════════════════════════════════
//  Participation — the Sentinel RELATIVE-VOLUME modulator                   |   Version v1.0.0
//  File: Participation_v1_0_0.cs   |   namespace …Indicators.Sentinel   |   display Name "Participation"
//
//  ⚠ NO ORDERS — read-only advisory indicator. Safe to run anywhere.
//
//  WHAT THIS IS — the SECOND ORTHOGONAL axis feeding the Council (Docs/ROADMAP.md · memory
//  signal-axes-plan). The Council's price-derived voters (Trend/ADX/CCI/Envelope/Brick) all echo the same
//  OHLC; VOLUME carries information price alone does not — "is this move BACKED by participation, or is it
//  drifting on air?" Participation publishes relative volume so the Council can MODULATE: a move on light
//  volume gets its conviction damped; it can only PENALISE an unbacked move, never inflate a backed one.
//
//  THE STATE (SentinelCore.ParticipationState, SentinelCore ≥ v1.9.0):
//    • Rvol        relative volume vs a typical (1.0 = normal, >1 heavy, <1 light)
//    • VolZ        volume z-score vs the recent distribution
//    • Climax      VolZ ≥ ClimaxZ (blow-off participation)
//    • DryUp       Rvol ≤ DryUpRvol (participation vacuum)
//    • TypicalVol  the typical volume used (diagnostic)
//
//  RVOL NORMALIZATION (default = BAR-normalized, universal):
//    Rvol = last COMPLETED bar volume ÷ SMA(Volume, VolStatPeriod). This works on ANY bar type — critical
//    because the suite runs on tick/renko/brick bars where clock-time buckets are meaningless (every bar a
//    different timestamp). OPTIONAL: UseTimeOfDayRvol normalizes against the typical volume at the SAME
//    minute-of-day over prior bars — the cleaner "orthogonal" RVOL, but only sensible on TIME-based charts.
//    Computed on the just-CLOSED bar (barsAgo=1) for stability, republished each tick to stay fresh.
//
//  SENTINEL WIRING (Docs/SENTINEL_DESIGN_SYSTEM.md §6c/§6d):
//    • PUBLISH: SetParticipationState(...) each update (default ON). No plots (a modulator).
//    • A SentinelSkin.Painter glass card + Sentinel palette + label remover.
//
//  CHANGELOG
//    v1.0.0 (2026-07-07) — initial: bar-normalized RVOL (+ optional time-of-day) + volume z-score +
//             climax/dry-up, published as SentinelCore.ParticipationState; Sentinel card/palette/label-
//             remover. Second orthogonal Council axis. (Cumulative-delta divergence = a future v1.1 add.)
// ═════════════════════════════════════════════════════════════════════════════
namespace NinjaTrader.NinjaScript.Indicators.Sentinel
{
    public class Participation_v1_0_0 : Indicator
    {
        private SentinelSkin.Painter _sp;
        private bool _hasData;
        private double _rvol = 1.0, _volZ, _typical;
        private bool   _climax, _dryUp;
        private int    _lastBar = -1;
        private bool   _lastClimax, _lastDryUp;
        // time-of-day RVOL buckets (only used when UseTimeOfDayRvol)
        private readonly Dictionary<int, double> _todSum = new Dictionary<int, double>();
        private readonly Dictionary<int, int>    _todCnt = new Dictionary<int, int>();

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description              = "Sentinel relative-volume modulator — publishes per-instrument RVOL + volume z-score + climax/dry-up as SentinelCore.ParticipationState so the Council can damp conviction on moves that aren't backed by participation.";
                Name                     = "Sentinel Participation v1.0.0";
                Calculate                = Calculate.OnPriceChange;
                IsOverlay                = true;
                IsSuspendedWhileInactive = true;
                DisplayInDataBox         = false;
                DrawOnPricePanel         = true;

                VolStatPeriod    = 20;      // SMA/StdDev window + bar-normalization base
                UseTimeOfDayRvol = false;   // OFF = bar-normalized (universal). ON = time-of-day (minute charts only)
                MinBucketSamples = 3;       // min prior samples before trusting a time-of-day bucket
                DryUpRvol        = 0.6;      // Rvol ≤ this = dry-up (participation vacuum)
                ClimaxZ          = 2.0;      // VolZ ≥ this = climax (blow-off)

                PublishState   = true;
                LogChanges     = true;
                ShowCard       = true;
                CardCorner     = SentinelCardCorner.TopRight;
                ShowIndicatorLabel = false;
            }
            else if (State == State.DataLoaded)
            {
                if (!ShowIndicatorLabel) Name = string.Empty;
            }
            else if (State == State.Terminated)
            {
                if (_sp != null) { try { _sp.Dispose(); } catch (Exception _sx) { SentinelCore.Swallow("Participation.OnStateChange", _sx); } _sp = null; }
                try { SentinelSkin.CardLayout.Release(this); } catch (Exception _sx) { SentinelCore.Swallow("Participation.OnStateChange", _sx); }
                if (_scope != null) { try { SentinelCore.ClearParticipationScope(_scope); } catch (Exception _sx) { SentinelCore.Swallow("Participation.OnStateChange", _sx); } }
            }
        }

        // ── Sentinel scope (v1.21.0 — seam scope migration 1.4 batch 4). RVOL is bar-type-dependent → SCOPE-keyed.
        //    OnPriceChange republishes per tick, so no heartbeat. ──
        private string _scope;
        /// <summary>This chart's SCOPE ("GC.69697v6x24") — instrument × primary bar type. Cached after first resolve.</summary>
        private string Scope()
        {
            if (_scope == null)
            {
                try { if (Instrument != null && BarsPeriod != null) _scope = SentinelCore.ScopeOf(Instrument, BarsPeriod); } catch (Exception _sx) { SentinelCore.Swallow("Participation.Scope", _sx); }
            }
            return _scope;
        }

        protected override void OnBarUpdate()
        {
            if (Instrument == null || Instrument.MasterInstrument == null) return;
            if (CurrentBar < VolStatPeriod + 1) return;
            string inst = Instrument.MasterInstrument.Name;

            // compute once per NEW bar (on the just-completed bar [1]); republish each tick to stay fresh
            if (CurrentBar != _lastBar)
            {
                _lastBar = CurrentBar;
                double vol   = Volume[1];
                double sma   = SMA(Volume, VolStatPeriod)[1];
                double sd    = StdDev(Volume, VolStatPeriod)[1];

                double typical = sma;
                if (UseTimeOfDayRvol)
                {
                    int key = (int)Time[1].TimeOfDay.TotalMinutes;
                    double bsum; int bcnt;
                    bool haveBucket = _todCnt.TryGetValue(key, out bcnt) && bcnt >= MinBucketSamples
                                      && _todSum.TryGetValue(key, out bsum) && bsum > 0;
                    if (haveBucket) typical = _todSum[key] / _todCnt[key];   // prior samples only (added below)
                    // accumulate this bar into its bucket AFTER using the prior mean
                    _todSum[key] = (_todSum.ContainsKey(key) ? _todSum[key] : 0) + vol;
                    _todCnt[key] = (_todCnt.ContainsKey(key) ? _todCnt[key] : 0) + 1;
                }

                _typical = typical;
                _rvol    = typical > 0 ? vol / typical : 1.0;
                _volZ    = sd > 0 ? (vol - sma) / sd : 0.0;
                _climax  = _volZ >= ClimaxZ;
                _dryUp   = _rvol <= DryUpRvol;
                _hasData = true;

                // v1.0.1 (2026-07-25) — REALTIME-GATE THE LOG. Measured: on a 15-chart workspace load this
                // emitted 79,603 lines in ~14 minutes, because climax/dryUp flip bar-to-bar through a
                // historical rebuild and every flip logged. sentinel.log rotates at 5 MB keeping ONE
                // generation, so this ALONE rotated it twice and destroyed every other diagnostic in it —
                // twice in one night, while the bar-type seam bug was being chased. Every other publisher in
                // the suite already gates its logging on realtime; this one did not. The STATE PUBLISH below
                // is deliberately untouched: consumers still get historical values, only the log is quiet.
                if (LogChanges && State == State.Realtime && (_climax != _lastClimax || _dryUp != _lastDryUp))
                {
                    _lastClimax = _climax; _lastDryUp = _dryUp;
                    try
                    {
                        SentinelCore.Log("Participation", inst + " rvol=" + _rvol.ToString("0.00") +
                            " z=" + _volZ.ToString("0.0") + (_climax ? " CLIMAX" : "") + (_dryUp ? " DRY-UP" : ""));
                    }
                    catch (Exception _sx) { SentinelCore.Swallow("Participation.OnBarUpdate", _sx); }
                }
            }

            if (PublishState)
            {
                try { SentinelCore.SetParticipationState(Scope(), SentinelCore.BarTag(BarsPeriod), inst, _rvol, _volZ, _climax, _dryUp, _typical, "Participation"); }
                catch (Exception _sx) { SentinelCore.Swallow("Participation.OnBarUpdate", _sx); }
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

                const float cw = 228f, ch = 138f;
                var slot = SentinelSkin.CardLayout.Place(this, ChartPanel,
                    ChartPanel.X, ChartPanel.Y, ChartPanel.W, ChartPanel.H, CardCorner, cw, ch);

                if (!_hasData)
                {
                    var rw = _sp.Card(slot.X, slot.Y, cw, ch, SentinelSkin.CLine);
                    _sp.Dot(rw.Left + 5f, rw.Top + 8f, SentinelSkin.CMute, false);
                    _sp.Text("PARTICIPATION", rw.Left + 16f, rw.Top, rw.Width - 20f, 16f, SentinelSkin.CInk, 11f, true);
                    _sp.Text("warming up…", rw.Left, rw.Top + 26f, rw.Width, 14f, SentinelSkin.CMute, 10.5f);
                    _sp.End();
                    return;
                }

                var trail  = SharpDX.DirectWrite.TextAlignment.Trailing;
                bool backed = _rvol >= 1.0;
                var col = _climax ? SentinelSkin.CWarn : (_dryUp ? SentinelSkin.CMute : (backed ? SentinelSkin.CAccent : SentinelSkin.CInk2));
                var r = _sp.Card(slot.X, slot.Y, cw, ch, backed ? SentinelSkin.CAccent : SentinelSkin.CLine);

                // header — dot, title, backed/light pill
                _sp.Dot(r.Left + 5f, r.Top + 8f, col, backed);
                _sp.Text("PARTICIPATION", r.Left + 16f, r.Top, r.Width - 74f, 16f, SentinelSkin.CInk, 11f, true);
                _sp.Pill(backed ? "BACKED" : "LIGHT", r.Right, r.Top - 1f, backed ? SentinelSkin.CAccent : SentinelSkin.CMute);

                // hero — RVOL
                _sp.Text("REL VOLUME", r.Left, r.Top + 24f, 120f, 12f, SentinelSkin.CMute, 8.5f, true);
                _sp.Text("×" + _rvol.ToString("0.00"), r.Left, r.Top + 34f, r.Width, 24f, col, 18f, false);
                _sp.Text("z " + (_volZ >= 0 ? "+" : "") + _volZ.ToString("0.0"), r.Left, r.Top + 26f, r.Width, 16f, SentinelSkin.CInk2, 11f, true, trail);

                // track — rvol scaled so 1.0 = half, 2.0+ = full
                _sp.Track(r.Left, r.Top + 60f, r.Width, (float)Math.Max(0.0, Math.Min(1.0, _rvol / 2.0)), col);

                // footer — climax / dry-up / backed state
                _sp.Divider(r.Left, r.Top + 78f, r.Right);
                string tag = _climax ? "CLIMAX — blow-off volume" : (_dryUp ? "DRY-UP — thin participation" : (backed ? "backed by volume" : "below-normal volume"));
                _sp.Text(tag, r.Left, r.Top + 84f, r.Width, 14f, col, 10f, _climax || _dryUp);

                _sp.End();
            }
            catch (Exception _sx) { SentinelCore.Swallow("Participation.OnRender", _sx); }
        }

        #region Properties
        [Range(2, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Vol Stat Period", Description = "Window for the SMA/StdDev of volume (the bar-normalized RVOL base + z-score).", Order = 1, GroupName = "Parameters")]
        public int VolStatPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Time-of-Day RVOL", Description = "Normalize RVOL against the typical volume at the same minute-of-day (cleaner, but ONLY sensible on time-based charts). OFF = bar-normalized (works on any bar type).", Order = 2, GroupName = "Parameters")]
        public bool UseTimeOfDayRvol { get; set; }

        [Range(1, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Min Bucket Samples", Description = "Time-of-day RVOL: minimum prior samples at a minute-of-day before trusting that bucket (else falls back to the bar-normalized average).", Order = 3, GroupName = "Parameters")]
        public int MinBucketSamples { get; set; }

        [Range(0.0, 1.0)]
        [NinjaScriptProperty]
        [Display(Name = "Dry-Up RVOL", Description = "Rvol at or below this flags a dry-up (participation vacuum).", Order = 4, GroupName = "Parameters")]
        public double DryUpRvol { get; set; }

        [Range(0.0, double.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Climax Z", Description = "Volume z-score at or above this flags a climax (blow-off participation).", Order = 5, GroupName = "Parameters")]
        public double ClimaxZ { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Publish to Sentinel", Description = "Publish as SentinelCore.ParticipationState so the Council/strategies can modulate on it. Needs SentinelCore ≥ v1.9.0.", Order = 10, GroupName = "Sentinel")]
        public bool PublishState { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Log Changes", Description = "Write climax / dry-up transitions to sentinel.log.", Order = 11, GroupName = "Sentinel")]
        public bool LogChanges { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Card", Order = 12, GroupName = "Sentinel")]
        public bool ShowCard { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Card Corner", Description = "Which chart corner the Sentinel card docks to. Cards in the same corner auto-stack.", Order = 13, GroupName = "Sentinel")]
        public SentinelCardCorner CardCorner { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show indicator label", Description = "Show NinjaTrader's chart name label. Sentinel default = OFF (clean chart); turn on to restore it.", GroupName = "Sentinel", Order = 100)]
        public bool ShowIndicatorLabel { get; set; }
        #endregion
    }
}
