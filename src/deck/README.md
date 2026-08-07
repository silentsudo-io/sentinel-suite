# 🎛️ The Deck (+ Cockpit + Dashboard)

**Rung 5 · the manual trading surface.** A chart-native order deck, plus the command and monitoring
surfaces around it.

## What's here
- **`SentinelDeck_v0_2_6.cs`** — the on-chart trader: bracket entry, position management, SIGNAL ARM.
- **`SentinelCockpit_v0_1_0.cs`** — the command surface (roster, profiles, Helm).
- **`SentinelDashboard_v1_0_0.cs`** — accounts, risk, copier and excursion tabs.
- **`Docs/`** — the Deck spec and its testing guide.

## ⚠ Before you arm anything
**Deck auto-fire has not been live-validated.** It ships enabled in a preview build. Run it in Sim,
watch it fire, and only then consider it anywhere near a funded account.

⭐ **SIGNAL ARM reads another indicator's plot generically** — it does not scrape drawings. Resolve the
reference on the UI thread and read `.Values` on the data thread; enumerating `ChartControl.Indicators`
from the data thread throws. A one-bar pulse is read from the **just-closed** bar and re-checked every
tick, so the bar-boundary race self-heals — meaning it fires the bar *after* the signal, confirmed and
non-repainting.

## Install
1. Copy [`../runtime/`](../runtime/) (shared runtime — once) and this folder's `Indicators/` into
   `Documents\NinjaTrader 8\bin\Custom\`.
2. Press **F5** in the NinjaScript Editor.
3. Right-click a chart → **Indicators** → **Sentinel**.

---

Part of the [Sentinel Suite](../../README.md) · MPL-2.0
