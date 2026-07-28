// ─────────────────────────────────────────────────────────────────────────────
// This Source Code Form is subject to the terms of the Mozilla Public License,
// v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
// one at https://mozilla.org/MPL/2.0/.
//
// Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
// ─────────────────────────────────────────────────────────────────────────────
// ═════════════════════════════════════════════════════════════════════════════
//  SentinelCore — FOUNDATION layer  (partial)
//  File: SentinelCore.Foundation.cs   |   part of `static partial class SentinelCore`
// ─────────────────────────────────────────────────────────────────────────────
//  PRODUCT-LADDER RUNTIME SPLIT — see Docs/PRODUCT_LADDER.md §4 (the runtime floor)
//  + §5 (the core-split finding). SentinelCore is being split into three co-operating
//  PARTIAL files so a distribution bundle can ship only the layers it needs:
//      • SentinelCore.Foundation.cs  (F)  — SettingsDir, Log, SeamStore<T>, ScopeOf,
//        BarTag, InstrumentRoot, Conditions, Alerts, + the context vetoes
//        (kill-switch / instrument-kill / news / rollover)
//      • SentinelCore.Bus.cs         (L1) — the …State publish/consult seam registry
//      • SentinelCore_v1_0_0.cs      (L2 + remainder, for now) — Gate/Ledger/State/governor
//
//  DEPENDENCY RULE (§4): a file may reference only its own layer or below. Nothing in
//  Foundation references L1 (Bus) or L2 (Safety). Verified: the seams reach DOWN into
//  Conditions/Log (Foundation); the Gate never reads a seam.
//
//  STATUS — the FOUNDATION partial, populated F5-verified per batch. DONE (batch 1, F5-clean):
//  SettingsDir / SettingsFile / LogFile / Log / WriteLogFile. (Ledger/State are also Foundation but
//  still sit in the main file for now — fine, same class.) Same class, same call sites -> zero churn.
// ═════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Globalization;   // v1.40.0 beacon — invariant parse/format of the generation stamp
using System.IO;
using System.Text;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.Sentinel
{
    public static partial class SentinelCore
    {
        // ─────────────────────────────────────────────────────────────────────
        //  SETTINGS DIRECTORY — one folder all tools persist config into.
        //  <UserDataDir>\Sentinel\   (created on first access).
        // ─────────────────────────────────────────────────────────────────────
        public static string SettingsDir
        {
            get
            {
                string dir;
                try { dir = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "Sentinel"); }
                catch { dir = "Sentinel"; }
                try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); } catch { }
                return dir;
            }
        }

        /// <summary>Full path for a tool's settings file, e.g. SettingsFile("Copy") -> ...\Sentinel\Copy.json</summary>
        public static string SettingsFile(string toolName)
        {
            return Path.Combine(SettingsDir, (toolName ?? "tool") + ".json");
        }

        // ─────────────────────────────────────────────────────────────────────
        //  REPLAY MODE (v1.38.0) — "this box is a BAKE NODE; the wall clock is not the data clock."
        //
        //  WHY: every Sentinel bars type gates its …State publish on
        //      (NinjaTrader.Core.Globals.Now - barTime) > RealtimePublishMinutes
        //  to stop a HISTORICAL REBUILD stamping weeks-old bricks as fresh (a seam has no as-of semantics —
        //  SetXState stamps UpdatedUtc = UtcNow regardless). That guard is correct and must not be deleted.
        //  But `Globals.Now` is WALL-CLOCK even in Playback, so replaying data from N weeks ago makes EVERY bar
        //  read ~N weeks stale ⇒ the guard returns on every tick ⇒ BrickState never publishes ⇒ **the BRK voter
        //  cannot vote in ANY replay bake**, which is why BRK is absent from every corpus we have.
        //  (Same wall-clock-in-replay family as the news-lockout leak.)
        //
        //  SCOPE OF THIS SWITCH: the guard exists to protect LIVE consumers from replayed stamps. A dedicated
        //  bake node has no live consumers — the whole box is a replay. So the fix is scoped to the BOX, not
        //  smuggled into the algorithm. Presence of  <UserDataDir>\Sentinel\replay.on  = bake node.
        //  Mirrors the proven cards.off / layout.off / theme.txt idiom.
        //
        //  ⚠ NEVER SILENT: a main box left with replay.on would stamp replayed bricks as fresh on a live chart,
        //  so the first read logs loudly. Cached with a TTL — bars types call this PER TICK; no file I/O per tick.
        //  ⚠ This is a deliberate stopgap. The principled fix is a replay-aware clock (reflect a playback-time
        //  member off NinjaTrader.Core.dll and use it instead of Globals.Now) — probe first, bind by reflection,
        //  and judge it by whether BRK actually lands in a replay corpus row, not by whether the member resolves.
        // ─────────────────────────────────────────────────────────────────────
        private static int      _replayOn = -1;          // -1 = never read
        private static DateTime _replayChecked = DateTime.MinValue;
        private const  double   ReplayPollSeconds = 5.0;

        public static bool ReplayMode
        {
            get
            {
                try
                {
                    DateTime now = DateTime.UtcNow;
                    if (_replayOn < 0 || (now - _replayChecked).TotalSeconds >= ReplayPollSeconds)
                    {
                        _replayChecked = now;
                        bool on = File.Exists(Path.Combine(SettingsDir, "replay.on"));
                        if (on && _replayOn != 1)
                            Log("Core", "REPLAY MODE ON (Sentinel\\replay.on) — bars-type freshness guards are "
                                      + "DISABLED so BrickState/FluxState publish during Playback. Do NOT leave "
                                      + "this file on a box trading live: replayed bars will stamp as fresh.");
                        else if (!on && _replayOn == 1)
                            Log("Core", "REPLAY MODE OFF (Sentinel\\replay.on removed) — freshness guards re-armed.");
                        _replayOn = on ? 1 : 0;
                    }
                }
                catch { }
                return _replayOn == 1;
            }
        }

        /// <summary>The rolling text log every Sentinel tool writes to (readable outside NT).</summary>
        public static string LogFile { get { return Path.Combine(SettingsDir, "sentinel.log"); } }

        // ─────────────────────────────────────────────────────────────────────
        //  LOG — tagged output. Tools call SentinelCore.Log("Copy", msg): the line
        //  goes to the NinjaScript Output window AND is appended (timestamped) to
        //  <UserDataDir>\Sentinel\sentinel.log so it's readable without a screenshot.
        // ─────────────────────────────────────────────────────────────────────
        private static readonly object _logLock = new object();

        /// <summary>How many rotated sentinel.log backups to keep (.1 .. .N). Was effectively 1
        /// until v1.42.0, which is why two forensic windows were lost mid-investigation.</summary>
        private const int LOG_GENERATIONS = 6;

        public static void Log(string tool, string msg)
        {
            string line = "[Sentinel:" + (tool ?? "?") + "] " + msg;
            try
            {
                NinjaTrader.Code.Output.Process(line, NinjaTrader.NinjaScript.PrintTo.OutputTab1);
            }
            catch { }
            WriteLogFile(line);
        }

        private static void WriteLogFile(string line)
        {
            try
            {
                lock (_logLock)
                {
                    string path = LogFile;
                    // Rotate at ~5 MB, keeping LOG_GENERATIONS backups.
                    //
                    // ⚠ THIS KEPT EXACTLY ONE GENERATION UNTIL v1.42.0, AND IT COST REAL EVIDENCE.
                    // At the rates this log has actually hit (41,340 lines in 27 SECONDS during a
                    // historical rebuild; 79,603 in ~14 min from a single ungated publisher), 5 MB is
                    // roughly THREE MINUTES of history at 100x replay. Deleting the only backup on
                    // every rotation destroyed the 2026-07-23→24 forensic window PERMANENTLY, twice
                    // in one night, while the BRK/FLUX seam bug was being chased — and the standing
                    // rule "copy sentinel.log aside BEFORE any bake forensics" exists only because
                    // this code made the log untrustworthy as a record. Generations are nearly free
                    // (disk is not the constraint); a lost investigation window is not recoverable.
                    try
                    {
                        var fi = new FileInfo(path);
                        if (fi.Exists && fi.Length > 5 * 1024 * 1024)
                        {
                            // Shift .N-1 -> .N, ... , .1 -> .2, then base -> .1. Oldest falls off the end.
                            for (int i = LOG_GENERATIONS - 1; i >= 1; i--)
                            {
                                string src = path + "." + i;
                                string dst = path + "." + (i + 1);
                                try
                                {
                                    if (File.Exists(src))
                                    {
                                        if (File.Exists(dst)) File.Delete(dst);
                                        File.Move(src, dst);
                                    }
                                }
                                catch { }   // see recursion note below
                            }
                            File.Move(path, path + ".1");
                        }
                    }
                    // ⛔ DELIBERATELY SILENT — do NOT "migrate" this to Swallow(). Swallow() calls
                    // Log(), which calls WriteLogFile(), which is this method: a fault here would
                    // recurse until the stack died. This is one of the documented deliberate empty
                    // catches (Foundation's Swallow/Log/WriteLogFile recursion guard).
                    catch { }

                    // timestamp with millis; DateTime.Now is fine here (real NT runtime, not a workflow).
                    string stamped = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + line + Environment.NewLine;
                    File.AppendAllText(path, stamped, Encoding.UTF8);
                }
            }
            catch { }   // logging must never throw into a tool's trading path
        }

        // ─────────────────────────────────────────────────────────────────────
        //  SWALLOW — the recorded empty catch  (v1.41.0, 2026-07-25)
        //
        //  WHY THIS EXISTS — ~350 empty `catch { }` blocks were counted across the suite (Deck 71 of
        //  107, Council 31 of 34, Risk 29, Bridge 28, GTrader21 29). The INTENT is right and must be
        //  preserved: telemetry and UI must never throw into a bar or order path. The DEFECT is that
        //  "don't propagate" was implemented as "don't record", and that is the proven mechanism of
        //  every expensive bug in this project's history — the BRK/FLUX seam hunt (the publish body
        //  sat inside a silent try/catch, so a throw in it was invisible), the 160 false NAKED
        //  POSITION criticals, the Eye that never loaded for weeks, the Deck's racy plot read.
        //  Each was undiagnosable BY CONSTRUCTION.
        //
        //  Swallow keeps the guarantee and drops the blindness. It NEVER rethrows, so a migrated
        //  `catch { }` behaves identically at runtime. It rate-limits per tag (first 3, then at most
        //  one line per minute) because an exception on a per-tick path would otherwise flood the log
        //  — which is the legitimate fear that made empty catches attractive in the first place.
        //  And it COUNTS, so "how many faults is this box swallowing right now" becomes a number the
        //  Cockpit and the health board can show instead of a question nobody can answer.
        //
        //  USAGE:  catch { }                       ->  catch (Exception _sx) { SentinelCore.Swallow("Deck.Fill", _sx); }
        //  Tag convention: "<Tool>.<site>" — stable, greppable, and it is what the counter is keyed by.
        // ─────────────────────────────────────────────────────────────────────
        private static readonly object _faultLock = new object();
        private static readonly Dictionary<string, long>     _faultCount = new Dictionary<string, long>();
        private static readonly Dictionary<string, DateTime> _faultLast  = new Dictionary<string, DateTime>();

        /// <summary>Record a swallowed exception. Never rethrows — a migrated `catch { }` keeps its
        /// exact runtime behaviour. Rate-limited per tag (first 3, then 1/min) so a per-tick throw
        /// cannot flood the log.</summary>
        public static void Swallow(string tag, Exception ex)
        {
            try
            {
                if (string.IsNullOrEmpty(tag)) tag = "?";
                long n;
                bool say;
                DateTime now = DateTime.UtcNow, last;
                lock (_faultLock)
                {
                    _faultCount.TryGetValue(tag, out n);
                    _faultCount[tag] = ++n;
                    say = n <= 3
                          || !_faultLast.TryGetValue(tag, out last)
                          || (now - last).TotalSeconds >= 60.0;
                    if (say) _faultLast[tag] = now;
                }
                if (!say) return;
                string msg = ex == null ? "(no exception)" : (ex.GetType().Name + ": " + ex.Message);
                Log("Fault", tag + " #" + n.ToString(CultureInfo.InvariantCulture) + " — " + msg);
            }
            catch { }   // the fault recorder itself must never throw; this is the ONE legitimate empty catch
        }

        /// <summary>Swallowed-fault counts by tag, newest snapshot. For the Cockpit / health board:
        /// a box quietly eating 40k exceptions an hour should be able to say so.</summary>
        public static Dictionary<string, long> Faults()
        {
            lock (_faultLock) { return new Dictionary<string, long>(_faultCount); }
        }

        /// <summary>Total swallowed faults across every tag — one number for a stat tile.</summary>
        public static long FaultTotal()
        {
            lock (_faultLock)
            {
                long t = 0;
                foreach (var kv in _faultCount) t += kv.Value;
                return t;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  ASSEMBLY-GENERATION BEACON  (v1.40.0, 2026-07-24)
        //
        //  WHY THIS EXISTS — the single most expensive bug of the corpus era.
        //  An F5 reloads the NinjaScript assembly and recreates every INDICATOR, but a chart's
        //  BARS TYPE instance is NOT recreated — it keeps executing the PREVIOUS assembly. Every
        //  seam store here is a `static Dictionary` on SentinelCore, and statics are per-assembly,
        //  so the surviving bars type publishes BrickState/FluxState/ConvictionState into the OLD
        //  assembly's dictionary while the rebuilt Council reads the NEW one. The write SUCCEEDS
        //  into a store nobody reads: guards report healthy, scope resolves, nothing throws, and
        //  BRK/FLUX/CVB simply never appear in the vote vector. It cost the 2026-07-23 audition
        //  bake (1,866 rows, zero bar-type voters) and days of misdiagnosis before the fingerprint
        //  was spotted: bars-type call counters never reset while Council instance ids did.
        //  ⇒ ONLY AN NT RESTART FIXES IT. A chart reload is NOT sufficient (measured 2026-07-24).
        //
        //  WHAT THIS DOES — it cannot repair the split (that needs the restart), but it makes the
        //  split LOUD instead of silent, at chart load rather than 10 minutes into a bake.
        //  The beacon lives in the APPDOMAIN, which outlives assembly reloads, and carries ONLY
        //  strings — no custom type crosses the boundary, so there is no type-identity problem
        //  (the same reason a `bars.BarsType as SentinelTBars_v1_0_0` cast fails across
        //  generations: type identity includes assembly identity).
        //
        //  A publisher beacons "generation G was alive for scope S at time T"; a consumer that
        //  finds a seam MISSING asks whether some OTHER generation is beaconing it. If so, the
        //  sensor is not absent — it is DECOUPLED, and the operator needs a restart, not a reload.
        // ─────────────────────────────────────────────────────────────────────
        private const  string BeaconSlot = "Sentinel.Seam.Beacon";
        private static readonly object _beaconLock = new object();
        private static readonly string _gen = Guid.NewGuid().ToString("N").Substring(0, 8);
        private static readonly Dictionary<string, DateTime> _beaconSent = new Dictionary<string, DateTime>();

        /// <summary>This assembly generation's id. Changes on every NinjaScript compile/reload.</summary>
        public static string Generation { get { return _gen; } }

        private static System.Collections.Hashtable BeaconTable()
        {
            var t = AppDomain.CurrentDomain.GetData(BeaconSlot) as System.Collections.Hashtable;
            if (t != null) return t;
            lock (_beaconLock)
            {
                t = AppDomain.CurrentDomain.GetData(BeaconSlot) as System.Collections.Hashtable;
                if (t == null)
                {
                    // Synchronized wrapper: publishers run on data threads, consumers on others.
                    t = System.Collections.Hashtable.Synchronized(new System.Collections.Hashtable());
                    AppDomain.CurrentDomain.SetData(BeaconSlot, t);
                }
                return t;
            }
        }

        /// <summary>Publisher heartbeat: "generation N is publishing <kind> for <scope>, now."
        /// Call it right where the seam is published. Throttled to one write per 5s per key, so it
        /// is safe on a per-tick path.</summary>
        public static void Beacon(string scope, string kind)
        {
            if (string.IsNullOrEmpty(scope) || string.IsNullOrEmpty(kind)) return;
            try
            {
                string key = kind + "@" + scope;
                DateTime now = DateTime.UtcNow, last;
                lock (_beaconSent)
                {
                    if (_beaconSent.TryGetValue(key, out last) && (now - last).TotalSeconds < 5.0) return;
                    _beaconSent[key] = now;
                }
                BeaconTable()[key] = _gen + "|" + now.Ticks.ToString(CultureInfo.InvariantCulture);
            }
            catch { }   // telemetry must never throw into the bar path
        }

        /// <summary>Consumer check: is a DIFFERENT assembly generation currently beaconing this
        /// seam? Returns a human-readable "gen ab12, 3s ago" when the publisher is alive but
        /// decoupled, else null. Null means genuinely absent (not loaded) — a different problem
        /// with a different fix, which is the whole point of asking.</summary>
        public static string BeaconForeign(string scope, string kind, double maxAgeSec = 120.0)
        {
            if (string.IsNullOrEmpty(scope) || string.IsNullOrEmpty(kind)) return null;
            try
            {
                object raw = BeaconTable()[kind + "@" + scope];
                string s = raw as string;
                if (string.IsNullOrEmpty(s)) return null;
                int bar = s.IndexOf('|');
                if (bar <= 0) return null;
                string gen = s.Substring(0, bar);
                if (gen == _gen) return null;                     // same generation ⇒ not a split
                long ticks;
                if (!long.TryParse(s.Substring(bar + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out ticks))
                    return null;
                double age = (DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)).TotalSeconds;
                if (age < 0 || age > maxAgeSec) return null;      // stale beacon = a dead old chart, not a live split
                return "gen " + gen + ", " + age.ToString("0") + "s ago";
            }
            catch { return null; }
        }
    }
}
