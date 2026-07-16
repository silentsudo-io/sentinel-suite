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
using NinjaTrader.NinjaScript.AddOns.Sentinel;   // SentinelSkin (glass card) + SentinelCore (ExhaustionState seam) + SentinelCardCorner
using NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors;
#endregion

// ═════════════════════════════════════════════════════════════════════════════
//  Sentinel Exhaustion — Leledc exhaustion-bar reversal detector (CLEAN-ROOM)  |   Version v1.0.0
//  File: SentinelExhaustion_v1_0_0.cs   |   namespace …Indicators.Sentinel.Sensors   |   display Name "Sentinel Exhaustion"
//
//  ⚠ NO ORDERS — read-only advisory indicator. Safe to run anywhere.
//
//  CLEAN-ROOM ORIGINAL. Written from scratch off the PUBLIC "Leledc exhaustion bar" method — a published,
//  non-copyrightable trading formula built from simple consecutive-close counters plus a high/low extreme
//  confirmation. It uses NO third-party code. The installed LeledcExhaustionPro.cs (author "glaz", TradingView)
//  in the tree was surveyed as a design reference only — none of its code was copied. See the provenance
//  audit + NOTICE.
//
//  WHY IT MATTERS — this is a MEAN-REVERSION / EXHAUSTION voter, orthogonal to the suite's trend/momentum
//  sensors: it flags when a run of same-direction closes finally prints a bar that reverses AND pokes a new
//  extreme, i.e. the move likely spent itself. It CONFIRMS or CONTRADICTS the trend axes at turning points.
//
//  THE PUBLIC METHOD:
//    • two counters bindex / sindex — each bar: Close[0] > Close[4] ⇒ bindex++,  Close[0] < Close[4] ⇒ sindex++.
//    • BEARISH (major) exhaustion → DOWN reversal (Signal −1): bindex > MajQual AND a down bar (Close < Open)
//      AND High[0] pokes the highest High of the last MajLen bars → reset bindex, fire −1, major = true.
//    • BULLISH (major) exhaustion → UP reversal (Signal +1): sindex > MajQual AND an up bar (Close > Open)
//      AND Low[0] pokes the lowest Low of the last MajLen bars → reset sindex, fire +1, major = true.
//    • MINOR (secondary) — same shape with the smaller MinQual / MinLen thresholds, considered only when no
//      major fired this bar. Minor fires do not reset the major counters.
//    • Signal = the pulse this bar (+1/−1/0). Dir = last non-zero Signal, HELD HoldBars bars then decays to 0.
//
//  THE SENTINEL PLUMBING (our own code — makes it a suite member):
//    • PUBLISHES SentinelCore.ExhaustionState (Signal / Dir / Major).
//    • Draws a reversal MARKER — up-triangle below the bar on +1, down-triangle above on −1.
//    • Hidden ±1 "Signal" PLOT (Values[0], transparent) for the Deck SIGNAL ARM / generic consumers.
//    • A SentinelSkin.Painter glass card + label remover + roster heartbeat + scope key.
//
//  CHANGELOG
//    v1.0.0 (2026-07-12) — NEW. Clean-room Leledc exhaustion-bar detector (consecutive-close counters + extreme
//             confirm; major + optional minor). ExhaustionState publish, reversal triangles, hidden Signal plot,
//             glass card, scope key + heartbeat, label remover.
// ═════════════════════════════════════════════════════════════════════════════
namespace NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors
{
	public class SentinelExhaustion_v1_0_0 : Indicator
	{
		private SentinelSkin.Painter _sp;
		private bool _hasData;

		// consecutive-close counters (the public Leledc method)
		private int _bindex;
		private int _sindex;

		// cached state (computed in OnBarUpdate; drawn in OnRender)
		private int  _signal;          // pulse this bar (+1/-1/0)
		private int  _dir;             // held reversal direction (decays to 0 after HoldBars)
		private bool _major;           // whether the last fire was major
		private int  _holdLeft;        // bars remaining on the held Dir
		private int  _lastLoggedSig = -999;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description              = "Clean-room Leledc exhaustion-bar reversal detector: consecutive-close counters plus a high/low extreme confirm flag when a directional run spends itself. Publishes SentinelCore.ExhaustionState so the Council gains a mean-reversion / exhaustion voter; draws reversal triangles on the price panel.";
				Name                     = "Sentinel Exhaustion v1.0.0";
				Calculate                = Calculate.OnBarClose;
				IsOverlay                = true;    // draws reversal markers on the price panel
				IsAutoScale              = false;   // the hidden ±1 Signal plot must not distort price autoscale
				DisplayInDataBox         = true;
				DrawOnPricePanel         = true;
				PaintPriceMarkers        = false;
				ScaleJustification       = NinjaTrader.Gui.Chart.ScaleJustification.Right;
				IsSuspendedWhileInactive = true;

				MajQual  = 6;
				MajLen   = 30;
				MinQual  = 5;
				MinLen   = 5;
				HoldBars = 5;

				PublishState       = true;
				LogChanges         = true;
				ShowCard           = true;
				CardCorner         = SentinelCardCorner.TopRight;
				ShowIndicatorLabel = false;

				// hidden ±1 pulse signal (Values[0]) — transparent; readable by the Deck SIGNAL ARM.
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

		// ── HEARTBEAT — re-stamp the cached seam on quotes so a healthy voter doesn't age out of the roster. ──
		private DateTime _lastHeartbeatUtc;
		private const double HeartbeatSec = 5.0;
		protected override void OnMarketData(NinjaTrader.Data.MarketDataEventArgs marketDataUpdate)
		{
			if (!PublishState || State != State.Realtime) return;
			DateTime now = DateTime.UtcNow;
			if ((now - _lastHeartbeatUtc).TotalSeconds < HeartbeatSec) return;
			_lastHeartbeatUtc = now;
			try { SentinelCore.TouchExhaustionState(Scope()); } catch { }
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < 4) return;

			// 1) consecutive-close counters vs the close 4 bars back (the public Leledc rule).
			if (Close[0] > Close[4]) _bindex++;
			if (Close[0] < Close[4]) _sindex++;

			int  signal = 0;
			bool major  = false;

			// 2) MAJOR exhaustion — the primary fire.
			//    Bearish: a run of up-closes, a down bar, poking the MajLen-bar high → expect a DOWN reversal.
			if (_bindex > MajQual && Close[0] < Open[0] && CurrentBar >= MajLen
			    && High[0] >= MAX(High, MajLen)[0])
			{
				_bindex = 0;
				signal  = -1;
				major   = true;
			}
			//    Bullish: a run of down-closes, an up bar, poking the MajLen-bar low → expect an UP reversal.
			else if (_sindex > MajQual && Close[0] > Open[0] && CurrentBar >= MajLen
			         && Low[0] <= MIN(Low, MajLen)[0])
			{
				_sindex = 0;
				signal  = 1;
				major   = true;
			}

			// 3) MINOR exhaustion — clearly secondary; only when no major fired this bar. Does NOT reset the
			//    major counters (it is a weaker, faster confirmation of the same shape).
			if (signal == 0 && CurrentBar >= MinLen)
			{
				if (_bindex > MinQual && Close[0] < Open[0] && High[0] >= MAX(High, MinLen)[0])
					signal = -1;
				else if (_sindex > MinQual && Close[0] > Open[0] && Low[0] <= MIN(Low, MinLen)[0])
					signal = 1;
			}

			// 4) held reversal direction — latch the last non-zero pulse, decay after HoldBars.
			if (signal != 0)
			{
				_dir      = signal;
				_major    = major;
				_holdLeft = Math.Max(1, HoldBars);
			}
			else if (_holdLeft > 0)
			{
				_holdLeft--;
				if (_holdLeft == 0) _dir = 0;
			}

			_signal = signal;
			Signal[0] = signal;   // hidden plot for the Deck SIGNAL ARM
			_hasData  = true;

			// 5) reversal marker on the price panel.
			if (signal > 0)
				Draw.TriangleUp(this, "SentExhU" + CurrentBar, false, 0, Low[0] - TickSize * 2, Brushes.LimeGreen);
			else if (signal < 0)
				Draw.TriangleDown(this, "SentExhD" + CurrentBar, false, 0, High[0] + TickSize * 2, Brushes.Crimson);

			// 6) publish the seam.
			if (PublishState && Instrument != null && Instrument.MasterInstrument != null)
			{
				try
				{
					SentinelCore.SetExhaustionState(new SentinelCore.ExhaustionState
					{
						Scope      = Scope(),
						Bartype    = SentinelCore.BarTag(BarsPeriod),
						Instrument = Instrument.MasterInstrument.Name,
						Signal     = signal,
						Dir        = _dir,
						Major      = _major,
						Source     = "EXH"
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
						SentinelCore.Log("EXH", inst + " " +
							(signal > 0 ? "exhaustion ▲ (up reversal)" : "exhaustion ▼ (down reversal)") +
							(major ? " [major]" : " [minor]"));
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
			const float cw = 228f, ch = 132f;
			var slot = SentinelSkin.CardLayout.Place(this, ChartPanel,
				ChartPanel.X, ChartPanel.Y, ChartPanel.W, ChartPanel.H, CardCorner, cw, ch);

			if (!_hasData)
			{
				var rw = _sp.Card(slot.X, slot.Y, cw, ch, SentinelSkin.CLine);
				_sp.Dot(rw.Left + 5f, rw.Top + 8f, SentinelSkin.CMute, false);
				_sp.Text("EXHAUSTION", rw.Left + 16f, rw.Top, rw.Width - 20f, 16f, SentinelSkin.CInk, 11f, true);
				_sp.Text("warming up…", rw.Left, rw.Top + 26f, rw.Width, 14f, SentinelSkin.CMute, 10.5f);
				return;
			}

			var trail  = SharpDX.DirectWrite.TextAlignment.Trailing;
			bool live   = _signal != 0;
			var sigCol = _signal > 0 ? SentinelSkin.CUp : _signal < 0 ? SentinelSkin.CDown : SentinelSkin.CMute;
			var dirCol = _dir > 0 ? SentinelSkin.CUp : _dir < 0 ? SentinelSkin.CDown : SentinelSkin.CMute;
			var heroCol = live ? sigCol : SentinelSkin.CMute;
			var edge   = live ? SentinelSkin.CAccent : SentinelSkin.CLine;
			var r = _sp.Card(slot.X, slot.Y, cw, ch, edge);

			_sp.Dot(r.Left + 5f, r.Top + 8f, live ? SentinelSkin.CAccent : SentinelSkin.CMute, live);
			_sp.Text("EXHAUSTION", r.Left + 16f, r.Top, r.Width - 70f, 16f, SentinelSkin.CInk, 11f, true);
			_sp.Pill(_major ? "MAJOR" : "MINOR", r.Right, r.Top - 1f, _major ? SentinelSkin.CAccent : SentinelSkin.CMute);

			_sp.Text("REVERSAL", r.Left, r.Top + 24f, 120f, 12f, SentinelSkin.CMute, 8.5f, true);
			_sp.Text(_signal > 0 ? "EXHAUSTION ▲" : _signal < 0 ? "EXHAUSTION ▼" : "quiet",
				r.Left, r.Top + 34f, r.Width, 24f, heroCol, 17f, false);

			_sp.Divider(r.Left, r.Top + 66f, r.Right);
			_sp.Text("hold " + (_dir != 0 ? (_dir > 0 ? "▲" : "▼") + " " + _holdLeft + "b" : "—"),
				r.Left, r.Top + 72f, r.Width, 14f, SentinelSkin.CInk2, 10f);
			_sp.Text(_dir > 0 ? "up bias" : _dir < 0 ? "down bias" : "neutral",
				r.Left, r.Top + 72f, r.Width, 14f, dirCol, 10f, true, trail);
			_sp.Text("b " + _bindex + "  s " + _sindex, r.Left, r.Top + 90f, r.Width, 14f, SentinelSkin.CMute, 10f);
			_sp.Text(_major ? "major fire" : "minor fire", r.Left, r.Top + 90f, r.Width, 14f, SentinelSkin.CMute, 10f, true, trail);
		}

		#region Properties
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Major Qual", Description="Consecutive-close count that must be exceeded before a MAJOR exhaustion can fire.", Order=1, GroupName="Parameters")]
		public int MajQual { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Major Len", Description="Lookback for the high/low extreme confirming a MAJOR exhaustion.", Order=2, GroupName="Parameters")]
		public int MajLen { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Minor Qual", Description="Consecutive-close count for a secondary MINOR exhaustion (fires only when no major does).", Order=3, GroupName="Parameters")]
		public int MinQual { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Minor Len", Description="Lookback for the high/low extreme confirming a MINOR exhaustion.", Order=4, GroupName="Parameters")]
		public int MinLen { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Hold Bars", Description="Bars the held reversal Dir persists after a fire before decaying to 0.", Order=5, GroupName="Parameters")]
		public int HoldBars { get; set; }

		// ── Sentinel ──
		[NinjaScriptProperty]
		[Display(Name="Publish Exhaustion to Sentinel", Description="Publish reversal pulses as SentinelCore.ExhaustionState so the Council can vote on it.", Order=20, GroupName="Sentinel")]
		public bool PublishState { get; set; }

		[NinjaScriptProperty]
		[Display(Name="Log Signal Changes", Description="Write exhaustion fires to sentinel.log.", Order=21, GroupName="Sentinel")]
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
		[Browsable(false)] [XmlIgnore] public Series<double> Signal => Values[0];   // ±1 pulse / 0
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private Sentinel.Sensors.SentinelExhaustion_v1_0_0[] cacheSentinelExhaustion_v1_0_0;
		public Sentinel.Sensors.SentinelExhaustion_v1_0_0 SentinelExhaustion_v1_0_0(int majQual, int majLen, int minQual, int minLen, int holdBars, bool publishState, bool logChanges, bool showCard, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return SentinelExhaustion_v1_0_0(Input, majQual, majLen, minQual, minLen, holdBars, publishState, logChanges, showCard, cardCorner, showIndicatorLabel);
		}

		public Sentinel.Sensors.SentinelExhaustion_v1_0_0 SentinelExhaustion_v1_0_0(ISeries<double> input, int majQual, int majLen, int minQual, int minLen, int holdBars, bool publishState, bool logChanges, bool showCard, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			if (cacheSentinelExhaustion_v1_0_0 != null)
				for (int idx = 0; idx < cacheSentinelExhaustion_v1_0_0.Length; idx++)
					if (cacheSentinelExhaustion_v1_0_0[idx] != null && cacheSentinelExhaustion_v1_0_0[idx].MajQual == majQual && cacheSentinelExhaustion_v1_0_0[idx].MajLen == majLen && cacheSentinelExhaustion_v1_0_0[idx].MinQual == minQual && cacheSentinelExhaustion_v1_0_0[idx].MinLen == minLen && cacheSentinelExhaustion_v1_0_0[idx].HoldBars == holdBars && cacheSentinelExhaustion_v1_0_0[idx].PublishState == publishState && cacheSentinelExhaustion_v1_0_0[idx].LogChanges == logChanges && cacheSentinelExhaustion_v1_0_0[idx].ShowCard == showCard && cacheSentinelExhaustion_v1_0_0[idx].CardCorner == cardCorner && cacheSentinelExhaustion_v1_0_0[idx].ShowIndicatorLabel == showIndicatorLabel && cacheSentinelExhaustion_v1_0_0[idx].EqualsInput(input))
						return cacheSentinelExhaustion_v1_0_0[idx];
			return CacheIndicator<Sentinel.Sensors.SentinelExhaustion_v1_0_0>(new Sentinel.Sensors.SentinelExhaustion_v1_0_0(){ MajQual = majQual, MajLen = majLen, MinQual = minQual, MinLen = minLen, HoldBars = holdBars, PublishState = publishState, LogChanges = logChanges, ShowCard = showCard, CardCorner = cardCorner, ShowIndicatorLabel = showIndicatorLabel }, input, ref cacheSentinelExhaustion_v1_0_0);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.Sentinel.Sensors.SentinelExhaustion_v1_0_0 SentinelExhaustion_v1_0_0(int majQual, int majLen, int minQual, int minLen, int holdBars, bool publishState, bool logChanges, bool showCard, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return indicator.SentinelExhaustion_v1_0_0(Input, majQual, majLen, minQual, minLen, holdBars, publishState, logChanges, showCard, cardCorner, showIndicatorLabel);
		}

		public Indicators.Sentinel.Sensors.SentinelExhaustion_v1_0_0 SentinelExhaustion_v1_0_0(ISeries<double> input , int majQual, int majLen, int minQual, int minLen, int holdBars, bool publishState, bool logChanges, bool showCard, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return indicator.SentinelExhaustion_v1_0_0(input, majQual, majLen, minQual, minLen, holdBars, publishState, logChanges, showCard, cardCorner, showIndicatorLabel);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.Sentinel.Sensors.SentinelExhaustion_v1_0_0 SentinelExhaustion_v1_0_0(int majQual, int majLen, int minQual, int minLen, int holdBars, bool publishState, bool logChanges, bool showCard, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return indicator.SentinelExhaustion_v1_0_0(Input, majQual, majLen, minQual, minLen, holdBars, publishState, logChanges, showCard, cardCorner, showIndicatorLabel);
		}

		public Indicators.Sentinel.Sensors.SentinelExhaustion_v1_0_0 SentinelExhaustion_v1_0_0(ISeries<double> input , int majQual, int majLen, int minQual, int minLen, int holdBars, bool publishState, bool logChanges, bool showCard, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return indicator.SentinelExhaustion_v1_0_0(input, majQual, majLen, minQual, minLen, holdBars, publishState, logChanges, showCard, cardCorner, showIndicatorLabel);
		}
	}
}

#endregion
