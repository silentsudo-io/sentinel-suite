# inject.py

> `Sentinel/Azimuth/gates/inject.py`

| | |
|---|---|
| **Family** | Azimuth (Python) |
| **Version** | — |
| **Size** | 418 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | [SENTINEL_AZIMUTH_SPEC](../../SENTINEL_AZIMUTH_SPEC.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
inject — the proof that each artefact's gate CAN fail.

    "A gate that has never failed is not a gate." -- SENTINEL_AZIMUTH_SPEC §2

`gate3.py` earned its authority by being proven four ways before anyone trusted a PASS out of it.
This module is the same discipline, applied to every artefact kind in the registry: for each kind
it builds a pair of identical sides, then damages one of them in a named, minimal way and asserts
the verdict the law requires.

    identical              -> PASS   (0)   the gate does not cry wolf
    mutated_field          -> FAIL   (1)   one gate field, by 1e-9. EXACT means EXACT.
    missing_row            -> FAIL   (1)   a record the port never produced
    extra_row              -> FAIL   (1)   a record the port invented
    identity_skew          -> ABORT  (2)   different tape / instrument / model = different experiment
    empty_side             -> ABORT  (2)   never a PASS, whatever the other side holds
    row_identity_skew      -> ABORT  (2)   the per-row version of the same thing
    provenance_missing     -> ABORT  (2)   a verdict that cannot name what it blessed
    unkeyable_row          -> ABORT  (2)   a row that cannot be paired must not vanish
    vacuous_side           -> ABORT  (2)   pairing keys and nothing else is not a PASS
    noted_only             -> PASS   (0)   per-run ids differ; that is expected, not drift
    tol_within             -> PASS(DEGRADED)  an override is allowed and is STAMPED
    tol_exceeded           -> FAIL   (1)   an override does not become a blindfold
    group_size             -> FAIL   (1)   a same-key group whose member count differs
    nan_all_field          -> ABORT  (2)   NaN==NaN on 100% of rows tested nothing
    nan_partial_field      -> PASS   (0)   some rows undefined is legitimate -- and is COUNTED
    forbidden_pair_key     -> SpecError     `fireId` can never become a key by accident

Run it:  python -m gates selftest        (or `pytest test_parity.py`, which calls straight in)
```

