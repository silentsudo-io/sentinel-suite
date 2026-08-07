---
layout: sentinel-ref
title: "ident_check.py"
blurb: "Lab (Python) · unversioned · 119 lines"
---

# ident_check.py

> `Sentinel/Lab/docs/ident_check.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 119 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
Prototype of the MISSING docs-health check: does every identifier a doc NAMES still exist?

audit.py tracks links, tokens and contract versions -- not whether a sentence is TRUE. That blind spot
is what let 9 docs name a dead class. This closes the cheapest part of it: any `backticked` symbol that
looks like an API identifier must appear somewhere in the tree.

    python ident_check.py <doc.md> [--section "## 6."] [--tree <bin/Custom>]

Reports UNKNOWN identifiers -- candidates for "this no longer exists". Read-only.
```

