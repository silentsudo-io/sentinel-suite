// ─────────────────────────────────────────────────────────────────────────────
// This Source Code Form is subject to the terms of the Mozilla Public License,
// v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
// one at https://mozilla.org/MPL/2.0/.
//
// Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
// ─────────────────────────────────────────────────────────────────────────────
// ═════════════════════════════════════════════════════════════════════════════
//  SentinelLogService — headless tier-1 zero-touch MAE/MFE capture (NT8 AddOn)
//  File: SentinelLogService_v1_0_0.cs
//  Service version: v1.0.0   (pairs with SentinelLogEngine schema 1.0)
// ─────────────────────────────────────────────────────────────────────────────
//  WHAT THIS IS
//    A headless, always-on AddOn service. It runs as a singleton for the lifetime of
//    the NinjaTrader process (AddOnBase auto-starts at platform launch / recompile).
//    NO WINDOW — the future dashboard window attaches to this service; closing a window
//    must never stop logging. (Architecture decision: service runs always.)
//
//  WHAT IT DOES (tier-1, zero-touch — spec §2)
//    For accounts/strategies the trader has enabled for logging, it logs MAE/MFE WITHOUT
//    any change to those strategies:
//      • Subscribes to every Account's PositionUpdate (and ExecutionUpdate for fills).
//      • On a position going flat -> open, opens an SentinelLogEngine record (tier 1, no ctx) and
//        starts a raw-tick MarketData subscription for that instrument.
//      • Each incoming tick updates running raw MAE/MFE against the entry price.
//      • On open -> flat, finalizes the engine record (one JSONL line) and releases the
//        market-data subscription.
//
//  WHY RAW TICKS (verified, spec §11.2)
//    BarsRequest data "cannot be used as input for an indicator" and isn't synchronized to
//    a strategy's series, and subfolders/bar-type matching is brittle. So tier-1 measures
//    RAW-TICK high/low excursion against entry — universal, faithful, no bar-type coupling.
//    (Tier-2 strategies still supply bar/HA excursion + ctx from inside themselves.)
//
//  VERIFIED API NOTES (NT8 help guide + forum; see spec §11)
//    • AddOnBase auto-starts as a singleton; OnStateChange fires for Active/Terminated.
//      GOTCHA: the base constructor calls OnStateChange BEFORE the subclass constructor —
//      so we DO NOT rely on field initializers; we lazily init inside OnStateChange.
//    • Position is authoritative only inside PositionUpdate (Account.Positions can be stale
//      inside ExecutionUpdate). Event order (exec vs position) is NOT guaranteed.
//    • Flat is signaled by Operation.Remove on the position update.
//    • MarketData (Level-1) updates arrive on a BACKGROUND thread — this service does no UI,
//      so no Dispatcher needed here; a future dashboard marshals to its own UI thread.
//    • MUST unsubscribe all events + dispose all MarketData in Terminated (leak otherwise).
//
//  STILL TO VALIDATE ON FIRST RUN (commented inline as VALIDATE:)
//    • Exact MarketData class surface / event arg property names in this NT8 build.
//    • Account.Name availability + which accounts to enumerate at Active.
//    • That entry price from PositionUpdate.AveragePrice is the right basis vs first exec.
//
//  CHANGELOG
//    sentinel-rebrand (2026-07-01) — MAECaptureService → SentinelLogService; namespace
//             MAELogging → Sentinel (now part of the Sentinel Suite). Logging routes through
//             SentinelCore.Log (Output window + Sentinel\sentinel.log). Uses SentinelLogEngine.
//             The old MAE* files were ARCHIVED out of bin\Custom; the monitor is now the Suite's
//             "Log" tab (standalone MAEDashboard window removed). No capture-logic change.
//    v1.0.4 — unified open-position registry (dashboard shows BOTH tiers).
//      - Subscribes SentinelLogEngine.OnEngineTradeOpened/Closed so tier-2 strategies that log
//        themselves now appear in GetOpenSnapshots() alongside tier-1 captures. The empty-
//        dashboard-while-tier2-strategies-run gap is closed. OpenSnapshot gains Tier +
//        Strategy so the dashboard can distinguish/zero-touch vs rich. Reference-keyed
//        registry; unsubscribed on Stop.
//    v1.0.3 — dashboard support surface (no logging-path changes).
//      - Added read-only live-state API for the dashboard window: GetOpenSnapshots()
//        returns a snapshot list of currently-open tracked positions (account, instrument,
//        dir, entry, running MAE/MFE ticks, last price). TradeClosed event fires on each
//        close so the window can refresh. Track now mirrors running MAE/MFE + last price
//        for display (engine still owns the authoritative logged values).
//    v1.0.2 — FILL-PRICE FIX (first live-data findings).
//      - BUG: every tier-1 trade logged entryPx == exitPx, pnlTicks == 0. Root cause:
//        PositionUpdate.AveragePrice reports the ENTRY price even on the closing/flat
//        update (verified NT8 behavior), so using it for the exit was always wrong.
//      - FIX: source fill prices from EXECUTIONS (e.Execution.Price), which are
//        authoritative. OnExecutionUpdate now records the last fill per account+instrument;
//        the position handler uses that for exit (and entry, falling back to position avg
//        only if no execution seen yet — safe because on the OPEN update the position avg
//        IS the entry price; the bug is exit-only).
//      - Clear last-fill on flat so it cannot leak into the next trade on that instrument.
//      - NOTE: the large entry-to-first-tick gap also seen in early data should shrink now
//        that entry uses the true fill; VALIDATE on next run that MAE/MFE look sane.
//    v1.0.1 — compile fix + teardown hardening (first-compile findings).
//      - MarketData has NO Dispose() (unlike BarsRequest); release is just
//        `Update -= handler`. Removed the erroneous Dispose() call (CS1061).
//      - Guard unsubscribe behind an MdSubscribed flag: unsubscribing a MarketData
//        Update event that was never subscribed throws an NRE in remove_Update
//        (documented NT8 behavior). Now we only detach if attach succeeded.
//    v1.0.0 — initial headless tier-1 capture service. Account subscription + raw-tick
//             excursion + SentinelLogEngine feed. No UI. Logging enabled per-account via the
//             EnabledAccounts set (default: log all sim accounts; live opt-in).
// ═════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.AddOns.Sentinel;   // shared SentinelLogEngine

namespace NinjaTrader.NinjaScript.AddOns.Sentinel
{
    public class SentinelLogService : NinjaTrader.NinjaScript.AddOnBase
    {
        // ── singleton handle so a future dashboard window can attach to the service ──
        public static SentinelLogService Instance { get; private set; }

        // per-open-position tracking context (one per live position we're logging)
        private sealed class Track
        {
            public SentinelLogEngine    Engine;
            public MarketData   Md;
            public bool         MdSubscribed;  // true only after Update += succeeded
            public Instrument   Instrument;
            public double       EntryPx;
            public int          Dir;          // +1 long / -1 short
            public DateTime     EntryTimeUtc;
            public double       Tick;
            public int          BarApprox;    // synthetic offset (tick-driven; no bars here)
            public double       RunMaeTicks;  // dashboard mirror of running adverse excursion
            public double       RunMfeTicks;  // dashboard mirror of running favorable excursion
            public double       LastPx;       // last seen trade price
        }

        // key positions by (account|instrument) — unique per open position we track
        private Dictionary<string, Track> _tracks;
        // last execution (fill) price per (account|instrument) — the AUTHORITATIVE fill
        // price source. Position updates report entry price even on the close, so we must
        // read exit (and entry) fills from executions, not from PositionUpdate.AveragePrice.
        private Dictionary<string, double> _lastFillPx;
        private List<Account> _subscribed;
        private bool _started;

        // which accounts to log (tier-1). Default policy applied at startup; a future
        // dashboard toggles membership live. Kept here so the service owns the truth.
        private HashSet<string> _enabledAccounts;

        // registry of tier-2 engines with an OPEN trade (strategies log themselves through
        // their own SentinelLogEngine; they register here via the engine's static hooks so the
        // dashboard can show tier-2 live trades alongside tier-1 captures). Reference-keyed.
        private readonly HashSet<SentinelLogEngine> _tier2Open = new HashSet<SentinelLogEngine>();
        private readonly object _t2Lock = new object();

        protected override void OnStateChange()
        {
            // NOTE: base ctor calls this BEFORE our ctor — init lazily, never assume fields.
            if (State == State.SetDefaults)
            {
                Name = "SentinelLogService";
                Description = "Headless tier-1 zero-touch MAE/MFE capture. Logs MAE/MFE for "
                            + "enabled accounts without instrumenting strategies. Runs always.";
            }
            else if (State == State.Active)
            {
                Start();
            }
            else if (State == State.Terminated)
            {
                Stop();
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Start — subscribe to all accounts. Idempotent (Active can re-fire).
        // ─────────────────────────────────────────────────────────────────────
        private void Start()
        {
            if (_started) return;
            _started = true;
            Instance = this;

            _tracks          = new Dictionary<string, Track>();
            _lastFillPx      = new Dictionary<string, double>();
            _subscribed      = new List<Account>();
            _enabledAccounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Default logging policy: enable all sim accounts; live accounts opt-in via
            // the dashboard later. (Conservative: don't silently log live trading.)
            lock (Account.All)
            {
                foreach (Account a in Account.All)
                {
                    // VALIDATE: Account.Provider / connection naming for "sim". Using the
                    // documented Simulation flag if present; fall back to name contains "Sim".
                    bool isSim = a.Name != null && a.Name.IndexOf("Sim", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (isSim) _enabledAccounts.Add(a.Name);

                    a.PositionUpdate  += OnPositionUpdate;
                    a.ExecutionUpdate += OnExecutionUpdate;
                    _subscribed.Add(a);
                }
            }

            Log("SentinelLogService started; subscribed to " + _subscribed.Count + " account(s).");

            // subscribe to tier-2 engine lifecycle so strategies that log themselves still
            // appear in the dashboard. Decoupled: the engine fires these if anyone listens.
            SentinelLogEngine.OnEngineTradeOpened += OnTier2Opened;
            SentinelLogEngine.OnEngineTradeClosed += OnTier2Closed;
        }

        private void OnTier2Opened(SentinelLogEngine eng)
        {
            if (eng == null) return;
            lock (_t2Lock) { _tier2Open.Add(eng); }
        }

        private void OnTier2Closed(SentinelLogEngine eng)
        {
            if (eng == null) return;
            lock (_t2Lock) { _tier2Open.Remove(eng); }
            var handler = TradeClosed;
            if (handler != null) { try { handler(); } catch (Exception _sx) { SentinelCore.Swallow("SentinelLogService.OnTier2Closed", _sx); } }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Stop — unsubscribe everything, dispose all live market-data subs.
        // ─────────────────────────────────────────────────────────────────────
        private void Stop()
        {
            if (!_started) return;
            _started = false;

            SentinelLogEngine.OnEngineTradeOpened -= OnTier2Opened;
            SentinelLogEngine.OnEngineTradeClosed -= OnTier2Closed;
            lock (_t2Lock) { _tier2Open.Clear(); }

            if (_subscribed != null)
            {
                foreach (Account a in _subscribed)
                {
                    a.PositionUpdate  -= OnPositionUpdate;
                    a.ExecutionUpdate -= OnExecutionUpdate;
                }
                _subscribed.Clear();
            }

            if (_tracks != null)
            {
                foreach (var kv in _tracks)
                    SafeReleaseMarketData(kv.Value);
                _tracks.Clear();
            }

            if (Instance == this) Instance = null;
            Log("SentinelLogService stopped; all subscriptions released.");
        }

        // ── dashboard-facing toggles (used later by the window; safe no-UI here) ──
        public void EnableAccount(string accountName)
        {
            if (_enabledAccounts != null && accountName != null)
                _enabledAccounts.Add(accountName);
        }
        public void DisableAccount(string accountName)
        {
            if (_enabledAccounts != null && accountName != null)
                _enabledAccounts.Remove(accountName);
        }
        public IEnumerable<string> EnabledAccounts
        {
            get { return _enabledAccounts != null ? new List<string>(_enabledAccounts) : new List<string>(); }
        }

        // ── live-state surface for the dashboard (read-only snapshots) ────────────
        // The window polls/refreshes these via its own Dispatcher; the service never
        // touches UI. All access is snapshot-copied to avoid handing out live mutable
        // collections that background threads are modifying.

        public sealed class OpenSnapshot
        {
            public string Account;
            public string Instrument;
            public string Strategy = "ZeroTouch";  // tier-1 default; tier-2 sets real name
            public int    Tier = 1;                 // 1 = zero-touch, 2 = rich
            public int    Dir;          // +1 / -1
            public double EntryPx;
            public double MaeTicks;     // running adverse (ticks)
            public double MfeTicks;     // running favorable (ticks)
            public double LastPx;
            public DateTime EntryTimeUtc;
        }

        // raised whenever a trade closes (so the dashboard can refresh closed-trade views)
        public event Action TradeClosed;

        // snapshot of all currently-open tracked positions
        public List<OpenSnapshot> GetOpenSnapshots()
        {
            var list = new List<OpenSnapshot>();
            var tracks = _tracks;
            if (tracks == null) return list;
            // copy under no lock is acceptable for a glanceable dashboard; values are
            // doubles/refs updated atomically enough for display. (Not used for logging.)
            foreach (var kv in tracks)
            {
                var t = kv.Value;
                if (t == null) continue;
                list.Add(new OpenSnapshot
                {
                    Account      = SplitAccount(kv.Key),
                    Instrument   = t.Instrument != null ? t.Instrument.MasterInstrument.Name : "?",
                    Dir          = t.Dir,
                    EntryPx      = t.EntryPx,
                    MaeTicks     = t.RunMaeTicks,
                    MfeTicks     = t.RunMfeTicks,
                    LastPx       = t.LastPx,
                    EntryTimeUtc = t.EntryTimeUtc
                });
            }

            // union tier-2 strategy engines that currently have an open trade
            lock (_t2Lock)
            {
                foreach (var eng in _tier2Open)
                {
                    if (eng == null || !eng.TradeOpen) continue;
                    list.Add(new OpenSnapshot
                    {
                        Account      = eng.LiveAccount,
                        Instrument   = eng.LiveInstrument,
                        Dir          = eng.LiveDir,
                        EntryPx      = eng.LiveEntryPx,
                        MaeTicks     = eng.LiveMaeTicks,
                        MfeTicks     = eng.LiveMfeTicks,
                        LastPx       = eng.LiveLastPx,
                        EntryTimeUtc = eng.LiveEntryTimeUtc,
                        Tier         = eng.LiveTier,
                        Strategy     = eng.LiveStrategy
                    });
                }
            }
            return list;
        }

        private static string SplitAccount(string key)
        {
            int i = key != null ? key.IndexOf('|') : -1;
            return i > 0 ? key.Substring(0, i) : (key ?? "?");
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Position lifecycle — the trade-boundary signal. Position is authoritative
        //  here (NOT inside ExecutionUpdate). Flat == Operation.Remove (verified).
        // ─────────────────────────────────────────────────────────────────────
        private void OnPositionUpdate(object sender, PositionEventArgs e)
        {
            try
            {
                Account acct = e.Position != null ? e.Position.Account : null;
                if (acct == null || acct.Name == null) return;
                if (_enabledAccounts == null || !_enabledAccounts.Contains(acct.Name)) return;

                Instrument instr = e.Position.Instrument;
                if (instr == null) return;
                string key = acct.Name + "|" + instr.FullName;

                bool isFlat = e.MarketPosition == MarketPosition.Flat;

                if (!isFlat)
                {
                    // flat -> open : begin tracking if we aren't already
                    if (!_tracks.ContainsKey(key))
                        BeginTrack(key, acct, instr, e);
                    // (Re-entry / scale handled minimally for v1: we track the first open
                    //  until flat. VALIDATE: decide later if scale-ins should re-baseline.)
                }
                else
                {
                    // open -> flat : finalize + release
                    Track t;
                    if (_tracks.TryGetValue(key, out t))
                    {
                        // Exit price from the LAST EXECUTION fill (authoritative), NOT from
                        // e.AveragePrice (which reports entry price on the flat update).
                        double exitPx;
                        if (!_lastFillPx.TryGetValue(key, out exitPx) || exitPx <= 0)
                            exitPx = t.EntryPx; // fallback if no fill seen (shouldn't happen)

                        // exitReason coarse for tier-1 (spec §3.2): we can't see WHY a
                        // strategy exited from account events alone.
                        if (t.Engine != null)
                            t.Engine.OnExit(exitPx, DateTime.UtcNow, "unknown");
                        SafeReleaseMarketData(t);
                        _tracks.Remove(key);
                        _lastFillPx.Remove(key);  // clear so it can't leak into next trade

                        var handler = TradeClosed;   // notify dashboard (if attached)
                        if (handler != null) { try { handler(); } catch (Exception _sx) { SentinelCore.Swallow("SentinelLogService.OnPositionUpdate", _sx); } }
                    }
                }
            }
            catch (Exception ex)
            {
                Log("OnPositionUpdate error: " + ex.Message);
            }
        }

        // Executions carry the AUTHORITATIVE fill price (e.Execution.Price). We record the
        // latest fill per account+instrument so the position handler can use the true entry
        // and exit fill prices instead of PositionUpdate.AveragePrice (which reports the
        // entry price even on the closing/flat update — verified NT8 behavior).
        private void OnExecutionUpdate(object sender, ExecutionEventArgs e)
        {
            try
            {
                if (e.Execution == null) return;
                Account acct = e.Execution.Account;
                Instrument instr = e.Execution.Instrument;
                if (acct == null || acct.Name == null || instr == null) return;
                string key = acct.Name + "|" + instr.FullName;
                _lastFillPx[key] = e.Execution.Price;
            }
            catch (Exception ex)
            {
                Log("OnExecutionUpdate error: " + ex.Message);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  BeginTrack — open an engine record (tier-1, no ctx) and start raw-tick feed.
        // ─────────────────────────────────────────────────────────────────────
        private void BeginTrack(string key, Account acct, Instrument instr, PositionEventArgs e)
        {
            var t = new Track();
            t.Instrument   = instr;
            // Entry price: prefer the last execution fill (authoritative); fall back to the
            // position update's AveragePrice if no execution recorded yet (event-order safe).
            double entryFill;
            if (!_lastFillPx.TryGetValue(key, out entryFill) || entryFill <= 0)
                entryFill = e.AveragePrice;
            t.EntryPx      = entryFill;
            t.Dir          = e.MarketPosition == MarketPosition.Long ? 1 : -1;
            t.EntryTimeUtc = DateTime.UtcNow;
            t.Tick         = instr.MasterInstrument.TickSize > 0 ? instr.MasterInstrument.TickSize : 0.1;
            t.BarApprox    = 0;

            // tier-1 engine instance: price-only. strategy name unknown from account events,
            // so we tag it generically; instanceId keys off account+instrument.
            string instanceId = "t1_" + acct.Name + "_" + instr.MasterInstrument.Name;
            t.Engine = new SentinelLogEngine(
                acct.Name, "ZeroTouch", "t1", instanceId,
                instr.MasterInstrument.Name, /*tier*/ 1,
                /*paramsJson*/ null, /*paramHash*/ null,
                /*logDirectory*/ null,
                /*pathSampling*/ true, /*stride*/ 1, /*maxSamples*/ 800,
                msg => Log(msg));

            // no ATR available zero-touch -> NaN; no ctx -> null
            t.Engine.OnEntry(t.Dir, Math.Abs(e.Quantity), t.EntryPx, t.EntryTimeUtc,
                t.Tick, double.NaN, null);

            // start raw-tick market data for excursion tracking.
            // VALIDATE: exact MarketData ctor + Update event signature in this build.
            try
            {
                t.Md = new MarketData(instr);
                t.Md.Update += OnMarketData;
                t.MdSubscribed = true;   // only now is unsubscribe safe (avoids NRE on teardown)
            }
            catch (Exception ex)
            {
                t.MdSubscribed = false;
                Log("MarketData subscribe failed for " + instr.FullName + ": " + ex.Message);
            }

            _tracks[key] = t;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Market data tick -> feed the engine a synthetic bar sample. We approximate a
        //  "bar" by the last trade price (hi=lo=c=last) so the engine's high/low excursion
        //  math still runs against true traded prices. Engine tracks the running MAE/MFE.
        //  VALIDATE: MarketDataEventArgs property names (MarketDataType.Last, .Price).
        // ─────────────────────────────────────────────────────────────────────
        private void OnMarketData(object sender, MarketDataEventArgs e)
        {
            try
            {
                if (e.MarketDataType != MarketDataType.Last) return; // trades only
                double px = e.Price;
                if (double.IsNaN(px) || px <= 0) return;

                // find the track owning this MarketData instance
                Track t = null;
                foreach (var kv in _tracks)
                {
                    if (ReferenceEquals(kv.Value.Md, sender)) { t = kv.Value; break; }
                }
                if (t == null || t.Engine == null) return;

                // synthetic per-tick "bar": hi=lo=c=last. Engine derives raw MAE/MFE vs entry.
                t.BarApprox++;
                t.Engine.OnBar(DateTime.UtcNow, t.BarApprox, px, px, px, double.NaN, null);

                // mirror running excursion for the dashboard (display-only; engine owns truth)
                t.LastPx = px;
                double adv = t.Dir > 0 ? (t.EntryPx - px) / t.Tick : (px - t.EntryPx) / t.Tick;
                double fav = t.Dir > 0 ? (px - t.EntryPx) / t.Tick : (t.EntryPx - px) / t.Tick;
                if (adv > t.RunMaeTicks) t.RunMaeTicks = adv;
                if (fav > t.RunMfeTicks) t.RunMfeTicks = fav;
            }
            catch (Exception ex)
            {
                Log("OnMarketData error: " + ex.Message);
            }
        }

        private void SafeReleaseMarketData(Track t)
        {
            if (t == null || t.Md == null) return;
            // MarketData has NO Dispose() (unlike BarsRequest). Release == unsubscribe the
            // Update event. CRITICAL: unsubscribing when we never subscribed throws an NRE
            // inside remove_Update (documented NT8 behavior), so guard on MdSubscribed.
            if (t.MdSubscribed)
            {
                try { t.Md.Update -= OnMarketData; }
                catch (Exception ex) { Log("MarketData unsubscribe warn: " + ex.Message); }
                t.MdSubscribed = false;
            }
            t.Md = null;
        }

        private void Log(string msg)
        {
            // Route through the shared Sentinel core → Output window AND Sentinel\sentinel.log.
            SentinelCore.Log("Log", msg);
        }
    }
}
