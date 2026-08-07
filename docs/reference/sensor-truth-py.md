---
layout: sentinel-ref
title: "sensor_truth.py"
blurb: "Lab (Python) · unversioned · 161 lines"
---

# sensor_truth.py

> `Sentinel/Lab/sensor_truth.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 161 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
sensor_truth — the SENSOR TRUTH TABLE: grade every voter STANDALONE, tick-true, per bar type.

The 2026-07-22 pivot: the fused Council was scaffolding and does not survive real fills. The question
now is which individual voters carry edge on their own. This answers exactly that and nothing else.

CONSTRUCTION (the part that matters)
  Each council row is one FIRE with a direction `dir` and a tick-true `first_touch` label
  (+1 target-first, -1 stop-first, 0 neither) measured against a symmetric ATR barrier from an
  HONEST entry price (schema 1.5 — see memory firepx-is-synthetic-ha-close).
  A voter V has its own vote v in votes_json. We grade V on ITS OWN call, not on the Council's:
      V is RIGHT  when (v == dir and first_touch == +1) or (v == -dir and first_touch == -1)
      V is WRONG  when (v == dir and first_touch == -1) or (v == -dir and first_touch == +1)
  So V gets credit for being correctly CONTRARIAN on a fire that went against the Council.
  v == 0 is abstention and is excluded from V's sample entirely — absence of evidence is not
  evidence against (see memory state-vs-trigger-voters).

GUARDRAILS (each one is here because it has already burned this project)
  • PER BAR TYPE, always. Base rate by bar type dominates every voter effect [[weight-fit-findings]].
  • 70/30 TIME holdout, split per lane. An in-sample edge here means nothing; the OOF number is the
    number. pathlab already killed an in-sample "best" exit this way.
  • COST BAR. On a symmetric ±1R barrier, expR = 2p-1. GC costs ≈ 0.12R ($4 RT + 1 tick/side), so a
    voter must clear p ≈ 0.56 to be net-profitable. 52% is not an edge, it is noise with good manners.
  • SELECTION BIAS is stated, not hidden: every row is a fire the COUNCIL chose to take, so this
    measures voters conditioned on Council agreement, not on the unconditional tape.

Usage:  python sensor_truth.py [--db PATH] [--min-n 30] [--lane SUBSTR]
```

