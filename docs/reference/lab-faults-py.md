---
layout: sentinel-ref
title: "lab_faults.py"
blurb: "Lab (Python) · unversioned · 280 lines"
---

# lab_faults.py

> `Sentinel/Lab/lab_faults.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 280 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
lab_faults — the Lab's counterpart to SentinelCore.Swallow (C#, Core v1.41.0).

WHY THIS EXISTS
---------------
The adversarial review (Docs/SENTINEL_ADVERSARIAL_REVIEW.md) found ~350 empty `catch {}` on the
C# side and their Python equivalents here. The intent was always right — a probe, an ingester or a
Streamlit page must never die because one malformed row failed to parse. The defect is that
*don't propagate* was implemented as *don't record*. Every expensive bug in this project has been
made expensive by exactly that: the BRK/FLUX seam hunt, the 160 false NAKED POSITION criticals, the
Eye never loading, `ingest.py --watch` running for three days against a schema it could not read.

`swallow()` keeps the runtime behaviour byte-for-byte identical — it never raises and never alters
control flow — and adds the one thing that was missing: a RECORD and a COUNT.

CONTRACT (deliberately identical to the C# SentinelCore.Swallow)
---------------------------------------------------------------
  * NEVER raises. Its own body is guarded; a broken recorder must never break a caller.
  * NEVER changes control flow. Put it *before* the existing `pass` / `continue` / `return`.
  * Rate-limited PER TAG: first 3 occurrences always, then at most one per 60 s. This is the
    answer to the flood fear that made empty handlers attractive in the first place.
  * COUNTS everything, including suppressed occurrences, so `fault_total()` is honest.

USAGE
-----
    from lab_faults import swallow

    try:
        row = json.loads(line)
    except json.JSONDecodeError as _swex:
        swallow("ingest.parse", _swex)
        continue

Read the counts:

    from lab_faults import faults, fault_total
    faults()        # {'ingest.parse': 12, 'probe.disk': 1}
    fault_total()   # 13

Or from a shell:

    python -m lab_faults            # print the tail of the fault log
    python -m lab_faults --clear    # rotate the log by hand

LOG RETENTION
-------------
The review's other finding was that single-generation rotation destroyed a forensic window twice in
one night. This keeps LOG_GENERATIONS backups, not one.

Stdlib only, on purpose: `verify_votes.py` and the health probes are deployed standalone to bake
nodes and must not grow a dependency.
```

