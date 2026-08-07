---
layout: sentinel-ref
title: "parity.py"
blurb: "Azimuth (Python) · unversioned · 719 lines"
---

# parity.py

> `Sentinel/Azimuth/gates/parity.py`

| | |
|---|---|
| **Family** | Azimuth (Python) |
| **Version** | — |
| **Size** | 719 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | [SENTINEL_AZIMUTH_SPEC](../../SENTINEL_AZIMUTH_SPEC.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
parity — the Azimuth's equivalence harness (SENTINEL_AZIMUTH_SPEC §2, THE PARITY LAW).

WHY THIS EXISTS
---------------
The Azimuth re-implements Sentinel bar types, sensors, the Council and strategies in Python
alongside the NinjaScript originals. **Two implementations of one definition can silently
disagree** — and a research surface that computes something slightly different from the live
system is researching a different system. §2 is the law that makes the second column safe:

    Anything implemented in both columns must pass an equivalence gate before the Python side
    is trusted for research. No exceptions, no "we'll gate it later."

This module is `Lab\\gate3.py` generalised from *one strategy, two boxes* to *any Sentinel
artefact, two columns*. Everything below that reads like paranoia was paid for once already;
the provenance is named at each rule.

THE THREE TIERS, AND WHY THEY ARE NOT ONE TIER  (gate3, verbatim discipline)
---------------------------------------------------------------------------
  PRECONDITION  identity + provenance: the tape, the instrument, the session, the scope, the
                model — and, per row, the identity fields. A mismatch here means you compared
                two DIFFERENT EXPERIMENTS, so the honest answer is neither pass nor fail; it is
                ABORT (exit 2). Reporting FAIL would send someone to debug a port when the real
                defect is that the two sides read different tape.
  GATE          the behaviour fields. A difference on any of them = FAIL (exit 1).
  NOTED         printed, never fails: per-run counters and ids, and `updated_utc` — a seam
                stamp that is *known* to carry no as-of semantics (see the memory
                `state-seam-freshness-heartbeat` / the lookahead poisoning of the corpus).

WHAT WOULD MAKE THIS GATE LIE, AND WHAT IS DONE ABOUT IT
--------------------------------------------------------
  * **An empty side.** Zero rows on either side is ABORT, never PASS. A port that never ran and
    a port that ran and produced nothing are indistinguishable from here — the same shape as
    *a crashed sensor is indistinguishable from a quiet one*.
  * **A vacuous side.** Two sides that carry the pairing keys and none of the compared fields
    would pair perfectly and PASS. `required_fields` makes that an ABORT. (New here; gate3
    never needed it because both its sides were the same code emitting the same schema.)
  * **Leading with counts.** 1,488 vs 1,488 can be two disjoint sets. The summary leads with
    matched/differing and prints counts underneath, labelled as not being the test.
  * **Pairing on a per-run id.** `fireId` / `episode_id` are NOT cross-run keys
    (`episode-id-not-a-cross-run-key`). A spec that names one as a pairing key raises
    `SpecError` — the trap is closed structurally, not by remembering.
  * **A tolerance quietly creeping in.** Every compared field declares its tolerance, and the
    declaration is printed on every run. Loosening one at the command line stamps DEGRADED on
    the verdict and on the verdict file, because a gate passed with a tolerance is not the gate.
  * **A gate that has never failed.** `inject.py` proves each artefact kind CAN fail, six ways.

EXIT CODES:  0 = PASS   1 = FAIL   2 = could not run the test (abort / precondition)
```

