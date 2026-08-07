# nrdcsv.py

> `Sentinel/Lab/harness/nrdcsv.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 305 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
nrdcsv — reader for the CSV that `gbNRDtoCSV` exports out of NinjaTrader's `.nrd` tick store.

This is layer 1 of the offline harness: it turns NT's replay data into a plain Python tick stream
so bar types / sensors / fusion can run with no GUI, no render thread, no connection, and across
all cores. `gbNRDtoCSV` (MIT, (c) 2021 Yevgeny Iliyn) is vendored at `bin\\Custom\\AddOns\\gbNRDtoCSV.cs`
and runs INSIDE NT via Tools -> NRD to CSV, so NT reads its own proprietary file once and
everything downstream is free of NT forever. Do not write a `.nrd` parser.

FORMAT -- MEASURED on GC 02-26 (2025-12-08 .. 2026-01-02), not taken from documentation
---------------------------------------------------------------------------------------
Semicolon-delimited, NO header row, decimal separator `.` (VERIFY per export -- it follows the
machine's regional setting). L1 and L2 rows are INTERLEAVED in the same file and are told apart by
a leading tag, which earlier notes here omitted:

    L1;<mdType>;<yyyyMMddHHmmss>;<subsec>;<price>;<volume>
    L2;<mdType>;<yyyyMMddHHmmss>;<subsec>;<op>;<pos>;<marketMaker>;<price>;<volume>

  mdType   NT's MarketDataType enum: 0 Ask · 1 Bid · 2 Last · 3 DailyHigh · 4 DailyLow ·
           5 DailyVolume · 6 LastClose · 7 Opening · 8 OpenInterest.
           0/1/2 are the three that matter: Last is the trade, Bid/Ask are what quote-rule
           signing needs. Without them a flow-clocked bar (Flux/Drift/Tide/CVD) is unreproducible.
  subsec   sub-second remainder in 100-ns ticks (0 .. 9_999_999), NOT microseconds.
  op/pos   L2 only: Operation (0 Add, 1 Update, 2 Remove) and book Position.

TIMEZONE -- the trap, and how it was established
------------------------------------------------
Timestamps are in NT's LOCAL display timezone (America/Chicago here), NOT UTC. The corpus JSONL is
UTC, so every consumer must convert or it will silently mis-join by six hours.

Proven by measurement rather than assumption, three independent ways:
  * the CME maintenance break (16:00-17:00 CT) shows up as an hour with EXACTLY ZERO trades at
    file-hour 16 -- hour 22 (the UTC candidate) and hour 17 (the ET candidate) are both busy;
  * every Friday file stops at exactly 16:00:00 -- the GC weekly close, 16:00 CT;
  * 2025-12-24 stops early (holiday session) and 2025-12-25 starts late.
`census()` below re-runs the zero-trade-hour probe on any file, so this is checkable, not folklore.

  WARNING -- DST fall-back is genuinely lossy. Local 01:00-01:59 on the November transition occurs
  twice and the export keeps no offset, so those rows cannot be placed on the UTC line without
  guessing. We resolve them as the FIRST pass (fold=0) and count them; `census()` reports the count.
  Do not run an equivalence gate across that hour.

DAY PARTITION
-------------
File `D.csv` spans local `[D-1 23:00:00, D 23:00:00)`. So a Sunday file holds only the pre-open
daily-high/low rows; the Sunday session open lands in MONDAY's file. Range a window by content,
never by filename.
```

