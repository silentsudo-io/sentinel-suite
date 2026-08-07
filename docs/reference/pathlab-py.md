# pathlab.py

> `Sentinel/Lab/pathlab.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 470 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
pathlab — tick-true PATH & EXIT-POLICY analysis over the Sentinel tick sidecar.

The excursion corpus (`Excursions\{council\1.4, candidates\cand.1}\*.jsonl`) records the
first-touch LABEL on a symmetric ATR barrier — a deliberately lossy 1-bit summary. The tick
SIDECAR (`Excursions\{council,candidates}\ticks\*.jsonl`) records the full millisecond price
PATH per trade + a pre-computed header fingerprint. This tool reads the paths and answers the
real question: how much path structure is sitting there waiting to be harvested by trade
management, and HOW should management be applied per cohort.

Three rungs (see NOW.md clock-edge thesis):
  1. characterize path ARCHETYPES per cohort (heat/run timing + magnitude)   [descriptive]
  2. score EXIT POLICIES tick-true over the sidecar -> expectancy per cohort  [the lift]
  3. condition management on ENTRY CONTEXT (does context predict archetype?)  [the payoff]

DISPLAY uses friendly speed labels (sentinel_lab.bartag). Raw scope stays the machine key.
Read-only. No NinjaTrader. Fill model = point-per-tick, stop/target fill AT the level touched
(no intrabar path between recorded ticks), timeout = mark-to-last.
```

