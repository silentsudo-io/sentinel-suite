# Sentinel System Builder — Spec

> A GUI surface that lets the operator **compose a per-scope sensor system** — pick the sensors, set
> their weights and kind, set the conviction floor — and **materialize** it: write the scope's
> `Roster.conf` (what the brain *expects*) **and** lay those sensors onto a chart (what the chart
> *provides*), so the Council fuses exactly the declared system and `RosterComplete` verifies the two
> agree.

**Status:** Phases 0 + 1 **BUILT** (bridge-compile clean; needs one authoritative F5) · 2026-07-12
  · SentinelCore **v1.31.0** (`VoterCatalog` + `RosterIO`) · Council **v1.6.3** (consumes `RosterIO`) · SentinelCockpit **v0.3.0** (⑤ BUILD mode)
**Host:** extends `SentinelCockpit` (the per-scope decision surface) — new **BUILD mode**
**Depends on:** `SentinelCore` (new `VoterCatalog` + `RosterIO`), `Council_v1_x` (unchanged consumer)
**Related:** [[council-custom-voters]] · [[sentinel-cockpit]] · [[seam-scope-migration]] · [[declared-roster]] · [[conditions-vs-latches]]

---

## 0. Why this is the natural next tool (not a novelty)

The Council is **already** a config-driven linear model over a **declared roster** — it reads
`Roster.conf`, folds each declared voter's `w=` and `state/trigger` kind into a weighted vote, and
reports `RosterComplete` when the declared set matches what actually published. Today that roster is
edited by hand in a text file. **This tool is the visual front-end for a decision already recorded**
([[council-custom-voters]]: *the roster must be user-customizable — build a generic vote registry, not
a locked dev set*).

Three facts make it fall out of the existing architecture rather than fight it:

1. **A "system" is defined per SCOPE** (instrument + bar type). That is exactly the coordinate a model
   is defined over ([[seam-scope-migration]]), and EXP-0002 proved the **edge lives per bar type** — so
   the builder is scoped by construction, not by afterthought.
2. **The persistence format already exists and is already consumed.** `Roster.conf` is parsed by the
   Council at [Council_v1_0_0.cs:1046](../Indicators/Council_v1_0_0.cs#L1046). The builder writes the
   same file; nothing new has to learn to read it.
3. **The correctness check already exists.** `RosterInfo` (Declared/Present/Missing/Unexpected/Complete)
   is the Council's own verdict on whether the loaded chart matches the declared system. The builder
   makes that verdict its *post-apply confirmation* — the same mechanism that catches a crashed sensor
   ([[eye-never-loads-bug]]) validates the build.

The result is a genuinely closed loop: **one author** for *what the brain expects* and *what the chart
provides*, and the Council's existing `RosterComplete` tells you they match.

---

## 1. The artifact: a **System**

A System is `(scope, roster, presence)`:

| Part | Definition | Persisted as |
|---|---|---|
| **Scope** | instrument + bar-type tag, e.g. `GC.69697v6x24` (`SentinelCore.ScopeOf` / `BarTag`) | implied by file path |
| **Roster** | ordered `{tag, included, weight, kind}` + `ConvictionFloor` / `Deadband` / `BaseContracts` | `Roster.conf` (Council-consumed) |
| **Presence** | the indicator instances (+ params) that must load so those tags publish their seams | generated chart template |
| **Manifest** | the builder's own superset record (catalog picks + params + template name + floor/etc.) so a system round-trips | `System.conf` |

Systems live under the scope directory the Council already searches:

```
Sentinel\Models\<INST>\<bartag>\Roster.conf     # consumed by the Council (exists today)
Sentinel\Models\<INST>\<bartag>\System.conf     # builder manifest (new; superset of the roster)
templates\Chart\Sentinel_<INST>_<bartag>.xml    # generated chart template (new)
```

---

## 2. The two halves (very different risk)

The consult established the split; the spec commits to it.

### Half 1 — the system DEFINITION *(easy; plumbing exists)*
A visual editor over `Roster.conf`. Read the cascade, edit an in-memory model, write the file back in
the exact `ParseRoster` format. **Zero chart risk** — it only edits a config file the Council already
parses. This is the Cockpit's read surface made *writable*. **Ships value on its own.**

### Half 2 — the chart MATERIALIZATION *(the crux; staged, de-risked)*
Get the selected sensors actually **loaded on a chart** so they publish. Recommended mechanism = a
generated **chart template** (NT owns instantiation + data-series wiring; survives reload). A runtime
`ChartControl.Indicators.Add(...)` path exists but is brittle — prototype before committing.

---

## 3. The missing artifact: the **Voter Catalog**

`Roster.conf` speaks in **voter tags** (`EYE`, `TRND`, `FLOW`, `FLUX`…), but a chart loads **indicator classes**
(`Eye_v1_1_0`, `SentinelTrend_v1_0_0`, `SentinelFlow…`, and the `SentinelFlux` bar type for `FLUX`). The builder
needs a **catalog** mapping one to the other. This is the one genuinely new data structure the tool requires.

> **Catalog note:** the `VoterCatalog` must now include the **FLUX** voter alongside FLOW — `FLUX` is published by
> the `SentinelFlux` order-flow-imbalance **bar type** (not a chart indicator), so its `CatalogEntry` "presence"
> is satisfied when the chart runs on `SentinelFlux` bars and `FluxState` is fresh, rather than by loading an indicator.

**Location:** `SentinelCore.VoterCatalog` — a static table (single source of truth), optionally
overridable by `Sentinel\Models\Catalog.conf` for user-added sensors.

```csharp
public sealed class CatalogEntry
{
    public string   Tag;          // "EYE"  — the Roster.conf token
    public string   Display;      // "Eye"
    public string   TypeName;     // "Eye_v1_1_0" — the NT indicator class to instantiate/template
    public Role     Role;         // Voter | Modulator | Veto | ContextAxis
    public VoterKind DefaultKind; // State | Trigger
    public double   DefaultWeight;// the F6 default weight
    public string   Seam;         // "EyeVerdict" — the …State seam it must publish to count as Present
    public string   Notes;        // "instrument-keyed by design", "delayed feed", etc.
}
```

Seed it from the Council's own `KnownVoters` + weight/kind maps
([Council_v1_0_0.cs:266](../Indicators/Council_v1_0_0.cs#L266), `BaseWeight`, `KindFor`) plus the
context axes (Clock / Participation / MTF / Location / Intermarket) that are modulators/vetoes rather
than voters. Every row carries its **role**, so the UI can group *voters* (weighted) apart from
*modulators / vetoes / context* (present-or-absent).

> **Future — self-registration.** Fold catalog registration into the standing sensor protocol
> (design system §9): a "born-compliant" sensor already publishes a `…State` seam and wires into the
> Council; add *"registers a `CatalogEntry` in `SetDefaults`"* so the catalog is never hand-maintained
> as new sensors land. Until then the static table + `Catalog.conf` override covers it.

---

## 4. Single source of truth: extract `RosterIO`

Today `ParseRoster` lives **inside the Council**. If the builder writes `Roster.conf` with its own
serializer, the read and write formats can drift. **Extract the parser (and a matching writer) into
`SentinelCore.RosterIO`**; the Council consumes it (no behavior change), the builder consumes it. One
format, two clients.

```csharp
public static class RosterIO
{
    public static RosterDoc  Read(string scopeInst, string barTag);   // cascade-resolves, returns entries + source path
    public static void       Write(string scopeInst, string barTag, RosterDoc doc); // atomic (temp + File.Replace)
    // RosterDoc = ordered List<RosterLine{ Tag, Weight?, Kind?, Comment }> + ConvictionFloor?/Deadband?/BaseContracts?
}
```

Atomic write (temp file + `File.Replace`) so a half-written roster can never be read by a live Council.

---

## 5. UI — a Cockpit **BUILD mode**

Host it in the **Cockpit**, not the Dashboard: the Cockpit is already the per-scope *"is my brain alive
/ why isn't it trading"* surface, floatable and pinnable, scope-picker driven. The System Builder is its
**write-side twin**. Add a header toggle **MONITOR ⇄ BUILD** (editing needs more room and a different
interaction than the monitoring column).

```
┌─ SENTINEL COCKPIT ····························· [MONITOR|BUILD] 📌 ─┐
│ Scope:  [GC ▾]   bartype [TBars 6/24 ▾]   →  GC.69697v6x24          │
│ Source: Models\GC\69697v6x24\Roster.conf   ● live Council on chart  │
├────────────────────────────────────────────────────────────────────┤
│  VOTERS                                    w        kind      seam   │
│  ☑ Eye            ▓▓▓▓▓░░░░  1.4   ● state ○ trigger        ● fresh │
│  ☑ SentinelTrend  ▓▓▓░░░░░░  1.0   ● state ○ trigger        ● fresh │
│  ☑ WoodiesCCI     ▓▓░░░░░░░  0.8   ● state ○ trigger        ● stale │
│  ☐ ADXPro         ░░░░░░░░░  0.6   …                        ○ absent│
│  ☑ Flow           ▓▓░░░░░░░  0.7   ● state ○ trigger        ● fresh │
│  ☑ Flux           ▓▓░░░░░░░  0.7   ● state ○ trigger        ● fresh │
│  … (22 voter rows, catalog-driven)                                  │
│                                                                     │
│  MODULATORS / VETOES / CONTEXT              include                 │
│  ☑ Clock   ☑ Participation   ☑ MTF   ☑ Location   ☑ Intermarket     │
├────────────────────────────────────────────────────────────────────┤
│  ConvictionFloor  0.20 ▓▓░░░░   Deadband 0.15   BaseContracts 2     │
│  declaredW 7.80   stateW 4.10 (quiet-bar denom)                     │
│  RosterComplete? ● 20/22 present · missing: ADX, BRK                 │
├────────────────────────────────────────────────────────────────────┤
│  [ Load from live ]  [ Save Roster ]  [ Generate Template ]  [ Apply]│
└────────────────────────────────────────────────────────────────────┘
```

Row anatomy (voter): **include checkbox · display name · weight slider + numeric · state/trigger toggle
· live-seam dot** (fresh / stale / absent, reusing the Cockpit's three-seam-state coloring). `w=0` is
expressible directly (the **exploration primitive** — recorded, contributes nothing).

**Live preview (no save required):** as the operator edits, recompute and show `declaredW`, `stateW`
(the quiet-trigger denominator — [[state-vs-trigger-voters]]), and a *predicted* `RosterComplete` by
checking which declared tags currently have a fresh seam for the scope (via `AllCouncilStates` / the
per-seam getters). This makes the cost of each toggle visible **before** it's committed.

---

## 6. Half 1 — Roster editor *(Phase 1, low risk)*

- **Read:** `RosterIO.Read(inst, barTag)` mirrors the Council's cascade; show which file won (scope ▸
  instrument ▸ global ▸ *default-derived*). If default-derived, seed the editor from configured weights.
- **Edit:** in-memory `RosterDoc`; sliders/toggles mutate it; preview recomputes.
- **Write:** `RosterIO.Write` → `Models\<INST>\<bartag>\Roster.conf`, atomic, with a generated header
  comment (`# generated by Sentinel System Builder <UTC> — hand-edits below the line are preserved`).
- **Reload semantics (decision point):** the Council caches its roster at load
  ([Council_v1_0_0.cs:405](../Indicators/Council_v1_0_0.cs#L405), inside `State.Configure`/`DataLoaded`).
  A live edit therefore **applies on the Council's next reload** (chart reload / indicator re-add).
  - **Phase 1:** accept next-reload; the UI says so plainly ("saved — reloads on next chart load").
  - **Phase 4:** hot-reload via a roster **version stamp** the Council polls on `OnBarClose` (bump a
    `SentinelCore.RosterVersion(scope)` on write; Council re-runs `LoadRoster` when it changes). No
    lookahead risk — it's config, not market data.

---

## 7. Half 2 — chart materialization

> **RESOLVED by reflection (2026-07-12): NT exposes a clean apply path.** `NinjaTrader.Gui.Chart.ChartControl`
> carries `TemplateLoad(string)` *(private)*, **`TemplateLoadIndicators(XElement)` *(internal)***,
> `TemplateSave(string)` *(private)*, `RemoveIndicator(IndicatorRenderBase)` *(private)*, and a
> **public** `Indicators` getter (`ChartObjectCollection<IndicatorRenderBase>`). The methods are
> private/internal — reachable from an AddOn **via reflection** (the accepted NT hatch; the suite already
> reflects into NT). Crucially, **`TemplateLoadIndicators` IS NT's own indicator-instantiation path**, so
> it does the `BarsArray`/data-series wiring for us. This supersedes the old "seed + patch + manual load"
> plan and **retires the runtime-`.Add` spike entirely.**

### 7A. Reflected `TemplateLoadIndicators` *(recommended)*

1. **Learn the schema once** — reflect-invoke `TemplateSave(tempPath)` on a chart that has the sensors
   loaded, read the resulting XML, and lift the indicators sub-tree. That is the exact `XElement` shape
   `TemplateLoadIndicators` consumes — no guessing at NT's schema.
2. **Build the element** — from the System's included catalog entries (`TypeName` + params), assemble an
   `<indicators>` `XElement` matching that shape.
3. **Apply** — get the target chart's `ChartControl` (enumerate open chart windows, or target the active
   chart), then on the **UI thread** (`chartControl.Dispatcher.InvokeAsync`) reflect-invoke
   `TemplateLoadIndicators(xel)`. This swaps the indicator set **without disturbing the rest of the chart**
   (bars, panels, colors) — surgical, unlike a whole-template load.
4. **Prune** — for sensors the System *excludes* that are currently present, reflect-invoke
   `RemoveIndicator` per the public `Indicators` enumeration (the suite's existing remove pattern).

Whole-chart `TemplateLoad(fileName)` remains the fallback if a full rebuild is ever wanted (it is literally
the menu action, invoked in code).

### 7B. Runtime add *(RETIRED)*

The earlier plan to `ChartControl.Indicators.Add(instance)` and hand-wire `BarsArray`/inputs is **no longer
needed** — `TemplateLoadIndicators` is NT's own path and does that wiring internally. Kept here only as a
record of the discarded approach.

**Recommendation:** implement 7A. The remaining unknown is not *whether* it works but the `<indicators>`
XElement schema — which the `TemplateSave` round-trip hands us directly. A ~1-hour spike (save → read →
re-apply a modified set → confirm the sensor loads + publishes its seam) confirms the whole path.

---

## 8. The closed loop (why this is self-verifying)

```
   Builder ─┬─► Roster.conf  (what the brain EXPECTS)  ─┐
            └─► chart template (what the chart PROVIDES)─┤
                                                         ▼
                                              Council loads scope
                                                         │
                                              ResolveRoster() ──► RosterInfo
                                                         │
   Builder reads back ◄──────────────────────────────────┘
     Complete  → green
     Missing   → declared but not publishing (indicator failed to load / crashed — eye-never-loads class)
     Unexpected→ loaded but undeclared (present + folded-out of the fusion)
```

After **Apply**, the builder polls the scope's `RosterInfo` and reports it. A green `Complete` is proof
the materialized chart matches the declared system. A `Missing` entry is exactly the crashed-sensor
signal the declared roster exists to surface — surfaced now at *build time*, in the tool that built it.

---

## 9. Scope & multi-chart guardrails

- The builder edits **one scope at a time**; the scope picker is the mode's anchor.
- **Scope contention:** two charts sharing instrument + bar type collide (the Council already logs
  `SCOPE CONTENTION` — [[scope-contention-detector]]). If the operator applies to a scope that already
  has a live Council on another chart, **warn before writing**.
- **Instrument-keyed-by-design seams** (Clock, Intermarket) are shared across bar types by design
  ([[seam-scope-migration]]). The builder shows them in the MODULATOR/CONTEXT group and labels them
  *global* — toggling them is not per-scope.
- **Participation is scope-keyed** ⇒ if included, it must be loaded on *this* chart — the builder's
  presence check enforces exactly that.

---

## 10. SentinelCore additions (summary)

| Addition | Purpose | Risk |
|---|---|---|
| `VoterCatalog` + `CatalogEntry` | tag → indicator class + role + seam + defaults | additive, none |
| `RosterIO.Read/Write` (extract `ParseRoster`) | one parser/writer for Council + Builder | Council swap to shared parser — verify parity |
| `RosterVersion(scope)` (Phase 4) | hot-reload signal | additive |
| optional `Catalog.conf` override | user-added sensors without a recompile | additive |

Version bump on `SentinelCore`; extend the Cockpit (bump `SentinelCockpit_v0_2_0`) rather than a new
AddOn — keep one command surface. Follow the federated naming law (AddOn base `…AddOns.Sentinel`,
display `Sentinel Cockpit`).

---

## 11. Build order

| Phase | Deliverable | Ships value | Chart risk |
|---|---|---|---|
| **0** ✅ | `SentinelCore.VoterCatalog` + extract `RosterIO` (Council swaps to it, bridge-compile clean, parity-preserved) | infra | none |
| **1** ✅ | Cockpit **BUILD mode** — Roster editor: read/edit/save + live `declaredW`/`stateW`/predicted `RosterComplete` | **yes — full config UI, zero chart risk** | none |
| **2** | `System.conf` manifest + **template generation** (seed+patch, written to `templates\Chart\`, manual apply) | yes | low (writes a file) |
| **3** | **Apply-to-chart** — reflect-invoke `TemplateLoadIndicators` on the chart `ChartControl` (UI thread); prune via `RemoveIndicator` | yes | low–medium (schema via `TemplateSave` round-trip) |
| **4** | **Hot roster reload** (version stamp; no chart reload needed) | polish | none |

Phase 1 alone replaces hand-editing `Roster.conf` and is the low-risk, high-payoff cut. Everything after
it is materialization.

---

## 12. Open questions / decisions to lock

1. **Reload:** next-reload (Phase 1) vs. version-stamp hot-reload (Phase 4) — confirm Phase 1 accepts
   next-reload.
2. **Template apply API:** ✅ RESOLVED — `ChartControl.TemplateLoadIndicators(XElement)` exists (internal;
   reflection). Phase 3 is reflect-and-invoke, not a manual-load fallback. Remaining sub-task = confirm
   the `<indicators>` XElement schema via a `TemplateSave` round-trip.
3. **7B runtime-add:** ✅ RETIRED — `TemplateLoadIndicators` supersedes manual `.Add`. No spike needed for it.
4. **Catalog maintenance:** static table now, self-registration later — confirm the standing-protocol
   amendment (sensor registers its `CatalogEntry`).
5. **Manifest scope:** does `System.conf` also capture per-indicator param overrides (beyond catalog
   defaults), or is Phase 2 defaults-only?

---

## 13. One-paragraph pitch

Everything the Council needs to be told is already a file it reads and a check it runs. The System
Builder is the surface that writes that file with sliders instead of a text editor, lays the matching
sensors onto the chart, and then reads the Council's own `RosterComplete` back as proof the two agree —
turning the declared-roster invariant from a thing you maintain into a thing you *compose*.

---

## 14. Per-Lane Rosters — authoring A/B test systems *(2026-07-14, ✅ BUILT — both phases)*

> **BUILT & compiling** (via `nt8bridge`; loads on the next F5). As shipped: **SentinelCore v1.33.0** (`RosterIO`
> baseline-inherit cascade + `LaneIO` for `Lane.conf`) · **Council v1.8.0** (`ApplyLaneProfile` — reads `Lane.conf` on
> load and overrides the F6 fusion knobs) · **Cockpit v0.4.0** (⑤ BUILD gains a **lane field** + a **Lane.conf profile
> editor**). Phase 1 (per-lane roster + fork) AND Phase 2 (per-lane `Lane.conf` profile) both landed. The design below
> is the as-built spec; §14.7's "Phase 2 optional" decision was taken — Phase 2 is IN.

**Goal.** Author, edit, and FORK a **per-lane** `Roster.conf` **+ `Lane.conf` profile** from the Cockpit, so you can run
two charts on *identical bars* (same instrument + bar type + size) with **different systems** and compare them — without
hand-editing files. This is the write-side companion to the per-chart **LANE** feature (`SentinelCore v1.32.0` ·
`Council v1.7.0`; see the `per-chart-lane` memory). A lane makes the scope `GC.212202v6x24@A`; this section makes the
System Builder able to target that `@A`.

### 14.1 What's already true (so this is small)
- **`RosterIO` already handles laned tags.** `Read`/`Write`/`PathFor`/`Candidates` take `(inst, barTag)` and `barTag`
  is an **opaque string** — `"212202v6x24@A"` already resolves to `Models\GC\212202v6x24@A\Roster.conf`. No I/O change
  needed to *store* a lane roster; the Council already *reads* it (it passes the laned tag to `RosterIO.Read`).
- The Cockpit ⑤ BUILD editor already Read/Write/previews a roster for a scope. It just has **no lane dimension** in its
  target picker, so today it can only write the *bare* scope.

So the gap is three things: (a) let the Cockpit TARGET a lane, (b) one cascade nicety, (c) a FORK action.

### 14.2 RosterIO cascade — a lane inherits its bar type's baseline *(Core, ~6 lines)*
Today `Candidates(inst, "212202v6x24@A")` = `…\212202v6x24@A\` ▸ `…\GC\` ▸ `…\` — it **skips the bare-bartype
roster**. Make a laned tag fall through the bare-bartype rung so a fresh lane isn't empty:

```
Models\GC\212202v6x24@A\Roster.conf   (the lane — what you fork/edit)
  ▸ Models\GC\212202v6x24\Roster.conf (the BAR-TYPE baseline — inherited until you fork)   ← NEW rung
  ▸ Models\GC\Roster.conf             (instrument default)
  ▸ Models\Roster.conf                (global)
```

Implementation: in `Candidates`, if `barTag` contains `'@'`, insert the pre-`@` (bare-bartype) path right after the
laned path. `PathFor` is **unchanged** (a Write still targets the most-specific = the laned path). Net: a new lane
**reads as** the bar-type baseline until you Save a lane file, then diverges. Clean default, no empty lanes.

### 14.3 Cockpit ⑤ — the lane dimension *(the real work)*
The build target becomes `<instrument> . <bartag> [@ <lane>]`. Three UI pieces (Cockpit → **v0.4.0**):

1. **Scope picker (primary).** Extend the current instrument combo to a **scope** combo populated from
   `AllCouncilStates()` — it already surfaces `GC.212202v6x24@A`, `@B`, … as live scopes. Pick the live laned chart you
   want to configure; the Cockpit parses `inst / bartag / lane` from the scope string. (Instrument-only stays valid →
   bare-bartype roster.)
2. **Editable "Lane" field.** A small alnum text field beside the scope so you can author a lane that **isn't live yet**
   (prepare lane `C` before opening its chart), or blank it to edit the bar-type baseline. Sanitized to match Core
   (`SanitizeLane`).
3. **Header truth line.** Show the resolved target + which cascade file actually wins, e.g.
   `editing GC.212202v6x24@A — NEW file (inherits 212202v6x24 baseline)` vs `editing … — existing lane file`. So you
   always know whether you're forking or editing.

### 14.4 Fork-to-lane — the A/B killer action
A **`Fork → lane…`** button in BUILD: take the roster **currently in the editor** and Save it under a NEW lane, then
switch the editor to that lane. Workflow becomes two clicks per variant:

```
compose a baseline  →  Fork → A  →  tweak weights/kind/voters  →  Save
                    →  Fork → B  (from the baseline)  →  tweak differently  →  Save
```

Optional companion: **`Clone from…`** — seed the current lane's editor from any existing scope's roster (copy B's
tuning onto C to iterate). Both are pure `RosterIO.Read`→edit→`RosterIO.Write` — no new plumbing.

### 14.5 Save & apply
`RosterIO.Write(inst, ladedTag, doc)` → `Models\GC\212202v6x24@A\Roster.conf` (atomic, as today). Applies on the
Council's **next reload** (F6 disable/enable or reload the chart) — hot-reload remains the later phase from §7. The
live preview (declaredW / stateW / `RosterComplete` vs the live Council mask) works unchanged, now against the laned
Council.

### 14.6 How you USE it — the end-to-end A/B test
1. Two charts, identical bars (GC · `212202v6x24`). Council **Scope Lane** = `A` on one, `B` on the other. Both
   inherit the bar-type baseline roster until forked.
2. Cockpit → **BUILD** → scope `GC.212202v6x24@A` → adjust variant A → **Save**. Then `@B` (or **Fork → B**) → adjust
   → **Save**.
3. Reload both Councils. `sentinel.log` shows the two lanes with **different rosters/denominators** — the proof they
   diverged.
4. (If auto-trading) set each **Bridge / GTrader21** `Scope Lane` to `A` / `B` so each trades its own lane.
5. Bake clean schema-1.3 rows — the Recorder files each lane separately (`bartype=212202v6x24@A` / `@B`).
6. `Lab\train.py` / `council_paths.py` group by scope → **grade A vs B on identical bars**. Promote the winner (copy
   its roster to the bar-type baseline, or keep the lane).

### 14.7 Decision to lock — is a "lane" just a Roster.conf, or a full system profile?
`Roster.conf` carries **voters + weights + kind**. But a lane's *behavior* also depends on Council F6 settings the
roster file does **not** hold: **`ConvictionFloor`**, the consult toggles, and the modulator damps. Those are already
**per-chart** (each chart's Council has its own F6), so they're *already* per-lane — but they live in the workspace,
not in the lane file.

- **Phase 1 (recommended, this section):** the System Builder authors the **`Roster.conf` only**. Simple, matches the
  tool's remit. ⚠ Footgun: if lanes should differ by **floor**, you must also set each chart's F6 floor by hand — the
  Cockpit won't capture it.
- **Phase 2 (optional):** promote a lane to a **one-file reproducible SYSTEM** — extend the lane folder with a sibling
  `Lane.conf` (or a header block in `Roster.conf`) carrying `floor=`, `consult*`, damps. The Council reads it on load
  (overrides F6 for that lane); the Cockpit edits it beside the roster. This makes an experiment a single shareable,
  versionable artifact — the honest unit for "system A vs system B". **Recommend deferring to Phase 2** but designing
  the file layout now so Phase 1 files are forward-compatible.

### 14.8 Scope / non-goals
- No `Roster.conf` **grammar** change (Phase 1). Only: RosterIO cascade (§14.2), Cockpit target+fork (§14.3–4), version
  bump to **Cockpit v0.4.0**.
- **Not** hot-reload (still §7's later phase). **Not** the per-lane full profile (Phase 2, §14.7). **Not** chart
  materialization (§7). **Not** laning individual sensors (a lane shares sensors by design — see the `per-chart-lane`
  memory).
