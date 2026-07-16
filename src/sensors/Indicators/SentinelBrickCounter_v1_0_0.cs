// ─────────────────────────────────────────────────────────────────────────────
// This Source Code Form is subject to the terms of the Mozilla Public License,
// v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
// one at https://mozilla.org/MPL/2.0/.
//
// Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
// ─────────────────────────────────────────────────────────────────────────────
// ═════════════════════════════════════════════════════════════════════════════
//  SentinelBrickCounter — "ticks to the next brick" glass HUD (Sentinel Suite)
//  File: SentinelBrickCounter_v1_0_0.cs   Class: SentinelBrickCounter_v1_0_0
// ─────────────────────────────────────────────────────────────────────────────
//  WHAT THIS IS
//    A generic on-chart countdown that reads SentinelCore.BrickState (v1.6.1) and
//    shows how many ticks remain until the next brick prints. NOT tied to any one
//    bars type — ANY Sentinel brick bars type that publishes BrickState feeds it
//    (SentinelTBars, SentinelTbarsCount, …), even from a DIFFERENT chart of the same
//    instrument. Supersedes TbarsCountRemainingCounter + its private feed.
//    v1.19.0: BrickState is keyed by SCOPE (a chart = instrument × bartype), so this reads
//    THIS chart's brick state first and falls back to a bare-instrument lookup — the fallback
//    is what lets it sit on a minute chart and count a brick type running elsewhere. That
//    lookup resolves only when exactly ONE brick scope exists for the instrument; with two it
//    fails closed rather than showing an arbitrary chart's countdown.
//
//  SENTINEL STYLE
//    Drawn as a SentinelSkin.Painter GLASS CARD, auto-docked via SentinelSkin.CardLayout
//    so it stacks with the other Sentinel cards (never overlaps). cyan = live/watching;
//    the hero number + direction pill are green/red (direction). Name label hidden by
//    default (label-remover standard). See Docs/SENTINEL_DESIGN_SYSTEM.md §4b.
//
//  CHANGELOG
//    v1.0.0 (2026-07-06) — first release; reads SentinelCore.BrickState. Glass-card
//      readout (Painter + CardLayout) replacing the initial Draw.TextFixed corner text.
// ═════════════════════════════════════════════════════════════════════════════

#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.AddOns.Sentinel;   // SentinelCore.BrickState + SentinelSkin (Painter/CardLayout/SentinelCardCorner)
using NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors
{
    public class SentinelBrickCounter_v1_0_0 : Indicator
    {
        private SentinelSkin.Painter _sp;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                     = "Sentinel Brick Counter v1.0.0";
                Description              = "Ticks to the next brick — reads SentinelCore.BrickState (any Sentinel brick bars type).";
                Calculate                = Calculate.OnEachTick;
                IsOverlay                = true;
                DisplayInDataBox         = false;
                DrawOnPricePanel         = true;
                PaintPriceMarkers        = false;
                IsSuspendedWhileInactive = false;

                CardCorner         = SentinelCardCorner.TopRight;
                MaxAgeSeconds      = 10;
                ShowUpDown         = true;
                ShowTriggerPrices  = false;
                ShowSource         = true;
                ShowIndicatorLabel = false;
            }
            else if (State == State.DataLoaded)
            {
                // Sentinel label-remover: hide NT's on-chart name label unless explicitly enabled.
                if (!ShowIndicatorLabel) Name = string.Empty;
            }
            else if (State == State.Terminated)
            {
                if (_sp != null) { try { _sp.Dispose(); } catch { } _sp = null; }
                try { SentinelSkin.CardLayout.Release(this); } catch { }
            }
        }

        protected override void OnBarUpdate()
        {
            // No per-bar logic — the card reads shared SentinelCore.BrickState in OnRender.
        }

        // The Sentinel "flight-instrument" glass card — docks via CardLayout so it never covers another card.
        protected override void OnRender(NinjaTrader.Gui.Chart.ChartControl chartControl, NinjaTrader.Gui.Chart.ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);
            if (RenderTarget == null || ChartPanel == null) return;
            try
            {
                if (_sp == null) _sp = new SentinelSkin.Painter();
                _sp.Begin(RenderTarget);

                // v1.19.0 (BrickState is scope-keyed now): prefer THIS chart's own brick state, then fall back to a
                // bare-instrument lookup. The fallback is deliberate and is this tool's whole point — the counter is
                // designed to be hung on ANY chart of the instrument (a minute chart, say) and read a brick bars type
                // running elsewhere. A strict scope lookup would show nothing there. The bare lookup resolves only
                // when exactly ONE brick scope exists for the instrument, and otherwise fails closed (null → the
                // "waiting for a Sentinel brick bars type…" card), which is the honest answer to "which chart?".
                string inst = Instrument != null && Instrument.MasterInstrument != null ? Instrument.MasterInstrument.Name : null;
                string skey = null;
                try { skey = SentinelCore.ScopeOf(Instrument, BarsPeriod); } catch { }

                SentinelCore.BrickState st = null;
                if (!string.IsNullOrEmpty(skey)) st = SentinelCore.GetBrickState(skey, MaxAgeSeconds);
                if (st == null && !string.IsNullOrEmpty(inst)) st = SentinelCore.GetBrickState(inst, MaxAgeSeconds);

                const float cw = 232f;
                var lead = SharpDX.DirectWrite.TextAlignment.Leading;

                if (st == null)
                {
                    const float chW = 62f;
                    var sw = SentinelSkin.CardLayout.Place(this, ChartPanel,
                        ChartPanel.X, ChartPanel.Y, ChartPanel.W, ChartPanel.H, CardCorner, cw, chW);
                    var rw = _sp.Card(sw.X, sw.Y, cw, chW, SentinelSkin.CDim);
                    _sp.Dot(rw.Left + 5f, rw.Top + 8f, SentinelSkin.CMute, false);
                    _sp.Text("BRICK COUNTER", rw.Left + 16f, rw.Top, rw.Width - 20f, 16f, SentinelSkin.CInk, 11f, true);
                    _sp.Text("waiting for a Sentinel brick bars type…", rw.Left, rw.Top + 22f, rw.Width, 14f, SentinelSkin.CMute, 9.5f);
                    _sp.End();
                    return;
                }

                int nearest = CeilTicks(st.NearestTicksRemaining);
                int up      = CeilTicks(st.TicksToUpper);
                int down    = CeilTicks(st.TicksToLower);
                int dir     = st.Direction;
                var dirCol  = dir > 0 ? SentinelSkin.CUp : dir < 0 ? SentinelSkin.CDown : SentinelSkin.CAccent;

                int rows = (ShowUpDown ? 1 : 0) + (ShowTriggerPrices ? 1 : 0) + (ShowSource ? 1 : 0);
                float ch = 96f + rows * 15f;

                var slot = SentinelSkin.CardLayout.Place(this, ChartPanel,
                    ChartPanel.X, ChartPanel.Y, ChartPanel.W, ChartPanel.H, CardCorner, cw, ch);
                var r = _sp.Card(slot.X, slot.Y, cw, ch, SentinelSkin.CAccent);   // live → cyan edge

                // header: live dot + title + direction pill
                _sp.Dot(r.Left + 5f, r.Top + 8f, SentinelSkin.CAccent, true);
                _sp.Text("BRICK COUNTER", r.Left + 16f, r.Top, r.Width - 74f, 16f, SentinelSkin.CInk, 11f, true);
                string pill = dir > 0 ? "▲ UP" : dir < 0 ? "▼ DN" : "FLAT";
                _sp.Pill(pill, r.Right, r.Top - 1f, dirCol);

                // hero: ticks to the next brick
                _sp.Text("TO NEXT BRICK", r.Left, r.Top + 24f, 140f, 12f, SentinelSkin.CMute, 9f, true);
                _sp.Text(nearest.ToString(), r.Left, r.Top + 33f, 90f, 26f, dirCol, 22f);
                _sp.Text(nearest == 1 ? "tick" : "ticks", r.Left + 42f, r.Top + 43f, 90f, 14f, SentinelSkin.CMute, 10f);

                // proximity track: centered = 0, at a boundary = 1 (about to fire)
                float half = (up + down) * 0.5f;
                float frac = half > 0.001f ? 1f - nearest / half : 0f;
                _sp.Track(r.Left, r.Top + 62f, r.Width, frac, SentinelSkin.CAccent, 5f);

                // stat rows (mono)
                float ry = r.Top + 74f;
                if (ShowUpDown)
                {
                    _sp.Text("up " + up + "     dn " + down, r.Left, ry, r.Width, 14f, SentinelSkin.CInk2, 10.5f, false, lead, true);
                    ry += 14f;
                }
                if (ShowTriggerPrices && Instrument != null && Instrument.MasterInstrument != null)
                {
                    _sp.Text(Instrument.MasterInstrument.FormatPrice(st.UpperPrice) + "   /   " + Instrument.MasterInstrument.FormatPrice(st.LowerPrice),
                        r.Left, ry, r.Width, 14f, SentinelSkin.CMute, 10f, false, lead, true);
                    ry += 14f;
                }
                if (ShowSource && !string.IsNullOrEmpty(st.Source))
                {
                    _sp.Text(st.Source + "   run " + st.SameDirCount + "   " + st.BarsThisSession + " bricks",
                        r.Left, ry, r.Width, 14f, SentinelSkin.CMute, 9.5f, false, lead, true);
                    ry += 14f;
                }

                _sp.End();
            }
            catch { }
        }

        private static int CeilTicks(double value)
        {
            if (value <= 0) return 0;
            return (int)Math.Ceiling(value - 0.0000001);
        }

        #region Properties
        [NinjaScriptProperty]
        [Display(Name = "Card corner", GroupName = "Layout", Order = 0)]
        public SentinelCardCorner CardCorner { get; set; }

        [NinjaScriptProperty]
        [Range(1, 120)]
        [Display(Name = "Max Age (seconds)", GroupName = "Data", Order = 0)]
        public int MaxAgeSeconds { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Up/Down", GroupName = "Content", Order = 0)]
        public bool ShowUpDown { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Trigger Prices", GroupName = "Content", Order = 1)]
        public bool ShowTriggerPrices { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Source", GroupName = "Content", Order = 2)]
        public bool ShowSource { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Indicator Label", GroupName = "Content", Order = 3)]
        public bool ShowIndicatorLabel { get; set; }
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private Sentinel.Sensors.SentinelBrickCounter_v1_0_0[] cacheSentinelBrickCounter_v1_0_0;
		public Sentinel.Sensors.SentinelBrickCounter_v1_0_0 SentinelBrickCounter_v1_0_0(SentinelCardCorner cardCorner, int maxAgeSeconds, bool showUpDown, bool showTriggerPrices, bool showSource, bool showIndicatorLabel)
		{
			return SentinelBrickCounter_v1_0_0(Input, cardCorner, maxAgeSeconds, showUpDown, showTriggerPrices, showSource, showIndicatorLabel);
		}

		public Sentinel.Sensors.SentinelBrickCounter_v1_0_0 SentinelBrickCounter_v1_0_0(ISeries<double> input, SentinelCardCorner cardCorner, int maxAgeSeconds, bool showUpDown, bool showTriggerPrices, bool showSource, bool showIndicatorLabel)
		{
			if (cacheSentinelBrickCounter_v1_0_0 != null)
				for (int idx = 0; idx < cacheSentinelBrickCounter_v1_0_0.Length; idx++)
					if (cacheSentinelBrickCounter_v1_0_0[idx] != null && cacheSentinelBrickCounter_v1_0_0[idx].CardCorner == cardCorner && cacheSentinelBrickCounter_v1_0_0[idx].MaxAgeSeconds == maxAgeSeconds && cacheSentinelBrickCounter_v1_0_0[idx].ShowUpDown == showUpDown && cacheSentinelBrickCounter_v1_0_0[idx].ShowTriggerPrices == showTriggerPrices && cacheSentinelBrickCounter_v1_0_0[idx].ShowSource == showSource && cacheSentinelBrickCounter_v1_0_0[idx].ShowIndicatorLabel == showIndicatorLabel && cacheSentinelBrickCounter_v1_0_0[idx].EqualsInput(input))
						return cacheSentinelBrickCounter_v1_0_0[idx];
			return CacheIndicator<Sentinel.Sensors.SentinelBrickCounter_v1_0_0>(new Sentinel.Sensors.SentinelBrickCounter_v1_0_0(){ CardCorner = cardCorner, MaxAgeSeconds = maxAgeSeconds, ShowUpDown = showUpDown, ShowTriggerPrices = showTriggerPrices, ShowSource = showSource, ShowIndicatorLabel = showIndicatorLabel }, input, ref cacheSentinelBrickCounter_v1_0_0);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.Sentinel.Sensors.SentinelBrickCounter_v1_0_0 SentinelBrickCounter_v1_0_0(SentinelCardCorner cardCorner, int maxAgeSeconds, bool showUpDown, bool showTriggerPrices, bool showSource, bool showIndicatorLabel)
		{
			return indicator.SentinelBrickCounter_v1_0_0(Input, cardCorner, maxAgeSeconds, showUpDown, showTriggerPrices, showSource, showIndicatorLabel);
		}

		public Indicators.Sentinel.Sensors.SentinelBrickCounter_v1_0_0 SentinelBrickCounter_v1_0_0(ISeries<double> input , SentinelCardCorner cardCorner, int maxAgeSeconds, bool showUpDown, bool showTriggerPrices, bool showSource, bool showIndicatorLabel)
		{
			return indicator.SentinelBrickCounter_v1_0_0(input, cardCorner, maxAgeSeconds, showUpDown, showTriggerPrices, showSource, showIndicatorLabel);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.Sentinel.Sensors.SentinelBrickCounter_v1_0_0 SentinelBrickCounter_v1_0_0(SentinelCardCorner cardCorner, int maxAgeSeconds, bool showUpDown, bool showTriggerPrices, bool showSource, bool showIndicatorLabel)
		{
			return indicator.SentinelBrickCounter_v1_0_0(Input, cardCorner, maxAgeSeconds, showUpDown, showTriggerPrices, showSource, showIndicatorLabel);
		}

		public Indicators.Sentinel.Sensors.SentinelBrickCounter_v1_0_0 SentinelBrickCounter_v1_0_0(ISeries<double> input , SentinelCardCorner cardCorner, int maxAgeSeconds, bool showUpDown, bool showTriggerPrices, bool showSource, bool showIndicatorLabel)
		{
			return indicator.SentinelBrickCounter_v1_0_0(input, cardCorner, maxAgeSeconds, showUpDown, showTriggerPrices, showSource, showIndicatorLabel);
		}
	}
}

#endregion
