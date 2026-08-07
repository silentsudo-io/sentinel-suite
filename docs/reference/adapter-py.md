---
layout: sentinel-ref
title: "adapter.py"
blurb: "Azimuth (Python) · unversioned · 309 lines"
---

# adapter.py

> `Sentinel/Azimuth/engine/adapter.py`

| | |
|---|---|
| **Family** | Azimuth (Python) |
| **Version** | — |
| **Size** | 309 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | [SENTINEL_AZIMUTH_SPEC](../../SENTINEL_AZIMUTH_SPEC.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
Execution adapters (§1.1.2) and the FROZEN adapter registry (§1.1.3).

    "Execution sits behind an adapter interface. `BacktestAdapter` is the first
     implementation. A broker adapter is a later implementation of the same
     interface, not a rewrite."

    ⛔ "Until then the adapter registry ships with exactly one entry and no way
       to add another at runtime."

That prohibition is enforced here, not merely observed: `_ADAPTERS` is a
`MappingProxyType`, there is no `register()`, no entry-point scan, no plugin
path, and `make_adapter` rejects any name that is not the one entry. A live
adapter requires the hardening surface (kill switch, governor, session gates,
prop rules) to exist on this side first; when it does, it is added HERE, in
source, in a reviewed diff.

THE FILL CONVENTION -- the reason the engine exists
---------------------------------------------------
    buy  fills at the ASK      sell fills at the BID

The suite has measured that its replay fills are unfaithful (0.00% of trades
print inside the spread) and that the P&L *is* the crossing cost. Nothing in
this file ever computes a mid price for a fill, and
`tests/test_fills.py::test_a_mid_price_fill_would_fail_this_test` fails loudly
if that ever changes.

    market / stop fills  -> taker, crossing price, `slippage_ticks` ADVERSE
    limit fills          -> maker, AT the limit price, no price slippage
                            (a limit slips in WHETHER it fills, not at what price)
    stop fills           -> `min(stop, bid)` long / `max(stop, ask)` short, so a
                            gap through the stop fills at the gapped price, never
                            better than the stop
```

