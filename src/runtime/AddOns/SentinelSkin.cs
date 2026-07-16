// ─────────────────────────────────────────────────────────────────────────────
// This Source Code Form is subject to the terms of the Mozilla Public License,
// v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
// one at https://mozilla.org/MPL/2.0/.
//
// Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
// ─────────────────────────────────────────────────────────────────────────────
// ============================================================================
// SentinelSkin — the shared "flight-instrument" drawing framework for the Suite
// ============================================================================
// ONE library every Sentinel indicator/strategy uses to draw cohesive on-chart
// cards, text, dots, pills, gauges, sparklines and tracks — so SentinelEye,
// SignalExcursionRecorder, CompressionBase, the Deck, GTrader21, and every FUTURE
// tool look and feel like they belong to the same instrument panel.
//
// USAGE (SharpDX OnRender):
//   private readonly SentinelSkin.Painter _sp = new SentinelSkin.Painter();   // field
//   ...
//   protected override void OnRender(ChartControl cc, ChartScale cs) {
//       base.OnRender(cc, cs);
//       if (RenderTarget == null) return;
//       _sp.Begin(RenderTarget);
//       var r = _sp.Card(x, y, 300f, 150f, active ? SentinelSkin.CLine : SentinelSkin.CWarn);
//       _sp.Dot(r.Left + 19f, r.Top + 23f, active ? SentinelSkin.CAccent : SentinelSkin.CWarn, glow:true);
//       _sp.Text("SENTINEL EYE", r.Left + 30f, r.Top + 14f, 160f, 18f, SentinelSkin.CInk, 12f, semibold:true);
//       _sp.Money(r.Left + 15f, r.Top + 42f, pnl, pnl >= 0 ? SentinelSkin.CUp : SentinelSkin.CDown);
//       _sp.End();
//   }
//   protected override void OnStateChange() { ... if (State==State.Terminated) _sp.Dispose(); }
//
// The Painter owns its DirectWrite factory (disposed in Dispose) and caches brushes
// (keyed to the RenderTarget) + text formats — so OnRender never allocates per frame
// except the handful of gradients/geometries, which End() releases. See
// Docs/SENTINEL_DESIGN_SYSTEM.md §"Indicator framework".
//
// Palette = the Sentinel tokens (identical to SentinelDashboard + the Sentinel skin).
// The one rule: cyan = live/watching; green/red = money + direction.
//
// CHANGELOG
//   2026-07-10 — CardLayout: FLICKER FIX (the collapse layout OSCILLATED; the chart flashed). Three defects, all
//     mine, all introduced by the collapse work the day before:
//     (a) THE VERDICT WAS RECOMPUTED INSIDE ALL 19 `Place()` CALLS, EVERY FRAME, from scratch. Now `Decide()`
//         computes a column's gap/scale/collapsed-set ONCE and caches it (`RecomputeMs` = 400ms). Layout does not
//         need to track the frame rate — and deciding every frame is precisely what let it oscillate.
//     (b) NO HYSTERESIS. On the fit boundary: collapse → fits → expand → doesn't fit → collapse … forever. Collapse
//         is now STICKY: cheap to collapse, but a card must fit with `ExpandHysteresisPx` (18px) of room TO SPARE
//         before it may expand again.
//     (c) UNSTABLE COLUMN ORDER. `Ord` is keyed by TYPE NAME, so two instances of one tool (the user runs two
//         SentinelExcursionRecorders) TIE — and `List.Sort` is unstable, so tied slots swapped each frame and the
//         collapse victim changed IDENTITY. Slots now carry a monotonic `Seq`; the column sorts by (Ord, Seq),
//         a total order.
//     Diagnosed straight from sentinel.log, which showed the same-size set with a DIFFERENT victim flipping back and
//     forth (`TopRight … CompressionBase` ⇄ `TopRight … SentinelGodReversal`). The log now signs on card NAMES, not
//     just the count — a count-only check would have hidden exactly this.
//   2026-07-09 — CardLayout: THREE LAYOUT BUGS FIXED (found by a live Blueprint screenshot showing the Deck's risk
//     card BURIED under the God Reversal card). See the CardLayout doc-comment + Docs/SENTINEL_RAIL_SPEC.md §1.
//     (1) ORDER DRIFT — stack order was registration order, and a pruned card was re-appended to the END on return,
//         so it walked down its column. Now a STICKY per-(panel,type) ordinal that survives pruning.
//     (2) OVERFLOW — `off` grew unbounded: a column taller than the panel pushed its tail off the edge and the card
//         was SILENTLY LOST. A column now FITS ITSELF to its budget, in order of least harm: compress the gap →
//         SCALE-TO-FIT (a Direct2D transform in Painter.Card; text is vector so it stays crisp) → only then hide
//         the tail. Hidden cards are counted (`OverflowCount`) + logged, never silent. `MinCardScale` is the
//         legibility floor; hiding one card lets the survivors re-expand, so the loop recomputes rather than settles.
//     (3) CROSS-CORNER COLLISION — a corner ignored the opposite corner on the SAME EDGE, so a long TopRight sensor
//         stack grew straight through the BottomRight-anchored Deck card. Top columns now RESERVE the bottom
//         column's height (≤60% of panel). POLICY: top yields to bottom, never the reverse — burying the risk card
//         is worse than hiding a sensor. PINNED cards (the Bridge's ARM button) are never hidden.
//     `Place()` gains an OPTIONAL `pinned` param → all 19 existing call sites compile untouched.
//     ⚠ SentinelSkin now depends on SentinelCore (for the overflow log). Both were already hard deps of every
//     Sentinel indicator (see Docs/SENTINEL_SHIP_MANIFEST.md), so the ship surface is unchanged.
//   2026-07-09 — AMBER, the 6th theme (warm dark / night-watch) — the FIRST theme to move the ACCENT OFF CYAN.
//     The law is "ONE accent = live/watching"; it never said the accent must be cyan. Consumers are untouched
//     (they read CAccent). ⚠ Moving the accent to amber FORCED Warn off amber → COOL BLUE #6FA8FF, because
//     "live" and "caution" must never share a hue. Amber is the only theme whose Warn is cool: deliberate.
//     Also: TryParseTheme is now PUBLIC, so UI surfaces (the Deck's theme button) map word→Theme from ONE place
//     instead of each keeping an if-chain that rots when a theme lands.
//   2026-07-09 — BLUEPRINT, the 5th theme (cyanotype drafting paper). Deep architect's-blue grounds,
//     drafting-white ink, and a LIFTED cyan accent (#5FE3F2 — plain #3FD1E0 reads as mud on a blue ground).
//     Skin `templates\Skins\Sentinel Blueprint\` lifts the GRID LINES well above the paper; the grid is the
//     theme's signature. Proof the 4-place extension contract holds: Palette · Theme · TryParseTheme · SkinBgTheme.
//   2026-07-09 — OBSIDIAN, the 4th theme (true-black OLED) + a HARDENED skin-follow glue.
//     • New `Palette Obsidian` (Void = literal #000) + `Theme.Obsidian` + theme.txt word "obsidian".
//     • New per-theme `Palette.GlowMul` scales every glow/halo ALPHA (Dot/Pill/HistoBar/GlowLine);
//       Obsidian = 0.6 because bloom that reads as light on navy reads as SMEAR on true black.
//       ⚠ GlowMul is a class FIELD → an unset Palette defaults to 0 (no glow). Every Palette sets it.
//     • THE GLUE IS NO LONGER LUMINANCE-ONLY. `SkinBgTheme` maps each Sentinel skin's EXACT
//       ChartControl.ChartBackground hex → its theme, because luminance cannot separate two dark
//       themes (Sentinel #0F1524 and Sentinel Obsidian #000000 share the same band — Obsidian would
//       have silently resolved as Dark). Luminance survives as the fallback for non-Sentinel skins.
//     • Theme.txt parsing centralised in `TryParseTheme` — adding a theme now touches ONE switch.
//   2026-07-07 — added the PLOT-SKIN primitives (PanelWash / RegimeShade / Baseline / HistoBar / GlowLine):
//                the sub-panel counterpart to the card, so an indicator's histograms/lines/background match
//                the cards' glass material. Reference impl: SentinelWAE. See design system "Sub-panel plot standard".
//
// CHANGELOG
//   v1.1 (2026-07-05) — CardLayout: a shared card-stacking registry so cards from DIFFERENT
//     Sentinel indicators/strategies never cover each other. Each card asks CardLayout.Place()
//     for its rect in a chosen corner (SentinelCardCorner); cards docked to the same corner of the
//     same chart panel AUTO-STACK vertically (gap-separated). Stale entries (indicator hidden/
//     removed) self-prune after ~2s; Release(key) on Terminated for hygiene. Adopt in every card.
//   v1.0 (2026-07-03) — first cut: palette (Color4 + WPF Color), fonts, Painter with
//     B/Ba/Text/Card/Dot/Pill/Money/Track/Gauge/Sparkline/Line + caching.
// ============================================================================

#region Using declarations
using System;
using System.Collections.Generic;
using WpfColor = System.Windows.Media.Color;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.Sentinel
{
    /// <summary>Shared flight-instrument palette + on-chart drawing helpers for all Sentinel tools.</summary>
    public static class SentinelSkin
    {
        // ── fonts ──
        public const string FSans = "Segoe UI";
        public const string FMono = "Consolas";

        // ── palette as SharpDX.Color4 (for OnRender) ──
        public static SharpDX.Color4 RC(int r, int g, int b, float a = 1f) => new SharpDX.Color4(r / 255f, g / 255f, b / 255f, a);
        public static SharpDX.Color4 Alpha(SharpDX.Color4 c, float a) => new SharpDX.Color4(c.Red, c.Green, c.Blue, a);

        // ═══ THEME ═══════════════════════════════════════════════════════════════════════════════════════
        //  Every C* token READS the active Palette, so one SetTheme() recolors every card + plot-skin across
        //  the WHOLE suite — consumers keep using SentinelSkin.CInk/CAccent/… unchanged. Dark is the default.
        //  Light is NOT inverted dark: cool off-white grounds (never glaring white), DEEPENED accents so
        //  cyan/green/red survive on light, dark-slate ink. Live toggle via <Documents>\NinjaTrader 8\Sentinel\
        //  theme.txt ("light"/"dark"), polled ≤2s in Painter.Begin (see Docs/SENTINEL_DESIGN_SYSTEM.md §1b).
        public sealed class Palette
        {
            public SharpDX.Color4 Void, Panel, Card, Line, Dim, Faint, Ink, Ink2, Mute, Accent, Up, Down, Warn;
            public SharpDX.Color4 GlassTop, GlassBot;   // card glass gradient stops
            public SharpDX.Color4 WashTop,  WashBot;    // sub-panel wash gradient stops
            /// <summary>Multiplies every glow/halo ALPHA in the Painter (Dot / Pill bloom / HistoBar / GlowLine).
            /// 1 = the tuned per-theme default. Obsidian drops it because bloom smears on a true-black ground.
            /// MUST be set explicitly by every Palette — it's a class field, so an unset one defaults to 0 (no glow).</summary>
            public float GlowMul;
        }

        public static readonly Palette Dark = new Palette {
            Void = RC(10,14,23), Panel = RC(17,23,38), Card = RC(14,20,32), Line = RC(30,42,61),
            Dim = RC(27,37,54), Faint = RC(38,52,76), Ink = RC(233,238,247), Ink2 = RC(174,186,206),
            Mute = RC(108,122,146), Accent = RC(63,209,224), Up = RC(37,208,139), Down = RC(255,92,106), Warn = RC(242,179,76),
            GlassTop = RC(23,32,50,0.94f), GlassBot = RC(10,14,23,0.95f),
            WashTop  = RC(17,23,38,1f),    WashBot  = RC(10,14,23,1f), GlowMul = 1f
        };

        // Light = SOFT daylight, not high-key white (a full chart of near-white glares — "staring at the sun").
        // Grounds are a calm cool-grey; the CARD stays the brightest surface so it reads as ELEVATED (hierarchy
        // = depth, not one flat white wall). Borders a touch stronger so light-on-light separates.
        public static readonly Palette Light = new Palette {
            Void = RC(222,228,236), Panel = RC(231,236,243), Card = RC(246,248,252), Line = RC(193,203,216),
            Dim = RC(213,221,232), Faint = RC(219,226,237), Ink = RC(27,36,50), Ink2 = RC(86,96,114),
            Mute = RC(134,146,164), Accent = RC(12,140,160), Up = RC(16,148,88), Down = RC(210,52,64), Warn = RC(190,124,14),
            GlassTop = RC(251,252,254,0.97f), GlassBot = RC(237,241,247,0.97f),
            WashTop  = RC(234,238,245,1f),    WashBot  = RC(222,228,236,1f), GlowMul = 1f   // light's glow-down is already baked into the IsLight branches
        };

        // Silver = brushed graphite/steel — a premium MID-TONE between the navy Dark and the grey Light.
        // Still a dark-side theme (light text), but cooler + lighter than Dark, with a brighter metallic cyan.
        public static readonly Palette Silver = new Palette {
            Void = RC(38,43,52), Panel = RC(46,52,62), Card = RC(52,59,70), Line = RC(72,81,95),
            Dim = RC(58,66,78), Faint = RC(66,75,89), Ink = RC(232,236,242), Ink2 = RC(178,188,201),
            Mute = RC(126,137,151), Accent = RC(80,214,228), Up = RC(46,206,142), Down = RC(255,100,116), Warn = RC(240,184,92),
            GlassTop = RC(60,68,82,0.94f), GlassBot = RC(40,46,56,0.95f),
            WashTop  = RC(48,55,66,1f),    WashBot  = RC(36,41,50,1f), GlowMul = 1f
        };

        // Obsidian = TRUE-BLACK OLED theme. The void is literal #000 (pixels off): maximum contrast, deepest
        // blacks, least eye strain in a dark room. NOT just "Dark, darker" — three deliberate differences:
        //   (1) the ground ramp is near-neutral (a hair of blue), because a navy tint over #000 reads as haze;
        //   (2) INK is lifted (#F2F5FA) since text on pure black needs less weight to feel bright, not more;
        //   (3) GLOW is dialled back (0.6) — a bloom halo that looks like light on navy looks like SMEAR on black.
        // Accent/Up/Down are unchanged from Dark: the suite's one law (cyan = live; green/red = money+direction).
        public static readonly Palette Obsidian = new Palette {
            Void = RC(0,0,0), Panel = RC(10,12,16), Card = RC(13,16,22), Line = RC(32,38,50),
            Dim = RC(23,28,37), Faint = RC(42,49,62), Ink = RC(242,245,250), Ink2 = RC(168,178,194),
            Mute = RC(102,112,126), Accent = RC(63,209,224), Up = RC(37,208,139), Down = RC(255,92,106), Warn = RC(242,179,76),
            GlassTop = RC(16,20,28,0.96f), GlassBot = RC(0,0,0,0.96f),
            WashTop  = RC(10,12,16,1f),    WashBot  = RC(0,0,0,1f), GlowMul = 0.6f
        };

        // Blueprint = CYANOTYPE drafting paper. Deep architect's blue grounds, drafting-white ink, and a LIFTED
        // cyan accent (#5FE3F2) — plain #3FD1E0 sits too close to a blue ground to read as "live". This is the one
        // theme where the suite's law (cyan = live/watching) feels native rather than applied: the whole surface is
        // the blueprint, the cyan is the ink you draw the live thing in. Grid lines are the signature — the platform
        // skin lifts them well above the paper on purpose (see Sentinel Blueprint\ChartControl.xaml).
        public static readonly Palette Blueprint = new Palette {
            Void = RC(8,24,47), Panel = RC(15,41,80), Card = RC(13,36,71), Line = RC(42,78,133),
            Dim = RC(20,52,98), Faint = RC(30,64,118), Ink = RC(234,242,255), Ink2 = RC(169,194,230),
            Mute = RC(110,140,184), Accent = RC(95,227,242), Up = RC(47,214,148), Down = RC(255,107,120), Warn = RC(255,194,77),
            GlassTop = RC(20,48,88,0.94f), GlassBot = RC(8,24,47,0.95f),
            WashTop  = RC(15,41,80,1f),    WashBot  = RC(8,24,47,1f), GlowMul = 1f
        };

        // Amber = the NIGHT WATCH / gold-desk theme, and the first theme to move the ACCENT OFF CYAN.
        //   • Grounds are warm near-black (a hair of brown, never grey) — a cool grey under amber reads as dirty.
        //   • Accent = amber #E8A33D. The suite's law is "ONE accent = live/watching"; it never said the accent
        //     must be cyan. Every tool's live dot simply turns gold here — no consumer changes (they read CAccent).
        //   • ⚠ THE LAW FORCED A SECOND MOVE: Warn is amber (#F2B34C) in every other theme, which would collide
        //     head-on with this accent — "live" and "caution" must never share a hue. So Warn becomes COOL BLUE
        //     #6FA8FF here. It is the only theme where Warn is not warm, and that is deliberate, not a slip.
        //   • Up/Down are re-tuned warm (#3FBF7F / #E5535F) so money still reads green/red against brown.
        public static readonly Palette Amber = new Palette {
            Void = RC(18,16,12), Panel = RC(27,23,16), Card = RC(23,19,13), Line = RC(54,45,30),
            Dim = RC(31,26,18), Faint = RC(41,34,23), Ink = RC(243,237,224), Ink2 = RC(192,180,155),
            Mute = RC(133,122,100), Accent = RC(232,163,61), Up = RC(63,191,127), Down = RC(229,83,95), Warn = RC(111,168,255),
            GlassTop = RC(35,29,20,0.94f), GlassBot = RC(18,16,12,0.95f),
            WashTop  = RC(27,23,16,1f),    WashBot  = RC(18,16,12,1f), GlowMul = 0.9f
        };

        // Neon = SYNTHWAVE. Electric violet on a violet-black night, with the GLOW CRANKED — a neon sign IS its bloom,
        // so this is the inverse of Obsidian: GlowMul 1.5 vs 0.6. The 2nd theme (after Amber) to move the accent off
        // cyan: Accent = neon violet #C13BFF. Checked against the law before committing (the Amber lesson):
        //   • vs Down (neon red #FF3357): violet is clearly blue-purple, red is warm — money never reads as "live".
        //   • vs Up (neon green) + Warn (amber): distinct hues, so Neon KEEPS the amber Warn (unlike the Amber theme,
        //     whose amber accent forced Warn to cool blue). Live and caution stay well apart.
        //   • Grounds are violet-black (a real purple tint, not grey) and INK is lifted (#F4EEFF) like Obsidian — bright
        //     text needs less weight on a near-black ground. Borders/Line are a visible lit violet — the neon edge.
        public static readonly Palette Neon = new Palette {
            Void = RC(11,7,20), Panel = RC(21,12,36), Card = RC(17,10,29), Line = RC(58,35,88),
            Dim = RC(30,18,52), Faint = RC(42,26,72), Ink = RC(244,238,255), Ink2 = RC(195,178,232),
            Mute = RC(136,120,174), Accent = RC(193,59,255), Up = RC(45,255,154), Down = RC(255,51,87), Warn = RC(255,185,46),
            GlassTop = RC(25,15,42,0.94f), GlassBot = RC(11,7,20,0.95f),
            WashTop  = RC(21,12,36,1f),    WashBot  = RC(11,7,20,1f), GlowMul = 1.5f
        };

        public enum Theme { Dark, Light, Silver, Obsidian, Blueprint, Amber, Neon }
        private static Palette _p = Dark;
        public static Theme Active { get; private set; } = Theme.Dark;
        public static bool IsLight => Active == Theme.Light;   // kept for existing IsLight gates
        public static void SetTheme(Theme t)
        {
            Active = t;
            switch (t)
            {
                case Theme.Light:     _p = Light;     break;
                case Theme.Silver:    _p = Silver;    break;
                case Theme.Obsidian:  _p = Obsidian;  break;
                case Theme.Blueprint: _p = Blueprint; break;
                case Theme.Amber:     _p = Amber;     break;
                default:              _p = Dark;      break;
            }
        }
        /// <summary>Reset the theme-poll throttle so the NEXT MaybeRefreshTheme re-resolves now (e.g. after a manual toggle to "auto").</summary>
        public static void ForceThemeRecheck() { lock (_themeLock) { _themeChecked = DateTime.MinValue; } }

        // token accessors (were static readonly fields; now read the active palette — reads unchanged for callers)
        public static SharpDX.Color4 CVoid    => _p.Void;
        public static SharpDX.Color4 CPanel   => _p.Panel;
        public static SharpDX.Color4 CCard    => _p.Card;
        public static SharpDX.Color4 CLine    => _p.Line;
        public static SharpDX.Color4 CDim     => _p.Dim;
        public static SharpDX.Color4 CFaint   => _p.Faint;
        public static SharpDX.Color4 CInk     => _p.Ink;
        public static SharpDX.Color4 CInk2    => _p.Ink2;
        public static SharpDX.Color4 CMute    => _p.Mute;
        public static SharpDX.Color4 CAccent  => _p.Accent;   // cyan (deeper on light) — live/watching
        public static SharpDX.Color4 CUp      => _p.Up;       // green — money up
        public static SharpDX.Color4 CDown    => _p.Down;     // red — money down
        public static SharpDX.Color4 CWarn    => _p.Warn;     // amber — caution
        public static float GlowMul => _p.GlowMul;             // per-theme glow-alpha scale (Obsidian dims the bloom)
        public static SharpDX.Color4 CGlassTop => _p.GlassTop;
        public static SharpDX.Color4 CGlassBot => _p.GlassBot;
        public static SharpDX.Color4 CWashTop  => _p.WashTop;
        public static SharpDX.Color4 CWashBot  => _p.WashBot;

        // WPF-Color accessors (theme-aware) for hosted WPF surfaces (the Deck / Dashboard). Read at BUILD time —
        // a WPF surface picks up the active theme when it's (re)built (live hot-swap would need a rebuild).
        public static System.Windows.Media.Color WCol(SharpDX.Color4 c) =>
            System.Windows.Media.Color.FromRgb((byte)(c.Red * 255f + 0.5f), (byte)(c.Green * 255f + 0.5f), (byte)(c.Blue * 255f + 0.5f));
        public static System.Windows.Media.Color KVoid   => WCol(CVoid);
        public static System.Windows.Media.Color KPanel  => WCol(CPanel);
        public static System.Windows.Media.Color KCard   => WCol(CCard);
        public static System.Windows.Media.Color KLine   => WCol(CLine);
        public static System.Windows.Media.Color KDim    => WCol(CDim);
        public static System.Windows.Media.Color KFaint  => WCol(CFaint);
        public static System.Windows.Media.Color KInk    => WCol(CInk);
        public static System.Windows.Media.Color KInk2   => WCol(CInk2);
        public static System.Windows.Media.Color KMute   => WCol(CMute);
        public static System.Windows.Media.Color KAccent => WCol(CAccent);
        public static System.Windows.Media.Color KUp     => WCol(CUp);
        public static System.Windows.Media.Color KDown   => WCol(CDown);
        public static System.Windows.Media.Color KWarn   => WCol(CWarn);

        // ── THE SENTINEL BRAND MARK — a Spartan "barbute" helmet (one source of truth for every header) ──
        //  ARTWORK CREDIT (required): "barbute" by Lorc, game-icons.net — licensed CC BY 3.0
        //  (https://creativecommons.org/licenses/by/3.0/). Attribution must also appear in the app's About/credits.
        //  Native 0..512 viewBox, nonzero winding. WPF parses it directly; the on-chart SharpDX path fills it via
        //  Painter.FillSvgPath. Swapping the mark = replace THIS string only (both renderers follow).
        public const string HelmetGeometry =
            "M255.406 17.75C189.313 39.42 124.536 85.124 79.03 150.344c21.238 57.44 32.72 94.314 32.72 131.375 0 " +
            "36.493-11.52 73.723-32.125 129.655 49.72 36.73 100.08 58.95 150.313 64.938-5.052-60.378-9.83-120.748 " +
            "1.593-181.125-30.644-3.28-61.384-13.286-92.03-30.72v-71.312c80.67 42.255 158.908 41.547 242.063 0v71.313" +
            "c-30.06 14.376-60.192 24.722-90.25 29.28 8.684 60.46 7.723 120.915 2.03 181.375 46.386-7.335 92.89-28.824 " +
            "139.032-64.312-33.966-112.954-34.03-145.933.594-260.47C391.162 84.844 317.924 39.89 255.405 17.75z" +
            "m-75.125 212c-11.16-.13-19.646 3.174-21.25 9.156-2.33 8.7 10.778 19.76 29.282 24.72 18.505 4.957 35.388 " +
            "1.92 37.72-6.782 2.33-8.7-10.775-19.76-29.282-24.72-5.783-1.55-11.396-2.315-16.47-2.374z" +
            "m160.69 0c-5.074.06-10.687.825-16.47 2.375-18.507 4.96-31.613 16.018-29.28 24.72 2.33 8.7 19.213 11.738 " +
            "37.717 6.78 18.505-4.958 31.613-16.018 29.282-24.72-1.604-5.98-10.09-9.286-21.25-9.155z";

        /// <summary>The Sentinel Spartan-helmet brand mark as a WPF element — theme-accent fill + theme-colored
        /// glow. Drop into every WPF panel header for ONE consistent identity. <paramref name="size"/> = glyph px;
        /// pass <paramref name="fill"/> to override the accent brush (e.g. a tool's own themed accent).</summary>
        public static System.Windows.FrameworkElement HelmetMark(double size = 22, System.Windows.Media.Brush fill = null)
        {
            var host = new System.Windows.Controls.Grid { Width = size + 4, Height = size + 4, VerticalAlignment = System.Windows.VerticalAlignment.Center };
            host.Children.Add(new System.Windows.Shapes.Path
            {
                Data = System.Windows.Media.Geometry.Parse("F1 " + HelmetGeometry),   // F1 = nonzero (matches the source SVG)
                Fill = fill ?? new System.Windows.Media.SolidColorBrush(KAccent),
                Stretch = System.Windows.Media.Stretch.Uniform, Width = size, Height = size,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Center
            });
            host.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = KAccent, BlurRadius = 11, ShadowDepth = 0, Opacity = 0.6 };
            return host;
        }

        // Theme resolution (throttled ≤2s, applied render-thread):
        //   1) MANUAL OVERRIDE — Sentinel\theme.txt = a theme word ("dark"|"light"|"silver"|"obsidian") pins it.
        //   2) THE GLUE — otherwise (absent / "auto") the on-chart theme FOLLOWS the active platform SKIN,
        //      identified by its ChartControl.ChartBackground, so ONE skin switch re-themes everything.
        private static DateTime _themeChecked = DateTime.MinValue;
        private static readonly object _themeLock = new object();

        /// <summary>Parse a theme.txt word. "auto"/empty/unknown → false (fall through to the glue).
        /// ONE place to extend when a theme is added. PUBLIC so UI surfaces (the Deck's theme button) can map a
        /// mode word → Theme without each keeping its own if-chain that rots the next time a theme lands.</summary>
        public static bool TryParseTheme(string s, out Theme t)
        {
            switch (s)
            {
                case "dark":      t = Theme.Dark;      return true;
                case "light":     t = Theme.Light;     return true;
                case "silver":    t = Theme.Silver;    return true;
                case "obsidian":  t = Theme.Obsidian;  return true;
                case "blueprint": t = Theme.Blueprint; return true;
                case "amber":     t = Theme.Amber;     return true;
                case "neon":      t = Theme.Neon;      return true;
                default:          t = Theme.Dark;      return false;
            }
        }

        internal static void MaybeRefreshTheme()
        {
            try
            {
                lock (_themeLock)
                {
                    var now = DateTime.UtcNow;
                    if ((now - _themeChecked).TotalSeconds < 2) return;
                    _themeChecked = now;
                }
                // 1) explicit manual override from theme.txt
                string path = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "NinjaTrader 8", "Sentinel", "theme.txt");
                string s = null;
                try { if (System.IO.File.Exists(path)) s = System.IO.File.ReadAllText(path).Trim().ToLowerInvariant(); } catch { }
                Theme pin;
                if (TryParseTheme(s, out pin)) { if (Active != pin) SetTheme(pin); return; }
                // 2) the glue: no explicit pin (absent / "auto") → follow the active platform skin
                Theme t;
                if (TryThemeFromSkin(out t) && t != Active) SetTheme(t);
            }
            catch { }
        }

        /// <summary>The EXACT ChartControl.ChartBackground of each Sentinel platform skin → its on-chart theme.
        /// Exact-match first because LUMINANCE CANNOT SEPARATE TWO DARK THEMES: Sentinel (#0F1524, lum≈.08) and
        /// Sentinel Obsidian (#000000, lum=0) both fall in the same "dark" band, so a luminance-only classifier
        /// would silently resolve Obsidian as Dark. Adding a skin = add its background hex here (and keep the hex
        /// in that skin's ChartControl.xaml in sync — both sides are commented).</summary>
        private static readonly Dictionary<uint, Theme> SkinBgTheme = new Dictionary<uint, Theme>
        {
            { 0x0F1524u, Theme.Dark      },   // templates\Skins\Sentinel
            { 0xE6EBF1u, Theme.Light     },   // templates\Skins\Sentinel Light
            { 0x2A2F38u, Theme.Silver    },   // templates\Skins\Sentinel Silver
            { 0x000000u, Theme.Obsidian  },   // templates\Skins\Sentinel Obsidian
            { 0x0A1E3Cu, Theme.Blueprint },   // templates\Skins\Sentinel Blueprint
            { 0x16130Du, Theme.Amber     },   // templates\Skins\Sentinel Amber
            { 0x0B0714u, Theme.Neon      },   // templates\Skins\Sentinel Neon
        };

        /// <summary>Infer the theme from the ACTIVE platform skin's ChartControl.ChartBackground. Exact-hex match
        /// against the Sentinel skins first; otherwise fall back to a luminance bucket so a NON-Sentinel skin
        /// (Midnight, Slate Dark, White-Ice…) still lands on a sane theme. False if the brush can't be read.</summary>
        private static bool TryThemeFromSkin(out Theme t)
        {
            t = Theme.Dark;
            try
            {
                var app = System.Windows.Application.Current;
                if (app == null) return false;
                var b = app.Resources["ChartControl.ChartBackground"] as System.Windows.Media.SolidColorBrush;
                if (b == null) return false;
                var c = b.Color;
                uint key = (uint)((c.R << 16) | (c.G << 8) | c.B);
                if (SkinBgTheme.TryGetValue(key, out t)) return true;             // a Sentinel skin — unambiguous
                double lum = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;   // some other skin — best effort
                t = lum > 0.55 ? Theme.Light : (lum > 0.12 ? Theme.Silver : (lum > 0.02 ? Theme.Dark : Theme.Obsidian));
                return true;
            }
            catch { return false; }
        }

        // ── palette as WPF Color (for indicators/panels that host WPF) ──
        public static WpfColor W(byte r, byte g, byte b) => WpfColor.FromRgb(r, g, b);
        public static readonly WpfColor WVoid   = W(10, 14, 23),   WPanel = W(17, 23, 38),  WCard = W(14, 20, 32),
                                        WLine   = W(30, 42, 61),   WDim   = W(27, 37, 54),  WFaint = W(38, 52, 76),
                                        WInk    = W(233, 238, 247), WInk2 = W(174, 186, 206), WMute = W(108, 122, 146),
                                        WAccent = W(63, 209, 224), WUp   = W(37, 208, 139), WDown = W(255, 92, 106),
                                        WWarn   = W(242, 179, 76);

        /// <summary>Per-indicator drawing context: cached brushes (per RenderTarget) + text formats,
        /// with the flight-instrument card vocabulary. Hold one as a field; Begin() each OnRender;
        /// Dispose() in Terminated.</summary>
        public sealed class Painter : IDisposable
        {
            private SharpDX.Direct2D1.RenderTarget _rt;
            private SharpDX.DirectWrite.Factory _dw;
            private readonly Dictionary<uint, SharpDX.Direct2D1.SolidColorBrush> _brushes = new Dictionary<uint, SharpDX.Direct2D1.SolidColorBrush>();
            private readonly Dictionary<string, SharpDX.DirectWrite.TextFormat> _fmts = new Dictionary<string, SharpDX.DirectWrite.TextFormat>();
            private readonly List<IDisposable> _frame = new List<IDisposable>();
            private SharpDX.Direct2D1.StrokeStyle _round;
            private bool _scaled;   // a card scale transform is active on the SHARED RenderTarget — must be undone

            /// <summary>Call at the top of OnRender with the live RenderTarget.</summary>
            public void Begin(SharpDX.Direct2D1.RenderTarget rt)
            {
                if (rt == null) return;
                MaybeRefreshTheme();   // live light/dark switch from Sentinel\theme.txt (throttled)
                // The RenderTarget is SHARED across every indicator on the chart. If a previous OnRender threw
                // between Card() and End(), its scale transform would still be armed and would silently corrupt
                // everyone drawn after it. Start every frame from identity — cheap, and it makes leaks impossible.
                try { rt.Transform = SharpDX.Matrix3x2.Identity; } catch { }
                _scaled = false;
                if (_dw == null) _dw = new SharpDX.DirectWrite.Factory();
                if (!ReferenceEquals(rt, _rt))
                {
                    // device/target changed — brushes + stroke are RT-bound, rebuild lazily
                    foreach (var b in _brushes.Values) { try { b.Dispose(); } catch { } }
                    _brushes.Clear();
                    if (_round != null) { try { _round.Dispose(); } catch { } _round = null; }
                    _rt = rt;
                }
                if (_round == null)
                {
                    try { _round = new SharpDX.Direct2D1.StrokeStyle(rt.Factory,
                        new SharpDX.Direct2D1.StrokeStyleProperties { StartCap = SharpDX.Direct2D1.CapStyle.Round, EndCap = SharpDX.Direct2D1.CapStyle.Round }); }
                    catch { _round = null; }
                }
            }

            /// <summary>Cached solid brush for a color (created once per RGBA per RenderTarget).</summary>
            public SharpDX.Direct2D1.SolidColorBrush B(SharpDX.Color4 c)
            {
                uint key = ((uint)(c.Red * 255f) & 255u) << 24 | ((uint)(c.Green * 255f) & 255u) << 16
                         | ((uint)(c.Blue * 255f) & 255u) << 8 | (uint)(c.Alpha * 255f) & 255u;
                SharpDX.Direct2D1.SolidColorBrush b;
                if (!_brushes.TryGetValue(key, out b)) { b = new SharpDX.Direct2D1.SolidColorBrush(_rt, c); _brushes[key] = b; }
                return b;
            }
            public SharpDX.Direct2D1.SolidColorBrush B(SharpDX.Color4 c, float a) => B(Alpha(c, a));

            private SharpDX.DirectWrite.TextFormat Fmt(float size, bool semibold, SharpDX.DirectWrite.TextAlignment align, bool mono)
            {
                string key = (mono ? "M" : "S") + size + (semibold ? "b" : "n") + (int)align;
                SharpDX.DirectWrite.TextFormat f;
                if (!_fmts.TryGetValue(key, out f))
                {
                    f = new SharpDX.DirectWrite.TextFormat(_dw, mono ? FMono : FSans,
                        semibold ? SharpDX.DirectWrite.FontWeight.SemiBold : SharpDX.DirectWrite.FontWeight.Normal,
                        SharpDX.DirectWrite.FontStyle.Normal, size);
                    f.TextAlignment = align;
                    f.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center;
                    _fmts[key] = f;
                }
                return f;
            }

            /// <summary>Draw text in the Sentinel font at (x,y) in a (w,h) box.</summary>
            public void Text(string text, float x, float y, float w, float h, SharpDX.Color4 col, float size,
                bool semibold = false, SharpDX.DirectWrite.TextAlignment align = SharpDX.DirectWrite.TextAlignment.Leading, bool mono = false)
            {
                if (string.IsNullOrEmpty(text) || _rt == null) return;
                _rt.DrawText(text, Fmt(size, semibold, align, mono), new SharpDX.RectangleF(x, y, w, h), B(col));
            }

            /// <summary>Glass card: gradient fill + hairline border + top highlight. Returns the inner
            /// content rect (card inset by pad).</summary>
            public SharpDX.RectangleF Card(float x, float y, float w, float h, SharpDX.Color4 border, float radius = 13f, float pad = 15f)
            {
                // How should this card be drawn? CardLayout decides per column (see its doc-comment):
                //  • COLLAPSED — draw a chip here, then TRANSLATE everything the caller draws next off-screen.
                //    The card lays out its contents exactly as always, in its own coordinates, and they simply land
                //    nowhere. No card ever learns that it was collapsed. Reversible, and nothing is lost: the chip
                //    keeps the tool's name + live dot on the chart.
                //  • SCALED — squeeze the whole card through a Direct2D scale transform anchored on the corner it
                //    docks to. Text is vector → it stays crisp.
                // Either transform is torn down in End(). Pinned cards get neither, so their hit-tested controls
                // stay in untransformed screen coordinates.
                float s, cx0, cy0; bool collapsed; string label;
                if (CardLayout.CardStyle(x, y, w, h, out s, out cx0, out cy0, out collapsed, out label))
                {
                    if (collapsed)
                    {
                        Chip(x, y, w, cx0 > x, label, border);
                        _rt.Transform = SharpDX.Matrix3x2.Translation(-100000f, -100000f);
                        _scaled = true;
                        return new SharpDX.RectangleF(x + pad, y + pad, w - pad * 2, h - pad * 2);
                    }
                    _rt.Transform = SharpDX.Matrix3x2.Scaling(s, s, new SharpDX.Vector2(cx0, cy0));
                    _scaled = true;
                }

                var rr = new SharpDX.Direct2D1.RoundedRectangle { Rect = new SharpDX.RectangleF(x, y, w, h), RadiusX = radius, RadiusY = radius };
                var gsc = new SharpDX.Direct2D1.GradientStopCollection(_rt, new[] {
                    new SharpDX.Direct2D1.GradientStop { Color = CGlassTop, Position = 0f },
                    new SharpDX.Direct2D1.GradientStop { Color = CGlassBot, Position = 1f } });
                _frame.Add(gsc);
                var glass = new SharpDX.Direct2D1.LinearGradientBrush(_rt,
                    new SharpDX.Direct2D1.LinearGradientBrushProperties { StartPoint = new SharpDX.Vector2(x, y), EndPoint = new SharpDX.Vector2(x, y + h) }, gsc);
                _frame.Add(glass);
                _rt.FillRoundedRectangle(rr, glass);
                _rt.DrawRoundedRectangle(rr, B(border), 1.2f);
                _rt.DrawLine(new SharpDX.Vector2(x + radius, y + 1.4f), new SharpDX.Vector2(x + w - radius, y + 1.4f), B(CInk, 0.06f), 1f);
                return new SharpDX.RectangleF(x + pad, y + pad, w - pad * 2, h - pad * 2);
            }

            /// <summary>A COLLAPSED card: a slim chip carrying the tool's name + a live dot, drawn at the card's real
            /// slot and flush with the edge it docks to. This is what makes overflow non-destructive — the card is
            /// reduced to a line, never removed, so the operator can always see it is present and rendering.</summary>
            private void Chip(float x, float y, float w, bool right, string label, SharpDX.Color4 border)
            {
                float cw = Math.Min(w, CardLayout.ChipMaxWidth);
                float cx = right ? x + w - cw : x;
                var rr = new SharpDX.Direct2D1.RoundedRectangle {
                    Rect = new SharpDX.RectangleF(cx, y, cw, CardLayout.ChipH), RadiusX = 7f, RadiusY = 7f };
                _rt.FillRoundedRectangle(rr, B(CCard, 0.92f));
                _rt.DrawRoundedRectangle(rr, B(border, 0.75f), 1f);
                // "rendering right now" is the one thing a chip can honestly assert — CAccent = live/watching.
                Dot(cx + 12f, y + CardLayout.ChipH * 0.5f, CAccent, false, 2.6f);
                if (!string.IsNullOrEmpty(label))
                    Text(label, cx + 22f, y + 4f, cw - 30f, CardLayout.ChipH - 6f, CInk2, 10.5f, true);
            }

            /// <summary>Live status dot (with optional cyan-style glow halo). Center at (cx,cy).</summary>
            public void Dot(float cx, float cy, SharpDX.Color4 col, bool glow = true, float r = 3.4f)
            {
                if (glow) _rt.FillEllipse(new SharpDX.Direct2D1.Ellipse(new SharpDX.Vector2(cx, cy), r * (IsLight ? 1.6f : 2f), r * (IsLight ? 1.6f : 2f)), B(col, (IsLight ? 0.10f : 0.26f) * GlowMul));   // glow reads as fuzz on light → tighten + fade; GlowMul dims it further on Obsidian
                _rt.FillEllipse(new SharpDX.Direct2D1.Ellipse(new SharpDX.Vector2(cx, cy), r, r), B(col));
            }

            /// <summary>The Sentinel brand-mark helmet, SharpDX (on-chart cards) — centered at (cx,cy), fitting `size`
            /// px, filled `col`. Fills SentinelSkin.HelmetGeometry (the SAME path the WPF HelmetMark uses) so the mark
            /// stays identical everywhere. Optional soft glow halo (theme-gated).</summary>
            public void Helmet(float cx, float cy, float size, SharpDX.Color4 col, bool glow = true)
                => FillSvgPath(HelmetGeometry, cx, cy, size, col, glow);

            private sealed class SvgFig
            {
                public SharpDX.Vector2 Start;
                public readonly System.Collections.Generic.List<SharpDX.Vector2[]> Segs = new System.Collections.Generic.List<SharpDX.Vector2[]>();
            }

            /// <summary>Fill an SVG path (M/m L/l H/h V/v C/c Z/z; abs+rel; implicit repeats) as a SharpDX geometry
            /// (nonzero winding), scaled Uniform to `size` and centered at (centerX,centerY). Works for any viewBox.</summary>
            public void FillSvgPath(string d, float centerX, float centerY, float size, SharpDX.Color4 col, bool glow = true)
            {
                var figs = ParseSvg(d);
                if (figs.Count == 0) return;
                float minx = float.MaxValue, miny = float.MaxValue, maxx = float.MinValue, maxy = float.MinValue;
                foreach (var f in figs)
                {
                    Acc(f.Start, ref minx, ref miny, ref maxx, ref maxy);
                    foreach (var s in f.Segs) foreach (var p in s) Acc(p, ref minx, ref miny, ref maxx, ref maxy);
                }
                float w = maxx - minx, h = maxy - miny;
                if (w <= 0f || h <= 0f) return;
                float sc = size / Math.Max(w, h);
                float ox = centerX - (minx + w / 2f) * sc, oy = centerY - (miny + h / 2f) * sc;
                System.Func<SharpDX.Vector2, SharpDX.Vector2> T = p => new SharpDX.Vector2(ox + p.X * sc, oy + p.Y * sc);
                var geo = new SharpDX.Direct2D1.PathGeometry(_rt.Factory);
                using (var sink = geo.Open())
                {
                    sink.SetFillMode(SharpDX.Direct2D1.FillMode.Winding);   // nonzero (matches the source SVG)
                    foreach (var f in figs)
                    {
                        sink.BeginFigure(T(f.Start), SharpDX.Direct2D1.FigureBegin.Filled);
                        foreach (var s in f.Segs)
                        {
                            if (s.Length == 3) sink.AddBezier(new SharpDX.Direct2D1.BezierSegment { Point1 = T(s[0]), Point2 = T(s[1]), Point3 = T(s[2]) });
                            else sink.AddLine(T(s[0]));
                        }
                        sink.EndFigure(SharpDX.Direct2D1.FigureEnd.Closed);
                    }
                    sink.Close();
                }
                _frame.Add(geo);
                if (glow && !IsLight) _rt.FillGeometry(geo, B(col, 0.22f * GlowMul));   // subtle bloom on dark/silver (dimmer on obsidian); skip on light
                _rt.FillGeometry(geo, B(col));
            }

            private static void Acc(SharpDX.Vector2 p, ref float minx, ref float miny, ref float maxx, ref float maxy)
            { if (p.X < minx) minx = p.X; if (p.Y < miny) miny = p.Y; if (p.X > maxx) maxx = p.X; if (p.Y > maxy) maxy = p.Y; }

            // Minimal SVG-path parser → figures of absolute line/bezier segments. Validated against a JS twin render.
            private static System.Collections.Generic.List<SvgFig> ParseSvg(string d)
            {
                var figs = new System.Collections.Generic.List<SvgFig>();
                var toks = System.Text.RegularExpressions.Regex.Matches(d, @"[MmLlHhVvCcSsQqZz]|-?(?:\d*\.\d+|\d+)");
                int i = 0; float cx = 0, cy = 0, sx = 0, sy = 0; char cmd = '\0'; SvgFig cur = null;
                System.Func<int, bool> isCmd = k => k < toks.Count && char.IsLetter(toks[k].Value[0]);
                System.Func<float> N = () => float.Parse(toks[i++].Value, System.Globalization.CultureInfo.InvariantCulture);
                while (i < toks.Count)
                {
                    if (isCmd(i)) cmd = toks[i++].Value[0];
                    bool rel = char.IsLower(cmd);
                    char C = char.ToUpperInvariant(cmd);
                    if (C == 'Z') { cx = sx; cy = sy; continue; }
                    do
                    {
                        if (C == 'M')
                        {
                            float x = N(), y = N(); if (rel) { x += cx; y += cy; }
                            cx = x; cy = y; sx = x; sy = y;
                            cur = new SvgFig { Start = new SharpDX.Vector2(x, y) }; figs.Add(cur);
                            C = 'L';   // subsequent implicit coordinate pairs are linetos
                        }
                        else if (C == 'L') { float x = N(), y = N(); if (rel) { x += cx; y += cy; } cx = x; cy = y; if (cur != null) cur.Segs.Add(new[] { new SharpDX.Vector2(x, y) }); }
                        else if (C == 'H') { float x = N(); if (rel) x += cx; cx = x; if (cur != null) cur.Segs.Add(new[] { new SharpDX.Vector2(cx, cy) }); }
                        else if (C == 'V') { float y = N(); if (rel) y += cy; cy = y; if (cur != null) cur.Segs.Add(new[] { new SharpDX.Vector2(cx, cy) }); }
                        else if (C == 'C') { float a = N(), b = N(), c = N(), e = N(), x = N(), y = N(); if (rel) { a += cx; b += cy; c += cx; e += cy; x += cx; y += cy; } cx = x; cy = y; if (cur != null) cur.Segs.Add(new[] { new SharpDX.Vector2(a, b), new SharpDX.Vector2(c, e), new SharpDX.Vector2(x, y) }); }
                        else { i++; }   // unsupported command param → skip (prevents an infinite loop)
                    }
                    while (i < toks.Count && !isCmd(i) && cur != null);
                }
                return figs;
            }

            /// <summary>Rounded state pill with translucent fill + colored border + centered label. Returns its width.</summary>
            public float Pill(string label, float rightX, float y, SharpDX.Color4 col, float h = 18f)
            {
                float w = 16f + (label == null ? 0 : label.Length) * 7.2f;
                float x = rightX - w;
                var pill = new SharpDX.Direct2D1.RoundedRectangle { Rect = new SharpDX.RectangleF(x, y, w, h), RadiusX = h / 2f, RadiusY = h / 2f };
                _rt.FillRoundedRectangle(pill, B(col, 0.14f));
                _rt.DrawRoundedRectangle(pill, B(col, 0.5f), 1f);
                Text(label, x, y + 1.5f, w, h - 3f, col, 9.5f, true, SharpDX.DirectWrite.TextAlignment.Center);
                return w;
            }

            /// <summary>Hero money value: small currency mark right-aligned so it hugs the big number
            /// (kills the "+$  0" kerning gap). Big number is Light-weight.</summary>
            public void Money(float x, float y, double value, SharpDX.Color4 col, float bigSize = 36f, float markSize = 15f)
            {
                string mark = value >= 0 ? "+$" : "-$";
                _rt.DrawText(mark, MoneyFmt(markSize, SharpDX.DirectWrite.TextAlignment.Trailing),
                    new SharpDX.RectangleF(x - 2f, y + (bigSize - markSize) + 1f, 24f, markSize + 5f), B(col));
                _rt.DrawText(Math.Abs(value).ToString("N0"), MoneyFmt(bigSize, SharpDX.DirectWrite.TextAlignment.Leading),
                    new SharpDX.RectangleF(x + 24f, y - 3f, 220f, bigSize + 10f), B(col));
            }
            private SharpDX.DirectWrite.TextFormat MoneyFmt(float size, SharpDX.DirectWrite.TextAlignment align)
            {
                string key = "L" + size + (int)align;
                SharpDX.DirectWrite.TextFormat f;
                if (!_fmts.TryGetValue(key, out f))
                {
                    f = new SharpDX.DirectWrite.TextFormat(_dw, FSans, SharpDX.DirectWrite.FontWeight.Light, SharpDX.DirectWrite.FontStyle.Normal, size);
                    f.TextAlignment = align; f.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center;
                    _fmts[key] = f;
                }
                return f;
            }

            /// <summary>Rounded progress track (bg) + fill to frac[0..1].</summary>
            public void Track(float x, float y, float w, float frac, SharpDX.Color4 fill, float h = 6f)
            {
                frac = Math.Max(0f, Math.Min(1f, frac));
                _rt.FillRoundedRectangle(new SharpDX.Direct2D1.RoundedRectangle { Rect = new SharpDX.RectangleF(x, y, w, h), RadiusX = h / 2f, RadiusY = h / 2f }, B(CFaint));
                float fw = w * frac;
                if (fw > 3f) _rt.FillRoundedRectangle(new SharpDX.Direct2D1.RoundedRectangle { Rect = new SharpDX.RectangleF(x, y, fw, h), RadiusX = h / 2f, RadiusY = h / 2f }, B(fill));
            }

            /// <summary>Circular gauge: 240° track arc + a fill arc to frac[0..1]. Center (cx,cy), radius r.</summary>
            public void Gauge(float cx, float cy, float r, float frac, SharpDX.Color4 track, SharpDX.Color4 fill)
            {
                frac = Math.Max(0f, Math.Min(1f, frac));
                Arc(cx, cy, r, 150f, 390f, track, 5f);
                if (frac > 0.001f) Arc(cx, cy, r, 150f, 150f + 240f * frac, fill, 5f);
            }
            private void Arc(float cx, float cy, float r, float a0, float a1, SharpDX.Color4 col, float width)
            {
                var geo = new SharpDX.Direct2D1.PathGeometry(_rt.Factory);
                using (var sink = geo.Open())
                {
                    var p0 = new SharpDX.Vector2(cx + r * (float)Math.Cos(a0 * Math.PI / 180.0), cy + r * (float)Math.Sin(a0 * Math.PI / 180.0));
                    var p1 = new SharpDX.Vector2(cx + r * (float)Math.Cos(a1 * Math.PI / 180.0), cy + r * (float)Math.Sin(a1 * Math.PI / 180.0));
                    sink.BeginFigure(p0, SharpDX.Direct2D1.FigureBegin.Hollow);
                    sink.AddArc(new SharpDX.Direct2D1.ArcSegment {
                        Point = p1, Size = new SharpDX.Size2F(r, r),
                        SweepDirection = SharpDX.Direct2D1.SweepDirection.Clockwise,
                        ArcSize = (a1 - a0) > 180f ? SharpDX.Direct2D1.ArcSize.Large : SharpDX.Direct2D1.ArcSize.Small });
                    sink.EndFigure(SharpDX.Direct2D1.FigureEnd.Open);
                    sink.Close();
                }
                _frame.Add(geo);
                _rt.DrawGeometry(geo, B(col), width, _round);
            }

            /// <summary>Sparkline of values across (x,y,w,h) with an emphasized endpoint dot + faint area fill.</summary>
            public void Sparkline(float x, float y, float w, float h, IList<double> vals, SharpDX.Color4 col)
            {
                if (vals == null || vals.Count < 2) return;
                double min = double.MaxValue, max = double.MinValue;
                for (int i = 0; i < vals.Count; i++) { if (vals[i] < min) min = vals[i]; if (vals[i] > max) max = vals[i]; }
                double rng = max - min; if (rng < 1e-9) rng = 1;
                int n = vals.Count;
                Func<int, float> X = i => x + w * i / (float)(n - 1);
                Func<double, float> Y = v => y + h - (float)((v - min) / rng) * h;

                var geo = new SharpDX.Direct2D1.PathGeometry(_rt.Factory);
                using (var sink = geo.Open())
                {
                    sink.BeginFigure(new SharpDX.Vector2(X(0), Y(vals[0])), SharpDX.Direct2D1.FigureBegin.Hollow);
                    for (int i = 1; i < n; i++) sink.AddLine(new SharpDX.Vector2(X(i), Y(vals[i])));
                    sink.EndFigure(SharpDX.Direct2D1.FigureEnd.Open);
                    sink.Close();
                }
                _frame.Add(geo);
                _rt.DrawGeometry(geo, B(col), 1.5f, _round);
                _rt.FillEllipse(new SharpDX.Direct2D1.Ellipse(new SharpDX.Vector2(X(n - 1), Y(vals[n - 1])), 2.5f, 2.5f), B(col));
            }

            /// <summary>Hairline (defaults to the faint ink divider).</summary>
            public void Line(float x0, float y0, float x1, float y1, SharpDX.Color4 col, float width = 1f)
                => _rt.DrawLine(new SharpDX.Vector2(x0, y0), new SharpDX.Vector2(x1, y1), B(col), width);
            public void Divider(float x0, float y, float x1) => Line(x0, y, x1, y, Alpha(CInk, 0.06f), 1f);

            // ═══ Sentinel PLOT-SKIN primitives (chart-space; the sub-panel counterpart to the card) ═════════
            //  Draw these AFTER base.OnRender so the wash occludes NT's stock plot rendering, then paint the
            //  series yourself in card material. See Docs/SENTINEL_DESIGN_SYSTEM.md "Sub-panel plot standard".

            /// <summary>Fill an indicator sub-panel with the Sentinel glass wash (top→bottom navy gradient).
            /// Draw FIRST (after base.OnRender) so it covers the platform's flat panel + stock plots.</summary>
            public void PanelWash(float x, float y, float w, float h)
            {
                var gsc = new SharpDX.Direct2D1.GradientStopCollection(_rt, new[] {
                    new SharpDX.Direct2D1.GradientStop { Color = CWashTop, Position = 0f },
                    new SharpDX.Direct2D1.GradientStop { Color = CWashBot, Position = 1f } });
                _frame.Add(gsc);
                var g = new SharpDX.Direct2D1.LinearGradientBrush(_rt,
                    new SharpDX.Direct2D1.LinearGradientBrushProperties { StartPoint = new SharpDX.Vector2(x, y), EndPoint = new SharpDX.Vector2(x, y + h) }, gsc);
                _frame.Add(g);
                _rt.FillRectangle(new SharpDX.RectangleF(x, y, w, h), g);
            }

            /// <summary>A faint full-panel state wash (e.g. cyan when live, green/red by bias). Keep alpha low.</summary>
            public void RegimeShade(float x, float y, float w, float h, SharpDX.Color4 col, float alpha = 0.05f)
                => _rt.FillRectangle(new SharpDX.RectangleF(x, y, w, h), B(col, alpha));

            /// <summary>A themed reference/zero baseline (a touch stronger than a Divider).</summary>
            public void Baseline(float x0, float x1, float y, SharpDX.Color4 col)
                => Line(x0, y, x1, y, Alpha(col, 0.22f), 1f);

            /// <summary>A card-material histogram column from yZero to yVal at center cx (halfW each side):
            /// vertical gradient (bright at the tip → translucent at the base), soft-rounded ends, optional glow.</summary>
            public void HistoBar(float cx, float yZero, float yVal, float halfW, SharpDX.Color4 col, bool glow = false)
            {
                if (halfW < 0.6f) halfW = 0.6f;
                float top = Math.Min(yZero, yVal), bot = Math.Max(yZero, yVal);
                if (bot - top < 0.75f) return;
                float x = cx - halfW, w = halfW * 2f;
                float rad = Math.Min(halfW * 0.7f, (bot - top) * 0.5f);
                bool up = yVal <= yZero;                 // value above zero → tip at top
                float tipY = up ? top : bot, baseY = up ? bot : top;
                var gsc = new SharpDX.Direct2D1.GradientStopCollection(_rt, new[] {
                    new SharpDX.Direct2D1.GradientStop { Color = Alpha(col, 0.94f), Position = 0f },
                    new SharpDX.Direct2D1.GradientStop { Color = Alpha(col, 0.32f), Position = 1f } });
                _frame.Add(gsc);
                var g = new SharpDX.Direct2D1.LinearGradientBrush(_rt,
                    new SharpDX.Direct2D1.LinearGradientBrushProperties { StartPoint = new SharpDX.Vector2(cx, tipY), EndPoint = new SharpDX.Vector2(cx, baseY) }, gsc);
                _frame.Add(g);
                if (glow) _rt.FillRectangle(new SharpDX.RectangleF(x - 1.5f, top - 1.5f, w + 3f, (bot - top) + 3f), B(col, (IsLight ? 0.07f : 0.16f) * GlowMul));   // fainter halo on light; dimmer again on obsidian
                _rt.FillRoundedRectangle(new SharpDX.Direct2D1.RoundedRectangle { Rect = new SharpDX.RectangleF(x, top, w, bot - top), RadiusX = rad, RadiusY = rad }, g);
            }

            /// <summary>A polyline drawn as a soft glow underlay + a crisp stroke — for oscillator/reference lines.</summary>
            public void GlowLine(IList<SharpDX.Vector2> pts, SharpDX.Color4 col, float width = 1.6f, float glow = 0.18f)
            {
                if (pts == null || pts.Count < 2) return;
                var geo = new SharpDX.Direct2D1.PathGeometry(_rt.Factory);
                using (var sink = geo.Open())
                {
                    sink.BeginFigure(pts[0], SharpDX.Direct2D1.FigureBegin.Hollow);
                    for (int i = 1; i < pts.Count; i++) sink.AddLine(pts[i]);
                    sink.EndFigure(SharpDX.Direct2D1.FigureEnd.Open);
                    sink.Close();
                }
                _frame.Add(geo);
                // glow underlay: wide+soft on dark/silver; on light a wide translucent halo reads as MUD, so tighten it hard + fade
                if (glow > 0f) _rt.DrawGeometry(geo, B(col, (IsLight ? glow * 0.4f : glow) * GlowMul), width * (IsLight ? 1.8f : 3.2f), _round);
                _rt.DrawGeometry(geo, B(col), IsLight ? width * 1.12f : width, _round);   // nudge the crisp line up slightly on light to hold presence without the halo
            }

            /// <summary>Release per-frame gradients/geometries. Call at the end of OnRender.</summary>
            public void End()
            {
                // undo any card scale transform BEFORE anything else draws into the shared RenderTarget
                if (_scaled && _rt != null) { try { _rt.Transform = SharpDX.Matrix3x2.Identity; } catch { } _scaled = false; }
                for (int i = 0; i < _frame.Count; i++) { try { _frame[i].Dispose(); } catch { } }
                _frame.Clear();
            }

            /// <summary>Release cached brushes, text formats, stroke + the DirectWrite factory. Call in Terminated.</summary>
            public void Dispose()
            {
                End();
                foreach (var b in _brushes.Values) { try { b.Dispose(); } catch { } }
                _brushes.Clear();
                foreach (var f in _fmts.Values) { try { f.Dispose(); } catch { } }
                _fmts.Clear();
                if (_round != null) { try { _round.Dispose(); } catch { } _round = null; }
                if (_dw != null) { try { _dw.Dispose(); } catch { } _dw = null; }
                _rt = null;
            }
        }

    /// <summary>Shared card-stacking registry so cards from different Sentinel tools never overlap.
    /// Every card calls <see cref="Place"/> each frame with a stable key (usually <c>this</c>) and the
    /// panel bounds; cards docked to the same corner of the same panel stack vertically. Entries the
    /// caller stops rendering (hidden / removed) self-prune after ~2s; call <see cref="Release"/> in
    /// Terminated for immediate cleanup. All static + lock-guarded (OnRender is on the render thread).
    ///
    /// 2026-07-09 — THREE BUGS FIXED (see Docs/SENTINEL_RAIL_SPEC.md §1):
    ///  (1) ORDER DRIFT. Stack order was list order = registration order, and a card pruned after ~2s of not
    ///      rendering was re-APPENDED on return, so it migrated down its column. Order is now a STICKY ORDINAL
    ///      per (panel, type-name) that SURVIVES pruning — first-seen order, forever, exactly as it looks today.
    ///  (2) NO OVERFLOW HANDLING. `off` grew unbounded, so a column taller than the panel pushed its tail past
    ///      the edge and the card was SILENTLY LOST.
    ///  (3) CROSS-CORNER COLLISION. A corner knew nothing about the opposite corner on the SAME EDGE, so a long
    ///      TopRight sensor stack grew straight through the BottomRight-anchored Deck card and buried it.
    ///      (This is why (2) shows up as an OVERLAP when an opposite anchor exists, and as a LOST CARD when not.)
    ///
    /// POLICY: **top columns yield to bottom columns, never the reverse.** Bottom-anchored cards are the
    /// operator/risk cards (the Deck's P&amp;L + governor); the top column is the sensor stack that grows without
    /// bound. Burying the risk card is strictly worse than shrinking a sensor. A top column therefore reserves the
    /// opposite bottom column's height (capped at 60% of the panel) and fits itself into what remains, in order of
    /// least harm: **compress the gap → scale to <see cref="MinCardScale"/> → COLLAPSE the tail to chips.**
    ///
    /// **Nothing is ever hidden.** A collapsed card keeps a 22px chip carrying its name and a live dot, so the
    /// operator can always see it is present and rendering; give the column room and it re-expands on its own.
    /// **Pinned cards neither scale nor collapse** — a card that owns a control (the Bridge's ARM button) must
    /// never become unreachable, and a risk readout (the Deck) must never become unreadable. The collapsed count
    /// is exposed via <see cref="OverflowCount"/> and logged (throttled) to sentinel.log.</summary>
    public static class CardLayout
    {
        private sealed class Slot
        {
            public object Key; public SentinelCardCorner Corner; public float H; public int Seen;
            public int Ord; public bool Pinned; public string Name;
            /// <summary>Monotonic per-instance tiebreak. Two instances of the SAME tool share an `Ord` (it is keyed by
            /// type name), and List.Sort is UNSTABLE — tied slots would swap every frame and the collapse victim
            /// would change identity. Sorting by (Ord, Seq) makes the column order total and stable.</summary>
            public int Seq;
            // last placed geometry + how Painter.Card() should draw it (see CardStyle)
            public float X, Y, W, Scale = 1f, Cx, Cy;
            public bool Collapsed;   // draw a chip instead of the card; contents are translated off-screen
        }

        /// <summary>One column's layout verdict, computed ONCE and reused by every card in that column.
        /// Recomputing it inside each of the 19 callers, every frame, made the layout oscillate: any wobble
        /// (a slot pruned at the 2s stale edge, a card whose height moves a pixel) flipped "fits"/"doesn't fit"
        /// and the chart flashed. Decide once, cache, and apply HYSTERESIS.</summary>
        private sealed class Decision
        {
            public int At;                 // TickCount when computed
            public int Count;              // column membership size, so add/remove forces a recompute
            public float Scale = 1f, Gap;
            public readonly List<Slot> Collapsed = new List<Slot>();
        }
        private static readonly Dictionary<object, Decision[]> _decisionByPanel = new Dictionary<object, Decision[]>();
        private static int _seqCounter;

        private static readonly Dictionary<object, List<Slot>> _byPanel = new Dictionary<object, List<Slot>>();
        /// <summary>(panel → type-name → first-seen ordinal). Survives slot pruning — this is what kills order drift.</summary>
        private static readonly Dictionary<object, Dictionary<string, int>> _ordByPanel = new Dictionary<object, Dictionary<string, int>>();
        private static readonly Dictionary<object, int[]> _overflowByPanel = new Dictionary<object, int[]>();
        private static readonly Dictionary<object, int[]> _lastLogByPanel  = new Dictionary<object, int[]>();
        private static readonly Dictionary<object, string[]> _sigByPanel   = new Dictionary<object, string[]>();
        private static readonly object _lock = new object();

        /// <summary>How long a slot survives without re-registering before it is pruned.
        ///
        /// ⚠ 2000ms WAS THE FLICKER (2026-07-10). A card registers when ITS OnRender runs, and NT paints all of a
        /// panel's indicators in one pass — so the FIRST card to render in a pass sees every other card's timestamp
        /// from the PREVIOUS paint. On a quiet chart the paint interval reaches 1.5–2.0s (measured: ages of 1937ms
        /// against a 2000ms threshold), so the first caller would EVICT ITS WHOLE COLUMN: n=4 → n=1, the Deck's
        /// reserve vanished, budget jumped 447 → 642px, every card expanded — then they all re-registered in the
        /// same pass and it collapsed again. The prune clock was implicitly assuming a 60fps repaint.
        ///
        /// 10s is comfortably above any repaint interval while still retiring a genuinely removed card promptly.
        /// A slot that stops rendering keeps its column's space for up to 10s; that is the correct trade.</summary>
        private const int   StaleMs        = 10000;
        private const float MinGap         = 2f;     // compress the inter-card gap this far before hiding anything
        private const float MaxOppositeRes = 0.60f;  // a bottom column may reserve at most this share of the panel
        private const int   LogThrottleMs  = 30000;
        /// <summary>Re-decide a column's layout at most this often. Layout does not need to track the frame rate,
        /// and deciding every frame is exactly what let it oscillate.</summary>
        private const int   RecomputeMs    = 400;
        /// <summary>A collapsed card must fit with this much room to SPARE before it is allowed to expand again.
        /// Without it, a card sitting exactly on the fit boundary collapses → fits → expands → doesn't fit → …
        /// forever, which is the flicker. Asymmetric on purpose: cheap to collapse, expensive to expand.</summary>
        private const float ExpandHysteresisPx = 18f;

        /// <summary>Layout tracing. Logs each column's budget/scale/collapsed-set + every slot's height and AGE,
        /// throttled 1s per (panel, corner). It is what found the flicker: the ages exposed that the first card to
        /// render in a paint pass was evicting its whole column. Flip to true when layout misbehaves.</summary>
        public static bool DebugLayout = false;

        /// <summary>KILL SWITCH — drop a file named `Sentinel\layout.off` and the whole adaptive layout (scale +
        /// collapse + the opposite-column reserve) goes back to plain stacking within 2s: no transforms, no chips,
        /// nothing to flicker. Costs you the overlap fixes; buys you a still chart while a bug is being hunted.
        /// Polled cheaply (≤2s) from Place, on the render thread.</summary>
        private static bool _layoutOff;
        private static int  _layoutOffChecked;
        private static bool LayoutDisabled(int now)
        {
            if (unchecked(now - _layoutOffChecked) > 2000)
            {
                _layoutOffChecked = now;
                try
                {
                    _layoutOff = System.IO.File.Exists(System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        "NinjaTrader 8", "Sentinel", "layout.off"));
                }
                catch { }
            }
            return _layoutOff;
        }
        private static readonly Dictionary<int, int> _dbgStamp = new Dictionary<int, int>();

        /// <summary>Last resort only. When a column still cannot fit after gap-compression AND scale-to-fit at
        /// <see cref="MinCardScale"/>, COLLAPSE its tail to chips (never a pinned card) — a collapsed card keeps a
        /// 22px chip in the column showing its name + live dot, so it is reduced, never *lost*. Collapse is
        /// self-correcting: give the column room and the card re-expands on its own.
        /// <c>false</c> = legacy overflow, which buries the opposite corner's card.</summary>
        public static bool CollapseOnOverflow = true;

        /// <summary>Height of a collapsed card's chip.</summary>
        public const float ChipH = 22f;
        /// <summary>A chip never grows wider than this, so a 505px card (the Eye) collapses to a normal-looking pill.</summary>
        public const float ChipMaxWidth = 200f;

        /// <summary>How small a card may be squeezed before we give up and hide something instead. Cards are drawn
        /// through a Direct2D scale transform, so text stays vector-crisp — but there is a LEGIBILITY floor, not a
        /// rendering one. 0.80 keeps a 9px micro-label at ~7.2px. Pinned cards NEVER scale (their controls are
        /// hit-tested in untransformed screen coords), so their height is reserved at full size.</summary>
        public static float MinCardScale = 0.80f;

        /// <summary>Reserve/refresh this card's slot and return its outer rect, auto-stacked within its
        /// corner. <paramref name="key"/> = stable per-card identity (pass <c>this</c>). <paramref name="panelKey"/>
        /// groups cards on the same chart panel (pass the ChartPanel). margin = gap to the chart edge; gap =
        /// spacing between stacked cards. <paramref name="pinned"/> = this card owns a CONTROL (e.g. the Bridge's
        /// ARM button) or is a risk readout (the Deck), so overflow must never scale or collapse it — it stays
        /// reachable and readable even on a short panel.
        /// An overflowed card is COLLAPSED, not hidden: Painter.Card() draws a chip at its slot and translates the
        /// card's own contents off-screen, so no caller needs to know it was collapsed.</summary>
        public static SharpDX.RectangleF Place(object key, object panelKey,
            float panelX, float panelY, float panelW, float panelH,
            SentinelCardCorner corner, float w, float h, float margin = 12f, float gap = 8f,
            bool pinned = false)
        {
            lock (_lock)
            {
                if (panelKey == null) panelKey = _lock;   // fallback bucket
                List<Slot> slots;
                if (!_byPanel.TryGetValue(panelKey, out slots)) { slots = new List<Slot>(); _byPanel[panelKey] = slots; }
                Dictionary<string, int> ord;
                if (!_ordByPanel.TryGetValue(panelKey, out ord)) { ord = new Dictionary<string, int>(); _ordByPanel[panelKey] = ord; }

                int now = Environment.TickCount;
                // prune cards that stopped rendering (hidden/removed) — but never the caller itself.
                // NOTE: the ORDINAL map is deliberately NOT pruned, so a card that comes back keeps its place.
                slots.RemoveAll(s => !ReferenceEquals(s.Key, key) && unchecked(now - s.Seen) > StaleMs);

                Slot me = null;
                for (int i = 0; i < slots.Count; i++) if (ReferenceEquals(slots[i].Key, key)) { me = slots[i]; break; }
                if (me == null)
                {
                    me = new Slot { Key = key, Name = TypeLabel(key), Seq = ++_seqCounter };
                    int o;
                    if (!ord.TryGetValue(me.Name, out o)) { o = ord.Count; ord[me.Name] = o; }
                    me.Ord = o;
                    slots.Add(me);
                }
                me.Corner = corner; me.H = h; me.Seen = now;
                if (pinned) me.Pinned = true;   // sticky: a card only ever becomes MORE protected

                bool right  = corner == SentinelCardCorner.TopRight || corner == SentinelCardCorner.BottomRight;
                bool bottom = corner == SentinelCardCorner.BottomRight || corner == SentinelCardCorner.BottomLeft;

                // KILL SWITCH: `Sentinel\layout.off` → plain stacking, exactly as before scale/collapse existed.
                // No transform is ever armed (CardStyle can't match a slot whose Scale is 1 and Collapsed false),
                // so nothing can flicker. Cards may overlap again; that is the price of a still chart.
                if (LayoutDisabled(now))
                {
                    float offL = 0f;
                    for (int i = 0; i < slots.Count; i++)
                    {
                        Slot s = slots[i];
                        if (ReferenceEquals(s, me)) break;
                        if (s.Corner == corner) offL += s.H + gap;
                    }
                    me.Scale = 1f; me.Collapsed = false; me.W = 0f;   // W=0 ⇒ CardStyle never matches ⇒ no transform
                    float xl = right  ? (panelX + panelW - w - margin) : (panelX + margin);
                    float yl = bottom ? (panelY + panelH - h - margin - offL) : (panelY + margin + offL);
                    return new SharpDX.RectangleF(xl, yl, w, h);
                }

                // ── (3) reserve the opposite column on this EDGE. Top yields to bottom; bottom never yields. ──
                // (Reserving is pointless in legacy mode: without collapse, the column just overflows through it.)
                float reserve = 0f;
                if (!bottom && CollapseOnOverflow)
                {
                    var oppCorner = right ? SentinelCardCorner.BottomRight : SentinelCardCorner.BottomLeft;
                    float hOpp = ColumnHeight(slots, oppCorner, gap);
                    if (hOpp > 0f) reserve = Math.Min(hOpp, panelH * MaxOppositeRes) + gap;
                }
                float budget = panelH - 2f * margin - reserve;
                if (budget < 0f) budget = 0f;

                // ── the ordered column. (Ord, Seq) is a TOTAL order: Ord is keyed by type name, so two instances of
                // the same tool tie — and List.Sort is unstable, which would swap them between frames. ──
                var col = new List<Slot>();
                for (int i = 0; i < slots.Count; i++) if (slots[i].Corner == corner) col.Add(slots[i]);
                col.Sort((a, b) => a.Ord != b.Ord ? a.Ord.CompareTo(b.Ord) : a.Seq.CompareTo(b.Seq));

                // ── decide ONCE per column (throttled + hysteresis), not once per caller per frame ──
                Decision d = Decide(panelKey, corner, col, budget, gap, now);
                float g = d.Gap, scale = d.Scale;
                var collapsed = d.Collapsed;

                // stack offset = the ON-SCREEN height (chip or scaled card) of same-corner cards ordered before me
                float off = 0f;
                for (int i = 0; i < col.Count; i++)
                {
                    Slot s = col[i];
                    if (ReferenceEquals(s, me)) break;
                    off += (collapsed.Contains(s) ? ChipH : s.H * (s.Pinned ? 1f : scale)) + g;
                }

                bool meCollapsed = collapsed.Contains(me);
                float mine = meCollapsed || me.Pinned ? 1f : scale;
                float onScreenH = meCollapsed ? ChipH : h * mine;
                float x = right  ? (panelX + panelW - w - margin) : (panelX + margin);
                // bottom-anchored cards grow upward, so they position off their ON-SCREEN height, not the natural one
                float y = bottom ? (panelY + panelH - onScreenH - margin - off) : (panelY + margin + off);

                // Scale about the ANCHORED corner so the card stays flush with the margin it docks to.
                me.X = x; me.Y = y; me.W = w; me.H = h; me.Scale = mine; me.Collapsed = meCollapsed;
                me.Cx = right ? x + w : x;
                me.Cy = bottom ? y + onScreenH : y;
                return new SharpDX.RectangleF(x, y, w, h);
            }
        }

        /// <summary>How many cards are currently COLLAPSED to chips by overflow in this (panel, corner).
        /// 0 = everything fits at full size.</summary>
        public static int OverflowCount(object panelKey, SentinelCardCorner corner)
        {
            lock (_lock)
            {
                int[] counts;
                if (panelKey != null && _overflowByPanel.TryGetValue(panelKey, out counts)) return counts[(int)corner];
                return 0;
            }
        }

        /// <summary>Decide a column's gap / scale / collapsed-set ONCE, then reuse it for every card in that column
        /// until it is stale (<see cref="RecomputeMs"/>) or the membership changes.
        ///
        /// ⚠ THIS EXISTS BECAUSE THE FIRST VERSION FLICKERED. It recomputed inside all 19 `Place()` calls, every
        /// frame, from scratch. Sitting on the fit boundary, a column would collapse a card (→ fits) then expand it
        /// (→ doesn't fit) on the next frame, forever; and because callers disagreed, the *victim's identity* changed
        /// too (sentinel.log showed TopRight alternating CompressionBase ⇄ SentinelGodReversal). Two fixes:
        ///   • THROTTLE — layout does not need to track the frame rate.
        ///   • HYSTERESIS — collapse is cheap, expansion demands <see cref="ExpandHysteresisPx"/> of spare room.
        /// Collapse is therefore STICKY: it stays collapsed until the column genuinely has room to spare.</summary>
        private static Decision Decide(object panelKey, SentinelCardCorner corner, List<Slot> col,
            float budget, float gap, int now)
        {
            Decision[] byCorner;
            if (!_decisionByPanel.TryGetValue(panelKey, out byCorner)) { byCorner = new Decision[4]; _decisionByPanel[panelKey] = byCorner; }
            Decision d = byCorner[(int)corner];
            if (d == null) { d = new Decision { At = now - RecomputeMs - 1, Count = -1 }; byCorner[(int)corner] = d; }

            bool stale = unchecked(now - d.At) >= RecomputeMs || d.Count != col.Count;
            if (!stale)
            {
                // membership can change without the count changing (one card swapped for another) — verify cheaply
                for (int i = 0; i < d.Collapsed.Count; i++)
                    if (!col.Contains(d.Collapsed[i])) { stale = true; break; }
            }
            if (!stale) return d;

            d.At = now; d.Count = col.Count;
            // Start from the PREVIOUS verdict (minus any card that has since left) — this is what makes it sticky.
            d.Collapsed.RemoveAll(s => !col.Contains(s) || s.Pinned);

            // 1. try to EXPAND collapsed cards, earliest-first, but only with room to spare (hysteresis)
            for (int i = 0; i < d.Collapsed.Count; i++)
            {
                Slot cand = d.Collapsed[i];
                d.Collapsed.RemoveAt(i);
                float tg = FitGap(col, d.Collapsed, gap, budget);
                float ts = ScaleToFit(col, d.Collapsed, tg, budget);
                if (Needed(col, d.Collapsed, tg, ts) <= budget - ExpandHysteresisPx) { i--; continue; }   // keep it expanded
                d.Collapsed.Insert(i, cand);                                                              // put it back
            }

            // 2. COLLAPSE the tail while the column still overflows (never a pinned card)
            d.Gap = FitGap(col, d.Collapsed, gap, budget);
            d.Scale = ScaleToFit(col, d.Collapsed, d.Gap, budget);
            for (int guard = col.Count + 1; guard > 0; guard--)
            {
                if (Needed(col, d.Collapsed, d.Gap, d.Scale) <= budget + 0.5f) break;   // fits
                if (!CollapseOnOverflow) break;                                          // legacy: let it overflow
                Slot victim = null;
                for (int i = col.Count - 1; i >= 0; i--)
                {
                    Slot s = col[i];
                    if (!s.Pinned && !d.Collapsed.Contains(s)) { victim = s; break; }
                }
                if (victim == null) break;   // only pinned cards left — let them overlap rather than vanish
                d.Collapsed.Add(victim);
                // with one card chipped the survivors may now fit at a LARGER scale — recompute, don't settle
                d.Gap = FitGap(col, d.Collapsed, gap, budget);
                d.Scale = ScaleToFit(col, d.Collapsed, d.Gap, budget);
            }

            NoteOverflow(panelKey, corner, d.Collapsed, now);
            DebugTrace(panelKey, corner, col, d, budget, now);
            return d;
        }

        /// <summary>Trace exactly what each column decided, and WHICH panel object decided it. The flicker survived
        /// two plausible fixes; guessing a third time is not a plan. Throttled 1s per (panel, corner).</summary>
        private static void DebugTrace(object panelKey, SentinelCardCorner corner, List<Slot> col, Decision d, float budget, int now)
        {
            if (!DebugLayout) return;
            try
            {
                int pid = panelKey == null ? 0 : panelKey.GetHashCode();
                int stampKey = pid * 4 + (int)corner;
                int last;
                if (_dbgStamp.TryGetValue(stampKey, out last) && unchecked(now - last) < 1000) return;
                _dbgStamp[stampKey] = now;

                var sb = new System.Text.StringBuilder();
                sb.Append("panel#").Append(pid).Append(' ').Append(corner.ToString())
                  .Append(" budget=").Append((int)budget)
                  .Append(" gap=").Append((int)d.Gap)
                  .Append(" scale=").Append((int)(d.Scale * 100f))
                  .Append(" n=").Append(col.Count).Append(" [");
                for (int i = 0; i < col.Count; i++)
                {
                    if (i > 0) sb.Append(' ');
                    sb.Append(col[i].Name == null ? "?" : col[i].Name).Append(':').Append((int)col[i].H);
                    sb.Append('@').Append(unchecked(now - col[i].Seen));   // slot age (ms) — >2000 means it is about to be PRUNED
                    if (col[i].Pinned) sb.Append('*');
                    if (d.Collapsed.Contains(col[i])) sb.Append("=CHIP");
                }
                sb.Append(']');
                SentinelCore.Log("LayoutTrace", sb.ToString());
            }
            catch (Exception ex)
            {
                // a silent catch is exactly how this tracer hid itself the first time — SAY something
                try { SentinelCore.Log("LayoutTrace", "TRACER FAILED: " + ex.GetType().Name + " " + ex.Message); } catch { }
            }
        }

        /// <summary>Compress the inter-card gap (free) until the column fits at full scale, or we hit MinGap.</summary>
        private static float FitGap(List<Slot> col, List<Slot> collapsed, float gap, float budget)
        {
            float g = gap;
            while (Needed(col, collapsed, g, 1f) > budget && g > MinGap) g = Math.Max(MinGap, g - 1f);
            return g;
        }

        /// <summary>Total stacked height of a corner's cards (used to reserve the opposite column).</summary>
        private static float ColumnHeight(List<Slot> slots, SentinelCardCorner corner, float gap)
        {
            float total = 0f; int n = 0;
            for (int i = 0; i < slots.Count; i++) if (slots[i].Corner == corner) { total += slots[i].H; n++; }
            return n == 0 ? 0f : total + gap * (n - 1);
        }

        /// <summary>Stacked height of an ordered column at gap <paramref name="g"/> and card scale <paramref name="s"/>.
        /// COLLAPSED cards cost only a chip; PINNED cards are counted at full size — they never scale or collapse.</summary>
        private static float Needed(List<Slot> col, List<Slot> collapsed, float g, float s)
        {
            float total = 0f;
            for (int i = 0; i < col.Count; i++)
            {
                Slot c = col[i];
                total += collapsed.Contains(c) ? ChipH : (c.Pinned ? c.H : c.H * s);
            }
            return col.Count == 0 ? 0f : total + g * (col.Count - 1);
        }

        /// <summary>The largest card scale ≤ 1 that fits this column into <paramref name="budget"/>, clamped at the
        /// legibility floor. Pinned height, chips and gaps are fixed costs; only the full cards absorb the squeeze.</summary>
        private static float ScaleToFit(List<Slot> col, List<Slot> collapsed, float g, float budget)
        {
            float hFixed = 0f, hScalable = 0f;
            for (int i = 0; i < col.Count; i++)
            {
                Slot c = col[i];
                if (collapsed.Contains(c)) hFixed += ChipH;
                else if (c.Pinned)         hFixed += c.H;
                else                       hScalable += c.H;
            }
            if (hScalable <= 0f) return 1f;
            float avail = budget - hFixed - g * (col.Count - 1);
            float s = avail / hScalable;
            if (s > 1f) s = 1f;
            if (s < MinCardScale) s = MinCardScale;
            return s;
        }

        /// <summary>Painter.Card() asks: "for the rect I was just handed, how should I draw it?" — full size, scaled,
        /// or collapsed to a chip. Matched by GEOMETRY because Painter has no card key: every Sentinel card passes
        /// the exact rect Place() returned. Returns false (draw at 1:1) for pinned, unscaled or non-Sentinel rects.</summary>
        internal static bool CardStyle(float x, float y, float w, float h,
            out float scale, out float cx, out float cy, out bool collapsed, out string label)
        {
            scale = 1f; cx = x; cy = y; collapsed = false; label = null;
            lock (_lock)
            {
                foreach (var kv in _byPanel)
                {
                    List<Slot> slots = kv.Value;
                    for (int i = 0; i < slots.Count; i++)
                    {
                        Slot s = slots[i];
                        if (s.W <= 0f) continue;
                        if (!s.Collapsed && s.Scale >= 0.999f) continue;      // full size → nothing to do
                        if (Math.Abs(s.X - x) > 0.5f || Math.Abs(s.Y - y) > 0.5f) continue;
                        if (Math.Abs(s.W - w) > 0.5f || Math.Abs(s.H - h) > 0.5f) continue;
                        scale = s.Scale; cx = s.Cx; cy = s.Cy; collapsed = s.Collapsed; label = s.Name;
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>"Council_v1_0_0" → "Council". The on-chart Name is unusable here (the label-remover blanks it).</summary>
        private static string TypeLabel(object key)
        {
            if (key == null) return "?";
            string n = key.GetType().Name;
            int i = n.LastIndexOf("_v", StringComparison.Ordinal);
            if (i > 0) n = n.Substring(0, i);
            return n;
        }

        /// <summary>Record + (throttled) LOG the collapsed cards. Nothing collapses silently.
        /// The signature compares NAMES, not just the count — a same-size set with a different victim is exactly the
        /// oscillation that flickered the chart, and a count-only check would have hidden it.</summary>
        private static void NoteOverflow(object panelKey, SentinelCardCorner corner, List<Slot> collapsed, int now)
        {
            int[] counts;
            if (!_overflowByPanel.TryGetValue(panelKey, out counts)) { counts = new int[4]; _overflowByPanel[panelKey] = counts; }
            counts[(int)corner] = collapsed.Count;

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < collapsed.Count; i++) { if (i > 0) sb.Append(", "); sb.Append(collapsed[i].Name); }
            string sig = sb.ToString();

            string[] sigs;
            if (!_sigByPanel.TryGetValue(panelKey, out sigs)) { sigs = new string[4]; _sigByPanel[panelKey] = sigs; }
            if (sig == (sigs[(int)corner] ?? "")) return;   // unchanged → say nothing
            sigs[(int)corner] = sig;
            if (collapsed.Count == 0) return;               // recovered; no need to announce

            int[] stamps;
            if (!_lastLogByPanel.TryGetValue(panelKey, out stamps)) { stamps = new int[4]; _lastLogByPanel[panelKey] = stamps; }
            if (stamps[(int)corner] != 0 && unchecked(now - stamps[(int)corner]) < LogThrottleMs) return;
            stamps[(int)corner] = now;

            try { SentinelCore.Log("CardLayout", corner + ": panel too short — " + collapsed.Count + " card(s) collapsed to chips: " + sig); } catch { }
        }

        /// <summary>Drop this card from every panel bucket (call in Terminated).</summary>
        public static void Release(object key)
        {
            lock (_lock)
            {
                foreach (var kv in _byPanel) kv.Value.RemoveAll(s => ReferenceEquals(s.Key, key));
                // a cached Decision may still reference the dead slot — drop it so the column re-decides
                foreach (var kv in _decisionByPanel)
                    for (int c = 0; c < 4; c++)
                        if (kv.Value[c] != null) { kv.Value[c].Collapsed.RemoveAll(s => ReferenceEquals(s.Key, key)); kv.Value[c].Count = -1; }
                // sweep now-empty panel buckets so dead panels don't accumulate
                var dead = new List<object>();
                foreach (var kv in _byPanel) if (kv.Value.Count == 0) dead.Add(kv.Key);
                for (int i = 0; i < dead.Count; i++)
                {
                    _byPanel.Remove(dead[i]);
                    _ordByPanel.Remove(dead[i]);        // the panel is gone — its ordinal map goes with it
                    _overflowByPanel.Remove(dead[i]);
                    _lastLogByPanel.Remove(dead[i]);
                    _sigByPanel.Remove(dead[i]);
                    _decisionByPanel.Remove(dead[i]);
                }
            }
        }
    }
    }

    /// <summary>Which chart corner a Sentinel card docks to. Cards in the SAME corner auto-stack.</summary>
    public enum SentinelCardCorner { TopRight, TopLeft, BottomRight, BottomLeft }
}
