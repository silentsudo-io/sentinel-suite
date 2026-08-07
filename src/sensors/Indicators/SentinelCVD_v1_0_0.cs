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
using NinjaTrader.NinjaScript.AddOns.Sentinel;   // SentinelSkin (glass card) + SentinelCore (CvdState seam) + SentinelCardCorner
#endregion

// ═════════════════════════════════════════════════════════════════════════════
//  SentinelCVD — session cumulative volume delta, and the part nobody plots    |   Version v1.0.0
//  File: SentinelCVD_v1_0_0.cs  |  namespace …Indicators.Sentinel  |  display Name "Sentinel CVD"
//
//  ⚠ NO ORDERS — read-only advisory indicator. Safe to run anywhere.
//
//  WHY THIS EXISTS
//  ---------------
//  The suite already CLOCKS on cumulative delta: SentinelFlux's θ is exactly that, and it closes a bar
//  when |θ| reaches its expectation (López de Prado imbalance bars). But θ RESETS EVERY BAR — it is the
//  clock, not a state — and `FluxState.Cvd`, the session-running figure, was computed, published, and
//  read by nobody. It is also only available where Flux is the chart's bar type.
//
//  SentinelCVD makes the session-scale read a first-class citizen on ANY bar type, and publishes the
//  three things that actually carry information:
//
//    1. SLOPE (+ z-score) — direction. The CVD LEVEL is close to meaningless: the session anchor is
//       arbitrary, so only the change matters. Anyone reading the level is reading an offset.
//    2. DIVERGENCE — price up while flow is down (or vice versa). The classic absorption tell.
//    3. EFFICIENCY — ⭐ the one nobody plots. Ticks of price bought per 1,000 contracts of NET
//       aggression. This is market IMPACT — Kyle's lambda in retail clothing. Rising CVD with LOW
//       efficiency means heavy buying that is going nowhere, i.e. someone is quietly filling into it.
//       High efficiency means a thin book where a little flow travels a long way. A CVD line alone
//       cannot show you either, and it is orthogonal to every price-derived voter in the Council —
//       which is the documented core problem ("conviction = agreement, not confirmation").
//
//  HONEST LIMITS — read before trusting a number
//  ---------------------------------------------
//    • CVD measures WHO CROSSED THE SPREAD, not net positioning. Every contract has a buyer and a
//      seller; "delta" is aggressor side only. It is a flow-pressure proxy, never a position.
//    • Signing is inferred. Quote rule where a real bid/ask is present, tick rule as fallback. Both are
//      estimators, and both are wrong on some prints.
//    • The level is anchor-dependent and accumulates signing error all session. Use slope/divergence.
//    • Block prints distort everything downstream (SentinelFlux had to winsorize E[|θ|] at 4× for
//      exactly this). Per-print volume is winsorized here for the same reason.
//    • WITHOUT TICK DATA the signing degrades to a bar-level proxy (close vs open) — far weaker.
//      `TickBacked=false` says so on the seam and the card shows a "bar-proxy" warning rather than
//      pretending. A degraded read that announces itself is worth having; one that does not, is not.
//
//  SENTINEL WIRING (Docs/SENTINEL_DESIGN_SYSTEM.md §6c/§6d)
//    • PUBLISH: SetCvdState(...) per update (default ON — needs SentinelCore ≥ v1.43.0).
//    • Council: the CVD voter (STATE) reads this seam. Wired in Council v1.9.x.
//    • Hidden ±1 `Signal` plot (transparent, IsAutoScale off) so the Deck SIGNAL ARM can read it
//      generically, per the suite convention for signal-emitting tools.
//    • Visible CVD line on its own panel + a SentinelSkin.Painter glass card + label remover.
//
//  CHANGELOG
//    v1.0.0 (2026-07-25) — initial. Session CVD from quote-rule signed tape (tick-rule fallback,
//             winsorized prints), slope EMA + z-score, flow-vs-price divergence, and the EFFICIENCY /
//             impact read. Publishes SentinelCore.CvdState. Panel plot + glass card + hidden signal plot.
// ═════════════════════════════════════════════════════════════════════════════
namespace NinjaTrader.NinjaScript.Indicators.Sentinel
{
    public class SentinelCVD_v1_0_0 : Indicator
    {
        // ── tape state ──
        private double _cvd;                  // session cumulative signed delta
        private double _barDelta;             // signed delta accruing in the forming bar
        private double _prevBid, _prevAsk;    // last known quote for the quote rule
        private double _lastPrice;            // for the tick-rule fallback
        private int    _lastTickSign;         // carry for unchanged-price prints
        private bool   _sawTape;              // any real OnMarketData Last print this session
        private double _volEwma;              // EWMA of per-print volume — the winsorizing reference

        // ── per-bar series ──
        private double _slopeEma;             // EMA of per-bar ΔCVD
        private double _slopeVar;             // EWMA variance of ΔCVD (for the z-score)
        private double _effEma;               // EMA of efficiency
        private double _effVar;
        private double _sessHi, _sessLo;
        private int    _barsThisSession;
        private DateTime _sessionDate = DateTime.MinValue;

        // ── cached display values (computed in OnBarUpdate; ONLY read in OnRender — card render rule) ──
        private double _dSlope, _dSlopeZ, _dEff, _dEffZ, _dCvd;
        private int    _dDir, _dPriceDir, _dDiverge;
        private bool   _dTickBacked, _hasData;
        private int    _lastLoggedDir = -999;

        private SentinelSkin.Painter _sp;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Sentinel session cumulative volume delta — publishes slope, flow-vs-price divergence and EFFICIENCY (ticks of price per 1,000 contracts of net aggression, i.e. market impact) as SentinelCore.CvdState so the Council can vote on order flow independently of price.";
                Name                     = "Sentinel CVD v1.0.0";
                Calculate                = Calculate.OnEachTick;
                IsOverlay                = false;          // own panel — the CVD line is the point
                IsSuspendedWhileInactive = true;
                DisplayInDataBox         = true;
                DrawOnPricePanel         = false;
                IsAutoScale              = true;

                SlopePeriod    = 14;    // bars in the slope EMA / z-score window
                Deadband       = 0.35;  // |slopeZ| below this reads as no direction (flat flow)
                DivergenceMin  = 0.50;  // |slopeZ| needed before a flow/price disagreement counts as divergence
                WinsorMult     = 4.0;   // clamp a single print's volume at N x its EWMA (block-trade guard)

                PublishState       = true;
                LogChanges         = true;
                ShowCard           = true;
                CardCorner         = SentinelCardCorner.TopLeft;
                ShowIndicatorLabel = false;

                AddPlot(new Stroke(System.Windows.Media.Brushes.DeepSkyBlue, 2), PlotStyle.Line, "Cvd");
                AddPlot(new Stroke(System.Windows.Media.Brushes.Transparent, 1), PlotStyle.Line, "Signal");
            }
            else if (State == State.Configure)
            {
                // Real signing needs the tape. Without it we degrade to a bar proxy and SAY SO.
                Calculate = Calculate.OnEachTick;
            }
            else if (State == State.DataLoaded)
            {
                if (!ShowIndicatorLabel) Name = string.Empty;      // Sentinel label remover
                Plots[1].Brush = System.Windows.Media.Brushes.Transparent;   // hidden ±1 signal for the Deck
            }
            else if (State == State.Terminated)
            {
                if (_sp != null) { try { _sp.Dispose(); } catch (Exception _sx) { SentinelCore.Swallow("SentinelCVD.OnStateChange", _sx); } _sp = null; }
                try { SentinelSkin.CardLayout.Release(this); } catch (Exception _sx) { SentinelCore.Swallow("SentinelCVD.OnStateChange", _sx); }
                try { SentinelCore.ClearCvdScope(Scope()); } catch (Exception _sx) { SentinelCore.Swallow("SentinelCVD.OnStateChange", _sx); }
            }
        }

        private string Scope()
        {
            try { return SentinelCore.ScopeOf(Instrument, BarsPeriod); }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelCVD.Scope", _sx); return null; }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  TAPE — quote rule first (a real bid/ask is the better estimator), tick rule as fallback.
        //  Mirrors the signing already proven in SentinelFlux / SentinelDrift, deliberately: three
        //  tools disagreeing about what a "buy" is would be worse than any of them being slightly wrong.
        // ─────────────────────────────────────────────────────────────────────
        protected override void OnMarketData(MarketDataEventArgs e)
        {
            if (e == null) return;
            try
            {
                if (e.MarketDataType == MarketDataType.Bid) { _prevBid = e.Price; return; }
                if (e.MarketDataType == MarketDataType.Ask) { _prevAsk = e.Price; return; }
                if (e.MarketDataType != MarketDataType.Last) return;

                _sawTape = true;
                double vol = e.Volume;
                if (vol <= 0) return;

                // Winsorize the print. One block can otherwise dominate a whole session's delta — the
                // same failure that spiked SentinelFlux's threshold to 149 and left its clock dormant.
                _volEwma = _volEwma <= 0 ? vol : _volEwma + 0.02 * (vol - _volEwma);
                double cap = _volEwma * WinsorMult;
                if (cap > 0 && vol > cap) vol = cap;

                int sign;
                if (_prevAsk > 0 && _prevBid > 0 && _prevAsk > _prevBid)
                {
                    // QUOTE RULE — at/above ask = aggressive buy, at/below bid = aggressive sell.
                    if (e.Price >= _prevAsk)      sign =  1;
                    else if (e.Price <= _prevBid) sign = -1;
                    else                          sign =  0;   // inside the spread: genuinely ambiguous, count neither
                }
                else
                {
                    // TICK RULE fallback — direction of the price change, carrying the last sign on no change.
                    if (_lastPrice > 0 && e.Price > _lastPrice)      sign =  1;
                    else if (_lastPrice > 0 && e.Price < _lastPrice) sign = -1;
                    else                                             sign = _lastTickSign;
                }
                if (sign != 0) _lastTickSign = sign;
                _lastPrice = e.Price;

                _cvd      += sign * vol;
                _barDelta += sign * vol;
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelCVD.OnMarketData", _sx); }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 2 || Instrument == null || Instrument.MasterInstrument == null) return;

            try
            {
                // ── session roll ──
                DateTime day = Times[0][0].Date;
                if (day != _sessionDate)
                {
                    _sessionDate = day;
                    _cvd = 0; _barDelta = 0; _barsThisSession = 0;
                    _sessHi = 0; _sessLo = 0;
                }

                if (IsFirstTickOfBar && CurrentBar > 2)
                {
                    // ── close out the PREVIOUS bar: this is where the derivatives are computed ──
                    double dCvd = _barDelta;

                    // Degraded path: no tape at all (historical rebuild without tick replay). Estimate the
                    // bar's delta from its body as a PROXY and flag it, rather than silently reporting zero.
                    bool tickBacked = _sawTape;
                    if (!tickBacked)
                    {
                        double rng = Math.Max(High[1] - Low[1], TickSize);
                        double body = Close[1] - Open[1];
                        dCvd = Volume[1] * Math.Max(-1.0, Math.Min(1.0, body / rng));
                        _cvd += dCvd;
                    }

                    _barsThisSession++;
                    _sessHi = _barsThisSession == 1 ? _cvd : Math.Max(_sessHi, _cvd);
                    _sessLo = _barsThisSession == 1 ? _cvd : Math.Min(_sessLo, _cvd);

                    // slope: EMA of per-bar ΔCVD + an EWMA variance for the z-score
                    double a = 2.0 / (Math.Max(2, SlopePeriod) + 1.0);
                    _slopeEma += a * (dCvd - _slopeEma);
                    double dev = dCvd - _slopeEma;
                    _slopeVar += a * (dev * dev - _slopeVar);
                    double sd  = Math.Sqrt(Math.Max(_slopeVar, 1e-9));
                    double slopeZ = sd > 0 ? _slopeEma / sd : 0;

                    // ── EFFICIENCY: ticks of price per 1,000 contracts of NET delta over the window ──
                    // The impact read. |ΔP| against |Δflow|, so it is a magnitude, not a direction.
                    double lookback = Math.Min(CurrentBar - 1, Math.Max(2, SlopePeriod));
                    double dPriceTicks = (Close[1] - Close[(int)lookback]) / TickSize;
                    double netFlow = Math.Abs(_slopeEma) * lookback;
                    double eff = netFlow > 1 ? Math.Abs(dPriceTicks) / (netFlow / 1000.0) : 0;
                    _effEma += a * (eff - _effEma);
                    double edev = eff - _effEma;
                    _effVar += a * (edev * edev - _effVar);
                    double esd = Math.Sqrt(Math.Max(_effVar, 1e-9));
                    double effZ = esd > 0 ? (eff - _effEma) / esd : 0;

                    int dir = Math.Abs(slopeZ) < Deadband ? 0 : (slopeZ > 0 ? 1 : -1);
                    int priceDir = dPriceTicks > 0 ? 1 : (dPriceTicks < 0 ? -1 : 0);

                    // Divergence: flow and price disagree, and the flow read is strong enough to mean it.
                    // +1 = bullish (price down, flow up) · -1 = bearish (price up, flow down).
                    int diverge = 0;
                    if (Math.Abs(slopeZ) >= DivergenceMin && dir != 0 && priceDir != 0 && dir != priceDir)
                        diverge = dir > 0 ? 1 : -1;

                    _dCvd = _cvd; _dSlope = _slopeEma; _dSlopeZ = slopeZ; _dDir = dir;
                    _dPriceDir = priceDir; _dDiverge = diverge; _dEff = eff; _dEffZ = effZ;
                    _dTickBacked = tickBacked; _hasData = true;

                    if (PublishState)
                    {
                        string sc = Scope();
                        if (!string.IsNullOrEmpty(sc))
                            SentinelCore.SetCvdState(sc, SentinelCore.BarTag(BarsPeriod),
                                Instrument.MasterInstrument.Name, _cvd, _slopeEma, slopeZ, dir, priceDir,
                                diverge, eff, effZ, _sessHi, _sessLo, _barsThisSession, tickBacked, "SentinelCVD");
                    }

                    if (LogChanges && State == State.Realtime && dir != _lastLoggedDir)
                    {
                        _lastLoggedDir = dir;
                        SentinelCore.Log("CVD", Instrument.MasterInstrument.Name +
                            " " + (dir > 0 ? "flow UP" : dir < 0 ? "flow DOWN" : "flow flat") +
                            " cvd=" + _cvd.ToString("F0") + " z=" + slopeZ.ToString("F2") +
                            " eff=" + eff.ToString("F1") + "t/1k" +
                            (diverge != 0 ? (diverge > 0 ? " DIVERGENCE(bull)" : " DIVERGENCE(bear)") : "") +
                            (tickBacked ? "" : " [bar-proxy]"));
                    }

                    _barDelta = 0;
                }

                Values[0][0] = _cvd;
                Values[1][0] = _dDir;
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelCVD.OnBarUpdate", _sx); }
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

                const float cw = 244f, ch = 150f;
                var slot = SentinelSkin.CardLayout.Place(this, ChartPanel,
                    ChartPanel.X, ChartPanel.Y, ChartPanel.W, ChartPanel.H, CardCorner, cw, ch);

                if (!_hasData)
                {
                    var rw = _sp.Card(slot.X, slot.Y, cw, ch, SentinelSkin.CLine);
                    _sp.Dot(rw.Left + 5f, rw.Top + 8f, SentinelSkin.CMute, false);
                    _sp.Text("CVD", rw.Left + 16f, rw.Top, rw.Width - 20f, 16f, SentinelSkin.CInk, 11f, true);
                    _sp.Text("reading tape…", rw.Left, rw.Top + 26f, rw.Width, 14f, SentinelSkin.CMute, 10.5f);
                    _sp.End();
                    return;
                }

                var trail = SharpDX.DirectWrite.TextAlignment.Trailing;
                var dirCol = _dDir > 0 ? SentinelSkin.CUp : _dDir < 0 ? SentinelSkin.CDown : SentinelSkin.CMute;
                var edge   = _dDiverge != 0 ? SentinelSkin.CWarn : (_dDir != 0 ? SentinelSkin.CAccent : SentinelSkin.CLine);
                var r = _sp.Card(slot.X, slot.Y, cw, ch, edge);

                _sp.Dot(r.Left + 5f, r.Top + 8f, _dDir != 0 ? SentinelSkin.CAccent : SentinelSkin.CMute, _dDir != 0);
                _sp.Text("CVD", r.Left + 16f, r.Top, r.Width - 80f, 16f, SentinelSkin.CInk, 11f, true);
                _sp.Pill(_dTickBacked ? "TAPE" : "PROXY", r.Right, r.Top - 1f,
                         _dTickBacked ? SentinelSkin.CAccent : SentinelSkin.CWarn);

                // hero — session CVD + direction
                _sp.Text("SESSION DELTA", r.Left, r.Top + 24f, 120f, 12f, SentinelSkin.CMute, 8.5f, true);
                _sp.Text((_dCvd >= 0 ? "+" : "") + _dCvd.ToString("N0"), r.Left, r.Top + 34f, r.Width, 24f, dirCol, 17f, false);
                _sp.Text(_dDir > 0 ? "▲ flow up" : _dDir < 0 ? "▼ flow down" : "flat",
                         r.Left, r.Top + 40f, r.Width, 16f, dirCol, 10.5f, true, trail);

                _sp.Divider(r.Left, r.Top + 66f, r.Right);

                // slope z + efficiency — the two reads that carry the information
                _sp.Text("slope z " + _dSlopeZ.ToString("F2"), r.Left, r.Top + 72f, r.Width, 14f, SentinelSkin.CInk2, 10f);
                _sp.Text("impact " + _dEff.ToString("F1") + "t/1k", r.Left, r.Top + 72f, r.Width, 14f,
                         _dEffZ < -1.0 ? SentinelSkin.CWarn : SentinelSkin.CInk2, 10f, true, trail);

                // the interpretation line — say what it MEANS, not just what it is
                string read = _dDiverge > 0 ? "DIVERGENCE — price down, flow up"
                            : _dDiverge < 0 ? "DIVERGENCE — price up, flow down"
                            : _dEffZ < -1.0 ? "ABSORBING — heavy flow, little price"
                            : _dEffZ >  1.0 ? "THIN — price travelling on light flow"
                            : _dDir != 0    ? "flow and price agree"
                                            : "no net pressure";
                var readCol = _dDiverge != 0 ? SentinelSkin.CWarn
                            : Math.Abs(_dEffZ) > 1.0 ? SentinelSkin.CAccent : SentinelSkin.CMute;
                _sp.Text(read, r.Left, r.Top + 92f, r.Width, 14f, readCol, 9.5f, true);

                if (!_dTickBacked)
                    _sp.Text("bar proxy — no tick data, signing degraded", r.Left, r.Top + 110f, r.Width, 14f,
                             SentinelSkin.CWarn, 9f);
                else
                    _sp.Text("session " + _sessLo.ToString("N0") + " … " + _sessHi.ToString("N0"),
                             r.Left, r.Top + 110f, r.Width, 14f, SentinelSkin.CMute, 9f);

                _sp.End();
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelCVD.OnRender", _sx); }
        }

        #region Properties
        [Range(2, 500)]
        [NinjaScriptProperty]
        [Display(Name = "Slope Period", Description = "Bars in the CVD slope EMA and its z-score window.", Order = 1, GroupName = "CVD")]
        public int SlopePeriod { get; set; }

        [Range(0.0, 5.0)]
        [NinjaScriptProperty]
        [Display(Name = "Deadband (z)", Description = "|slope z| below this reads as no direction. Keeps a drifting tape from voting.", Order = 2, GroupName = "CVD")]
        public double Deadband { get; set; }

        [Range(0.0, 5.0)]
        [NinjaScriptProperty]
        [Display(Name = "Divergence Min (z)", Description = "|slope z| required before a flow-vs-price disagreement is called a divergence.", Order = 3, GroupName = "CVD")]
        public double DivergenceMin { get; set; }

        [Range(1.0, 50.0)]
        [NinjaScriptProperty]
        [Display(Name = "Winsor Multiple", Description = "Clamp one print's volume at N x its EWMA. Block-trade guard — a single print must not own the session's delta.", Order = 4, GroupName = "CVD")]
        public double WinsorMult { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Publish CVD to Sentinel", Description = "Publish as SentinelCore.CvdState so the Council can vote on it. Needs SentinelCore >= v1.43.0.", Order = 10, GroupName = "Sentinel")]
        public bool PublishState { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Log Direction Changes", Description = "Write flow-direction changes to sentinel.log (realtime only).", Order = 11, GroupName = "Sentinel")]
        public bool LogChanges { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Card", Order = 12, GroupName = "Sentinel")]
        public bool ShowCard { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Card Corner", Description = "Which chart corner the Sentinel card docks to. Cards in the same corner auto-stack.", Order = 13, GroupName = "Sentinel")]
        public SentinelCardCorner CardCorner { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show indicator label", Description = "Show NinjaTrader's chart name label. Sentinel default = OFF.", GroupName = "Sentinel", Order = 100)]
        public bool ShowIndicatorLabel { get; set; }

        [Browsable(false)][XmlIgnore] public Series<double> Cvd    { get { return Values[0]; } }
        [Browsable(false)][XmlIgnore] public Series<double> Signal { get { return Values[1]; } }
        #endregion
    }
}
