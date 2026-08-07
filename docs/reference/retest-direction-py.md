# retest_direction.py

> `Sentinel/Lab/harness/retest_direction.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 268 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
retest_direction — DOES ANYTHING HERE PREDICT DIRECTION? Re-run of the 2026-07-22 pivot verdict
on labels that are actually correct.

WHY THIS EXISTS
---------------
The pivot measured 3,777 fires and concluded the Council loses and no sensor survives standalone. That
verdict was measured with a broken ruler: schema<=1.4 priced entries at a synthetic Heikin-Ashi close
that never traded, and `sentinel.db.first_touch` goes blind after 5 minutes
(`TickPathMaxMs=300000`) while the label horizon is ~60 min. So the sensors were never actually
cleared or convicted -- they were measured badly. This re-runs the question on:

  population A  ROW labels, both audition legs, 19-voter roster, schema 1.5 (n~2,965)
                -- bar-derived but externally validated at 98.4% by EXP-0005
  population B  HARNESS labels, GC 08-26 only (n~813)
                -- recomputed from raw tape, full horizon, no 5-min cap, no synthetic price

Agreement between A and B is the robustness check. A result that appears in one and not the other is
not a result.

THE ARITHMETIC
--------------
Barriers are SYMMETRIC (`firePx +- barrierTicks`), so a coin flip gives 50% and expectancy per resolved
fire is simply `barrier * (2p - 1)` ticks, before cost. `COST_TICKS` charges the crossing, matching
observatory.py, because a gross edge smaller than the spread is not an edge.

`firstTouch=+1` means the FAVORABLE barrier was hit first (recorder line 769) = the trade worked.

HONEST n
--------
94% of fires start inside the prior fire's 60-min horizon, so fires are NOT independent -- their
forward tape overlaps ~9 ways. Every interval here is a **day-block bootstrap** (resample whole days
with replacement), which respects that clustering. A naive binomial CI would be ~3x too narrow and is
deliberately not offered.

MULTIPLE COMPARISONS
--------------------
19 voters are scored. Testing 19 things at 95% means ~1 looks significant by luck. The per-voter table
prints a Bonferroni-adjusted interval alongside the raw one; read the adjusted one.
```

