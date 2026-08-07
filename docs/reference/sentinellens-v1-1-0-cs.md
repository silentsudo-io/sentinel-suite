---
layout: sentinel-ref
title: "SentinelLens_v1_1_0.cs"
blurb: "AddOns / runtime · 1.1.0 · 305 lines"
---

# SentinelLens_v1_1_0.cs

> `bin/Custom/AddOns/SentinelLens_v1_1_0.cs`

| | |
|---|---|
| **Family** | AddOns / runtime |
| **Version** | 1.1.0 |
| **Size** | 305 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `Trade` |
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
 SentinelLens — trade analytics over the Sentinel Log JSONL (NT8, Sentinel Suite)
 File: SentinelLens_v1_1_0.cs
 Version: v1.1.0
─────────────────────────────────────────────────────────────────────────────
 WHAT THIS IS  (see memory: sentinel-log-integration, sentinel-eye-tool, profit-plan-and-accounts)
   The suite's ANALYTICS layer. It reads the MAE/MFE trade records that Sentinel Log
   writes to <UserDataDir>\Sentinel\Log\*.jsonl and aggregates them into win rate,
   profit factor, net/avg ticks, average heat (MAE), average favorable (MFE), and
   MFE-capture efficiency — overall and broken down by strategy and instrument.

   v1.1.0 adds the EYE-PARTITION analysis — the profit keystone's payoff. Sentinel Log
   schema 1.1 stamps each trade with the SentinelEye verdict that stood at entry
   (eyeHad/eyeDir/eyeScore/eyeAligned/eyeAgeSec). Lens now partitions trades by that
   verdict and answers the one question the whole suite rests on:
       Do Eye-ENDORSED trades out-earn the rest?  (i.e. does the Eye filter add edge?)
   Two views: (1) endorsement partition — Endorsed / NotEndorsed / NoVerdict; and
   (2) a score-band curve (0-20 … 80-100) so we can SEE where expectancy turns positive
   and set the qualify threshold on evidence, not a guess. Plus a plain-English verdict.

   UNLIKE Copy/Log/Risk, Lens is NOT an always-on AddOnBase service — it's a read-only,
   on-demand analyzer (a static class the dashboard's "Lens" tab calls on a button click).

 PARSING: targeted field extraction (no JSON library dependency). We only pull the top-level
   scalar fields we need per record and IGNORE the nested "path":[…] array entirely, so a
   hand parser is safe against the known, self-produced schema. Fields (SentinelLogEngine):
   account, strategy, inst, dir, qty, tier, pnlTicks, maeTicksRaw, mfeTicksRaw, exitReason,
   + (schema 1.1) eyeHad, eyeDir, eyeScore, eyeSource, eyeAgeSec, eyeAligned.

 CHANGELOG
   v1.1.0 — EYE-PARTITION analysis. Trade gains eye fields; Summary gains ByEye (Endorsed/
            NotEndorsed/NoVerdict), ByEyeScoreBand (20-wide bands over trades with a verdict),
            and EyeVerdict (human-readable edge conclusion, expectancy-based). Back-compatible:
            schema-1.0 records (no eye block) fall into NoVerdict. New file; v1_0_0 frozen.
   v1.0.0 — initial: LoadSummary() reads all Sentinel\Log\*.jsonl, parses records,
            aggregates Overall + ByStrategy + ByInstrument. Defensive (skips unparseable lines).
```

