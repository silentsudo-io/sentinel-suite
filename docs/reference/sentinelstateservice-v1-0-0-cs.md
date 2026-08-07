# SentinelStateService_v1_0_0.cs

> `bin/Custom/AddOns/SentinelStateService_v1_0_0.cs`

| | |
|---|---|
| **Family** | AddOns / runtime |
| **Version** | 1.0.0 |
| **Size** | 579 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelStateService_v1_0_0` |
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
 SentinelStateService — periodic state snapshot for the Sentinel Suite (NT8)
 File: SentinelStateService_v1_0_0.cs
 Version: v1.0.0
─────────────────────────────────────────────────────────────────────────────
 WHAT THIS IS  (see memory: ninjatrader-observability, sentinel-suite-architecture)
   A headless, always-on AddOnBase service that writes a live snapshot of NinjaTrader
   state to a file every couple seconds:
       <UserDataDir>\Sentinel\state.json
   So the current picture (accounts / positions / working orders / P&L / kill-switch /
   copier config) is READABLE without a screenshot — the Positions/Orders/Accounts panels
   become one file. Complements NT's own log\log.*.txt (event stream) and Sentinel\sentinel.log
   (our Output text). Written atomically (tmp → move) so a reader never sees a half file.

 KEEPS THE FILE SMALL among 169 accounts: only accounts WITH a position or a working order
 are detailed; the rest are summarized as counts (total / connected).

 VERIFIED APIs (in-repo): acct.Get(AccountItem.Realized/UnrealizedProfitLoss, Currency.UsDollar)
   (GTrader21v_0_0_4Panel.cs:2738), position.GetUnrealizedProfitLoss(PerformanceUnit.Currency)
   (@UnrealizedProfitLoss.cs:73), Account.Connection.Status/.Options.Name, Account.All.

 CHANGELOG
   v1.0.7 — added the "profiles" block (per-account profile: firm/ratio/target/cap/dailyLoss/size/
            contracts/session from SentinelCore's AccountProfile registry) — readable outside NT.
   v1.0.6 — added the "governor" block (per-account daily cap/loss state from SentinelCore) and a
            THROTTLED "eyeReferee" block (per-signal Eye verdict +1/-1/0 from SentinelExcursions,
            recomputed every 5 min since it parses the excursion files — empty until Eye accrues).
   v1.0.5 — added the "configs" block (AppendConfigs): which running GTrader21 instance auto-read
            which lab .conf (strategy/instrument/account/config/tp/sl/ageSec) from SentinelCore's
            config-use registry. Also surfaced the risk block's per-instrument SCOPED kills
            ("instrumentKills") — so "GC halted, ES/NQ fine" is readable outside NT.
   v1.0.4 — added the "arc" block (AppendArc): SentinelArc fleet plan + live supervision (leader,
            per-slot instrument/strategy/enabled/inSession/session/health/posQty/dayPnl/fillsToday/
            lastSignalAgeSec) from SentinelCore's fleet registry. Fleet status now readable in
            state.json, not just sentinel.log heartbeats.
   v1.0.3 — added the "eye" block: per-instrument SentinelEye GodTrades qualification verdicts
            (instrument/direction/score/source/ageSec) from SentinelCore's Eye registry.
   v1.0.2 — teardown hardening: `_stopping` flag set FIRST in Stop() so the 2s timer callback
            bails instantly during NT recompile/teardown (was a plausible compile-hang cause —
            a threadpool callback touching Account.All while NT disposes AddOns). Timer now
            DRAINED on dispose (bounded 500ms wait). No functional change to the snapshot.
   v1.0.1 — added the "risk" block (AppendRisk): Sentinel Risk feed lag/stall, connections, and
            kill state now surface in state.json (readable, not just the Risk tab).
   v1.0.0 — initial: 2s timer snapshot of kill-switch, copier config, account summary, and
            per-active-account positions/orders + P&L. Manual JSON (no serializer dep). Atomic
            write. Reads account collections defensively (try/catch; glance snapshot, not audit).
```

