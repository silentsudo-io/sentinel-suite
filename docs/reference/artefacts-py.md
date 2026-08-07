---
layout: sentinel-ref
title: "artefacts.py"
blurb: "Azimuth (Python) · unversioned · 278 lines"
---

# artefacts.py

> `Sentinel/Azimuth/gates/artefacts.py`

| | |
|---|---|
| **Family** | Azimuth (Python) |
| **Version** | — |
| **Size** | 278 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | [SENTINEL_AZIMUTH_SPEC](../../SENTINEL_AZIMUTH_SPEC.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
artefacts — the four ported kinds of §2, and the registry a fifth is added to.

Each spec is the WRITTEN-DOWN answer to two questions §2 asks of every port:

    what does the gate compare, and what pairs a record on one side with a record on the other?

The field names are §2's own, verbatim, which means they are inconsistently cased (`bar_ts` and
`netScore` in the same table). That is deliberate: the reference column is the NinjaScript corpus
and its keys ARE camelCase, while the tape contract (§3.1) is snake_case. Rather than invent a
third convention, the canonical name is whatever §2 wrote, and a side that spells it differently
declares a `Side.alias` — which is printed on every run, so a rename can never hide a field.

TOLERANCES ARE DECLARED HERE AND NOWHERE ELSE
---------------------------------------------
Every gate field below carries an explicit tolerance and every one of them is `EXACT` (0.0).
That is a judgement, and it is the strict one on purpose: a bar boundary is a tape price copied,
not computed, and a seam value is a deterministic function of the same bars. If a port cannot be
bit-identical, the honest response is to find out WHY, not to open a window and stop looking.
Loosening one is possible (`--tol field=0.25`) and stamps DEGRADED on the verdict, exactly as
`gate3 --tol-ticks` does.
```

