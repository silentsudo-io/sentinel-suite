---
layout: sentinel-ref
title: "cvddrift.py"
blurb: "Lab (Python) · unversioned · 212 lines"
---

# cvddrift.py

> `Sentinel/Lab/harness/cvddrift.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 212 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
cvddrift — localise WHY the harness and NinjaTrader disagree, down to the individual print.

The equivalence gate reports 68% boundary agreement / 39% full agreement with a consistent bias:
the harness prints ~1% MORE bars every session, i.e. its CVD travels slightly further. That is a
sub-contract difference in the flow accumulator. This finds it.

TWO MODES, and they answer different questions.

  --residuals   Sample the harness's session CVD at each of NinjaTrader's bar-close timestamps.
                If the two implementations were identical, NinjaTrader's closes would land exactly
                on our lattice lines, so `cvd mod deltaPerBrick` would sit near zero (within one
                print's overshoot) forever. Watching that residual, and the running bar-count
                difference, separates the two candidate causes:
                  * residual grows steadily, bar-count difference climbs linearly
                       => a SCALING error. Every print is slightly too big: winsorization, or a
                          volume field we are reading differently.
                  * residual wanders, bar-count difference random-walks
                       => SIGN FLIPS. Individual prints are being attributed to the wrong side,
                          which points at quote/trade pairing rather than magnitude.

  --trace A B   Print-by-print trace between two timestamps: raw volume, the winsorized volume
                actually applied, prevailing bid/ask, the resulting sign, and running CVD, with the
                lattice crossings marked. Run it over the window containing the FIRST divergence
                and the offending print is visible directly. There is no inference left at that
                point -- you are reading the tape the decision was made on.

Timestamps are UTC, matching the SentinelBarDump rows ("HH:MM:SS" or full ISO).
```

