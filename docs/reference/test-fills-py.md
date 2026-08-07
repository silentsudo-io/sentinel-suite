---
layout: sentinel-ref
title: "test_fills.py"
blurb: "Azimuth (Python) · unversioned · 246 lines"
---

# test_fills.py

> `Sentinel/Azimuth/engine/tests/test_fills.py`

| | |
|---|---|
| **Family** | Azimuth (Python) |
| **Version** | — |
| **Size** | 246 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | [SENTINEL_AZIMUTH_SPEC](../../SENTINEL_AZIMUTH_SPEC.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
THE FILL CONVENTION -- buy at the ASK, sell at the BID.

This project has measured that its replay fills are unfaithful (0.00% of trades
print inside the spread) and that the P&L *is* the crossing cost. These tests are
the reason the engine exists; if any of them go green on a mid-price model,
delete the engine.
```

