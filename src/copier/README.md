# 🔀 The Copier

**Rung 8 · mirror one account to many, prop-rule aware.** A headless fill-mirror service:
Primary → Followers, with the rules that keep a prop account legal.

## What's here
- **`SentinelCopierService_v0_1_0.cs`** — same-provider prop rule, GC→MGC cross-instrument sizing,
  kill/governor/session gates, manual-assist tickets and copy-slippage capture.

## Configure
`Sentinel\Copy.conf` (template in [`../config/Copy.conf`](../config/Copy.conf)), or the **Copy** tab
in the Dashboard.

⚠ Copying multiplies mistakes as faithfully as it multiplies edge. Run it in Sim across every follower
before a single funded account is attached.

## Install
1. Copy [`../runtime/`](../runtime/) (shared runtime — once) and this folder's `AddOns/` into
   `Documents\NinjaTrader 8\bin\Custom\`.
2. Press **F5** in the NinjaScript Editor.
3. The service loads with the assembly; configure it from the Dashboard's **Copy** tab.

---

Part of the [Sentinel Suite](../../README.md) · MPL-2.0
