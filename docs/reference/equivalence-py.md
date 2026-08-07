---
layout: sentinel-ref
title: "equivalence.py"
blurb: "Lab (Python) · unversioned · 418 lines"
---

# equivalence.py

> `Sentinel/Lab/harness/equivalence.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 418 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
equivalence — does the offline harness build the SAME bars NinjaTrader does?

This is the gate the whole harness stands on. A harness not yet proven equal to NT is just a
faster way to be wrong, and the corpus's entire value is that it is trustworthy -- so this runs
BEFORE anything is allowed to depend on harness output.

  ANSWER KEY   `SentinelBarDump` on an NT chart -> Sentinel\\Harness\\bars\\*.jsonl
  CANDIDATE    `harness.tide` over the same tape -> the same bars, or not

WHY IT COMPARES PER SESSION
---------------------------
Tide resets its CVD lattice at every session open, so a session is a self-contained experiment:
its first bar depends on nothing before it. That makes each session independently comparable and
localises any disagreement -- if session A matches and session B does not, the fault is inside B,
not upstream of it. It also sidesteps the chart's arbitrary left edge: NT's first session is
truncated by the lookback window and is skipped rather than reported as a mismatch.

WHAT COUNTS AS A MATCH
----------------------
Bar close time to the millisecond, and O/H/L/C to half a tick. Volume is compared but NOT
required: NT's per-bar volume includes prints the signer skips (inside-spread trades still carry
volume), so a volume disagreement is expected and is reported for information rather than scored.

READING A FAILURE
-----------------
`first divergence` is the number that matters. If a session matches for 400 bars and then splits,
the cause is at bar 400 -- a single print signed differently -- and not a structural difference.
If a session diverges at bar 1, suspect the session boundary (`--session-open`) or the size, which
move every bar at once. Those two failure shapes have completely different causes; the report
separates them on purpose.
```

