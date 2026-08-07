---
layout: sentinel-ref
title: "SentinelTapeRecorder_v1_0_0.cs"
blurb: "Indicators · 1.0.0 · 384 lines"
---

# SentinelTapeRecorder_v1_0_0.cs

> `bin/Custom/Indicators/SentinelTapeRecorder_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 384 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelTapeRecorder_v1_0_0` |
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
 SentinelTapeRecorder — capture a REAL order book, live, because the replay store has none
 File: SentinelTapeRecorder_v1_0_0.cs   ·   Version v1.0.0   ·   Schema = gbNRDtoCSV L1/L2
 namespace …Indicators.Sentinel
─────────────────────────────────────────────────────────────────────────────
 WHY THIS EXISTS (2026-07-26) — read this before "improving" it
   Execution research needs a book: the true touch, the size resting on it, and where you would
   sit in the queue. NinjaTrader's MARKET REPLAY store cannot supply one. Measured on the
   gbNRDtoCSV export of GC 08-26 (memory `replay-depth-not-fit-for-execution`):
     • 0.00% of 103,157 trades printed INSIDE the spread — structurally impossible in a real book
       when three empty price levels sit between bid and ask;
     • median size at the touch was 1 CONTRACT (real front-month GC holds dozens);
     • the spread was 4-5 ticks and stayed 5 ticks in QUIET moments (530,310 samples, no trade for
       >5 s), so staleness and row-ordering cannot explain it;
     • the L1 Bid/Ask latch and the fully reconstructed L2 ladder agree exactly (20.1% of trades
       outside the book either way) ⇒ both encodings share one degraded source.
   The reader was not the problem: `Lab\harness\l2book.py` reconstructs those ladders at 100.00%
   monotonic with zero malformed ops. The DATA is the problem.

   Real-time depth is genuine — it is only the recorded replay store that is degraded. So the fix
   is to capture the live tape ourselves, starting now, because every day not recording is a day
   of book data that cannot be bought back later.

 ⚠ DELIBERATE: THIS ONE **IS** REALTIME-GATED — the opposite of SentinelBarDump
   BarDump deliberately records historical bars, because a bar transcript contains nothing
   forward-looking and the rebuild is exactly what its gate needs. The reasoning inverts here.
   Historical/replay depth IS the degraded data this tool exists to escape; recording it would
   re-poison the well with the very artifact we just spent a day proving is unusable. Nothing is
   written until `State == State.Realtime`, and the card says WAITING until then.

 FORMAT — byte-identical to gbNRDtoCSV, on purpose
   Semicolon-delimited, NO header row, NT-LOCAL timestamps, sub-second as 100-ns ticks:
     L1;{mdType};{yyyyMMddHHmmss};{subsec100ns};{price};{volume}
     L2;{kind};{yyyyMMddHHmmss};{subsec100ns};{op};{pos};{maker};{price};{volume}
   mdType/kind = NT MarketDataType (0=Ask 1=Bid 2=Last).  op = NT Operation (0=Add 1=Update
   2=Remove).  pos = depth position, 0 = top of book.
   ⇒ `nrdcsv.iter_l1`, `nrdcsv.iter_l2` and `l2book.py` read these files with ZERO changes, and
   `CSV_ROOT` can simply be pointed at the tape directory. Matching an existing format beat
   inventing a better one: it makes every tool already written work on day one.
   Line 1 is a `#META;` comment — both parsers filter on the `L1;`/`L2;` prefix, so it is
   invisible to them while keeping the file self-describing.

 LAYOUT + ROTATION
   Sentinel\Tape\<Instrument FullName>\yyyyMMdd.csv — one file per NT-LOCAL date, mirroring the
   export's own layout so the Lab's day-file logic (`regime_study`, `noise_floor`) works unchanged.

 ⚠ DISK. This is not a small file. GC alone produced 6.7M depth events in one day; expect roughly
   **300 MB per instrument per day** uncompressed, and L2 is ~3.5x the row count of L1. Set
   `RecordDepth = false` if you only need trades. Watch free space before leaving it unattended.

 NOT A SENSOR — the …State publish protocol does NOT apply
   Design system §9 item 6 requires new signal/regime/bias/context indicators to publish a `…State`
   seam and wire into the Council. This is a capture device: no opinion, nothing to fuse, no
   in-platform consumer. It writes files and draws a card.

 HOW TO USE IT
   1. Put it on a chart of the instrument you want taped. Any bar type — it never reads bars.
   2. Leave the chart open and connected. The card shows LIVE + running row counts.
   3. Point the Lab at Sentinel\Tape\ and every existing harness reader just works.

 CHANGELOG
   v1.0.0 (2026-07-26) — initial. Live L1 + L2 capture in gbNRDtoCSV format, per-day rotation,
          realtime-gated on purpose, buffered writes, glass card + label remover.
```

