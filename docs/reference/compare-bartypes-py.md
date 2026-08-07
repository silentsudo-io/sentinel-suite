---
layout: sentinel-ref
title: "compare_bartypes.py"
blurb: "Lab (Python) · unversioned · 165 lines"
---

# compare_bartypes.py

> `Sentinel/Lab/compare_bartypes.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 165 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
Compare the Council's decisions ACROSS bar types on one instrument.

Answers the operator question "how much does bar type matter?" from the per-scope
excursion corpus a Market-Replay session produces (one JSONL per scope). It reuses
the Lab's own parsing/label logic (sentinel_lab.dataset + labels) so the numbers are
consistent with train.py -- this is a lens on the SAME corpus, not a second pipeline.

Three views:
  1. PER-BARTYPE -- fire count, cadence, direction balance, conviction, and the
                    first-touch WIN rate (the outcome that actually pays). eff_n is
                    the AFML concurrency-adjusted N -- trust it over the raw count.
  2. CO-FIRE     -- when two bar types fire within a tolerance window, do they AGREE
                    on direction? (decision consistency -- do they see the same call?)
  3. OVERLAP     -- how often do they fire at the same time at all? (do they even see
                    the same setups, or is each bar type its own market?)

Usage:
  python compare_bartypes.py --inst GC [--dir ../Excursions] [--tol-min 3]
                             [--since 2026-07-13] [--until 2026-07-14] [--horizon 15]

Reads only the corpus; never touches bin\\Custom.
```

