# SentinelQuartermaster_v0_2_0.cs

> `bin/Custom/AddOns/SentinelQuartermaster_v0_2_0.cs`

| | |
|---|---|
| **Family** | AddOns / runtime |
| **Version** | 0.2.0 |
| **Size** | 526 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelQuartermasterAddOn` |
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
 SentinelQuartermaster — the Sentinel Suite's raw market-data SUPPLY OFFICER (NT8)
 File: SentinelQuartermaster_v0_2_0.cs   ·   Version v0.2.0   ·   namespace …AddOns.Sentinel
─────────────────────────────────────────────────────────────────────────────
 WHAT THIS IS
   The PROCUREMENT head of Quartermaster (spec: Docs/SENTINEL_QUARTERMASTER_SPEC.md §3). A Control
   Center ▸ Tools window that downloads NT Market Replay data (.nrd). v0.2.0 = THE FLEET: a manifest of
   many instruments, a ROLL ENGINE that auto-enumerates each root's per-expiry contracts + their front
   windows, and a BOUNDED-CONCURRENT worker pool that grinds the whole list unattended with skip-existing
   resume, retry/backoff, a disk-space guard, and per-file provenance for the Python catalog head.

 ⚖ CLEAN-ROOM ORIGIN NOTE (mandatory — spec §8, same discipline as SentinelWAE v2 / LiquidityWalls)
   NinjaTrader exposes NO public API for triggering Market Replay downloads. The METHOD SHAPE is a FACT
   about NT's API, discovered by REFLECTING over NinjaTrader.Core.dll metadata (2026-07-18):
       <adapter/HistoricalDataClient>.RequestMarketReplay(Cbi.Instrument, DateTime dateEst,
           Action<Cbi.ErrorCode,string,object> callback, object state, …)  → output lands at
       Core.Globals.UserDataDir\db\replay\<Instrument.FullName>\yyyyMMdd.nrd
   The concrete overload VARIES by provider, so the invoke is built ADAPTIVELY from the resolved method's
   actual parameters (BuildArgs). Reimplemented from OBSERVED behaviour — not one line of the unlicensed
   reference (greybeard MultiDayDownload, all-rights-reserved) ships here. Suite dep = SentinelCore
   Foundation only (Log + SettingsDir).

 CHANGELOG
   v0.2.0a (2026-07-18) — RETRY CLASSIFICATION (in-place patch). A "no market replay data available" panic is
           a PERMANENT answer, not a transient fault: PROVEN (3 independent ways — fetch-log fingerprint, the
           .nrd disk store, a manual GUI Get-Market-Replay pull) that Tradovate replay is a ~90-day ROLLING
           floor, hard-stop at 2026-04-19. So FailJob no longer re-enqueues a permanent "not available" — it
           was re-asking each dead date ~2× → ~16k wasted round-trips on the first 2023→now fleet run. TRANSPORT
           faults (timeout / invoke-exception) still retry. Permanent misses now log "∅ … not available" (amber),
           not "✗" (red), so the operator reads absence-of-data, not error. (Deep 2023 tick+quote = Databento.)
   v0.2.0 (2026-07-18) — THE FLEET. Manifest (Sentinel\Quartermaster\Fetch.conf: roots + global window +
           concurrency/attempts); ROLL ENGINE (root → per-expiry contracts over the range, front-month
           tiling via 3rd-Friday roll dates; quarterly index/FX/rates · even-month metals · monthly energy);
           BOUNDED-CONCURRENT pool (auto-tunes down on error bursts, up when clean); retry+re-enqueue;
           per-request timeout sweep; DISK-SPACE GUARD (pause at a free-GB floor — never fill the drive);
           most-recent-first ordering (freshest data lands before the floor); aggregate progress + STOP;
           provenance JSONL unchanged. LIVE-PROVEN base = v0.1.0 (frozen): reflection self-test + adaptive
           invoke + provenance, validated pulling real MNQ .nrd on legacy-node.
   v0.1.0 (2026-07-18) — first cut (frozen checkpoint): Tools-menu window; version-guard self-test; single
           instrument + date-range fetch; skip-existing; watchdog; provenance JSONL; Fetch.conf persist.
```

