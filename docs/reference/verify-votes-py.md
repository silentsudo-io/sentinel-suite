---
layout: sentinel-ref
title: "verify_votes.py"
blurb: "Lab (Python) · unversioned · 279 lines"
---

# verify_votes.py

> `Sentinel/Lab/verify_votes.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 279 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
ACCEPTANCE TEST for VOTE-VECTOR COMPLETENESS — does every voter the lane DECLARES actually reach the corpus?

    cd "Sentinel\\Lab"
    .\\.venv\\Scripts\\python.exe verify_votes.py            # last 3 days of council rows
    .\\.venv\\Scripts\\python.exe verify_votes.py --days 14
    .\\.venv\\Scripts\\python.exe verify_votes.py --json     # machine output (corpus_probe consumes this)

WHY THIS EXISTS
  On 2026-07-23 a clean-looking audition bake produced 1,866 rows that passed every existing check --
  schema 1.5, honest firePx, 0 dups, balanced direction, versions stamped -- and were still USELESS for
  their purpose: every single row carried 18 voters and NEVER BRK, FLUX or CVB, and brkUpper/brkLower
  were 0 throughout. Three of the bar-type-published voters had silently never reached the corpus, and
  brick-level (limit-vs-market) grading was impossible. Nobody noticed for a day; the cause was never
  established and the logs that would have shown it were destroyed by rotation before they were read.

  The cause does not have to be understood for the FAILURE to be caught. What makes this class of bug
  expensive is not that it happens -- it is that it is SILENT and is discovered weeks later, in analysis,
  after the compute has been spent. So this is a script, not a habit of looking.

  ⚠ THIS IS NOT roster_health. probe.py's roster_health reads the Council's LIVE roster line off
  sentinel.log; this reads what was actually WRITTEN TO DISK. In the 07-23 failure the roster line said
  "COMPLETE 20/20" while the recorded vote vector had 18 -- the live claim and the recorded artifact
  DISAGREED. Only the corpus can testify about the corpus.

CHECKS (per inst x bartype lane, on council row corpus)
  1. SEAM      -- the bar type's OWN published voter(s) must be present. Derived from the bar-type id, so
                  it needs NO config and cannot drift: 212201/212202 SentinelTBars/TbarsCount -> BRK,
                  212203 SentinelFlux -> FLUX, 212204 SentinelDrift -> BRK + CVB (Drift publishes both).
                  Coverage 0 on a seam voter is EXACTLY the 07-23 failure => CRIT.
  2. DECLARED  -- every voter in the lane's Roster.conf (resolved by the cascade
                  Models\\<inst>\\<bartag>\\ -> Models\\<inst>\\ -> Models\\) must appear as a KEY in the
                  vote vector. Absent entirely => CRIT; present on <90% of rows => WARN (intermittent
                  dropout, which a union-of-all-rows check would hide).
  3. BRK LEVELS-- on a BRICK bar type, brkUpper/brkLower must be populated, or limit-vs-market grading
                  (limitlab.py) is dead on arrival. <99% populated => CRIT. Flux is NOT a brick type and
                  is correctly exempt -- it has no brick boundaries to record.

  A voter recorded with value 0 COUNTS AS PRESENT. That is the whole point: an abstaining voter wrote
  "I looked, nothing to report", while a missing KEY means the seam never arrived. Those two are what
  the declared roster exists to distinguish, and conflating them is what hid this bug.

EXIT  0 = all lanes complete   1 = WARN (partial coverage, or too thin to judge)   2 = CRIT (missing data)
```

