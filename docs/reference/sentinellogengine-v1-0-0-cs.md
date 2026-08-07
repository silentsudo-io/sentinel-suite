# SentinelLogEngine_v1_0_0.cs

> `bin/Custom/AddOns/SentinelLogEngine_v1_0_0.cs`

| | |
|---|---|
| **Family** | AddOns / runtime |
| **Version** | 1.1 |
| **Size** | 602 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelLogEngine` |
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
 SentinelLogEngine — workspace-wide MAE/MFE trade-excursion logging engine
 File: SentinelLogEngine_v1_0_0.cs
 Engine version: v1.0.0
 Schema: 1.0   (see MAE_Logger_Schema_Spec_v1.0.md — the authoritative contract)
─────────────────────────────────────────────────────────────────────────────
 PURPOSE
   A standalone, strategy-AGNOSTIC engine that records per-trade MAE/MFE excursion
   to JSONL. Extracted from ConfluenceArchitect's embedded recorder (v0.6.1) and
   generalized so ANY strategy — or the future zero-touch Add-On — can feed it.

   The engine knows nothing about indicators, bars, or NinjaScript host state. The
   CALLER feeds it primitives (prices, timestamps, optional opaque context). This is
   what lets one engine serve every strategy and both logging tiers.

 TWO TIERS (one schema — see spec §2):
   • Tier 1 (zero-touch): Add-On feeds price-only excursion from account + market data.
                          Produces core record, no ctx.
   • Tier 2 (rich):       Instrumented strategy additionally supplies ctx + per-bar ext
                          + atr. Produces core record PLUS context.

 BASKET-READY (spec §1, §3.1, §3.5):
   Every record carries account/strategy/instanceId/params identity, and every path
   sample is wall-clock TIMESTAMPED so cross-strategy basket excursion can be computed
   at analysis time (a time-overlap computation, not a sum of per-trade MAEs).

 USAGE (tier-2 strategy, per trade):
   1. OnEntry(account, dir, qty, entryPriceAvg, entryTimeUtc, tick, atrAtEntry, ctx)
   2. OnBar(timeUtc, barOffset, high, low, close, atr, ext)   // each in-trade bar
   3. OnExit(exitPriceAvg, exitTimeUtc, exitReason)            // writes one JSONL line

 USAGE (tier-1 Add-On, per trade): same calls, but ctx/ext null, atr NaN, tier=1.

 THREADING NOTE (spec §11.3): the engine itself does no UI work and is safe to call
   from background data-event threads. Any DASHBOARD reading engine output must marshal
   to the UI thread via Dispatcher.InvokeAsync (NOT the engine's concern, but noted so
   callers don't mistake the engine for UI-thread-bound).

─────────────────────────────────────────────────────────────────────────────
 CHANGELOG
 v1.2.0 / schema 1.1 (2026-07-01) — EYE VERDICT CAPTURE (the profit keystone).
   - OnEntry now snapshots the current SentinelEye verdict for the trade's instrument
     (SentinelCore.GetEyeVerdict, no staleness filter) and freezes it with the trade.
   - Every record gains an eye block: eyeHad, eyeDir, eyeScore, eyeSource, eyeAgeSec,
     and eyeAligned (= did Eye qualify THIS trade's direction). This is what lets Lens
     partition trades into Eye-endorsed vs not and prove whether the Eye filter adds edge.
   - Additive/backward-compatible: schema bumped 1.0 → 1.1; old records simply lack the
     eye fields and analysis treats them as null. No path/ctx/identity changes.
   - Class name + filename intentionally UNCHANGED (SentinelLogEngine is a shared symbol,
     edited in place like SentinelCore — a versioned copy would collide, CS0101).
 sentinel-rebrand (2026-07-01) — MAEEngine → SentinelLogEngine; namespace MAELogging → Sentinel.
            JSONL logs now under <UserDataDir>\Sentinel\Log (was "MAELogger"). Schema UNCHANGED
            (still 1.0; JSON field names like maeTicksRaw are the DOMAIN term MAE — intentionally
            NOT renamed). Consumed by GodTradesStrategy_v1_1_0 + ConfluenceArchitect_v0_7_0 (tier-2).
 v1.1.0 — live-state surface + decoupled service registry (dashboard sees BOTH tiers).
   - Added public Live* getters exposing the in-flight trade (account/strategy/inst/tier/
     dir/entry/running MAE-MFE/last px). Display-only; reads the same values the JSONL uses.
   - Added static hooks OnEngineTradeOpened / OnEngineTradeClosed. The capture service (if
     loaded) subscribes them to union tier-2 strategy trades into its open-position
     registry, so the dashboard shows tier-1 AND tier-2 live trades. No hard dependency:
     if no service is present the hooks are null and the engine behaves exactly as before.
     This keeps SentinelLogEngine both strategy- and service-agnostic.
   - Record format unchanged (schema 1.0).
 v1.0.1 — descriptive filename scheme + auto-derived paramHash.
   - Filenames now: {UTCstamp}__{account}__{strategy}-{ver}__{inst}__t{tier}__p{hash}.jsonl
     UTC ISO-basic timestamp first (lexical sort == chronological); "__" field
     separators (parse-safe even when a field contains a single "_"); strategy version,
     tier marker, and a 6-char param hash all visible at a glance. Solves the A/B case
     (different configs => visibly different names) and the basket case (identity in name).
   - paramHash now AUTO-DERIVED (deterministic FNV-1a) from the params JSON when the
     caller doesn't supply one, so every strategy gets a correct, stable hash for free.
     Same config => same hash (groups re-runs); different config => different hash.
   - Record format UNCHANGED (schema still 1.0); only the filename and the hash-fill
     behavior changed. Existing analysis code is unaffected.

 v1.0.0 — initial extraction from ConfluenceArchitect v0.6.1.
   - Lifted SamplePath / WriteTradeRecord / lifecycle logic verbatim in spirit, made
     strategy-agnostic: caller passes prices+time+opaque bags instead of the engine
     reaching into ConfluenceState / Close[0] / CurrentBar.
   - Schema 1.0 additions over the embedded recorder:
       * identity: account, strategy, stratVer, instanceId, params/paramHash, engineVer, tier
       * path samples: wall-clock "t" timestamp (basket alignment)
       * path samples: "atr" per sample (closes the ATR-replay-fidelity gap)
       * excursion: maeTimeToMs / mfeTimeToMs (cross-bar-type basket alignment)
       * ctx / ext are OPAQUE pass-through bags (engine stays strategy-agnostic)
   - Running MAE/MFE tracked EVERY bar (before stride gate); only path SAMPLES thin.
     (Same correctness guarantee as the embedded recorder.)
   - v1 DEFERS tier-1/tier-2 in-engine merge (spec §4): records written separately,
     reconciled in the Python analysis layer.
```

