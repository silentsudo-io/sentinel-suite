---
layout: sentinel-ref
title: "contract.py"
blurb: "Azimuth (Python) · unversioned · 359 lines"
---

# contract.py

> `Sentinel/Azimuth/engine/contract.py`

| | |
|---|---|
| **Family** | Azimuth (Python) |
| **Version** | — |
| **Size** | 359 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | [SENTINEL_AZIMUTH_SPEC](../../SENTINEL_AZIMUTH_SPEC.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
The §3.1 tape contract -- loader, validator, and contract-valid fixture synthesis.

    tape/<instrument>/<session_date>.parquet      session_date = ET trading day
    tape/<instrument>/<session_date>.meta.json    provenance sidecar

    ts_ms     int64    unix ms UTC, monotonic non-decreasing, NEVER bar-snapped
    bid       float64  best bid
    ask       float64  best ask
    last      float64  trade price, null on a quote-only row
    size      int32    trade size, 0 on a quote-only row
    bid_size  int32    nullable
    ask_size  int32    nullable
    kind      int8     0 = quote, 1 = trade

The tape files are produced by a sibling track. This module owns only the
CONSUMER side of the contract: it validates hard (a tape that violates the
contract is refused, not silently coerced) and it can synthesise a
contract-valid tape so the engine can be tested and benchmarked before supply
lands.

⚠ Provenance is not optional (§3.1). `load_session` refuses a tape with no
sidecar unless `require_sidecar=False` is passed EXPLICITLY -- that argument
exists for synthetic fixtures, not for real data.
```

