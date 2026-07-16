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
using NinjaTrader.NinjaScript.AddOns.Sentinel;   // SentinelSkin (glass card) + SentinelCore (AdxvmaState seam) + SentinelCardCorner
using NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors;
#endregion

// ═════════════════════════════════════════════════════════════════════════════
//  Sentinel ADXVMA — the ADX Volatility Moving Average axis (CLEAN-ROOM)      |   Version v1.0.0
//  File: SentinelADXVMA_v1_0_0.cs   |   namespace …Indicators.Sentinel.Sensors (Tier-③ SENSOR)   |   Name "Sentinel ADXVMA"
//
//  ⚠ NO ORDERS — read-only advisory indicator. Safe to run anywhere.
//
//  CLEAN-ROOM ORIGINAL. Written from scratch off the PUBLIC ADXVMA (ADX Volatility Moving Average)
//  method — a published, non-copyrightable adaptive-moving-average formula. It uses NO third-party code.
//  The installed AuADXVMA.cs / auADXVMASignalMod.cs (an unlicensed "Au" pack) were surveyed as design
//  references for the CONCEPT ONLY — the recursion below is a fresh implementation of the public formula;
//  no code was copied. See the provenance audit + NOTICE.
//
//  WHY IT MATTERS — a plain EMA lags equally in trend and in chop. ADXVMA drives its smoothing constant
//  from a Wilder-smoothed directional-volatility index: when direction is strong the MA snaps toward price;
//  when the tape is indecisive the MA nearly freezes. The result is a self-adaptive trend rail the Council
//  can lean with, plus a clean chop read (the MA goes flat) that context modulators care about.
//
//  THE PUBLIC FORMULA:
//    • up / down moves    : upMove = max(Close−Close[1], 0) · downMove = max(Close[1]−Close, 0)
//    • Wilder smooth (k=1/Period, i.e. rma): up  = (1−k)·up[1]  + k·upMove
//                                            down = (1−k)·down[1]+ k·downMove
//    • directional idx    : DI+ = up/(up+down) · DI− = down/(up+down)
//    • DX                 : |DI+ − DI−| / (DI+ + DI−)                                   [in 0…1]
//    • volatility index vi: Wilder-smoothed DX → vi = (1−k)·vi[1] + k·DX                 [in 0…1]
//    • adaptive MA        : adxvma = adxvma[1] + (vi^K)·(Close − adxvma[1])              [K≈2 sharpens response]
//    • TREND (trinary, ATR-deadband + hysteresis): let band = ATR(AtrLength)·DeadbandMult;
//        slope = adxvma − adxvma[1].  slope > +band → +1 (rising)  ·  slope < −band → −1 (falling) ·
//        inside the band → HOLD the last non-flat side (hysteresis carries the trend through minor
//        pullbacks) UNLESS the MA has gone genuinely flat over the ATR window → 0 (CHOP).
//        A reversal must therefore cross the OPPOSITE band, never flip +1↔−1 directly.
//    • Bias / Signal      = that trinary trend (−1/0/+1). STATE voter (always a reading; neutral in chop).
//
//  THE SENTINEL PLUMBING (our own code — makes it a suite member):
//    • PUBLISHES SentinelCore.AdxvmaState (Bias / Value = the MA / Signal).
//    • WIRED INTO THE COUNCIL as the ADXVMA voter (a STATE voter on AdxvmaState.Signal).
//    • Hidden ±1 "Signal" PLOT (Values[1], transparent) for the Deck SIGNAL ARM / generic consumers.
//    • Trend-colored adaptive-MA line on the price panel + a SentinelSkin.Painter glass card +
//      label remover + roster heartbeat.
//
//  CHANGELOG
//    v1.0.0 (2026-07-12) — NEW. Clean-room ADXVMA axis (Wilder directional-volatility index → vi^K adaptive
//             MA + ATR-deadband hysteresis trend). AdxvmaState publish, Council ADXVMA voter, hidden Signal
//             plot, trend-colored MA line, glass card, scope key + heartbeat.
// ═════════════════════════════════════════════════════════════════════════════
namespace NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors
{
	public class SentinelADXVMA_v1_0_0 : Indicator
	{
		private SentinelSkin.Painter _sp;
		private bool _hasData;

		// internal recursive buffers (public-formula ADXVMA)
		private Series<double> _up;
		private Series<double> _down;
		private Series<double> _vi;

		// cached state (computed in OnBarUpdate; drawn in OnRender)
		private int    _bias;          // -1/0/+1 trend
		private double _value;         // the adaptive MA
		private double _vidx;          // volatility index vi (0..1) — shown on the card
		private int    _sig;           // = bias
		private int    _lastLoggedSig = -999;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description              = "Clean-room ADX Volatility Moving Average: a Wilder directional-volatility index drives the smoothing constant of an adaptive MA (adxvma = adxvma[1] + vi^K·(Close−adxvma[1])), with an ATR-deadband + hysteresis trinary trend. Publishes SentinelCore.AdxvmaState (bias / MA value / trend Signal) so the Council gains an adaptive-MA voter.";
				Name                     = "Sentinel ADXVMA v1.0.0";
				Calculate                = Calculate.OnBarClose;
				IsOverlay                = true;
				DisplayInDataBox         = true;
				DrawOnPricePanel         = true;
				DrawHorizontalGridLines  = false;
				DrawVerticalGridLines    = false;
				PaintPriceMarkers        = true;
				ScaleJustification       = NinjaTrader.Gui.Chart.ScaleJustification.Right;
				IsSuspendedWhileInactive = true;

				Period       = 14;
				K            = 2.0;
				AtrLength    = 14;
				DeadbandMult = 0.10;

				PublishState       = true;
				LogChanges         = true;
				ShowCard           = true;
				CardCorner         = SentinelCardCorner.TopRight;
				ShowIndicatorLabel = false;

				// visible adaptive-MA line (recolored per bar by trend)
				AddPlot(new Stroke(Brushes.Goldenrod, 2), PlotStyle.Line, "Adxvma");
				// hidden ±1 trend signal (Values[1]) — transparent; readable by the Deck SIGNAL ARM.
				AddPlot(new Stroke(Brushes.Transparent, 1f), PlotStyle.Line, "Signal");
			}
			else if (State == State.Configure)
			{
				_up   = new Series<double>(this);
				_down = new Series<double>(this);
				_vi   = new Series<double>(this);
			}
			else if (State == State.DataLoaded)
			{
				if (!ShowIndicatorLabel) Name = string.Empty;   // Sentinel label remover
			}
			else if (State == State.Terminated)
			{
				if (_sp != null) { try { _sp.Dispose(); } catch { } _sp = null; }
				try { SentinelSkin.CardLayout.Release(this); } catch { }
			}
		}

		// ── scope key ("<masterInstrument>.<barTag>" — ONE CHART's worth of context). Lazily resolved + cached. ──
		private string _scope;
		private string Scope()
		{
			if (_scope == null) { try { _scope = SentinelCore.ScopeOf(Instrument, BarsPeriod); } catch { } }
			return _scope;
		}

		// ── HEARTBEAT — re-stamp the cached seam on incoming quotes so a healthy voter doesn't age out of the
		//    Council roster in a quiet market. No recompute, realtime only, throttled. ──
		private DateTime _lastHeartbeatUtc;
		private const double HeartbeatSec = 5.0;
		protected override void OnMarketData(NinjaTrader.Data.MarketDataEventArgs marketDataUpdate)
		{
			if (!PublishState || State != State.Realtime) return;
			DateTime now = DateTime.UtcNow;
			if ((now - _lastHeartbeatUtc).TotalSeconds < HeartbeatSec) return;
			_lastHeartbeatUtc = now;
			try { SentinelCore.TouchAdxvmaState(Scope()); } catch { }
		}

		private int _trendPrev;   // last emitted trinary trend (for hysteresis)

		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 0) return;

			double k = 1.0 / Math.Max(1, Period);

			// ── warmup / seed ──
			if (CurrentBar < 1)
			{
				_up[0] = 0; _down[0] = 0; _vi[0] = 0;
				Adxvma[0] = Close[0];
				Signal[0] = 0;
				return;
			}

			// ── 1) Wilder-smoothed up/down moves ──
			double cu = Math.Max(Close[0] - Close[1], 0);
			double cd = Math.Max(Close[1] - Close[0], 0);
			_up[0]   = (1 - k) * _up[1]   + k * cu;
			_down[0] = (1 - k) * _down[1] + k * cd;

			// ── 2) directional index → DX (0..1) ──
			double s       = _up[0] + _down[0];
			double diPlus  = s > 1e-12 ? _up[0]   / s : 0;
			double diMinus = s > 1e-12 ? _down[0] / s : 0;
			double dsum    = diPlus + diMinus;
			double dx      = dsum > 1e-12 ? Math.Abs(diPlus - diMinus) / dsum : 0;

			// ── 3) Wilder-smoothed volatility index vi (0..1) ──
			_vi[0] = (1 - k) * _vi[1] + k * dx;
			double vi = _vi[0]; if (vi < 0) vi = 0; else if (vi > 1) vi = 1;

			// ── 4) adaptive MA: smoothing constant = vi^K ──
			double sc = Math.Pow(vi, K);
			Adxvma[0] = Adxvma[1] + sc * (Close[0] - Adxvma[1]);

			// ── 5) trinary trend: ATR deadband + hysteresis hold ──
			double band  = ATR(AtrLength)[0] * DeadbandMult;
			double slope = Adxvma[0] - Adxvma[1];

			int trend = _trendPrev;                 // default: HOLD the last non-flat side (hysteresis)
			if (slope > band)        trend = 1;     // rising past the band
			else if (slope < -band)  trend = -1;    // falling past the band
			else
			{
				// inside the deadband: only call CHOP if the MA has gone genuinely flat over the ATR window;
				// otherwise keep holding the last side through the minor pullback.
				int look = Math.Min(CurrentBar, AtrLength);
				double drift = Adxvma[0] - Adxvma[look];
				if (Math.Abs(drift) <= band) trend = 0;
			}
			_trendPrev = trend;

			// trend-colored MA line
			PlotBrushes[0][0] = trend > 0 ? Brushes.LimeGreen : (trend < 0 ? Brushes.Crimson : Brushes.Goldenrod);

			int sig = trend;
			Signal[0] = sig;

			_bias = trend; _value = Adxvma[0]; _vidx = vi; _sig = sig; _hasData = true;

			if (PublishState && Instrument != null && Instrument.MasterInstrument != null)
			{
				try
				{
					SentinelCore.SetAdxvmaState(new SentinelCore.AdxvmaState
					{
						Scope      = Scope(),
						Bartype    = SentinelCore.BarTag(BarsPeriod),
						Instrument = Instrument.MasterInstrument.Name,
						Bias       = trend,
						Value      = Adxvma[0],
						Signal     = sig,
						Source     = "AVMA"
					});
				}
				catch { }
			}

			if (LogChanges && sig != _lastLoggedSig)
			{
				_lastLoggedSig = sig;
				try
				{
					string inst = Instrument != null && Instrument.MasterInstrument != null ? Instrument.MasterInstrument.Name : "?";
					SentinelCore.Log("AVMA", inst + " " +
						(sig > 0 ? "trend ▲ (rising)" : sig < 0 ? "trend ▼ (falling)" : "chop ~") +
						" ma=" + Adxvma[0].ToString("0.##") + " vi=" + vi.ToString("0.00"));
				}
				catch { }
			}
		}

		// ── glass card (the MA line renders via NT's normal overlay plot) ──
		protected override void OnRender(NinjaTrader.Gui.Chart.ChartControl chartControl, NinjaTrader.Gui.Chart.ChartScale chartScale)
		{
			base.OnRender(chartControl, chartScale);
			if (RenderTarget == null || ChartPanel == null) return;
			if (_sp == null) _sp = new SentinelSkin.Painter();
			_sp.Begin(RenderTarget);
			try { if (ShowCard) RenderCard(); } catch { }
			try { _sp.End(); } catch { }
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
				_sp.Text("ADXVMA", rw.Left + 16f, rw.Top, rw.Width - 20f, 16f, SentinelSkin.CInk, 11f, true);
				_sp.Text("warming up…", rw.Left, rw.Top + 26f, rw.Width, 14f, SentinelSkin.CMute, 10.5f);
				return;
			}

			var trail   = SharpDX.DirectWrite.TextAlignment.Trailing;
			bool live   = _sig != 0;
			var dirCol  = _bias > 0 ? SentinelSkin.CUp : _bias < 0 ? SentinelSkin.CDown : SentinelSkin.CMute;
			var heroCol = live ? dirCol : SentinelSkin.CMute;
			var edge    = live ? SentinelSkin.CAccent : SentinelSkin.CLine;
			var r = _sp.Card(slot.X, slot.Y, cw, ch, edge);

			_sp.Dot(r.Left + 5f, r.Top + 8f, live ? SentinelSkin.CAccent : SentinelSkin.CMute, live);
			_sp.Text("ADXVMA", r.Left + 16f, r.Top, r.Width - 70f, 16f, SentinelSkin.CInk, 11f, true);
			_sp.Pill(_bias > 0 ? "UP" : _bias < 0 ? "DOWN" : "CHOP", r.Right, r.Top - 1f, dirCol);

			_sp.Text("ADAPTIVE TREND", r.Left, r.Top + 24f, 140f, 12f, SentinelSkin.CMute, 8.5f, true);
			_sp.Text(_bias > 0 ? "UP ▲" : _bias < 0 ? "DOWN ▼" : "chop",
				r.Left, r.Top + 34f, r.Width, 24f, heroCol, 17f, false);

			_sp.Divider(r.Left, r.Top + 66f, r.Right);
			_sp.Text("ma " + _value.ToString("0.##"), r.Left, r.Top + 72f, r.Width, 14f, SentinelSkin.CInk2, 10f);
			_sp.Text("vi " + _vidx.ToString("0.00"), r.Left, r.Top + 72f, r.Width, 14f, SentinelSkin.CInk2, 10f, true, trail);
			_sp.Text("volatility index → smoothing", r.Left, r.Top + 90f, r.Width, 14f, SentinelSkin.CMute, 10f);
		}

		#region Properties
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Period", Description="Wilder smoothing period for the directional-volatility index (public default 14).", Order=1, GroupName="Parameters")]
		public int Period { get; set; }

		[NinjaScriptProperty]
		[Range(0.1, double.MaxValue)]
		[Display(Name="K", Description="Response exponent on the volatility index (smoothing constant = vi^K; K≈2 sharpens response).", Order=2, GroupName="Parameters")]
		public double K { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="ATR Length", Description="ATR length for the trend deadband.", Order=3, GroupName="Parameters")]
		public int AtrLength { get; set; }

		[NinjaScriptProperty]
		[Range(0.0, double.MaxValue)]
		[Display(Name="Deadband Mult", Description="Trend deadband = ATR(AtrLength) × this. The MA slope must exceed it to register a direction.", Order=4, GroupName="Parameters")]
		public double DeadbandMult { get; set; }

		// ── Sentinel ──
		[NinjaScriptProperty]
		[Display(Name="Publish ADXVMA to Sentinel", Description="Publish the adaptive-MA read as SentinelCore.AdxvmaState so the Council can vote on it.", Order=20, GroupName="Sentinel")]
		public bool PublishState { get; set; }

		[NinjaScriptProperty]
		[Display(Name="Log Signal Changes", Description="Write trend-signal transitions to sentinel.log.", Order=21, GroupName="Sentinel")]
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
		[Browsable(false)] [XmlIgnore] public Series<double> Adxvma => Values[0];
		[Browsable(false)] [XmlIgnore] public Series<double> Signal => Values[1];   // ±1 trend / 0
		#endregion
	}
}
