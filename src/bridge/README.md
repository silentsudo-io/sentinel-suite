# 🌉 The Bridge

**Rung 7 · autopilot.** Consumes the Council's verdict and executes it — the automated counterpart to
the Deck's manual trading.

## What's here
- **`SentinelBridge_v0_2_0.cs`** — reads `CouncilState`, applies the conviction floor and the active
  profile, and submits.

## ⚠ Read this before running it with money
- It executes a verdict; it does not judge one. **If the Council has no edge on your instrument, the
  Bridge automates that.** Grade first.
- **NinjaTrader's Strategy selector does not surface sub-namespaced strategies.** This lives in the
  base `NinjaTrader.NinjaScript.Strategies` namespace deliberately — move it and it will compile
  cleanly and never appear in the list.
- Managed strategy positions must not be closed by hand or by a raw order. Doing so desynchronises the
  strategy's internal position from the account and blocks every new entry until you disable and
  re-enable it.

## Install
1. Copy [`../runtime/`](../runtime/) (shared runtime — once) and this folder's `Strategies/` into
   `Documents\NinjaTrader 8\bin\Custom\`.
2. Press **F5** in the NinjaScript Editor.
3. Right-click a chart → **Strategies** → **SentinelBridge**.

---

Part of the [Sentinel Suite](../../README.md) · MPL-2.0
