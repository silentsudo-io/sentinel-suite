# council_paths.py

> `Sentinel/Lab/council_paths.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 239 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
Council PATH-QUALITY analysis - does conviction buy better-SHAPED trades?

The redirection thesis (memory sentinel-ml-lab): "conviction is noise" (OOF AUC ~0.477) was
computed against a COARSE win/loss binary that discards the trade PATH. Two fires with the same
first-touch label can have opposite shapes - clean run vs chop, heat-before-it-worked, quick vs
slow to target, how much of the peak was given back. If higher conviction buys better-SHAPED
paths even at a similar win rate, that's real edge the binary can't see. This measures the path.

Reads Sentinel\\Lab\\db\\sentinel.db (source='council', populated by ingest\\ingest.py from the
Recorder's council\\ticks\\ sidecars). Computes per-fire path features from the raw tick path,
then relates CONVICTION to them, PER scope/bartype (never pool bartypes - the Council's edge is
bar-type-dependent; memory sentinel-ml-lab).

    python council_paths.py                              # every council fire, grouped by scope
    python council_paths.py --inst GC --bartype 212201v6x24
    python council_paths.py --scope GC.2016v2x8          # one scope
    python council_paths.py --buckets quantile           # 4 conviction quantiles instead of LOW/MID/HIGH

Run via the Lab venv (has numpy/pandas): Sentinel\\Lab\\.venv\\Scripts\\python.exe council_paths.py
```

