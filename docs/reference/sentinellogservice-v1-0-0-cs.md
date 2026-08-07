---
layout: sentinel-ref
title: "SentinelLogService_v1_0_0.cs"
blurb: "AddOns / runtime · 1.0.0 · 522 lines"
---

# SentinelLogService_v1_0_0.cs

> `bin/Custom/AddOns/SentinelLogService_v1_0_0.cs`

| | |
|---|---|
| **Family** | AddOns / runtime |
| **Version** | 1.0.0 |
| **Size** | 522 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelLogService` |
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
 SentinelLogService — headless tier-1 zero-touch MAE/MFE capture (NT8 AddOn)
 File: SentinelLogService_v1_0_0.cs
 Service version: v1.0.0   (pairs with SentinelLogEngine schema 1.0)
─────────────────────────────────────────────────────────────────────────────
 WHAT THIS IS
   A headless, always-on AddOn service. It runs as a singleton for the lifetime of
   the NinjaTrader process (AddOnBase auto-starts at platform launch / recompile).
   NO WINDOW — the future dashboard window attaches to this service; closing a window
   must never stop logging. (Architecture decision: service runs always.)

 WHAT IT DOES (tier-1, zero-touch — spec §2)
   For accounts/strategies the trader has enabled for logging, it logs MAE/MFE WITHOUT
   any change to those strategies:
     • Subscribes to every Account's PositionUpdate (and ExecutionUpdate for fills).
     • On a position going flat -> open, opens an SentinelLogEngine record (tier 1, no ctx) and
       starts a raw-tick MarketData subscription for that instrument.
     • Each incoming tick updates running raw MAE/MFE against the entry price.
     • On open -> flat, finalizes the engine record (one JSONL line) and releases the
       market-data subscription.

 WHY RAW TICKS (verified, spec §11.2)
   BarsRequest data "cannot be used as input for an indicator" and isn't synchronized to
   a strategy's series, and subfolders/bar-type matching is brittle. So tier-1 measures
   RAW-TICK high/low excursion against entry — universal, faithful, no bar-type coupling.
   (Tier-2 strategies still supply bar/HA excursion + ctx from inside themselves.)

 VERIFIED API NOTES (NT8 help guide + forum; see spec §11)
   • AddOnBase auto-starts as a singleton; OnStateChange fires for Active/Terminated.
     GOTCHA: the base constructor calls OnStateChange BEFORE the subclass constructor —
     so we DO NOT rely on field initializers; we lazily init inside OnStateChange.
   • Position is authoritative only inside PositionUpdate (Account.Positions can be stale
     inside ExecutionUpdate). Event order (exec vs position) is NOT guaranteed.
   • Flat is signaled by Operation.Remove on the position update.
   • MarketData (Level-1) updates arrive on a BACKGROUND thread — this service does no UI,
     so no Dispatcher needed here; a future dashboard marshals to its own UI thread.
   • MUST unsubscribe all events + dispose all MarketData in Terminated (leak otherwise).

 STILL TO VALIDATE ON FIRST RUN (commented inline as VALIDATE:)
   • Exact MarketData class surface / event arg property names in this NT8 build.
   • Account.Name availability + which accounts to enumerate at Active.
   • That entry price from PositionUpdate.AveragePrice is the right basis vs first exec.

 CHANGELOG
   sentinel-rebrand (2026-07-01) — MAECaptureService → SentinelLogService; namespace
            MAELogging → Sentinel (now part of the Sentinel Suite). Logging routes through
            SentinelCore.Log (Output window + Sentinel\sentinel.log). Uses SentinelLogEngine.
            The old MAE* files were ARCHIVED out of bin\Custom; the monitor is now the Suite's
            "Log" tab (standalone MAEDashboard window removed). No capture-logic change.
   v1.0.4 — unified open-position registry (dashboard shows BOTH tiers).
     - Subscribes SentinelLogEngine.OnEngineTradeOpened/Closed so tier-2 strategies that log
       themselves now appear in GetOpenSnapshots() alongside tier-1 captures. The empty-
       dashboard-while-tier2-strategies-run gap is closed. OpenSnapshot gains Tier +
       Strategy so the dashboard can distinguish/zero-touch vs rich. Reference-keyed
       registry; unsubscribed on Stop.
   v1.0.3 — dashboard support surface (no logging-path changes).
     - Added read-only live-state API for the dashboard window: GetOpenSnapshots()
       returns a snapshot list of currently-open tracked positions (account, instrument,
       dir, entry, running MAE/MFE ticks, last price). TradeClosed event fires on each
       close so the window can refresh. Track now mirrors running MAE/MFE + last price
       for display (engine still owns the authoritative logged values).
   v1.0.2 — FILL-PRICE FIX (first live-data findings).
     - BUG: every tier-1 trade logged entryPx == exitPx, pnlTicks == 0. Root cause:
       PositionUpdate.AveragePrice reports the ENTRY price even on the closing/flat
       update (verified NT8 behavior), so using it for the exit was always wrong.
     - FIX: source fill prices from EXECUTIONS (e.Execution.Price), which are
       authoritative. OnExecutionUpdate now records the last fill per account+instrument;
       the position handler uses that for exit (and entry, falling back to position avg
       only if no execution seen yet — safe because on the OPEN update the position avg
       IS the entry price; the bug is exit-only).
     - Clear last-fill on flat so it cannot leak into the next trade on that instrument.
     - NOTE: the large entry-to-first-tick gap also seen in early data should shrink now
       that entry uses the true fill; VALIDATE on next run that MAE/MFE look sane.
   v1.0.1 — compile fix + teardown hardening (first-compile findings).
     - MarketData has NO Dispose() (unlike BarsRequest); release is just
       `Update -= handler`. Removed the erroneous Dispose() call (CS1061).
     - Guard unsubscribe behind an MdSubscribed flag: unsubscribing a MarketData
       Update event that was never subscribed throws an NRE in remove_Update
       (documented NT8 behavior). Now we only detach if attach succeeded.
   v1.0.0 — initial headless tier-1 capture service. Account subscription + raw-tick
            excursion + SentinelLogEngine feed. No UI. Logging enabled per-account via the
            EnabledAccounts set (default: log all sim accounts; live opt-in).
```

