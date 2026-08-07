---
layout: sentinel-ref
title: "SentinelConductor_v0_1_0.cs"
blurb: "AddOns / runtime · 0.1.0 · 2203 lines"
---

# SentinelConductor_v0_1_0.cs

> `bin/Custom/AddOns/SentinelConductor_v0_1_0.cs`

| | |
|---|---|
| **Family** | AddOns / runtime |
| **Version** | 0.1.0 |
| **Size** | 2203 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelConductorAddOn` |
| **Namespace** | `NinjaTrader.NinjaScript.AddOns.Sentinel` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
one at https://mozilla.org/MPL/2.0/.

Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
─────────────────────────────────────────────────────────────────────────────
═════════════════════════════════════════════════════════════════════════════
 SentinelConductor — the Sentinel Suite's REPLAY TRANSPORT (NT8)
 File: SentinelConductor_v0_1_0.cs   ·   Version v0.1.0   ·   namespace …AddOns.Sentinel
─────────────────────────────────────────────────────────────────────────────
 WHAT THIS IS
   Spec: Docs/SENTINEL_CONDUCTOR_SPEC.md. A Control Center ▸ Tools window that DRIVES NinjaTrader's own
   Market Replay engine programmatically — connect-state · seek · speed · job queue · checkpoint/resume ·
   telemetry — so a corpus bake is a DECLARATIVE JOB instead of a human sitting on the Connections menu.

   It is a TRANSPORT, NOT A TRADER. It holds no Account reference and calls no order method. That is what
   makes it safe to run unattended on a bake node.

 ⚖ CLEAN-ROOM ORIGIN NOTE (mandatory — spec §2, same discipline as SentinelQuartermaster / WAE v2)
   The IDEA of programmatic playback control was noticed in a third-party AddOn (ReplayWindowSkipper,
   unlicensed → all-rights-reserved → NOT ONE LINE ADOPTED). The API map below was derived INDEPENDENTLY
   by reflecting over NinjaTrader.Core.dll metadata on NT 8.1.7.2 (2026-07-20). A method signature is a
   FACT about the platform, not the reference's IP.

       NinjaTrader.Adapter.PlaybackAdapter   (public type; these members all STATIC)
         Int32    PlaybackSpeed                       get/set   — 0 = pause
         Int32    MaxSpeedValue                       static readonly FIELD (not const → runtime read only)
         DateTime NowEst / NowLocal                   get/set   — the replay clock
         void     Reset(DateTime targetTimeEst, Action<bool> callback)      ← the PROPER seek
         void     GetReplayMinMaxDates(string file, out DateTime, out DateTime)
       NinjaTrader.Cbi.Connection
         Connection PlaybackConnection                STATIC get/set (null when not connected)

 ⚠ WHY REFLECTION AND NOT A DIRECT REFERENCE (deliberate — HARD BUILD RULE #1)
   NinjaTrader compiles every .cs under bin\Custom into ONE assembly, so ONE broken file blocks the WHOLE
   suite. Binding these internal-ish members directly would turn any NT API change into a total compile
   break. Reflection degrades instead to a loud red banner + a sentinel.log dump at RUNTIME, and the rest
   of the suite keeps compiling. Fail loud, never silently idle.

 ⚠ NOT FIXED HERE (spec §7): the wall-clock leak. Driving Playback well does NOT make Core.Globals.Now
   replay-aware, so the news veto and the SentinelTBars BRK freshness gate stay as broken as they are
   today. The Conductor makes baking CONVENIENT; the Tier-2 harness is what makes it FAITHFUL.

 CHANGELOG  (file/class name frozen at _v0_1_0 per the naming law; internal version = this header + `Ver`)
   v0.2.0f (2026-08-02) — TRANSPORT STATE IS AN INPUT, AND NOTHING WAS VERIFYING IT.
           Gate 3 ran on two boxes proven byte-identical in all FOUR verified inputs — code (muster), replay
           .nrd, historical bars, chart+strategy blob — and still diverged. legacy-node seeked to target and ran;
           worker-1's Reset no-opped and the job aborted. Same code, same conf, same NT build.
           THE MEASURED DIFFERENCE, from the two boxes' own logs:
               legacy-node    SEEK job 1 start | 2026-04-20 00:00:00 -> 2026-04-21 17:00:00   (7 s, ON TARGET)
               worker-1  clk 2026-04-21 02:53:52 → landed 02:54:00, target 17:00:00      (57 ms, NO-OP)
           legacy-node's clock was PARKED at the loaded range start. worker-1's was 27 h in and MOVING — its
           run-log records job-start at 04-20 00:00:00 and the seek 0.6 s later at 04-21 02:54, i.e. the
           transport was already playing at the int.MaxValue speed named in its own SPEED CLAMP line and
           ate a replay-day before the clamp bit. ⇒ Reset appears to retarget a PARKED transport and to
           no-op on a RUNNING one — the reverse of what we had assumed.
           ⭐ THE STRUCTURAL LESSON, which is bigger than the bug: every input we had made verifiable is a
             FILE we can hash. Transport state lives only inside a running NT — no file, no hash, no
             read-back — so it was the one input still set by hand, per box, at different moments. We were
             verifying what was easy to verify and the divergence arrived from the rest. The Watch is six
             workers; any input that needs a GUI click per box is GUARANTEED to diverge across a matrix.
           ⇒ PRE-FLIGHT, at the click, alongside the v0.2.0c strategy check (`transportPreflight = false`
             to disable):
             ① CLAMP FIRST, ASK SECOND. Speed is forced to the job's speed BEFORE the queue advances. The
               old order let NT sit at int.MaxValue until the first clamp tick — long enough on worker-1
               to burn 27 replay-hours and put the clock where the seek could not recover it.
             ② REFUSE A MOVING TRANSPORT. The clock is sampled twice `transportSettleMs` apart; if it
               advanced, the run is refused with the reason, because Reset cannot be trusted to retarget it.
             ③ MEASURE POSITION, DO NOT REFUSE ON IT. The offset from the loaded range start is logged at
               every queue start — but only as evidence. With n=1 each way we cannot yet separate "was
               moving" from "was not at range start", and a guard that blocks on an unproven discriminator
               would break every legitimate resume. Record it until the data says which one matters.
             ⭐ ② is a refusal and ③ is a measurement ON PURPOSE. Blocking on what we have proven and
               instrumenting what we have not is the difference between a gate and a guess.

   v0.2.0b (2026-08-02) — THREE DEFECTS IN v0.2.0 ITSELF, all found by DRIVING it on worker-1, none by
           review. Recorded because each one is the guard failing in the exact way it was written to prevent:
           ① THE RESUME CHECK FAILED OPEN. It read `if (mfp != null && mfp != fp) continue;` — so a
             pre-v0.2.0 row, which carries no `manifest` field, matched EVERY manifest. It even logged
             "manifest unverified" and proceeded. First live test: worker-1, a box that has NEVER baked
             anything, took the RESUME path. ⇒ a row without a fingerprint now proves nothing. **A check
             that cannot verify must not pass** — the whole point of this version, broken in its own code.
           ② THE CHECKPOINT LEDGER TRAVELLED WITH THE TREE. `Conductor\run-log.jsonl` came to the sentries
             in the carve, so a cold box inherited legacy-node's 45h-old checkpoints. A checkpoint asserts "THIS
             machine baked these sessions"; copying it makes that a lie on arrival. Now excluded by
             muster.py alongside Run.conf and Excursions.
           ③ THE PRODUCTIVITY GATE HAD A HOLE, and it was the worse bug: it only evaluates INSIDE a job
             that runs `productivityGraceMin` of replay clock. On the live test all five jobs ended in
             seconds (no loaded data for their windows) so it never evaluated once, and the queue reported
             "QUEUE COMPLETE · 5/5 JOBS · 0 SESSIONS · 0 STALL(S)" — a success-shaped nothing. ⇒ added the
             QUEUE-LEVEL assertion: 0 sessions AND no corpus advance is a FAILED queue however it ended,
             said in the same breath as "complete".
           ⇒ The meta-lesson, again: every one of these was invisible to reading and obvious to running.

   v0.2.0 (2026-08-02) — THE FALSE BAKE, and the three guards it earned. Found live on legacy-node: NT restarted
           for an unrelated reason, `autostart = true` had sat in Run.conf since 07-30, and the Conductor
           fired a cell nobody asked for — 144 minutes at 100×, 8 sessions "checkpointed", NO strategy
           loaded, ZERO corpus rows, and not one complaint. Three independent defects, three fixes:

           ① ARMING — autostart is now an INTENT, not a standing permission. A persistent boolean cannot
             tell "I armed this and rebooted to start it" from "this has been true for three days". So the
             two cases that both look like autostart are now separated: a RESUME (a checkpoint for THIS
             manifest, newer than `resumeGraceHours`) proceeds automatically — that is the crash-recovery
             case autostart exists for and it must never need permission; a COLD START requires
             `Conductor\armed.token`, which carries an `armedUtc`, a TTL, and the manifest fingerprint it
             authorises, and is CONSUMED on use. ⚠ Clicking RUN is intent by definition and is never gated.
             ⚠ The manifest fingerprint covers the JOB LINES only — editing `heartbeatSec` must not
             invalidate an arm; editing what actually runs must.
           ② PRODUCTIVITY GATE — after `productivityGraceMin` of REPLAY-clock advance with no corpus
             written, abort. Deliberately an OUTPUT assertion, not a pre-flight inspection of charts and
             strategies: enumerating them is fragile and NT-version-bound and would only prove the objects
             exist, whereas measuring the corpus catches every way this fails at once (no strategy,
             strategy off, no recorder, wrong chart, wrong bar type, wrong instrument).
             ⚠ Measured on the LANDED clock after the seek — anchoring before it would satisfy the gate
             instantly, since a seek jumps weeks. ⚠ Never walks the corpus tree (~99k files); a directory's
             own mtime is O(1) and sufficient.
           ③ JOB WINDOW GUARD — the clock must stay INSIDE the window the job claims. Completion only ever
             tested `clk.Date > To.Date`, which a clock BEFORE the window never trips: job 3/5 announced
             2026-05-17→05-29 and ran at 2026-04-26…04-29 for two hours, stamping session rows labelled
             with the 05-17 window. A run that MISLABELS is worse than one that fails, because the failure
             is invisible downstream. Checked continuously, so a seek that silently lands elsewhere is
             caught by the same net.

           THE THROUGH-LINE: the manifest already carried the right rule — "FLIP TO true ONLY once the
           chart is confirmed loaded and the recorder is on it" — but it was written in ENGLISH TO A HUMAN
           instead of in code to the machine. Three of these are the same bug: a condition only a person
           was checking. ⇒ run-log rows now also carry `manifest` and `arm` so a run's authority is
           recoverable after the fact rather than inferred.
   v0.1.0l (2026-07-21) — END-OF-DATA IS NOT A HANG (liveness bug, found by driving job 2 to the end of its
           replay range). Completion tested `clk.Date > To.Date`, which is UNREACHABLE when `to` is the last day
           of loaded data — the clock parks at 23:59:59 of that day and the job (and the queue behind it) hangs
           forever. Since "bake everything I have" is the obvious manifest to write, this was waiting for anyone.
           A stall that occurs while already on/past the job's final day now finishes the job (`done-endofdata`);
           a stall BEFORE the final day is still just reported. ⚠ Consequence accepted: a real hang on the final
           day ends the job early instead of never — the session is checkpointed, so a re-run re-bakes it.
   v0.1.0m (2026-07-21) — INTERLOCK LEG ② REWRITTEN: it never worked. `Application.Current.Windows` does not
           contain NT tool windows (they live on their own dispatchers), so an IDLE Conductor — which emits no
           heartbeat for leg ① to find — was invisible, and every recompile opened another window (three stacked
           on the dev box before it was noticed). Now enumerates `Globals.AllWindows`, NT's cross-dispatcher
           registry. Lesson: leg ① was verified live, leg ② never was, and the unverified half is the half that
           failed. A fallback nobody has SEEN fire is a guess, not a fallback.
   v0.1.0k (2026-07-21) — AUTO-OPEN INTERLOCK. A recompile does NOT close an open Conductor window (the WPF
           window survives the assembly reload), so v0.1.0i auto-opened a SECOND one on top of a running
           transport: two Conductors driving one Playback, interleaved heartbeats, one seeking backwards
           mid-job. Auto-open now probes for a live Conductor first — primarily via the heartbeat already in
           sentinel.log, the one signal that crosses the assembly-reload boundary (statics and Type identity
           both reset), with a window-name match as backstop. Caught by reading the heartbeats, not by review.
   v0.1.0j (2026-07-21) — RESUME NO LONGER SKIPS THE INTERRUPTED SESSION. `ResumePoint` returned
           `lastCheckpoint + 1 day`, but a checkpoint is stamped when replay CROSSES INTO a session (bug ⑦),
           so the session that was in flight was never re-baked — a crash at 09:00 on a weekday silently
           dropped that entire RTH session and the corpus still looked complete. Now resumes AT the boundary
           and re-bakes it. The failure modes are NOT symmetric: a duplicate is dedupable, a missing session is
           invisible forever. Found by watching a live reload resume. ⚠ CORRECTED 2026-07-21 by MEASURING the
           re-baked rows: the dedupe key is **(instrument, bartype, fireTime)** — which is exactly what the Lab
           ingester already uses (`trade_id = row:{inst}:{bartype}:{ft}`) — NOT `episodeId`. episodeId is a
           PER-RUN sequence counter: across a restart the same event gets a different id AND an id gets reused
           for a different event. Measured: 64 re-baked events, 59 byte-identical outcomes, **0** sharing an
           episodeId. See [[episode-id-not-a-cross-run-key]].
   v0.1.0i (2026-07-21) — AUTO-OPEN, the missing leg of lights-out. v0.1.0h could auto-RUN but nothing ever
           auto-OPENED the window, so after a reboot the whole chain died silently at "no Conductor window".
           Now: `autostart=true` in Run.conf ALSO opens the window itself, AutoOpenDelaySec after the Control
           Center appears. ⚠ Deliberately NOT NT workspace persistence (IWorkspacePersistence): a bake node
           that is HARD-KILLED — the exact case self-healing exists for — never saves its workspace, so a
           workspace-restored window would vanish precisely when it is needed. A file on disk cannot.
           One switch, not two: declaring lights-out implies the window. Logs AUTO-OPEN (headlessly verifiable).
   v0.1.0h (2026-07-20) — AUTORUN (lights-out): Run.conf `autostart=true` auto-runs the queue once Playback
           connects+settles. Reflection REJECTED programmatic connect-with-range (ConnectOptions has no clean
           date-range field; Start/End = obfuscated internals) → use NT-native ConnectOnStartup instead. With
           checkpoint/resume = SELF-HEALING baking. ✅ Proven main box AND legacy-node (first unattended corpus bake).
   v0.1.0g — SPEED INVARIANT made BIDIRECTIONAL: during a running job, hold PlaybackSpeed AT the target both
           ways (clamp down over the ceiling AND push up when NT parks replay at 0/1× post-seek). Ended the
           recurring "job sits frozen until MAX is clicked". Manual buttons update the intended speed.
   v0.1.0f — SEEK stamped FALSE checkpoints (a Reset JUMP across a boundary counted as a baked session →
           misdirected resume → chart gap). Count boundaries only on real replay advance (!_seeking).
   v0.1.0e — SEEK WATCHDOG: a seek into a NO-DATA time never fires Reset's callback → _seeking hung forever
           (silent wedge, stall gated on !_seeking). Watchdog self-evaluates; off-target during a job aborts it.
           Also: resume decisions now log to sentinel.log.
   v0.1.0c/d — Reset leaves PlaybackSpeed MAXED (int.MaxValue) on a DELAY. One-shot restore + a timed re-assert
           both lost the race → replaced by the standing invariant (see g).
   v0.1.0b — Reset's Action<bool> callback is NOT success (returned false on a seek that landed EXACTLY on
           target). Judge the seek by the MEASURED landed clock vs target (SeekTolHours), never the flag.
   v0.1.0a — MaxSpeedValue reads int.MaxValue (NT declares NO cap) — a resolved-but-implausible value that
           silently disabled the speed clamp. Band-check 1..5000; else conservative 100× + `maxSpeed` conf
           override. Self-test PASS now LOGS (was green-banner-only → unverifiable headlessly).
   v0.1.0 (2026-07-20) — first cut. Self-test; live transport readout; manual speed + seek (pause → Reset →
           restore); Run.conf job manifest; sequential job queue; per-session checkpoint + resume; heartbeat;
           run-log.jsonl provenance; stall DETECTION. Seek restricted to session boundaries (spec §3.1).
   ⭐ META-LESSON (recurred 3×, worth internalizing): a value that RESOLVES is not a value you can TRUST —
      measure the outcome (landed clock, actual speed, corpus rows), don't believe the flag. Every bug above
      was caught by DRIVING it on live data, none by reasoning.
   ⚠ DEFERRED (v0.2.0): a job stamps its STARTING session boundary (ResolveSeek lands a hair before target,
      replay crosses it → counted). Not a false stamp (replay DID cross) but a session-semantics design Q.
```

