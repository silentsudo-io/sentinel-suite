# SentinelRiskService_v1_0_0.cs

> `bin/Custom/AddOns/SentinelRiskService_v1_0_0.cs`

| | |
|---|---|
| **Family** | AddOns / runtime |
| **Version** | 1.0.0 |
| **Size** | 1263 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelRiskService_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.AddOns.Sentinel` |
| **Publishes seams** | `DrawdownState`, `GovernorState` |
| **Documented by** | _no doc tracks this artifact_ |
| **Depends on this** | [SentinelCopierService_v0_1_0.cs](sentinelcopierservice-v0-1-0-cs.md), [SentinelDeck_v0_2_6.cs](sentineldeck-v0-2-6-cs.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
one at https://mozilla.org/MPL/2.0/.

Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
─────────────────────────────────────────────────────────────────────────────
═════════════════════════════════════════════════════════════════════════════
 SentinelRiskService — feed-health / lag watchdog for the Sentinel Suite (NT8)
 File: SentinelRiskService_v1_0_0.cs
 Version: v1.0.11
─────────────────────────────────────────────────────────────────────────────
 WHAT THIS IS  (see memory: sentinel-suite-architecture, ninjatrader-observability)
   A headless, always-on AddOnBase service — the SAFETY tool of the suite. It watches
   the DATA FEED for the instruments you're actively trading and your connection health,
   and when a feed lags or stalls it AUTO-ENGAGES the shared SentinelCore kill-switch —
   which in turn halts the Copier (CanMirror consults SentinelCore.KillSwitchEngaged).
   It also registers SentinelCore.FeedHealthProbe so any tool can gate per-account.

 WHY  — the user hit real OVERNIGHT DATA LAG degrading feeds. This generalizes the
   GTrader21 v0.1.2 lag metric into a suite-wide, screen-free watchdog.

 HOW IT MEASURES (verified in-repo)
   Lag  = (Core.Globals.Now - e.Time).TotalSeconds        — GTrader21v_0_1_3Panel.cs:749
          (how far behind wall-clock the latest tick's own timestamp is)
   Stall= (Core.Globals.Now - lastTickWallClock)          — no tick received for N seconds
   Feeds: new MarketData(instr) + .Update += handler; release via .Update -= (no Dispose).
   Auto-subscribes to instruments that currently have an OPEN POSITION on any account
   (polled every 2s); drops the subscription when the position closes.

 AUTO-KILL (default ON): lag > MaxLagSeconds OR stall > MaxStallSeconds on ANY monitored
   feed → SentinelCore.SetKillSwitch(true). When feeds recover (and AutoReleaseWhenHealthy),
   it releases the switch. It only releases a kill IT engaged (won't undo a manual/other kill).
   NOTE (v1): during a live breach it will re-assert the kill even if a human clears it —
   safety-first; refine later if that's too aggressive.

 VERIFIED APIs: MarketDataEventArgs.{Time,Price,MarketDataType} (GTrader21 + archived MAE),
   Connection.ConnectionStatusUpdate + ConnectionStatusEventArgs.{Status,Connection}
   (AutoReconnect.cs), Account.Connection.Status, Account.Positions.

 CHANGELOG
   v1.0.11 (2026-07-09) — THE NAKED-POSITION ALERT WAS CRYING WOLF. `ReconcileAccount` counted a stop as present
            only in Working|Accepted|PartFilled. NT transits an order through ChangePending/ChangeSubmitted on
            every modify, and GTrader21 TRAILS its stop — so at each trail step the stop left that set, this
            2-second scan saw a naked position, and fired a CRITICAL. **160 false NAKED POSITION alerts** sit in
            the Ledger (74 on 07-05 alone), same account + instrument, 20-60 s apart. The system's most severe
            alert was mostly noise, so a REAL naked position would have been invisible inside it.
              • `IsLive(OrderState)` now counts the pending-modify states: a stop mid-trail-move is still a stop.
              • naked + orphan are CONDITION ALERTS through `SentinelCore.Conditions` (≥ v1.17.0): 10 s debounce,
                re-stated every 300 s / 900 s while true, auto-cleared on resolve. The alert now says how long the
                position has been unprotected.
              • the ORPHAN latch was the mirror bug — the naked-flag sweep deleted `<acct>|ORPHAN` on EVERY scan
                (it starts with the account prefix but is never an instrument name), so it re-alerted every 2 s.
                They shared one HashSet and got opposite bugs from it. Now separate, distinct keys.
              • a flat instrument's episode is explicitly Cleared — otherwise it lingers "already reported" and
                suppresses the NEXT genuine naked position for a whole cooldown.
              • naked/orphan are NO LONGER reset at the day roll: a position naked across the roll is still naked.
                Only the ACTION latches (_hardFlattened/_ddFlattened) and transition memory reset there.
            + every Risk alert now passes its ACCOUNT (`Alerts.Critical(title, detail, account)`) — all 164
              ALERT-CRIT Ledger rows previously recorded `acct:""`. (Execution-plan step 2.3.)
   v1.0.10 — ALWAYS-MONITORED ROLLOVER ROOTS: RolloverWatchRoots (default "ES,GC,SI,CL") are resolved to their
            front-month instrument and folded into the rollover countdown even when nothing is held/charted, so
            the dashboard Risk-tab rollover list always shows the key contracts. Resolved once + cached
            (Instrument.GetInstrument hits the instrument DB); fail-safe (a root that won't resolve is skipped).
   v1.0.9 — NEWS-CALENDAR FRESHNESS GUARD (event-veto safety): a stale/missing Sentinel\News.conf means
            today's high-impact windows aren't loaded → the news veto silently fails OPEN (you'd trade
            through FOMC/NFP unprotected). CheckNewsFreshness now WARNS (Log + Alert, fail-to-caution,
            throttled 6h) when News.conf is missing or older than ~26h; clears when refreshed. Does NOT
            block. Closes the critical freshness gap in the EconomicCalendar.py → sentinel_newsconf.py →
            News.conf → SetNewsLockouts → Council veto pipeline (economic-calendar-event-veto memory).
   v1.0.8 — PERSIST the governor daily-P&L baseline (SentinelCore.State, keyed by account + trading-day)
            so a mid-day F5/restart no longer zeroes the day's realized P&L; a new trading day recaptures.
            (Pairs with SentinelDashboard v1.1.6 showing open/unrealized P&L on the Accounts cards.)
   v1.0.7 — TRAILING-DRAWDOWN TRACKER (completes AccountProfile.DdAmount, was "future"). Alongside the
            daily-realized GOVERNOR, the governor tick now also tracks each governed account's lifetime
            EQUITY (CashValue + open P&L) vs its firm trailing threshold (profile ddAmt/ddType): peak =
            persisted high-water mark (SentinelCore.State, survives restart); floor = peak - ddAmt
            (static = pinned at start-ddAmt, doesn't trail); cushion = equity - floor. Publishes
            SentinelCore.SetDrawdownState → CanEnter blocks new entries when the cushion is thin (the #1
            funded-account killer the daily governor can't see). Opt-in hardEnforce auto-flattens ONCE a
            hair above the floor (new ddFlat= key) to beat the firm's engine. Zone-transition alerts.
            Fail-open (ddAmt=0 → not tracked). eod-type ratchet is a conservative intraday approximation.
   v1.0.6 — ACCOUNT PROFILES: the governor config source is now Sentinel\Profiles.conf (rich per-account
            profile: firm/size/contracts/ddType/ddAmt/dailyLoss/ratio/target/manualDaily/session; firm
            preset fills defaults you override). Parsed → published to SentinelCore.SetAccountProfiles →
            the governor derives cap/loss from the profile. Falls back to the legacy Governor.conf name.
   v1.0.5 — CONSISTENCY GOVERNOR host (Docs/CONSISTENCY_GOVERNOR_SPEC.md): loads Sentinel\Governor.conf
            (per account: firm/ratio/target/dailyLossStop/manualDailyTarget), tracks each account's
            DAILY realized P&L (baseline captured at first sight + session rollover), and publishes
            SentinelCore.SetGovernorState — DayComplete at the firm cap (R×target, consistency), DayHalted
            at the loss-stop. Consumers gate via TradingAllowedToday. Risk "owns account P&L" so it hosts
            this per the spec; distinct from the trailing-DD (feed) kill-switch. Snapshot gains Governors.
   v1.0.4 — SCOPED (per-instrument) AUTO-KILL. Instead of one GLOBAL kill on any feed breach,
            Risk now engages a PER-ROOT kill (SentinelCore.SetInstrumentKill) so a lagging GC
            feed halts only GC actions — ES/NQ keep trading. Hysteresis is now per-root
            (engage instantly, release after HealthyDebounceSeconds clean per root). Roots whose
            feed stops being monitored, and all our kills on Stop(), are released so nothing stays
            stuck. Snapshot gains InstrumentKills (root — reason). The GLOBAL kill-switch stays a
            manual "halt everything". Consumers scope for free: Copier via CanActInstrument,
            GTrader21 via CanEnter. RootOf() = MasterInstrument.Name.
   v1.0.3 — LIVE-PHASE HARDENING (4 additions; all respect the v1.0.2 "no NT market-data calls
            under _lock" rule):
              • KILL-SWITCH HYSTERESIS — engage instantly on breach, but only RELEASE after
                feeds are continuously clean for HealthyDebounceSeconds (default 10s). Kills the
                flapping seen 2026-07-02 (lag bouncing across the 2s threshold → engage/release
                every ~2s).
              • WATCH-LIST — monitors not just held-position instruments but also any Instrument
                a chart strategy REGISTERS via SentinelCore.RegisterWatchInstrument. Closes the
                gap where a FLAT leader's stalled chart feed went uncaught (only its own strategy
                halt fired). (Wiring the leader strategy to register is a 1-line follow-up.)
              • FEED-RECOVERY — on a SUSTAINED stall (> RecoveryStallSeconds, default 60s) auto
                RE-REQUESTS the feed (release + re-subscribe MarketData) with cooldown + max
                attempts + logging. Honest limit: this is a data re-request; the guaranteed human
                fix remains disable/re-enable the strategy. VALIDATE which action actually clears
                a stuck subscription live. Manual "Re-request feeds" via ReRequestAllFeeds().
              • ROLLOVER + NEWS GATES — computes each monitored instrument's days-to-roll (from
                MasterInstrument.RolloverCollection, same API as the DaysUntilRollover column) and
                publishes SentinelCore.SetRollover (Blocked within RollBlockDays). Loads a
                Sentinel\News.conf calendar and publishes active SentinelCore news lockouts.
                Strategies/copier gate entries via SentinelCore.CanEnter. Blocks entries only —
                never auto-flattens (you must always be able to exit).
   v1.0.2 — DEADLOCK FIX (likely the root cause of the recurring NT compile/teardown hangs AND
            the frozen state.json): OnTimer used to call `new MarketData()` / unsubscribe WHILE
            holding _lock. On a connecting feed that NT call can BLOCK, so _lock stayed held, and
            the State-service writer (which reads GetSnapshot for state.json's risk block) hung on
            it — leaving a thread stuck inside NT's data code that also deadlocked recompile
            teardown. Fix: subscribe/unsubscribe OUTSIDE _lock; reentrancy guard so a slow tick
            can't overlap; GetSnapshot uses TryEnter(50ms) so it NEVER blocks its caller.
   v1.0.1 — teardown hardening: `_stopping` flag set FIRST in Stop(); OnTimer/OnMarketTick/
            OnConnStatus bail instantly while stopping; timer DRAINED on dispose (bounded 500ms).
            Reduces the compile-hang risk of a threadpool callback touching Account.All / market
            data while NT disposes AddOns on recompile. No functional change to the watchdog.
   v1.0.0 — initial: per-instrument lag/stall watchdog on held instruments + connection-status
            tracking; auto-engages/releases the shared kill-switch on breach; registers
            FeedHealthProbe (per-account connection health); GetSnapshot() for the Risk tab.
            Logs to sentinel.log via SentinelCore. Headless singleton; runs always.
```

