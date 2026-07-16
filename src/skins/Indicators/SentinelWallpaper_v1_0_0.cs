// ─────────────────────────────────────────────────────────────────────────────
// This Source Code Form is subject to the terms of the Mozilla Public License,
// v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
// one at https://mozilla.org/MPL/2.0/.
//
// Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
// ─────────────────────────────────────────────────────────────────────────────
// ═════════════════════════════════════════════════════════════════════════════
//  SentinelWallpaper — the ghosted brand mark behind the chart (Sentinel Suite)
//  File: SentinelWallpaper_v1_0_0.cs   Class: SentinelWallpaper_v1_0_0
// ─────────────────────────────────────────────────────────────────────────────
//  WHAT THIS IS
//    A decorative price-panel "wallpaper": the Sentinel Spartan-helmet brand mark,
//    ghosted at a few percent opacity, centered (or anchored) in the chart panel.
//    It re-themes itself with the rest of the suite — one wallpaper, every skin.
//
//  WHY VECTOR, NOT A PNG
//    NT's ChartControl.ChartBackground is a SolidColorBrush key in every skin (an
//    ImageBrush is not supported there), so a raster wallpaper has nowhere to live.
//    Drawing SentinelSkin.HelmetGeometry through Painter.FillSvgPath instead means
//    the mark is resolution-independent: SHARP at any zoom, any DPI, any panel size,
//    with no bitmap to scale, blur, or ship. It is the SAME geometry as the WPF
//    header mark, so the brand identity is literally one string in SentinelSkin.
//
//  THEME / COLOR
//    The ghost is drawn from SentinelSkin.CInk, which already flips per theme (bone
//    white on Amber, drafting white on Blueprint, near-white on Dark/Silver/Obsidian,
//    dark-slate on Light) — so it complements every skin's background with no
//    per-theme branch. Effective opacity is scaled per theme
//    (see ThemeGhostScale): true black needs a touch more lift than navy; light
//    needs restraint. Optional ENGRAVE draws a 1px light/dark offset pair for a
//    subtle bevel, which is what makes it read as pressed into the glass.
//
//    ⚠ DELIBERATELY NOT CYAN BY DEFAULT. The suite's one law is "cyan = live/
//    watching; green/red = money + direction". A chart-sized cyan helmet behind
//    every candle would quietly spend that signal on decoration. TintWithAccent
//    exists for screenshots/marketing, and defaults OFF.
//
//  Z-ORDER (honest caveat)
//    NT renders bars first, then indicators — there is no "behind the bars" layer.
//    This draws OVER the candles at a very low alpha, which reads as behind them.
//    Keep GhostOpacity low (default .05); above ~.12 it starts veiling price.
//
//  NOT a signal tool: no orders, no plots, no SentinelCore …State seam. The
//  publish-a-State-seam standing protocol (design system §9.6) applies to signal/
//  regime/bias/context indicators; this is purely decorative and is exempt.
//
//  All settings are [Display]-only serialized properties (NOT [NinjaScriptProperty]),
//  so they persist to the workspace/template without adding constructor params to
//  NT's generated region — and the custom anchor enum never lands in codegen bare.
//
//  CHANGELOG
//    v1.0.0 (2026-07-09) — first release. Vector ghosted helmet, theme-aware color +
//      per-theme opacity scaling, engrave/bevel, anchor + size + opacity, opt-in
//      accent tint. Label-remover standard. See Docs/SENTINEL_DESIGN_SYSTEM.md §1b/§4b.
// ═════════════════════════════════════════════════════════════════════════════

#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.AddOns.Sentinel;              // SentinelSkin (Painter / tokens / HelmetGeometry)
using NinjaTrader.NinjaScript.Indicators.Sentinel;          // so the generated region resolves the bare anchor enum
#endregion

namespace NinjaTrader.NinjaScript.Indicators.Sentinel
{
    /// <summary>Where the ghosted mark sits inside the price panel.</summary>
    public enum SentinelWallpaperAnchor
    {
        Center,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    public class SentinelWallpaper_v1_0_0 : Indicator
    {
        private SentinelSkin.Painter _sp;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                     = "Sentinel Wallpaper v1.0.0";
                Description              = "Ghosted Sentinel brand mark behind the chart — vector, theme-aware, sharp at any zoom.";
                Calculate                = Calculate.OnBarClose;   // render-only; nothing is computed per tick
                IsOverlay                = true;
                DisplayInDataBox         = false;
                DrawOnPricePanel         = true;
                PaintPriceMarkers        = false;
                IsAutoScale              = false;                  // never influence the price scale
                IsSuspendedWhileInactive = false;

                Anchor             = SentinelWallpaperAnchor.Center;
                SizePercent        = 55;
                GhostOpacity       = 0.05;
                Engrave            = true;
                TintWithAccent     = false;
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
            }
        }

        protected override void OnBarUpdate()
        {
            // No per-bar logic — the wallpaper is drawn entirely in OnRender.
        }

        /// <summary>Per-theme lift for the ghost alpha. A mark at 5% over navy and the SAME mark at 5% over
        /// true black do not read the same: black has no ambient value to carry it, so Obsidian gets a nudge.
        /// Light gets a touch of restraint — dark ink on a pale ground already has plenty of contrast.
        /// ⚠ Keep in sync with the $themes table in Sentinel\Wallpapers\Build-SentinelWallpapers.ps1 — the PNG
        /// wallpapers mirror these scales, and they'd silently diverge from the on-chart mark otherwise.</summary>
        private static double ThemeGhostScale()
        {
            switch (SentinelSkin.Active)
            {
                case SentinelSkin.Theme.Obsidian:  return 1.25;   // no ambient value on pure black
                case SentinelSkin.Theme.Light:     return 0.90;
                case SentinelSkin.Theme.Silver:    return 1.05;
                case SentinelSkin.Theme.Amber:     return 1.10;   // warm near-black, a shade under Obsidian
                case SentinelSkin.Theme.Neon:      return 1.15;   // violet-black night, near Obsidian territory
                case SentinelSkin.Theme.Blueprint: return 1.00;
                default:                           return 1.00;   // Dark
            }
        }

        protected override void OnRender(NinjaTrader.Gui.Chart.ChartControl chartControl, NinjaTrader.Gui.Chart.ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);
            if (RenderTarget == null || ChartPanel == null) return;
            try
            {
                if (_sp == null) _sp = new SentinelSkin.Painter();
                _sp.Begin(RenderTarget);   // also polls the theme (≤2s) so the ghost follows the active skin

                float px = ChartPanel.X, py = ChartPanel.Y, pw = ChartPanel.W, ph = ChartPanel.H;
                if (pw <= 4f || ph <= 4f) { _sp.End(); return; }

                float size = (float)(Math.Min(pw, ph) * (SizePercent / 100.0));
                if (size < 8f) { _sp.End(); return; }

                float pad = size * 0.5f + 12f;   // keep the mark fully inside the panel when corner-anchored
                float cx, cy;
                switch (Anchor)
                {
                    case SentinelWallpaperAnchor.TopLeft:     cx = px + pad;      cy = py + pad;      break;
                    case SentinelWallpaperAnchor.TopRight:    cx = px + pw - pad; cy = py + pad;      break;
                    case SentinelWallpaperAnchor.BottomLeft:  cx = px + pad;      cy = py + ph - pad; break;
                    case SentinelWallpaperAnchor.BottomRight: cx = px + pw - pad; cy = py + ph - pad; break;
                    default:                                  cx = px + pw / 2f;  cy = py + ph / 2f;  break;
                }

                var baseCol = TintWithAccent ? SentinelSkin.CAccent : SentinelSkin.CInk;
                float a = (float)Math.Max(0.0, Math.Min(1.0, GhostOpacity * ThemeGhostScale()));
                if (a <= 0.002f) { _sp.End(); return; }

                // ENGRAVE: a highlight/shadow offset pair under the fill reads as "pressed into the glass".
                // On a dark ground the light edge goes up-left; on a light ground the relationship inverts.
                if (Engrave)
                {
                    float ea = a * 0.55f, d = Math.Max(1f, size * 0.004f);
                    var hi = SentinelSkin.IsLight ? SentinelSkin.CVoid : SentinelSkin.CInk;
                    var lo = SentinelSkin.IsLight ? SentinelSkin.CInk  : SentinelSkin.CVoid;
                    _sp.Helmet(cx - d, cy - d, size, SentinelSkin.Alpha(hi, ea), glow: false);
                    _sp.Helmet(cx + d, cy + d, size, SentinelSkin.Alpha(lo, ea), glow: false);
                }

                // The mark itself — glow OFF: a halo is the opposite of sharp, and it smears on Obsidian.
                _sp.Helmet(cx, cy, size, SentinelSkin.Alpha(baseCol, a), glow: false);

                _sp.End();
            }
            catch { try { _sp.End(); } catch { } }
        }

        #region Properties
        [Display(Name = "Anchor", GroupName = "Wallpaper", Order = 0)]
        public SentinelWallpaperAnchor Anchor { get; set; }

        [Range(5, 95)]
        [Display(Name = "Size (% of panel)", GroupName = "Wallpaper", Order = 1)]
        public int SizePercent { get; set; }

        [Range(0.005, 0.30)]
        [Display(Name = "Ghost opacity", GroupName = "Wallpaper", Order = 2,
                 Description = "Fraction of full opacity, before per-theme scaling. Above ~0.12 the mark starts veiling price.")]
        public double GhostOpacity { get; set; }

        [Display(Name = "Engrave (bevel)", GroupName = "Wallpaper", Order = 3)]
        public bool Engrave { get; set; }

        [Display(Name = "Tint with accent", GroupName = "Wallpaper", Order = 4,
                 Description = "Draw the mark in the cyan accent instead of neutral ink. OFF by default — cyan is reserved for live/watching.")]
        public bool TintWithAccent { get; set; }

        [Display(Name = "Show Indicator Label", GroupName = "Wallpaper", Order = 5)]
        public bool ShowIndicatorLabel { get; set; }
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private Sentinel.SentinelWallpaper_v1_0_0[] cacheSentinelWallpaper_v1_0_0;
		public Sentinel.SentinelWallpaper_v1_0_0 SentinelWallpaper_v1_0_0()
		{
			return SentinelWallpaper_v1_0_0(Input);
		}

		public Sentinel.SentinelWallpaper_v1_0_0 SentinelWallpaper_v1_0_0(ISeries<double> input)
		{
			if (cacheSentinelWallpaper_v1_0_0 != null)
				for (int idx = 0; idx < cacheSentinelWallpaper_v1_0_0.Length; idx++)
					if (cacheSentinelWallpaper_v1_0_0[idx] != null &&  cacheSentinelWallpaper_v1_0_0[idx].EqualsInput(input))
						return cacheSentinelWallpaper_v1_0_0[idx];
			return CacheIndicator<Sentinel.SentinelWallpaper_v1_0_0>(new Sentinel.SentinelWallpaper_v1_0_0(), input, ref cacheSentinelWallpaper_v1_0_0);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.Sentinel.SentinelWallpaper_v1_0_0 SentinelWallpaper_v1_0_0()
		{
			return indicator.SentinelWallpaper_v1_0_0(Input);
		}

		public Indicators.Sentinel.SentinelWallpaper_v1_0_0 SentinelWallpaper_v1_0_0(ISeries<double> input )
		{
			return indicator.SentinelWallpaper_v1_0_0(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.Sentinel.SentinelWallpaper_v1_0_0 SentinelWallpaper_v1_0_0()
		{
			return indicator.SentinelWallpaper_v1_0_0(Input);
		}

		public Indicators.Sentinel.SentinelWallpaper_v1_0_0 SentinelWallpaper_v1_0_0(ISeries<double> input )
		{
			return indicator.SentinelWallpaper_v1_0_0(input);
		}
	}
}

#endregion
