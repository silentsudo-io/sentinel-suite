# SentinelAlertService_v1_0_0.cs

> `bin/Custom/AddOns/SentinelAlertService_v1_0_0.cs`

| | |
|---|---|
| **Family** | AddOns / runtime |
| **Version** | 1.0.0 |
| **Size** | 270 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelAlertService_v1_0_0` |
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
 SentinelAlertService — the audible/push channel for Sentinel alerts (NT8)
 File: SentinelAlertService_v1_0_0.cs
 Version: v1.0.0
─────────────────────────────────────────────────────────────────────────────
 WHAT THIS IS  (Hardening Substrate 3 — the "learn about it when you're NOT staring
   at the screen" half of Alerts; see Docs/SENTINEL_HARDENING_FRAMEWORK.md)
   A headless, always-on AddOnBase singleton that subscribes to SentinelCore.Alerts.Raised
   and turns each alert into something you can NOTICE away from the chart:
     • SOUND — a .wav (SoundPlayer) or a System sound fallback. Critical always; Info opt-in.
     • PUSH  — an optional shell command run on the alert (e.g. curl to Pushover/ntfy/Slack),
               so a phone push is a config line, not hardcoded to any provider. Opt-in, empty=off.
   Over-alerting trains you to ignore alerts, so this mirrors Alerts' two-tier design: Critical
   is rare by construction and gets the loud treatment; Info is quiet/opt-in. A per-level throttle
   stops a burst from machine-gunning the speaker.

 CONFIG  <UserDataDir>\Sentinel\Alerts.conf  (key=value; re-read on NT restart). All optional:
     enabled=true          # master switch for the channel
     playInfo=false        # also play a (soft) sound on Info alerts
     throttleSec=3         # min seconds between sounds of the same level
     critWav=              # path to a .wav for Critical; empty → SystemSounds.Hand
     infoWav=              # path to a .wav for Info;     empty → SystemSounds.Asterisk
     pushCommand=          # shell command run on an alert; {level} {title} {detail} substituted
     pushOnInfo=false      # run pushCommand on Info too (default: Critical only)
   Missing file = sensible defaults (Critical sound ON, Info off, no push).

 SAFETY: never throws into NT. The Raised handler is wrapped; sound + push run on the thread pool
   (never block the alert path); teardown sets a flag first so in-flight callbacks bail. Sound is
   a NOTIFICATION only — it does not act on the account (that's the Gate / auto-flatten's job).

 CHANGELOG
   v1.0.1 — (in-place) LIVE CONFIG API for the dashboard Test tab: GetConfig()/Apply(cfg)/Reload().
            Apply persists to Alerts.conf AND updates the running service with no NT restart. Test-tab
            buttons fire a real Alerts.Info/Critical to exercise the whole path (sound+push+ledger).
   v1.0.0 — initial: subscribe SentinelCore.Alerts.Raised → sound (wav/SystemSounds) + optional push
            shell command, two-tier (Critical loud / Info quiet-opt-in), per-level throttle, Alerts.conf.
```

