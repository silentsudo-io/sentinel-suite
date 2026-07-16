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
using NinjaTrader.NinjaScript.AddOns.Sentinel;   // SentinelSkin (glass card) + SentinelCore (RegimeState seam) + SentinelCardCorner
using NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors;
#endregion

// ═════════════════════════════════════════════════════════════════════════════
//  Sentinel Regime — the VOLATILITY-REGIME modulator (CLEAN-ROOM)            |   Version v1.0.0
//  File: SentinelRegime_v1_0_0.cs   |   namespace …Indicators.Sentinel.Sensors (Tier-③ SENSOR)   |   Name "Sentinel Regime"
//
//  ⚠ NO ORDERS — read-only advisory indicator. Safe to run anywhere.
//
//  CLEAN-ROOM ORIGINAL. Written from scratch off PUBLIC, non-copyrightable statistics — 1-D K-means
//  clustering of rolling return-volatility into three regimes, and a first-order Markov forward filter
//  over the cluster posterior. It uses NO third-party code. The installed MarkovRegimeSwitching.cs was
//  surveyed as a design reference ONLY — none of its code was copied. See the provenance audit + NOTICE.
//
//  WHY IT MATTERS — this is NOT a directional voter; it is a CONTEXT MODULATOR. It answers "what kind of
//  market is this right now — calm, normal, or chaotic?" so the Council can DAMPEN conviction in a
//  high-volatility (regime 2) tape and let orderly low/med-vol (regime 0/1) trends run.
//
//  THE PUBLIC METHOD:
//    • volatility     = stddev of the last VolWindow log-returns (r = ln(Close[0]/Close[1])).
//    • sample buffer  = the last SampleWindow volatility values.
//    • K-means (k=3)  = a few Lloyd iterations over that buffer, centers init at min/median/max; the 3
//                       centers are then SORTED ASCENDING (0=low, 1=med, 2=high) — label-stabilization is
//                       REQUIRED, else the cluster labels permute between recomputes. K-means is refit only
//                       every RecomputeEvery bars for cost; the sorted centers are cached between refits.
//    • transitions    = a 3×3 count of consecutive raw-regime labels across the buffer, Laplace-smoothed
//                       (+1) and row-normalized → the Markov transition matrix T.
//    • Markov filter  = belief b=[pLow,pMed,pHigh]; each bar predict b'=b·T, multiply by a Gaussian
//                       emission likelihood of the current vol under each (center, spread), then normalize.
//    • Regime         = argmax(b'); RegimeProb = max(b'); Trending = (Regime ≤ 1).
//
//  THE SENTINEL PLUMBING (our own code — makes it a suite member):
//    • PUBLISHES SentinelCore.RegimeState (Regime / RegimeProb / Low·Med·HighProb / Trending).
//    • Consumed by the Council as a CONTEXT MODULATOR (not a directional voter → no hidden Signal plot).
//    • CARD-ONLY readout: both plots are hidden (transparent). A 0..1 modulator plot cannot coexist on a chart
//      panel shared with a big-range series (Flow's ±2000 CVD), so the glass card is the sole readout.
//    • A SentinelSkin.Painter glass card + label remover + roster heartbeat.
//
//  CHANGELOG
//    v1.0.0 (2026-07-13b) — CARD-ONLY. The visible panel plots collided with Flow's CVD when the workspace put both
//             on one shared panel (Regime 0..1 collapsed to a flat row). Both plots hidden (transparent); the card is
//             the readout. Values[]/DataBox + RegimeState seam unchanged.
//    v1.0.0 (2026-07-13a) — plot attempt: normalized Regime to 0/0.5/1 (regime/2) + Dot markers. Superseded same day
//             once the live shared-panel collision with Flow made any visible 0..1 plot unviewable.
//    v1.0.0 (2026-07-12) — NEW. Clean-room volatility-regime modulator (rolling-vol K-means + Markov
//             forward filter). RegimeState publish, two visible plots, glass card, scope key + heartbeat.
// ═════════════════════════════════════════════════════════════════════════════
namespace NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors
{
	public class SentinelRegime_v1_0_0 : Indicator
	{
		private SentinelSkin.Painter _sp;
		private bool   _hasData;

		// rolling buffers
		private readonly List<double> _returns = new List<double>();   // last VolWindow log-returns
		private readonly List<double> _vols    = new List<double>();   // last SampleWindow volatilities

		// cached K-means fit (recomputed every RecomputeEvery bars, sorted ascending)
		private double[] _centers;                  // [low, med, high] sorted
		private double[] _spreads = new double[3];  // per-cluster stddev (floored)
		private double[,] _trans;                   // 3×3 row-normalized transition matrix
		private int    _barsSinceRecompute;

		// Markov belief (posterior over the 3 regimes)
		private double[] _belief = new double[] { 1.0 / 3.0, 1.0 / 3.0, 1.0 / 3.0 };

		// cached state (computed in OnBarUpdate; drawn in OnRender)
		private int    _regime;
		private double _regimeProb;
		private double _pLow, _pMed, _pHigh;
		private bool   _trending;
		private int    _lastLoggedRegime = -999;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description              = "Clean-room volatility-regime modulator: 1-D K-means of rolling return-volatility into 3 regimes (low/med/high) with a Markov forward filter over the posterior. Publishes SentinelCore.RegimeState so the Council can DAMPEN conviction in chaotic high-vol tape and let orderly trends run. Non-directional context, not a voter.";
				Name                     = "Sentinel Regime v1.0.0";
				Calculate                = Calculate.OnBarClose;
				IsOverlay                = false;
				DisplayInDataBox         = true;
				DrawOnPricePanel         = false;
				DrawHorizontalGridLines  = true;
				DrawVerticalGridLines    = false;
				PaintPriceMarkers        = true;
				ScaleJustification       = NinjaTrader.Gui.Chart.ScaleJustification.Right;
				IsSuspendedWhileInactive = true;

				VolWindow     = 20;
				SampleWindow  = 200;
				RecomputeEvery= 20;

				PublishState       = true;
				LogChanges         = true;
				ShowCard           = true;
				CardCorner         = SentinelCardCorner.TopRight;
				ShowIndicatorLabel = false;

				// CARD-ONLY: both plots hidden. Regime is a 0..1 CONTEXT MODULATOR — as a visible plot it collided with
				// whatever big-range series shared its panel (e.g. Flow's ±2000 CVD), collapsing to a flat row. The glass
				// card is the readout; the values still populate Values[]/DataBox and the RegimeState seam.
				AddPlot(new Stroke(Brushes.Transparent, 3), PlotStyle.Dot,  "Regime");       // 0/0.5/1 (regime/2) — hidden
				AddPlot(new Stroke(Brushes.Transparent, 1), PlotStyle.Line, "RegimeProb");   // 0..1 — hidden
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

		// ── HEARTBEAT — re-stamp the cached seam on incoming quotes so a healthy modulator doesn't age out of the
		//    Council roster in a quiet market. No recompute, realtime only, throttled. ──
		private DateTime _lastHeartbeatUtc;
		private const double HeartbeatSec = 5.0;
		protected override void OnMarketData(NinjaTrader.Data.MarketDataEventArgs marketDataUpdate)
		{
			if (!PublishState || State != State.Realtime) return;
			DateTime now = DateTime.UtcNow;
			if ((now - _lastHeartbeatUtc).TotalSeconds < HeartbeatSec) return;
			_lastHeartbeatUtc = now;
			try { SentinelCore.TouchRegimeState(Scope()); } catch { }
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 0) return;
			if (CurrentBar < 1) return;

			// 1) log-return + rolling volatility (stddev of the last VolWindow returns)
			double prev = Close[1];
			if (prev <= 0) return;
			double r = Math.Log(Close[0] / prev);
			_returns.Add(r);
			int rw = Math.Max(2, VolWindow);
			if (_returns.Count > rw) _returns.RemoveAt(0);
			if (_returns.Count < rw) return;

			double vol = Std(_returns);

			// 2) rolling volatility sample buffer
			_vols.Add(vol);
			int sw = Math.Max(6, SampleWindow);
			if (_vols.Count > sw) _vols.RemoveAt(0);
			if (_vols.Count < 6) return;   // need a handful of samples before clustering is meaningful

			// 3) K-means refit (throttled) → sorted centers, spreads, transition matrix
			if (_centers == null || _barsSinceRecompute >= Math.Max(1, RecomputeEvery))
			{
				RecomputeKMeans();
				_barsSinceRecompute = 0;
			}
			else _barsSinceRecompute++;

			if (_centers == null || _trans == null) return;

			// 4) Markov forward filter: predict b' = b · T, apply Gaussian emission, normalize.
			double[] pred = new double[3];
			for (int j = 0; j < 3; j++)
			{
				double s = 0;
				for (int i = 0; i < 3; i++) s += _belief[i] * _trans[i, j];
				pred[j] = s;
			}
			for (int j = 0; j < 3; j++)
				pred[j] *= GaussianLikelihood(vol, _centers[j], _spreads[j]);

			double sum = pred[0] + pred[1] + pred[2];
			if (sum < 1e-300 || double.IsNaN(sum) || double.IsInfinity(sum))
			{
				// emission collapsed (vol far from every center) → fall back to a hard vote at the nearest center
				int nearest = NearestCenter(vol);
				pred[0] = pred[1] = pred[2] = 0;
				pred[nearest] = 1;
				sum = 1;
			}
			for (int j = 0; j < 3; j++) _belief[j] = pred[j] / sum;

			// 5) argmax → regime; max → probability
			int regime = 0;
			double best = _belief[0];
			if (_belief[1] > best) { best = _belief[1]; regime = 1; }
			if (_belief[2] > best) { best = _belief[2]; regime = 2; }

			_regime     = regime;
			_regimeProb = best;
			_pLow  = _belief[0];
			_pMed  = _belief[1];
			_pHigh = _belief[2];
			_trending = regime <= 1;   // orderly low/med vol favors trend continuation; high vol = chaotic
			_hasData  = true;

			Regime[0]     = regime / 2.0;   // normalize 0/1/2 → 0/0.5/1 so it shares RegimeProb's 0..1 scale (seam still carries the int)
			RegimeProb[0] = best;

			if (PublishState && Instrument != null && Instrument.MasterInstrument != null)
			{
				try
				{
					SentinelCore.SetRegimeState(new SentinelCore.RegimeState
					{
						Scope      = Scope(),
						Bartype    = SentinelCore.BarTag(BarsPeriod),
						Instrument = Instrument.MasterInstrument.Name,
						Regime     = regime,
						RegimeProb = best,
						LowProb    = _pLow,
						MedProb    = _pMed,
						HighProb   = _pHigh,
						Trending   = _trending,
						Source     = "REGIME"
					});
				}
				catch { }
			}

			if (LogChanges && regime != _lastLoggedRegime)
			{
				_lastLoggedRegime = regime;
				try
				{
					string inst = Instrument != null && Instrument.MasterInstrument != null ? Instrument.MasterInstrument.Name : "?";
					SentinelCore.Log("REGIME", inst + " " +
						(regime == 0 ? "LOW vol (orderly)" : regime == 1 ? "MED vol (normal)" : "HIGH vol (chaotic)") +
						" p=" + best.ToString("0.00") + (_trending ? " trending" : " choppy"));
				}
				catch { }
			}
		}

		// ── 1-D K-means (k=3) over the volatility buffer → sorted centers, per-cluster spreads, transition matrix ──
		private void RecomputeKMeans()
		{
			int n = _vols.Count;
			if (n < 6) return;

			// init centers at min / median / max of the buffer
			var sorted = new List<double>(_vols);
			sorted.Sort();
			double[] c = new double[3];
			c[0] = sorted[0];
			c[1] = sorted[n / 2];
			c[2] = sorted[n - 1];

			// Lloyd iterations
			for (int iter = 0; iter < 12; iter++)
			{
				double[] sums = new double[3];
				int[]    cnt  = new int[3];
				for (int i = 0; i < n; i++)
				{
					int k = Nearest(c, _vols[i]);
					sums[k] += _vols[i];
					cnt[k]++;
				}
				bool moved = false;
				for (int k = 0; k < 3; k++)
				{
					if (cnt[k] > 0)
					{
						double nc = sums[k] / cnt[k];
						if (Math.Abs(nc - c[k]) > 1e-12) moved = true;
						c[k] = nc;
					}
				}
				if (!moved) break;
			}

			// SORT centers ascending → stable labels (0=low,1=med,2=high). REQUIRED.
			Array.Sort(c);
			_centers = c;

			// global scale to floor degenerate cluster spreads
			double gScale = Std(_vols);
			double floor  = Math.Max(1e-12, gScale * 0.15);

			// assign every buffer member to the sorted centers → per-cluster spreads + raw label sequence
			int[] labels = new int[n];
			double[] mSum = new double[3];
			int[]    mCnt = new int[3];
			for (int i = 0; i < n; i++)
			{
				int k = Nearest(c, _vols[i]);
				labels[i] = k;
				mSum[k] += _vols[i];
				mCnt[k]++;
			}
			double[] mMean = new double[3];
			for (int k = 0; k < 3; k++) mMean[k] = mCnt[k] > 0 ? mSum[k] / mCnt[k] : c[k];
			double[] mSq = new double[3];
			for (int i = 0; i < n; i++)
			{
				int k = labels[i];
				double d = _vols[i] - mMean[k];
				mSq[k] += d * d;
			}
			for (int k = 0; k < 3; k++)
			{
				double sp = mCnt[k] > 1 ? Math.Sqrt(mSq[k] / (mCnt[k] - 1)) : 0;
				_spreads[k] = Math.Max(sp, floor);
			}

			// transition matrix: count consecutive raw-label transitions, Laplace +1, row-normalize
			double[,] cntT = new double[3, 3];
			for (int i = 1; i < n; i++) cntT[labels[i - 1], labels[i]] += 1;
			double[,] T = new double[3, 3];
			for (int i = 0; i < 3; i++)
			{
				double rowSum = cntT[i, 0] + cntT[i, 1] + cntT[i, 2] + 3.0;   // +3 for the +1 Laplace on each cell
				for (int j = 0; j < 3; j++) T[i, j] = (cntT[i, j] + 1.0) / rowSum;
			}
			_trans = T;
		}

		// nearest of 3 sorted centers (used inside the fit)
		private static int Nearest(double[] c, double v)
		{
			double d0 = Math.Abs(v - c[0]);
			double d1 = Math.Abs(v - c[1]);
			double d2 = Math.Abs(v - c[2]);
			int k = 0; double best = d0;
			if (d1 < best) { best = d1; k = 1; }
			if (d2 < best) { best = d2; k = 2; }
			return k;
		}

		private int NearestCenter(double v) => _centers != null ? Nearest(_centers, v) : 0;

		// unnormalized Gaussian emission likelihood of vol under cluster (mean, spread)
		private static double GaussianLikelihood(double v, double mean, double spread)
		{
			double s = spread > 1e-12 ? spread : 1e-12;
			double z = (v - mean) / s;
			return Math.Exp(-0.5 * z * z) / s;
		}

		// sample standard deviation of a list
		private static double Std(List<double> xs)
		{
			int n = xs.Count;
			if (n < 2) return 0;
			double mean = 0;
			for (int i = 0; i < n; i++) mean += xs[i];
			mean /= n;
			double sq = 0;
			for (int i = 0; i < n; i++) { double d = xs[i] - mean; sq += d * d; }
			return Math.Sqrt(sq / (n - 1));
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
				_sp.Text("REGIME", rw.Left + 16f, rw.Top, rw.Width - 20f, 16f, SentinelSkin.CInk, 11f, true);
				_sp.Text("warming up…", rw.Left, rw.Top + 26f, rw.Width, 14f, SentinelSkin.CMute, 10.5f);
				return;
			}

			var trail    = SharpDX.DirectWrite.TextAlignment.Trailing;
			// non-directional: low = calm (up-tone), med = neutral accent, high = chaotic (warn/down-tone)
			var regCol   = _regime == 0 ? SentinelSkin.CUp : _regime == 1 ? SentinelSkin.CAccent : SentinelSkin.CWarn;
			var edge     = _regime == 2 ? SentinelSkin.CWarn : SentinelSkin.CLine;
			string label = _regime == 0 ? "LOW VOL" : _regime == 1 ? "MED VOL" : "HIGH VOL";
			var r = _sp.Card(slot.X, slot.Y, cw, ch, edge);

			_sp.Dot(r.Left + 5f, r.Top + 8f, regCol, _regime != 2);
			_sp.Text("REGIME", r.Left + 16f, r.Top, r.Width - 78f, 16f, SentinelSkin.CInk, 11f, true);
			_sp.Pill(_trending ? "TRENDING" : "CHOPPY", r.Right, r.Top - 1f, _trending ? SentinelSkin.CUp : SentinelSkin.CMute);

			_sp.Text("VOLATILITY", r.Left, r.Top + 24f, 120f, 12f, SentinelSkin.CMute, 8.5f, true);
			_sp.Text(label, r.Left, r.Top + 34f, r.Width, 24f, regCol, 17f, false);

			_sp.Divider(r.Left, r.Top + 66f, r.Right);
			_sp.Text("prob " + _regimeProb.ToString("0.00"), r.Left, r.Top + 72f, r.Width, 14f, SentinelSkin.CInk2, 10f);
			_sp.Text("L " + _pLow.ToString("0.00") + "  M " + _pMed.ToString("0.00") + "  H " + _pHigh.ToString("0.00"),
				r.Left, r.Top + 72f, r.Width, 14f, SentinelSkin.CInk2, 9f, true, trail);
			_sp.Text(_trending ? "orderly — let trends run" : "chaotic — dampen conviction",
				r.Left, r.Top + 90f, r.Width, 14f, SentinelSkin.CMute, 10f);
		}

		#region Properties
		[NinjaScriptProperty]
		[Range(2, int.MaxValue)]
		[Display(Name="Vol Window", Description="Number of log-returns in the rolling volatility (stddev) window.", Order=1, GroupName="Parameters")]
		public int VolWindow { get; set; }

		[NinjaScriptProperty]
		[Range(6, int.MaxValue)]
		[Display(Name="Sample Window", Description="Number of recent volatility values clustered by K-means.", Order=2, GroupName="Parameters")]
		public int SampleWindow { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Recompute Every", Description="Refit K-means (and the transition matrix) every N bars; sorted centers are cached between refits.", Order=3, GroupName="Parameters")]
		public int RecomputeEvery { get; set; }

		// ── Sentinel ──
		[NinjaScriptProperty]
		[Display(Name="Publish Regime to Sentinel", Description="Publish the volatility regime as SentinelCore.RegimeState so the Council can modulate on it.", Order=20, GroupName="Sentinel")]
		public bool PublishState { get; set; }

		[NinjaScriptProperty]
		[Display(Name="Log Regime Changes", Description="Write regime transitions to sentinel.log.", Order=21, GroupName="Sentinel")]
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
		[Browsable(false)] [XmlIgnore] public Series<double> Regime     => Values[0];   // 0/0.5/1 (regime/2) for display; the integer regime lives on RegimeState.Regime
		[Browsable(false)] [XmlIgnore] public Series<double> RegimeProb => Values[1];   // 0..1 posterior of the winning regime
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private Sentinel.Sensors.SentinelRegime_v1_0_0[] cacheSentinelRegime_v1_0_0;
		public Sentinel.Sensors.SentinelRegime_v1_0_0 SentinelRegime_v1_0_0(int volWindow, int sampleWindow, int recomputeEvery, bool publishState, bool logChanges, bool showCard, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return SentinelRegime_v1_0_0(Input, volWindow, sampleWindow, recomputeEvery, publishState, logChanges, showCard, cardCorner, showIndicatorLabel);
		}

		public Sentinel.Sensors.SentinelRegime_v1_0_0 SentinelRegime_v1_0_0(ISeries<double> input, int volWindow, int sampleWindow, int recomputeEvery, bool publishState, bool logChanges, bool showCard, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			if (cacheSentinelRegime_v1_0_0 != null)
				for (int idx = 0; idx < cacheSentinelRegime_v1_0_0.Length; idx++)
					if (cacheSentinelRegime_v1_0_0[idx] != null && cacheSentinelRegime_v1_0_0[idx].VolWindow == volWindow && cacheSentinelRegime_v1_0_0[idx].SampleWindow == sampleWindow && cacheSentinelRegime_v1_0_0[idx].RecomputeEvery == recomputeEvery && cacheSentinelRegime_v1_0_0[idx].PublishState == publishState && cacheSentinelRegime_v1_0_0[idx].LogChanges == logChanges && cacheSentinelRegime_v1_0_0[idx].ShowCard == showCard && cacheSentinelRegime_v1_0_0[idx].CardCorner == cardCorner && cacheSentinelRegime_v1_0_0[idx].ShowIndicatorLabel == showIndicatorLabel && cacheSentinelRegime_v1_0_0[idx].EqualsInput(input))
						return cacheSentinelRegime_v1_0_0[idx];
			return CacheIndicator<Sentinel.Sensors.SentinelRegime_v1_0_0>(new Sentinel.Sensors.SentinelRegime_v1_0_0(){ VolWindow = volWindow, SampleWindow = sampleWindow, RecomputeEvery = recomputeEvery, PublishState = publishState, LogChanges = logChanges, ShowCard = showCard, CardCorner = cardCorner, ShowIndicatorLabel = showIndicatorLabel }, input, ref cacheSentinelRegime_v1_0_0);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.Sentinel.Sensors.SentinelRegime_v1_0_0 SentinelRegime_v1_0_0(int volWindow, int sampleWindow, int recomputeEvery, bool publishState, bool logChanges, bool showCard, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return indicator.SentinelRegime_v1_0_0(Input, volWindow, sampleWindow, recomputeEvery, publishState, logChanges, showCard, cardCorner, showIndicatorLabel);
		}

		public Indicators.Sentinel.Sensors.SentinelRegime_v1_0_0 SentinelRegime_v1_0_0(ISeries<double> input , int volWindow, int sampleWindow, int recomputeEvery, bool publishState, bool logChanges, bool showCard, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return indicator.SentinelRegime_v1_0_0(input, volWindow, sampleWindow, recomputeEvery, publishState, logChanges, showCard, cardCorner, showIndicatorLabel);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.Sentinel.Sensors.SentinelRegime_v1_0_0 SentinelRegime_v1_0_0(int volWindow, int sampleWindow, int recomputeEvery, bool publishState, bool logChanges, bool showCard, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return indicator.SentinelRegime_v1_0_0(Input, volWindow, sampleWindow, recomputeEvery, publishState, logChanges, showCard, cardCorner, showIndicatorLabel);
		}

		public Indicators.Sentinel.Sensors.SentinelRegime_v1_0_0 SentinelRegime_v1_0_0(ISeries<double> input , int volWindow, int sampleWindow, int recomputeEvery, bool publishState, bool logChanges, bool showCard, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return indicator.SentinelRegime_v1_0_0(input, volWindow, sampleWindow, recomputeEvery, publishState, logChanges, showCard, cardCorner, showIndicatorLabel);
		}
	}
}

#endregion
