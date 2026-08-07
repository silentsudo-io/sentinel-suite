# flowscale.py

> `Sentinel/Lab/harness/flowscale.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 259 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
flowscale — is signed order flow a random walk, and does the bar-count ladder actually say so?

WHY
---
The Tide size sweep on 2025-12-16 produced ~3x more bars per halving of the quantum, where a
DIFFUSION would give 4x (lattice crossings scale as 1/Δ²). Read naively that implies a persistent
flow path with Hurst H ≈ 0.62 -- and if true it matters more than any voter, because it would mean
the aggression path itself carries structure that is not a function of the OHLC it produced.

BUT THE NAIVE READING IS PROBABLY WRONG, AND THIS EXISTS TO FIND OUT
--------------------------------------------------------------------
A crossing count has TWO regimes and they bracket the observation:

  * Δ >> print size  -- the diffusive regime. Crossings ∝ V/Δ², so halving Δ gives 4x.
  * Δ ~  print size  -- the discrete regime. A single print jumps several lattice lines at once, so
                        crossings saturate at (total |signed volume|)/Δ ∝ 1/Δ, and halving gives 2x.

Anything measured BETWEEN those scales lands between 2x and 4x for purely mechanical reasons, with
no persistence involved at all. And the measured ratios already lean that way: 2.95, 3.04, 3.34 --
RISING with Δ, exactly the shape of a crossover toward the diffusive limit, not the flat line a
constant Hurst exponent would produce.

So the bar-count ladder cannot answer the question. It confounds the property of the tape with the
geometry of the measuring instrument. This module therefore runs two tests:

  (a) THE LADDER, with LOCAL slopes per adjacent pair, so a crossover is visible as a trend rather
      than being averaged into one misleading number.
  (b) VARIANCE-OF-INCREMENTS SCALING in TRADE TIME, which has no lattice in it at all:
          Var[CVD(i+n) - CVD(i)] ∝ n^(2H)
      Independent of Δ, so it cannot be fooled by the crossover. Trade time (n prints) rather than
      wall time is the natural clock for flow and removes the intraday activity cycle for free.

READING THE RESULT
------------------
  (a) drifts AND (b) says H ≈ 0.5  ->  the 3x was an instrument artefact. Flow is a random walk at
                                       these scales and the sweep says nothing about edge.
  (a) flat  AND (b) says H > 0.5   ->  persistent flow, and worth real work.
  they disagree                    ->  trust (b); it is the one without the confound.

Deliberately label-free: no outcomes, no holdout, nothing to overfit. It measures the tape's own
geometry, which is the one class of result that cheap search cannot manufacture.
```

