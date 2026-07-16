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
using NinjaTrader.NinjaScript.AddOns.Sentinel;   // SentinelSkin (glass card) + SentinelCore (trend/ADX seams) + SentinelCardCorner
using NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors;
#endregion

// ═════════════════════════════════════════════════════════════════════════════
//  SentinelTrend — the corrected, unified ATR/CCI trailing-line indicator   |   Version v1.0.0
//  File: SentinelTrend_v1_0_0.cs   |   namespace …Indicators.Sentinel
//
//  ⚠ NO ORDERS — read-only trend/direction indicator. Safe to run anywhere.
//
//  WHAT THIS IS — the definitive replacement for the old TrendMagic family (TrendMagic /
//  TrendMagicOscillator / TrendMagicSignalMod + the TMEntry/TMEntry50/TMXEntryExit/TMSquared/
//  TripleTM strategies). Same idea — an ATR band trailing line whose side is chosen by CCI —
//  but it FIXES the four flaws that made the originals whipsaw, and homes into the Sentinel suite.
//
//  WHY IT IS SUPERIOR (vs the original TrendMagic algorithm):
//    1. TRUE ATR.  The originals call ATR(Close, n) — feeding the CLOSE SERIES into ATR, which
//       collapses it to smoothed close-to-close change and IGNORES the high-low span + gaps, so it
//       SYSTEMATICALLY UNDERSTATES volatility. This uses ATR(n) on the bar — real True Range.
//    2. CCI HYSTERESIS.  The originals flip the trend on a naked CCI zero-cross (cci >= 0) — laggy and
//       noisy, so in chop the line teleports between the up-floor and down-ceiling every few bars. This
//       uses a DEADBAND: flip up only when CCI > +CciThreshold, down only when CCI < -CciThreshold,
//       otherwise HOLD. That single change kills most of the whipsaw.
//    3. DOT RENDER.  On a regime flip the line jumps discontinuously; drawn as PlotStyle.Line the
//       originals streak a vertical connector across the jump (see memory ninjascript-plot-config-override).
//       This renders the trailing line as Dots.
//    4. SANE DEFAULTS.  The core TrendMagic default AtrMult = 0.01 makes the band ~1% of an already-
//       understated ATR, so the line hugs price and flips constantly. Default here = 1.5 (a real band).
//
//  SENTINEL WIRING (Docs/SENTINEL_DESIGN_SYSTEM.md):
//    • Direction (+1/-1/0) + Trend plots for strategy consumption (SentinelTrendStrategy consumes them,
//      exactly as the old strategies consumed TrendMagicSignalMod.Direction).
//    • CONSULT: optional ADX-regime filter — reads SentinelCore.GetAdxState so signal markers can require
//      "trend ON + bias agrees" (needs ADXPro publishing; SentinelCore ≥ v1.2.0). Fail-open when absent.
//    • PUBLISH: optional SentinelCore.SetTrendState(...) so GTrader21 / Eye / strategies can consult this
//      trend's direction + line + distance (needs SentinelCore ≥ v1.3.0).
//    • A SentinelSkin.Painter glass card (CardLayout-docked) + Sentinel palette + label remover.
//
//  CHANGELOG
//    v1.0.0b (2026-07-07) — PublishState now DEFAULTS ON so SentinelTrend feeds the Council's TrendState voter
//             out of the box. In-place patch (no rename); existing chart placements keep their serialized value.
//    v1.0.0a (2026-07-06) — default CardCorner TopLeft → TopRight (Sentinel house default: cards dock right).
//             In-place patch (no rename). Existing placements keep their serialized corner.
//    v1.0.0 — initial: true ATR, CCI hysteresis deadband, Dot render, ADX consult + TrendState publish,
//             Sentinel card/palette/label-remover. Supersedes the TrendMagic family (kept as fallbacks).
// ═════════════════════════════════════════════════════════════════════════════
namespace NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors
{
    public class SentinelTrend_v1_0_0 : Indicator
    {
        // cached card state — drawn in OnRender; OnBarUpdate just caches
        private SentinelSkin.Painter _sp;
        private bool   _warming = true;
        private int    _dir;               // -1 / 0 / 1
        private double _trendPrice, _distTicks, _cci;
        private int    _barsInTrend;
        private bool   _flipped, _adxAligned, _adxConsulted;
        private int    _lastFlipBar, _lastHistBar = -1;                  // tick-safe (Calculate.OnPriceChange)
        private readonly List<double> _distHist = new List<double>();   // ring for the sparkline
        private const int HistMax = 48;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                 = "Corrected, unified ATR/CCI trailing-line trend indicator (supersedes TrendMagic): true ATR, CCI hysteresis, Dot render, Sentinel card. Optionally consults ADX + publishes its trend to SentinelCore.";
                Name                        = "Sentinel Trend v1.0.0";
                Calculate                   = Calculate.OnPriceChange;
                IsOverlay                   = true;
                IsSuspendedWhileInactive    = true;
                DisplayInDataBox            = true;

                CciPeriod                   = 20;
                AtrPeriod                   = 14;
                AtrMult                     = 1.5;
                CciThreshold                = 15.0;   // deadband half-width in CCI units (0 = classic zero-cross)

                ShowSignals                 = true;
                SignalOffsetTicks           = 8;
                ColorBars                   = false;
                HighlightBackground         = false;
                BackgroundOpacity           = 12;

                RequireAdxAlign             = false;
                StaleSec                    = 90;

                ShowCard                    = true;
                CardCorner                  = SentinelCardCorner.TopRight;   // house default: cards dock right
                PublishState                = true;    // default ON — feed the Council out of the box (v1.0.0b)

                ShowIndicatorLabel          = false;   // Sentinel standard: clean chart (NT name label removed)

                // Sentinel palette (Docs §1) — green/red = direction (money), cyan reserved for the live dot.
                BullBrush                   = Sb(37, 208, 139);    // up (green)
                BearBrush                   = Sb(255, 92, 106);    // down (red)

                AddPlot(new Stroke(BullBrush, 2), PlotStyle.Dot, "Trend");
                AddPlot(Brushes.Transparent, "Direction");         // +1/-1/0, consumed by strategies
            }
            else if (State == State.DataLoaded)
            {
                if (!ShowIndicatorLabel) Name = string.Empty;      // Sentinel label remover (LabelRemover.cs pattern)
            }
            else if (State == State.Terminated)
            {
                if (_sp != null) { try { _sp.Dispose(); } catch { } _sp = null; }
                try { SentinelSkin.CardLayout.Release(this); } catch { }
            }
        }

        // helper: frozen Sentinel-palette brush
        private static Brush Sb(byte r, byte g, byte b)
        {
            var br = new SolidColorBrush(Color.FromRgb(r, g, b));
            br.Freeze();
            return br;
        }

        // ── HEARTBEAT (SentinelCore v1.19.0) ─────────────────────────────────────────────────
        // An OnBarClose publisher only refreshes its seam when a bar CLOSES. In a quiet market bars close
        // slowly, the seam ages past the Council's StaleSec, and a perfectly healthy voter silently drops
        // out of the roster — seen live as a FULLY LOADED chart reporting "roster 3/10". The Council has
        // heartbeated its own verdict since v1.0.0; its sensors never did. Re-stamp the cached reading on
        // incoming quotes: no recompute, realtime only (a historical re-stamp would fake freshness onto a
        // replayed bar), throttled.
        private DateTime _lastHeartbeatUtc;
        private const double HeartbeatSec = 5.0;
        protected override void OnMarketData(NinjaTrader.Data.MarketDataEventArgs marketDataUpdate)
        {
            if (!PublishState || State != State.Realtime) return;
            DateTime now = DateTime.UtcNow;
            if ((now - _lastHeartbeatUtc).TotalSeconds < HeartbeatSec) return;
            _lastHeartbeatUtc = now;
            try { SentinelCore.TouchTrendState(Scope()); } catch { }
        }

        protected override void OnBarUpdate()
        {
            int warmup = Math.Max(CciPeriod, AtrPeriod);
            if (CurrentBar < warmup)
            {
                Trend[0]     = Input[0];
                Direction[0] = 0;
                _warming     = true;
                return;
            }

            double cci = CCI(CciPeriod)[0];      // CCI on the bar (default HLC3 typical price)
            double atr = ATR(AtrPeriod)[0];      // TRUE ATR on the bar — NOT ATR(Close, n)
            double off = atr * AtrMult;
            double up  = Low[0]  - off;          // support candidate (below price)
            double dn  = High[0] + off;          // resistance candidate (above price)

            int prevDir = (int)Direction[1];

            // ── CCI hysteresis deadband: flip only past the threshold; otherwise HOLD ──
            int d = prevDir;
            if (cci >  CciThreshold) d = 1;
            else if (cci < -CciThreshold) d = -1;
            if (d == 0) d = cci >= 0 ? 1 : -1;   // startup seed so the line has a side

            bool flipped = prevDir != 0 && d != prevDir;

            // ── ratchet the trailing line; seed cleanly on a flip (no stale-side carry) ──
            double prevTrend = Trend[1];
            double t;
            if (prevDir == 0 || flipped)
                t = d == 1 ? up : dn;                 // start the line on the new side
            else if (d == 1)
                t = Math.Max(up, prevTrend);          // uptrend support only rises
            else
                t = Math.Min(dn, prevTrend);          // downtrend resistance only falls

            Trend[0]     = t;
            Direction[0] = d;
            PlotBrushes[0][0] = d == 1 ? BullBrush : BearBrush;

            // ── derived state ──
            _cci         = cci;
            _dir         = d;
            _trendPrice  = t;
            _distTicks   = TickSize > 0 ? (Close[0] - t) / TickSize : 0.0;
            _flipped     = flipped;
            _warming     = false;
            if (flipped) _lastFlipBar = CurrentBar;
            _barsInTrend = CurrentBar - _lastFlipBar;               // tick-safe count (not a per-tick ++)

            if (CurrentBar != _lastHistBar)                        // one sparkline sample per bar
            {
                _distHist.Add(_distTicks);
                if (_distHist.Count > HistMax) _distHist.RemoveAt(0);
                _lastHistBar = CurrentBar;
            }

            // ── optional ADX-regime consult (fail-open when nothing published) ──
            _adxConsulted = false; _adxAligned = false;
            if ((RequireAdxAlign || PublishState) && Instrument != null && Instrument.MasterInstrument != null)
            {
                try
                {
                    // Consult the ADX on THIS chart (scope), not whichever chart wrote last (v1.18.0).
                    var a = SentinelCore.GetAdxState(Scope() ?? Instrument.MasterInstrument.Name, StaleSec);
                    if (a != null) { _adxConsulted = true; _adxAligned = a.TrendOn && a.Aligned(d); }
                }
                catch { }
            }

            // ── flip signal markers (optionally gated by ADX alignment) ──
            if (ShowSignals && flipped && (!RequireAdxAlign || _adxAligned))
            {
                if (d == 1)
                    Draw.Text(this, "STup" + CurrentBar, "▲", 0, Low[0] - SignalOffsetTicks * TickSize, BullBrush);
                else
                    Draw.Text(this, "STdn" + CurrentBar, "▼", 0, High[0] + SignalOffsetTicks * TickSize, BearBrush);
            }

            // ── optional bar coloring / background ──
            if (ColorBars) BarBrush = d == 1 ? BullBrush : BearBrush;
            if (HighlightBackground) BackBrush = MakeTransparent(d == 1 ? BullBrush : BearBrush, BackgroundOpacity);

            // ── publish for the fleet to consult (SentinelCore ≥ v1.18.0 — keyed by SCOPE, not instrument) ──
            if (PublishState && Instrument != null && Instrument.MasterInstrument != null)
            {
                try
                {
                    SentinelCore.SetTrendState(Scope(), SentinelCore.BarTag(BarsPeriod), Instrument.MasterInstrument.Name,
                                               d, t, _distTicks, _barsInTrend, flipped, cci, _adxAligned, "SentinelTrend");
                }
                catch { }
            }
        }

        // ── scope (SentinelCore v1.18.0 · execution plan 1.4) ──
        // "<masterInstrument>.<barTag>" — ONE CHART's worth of context. Two charts on one instrument used to
        // overwrite each other's trend reading. Resolved lazily and cached; a null scope no-ops the publish.
        private string _scope;
        private string Scope()
        {
            if (_scope == null) { try { _scope = SentinelCore.ScopeOf(Instrument, BarsPeriod); } catch { } }
            return _scope;
        }

        private Brush MakeTransparent(Brush src, int opacityPercent)
        {
            if (src == null) return null;
            Brush b = src.Clone();
            b.Opacity = Math.Max(0, Math.Min(100, opacityPercent)) / 100.0;
            b.Freeze();
            return b;
        }

        // ── Sentinel "flight-instrument" glass card (SharpDX / SentinelSkin.Painter) ──
        protected override void OnRender(NinjaTrader.Gui.Chart.ChartControl chartControl, NinjaTrader.Gui.Chart.ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);
            if (!ShowCard || RenderTarget == null || ChartPanel == null) return;
            try
            {
                if (_sp == null) _sp = new SentinelSkin.Painter();
                _sp.Begin(RenderTarget);

                const float cw = 238f, ch = 150f;
                var slot = SentinelSkin.CardLayout.Place(this, ChartPanel,
                    ChartPanel.X, ChartPanel.Y, ChartPanel.W, ChartPanel.H, CardCorner, cw, ch);

                if (_warming)
                {
                    var rw = _sp.Card(slot.X, slot.Y, cw, ch, SentinelSkin.CLine);
                    _sp.Dot(rw.Left + 5f, rw.Top + 8f, SentinelSkin.CMute, false);
                    _sp.Text("SENTINEL TREND", rw.Left + 16f, rw.Top, rw.Width - 20f, 16f, SentinelSkin.CInk, 11f, true);
                    _sp.Text("warming up…", rw.Left, rw.Top + 24f, rw.Width, 14f, SentinelSkin.CMute, 10.5f);
                    _sp.End();
                    return;
                }

                var dirCol = _dir > 0 ? SentinelSkin.CUp : (_dir < 0 ? SentinelSkin.CDown : SentinelSkin.CMute);
                var trail  = SharpDX.DirectWrite.TextAlignment.Trailing;
                var r = _sp.Card(slot.X, slot.Y, cw, ch, _flipped ? SentinelSkin.CAccent : SentinelSkin.CLine);

                // header — live dot (cyan on the flip bar), title, direction pill
                _sp.Dot(r.Left + 5f, r.Top + 8f, _flipped ? SentinelSkin.CAccent : dirCol, true);
                _sp.Text("SENTINEL TREND", r.Left + 16f, r.Top, r.Width - 84f, 16f, SentinelSkin.CInk, 11f, true);
                _sp.Pill(_dir > 0 ? "LONG" : (_dir < 0 ? "SHORT" : "FLAT"), r.Right, r.Top - 1f, dirCol);

                // hero — trailing line price + signed distance in ticks
                _sp.Text("LINE", r.Left, r.Top + 24f, 60f, 12f, SentinelSkin.CMute, 8.5f, true);
                _sp.Text(_trendPrice.ToString("0.00"), r.Left, r.Top + 34f, r.Width, 24f, SentinelSkin.CInk, 18f, false);
                string dt = (_distTicks >= 0 ? "+" : "") + _distTicks.ToString("0.0") + "t";
                _sp.Text(dt, r.Left, r.Top + 24f, r.Width, 20f, dirCol, 12f, true, trail);
                _sp.Text("dist", r.Left, r.Top + 42f, r.Width, 12f, SentinelSkin.CMute, 8.5f, false, trail);

                // distance sparkline — price pushing away from / falling back to the line
                _sp.Sparkline(r.Left, r.Top + 64f, r.Width, 22f, _distHist, dirCol);

                // footer — bars-in-trend + optional ADX-align chip
                _sp.Divider(r.Left, r.Top + 94f, r.Right);
                _sp.Text(_barsInTrend + " bars in trend", r.Left, r.Top + 98f, r.Width, 14f, SentinelSkin.CInk2, 10.5f);
                if (_adxConsulted)
                {
                    var chip = _adxAligned ? SentinelSkin.CUp : SentinelSkin.CWarn;
                    _sp.Text(_adxAligned ? "ADX ✓" : "ADX ✗", r.Left, r.Top + 98f, r.Width, 14f, chip, 10.5f, true, trail);
                }

                _sp.End();
            }
            catch { }
        }

        #region Properties
        [Range(1, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "CCI Period", Order = 1, GroupName = "Parameters")]
        public int CciPeriod { get; set; }

        [Range(1, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "ATR Period", Order = 2, GroupName = "Parameters")]
        public int AtrPeriod { get; set; }

        [Range(0.00001, double.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "ATR Multiplier", Order = 3, GroupName = "Parameters")]
        public double AtrMult { get; set; }

        [Range(0.0, double.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "CCI Hysteresis Threshold", Description = "Deadband half-width in CCI units. Flip up only when CCI > +this, down when CCI < -this; otherwise hold. 0 = classic zero-cross.", Order = 4, GroupName = "Parameters")]
        public double CciThreshold { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Flip Signals", Order = 10, GroupName = "Visual")]
        public bool ShowSignals { get; set; }

        [Range(0, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Signal Offset (ticks)", Order = 11, GroupName = "Visual")]
        public int SignalOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Color Bars", Order = 12, GroupName = "Visual")]
        public bool ColorBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Highlight Background", Order = 13, GroupName = "Visual")]
        public bool HighlightBackground { get; set; }

        [Range(0, 100)]
        [NinjaScriptProperty]
        [Display(Name = "Background Opacity %", Order = 14, GroupName = "Visual")]
        public int BackgroundOpacity { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Card", Order = 15, GroupName = "Visual")]
        public bool ShowCard { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Card Corner", Description = "Which chart corner the Sentinel card docks to. Cards in the same corner auto-stack.", Order = 16, GroupName = "Visual")]
        public SentinelCardCorner CardCorner { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Require ADX Alignment", Description = "Only draw flip signals when ADXPro publishes trend ON + bias agreeing with the flip (needs SentinelCore ≥ v1.2.0 + ADXPro publishing).", Order = 20, GroupName = "Sentinel")]
        public bool RequireAdxAlign { get; set; }

        [Range(0, double.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Consult Stale (sec)", Description = "Ignore a published ADX state older than this many seconds (0 = never stale).", Order = 21, GroupName = "Sentinel")]
        public double StaleSec { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Publish Trend to Sentinel", Description = "Publish direction + line + distance to SentinelCore so GTrader21/Eye/strategies can consult it. Needs SentinelCore ≥ v1.3.0.", Order = 22, GroupName = "Sentinel")]
        public bool PublishState { get; set; }

        [XmlIgnore]
        [Display(Name = "Bull (Up) Color", Order = 30, GroupName = "Colors")]
        public Brush BullBrush { get; set; }

        [Browsable(false)]
        public string BullBrushSerialize
        {
            get { return Serialize.BrushToString(BullBrush); }
            set { BullBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Bear (Down) Color", Order = 31, GroupName = "Colors")]
        public Brush BearBrush { get; set; }

        [Browsable(false)]
        public string BearBrushSerialize
        {
            get { return Serialize.BrushToString(BearBrush); }
            set { BearBrush = Serialize.StringToBrush(value); }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> Trend
        {
            get { return Values[0]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> Direction
        {
            get { return Values[1]; }
        }

        [NinjaScriptProperty]
        [Display(Name = "Show indicator label", Description = "Show NinjaTrader's chart name label. Sentinel default = OFF (clean chart); turn on to restore it.", GroupName = "Sentinel", Order = 100)]
        public bool ShowIndicatorLabel { get; set; }
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private Sentinel.Sensors.SentinelTrend_v1_0_0[] cacheSentinelTrend_v1_0_0;
		public Sentinel.Sensors.SentinelTrend_v1_0_0 SentinelTrend_v1_0_0(int cciPeriod, int atrPeriod, double atrMult, double cciThreshold, bool showSignals, int signalOffsetTicks, bool colorBars, bool highlightBackground, int backgroundOpacity, bool showCard, SentinelCardCorner cardCorner, bool requireAdxAlign, double staleSec, bool publishState, bool showIndicatorLabel)
		{
			return SentinelTrend_v1_0_0(Input, cciPeriod, atrPeriod, atrMult, cciThreshold, showSignals, signalOffsetTicks, colorBars, highlightBackground, backgroundOpacity, showCard, cardCorner, requireAdxAlign, staleSec, publishState, showIndicatorLabel);
		}

		public Sentinel.Sensors.SentinelTrend_v1_0_0 SentinelTrend_v1_0_0(ISeries<double> input, int cciPeriod, int atrPeriod, double atrMult, double cciThreshold, bool showSignals, int signalOffsetTicks, bool colorBars, bool highlightBackground, int backgroundOpacity, bool showCard, SentinelCardCorner cardCorner, bool requireAdxAlign, double staleSec, bool publishState, bool showIndicatorLabel)
		{
			if (cacheSentinelTrend_v1_0_0 != null)
				for (int idx = 0; idx < cacheSentinelTrend_v1_0_0.Length; idx++)
					if (cacheSentinelTrend_v1_0_0[idx] != null && cacheSentinelTrend_v1_0_0[idx].CciPeriod == cciPeriod && cacheSentinelTrend_v1_0_0[idx].AtrPeriod == atrPeriod && cacheSentinelTrend_v1_0_0[idx].AtrMult == atrMult && cacheSentinelTrend_v1_0_0[idx].CciThreshold == cciThreshold && cacheSentinelTrend_v1_0_0[idx].ShowSignals == showSignals && cacheSentinelTrend_v1_0_0[idx].SignalOffsetTicks == signalOffsetTicks && cacheSentinelTrend_v1_0_0[idx].ColorBars == colorBars && cacheSentinelTrend_v1_0_0[idx].HighlightBackground == highlightBackground && cacheSentinelTrend_v1_0_0[idx].BackgroundOpacity == backgroundOpacity && cacheSentinelTrend_v1_0_0[idx].ShowCard == showCard && cacheSentinelTrend_v1_0_0[idx].CardCorner == cardCorner && cacheSentinelTrend_v1_0_0[idx].RequireAdxAlign == requireAdxAlign && cacheSentinelTrend_v1_0_0[idx].StaleSec == staleSec && cacheSentinelTrend_v1_0_0[idx].PublishState == publishState && cacheSentinelTrend_v1_0_0[idx].ShowIndicatorLabel == showIndicatorLabel && cacheSentinelTrend_v1_0_0[idx].EqualsInput(input))
						return cacheSentinelTrend_v1_0_0[idx];
			return CacheIndicator<Sentinel.Sensors.SentinelTrend_v1_0_0>(new Sentinel.Sensors.SentinelTrend_v1_0_0(){ CciPeriod = cciPeriod, AtrPeriod = atrPeriod, AtrMult = atrMult, CciThreshold = cciThreshold, ShowSignals = showSignals, SignalOffsetTicks = signalOffsetTicks, ColorBars = colorBars, HighlightBackground = highlightBackground, BackgroundOpacity = backgroundOpacity, ShowCard = showCard, CardCorner = cardCorner, RequireAdxAlign = requireAdxAlign, StaleSec = staleSec, PublishState = publishState, ShowIndicatorLabel = showIndicatorLabel }, input, ref cacheSentinelTrend_v1_0_0);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.Sentinel.Sensors.SentinelTrend_v1_0_0 SentinelTrend_v1_0_0(int cciPeriod, int atrPeriod, double atrMult, double cciThreshold, bool showSignals, int signalOffsetTicks, bool colorBars, bool highlightBackground, int backgroundOpacity, bool showCard, SentinelCardCorner cardCorner, bool requireAdxAlign, double staleSec, bool publishState, bool showIndicatorLabel)
		{
			return indicator.SentinelTrend_v1_0_0(Input, cciPeriod, atrPeriod, atrMult, cciThreshold, showSignals, signalOffsetTicks, colorBars, highlightBackground, backgroundOpacity, showCard, cardCorner, requireAdxAlign, staleSec, publishState, showIndicatorLabel);
		}

		public Indicators.Sentinel.Sensors.SentinelTrend_v1_0_0 SentinelTrend_v1_0_0(ISeries<double> input , int cciPeriod, int atrPeriod, double atrMult, double cciThreshold, bool showSignals, int signalOffsetTicks, bool colorBars, bool highlightBackground, int backgroundOpacity, bool showCard, SentinelCardCorner cardCorner, bool requireAdxAlign, double staleSec, bool publishState, bool showIndicatorLabel)
		{
			return indicator.SentinelTrend_v1_0_0(input, cciPeriod, atrPeriod, atrMult, cciThreshold, showSignals, signalOffsetTicks, colorBars, highlightBackground, backgroundOpacity, showCard, cardCorner, requireAdxAlign, staleSec, publishState, showIndicatorLabel);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.Sentinel.Sensors.SentinelTrend_v1_0_0 SentinelTrend_v1_0_0(int cciPeriod, int atrPeriod, double atrMult, double cciThreshold, bool showSignals, int signalOffsetTicks, bool colorBars, bool highlightBackground, int backgroundOpacity, bool showCard, SentinelCardCorner cardCorner, bool requireAdxAlign, double staleSec, bool publishState, bool showIndicatorLabel)
		{
			return indicator.SentinelTrend_v1_0_0(Input, cciPeriod, atrPeriod, atrMult, cciThreshold, showSignals, signalOffsetTicks, colorBars, highlightBackground, backgroundOpacity, showCard, cardCorner, requireAdxAlign, staleSec, publishState, showIndicatorLabel);
		}

		public Indicators.Sentinel.Sensors.SentinelTrend_v1_0_0 SentinelTrend_v1_0_0(ISeries<double> input , int cciPeriod, int atrPeriod, double atrMult, double cciThreshold, bool showSignals, int signalOffsetTicks, bool colorBars, bool highlightBackground, int backgroundOpacity, bool showCard, SentinelCardCorner cardCorner, bool requireAdxAlign, double staleSec, bool publishState, bool showIndicatorLabel)
		{
			return indicator.SentinelTrend_v1_0_0(input, cciPeriod, atrPeriod, atrMult, cciThreshold, showSignals, signalOffsetTicks, colorBars, highlightBackground, backgroundOpacity, showCard, cardCorner, requireAdxAlign, staleSec, publishState, showIndicatorLabel);
		}
	}
}

#endregion
