# Reserved names — the Sentinel namespace registry

**This file is the source of truth for which type names the Sentinel Suite claims.** It exists so that
someone building on Sentinel can find out — *before* they write the file — whether a name is safe to take.

It is not a legal document and it is not about credit. It is about one property of the platform:

> **NinjaTrader compiles every `.cs` under `bin\Custom` into a single assembly.** Two types with the same
> name in the same namespace are a `CS0101`, and a `CS0101` anywhere stops **the entire tree** from
> compiling — every indicator and strategy the user owns, not just Sentinel. A name collision is not a
> conflict between two projects. It is an outage on a stranger's trading platform.

---

## The rule

**Reserved for this project:**

| | reserved |
|---|---|
| Namespaces | `NinjaTrader.NinjaScript.Indicators.Sentinel` and every child (`.Sensors`, `.Smoothers`, …) · `NinjaTrader.NinjaScript.AddOns.Sentinel` |
| Type prefix | `Sentinel*` — in **any** namespace, since NinjaTrader's picker and workspace files key on the type name |
| Config folder | `<Documents>\NinjaTrader 8\Sentinel\` |

**If you are building your own tool**, pick your own prefix, your own namespace, and your own config
folder, and use them everywhere — file name, class name, display `Name`, namespace. Then your work and
this project can be installed side by side forever. That is the entire ask, and it is a five-minute
decision at the start that is expensive to undo later.

You are explicitly welcome to **consume** everything here: read the published `SentinelCore.…State`
seams, subclass nothing, and ship whatever you like under your own name. See
[CONTRIBUTING.md](CONTRIBUTING.md) → *Forks and your own tools*.

⚠ **Do not add a `partial class SentinelCore` of your own.** It compiles — the type is
`public static partial` — but it produces a runtime that is partly this project's and partly yours, under
this project's type name, with nothing to tell a user which is which. If you need something `SentinelCore`
does not expose, **open an issue asking for the seam**; a member added upstream will not vanish on your
next update.

---

## Why this file exists

A contributor built his own confluence arbiter, named it `SentinelCouncil_v1_0_0`, and put it in
`NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors`. That was a reasonable thing to do: he was extending
Sentinel, so he used Sentinel's naming convention, exactly as the published naming law describes it.

**He had no way to know a `Council` already existed** — it was built, but unreleased, and its name lived
only in private design docs. The collision was a documentation failure on this project's side, not a
mistake on his.

So the names below include tools **that have not shipped yet**. Publishing the reservation is the only
thing that actually prevents the next collision.

---

## Claimed — shipped, in this repo

Runtime · `SentinelCore` (+ `.Foundation`, `.Safety`) · `SentinelSkin` · `SentinelWallpaper`

Bar types · `SentinelTBars` · `SentinelTbarsCount` · `SentinelFlux`

Sensors · `SentinelTrend` · `SentinelWAE` · `SentinelGodReversal` · `SentinelADXVMA` · `SentinelVIDYA` ·
`SentinelSuperTrend` · `SentinelParabolicSAR` · `SentinelStructure` · `SentinelExhaustion` ·
`SentinelHarmonic` · `SentinelZScore` · `SentinelRegime` · `SentinelStochasticTripleFilter` ·
`SentinelBrickCounter` · `SentinelBarsPerSessionAdvisor` · `ADXPro` · `CompressionBase` ·
`LiquidityWalls` · `VolEnvelope` · `WoodiesCCIPro` · `BuySellVolumePressureMountain`

Smoothers · `SentinelEMA` `SMA` `HMA` `WMA` `VWMA` `TMA` `TSMA` `TWMA` `DEMA` `DSMA` `DTMA` `DWMA` `TEMA`
`ZLEMA` `ZeroLagTEMA` `ZeroLagHATEMA` `HoltEMA` `LinReg` `MovingMedian` `GaussianFilter`
`ButterworthFilter` `SuperSmootherFilter` `EhlersFilter` (all `Sentinel`-prefixed)

Deck · `SentinelDeck`

---

## Reserved — built or planned, **not yet published**

**These names are taken. Do not use them.** They exist in the private development tree or on the roadmap,
and will appear in this repo as their rung ships.

| name | what it is |
|---|---|
| `SentinelCouncil` | the confluence arbiter — fuses every published sensor seam into one verdict |
| `SentinelBridge` | automated Council-consumer (strategy) |
| `SentinelCockpit` | suite command surface (AddOn) |
| `SentinelHelm` | interdiction layer over a running strategy |
| `SentinelDashboard` · `SentinelBinds` · `SentinelConductor` · `SentinelQuartermaster` | AddOn surfaces |
| `SentinelRisk` · `SentinelArc` · `SentinelLens` · `SentinelLog` · `SentinelAlert` · `SentinelState` · `SentinelNews` · `SentinelExcursions` | services |
| `SentinelClock` · `SentinelParticipation` · `SentinelLocation` · `SentinelMtf` · `SentinelIntermarket` | orthogonal context axes |
| `SentinelCVD` · `SentinelProfile` · `SentinelFlow` · `SentinelTapeRecorder` · `SentinelBarDump` · `SentinelLagCheck` | flow / instrumentation |
| `SentinelDrift` · `SentinelEffort` · `SentinelLattice` · `SentinelTide` | bar types |
| `SentinelExcursionRecorder` · `SentinelCandidateRecorder` · `SentinelAdaptivePerformanceGrid` | recorders / analytics |
| `SentinelEye` · `SentinelTrendArchitect` | legacy / historical |

⚠ **Pre-law names in the shipped set are also reserved in their compliant form.** Several shipped tools
predate the naming law and still carry a bare class name — `ADXPro`, `CompressionBase`, `LiquidityWalls`,
`VolEnvelope`, `WoodiesCCIPro`, `BuySellVolumePressureMountain`. Their compliant names
(`SentinelADXPro`, `SentinelCompressionBase`, …) are **reserved for those same tools** and will be adopted
at a version bump. Do not claim one.

### Why they are not simply renamed today

**A NinjaScript indicator's namespace + class name are its serialization identity.** Renaming one silently
drops it off every saved chart, workspace and template a user has — the tool does not error, it just is not
there any more, and the settings are gone.

So this project renames **only at a planned version bump**, where that break is announced and the old
version stays available as a frozen fallback. Reserving the name costs nothing and is immediate; taking it
is scheduled. The reservation is what makes the eventual rename safe.

---

## If you have already shipped something that collides

Nothing dramatic — this is easy while adoption is small and painful later:

1. Rename your type and namespace to your own prefix (a find/replace, then recompile).
2. Tell whoever installed it to delete the old file **before** dropping in the new one, or they will hold
   both copies and hit the `CS0101` this file exists to prevent.
3. If your tool is genuinely good, open an issue — it can be linked from these docs under your own name.

## Claiming a name

Open an issue titled `reserve: <Name>`. If it does not collide, it gets added here. Reserving a name for a
tool you are actually building is welcome; the registry works in both directions.
