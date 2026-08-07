---
layout: sentinel-ref
title: "limitlab.py"
blurb: "Lab (Python) · unversioned · 171 lines"
---

# limitlab.py

> `Sentinel/Lab/limitlab.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 171 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
limitlab -- does a RESTING LIMIT at the brick boundary beat a MARKET order at the fire?

    cd "Sentinel\\Lab"
    .\\.venv\\Scripts\\python.exe limitlab.py                 # all lanes
    .\\.venv\\Scripts\\python.exe limitlab.py --queue 0,1,5,10,25,50
    .\\.venv\\Scripts\\python.exe limitlab.py --holdout 0.30 --target 30 --stop 30

WHAT THIS ANSWERS, AND WHY IT IS NOT THE ORIGINAL QUESTION
  The limit-level bar was specced to recover a "~9-tick entry bleed". That bleed turned out to be a
  MEASUREMENT ARTIFACT -- `firePx` used to be the Heikin-Ashi synthetic close, a price that never traded
  (memory: firepx-is-synthetic-ha-close). There are no 9 ticks to recover.
  A second premise also died: the spec claimed TBars re-derives its boundaries every tick, which would make a
  resting order impossible to maintain. FALSE -- barMax/barMin are assigned only at bar CREATION, so the
  boundaries are already latched, and BrickState has been publishing them per tick all along.

  So the surviving question is narrow and purely quantitative:

      You decide at a brick close. A market order fills at `firePx`.
      A resting limit at the FORMING bar's boundary (`brkUpper`/`brkLower`, both known in advance)
      fills at a KNOWN price -- but only sometimes.
      Does the better price pay for the trades you never get?

  The prize is the crossing cost (~1 tick/side), NOT 9 ticks. This is a tight race by construction, and the
  honest output is a CURVE over queue depth, not a single number.

THE FILL MODEL (conservative by default)
  Resting BUY at L fills when the tape prints at or below L; SELL at L when it prints at or above L.
  `--queue Q` additionally requires Q contracts to print through the level before you are filled -- we have no
  book depth in the corpus, so Q is approximated as Q tick-prints at-or-through L. Q=0 is the OPTIMISTIC
  touch-fill bound and should never be quoted alone.
  ⚠ A limit backtest that fills on touch is the same class of lie as bar-level excursion (81% -> 37.5% at tick
  resolution). Report the decay with Q; that decay IS the finding.

SELECTION BIAS IS REPORTED, NEVER ASSUMED AWAY
  Unfilled trades are not free -- they are disproportionately the fast ones, i.e. the winners. So the table
  prints fill rate, expectancy ON FILLS, and the market-order expectancy OF THE TRADES THE LIMIT MISSED.
  If the missed set is better than the filled set, a great expR at 30% fill is a worse business than a
  mediocre one at 90%.
```

