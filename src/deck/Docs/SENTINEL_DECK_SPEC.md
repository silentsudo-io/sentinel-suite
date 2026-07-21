# Sentinel Deck — Manual Discretionary Order Deck (spec)

**Status:** 🟡 **PUBLIC TESTERS' PREVIEW (DEV) — auto-fire deliberately UNVALIDATED** · **Author:** Sentinel Suite · **Date:** 2026-07-21
**Artifact:** `Indicators\SentinelDeck_v0_2_5.cs` · class `SentinelDeck_v0_2_5` · display `"Sentinel Deck v0.2.5 (DEV)"`
**Namespace:** `NinjaTrader.NinjaScript.Indicators.Sentinel` (picker folder **Sentinel**)
**Rung:** 5 (*The Deck*) · **Layer:** L2 · **Phase:** P3 Execution — see `PRODUCT_LADDER.md`
**Hard dependencies:** `SentinelCore` **Foundation + Safety** · `SentinelSkin`
**Requires:** NinjaTrader **Chart Trader open** on the host chart (account + instrument come from its selectors)

> One line: your own Buy/Sell deck on the chart — full order types, bracket/breakeven/trailing management, an
> account-tracking risk card, and a generic signal reader that can **arm** you or **fire for you** — where the deck
> **owns every order it places** and a human can always act, especially to exit.

---

## 1. Why this exists

NinjaTrader's native Chart Trader is competent but fixed: its order types, its bracket handling, its layout, its
colours. The Deck exists to be **the discretionary surface the rest of Sentinel can see** — it publishes nothing a
strategy has to trust, but everything it does lands in the same `Ledger`, the same `sentinel.log`, and the same
excursion corpus as the automated side. One trader, one telemetry trail, whether the click was human or machine.

It is also the **manual half of a duality**: the Deck is to a human what the **Bridge** (Rung 7) is to the Council.
Same Gate, same Ledger, same account model — different hand on the trigger.

---

## 2. Order ownership — why account-level UNMANAGED

The Deck submits with `Account.CreateOrder` / `Account.Submit` and **owns its orders outright**. This is a
deliberate rejection of the managed framework, for reasons the suite learned the hard way:

- A **managed strategy's** position must never be closed by hand or by a raw order — doing so desyncs the internal
  `Position` from the account and blocks all new entries with *"Position not flat"*, recoverable only by disabling
  and re-enabling the strategy.
- The managed framework **re-asserts `SetStopLoss` prices**, so a panel edit of a managed stop silently reverts.
- A panel using `Account.Change` / `Account.Cancel` **cannot touch strategy-submitted orders** — attempting it
  throws *"Unable to change order"* and, with `RealtimeErrorHandling.StopCancelClose`, kills the strategy.

Owning the orders removes all three failure modes at the cost of writing the exit logic ourselves. That trade is the
correct one for a discretionary tool: **the human must always be able to act, especially to exit.**

**Order naming** is the audit key: `<tag>_<ACTION>` for a manual click and `<tag>_SIG_<ACTION>` for an auto-fire
(tag = `SentinelDeck`). Fills are captured off the account's `ExecutionUpdate` and written to the Ledger, which is
what feeds the dashboard's slippage view.

---

## 3. The safety model — an asymmetry, not a lock

**This is the most important section in the document.**

| path | gate behaviour | rationale |
|---|---|---|
| **manual click** | `SentinelCore` is **advisory** — kill switch, governor, feed health, rollover and news are *displayed*, never blocking | a human must always be able to act, especially to exit a position |
| **auto-fire** | **fail-CLOSED** through `SentinelCore.GateEntry` — a `Hard` reason aborts the submit and says why | a robot acting into a kill state has no judgement to override it |

That asymmetry is the design. It means the Deck **cannot** be described as "gated" or "protected" — a human holding
the mouse is the last line, by choice (decision 2026-07-03, reaffirmed 2026-07-21 for the public preview).

### 3.1 The preview band (v0.2.5)

Because there is no SIM lock, the **warning band is the only mitigation on the manual path**, so it is built to be
load-bearing rather than decorative:

- **Sits under the header, above every control** — impossible to miss.
- **Classifies by `Account.Provider`**, *not* by connection and *not* by name. `Connection.Options.Provider`
  describes the connection supplying data: NT re-homes `Sim101` to whatever feed is connected, so a connection-based
  test reads SIM in Playback and flips to LIVE the instant a prop connection exists. A name test is worse — a live
  account may be called `Sim-anything`, and a real simulation account may be called `SimKGT21TEST GC 70`.
- **Fails toward REAL.** Null account, null connection, `Unknown`, or any exception ⇒ shown as a broker account.
- **Runs on its own 1s UI timer.** It reads the Chart Trader selector directly and never depends on tick flow — a
  tick-driven readout displays a stale account indefinitely in a quiet market, which is how it would come to say
  "SIM" while a funded account is selected.
- **Red is spent only on `REAL + auto-fire armed`.** A red band on every live session is alarm fatigue, and alarm
  fatigue is precisely how a warnings-only mitigation fails.
- **Says "REAL orders (prop eval or funded)", never "real money."** NT cannot distinguish an evaluation account
  from a funded one, and a prop eval still costs a fee, a reset, or the account.

### 3.2 What the Deck never does

It holds no strategy position, publishes no `…State` seam, and takes no part in Council fusion. It reads Sentinel;
Sentinel does not read it.

---

## 4. Panel anatomy

Top to bottom, as built:

| section | controls | notes |
|---|---|---|
| **header** | theme cycle `S` · pin `^` · pop-out `[]` · version chip | theme cycles 7 skins via `SentinelSkin.TryParseTheme` |
| **preview band** | *(read-only)* | §3.1 — four states |
| **diagnostics** | `Export diagnostics for a bug report` | §8 |
| **SIGNAL ARM** | presets · `Rescan sources` · Source A/B · `Rule:` · `Invert` · `Mode: ARM / AUTO-FIRE` · `Eval:` cadence · `Signal watch` | §6; collapsed by default |
| **ORDER TYPE** | `MKT` `LMT` `STP` `STLM` | |
| **QUANTITY** | `−` / qty / `+` · presets `1 2 5 10` · `$ RISK` + amount | `$ RISK` sizes from stop distance via `SizeForRisk` |
| **actions** | `BUY` · `SELL` · `Reverse` · `Close` · **`FLATTEN THIS CHART`** | flatten is **atomic** `Account.Flatten`, scoped to this chart's instrument on the selected account |
| **BRACKET / STOP** | `Stop tk` · `Target tk` · `Attach Bracket` · `Add Stop` · `Auto on entry` | |
| **BREAKEVEN** | `Trigger tk` · `Offset tk` · `-> Breakeven` · `Auto BE` | |
| **TRAILING** | `Trail` `BE+` `BarHL` `NBar` `ATR` `Magic` `HalfBE` · params · `Auto-trail on entry` | one active mode at a time |
| **SCALE** | `Close Half` | |
| **RECORD** | `Log Tick Path` | §7 |

---

## 5. On-chart surface

- **Risk card** (SharpDX) — account, day P&L, position, unrealised, open risk, bar timer. Follows the
  `SentinelSkin.CardLayout` docking rules like every other suite card.
- **Order lines** — entry / stop / target with price pills. **Drag** a line to re-price; **hover** a loaded
  indicator's plot to attach; **click** the chart to set a working price.
- **`Order lines ALWAYS ON TOP` overlay** — SharpDX `OnRender` loses z-order to other indicators' cards, so the
  lines can render *under* them. The overlay redraws the same line data on a hit-transparent WPF canvas above all
  SharpDX output. Line geometry is computed once (`ComputeOrderLines`) so the two paths cannot diverge.
- **DPI** — the chart scale works in device pixels while WPF mouse events arrive in DIPs. Every mouse-Y is converted
  before hit-testing, so drag and snap stay accurate above 100% display scaling.

---

## 6. SIGNAL ARM — a generic plot reader

No hardcoded signals. Sources are discovered from `ChartControl.Indicators` at runtime, so **any** loaded
indicator's plot can be a signal.

- **Rules:** `Sign(>0)` · `Rising` · `A × B` cross · `Threshold`, each with `Invert`.
- **Modes:** **ARM** (default) highlights the primed BUY/SELL button and waits for a human; **AUTO-FIRE** (opt-in,
  amber) submits by itself — **fail-closed**, one-shot per bar, flat-only plus reverse, forced to MARKET.
- **Cadence:** bar-close (default) or every tick.
- **The pulse read-race** (load-bearing): a one-bar pulse plot read from a foreign indicator is racy. An
  `OnEachTick` consumer reading the *current* bar of an `OnBarClose` source gets a not-yet-computed value, so a
  one-bar signal is missed forever. The Deck reads the **just-closed bar** (`barsAgo = 1`) and re-checks every tick,
  so bar-boundary processing order self-heals. Signals therefore fire on the bar **after** the source — confirmed,
  non-repainting.
- **Persistence:** signal configuration and presets persist across F5 and workspace save. **`Signal watch` does
  NOT persist, by design** — automation must never silently re-arm itself on a chart reload.

---

## 7. Recording & telemetry

- **Tick-path tape** (`Log Tick Path`, default OFF) passively records the tick-by-tick price path of a manual
  trade — begins on a flat→in-position transition at the *account* level, so it also catches native Chart Trader
  fills. Writes `Sentinel\Excursions\ticks\<id>.jsonl` on the return to flat. **It never touches the order path.**
  Prices come from `OnMarketData` (the true last trade), not `Close[0]` — on an HA/Renko/TBars chart `Close[0]` is a
  synthetic brick close, which quantised and biased the earlier MFE/MAE numbers.
- **Ledger** — every submit writes a row tagged **`Deck`** (manual) or **`Deck:signal`** (auto-fire). That tag is
  what makes auto-fire gradable: it separates robot decisions from human ones without inference.
- **`sentinel.log`** — `Deck` (general), `Deck:sig` (the full signal decision trail: transition, already-fired-this-bar,
  already-in-direction, no account/instrument, BLOCKED + reason), `Deck:acct` (account classification evidence),
  `Deck:trail` (per-bar trail diagnostic), `Deck:attach` (attach candidate under the cursor).

---

## 8. Diagnostics export

One button writes `Sentinel\Support\deck-diag-<timestamp>.txt`: environment (theme, display scale, overlay DPI,
Chart Trader present, docked/floating) · position and card values · every panel section's live configuration ·
**the chart's indicator inventory with plot indices** · the `[Sentinel:Deck` log trail · today's Ledger rows.

Its purpose is narrow and deliberate: **auto-fire ships to testers unvalidated on purpose — their runs are how it
earns a green light — and that only works if a run produces evidence rather than an impression.** The
instrumentation already existed; *retrieval* was the gap, since all of it sat in a log on the tester's machine.

Single `.txt` on purpose: nothing for a stranger to assemble incorrectly.

---

## 9. Configuration & persistence

Runtime settings are serialised via public properties that are **not** `[NinjaScriptProperty]` — they persist to the
workspace/template without touching the generated region or risking bare-enum codegen. The deliberate exception is
the live arm/watch flag (§6).

---

## 10. Known-open issues (disclosed, not hidden)

| # | issue | status |
|---|---|---|
| 1 | **Auto-fire has never been live-validated** | shipping enabled **on purpose** — this preview is how it gets graded |
| 2 | **Drag-to-attach snap can fail** — the grab works; the snap misses (racy UI-thread plot read; some plot styles are not enumerable) | open |
| 3 | Manual clicks are **not** gate-blocked | **by design** (§3), not a defect |

---

## 11. Build, verify, freeze

- **Compile:** `C:\ntbv\Scripts\python.exe -m nt8bridge compile` (NT's own compiler, real Roslyn diagnostics).
- **Load:** a bridge compile validates only. Writing the `.cs` into `bin\Custom` while NT runs triggers NT's own
  recompile + assembly reload; otherwise F5.
- **Version:** the single source of truth is the `DeckVersion` constant. `Name` reads from it, the diagnostics
  export reads from it. Do **not** read `Name` at runtime — the Sentinel label remover blanks it in `DataLoaded`.
- **Freeze step:** drop ` (DEV)` from `DeckVersion` (naming federation §9). That is the whole ceremony.

---

## 12. Open questions

1. **Does auto-fire deserve a green light?** — the question this preview exists to answer. Grading rail: Ledger
   `Deck:signal` rows joined to fills, plus the `Deck:sig` decision trail.
2. **Should the Gate ever block a manual click?** — currently never. A "hard kill blocks even the human, except
   exits" variant has been discussed and rejected once; the preview may produce evidence either way.
3. **Attach snap** — worth repairing before or after the preview?
