---
layout: sentinel-ref
title: "loaders.py"
blurb: "Azimuth (Python) · unversioned · 186 lines"
---

# loaders.py

> `Sentinel/Azimuth/gates/loaders.py`

| | |
|---|---|
| **Family** | Azimuth (Python) |
| **Version** | — |
| **Size** | 186 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | [SENTINEL_AZIMUTH_SPEC](../../SENTINEL_AZIMUTH_SPEC.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
loaders — turn one column's output into a `Side`.

A loader's whole job is to hand `run_gate` rows and metadata WITHOUT losing anything on the way.
The rule inherited from `gate3.load_side` is the important one: **a file that could not be read is
counted and named, never skipped quietly.** A side that silently dropped forty rows otherwise
presents as a clean smaller set, and the diff blames the port instead of the loader.

Four sources, because that is what the two columns actually emit:

    rows_side      an in-memory list (a Python port's output, and every fault-injection proof)
    jsonl_side     the corpus / any JSONL. `record="first-line"` is the Sentinel corpus
                   convention where the FIRST line of a file is the record and the rest is the
                   tick path (gate3.read_header).
    sqlite_side    `sentinel.db`, opened READ-ONLY through a mode=ro URI. This harness never
                   writes to the corpus warehouse.
    parquet_side   the §3.1 tape and anything else Parquet (needs pyarrow).

PROVENANCE IS NOT OPTIONAL (§3.1). `tape_meta()` lifts a tape sidecar into the identity keys the
bar-type spec demands, so "the two sides read the same tape" is proven by a sha256 rather than
asserted by whoever ran the command.
```

