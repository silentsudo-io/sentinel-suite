---
layout: sentinel-ref
title: "exp0005.py"
blurb: "Lab (Python) · unversioned · 455 lines"
---

# exp0005.py

> `Sentinel/Lab/harness/exp0005.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 455 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
exp0005 — cross-correlate the HARNESS (raw tape) against the BAKE (NT's decisions).

**Pre-registration: `PREREGISTRATION_EXP0005_harness_x_bake.md`. Read it before changing a threshold.**

The harness and the bake are two independent measurements of the SAME 11 days of GC 08-26 tape. The
bake has 834 decision instants with tick-true sidecars but cannot check its own labels; the harness has
the raw tape but no decisions. Crossed, the harness is an external referee for the corpus.

The join is on the TIME AXIS and the tape, never on bars -- the bake is TBars `212201v6x24` and the
harness has only Tide `212207` ported, so no bar-for-bar comparison exists and none is attempted. That
makes every result here bar-type-agnostic.

    A   alignment control     first tape print at/after each fire == corpus firePx (>= 99%, <= 1 ms)
    A-  INVERTED control      the same test with +1 h injected. It MUST FAIL. A matcher that passes a
                              one-hour shift is measuring its own tolerance, not the data.
    B   path replication      harness path extremes vs the sidecar's (>= 95%)
    C   label adjudication    harness recomputes first_touch from the tape ALONE, then 3-way against
                              the sidecar (tick-true) and the row (bar-derived)

Stages gate: A fails -> stop; B fails -> that IS the finding, and C's label claims are not to be read
as if the sidecars were sound.

`nrdcsv.iter_l1` already yields UTC nanoseconds (Chicago->UTC internal, DST-ambiguity counted), so no
hand-rolled timezone math enters the join.
```

