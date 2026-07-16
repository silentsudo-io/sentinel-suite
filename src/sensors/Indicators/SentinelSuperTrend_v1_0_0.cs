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
using NinjaTrader.NinjaScript.AddOns.Sentinel;   // SentinelSkin (glass card) + SentinelCore (SuperTrendState seam) + SentinelCardCorner
using NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors;
#endregion

// ═════════════════════════════════════════════════════════════════════════════
//  Sentinel SuperTrend — the ATR-BAND TREND axis (CLEAN-ROOM)                |   Version v1.0.0
//  File: SentinelSuperTrend_v1_0_0.cs   |   namespace …Indicators.Sentinel.Sensors (Tier-③ SENSOR)   |   Name "Sentinel SuperTrend"
//
//  ⚠ NO ORDERS — read-only advisory indicator. Safe to run anywhere.
//
//  CLEAN-ROOM ORIGINAL. Written from scratch off the PUBLIC SuperTrend formula — a published,
//  non-copyrightable trend-following method: ATR bands around the median price with a trailing flip.
//  It uses NO third-party code. The installed AuSuperTrendU11.cs (unlicensed "Au" pack) was surveyed
//  as a DESIGN REFERENCE only — no code was copied. See the provenance audit + NOTICE.
//
//  ⚠ v1.0.0 uses an INLINE ATR baseline (HL2 ± Multiplier×ATR) — it does NOT reproduce Au's full
//    20-moving-average library selector (that is DEFERRED to a later version).
//
//  THE PUBLIC FORMULA:
//    • hl2        = (High + Low) / 2
//    • atr        = ATR(AtrPeriod)
//    • upperBasic = hl2 + Multiplier·atr        ·  lowerBasic = hl2 − Multiplier·atr
//    • trailing bands (standard clamp):
//        finalUpper = (upperBasic < finalUpper[1] || Close[1] > finalUpper[1]) ? upperBasic : finalUpper[1]
//        finalLower = (lowerBasic > finalLower[1] || Close[1] < finalLower[1]) ? lowerBasic : finalLower[1]
//    • direction flip:
//        if prev SuperTrend == finalUpper[1]:  dir = Close > finalUpper ? +1 : −1
//        else                                :  dir = Close < finalLower ? −1 : +1
//      SuperTrend line = dir>0 ? finalLower : finalUpper.
//    • Bias/Signal = dir (±1) — a STATE voter (always ±1).
//    • Flip        = +1/−1 pulse on the bar the direction changes, else 0.
//    • Line        = the SuperTrend trailing-line value.
//
//  THE SENTINEL PLUMBING (our own code — makes it a suite member):
//    • PUBLISHES SentinelCore.SuperTrendState (Bias / Line / Flip / Signal).
//    • WIRED INTO THE COUNCIL as the SUPERTREND voter (a STATE voter on SuperTrendState.Signal).
//    • Hidden ±1 "Signal" PLOT (Values[1], transparent) for the Deck SIGNAL ARM / generic consumers.
//    • Green/red SuperTrend trailing line (Values[0]) + a SentinelSkin.Painter glass card +
//      label remover + roster heartbeat.
//
//  CHANGELOG
//    v1.0.0 (2026-07-12) — NEW. Clean-room ATR-band SuperTrend trend axis (inline HL2 ± Multiplier×ATR
//             baseline; full 20-MA library selector deferred). SuperTrendState publish, Council SUPERTREND
//             voter, hidden Signal plot, green/red trailing line, glass card, scope key + heartbeat.
// ═════════════════════════════════════════════════════════════════════════════
namespace NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors
{
	public class SentinelSuperTrend_v1_0_0 : Indicator
	{
		private SentinelSkin.Painter _sp;
		private bool   _hasData;

		// per-bar working series (trailing bands + direction + line — needed for the [1] look-backs)
		private Series<double> _finalUpper;
		private Series<double> _finalLower;
		private Series<double> _dir;
		private Series<double> _stLine;

		// cached state (computed in OnBarUpdate; drawn in OnRender)
		private int    _bias;        // +1 up / −1 down
		private double _line;        // SuperTrend trailing-line value
		private int    _flip;        // +1/−1 pulse on the bar direction changes, else 0
		private int    _sig;         // = dir
		private int    _lastLoggedSig = -999;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description              = "Clean-room SuperTrend trend axis: ATR bands around the median price (HL2 ± Multiplier×ATR) with a trailing-band flip. Publishes SentinelCore.SuperTrendState (bias / trailing line / flip pulse / confirmed trend Signal) so the Council gains an ATR-band trend voter.";
				Name                     = "Sentinel SuperTrend v1.0.0";
				Calculate                = Calculate.OnBarClose;
				IsOverlay                = true;
				DisplayInDataBox         = true;
				DrawOnPricePanel         = true;
				DrawHorizontalGridLines  = false;
				DrawVerticalGridLines    = false;
				PaintPriceMarkers        = true;
				ScaleJustification       = NinjaTrader.Gui.Chart.ScaleJustification.Right;
				IsSuspendedWhileInactive = true;

				AtrPeriod  = 10;
				Multiplier = 3.0;

				PublishState       = true;
				LogChanges         = true;
				ShowCard           = true;
				CardCorner         = SentinelCardCorner.TopRight;
				ShowIndicatorLabel = false;

				// the SuperTrend trailing line (green when dir>0 / red when dir<0 — colored per bar).
				AddPlot(new Stroke(Brushes.Green, 2), PlotStyle.Line, "SuperTrend");
				// hidden ±1 confirmed-trend signal (Values[1]) — transparent; readable by the Deck SIGNAL ARM.
				AddPlot(new Stroke(Brushes.Transparent, 1f), PlotStyle.Line, "Signal");
			}
			else if (State == State.DataLoaded)
			{
				if (!ShowIndicatorLabel) Name = string.Empty;   // Sentinel label remover
				_finalUpper = new Series<double>(this);
				_finalLower = new Series<double>(this);
				_dir        = new Series<double>(this);
				_stLine     = new Series<double>(this);
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
			try { SentinelCore.TouchSuperTrendState(Scope()); } catch { }
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 0) return;
			if (CurrentBar < AtrPeriod) return;

			// ── 1) ATR bands around the median price ──
			double hl2        = (High[0] + Low[0]) / 2.0;
			double atr        = ATR(AtrPeriod)[0];
			double upperBasic = hl2 + Multiplier * atr;
			double lowerBasic = hl2 - Multiplier * atr;

			// prior bar carry (seed from the basic bands on the very first computed bar)
			bool   havePrev       = CurrentBar > 0 && !double.IsNaN(_finalUpper[1]);
			double prevFinalUpper = havePrev ? _finalUpper[1] : upperBasic;
			double prevFinalLower = havePrev ? _finalLower[1] : lowerBasic;
			double prevClose      = CurrentBar > 0 ? Close[1] : Close[0];
			int    prevDir        = havePrev ? (int)_dir[1] : 1;
			double prevST         = havePrev ? _stLine[1] : prevFinalUpper;

			// ── 2) trailing bands (standard clamp) ──
			double finalUpper = (upperBasic < prevFinalUpper || prevClose > prevFinalUpper) ? upperBasic : prevFinalUpper;
			double finalLower = (lowerBasic > prevFinalLower || prevClose < prevFinalLower) ? lowerBasic : prevFinalLower;
			_finalUpper[0] = finalUpper;
			_finalLower[0] = finalLower;

			// ── 3) direction flip ──
			int dir;
			if (prevST == prevFinalUpper)
				dir = Close[0] > finalUpper ? 1 : -1;
			else
				dir = Close[0] < finalLower ? -1 : 1;

			double line = dir > 0 ? finalLower : finalUpper;
			_dir[0]    = dir;
			_stLine[0] = line;

			int flip = (havePrev && dir != prevDir) ? dir : 0;

			// ── 4) plots: colored trailing line + hidden signal ──
			SuperTrend[0]     = line;
			PlotBrushes[0][0] = dir > 0 ? Brushes.Green : Brushes.Red;
			Signal[0]         = dir;

			_bias = dir; _line = line; _flip = flip; _sig = dir; _hasData = true;

			if (PublishState && Instrument != null && Instrument.MasterInstrument != null)
			{
				try
				{
					SentinelCore.SetSuperTrendState(new SentinelCore.SuperTrendState
					{
						Scope      = Scope(),
						Bartype    = SentinelCore.BarTag(BarsPeriod),
						Instrument = Instrument.MasterInstrument.Name,
						Bias       = dir,
						Line       = line,
						Flip       = flip,
						Signal     = dir,
						Source     = "SPRT"
					});
				}
				catch { }
			}

			if (LogChanges && dir != _lastLoggedSig)
			{
				_lastLoggedSig = dir;
				try
				{
					string inst = Instrument != null && Instrument.MasterInstrument != null ? Instrument.MasterInstrument.Name : "?";
					SentinelCore.Log("SPRT", inst + " " +
						(dir > 0 ? "trend ▲ (long)" : "trend ▼ (short)") +
						" line=" + line.ToString("0.##") +
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
				_sp.Text("SUPERTREND", rw.Left + 16f, rw.Top, rw.Width - 20f, 16f, SentinelSkin.CInk, 11f, true);
				_sp.Text("warming up…", rw.Left, rw.Top + 26f, rw.Width, 14f, SentinelSkin.CMute, 10.5f);
				return;
			}

			var trail   = SharpDX.DirectWrite.TextAlignment.Trailing;
			bool live    = _sig != 0;
			var dirCol  = _bias > 0 ? SentinelSkin.CUp : _bias < 0 ? SentinelSkin.CDown : SentinelSkin.CMute;
			var heroCol = live ? dirCol : SentinelSkin.CMute;
			var edge    = _flip != 0 ? SentinelSkin.CAccent : SentinelSkin.CLine;
			var r = _sp.Card(slot.X, slot.Y, cw, ch, edge);

			_sp.Dot(r.Left + 5f, r.Top + 8f, _flip != 0 ? SentinelSkin.CAccent : SentinelSkin.CMute, _flip != 0);
			_sp.Text("SUPERTREND", r.Left + 16f, r.Top, r.Width - 70f, 16f, SentinelSkin.CInk, 11f, true);
			_sp.Pill(_bias > 0 ? "UP" : _bias < 0 ? "DOWN" : "FLAT", r.Right, r.Top - 1f, dirCol);

			_sp.Text("ATR-BAND TREND", r.Left, r.Top + 24f, 140f, 12f, SentinelSkin.CMute, 8.5f, true);
			_sp.Text(_bias > 0 ? "UP ▲" : _bias < 0 ? "DOWN ▼" : "flat",
				r.Left, r.Top + 34f, r.Width, 24f, heroCol, 17f, false);

			_sp.Divider(r.Left, r.Top + 66f, r.Right);
			_sp.Text("line " + _line.ToString("0.##"), r.Left, r.Top + 72f, r.Width, 14f, SentinelSkin.CInk2, 10f);
			if (_flip != 0)
				_sp.Text(_flip > 0 ? "FLIP ▲" : "FLIP ▼", r.Left, r.Top + 72f, r.Width, 14f,
					_flip > 0 ? SentinelSkin.CUp : SentinelSkin.CDown, 10f, true, trail);
			_sp.Text("atr(" + AtrPeriod + ") × " + Multiplier.ToString("0.#"),
				r.Left, r.Top + 90f, r.Width, 14f, SentinelSkin.CMute, 10f);
		}

		#region Properties
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="ATR Period", Description="ATR length for the band width (public default 10).", Order=1, GroupName="Parameters")]
		public int AtrPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(0.1, double.MaxValue)]
		[Display(Name="Multiplier", Description="Band width = Multiplier × ATR (public default 3.0).", Order=2, GroupName="Parameters")]
		public double Multiplier { get; set; }

		// ── Sentinel ──
		[NinjaScriptProperty]
		[Display(Name="Publish SuperTrend to Sentinel", Description="Publish the trend read as SentinelCore.SuperTrendState so the Council can vote on it.", Order=20, GroupName="Sentinel")]
		public bool PublishState { get; set; }

		[NinjaScriptProperty]
		[Display(Name="Log Signal Changes", Description="Write trend-flip transitions to sentinel.log.", Order=21, GroupName="Sentinel")]
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
		[Browsable(false)] [XmlIgnore] public Series<double> SuperTrend => Values[0];
		[Browsable(false)] [XmlIgnore] public Series<double> Signal     => Values[1];   // ±1 confirmed trend
		#endregion
	}
}
