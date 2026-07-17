---
name: port-sentinel-indicator
description: >-
  Port / convert a plain NinjaTrader 8 indicator into a fully Sentinel-plumbed suite SENSOR.
  Use whenever the user asks to "port <X> to Sentinel", "Sentinel-ify", "add Sentinel plumbing / card /
  State seam", "make <indicator> a Sentinel sensor", or contribute a sensor to the suite. Enforces the
  mandatory naming law, the glass card + label remover, and the publish-a-State-seam protocol so a
  half-plumbed sensor can never ship. Routes the port into the Sensors picker folder (Indicators.Sentinel.Sensors).
  FIRST checks the source's LICENSE / PROVENANCE — a port is a derivative work, so an unlicensed or protected
  source is clean-roomed or stopped, never shipped as "original". Needs SentinelCore + SentinelSkin (src/runtime/AddOns) present.
---

# Port an indicator → a Sentinel sensor

You are converting a **plain NinjaTrader indicator** into a **first-class Sentinel sensor**. A raw
NT indicator draws plots and nothing else. A *Sentinel* sensor additionally: carries the suite naming
law, renders a **SentinelSkin glass card** with the Sentinel palette, hides NT's name label by default, and
— the load-bearing part — **publishes a `SentinelCore.…State` seam** that the suite's fusion layer can read.
A port that only restyles (card + palette) but publishes nothing is **not done** — see *Definition of done*.

> **Scope of this public repo.** This skill targets a **contributed sensor**. The suite's confluence arbiter
> (the "Council") that *consumes* published seams is not part of this open-source cut — so your deliverable is a
> **correctly-published State seam**, and the Council-side wiring (adding your voter to the fusion) is a
> maintainer step done on merge. Everything else — naming, card, provenance, the seam itself — is fully yours to
> complete here. Requires **SentinelCore + SentinelSkin** (`src/runtime/AddOns/`); see
> `docs/SENTINEL_SHIP_MANIFEST.md`.

Read first: **`docs/SENTINEL_DESIGN_SYSTEM.md`** (§3 chart primitives, §4b `SentinelSkin.Painter`/`CardLayout`,
§6 SentinelCore seams, §9 standing protocols), **`docs/SENTINEL_NAMING_FEDERATION.md`**, **`CONTRIBUTING.md`**,
and **`SENSOR_COMPLIANCE_CHECKLIST.md`** (the human-readable form of this skill's *Definition of done*).

## ⚠ License & provenance — the FIRST gate (check BEFORE you port a single line)

**A port is a DERIVATIVE WORK.** It inherits the copyright and license of whatever you port FROM.
Translating a TradingView Pine script — or forking someone's NT indicator — into C# does NOT make it
yours: copyright protects the *code expression*, not just the name. So before porting, establish where
the source came from and whether you have the right to relicense/redistribute it. *(This gate exists
because an audit found three "clean-room" sensors were actually unlicensed third-party ports — see the
repo `NOTICE`. The suite ships under **MPL-2.0**.)*

**Step 0 — read the SOURCE's header and classify it:**

| Source is… | Then… |
|---|---|
| **your OWN prior work** | ✅ clean — self-derived, no third-party rights |
| **an original implementation of a PUBLIC method/formula** (methods & formulas are NOT copyrightable) | ✅ clean — but write from the *formula*, not by copying a specific implementation's code |
| **third-party code under an OPEN license** (MPL-2.0, MIT, Apache-2.0, BSD…) | ⚠ portable — the ported file **KEEPS that license** (file-level for MPL), add its license header, and credit the author in `NOTICE`. MPL-2.0 is fine *unless* its header says *"Incompatible With Secondary Licenses."* |
| **third-party code with NO license / "all rights reserved" / TradingView "protected"** | ⛔ **NOT portable as-is.** NT-forum and older TradingView scripts often carry NO license = no redistribution right. Options: (a) written permission from the author, (b) **clean-room** reimplement from the public method (fresh code, no copying), or (c) don't ship it. |

**Where to look:** the source's top comment block — `©`/`copyright`, an explicit license
(`Mozilla Public License 2.0`, `MIT`, …), `@author`, and lineage words (`port of`, `based on`,
`adapted from`, `modified by`). A TradingView script states its license in the header; an NT-community
indicator usually shows an author but **no license** → treat as all-rights-reserved.

**Record the finding — two places, always:**
1. In the ported file's header: a one-line provenance/license note (source · author · license, **or**
   "clean-room from the public formula — no third-party code").
2. In the repo `NOTICE`: attribution + license for any third-party-derived or MPL component.

**This gate is a HARD STOP.** Never call a port "original" — a port is derivative by definition. If the
source isn't cleanly licensed, resolve provenance (clean-room or permission) BEFORE porting — or say so
and stop. A PR that ships an unlicensed port as "original" will be rejected.

## Canonical skeletons — copy structure from these shipped sensors, do not invent

- **`src/sensors/Indicators/SentinelTrend_v1_0_0.cs`** — the reference sensor: header block,
  `SetDefaults`/`DataLoaded` (label remover), `OnBarUpdate` publish (`SentinelCore.SetTrendState`),
  `OnRender` glass card, Sentinel-grouped `[NinjaScriptProperty]` block, and a **zero-length generated region**
  on disk. Mirror it exactly.
- **`src/sensors/Indicators/CompressionBase_v1_3_0.cs`** — the reference for a **directional signal** exposed as
  a hidden `Signal` plot (transparent + `IsAutoScale=false` so ±1 doesn't render/scale).
- Using shipped sensors as the template means the skeleton never drifts from the current SentinelCore API. Any
  file under `src/sensors/Indicators/` is a valid reference.

## The naming law — enforce ALL FOUR layers (non-negotiable)

A novice must be unable to confuse a plumbed sensor with a raw indicator. Four surfaces carry the tell; set all four.

| Layer | Rule | Example |
|---|---|---|
| **Display `Name`** (in `SetDefaults`) | **`"Sentinel <Thing> v<M>.<m>.<p>"`** — prefix **+ version**; ` (DEV)` while in dev, dropped at freeze. NT's picker lists by display Name, so the version MUST live here — the `_vX_Y_Z` class suffix is invisible at selection time. | `Name = "Sentinel Trend v1.0.0";` |
| **Namespace** | **`…Indicators.Sentinel.Sensors`** (the Sensors folder — where every port lands). | `namespace …Indicators.Sentinel.Sensors` |
| **Class + file** | `Sentinel<Thing>_vX_Y_Z` | `SentinelTrend_v1_0_0` / `SentinelTrend_v1_0_0.cs` |
| **Runtime** | cyan glass card + label remover ON | `ShowIndicatorLabel = false` |

- The **`Name` property is display only** — NOT the serialization identity, so `"Sentinel Trend"` is safe to set
  freely. **Namespace + class name ARE the identity** — a one-time, version-bump-only choice; renaming later drops
  the tool off saved charts.
- Add **two** file-top usings: `using NinjaTrader.NinjaScript.AddOns.Sentinel;` (resolves `SentinelSkin` /
  `SentinelCore` / `SentinelCardCorner`) **and** `using …Indicators.Sentinel.Sensors;` (so NT's generated wrapper,
  which sits in `…Indicators` and references custom enum params **bare**, resolves them). **Custom
  `[NinjaScriptProperty]` enums must be declared in the SAME `…Sentinel.Sensors` namespace**, or you get CS0246/CS0101.

## Where it lands — a port is a SENSOR

The picker groups by sub-namespace into folders, and **3-level namespaces nest** (`Indicators.Sentinel.Sensors`
→ "Sentinel ▸ Sensors"). **Every port lands in `…Indicators.Sentinel.Sensors`** — that keeps the curated
"Sentinel" folder a clean brick-list while ports pile into the Sensors subfolder.

- **⚠ Serialization identity — one-time choice.** Namespace = identity, picked at v1; can't change later without a
  version bump + re-adding to every saved chart.
- **⚠ Set it in CODE, never hand-drag in the NinjaScript editor** — the picker nests off the *namespace*; an
  editor folder/namespace desync flattens it.

## Procedure

1. **Locate the source** indicator and read it fully. Identify: its plots, its core signal/regime/bias
   output, any custom enums used as `[NinjaScriptProperty]`, **and its LICENSE / PROVENANCE — run the
   *License & provenance gate* above FIRST.**
2. **Pick the name** per the four-layer law. **⚠ NAME FIDELITY: the `<Thing>` is the SOURCE indicator's FULL
   name, verbatim** — `StochasticTripleFilter` → `SentinelStochasticTripleFilter` / "Sentinel Stochastic Triple
   Filter", NOT a shortened "Sentinel Stoch Filter". A port is a derivative work; its name must trace back to its
   source. Do NOT abbreviate or re-brand. Confirm only spacing/casing if ambiguous.
3. **Copy → rename** into the new versioned file (`sed 's/OldName/SentinelThing_v1_0_0/g'` across file
   name, class, `Name`, and any version-suffixed enums).
4. **Rehome the namespace** to `…Indicators.Sentinel.Sensors`. Add the matching file-top `using`. Move any custom
   `[NinjaScriptProperty]` enum into that SAME namespace (bare-enum codegen rule).
5. **Add the plumbing** (mirror SentinelTrend):
   - Sentinel `using` + header block with a `CHANGELOG` + provenance note starting at this version.
   - Label remover: `if (!ShowIndicatorLabel) Name = string.Empty;` as the FIRST line of `DataLoaded`.
   - `SentinelSkin.Painter` card in `OnRender` (+ `CardLayout.Place` / `CardLayout.Release` in
     `Terminated`); cyan = live/watching, green/red = money+direction only.
   - **⚠ TWO CARD-RENDER RULES — a card that breaks them SILENTLY DOES NOT RENDER** (no error, no log):
     1. **NEVER read `Value[0]`/`Value[1]` (any Series) inside `OnRender`** — it's a cross-thread read that can
        throw, the card's `try/catch` swallows it, and the card vanishes. **Cache the readout in `OnBarUpdate`**
        into plain fields (`_cardVal`/`_cardSlope`/`_cardHasData`); read those in render.
     2. **`_sp.Begin()` and `_sp.End()` go OUTSIDE the `try`; only the draw goes inside** — so `End()` ALWAYS
        runs (`_sp.Begin(RenderTarget); try { RenderCard(); } catch { } try { _sp.End(); } catch { }`). A skipped
        `End()` kills the card and can leave the shared RenderTarget's transform armed for the next indicator.
   - The Sentinel property group: `PublishState` (default **true**), `ShowCard`, `CardCorner`,
     `ShowIndicatorLabel`, optional `LogChanges`.
6. **Publish a State seam (the standing protocol — Docs §9 item 6, do not skip):**
   - If SentinelCore already has a fitting `…State`, publish it in `OnBarUpdate` (default ON). If not, **add a new
     seam to SentinelCore** (`Set<Thing>State` / `Get<Thing>State` / `All<Thing>States`), **bump SentinelCore's
     internal version**, and record the seam version in its header. (Many seams already exist — grep
     `src/runtime/AddOns/SentinelCore_v1_0_0.cs` for `Set…State`.)
   - **The fusion/Council wiring is a maintainer step** (the Council isn't in this public cut). Your job is a seam
     that is correct, versioned, and published default-ON — a seam the maintainers can consume on merge.
   - If the indicator emits a **directional signal**, ALSO expose it as a hidden `Signal` plot per
     CompressionBase (so generic consumers can read it).
7. **Strip the generated region to zero on disk** before handing off (see gotchas).
8. **Bump/update the changelog**, add the file to your build, and note any new pattern in the docs.

## Gotchas that WILL bite

- **Generated-region duplication:** editing a `.cs` while NT is running re-appends the generated
  region → CS0111/CS0102. **Strip the `#region NinjaScript generated code` block to zero lines on disk**;
  NT regenerates exactly one on F5. To compile a fresh port: in the NinjaScript Editor **close the tab
  WITHOUT saving, then F5.**
- **Headless build false-CLEANs:** a stale/incremental `dotnet build` silently skips your file. **NT's F5 is the
  only authority** — it catches real errors headless misses (e.g. `ind.Plots.Count` binds to LINQ → **CS0019**;
  use the indexer in try/catch, never `.Count`).
- **Bare-enum codegen:** a custom enum used as a `[NinjaScriptProperty]` must live in the class's own
  `…Sentinel.Sensors` namespace with the **matching** `using` at file top, or the generated wrapper won't resolve
  it. Keep exactly ONE version of the tool in the tree (two versions' same enum = CS0101).
- **Serialize runtime toggles WITHOUT a constructor param:** persist config via a plain `public` get/set
  property that is NOT `[NinjaScriptProperty]` (`[Display]` to show in F6, or `[Browsable(false)]` to hide).
  Never persist a live "arm/watch" flag → automation must not silently re-arm.
- **One broken `.cs` blocks the WHOLE compile** (single DLL). If your new tool "isn't showing up," suspect
  a collision elsewhere first.

## Definition of done — refuse to hand off a port missing ANY of these

- [ ] **Source license/provenance classified + recorded** (file-header note + `NOTICE`) — no unlicensed third-party code shipped as "original"; open-licensed sources keep their license + attribution; unlicensed ones were clean-roomed or cleared by permission
- [ ] Name = `"Sentinel <Thing> v<M>.<m>.<p>"` (+ ` (DEV)` if in dev), class/file `Sentinel<Thing>_vX_Y_Z`
- [ ] **Landed in `…Indicators.Sentinel.Sensors`**; custom enums + file-top `using` match that namespace
- [ ] Glass card (SentinelSkin palette, cyan-only accent) + label remover default ON — **and it obeys the two card-render rules: no `Value[]` read in `OnRender` (cache in `OnBarUpdate`); `_sp.Begin/End` outside the `try`** (else the card silently doesn't render)
- [ ] Publishes a `SentinelCore.…State` seam, `PublishState` default **true** (fusion/Council wiring is the maintainer's step)
- [ ] Directional signals also exposed as a hidden `Signal` plot
- [ ] Generated region stripped to zero on disk; changelog bumped
- [ ] Compiles under **NT F5** (headless clean is necessary, not sufficient)

If a step can't be met (e.g. SentinelCore lacks a place for the seam and adding one is out of scope),
**say so explicitly** rather than shipping a restyle-only port that *looks* Sentinel but isn't.
