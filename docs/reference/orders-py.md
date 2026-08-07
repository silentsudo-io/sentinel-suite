---
layout: sentinel-ref
title: "orders.py"
blurb: "Azimuth (Python) · unversioned · 226 lines"
---

# orders.py

> `Sentinel/Azimuth/engine/orders.py`

| | |
|---|---|
| **Family** | Azimuth (Python) |
| **Version** | — |
| **Size** | 226 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | [SENTINEL_AZIMUTH_SPEC](../../SENTINEL_AZIMUTH_SPEC.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
The order model (§1.1.1) -- real order objects with a real state machine.

    "The engine's order model is a REAL order model -- order objects with state,
     working/filled/cancelled transitions, partial fills, one position at a time,
     an SL/TP cascade -- not a vectorised shortcut that only produces P&L."

Every transition goes through `Order.transition()`, which refuses illegal moves.
That is deliberate: the reason this model exists is so a broker adapter is a
later implementation of the same interface rather than a rewrite, and a broker
will absolutely hand you a WORKING -> PARTIALLY_FILLED -> CANCELLED sequence that
a P&L-only shortcut has no vocabulary for.
```

