# selfdiff.py

> `Sentinel/Lab/harness/selfdiff.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 203 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
selfdiff — is NinjaTrader DETERMINISTIC? Diff two SentinelBarDump files of the same tape.

WHY THIS IS THE DECISIVE TEST
-----------------------------
The equivalence gate says the harness reproduces 39% of NinjaTrader's bars exactly, and the
print-by-print trace showed why: bar boundaries depend on the ordering of quote and trade events
sharing a timestamp, an ordering the export flattens away. The pairing study then showed no
recoverable rule closes the gap.

But that whole line of reasoning silently assumes NinjaTrader is REPRODUCIBLE -- that the 61% is
our error against a fixed answer. If NinjaTrader's own event ordering varies between runs, there
is no fixed answer, and part of that 61% is not an error at all.

This removes our signing rule from the experiment entirely. Two rebuilds of the same tape by the
same program. Nothing in between.

  IDENTICAL      NinjaTrader is deterministic. The whole gap is ours, there IS a fixed target, and
                 bit-equality is worth continuing to chase.
  NOT IDENTICAL  NinjaTrader's bars carry run-to-run noise. The existing corpus inherits it, part
                 of the 61% is irreducible, and "replace NinjaTrader" stops being a preference
                 about speed and becomes an argument about correctness.

Compares only bars built on the SAME path (historical rebuild by default) and only sessions both
files cover from the session open, for the same reason the harness gate does: a session joined
part-way through has a different lattice anchor and is incomparable rather than wrong.
```

