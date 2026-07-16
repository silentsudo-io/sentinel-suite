# Sentinel Suite — SHIP MANIFEST

> **⚠ Dated snapshot (2026-07-06).** The current ship set is larger than what's listed below — see
> **SENTINEL_BOUNDARY_INVENTORY.md** for the authoritative file→rung→bundle map
> (it adds Council, Bridge, Cockpit, WAE, GodReversal, SentinelFlux, the 5 orthogonal axes, and the data platform).
> The dependency-graph *principles* here (one-assembly compile, 2 shared runtime files, collision defense) still hold.

> **What this is:** the exact file set to export for each shippable Sentinel tool, the shared
> runtime it depends on, and the collision-risk names to defend before distributing to machines
> you don't control. Derived from the actual dependency graph (2026-07-06), not memory.
>
> **Read alongside:** the root `CONTRIBUTING.md` HARD BUILD RULES (one-assembly compile model) and the
> `sentinel-ship-manifest` memory.

---

## 0. The one fact that governs everything

NinjaTrader compiles **every `.cs` under `bin\Custom` into ONE assembly** (`NinjaTrader.Custom.dll`)
on the customer's machine. Consequences for shipping:

- A **missing type** (you shipped an indicator but not a type it references) → **CS0246** →
  **the customer's ENTIRE compile breaks**, not just your tool. None of their tools load.
- A **name collision** with something the customer already has (same class/enum name) → **CS0101**
  → same total breakage.

So "shipping a tool" = shipping a **compile-complete, collision-safe** file set. There is no
partial install.

---

## 1. The shared runtime (ship with EVERY Sentinel indicator)

Every subscribed indicator hard-references exactly **two AddOn files**. Both are self-contained
(no back-reference to any service/dashboard):

| File | Provides | Depends on |
|---|---|---|
| `AddOns/SentinelCore_v1_0_0.cs` | `SentinelCore` static seam — publish/consult state (`Get*`/`Set*`), `Log`, `SettingsDir`, `Ledger`, `State`, `Alerts` | static state + file I/O only |
| `AddOns/SentinelSkin.cs` | `SentinelSkin` painter (glass cards), `CardLayout`, `enum SentinelCardCorner` | `System` + WPF only |

**These two files are the Sentinel shared runtime.** Ship them with anything. They do NOT pull in
the Dashboard, Copier, Risk, Alert, State-service, Log, Arc, or Lens — those are optional runtime
infrastructure, never a compile dependency of an indicator.

**Runtime behavior with the services absent:** none. `SentinelCore.Get*` calls are all
staleness-gated (`maxAgeSec`) and return no-data → the consult path degrades to **neutral**.
State the indicator *publishes* simply sits unread. No exceptions thrown.
> ⚠️ Before ship, eyeball each indicator's no-data branch once — the neutral path is by design but
> should be confirmed per tool.

---

## 2. Per-tool ship sets

Each row = the complete export set. "Runtime" column = the 2 shared files from §1 (always required).

| Tool | Ship these files | + Shared runtime | Notes |
|---|---|---|---|
| **ADXPro** | `Indicators/ADXPro_v1_2_0.cs` | Core + Skin | Publishes `AdxState`. Standalone-clean. |
| **CompressionBase** | `Indicators/CompressionBase_v1_3_0.cs` | Core + Skin | Consumes `EyeVerdict` (optional), exposes hidden `Signal` plot. |
| **Eye** | `Indicators/Eye_v1_1_0.cs` | Core + Skin | Own enums (`SentinelEye*`) are in-file. Publishes `EyeVerdict`. |
| **VolEnvelope** | `Indicators/VolEnvelope_v0_2_0.cs` | Core + Skin | Publishes `EnvelopeState`; consumes `EyeVerdict` (optional). |
| **WoodiesCCIPro** | `Indicators/WoodiesCCIPro_v1_0_0.cs` | Core + Skin | Publishes `CciState`. Card renders in a sub-panel. |
| **SentinelTrend** | `Indicators/SentinelTrend_v1_0_0.cs` | Core + Skin | Consumes `AdxState`, publishes `TrendState`. |
| **SentinelTrend Strategy** | `Strategies/SentinelTrendStrategy_v1_0_0.cs` | Core + Skin (+ SentinelTrend indicator if it hosts it) | Companion strategy; consumes the indicator's plots. |
| **LiquidityWalls** | `Indicators/LiquidityWalls_v1_0_0.cs` | Core + Skin | Publishes `LiquidityState` veto. |
| **SignalExcursionRecorder** | `Indicators/SignalExcursionRecorder_v1_3.cs` | Core + Skin | — |
| **TBars (bars type)** | `BarsTypes/SentinelTBars_v1_0_0.cs` | Core (+ Skin if it draws) | Publishes `BrickState`. Verify exact filename before ship. |

**Special case — Deck** (`Indicators/Deck_v0_2_2.cs`): the manual trader. Hard-deps Core + Skin
like the rest, but is functionally tied to live account/order infrastructure and the SIGNAL ARM
plot-reader. **Not a "drop-in indicator" — ship as part of the full suite, not standalone.**

---

## 3. Collision-risk names (defend before public distribution)

These names go into the customer's shared assembly. If they already have a type with the same name,
CS0101 breaks their whole compile. Ranked by risk:

| Name | Risk | Mitigation |
|---|---|---|
| `SentinelCore` | **HIGH** — generic; a customer's own "Sentinel*" tool could collide | It's your shared singleton. Namespaced in `…AddOns.Sentinel`, but the **class name is still global-ish** in NT codegen. Keep it the ONE `SentinelCore` in any tree. |
| `SentinelSkin`, `CardLayout`, `SentinelCardCorner` | **HIGH** — `CardLayout` especially is a common name | `CardLayout` is declared in `SentinelSkin.cs`; consider renaming to `SentinelCardLayout` at the next Skin bump for public builds. |
| `SentinelCardCorner` | MED | Already prefixed — fine. |
| Per-tool enums (`SentinelEyeDirectionMode`, etc.) | LOW | Already `Sentinel`-prefixed + in `.Sentinel` namespace. |
| Indicator class names (`ADXPro`, `Eye`, `VolEnvelope`, `CompressionBase`) | **MED** — short, unprefixed | For public distribution consider the `Sentinel`-class-prefix convention (see `sentinel-namespace-and-naming` memory). These are also the **serialization identity** — renaming drops them off saved charts, so do it only at a version bump. |

**Golden rules for a clean import on a stranger's machine:**
1. Ship **exactly ONE version** of each file. Two versions in the tree re-collide on their
   same-named classes/enums (CS0101). (Internally enforced by `_archive\`.)
2. Ship the **2 shared runtime files** with every tool, deduplicated (never two copies of
   `SentinelCore`/`SentinelSkin`).
3. Custom `[NinjaScriptProperty]` enums must live in the tool's `.Sentinel` namespace (the Eye saga).
4. Namespace + class name = serialization identity — freeze them before ship.

---

## 4. Packaging procedure (NT Export → NinjaScript Add-On)

1. In NT: **Tools → Export → NinjaScript Add-On**.
2. Select the tool's `.cs` **plus** `SentinelCore_v1_0_0.cs` **plus** `SentinelSkin.cs`
   (NT does NOT auto-resolve AddOn dependencies — you pick them, or the customer's import
   compiles broken).
3. For a protected/obfuscated build, use NT's compiled-assembly export option.
4. **Test the export on a clean NT install** (or a VM) before distributing — that's the only way
   to catch a collision you don't have locally.

---

## 5. Minimal shippable unit — TL;DR

> **Any single Sentinel indicator = 3 files:** the indicator + `SentinelCore_v1_0_0.cs` +
> `SentinelSkin.cs`. No service stack. No runtime errors when the rest of the suite is absent.
> The only hard part is name-collision defense on machines you don't control.

---

*Changelog*
- **v1.0 (2026-07-06)** — initial manifest; dependency graph verified against the tree
  (all subscribers hard-dep exactly Core + Skin; services are optional runtime).
