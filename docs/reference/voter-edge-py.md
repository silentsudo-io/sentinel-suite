---
layout: sentinel-ref
title: "voter_edge.py"
blurb: "Lab (Python) · unversioned · 89 lines"
---

# voter_edge.py

> `Sentinel/Lab/voter_edge.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 89 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
Model-free voter edge — is a voter CONFIRMING or CONTRARIAN, and on how much data?

    python voter_edge.py --inst GC --bartypes 212201v6x24 0v150x1 9v1x1

The ridge weights in train.py are confounded by heavy collinearity among the price-derived
voters, so a negative coefficient can be an artifact of another voter stealing the credit.
This looks at each voter UNIVARIATELY: fold x = vote*dir (+1 = agreed with the taken side),
then the uniqueness-weighted first-touch win rate when it AGREED vs DISAGREED. A real
confirming voter: wr(agree) > base > wr(disagree). Contrarian: the reverse. n_agree /
n_disagree is the data behind the claim -- a trigger that rarely fires can't be trusted, and
'(thin)' flags when either bucket is under 40. Read this BEFORE believing any learned weight.
```

