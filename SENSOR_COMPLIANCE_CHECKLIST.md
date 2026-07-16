# Sentinel Sensor — Compliance Checklist

A new or ported Sentinel **indicator** must satisfy every applicable item before it ships. This is what
keeps the suite seamless (and what a reviewer checks). See [CONTRIBUTING.md](CONTRIBUTING.md) for the why.

## Identity — the 4-layer naming tell
- [ ] **File** named `Sentinel<Thing>_vX_Y_Z.cs`
- [ ] **Class** named `Sentinel<Thing>_vX_Y_Z`
- [ ] **Display** `Name = "Sentinel <Thing>"` set in `SetDefaults`
- [ ] **Namespace** `NinjaTrader.NinjaScript.Indicators.Sentinel`
      *(strategies only: stay in the BASE `…Strategies` namespace — carry the tell via class prefix + Name)*

## Look — the Sentinel design system
- [ ] Draws a **glass card** via `SentinelSkin.Painter` (header dot + title + state pill + track + mono rows)
- [ ] Card docks via `SentinelSkin.CardLayout` with a **`CardCorner`** property (cards auto-stack, never overlap)
- [ ] **Label remover**: a `ShowIndicatorLabel` property (default off) and, as the FIRST line of
      `State.DataLoaded`, `if (!ShowIndicatorLabel) Name = string.Empty;`
- [ ] **Palette tokens only** — colors come from `SentinelSkin` (`CInk`, `CUp`, `CDown`, `CCyan`, …); no
      hardcoded `Brushes.X` / raw hex on the chart

## Brain — the publish/consult protocol
- [ ] If it emits a **signal / regime / bias / context**, it **publishes a `…State` seam** to `SentinelCore`
      (add the seam to `SentinelCore` if new; INT/double/bool fields)
- [ ] `PublishState` property defaults **ON**
- [ ] **Scope-keyed** if the value varies with the chart's bar type (use `SentinelCore.ScopeOf(Instrument,
      BarsPeriod)`); instrument-keyed only if the value is bar-type-independent (e.g. session/macro)
- [ ] Wired into the **Council** as a voter / modulator / veto (+ named in its Reasons audit), if it's a
      decision input — *a hidden plot alone is not enough*
- [ ] Optional: exposes a hidden transparent `Signal` ±1 plot (`IsAutoScale = false`) if it emits a
      directional pulse, so generic consumers (e.g. the Deck) can read it

## Layering & safety
- [ ] References only its own layer or **below** — **a sensor never calls a Safety/order API**
      (`GateEntry`, `CanEnter`, governor, `SizedQuantity`, …)
- [ ] No order placement of any kind (sensors observe; they never trade)

## Build & housekeeping
- [ ] Compiles **clean under NT F5** (not just headless)
- [ ] Zero duplicate `#region NinjaScript generated code` blocks on disk
- [ ] **Version + in-file changelog** entry; prior versions left frozen
- [ ] Depends only on `SentinelCore` + `SentinelSkin` (the runtime) — runs with no errors standalone

## Provenance
- [ ] Original work, or a **clean-room** reimplementation of a *publicly-published* formula
- [ ] **No** code derived from proprietary / third-party engines
- [ ] Author added to `AUTHORS`
