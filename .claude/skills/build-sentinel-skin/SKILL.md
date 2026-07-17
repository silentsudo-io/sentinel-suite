---
name: build-sentinel-skin
description: Add a new THEME/skin to the Sentinel suite end-to-end — the on-chart palette AND the platform skin — so one switch re-colors every card, plot, the wallpaper, and the NT chrome. Use whenever the user asks to "build/add a Sentinel theme/skin", names a new theme (e.g. "Neon", "Viridian"), or wants to restyle the suite's palette. Needs SentinelSkin (src/runtime/AddOns) + the src/skins/themes/Sentinel* folders present.
---

# Build a Sentinel skin/theme

A Sentinel theme is a CENTRAL swap: every on-chart pixel funnels through `SentinelSkin` `C*`/`K*` tokens, so a
theme = one `Palette` + a few registrations. The platform chrome is a separate 16-file skin folder. Full background:
`docs/SENTINEL_DESIGN_SYSTEM.md` §1/§1b.

> Files referenced below live in this repo: `src/runtime/AddOns/SentinelSkin.cs` · `src/skins/Indicators/SentinelWallpaper_v1_0_0.cs`
> · the theme folders under `src/skins/themes/Sentinel *`. If you're building against the full Sentinel suite, the
> **Deck** (manual trader) also carries a theme-cycle button — its step below is marked *optional* because the Deck
> isn't part of this public cut.

## THE ONE LAW (design gate — check BEFORE picking colors)
**ONE accent = live/watching; green/red = money + direction.** The accent need NOT be cyan (Amber = gold, Neon =
violet), but before committing an accent, check it against **Warn AND Up AND Down** — *live and caution must never
share a hue, and the accent must never rhyme with the money colors.* (Amber's gold accent forced Warn from amber →
cool blue. A proposed "Viridian" was rejected: its accent rhymed with Up-green.) On a near-black ground, **LIFT the
ink** (bright text needs less weight, not more) and tune **`GlowMul`** (neon cranks it ~1.5; Obsidian dims it 0.6;
light bakes its glow-down into the `IsLight` branches).

## PART A — the on-chart theme (4 code places + 2 UI). Files: `src/runtime/AddOns/SentinelSkin.cs`, `src/skins/Indicators/SentinelWallpaper_v1_0_0.cs` (+ the Deck, if present)
This alone is **previewable** via `Sentinel\theme.txt` = `<name>` + F5 (a pin BEATS the skin — see the glue note).

1. **`Palette <Name>`** — add a `public static readonly Palette` beside the others in `SentinelSkin.cs`. Set ALL of:
   `Void Panel Card Line Dim Faint Ink Ink2 Mute Accent Up Down Warn`, the `GlassTop/GlassBot` + `WashTop/WashBot`
   gradient stops, and **`GlowMul`** (⚠ it's a class field → an unset Palette defaults to **0** = no glow; every
   Palette MUST set it). Colors via `RC(r,g,b)` / `RC(r,g,b,a)`. The `K*` WPF-Color accessors auto-derive from the
   active Palette — no per-theme K* work.
2. **`enum Theme`** — append `<Name>`.
3. **`TryParseTheme`** — add `case "<name>": t = Theme.<Name>; return true;`.
4. **`SkinBgTheme`** — add `{ 0x<CHARTBG>u, Theme.<Name> }`. This is the GLUE: it maps the platform skin's EXACT
   `ChartControl.ChartBackground` hex → theme (luminance CANNOT separate two dark themes, so exact-hex first). Keep
   this hex identical to the skin folder's `ChartControl.xaml` (both sides carry a comment).
5. **Wallpaper** (`SentinelWallpaper_v1_0_0.cs` `ThemeGhostScale`): add a `case Theme.<Name>: return <k>;` (near-black
   grounds need ~1.1–1.25; light ~0.9).
6. **(Optional — only if you have the Deck)** Deck theme cycle: append the mode word to `ThemeModes` and a 1-char
   face to `ThemeGlyphs` (keep the arrays equal length; "auto" is `~` because it means FOLLOW THE SKIN).
   `TryParseTheme` is the only word→Theme map — no other Deck logic changes.

## PART B — the platform skin folder (16 files). `src/skins/themes/Sentinel <Name>\`
Gives full cohesion (price panel + NT chrome). Two toggles until glue: Tools▸Options▸General▸Skin = "Sentinel <Name>"
(often needs a RESTART) + `theme.txt`=auto so the on-chart follows. (To install: copy the folder into your NinjaTrader
`templates\Skins\` directory.)

1. **Copy the closest existing theme** (a DARK-side theme → copy `Sentinel Obsidian` or `Sentinel`; a LIGHT theme →
   copy `Sentinel Light`). 16 files: ChartControl, ControlCenter, SuperDom, OrderTicket, DataSeries, Level2,
   MarketAnalyzer, News, TimeAndSales, AtmGrouping, BasicEntry, FxBoard, FxPro, NinjaScriptWizard, OptionChain,
   BluePrint (the last is NT's 564-key master palette — unrelated to the Blueprint theme).
2. **Role-based hex-CORE sed map** on the 15 non-ChartControl files: lighten/darken GROUNDS toward the new theme,
   move BORDERS, deepen/shift ACCENTS. **Preserve alpha** (map the 6-hex core, keep the `#AARRGGBB` prefix). Dark
   themes KEEP text light (no darken); light themes darken text.
3. **Hand-write `ChartControl.xaml`**: price-panel bg = the exact `SkinBgTheme` hex; text ink; the accent; **gridlines
   are the trap** — the role sed usually lands them ON the ground (invisible) → hand-LIFT them a step above the panel
   (the drafting grid is a theme signature). Axis pens tuned; keep the user's candle colors unless asked; keep the
   deep buy/sell button tints.
4. **Balance every file** — each is a `ResourceDictionary`; a stray/duplicate key breaks skin load silently.

## VERIFY
- Compile Part A via NT's own compiler (F5 in the NinjaScript Editor, or `nt8bridge compile` if you use the
  cli-nt-bridge tool). ⚠ Strip each edited indicator's generated region to 0 first (external edits re-append it).
  AddOns (SentinelSkin) have no generated region.
- Live: pin `theme.txt`=`<name>` + F5 → on-chart preview (cards/plots/wallpaper). Then select the skin +
  `theme.txt`=auto → the whole platform + on-chart follow via the glue. **When a skin switch seems not to re-theme,
  CHECK theme.txt FIRST** — a pin beats the skin (back up the old value to `theme.txt.bak`).
- ⚠ Grep any tool you touched for hardcoded `Color.FromRgb(` / `Color4(` / `RC(` outside SentinelSkin — a surface
  with its own palette won't follow.

## The extension contract (the whole point)
Consumers read `CAccent`/`K*` and are UNTOUCHED — so a new theme is additive, never a per-file hunt. Seven themes
(Dark/Light/Silver/Obsidian/Blueprint/Amber/Neon) prove it holds. Adding one should be a single clean pass.
