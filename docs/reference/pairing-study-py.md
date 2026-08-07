# pairing_study.py

> `Sentinel/Lab/harness/pairing_study.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 307 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
pairing_study — the complete read on trade/quote pairing: does it explain the gate, and does it
                   threaten anything we have already claimed?

THE FINDING THIS INVESTIGATES
-----------------------------
A print-by-print trace of the first divergence showed the same price signing +1, then -1, then +1
inside 8 ms, because the quote oscillated underneath it -- and seven prints sharing ONE timestamp
with the quote moving mid-cluster. Quote-rule signing is order-sensitive at sub-millisecond
resolution, the export flattens NinjaTrader's event ordering into row order, and we cannot observe
what that ordering was. So bit-equality may be unreachable for reasons that are not a defect in
either implementation.

That reframes the question from "are we identical?" to two answerable ones:

  Q1  Does some OTHER pairing rule reproduce NinjaTrader? If one jumps to high agreement, we have
      simply been pairing differently, and it is fixable after all.
  Q2  If not -- how much does the choice MATTER? Every flow-derived result we have claimed
      (H ~= 0.60, the sweep fraction, the bar-count ladder) rests on signed flow. If those numbers
      move when the pairing rule changes, they were artefacts of an arbitrary choice and must be
      withdrawn. If they are stable across all four rules, they are properties of the tape and
      survive regardless of how the gate lands.

Q2 is the important one. Q1 only decides how good the harness is at imitation; Q2 decides whether
anything in the whitepaper is true.

THE FOUR RULES
--------------
  stream  quote updated in row order; a trade signs against the most recent quote. (What we ship.)
  pre     every trade in a same-timestamp cluster signs against the quote as it stood BEFORE the
          cluster -- i.e. quotes at timestamp T are not visible to trades at T.
  post    ...against the quote AFTER the whole cluster is applied. The opposite extreme.
  tick    quotes ignored entirely; Lee-Ready tick rule only. Not a pairing at all -- a floor. If a
          result survives even this, it cannot be an artefact of quote handling.

`pre` and `post` BRACKET every possible within-timestamp ordering, so the spread between them is a
genuine bound on the ambiguity, not a sample of it.
```

