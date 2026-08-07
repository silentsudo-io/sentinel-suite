# engine.py

> `Sentinel/Azimuth/engine/engine.py`

| | |
|---|---|
| **Family** | Azimuth (Python) |
| **Version** | — |
| **Size** | 673 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | [SENTINEL_AZIMUTH_SPEC](../../SENTINEL_AZIMUTH_SPEC.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
The engine (§6). One engine behind chart, analyzer, optimizer and WFA.

READ `bars.py` "THE INTERVAL GEOMETRY" FIRST -- everything below is expressed in it.

WHAT THIS ENGINE GUARANTEES
---------------------------
1. ONE POSITION AT A TIME. Never two, never scaled.
2. SAME-BAR CONFLICT. `entry_long[k] and entry_short[k]` -> NEITHER triggers, and
   the bar is counted in `entries_blocked_conflict`. It is not "long wins because
   the `if` came first".
3. FILLS AT THE CROSSING PRICE. Buy at the ask, sell at the bid, always, in
   `adapter.py`. No code path in this package computes a mid for a fill.
4. MILLISECOND TIMESTAMPS THAT NEVER SNAP TO A BAR BOUNDARY. Every fill carries
   the `ts_ms` of the tape row it happened on. The bar index is bookkeeping; the
   tape row is the truth.
5. A DECLARED SL/TP RESOLUTION ORDER. `config.EXIT_PRIORITY` +
   `config.TouchResolution`, applied in one place, counted in
   `BacktestResult.ambiguous_exits`.
6. LIMIT ENTRIES WITH A LIFETIME. Placed -> filled or EXPIRED, as real order
   objects with real state transitions.
7. WARMUP DAYS, CONTINUOUS MODE, AND FORCE-FLAT at session end, contract
   rollover, tape gap and end of data.

WHAT IT DELIBERATELY DOES NOT DO
--------------------------------
It does not scale in or out, it does not hold two instruments, and it does not
route to anything but `BacktestAdapter` (§1.1.3).
```

