"""Engine semantics -- one position at a time, the conflict rule, the declared
SL/TP resolution order, warmup, continuous mode and the force-flat cascade."""
from __future__ import annotations

import numpy as np
import pytest

from engine import (AmbiguousExit, Backtester, EXIT_PRIORITY, EngineConfig,
                    ExitReason, PositionMode, Signals, SignalError, TouchResolution,
                    spec_for, time_bars)
from engine.bars import bars_from_end_idx
from engine.contract import synth_tape
from engine.demo_strategies import MaCrossBracket, MaCrossTrail
from engine.strategy import MarketContext
from fixtures import BASE_TS, flat_book, mk_tape, two_sessions

GC = spec_for("GC")
END = [1, 4, 7, 11]


def _bars(bid, ask, ts=None):
    return bars_from_end_idx(mk_tape(bid, ask, ts=ts), END)


def _run(bars, sig, **cfgkw):
    cfg = EngineConfig(commission_per_side=0.0, **cfgkw)
    return Backtester(cfg, GC).run(bars, sig)


# ---------------------------------------------------------- conflict rule
def test_same_bar_conflict_triggers_neither_side():
    bars = _bars(*flat_book(12))
    sig = Signals(4)
    sig.entry_long[0] = True
    sig.entry_short[0] = True
    res = _run(bars, sig, continuous=True)
    assert res.trades == [], "a same-bar conflict must trigger NEITHER side"
    assert res.entries_blocked_conflict == 1
    assert res.orders == []


def test_conflict_does_not_silently_prefer_long_because_the_if_came_first():
    """The same tape with only the short leg DOES trade -- proving the conflict
    test above is measuring the rule, not a dead signal."""
    bars = _bars(*flat_book(12))
    sig = Signals(4)
    sig.entry_short[0] = True
    assert len(_run(bars, sig, continuous=True).trades) == 1


def test_one_position_at_a_time():
    bars = _bars(*flat_book(12))
    sig = Signals(4)
    sig.entry_long[0] = True
    sig.entry_long[1] = True
    sig.entry_long[2] = True
    res = _run(bars, sig)
    assert len(res.trades) == 1
    assert res.trades[0].entry_bar == 0


# ------------------------------------------------- SL/TP same-ms ambiguity
#
# At tick resolution a single quote row carries ONE bid, so `bid <= sl` and
# `bid >= tp` cannot both hold (an inverted bracket is rejected outright). The
# ambiguity survives only where the tape ties: two rows sharing a ts_ms.
#
AMBIG_TS = BASE_TS + np.array([0, 7, 14, 21, 28, 35, 35, 49, 56, 63, 70, 77],
                              dtype=np.int64)


def _ambiguous_bars():
    bid, ask = flat_book(12, 100.0, 0.5)
    bid[5], ask[5] = 99.0, 99.5        # touches a 99.5 stop
    bid[6], ask[6] = 101.5, 102.0      # touches a 101.0 target, SAME millisecond
    return _bars(bid, ask, ts=AMBIG_TS)


def _ambiguous_signals():
    sig = Signals(4)
    sig.entry_long[0] = True
    sig.sl_long[:] = 99.5
    sig.tp_long[:] = 101.0
    return sig


@pytest.mark.parametrize("policy,reason,price", [
    (TouchResolution.STOP_FIRST, ExitReason.STOP, 99.0),
    (TouchResolution.TARGET_FIRST, ExitReason.TARGET, 101.0),
    (TouchResolution.ROW_ORDER, ExitReason.STOP, 99.0),
])
def test_touch_resolution_is_a_declared_policy(policy, reason, price):
    res = _run(_ambiguous_bars(), _ambiguous_signals(),
               touch_resolution=policy, continuous=True)
    t = res.trades[0]
    assert t.exit_reason is reason
    assert t.exit_price == pytest.approx(price)
    assert t.ambiguous_exit is True
    assert res.ambiguous_exits == 1


def test_strict_mode_refuses_to_guess():
    with pytest.raises(AmbiguousExit, match="refuses to guess"):
        _run(_ambiguous_bars(), _ambiguous_signals(),
             touch_resolution=TouchResolution.STRICT, continuous=True)


def test_an_unambiguous_run_reports_zero_ambiguity():
    bid, ask = flat_book(12, 100.0, 0.5)
    bid[5], ask[5] = 99.0, 99.5
    sig = _ambiguous_signals()
    res = _run(_bars(bid, ask), sig, continuous=True)
    assert res.ambiguous_exits == 0
    assert res.trades[0].ambiguous_exit is False


def test_exit_priority_is_the_documented_order():
    assert EXIT_PRIORITY == (
        ExitReason.STOP, ExitReason.TARGET, ExitReason.SIGNAL,
        ExitReason.FORCE_FLAT_SESSION, ExitReason.FORCE_FLAT_ROLLOVER,
        ExitReason.FORCE_FLAT_EOD,
    )
    assert [r.value for r in EXIT_PRIORITY] == sorted(r.value for r in EXIT_PRIORITY)


@pytest.mark.parametrize("field,reason", [("sl_long", ExitReason.STOP),
                                          ("tp_long", ExitReason.TARGET)])
def test_protective_orders_beat_a_signal_exit_on_the_same_row(field, reason):
    bid, ask = flat_book(12, 100.0, 0.5)
    bid[5], ask[5] = (99.0, 99.5) if field == "sl_long" else (101.5, 102.0)
    sig = Signals(4)
    sig.entry_long[0] = True
    getattr(sig, field)[:] = 99.5 if field == "sl_long" else 101.0
    sig.exit_long[1] = True            # a market exit on the SAME row (5)
    res = _run(_bars(bid, ask), sig, continuous=True)
    assert res.trades[0].exit_tape_idx == 5
    assert res.trades[0].exit_reason is reason


def test_inverted_bracket_is_refused_rather_than_resolved():
    bars = _bars(*flat_book(12))
    sig = Signals(4)
    sig.entry_long[0] = True
    sig.sl_long[:] = 101.0
    sig.tp_long[:] = 100.0
    with pytest.raises(SignalError, match="inverted long bracket"):
        _run(bars, sig)


# ------------------------------------------------------- session structure
def _two_session_bars(**kw):
    b0, a0 = flat_book(12, 100.0, 0.5)
    b1, a1 = flat_book(12, 100.0, 0.5)
    t = two_sessions(b0, a0, b1, a1, **kw)
    return bars_from_end_idx(t, [1, 4, 7, 11, 13, 16, 19, 23])


def test_force_flat_at_session_end():
    bars = _two_session_bars()
    sig = Signals(8)
    sig.entry_long[0] = True
    res = _run(bars, sig, continuous=False)
    t = res.trades[0]
    assert t.exit_reason is ExitReason.FORCE_FLAT_SESSION
    assert t.exit_tape_idx == 11, "must flatten on the session's LAST row"


def test_continuous_mode_carries_the_position_across_the_boundary():
    bars = _two_session_bars()
    sig = Signals(8)
    sig.entry_long[0] = True
    res = _run(bars, sig, continuous=True)
    t = res.trades[0]
    assert t.exit_reason is ExitReason.FORCE_FLAT_EOD
    assert t.exit_tape_idx == 23


def test_rollover_forces_flat_even_in_continuous_mode():
    bars = _two_session_bars(contract0="GC 12-26", contract1="GC 02-27")
    sig = Signals(8)
    sig.entry_long[0] = True
    res = _run(bars, sig, continuous=True)
    t = res.trades[0]
    assert t.exit_reason is ExitReason.FORCE_FLAT_ROLLOVER
    assert t.exit_tape_idx == 11


def test_a_tape_gap_can_force_flat_mid_stream():
    bars = _two_session_bars()
    sig = Signals(8)
    sig.entry_long[0] = True
    res = _run(bars, sig, continuous=True, force_flat_gap_ms=3_600_000)
    assert res.trades[0].exit_reason is ExitReason.FORCE_FLAT_SESSION
    assert res.trades[0].exit_tape_idx == 11


def test_warmup_days_block_entries_but_not_later_ones():
    bars = _two_session_bars()
    sig = Signals(8)
    sig.entry_long[0] = True
    sig.entry_long[4] = True
    res = _run(bars, sig, warmup_days=1, continuous=True)
    assert res.entries_blocked_warmup == 1
    assert len(res.trades) == 1
    assert res.trades[0].entry_bar == 4


def test_block_entries_is_counted_separately_and_frees_the_next_signal():
    """§5.2 -- suppressing a trade must FREE the engine to take the next one."""
    bars = _bars(*flat_book(12))
    base = Signals(4)
    base.entry_long[0] = True
    base.entry_long[1] = True
    unfiltered = _run(bars, base, continuous=True)

    filt = Signals(4)
    filt.entry_long[0] = True
    filt.entry_long[1] = True
    filt.block_entries[0] = True
    res = _run(bars, filt, continuous=True)
    assert res.entries_blocked_filter == 1
    assert unfiltered.trades[0].entry_bar == 0
    assert res.trades[0].entry_bar == 1, "trade #2 must become reachable"


# ------------------------------------------------------- target position
def test_target_position_flips_without_ever_holding_both():
    bid, ask = flat_book(20, 100.0, 0.5)
    bars = bars_from_end_idx(mk_tape(bid, ask), [1, 3, 5, 7, 9, 11, 13, 19])
    sig = Signals(8)
    sig.position[0] = 1
    sig.position[3] = -1
    sig.position[6] = 0
    res = _run(bars, sig, continuous=True)
    assert [t.direction for t in res.trades] == [1, -1]
    assert res.trades[0].exit_tape_idx < res.trades[1].entry_tape_idx
    assert res.trades[1].exit_reason is ExitReason.SIGNAL


def test_two_authorities_over_the_same_decision_is_refused():
    bars = _bars(*flat_book(12))
    sig = Signals(4)
    sig.position[0] = 1
    sig.entry_long[1] = True
    with pytest.raises(SignalError, match="Two authorities"):
        _run(bars, sig)


def test_strict_mode_restores_the_one_fill_in_one_fill_out_guarantee():
    from engine import ScalingMode

    bars = _bars(*flat_book(12))
    sig = Signals(4)
    sig.position[0] = 1
    sig.position[1] = 2
    with pytest.raises(SignalError, match="STRICT"):
        _run(bars, sig, scaling=ScalingMode.STRICT)


# ------------------------------------------------------------ amendments
def test_the_amendment_ledger_is_complete_even_for_skipped_intervals():
    bid, ask = flat_book(12, 100.0, 0.5)
    sig = Signals(4)
    sig.entry_long[0] = True
    sig.sl_long[:] = [99.0, 99.1, 99.2, 99.3]
    res = _run(_bars(bid, ask), sig)
    stop = [o for o in res.orders if o.type.value == "stop_market"][0]
    assert [a.new for a in stop.amendments] == [99.1, 99.2]


# --------------------------------------------- the fast filter is an OPTIMISATION
def test_fast_filter_is_a_pure_optimisation():
    tape = synth_tape(2, rows_per_session=25_000, seed=3)
    bars = time_bars(tape, 30_000)
    strat = MaCrossTrail(fast=5, slow=20, stop_ticks=25, target_ticks=45,
                         trail_ticks=18, trail_lookback=10)
    sig = strat.generate(MarketContext(tape, bars))

    on = Backtester(EngineConfig(fast_interval_filter=True), GC).run(bars, sig)
    off = Backtester(EngineConfig(fast_interval_filter=False), GC).run(bars, sig)
    assert len(on.trades) > 5, "the fixture must actually trade for this to mean anything"

    def strip(t):
        d = t.to_dict()
        # order ids come from a process-global counter, so they legitimately
        # differ between two runs; everything else must be identical.
        d.pop("entry_order_id"), d.pop("exit_order_id")
        return d

    assert [strip(t) for t in on.trades] == [strip(t) for t in off.trades]


def test_end_to_end_run_over_synthetic_tape_is_coherent():
    tape = synth_tape(3, rows_per_session=20_000, seed=11)
    bars = time_bars(tape, 30_000)
    strat = MaCrossBracket(fast=8, slow=30, stop_ticks=20, target_ticks=40)
    res = Backtester(EngineConfig(), GC).run_strategy(strat, bars)
    assert res.trades
    m = res.metrics()
    assert m["n_trades"] == len(res.trades)
    assert m["spread_cost_ccy"] > 0, "crossing the spread is never free"
    # never two positions, and never overlapping
    for a, b in zip(res.trades, res.trades[1:]):
        assert a.exit_tape_idx < b.entry_tape_idx
    # every trade closed for a declared reason
    assert set(res.exit_reason_counts()) <= {r.name for r in ExitReason}
    # ms fills, not bar snaps. Entries can NEVER land on a bar close: the
    # decision is taken there and worked from the next tape row. A FORCE-FLAT
    # exit is the one legitimate exception -- the session's last row IS a bar
    # close -- and it must be exactly that row, not merely near it.
    closes = set(bars.ts_ms.tolist())
    assert all(t.entry_ts_ms not in closes for t in res.trades)
    forced = {ExitReason.FORCE_FLAT_SESSION, ExitReason.FORCE_FLAT_ROLLOVER,
              ExitReason.FORCE_FLAT_EOD}
    for t in res.trades:
        on_close = t.exit_ts_ms in closes
        if t.exit_reason not in forced:
            assert not on_close or t.exit_tape_idx == int(bars.end_idx[t.exit_bar + 1])
        else:
            assert on_close
    assert res.params["strategy"] == "MaCrossBracket"


def test_every_fill_in_a_full_run_is_on_the_correct_side_of_the_book():
    """The audit that would catch a mid-price fill hiding in ONE code path.

    Re-derives every entry and exit price from the tape and the declared rules,
    over a run that produces dozens of trades of both directions and every exit
    reason -- not a hand-built two-trade fixture.
    """
    tape = synth_tape(6, rows_per_session=25_000, seed=23)
    bars = time_bars(tape, 15_000)
    strat = MaCrossBracket(fast=6, slow=25, stop_ticks=18, target_ticks=36)
    sig = strat.generate(MarketContext(tape, bars))
    res = Backtester(EngineConfig(commission_per_side=0.0), GC).run(bars, sig)
    assert len(res.trades) > 20
    assert {t.direction for t in res.trades} == {1, -1}
    assert len(set(res.exit_reason_counts())) >= 3

    for t in res.trades:
        ei, xi, d = t.entry_tape_idx, t.exit_tape_idx, t.direction
        cross_in = tape.ask[ei] if d > 0 else tape.bid[ei]
        assert t.entry_price == pytest.approx(cross_in), "entry not at the crossing price"
        assert t.entry_price != pytest.approx(tape.mid[ei]), "entry collapsed to the mid"

        if t.exit_reason is ExitReason.TARGET:
            want = sig.tp_long[t.exit_bar] if d > 0 else sig.tp_short[t.exit_bar]
        elif t.exit_reason is ExitReason.STOP:
            lvl = sig.sl_long[t.exit_bar] if d > 0 else sig.sl_short[t.exit_bar]
            want = min(lvl, tape.bid[xi]) if d > 0 else max(lvl, tape.ask[xi])
        else:
            want = tape.bid[xi] if d > 0 else tape.ask[xi]
        assert t.exit_price == pytest.approx(want), f"{t.exit_reason.name} exit mispriced"
        # gross P&L must be exactly the crossing arithmetic, nothing else
        assert t.gross_pnl == pytest.approx(
            d * (t.exit_price - t.entry_price) * GC.point_value * t.qty)
