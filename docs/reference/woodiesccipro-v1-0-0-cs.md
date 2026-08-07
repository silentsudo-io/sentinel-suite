# WoodiesCCIPro_v1_0_0.cs

> `bin/Custom/Indicators/WoodiesCCIPro_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 1223 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `WoodiesCCIPro_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors` |
| **Publishes seams** | `CciState` |
| **Documented by** | _no doc tracks this artifact_ |
| **Depends on this** | [Council_v1_11_0.cs](council-v1-11-0-cs.md), [SentinelCockpit_v0_1_0.cs](sentinelcockpit-v0-1-0-cs.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 WoodiesCCIPro — Woodies CCI / Turbo-CCI trend-filter oscillator   (Sentinel-graded)
 File: WoodiesCCIPro_v1_0_0.cs   |   Version: v1.0.0   |   namespace …Indicators.Sentinel
─────────────────────────────────────────────────────────────────────────────
 The Sentinel-grade upgrade of WoodiesCCIProV002. ALL of the trend/signal engine is preserved
 verbatim (raw + persisted trend state machine, turbo/slope/persistence/strict/neutral-suppression
 confirmation, weakening/strengthening logic, ZLR + Hook signals, bar coloring, and the full 22-plot
 Strategy-Builder series map — same indices). What changed is the *surface*:
   • REHOMED to namespace  NinjaTrader.NinjaScript.Indicators.Sentinel  → clusters under the "Sentinel"
     indicator-picker folder. Clean class name (design-system §7). NEW type identity vs V002 (namespace +
     name changed) → re-add on charts; V002 stays a FROZEN fallback.
   • SENTINEL PALETTE — default plot/line/bar brushes remapped to the Sentinel tokens (cyan = the primary
     watched line; green/red = bull/bear DIRECTION; mute = neutral; amber-dim = weakening). Still fully
     user-customizable via the Brush properties.
   • SENTINEL GLASS CARD (SentinelSkin.Painter) docked in the oscillator panel via CardLayout — trend-state
     pill + Main/Turbo CCI hero + strength track + slope/signal row. Never overlaps another Sentinel card.
   • LABEL REMOVER (mandatory) — NT's chart name-label hidden by default (ShowIndicatorLabel to restore).
   • SENTINEL PUBLISH — broadcasts SentinelCore.CciState (SetCciState) each bar so GTrader21/Eye/strategies
     can consult "Woodies trend is bull and not weakening" (SentinelCore v1.5.0 seam).

 Edge lane: NO orders — a trend-filter/observer only. The Values[] signal plots feed Strategy Builder.

 CHANGELOG
   v1.0.0b (in-place 2026-07-07) — SENTINEL PLOT SKIN: OnRender paints a glass PanelWash (covers stock plots)
            + a bottom TREND RIBBON (per-bar state) + themed 0/±100 reference lines + glowing Main (cyan) /
            Turbo (mute) CCI lines. Toggle SentinelPlotSkin (default ON); stock gridlines off. §4c. No logic change.
   v1.0.0a (2026-07-06) — default CardCorner TopLeft → TopRight (card docks on the right by default;
            in-place patch, NOT a rename — a rename would drop it off saved charts. Existing placements keep
            their serialized corner; flip the "Card corner" property or re-add to move an existing one.)
   v1.0.0 — Sentinel-grade fork of WoodiesCCIProV002 (frozen). Rehomed to Indicators.Sentinel, clean name.
            Sentinel palette defaults, glass card (CardLayout), label remover, and SentinelCore CciState
            publish seam. Trend/signal LOGIC + plot indices unchanged (drop-in for the V002 Strategy Builder
            series). ⚠ New serialization identity — existing V002 placements keep using V002.
```

