---
layout: sentinel-ref
title: "SentinelNewsService_v1_0_0.cs"
blurb: "AddOns / runtime · 1.0.0 · 370 lines"
---

# SentinelNewsService_v1_0_0.cs

> `bin/Custom/AddOns/SentinelNewsService_v1_0_0.cs`

| | |
|---|---|
| **Family** | AddOns / runtime |
| **Version** | 1.0.0 |
| **Size** | 370 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelNewsService_v1_0_0` |
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
 SentinelNewsService — native C# economic-calendar → News.conf feeder (Sentinel Suite, NT8)
 File: SentinelNewsService_v1_0_0.cs   ·   Version v1.0.0   ·   namespace …AddOns.Sentinel
─────────────────────────────────────────────────────────────────────────────
 WHAT  (the event-veto axis, fully AUTOMATED, NO PYTHON — see economic-calendar-event-veto memory)
   A headless AddOn that, on a timer inside NinjaTrader (which is always running), FETCHES the
   high-impact economic calendar itself and WRITES the Sentinel\News.conf managed block that
   SentinelRiskService already reads → SetNewsLockouts → CanEnter + the Council's news veto.
   This REPLACES the external EconomicCalendar.py → sentinel_newsconf.py chain with one native
   service (no Python, no schtasks). It writes the IDENTICAL managed-block format the Python bridge
   wrote, so it is a drop-in — the RiskService reader, the v1.0.9 freshness guard, and the dashboard
   Risk-view news section all consume it unchanged.

 DATA  — ForexFactory weekly JSON (https://nfs.faireconomy.media/ff_calendar_thisweek.json): a flat
   array of {title,country,date(ISO w/ offset),impact,forecast,previous}. We keep only the configured
   currencies (default USD — macro USD hits ES/NQ/GC alike) at/above the min impact, convert each
   event's offset-aware time to MACHINE-LOCAL wall time (matches Core.Globals.Now, which the RiskService
   compares against), and emit `YYYY-MM-DD HH:mm | Event | all | beforeMin | afterMin` lines.

 DELIBERATELY NOT WRITTEN: the directional bias. Only the BLACKOUT WINDOWS are universal; the equity
   bias_score is NOT (hot CPI → hawkish → higher real yields → often BEARISH gold, opposite of equities).
   scope is always "all" (a spike halts every instrument); direction stays out of News.conf (caveat #2).

 SAFETY  — fully fail-SAFE: any fetch/parse error leaves the existing News.conf UNTOUCHED and logs a
   warning; the RiskService freshness guard (v1.0.9) then makes the silent fail-OPEN visible. Network I/O
   runs on the timer threadpool thread with a reentrancy guard; every path is wrapped, nothing throws into
   NT. Manual News.conf lines OUTSIDE the ECONCAL markers are always preserved.

 CONFIG (optional Sentinel\NewsService.conf, key=value; sensible defaults if absent):
   enabled=true  minImpact=HIGH  currencies=USD  beforeMin=5  afterMin=20  refreshMinutes=240  url=<override>

 CHANGELOG
   v1.0.0 (2026-07-08) — initial native feeder. Timer fetch (ForexFactory weekly JSON) → filter (currency +
            min impact) → ET/offset → local → managed-block write into News.conf (byte-compatible with the
            Python bridge's markers/format). Optional NewsService.conf overrides. Fail-safe; no Python.
            LIVE-VALIDATED 2026-07-08: fetched + parsed the real FF payload, wrote "FOMC Meeting Minutes 13:00".
            + MinRefetchMinutes backoff (default 60) — skip the fetch if News.conf was refreshed recently so a
            rapid F5/restart storm can't hammer the feed's CDN into a 429 (observed on repeated recompiles).
            + no-trade WINDOW is dashboard-editable: SaveConfig() persists to NewsService.conf; RewriteFromCache()
            re-emits News.conf from the last fetch with the new before/after INSTANTLY (no network). Props often
            want ~10-15m each side — set it on the Home tab.
```

