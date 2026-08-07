---
layout: sentinel-ref
title: "exit_policy_sweep.py"
blurb: "Lab (Python) · unversioned · 316 lines"
---

# exit_policy_sweep.py

> `Sentinel/Lab/exit_policy_sweep.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 316 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
exit_policy_sweep - rank EXIT POLICIES over the banked KEEL corpus, in two honest tiers.

WHY THIS EXISTS (NOW.md, user's direction 2026-07-31):
  "we will figure out a way to stop using the current stop and tp through this data ...
   I know we are smart enough to capture the profit from these entries."
  => the working hypothesis is that the ENTRIES are fine and the EXIT POLICY is what loses.
  D2 (61% of stop-outs had the peak still ahead) is the evidence for exactly that.

WHY IT IS NOT JUST "SIMULATE THE POLICIES":
  The corpus CANNOT resolve most of the interesting policy space, and a sweep that quietly
  returns a number for a policy it cannot resolve is the fifth instance of the pattern named in
  NOW.md -- a truncated measurement that presents as a plausible NUMBER rather than an error.
  Measured on the bake cohort (n=1435) before this tool was written:

      tick paths : p50 max-favorable 1.01R   only 31% ever reach +1.25R   6% reach +2R
      row 60-min : p50 MFE           3.40R        69% reach +2R          33% reach +5R
      1125 of 1372 trades (82%) have their true 60-min MFE OUTSIDE the tick path

  Cause: SentinelExcursionRecorder v2.4.0 releases the tick buffer TickPathTailMs (30s) after
  the FIRST +-1R barrier touch -- a RESOLUTION recorder, not a WINDOW recorder. So the tick
  corpus is blind above ~1R, which is where every interesting target lives.

THE TWO TIERS, NEVER BLENDED:
  TIER 1  TICK-TRUE   stop <= 1R and target <= 1R. The whole policy resolves inside the tick
                      path. Walked tick by tick. A real expectancy.
  TIER 2  BAR-BOUNDED anything wider. Resolved from the row milestone ENVELOPES (mfe/mae at
                      1/5/15/60 min). Touch ORDER is only knowable when one side is satisfied
                      at a horizon and the other is not, so each trade lands in
                      DEFINITE-TARGET / DEFINITE-STOP / AMBIGUOUS / OPEN, and the result is a
                      [lower, upper] BOUND with the ambiguous fraction stated out loud.

  A Tier-2 row with a wide bound is not a ranking. It is the corpus saying "ask me again after
  the D2 bake" (tail ~= 3600000 ms, Recorder v2.5.0).

DATA PATH: sentinel.db, READ-ONLY (the ingester owns it; an analyzer that could write is a
second writer waiting to happen).

R UNIT: R = `barrier_ticks`, the recorder's own ATR barrier resolved at fire. `barsToStopR` /
`barsToTargetR` are first-touch of exactly -+1 x barrier_ticks (recorder v2_0_0.cs:413-414),
-1 = never. Everything here is denominated in that same R so the sim and the corpus agree by
construction.

COST: commission is REAL and these expectancies are small. $4.36/RT on NQ at $5.00/tick =
0.872 ticks round turn, which is 0.872/barrier_ticks in R -- a different R-cost per trade,
because the barrier is ATR-scaled. Applied per trade, never as a flat R constant.
```

