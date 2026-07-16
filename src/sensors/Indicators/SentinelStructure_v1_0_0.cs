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
using NinjaTrader.NinjaScript.AddOns.Sentinel;   // SentinelSkin (glass card) + SentinelCore (StructureState seam) + SentinelCardCorner
using NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors;
#endregion

// ═════════════════════════════════════════════════════════════════════════════
//  Sentinel Structure — the MARKET-STRUCTURE axis (CLEAN-ROOM)               |   Version v1.0.0
//  File: SentinelStructure_v1_0_0.cs   |   namespace …Indicators.Sentinel.Sensors (Tier-③ SENSOR)   |   Name "Sentinel Structure"
//
//  ⚠ NO ORDERS — read-only advisory indicator. Safe to run anywhere.
//
//  CLEAN-ROOM ORIGINAL. Written from scratch off the PUBLIC market-structure method — a published,
//  non-copyrightable price-action technique: swing-pivot detection → HH/HL/LH/LL classification →
//  break-of-structure. It uses NO third-party code and does NOT call NinjaTrader's own Swing indicator;
//  the pivot detector is self-contained. The installed PriceActionSwingPro.cs (CC BY-NC-SA) was NOT
//  copied — only its CONCEPT (swing pivots + structure labels) was surveyed as a design reference.
//  See the provenance audit + NOTICE.
//
//  WHY IT MATTERS — most Council voters read momentum/volatility. MARKET STRUCTURE reads the SKELETON of
//  price: the sequence of confirmed swing highs and lows. Higher-high + higher-low = an up-structure the
//  Council can lean with; a break of the last confirmed swing = a regime-change PULSE it can react to.
//
//  THE PUBLIC METHOD:
//    • swing high (fractal) confirms at the bar `Strength` back when its High is strictly the maximum of
//      the High over the symmetric window [ −Strength … +Strength ]; a swing low is the mirror on Low.
//    • classify vs the PRIOR confirmed swing of the same kind:
//         HH = swingHigh > prevSwingHigh   ·   LH = swingHigh < prevSwingHigh
//         HL = swingLow  > prevSwingLow    ·   LL = swingLow  < prevSwingLow
//    • Bias      = +1 up-structure (HH && HL) · −1 down-structure (LH && LL) · 0 mixed/unknown.
//    • SwingType = the LAST swing classification: +2 HH · +1 HL · −1 LH · −2 LL · 0.
//    • Bos (break-of-structure PULSE) = +1 when Close closes ABOVE the last confirmed swing-high price,
//      −1 when it closes BELOW the last confirmed swing-low price; one-shot per break (latches until the
//      OPPOSING level is taken), else 0.
//    • Signal    = Bias (the confirmed structure direction).
//
//  THE SENTINEL PLUMBING (our own code — makes it a suite member):
//    • PUBLISHES SentinelCore.StructureState (Bias / SwingType / Bos / Signal).
//    • WIRED INTO THE COUNCIL as the STRUCTURE voter (a STATE voter on StructureState.Signal).
//    • Hidden ±1 "Signal" PLOT (Values[2], transparent) for the Deck SIGNAL ARM / generic consumers.
//    • Muted confirmed-swing level lines (Values[0]/[1]) + a SentinelSkin.Painter glass card +
//      label remover + roster heartbeat.
//
//  CHANGELOG
//    v1.0.0 (2026-07-12) — NEW. Clean-room market-structure axis (self-contained fractal pivots +
//             HH/HL/LH/LL classification + break-of-structure). StructureState publish, Council STRUCTURE
//             voter, hidden Signal plot, swing-level lines, glass card, scope key + heartbeat.
// ═════════════════════════════════════════════════════════════════════════════
namespace NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors
{
	public class SentinelStructure_v1_0_0 : Indicator
	{
		private SentinelSkin.Painter _sp;
		private bool   _hasData;

		// last two confirmed swing prices (NaN until seen)
		private double _swingHigh     = double.NaN;
		private double _prevSwingHigh = double.NaN;
		private double _swingLow      = double.NaN;
		private double _prevSwingLow  = double.NaN;

		// latest classification flags
		private bool   _hh, _hl, _lh, _ll;

		// break-of-structure latch: 0 none / +1 broke high / −1 broke low
		private int    _bosLatch;

		// cached state (computed in OnBarUpdate; drawn in OnRender)
		private int    _bias;        // +1 up-structure / −1 down-structure / 0 mixed
		private int    _swingType;   // +2 HH / +1 HL / −1 LH / −2 LL / 0
		private int    _bos;         // +1/−1/0 pulse (this bar)
		private int    _sig;         // = bias
		private int    _lastLoggedSig = -999;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description              = "Clean-room market-structure axis: self-contained swing-pivot (fractal) detection → HH/HL/LH/LL classification → break-of-structure. Publishes SentinelCore.StructureState (bias / last swing type / BOS pulse / confirmed structure Signal) so the Council gains a market-structure voter.";
				Name                     = "Sentinel Structure v1.0.0";
				Calculate                = Calculate.OnBarClose;
				IsOverlay                = true;
				DisplayInDataBox         = true;
				DrawOnPricePanel         = true;
				DrawHorizontalGridLines  = false;
				DrawVerticalGridLines    = false;
				PaintPriceMarkers        = true;
				ScaleJustification       = NinjaTrader.Gui.Chart.ScaleJustification.Right;
				IsSuspendedWhileInactive = true;

				Strength = 3;

				PublishState       = true;
				LogChanges         = true;
				ShowCard           = true;
				CardCorner         = SentinelCardCorner.TopRight;
				ShowIndicatorLabel = false;

				// muted confirmed-swing level lines (step-held on the price panel)
				AddPlot(new Stroke(Brushes.MediumSeaGreen, DashStyleHelper.Dot, 1), PlotStyle.Line, "SwingHighLine");
				AddPlot(new Stroke(Brushes.IndianRed,      DashStyleHelper.Dot, 1), PlotStyle.Line, "SwingLowLine");
				// hidden ±1 confirmed-structure signal (Values[2]) — transparent; readable by the Deck SIGNAL ARM.
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
			try { SentinelCore.TouchStructureState(Scope()); } catch { }
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 0) return;
			// need the full symmetric window to confirm the bar `Strength` back.
			if (CurrentBar < 2 * Strength) return;

			// ── 1) self-contained fractal pivot detection at the bar `Strength` bars back ──
			//    candidate = High[Strength] / Low[Strength]; strictly extreme over barsAgo 0..2*Strength.
			double candHigh = High[Strength];
			double candLow  = Low[Strength];
			bool isHigh = true, isLow = true;
			for (int k = 0; k <= 2 * Strength; k++)
			{
				if (k == Strength) continue;
				if (!(candHigh > High[k])) isHigh = false;
				if (!(candLow  < Low[k]))  isLow  = false;
				if (!isHigh && !isLow) break;
			}

			// ── 2) register a confirmed swing + classify vs the prior swing of the same kind ──
			if (isHigh)
			{
				_prevSwingHigh = _swingHigh;
				_swingHigh     = candHigh;
				if (!double.IsNaN(_prevSwingHigh))
				{
					_hh = _swingHigh > _prevSwingHigh;
					_lh = _swingHigh < _prevSwingHigh;
					_swingType = _hh ? 2 : (_lh ? -1 : _swingType);
				}
			}
			if (isLow)
			{
				_prevSwingLow = _swingLow;
				_swingLow     = candLow;
				if (!double.IsNaN(_prevSwingLow))
				{
					_hl = _swingLow > _prevSwingLow;
					_ll = _swingLow < _prevSwingLow;
					_swingType = _hl ? 1 : (_ll ? -2 : _swingType);
				}
			}

			// ── 3) structural bias ──
			int bias = (_hh && _hl) ? 1 : ((_lh && _ll) ? -1 : 0);

			// ── 4) break-of-structure PULSE (one-shot per break; latch until the opposing level is taken) ──
			int bos = 0;
			if (!double.IsNaN(_swingHigh) && Close[0] > _swingHigh && _bosLatch != 1)
			{
				bos = 1; _bosLatch = 1;
			}
			else if (!double.IsNaN(_swingLow) && Close[0] < _swingLow && _bosLatch != -1)
			{
				bos = -1; _bosLatch = -1;
			}

			// ── 5) plot the confirmed swing levels (step-held) + hidden signal ──
			if (!double.IsNaN(_swingHigh)) SwingHighLine[0] = _swingHigh;
			if (!double.IsNaN(_swingLow))  SwingLowLine[0]  = _swingLow;

			int sig = bias;
			Signal[0] = sig;

			_bias = bias; _bos = bos; _sig = sig; _hasData = true;

			if (PublishState && Instrument != null && Instrument.MasterInstrument != null)
			{
				try
				{
					SentinelCore.SetStructureState(new SentinelCore.StructureState
					{
						Scope      = Scope(),
						Bartype    = SentinelCore.BarTag(BarsPeriod),
						Instrument = Instrument.MasterInstrument.Name,
						Bias       = bias,
						SwingType  = _swingType,
						Bos        = bos,
						Signal     = sig,
						Source     = "STRC"
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
					SentinelCore.Log("STRC", inst + " " +
						(sig > 0 ? "structure ▲ (HH/HL)" : sig < 0 ? "structure ▼ (LH/LL)" : "structure ~ (mixed)") +
						" swing=" + SwingLabel(_swingType) +
						(bos != 0 ? (bos > 0 ? " +BOS" : " -BOS") : ""));
				}
				catch { }
			}
		}

		private static string SwingLabel(int t)
		{
			switch (t)
			{
				case  2: return "HH";
				case  1: return "HL";
				case -1: return "LH";
				case -2: return "LL";
				default: return "—";
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
				_sp.Text("STRUCTURE", rw.Left + 16f, rw.Top, rw.Width - 20f, 16f, SentinelSkin.CInk, 11f, true);
				_sp.Text("warming up…", rw.Left, rw.Top + 26f, rw.Width, 14f, SentinelSkin.CMute, 10.5f);
				return;
			}

			var trail   = SharpDX.DirectWrite.TextAlignment.Trailing;
			bool live    = _sig != 0;
			var dirCol  = _bias > 0 ? SentinelSkin.CUp : _bias < 0 ? SentinelSkin.CDown : SentinelSkin.CMute;
			var heroCol = live ? dirCol : SentinelSkin.CMute;
			var edge    = live ? SentinelSkin.CAccent : SentinelSkin.CLine;
			var r = _sp.Card(slot.X, slot.Y, cw, ch, edge);

			_sp.Dot(r.Left + 5f, r.Top + 8f, live ? SentinelSkin.CAccent : SentinelSkin.CMute, live);
			_sp.Text("STRUCTURE", r.Left + 16f, r.Top, r.Width - 70f, 16f, SentinelSkin.CInk, 11f, true);
			_sp.Pill(_bias > 0 ? "UP" : _bias < 0 ? "DOWN" : "MIXED", r.Right, r.Top - 1f, dirCol);

			_sp.Text("MARKET STRUCTURE", r.Left, r.Top + 24f, 140f, 12f, SentinelSkin.CMute, 8.5f, true);
			_sp.Text(_bias > 0 ? "UP ▲" : _bias < 0 ? "DOWN ▼" : "mixed",
				r.Left, r.Top + 34f, r.Width, 24f, heroCol, 17f, false);

			_sp.Divider(r.Left, r.Top + 66f, r.Right);
			_sp.Text("last swing " + SwingLabel(_swingType), r.Left, r.Top + 72f, r.Width, 14f, SentinelSkin.CInk2, 10f);
			if (_bos != 0)
				_sp.Text(_bos > 0 ? "BOS ▲" : "BOS ▼", r.Left, r.Top + 72f, r.Width, 14f,
					_bos > 0 ? SentinelSkin.CUp : SentinelSkin.CDown, 10f, true, trail);
			_sp.Text("break latch " + (_bosLatch > 0 ? "high" : _bosLatch < 0 ? "low" : "none"),
				r.Left, r.Top + 90f, r.Width, 14f, SentinelSkin.CMute, 10f);
		}

		#region Properties
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Strength", Description="Bars on each side of a pivot required to confirm a swing high/low (fractal strength).", Order=1, GroupName="Parameters")]
		public int Strength { get; set; }

		// ── Sentinel ──
		[NinjaScriptProperty]
		[Display(Name="Publish Structure to Sentinel", Description="Publish the market-structure read as SentinelCore.StructureState so the Council can vote on it.", Order=20, GroupName="Sentinel")]
		public bool PublishState { get; set; }

		[NinjaScriptProperty]
		[Display(Name="Log Signal Changes", Description="Write structure-signal transitions to sentinel.log.", Order=21, GroupName="Sentinel")]
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
		[Browsable(false)] [XmlIgnore] public Series<double> SwingHighLine => Values[0];
		[Browsable(false)] [XmlIgnore] public Series<double> SwingLowLine  => Values[1];
		[Browsable(false)] [XmlIgnore] public Series<double> Signal        => Values[2];   // ±1 confirmed structure / 0
		#endregion
	}
}
