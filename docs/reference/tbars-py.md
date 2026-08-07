# tbars.py

> `Sentinel/Azimuth/bars/tbars.py`

| | |
|---|---|
| **Family** | Azimuth (Python) |
| **Version** | — |
| **Size** | 1304 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | [SENTINEL_AZIMUTH_SPEC](../../SENTINEL_AZIMUTH_SPEC.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
SentinelTBars -> Python. The Azimuth's port of `BarsTypes\\SentinelTBars_v1_0_0.cs`.

SPEC: SENTINEL_AZIMUTH_SPEC.md 1 (the two columns) - 2 (THE PARITY LAW) - 3.1 (tape contract).
GATE: artefact kind `bartype`, pairing key `(session, bar_index)`.

    ONE definition, TWO implementations. This file is the second one. It is NOT trusted
    for research until the `bartype` gate has been RUN against NinjaTrader, and as of
    this writing it has NOT been - see "THE GATE'S TRUE STATUS" at the bottom.

WHAT IT IS
----------
SentinelTBars is an adaptive Renko-hybrid: Renko bricks with Heikin-Ashi BODIES and real
price wicks, wrapped in an ATR floor, a breakout-confirmation gate, trend hysteresis, a
per-brick density controller, quiet-hours gating, forced time bricks and micro-splits.
It is a per-TICK state machine and there is no vectorised form of it: every brick's
geometry depends on the ATR and the density scale left behind by the brick before it.

THE PARAMETERS - DECODED FROM THE CODE, NOT FROM THE NAME
---------------------------------------------------------
The corpus bartag is `212201v6x24[@LANE]`. It decomposes as `<barTypeId>v<Value>x<Value2>`
(SentinelCore.BarTag, SentinelCore_v1_0_0.cs). 212201 is SentinelTBars' reserved
BarsPeriodType id.

* `6/24` is NOT two knobs. The chart exposes ONE field - "Speed Settings", which is
  NT's `BaseBarsPeriodValue` renamed by `SetPropertyName` in `State.Configure`. The same
  block derives the pair (SentinelTBars_v1_0_0.cs, State.Configure):

      BarsPeriod.Value  = BaseBarsPeriodValue / 2      # integer division
      BarsPeriod.Value2 = BaseBarsPeriodValue * 2

  so `6/24` == **Speed Settings 12**, and the three offsets that actually drive the bars
  (`LatchConfig`) are:

      baseTrendOffset    = Value                * tickSize =  6 ticks   (with-trend)
      baseReversalOffset = Value2               * tickSize = 24 ticks   (counter-trend)
      baseOpenOffset     = BaseBarsPeriodValue  * tickSize = 12 ticks   (brick body seed)

  Cross-checked against `Lab\\sentinel_lab\\bartag.py::_is_speed`, which classifies a brick
  bar STRUCTURALLY by `Value2 == 4 * Value` and reports `Speed = Value * 2` -> 12. Same
  answer from an independent implementation.

* `@AUD` / `@AUD0826` / `@AUD0626` / `@TEST` / `@STB20FCA` are **LANES, not parameters.**
  A lane is a per-chart scope discriminator: `SentinelCore.ComposeLane` appends `"@" + lane`
  to a bare scope, `SanitizeLane` strips it to alphanumerics, and it is sourced from
  `Sentinel\\Lanes.conf` or the F6 `ScopeLane` property. It exists so two charts on the SAME
  instrument and SAME bars type do not overwrite each other's seams.
  **The bars type never reads it** - `PublishBrickTick`/`LogBrick` call
  `SentinelCore.ScopeOf(bars.Instrument, bars.BarsPeriod)`, the BARE two-part form, with no
  lane overload anywhere in the file. Confirmed by the corpus itself: it carries both
  `212201v6x24` and `212201v6x24@AUD`, which are the same geometry recorded on two charts.
  So `@AUD` changes WHICH ROWS you select, never HOW THE BARS ARE BUILT.
  (`@STB20FCA` sits on `212201v10x40` and `@STB24FCA` on `212201v12x48` - the "20"/"24" in
  the lane name is a human echo of Speed 20 / Speed 24, redundant with the numeric part.
  That redundancy is the tell that the lane is a label.)

WHAT A NAIVE PORT GETS WRONG - every one of these is a real behaviour of the C#
-------------------------------------------------------------------------------
 1. **The closing brick's extreme is CLIPPED to the boundary, the other extreme is not.**
    `CreateBreakoutBar`: `barHigh = overMax ? breakoutPrice : GetHigh(last)`. On an up-brick
    the real high (which by construction exceeded barMax) is DISCARDED and replaced by the
    boundary; the low survives. A port that keeps real OHLC produces different bars.
 2. **`breakoutPrice` is always the boundary**, never the trade price: `min(close, barMax)`
    when close > barMax is barMax. The overshoot is deliberately handed to the next brick.
 3. **A new brick is BORN with a 12-tick body**, not flat. `AddBar(nextHaOpen, nextHigh,
    nextLow, ...)` with `nextLow = syntheticOpen = breakoutPrice - baseOpenOffset*dir`.
    That is what `baseOpenOffset` is for, and it feeds straight into the micro-split ratio.
 4. **Volume is DOUBLE-COUNTED at every brick boundary.** The closing tick's volume is
    passed to `UpdateBar` (which accumulates) AND to `AddBar` for the new brick - and on a
    confirming tick `UpdateExistingBar` runs twice more around the chain. So
    `sum(bar volumes) > sum(tick volumes)`, by construction. Faithfully reproduced.
 5. **`lastBoundaryTouch` does not mean what it says.** It is only ever assigned in
    `CreateBreakoutBar`/`ForceTimeBrick`, i.e. it is "time of last brick". With
    ForceStagnationSeconds=90 > MinBarLifeSeconds=10 the whole `ShouldForceTimeBrick`
    predicate collapses to "90 s since the last brick".
 6. **A slow drift beyond the boundary NEVER prints a breakout brick.** The speed gate is
    `penetrationTicks / elapsedSeconds >= 1.6`, and elapsed is measured from the FIRST tick
    beyond. One tick of penetration after 5 s scores 0.2 and can never recover, because
    `pendingStartTime` is not refreshed while price stays outside. Such a move exits via the
    90-second forced time brick instead. This dominates quiet tape.
 7. **`ForceTimeBrick` emits a candle whose open/close sit OUTSIDE its high/low.**
    `AddBar(bars, haOpen, barOpen, barOpen, haOpen, ...)` -> (open, high, low, close) =
    (haOpen, close, close, haOpen). high == low == the real price, open == close == the HA
    value. Not a typo in the port; it is what the C# does.
 8. **`ForceTimeBrick` flips `barDirection` without touching `sameDirCount`**, so the
    hysteresis run-length survives a direction change through that path.
 9. **The HA chain is re-anchored every tick.** `UpdateExistingBar` ends with
    `haPrevOpen = bars.GetOpen(last)` - the bar's actual open, discarding the smoothed
    value. And `CreateBreakoutBar` applies `GetHeikinAshiOpen` TWICE (once into `haPrevOpen`,
    then again to form `nextHaOpen`). Both reproduced literally.
10. **The `backInside` branch is DEAD CODE.** It is only reached when `overMax || underMin`
    against unchanged barMax/barMin, so `backInside` is always false. Reproduced as written
    rather than "fixed", because fixing it changes the bars.
11. **`InitializeFirstBar` does not round `barMax`/`barMin` to tick size**, unlike every
    other assignment to them.
12. **Quiet hours read `DateTime.Hour` of the platform's display timezone**, not UTC. The
    tape is UTC ms. See `TBarsParams.timezone` - this is a PARAMETER, and getting it wrong
    silently changes the confirmation thresholds for 5 hours of every session.
13. **`ConfirmTicksBeyond = 1` actually requires TWO ticks of penetration**, because
    `Math.Abs((pendingFarthest - pendingBoundary) / tickSize)` on a genuine one-tick move
    evaluates to **0.999999999994543**, not 1.0, and the test is `penetrationTicks <
    ticksThresh -> reject`. Measured, not assumed. A port that "tidies" this into
    `round((farthest - boundary) / tick)` confirms a tick earlier than NinjaTrader on every
    breakout and is a different bar type. Reproduced by using the same expression.
14. **DIVIDE by tickSize; never multiply by its reciprocal.** For binary64 `x / 0.1` and
    `x * 10.0` differ at the last ULP on ~18% of values (measured over 200,000 samples).
    The C# divides, so this does. The `bartype` gate declares EXACT (0.0) - one ULP FAILS.

THE ENGINE SEAM
---------------
`engine\\bars.py::bars_from_end_idx(tape, end_idx)` is the ONE interface (spec 4). This
module produces `end_idx`; it does not invent a second seam. Note the split:

  * `TBarsSeries.close_row` - the tape row that closed each NATIVE brick. That IS `end_idx`,
    handed to the seam unchanged. It is NON-DECREASING, not strictly increasing: chaining and
    micro-splits close several bricks on ONE tick, and the seam represents those as row-less
    bars. Collapsing them would renumber every later bar and move the gate's
    `(session, bar_index)` coordinate, so they are passed through and COUNTED
    (`duplicate_end_idx`).
  * `to_bars` hands the NATIVE HA/Renko OHLCV through the seam's keyword overrides, because
    a brick LEVEL and a Heikin-Ashi body are not tape prices and must not be re-derived.

TAPE
----
Reads the 3.1 parquet directly rather than through `engine.contract.load_session`, because
`validate()` refuses the real `GC 02-26` tape over 3.2's crossed quotes (140 rows on
2025-12-09) and TBars is BuiltFrom=Tick - it never touches bid/ask, so a crossed book cannot
affect a single brick. The crossed count is COUNTED AND RETURNED, never dropped silently.
```

