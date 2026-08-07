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
using NinjaTrader.NinjaScript.AddOns.Sentinel;   // SentinelSkin (glass card) + SentinelCore (ProfileState seam) + SentinelCardCorner
using NinjaTrader.NinjaScript.Indicators.Sentinel;
#endregion

// ═════════════════════════════════════════════════════════════════════════════
//  Sentinel Profile — the VOLUME-PROFILE (LOCATION) context axis (CLEAN-ROOM)  |   Version v1.0.0
//  File: SentinelProfile_v1_0_0.cs   |   namespace …Indicators.Sentinel (Context AXIS)   |   Name "Sentinel Profile"
//
//  ⚠ NO ORDERS — read-only advisory indicator. Safe to run anywhere.
//
//  CLEAN-ROOM ORIGINAL. Written from scratch off the PUBLIC volume-profile method — a published,
//  non-copyrightable market-structure technique — using only NinjaTrader's own bar OHLCV. It uses NO
//  third-party code. The installed RedTail / Alighten profile tools were surveyed as design references
//  only; none of their code was copied. See the provenance audit + NOTICE.
//
//  WHY IT MATTERS — most Council voters are momentum/trend derived. VOLUME PROFILE answers a different
//  question: WHERE is price relative to where volume has actually traded? Point of Control (fairest price),
//  the 70% Value Area, and high/low-volume nodes give the Council a LOCATION axis for acceptance vs. rejection.
//
//  THE PUBLIC METHOD (developing SESSION profile):
//    • bins            — a Dictionary keyed by (long)round(price / TickSize). Each bar distributes Volume[0]
//                        evenly across every tick level from Low[0]..High[0]. Reset on the first bar of session.
//    • POC             — the price bin holding the maximum volume (the "fairest" / most-traded price).
//    • Value Area (70%)— start at the POC, repeatedly annex whichever adjacent bin (above VAH or below VAL)
//                        holds more volume, until cumulative ≥ ValueAreaPct of total → VAH (top) / VAL (bottom).
//    • HVN / LVN       — a bin is a High-Volume Node if its volume is a LOCAL MAX above the mean bin volume;
//                        a Low-Volume Node if a LOCAL MIN below the mean. Near = Close within NodeProximityTicks.
//    • Location        = Close>VAH ? +1 (above value) : Close<VAL ? −1 (below value) : 0 (inside value).
//    • Signal          = POC-reversion / mean-reversion lean: Close>VAH ? −1 (fade the push up) :
//                        Close<VAL ? +1 (fade the push down) : 0.
//    • DistPocTicks    = (Close − POC) / TickSize — signed distance to fair value.
//
//  THE SENTINEL PLUMBING (our own code — makes it a suite member):
//    • PUBLISHES SentinelCore.ProfileState (Poc / Vah / Val / Location / Signal / DistPocTicks / NearHVN / NearLVN).
//    • Overlay plots — Poc (cyan), Vah / Val (muted) drawn on the price panel; hidden ±1 "Signal" plot.
//    • A SentinelSkin.Painter glass card + label remover + roster heartbeat.
//
//  CHANGELOG
//    v1.0.0 (2026-07-12) — NEW. Clean-room developing-session volume profile (POC / 70% value area / HVN-LVN).
//             ProfileState publish, overlay POC/VAH/VAL lines, hidden Signal plot, glass card, scope key + heartbeat.
// ═════════════════════════════════════════════════════════════════════════════
namespace NinjaTrader.NinjaScript.Indicators.Sentinel
{
	public class SentinelProfile_v1_0_0 : Indicator
	{
		private SentinelSkin.Painter _sp;
		private bool _hasData;

		// developing session volume profile — bins keyed by (long)round(price / TickSize)
		private readonly Dictionary<long, double> _bins = new Dictionary<long, double>();
		private const int MaxLevelsPerBar = 20000;   // safety clamp so a bad/huge bar can't blow up the dictionary

		// cached state (computed in OnBarUpdate; drawn in OnRender)
		private double _poc, _vah, _val;
		private int    _location;
		private int    _sig;
		private double _distPocTicks;
		private bool   _nearHvn, _nearLvn;
		private int    _lastLoggedLoc = -999;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description              = "Clean-room developing-session volume profile: Point of Control, the 70% Value Area (VAH/VAL), and high/low-volume nodes. Publishes SentinelCore.ProfileState so the Council gains a LOCATION axis — where price sits relative to where volume has actually traded.";
				Name                     = "Sentinel Profile v1.0.0";
				Calculate                = Calculate.OnBarClose;
				IsOverlay                = true;   // POC/VAH/VAL are price levels drawn on the price panel
				DisplayInDataBox         = true;
				DrawOnPricePanel         = true;
				DrawHorizontalGridLines  = true;
				DrawVerticalGridLines    = false;
				PaintPriceMarkers        = true;
				ScaleJustification       = NinjaTrader.Gui.Chart.ScaleJustification.Right;
				IsSuspendedWhileInactive = true;

				ValueAreaPct       = 0.70;
				NodeProximityTicks = 8;

				PublishState       = true;
				LogChanges         = true;
				ShowCard           = true;
				CardCorner         = SentinelCardCorner.TopRight;
				ShowIndicatorLabel = false;

				AddPlot(new Stroke(Brushes.DeepSkyBlue, 2), PlotStyle.Line, "Poc");
				AddPlot(new Stroke(Brushes.Gray, DashStyleHelper.Dash, 1), PlotStyle.Line, "Vah");
				AddPlot(new Stroke(Brushes.Gray, DashStyleHelper.Dash, 1), PlotStyle.Line, "Val");
				// hidden ±1 POC-reversion lean (Values[3]) — transparent; readable by the Deck SIGNAL ARM.
				AddPlot(new Stroke(Brushes.Transparent, 1f), PlotStyle.Line, "Signal");
			}
			else if (State == State.DataLoaded)
			{
				if (!ShowIndicatorLabel) Name = string.Empty;   // Sentinel label remover
			}
			else if (State == State.Terminated)
			{
				if (_sp != null) { try { _sp.Dispose(); } catch (Exception _sx) { SentinelCore.Swallow("SentinelProfile.OnStateChange", _sx); } _sp = null; }
				try { SentinelSkin.CardLayout.Release(this); } catch (Exception _sx) { SentinelCore.Swallow("SentinelProfile.OnStateChange", _sx); }
			}
		}

		// ── scope key ("<masterInstrument>.<barTag>" — ONE CHART's worth of context). Lazily resolved + cached. ──
		private string _scope;
		private string Scope()
		{
			if (_scope == null) { try { _scope = SentinelCore.ScopeOf(Instrument, BarsPeriod); } catch (Exception _sx) { SentinelCore.Swallow("SentinelProfile.Scope", _sx); } }
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
			try { SentinelCore.TouchProfileState(Scope()); } catch (Exception _sx) { SentinelCore.Swallow("SentinelProfile.OnMarketData", _sx); }
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 0 || CurrentBar < 1) return;

			double tick = TickSize;
			if (tick <= 0) return;

			// new session → the developing profile starts fresh
			if (Bars.IsFirstBarOfSession && CurrentBar > 0)
				_bins.Clear();

			// ── distribute this bar's volume evenly across every tick level Low..High ──
			long loT = (long)Math.Round(Low[0]  / tick);
			long hiT = (long)Math.Round(High[0] / tick);
			if (hiT < loT) { long t = loT; loT = hiT; hiT = t; }
			long levels = hiT - loT + 1;
			if (levels < 1) levels = 1;
			if (levels > MaxLevelsPerBar) levels = MaxLevelsPerBar;   // safety clamp
			double vol = Volume[0];
			if (vol > 0)
			{
				double perLevel = vol / levels;
				long end = loT + levels - 1;
				for (long k = loT; k <= end; k++)
				{
					double cur;
					_bins[k] = _bins.TryGetValue(k, out cur) ? cur + perLevel : perLevel;
				}
			}

			if (_bins.Count == 0) return;

			// ── POC = the bin with the most volume + total session volume ──
			long   pocKey = 0;
			double pocVol = double.NegativeInfinity;
			double total  = 0;
			long   minKey = long.MaxValue, maxKey = long.MinValue;
			foreach (var kv in _bins)
			{
				total += kv.Value;
				if (kv.Value > pocVol) { pocVol = kv.Value; pocKey = kv.Key; }
				if (kv.Key < minKey) minKey = kv.Key;
				if (kv.Key > maxKey) maxKey = kv.Key;
			}
			if (total <= 0) return;

			// ── Value Area: grow out from the POC, annexing the heavier adjacent bin until ≥ ValueAreaPct ──
			double target = total * ValueAreaPct;
			long lo = pocKey, hi = pocKey;
			double cum = pocVol;
			while (cum < target && (lo > minKey || hi < maxKey))
			{
				double vAbove = hi < maxKey ? BinVol(hi + 1) : double.NegativeInfinity;
				double vBelow = lo > minKey ? BinVol(lo - 1) : double.NegativeInfinity;
				if (double.IsNegativeInfinity(vAbove) && double.IsNegativeInfinity(vBelow)) break;
				if (vAbove >= vBelow) { hi++; cum += Math.Max(0, vAbove); }
				else                  { lo--; cum += Math.Max(0, vBelow); }
			}

			double poc = pocKey * tick;
			double vah = hi     * tick;
			double val = lo     * tick;

			Poc[0] = poc;
			Vah[0] = vah;
			Val[0] = val;

			// ── HVN / LVN: local extrema of the bin volumes relative to the mean bin volume ──
			double mean = total / _bins.Count;
			bool nearHvn = false, nearLvn = false;
			double proximity = NodeProximityTicks * tick;
			double close = Close[0];
			var sorted = _bins.Keys.ToList();
			sorted.Sort();
			for (int i = 1; i < sorted.Count - 1; i++)
			{
				double v = _bins[sorted[i]];
				double vPrev = _bins[sorted[i - 1]];
				double vNext = _bins[sorted[i + 1]];
				double price = sorted[i] * tick;
				if (v > vPrev && v > vNext && v > mean)      // high-volume node
				{
					if (Math.Abs(close - price) <= proximity) nearHvn = true;
				}
				else if (v < vPrev && v < vNext && v < mean) // low-volume node
				{
					if (Math.Abs(close - price) <= proximity) nearLvn = true;
				}
			}

			int location = close > vah ? 1 : (close < val ? -1 : 0);
			int sig      = close > vah ? -1 : (close < val ? 1 : 0);   // POC-reversion / fade lean
			double distPocTicks = tick > 0 ? (close - poc) / tick : 0;

			Signal[0] = sig;   // hidden plot for the Deck SIGNAL ARM

			_poc = poc; _vah = vah; _val = val;
			_location = location; _sig = sig; _distPocTicks = distPocTicks;
			_nearHvn = nearHvn; _nearLvn = nearLvn; _hasData = true;

			if (PublishState && Instrument != null && Instrument.MasterInstrument != null)
			{
				try
				{
					SentinelCore.SetProfileState(new SentinelCore.ProfileState
					{
						Scope        = Scope(),
						Bartype      = SentinelCore.BarTag(BarsPeriod),
						Instrument   = Instrument.MasterInstrument.Name,
						Poc          = poc,
						Vah          = vah,
						Val          = val,
						Location     = location,
						Signal       = sig,
						DistPocTicks = distPocTicks,
						NearHVN      = nearHvn,
						NearLVN      = nearLvn,
						Source       = "PROF"
					});
				}
				catch (Exception _sx) { SentinelCore.Swallow("SentinelProfile.OnBarUpdate", _sx); }
			}

			if (LogChanges && State == State.Realtime && location != _lastLoggedLoc)
			{
				_lastLoggedLoc = location;
				try
				{
					string inst = Instrument != null && Instrument.MasterInstrument != null ? Instrument.MasterInstrument.Name : "?";
					SentinelCore.Log("PROF", inst + " " +
						(location > 0 ? "above value (▲ over VAH)" : location < 0 ? "below value (▼ under VAL)" : "inside value area") +
						" poc=" + poc.ToString("0.##") + " va=[" + val.ToString("0.##") + "," + vah.ToString("0.##") + "]" +
						(nearHvn ? " nearHVN" : "") + (nearLvn ? " nearLVN" : ""));
				}
				catch (Exception _sx) { SentinelCore.Swallow("SentinelProfile.OnBarUpdate", _sx); }
			}
		}

		private double BinVol(long key)
		{
			double v;
			return _bins.TryGetValue(key, out v) ? v : 0;
		}

		// ── glass card ──
		protected override void OnRender(NinjaTrader.Gui.Chart.ChartControl chartControl, NinjaTrader.Gui.Chart.ChartScale chartScale)
		{
			base.OnRender(chartControl, chartScale);
			if (RenderTarget == null || ChartPanel == null) return;
			if (_sp == null) _sp = new SentinelSkin.Painter();
			_sp.Begin(RenderTarget);
			try { if (ShowCard) RenderCard(); } catch (Exception _sx) { SentinelCore.Swallow("SentinelProfile.OnRender", _sx); }
			try { _sp.End(); } catch (Exception _sx) { SentinelCore.Swallow("SentinelProfile.OnRender", _sx); }
		}

		private void RenderCard()
		{
			const float cw = 228f, ch = 138f;
			var slot = SentinelSkin.CardLayout.Place(this, ChartPanel,
				ChartPanel.X, ChartPanel.Y, ChartPanel.W, ChartPanel.H, CardCorner, cw, ch);

			if (!_hasData)
			{
				var rw = _sp.Card(slot.X, slot.Y, cw, ch, SentinelSkin.CLine);
				_sp.Dot(rw.Left + 5f, rw.Top + 8f, SentinelSkin.CMute, false);
				_sp.Text("PROFILE", rw.Left + 16f, rw.Top, rw.Width - 20f, 16f, SentinelSkin.CInk, 11f, true);
				_sp.Text("building profile…", rw.Left, rw.Top + 26f, rw.Width, 14f, SentinelSkin.CMute, 10.5f);
				return;
			}

			var trail   = SharpDX.DirectWrite.TextAlignment.Trailing;
			bool live    = _sig != 0;
			var locCol  = _location > 0 ? SentinelSkin.CUp : _location < 0 ? SentinelSkin.CDown : SentinelSkin.CMute;
			var heroCol = _location != 0 ? locCol : SentinelSkin.CMute;
			var edge    = live ? SentinelSkin.CAccent : SentinelSkin.CLine;
			var r = _sp.Card(slot.X, slot.Y, cw, ch, edge);

			_sp.Dot(r.Left + 5f, r.Top + 8f, live ? SentinelSkin.CAccent : SentinelSkin.CMute, live);
			_sp.Text("PROFILE", r.Left + 16f, r.Top, r.Width - 90f, 16f, SentinelSkin.CInk, 11f, true);
			_sp.Pill(_location > 0 ? "ABOVE VAH" : _location < 0 ? "BELOW VAL" : "IN VALUE", r.Right, r.Top - 1f, locCol);

			_sp.Text("LOCATION", r.Left, r.Top + 24f, 120f, 12f, SentinelSkin.CMute, 8.5f, true);
			_sp.Text(_location > 0 ? "ABOVE VALUE ▲" : _location < 0 ? "BELOW VALUE ▼" : "inside value",
				r.Left, r.Top + 34f, r.Width, 24f, heroCol, 16f, false);

			_sp.Divider(r.Left, r.Top + 66f, r.Right);
			_sp.Text("poc " + _poc.ToString("0.##"), r.Left, r.Top + 72f, r.Width, 14f, SentinelSkin.CInk2, 10f);
			_sp.Text(_distPocTicks.ToString("+0;-0") + "t", r.Left, r.Top + 72f, r.Width, 14f, SentinelSkin.CInk2, 10f, true, trail);
			_sp.Text("va [" + _val.ToString("0.##") + " – " + _vah.ToString("0.##") + "]",
				r.Left, r.Top + 90f, r.Width, 14f, SentinelSkin.CMute, 10f);
			if (_sig != 0)
				_sp.Text(_sig > 0 ? "fade ▲ (rev up)" : "fade ▼ (rev down)",
					r.Left, r.Top + 108f, r.Width, 14f, SentinelSkin.CWarn, 10f);
			if (_nearHvn || _nearLvn)
				_sp.Text(_nearHvn ? "near HVN" : "near LVN",
					r.Left, r.Top + 108f, r.Width, 14f, SentinelSkin.CInk2, 10f, true, trail);
		}

		#region Properties
		[NinjaScriptProperty]
		[Range(0.5, 0.95)]
		[Display(Name="Value Area %", Description="Fraction of session volume that defines the value area (classic 0.70 = 70%).", Order=1, GroupName="Parameters")]
		public double ValueAreaPct { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Node Proximity Ticks", Description="How close (in ticks) Close must be to a high/low-volume node to flag NearHVN / NearLVN.", Order=2, GroupName="Parameters")]
		public int NodeProximityTicks { get; set; }

		// ── Sentinel ──
		[NinjaScriptProperty]
		[Display(Name="Publish Profile to Sentinel", Description="Publish POC / value area / nodes as SentinelCore.ProfileState so the Council can vote on location.", Order=20, GroupName="Sentinel")]
		public bool PublishState { get; set; }

		[NinjaScriptProperty]
		[Display(Name="Log Location Changes", Description="Write value-area location transitions to sentinel.log.", Order=21, GroupName="Sentinel")]
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
		[Browsable(false)] [XmlIgnore] public Series<double> Poc    => Values[0];
		[Browsable(false)] [XmlIgnore] public Series<double> Vah    => Values[1];
		[Browsable(false)] [XmlIgnore] public Series<double> Val    => Values[2];
		[Browsable(false)] [XmlIgnore] public Series<double> Signal => Values[3];   // ±1 POC-reversion lean / 0
		#endregion
	}
}
