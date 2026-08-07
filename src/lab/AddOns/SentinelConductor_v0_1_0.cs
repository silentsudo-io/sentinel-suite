// ─────────────────────────────────────────────────────────────────────────────
// This Source Code Form is subject to the terms of the Mozilla Public License,
// v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
// one at https://mozilla.org/MPL/2.0/.
//
// Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
// ─────────────────────────────────────────────────────────────────────────────
// ═════════════════════════════════════════════════════════════════════════════
//  SentinelConductor — the Sentinel Suite's REPLAY TRANSPORT (NT8)
//  File: SentinelConductor_v0_1_0.cs   ·   Version v0.1.0   ·   namespace …AddOns.Sentinel
// ─────────────────────────────────────────────────────────────────────────────
//  WHAT THIS IS
//    Spec: Docs/SENTINEL_CONDUCTOR_SPEC.md. A Control Center ▸ Tools window that DRIVES NinjaTrader's own
//    Market Replay engine programmatically — connect-state · seek · speed · job queue · checkpoint/resume ·
//    telemetry — so a corpus bake is a DECLARATIVE JOB instead of a human sitting on the Connections menu.
//
//    It is a TRANSPORT, NOT A TRADER. It holds no Account reference and calls no order method. That is what
//    makes it safe to run unattended on a bake node.
//
//  ⚖ CLEAN-ROOM ORIGIN NOTE (mandatory — spec §2, same discipline as SentinelQuartermaster / WAE v2)
//    The IDEA of programmatic playback control was noticed in a third-party AddOn (ReplayWindowSkipper,
//    unlicensed → all-rights-reserved → NOT ONE LINE ADOPTED). The API map below was derived INDEPENDENTLY
//    by reflecting over NinjaTrader.Core.dll metadata on NT 8.1.7.2 (2026-07-20). A method signature is a
//    FACT about the platform, not the reference's IP.
//
//        NinjaTrader.Adapter.PlaybackAdapter   (public type; these members all STATIC)
//          Int32    PlaybackSpeed                       get/set   — 0 = pause
//          Int32    MaxSpeedValue                       static readonly FIELD (not const → runtime read only)
//          DateTime NowEst / NowLocal                   get/set   — the replay clock
//          void     Reset(DateTime targetTimeEst, Action<bool> callback)      ← the PROPER seek
//          void     GetReplayMinMaxDates(string file, out DateTime, out DateTime)
//        NinjaTrader.Cbi.Connection
//          Connection PlaybackConnection                STATIC get/set (null when not connected)
//
//  ⚠ WHY REFLECTION AND NOT A DIRECT REFERENCE (deliberate — HARD BUILD RULE #1)
//    NinjaTrader compiles every .cs under bin\Custom into ONE assembly, so ONE broken file blocks the WHOLE
//    suite. Binding these internal-ish members directly would turn any NT API change into a total compile
//    break. Reflection degrades instead to a loud red banner + a sentinel.log dump at RUNTIME, and the rest
//    of the suite keeps compiling. Fail loud, never silently idle.
//
//  ⚠ NOT FIXED HERE (spec §7): the wall-clock leak. Driving Playback well does NOT make Core.Globals.Now
//    replay-aware, so the news veto and the SentinelTBars BRK freshness gate stay as broken as they are
//    today. The Conductor makes baking CONVENIENT; the Tier-2 harness is what makes it FAITHFUL.
//
//  CHANGELOG  (file/class name frozen at _v0_1_0 per the naming law; internal version = this header + `Ver`)
//    v0.2.0f (2026-08-02) — TRANSPORT STATE IS AN INPUT, AND NOTHING WAS VERIFYING IT.
//            Gate 3 ran on two boxes proven byte-identical in all FOUR verified inputs — code (muster), replay
//            .nrd, historical bars, chart+strategy blob — and still diverged. legacy-node seeked to target and ran;
//            worker-1's Reset no-opped and the job aborted. Same code, same conf, same NT build.
//            THE MEASURED DIFFERENCE, from the two boxes' own logs:
//                legacy-node    SEEK job 1 start | 2026-04-20 00:00:00 -> 2026-04-21 17:00:00   (7 s, ON TARGET)
//                worker-1  clk 2026-04-21 02:53:52 → landed 02:54:00, target 17:00:00      (57 ms, NO-OP)
//            legacy-node's clock was PARKED at the loaded range start. worker-1's was 27 h in and MOVING — its
//            run-log records job-start at 04-20 00:00:00 and the seek 0.6 s later at 04-21 02:54, i.e. the
//            transport was already playing at the int.MaxValue speed named in its own SPEED CLAMP line and
//            ate a replay-day before the clamp bit. ⇒ Reset appears to retarget a PARKED transport and to
//            no-op on a RUNNING one — the reverse of what we had assumed.
//            ⭐ THE STRUCTURAL LESSON, which is bigger than the bug: every input we had made verifiable is a
//              FILE we can hash. Transport state lives only inside a running NT — no file, no hash, no
//              read-back — so it was the one input still set by hand, per box, at different moments. We were
//              verifying what was easy to verify and the divergence arrived from the rest. The Watch is six
//              workers; any input that needs a GUI click per box is GUARANTEED to diverge across a matrix.
//            ⇒ PRE-FLIGHT, at the click, alongside the v0.2.0c strategy check (`transportPreflight = false`
//              to disable):
//              ① CLAMP FIRST, ASK SECOND. Speed is forced to the job's speed BEFORE the queue advances. The
//                old order let NT sit at int.MaxValue until the first clamp tick — long enough on worker-1
//                to burn 27 replay-hours and put the clock where the seek could not recover it.
//              ② REFUSE A MOVING TRANSPORT. The clock is sampled twice `transportSettleMs` apart; if it
//                advanced, the run is refused with the reason, because Reset cannot be trusted to retarget it.
//              ③ MEASURE POSITION, DO NOT REFUSE ON IT. The offset from the loaded range start is logged at
//                every queue start — but only as evidence. With n=1 each way we cannot yet separate "was
//                moving" from "was not at range start", and a guard that blocks on an unproven discriminator
//                would break every legitimate resume. Record it until the data says which one matters.
//              ⭐ ② is a refusal and ③ is a measurement ON PURPOSE. Blocking on what we have proven and
//                instrumenting what we have not is the difference between a gate and a guess.
//
//    v0.2.0b (2026-08-02) — THREE DEFECTS IN v0.2.0 ITSELF, all found by DRIVING it on worker-1, none by
//            review. Recorded because each one is the guard failing in the exact way it was written to prevent:
//            ① THE RESUME CHECK FAILED OPEN. It read `if (mfp != null && mfp != fp) continue;` — so a
//              pre-v0.2.0 row, which carries no `manifest` field, matched EVERY manifest. It even logged
//              "manifest unverified" and proceeded. First live test: worker-1, a box that has NEVER baked
//              anything, took the RESUME path. ⇒ a row without a fingerprint now proves nothing. **A check
//              that cannot verify must not pass** — the whole point of this version, broken in its own code.
//            ② THE CHECKPOINT LEDGER TRAVELLED WITH THE TREE. `Conductor\run-log.jsonl` came to the sentries
//              in the carve, so a cold box inherited legacy-node's 45h-old checkpoints. A checkpoint asserts "THIS
//              machine baked these sessions"; copying it makes that a lie on arrival. Now excluded by
//              muster.py alongside Run.conf and Excursions.
//            ③ THE PRODUCTIVITY GATE HAD A HOLE, and it was the worse bug: it only evaluates INSIDE a job
//              that runs `productivityGraceMin` of replay clock. On the live test all five jobs ended in
//              seconds (no loaded data for their windows) so it never evaluated once, and the queue reported
//              "QUEUE COMPLETE · 5/5 JOBS · 0 SESSIONS · 0 STALL(S)" — a success-shaped nothing. ⇒ added the
//              QUEUE-LEVEL assertion: 0 sessions AND no corpus advance is a FAILED queue however it ended,
//              said in the same breath as "complete".
//            ⇒ The meta-lesson, again: every one of these was invisible to reading and obvious to running.
//
//    v0.2.0 (2026-08-02) — THE FALSE BAKE, and the three guards it earned. Found live on legacy-node: NT restarted
//            for an unrelated reason, `autostart = true` had sat in Run.conf since 07-30, and the Conductor
//            fired a cell nobody asked for — 144 minutes at 100×, 8 sessions "checkpointed", NO strategy
//            loaded, ZERO corpus rows, and not one complaint. Three independent defects, three fixes:
//
//            ① ARMING — autostart is now an INTENT, not a standing permission. A persistent boolean cannot
//              tell "I armed this and rebooted to start it" from "this has been true for three days". So the
//              two cases that both look like autostart are now separated: a RESUME (a checkpoint for THIS
//              manifest, newer than `resumeGraceHours`) proceeds automatically — that is the crash-recovery
//              case autostart exists for and it must never need permission; a COLD START requires
//              `Conductor\armed.token`, which carries an `armedUtc`, a TTL, and the manifest fingerprint it
//              authorises, and is CONSUMED on use. ⚠ Clicking RUN is intent by definition and is never gated.
//              ⚠ The manifest fingerprint covers the JOB LINES only — editing `heartbeatSec` must not
//              invalidate an arm; editing what actually runs must.
//            ② PRODUCTIVITY GATE — after `productivityGraceMin` of REPLAY-clock advance with no corpus
//              written, abort. Deliberately an OUTPUT assertion, not a pre-flight inspection of charts and
//              strategies: enumerating them is fragile and NT-version-bound and would only prove the objects
//              exist, whereas measuring the corpus catches every way this fails at once (no strategy,
//              strategy off, no recorder, wrong chart, wrong bar type, wrong instrument).
//              ⚠ Measured on the LANDED clock after the seek — anchoring before it would satisfy the gate
//              instantly, since a seek jumps weeks. ⚠ Never walks the corpus tree (~99k files); a directory's
//              own mtime is O(1) and sufficient.
//            ③ JOB WINDOW GUARD — the clock must stay INSIDE the window the job claims. Completion only ever
//              tested `clk.Date > To.Date`, which a clock BEFORE the window never trips: job 3/5 announced
//              2026-05-17→05-29 and ran at 2026-04-26…04-29 for two hours, stamping session rows labelled
//              with the 05-17 window. A run that MISLABELS is worse than one that fails, because the failure
//              is invisible downstream. Checked continuously, so a seek that silently lands elsewhere is
//              caught by the same net.
//
//            THE THROUGH-LINE: the manifest already carried the right rule — "FLIP TO true ONLY once the
//            chart is confirmed loaded and the recorder is on it" — but it was written in ENGLISH TO A HUMAN
//            instead of in code to the machine. Three of these are the same bug: a condition only a person
//            was checking. ⇒ run-log rows now also carry `manifest` and `arm` so a run's authority is
//            recoverable after the fact rather than inferred.
//    v0.1.0l (2026-07-21) — END-OF-DATA IS NOT A HANG (liveness bug, found by driving job 2 to the end of its
//            replay range). Completion tested `clk.Date > To.Date`, which is UNREACHABLE when `to` is the last day
//            of loaded data — the clock parks at 23:59:59 of that day and the job (and the queue behind it) hangs
//            forever. Since "bake everything I have" is the obvious manifest to write, this was waiting for anyone.
//            A stall that occurs while already on/past the job's final day now finishes the job (`done-endofdata`);
//            a stall BEFORE the final day is still just reported. ⚠ Consequence accepted: a real hang on the final
//            day ends the job early instead of never — the session is checkpointed, so a re-run re-bakes it.
//    v0.1.0m (2026-07-21) — INTERLOCK LEG ② REWRITTEN: it never worked. `Application.Current.Windows` does not
//            contain NT tool windows (they live on their own dispatchers), so an IDLE Conductor — which emits no
//            heartbeat for leg ① to find — was invisible, and every recompile opened another window (three stacked
//            on the dev box before it was noticed). Now enumerates `Globals.AllWindows`, NT's cross-dispatcher
//            registry. Lesson: leg ① was verified live, leg ② never was, and the unverified half is the half that
//            failed. A fallback nobody has SEEN fire is a guess, not a fallback.
//    v0.1.0k (2026-07-21) — AUTO-OPEN INTERLOCK. A recompile does NOT close an open Conductor window (the WPF
//            window survives the assembly reload), so v0.1.0i auto-opened a SECOND one on top of a running
//            transport: two Conductors driving one Playback, interleaved heartbeats, one seeking backwards
//            mid-job. Auto-open now probes for a live Conductor first — primarily via the heartbeat already in
//            sentinel.log, the one signal that crosses the assembly-reload boundary (statics and Type identity
//            both reset), with a window-name match as backstop. Caught by reading the heartbeats, not by review.
//    v0.1.0j (2026-07-21) — RESUME NO LONGER SKIPS THE INTERRUPTED SESSION. `ResumePoint` returned
//            `lastCheckpoint + 1 day`, but a checkpoint is stamped when replay CROSSES INTO a session (bug ⑦),
//            so the session that was in flight was never re-baked — a crash at 09:00 on a weekday silently
//            dropped that entire RTH session and the corpus still looked complete. Now resumes AT the boundary
//            and re-bakes it. The failure modes are NOT symmetric: a duplicate is dedupable, a missing session is
//            invisible forever. Found by watching a live reload resume. ⚠ CORRECTED 2026-07-21 by MEASURING the
//            re-baked rows: the dedupe key is **(instrument, bartype, fireTime)** — which is exactly what the Lab
//            ingester already uses (`trade_id = row:{inst}:{bartype}:{ft}`) — NOT `episodeId`. episodeId is a
//            PER-RUN sequence counter: across a restart the same event gets a different id AND an id gets reused
//            for a different event. Measured: 64 re-baked events, 59 byte-identical outcomes, **0** sharing an
//            episodeId. See [[episode-id-not-a-cross-run-key]].
//    v0.1.0i (2026-07-21) — AUTO-OPEN, the missing leg of lights-out. v0.1.0h could auto-RUN but nothing ever
//            auto-OPENED the window, so after a reboot the whole chain died silently at "no Conductor window".
//            Now: `autostart=true` in Run.conf ALSO opens the window itself, AutoOpenDelaySec after the Control
//            Center appears. ⚠ Deliberately NOT NT workspace persistence (IWorkspacePersistence): a bake node
//            that is HARD-KILLED — the exact case self-healing exists for — never saves its workspace, so a
//            workspace-restored window would vanish precisely when it is needed. A file on disk cannot.
//            One switch, not two: declaring lights-out implies the window. Logs AUTO-OPEN (headlessly verifiable).
//    v0.1.0h (2026-07-20) — AUTORUN (lights-out): Run.conf `autostart=true` auto-runs the queue once Playback
//            connects+settles. Reflection REJECTED programmatic connect-with-range (ConnectOptions has no clean
//            date-range field; Start/End = obfuscated internals) → use NT-native ConnectOnStartup instead. With
//            checkpoint/resume = SELF-HEALING baking. ✅ Proven main box AND legacy-node (first unattended corpus bake).
//    v0.1.0g — SPEED INVARIANT made BIDIRECTIONAL: during a running job, hold PlaybackSpeed AT the target both
//            ways (clamp down over the ceiling AND push up when NT parks replay at 0/1× post-seek). Ended the
//            recurring "job sits frozen until MAX is clicked". Manual buttons update the intended speed.
//    v0.1.0f — SEEK stamped FALSE checkpoints (a Reset JUMP across a boundary counted as a baked session →
//            misdirected resume → chart gap). Count boundaries only on real replay advance (!_seeking).
//    v0.1.0e — SEEK WATCHDOG: a seek into a NO-DATA time never fires Reset's callback → _seeking hung forever
//            (silent wedge, stall gated on !_seeking). Watchdog self-evaluates; off-target during a job aborts it.
//            Also: resume decisions now log to sentinel.log.
//    v0.1.0c/d — Reset leaves PlaybackSpeed MAXED (int.MaxValue) on a DELAY. One-shot restore + a timed re-assert
//            both lost the race → replaced by the standing invariant (see g).
//    v0.1.0b — Reset's Action<bool> callback is NOT success (returned false on a seek that landed EXACTLY on
//            target). Judge the seek by the MEASURED landed clock vs target (SeekTolHours), never the flag.
//    v0.1.0a — MaxSpeedValue reads int.MaxValue (NT declares NO cap) — a resolved-but-implausible value that
//            silently disabled the speed clamp. Band-check 1..5000; else conservative 100× + `maxSpeed` conf
//            override. Self-test PASS now LOGS (was green-banner-only → unverifiable headlessly).
//    v0.1.0 (2026-07-20) — first cut. Self-test; live transport readout; manual speed + seek (pause → Reset →
//            restore); Run.conf job manifest; sequential job queue; per-session checkpoint + resume; heartbeat;
//            run-log.jsonl provenance; stall DETECTION. Seek restricted to session boundaries (spec §3.1).
//    ⭐ META-LESSON (recurred 3×, worth internalizing): a value that RESOLVES is not a value you can TRUST —
//       measure the outcome (landed clock, actual speed, corpus rows), don't believe the flag. Every bug above
//       was caught by DRIVING it on live data, none by reasoning.
//    ⚠ DEFERRED (v0.2.0): a job stamps its STARTING session boundary (ResolveSeek lands a hair before target,
//       replay crosses it → counted). Not a false stamp (replay DID cross) but a session-semantics design Q.
// ═════════════════════════════════════════════════════════════════════════════

#region Using declarations
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NinjaTrader.Cbi;
using NinjaTrader.Core;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.Sentinel
{
    // ── AddOn: adds "Sentinel Conductor" under Control Center ▸ Tools (fallback ▸ New) ──
    public class SentinelConductorAddOn : NinjaTrader.NinjaScript.AddOnBase
    {
        private NTMenuItem _menuItem, _hostMenu;

        // ── AUTO-OPEN (the last leg of lights-out baking) ─────────────────────────────────────────────
        // A reboot chain is only as strong as its weakest link, and v0.1.0h's was invisible: NT auto-connected
        // Playback and the queue was armed, but NOTHING re-opened the Conductor window — so a rebooted node came
        // back looking healthy and baked nothing. If Run.conf declares `autostart = true`, the operator has
        // declared lights-out; the window opening itself is implied, so this reads the SAME switch (one knob).
        // ⚠ Why not NT's IWorkspacePersistence: workspace state is written on a CLEAN exit. The scenario this
        // exists for is the unclean one (crash / force-kill / power loss), which saves nothing — the window
        // would disappear exactly when it is needed. Run.conf is on disk before the crash and after it.
        private const int AutoOpenDelaySec = 30;   // let NT finish loading the workspace before we open a window
        private static bool  _autoOpened;          // one-shot per process, not per ControlCenter
        private DispatcherTimer _openTimer;

        private void MaybeAutoOpen()
        {
            if (_autoOpened || !ConfDeclaresAutostart()) return;
            string blocker = ExistingConductor();
            if (blocker != null)
            {
                _autoOpened = true;   // don't re-evaluate every ControlCenter event
                SentinelCore.Log("Conductor", "AUTO-OPEN skipped — a Conductor is already live (" + blocker + ")");
                return;
            }
            _autoOpened = true;
            _openTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(AutoOpenDelaySec) };
            _openTimer.Tick += (s, e) =>
            {
                if (_openTimer != null) { _openTimer.Stop(); _openTimer = null; }
                try
                {
                    SentinelCore.Log("Conductor", "AUTO-OPEN — Run.conf declares autostart; opening the Conductor window");
                    Globals.RandomDispatcher.InvokeAsync(new Action(() => { var w = new SentinelConductorWindow(); w.Show(); w.Activate(); }));
                }
                catch (Exception ex) { SentinelCore.Log("Conductor", "AUTO-OPEN failed: " + ex.Message); }
            };
            _openTimer.Start();
        }

        // ── THE INTERLOCK (learned the hard way, live, 2026-07-21) ───────────────────────────────────
        // A NinjaScript recompile does NOT close an already-open Conductor window — the WPF window survives
        // the assembly reload. v0.1.0i's auto-open therefore fired on top of a RUNNING Conductor and produced
        // TWO transports driving one Playback: interleaved heartbeats, one seeking backwards while the other
        // was mid-job, both stamping checkpoints. A second transport is worse than none.
        // ⚠ Statics cannot detect this: the reload resets the new assembly's statics while the old window keeps
        // running on the OLD assembly's. Neither can Type identity (same name, different Type object). So the
        // primary probe is the one artifact that crosses assembly boundaries — the heartbeat the running
        // Conductor is already writing to sentinel.log. No new file, no new write path.
        // Returns a human reason if a Conductor is already live, else null.
        private static string ExistingConductor()
        {
            // ① a RUNNING transport — the dangerous case — is emitting a heartbeat into sentinel.log.
            //    ⚠ Freshness ALONE is wrong: after a reboot the last heartbeat is only minutes old but its
            //    process is DEAD, and blocking on it would break the very case auto-open exists for. So a
            //    heartbeat only counts if it was written by THIS NinjaTrader process — i.e. after its start.
            DateTime procStart;
            try { procStart = System.Diagnostics.Process.GetCurrentProcess().StartTime; }
            catch { procStart = DateTime.MinValue; }
            try
            {
                var fi = new FileInfo(SentinelCore.LogFile);
                if (fi.Exists)
                {
                    string tail;
                    using (var fs = new FileStream(fi.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        long take = Math.Min(fs.Length, 64 * 1024);
                        fs.Seek(-take, SeekOrigin.End);
                        using (var sr = new StreamReader(fs)) tail = sr.ReadToEnd();
                    }
                    foreach (string line in tail.Split('\n'))
                    {
                        if (line.IndexOf("[Sentinel:" + LogTagStatic + "] job ", StringComparison.Ordinal) < 0) continue;
                        DateTime t;
                        if (line.Length < 23) continue;
                        if (!DateTime.TryParse(line.Substring(0, 23), CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) continue;
                        if (t <= procStart) continue;                      // a dead process's heartbeat — ignore
                        double age = (DateTime.Now - t).TotalSeconds;
                        if (age >= 0 && age < HeartbeatFreshSec) return "heartbeat " + age.ToString("0") + "s old";
                    }
                }
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelConductor.ExistingConductor", _sx); }

            // ② an IDLE window emits no heartbeat, so leg ① cannot see it. v0.1.0k used
            //    Application.Current.Windows here and it MISSED EVERY TIME (live 2026-07-21: three Conductors
            //    stacked on the dev box, one per recompile) — NT hosts tool windows on their own dispatchers,
            //    so they are absent from Application.Current.Windows. `Globals.AllWindows` is NT's own
            //    cross-dispatcher registry (the same one NT8BridgeServer uses to find the Strategy Analyzer).
            //    Match on the TYPE NAME as a string: an assembly reload gives the same name a different Type
            //    identity, so a typeof() comparison would silently fail exactly when it matters.
            try
            {
                var all = Globals.AllWindows;
                if (all != null)
                {
                    var snap = new List<Window>();
                    try { for (int i = 0; i < all.Count; i++) snap.Add(all[i]); } catch (Exception _sx) { SentinelCore.Swallow("SentinelConductor.ExistingConductor", _sx); }
                    foreach (Window w in snap)
                        if (w != null && w.GetType().Name == "SentinelConductorWindow") return "window already open";
                }
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelConductor.ExistingConductor", _sx); }

            // ③ last resort — same match against the WPF app windows (cheap; catches a docked/odd host).
            try
            {
                if (Application.Current != null)
                    foreach (Window w in Application.Current.Windows)
                        if (w != null && w.GetType().Name == "SentinelConductorWindow") return "window already open";
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelConductor.ExistingConductor", _sx); }

            return null;
        }
        private const string LogTagStatic     = "Conductor";
        private const int    HeartbeatFreshSec = 200;   // > 3× the default 60s heartbeat, so one slow tick can't unlock it

        // Read-only peek at the ONE key we care about — the window owns the real parse.
        private static bool ConfDeclaresAutostart()
        {
            try
            {
                string p = Path.Combine(SentinelCore.SettingsDir, "Conductor", "Run.conf");
                if (!File.Exists(p)) return false;
                foreach (string raw in File.ReadAllLines(p))
                {
                    string line = raw;
                    int hash = line.IndexOf('#'); if (hash >= 0) line = line.Substring(0, hash);
                    int eq = line.IndexOf('=');   if (eq <= 0) continue;
                    if (!line.Substring(0, eq).Trim().Equals("autostart", StringComparison.OrdinalIgnoreCase)) continue;
                    string v = line.Substring(eq + 1).Trim().ToLowerInvariant();
                    return v == "true" || v == "1" || v == "yes";
                }
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelConductor.ConfDeclaresAutostart", _sx); }
            return false;
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name        = "SentinelConductor";
                Description = "Sentinel Conductor — Market Replay transport: seek / speed / job queue (Control Center ▸ Tools).";
            }
        }

        protected override void OnWindowCreated(Window window)
        {
            ControlCenter cc = window as ControlCenter;
            if (cc == null) return;
            MaybeAutoOpen();          // before the menu wiring — lights-out must not depend on the menu resolving
            if (_menuItem != null) return;
            _hostMenu = cc.FindFirst("ControlCenterMenuItemTools") as NTMenuItem
                     ?? cc.FindFirst("ControlCenterMenuItemNew")   as NTMenuItem;
            if (_hostMenu == null) return;
            _menuItem = new NTMenuItem { Header = "Sentinel Conductor", Style = Application.Current.TryFindResource("MainMenuItem") as Style };
            _menuItem.Click += OnMenuClick;
            _hostMenu.Items.Add(_menuItem);
        }

        protected override void OnWindowDestroyed(Window window)
        {
            if (_menuItem != null && window is ControlCenter)
            {
                if (_hostMenu != null && _hostMenu.Items.Contains(_menuItem)) _hostMenu.Items.Remove(_menuItem);
                _menuItem.Click -= OnMenuClick; _menuItem = null; _hostMenu = null;
            }
        }

        private void OnMenuClick(object sender, RoutedEventArgs e)
        {
            Globals.RandomDispatcher.InvokeAsync(new Action(() => { var w = new SentinelConductorWindow(); w.Show(); w.Activate(); }));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    public class SentinelConductorWindow : NTWindow
    {
        private const string Ver          = "v0.2.0f";
        private const string LogTag       = "Conductor";
        private const int    SafeMaxSpeed = 100;   // conservative fallback if MaxSpeedValue won't resolve — NEVER guess high
        // ⚠ LIVE FINDING (2026-07-20): NT reports MaxSpeedValue = 2147483647 (int.MaxValue) — i.e. "no cap DECLARED",
        // not "2.1 billion× is safe". A resolved-but-implausible value is MORE dangerous than an unresolved one,
        // because it silently disables the clamp. So we band-check the reading and fall back when it's not a real
        // operational ceiling. Raise it deliberately with `maxSpeed = N` in Run.conf once a speed is proven (Test C).
        private const int    PlausibleMaxSpeed = 5000;
        private const double SeekTolHours = 26.0;   // a seek "landed" if the clock is within this of target — replay
                                                    // data can begin a little after the exact 17:00 boundary (§Seek)
        private const int    SeekWatchdogSec = 25;   // if Reset's callback hasn't landed in this long, self-evaluate
                                                     // (a seek into a no-data gap never calls back — don't wedge)
        private static readonly BindingFlags BF = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        // theme (Sentinel glass — matches Quartermaster/Cockpit)
        private static Brush Bg=FB(Color.FromRgb(0x0A,0x0E,0x17)), Card=FB(Color.FromRgb(0x11,0x17,0x26)),
            Edge=FB(Color.FromRgb(0x1E,0x2A,0x3D)), Text=FB(Color.FromRgb(0xE9,0xEE,0xF7)), Ink2=FB(Color.FromRgb(0xAE,0xBA,0xCE)),
            Muted=FB(Color.FromRgb(0x6C,0x7A,0x92)), Accent=FB(Color.FromRgb(0x3F,0xD1,0xE0)), Green=FB(Color.FromRgb(0x25,0xD0,0x8B)),
            Red=FB(Color.FromRgb(0xFF,0x5C,0x6A)), Amber=FB(Color.FromRgb(0xF2,0xB3,0x4C));
        private static SolidColorBrush FB(Color c){ var b=new SolidColorBrush(c); b.Freeze(); return b; }
        private static void ApplyTheme()
        {
            try { SentinelSkin.MaybeRefreshTheme();
                Bg=FB(SentinelSkin.KVoid); Card=FB(SentinelSkin.KPanel); Edge=FB(SentinelSkin.KLine); Text=FB(SentinelSkin.KInk);
                Ink2=FB(SentinelSkin.KInk2); Muted=FB(SentinelSkin.KMute); Accent=FB(SentinelSkin.KAccent);
                Green=FB(SentinelSkin.KUp); Red=FB(SentinelSkin.KDown); Amber=FB(SentinelSkin.KWarn); } catch (Exception _sx) { SentinelCore.Swallow("SentinelConductor.ApplyTheme", _sx); }
        }

        // ── reflection conduit (resolved once by SelfTest; see the origin note) ──
        private Type         _pbType;
        private PropertyInfo _piSpeed, _piNowEst, _piNowLocal, _piPlaybackConn;
        private FieldInfo    _fiMaxSpeed;
        private MethodInfo   _miReset, _miMinMax;
        private bool         _ready;
        private int          _maxSpeed = SafeMaxSpeed;
        private bool         _maxSpeedReal;      // did MaxSpeedValue give a PLAUSIBLE ceiling, or are we on the fallback?
        private int          _maxSpeedRaw;       // what NT actually reported (kept for honest display — e.g. 2147483647)
        private int          _maxSpeedOverride;  // Run.conf `maxSpeed = N` — the operator raising the ceiling deliberately

        // ── job model ──
        private sealed class Job
        {
            public string Instrument, Lane, Note;
            public DateTime From, To;
            public int Speed;
            public int LineNo;
            public override string ToString(){ return Instrument+" "+From.ToString("yyyy-MM-dd")+"→"+To.ToString("yyyy-MM-dd")+" @"+Speed+"x"; }
        }
        private readonly List<Job> _jobs = new List<Job>();
        private int  _ji = -1;
        private bool _running;
        private Job  _cur;

        // ── run state ──
        private DateTime _lastClock = DateTime.MinValue;   // replay clock (EST) at last tick
        private DateTime _lastClockMoveUtc = DateTime.MinValue;

        // ── EFFECTIVE SPEED (v0.1.1, 2026-07-25) — what NT is ACTUALLY delivering ──────────────────
        //  WHY THIS EXISTS: the panel used to show only the speed we ASKED NT for. On the first
        //  continuous-contract bake it read a confident "100x" while the replay clock was advancing
        //  15 SECONDS PER WALL MINUTE — an effective 0.25x, ~280x short of target. Nothing surfaced it:
        //  the clock WAS moving so the stall detector stayed quiet (it only catches a FROZEN clock),
        //  NT was responsive, and no error was logged. It took log forensics to see, and a bake that
        //  slow would have run for months while looking healthy on screen.
        //  ⭐ This is the project's recurring lesson in UI form: REPORT THE OUTCOME, NOT THE INTENT.
        //  `spd` is a request; `eff` is a measurement. Sampled over a rolling window (a per-tick ratio
        //  is far too noisy) and re-anchored across seeks, which jump the clock and would otherwise
        //  register as an absurd speed.
        private DateTime _effAnchorUtc = DateTime.MinValue;   // wall time at the anchor sample
        private DateTime _effAnchorClk = DateTime.MinValue;   // replay clock at the anchor sample
        private double   _effSpeed     = -1.0;                // replay-seconds per wall-second; <0 = not measured yet
        private const double EffWindowSec = 20.0;             // smoothing window
        private DateTime _jobStartedUtc;
        private DateTime _lastSessionStamp = DateTime.MinValue;  // last session-start we checkpointed
        private int      _sessionsDone, _jobsDone, _stalls;
        private bool     _stalled;
        private bool     _seeking;
        // ⚠ LIVE FINDING (2026-07-20): if a seek TARGETS A TIME WITH NO LOADED DATA (e.g. resume lands outside the
        // Playback range), NT never fires Reset's callback → _seeking stays true forever → the queue wedges
        // silently (no SEEK line, and stall detection is gated on !_seeking). So a seek carries a WATCHDOG: if the
        // callback hasn't landed within SeekWatchdogSec, we evaluate the landing ourselves and move on.
        private DateTime _seekStartedUtc = DateTime.MinValue;
        private DateTime _seekBefore, _seekTarget;
        private string   _seekWhy;
        private bool     _seekResolved;   // guards against callback + watchdog both firing
        // ⚠ LIVE FINDING (2026-07-20): NT's Reset() slams PlaybackSpeed to MaxSpeedValue (int.MaxValue) and
        // leaves it there — and it does so on a DELAY, after a short post-seek window has closed. A fixed-length
        // re-assert therefore misses it. The fix is a STANDING INVARIANT: `_intendedSpeed` is the speed the
        // operator/queue actually wants (updated by SetSpeed + Seek), and the tick loop clamps PlaybackSpeed
        // back to it ANY time NT pushes it above our ceiling — for the whole session, not a few ticks.
        private int      _intendedSpeed;      // the clamped speed we WANT NT to hold (0 = paused)
        private bool     _speedClampWarned;   // one log line per max-injection episode, not per tick
        private bool     _speedHoldWarned;    // one log line per "NT drifted speed down during a run" episode

        // ── settings (Run.conf `key = value` lines) ──
        private int    _sessionStartHourEst = 17;   // CME session open 17:00 ET — the natural session boundary
        private int    _stallSec            = 120;

        // ── AUTO-RECOVERY (v0.2.0) ─────────────────────────────────────────────────────
        // A stall used to be DETECTED and then left. On 2026-07-25 that cost ~4 wall-hours: the
        // detector fired correctly at 20:20 (`clock frozen 721s`) and the box then sat wedged until
        // a human happened to look. The failure itself is not preventable from in here — it was a
        // WPF render-thread death (UCEERR_RENDERTHREADFAILURE) 68 minutes later — so the goal is
        // NOT reliability, it is making a failure cost minutes instead of a night.
        //
        // ESCALATION LADDER, cheapest first. Never jump to the destructive rung: most stalls are a
        // wedged seek or NT re-maxing the speed, and a re-seek fixes those without losing the process.
        //   rung 1  RE-SEEK to the current session boundary + re-assert speed   (non-destructive)
        //   rung 2  hand off to the EXTERNAL restart task, after checkpointing  (destructive)
        //   rung 3  stop and raise a CRITICAL alert                             (give up loudly)
        //
        // ⚠ OPT-IN (default OFF), like every other risky feature here: an unattended process that
        // restarts its own host is exactly the kind of thing that must be switched on deliberately.
        // ⚠ CAPPED. A recovery loop that never gives up is worse than no recovery at all — it burns
        // the box and reports success-shaped noise forever.
        private bool     _autoRecover        = false;
        private bool     _recoverReseekFirst = true;
        private int      _maxRecoveries      = 3;      // per queue run, then rung 3
        private int      _recoverCooldownSec = 300;    // ignore a new stall this soon after a recovery
        private string   _restartTaskName    = "Sentinel-RestartNT";
        private int      _recoveries;
        private bool     _reseekTried;                 // rung 1 used for THIS stall episode
        private DateTime _lastRecoveryUtc = DateTime.MinValue;
        private int    _heartbeatSec        = 60;
        private DateTime _lastHeartbeatUtc  = DateTime.MinValue;

        // ── AUTORUN (lights-out baking) — with NT's native ConnectOnStartup on the Playback connection + the
        //    checkpoint/resume already built, this makes a bake SELF-HEALING: node reboots → NT auto-connects →
        //    Conductor auto-starts the queue → resume skips already-baked sessions. A crash costs nothing. ──
        private bool     _autostart;                 // Run.conf `autostart = true` (default OFF — opt-in safety)
        private int      _autostartDelaySec = 20;    // wait after open for the connection/data to settle
        private bool     _autostarted;               // one-shot per window
        private bool     _autostartWaitLogged;
        private DateTime _openedUtc = DateTime.MinValue;

        // ── v0.2.0 · ARMING — the flag said "run on next login" but MEANT "run on EVERY login, forever" ────
        //    Found live 2026-08-02: legacy-node's NT restarted for an unrelated reason, `autostart = true` had been
        //    sitting in Run.conf since 07-30, and the Conductor fired a cell nobody asked for. It replayed at
        //    100× for 144 minutes with no strategy loaded and wrote ZERO corpus rows. Nothing complained.
        //    ⚠ The defect is NOT that autostart exists — autostart-on-login is the whole point of a lights-out
        //    worker. The defect is that a PERSISTENT boolean cannot distinguish "I armed this deliberately and
        //    rebooted to start it" from "this has been true for three days and NT happened to restart". That is
        //    [[conditions-vs-latches]] inverted: a latch that never DISARMS is indistinguishable from intent.
        //    ⇒ Split the two cases that both look like "autostart" and treat them differently:
        //        RESUME     — a bake was already in flight (fresh checkpoint for THIS manifest). Legitimate,
        //                     automatic, needs no permission: this is the crash-recovery case autostart exists for.
        //        COLD START — no fresh checkpoint. Needs a deliberate, EXPIRING, SINGLE-USE arming token.
        //    A human clicking RUN is intent by definition and is never gated — the token guards the UNATTENDED
        //    path only, so the tool stays usable interactively.
        private bool     _requireArm          = true;  // conf `requireArm = false` restores v0.1.x behaviour
        private int      _armTtlHours         = 12;    // a token older than this is expired, not authorisation
        private int      _resumeGraceHours    = 48;    // a checkpoint older than this is history, not a bake in flight
        private string   _armVerdict;                  // why the last autostart decision went the way it did

        // ── v0.2.0 · PRODUCTIVITY GATE — "is anything actually being produced?" ──────────────────────────
        //    Deliberately an OUTPUT assertion rather than a pre-flight chart/strategy inspection. Enumerating
        //    chart windows and their strategies is fragile and NT-version-bound, and it would only prove the
        //    objects EXIST. Measuring the corpus proves the thing we actually care about, and it catches every
        //    failure mode at once: no strategy, strategy disabled, no recorder, wrong chart, wrong bar type,
        //    wrong instrument. 144 minutes of silence must be impossible, not merely unlikely.
        //    ⚠ Cost discipline: NEVER enumerate the corpus tree — it holds ~99k files and a recursive walk is
        //    minutes (learned on legacy-node). A directory's own mtime bumps when a file is created in it, so a
        //    handful of Directory.GetLastWriteTimeUtc calls is O(1) and exact enough.
        private int      _prodGraceMin        = 25;    // replay-clock minutes of grace before demanding output
        private DateTime _prodStampAtStart    = DateTime.MinValue;   // newest corpus mtime when the job began
        private DateTime _prodClockAtStart    = DateTime.MinValue;   // replay clock when the job began
        private bool     _prodProven;                  // this job has produced something — gate satisfied, stop checking
        private bool     _prodGateOff;                 // conf `productivityGraceMin = 0` disables it

        // ── v0.2.0 · JOB WINDOW GUARD ───────────────────────────────────────────────────────────────────
        //    Live 2026-08-02: Conductor announced job 3/5 as 2026-05-17→2026-05-29 while the replay clock sat
        //    at 2026-04-26…04-29 — three weeks outside its own window — and ran on regardless, stamping
        //    `session` rows into run-log.jsonl carrying the 05-17 job's from/to. Completion only ever tested
        //    `clk.Date > To.Date`, which a clock BEFORE the window never trips. Even had it recorded, every
        //    row would have been labelled with a window it never visited. A run that mislabels is worse than
        //    one that fails, because the failure is invisible downstream.
        private int      _windowGuardHours    = 48;    // slack each side; 0 disables
        private bool     _windowTripped;
        // v0.2.0c — minutes, not hours. 0 falls back to the legacy SeekTolHours band for coarse seeks.
        private int      _seekTolMin          = 90;
        private bool     _requireStrategy     = true;   // v0.2.0c pre-flight; false = legacy
        // ── v0.2.0e · WE MAY HAVE BEEN BREAKING OUR OWN SEEK (2026-08-02) ──────────────────────────────
        //    Seek() zeroed PlaybackSpeed immediately before calling Reset, "to let the adapter settle".
        //    But bug #3 in this file's own changelog records that Reset SLAMS PlaybackSpeed TO int.MaxValue
        //    on a delay -- i.e. Reset drives the speed itself as part of repositioning. Pausing the engine
        //    and then asking it to move may be why Reset silently no-ops: measured on both boxes today,
        //    clock identical before and after, callback in ~100 ms, every time. The one seek that DID work
        //    (legacy-node, earlier) happened while the replay was already running.
        //    ⇒ `seekPauseFirst = false` skips the pre-pause. Default stays TRUE so nothing changes for
        //      anyone who has a working seek; we flip it in Run.conf and measure.
        private bool     _seekPauseFirst      = true;
        //    And the guaranteed fallback: don't depend on Reset at all. `seekMode = none` runs the job from
        //    wherever the clock already is, leaving the WINDOW GUARD to catch a start that is actually wrong.
        //    Positioning is then done by loading a Playback range whose start IS the intended start, which
        //    is deterministic and identical across boxes -- unlike dragging a 5-day slider by hand.
        private bool     _seekEnabled         = true;
        // ── v0.2.0f · TRANSPORT PRE-FLIGHT ─────────────────────────────────────────────────────────────
        //    See the header. The transport's own state is a Gate 3 input and was the only one still unverified.
        //    `transportPreflight = false` restores the legacy behaviour (clamp late, start regardless).
        private bool     _transportPreflight  = true;
        private int      _transportSettleMs   = 400;   // gap between the two clock samples that detect motion
        //    A replay clock is only nominally continuous, so demand a real gap before calling it "moving".
        //    400 ms at 100× is 40 replay-seconds; anything under this is sampling noise, not playback.
        private const double MovingToleranceSec = 2.0;
        private DateTime _queueProdStamp = DateTime.MinValue;   // corpus mtime when the QUEUE started (v0.2.0b)

        // UI
        private Border _dot;
        private TextBlock _statusTb, _clockTb, _speedTb, _jobTb, _progTb;
        private Button _runBtn, _stopBtn, _retestBtn, _reloadBtn;
        private StackPanel _logPanel; private ScrollViewer _logScroll;
        private DispatcherTimer _tick;

        public SentinelConductorWindow()
        {
            Caption = "Sentinel Conductor"; Width = 620; Height = 700;
            _openedUtc = DateTime.UtcNow;
            ApplyTheme(); Content = BuildLayout();
            EnsureDefaultManifest();
            Closed += (s,e) => { _running=false; try { if (_tick!=null) _tick.Stop(); } catch (Exception _sx) { SentinelCore.Swallow("SentinelConductor.SentinelConductorWindow", _sx); } };
            SelfTest();
            _tick = new DispatcherTimer(DispatcherPriority.Background, Dispatcher){ Interval = TimeSpan.FromSeconds(1) };
            _tick.Tick += (s,e) => OnTick();
            _tick.Start();
        }

        // ── layout ──
        private FrameworkElement BuildLayout()
        {
            var root = new DockPanel { Background = Bg, LastChildFill = true };

            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12,10,12,6) };
            head.Children.Add(new TextBlock { Text="CONDUCTOR", Foreground=Text, FontWeight=FontWeights.Bold, FontSize=15, VerticalAlignment=VerticalAlignment.Center });
            head.Children.Add(Chip(Ver)); head.Children.Add(Chip("REPLAY TRANSPORT"));
            DockPanel.SetDock(head, Dock.Top); root.Children.Add(head);

            var statusRow = new StackPanel { Orientation=Orientation.Horizontal, Margin=new Thickness(12,0,12,6) };
            _dot = new Border { Width=10, Height=10, CornerRadius=new CornerRadius(5), Background=Muted, VerticalAlignment=VerticalAlignment.Center, Margin=new Thickness(0,0,7,0) };
            statusRow.Children.Add(_dot);
            _statusTb = new TextBlock { Text="checking…", Foreground=Ink2, FontSize=12, VerticalAlignment=VerticalAlignment.Center };
            statusRow.Children.Add(_statusTb);
            DockPanel.SetDock(statusRow, Dock.Top); root.Children.Add(statusRow);

            // transport readout
            var tp = new Border { Background=Card, BorderBrush=Edge, BorderThickness=new Thickness(1), Margin=new Thickness(12,2,12,6), Padding=new Thickness(10,8,10,8) };
            var tpv = new StackPanel();
            _clockTb = new TextBlock { Text="clock  —",  Foreground=Text,  FontSize=13, FontFamily=new FontFamily("Consolas") };
            _speedTb = new TextBlock { Text="speed  —",  Foreground=Ink2,  FontSize=12, FontFamily=new FontFamily("Consolas"), Margin=new Thickness(0,3,0,0) };
            _jobTb   = new TextBlock { Text="job    idle", Foreground=Muted, FontSize=12, FontFamily=new FontFamily("Consolas"), Margin=new Thickness(0,3,0,0) };
            tpv.Children.Add(_clockTb); tpv.Children.Add(_speedTb); tpv.Children.Add(_jobTb);
            tp.Child = tpv;
            DockPanel.SetDock(tp, Dock.Top); root.Children.Add(tp);

            var info = new TextBlock { Foreground=Muted, FontSize=11, Margin=new Thickness(12,0,12,6), TextWrapping=TextWrapping.Wrap,
                Text="Jobs read Sentinel\\Conductor\\Run.conf. Connect the Playback connection manually first (auto-connect lands in v0.2.0). Seek pauses → Reset → restores speed; session-boundary only until Test C passes." };
            DockPanel.SetDock(info, Dock.Top); root.Children.Add(info);

            // speed controls
            var spRow = new StackPanel { Orientation=Orientation.Horizontal, Margin=new Thickness(12,2,12,4) };
            spRow.Children.Add(SmallBtn("⏸ PAUSE", () => SetSpeed(0)));
            spRow.Children.Add(SmallBtn("10×",  () => SetSpeed(10)));
            spRow.Children.Add(SmallBtn("100×", () => SetSpeed(100)));
            spRow.Children.Add(SmallBtn("MAX",  () => SetSpeed(_maxSpeed)));
            spRow.Children.Add(SmallBtn("⏭ NEXT SESSION", () => SeekNextSession()));
            DockPanel.SetDock(spRow, Dock.Top); root.Children.Add(spRow);

            var btnRow = new StackPanel { Orientation=Orientation.Horizontal, Margin=new Thickness(12,4,12,6) };
            _runBtn    = Btn("RUN QUEUE", Accent, true); _runBtn.Click += (s,e) => StartQueue();
            _stopBtn   = Btn("STOP", Edge, false); _stopBtn.IsEnabled=false; _stopBtn.Click += (s,e) => StopQueue("operator");
            _reloadBtn = Btn("RELOAD CONF", Edge, false); _reloadBtn.Click += (s,e) => { _logPanel.Children.Clear(); LoadManifest(true); };
            _retestBtn = Btn("RE-TEST", Edge, false); _retestBtn.Click += (s,e) => SelfTest();
            btnRow.Children.Add(_runBtn); btnRow.Children.Add(_stopBtn); btnRow.Children.Add(_reloadBtn); btnRow.Children.Add(_retestBtn);
            DockPanel.SetDock(btnRow, Dock.Top); root.Children.Add(btnRow);

            _progTb = new TextBlock { Text="", Foreground=Text, FontSize=12, Margin=new Thickness(12,0,12,4) };
            DockPanel.SetDock(_progTb, Dock.Top); root.Children.Add(_progTb);

            _logPanel = new StackPanel { Margin=new Thickness(8,2,8,8) };
            _logScroll = new ScrollViewer { VerticalScrollBarVisibility=ScrollBarVisibility.Auto, Content=_logPanel, Background=Card, Margin=new Thickness(8) };
            root.Children.Add(_logScroll);
            return root;
        }

        // ═══════════════════════ SELF-TEST (fail loud, never silently idle) ═══════════════════════
        private void SelfTest()
        {
            _ready=false; _pbType=null; _piSpeed=_piNowEst=_piNowLocal=_piPlaybackConn=null; _fiMaxSpeed=null; _miReset=_miMinMax=null;
            _maxSpeed=SafeMaxSpeed; _maxSpeedReal=false;
            var diag = new List<string>();

            try
            {
                Assembly core = typeof(Connection).Assembly;
                diag.Add("NinjaTrader.Core " + core.GetName().Version);

                _pbType = core.GetType("NinjaTrader.Adapter.PlaybackAdapter", false);
                if (_pbType == null) diag.Add("✗ type NinjaTrader.Adapter.PlaybackAdapter NOT FOUND");
                else
                {
                    diag.Add("✓ " + _pbType.FullName);
                    _piSpeed    = _pbType.GetProperty("PlaybackSpeed", BF);
                    _piNowEst   = _pbType.GetProperty("NowEst",        BF);
                    _piNowLocal = _pbType.GetProperty("NowLocal",      BF);
                    _fiMaxSpeed = _pbType.GetField("MaxSpeedValue",    BF);
                    _miReset    = _pbType.GetMethod("Reset", BF, null, new[]{ typeof(DateTime), typeof(Action<bool>) }, null);
                    _miMinMax   = _pbType.GetMethod("GetReplayMinMaxDates", BF, null,
                                     new[]{ typeof(string), typeof(DateTime).MakeByRefType(), typeof(DateTime).MakeByRefType() }, null);

                    diag.Add(Mark(_piSpeed   != null, "PlaybackSpeed (get/set)"));
                    diag.Add(Mark(_piNowEst  != null, "NowEst (the replay clock)"));
                    diag.Add(Mark(_fiMaxSpeed!= null, "MaxSpeedValue (static readonly field)"));
                    diag.Add(Mark(_miReset   != null, "Reset(DateTime, Action<bool>)  ← seek"));
                    diag.Add(Mark(_miMinMax  != null, "GetReplayMinMaxDates(string, out, out)"));

                    // MaxSpeedValue is static READONLY (not const) → the value exists only at runtime.
                    if (_fiMaxSpeed != null)
                    {
                        try
                        {
                            object v = _fiMaxSpeed.GetValue(null);
                            if (v is int)
                            {
                                _maxSpeedRaw = (int)v;
                                if (_maxSpeedRaw > 0 && _maxSpeedRaw <= PlausibleMaxSpeed)
                                { _maxSpeed = _maxSpeedRaw; _maxSpeedReal = true; diag.Add("   → MAX SPEED = " + _maxSpeed + "×  (read from NT)"); }
                                else
                                    diag.Add("   ⚠ MaxSpeedValue = " + _maxSpeedRaw
                                             + (_maxSpeedRaw == int.MaxValue ? " (int.MaxValue → NT declares NO CAP)" : " (implausible)")
                                             + " — not an operational ceiling, ignoring");
                            }
                        }
                        catch (Exception ex) { diag.Add("   → MaxSpeedValue read failed: " + ex.Message); }
                    }
                    if (!_maxSpeedReal)
                        diag.Add("   ⚠ MAX SPEED = " + SafeMaxSpeed + "× (conservative fallback — raise with `maxSpeed = N` in Run.conf once proven)");
                }

                _piPlaybackConn = typeof(Connection).GetProperty("PlaybackConnection", BF);
                diag.Add(Mark(_piPlaybackConn != null, "Connection.PlaybackConnection (static)"));
            }
            catch (Exception ex) { diag.Add("self-test exception: " + ex.Message); }

            _ready = _pbType!=null && _piSpeed!=null && _piNowEst!=null && _miReset!=null && _piPlaybackConn!=null;

            if (_logPanel!=null)
            {
                _logPanel.Children.Clear();
                AddLog("— self-test —", Accent);
                foreach (string d in diag) AddLog("  " + d, d.StartsWith("✗") || d.Contains("NOT FOUND") ? Red : Muted);
            }

            if (_ready)
            {
                SetStatus(Green, "READY · transport resolved · max " + _maxSpeed + "×");
                // Log the PASS too, not just the fail — a green banner only exists on the screen, and the suite is
                // operated by reading sentinel.log, not screenshots ([[ninjatrader-observability]]).
                SentinelCore.Log(LogTag, "SELF-TEST PASS | NT " + NtVersion()
                    + " | maxSpeed=" + _maxSpeed + (_maxSpeedReal ? " (read)" : " (SAFE FALLBACK; NT MaxSpeedValue=" + _maxSpeedRaw + ")")
                    + " | Reset=" + (_miReset!=null) + " GetReplayMinMaxDates=" + (_miMinMax!=null)
                    + " NowEst=" + (_piNowEst!=null) + " Speed=" + (_piSpeed!=null) + " PlaybackConn=" + (_piPlaybackConn!=null)
                    + " | playbackConnected=" + PlaybackConnected());
            }
            else
            {
                SetStatus(Red, "NOT READY · playback transport did not resolve — see the dump");
                SentinelCore.Log(LogTag, "SELF-TEST FAIL | " + string.Join(" ; ", diag));
            }
            if (_runBtn != null) _runBtn.IsEnabled = _ready;
            LoadManifest(false);
        }
        private static string Mark(bool ok, string what){ return (ok ? "✓ " : "✗ ") + what; }

        // ═══════════════════════ TRANSPORT PRIMITIVES ═══════════════════════
        private bool PlaybackConnected()
        {
            try
            {
                if (_piPlaybackConn == null) return false;
                var c = _piPlaybackConn.GetValue(null) as Connection;
                return c != null && c.Status == ConnectionStatus.Connected;
            }
            catch { return false; }
        }

        private DateTime ClockEst()
        {
            try { if (_piNowEst != null) return (DateTime)_piNowEst.GetValue(null); } catch (Exception _sx) { SentinelCore.Swallow("SentinelConductor.ClockEst", _sx); }
            return DateTime.MinValue;
        }

        private int Speed()
        {
            try { if (_piSpeed != null) return (int)_piSpeed.GetValue(null); } catch (Exception _sx) { SentinelCore.Swallow("SentinelConductor.Speed", _sx); }
            return -1;
        }

        private void SetSpeed(int s)
        {
            if (!_ready) return;
            int clamped = Math.Max(0, Math.Min(_maxSpeed, s));
            _intendedSpeed = clamped;   // this is now what the invariant holds against NT's max-injection
            _speedClampWarned = false;
            try
            {
                _piSpeed.SetValue(null, clamped);
                if (clamped != s) AddLog("speed " + s + "× clamped to " + clamped + "× (NT max)", Amber);
                else AddLog(clamped == 0 ? "⏸ paused" : "speed → " + clamped + "×", Ink2);
            }
            catch (Exception ex) { AddLog("✗ set speed failed: " + ex.Message, Red); SentinelCore.Log(LogTag, "set speed failed: " + ex.Message); }
        }

        // SEEK — pause → Reset(target, callback) → HOLD the intended resume speed (re-asserted for a few ticks,
        // because NT leaves PlaybackSpeed maxed after Reset). Never a bare NowEst assignment (spec §3.1).
        // resumeSpeed = the speed to run at AFTER landing (0 = stay paused). Intent, clamped — not a live capture.
        private void Seek(DateTime targetEst, string why, int resumeSpeed)
        {
            if (!_ready || _seeking) return;
            if (!PlaybackConnected()) { AddLog("✗ seek refused — playback not connected", Red); return; }

            _seekBefore = ClockEst();
            _seekTarget = targetEst;
            _seekWhy    = why;
            _seekStartedUtc = DateTime.UtcNow;
            _seekResolved   = false;
            _intendedSpeed = Math.Max(0, Math.Min(_maxSpeed, resumeSpeed));   // the standing invariant now holds this
            _speedClampWarned = false;
            _seeking = true;
            try
            {
                // v0.2.0e — conditional. See _seekPauseFirst: Reset drives the speed itself, so pausing
                // first may be what stops it repositioning at all.
                if (_seekPauseFirst) _piSpeed.SetValue(null, 0);
                // ⚠ Reset's Action<bool> does NOT mean "success" (§3.1a) — we judge by the landed clock, not the
                //    flag. And a seek into a no-data gap never calls back at all — the OnTick watchdog covers that.
                Action<bool> cb = rawFlag => Dispatcher.InvokeAsync(new Action(() => ResolveSeek("callback", rawFlag.ToString())));
                _miReset.Invoke(null, new object[]{ targetEst, cb });
            }
            catch (Exception ex)
            {
                _seeking = false; _seekResolved = true;
                try { _piSpeed.SetValue(null, _intendedSpeed); } catch (Exception _sx) { SentinelCore.Swallow("SentinelConductor.Seek", _sx); }
                AddLog("✗ seek invoke failed: " + ex.Message, Red);
                SentinelCore.Log(LogTag, "SEEK invoke failed: " + ex.Message);
            }
        }

        // Evaluate where a seek actually LANDED — called by Reset's callback OR the watchdog (whichever wins).
        // The clock is ground truth; the callback flag is not (§3.1a). A seek into a no-data gap only ever
        // reaches here via the watchdog, so a wedged queue self-heals instead of hanging forever.
        private void ResolveSeek(string via, string rawFlag)
        {
            if (_seekResolved) return;   // callback + watchdog can't both fire
            _seekResolved = true;
            _seeking = false;

            DateTime after = ClockEst();
            double deltaH = (after - _seekTarget).TotalHours;
            // ── v0.2.0c · THE TOLERANCE WAS SIZED FOR THE WRONG QUESTION (live, 2026-08-02) ──────────────
            //    `SeekTolHours = 26` was chosen for SESSION-granularity seeks, where landing anywhere inside
            //    the right day is fine. On the Gate 3 1-Minute cell worker-1 targeted 2026-04-21 17:00 and
            //    landed at 07:09 — off by 9.8 h, well inside 26 — so it logged "seek ok" and started the job
            //    ten hours from where it meant to, mid-session, in a stretch where the strategy never fires.
            //    Then the productivity gate stopped it and we spent twenty minutes blaming the gate.
            //    ⚠ The job window guard could not catch it either: 04-21 07:09 IS inside 04-21→04-23. Being
            //    in the right WINDOW while starting in the wrong SESSION is exactly how a partial session is
            //    baked and then labelled whole.
            //    ⇒ A seek that names a session start must land near THAT START. `seekTolMin` (default 90) is
            //    judged first; the legacy hour band only applies when the caller asked for a coarse seek.
            double deltaMin = Math.Abs((after - _seekTarget).TotalMinutes);
            bool landed = _seekTolMin > 0 ? (deltaMin <= _seekTolMin)
                                          : (Math.Abs(deltaH) <= SeekTolHours);
            string tail = " | target " + Iso(_seekTarget) + " | via=" + via + " rawFlag=" + rawFlag;

            if (landed)
            {
                AddLog("⏭ seek ok · " + Iso(_seekBefore) + " → " + Iso(after) + "  (" + _seekWhy + ")", Green);
                SentinelCore.Log(LogTag, "SEEK " + _seekWhy + " | " + Iso(_seekBefore) + " -> " + Iso(after) + tail);
            }
            else
            {
                // Landed off the target — usually the target time has NO LOADED DATA (e.g. resume outside the
                // Playback range). Flag it loudly; if we're running a job, abort that job rather than grind on a
                // frozen clock (the operator needs to widen the Playback range).
                AddLog("⚠ seek OFF-TARGET · " + Iso(after) + " (wanted " + Iso(_seekTarget) + ", Δ" + deltaH.ToString("0.0") + "h) — target likely has no loaded replay data  (" + _seekWhy + ")", Amber);
                SentinelCore.Log(LogTag, "SEEK OFF-TARGET " + _seekWhy + " | landed " + Iso(after) + tail + " deltaH=" + deltaH.ToString("0.0"));
                if (_running && _cur != null)
                {
                    LogReplayCoverage(_cur.Instrument, "OFF-TARGET seek");   // v0.2.0d — measure, don't theorise
                    AddLog("   ↳ aborting job — widen the Playback data range to cover " + Iso(_seekTarget), Red);
                    FinishJob("aborted-nodata");
                    return;
                }
            }
            try { _piSpeed.SetValue(null, _intendedSpeed); } catch (Exception _sx) { SentinelCore.Swallow("SentinelConductor.ResolveSeek", _sx); }   // standing invariant holds it thereafter
            _lastClock = after; _lastClockMoveUtc = DateTime.UtcNow; _stalled = false;
            // v0.2.0 — anchor the productivity gate on the LANDED clock, never on the pre-seek one: a seek
            // jumps weeks, and measuring "replay minutes elapsed" across that jump would satisfy the gate
            // instantly without a single bar having been replayed.
            if (_running && _cur != null) _prodClockAtStart = after;
        }


        // ═════════════════════════════════════════════════════════════════════════════════════
        //  AUTO-RECOVERY (v0.2.0) — the escalation ladder. See the field block for the rationale.
        //
        //  ⚠ THIS METHOD CAN RESTART NINJATRADER. Everything in it is written to fail toward doing
        //  NOTHING: capped attempts, a cooldown, a checkpoint before the destructive rung, and an
        //  external hand-off (an AddOn cannot cleanly kill its own host, and must not try).
        // ═════════════════════════════════════════════════════════════════════════════════════
        private void TryRecover(DateTime clk, double frozenSec)
        {
            // COOLDOWN — a stall arriving moments after a recovery is almost always the SAME stall
            // still resolving (a seek takes time, a restart takes longer). Acting again here is how
            // a recovery loop is born.
            if (_lastRecoveryUtc != DateTime.MinValue &&
                (DateTime.UtcNow - _lastRecoveryUtc).TotalSeconds < _recoverCooldownSec)
            {
                AddLog("· recovery on cooldown (" + _recoverCooldownSec + "s) — not acting", Amber);
                return;
            }

            // RUNG 3 — give up LOUDLY. Stopping and alerting beats thrashing: a box that restarts
            // forever looks alive on every dashboard while producing nothing, which is the exact
            // failure shape this project keeps paying for.
            if (_recoveries >= _maxRecoveries)
            {
                AddLog("✗ recovery budget spent (" + _recoveries + "/" + _maxRecoveries + ") — stopping the queue", Red);
                SentinelCore.Log(LogTag, "RECOVERY EXHAUSTED after " + _recoveries + " attempt(s); stopping. clock=" + Iso(clk));
                WriteRunLog("recover-exhausted", _cur, clk, _recoveries + " attempts");
                try
                {
                    SentinelCore.Alerts.Critical("Conductor: bake stopped",
                        "Auto-recovery exhausted after " + _recoveries + " attempt(s). Replay clock stuck at "
                        + Iso(clk) + ". Job " + (_cur != null ? _cur.ToString() : "-") + ". Needs a human.");
                }
                catch (Exception _sx) { SentinelCore.Swallow("SentinelConductor.TryRecover", _sx); }
                StopQueue("auto-recovery exhausted");
                return;
            }

            _recoveries++;
            _lastRecoveryUtc = DateTime.UtcNow;

            // RUNG 1 — RE-SEEK. Non-destructive and it fixes the two commonest wedges: a seek whose
            // callback never landed, and NT slamming PlaybackSpeed to max and leaving it there.
            // Tried ONCE per stall episode; _reseekTried re-arms when the clock moves again.
            if (_recoverReseekFirst && !_reseekTried)
            {
                _reseekTried = true;
                DateTime target = clk.Date.AddHours(_sessionStartHourEst);
                if (target > clk) target = target.AddDays(-1);     // seek to THIS session's start, not the future
                AddLog("↻ recovery 1/2 — re-seeking to " + Iso(target) + " and re-asserting speed", Amber);
                SentinelCore.Log(LogTag, "RECOVER reseek → " + Iso(target) + " (stall " + frozenSec.ToString("0") + "s, attempt " + _recoveries + ")");
                WriteRunLog("recover-reseek", _cur, clk, "stall " + frozenSec.ToString("0") + "s");
                try { Seek(target, "auto-recovery re-seek", _cur != null ? _cur.Speed : _intendedSpeed); }
                catch (Exception _sx) { SentinelCore.Swallow("SentinelConductor.TryRecover", _sx); }
                return;
            }

            // RUNG 2 — RESTART NINJATRADER, via an EXTERNAL scheduled task.
            //
            // Why a task and not Process.Kill: (a) an AddOn killing its own host cannot then relaunch
            // it; (b) NT shows an exit-confirmation and a save-workspace prompt, so a forced kill
            // risks the documented workspace-blanking bug; (c) `schtasks /run` on a task created with
            // /IT executes in the INTERACTIVE session, which is the only way UI automation works at
            // all from a service-side context.
            //
            // The checkpoint is written FIRST and deliberately: everything after this line may not
            // execute, and a resume that re-bakes one session is free while a lost checkpoint is not.
            AddLog("↻ recovery 2/2 — checkpointing and requesting an NT restart via '" + _restartTaskName + "'", Red);
            SentinelCore.Log(LogTag, "RECOVER restart-request task=" + _restartTaskName
                + " clock=" + Iso(clk) + " stall=" + frozenSec.ToString("0") + "s attempt=" + _recoveries);
            WriteRunLog("recover-restart", _cur, clk, "task=" + _restartTaskName);

            try
            {
                SentinelCore.Alerts.Critical("Conductor: restarting NinjaTrader",
                    "Replay clock frozen " + frozenSec.ToString("0") + "s at " + Iso(clk)
                    + " and a re-seek did not clear it. Requesting restart (" + _recoveries + "/" + _maxRecoveries
                    + "). The queue resumes from the last checkpoint if autostart is on.");
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelConductor.TryRecover", _sx); }

            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("schtasks.exe", "/run /tn \"" + _restartTaskName + "\"")
                { UseShellExecute = false, CreateNoWindow = true };
                System.Diagnostics.Process.Start(psi);
                AddLog("· restart requested — the queue resumes from its checkpoint if autostart=true", Amber);
            }
            catch (Exception ex)
            {
                // The hand-off itself failed (task missing / no permission). Say so plainly — a silent
                // failure here would leave the operator believing recovery is armed when it is not.
                SentinelCore.Swallow("SentinelConductor.RestartTask", ex);
                AddLog("✗ could not run task '" + _restartTaskName + "' — recovery cannot restart NT", Red);
                SentinelCore.Log(LogTag, "RECOVER restart FAILED: " + ex.Message
                    + " — create it with: schtasks /create /tn " + _restartTaskName + " /it /sc once /st 00:00 /tr <script>");
            }
        }

        /// <summary>"GC 06-26" -> "GC"; "GC ##-##" -> "GC". Matches SentinelCore.ScopeOf, which keys on
        /// Instrument.MasterInstrument.Name — the reason two GC contracts collide without a lane.</summary>
        private static string MasterOf(string instrument)
        {
            if (string.IsNullOrEmpty(instrument)) return "";
            string s = instrument.Trim();
            int sp = s.IndexOf(' ');
            return sp > 0 ? s.Substring(0, sp) : s;
        }

        /// <summary>Returns a reason to REFUSE the job, or null to proceed.
        ///
        /// THE BUG THIS EXISTS FOR (2026-07-25): leg 1 was launched with `lane=AUD0626` while the chart's
        /// F6 ScopeLane still read TEST. The Council fused with the SCRAPPED 7-voter roster and excluded
        /// 12 computed voters as undeclared. Nothing errored — it just baked worthless rows for as long
        /// as nobody read `roster declares` in the log.
        ///
        /// POSITIVE DISAGREEMENT ONLY. A Council that is publishing a DIFFERENT lane for this
        /// instrument blocks. ABSENCE does not block: at job start a freshly loaded chart may not have
        /// published a verdict yet, and blocking on silence would make a correct setup un-runnable
        /// (absence of evidence is not evidence — see [[measure-dont-infer]]). Absence warns loudly.</summary>
        private string LaneGuardTrip(Job job)
        {
            try
            {
                string want = (job.Lane ?? "").Trim();
                string mi = MasterOf(job.Instrument);
                if (mi.Length == 0) return null;

                var states = SentinelCore.AllCouncilStates();
                if (states == null || states.Count == 0)
                {
                    AddLog("⚠ lane guard: no Council is publishing yet — cannot verify the chart's lane is '"
                         + (want.Length > 0 ? want : "<bare>") + "'. Check `roster declares` once bars flow.", Amber);
                    return null;
                }

                var mismatched = new List<string>();
                int matched = 0;
                foreach (var cs in states)
                {
                    string scope = (cs == null ? null : cs.Scope) ?? "";
                    if (scope.Length == 0) continue;
                    int dot = scope.IndexOf('.');
                    if (dot <= 0 || !string.Equals(scope.Substring(0, dot), mi, StringComparison.OrdinalIgnoreCase)) continue;
                    int at = scope.IndexOf('@');
                    string lane = at >= 0 ? scope.Substring(at + 1) : "";
                    if (string.Equals(lane, want, StringComparison.OrdinalIgnoreCase)) matched++;
                    else mismatched.Add(scope + " (lane '" + (lane.Length > 0 ? lane : "<bare>") + "')");
                }

                if (mismatched.Count > 0)
                    return "LANE MISMATCH — job " + (_ji + 1) + " asks for lane '" + (want.Length > 0 ? want : "<bare>")
                         + "' on " + mi + ", but " + mismatched.Count + " Council(s) publish: " + string.Join(", ", mismatched.ToArray())
                         + ". Set the chart's F6 'Scope Lane' (group '0. Scope Lane') to '" + want
                         + "' and reload, or fix the job line. REFUSING so this cannot bake the wrong roster.";

                if (matched == 0)
                    AddLog("⚠ lane guard: no Council on " + mi + " is publishing — cannot verify the lane.", Amber);
                else
                    AddLog("lane guard OK — " + matched + " Council(s) on " + mi + " publishing lane '"
                         + (want.Length > 0 ? want : "<bare>") + "'", Muted);
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelConductor.LaneGuardTrip", _sx); }
            return null;   // never let the guard itself break a run
        }

        // v0.1.0: session-boundary seek only. The next boundary = the next SessionStartHourEst crossing.
        // Manual seek preserves the operator's current speed (0 if paused) as the resume intent.
        private void SeekNextSession()
        {
            DateTime now = ClockEst();
            if (now == DateTime.MinValue) { AddLog("✗ no replay clock — is playback connected?", Red); return; }
            Seek(NextSessionStart(now), "next session boundary", Math.Max(0, Speed()));
        }

        private DateTime NextSessionStart(DateTime est)
        {
            DateTime today = est.Date.AddHours(_sessionStartHourEst);
            DateTime next  = est < today ? today : today.AddDays(1);
            while (next.DayOfWeek == DayOfWeek.Saturday) next = next.AddDays(1);   // no Saturday session
            return next;
        }

        // Interrogate a .nrd for its true coverage (spec §2.1) — used for planning + operator sanity.
        // ── v0.2.0d · WHAT DOES NT ACTUALLY THINK IT HAS? (2026-08-02) ──────────────────────────────────
        //    worker-1's Reset was a silent no-op: clock identical before and after, resolved in 125 ms,
        //    while legacy-node seeked the same target correctly. Three theories died in twenty minutes — stale
        //    index, unloaded data, uninitialised engine — because each was reasoning, not measurement. The
        //    Playback SLIDER was misread as proof the data was loaded; its range is the CONNECTION's
        //    Start/End, not the indexed data. `GetReplayMinMaxDates` was reflected on day one and never
        //    called. It answers the question directly, per file, from NT's own mouth.
        //    ⚠ Logged at job start AND on an off-target seek — the moment you want it is the moment it fails.
        private void LogReplayCoverage(string instrument, string why)
        {
            try
            {
                if (_miMinMax == null) { SentinelCore.Log(LogTag, "COVERAGE unavailable — GetReplayMinMaxDates not bound"); return; }
                string dir = Path.Combine(Globals.UserDataDir, "db", "replay", instrument);
                if (!Directory.Exists(dir)) { SentinelCore.Log(LogTag, "COVERAGE: no replay folder for '" + instrument + "'"); return; }
                string[] files = Directory.GetFiles(dir, "*.nrd");
                Array.Sort(files);
                DateTime lo = DateTime.MaxValue, hi = DateTime.MinValue;
                int ok = 0, bad = 0;
                var sb = new StringBuilder();
                foreach (string f in files)
                {
                    DateTime a, b;
                    if (ReplayCoverage(f, out a, out b))
                    {
                        ok++;
                        if (a < lo) lo = a;
                        if (b > hi) hi = b;
                        if (sb.Length < 700) sb.Append(Path.GetFileNameWithoutExtension(f)).Append('[')
                                               .Append(a.ToString("MM-dd HH:mm")).Append("..").Append(b.ToString("MM-dd HH:mm")).Append("] ");
                    }
                    else { bad++; if (sb.Length < 700) sb.Append(Path.GetFileNameWithoutExtension(f)).Append("[UNREADABLE] "); }
                }
                SentinelCore.Log(LogTag, "COVERAGE " + why + " " + instrument + " — " + files.Length + " file(s), "
                    + ok + " readable, " + bad + " unreadable | span "
                    + (ok > 0 ? Iso(lo) + " .. " + Iso(hi) : "NONE"));
                if (sb.Length > 0) SentinelCore.Log(LogTag, "COVERAGE detail: " + sb.ToString().Trim());
                AddLog("coverage " + instrument + ": " + ok + "/" + files.Length + " readable, span "
                    + (ok > 0 ? Iso(lo) + " .. " + Iso(hi) : "NONE"), ok == files.Length && ok > 0 ? Muted : Red);
            }
            catch (Exception ex) { SentinelCore.Log(LogTag, "COVERAGE probe failed: " + ex.Message); }
        }

        private bool ReplayCoverage(string file, out DateTime min, out DateTime max)
        {
            min = max = DateTime.MinValue;
            if (_miMinMax == null || !File.Exists(file)) return false;
            try
            {
                object[] args = new object[]{ file, DateTime.MinValue, DateTime.MinValue };
                _miMinMax.Invoke(null, args);
                min = (DateTime)args[1]; max = (DateTime)args[2];
                return true;
            }
            catch { return false; }
        }

        // ═══════════════════════ v0.2.0f · TRANSPORT PRE-FLIGHT ═══════════════════════
        //  Assert the transport's OWN state before the queue advances — the Gate 3 input that had no file to
        //  hash and so was never checked. Full reasoning in the header changelog. Three parts, deliberately
        //  of two different kinds: ① and ② are ACTIONS/REFUSALS on what we have measured, ③ is a MEASUREMENT
        //  of what we have not yet proven. Blocking on a discriminator we cannot separate would break every
        //  legitimate resume, so it is recorded rather than enforced until the corpus of runs decides it.
        private bool TransportReady(out string why)
        {
            why = null;
            try
            {
                if (_piNowEst == null || _piSpeed == null) { why = "playback transport not resolved (self-test)"; return false; }

                Job j0 = _jobs.Count > 0 ? _jobs[0] : null;

                // ① CLAMP FIRST, ASK SECOND. On worker-1 NT sat at int.MaxValue and burned 27 replay-hours
                //    before the first clamp tick — putting the clock where the seek could no longer recover it.
                //    A clamp that runs after the damage is not a clamp.
                int speedBefore = -1;
                try { speedBefore = (int)_piSpeed.GetValue(null); } catch (Exception _sx) { SentinelCore.Swallow("SentinelConductor.TransportReady.readSpeed", _sx); }
                int want = j0 != null ? Math.Min(j0.Speed, _maxSpeed) : _maxSpeed;
                if (speedBefore > want)
                {
                    try
                    {
                        _piSpeed.SetValue(null, want);
                        SentinelCore.Log(LogTag, "PRE-FLIGHT CLAMP: speed " + speedBefore + " → " + want
                            + " before the queue advances (was " + (speedBefore == int.MaxValue ? "int.MaxValue" : speedBefore.ToString()) + ")");
                        AddLog("pre-flight clamp: speed " + speedBefore + " → " + want, Amber);
                    }
                    catch (Exception ex) { SentinelCore.Log(LogTag, "PRE-FLIGHT CLAMP FAILED: " + ex.Message); }
                }

                // ② REFUSE A MOVING TRANSPORT. Two samples, a real gap apart. This is the discriminator we
                //    have actually measured, so it is the one that blocks.
                DateTime c1 = ClockEst();
                if (_transportSettleMs > 0) System.Threading.Thread.Sleep(_transportSettleMs);
                DateTime c2 = ClockEst();
                double movedSec = (c2 - c1).TotalSeconds;

                // ③ MEASURE POSITION — evidence only, never a refusal (see the header).
                string posNote = "range start unknown";
                if (j0 != null)
                {
                    DateTime lo, hi;
                    if (ReplaySpan(j0.Instrument, out lo, out hi))
                    {
                        double offH = (c2 - lo).TotalHours;
                        posNote = "clock " + Iso(c2) + " is " + offH.ToString("0.0") + " h from range start " + Iso(lo);
                        SentinelCore.Log(LogTag, "PRE-FLIGHT POSITION " + j0.Instrument + " | " + posNote
                            + " | span " + Iso(lo) + " .. " + Iso(hi)
                            + " | movedSec=" + movedSec.ToString("0.0"));
                    }
                    else
                        SentinelCore.Log(LogTag, "PRE-FLIGHT POSITION unavailable for " + j0.Instrument
                            + " (no readable .nrd span) | clock " + Iso(c2) + " | movedSec=" + movedSec.ToString("0.0"));
                }

                if (movedSec > MovingToleranceSec)
                {
                    why = "replay clock ADVANCED " + movedSec.ToString("0.0") + " replay-seconds in "
                        + _transportSettleMs + " ms of wall time (" + Iso(c1) + " → " + Iso(c2) + ") — playback is running; "
                        + posNote;
                    return false;
                }

                why = "clock " + Iso(c2) + " steady over " + _transportSettleMs + " ms; " + posNote
                    + (speedBefore > want ? "; speed clamped " + speedBefore + "→" + want : "; speed " + speedBefore);
                return true;
            }
            catch (Exception ex)
            {
                // Fail CLOSED. An unreadable transport is exactly the state this check exists to catch, and a
                // pre-flight that passes when it could not look is the failure mode of v0.2.0b's resume check.
                why = "transport pre-flight could not read the transport: " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        // Min/max across every readable .nrd for an instrument. Same primitive LogReplayCoverage uses; kept
        // separate because the pre-flight wants the SPAN, not the per-file report.
        private bool ReplaySpan(string instrument, out DateTime lo, out DateTime hi)
        {
            lo = DateTime.MaxValue; hi = DateTime.MinValue;
            try
            {
                if (_miMinMax == null) return false;
                string dir = Path.Combine(Globals.UserDataDir, "db", "replay", instrument);
                if (!Directory.Exists(dir)) return false;
                int ok = 0;
                foreach (string f in Directory.GetFiles(dir, "*.nrd"))
                {
                    DateTime a, b;
                    if (!ReplayCoverage(f, out a, out b)) continue;
                    ok++;
                    if (a < lo) lo = a;
                    if (b > hi) hi = b;
                }
                return ok > 0;
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelConductor.ReplaySpan", _sx); return false; }
        }

        // ═══════════════════════ MANIFEST ═══════════════════════
        private string ConfDir(){ return Path.Combine(SentinelCore.SettingsDir, "Conductor"); }
        private string ConfPath(){ return Path.Combine(ConfDir(), "Run.conf"); }
        private string RunLogPath(){ return Path.Combine(ConfDir(), "run-log.jsonl"); }

        private void EnsureDefaultManifest()
        {
            try
            {
                string p = ConfPath(); if (File.Exists(p)) return;
                Directory.CreateDirectory(ConfDir());
                var sb = new StringBuilder();
                sb.Append("# Sentinel Conductor — replay job manifest\n");
                sb.Append("# Settings (key = value):\n");
                sb.Append("sessionStartHourEst = 17    # CME session open (ET) — the checkpoint/seek boundary\n");
                sb.Append("stallSec            = 120   # replay clock frozen this long at speed>0 => STALLED\n");
                sb.Append("\n# -- AUTO-RECOVERY (v0.2.0) - makes a stall cost minutes, not a night. OPT-IN. --\n");
                sb.Append("# On 2026-07-25 the stall detector fired correctly and the box sat wedged for ~4h.\n");
                sb.Append("# The underlying crash (WPF UCEERR render-thread death) is NOT preventable from in\n");
                sb.Append("# here, so the point is recovery, not prevention. Ladder: re-seek -> restart -> stop.\n");
                sb.Append("# autoRecover        = true  # act on a stall instead of only reporting it (default false)\n");
                sb.Append("# recoverReseekFirst = true  # try a cheap re-seek before restarting NT (default true)\n");
                sb.Append("# maxRecoveries      = 3     # then STOP + CRITICAL alert. A loop is worse than a stop.\n");
                sb.Append("# recoverCooldownSec = 300   # ignore a fresh stall this soon after acting (anti-thrash)\n");
                sb.Append("# restartTaskName    = Sentinel-RestartNT   # schtasks task, must be created with /IT\n");
                sb.Append("heartbeatSec        = 60    # sentinel.log heartbeat cadence\n");
                sb.Append("# maxSpeed          = 500   # ceiling for job speeds. NT reports MaxSpeedValue=int.MaxValue\n");
                sb.Append("#                           # (= no cap DECLARED), so the Conductor uses a conservative 100x\n");
                sb.Append("#                           # until YOU raise it here — deliberately, ideally after Test C.\n");
                sb.Append("# autostart         = true  # LIGHTS-OUT: auto-run the queue once Playback is connected.\n");
                sb.Append("#                           # Pair with NT's ConnectOnStartup on the Playback connection +\n");
                sb.Append("#                           # add the Conductor to the workspace → a bake that self-heals\n");
                sb.Append("#                           # across restarts (resume skips already-baked sessions). Default OFF.\n");
                sb.Append("# autostartDelaySec = 20    # wait this long after open for the connection/data to settle.\n");
                sb.Append("\n# -- ARMING (v0.2.0) - autostart is an INTENT, not a standing permission. --\n");
                sb.Append("# 2026-08-02: autostart=true had sat here since 07-30; NT restarted for an unrelated\n");
                sb.Append("# reason and baked a cell nobody asked for - 144 min, no strategy, ZERO rows, silently.\n");
                sb.Append("# A RESUME (checkpoint for this manifest newer than resumeGraceHours) still starts on\n");
                sb.Append("# its own - that is the crash-recovery case autostart exists for. A COLD START needs\n");
                sb.Append("# Conductor\\armed.token, which EXPIRES and is CONSUMED. Clicking RUN is never gated.\n");
                sb.Append("# requireArm         = true  # default ON. false restores the v0.1.x free-for-all.\n");
                sb.Append("# armTtlHours        = 12    # a token older than this is expired, not authorisation\n");
                sb.Append("# resumeGraceHours   = 48    # a checkpoint older than this is history, not a live bake\n");
                sb.Append("\n# -- OUTPUT ASSERTIONS (v0.2.0) - silence must be impossible, not merely unlikely. --\n");
                sb.Append("# productivityGraceMin = 25  # replay-clock minutes allowed with ZERO corpus written\n");
                sb.Append("#                           # before aborting. 0 disables. Catches: no strategy, no\n");
                sb.Append("#                           # recorder, wrong chart, wrong bar type, wrong instrument.\n");
                sb.Append("# windowGuardHours   = 48    # abort if the replay clock is outside the job's own\n");
                sb.Append("#                           # from/to by more than this. 0 disables. A run that\n");
                sb.Append("#                           # MISLABELS is worse than one that fails.\n");
                sb.Append("\n");
                sb.Append("# --- TRANSPORT PRE-FLIGHT (v0.2.0f) ------------------------------------------\n");
                sb.Append("# The transport's own state is a Gate 3 INPUT and was the only one with no file to\n");
                sb.Append("# hash. 2026-08-02: two boxes byte-identical in code, .nrd, historical bars and the\n");
                sb.Append("# strategy blob still diverged - legacy-node seeked on target from a PARKED clock while\n");
                sb.Append("# worker-1's Reset no-opped on a clock already running at int.MaxValue. Clamp before\n");
                sb.Append("# the queue advances; refuse a moving transport; log the distance from range start.\n");
                sb.Append("# transportPreflight = true  # default ON. false = legacy (clamp late, start anyway).\n");
                sb.Append("# transportSettleMs  = 400   # gap between the two clock samples that detect motion.\n");
                sb.Append("\n");
                sb.Append("# Jobs (one per line):  instrument | from | to | speed | lane | note\n");
                sb.Append("# GC 08-26 | 2026-06-22 | 2026-07-15 | 100 | A | canonical GC TBars bake\n");
                sb.Append("# GC 08-26 | 2026-06-22 | 2026-06-24 |  10 | C | Test C slow control\n");
                File.WriteAllText(p, sb.ToString());
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelConductor.EnsureDefaultManifest", _sx); }
        }

        private void LoadManifest(bool verbose)
        {
            _jobs.Clear();
            try
            {
                string p = ConfPath(); if (!File.Exists(p)) EnsureDefaultManifest();
                string[] lines = File.ReadAllLines(p);
                ComputeManifestFingerprint(lines);   // v0.2.0 — before PASS 1, so it never depends on parse state

                // PASS 1 — settings only. Must run before jobs, or a `maxSpeed` line below a job line would
                // fail to raise that job's clamp (line order should not change behaviour).
                _maxSpeedOverride = 0;
                foreach (string raw in lines)
                {
                    string t = raw.Trim();
                    if (t.Length == 0 || t[0] == '#' || t.IndexOf('|') >= 0) continue;
                    int eq = t.IndexOf('='); if (eq <= 0) continue;
                    string k = t.Substring(0,eq).Trim().ToLowerInvariant();
                    string v = StripComment(t.Substring(eq+1)).Trim();
                    int n;
                    if      (k=="sessionstarthourest" && int.TryParse(v, out n)) _sessionStartHourEst = Math.Max(0, Math.Min(23, n));
                    else if (k=="stallsec"            && int.TryParse(v, out n)) _stallSec     = Math.Max(15, n);
                    else if (k=="heartbeatsec"        && int.TryParse(v, out n)) _heartbeatSec = Math.Max(10, n);
                    else if (k=="maxspeed"            && int.TryParse(v, out n) && n > 0) _maxSpeedOverride = Math.Min(n, PlausibleMaxSpeed);
                    else if (k=="autostart")          _autostart = (v=="true" || v=="1" || v=="yes");
                    else if (k=="requirearm")         _requireArm = !(v=="false" || v=="0" || v=="no");
                    else if (k=="armttlhours"         && int.TryParse(v, out n)) _armTtlHours = Math.Max(1, n);
                    else if (k=="resumegracehours"    && int.TryParse(v, out n)) _resumeGraceHours = Math.Max(1, n);
                    else if (k=="productivitygracemin"&& int.TryParse(v, out n)) { _prodGateOff = n <= 0; _prodGraceMin = Math.Max(1, n); }
                    else if (k=="windowguardhours"    && int.TryParse(v, out n)) _windowGuardHours = Math.Max(0, n);
                    else if (k=="seektolmin"          && int.TryParse(v, out n)) _seekTolMin = Math.Max(0, n);
                    else if (k=="requirestrategy")    _requireStrategy = !(v=="false" || v=="0" || v=="no");
                    else if (k=="seekpausefirst")     _seekPauseFirst  = !(v=="false" || v=="0" || v=="no");
                    else if (k=="transportpreflight") _transportPreflight = !(v=="false" || v=="0" || v=="no");
                    else if (k=="transportsettlems"  && int.TryParse(v, out n)) _transportSettleMs = Math.Max(0, n);
                    else if (k=="seekmode")           _seekEnabled     = !(v=="none" || v=="off");
                    else if (k=="autorecover")        _autoRecover = (v=="true" || v=="1" || v=="yes");
                    else if (k=="recoverreseekfirst") _recoverReseekFirst = (v=="true" || v=="1" || v=="yes");
                    else if (k=="maxrecoveries"       && int.TryParse(v, out n)) _maxRecoveries = Math.Max(0, Math.Min(10, n));
                    else if (k=="recovercooldownsec"  && int.TryParse(v, out n)) _recoverCooldownSec = Math.Max(60, n);
                    else if (k=="restarttaskname")    _restartTaskName = v;
                    else if (k=="autostartdelaysec"   && int.TryParse(v, out n)) _autostartDelaySec = Math.Max(5, n);
                }
                if (_maxSpeedOverride > 0 && _maxSpeedOverride != _maxSpeed)
                {
                    _maxSpeed = _maxSpeedOverride;
                    if (verbose) AddLog("maxSpeed override → " + _maxSpeed + "× (operator, from Run.conf)", Amber);
                }

                // PASS 2 — jobs.
                for (int i = 0; i < lines.Length; i++)
                {
                    string t = lines[i].Trim();
                    if (t.Length == 0 || t[0] == '#' || t.IndexOf('|') < 0) continue;

                    string[] f = t.Split('|');
                    if (f.Length < 4) { if (verbose) AddLog("line "+(i+1)+": need at least instrument|from|to|speed", Amber); continue; }
                    DateTime from, to; int spd;
                    if (!DateTime.TryParseExact(f[1].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out from)
                     || !DateTime.TryParseExact(f[2].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out to))
                    { if (verbose) AddLog("line "+(i+1)+": bad date (want yyyy-MM-dd)", Amber); continue; }
                    if (!int.TryParse(StripComment(f[3]).Trim(), out spd) || spd <= 0)
                    { if (verbose) AddLog("line "+(i+1)+": speed must be > 0 (0 is pause, not a job)", Amber); continue; }
                    if (to < from) { if (verbose) AddLog("line "+(i+1)+": 'to' precedes 'from'", Amber); continue; }

                    _jobs.Add(new Job {
                        Instrument = f[0].Trim(),
                        From = from, To = to,
                        Speed = Math.Min(spd, _maxSpeed),
                        Lane = f.Length > 4 ? f[4].Trim() : "",
                        Note = f.Length > 5 ? StripComment(f[5]).Trim() : "",
                        LineNo = i+1
                    });
                }
                if (verbose || _jobs.Count > 0)
                {
                    AddLog("— manifest: " + _jobs.Count + " job(s) · session boundary " + _sessionStartHourEst + ":00 ET · stall " + _stallSec + "s"
                        + (_autostart ? " · ⚡AUTOSTART (delay " + _autostartDelaySec + "s)" : ""), Accent);
                    foreach (Job j in _jobs) AddLog("   " + j + (j.Lane.Length>0 ? "  lane " + j.Lane : "") + (j.Note.Length>0 ? "  — " + j.Note : ""), Muted);
                }
            }
            catch (Exception ex) { AddLog("✗ Run.conf parse failed: " + ex.Message, Red); }
            Progress();
        }
        private static string StripComment(string s){ int h = s.IndexOf('#'); return h >= 0 ? s.Substring(0, h) : s; }

        // ═══════════════════════ QUEUE ═══════════════════════
        private void StartQueue()
        {
            if (!_ready || _running) return;
            LoadManifest(true);
            if (_jobs.Count == 0) { AddLog("✗ nothing queued — add job lines to Run.conf", Red); return; }
            if (!PlaybackConnected())
            {
                AddLog("✗ playback NOT connected — connect the Playback connection first (auto-connect is v0.2.0)", Red);
                SetStatus(Red, "playback not connected");
                return;
            }

            // ── v0.2.0c PRE-FLIGHT — say NO at the click, not four minutes in ────────────────────────────
            string armWhy = null;
            if (_requireStrategy && !StrategyArmed(out armWhy))
            {
                AddLog("✗ PRE-FLIGHT: no strategy is armed — refusing to start", Red);
                AddLog("   " + armWhy, Red);
                AddLog("   a replay with no strategy produces nothing and only LOOKS like a bake.", Muted);
                AddLog("   (override with `requireStrategy = false` in Run.conf if this is deliberate)", Muted);
                SentinelCore.Log(LogTag, "PRE-FLIGHT REFUSED: no armed strategy — " + armWhy);
                SetStatus(Red, "pre-flight: no strategy armed");
                return;
            }
            if (_requireStrategy)
                AddLog("✓ pre-flight: strategy armed — " + armWhy, Muted);

            // ── v0.2.0f TRANSPORT PRE-FLIGHT — the input nobody was verifying (see header) ───────────────
            string transWhy = null;
            if (_transportPreflight && !TransportReady(out transWhy))
            {
                AddLog("✗ PRE-FLIGHT: transport is not parked — refusing to start", Red);
                AddLog("   " + transWhy, Red);
                AddLog("   Reset() has only ever retargeted a PARKED transport; on a moving one it no-ops", Muted);
                AddLog("   and the job aborts. Stop playback, rewind to the range start, then RUN.", Muted);
                AddLog("   (override with `transportPreflight = false` in Run.conf if this is deliberate)", Muted);
                SentinelCore.Log(LogTag, "PRE-FLIGHT REFUSED: transport not parked — " + transWhy);
                SetStatus(Red, "pre-flight: transport moving");
                return;
            }
            if (_transportPreflight && transWhy != null)
                AddLog("✓ pre-flight: transport parked — " + transWhy, Muted);

            _running=true; _ji=-1; _jobsDone=0; _sessionsDone=0; _stalls=0;
            _queueProdStamp = ProductionStamp();   // v0.2.0b — baseline for the queue-level production assertion
            _runBtn.IsEnabled=false; _stopBtn.IsEnabled=true;
            AddLog("── queue START · " + _jobs.Count + " job(s)", Accent);
            SentinelCore.Log(LogTag, "QUEUE start " + _jobs.Count + " jobs");
            NextJob();
        }

        private void NextJob()
        {
            _ji++;
            if (!_running || _ji >= _jobs.Count) { FinishQueue(_ji >= _jobs.Count ? "complete" : "stopped"); return; }

            _cur = _jobs[_ji];
            _jobStartedUtc = DateTime.UtcNow;
            _lastSessionStamp = DateTime.MinValue;
            _stalled = false;

            // v0.2.0 — per-job baselines for the productivity gate and the window guard. Taken BEFORE the seek
            // so the gate measures what THIS job produced, not what the previous one left behind.
            _prodStampAtStart = ProductionStamp();
            _prodClockAtStart = DateTime.MinValue;   // set on the first post-seek clock read (see ResolveSeek)
            _prodProven       = false;
            _windowTripped    = false;


            // v0.1.2 — MAKE `lane=` REAL. Until now this field was decorative provenance: it was
            // copied into checkpoint rows and the log, but nothing applied it, so the chart's F6
            // ScopeLane silently decided the corpus. Publish it so the Council resolves it on its
            // next load. Keyed by MASTER INSTRUMENT so every chart of this instrument (TBars +
            // Flux + Drift) lanes together, which is exactly what a multi-bartype bake wants.
            if (!string.IsNullOrEmpty(_cur.Lane))
            {
                string mi = MasterOf(_cur.Instrument);
                if (!string.IsNullOrEmpty(mi))
                {
                    bool wrote = SentinelCore.LaneAssign.Set(mi, _cur.Lane, "Conductor job " + (_ji + 1) + " (line " + _cur.LineNo + ")");
                    AddLog(wrote ? ("lane '" + _cur.Lane + "' published for " + mi + " (Lanes.conf)")
                                 : "⚠ could not write Lanes.conf — the chart's F6 ScopeLane still decides", wrote ? Muted : Amber);
                }
            }

            // v0.1.2 — FAIL-CLOSED LANE GUARD. Charts already loaded do NOT re-read Lanes.conf, so
            // publishing above is not enough on its own. Compare what the Councils are ACTUALLY
            // publishing against what this job asked for, and refuse on positive disagreement.
            string laneTrip = LaneGuardTrip(_cur);
            if (laneTrip != null)
            {
                AddLog("✗ " + laneTrip, Red);
                SentinelCore.Log(LogTag, "LANE GUARD BLOCKED job " + (_ji + 1) + " — " + laneTrip);
                StopQueue("lane guard");
                return;
            }


            // RESUME: skip sessions this job already checkpointed in a previous run
            DateTime resumeFrom = ResumePoint(_cur);
            DateTime target = resumeFrom.Date.AddHours(_sessionStartHourEst);
            if (resumeFrom > _cur.From)
            {
                AddLog("↻ resuming " + _cur.Instrument + " at " + resumeFrom.ToString("yyyy-MM-dd") + " (checkpointed sessions skipped)", Amber);
                SentinelCore.Log(LogTag, "RESUME " + _cur.Instrument + " lane=" + _cur.Lane + " from " + resumeFrom.ToString("yyyy-MM-dd") + " (job window " + _cur.From.ToString("yyyy-MM-dd") + "→" + _cur.To.ToString("yyyy-MM-dd") + ", checkpointed sessions skipped)");
            }

            LogReplayCoverage(_cur.Instrument, "job " + (_ji+1) + " start");   // v0.2.0d
            AddLog("▶ job " + (_ji+1) + "/" + _jobs.Count + "  " + _cur, Accent);
            SentinelCore.Log(LogTag, "JOB " + (_ji+1) + "/" + _jobs.Count + " " + _cur + " lane=" + _cur.Lane + " note=" + _cur.Note);
            WriteRunLog("job-start", _cur, DateTime.MinValue, null);

            // Resume speed = the job's speed, applied by the seek's post-Reset re-assert (no race with NT's re-max).
            if (!_seekEnabled)
            {
                // v0.2.0e — seekMode=none. Start from wherever the clock is; the WINDOW GUARD is what makes
                // this safe, and it is already continuous. Position by loading a Playback range that STARTS
                // at the intended start: deterministic, and identical on every box without hand-positioning.
                DateTime now0 = ClockEst();
                AddLog("seekMode=none — starting from the current clock " + Iso(now0)
                       + " (target was " + Iso(target) + ")", Amber);
                SentinelCore.Log(LogTag, "SEEK SKIPPED (seekMode=none) — starting at " + Iso(now0)
                    + " target was " + Iso(target) + " — window guard is the safety net");
                _intendedSpeed = Math.Max(0, Math.Min(_maxSpeed, _cur.Speed));
                try { _piSpeed.SetValue(null, _intendedSpeed); } catch (Exception _sx) { SentinelCore.Swallow("SentinelConductor.NextJob", _sx); }
                _lastClock = now0; _lastClockMoveUtc = DateTime.UtcNow;
                _prodClockAtStart = now0;
                return;
            }
            Seek(target, "job " + (_ji+1) + " start", _cur.Speed);
        }

        private void FinishJob(string why)
        {
            if (_cur == null) return;
            double mins = (DateTime.UtcNow - _jobStartedUtc).TotalMinutes;
            AddLog("■ job " + (_ji+1) + " " + why + " · " + _sessionsDone + " session(s) · " + mins.ToString("0.0") + " min", why=="done"?Green:Amber);
            SentinelCore.Log(LogTag, "JOB " + (_ji+1) + " " + why + " sessions=" + _sessionsDone + " mins=" + mins.ToString("0.0"));
            WriteRunLog("job-" + why, _cur, DateTime.MinValue, null);
            _jobsDone++;
            _cur = null;
            NextJob();
        }

        private void StopQueue(string why)
        {
            if (!_running) return;
            _running = false;
            SetSpeed(0);
            FinishQueue("stopped (" + why + ")");
        }

        private void FinishQueue(string why)
        {
            _running=false; _cur=null;

            // ── v0.2.0c · A FINISHED QUEUE MUST PAUSE THE REPLAY (live, 2026-08-02) ──────────────────────
            //    `StopQueue` paused; `FinishQueue` — the NORMAL COMPLETION path — did not. So a queue that
            //    completed cleanly left Playback rolling with the strategy still armed, and the strategy went
            //    on trading PAST the job window straight into the corpus. Measured on legacy-node's Gate 3 run:
            //    job done at 13:33 with 113 in-window files, then 22 more stamped 2026-04-24 — outside the
            //    04/21–04/23 window the rows would have been compared against.
            //    ⚠ The failure is silent and it CONTAMINATES: the extra rows look exactly like real ones and
            //    land in the same directory. A run that keeps producing after it finished is worse than one
            //    that stops early, because early stopping is visible and this is not.
            SetSpeed(0);
            if (_runBtn!=null) _runBtn.IsEnabled=_ready;
            if (_stopBtn!=null) _stopBtn.IsEnabled=false;
            string s = "queue " + why + " · " + _jobsDone + "/" + _jobs.Count + " jobs · " + _sessionsDone + " sessions · " + _stalls + " stall(s)";

            // ── v0.2.0b · THE QUEUE-LEVEL PRODUCTION ASSERTION (found by driving it, 2026-08-02) ─────────
            //    The per-job productivity gate only evaluates INSIDE a job that runs `productivityGraceMin`
            //    of replay clock. On the first live test all five jobs ended in seconds (no loaded data for
            //    their windows), so the gate never ran once — and the queue reported
            //        "QUEUE COMPLETE · 5/5 JOBS · 0 SESSIONS · 0 STALL(S)"
            //    A success-shaped nothing, which is precisely the failure this version exists to abolish.
            //    A queue that checkpointed no session AND advanced no corpus did not complete: it failed,
            //    and it must say so in the same breath it says "complete".
            bool produced = ProductionStamp() > _queueProdStamp;
            if (_sessionsDone == 0 && !produced)
            {
                AddLog("✗ QUEUE PRODUCED NOTHING — 0 sessions checkpointed, 0 corpus written", Red);
                AddLog("   '" + why + "' describes how the queue ENDED, not that it worked. Likely causes:", Red);
                AddLog("   no replay data for these windows · no strategy on the chart · no recorder.", Red);
                SentinelCore.Log(LogTag, "QUEUE PRODUCED NOTHING — " + s
                    + " — 0 sessions and no corpus advance. Treat as FAILED regardless of the '" + why + "' label.");
                WriteRunLog("queue-barren", null, DateTime.MinValue, s);
                SetStatus(Red, "queue produced nothing");
            }
            else
            {
                SetStatus(_sessionsDone > 0 ? Green : Amber, "queue " + why);
            }

            AddLog("── " + s, Accent);
            SentinelCore.Log(LogTag, s.ToUpperInvariant());
            Progress();
        }

        // ═══════════════════════ THE TICK — readout · session checkpoint · stall · job completion ═══════════════════════
        private void OnTick()
        {
            if (!_ready) return;

            // ── AUTORUN — lights-out queue start once playback is connected + settled (opt-in, one-shot). ──
            if (_autostart && !_autostarted && !_running && _jobs.Count > 0)
            {
                bool settled = _openedUtc != DateTime.MinValue && (DateTime.UtcNow - _openedUtc).TotalSeconds >= _autostartDelaySec;
                if (settled && PlaybackConnected())
                {
                    _autostarted = true;

                    // ── v0.2.0 THE ARMING GATE — the ONLY thing standing between an NT restart and a bake ──
                    //    Two cases wear the same "autostart" costume and must not be treated alike.
                    string resumeWhy, armWhy;
                    bool resume = IsResumeOfInflightBake(out resumeWhy);
                    if (resume)
                    {
                        _armVerdict = "RESUME — " + resumeWhy;
                        AddLog("▶ AUTOSTART · RESUME — " + resumeWhy, Accent);
                        SentinelCore.Log(LogTag, "AUTOSTART RESUME — " + resumeWhy + " · manifest " + ManifestFingerprint());
                    }
                    else if (!_requireArm)
                    {
                        _armVerdict = "UNGATED — requireArm=false in Run.conf";
                        AddLog("▶ AUTOSTART · ⚠ UNGATED (requireArm=false) — a cold start with no arming token", Amber);
                        SentinelCore.Log(LogTag, "AUTOSTART COLD START, UNGATED (requireArm=false) — " + resumeWhy);
                    }
                    else if (ArmTokenValid(out armWhy))
                    {
                        _armVerdict = "ARMED — " + armWhy;
                        AddLog("▶ AUTOSTART · ARMED — " + armWhy, Accent);
                        SentinelCore.Log(LogTag, "AUTOSTART COLD START AUTHORISED — " + armWhy + " · manifest " + ManifestFingerprint());
                        ConsumeArmToken();   // single use: this must not authorise the NEXT restart too
                    }
                    else
                    {
                        // The 2026-08-02 false bake ends here, at second one instead of minute 144.
                        _armVerdict = "REFUSED — " + armWhy;
                        AddLog("✗ AUTOSTART REFUSED — cold start with no valid arming token", Red);
                        AddLog("   " + armWhy, Red);
                        AddLog("   " + resumeWhy, Muted);
                        AddLog("   this is NOT a fault: autostart=true persisted, but nothing authorised THIS run.", Muted);
                        AddLog("   to run: write " + ArmTokenPath() + " (armedUtc/ttlHours/manifest), or click RUN.", Muted);
                        SetStatus(Amber, "autostart refused — not armed");
                        SentinelCore.Log(LogTag, "AUTOSTART REFUSED (cold start, unarmed) — " + armWhy + " · " + resumeWhy
                            + " · manifest " + ManifestFingerprint() + " · jobs=" + _jobs.Count
                            + " — nothing was replayed. Arm deliberately or press RUN.");
                        return;   // ⚠ _autostarted stays true: one decision per window, never a retry loop
                    }
                    StartQueue();
                }
                else if (settled && !_autostartWaitLogged)
                {
                    _autostartWaitLogged = true;
                    AddLog("… autostart armed — waiting for the Playback connection", Amber);
                    SentinelCore.Log(LogTag, "AUTOSTART armed, waiting for playback connection");
                }
            }

            DateTime clk = ClockEst();
            int spd = Speed();
            bool conn = PlaybackConnected();

            // ── SEEK WATCHDOG — a seek into a time with no loaded data never fires Reset's callback, so _seeking
            //    would hang forever. If the callback hasn't landed in time, evaluate the landing ourselves. ──
            if (_seeking && !_seekResolved && _seekStartedUtc != DateTime.MinValue
                && (DateTime.UtcNow - _seekStartedUtc).TotalSeconds > SeekWatchdogSec)
            {
                SentinelCore.Log(LogTag, "SEEK WATCHDOG — no callback in " + SeekWatchdogSec + "s, self-evaluating (" + _seekWhy + ")");
                ResolveSeek("watchdog", "none");
            }

            // ── STANDING SPEED INVARIANT (BIDIRECTIONAL) — during a running job the Conductor OWNS the speed and
            //    holds it AT _intendedSpeed, both directions:
            //      • clamp DOWN — NT re-maxes PlaybackSpeed to MaxSpeedValue after Reset (on a delay).
            //      • push  UP  — after a seek / play-state change NT leaves replay at 0 or 1×, and a running job
            //        must actually RUN at its speed (this is why a job used to sit frozen until MAX was clicked).
            //    Not while _seeking (we deliberately pause to reposition). Manual buttons update _intendedSpeed,
            //    so PAUSE/10×/100×/MAX still work — the hold enforces whatever the operator last asked for. ──
            if (conn && spd > _maxSpeed)
            {
                try { _piSpeed.SetValue(null, _intendedSpeed); } catch (Exception _sx) { SentinelCore.Swallow("SentinelConductor.OnTick", _sx); }
                if (!_speedClampWarned)
                {
                    _speedClampWarned = true;
                    AddLog("speed re-clamped — NT set it to " + spd + "× (held at " + _intendedSpeed + "×)", Amber);
                    SentinelCore.Log(LogTag, "SPEED CLAMP: NT set " + spd + " > max " + _maxSpeed + " → held " + _intendedSpeed);
                }
                spd = _intendedSpeed;
            }
            else if (conn && _running && !_seeking && spd != _intendedSpeed)
            {
                // running job, NT drifted the speed off target (typically down to 0/1×) → hold it up.
                try { _piSpeed.SetValue(null, _intendedSpeed); } catch (Exception _sx) { SentinelCore.Swallow("SentinelConductor.OnTick", _sx); }
                if (!_speedHoldWarned)
                {
                    _speedHoldWarned = true;
                    SentinelCore.Log(LogTag, "SPEED HOLD: NT left speed=" + spd + " during run → held " + _intendedSpeed);
                }
                spd = _intendedSpeed;
            }
            else if (conn && spd == _intendedSpeed) { _speedClampWarned = false; _speedHoldWarned = false; }   // on target → re-arm latches

            // ── readout ──
            if (_clockTb != null)
            {
                _clockTb.Text = "clock  " + (clk == DateTime.MinValue ? "—" : Iso(clk)) + (conn ? "" : "   [not connected]");
                _clockTb.Foreground = conn ? Text : Muted;
            }
            if (_speedTb != null)
            {
                // ASKED vs DELIVERED, side by side. `eff` is measured; if it lags the request badly the
                // colour says so, because a bake running 100x too slow otherwise looks perfectly healthy.
                string effTxt = "";
                bool   effBad = false;
                if (_effSpeed >= 0)
                {
                    effTxt = "  ·  eff " + (_effSpeed < 10 ? _effSpeed.ToString("0.00") : _effSpeed.ToString("0")) + "×";
                    if (_running && spd > 0) effBad = _effSpeed < spd * 0.5;   // delivering under half of intent
                }
                _speedTb.Text = "speed  " + (spd < 0 ? "—" : (spd == 0 ? "PAUSED" : spd + "×")) + effTxt
                    + "   (max " + _maxSpeed + "×"
                    + (_maxSpeedReal ? "" : (_maxSpeedOverride > 0 ? " conf" : " safe; NT declares no cap")) + ")";
                _speedTb.Foreground = spd == 0 ? Amber : (effBad ? Red : Ink2);
            }
            if (_jobTb != null)
                _jobTb.Text = "job    " + (_cur == null ? (_running ? "…" : "idle") : (_ji+1) + "/" + _jobs.Count + "  " + _cur.Instrument + "  sess " + _sessionsDone);

            // ── measure EFFECTIVE speed (see the field block) — outcome, not intent ──
            if (conn && clk != DateTime.MinValue && !_seeking)
            {
                if (_effAnchorUtc == DateTime.MinValue) { _effAnchorUtc = DateTime.UtcNow; _effAnchorClk = clk; }
                else
                {
                    double wallSec = (DateTime.UtcNow - _effAnchorUtc).TotalSeconds;
                    if (wallSec >= EffWindowSec)
                    {
                        double repSec = (clk - _effAnchorClk).TotalSeconds;
                        _effSpeed = repSec > 0 ? repSec / wallSec : 0.0;   // backwards/no movement reads 0, never negative
                        _effAnchorUtc = DateTime.UtcNow; _effAnchorClk = clk;
                    }
                }
            }
            else { _effAnchorUtc = DateTime.MinValue; _effSpeed = -1.0; }   // a seek jumps the clock — re-anchor, never divide across it

            if (!conn || clk == DateTime.MinValue) { _lastClock = DateTime.MinValue; return; }

            // ── clock movement / stall detection ──
            if (clk != _lastClock)
            {
                // session boundary crossed? (checkpoint at the same boundary the Recorder flushes on)
                // ⚠ LIVE FINDING (2026-07-20): count boundaries ONLY on real replay advance, never while _seeking —
                //    a Reset JUMP across a boundary was being stamped as a baked session (false Dec-09/Dec-10 rows),
                //    which then MISDIRECTED resume (a fake checkpoint → job 2 skipped ahead → the chart-gap seek).
                //    ResolveSeek sets _lastClock = landed, so the jump itself is never re-counted after the seek.
                if (_running && _cur != null && _lastClock != DateTime.MinValue && !_seeking)
                    CheckSessionBoundary(_lastClock, clk);

                _lastClock = clk;
                _lastClockMoveUtc = DateTime.UtcNow;
                if (_stalled)
                {
                    _stalled = false; _reseekTried = false;   // episode over — rung 1 re-arms for the next one
                    AddLog("✓ clock moving again", Green);
                }
            }
            else if (_running && spd > 0 && !_seeking && _lastClockMoveUtc != DateTime.MinValue)
            {
                double frozen = (DateTime.UtcNow - _lastClockMoveUtc).TotalSeconds;
                if (!_stalled && frozen > _stallSec)
                {
                    _stalled = true; _stalls++;
                    AddLog("⚠ STALLED · clock frozen at " + Iso(clk) + " for " + frozen.ToString("0") + "s", Red);
                    SentinelCore.Log(LogTag, "STALLED clock=" + Iso(clk) + " frozenSec=" + frozen.ToString("0") + " job=" + (_cur!=null?_cur.ToString():"-"));
                    SetStatus(Red, "STALLED — clock frozen " + frozen.ToString("0") + "s");
                    WriteRunLog("stall", _cur, clk, "clock frozen " + frozen.ToString("0") + "s");
                    // v0.1.0 DETECTS; v0.2.0 auto-restarts from the last checkpoint.

                    // ⚠ END OF DATA IS NOT A HANG (found live 2026-07-21, legacy-node). The completion test below
                    // needs the clock to cross PAST To.Date — which can NEVER happen for a job whose `to` IS the
                    // last day of loaded replay data: the clock stops at 23:59:59 of that day, the job hangs, and
                    // the whole queue hangs behind it. That is the most natural manifest anyone writes ("bake
                    // everything I have"), so it is a liveness bug, not an exotic edge. A frozen clock while we
                    // are ALREADY on/past the job's final day means the data ran out → finish the job instead of
                    // waiting for a boundary that cannot arrive. A stall BEFORE the final day is still a real
                    // stall and still only reported (v0.2.0 restarts it from the checkpoint).
                    // Trade-off, deliberate: a genuine hang ON the final day now ends the job early rather than
                    // hanging forever. That is the safe direction — the session is checkpointed, so a re-run
                    // resumes and re-bakes it, whereas a permanent hang bakes nothing and reports nothing.
                    if (_cur != null && clk.Date >= _cur.To.Date)
                    {
                        AddLog("■ end of data at " + Iso(clk) + " — job's final day, finishing", Amber);
                        FinishJob("done-endofdata");
                        return;
                    }

                    // A REAL stall, before the job's final day. Detect-and-leave was the 07-25 lesson.
                    if (_autoRecover) TryRecover(clk, frozen);
                }
            }

            // ── v0.2.0 JOB WINDOW GUARD — the clock must be INSIDE the window it claims to be baking ──
            //    Completion only ever tested `clk.Date > To.Date`. A clock BEFORE the window never trips that,
            //    so job 3/5 (2026-05-17→05-29) ran for over two hours at 2026-04-26…04-29, stamping session
            //    rows carrying the 05-17 window. Guard BOTH sides, continuously — not just at seek time, so a
            //    seek that silently lands elsewhere is caught by the same net.
            if (_running && _cur != null && !_seeking && !_windowTripped && _windowGuardHours > 0)
            {
                DateTime lo = _cur.From.Date.AddHours(-_windowGuardHours);
                DateTime hi = _cur.To.Date.AddDays(1).AddHours(_windowGuardHours);
                if (clk < lo || clk > hi)
                {
                    _windowTripped = true;
                    double offH = clk < lo ? (lo - clk).TotalHours : (clk - hi).TotalHours;
                    AddLog("✗ WINDOW GUARD — clock " + Iso(clk) + " is outside job " + (_ji+1) + " ("
                           + _cur.From.ToString("yyyy-MM-dd") + "→" + _cur.To.ToString("yyyy-MM-dd") + ") by "
                           + (offH/24.0).ToString("0.0") + " days", Red);
                    AddLog("   any row written here would be LABELLED with a window it never visited — aborting", Red);
                    SentinelCore.Log(LogTag, "WINDOW GUARD TRIPPED job " + (_ji+1) + " clk=" + Iso(clk)
                        + " window=" + _cur.From.ToString("yyyy-MM-dd") + "→" + _cur.To.ToString("yyyy-MM-dd")
                        + " offByDays=" + (offH/24.0).ToString("0.0") + " — the seek did not land where the job asked");
                    WriteRunLog("window-guard", _cur, clk, "clock outside job window by " + (offH/24.0).ToString("0.0") + " days");
                    SetSpeed(0);
                    StopQueue("window guard");
                    return;
                }
            }

            // ── v0.2.0 PRODUCTIVITY GATE — a bake that produces nothing must not be able to do it quietly ──
            //    Judged on REPLAY-clock advance, not wall time: a legitimately slow node deserves patience, a
            //    node replaying three weeks of market data with nothing to show does not.
            if (_running && _cur != null && !_prodProven && !_prodGateOff && !_seeking
                && _prodClockAtStart != DateTime.MinValue)
            {
                double repMin = (clk - _prodClockAtStart).TotalMinutes;
                if (repMin >= _prodGraceMin)
                {
                    DateTime now = ProductionStamp();
                    if (now > _prodStampAtStart)
                    {
                        _prodProven = true;   // satisfied for this job — stop stat-ing the corpus dirs
                        AddLog("✓ producing — corpus advanced within " + repMin.ToString("0") + " replay-min", Green);
                        SentinelCore.Log(LogTag, "PRODUCTIVITY OK job " + (_ji+1) + " after " + repMin.ToString("0") + " replay-min");
                    }
                    else
                    {
                        AddLog("✗ PRODUCTIVITY GATE — " + repMin.ToString("0") + " replay-min with ZERO corpus written", Red);
                        AddLog("   nothing is recording: no strategy enabled, no recorder on the chart, or the", Red);
                        AddLog("   chart is not the cell this manifest describes. Aborting rather than burning CPU.", Red);
                        SentinelCore.Log(LogTag, "PRODUCTIVITY GATE TRIPPED job " + (_ji+1) + " — " + repMin.ToString("0")
                            + " replay-min advanced, corpus mtime unchanged since " + Iso(_prodStampAtStart)
                            + ". A 144-minute silent bake is what this exists to prevent.");
                        WriteRunLog("productivity-gate", _cur, clk, repMin.ToString("0") + " replay-min, no corpus written");
                        SetSpeed(0);
                        StopQueue("productivity gate");
                        return;
                    }
                }
            }

            // ── job completion: replay clock passed the job's 'to' session ──
            if (_running && _cur != null && clk.Date > _cur.To.Date) FinishJob("done");

            // ── heartbeat ──
            if (_running && (_lastHeartbeatUtc == DateTime.MinValue || (DateTime.UtcNow - _lastHeartbeatUtc).TotalSeconds >= _heartbeatSec))
            {
                _lastHeartbeatUtc = DateTime.UtcNow;
                if (_cur != null)
                    SentinelCore.Log(LogTag, "job " + (_ji+1) + "/" + _jobs.Count + " " + _cur.Instrument
                        + " | clk " + Iso(clk) + " | spd " + spd + "x"
                        + " | eff " + (_effSpeed < 0 ? "?" : (_effSpeed < 10 ? _effSpeed.ToString("0.00") : _effSpeed.ToString("0"))) + "x"
                        + " | sess " + _sessionsDone
                        + " | elapsed " + ((DateTime.UtcNow-_jobStartedUtc).TotalMinutes).ToString("0") + "m"
                        + (_stalled ? " | STALLED" : ""));
            }

            if (!_stalled && _running) SetStatus(Green, "running · job " + (_ji+1) + "/" + _jobs.Count);
            Progress();
        }

        // A session boundary = a crossing of SessionStartHourEst. Session != calendar day (GC runs 17:00→16:00 ET),
        // so we checkpoint per TRADING SESSION, exactly like SentinelExcursionRecorder flushes.
        private void CheckSessionBoundary(DateTime prev, DateTime now)
        {
            DateTime b = NextSessionStart(prev);
            while (b <= now)
            {
                if (b != _lastSessionStamp)
                {
                    _lastSessionStamp = b;
                    _sessionsDone++;
                    AddLog("✓ session checkpoint " + b.ToString("yyyy-MM-dd HH:mm") + " ET", Green);
                    WriteRunLog("session", _cur, b, null);
                }
                b = NextSessionStart(b);
            }
        }

        // ═══════════════════════ CHECKPOINT / RESUME / PROVENANCE ═══════════════════════
        private void WriteRunLog(string kind, Job j, DateTime sessionEst, string detail)
        {
            try
            {
                Directory.CreateDirectory(ConfDir());
                var sb = new StringBuilder(320);
                sb.Append('{')
                  .Append("\"kind\":").Append(Js(kind))
                  .Append(",\"instrument\":").Append(Js(j!=null?j.Instrument:null))
                  .Append(",\"lane\":").Append(Js(j!=null?j.Lane:null))
                  .Append(",\"speed\":").Append(j!=null?j.Speed:0)
                  .Append(",\"jobFrom\":").Append(Js(j!=null?j.From.ToString("yyyy-MM-dd"):null))
                  .Append(",\"jobTo\":").Append(Js(j!=null?j.To.ToString("yyyy-MM-dd"):null))
                  .Append(",\"sessionEst\":").Append(Js(sessionEst==DateTime.MinValue?null:sessionEst.ToString("yyyy-MM-ddTHH:mm:ss")))
                  .Append(",\"clockEst\":").Append(Js(Iso(ClockEst())))
                  .Append(",\"note\":").Append(Js(j!=null?j.Note:null))
                  .Append(",\"detail\":").Append(Js(detail))
                  .Append(",\"freeGB\":").Append(FreeGB().ToString("0.0", CultureInfo.InvariantCulture))
                  .Append(",\"ntVersion\":").Append(Js(NtVersion()))
                  .Append(",\"conductor\":").Append(Js(Ver))
                  .Append(",\"manifest\":").Append(Js(ManifestFingerprint()))   // v0.2.0 — lets resume tell THIS cell's checkpoints from another's
                  .Append(",\"arm\":").Append(Js(_armVerdict))
                  .Append(",\"wroteUtc\":\"").Append(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")).Append('"')
                  .Append("}\n");
                File.AppendAllText(RunLogPath(), sb.ToString());
            }
            catch (Exception ex) { SentinelCore.Log(LogTag, "run-log write failed: " + ex.Message); }
        }

        // ═══════════════════ v0.2.0 · ARMING, RESUME-vs-COLD-START, PRODUCTIVITY ═══════════════════

        // ── v0.2.0c · PRE-FLIGHT: IS A STRATEGY ACTUALLY ARMED? ─────────────────────────────────────────
        //    Twice on 2026-08-02 a run was started with the strategy disabled — once because toggling
        //    Playback silently disables it, once because a workspace reload brought it back off. Both times
        //    the replay ran for minutes producing nothing, and the productivity gate reported it. That is the
        //    right guard in the wrong place: the condition was knowable AT THE CLICK.
        //    ⚠ Deliberately NOT chart introspection. Enumerating ChartControl.Indicators/Strategies off the
        //    data thread throws, is NT-version-fragile, and would only prove an object EXISTS. Instead read
        //    the one artifact a live strategy already emits — its `armed` line in sentinel.log — the same
        //    cross-assembly evidence idiom ExistingConductor() uses for the heartbeat.
        //    ⚠ Must be written by THIS process: after a restart the previous run's `armed` line is still in
        //    the file and would authorise a strategy that is no longer loaded.
        private bool StrategyArmed(out string why)
        {
            why = null;
            try
            {
                DateTime procStart;
                try { procStart = System.Diagnostics.Process.GetCurrentProcess().StartTime; }
                catch { procStart = DateTime.MinValue; }

                var fi = new FileInfo(SentinelCore.LogFile);
                if (!fi.Exists) { why = "sentinel.log not found — cannot verify a strategy is armed"; return false; }
                string tail;
                using (var fs = new FileStream(fi.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    long take = Math.Min(fs.Length, 256 * 1024);
                    fs.Seek(-take, SeekOrigin.End);
                    using (var sr = new StreamReader(fs)) tail = sr.ReadToEnd();
                }
                string best = null; DateTime bestT = DateTime.MinValue;
                foreach (string line in tail.Split('\n'))
                {
                    if (line.IndexOf("] armed ", StringComparison.Ordinal) < 0) continue;
                    if (line.Length < 23) continue;
                    DateTime t;
                    if (!DateTime.TryParse(line.Substring(0, 23), CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) continue;
                    if (t <= procStart) continue;                 // a previous process's arm — proves nothing now
                    if (t > bestT) { bestT = t; best = line.Trim(); }
                }
                if (best == null)
                {
                    why = "no Sentinel strategy has logged `armed` since this NinjaTrader started "
                        + "(" + procStart.ToString("HH:mm:ss") + "). Enable the strategy on the chart first — "
                        + "toggling Playback disables it.";
                    return false;
                }
                int i = best.IndexOf("] armed ", StringComparison.Ordinal);
                why = best.Substring(i + 2, Math.Min(120, best.Length - i - 2)).Trim()
                    + "  (" + bestT.ToString("HH:mm:ss") + ")";
                return true;
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelConductor.StrategyArmed", _sx); why = "sentinel.log unreadable"; }
            return false;
        }

        private string ArmTokenPath()  { return Path.Combine(ConfDir(), "armed.token"); }

        // The manifest's identity. A token armed for one cell must not authorise a different one — otherwise
        // arming is "autostart = true" again with extra steps. JOB LINES ONLY: changing `heartbeatSec` must
        // not invalidate an arm, changing what actually runs must.
        // ⚠ Hashes the RAW TRIMMED LINES, not the parsed Job fields. Two reasons, both learned writing the
        // arming tool: (1) `Job.Speed` is stored ALREADY CLAMPED by `maxSpeed`, so hashing it would couple the
        // fingerprint to an unrelated setting; (2) an external tool must reproduce this EXACTLY or every arm is
        // refused and it looks like a Conductor bug — and text is reproducible where a parse is guesswork.
        // Keep this in lockstep with Lab\conductor_arm.py::fingerprint().
        private string _manifestFp = "-";
        private string ManifestFingerprint() { return _manifestFp; }

        private void ComputeManifestFingerprint(string[] lines)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                string t = lines[i].Trim();
                if (t.Length == 0 || t[0] == '#' || t.IndexOf('|') < 0) continue;   // same filter as PASS 2
                sb.Append(t).Append('\n');
            }
            string s = sb.ToString();
            int h = 17;
            for (int i = 0; i < s.Length; i++) h = unchecked(h * 31 + s[i]);
            _manifestFp = (h & 0x7FFFFFFF).ToString("x8");
        }

        // Is a bake already in flight for THIS manifest? A checkpoint written recently means a real run was
        // interrupted, and resuming it is the crash-recovery case autostart exists for — no permission needed.
        // ⚠ Freshness is judged on the run-log ROW's own wroteUtc, not the file mtime: the file also grows for
        // stalls and job-start rows from unrelated manifests.
        private bool IsResumeOfInflightBake(out string why)
        {
            why = null;
            try
            {
                string p = RunLogPath();
                if (!File.Exists(p)) { why = "no run-log — nothing was ever in flight"; return false; }
                string fp = ManifestFingerprint();
                string[] lines = File.ReadAllLines(p);
                for (int i = lines.Length - 1; i >= 0 && i >= lines.Length - 400; i--)
                {
                    string line = lines[i];
                    if (line.IndexOf("\"kind\":\"session\"", StringComparison.Ordinal) < 0) continue;
                    string w = JsonStr(line, "wroteUtc"); if (w == null) continue;
                    DateTime t;
                    if (!DateTime.TryParse(w, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out t)) continue;
                    double ageH = (DateTime.UtcNow - t).TotalHours;
                    if (ageH > _resumeGraceHours) break;                       // rows are chronological — older only from here
                    string mfp = JsonStr(line, "manifest");
                    // ⚠ FAIL CLOSED (fixed 2026-08-02, caught by driving it). The first cut read
                    //   `if (mfp != null && mfp != fp) continue;` — so a row with NO manifest field matched
                    //   EVERY manifest, and it logged "manifest unverified" while proceeding anyway. On the
                    //   very first live test worker-1 — which has never baked anything — took the RESUME path
                    //   off legacy-node's run-log, which had travelled with the tree carve. A check that cannot
                    //   verify must not pass: that is the entire lesson this version exists to encode, and the
                    //   first implementation of it broke the rule.
                    if (mfp == null) continue;                                 // pre-v0.2.0 row: proves nothing
                    if (mfp != fp) continue;                                   // a different cell's bake
                    why = "checkpoint " + ageH.ToString("0.0") + "h old for this manifest";
                    return true;
                }
                why = "no checkpoint newer than " + _resumeGraceHours + "h for this manifest";
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelConductor.IsResumeOfInflightBake", _sx); why = "run-log unreadable"; }
            return false;
        }

        // A valid token is present, unexpired, and names THIS manifest. Consumed on use.
        private bool ArmTokenValid(out string why)
        {
            why = null;
            try
            {
                string p = ArmTokenPath();
                if (!File.Exists(p)) { why = "no arming token at " + p; return false; }
                string armedBy = null, cell = null, mfp = null;
                DateTime armedUtc = DateTime.MinValue;
                int ttl = _armTtlHours;
                foreach (string raw in File.ReadAllLines(p))
                {
                    string line = StripComment(raw);
                    int eq = line.IndexOf('='); if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim().ToLowerInvariant(), v = line.Substring(eq + 1).Trim();
                    int n;
                    if      (k == "armedutc") DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out armedUtc);
                    else if (k == "ttlhours" && int.TryParse(v, out n)) ttl = Math.Max(1, n);
                    else if (k == "by")       armedBy = v;
                    else if (k == "cell")     cell = v;
                    else if (k == "manifest") mfp = v;
                }
                if (armedUtc == DateTime.MinValue) { why = "token has no valid armedUtc"; return false; }
                double ageH = (DateTime.UtcNow - armedUtc).TotalHours;
                if (ageH > ttl)      { why = "token EXPIRED (" + ageH.ToString("0.0") + "h old, ttl " + ttl + "h)"; return false; }
                if (ageH < -0.25)    { why = "token armedUtc is in the future — refusing (clock skew?)"; return false; }
                string cur = ManifestFingerprint();
                if (!string.IsNullOrEmpty(mfp) && mfp != cur)
                    { why = "token was armed for manifest " + mfp + ", Run.conf is now " + cur + " — the jobs changed since arming"; return false; }
                why = "armed " + ageH.ToString("0.0") + "h ago" + (armedBy != null ? " by " + armedBy : "")
                    + (cell != null ? " for " + cell : "") + (string.IsNullOrEmpty(mfp) ? " (manifest not pinned)" : "");
                return true;
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelConductor.ArmTokenValid", _sx); why = "token unreadable"; }
            return false;
        }

        // SINGLE USE. Consuming is what makes the token an intent rather than a standing permission — an
        // unconsumed token would fire again on the next restart, which is the bug this replaces.
        private void ConsumeArmToken()
        {
            try
            {
                string p = ArmTokenPath();
                if (!File.Exists(p)) return;
                string used = p + ".used-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");
                File.Move(p, used);
                SentinelCore.Log(LogTag, "ARM token consumed → " + Path.GetFileName(used));
            }
            catch (Exception ex) { SentinelCore.Log(LogTag, "⚠ could not consume the arm token (" + ex.Message + ") — DISARMING in memory so it cannot re-fire"); _requireArm = true; _autostart = false; }
        }

        // Newest corpus write, without walking ~99k files: a directory's mtime bumps when a file is created
        // in it, so stat the leaves the Recorder actually writes to. Unknown → DateTime.MinValue (never a
        // false "productive").
        private DateTime ProductionStamp()
        {
            DateTime best = DateTime.MinValue;
            try
            {
                string root = Path.Combine(SentinelCore.SettingsDir, "Excursions");
                string[] leaves = { root,
                                    Path.Combine(root, "council"), Path.Combine(root, "council", "ticks"),
                                    Path.Combine(root, "council", "1.5"), Path.Combine(root, "council", "1.4"),
                                    Path.Combine(root, "candidates"), Path.Combine(root, "candidates", "ticks"),
                                    Path.Combine(root, "candidates", "cand.1"), Path.Combine(root, "ticks") };
                for (int i = 0; i < leaves.Length; i++)
                {
                    if (!Directory.Exists(leaves[i])) continue;
                    DateTime t = Directory.GetLastWriteTimeUtc(leaves[i]);
                    if (t > best) best = t;
                }
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelConductor.ProductionStamp", _sx); }
            return best;
        }

        // Resume = the day AFTER the latest checkpointed session for this instrument+lane inside the job window.
        private DateTime ResumePoint(Job j)
        {
            DateTime best = DateTime.MinValue;
            try
            {
                string p = RunLogPath(); if (!File.Exists(p)) return j.From;
                foreach (string line in File.ReadAllLines(p))
                {
                    if (line.IndexOf("\"kind\":\"session\"", StringComparison.Ordinal) < 0) continue;
                    if (line.IndexOf("\"instrument\":" + Js(j.Instrument), StringComparison.Ordinal) < 0) continue;
                    if (j.Lane.Length > 0 && line.IndexOf("\"lane\":" + Js(j.Lane), StringComparison.Ordinal) < 0) continue;
                    string s = JsonStr(line, "sessionEst"); if (s == null) continue;
                    DateTime d;
                    if (!DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) continue;
                    if (d.Date < j.From.Date || d.Date > j.To.Date) continue;   // a different run's window
                    if (d > best) best = d;
                }
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelConductor.ResumePoint", _sx); }
            // ⚠ A checkpoint records that replay CROSSED this boundary — i.e. the session STARTING here was
            // IN FLIGHT, not finished (that is deferred bug ⑦: a job stamps the boundary it begins on). So
            // resuming a day later does not resume that session, it SKIPS it: an interruption at 09:00 on a
            // weekday silently drops that whole RTH session from the corpus, and the corpus still looks clean.
            // Resume AT the boundary instead and re-bake the session. The two failure modes are not symmetric:
            // duplicate rows are detectable and dedupable on (instrument, bartype, fireTime) — the key the Lab
            // ingester already uses — while a missing session is invisible forever. (⚠ NOT episodeId: measured
            // 2026-07-21, it is a per-run counter, so 0 of 64 re-baked events shared one.) Cost: re-running one job re-bakes its final session. Correct fix (v0.2.0)
            // = stamp the checkpoint on session EXIT, so "done" means done; this is the safe interim.
            return best == DateTime.MinValue ? j.From : best;
        }

        private static string JsonStr(string line, string key)
        {
            string k = "\"" + key + "\":\"";
            int i = line.IndexOf(k, StringComparison.Ordinal); if (i < 0) return null;
            i += k.Length; int e = line.IndexOf('"', i); if (e < 0) return null;
            return line.Substring(i, e - i);
        }

        // ═══════════════════════ helpers ═══════════════════════
        private static string Iso(DateTime d){ return d == DateTime.MinValue ? "—" : d.ToString("yyyy-MM-dd HH:mm:ss"); }
        private static string NtVersion(){ try { return typeof(Connection).Assembly.GetName().Version.ToString(); } catch { return "?"; } }
        private static double FreeGB(){ try { var di = new DriveInfo(Path.GetPathRoot(Globals.UserDataDir)); return di != null ? di.AvailableFreeSpace/1073741824.0 : 0; } catch { return 0; } }
        private static string Js(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder(s.Length + 2); sb.Append('"');
            foreach (char c in s) { if (c=='"'||c=='\\') sb.Append('\\').Append(c); else if (c=='\n') sb.Append("\\n"); else sb.Append(c); }
            return sb.Append('"').ToString();
        }

        private void SetStatus(Brush dot, string t){ if (_dot!=null) _dot.Background=dot; if (_statusTb!=null){ _statusTb.Text=t; _statusTb.Foreground = dot==Red?Red:Ink2; } }
        private void Progress()
        {
            if (_progTb == null) return;
            _progTb.Text = _jobs.Count == 0 ? "no jobs queued"
                : (_running ? "job " + (_ji+1) + "/" + _jobs.Count : _jobs.Count + " job(s) queued")
                  + "  ·  sessions " + _sessionsDone + "  ·  stalls " + _stalls + "  ·  free " + FreeGB().ToString("0") + " GB";
        }
        private void AddLog(string s, Brush color)
        {
            if (_logPanel==null) return;
            _logPanel.Children.Add(new TextBlock { Text=s, Foreground=color, FontSize=11, FontFamily=new FontFamily("Consolas"), TextWrapping=TextWrapping.Wrap, Margin=new Thickness(2,1,2,1) });
            while (_logPanel.Children.Count > 300) _logPanel.Children.RemoveAt(0);
            if (_logScroll!=null) _logScroll.ScrollToEnd();
        }
        private Border Chip(string t){ return new Border { Background=Edge, CornerRadius=new CornerRadius(3), Margin=new Thickness(8,0,0,0), Padding=new Thickness(6,1,6,1), VerticalAlignment=VerticalAlignment.Center, Child=new TextBlock { Text=t, Foreground=Accent, FontSize=11 } }; }
        private Button Btn(string t, Brush bg, bool primary){ return new Button { Content=t, Foreground=primary?Bg:Text, Background=bg, BorderBrush=Edge, BorderThickness=new Thickness(1), Padding=new Thickness(14,5,14,5), Margin=new Thickness(0,0,8,0), FontWeight=primary?FontWeights.Bold:FontWeights.Normal, Cursor=System.Windows.Input.Cursors.Hand }; }
        private Button SmallBtn(string t, Action onClick)
        {
            var b = new Button { Content=t, Foreground=Text, Background=Edge, BorderBrush=Edge, BorderThickness=new Thickness(1), Padding=new Thickness(9,3,9,3), Margin=new Thickness(0,0,6,0), FontSize=11, Cursor=System.Windows.Input.Cursors.Hand };
            b.Click += (s,e) => onClick();
            return b;
        }
    }
}
