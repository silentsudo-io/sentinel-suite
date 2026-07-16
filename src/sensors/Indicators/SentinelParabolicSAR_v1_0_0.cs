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
using NinjaTrader.NinjaScript.AddOns.Sentinel;   // SentinelSkin (glass card) + SentinelCore (SarState seam) + SentinelCardCorner
using NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors;
#endregion

// ═════════════════════════════════════════════════════════════════════════════
//  Sentinel Parabolic SAR — the STOP-AND-REVERSE trend axis (CLEAN-ROOM)     |   Version v1.0.0
//  File: SentinelParabolicSAR_v1_0_0.cs   |   namespace …Indicators.Sentinel.Sensors (Tier-③ SENSOR)   |   Name "Sentinel Parabolic SAR"
//
//  ⚠ NO ORDERS — read-only advisory indicator. Safe to run anywhere.
//
//  CLEAN-ROOM ORIGINAL. Written from scratch off J. Welles Wilder's PUBLIC Parabolic SAR formula — a
//  classic, non-copyrightable trend-following technique first published in "New Concepts in Technical
//  Trading Systems" (1978) and reproduced in every reference text since. It uses NO third-party code.
//  The installed amaParabolicSAR.cs (LizardIndicators, GPL) was NOT copied — it was surveyed only as a
//  design reference; every line here is written fresh from Wilder's published recurrence. See the
//  provenance audit + NOTICE.
//
//  WHY IT MATTERS — the Council leans on momentum/volatility/structure voters. Parabolic SAR is the
//  purest STOP-AND-REVERSE trend read: a single trailing dot that sits below price in an uptrend and
//  above it in a downtrend, accelerating toward price as the move extends. Its FLIP is a clean,
//  unambiguous regime-change pulse the Council can react to; its side is an always-on trend lean.
//
//  THE PUBLIC FORMULA (Wilder):
//    • State per bar: trend (long/short), SAR value, EP (extreme point of the current run), AF (accel).
//    • Init on the first usable bar: trend from Close vs Open; SAR = the prior bar's extreme;
//      AF = AccelStart; EP = current High (if long) / Low (if short).
//    • Each bar:  SAR = SAR[1] + AF · (EP − SAR[1]).
//         – uptrend  : clamp SAR ≤ min(Low[1], Low[2]).   If Low crosses SAR → FLIP short.
//         – downtrend: clamp SAR ≥ max(High[1], High[2]). If High crosses SAR → FLIP long.
//         – on FLIP  : SAR = EP, AF = AccelStart, EP = the new extreme.
//         – else on a NEW extreme: update EP and AF += AccelStep, capped at AccelMax.
//    • Bias / Signal = +1 uptrend (price above SAR) · −1 downtrend (price below SAR)  [a STATE voter, always ±1].
//    • Flip          = ±1 PULSE on the reversal bar, else 0.
//    • Sar           = the trailing SAR value.
//
//  THE SENTINEL PLUMBING (our own code — makes it a suite member):
//    • PUBLISHES SentinelCore.SarState (Bias / Sar / Flip / Signal).
//    • WIRED INTO THE COUNCIL as a STATE trend voter on SarState.Signal.
//    • Hidden ±1 "Signal" PLOT (Values[1], transparent) for the Deck SIGNAL ARM / generic consumers.
//    • Trend-colored SAR Dot plot (Values[0]) + a SentinelSkin.Painter glass card + label remover +
//      scope key + roster heartbeat.
//
//  CHANGELOG
//    v1.0.0 (2026-07-12) — NEW. Clean-room Parabolic SAR from Wilder's public recurrence (own C#; no
//             third-party code). SarState publish, Council STATE trend voter, hidden Signal plot,
//             trend-colored SAR dots, glass card, scope key + heartbeat.
// ═════════════════════════════════════════════════════════════════════════════
namespace NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors
{
	public class SentinelParabolicSAR_v1_0_0 : Indicator
	{
		private SentinelSkin.Painter _sp;
		private bool   _hasData;

		// ── Wilder SAR running state ──
		private bool   _init;      // seeded on the first usable bar
		private bool   _long;      // current trend (true = uptrend)
		private double _sar;       // current SAR value
		private double _ep;        // extreme point of the current run
		private double _af;        // acceleration factor

		// cached read (computed in OnBarUpdate; drawn in OnRender)
		private int    _bias;      // +1 up / −1 down
		private double _sarVal;    // the SAR value this bar
		private int    _flip;      // +1/−1 flip pulse this bar, else 0
		private int    _sig;       // = bias
		private int    _lastLoggedSig = -999;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description              = "Clean-room Parabolic SAR (Wilder's public stop-and-reverse recurrence): a trailing accelerating dot below price in an uptrend / above in a downtrend. Publishes SentinelCore.SarState (trend bias / SAR value / flip pulse / Signal) so the Council gains a stop-and-reverse trend voter.";
				Name                     = "Sentinel Parabolic SAR v1.0.0";
				Calculate                = Calculate.OnBarClose;
				IsOverlay                = true;
				DisplayInDataBox         = true;
				DrawOnPricePanel         = true;
				DrawHorizontalGridLines  = false;
				DrawVerticalGridLines    = false;
				PaintPriceMarkers        = true;
				ScaleJustification       = NinjaTrader.Gui.Chart.ScaleJustification.Right;
				IsSuspendedWhileInactive = true;

				AccelStart = 0.02;
				AccelStep  = 0.02;
				AccelMax   = 0.2;

				PublishState       = true;
				LogChanges         = true;
				ShowCard           = true;
				CardCorner         = SentinelCardCorner.TopRight;
				ShowIndicatorLabel = false;

				// trailing SAR dots (colored per-bar by trend in OnBarUpdate)
				AddPlot(new Stroke(Brushes.DeepSkyBlue, 2), PlotStyle.Dot, "Sar");
				// hidden ±1 trend signal (Values[1]) — transparent; readable by the Deck SIGNAL ARM.
				AddPlot(new Stroke(Brushes.Transparent, 1f), PlotStyle.Line, "Signal");
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
			try { SentinelCore.TouchSarState(Scope()); } catch { }
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 0) return;
			// need High[1]/High[2] + Low[1]/Low[2] for the clamp.
			if (CurrentBar < 2) return;

			int flip = 0;

			// ── 1) seed the recurrence on the first usable bar ──
			if (!_init)
			{
				_long = Close[0] >= Open[0];
				_af   = AccelStart;
				if (_long) { _ep = High[0]; _sar = Low[1]; }   // SAR starts at the prior extreme
				else       { _ep = Low[0];  _sar = High[1]; }
				_init = true;
			}
			else
			{
				// ── 2) advance the SAR toward the extreme point ──
				double sar = _sar + _af * (_ep - _sar);

				if (_long)
				{
					// SAR may never rise above the prior two lows in an uptrend.
					sar = Math.Min(sar, Math.Min(Low[1], Low[2]));

					if (Low[0] < sar)
					{
						// price crossed below the SAR → FLIP to a downtrend.
						_long = false;
						sar   = _ep;          // reset SAR to the run's extreme
						_af   = AccelStart;
						_ep   = Low[0];       // new extreme for the fresh run
						flip  = -1;
					}
					else if (High[0] > _ep)
					{
						_ep = High[0];
						_af = Math.Min(_af + AccelStep, AccelMax);
					}
				}
				else
				{
					// SAR may never fall below the prior two highs in a downtrend.
					sar = Math.Max(sar, Math.Max(High[1], High[2]));

					if (High[0] > sar)
					{
						// price crossed above the SAR → FLIP to an uptrend.
						_long = true;
						sar   = _ep;
						_af   = AccelStart;
						_ep   = High[0];
						flip  = 1;
					}
					else if (Low[0] < _ep)
					{
						_ep = Low[0];
						_af = Math.Min(_af + AccelStep, AccelMax);
					}
				}

				_sar = sar;
			}

			// ── 3) plot + derive the Sentinel read ──
			int bias = _long ? 1 : -1;
			int sig  = bias;

			Sar[0]    = _sar;
			Signal[0] = bias;
			PlotBrushes[0][0] = _long ? Brushes.MediumSeaGreen : Brushes.IndianRed;

			_bias = bias; _sarVal = _sar; _flip = flip; _sig = sig; _hasData = true;

			if (PublishState && Instrument != null && Instrument.MasterInstrument != null)
			{
				try
				{
					SentinelCore.SetSarState(new SentinelCore.SarState
					{
						Scope      = Scope(),
						Bartype    = SentinelCore.BarTag(BarsPeriod),
						Instrument = Instrument.MasterInstrument.Name,
						Bias       = bias,
						Sar        = _sar,
						Flip       = flip,
						Signal     = sig,
						Source     = "PSAR"
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
					SentinelCore.Log("PSAR", inst + " " +
						(sig > 0 ? "trend ▲ (long)" : "trend ▼ (short)") +
						" sar=" + _sar.ToString("0.##") +
						(flip != 0 ? (flip > 0 ? " +FLIP" : " -FLIP") : ""));
				}
				catch { }
			}
		}

		// ── glass card ──
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
				_sp.Text("PARABOLIC SAR", rw.Left + 16f, rw.Top, rw.Width - 20f, 16f, SentinelSkin.CInk, 11f, true);
				_sp.Text("warming up…", rw.Left, rw.Top + 26f, rw.Width, 14f, SentinelSkin.CMute, 10.5f);
				return;
			}

			var trail   = SharpDX.DirectWrite.TextAlignment.Trailing;
			bool flipped = _flip != 0;
			var dirCol  = _bias > 0 ? SentinelSkin.CUp : SentinelSkin.CDown;
			var edge    = flipped ? SentinelSkin.CAccent : SentinelSkin.CLine;
			var r = _sp.Card(slot.X, slot.Y, cw, ch, edge);

			_sp.Dot(r.Left + 5f, r.Top + 8f, flipped ? SentinelSkin.CAccent : dirCol, flipped);
			_sp.Text("PARABOLIC SAR", r.Left + 16f, r.Top, r.Width - 70f, 16f, SentinelSkin.CInk, 11f, true);
			_sp.Pill(_bias > 0 ? "UP" : "DOWN", r.Right, r.Top - 1f, dirCol);

			_sp.Text("STOP-AND-REVERSE", r.Left, r.Top + 24f, 140f, 12f, SentinelSkin.CMute, 8.5f, true);
			_sp.Text(_bias > 0 ? "UP ▲" : "DOWN ▼",
				r.Left, r.Top + 34f, r.Width, 24f, dirCol, 17f, false);

			_sp.Divider(r.Left, r.Top + 66f, r.Right);
			_sp.Text("SAR " + _sarVal.ToString("0.##"), r.Left, r.Top + 72f, r.Width, 14f, SentinelSkin.CInk2, 10f);
			_sp.Text("AF " + _af.ToString("0.###"), r.Left, r.Top + 72f, r.Width, 14f, SentinelSkin.CInk2, 10f, true, trail);
			if (flipped)
				_sp.Text(_flip > 0 ? "FLIP ▲ long" : "FLIP ▼ short", r.Left, r.Top + 90f, r.Width, 14f,
					SentinelSkin.CAccent, 10f, true);
			else
				_sp.Text("no flip", r.Left, r.Top + 90f, r.Width, 14f, SentinelSkin.CMute, 10f);
		}

		#region Properties
		[NinjaScriptProperty]
		[Range(0.0001, double.MaxValue)]
		[Display(Name="Accel Start", Description="Initial acceleration factor at the start of a run (Wilder default 0.02).", Order=1, GroupName="Parameters")]
		public double AccelStart { get; set; }

		[NinjaScriptProperty]
		[Range(0.0001, double.MaxValue)]
		[Display(Name="Accel Step", Description="Acceleration factor increment added on each new extreme (Wilder default 0.02).", Order=2, GroupName="Parameters")]
		public double AccelStep { get; set; }

		[NinjaScriptProperty]
		[Range(0.0001, double.MaxValue)]
		[Display(Name="Accel Max", Description="Maximum acceleration factor cap (Wilder default 0.2).", Order=3, GroupName="Parameters")]
		public double AccelMax { get; set; }

		// ── Sentinel ──
		[NinjaScriptProperty]
		[Display(Name="Publish SAR to Sentinel", Description="Publish the SAR trend read as SentinelCore.SarState so the Council can vote on it.", Order=20, GroupName="Sentinel")]
		public bool PublishState { get; set; }

		[NinjaScriptProperty]
		[Display(Name="Log Signal Changes", Description="Write SAR trend/flip transitions to sentinel.log.", Order=21, GroupName="Sentinel")]
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
		[Browsable(false)] [XmlIgnore] public Series<double> Sar    => Values[0];
		[Browsable(false)] [XmlIgnore] public Series<double> Signal => Values[1];   // ±1 trend / 0
		#endregion
	}
}
