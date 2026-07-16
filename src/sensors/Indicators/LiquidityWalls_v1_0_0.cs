// ─────────────────────────────────────────────────────────────────────────────
// This Source Code Form is subject to the terms of the Mozilla Public License,
// v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
// one at https://mozilla.org/MPL/2.0/.
//
// Sentinel LiquidityWalls is a NinjaScript port of "Liquidity Walls" (Pine v6),
// © TradingIQ, released under MPL-2.0. This file REMAINS under MPL-2.0. The port
// and Sentinel adaptation are © the Sentinel Suite contributors, also under MPL-2.0.
// ─────────────────────────────────────────────────────────────────────────────
#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors;
using NinjaTrader.NinjaScript.AddOns.Sentinel;
#endregion

// ═════════════════════════════════════════════════════════════════════════════
//  LiquidityWalls — order-flow ABSORPTION detector + liquidity WALL zones   (Sentinel-homed)
//  File: LiquidityWalls_v1_0_0.cs   |   Version: v1.0.0   |   namespace …Indicators.Sentinel
// ─────────────────────────────────────────────────────────────────────────────
//  A faithful NinjaScript port of the TradingIQ "Liquidity Walls" Pine v6 study, rebuilt to the
//  Sentinel design system (SentinelSkin glass card + CardLayout + label remover + SentinelCore seam).
//
//  WHAT IT DOES (the absorption thesis):
//    • Per bar it measures net aggressive order flow (DELTA = Σ signedVolume of the lower-timeframe
//      ticks, tick-rule classified) and how far price actually moved (moveTicks).
//    • A rolling 100-bar OLS regression predicts how far price *should* have moved for that delta
//      (expectedTicks = α + β·delta). The signed shortfall — price failing to follow the flow —
//      is z-scored. A HIGH z = ABSORPTION: aggressive orders hit a passive wall and price barely budged.
//    • On an absorption event (z ≥ threshold) it drops a liquidity WALL one ATR thick: above the high
//      when up-flow was absorbed (RESISTANCE), below the low when down-flow was absorbed (SUPPORT).
//      Walls extend right until price trades clean through their far edge, then fade.
//
//  DELTA SOURCE — a 1-tick added series (AddDataSeries(Tick,1)); ticks are buy/sell classified by the
//    TICK RULE (uptick = buy, downtick = sell, zero-tick carries the last side) — this mirrors the Pine
//    study's non-tick granularity `sign(close-close[1])` and works on historical + live data. Per-bar
//    delta is attributed order-independently via an accumulator + a primary-synced Series write (so it's
//    correct regardless of whether the primary bar-close or the boundary tick fires first).
//    ⚠ Historical delta is only as granular as the provider's historical tick data; live is exact.
//
//  SENTINEL:
//    • namespace Indicators.Sentinel → groups under the "Sentinel" picker folder. Clean class name.
//    • Glass card via SentinelSkin.Painter, docked with CardLayout (never overlaps other Sentinel cards).
//    • Label remover (mandatory) — NT's chart name-label hidden by default.
//    • Hidden "Signal" plot (Values[1]) = absorbSide on an absorption bar (+1 resistance / -1 support / 0)
//      so the Deck SIGNAL ARM / any consumer reads it generically (design-system §6b convention).
//    • Publishes SentinelCore.LiquidityState (SetLiquidityState) — absorption z + nearest wall above/below —
//      so GTrader21/Deck/Eye can veto entries into a wall (SentinelCore v1.4.0 seam).
//
//  Edge lane: NO orders — a detector/observer only.
//
//  CHANGELOG
//    v1.0.0 — First cut. Port of TradingIQ "Liquidity Walls": tick-rule delta, 100-bar OLS regression,
//             failTicks z-score absorption, ATR-thick walls w/ break-through fade, optional inefficiency
//             candle coloring (cyan gradient by z) + optional expected-close phantom dot. Sentinel card +
//             CardLayout + label remover + hidden Signal plot + SentinelCore LiquidityState publish seam.
//             (The Pine study's vestigial IQZZ zigzag — computed but never rendered — is intentionally
//             omitted; and the last-bar box-shrink / commented gradient-line render are dropped.)
// ═════════════════════════════════════════════════════════════════════════════

namespace NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors
{
	public class LiquidityWalls_v1_0_0 : Indicator
	{
		// ── wall brushes (frozen → safe on the render thread; created once on the config thread) ──
		private static readonly Brush WB_Cyan  = SFreeze(63, 209, 224);   // active wall — cyan accent (liquidity / watching)
		private static readonly Brush WB_Faint = SFreeze(63, 209, 224);   // faded wall (drawn at low opacity)
		private static Brush SFreeze(byte r, byte g, byte b)
		{ var br = new SolidColorBrush(Color.FromRgb(r, g, b)); br.Freeze(); return br; }

		// ── inefficiency candle-coloring gradient (gray → cyan by z/2), precomputed + frozen ──
		private const int GradSteps = 12;
		private static readonly Brush[] Grad = BuildGrad();
		private static Brush[] BuildGrad()
		{
			var a = new Brush[GradSteps];
			// mute-gray (108,122,146) → cyan accent (63,209,224)
			for (int i = 0; i < GradSteps; i++)
			{
				double t = i / (double)(GradSteps - 1);
				byte r = (byte)Math.Round(108 + (63 - 108) * t);
				byte g = (byte)Math.Round(122 + (209 - 122) * t);
				byte b = (byte)Math.Round(146 + (224 - 146) * t);
				var br = new SolidColorBrush(Color.FromRgb(r, g, b)); br.Freeze(); a[i] = br;
			}
			return a;
		}

		// ── rolling history for the regression (delta, moveTicks) + the failTicks z-score ──
		private readonly List<double> hDelta = new List<double>();
		private readonly List<double> hMove  = new List<double>();
		private readonly List<double> hFail  = new List<double>();

		// ── per-bar delta accumulation off the 1-tick series ──
		private Series<double> deltaSeries;   // primary-synced: the running delta of the forming primary bar
		private double curBarDelta;
		private int    accBar = -1;
		private double prevTickPx = double.NaN;
		private int    lastTickDir = 1;

		// ── liquidity walls ──
		private sealed class Wall
		{
			public int    Side;          // +1 resistance (above) / -1 support (below)
			public double Top, Bottom;   // price levels, Top > Bottom always
			public double Near;          // the near edge price must clear to break it (Bottom for above, Top for below)
			public int    StartBar;
			public string Tag;
		}
		private readonly List<Wall> walls = new List<Wall>();
		private int wallSeq;

		// ── latest computed values (for the card + publish) ──
		private double lastZ, lastDelta, lastExpTicks, lastMoveTicks;
		private int    lastAbsorbSide;
		private bool   absorbedThisBar;
		private double wallAbove = double.NaN, wallBelow = double.NaN;
		private double distAboveTicks = double.NaN, distBelowTicks = double.NaN;

		// ── Sentinel glass-card readout ──
		private SentinelSkin.Painter _sp;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description                 = "Order-flow absorption detector + liquidity wall zones (TradingIQ port). Edge lane — no orders.";
				Name                        = "Sentinel Liquidity Walls v1.0.0";
				Calculate                   = Calculate.OnBarClose;
				IsOverlay                   = true;
				DisplayInDataBox            = true;
				DrawOnPricePanel            = true;
				PaintPriceMarkers           = false;
				IsSuspendedWhileInactive    = false;
				IsAutoScale                 = false;   // hidden ±1 Signal plot must not squash the price panel

				Lookback                = 100;
				ZThreshold              = 2.0;
				AtrPeriod               = 14;
				AtrMult                 = 1.0;
				MaxWalls                = 40;
				ShowWalls               = true;
				ShowInefficientCandles  = false;
				ShowExpectedClose       = false;
				ShowInfo                = true;
				PublishState            = true;
				CardCorner              = SentinelCardCorner.TopRight;
				ShowIndicatorLabel      = false;   // Sentinel standard: clean chart (NT name label removed)

				// Values[0] — expected-close phantom dot (cyan, faint; NaN unless ShowExpectedClose)
				AddPlot(new Stroke(WB_Cyan, 2f), PlotStyle.Dot, "ExpectedClose");
				// Values[1] — hidden breakout/absorption signal (+1 resistance-above / -1 support-below / 0).
				// Transparent + IsAutoScale=false so the ±1 values never render or scale — read as Values[1] "Signal".
				AddPlot(new Stroke(Brushes.Transparent, 1f), PlotStyle.Line, "Signal");
			}
			else if (State == State.Configure)
			{
				// 1-tick series → per-bar order-flow delta (tick-rule classified). BarsInProgress == 1.
				AddDataSeries(BarsPeriodType.Tick, 1);
			}
			else if (State == State.DataLoaded)
			{
				if (!ShowIndicatorLabel) Name = string.Empty;   // Sentinel label remover (NT draws the chart label from Name)
				deltaSeries = new Series<double>(this);
			}
			else if (State == State.Terminated)
			{
				if (_sp != null) { try { _sp.Dispose(); } catch { } _sp = null; }
				try { SentinelSkin.CardLayout.Release(this); } catch { }
				if (_scope != null) { try { SentinelCore.ClearLiquidityScope(_scope); } catch { } }
			}
		}

		// ── Sentinel scope + heartbeat (v1.20.0 — seam scope migration 1.4) ──
		private string   _scope;
		private DateTime _lastLiqBeatUtc;
		private const double LiqBeatSec = 5.0;
		/// <summary>This chart's SCOPE ("GC.69697v6x24") — instrument × primary bar type. Cached after first resolve.</summary>
		private string Scope()
		{
			if (_scope == null)
			{
				try { if (Instrument != null && BarsPeriod != null) _scope = SentinelCore.ScopeOf(Instrument, BarsPeriod); } catch { }
			}
			return _scope;
		}
		/// <summary>Heartbeat — re-stamp the published wall map on quotes so a quiet market doesn't age it out of the
		/// Council's roster. Realtime + throttled; never recomputes.</summary>
		protected override void OnMarketData(NinjaTrader.Data.MarketDataEventArgs marketDataUpdate)
		{
			if (State != State.Realtime || marketDataUpdate.MarketDataType != NinjaTrader.Data.MarketDataType.Last) return;
			DateTime now = DateTime.UtcNow;
			if ((now - _lastLiqBeatUtc).TotalSeconds < LiqBeatSec) return;
			_lastLiqBeatUtc = now;
			string sc = Scope();
			if (sc != null) { try { SentinelCore.TouchLiquidityState(sc); } catch { } }
		}

		protected override void OnBarUpdate()
		{
			// ── tick series: accumulate order-flow delta into the forming primary bar ──
			if (BarsInProgress == 1)
			{
				int pb = CurrentBars[0];
				if (pb < 0) return;
				if (pb != accBar) { accBar = pb; curBarDelta = 0; }   // primary rolled → fresh accumulator

				double px  = Closes[1][0];
				double vol = Volumes[1][0];
				if (!double.IsNaN(prevTickPx))
				{
					if      (px > prevTickPx) lastTickDir = 1;
					else if (px < prevTickPx) lastTickDir = -1;
					// zero-tick: carry the last direction (Pine's tick path uses bid/ask; we carry)
				}
				prevTickPx = px;
				curBarDelta += vol * lastTickDir;
				if (deltaSeries != null) deltaSeries[0] = curBarDelta;   // [0] = primary bar this tick belongs to
				return;
			}

			// ── primary series (BarsInProgress == 0), bar close ──
			absorbedThisBar = false;
			Signal[0] = 0;
			ExpectedClose[0] = double.NaN;

			double delta = deltaSeries != null ? deltaSeries[0] : 0.0;
			double moveTicks = (Close[0] - Open[0]) / TickSize;

			// OLS regression over the previous `Lookback` closed bars (the Pine [1]-lag → no look-ahead)
			double expTicks = moveTicks;   // degenerate default before warmup
			int c = hDelta.Count;
			if (c >= Lookback)
			{
				double sd = 0, sm = 0;
				for (int i = c - Lookback; i < c; i++) { sd += hDelta[i]; sm += hMove[i]; }
				double md = sd / Lookback, mm = sm / Lookback;
				double vd = 0, cov = 0;
				for (int i = c - Lookback; i < c; i++)
				{
					double dd = hDelta[i] - md, dmv = hMove[i] - mm;
					vd += dd * dd; cov += dd * dmv;
				}
				double beta  = vd > 0 ? cov / vd : 0;   // OLS slope == Pine's corr·(σmove/σdelta)
				double alpha = mm - beta * md;
				expTicks = alpha + beta * delta;
			}

			// signed shortfall (absorption): price failed to follow the flow
			double failTicks = Math.Sign(expTicks) * (expTicks - moveTicks);

			// z-score of failTicks over the last `Lookback` bars (including current, per Pine z())
			hFail.Add(failTicks);
			while (hFail.Count > Lookback) hFail.RemoveAt(0);
			double z = 0;
			if (hFail.Count >= 2)
			{
				double s = 0; for (int i = 0; i < hFail.Count; i++) s += hFail[i];
				double m = s / hFail.Count, v = 0;
				for (int i = 0; i < hFail.Count; i++) { double d = hFail[i] - m; v += d * d; }
				double sdv = Math.Sqrt(v / hFail.Count);
				if (sdv > 0) z = (failTicks - m) / sdv;
			}

			int absorbSide = Math.Sign(expTicks);   // +1 = up-flow absorbed → resistance; -1 = down-flow → support
			double atr = (CurrentBar > AtrPeriod) ? ATR(AtrPeriod)[0] * AtrMult : 0;

			// ── walls: break-through check on existing, then spawn a new one on an absorption event ──
			UpdateWalls();
			if (z >= ZThreshold && absorbSide != 0 && atr > 0)
			{
				SpawnWall(absorbSide, atr);
				absorbedThisBar = true;
			}

			// ── inefficiency candle coloring (cyan gradient by z/2, like the Pine study) ──
			if (ShowInefficientCandles)
			{
				double t = Math.Max(0.0, Math.Min(1.0, z / 2.0));
				var br = Grad[(int)Math.Round(t * (GradSteps - 1))];
				BarBrush = br; CandleOutlineBrush = br;
			}

			// ── expected-close phantom dot ──
			if (ShowExpectedClose)
				ExpectedClose[0] = Instrument.MasterInstrument.RoundToTickSize(Open[0] + expTicks * TickSize);

			// ── publish hidden signal + roll history ──
			if (absorbedThisBar) Signal[0] = absorbSide;
			hDelta.Add(delta); hMove.Add(moveTicks);
			while (hDelta.Count > Lookback) hDelta.RemoveAt(0);
			while (hMove.Count  > Lookback) hMove.RemoveAt(0);

			// ── stash for the card + resolve nearest walls + publish ──
			lastZ = z; lastDelta = delta; lastExpTicks = expTicks; lastMoveTicks = moveTicks;
			lastAbsorbSide = absorbSide;
			ResolveNearestWalls();
			if (PublishState)
			{
				try
				{
					SentinelCore.SetLiquidityState(Scope(), SentinelCore.BarTag(BarsPeriod), InstName(), Close[0], z,
						absorbedThisBar ? absorbSide : 0,
						wallAbove, wallBelow, distAboveTicks, distBelowTicks, walls.Count, Name.Length == 0 ? "LiquidityWalls" : Name);
				}
				catch { }
			}
		}

		// Break-through check + redraw (extend) every active wall. Broken walls fade + drop out of the active set.
		private void UpdateWalls()
		{
			for (int i = walls.Count - 1; i >= 0; i--)
			{
				Wall w = walls[i];
				// above/resistance breaks when price trades ABOVE the far edge (Top); below/support when BELOW far edge (Bottom)
				bool broken = w.Side > 0 ? High[0] > w.Top : Low[0] < w.Bottom;
				if (broken)
				{
					if (ShowWalls) DrawWall(w, true);   // final faded snapshot; stops extending
					walls.RemoveAt(i);
				}
				else if (ShowWalls)
				{
					DrawWall(w, false);   // extend to the current bar
				}
			}
		}

		private void SpawnWall(int side, double atr)
		{
			// cap the active set — retire the oldest if we're at the ceiling
			while (walls.Count >= MaxWalls && walls.Count > 0)
			{
				try { RemoveDrawObject(walls[0].Tag); } catch { }
				walls.RemoveAt(0);
			}

			var w = new Wall { Side = side, StartBar = CurrentBar, Tag = "lwWall" + (wallSeq++) };
			if (side > 0) { w.Bottom = High[0];        w.Top = High[0] + atr; w.Near = w.Bottom; }  // resistance above
			else          { w.Top    = Low[0];         w.Bottom = Low[0] - atr; w.Near = w.Top;    }  // support below
			walls.Add(w);
			if (ShowWalls) DrawWall(w, false);
		}

		private void DrawWall(Wall w, bool faded)
		{
			int startAgo = CurrentBar - w.StartBar; if (startAgo < 0) startAgo = 0;
			var br = faded ? WB_Faint : WB_Cyan;
			var rect = Draw.Rectangle(this, w.Tag, false, startAgo, w.Bottom, 0, w.Top, br, br, faded ? 4 : 12);
			rect.OutlineStroke = new Stroke(br, faded ? DashStyleHelper.Dot : DashStyleHelper.Dash, faded ? 1f : 1.6f);
		}

		// Nearest active wall above/below the last close → distances in ticks (for the card + publish).
		private void ResolveNearestWalls()
		{
			wallAbove = double.NaN; wallBelow = double.NaN;
			distAboveTicks = double.NaN; distBelowTicks = double.NaN;
			double px = Close[0];
			for (int i = 0; i < walls.Count; i++)
			{
				Wall w = walls[i];
				if (w.Side > 0 && w.Near >= px)   // resistance overhead — near edge = Bottom
				{
					if (double.IsNaN(wallAbove) || w.Near < wallAbove) wallAbove = w.Near;
				}
				else if (w.Side < 0 && w.Near <= px)   // support underneath — near edge = Top
				{
					if (double.IsNaN(wallBelow) || w.Near > wallBelow) wallBelow = w.Near;
				}
			}
			if (!double.IsNaN(wallAbove)) distAboveTicks = Math.Max(0, (wallAbove - px) / TickSize);
			if (!double.IsNaN(wallBelow)) distBelowTicks = Math.Max(0, (px - wallBelow) / TickSize);
		}

		private string InstName() { return (Instrument != null && Instrument.MasterInstrument != null) ? Instrument.MasterInstrument.Name : "unknown"; }
		private string BarTag()   { return BarsPeriod.BarsPeriodType + " " + BarsPeriod.Value; }

		// ── the Sentinel "flight-instrument" glass card (docks via CardLayout so it never covers another card) ──
		protected override void OnRender(NinjaTrader.Gui.Chart.ChartControl chartControl, NinjaTrader.Gui.Chart.ChartScale chartScale)
		{
			base.OnRender(chartControl, chartScale);
			if (!ShowInfo || RenderTarget == null || ChartPanel == null) return;
			try
			{
				if (_sp == null) _sp = new SentinelSkin.Painter();
				_sp.Begin(RenderTarget);

				const float cw = 258f, ch = 150f;
				var slot = SentinelSkin.CardLayout.Place(this, ChartPanel,
					ChartPanel.X, ChartPanel.Y, ChartPanel.W, ChartPanel.H, CardCorner, cw, ch);

				bool watching = walls.Count > 0;
				var edge = absorbedThisBar ? SentinelSkin.CAccent : (watching ? SentinelSkin.CLine : SentinelSkin.CDim);
				var r = _sp.Card(slot.X, slot.Y, cw, ch, edge);

				// header: live dot + title + state pill
				bool live = watching || absorbedThisBar;
				_sp.Dot(r.Left + 5f, r.Top + 8f, live ? SentinelSkin.CAccent : SentinelSkin.CMute, live);
				_sp.Text("LIQUIDITY WALLS", r.Left + 16f, r.Top, r.Width - 70f, 16f, SentinelSkin.CInk, 11f, true);
				string st = absorbedThisBar ? "ABSORB" : (watching ? "WATCH" : "IDLE");
				var stCol = absorbedThisBar ? SentinelSkin.CAccent : (watching ? SentinelSkin.CWarn : SentinelSkin.CMute);
				_sp.Pill(st, r.Right, r.Top - 1f, stCol);

				// z-score hero + threshold
				bool hot = lastZ >= ZThreshold;
				var zCol = hot ? SentinelSkin.CAccent : SentinelSkin.CInk2;
				_sp.Text("ABSORPTION Z", r.Left, r.Top + 26f, 120f, 12f, SentinelSkin.CMute, 9f, true);
				_sp.Text(lastZ.ToString("0.00"), r.Left, r.Top + 35f, 110f, 26f, zCol, 22f);
				_sp.Text("/ thr " + ZThreshold.ToString("0.0"), r.Left + 84f, r.Top + 45f, 120f, 14f, SentinelSkin.CMute, 10f);

				// z-vs-threshold track (thr sits at 50%)
				float frac = ZThreshold > 0 ? (float)Math.Max(0, Math.Min(1, lastZ / (2.0 * ZThreshold))) : 0f;
				_sp.Track(r.Left, r.Top + 66f, r.Width, frac, hot ? SentinelSkin.CAccent : SentinelSkin.CFaint, 5f);

				// stat rows (mono)
				var lead = SharpDX.DirectWrite.TextAlignment.Leading;
				string above = double.IsNaN(distAboveTicks) ? "—" : distAboveTicks.ToString("0") + "t";
				string below = double.IsNaN(distBelowTicks) ? "—" : distBelowTicks.ToString("0") + "t";
				_sp.Text("walls " + walls.Count + "   resist +" + above + "   support -" + below,
					r.Left, r.Top + 78f, r.Width, 14f, SentinelSkin.CInk2, 10.5f, false, lead, true);
				_sp.Text("delta " + lastDelta.ToString("+0;-0;0") + "   exp " + lastExpTicks.ToString("0.0") + "t  vs act " + lastMoveTicks.ToString("0.0") + "t",
					r.Left, r.Top + 92f, r.Width, 14f, SentinelSkin.CMute, 10f, false, lead, true);
				_sp.Text(InstName() + "  " + BarTag(), r.Left, r.Top + 106f, r.Width, 12f, SentinelSkin.CMute, 9f, false, lead, true);

				_sp.End();
			}
			catch { }
		}

		#region Consumable "current" surface
		[Browsable(false)] [XmlIgnore] public double Zscore          => lastZ;
		[Browsable(false)] [XmlIgnore] public int    AbsorbSide      => absorbedThisBar ? lastAbsorbSide : 0;
		[Browsable(false)] [XmlIgnore] public int    ActiveWalls     => walls.Count;
		[Browsable(false)] [XmlIgnore] public double NearestWallAbove => wallAbove;
		[Browsable(false)] [XmlIgnore] public double NearestWallBelow => wallBelow;
		#endregion

		#region Plots
		[Browsable(false)] [XmlIgnore] public Series<double> ExpectedClose => Values[0];
		[Browsable(false)] [XmlIgnore] public Series<double> Signal        => Values[1];   // +1 resistance / -1 support / 0 (absorption-event bar)
		#endregion

		#region Parameters
		[NinjaScriptProperty] [Range(20, int.MaxValue)]
		[Display(Name = "Lookback (regression + z)", Order = 1, GroupName = "Detection")]
		public int Lookback { get; set; }

		[NinjaScriptProperty] [Range(0.5, 10.0)]
		[Display(Name = "Absorption Z threshold", Order = 2, GroupName = "Detection")]
		public double ZThreshold { get; set; }

		[NinjaScriptProperty] [Range(2, int.MaxValue)]
		[Display(Name = "ATR period (wall height)", Order = 3, GroupName = "Detection")]
		public int AtrPeriod { get; set; }

		[NinjaScriptProperty] [Range(0.1, 10.0)]
		[Display(Name = "ATR multiplier (wall thickness)", Order = 4, GroupName = "Detection")]
		public double AtrMult { get; set; }

		[NinjaScriptProperty] [Range(1, int.MaxValue)]
		[Display(Name = "Max active walls", Order = 5, GroupName = "Detection")]
		public int MaxWalls { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show walls", Order = 6, GroupName = "Display")]
		public bool ShowWalls { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Color inefficient candles", Order = 7, GroupName = "Display")]
		public bool ShowInefficientCandles { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show expected close", Order = 8, GroupName = "Display")]
		public bool ShowExpectedClose { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show info readout", Order = 9, GroupName = "Display")]
		public bool ShowInfo { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Publish Sentinel state", Order = 10, GroupName = "Sentinel")]
		public bool PublishState { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Card corner", Order = 11, GroupName = "Display")]
		public SentinelCardCorner CardCorner { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show indicator label", Description = "Show NinjaTrader's chart name label. Sentinel default = OFF (clean chart); turn on to restore it.", GroupName = "Sentinel", Order = 100)]
		public bool ShowIndicatorLabel { get; set; }
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private Sentinel.Sensors.LiquidityWalls_v1_0_0[] cacheLiquidityWalls_v1_0_0;
		public Sentinel.Sensors.LiquidityWalls_v1_0_0 LiquidityWalls_v1_0_0(int lookback, double zThreshold, int atrPeriod, double atrMult, int maxWalls, bool showWalls, bool showInefficientCandles, bool showExpectedClose, bool showInfo, bool publishState, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return LiquidityWalls_v1_0_0(Input, lookback, zThreshold, atrPeriod, atrMult, maxWalls, showWalls, showInefficientCandles, showExpectedClose, showInfo, publishState, cardCorner, showIndicatorLabel);
		}

		public Sentinel.Sensors.LiquidityWalls_v1_0_0 LiquidityWalls_v1_0_0(ISeries<double> input, int lookback, double zThreshold, int atrPeriod, double atrMult, int maxWalls, bool showWalls, bool showInefficientCandles, bool showExpectedClose, bool showInfo, bool publishState, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			if (cacheLiquidityWalls_v1_0_0 != null)
				for (int idx = 0; idx < cacheLiquidityWalls_v1_0_0.Length; idx++)
					if (cacheLiquidityWalls_v1_0_0[idx] != null && cacheLiquidityWalls_v1_0_0[idx].Lookback == lookback && cacheLiquidityWalls_v1_0_0[idx].ZThreshold == zThreshold && cacheLiquidityWalls_v1_0_0[idx].AtrPeriod == atrPeriod && cacheLiquidityWalls_v1_0_0[idx].AtrMult == atrMult && cacheLiquidityWalls_v1_0_0[idx].MaxWalls == maxWalls && cacheLiquidityWalls_v1_0_0[idx].ShowWalls == showWalls && cacheLiquidityWalls_v1_0_0[idx].ShowInefficientCandles == showInefficientCandles && cacheLiquidityWalls_v1_0_0[idx].ShowExpectedClose == showExpectedClose && cacheLiquidityWalls_v1_0_0[idx].ShowInfo == showInfo && cacheLiquidityWalls_v1_0_0[idx].PublishState == publishState && cacheLiquidityWalls_v1_0_0[idx].CardCorner == cardCorner && cacheLiquidityWalls_v1_0_0[idx].ShowIndicatorLabel == showIndicatorLabel && cacheLiquidityWalls_v1_0_0[idx].EqualsInput(input))
						return cacheLiquidityWalls_v1_0_0[idx];
			return CacheIndicator<Sentinel.Sensors.LiquidityWalls_v1_0_0>(new Sentinel.Sensors.LiquidityWalls_v1_0_0(){ Lookback = lookback, ZThreshold = zThreshold, AtrPeriod = atrPeriod, AtrMult = atrMult, MaxWalls = maxWalls, ShowWalls = showWalls, ShowInefficientCandles = showInefficientCandles, ShowExpectedClose = showExpectedClose, ShowInfo = showInfo, PublishState = publishState, CardCorner = cardCorner, ShowIndicatorLabel = showIndicatorLabel }, input, ref cacheLiquidityWalls_v1_0_0);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.Sentinel.Sensors.LiquidityWalls_v1_0_0 LiquidityWalls_v1_0_0(int lookback, double zThreshold, int atrPeriod, double atrMult, int maxWalls, bool showWalls, bool showInefficientCandles, bool showExpectedClose, bool showInfo, bool publishState, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return indicator.LiquidityWalls_v1_0_0(Input, lookback, zThreshold, atrPeriod, atrMult, maxWalls, showWalls, showInefficientCandles, showExpectedClose, showInfo, publishState, cardCorner, showIndicatorLabel);
		}

		public Indicators.Sentinel.Sensors.LiquidityWalls_v1_0_0 LiquidityWalls_v1_0_0(ISeries<double> input , int lookback, double zThreshold, int atrPeriod, double atrMult, int maxWalls, bool showWalls, bool showInefficientCandles, bool showExpectedClose, bool showInfo, bool publishState, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return indicator.LiquidityWalls_v1_0_0(input, lookback, zThreshold, atrPeriod, atrMult, maxWalls, showWalls, showInefficientCandles, showExpectedClose, showInfo, publishState, cardCorner, showIndicatorLabel);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.Sentinel.Sensors.LiquidityWalls_v1_0_0 LiquidityWalls_v1_0_0(int lookback, double zThreshold, int atrPeriod, double atrMult, int maxWalls, bool showWalls, bool showInefficientCandles, bool showExpectedClose, bool showInfo, bool publishState, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return indicator.LiquidityWalls_v1_0_0(Input, lookback, zThreshold, atrPeriod, atrMult, maxWalls, showWalls, showInefficientCandles, showExpectedClose, showInfo, publishState, cardCorner, showIndicatorLabel);
		}

		public Indicators.Sentinel.Sensors.LiquidityWalls_v1_0_0 LiquidityWalls_v1_0_0(ISeries<double> input , int lookback, double zThreshold, int atrPeriod, double atrMult, int maxWalls, bool showWalls, bool showInefficientCandles, bool showExpectedClose, bool showInfo, bool publishState, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return indicator.LiquidityWalls_v1_0_0(input, lookback, zThreshold, atrPeriod, atrMult, maxWalls, showWalls, showInefficientCandles, showExpectedClose, showInfo, publishState, cardCorner, showIndicatorLabel);
		}
	}
}

#endregion
