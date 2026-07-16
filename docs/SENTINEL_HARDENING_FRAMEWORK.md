# Sentinel — Hardening Framework (path to a system you'd fund)

The plan for turning "a cockpit that looks like a system" into "a system safe enough for a funded
account." This is the north star for the safety/correctness work — read it before building any of
the items below so we build **substrates, not 18 disconnected tools.**

---

## North Star (the acceptance test)

> **Could a tired human at 9:32am — after a disconnect, on a prop account — blow up the account
> using this?** Every item below exists to move that answer toward a hard **No.**

The vision doesn't change: **a trading system a human operates with confidence.** We are not adding
features; we are making the operator unable to accidentally hurt themselves and able to trust what
they see. If a proposed build doesn't serve that, it doesn't ship.

---

## The key insight: 18 items collapse into 3 substrates + their views

The "missing elements" list looks like 18 tools. It isn't. Almost every item is either **a rule at
the order-submission choke point**, **a consumer of one event/state stream**, or **a view of health
state**. Build the 3 substrates and most items become cheap views. This is the anti-sprawl move.

| The item | Is really… | Substrate |
|---|---|---|
| Risk-based position sizer | a sizing rule at submit | **1 Gate** |
| Hard daily-loss / consistency enforcement | a hard rule at submit | **1 Gate** |
| Fat-finger / rate guards | a rule at submit | **1 Gate** |
| Stale-feed gating on entries | a rule at submit | **1 Gate** |
| "Why blocked" explainer | the Gate's reject reason | **1 Gate** |
| Kill-switch *proof* | a live test *through* the Gate | **1 Gate** |
| Trade journal / blotter | a view of the event stream | **2 Ledger** |
| Action audit log | a view of the event stream | **2 Ledger** |
| Slippage / execution quality | intended-vs-actual fill from the stream | **2 Ledger** |
| Position-state persistence | the intended-state store | **2 Ledger** |
| Reconnect reconciliation | diff intended-vs-actual on reconnect | **2 Ledger** |
| Pre-trade checklist panel | a view of health state | **3 Health** |
| Alerting | a watcher on health + ledger events | **3 Health** |
| Clock / timezone correctness | a fix to the health foundation | **3 Health** |
| Config backup / versioning | orthogonal insurance | **Phase 0** |
| Runbook depth | human ops doc | **Phase 4** |

---

## The 3 substrates

### Substrate 1 — The Order Gate  (a single pre-submit choke point; lives in SentinelCore)
Every order — Deck, GTrader21, Copier — passes through **one** gate before `acct.Submit`. The gate:
- **Sizes** — `SizeForRisk(acct, instr, stopTicks, riskDollars)` → qty. (The single most valuable
  daily-use feature.)
- **Enforces hard limits** — daily loss, consistency %, contract cap. *Hard* means it can refuse an
  entry and/or trigger an auto-flatten, not just advise.
- **Guards** — max size, max orders/interval (fat-finger + runaway).
- **Gates on feed health** — advisory-loud for manual, blocking for automated.
- **Returns a reason** — one string that answers "why blocked / why this size" (that IS the D2
  explainer, for free).

**Absorbs:** risk sizer, hard limits, fat-finger, feed gate, why-blocked. **Enables:** the live
kill-switch test (once every order truly routes through here, "engage kill" is verifiable).

`SentinelCore.CanEnter`/`SizedQuantity` are the seed — they're advisory today. The work is: a HARD
mode, the sizing function, the guards, and **wiring every submitter to call the gate.**

### Substrate 2 — The State Ledger  (event stream + intended-state store; EXTENDS SentinelLog)
One append-only, structured record of everything that happens, plus a snapshot of what *should* be
true. **Do not build a second journal — extend SentinelLog** (it already records trade excursions).
- **Event stream:** order submitted / fill / cancel / automated action (copier mirror, governor
  block, kill fire) / state change — each timestamped, on disk.
- **Intended-state store:** the working orders, position, and trail/BE arm-state we *believe* exist.
  Survives restart.
- **Reconcile:** on reconnect/restart, diff intended-vs-actual → surface the delta.

**Absorbs:** journal/blotter (a view), audit log (a view), slippage (intended-vs-actual fill from the
stream), position-state persistence (the store), reconnect reconciliation (the diff).

### Substrate 3 — The Health layer  (~70% already exists in SentinelCore + SentinelRiskService)
Feed health, connections, governor, kill, news, and the **clock/TZ** the governor's daily reset +
sessions depend on. Mostly built — the remaining work is the **views** on top and one **fix**:
- **Pre-trade checklist panel** — feeds green · accounts connected · governor reset · news loaded ·
  kill off → "cleared to trade." A view.
- **Alerting** — a tiered watcher (critical: kill/limit/disconnect · info: everything else) that
  notifies when you're *not* staring at the screen.
- **Clock/TZ fix** — make the reset time explicit, TZ-correct, and *displayed*. A wrong reset TZ =
  silently breaking a prop rule.

---

## Dependency map (what must come before what)

```
Phase 0  Config→git ─┐   Clock/TZ fix ─┐            (cheap, unblock correctness)
                     │                 ↓
Phase 1              └─► ORDER GATE ◄── reads Health(3)     ← highest safety + daily value
                          ├ risk sizer   ├ hard limits
                          ├ guards       ├ feed gate  ├ why-blocked
                          └► KILL-SWITCH LIVE TEST (through the gate)
                          └► wire Deck → GTrader → Copier
Phase 2              STATE LEDGER (extend SentinelLog)      ← post-mortem + don't-lose-track
                          ├ event stream  ├ intended-state store
                          ├► reconciliation + persistence (detect+ALERT, opt-in auto)
                          └► journal / audit / slippage  (views)
Phase 3              HEALTH VIEWS                            ← operator confidence
                          ├ pre-trade checklist  └ alerting (watches Gate + Ledger events)
Phase 4              Runbook depth · path-to-live validation
```

The gate is first because everything protective funnels through it and it delivers the sizer (daily
value) on day one. The ledger is second because reconciliation/persistence need its store, and the
journal/audit/slippage/alerting all consume its stream. Health views are last because they *read*
1 and 2. Category-A cosmetics (GodTrades repalette, graph polish) fill gaps between phases — never on
the critical path.

---

## Where these work AGAINST each other — and the resolutions

These tensions are real; designing them wrong is how safety systems backfire.

1. **Hard enforcement ⟂ manual discretion.** The Deck is deliberately advisory — a human must always
   be able to *exit*. Hard auto-flatten seems to contradict "never block a human."
   **Resolution:** the Gate distinguishes **entry (hard-gatable)** from **exit (always allowed)**;
   the auto-flatten *is* an exit, so it's permitted; hard mode is **armed per-account** (on for prop,
   off for discretionary sim). Never trap a human trying to get out.

2. **Single choke point ⟂ fail-safety.** If the Gate throws, does trading stop (fail-closed) or
   proceed (fail-open)? **Resolution: per-caller.** Manual Deck = **fail-open + loud warning** (never
   trap a human on a code bug). Automated GTrader/Copier = **fail-closed** (don't trade what you
   can't validate).

3. **Centralization ⟂ blast radius.** One Gate + one Ledger means one bug affects everything.
   **Resolution:** centralizing is *safer* (one place to get right) but raises the stakes of that
   place — so **test rigor scales with centralization.** The Gate and Ledger get the most tests and
   the SIM-first discipline; nothing else routes live until they're proven.

4. **Reconciliation autonomy ⟂ safety.** Auto-canceling an "orphaned" stop is catastrophic if the
   logic is wrong (it might kill a legit protective stop). **Resolution:** reconciliation is
   **detect + ALERT by default** ("position with no stop" / "unmanaged working order from before the
   restart"); **auto-act is explicit opt-in only.** Wrong autonomous action is worse than a human
   prompt.

5. **Ledger ⟂ latency.** Order submission is latency-sensitive; the ledger writes on every event.
   **Resolution:** the Gate's checks are cheap/in-memory; the Ledger writes **async** (never in the
   submit path).

6. **Alerting ⟂ noise.** Over-alerting trains you to ignore alerts — the opposite of safety.
   **Resolution:** two tiers only (critical vs info); critical is rare by construction.

7. **New tools ⟂ the vision.** Every item here maps to a substrate or a view — **if a build doesn't,
   question it.** Extend SentinelLog/SentinelCore; don't spawn parallel journals or gates.

---

## Definition of done (the go-live gate)

**STATUS (2026-07-04): every item below is BUILT + compiles clean; the checkboxes are the LIVE-validation
gate, still open because the market was closed.** `[B]` = code built, needs live verify · `[x]` = verified.

A funded account is trusted to this system only when:
- [B] Every order (Deck, GTrader, Copier) routes through the **Gate**; SIM-verified. *(built; SIM-verify)*
- [B] **Kill-switch proven live** to stop new entries + (armed) auto-flatten. *(Test-tab dry-run probe →
      HARD proves the gate logic now; live-fire pending.)*
- [B] **Hard daily-loss + consistency** enforced on prop-armed accounts (`hardEnforce=true`); SIM-verify vs
      the firm's real numbers + reset TZ.
- [B] **Risk sizer** in the Deck (`$ RISK` toggle) + Test-tab dry-run probe; sizing math checked live.
- [B] **Reconnect** produces a correct **detect+alert** (pull the ethernet on SIM and watch).
- [B] **Journal + audit** capture a full session (dashboard Journal tab; the ledger JSONL is the export).
- [B] **Pre-trade checklist** reads green (Risk tab readiness); **critical alerts** fire on kill/limit/
      disconnect **+ audibly** (`SentinelAlertService` — verify sound/push from the Test tab).
- [B] Position-state **persist + restore** survives a mid-position restart (GTrader21 opt-in; verify **no
      duplicate stop**). *(added to the go-live gate this session.)*
- [x] Config under **git** (`Documents\NinjaTrader 8\Sentinel\`). · [x] **runbook** covers stuck-position /
      broker-reject / NT-hang. *(DONE — SENTINEL_RUNBOOK.md exists.)*

---

*Companion to `Docs/SENTINEL_DESIGN_SYSTEM.md` (how it looks) — this is how it stays safe.
Related memory: [[sentinel-backlog]], [[sentinel-suite-architecture]], [[sentinel-risk-tool]],
[[sentinel-log-integration]], [[consistency-governor]] / Docs/CONSISTENCY_GOVERNOR_SPEC.md.*
