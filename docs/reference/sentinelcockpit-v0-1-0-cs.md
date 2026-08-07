---
layout: sentinel-ref
title: "SentinelCockpit_v0_1_0.cs"
blurb: "AddOns / runtime · 0.1.0 · 1734 lines"
---

# SentinelCockpit_v0_1_0.cs

> `bin/Custom/AddOns/SentinelCockpit_v0_1_0.cs`

| | |
|---|---|
| **Family** | AddOns / runtime |
| **Version** | 0.1.0 |
| **Size** | 1734 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelCockpitAddOn` |
| **Namespace** | `NinjaTrader.NinjaScript.AddOns.Sentinel` |
| **Consumes seams** | `AdxState`, `BrickState`, `CciState`, `ClockState`, `CompressionState`, `CouncilState`, `EnvelopeState`, `GodReversalState`, `IntermarketState`, `LevelState`, `LiquidityState`, `MtfState`, `ParticipationState`, `TrendState`, `WaeState` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
one at https://mozilla.org/MPL/2.0/.

Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
─────────────────────────────────────────────────────────────────────────────
═════════════════════════════════════════════════════════════════════════════
 SentinelCockpit — the Sentinel Suite command surface (NT8 AddOn window)
 File: SentinelCockpit_v0_1_0.cs   ·   Version v0.5.0   ·   namespace …AddOns.Sentinel
 (AddOn windows bump IN-PLACE — file/class identity is not chart-serialized. Same precedent as
  SentinelDashboard_v1_0_0.cs @ v1.1.9 and SentinelCore_v1_0_0.cs @ v1.14.0.)
─────────────────────────────────────────────────────────────────────────────
 WHAT THIS IS  (spec: Docs/SENTINEL_COCKPIT_SPEC.md · memory: sentinel-backlog "THE SENTINEL COCKPIT")
   ONE dockable/floatable, always-on-top rail that RE-READS the published SentinelCore …State seams so the
   operator never opens the Indicator dialog or greps the log to answer "is my brain alive, and why isn't it
   trading?". Opens from Control Center ▸ New ▸ "Sentinel Cockpit". It's a plain WPF NTWindow (like the
   Dashboard) — so float + always-on-top pin come free — and it OWNS no service and consults no chart render
   target: it only reads SentinelCore.Get…State. The on-chart SharpDX cards are untouched (this is a parallel,
   opt-in readout). Theme follows the active skin via SentinelSkin.K* brushes (rebuilt at window open).

 ARCHITECTURE (why WPF-reads-the-seams, not a SharpDX rail): every sensor already publishes a …State seam, so
   the Cockpit is just another consumer — the same one the Council is. Dock/undock/float/pin are native to WPF
   windows (proven by the Deck). See spec §2.

 PHASE 1 + 2 (this file): ① DECISION (CouncilState + the computed "why no trade" line) · ② GATE (kill-switch +
   per-account Governor) · ③ CONTEXT (the modulators) · ④ VOTERS (the confluence, unburied). Float + pin +
   instrument picker + theme + collapsible sections. NO change to any existing tool and NO new SentinelCore seam
   (this only READS Get…State) → the F5 that compiles it is safe for a running test.
   Phase 3 (next) = ChartTrader dock + follow-active-chart. (Spec §7.)

 THE THREE SEAM STATES (the honesty rule this window exists to enforce):
   FRESH   — seen within its StaleSec           → green dot, live values
   STALE   — seen, but not recently             → amber dot + "Ns" (the dry-up flicker: it is QUIET, not GONE)
   ABSENT  — never published this session       → faint dot + "— not loaded" (the sensor isn't on the chart)
   Conflating STALE with ABSENT is exactly the bug that made the Council look dead while it was running.

 CHANGELOG
   v0.5.0 (2026-07-15) — ⑤ HELM · INTERDICT (Phase 5; needs SentinelCore ≥ v1.34.0; memory helm-interdiction-layer).
            A new monitor-rail section that lets the operator GRAB THE WHEEL of a running actor without stopping it:
            it reads AllHelmStates() → shows the interdictable actor for the picked instrument (instanceKey · status ·
            position · live stop/target · paused/override · freshness dot) and PUBLISHES HelmIntents via
            SentinelCore.SetHelmIntent — Pause/Resume/Skip/Flatten/Breakeven, MoveStop/MoveTarget (type a price →
            Stop→/Tgt→), Scale-down (reduce N), TakeOver/HandBack. The Bridge (v0.3.0, 'Obey Helm' on) executes each
            with its OWN order handles — this surface never touches an order (risk-adding verbs pass the Bridge's
            GateEntry). Persistent controls (not rebuilt each tick, so a typed price survives). The Cockpit was
            already a writer (BUILD writes Roster.conf); this writes INTENTS. Reads/publishes seams only, no order
            path → the F5 is test-safe.
   v0.4.0 (2026-07-14) — PER-LANE AUTHORING (System Builder spec §14; needs SentinelCore ≥ v1.33.0). BUILD mode gains
            a "lane" field: the roster + a new PROFILE editor target Models\<inst>\<bartag>@<lane>\. (1) Roster.conf
            is written to the LANE folder (RosterIO write; a laned READ inherits the bar-type baseline until you Save
            = fork). (2) A Lane.conf PROFILE editor (floor + deadband first-class, plus a raw key=value box for the
            consult toggles / modulator damps) writes the fusion-knob overrides beside the roster (LaneIO); blank =
            inherit F6. Selecting a laned scope auto-fills the lane field; Save writes both files → two same-bartype
            A/B charts run different SYSTEMS on identical bars, authored from the GUI. Applies on the Council's reload.
   v0.3.0 (2026-07-12) — ⑤ BUILD MODE (System Builder Phase 1; needs SentinelCore ≥ v1.27.0 for VoterCatalog +
            RosterIO). A header "BUILD" toggle swaps the body from the monitor rail to a per-scope ROSTER EDITOR:
            the 14 catalog voters as rows (include ✓ · weight · state/trigger · live-seam dot), seeded from the
            scope's Roster.conf (via RosterIO.Read) or the Council's default declaration when no file exists. Live
            preview recomputes declaredW + stateW (the quiet-bar denominator) and predicts RosterComplete against
            the live Council's roster mask (who is actually speaking) as you edit. Save writes Roster.conf
            atomically (RosterIO.Write) — applies on the Council's next reload (hot-reload is a later phase).
            This is the WRITE-SIDE twin of the Decision readout: the same RosterComplete the hero card shows is
            what the editor predicts. Spec: Docs/SENTINEL_SYSTEM_BUILDER_SPEC.md. Reads/writes config only — no
            new seam, no order path → the F5 is test-safe.
   v0.2.2 (2026-07-09) — ROSTER LINE (exec plan 3.1; needs SentinelCore ≥ v1.16.0). Renders the Council's
            declared-vs-actual roster under the tally — `Roster 8/10 — EYE, BRK missing`, amber when incomplete
            or when an undeclared sensor spoke. Given its OWN row rather than a why-line rung: an incomplete
            roster degrades TRUST without BLOCKING, so folding it into the kill ▸ governor ▸ veto ▸ stale ▸ floor
            chain would mask a real blocker beneath it. The one concession — a READY verdict fused from a partial
            declaration reads "READY (roster 8/10)" in amber, because that is not the verdict the model describes.
   v0.2.1 (2026-07-09) — SCOPE PICKER. The picker seeded BARE instrument names from WatchedInstruments alongside
            the scoped ones, and Cockpit.conf still held a bare "GC" from before scope keys. With two GC charts
            live, GetCouncilState("GC") fails CLOSED (logging AMBIGUOUS SCOPE) — so the hero card read "waiting
            for Council" while the Council was in fact publishing two healthy verdicts. Now: a bare name is only
            offered for instruments with NO scope yet (and pruned once a scope appears); a bare selection that
            resolves to exactly one scope is silently upgraded to it; a bare selection with several scopes renders
            an explicit "on N charts — pick a scope" list instead of a false absence. Patched in place (v0.2.0
            never froze). Reads seams only — SentinelCore stays v1.15.0.
   v0.2.0 (2026-07-08) — PHASE 2. New ③ CONTEXT section (Clock · Participation · Location · MTF · Intermarket —
            the modulators, i.e. why conviction is damped) and ④ VOTERS section (Eye · Trend · CCI · ADX ·
            VolEnvelope · Brick · Compression · WAE · GodReversal, each with dir arrow, strength detail, and an
            agree/dissent mark vs the Council bias; plus a Liquidity-walls VETO row). AGE DOTS everywhere via a
            single fresh/stale/absent classifier. Sections ②③④ collapse on header click (hero ① never does) and
            the collapsed set persists. New "⊘" header toggle hides ABSENT seams (declutter). Cockpit.conf gains
            hideAbsent= + collapsed=. Collapsed sections skip their rebuild.
            NOTE: the spec's full per-seam settings sheet is DEFERRED — the hide-absent toggle covers the real
            decluttering need at a fraction of the UI surface.
   v0.1.0 (2026-07-08) — initial: window + registration; Decision (Council verdict + why-line: kill ▸ governor ▸
            veto ▸ stale ▸ floor ▸ size ▸ edge) + Gate (kill + governor cards); instrument picker (from
            AllCouncilStates); pin=Topmost; Cockpit.conf persistence (instrument + pin); K* theme.
```

