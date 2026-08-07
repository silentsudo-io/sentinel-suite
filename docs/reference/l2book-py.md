---
layout: sentinel-ref
title: "l2book.py"
blurb: "Lab (Python) · unversioned · 182 lines"
---

# l2book.py

> `Sentinel/Lab/harness/l2book.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 182 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
l2book — reconstruct the REAL order book from the export's L2 depth stream.

WHY THIS EXISTS
---------------
The L1 `Bid`/`Ask` rows are a DEGRADED summary. Measured on GC 08-26 2026-07-02, latching L1 quotes
gives a median spread of 3-5 ticks in EVERY hour -- including the two busiest, where GC is a 1-tick
market -- and 20% of trades print outside the resulting book, a fraction that does NOT improve with
liquidity. That is not a wide market, it is an incomplete feed.

The same file carries **3.1M L2 rows against 894k L1 rows**: a full depth ladder at 1-tick increments
with a position index and size at every level. That is the real book, and it is what every execution
question needs -- you cannot measure the cost of crossing a spread you cannot see, and you cannot model
a passive fill without queue position.

NT semantics being assumed, and VALIDATED rather than trusted (`--validate`):
    kind      0 = Ask ladder, 1 = Bid ladder      (MarketDataType)
    op        0 = Add, 1 = Update, 2 = Remove      (NT Operation enum)
    pos       0 = top of book, ascending into the ladder
If any of that is wrong the ladder goes incoherent within seconds -- crossed books, non-monotonic
levels -- so the coherence report IS the test of the assumption, not a formality.
```

