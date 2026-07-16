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
using NinjaTrader.NinjaScript.AddOns.Sentinel;   // SentinelSkin (glass card) + SentinelCore (ZScoreState seam) + SentinelCardCorner
using NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors;
#endregion

// ═════════════════════════════════════════════════════════════════════════════
//  Sentinel Z-Score — statistical standard score as a MEAN-REVERSION trigger    |   Version v1.0.0
//  File: SentinelZScore_v1_0_0.cs   |   namespace …Indicators.Sentinel.Sensors   |   display Name "Sentinel Z-Score"
//
//  ⚠ NO ORDERS — read-only advisory indicator. Safe to run anywhere.
//
//  CLEAN-ROOM ORIGINAL. Written from scratch off the PUBLIC statistical z-score (the "standard score") — a
//  textbook, non-copyrightable formula: how many standard deviations a value sits from its rolling mean. It
//  uses NO third-party code; it computes off NinjaTrader's own SMA / StdDev. The installed amaZScore.cs
//  (LizardIndicators, GPL) in the tree was surveyed as a design reference only — none of its code was copied.
//  See the provenance audit + NOTICE.
//
//  WHY IT MATTERS — this is a MEAN-REVERSION voter, orthogonal to the suite's trend/momentum sensors. When
//  price stretches far from its own rolling mean (a large |z|), the statistical expectation is a snap BACK
//  toward the mean, not a continuation. So Z-Score CONTRADICTS the trend axes at extremes — exactly the
//  counter-weight the Council needs to avoid chasing an overextended move.
//
//  THE PUBLIC FORMULA:
//    • z = ( Price − SMA(Period) ) / StdDev(Period),  Price = the selected Input (Close by default).
//    • guard StdDev > 0 (flat series ⇒ z = 0, no signal).
//    • MEAN-REVERSION trigger:  z ≥ +Band  → price stretched HIGH → expect reversion DOWN → Signal = −1.
//                               z ≤ −Band  → price stretched LOW  → expect reversion UP   → Signal = +1.
//                               otherwise (|z| < Band)            →                         Signal =  0.
//    • Extreme = |z| ≥ Band. (Signal is simply the mean-reversion sign while beyond the band, else 0.)
//
//  THE SENTINEL PLUMBING (our own code — makes it a suite member):
//    • PUBLISHES SentinelCore.ZScoreState (Z / Signal / Extreme).
//    • Hidden ±1 "Signal" PLOT (Values[1], transparent) for the Deck SIGNAL ARM / generic consumers.
//    • A SentinelSkin.Painter glass card + label remover + roster heartbeat + scope key.
//
//  CHANGELOG
//    v1.0.0 (2026-07-12) — NEW. Clean-room statistical z-score (standard score) as a mean-reversion TRIGGER
//             voter. ZScoreState publish, visible ZScore line + hidden ±1 Signal plot, ± band + zero
//             reference lines, glass card, scope key + heartbeat, label remover.
// ═════════════════════════════════════════════════════════════════════════════
namespace NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors
{
	public class SentinelZScore_v1_0_0 : Indicator
	{
		private SentinelSkin.Painter _sp;
		private bool _hasData;

		// cached state (computed in OnBarUpdate; drawn in OnRender)
		private double _z;             // the z value this bar
		private int    _signal;        // mean-reversion sign while beyond the band (+1/-1/0)
		private bool   _extreme;       // |z| >= Band
		private int    _lastLoggedSig = -999;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description              = "Clean-room statistical z-score (standard score): how many standard deviations price sits from its rolling mean, z = (Price − SMA) / StdDev. Read as a MEAN-REVERSION trigger — a large |z| flags an overextended move likely to snap back. Publishes SentinelCore.ZScoreState so the Council gains a mean-reversion voter.";
				Name                     = "Sentinel Z-Score v1.0.0";
				Calculate                = Calculate.OnBarClose;
				IsOverlay                = false;   // sub-panel oscillator (zero line + ± bands)
				IsAutoScale              = true;
				DisplayInDataBox         = true;
				DrawOnPricePanel         = false;
				DrawHorizontalGridLines  = false;
				DrawVerticalGridLines    = false;
				PaintPriceMarkers        = true;
				ScaleJustification       = NinjaTrader.Gui.Chart.ScaleJustification.Right;
				IsSuspendedWhileInactive = true;

				Period = 20;
				Band   = 2.0;

				PublishState       = true;
				LogChanges         = true;
				ShowCard           = true;
				CardCorner         = SentinelCardCorner.TopRight;
				ShowIndicatorLabel = false;

				// visible z line + reference lines.
				AddPlot(new Stroke(Brushes.DeepSkyBlue, 2f), PlotStyle.Line, "ZScore");
				// hidden ±1 mean-reversion signal (Values[1]) — transparent; readable by the Deck SIGNAL ARM.
				AddPlot(new Stroke(Brushes.Transparent, 1f), PlotStyle.Line, "Signal");

				AddLine(new Stroke(Brushes.Gray, DashStyleHelper.Dash, 1f),  0.0, "Zero");
				AddLine(new Stroke(Brushes.Crimson,   DashStyleHelper.Dot,  1f),  Band, "UpperBand");
				AddLine(new Stroke(Brushes.LimeGreen, DashStyleHelper.Dot,  1f), -Band, "LowerBand");
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
			try { SentinelCore.TouchZScoreState(Scope()); } catch { }
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < Period) return;

			// 1) the textbook standard score: z = (Price − mean) / stddev, over the rolling Period window.
			double mean = SMA(Input, Period)[0];
			double sd   = StdDev(Input, Period)[0];

			double z = 0.0;
			if (sd > 0.0)
				z = (Input[0] - mean) / sd;

			ZScore[0] = z;   // visible line

			// 2) MEAN-REVERSION trigger — stretched HIGH ⇒ expect DOWN (−1); stretched LOW ⇒ expect UP (+1).
			bool extreme = Math.Abs(z) >= Band;
			int  signal  = 0;
			if (z >=  Band) signal = -1;   // too high → revert down
			else if (z <= -Band) signal =  1;   // too low  → revert up

			Signal[0] = signal;   // hidden plot for the Deck SIGNAL ARM

			_z = z; _signal = signal; _extreme = extreme; _hasData = true;

			// 3) publish the seam.
			if (PublishState && Instrument != null && Instrument.MasterInstrument != null)
			{
				try
				{
					SentinelCore.SetZScoreState(new SentinelCore.ZScoreState
					{
						Scope      = Scope(),
						Bartype    = SentinelCore.BarTag(BarsPeriod),
						Instrument = Instrument.MasterInstrument.Name,
						Z          = z,
						Signal     = signal,
						Extreme    = extreme,
						Source     = "ZSC"
					});
				}
				catch { }
			}

			if (LogChanges && signal != _lastLoggedSig)
			{
				_lastLoggedSig = signal;
				if (signal != 0)
				{
					try
					{
						string inst = Instrument != null && Instrument.MasterInstrument != null ? Instrument.MasterInstrument.Name : "?";
						SentinelCore.Log("ZSC", inst + " " +
							(signal > 0 ? "stretched LOW ▲ (revert up)" : "stretched HIGH ▼ (revert down)") +
							" z=" + z.ToString("0.00"));
					}
					catch { }
				}
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
			const float cw = 228f, ch = 126f;
			var slot = SentinelSkin.CardLayout.Place(this, ChartPanel,
				ChartPanel.X, ChartPanel.Y, ChartPanel.W, ChartPanel.H, CardCorner, cw, ch);

			if (!_hasData)
			{
				var rw = _sp.Card(slot.X, slot.Y, cw, ch, SentinelSkin.CLine);
				_sp.Dot(rw.Left + 5f, rw.Top + 8f, SentinelSkin.CMute, false);
				_sp.Text("Z-SCORE", rw.Left + 16f, rw.Top, rw.Width - 20f, 16f, SentinelSkin.CInk, 11f, true);
				_sp.Text("warming up…", rw.Left, rw.Top + 26f, rw.Width, 14f, SentinelSkin.CMute, 10.5f);
				return;
			}

			var trail   = SharpDX.DirectWrite.TextAlignment.Trailing;
			bool live    = _signal != 0;
			var sigCol  = _signal > 0 ? SentinelSkin.CUp : _signal < 0 ? SentinelSkin.CDown : SentinelSkin.CMute;
			var heroCol = live ? sigCol : SentinelSkin.CMute;
			var edge    = live ? SentinelSkin.CAccent : SentinelSkin.CLine;
			var r = _sp.Card(slot.X, slot.Y, cw, ch, edge);

			_sp.Dot(r.Left + 5f, r.Top + 8f, live ? SentinelSkin.CAccent : SentinelSkin.CMute, live);
			_sp.Text("Z-SCORE", r.Left + 16f, r.Top, r.Width - 70f, 16f, SentinelSkin.CInk, 11f, true);
			_sp.Pill(_extreme ? "EXTREME" : "IN-BAND", r.Right, r.Top - 1f, _extreme ? SentinelSkin.CAccent : SentinelSkin.CMute);

			_sp.Text("MEAN REVERSION", r.Left, r.Top + 24f, 140f, 12f, SentinelSkin.CMute, 8.5f, true);
			_sp.Text(_signal > 0 ? "STRETCHED LOW ▲" : _signal < 0 ? "STRETCHED HIGH ▼" : "neutral",
				r.Left, r.Top + 34f, r.Width, 24f, heroCol, 15f, false);

			_sp.Divider(r.Left, r.Top + 66f, r.Right);
			_sp.Text("z " + _z.ToString("0.00"), r.Left, r.Top + 72f, r.Width, 14f, SentinelSkin.CInk2, 10f);
			_sp.Text("band ±" + Band.ToString("0.0") + "σ", r.Left, r.Top + 72f, r.Width, 14f, SentinelSkin.CMute, 10f, true, trail);
			_sp.Text(_signal > 0 ? "revert up" : _signal < 0 ? "revert down" : "within band",
				r.Left, r.Top + 90f, r.Width, 14f, sigCol, 10f, true);
		}

		#region Properties
		[NinjaScriptProperty]
		[Range(2, int.MaxValue)]
		[Display(Name="Period", Description="Rolling window for the mean (SMA) and standard deviation of the z-score.", Order=1, GroupName="Parameters")]
		public int Period { get; set; }

		[NinjaScriptProperty]
		[Range(0.1, double.MaxValue)]
		[Display(Name="Band", Description="Standard-deviation band. |z| ≥ Band ⇒ stretched (Extreme); a mean-reversion Signal fires against the stretch.", Order=2, GroupName="Parameters")]
		public double Band { get; set; }

		// ── Sentinel ──
		[NinjaScriptProperty]
		[Display(Name="Publish Z-Score to Sentinel", Description="Publish the z value / mean-reversion signal as SentinelCore.ZScoreState so the Council can vote on it.", Order=20, GroupName="Sentinel")]
		public bool PublishState { get; set; }

		[NinjaScriptProperty]
		[Display(Name="Log Signal Changes", Description="Write mean-reversion signal transitions to sentinel.log.", Order=21, GroupName="Sentinel")]
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
		[Browsable(false)] [XmlIgnore] public Series<double> ZScore => Values[0];   // the z value
		[Browsable(false)] [XmlIgnore] public Series<double> Signal => Values[1];   // ±1 mean-reversion / 0
		#endregion
	}
}
