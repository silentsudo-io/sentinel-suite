// ─────────────────────────────────────────────────────────────────────────────
// This Source Code Form is subject to the terms of the Mozilla Public License,
// v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
// one at https://mozilla.org/MPL/2.0/.
//
// Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
// ─────────────────────────────────────────────────────────────────────────────
// ═════════════════════════════════════════════════════════════════════════════
//  SentinelCopierService — headless multi-account trade copier (NT8 AddOn)
//  File: SentinelCopierService_v0_1_0.cs
//  Service version: v0.1.0   (SKELETON — compiles + structured; live paths marked VALIDATE)
// ─────────────────────────────────────────────────────────────────────────────
//  WHAT THIS IS  (part of the "Sentinel Suite"; see Docs/ROADMAP.md)
//    A headless, always-on AddOn service — SAME architecture as MAECaptureService:
//    an AddOnBase singleton that lives for the NT process, exposes a static Instance
//    so a future dashboard/settings window ATTACHES to it, and never depends on any
//    window being open. Closing a UI must never stop copying.
//
//  WHAT IT DOES  (pure FILL-MIRROR copier — the "Horde" model)
//    • Subscribes to the LEADER account's ExecutionUpdate.
//    • On each LEADER fill (Filled/PartialFilled), for every enabled+connected FOLLOWER:
//        - size  = leaderFillQty × instrumentSizeRatio × followerMultiplier
//        - symbol = per-follower instrument map (e.g. GC→MGC), else same instrument
//        - submits a MARKET order on the follower account (unmanaged, account-level).
//    • Pure fill-mirror: it never reads the leader strategy's internal state. Entry AND
//      exit both arrive as fills, so each follower's NET position stays synced with the
//      leader automatically. Works with GTrader21, manual ChartTrader, or any strategy.
//
//  CREDIT / ATTRIBUTION  (required before any open-source release)
//    The fill-mirror engine (subscribe to master ExecutionUpdate → qty×multiplier → market
//    CreateOrder+Submit, Guid order name) is ADAPTED from the TickHunter NT "Horde" copier by
//    **Frosty** (TradeLikeAZombie community). Even the "SentCopy_" order prefix echoes their
//    "TH-Horde_". Sentinel ADDS: same-provider prop rule, bidirectional GC↔MGC / CL↔MCL map,
//    SentinelCore kill-switch + feed-health gating, Eye-gate, and manual-assist mode. Thanks, Frosty.
//
//  LEADER MODEL (decided — see memory copier-samples-analysis)
//    The leader is a SIGNAL account (a dedicated SIM or small live acct trading native GC).
//    EVERY real account — including what the user calls "primary" — is an execution TARGET
//    with its own instrument map (GC→MGC) + size. This satisfies "primary cross-trades"
//    AND "SIM leader → prop accounts" with ZERO changes to GTrader21's order path.
//
//  SUITE CONTRACTS (how this ties into the rest of the Sentinel Suite)
//    • ORDER-NAME PREFIX  = "SentCopy_"  → distinguishes copier orders from strategy orders
//      (GTrader21 uses "GTS_"; keep prefixes disjoint so tools never edit each other's orders).
//    • SHARED KILL-SWITCH = SentinelCore.KillSwitchEngaged (in SentinelCore_v1_0_0.cs). Any
//      suite tool (a risk monitor, the panel's lockout, the dashboard) flips it to halt ALL
//      mirroring. The copier no longer owns its own flag — it consults the suite Core.
//    • MIRROR GATE        = SentinelCore.CanAct() + per-follower connection health, the single
//      choke point every mirror passes through. Core's feed-health probe (once a health tool
//      registers one — e.g. the GTrader21 v0.1.2 lag metric) gates each account automatically.
//
//  VERIFIED API NOTES (from in-repo usage — AlightenButtonPanelv3, MAECaptureService)
//    • AddOnBase auto-starts as a singleton; OnStateChange fires SetDefaults/Active/Terminated.
//      GOTCHA: base ctor calls OnStateChange BEFORE the subclass ctor → init lazily in Start(),
//      never rely on field initializers for mutable state.
//    • Order submit (account-level, no strategy):
//        acct.CreateOrder(instr, OrderAction, OrderType, OrderEntry.Automated, TimeInForce,
//                         qty, limitPx, stopPx, ocoId, name, Core.Globals.MaxDate, null);
//        acct.Submit(new[]{ order });   // wrap in try/catch
//    • Instrument.GetInstrument(fullName) resolves a contract; returns null if not found.
//    • Execution carries the AUTHORITATIVE fill: e.Execution.{Account,Instrument,Price,
//      Quantity,ExecutionId,Order}. e.Execution.Order.{OrderState,OrderAction}.
//    • MUST unsubscribe every account event in Terminated (leak / dangling handler otherwise).
//
//  VERIFIED AGAINST IN-REPO USAGE (2026-07-01 — these compile, no longer VALIDATE):
//    • Account.Connection.Status == ConnectionStatus.Connected — Account.Connection is real
//      (@ProfitLoss.cs:89 position.Account.Connection); Connection.Status (@BarTimer.cs:50).
//    • Connection.Options.Name for provider identity — AutoReconnect.cs:247 (c.Options.Name).
//
//  STILL TO VALIDATE ON A SIM RUN (marked VALIDATE: inline — real behavior, not compile)
//    • Cross-instrument contract resolution for GC→MGC: the same-expiry heuristic assumes MGC
//      shares GC's month code, which may NOT hold — the one item that genuinely needs a live check.
//    • Account.Positions iteration for entry-vs-exit action (Positions exists; confirm it's fresh
//      enough at mirror time — a zero-crossing fill still needs an order split, deferred).
//    • Provider grouping semantics: Options.Name is the CONNECTION name (firm-level, correct);
//      note the Provider ENUM would group by tech-provider (too coarse — two Rithmic firms collide).
//    • Whether ExecutionUpdate can re-fire the same ExecutionId (dedupe guard added defensively).
//
//  CHANGELOG
//    (in-place, 2026-07-25) — RECORDED CATCHES: 5 empty `catch {}` -> SentinelCore.Swallow (Core >= v1.41.0).
//             Behaviour identical; a swallowed fault on the mirror path is now counted and logged.
//    v0.1.0h — (in-place) COPY-SLIPPAGE capture: also subscribe enabled AUTO followers' ExecutionUpdate
//             (OnFollowerExecution) to log each mirror FILL to SentinelCore.Ledger.Fill with intended =
//             the LEADER fill price we replicated (correlated by mirror order name at submit) vs the
//             follower's actual fill → adverse slip ticks in the dashboard Slippage view (how faithfully
//             followers track the signal — a real prop-copy quality metric). Same-symbol + GC→MGC/ES→MES
//             share a price scale (meaningful); exotic cross-maps would not (rare). CAPTURE ONLY — never
//             acts; bounded dedupe + correlation map; follower subs torn down in Unsubscribe/Stop.
//    v0.1.0g — (in-place) the flat-follower entry gate now ALSO honors the account profile's SESSION
//             window (SentinelCore.InAccountSession) — a flat follower opens no new trades outside its
//             session; open positions still mirror (manage/close). Composes with the governor gate.
//    v0.1.0f — (in-place) CONSISTENCY GOVERNOR: a follower that hit its daily cap/loss (SentinelCore
//             governor, hosted by Risk) opens NO new trades today — MirrorToFollower skips a mirror when
//             the follower is FLAT and !TradingAllowedToday. Non-flat followers always mirror (manage/
//             close), so the governor never traps a live position. Per-follower, per the spec.
//    v0.1.0e — (in-place) SCOPED KILL: CanMirror now gates via SentinelCore.CanActInstrument(leader
//             instrument) instead of CanAct, so a per-instrument kill (Risk on a lagging GC feed)
//             halts only GC mirrors — ES/NQ keep copying. Global kill-switch still halts everything.
//    v0.1.0d — (in-place) ATTRIBUTION: added explicit CREDIT to Frosty / TickHunter "Horde" (see
//             the CREDIT block above). Comment-only — no behavior change. Required before any
//             open-source release; previously the header only nodded at "the 'Horde' model".
//    v0.1.0c — (in-place) MANUAL-ASSIST mode. A follower can be 'manual' (follower=<label>|manual|…):
//             instead of auto-submitting, the Copier PUBLISHES a place-by-hand ticket to
//             SentinelCore's assist registry (dashboard Assist tab + state.json). Same map/size/
//             Eye-gate pipeline; mirrors the leader's exact action; the account name is just a label
//             (need not be an NT account). For prop firms that bar automated copy-trading (TPT eval/
//             PRO, Bulenox) — decision-support instead of auto-execution. Auto path unchanged.
//    v0.1.0b — (in-place) Eye-gate: when UseEyeGate, mirror only ENTRIES SentinelEye qualifies
//             (exits always mirror). eyeGate=on/off in Copy.conf.
//    v0.1.0 — initial SKELETON. Headless AddOnBase singleton; leader ExecutionUpdate
//             subscription; fill-mirror engine; per-follower instrument map + size ratio +
//             multiplier; same-provider policy (Off/Warn/Block) with SIM-leader exemption;
//             shared kill-switch + mirror gate; "SentCopy_" order prefix. Config is EMPTY by
//             default (service is inert until a leader+followers are set) — safe to install.
//             Live order paths marked VALIDATE; dashboard + JSON config load are follow-ups.
//    v0.1.0a — (in-place, pre-freeze) consume SentinelCore: kill-switch + feed-health gate now
//             live in the shared Core (SentinelCore_v1_0_0.cs) instead of a copier-local flag;
//             logging routed through SentinelCore.Log("Copy", …). No behavior change to mirroring.
//    v0.1.0b — (in-place, pre-freeze) CONFIG PERSISTENCE: SaveConfig()/LoadConfig() to a simple
//             text file <UserDataDir>\Sentinel\Copy.conf (leader/policy/follower lines, follower
//             map reuses the "GC>MGC*10" DSL). Start() auto-loads it → the copier survives NT
//             recompiles/restarts instead of resetting to inert. Dashboard Apply saves it.
// ═════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NinjaTrader.Cbi;
using NinjaTrader.Core;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.AddOns.Sentinel;   // shared suite core (kill-switch, health gate)

namespace NinjaTrader.NinjaScript.AddOns.SentinelCopier
{
    // ── same-provider prop-firm policy ───────────────────────────────────────
    // Off   : copy across any provider freely.
    // Warn  : copy across providers but log a warning (audit trail).
    // Block : refuse a mirror when leader/follower providers differ (prop compliance).
    // NOTE: a SIM leader is EXEMPT (it's local, belongs to no firm) — the meaningful
    //       enforcement is among EXECUTION accounts. See copier-samples-analysis memory:
    //       SIM→multiple prop firms is desired, so "all targets share leader provider"
    //       is NOT the rule. This enum is the coarse first cut; per-firm grouping refines it.
    public enum ProviderPolicy { Off, Warn, Block }

    // Auto = mirror by submitting orders (default). Manual = emit a "place by hand" ASSIST ticket
    // instead of submitting — for prop firms that bar automated copy-trading (TPT eval/PRO, Bulenox).
    public enum FollowerMode { Auto, Manual }

    // one entry in a follower's instrument map: leader symbol → target symbol + size ratio.
    // e.g. GC → { MGC, 10 }  (1 GC ≈ 10 MGC); size = leaderQty × ratio × follower.Multiplier.
    public sealed class InstrumentMapEntry
    {
        public string TargetSymbol;      // master-instrument name, e.g. "MGC"
        public double SizeRatio = 1.0;   // contract multiplier vs leader (GC→MGC = 10)
    }

    public sealed class FollowerConfig
    {
        public string AccountName;
        public bool   Enabled = true;
        public FollowerMode Mode = FollowerMode.Auto;   // Auto = submit orders; Manual = emit assist ticket
        public double Multiplier = 1.0;  // extra per-follower size scaling on top of ratio
        // leader master symbol (e.g. "GC") → target (e.g. MGC ×10). Empty = same instrument.
        public Dictionary<string, InstrumentMapEntry> InstrumentMap =
            new Dictionary<string, InstrumentMapEntry>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class CopierConfig
    {
        public string LeaderAccount;                 // the SIGNAL account we mirror FROM
        public ProviderPolicy Policy = ProviderPolicy.Warn;
        public bool UseEyeGate = false;              // when true, mirror only ENTRIES SentinelEye qualifies (exits always mirror)
        public List<FollowerConfig> Followers = new List<FollowerConfig>();
    }

    public class SentinelCopierService_v0_1_0 : NinjaTrader.NinjaScript.AddOnBase
    {
        private const string OrderPrefix = "SentCopy_";     // suite contract: copier order tag

        // ── singleton so a future dashboard/settings window can attach ──
        public static SentinelCopierService_v0_1_0 Instance { get; private set; }

        // Kill-switch + feed-health now live in the shared SentinelCore (suite-wide), not here.
        // Flip via SentinelCore.SetKillSwitch(true, "..."); the mirror gate consults SentinelCore.

        // active config — EMPTY by default → service is inert (safe) until configured.
        private CopierConfig _config;

        private List<Account> _subscribed;      // leader account(s) we've hooked
        private HashSet<string> _seenExecIds;   // dedupe defensive guard on ExecutionUpdate
        private bool _started;
        private readonly object _lock = new object();

        // ── copy-slippage capture (follower fill vs the leader fill price we're replicating) ──
        private List<Account> _followerSubscribed;                  // follower accounts hooked for fill capture
        private HashSet<string> _seenFollowerExecIds;               // dedupe follower ExecutionUpdate
        private readonly Dictionary<string, double> _mirrorLeaderPx // mirror order name → leader fill px (intended)
            = new Dictionary<string, double>(StringComparer.Ordinal);

        // ─────────────────────────────────────────────────────────────────────
        protected override void OnStateChange()
        {
            // base ctor calls this BEFORE our ctor — do not rely on field initializers.
            if (State == State.SetDefaults)
            {
                Name = "SentinelCopierService_v0_1_0";
                Description = "Sentinel Suite — headless multi-account fill-mirror trade copier. "
                            + "Mirrors a leader account's fills to enabled followers (with "
                            + "instrument cross-map + size). Inert until configured. Runs always.";
            }
            else if (State == State.Active)      Start();
            else if (State == State.Terminated)  Stop();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Start — hook the leader account. Idempotent (Active can re-fire).
        // ─────────────────────────────────────────────────────────────────────
        private void Start()
        {
            if (_started) return;
            _started = true;
            Instance = this;

            _subscribed  = new List<Account>();
            _seenExecIds = new HashSet<string>();
            _followerSubscribed = new List<Account>();
            _seenFollowerExecIds = new HashSet<string>();

            // CONFIG PERSISTENCE: reload the last-applied config so a recompile/restart doesn't
            // silently reset the copier to inert (the recompile footgun). Empty if no file yet.
            if (_config == null)
            {
                _config = LoadConfig();
                if (_config == null) _config = new CopierConfig();   // no saved config → inert
                else Log("Loaded persisted config from " + ConfigPath);
            }

            Subscribe();
            Log("SentinelCopierService v0.1.0 started. Leader='" + (_config.LeaderAccount ?? "<none>")
                + "', followers=" + _config.Followers.Count
                + (string.IsNullOrEmpty(_config.LeaderAccount) ? " (inert until a leader is set)." : "."));
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Stop — unsubscribe every hooked account.
        // ─────────────────────────────────────────────────────────────────────
        private void Stop()
        {
            if (!_started) return;
            _started = false;
            Unsubscribe();
            if (Instance == this) Instance = null;
            Log("SentinelCopierService stopped; leader subscription released.");
        }

        // ── (re)subscribe to the configured leader account's executions ──────────
        private void Subscribe()
        {
            Unsubscribe();
            if (_config == null || string.IsNullOrEmpty(_config.LeaderAccount)) return;

            Account leader = FindAccount(_config.LeaderAccount);
            if (leader == null)
            {
                Log("WARNING: leader account '" + _config.LeaderAccount + "' not found (yet). "
                    + "Will not mirror until it exists and Reconfigure() is called.");
                return;
            }
            leader.ExecutionUpdate += OnLeaderExecution;
            _subscribed.Add(leader);
            Log("Subscribed to leader executions: " + leader.Name);

            // hook enabled AUTO followers too — only to CAPTURE their mirror fills (copy slippage),
            // never to act. (Manual followers submit no orders → nothing to capture.)
            if (_followerSubscribed != null)
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (FollowerConfig f in _config.Followers)
                {
                    if (f == null || !f.Enabled || f.Mode == FollowerMode.Manual || string.IsNullOrEmpty(f.AccountName)) continue;
                    if (!seen.Add(f.AccountName)) continue;   // one subscription per account
                    Account fa = FindAccount(f.AccountName);
                    if (fa == null) continue;
                    fa.ExecutionUpdate += OnFollowerExecution;
                    _followerSubscribed.Add(fa);
                }
                if (_followerSubscribed.Count > 0) Log("Subscribed to " + _followerSubscribed.Count + " follower account(s) for copy-slippage capture.");
            }
        }

        private void Unsubscribe()
        {
            if (_subscribed != null)
            {
                foreach (Account a in _subscribed)
                    a.ExecutionUpdate -= OnLeaderExecution;
                _subscribed.Clear();
            }
            if (_followerSubscribed != null)
            {
                foreach (Account a in _followerSubscribed)
                    a.ExecutionUpdate -= OnFollowerExecution;
                _followerSubscribed.Clear();
            }
            lock (_lock) { _mirrorLeaderPx.Clear(); }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  PUBLIC CONFIG API (a future dashboard / JSON loader calls these).
        //  Mirrors MAECaptureService's pattern of the service owning the truth.
        // ─────────────────────────────────────────────────────────────────────
        public void Reconfigure(CopierConfig cfg)
        {
            lock (_lock)
            {
                _config = cfg ?? new CopierConfig();
                if (_seenExecIds != null) _seenExecIds.Clear();
                if (_started) Subscribe();   // re-hook the (possibly new) leader
            }
            Log("Reconfigured. Leader='" + (_config.LeaderAccount ?? "<none>")
                + "', followers=" + _config.Followers.Count + ", policy=" + _config.Policy);
        }

        public CopierConfig CurrentConfig { get { return _config; } }

        // ─────────────────────────────────────────────────────────────────────
        //  CONFIG PERSISTENCE — a simple, hand-editable text file (NOT JSON, so no
        //  serializer dependency + easy to read/edit outside NT). One key per line:
        //      leader=<account>
        //      policy=Off|Warn|Block
        //      follower=<account>|<on|off|manual>|<multiplier>|<mapDSL>
        //        on = auto-submit · manual = emit a place-by-hand ASSIST ticket (no order) · off = disabled
        //  where mapDSL reuses the dashboard's "GC>MGC*10, CL>MCL" grammar.
        // ─────────────────────────────────────────────────────────────────────
        public static string ConfigPath { get { return Path.Combine(SentinelCore.SettingsDir, "Copy.conf"); } }

        public static void SaveConfig(CopierConfig cfg)
        {
            if (cfg == null) return;
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# Sentinel Copier config — auto-saved by the dashboard. Safe to hand-edit.");
                sb.AppendLine("leader=" + (cfg.LeaderAccount ?? ""));
                sb.AppendLine("policy=" + cfg.Policy);
                sb.AppendLine("eyeGate=" + (cfg.UseEyeGate ? "on" : "off"));
                foreach (FollowerConfig f in cfg.Followers)
                {
                    if (f == null || string.IsNullOrEmpty(f.AccountName)) continue;
                    sb.AppendLine("follower=" + f.AccountName
                        + "|" + (!f.Enabled ? "off" : (f.Mode == FollowerMode.Manual ? "manual" : "on"))
                        + "|" + f.Multiplier.ToString(CultureInfo.InvariantCulture)
                        + "|" + MapToDsl(f.InstrumentMap));
                }
                File.WriteAllText(ConfigPath, sb.ToString(), Encoding.UTF8);
                SentinelCore.Log("Copy", "Config saved → " + ConfigPath);
            }
            catch (Exception ex) { SentinelCore.Log("Copy", "SaveConfig error: " + ex.Message); }
        }

        public static CopierConfig LoadConfig()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return null;
                var cfg = new CopierConfig();
                foreach (string raw in File.ReadAllLines(ConfigPath))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();

                    if (key.Equals("leader", StringComparison.OrdinalIgnoreCase))
                        cfg.LeaderAccount = val.Length > 0 ? val : null;
                    else if (key.Equals("policy", StringComparison.OrdinalIgnoreCase))
                    {
                        ProviderPolicy p;
                        if (Enum.TryParse(val, true, out p)) cfg.Policy = p;
                    }
                    else if (key.Equals("eyeGate", StringComparison.OrdinalIgnoreCase))
                        cfg.UseEyeGate = val.Equals("on", StringComparison.OrdinalIgnoreCase) || val.Equals("true", StringComparison.OrdinalIgnoreCase);
                    else if (key.Equals("follower", StringComparison.OrdinalIgnoreCase))
                    {
                        string[] parts = val.Split('|');
                        if (parts.Length < 1 || parts[0].Trim().Length == 0) continue;
                        var f = new FollowerConfig { AccountName = parts[0].Trim() };
                        if (parts.Length > 1)
                        {
                            string mode = parts[1].Trim();
                            if (mode.Equals("off", StringComparison.OrdinalIgnoreCase)) f.Enabled = false;
                            else if (mode.Equals("manual", StringComparison.OrdinalIgnoreCase)) { f.Enabled = true; f.Mode = FollowerMode.Manual; }
                            else { f.Enabled = true; f.Mode = FollowerMode.Auto; }   // on / auto
                        }
                        double m;
                        if (parts.Length > 2 && double.TryParse(parts[2].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out m)) f.Multiplier = m;
                        if (parts.Length > 3) ParseMapDsl(parts[3], f.InstrumentMap);
                        cfg.Followers.Add(f);
                    }
                }
                return cfg;
            }
            catch (Exception ex) { SentinelCore.Log("Copy", "LoadConfig error: " + ex.Message); return null; }
        }

        // Canonical instrument-map DSL parse/format (the dashboard reuses these).
        //   "GC>MGC*10, CL>MCL"  ⇄  { GC:{MGC,10}, CL:{MCL,1} }
        public static void ParseMapDsl(string dsl, Dictionary<string, InstrumentMapEntry> into)
        {
            if (into == null || string.IsNullOrWhiteSpace(dsl)) return;
            foreach (string part in dsl.Split(','))
            {
                string p = part.Trim();
                if (p.Length == 0) continue;
                int gt = p.IndexOf('>');
                if (gt <= 0) continue;
                string left = p.Substring(0, gt).Trim();
                string right = p.Substring(gt + 1).Trim();
                double ratio = 1.0;
                int star = right.IndexOf('*');
                if (star >= 0)
                {
                    double.TryParse(right.Substring(star + 1).Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out ratio);
                    right = right.Substring(0, star).Trim();
                }
                if (left.Length == 0 || right.Length == 0) continue;
                into[left] = new InstrumentMapEntry { TargetSymbol = right, SizeRatio = ratio <= 0 ? 1.0 : ratio };
            }
        }

        public static string MapToDsl(Dictionary<string, InstrumentMapEntry> map)
        {
            if (map == null || map.Count == 0) return "";
            var parts = new List<string>();
            foreach (var kv in map)
                parts.Add(kv.Key + ">" + kv.Value.TargetSymbol
                    + (Math.Abs(kv.Value.SizeRatio - 1.0) > 1e-9 ? "*" + kv.Value.SizeRatio.ToString(CultureInfo.InvariantCulture) : ""));
            return string.Join(", ", parts);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  MIRROR GATE — the single choke point. kill-switch now; feed-health next.
        // ─────────────────────────────────────────────────────────────────────
        private bool CanMirror(Instrument instr, out string reason)
        {
            // Global kill-switch + PER-INSTRUMENT scoped kill (v0.1.0e) + leader feed health. Scoping
            // by the leader's instrument means a lagging GC feed halts only GC mirrors, not ES/NQ.
            Account leader = FindAccount(_config != null ? _config.LeaderAccount : null);
            return SentinelCore.CanActInstrument(instr != null ? instr.FullName : null, leader, out reason);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  LEADER FILL → mirror to followers. This is the whole engine.
        // ─────────────────────────────────────────────────────────────────────
        private void OnLeaderExecution(object sender, ExecutionEventArgs e)
        {
            try
            {
                var cfg = _config;
                if (cfg == null || string.IsNullOrEmpty(cfg.LeaderAccount)) return;

                Execution exec = e.Execution;
                if (exec == null || exec.Account == null || exec.Order == null) return;

                // only the LEADER account's fills drive copying
                if (!string.Equals(exec.Account.Name, cfg.LeaderAccount, StringComparison.OrdinalIgnoreCase))
                    return;

                // act on FILLS only (entry AND exit both arrive here → net stays synced)
                OrderState st = exec.Order.OrderState;
                if (st != OrderState.Filled && st != OrderState.PartFilled) return;

                int qty = exec.Quantity;                 // qty of THIS event → partials handled
                if (qty <= 0) return;

                // defensive dedupe: don't mirror the same execution twice if the event re-fires
                if (exec.ExecutionId != null)
                {
                    lock (_lock) { if (!_seenExecIds.Add(exec.ExecutionId)) return; }
                }

                string reason;
                if (!CanMirror(exec.Instrument, out reason))
                {
                    Log("Mirror BLOCKED (" + reason + ") — leader fill " + qty + " "
                        + exec.Instrument.FullName + " not copied.");
                    return;
                }

                bool leaderIsBuy = exec.Order.OrderAction == OrderAction.Buy
                                || exec.Order.OrderAction == OrderAction.BuyToCover;
                string leaderSym = exec.Instrument.MasterInstrument.Name;

                foreach (FollowerConfig f in cfg.Followers)
                {
                    if (f == null || !f.Enabled || string.IsNullOrEmpty(f.AccountName)) continue;
                    MirrorToFollower(f, exec, leaderSym, leaderIsBuy, qty);
                }
            }
            catch (Exception ex)
            {
                Log("OnLeaderExecution error: " + ex.Message);
            }
        }

        // A follower's mirror order FILLED → record copy slippage (follower fill vs the leader price we
        // replicated). CAPTURE ONLY — never acts on the account. Intended = the leader fill price
        // correlated at mirror-submit time; 0/unknown → slip omitted (still logged as a fill).
        private void OnFollowerExecution(object sender, ExecutionEventArgs e)
        {
            try
            {
                Execution exec = e.Execution;
                if (exec == null || exec.Order == null || exec.Instrument == null) return;
                if (!IsCopierOrder(exec.Order.Name)) return;   // only our SentCopy_ mirror orders

                OrderState st = exec.Order.OrderState;
                if (st != OrderState.Filled && st != OrderState.PartFilled) return;
                int qty = exec.Quantity;
                if (qty <= 0) return;

                if (!string.IsNullOrEmpty(exec.ExecutionId))
                {
                    lock (_lock)
                    {
                        if (_seenFollowerExecIds.Count > 4000) _seenFollowerExecIds.Clear();
                        if (!_seenFollowerExecIds.Add(exec.ExecutionId)) return;
                    }
                }

                double leaderPx;
                lock (_lock) { if (!_mirrorLeaderPx.TryGetValue(exec.Order.Name, out leaderPx)) leaderPx = 0; }

                double tick = exec.Instrument.MasterInstrument != null ? exec.Instrument.MasterInstrument.TickSize : 0;
                SentinelCore.Ledger.Fill(
                    exec.Account != null ? exec.Account.Name : "?",
                    exec.Instrument.FullName, exec.Order.OrderAction.ToString(), qty, leaderPx, exec.Price, tick, "Copier:mirror");
            }
            catch (Exception ex) { Log("OnFollowerExecution error: " + ex.Message); }
        }

        private void MirrorToFollower(FollowerConfig f, Execution exec, string leaderSym,
                                      bool leaderIsBuy, int leaderQty)
        {
            try
            {
                // MANUAL mode: don't submit — publish a "place by hand" assist ticket instead
                // (for prop accounts that bar automated copy-trading). Self-contained path.
                if (f.Mode == FollowerMode.Manual)
                {
                    EmitAssistTicket(f, exec, leaderSym, leaderIsBuy, leaderQty);
                    return;
                }

                Account fAcct = FindAccount(f.AccountName);
                if (fAcct == null) { Log("skip: follower '" + f.AccountName + "' not found"); return; }

                if (!IsConnected(fAcct))
                {
                    Log("skip: follower '" + f.AccountName + "' not connected — no mirror (fill lost, "
                        + "by design; a disconnected account can't be kept in sync).");
                    return;
                }

                // per-follower feed health via the shared Core probe (no-op until a health tool
                // registers one). Gates each execution account on ITS OWN feed, per the design.
                if (!SentinelCore.IsAccountHealthy(fAcct))
                {
                    Log("skip: follower '" + f.AccountName + "' feed unhealthy (Core gate).");
                    return;
                }

                // consistency governor (v0.1.0f): a follower that hit its daily cap/loss opens NO new
                // trades today. Only block when the follower is FLAT (a fill on a flat account = a new
                // entry); a follower with an open position is always allowed to manage/close it, so the
                // governor never traps a follower in a live trade.
                if (IsAccountFlat(fAcct))   // a fill on a flat account = a NEW entry → gate it
                {
                    string blockReason = null;
                    if (!SentinelCore.TradingAllowedToday(fAcct))
                    {
                        var gs = SentinelCore.GetGovernorState(fAcct.Name);
                        blockReason = "governor " + (gs != null ? gs.Status + " (" + gs.Reason + ")" : "day done");
                    }
                    else
                    {
                        string sr;
                        if (!SentinelCore.InAccountSession(SentinelCore.GetAccountProfile(fAcct.Name), out sr)) blockReason = sr;
                    }
                    if (blockReason != null)
                    {
                        Log("skip: follower '" + f.AccountName + "' — " + blockReason + " — no new entries (open positions still mirror).");
                        return;
                    }
                }

                // same-provider prop policy (SIM leader exempt) ───────────────────
                string provReason;
                if (!ProviderAllowed(exec.Account, fAcct, out provReason))
                {
                    Log("BLOCKED by provider policy: " + provReason + " → '" + f.AccountName + "'");
                    return;
                }

                // resolve target instrument + size ────────────────────────────────
                InstrumentMapEntry map;
                f.InstrumentMap.TryGetValue(leaderSym, out map);   // null → same instrument, ratio 1
                double ratio = map != null ? map.SizeRatio : 1.0;
                int fQty = (int)Math.Round(leaderQty * ratio * f.Multiplier);
                if (fQty <= 0) { Log("skip: '" + f.AccountName + "' computed qty <= 0"); return; }

                Instrument tInstr = ResolveTargetInstrument(exec.Instrument, map);
                if (tInstr == null)
                {
                    Log("skip: '" + f.AccountName + "' could not resolve target instrument for "
                        + leaderSym + (map != null ? "→" + map.TargetSymbol : ""));
                    return;
                }

                OrderAction fAction = ResolveFollowerAction(leaderIsBuy, fAcct, tInstr);

                // ── EYE-GATE (opt-in): only mirror ENTRIES that SentinelEye qualifies for this leader
                //    instrument+direction. Exits (Sell/BuyToCover) ALWAYS mirror to keep followers synced.
                if (_config != null && _config.UseEyeGate
                    && (fAction == OrderAction.Buy || fAction == OrderAction.SellShort))
                {
                    int wantDir = fAction == OrderAction.Buy ? 1 : -1;
                    var verdict = SentinelCore.GetEyeVerdict(leaderSym, 30);   // 30s staleness window
                    if (verdict == null || verdict.Direction != wantDir)
                    {
                        Log("Eye-gate BLOCKED entry ▶ " + f.AccountName + " " + fAction + " " + tInstr.FullName
                            + " — Eye for " + leaderSym + " = "
                            + (verdict == null ? "no/stale verdict" : "dir " + verdict.Direction + " score " + verdict.Score.ToString("0") + " (" + verdict.Source + ")"));
                        return;
                    }
                    Log("Eye-gate OK ▶ " + leaderSym + " qualified dir " + verdict.Direction + " score " + verdict.Score.ToString("0"));
                }

                // ── ORDER GATE (v1.1.0): automated mirror = fail CLOSED. Gate only NEW entries (flat
                //    follower); exits always mirror so followers stay synced. Adds kill/feed/rollover/
                //    news/rate/qty-cap on top of the governor/session check above (the single choke point).
                if (IsAccountFlat(fAcct))
                {
                    var gd = SentinelCore.GateEntry(fAcct, tInstr.FullName, fQty);
                    if (!gd.IsClear)
                    {
                        Log("gate BLOCKED entry ▶ " + f.AccountName + " " + fAction + " " + tInstr.FullName + " — " + gd.Reason);
                        return;
                    }
                }

                string name = OrderPrefix + Guid.NewGuid().ToString("N").Substring(0, 8);

                Order o = fAcct.CreateOrder(
                    tInstr, fAction, OrderType.Market, OrderEntry.Automated, TimeInForce.Day,
                    fQty, 0, 0, string.Empty, name, Core.Globals.MaxDate, null);

                // correlate this mirror to the LEADER's fill price so OnFollowerExecution can score copy
                // slippage (follower fill vs the price we tried to replicate). Same-symbol + GC→MGC/ES→MES
                // share a price scale so the tick diff is meaningful; exotic cross-maps would not (rare).
                lock (_lock)
                {
                    if (_mirrorLeaderPx.Count > 4000) _mirrorLeaderPx.Clear();   // bounded
                    _mirrorLeaderPx[name] = exec.Price;
                }

                try
                {
                    fAcct.Submit(new[] { o });
                    try { SentinelCore.NoteOrderSubmitted(fAcct.Name); } catch (Exception _sx) { SentinelCore.Swallow("SentinelCopier.MirrorToFollower", _sx); }   // feed the rate guard
                    try { SentinelCore.Ledger.Order(fAcct.Name, tInstr.FullName, fAction.ToString(), "Market", fQty, 0, "Copier:mirror"); } catch (Exception _sx) { SentinelCore.Swallow("SentinelCopier.MirrorToFollower", _sx); }
                    Log("MIRROR ▶ " + fAcct.Name + " " + fAction + " " + fQty + " "
                        + tInstr.FullName + " (leader " + leaderQty + " " + exec.Instrument.FullName + ")");
                }
                catch (Exception ex)
                {
                    Log("submit FAILED for '" + f.AccountName + "': " + ex.Message);
                }
            }
            catch (Exception ex)
            {
                Log("MirrorToFollower('" + (f != null ? f.AccountName : "?") + "') error: " + ex.Message);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  MANUAL-ASSIST: reuse the map/size/Eye-gate pipeline, but PUBLISH a "place this by hand"
        //  ticket instead of submitting an order. Mirrors the leader's exact action (fill-mirror), so
        //  no follower-account lookup is needed — the account name is just a label the user places on.
        // ─────────────────────────────────────────────────────────────────────
        private void EmitAssistTicket(FollowerConfig f, Execution exec, string leaderSym,
                                      bool leaderIsBuy, int leaderQty)
        {
            try
            {
                InstrumentMapEntry map;
                f.InstrumentMap.TryGetValue(leaderSym, out map);
                double ratio = map != null ? map.SizeRatio : 1.0;
                int fQty = (int)Math.Round(leaderQty * ratio * f.Multiplier);
                if (fQty <= 0) { Log("assist skip: '" + f.AccountName + "' computed qty <= 0"); return; }

                Instrument tInstr = ResolveTargetInstrument(exec.Instrument, map);
                string tName = tInstr != null ? tInstr.FullName
                    : (map != null && !string.IsNullOrEmpty(map.TargetSymbol) ? map.TargetSymbol : leaderSym);

                OrderAction fAction = exec.Order.OrderAction;   // fill-mirror: place exactly what the leader did
                bool isEntry = fAction == OrderAction.Buy || fAction == OrderAction.SellShort;

                // Eye-gate entries, same policy as the auto path (exits always pass so you stay in sync).
                if (_config != null && _config.UseEyeGate && isEntry)
                {
                    int wantDir = fAction == OrderAction.Buy ? 1 : -1;
                    var verdict = SentinelCore.GetEyeVerdict(leaderSym, 30);
                    if (verdict == null || verdict.Direction != wantDir)
                    {
                        Log("Assist Eye-gate BLOCKED ▶ " + f.AccountName + " " + fAction + " " + tName
                            + " — Eye " + leaderSym + " = "
                            + (verdict == null ? "no/stale verdict" : "dir " + verdict.Direction + " score " + verdict.Score.ToString("0")));
                        return;
                    }
                }

                var v = SentinelCore.GetEyeVerdict(leaderSym, 60);
                string eyeCtx = (v != null && v.Direction != 0) ? " · Eye " + (v.Direction > 0 ? "L" : "S") + v.Score.ToString("0") : "";

                SentinelCore.PublishAssistTicket(new SentinelCore.AssistTicket
                {
                    TimeUtc    = DateTime.UtcNow,
                    Account    = f.AccountName,
                    Action     = fAction.ToString(),
                    Qty        = fQty,
                    Instrument = tName,
                    IsEntry    = isEntry,
                    Context    = leaderQty + " " + leaderSym + " " + (leaderIsBuy ? "buy" : "sell") + eyeCtx
                });
                Log("ASSIST ▶ PLACE " + fAction + " " + fQty + " " + tName + " on '" + f.AccountName
                    + "'  (leader " + leaderQty + " " + leaderSym + (leaderIsBuy ? " buy" : " sell") + ")" + eyeCtx);
            }
            catch (Exception ex)
            {
                Log("EmitAssistTicket('" + (f != null ? f.AccountName : "?") + "') error: " + ex.Message);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Helpers (several marked VALIDATE — confirm exact API on first NT compile/run)
        // ─────────────────────────────────────────────────────────────────────

        private static Account FindAccount(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            lock (Account.All)
            {
                foreach (Account a in Account.All)
                    if (a != null && string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))
                        return a;
            }
            return null;
        }

        // VERIFIED: Account.Connection (@ProfitLoss.cs:89) + Connection.Status (@BarTimer.cs:50).
        private static bool IsConnected(Account a)
        {
            try
            {
                return a != null && a.Connection != null
                    && a.Connection.Status == ConnectionStatus.Connected;
            }
            catch { return true; }   // fail-open: a lookup hiccup shouldn't silently drop mirrors
        }

        // v0.1.0f: is the account entirely flat? (used by the governor gate — a fill on a flat account
        // is a NEW entry; a non-flat account is managing/closing and is never governor-blocked). Fail-open.
        private static bool IsAccountFlat(Account a)
        {
            try { foreach (Position p in a.Positions) if (p != null && p.MarketPosition != MarketPosition.Flat) return false; }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelCopier.IsAccountFlat", _sx); }
            return true;
        }

        // Same-provider prop rule. SIM leader is EXEMPT (local, no firm). Coarse first cut:
        // compare leader vs follower provider identity. Refine to per-firm grouping later.
        private bool ProviderAllowed(Account leader, Account follower, out string reason)
        {
            reason = null;
            if (_config.Policy == ProviderPolicy.Off) return true;
            if (IsSimAccount(leader)) return true;   // SIM leader exempt (see memory)

            string lp = ProviderKey(leader);
            string fp = ProviderKey(follower);
            if (string.Equals(lp, fp, StringComparison.OrdinalIgnoreCase)) return true;

            reason = "cross-provider (" + lp + " → " + fp + ")";
            if (_config.Policy == ProviderPolicy.Warn) { Log("WARN " + reason); return true; }
            return false;   // Block
        }

        // VERIFIED: Connection.Options.Name (AutoReconnect.cs:247) — the CONNECTION name, which is
        // the firm-level key we want (Provider enum would be too coarse: two Rithmic firms collide).
        private static string ProviderKey(Account a)
        {
            try
            {
                if (a != null && a.Connection != null && a.Connection.Options != null)
                    return a.Connection.Options.Name ?? "?";
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelCopier.ProviderKey", _sx); }
            return "?";
        }

        private static bool IsSimAccount(Account a)
        {
            // heuristic (matches MAECaptureService): name contains "Sim". VALIDATE: prefer a
            // real Simulation flag / Provider.Simulation if this build exposes one.
            return a != null && a.Name != null
                && a.Name.IndexOf("Sim", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Resolve the follower's target contract. Same symbol → same instrument object.
        // Cross-map (GC→MGC) → resolve the mapped master symbol, keeping the source expiry.
        // VALIDATE: front-month vs same-expiry resolution + exact full-name format ("MGC 08-26").
        private Instrument ResolveTargetInstrument(Instrument source, InstrumentMapEntry map)
        {
            if (source == null) return null;
            if (map == null || string.IsNullOrEmpty(map.TargetSymbol)
                || string.Equals(map.TargetSymbol, source.MasterInstrument.Name, StringComparison.OrdinalIgnoreCase))
                return source;   // no cross-map → mirror on the same instrument

            // try same-expiry contract first (keep the leader's month), then bare symbol.
            try
            {
                string suffix = source.FullName.Contains(" ")
                    ? source.FullName.Substring(source.FullName.IndexOf(' '))   // " 08-26"
                    : "";
                Instrument byExpiry = Instrument.GetInstrument(map.TargetSymbol + suffix);
                if (byExpiry != null) return byExpiry;
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelCopier.ResolveTargetInstrument", _sx); }
            try { return Instrument.GetInstrument(map.TargetSymbol); } catch { return null; }
        }

        // Choose entry-vs-exit action so brokers that distinguish SellShort/BuyToCover accept it.
        // VALIDATE: Account.Positions surface (name/type). LIMITATION: a fill that crosses zero
        // (cover + flip in one event) needs an order split — deferred; skeleton uses one action.
        private static OrderAction ResolveFollowerAction(bool leaderIsBuy, Account fAcct, Instrument tInstr)
        {
            MarketPosition mp = MarketPosition.Flat;
            try
            {
                if (fAcct != null && fAcct.Positions != null)
                {
                    foreach (Position p in fAcct.Positions)
                    {
                        if (p != null && p.Instrument == tInstr) { mp = p.MarketPosition; break; }
                    }
                }
            }
            catch { /* fall back to Flat → Buy/SellShort */ }

            if (leaderIsBuy) return mp == MarketPosition.Short ? OrderAction.BuyToCover : OrderAction.Buy;
            return mp == MarketPosition.Long ? OrderAction.Sell : OrderAction.SellShort;
        }

        // suite helper: is this order one of ours? (so other tools never touch copier orders)
        public static bool IsCopierOrder(string orderName)
        {
            return orderName != null && orderName.StartsWith(OrderPrefix, StringComparison.Ordinal);
        }

        private void Log(string msg)
        {
            SentinelCore.Log("Copy", msg);   // "[Sentinel:Copy] ..." in the Output window
        }
    }
}
