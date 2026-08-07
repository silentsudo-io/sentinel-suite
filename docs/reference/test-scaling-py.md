---
layout: sentinel-ref
title: "test_scaling.py"
blurb: "Azimuth (Python) · unversioned · 298 lines"
---

# test_scaling.py

> `Sentinel/Azimuth/engine/tests/test_scaling.py`

| | |
|---|---|
| **Family** | Azimuth (Python) |
| **Version** | — |
| **Size** | 298 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | [SENTINEL_AZIMUTH_SPEC](../../SENTINEL_AZIMUTH_SPEC.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
Scaling — a trade is entry → flat, composed of LEGS.

A partial exit is a partial fill of the trade, so it belongs in the order model
(§1.1.1), not in a post-hoc P&L adjustment. Scaling is expressed as a change in
the authoritative net target `position[]`; each change emits one leg.

The live thesis is the exit policy — scale-and-trail is the obvious candidate
family — so the assertions below are about the thing that would silently ruin
it: **the protective orders must cover the REMAINING quantity after a partial.**
```

