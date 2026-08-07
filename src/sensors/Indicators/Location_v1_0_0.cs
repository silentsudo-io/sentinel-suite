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
using NinjaTrader.NinjaScript.AddOns.Sentinel;   // SentinelSkin + SentinelCore (LevelState seam) + SentinelCardCorner
#endregion

// ═════════════════════════════════════════════════════════════════════════════
//  Location — the Sentinel STRUCTURAL-LEVELS axis ("where are we?")         |   Version v1.0.0
//  File: Location_v1_0_0.cs   |   namespace …Indicators.Sentinel   |   display Name "Location"
//
//  ⚠ NO ORDERS — read-only advisory indicator. Safe to run anywhere.
//
//  WHAT THIS IS — the THIRD orthogonal axis feeding the Council (Docs/ROADMAP.md · memory
//  signal-axes-plan). Every other sensor answers "what is price doing"; NONE answered "WHERE is it doing
//  it." A breakout into prior-day-high / the session VWAP is a different trade than one in open air.
//  Location computes the key structural levels and publishes them + the NEAREST level (ATR-normalized
//  distance) so the Council can damp a signal that would run straight into a wall of memory.
//
//  THE STATE (SentinelCore.LevelState, SentinelCore ≥ v1.10.0):
//    VWAP + volume-weighted std bands · prior-day H/L · opening range · initial balance · session H/L ·
//    NearestPrice/NearestName/NearestDistTicks(signed)/NearestDistAtr · VwapSide.
//    (Volume-profile POC/VAH/VAL is a future v1.1 add — it needs a volume-by-price histogram.)
//
//  SENTINEL WIRING (Docs/SENTINEL_DESIGN_SYSTEM.md §6c/§6d):
//    • PUBLISH: SetLevelState(...) each update (default ON). No plots (context consumed via the seam).
//    • Prior-day H/L from an added daily series; VWAP/OR/IB/session H-L from the primary series with a
//      SessionIterator/IsFirstBarOfSession reset. VWAP includes the live forming bar (volume-weighted std).
//    • A SentinelSkin.Painter glass card + Sentinel palette + label remover.
//
//  CHANGELOG
//    v1.0.0 (2026-07-07) — initial: VWAP+bands / PDH-PDL / OR / IB / session H-L + nearest-level
//             (ATR-normalized), published as SentinelCore.LevelState; Sentinel card. Third Council axis.
// ═════════════════════════════════════════════════════════════════════════════
namespace NinjaTrader.NinjaScript.Indicators.Sentinel
{
    public class Location_v1_0_0 : Indicator
    {
        private SentinelSkin.Painter _sp;
        private bool _hasData;
        private int  _lastBar = -1;
        private DateTime _sessionOpen = DateTime.MinValue;
        // session VWAP accumulators (COMPLETED bars this session; the live bar is added each compute)
        private double _sumV, _sumVP, _sumVP2;
        private double _orh = double.NaN, _orl = double.NaN, _ibh = double.NaN, _ibl = double.NaN;
        private double _sh = double.NaN, _sl = double.NaN;
        // cached published snapshot for the card
        private SentinelCore.LevelState _cache;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description              = "Sentinel structural-levels axis — publishes VWAP+bands, prior-day H/L, opening range, initial balance, session H/L, and the nearest level (ATR-normalized) as SentinelCore.LevelState so the Council knows WHERE price is.";
                Name                     = "Sentinel Location v1.0.0";
                Calculate                = Calculate.OnPriceChange;
                IsOverlay                = true;
                IsSuspendedWhileInactive = true;
                DisplayInDataBox         = false;
                DrawOnPricePanel         = true;

                AtrPeriod   = 14;
                OrMinutes   = 30;    // opening-range window
                IbMinutes   = 60;    // initial-balance window
                VwapBandK   = 1.0;   // band width in volume-weighted std

                PublishState = true;
                ShowCard     = true;
                CardCorner   = SentinelCardCorner.TopRight;
                ShowIndicatorLabel = false;
            }
            else if (State == State.Configure)
            {
                // v1.0.1 (2026-07-23) — GUARDED (same class as Intermarket/Eye). If the daily row fails, Location
                // must lose PDH/PDL only — not vanish. The consumer at Highs[1][1] is already try/caught, so a
                // missing series degrades to "no prior-day levels" instead of killing the whole indicator.
                try
                {
                    AddDataSeries(BarsPeriodType.Day, 1);   // series index 1 = daily (prior-day H/L)
                }
                catch (Exception ex)
                {
                    try { SentinelCore.Log("Location", string.Format(
                        "daily row DISABLED — AddDataSeries failed: {0} (PDH/PDL unavailable)", ex.Message)); } catch (Exception _sx) { SentinelCore.Swallow("Location.OnStateChange", _sx); }
                }
            }
            else if (State == State.DataLoaded)
            {
                if (!ShowIndicatorLabel) Name = string.Empty;
            }
            else if (State == State.Terminated)
            {
                if (_sp != null) { try { _sp.Dispose(); } catch (Exception _sx) { SentinelCore.Swallow("Location.OnStateChange", _sx); } _sp = null; }
                try { SentinelSkin.CardLayout.Release(this); } catch (Exception _sx) { SentinelCore.Swallow("Location.OnStateChange", _sx); }
                if (_scope != null) { try { SentinelCore.ClearLevelScope(_scope); } catch (Exception _sx) { SentinelCore.Swallow("Location.OnStateChange", _sx); } }
            }
        }

        // ── Sentinel scope (v1.20.0 — seam scope migration 1.4). OnPriceChange republishes per tick, so no heartbeat. ──
        private string _scope;
        /// <summary>This chart's SCOPE ("GC.69697v6x24") — instrument × primary bar type. Cached after first resolve.</summary>
        private string Scope()
        {
            if (_scope == null)
            {
                try { if (Instrument != null && BarsPeriod != null) _scope = SentinelCore.ScopeOf(Instrument, BarsPeriod); } catch (Exception _sx) { SentinelCore.Swallow("Location.Scope", _sx); }
            }
            return _scope;
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0) return;   // compute only on the primary series
            if (Instrument == null || Instrument.MasterInstrument == null) return;
            if (CurrentBar < AtrPeriod) return;

            // ── per-new-bar session bookkeeping ──
            if (CurrentBar != _lastBar)
            {
                _lastBar = CurrentBar;
                if (Bars.IsFirstBarOfSession)
                {
                    _sumV = _sumVP = _sumVP2 = 0;
                    _orh = _orl = _ibh = _ibl = double.NaN;
                    _sh = _sl = double.NaN;
                    _sessionOpen = Times[0][0];
                }
                else if (CurrentBar >= 1)
                {
                    // fold the just-completed bar into the session VWAP accumulators
                    double v = Volume[1], typ = (High[1] + Low[1] + Close[1]) / 3.0;
                    _sumV += v; _sumVP += v * typ; _sumVP2 += v * typ * typ;
                }
            }
            if (_sessionOpen == DateTime.MinValue) _sessionOpen = Times[0][0];

            // ── VWAP incl. the live forming bar ──
            double lv = Volume[0], ltyp = (High[0] + Low[0] + Close[0]) / 3.0;
            double tV = _sumV + lv, tVP = _sumVP + lv * ltyp, tVP2 = _sumVP2 + lv * ltyp * ltyp;
            double vwap = tV > 0 ? tVP / tV : Close[0];
            double var  = tV > 0 ? Math.Max(0.0, tVP2 / tV - vwap * vwap) : 0.0;
            double std  = Math.Sqrt(var);
            double vwUp = vwap + VwapBandK * std, vwDn = vwap - VwapBandK * std;

            // ── OR / IB / session H-L (live) ──
            double mins = (Times[0][0] - _sessionOpen).TotalMinutes;
            _sh = double.IsNaN(_sh) ? High[0] : Math.Max(_sh, High[0]);
            _sl = double.IsNaN(_sl) ? Low[0]  : Math.Min(_sl, Low[0]);
            if (mins <= OrMinutes) { _orh = double.IsNaN(_orh) ? High[0] : Math.Max(_orh, High[0]); _orl = double.IsNaN(_orl) ? Low[0] : Math.Min(_orl, Low[0]); }
            if (mins <= IbMinutes) { _ibh = double.IsNaN(_ibh) ? High[0] : Math.Max(_ibh, High[0]); _ibl = double.IsNaN(_ibl) ? Low[0] : Math.Min(_ibl, Low[0]); }

            // ── prior-day H/L from the daily series (index 1) ──
            double pdh = double.NaN, pdl = double.NaN;
            if (CurrentBars.Length > 1 && CurrentBars[1] >= 1)
            {
                try { pdh = Highs[1][1]; pdl = Lows[1][1]; } catch (Exception _sx) { SentinelCore.Swallow("Location.OnBarUpdate", _sx); }
            }

            double atr = ATR(AtrPeriod)[0];
            double px  = Close[0];

            // ── nearest SIGNIFICANT level (VWAP/bands/PDH/PDL/OR/IB — not the live session extremes) ──
            string nName = null; double nPrice = double.NaN, nAbs = double.MaxValue;
            AddCand(ref nName, ref nPrice, ref nAbs, px, "VWAP", vwap);
            AddCand(ref nName, ref nPrice, ref nAbs, px, "VWAP+", vwUp);
            AddCand(ref nName, ref nPrice, ref nAbs, px, "VWAP-", vwDn);
            AddCand(ref nName, ref nPrice, ref nAbs, px, "PDH", pdh);
            AddCand(ref nName, ref nPrice, ref nAbs, px, "PDL", pdl);
            AddCand(ref nName, ref nPrice, ref nAbs, px, "ORH", _orh);
            AddCand(ref nName, ref nPrice, ref nAbs, px, "ORL", _orl);
            AddCand(ref nName, ref nPrice, ref nAbs, px, "IBH", _ibh);
            AddCand(ref nName, ref nPrice, ref nAbs, px, "IBL", _ibl);

            var s = new SentinelCore.LevelState
            {
                Scope = Scope(), Bartype = SentinelCore.BarTag(BarsPeriod),
                Instrument = Instrument.MasterInstrument.Name,
                Vwap = vwap, VwapUpper = vwUp, VwapLower = vwDn,
                Pdh = pdh, Pdl = pdl, Orh = _orh, Orl = _orl, Ibh = _ibh, Ibl = _ibl,
                SessHigh = _sh, SessLow = _sl,
                NearestPrice = nPrice, NearestName = nName ?? "",
                NearestDistTicks = (nName != null && TickSize > 0) ? (px - nPrice) / TickSize : double.NaN,
                NearestDistAtr   = (nName != null && atr > 0) ? Math.Abs(px - nPrice) / atr : double.NaN,
                VwapSide = px > vwap ? 1 : (px < vwap ? -1 : 0),
                Source = "Location"
            };
            _cache = s;
            _hasData = true;

            if (PublishState) { try { SentinelCore.SetLevelState(s); } catch (Exception _sx) { SentinelCore.Swallow("Location.OnBarUpdate", _sx); } }
        }

        private static void AddCand(ref string name, ref double price, ref double absDist, double px, string n, double lvl)
        {
            if (double.IsNaN(lvl)) return;
            double d = Math.Abs(px - lvl);
            if (d < absDist) { absDist = d; price = lvl; name = n; }
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

                const float cw = 238f, ch = 168f;
                var slot = SentinelSkin.CardLayout.Place(this, ChartPanel,
                    ChartPanel.X, ChartPanel.Y, ChartPanel.W, ChartPanel.H, CardCorner, cw, ch);

                if (!_hasData || _cache == null)
                {
                    var rw = _sp.Card(slot.X, slot.Y, cw, ch, SentinelSkin.CLine);
                    _sp.Dot(rw.Left + 5f, rw.Top + 8f, SentinelSkin.CMute, false);
                    _sp.Text("LOCATION", rw.Left + 16f, rw.Top, rw.Width - 20f, 16f, SentinelSkin.CInk, 11f, true);
                    _sp.Text("mapping levels…", rw.Left, rw.Top + 26f, rw.Width, 14f, SentinelSkin.CMute, 10.5f);
                    _sp.End();
                    return;
                }

                var c = _cache;
                var trail = SharpDX.DirectWrite.TextAlignment.Trailing;
                bool near = !double.IsNaN(c.NearestDistAtr) && c.NearestDistAtr <= 0.5;
                var col = near ? SentinelSkin.CWarn : SentinelSkin.CAccent;
                var r = _sp.Card(slot.X, slot.Y, cw, ch, near ? SentinelSkin.CWarn : SentinelSkin.CLine);

                // header — dot, title, VWAP side pill
                _sp.Dot(r.Left + 5f, r.Top + 8f, SentinelSkin.CAccent, true);
                _sp.Text("LOCATION", r.Left + 16f, r.Top, r.Width - 84f, 16f, SentinelSkin.CInk, 11f, true);
                _sp.Pill(c.VwapSide > 0 ? "↑VWAP" : (c.VwapSide < 0 ? "↓VWAP" : "VWAP"),
                         r.Right, r.Top - 1f, c.VwapSide >= 0 ? SentinelSkin.CUp : SentinelSkin.CDown);

                // hero — nearest level + signed distance
                _sp.Text("NEAREST", r.Left, r.Top + 24f, 120f, 12f, SentinelSkin.CMute, 8.5f, true);
                _sp.Text(string.IsNullOrEmpty(c.NearestName) ? "—" : c.NearestName, r.Left, r.Top + 34f, r.Width, 24f, col, 18f, false);
                if (!double.IsNaN(c.NearestDistTicks))
                {
                    string dt = (c.NearestDistTicks >= 0 ? "+" : "") + c.NearestDistTicks.ToString("0.0") + "t";
                    _sp.Text(dt, r.Left, r.Top + 26f, r.Width, 20f, col, 12f, true, trail);
                    _sp.Text(double.IsNaN(c.NearestDistAtr) ? "" : c.NearestDistAtr.ToString("0.00") + " atr", r.Left, r.Top + 44f, r.Width, 12f, SentinelSkin.CMute, 8.5f, false, trail);
                }

                // rows — VWAP + prior day
                _sp.Divider(r.Left, r.Top + 66f, r.Right);
                _sp.Text("VWAP", r.Left, r.Top + 72f, 90f, 14f, SentinelSkin.CMute, 9f, true);
                _sp.Text(c.Vwap.ToString("0.00"), r.Left, r.Top + 72f, r.Width, 14f, SentinelSkin.CInk2, 10f, false, trail);
                _sp.Text("PDH / PDL", r.Left, r.Top + 90f, 90f, 14f, SentinelSkin.CMute, 9f, true);
                _sp.Text((double.IsNaN(c.Pdh) ? "—" : c.Pdh.ToString("0.00")) + " / " + (double.IsNaN(c.Pdl) ? "—" : c.Pdl.ToString("0.00")),
                         r.Left, r.Top + 90f, r.Width, 14f, SentinelSkin.CInk2, 10f, false, trail);
                _sp.Text("OR H / L", r.Left, r.Top + 108f, 90f, 14f, SentinelSkin.CMute, 9f, true);
                _sp.Text((double.IsNaN(c.Orh) ? "—" : c.Orh.ToString("0.00")) + " / " + (double.IsNaN(c.Orl) ? "—" : c.Orl.ToString("0.00")),
                         r.Left, r.Top + 108f, r.Width, 14f, SentinelSkin.CInk2, 10f, false, trail);

                _sp.End();
            }
            catch (Exception _sx) { SentinelCore.Swallow("Location.OnRender", _sx); }
        }

        #region Properties
        [Range(1, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "ATR Period", Description = "ATR used to scale-normalize the nearest-level distance.", Order = 1, GroupName = "Parameters")]
        public int AtrPeriod { get; set; }

        [Range(1, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Opening-Range Minutes", Description = "Minutes after session open that define the opening range.", Order = 2, GroupName = "Parameters")]
        public int OrMinutes { get; set; }

        [Range(1, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Initial-Balance Minutes", Description = "Minutes after session open that define the initial balance.", Order = 3, GroupName = "Parameters")]
        public int IbMinutes { get; set; }

        [Range(0.0, double.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "VWAP Band K", Description = "VWAP band width in volume-weighted standard deviations.", Order = 4, GroupName = "Parameters")]
        public double VwapBandK { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Publish to Sentinel", Description = "Publish as SentinelCore.LevelState so the Council/strategies can consult location. Needs SentinelCore ≥ v1.10.0.", Order = 10, GroupName = "Sentinel")]
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
