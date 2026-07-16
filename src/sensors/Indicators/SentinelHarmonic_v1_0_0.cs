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
using NinjaTrader.NinjaScript.AddOns.Sentinel;   // SentinelSkin (glass card) + SentinelCore (HarmonicState seam) + SentinelCardCorner
using NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors;
#endregion

// ═════════════════════════════════════════════════════════════════════════════
//  Sentinel Harmonic — XABCD harmonic-pattern reversal detector (CLEAN-ROOM)   |   Version v1.0.0
//  File: SentinelHarmonic_v1_0_0.cs   |   namespace …Indicators.Sentinel.Sensors   |   display Name "Sentinel Harmonic"
//
//  ⚠ NO ORDERS — read-only advisory indicator. Safe to run anywhere.
//
//  CLEAN-ROOM ORIGINAL. Written from scratch off the PUBLIC harmonic-pattern definitions
//  (Gartley / Bat / Butterfly / Crab) — the standard, non-copyrightable Fibonacci XABCD ratio tables that
//  are published in every technical-analysis reference. It uses NO third-party code. The installed
//  VdubusPatternGenV2_NT8.cs (an unlicensed Pine port) in the tree was surveyed as a design reference only —
//  none of its code was copied; the ratio windows below are typed fresh from the public tables. See the
//  provenance audit + NOTICE.
//
//  WHY IT MATTERS — this is a GEOMETRIC MEAN-REVERSION / turning-point voter, orthogonal to the suite's
//  trend/momentum sensors: it flags when five alternating swing pivots trace a completed harmonic figure whose
//  D point is a high-probability reversal zone. It CONFIRMS or CONTRADICTS the trend axes at exhaustion turns.
//
//  THE PUBLIC METHOD:
//    • A self-contained fractal pivot detector confirms a swing HIGH/LOW `Strength` bars back when that bar is
//      the extreme of the symmetric ±Strength window. Pivots are kept ALTERNATING (H,L,H,L,…); the most recent
//      five become X, A, B, C, D.
//    • Leg retracement ratios (absolute price differences): AB/XA, BC/AB, CD/BC, AD/XA — matched against the
//      PUBLIC windows (tolerance `Tol`):
//        Gartley:   AB/XA≈0.618 · BC/AB∈[0.382,0.886] · CD/BC∈[1.13,1.618]  · AD/XA≈0.786
//        Bat:       AB/XA∈[0.382,0.5] · BC/AB∈[0.382,0.886] · CD/BC∈[1.618,2.618] · AD/XA≈0.886
//        Butterfly: AB/XA≈0.786 · BC/AB∈[0.382,0.886] · CD/BC∈[1.618,2.24]  · AD/XA∈[1.27,1.618]
//        Crab:      AB/XA∈[0.382,0.618] · BC/AB∈[0.382,0.886] · CD/BC∈[2.618,3.618] · AD/XA≈1.618
//    • DIRECTION from the D pivot: D is a swing LOW ⇒ BULLISH (Signal +1, expect up); D is a swing HIGH ⇒
//      BEARISH (Signal −1). Only the most recent valid match fires — ONE-SHOT per new D pivot.
//    • Signal = the pulse on the confirmation bar (+1/−1/0). Dir = last non-zero Signal, HELD HoldBars bars
//      then decays to 0.
//
//  THE SENTINEL PLUMBING (our own code — makes it a suite member):
//    • PUBLISHES SentinelCore.HarmonicState (Signal / Dir / Pattern).
//    • Draws the XABCD skeleton (Draw.Line) + a labelled reversal marker (triangle + pattern text) at D.
//    • Hidden ±1 "Signal" PLOT (Values[0], transparent) for the Deck SIGNAL ARM / generic consumers.
//    • A SentinelSkin.Painter glass card + label remover + roster heartbeat + scope key.
//
//  CHANGELOG
//    v1.0.0 (2026-07-12) — NEW. Clean-room XABCD harmonic detector (fractal pivots + public Gartley/Bat/
//             Butterfly/Crab ratio tables; direction from the D pivot). HarmonicState publish, XABCD skeleton
//             + labelled reversal marker, hidden Signal plot, glass card, scope key + heartbeat, label remover.
// ═════════════════════════════════════════════════════════════════════════════
namespace NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors
{
	public class SentinelHarmonic_v1_0_0 : Indicator
	{
		private SentinelSkin.Painter _sp;
		private bool _hasData;

		// ── alternating swing pivots (most recent five = X,A,B,C,D) ──
		private struct Pivot { public bool IsHigh; public double Price; public int Bar; }
		private readonly List<Pivot> _pivots = new List<Pivot>();
		private int _lastFiredDBar = -1;   // one-shot guard per new D pivot

		// cached state (computed in OnBarUpdate; drawn in OnRender)
		private int    _signal;            // pulse this bar (+1/-1/0)
		private int    _dir;               // held reversal direction (decays to 0 after HoldBars)
		private int    _holdLeft;          // bars remaining on the held Dir
		private string _pattern = "";      // matched pattern name for the held Dir ("" when none)
		private string _barPattern = "";   // pattern matched on THIS bar (transient)
		private int    _barSignal;         // signal fired on THIS bar (transient)
		private double _abxa, _bcab, _cdbc, _adxa;   // last computed ratios (for the card)
		private int    _lastLoggedSig = -999;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description              = "Clean-room XABCD harmonic-pattern reversal detector (Gartley / Bat / Butterfly / Crab) built from the public Fibonacci ratio tables and a self-contained fractal pivot detector. Publishes SentinelCore.HarmonicState so the Council gains a geometric turning-point voter; draws the XABCD skeleton + a labelled reversal marker at D.";
				Name                     = "Sentinel Harmonic v1.0.0";
				Calculate                = Calculate.OnBarClose;
				IsOverlay                = true;    // draws the pattern + a reversal marker on the price panel
				IsAutoScale              = false;   // the hidden ±1 Signal plot must not distort price autoscale
				DisplayInDataBox         = true;
				DrawOnPricePanel         = true;
				PaintPriceMarkers        = false;
				ScaleJustification       = NinjaTrader.Gui.Chart.ScaleJustification.Right;
				IsSuspendedWhileInactive = true;

				Strength = 5;
				Tol      = 0.08;
				HoldBars = 8;

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
			try { SentinelCore.TouchHarmonicState(Scope()); } catch { }
		}

		protected override void OnBarUpdate()
		{
			int s = Math.Max(1, Strength);
			if (CurrentBar < 2 * s) return;

			_barSignal  = 0;
			_barPattern = "";

			// 1) fractal pivot confirm at barsAgo == Strength: that bar is the extreme of the ±Strength window.
			DetectPivot(s);

			int signal = _barSignal;

			// 2) held reversal direction — latch the last non-zero pulse, decay after HoldBars.
			if (signal != 0)
			{
				_dir      = signal;
				_pattern  = _barPattern;
				_holdLeft = Math.Max(1, HoldBars);
			}
			else if (_holdLeft > 0)
			{
				_holdLeft--;
				if (_holdLeft == 0) { _dir = 0; _pattern = ""; }
			}

			_signal   = signal;
			Signal[0] = signal;   // hidden plot for the Deck SIGNAL ARM
			_hasData  = true;

			// 3) publish the seam.
			if (PublishState && Instrument != null && Instrument.MasterInstrument != null)
			{
				try
				{
					SentinelCore.SetHarmonicState(new SentinelCore.HarmonicState
					{
						Scope      = Scope(),
						Bartype    = SentinelCore.BarTag(BarsPeriod),
						Instrument = Instrument.MasterInstrument.Name,
						Signal     = signal,
						Dir        = _dir,
						Pattern    = _pattern ?? "",
						Source     = "HARM"
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
						SentinelCore.Log("HARM", inst + " " + _barPattern + " " +
							(signal > 0 ? "▲ (bullish reversal)" : "▼ (bearish reversal)"));
					}
					catch { }
				}
			}
		}

		// ── self-contained fractal pivot detector + alternation bookkeeping ──
		private void DetectPivot(int s)
		{
			double ph = High[s];
			double pl = Low[s];
			bool isHigh = true, isLow = true;
			for (int k = 0; k <= 2 * s; k++)
			{
				if (High[k] > ph) isHigh = false;
				if (Low[k]  < pl) isLow  = false;
			}

			if (isHigh)      AddPivot(true,  ph, CurrentBar - s);
			else if (isLow)  AddPivot(false, pl, CurrentBar - s);
		}

		private void AddPivot(bool isHigh, double price, int bar)
		{
			if (_pivots.Count > 0 && _pivots[_pivots.Count - 1].IsHigh == isHigh)
			{
				// same type as the last pivot — no new alternation; keep the more extreme one, do NOT re-evaluate.
				var last = _pivots[_pivots.Count - 1];
				if ((isHigh && price > last.Price) || (!isHigh && price < last.Price))
					_pivots[_pivots.Count - 1] = new Pivot { IsHigh = isHigh, Price = price, Bar = bar };
				return;
			}

			_pivots.Add(new Pivot { IsHigh = isHigh, Price = price, Bar = bar });
			while (_pivots.Count > 5) _pivots.RemoveAt(0);

			if (_pivots.Count == 5)
				EvaluatePattern();
		}

		private void EvaluatePattern()
		{
			Pivot X = _pivots[0], A = _pivots[1], B = _pivots[2], C = _pivots[3], D = _pivots[4];
			if (D.Bar == _lastFiredDBar) return;   // one-shot per new D pivot

			double xa = Math.Abs(A.Price - X.Price);
			double ab = Math.Abs(B.Price - A.Price);
			double bc = Math.Abs(C.Price - B.Price);
			double cd = Math.Abs(D.Price - C.Price);
			double ad = Math.Abs(D.Price - A.Price);
			if (xa <= 0 || ab <= 0 || bc <= 0) return;   // divide-by-zero guard

			double abxa = ab / xa;
			double bcab = bc / ab;
			double cdbc = cd / bc;
			double adxa = ad / xa;
			_abxa = abxa; _bcab = bcab; _cdbc = cdbc; _adxa = adxa;

			string pattern = Classify(abxa, bcab, cdbc, adxa);
			if (pattern == null) return;

			_lastFiredDBar = D.Bar;
			int signal = D.IsHigh ? -1 : 1;   // D swing HIGH ⇒ bearish; D swing LOW ⇒ bullish
			_barSignal  = signal;
			_barPattern = pattern;

			DrawPattern(X, A, B, C, D, pattern, signal);
		}

		private bool Near(double v, double target) => Math.Abs(v - target) <= Tol;
		private bool In(double v, double lo, double hi) => v >= lo - Tol && v <= hi + Tol;

		private string Classify(double abxa, double bcab, double cdbc, double adxa)
		{
			if (Near(abxa, 0.618) && In(bcab, 0.382, 0.886) && In(cdbc, 1.13, 1.618) && Near(adxa, 0.786))
				return "Gartley";
			if (In(abxa, 0.382, 0.5) && In(bcab, 0.382, 0.886) && In(cdbc, 1.618, 2.618) && Near(adxa, 0.886))
				return "Bat";
			if (Near(abxa, 0.786) && In(bcab, 0.382, 0.886) && In(cdbc, 1.618, 2.24) && In(adxa, 1.27, 1.618))
				return "Butterfly";
			if (In(abxa, 0.382, 0.618) && In(bcab, 0.382, 0.886) && In(cdbc, 2.618, 3.618) && Near(adxa, 1.618))
				return "Crab";
			return null;
		}

		private void DrawPattern(Pivot X, Pivot A, Pivot B, Pivot C, Pivot D, string pattern, int signal)
		{
			string tag = "SentHarm" + D.Bar;   // unique per pattern instance (keyed by the D pivot bar)
			Brush col = signal > 0 ? Brushes.DeepSkyBlue : Brushes.Orange;

			DrawLeg(tag + "XA", X, A, col);
			DrawLeg(tag + "AB", A, B, col);
			DrawLeg(tag + "BC", B, C, col);
			DrawLeg(tag + "CD", C, D, col);

			int dAgo = CurrentBar - D.Bar;
			if (signal > 0)
			{
				Draw.TriangleUp(this, tag + "M", false, dAgo, D.Price - TickSize * 3, Brushes.LimeGreen);
				Draw.Text(this, tag + "T", pattern + " ▲", dAgo, D.Price - TickSize * 8, Brushes.LimeGreen);
			}
			else
			{
				Draw.TriangleDown(this, tag + "M", false, dAgo, D.Price + TickSize * 3, Brushes.Crimson);
				Draw.Text(this, tag + "T", pattern + " ▼", dAgo, D.Price + TickSize * 8, Brushes.Crimson);
			}
		}

		private void DrawLeg(string tag, Pivot p0, Pivot p1, Brush col)
		{
			int a0 = CurrentBar - p0.Bar;
			int a1 = CurrentBar - p1.Bar;
			Draw.Line(this, tag, false, a0, p0.Price, a1, p1.Price, col, DashStyleHelper.Solid, 2);
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
				_sp.Text("HARMONIC", rw.Left + 16f, rw.Top, rw.Width - 20f, 16f, SentinelSkin.CInk, 11f, true);
				_sp.Text("warming up…", rw.Left, rw.Top + 26f, rw.Width, 14f, SentinelSkin.CMute, 10.5f);
				return;
			}

			var trail   = SharpDX.DirectWrite.TextAlignment.Trailing;
			bool live    = _signal != 0;
			int  showDir = live ? _signal : _dir;
			var dirCol  = showDir > 0 ? SentinelSkin.CUp : showDir < 0 ? SentinelSkin.CDown : SentinelSkin.CMute;
			var heroCol = showDir != 0 ? dirCol : SentinelSkin.CMute;
			var edge    = live ? SentinelSkin.CAccent : (showDir != 0 ? SentinelSkin.CLine : SentinelSkin.CLine);
			var r = _sp.Card(slot.X, slot.Y, cw, ch, edge);

			_sp.Dot(r.Left + 5f, r.Top + 8f, live ? SentinelSkin.CAccent : SentinelSkin.CMute, live);
			_sp.Text("HARMONIC", r.Left + 16f, r.Top, r.Width - 70f, 16f, SentinelSkin.CInk, 11f, true);
			_sp.Pill(showDir > 0 ? "BULLISH" : showDir < 0 ? "BEARISH" : "FLAT", r.Right, r.Top - 1f, dirCol);

			_sp.Text("PATTERN", r.Left, r.Top + 24f, 120f, 12f, SentinelSkin.CMute, 8.5f, true);
			string hero = showDir != 0 && !string.IsNullOrEmpty(_pattern)
				? _pattern + (showDir > 0 ? " ▲" : " ▼")
				: "scanning…";
			_sp.Text(hero, r.Left, r.Top + 34f, r.Width, 24f, heroCol, 17f, false);

			_sp.Divider(r.Left, r.Top + 66f, r.Right);
			_sp.Text("hold " + (_dir != 0 ? (_dir > 0 ? "▲" : "▼") + " " + _holdLeft + "b" : "—"),
				r.Left, r.Top + 72f, r.Width, 14f, SentinelSkin.CInk2, 10f);
			_sp.Text("pivots " + _pivots.Count + "/5", r.Left, r.Top + 72f, r.Width, 14f, SentinelSkin.CMute, 10f, true, trail);
			_sp.Text("AB/XA " + _abxa.ToString("0.00") + "  AD/XA " + _adxa.ToString("0.00"),
				r.Left, r.Top + 90f, r.Width, 14f, SentinelSkin.CMute, 10f);
		}

		#region Properties
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Strength", Description="Fractal pivot strength: a swing high/low confirms this many bars back when it is the extreme of the symmetric ±Strength window.", Order=1, GroupName="Parameters")]
		public int Strength { get; set; }

		[NinjaScriptProperty]
		[Range(0.0, 1.0)]
		[Display(Name="Tol", Description="Fibonacci ratio tolerance applied to every window / point target when matching a harmonic pattern.", Order=2, GroupName="Parameters")]
		public double Tol { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Hold Bars", Description="Bars the held reversal Dir persists after a fire before decaying to 0.", Order=3, GroupName="Parameters")]
		public int HoldBars { get; set; }

		// ── Sentinel ──
		[NinjaScriptProperty]
		[Display(Name="Publish Harmonic to Sentinel", Description="Publish harmonic reversal pulses as SentinelCore.HarmonicState so the Council can vote on it.", Order=20, GroupName="Sentinel")]
		public bool PublishState { get; set; }

		[NinjaScriptProperty]
		[Display(Name="Log Signal Changes", Description="Write harmonic pattern fires to sentinel.log.", Order=21, GroupName="Sentinel")]
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
