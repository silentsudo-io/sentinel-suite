# Sentinel Suite — Design System & Build Framework

**The single source of truth for building any Sentinel tool so it stays visually seamless and
architecturally consistent.** Read this before creating or restyling a Sentinel indicator,
strategy, AddOn, or dashboard tab. Update it whenever a convention changes — it is a *living*
spec, not a snapshot.

> Proven across: `SentinelDashboard`, `GTrader21` panel + on-chart risk card, `SentinelDeck`.
> When a fourth tool needs something new, add the pattern here first, then build to it.

---

## 0. The one rule

> **Cyan is the only accent, and it means LIVE / watching / active.**
> **Green and red are reserved for money + direction (P&L, long/short, buy/sell).**
> Everything else is a blue-biased neutral. Amber = caution/advisory. That's the whole language.

If you're reaching for a second accent color, stop — restraint *is* the identity.

---

## 1. Palette tokens (exact hex — never approximate)

| Token | Hex | RGB | Use |
|---|---|---|---|
| `void`   | `#0A0E17` | 10,14,23   | app/window ground, deepest bg |
| `panel`  | `#111726` | 17,23,38   | panel surface, caption bar |
| `card2`  | `#0E1420` | 14,20,32   | deeper card / gradient bottom |
| `line`   | `#1E2A3D` | 30,42,61   | hairline borders, dividers |
| `dim`    | `#1B2536` | 27,37,54   | control / button idle bg |
| `faint`  | `#26344C` | 38,52,76   | track bg, gridlines |
| `ink`    | `#E9EEF7` | 233,238,247| primary text |
| `ink2`   | `#AEBACE` | 174,186,206| labels, secondary text |
| `mute`   | `#6C7A92` | 108,122,146| captions, idle/muted |
| **`accent`** | `#3FD1E0` | 63,209,224 | **cyan — LIVE/watching (the one accent)** |
| **`up`**     | `#25D08B` | 37,208,139 | **green — money up / long / buy** |
| **`down`**   | `#FF5C6A` | 255,92,106 | **red — money down / short / sell** |
| `warn`   | `#F2B34C` | 242,179,76 | amber — caution / advisory breach |

Deep button tints for Buy/Sell surfaces (order buttons): **Buy** `#1C8F63`→`#125C41`, **Sell** `#B84550`→`#7E2C34`.

**Translucent recipes** (WPF `Color.FromArgb` / SharpDX alpha):
- Cyan selection wash: `#553FD1E0` (33% alpha).
- Tinted pill bg: accent at ~28/255 alpha; pill border: accent at ~120/255.
- `Tint(accent, k)` = linear blend from `void` toward `accent` by `k` (solid, reads on all skins). Typical `k`: chip 0.10, pill 0.12–0.16, armed/selected 0.22, big button 0.30.

---

## 2. Typography

- **Display / labels / hero numbers:** `Segoe UI`. Weights: hero numbers `Light`; labels/titles `SemiBold`; body `Normal`.
- **Data / monospace / prices / tickers:** `Consolas` (fallback `"Consolas, Courier New"`).
- **No external/webfont dependency** — both ship with Windows.
- Uppercase micro-labels (`SESSION RISK`, `DAY P&L`) at 8.5–9px `SemiBold`, color `mute` or `ink2`.
- Hero numbers 27–38px `Light`. Section titles 12–13px `SemiBold`.

---

## 3. WPF component patterns

Every Sentinel WPF surface declares the palette as fields/consts and builds from shared helpers.

### Palette fields
```csharp
private static readonly Color C_BG=Color.FromRgb(10,14,23),   C_PANEL=Color.FromRgb(17,23,38),
    C_CARD2=Color.FromRgb(14,20,32),  C_BORDER=Color.FromRgb(30,42,61), C_DIM=Color.FromRgb(27,37,54),
    C_LABEL=Color.FromRgb(174,186,206), C_MUTED=Color.FromRgb(108,122,146), C_TEXT=Color.FromRgb(233,238,247),
    C_ACCENT=Color.FromRgb(63,209,224), C_GREEN=Color.FromRgb(37,208,139), C_RED=Color.FromRgb(255,92,106),
    C_AMBER=Color.FromRgb(242,179,76);
private Color Tint(Color a,double k)=>Blend(C_BG,a,k);
```

### 1b. Theme (Dark · Light · Silver · Obsidian · Blueprint · Amber) — the on-chart SharpDX layer
`SentinelSkin` tokens (`CInk/CAccent/CUp/CVoid/…`) are **theme-aware accessors** that read the active
`SentinelSkin.Palette`. One `SetTheme(t)` recolors **every card + plot-skin across the whole suite** —
consumers never change (they still read `SentinelSkin.CInk`). Card/wash gradients read the `CGlassTop/Bot`
+ `CWashTop/Bot` tokens, so they re-theme too.

| Theme | Character | Void | Ink | Accent | GlowMul |
|---|---|---|---|---|---|
| `Dark` | default; navy flight-deck | `#0A0E17` | `#E9EEF7` | `#3FD1E0` | 1.0 |
| `Light` | soft daylight, **not** inverted dark | `#DEE4EC` | `#1B2432` | `#0C8CA0` | 1.0 † |
| `Silver` | brushed graphite/steel mid-tone | `#262B34` | `#E8ECF2` | `#50D6E4` | 1.0 |
| `Obsidian` | true-black OLED | `#000000` | `#F2F5FA` | `#3FD1E0` | 0.6 |
| `Blueprint` | cyanotype drafting paper | `#08182F` | `#EAF2FF` | `#5FE3F2` ‡ | 1.0 |
| `Amber` | warm dark / night watch | `#12100C` | `#F3EDE0` | `#E8A33D` § | 0.9 |

‡ Blueprint **lifts** the accent (`#3FD1E0` → `#5FE3F2`): plain cyan sits too close to a blue ground to read as
"live". Its platform skin also lifts the **grid lines** well above the paper — the drafting grid is the theme's
signature, not an accident of the recolor.

§ **The accent is not required to be cyan.** The law is "ONE accent = live/watching"; Amber moves that accent to
gold, and consumers never notice (they read `CAccent`). But moving it **forced a second move**: `Warn` is amber
(`#F2B34C`) in every other theme and would have collided head-on with the accent — *live* and *caution* must never
share a hue. So Amber's `Warn` is **cool blue `#6FA8FF`**, the only theme where Warn is not warm. When you invent a
theme, check the accent against `Warn` **and** against `Up`/`Down` before you fall in love with it.

† Light's glow-down is baked into the `IsLight` branches (tighter radii **and** lower alpha), so its
`GlowMul` stays 1.

**Each theme is a deliberate design, never a brightness knob.** Light uses cool off-white grounds (never
glaring white) and DEEPENED accents so cyan/green/red survive on a light ground. Obsidian is not "Dark,
darker": its ground ramp is near-neutral (a navy tint over `#000` reads as haze), its ink is *lifted*
(text on pure black needs less weight to feel bright), and its **glow is dialled back** — a bloom halo
that reads as light on navy reads as **smear** on true black.

**`Palette.GlowMul`** scales every glow/halo *alpha* in the Painter (`Dot` · `Pill` bloom · `HistoBar` ·
`GlowLine`). ⚠ It is a class **field**, so an unset `Palette` silently defaults to `0` (no glow at all) —
every palette must set it explicitly.

**Adding a theme touches exactly four places:** a `Palette` instance · a `Theme` enum member · a word in
`TryParseTheme` · its skin background hex in `SkinBgTheme`.

#### Theme resolution — the glue
`Painter.Begin` calls `MaybeRefreshTheme()` (throttled ≤2s), which resolves in priority order:
1. **Manual pin** — `<Documents>\NinjaTrader 8\Sentinel\theme.txt` containing a theme word
   (`dark`/`light`/`silver`/`obsidian`) pins the on-chart theme and wins over everything.
2. **The glue** — otherwise (`auto`/absent) the on-chart theme **follows the active platform skin**, read
   from `ChartControl.ChartBackground` in the app resources. So *one* skin switch re-themes the whole suite
   within 2s, no F5.

Skin backgrounds, which are the glue's keys: `Sentinel` `#0F1524` · `Light` `#E6EBF1` · `Silver` `#2A2F38` ·
`Obsidian` `#000000` · `Blueprint` `#0A1E3C` · `Amber` `#16130D`.

The glue matches the skin's **exact background hex** (`SkinBgTheme`), *not* its luminance. This matters:
**luminance cannot separate two dark themes** — `Sentinel` (`#0F1524`, lum ≈ .08) and `Sentinel Obsidian`
(`#000000`, lum 0) sit in the same band, so a luminance-only classifier resolved Obsidian as Dark. Luminance
survives only as a **fallback for non-Sentinel skins** (Midnight, Slate Dark, White-Ice…). Keep each skin's
`ChartControl.xaml` background hex in sync with `SkinBgTheme` — both sides carry a comment saying so.

**Scope.** The token system covers the on-chart SharpDX layer. WPF-hosted surfaces (Deck, Dashboard,
Cockpit) read the **`K*` WPF-Color accessors** and re-theme at **build/open time**, not live — the Deck's
header theme button rebuilds its panel for exactly this reason. Each theme also needs a parallel **platform
skin** folder (`templates\Skins\Sentinel`, `… Light`, `… Silver`, `… Obsidian`) for the price panel,
Control Center and tickets. See §4c for the price panel.

⚠ Before declaring a tool "themed", grep it for hardcoded `RC(` / `Color.FromRgb(` / `Color4(` — the Deck
has **three** themed surfaces (WPF panel, SharpDX risk card, order lines) and each was missed once.

### Header (every panel/window opens with this)
Cyan **eye** dot with a `DropShadowEffect` glow (Color=accent, BlurRadius 9, ShadowDepth 0, Opacity .85)
· wordmark (`SENTINEL` ink bold + tool name in mute) · a cyan-tinted **version chip**
(`Tint(accent,.10)` bg, accent border @90α, accent text). See `SentinelDeck.BuildDeck` / dashboard top bar.

### Section labels & collapsible sections
- Micro-label: 9px `SemiBold`, `C_LABEL`, margin `(8,6,8,0)`.
- Collapsible header: chevron `▶`/`▼` that turns **cyan when expanded**, mute when collapsed; label + a hairline `C_BORDER` rule filling the row. (See `GTrader21Panel.BuildCollapsibleSection`.)

### Buttons
- **Big action** (Buy/Sell/Flatten): `Tint(accent,.30)` bg, accent fg, accent border 1.5, height 34, 14px bold.
- **Small action** (Reverse/Close): `Tint(accent,.16)` bg, accent fg, border 1, height 24.
- **Segmented pill** (order type, tabs): idle = `C_DIM`/`C_MUTED`; **selected = `Tint(accent,.22)` + accent fg + accent border 1.5**.
- **Stepper/nudge/preset**: `C_DIM` bg, `C_LABEL`/`C_MUTED` fg, `C_BORDER` border.
- Directional buttons stay green/red; neutral actions (Close/Flat/Entry) stay dim. Never set `Style=null` except on tiny glyph buttons (it strips the template).

### Chips / pills / state badges
Translucent colored pill: bg `Color.FromArgb(28,c)`, border `Color.FromArgb(120,c)`, radius 7–9, colored `SemiBold` text. State badges (MANAGED-BY, ACTIVE/HALT) tint by state via `Tint(col,.12)`.

### Dashboard cards (reusable set in `SentinelDashboard`)
- `MakeCard(child)` — `CardBg` gradient (`#131A28`→`#0E1420`, frozen) + `Edge` border + CornerRadius 12 + pad.
- `StatTile(label,value,valueBrush,sub)` — the hero-tile: micro-label + big `Light` number + mono sub.
- `Track(frac,fill)` — 2-star grid progress bar on a `Faint` track.
- `Chip(text,col)`, `GovernorCard(profile,gov)` — see source. **New tabs adopt these, don't reinvent.**

### Chart primitives (reusable WPF charts in `SentinelDashboard`, v1.1.4 — dataviz method)
- `HBars(labels,values,hues,fmt,…)` — horizontal **magnitude** bars (single hue; identity from the row label).
- `HDivBars(labels,values,fmt,posIsGood,…)` — horizontal **diverging** bars around a center baseline
  (green/red = money/polarity; Canvas-positioned; `posIsGood` picks which side is green). Net ticks, slip, P&L, score.
- `Columns(values,axisLabels,hue,height)` — vertical **time histogram** (activity per bucket).
- **Rules (from the dataviz skill):** cyan(Accent)=magnitude, Green/Red=polarity, Amber=caution; **no rainbow
  categorical** (the suite reserves ONE accent — identity comes from the label, not a hue). Thin bars, rounded
  DATA end / square at the baseline, recessive `Faint`/`Edge` gridlines, **values & labels in TEXT tokens, never
  the bar hue**. Put a chart ABOVE its text rows (chart + table = the accessibility pair). Prefer counts/tables
  over bars when values span orders of magnitude and the small ones matter (e.g. the Test tab safety audit).

### TextBoxes
`C_DIM` bg, `C_TEXT` fg, `C_BORDER` border, `Consolas` 12px, `VerticalContentAlignment=Center`.

---

## 4. On-chart risk card (SharpDX / Direct2D)

The signature "glass instrument card." Reference: `GTrader21Panel.OnRenderRiskCard`, `SentinelDeck.OnRender`.

- **Glass fill:** vertical `LinearGradientBrush` `#1720324F..EE` → `#0A0E17..EF` (≈0.94 alpha), `RoundedRectangle` radius 13–14.
- **Border:** `line` normally; `warn`/`down` when an advisory/lock state is active.
- **Top highlight:** 1px line at `y+1.4` in `ink @ 0.06α`.
- **Header:** glow dot (fill accent@0.26 halo + solid accent core) + `SENTINEL <TOOL>` ink SemiBold + right-aligned context + a state **pill**.
- **Hero number (P&L):** small currency mark `+$`/`-$` **right-aligned in a 24px box ending where the number starts** (kills the `+$   0` kerning gap), then the big `Light` number at `ix+24`.
- **Gauge** (R-multiple etc.): `PathGeometry` + `ArcSegment`, 240° sweep (a0=150°, a1=390°), round `StrokeStyle`.
- **Sparkline / area:** `PathGeometry`; emphasize the endpoint with a filled dot.
- **Track bar:** `RoundedRectangle` on `faint`, fill in accent or a gradient.
- **Footer stats:** `LABEL` mute + value, divider line in `ink@0.06`.
- **Perf:** cache brushes/stroke-style keyed to the `RenderTarget` (rebuild only on device change) OR pool per-frame disposables in a `List` and dispose in `finally`. Cache the `DirectWrite.Factory` for the tool's lifetime; dispose in `Terminated`. **Never allocate a Factory per frame.**
- **⚠ POSITION FROM `ChartPanel`, NOT `chartControl.ActualHeight`.** `ActualHeight/ActualWidth` is the WHOLE chart (incl. subpanels like an ADX pane + the time axis), so a Bottom-anchored card computed from it lands BELOW the price panel and is **clipped → invisible** (only shows on charts with no subpanel). Use `ChartPanel.X/Y/W/H` (the price panel) for both cards and on-chart lines. Bit the Deck risk card (v0.2.1 fix, 2026-07-05).
- Colors via `RC(r,g,b)` → `SharpDX.Color4`. Alpha variants via `new Color4(c.Red,c.Green,c.Blue,a)`.

**Don't hand-roll any of the above — use the shared framework (§4b).** §4 is the *recipe* the framework implements; reach for it only when extending `SentinelSkin` itself.

---

## 4b. The indicator framework — `SentinelSkin` (USE THIS)

`AddOns/SentinelSkin.cs` is the single library every Sentinel indicator/strategy draws with, so
they're cohesive by construction. It packages the whole §4 vocabulary + the §1 palette + §2 fonts.
Namespace `NinjaTrader.NinjaScript.AddOns.Sentinel` — add `using NinjaTrader.NinjaScript.AddOns.Sentinel;`.

> **Live reference implementation: `CompressionBase_v1_3_0`** (in `Indicators.Sentinel`) — its OnRender glass
> card (header dot + title + state pill + coil-vs-threshold Track + mono stat rows) is the pattern to copy for
> retrofitting any plain `Draw.TextFixed`/hand-rolled on-chart readout. **Retrofitted (all Sentinel-homed):**
> `CompressionBase_v1_3_0`, `ADXPro_v1_2_0` (gauge hero + DI tracks + ADX sparkline + regime publish),
> `Eye_v1_1_0` (perf grid), `SignalExcursionRecorder_v1_3` (a
> previously-headless recorder that *gained* a card: REC/IDLE pill · tracking-count hero · regime+ADX track ·
> BG/FC/OBR tally · latest-record MFE/MAE). All place their card via `CardLayout` (below) so they never overlap,
> and all carry the label remover (below). **Still to sweep:** any remaining tool with a plain `Draw.TextFixed`
> readout — give each a `CardCorner` + `CardLayout` + the label remover.
> **FORK GOTCHA:** when you `cp` a versioned indicator to fork it, the copy carries its NT generated
> `#region`s and running NT appends more → CS0111/CS0102. **Strip ALL generated regions to EOF right after
> the copy** (`head -n <lastRealLine> file > tmp && mv`); NT regenerates one clean copy on F5.

**Palette (no local color fields anymore):** `SentinelSkin.CVoid/CPanel/CCard/CLine/CDim/CFaint/CInk/
CInk2/CMute/CAccent/CUp/CDown/CWarn` (SharpDX `Color4` for OnRender) and `WVoid…WWarn` (WPF `Color`
for hosted panels). `RC(r,g,b,a)`, `Alpha(c,a)`, `W(r,g,b)`. Fonts: `SentinelSkin.FSans` / `FMono`.

**The `Painter`** — hold one as a field, `Begin()` each frame, `Dispose()` in Terminated:
```csharp
private readonly SentinelSkin.Painter _sp = new SentinelSkin.Painter();

protected override void OnRender(ChartControl cc, ChartScale cs) {
    base.OnRender(cc, cs);
    if (RenderTarget == null) return;
    _sp.Begin(RenderTarget);
    var r = _sp.Card(x, y, 300f, 148f, active ? SentinelSkin.CLine : SentinelSkin.CWarn);   // glass card → inner rect
    _sp.Dot(r.Left + 4f, r.Top + 8f, active ? SentinelSkin.CAccent : SentinelSkin.CWarn);    // live glow dot
    _sp.Text("SENTINEL EYE", r.Left + 15f, r.Top - 1f, 160f, 18f, SentinelSkin.CInk, 12f, semibold:true);
    _sp.Pill(state, r.Right, r.Top - 2f, dotColor);                                          // state pill
    _sp.Money(r.Left - 2f, r.Top + 40f, pnl, pnl >= 0 ? SentinelSkin.CUp : SentinelSkin.CDown);  // kerned hero
    _sp.Track(r.Left, r.Top + 96f, r.Width, frac, SentinelSkin.CAccent);                     // progress bar
    _sp.Gauge(r.Left + 44f, r.Top + 110f, 34f, rFrac, SentinelSkin.CFaint, SentinelSkin.CAccent);
    _sp.Sparkline(r.Left, r.Top + 70f, r.Width, 22f, history, SentinelSkin.CUp);
    _sp.Divider(r.Left, r.Top + 120f, r.Right);
    _sp.End();   // releases per-frame gradients/geometries
}
protected override void OnStateChange() { /* … */ if (State == State.Terminated) _sp.Dispose(); }
```
`B(color)` gives a cached brush if you need raw draws. The Painter caches brushes (per RenderTarget) +
text formats + a round stroke, and owns its DirectWrite factory — no per-frame allocation beyond the
gradients/geometries `End()` frees. **Any on-chart Sentinel drawing goes through the Painter** so the
look stays identical everywhere; if you need a new primitive, add it to the Painter, not to one tool.

**Chart data-series / candles + drawn-text fonts** are handled by the **Sentinel skin**, not per tool:
`ChartControl.UpBrush/DownBrush` = candle bodies, `Stroke/Stroke2` match the bodies (set all four together),
selection = cyan, chart text = crisp ink-grey (`Docs`/[[sentinel-skin]]). **Current candle colors (2026-07-05,
user pick): up = teal `#FF009999`, down = grey `#FF8E8E8E`** (was green/red `#FF25D08B`/`#FFFF5C6A`). Skin
color edits load only when the skin (re)loads — **re-select the skin or restart NT; a NinjaScript F5 does NOT
reload skins.** For a *saved* data-series template, NT stores `UpBrushSerialize`/`DownBrushSerialize` inside the
ChartStyle — set those to the same hex if you export one, but the skin default is the cohesive path.

**Chart right-side margin is NOT a skin resource.** Skins carry only brushes/pens/fonts/UI-margins — there is
no `ChartControl` key for the price-panel right margin (confirmed by grep). It is a **per-chart property**:
right-click chart → Properties → *Right side margin*. To make it global-ish, set it on a chart then **right-click
→ Templates → Save As → "Default"** so every new chart inherits it (e.g. 350px to keep the stacked glass cards
clear of price action). If a true app-level default exists, it lives in NT's user config, not the skin.

**Card placement — `SentinelSkin.CardLayout` (USE THIS; never hardcode the corner).** Cards from
different tools all defaulting to `ChartPanel.X + W - cw - 12` collide (two cards → one on top of the
other). Instead ask the shared registry for the rect each frame:
```csharp
var slot = SentinelSkin.CardLayout.Place(this, ChartPanel,
    ChartPanel.X, ChartPanel.Y, ChartPanel.W, ChartPanel.H, CardCorner, cw, ch);   // → SharpDX.RectangleF
var r = _sp.Card(slot.X, slot.Y, cw, ch, edge);
```
- `key` = a stable per-card identity (pass `this`). `panelKey` = the `ChartPanel` (groups cards on one panel).
- Cards docked to the **same corner** of the same panel **auto-stack** vertically (gap-separated), in
  first-render order — so N tools never overlap. Expose a `SentinelCardCorner CardCorner` `[NinjaScriptProperty]`
  (default `TopRight`) so the user can spread cards across the four corners too.
- Entries the caller stops drawing (ShowInfo off / indicator removed) **self-prune after ~2s**; also call
  `SentinelSkin.CardLayout.Release(this)` in `Terminated` for immediate cleanup. All lock-guarded (render thread).

**Retrofitting an existing indicator** (SentinelEye, SignalExcursionRecorder, CompressionBase, …):
delete its local color consts + hand-rolled OnRender card, add the `_sp` field + `Dispose()`, redraw
via the Painter, and place the card with `CardLayout` (+ a `CardCorner` property). Bump the version + changelog per §7.

### 4c. The Sub-panel PLOT STANDARD (bring histograms/lines up to the card's material)

A gorgeous card floating over stock NT plots (flat bars in raw WPF brushes, flat-black panel, default grid)
is the #1 vibe-killer. Fix it by rendering the **plots themselves** through the Painter, in the SAME
`OnRender` frame as the card. **The key trick: NT draws its stock plots BEFORE `OnRender`, so an OPAQUE
`PanelWash` drawn first in `OnRender` covers them** — which also defeats the chart's saved-plot-color
override (no remove/re-add needed). Reference impl: **`SentinelWAE_v1_0_0`** (`RenderPlotSkin`).

New `Painter` primitives (the sub-panel counterpart to the card set):
- `PanelWash(x,y,w,h)` — the navy glass gradient behind everything. Draw FIRST (after `base.OnRender`).
- `RegimeShade(x,y,w,h,col,alpha)` — a faint full-panel state wash (cyan live / green-red bias). Low alpha.
- `Baseline(x0,x1,y,col)` — a themed zero/reference line (turn NT's own grid OFF in `SetDefaults`).
- `HistoBar(cx,yZero,yVal,halfW,col,glow)` — a card-material histogram column: vertical gradient (bright
  tip → translucent base), soft-rounded ends, optional glow. Palette: `CUp`/`CDown` (NOT raw green/red),
  dim the alpha for the "weakening" tone instead of clashing lime/orange.
- `GlowLine(pts,col,width,glow)` — an oscillator/reference line as a soft glow underlay + crisp stroke.

The `OnRender` recipe (chart-space; read series by **absolute** bar index — barsAgo throws in render):
```csharp
base.OnRender(cc, cs);
if (RenderTarget == null || ChartPanel == null) return;
_sp.Begin(RenderTarget);
try { if (SentinelPlotSkin) RenderPlotSkin(cc, cs); } catch { }   // wash → shade → baseline → histobars → lines
try { if (ShowCard) RenderCard(); } catch { }                     // card LAST, on top
_sp.End();
// inside RenderPlotSkin: for idx in [ChartBars.FromIndex..ToIndex]:
//   x = cc.GetXByBarIndex(ChartBars, idx);  y = cs.GetYByValue(Values[i].GetValueAt(idx));
```
Expose a `SentinelPlotSkin` toggle — deliberately **NOT** `[NinjaScriptProperty]` (a plain `[Display]` get/set
serializes without a constructor-param / codegen churn), default ON, so a user can fall back to stock plots.

**Adopted (2026-07-07):** `SentinelWAE` (reference) · `ADXPro_v1_2_0` (wash + per-bar regime bands that
supersede its muddy `BackBrushes` + glowing ADX/DI lines + trigger/strong reference lines) ·
`WoodiesCCIPro_v1_0_0` (wash + bottom trend ribbon + glowing Main/Turbo lines + 0/±100 lines) ·
`BuySellVolumePressureMountain_v1_0_0` (wash + two-sided gradient histobars). Each keeps a `SentinelPlotSkin`
toggle. **Still stock:** VolEnvelope (a price-panel overlay/cone — different case), and any newly ported panel.

**The PRICE panel is different — it lives in the skin, not an indicator.** You can't `PanelWash` the price
panel: `OnRender` draws *over* the candles, so an opaque wash there would hide them. The candle-safe home for
the price-panel background is the platform skin's `ChartControl.ChartBackground` (drawn *behind* the bars) —
set it to the same navy as the wash (`templates\Skins\Sentinel\ChartControl.xaml`, currently `#0F1524`, between
CPanel and CVoid). NOTE: a skin edit needs a **skin reapply / NT restart** to take effect — it is NOT picked up
by an F5.

### MANDATORY on EVERY Sentinel indicator — the label remover
The chart is a flight instrument; NT's default top-left name-label is clutter. **Every Sentinel indicator hides it
by default** with a trader toggle to restore it. **NT draws the chart panel label from the indicator's `Name`
property** (NOT `ToString()` — that override is inert here; VERIFIED 2026-07-05 against the shipped `LabelRemover.cs`,
which blanks other indicators via `indicator.Name = ""`). So blank `Name` at `DataLoaded` when the toggle is off:
```csharp
[NinjaScriptProperty]
[Display(Name = "Show indicator label", Description = "Show NinjaTrader's chart name label. Sentinel default = OFF (clean chart).", GroupName = "Sentinel", Order = 100)]
public bool ShowIndicatorLabel { get; set; }
// SetDefaults:  Name = "<Thing>_vX_Y_Z"; … ShowIndicatorLabel = false;
// DataLoaded (FIRST line):  if (!ShowIndicatorLabel) Name = string.Empty;
```
`Name` is set to the real identity in `SetDefaults` (picker/serialization see it) and blanked at `DataLoaded` (the
chart label reads the runtime value → hidden). Toggling the property re-runs OnStateChange, so it takes effect on
Apply. Default OFF = clean chart. (Trade-off: when OFF the indicator also reads blank in the Indicators-dialog
"Configured" list — acceptable; identify by the picker folder.) Applied to CompressionBase_v1_3_0,
SignalExcursionRecorder_v1_3, ADXPro_v1_2_0, Eye_v1_1_0, Deck_v0_2_1; **required on all future ones.**

- **⚠ ON A STRATEGY THE LABEL SURVIVES UNTIL IT IS ENABLED (verified 2026-07-09, SentinelBridge).** A strategy that
  is on the chart but **disabled never reaches `State.DataLoaded`**, so the blanking line never runs and NT keeps
  drawing `(D) <Name>(param, param, …)` across the top-left. It disappears the moment the strategy is enabled.
  **Do NOT "fix" this by blanking `Name` in `SetDefaults`** — that is the identity the Control Center's Strategies
  grid displays, and a nameless row is a strategy you cannot find in order to disable it. Live with the `(D)` label;
  it is only present precisely when the strategy is inert.
- **⚠ ORDER-SUBMITTING tools (Deck, any strategy): DECOUPLE order-tag identity from `Name` FIRST.** If the tool
  tags orders with `Name` (`acct.CreateOrder(..., Name + "_SL", …)`) or matches fills via `order.Name.StartsWith(Name+"_")`,
  blanking `Name` would corrupt order identity / fill capture. Capture it into a stable field once and use that for
  tags: `private string _tag = "Deck"; … // DataLoaded: _tag = Name; if (!ShowIndicatorLabel) Name = "";` then
  `_tag + "_SL"` everywhere. (Learned on Deck_v0_2_1 — 11 tag sites + the fill match.)

---

## 5. NinjaScript architecture patterns

### Which type?
- **Indicator** — anything that lives on a chart (order decks, risk cards, scanners, drawing). Manual/discretionary order entry belongs here (`SentinelDeck`).
- **Strategy** — automated entry/exit logic (`GTrader21`). Strategies CAN `OnRender` (verified).
- **AddOn** (`AddOnBase` singleton) — headless cross-tool services + the control-center dashboard (`SentinelCore`, `SentinelRiskService`, `SentinelDashboard`). Namespace `NinjaTrader.NinjaScript.AddOns.Sentinel`.

### Hosting a panel in the ChartTrader sidebar (the proven recipe)
An indicator/strategy injects a WPF panel into ChartTrader's Content `Grid`:
```csharp
_ctChart      = Window.GetWindow(ChartControl.Parent) as Chart;
var ct        = _ctChart.FindFirst("ChartWindowChartTraderControl") as ChartTrader;
_ctTraderGrid = ct.Content as Grid;
// build hudStack → wrap in ScrollViewer → InsertPanel(): add a Star RowDefinition, Grid.SetRow,
// SetColumnSpan, add to grid.Children. Teardown removes the child + row.
```
Account + instrument come from the native selectors — do NOT build your own pickers:
`GetChartTraderWindow().FindFirst("ChartTraderControlAccountSelector") as AccountSelector` → `.SelectedAccount`;
`"ChartWindowInstrumentSelector" as InstrumentSelector` → `.Instrument`. (Wrap reads in `ChartControl.Dispatcher.Invoke`.)

### Account-level (unmanaged) orders — the manual/deck path
An indicator that submits its own orders **fully owns them** (no managed-position desync). Signature:
```csharp
acct.CreateOrder(instr, OrderAction, OrderType, OrderEntry.Manual, TimeInForce, qty,
                 limitPrice, stopPrice, oco, Name+"_tag", Core.Globals.MaxDate, null);
acct.Submit(new[]{ order });   // acct.Cancel(orders) to pull working orders
```
- Actions: entry `Buy`/`SellShort`; exit `Sell`/`BuyToCover`. NT nets automatically.
- Types: `Market`, `Limit` (limitPrice), `StopMarket` (stopPrice), `StopLimit` (both).
- Always `instr.MasterInstrument.RoundToTickSize(px)`; tick = `.TickSize`; $ = `.PointValue`.
- **Chart-scoped flatten:** cancel working orders WHERE `Instrument.FullName==this` on the selected account, then market-close that position (retry loop, off the UI thread via `Task.Run`). Never account-wide unless explicitly asked.

### Managed-framework landmines (do NOT relearn)
- A managed strategy position must not be closed by raw/panel orders — desyncs `Position`, blocks new entries ("Position not flat"). Recovery: disable→re-enable.
- The managed framework re-asserts `SetStopLoss` prices; a panel edit won't stick. Unmanaged mode required for panel-owned exits.
- A panel using `Account.Change`/`Cancel` **cannot** touch *strategy*-submitted orders (throws "Unable to change order"; with `RealtimeErrorHandling=StopCancelClose` it kills the strategy). Indicators submitting their OWN account orders avoid this entirely.

---

## 6. Sentinel integration seam (`SentinelCore`)

Reference `SentinelCore` (`AddOns/SentinelCore_v1_0_0.cs`) — all statics, namespace `...AddOns.Sentinel`.
Add `using NinjaTrader.NinjaScript.AddOns.Sentinel;`.

| Call | Returns | Use |
|---|---|---|
| `CanEnter(instr, acct, out reason)` | bool | composite entry gate (kill+scoped-kill+feed+governor+session+rollover+news) |
| `CanActInstrument(instr, acct, out reason)` | bool | copier/mirror gate |
| `GetGovernorState(account)` | `GovernorState` | day P&L / cap / status (`.DailyPnl/.Cap/.Allowed/.Status`) |
| `TradingAllowedToday(acct)` / `InAccountSession(p,out r)` | bool | governor / session |
| `InstrumentKillEngaged(instr)` · `RolloverBlocked` · `NewsLockoutActive` | bool | scoped states |
| `SizedQuantity(acct, baseQty)` | int | profile-scaled size |

### v1.1.0 hardening surface (Gate / Ledger / State / Alerts — USE THESE for anything order- or safety-related)
The **Order Gate** is the single pre-submit choke point (`Docs/SENTINEL_HARDENING_FRAMEWORK.md`). New order
paths route through it instead of raw `CanEnter`.

| Call | Returns | Use |
|---|---|---|
| `GateEntry(acct, instr, qty, stopTicks=0, riskDollars=0, instr=null)` | `GateDecision {Level (Clear/Advisory/Hard), Reason, Size}` | THE entry gate — risk-sizes + classifies. Pass stopTicks+riskDollars+instr to size by $-risk. |
| `SizeForRisk(acct, instr, stopTicks, riskDollars)` | int | contracts for a $-risk (0 = can't afford a 1-lot) |
| `TickValue(instr)` | double | $ per tick (PointValue×TickSize) |
| `NoteOrderSubmitted(account)` · `SetOrderGuards(maxQty, perWindow, sec)` | — | feed / tune the fat-finger rate guard (call NoteOrderSubmitted after every submit) |
| `Ledger.Order(acct, instr, action, type, qty, px, tag)` · `Ledger.Action(kind, acct, detail)` · `Ledger.Fill(acct, instr, action, qty, intended, fill, tickSize, tag)` | — | WRITE (async) to the daily event stream. `Fill` carries intended-vs-actual price → adverse slip ticks (feeds the Slippage view); call it from `OnExecutionUpdate` (realtime only). |
| `Ledger.ReadRecent(days)` · `Ledger.ReadDay(date)` · `Ledger.Parse(line)` | `List<Ledger.Entry>` / `Entry` | READ the stream → typed rows (`Evt`/`Account`/order:`Instrument,Action,Type,Qty,Price,Tag`/action:`Kind,Detail`; `TimeLocal`,`IsOrder`,`IsAlert`,`IsCritical`). The Dashboard **Journal** tab + future audit/slippage are VIEWS of this — never build a 2nd journal. |
| `Ledger.Dir` · `Ledger.FileFor(date)` | string | ledger folder / a day's JSONL path |
| `State.Save(key,json)` · `State.Load(key)` · `State.Clear(key)` · `State.Age(key)` · `State.SaveMap(key,map)` · `State.LoadMap(key)` | — / string / `Dictionary` | **intended-state store**: keyed atomic blob (`<SettingsDir>\State\<key>.json`) so a tool's arm-state (trail high-water / BE-armed / active stop) survives a restart. Save on change, Clear on flat, Load+**reconcile** on restart. Key by tool identity, e.g. `"GTrader21|<acct>|<instr>"`. |
| `Alerts.Critical(title, detail)` · `Alerts.Info(...)` · `Alerts.Recent(n)` · `Alerts.Raised` event | — | 2-tier alerts (Critical rare by design). Consumers: dashboard Risk tab (Recent), **SentinelAlertService** (sound + optional push shell command, config `Sentinel\Alerts.conf`). |
| `HardEnforceArmed(acct)` · `GovernorResetHour` / `GovernorResetLabel` | bool/int/string | opt-in hard auto-flatten flag · daily reset clock |

**Fail-open vs fail-closed on the Gate (the core policy split):**
- *Manual* tools (SentinelDeck) = **fail OPEN** — surface a Hard reason loudly but never block a human (they must always be able to exit). `if (gate.IsHard) StatusLoud(reason); submit anyway;`
- *Automated* tools (GTrader21, Copier) = **fail CLOSED** — enter only on `gate.IsClear`. A code *exception* still fails open (resilience ≠ gate bypass).
- **Exits never gate** — always allow flatten/close.

Publish/consult registries exist for kill-switch, feed-watch (ref-counted), Eye verdicts, VolEnvelope regime, **ADX regime (v1.2.0 — `SetAdxState`/`GetAdxState`/`AllAdxStates` + `AdxState`; ADXPro publishes trend strength + bias as INT `-1/0/1`, `.TrendOn`/`.Building`/`.Aligned(dir)`)**, **trend line (v1.3.0 — `SetTrendState`/`GetTrendState`/`AllTrendStates` + `TrendState`; SentinelTrend publishes trailing direction INT `-1/0/1` + line price + signed distance ticks + bars-in-trend + `.Flipped`, `.IsUp`/`.IsDown`/`.Aligned(dir)`)**, **liquidity walls (v1.4.0 — `SetLiquidityState`/`GetLiquidityState`/`AllLiquidityStates` + `LiquidityState`; LiquidityWalls publishes absorption z-score + side INT `-1` support-below/`0`/`1` resistance-above + nearest wall above/below price + distances, with `.ResistanceAbove`/`.SupportBelow`/`.NearWall(ticks)`/`.BlocksEntry(dir,ticks)` so a consumer can veto entries into a wall)**, **CCI trend (v1.5.0 — `SetCciState`/`GetCciState`/`AllCciStates` + `CciState`; WoodiesCCIPro publishes persisted Woodies trend state INT `-2..+2` + Main/Turbo CCI + slope + last entry signal, with `.Bias`/`.Strong`/`.TrendOn`/`.Aligned(dir)`)**, **brick/bar-state (v1.6.0 — `SetBrickState`/`GetBrickState`/`AllBrickStates` + `BrickState`; the Sentinel bartypes publish adaptive ATR + brick direction INT `-1/1` + offsets + live tick-countdown, with `.IsUp`/`.AtrTicks(ts)`/`.Aligned(dir)`)**, **the Council verdict (v1.7.0 — `SetCouncilState`/`GetCouncilState`/`AllCouncilStates` + `CouncilState`; see §6c)**, **session clock (v1.8.0 — `SetClockState`/`GetClockState`/`AllClockStates` + `ClockState`; Clock publishes session phase INT `0/1/2/3` + mins-since-open/to-close + kill-window)**, **participation (v1.9.0 — `SetParticipationState`/`GetParticipationState`/`AllParticipationStates` + `ParticipationState`; Participation publishes relative volume + z-score + climax/dry-up)**, **structural levels (v1.10.0 — `SetLevelState`(object)/`GetLevelState` + `LevelState`; Location publishes VWAP+bands/PDH-PDL/OR/IB/session H-L + nearest level, `.InPath(dir,atr)`)**, **MTF alignment (v1.10.0 — `SetMtfState`(object)/`GetMtfState` + `MtfState`; MTF publishes higher-TF consensus Bias/AlignmentScore/AllAgree)**, **compression breakout (v1.11.0 — `SetCompressionState`(object)/`GetCompressionState` + `CompressionState`; CompressionBase publishes breakout pulse + held BreakDir + coil)**, **intermarket (v1.12.0 — `SetIntermarketState`(object)/`GetIntermarketState` + `IntermarketState`; Intermarket publishes a configurable correlated-instrument Lean)**, **WAE momentum (v1.13.0 — `SetWaeState`/`GetWaeState` + `WaeState`; SentinelWAE publishes a Waddah-Attar momentum-breakout vote)**, **god-reversal (v1.14.0 — `SetGodReversalState`/`GetGodReversalState` + `GodReversalState`; SentinelGodReversal publishes a candle-grammar reversal trigger)**, **flux order-flow (v1.31.0 — `SetFluxState`/`GetFluxState` + `FluxState`; the `SentinelFlux` order-flow-imbalance BAR TYPE publishes a signed tape-imbalance vote — the first genuinely ORTHOGONAL axis)**, fleet plan (Arc), config-use, governor, profiles — see `SentinelCore` (internal v1.31.0) for the setters when a tool should broadcast state.
New Profiles.conf keys: `resetHour=17` (governor daily reset hour, local) · `hardEnforce=true` (arm auto-flatten at the loss stop).

### 6b. Signals as PLOTS — the generic consumer seam (Deck SIGNAL ARM)
Two ways a tool broadcasts a signal; **prefer whichever the consumer needs, but a plot is the universal one:**
1. **Hidden PLOT (universal — any consumer, no coupling):** a signal-emitting indicator exposes its signal as a plot,
   e.g. `Signal[0] = +1/-1/0`. Make it **invisible + non-scaling**: `AddPlot(new Stroke(Brushes.Transparent,1f), PlotStyle.Line, "Signal")`
   **and set `IsAutoScale = false`** so the ±1 values never render or squash the price panel. Reference: `CompressionBase_v1_3_0`
   writes its breakout to `Signal` inside `MarkBreak`. Any tool then reads it generically — the **Deck SIGNAL ARM** discovers
   ALL loaded indicators' plots (`ChartControl.Indicators` → `ind.Values[i]`, names via `ind.Plots[i].Name`) with **zero
   hardcoding**, and arms / auto-fires off the chosen one.
2. **`SentinelCore` publish (Sentinel-aware only):** pre-digested state consumed by name, e.g. the Eye's `SetEyeVerdict`,
   ADX `SetAdxState`, Trend `SetTrendState`. Use when the signal is a rich verdict, not a plottable scalar.

**Consuming another indicator's plots — the rules (memory `nt-consume-indicator-plots`):** resolve the source ref on the
**UI thread** (cache it), READ `.Values` on the **data thread** (never enumerate `ChartControl.Indicators` off the UI
thread). **A one-bar PULSE plot is race-prone** — read the **just-closed bar** (`barsAgo=1`) and **re-check every tick**
(fires the bar after the signal, non-repaint). **Never write `ind.Plots.Count`** (method group → CS0019, and headless
won't catch it) — use the indexer. Sentinel tools **blank `Name`** (label remover) → name a source by `GetType().Name`.

### 6c. The Council — the confluence arbiter ("the brain")   *(SentinelCore ≥ v1.7.0)*
`Council_v1_0_0` (a read-only chart indicator, NO ORDERS) is the one tool that **CONSUMES every sensor seam and
FUSES them** into a single verdict, then re-publishes it as `CouncilState` for everything else to consult. It is the
top of the consult chain: sensors publish → **Council fuses** → strategies/Bridge/Deck consult the Council.

- **Fusion = a weighted vote.** Each fresh seam (`GetEyeVerdict/GetTrendState/GetCciState/GetAdxState/GetEnvelopeState/
  GetBrickState`) casts a signed vote × weight; stale/absent seams **abstain** (fail-open). `Bias = sign(Σ vote×weight)`
  past a deadband; `Conviction = |Σ|/Σ(active weight)`, damped by voter breadth and a VolEnvelope squeeze. Weights are
  `[NinjaScriptProperty]` — **the weights ARE the edge; tune them, then let Lens grade them.**
- **Hard vetoes are account-free** (an indicator has no `Account`): consult `KillSwitchEngaged`,
  `InstrumentKillEngaged(inst)`, `RolloverBlocked(inst)`, `NewsLockoutActive(inst)`, and `LiquidityState.BlocksEntry(bias,ticks)`
  directly — do NOT call `CanEnter` (it needs an account). Any veto zeroes `Conviction`/`SizeMult` and names itself in `VetoReason`.
- **Advisory, not the order gate.** The Council mirrors the gates so its verdict is honest, but a consuming *strategy* still
  calls `GateEntry` at submit. Consumers **gate on `HasEdge` + `Aligned(dir)`, size with `SizeMult`**, and **write the verdict
  into the Ledger/Log `ctx` on every FIRE** (the Council itself only logs *changes* to `sentinel.log`) so Lens can grade it.
- ⚠ **Independence caveat (state it in every discussion):** today's voters are nearly all price-derived (ADX/CCI/Trend/
  Envelope/Brick echo the same OHLC) — `Conviction` is *agreement*, not *confirmation*. The verdict only gets independent as
  the **orthogonal axes** below publish their own seams. **Now partly addressed:** the **FLUX** voter (the `SentinelFlux`
  order-flow-imbalance bar type, §6d) is the first genuinely orthogonal axis — a tape-sourced vote, not the same OHLC.
  The Council (v1.6.3) now fuses **22 voters**; interim `ConvictionFloor = 0.20`.

### 6d. Signal collection — the orthogonal axes (planned seams, build in dependency order)
The Council is built to pick these up automatically as each publishes a `…State` seam. **Rule for every new axis: publish a
clean `…State` seam AND record it to the Ledger on every fire, or Lens can never tell whether it helped.**
1. **`ClockState`** — session phase / minsSinceOpen / minsToClose / dayOfWeek / inKillWindow. A **modulator** (scales weights),
   not a voter. Nearly free; build first.
2. **`EventState` / event veto** — **already has a live consumer**: `SentinelRiskService` reads `Sentinel\News.conf`
   (`YYYY-MM-DD HH:mm | Event | scope | beforeMin | afterMin`) → `SentinelCore.SetNewsLockouts` → folded into `CanEnter` **and**
   consulted by the Council. To automate, land `EconomicCalendar.py`'s `block_windows` into `News.conf`. **Consume rules:**
   (a) **freshness guard** — treat a stale/missing calendar (date ≠ today) as *fail-to-caution*, never trust yesterday's windows;
   (b) the script's directional `bias_score` is **EQUITY (MNQ) specific — do NOT feed it to a gold (GC/MGC) strategy**; only the
   blackout windows are universal; (c) times are **ET wall-clock — convert ET→session TZ (DST-aware)**.
3. **`ParticipationState`** — time-of-day-normalized RVOL + cumulative-delta divergence + climax/dry-up. Reuses existing delta +
   the `State` blob store (persist the per-minute volume curve).
4. **`LevelState` + `MtfState` + `InternalsState`** — one shared multi-series "Context" host (all need `AddDataSeries`).
   Location = VWAP+bands / PDH-PDL / opening range / IB / session H-L / volume-profile POC-VAH-VAL, ATR-normalized distances.
   MTF = bias on the 1/5/15/60/240 ladder, **anchored to SentinelTrend's trend definition**. Internals = DXY+yields (gold) or
   `$TICK/$ADD/$VOLD/$TRIN` (ES) — **PARKED** on feed availability.
5. **`FluxState`** — ✅ **SHIPPED (v1.31.0): the ORDER-FLOW axis.** The `SentinelFlux` bar type (BarsPeriodType 212203)
   closes bars on accumulated signed order-flow imbalance (López de Prado information-driven bars, TBars-stabilised) and
   publishes a tape-sourced `FluxState` vote — the suite's **first genuinely orthogonal** axis, wired into the Council as
   the **FLUX** voter (+ an absorption-damp modulator). This is the piece the §6c "agreement, not confirmation" caveat
   was waiting on.
   *Parked lower still:* book/spread microstructure, VIX/vol-term-structure regime — **note the order-flow slice is now
   delivered by Flux;** what remains parked is full book/spread microstructure and the VIX regime.

### 6e. Helm — the interdiction seam (the publish/consult idiom pointed the OTHER way)   *(SentinelCore ≥ v1.34.0)*
Everywhere else in §6 a *sensor* publishes state and a *consumer* reads it. **Helm inverts that: a HUMAN publishes an
intent addressed to a RUNNING automated actor, and the actor consumes and executes it with its OWN order handles.**
Helm is the interdiction layer — a human grabs the wheel of a running actor **without stopping it**. It completes the
trio: **Deck** (you drive) · **Bridge** (it drives) · **Helm** (grab the wheel). **Helm owns NOTHING** — it never
touches an order; it publishes an `HelmIntent` and the actor stays the sole owner of its position and exits.

- **Why command-the-owner (the constraint that forces this shape):** a managed position closed by a panel desyncs
  `Position`→account; the managed framework re-asserts `SetStopLoss`; `Account.Change`/`Cancel` on a strategy order
  throws and can terminate the strategy (§5 + the CONTRIBUTING.md order-ownership lessons). So Helm does NOT act on the
  order — it commands the owner, and the owner acts. Human input becomes a seam.
- **The seam (all static on `SentinelCore`), keyed by `instanceKey`** (`<class>#<scope>@<account>`, beside the
  `RegisterActor` registry):

| Call | Returns | Use |
|---|---|---|
| `SetHelmIntent(instanceKey, HelmIntent)` | — | **PUBLISH** an intent (human side). Auto-fills `Id`/`IssuedUtc`/`ExpiryUtc` (default TTL 120s); FIFO-queued per `instanceKey`. |
| `TakeHelmIntent(instanceKey)` | `HelmIntent` | **CONSUME** (idempotent) — pops the next live intent, drops expired ones, returns null when empty. **Drain in a LOOP each pass.** |
| `PendingHelmIntents(instanceKey)` | int | non-consuming count of live queued intents. |
| `SetHelmState(instanceKey, HelmState)` · `GetHelmState(key, maxAgeSec)` · `AllHelmStates()` · `ClearHelm(key)` | — / `HelmState` / list / — | the actor publishes `HelmState` **BACK** (position/stop/target/paused/override/status) so a surface renders reality; `ClearHelm` on teardown. |

  - `enum HelmVerb { Pause, Resume, SkipNext, FlattenNow, MoveStop, MoveTarget, BreakevenNow, Scale, TakeOver, HandBack }`.
  - `HelmIntent { Id; InstanceKey; Verb; Price; QtyDelta; Reason; IssuedUtc, ExpiryUtc; IsExpired; IsRiskReducing; IsRiskAdding }`
    — `IsRiskReducing`/`IsRiskAdding` answer only the **unambiguous** verbs; `MoveStop`/`MoveTarget` are
    **context-classified by the consumer** (a widen is risk-adding, a tighten is risk-reducing).
  - `HelmState { InstanceKey, Instrument, Account, Scope; PositionQty (signed); AvgPrice, StopPrice, TargetPrice;
    Paused, SkipArmed, HumanOverride; LastIntentId, Status; UpdatedUtc }`.
- **The asymmetric gate (applied by the CONSUMER, not the seam):** risk-**reducing** verbs are **fail-OPEN** (a human
  must always be able to reduce risk); risk-**adding** verbs (`Resume` / widen-stop / `HandBack` / scale-UP) pass
  `GateEntry` **fail-CLOSED**. The Gate does not care whose finger it was.
- **One-shot + expiry are load-bearing.** Intents are **consumed** (never re-read) and **expire** (TTL); they are
  **RAM-only** (a restart drops them) and the "armed/pending" idea is **never persisted** — a stale intent replayed
  after a restart is exactly the class of bug that duplicates a stop.
- **Consumer contract:** key by `instanceKey`, **drain `TakeHelmIntent` in a loop** every pass (a tick-thread drain
  + an `OnBarUpdate` backstop), execute with your OWN order handles, and **publish `HelmState` back** each pass.
  Reference consumer: `SentinelBridge` v0.3.0 (`Docs/BRIDGE_SPEC.md`); reference surface (writer of intents):
  `SentinelCockpit` v0.5.0 ⑤ Helm (`Docs/SENTINEL_COCKPIT_SPEC.md`). Punch list: `Docs/HELM_TEST_PUNCHLIST.md`.

---

## 7. Naming & versioning

> **⚑ FEDERATED NAMING LAW (RATIFIED 2026-07-07 — this is the single source of truth; supersedes the
> earlier "drop the prefix" decision).** A novice must be unable to confuse a Sentinel-plumbed tool with a
> stock NT one — so the "Sentinel" tell shows up on **FOUR layers**. The earlier convention (2026-07-05,
> `sentinel-namespace-and-naming`) that *dropped* the class prefix is **DEPRECATED**; its reasoning (the
> folder supplies context) held only for the *picker*, but the on-chart **display `Name` still read
> generic** and that's what a novice sees. The ratified law re-adds the prefix on the class **and** puts
> "Sentinel" in the display Name.

| Layer | Rule | Example | Serialization-locked? |
|---|---|---|---|
| **① Display `Name`** (in `SetDefaults`) | **`"Sentinel <Thing> v<M>.<m>.<p>"`** — mandatory prefix **+ version** (amended 2026-07-10, §9); ` (DEV)` while in dev | `Name = "Sentinel Trend v1.0.0";` | **No** — display-only; **safe to change ANY time** |
| **② Namespace** | **Indicators** → `…Indicators.Sentinel` (→ picker "Sentinel" folder). **⚠ STRATEGIES → BASE `…Strategies`, NOT `.Strategies.Sentinel`** — NT's Strategy selector does NOT surface sub-namespaced strategies (verified 2026-07-07 on SentinelBridge: compiled clean, never listed); identity carried by class prefix + Name. | `namespace …Indicators.Sentinel` / `namespace …Strategies` | **YES** — bump only |
| **③ Class + file** | `Sentinel<Thing>_vX_Y_Z` | `SentinelTrend_v1_0_0[.cs]` | **YES** — change at version bump only |
| **④ Runtime** | cyan glass card + label remover ON (+ publish a `…State` seam wired to the Council — §9 item 6) | `ShowIndicatorLabel = false` | n/a |

- **The split that makes migration painless:** layer ① is display-only (the `Name` property is NOT the
  serialization identity), so the "Sentinel " display prefix can be **retrofitted across the whole suite
  right now** without dropping anything off saved charts. Layers ②③ ARE the identity (namespace + class),
  so they change **only at a tool's next version bump** — frozen old versions keep their old identity.
- **Executors (strategies) still follow ①②③** but layer ④'s "publish a seam" clause is for *sensors*; an
  executor consumes seams instead (e.g. the **Bridge** reads `CouncilState`), so it carries the card +
  label remover but publishes no `…State`.
- **⚠ NAME FIDELITY — the `<Thing>` is the SOURCE indicator's FULL name (amended 2026-07-10 — see Naming
  Federation §10; REVERSES the earlier short-name calls).** When a Sentinel tool is a *port* of a standard
  indicator, its `<Thing>` is that indicator's full name, verbatim — `StochasticTripleFilter` →
  `SentinelStochasticTripleFilter` / "Sentinel Stochastic Triple Filter", NOT "Sentinel Stoch Filter". Never
  abbreviate or re-brand a port; the name must trace back to its source. (Tools with no external source —
  Council, Clock, Eye, … — keep their own name.) The withdrawn short names (Sentinel ADX/CCI/Envelope/
  Compression) become the fidelity names at each tool's next bump.
- **⚠ Layer ① carries the VERSION (amended 2026-07-10 — see Naming Federation §9).** The display Name is
  `"Sentinel <Thing> v<M>.<m>.<p>"` (e.g. `"Sentinel Trend v1.0.0"`), plus a trailing ` (DEV)` while the
  build is under development (dropped as the freeze step). **Reason:** NT's Indicators picker "Available"
  pane lists tools by display `Name`, not class name — so the `_vX_Y_Z` class suffix is invisible at
  selection time and two in-dev versions read as identical `Sentinel <Thing>` rows. The version string
  must match the `_vX_Y_Z` class suffix + header CHANGELOG; all three move together on a bump. Still
  display-only, so retrofit freely.

Base rules:
- File = class = version suffix, all in sync. Keep name, class, `Name`, version-suffixed enums, and display
  strings ("v0.1.0") in sync. **Bump the version + update the in-file changelog for any important change**
  (history is per-file, not git). Old versions are FROZEN — never edit them. Minor bump (0.x.0) =
  architectural rework; patch (0.0.x) = incremental.

### Namespace grouping (adopt at each tool's NEXT bump)
Suite indicators/strategies live in a sub-namespace so they cohere in code AND cluster in the indicator picker:
- **Indicators** → `namespace NinjaTrader.NinjaScript.Indicators.Sentinel`. **⚠ Strategies STAY in the BASE `NinjaTrader.NinjaScript.Strategies` namespace** — a sub-namespaced strategy compiles but **never appears in NT's Strategy selector** (verified 2026-07-07, SentinelBridge). Only indicators fold into picker sub-folders; strategies carry the "Sentinel" tell via the class prefix + display Name only.
  NT's codegen is namespace-aware — it emits `Indicators.Sentinel.<Type>` wrappers exposed by simple method name,
  so **hosting from a strategy/indicator/MA-column still works** (proven by vendor `Indicators.LizardIndicators`,
  `.AlgoTrader`, `.AlgoAlpha`). AddOns already use `AddOns.Sentinel`. (File folder is independent of namespace.)
- **The picker GROUPS BY SUB-NAMESPACE into expandable folders** (VERIFIED 2026-07-05 — a "Sentinel" folder
  appeared; every vendor pack is likewise a folder). Root-namespace indicators sit flat at the top. The
  namespace clusters them **and** (per the law above) the class carries the `Sentinel` prefix, so it reads
  "Sentinel › SentinelTrend". Class names must stay globally UNIQUE (the version suffix ensures it → no
  generated-wrapper collision).
- **Never retroactively move a placed indicator.** Namespace + class are its serialization identity; changing
  either drops it off saved charts/workspaces (same as a version bump). Adopt at the **next version bump only**;
  frozen old versions stay in their old namespace/name. See memory `sentinel-namespace-and-naming`.
- **GOTCHA — custom enums on a `.Sentinel` indicator: declare them in the class's own `…Indicators.Sentinel`
  namespace AND add `using …Indicators.Sentinel;` at the file top.** VERIFIED 2026-07-05 on Eye (3 tries, cost real
  time). NT's generated host-wrapper is emitted in `namespace …Indicators`, references the indicator's own type
  QUALIFIED (`Sentinel.Eye_v1_1_0`) but leaves custom **enum params BARE** (`SentinelEyeDirectionMode directionMode`).
  KEY FACT: **the generated `#region` shares THIS file's `using` directives** (same compilation unit). So:
  - enum in `.Sentinel` **without** the using → bare name can't see the child namespace (`CS0246`), or binds to a
    frozen old version's same-named enum in base `NinjaTrader.NinjaScript` (`CS0019`/`CS0266` mismatch).
  - enum in `.Indicators` → resolves, but it **changed how NT qualified the class** in the MA/Strategy partials
    (emitted `Indicators.X` instead of `Indicators.Sentinel.X` → `CS0234`).
  - **enum in `.Sentinel` + `using …Indicators.Sentinel;`** → the region resolves the bare enum via the using, and
    the class qualifies correctly. This mirrors how CompressionBase's `using …AddOns.Sentinel;` resolves
    `SentinelCardCorner`. ✅ This is the pattern.
  Also **keep only ONE version of the tool in the tree** — two versions' same-named enums re-collide (`CS0101`).
  Another reason to **archive old versions when rehoming** (below).
- **Rehome mechanics:** `cp` the file, strip the generated `#region` to EOF, change `namespace`+class+`Name`+header,
  add to csproj, **F5 (authoritative)**. NT running re-appends generated regions + auto-adds the csproj entry
  (dedupe CS2002) and may briefly emit stale-name CS0111/CS0102/CS0246 region ghosts — strip-to-zero + F5 clears them.
- `SentinelCore` and the services are edited **in place** (stable symbol other tools bind to) — internal version const only.
- Every file opens with a header block: purpose, design/order/Sentinel notes, a `⚠ validate on SIM` line for anything that submits live orders, then `CHANGELOG`.

---

## 8. Build & verify workflow

NT compiles **every `.cs` under `bin\Custom` into one `NinjaTrader.Custom.dll`** — one broken file blocks the whole compile. **NT's F5 is authoritative.**

Headless sanity-check (flaky; produces GHOST errors NT does not hit):
```bash
dotnet build NinjaTrader.Custom.csproj -t:Rebuild -p:UseWPF=false \
  -p:ImportWindowsDesktopTargets=false -nologo -clp:ErrorsOnly 2>&1 \
  | grep -E "error CS" | grep -viE "AlightenGEX|Energy\.cs|TrendArchitectBotV13" | sort -u
```
- **Known ghosts to IGNORE:** `Energy.cs` CS0104 (Brush ambiguous), `@@AlightenGEX…` CS0234, `TrendArchitectBotV13(81)` — env artifacts, not real. Also `CS0115 OnMarketData` on the newest panel, `MC1000 Infragistics`.
- **Real = a localized `CS` in a file YOU touched.** Cross-file "namespace missing" on vendor files is a ghost.
- **csproj-drop gotcha:** NT regenerates `NinjaTrader.Custom.csproj` on F5 and **drops** `<Compile Include>` lines for files it still compiles. Before a headless check, re-add the entries for your files (else the build reports "clean" without compiling them). NT re-adds them on its next F5. Watch for `CS2002` (duplicate) and dedupe.
- **WPF gotcha:** `using System.Windows.Shapes` collides with `NinjaTrader.Gui.Line` (CS0104) — alias `ShapeLine/ShapeEllipse/ShapePolyline/ShapePolygon`. NT F5 catches this; headless does not.
- **Skin XAML:** validate well-formedness after edits (`[xml](Get-Content -Raw f.xaml)`); one malformed file silently falls back to the default skin. Skins load at NT startup / on re-select, independent of the NinjaScript compile.

---

## 9. New-tool checklist

1. Pick the type (§5). **Apply the §7 FEDERATED NAMING LAW (all 4 layers):** display `Name = "Sentinel <Thing> v<M>.<m>.<p>"` (+ ` (DEV)` in dev),
   namespace `…Indicators.Sentinel` (indicators) **or BASE `…Strategies`** (strategies — sub-ns hides them from
   the selector), class/file `Sentinel<Thing>_vX_Y_Z` (lowercase `v`, three parts, underscores — e.g.
   `SentinelTrend_v1_0_0`, `SentinelBridge_v0_2_0`), cyan card + label remover.
   File = class = version suffix, all in sync. Header block + changelog + `⚠ SIM` line if it trades.
2. Palette fields (§3) — copy verbatim. No new colors.
3. Header: cyan eye + wordmark + version chip. One cyan accent; green/red only for money/direction.
4. WPF from the shared helpers (§3); on-chart card from the glass recipe (§4).
5. Orders (if any): account-level unmanaged (§5); chart-scoped flatten; `OrderEntry.Manual`.
6. Sentinel: state advisory-vs-blocking in the header; wire `SentinelCore` (§6).
   **COUNCIL PROTOCOL (mandatory for any signal/regime/bias/context emitter):** the tool MUST (a) **publish a
   `…State` seam** to `SentinelCore` carrying its read as INT/double/bool (never couple to an enum — publish
   `(int)`); (b) gate it behind a `PublishState` (or `Publish…`) property that **DEFAULTS ON** — do NOT ship it
   dark (the 3-voters-off-by-default miss cost us; audited & fixed 2026-07-07); (c) be **wired into the Council**
   as a VOTER (directional → `AddVote`), a MODULATOR (context that scales conviction), or a VETO (hard gate) —
   and added to the Council's card/Reasons audit. A hidden `Signal` plot alone is NOT enough — the Council reads
   seams, not plots (that was the CompressionBase gap). If it has no directional/context read, say so in the header.
   **(Consuming a HUMAN interdiction rather than publishing a sensor read? That's the same idiom pointed the other
   way — publish `HelmIntent`, drain by `instanceKey`, publish `HelmState` back; see §6e.)**
7. **CORPUS RULE (mandatory) — a sensor NEVER records; only the Recorder records.** A regular
   signal/regime/context indicator MUST NOT write to the training corpus. `Sentinel\Excursions\council\<schema>\`
   is owned **solely** by the dedicated `SentinelExcursionRecorder` — **one writer, one schema per folder.** A
   tool that wants to characterize its OWN signal (a baseline) writes to
   `Sentinel\Excursions\_baselines\<signal>\<schema>\`, gated behind a property that **DEFAULTS OFF**, opt-in
   only — never the training path, never on by default. Corpus recording is a *training-pipeline* concern, not a
   sensor feature; a corpus that anyone might `train.py` over must be one-writer/one-schema or the model learns
   the mix. (CompressionBase shipped a default-ON `CBRK` logger into the shared dir — it muddied the corpus and
   cost a debug detour; default-OFF + `_baselines\` routing, 2026-07-11. See memory `corpus-hygiene-and-fill-fidelity`.)
8. Add `<Compile Include>` to the csproj; run the headless check (§8); then **F5 in NT (authoritative)**.
9. Update this doc if you introduced a new pattern. Update `Docs/ROADMAP.md` + memory.

---

*Related: `CONTRIBUTING.md` (build rules/map) · `Docs/ROADMAP.md` (status) · `Docs/SENTINEL-CONTRACTS.md`
(service seams) · memory `sentinel-suite-architecture`, `sentinel-skin`, `gtrader21-panel-integration`.*
