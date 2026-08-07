// ─────────────────────────────────────────────────────────────────────────────
// This Source Code Form is subject to the terms of the Mozilla Public License,
// v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
// one at https://mozilla.org/MPL/2.0/.
//
// Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
// ─────────────────────────────────────────────────────────────────────────────
#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

// ═════════════════════════════════════════════════════════════════════════════
//  Sentinel TBars Edge Probe — the PHASE-A honest-fill test        |  Version v1.0.0
//  File: SentinelTBarsEdgeProbe_v1_0_0.cs  |  namespace …Strategies (base — strategies do NOT sub-namespace)
//
//  THE ONE QUESTION THIS ANSWERS: is the ~66% first-touch win rate on TBars 6/24 a REAL,
//  TRADEABLE edge, or an artifact of the excursion recorder's bar-level label?
//
//  METHOD — deliberately DUMB + COUNCIL-FREE, so it isolates the BARS (no 21-voter confound):
//    • Signal   = the TBars brick direction (sign of Close-Open). Enter on a FLIP (default) or every brick.
//    • Barrier  = a SYMMETRIC R target/stop, R = max(MinTicks, AtrMult × ATR(AtrPeriod)) in ticks —
//                 this MIRRORS the recorder's ATR-scaled first-touch barrier, so a win here == firstTouch=+1 there.
//    • One position at a time (act only when flat) → each trade is an independent first-touch barrier test.
//    • Managed SetProfitTarget/SetStopLoss so NT's own fill engine resolves the barrier.
//
//  ⚠ HOW TO RUN IT HONESTLY (the whole point): Strategy Analyzer ▸ your GC SentinelTBars 6/24 series ▸
//    **Order Fill Resolution = High, 1 Tick** (NOT the default bar-close fill — that would just reproduce the
//    optimistic bar-level label). Set Slippage to your real cost. Compare the win rate + expectancy to the
//    corpus 66%. If it SURVIVES → the bars carry the edge (advance to Phase B: variants + session overlay).
//    If it CRATERS (à la CompressionBase 81%→37.5%) → the 66% was label optimism, and we've saved months.
//
//  NO Sentinel plumbing on purpose: no SentinelCore, no card, no seam — a clean, independent instrument.
//
//  CHANGELOG
//    v1.0.0 (2026-07-13) — NEW. Phase-A honest-fill probe of the TBars edge.
// ═════════════════════════════════════════════════════════════════════════════
namespace NinjaTrader.NinjaScript.Strategies
{
	public class SentinelTBarsEdgeProbe_v1_0_0 : Strategy
	{
		private ATR atr;
		private int _lastBrickDir;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Name                = "Sentinel TBars Edge Probe";
				Description         = "Phase-A honest-fill test: enter in the TBars brick direction (flip), symmetric ATR-scaled R target/stop, Council-free. Run in the Strategy Analyzer at Order Fill Resolution = High/1-tick to see whether the ~66% first-touch survives honest fills. Isolates the BAR TYPE as the edge source.";
				Calculate           = Calculate.OnBarClose;
				EntriesPerDirection = 1;
				EntryHandling       = EntryHandling.AllEntries;
				IsUnmanaged         = false;
				BarsRequiredToTrade = 25;
				DefaultQuantity     = 1;
				IncludeCommission   = true;   // grade net of commission in the Analyzer

				AtrPeriod       = 14;
				AtrMult         = 1.0;
				MinTicks        = 20;
				EntryOnFlipOnly = true;
			}
			else if (State == State.DataLoaded)
			{
				atr = ATR(AtrPeriod);
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < BarsRequiredToTrade) return;

			// brick direction from the completed bar (HA/Renko-style bricks color by Close vs Open)
			int brickDir = Close[0] > Open[0] ? 1 : Close[0] < Open[0] ? -1 : _lastBrickDir;
			bool flip    = brickDir != 0 && brickDir != _lastBrickDir;
			_lastBrickDir = brickDir;

			// one trade at a time → each entry is an independent first-touch barrier test
			if (Position.MarketPosition != MarketPosition.Flat) return;

			bool signal = EntryOnFlipOnly ? flip : (brickDir != 0);
			if (!signal) return;

			// R = the recorder's ATR-scaled barrier, symmetric target/stop
			int r = (int)Math.Max(MinTicks, Math.Round(AtrMult * atr[0] / TickSize));
			SetProfitTarget(CalculationMode.Ticks, r);
			SetStopLoss(CalculationMode.Ticks, r);

			if (brickDir > 0) EnterLong(DefaultQuantity, "probeL");
			else              EnterShort(DefaultQuantity, "probeS");
		}

		#region Properties
		[NinjaScriptProperty]
		[Range(2, int.MaxValue)]
		[Display(Name = "ATR Period", Description = "ATR period for the R barrier (mirrors the recorder's ATR(14)).", Order = 1, GroupName = "Barrier")]
		public int AtrPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(0.1, double.MaxValue)]
		[Display(Name = "ATR Mult (R)", Description = "Symmetric target/stop = this × ATR, in ticks.", Order = 2, GroupName = "Barrier")]
		public double AtrMult { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Min R Ticks", Description = "Floor for R so the barrier stays above the noise (recorder floors at 20t on gold).", Order = 3, GroupName = "Barrier")]
		public int MinTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Entry on brick flip only", Description = "ON = enter only when the brick direction flips (a 'signal'); OFF = enter in the brick direction whenever flat.", Order = 4, GroupName = "Signal")]
		public bool EntryOnFlipOnly { get; set; }
		#endregion
	}
}
