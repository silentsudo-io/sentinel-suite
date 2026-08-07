# gate.py

> `Sentinel/Azimuth/bars/gate.py`

| | |
|---|---|
| **Family** | Azimuth (Python) |
| **Version** | — |
| **Size** | 270 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | [SENTINEL_AZIMUTH_SPEC](../../SENTINEL_AZIMUTH_SPEC.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
The §2 parity gate for any registered bar type. One driver, every port.

    C:\\ntbv\\Scripts\\python.exe -m bars.gate --bartype renko --instrument "GC 02-26" \\
        --session 2025-12-09 --param brick_ticks=1 --param tick_size=0.1

Exit codes are `gates`': 0 PASS, 1 FAIL, 2 ABORT. An ABORT is not a soft pass -- a
missing reference side, a session the chart never loaded, and a tape without its
sidecar all land here on purpose.

WHY THE PYTHON SIDE IS BUILT OVER EVERY SESSION AND THEN SLICED
--------------------------------------------------------------
NinjaTrader's chart holds several trading days at once, and stock Renko's session
handling REACHES BACKWARDS: on a new trading day it removes the previous session's
last bar and re-adds it flattened to a doji. Build one session in isolation and that
bar stays a brick, so the two sides differ on exactly one bar per session for a reason
that has nothing to do with the port. Building the whole tape and slicing reproduces
the chart's own context.
```

