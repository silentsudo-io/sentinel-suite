# SentinelExcursions_v1_0.cs

> `bin/Custom/AddOns/SentinelExcursions_v1_0.cs`

| | |
|---|---|
| **Family** | AddOns / runtime |
| **Version** | — |
| **Size** | 408 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `Group` |
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
 SentinelExcursions — signal-excursion analytics for the Sentinel Suite (NT8)
 File: SentinelExcursions_v1_0.cs   ·   Version v1.0
─────────────────────────────────────────────────────────────────────────────
 WHAT  (pairs with Indicators\SignalExcursionRecorder_v1_0.cs; see sentinel-roadmap)
   Reads the RAW signal-excursion records the Recorder writes to <UserDataDir>\Sentinel\
   Excursions\*.jsonl (schema 1.0, "kind":"excursion") and turns them into the actionable
   truth: per (instrument × signal × direction), the DISTRIBUTIONS of max MFE / max MAE and
   the fixed-horizon milestone curves (1/5/15/60 min), as percentiles. That directly answers
   "what's a responsible base-hit TP (an MFE percentile) and stop (an MAE percentile) for this
   signal?" — uncontaminated by execution (the Recorder takes no orders).

   Static, on-demand (the dashboard Excursion tab calls it on a button click) — like Lens.
   Targeted JSONL field extraction; null milestones (signal didn't reach that horizon) are
   EXCLUDED from the percentile (not treated as 0).

 CHANGELOG
   v1.0.5 — COUNCIL support (pairs with SentinelExcursionRecorder_v1_4, schema 1.2): schema 1.2 already
            passes the 1.0-only filter, so the "COUNCIL" signal group appears automatically with the full
            metric set. NEW: ByConviction partition (LOW/MID/HIGH buckets from convBucket) + CouncilCount +
            ConvictionVerdictCode (+1 HIGH-conviction fires out-earn LOW at 15m / -1 worse / 0 inconclusive)
            — the "does higher conviction actually pay?" referee for the dashboard + the Bridge floor.
   v1.0.4 — Group.EyeVerdictCode getter (+1 Eye adds edge / -1 hurts / 0 inconclusive) — the shared
            Eye-referee verdict used by both the dashboard ④ section and the State writer's eye block.
   v1.0.3 — FIRE-RATE: Group tracks distinct fire dates (FireDates) → FiresPerDay = N/days, so the
            dashboard can show "signals/day" (a +EV signal that fires twice a month isn't a business).
   v1.0.2 — VIZ SUPPORT (for the dashboard Excursion visuals): Group.Compute now also computes
            MaeMed5/MaeMed60 (median adverse at 5/60 min) for the growth-line plot; new public
            TpStopGrid(pts) returns ALL 12 TP/stop configs' estimates (the expectancy-curve viz;
            BestTpStop is the max-Exp of these). Pctl is already public (scatter axis scaling).
   v1.0.1 — CORRECTNESS: (a) DEDUPE — the Recorder rewrites its FULL history to a NEW per-load file
            on every F5/re-add, so the same signal fire appeared in many files and was counted many
            times (seen ~9× inflation: 36,668 lines → 4,177 unique). Now keyed by
            inst|bartype|signal|dir|fireTime across all files, so each fire counts once. (b) drop the
            legacy schema-1.0 recorder output (no regime; superseded by 1.1). Summary gains Deduped +
            SchemaSkipped counts (surfaced in the dashboard status). No change to the math/percentiles.
   v1.0 — initial. Group by inst×signal×dir; percentiles of maxMFE/maxMAE + mfe/mae@5/15/60.
```

