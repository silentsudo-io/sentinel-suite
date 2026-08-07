# BuySellVolumePressureMountain_v1_0_0.cs

> `bin/Custom/Indicators/BuySellVolumePressureMountain_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 758 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `BuySellVolumePressureMountain_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors` |
| **Publishes seams** | `PressureState` |
| **Documented by** | _no doc tracks this artifact_ |
| **Depends on this** | [Council_v1_11_0.cs](council-v1-11-0-cs.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 Buy/Sell Volume Pressure Mountain — Sentinel-homed rebuild of BuySellVolumePressureMountainV001/V002
 File: BuySellVolumePressureMountain_v1_0_0.cs   |   Version: v1.0.0   |   namespace …Indicators.Sentinel
─────────────────────────────────────────────────────────────────────────────
 WHAT IT IS
   Sub-panel oscillator that splits each bar's volume into buy vs sell "mountains" (green up / red down),
   smooths them into a buy% / sell% pressure reading, and surfaces the state in a Sentinel glass card.
   Edge lane — a discretionary READ tool, submits no orders.

 IMPROVEMENTS OVER V001/V002
   • HYBRID order-flow source: classifies TRUE bid/ask volume via OnMarketData when live ticks are present,
     and falls back to the OHLC candle-shape proxy on historical/backtest bars (or when forced off). The card
     lights TICK (cyan) vs OHLC (mute) so you always know which you're reading.
   • PRESSURE DIVERGENCE flags: marks a swing high where buy-pressure fails to confirm (bearish) or a swing
     low where sell-pressure fails to confirm (bullish) — a reversal tell the originals never surfaced.
   • Full Sentinel styling: SentinelSkin.Painter glass card (CardLayout anti-overlap + CardCorner), suite
     palette (green=buy, red=sell, cyan=live), label remover, Indicators.Sentinel picker folder.
   • Simplified: dropped V001's fiddly absolute-volume Long/Short signal block (instrument-specific, rarely
     useful). The ratio-based STRONG dominance markers are kept (optional).

 DESIGN-SYSTEM NOTES  (Docs/SENTINEL_DESIGN_SYSTEM.md)
   §4b Painter glass card + CardLayout + mandatory label remover · §7 namespace/naming.
   OnMarketData only fires realtime / tick-replay, so historical load auto-uses the proxy (accumulator == 0).

 CHANGELOG
   v1.0.1 (in-place 2026-07-26) — THE …State SEAM, which this tool shipped without. It has been computing an
            order-flow opinion that nothing in the suite could consult (design system §9 item 6 miss).
            Publishes SentinelCore.PressureState (Core v1.45.0): BuyPct/SellPct/Delta/Dir/DomRatio/Strong/
            Divergence/TickBacked, scope-keyed and BARE (sensors are shared across lanes), PublishState
            default ON, plus an OnMarketData heartbeat so a quiet tick/volume chart reads STALE not ABSENT.
            New Council voter tag BSP at **weight 0.0 (AUDITION)** — the 2026-07-26 re-test killed all 19
            voters and every one was PRICE-derived; this is genuine bid/ask-classified ORDER FLOW, the one
            untested family, so it is recorded and graded before it is allowed to move a verdict.
            ⚠ TickBacked is load-bearing: OnMarketData is realtime-only, so a historical rebuild falls back
            to the OHLC candle-shape proxy, which is itself price-derived. Never grade a proxy row as flow.
            KEPT IN PLACE (no v1_1_0 rename): class + namespace are an indicator's serialization identity,
            and renaming would silently drop it off every saved chart for a purely additive change.
   v1.0.0 (in-place 2026-07-07) — SENTINEL PLOT SKIN: OnRender paints a glass PanelWash (covers stock plots)
            + zero baseline + two-sided gradient HISTOBARS (buy = CUp above zero, sell = CDown below). Toggle
            SentinelPlotSkin (default ON); stock gridlines off. Design system §4c. No logic change.
   v1.0.0 (2026-07-05) — first cut. Fresh Sentinel identity; hybrid tick/OHLC classification; divergence
                         flags; glass-card readout; V001 long/short absolute-volume signals removed.
                         FIX: freeze all WPF brushes (defaults + deserialized) — unfrozen brushes threw
                         "calling thread cannot access this object" in OnBarUpdate → nothing plotted.
                         Mountains recolored to the candle skin: buy = teal #009999, sell = grey #8E8E8E.
```

