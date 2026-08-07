# gate3.py

> `Sentinel/Lab/gate3.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 646 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
gate3 — did lab-host reproduce legacy-node's replay cell, TRADE FOR TRADE?

WHY THIS EXISTS
---------------
the fleet is six replay workers whose entire value is throughput. Throughput is worthless if the
answers changed on the way: *"a faster box that changes results is not a faster box, it is a
different experiment."* Gate 3 is the acceptance test that stands between the fleet and every cell
we would ever run on it, and it is the same discipline as the Keel equivalence gate.

    python gate3.py cell                                    # the pre-registered Gate 3 cell
    python gate3.py collect --node worker-1 --cell G3        # snapshot that node's cell output
    python gate3.py collect --node legacy-node   --cell G3
    python gate3.py compare --cell G3 --ref legacy-node --node worker-1

WHAT IT ACTUALLY COMPARES
-------------------------
A cell's output is one JSONL per fire, whose FIRST LINE is the record (`ctick.*` tick-path headers,
`cand.*` / schema-1.x excursion rows). Those headers carry the entry, the context the strategy saw,
and the realised excursion. Two nodes running identical code over identical replay data must produce
the same set, field for field.

THE THREE TIERS, AND WHY THEY ARE NOT ONE TIER
----------------------------------------------
  PRECONDITION  recVer / coreVer / barLabel / inst / bartype.  A mismatch here means you compared
                two DIFFERENT EXPERIMENTS, so the honest answer is neither pass nor fail — it is
                ABORT (exit 2). Reporting "FAIL" would invite someone to go debug determinism when
                the real defect is that one node is running old code.
  GATE          the behaviour fields.  Zero tolerance.  Any difference = FAIL (exit 1).
  NOTED         printed, never fails: the `fireId` sequence number (a PER-RUN counter, never a
                cross-run key -- see episode-id-not-a-cross-run-key) and the tick-path length.

PAIRING, AND THE TRAP IN IT
---------------------------
Trades are paired on (fireTime, dir, signal) -- NOT on `fireId`, whose trailing counter is per-run.
Two fires genuinely can share a stamp (seen in the corpus: `..._GC_S_2` and `..._GC_S_3` at the same
`fireTime`), so a key can hold several records; those are paired in sequence order within the group,
and a group whose SIZE differs is a failure even though every member matched something.

WHAT WOULD MAKE THIS GATE LIE, AND WHAT IS DONE ABOUT IT
--------------------------------------------------------
  * Comparing two different trees.  `--ref`/`--node` tree hashes are checked via muster before any
    trade is read; a mismatch ABORTS.  `--no-tree-check` exists and says what it is overriding.
  * An empty side.  Zero trades on either node is ABORT, never PASS -- a node that never ran and a
    node that ran and found nothing look identical from here, the same shape as *a crashed sensor is
    indistinguishable from a quiet one*.
  * Leading with counts.  1,488 vs 1,488 can be two disjoint sets.  The summary leads with
    matched/differing and prints counts underneath.
  * A tolerance quietly creeping in.  `--tol-ticks` exists for diagnosis; using it stamps
    DEGRADED on the verdict and the verdict file, because a gate passed with a tolerance is not the
    gate this project agreed to.

Exit codes:  0 = PASS   1 = FAIL   2 = could not run the test (abort / precondition)
```

