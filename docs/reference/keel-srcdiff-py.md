---
layout: sentinel-ref
title: "keel_srcdiff.py"
blurb: "Lab (Python) · unversioned · 155 lines"
---

# keel_srcdiff.py

> `Sentinel/Lab/keel_srcdiff.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 155 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
keel_srcdiff — is SentinelKeel still a faithful transcription of the frozen control?

WHY THIS EXISTS
---------------
The Keel programme's whole acceptance test is the EQUIVALENCE GATE: with BracketMode=AtrBracket and
default parameters, `SentinelKeel` must produce a trade list identical to `RangeFilterATRStrategy` on
the same data. "Instrumentation that changes behaviour is not instrumentation."

That gate is a REPLAY test and it costs bake time. This is the cheap pre-check that runs in a second
and catches the overwhelmingly likely way the gate breaks: someone edits Keel's signal or order logic
— to fix a defect, to "improve" a comment, to refactor — and the transcription silently drifts from
the control. A drifted Keel does not fail loudly; it produces a plausible trade list that answers a
different question, and every number downstream becomes uninterpretable.

⚠ THIS IS NOT THE EQUIVALENCE GATE. It compares SOURCE, so it proves the two implementations still
say the same thing, never that they DO the same thing. A real gate needs both strategies run over the
same replay data with their trade lists diffed. Passing here and skipping that is precisely the
"improving the fidelity of a scrapped experiment" error in a new costume.

WHAT IT ALLOWS
--------------
Keel is the control plus LEAF instrumentation, so exactly two classes of difference are legal:
  * ADDED lines that are instrumentation calls (whitelist below) — leaves that cannot alter control flow
  * the SetStopLoss/SetProfitTarget pair MOVED into ApplyBracket, called with identical arguments
Anything else is drift and exits 1.

    cd "Sentinel\\Lab" && python keel_srcdiff.py
```

