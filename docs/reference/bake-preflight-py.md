# bake_preflight.py

> `Sentinel/Lab/bake_preflight.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 191 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
bake_preflight - refuse to start a Strategy Analyzer cell that would answer the wrong question.

WHY (Keel test plan gate item 9): swapping the strategy on the SA tab via `nt8bridge configure`
SILENTLY RESETS the commission -- after a swap both templates read
`BacktestCommissionTemplate=null` and `IncludeCommission=False`. The run then completes, reports
healthy, and produces a COMMISSION-FREE trade list. Test-plan Q4 is "what does a fill actually
cost", so a commission-free matrix does not merely lose precision, it answers the question
wrongly while looking perfect. That happened for real during the equivalence gate: the
+4.36 x 318 = 1386.48 delta was commission, not behaviour.

The whole point is that the failure is INVISIBLE at the end. So it has to be caught at the start.

DESIGN RULES, learned from this project's own bugs:
  * A MISSING property is a FAILURE, never a pass. `ok` + `applied: []` is how `configure`
    reported success having done nothing; a checker that treats "couldn't read it" as "fine"
    reproduces that bug one layer up.
  * Exit code is the contract: 0 = safe to run, 1 = do not run. Scriptable as a hard gate.
  * The HOLDOUT is guarded here too. The pre-registered split (EXPLORE = NQ 06-26,
    HOLDOUT = NQ 09-26 2026-06-21..07-17) is only worth something if spending it takes a
    deliberate act, so touching it requires --allow-holdout and says so loudly.

USAGE
  python bake_preflight.py --strategy SentinelKeel_v0_1_0 --instrument "NQ 06-26" \
                           --from 2026-04-19 --to 2026-06-18 --commission "<template name>"
  python bake_preflight.py --require-tick-fill        # corpus bakes must fill at tick resolution
```

