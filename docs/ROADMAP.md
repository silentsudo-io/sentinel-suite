# NinjaTrader Suite — Vision & Roadmap

**Living document.** This is the single place that captures *what we're building and why*, so
every new Claude Code session (and you) starts oriented. Update the **Status** column as things
move. Paired with the auto-loaded ../CONTRIBUTING.md (the rules/map) and the memory
files (evolving per-project history). For the **product/packaging view** (the rungs, the runtime
floor, the open-source posture), see the companion [PRODUCT_LADDER.md](PRODUCT_LADDER.md) /
[PRODUCT_LADDER.html](PRODUCT_LADDER.html) — this doc is the *pipeline* map, that one is the *product* map.

Status legend: 💡 idea · 📝 designed · 🔨 building · ✅ shipped · ❄️ frozen checkpoint

---

## 🎯 The Vision

A cohesive **suite of trading tools** for NinjaTrader 8 — not a pile of one-off scripts — sharing
a common design language and, over time, shared infrastructure (a kill-switch, feed-health
gating, a unified logging/analytics layer). The pieces fall into four roles:

1. **Signal sources** — indicators that generate entry/bias signals (TrendArchitect MQB/MQS,
   GodTrades BG/FC/OBR, regime detectors).
2. **Execution engine** — arms signals and runs the full trade lifecycle (entry → protective
   orders → trailing → risk-gated exit) with a rich on-chart panel + risk card. *(GTrader21 /
   the ported TrendArchitect panel.)*
3. **Cross-account distribution** — the Copier AddOn: mirror one signal account to many
   execution accounts with prop-firm and instrument rules.
4. **Observation & safety** — the "Sentinel" layer: feed-health gating, a shared kill-switch,
   and post-trade analytics/logging (MAE/MFE Logger, Sentinel Log).

The guiding principle emerging from the copier work: **decouple the leader (signal) from the
execution accounts** via fill-mirroring, so safety and distribution tools can wrap the strategy
*without touching its order path*.

---

## 📍 Current State (2026-07-14) — **LATEST** (supersedes the dated snapshots below, which are kept as history)

The suite has moved well past the snapshots below. Where they disagree on a version or count, **this block wins.**

- **The Council fuses 22 VOTERS** (was 8–9). **SentinelCore → v1.33.0**, **Council → v1.8.0.**
- **⭐ Newest thread — SentinelFlux (order-flow imbalance BAR TYPE).** A from-scratch López-de-Prado information-driven
  bar type (BarsPeriodType **212203**, reserved block 212200–212299) stabilised by the TBars discipline. It closes bars on
  accumulated signed order-flow imbalance → the suite's **first genuinely orthogonal axis** (every other voter is
  price-derived). Publishes `SentinelCore.FluxState` → the **FLUX voter** + a `FluxAbsorbDamp` absorption modulator.
  Built + live-validated on a full 10/10 Council roster on `GC.212203v8`. Spec: [SentinelFlux Bars](SENTINEL_FLUXBARS_SPEC.html).
  Grading vs TBars is pre-registered as **EXP-0004**.
- **⭐ The DATA PLATFORM is BUILT** (was a plan): SQLite `Sentinel\Lab\db\sentinel.db` (WAL) + a live `--watch` ingester +
  **Streamlit :8501** + **Grafana :3000**, auto-started by the `SentinelDataPlatform` scheduled task. The ingester now
  **folds the Council VOTE VECTOR** from the `council\1.3\` corpus into the DB (~5,300 trades, ~99% carrying the vote
  vector) — the precondition for fitting the model. Spec: [Data Platform](SENTINEL_DATA_PLATFORM_SPEC.html).
- **The excursion recorder** is `SentinelExcursionRecorder_v2_0_0` (v2.1.2), Council-only: schema-1.3 ROW files in
  `Excursions\council\1.3\` + per-fire tick-path sidecars in `Excursions\council\ticks\`; v2.1.2 streams each row to disk
  on window-completion for crash-safety.
- **⭐ PER-CHART LANES — run N charts on identical bars with different systems.** A scope discriminator: set a chart's
  Council **Scope Lane** = A/B and its scope becomes `GC.212202v6x24@A` (blank = bare, back-compat), so two charts identical
  on instrument+bartype+size no longer clobber each other's CouncilState/roster/corpus. The Council publishes laned, reads
  shared sensors bare, and loads a **per-lane roster** (`Models\GC\<bartag>@A\Roster.conf`, which inherits the bar-type
  baseline) **plus a per-lane `Lane.conf` PROFILE** (ConvictionFloor / deadband / consult toggles / modulator damps; sparse
  — absent keys inherit F6). Recorder v2.1.3 files each lane into its own corpus; SentinelBridge v0.2.4 + GTrader21 v0.1.8
  consult the matching lane; the **Cockpit ⑤ BUILD (v0.4.0)** authors the lane roster + Lane.conf from the GUI. Fixes the
  SCOPE CONTENTION case. This is the A/B testing surface. (Core v1.32.0 lane registry + v1.33.0 LaneIO/cascade; Council
  v1.7.0 lane + v1.8.0 profile.) See System Builder spec §14 + the per-chart-lane memory.
- **⭐ HELM — the interdiction layer is BUILT (2026-07-15).** A human grabs the wheel of a *running* automated actor
  without stopping it (the trio: **Deck** you drive · **Bridge** it drives · **Helm** grab the wheel). Helm owns no
  orders — it publishes a `HelmIntent` addressed to a specific actor's `instanceKey`; that actor executes with its own
  order handles and stays the sole owner (the only safe path: a panel closing a managed position desyncs it and locks
  the strategy out). **SentinelCore → v1.34.0** (`HelmVerb`/`HelmIntent`/`HelmState` seam, one-shot expiry-guarded
  drain; risk-reducing verbs fail-OPEN, risk-adding pass `GateEntry` fail-CLOSED). **SentinelBridge → v0.3.0** = the
  reference consumer (obeys all 10 verbs, Ledgers every intent as `helm-intent` stamped with the EpisodeId, marks the
  episode `HumanOverride` so the Lab excludes it). **SentinelCockpit → v0.5.0** = the surface (new ⑤ **Helm · interdict**
  rail reads `AllHelmStates()` and its buttons publish intents). Plumbing tier live-validated (Pause/Resume round-tripped
  Cockpit→Bridge→HelmState→Ledger in ~350ms); position tier (Flatten/BE/MoveStop on a real trade) pending — see
  `Docs/HELM_TEST_PUNCHLIST.md`. GTrader21 is the frozen prototype Helm supersedes.
- **The standing ML payoff** is unchanged and now data-ready: fit the **ConvictionFloor** (0.20 interim) + per-bar-type
  weights from the clean schema-1.3 first-touch corpus.

---

## 📍 Current State (2026-07-07)

> **⭐ NEWEST (2026-07-11) — Sentinel goes OPEN SOURCE (MPL-2.0).** A parallel *productization* thread turned the
> suite into a shipping open-source product: the **Product Ladder** ([PRODUCT_LADDER.md](PRODUCT_LADDER.md)) + the
> **boundary inventory** (SENTINEL_BOUNDARY_INVENTORY.md); the SentinelCore
> **runtime split** (→ **v1.23.0**, now a `partial class` so the Sensors bundle omits the L2 Safety layer,
> bundle-clean-verified); the assembled **`suite-oss`** repo (in `Documents\NinjaTrader 8\Sentinel\`, *outside*
> `bin\Custom`); a full **provenance audit** (WAE was an unlicensed port → **clean-roomed** as `SentinelWAE_v2_0_0`;
> LiquidityWalls kept as **MPL-2.0** © TradingIQ); and **MPL-2.0** chosen as the license. The `port-sentinel-indicator`
> skill now **gates on license/provenance**. Read the **continue-here-2026-07-11** + **product-ladder** memories.

The suite looks and behaves like a **system**, and as of 2026-07-07 it has a **brain**: the **Council** confluence
arbiter (see below). Prior threads: (A) the **safety/correctness hardening push** to make it
fundable/prop-safe (offline build DONE; LIVE validation pending) — see
**[SENTINEL_HARDENING_FRAMEWORK.md](SENTINEL_HARDENING_FRAMEWORK.md)** + **SENTINEL_TEST_TRACKER.md**
+ `sentinel-hardening` memory; (B) a **design-system / Sentinel-homing polish** thread that made the on-chart tools
cohere and the tree self-organize; and (C) **the Deck SIGNAL ARM** — a generic indicator-plot signal → arm/auto-fire
engine (see below + `deck-signal-arm` memory). The newest thread (D) is **fusion + orthogonal signal collection** —
the Council fuses the suite's sensors into one verdict, and as of the 2026-07-07 session close **that arc is COMPLETE**:
the Council fuses **8 voters + 6 modulators/vetoes**, with **five orthogonal axes SHIPPED** (Clock · Participation ·
Location · MTF · Intermarket, all live on GC), SentinelCore at **v1.12.0**, and a codified **Council Protocol**. The next
thread (E) is the **Bridge side** — an execution surface that *consumes* `CouncilState` and *records* verdicts to the
Ledger (see Near-term sequence #8). The one open item across A/B/C remains LIVE market-open validation.

### ⭐ Current State (2026-07-08, later) — **THE COCKPIT: the suite gets a command surface**
The push after closing the loop was **operability** — a single place to answer *"is my brain alive, and why isn't it
trading?"* without opening the Indicator dialog or grepping the log.
- **`SentinelCockpit_v0_1_0` — BUILT (Phase 1), NEEDS F5.** A floatable/pinnable WPF `NTWindow` (`AddOns/SentinelCockpit_v0_1_0.cs`,
  base `…AddOns.Sentinel` ns, display **"Sentinel Cockpit"**, **Control Center ▸ New ▸ Sentinel Cockpit**) that
  **RE-READS the published `…State` seams** instead of fighting for on-chart card corners — **the ultimate dogfood of the
  publish/consult architecture (ONE consumer of all of it).** **Phase 1 = ① DECISION** (`GetCouncilState` → last verdict +
  self-computed freshness → honest **stale Ns** dot; bias pill / conviction-vs-floor track / size×; the **why-line**:
  kill ▸ governor ▸ veto ▸ **stale** ▸ **floor** ▸ size ▸ edge) **+ ② GATE** (kill-switch + per-account Governor cards).
  Instrument picker from `AllCouncilStates`; **📌 pin = `Topmost`**; `Sentinel\Cockpit.conf` persists instrument+pin; K*
  theme. **No change to any existing tool → the F5 is test-safe; SentinelCore stays v1.14.0 (reads seams only).** NEXT =
  **Phase 2** (③ Context + ④ Voters + age dots + per-card show/hide); **Phase 3** (ChartTrader dock + follow-active-chart).
  Spec **[SENTINEL_COCKPIT_SPEC.md](SENTINEL_COCKPIT_SPEC.md)**. *Motivating episode:* the Bridge "traded then stopped" —
  the Council looked *gone* but was live (67 verdicts), its card just buried in a stacked corner + a dry-up staleness
  flicker; real cause benign (conviction below the 0.35 floor → correct stand-down). The Cockpit surfaces exactly the
  stale ▸ floor pair that confused the operator.
- **`SentinelDashboard` → v1.1.9** — Accounts tab exposes the **FULL governor** in-tab (manual daily cap / reset hour /
  trailing DD + type + flatten buffer / auto-flatten); Save **merges** into the account's conf line (no longer wipes
  unmanaged fields). (v1.1.7 Home/News front page + v1.1.8 Excursion two-column redesign earlier the same day.)

### ⭐ Current State (2026-07-08) — **THE LOOP IS CLOSED: the Bridge is built and LIVE-VALIDATED**
Thread (E) landed. The suite now has a full **sense → decide → act → record** loop: sensors publish `…State` seams → the
**Council** fuses them into `CouncilState` → the **Bridge** consumes that verdict, sizes/gates/routes an order, and
**records the verdict to the Ledger on every fire** so **Lens** can grade the weights. Highlights:
- **`SentinelBridge` — the automated Council-consumer, BUILT + LIVE-VALIDATED.** (`namespace NinjaTrader.NinjaScript.Strategies`
  — deliberately the **BASE** Strategies namespace, display **"Sentinel Bridge"**.) **v0.1.0** = headless engine: reads
  `SentinelCore.GetCouncilState` (Council-as-signal, edge-detected bias-flip, one-shot per verdict, flat-only), sizes
  `baseQty × SizeMult`, routes through `GateEntry` (**fail-CLOSED**, enters only on `IsClear`), managed bracket, and
  **RECORDS the verdict every fire** (`Ledger.Order` + a `"bridge-fire"` Action) so Lens can grade the sensor weights.
  **v0.2.0** adds an on-chart Sentinel glass card with a clickable **ARM BRIDGE** button (arming gates all firing; off on
  load; never persisted), optional **ExitOnCouncilFlip**, and **`UseSentinelConfig`** (auto-reads `<inst>_COUNCIL_<dir>.conf`).
  v0.1.0 archived. **LIVE-VALIDATED 2026-07-07:** fired a SIM **GC SHORT** off the Council verdict with a managed 40t/30t
  bracket; the fill was captured in the Ledger (verified in the dashboard **Slippage** tab, tag `SentinelBridge`). See
  **[BRIDGE_SPEC.md](BRIDGE_SPEC.md)**. **CAVEAT (fixed):** the first live test fail-CLOSED because the Bridge passed
  `RiskDollars` into `GateEntry` → it risk-sized to 0 → "risk too small" Advisory; fix = pass `riskDollars=0` (the Bridge
  sizes itself; the Gate only validates). Also fixed: card reads `CouncilState` live every render; gate on `SizeMult>0`.
- **GTrader21 v0.1.7 — Eye→Council DECOUPLE done** (the P3 the roadmap listed as *planned*). New opt-in **`UseCouncilGate`**
  + **`CouncilMaxAgeSeconds`** (group 14, default OFF): `SubmitEntry` consults the Council's FUSED verdict
  (`GetCouncilState` → `HasEdge && Aligned(dir)`) instead of the Eye directly; the Eye becomes one voter *inside* the
  Council. Turn ON + `UseEyeGate` OFF to decouple. v0.1.6 frozen (both coexist).
- **Council now fuses 9 VOTERS** — the prior 8 + **WAE**. **`SentinelWAE_v1_0_0`** (Waddah Attar Explosion) is a new
  momentum-breakout voter publishing **`SentinelCore.WaeState`** — the first tool "born compliant" under the naming law.
  **SentinelCore bumped to v1.13.0.**
- **Excursion lab records the Council verdict.** **`SentinelExcursionRecorder_v1_4`** (renamed from
  `SignalExcursionRecorder_v1_3` — first tool to fully adopt the naming law; v1_3 archived) records the **COUNCIL** verdict
  as a `"COUNCIL"` signal (schema 1.2, conviction buckets LOW/MID/HIGH). **SentinelExcursions → v1.0.5** (ByConviction
  partition + `ConvictionVerdictCode` — "does conviction pay?"). **Dashboard → v1.1.6** (⑤ Conviction referee in the
  Excursion tab; **Apply ◆** writes `<inst>_COUNCIL_<dir>.conf` — the file the Bridge reads). See
  **EXCURSION_RECORDER_v1_4_SPEC.md**.
- **FEDERATED NAMING LAW ratified** (**[SENTINEL_NAMING_FEDERATION.md](SENTINEL_NAMING_FEDERATION.md)** + design-system §7):
  every Sentinel tool carries the tell on **4 layers** — display `Name="Sentinel <Thing>"`, class `Sentinel<Thing>_vX_Y_Z`,
  namespace `…Indicators.Sentinel`, cyan card + label-remover. **⚠ STRATEGIES stay in the BASE `…Strategies` namespace
  (NOT sub-namespaced) — NT's Strategy selector HIDES sub-namespaced strategies (verified on SentinelBridge).** This
  **REVERSES** the earlier "drop the prefix" convention. **17 indicator heads** had their display Names retrofitted to
  "Sentinel X" (identity/class renames happen at each tool's next bump); `BuySellVolumePressureMountain` display →
  "Sentinel BSVPMountain". **Retired/archived:** `GTrader_v1_0_0` + `GTraderStrategy_v1_0_0`, `ADXPro_v1_1_0`,
  `Deck_v0_2_1`, `SignalExcursionRecorder_v1_3`.
- **Hardening / observability:** **SentinelRiskService → v1.0.8** (persists the governor daily-P&L baseline via
  `SentinelCore.State` so a mid-day F5 no longer zeroes the day). **SentinelDashboard → v1.1.6** (Accounts cards now show
  live open/unrealized + the account's raw realized matching NT).
- **New living manual:** **[SENTINEL_SUITE_MANUAL.md](SENTINEL_SUITE_MANUAL.md)** — the zero-to-fluent suite manual
  (trader + coder).

### ⭐ NEW 2026-07-05 — Design-system homing + UX polish (all compiles clean; F5-validated in part; see `continue-here-2026-07-05` + `sentinel-namespace-and-naming` memories)
- **On-chart "flight-instrument" cards — retrofit COMPLETE.** Every retrofitted tool now draws its readout as a
  `SentinelSkin.Painter` glass card (header dot + title + state pill + track + mono rows) instead of `Draw.TextFixed`:
  **CompressionBase, ADXPro, Eye (perf grid), SignalExcursionRecorder** (the last *gained* a card — was headless).
- **`SentinelSkin.CardLayout`** (SentinelSkin internal **v1.1**) — a shared **anti-overlap registry**: cards from
  different tools docked to the same chart corner **auto-stack** (never cover each other); each card gets a
  `CardCorner` property to spread across corners. VALIDATED live (two cards stacked cleanly).
- **Namespace convention — tools group in the picker.** Suite indicators live in `namespace …Indicators.Sentinel`;
  NT's picker renders sub-namespaces as **expandable folders**, so they cluster under a **"Sentinel" folder**
  (VERIFIED). NT codegen is namespace-aware → still hostable by simple name. AddOns already use `AddOns.Sentinel`.
- **Strict naming enforced** — clean `<Thing>_vX_Y_Z` (folder supplies the "Sentinel" context; drop redundant
  prefixes; lowercase `v`, three parts). Adopt at each tool's next bump only (namespace+name = serialization identity).
- **Old versions ARCHIVED out of the tree** (13 files → `Documents\NinjaTrader 8\_archive\Indicators\`) so no one uses
  stale ones — kept only the newest Sentinel-homed head of each.
- **Label remover — MANDATORY standard.** Every Sentinel indicator hides NT's top-left name label by default
  (`ToString()` override) with a `ShowIndicatorLabel` toggle to restore it.
- **Chart candles recolored** (user pick): up = teal `#FF009999`, down = grey `#FF8E8E8E` (skin `ChartControl.xaml`);
  chart right-side margin is a per-chart property (save as Default template), not a skin key.

**The four Sentinel-homed heads (2026-07-05):** `CompressionBase_v1_3_0` · `SignalExcursionRecorder_v1_3` ·
`ADXPro_v1_1_0` · `Eye_v1_1_0` — all in `Indicators.Sentinel`, all with the glass card + `CardCorner` + label remover.

### ⭐ NEW 2026-07-06 — Deck SIGNAL ARM (generic indicator-plot → arm / auto-fire; see `deck-signal-arm` memory)
`Deck_v0_2_2` gained a **SIGNAL ARM** section (top of panel, collapsible) — the Deck can now **arm or auto-fire off ANY
loaded indicator's PLOT**, discovered from `ChartControl.Indicators` at runtime (no hardcoded signals).
- **Rules** Sign / Rising / A×B cross / Threshold + Invert; cadence bar-close/tick; **dropdown** source pickers (A/B).
- **ARM** = highlight BUY/SELL, human confirms. **AUTO-FIRE** = fail-CLOSED through the Gate, one-shot/bar, flat-only +
  opposite-reverse, forces MARKET, tag `Deck:signal`. Suppresses the state-at-enable (only a real change fires).
- **Read-race hardened** — a one-bar pulse from a foreign `OnBarClose` source is read on the just-closed bar + re-checked
  every tick (fires the bar after the signal; non-repaint). Diagnostics → `sentinel.log` (`Deck:sig`).
- **New suite convention: signal-emitting indicators expose their signal as a hidden PLOT** (transparent + `IsAutoScale=false`).
  `CompressionBase_v1_3_0` added a `Signal` plot (±1 breakout) as the reference. (The other seam is a `SentinelCore` publish.)
- **Persisted** config (survives F5/workspace; watch NOT persisted) + in-Deck **presets** (signal + entry; load/save/delete).
- **STATUS:** all compiles clean; **LIVE auto-fire validation is the sole open item** (Test tracker §D3). Kept IN-PLACE on
  `Deck_v0_2_2`; freeze `Deck_v0_2_3` once auto-fire + drag/attach both pass SIM.

### ⭐ NEW 2026-07-07 — the **Council**: the suite's CONFLUENCE ARBITER ("the brain") — BUILT ✅
`Council_v1_0_0.cs` (`namespace NinjaTrader.NinjaScript.Indicators.Sentinel`, display name **"Council"**) is a **read-only
chart indicator — it places NO ORDERS.** It is the fusion point the suite was missing: every sensor already published a
`SentinelCore` state seam, but nothing *combined* them. The Council does.
- **Consumes every existing published sensor seam for its instrument** via a weighted **VOTE**: Eye verdict, `TrendState`
  (SentinelTrend), `CciState` (WoodiesCCIPro), `AdxState` (ADXPro), `EnvelopeState` (VolEnvelope), `BrickState` (Sentinel
  bartypes) → fuses into **ONE verdict**: fused **Bias** (−1/0/+1), **Conviction** (0..1 = how ALIGNED the *fresh* voters
  are), **SizeMult** (0..1; 0 when vetoed or below a conviction floor), **agree/disagree/voter tallies**, and a compact
  human-readable **Reasons** audit string.
- **Account-free HARD VETOES** zero conviction: global kill · scoped per-root kill · rollover · news lockout · an
  absorption **wall** blocking the intended side.
- **PUBLISHES** all of the above as a new **`SentinelCore.CouncilState`** seam (**SentinelCore bumped to v1.7.0**) so any
  strategy / **Bridge** / Deck can consult the SAME decision instead of re-deriving confluence.
- Exposes hidden transparent **`Bias`/`Conviction` plots** (so the Deck SIGNAL ARM reads it generically), draws a
  **Sentinel glass card**, and **logs verdict CHANGES to `sentinel.log`**.
- **⚠ HONEST CAVEAT (record this loudly):** the sensors the Council fuses *today* are almost ALL **price-derived**
  (ADX/CCI/Trend/Envelope/Brick all echo the same OHLC), so they are **NOT independent**. **"Conviction" measures
  AGREEMENT, which is not the same as CONFIRMATION.** The Council only gets genuinely smarter as **ORTHOGONAL signal
  axes** are added — which is exactly the next thread (see **The Council & orthogonal signal collection** below).

#### ✅ SESSION-CLOSE UPDATE (2026-07-07) — the signal-collection arc is COMPLETE (all built + compiling + rendering LIVE on a GC chart)
The Council now fuses **8 VOTERS** — Eye(1.4) · SentinelTrend/`TrendState`(1.0) · WoodiesCCIPro/`CciState`(0.8×strong) ·
ADXPro/`AdxState`(0.6×strong) · VolEnvelope/`EnvelopeState`(0.6) · Brick/`BrickState`(0.5) ·
CompressionBase/`CompressionState`(0.7) · **Intermarket/`IntermarketState`(0.6)** — plus **6 modulators/vetoes**:
breadth · VolEnvelope squeeze · **Clock** (midday/off-session damp + kill-window veto) · **Participation** (rvol damp) ·
**MTF** (counter-higher-TF damp) · **Location** (into-level damp); hard vetoes = global/scoped kill · rollover · news
lockout · liquidity wall. **SentinelCore bumped to v1.12.0** across the session (seams: CouncilState v1.7.0 ·
ClockState v1.8.0 · ParticipationState v1.9.0 · LevelState+MtfState v1.10.0 · CompressionState v1.11.0 ·
IntermarketState v1.12.0). The **3 dark voters** (SentinelTrend/ADXPro/VolEnvelope) were fixed to publish **default ON**.
**COUNCIL PROTOCOL codified** (SENTINEL_DESIGN_SYSTEM.md §9 item 6): every new signal/regime/bias/context indicator MUST
publish a `…State` seam (INT/double/bool), default `PublishState` ON, and be wired into the Council (voter/modulator/veto)
— a hidden plot alone is not enough. **PARKED:** breadth internals ($TICK/$ADD — feed is DELAYED, needs a real-time
entitlement, and it's an ES/NQ tool anyway) · book/spread microstructure · VIX regime.

---

### Safety/correctness hardening (thread A)
The active work is a **safety/correctness hardening push** to make it fundable/prop-safe — see
**[SENTINEL_HARDENING_FRAMEWORK.md](SENTINEL_HARDENING_FRAMEWORK.md)** (the plan) +
**SENTINEL_TEST_TRACKER.md** + the `sentinel-hardening` memory.

**Newest since 2026-07-03 (all compiles clean; ⚠ needs live validation — market was closed):**
- **Sentinel Deck** (`Deck_v0_2_1`, indicator, `Indicators.Sentinel`) — a MANUAL discretionary order deck +
  account risk card: all order types, all price-entry methods, full trade management (bracket/OCO, breakeven,
  all 7 trail modes, scale), chart-scoped flatten, **pop-out/dock**, and the **$-risk sizer**. **2026-07-05
  suite-convention alignment:** rehomed to `Indicators.Sentinel` (Sentinel folder) + renamed `SentinelDeck_v0_2_0`→
  `Deck_v0_2_1` (strict naming) + label remover (order tags decoupled from `Name` into a stable `_tag`) + risk
  card docks via `CardLayout`/`SentinelCardCorner`. **No order-LOGIC change (⚠ still validate on SIM).** v0_1_0/v0_2_0 archived.
  **+ FEATURE (2026-07-05) — on-chart order visuals** ("Show order lines"): live ENTRY (cyan) / STOP (red) /
  TARGET (green) lines with R/$/tick chips (right-side labels default; editable width). **`Deck_v0_2_2`** adds
  **DRAG-TO-ADJUST + HOVER-ATTACH** (⚠ modifies live orders — SIM-first): hover a STOP/TARGET line → drag to
  re-price the working order (`StopPriceChanged`/`LimitPriceChanged` + `Account.Change`); drop on an overlay
  indicator plot to **attach** (order follows the plot each tick, throttled; only-improve toggle). Esc cancels.
  Also v0.2.2: risk-card **bar timer/tick counter**, button-sizing fixes. v0_2_1 frozen.
- **SentinelSkin** framework (`AddOns/SentinelSkin.cs`) — the shared on-chart drawing library (palette +
  Painter) every tool uses; + a platform **Sentinel skin** (`templates/Skins/Sentinel/`) that themes candles/
  chrome/selection. SentinelV1_0, SentinelEye, CompressionBase repaletted to the tokens.
- **HARDENING — the OFFLINE build list is COMPLETE (SentinelCore v1.1.0, SentinelDashboard v1.1.4).**
  The 3 substrates + their views are all built; only LIVE (market-open) validation remains (Test tab).
  - **Gate** — `GateEntry`/`SizeForRisk`/`TickValue` + rate guard, wired Deck (fail-open) + GTrader21/Copier
    (fail-closed); **hard auto-flatten** (opt-in `hardEnforce=true`).
  - **Ledger + State** — `SentinelCore.Ledger` daily JSONL (`Order`/`Action`/`Fill`) + READ API; `SentinelCore
    .State` keyed restart-surviving store. **Fill capture wired into ALL 3 order sources** (GTrader21/Deck/
    Copier→copy-slippage). **Position-state persist + restore** (GTrader21, restore = opt-in, safe 3-case).
    Reconnect reconciliation (naked-position detect+alert).
  - **Alerts** — 2-tier `SentinelCore.Alerts` + **`SentinelAlertService`** (sound + optional push shell cmd,
    `Sentinel\Alerts.conf`). Clock/TZ (`resetHour=`), readiness view, config-git.
  - **VIEWS** — dashboard **Journal** (blotter/audit + live tail), **Slippage** (execution quality), **Test**
    (alert config + dry-run gate probe + safe self-checks + ledger audit), + **reusable WPF chart primitives**
    (`HBars`/`HDivBars`/`SignedBars`/`Columns`) across the data tabs.
  - NEXT = LIVE validation only (market was closed): kill proof, alert sound, restore, auto-flatten, reconnect.

Highlights since the Jul-1 snapshot below:

- **SentinelDashboard** (v1.1.4) — **12 tabs**: **Copy · Log · Risk · Journal · Slippage · Lens · Eye · Arc ·
  Assist · Excursion · Accounts · Test** (all live). Reskinned "flight-instrument" palette + chart primitives.
- **SentinelCore v1.0.9** — shared registries/gates: kill-switch, **per-instrument scoped kill**, feed-health,
  Eye verdicts, **fleet plan (Arc)**, manual-assist tickets, rollover, news lockout, config-use, **consistency
  governor**, **account profiles**. Combined entry gate `CanEnter` = kill + scoped-kill + feed + governor +
  **session** + rollover + news; `CanActInstrument` for the copier; `SizedQuantity` for profile sizing.
- **GTrader21 v0.1.6** (v0.1.5 frozen; v0.1.4 was the live ES FC-in-trend test) — Sentinel entry gates (`CanEnter`),
  feed-watch registration, Arc/Eye/Trend gates, **lab-config auto-read** (`GTraderConfigs\*.conf`) + reports back,
  **profile-aware sizing + session gating**.
- **Signal lab (measurement ≠ execution):** `SentinelExcursionRecorder_v1_4` (renamed from `SignalExcursionRecorder_v1_3`,
  now records the **COUNCIL** verdict as a `"COUNCIL"` signal — schema 1.2, LOW/MID/HIGH conviction buckets; `Indicators.Sentinel`;
  glass card + label remover; raw maxMFE/maxMAE per signal, regime+eye tagged) → **SentinelExcursions v1.0.5** (ByConviction
  partition + `ConvictionVerdictCode`) + the **Excursion tab** (edge chart, per-signal detail, expectancy grid w/ ◆ best-responsible,
  Eye referee, **⑤ Conviction referee**, fire-rate) → **Apply ◆ / Sync-all** writes `.conf` (incl. `<inst>_COUNCIL_<dir>.conf`
  the Bridge reads) → GTrader21 auto-reads. First finding: **FC-in-trend is the edge; OBR is noise.** See EXCURSION_RECORDER_v1_4_SPEC.md.
- **Safety (Risk v1.0.6):** feed lag/stall watchdog with **hysteresis** + **auto feed-recovery**; **scoped** (per-
  instrument) auto-kill; **rollover countdown**; **news lockout** (`News.conf`); **consistency governor** hosting
  (`Profiles.conf`, firm presets). **Copier v0.1.0g** — scoped-kill + governor + session gating per follower.
- **Observability:** `state.json` (v1.0.7) now carries risk/eye/arc/assist/configs/governor/profiles/eyeReferee blocks.

**Immediate next (all LIVE / market-open — the offline build is done):** open the dashboard **Test** tab and run
the safe checks now (alert sound, dry-run gate probe, self-checks, ledger audit); then on a Sim session validate
the market-open items in **SENTINEL_TEST_TRACKER.md**: kill-switch proof, GTrader21
position-state **restore** (turn on the opt-in prop, recompile mid-position, confirm NO duplicate stop), stop-fill
**slippage**, **auto-flatten**, and **reconnect** naked-position alert. Then re-arm the live ES FC-in-trend test and
validate the governor/session/sizing gates funded. See `memory/sentinel-hardening.md` for the blow-by-blow.

## 🧠 The Council & orthogonal signal collection (✅ the axes are SHIPPED — 2026-07-07)

The **Council** (shipped 2026-07-07, above) is the fusion seam. It is only as good as the axes it fuses — and the
original sensors were almost all price-derived and therefore correlated (**agreement ≠ confirmation**). The push this
session was to add **ORTHOGONAL signal axes** — genuinely independent information — each of which:
- **publishes its own `…State` seam** (so the Council votes on it, and so any tool can consult it), and
- **records to the `SentinelCore.Ledger` on every fire** (so **Lens** can grade whether the axis actually adds edge).

**STATUS: the ladder is DONE — five orthogonal axes shipped as their own `Indicators.Sentinel` NO-ORDER indicators
(publish default ON), all wired into the Council + live-verified on a GC chart. The COUNCIL PROTOCOL is codified**
(SENTINEL_DESIGN_SYSTEM.md §9 item 6): every new signal/regime/bias/context indicator MUST publish a `…State` seam and
be wired into the Council — a hidden plot alone is not enough.

1. ✅ **Clock** — `ClockState` (Core v1.8.0). Session phase, mins-to-close, kill window (via `SessionIterator`).
   **MODULATES** every voter's weight (midday / off-session damp + kill-window veto) rather than casting its own vote.
2. **Event veto** — `EventState`. **Already has a consumer path** (folded into the news-lockout veto): `SentinelRiskService`
   reads `Sentinel\News.conf` → `SentinelCore.SetNewsLockouts` → `CanEnter`, and the Council vetoes on `NewsLockoutActive`.
   The user's Python **`EconomicCalendar.py`** (ForexFactory fetch → scored JSON) should FEED this — its `block_windows`
   land in `News.conf`. **CAVEATS:** (a) **freshness guard** — treat a stale/missing calendar as *fail-to-caution*;
   (b) its directional `bias_score` is **EQUITY (MNQ)-specific** and must **NOT** be consumed by gold (GC/MGC) — only the
   **blackout windows** are universal; (c) timestamps are **ET wall-clock** (need ET→session conversion). *(A dedicated
   `EventState` seam beyond the existing news-lockout path is still open.)*
3. ✅ **Participation** — `ParticipationState` (Core v1.9.0). Time/bar-**normalized** relative volume (RVOL) + climax /
   dry-up detection. Modulates via an rvol damp.
4. ✅ **Location + MTF** (Core v1.10.0):
   - ✅ **Location** — `LevelState`: VWAP + bands, prior-day H/L, opening range, initial balance, session H/L + the
     **nearest level**; modulates via an into-level damp.
   - ✅ **MTF** — `MtfState`: bias on the **1/5/15/60/240** ladder, per-TF trend = **hosted SentinelTrend**; modulates
     via a counter-higher-TF damp.
5. ✅ **Intermarket** — `IntermarketState` (Core v1.12.0), the fifth axis and a genuine **8th VOTER** (0.6). Configurable
   correlated instruments, **instrument-agnostic polarity** (default **ZN+** for gold). Built precisely because **DXY isn't
   on the feed but ZN/ZB are real-time** — the concrete answer to the parked "Internals" item for gold.
6. **PARKED** (keep on the list): **breadth internals** ($TICK/$ADD/$VOLD/$TRIN — the feed is **DELAYED** without a
   real-time entitlement, and it's an ES/NQ tool anyway) · book/spread **microstructure** · **VIX / vol-term-structure**
   regime.

## 🏷️ Naming / renames

- **The brain is named "Council"** — deliberately **NOT "Architect"** (rejected to avoid collision with the existing
  **TrendArchitect** panel).
- **The automated-strategy chart trader is named "Bridge"** — pairs with the manual **Deck** (nautical: trade by
  hand on the **Deck**, command the autopilot from the **Bridge**). **"Helm" = you grab the wheel** of a running
  actor without stopping the car — the discretionary interdiction layer. *(✅ BUILT 2026-07-15 — see Current State
  2026-07-14; Bridge ✅ BUILT + LIVE-VALIDATED 2026-07-07 as `SentinelBridge` — see Current State 2026-07-08.)*
- **Eye is DECOUPLED from GTrader21** *(✅ done, v0.1.7)*. The Eye is just **one sensor** publishing `EyeVerdict` to the
  bus (already generic at the Core level); **GTrader21 is now just one strategy among many** — its opt-in `UseCouncilGate`
  consults the **Council's fused verdict** rather than consulting the Eye directly.
- **FEDERATED NAMING LAW** (**[SENTINEL_NAMING_FEDERATION.md](SENTINEL_NAMING_FEDERATION.md)** + design-system §7): the
  4-layer tell (display `Name="Sentinel <Thing>"` · class `Sentinel<Thing>_vX_Y_Z` · namespace `…Indicators.Sentinel` ·
  cyan card + label-remover). **⚠ EXCEPTION — strategies stay in the BASE `…Strategies` namespace** (NT's Strategy
  selector hides sub-namespaced strategies; verified on SentinelBridge). This **reverses** the earlier "drop the prefix"
  convention. 17 indicator heads got the display-Name retrofit; class/identity renames land at each tool's next bump.

## 🛠️ Tool Inventory & Status

### Execution engine
| Tool | Role | Status | Notes |
|---|---|---|---|
| **GTrader21** | GodTrades signals + WPF trade panel + risk card + data-lag safety + Sentinel entry gates / auto-read / profile sizing + **Council gate** | 🔨 **v0.1.7 active** | **v0.1.7 = Eye→Council DECOUPLE** — opt-in `UseCouncilGate` + `CouncilMaxAgeSeconds` (group 14, default OFF): `SubmitEntry` consults the Council's fused `GetCouncilState` (`HasEdge && Aligned`) not the Eye; turn ON + `UseEyeGate` OFF to decouple. v0.1.6 ❄️ frozen (both coexist). v0.1.4 ran the live ES FC-in-trend test. See `gtrader21-panel-integration` memory. |
| TrendArchitectMQPanel | The original panel this was ported from (7 trail modes, session risk, arming) | ✅ v1.6.1 (source) | Lives as an indicator; being absorbed into GTrader21. |
| **Sentinel Bridge** | Automated-strategy chart trader that consults the **Council's** fused verdict — the autopilot counterpart to the manual **Deck** | ✅ **v0.2.0 built + LIVE-VALIDATED (2026-07-07)** | `SentinelBridge` (**BASE `…Strategies` namespace** — sub-namespaced strategies are hidden by NT's selector). v0.1.0 = headless engine (edge-detected Council bias-flip → `baseQty×SizeMult` → **fail-CLOSED** `GateEntry` → managed bracket → **records the verdict to the Ledger every fire**). v0.2.0 = + glass card **ARM BRIDGE** button + `ExitOnCouncilFlip` + `UseSentinelConfig` (reads `<inst>_COUNCIL_<dir>.conf`). LIVE: fired a SIM GC SHORT, fill captured in Ledger (Slippage tab, tag `SentinelBridge`). See [BRIDGE_SPEC.md](BRIDGE_SPEC.md). |

### Suite architecture (decided 2026-07-01: **Hybrid**)
Each tool is its own headless `AddOnBase` service (fault isolation) + one shared **`SentinelCore`**
(kill-switch, feed-health gate, settings dir — the only cross-tool dependency) + one **unified
tabbed dashboard** (`SentinelDashboard`) whose tabs attach to each service's `.Instance`.
Product family: **Copy · Log · Risk · Lens · Arc · Eye** (from the user's naming).

| Component | Role | Status | Notes |
|---|---|---|---|
| **SentinelCore** | Shared registries + gates + the **hardening substrates** (Order **Gate**, State **Ledger** + intended-state **State** store, **Alerts**) + `CanEnter`/`SizedQuantity`/`GateEntry`/`SizeForRisk` + the **`CouncilState`** fused-verdict seam + the axis seams (`ClockState`/`ParticipationState`/`LevelState`/`MtfState`/`CompressionState`/`IntermarketState`/`WaeState`) | ✅ **internal v1.13.0** | `SentinelCore_v1_0_0.cs` — class name unversioned (shared symbol), edited in place. Session ladder: v1.7.0 CouncilState · v1.8.0 ClockState · v1.9.0 ParticipationState · v1.10.0 Level+Mtf · v1.11.0 Compression · v1.12.0 Intermarket · **v1.13.0 WaeState**. See design-system §6 + `sentinel-hardening` memory. |
| **Council** | **Confluence ARBITER ("the brain")** — read-only chart indicator (NO ORDERS). Fuses **9 voters** (Eye/Trend/Cci/Adx/Envelope/Brick/Compression/Intermarket/**WAE**) + **6 modulators/vetoes** (breadth · Envelope-squeeze · Clock · Participation · MTF · Location) → fused Bias/Conviction/SizeMult + Reasons; account-free hard vetoes; publishes `CouncilState` | ✅ **v1.0.0 built + live** | `Council_v1_0_0.cs` (`Indicators.Sentinel`). Hidden `Bias`/`Conviction` plots (Deck-ARM-readable) + glass card; logs verdict CHANGES to `sentinel.log`. Signal-collection arc COMPLETE; 5 orthogonal axes + WAE feed it. ✅ **Bridge now consumes `CouncilState` + records verdicts to the Ledger (loop closed).** |
| **Sentinel WAE** | Momentum-breakout voter (Waddah Attar Explosion) publishing `WaeState`; first tool "born compliant" under the naming law | ✅ **v1.0.0 built** | `SentinelWAE_v1_0_0.cs` (`Indicators.Sentinel`). Council's **9th voter**. |
| **SentinelDashboard** | Control Center > New → one window, tab per tool | ✅ **v1.1.6** | **12 tabs** LIVE: Copy · Log · Risk · Journal · Slippage · Lens · Eye · Arc · Assist · Excursion · Accounts · Test. v1.1.6: ⑤ **Conviction referee** in the Excursion tab (**Apply ◆** writes `<inst>_COUNCIL_<dir>.conf` the Bridge reads); Accounts cards show live open/unrealized + raw realized. Chart primitives + flight-instrument reskin. |
| **SentinelAlertService** | Audible + push channel — subscribes `SentinelCore.Alerts.Raised` → sound (wav/SystemSounds) + optional push shell cmd | ✅ **v1.0.1** | `SentinelAlertService_v1_0_0.cs`; config `Sentinel\Alerts.conf`; Test tab edits it live. |
| **SentinelStateService** | 2s snapshot of accounts/positions/orders/P&L/governor/etc → `Sentinel\state.json` (readable outside NT) | ✅ **v1.0.7** | `SentinelStateService_v1_0_0.cs`. Observability, not restore (that's `SentinelCore.State`). |
| **Sentinel Eye** | Adaptive GodTrades scanner (JET's engine) that QUALIFIES; Copier mirrors only Eye-qualified | ✅ **built + validated; Sentinel-homed** | **`Eye_v1_1_0`** (`Indicators.Sentinel`; was `SentinelEyeV1_0`, archived) → glass-card perf grid + label remover → publishes verdict to SentinelCore → Copier Eye-gate (off by default) + Eye tab. Verdict keyed by INSTRUMENT (rename-safe). **Being DECOUPLED from GTrader21 (planned):** Eye is just one sensor publishing `EyeVerdict` to the bus; consumers should read the **Council's** fused verdict, not the Eye directly. See `sentinel-eye-tool` + `sentinel-namespace-and-naming` memories. |
| **Sentinel Lens** | On-demand analytics over Sentinel\Log JSONL (winrate/PF/MAE/MFE, per strategy+instrument) | 🔨 **v1.0 built** | `SentinelLens_v1_0_0.cs` (static, no service) + Lens tab. Pairs with Log. See `sentinel-lens-tool` memory. |
| **Sentinel Log** (MAE/MFE) | Zero-touch + tier-2 MAE/MFE trade-excursion logging + live monitor | ✅ **rebranded & integrated** | `SentinelLogEngine`/`SentinelLogService` (ex-MAE), monitor = the Log tab. JSONL → `Sentinel\Log`. Old MAE files archived out of tree. See `sentinel-log-integration` memory. |

### Cross-account distribution
| Tool | Role | Status | Notes |
|---|---|---|---|
| **Sentinel Copy** (Copier) | Primary→Followers fill-mirror; same-provider prop rule; GC↔MGC cross-trading; Gate/kill/governor/session/Eye gates; manual-assist tickets; copy-slippage capture | ✅ **core validated live; internal v0.1.0h** | `SentinelCopierService_v0_1_0.cs`. VALIDATED 2026-07-01: same-instrument mirror (entry+exit), GC→MGC cross-map ×10, config persist/auto-resume. Untested: Block policy, live kill-switch, multi-contract/partial, multi-follower, copy-slippage. See `copier-addon-skeleton` memory. |

### Observation & safety ("Sentinel" layer)
| Tool | Role | Status | Notes |
|---|---|---|---|
| **Sentinel Risk** | Feed lag/stall + connection watchdog; auto-engages the kill-switch on a breach | ✅ **v1.0.8 built** | `SentinelRiskService_v1_0_0.cs` + Risk tab. Fills `SentinelCore.FeedHealthProbe`; halts Copy on lag. **v1.0.8 persists the governor daily-P&L baseline via `SentinelCore.State`** so a mid-day F5 no longer zeroes the day. See `sentinel-risk-tool` memory. |
| **Shared kill-switch** | One flag that suppresses entries/mirroring across all tools | ✅ in SentinelCore | Now DRIVEN by Sentinel Risk (auto) + dashboard toggle (manual); consumed by Copy's mirror-gate. |
| **MAE/MFE Logger** ("Sentinel Log") | Log max adverse/favorable excursion + trade analytics per trade | 💡 idea | Called out repeatedly as a future AddOn. Post-execution analytics layer. |
| **Provider-health indicator** | Live per-account connection health surfaced in the UI | 💡 idea | Extends the single-feed lag protection to per-account, provider-aware. |

### Signal sources & analytics (existing library)
| Tool | Role | Status |
|---|---|---|
| AdaptiveRegimeDetector | Regime classification 0–3 (Ranging/Trending/Breakout/Exhaustion) | ✅ |
| OrderFlowDivergence | Price vs cumulative-delta divergence | ✅ |
| LiquidityPoolMapper | Unmitigated S/R zones | ✅ |
| MTFConfluenceOscillator | Multi-timeframe strength −100..+100 | ✅ |
| GodTrades21 | BG/FC/OBR entry signals (GTrader21's signal source) | ✅ |
| _~516 indicators / ~98 strategies total_ | Large existing library | mixed |

#### Orthogonal signal axes (Council voters/modulators — `Indicators.Sentinel`, NO ORDERS, publish default ON, live-verified 2026-07-07)
| Axis indicator | Publishes | Council role | Status |
|---|---|---|---|
| **Clock** | `ClockState` (session phase / mins-to-close / kill window) | modulator + kill-window veto | ✅ v1.0.0 |
| **Participation** | `ParticipationState` (time/bar-normalized RVOL + climax/dry-up) | rvol modulator | ✅ v1.0.0 |
| **Location** | `LevelState` (VWAP/bands · PDH-PDL · OR · IB · session H-L + nearest level) | into-level damp modulator | ✅ v1.0.0 |
| **MTF** | `MtfState` (1/5/15/60/240 ladder; per-TF trend = hosted SentinelTrend) | counter-higher-TF damp modulator | ✅ v1.0.0 |
| **Intermarket** | `IntermarketState` (configurable correlated instruments; default ZN+ for gold) | **8th VOTER** (0.6) | ✅ v1.0.0 |
| **CompressionBase** | `CompressionState` (breakout ±1) | voter (0.7) | ✅ (wired 2026-07-07) |
| **Sentinel WAE** | `WaeState` (Waddah Attar Explosion momentum-breakout) | **9th voter** | ✅ v1.0.0 (2026-07-08) |

> **Idea for later:** "regime gating" — only arm a signal when the regime detector agrees.
> Cross-cuts signal sources + execution engine.

---

## 🧭 Near-term sequence

Items 1-6 below (the original Jul-1 skeleton plan) are all ✅ **done** — SentinelCore, Copier, all dashboard
tabs, config persistence, feed-health probe (Risk), and stop-handling (pure fill-mirror kept) all shipped.
Current sequence:

0. ✅ **DONE — the whole offline hardening build** (SentinelCore v1.1.0 · Dashboard v1.1.4 · AlertService v1.0.1 ·
   GTrader21 v0.1.6 fill-capture/persist/restore · Deck v0.2.0 + Copier v0.1.0h fill-capture). Compiles clean, F5'd.
1. 🔜 **LIVE validation via the Test tab + SENTINEL_TEST_TRACKER.md**: kill-switch proof
   (probe→HARD), alert sound, GTrader21 restore (opt-in; recompile mid-position; **no dup stop**), stop-fill slippage,
   auto-flatten, reconnect naked-position alert.
2. **Validate the safety gates on a Sim account** before funded use: governor caps/loss-stop, per-account session
   windows, and profile sizing (set `size=0.5|contracts=2|session=<now>|dailyLoss=<small>` and watch the logs).
3. **Accrue the lab data:** run FC-in-trend on GC + NQ (per-instrument TP/SL from their own excursion medians);
   let Eye accrue so the **Eye referee** produces a verdict.
4. **Governor phase-5** (SizeScale/dilution after an over-cap day) once there's cross-day P&L to work with.
5. **Consume `session` / `ContractLimit` more widely** and wire per-account **profiles** into the copier sizing
   if the desync concern is resolved.
6. **Docs freshness discipline** (roadmap thread #5): keep ROADMAP + the specs + memory current at each milestone.
7. ✅ **DONE — the Council has real independence.** The orthogonal signal-collection ladder is SHIPPED: **Clock ·
   Participation · Location · MTF · Intermarket** — five independent axes, all wired into the 8-voter/6-modulator
   Council and live-verified on GC (see **The Council & orthogonal signal collection** above). *(A dedicated `EventState`
   seam beyond the existing news-lockout path remains open; breadth internals / microstructure / VIX are parked.)*
8. ✅ **DONE — the Bridge closed the loop.** `SentinelBridge` (v0.2.0) consumes `CouncilState` (gate on HasEdge + Aligned,
   size `SizeMult → GateEntry`) and records the verdict to the `SentinelCore.Ledger` on every fire; **LIVE-VALIDATED** on a
   SIM GC short (fill captured, Slippage tab). The **Eye→Council decouple** also shipped (GTrader21 v0.1.7 `UseCouncilGate`).
9. 🔜 **NEXT ACTIVE THREAD — validate the closed loop + start grading it.** (a) **LIVE-validate an overnight Bridge run**
   (arm on SIM, confirm it fires/gates/records over a session, no naked positions); (b) **Lens grading** — run Lens over the
   Bridge/Excursion Ledger records to answer *does conviction actually pay?* and feed the answer back into the sensor weights
   / `<inst>_COUNCIL_<dir>.conf`; (c) build the **Event/news veto axis** — a dedicated `EventState` seam beyond the existing
   news-lockout path, fed from `EconomicCalendar.py` `block_windows` (freshness guard · equity-bias-not-for-gold · ET→session);
   (d) clear the pending **SENTINEL_TEST_TRACKER.md** safety proofs (items 1-5 above).

---

## 📚 Where the deeper detail lives

- **Build rules & versioning policy:** ../CONTRIBUTING.md
- **GTrader21 full build history & lessons:** `memory/gtrader21-panel-integration.md`
- **Copier architecture & decisions:** `memory/copier-samples-analysis.md`
- **Advanced suite build docs:** AdvancedSuiteDocumentation.md
- **Per-indicator/strategy quick reference:** [QuickReferenceGuide.md](QuickReferenceGuide.md)
- **Original Sentinel design transcripts:** `~/Downloads/Sentinel_Project_*.txt`
