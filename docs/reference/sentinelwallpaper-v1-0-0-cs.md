# SentinelWallpaper_v1_0_0.cs

> `bin/Custom/Indicators/SentinelWallpaper_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 270 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelWallpaper_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
one at https://mozilla.org/MPL/2.0/.

Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
─────────────────────────────────────────────────────────────────────────────
═════════════════════════════════════════════════════════════════════════════
 SentinelWallpaper — the ghosted brand mark behind the chart (Sentinel Suite)
 File: SentinelWallpaper_v1_0_0.cs   Class: SentinelWallpaper_v1_0_0
─────────────────────────────────────────────────────────────────────────────
 WHAT THIS IS
   A decorative price-panel "wallpaper": the Sentinel Spartan-helmet brand mark,
   ghosted at a few percent opacity, centered (or anchored) in the chart panel.
   It re-themes itself with the rest of the suite — one wallpaper, every skin.

 WHY VECTOR, NOT A PNG
   NT's ChartControl.ChartBackground is a SolidColorBrush key in every skin (an
   ImageBrush is not supported there), so a raster wallpaper has nowhere to live.
   Drawing SentinelSkin.HelmetGeometry through Painter.FillSvgPath instead means
   the mark is resolution-independent: SHARP at any zoom, any DPI, any panel size,
   with no bitmap to scale, blur, or ship. It is the SAME geometry as the WPF
   header mark, so the brand identity is literally one string in SentinelSkin.

 THEME / COLOR
   The ghost is drawn from SentinelSkin.CInk, which already flips per theme (bone
   white on Amber, drafting white on Blueprint, near-white on Dark/Silver/Obsidian,
   dark-slate on Light) — so it complements every skin's background with no
   per-theme branch. Effective opacity is scaled per theme
   (see ThemeGhostScale): true black needs a touch more lift than navy; light
   needs restraint. Optional ENGRAVE draws a 1px light/dark offset pair for a
   subtle bevel, which is what makes it read as pressed into the glass.

   ⚠ DELIBERATELY NOT CYAN BY DEFAULT. The suite's one law is "cyan = live/
   watching; green/red = money + direction". A chart-sized cyan helmet behind
   every candle would quietly spend that signal on decoration. TintWithAccent
   exists for screenshots/marketing, and defaults OFF.

 Z-ORDER (honest caveat)
   NT renders bars first, then indicators — there is no "behind the bars" layer.
   This draws OVER the candles at a very low alpha, which reads as behind them.
   Keep GhostOpacity low (default .05); above ~.12 it starts veiling price.

 NOT a signal tool: no orders, no plots, no SentinelCore …State seam. The
 publish-a-State-seam standing protocol (design system §9.6) applies to signal/
 regime/bias/context indicators; this is purely decorative and is exempt.

 All settings are [Display]-only serialized properties (NOT [NinjaScriptProperty]),
 so they persist to the workspace/template without adding constructor params to
 NT's generated region — and the custom anchor enum never lands in codegen bare.

 CHANGELOG
   v1.0.0 (2026-07-09) — first release. Vector ghosted helmet, theme-aware color +
     per-theme opacity scaling, engrave/bevel, anchor + size + opacity, opt-in
     accent tint. Label-remover standard. See Docs/SENTINEL_DESIGN_SYSTEM.md §1b/§4b.
```

