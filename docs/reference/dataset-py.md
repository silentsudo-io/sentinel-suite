---
layout: sentinel-ref
title: "dataset.py"
blurb: "Lab (Python) · unversioned · 297 lines"
---

# dataset.py

> `Sentinel/Lab/sentinel_lab/dataset.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 297 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
Load Sentinel excursion JSONL into a training frame.

Schema-tolerant: reads 1.2 (no decision vector) and 1.3 (with it). Rows that
predate 1.3 simply carry NaN in the voter columns and are dropped by the weight
trainer -- but they remain fully usable for the calibration curve, which needs
nothing but `conviction`.

The one modelling decision that lives here: FOLD BY DIRECTION.

    x_i = vote_i * dir

A long verdict with EYE=+1 and a short verdict with EYE=-1 are the SAME evidence
("the Eye agreed with the taken side"). Folding halves the feature space, doubles
effective N, and -- because `dir == sign(netScore)` for any verdict -- makes the
fitted coefficient directly comparable to the Council's hand-set `WeightEye`.
It drops straight into Model.conf.
```

