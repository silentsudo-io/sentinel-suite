"""Zero-row bars — one tape row closing several bars.

Renko, brick and range clocks print SEVERAL bars from ONE tick when price jumps
far enough to break multiple levels at once. Measured on real tape, 35.7% of
Renko 1/1 bars are row-less, and Renko 1/1 is the largest bartag in the corpus.

The contract: `end_idx` is NON-DECREASING; a zero-row interval offers NO fill
opportunity; a decision taken at a zero-row bar's close CARRIES FORWARD to the
next interval that has rows. A DECREASING `end_idx` is still malformed.
"""
from __future__ import annotations

import numpy as np
import pytest

from engine import (Backtester, EngineConfig, ExitReason, Signals, spec_for)
from engine.bars import bars_from_end_idx
from fixtures import flat_book, mk_tape

GC = spec_for("GC")

# 12 rows. Bars 1,2,3 all close on row 3 -> intervals 1 and 2 are ZERO-ROW.
#   bar:        0    1    2    3    4    5
#   end_idx:    1    3    3    3    7   11
#   interval:   0 -> rows 2..3     (has rows)
#               1 -> rows 4..3     (EMPTY)
#               2 -> rows 4..3     (EMPTY)
#               3 -> rows 4..7     (has rows)
#               4 -> rows 8..11    (has rows)
JUMPY = [1, 3, 3, 3, 7, 11]
#: the same market expressed without the row-less bars
MERGED = [1, 3, 7, 11]


def _bars(bid, ask, end_idx):
    return bars_from_end_idx(mk_tape(bid, ask), end_idx)


def _run(bars, sig, **cfgkw):
    cfgkw.setdefault("continuous", True)
    cfg = EngineConfig(commission_per_side=0.0, **cfgkw)
    return Backtester(cfg, GC).run(bars, sig)


# ------------------------------------------------------------- the contract
def test_non_decreasing_end_idx_is_accepted():
    b = _bars(*flat_book(12), JUMPY)
    assert b.n == 6
    assert list(b.iv_nonempty) == [True, False, False, True, True]
    assert b.n_empty_intervals == 2


def test_decreasing_end_idx_still_refuses_loudly():
    bid, ask = flat_book(12)
    with pytest.raises(ValueError, match="decreases at bar"):
        _bars(bid, ask, [1, 3, 2, 7, 11])


def test_a_zero_row_bar_carries_the_previous_close_and_no_volume():
    b = _bars(*flat_book(12), JUMPY)
    # bars 1..3 all close on row 3; bar 1 HAS rows (2..3), bars 2 and 3 do not
    assert b.volume[2] == 0 and b.volume[3] == 0
    assert b.close[2] == b.close[3] == b.close[1], "carried forward from the last real bar"
    for k in (2, 3):
        assert b.open[k] == b.high[k] == b.low[k] == b.close[k]
    assert b.volume[0] > 0 and b.volume[1] > 0 and b.volume[4] > 0


def test_zero_row_intervals_are_reported_as_structure_not_ambiguity():
    bid, ask = flat_book(12, 100.0, 0.5)
    sig = Signals(6)
    sig.entry_long[0] = True
    res = _run(_bars(bid, ask, JUMPY), sig)
    assert res.zero_row_intervals == 2
    assert res.ambiguous_exits == 0, "a zero-row interval has exactly one lawful answer"
    assert all(not t.ambiguous_exit for t in res.trades)


# ------------------------------------------- no fill inside a zero-row interval
def test_no_fill_is_ever_attributed_to_a_zero_row_interval():
    bid, ask = flat_book(12, 100.0, 0.5)
    sig = Signals(6)
    sig.entry_long[0] = True
    sig.exit_long[1] = True          # decision at a ZERO-ROW bar's close
    res = _run(_bars(bid, ask, JUMPY), sig)

    b = _bars(bid, ask, JUMPY)
    empty_rows = set()               # rows that belong to no non-empty interval
    for k in range(len(b.iv_nonempty)):
        if not b.iv_nonempty[k]:
            empty_rows.add((int(b.iv_start[k]), int(b.iv_end[k])))
    assert empty_rows == {(4, 3)}, "both empty intervals span the same null range"

    for t in res.trades:
        for lg in t.legs:
            k = None
            for i in range(len(b.iv_nonempty)):
                if b.iv_start[i] <= lg.tape_idx <= b.iv_end[i]:
                    k = i
                    break
            assert k is not None and bool(b.iv_nonempty[k]), \
                f"leg filled at row {lg.tape_idx} inside a zero-row interval"


def test_a_decision_at_a_zero_row_bar_carries_forward_to_the_next_rows():
    """The exit signal fires at bar 1's close. Interval 1 and 2 have no rows, so
    it must fill on the FIRST row of interval 3 — row 4 — not at row 3's price
    and not discarded."""
    bid, ask = flat_book(12, 100.0, 0.5)
    bid[4], ask[4] = 101.0, 101.5      # so the carried fill is identifiable
    sig = Signals(6)
    sig.entry_long[0] = True
    sig.exit_long[1] = True
    res = _run(_bars(bid, ask, JUMPY), sig)

    t = res.trades[0]
    assert t.entry_tape_idx == 2
    assert t.exit_tape_idx == 4, "the exit must fill on the next row that EXISTS"
    assert t.exit_price == 101.0, "and at THAT row's bid, not the stale one"
    assert t.exit_reason is ExitReason.SIGNAL


def test_a_stop_is_not_triggered_inside_a_zero_row_bar():
    """Bar 2 is row-less and its (bar-type supplied) LOW is far below the stop.
    Nothing traded there, so the stop cannot fill there. It fills on the next
    interval that has rows, at that interval's price."""
    bid, ask = flat_book(12, 100.0, 0.5)
    bid[4], ask[4] = 99.0, 99.5        # the first real row after the gap
    tape = mk_tape(bid, ask)
    n = len(JUMPY)
    low = np.full(n, 100.0)
    low[1] = low[2] = low[3] = 95.0    # the bricks "printed" through the stop
    b = bars_from_end_idx(tape, JUMPY, low=low)

    sig = Signals(n)
    sig.entry_long[0] = True
    sig.sl_long[:] = 99.5
    res = _run(b, sig)

    t = res.trades[0]
    assert t.exit_reason is ExitReason.STOP
    assert t.exit_tape_idx == 4, "the stop cannot fill in a bar that had no ticks"
    assert t.exit_price == 99.0, "it fills at the gapped price of the first real row"


# ------------------------------------------------ equivalence with merged bars
def _scenario(end_idx, entry_bar, exit_bar, bid, ask):
    sig = Signals(len(end_idx))
    sig.entry_long[entry_bar] = True
    sig.exit_long[exit_bar] = True
    return _run(_bars(bid, ask, end_idx), sig)


@pytest.mark.parametrize("shift", [0.0, 0.7])
def test_row_less_bars_produce_identical_trades_to_the_merged_expression(shift):
    """Splitting one tick into several bars must not change what the engine does.

    Same tape, same market: expressed once with three bars closing on row 3 and
    once with those merged into one. The signals are placed on the bar that
    actually carries the row in each expression, so the two are the same
    decision — and must produce byte-identical trades.
    """
    bid, ask = flat_book(12, 100.0, 0.5)
    bid[8] += shift
    ask[8] += shift

    # JUMPY:  entry at bar 0 (interval 0 = rows 2..3), exit at bar 3 (rows 4..7)
    # MERGED: entry at bar 0 (interval 0 = rows 2..3), exit at bar 1 (rows 4..7)
    jumpy = _scenario(JUMPY, 0, 3, bid, ask)
    merged = _scenario(MERGED, 0, 1, bid, ask)

    def strip(t):
        d = t.to_dict()
        # order ids come from a process-global counter; bar INDICES legitimately
        # differ because one expression has three extra bars. Everything that
        # describes what actually happened must match.
        for k in ("entry_order_id", "exit_order_id", "entry_bar", "exit_bar"):
            d.pop(k)
        # NaN != NaN, so "absent" must normalise before comparing
        return {k: ("nan" if isinstance(v, float) and v != v else v)
                for k, v in d.items()}

    assert len(jumpy.trades) == len(merged.trades) == 1
    assert [strip(t) for t in jumpy.trades] == [strip(t) for t in merged.trades]
    assert jumpy.zero_row_intervals == 2 and merged.zero_row_intervals == 0


def test_force_flat_rolls_back_off_a_zero_row_interval():
    """The session's last bar is row-less. The flatten must still happen on the
    session's genuine final row, not be skipped and not spill into the next
    session."""
    from engine.contract import concat
    from fixtures import mk_tape as mk

    a = mk(*flat_book(8, 100.0, 0.5), session_date="2026-07-20")
    ts1 = a.ts_ms[-1] + 8 * 3_600_000 + np.arange(8, dtype=np.int64) * 7
    c = mk(*flat_book(8, 100.0, 0.5), ts=ts1, session_date="2026-07-21")
    tape = concat([a, c])
    # bars 2 and 3 both close on row 7 -> the session's last bar is row-less
    b = bars_from_end_idx(tape, [1, 4, 7, 7, 11, 15])
    assert list(b.iv_nonempty) == [True, True, False, True, True]

    sig = Signals(6)
    sig.entry_long[0] = True
    res = _run(b, sig, continuous=False)
    t = res.trades[0]
    assert t.exit_reason is ExitReason.FORCE_FLAT_SESSION
    assert t.exit_tape_idx == 7, "flatten on the session's real final row"


def test_fast_filter_is_a_pure_optimisation_across_zero_row_intervals():
    bid, ask = flat_book(12, 100.0, 0.5)
    bid[4], ask[4] = 99.0, 99.5
    sig = Signals(6)
    sig.entry_long[0] = True
    sig.sl_long[:] = 99.5
    sig.exit_long[2] = True

    def legs(fast):
        cfg = EngineConfig(commission_per_side=0.0, continuous=True,
                           fast_interval_filter=fast)
        r = Backtester(cfg, GC).run(_bars(bid, ask, JUMPY), sig)
        return [{k: v for k, v in l.to_dict().items() if k != "order_id"}
                for l in r.legs()]

    assert legs(True) == legs(False)
