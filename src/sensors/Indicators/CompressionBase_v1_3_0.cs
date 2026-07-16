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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.AddOns.Sentinel;
using NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors;
#endregion

// ═════════════════════════════════════════════════════════════════════════════
//  CompressionBase — coil base detector + EXCURSION RECORDER w/ TP/SL first-touch grid  (Sentinel-homed)
//  File: CompressionBase_v1_3_0.cs   |   Version: v1.3.3   |   namespace …Indicators.Sentinel
// ─────────────────────────────────────────────────────────────────────────────
//  FIRST TOOL TO ADOPT THE SENTINEL NAMESPACE CONVENTION (design-system §7):
//    • REHOMED to namespace  NinjaTrader.NinjaScript.Indicators.Sentinel  → the NT indicator picker groups
//      sub-namespaces into expandable FOLDERS, so this appears under a "Sentinel" folder automatically (verified).
//    • Class name kept CLEAN (no "Sentinel" prefix) — the folder supplies the context: reads "Sentinel › CompressionBase".
//  Behaviour is IDENTICAL to CompressionBase_v1_2_4 — CardLayout-docked glass card + all detector / recording /
//  box-breakout logic unchanged. NT's codegen is namespace-aware, so it's still hostable by simple name.
//  ⚠ NEW TYPE IDENTITY: different type than CompressionBase_v1_2_4 (namespace + version changed) — re-add on charts.
//    Existing placements keep using the frozen v1_2_4. See memory sentinel-namespace-and-naming.
//
//  (See v1.2.3/v1.2.4 headers + memory sentinel-edge-lanes / backtest-fill-resolution-lesson for full context.)
//  Levels (ticks): 10,20,30,40,50,60,80,100. fT<L>/aT<L> = bars to first favorable/adverse touch.
//  File: <SentinelCore.SettingsDir>\Excursions\{localStamp}__{inst}__{bartag}.jsonl · signal "CBRK".
//
//  CHANGELOG
//    v1.3.3 (in-place, fix 2026-07-16) — CADENCE-INDEPENDENT TIGHTNESS GATE (indicator punch list; the whole chart
//             read as one compression base on SentinelFlux). ROOT CAUSE: the coil metric is container/Σ(barRange) — a
//             RATIO that assumes small, non-overlapping bars (Renko/TBars bricks). Event-driven Flux bars are large +
//             overlapping, so that ratio stays chronically ≤ threshold and the base's run-extension never terminated —
//             the box swelled to span the entire price range. FIX: a base must ALSO be physically TIGHT — its box
//             height ≤ `BaseMaxAtrMult` × ATR(14) (new `Tight()`), gating BOTH base formation and run-extension. ATR
//             adapts to the bar type, so this is construction-independent: a genuine TBars coil IS tight (unchanged),
//             while the Flux runaway is capped (it ARMs instead of swelling). New "Base max ATR mult" param (default
//             8.0 — safe for TBars; try 4-6 to tighten Flux). ⚠ This also un-poisons the Council's CMP voter on Flux
//             scopes. Kept IN-PLACE (class/file identity v1_3_0 — no re-add on charts).
//    v1.3.2 (in-place, additive 2026-07-11) — CBRK baseline SCHEMA 1.1 -> 1.3 (ML spec sec 2.3): adds the ATR-scaled
//             FIRST-TOUCH label (barrierTicks / barsToTargetR / barsToStopR / firstTouch / ftAmbig), mirroring the
//             Council ExcursionRecorder, so CBRK baselines carry the SAME unambiguous target-or-stop-first label and
//             become fittable. WRITER-ONLY (RecordExcursions still default OFF; rows still land in Excursions\_baselines\n//             cbrk\<schema>\, OUT of the Council corpus). The fixed-level fT*/aT* touch grid is unchanged. Kept IN-PLACE.
//    v1.3.1 (in-place, additive 2026-07-07) — COUNCIL VOTER: publishes SentinelCore.CompressionState
//             (breakout PULSE + a HELD BreakDir for BreakHoldBars + coil/compressed/armed). PublishState
//             defaults ON. The Council now fuses the breakout as a voter (it previously only saw the hidden
//             Signal plot). No behaviour change to detection/rendering. Needs SentinelCore ≥ v1.11.0.
//    v1.3.0 (in-place, additive 2026-07-05) — Exposes its breakout as a HIDDEN "Signal" plot (Values[2]):
//             +1 on the exact BreakUp bar, -1 on BreakDown, 0 otherwise (mirrors the triangles MarkBreak draws).
//             Lets the Sentinel Deck's SIGNAL ARM read the REAL breakout generically (plot, not drawing-scrape).
//             Plot is transparent + IsAutoScale=false so the ±1 values never render or squash the price panel.
//             Backward-compatible (new plot only) → no version fork, like the Deck's in-place fill-capture add.
//    v1.3.0 — Rehomed to Indicators.Sentinel (→ groups under the "Sentinel" picker folder). Clean class name
//             (no prefix — the folder gives context). No behaviour change vs v1_2_4. First namespace-convention
//             adopter (§7). v1_2_4 frozen.
//    v1.2.4 — [frozen, as CompressionBase_v1_2_4] Card docks via SentinelSkin.CardLayout (anti-overlap) + Card
//             corner property.
//    v1.2.3 — [frozen] SENTINEL GLASS-CARD readout (Painter). DASHED cyan base BOX + box-anchored breakout
//             TRIANGLES + ShowBox/ShowBreakouts. BaseHigh/BaseLow → cyan DOT (ninjascript-plot-config-override fix).
//    v1.2.2 — [frozen] Legible bar-tag (TBC<Value>-<Value2>-<TypeId>).
//    v1.2.1 — [frozen] TP/SL first-touch grid (fT*/aT*).
//    v1.2.0 — [frozen] excursion recorder (max-over-horizon MFE/MAE + context).
//    v1.1.1 — [frozen] coil detector, run-coil maintenance.
// ═════════════════════════════════════════════════════════════════════════════

namespace NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors
{
	public class CompressionBase_v1_3_0 : Indicator
	{
		// Sentinel repalette (2026-07-03): base = cyan accent, breakout up = green, down = red.
		// Static + FROZEN so they're safe to use on the render thread (created once on the config thread).
		private static readonly System.Windows.Media.Brush SB_Base = SFreeze(63, 209, 224);   // cyan accent (card only now)
		private static readonly System.Windows.Media.Brush SB_Up   = SFreeze(37, 208, 139);   // up / breakout long
		private static readonly System.Windows.Media.Brush SB_Down = SFreeze(255, 92, 106);   // down / breakout short
		private static System.Windows.Media.Brush SFreeze(byte r, byte g, byte b)
		{ var br = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b)); br.Freeze(); return br; }

		private int    compressedRun;
		private bool   baseConfirmed;
		private bool   armed;
		private int    baseStartBar;
		private int    armedBar;
		private double baseTop;
		private double baseBottom;
		private int    lastBreakDirection;
		private int    lastBreakBar = -1;   // bar of the last breakout (times the held BreakDir vote for the Council)
		private int    baseCount;
		private int    breakCount;
		private int    abandonCount;
		private double minCoil = double.MaxValue;
		private double maxCoil = double.MinValue;
		private double coilAtConfirm;

		private readonly List<Exc> open = new List<Exc>();
		private string  logPath;
		private int     writtenCount;

		// Sentinel glass-card readout (SharpDX via SentinelSkin.Painter, drawn in OnRender)
		private SentinelSkin.Painter _sp;
		private double lastWindowCoil;

		private const string SchemaVer = "1.3";
		private const string SignalTag = "CBRK";
		private static readonly int[] Levels = { 10, 20, 30, 40, 50, 60, 80, 100 };
		// schema 1.3 (ML spec §1.3/§2.3) — the ATR-scaled FIRST-TOUCH barrier, mirroring the Council ExcursionRecorder:
		// R = BarrierAtrMult × ATR(14) in ticks, floored above the noise, so a CBRK baseline row carries the SAME
		// unambiguous target-or-stop-first label the Council corpus does (which is what makes the two comparable, and
		// makes CBRK's own baselines fittable). Additive: the fixed-level fT*/aT* touch grid is unchanged.
		private const double BarrierAtrMult  = 1.0;
		private const double BarrierMinTicks = 20.0;

		private sealed class Exc
		{
			public double   FirePx;
			public DateTime FireTime;
			public int      FireBar;
			public int      Dir;
			public double   Coil;
			public int      BaseBars;
			public double   BaseHeightTicks;
			public string   Regime;
			public double   Adx;
			public bool     EyeHad;
			public double   EyeScore;
			public int      EyeDir;
			public bool     EyeAligned;
			public double   MaxMFE, MaxMAE;
			public int      BarsToMFE, BarsToMAE;
			public double   MsToMFE, MsToMAE;
			public double   M1f = double.NaN, M1a = double.NaN, M5f = double.NaN, M5a = double.NaN;
			public double   M15f = double.NaN, M15a = double.NaN, M60f = double.NaN, M60a = double.NaN;
			public int[]    FavTouch;
			public int[]    AdvTouch;
			// schema 1.3 first-touch (ATR-scaled barrier + which side crosses R first)
			public double   BarrierTicks;
			public int      FtFavBar;
			public int      FtAdvBar;
		}

		// schema 1.3 — R = ATR(14)-scaled, floored above the noise (a fixed small barrier sits inside gold's ~60t MAE)
		private double FirstTouchBarrier()
		{
			double atrTicks = 0;
			try { if (CurrentBar > 14 && TickSize > 0) atrTicks = ATR(14)[0] / TickSize; } catch { }
			return Math.Max(BarrierMinTicks, BarrierAtrMult * atrTicks);
		}

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description					= "Coil base detector + excursion recorder — Edge lane (no orders). Sentinel namespace/naming.";
				Name						= "Sentinel Compression Base v1.3.3";
				Calculate					= Calculate.OnBarClose;
				IsOverlay					= true;
				DisplayInDataBox			= true;
				DrawOnPricePanel			= true;
				PaintPriceMarkers			= false;
				IsSuspendedWhileInactive	= false;
				IsAutoScale					= false;   // v1.3.0: keep the hidden ±1 Signal plot from squashing the price panel (base dots sit within price range anyway)

				CompressionWindow	= 10;
				CoilThreshold		= 0.25;
				BaseMaxAtrMult		= 8.0;    // v1.3.3 — max base box height in ATR(14) multiples (cadence-independent tightness gate). Lower = tighter bases; raise if legit bases get cut short.
				MinBaseBars			= 4;
				BreakWindow			= 10;
				RecordExcursions	= false;   // default OFF — Sentinel\Excursions is the COUNCIL training corpus (one writer). Opt-in only.
				MaxHorizonMin		= 60;
				AdxPeriod			= 14;
				ShowInfo			= true;
				ShowBox				= true;
				ShowBreakouts		= true;
				CardCorner			= SentinelCardCorner.TopRight;
				PublishState		= true;    // default ON — feed the Council's Compression (breakout) voter out of the box
				BreakHoldBars		= 10;      // hold the breakout direction this many bars as the Council vote
				ShowIndicatorLabel	= false;   // Sentinel standard: clean chart (NT name label removed)

				// BaseHigh/BaseLow render as cyan DOTS (see v1.2.3 root-cause fix — PlotStyle.Dot can't draw the
				// spurious "vertical line to the top"). A CHART's SAVED plot config overrides these code defaults.
				AddPlot(new Stroke(SB_Base, 3f), PlotStyle.Dot, "BaseHigh");
				AddPlot(new Stroke(SB_Base, 3f), PlotStyle.Dot, "BaseLow");
				// v1.3.0: hidden breakout signal series (+1 BreakUp / -1 BreakDown / 0). Transparent + IsAutoScale=false
				// so it never renders or scales; readable as Values[2] "Signal" by the Sentinel Deck's SIGNAL ARM.
				AddPlot(new Stroke(System.Windows.Media.Brushes.Transparent, 1f), PlotStyle.Line, "Signal");
			}
			else if (State == State.DataLoaded)
			{
				if (!ShowIndicatorLabel) Name = string.Empty;   // Sentinel label remover — NT draws the chart panel label from Name (see LabelRemover.cs)
				BaseState			= new Series<double>(this);
				BaseTopSeries		= new Series<double>(this);
				BaseBottomSeries	= new Series<double>(this);
			}
			else if (State == State.Terminated)
			{
				if (RecordExcursions)
					FlushAll("terminated");
				if (_sp != null) { try { _sp.Dispose(); } catch { } _sp = null; }
				try { SentinelSkin.CardLayout.Release(this); } catch { }
			}
		}

		private double Coil(int k)
		{
			double container = MAX(High, k)[0] - MIN(Low, k)[0];
			double path = 0;
			for (int i = 0; i < k; i++)
				path += (High[i] - Low[i]);
			return path > 0 ? container / path : 1.0;
		}

		// v1.3.3 — the k-bar box HEIGHT (price envelope). Paired with Tight() for the cadence-independent gate below.
		private double Container(int k)
		{
			return MAX(High, k)[0] - MIN(Low, k)[0];
		}

		// v1.3.3 — is the k-bar box PHYSICALLY TIGHT (height small vs ATR)? The coil RATIO alone (container/Σrange)
		// reads chronically "compressed" on large/overlapping event-driven bars (SentinelFlux), so the base run-
		// extension never terminated and the box swelled to the whole price range. A real base is ALSO tight in
		// absolute terms; ATR adapts to the bar type, so this gate is construction-independent (unchanged on TBars,
		// where a genuine coil IS tight; it caps the Flux runaway). Permissive during ATR warmup (atr ≤ 0).
		private bool Tight(int k, double atr)
		{
			return atr <= 0 || Container(k) <= BaseMaxAtrMult * atr;
		}

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
		    try { SentinelCore.TouchCompressionState(Scope()); } catch { }
		}

		protected override void OnBarUpdate()
		{
			Signal[0] = 0;   // v1.3.0: default no-signal; MarkBreak sets +1/-1 on a breakout bar

			if (CurrentBar < CompressionWindow + 1)
			{
				BaseHigh[0] = BaseLow[0] = double.NaN;
				BaseState[0] = 0;
				BaseTopSeries[0] = BaseBottomSeries[0] = double.NaN;
				return;
			}

			if (RecordExcursions && Bars.IsFirstBarOfSession && open.Count > 0)
				FlushAll("session");

			double windowCoil = Coil(CompressionWindow);
			// v1.3.3 — gate the coil SHAPE test with the volatility-normalized TIGHTNESS test (see Tight()), so a
			// base only forms when the window is BOTH coiled AND physically tight. Fixes the whole-chart false base
			// on event-driven bar types (SentinelFlux). atrPrice is reused by the base run-extension gate below.
			double atrPrice = 0; try { atrPrice = ATR(14)[0]; } catch { }
			bool   compressed = windowCoil <= CoilThreshold && Tight(CompressionWindow, atrPrice);

			if (windowCoil < minCoil) minCoil = windowCoil;
			if (windowCoil > maxCoil) maxCoil = windowCoil;

			if (armed)
			{
				if (Close[0] > baseTop)        { MarkBreak(1);  ResetBase(); }
				else if (Close[0] < baseBottom){ MarkBreak(-1); ResetBase(); }
				else if (CurrentBar - armedBar >= BreakWindow) { abandonCount++; ResetBase(); }
			}
			else if (baseConfirmed)
			{
				int    len     = CurrentBar - baseStartBar + 1;
				double runCoil = Coil(len);
				// v1.3.3 — extend the base ONLY while it stays coiled AND still physically tight. Without the Tight()
				// cap the box grew unbounded on SentinelFlux (runCoil stayed ≤ threshold forever) → whole-chart band.
				// When the box outgrows BaseMaxAtrMult × ATR it ARMs (falls to the else branch) instead of swelling.
				if (runCoil <= CoilThreshold && Tight(len, atrPrice))
				{
					baseTop   = MAX(High, len)[0];
					baseBottom= MIN(Low,  len)[0];
					DrawBox();
				}
				else
				{
					armed    = true;
					armedBar = CurrentBar;
					DrawBox();
				}
			}
			else
			{
				if (compressed)
				{
					compressedRun++;
					if (compressedRun >= MinBaseBars)
					{
						baseConfirmed = true;
						baseStartBar  = CurrentBar - compressedRun + 1;
						baseCount++;
						baseTop   = MAX(High, compressedRun)[0];
						baseBottom= MIN(Low,  compressedRun)[0];
						coilAtConfirm = windowCoil;
						DrawBox();
					}
				}
				else
					compressedRun = 0;
			}

			if (RecordExcursions && open.Count > 0)
				UpdateExcursions();

			if (baseConfirmed)
			{
				BaseHigh[0]        = baseTop;
				BaseLow[0]         = baseBottom;
				BaseState[0]       = armed ? 2 : 1;
				BaseTopSeries[0]   = baseTop;
				BaseBottomSeries[0]= baseBottom;
			}
			else
			{
				BaseHigh[0] = BaseLow[0] = double.NaN;
				BaseState[0] = 0;
				BaseTopSeries[0] = BaseBottomSeries[0] = double.NaN;
			}

			if (ShowInfo)
				DrawInfo(windowCoil);

			// v1.3.1: publish breakout/coil state so the Council gains a breakout VOTER (SentinelCore ≥ v1.11.0)
			if (PublishState && Instrument != null && Instrument.MasterInstrument != null)
			{
				int held = (lastBreakBar >= 0 && CurrentBar - lastBreakBar <= BreakHoldBars) ? lastBreakDirection : 0;
				try
				{
					SentinelCore.SetCompressionState(new SentinelCore.CompressionState
					{
						// SentinelCore ≥ v1.19.0 — keyed by SCOPE, not instrument.
						Scope = Scope(), Bartype = SentinelCore.BarTag(BarsPeriod),
						Instrument = Instrument.MasterInstrument.Name,
						Signal = (int)Signal[0], BreakDir = held, Coil = windowCoil,
						Compressed = compressed, Armed = armed, Source = "CompressionBase"
					});
				}
				catch { }
			}
		}

		private void DrawBox()
		{
			if (!ShowBox) return;   // optional: hide the base box

			var box = Draw.Rectangle(this, "cbBox" + baseStartBar, false,
				CurrentBar - baseStartBar, baseBottom, 0, baseTop,
				SB_Base, SB_Base, 6);
			box.OutlineStroke = new Stroke(SB_Base, DashStyleHelper.Dash, 1.6f);
		}

		private void MarkBreak(int dir)
		{
			lastBreakDirection = dir;
			lastBreakBar = CurrentBar;
			breakCount++;
			Signal[0] = dir;   // v1.3.0: publish the breakout on this bar's Signal plot (+1 up / -1 down) for the Deck

			if (ShowBreakouts)
			{
				// anchor the breakout marker to the BOX/dot extreme (not the candle) with clearance.
				double off = Math.Max((baseTop - baseBottom) * 0.25, 6 * TickSize);
				if (dir > 0)
					Draw.TriangleUp(this, "cbBrkUp" + CurrentBar, false, 0, baseBottom - off, SB_Up);
				else
					Draw.TriangleDown(this, "cbBrkDn" + CurrentBar, false, 0, baseTop + off, SB_Down);
			}

			if (RecordExcursions)
				OpenExcursion(dir);
		}

		private void ResetBase()
		{
			baseConfirmed = false;
			armed         = false;
			compressedRun = 0;
		}

		private void OpenExcursion(int dir)
		{
			double adx = (CurrentBar > AdxPeriod) ? ADX(AdxPeriod)[0] : 0;
			var e = new Exc
			{
				FirePx          = Close[0],
				FireTime        = Time[0],
				FireBar         = CurrentBar,
				Dir             = dir,
				Coil            = coilAtConfirm,
				BaseBars        = armedBar > 0 ? (armedBar - baseStartBar + 1) : (CurrentBar - baseStartBar + 1),
				BaseHeightTicks = (baseTop - baseBottom) / TickSize,
				Adx             = adx,
				Regime          = adx >= 25 ? "trend" : (adx < 20 ? "chop" : "mid"),
				MaxMFE = 0, MaxMAE = 0,
				FavTouch = new int[Levels.Length],
				AdvTouch = new int[Levels.Length],
				BarrierTicks = FirstTouchBarrier(), FtFavBar = -1, FtAdvBar = -1
			};
			for (int i = 0; i < Levels.Length; i++) { e.FavTouch[i] = -1; e.AdvTouch[i] = -1; }

			try
			{
				var v = SentinelCore.GetEyeVerdict(Instrument.MasterInstrument.Name, 0);
				if (v != null)
				{
					e.EyeHad = true;
					e.EyeScore = v.Score;
					e.EyeDir = v.Direction;
					e.EyeAligned = v.Direction == dir;
				}
			}
			catch { }

			open.Add(e);
		}

		private void UpdateExcursions()
		{
			double tick = TickSize;
			for (int i = open.Count - 1; i >= 0; i--)
			{
				Exc e = open[i];
				double fav = e.Dir > 0 ? (High[0] - e.FirePx) / tick : (e.FirePx - Low[0]) / tick;
				double adv = e.Dir > 0 ? (e.FirePx - Low[0]) / tick : (High[0] - e.FirePx) / tick;
				int    barsSince = CurrentBar - e.FireBar;

				if (fav > e.MaxMFE) { e.MaxMFE = fav; e.BarsToMFE = barsSince; e.MsToMFE = (Time[0] - e.FireTime).TotalMilliseconds; }
				if (adv > e.MaxMAE) { e.MaxMAE = adv; e.BarsToMAE = barsSince; e.MsToMAE = (Time[0] - e.FireTime).TotalMilliseconds; }

				for (int j = 0; j < Levels.Length; j++)
				{
					if (e.FavTouch[j] < 0 && fav >= Levels[j]) e.FavTouch[j] = barsSince;
					if (e.AdvTouch[j] < 0 && adv >= Levels[j]) e.AdvTouch[j] = barsSince;
				}

				// schema 1.3 first-touch — latch the bar each side first crosses the ATR-scaled barrier R (never re-set)
				if (e.FtFavBar < 0 && fav >= e.BarrierTicks) e.FtFavBar = barsSince;
				if (e.FtAdvBar < 0 && adv >= e.BarrierTicks) e.FtAdvBar = barsSince;

				double em = (Time[0] - e.FireTime).TotalMinutes;
				if (em >= 1  && double.IsNaN(e.M1f))  { e.M1f  = e.MaxMFE; e.M1a  = e.MaxMAE; }
				if (em >= 5  && double.IsNaN(e.M5f))  { e.M5f  = e.MaxMFE; e.M5a  = e.MaxMAE; }
				if (em >= 15 && double.IsNaN(e.M15f)) { e.M15f = e.MaxMFE; e.M15a = e.MaxMAE; }
				if (em >= 60 && double.IsNaN(e.M60f)) { e.M60f = e.MaxMFE; e.M60a = e.MaxMAE; }

				if (em >= MaxHorizonMin)
				{
					WriteRecord(e, "horizon");
					open.RemoveAt(i);
				}
			}
		}

		private void FlushAll(string reason)
		{
			for (int i = 0; i < open.Count; i++)
				WriteRecord(open[i], reason);
			open.Clear();
		}

		private void WriteRecord(Exc e, string endReason)
		{
			try
			{
				if (logPath == null)
				{
					// opt-in baseline data lives OUT of the Council training corpus (Excursions\council\<schema>) — see
					// memory corpus-hygiene-and-fill-fidelity: one writer/one schema per folder.
					string dir = Path.Combine(SentinelCore.SettingsDir, "Excursions", "_baselines", SignalTag.ToLowerInvariant(), SchemaVer);
					Directory.CreateDirectory(dir);
					string stamp = DateTime.Now.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture);
					logPath = Path.Combine(dir, stamp + "__" + Safe(InstName()) + "__" + Safe(BarTag()) + ".jsonl");
				}

				var sb = new StringBuilder(768);
				sb.Append('{');
				S(sb, "schema", SchemaVer);      C(sb); S(sb, "kind", "excursion"); C(sb);
				S(sb, "inst", InstName());       C(sb); S(sb, "bartype", BarTag()); C(sb);
				S(sb, "signal", SignalTag);      C(sb); N(sb, "dir", e.Dir);        C(sb);
				S(sb, "regime", e.Regime);       C(sb); N(sb, "adx", e.Adx);        C(sb);
				B(sb, "eyeHad", e.EyeHad);       C(sb);
				if (e.EyeHad) { N(sb, "eyeScore", e.EyeScore); C(sb); N(sb, "eyeDir", e.EyeDir); C(sb); B(sb, "eyeAligned", e.EyeAligned); C(sb); }
				else          { Null(sb, "eyeScore"); C(sb); Null(sb, "eyeDir"); C(sb); Null(sb, "eyeAligned"); C(sb); }
				S(sb, "fireTime", e.FireTime.ToString("o", CultureInfo.InvariantCulture)); C(sb);
				N(sb, "firePx", e.FirePx);       C(sb);
				N(sb, "maxMFE", e.MaxMFE);       C(sb); N(sb, "maxMAE", e.MaxMAE);   C(sb);
				N(sb, "barsToMFE", e.BarsToMFE); C(sb); N(sb, "barsToMAE", e.BarsToMAE); C(sb);
				N(sb, "msToMFE", e.MsToMFE);     C(sb); N(sb, "msToMAE", e.MsToMAE); C(sb);
				N(sb, "bars", CurrentBar - e.FireBar); C(sb);
				Nn(sb, "mfe1", e.M1f);  C(sb); Nn(sb, "mae1", e.M1a);  C(sb);
				Nn(sb, "mfe5", e.M5f);  C(sb); Nn(sb, "mae5", e.M5a);  C(sb);
				Nn(sb, "mfe15", e.M15f); C(sb); Nn(sb, "mae15", e.M15a); C(sb);
				Nn(sb, "mfe60", e.M60f); C(sb); Nn(sb, "mae60", e.M60a); C(sb);
				N(sb, "coil", e.Coil);           C(sb); N(sb, "baseBars", e.BaseBars); C(sb);
				N(sb, "baseHeightTicks", e.BaseHeightTicks);
				for (int j = 0; j < Levels.Length; j++)
				{
					int L = Levels[j];
					C(sb); if (e.FavTouch[j] >= 0) N(sb, "fT" + L, e.FavTouch[j]); else Null(sb, "fT" + L);
					C(sb); if (e.AdvTouch[j] >= 0) N(sb, "aT" + L, e.AdvTouch[j]); else Null(sb, "aT" + L);
				}
				// schema 1.3 first-touch label (ML spec §2.3): +1 target-first · -1 stop-first · 0 neither by end · ftAmbig = same bar
				int firstTouch; bool ftAmbig = false;
				if (e.FtFavBar >= 0 && e.FtAdvBar >= 0) { if (e.FtFavBar < e.FtAdvBar) firstTouch = 1; else if (e.FtAdvBar < e.FtFavBar) firstTouch = -1; else { firstTouch = 0; ftAmbig = true; } }
				else if (e.FtFavBar >= 0) firstTouch = 1;
				else if (e.FtAdvBar >= 0) firstTouch = -1;
				else firstTouch = 0;
				C(sb); N(sb, "barrierTicks", e.BarrierTicks);
				C(sb); N(sb, "barsToTargetR", e.FtFavBar);
				C(sb); N(sb, "barsToStopR", e.FtAdvBar);
				C(sb); N(sb, "firstTouch", firstTouch);
				C(sb); B(sb, "ftAmbig", ftAmbig);
				C(sb); S(sb, "endReason", endReason);
				C(sb); S(sb, "endTime", Time[0].ToString("o", CultureInfo.InvariantCulture));
				sb.Append('}');

				File.AppendAllText(logPath, sb.ToString() + Environment.NewLine);
				writtenCount++;
			}
			catch (Exception ex)
			{
				try { SentinelCore.Log("CBRK", "excursion write failed: " + ex.Message); } catch { }
			}
		}

		private static void C(StringBuilder sb) { sb.Append(','); }
		private static void S(StringBuilder sb, string k, string v) { sb.Append('"').Append(k).Append("\":\"").Append(v == null ? "" : v.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"'); }
		private static void N(StringBuilder sb, string k, double v) { sb.Append('"').Append(k).Append("\":").Append((double.IsNaN(v) || double.IsInfinity(v)) ? "null" : Math.Round(v, 4).ToString(CultureInfo.InvariantCulture)); }
		private static void Nn(StringBuilder sb, string k, double v) { if (double.IsNaN(v)) Null(sb, k); else N(sb, k, v); }
		private static void B(StringBuilder sb, string k, bool v) { sb.Append('"').Append(k).Append("\":").Append(v ? "true" : "false"); }
		private static void Null(StringBuilder sb, string k) { sb.Append('"').Append(k).Append("\":null"); }
		private static string Safe(string s) { return string.IsNullOrEmpty(s) ? "x" : s.Replace(" ", "").Replace("\\", "").Replace("/", ""); }

		private string InstName() { return (Instrument != null && Instrument.MasterInstrument != null) ? Instrument.MasterInstrument.Name : "unknown"; }
		// legible: TBC<Value>-<Value2>-<TypeId>  (Value / Value2 carry the TBars size; TypeId disambiguates)
		private string BarTag()   { return "TBC" + BarsPeriod.Value + "-" + BarsPeriod.Value2 + "-" + ((int)BarsPeriod.BarsPeriodType); }

		// cache the latest coil for the OnRender glass card.
		private void DrawInfo(double windowCoil)
		{
			lastWindowCoil = windowCoil;
		}

		// the Sentinel "flight-instrument" glass card — docks via SentinelSkin.CardLayout so it never covers
		// another Sentinel card.
		protected override void OnRender(NinjaTrader.Gui.Chart.ChartControl chartControl, NinjaTrader.Gui.Chart.ChartScale chartScale)
		{
			base.OnRender(chartControl, chartScale);
			if (!ShowInfo || RenderTarget == null || ChartPanel == null) return;
			try
			{
				if (_sp == null) _sp = new SentinelSkin.Painter();
				_sp.Begin(RenderTarget);

				const float cw = 252f, ch = 143f;
				var slot = SentinelSkin.CardLayout.Place(this, ChartPanel,
					ChartPanel.X, ChartPanel.Y, ChartPanel.W, ChartPanel.H, CardCorner, cw, ch);

				var edge = armed ? SentinelSkin.CAccent : (baseConfirmed ? SentinelSkin.CLine : SentinelSkin.CDim);
				var r = _sp.Card(slot.X, slot.Y, cw, ch, edge);

				// header: live dot + title + state pill
				bool live = baseConfirmed;
				_sp.Dot(r.Left + 5f, r.Top + 8f, live ? SentinelSkin.CAccent : SentinelSkin.CMute, live);
				_sp.Text("COMPRESSION BASE", r.Left + 16f, r.Top, r.Width - 70f, 16f, SentinelSkin.CInk, 11f, true);
				string st = !baseConfirmed ? "IDLE" : (armed ? "ARMED" : "FORMING");
				var stCol = !baseConfirmed ? SentinelSkin.CMute : (armed ? SentinelSkin.CAccent : SentinelSkin.CWarn);
				_sp.Pill(st, r.Right, r.Top - 1f, stCol);

				// coil hero + threshold
				double coil = lastWindowCoil, thr = CoilThreshold;
				bool compressed = coil <= thr;
				var coilCol = compressed ? SentinelSkin.CAccent : SentinelSkin.CInk2;
				_sp.Text("COIL", r.Left, r.Top + 26f, 60f, 12f, SentinelSkin.CMute, 9f, true);
				_sp.Text(coil.ToString("0.00"), r.Left, r.Top + 35f, 90f, 26f, coilCol, 22f);
				_sp.Text("/ thr " + thr.ToString("0.00"), r.Left + 72f, r.Top + 45f, 120f, 14f, SentinelSkin.CMute, 10f);

				// coil-vs-threshold track (thr sits at 50%; cyan when compressed, faint otherwise)
				float frac = thr > 0 ? (float)Math.Max(0, Math.Min(1, coil / (2.0 * thr))) : 0f;
				_sp.Track(r.Left, r.Top + 66f, r.Width, frac, compressed ? SentinelSkin.CAccent : SentinelSkin.CFaint, 5f);

				// stat rows (mono)
				var lead = SharpDX.DirectWrite.TextAlignment.Leading;
				_sp.Text("bases " + baseCount + "   breaks " + breakCount + "   abandon " + abandonCount,
					r.Left, r.Top + 78f, r.Width, 14f, SentinelSkin.CInk2, 10.5f, false, lead, true);
				double lo = minCoil == double.MaxValue ? 0 : minCoil, hi = maxCoil == double.MinValue ? 0 : maxCoil;
				_sp.Text("rec " + writtenCount + "   open " + open.Count + "   seen " + lo.ToString("0.00") + "-" + hi.ToString("0.00"),
					r.Left, r.Top + 92f, r.Width, 14f, SentinelSkin.CMute, 10f, false, lead, true);
				_sp.Text(BarTag(), r.Left, r.Top + 106f, r.Width, 12f, SentinelSkin.CMute, 9f, false, lead, true);

				_sp.End();
			}
			catch { }
		}

		#region Consumable "current" surface
		[Browsable(false)] [XmlIgnore] public bool   BaseActive         => baseConfirmed;
		[Browsable(false)] [XmlIgnore] public double CurrentBaseTop     => baseTop;
		[Browsable(false)] [XmlIgnore] public double CurrentBaseBottom  => baseBottom;
		[Browsable(false)] [XmlIgnore] public int    LastBreakDirection => lastBreakDirection;
		[Browsable(false)] [XmlIgnore] public int    BaseAgeBars        => baseConfirmed ? (CurrentBar - baseStartBar + 1) : 0;
		#endregion

		#region Plots & historical series
		[Browsable(false)] [XmlIgnore] public Series<double> BaseHigh => Values[0];
		[Browsable(false)] [XmlIgnore] public Series<double> BaseLow  => Values[1];
		[Browsable(false)] [XmlIgnore] public Series<double> Signal   => Values[2];   // v1.3.0: +1 BreakUp bar / -1 BreakDown bar / 0 otherwise
		[Browsable(false)] [XmlIgnore] public Series<double> BaseState        { get; private set; }
		[Browsable(false)] [XmlIgnore] public Series<double> BaseTopSeries    { get; private set; }
		[Browsable(false)] [XmlIgnore] public Series<double> BaseBottomSeries { get; private set; }
		#endregion

		#region Parameters
		[NinjaScriptProperty] [Range(2, int.MaxValue)]
		[Display(Name = "Compression window", Order = 1, GroupName = "Detection")]
		public int CompressionWindow { get; set; }

		[NinjaScriptProperty] [Range(0.05, 1.0)]
		[Display(Name = "Coil threshold", Order = 2, GroupName = "Detection")]
		public double CoilThreshold { get; set; }

		[NinjaScriptProperty] [Range(1.0, double.MaxValue)]
		[Display(Name = "Base max ATR mult", Description = "Max base box HEIGHT as a multiple of ATR(14) — the cadence-independent tightness gate (v1.3.3). Stops the box growing unbounded on event-driven bar types (SentinelFlux), where the coil ratio alone reads the whole chart as one base. Lower = tighter bases; 8 is safe for TBars, try 4-6 to tighten Flux.", Order = 3, GroupName = "Detection")]
		public double BaseMaxAtrMult { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Publish to Sentinel", Description = "Publish breakout/coil state as SentinelCore.CompressionState so the Council gains a breakout voter. Needs SentinelCore ≥ v1.11.0.", Order = 20, GroupName = "Sentinel")]
		public bool PublishState { get; set; }

		[NinjaScriptProperty] [Range(1, int.MaxValue)]
		[Display(Name = "Break Hold Bars", Description = "Hold the breakout direction this many bars after a break as the Council's vote (a one-bar pulse is too fleeting).", Order = 21, GroupName = "Sentinel")]
		public int BreakHoldBars { get; set; }

		[NinjaScriptProperty] [Range(1, int.MaxValue)]
		[Display(Name = "Min base bars", Order = 3, GroupName = "Detection")]
		public int MinBaseBars { get; set; }

		[NinjaScriptProperty] [Range(1, int.MaxValue)]
		[Display(Name = "Break window", Order = 4, GroupName = "Detection")]
		public int BreakWindow { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Record excursions", Order = 5, GroupName = "Recording")]
		public bool RecordExcursions { get; set; }

		[NinjaScriptProperty] [Range(1, int.MaxValue)]
		[Display(Name = "Max horizon (min)", Order = 6, GroupName = "Recording")]
		public int MaxHorizonMin { get; set; }

		[NinjaScriptProperty] [Range(2, int.MaxValue)]
		[Display(Name = "ADX period", Order = 7, GroupName = "Recording")]
		public int AdxPeriod { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show info readout", Order = 8, GroupName = "Display")]
		public bool ShowInfo { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show box", Order = 9, GroupName = "Display")]
		public bool ShowBox { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show breakouts", Order = 10, GroupName = "Display")]
		public bool ShowBreakouts { get; set; }

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
		private Sentinel.Sensors.CompressionBase_v1_3_0[] cacheCompressionBase_v1_3_0;
		public Sentinel.Sensors.CompressionBase_v1_3_0 CompressionBase_v1_3_0(int compressionWindow, double coilThreshold, double baseMaxAtrMult, bool publishState, int breakHoldBars, int minBaseBars, int breakWindow, bool recordExcursions, int maxHorizonMin, int adxPeriod, bool showInfo, bool showBox, bool showBreakouts, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return CompressionBase_v1_3_0(Input, compressionWindow, coilThreshold, baseMaxAtrMult, publishState, breakHoldBars, minBaseBars, breakWindow, recordExcursions, maxHorizonMin, adxPeriod, showInfo, showBox, showBreakouts, cardCorner, showIndicatorLabel);
		}

		public Sentinel.Sensors.CompressionBase_v1_3_0 CompressionBase_v1_3_0(ISeries<double> input, int compressionWindow, double coilThreshold, double baseMaxAtrMult, bool publishState, int breakHoldBars, int minBaseBars, int breakWindow, bool recordExcursions, int maxHorizonMin, int adxPeriod, bool showInfo, bool showBox, bool showBreakouts, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			if (cacheCompressionBase_v1_3_0 != null)
				for (int idx = 0; idx < cacheCompressionBase_v1_3_0.Length; idx++)
					if (cacheCompressionBase_v1_3_0[idx] != null && cacheCompressionBase_v1_3_0[idx].CompressionWindow == compressionWindow && cacheCompressionBase_v1_3_0[idx].CoilThreshold == coilThreshold && cacheCompressionBase_v1_3_0[idx].BaseMaxAtrMult == baseMaxAtrMult && cacheCompressionBase_v1_3_0[idx].PublishState == publishState && cacheCompressionBase_v1_3_0[idx].BreakHoldBars == breakHoldBars && cacheCompressionBase_v1_3_0[idx].MinBaseBars == minBaseBars && cacheCompressionBase_v1_3_0[idx].BreakWindow == breakWindow && cacheCompressionBase_v1_3_0[idx].RecordExcursions == recordExcursions && cacheCompressionBase_v1_3_0[idx].MaxHorizonMin == maxHorizonMin && cacheCompressionBase_v1_3_0[idx].AdxPeriod == adxPeriod && cacheCompressionBase_v1_3_0[idx].ShowInfo == showInfo && cacheCompressionBase_v1_3_0[idx].ShowBox == showBox && cacheCompressionBase_v1_3_0[idx].ShowBreakouts == showBreakouts && cacheCompressionBase_v1_3_0[idx].CardCorner == cardCorner && cacheCompressionBase_v1_3_0[idx].ShowIndicatorLabel == showIndicatorLabel && cacheCompressionBase_v1_3_0[idx].EqualsInput(input))
						return cacheCompressionBase_v1_3_0[idx];
			return CacheIndicator<Sentinel.Sensors.CompressionBase_v1_3_0>(new Sentinel.Sensors.CompressionBase_v1_3_0(){ CompressionWindow = compressionWindow, CoilThreshold = coilThreshold, BaseMaxAtrMult = baseMaxAtrMult, PublishState = publishState, BreakHoldBars = breakHoldBars, MinBaseBars = minBaseBars, BreakWindow = breakWindow, RecordExcursions = recordExcursions, MaxHorizonMin = maxHorizonMin, AdxPeriod = adxPeriod, ShowInfo = showInfo, ShowBox = showBox, ShowBreakouts = showBreakouts, CardCorner = cardCorner, ShowIndicatorLabel = showIndicatorLabel }, input, ref cacheCompressionBase_v1_3_0);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.Sentinel.Sensors.CompressionBase_v1_3_0 CompressionBase_v1_3_0(int compressionWindow, double coilThreshold, double baseMaxAtrMult, bool publishState, int breakHoldBars, int minBaseBars, int breakWindow, bool recordExcursions, int maxHorizonMin, int adxPeriod, bool showInfo, bool showBox, bool showBreakouts, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return indicator.CompressionBase_v1_3_0(Input, compressionWindow, coilThreshold, baseMaxAtrMult, publishState, breakHoldBars, minBaseBars, breakWindow, recordExcursions, maxHorizonMin, adxPeriod, showInfo, showBox, showBreakouts, cardCorner, showIndicatorLabel);
		}

		public Indicators.Sentinel.Sensors.CompressionBase_v1_3_0 CompressionBase_v1_3_0(ISeries<double> input , int compressionWindow, double coilThreshold, double baseMaxAtrMult, bool publishState, int breakHoldBars, int minBaseBars, int breakWindow, bool recordExcursions, int maxHorizonMin, int adxPeriod, bool showInfo, bool showBox, bool showBreakouts, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return indicator.CompressionBase_v1_3_0(input, compressionWindow, coilThreshold, baseMaxAtrMult, publishState, breakHoldBars, minBaseBars, breakWindow, recordExcursions, maxHorizonMin, adxPeriod, showInfo, showBox, showBreakouts, cardCorner, showIndicatorLabel);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.Sentinel.Sensors.CompressionBase_v1_3_0 CompressionBase_v1_3_0(int compressionWindow, double coilThreshold, double baseMaxAtrMult, bool publishState, int breakHoldBars, int minBaseBars, int breakWindow, bool recordExcursions, int maxHorizonMin, int adxPeriod, bool showInfo, bool showBox, bool showBreakouts, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return indicator.CompressionBase_v1_3_0(Input, compressionWindow, coilThreshold, baseMaxAtrMult, publishState, breakHoldBars, minBaseBars, breakWindow, recordExcursions, maxHorizonMin, adxPeriod, showInfo, showBox, showBreakouts, cardCorner, showIndicatorLabel);
		}

		public Indicators.Sentinel.Sensors.CompressionBase_v1_3_0 CompressionBase_v1_3_0(ISeries<double> input , int compressionWindow, double coilThreshold, double baseMaxAtrMult, bool publishState, int breakHoldBars, int minBaseBars, int breakWindow, bool recordExcursions, int maxHorizonMin, int adxPeriod, bool showInfo, bool showBox, bool showBreakouts, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return indicator.CompressionBase_v1_3_0(input, compressionWindow, coilThreshold, baseMaxAtrMult, publishState, breakHoldBars, minBaseBars, breakWindow, recordExcursions, maxHorizonMin, adxPeriod, showInfo, showBox, showBreakouts, cardCorner, showIndicatorLabel);
		}
	}
}

#endregion
