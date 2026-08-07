# config.py

> `Sentinel/Azimuth/engine/config.py`

| | |
|---|---|
| **Family** | Azimuth (Python) |
| **Version** | — |
| **Size** | 225 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | [SENTINEL_AZIMUTH_SPEC](../../SENTINEL_AZIMUTH_SPEC.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
Engine configuration and the DECLARED semantic choices.

Everything in this module that could otherwise be "an accident of code order" is
named, defaulted and testable here. If a fidelity question has more than one
defensible answer, it becomes an enum in this file -- not an `if` buried in a loop.

Spec: Docs/SENTINEL_AZIMUTH_SPEC.md  §1.1, §4.3, §6
```

