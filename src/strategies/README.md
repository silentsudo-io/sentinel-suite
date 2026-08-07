# 📈 Strategies

**Reference strategies** built on the suite's own instrumentation — the Ledger, the excursion corpus
and the advisory Gate.

## What's here
- **`SentinelKeel_v0_1_0.cs`** — a range-filter stop-and-reverse with a full measurement rig bolted on
  and *nothing else changed*. Its acceptance test is an **equivalence gate**: at default parameters it
  must produce a trade list identical to the uninstrumented original.
- **`SentinelTrendStrategy_v1_0_0.cs`** — a minimal trend-following reference.
- **`SentinelTBarsEdgeProbe_v1_0_0.cs`** — a probe for measuring bar-type behaviour, not a strategy.

## Why the Keel is shaped the way it is
Every Sentinel call inside it sits in a `try/catch` and is a **leaf**: it may not `return`, `continue`
or mutate signal state, and the Gate is consulted, logged, and its answer **discarded**. A blocking
gate would change the trade set — which is exactly what a baseline cannot survive.
**Instrumentation that changes behaviour is not instrumentation.**

⚠ These are references for how to instrument a strategy. They are not trade recommendations.

## Install
1. Copy [`../runtime/`](../runtime/) (shared runtime — once) and this folder's `Strategies/` into
   `Documents\NinjaTrader 8\bin\Custom\`.
2. Press **F5** in the NinjaScript Editor.
3. Right-click a chart → **Strategies**.

---

Part of the [Sentinel Suite](../../README.md) · MPL-2.0
