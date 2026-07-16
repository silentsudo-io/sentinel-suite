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
using NinjaTrader.NinjaScript.AddOns.Sentinel;   // SentinelSkin (glass card) + SentinelCore (VidyaState seam) + SentinelCardCorner
using NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors;
#endregion

// ═════════════════════════════════════════════════════════════════════════════
//  Sentinel VIDYA — the ADAPTIVE-MA TREND axis (CLEAN-ROOM)                  |   Version v1.0.0
//  File: SentinelVIDYA_v1_0_0.cs   |   namespace …Indicators.Sentinel.Sensors (Tier-③ SENSOR)   |   Name "Sentinel VIDYA"
//
//  ⚠ NO ORDERS — read-only advisory indicator. Safe to run anywhere.
//
//  CLEAN-ROOM ORIGINAL. Written from scratch off the PUBLIC VIDYA formula (Variable Index Dynamic
//  Average, Tushar Chande) — a classic, published, non-copyrightable adaptive moving average: a
//  Chande-Momentum-Oscillator-modulated EMA whose smoothing speeds up in a trend and slows in chop.
//  It uses NO third-party code. The installed volumaticVIDYA.cs / BigBeluga VolumaticVIDYA
//  (CC-BY-NC-SA) were surveyed as DESIGN REFERENCES only — no code was copied and the BigBeluga
//  liquidity-zone overlay is NOT reproduced. See the provenance audit + NOTICE.
//
//  THE PUBLIC FORMULA:
//    • Chande Momentum (over CmoPeriod):
//        up  = Σ max(Close − Close[1], 0)      dn = Σ max(Close[1] − Close, 0)
//        cmo = (up + dn) > 0 ? |(up − dn) / (up + dn)| : 0        (0..1)
//    • alpha    = 2 / (Length + 1)
//    • VIDYA[0] = Close[0]·(alpha·cmo) + VIDYA[1]·(1 − alpha·cmo)   (seed VIDYA = Close on bar 0)
//      → the smoothing factor (alpha·cmo) is large when momentum is strong (fast follow) and small
//        when momentum is weak (heavy smoothing), so the line hugs trends and floats through noise.
//    • Bias/Signal = slope direction with a small hysteresis deadband (TickSize × SlopeTicks):
//        +1 when the line rose beyond the band, −1 when it fell beyond it, HOLD the last side inside
//        the band. A STATE voter — always carries a ±1 reading.
//
//  THE SENTINEL PLUMBING (our own code — makes it a suite member):
//    • PUBLISHES SentinelCore.VidyaState (Bias / VIDYA value / Signal).
//    • Intended to be WIRED INTO THE COUNCIL as an adaptive-MA STATE voter on VidyaState.Signal.
//    • Hidden ±1 "Signal" PLOT (Values[1], transparent) for the Deck SIGNAL ARM / generic consumers.
//    • Green/red VIDYA line (Values[0], colored by slope) + a SentinelSkin.Painter glass card +
//      label remover + roster heartbeat.
//
//  CHANGELOG
//    v1.0.0 (2026-07-12) — NEW. Clean-room CMO-modulated EMA (public VIDYA) adaptive-MA trend axis.
//             VidyaState publish, hidden Signal plot, slope-hysteresis bias, green/red overlay line,
//             glass card, scope key + heartbeat.
// ═════════════════════════════════════════════════════════════════════════════
namespace NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors
{
	public class SentinelVIDYA_v1_0_0 : Indicator
	{
		private SentinelSkin.Painter _sp;
		private bool   _hasData;

		// per-bar working series (VIDYA carries its own [1] look-back)
		private Series<double> _vidya;

		// cached state (computed in OnBarUpdate; drawn in OnRender)
		private int    _bias;        // +1 up / −1 down (slope side, hysteresis)
		private double _value;       // the VIDYA line value
		private int    _sig;         // = bias
		private int    _lastLoggedSig = -999;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description              = "Clean-room VIDYA (Variable Index Dynamic Average, Chande): a CMO-modulated EMA whose smoothing accelerates in trends and slows in chop. Publishes SentinelCore.VidyaState (slope bias / VIDYA value / confirmed Signal) so the Council gains an adaptive-MA trend voter.";
				Name                     = "Sentinel VIDYA v1.0.0";
				Calculate                = Calculate.OnBarClose;
				IsOverlay                = true;
				DisplayInDataBox         = true;
				DrawOnPricePanel         = true;
				DrawHorizontalGridLines  = false;
				DrawVerticalGridLines    = false;
				PaintPriceMarkers        = true;
				ScaleJustification       = NinjaTrader.Gui.Chart.ScaleJustification.Right;
				IsSuspendedWhileInactive = true;

				Length     = 14;
				CmoPeriod  = 9;
				SlopeTicks = 1.0;

				PublishState       = true;
				LogChanges         = true;
				ShowCard           = true;
				CardCorner         = SentinelCardCorner.TopRight;
				ShowIndicatorLabel = false;

				// the VIDYA line (green when rising / red when falling — colored per bar).
				AddPlot(new Stroke(Brushes.Green, 2), PlotStyle.Line, "Vidya");
				// hidden ±1 slope signal (Values[1]) — transparent; readable by the Deck SIGNAL ARM.
				AddPlot(new Stroke(Brushes.Transparent, 1f), PlotStyle.Line, "Signal");
			}
			else if (State == State.DataLoaded)
			{
				if (!ShowIndicatorLabel) Name = string.Empty;   // Sentinel label remover
				_vidya = new Series<double>(this);
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
			try { SentinelCore.TouchVidyaState(Scope()); } catch { }
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 0) return;

			// seed on the very first bar
			if (CurrentBar == 0)
			{
				_vidya[0] = Close[0];
				_value = Close[0];
				return;
			}
			if (CurrentBar < CmoPeriod) { _vidya[0] = Close[0]; _value = Close[0]; return; }

			// ── 1) Chande Momentum absolute ratio over CmoPeriod ──
			double up = 0, dn = 0;
			for (int i = 0; i < CmoPeriod; i++)
			{
				double diff = Close[i] - Close[i + 1];
				if (diff > 0) up += diff;
				else          dn += -diff;
			}
			double denom = up + dn;
			double cmo   = denom > 0 ? Math.Abs((up - dn) / denom) : 0.0;

			// ── 2) CMO-modulated EMA (VIDYA) ──
			double alpha = 2.0 / (Length + 1.0);
			double k     = alpha * cmo;                         // effective smoothing factor
			double prev  = !double.IsNaN(_vidya[1]) ? _vidya[1] : Close[1];
			double vidya = Close[0] * k + prev * (1.0 - k);
			_vidya[0] = vidya;

			// ── 3) slope bias with a small hysteresis deadband ──
			double band  = TickSize * SlopeTicks;
			double slope = vidya - prev;
			int    bias;
			if (slope > band)       bias = 1;
			else if (slope < -band) bias = -1;
			else                    bias = _bias != 0 ? _bias : (slope >= 0 ? 1 : -1);   // hold inside the band

			// ── 4) plots: colored VIDYA line + hidden signal ──
			Vidya[0]          = vidya;
			PlotBrushes[0][0] = bias > 0 ? Brushes.Green : bias < 0 ? Brushes.Red : Brushes.Gray;
			Signal[0]         = bias;

			_bias = bias; _value = vidya; _sig = bias; _hasData = true;

			if (PublishState && Instrument != null && Instrument.MasterInstrument != null)
			{
				try
				{
					SentinelCore.SetVidyaState(new SentinelCore.VidyaState
					{
						Scope      = Scope(),
						Bartype    = SentinelCore.BarTag(BarsPeriod),
						Instrument = Instrument.MasterInstrument.Name,
						Bias       = bias,
						Value      = vidya,
						Signal     = bias,
						Source     = "VDYA"
					});
				}
				catch { }
			}

			if (LogChanges && bias != _lastLoggedSig)
			{
				_lastLoggedSig = bias;
				try
				{
					string inst = Instrument != null && Instrument.MasterInstrument != null ? Instrument.MasterInstrument.Name : "?";
					SentinelCore.Log("VDYA", inst + " " +
						(bias > 0 ? "rising ▲ (long)" : "falling ▼ (short)") +
						" vidya=" + vidya.ToString("0.##"));
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
				_sp.Text("VIDYA", rw.Left + 16f, rw.Top, rw.Width - 20f, 16f, SentinelSkin.CInk, 11f, true);
				_sp.Text("warming up…", rw.Left, rw.Top + 26f, rw.Width, 14f, SentinelSkin.CMute, 10.5f);
				return;
			}

			bool live    = _sig != 0;
			var dirCol  = _bias > 0 ? SentinelSkin.CUp : _bias < 0 ? SentinelSkin.CDown : SentinelSkin.CMute;
			var heroCol = live ? dirCol : SentinelSkin.CMute;
			var r = _sp.Card(slot.X, slot.Y, cw, ch, SentinelSkin.CLine);

			_sp.Dot(r.Left + 5f, r.Top + 8f, live ? dirCol : SentinelSkin.CMute, live);
			_sp.Text("VIDYA", r.Left + 16f, r.Top, r.Width - 70f, 16f, SentinelSkin.CInk, 11f, true);
			_sp.Pill(_bias > 0 ? "UP" : _bias < 0 ? "DOWN" : "FLAT", r.Right, r.Top - 1f, dirCol);

			_sp.Text("ADAPTIVE MA", r.Left, r.Top + 24f, 140f, 12f, SentinelSkin.CMute, 8.5f, true);
			_sp.Text(_bias > 0 ? "UP ▲" : _bias < 0 ? "DOWN ▼" : "flat",
				r.Left, r.Top + 34f, r.Width, 24f, heroCol, 17f, false);

			_sp.Divider(r.Left, r.Top + 66f, r.Right);
			_sp.Text("vidya " + _value.ToString("0.##"), r.Left, r.Top + 72f, r.Width, 14f, SentinelSkin.CInk2, 10f);
			_sp.Text("len " + Length + " · cmo(" + CmoPeriod + ")",
				r.Left, r.Top + 90f, r.Width, 14f, SentinelSkin.CMute, 10f);
		}

		#region Properties
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Length", Description="Base EMA length; alpha = 2/(Length+1) is modulated by CMO (public default 14).", Order=1, GroupName="Parameters")]
		public int Length { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="CMO Period", Description="Chande Momentum Oscillator lookback that drives the adaptive smoothing (public default 9).", Order=2, GroupName="Parameters")]
		public int CmoPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(0.0, double.MaxValue)]
		[Display(Name="Slope Ticks", Description="Slope deadband in ticks; the bias holds its last side inside ±(TickSize × this).", Order=3, GroupName="Parameters")]
		public double SlopeTicks { get; set; }

		// ── Sentinel ──
		[NinjaScriptProperty]
		[Display(Name="Publish VIDYA to Sentinel", Description="Publish the slope read as SentinelCore.VidyaState so the Council can vote on it.", Order=20, GroupName="Sentinel")]
		public bool PublishState { get; set; }

		[NinjaScriptProperty]
		[Display(Name="Log Signal Changes", Description="Write slope-flip transitions to sentinel.log.", Order=21, GroupName="Sentinel")]
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
		[Browsable(false)] [XmlIgnore] public Series<double> Vidya  => Values[0];
		[Browsable(false)] [XmlIgnore] public Series<double> Signal => Values[1];   // ±1 slope side
		#endregion
	}
}
