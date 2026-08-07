# SentinelSkin.cs

> `bin/Custom/AddOns/SentinelSkin.cs`

| | |
|---|---|
| **Family** | AddOns / runtime |
| **Version** | — |
| **Size** | 1415 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `Palette` |
| **Namespace** | `NinjaTrader.NinjaScript.AddOns.Sentinel` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
one at https://mozilla.org/MPL/2.0/.

Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
─────────────────────────────────────────────────────────────────────────────
============================================================================
SentinelSkin — the shared "flight-instrument" drawing framework for the Suite
============================================================================
ONE library every Sentinel indicator/strategy uses to draw cohesive on-chart
cards, text, dots, pills, gauges, sparklines and tracks — so SentinelEye,
SignalExcursionRecorder, CompressionBase, the Deck, GTrader21, and every FUTURE
tool look and feel like they belong to the same instrument panel.

USAGE (SharpDX OnRender):
  private readonly SentinelSkin.Painter _sp = new SentinelSkin.Painter();   // field
  ...
  protected override void OnRender(ChartControl cc, ChartScale cs) {
      base.OnRender(cc, cs);
      if (RenderTarget == null) return;
      _sp.Begin(RenderTarget);
      var r = _sp.Card(x, y, 300f, 150f, active ? SentinelSkin.CLine : SentinelSkin.CWarn);
      _sp.Dot(r.Left + 19f, r.Top + 23f, active ? SentinelSkin.CAccent : SentinelSkin.CWarn, glow:true);
      _sp.Text("SENTINEL EYE", r.Left + 30f, r.Top + 14f, 160f, 18f, SentinelSkin.CInk, 12f, semibold:true);
      _sp.Money(r.Left + 15f, r.Top + 42f, pnl, pnl >= 0 ? SentinelSkin.CUp : SentinelSkin.CDown);
      _sp.End();
  }
  protected override void OnStateChange() { ... if (State==State.Terminated) _sp.Dispose(); }

The Painter owns its DirectWrite factory (disposed in Dispose) and caches brushes
(keyed to the RenderTarget) + text formats — so OnRender never allocates per frame
except the handful of gradients/geometries, which End() releases. See
Docs/SENTINEL_DESIGN_SYSTEM.md §"Indicator framework".

Palette = the Sentinel tokens (identical to SentinelDashboard + the Sentinel skin).
The one rule: cyan = live/watching; green/red = money + direction.

CHANGELOG
  2026-07-10 — CardLayout: FLICKER FIX (the collapse layout OSCILLATED; the chart flashed). Three defects, all
    mine, all introduced by the collapse work the day before:
    (a) THE VERDICT WAS RECOMPUTED INSIDE ALL 19 `Place()` CALLS, EVERY FRAME, from scratch. Now `Decide()`
        computes a column's gap/scale/collapsed-set ONCE and caches it (`RecomputeMs` = 400ms). Layout does not
        need to track the frame rate — and deciding every frame is precisely what let it oscillate.
    (b) NO HYSTERESIS. On the fit boundary: collapse → fits → expand → doesn't fit → collapse … forever. Collapse
        is now STICKY: cheap to collapse, but a card must fit with `ExpandHysteresisPx` (18px) of room TO SPARE
        before it may expand again.
    (c) UNSTABLE COLUMN ORDER. `Ord` is keyed by TYPE NAME, so two instances of one tool (the user runs two
        SentinelExcursionRecorders) TIE — and `List.Sort` is unstable, so tied slots swapped each frame and the
        collapse victim changed IDENTITY. Slots now carry a monotonic `Seq`; the column sorts by (Ord, Seq),
        a total order.
    Diagnosed straight from sentinel.log, which showed the same-size set with a DIFFERENT victim flipping back and
    forth (`TopRight … CompressionBase` ⇄ `TopRight … SentinelGodReversal`). The log now signs on card NAMES, not
    just the count — a count-only check would have hidden exactly this.
  2026-07-09 — CardLayout: THREE LAYOUT BUGS FIXED (found by a live Blueprint screenshot showing the Deck's risk
    card BURIED under the God Reversal card). See the CardLayout doc-comment + Docs/SENTINEL_RAIL_SPEC.md §1.
    (1) ORDER DRIFT — stack order was registration order, and a pruned card was re-appended to the END on return,
        so it walked down its column. Now a STICKY per-(panel,type) ordinal that survives pruning.
    (2) OVERFLOW — `off` grew unbounded: a column taller than the panel pushed its tail off the edge and the card
        was SILENTLY LOST. A column now FITS ITSELF to its budget, in order of least harm: compress the gap →
        SCALE-TO-FIT (a Direct2D transform in Painter.Card; text is vector so it stays crisp) → only then hide
        the tail. Hidden cards are counted (`OverflowCount`) + logged, never silent. `MinCardScale` is the
        legibility floor; hiding one card lets the survivors re-expand, so the loop recomputes rather than settles.
    (3) CROSS-CORNER COLLISION — a corner ignored the opposite corner on the SAME EDGE, so a long TopRight sensor
        stack grew straight through the BottomRight-anchored Deck card. Top columns now RESERVE the bottom
        column's height (≤60% of panel). POLICY: top yields to bottom, never the reverse — burying the risk card
        is worse than hiding a sensor. PINNED cards (the Bridge's ARM button) are never hidden.
    `Place()` gains an OPTIONAL `pinned` param → all 19 existing call sites compile untouched.
    ⚠ SentinelSkin now depends on SentinelCore (for the overflow log). Both were already hard deps of every
    Sentinel indicator (see Docs/SENTINEL_SHIP_MANIFEST.md), so the ship surface is unchanged.
  2026-07-09 — AMBER, the 6th theme (warm dark / night-watch) — the FIRST theme to move the ACCENT OFF CYAN.
    The law is "ONE accent = live/watching"; it never said the accent must be cyan. Consumers are untouched
    (they read CAccent). ⚠ Moving the accent to amber FORCED Warn off amber → COOL BLUE #6FA8FF, because
    "live" and "caution" must never share a hue. Amber is the only theme whose Warn is cool: deliberate.
    Also: TryParseTheme is now PUBLIC, so UI surfaces (the Deck's theme button) map word→Theme from ONE place
    instead of each keeping an if-chain that rots when a theme lands.
  2026-07-09 — BLUEPRINT, the 5th theme (cyanotype drafting paper). Deep architect's-blue grounds,
    drafting-white ink, and a LIFTED cyan accent (#5FE3F2 — plain #3FD1E0 reads as mud on a blue ground).
    Skin `templates\Skins\Sentinel Blueprint\` lifts the GRID LINES well above the paper; the grid is the
    theme's signature. Proof the 4-place extension contract holds: Palette · Theme · TryParseTheme · SkinBgTheme.
  2026-07-09 — OBSIDIAN, the 4th theme (true-black OLED) + a HARDENED skin-follow glue.
    • New `Palette Obsidian` (Void = literal #000) + `Theme.Obsidian` + theme.txt word "obsidian".
    • New per-theme `Palette.GlowMul` scales every glow/halo ALPHA (Dot/Pill/HistoBar/GlowLine);
      Obsidian = 0.6 because bloom that reads as light on navy reads as SMEAR on true black.
      ⚠ GlowMul is a class FIELD → an unset Palette defaults to 0 (no glow). Every Palette sets it.
    • THE GLUE IS NO LONGER LUMINANCE-ONLY. `SkinBgTheme` maps each Sentinel skin's EXACT
      ChartControl.ChartBackground hex → its theme, because luminance cannot separate two dark
      themes (Sentinel #0F1524 and Sentinel Obsidian #000000 share the same band — Obsidian would
      have silently resolved as Dark). Luminance survives as the fallback for non-Sentinel skins.
    • Theme.txt parsing centralised in `TryParseTheme` — adding a theme now touches ONE switch.
  2026-07-07 — added the PLOT-SKIN primitives (PanelWash / RegimeShade / Baseline / HistoBar / GlowLine):
               the sub-panel counterpart to the card, so an indicator's histograms/lines/background match
               the cards' glass material. Reference impl: SentinelWAE. See design system "Sub-panel plot standard".

CHANGELOG
  v1.1 (2026-07-05) — CardLayout: a shared card-stacking registry so cards from DIFFERENT
    Sentinel indicators/strategies never cover each other. Each card asks CardLayout.Place()
    for its rect in a chosen corner (SentinelCardCorner); cards docked to the same corner of the
    same chart panel AUTO-STACK vertically (gap-separated). Stale entries (indicator hidden/
    removed) self-prune after ~2s; Release(key) on Terminated for hygiene. Adopt in every card.
  v1.0 (2026-07-03) — first cut: palette (Color4 + WPF Color), fonts, Painter with
    B/Ba/Text/Card/Dot/Pill/Money/Track/Gauge/Sparkline/Line + caching.
============================================================================
```

