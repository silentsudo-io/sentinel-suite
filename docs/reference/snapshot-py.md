---
layout: sentinel-ref
title: "snapshot.py"
blurb: "Lab (Python) · unversioned · 367 lines"
---

# snapshot.py

> `Sentinel/Lab/snapshot/snapshot.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 367 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
Sentinel corpus snapshot ladder  (tiered, WORM-style, validate-before-destruct).

Tiers
-----
  session   the LIVE Excursions\ tree itself. Recorder v2.1.2 streams rows to
            disk crash-safe every 60 min, so live IS the continuous session-durable
            record. No separate copy is taken -- it is the ground truth the daily
            validates against.
  daily     `daily <date>`  -- point-in-time copy of the corpus + a consistent
            VACUUM'd sentinel.db, zipped, with a per-line-hash manifest. Validates
            its own copy is a SUPERSET of live (catches a mid-copy miss + self-heals).
            Pruned only after the covering weekly validates.
  weekly    `weekly <isoweek>` -- the permanent master (kept forever). Validates it
            is a superset of the union of that week's dailies (WORM guarantee: a row
            any daily saw survives even if live later sheds it), self-heals a gap,
            then destructs the validated dailies.

Validation is SUPERSET-of-row-content-hashes, not a file diff: corpus files grow
append-only through the day, so byte-compare is useless but "every line I had before
is still here" is exact and schema-agnostic.

CAVEAT: this guarantees ARCHIVE INTEGRITY (no row is ever lost), NOT corpus
CORRECTNESS. A superset check preserves poisoned rows as faithfully as clean ones --
contamination is corpus_probe's job, not this ladder's.

Usage
-----
  python snapshot.py daily              # snapshot today, validate vs live
  python snapshot.py weekly             # snapshot this iso-week, validate+prune dailies
  python snapshot.py daily  --date 2026-07-17
  python snapshot.py weekly --week 2026-W29
  python snapshot.py verify <snapshot-dir>   # re-check a snapshot's manifest vs its zip
  python snapshot.py list               # show the ladder
Add --dry-run to any command to plan without writing / destructing.
```

