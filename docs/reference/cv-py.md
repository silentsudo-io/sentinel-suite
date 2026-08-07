---
layout: sentinel-ref
title: "cv.py"
blurb: "Lab (Python) · unversioned · 66 lines"
---

# cv.py

> `Sentinel/Lab/sentinel_lab/cv.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 66 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
Purged walk-forward cross-validation with an embargo.

NEVER use random k-fold here. Overlapping forward label windows mean adjacent rows
share outcomes; random k-fold leaks the test fold into training and is the single
most common way people convince themselves they have an edge they do not have.
(Lopez de Prado, *Advances in Financial Machine Learning*, ch. 7.)

Expanding window, strictly forward:

    train:  rows whose LABEL WINDOW closed at least `embargo` before the fold opens
            -- i.e. t1 <= test_start - embargo.  A row that fired before the fold but
            whose outcome resolves inside it is PURGED: its label is contaminated.
    test:   rows firing inside the fold.
```

