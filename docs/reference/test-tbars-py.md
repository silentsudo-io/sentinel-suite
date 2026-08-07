# test_tbars.py

> `Sentinel/Azimuth/bars/test_tbars.py`

| | |
|---|---|
| **Family** | Azimuth (Python) |
| **Version** | — |
| **Size** | 545 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | [SENTINEL_AZIMUTH_SPEC](../../SENTINEL_AZIMUTH_SPEC.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
Tests for the SentinelTBars Python port (`bars/tbars.py`).

    C:\\ntbv\\Scripts\\python.exe -m pytest bars\\test_tbars.py -q      # from Sentinel\\Azimuth
    C:\\ntbv\\Scripts\\python.exe bars\\test_tbars.py                   # same proofs, no pytest

⚠ READ THIS BEFORE READING A GREEN RUN AS PARITY.
These tests prove the port is INTERNALLY consistent, deterministic, faithful to the specific
edge cases enumerated in `tbars.py`, and that its `bartype` gate is wired and CAN FAIL. They
prove NOTHING about agreement with NinjaTrader, because no NT reference side exists - see
"THE GATE'S TRUE STATUS" at the foot of `tbars.py`. Per spec §2 the port is NOT TRUSTED for
research until that gate is RUN.
```

