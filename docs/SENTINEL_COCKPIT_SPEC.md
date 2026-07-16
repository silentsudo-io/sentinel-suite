# The Sentinel Cockpit — Spec (v0, design)

**Status:** ✅ BUILT — `SentinelCockpit_v0_1_0` (Control Center ▸ New ▸ Sentinel Cockpit). Spec retained as design record.
**v0.5.0** adds the **⑤ Helm · interdict** section (the Cockpit's first *writer* role — see §4b).
**Named by the user (2026-07-08), TOP backlog priority.** See memory `sentinel-backlog` (the Cockpit block) +
`state-seam-freshness-heartbeat` (the flicker this must expose) + design system §4b (CardLayout/Painter).

---

## 1. The problem it kills

On a working chart there are now ~22 Sentinel voters, each drawing its own **SharpDX glass card** in `OnRender`
via `SentinelSkin.CardLayout.Place`. Per-corner auto-stacking overflows → cards **overlap → unreadable**.

**The motivating episode (2026-07-08):** the Bridge "traded a lot then stopped." From the chart it looked like the
Council was *gone* — its card even flashed *"no verdict — add the Council."* Reality: the Council was live the whole
time (67 verdicts); its card was just **buried in the stacked corner**, and "no verdict" was a dry-up **staleness
flicker**. The real cause was benign — conviction sat **below the interim 0.20 floor** so the Bridge correctly stood down.

The operator had to open the Indicator dialog and I had to grep `sentinel.log` to answer a question the system
should answer at a glance: **"is my brain alive, and why isn't it trading?"** That question is the Cockpit's whole job.

---

## 2. The architecture decision (and why it's basically forced)

**The Cockpit is a WPF surface that RE-READS the published `…State` seams — not a SharpDX on-chart rail.**

Every sensor already publishes its state to `SentinelCore` (the Council protocol, design system §9). So the Cockpit
doesn't need the chart's render target at all — it just consults the same seams the Council does. That single fact
resolves the fork the backlog raised:

| | SharpDX on-chart rail | **WPF reads-the-seams (chosen)** |
|---|---|---|
| Off-chart float | hard (chart-bound render target) | **free** (WPF window — proven by the Deck) |
| Always-on-top pin | n/a | **free** (`Window.Topmost`) |
| Dock / undock | n/a | **free** (ChartTrader host + float, like the Deck) |
| Theme | reuse Painter tokens | reuse **K\* brushes** (like Deck/Dashboard) |
| Data source | — | the **same** `SentinelCore` seams the cards already read |
| Consumers fighting for corners | yes (the bug) | **no** — ONE consumer |

The on-chart SharpDX cards **stay** (nothing is removed); the Cockpit is a parallel, opt-in readout. A user who
wants a clean chart turns off each indicator's `ShowCard` and reads everything in the Cockpit instead.

This makes the Cockpit **the ultimate dogfood of the publish/consult architecture** — it is the one place that
consumes *all* of it.

---

## 3. Form factor

- **Primary:** a floating **NTWindow** ("Sentinel Cockpit" under Control Center ▸ New), like the Dashboard — but
  narrow and tall (a rail, ~300×720). **Pinnable** (always-on-top) so it never hides behind the chart.
- **Secondary (phase 3):** an optional **ChartTrader dock** (right edge of a chart window, like the Deck) with a
  collapse handle — the "slide-out" feel, for users who want it attached to one chart.
- **Instrument scope:** a header dropdown picks which instrument's seams to show (seams are keyed by instrument).
  Default = **follow the active chart**; manual override sticks.

---

## 4. Layout — four sections, decision-first

A vertical scroll of **collapsible sections**. The hero decision card is always visible (never collapses). Order is
deliberately "can I trust it → am I allowed → what's the context → who voted":

### ① DECISION (hero, always visible) — `CouncilState`
The one verdict. Big **BIAS** pill (LONG/SHORT/FLAT, green/red/mute) · **conviction %** with a track vs the **floor**
marker · **size ×** (accent if >0, mute if 0) · **▲agree ▼disagree · Nv** · the **Reasons** audit (wrapped, dim).

**The killer line — computed "why":** one plain-language sentence, resolved in priority order:
1. `kill-switch` → **BLOCKED — kill-switch engaged**
2. governor not allowed → **BLOCKED — {account} day {halted/complete}**
3. `Vetoed` → **VETOED — {vetoReason}** (news / rollover / liquidity wall / kill window)
4. stale (no fresh seam within StaleSec) → **STALE — no fresh verdict for {n}s** *(the flicker, now named — not "gone")*
5. `conviction < floor` → **STAND DOWN — conviction {c} < floor {f}**
6. `size == 0` → **STAND DOWN — size 0**
7. `!HasEdge` → **NO EDGE — waiting**
8. else → **READY — {side} · size ×{size}**

Line 4/5 are exactly the two things that confused the operator in the motivating episode. Surfacing them = the point.

> **⑤ Helm · interdict was added in v0.5.0 — see §4b below.** ① DECISION stays the hero; ⑤ Helm is a monitor-rail
> section, so the "four sections" framing above is unchanged for the read-only readout.

### ② GATE — "am I cleared to trade"
- **Governor** (per governed account): status chip (Trading / DayComplete / DayHalted), **day P&L vs cap** track,
  **vs loss-stop** track, reset-hour. Reads `AllGovernorStates` + account profiles.
- **Kill-switch**: `KillSwitchEngaged` (global) + scoped kills.
- **Trailing DD**: `DrawdownState` — equity vs the firm floor (how close to liquidation).
- **Feed / News**: feed-lag health + active news lockout (from the Council veto / RiskService).

### ③ CONTEXT — the modulators (why conviction is damped)
`ClockState` (session phase · mins-to-close · kill window) · `ParticipationState` (rvol · climax/dry-up) ·
`LevelState` (VWAP/PDH-PDL/OR location · level-in-path) · `MtfState` (higher-TF ladder · counter?) ·
`IntermarketState` (correlated lean, e.g. ZN for gold). Each shows its value + how it's nudging conviction.

### ④ VOTERS — the confluence, unburied
One compact row per voter, each showing **dir arrow** (▲/▼/~), a small **conviction/quality**, and whether it
**agrees** with the Council bias (cyan tick) or dissents (dim): `TrendState` · `CciState` · `AdxState` ·
`EnvelopeState` · `BrickState` · `CompressionState` · `WaeState` · `GodReversalState` · `EyeVerdict` ·
`LiquidityState` (veto). This is the "5 up / 1 down" split at a glance — no Indicator dialog needed.

---

## 4b. ⑤ Helm · interdict — the Cockpit becomes a WRITER (v0.5.0)   *(SentinelCore ≥ v1.34.0)*

Everything above is **read-only** — the Cockpit consults seams. The **⑤ Helm · interdict** monitor-rail section
(added v0.5.0) makes the Cockpit a **writer of INTENTS** — the same way the ⑤ BUILD tab writes `Roster.conf`.
**It still never touches an order.** It publishes an `HelmIntent` to a running actor's `instanceKey`; the actor
(the Bridge — `Docs/BRIDGE_SPEC.md §10`) drains and executes it with its own handles. This is the Helm
interdiction layer (design system §6e · memory `helm-interdiction-layer` · `Docs/HELM_TEST_PUNCHLIST.md`) — a human
grabs the wheel of a running automated actor without stopping it.

- **What it reads:** `AllHelmStates()` → picks the actor for the section's instrument and shows its `instanceKey` ·
  `Status` · signed position · live stop / target · paused / `HumanOverride` flags · a **freshness dot** (green when
  the actor is publishing `HelmState`, like every other card).
- **How it picks the actor:** by the section's instrument. The header instrument picker only auto-lists instruments
  that have a **Council**; to interdict a **Bridge that has no Council on its chart**, **type a bare instrument**
  (e.g. `NQ`) and the section resolves it against `AllHelmStates()`.
- **The button set (each publishes an `HelmIntent` via `SentinelCore.SetHelmIntent`):** `Pause` · `Resume` · `Skip`
  · `Flatten` · `BE`; **type-a-price** boxes → `Stop→` / `Tgt→` (`MoveStop`/`MoveTarget` to the typed price); a
  **reduce N** box → scale-down; `TakeOver` / `HandBack`.
- **Persistent controls:** the typed price / reduce-N boxes **survive the 750 ms refresh** (they are not rebuilt each
  tick) so a value you're typing isn't wiped mid-edit.
- **The asymmetric gate lives in the CONSUMER, not here** — the Cockpit publishes intent freely; the Bridge fail-OPENs
  risk-reducing verbs and passes risk-adding ones through `GateEntry` (design system §6e). The Cockpit is a command
  surface; the owner decides whether the command is allowed.

---

## 5. Interaction & chrome
- **Header:** instrument picker · ⌂ follow-chart toggle · ◹ dock/float · 📌 pin (always-on-top) · ⤢ collapse-all · theme follows the active skin (K\* brushes, like Deck/Dashboard).
- **Sections:** click header to collapse/expand (▸/▾). Hero never collapses.
- **Per-card show/hide:** a small settings sheet to choose which seams appear (some users won't care about CCI/ADX).
- **Staleness:** every card shows a faint **age dot** — green (fresh) → amber (stale past its StaleSec). This is the
  general fix for "is it live?" across the whole suite, not just the Council.
- **Empty/absent seam:** a card whose seam was never seen reads **"— not loaded"** (distinct from stale), so a
  missing sensor is obvious and not confused with a quiet one.

## 6. Persistence
A `Sentinel\Cockpit.conf` (NOT NinjaScriptProperties — this is an AddOn window, no codegen concern): collapsed
sections, pin state, dock/float + float geometry, per-card visibility, selected instrument / follow-chart flag,
theme-follow. Same pattern as the Deck's float geometry + the Dashboard.

## 7. Build plan (small first F5, testable in stages)
- **Phase 1 — the core question.** WPF window + **① DECISION** (Council + the why-line) + **② GATE** (governor/kill/feed).
  Float + pin. This alone answers "is my brain alive & am I cleared." ~one AddOn file, reuses `SentinelSkin.K*` +
  `SentinelCore.Get*State`. No changes to any existing tool → zero risk to the running test when we do F5.
- **Phase 2 — full readout.** ③ CONTEXT + ④ VOTERS (all remaining seams), age dots, per-card show/hide, collapse
  persistence.
- **Phase 3 — dock + follow.** ChartTrader dock mode; follow-active-chart instrument; optional "retire redundant
  on-chart cards" convenience (bulk-toggle the indicators' `ShowCard`).

## 8. Naming
Window/AddOn `SentinelCockpit` · display **"Sentinel Cockpit"** · base AddOn namespace (a window, not an indicator or
strategy — no picker-folder concern). Follows the federated naming law (Sentinel <Thing>).

---

*Companion visual mockup published as an artifact this session — approve the look before Phase 1.*
