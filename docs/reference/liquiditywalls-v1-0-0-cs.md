# LiquidityWalls_v1_0_0.cs

> `bin/Custom/Indicators/LiquidityWalls_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 563 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `LiquidityWalls_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors` |
| **Publishes seams** | `LiquidityState` |
| **Documented by** | _no doc tracks this artifact_ |
| **Depends on this** | [Council_v1_11_0.cs](council-v1-11-0-cs.md), [SentinelCockpit_v0_1_0.cs](sentinelcockpit-v0-1-0-cs.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 LiquidityWalls — order-flow ABSORPTION detector + liquidity WALL zones   (Sentinel-homed)
 File: LiquidityWalls_v1_0_0.cs   |   Version: v1.0.0   |   namespace …Indicators.Sentinel
─────────────────────────────────────────────────────────────────────────────
 A faithful NinjaScript port of the TradingIQ "Liquidity Walls" Pine v6 study, rebuilt to the
 Sentinel design system (SentinelSkin glass card + CardLayout + label remover + SentinelCore seam).

 WHAT IT DOES (the absorption thesis):
   • Per bar it measures net aggressive order flow (DELTA = Σ signedVolume of the lower-timeframe
     ticks, tick-rule classified) and how far price actually moved (moveTicks).
   • A rolling 100-bar OLS regression predicts how far price *should* have moved for that delta
     (expectedTicks = α + β·delta). The signed shortfall — price failing to follow the flow —
     is z-scored. A HIGH z = ABSORPTION: aggressive orders hit a passive wall and price barely budged.
   • On an absorption event (z ≥ threshold) it drops a liquidity WALL one ATR thick: above the high
     when up-flow was absorbed (RESISTANCE), below the low when down-flow was absorbed (SUPPORT).
     Walls extend right until price trades clean through their far edge, then fade.

 DELTA SOURCE — a 1-tick added series (AddDataSeries(Tick,1)); ticks are buy/sell classified by the
   TICK RULE (uptick = buy, downtick = sell, zero-tick carries the last side) — this mirrors the Pine
   study's non-tick granularity `sign(close-close[1])` and works on historical + live data. Per-bar
   delta is attributed order-independently via an accumulator + a primary-synced Series write (so it's
   correct regardless of whether the primary bar-close or the boundary tick fires first).
   ⚠ Historical delta is only as granular as the provider's historical tick data; live is exact.

 SENTINEL:
   • namespace Indicators.Sentinel → groups under the "Sentinel" picker folder. Clean class name.
   • Glass card via SentinelSkin.Painter, docked with CardLayout (never overlaps other Sentinel cards).
   • Label remover (mandatory) — NT's chart name-label hidden by default.
   • Hidden "Signal" plot (Values[1]) = absorbSide on an absorption bar (+1 resistance / -1 support / 0)
     so the Deck SIGNAL ARM / any consumer reads it generically (design-system §6b convention).
   • Publishes SentinelCore.LiquidityState (SetLiquidityState) — absorption z + nearest wall above/below —
     so GTrader21/Deck/Eye can veto entries into a wall (SentinelCore v1.4.0 seam).

 Edge lane: NO orders — a detector/observer only.

 CHANGELOG
   v1.0.0 — First cut. Port of TradingIQ "Liquidity Walls": tick-rule delta, 100-bar OLS regression,
            failTicks z-score absorption, ATR-thick walls w/ break-through fade, optional inefficiency
            candle coloring (cyan gradient by z) + optional expected-close phantom dot. Sentinel card +
            CardLayout + label remover + hidden Signal plot + SentinelCore LiquidityState publish seam.
            (The Pine study's vestigial IQZZ zigzag — computed but never rendered — is intentionally
            omitted; and the last-bar box-shrink / commented gradient-line render are dropped.)
```

