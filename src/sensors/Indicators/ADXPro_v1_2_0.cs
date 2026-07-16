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
using NinjaTrader.Gui.Tools;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.AddOns.Sentinel;   // SentinelSkin (glass card) + SentinelCore (regime publish) + SentinelCardCorner
using NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors;
#endregion

// ═════════════════════════════════════════════════════════════════════════════
//  ADXPro — ADX / DI bias indicator with a Sentinel flight-instrument card   |   Version v1.2.0
//  File: ADXPro_v1_2_0.cs   |   namespace …Indicators.Sentinel
//
//  ⚠ NO ORDERS — read-only bias/regime indicator. Safe to run anywhere.
//
//  v1.2.0 completes the Sentinel retrofit that v1.1.0 only half-did:
//    • CardLayout-DOCKED glass card (+ a CardCorner property) — v1.1.0 hardcoded the top-right rect, so
//      it COVERED CompressionBase/Eye/SignalExcursionRecorder (all default there). Now it auto-stacks.
//    • Richer card via the SentinelSkin.Painter vocabulary: an ADX GAUGE hero (0–50 dial), +DI / −DI
//      dual magnitude tracks, an ADX SPARKLINE (trend building vs fading at a glance), and a revived
//      Strong / Building / Weakening bias label (the slope nuance v1.1.0 computed then threw away).
//    • PLOT COLORS → the Sentinel palette (dataviz language): ADX = cyan (strength/magnitude),
//      +DI = green, −DI = red (money/direction), Trigger = mute, Strong = amber (advisory threshold),
//      bull/bear background = green/red tint. No more Gold/DeepSkyBlue/MediumPurple/Teal/Purple.
//    • PublishRegime → SentinelCore.SetAdxState(instrument, adx, +DI, −DI, bias, slope5, strong) so
//      GTrader21 / Eye / Copier can consult "trend ON + bias agrees" (needs SentinelCore ≥ v1.2.0).
//    • Dropped dead pre-card cruft: BiasTablePosition / TableFontSize / TableTextBrush props + the
//      unused BiasText()/SlopeDirection()/TrendText() methods.
//
//  NEW TYPE IDENTITY (namespace+class+Name) → re-add on charts; ADXPro_v1_1_0 stays a FROZEN fallback.
//  See Docs/SENTINEL_DESIGN_SYSTEM.md §4b (CardLayout/Painter) + §1 (palette) + memory sentinel-namespace-and-naming.
//  CHANGELOG
//    v1.2.0b (in-place 2026-07-07) — SENTINEL PLOT SKIN: OnRender now paints a glass PanelWash (covers stock
//             plots) + refined PER-BAR trend-regime bands (CUp/CDown, low alpha — supersedes the muddy
//             BackBrushes, now skipped when the skin is on) + themed trigger/strong reference lines + glowing
//             ADX/+DI/−DI lines (ADX cyan when strong). Toggle: SentinelPlotSkin (default ON); grid off. §4c.
//    v1.2.0a (2026-07-07) — PublishRegime now DEFAULTS ON so ADXPro feeds the Council's AdxState voter out of
//             the box. In-place patch (no rename); existing chart placements keep their serialized value.
//    v1.2.0 — CardLayout dock + CardCorner; gauge/DI-tracks/sparkline card; Sentinel plot colors;
//             SentinelCore ADX-regime publish; removed dead table props/methods. (Prior: ADXPro_v1_1_0.)
// ═════════════════════════════════════════════════════════════════════════════
namespace NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors
{
    public class ADXPro_v1_2_0 : Indicator
    {
        private Series<double> trueRange;
        private Series<double> plusDM;
        private Series<double> minusDM;
        private Series<double> smoothedTR;
        private Series<double> smoothedPlusDM;
        private Series<double> smoothedMinusDM;
        private Series<double> dxSeries;
        private Series<double> adxInternal;

        // Sentinel glass-card readout: drawn in OnRender via SentinelSkin.Painter. OnBarUpdate just caches state.
        private SentinelSkin.Painter _sp;
        private bool   _adxWarming = true;
        private double _adx, _diPlus, _diMinus, _slope2, _slope5;
        private int    _bias;                                  // -1 bear / 0 neutral / 1 bull
        private bool   _strong;
        private readonly List<double> _adxHist = new List<double>();   // ring for the sparkline
        private const int HistMax = 48;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                 = "ADX / DI trend-strength + bias indicator with a Sentinel flight-instrument card. Optionally publishes its regime to SentinelCore.";
                Name                        = "Sentinel ADX Pro v1.2.0";
                Calculate                   = Calculate.OnBarClose;
                IsOverlay                   = false;
                DisplayInDataBox            = true;
                DrawOnPricePanel            = false;
                DrawHorizontalGridLines     = false;   // plot skin paints its own wash + reference lines
                DrawVerticalGridLines       = false;
                PaintPriceMarkers           = true;
                ScaleJustification          = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsSuspendedWhileInactive    = true;

                Period                      = 14;
                TriggerLevel                = 10.0;
                StrongLevel                 = 20.0;
                FlatSlopeThreshold          = 0.20;

                UseBackground               = true;
                BackgroundOpacity           = 18;
                ShowBiasTable               = true;
                SentinelPlotSkin            = true;   // render the panel to the Sentinel plot standard
                CardCorner                  = SentinelCardCorner.TopRight;
                PublishRegime               = true;    // default ON — feed the Council out of the box

                // Sentinel palette (Docs §1) — ADX = cyan strength, +DI/−DI = green/red direction,
                // Trigger = mute reference, Strong = amber advisory threshold, bull/bear bg = green/red.
                ADXLineBrush                = Sb(63, 209, 224);    // accent (cyan)
                DIPlusLineBrush             = Sb(37, 208, 139);    // up (green)
                DIMinusLineBrush            = Sb(255, 92, 106);    // down (red)
                TriggerLineBrush            = Sb(108, 122, 146);   // mute
                StrongLineBrush             = Sb(242, 179, 76);    // warn (amber)
                BullishBackgroundBrush      = Sb(37, 208, 139);    // up (green)
                BearishBackgroundBrush      = Sb(255, 92, 106);    // down (red)

                ShowIndicatorLabel          = false;   // Sentinel standard: clean chart (NT name label removed)

                AddPlot(ADXLineBrush, "ADX");
                AddPlot(DIPlusLineBrush, "+DI");
                AddPlot(DIMinusLineBrush, "-DI");

                AddLine(TriggerLineBrush, TriggerLevel, "Trigger");
                AddLine(StrongLineBrush, StrongLevel, "Strong");
            }
            else if (State == State.DataLoaded)
            {
                if (!ShowIndicatorLabel) Name = string.Empty;   // Sentinel label remover — NT draws the panel label from Name (LabelRemover.cs)
                trueRange       = new Series<double>(this);
                plusDM          = new Series<double>(this);
                minusDM         = new Series<double>(this);
                smoothedTR      = new Series<double>(this);
                smoothedPlusDM  = new Series<double>(this);
                smoothedMinusDM = new Series<double>(this);
                dxSeries        = new Series<double>(this);
                adxInternal     = new Series<double>(this);
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

        protected override void OnBarUpdate()
        {
            UpdateReferenceLines();

            if (CurrentBar == 0)
            {
                ADXValue[0]     = double.NaN;
                DIPlusValue[0]  = double.NaN;
                DIMinusValue[0] = double.NaN;
                return;
            }

            double highLow     = High[0] - Low[0];
            double highClose   = Math.Abs(High[0] - Close[1]);
            double lowClose    = Math.Abs(Low[0] - Close[1]);
            double tr          = Math.Max(highLow, Math.Max(highClose, lowClose));

            double upMove      = High[0] - High[1];
            double downMove    = Low[1] - Low[0];
            double pdm         = (upMove > downMove && upMove > 0) ? upMove : 0.0;
            double mdm         = (downMove > upMove && downMove > 0) ? downMove : 0.0;

            trueRange[0]       = tr;
            plusDM[0]          = pdm;
            minusDM[0]         = mdm;

            ADXValue[0]        = double.NaN;
            DIPlusValue[0]     = double.NaN;
            DIMinusValue[0]    = double.NaN;
            adxInternal[0]     = double.NaN;

            if (CurrentBar < Period)
            {
                _adxWarming = true;
                return;
            }

            if (CurrentBar == Period)
            {
                double trSum   = 0.0;
                double pdmSum  = 0.0;
                double mdmSum  = 0.0;

                for (int i = 0; i < Period; i++)
                {
                    trSum  += trueRange[i];
                    pdmSum += plusDM[i];
                    mdmSum += minusDM[i];
                }

                smoothedTR[0]      = trSum;
                smoothedPlusDM[0]  = pdmSum;
                smoothedMinusDM[0] = mdmSum;
            }
            else
            {
                smoothedTR[0]      = smoothedTR[1]      - (smoothedTR[1]      / Period) + trueRange[0];
                smoothedPlusDM[0]  = smoothedPlusDM[1]  - (smoothedPlusDM[1]  / Period) + plusDM[0];
                smoothedMinusDM[0] = smoothedMinusDM[1] - (smoothedMinusDM[1] / Period) + minusDM[0];
            }

            double diPlus  = smoothedTR[0] <= 0 ? 0.0 : 100.0 * smoothedPlusDM[0]  / smoothedTR[0];
            double diMinus = smoothedTR[0] <= 0 ? 0.0 : 100.0 * smoothedMinusDM[0] / smoothedTR[0];
            double diTotal = diPlus + diMinus;
            double dx      = diTotal <= 0 ? 0.0 : 100.0 * Math.Abs(diPlus - diMinus) / diTotal;

            DIPlusValue[0]  = diPlus;
            DIMinusValue[0] = diMinus;
            dxSeries[0]     = dx;

            bool adxReady = false;
            double adx    = double.NaN;
            int firstAdxBar = (2 * Period) - 1;

            if (CurrentBar == firstAdxBar)
            {
                double dxSum = 0.0;
                for (int i = 0; i < Period; i++)
                    dxSum += dxSeries[i];

                adx = dxSum / Period;
                adxReady = true;
            }
            else if (CurrentBar > firstAdxBar)
            {
                adx = ((adxInternal[1] * (Period - 1)) + dx) / Period;
                adxReady = true;
            }

            if (adxReady)
            {
                ADXValue[0]    = adx;
                adxInternal[0] = adx;
            }

            PlotBrushes[0][0] = ADXLineBrush;
            PlotBrushes[1][0] = DIPlusLineBrush;
            PlotBrushes[2][0] = DIMinusLineBrush;

            if (!adxReady)
            {
                _adxWarming = true;
                return;
            }

            double slope2 = (CurrentBar >= firstAdxBar + 2 && !double.IsNaN(ADXValue[2])) ? adx - ADXValue[2] : double.NaN;
            double slope5 = (CurrentBar >= firstAdxBar + 5 && !double.IsNaN(ADXValue[5])) ? adx - ADXValue[5] : double.NaN;

            int  bias   = adx < TriggerLevel ? 0 : (diPlus > diMinus ? 1 : -1);
            bool strong = adx >= StrongLevel;

            ApplyBiasBackground(adx, diPlus, diMinus);
            CacheCardState(adx, diPlus, diMinus, slope2, slope5, bias, strong);

            // Publish the regime for the fleet to consult (SentinelCore ≥ v1.18.0 — keyed by SCOPE, not instrument:
            // two GC charts on different bar types used to overwrite each other's ADX reading every bar, so a
            // Council could fuse the OTHER chart's regime).
            if (PublishRegime && Instrument != null && Instrument.MasterInstrument != null)
            {
                try
                {
                    SentinelCore.SetAdxState(Scope(), SentinelCore.BarTag(BarsPeriod), Instrument.MasterInstrument.Name,
                                             adx, diPlus, diMinus, bias, slope5, strong, "ADXPro");
                }
                catch { }
            }
        }

        // ── scope (SentinelCore v1.18.0 · execution plan 1.4) ──
        // "<masterInstrument>.<barTag>" — ONE CHART's worth of context. Resolved lazily (Instrument/BarsPeriod are
        // live from DataLoaded on) and cached. A null scope makes the publish a no-op, which is the right
        // fail-silent for an indicator that is not yet configured.
        private string _scope;
        private string Scope()
        {
            if (_scope == null) { try { _scope = SentinelCore.ScopeOf(Instrument, BarsPeriod); } catch { } }
            return _scope;
        }

        private void UpdateReferenceLines()
        {
            if (Lines != null && Lines.Length >= 2)
            {
                Lines[0].Value = TriggerLevel;
                Lines[0].Brush = TriggerLineBrush;
                Lines[1].Value = StrongLevel;
                Lines[1].Brush = StrongLineBrush;
            }
        }

        private void ApplyBiasBackground(double adx, double diPlus, double diMinus)
        {
            // The plot skin draws its own refined per-bar regime bands in OnRender; skip the (coarser) BackBrushes.
            if (SentinelPlotSkin || !UseBackground || adx < TriggerLevel || Math.Abs(diPlus - diMinus) < double.Epsilon)
            {
                BackBrushes[0] = null;
                return;
            }

            BackBrushes[0] = MakeTransparentBrush(diPlus > diMinus ? BullishBackgroundBrush : BearishBackgroundBrush, BackgroundOpacity);
        }

        private Brush MakeTransparentBrush(Brush sourceBrush, int opacityPercent)
        {
            if (sourceBrush == null)
                return null;

            Brush b = sourceBrush.Clone();
            b.Opacity = Math.Max(0, Math.Min(100, opacityPercent)) / 100.0;
            b.Freeze();
            return b;
        }

        private void CacheCardState(double adx, double diPlus, double diMinus, double slope2, double slope5, int bias, bool strong)
        {
            _adxWarming = false;
            _adx = adx; _diPlus = diPlus; _diMinus = diMinus; _slope2 = slope2; _slope5 = slope5;
            _bias = bias; _strong = strong;

            _adxHist.Add(adx);
            if (_adxHist.Count > HistMax) _adxHist.RemoveAt(0);
        }

        // Slope-aware bias label — revives the nuance v1.1.0 computed then discarded.
        private string BiasLabel(int bias, bool strong, double slope5)
        {
            if (bias == 0)
                return "Neutral / Weak";

            bool rising  = !double.IsNaN(slope5) && slope5 >  FlatSlopeThreshold;
            bool falling = !double.IsNaN(slope5) && slope5 < -FlatSlopeThreshold;
            string dir   = bias > 0 ? "Bull" : "Bear";

            if (strong && rising)  return "Strong " + (bias > 0 ? "Uptrend" : "Downtrend");
            if (falling)           return "Weakening " + dir;
            if (rising)            return "Building " + dir;
            return dir + "ish Bias";
        }

        // ── Sentinel plot skin + "flight-instrument" glass card (both in OnRender, one Begin/End frame) ──
        protected override void OnRender(NinjaTrader.Gui.Chart.ChartControl chartControl, NinjaTrader.Gui.Chart.ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);
            if (RenderTarget == null || ChartPanel == null) return;
            if (_sp == null) _sp = new SentinelSkin.Painter();
            _sp.Begin(RenderTarget);
            try { if (SentinelPlotSkin) RenderPlotSkin(chartControl, chartScale); } catch { }
            try { if (ShowBiasTable) RenderCard(); } catch { }
            try { _sp.End(); } catch { }
        }

        // Sentinel PLOT STANDARD for the ADX panel: glass wash (covers stock plots) + per-bar trend-regime
        // bands (CUp/CDown, low alpha — supersedes the muddy BackBrushes) + themed trigger/strong reference
        // lines + glowing ADX / +DI / −DI lines. Series read by ABSOLUTE bar index (render-safe).
        private void RenderPlotSkin(NinjaTrader.Gui.Chart.ChartControl chartControl, NinjaTrader.Gui.Chart.ChartScale chartScale)
        {
            if (Bars == null || Bars.Count < 2 || ChartBars == null) return;
            float px = ChartPanel.X, py = ChartPanel.Y, pw = ChartPanel.W, ph = ChartPanel.H;
            _sp.PanelWash(px, py, pw, ph);

            int from = ChartBars.FromIndex, to = ChartBars.ToIndex;
            if (from < 0) from = 0;
            if (to > Bars.Count - 1) to = Bars.Count - 1;
            if (to < from) return;

            float dx = to > from ? (chartControl.GetXByBarIndex(ChartBars, to) - chartControl.GetXByBarIndex(ChartBars, to - 1)) : 6f;
            if (dx <= 0f || float.IsNaN(dx) || float.IsInfinity(dx)) dx = 6f;
            float halfW = Math.Max(0.8f, dx * 0.5f);

            var adxPts = new List<SharpDX.Vector2>();
            var dpPts  = new List<SharpDX.Vector2>();
            var dmPts  = new List<SharpDX.Vector2>();
            for (int idx = from; idx <= to; idx++)
            {
                if (!Values[0].IsValidDataPointAt(idx)) continue;
                float x = chartControl.GetXByBarIndex(ChartBars, idx);
                double a  = Values[0].GetValueAt(idx);
                double dp = Values[1].IsValidDataPointAt(idx) ? Values[1].GetValueAt(idx) : double.NaN;
                double dm = Values[2].IsValidDataPointAt(idx) ? Values[2].GetValueAt(idx) : double.NaN;

                // per-bar trend-regime band, only where ADX confirms a trend
                if (a >= TriggerLevel && !double.IsNaN(dp) && !double.IsNaN(dm) && Math.Abs(dp - dm) > 1e-9)
                {
                    var rc = dp > dm ? SentinelSkin.CUp : SentinelSkin.CDown;
                    _sp.RegimeShade(x - halfW, py, halfW * 2f, ph, rc, a >= StrongLevel ? 0.14f : 0.07f);
                }

                adxPts.Add(new SharpDX.Vector2(x, chartScale.GetYByValue(a)));
                if (!double.IsNaN(dp)) dpPts.Add(new SharpDX.Vector2(x, chartScale.GetYByValue(dp)));
                if (!double.IsNaN(dm)) dmPts.Add(new SharpDX.Vector2(x, chartScale.GetYByValue(dm)));
            }

            // themed reference lines (trigger = amber, strong = cyan)
            _sp.Baseline(px, px + pw, chartScale.GetYByValue(TriggerLevel), SentinelSkin.CWarn);
            _sp.Baseline(px, px + pw, chartScale.GetYByValue(StrongLevel),  SentinelSkin.CAccent);

            // DI lines first, ADX on top (cyan glow when strong)
            if (dpPts.Count  > 1) _sp.GlowLine(dpPts,  SentinelSkin.CUp,   1.4f, 0.16f);
            if (dmPts.Count  > 1) _sp.GlowLine(dmPts,  SentinelSkin.CDown, 1.4f, 0.16f);
            if (adxPts.Count > 1) _sp.GlowLine(adxPts, _strong ? SentinelSkin.CAccent : SentinelSkin.CInk, 1.9f, 0.22f);
        }

        // Sentinel glass card (content unchanged; now painted inside the shared Begin/End frame).
        private void RenderCard()
        {
            const float cw = 244f, ch = 162f;
            var slot = SentinelSkin.CardLayout.Place(this, ChartPanel,
                ChartPanel.X, ChartPanel.Y, ChartPanel.W, ChartPanel.H, CardCorner, cw, ch);

            if (_adxWarming)
            {
                var rw = _sp.Card(slot.X, slot.Y, cw, ch, SentinelSkin.CLine);
                _sp.Dot(rw.Left + 5f, rw.Top + 8f, SentinelSkin.CMute, false);
                _sp.Text("ADX PRO", rw.Left + 16f, rw.Top, rw.Width - 20f, 16f, SentinelSkin.CInk, 11f, true);
                _sp.Text("warming up…", rw.Left, rw.Top + 24f, rw.Width, 14f, SentinelSkin.CMute, 10.5f);
                return;
            }

            var biasCol = _bias > 0 ? SentinelSkin.CUp : (_bias < 0 ? SentinelSkin.CDown : SentinelSkin.CMute);
            var r = _sp.Card(slot.X, slot.Y, cw, ch, _strong ? biasCol : SentinelSkin.CLine);

                // header
                _sp.Dot(r.Left + 5f, r.Top + 8f, biasCol, _bias != 0);
                _sp.Text("ADX PRO", r.Left + 16f, r.Top, r.Width - 78f, 16f, SentinelSkin.CInk, 11f, true);
                _sp.Pill(_bias > 0 ? "BULL" : (_bias < 0 ? "BEAR" : "NEUTRAL"), r.Right, r.Top - 1f, biasCol);

                // hero — ADX gauge (0..50 dial) with the value inside it
                var adxCol = _strong ? SentinelSkin.CAccent : (_adx >= TriggerLevel ? SentinelSkin.CWarn : SentinelSkin.CInk2);
                float gcx = r.Left + 28f, gcy = r.Top + 52f, gr = 26f;
                float frac = (float)Math.Max(0, Math.Min(1, _adx / 50.0));
                _sp.Gauge(gcx, gcy, gr, frac, SentinelSkin.CFaint, adxCol);
                _sp.Text(_adx.ToString("0.0"), gcx - 28f, gcy - 12f, 56f, 22f, adxCol, 16f, false, SharpDX.DirectWrite.TextAlignment.Center);
                _sp.Text("ADX", gcx - 28f, gcy + 26f, 56f, 12f, SentinelSkin.CMute, 8.5f, true, SharpDX.DirectWrite.TextAlignment.Center);

                // +DI / −DI dual magnitude tracks (right column)
                var trail = SharpDX.DirectWrite.TextAlignment.Trailing;
                float xcol = r.Left + 72f, wcol = r.Width - 72f;
                _sp.Text("+DI", xcol, r.Top + 24f, 44f, 13f, SentinelSkin.CUp, 10f, true);
                _sp.Text(_diPlus.ToString("0.0"), xcol, r.Top + 24f, wcol, 13f, SentinelSkin.CUp, 11f, false, trail);
                _sp.Track(xcol, r.Top + 40f, wcol, (float)Math.Max(0, Math.Min(1, _diPlus / 50.0)), SentinelSkin.CUp, 5f);
                _sp.Text("-DI", xcol, r.Top + 50f, 44f, 13f, SentinelSkin.CDown, 10f, true);
                _sp.Text(_diMinus.ToString("0.0"), xcol, r.Top + 50f, wcol, 13f, SentinelSkin.CDown, 11f, false, trail);
                _sp.Track(xcol, r.Top + 66f, wcol, (float)Math.Max(0, Math.Min(1, _diMinus / 50.0)), SentinelSkin.CDown, 5f);

                // ADX sparkline — building vs fading at a glance
                _sp.Sparkline(r.Left, r.Top + 84f, r.Width, 20f, _adxHist, _bias != 0 ? biasCol : SentinelSkin.CMute);

                // footer — revived slope-aware bias + Δ slopes
                string s2 = double.IsNaN(_slope2) ? "n/a" : (_slope2 >= 0 ? "+" : "") + _slope2.ToString("0.00");
                string s5 = double.IsNaN(_slope5) ? "n/a" : (_slope5 >= 0 ? "+" : "") + _slope5.ToString("0.00");
                _sp.Divider(r.Left, r.Top + 110f, r.Right);
                _sp.Text(BiasLabel(_bias, _strong, _slope5), r.Left, r.Top + 114f, r.Width, 14f, biasCol, 11f, true);
                _sp.Text("Δ2 " + s2 + "   Δ5 " + s5, r.Left, r.Top + 114f, r.Width, 14f, SentinelSkin.CInk2, 10f, false, trail, true);
        }

        #region Properties

        [Range(1, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Period", Order = 1, GroupName = "Parameters")]
        public int Period { get; set; }

        [Range(0.0, double.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Trigger Level", Order = 2, GroupName = "Parameters")]
        public double TriggerLevel { get; set; }

        [Range(0.0, double.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Strong Trend Level", Order = 3, GroupName = "Parameters")]
        public double StrongLevel { get; set; }

        [Range(0.0, double.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Flat Slope Threshold", Order = 4, GroupName = "Parameters")]
        public double FlatSlopeThreshold { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Background Bias", Order = 10, GroupName = "Visual")]
        public bool UseBackground { get; set; }

        [Range(0, 100)]
        [NinjaScriptProperty]
        [Display(Name = "Background Opacity %", Order = 11, GroupName = "Visual")]
        public int BackgroundOpacity { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Bias Card", Order = 12, GroupName = "Visual")]
        public bool ShowBiasTable { get; set; }

        // Not [NinjaScriptProperty] — serializes without a constructor param (no generated-region churn).
        [Display(Name = "Sentinel Plot Skin", Description = "Render the panel to the Sentinel plot standard (glass wash + regime bands + glowing ADX/DI lines) instead of NT's stock plots.", Order = 13, GroupName = "Visual")]
        public bool SentinelPlotSkin { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Card Corner", Description = "Which chart corner the Sentinel card docks to. Cards in the same corner auto-stack.", Order = 13, GroupName = "Visual")]
        public SentinelCardCorner CardCorner { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Publish Regime to Sentinel", Description = "Publish ADX strength + bias to SentinelCore so GTrader21/Eye/Copier can consult it. Needs SentinelCore ≥ v1.2.0.", Order = 14, GroupName = "Sentinel")]
        public bool PublishRegime { get; set; }

        [XmlIgnore]
        [Display(Name = "ADX Line", Order = 20, GroupName = "Colors")]
        public Brush ADXLineBrush { get; set; }

        [Browsable(false)]
        public string ADXLineBrushSerializable
        {
            get { return Serialize.BrushToString(ADXLineBrush); }
            set { ADXLineBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "+DI Line", Order = 21, GroupName = "Colors")]
        public Brush DIPlusLineBrush { get; set; }

        [Browsable(false)]
        public string DIPlusLineBrushSerializable
        {
            get { return Serialize.BrushToString(DIPlusLineBrush); }
            set { DIPlusLineBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "-DI Line", Order = 22, GroupName = "Colors")]
        public Brush DIMinusLineBrush { get; set; }

        [Browsable(false)]
        public string DIMinusLineBrushSerializable
        {
            get { return Serialize.BrushToString(DIMinusLineBrush); }
            set { DIMinusLineBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Trigger Line", Order = 23, GroupName = "Colors")]
        public Brush TriggerLineBrush { get; set; }

        [Browsable(false)]
        public string TriggerLineBrushSerializable
        {
            get { return Serialize.BrushToString(TriggerLineBrush); }
            set { TriggerLineBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Strong Line", Order = 24, GroupName = "Colors")]
        public Brush StrongLineBrush { get; set; }

        [Browsable(false)]
        public string StrongLineBrushSerializable
        {
            get { return Serialize.BrushToString(StrongLineBrush); }
            set { StrongLineBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Bullish Background", Order = 25, GroupName = "Colors")]
        public Brush BullishBackgroundBrush { get; set; }

        [Browsable(false)]
        public string BullishBackgroundBrushSerializable
        {
            get { return Serialize.BrushToString(BullishBackgroundBrush); }
            set { BullishBackgroundBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Bearish Background", Order = 26, GroupName = "Colors")]
        public Brush BearishBackgroundBrush { get; set; }

        [Browsable(false)]
        public string BearishBackgroundBrushSerializable
        {
            get { return Serialize.BrushToString(BearishBackgroundBrush); }
            set { BearishBackgroundBrush = Serialize.StringToBrush(value); }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> ADXValue
        {
            get { return Values[0]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> DIPlusValue
        {
            get { return Values[1]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> DIMinusValue
        {
            get { return Values[2]; }
        }

        [NinjaScriptProperty]
        [Display(Name = "Show indicator label", Description = "Show NinjaTrader's chart name label. Sentinel default = OFF (clean chart); turn on to restore it.", GroupName = "Sentinel", Order = 100)]
        public bool ShowIndicatorLabel { get; set; }
        #endregion

        // ── HEARTBEAT (SentinelCore v1.19.0) ─────────────────────────────────────────────────
        // An OnBarClose publisher only refreshes its seam when a bar closes. In a quiet market bars close
        // slowly, the seam ages past the Council's StaleSec, and a perfectly healthy voter silently drops
        // out of the roster — observed live as a FULLY LOADED chart reporting "roster 3/10". The Council
        // already heartbeats its own verdict; its sensors need the same. Re-stamp the cached reading on
        // incoming quotes: no recompute, realtime only (a historical re-stamp would fake freshness onto a
        // replayed bar), throttled.
        private DateTime _lastHeartbeatUtc;
        private const double HeartbeatSec = 5.0;
        protected override void OnMarketData(NinjaTrader.Data.MarketDataEventArgs marketDataUpdate)
        {
            if (!PublishRegime || State != State.Realtime) return;
            DateTime now = DateTime.UtcNow;
            if ((now - _lastHeartbeatUtc).TotalSeconds < HeartbeatSec) return;
            _lastHeartbeatUtc = now;
            try { SentinelCore.TouchAdxState(Scope()); } catch { }
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private Sentinel.Sensors.ADXPro_v1_2_0[] cacheADXPro_v1_2_0;
		public Sentinel.Sensors.ADXPro_v1_2_0 ADXPro_v1_2_0(int period, double triggerLevel, double strongLevel, double flatSlopeThreshold, bool useBackground, int backgroundOpacity, bool showBiasTable, SentinelCardCorner cardCorner, bool publishRegime, bool showIndicatorLabel)
		{
			return ADXPro_v1_2_0(Input, period, triggerLevel, strongLevel, flatSlopeThreshold, useBackground, backgroundOpacity, showBiasTable, cardCorner, publishRegime, showIndicatorLabel);
		}

		public Sentinel.Sensors.ADXPro_v1_2_0 ADXPro_v1_2_0(ISeries<double> input, int period, double triggerLevel, double strongLevel, double flatSlopeThreshold, bool useBackground, int backgroundOpacity, bool showBiasTable, SentinelCardCorner cardCorner, bool publishRegime, bool showIndicatorLabel)
		{
			if (cacheADXPro_v1_2_0 != null)
				for (int idx = 0; idx < cacheADXPro_v1_2_0.Length; idx++)
					if (cacheADXPro_v1_2_0[idx] != null && cacheADXPro_v1_2_0[idx].Period == period && cacheADXPro_v1_2_0[idx].TriggerLevel == triggerLevel && cacheADXPro_v1_2_0[idx].StrongLevel == strongLevel && cacheADXPro_v1_2_0[idx].FlatSlopeThreshold == flatSlopeThreshold && cacheADXPro_v1_2_0[idx].UseBackground == useBackground && cacheADXPro_v1_2_0[idx].BackgroundOpacity == backgroundOpacity && cacheADXPro_v1_2_0[idx].ShowBiasTable == showBiasTable && cacheADXPro_v1_2_0[idx].CardCorner == cardCorner && cacheADXPro_v1_2_0[idx].PublishRegime == publishRegime && cacheADXPro_v1_2_0[idx].ShowIndicatorLabel == showIndicatorLabel && cacheADXPro_v1_2_0[idx].EqualsInput(input))
						return cacheADXPro_v1_2_0[idx];
			return CacheIndicator<Sentinel.Sensors.ADXPro_v1_2_0>(new Sentinel.Sensors.ADXPro_v1_2_0(){ Period = period, TriggerLevel = triggerLevel, StrongLevel = strongLevel, FlatSlopeThreshold = flatSlopeThreshold, UseBackground = useBackground, BackgroundOpacity = backgroundOpacity, ShowBiasTable = showBiasTable, CardCorner = cardCorner, PublishRegime = publishRegime, ShowIndicatorLabel = showIndicatorLabel }, input, ref cacheADXPro_v1_2_0);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.Sentinel.Sensors.ADXPro_v1_2_0 ADXPro_v1_2_0(int period, double triggerLevel, double strongLevel, double flatSlopeThreshold, bool useBackground, int backgroundOpacity, bool showBiasTable, SentinelCardCorner cardCorner, bool publishRegime, bool showIndicatorLabel)
		{
			return indicator.ADXPro_v1_2_0(Input, period, triggerLevel, strongLevel, flatSlopeThreshold, useBackground, backgroundOpacity, showBiasTable, cardCorner, publishRegime, showIndicatorLabel);
		}

		public Indicators.Sentinel.Sensors.ADXPro_v1_2_0 ADXPro_v1_2_0(ISeries<double> input , int period, double triggerLevel, double strongLevel, double flatSlopeThreshold, bool useBackground, int backgroundOpacity, bool showBiasTable, SentinelCardCorner cardCorner, bool publishRegime, bool showIndicatorLabel)
		{
			return indicator.ADXPro_v1_2_0(input, period, triggerLevel, strongLevel, flatSlopeThreshold, useBackground, backgroundOpacity, showBiasTable, cardCorner, publishRegime, showIndicatorLabel);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.Sentinel.Sensors.ADXPro_v1_2_0 ADXPro_v1_2_0(int period, double triggerLevel, double strongLevel, double flatSlopeThreshold, bool useBackground, int backgroundOpacity, bool showBiasTable, SentinelCardCorner cardCorner, bool publishRegime, bool showIndicatorLabel)
		{
			return indicator.ADXPro_v1_2_0(Input, period, triggerLevel, strongLevel, flatSlopeThreshold, useBackground, backgroundOpacity, showBiasTable, cardCorner, publishRegime, showIndicatorLabel);
		}

		public Indicators.Sentinel.Sensors.ADXPro_v1_2_0 ADXPro_v1_2_0(ISeries<double> input , int period, double triggerLevel, double strongLevel, double flatSlopeThreshold, bool useBackground, int backgroundOpacity, bool showBiasTable, SentinelCardCorner cardCorner, bool publishRegime, bool showIndicatorLabel)
		{
			return indicator.ADXPro_v1_2_0(input, period, triggerLevel, strongLevel, flatSlopeThreshold, useBackground, backgroundOpacity, showBiasTable, cardCorner, publishRegime, showIndicatorLabel);
		}
	}
}

#endregion
