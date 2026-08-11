// ─────────────────────────────────────────────────────────────────────────────
// This Source Code Form is subject to the terms of the Mozilla Public License,
// v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
// one at https://mozilla.org/MPL/2.0/.
//
// Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
// ─────────────────────────────────────────────────────────────────────────────
// ═════════════════════════════════════════════════════════════════════════════
//  SentinelStateService — periodic state snapshot for the Sentinel Suite (NT8)
//  File: SentinelStateService_v1_0_0.cs
//  Version: v1.0.0
// ─────────────────────────────────────────────────────────────────────────────
//  WHAT THIS IS  (see memory: ninjatrader-observability, sentinel-suite-architecture)
//    A headless, always-on AddOnBase service that writes a live snapshot of NinjaTrader
//    state to a file every couple seconds:
//        <UserDataDir>\Sentinel\state.json
//    So the current picture (accounts / positions / working orders / P&L / kill-switch /
//    copier config) is READABLE without a screenshot — the Positions/Orders/Accounts panels
//    become one file. Complements NT's own log\log.*.txt (event stream) and Sentinel\sentinel.log
//    (our Output text). Written atomically (tmp → move) so a reader never sees a half file.
//
//  KEEPS THE FILE SMALL among 169 accounts: only accounts WITH a position or a working order
//  are detailed; the rest are summarized as counts (total / connected).
//
//  VERIFIED APIs (in-repo): acct.Get(AccountItem.Realized/UnrealizedProfitLoss, Currency.UsDollar)
//    (a pulled predecessor panel), position.GetUnrealizedProfitLoss(PerformanceUnit.Currency)
//    (@UnrealizedProfitLoss.cs:73), Account.Connection.Status/.Options.Name, Account.All.
//
//  CHANGELOG
//    v1.0.7 — added the "profiles" block (per-account profile: firm/ratio/target/cap/dailyLoss/size/
//             contracts/session from SentinelCore's AccountProfile registry) — readable outside NT.
//    v1.0.6 — added the "governor" block (per-account daily cap/loss state from SentinelCore) and a
//             THROTTLED "eyeReferee" block (per-signal Eye verdict +1/-1/0 from SentinelExcursions,
//             recomputed every 5 min since it parses the excursion files — empty until Eye accrues).
//    v1.0.5 — added the "configs" block (AppendConfigs): which running the Bridge instance auto-read
//             which lab .conf (strategy/instrument/account/config/tp/sl/ageSec) from SentinelCore's
//             config-use registry. Also surfaced the risk block's per-instrument SCOPED kills
//             ("instrumentKills") — so "GC halted, ES/NQ fine" is readable outside NT.
//    v1.0.4 — added the "arc" block (AppendArc): SentinelArc fleet plan + live supervision (leader,
//             per-slot instrument/strategy/enabled/inSession/session/health/posQty/dayPnl/fillsToday/
//             lastSignalAgeSec) from SentinelCore's fleet registry. Fleet status now readable in
//             state.json, not just sentinel.log heartbeats.
//    v1.0.3 — (REMOVED 2026-08-11) an "eye" block: per-instrument godTrades qualification verdicts
//             (instrument/direction/score/source/ageSec) from SentinelCore's Eye registry.
//    v1.0.2 — teardown hardening: `_stopping` flag set FIRST in Stop() so the 2s timer callback
//             bails instantly during NT recompile/teardown (was a plausible compile-hang cause —
//             a threadpool callback touching Account.All while NT disposes AddOns). Timer now
//             DRAINED on dispose (bounded 500ms wait). No functional change to the snapshot.
//    v1.0.1 — added the "risk" block (AppendRisk): Sentinel Risk feed lag/stall, connections, and
//             kill state now surface in state.json (readable, not just the Risk tab).
//    v1.0.0 — initial: 2s timer snapshot of kill-switch, copier config, account summary, and
//             per-active-account positions/orders + P&L. Manual JSON (no serializer dep). Atomic
//             write. Reads account collections defensively (try/catch; glance snapshot, not audit).
// ═════════════════════════════════════════════════════════════════════════════

using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.AddOns.SentinelCopier;   // copier config for the snapshot

namespace NinjaTrader.NinjaScript.AddOns.Sentinel
{
    public class SentinelStateService_v1_0_0 : NinjaTrader.NinjaScript.AddOnBase
    {
        public static SentinelStateService_v1_0_0 Instance { get; private set; }

        private Timer _timer;
        private readonly object _writeLock = new object();
        private bool _started;
        private volatile bool _stopping;   // set FIRST in Stop() so timer callbacks bail during teardown

        private static string StatePath { get { return Path.Combine(SentinelCore.SettingsDir, "state.json"); } }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "SentinelStateService";
                Description = "Sentinel Suite — writes a live state snapshot to Sentinel\\state.json "
                            + "(accounts/positions/orders/P&L/kill-switch/copier) every 2s. Runs always.";
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
            // fire first snapshot after 1.5s (let accounts populate), then every 2s.
            _timer = new Timer(OnTick, null, 1500, 2000);
            SentinelCore.Log("State", "SentinelStateService started → " + StatePath);
        }

        private void Stop()
        {
            if (!_started) return;
            _stopping = true;   // next/in-flight timer callbacks bail before NT tears down its objects
            _started = false;
            if (_timer != null)
            {
                // drain: signal when in-flight callbacks finish; wait a BOUNDED time (never block teardown)
                try { var done = new ManualResetEvent(false); if (_timer.Dispose(done)) done.WaitOne(500); done.Close(); }
                catch { try { _timer.Dispose(); } catch (Exception _sx) { SentinelCore.Swallow("SentinelState.Stop", _sx); } }
                _timer = null;
            }
            if (Instance == this) Instance = null;
            SentinelCore.Log("State", "SentinelStateService stopped.");
        }

        private void OnTick(object _)
        {
            if (_stopping || !_started) return;   // teardown guard: don't touch NT internals while stopping
            // guard against overlap if a write ever runs long
            if (!Monitor.TryEnter(_writeLock)) return;
            try { WriteState(); }
            catch (Exception ex) { SentinelCore.Log("State", "snapshot error: " + ex.Message); }
            finally { Monitor.Exit(_writeLock); }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Build + atomically write the snapshot.
        // ─────────────────────────────────────────────────────────────────────
        private void WriteState()
        {
            var sb = new StringBuilder(4096);
            sb.Append("{\n");
            sb.Append("  \"ts\": ").Append(Str(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))).Append(",\n");
            sb.Append("  \"killSwitch\": ").Append(SentinelCore.KillSwitchEngaged ? "true" : "false").Append(",\n");

            // ── copier config summary ──
            sb.Append("  \"copier\": ");
            AppendCopier(sb);
            sb.Append(",\n");

            // ── risk watchdog (feed lag/stall + connections + kill state) ──
            sb.Append("  \"risk\": ");
            AppendRisk(sb);
            sb.Append(",\n");


            // ── arc fleet (orchestration plan + live supervision from SentinelCore's fleet registry) ──
            sb.Append("  \"arc\": ");
            AppendArc(sb);
            sb.Append(",\n");

            // ── manual-assist tickets (place-by-hand queue for automation-restricted prop accounts) ──
            sb.Append("  \"assist\": ");
            AppendAssist(sb);
            sb.Append(",\n");

            // ── lab config-use (which running strategy instance auto-read which .conf) ──
            sb.Append("  \"configs\": ");
            AppendConfigs(sb);
            sb.Append(",\n");

            // ── account profiles (per-account firm/cap/loss/size/session config) ──
            sb.Append("  \"profiles\": ");
            AppendProfiles(sb);
            sb.Append(",\n");

            // ── consistency governor (per-account daily cap/loss state) ──
            sb.Append("  \"governor\": ");
            AppendGovernor(sb);
            sb.Append(",\n");


            // ── accounts ──
            int total = 0, connected = 0;
            var active = new StringBuilder();
            int activeCount = 0;

            System.Collections.Generic.List<Account> accts = SnapshotAccounts();
            foreach (Account a in accts)
            {
                total++;
                bool isConn = false;
                try { isConn = a.Connection != null && a.Connection.Status == ConnectionStatus.Connected; } catch (Exception _sx) { SentinelCore.Swallow("SentinelState.WriteState", _sx); }
                if (isConn) connected++;

                // detail only accounts that have a position or a working order (keeps file small)
                string detail = TryAccountDetail(a);
                if (detail != null)
                {
                    if (activeCount++ > 0) active.Append(",\n");
                    active.Append(detail);
                }
            }

            sb.Append("  \"accounts\": { \"total\": ").Append(total)
              .Append(", \"connected\": ").Append(connected).Append(" },\n");
            sb.Append("  \"activeAccounts\": [\n").Append(active).Append("\n  ]\n");
            sb.Append("}\n");

            AtomicWrite(StatePath, sb.ToString());
        }

        private void AppendProfiles(StringBuilder sb)
        {
            var ps = SentinelCore.AllAccountProfiles();
            sb.Append("[");
            int n = 0;
            foreach (var p in ps)
            {
                if (p == null) continue;
                if (n++ > 0) sb.Append(", ");
                double cap = p.Ratio * p.ProfitTarget;
                if (p.ManualDailyTarget > 0) cap = Math.Min(p.ManualDailyTarget, cap);
                sb.Append("{ \"account\": ").Append(Str(p.Account))
                  .Append(", \"firm\": ").Append(Str(p.Firm))
                  .Append(", \"ratio\": ").Append(Num(p.Ratio))
                  .Append(", \"target\": ").Append(Num(p.ProfitTarget))
                  .Append(", \"dailyCap\": ").Append(Num(cap))
                  .Append(", \"dailyLoss\": ").Append(Num(p.DailyLossStop))
                  .Append(", \"size\": ").Append(Num(p.SizeScale))
                  .Append(", \"contracts\": ").Append(p.ContractLimit)
                  .Append(", \"session\": ").Append(Str(p.Session))
                  .Append(" }");
            }
            sb.Append("]");
        }

        private void AppendGovernor(StringBuilder sb)
        {
            var states = SentinelCore.AllGovernorStates();
            sb.Append("[");
            int n = 0;
            foreach (var g in states)
            {
                if (g == null) continue;
                if (n++ > 0) sb.Append(", ");
                sb.Append("{ \"account\": ").Append(Str(g.Account))
                  .Append(", \"status\": ").Append(Str(g.Status))
                  .Append(", \"allowed\": ").Append(g.Allowed ? "true" : "false")
                  .Append(", \"dailyPnl\": ").Append(Num(g.DailyPnl))
                  .Append(", \"cap\": ").Append(Num(g.Cap))
                  .Append(", \"lossStop\": ").Append(Num(g.LossStop))
                  .Append(", \"reason\": ").Append(Str(g.Reason))
                  .Append(" }");
            }
            sb.Append("]");
        }




        private void AppendConfigs(StringBuilder sb)
        {
            var uses = SentinelCore.AllConfigUses();
            sb.Append("[");
            int n = 0;
            foreach (var c in uses)
            {
                if (c == null) continue;
                if (n++ > 0) sb.Append(", ");
                double ageSec = 0; try { ageSec = (DateTime.Now.ToUniversalTime() - c.UpdatedUtc).TotalSeconds; } catch (Exception _sx) { SentinelCore.Swallow("SentinelState.AppendConfigs", _sx); }
                sb.Append("{ \"strategy\": ").Append(Str(c.Strategy))
                  .Append(", \"instrument\": ").Append(Str(c.Instrument))
                  .Append(", \"account\": ").Append(Str(c.Account))
                  .Append(", \"config\": ").Append(Str(c.ConfigName))
                  .Append(", \"tp\": ").Append(c.Tp)
                  .Append(", \"sl\": ").Append(c.Sl)
                  .Append(", \"ageSec\": ").Append(Num(ageSec))
                  .Append(" }");
            }
            sb.Append("]");
        }


        private void AppendAssist(StringBuilder sb)
        {
            var tickets = SentinelCore.RecentAssistTickets(15);
            sb.Append("[");
            int n = 0;
            foreach (var t in tickets)
            {
                if (t == null) continue;
                if (n++ > 0) sb.Append(", ");
                double ageSec = 0; try { ageSec = (DateTime.Now.ToUniversalTime() - t.TimeUtc).TotalSeconds; } catch (Exception _sx) { SentinelCore.Swallow("SentinelState.AppendAssist", _sx); }
                sb.Append("{ \"account\": ").Append(Str(t.Account))
                  .Append(", \"action\": ").Append(Str(t.Action))
                  .Append(", \"qty\": ").Append(t.Qty)
                  .Append(", \"instrument\": ").Append(Str(t.Instrument))
                  .Append(", \"isEntry\": ").Append(t.IsEntry ? "true" : "false")
                  .Append(", \"context\": ").Append(Str(t.Context))
                  .Append(", \"ageSec\": ").Append(Num(ageSec))
                  .Append(" }");
            }
            sb.Append("]");
        }

        private void AppendArc(StringBuilder sb)
        {
            var svc = SentinelArcService_v0_1_0.Instance;
            if (svc == null) { sb.Append("{ \"running\": false }"); return; }
            string leader = null;
            try { var c = svc.CurrentConfig(); leader = c != null ? c.Leader : null; } catch (Exception _sx) { SentinelCore.Swallow("SentinelState.AppendArc", _sx); }
            var slots = SentinelCore.AllFleetSlots();
            sb.Append("{ \"running\": true, \"leader\": ").Append(Str(leader)).Append(", \"slots\": [");
            int n = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (s == null) continue;
                if (n++ > 0) sb.Append(", ");
                double ageSec = -1;
                try { if (s.LastSignalUtc > DateTime.MinValue) ageSec = (DateTime.UtcNow - s.LastSignalUtc).TotalSeconds; } catch (Exception _sx) { SentinelCore.Swallow("SentinelState.AppendArc", _sx); }
                sb.Append("{ \"instrument\": ").Append(Str(s.Instrument))
                  .Append(", \"strategy\": ").Append(Str(s.Strategy))
                  .Append(", \"enabled\": ").Append(s.Enabled ? "true" : "false")
                  .Append(", \"inSession\": ").Append(s.InSession ? "true" : "false")
                  .Append(", \"session\": ").Append(Str(SentinelArcService_v0_1_0.SessionText(s.SessionStartMin, s.SessionEndMin)))
                  .Append(", \"health\": ").Append(Str(s.Health))
                  .Append(", \"posQty\": ").Append(s.PositionQty)
                  .Append(", \"dayPnl\": ").Append(Num(s.DayPnl))
                  .Append(", \"fillsToday\": ").Append(s.FillsToday)
                  .Append(", \"lastSignalAgeSec\": ").Append(Num(ageSec))
                  .Append(" }");
            }
            sb.Append("] }");
        }

        private void AppendRisk(StringBuilder sb)
        {
            var svc = SentinelRiskService_v1_0_0.Instance;
            if (svc == null) { sb.Append("{ \"running\": false }"); return; }
            var s = svc.GetSnapshot();
            sb.Append("{ \"running\": true, \"autoKill\": ").Append(s.AutoKill ? "true" : "false")
              .Append(", \"killEngaged\": ").Append(s.KillEngaged ? "true" : "false")
              .Append(", \"killByRisk\": ").Append(s.KillByRisk ? "true" : "false")
              .Append(", \"maxLagSec\": ").Append(Num(s.MaxLag))
              .Append(", \"maxStallSec\": ").Append(Num(s.MaxStall))
              .Append(", \"feeds\": [");
            for (int i = 0; i < s.Feeds.Count; i++)
            {
                var f = s.Feeds[i];
                if (i > 0) sb.Append(", ");
                sb.Append("{ \"instrument\": ").Append(Str(f.Instrument))
                  .Append(", \"lagSec\": ").Append(Num(f.LagSec))
                  .Append(", \"stallSec\": ").Append(Num(f.StallSec))
                  .Append(", \"gotTick\": ").Append(f.GotTick ? "true" : "false")
                  .Append(", \"healthy\": ").Append(f.Healthy ? "true" : "false")
                  .Append(" }");
            }
            sb.Append("], \"connections\": [");
            for (int i = 0; i < s.Connections.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(Str(s.Connections[i]));
            }
            sb.Append("], \"instrumentKills\": [");   // v1.0.5: per-instrument scoped halts ("GC — Risk: lag 3.4s ...")
            for (int i = 0; i < s.InstrumentKills.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(Str(s.InstrumentKills[i]));
            }
            sb.Append("] }");
        }

        private void AppendCopier(StringBuilder sb)
        {
            var svc = SentinelCopierService_v0_2_1.Instance;
            CopierConfig cfg = svc != null ? svc.CurrentConfig : null;
            if (svc == null) { sb.Append("{ \"running\": false }"); return; }
            sb.Append("{ \"running\": true, \"leader\": ").Append(Str(cfg != null ? cfg.LeaderAccount : null));
            sb.Append(", \"policy\": ").Append(Str(cfg != null ? cfg.Policy.ToString() : null));
            sb.Append(", \"followers\": [");
            if (cfg != null)
            {
                for (int i = 0; i < cfg.Followers.Count; i++)
                {
                    FollowerConfig f = cfg.Followers[i];
                    if (i > 0) sb.Append(", ");
                    sb.Append("{ \"account\": ").Append(Str(f.AccountName))
                      .Append(", \"enabled\": ").Append(f.Enabled ? "true" : "false")
                      .Append(", \"mult\": ").Append(Num(f.Multiplier))
                      .Append(", \"map\": ").Append(Str(SentinelCopierService_v0_2_1.MapToDsl(f.InstrumentMap)))
                      .Append(" }");
                }
            }
            sb.Append("] }");
        }

        // Returns a JSON object for an account IF it has positions or working orders; else null.
        private string TryAccountDetail(Account a)
        {
            try
            {
                var positions = new StringBuilder();
                int posCount = 0;
                foreach (Position p in SnapshotPositions(a))
                {
                    if (p == null || p.MarketPosition == MarketPosition.Flat) continue;
                    double upl = 0;
                    try { upl = p.GetUnrealizedProfitLoss(PerformanceUnit.Currency); } catch (Exception _sx) { SentinelCore.Swallow("SentinelState.TryAccountDetail", _sx); }
                    if (posCount++ > 0) positions.Append(", ");
                    positions.Append("{ \"instrument\": ").Append(Str(FullName(p.Instrument)))
                             .Append(", \"side\": ").Append(Str(p.MarketPosition.ToString()))
                             .Append(", \"qty\": ").Append(p.Quantity)
                             .Append(", \"avg\": ").Append(Num(p.AveragePrice))
                             .Append(", \"upnl\": ").Append(Num(upl)).Append(" }");
                }

                var orders = new StringBuilder();
                int ordCount = 0;
                foreach (Order o in SnapshotOrders(a))
                {
                    if (o == null) continue;
                    OrderState st = o.OrderState;
                    if (st != OrderState.Working && st != OrderState.Accepted) continue;
                    if (ordCount++ > 0) orders.Append(", ");
                    orders.Append("{ \"name\": ").Append(Str(o.Name))
                          .Append(", \"instrument\": ").Append(Str(FullName(o.Instrument)))
                          .Append(", \"state\": ").Append(Str(st.ToString()))
                          .Append(", \"type\": ").Append(Str(o.OrderType.ToString()))
                          .Append(", \"action\": ").Append(Str(o.OrderAction.ToString()))
                          .Append(", \"qty\": ").Append(o.Quantity)
                          .Append(", \"limit\": ").Append(Num(o.LimitPrice))
                          .Append(", \"stop\": ").Append(Num(o.StopPrice)).Append(" }");
                }

                if (posCount == 0 && ordCount == 0) return null;   // nothing to show → skip

                double realized = 0, unrealized = 0;
                try { realized   = a.Get(AccountItem.RealizedProfitLoss,   Currency.UsDollar); } catch (Exception _sx) { SentinelCore.Swallow("SentinelState.TryAccountDetail", _sx); }
                try { unrealized = a.Get(AccountItem.UnrealizedProfitLoss, Currency.UsDollar); } catch (Exception _sx) { SentinelCore.Swallow("SentinelState.TryAccountDetail", _sx); }
                string provider = null; string status = null;
                try { if (a.Connection != null) { status = a.Connection.Status.ToString(); if (a.Connection.Options != null) provider = a.Connection.Options.Name; } } catch (Exception _sx) { SentinelCore.Swallow("SentinelState.TryAccountDetail", _sx); }

                var sb = new StringBuilder();
                sb.Append("    { \"name\": ").Append(Str(a.Name))
                  .Append(", \"provider\": ").Append(Str(provider))
                  .Append(", \"status\": ").Append(Str(status))
                  .Append(", \"realizedPnl\": ").Append(Num(realized))
                  .Append(", \"unrealizedPnl\": ").Append(Num(unrealized))
                  .Append(", \"positions\": [").Append(positions).Append("]")
                  .Append(", \"orders\": [").Append(orders).Append("] }");
                return sb.ToString();
            }
            catch { return null; }   // account changed under us → skip this snapshot for it
        }

        // ── defensive collection snapshots (avoid "collection modified" mid-iterate) ──
        private static System.Collections.Generic.List<Account> SnapshotAccounts()
        {
            var list = new System.Collections.Generic.List<Account>();
            try { lock (Account.All) { foreach (Account a in Account.All) if (a != null) list.Add(a); } } catch (Exception _sx) { SentinelCore.Swallow("SentinelState.SnapshotAccounts", _sx); }
            return list;
        }

        private static System.Collections.Generic.List<Position> SnapshotPositions(Account a)
        {
            var list = new System.Collections.Generic.List<Position>();
            try { foreach (Position p in a.Positions) list.Add(p); } catch (Exception _sx) { SentinelCore.Swallow("SentinelState.SnapshotPositions", _sx); }
            return list;
        }

        private static System.Collections.Generic.List<Order> SnapshotOrders(Account a)
        {
            var list = new System.Collections.Generic.List<Order>();
            try { foreach (Order o in a.Orders) list.Add(o); } catch (Exception _sx) { SentinelCore.Swallow("SentinelState.SnapshotOrders", _sx); }
            return list;
        }

        private static string FullName(Instrument i)
        {
            try { return i != null ? i.FullName : null; } catch { return null; }
        }

        // ── atomic write: tmp then move over the target ──
        private static void AtomicWrite(string path, string content)
        {
            string tmp = path + ".tmp";
            try
            {
                File.WriteAllText(tmp, content, Encoding.UTF8);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch
            {
                try { File.WriteAllText(path, content, Encoding.UTF8); } catch (Exception _sx) { SentinelCore.Swallow("SentinelState.AtomicWrite", _sx); }   // fallback: direct
            }
        }

        // ── tiny JSON helpers ──
        private static string Str(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        private static string Num(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return "null";
            return d.ToString("0.############", CultureInfo.InvariantCulture);
        }
    }
}
