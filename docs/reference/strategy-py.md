---
layout: sentinel-ref
title: "strategy.py"
blurb: "Azimuth (Python) · unversioned · 247 lines"
---

# strategy.py

> `Sentinel/Azimuth/engine/strategy.py`

| | |
|---|---|
| **Family** | Azimuth (Python) |
| **Version** | — |
| **Size** | 247 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | [SENTINEL_AZIMUTH_SPEC](../../SENTINEL_AZIMUTH_SPEC.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
The strategy interface (§6): a strategy returns ALIGNED ARRAYS, one per bar.

    entry_long  exit_long  entry_short  exit_short
    sl_long  tp_long  sl_short  tp_short
    entry_limit_long  entry_limit_short
    block_entries  size  position
    + arbitrary `tag_name` bool arrays

A strategy computes; it does not execute. Everything about WHEN and AT WHAT
PRICE lives in the engine and the adapter, which is what makes one engine sit
behind chart, analyzer, optimizer and WFA, and what makes §5.2 possible: a tag
filter modifies `block_entries` and the engine RE-RUNS, so suppressing trade #3
genuinely frees the engine to take #4.

TIMING CONTRACT -- the rule that keeps this free of lookahead
--------------------------------------------------------------
Every array is indexed by BAR and read AT THAT BAR'S CLOSE. The decision taken
at bar k's close is worked over interval k == tape rows (end_idx[k], end_idx[k+1]].
`sl_long[k]` is therefore the stop in force from bar k's close to bar k+1's
close -- it is a TRAILING stop for free, and it can never be in force during
bar k, which the strategy had not finished seeing when it chose the price.
```

