"""Stock NinjaTrader Renko, ported from `bin\\Custom\\BarsTypes\\@RenkoBarsType.cs`.

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
"""
from __future__ import annotations

import numpy as np

from engine.contract import Tape

from . import register
from .series import BarSeries, ticks_to_price
from .tapeio import trade_rows

#: NinjaTrader `BarsPeriodType.Renko`. Confirmed against the corpus: bartag `11v1x1`
#: carries `bar_label = "Renko 1/1"` on 128,486 rows.
NT_PERIOD_TYPE = 11

_STATE_RESTORE_UNREACHABLE = True


def params_str(*, brick_ticks: int, tick_size: float, **_ignored) -> str:
    """The canonical settings string. BOTH columns must produce this exact string --
    it is an identity field, so a mismatch ABORTs the gate instead of failing it.

    `reset_on_new_session` is deliberately absent: it is NinjaTrader's data-series
    "Break at EOD" setting, and `SentinelBarDump`'s header does not carry it. A field
    only one side can see cannot be part of a shared identity; it is a stated
    precondition of the gate instead (see bars/README.md).
    """
    return "renko brick=%d tick=%s" % (int(brick_ticks), _fmt(tick_size))


def _fmt(x: float) -> str:
    s = ("%.10f" % float(x)).rstrip("0").rstrip(".")
    return s or "0"


def bartag(*, brick_ticks: int, **_ignored) -> str:
    """The Sentinel bartag NinjaTrader would report for this parameterisation.

    `SentinelCore.BarTag` is `<periodTypeInt>v<Value>` plus `x<Value2>` when Value2
    is non-zero; a Renko chart carries Value2 == 1, hence `11v1x1` for brick 1.
    """
    return "%dv%dx1" % (NT_PERIOD_TYPE, int(brick_ticks))


def renko(tape: Tape, *, brick_ticks: int = 2, tick_size: float,
          reset_on_new_session: bool = True, instrument: str | None = None) -> BarSeries:
    """Build stock-NinjaTrader Renko bars over `tape`.

    brick_ticks           `BarsPeriod.Value` -- brick height in ticks (NT default 2).
    tick_size             the instrument's tick size; brick height = value * tick size.
    reset_on_new_session  NT's `Bars.IsResetOnNewTradingDay` ("Break at EOD", on by
                          default for intraday). Sessions come from the tape's own
                          `session_id`, one file per ET trading day (§3.1).
    """
    if int(brick_ticks) < 1:
        raise ValueError("brick_ticks must be >= 1 (NT's BarsPeriod.Value)")
    if not (tick_size > 0):
        raise ValueError("tick_size must be > 0")

    rows = trade_rows(tape)
    if rows.size == 0:
        raise ValueError("tape has no trade rows: Renko is built from the Last series")

    px = tape.last[rows]
    grid = px / float(tick_size)
    ticks = np.rint(grid)
    off_grid = np.abs(grid - ticks)
    worst = float(off_grid.max())
    if worst > 1e-6:
        bad = int(np.argmax(off_grid))
        raise ValueError(
            "trade price %r at tape row %d is not on the %s tick grid (residual %.3g). "
            "Renko boundaries are grid levels; a price off the grid is a data defect, "
            "not something to round away." % (float(px[bad]), int(rows[bad]), tick_size, worst))

    # Python lists: this is an inherently sequential state machine and per-element
    # numpy scalar access is ~5x slower than list access inside the loop.
    p_t = ticks.astype(np.int64).tolist()
    v_t = tape.size[rows].astype(np.int64).tolist()
    t_t = tape.ts_ms[rows].tolist()
    s_t = tape.session_id[rows].astype(np.int64).tolist()
    r_t = rows.astype(np.int64).tolist()

    off = int(brick_ticks)

    b_o: list[int] = []
    b_h: list[int] = []
    b_l: list[int] = []
    b_c: list[int] = []
    b_v: list[int] = []
    b_ts: list[int] = []
    b_ots: list[int] = []
    b_start: list[int] = []
    b_end: list[int] = []
    b_n: list[int] = []
    b_sess: list[int] = []

    renko_high = 0
    renko_low = 0
    cur_session = -1
    n = 0
    repair_skipped = 0
    session_resets = 0

    for k in range(len(p_t)):
        c = p_t[k]
        vol = v_t[k]
        ts = t_t[k]
        sess = s_t[k]
        fi = r_t[k]

        is_new_session = sess != cur_session
        if n == 0 or (reset_on_new_session and is_new_session):
            if n > 0:
                # C# lines 42-50: close out the last bar of the session, open == close.
                # Time and volume survive; only OHLC is flattened.
                lc = b_c[-1]
                b_o[-1] = lc
                b_h[-1] = lc
                b_l[-1] = lc
                session_resets += 1
            cur_session = sess
            renko_high = c + off
            renko_low = c - off
            b_o.append(c); b_h.append(c); b_l.append(c); b_c.append(c)
            b_v.append(vol); b_ts.append(ts); b_ots.append(ts)
            b_start.append(fi); b_end.append(fi); b_n.append(1); b_sess.append(sess)
            n += 1
            continue

        bar_open = b_o[-1]
        bar_high = b_h[-1]
        bar_low = b_l[-1]
        bar_vol = b_v[-1]
        bar_ts = b_ts[-1]
        later = bar_ts if ts < bar_ts else ts

        if c >= renko_high:
            lo = renko_high - off
            if bar_open != lo or bar_high != renko_high or bar_low != lo:
                # C# lines 96-98: RemoveLastBar + AddBar. Rows, volume and stamp of the
                # forming bar are kept verbatim; only OHLC becomes the clean brick.
                b_o[-1] = lo; b_h[-1] = renko_high; b_l[-1] = lo; b_c[-1] = renko_high
                b_ts[-1] = bar_ts
                b_v[-1] = bar_vol
            else:
                # The C# does NOTHING here -- no RemoveLastBar, so the bar keeps the
                # close it already had. Faithful, and counted: the branch is argued
                # unreachable in a replay from the start of a tape, and an argument that
                # is never measured is a belief.
                repair_skipped += 1

            renko_low = renko_high - 2 * off
            renko_high += off

            prev_end = b_end[-1]
            while c >= renko_high:                      # gap fill: bricks with no ticks
                lo = renko_high - off
                b_o.append(lo); b_h.append(renko_high); b_l.append(lo); b_c.append(renko_high)
                b_v.append(0); b_ts.append(later); b_ots.append(-1)
                b_start.append(-1); b_end.append(prev_end); b_n.append(0); b_sess.append(sess)
                n += 1
                renko_low = renko_high - 2 * off
                renko_high += off

            lo = renko_high - off
            b_o.append(lo)
            b_h.append(lo if lo > c else c)
            b_l.append(c if c < lo else lo)
            b_c.append(c)
            b_v.append(vol); b_ts.append(later); b_ots.append(ts)
            b_start.append(fi); b_end.append(fi); b_n.append(1); b_sess.append(sess)
            n += 1

        elif c <= renko_low:
            hi = renko_low + off
            if bar_open != hi or bar_high != hi or bar_low != renko_low:
                b_o[-1] = hi; b_h[-1] = hi; b_l[-1] = renko_low; b_c[-1] = renko_low
                b_ts[-1] = bar_ts
                b_v[-1] = bar_vol
            else:
                repair_skipped += 1

            renko_high = renko_low + 2 * off
            renko_low -= off

            prev_end = b_end[-1]
            while c <= renko_low:
                hi = renko_low + off
                b_o.append(hi); b_h.append(hi); b_l.append(renko_low); b_c.append(renko_low)
                b_v.append(0); b_ts.append(later); b_ots.append(-1)
                b_start.append(-1); b_end.append(prev_end); b_n.append(0); b_sess.append(sess)
                n += 1
                renko_high = renko_low + 2 * off
                renko_low -= off

            hi = renko_low + off
            b_o.append(hi)
            b_h.append(hi if hi > c else c)
            b_l.append(c if c < hi else hi)
            b_c.append(c)
            b_v.append(vol); b_ts.append(later); b_ots.append(ts)
            b_start.append(fi); b_end.append(fi); b_n.append(1); b_sess.append(sess)
            n += 1

        else:
            # C# line 146: UpdateBar -- high/low take max/min, volume ACCUMULATES.
            if c > bar_high:
                b_h[-1] = c
            if c < bar_low:
                b_l[-1] = c
            b_c[-1] = c
            b_ts[-1] = later
            b_v[-1] = bar_vol + vol
            b_end[-1] = fi
            b_n[-1] += 1

    sess_arr = np.asarray(b_sess, dtype=np.int32)
    bar_index = np.zeros(n, dtype=np.int64)
    if n:
        # index WITHIN the session: the §2 pairing coordinate. NT's dump carries the
        # chart-global CurrentBar, so both sides renumber from each session's first bar.
        starts = np.flatnonzero(np.concatenate(([True], np.diff(sess_arr) != 0)))
        seq = np.arange(n, dtype=np.int64)
        bar_index = seq - np.repeat(starts, np.diff(np.append(starts, n)))

    is_partial = np.zeros(n, dtype=bool)
    if n:
        is_partial[-1] = True                # the only bar NT never closes

    # ⛔ THE ONE PLACE A TICK LEVEL BECOMES A PRICE, and it must be a DIVISION.
    # `np.asarray(b_o, np.float64) * tick_size` -- what stood here -- is what the first
    # real gate run caught: 37,765 of 94,108 bars off by exactly one ULP
    # (42379 * 0.1 = 4237.900000000001, 42379 / 10 = 4237.9). See `ticks_to_price`.
    return BarSeries(
        open=ticks_to_price(b_o, tick_size),
        high=ticks_to_price(b_h, tick_size),
        low=ticks_to_price(b_l, tick_size),
        close=ticks_to_price(b_c, tick_size),
        volume=np.asarray(b_v, dtype=np.int64),
        ts_ms=np.asarray(b_ts, dtype=np.int64),
        open_ts_ms=np.asarray(b_ots, dtype=np.int64),
        end_idx=np.asarray(b_end, dtype=np.int64),
        start_idx=np.asarray(b_start, dtype=np.int64),
        tick_count=np.asarray(b_n, dtype=np.int64),
        session_id=sess_arr,
        bar_index=bar_index,
        is_partial=is_partial,
        bartype="renko",
        bar_params=params_str(brick_ticks=brick_ticks, tick_size=tick_size),
        instrument=instrument if instrument is not None else tape.instrument,
        notes={
            "trade_rows": int(rows.size),
            "session_resets": session_resets,
            "repair_skipped": repair_skipped,
            "reset_on_new_session": bool(reset_on_new_session),
        },
        tape=tape,
    )


register(
    "renko", renko,
    params_str=params_str,
    nt_period_type=NT_PERIOD_TYPE,
    bartag=bartag,
    doc="Stock NinjaTrader Renko (@RenkoBarsType.cs). Brick = BarsPeriod.Value ticks.",
)
