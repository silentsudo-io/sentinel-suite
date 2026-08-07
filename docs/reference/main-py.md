---
layout: sentinel-ref
title: "__main__.py"
blurb: "Azimuth (Python) · unversioned · 172 lines"
---

# __main__.py

> `Sentinel/Azimuth/gates/__main__.py`

| | |
|---|---|
| **Family** | Azimuth (Python) |
| **Version** | — |
| **Size** | 172 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | [SENTINEL_AZIMUTH_SPEC](../../SENTINEL_AZIMUTH_SPEC.md) |

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

