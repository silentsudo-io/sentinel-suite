---
layout: sentinel-ref
title: "bars.py"
blurb: "Azimuth (Python) · unversioned · 324 lines"
---

# bars.py

> `Sentinel/Azimuth/engine/bars.py`

| | |
|---|---|
| **Family** | Azimuth (Python) |
| **Version** | — |
| **Size** | 324 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | [SENTINEL_AZIMUTH_SPEC](../../SENTINEL_AZIMUTH_SPEC.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
Derived bars + the INTERVAL geometry the engine's fidelity rests on.

⚠ This is NOT the Sentinel bar-type port. TBars / Flux / BRK / CVB / Drift / Flow
live in the sibling `Azimuth\\bars\\` track with their own parity gates (spec §4).
What lives here is the minimum a backtest engine needs to exist: time bars and
tick bars derived from the tape, plus the index geometry below. Any bar type from
the sibling track can drive this engine by handing it a `Bars` with a valid
`end_idx` -- that is the whole interface.

⭐ Bars are always DERIVED, never stored as truth (§3.1).

THE INTERVAL GEOMETRY -- read this before reading engine.py
-----------------------------------------------------------
`end_idx[k]` is the index of the LAST tape row belonging to bar k.

    interval k  ==  tape rows (end_idx[k], end_idx[k+1]]     for k in [0, n_bars-2)

A decision taken at bar k's CLOSE is worked over interval k. This is the single
rule that keeps the engine free of lookahead: `sl_long[k]` is the stop that was
in force from bar k's close until bar k+1's close -- never during bar k itself,
which is data the strategy had not seen when it chose the price.

Bar k+1's rows ARE exactly interval k's rows, so the per-interval bid/ask extremes
below are the same numbers as bar k+1's high/low of the book. They are precomputed
ONCE per tape and shared across every combo of a sweep; they are what makes the
engine skip 99% of intervals without ever guessing.

⭐ `end_idx` IS NON-DECREASING, NOT STRICTLY INCREASING
-------------------------------------------------------
A threshold-crossing bar clock -- Renko, brick, range, and plausibly Flux and
TBars -- prints SEVERAL bars from ONE tape row when price jumps far enough to
break multiple levels at once. Those bars carry zero rows and zero volume.
Measured on real tape, **35.7% of Renko 1/1 bars are row-less** (672,685 of
1,885,078) and Renko 1/1 is the largest bartag in the corpus. A strictly
increasing `end_idx` quietly assumes every bar contains market data, and that
assumption is false for most of the clocks this suite actually trades.

So equal consecutive `end_idx` values are LEGAL and mean "these bars closed on
the same tape row". The consequences are physical, not conventions:

  * **A zero-row interval offers NO fill opportunity.** No entry, no exit, no
    stop and no target can trigger inside it -- a fill needs a quote or a trade
    to fill against, and there was none.
  * **A decision taken at a zero-row bar's close CARRIES FORWARD** to the next
    interval that has rows, where it is worked normally. It is not discarded and
    it never fills at the previous row's price.
  * **It is unambiguous.** There is exactly one lawful answer, so it is reported
    as structure (`Bars.n_empty_intervals`, `BacktestResult.zero_row_intervals`)
    and NOT as an `ambiguous_exit`.

A DECREASING `end_idx` is still malformed input and still refuses loudly.
```

