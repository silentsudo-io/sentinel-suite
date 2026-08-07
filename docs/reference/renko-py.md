# renko.py

> `Sentinel/Azimuth/bars/renko.py`

| | |
|---|---|
| **Family** | Azimuth (Python) |
| **Version** | — |
| **Size** | 327 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | [SENTINEL_AZIMUTH_SPEC](../../SENTINEL_AZIMUTH_SPEC.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
Stock NinjaTrader Renko, ported from `bin\\Custom\\BarsTypes\\@RenkoBarsType.cs`.

FIRST PORT ON PURPOSE. `11v1x1` is "Renko 1/1" and at 128,486 corpus rows it is the
largest bartag we hold; it is also stock NinjaTrader, so its construction is fixed
source rather than a Sentinel design that could move under the gate.

WHAT THE C# ACTUALLY DOES -- the five things a from-memory Renko gets wrong
--------------------------------------------------------------------------
1. A COMPLETED BRICK HAS NO WICKS. `AddBar(renkoHigh - offset, Math.Max(renkoHigh -
   offset, renkoHigh), Math.Min(...), renkoHigh, ...)` collapses to exactly
   O=RH-off, H=RH, L=RH-off, C=RH. Whatever the forming bar had reached is REMOVED
   (`RemoveLastBar`) and rewritten. Textbook Renko draws wicks; NT's does not.

2. THE BRICK'S TIMESTAMP AND VOLUME ARE THE FORMING BAR'S, NOT THE BREAKING TICK'S.
   The rewrite passes `barTime` and `barVolume` -- read off the bar BEFORE this data
   point. So a brick closes at the time of the PREVIOUS tick, and the breaking tick's
   volume goes into the NEXT bar. Stamping the brick with the tick that completed it
   is the obvious implementation and it is wrong.

3. A PRICE JUMP EMITS BRICKS THAT CONTAIN NO TICKS AT ALL. The `while` loop adds
   gap-fill bricks with `volume = 0`. At brick size 1 tick on GC these are common,
   not exotic -- and they are why `end_idx` can repeat (see `series.py`).

4. THE LAST BAR OF A SESSION IS FLATTENED TO A DOJI. On a new trading day with
   `IsResetOnNewTradingDay`, the previous bar is removed and re-added as
   O=H=L=C=its close, keeping its time and volume. The session's final bar is
   therefore never a brick.

5. THE REVERSAL DISTANCE IS TWO BRICKS. After an up-brick closing at RH,
   `renkoLow = RH - 2*offset` and `renkoHigh = RH + offset`; the new forming bar
   opens at RH. One brick up, two bricks down to reverse.

Also faithful: the forming bar is a real bar in the series (updated in place by
`UpdateBar`, which takes max/min into the high/low and ADDS volume), and the bar
timestamp is `max(tick_time, bar_time)` -- NT never lets a bar stamp go backwards.

NOT PORTED, deliberately: the `renkoHigh.ApproxCompare(0.0) == 0` restoration block
(lines 71-88 of the C#). Those fields are only zero before the first data point of a
series, and the `bars.Count == 0` branch above has already handled that case. In a
replay from the start of a tape the block is unreachable; a state-restore path that
cannot be exercised offline must not be guessed at. `_STATE_RESTORE_UNREACHABLE`
asserts it stayed that way instead of leaving the claim in a comment.

FLOATING POINT: all boundary arithmetic is done in INTEGER TICKS. The C# compares
with `ApproxCompare`, an epsilon comparison that exists precisely because
`renkoHigh += offset` accumulates error over thousands of bricks. Integers remove the
question rather than tune an epsilon, and prices are on the tick grid by construction
(enforced below -- a price off the grid is an error, not something to round away).

⛔ AND THE CONVERSION BACK OUT IS A DIVISION. Integer geometry buys nothing if the
last step multiplies: the FIRST real gate run against NinjaTrader's own dump (GC 02-26,
2025-12-10, 94,108 bars on both sides, every boundary agreeing) still FAILED on 37,765
records, all of them one ULP high, because this module ended with `ticks * tick_size`
and `42379 * 0.1` is `4237.900000000001` while `42379 / 10` is `4237.9`. `ticks_to_price`
(series.py) does the crossing once, by dividing through the tick size's exact rational
form. Do not reintroduce a scale factor, and do not "fix" a recurrence of this with a
tolerance -- the gate is EXACT so that a systematically-wrong price cannot reach the
engine wearing a passing verdict.
```

