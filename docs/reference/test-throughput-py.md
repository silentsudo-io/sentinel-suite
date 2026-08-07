# test_throughput.py

> `Sentinel/Azimuth/engine/tests/test_throughput.py`

| | |
|---|---|
| **Family** | Azimuth (Python) |
| **Version** | — |
| **Size** | 63 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | [SENTINEL_AZIMUTH_SPEC](../../SENTINEL_AZIMUTH_SPEC.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
Throughput -- the number that sets the Rust bar (§6, §10.3).

The full measurement lives in `engine.sweep.bench`; run it directly for the
headline figure. What is asserted here is a FLOOR, so a refactor that quietly
turns a vectorised scan into a Python loop fails CI instead of being discovered
by a slow sweep six weeks later.
```

