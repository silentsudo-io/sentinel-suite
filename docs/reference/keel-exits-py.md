# keel_exits.py

> `Sentinel/Lab/keel_exits.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 290 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
keel_exits - the D1 GIVEBACK CURVE and the D2 KNOCKOUT measure, over the recorded corpus.

WHY THIS EXISTS (NOW.md, "TWO EXIT DEFECTS, NOT ONE"):
  D1 GIVEBACK  a winner reaches a decent MFE, decays, and dies on a full stop.
  D2 KNOCKOUT  the stop fills INSIDE a live trend and there is no way back in, so the rest of
               that trend is forfeited. D2 is STRUCTURAL: `trendState` only changes on the
               filter's own motion, so a stop-out while the filter still points the trade's way
               leaves the strategy flat with no re-entry until the filter turns and turns back.

  These are DIFFERENT defects and a BE+ trigger cannot fix D2 at all. Measuring only D1 would
  tune a breakeven level against losses it structurally cannot address, then read the poor
  result as "BE+ doesn't help" -- a wrong conclusion from a correct experiment. So this tool
  reports them SEPARATELY and refuses to blend them into one "exit quality" number.

THE DELIVERABLE IS A CURVE, NOT A LEVEL. Guessing a BE+ threshold and then measuring the guess
makes a bad level and a bad idea indistinguishable. For each arm level we report the PAIRED
decomposition -- same path, two policies -- so the trade-off is legible:
    SAVED      baseline took a full stop; BE+ got out at/near scratch      (BE+ helped)
    SCRATCHED  baseline was a WINNER; BE+ armed, price retraced, scratched (BE+ cost us)
    UNCHANGED  same outcome either way
The crossover -- where SAVED gains stop outrunning SCRATCHED losses -- is the answer.
A BE+ trigger is a TRADE, not a free win.

DATA PATH: reads sentinel.db (canon: tools -> JSONL -> ingester -> DB -> analyzer), read-only.
  D1 runs TICK-TRUE off the `ticks` sidecar path (ms, fav_t).
  D2 runs off the ROW milestones (mfe/mae at 1/5/15/60 min + barsToMFE/barsToStopR), because
  the tick path is capped by TickPathMaxMs and would UNDER-count a long trend by construction.

R UNIT: R = `barrier_ticks`, the recorder's own barrier. That is the unit `ms_to_stop_r` and
`ms_to_target_r` are already defined against, so the policy sim and the corpus agree by
construction. Baseline bracket is expressed in that R via --stop-r / --target-r.
```

