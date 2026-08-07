# noise_floor.py

> `Sentinel/Lab/harness/noise_floor.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 125 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
noise_floor — how precise is each observable, before anyone interprets a difference in it?

THE MISTAKE THIS PREVENTS, WHICH WAS ALREADY MADE ONCE
------------------------------------------------------
§5.6b of the whitepaper reported that H differs between GC and MGC on matched dates, concluded flow
persistence tracks participant mix, and was published. Two more matched windows flipped the sign. The
"effect" was 0.027 against a window-to-window spread that turned out to be ~0.05 within a single
contract — i.e. it was never resolvable, and nobody had measured the noise floor to notice.

So: measure each observable's WITHIN-CONTRACT, ACROSS-WINDOW dispersion first. That number is the
resolution limit of the instrument. Any between-group difference smaller than it is uninterpretable
no matter how clean the arithmetic looks, and any difference much larger than it is real.

Reported per observable:
    mean, SD across windows, and the DETECTION FLOOR (2 SD) -- the smallest difference worth a
    sentence.

Different observables computed from the SAME data can have wildly different precision. Establishing
that separately is the point: it tells you which of your findings you are entitled to believe.
```

