# test_zero_row_bars.py

> `Sentinel/Azimuth/engine/tests/test_zero_row_bars.py`

| | |
|---|---|
| **Family** | Azimuth (Python) |
| **Version** | — |
| **Size** | 227 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | [SENTINEL_AZIMUTH_SPEC](../../SENTINEL_AZIMUTH_SPEC.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
Zero-row bars — one tape row closing several bars.

Renko, brick and range clocks print SEVERAL bars from ONE tick when price jumps
far enough to break multiple levels at once. Measured on real tape, 35.7% of
Renko 1/1 bars are row-less, and Renko 1/1 is the largest bartag in the corpus.

The contract: `end_idx` is NON-DECREASING; a zero-row interval offers NO fill
opportunity; a decision taken at a zero-row bar's close CARRIES FORWARD to the
next interval that has rows. A DECREASING `end_idx` is still malformed.
```

