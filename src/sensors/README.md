# 📡 Sentinel Sensors

**Rung 1 · the Intelligence axis.** Standalone indicators that read the tape and publish a clean
`…State` seam. Each works on a plain chart and draws a glass card.

## What's here
**8 hero signals** — Sentinel Trend · ADX Pro · Woodies CCI Pro · VolEnvelope · Compression Base ·
Liquidity Walls · God Reversal · WAE
**13 more sensors** — SuperTrend · Regime · Structure · Exhaustion · Harmonic · Z-Score · VIDYA ·
Parabolic SAR · ADXVMA · BrickCounter · BSVPMountain · Stochastic Triple Filter · Bars-per-session advisor
**3 bar types** (`BarsTypes/`) — TBars · TbarsCount · **Flux** (order-flow imbalance)

## Install
1. Copy [`../runtime/`](../runtime/) (shared runtime — once) and this folder's `Indicators/`,
   `BarsTypes/`, **and `Shared/`** into `Documents\NinjaTrader 8\bin\Custom\`.
   (`Shared/TbarsSudoV3Config.cs` is a compile-time dependency of the TBars bar type — omit it and
   the whole Custom tree fails to compile with `CS0246`/`CS0103`, since NT builds it as one assembly.)
2. Press **F5** in the NinjaScript Editor.
3. Right-click a chart → **Indicators** (or **Bar Types**) → **Sentinel**.

Every signal publishes a `…State` seam other tools can consult — the seam is how the suite fuses
sensors without any of them knowing about each other. See [`../../docs/`](../../docs/).

> Licensing: `LiquidityWalls` is © TradingIQ (MPL-2.0); `StochasticTripleFilter` is a port of
> AlgoTrade_Pro (MPL-2.0). See [NOTICE](../../NOTICE).
