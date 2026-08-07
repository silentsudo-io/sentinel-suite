# SentinelBarDump_v1_0_0.cs

> `bin/Custom/Indicators/SentinelBarDump_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 385 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelBarDump_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
one at https://mozilla.org/MPL/2.0/.

Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
─────────────────────────────────────────────────────────────────────────────
═════════════════════════════════════════════════════════════════════════════
 SentinelBarDump — the EQUIVALENCE GATE's ground truth
 File: SentinelBarDump_v1_0_0.cs   ·   Version v1.0.0   ·   Schema bars.1   ·   namespace …Indicators.Sentinel
─────────────────────────────────────────────────────────────────────────────
 WHAT THIS IS
   A transcript of what NinjaTrader's bar construction ACTUALLY produced: every completed bar's
   time + OHLC + volume, unthrottled, written to JSONL. It exists to answer exactly one question —
   **does the offline harness build the same bars NT does?** Load it on a chart, and the file it
   writes is the answer key.

 WHY IT HAD TO BE BUILT (2026-07-26)
   The harness's first equivalence target is SentinelTide, and the plan was to compare against the
   `[Sentinel:Tide]` lines in sentinel.log. That log cannot do the job, and the reason is worth
   recording so nobody tries again:
     • it is throttled to one line per 10 WALL-seconds, so a rebuild logs ~8% of bars — and not a
       random 8%, since bar time advances thousands of times faster than wall time;
     • it is REALTIME-GATED, so a static historical chart is silent;
     • it carries NO BAR TIMESTAMP, only the wall-clock instant of the log write;
     • `_lastLog` is per-instance, so several bars-type instances interleave and the `N bars`
       session ordinal jumps between them (measured: 34 lines total, ordinals 1 / 429 / 58, with
       four byte-identical lines 0.24s apart).
   A sample with no join key is not ground truth. This writes every bar, with its own time.

 ⚠ DELIBERATE EXCEPTION: NO REALTIME GATE
   Every recorder in this suite gates on `State == State.Realtime`, because a seam has no as-of
   semantics and a replayed verdict stamped onto an old bar is lookahead contamination. That
   reasoning does not apply here and applying it anyway would destroy the tool: a bar transcript
   contains no labels, no seam reads and nothing forward-looking — it records only what a bar
   ALREADY IS at its own close. Historical bars are precisely what the gate needs to compare.
   Every row carries `rt` (true = built live, false = built during the historical rebuild) so the
   Lab can split them if it ever matters. This also fixes the defect flagged in FLOWBARS §3b:
   "G1 must be checkable on a static chart."

 BAR-TYPE AGNOSTIC ON PURPOSE
   It reads Time/Open/High/Low/Close/Volume, so it works unchanged on Tide, TBars, TbarsCount,
   Flux, Drift, Lattice, Effort — and on stock NT bar types. Every future harness equivalence
   gate uses this same file; it is not a Tide-specific probe.

 NOT A SENSOR — the …State publish protocol does NOT apply
   Design system §9 item 6 requires every new signal/regime/bias/context indicator to publish a
   `…State` seam and be wired into the Council. This is a diagnostic exporter: it has no opinion
   about direction, nothing to fuse, and no consumer inside the platform. Publishing a seam would
   add a publisher to the very seam-store whose failure modes this tool exists to investigate.

 HOW TO USE IT
   1. Add it to the chart whose bar type you want to reproduce. It starts writing immediately —
      the historical rebuild alone gives a full answer key, no replay needed.
   2. The file lands in Sentinel\Harness\bars\<stamp>__<inst>__<bartag>.jsonl. Line 1 is a HEADER
      object (schema, instrument, bar tag, tick size, bars-period values, versions) so the file is
      self-describing and the Lab never has to guess the tick size or the period.
   3. Diff it against the harness with Lab\harness\equivalence.py.

 CHANGELOG
   v1.0.0 (2026-07-26) — initial. Every-bar JSONL transcript, self-describing header, buffered
          writes with periodic flush, no realtime gate (see above), glass card + label remover.
```

