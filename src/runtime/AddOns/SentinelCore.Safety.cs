// =============================================================================
//  SentinelCore - SAFETY layer  (partial)
//  File: SentinelCore.Safety.cs   |   part of static partial class SentinelCore
// -----------------------------------------------------------------------------
//  PRODUCT-LADDER RUNTIME SPLIT - see Docs/PRODUCT_LADDER.md sec 4-5.
//  L2 SAFETY = the account-risk DECISION logic (feed-health, CanEnter, governor,
//  drawdown, account profiles, session, sizing, order guards, GateEntry). This is
//  the ONE file the Skins/Sensors bundles OMIT (they never place an order).
//  Depends DOWNWARD on Foundation only (news/rollover/InstrumentRoot/Ledger).
//  Migrated 2026-07-10 batch 2: governor/drawdown/profiles/sizing/gate block.
//  Same class, same call sites -> zero consumer churn.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.Sentinel
{
    public static partial class SentinelCore
    {
        // ─────────────────────────────────────────────────────────────────────
        //  CONSISTENCY GOVERNOR (v1.0.7) — per-ACCOUNT daily prop-firm gate (Docs/
        //  CONSISTENCY_GOVERNOR_SPEC.md). A host (Risk) tracks each account's daily realized P&L vs its
        //  firm cap (consistency: best_day ≤ R×target → cap each day at R×target) + a loss-stop, and
        //  publishes state here. Consumers gate ENTRIES via TradingAllowedToday(). Keyed by account name.
        //  FAIL-OPEN: an account with no published governor state trades normally.
        // ─────────────────────────────────────────────────────────────────────
        public sealed class GovernorState
        {
            public string   Account, Status, Reason;   // Status: "Trading"/"DayComplete"/"DayHalted"
            public bool     Allowed;
            public double   DailyPnl, Cap, LossStop, RecommendedSize;
            public DateTime UpdatedUtc;
        }

        private static readonly Dictionary<string, GovernorState> _gov =
            new Dictionary<string, GovernorState>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _govLock = new object();

        public static void SetGovernorState(GovernorState g)
        {
            if (g == null || string.IsNullOrEmpty(g.Account)) return;
            g.UpdatedUtc = DateTime.UtcNow;
            lock (_govLock) { _gov[g.Account] = g; }
        }

        public static GovernorState GetGovernorState(string account)
        {
            if (string.IsNullOrEmpty(account)) return null;
            lock (_govLock) { GovernorState g; return _gov.TryGetValue(account, out g) ? g : null; }
        }

        public static List<GovernorState> AllGovernorStates()
        {
            lock (_govLock) { return new List<GovernorState>(_gov.Values); }
        }

        // ── governor daily-reset clock (v1.1.0) ─────────────────────────────
        // Prop firms reset the daily P&L at a specific hour (often 17:00 ET), NOT your machine's
        // midnight. A wrong reset = silently breaking the daily-loss / consistency rule. Set the
        // LOCAL hour the trading day rolls over; the Risk governor uses it, and it's DISPLAYED so
        // you can confirm it matches your firm. Default 0 = machine midnight (legacy behavior).
        private static int _govResetHour = 0;
        public static int GovernorResetHour => _govResetHour;
        public static void SetGovernorResetHour(int hourLocal) { _govResetHour = ((hourLocal % 24) + 24) % 24; }
        /// <summary>Human label for the daily reset, e.g. "resets 17:00 local". Shown on the dashboard.</summary>
        public static string GovernorResetLabel =>
            "resets " + _govResetHour.ToString("00") + ":00 local"
            + (_govResetHour == 0 ? " (midnight — set resetHour= to match your prop firm, e.g. 17 for 5pm)" : "");

        /// <summary>Entry consult: is this account allowed to open new trades today? Fail-open (no state → true).</summary>
        public static bool TradingAllowedToday(Account acct)
        {
            if (acct == null || acct.Name == null) return true;
            var g = GetGovernorState(acct.Name);
            return g == null || g.Allowed;
        }

        /// <summary>Recommended position-size scale for an account (1.0 = full). Fail-open (no state → 1.0).</summary>
        public static double RecommendedSize(Account acct)
        {
            if (acct == null || acct.Name == null) return 1.0;
            var g = GetGovernorState(acct.Name);
            return (g == null || g.RecommendedSize <= 0) ? 1.0 : g.RecommendedSize;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  TRAILING DRAWDOWN (v1.3.1) — per-ACCOUNT peak-equity floor, the line the prop firm
        //  liquidates at. Distinct from the GOVERNOR (which is daily REALIZED P&L vs a cap/loss):
        //  THIS tracks lifetime EQUITY = realized balance + OPEN P&L against a TRAILING threshold that
        //  ratchets up off the peak and NEVER comes back down. The dangerous case the governor misses:
        //  you're up $800 open, the firm's trail sits $200 under your peak equity, price ticks against
        //  you — one tick past the floor and the account is gone, with the daily P&L still green.
        //  A host (Risk, which owns account P&L) computes peak/floor/cushion (peak is PERSISTED so a
        //  restart never loses the high-water mark) and publishes state here. Consumers gate ENTRIES via
        //  DrawdownAllowsEntry() (blocks when the cushion is thin — stop ADDING risk near the floor); the
        //  host may additionally auto-flatten a hair ABOVE the floor to beat the firm's engine. The core
        //  owns NO firm-specific math — it just carries the computed flags. FAIL-OPEN (no state → allowed).
        //  Keyed by account name.
        // ─────────────────────────────────────────────────────────────────────
        public sealed class DrawdownState
        {
            public string   Account, DdType, Reason;   // DdType: trailing / static / eod
            public double   Equity;        // realized balance + open P&L (what the firm trails)
            public double   PeakEquity;    // high-water mark of Equity (persisted; never decreases for trailing)
            public double   Floor;         // liquidation level = PeakEquity - DdAmount (static: pinned)
            public double   Cushion;       // Equity - Floor  ($ to the floor; ≤0 = at/through it)
            public double   DdAmount;      // the firm's trailing threshold $
            public bool     Warn;          // cushion inside the warn buffer (getting close)
            public bool     EntryBlocked;  // stop opening NEW risk (cushion inside the entry buffer)
            public bool     Breach;        // at/through the flatten line — get flat NOW
            public DateTime UpdatedUtc;
        }

        private static readonly Dictionary<string, DrawdownState> _dd =
            new Dictionary<string, DrawdownState>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _ddLock = new object();

        /// <summary>Host (Risk) publishes the computed trailing-drawdown state for an account (each governor tick).</summary>
        public static void SetDrawdownState(DrawdownState d)
        {
            if (d == null || string.IsNullOrEmpty(d.Account)) return;
            d.UpdatedUtc = DateTime.UtcNow;
            lock (_ddLock) { _dd[d.Account] = d; }
        }

        public static DrawdownState GetDrawdownState(string account)
        {
            if (string.IsNullOrEmpty(account)) return null;
            lock (_ddLock) { DrawdownState d; return _dd.TryGetValue(account, out d) ? d : null; }
        }

        public static List<DrawdownState> AllDrawdownStates()
        {
            lock (_ddLock) { return new List<DrawdownState>(_dd.Values); }
        }

        /// <summary>Entry consult: is this account clear of its trailing-drawdown floor to open NEW risk?
        /// Fail-open (no published state → true). EXITS must never consult this — you must always get flat.</summary>
        public static bool DrawdownAllowsEntry(Account acct, out string reason)
        {
            reason = null;
            if (acct == null || acct.Name == null) return true;
            var d = GetDrawdownState(acct.Name);
            if (d == null || !(d.EntryBlocked || d.Breach)) return true;
            reason = d.Breach
                ? "trailing DD BREACH (cushion $" + Math.Round(d.Cushion) + ")"
                : "trailing DD thin (cushion $" + Math.Round(d.Cushion) + " of $" + Math.Round(d.DdAmount) + ")";
            return false;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  ACCOUNT PROFILES (v1.0.8) — the intuitive per-account config (firm preset OR custom).
        //  Risk parses Sentinel\Profiles.conf and publishes here; the Governor derives its cap/loss
        //  from the profile, sizing reads SizeScale/ContractLimit, session gates on the window.
        //  Keyed by account name. Consumers read GetAccountProfile(); fail-open (no profile → nulls).
        // ─────────────────────────────────────────────────────────────────────
        public sealed class AccountProfile
        {
            public string   Account, Firm, DdType, Session;   // Firm: lucid/bulenox/tpt/apex/custom; DdType: trailing/static/eod
            public double   SizeScale;         // default position-size multiplier (1.0 = full)
            public int      ContractLimit;     // max contracts (0 = no limit)
            public double   DdAmount;          // drawdown $ (Risk trailing-DD, future)
            public double   DailyLossStop;     // Governor daily loss stop $
            public double   Ratio;             // consistency ratio R
            public double   ProfitTarget;      // eval/payout target $
            public double   ManualDailyTarget; // override daily cap (0 = use R×target)
            public int      SessionStartMin, SessionEndMin;   // minutes-of-day (-1 = 24h)
            public bool     HardEnforce;       // v1.1.0: arm auto-flatten + lockout at the loss stop (default OFF)
            public DateTime UpdatedUtc;
        }

        /// <summary>Is auto-flatten enforcement armed for this account? Default false (safe).</summary>
        public static bool HardEnforceArmed(string account)
        {
            var p = GetAccountProfile(account);
            return p != null && p.HardEnforce;
        }

        private static readonly Dictionary<string, AccountProfile> _profiles =
            new Dictionary<string, AccountProfile>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _profilesLock = new object();

        /// <summary>Publisher replaces the FULL profile set (empty = none) — one atomic swap per Profiles.conf load.</summary>
        public static void SetAccountProfiles(List<AccountProfile> profiles)
        {
            lock (_profilesLock)
            {
                _profiles.Clear();
                if (profiles != null)
                    foreach (var p in profiles)
                        if (p != null && !string.IsNullOrEmpty(p.Account)) { p.UpdatedUtc = DateTime.UtcNow; _profiles[p.Account] = p; }
            }
        }

        public static AccountProfile GetAccountProfile(string account)
        {
            if (string.IsNullOrEmpty(account)) return null;
            lock (_profilesLock) { AccountProfile p; return _profiles.TryGetValue(account, out p) ? p : null; }
        }

        public static List<AccountProfile> AllAccountProfiles()
        {
            lock (_profilesLock) { return new List<AccountProfile>(_profiles.Values); }
        }

        /// <summary>Profile-aware entry size: baseQty × SizeScale × governor RecommendedSize, clamped to the
        /// profile's ContractLimit (never below 1). Fail-open — an unprofiled account returns baseQty.</summary>
        public static int SizedQuantity(Account acct, int baseQty)
        {
            if (acct == null || acct.Name == null || baseQty <= 0) return baseQty;
            var p = GetAccountProfile(acct.Name);
            double scale = (p != null && p.SizeScale > 0 ? p.SizeScale : 1.0) * RecommendedSize(acct);
            int q = (int)Math.Round(baseQty * scale);
            if (q < 1) q = 1;                                       // never round a real entry down to 0
            if (p != null && p.ContractLimit > 0 && q > p.ContractLimit) q = p.ContractLimit;
            return q;
        }

        /// <summary>Is now within the account profile's session window? true if 24h / no profile. reason set when outside.</summary>
        public static bool InAccountSession(AccountProfile p, out string reason)
        {
            reason = null;
            if (p == null || p.SessionStartMin < 0 || p.SessionEndMin < 0) return true;   // 24h / no window
            int mins;
            try { var now = NinjaTrader.Core.Globals.Now; mins = now.Hour * 60 + now.Minute; } catch { return true; }
            bool inWin = p.SessionStartMin <= p.SessionEndMin
                ? (mins >= p.SessionStartMin && mins < p.SessionEndMin)          // intraday window
                : (mins >= p.SessionStartMin || mins < p.SessionEndMin);          // overnight window
            if (!inWin) reason = "outside session " + Hhmm(p.SessionStartMin) + "-" + Hhmm(p.SessionEndMin);
            return inWin;
        }
        private static string Hhmm(int m) { return (m / 60).ToString("00") + (m % 60).ToString("00"); }

        // ═════════════════════════════════════════════════════════════════════
        //  THE ORDER GATE (v1.1.0) — the single pre-submit choke point.
        //  See Docs/SENTINEL_HARDENING_FRAMEWORK.md (Substrate 1). Every order — Deck,
        //  GTrader21, Copier — asks GateEntry() before Account.Submit. The gate CLASSIFIES
        //  (Clear / Advisory / Hard) and RISK-SIZES; it does NOT itself submit or cancel.
        //  The CALLER applies policy: manual = fail-open (surface Advisory/Hard loudly but a
        //  human may still act), automated = fail-closed (refuse on Hard). Exits never gate
        //  here — you must always be able to get flat.
        // ═════════════════════════════════════════════════════════════════════

        public enum GateLevel { Clear, Advisory, Hard }

        public sealed class GateDecision
        {
            public GateLevel Level;     // Clear=go · Advisory=surface, manual may proceed · Hard=protective stop
            public string    Reason;    // human-readable ("why blocked / why this size"); null when Clear
            public int       Size;      // risk-sized (or passed-through) quantity
            public bool IsClear    => Level == GateLevel.Clear;
            public bool IsHard     => Level == GateLevel.Hard;
        }
        private static GateDecision Dec(GateLevel lvl, string why, int size)
            => new GateDecision { Level = lvl, Reason = why, Size = size };

        // ── $-risk position sizing ──────────────────────────────────────────
        /// <summary>$ value of one tick for an instrument (PointValue × TickSize). 0 if unknown.</summary>
        public static double TickValue(NinjaTrader.Cbi.Instrument instr)
        {
            try { return instr != null && instr.MasterInstrument != null
                ? instr.MasterInstrument.PointValue * instr.MasterInstrument.TickSize : 0.0; }
            catch { return 0.0; }
        }

        /// <summary>Contracts to risk ~riskDollars given a stop of stopTicks, clamped to the account's
        /// contract limit. Returns 0 when you can't afford even a 1-lot (risk too small / stop too wide) —
        /// that 0 is a SIGNAL the caller should surface, not silently trade.</summary>
        public static int SizeForRisk(Account acct, NinjaTrader.Cbi.Instrument instr, double stopTicks, double riskDollars)
        {
            double tv = TickValue(instr);
            if (tv <= 0 || stopTicks <= 0 || riskDollars <= 0) return 0;
            double perContract = tv * stopTicks;
            if (perContract <= 0) return 0;
            int q = (int)Math.Floor(riskDollars / perContract);
            if (q < 0) q = 0;
            var p = acct != null ? GetAccountProfile(acct.Name) : null;
            if (p != null && p.ContractLimit > 0 && q > p.ContractLimit) q = p.ContractLimit;
            return q;
        }

        // ── fat-finger / runaway rate guard ─────────────────────────────────
        private static int _maxOrderQty        = 0;    // 0 = no absolute cap
        private static int _maxOrdersPerWindow = 12;   // runaway / fat-finger rate cap
        private static int _rateWindowSec      = 10;
        private static readonly Dictionary<string, List<DateTime>> _recentOrders =
            new Dictionary<string, List<DateTime>>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _rateLock = new object();

        /// <summary>Tune the fat-finger guards. maxQty 0 = no cap.</summary>
        public static void SetOrderGuards(int maxOrderQty, int maxOrdersPerWindow, int windowSeconds)
        {
            _maxOrderQty = Math.Max(0, maxOrderQty);
            _maxOrdersPerWindow = Math.Max(1, maxOrdersPerWindow);
            _rateWindowSec = Math.Max(1, windowSeconds);
        }
        public static int MaxOrderQty => _maxOrderQty;

        /// <summary>Record a submitted order for the rate guard. Call right AFTER a successful submit.</summary>
        public static void NoteOrderSubmitted(string account)
        {
            if (string.IsNullOrEmpty(account)) return;
            var now = DateTime.UtcNow;
            lock (_rateLock)
            {
                List<DateTime> l;
                if (!_recentOrders.TryGetValue(account, out l)) { l = new List<DateTime>(); _recentOrders[account] = l; }
                l.Add(now);
                l.RemoveAll(t => (now - t).TotalSeconds > _rateWindowSec);
            }
        }
        private static bool RateExceeded(string account)
        {
            if (string.IsNullOrEmpty(account)) return false;
            var now = DateTime.UtcNow;
            lock (_rateLock)
            {
                List<DateTime> l;
                if (!_recentOrders.TryGetValue(account, out l)) return false;
                l.RemoveAll(t => (now - t).TotalSeconds > _rateWindowSec);
                return l.Count >= _maxOrdersPerWindow;
            }
        }

        // ── the gate itself ─────────────────────────────────────────────────
        /// <summary>Validate + risk-size an ENTRY before submit. Pass stopTicks+riskDollars+instr to
        /// size by $-risk (else requestedQty passes through). Returns a classified decision + a reason +
        /// the quantity. Does not submit. HARD = protective (kill/loss-stop/rate/qty-cap); ADVISORY =
        /// context (feed/target-done/session/rollover/news/undersized). EXITS should NOT call this.</summary>
        public static GateDecision GateEntry(Account acct, string instrument, int requestedQty,
            double stopTicks = 0, double riskDollars = 0, NinjaTrader.Cbi.Instrument instr = null)
        {
            int size = requestedQty;
            bool riskSized = riskDollars > 0 && stopTicks > 0 && instr != null;
            if (riskSized) size = SizeForRisk(acct, instr, stopTicks, riskDollars);

            // HARD — protective stops (an automated caller must refuse; a human is warned loudly)
            if (_kill) return Dec(GateLevel.Hard, "kill-switch engaged", size);
            string ik = InstrumentKillReason(instrument);
            if (ik != null) return Dec(GateLevel.Hard, InstrumentRoot(instrument) + " halted (" + ik + ")", size);
            var g = acct != null ? GetGovernorState(acct.Name) : null;
            if (g != null && !g.Allowed && g.Status == "DayHalted")
                return Dec(GateLevel.Hard, "daily loss stop hit" + (g.Reason != null ? " (" + g.Reason + ")" : ""), size);
            if (RateExceeded(acct != null ? acct.Name : null))
                return Dec(GateLevel.Hard, "rate limit (" + _maxOrdersPerWindow + "/" + _rateWindowSec + "s) — possible runaway/fat-finger", size);
            var pr = acct != null ? GetAccountProfile(acct.Name) : null;
            if (_maxOrderQty > 0 && size > _maxOrderQty)
                return Dec(GateLevel.Hard, "qty " + size + " exceeds max order " + _maxOrderQty, size);
            if (pr != null && pr.ContractLimit > 0 && size > pr.ContractLimit)
                return Dec(GateLevel.Hard, "qty " + size + " exceeds contract limit " + pr.ContractLimit, size);

            // ADVISORY — context; a human may still choose to act
            if (acct != null && !IsAccountHealthy(acct))
                return Dec(GateLevel.Advisory, "feed unhealthy (" + (acct.Name ?? "?") + ")", size);
            if (g != null && !g.Allowed && g.Status == "DayComplete")
                return Dec(GateLevel.Advisory, "daily target complete — consider stopping", size);
            string sr;
            if (!InAccountSession(pr, out sr)) return Dec(GateLevel.Advisory, sr, size);
            if (RolloverBlocked(instrument)) return Dec(GateLevel.Advisory, "rollover imminent (" + InstrumentRoot(instrument) + ")", size);
            var nl = ActiveNewsLockoutFor(instrument);
            if (nl != null) return Dec(GateLevel.Advisory, "news lockout: " + nl.Event, size);
            if (riskSized && size < 1)
                return Dec(GateLevel.Advisory, "risk too small for a 1-lot at a " + stopTicks + "-tick stop", size);

            return Dec(GateLevel.Clear, null, size);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  FEED-HEALTH GATE — pluggable. A future "Sentinel Risk"/health service (or
        //  the promoted GTrader21 v0.1.2 lag metric) registers a probe here; until then
        //  the default is "healthy". Tools call IsAccountHealthy() before acting on a feed.
        // ─────────────────────────────────────────────────────────────────────
        /// <summary>
        /// Optional probe: given an account, return false if ITS feed is unsafe to act on
        /// (stalled/lagging/disconnected). Null => always healthy (default). Set by a
        /// health-monitoring tool. Keep it fast and exception-safe.
        /// </summary>
        public static Func<Account, bool> FeedHealthProbe;

        public static bool IsAccountHealthy(Account a)
        {
            var probe = FeedHealthProbe;
            if (probe == null) return true;
            try { return probe(a); } catch { return true; } // fail-open; a bad probe never blocks
        }

        // ─────────────────────────────────────────────────────────────────────
        //  COMBINED GATE — the single question every risky action asks.
        //  reason is set to a human-readable cause when it returns false.
        // ─────────────────────────────────────────────────────────────────────
        public static bool CanAct(Account acct, out string reason)
        {
            if (_kill) { reason = "kill-switch engaged"; return false; }
            if (acct != null && !IsAccountHealthy(acct))
            {
                reason = "feed unhealthy (" + (acct.Name ?? "?") + ")";
                return false;
            }
            reason = null;
            return true;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  SCOPED GATE (v1.0.5) — like CanAct, but ALSO honors the per-instrument kill.
        //  Use this whenever the action targets a specific instrument (a mirror or an entry),
        //  so a scoped halt on one root blocks only that instrument. Global kill still wins.
        // ─────────────────────────────────────────────────────────────────────
        public static bool CanActInstrument(string instrument, Account acct, out string reason)
        {
            if (_kill) { reason = "kill-switch engaged"; return false; }
            string kr = InstrumentKillReason(instrument);
            if (kr != null) { reason = InstrumentRoot(instrument) + " halted (" + kr + ")"; return false; }
            if (acct != null && !IsAccountHealthy(acct))
            {
                reason = "feed unhealthy (" + (acct.Name ?? "?") + ")";
                return false;
            }
            reason = null;
            return true;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  COMBINED ENTRY GATE — the single question a Sentinel-aware strategy asks at
        //  ENTRY time (a superset of CanAct): kill-switch + feed health + rollover-imminent
        //  + news lockout. EXITS should use CanAct (never blocked by rollover/news — you must
        //  always be able to get flat). instrument = full contract name (e.g. "ES 09-26") or
        //  root ("ES"); matching is by root. Fail-open for anything not published.
        // ─────────────────────────────────────────────────────────────────────
        public static bool CanEnter(string instrument, Account acct, out string reason)
        {
            if (!CanActInstrument(instrument, acct, out reason)) return false;   // global + scoped kill + feed health
            if (acct != null && !TradingAllowedToday(acct))                     // per-account daily governor
            {
                var gs = GetGovernorState(acct.Name);
                reason = "governor: " + (gs != null && gs.Reason != null ? gs.Reason : "day complete/halted");
                return false;
            }
            if (acct != null && !DrawdownAllowsEntry(acct, out reason))          // per-account TRAILING drawdown floor
                return false;
            if (acct != null)                                                   // per-account profile session window
            {
                string sessReason;
                if (!InAccountSession(GetAccountProfile(acct.Name), out sessReason))
                { reason = "account " + (acct.Name ?? "?") + ": " + sessReason; return false; }
            }
            if (RolloverBlocked(instrument)) { reason = "rollover imminent (" + InstrumentRoot(instrument) + ")"; return false; }
            var nl = ActiveNewsLockoutFor(instrument);
            if (nl != null) { reason = "news lockout: " + nl.Event; return false; }
            reason = null;
            return true;
        }
    }
}
