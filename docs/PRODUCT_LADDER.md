# Sentinel — Product Ladder & Open-Source Packaging

**Living document (started 2026-07-10).** This is the **product map** — *what a user adopts, how deep
they go, and how the pieces are packaged.* It is deliberately distinct from [ROADMAP.md](ROADMAP.md),
the **pipeline map** (how data flows: sense → decide → act → record → learn). Same system, two lenses.
When they disagree, the ROADMAP owns *engineering sequence*; this doc owns *packaging & product shape*.

> **North star:** open-source **all of Sentinel**, designed so the plumbing works **no matter which
> rung a user is standing on.** Open source and modularity are the *same cut* — if every rung must
> stand alone as its own bundle over a shared runtime, you cannot accidentally build a monolith.

> **Adoption tiers, not price tiers.** Everything is free, so the ladder is a *depth-of-adoption* path
> — how far a user chooses to climb — **not** a pricing menu. "Focus" therefore means *what we release
> first and point newcomers at*, not *what we sell first* (see §7).

---

## 1. Scope — what "Sentinel" means here

This document concerns **only the Sentinel-owned files** in `bin\Custom`. The unrelated commercial
vendor tools in the same tree (FlowMatriX, PredatorX, NinZaRenko, GoldenAxe, EliteLicenser, etc.) are
**out of scope** and are never part of any Sentinel bundle or license. Contributed work *inside*
Sentinel was released with full permission for open-source use (to be recorded in `NOTICE`/`AUTHORS` —
§6).

---

## 2. The two axes of value

Sentinel spans **two orthogonal axes** that compose but are independently valuable:

- **Axis A — Beauty.** The Skin + `SentinelSkin` drawing framework. Makes *any* NinjaTrader chart
  beautiful. Needs **zero** intelligence.
- **Axis B — Intelligence.** The sense → decide → act → learn pipeline (sensors → Council → Bridge →
  Lab). Works with **plain** NT rendering. Needs **zero** skin.

They compose, but appeal to different people at different depths. This mirrors the publish/consult
decoupling already built into the code: aesthetics and intelligence are separable *by design*.

---

## 3. The product ladder — the spine

Each rung **stands alone** (stop here and it's genuinely useful on its own) **and unlocks the next.**
These are **adoption tiers** — how deep a user climbs — and a user climbs only as far as they want.

| Rung | Product | Stands alone because… | Audience |
|---|---|---|---|
| **0** | **Sentinel Skins** — 6 themes + `SentinelSkin` | makes *any* NT chart beautiful; the widest, easiest entry | everyone |
| **1** | **The Sensors** — SentinelTrend, ADXPro, WoodiesCCIPro, VolEnvelope, CompressionBase, LiquidityWalls, God Reversal, WAE (the **8 hero signals**) + Clock, Participation, Location, MTF, Intermarket (the **5 axes**) + the **BarTypes** (SentinelTBars/TbarsCount + **SentinelFlux**, the order-flow-imbalance bar-type axis) | each hero is a gorgeous, functional standalone indicator | indicator shoppers |
| **2** | **The Recorder + Log** — the capture engines (excursion corpus + MAE/MFE journaling) | records every trade's outcome to JSONL, over *any* strategy | data-minded traders |
| **3** | **The Observatory** — the human interface to the excursion corpus (**consolidates** Lens + the Dashboard Excursion tab + SentinelExcursions) | explore / curate / understand your recorded outcomes; the **human counterpart to the ML Lab** | the "understand your trading" crowd |
| **4** | **The Council** | one honest verdict fused from all your sensors — a confluence dashboard | confluence traders |
| **5** | **The Deck** (+ Cockpit + Dashboard) | advanced manual chart trader / command surface | discretionary traders |
| **6** | **The Prop-Survival Kit** — Risk + Governor + Kill-switch + Gate + auto-flatten + reconnect reconciliation | *keep your funded account alive* | **prop-firm traders (large, urgent market)** |
| **7** | **The Bridge** | autopilot that consumes the Council | automation crowd |
| **8** | **The Copier** | mirror one account to many, prop-rule-aware | multi-account / prop |
| **9** | **Helm** — discretionary human interdiction over *any* running setup (move stop / close early / skip / pause; publishes a `HelmIntent`, never touches an order). **✅ BUILT 2026-07-15** (Core v1.34.0 seam · Bridge v0.3.0 consumer · Cockpit v0.5.0 ⑤ Helm rail; plumbing tier live-validated) | oversight that works over the Deck **and** the Bridge — the human hand on the tiller | hands-on overseers |
| **10** | **The ML Lab** — pairs with the Observatory (the *machine* counterpart over the same corpus); now a BUILT data platform (SQLite corpus `Lab\db\sentinel.db` + live JSONL→SQLite ingester + Streamlit :8501 + Grafana :3000) | the system fits its own weights from your outcomes | top tier, the believers |

**Rung 6 is both a product and a runtime layer** (§4, L2). The Safety layer **is** the survival kit,
surfaced through the Cockpit/Dashboard. Some rungs are infrastructure turned into a product — build once,
sell as a rung. **Rung 9 (Helm) is the discretionary sibling of Rung 6:** the survival kit is *automatic
hard limits*, Helm is the *human hand on the tiller* — distinct capabilities, distinct products.

**The ladder is a light tree, not a strict line.** Rung 3 opens an *analysis branch* (Recorder →
**Observatory** ↔ ML Lab) that runs parallel to the *execution branch* (sensors → Council →
Deck/Bridge), both rising over the shared runtime (§4). The **Observatory** (Rung 3) and the **ML Lab**
(Rung 10) are the **human** and **machine** consumers of the *same* excursion corpus — the analysis-tier
mirror of the Deck↔Bridge (manual↔auto) duality.

### Cross-cutting foundations (they span the rungs — NOT rungs themselves)

You don't *climb* to these; they are the ground the ladder stands on and the tissue between rungs:

- **The Runtime** (L0 Skin + F Foundation + L1 Bus + L2 Safety — §4). The floor every rung stands on.
- **The Platform Contract** — the naming law + the State-seam protocol + the `port-sentinel-indicator`
  skill. This *is* the "developer / SDK" story: the rules that let anyone extend *any* rung with a
  compliant tool. It makes Sentinel a **platform**, not just a product — but it is not a rung.
- **The Field Manual** (`SENTINEL_PROCESS_ATLAS.html`) — the education / onboarding layer that makes
  every rung learnable. A retention asset, bundled, not a rung.

### Still-open candidate products (genuine maybes)

- **Council-as-a-signal** — the verdict is already a published seam; it could become a shared/subscription
  feed. *(Kept as a parked candidate, 2026-07-10 — near-free optionality, no commitment yet.)*

---

## 4. The runtime floor — what makes "any rung" real

Every rung bundles a shared substrate. That substrate is **not one floor — it is layers, and rungs only
ever depend downward.**

| Layer | Contains | Needed by | Dependency |
|---|---|---|---|
| **L0 · Skin** | palette, `Painter`, `CardLayout`, themes (`SentinelSkin.cs`) | everyone who draws | none |
| **F · Foundation** | `SettingsDir`, `Log`, `SeamStore<T>`, `ScopeOf`/`BarTag`, `InstrumentRoot`, `Conditions`, `Alerts`, **`Ledger`**, **`State`** (audit/persistence primitives), + the context vetoes (kill / instrument-kill / news / rollover) | everyone | none (above NT) |
| **L1 · Bus/Seams** | the publish/consult registry — all 13 `…State` types + Set/Get, `EyeVerdict`, `BrickLog`, fleet/assist state | sensors, Council | → F only |
| **L2 · Safety** | the account-risk *decision* logic — feed-health, `CanEnter`, governor, drawdown, profiles, session, `SizedQuantity`/`TickValue`/`SizeForRisk`, order guards, `GateEntry` | anything that trades (Deck, Bridge, Copier, Risk) | → F only |

**The one rule that keeps every rung standalone:** *a file may only reference files in its own layer or
below.* L1 never references L2; L2 never references L1. (Verified today — §5.)

### The NT one-assembly caveat
NinjaTrader compiles **everything in `bin\Custom` into a single DLL**, so we cannot ship literal separate
assemblies. But **distribution ≠ installation**: a "Sentinel Skins" download is just the L0 files;
"Sentinel Sensors" is L0 + F + L1 + the indicators; each bundle is self-contained and installs into the
one assembly fine. **The layering is enforced by dependency discipline, not by assembly boundaries.**

---

## 5. Core-split finding (2026-07-10 structural read)

`SentinelCore_v1_0_0.cs` is one **3,215-line** file, one giant `static class SentinelCore` with ~30
nested types. Verdict: **splittable, not entangled — and the dependency arrows already point the right
way.**

- **Safety does not read the seams.** The entire gate region (`GateEntry`/`CanEnter`) has **zero**
  `GetXState`/`GetCouncilState` calls — it decides purely on kill/governor/session/news/rollover.
- **The seams only reach downward** — the sole cross-call out of the seam setters is to `Conditions.*`
  (a Foundation utility), never to the Gate or Ledger.

**Mechanism — split without API churn:** make it `public static partial class SentinelCore` across three
files — `SentinelCore.Foundation.cs`, `SentinelCore.Bus.cs`, `SentinelCore.Safety.cs`. **Same class, same
call sites, zero churn** across the ~40 consumer files. A partial class missing one partial still compiles
as a *smaller* class → **this is the distribution tiering**: a Rung-1 bundle ships Foundation + Bus only,
and the class simply won't have `GateEntry` (which no sensor calls). The class shrinks gracefully per rung.

**Two relocations the split surfaces:**
1. `Conditions` + `Alerts` are physically filed among Safety but are **Foundation** utilities (the Bus
   depends on `Conditions`) → move them to `SentinelCore.Foundation.cs`. Confirm `Conditions` itself
   doesn't reach back into Safety before committing.
2. The **context vetoes** (kill / instrument-kill / news / rollover) are read by *both* the Council
   (L1-side veto) and the Gate (L2) → pure state stores, so they live in **Foundation**, preserving the
   downward-only rule.

**Caveat:** arrows verified by structural grep, not a full method-by-method read; re-compile each partial
against only its downward deps to prove the split. And this file is the one NT loves to clobber at the
tail on F5 — anchor mid-class and re-verify before building.

---

## 6. Open-source posture

- **Adoption tiers, not price tiers.** Everything is free; the ladder is a depth-of-adoption path, not a
  pricing menu. Any monetization (hosted service / support / an "SDK-certified" program) rides *on top*
  of the retained trademark and is **fully optional** — nothing on the ladder depends on it. That is why
  deferring the license costs nothing.
- **License: MPL-2.0 (chosen 2026-07-11).** Weak, **file-level** copyleft — improvements to Sentinel's own
  files flow back, while others can still build proprietary tools *alongside* it. Chosen because the suite
  absorbs MPL-2.0 components (e.g. LiquidityWalls, © TradingIQ) → **no license-mixing**; it's **GPL-compatible**;
  and it sidesteps GPL's friction with the proprietary NinjaTrader host. Every source file carries the MPL
  per-file header; the **SENTINEL trademark is retained** (not granted by MPL).
- **Umbrella = "Sentinel Suite"** (decided 2026-07-10) — the public brand + monorepo (`sentinel-suite`);
  the trademark held is **SENTINEL**. Extends the Federated Naming Law: bundles read "Sentinel <Thing>"
  (Sentinel Skins / Sentinel Sensors / …).
- **Trademark retained.** Open-sourcing the *code* doesn't give away the *name*. Hold "Sentinel" as a
  mark — free code, owned brand — preserving the optional commercial door above without closing a line.
- **`NOTICE` / `AUTHORS`.** Record, in writing, that contributed-Sentinel work was released for
  open-source use — so the permission never has to be reconstructed from memory later.
- **Provenance is clean.** Only Sentinel-owned files are in scope; publicly-published indicators (WAE,
  Woodies, ADX) are reimplementations of public formulas — the implementations are ours. NinjaScript
  source is open-sourceable; we never redistribute NT's DLLs (reference them as build prerequisites).

---

## 7. Release focus & phasing

Because these are **adoption tiers, not price tiers** (§6), "focus" = *what we release first and point
newcomers at.* The rungs collapse into **4 release phases on a risk gradient:**

| Phase | Ships | Order risk | Why here |
|---|---|---|---|
| **P1 · Beachhead** ✅ **the focus** | Runtime + **Skins** (0) + **Sensors** — the **8 hero signal indicators + BarTypes** (1) | **none** | beautiful, wide appeal, proves the modular runtime, zero liability |
| **P2 · Intelligence** | the **5 orthogonal axes** (Clock/Participation/Location/MTF/Intermarket, rung 1) + Recorder+Log (2) + **Observatory** (3) + **Council** (4) | **none** | "understand + fuse" — the axes only mean something once the Council consumes them; nothing touches an account |
| **P3 · Execution** | **Prop-Survival Kit** (6, *first*) → Deck (5) → Bridge (7) → Copier (8) → **Helm** (9) | **real money** | safety ships *before* autopilot; Helm = human interdiction over any running setup; loudest disclaimers |
| **P4 · Learning** | **ML Lab** (10) | n/a | capstone; needs a baked corpus anyway |

**P1 is the confirmed focus (2026-07-10).** It carries no order-execution risk, has the widest appeal,
and *is* the platform floor every later phase plugs into. Clean risk gradient across the four phases:
**none → none → real-money → advanced.**

### Decided (formerly open questions)
- **Rung 1 splits across P1/P2** (decided 2026-07-10) — the **8 hero signal/regime indicators**
  (SentinelTrend, ADXPro, WoodiesCCIPro, VolEnvelope, CompressionBase, LiquidityWalls, God Reversal, WAE)
  + the **BarTypes** ship in **P1**; the **5 orthogonal axes** (Clock, Participation, Location, MTF,
  Intermarket) are Council inputs, so their release waits for **P2**.
- **P1 ships as two rung-bundles** (decided 2026-07-10) — **Sentinel Skins** (runtime + 6 themes) and
  **Sentinel Sensors** (runtime + bus + the 8 hero indicators + BarTypes), mirroring the two axes, plus an
  optional "grab-both" **Sentinel Starter** meta-bundle. Each bundle is self-contained (carries its own
  runtime floor per §4). This is the per-rung-bundle model for the whole ladder, seeded by P1.
- **Prop-Survival Kit is its own product** (Rung 6) — and it **leads P3** (safety before autopilot).
- **Helm is its own advanced rung** (Rung 9, decided 2026-07-10) — placed after the Bridge/Copier as
  discretionary oversight over any running setup; the human counterpart to the automatic Prop-Survival Kit.
- **The Copier is just Rung 8** — the "free lead-gen" framing dissolves when everything is open.
- **No SDK rung** — that role is the **Platform Contract** (a cross-cutting foundation, §3), not a
  product you climb to.
- **No education rung** — that role is the **Field Manual** (a cross-cutting foundation, §3).
- **Repo = monorepo** with per-rung folders + per-rung release bundles (mirrors the partial-class tiering).
- **Per-bundle SemVer** (decided 2026-07-10) — each release bundle versions independently (Sentinel Skins
  v1.0.0, Sentinel Sensors v1.x.0), releasing on its own clock as phases ship. The internal per-file
  `_vX_Y_Z` convention stays as the source-of-truth for individual tools underneath; the bundle version is
  the release/changelog marker.

### P1 Definition of Done — per bundle (bar: FULL community launch, decided 2026-07-10)

Each P1 bundle (**Sentinel Skins**, **Sentinel Sensors**) must satisfy **A–E** before release —
contributor-ready from day one.

**A · Code & compliance**
- Every shipped tool **F5-compiles clean** (NT is authoritative).
- Each sensor **fully naming-law compliant** (via the `port-sentinel-indicator` skill): "Sentinel &lt;Thing&gt;",
  glass card, label remover, published `…State` seam. A half-plumbed sensor cannot ship.
- **Bundle dependency-clean** — Skins references no Bus/Safety; Sensors references no L2 Safety. The
  downward-only rule (§4) holds at the *bundle* boundary, not just the file.

**B · Docs**
- Top-level **README** (what Sentinel Suite is, the two-axis pitch, install).
- **Install guide** (drop into `bin\Custom` → F5; NT-version prereq; we ship no NT DLLs).
- **Per-tool one-pager** each (purpose, inputs, card readout) — or a combined Sensors reference.
- Links to the **Field Manual** + **Design System**.

**C · Visual showcase** (the beauty beachhead — non-negotiable)
- A **demo workspace/template** users open to see it instantly.
- **Screenshots / GIFs** in the README, **dark + light** themes.

**D · Legal / meta**
- **NOTICE / AUTHORS** (records contributed-Sentinel work released with permission).
- **Trading disclaimer** (educational, not financial advice, no warranty) — even for indicators.
- **LICENSE** (deferred placeholder until §6 is chosen).

**E · Community scaffolding**
- **CONTRIBUTING** → the Platform Contract + the `port-sentinel-indicator` skill (how to add a compliant tool).
- **Issue / PR templates**.
- **Third-party sensor compliance checklist** (naming law + card + State seam).
- **Build verification** — ⚠ *not conventional CI.* NT compiles ONE assembly and **F5 is authoritative**;
  headless builds false-clean and produce ghosts (see ../CONTRIBUTING.md build rules). So "CI"
  here = the **documented headless-verify recipe + a manual F5 checklist**, not an automated compile gate.

---

## 8. Next steps

1. **Boundary inventory** — enumerate every Sentinel-owned file, mapped to its rung + layer. The
   license-agnostic input every downstream step needs. Buildable offline.
2. **Cut the two P1 bundles** — from the inventory, isolate **Sentinel Skins** (runtime + themes) and
   **Sentinel Sensors** (runtime + bus + the 8 hero indicators + BarTypes), plus the grab-both **Starter**.
3. **Prototype the Core partial-class split** (behind a careful F5) to prove the three-layer floor.
4. **Draft `NOTICE` / `AUTHORS`** + choose the monorepo layout.
5. ✅ **License chosen — MPL-2.0** (§6). Paste the full canonical MPL-2.0 text into `LICENSE` before publish.

### ✅ P2 ASSEMBLED (2026-07-11) — the Intelligence tier, two bundles
Both **LAYER on Sensors** for the runtime (no re-shipped `SentinelCore`/`SentinelSkin` → no CS0101), verified
**Safety-free**. See SENTINEL_BOUNDARY_INVENTORY.md §9 for the file lists.
- **Sentinel Intelligence** (Rungs 1→4): 5 orthogonal axes (Clock/Participation/Location/MTF/Intermarket) +
  **Council** + `SentinelNewsService`. Mtf/Intermarket host `SentinelTrend` (⇒ needs Sensors). **Eye DEFERRED**
  (JET engine; Council consumes its seam only, ships without it).
- **Sentinel Observatory** (Rungs 2–3): **`SentinelExcursionRecorder_v2_0_0`** (clean-room — the private v1_4
  with GodTrades21 hosting stripped; Council-only; schema 1.3 unchanged; **headless-verified clean**, awaits an
  F5) + LogEngine/LogService + Lens + Excursions.
- **Provenance:** ⚠ the boundary doc's §6 was WRONG — v1_4 Recorder EMBEDDED GodTrades (not reference-only);
  fixed by the v2 clean-room. All shipped P2 files are now provenance-clean.
- **Open at P2:** Eye clean-room · live write-validation of v2 recorder (market-open Council fires) · demo
  workspace + screenshots (DoD C) · the Dashboard Excursion-tab → Observatory consolidation (Dashboard is P3).

---

## 9. Relationship to other docs

- [ROADMAP.md](ROADMAP.md) — the pipeline map (engineering sequence). This doc is the product map.
- [SENTINEL_SHIP_MANIFEST.md](SENTINEL_SHIP_MANIFEST.md) — every indicator's hard-deps (SentinelCore +
  SentinelSkin); the raw material for the boundary inventory.
- [SENTINEL_NAMING_FEDERATION.md](SENTINEL_NAMING_FEDERATION.md) — the 4-layer naming tell; the heart of
  the Platform Contract.
- [SENTINEL_DESIGN_SYSTEM.md](SENTINEL_DESIGN_SYSTEM.md) — the palette/component/State-seam spec (L0 + the
  publish protocol).
- [SENTINEL_PROCESS_ATLAS.html](SENTINEL_PROCESS_ATLAS.html) — the Field Manual (the education foundation).
