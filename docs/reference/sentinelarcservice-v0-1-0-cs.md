---
layout: sentinel-ref
title: "SentinelArcService_v0_1_0.cs"
blurb: "AddOns / runtime · 0.1.0 · 462 lines"
---

# SentinelArcService_v0_1_0.cs

> `bin/Custom/AddOns/SentinelArcService_v0_1_0.cs`

| | |
|---|---|
| **Family** | AddOns / runtime |
| **Version** | 0.1.0 |
| **Size** | 462 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelArcService_v0_1_0` |
| **Namespace** | `NinjaTrader.NinjaScript.AddOns.Sentinel` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
one at https://mozilla.org/MPL/2.0/.

Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
─────────────────────────────────────────────────────────────────────────────
═════════════════════════════════════════════════════════════════════════════
 SentinelArcService — fleet orchestration for the Sentinel Suite (NT8)
 File: SentinelArcService_v0_1_0.cs
 Version: v0.1.0
─────────────────────────────────────────────────────────────────────────────
 WHAT THIS IS  (see memory: sentinel-suite-architecture, profit-plan-and-accounts)
   The suite's ORCHESTRATION tool — the manager of the leader "signal engine" fleet.
   A NinjaScript AddOn CANNOT start/stop a chart strategy (same platform limit that
   made Eye a chart indicator). So Arc orchestrates the Sentinel way — publish/consult:

     • Arc PUBLISHES a per-instrument FLEET PLAN to SentinelCore (enable, size, session
       window) from Arc.conf / the dashboard.
     • Sentinel-aware STRATEGIES (GTrader21, once wired in v0.2.0) CONSULT SlotLive() at
       entry time and only trade when their slot is live. You load the strategy on each
       chart ONCE; Arc controls which instruments trade, and when, from one place.
     • Arc also SUPERVISES the leader: per-slot position, day PnL, fills-today, last
       signal, and a health verdict (OFF / CLOSED / IDLE / LIVE / DARK). It's the
       watchdog for the TOP of the funnel (Risk watches feeds; Copy fans out).

   This v0.1.0 does the PLAN + SUPERVISION halves (fully testable headless via
   sentinel.log). The CONTROL half lands when GTrader21 consults SlotLive() (v0.2.0) —
   until then the plan is published + honored by any strategy that opts in, and Arc
   reports the fleet's live status regardless.

 ARC.CONF (in <UserDataDir>\Sentinel\, hand-editable):
   leader=Sim101
   slot=GC|GTrader21|on|1|24h
   slot=NQ|GTrader21|off|1|0830-1500          (session HHMM-HHMM in NT clock time; 24h = always)

 VERIFIED APIs: Account.All / Account.Name / Account.Connection.Status; Account.Positions +
   Position.{MarketPosition,Quantity,Instrument}; Position.GetUnrealizedProfitLoss(Currency);
   Account.ExecutionUpdate += (ExecutionEventArgs e) => e.Execution.{Instrument,Time} (copier).

 CHANGELOG
   v0.1.0 — initial: Arc.conf fleet plan → SentinelCore fleet registry (publish); leader
            ExecutionUpdate subscription for fills-today/last-signal; 3s supervision tick
            computing InSession + position + unrealized + health per slot; logs on health
            change + a 30s heartbeat. Headless singleton. GTrader21 consult-gate = v0.2.0.
```

