# Bridge — Design Spec

> **STATUS: ✅ BUILT — spec retained as design record (SentinelBridge, internal v0.2.3).** The Bridge is the execution surface
> that *consumes* the Council's fused verdict and *records* it on every fire, closing the suite's loop
> (see the manual, Part VIII). Written 2026-07-07. Companion to `bridge-plan` + `naming-council-bridge`
> memories, `SENTINEL-CONTRACTS.md §6` (the Council consumer contract), and the hardening framework.
> **✅ BUILT — spec retained as design record (SentinelBridge, internal v0.2.3; **v0.3.0** adds the Helm-consumer
> role — see §10).** The design below shipped.

---

## 1. Purpose — close the loop

Today the Council *advises* (publishes `CouncilState`) but nothing *acts* on it, and — critically —
nothing **records** the verdict at the moment of a trade so we can later ask *"did agreement actually
pay?"* The weights are hunches until that record exists. The Bridge is the piece that:

1. **Consumes** `CouncilState` — gates and sizes a trade off the fused verdict.
2. **Fires** through the real safety choke point (`GateEntry`, fail-CLOSED).
3. **Records** the verdict on every fire so **Lens** can grade the weights (the whole point).
4. Eventually becomes the **on-chart autopilot control surface** — the automated counterpart to the
   manual **Deck** (nautical: trade by hand on the Deck, command the autopilot from the Bridge).

It is deliberately **not branded to one signal.** Where GTrader21 trades GodTrades BG/FC/OBR, the
Bridge trades **the Council's verdict itself** — which is strategy-agnostic by construction. That is
exactly what makes it "not branded to one strategy" (naming memory).

## 2. Where it sits (the loop the manual describes)

```
   sensors → SentinelCore → COUNCIL → CouncilState ──┐
                                                      ▼
                                              ┌──────────────┐
                                              │   BRIDGE     │  consume + gate + size
                                              │  (this spec) │  fire + RECORD verdict
                                              └──────┬───────┘
                                                     ▼ fill
                                   Ledger + SentinelLog (verdict attached)
                                                     ▼
                                          LENS grades the weights
                                                     ▼
                                    tune weights → Council gets smarter → repeat
```

## 3. The three phases (build order)

| Phase | Deliverable | Status |
|---|---|---|
| **P1** | `SentinelBridge_v0_1_0` — the **headless engine**: consume `CouncilState`, gate via `GateEntry`, size via `SizeMult`, fire a managed bracket, **record the verdict on every fire.** | ✅ **BUILT 2026-07-07** (API-verified; needs F5 + SIM validation). Note: `SizingMode` enum → `UseRiskSizing` **bool** (avoids strategy bare-enum codegen). First `.Strategies.Sentinel` member. |
| **P2** | `SentinelBridge_v0_2_0` — the **on-chart control surface**: a Sentinel glass card showing the Council verdict + the strategy's armed/position state; a **clickable ARM BRIDGE button** (ChartControl hit-test, off on load, click to arm); arming gates all firing; optional Council-flip exit. | ✅ **BUILT 2026-07-07** (needs F5 + SIM validation). Supersedes v0.1.0 (archive after F5-clean). |
| **P3** | **Eye→Council decouple** — GTrader21 (and any strategy) consults the **Council** verdict, not the Eye directly; Eye demoted to one voter. | ✅ **BUILT 2026-07-08** — GTrader21 **v0.1.7**: opt-in `UseCouncilGate` (consults `GetCouncilState` HasEdge+Aligned) alongside the legacy Eye gate; ON + Eye-gate OFF = decoupled. |

**This spec details P1.** P2/P3 are scoped at the end so P1's shape doesn't paint them into a corner.

---

## 4. Phase 1 — the headless Council-consumer engine

### 4.1 Identity & conventions — born compliant with the FEDERATED NAMING LAW

Per the ratified 4-layer law (design system §7). The Bridge is **new**, so it lands correct on day one
(no "next bump" grace — that grace is only for already-shipped tools):

- **① Display `Name`:** `"Sentinel Bridge"`
- **② Namespace:** base **`NinjaTrader.NinjaScript.Strategies`** (⚠ NOT `.Strategies.Sentinel` — NT's Strategy
  selector does NOT surface sub-namespaced strategies; identity is carried by the class prefix + display Name only)
- **③ Class + file:** `SentinelBridge_v0_1_0` / `Strategies/SentinelBridge_v0_1_0.cs`
- **④ Runtime:** cyan glass card + label remover at P2 (P1 is headless). As an **executor it consumes**
  `CouncilState` rather than publishing a `…State` seam — layer ④'s publish clause is for sensors.
- **Order tag:** a stable `"SentinelBridge"` tag (decoupled from `Name`, per the label-remover rule).
- **Hard deps:** `SentinelCore_v1_0_0.cs` (+ `SentinelSkin.cs` at P2). Standalone-safe: no Council loaded
  → `GetCouncilState` returns null → the Bridge simply never fires (neutral).
- **Versioning:** changelog in-file; strict `SentinelBridge_vX_Y_Z` naming.

### 4.2 Architecture decision — MANAGED (recommended for P1)

> **Recommendation: build P1 on the MANAGED order framework** (`EnterLong/EnterShort` +
> `SetProfitTarget`/`SetStopLoss` in ticks). Rationale, straight from the suite's own scars:

- The Bridge **owns its entire order lifecycle in code** — no external panel touches its exits in P1.
  That is precisely the case the managed framework handles cleanly and safely.
- GTrader21 went **unmanaged** only because its **panel needed to own exits** — which desyncs a managed
  Position ("Position not flat"). P1 has no panel, so that reason doesn't apply.
- Managed gives us **free restart reconciliation** — the framework restores the strategy's position on
  reload, sidestepping the entire unmanaged persist/restore/naked-position saga (GTrader21's hardest code).
- **When P2's on-chart panel needs to move a live stop by hand, THAT is the moment to evaluate going
  unmanaged** — a deliberate, later decision, not a P1 default.

**Hard rule carried in:** never manually close/flatten the Bridge's managed position with raw orders —
it desyncs Position and blocks new entries. Disable→re-enable is the recovery.

### 4.3 Entry logic

**Cadence:** `Calculate.OnBarClose` for the entry decision (non-repaint, matches how the Excursion lab
measures — so the grades and the live behavior agree). The Council itself runs `OnPriceChange`; the
Bridge reads its latest published verdict with a **staleness gate** (`StaleSec`, default 90s) so a
frozen Council can't fire a stale trade.

**The trigger (primary mode — "Council-as-signal"):**

```
on bar close:
    v = SentinelCore.GetCouncilState(instrument, StaleSec)
    if v == null:  return                       // no fresh verdict → abstain
    dir = v.Bias                                 // −1 / 0 / +1
    edge = v.HasEdge && v.Aligned(dir)           // HasEdge = !Vetoed && Bias≠0 && Conviction>0
    if edge && flat && dir != lastFiredBias:     // EDGE-DETECT: fire once per new verdict, flat-only
        tryEnter(dir, v)
```

- **Edge-detected + flat-only + one-shot per verdict:** the Council's bias persists for many bars; we
  fire on the *transition* into an aligned-edge state, not every bar it's true. `lastFiredBias` resets
  when flat.
- **Opposite-reverse (optional, default OFF for P1):** if in a position and the Council flips to an
  aligned edge the other way, reverse. Off by default keeps P1's excursions clean for grading.
- **Conviction floor is already in `SizeMult`** — the Council zeroes it below its floor, so a
  low-conviction verdict yields size 0 and is naturally skipped at the gate (below).

### 4.4 The gate + sizing path (the exact calls)

The Council is **advisory**; `GateEntry` is **authoritative** (hardening rule). Every fire runs both:

```
wantQty  = max(1, round(BaseContracts * v.SizeMult))          // Council scales the base size
sizedQty = SentinelCore.SizedQuantity(Account, wantQty)      // the ONE place sizing math lives — CLAMPS (profile scale + governor)
gate     = SentinelCore.GateEntry(Account, Instrument, sizedQty, StopLossTicks, riskDollars: 0, Instrument)
                                                              // riskDollars=0 → the Gate only VALIDATES (kill/limits/session);
                                                              // the Bridge already sized itself, so DON'T let the Gate re-size to 0
if !gate.IsClear:  logBlock(gate.Reason); return  // AUTO tool = fail-CLOSED: enter ONLY on Clear
qty      = gate.Size                              // clamped final qty (contract/qty/limit caps)
SetStopLoss(CalculationMode.Ticks, StopLossTicks)
SetProfitTarget(CalculationMode.Ticks, ProfitTargetTicks)
if dir > 0:  EnterLong(qty, "SentinelBridge")  else  EnterShort(qty, "SentinelBridge")
SentinelCore.NoteOrderSubmitted(Account.Name)     // fat-finger rate guard
```

- **`gate.IsClear` (Level==Clear)** blocks on BOTH `Hard` *and* `Advisory` — the conservative reading
  for an automated tool (an Advisory feed/session/rollover/news condition stops an autopilot). This is
  stricter than the Deck (manual, fail-OPEN).
- `GateEntry` already enforces kill / scoped-kill / loss-stop / rate / qty / contract-limit (Hard) and
  feed / session / rollover / news (Advisory). The Council *also* mirrors kill/rollover/news/liquidity
  vetoes into its verdict — so a vetoed verdict never reaches the gate anyway. Belt **and** suspenders.
- **Sizing path (the exact chain):** `baseQty × SizeMult → SizedQuantity()` (clamps: profile scale + governor
  `RecommendedSize`) `→ GateEntry(riskDollars=0)` (VALIDATES only). The Bridge sizes itself, so the Gate must not
  re-size — pass `riskDollars=0` or it computes a risk-size and can return 0 (block). `SizedQuantity` is the one
  place sizing math lives (clamps, never rejects); `ContractLimit` is enforced there as a clamp, not a hard reject.

### 4.5 Exits

- **P1 = bracket only** (managed `SetStopLoss`/`SetProfitTarget` in ticks). **Exits never gate** — the
  framework manages them. Simplest, and it matches the excursion measurement window so grades transfer.
- **Council-flip exit** (exit early if the Council flips against an open position) is a tempting mode
  but it **changes the excursion/grading semantics** — deferred, default OFF, add as an option in P2.

### 4.6 The RECORD-on-fire — the whole point

On every fire, capture a **verdict snapshot** and persist it so **Lens** can compute conditional
performance ("when `Conviction ≥ 0.7` and MTF aligned, what was the PF?"). Two writes:

```
// 1) the daily event stream (audit / journal / slippage):
SentinelCore.Ledger.Order(Account.Name, Instrument.FullName, dir>0?"Buy":"Sell", "Market",
                          qty, price, "SentinelBridge:council");
// on fill (OnExecutionUpdate):
SentinelCore.Ledger.Fill(Account.Name, Instrument.FullName, action, qty, intended, fill, tickSize,
                         "SentinelBridge:council");

// 2) the verdict itself, attached to the trade so Lens can grade it:
//    fields: Bias, Conviction, SizeMult, Agree, Disagree, Voters, Reasons, VetoReason
//    → written into the per-trade SentinelLog context (ctxJson) at entry.
```

> **⚠ Build-time confirm:** the exact hook for attaching `ctxJson` to a trade record lives in
> `SentinelLogEngine` — verify its context API when wiring this (the *intent* is fixed: every Bridge
> trade carries its entry-verdict snapshot; the *mechanism* is a 10-minute read at build). If the Log
> engine has no per-trade context slot, fall back to a `Ledger.Action("bridge-verdict", …, reasonsJson)`
> keyed to the order tag, and teach Lens to join on it. **This join is what makes the loop real —
> don't ship P1 without it.**

### 4.7 User-facing properties (P1)

Group "Bridge" unless noted:
- `BaseContracts` (int, default 1) — scaled by `SizeMult`, then clamped by `SizedQuantity()` (profile scale + governor)
- `UseRiskSizing` (bool, default false) — enum avoided (strategy bare-enum codegen); risk-sizing is opt-in
- `RiskDollars` (double, used when `UseRiskSizing`)
- `ProfitTargetTicks` (int) · `StopLossTicks` (int)
- `StaleSec` (double, default 90) — max Council-verdict age to act on
- `MinConviction` (double, default 0.0) — optional extra floor *above* the Council's own (0 = trust the Council)
- `ReverseOnFlip` (bool, default false)
- Group "Sentinel": `RecordVerdict` (bool, default **true** — never ship the grading dark),
  `LogChanges` (bool, default true)

### 4.8 Guardrails carried in (from the memories)

- Fail-OPEN advisory / **fail-CLOSED auto-gate** (Bridge enters only on `gate.IsClear`); **exits never gate.**
- A code *exception* fails open for resilience, but must never bypass the gate or freeze exits.
- Build while market **closed**; **NT F5 is authoritative**; validate at **High/Tick fill resolution**
  live (bar-level excursion is optimistic — the 81%→37.5% lesson).
- If forking an existing file, **strip NT generated regions to EOF** first; add to csproj; one version in tree.

---

## 5. The Council-Excursion feedback loop (how we get REAL numbers)

There's a gap worth naming: the Bridge trades the **Council**, but the Excursion lab today records
**GodTrades BG/FC/OBR** fires — so it can't yet tell us the right TP/SL *for a Council trade*, and it's
why the manual's worked-example numbers are placeholders.

**Proposed small enhancement (separate, optional, high-leverage):** teach `SignalExcursionRecorder` to
also record a **"Council fire"** as a signal (edge-detected Council bias-flip → a record, tagged with
conviction bucket). Then the Excursion tab's ◆ gives us the **real TP/SL for Council trades**, and
those become the Bridge's defaults *and* the manual's real numbers. One recorder change closes two open
items at once. **Not required for P1** (P1 can ship with hand-set TP/SL), but it's the natural next step
after P1 and the cleanest source of the "real numbers" you flagged.

---

## 6. Phase 2 & 3 (scoped, not built)

- **P2 — Bridge on-chart control surface (`SentinelBridge_v0_2_0`):** a `SentinelSkin` glass card showing the
  live Council verdict + the strategy's armed/position/last-fire state; an **arm/disarm** toggle
  (automation off by default on load — never silently auto-arm, the Deck lesson); optional
  Council-flip-exit and reverse toggles. This is where **unmanaged** may become necessary *if* the
  panel is to hand-move exits — decide then.
- **P3 — Eye→Council decouple:** point GTrader21's gate at `GetCouncilState` instead of `GetEyeVerdict`;
  Eye stays a voter. Low-risk once P1 proves the consume path.

## 7. Non-goals for P1 (kept honest)

- No on-chart panel, no arm/disarm UI (that's P2).
- No Council-flip exit, no scale-in/out, no trailing (bracket only).
- No multi-instrument fan-out (one chart = one instrument = one Council verdict).
- No `.conf` auto-read *yet* — the GTrader21 `.conf` is keyed by GodTrades signal, which doesn't map to
  a Council trade. A Council-specific config (or the §5 recorder enhancement) comes after P1.

## 8. Build & verify plan

1. Build `SentinelBridge_v0_1_0` market-closed; add to csproj; **F5 clean** (authoritative).
2. **SIM dry runs:** confirm it fires on a fresh aligned-edge verdict, sizes by `SizeMult`, and
   **blocks** when the kill-switch is on (fail-closed) — mirror the Test-tab gate probe.
3. **Grading proof:** take a few SIM trades → confirm each writes a Ledger order+fill **and** a
   verdict snapshot Lens can read → open Lens and see a conviction-bucketed stat. *This is the
   acceptance test — P1 isn't "done" until Lens can grade a Bridge trade.*
4. **Honest fill check:** validate the chosen TP/SL at High/Tick resolution before trusting any edge.
5. Add the market-open items to `SENTINEL_TEST_TRACKER.md` (fail-closed live, restart reconciliation).

---

## 9. Decisions — RESOLVED (user, 2026-07-07)

- **D1 — Entry mode → ✅ Council-as-signal.** The Council's aligned-edge verdict *is* the trigger (§4.3
  primary path). A `EntryMode` toggle for gated-signal can come later.
- **D2 — Order framework → ✅ Managed.** Code owns the bracket; free restart reconciliation (§4.2).
- **D3 — TP/SL source → ✅ Build the Council-excursion recorder enhancement FIRST** (§5), *before* P1, so
  the Bridge launches with real lab-derived TP/SL and the manual's placeholder numbers get filled from
  day one. **⇒ Revised build order: (0) recorder enhancement → accrue Council-fire data → read the ◆ →
  (1) `SentinelBridge_v0_1_0` with real defaults.**
- **D4 — Identity → ✅ FEDERATED NAMING LAW** (see §4.1): `SentinelBridge_v0_1_0`, `Strategies.Sentinel`,
  display `Name = "Sentinel Bridge"`. Part of the suite-wide naming federation ratified this session
  (design system §7; migration ledger for existing tools tracked separately).

### Build order (updated for D3)
1. **Recorder enhancement** — teach `SignalExcursionRecorder` to log a **Council-fire** signal
   (edge-detected bias-flip, tagged with conviction bucket). Ship, accrue data on GC.
2. **Read the ◆** for "Council" in the Excursion tab → real TP/SL → also fills the manual's worked-example
   placeholders.
3. **`SentinelBridge_v0_1_0`** — the headless consume+gate+size+fire+**record** engine, with the lab TP/SL
   as defaults.
4. **`SentinelBridge_v0_2_0`** — on-chart control surface (arm/disarm, verdict card).
5. **Eye→Council decouple.**

---

## 10. The Bridge as HELM consumer (v0.3.0 — the reference interdiction target)

**Built 2026-07-15.** The Bridge is now also the **reference consumer of the Helm interdiction seam** (`SentinelCore`
v1.34.0, design system §6e). Helm lets a human grab the wheel of the *running* Bridge — Pause it, Flatten it, move a
stop by hand — **without disabling the strategy.** The Bridge stays the sole owner of its managed orders; Helm only
publishes an `HelmIntent` addressed to the Bridge's `instanceKey`, and the Bridge executes it. This is the manual
**Deck** ▸ automated **Bridge** ▸ interdiction **Helm** trio.

### 10.1 How it drains
- **Identity:** the Bridge computes an `instanceKey` (`SentinelBridge#<scope>@<account>`) — the same key its actor
  interlock already registers (§ ML identity work).
- **Drain cadence:** every tick in a **new `OnMarketData`** handler it calls `SentinelCore.TakeHelmIntent(instanceKey)`
  **in a loop** (consuming + idempotent; drops expired), **plus an `OnBarUpdate` backstop** so an intent still applies
  in a quiet market. Realtime only.
- **Executes with its OWN managed handles** (`ExitLong/ExitShort`, `SetStopLoss`, `SetProfitTarget`) — never a raw
  order, never touching anyone else's order.
- **Publishes `HelmState` back** every pass (position/avg/stop/target/paused/override/status) so the Cockpit's ⑤ Helm
  surface renders the Bridge's reality.
- **Toggle:** `"Obey Helm intents"` (group Sentinel), **default ON.** With it OFF the Bridge ignores intents but still
  publishes `HelmState` (so the surface still sees the actor).

### 10.2 The asymmetric gate (same rule the Deck/Council live by)
- **Risk-reducing verbs = fail-OPEN** — `Pause`, `FlattenNow`, `BreakevenNow`, a **tighten** `MoveStop`, `MoveTarget`,
  scale-DOWN, `SkipNext`. A human can always cut risk.
- **Risk-adding verbs = fail-CLOSED** — `Resume`, a **widen** `MoveStop`, `HandBack`, scale-UP pass `GateEntry`
  (validate-only, `riskDollars=0`); a blocked gate refuses the intent and the Bridge stays stood-down.
- `MoveStop`/`MoveTarget` are **context-classified at apply time** (a widen is risk-adding, a tighten reducing) —
  the seam's `IsRiskReducing`/`IsRiskAdding` only answer the unambiguous verbs.

### 10.3 Per-verb behavior — managed-mode honesty
The Bridge obeys **all 10 verbs**, but a single-entry **managed** strategy can't do everything an unmanaged one could,
and it says so rather than faking it:
- `Pause` / `Resume` — stop / resume firing (Resume gated).
- `SkipNext` — arm a one-shot skip of the next fire.
- `FlattenNow` — managed exit + cancel bracket; holds `_lastFiredBias` so it does **not** instantly re-enter the same
  side (re-entry needs a genuine Council flip).
- `BreakevenNow` — stop → `Position.AveragePrice` (sticks; the engine only calls `SetStopLoss` at entry).
- `MoveStop` / `MoveTarget` — `SetStopLoss`/`SetProfitTarget` to the typed price.
- `Scale` down (`QtyDelta < 0`) — partial `ExitLong/ExitShort` of `min(N, |qty|)`.
- **`Scale` UP — REFUSED** (`applied=False`, logged): a single-entry managed Bridge can't scale-in.
- **`TakeOver` / `HandBack` — stand-down / resume**, NOT order-ownership transfer: managed mode can't hand a live
  order to another owner. Fully honoring scale-up + real take-over/hand-back needs an **unmanaged Bridge** (deferred).

### 10.4 Recording — every intent is Ledgered, every touched episode is flagged
- **Every intent → a `helm-intent` Ledger `Action`**, stamped with the Council **`EpisodeId`** and the Bridge's
  **`instanceKey`** (and `applied=True/False`).
- The interdicted episode is marked **`HumanOverride`** — so the **Lab** can find those rows and **exclude or
  separately model** them: the model grades the *policy*, not the human who reached in. This is the ML-honesty payoff.

### 10.5 Status & validation
- **v0.3.0 plumbing tier is LIVE-VALIDATED (2026-07-15):** Pause + Resume round-tripped in ~350ms on a disarmed, flat
  NQ SIM Bridge (Resume routed through the gate and CLEARED). The **position-verb tier is pending a real Bridge-owned
  trade** — full checklist in `Docs/HELM_TEST_PUNCHLIST.md`.
- ⚠ Order calls issue from the `OnMarketData` tick thread — compiles clean; watch the log for any managed
  "order ignored" / "Unable to change order" during Flatten/MoveStop (the one runtime unknown, tracked in the punch list).
