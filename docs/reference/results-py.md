# results.py

> `Sentinel/Azimuth/engine/results.py`

| | |
|---|---|
| **Family** | Azimuth (Python) |
| **Version** | — |
| **Size** | 210 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | [SENTINEL_AZIMUTH_SPEC](../../SENTINEL_AZIMUTH_SPEC.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
Trades, the run result, and the metrics -- including the mandatory crossing-cost axis.

⭐ §5.4: "Crossing cost is a required axis." Every trade carries `spread_cost_ticks`
and `spread_cost_ccy` measured as the ADVERSE distance from the mid on both legs,
so the optimizer can plot it and the analyzer can colour by it. It is not a
derived nicety -- THE HORIZON says the P&L *is* the crossing cost, so the engine
records it per trade at the moment of the fill and never reconstructs it later.
```

