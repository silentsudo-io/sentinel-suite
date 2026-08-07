---
layout: sentinel-ref
title: "__init__.py"
blurb: "Azimuth (Python) · 0.1.0 · 45 lines"
---

# __init__.py

> `Sentinel/Azimuth/engine/__init__.py`

| | |
|---|---|
| **Family** | Azimuth (Python) |
| **Version** | 0.1.0 |
| **Size** | 45 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | [SENTINEL_AZIMUTH_SPEC](../../SENTINEL_AZIMUTH_SPEC.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
`bars` -- the Sentinel bar types, in Python, each behind a parity gate.

SPEC §1 (the two columns): one definition, two implementations. SPEC §2 (the parity
law): the Python side is not trusted until the gate says the two agree.

THE PACKAGE CONTRACT -- all of it
---------------------------------
A bar type is a CALLABLE:

    build(tape: Tape, **params) -> BarSeries

`BarSeries` (see `series.py`) carries the bar type's own OHLCV plus `end_idx` -- the
tape row that closed each bar -- which is `engine.bars.bars_from_end_idx`'s existing
seam. `to_engine_bars(series)` hands the result to the backtest engine.

A module registers itself AT IMPORT:

    from . import register
    register("renko", renko, params_str=..., nt_period_type=11, doc="...")

Nothing imports it by name. `__init__` walks its own directory once and imports every
module that is not private, not a test and not part of the plumbing; each one registers
itself on the way past. Dropping `tbars.py` or `flux.py` into this folder is the whole
installation step.

⚠ A module that fails to import is NOT silently skipped. It lands in
`DISCOVERY_ERRORS`, `kinds()` reports it, `get()` names it in its error message, and
`raise_discovery_errors()` turns it back into the exception. A crashed bar type must
not be indistinguishable from an absent one -- that is `eye-never-loads-bug` wearing a
different hat.
```

