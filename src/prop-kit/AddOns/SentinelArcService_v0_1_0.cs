// ─────────────────────────────────────────────────────────────────────────────
// This Source Code Form is subject to the terms of the Mozilla Public License,
// v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
// one at https://mozilla.org/MPL/2.0/.
//
// Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
// ─────────────────────────────────────────────────────────────────────────────
// ═════════════════════════════════════════════════════════════════════════════
//  SentinelArcService — fleet orchestration for the Sentinel Suite (NT8)
//  File: SentinelArcService_v0_1_0.cs
//  Version: v0.1.0
// ─────────────────────────────────────────────────────────────────────────────
//  WHAT THIS IS  (see memory: sentinel-suite-architecture, profit-plan-and-accounts)
//    The suite's ORCHESTRATION tool — the manager of the leader "signal engine" fleet.
//    A NinjaScript AddOn CANNOT start/stop a chart strategy (same platform limit that
//    made Eye a chart indicator). So Arc orchestrates the Sentinel way — publish/consult:
//
//      • Arc PUBLISHES a per-instrument FLEET PLAN to SentinelCore (enable, size, session
//        window) from Arc.conf / the dashboard.
//      • Sentinel-aware STRATEGIES (GTrader21, once wired in v0.2.0) CONSULT SlotLive() at
//        entry time and only trade when their slot is live. You load the strategy on each
//        chart ONCE; Arc controls which instruments trade, and when, from one place.
//      • Arc also SUPERVISES the leader: per-slot position, day PnL, fills-today, last
//        signal, and a health verdict (OFF / CLOSED / IDLE / LIVE / DARK). It's the
//        watchdog for the TOP of the funnel (Risk watches feeds; Copy fans out).
//
//    This v0.1.0 does the PLAN + SUPERVISION halves (fully testable headless via
//    sentinel.log). The CONTROL half lands when GTrader21 consults SlotLive() (v0.2.0) —
//    until then the plan is published + honored by any strategy that opts in, and Arc
//    reports the fleet's live status regardless.
//
//  ARC.CONF (in <UserDataDir>\Sentinel\, hand-editable):
//    leader=Sim101
//    slot=GC|GTrader21|on|1|24h
//    slot=NQ|GTrader21|off|1|0830-1500          (session HHMM-HHMM in NT clock time; 24h = always)
//
//  VERIFIED APIs: Account.All / Account.Name / Account.Connection.Status; Account.Positions +
//    Position.{MarketPosition,Quantity,Instrument}; Position.GetUnrealizedProfitLoss(Currency);
//    Account.ExecutionUpdate += (ExecutionEventArgs e) => e.Execution.{Instrument,Time} (copier).
//
//  CHANGELOG
//    v0.1.0 — initial: Arc.conf fleet plan → SentinelCore fleet registry (publish); leader
//             ExecutionUpdate subscription for fills-today/last-signal; 3s supervision tick
//             computing InSession + position + unrealized + health per slot; logs on health
//             change + a 30s heartbeat. Headless singleton. GTrader21 consult-gate = v0.2.0.
// ═════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;

namespace NinjaTrader.NinjaScript.AddOns.Sentinel
{
    public class SentinelArcService_v0_1_0 : NinjaTrader.NinjaScript.AddOnBase
    {
        public static SentinelArcService_v0_1_0 Instance { get; private set; }

        // ── config model ─────────────────────────────────────────────────────────
        public sealed class SlotConfig
        {
            public string Instrument;      // master name, e.g. "GC"
            public string Strategy;        // label, e.g. "GTrader21"
            public bool   Enabled;
            public int    Contracts;       // 0 = let the strategy decide
            public int    SessionStartMin; // minutes-of-day; -1 = 24h
            public int    SessionEndMin;   // minutes-of-day; -1 = 24h
        }
        public sealed class ArcConfig
        {
            public string Leader = "";
            public List<SlotConfig> Slots = new List<SlotConfig>();
        }

        private ArcConfig _config = new ArcConfig();
        private Account   _leaderAcct;
        private bool      _execSubscribed;
        private readonly Dictionary<string, int>      _fillsToday = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _lastSignal = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string>   _lastHealth = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private DateTime  _dayKey;
        private DateTime  _lastHeartbeatUtc;
        private Timer     _timer;
        private bool      _started;
        private volatile bool _stopping;
        private int       _ticking;
        private readonly object _lock = new object();

        public static string ConfFile { get { return Path.Combine(SentinelCore.SettingsDir, "Arc.conf"); } }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "SentinelArcService";
                Description = "Sentinel Suite — fleet orchestration. Publishes a per-instrument fleet plan "
                            + "(enable/size/session) that Sentinel-aware strategies consult, and supervises "
                            + "the leader's signal engine. Runs always.";
            }
            else if (State == State.Active)     Start();
            else if (State == State.Terminated) Stop();
        }

        // ── lifecycle ──────────────────────────────────────────────────────────────
        private void Start()
        {
            if (_started) return;
            _stopping = false; _started = true; Instance = this;
            _dayKey = SafeNow().Date;
            LoadConfig();
            PublishAll();   // publish the plan immediately so consults work before the first tick
            _timer = new Timer(OnTimer, null, 1500, 3000);
            SentinelCore.Log("Arc", "SentinelArcService started. Leader='" + _config.Leader
                + "', slots=" + _config.Slots.Count + ".");
        }

        private void Stop()
        {
            if (!_started) return;
            _stopping = true; _started = false;
            UnsubscribeExec();
            if (_timer != null)
            {
                try { var done = new ManualResetEvent(false); if (_timer.Dispose(done)) done.WaitOne(500); done.Close(); }
                catch { try { _timer.Dispose(); } catch (Exception _sx) { SentinelCore.Swallow("SentinelArc.Stop", _sx); } }
                _timer = null;
            }
            try { foreach (var s in _config.Slots) SentinelCore.RemoveFleetSlot(s.Instrument); } catch (Exception _sx) { SentinelCore.Swallow("SentinelArc.Stop", _sx); }
            if (Instance == this) Instance = null;
            SentinelCore.Log("Arc", "SentinelArcService stopped.");
        }

        // ── 3s supervision tick ─────────────────────────────────────────────────────
        private void OnTimer(object _)
        {
            if (_stopping || !_started) return;
            if (Interlocked.CompareExchange(ref _ticking, 1, 0) != 0) return;
            try
            {
                DateTime now = SafeNow();
                if (now.Date != _dayKey) { lock (_lock) { _fillsToday.Clear(); } _dayKey = now.Date; }

                EnsureLeaderSubscribed();

                int nowMin = (int)now.TimeOfDay.TotalMinutes;
                List<SlotConfig> slots; string leaderName;
                lock (_lock) { slots = _config.Slots.ToList(); leaderName = _config.Leader; }

                Account acct = _leaderAcct;
                bool connected = false;
                try { connected = acct != null && acct.Connection != null && acct.Connection.Status == ConnectionStatus.Connected; }
                catch (Exception _sx) { SentinelCore.Swallow("SentinelArc.OnTimer", _sx); }

                // snapshot the leader's positions ONCE per tick — avoids a "collection modified"
                // throw mid-iterate (which would transiently report flat); mirrors the State service.
                var acctPos = new List<Position>();
                try { if (acct != null) foreach (Position p in acct.Positions) if (p != null) acctPos.Add(p); }
                catch (Exception _sx) { SentinelCore.Swallow("SentinelArc.OnTimer", _sx); }

                foreach (var sc in slots)
                {
                    bool inSession = InSession(nowMin, sc.SessionStartMin, sc.SessionEndMin);

                    int posQty = 0; double unreal = 0;
                    foreach (var p in acctPos)
                        if (p.Instrument != null && p.MarketPosition != MarketPosition.Flat
                            && string.Equals(p.Instrument.MasterInstrument.Name, sc.Instrument, StringComparison.OrdinalIgnoreCase))
                        {
                            posQty = p.MarketPosition == MarketPosition.Long ? p.Quantity : -p.Quantity;
                            try { unreal = p.GetUnrealizedProfitLoss(PerformanceUnit.Currency); } catch (Exception _sx) { SentinelCore.Swallow("SentinelArc.OnTimer", _sx); }
                            break;
                        }

                    int fills; DateTime lastSig;
                    lock (_lock)
                    {
                        _fillsToday.TryGetValue(sc.Instrument, out fills);
                        _lastSignal.TryGetValue(sc.Instrument, out lastSig);
                    }

                    string health = ComputeHealth(sc.Enabled, inSession, connected, posQty);

                    SentinelCore.PublishFleetSlot(new SentinelCore.FleetSlot
                    {
                        Instrument = sc.Instrument, Strategy = sc.Strategy, Enabled = sc.Enabled,
                        Contracts = sc.Contracts, SessionStartMin = sc.SessionStartMin, SessionEndMin = sc.SessionEndMin,
                        InSession = inSession, FillsToday = fills, PositionQty = posQty, DayPnl = unreal,
                        LastSignalUtc = lastSig, Health = health
                    });

                    string prev;
                    lock (_lock) { _lastHealth.TryGetValue(sc.Instrument, out prev); _lastHealth[sc.Instrument] = health; }
                    // The FIRST observation used to be dropped (`prev != null`), so a slot that came up UNHEALTHY
                    // said nothing until it happened to change. Announce the initial state too — silence on first
                    // observation is the same bug as silence after a latch.
                    if (prev == null)
                        SentinelCore.Log("Arc", "slot " + sc.Instrument + "  initial " + health
                            + "  (pos " + posQty + ", fills " + fills + ")");
                    else if (prev != health)
                        SentinelCore.Log("Arc", "slot " + sc.Instrument + "  " + prev + " → " + health
                            + "  (pos " + posQty + ", fills " + fills + ")");
                }

                if ((now.ToUniversalTime() - _lastHeartbeatUtc).TotalSeconds >= 30)
                {
                    _lastHeartbeatUtc = now.ToUniversalTime();
                    SentinelCore.Log("Arc", "fleet [" + leaderName + (connected ? " ✓" : " ✗conn") + "]  " + FleetSummary(slots, nowMin));
                }
            }
            catch (Exception ex) { SentinelCore.Log("Arc", "tick error: " + ex.Message); }
            finally { Interlocked.Exchange(ref _ticking, 0); }
        }

        // health: honest verdict from what Arc can actually observe
        private static string ComputeHealth(bool enabled, bool inSession, bool connected, int posQty)
        {
            if (!enabled)   return "OFF";
            if (!connected) return "DARK";    // enabled but the leader can't trade at all
            if (!inSession) return "CLOSED";  // enabled but outside the session window (expected quiet)
            if (posQty != 0) return "LIVE";   // actively in a position
            return "IDLE";                    // enabled, in session, flat — waiting for a signal
        }

        // ── leader execution subscription (fills-today + last-signal) ────────────────
        private void EnsureLeaderSubscribed()
        {
            string want;
            lock (_lock) { want = _config.Leader; }
            if (string.IsNullOrEmpty(want)) return;
            if (_leaderAcct != null && _execSubscribed
                && string.Equals(_leaderAcct.Name, want, StringComparison.OrdinalIgnoreCase)) return;

            Account found = null;
            try { lock (Account.All) { foreach (Account a in Account.All) if (a != null && string.Equals(a.Name, want, StringComparison.OrdinalIgnoreCase)) { found = a; break; } } }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelArc.EnsureLeaderSubscribed", _sx); }
            if (found == null) return;

            if (!ReferenceEquals(found, _leaderAcct)) { UnsubscribeExec(); _leaderAcct = found; }
            if (!_execSubscribed)
            {
                try { _leaderAcct.ExecutionUpdate += OnExecution; _execSubscribed = true;
                      SentinelCore.Log("Arc", "subscribed to leader '" + _leaderAcct.Name + "' executions."); }
                catch (Exception ex) { SentinelCore.Log("Arc", "exec subscribe failed: " + ex.Message); }
            }
        }

        private void UnsubscribeExec()
        {
            if (_leaderAcct != null && _execSubscribed) { try { _leaderAcct.ExecutionUpdate -= OnExecution; } catch (Exception _sx) { SentinelCore.Swallow("SentinelArc.UnsubscribeExec", _sx); } }
            _execSubscribed = false;
        }

        private void OnExecution(object sender, ExecutionEventArgs e)
        {
            if (_stopping) return;
            try
            {
                if (e == null || e.Execution == null || e.Execution.Instrument == null) return;
                string inst = e.Execution.Instrument.MasterInstrument.Name;
                bool tracked;
                lock (_lock) { tracked = _config.Slots.Any(s => string.Equals(s.Instrument, inst, StringComparison.OrdinalIgnoreCase)); }
                if (!tracked) return;
                lock (_lock)
                {
                    int c; _fillsToday.TryGetValue(inst, out c); _fillsToday[inst] = c + 1;
                    _lastSignal[inst] = SafeNow().ToUniversalTime();
                }
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelArc.OnExecution", _sx); }
        }

        // ── config load / save / reconfigure ─────────────────────────────────────────
        public void LoadConfig()
        {
            var cfg = new ArcConfig();
            try
            {
                if (File.Exists(ConfFile))
                    foreach (var raw in File.ReadAllLines(ConfFile))
                    {
                        string line = raw.Trim();
                        if (line.Length == 0 || line[0] == '#') continue;
                        if (line.StartsWith("leader=", StringComparison.OrdinalIgnoreCase))
                            cfg.Leader = line.Substring(7).Trim();
                        else if (line.StartsWith("slot=", StringComparison.OrdinalIgnoreCase))
                        {
                            var sc = ParseSlot(line.Substring(5).Trim());
                            if (sc != null) cfg.Slots.Add(sc);
                        }
                    }
            }
            catch (Exception ex) { SentinelCore.Log("Arc", "config load error: " + ex.Message); }
            lock (_lock) { _config = cfg; }
        }

        public void SaveConfig()
        {
            try
            {
                ArcConfig cfg; lock (_lock) { cfg = _config; }
                var sb = new StringBuilder();
                sb.AppendLine("# Sentinel Arc — fleet orchestration plan. Safe to hand-edit.");
                sb.AppendLine("# slot=<instrument>|<strategy>|on/off|<contracts>|<HHMM-HHMM or 24h>");
                sb.AppendLine("leader=" + (cfg.Leader ?? ""));
                foreach (var s in cfg.Slots)
                    sb.AppendLine("slot=" + s.Instrument + "|" + (s.Strategy ?? "GTrader21") + "|"
                        + (s.Enabled ? "on" : "off") + "|" + s.Contracts + "|" + SessionText(s.SessionStartMin, s.SessionEndMin));
                Directory.CreateDirectory(SentinelCore.SettingsDir);
                File.WriteAllText(ConfFile, sb.ToString());
            }
            catch (Exception ex) { SentinelCore.Log("Arc", "config save error: " + ex.Message); }
        }

        /// <summary>Apply a new plan from the dashboard: swap config, republish, persist.</summary>
        public void Reconfigure(ArcConfig cfg)
        {
            if (cfg == null) return;
            lock (_lock) { _config = cfg; }
            PublishAll();
            SaveConfig();
            SentinelCore.Log("Arc", "Reconfigured. Leader='" + cfg.Leader + "', slots=" + cfg.Slots.Count + ".");
        }

        /// <summary>Re-read Arc.conf from disk and republish (dashboard Reload button).</summary>
        public void ReloadFromFile()
        {
            LoadConfig();
            PublishAll();
            ArcConfig c; lock (_lock) { c = _config; }
            SentinelCore.Log("Arc", "Reloaded Arc.conf. Leader='" + c.Leader + "', slots=" + c.Slots.Count + ".");
        }

        /// <summary>Deep copy of the live config for the dashboard to edit.</summary>
        public ArcConfig CurrentConfig()
        {
            lock (_lock)
            {
                var c = new ArcConfig { Leader = _config.Leader };
                foreach (var s in _config.Slots)
                    c.Slots.Add(new SlotConfig { Instrument = s.Instrument, Strategy = s.Strategy, Enabled = s.Enabled,
                        Contracts = s.Contracts, SessionStartMin = s.SessionStartMin, SessionEndMin = s.SessionEndMin });
                return c;
            }
        }

        // publish the plan (no live status yet) so consults work; prune Core slots we dropped
        private void PublishAll()
        {
            int nowMin = (int)SafeNow().TimeOfDay.TotalMinutes;
            List<SlotConfig> slots; lock (_lock) { slots = _config.Slots.ToList(); }
            foreach (var sc in slots)
                SentinelCore.PublishFleetSlot(new SentinelCore.FleetSlot
                {
                    Instrument = sc.Instrument, Strategy = sc.Strategy, Enabled = sc.Enabled, Contracts = sc.Contracts,
                    SessionStartMin = sc.SessionStartMin, SessionEndMin = sc.SessionEndMin,
                    InSession = InSession(nowMin, sc.SessionStartMin, sc.SessionEndMin),
                    Health = sc.Enabled ? "IDLE" : "OFF"
                });
            try
            {
                var keep = new HashSet<string>(slots.Select(s => s.Instrument), StringComparer.OrdinalIgnoreCase);
                foreach (var fs in SentinelCore.AllFleetSlots())
                    if (fs != null && !keep.Contains(fs.Instrument)) SentinelCore.RemoveFleetSlot(fs.Instrument);
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelArc.PublishAll", _sx); }
        }

        // ── helpers ──────────────────────────────────────────────────────────────────
        private static DateTime SafeNow()
        {
            try { return NinjaTrader.Core.Globals.Now; } catch { return DateTime.Now; }
        }

        private static bool InSession(int nowMin, int startMin, int endMin)
        {
            if (startMin < 0 || endMin < 0 || startMin == endMin) return true;   // 24h
            if (startMin < endMin) return nowMin >= startMin && nowMin < endMin;
            return nowMin >= startMin || nowMin < endMin;                        // wraps midnight
        }

        private string FleetSummary(List<SlotConfig> slots, int nowMin)
        {
            if (slots.Count == 0) return "(no slots)";
            var parts = new List<string>();
            foreach (var s in slots)
            {
                int fills; lock (_lock) { _fillsToday.TryGetValue(s.Instrument, out fills); }
                string prev; lock (_lock) { _lastHealth.TryGetValue(s.Instrument, out prev); }
                parts.Add(s.Instrument + "=" + (prev ?? (s.Enabled ? "IDLE" : "OFF")) + "/" + fills + "f");
            }
            return string.Join("  ", parts);
        }

        private static SlotConfig ParseSlot(string s)
        {
            var p = s.Split('|');
            if (p.Length < 1 || string.IsNullOrWhiteSpace(p[0])) return null;
            var sc = new SlotConfig
            {
                Instrument = p[0].Trim(),
                Strategy   = p.Length > 1 && !string.IsNullOrWhiteSpace(p[1]) ? p[1].Trim() : "GTrader21",
                Enabled    = p.Length > 2 && IsOn(p[2]),
                Contracts  = p.Length > 3 ? ParseInt(p[3], 0) : 0,
                SessionStartMin = -1,
                SessionEndMin   = -1
            };
            if (p.Length > 4)
            {
                int st, en;
                ParseSession(p[4].Trim(), out st, out en);
                sc.SessionStartMin = st; sc.SessionEndMin = en;
            }
            return sc;
        }

        private static bool IsOn(string s)
        {
            s = (s ?? "").Trim();
            return s.Equals("on", StringComparison.OrdinalIgnoreCase) || s == "1"
                || s.Equals("true", StringComparison.OrdinalIgnoreCase) || s.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        private static int ParseInt(string s, int fallback)
        {
            int v; return int.TryParse((s ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        // "24h"/""/"0" -> (-1,-1); "0830-1500" -> (510, 900)
        private static void ParseSession(string s, out int startMin, out int endMin)
        {
            startMin = -1; endMin = -1;
            if (string.IsNullOrWhiteSpace(s)) return;
            if (s.Equals("24h", StringComparison.OrdinalIgnoreCase) || s == "0") return;
            int dash = s.IndexOf('-');
            if (dash <= 0) return;
            int a = HhmmToMin(s.Substring(0, dash));
            int b = HhmmToMin(s.Substring(dash + 1));
            if (a >= 0 && b >= 0) { startMin = a; endMin = b; }
        }

        private static int HhmmToMin(string hhmm)
        {
            hhmm = (hhmm ?? "").Trim();
            if (hhmm.Length < 3 || hhmm.Length > 4) return -1;
            int v; if (!int.TryParse(hhmm, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return -1;
            int h = v / 100, m = v % 100;
            if (h < 0 || h > 23 || m < 0 || m > 59) return -1;
            return h * 60 + m;
        }

        public static string SessionText(int startMin, int endMin)
        {
            if (startMin < 0 || endMin < 0) return "24h";
            return MinToHhmm(startMin) + "-" + MinToHhmm(endMin);
        }

        private static string MinToHhmm(int min)
        {
            if (min < 0) min = 0;
            return (min / 60).ToString("00", CultureInfo.InvariantCulture) + (min % 60).ToString("00", CultureInfo.InvariantCulture);
        }
    }
}
