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
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.AddOns.Sentinel;   // SentinelCore + SentinelSkin
using NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors;
#endregion

// ═════════════════════════════════════════════════════════════════════════════
//  Sentinel God Reversal — the candle-grammar REVERSAL recognizer   [displayed as "Sentinel › God Reversal"]
//  File: SentinelGodReversal_v1_0_0.cs  |  Version: v1.0.0  |  class SentinelGodReversal_v1_0_0  |  ns …Indicators.Sentinel
// ─────────────────────────────────────────────────────────────────────────────
//  WHAT IT IS  (see Docs/SENTINEL_GOD_REVERSAL_DOCTRINE.md)
//    Encodes the reversal grammar from the "god trade masterclass" (Trading for Rent Money) that GodTrades21
//    does NOT: shaved close/open · engulfing-at-level · equal high/low · doji-cluster exhaustion · VI-fill ·
//    attack-angle — gated on a "predictable place" (a Bollinger-band edge, optionally boosted by a Location
//    structural level). Fires on the CLOSE of the reversal candle (non-repaint; entry = next bar).
//    NO ORDERS — a read-only sensor. It:
//      • MARKS each trigger on the chart (triangle + score/setup label + single-candle STOP line + optional VI box)
//      • exposes a hidden ±1 "Signal" plot (Deck SIGNAL ARM / generic consumers)
//      • publishes SentinelCore.GodReversalState (pulse + HELD dir + quality + setup) → the Council's GREV voter
//      • draws a Sentinel glass card (Painter) with the live location read
//
//  DEPS: SentinelCore ≥ v1.14.0 (GodReversalState seam) + SentinelSkin (card). Location consult is a SOFT dep.
//  NO CUSTOM ENUM PARAMS (dodges the bare-enum codegen saga) — every [NinjaScriptProperty] is bool/int/double.
//
//  CHANGELOG
//    v1.0.0 (2026-07-08) — first build. Grammar: shaved/engulf/equal/doji-exhaustion/VI/attack + BB-edge gate +
//             no-trade guards (endless-doji chop, sideways grind). Publishes GodReversalState; Council GREV voter.
//             HONEST CAVEAT: thresholds are first-guess defaults — tune vs the video's examples + let Lens grade.
// ═════════════════════════════════════════════════════════════════════════════

namespace NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors
{
	public class SentinelGodReversal_v1_0_0 : Indicator
	{
		// Sentinel palette — frozen static brushes (safe on the render thread; created once).
		private static readonly Brush SB_Up   = SFreeze(37, 208, 139);   // long / bullish reversal
		private static readonly Brush SB_Down = SFreeze(255, 92, 106);   // short / bearish reversal
		private static readonly Brush SB_Stop = SFreeze(150, 120, 120);  // single-candle stop line
		private static readonly Brush SB_Vi   = SFreeze(63, 209, 224);   // volume-imbalance box (cyan)
		private static Brush SFreeze(byte r, byte g, byte b)
		{ var br = new SolidColorBrush(Color.FromRgb(r, g, b)); br.Freeze(); return br; }

		private Bollinger bb;

		// last-signal state (for the card + the seam)
		private int    lastDir;            // +1/-1 of the last reversal, HELD as the voter for HoldBars
		private int    lastDirBar = -1;
		private double lastQuality;
		private string lastSetup = "—";
		private int    longCount, shortCount;
		private bool   atBandNow;          // current-bar location read (for the card)
		private string locText = "idle";

		private SentinelSkin.Painter _sp;

		#region OnStateChange
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Name                     = "Sentinel God Reversal v1.0.0";
				Description              = "Candle-grammar reversal recognizer (shaved/engulf/equal-high/doji-exhaustion/VI) gated at a Bollinger-band edge. Marks triggers + single-candle stop; publishes GodReversalState for the Council. No orders.";
				Calculate                = Calculate.OnBarClose;   // grammar reads CLOSED candles
				IsOverlay                = true;
				DisplayInDataBox         = true;
				DrawOnPricePanel         = true;
				PaintPriceMarkers        = false;
				IsSuspendedWhileInactive = false;
				IsAutoScale              = false;   // keep the hidden ±1 Signal plot from squashing the price panel

				// ── location gate (the "predictable place") ──
				BbPeriod            = 20;
				BbStdDev            = 2.0;
				BandProximityTicks  = 10;     // "at the band" = within this many ticks of the outer band
				ConsultLocation     = true;   // a SentinelCore LevelState (Location indicator) near price boosts score
				LevelNearTicks      = 12;

				// ── candle grammar thresholds (ticks) ──
				ShaveTicks          = 2;      // wick on the close ≤ this = shaved
				EqualTol            = 2;      // prior close ≈ this bar's open within this = equal high/low
				DojiTicks           = 3;      // body ≤ this = doji
				MinBodyTicks        = 3;      // reversal candle body must be ≥ this (not itself a doji)
				ClutterLookback     = 6;      // bars scanned for exhaustion / endless-doji chop
				AttackLookback      = 6;      // bars for the attack-angle (approach directness) read
				AttackMin           = 0.45;   // net/path ratio ≥ this = clean attack (not a sideways grind)

				// ── firing ──
				MinQuality          = 0.45;   // score threshold to mark/fire
				HoldBars            = 6;       // hold the reversal direction this many bars as the Council vote
				RequireAttackOrHard = true;    // suppress a sideways-grind approach unless a hard signal (engulf/equal)

				// ── output ──
				PublishState        = true;    // default ON — feed the Council's GREV voter out of the box
				ShowMarkers         = true;
				ShowStop            = true;
				ShowViBoxes         = false;   // off by default (can get busy); on to verify what it keyed on
				ShowCard            = true;
				CardCorner          = SentinelCardCorner.TopRight;
				ShowIndicatorLabel  = false;   // Sentinel standard: clean chart (NT name label removed)

				// hidden ±1 breakout-style pulse plot (transparent; IsAutoScale=false above) — Deck SIGNAL ARM reads Values[0]
				AddPlot(new Stroke(Brushes.Transparent, 1f), PlotStyle.Line, "Signal");
			}
			else if (State == State.DataLoaded)
			{
				if (!ShowIndicatorLabel) Name = string.Empty;   // Sentinel label remover (LabelRemover.cs pattern)
				bb = Bollinger(BbStdDev, BbPeriod);
			}
			else if (State == State.Terminated)
			{
				if (_sp != null) { try { _sp.Dispose(); } catch { } _sp = null; }
				try { SentinelSkin.CardLayout.Release(this); } catch { }
			}
		}
		#endregion

		#region OnBarUpdate
		// ── scope (SentinelCore v1.19.0 · execution plan 1.4) ──
		// "<masterInstrument>.<barTag>" — ONE CHART's worth of context. Lazily resolved, then cached.
		// A null scope no-ops the publish: the right fail-silent for an unconfigured indicator.
		private string _scope;
		private string Scope()
		{
		    if (_scope == null) { try { _scope = SentinelCore.ScopeOf(Instrument, BarsPeriod); } catch { } }
		    return _scope;
		}

		// ── HEARTBEAT (SentinelCore v1.19.0) ─────────────────────────────────────────────────
		// An OnBarClose publisher only refreshes its seam when a bar CLOSES. In a quiet market bars close
		// slowly, the seam ages past the Council's StaleSec, and a perfectly healthy voter silently drops
		// out of the roster — seen live as a FULLY LOADED chart reporting "roster 3/10". The Council has
		// heartbeated its own verdict since v1.0.0; its sensors never did. Re-stamp the cached reading on
		// incoming quotes: no recompute, realtime only (a historical re-stamp would fake freshness onto a
		// replayed bar), throttled.
		private DateTime _lastHeartbeatUtc;
		private const double HeartbeatSec = 5.0;
		protected override void OnMarketData(NinjaTrader.Data.MarketDataEventArgs marketDataUpdate)
		{
		    if (!PublishState || State != State.Realtime) return;
		    DateTime now = DateTime.UtcNow;
		    if ((now - _lastHeartbeatUtc).TotalSeconds < HeartbeatSec) return;
		    _lastHeartbeatUtc = now;
		    try { SentinelCore.TouchGodReversalState(Scope()); } catch { }
		}

		protected override void OnBarUpdate()
		{
			Signal[0] = 0;   // default no-signal

			int need = Math.Max(BbPeriod + 2, Math.Max(ClutterLookback, AttackLookback) + 3);
			if (CurrentBar < need) { atBandNow = false; locText = "warming"; return; }

			double tick = TickSize;
			double upper = bb.Upper[0], lower = bb.Lower[0];

			// ── location read (current bar) for the card ──
			double distLowerT = (Close[0] - lower) / tick;
			double distUpperT = (upper - Close[0]) / tick;
			bool nearLower = distLowerT <= BandProximityTicks;
			bool nearUpper = distUpperT <= BandProximityTicks;
			atBandNow = nearLower || nearUpper;
			locText   = nearLower ? "AT LOWER BAND" : (nearUpper ? "AT UPPER BAND" : "mid-band idle");

			// ── evaluate a LONG reversal (bounce off the lower band) then a SHORT (off the upper) ──
			int    firedDir = 0;
			double firedQ   = 0;
			string firedSetup = null;

			if (nearLower && EvaluateReversal(+1, out firedQ, out firedSetup))
				firedDir = +1;
			else if (nearUpper && EvaluateReversal(-1, out firedQ, out firedSetup))
				firedDir = -1;

			if (firedDir != 0)
			{
				Signal[0]   = firedDir;
				lastDir     = firedDir;
				lastDirBar  = CurrentBar;
				lastQuality = firedQ;
				lastSetup   = firedSetup;
				if (firedDir > 0) longCount++; else shortCount++;
				if (ShowMarkers) DrawMarker(firedDir, firedQ, firedSetup);
			}

			// ── publish the seam (held direction is the Council vote) ──
			if (PublishState && Instrument != null && Instrument.MasterInstrument != null)
			{
				int held = (lastDirBar >= 0 && CurrentBar - lastDirBar <= HoldBars) ? lastDir : 0;
				try
				{
					SentinelCore.SetGodReversalState(new SentinelCore.GodReversalState
					{
						// SentinelCore ≥ v1.19.0 — keyed by SCOPE, not instrument.
						Scope      = Scope(),
						Bartype    = SentinelCore.BarTag(BarsPeriod),
						Instrument = Instrument.MasterInstrument.Name,
						Signal     = (int)Signal[0],
						Dir        = held,
						Quality    = (held != 0) ? lastQuality : 0,
						Setup      = (held != 0) ? lastSetup : "—",
						AtBand     = atBandNow,
						Exhausted  = ExhaustionPresent(firedDir != 0 ? firedDir : -Math.Sign(lastDir == 0 ? 1 : lastDir)),
						Source     = "SentinelGodReversal"
					});
				}
				catch { }
			}
		}
		#endregion

		#region Detection
		// dir: +1 looking for a bullish reversal (long), -1 bearish (short).
		private bool EvaluateReversal(int dir, out double quality, out string setup)
		{
			quality = 0; setup = null;

			// no-trade guard: endless-doji chop = a terrible place to trade
			if (EndlessDojiChop()) return false;

			// the reversal candle must be the right color + a real body (not itself a doji)
			double body0 = Math.Abs(Close[0] - Open[0]);
			bool rightColor = dir > 0 ? Close[0] > Open[0] : Close[0] < Open[0];
			if (!rightColor) return false;
			if (body0 < MinBodyTicks * TickSize) return false;

			bool shaved  = IsShavedClose(0, dir);
			bool engulf  = IsEngulfing(dir);
			bool equal   = IsEqualLevel(dir);
			bool exhaust = ExhaustionPresent(dir);
			bool viFill  = ViFilledInto(dir);
			bool attack  = AttackDirectness() >= AttackMin;
			bool hard    = engulf || equal;   // "hard" reversal signals survive a sideways approach

			// core requirement: a reversal candle with at least one candle-grammar confirmation
			if (!(shaved || engulf || equal)) return false;

			// no-trade guard: a sideways grind INTO the level (unless a hard signal is present)
			if (RequireAttackOrHard && !attack && !hard) return false;

			// ── score the confluence (capped at 1.0) ──
			double q = 0.20;                       // base: at band + a qualifying reversal candle
			if (shaved)  q += 0.30;
			if (engulf)  q += 0.30;
			if (equal)   q += 0.25;
			if (exhaust) q += 0.15;
			if (viFill)  q += 0.15;
			if (attack)  q += 0.15;
			if (ConsultLocation && LocationNear(dir)) q += 0.15;
			if (q > 1.0) q = 1.0;

			if (q < MinQuality) return false;

			// label the setup (most-specific first)
			if (viFill && exhaust)      setup = "lateBloomer";
			else if (equal)             setup = "equalLevel";
			else if (exhaust && !attack) setup = "lineBounce";
			else if (engulf)            setup = "engulf";
			else                        setup = "shaved";

			quality = q;
			return true;
		}

		// shaved close: the wick on the CLOSE side is tiny (price closed hard in the reversal direction).
		private bool IsShavedClose(int barsAgo, int dir)
		{
			double hi = High[barsAgo], lo = Low[barsAgo], o = Open[barsAgo], c = Close[barsAgo];
			double upperWick = hi - Math.Max(o, c);
			double lowerWick = Math.Min(o, c) - lo;
			return dir > 0 ? (upperWick <= ShaveTicks * TickSize)   // bullish: closed on the high
						   : (lowerWick <= ShaveTicks * TickSize);   // bearish: closed on the low
		}

		// engulfing: this bar's body is opposite-colored to and larger than the prior body, and covers it.
		private bool IsEngulfing(int dir)
		{
			double o0 = Open[0], c0 = Close[0], o1 = Open[1], c1 = Close[1];
			double body0 = Math.Abs(c0 - o0), body1 = Math.Abs(c1 - o1);
			if (body0 <= body1) return false;
			if (dir > 0) return c1 < o1 && c0 > o1 && o0 < c1;   // prior red, bull engulfs it
			else         return c1 > o1 && c0 < o1 && o0 > c1;   // prior green, bear engulfs it
		}

		// equal high / equal low: the prior candle's close sits (within tol) at this candle's open — the
		// exact hand-off level jammed against the band ("the green closed at the precise level red took over").
		private bool IsEqualLevel(int dir)
		{
			double tol = EqualTol * TickSize;
			if (dir > 0) return Close[1] < Open[1] && Math.Abs(Close[1] - Open[0]) <= tol;   // prior red close == this open (equal low)
			else         return Close[1] > Open[1] && Math.Abs(Close[1] - Open[0]) <= tol;   // prior green close == this open (equal high)
		}

		private bool IsDoji(int barsAgo)
		{
			return Math.Abs(Close[barsAgo] - Open[barsAgo]) <= DojiTicks * TickSize;
		}

		// exhaustion of the COUNTER side before a dir reversal: a doji, or shrinking counter-color bodies
		// ("shitty green / shitty red") in the lookback preceding this bar.
		private bool ExhaustionPresent(int dir)
		{
			if (dir == 0) return false;
			int dojis = 0, weakCounter = 0;
			double avgBody = 0; int n = 0;
			for (int i = 1; i <= ClutterLookback && i <= CurrentBar; i++)
			{
				if (IsDoji(i)) dojis++;
				double body = Math.Abs(Close[i] - Open[i]);
				avgBody += body; n++;
				// counter-color = the side we're reversing AGAINST (dir>0 ⇒ prior down push was red)
				bool counterColor = dir > 0 ? Close[i] < Open[i] : Close[i] > Open[i];
				if (counterColor && body <= (DojiTicks + 2) * TickSize) weakCounter++;
			}
			if (dojis >= 1) return true;
			return n > 0 && weakCounter >= 2;   // a couple of feeble counter-candles = the push is out of gas
		}

		// endless-doji chop: most of the recent window is dojis → a terrible place to trade (suppress).
		private bool EndlessDojiChop()
		{
			int dojis = 0, n = 0;
			for (int i = 0; i < ClutterLookback && i <= CurrentBar; i++)
			{
				if (IsDoji(i)) dojis++;
				n++;
			}
			return n > 0 && dojis >= (int)Math.Ceiling(0.6 * n);
		}

		// a volume imbalance (2-bar gap) that was left behind on the counter side and is being filled INTO the level.
		// bullish (dir>0): a down-gap (High[k] < Low[k+1]) existed recently and price has traded back up through it.
		private bool ViFilledInto(int dir)
		{
			for (int k = 1; k <= ClutterLookback && k + 1 <= CurrentBar; k++)
			{
				if (dir > 0)
				{
					bool downGap = High[k] < Low[k + 1];
					if (downGap && High[0] >= Low[k + 1]) return true;   // filled back up into the gap
				}
				else
				{
					bool upGap = Low[k] > High[k + 1];
					if (upGap && Low[0] <= High[k + 1]) return true;      // filled back down into the gap
				}
			}
			return false;
		}

		// approach directness (attack angle): net move / summed range over the lookback. High = clean plunge
		// into the level ("landing a plane"); low = sideways grind ("hanging out in a seven-point range").
		private double AttackDirectness()
		{
			double path = 0;
			for (int i = 0; i < AttackLookback && i <= CurrentBar; i++)
				path += (High[i] - Low[i]);
			if (path <= 0) return 0;
			double net = Math.Abs(Close[0] - Close[Math.Min(AttackLookback, CurrentBar)]);
			return net / path;
		}

		// optional Location booster: a structural level (VWAP/PDH-PDL/OR/IB/session H-L) within reach.
		private bool LocationNear(int dir)
		{
			try
			{
				var lv = SentinelCore.GetLevelState(Instrument.MasterInstrument.Name, 90.0);
				if (lv == null) return false;
				return Math.Abs(lv.NearestDistTicks) <= LevelNearTicks;
			}
			catch { return false; }
		}
		#endregion

		#region Rendering
		private void DrawMarker(int dir, double q, string setup)
		{
			string tag = "gr" + CurrentBar;
			double off = Math.Max((bb.Upper[0] - bb.Lower[0]) * 0.06, 6 * TickSize);
			Brush col = dir > 0 ? SB_Up : SB_Down;

			if (dir > 0)
				Draw.TriangleUp(this, tag, false, 0, Low[0] - off, col);
			else
				Draw.TriangleDown(this, tag, false, 0, High[0] + off, col);

			string label = (dir > 0 ? "GR▲ " : "GR▼ ") + q.ToString("0.00") + " " + setup;
			double ly = dir > 0 ? Low[0] - off * 2.0 : High[0] + off * 2.0;
			Draw.Text(this, tag + "_t", label, 0, ly, col);

			if (ShowStop)
			{
				// the single-candle stop = the back of the reversal candle
				double stopY = dir > 0 ? Low[0] : High[0];
				Draw.Line(this, tag + "_s", false, 0, stopY, -3, stopY, SB_Stop, DashStyleHelper.Dot, 1);
			}

			if (ShowViBoxes)
				DrawViBox(dir);
		}

		private void DrawViBox(int dir)
		{
			for (int k = 1; k <= ClutterLookback && k + 1 <= CurrentBar; k++)
			{
				if (dir > 0 && High[k] < Low[k + 1])
				{
					Draw.Rectangle(this, "grvi" + CurrentBar + "_" + k, false, k + 1, Low[k + 1], k, High[k], SB_Vi, SB_Vi, 5);
					return;
				}
				if (dir < 0 && Low[k] > High[k + 1])
				{
					Draw.Rectangle(this, "grvi" + CurrentBar + "_" + k, false, k + 1, High[k + 1], k, Low[k], SB_Vi, SB_Vi, 5);
					return;
				}
			}
		}

		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			base.OnRender(chartControl, chartScale);
			if (!ShowCard || RenderTarget == null || ChartPanel == null) return;
			try
			{
				if (_sp == null) _sp = new SentinelSkin.Painter();
				_sp.Begin(RenderTarget);

				const float cw = 250f, ch = 140f;
				var slot = SentinelSkin.CardLayout.Place(this, ChartPanel,
					ChartPanel.X, ChartPanel.Y, ChartPanel.W, ChartPanel.H, CardCorner, cw, ch);

				bool held = lastDirBar >= 0 && CurrentBar - lastDirBar <= HoldBars && lastDir != 0;
				var edge = held ? (lastDir > 0 ? SentinelSkin.CUp : SentinelSkin.CDown)
								: (atBandNow ? SentinelSkin.CAccent : SentinelSkin.CDim);
				var r = _sp.Card(slot.X, slot.Y, cw, ch, edge);

				// header
				_sp.Dot(r.Left + 5f, r.Top + 8f, atBandNow ? SentinelSkin.CAccent : SentinelSkin.CMute, atBandNow);
				_sp.Text("GOD REVERSAL", r.Left + 16f, r.Top, r.Width - 70f, 16f, SentinelSkin.CInk, 11f, true);
				string pill = held ? (lastDir > 0 ? "LONG" : "SHORT") : "WATCH";
				var pillCol = held ? (lastDir > 0 ? SentinelSkin.CUp : SentinelSkin.CDown) : SentinelSkin.CMute;
				_sp.Pill(pill, r.Right, r.Top - 1f, pillCol);

				// quality hero + setup
				_sp.Text("QUALITY", r.Left, r.Top + 26f, 80f, 12f, SentinelSkin.CMute, 9f, true);
				var qCol = lastQuality >= MinQuality ? SentinelSkin.CAccent : SentinelSkin.CInk2;
				_sp.Text(held ? lastQuality.ToString("0.00") : "—", r.Left, r.Top + 35f, 90f, 26f, qCol, 22f);
				_sp.Text(lastSetup, r.Left + 84f, r.Top + 45f, 150f, 14f, SentinelSkin.CMute, 10f);

				// quality track (MinQuality sits at 50%)
				float frac = MinQuality > 0 ? (float)Math.Max(0, Math.Min(1, lastQuality / (2.0 * MinQuality))) : 0f;
				_sp.Track(r.Left, r.Top + 66f, r.Width, held ? frac : 0f, SentinelSkin.CAccent, 5f);

				// location + counts
				var lead = SharpDX.DirectWrite.TextAlignment.Leading;
				var locCol = atBandNow ? SentinelSkin.CAccent : SentinelSkin.CMute;
				_sp.Text(locText, r.Left, r.Top + 78f, r.Width, 14f, locCol, 10.5f, false, lead, true);
				_sp.Text("longs " + longCount + "   shorts " + shortCount, r.Left, r.Top + 94f, r.Width, 14f, SentinelSkin.CInk2, 10f, false, lead, true);
				_sp.Text("hold " + HoldBars + "b   minQ " + MinQuality.ToString("0.00"), r.Left, r.Top + 108f, r.Width, 12f, SentinelSkin.CMute, 9f, false, lead, true);

				_sp.End();
			}
			catch { }
		}
		#endregion

		#region Consumable surface + plot
		[Browsable(false)] [XmlIgnore] public Series<double> Signal => Values[0];   // ±1 on the trigger bar, 0 otherwise
		[Browsable(false)] [XmlIgnore] public int    HeldDirection => (lastDirBar >= 0 && CurrentBar - lastDirBar <= HoldBars) ? lastDir : 0;
		[Browsable(false)] [XmlIgnore] public double LastQuality   => lastQuality;
		[Browsable(false)] [XmlIgnore] public string LastSetup     => lastSetup;
		#endregion

		#region Parameters
		[NinjaScriptProperty] [Range(2, int.MaxValue)]
		[Display(Name = "Bollinger period", Order = 1, GroupName = "01. Location gate")]
		public int BbPeriod { get; set; }

		[NinjaScriptProperty] [Range(0.1, 10.0)]
		[Display(Name = "Bollinger std-dev", Order = 2, GroupName = "01. Location gate")]
		public double BbStdDev { get; set; }

		[NinjaScriptProperty] [Range(0, int.MaxValue)]
		[Display(Name = "Band proximity (ticks)", Description = "Price within this many ticks of the outer band counts as 'at the band'.", Order = 3, GroupName = "01. Location gate")]
		public int BandProximityTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Consult Location", Description = "A SentinelCore LevelState (Location indicator) structural level near price adds to the score. Soft dependency.", Order = 4, GroupName = "01. Location gate")]
		public bool ConsultLocation { get; set; }

		[NinjaScriptProperty] [Range(0, int.MaxValue)]
		[Display(Name = "Level near (ticks)", Order = 5, GroupName = "01. Location gate")]
		public int LevelNearTicks { get; set; }

		[NinjaScriptProperty] [Range(0, int.MaxValue)]
		[Display(Name = "Shaved wick (ticks)", Description = "Wick on the close ≤ this = a shaved close.", Order = 1, GroupName = "02. Candle grammar")]
		public int ShaveTicks { get; set; }

		[NinjaScriptProperty] [Range(0, int.MaxValue)]
		[Display(Name = "Equal-level tol (ticks)", Description = "Prior close ≈ this bar's open within this = equal high/low.", Order = 2, GroupName = "02. Candle grammar")]
		public int EqualTol { get; set; }

		[NinjaScriptProperty] [Range(0, int.MaxValue)]
		[Display(Name = "Doji body (ticks)", Order = 3, GroupName = "02. Candle grammar")]
		public int DojiTicks { get; set; }

		[NinjaScriptProperty] [Range(0, int.MaxValue)]
		[Display(Name = "Min reversal body (ticks)", Order = 4, GroupName = "02. Candle grammar")]
		public int MinBodyTicks { get; set; }

		[NinjaScriptProperty] [Range(1, int.MaxValue)]
		[Display(Name = "Clutter lookback (bars)", Description = "Bars scanned for exhaustion / VI / endless-doji chop.", Order = 5, GroupName = "02. Candle grammar")]
		public int ClutterLookback { get; set; }

		[NinjaScriptProperty] [Range(1, int.MaxValue)]
		[Display(Name = "Attack lookback (bars)", Order = 6, GroupName = "02. Candle grammar")]
		public int AttackLookback { get; set; }

		[NinjaScriptProperty] [Range(0.0, 1.0)]
		[Display(Name = "Attack min (net/path)", Description = "Approach directness ≥ this = a clean attack (not a sideways grind).", Order = 7, GroupName = "02. Candle grammar")]
		public double AttackMin { get; set; }

		[NinjaScriptProperty] [Range(0.0, 1.0)]
		[Display(Name = "Min quality", Description = "Confluence score threshold to mark/fire.", Order = 1, GroupName = "03. Firing")]
		public double MinQuality { get; set; }

		[NinjaScriptProperty] [Range(1, int.MaxValue)]
		[Display(Name = "Hold bars", Description = "Hold the reversal direction this many bars after a fire as the Council's vote.", Order = 2, GroupName = "03. Firing")]
		public int HoldBars { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Require attack or hard signal", Description = "Suppress a sideways-grind approach unless an engulfing/equal-level signal is present.", Order = 3, GroupName = "03. Firing")]
		public bool RequireAttackOrHard { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Publish to Sentinel", Description = "Publish GodReversalState so the Council gains the GREV voter. Needs SentinelCore ≥ v1.14.0.", Order = 1, GroupName = "04. Output")]
		public bool PublishState { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show markers", Order = 2, GroupName = "04. Output")]
		public bool ShowMarkers { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show stop line", Order = 3, GroupName = "04. Output")]
		public bool ShowStop { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show VI boxes", Description = "Draw the volume-imbalance box the signal keyed on (busy — for verification).", Order = 4, GroupName = "04. Output")]
		public bool ShowViBoxes { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show card", Order = 5, GroupName = "04. Output")]
		public bool ShowCard { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Card corner", Order = 6, GroupName = "04. Output")]
		public SentinelCardCorner CardCorner { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show indicator label", Description = "Show NinjaTrader's chart name label. Sentinel default = OFF (clean chart).", Order = 100, GroupName = "04. Output")]
		public bool ShowIndicatorLabel { get; set; }
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private Sentinel.Sensors.SentinelGodReversal_v1_0_0[] cacheSentinelGodReversal_v1_0_0;
		public Sentinel.Sensors.SentinelGodReversal_v1_0_0 SentinelGodReversal_v1_0_0(int bbPeriod, double bbStdDev, int bandProximityTicks, bool consultLocation, int levelNearTicks, int shaveTicks, int equalTol, int dojiTicks, int minBodyTicks, int clutterLookback, int attackLookback, double attackMin, double minQuality, int holdBars, bool requireAttackOrHard, bool publishState, bool showMarkers, bool showStop, bool showViBoxes, bool showCard, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return SentinelGodReversal_v1_0_0(Input, bbPeriod, bbStdDev, bandProximityTicks, consultLocation, levelNearTicks, shaveTicks, equalTol, dojiTicks, minBodyTicks, clutterLookback, attackLookback, attackMin, minQuality, holdBars, requireAttackOrHard, publishState, showMarkers, showStop, showViBoxes, showCard, cardCorner, showIndicatorLabel);
		}

		public Sentinel.Sensors.SentinelGodReversal_v1_0_0 SentinelGodReversal_v1_0_0(ISeries<double> input, int bbPeriod, double bbStdDev, int bandProximityTicks, bool consultLocation, int levelNearTicks, int shaveTicks, int equalTol, int dojiTicks, int minBodyTicks, int clutterLookback, int attackLookback, double attackMin, double minQuality, int holdBars, bool requireAttackOrHard, bool publishState, bool showMarkers, bool showStop, bool showViBoxes, bool showCard, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			if (cacheSentinelGodReversal_v1_0_0 != null)
				for (int idx = 0; idx < cacheSentinelGodReversal_v1_0_0.Length; idx++)
					if (cacheSentinelGodReversal_v1_0_0[idx] != null && cacheSentinelGodReversal_v1_0_0[idx].BbPeriod == bbPeriod && cacheSentinelGodReversal_v1_0_0[idx].BbStdDev == bbStdDev && cacheSentinelGodReversal_v1_0_0[idx].BandProximityTicks == bandProximityTicks && cacheSentinelGodReversal_v1_0_0[idx].ConsultLocation == consultLocation && cacheSentinelGodReversal_v1_0_0[idx].LevelNearTicks == levelNearTicks && cacheSentinelGodReversal_v1_0_0[idx].ShaveTicks == shaveTicks && cacheSentinelGodReversal_v1_0_0[idx].EqualTol == equalTol && cacheSentinelGodReversal_v1_0_0[idx].DojiTicks == dojiTicks && cacheSentinelGodReversal_v1_0_0[idx].MinBodyTicks == minBodyTicks && cacheSentinelGodReversal_v1_0_0[idx].ClutterLookback == clutterLookback && cacheSentinelGodReversal_v1_0_0[idx].AttackLookback == attackLookback && cacheSentinelGodReversal_v1_0_0[idx].AttackMin == attackMin && cacheSentinelGodReversal_v1_0_0[idx].MinQuality == minQuality && cacheSentinelGodReversal_v1_0_0[idx].HoldBars == holdBars && cacheSentinelGodReversal_v1_0_0[idx].RequireAttackOrHard == requireAttackOrHard && cacheSentinelGodReversal_v1_0_0[idx].PublishState == publishState && cacheSentinelGodReversal_v1_0_0[idx].ShowMarkers == showMarkers && cacheSentinelGodReversal_v1_0_0[idx].ShowStop == showStop && cacheSentinelGodReversal_v1_0_0[idx].ShowViBoxes == showViBoxes && cacheSentinelGodReversal_v1_0_0[idx].ShowCard == showCard && cacheSentinelGodReversal_v1_0_0[idx].CardCorner == cardCorner && cacheSentinelGodReversal_v1_0_0[idx].ShowIndicatorLabel == showIndicatorLabel && cacheSentinelGodReversal_v1_0_0[idx].EqualsInput(input))
						return cacheSentinelGodReversal_v1_0_0[idx];
			return CacheIndicator<Sentinel.Sensors.SentinelGodReversal_v1_0_0>(new Sentinel.Sensors.SentinelGodReversal_v1_0_0(){ BbPeriod = bbPeriod, BbStdDev = bbStdDev, BandProximityTicks = bandProximityTicks, ConsultLocation = consultLocation, LevelNearTicks = levelNearTicks, ShaveTicks = shaveTicks, EqualTol = equalTol, DojiTicks = dojiTicks, MinBodyTicks = minBodyTicks, ClutterLookback = clutterLookback, AttackLookback = attackLookback, AttackMin = attackMin, MinQuality = minQuality, HoldBars = holdBars, RequireAttackOrHard = requireAttackOrHard, PublishState = publishState, ShowMarkers = showMarkers, ShowStop = showStop, ShowViBoxes = showViBoxes, ShowCard = showCard, CardCorner = cardCorner, ShowIndicatorLabel = showIndicatorLabel }, input, ref cacheSentinelGodReversal_v1_0_0);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.Sentinel.Sensors.SentinelGodReversal_v1_0_0 SentinelGodReversal_v1_0_0(int bbPeriod, double bbStdDev, int bandProximityTicks, bool consultLocation, int levelNearTicks, int shaveTicks, int equalTol, int dojiTicks, int minBodyTicks, int clutterLookback, int attackLookback, double attackMin, double minQuality, int holdBars, bool requireAttackOrHard, bool publishState, bool showMarkers, bool showStop, bool showViBoxes, bool showCard, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return indicator.SentinelGodReversal_v1_0_0(Input, bbPeriod, bbStdDev, bandProximityTicks, consultLocation, levelNearTicks, shaveTicks, equalTol, dojiTicks, minBodyTicks, clutterLookback, attackLookback, attackMin, minQuality, holdBars, requireAttackOrHard, publishState, showMarkers, showStop, showViBoxes, showCard, cardCorner, showIndicatorLabel);
		}

		public Indicators.Sentinel.Sensors.SentinelGodReversal_v1_0_0 SentinelGodReversal_v1_0_0(ISeries<double> input , int bbPeriod, double bbStdDev, int bandProximityTicks, bool consultLocation, int levelNearTicks, int shaveTicks, int equalTol, int dojiTicks, int minBodyTicks, int clutterLookback, int attackLookback, double attackMin, double minQuality, int holdBars, bool requireAttackOrHard, bool publishState, bool showMarkers, bool showStop, bool showViBoxes, bool showCard, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return indicator.SentinelGodReversal_v1_0_0(input, bbPeriod, bbStdDev, bandProximityTicks, consultLocation, levelNearTicks, shaveTicks, equalTol, dojiTicks, minBodyTicks, clutterLookback, attackLookback, attackMin, minQuality, holdBars, requireAttackOrHard, publishState, showMarkers, showStop, showViBoxes, showCard, cardCorner, showIndicatorLabel);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.Sentinel.Sensors.SentinelGodReversal_v1_0_0 SentinelGodReversal_v1_0_0(int bbPeriod, double bbStdDev, int bandProximityTicks, bool consultLocation, int levelNearTicks, int shaveTicks, int equalTol, int dojiTicks, int minBodyTicks, int clutterLookback, int attackLookback, double attackMin, double minQuality, int holdBars, bool requireAttackOrHard, bool publishState, bool showMarkers, bool showStop, bool showViBoxes, bool showCard, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return indicator.SentinelGodReversal_v1_0_0(Input, bbPeriod, bbStdDev, bandProximityTicks, consultLocation, levelNearTicks, shaveTicks, equalTol, dojiTicks, minBodyTicks, clutterLookback, attackLookback, attackMin, minQuality, holdBars, requireAttackOrHard, publishState, showMarkers, showStop, showViBoxes, showCard, cardCorner, showIndicatorLabel);
		}

		public Indicators.Sentinel.Sensors.SentinelGodReversal_v1_0_0 SentinelGodReversal_v1_0_0(ISeries<double> input , int bbPeriod, double bbStdDev, int bandProximityTicks, bool consultLocation, int levelNearTicks, int shaveTicks, int equalTol, int dojiTicks, int minBodyTicks, int clutterLookback, int attackLookback, double attackMin, double minQuality, int holdBars, bool requireAttackOrHard, bool publishState, bool showMarkers, bool showStop, bool showViBoxes, bool showCard, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return indicator.SentinelGodReversal_v1_0_0(input, bbPeriod, bbStdDev, bandProximityTicks, consultLocation, levelNearTicks, shaveTicks, equalTol, dojiTicks, minBodyTicks, clutterLookback, attackLookback, attackMin, minQuality, holdBars, requireAttackOrHard, publishState, showMarkers, showStop, showViBoxes, showCard, cardCorner, showIndicatorLabel);
		}
	}
}

#endregion
