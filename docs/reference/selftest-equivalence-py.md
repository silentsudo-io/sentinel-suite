# selftest_equivalence.py

> `Sentinel/Lab/harness/selftest_equivalence.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 184 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
selftest_equivalence — prove the equivalence gate can FAIL before we trust it to PASS.

A differ that has only ever been run on data it agrees with is not evidence of anything. The
2026-07-24 lesson was exactly this shape: `verify_votes.py` windowed on fireTime, so it silently
skipped the entire replay corpus it was built to audit and would have passed the very bake it
existed to catch. It went green on good data only after that was found. Same discipline here --
build the answer key from the harness itself (so a PASS is guaranteed), then INJECT known faults
one at a time and require the gate to catch each one, at the right bar, for the right reason.

Faults injected:
  1. price   -- one bar's close moved by one tick        -> FAIL, mid-session, "price:"
  2. time    -- one bar's close time moved by 5 ms       -> FAIL, mid-session, "time off by"
  3. missing -- one bar deleted                          -> FAIL (count mismatch and/or shift)
  4. size    -- answer key built at a different quantum  -> FAIL at bar 1 (structural, not local)

Fault 4 is the important one: it is the failure shape that means "everything moved", and the gate
must distinguish it from a single mis-signed print. If 1 and 4 look the same in the report, the
report cannot direct the debugging and is worth little.

Run:  python -m harness.selftest_equivalence [--csv-dir DIR] [--session YYYY-MM-DD]
```

