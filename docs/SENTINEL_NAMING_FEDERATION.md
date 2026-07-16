# Sentinel Naming Federation

> **STATUS: RATIFIED LAW + migration ledger — DRAFT dictionary pending your sign-off (2026-07-07).**
> This is the operational companion to the **FEDERATED NAMING LAW** (design system §7). The law says
> *what* the rule is; this doc is *the audit of where every part stands today and exactly what it
> becomes.* Goal: **every Sentinel part ships correct from today on, and the migration of existing
> parts is a checklist, not a guess.**

---

## 1. The law (one-line recap)

A Sentinel tool carries the tell on **4 layers**: ① display `Name = "Sentinel <Thing>"` · ② namespace
`…{Indicators|Strategies}.Sentinel` · ③ class/file `Sentinel<Thing>_vX_Y_Z` · ④ cyan card + label
remover (+ a `…State` seam wired to the Council, for sensors). Full detail + rationale: design
system §7.

**The migration split (why this is painless):**
- **① display `Name` is display-only → retrofit NOW, safely, on everything** (no chart/workspace loss).
- **②③ namespace + class are serialization identity → change only at each tool's NEXT version bump**
  (changing them drops the tool off saved charts). Frozen old versions keep their old identity.

---

## 2. Audit snapshot (2026-07-07, 42 files)

| Layer | Compliant today | Gap |
|---|---|---|
| ② namespace `.Sentinel` | 31/42 | Strategies 0/7 · `SentinelCopierService` (`.SentinelCopier`) · `GTrader_v1_0_0` (flat) · BarsTypes 0/2 |
| ③ class `Sentinel<Thing>` | 18/42 | most **chart indicators** are bare (`Council_v1_0_0`, `Eye_v1_1_0`, the axes, CompressionBase…) |
| ① display `Name = "Sentinel <Thing>"` | **0/42** | every indicator's Name is the raw class-token (`"Council_v1_0_0"`); services use one-word `"SentinelRiskService"` |
| ④ label remover | 19/19 indicators | (only indicators; the sole miss is `GTrader_v1_0_0`) |

**Headline:** the AddOns are basically compliant (class+namespace already `Sentinel*`), the **chart
indicators** are the real gap (bare class + class-token display Name). **⚠ STRATEGIES STAY in the BASE
`…Strategies` namespace — NOT `.Strategies.Sentinel`** (verified 2026-07-07: SentinelBridge in a strategy
sub-namespace compiled clean but NEVER appeared in NT's Strategy selector → moved back to base). Strategies
carry the "Sentinel" tell via the class prefix + display Name only; only INDICATORS fold into picker sub-folders.

---

## 3. The naming dictionary — the canonical `<Thing>` for every part

> **⚠ NEEDS YOUR SIGN-OFF.** The `<Thing>` fixes BOTH the display Name (`"Sentinel <Thing>"`) and the
> class (`Sentinel<Thing>_v…`), so agreeing it here settles layers ① and ③ at once. Rows marked **⚑**
> are genuine brand calls I need you to confirm or redline; the rest I consider obvious.

### Chart indicators (the priority — this is what reads generic on a chart today)

| Current class | Current display Name | → Display `Name` | → Target class (at next bump) |
|---|---|---|---|
| Council_v1_0_0 | "Council_v1_0_0" | **Sentinel Council** | SentinelCouncil_vX_Y_Z |
| Clock_v1_0_0 | "Clock_v1_0_0" | **Sentinel Clock** | SentinelClock_vX_Y_Z |
| Participation_v1_0_0 | "Participation_v1_0_0" | **Sentinel Participation** | SentinelParticipation_vX_Y_Z |
| Location_v1_0_0 | "Location_v1_0_0" | **Sentinel Location** | SentinelLocation_vX_Y_Z |
| Mtf_v1_0_0 | "Mtf_v1_0_0" | **Sentinel MTF** | SentinelMtf_vX_Y_Z |
| Intermarket_v1_0_0 | "Intermarket_v1_0_0" | **Sentinel Intermarket** | SentinelIntermarket_vX_Y_Z |
| Eye_v1_1_0 | "Eye_v1_1_0" | **Sentinel Eye** | SentinelEye_vX_Y_Z |
| SentinelTrend_v1_0_0 | "SentinelTrend_v1_0_0" | **Sentinel Trend** | *(class already compliant)* |
| WoodiesCCIPro_v1_0_0 | "WoodiesCCIPro_v1_0_0" | **Sentinel CCI** ✅ | SentinelCci_vX_Y_Z |
| ADXPro_v1_2_0 | "ADXPro_v1_2_0" | **Sentinel ADX** ✅ | SentinelAdx_vX_Y_Z |
| VolEnvelope_v0_2_0 | "VolEnvelope_v0_2_0" | **Sentinel Envelope** ✅ | SentinelEnvelope_vX_Y_Z |
| CompressionBase_v1_3_0 | "CompressionBase_v1_3_0" | **Sentinel Compression** ✅ | SentinelCompression_vX_Y_Z |
| LiquidityWalls_v1_0_0 | "LiquidityWalls_v1_0_0" | **Sentinel Liquidity Walls** | SentinelLiquidityWalls_vX_Y_Z |
| SignalExcursionRecorder_v1_3 | "SignalExcursionRecorder_v1_3" | **Sentinel Excursion Recorder** | SentinelExcursionRecorder_vX_Y_Z |
| Deck_v0_2_2 | "Deck_v0_2_2" | **Sentinel Deck** | SentinelDeck_vX_Y_Z |
| SentinelBrickCounter_v1_0_0 | "SentinelBrickCounter v1.0.0" | **Sentinel Brick Counter** | *(class already compliant)* |
| BuySellVolumePressureMountain_v1_0_0 | "BuySellVolumePressureMountain_v1_0_0" | **Sentinel BSVPMountain** ✅ | SentinelBSVPMountain_vX_Y_Z |
| SentinelWAE_v1_0_0 | "Sentinel WAE" ✅ | *(already compliant — born under the law)* | *(class already compliant)* |
| GTrader_v1_0_0 (flat ns) | — | **retire** ✅ | archive the pair (w/ GTraderStrategy_v1_0_0) |

### Strategies (executors — layers ①②③, no seam-publish clause)

| Current | → Display `Name` | → Target class + namespace (at bump) |
|---|---|---|
| **SentinelBridge_v0_2_0** (NEW today) | **Sentinel Bridge** | born compliant: class `SentinelBridge`, **BASE `…Strategies`** (v0_1_0 tried `.Strategies.Sentinel` → didn't list → moved to base; archived) |
| SentinelTrendStrategy_v1_0_0 (`.Strategies`) | **Sentinel Trend Strategy** | class → `SentinelTrendStrategy`; **STAYS in base `…Strategies`** (no sub-ns for strategies) |
| GTrader21v_0_1_6 (`.Strategies`) | **Sentinel GTrader** ✅ | class `SentinelGTrader_vX_Y_Z`, **STAYS in base `…Strategies`**; big move — its own thread (sheds the "21") |
| ADXProStrategy / CompressionBaseStrategy_* | *(do NOT consume SentinelCore — not suite members unless we wire them)* | — |

### AddOns / BarsTypes (already mostly compliant)

- **AddOns** — class + namespace already `Sentinel*` / `AddOns.Sentinel`. Two nits to fix at next bump:
  `SentinelCopierService` namespace is `.AddOns.SentinelCopier` (→ `.AddOns.Sentinel`), and display
  strings vary (`"SentinelDashboard"` one-word). Services have no chart label, so display normalization
  ("Sentinel Dashboard", "Sentinel Risk", …) is cosmetic/low-priority.
- **BarsTypes** — `SentinelTBars`, `SentinelTbarsCount`: class already prefixed; they live in
  `NinjaTrader.NinjaScript.BarsTypes` (bar types don't take a `.Sentinel` sub-namespace — **documented
  exception**, they group differently in NT's bar-type picker).

---

## 4. Migration ledger — what happens, and WHEN

### NOW (safe, display-only — layer ①) — ✅ EXECUTED 2026-07-07
Retrofitted all **16 suite indicator heads** in one batch: `Name` → `"Sentinel <Thing>"` per §3
(backup in scratchpad `naming-backup\`). Each file's `Name` value swapped (anchored regex — one hit
each, nothing else touched); generated regions stripped, then **NT re-appended and settled to exactly
one clean region per file** (NT's live watcher regenerated them — confirmed 1/1 across all 16, braces
balanced). **⏳ Pending: ONE NT F5 to confirm the compile + verify the picker now reads "Sentinel X".**

Files done: Council · Clock · Participation · Location · Mtf · Intermarket · Eye · SentinelTrend ·
WoodiesCCIPro · ADXPro(v1_2_0) · VolEnvelope · CompressionBase · LiquidityWalls · SignalExcursionRecorder ·
Deck(v0_2_2) · SentinelBrickCounter.

**⚠ Deck caveat (order-path):** `Deck_v0_2_2` couples its order tag to `Name` (`_tag = Name` in
DataLoaded), so its live order tag changed `Deck_v0_2_2` → `"Sentinel Deck"` (now contains a space).
**Decision pending** — recommend a stable space-free `_tag = "SentinelDeck"` (a true tag/Name decouple);
one-time continuity note: flatten/re-arm any live Deck position across the change. Services/AddOns +
BarsTypes were NOT part of this pass (no chart `Name`; cosmetic display strings only).

### AT EACH TOOL'S NEXT VERSION BUMP (layers ②③ — identity)
Rename class+file to `Sentinel<Thing>_vX_Y_Z`, confirm namespace `.Sentinel`, **archive the superseded
version** out of the tree (chart-swap first, per the archive-on-rehome sequence), update the csproj. Do
**not** retro-rename a placed tool outside a bump.

### Anomalies to resolve (each its own small task)
1. **Strategies STAY in base `…Strategies`** (do NOT sub-namespace them — NT's Strategy selector hides
   sub-namespaced strategies; verified on SentinelBridge). At their next bump, `SentinelTrendStrategy` /
   GTrader21 only adopt the **class prefix + display Name** (`SentinelGTrader`, "Sentinel GTrader"), namespace
   unchanged. `SentinelBridge` (base ns, class prefix) sets the precedent.
2. **`SentinelCopierService`** namespace `.AddOns.SentinelCopier` → `.AddOns.Sentinel` at next bump.
3. **`GTrader_v1_0_0`** sits in the flat `.Indicators` namespace *on purpose* (a header note says the
   auto-generated `[NinjaScriptProperty]` wrapper referenced the class unqualified). ⚑ **Decision:** is it
   a suite member? If yes, resolve the wrapper issue (bare-enum pattern) and rehome; if no, drop it from
   the suite list.
4. **Duplicate versions on disk** (both compile-live): `ADXPro v1_1_0`+`v1_2_0`, `Deck v0_2_1`+`v0_2_2`,
   `SentinelLens v1_0_0`+`v1_1_0`, `CompressionBaseStrategy v1_3_0`+`v1_4_0`. **Archive the older of each**
   (chart-swap first) so only one head lives in the tree — the ship-manifest one-version rule.
5. **GTrader21 version sprawl** — `v0_0_1`…`v0_1_6` + paired `Panel.cs` all on disk. Archive everything
   below the shipped `v0_1_6`.

---

## 5. Go-forward rule (the point of all this)

From today, **every new Sentinel part is born compliant** — the port skill + design system §9 checklist
enforce all 4 layers, and no PR/hand-off ships a bare-named suite tool. `SentinelBridge_v0_1_0` and the
`SentinelExcursionRecorder` bump (the D3 recorder enhancement — the first existing tool to fully adopt the
law at its bump) are the two reference cases.

## 6. Decisions — RESOLVED (user, 2026-07-07)

1. **Sensor brand `<Thing>` calls → ✅ SHORT names:** `Sentinel ADX` · `Sentinel CCI` ·
   `Sentinel Envelope` · `Sentinel Compression` (classes `SentinelAdx`/`SentinelCci`/`SentinelEnvelope`/
   `SentinelCompression` at next bump).
2. **GTrader21 → ✅ `Sentinel GTrader`** (class `SentinelGTrader_vX_Y_Z`, sheds the "21", at its next bump —
   its own thread).
3. **Layer-① display-Name retrofit → ✅ NOW** (one batch + one F5, this session).

### Resolved (user, 2026-07-07)
- **`BuySellVolumePressureMountain_v1_0_0`** → ✅ suite member. Display Name retrofitted to
  **"Sentinel BSVPMountain"** (user pick — keeps the recognizable BSVP/Mountain identity; already
  `.Sentinel` + label remover); class → `SentinelBSVPMountain_vX_Y_Z` at next bump. *(Follow-up: verify it
  publishes a `…State` seam wired to the Council — Council Protocol; if not, it needs plumbing via the port skill.)*
- **`GTrader_v1_0_0`** → ✅ **RETIRED (archived 2026-07-07).** It was the indicator form of GodTrades
  signals, hosted only by `GTraderStrategy_v1_0_0`. Both moved to `_archive\Indicators\` + `_archive\Strategies\`
  (reversible; zero remaining `.cs` references; csproj entries self-drop on the next F5).

### Newly born-compliant (no action)
- **`SentinelWAE_v1_0_0`** — the FIRST tool built under the law: Name "Sentinel WAE", class `SentinelWAE`,
  `.Sentinel`, label remover, publishes `SentinelCore.WaeState` (**Core → v1.13.0**) wired to the Council as
  a momentum-breakout voter. The reference for "born compliant."

---

## 7. ACCOUNT NAMING — the runtime axis (RATIFIED 2026-07-09)

> The law's four layers are all **build-time** names. An **account name** is a *runtime* name, and it is
> load-bearing in a way the others aren't: it is embedded in `instanceKey`, keyed in `Profiles.conf`,
> written into every Ledger row, and it is the **only** string standing between a SIM test and a funded
> account. It gets its own law.

### 7.1 The one idea

**An account is a RISK CONTAINER and a CONCURRENCY SLOT. It is not a label for what runs on it.**

Today's sim accounts — `SimJGT3 NQ 1000`, `SimKGT21TEST ES 50` — encode **strategy + instrument + bartype**.
That is `instanceKey` in disguise, invented before `instanceKey` existed. It forces account sprawl (a new
account per chart), it duplicates information the Ledger will now carry properly, and it puts **spaces** in
a string that flows into config keys and composite identifiers.

**Corollary — you need a second account only when two actors share a *scope*.** A GC Bridge and an NQ Bridge
have different `instanceKey`s and may safely share one account. They *should*: the governor is per-account,
so a shared daily-loss cap across GC + NQ **is portfolio risk**, which is exactly what a funded account
enforces. Only champion-vs-challenger on the *same* scope needs a second account.

### 7.2 The structure

> **⚠ CORRECTED 2026-07-09 by the platform.** **NinjaTrader auto-prefixes `Sim` to every simulation
> account name.** Typing `SIM-LAB-A` into NT's dialog produces **`SimSIM-LAB-A`**. Evidence, from
> `log\log.20260709.*.txt`: `Simulation account 'SimSIM-LAB-A' reset`. It also explains the legacy names —
> `SimJGT3 NQ 1000` is `Sim` + `JGT3 NQ 1000`, not "Sim J".
>
> So **drop our `SIM-` prefix**: type only `<LANE>-<SLOT>`. This is strictly better. The `Sim` prefix is
> now **guaranteed by the platform rather than asserted by convention** — a name cannot lie about being a
> simulation account, because NT wrote it.

```
type into NT:   <LANE>-<SLOT>        uppercase · [A-Z0-9-] only · ≤ 12 chars
NT stores:      Sim<LANE>-<SLOT>     e.g. SimLAB-A · SimBURN-1 · SimCOPY-LEAD
```

| Lane | Purpose | Slots |
|---|---|---|
| **`LAB`** | automated Sentinel experiments (Council → Bridge) | `A` `B` `C` — the A/B arms |
| **`COPY`** | the copier fleet | `LEAD` `F1` `F2` … |
| **`PROP`** | rehearsal against a prop firm's exact rules | `TPT50K` `LCD25K` `BLX100K` |
| **`HAND`** | manual Deck / discretionary practice | `1` `2` |
| **`BURN`** | destructive safety proofs — kill switch, auto-flatten, governor trip | `1` |

`BURN` earns its own lane. You must prove the kill switch and auto-flatten **live**, and doing that on
`SIM-LAB-A` poisons its P&L series and its persisted governor baseline. **Give destruction its own account.**

### 7.3 Forbidden characters — and why

An account name is embedded in four formats at once. These are hard exclusions, not style:

| Char | Breaks |
|---|---|
| `\|` | `Profiles.conf` field separator (`account=X\|firm=…`) |
| `=` | every `.conf` key/value split |
| `#` `@` | the `instanceKey` delimiters (`SentinelBridge#<scope>@<account>`) |
| space | CLI/grep pain; card + Cockpit column rendering |
| `"` `\` | Ledger JSONL escaping |
| `, : / * ? < >` | filenames (excursion + ledger paths) |

Uppercase is deliberate: it distinguishes our accounts at a glance from NT's built-in `Sim101` and from
broker-assigned live names. (The seam dictionaries are `OrdinalIgnoreCase`, so this is convention, not
collision-avoidance.)

> **⚠ AMENDS THE SCOPE DELIMITER.** `SENTINEL_ML_SPEC.md` originally wrote scope as `GC|TBC6-24-69697`.
> **`|` is `Profiles.conf`'s field separator** — it cannot appear in a composite key. Scope is now
> **`GC.TBC6-24-69697`**, and `instanceKey` is **`SentinelBridge#GC.TBC6-24-69697@SIM-LAB-A`**.

### 7.4 The rules the Gate enforces

A convention nobody checks is decoration. These are **assertions the name makes, which `GateEntry`
verifies before any order:**

1. **Name/venue mismatch → REFUSE.** The `Sim` prefix is NT-guaranteed, so the *real* check is the
   converse: assert `Account.Connection` against the name, both ways. A `Sim…` account on a funded
   connection, or a non-`Sim` account where the actor expected SIM, is the single worst mistake available
   in this domain — and it becomes unreachable. *(Verify at implementation time: the trace shows sim
   accounts emitting `OnConnectionStatus` under a `(Lucid)` context, so confirm which `Connection` object a
   simulation account actually reports before keying a guard on it.)*
2. **An account with no `Profiles.conf` entry → REFUSE.** Fail-closed on unprofiled accounts. No profile
   means no daily cap, no trailing drawdown, no flatten buffer — trading it is trading ungoverned.
   **⚠ Today this is fail-OPEN** (`SentinelCore`: *"Fail-open (unprofiled → unchanged)"*). Until it flips,
   an unprofiled account is *ungoverned*, which is why every account gets a line even in the interim.
3. **`Sim101` → REFUSE, always.** NT's built-in default is **quarantine**: it is where an accidental
   strategy-enable lands, so nothing Sentinel may ever arm on it. This turns the platform's default into a
   *safe* default at zero cost. **Interim mitigation (already in `Profiles.conf`):** 1 contract, a $100 loss
   stop, `hardEnforce=true` — an accident trips almost instantly and auto-flattens.

### 7.5 Live and prop accounts

Broker-assigned names cannot be renamed and are **exempt**: `TAKEPROFIT935519372`, `LTD15081874790001`.
They are identified by their `Profiles.conf` `firm=` field, never by a prefix. The `PROP` lane is for **SIM
rehearsal of a firm's rules**, not for the firm's real account.

### 7.6 Migration

| Today | Becomes | Note |
|---|---|---|
| `Sim101` | *(unchanged)* | reclassified as **quarantine** — never armed |
| `SimJGT3 NQ 1000` | `SIM-LAB-A` | drop the scope; it lives in `instanceKey` now |
| `SimKGT21TEST ES 50` | `SIM-LAB-B` | the challenger arm |
| — | `SIM-BURN-1` | new: destructive proofs |
| — | `SIM-HAND-1` | new: Deck / discretionary |
| — | `SIM-COPY-LEAD` · `SIM-COPY-F1` | new: copier fleet |
| `TAKEPROFIT935519372` · `LTD15081874790001` | *(unchanged)* | broker-assigned, exempt |

**A rename is not a rewrite.** Historical Ledger rows keep their literal `acct` string and stay joinable.
Nothing breaks retroactively.

Every SIM account gets a `Profiles.conf` line **mirroring its purpose** — `LAB` arms mirror the funded
account you are actually targeting, so recorded P&L reflects the constraints you will really trade under;
`BURN` gets a tiny cap so it trips fast; `PROP` mirrors the firm's exact rules.

### 7.7 A defect this surfaced

**164 `ALERT-CRIT` Ledger rows carry an empty `acct`** — every one of them a `NAKED POSITION` alert. The
most urgent alert the system can raise does not say *which account* it concerns. `Ledger.Action(kind,
account, detail)` is being called with `""` from the alert path. Fix alongside the Ledger context block
(execution plan step 2.3).

---

## 8. Amendment — GTrader21's destination changed (2026-07-09)

§6 decision 2 resolved *"GTrader21 → **Sentinel GTrader**."* **That is superseded.** Per the user
(2026-07-09), GTrader21 was always a prototype: *"the point was to create a managed chart trader and test
it."* The product it stands in for is **Helm**.

- The **interdiction surface owns no orders** and commands the running actor that does, via a `HelmIntent`
  seam. See `Docs/SENTINEL_EXECUTION_PLAN.html` Phase 5 and the `helm-interdiction-layer` memory.
  **✅ SHIPPED 2026-07-15** — as-built, Helm is *not* a standalone `SentinelHelm` indicator: the **seam** lives
  in `SentinelCore` (v1.34.0, `HelmVerb`/`HelmIntent`/`HelmState`), the **consumer** is **`SentinelBridge`**
  (v0.3.0 — base `…Strategies` namespace per the strategy exception in §7, obeys all 10 verbs), and the
  **surface** is a new ⑤ **Helm · interdict** rail in the **Cockpit** (v0.5.0). The reserved name still names
  the capability (the trio), not a new file.
- The **GTrader21 strategy engine** is frozen as the prototype it was and retired as a product name. Its
  TrendArchitect-derived panel is the reference implementation Helm supersedes.
- `Sentinel GTrader` is therefore **withdrawn** from the naming dictionary. **`Helm`** was reserved for
  exactly this in `naming-council-bridge`; it is now claimed.

The trio is complete: **Deck** = you drive · **Bridge** = it drives · **Helm** = you grab the wheel without
stopping the car.

---

## 9. Amendment — the display Name CARRIES THE VERSION (RATIFIED 2026-07-10)

> **Layer ① is amended.** Old rule: `Name = "Sentinel <Thing>"`. **New rule:**
> `Name = "Sentinel <Thing> v<Major>.<Minor>.<Patch>"`, with a trailing ` (DEV)` while the build is
> in development.

### 9.1 Why (the motivating bug)

NT's **Indicators picker "Available" pane lists tools by their display `Name`** — not the class name. The
class name (with its `_vX_Y_Z` suffix) only appears in the **"Configured"** pane, *after* a tool is added.
So with two in-development versions of the same tool in the tree, the picker shows **two identical
`Sentinel Trend` rows** and the operator cannot tell which version they're about to place on a chart. The
version lived in the one place you can't see at selection time. (User screenshot, 2026-07-10.)

The `_vX_Y_Z` class suffix is still correct and stays — it is the on-disk / Configured-pane identity. This
amendment adds the version to the **one surface the operator reads when picking**: the display Name.

### 9.2 The rule

```
frozen build:   Name = "Sentinel <Thing> v<M>.<m>.<p>"          e.g.  "Sentinel Trend v1.0.0"
in-dev build:   Name = "Sentinel <Thing> v<M>.<m>.<p> (DEV)"    e.g.  "Sentinel Trend v1.1.0 (DEV)"
```

- **Format:** space + `v` + **dot-separated** version (mirrors how the version reads verbally and in the
  class suffix `_v1_0_0`). Not `(v1.0.0)`, not `· v1.0.0`.
- **`(DEV)`** marks a work-in-progress head so it can never be mistaken for the frozen fallback on a live
  chart. **Drop `(DEV)` as the freeze step** when the build is promoted — the only display-Name edit a
  freeze requires.
- Still **display-only** → safe to set/change freely; it is NOT serialization identity (namespace + class
  are). Bumping the version string does not drop the tool off saved charts.
- The version in the display Name must **match** the `_vX_Y_Z` class suffix and the header CHANGELOG. All
  three move together on a bump (design-system versioning policy).

### 9.3 Migration

Retrofit the display-Name version **NOW** on every indicator head (display-only, safe), same as the §4
layer-① batch. `SentinelBrickCounter` already read `"Sentinel Brick Counter v1.0.0"` — it was the
inadvertent precedent; the law now matches it. Newly-built tools are born with the versioned Name.
`(DEV)` is applied to any head actively under development; removed at freeze.

---

## 10. Amendment — the `<Thing>` is the SOURCE INDICATOR'S FULL NAME (RATIFIED 2026-07-10)

> **This REVERSES §6 decision 1 (the "SHORT names" call).** The `<Thing>` is no longer a curated brand
> word — it is the **full name of the standard indicator the Sentinel port was derived from**, preserved
> verbatim.

### 10.1 Why (the motivating bug)

The short-name policy silently **severed the derivation trail**. The perfect example: the standard port is
`StochasticTripleFilter` and the first Sentinel port was named "Sentinel Stoch Filter" — **"Triple" was
dropped**, and over time the operator loses track of which raw indicator the Sentinel version came from. A
Sentinel port is a *derivative work*; its name must let you trace it back to its source at a glance.

### 10.2 The rule

**The Sentinel port keeps the source indicator's full name.** File / class / display all carry it verbatim
(only spacing differs — the class strips spaces, the display keeps them):

```
standard indicator:   StochasticTripleFilter            display "Stochastic Triple Filter [ATP]"
Sentinel port class:   SentinelStochasticTripleFilter_vX_Y_Z
Sentinel port display: "Sentinel Stochastic Triple Filter vX.Y.Z"   (+ " (DEV)" while in dev)
```

There must be **name fidelity between the standard and the Sentinel version every time.** Do not abbreviate,
re-brand, or curate — if the source is `WoodiesCCIPro`, the port is `SentinelWoodiesCCIPro` / "Sentinel
Woodies CCI Pro", not "Sentinel CCI".

### 10.3 What this reverses — the short-name dictionary is WITHDRAWN

§6 decision 1 mapped the sensors to short brand words (`Sentinel ADX / CCI / Envelope / Compression`).
**Those are withdrawn.** The fidelity targets:

| Current class | short (WITHDRAWN) | → fidelity display `Name` | → target class (at next bump) |
|---|---|---|---|
| WoodiesCCIPro_v1_0_0 | ~~Sentinel CCI~~ | **Sentinel Woodies CCI Pro vX.Y.Z** | SentinelWoodiesCCIPro_vX_Y_Z |
| ADXPro_v1_2_0 | ~~Sentinel ADX~~ | **Sentinel ADX Pro vX.Y.Z** | SentinelADXPro_vX_Y_Z |
| VolEnvelope_v0_2_0 | ~~Sentinel Envelope~~ | **Sentinel Vol Envelope vX.Y.Z** | SentinelVolEnvelope_vX_Y_Z |
| CompressionBase_v1_3_0 | ~~Sentinel Compression~~ | **Sentinel Compression Base vX.Y.Z** | SentinelCompressionBase_vX_Y_Z |
| BuySellVolumePressureMountain_v1_0_0 | ~~Sentinel BSVPMountain~~ | **Sentinel Buy Sell Volume Pressure Mountain vX.Y.Z** | SentinelBuySellVolumePressureMountain_vX_Y_Z |

(The tools that were already born from a Sentinel-original concept — Council, Clock, Eye, Trend, Deck,
Bridge, Location, MTF, Participation, Intermarket, WAE, God Reversal, Liquidity Walls — have **no external
source name to preserve**; their `<Thing>` is their own name and is unaffected.)

### 10.4 Migration timing (same split as always)

- **Display `Name` → NOW** (display-only, safe): retrofit every port's Name to the fidelity string + version.
- **Class + namespace → at each tool's NEXT version bump** (serialization identity; archive the superseded
  version). A short-named class that is live on charts is not renamed outside a bump.

**⚠ NEEDS SIGN-OFF on spacing/casing** for the display strings above (e.g. "Woodies CCI Pro" vs "WoodiesCCIPro",
"Vol Envelope" vs "VolEnvelope") before the retrofit batch runs.

### 10.5 Migration ledger — display-Name retrofit ✅ EXECUTED 2026-07-10

Both amendments (§9 version-in-Name + §10 fidelity) applied to **20 indicator heads** in one batch (display-only,
byte-safe `sed` anchored on `";`, backup in scratchpad `naming-batch-backup\`; generated regions stripped, NT
settles to one per file on F5). User sign-off: acronyms **expand fully** (WAE → "Sentinel Waddah Attar Explosion");
Recorder version **v1.5.0** (matches changelog, not the `_v1_4` class suffix — class catches up at its next bump).

Fidelity renames: WoodiesCCIPro→**Sentinel Woodies CCI Pro v1.0.0** · ADXPro→**Sentinel ADX Pro v1.2.0** ·
VolEnvelope→**Sentinel Vol Envelope v0.2.0** · CompressionBase→**Sentinel Compression Base v1.3.0** ·
BuySellVolumePressureMountain→**Sentinel Buy Sell Volume Pressure Mountain v1.0.0** · SentinelWAE→**Sentinel
Waddah Attar Explosion v1.0.0**. Version-only: Council/Clock/Eye/Trend/MTF/Location/Participation/Intermarket/
LiquidityWalls/GodReversal/BrickCounter/Wallpaper/ExcursionRecorder. Deck→**Sentinel Deck v0.2.2** (its `_tag`
was already the stable literal `"SentinelDeck"` — the §4 decouple caveat is RESOLVED; rename is order-path-safe).

**Class/file/namespace renames remain deferred to each tool's next version bump** (identity). **EXCLUDED:**
`SentinelV1_0.cs` — an early floating trade panel (order-entry, Deck's predecessor, non-compliant name); it is an
executor, likely superseded → handle in its own review (archival?), not this display-only batch.
**Pending: ONE NT F5** (close the edited tabs WITHOUT saving first) to confirm the compile + the picker now reads
the versioned fidelity names.
