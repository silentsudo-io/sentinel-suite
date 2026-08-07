# test_parity.py

> `Sentinel/Azimuth/gates/test_parity.py`

| | |
|---|---|
| **Family** | Azimuth (Python) |
| **Version** | — |
| **Size** | 355 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | [SENTINEL_AZIMUTH_SPEC](../../SENTINEL_AZIMUTH_SPEC.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
test_parity — the proofs. `A gate that has never failed is not a gate.` (SPEC §2)

    cd "Sentinel\\Azimuth"
    C:\\ntbv\\Scripts\\python.exe -m pytest gates\\test_parity.py -q
    C:\\ntbv\\Scripts\\python.exe gates\\test_parity.py          # same proofs, no pytest

Every artefact kind in the registry is put through the six §2 failure modes plus eight more that
`gate3.py` learned the hard way. A kind added to `artefacts.py` without a fixture in `inject.py`
FAILS here rather than being skipped -- an ungated port must not be able to arrive quietly.
```

