---
layout: sentinel-ref
title: "SentinelCopierService_v0_1_0.cs"
blurb: "AddOns / runtime · 0.1.0 · 875 lines"
---

# SentinelCopierService_v0_1_0.cs

> `bin/Custom/AddOns/SentinelCopierService_v0_1_0.cs`

| | |
|---|---|
| **Family** | AddOns / runtime |
| **Version** | 0.1.0 |
| **Size** | 875 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `InstrumentMapEntry` |
| **Namespace** | `NinjaTrader.NinjaScript.AddOns.SentinelCopier` |
| **Consumes seams** | `GovernorState` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
one at https://mozilla.org/MPL/2.0/.

Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
─────────────────────────────────────────────────────────────────────────────
═════════════════════════════════════════════════════════════════════════════
 SentinelCopierService — headless multi-account trade copier (NT8 AddOn)
 File: SentinelCopierService_v0_1_0.cs
 Service version: v0.1.0   (SKELETON — compiles + structured; live paths marked VALIDATE)
─────────────────────────────────────────────────────────────────────────────
 WHAT THIS IS  (part of the "Sentinel Suite"; see Docs/ROADMAP.md)
   A headless, always-on AddOn service — SAME architecture as MAECaptureService:
   an AddOnBase singleton that lives for the NT process, exposes a static Instance
   so a future dashboard/settings window ATTACHES to it, and never depends on any
   window being open. Closing a UI must never stop copying.

 WHAT IT DOES  (pure FILL-MIRROR copier — the "Horde" model)
   • Subscribes to the LEADER account's ExecutionUpdate.
   • On each LEADER fill (Filled/PartialFilled), for every enabled+connected FOLLOWER:
       - size  = leaderFillQty × instrumentSizeRatio × followerMultiplier
       - symbol = per-follower instrument map (e.g. GC→MGC), else same instrument
       - submits a MARKET order on the follower account (unmanaged, account-level).
   • Pure fill-mirror: it never reads the leader strategy's internal state. Entry AND
     exit both arrive as fills, so each follower's NET position stays synced with the
     leader automatically. Works with GTrader21, manual ChartTrader, or any strategy.

 CREDIT / ATTRIBUTION  (required before any open-source release)
   The fill-mirror engine (subscribe to master ExecutionUpdate → qty×multiplier → market
   CreateOrder+Submit, Guid order name) is ADAPTED from the TickHunter NT "Horde" copier by
   **Frosty** (TradeLikeAZombie community). Even the "SentCopy_" order prefix echoes their
   "TH-Horde_". Sentinel ADDS: same-provider prop rule, bidirectional GC↔MGC / CL↔MCL map,
   SentinelCore kill-switch + feed-health gating, Eye-gate, and manual-assist mode. Thanks, Frosty.

 LEADER MODEL (decided — see memory copier-samples-analysis)
   The leader is a SIGNAL account (a dedicated SIM or small live acct trading native GC).
   EVERY real account — including what the user calls "primary" — is an execution TARGET
   with its own instrument map (GC→MGC) + size. This satisfies "primary cross-trades"
   AND "SIM leader → prop accounts" with ZERO changes to GTrader21's order path.

 SUITE CONTRACTS (how this ties into the rest of the Sentinel Suite)
   • ORDER-NAME PREFIX  = "SentCopy_"  → distinguishes copier orders from strategy orders
     (GTrader21 uses "GTS_"; keep prefixes disjoint so tools never edit each other's orders).
   • SHARED KILL-SWITCH = SentinelCore.KillSwitchEngaged (in SentinelCore_v1_0_0.cs). Any
     suite tool (a risk monitor, the panel's lockout, the dashboard) flips it to halt ALL
     mirroring. The copier no longer owns its own flag — it consults the suite Core.
   • MIRROR GATE        = SentinelCore.CanAct() + per-follower connection health, the single
     choke point every mirror passes through. Core's feed-health probe (once a health tool
     registers one — e.g. the GTrader21 v0.1.2 lag metric) gates each account automatically.

 VERIFIED API NOTES (from in-repo usage — AlightenButtonPanelv3, MAECaptureService)
   • AddOnBase auto-starts as a singleton; OnStateChange fires SetDefaults/Active/Terminated.
     GOTCHA: base ctor calls OnStateChange BEFORE the subclass ctor → init lazily in Start(),
     never rely on field initializers for mutable state.
   • Order submit (account-level, no strategy):
       acct.CreateOrder(instr, OrderAction, OrderType, OrderEntry.Automated, TimeInForce,
                        qty, limitPx, stopPx, ocoId, name, Core.Globals.MaxDate, null);
       acct.Submit(new[]{ order });   // wrap in try/catch
   • Instrument.GetInstrument(fullName) resolves a contract; returns null if not found.
   • Execution carries the AUTHORITATIVE fill: e.Execution.{Account,Instrument,Price,
     Quantity,ExecutionId,Order}. e.Execution.Order.{OrderState,OrderAction}.
   • MUST unsubscribe every account event in Terminated (leak / dangling handler otherwise).

 VERIFIED AGAINST IN-REPO USAGE (2026-07-01 — these compile, no longer VALIDATE):
   • Account.Connection.Status == ConnectionStatus.Connected — Account.Connection is real
     (@ProfitLoss.cs:89 position.Account.Connection); Connection.Status (@BarTimer.cs:50).
   • Connection.Options.Name for provider identity — AutoReconnect.cs:247 (c.Options.Name).

 STILL TO VALIDATE ON A SIM RUN (marked VALIDATE: inline — real behavior, not compile)
   • Cross-instrument contract resolution for GC→MGC: the same-expiry heuristic assumes MGC
     shares GC's month code, which may NOT hold — the one item that genuinely needs a live check.
   • Account.Positions iteration for entry-vs-exit action (Positions exists; confirm it's fresh
     enough at mirror time — a zero-crossing fill still needs an order split, deferred).
   • Provider grouping semantics: Options.Name is the CONNECTION name (firm-level, correct);
     note the Provider ENUM would group by tech-provider (too coarse — two Rithmic firms collide).
   • Whether ExecutionUpdate can re-fire the same ExecutionId (dedupe guard added defensively).

 CHANGELOG
   (in-place, 2026-07-25) — RECORDED CATCHES: 5 empty `catch {}` -> SentinelCore.Swallow (Core >= v1.41.0).
            Behaviour identical; a swallowed fault on the mirror path is now counted and logged.
   v0.1.0h — (in-place) COPY-SLIPPAGE capture: also subscribe enabled AUTO followers' ExecutionUpdate
            (OnFollowerExecution) to log each mirror FILL to SentinelCore.Ledger.Fill with intended =
            the LEADER fill price we replicated (correlated by mirror order name at submit) vs the
            follower's actual fill → adverse slip ticks in the dashboard Slippage view (how faithfully
            followers track the signal — a real prop-copy quality metric). Same-symbol + GC→MGC/ES→MES
            share a price scale (meaningful); exotic cross-maps would not (rare). CAPTURE ONLY — never
            acts; bounded dedupe + correlation map; follower subs torn down in Unsubscribe/Stop.
   v0.1.0g — (in-place) the flat-follower entry gate now ALSO honors the account profile's SESSION
            window (SentinelCore.InAccountSession) — a flat follower opens no new trades outside its
            session; open positions still mirror (manage/close). Composes with the governor gate.
   v0.1.0f — (in-place) CONSISTENCY GOVERNOR: a follower that hit its daily cap/loss (SentinelCore
            governor, hosted by Risk) opens NO new trades today — MirrorToFollower skips a mirror when
            the follower is FLAT and !TradingAllowedToday. Non-flat followers always mirror (manage/
            close), so the governor never traps a live position. Per-follower, per the spec.
   v0.1.0e — (in-place) SCOPED KILL: CanMirror now gates via SentinelCore.CanActInstrument(leader
            instrument) instead of CanAct, so a per-instrument kill (Risk on a lagging GC feed)
            halts only GC mirrors — ES/NQ keep copying. Global kill-switch still halts everything.
   v0.1.0d — (in-place) ATTRIBUTION: added explicit CREDIT to Frosty / TickHunter "Horde" (see
            the CREDIT block above). Comment-only — no behavior change. Required before any
            open-source release; previously the header only nodded at "the 'Horde' model".
   v0.1.0c — (in-place) MANUAL-ASSIST mode. A follower can be 'manual' (follower=<label>|manual|…):
            instead of auto-submitting, the Copier PUBLISHES a place-by-hand ticket to
            SentinelCore's assist registry (dashboard Assist tab + state.json). Same map/size/
            Eye-gate pipeline; mirrors the leader's exact action; the account name is just a label
            (need not be an NT account). For prop firms that bar automated copy-trading (TPT eval/
            PRO, Bulenox) — decision-support instead of auto-execution. Auto path unchanged.
   v0.1.0b — (in-place) Eye-gate: when UseEyeGate, mirror only ENTRIES SentinelEye qualifies
            (exits always mirror). eyeGate=on/off in Copy.conf.
   v0.1.0 — initial SKELETON. Headless AddOnBase singleton; leader ExecutionUpdate
            subscription; fill-mirror engine; per-follower instrument map + size ratio +
            multiplier; same-provider policy (Off/Warn/Block) with SIM-leader exemption;
            shared kill-switch + mirror gate; "SentCopy_" order prefix. Config is EMPTY by
            default (service is inert until a leader+followers are set) — safe to install.
            Live order paths marked VALIDATE; dashboard + JSON config load are follow-ups.
   v0.1.0a — (in-place, pre-freeze) consume SentinelCore: kill-switch + feed-health gate now
            live in the shared Core (SentinelCore_v1_0_0.cs) instead of a copier-local flag;
            logging routed through SentinelCore.Log("Copy", …). No behavior change to mirroring.
   v0.1.0b — (in-place, pre-freeze) CONFIG PERSISTENCE: SaveConfig()/LoadConfig() to a simple
            text file <UserDataDir>\Sentinel\Copy.conf (leader/policy/follower lines, follower
            map reuses the "GC>MGC*10" DSL). Start() auto-loads it → the copier survives NT
            recompiles/restarts instead of resetting to inert. Dashboard Apply saves it.
```

