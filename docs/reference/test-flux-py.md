# test_flux.py

> `Sentinel/Azimuth/bars/test_flux.py`

| | |
|---|---|
| **Family** | Azimuth (Python) |
| **Version** | — |
| **Size** | 997 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | [SENTINEL_AZIMUTH_SPEC](../../SENTINEL_AZIMUTH_SPEC.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
Tests for the SentinelFlux Python port, over REAL §3.1 tape.

Run:  C:\\ntbv\\Scripts\\python.exe -m pytest bars/test_flux.py -q
 or:  C:\\ntbv\\Scripts\\python.exe bars/test_flux.py        (no pytest needed)

⚠ READ THIS BEFORE QUOTING A NUMBER FROM HERE. Everything below is SELF-CONSISTENCY
evidence: it proves the port does what ``SentinelFlux_v1_0_0.cs`` says it does, on real
GC tape, deterministically. It is **not parity evidence.** No NinjaTrader reference bars
exist for any session this tape covers -- see THE GATE in ``flux.py``'s docstring for the
measurement that establishes that. ``test_gate_is_wired_and_can_fail`` proves the parity
gate is wired and CAN fail; it does not and cannot prove the port is correct.
```

