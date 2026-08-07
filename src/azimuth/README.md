# 🧭 Azimuth — the research workbench

**The suite's computation layer, host-agnostic.** Bar types, the engine and the parity gates in Python,
so Sentinel ideas can be researched outside NinjaTrader. **NinjaTrader is *a* host, not *the* host.**

## What's here (54 files)
- **`bars/`** — Renko, SentinelTBars and SentinelFlux ported to Python. 105 tests.
- **`engine/`** — a tick-resolution backtest engine: ask/bid fills, an order model with legs, partial
  fills and scale-outs. 87 tests.
- **`gates/`** — the parity harness. 33 tests, plus a selftest of 81 checks **proven able to fail**.
- **`app/`** — a Tauri + React shell over the corpus (DuckDB).

## 🔒 The parity law — the price of a second implementation
Anything implemented in both columns needs a **tiered equivalence gate, proven able to FAIL**, before
the Python side is trusted. Two implementations is fine; two *unproven* ones is how a research corpus
ends up describing a system nobody runs.

⚠ **The ports are NOT blessed.** An unrun gate is not a passing gate, and a failing one is not either.
Current state: Flux matches NinjaTrader **byte-identically for its first 559 bars**; Renko matches
`open/high/low/close` exactly across all 17 sessions, with the residual traced to a *data* gap rather
than port logic. Read `bars/gate.py` before drawing a conclusion from either.

## On the name
An **azimuth** is the bearing you take to a landmark **already in sight**, in order to establish where
*you* are. It is retrospective measurement, not a forecast — which is deliberate, and load-bearing.

---

Part of the [Sentinel Suite](../../README.md) · MPL-2.0
