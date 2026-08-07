// ─────────────────────────────────────────────────────────────────────────────
// This Source Code Form is subject to the terms of the Mozilla Public License,
// v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
// one at https://mozilla.org/MPL/2.0/.
//
// Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
// ─────────────────────────────────────────────────────────────────────────────
// ═════════════════════════════════════════════════════════════════════════════
//  SentinelRiskService — feed-health / lag watchdog for the Sentinel Suite (NT8)
//  File: SentinelRiskService_v1_0_0.cs
//  Version: v1.0.11
// ─────────────────────────────────────────────────────────────────────────────
//  WHAT THIS IS  (see memory: sentinel-suite-architecture, ninjatrader-observability)
//    A headless, always-on AddOnBase service — the SAFETY tool of the suite. It watches
//    the DATA FEED for the instruments you're actively trading and your connection health,
//    and when a feed lags or stalls it AUTO-ENGAGES the shared SentinelCore kill-switch —
//    which in turn halts the Copier (CanMirror consults SentinelCore.KillSwitchEngaged).
//    It also registers SentinelCore.FeedHealthProbe so any tool can gate per-account.
//
//  WHY  — the user hit real OVERNIGHT DATA LAG degrading feeds. This generalizes the
//    GTrader21 v0.1.2 lag metric into a suite-wide, screen-free watchdog.
//
//  HOW IT MEASURES (verified in-repo)
//    Lag  = (Core.Globals.Now - e.Time).TotalSeconds        — GTrader21v_0_1_3Panel.cs:749
//           (how far behind wall-clock the latest tick's own timestamp is)
//    Stall= (Core.Globals.Now - lastTickWallClock)          — no tick received for N seconds
//    Feeds: new MarketData(instr) + .Update += handler; release via .Update -= (no Dispose).
//    Auto-subscribes to instruments that currently have an OPEN POSITION on any account
//    (polled every 2s); drops the subscription when the position closes.
//
//  AUTO-KILL (default ON): lag > MaxLagSeconds OR stall > MaxStallSeconds on ANY monitored
//    feed → SentinelCore.SetKillSwitch(true). When feeds recover (and AutoReleaseWhenHealthy),
//    it releases the switch. It only releases a kill IT engaged (won't undo a manual/other kill).
//    NOTE (v1): during a live breach it will re-assert the kill even if a human clears it —
//    safety-first; refine later if that's too aggressive.
//
//  VERIFIED APIs: MarketDataEventArgs.{Time,Price,MarketDataType} (GTrader21 + archived MAE),
//    Connection.ConnectionStatusUpdate + ConnectionStatusEventArgs.{Status,Connection}
//    (AutoReconnect.cs), Account.Connection.Status, Account.Positions.
//
//  CHANGELOG
//    v1.0.11 (2026-07-09) — THE NAKED-POSITION ALERT WAS CRYING WOLF. `ReconcileAccount` counted a stop as present
//             only in Working|Accepted|PartFilled. NT transits an order through ChangePending/ChangeSubmitted on
//             every modify, and GTrader21 TRAILS its stop — so at each trail step the stop left that set, this
//             2-second scan saw a naked position, and fired a CRITICAL. **160 false NAKED POSITION alerts** sit in
//             the Ledger (74 on 07-05 alone), same account + instrument, 20-60 s apart. The system's most severe
//             alert was mostly noise, so a REAL naked position would have been invisible inside it.
//               • `IsLive(OrderState)` now counts the pending-modify states: a stop mid-trail-move is still a stop.
//               • naked + orphan are CONDITION ALERTS through `SentinelCore.Conditions` (≥ v1.17.0): 10 s debounce,
//                 re-stated every 300 s / 900 s while true, auto-cleared on resolve. The alert now says how long the
//                 position has been unprotected.
//               • the ORPHAN latch was the mirror bug — the naked-flag sweep deleted `<acct>|ORPHAN` on EVERY scan
//                 (it starts with the account prefix but is never an instrument name), so it re-alerted every 2 s.
//                 They shared one HashSet and got opposite bugs from it. Now separate, distinct keys.
//               • a flat instrument's episode is explicitly Cleared — otherwise it lingers "already reported" and
//                 suppresses the NEXT genuine naked position for a whole cooldown.
//               • naked/orphan are NO LONGER reset at the day roll: a position naked across the roll is still naked.
//                 Only the ACTION latches (_hardFlattened/_ddFlattened) and transition memory reset there.
//             + every Risk alert now passes its ACCOUNT (`Alerts.Critical(title, detail, account)`) — all 164
//               ALERT-CRIT Ledger rows previously recorded `acct:""`. (Execution-plan step 2.3.)
//    v1.0.10 — ALWAYS-MONITORED ROLLOVER ROOTS: RolloverWatchRoots (default "ES,GC,SI,CL") are resolved to their
//             front-month instrument and folded into the rollover countdown even when nothing is held/charted, so
//             the dashboard Risk-tab rollover list always shows the key contracts. Resolved once + cached
//             (Instrument.GetInstrument hits the instrument DB); fail-safe (a root that won't resolve is skipped).
//    v1.0.9 — NEWS-CALENDAR FRESHNESS GUARD (event-veto safety): a stale/missing Sentinel\News.conf means
//             today's high-impact windows aren't loaded → the news veto silently fails OPEN (you'd trade
//             through FOMC/NFP unprotected). CheckNewsFreshness now WARNS (Log + Alert, fail-to-caution,
//             throttled 6h) when News.conf is missing or older than ~26h; clears when refreshed. Does NOT
//             block. Closes the critical freshness gap in the EconomicCalendar.py → sentinel_newsconf.py →
//             News.conf → SetNewsLockouts → Council veto pipeline (economic-calendar-event-veto memory).
//    v1.0.8 — PERSIST the governor daily-P&L baseline (SentinelCore.State, keyed by account + trading-day)
//             so a mid-day F5/restart no longer zeroes the day's realized P&L; a new trading day recaptures.
//             (Pairs with SentinelDashboard v1.1.6 showing open/unrealized P&L on the Accounts cards.)
//    v1.0.7 — TRAILING-DRAWDOWN TRACKER (completes AccountProfile.DdAmount, was "future"). Alongside the
//             daily-realized GOVERNOR, the governor tick now also tracks each governed account's lifetime
//             EQUITY (CashValue + open P&L) vs its firm trailing threshold (profile ddAmt/ddType): peak =
//             persisted high-water mark (SentinelCore.State, survives restart); floor = peak - ddAmt
//             (static = pinned at start-ddAmt, doesn't trail); cushion = equity - floor. Publishes
//             SentinelCore.SetDrawdownState → CanEnter blocks new entries when the cushion is thin (the #1
//             funded-account killer the daily governor can't see). Opt-in hardEnforce auto-flattens ONCE a
//             hair above the floor (new ddFlat= key) to beat the firm's engine. Zone-transition alerts.
//             Fail-open (ddAmt=0 → not tracked). eod-type ratchet is a conservative intraday approximation.
//    v1.0.6 — ACCOUNT PROFILES: the governor config source is now Sentinel\Profiles.conf (rich per-account
//             profile: firm/size/contracts/ddType/ddAmt/dailyLoss/ratio/target/manualDaily/session; firm
//             preset fills defaults you override). Parsed → published to SentinelCore.SetAccountProfiles →
//             the governor derives cap/loss from the profile. Falls back to the legacy Governor.conf name.
//    v1.0.5 — CONSISTENCY GOVERNOR host (Docs/CONSISTENCY_GOVERNOR_SPEC.md): loads Sentinel\Governor.conf
//             (per account: firm/ratio/target/dailyLossStop/manualDailyTarget), tracks each account's
//             DAILY realized P&L (baseline captured at first sight + session rollover), and publishes
//             SentinelCore.SetGovernorState — DayComplete at the firm cap (R×target, consistency), DayHalted
//             at the loss-stop. Consumers gate via TradingAllowedToday. Risk "owns account P&L" so it hosts
//             this per the spec; distinct from the trailing-DD (feed) kill-switch. Snapshot gains Governors.
//    v1.0.4 — SCOPED (per-instrument) AUTO-KILL. Instead of one GLOBAL kill on any feed breach,
//             Risk now engages a PER-ROOT kill (SentinelCore.SetInstrumentKill) so a lagging GC
//             feed halts only GC actions — ES/NQ keep trading. Hysteresis is now per-root
//             (engage instantly, release after HealthyDebounceSeconds clean per root). Roots whose
//             feed stops being monitored, and all our kills on Stop(), are released so nothing stays
//             stuck. Snapshot gains InstrumentKills (root — reason). The GLOBAL kill-switch stays a
//             manual "halt everything". Consumers scope for free: Copier via CanActInstrument,
//             GTrader21 via CanEnter. RootOf() = MasterInstrument.Name.
//    v1.0.3 — LIVE-PHASE HARDENING (4 additions; all respect the v1.0.2 "no NT market-data calls
//             under _lock" rule):
//               • KILL-SWITCH HYSTERESIS — engage instantly on breach, but only RELEASE after
//                 feeds are continuously clean for HealthyDebounceSeconds (default 10s). Kills the
//                 flapping seen 2026-07-02 (lag bouncing across the 2s threshold → engage/release
//                 every ~2s).
//               • WATCH-LIST — monitors not just held-position instruments but also any Instrument
//                 a chart strategy REGISTERS via SentinelCore.RegisterWatchInstrument. Closes the
//                 gap where a FLAT leader's stalled chart feed went uncaught (only its own strategy
//                 halt fired). (Wiring the leader strategy to register is a 1-line follow-up.)
//               • FEED-RECOVERY — on a SUSTAINED stall (> RecoveryStallSeconds, default 60s) auto
//                 RE-REQUESTS the feed (release + re-subscribe MarketData) with cooldown + max
//                 attempts + logging. Honest limit: this is a data re-request; the guaranteed human
//                 fix remains disable/re-enable the strategy. VALIDATE which action actually clears
//                 a stuck subscription live. Manual "Re-request feeds" via ReRequestAllFeeds().
//               • ROLLOVER + NEWS GATES — computes each monitored instrument's days-to-roll (from
//                 MasterInstrument.RolloverCollection, same API as the DaysUntilRollover column) and
//                 publishes SentinelCore.SetRollover (Blocked within RollBlockDays). Loads a
//                 Sentinel\News.conf calendar and publishes active SentinelCore news lockouts.
//                 Strategies/copier gate entries via SentinelCore.CanEnter. Blocks entries only —
//                 never auto-flattens (you must always be able to exit).
//    v1.0.2 — DEADLOCK FIX (likely the root cause of the recurring NT compile/teardown hangs AND
//             the frozen state.json): OnTimer used to call `new MarketData()` / unsubscribe WHILE
//             holding _lock. On a connecting feed that NT call can BLOCK, so _lock stayed held, and
//             the State-service writer (which reads GetSnapshot for state.json's risk block) hung on
//             it — leaving a thread stuck inside NT's data code that also deadlocked recompile
//             teardown. Fix: subscribe/unsubscribe OUTSIDE _lock; reentrancy guard so a slow tick
//             can't overlap; GetSnapshot uses TryEnter(50ms) so it NEVER blocks its caller.
//    v1.0.1 — teardown hardening: `_stopping` flag set FIRST in Stop(); OnTimer/OnMarketTick/
//             OnConnStatus bail instantly while stopping; timer DRAINED on dispose (bounded 500ms).
//             Reduces the compile-hang risk of a threadpool callback touching Account.All / market
//             data while NT disposes AddOns on recompile. No functional change to the watchdog.
//    v1.0.0 — initial: per-instrument lag/stall watchdog on held instruments + connection-status
//             tracking; auto-engages/releases the shared kill-switch on breach; registers
//             FeedHealthProbe (per-account connection health); GetSnapshot() for the Risk tab.
//             Logs to sentinel.log via SentinelCore. Headless singleton; runs always.
// ═════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;

namespace NinjaTrader.NinjaScript.AddOns.Sentinel
{
    public class SentinelRiskService_v1_0_0 : NinjaTrader.NinjaScript.AddOnBase
    {
        public static SentinelRiskService_v1_0_0 Instance { get; private set; }

        // thresholds + policy (static so the dashboard can tune them live; Risk.conf later)
        public static double MaxLagSeconds        = 5.0;   // 2.0 → 5.0 (2026-07-14): prop feeds carry chronic 2–3 s lag;
                                                           //   2 s kill-vetoed every entry. 5 s tolerates the feed while
                                                           //   still catching a genuine stall/failure. Tune live via dashboard.
        public static double MaxStallSeconds      = 15.0;
        public static bool   AutoKillOnBreach     = true;
        public static bool   AutoReleaseWhenHealthy = true;

        // v1.0.3 — hysteresis: only RELEASE the kill after feeds stay clean this long (anti-flapping)
        public static double HealthyDebounceSeconds = 10.0;

        // v1.0.3 — feed-recovery: a stall beyond this long triggers an auto data re-request
        public static bool   AutoRecoverFeeds       = true;
        public static double RecoveryStallSeconds   = 60.0;   // trigger well before the 43-min dead-feed cases seen
        public static double RecoverCooldownSeconds = 45.0;   // min gap between recovery attempts on one feed
        public static int    MaxRecoverAttempts     = 3;      // then give up (needs the human disable/re-enable fix)

        // v1.0.3 — rollover gate (days-to-roll from MasterInstrument.RolloverCollection)
        public static int    RollBlockDays          = 2;      // block NEW entries this many days before roll
        public static int    RollWarnDays           = 7;      // start the countdown/warn this many days out
        public static double RollComputeSeconds     = 30.0;   // recompute cadence (rollover changes slowly)

        // v1.0.3 — news lockout (Sentinel\News.conf); default window minutes around an event
        public static int    NewsBeforeMin          = 2;
        public static int    NewsAfterMin           = 2;

        // v1.0.5 — consistency governor (Sentinel\Governor.conf); per-account daily cap/loss gate
        public static bool   GovernorEnabled        = true;

        private sealed class Feed
        {
            public Instrument Instr;
            public MarketData Md;
            public bool       Subscribed;
            public DateTime   LastTickTime;   // e.Time of the latest tick (data timestamp)
            public DateTime   LastTickWall;   // Core.Globals.Now at receipt
            public bool       GotTick;
            public double     LagSec;
            public double     StallSec;
            // v1.0.3 — feed-recovery bookkeeping
            public int        RecoverAttempts;
            public DateTime   LastRecoverWall;
            public bool       FromWatch;      // monitored because a strategy registered it (vs. held position)
        }

        private Dictionary<string, Feed>   _feeds;       // key = instrument.FullName
        private Dictionary<string, string> _connStatus;  // connection name -> status
        private Timer  _timer;
        private bool   _started;
        private volatile bool _stopping;   // set FIRST in Stop() so timer/event callbacks bail during teardown
        private int    _ticking;         // reentrancy guard (a slow subscribe must not overlap the next tick)
        private readonly object _lock = new object();

        // v1.0.4 — per-ROOT scoped-kill bookkeeping (replaces the single global auto-kill so a bad
        // GC feed halts only GC, not ES/NQ). Touched only on the timer thread.
        private readonly HashSet<string> _weKilledRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _rootCleanSince = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        // v1.0.3 state (all touched only on the timer thread or under the noted lock)
        private DateTime _lastRollWall   = DateTime.MinValue;   // last rollover recompute
        private DateTime _newsFileMtime  = DateTime.MinValue;   // News.conf mtime we last parsed
        private bool     _newsStale;                            // v1.0.9: News.conf missing/older than the max age → protection may be MISSING
        private DateTime _newsStaleAlertUtc = DateTime.MinValue;// throttle the stale-calendar warning
        private const double NewsMaxAgeHours = 26.0;            // News.conf must be refreshed within ~a day (the feeder runs pre-open)
        private List<NewsEvent> _newsEvents = new List<NewsEvent>();
        private volatile string _newsNext = null;               // next upcoming event (display)

        // v1.0.5 — governor: per-account config + daily realized-P&L baselines (reset at session rollover)
        private DateTime _govFileMtime = DateTime.MinValue;
        private Dictionary<string, GovConfig> _govConfig = new Dictionary<string, GovConfig>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double> _govBaseline = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private DateTime _govDay = DateTime.MinValue;
        private readonly HashSet<string> _hardFlattened = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // accounts auto-flattened this day
        private readonly Dictionary<string, string> _govPrevStatus = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // last governor status (transition alerts)
        // v1.0.11 — _nakedAlerted is GONE. Naked + orphan are CONDITION alerts owned by SentinelCore.Conditions
        // (debounce → report → re-state on a cooldown → auto-clear on resolve). See [[conditions-vs-latches]].
        // trailing-drawdown (v1.0.7): peak-equity high-water per account (PERSISTED — never lost on restart);
        // _ddFlattened = auto-flattened-on-breach this day (day-roll re-arms); _ddZone = last zone for once-per alerts
        private readonly Dictionary<string, double> _ddPeak = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _ddFlattened = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _ddZone = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private sealed class GovConfig { public string Firm; public double Cap; public double LossStop; public bool HardEnforce;
            public double DdAmount; public string DdType; public double DdFlatBuffer; }   // v1.0.7 trailing-DD

        private sealed class NewsEvent
        {
            public DateTime When;      // platform-local scheduled time
            public string   Name;
            public string[] Scope;     // instrument roots, or null = all
            public int      BeforeMin;
            public int      AfterMin;
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "SentinelRiskService";
                Description = "Sentinel Suite — feed lag/stall + connection watchdog. Auto-engages the "
                            + "shared kill-switch on a data-feed breach (halting Copy). Runs always.";
            }
            else if (State == State.Active)      Start();
            else if (State == State.Terminated)  Stop();
        }

        private void Start()
        {
            if (_started) return;
            _stopping = false;
            _started = true;
            Instance = this;
            _feeds      = new Dictionary<string, Feed>();
            _connStatus = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            Connection.ConnectionStatusUpdate += OnConnStatus;
            // seed current connection statuses (the event only fires on CHANGES)
            try { lock (Connection.Connections) { foreach (Connection c in Connection.Connections) if (c != null) _connStatus[ConnName(c)] = c.Status.ToString(); } } catch (Exception _sx) { SentinelCore.Swallow("SentinelRisk.Start", _sx); }
            SentinelCore.FeedHealthProbe = ProbeAccount;   // ← the real probe (Copier now gates on this)

            _timer = new Timer(OnTimer, null, 2000, 2000);
            SentinelCore.Log("Risk", "SentinelRiskService started. Auto-kill "
                + (AutoKillOnBreach ? "ON" : "OFF") + " (lag>" + MaxLagSeconds + "s or stall>" + MaxStallSeconds + "s).");
        }

        private void Stop()
        {
            if (!_started) return;
            _stopping = true;   // callbacks bail before we unhook events / touch NT market-data objects
            _started = false;
            Connection.ConnectionStatusUpdate -= OnConnStatus;
            SentinelCore.FeedHealthProbe = null;   // release the probe (fail-open again)
            if (_timer != null)
            {
                // drain in-flight timer callbacks, bounded (never block NT teardown)
                try { var done = new ManualResetEvent(false); if (_timer.Dispose(done)) done.WaitOne(500); done.Close(); }
                catch { try { _timer.Dispose(); } catch (Exception _sx) { SentinelCore.Swallow("SentinelRisk.Stop", _sx); } }
                _timer = null;
            }
            lock (_lock)
            {
                if (_feeds != null) { foreach (var kv in _feeds) ReleaseFeed(kv.Value); _feeds.Clear(); }
            }
            // v1.0.4: release every scoped kill WE engaged — else a root stays halted with no one
            // left to clear it (Risk is going away).
            foreach (var root in _weKilledRoots.ToList())
                SentinelCore.SetInstrumentKill(root, false, "Risk: service stopping");
            _weKilledRoots.Clear(); _rootCleanSince.Clear();
            if (Instance == this) Instance = null;
            SentinelCore.Log("Risk", "SentinelRiskService stopped.");
        }

        // ── connection status tracking (verified pattern from AutoReconnect) ─────
        private void OnConnStatus(object sender, ConnectionStatusEventArgs e)
        {
            if (_stopping) return;
            try
            {
                if (e == null || e.Connection == null) return;
                string name = ConnName(e.Connection);
                string status = e.Status.ToString();
                lock (_lock) { _connStatus[name] = status; }
                SentinelCore.Log("Risk", "connection '" + name + "' → " + status);
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelRisk.OnConnStatus", _sx); }
        }

        private static string ConnName(Connection c)
        {
            try { if (c != null && c.Options != null && c.Options.Name != null) return c.Options.Name; } catch (Exception _sx) { SentinelCore.Swallow("SentinelRisk.ConnName", _sx); }
            return "?";
        }

        // ── the FeedHealthProbe the Copier (and others) consult per account ──────
        // Per-account granularity = connection up. (Lag is feed/instrument-level and is
        // handled via the kill-switch path, which halts ALL mirroring on a breach.)
        private bool ProbeAccount(Account a)
        {
            try { return a == null || a.Connection == null || a.Connection.Status == ConnectionStatus.Connected; }
            catch { return true; }   // fail-open: never let a probe hiccup block trading
        }

        // ── 2s watchdog tick ─────────────────────────────────────────────────────
        private void OnTimer(object _)
        {
            if (_stopping || !_started) return;
            if (System.Threading.Interlocked.CompareExchange(ref _ticking, 1, 0) != 0) return; // no overlap
            try
            {
                HashSet<string> heldKeys;
                Dictionary<string, Instrument> desired = ActiveInstruments(out heldKeys);   // no lock

                // 1) decide adds/removes under a BRIEF lock — NO market-data calls in here
                var toAdd = new List<Instrument>();
                var toRelease = new List<Feed>();
                lock (_lock)
                {
                    foreach (var kv in desired)
                        if (!_feeds.ContainsKey(kv.Key)) toAdd.Add(kv.Value);
                    foreach (var k in _feeds.Keys.Where(k => !desired.ContainsKey(k)).ToList())
                    {
                        toRelease.Add(_feeds[k]);
                        _feeds.Remove(k);   // remove now; the NT unsubscribe happens OUTSIDE the lock
                    }
                }

                // 2) potentially-BLOCKING NT market-data calls happen OUTSIDE _lock (the fix:
                //    a blocking new MarketData() on a connecting feed used to hold _lock and hang
                //    the State writer that reads GetSnapshot).
                foreach (var f in toRelease) ReleaseFeed(f);
                var added = new List<Feed>();
                foreach (var instr in toAdd)
                {
                    if (_stopping) return;
                    string key = instr != null ? Fn(instr) : null;
                    var f = new Feed { Instr = instr, FromWatch = key != null && !heldKeys.Contains(key) };
                    SubscribeFeed(f);                       // new MarketData(...) — may block; NOT holding _lock
                    added.Add(f);
                }

                // 3) insert new feeds + measure lag/stall under a BRIEF lock — no NT calls.
                //    Collect feeds needing recovery; the NT re-subscribe happens OUTSIDE the lock.
                DateTime now = NinjaTrader.Core.Globals.Now;
                var rootBreach = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);  // root -> worst-breach reason
                var rootsSeen  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);              // all monitored roots
                var toRecover = new List<Feed>();
                lock (_lock)
                {
                    foreach (var f in added)
                    {
                        string key = f.Instr != null ? Fn(f.Instr) : null;
                        if (key != null && !_feeds.ContainsKey(key)) _feeds[key] = f;
                    }
                    foreach (var f in _feeds.Values)
                    {
                        string root = RootOf(f.Instr);
                        if (root.Length > 0) rootsSeen.Add(root);
                        if (!f.GotTick) { f.LagSec = 0; f.StallSec = 0; continue; }
                        f.LagSec   = (now - f.LastTickTime).TotalSeconds;
                        f.StallSec = (now - f.LastTickWall).TotalSeconds;
                        string br = null;
                        if (f.LagSec > MaxLagSeconds)         br = "lag "   + f.LagSec.ToString("0.0") + "s on " + Fn(f.Instr);
                        else if (f.StallSec > MaxStallSeconds) br = "stall " + f.StallSec.ToString("0")  + "s on " + Fn(f.Instr);
                        if (br != null && root.Length > 0 && !rootBreach.ContainsKey(root)) rootBreach[root] = br;

                        // recovery candidacy: a SUSTAINED stall, honoring cooldown + max attempts
                        if (AutoRecoverFeeds && f.StallSec > RecoveryStallSeconds
                            && f.RecoverAttempts < MaxRecoverAttempts
                            && (f.LastRecoverWall == DateTime.MinValue
                                || (now - f.LastRecoverWall).TotalSeconds >= RecoverCooldownSeconds))
                            toRecover.Add(f);
                        // a healthy feed resets its recovery budget
                        else if (f.StallSec <= MaxStallSeconds && f.RecoverAttempts != 0)
                            f.RecoverAttempts = 0;
                    }
                }

                // 4) per-ROOT scoped kill WITH HYSTERESIS (v1.0.4): engage a root's kill instantly on
                //    its breach; only RELEASE after that root stays clean HealthyDebounceSeconds. A bad
                //    GC feed halts ONLY GC (SetInstrumentKill), not ES/NQ. The GLOBAL kill stays manual.
                if (AutoKillOnBreach)
                {
                    foreach (string root in rootsSeen)
                    {
                        if (rootBreach.ContainsKey(root))
                        {
                            _rootCleanSince.Remove(root);
                            if (!_weKilledRoots.Contains(root))
                            { SentinelCore.SetInstrumentKill(root, true, "Risk: " + rootBreach[root]); _weKilledRoots.Add(root); }
                        }
                        else
                        {
                            DateTime cs;
                            if (!_rootCleanSince.TryGetValue(root, out cs)) { cs = now; _rootCleanSince[root] = now; }
                            if (_weKilledRoots.Contains(root) && AutoReleaseWhenHealthy
                                && (now - cs).TotalSeconds >= HealthyDebounceSeconds)
                            {
                                SentinelCore.SetInstrumentKill(root, false, "Risk: clean " + HealthyDebounceSeconds.ToString("0") + "s");
                                _weKilledRoots.Remove(root); _rootCleanSince.Remove(root);
                            }
                        }
                    }
                    // release a root we killed whose feed is no longer monitored (position closed / unwatched)
                    foreach (var root in _weKilledRoots.Where(r => !rootsSeen.Contains(r)).ToList())
                    {
                        SentinelCore.SetInstrumentKill(root, false, "Risk: feed no longer monitored");
                        _weKilledRoots.Remove(root); _rootCleanSince.Remove(root);
                    }
                }

                // 5) feed-recovery: re-request stalled feeds OUTSIDE _lock (may block on NT data code)
                foreach (var f in toRecover)
                {
                    if (_stopping) return;
                    f.RecoverAttempts++;
                    f.LastRecoverWall = now;
                    SentinelCore.Log("Risk", "feed-recovery: re-requesting " + Fn(f.Instr) + " (stall "
                        + f.StallSec.ToString("0") + "s, attempt " + f.RecoverAttempts + "/" + MaxRecoverAttempts
                        + ") — data re-request; if this doesn't clear it, disable/re-enable the strategy.");
                    ReleaseFeed(f);
                    SubscribeFeed(f);
                    // reset staleness so we don't re-fire before the cooldown; next real tick re-stamps it
                    f.LastTickWall = now;
                }

                // 6) rollover countdown (throttled — changes slowly) + news lockout (every tick)
                if (_lastRollWall == DateTime.MinValue || (now - _lastRollWall).TotalSeconds >= RollComputeSeconds)
                { _lastRollWall = now; ComputeAndPublishRollover(desired.Values.Concat(DefaultRolloverInstruments())); }
                ComputeAndPublishNews(now);

                // 7) consistency governor: per-account daily cap/loss gate (Docs/CONSISTENCY_GOVERNOR_SPEC.md)
                if (GovernorEnabled) { try { LoadProfilesIfChanged(); GovernorTick(now); } catch (Exception _sx) { SentinelCore.Swallow("SentinelRisk.OnTimer", _sx); } }
            }
            catch (Exception ex) { SentinelCore.Log("Risk", "tick error: " + ex.Message); }
            finally { System.Threading.Interlocked.Exchange(ref _ticking, 0); }
        }

        // ── rollover: days-to-roll per root, published to SentinelCore (same API as the
        //    DaysUntilRollover market-analyzer column: MasterInstrument.RolloverCollection +
        //    GetNextRolloverDate). Runs OUTSIDE _lock (locks the NT RolloverCollection). ────────
        private void ComputeAndPublishRollover(IEnumerable<Instrument> instrs)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var instr in instrs)
            {
                try
                {
                    if (instr == null || instr.MasterInstrument == null) continue;
                    string root = instr.MasterInstrument.Name;
                    if (string.IsNullOrEmpty(root) || !seen.Add(root)) continue;

                    DateTime rollDate;
                    if (!TryGetRollDate(instr, out rollDate)) continue;
                    double days = (rollDate.Date - NinjaTrader.Core.Globals.Now.Date).TotalDays;

                    SentinelCore.SetRollover(new SentinelCore.RolloverInfo
                    {
                        Root = root, Contract = Fn(instr),
                        DaysToRoll = days, RollDateLocal = rollDate,
                        Blocked = days <= RollBlockDays, Warn = days <= RollWarnDays
                    });
                }
                catch (Exception _sx) { SentinelCore.Swallow("SentinelRisk.ComputeAndPublishRollover", _sx); }
            }
        }

        // Always-monitored rollover roots (shown even when nothing is held/charted). Resolved to their
        // front-month Instrument once and cached (GetInstrument hits the instrument DB). Comma-separated roots.
        public static string RolloverWatchRoots = "ES,GC,SI,CL";
        private readonly Dictionary<string, Instrument> _rollWatch = new Dictionary<string, Instrument>(StringComparer.OrdinalIgnoreCase);

        private IEnumerable<Instrument> DefaultRolloverInstruments()
        {
            var outp = new List<Instrument>();
            string roots = RolloverWatchRoots ?? "";
            foreach (string r0 in roots.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string root = r0.Trim();
                if (root.Length == 0) continue;
                Instrument ins;
                if (!_rollWatch.TryGetValue(root, out ins) || ins == null)
                {
                    try { ins = Instrument.GetInstrument(root); } catch { ins = null; }   // front-month lookup (DB)
                    if (ins != null) _rollWatch[root] = ins;
                }
                if (ins != null) outp.Add(ins);
            }
            return outp;
        }

        private static bool TryGetRollDate(Instrument instr, out DateTime rollDate)
        {
            rollDate = DateTime.MinValue;
            try
            {
                var mi = instr.MasterInstrument;
                if (mi == null || mi.RolloverCollection == null) return false;
                lock (mi.RolloverCollection)
                {
                    foreach (Rollover r in mi.RolloverCollection)
                        if (r.ContractMonth == instr.Expiry)
                        { rollDate = mi.GetNextRolloverDate(r.Date); return true; }
                }
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelRisk.TryGetRollDate", _sx); }
            return false;
        }

        // ── news lockout: load Sentinel\News.conf (on mtime change), publish active windows ─────
        private void ComputeAndPublishNews(DateTime now)
        {
            try { LoadNewsIfChanged(); } catch (Exception _sx) { SentinelCore.Swallow("SentinelRisk.ComputeAndPublishNews", _sx); }
            CheckNewsFreshness(now);   // v1.0.9: warn if the calendar is stale (today's windows may be missing → unprotected)

            var active = new List<SentinelCore.NewsLockout>();
            DateTime? nextWhen = null; string nextName = null;
            foreach (var ev in _newsEvents)
            {
                if (ev == null) continue;
                DateTime start = ev.When.AddMinutes(-Math.Max(0, ev.BeforeMin));
                DateTime end   = ev.When.AddMinutes( Math.Max(0, ev.AfterMin));
                if (now >= start && now <= end)
                    active.Add(new SentinelCore.NewsLockout { Event = ev.Name, StartLocal = start, EndLocal = end, Scope = ev.Scope });
                else if (ev.When > now && (nextWhen == null || ev.When < nextWhen.Value))
                { nextWhen = ev.When; nextName = ev.Name; }
            }
            SentinelCore.SetNewsLockouts(active);
            _newsNext = nextWhen != null ? (nextName + " " + nextWhen.Value.ToString("MMM d HH:mm")) : null;
        }

        private void LoadNewsIfChanged()
        {
            string path = System.IO.Path.Combine(SentinelCore.SettingsDir, "News.conf");
            if (!System.IO.File.Exists(path))
            {
                if (_newsEvents.Count != 0) { _newsEvents = new List<NewsEvent>(); _newsFileMtime = DateTime.MinValue; }
                return;
            }
            DateTime mtime = System.IO.File.GetLastWriteTimeUtc(path);
            if (mtime == _newsFileMtime) return;   // unchanged
            _newsFileMtime = mtime;

            var list = new List<NewsEvent>();
            foreach (string raw in System.IO.File.ReadAllLines(path))
            {
                string line = raw == null ? "" : raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//")) continue;
                // YYYY-MM-DD HH:mm | Event | scope | beforeMin | afterMin
                string[] p = line.Split('|');
                if (p.Length < 2) continue;
                DateTime when;
                if (!DateTime.TryParseExact(p[0].Trim(), "yyyy-MM-dd HH:mm",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out when)) continue;

                var ev = new NewsEvent { When = when, Name = p[1].Trim(), BeforeMin = NewsBeforeMin, AfterMin = NewsAfterMin };
                if (p.Length >= 3)
                {
                    string sc = p[2].Trim();
                    if (sc.Length > 0 && !sc.Equals("all", StringComparison.OrdinalIgnoreCase))
                        ev.Scope = sc.Split(',').Select(x => x.Trim().ToUpperInvariant()).Where(x => x.Length > 0).ToArray();
                }
                int b, a;
                if (p.Length >= 4 && int.TryParse(p[3].Trim(), out b)) ev.BeforeMin = b;
                if (p.Length >= 5 && int.TryParse(p[4].Trim(), out a)) ev.AfterMin  = a;
                list.Add(ev);
            }
            _newsEvents = list;
            SentinelCore.Log("Risk", "loaded News.conf: " + list.Count + " event(s).");
        }

        // v1.0.9 FRESHNESS GUARD (the critical event-veto consume rule): a stale/missing News.conf means
        // TODAY's high-impact windows aren't loaded — so the news veto silently fails OPEN and you could
        // trade straight through FOMC/NFP unprotected. This does NOT block (that would punish anyone not
        // running the calendar feeder); it WARNS loudly (fail-to-caution) so the trader KNOWS protection is
        // off. Re-warns at most every 6h; clears when the file is refreshed.
        private void CheckNewsFreshness(DateTime now)
        {
            try
            {
                string path = System.IO.Path.Combine(SentinelCore.SettingsDir, "News.conf");
                bool exists = System.IO.File.Exists(path);
                double ageH = exists ? (DateTime.UtcNow - System.IO.File.GetLastWriteTimeUtc(path)).TotalHours : double.MaxValue;
                bool stale  = !exists || ageH > NewsMaxAgeHours;

                if (stale)
                {
                    _newsStale = true;
                    if ((DateTime.UtcNow - _newsStaleAlertUtc).TotalHours >= 6.0)   // throttle the warning
                    {
                        _newsStaleAlertUtc = DateTime.UtcNow;
                        string why = exists ? ("last updated " + Math.Round(ageH) + "h ago") : "file missing";
                        SentinelCore.Log("Risk", "⚠ News.conf STALE (" + why + ") — today's news windows may be MISSING; "
                            + "the news-event veto is NOT protecting entries. Re-run the calendar feeder (EconomicCalendar.py → sentinel_newsconf.py).");
                        try { SentinelCore.Alerts.Info("News calendar stale",
                            "News.conf " + why + " — news-event protection may be missing. Re-run the calendar feeder before trading through a news window."); } catch (Exception _sx) { SentinelCore.Swallow("SentinelRisk.CheckNewsFreshness", _sx); }
                    }
                }
                else if (_newsStale)
                {
                    _newsStale = false;
                    SentinelCore.Log("Risk", "News.conf refreshed — news-event protection active again.");
                }
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelRisk.CheckNewsFreshness", _sx); }
        }

        // ── account profiles / consistency governor ───────────────────────────────
        // Sentinel\Profiles.conf line (firm preset fills defaults you override):
        //   account=<name>|firm=lucid|size=1|contracts=0|ddType=trailing|ddAmt=0|ddFlat=0|dailyLoss=1500|ratio=0.20|target=9000|manualDaily=0|session=24h
        //   ddAmt = trailing-drawdown threshold $ (0 = OFF, no DD tracking). ddType = trailing|static|eod.
        //   ddFlat = $ ABOVE the floor to auto-flatten (needs hardEnforce=true); 0 = flatten AT the floor.
        private void LoadProfilesIfChanged()
        {
            string path = System.IO.Path.Combine(SentinelCore.SettingsDir, "Profiles.conf");
            if (!System.IO.File.Exists(path))
            {
                string legacy = System.IO.Path.Combine(SentinelCore.SettingsDir, "Governor.conf");  // backward-compat
                if (System.IO.File.Exists(legacy)) path = legacy;
                else
                {
                    if (_govConfig.Count != 0)
                    {
                        _govConfig = new Dictionary<string, GovConfig>(StringComparer.OrdinalIgnoreCase);
                        SentinelCore.SetAccountProfiles(null);
                        _govFileMtime = DateTime.MinValue;
                    }
                    return;
                }
            }
            DateTime mtime = System.IO.File.GetLastWriteTimeUtc(path);
            if (mtime == _govFileMtime) return;
            _govFileMtime = mtime;

            var map = new Dictionary<string, GovConfig>(StringComparer.OrdinalIgnoreCase);
            var profiles = new List<SentinelCore.AccountProfile>();
            foreach (string raw in System.IO.File.ReadAllLines(path))
            {
                string line = raw == null ? "" : raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//")) continue;
                var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string part in line.Split('|'))
                {
                    int e = part.IndexOf('=');
                    if (e > 0) kv[part.Substring(0, e).Trim()] = part.Substring(e + 1).Trim();
                }
                string acct; if (!kv.TryGetValue("account", out acct) || string.IsNullOrEmpty(acct)) continue;
                string firm = kv.ContainsKey("firm") ? kv["firm"].ToLowerInvariant() : "custom";

                double ratio  = GetD(kv, "ratio",  FirmRatio(firm));
                double target = GetD(kv, "target", 9000);
                double manual = GetD(kv, "manualDaily", GetD(kv, "manualDailyTarget", 0));
                double loss   = GetD(kv, "dailyLoss", GetD(kv, "dailyLossStop", FirmLoss(firm)));
                double size   = GetD(kv, "size", 1.0);
                int contracts = (int)GetD(kv, "contracts", 0);
                string ddType = (kv.ContainsKey("ddType") ? kv["ddType"] : "trailing").ToLowerInvariant();
                double ddAmt  = GetD(kv, "ddAmt", 0);
                double ddFlat = GetD(kv, "ddFlat", 0);   // v1.0.7: $ ABOVE the floor to auto-flatten (beat the firm's engine); 0 = at the floor
                string sess   = kv.ContainsKey("session") ? kv["session"] : "24h";
                int sStart, sEnd; ParseSession(sess, out sStart, out sEnd);
                string he = kv.ContainsKey("hardEnforce") ? kv["hardEnforce"].ToLowerInvariant() : "";
                bool hardEnforce = he == "true" || he == "1" || he == "on" || he == "yes";
                if (kv.ContainsKey("resetHour")) { try { SentinelCore.SetGovernorResetHour((int)GetD(kv, "resetHour", 0)); } catch (Exception _sx) { SentinelCore.Swallow("SentinelRisk.LoadProfilesIfChanged", _sx); } }   // global daily-reset hour (last line wins)

                double cap = ratio * target;
                if (manual > 0) cap = Math.Min(manual, cap);
                map[acct] = new GovConfig { Firm = firm, Cap = cap, LossStop = loss, HardEnforce = hardEnforce,
                    DdAmount = ddAmt, DdType = ddType, DdFlatBuffer = ddFlat };
                profiles.Add(new SentinelCore.AccountProfile
                {
                    Account = acct, Firm = firm, DdType = ddType, Session = sess,
                    SizeScale = size, ContractLimit = contracts, DdAmount = ddAmt,
                    DailyLossStop = loss, Ratio = ratio, ProfitTarget = target, ManualDailyTarget = manual,
                    SessionStartMin = sStart, SessionEndMin = sEnd, HardEnforce = hardEnforce
                });
            }
            _govConfig = map;
            SentinelCore.SetAccountProfiles(profiles);
            SentinelCore.Log("Risk", "loaded " + System.IO.Path.GetFileName(path) + ": " + profiles.Count + " account profile(s).");
        }

        private static double FirmRatio(string firm)
        {
            switch (firm) { case "lucid": return 0.20; case "bulenox": return 0.40; case "tpt": return 0.50; case "apex": return 0.30; default: return 0.30; }
        }
        private static double FirmLoss(string firm)
        {
            switch (firm) { case "lucid": return 1000; case "bulenox": return 1000; case "tpt": return 1500; case "apex": return 1500; default: return 1500; }
        }
        private static double GetD(Dictionary<string, string> kv, string k, double dflt)
        {
            string v; double d;
            return (kv.TryGetValue(k, out v) && double.TryParse(v, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out d)) ? d : dflt;
        }
        private static void ParseSession(string s, out int start, out int end)
        {
            start = -1; end = -1;
            if (string.IsNullOrEmpty(s) || s.Equals("24h", StringComparison.OrdinalIgnoreCase)) return;
            var p = s.Split('-');
            if (p.Length != 2) return;
            start = ParseHHmm(p[0]); end = ParseHHmm(p[1]);
        }
        private static int ParseHHmm(string t)
        {
            t = (t ?? "").Trim();
            int v;
            if (int.TryParse(t, out v) && v >= 0 && v <= 2359) return (v / 100) * 60 + (v % 100);
            return -1;
        }

        // per-account daily realized P&L vs firm cap/loss → publish governor state (baselines reset on rollover)
        private void GovernorTick(DateTime now)
        {
            if (_govConfig.Count == 0) return;
            // "trading day" rolls at the configured local reset hour (prop firms reset ~17:00, not midnight)
            DateTime tradingDay = now.AddHours(-SentinelCore.GovernorResetHour).Date;
            // Daily reset of the ACTION latches (flatten once per day) + transition memory. NOT _ddPeak — trailing DD
            // is a lifetime high-water mark. And NOT the naked/orphan CONDITIONS: a position that is naked across the
            // day roll is still naked, and its episode ends when it resolves, not when the clock does.
            if (tradingDay != _govDay) { _govDay = tradingDay; _govBaseline.Clear(); _hardFlattened.Clear(); _govPrevStatus.Clear(); _ddFlattened.Clear(); }

            var accts = new List<Account>();
            try { lock (Account.All) { foreach (Account a in Account.All) if (a != null && a.Name != null && _govConfig.ContainsKey(a.Name)) accts.Add(a); } } catch (Exception _sx) { SentinelCore.Swallow("SentinelRisk.GovernorTick", _sx); }

            foreach (Account a in accts)
            {
                GovConfig gc; if (!_govConfig.TryGetValue(a.Name, out gc)) continue;
                double realized;
                try { realized = a.Get(AccountItem.RealizedProfitLoss, Currency.UsDollar); } catch { continue; }
                double baseline;
                if (!_govBaseline.TryGetValue(a.Name, out baseline))
                {
                    // PERSISTED baseline (SentinelCore.State, keyed by account + trading-day) so a mid-day F5/
                    // restart no longer zeroes the day's realized P&L; a genuinely new trading day recaptures.
                    if (!TryLoadGovBaseline(a.Name, tradingDay, out baseline))
                    {
                        baseline = realized;
                        SaveGovBaseline(a.Name, tradingDay, baseline);
                    }
                    _govBaseline[a.Name] = baseline;
                }
                double dayPnl = realized - baseline;

                string status, reason; bool allowed;
                if (dayPnl >= gc.Cap)            { status = "DayComplete"; allowed = false; reason = "daily cap $" + Math.Round(gc.Cap) + " hit (banked $" + Math.Round(dayPnl) + ")"; }
                else if (dayPnl <= -gc.LossStop) { status = "DayHalted";   allowed = false; reason = "daily loss stop -$" + Math.Round(gc.LossStop) + " hit ($" + Math.Round(dayPnl) + ")"; }
                else                             { status = "Trading";     allowed = true;  reason = null; }

                SentinelCore.SetGovernorState(new SentinelCore.GovernorState
                {
                    Account = a.Name, Status = status, Allowed = allowed, Reason = reason,
                    DailyPnl = dayPnl, Cap = gc.Cap, LossStop = gc.LossStop, RecommendedSize = 1.0
                });

                // HARD ENFORCEMENT (v1.1.0, opt-in per profile hardEnforce=true; default OFF): at the
                // daily LOSS STOP, auto-flatten the account ONCE and lock out (the governor already blocks
                // new entries). Flatten is an EXIT, so it's always permitted. NOT done at DayComplete — a
                // trader who hit their target may keep an open winner; only the loss stop is protective.
                if (status == "DayHalted" && gc.HardEnforce && !_hardFlattened.Contains(a.Name))
                {
                    _hardFlattened.Add(a.Name);
                    SentinelCore.Log("Risk", "HARD ENFORCE ▶ " + a.Name + " loss stop hit — AUTO-FLATTEN + lockout (" + reason + ")");
                    try { SentinelCore.Ledger.Action("hard-flatten", a.Name, reason); } catch (Exception _sx) { SentinelCore.Swallow("SentinelRisk.GovernorTick", _sx); }
                    try { SentinelCore.Alerts.Critical("AUTO-FLATTEN " + a.Name, reason, a.Name); } catch (Exception _sx) { SentinelCore.Swallow("SentinelRisk.GovernorTick", _sx); }
                    try { HardFlattenAccount(a); } catch (Exception ex) { SentinelCore.Log("Risk", "auto-flatten error (" + a.Name + "): " + ex.Message); }
                }

                // governor TRANSITION alerts (fire once per change, not every tick)
                string prev; _govPrevStatus.TryGetValue(a.Name, out prev);
                if (status != prev)
                {
                    _govPrevStatus[a.Name] = status;
                    try {
                        if (status == "DayHalted")        SentinelCore.Alerts.Critical("Daily loss stop — " + a.Name, reason, a.Name);
                        else if (status == "DayComplete") SentinelCore.Alerts.Info("Daily target reached — " + a.Name, reason, a.Name);
                    } catch (Exception _sx) { SentinelCore.Swallow("SentinelRisk.GovernorTick", _sx); }
                }

                // RECONCILIATION (v1.1.0): a GOVERNED (prop) account should never hold a position with no
                // protective stop — the classic post-disconnect "orphaned stop" hazard. DETECT + ALERT
                // (never auto-act — a wrong autonomous cancel is worse). Scoped to governed accounts to
                // stay quiet; fires once per naked position, clears when a stop appears or it goes flat.
                try { ReconcileAccount(a); } catch (Exception _sx) { SentinelCore.Swallow("SentinelRisk.GovernorTick", _sx); }

                // TRAILING DRAWDOWN (v1.0.7): the peak-equity floor the firm liquidates at — the governor's
                // daily-realized gate can't see it (it fires on OPEN P&L against a lifetime high-water mark).
                try { DrawdownTick(a, gc); } catch (Exception ex) { SentinelCore.Log("Risk", "dd-tick error (" + a.Name + "): " + ex.Message); }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  TRAILING-DRAWDOWN TRACKER (v1.0.7) — completes AccountProfile.DdAmount ("Risk trailing-DD, future").
        //  Equity = realized balance (CashValue) + OPEN P&L. Peak = the high-water mark of Equity, PERSISTED
        //  via SentinelCore.State so a restart never loses it (losing the peak would mis-place the floor —
        //  dangerous). Floor = Peak - DdAmount for trailing/eod; for "static" the floor is pinned at the
        //  first-seen (start) equity minus DdAmount and never trails. Cushion = Equity - Floor.
        //    • EntryBlocked when the cushion is inside the entry buffer (stop ADDING risk near the floor) —
        //      CanEnter consults this via SentinelCore.DrawdownAllowsEntry (exits are never blocked).
        //    • Breach (cushion ≤ flat buffer): if hardEnforce is armed, auto-flatten ONCE — a hair ABOVE
        //      the floor (ddFlat=) so our market order beats the firm's own liquidation engine.
        //  NOTE (eod): a pure end-of-day-trailing firm ratchets the floor only at day close; we ratchet off
        //  intraday balance, which can only place the floor HIGHER (more conservative → safe). TODO: ratchet
        //  eod strictly at the day-roll boundary. Trailing (Apex/TPT/most, the dangerous case) is exact.
        // ─────────────────────────────────────────────────────────────────────
        private void DrawdownTick(Account a, GovConfig gc)
        {
            if (a == null || a.Name == null || gc == null || gc.DdAmount <= 0) return;   // no trailing DD configured → nothing to track (fail-open)

            double bal, uPnl;
            try
            {
                bal  = a.Get(AccountItem.CashValue, Currency.UsDollar);
                uPnl = a.Get(AccountItem.UnrealizedProfitLoss, Currency.UsDollar);
            }
            catch { return; }
            double equity = bal + uPnl;

            string ddType = string.IsNullOrEmpty(gc.DdType) ? "trailing" : gc.DdType;
            // "static" firms don't trail: the floor is pinned at (start equity - DdAmount). eod trails off
            // the daily balance; trailing trails off intraday equity (incl. open P&L). We ratchet the peak
            // off `candidate` and persist it.
            double candidate = ddType == "eod" ? bal : equity;
            double peak = DdPeak(a.Name, candidate);
            if (candidate > peak) { peak = candidate; DdSetPeak(a.Name, peak); }

            double floor = (ddType == "static")
                ? DdStaticFloor(a.Name, candidate, gc.DdAmount)   // pinned at first-seen equity - DdAmount
                : peak - gc.DdAmount;                             // trailing / eod

            double cushion  = equity - floor;
            double warnBuf  = gc.DdAmount * 0.35;                 // amber when within 35% of the threshold
            double entryBuf = gc.DdAmount * 0.20;                 // stop opening new risk within 20% of the floor
            double flatBuf  = Math.Max(0, gc.DdFlatBuffer);       // auto-flatten this many $ ABOVE the floor

            bool warn    = cushion <= warnBuf;
            bool blocked = cushion <= entryBuf;
            bool breach  = cushion <= flatBuf;

            SentinelCore.SetDrawdownState(new SentinelCore.DrawdownState
            {
                Account = a.Name, DdType = ddType, Equity = equity, PeakEquity = peak,
                Floor = floor, Cushion = cushion, DdAmount = gc.DdAmount,
                Warn = warn, EntryBlocked = blocked || breach, Breach = breach,
                Reason = breach ? "at trailing-DD floor" : (blocked ? "cushion thin" : (warn ? "approaching floor" : null))
            });

            // zone-transition alert (fire once per crossing, not every tick)
            string zone = breach ? "breach" : (blocked ? "block" : (warn ? "warn" : "ok"));
            string prevZone; _ddZone.TryGetValue(a.Name, out prevZone);
            if (zone != prevZone)
            {
                _ddZone[a.Name] = zone;
                try
                {
                    string msg = "cushion $" + Math.Round(cushion) + " to floor $" + Math.Round(floor) + " (peak $" + Math.Round(peak) + ", DD $" + Math.Round(gc.DdAmount) + ")";
                    if (zone == "breach" || zone == "block") SentinelCore.Alerts.Critical("Trailing DD " + (zone == "breach" ? "BREACH" : "thin") + " — " + a.Name, msg, a.Name);
                    else if (zone == "warn")                 SentinelCore.Alerts.Info("Trailing DD approaching — " + a.Name, msg, a.Name);
                }
                catch (Exception _sx) { SentinelCore.Swallow("SentinelRisk.DdStaticFloor", _sx); }
            }

            // hard-flatten opt-in: at/through the flatten line, get flat ONCE (an exit is always allowed).
            if (breach && gc.HardEnforce && !_ddFlattened.Contains(a.Name) && !_hardFlattened.Contains(a.Name))
            {
                _ddFlattened.Add(a.Name);
                string reason = "trailing-DD floor — cushion $" + Math.Round(cushion) + " (floor $" + Math.Round(floor) + ", peak $" + Math.Round(peak) + ")";
                SentinelCore.Log("Risk", "HARD ENFORCE ▶ " + a.Name + " " + reason + " — AUTO-FLATTEN");
                try { SentinelCore.Ledger.Action("dd-flatten", a.Name, reason); } catch (Exception _sx) { SentinelCore.Swallow("SentinelRisk.DdStaticFloor", _sx); }
                try { SentinelCore.Alerts.Critical("AUTO-FLATTEN (trailing DD) " + a.Name, reason, a.Name); } catch (Exception _sx) { SentinelCore.Swallow("SentinelRisk.DdStaticFloor", _sx); }
                try { HardFlattenAccount(a); } catch (Exception ex) { SentinelCore.Log("Risk", "dd auto-flatten error (" + a.Name + "): " + ex.Message); }
            }
        }

        // peak-equity high-water, cached in-memory + PERSISTED via SentinelCore.State (survives restart).
        // seed = current candidate on first sight (so a fresh account starts its trail at "now").
        private double DdPeak(string account, double seedIfNew)
        {
            double v;
            if (_ddPeak.TryGetValue(account, out v)) return v;
            v = seedIfNew;
            string s = SentinelCore.State.Load("dd-peak-" + account);
            double loaded;
            if (!string.IsNullOrEmpty(s) && double.TryParse(s.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out loaded))
                v = Math.Max(loaded, seedIfNew);   // never regress below the persisted high-water mark
            _ddPeak[account] = v;
            SentinelCore.State.Save("dd-peak-" + account, v.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return v;
        }
        private void DdSetPeak(string account, double peak)
        {
            _ddPeak[account] = peak;
            SentinelCore.State.Save("dd-peak-" + account, peak.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        // static (non-trailing) floor: pinned once at (first-seen equity - DdAmount), persisted.
        private double DdStaticFloor(string account, double firstSeenEquity, double ddAmount)
        {
            string s = SentinelCore.State.Load("dd-sfloor-" + account);
            double f;
            if (!string.IsNullOrEmpty(s) && double.TryParse(s.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out f))
                return f;
            f = firstSeenEquity - ddAmount;
            SentinelCore.State.Save("dd-sfloor-" + account, f.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return f;
        }

        // governor daily-P&L baseline (realized at day-start), PERSISTED so a mid-day restart/F5 doesn't zero
        // the day. Stored as "<yyyy-MM-dd>|<baseline>"; a stale (other-day) value is ignored → recaptured.
        private bool TryLoadGovBaseline(string account, DateTime tradingDay, out double baseline)
        {
            baseline = 0;
            try
            {
                string s = SentinelCore.State.Load("gov-baseline-" + account);
                if (string.IsNullOrEmpty(s)) return false;
                int bar = s.IndexOf('|');
                if (bar <= 0) return false;
                if (s.Substring(0, bar) != tradingDay.ToString("yyyy-MM-dd")) return false;   // stale (previous trading day)
                return double.TryParse(s.Substring(bar + 1).Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out baseline);
            }
            catch { return false; }
        }
        private void SaveGovBaseline(string account, DateTime tradingDay, double baseline)
        {
            try { SentinelCore.State.Save("gov-baseline-" + account, tradingDay.ToString("yyyy-MM-dd") + "|" + baseline.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelRisk.SaveGovBaseline", _sx); }
        }

        // Cancel every working order + market-close every position on an account. Off the tick path.
        private void HardFlattenAccount(Account acct)
        {
            if (acct == null) return;
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var work = acct.Orders.Where(o => o != null && (o.OrderState == OrderState.Working
                        || o.OrderState == OrderState.Accepted || o.OrderState == OrderState.PartFilled)).ToArray();
                    if (work.Length > 0) acct.Cancel(work);
                    System.Threading.Thread.Sleep(60);
                    for (int attempt = 0; attempt < 6; attempt++)
                    {
                        var open = acct.Positions.Where(p => p != null && p.Quantity != 0 && p.Instrument != null).ToList();
                        if (open.Count == 0) break;
                        foreach (var p in open)
                        {
                            var act = p.MarketPosition == MarketPosition.Long ? OrderAction.Sell : OrderAction.BuyToCover;
                            var o = acct.CreateOrder(p.Instrument, act, OrderType.Market, OrderEntry.Automated, TimeInForce.Day,
                                Math.Abs(p.Quantity), 0, 0, string.Empty, "SentinelHardFlatten", Core.Globals.MaxDate, null);
                            acct.Submit(new[] { o });
                        }
                        System.Threading.Thread.Sleep(250);
                    }
                    SentinelCore.Log("Risk", "HARD ENFORCE ▶ " + acct.Name + " auto-flatten complete.");
                }
                catch (Exception ex) { SentinelCore.Log("Risk", "auto-flatten failed (" + acct.Name + "): " + ex.Message); }
            });
        }

        // Detect + ALERT dangerous state on a governed account (never auto-cancels — detection only):
        //  • a position with NO working protective stop (the post-disconnect orphaned-stop hazard)
        //  • working orders sitting on a FLAT account (orphan)
        //
        // v1.0.11 — both are CONDITION ALERTS, routed through SentinelCore.Conditions. They used to share one
        // HashSet and got OPPOSITE bugs from it:
        //   • NAKED had no debounce. A stop order transits ChangePending/ChangeSubmitted during every modify, and
        //     GTrader21 TRAILS its stop — so on each trail step the stop left the "working" set for a moment, this
        //     2-second scan saw no stop, and fired a CRITICAL. 160 false NAKED POSITION alerts sit in the Ledger,
        //     same account + instrument, 20-60s apart. The alert you most need to trust was crying wolf.
        //   • ORPHAN's latch was DELETED on every scan by the naked-flag sweep below (its key starts with
        //     "<acct>|" but is never an instrument name, so it never survived `held`), making `Add` return true
        //     each time → an Info alert every 2 seconds for as long as the condition held.
        // Now: a pending-modify stop still COUNTS as a stop, naked must persist NakedDebounceSec before it speaks,
        // and each condition owns a distinct key that auto-clears on resolve and re-states on a cooldown.
        private const double NakedDebounceSec  = 10.0;   // > any stop-modify round trip, << any real exposure
        private const double NakedRestateSec   = 300.0;  // a live naked position must keep saying so
        private const double OrphanDebounceSec = 10.0;
        private const double OrphanRestateSec  = 900.0;

        private void ReconcileAccount(Account acct)
        {
            if (acct == null || acct.Name == null) return;
            List<Position> positions; List<Order> orders;
            try { positions = acct.Positions.ToList(); orders = acct.Orders.ToList(); }
            catch { return; }

            var working = orders.Where(o => o != null && o.Instrument != null && IsLive(o.OrderState)).ToList();

            // naked position: open pos with no live stop for that instrument
            foreach (var p in positions)
            {
                if (p == null || p.Quantity == 0 || p.Instrument == null) continue;
                bool hasStop = working.Any(o => o.Instrument.FullName == p.Instrument.FullName
                    && (o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit));

                string key = "risk|naked|" + acct.Name + "|" + p.Instrument.FullName;
                _nakedKeys.Add(key);
                if (SentinelCore.Conditions.ShouldReport(key, !hasStop, NakedDebounceSec, NakedRestateSec))
                {
                    int secs = (int)SentinelCore.Conditions.ActiveFor(key).TotalSeconds;
                    try { SentinelCore.Alerts.Critical("NAKED POSITION — " + acct.Name,
                        p.MarketPosition + " " + Math.Abs(p.Quantity) + " " + p.Instrument.FullName
                        + " has had no protective stop for " + secs + "s", acct.Name); } catch (Exception _sx) { SentinelCore.Swallow("SentinelRisk.ReconcileAccount", _sx); }
                }
            }

            // A position that went FLAT must have its episode ended explicitly. Nothing iterates it above any more,
            // so ShouldReport(false) is never called for it and the episode would linger as "already reported" —
            // suppressing the NEXT genuine naked position on that instrument for a whole cooldown.
            var held = new HashSet<string>(positions.Where(p => p != null && p.Quantity != 0 && p.Instrument != null)
                .Select(p => "risk|naked|" + acct.Name + "|" + p.Instrument.FullName), StringComparer.OrdinalIgnoreCase);
            foreach (var gone in NakedKeysFor(acct.Name).Where(k => !held.Contains(k)))
            {
                SentinelCore.Conditions.Clear(gone);
                _nakedKeys.Remove(gone);
            }

            // orphan orders: live orders while the account is flat everywhere
            bool anyPos = positions.Any(p => p != null && p.Quantity != 0);
            bool orphan = !anyPos && working.Count > 0;
            string okey = "risk|orphan|" + acct.Name;
            if (SentinelCore.Conditions.ShouldReport(okey, orphan, OrphanDebounceSec, OrphanRestateSec))
                try { SentinelCore.Alerts.Info("Orphan orders — " + acct.Name,
                    working.Count + " working order(s) with no open position", acct.Name); } catch (Exception _sx) { SentinelCore.Swallow("SentinelRisk.ReconcileAccount", _sx); }
        }

        /// <summary>An order is LIVE (still protecting you) in these states. Crucially this includes the pending-
        /// modify states: a stop mid-trail-move is still a stop, and treating it as absent is what produced 160
        /// false NAKED POSITION criticals. Only a filled/cancelled/rejected order has truly stopped protecting.</summary>
        private static bool IsLive(OrderState st)
        {
            return st == OrderState.Working        || st == OrderState.Accepted
                || st == OrderState.PartFilled     || st == OrderState.TriggerPending
                || st == OrderState.ChangePending  || st == OrderState.ChangeSubmitted
                || st == OrderState.Submitted;
        }

        /// <summary>The naked-condition keys this service has ever opened for an account, so a flat instrument's
        /// episode can be ended. Tracked here because Conditions deliberately exposes no enumeration.</summary>
        private readonly HashSet<string> _nakedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private IEnumerable<string> NakedKeysFor(string account)
        {
            string prefix = "risk|naked|" + account + "|";
            return _nakedKeys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        /// <summary>Manual "Re-request feeds" (dashboard button): release + re-subscribe every monitored
        /// feed now. Best-effort data re-request; returns how many feeds were re-requested.</summary>
        public int ReRequestAllFeeds()
        {
            var feeds = new List<Feed>();
            lock (_lock) { if (_feeds != null) feeds.AddRange(_feeds.Values); }
            foreach (var f in feeds)
            {
                try { ReleaseFeed(f); SubscribeFeed(f); f.LastTickWall = NinjaTrader.Core.Globals.Now; f.RecoverAttempts = 0; }
                catch (Exception _sx) { SentinelCore.Swallow("SentinelRisk.ReRequestAllFeeds", _sx); }
            }
            SentinelCore.Log("Risk", "manual re-request: re-subscribed " + feeds.Count + " feed(s).");
            return feeds.Count;
        }

        // ── market-data tick: stamp the feed's last-tick time (verified: e.Time) ──
        private void OnMarketTick(object sender, MarketDataEventArgs e)
        {
            if (_stopping) return;
            try
            {
                if (e == null) return;
                if (e.MarketDataType != MarketDataType.Last
                    && e.MarketDataType != MarketDataType.Bid
                    && e.MarketDataType != MarketDataType.Ask) return;
                lock (_lock)
                {
                    if (_feeds == null) return;
                    foreach (var f in _feeds.Values)
                        if (ReferenceEquals(f.Md, sender))
                        {
                            f.LastTickTime = e.Time;
                            f.LastTickWall = NinjaTrader.Core.Globals.Now;
                            f.GotTick = true;
                            break;
                        }
                }
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelRisk.OnMarketTick", _sx); }
        }

        private void SubscribeFeed(Feed f)
        {
            try { f.Md = new MarketData(f.Instr); f.Md.Update += OnMarketTick; f.Subscribed = true; }
            catch (Exception ex) { f.Subscribed = false; SentinelCore.Log("Risk", "subscribe failed " + Fn(f.Instr) + ": " + ex.Message); }
        }

        private void ReleaseFeed(Feed f)
        {
            if (f == null || f.Md == null) return;
            if (f.Subscribed) { try { f.Md.Update -= OnMarketTick; } catch (Exception _sx) { SentinelCore.Swallow("SentinelRisk.ReleaseFeed", _sx); } f.Subscribed = false; }
            f.Md = null;
        }

        // Monitored set = instruments with an OPEN POSITION on any account  ∪  instruments a chart
        // strategy REGISTERED via SentinelCore.RegisterWatchInstrument (so a FLAT leader's chart feed
        // is still watched). heldKeys receives the FullNames that are held (the rest are watch-only).
        private static Dictionary<string, Instrument> ActiveInstruments(out HashSet<string> heldKeys)
        {
            var d = new Dictionary<string, Instrument>();
            heldKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                lock (Account.All)
                {
                    foreach (Account a in Account.All)
                    {
                        try
                        {
                            foreach (Position p in a.Positions)
                                if (p != null && p.MarketPosition != MarketPosition.Flat && p.Instrument != null)
                                { d[p.Instrument.FullName] = p.Instrument; heldKeys.Add(p.Instrument.FullName); }
                        }
                        catch (Exception _sx) { SentinelCore.Swallow("SentinelRisk.ReleaseFeed", _sx); }
                    }
                }
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelRisk.ReleaseFeed", _sx); }
            // union registered watch instruments (flat or not)
            try
            {
                foreach (Instrument w in SentinelCore.WatchedInstruments())
                {
                    if (w == null) continue;
                    string k; try { k = w.FullName; } catch { continue; }
                    if (!string.IsNullOrEmpty(k) && !d.ContainsKey(k)) d[k] = w;
                }
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelRisk.ReleaseFeed", _sx); }
            return d;
        }

        private static string Fn(Instrument i) { try { return i != null ? i.FullName : "?"; } catch { return "?"; } }

        // v1.0.4: instrument ROOT for scoped kills — MasterInstrument.Name ("GC"), fallback to the
        // root of the full contract name. Property reads only (safe to call under _lock).
        private static string RootOf(Instrument i)
        {
            try { if (i != null && i.MasterInstrument != null && !string.IsNullOrEmpty(i.MasterInstrument.Name)) return i.MasterInstrument.Name; } catch (Exception _sx) { SentinelCore.Swallow("SentinelRisk.RootOf", _sx); }
            try { return SentinelCore.InstrumentRoot(Fn(i)); } catch { return ""; }
        }

        // ── snapshot for the Risk dashboard tab ──────────────────────────────────
        public sealed class FeedHealth
        {
            public string Instrument;
            public double LagSec, StallSec;
            public bool   GotTick;
            public bool   Healthy;
            public bool   FromWatch;        // watched via registry (vs. held position)
            public int    RecoverAttempts;  // recovery re-requests fired this stall
        }

        public sealed class RollRow
        {
            public string   Root, Contract;
            public double   Days;
            public DateTime RollDate;
            public bool     Blocked, Warn;
        }

        public sealed class RiskSnapshot
        {
            public List<FeedHealth> Feeds = new List<FeedHealth>();
            public List<string>     Connections = new List<string>();
            public bool   KillEngaged;
            public bool   KillByRisk;
            public double MaxLag, MaxStall;
            public bool   AutoKill;
            // v1.0.3 additions
            public List<RollRow> Rollovers = new List<RollRow>();
            public List<string>  NewsActive = new List<string>();
            public string        NewsNext;
            // v1.0.4: per-instrument scoped kills currently engaged ("GC — Risk: lag 3.4s on GC 08-26")
            public List<string>  InstrumentKills = new List<string>();
        }

        public RiskSnapshot GetSnapshot()
        {
            var s = new RiskSnapshot
            {
                KillEngaged = SentinelCore.KillSwitchEngaged,
                KillByRisk  = _weKilledRoots.Count > 0,
                MaxLag      = MaxLagSeconds,
                MaxStall    = MaxStallSeconds,
                AutoKill    = AutoKillOnBreach,
                NewsNext    = _newsNext
            };
            // NON-BLOCKING: the State-service writer calls this every 2s. Never block it on _lock —
            // if Risk is briefly busy, return a partial snapshot rather than hanging the caller.
            bool got = Monitor.TryEnter(_lock, 50);
            try
            {
                if (!got) { s.Connections.Add("(risk busy — snapshot skipped this tick)"); }
                else
                {
                    if (_feeds != null)
                        foreach (var f in _feeds.Values)
                            s.Feeds.Add(new FeedHealth
                            {
                                Instrument = Fn(f.Instr),
                                LagSec = f.LagSec, StallSec = f.StallSec, GotTick = f.GotTick,
                                FromWatch = f.FromWatch, RecoverAttempts = f.RecoverAttempts,
                                Healthy = !f.GotTick || (f.LagSec <= MaxLagSeconds && f.StallSec <= MaxStallSeconds)
                            });
                    if (_connStatus != null)
                        foreach (var kv in _connStatus) s.Connections.Add(kv.Key + ": " + kv.Value);
                }
            }
            finally { if (got) Monitor.Exit(_lock); }

            // rollover + news read their own Core registries (no _lock needed)
            foreach (var r in SentinelCore.AllRollovers())
                if (r != null) s.Rollovers.Add(new RollRow {
                    Root = r.Root, Contract = r.Contract, Days = r.DaysToRoll,
                    RollDate = r.RollDateLocal, Blocked = r.Blocked, Warn = r.Warn });
            s.Rollovers.Sort((a, b) => a.Days.CompareTo(b.Days));

            foreach (var n in SentinelCore.ActiveNewsLockouts())
                if (n != null)
                    s.NewsActive.Add(n.Event + "  "
                        + n.StartLocal.ToString("HH:mm") + "–" + n.EndLocal.ToString("HH:mm")
                        + "  [" + (n.Scope == null || n.Scope.Length == 0 ? "all" : string.Join(",", n.Scope)) + "]");

            // v1.0.4: per-instrument scoped kills currently engaged (root — reason)
            foreach (var kv in SentinelCore.AllInstrumentKills())
                s.InstrumentKills.Add(kv.Key + " — " + kv.Value);
            s.InstrumentKills.Sort(StringComparer.OrdinalIgnoreCase);
            return s;
        }
    }
}
