# 🛡️ The Prop-Survival Kit

**Rung 6 · keep your funded account alive.** If you trade a prop evaluation, this is the rung that
matters most.

## What's here
- **`SentinelRiskService`** — daily loss limits, trailing drawdown, the consistency governor.
- **`SentinelStateService`** — the kill switch and shared runtime state.
- **`SentinelAlertService`** — alerting when a limit is *approached*, not after it is breached.
- **`SentinelArcService`** — per-account fleet state (which account is live, flat, or idle).
- **`SentinelNewsService`** — economic-calendar blackouts.

## The honest framing
An evaluation is a **constrained optimisation**, not expectancy maximisation. A high hit-rate with
small wins and tight stops often passes an eval while losing money long-term; maximum expectancy often
fails one on a drawdown rule. **Pick which you are optimising deliberately — they pull apart.**

⭐ Route sizing through `SentinelCore.SizedQuantity()` (which clamps) and then `GateEntry(riskDollars=0)`
(which validates). Passing a non-zero `riskDollars` makes the Gate *re-size*, and a size that rounds
below the floor returns 0 and silently blocks every entry.

## Install
1. Copy [`../runtime/`](../runtime/) (shared runtime — once) and this folder's `AddOns/` into
   `Documents\NinjaTrader 8\bin\Custom\`.
2. Press **F5** in the NinjaScript Editor.
3. The services load with the assembly; configure them from the Cockpit.

---

Part of the [Sentinel Suite](../../README.md) · MPL-2.0
