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
using System.Linq;
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
using NinjaTrader.NinjaScript.AddOns.Sentinel;   // SentinelSkin (glass card) + SentinelCore (FlowState seam) + SentinelCardCorner
using NinjaTrader.NinjaScript.Indicators.Sentinel;
#endregion

// ═════════════════════════════════════════════════════════════════════════════
//  Sentinel Flow — the ORDER-FLOW axis (CLEAN-ROOM)                          |   Version v1.0.0
//  File: SentinelFlow_v1_0_0.cs   |   namespace …Indicators.Sentinel (Context AXIS)   |   Name "Sentinel Flow"
//
//  ⚠ NO ORDERS — read-only advisory indicator. Safe to run anywhere.
//
//  CLEAN-ROOM ORIGINAL. Written from scratch off PUBLIC, non-copyrightable methods — the tick (uptick/downtick)
//  trade-classification rule, cumulative volume delta, and ordinary-least-squares linear regression. It uses NO
//  third-party code. It is the suite's answer to the acknowledged FLOW gap; the installed Alighten/vga CVD family
//  (all descended from "Volume Delta by Gill", 2019, unlicensed) and RedTail profile tools were surveyed as design
//  references only — none of their code was copied. See the provenance audit + NOTICE.
//
//  WHY IT MATTERS — every other Council voter is price-derived (ADX/CCI/Trend/Envelope/Brick echo the same OHLC).
//  CUMULATIVE VOLUME DELTA is the one axis built from the transaction tape, so it can CONFIRM or DIVERGE from price.
//
//  THE PUBLIC METHOD:
//    • tick rule       — a trade printing above the prior trade is buyer-initiated (+vol); below is seller (−vol);
//                        an unchanged print carries the prior sign. Session CVD = running sum of signed volume.
//    • flow regime     — OLS regression of the last N session-CVD samples vs bar index → Slope + R² (fit quality).
//    • strength (0..1) = R² × min(1, |slope| / mean|ΔCVD|) — how convincingly, and how cleanly, flow leans.
//    • divergence      — price change vs CVD change over the window disagree (price up while CVD falls = bearish).
//    • Signal          = Bias (= sign of Slope) once R² ≥ gate AND strength ≥ gate, else 0 — the CONFIRMED flow.
//
//  THE SENTINEL PLUMBING (our own code — makes it a suite member):
//    • PUBLISHES SentinelCore.FlowState (Bias / Cvd / Slope / RSquared / Strength / Divergence / Signal).
//    • WIRED INTO THE COUNCIL as the FLOW voter (a STATE voter on FlowState.Signal).
//    • Hidden ±1 "Signal" PLOT (Values[1], transparent) for the Deck SIGNAL ARM / generic consumers.
//    • A SentinelSkin.Painter glass card + label remover + roster heartbeat.
//
//  CHANGELOG
//    v1.0.0 (2026-07-12) — NEW. Clean-room CVD/order-flow axis (tick-rule CVD + OLS regime + divergence).
//             FlowState publish, Council FLOW voter, hidden Signal plot, glass card, scope key + heartbeat.
// ═════════════════════════════════════════════════════════════════════════════
namespace NinjaTrader.NinjaScript.Indicators.Sentinel
{
	public class SentinelFlow_v1_0_0 : Indicator
	{
		private SentinelSkin.Painter _sp;
		private bool   _hasData;

		// tick-rule CVD accumulation (computed on the 1-tick series)
		private double _sessionCvd;
		private double _prevTickPrice = double.NaN;
		private int    _lastTickSign;
		private bool   _resetPending;

		// rolling window of session-CVD sampled at each primary bar close
		private readonly List<double> _cvd   = new List<double>();
		private readonly List<double> _price = new List<double>();

		// cached state (computed in OnBarUpdate; drawn in OnRender)
		private int    _bias;
		private double _slope;
		private double _rSq;
		private double _strength;
		private int    _divergence;
		private int    _sig;
		private int    _lastLoggedSig = -999;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description              = "Clean-room order-flow axis: tick-rule cumulative volume delta, OLS regression regime, and price-vs-CVD divergence. Publishes SentinelCore.FlowState so the Council gains a FLOW voter — the one axis not derived from price.";
				Name                     = "Sentinel Flow v1.0.0";
				Calculate                = Calculate.OnBarClose;
				IsOverlay                = false;
				DisplayInDataBox         = true;
				DrawOnPricePanel         = false;
				DrawHorizontalGridLines  = true;
				DrawVerticalGridLines    = false;
				PaintPriceMarkers        = true;
				ScaleJustification       = NinjaTrader.Gui.Chart.ScaleJustification.Right;
				IsSuspendedWhileInactive = true;

				Window        = 20;
				RSquaredGate  = 0.30;
				StrengthGate  = 0.40;

				PublishState       = true;
				LogChanges         = true;
				ShowCard           = true;
				CardCorner         = SentinelCardCorner.TopRight;
				ShowIndicatorLabel = false;

				// CARD-ONLY: the CVD line is hidden. Its unbounded ±thousands range dominated any panel it shared
				// (it crushed Regime's 0..1 plot to a flat row), so the glass card is the readout. Values[0] (Cvd) still
				// populates the DataBox + FlowState seam. Want the CVD line back? Put THIS indicator on its own panel.
				AddPlot(new Stroke(Brushes.Transparent, 2), PlotStyle.Line, "Cvd");
				// hidden ±1 confirmed-flow signal (Values[1]) — transparent; readable by the Deck SIGNAL ARM.
				AddPlot(new Stroke(Brushes.Transparent, 1f), PlotStyle.Line, "Signal");
			}
			else if (State == State.Configure)
			{
				// the tape — a 1-tick series drives the tick-rule CVD (BarsInProgress == 1).
				AddDataSeries(BarsPeriodType.Tick, 1);
			}
			else if (State == State.DataLoaded)
			{
				if (!ShowIndicatorLabel) Name = string.Empty;   // Sentinel label remover
			}
			else if (State == State.Terminated)
			{
				if (_sp != null) { try { _sp.Dispose(); } catch (Exception _sx) { SentinelCore.Swallow("SentinelFlow.OnStateChange", _sx); } _sp = null; }
				try { SentinelSkin.CardLayout.Release(this); } catch (Exception _sx) { SentinelCore.Swallow("SentinelFlow.OnStateChange", _sx); }
			}
		}

		// ── scope key ("<masterInstrument>.<barTag>" — ONE CHART's worth of context). Lazily resolved + cached. ──
		private string _scope;
		private string Scope()
		{
			if (_scope == null) { try { _scope = SentinelCore.ScopeOf(Instrument, BarsPeriod); } catch (Exception _sx) { SentinelCore.Swallow("SentinelFlow.Scope", _sx); } }
			return _scope;
		}

		// ── HEARTBEAT — re-stamp the cached seam on quotes so a healthy voter doesn't age out of the roster. ──
		private DateTime _lastHeartbeatUtc;
		private const double HeartbeatSec = 5.0;
		protected override void OnMarketData(NinjaTrader.Data.MarketDataEventArgs marketDataUpdate)
		{
			if (!PublishState || State != State.Realtime) return;
			DateTime now = DateTime.UtcNow;
			if ((now - _lastHeartbeatUtc).TotalSeconds < HeartbeatSec) return;
			_lastHeartbeatUtc = now;
			try { SentinelCore.TouchFlowState(Scope()); } catch (Exception _sx) { SentinelCore.Swallow("SentinelFlow.OnMarketData", _sx); }
		}

		protected override void OnBarUpdate()
		{
			// ── the tape (1-tick series): accumulate tick-rule CVD ──
			if (BarsInProgress == 1)
			{
				if (_resetPending) { _sessionCvd = 0; _prevTickPrice = double.NaN; _lastTickSign = 0; _resetPending = false; }

				double px  = Close[0];
				double vol = Volume[0];
				int sign;
				if (double.IsNaN(_prevTickPrice) || px == _prevTickPrice) sign = _lastTickSign;   // unchanged → carry
				else sign = px > _prevTickPrice ? 1 : -1;
				if (sign != 0) { _sessionCvd += sign * vol; _lastTickSign = sign; }
				_prevTickPrice = px;
				return;
			}

			if (BarsInProgress != 0) return;

			// new session → reset CVD accumulation at the next tick
			if (Bars.IsFirstBarOfSession && CurrentBar > 0)
				_resetPending = true;

			// sample session-CVD + price at this bar close into the rolling window
			_cvd.Add(_sessionCvd);
			_price.Add(Close[0]);
			int cap = Math.Max(4, Window) + 2;
			if (_cvd.Count   > cap) _cvd.RemoveAt(0);
			if (_price.Count > cap) _price.RemoveAt(0);

			Cvd[0] = _sessionCvd;

			if (_cvd.Count < Math.Max(4, Window)) return;

			// ── OLS regression of the last N CVD samples vs index ──
			int n = Window;
			int start = _cvd.Count - n;
			double sx = 0, sy = 0, sxx = 0, sxy = 0, syy = 0, sumAbsD = 0;
			for (int i = 0; i < n; i++)
			{
				double x = i;
				double y = _cvd[start + i];
				sx += x; sy += y; sxx += x * x; sxy += x * y; syy += y * y;
				if (i > 0) sumAbsD += Math.Abs(_cvd[start + i] - _cvd[start + i - 1]);
			}
			double denom = (n * sxx - sx * sx);
			double slope = denom != 0 ? (n * sxy - sx * sy) / denom : 0;
			// R² of the fit
			double ssTot = syy - (sy * sy) / n;
			double num   = (n * sxy - sx * sy);
			double rSq   = (denom != 0 && ssTot > 1e-9) ? (num * num) / (denom * (n * ssTot)) : 0;
			if (rSq < 0) rSq = 0; if (rSq > 1) rSq = 1;

			double meanAbsD = (n > 1) ? sumAbsD / (n - 1) : 0;
			double strength = rSq * Math.Min(1.0, meanAbsD > 1e-9 ? Math.Abs(slope) / meanAbsD : 0);
			if (strength > 1) strength = 1;

			int bias = slope > 0 ? 1 : (slope < 0 ? -1 : 0);

			// ── divergence: price change vs CVD change over the window disagree ──
			double dPrice = _price[_price.Count - 1] - _price[start];
			double dCvd   = _cvd[_cvd.Count - 1]     - _cvd[start];
			int divergence = 0;
			if (dPrice > 0 && dCvd < 0) divergence = -1;   // price up, flow down → bearish
			else if (dPrice < 0 && dCvd > 0) divergence = 1;   // price down, flow up → bullish

			int sig = (rSq >= RSquaredGate && strength >= StrengthGate) ? bias : 0;
			Signal[0] = sig;

			_bias = bias; _slope = slope; _rSq = rSq; _strength = strength; _divergence = divergence; _sig = sig; _hasData = true;

			if (PublishState && Instrument != null && Instrument.MasterInstrument != null)
			{
				try
				{
					SentinelCore.SetFlowState(new SentinelCore.FlowState
					{
						Scope      = Scope(),
						Bartype    = SentinelCore.BarTag(BarsPeriod),
						Instrument = Instrument.MasterInstrument.Name,
						Bias       = bias,
						Cvd        = _sessionCvd,
						Slope      = slope,
						RSquared   = rSq,
						Strength   = strength,
						Divergence = divergence,
						Signal     = sig,
						Source     = "FLOW"
					});
				}
				catch (Exception _sx) { SentinelCore.Swallow("SentinelFlow.OnBarUpdate", _sx); }
			}

			if (LogChanges && State == State.Realtime && sig != _lastLoggedSig)
			{
				_lastLoggedSig = sig;
				try
				{
					string inst = Instrument != null && Instrument.MasterInstrument != null ? Instrument.MasterInstrument.Name : "?";
					SentinelCore.Log("FLOW", inst + " " +
						(sig > 0 ? "flow ▲ (buyers)" : sig < 0 ? "flow ▼ (sellers)" : "flow ~ (balanced)") +
						" r2=" + rSq.ToString("0.00") + " str=" + strength.ToString("0.00") +
						(divergence != 0 ? (divergence > 0 ? " +DIVERGENCE" : " -DIVERGENCE") : ""));
				}
				catch (Exception _sx) { SentinelCore.Swallow("SentinelFlow.OnBarUpdate", _sx); }
			}
		}

		// ── glass card ──
		protected override void OnRender(NinjaTrader.Gui.Chart.ChartControl chartControl, NinjaTrader.Gui.Chart.ChartScale chartScale)
		{
			base.OnRender(chartControl, chartScale);
			if (RenderTarget == null || ChartPanel == null) return;
			if (_sp == null) _sp = new SentinelSkin.Painter();
			_sp.Begin(RenderTarget);
			try { if (ShowCard) RenderCard(); } catch (Exception _sx) { SentinelCore.Swallow("SentinelFlow.OnRender", _sx); }
			try { _sp.End(); } catch (Exception _sx) { SentinelCore.Swallow("SentinelFlow.OnRender", _sx); }
		}

		private void RenderCard()
		{
			const float cw = 228f, ch = 132f;
			var slot = SentinelSkin.CardLayout.Place(this, ChartPanel,
				ChartPanel.X, ChartPanel.Y, ChartPanel.W, ChartPanel.H, CardCorner, cw, ch);

			if (!_hasData)
			{
				var rw = _sp.Card(slot.X, slot.Y, cw, ch, SentinelSkin.CLine);
				_sp.Dot(rw.Left + 5f, rw.Top + 8f, SentinelSkin.CMute, false);
				_sp.Text("FLOW", rw.Left + 16f, rw.Top, rw.Width - 20f, 16f, SentinelSkin.CInk, 11f, true);
				_sp.Text("warming up…", rw.Left, rw.Top + 26f, rw.Width, 14f, SentinelSkin.CMute, 10.5f);
				return;
			}

			var trail   = SharpDX.DirectWrite.TextAlignment.Trailing;
			bool live    = _sig != 0;
			var dirCol  = _bias > 0 ? SentinelSkin.CUp : _bias < 0 ? SentinelSkin.CDown : SentinelSkin.CMute;
			var heroCol = live ? dirCol : SentinelSkin.CMute;
			var edge    = live ? SentinelSkin.CAccent : SentinelSkin.CLine;
			var r = _sp.Card(slot.X, slot.Y, cw, ch, edge);

			_sp.Dot(r.Left + 5f, r.Top + 8f, live ? SentinelSkin.CAccent : SentinelSkin.CMute, live);
			_sp.Text("FLOW", r.Left + 16f, r.Top, r.Width - 70f, 16f, SentinelSkin.CInk, 11f, true);
			_sp.Pill(_bias > 0 ? "BUYERS" : _bias < 0 ? "SELLERS" : "BALANCED", r.Right, r.Top - 1f, dirCol);

			_sp.Text("CUM DELTA", r.Left, r.Top + 24f, 120f, 12f, SentinelSkin.CMute, 8.5f, true);
			_sp.Text(_sig > 0 ? "FLOW ▲" : _sig < 0 ? "FLOW ▼" : "balanced",
				r.Left, r.Top + 34f, r.Width, 24f, heroCol, 17f, false);

			_sp.Divider(r.Left, r.Top + 66f, r.Right);
			_sp.Text("cvd " + _slope.ToString("+0.#;-0.#") + "/bar", r.Left, r.Top + 72f, r.Width, 14f, SentinelSkin.CInk2, 10f);
			_sp.Text("r² " + _rSq.ToString("0.00"), r.Left, r.Top + 72f, r.Width, 14f, SentinelSkin.CInk2, 10f, true, trail);
			_sp.Text("strength " + _strength.ToString("0.00"), r.Left, r.Top + 90f, r.Width, 14f, SentinelSkin.CMute, 10f);
			if (_divergence != 0)
				_sp.Text(_divergence > 0 ? "bullish divergence" : "bearish divergence",
					r.Left, r.Top + 90f, r.Width, 14f, SentinelSkin.CWarn, 10f, true, trail);
		}

		#region Properties
		[NinjaScriptProperty]
		[Range(4, int.MaxValue)]
		[Display(Name="Window", Description="Bars in the CVD regression window.", Order=1, GroupName="Parameters")]
		public int Window { get; set; }

		[NinjaScriptProperty]
		[Range(0.0, 1.0)]
		[Display(Name="R² Gate", Description="Minimum regression fit quality before flow confirms a direction.", Order=2, GroupName="Parameters")]
		public double RSquaredGate { get; set; }

		[NinjaScriptProperty]
		[Range(0.0, 1.0)]
		[Display(Name="Strength Gate", Description="Minimum flow strength (R² × normalized slope) before Signal fires.", Order=3, GroupName="Parameters")]
		public double StrengthGate { get; set; }

		// ── Sentinel ──
		[NinjaScriptProperty]
		[Display(Name="Publish Flow to Sentinel", Description="Publish the CVD/flow regime as SentinelCore.FlowState so the Council can vote on it.", Order=20, GroupName="Sentinel")]
		public bool PublishState { get; set; }

		[NinjaScriptProperty]
		[Display(Name="Log Signal Changes", Description="Write flow-signal transitions to sentinel.log.", Order=21, GroupName="Sentinel")]
		public bool LogChanges { get; set; }

		[NinjaScriptProperty]
		[Display(Name="Show Card", Order=22, GroupName="Sentinel")]
		public bool ShowCard { get; set; }

		[NinjaScriptProperty]
		[Display(Name="Card Corner", Description="Which panel corner the Sentinel card docks to. Cards in the same corner auto-stack.", Order=23, GroupName="Sentinel")]
		public SentinelCardCorner CardCorner { get; set; }

		[NinjaScriptProperty]
		[Display(Name="Show indicator label", Description="Show NinjaTrader's chart name label. Sentinel default = OFF; turn on to restore it.", Order=100, GroupName="Sentinel")]
		public bool ShowIndicatorLabel { get; set; }

		// ── plot series accessors ──
		[Browsable(false)] [XmlIgnore] public Series<double> Cvd    => Values[0];
		[Browsable(false)] [XmlIgnore] public Series<double> Signal => Values[1];   // ±1 confirmed flow / 0
		#endregion
	}
}
