# test_renko.py

> `Sentinel/Azimuth/bars/test_renko.py`

| | |
|---|---|
| **Family** | Azimuth (Python) |
| **Version** | — |
| **Size** | 594 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | [SENTINEL_AZIMUTH_SPEC](../../SENTINEL_AZIMUTH_SPEC.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
Tests for the Renko port and the `bars` package plumbing.

    C:\\ntbv\\Scripts\\python.exe -m pytest bars\\test_renko.py -q
    C:\\ntbv\\Scripts\\python.exe bars\\test_renko.py          # same proofs, no pytest

Two kinds of test, and both are needed. The HAND WALK is the only thing that pins the
five behaviours the C# actually has (wickless bricks, the forming bar's stamp and
volume, row-less gap bricks, the session doji, the two-brick reversal) -- an invariant
suite would happily pass on a wrong-but-self-consistent Renko. The INVARIANTS over the
real `GC 02-26` tape are what say the port survives 1.5 million rows of real data.

⚠ Neither is the gate. Passing these does NOT mean the port matches NinjaTrader; only
`bars.gate` can say that. It HAS now been run (2026-08-05, all 17 `GC 02-26` sessions
against `20260805T015237__GC__11v1x1.jsonl`) and it does NOT yet pass -- see README.
Everything it caught that this file could have caught is now pinned below.
```

