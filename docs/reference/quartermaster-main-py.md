---
layout: sentinel-ref
title: "__main__.py"
blurb: "Lab (Python) · unversioned · 113 lines"
---

# __main__.py

> `Sentinel/Lab/quartermaster/__main__.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 113 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
The parity harness on the command line.

    cd "Sentinel\\Azimuth"
    python -m gates list
    python -m gates describe --artefact strategy
    python -m gates selftest                       # the fault-injection proof
    python -m gates compare --artefact sensor \\
        --ref-jsonl  nt\\trend.jsonl      --ref-meta  nt\\trend.meta.json  --ref-label NT \\
        --cmp-jsonl  py\\trend.jsonl      --cmp-meta  py\\trend.meta.json  --cmp-label Azimuth \\
        --json verdict.json

Exit codes are the contract:  0 = PASS   1 = FAIL   2 = ABORT / could not run the test.
```

