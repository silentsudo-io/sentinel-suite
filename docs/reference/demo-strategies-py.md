# demo_strategies.py

> `Sentinel/Azimuth/engine/demo_strategies.py`

| | |
|---|---|
| **Family** | Azimuth (Python) |
| **Version** | — |
| **Size** | 211 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | [SENTINEL_AZIMUTH_SPEC](../../SENTINEL_AZIMUTH_SPEC.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
Reference strategies. They exist to EXERCISE and BENCHMARK the engine.

⚠ These are not trading ideas and no conclusion should ever be drawn from them.
The suite already knows why: DIRECTION IS DEAD, and a moving-average cross is a
coin flip gross. They are here because a throughput number needs a realistic
signal-generation cost attached to it, and because the semantics tests need a
strategy that produces brackets, limits and flips.

The real Sentinel strategies (Keel) arrive via the §2 parity gate, not here.
```

