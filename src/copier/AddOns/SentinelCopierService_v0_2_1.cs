// ─────────────────────────────────────────────────────────────────────────────
// This Source Code Form is subject to the terms of the Mozilla Public License,
// v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
// one at https://mozilla.org/MPL/2.0/.
//
// Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
// ─────────────────────────────────────────────────────────────────────────────
// ═════════════════════════════════════════════════════════════════════════════
//  SentinelCopierService — headless multi-account trade copier (NT8 AddOn)
//  File: SentinelCopierService_v0_2_1.cs
//  Service version: v0.2.1   (mirror path DRIVEN on real accounts; reconciler validated + FAILED)
// ─────────────────────────────────────────────────────────────────────────────
//  WHAT THIS IS  (part of the "Sentinel Suite"; see Docs/ROADMAP.md)
//    A headless, always-on AddOn service — SAME architecture as MAECaptureService:
//    an AddOnBase that lives for the NT process, exposes a static Instance so a future
//    dashboard/settings window ATTACHES to it, and never depends on any window being
//    open. Closing a UI must never stop copying.
//    ⛔ "SINGLETON" IS AN ASPIRATION, NOT A GUARANTEE — MEASURED FALSE 2026-08-11.
//    Two instances were found alive in one NT (every log line emitted exactly twice,
//    3.78 s apart on one 30 s timer). Suspected cause: an assembly reload leaves the
//    old assembly's AddOn un-Terminated and still running. ⇒ Anything whose
//    correctness depends on there being ONE of us must be enforced somewhere that
//    outlives the assembly — see MirrorClaim. Do not add a `static` guard and believe
//    it: across a reload the new assembly gets its own statics.
//
//  WHAT IT DOES  (pure FILL-MIRROR copier — the "Horde" model)
//    • Subscribes to the LEADER account's ExecutionUpdate.
//    • On each LEADER fill (Filled/PartialFilled), for every enabled+connected FOLLOWER:
//        - size  = leaderFillQty × instrumentSizeRatio × followerMultiplier
//        - symbol = per-follower instrument map (e.g. GC→MGC), else same instrument
//        - submits a MARKET order on the follower account (unmanaged, account-level).
//    • Pure fill-mirror: it never reads the leader strategy's internal state. Entry AND
//      exit both arrive as fills, so each follower's NET position stays synced with the
//      leader automatically. Works with the Bridge, manual ChartTrader, or any strategy.
//
//  CREDIT / ATTRIBUTION  (required before any open-source release)
//    The fill-mirror engine (subscribe to master ExecutionUpdate → qty×multiplier → market
//    CreateOrder+Submit, Guid order name) is ADAPTED from the TickHunter NT "Horde" copier by
//    **Frosty** (TradeLikeAZombie community). Even the "SentCopy_" order prefix echoes their
//    "TH-Horde_". Sentinel ADDS: same-provider prop rule, bidirectional GC↔MGC / CL↔MCL map,
//    SentinelCore kill-switch + feed-health gating and manual-assist mode. Thanks, Frosty.
//
//  LEADER MODEL (decided — see memory copier-samples-analysis)
//    The leader is a SIGNAL account (a dedicated SIM or small live acct trading native GC).
//    EVERY real account — including what the user calls "primary" — is an execution TARGET
//    with its own instrument map (GC→MGC) + size. This satisfies "primary cross-trades"
//    AND "SIM leader → prop accounts" with ZERO changes to the Bridge's order path.
//
//  SUITE CONTRACTS (how this ties into the rest of the Sentinel Suite)
//    • ORDER-NAME PREFIX  = "SentCopy_"  → distinguishes copier orders from strategy orders
//      (the Bridge uses "GTS_"; keep prefixes disjoint so tools never edit each other's orders).
//    • SHARED KILL-SWITCH = SentinelCore.KillSwitchEngaged (in SentinelCore_v1_0_0.cs). Any
//      suite tool (a risk monitor, the panel's lockout, the dashboard) flips it to halt ALL
//      mirroring. The copier no longer owns its own flag — it consults the suite Core.
//    • MIRROR GATE        = SentinelCore.CanAct() + per-follower connection health, the single
//      choke point every mirror passes through. Core's feed-health probe (once a health tool
//      registers one — e.g. a pulled predecessor’s v0.1.2 lag metric) gates each account automatically.
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
//    v0.2.1 — ONE LEADER FILL ⇒ ONE MIRROR, however many instances are alive.
//             Fork of v0.2.0; v0.2.0 frozen to bin\_archive\ so two copiers cannot both run.
//             ⭐ THIS FIXES A DEFECT THAT WAS MEASURED, NOT SUSPECTED (main, 2026-08-11):
//             one leader BuyToCover produced TWO `MIRROR ▶` lines 26 ms apart and left the
//             follower LONG +1 against a FLAT leader — a side the leader never took. Cause:
//             two live service instances, each with its own per-instance _seenExecIds, so
//             neither could see the other's mirror.
//             • NEW `MirrorClaim` — an AppDomain-hosted, reload-surviving atomic claim on
//               (leader, executionId). Read its header before changing it: a `static` field
//               CANNOT fix this, because the orphan lives in a different assembly with its
//               own statics. Fails CLOSED (no claim ⇒ no mirror): a duplicate order is
//               unrecoverable, a missed one is visible and the reconciler reports it.
//             • _seenExecIds is retained but DEMOTED to diagnostics — it is no longer the guard.
//             ⚠ STILL OPEN (do not read this version as closing them): the two instances are
//               not PREVENTED, only made harmless on the mirror path — the reconciler still
//               double-reports; and the reconciler's flat-leader blind spot is v0.2.2 / item 2b.
//    v0.2.0 — COPY MODE + THE POSITION RECONCILER. (fork of v0.1.0h; v0.1.0 frozen to
//             bin\_archive\copier-v0_1_0-superseded-2026-08-09\ so two singletons cannot both run.)
//             • CopyMode {Fill,Order} per follower, default Fill — an axis INDEPENDENT of
//               FollowerMode {Auto,Manual}: Copy Mode is WHAT we mirror, FollowerMode is HOW it
//               reaches the follower. Order+Manual is a real combination.
//             • Copy.conf field 5, APPENDED not inserted: a v0.1.0 four-field file still loads and
//               defaults to Fill. An UNRECOGNISED value logs and stays Fill — a config we cannot
//               read is one the operator believes says something else.
//             • ⛔ ORDER MODE FAILS CLOSED. Not implemented in v0.2.0, so an Order-mode follower is
//               REFUSED (at Subscribe AND again in MirrorToFollower, so a reload cannot sneak one
//               onto the Fill path) rather than silently mirrored on fills. A copier that works but
//               not the way it was configured, saying nothing, is the worse outcome.
//             • POSITION RECONCILER, every 30s, for BOTH modes: follower net vs leader net × ratio
//               × multiplier, per mapped instrument. Fill mode drifts too — a rejected mirror, a
//               partial, a hand-placed follower trade, a restart mid-flight — and nothing noticed.
//               ⭐ Alerts only after TWO consecutive disagreements: a mirror in flight IS a
//               divergence for a moment, and an alert that fires on healthy operation gets ignored.
//               ⛔ REPORTS, NEVER CORRECTS — a wrong "fix" on a real position costs far more than a
//               delta the operator is told about (cf. the unmanaged-restart orphan-cancel lesson).
//             VALIDATE on SIM: Position.Quantity/MarketPosition freshness inside the timer callback,
//             and that the streak logic does not alarm during normal mirror latency.
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
//             mirror pipeline; mirrors the leader's exact action; the account name is just a label
//             (need not be an NT account). For prop firms that bar automated copy-trading (TPT eval/
//             PRO, Bulenox) — decision-support instead of auto-execution. Auto path unchanged.
//    v0.1.0b — (in-place) an entry qualifier gate (REMOVED 2026-08-11 with its producer)
//             (exits always mirrored).
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
using System.Collections;            // non-generic Hashtable: the cross-assembly mirror claim (see MirrorClaim)
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

    // ── COPY MODE — WHAT WE MIRROR. An axis independent of FollowerMode, which is HOW the mirror
    //    reaches the follower (submit vs assist ticket). Order+Manual is a real combination: mirror
    //    the leader's working order as a ticket for a firm you would rather hand-place into.
    //
    //  Fill  (default) — mirror EXECUTIONS. Only ever copies things that actually happened, so a
    //        follower can never hold a position the leader never took. Cannot mirror a resting order.
    //  Order — mirror ORDER events. Faster off the mark and mirrors working orders, but it copies
    //        INTENT: if the leader's limit never fills and the follower's does, they diverge and
    //        nothing in the fill path will ever notice. That is why Order mode is inseparable from
    //        the position reconciler below — an Order-mode copier without one looks fine for weeks.
    public enum CopyMode { Fill, Order }

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
        public CopyMode CopyMode = CopyMode.Fill;       // Fill = mirror executions; Order = mirror intent
        public double Multiplier = 1.0;  // extra per-follower size scaling on top of ratio
        // leader master symbol (e.g. "GC") → target (e.g. MGC ×10). Empty = same instrument.
        public Dictionary<string, InstrumentMapEntry> InstrumentMap =
            new Dictionary<string, InstrumentMapEntry>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class CopierConfig
    {
        public string LeaderAccount;                 // the SIGNAL account we mirror FROM
        public ProviderPolicy Policy = ProviderPolicy.Warn;
        public List<FollowerConfig> Followers = new List<FollowerConfig>();
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  MirrorClaim — "this leader fill is MINE to mirror", process-wide.
    //
    //  WHY THIS EXISTS (measured on main 2026-08-11, not theorised): a single leader
    //  BuyToCover produced TWO `MIRROR ▶` lines 26 ms apart and drove the follower from
    //  -1 to LONG +1 — a side the leader never took. TWO service instances were alive in
    //  one NT: every copier log line emitted exactly twice, 3.781/3.789/3.790 s apart on
    //  one 30 s timer period. The old guard was a per-instance HashSet, so neither
    //  instance could see the other's mirror.
    //
    //  WHY IT IS NOT A `static` FIELD — the trap that makes the obvious fix useless:
    //  the instances are suspected to come from an ASSEMBLY RELOAD, where the old
    //  assembly's AddOn is never Terminated and keeps running. Across a reload the new
    //  assembly gets its OWN copy of every static, so a static set (or a static
    //  `Instance` guard) is invisible to the orphan and cannot stop it. The claim must
    //  live somewhere that OUTLIVES the assembly. Two consequences shape the code below:
    //    • the store hangs off AppDomain.CurrentDomain, which spans reloads; and
    //    • it MUST be a type from a stable assembly. A Hashtable from mscorlib is the
    //      same type to both assemblies; anything declared HERE would be a different
    //      type per load and the cast back would silently fail — which fails OPEN, i.e.
    //      straight back to double-mirroring. That is why this is a non-generic
    //      Hashtable and not the HashSet<string> you would otherwise reach for.
    //    • the creation lock is an INTERNED string for the same reason: the intern pool
    //      is process-wide, so both assemblies lock the identical object.
    //
    //  ⛔ FAILS CLOSED, deliberately: if the claim cannot be taken we do NOT mirror.
    //  A duplicate order is unrecoverable; a missed mirror is visible and the reconciler
    //  is built to report it.
    // ═════════════════════════════════════════════════════════════════════════
    internal static class MirrorClaim
    {
        private const string SlotKey    = "Sentinel.Copier.MirroredExecutionIds.v1";
        private const int    PruneAbove = 4000;   // bound the store; ids are only useful while fresh

        // Interned → the SAME object in every assembly loaded into this AppDomain.
        private static readonly object CreateGate = string.Intern("Sentinel.Copier.MirrorClaim.CreateGate.v1");

        private static Hashtable Store()
        {
            AppDomain ad = AppDomain.CurrentDomain;
            Hashtable t = ad.GetData(SlotKey) as Hashtable;
            if (t != null) return t;
            lock (CreateGate)
            {
                t = ad.GetData(SlotKey) as Hashtable;      // re-check: another instance may have won
                if (t == null)
                {
                    t = new Hashtable();
                    ad.SetData(SlotKey, t);
                }
                return t;
            }
        }

        /// <summary>
        /// TRUE exactly once per (leader, executionId), for the whole NT process — including
        /// across an assembly reload. Every later caller, in any instance, gets FALSE.
        /// </summary>
        internal static bool TryClaim(string leaderAccount, string executionId)
        {
            if (string.IsNullOrEmpty(executionId)) return false;   // cannot dedupe it ⇒ do not mirror it
            string key = (leaderAccount ?? "?") + "|" + executionId;
            try
            {
                Hashtable t = Store();
                lock (t.SyncRoot)                       // check + set must be ONE atomic step
                {
                    if (t.ContainsKey(key)) return false;
                    if (t.Count >= PruneAbove) t.Clear();
                    t[key] = null;
                    return true;
                }
            }
            catch (Exception) { return false; }         // fail CLOSED — see the header
        }

        /// <summary>Diagnostics only: how many claims the process is currently holding.</summary>
        internal static int Held { get { try { Hashtable t = Store(); lock (t.SyncRoot) return t.Count; } catch { return -1; } } }
    }

    public class SentinelCopierService_v0_2_1 : NinjaTrader.NinjaScript.AddOnBase
    {
        private const string OrderPrefix = "SentCopy_";     // suite contract: copier order tag

        // ── singleton so a future dashboard/settings window can attach ──
        public static SentinelCopierService_v0_2_1 Instance { get; private set; }

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

        // ── copy-mode refusal: log once per account, not once per fill ──
        private readonly HashSet<string> _orderModeWarned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ── POSITION RECONCILER ───────────────────────────────────────────────
        // Answers the one question neither copy path can answer about itself: is the follower
        // actually where the leader is? Fill mode can still drift — a rejected mirror, a partial,
        // a hand-placed trade on the follower, a restart mid-flight — and today NOTHING notices.
        // Order mode cannot ship without this at all, because it copies intent.
        //
        // ⭐ TWO CONSECUTIVE DISAGREEMENTS BEFORE IT SPEAKS. A mirror in flight IS a divergence for
        // a moment, so a single sample would cry wolf on every single trade — and an alert that
        // fires on healthy operation is one the operator learns to ignore, which is worse than no
        // alert. Same lesson this suite already applies to every NT state read.
        private System.Threading.Timer _reconcileTimer;
        private readonly Dictionary<string, int> _divergedStreak     // "acct|instrument" → consecutive bad samples
            = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private const int ReconcileSeconds = 30;
        private const int DivergeStreakToAlert = 2;

        // ─────────────────────────────────────────────────────────────────────
        protected override void OnStateChange()
        {
            // base ctor calls this BEFORE our ctor — do not rely on field initializers.
            if (State == State.SetDefaults)
            {
                Name = "SentinelCopierService_v0_2_1";
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

            // The reconciler runs regardless of copy mode: Fill can drift too (a rejected mirror, a
            // partial, a hand-placed follower trade, a restart mid-flight) and nothing noticed before.
            _reconcileTimer = new System.Threading.Timer(ReconcileTick, null,
                ReconcileSeconds * 1000, ReconcileSeconds * 1000);

            Log("SentinelCopierService v0.2.1 started. Leader='" + (_config.LeaderAccount ?? "<none>")
                + "', followers=" + _config.Followers.Count
                + ", reconciler every " + ReconcileSeconds + "s"
                + (string.IsNullOrEmpty(_config.LeaderAccount) ? " (inert until a leader is set)." : "."));
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Stop — unsubscribe every hooked account.
        // ─────────────────────────────────────────────────────────────────────
        private void Stop()
        {
            if (!_started) return;
            _started = false;
            if (_reconcileTimer != null)
            {
                try { _reconcileTimer.Dispose(); }
                catch (Exception _sx) { SentinelCore.Swallow("SentinelCopier.Stop", _sx); }
                _reconcileTimer = null;
            }
            _divergedStreak.Clear();
            _orderModeWarned.Clear();
            Unsubscribe();
            if (Instance == this) Instance = null;
            Log("SentinelCopierService stopped; leader subscription released, reconciler disposed.");
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

            // ⛔ ORDER MODE IS NOT BUILT YET, AND IT FAILS CLOSED RATHER THAN FALLING BACK.
            // Silently mirroring an Order-mode follower on the Fill path would give the operator a
            // copier that works — just not the one they configured — and nothing would ever say so.
            // Refusing is the honest state: they see it at startup, before a trade, not after one.
            foreach (FollowerConfig f in _config.Followers)
            {
                if (f == null || !f.Enabled || f.CopyMode != CopyMode.Order) continue;
                Log("REFUSING follower '" + f.AccountName + "': copy mode ORDER is not implemented "
                    + "in v0.2.1 either (the mirror path and its position reconciler land together — an "
                    + "Order-mode copier without reconciliation diverges invisibly). This follower "
                    + "will NOT be mirrored at all. Set it to fill in Copy.conf to copy it now.");
            }
        }

        // ── the reconciler: does each follower's NET position match the leader's, scaled? ────────
        // Reports only. It never places or cancels an order to "fix" a difference: a wrong
        // correction on a real position is far more expensive than a delta the operator is told
        // about, and this suite already learned that from unmanaged restart reconciliation
        // (a mistaken "orphan" cancel kills a live stop).
        private void ReconcileTick(object _)
        {
            try
            {
                CopierConfig cfg = _config;
                if (cfg == null || string.IsNullOrEmpty(cfg.LeaderAccount)) return;
                Account leader = FindAccount(cfg.LeaderAccount);
                if (leader == null || leader.Positions == null) return;

                foreach (FollowerConfig f in cfg.Followers)
                {
                    if (f == null || !f.Enabled || string.IsNullOrEmpty(f.AccountName)) continue;
                    if (f.Mode == FollowerMode.Manual) continue;   // we do not place these; not ours to reconcile
                    Account fa = FindAccount(f.AccountName);
                    if (fa == null || !IsConnected(fa) || fa.Positions == null) continue;

                    foreach (Position lp in leader.Positions)
                    {
                        if (lp == null || lp.Instrument == null) continue;
                        string leaderSym = lp.Instrument.MasterInstrument != null
                            ? lp.Instrument.MasterInstrument.Name : null;
                        if (string.IsNullOrEmpty(leaderSym)) continue;

                        double ratio = 1.0;
                        string targetSym = leaderSym;
                        InstrumentMapEntry me;
                        if (f.InstrumentMap != null && f.InstrumentMap.TryGetValue(leaderSym, out me) && me != null)
                        {
                            targetSym = me.TargetSymbol;
                            ratio = me.SizeRatio;
                        }

                        int leaderNet = SignedQty(lp);
                        int expected = (int)Math.Round(leaderNet * ratio * f.Multiplier);
                        int actual = 0;
                        foreach (Position fp in fa.Positions)
                        {
                            if (fp == null || fp.Instrument == null || fp.Instrument.MasterInstrument == null) continue;
                            if (!string.Equals(fp.Instrument.MasterInstrument.Name, targetSym,
                                               StringComparison.OrdinalIgnoreCase)) continue;
                            actual += SignedQty(fp);
                        }

                        string key = f.AccountName + "|" + targetSym;
                        if (expected == actual)
                        {
                            int had;
                            if (_divergedStreak.TryGetValue(key, out had) && had >= DivergeStreakToAlert)
                                Log("RECONCILED: " + key + " is back in line at " + actual + ".");
                            _divergedStreak.Remove(key);
                            continue;
                        }

                        int streak;
                        _divergedStreak.TryGetValue(key, out streak);
                        streak++;
                        _divergedStreak[key] = streak;
                        if (streak != DivergeStreakToAlert) continue;   // speak once, on crossing
                        Log("🔴 POSITION DIVERGENCE: follower '" + f.AccountName + "' holds " + actual
                            + " " + targetSym + " but the leader implies " + expected
                            + " (leader " + leaderNet + " " + leaderSym + " × ratio " + ratio
                            + " × mult " + f.Multiplier + "). Seen on " + streak
                            + " consecutive checks " + ReconcileSeconds + "s apart. NOT auto-corrected — "
                            + "reconciling by hand is the operator's call.");
                        try { SentinelCore.Log("Copy", "DIVERGENCE " + key + " actual=" + actual + " expected=" + expected); }
                        catch (Exception _sx) { SentinelCore.Swallow("SentinelCopier.Reconcile", _sx); }
                    }
                }
            }
            catch (Exception ex) { Log("ReconcileTick error: " + ex.Message); }
        }

        // Net signed quantity of a position: long positive, short negative, flat zero.
        private static int SignedQty(Position p)
        {
            if (p == null || p.MarketPosition == MarketPosition.Flat) return 0;
            return p.MarketPosition == MarketPosition.Long ? p.Quantity : -p.Quantity;
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
                foreach (FollowerConfig f in cfg.Followers)
                {
                    if (f == null || string.IsNullOrEmpty(f.AccountName)) continue;
                    sb.AppendLine("follower=" + f.AccountName
                        + "|" + (!f.Enabled ? "off" : (f.Mode == FollowerMode.Manual ? "manual" : "on"))
                        + "|" + f.Multiplier.ToString(CultureInfo.InvariantCulture)
                        + "|" + MapToDsl(f.InstrumentMap)
                        // field 5 = copy mode. APPENDED, never inserted: a Copy.conf written by
                        // v0.1.0 has four fields and must keep loading, defaulting to Fill.
                        + "|" + (f.CopyMode == CopyMode.Order ? "order" : "fill"));
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
                        SentinelCore.Log("Copy", "OBSOLETE KEY IGNORED: 'eyeGate' was removed 2026-08-11 along with the qualifier "
                            + "it consulted. It is NOT in force. Delete the line from Copy.conf. (Silently "
                            + "ignoring it is how this gate blocked every ENTRY for 19 days unnoticed.)");
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
                        // Absent field 5 (any v0.1.0 file) => Fill. An UNRECOGNISED value is NOT
                        // silently treated as Fill: it is logged and left at Fill, because a config
                        // we cannot read is a config the operator believes says something else.
                        if (parts.Length > 4)
                        {
                            string cm = parts[4].Trim();
                            if (cm.Equals("order", StringComparison.OrdinalIgnoreCase)) f.CopyMode = CopyMode.Order;
                            else if (!cm.Equals("fill", StringComparison.OrdinalIgnoreCase) && cm.Length > 0)
                                SentinelCore.Log("Copy", "LoadConfig: follower '" + f.AccountName
                                    + "' has an unrecognised copy mode '" + cm + "' — using Fill. "
                                    + "Valid values: fill, order.");
                        }
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

                // DEDUPE — PROCESS-WIDE, not per-instance. Measured 2026-08-11 on main: one leader fill
                // produced TWO mirrors 26 ms apart and left the follower LONG on a FLAT leader, because
                // two service instances were alive and each had its own _seenExecIds. A per-instance set
                // cannot see another instance's mirror, so it cannot prevent the double. See MirrorClaim.
                if (exec.ExecutionId != null)
                {
                    if (!MirrorClaim.TryClaim(cfg.LeaderAccount, exec.ExecutionId)) return;
                    lock (_lock) { _seenExecIds.Add(exec.ExecutionId); }   // local copy, diagnostics only
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
                // ⛔ The refusal is enforced HERE too, not only announced at Subscribe(). A config
                // reload, a Reconfigure() or a hand-edit between startup and this fill must not be
                // able to sneak an Order-mode follower onto the Fill path.
                if (f.CopyMode == CopyMode.Order)
                {
                    if (_orderModeWarned.Add(f.AccountName ?? ""))
                        Log("skip: follower '" + f.AccountName + "' is copy mode ORDER, which is not "
                            + "implemented — NOT mirroring (it is not silently copied on the Fill path).");
                    return;
                }

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
        //  MANUAL-ASSIST: reuse the map/size pipeline, but PUBLISH a "place this by hand"
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

                SentinelCore.PublishAssistTicket(new SentinelCore.AssistTicket
                {
                    TimeUtc    = DateTime.UtcNow,
                    Account    = f.AccountName,
                    Action     = fAction.ToString(),
                    Qty        = fQty,
                    Instrument = tName,
                    IsEntry    = isEntry,
                    Context    = leaderQty + " " + leaderSym + " " + (leaderIsBuy ? "buy" : "sell") 
                });
                Log("ASSIST ▶ PLACE " + fAction + " " + fQty + " " + tName + " on '" + f.AccountName
                    + "'  (leader " + leaderQty + " " + leaderSym + (leaderIsBuy ? " buy" : " sell") + ")");
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
