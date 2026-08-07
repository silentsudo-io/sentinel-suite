# ntdump.py

> `Sentinel/Azimuth/bars/ntdump.py`

| | |
|---|---|
| **Family** | Azimuth (Python) |
| **Version** | — |
| **Size** | 260 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | [SENTINEL_AZIMUTH_SPEC](../../SENTINEL_AZIMUTH_SPEC.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
The REFERENCE column: reading NinjaTrader's own bars out of `SentinelBarDump`.

`bin\\Custom\\Indicators\\SentinelBarDump_v1_0_0.cs` (schema `bars.1`) is already
installed and already compiled. Loaded on a chart it writes every COMPLETED bar --
time, OHLC, volume -- to `Sentinel\\Harness\\bars\\<stamp>__<inst>__<bartag>.jsonl`,
historical rebuild included, unthrottled. That file is the answer key §2 requires, and
it is the only way found to get NinjaTrader's bars out of NinjaTrader: the bridge has
no bar-series command (`chartseries` only MUTATES a chart's series, `histdump` exports
depth, `histget` fetches `.nrd`), and bars are derived on the fly, never stored.

WHAT THIS MODULE HAS TO RECONCILE
---------------------------------
* `i` is the chart-global `CurrentBar`, but §2 pairs on `(session, bar_index)`. Both
  sides renumber from each session's first bar, found via the dump's own
  `newSession` flag (`Bars.IsFirstBarOfSession`).
* `t` is ISO-8601 UTC at 100 ns resolution; the tape is integer ms. Both sides FLOOR
  (`build_tape.py` does `ticks // 1_000_000`), so the conversion is lossless in the
  sense that matters -- but a rounding disagreement here would look like a bar-boundary
  disagreement, which is why it is done in one place and said out loud.
* The dumper is `Calculate.OnBarClose`, so the forming bar is never written. The Python
  side drops its trailing bar to match (`gate_rows(closed_only=True)`).
* A repeated `i` means NinjaTrader rebuilt a bar (`RemoveLastBar` -- which is exactly
  what Renko does on every brick). LAST WINS, and the count is surfaced, never hidden:
  a dump with unexpected rebuilds is telling you something about the bar type.
```

