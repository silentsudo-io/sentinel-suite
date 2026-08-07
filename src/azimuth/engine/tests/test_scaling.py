"""Scaling — a trade is entry → flat, composed of LEGS.

A partial exit is a partial fill of the trade, so it belongs in the order model
(§1.1.1), not in a post-hoc P&L adjustment. Scaling is expressed as a change in
the authoritative net target `position[]`; each change emits one leg.

The live thesis is the exit policy — scale-and-trail is the obvious candidate
family — so the assertions below are about the thing that would silently ruin
it: **the protective orders must cover the REMAINING quantity after a partial.**
"""
from __future__ import annotations

import numpy as np
import pytest

from engine import (Backtester, EngineConfig, ExitReason, SignalError, Signals,
                    ScalingMode, spec_for)
from engine.bars import bars_from_end_idx
from engine.config import LegReason
from engine.orders import OrderState, OrderType, Purpose, Side
from fixtures import flat_book, mk_tape

GC = spec_for("GC")
# 20 rows, 8 bars. interval k = rows (END[k], END[k+1]]
#   0: 2-3   1: 4-5   2: 6-7   3: 8-9   4: 10-11   5: 12-13   6: 14-19
END = [1, 3, 5, 7, 9, 11, 13, 19]


def _bars(bid, ask):
    return bars_from_end_idx(mk_tape(bid, ask), END)


def _run(bid, ask, sig, **cfgkw):
    cfg = EngineConfig(commission_per_side=0.0, continuous=True, **cfgkw)
    return Backtester(cfg, GC).run(_bars(bid, ask), sig)


# ------------------------------------------------------------- scale out
def test_a_scale_out_is_a_leg_not_a_new_trade():
    bid, ask = flat_book(20, 100.0, 0.5)
    sig = Signals(8)
    sig.position[0] = 2          # long 2
    sig.position[2] = 1          # bank one
    sig.position[6] = 0          # flat
    res = _run(bid, ask, sig)

    assert len(res.trades) == 1, "scaling out is not a second trade"
    t = res.trades[0]
    assert t.was_scaled and t.n_legs == 3
    assert [l.reason for l in t.legs] == [
        LegReason.ENTRY, LegReason.SCALE_OUT, LegReason.SIGNAL]
    assert [l.qty for l in t.legs] == [2, 1, 1]
    assert [l.position_after for l in t.legs] == [2, 1, 0]
    assert [l.tape_idx for l in t.legs] == [2, 6, 14]
    assert [l.side for l in t.legs] == [Side.BUY, Side.SELL, Side.SELL]

    assert t.qty == 2 and t.peak_qty == 2
    assert t.entry_price == 100.5              # ASK
    assert t.exit_price == 100.0               # both closing legs hit the BID
    assert t.exit_reason is ExitReason.SIGNAL


def test_trade_pnl_is_the_sum_over_legs():
    bid, ask = flat_book(20, 100.0, 0.5)
    bid[6], ask[6] = 101.0, 101.5              # bank the first half higher
    sig = Signals(8)
    sig.position[0] = 2
    sig.position[2] = 1
    sig.position[6] = 0
    t = _run(bid, ask, sig).trades[0]

    scale_leg = t.legs[1]
    assert scale_leg.price == 101.0            # sold at the BID
    assert scale_leg.realised_pnl == pytest.approx((101.0 - 100.5) * 100.0)
    assert t.legs[0].realised_pnl == 0.0, "an opening leg realises nothing"
    assert t.gross_pnl == pytest.approx(sum(l.realised_pnl for l in t.legs))
    assert t.gross_pnl == pytest.approx(50.0 - 50.0)
    assert t.spread_cost_ccy == pytest.approx(sum(l.spread_cost_ccy for l in t.legs))


def test_commission_accrues_per_leg():
    bid, ask = flat_book(20, 100.0, 0.5)
    sig = Signals(8)
    sig.position[0] = 2
    sig.position[2] = 1
    sig.position[6] = 0
    res = Backtester(EngineConfig(commission_per_side=2.0, continuous=True), GC).run(
        _bars(bid, ask), sig)
    t = res.trades[0]
    assert t.commission == pytest.approx(2.0 * (2 + 1 + 1))
    assert t.net_pnl == pytest.approx(t.gross_pnl - t.commission)


# ---------------------------------------- THE ONE THAT MUST NOT REGRESS
def test_the_stop_covers_the_remaining_quantity_after_a_partial():
    bid, ask = flat_book(20, 100.0, 0.5)
    bid[10], ask[10] = 99.0, 99.5              # gaps through a 99.5 stop
    sig = Signals(8)
    sig.position[0] = 2
    sig.position[2] = 1                        # bank one at interval 2
    sig.sl_long[:] = 99.5
    res = _run(bid, ask, sig)

    t = res.trades[0]
    assert [l.reason for l in t.legs] == [
        LegReason.ENTRY, LegReason.SCALE_OUT, LegReason.STOP]
    stop_leg = t.legs[-1]
    assert stop_leg.qty == 1, "the stop must be sized to what is still open"
    assert stop_leg.position_after == 0
    assert stop_leg.price == 99.0

    stops = [o for o in res.orders if o.purpose is Purpose.EXIT_STOP]
    assert [o.qty for o in stops] == [2, 1], "the 2-lot stop must be replaced by a 1-lot"
    assert stops[0].state is OrderState.CANCELLED
    assert "resized to remaining quantity 1" in stops[0].note
    assert stops[1].state is OrderState.FILLED


def test_the_target_is_also_resized_after_a_partial():
    bid, ask = flat_book(20, 100.0, 0.5)
    bid[10], ask[10] = 101.5, 102.0
    sig = Signals(8)
    sig.position[0] = 3
    sig.position[2] = 2
    sig.tp_long[:] = 101.0
    res = _run(bid, ask, sig)
    t = res.trades[0]
    assert t.legs[-1].reason is LegReason.TARGET
    assert t.legs[-1].qty == 2
    tgts = [o for o in res.orders if o.purpose is Purpose.EXIT_TARGET]
    assert [o.qty for o in tgts] == [3, 2]


def test_an_oversized_bracket_can_never_flip_the_position():
    """Every leg's `position_after` must keep the same sign until it reaches 0."""
    bid, ask = flat_book(20, 100.0, 0.5)
    bid[10], ask[10] = 99.0, 99.5
    sig = Signals(8)
    sig.position[0] = 3
    sig.position[2] = 2          # interval 2 -> rows 6..7
    sig.position[3] = 1          # interval 3 -> rows 8..9
    sig.sl_long[:] = 99.5        # fires at row 10, on the last contract
    res = _run(bid, ask, sig)
    seen = [l.position_after for l in res.trades[0].legs]
    assert seen == [3, 2, 1, 0]
    assert all(p >= 0 for p in seen)
    assert res.trades[0].legs[-1].qty == 1


def test_a_protective_order_beats_a_pending_scale_on_the_same_row():
    """A stop and a scale-out both due on row 10: the stop was working across the
    whole prior interval, the scale was submitted at this interval's open. The
    stop wins and takes the WHOLE remaining position -- there is no half-scaled
    limbo state."""
    bid, ask = flat_book(20, 100.0, 0.5)
    bid[10], ask[10] = 99.0, 99.5
    sig = Signals(8)
    sig.position[0] = 3
    sig.position[2] = 2
    sig.position[4] = 1          # interval 4 == rows 10..11, same row as the stop
    sig.sl_long[:] = 99.5
    res = _run(bid, ask, sig)
    t = res.trades[0]
    assert [l.position_after for l in t.legs] == [3, 2, 0]
    assert t.legs[-1].reason is LegReason.STOP and t.legs[-1].qty == 2
    scales = [o for o in res.orders if o.purpose is Purpose.SCALE_OUT]
    assert scales[-1].state is OrderState.CANCELLED, "the losing scale must be cancelled"


# -------------------------------------------------------------- scale in
def test_pyramiding_is_off_by_default():
    bid, ask = flat_book(20, 100.0, 0.5)
    sig = Signals(8)
    sig.position[0] = 1
    sig.position[2] = 2
    with pytest.raises(SignalError, match="pyramids"):
        _run(bid, ask, sig)


def test_pyramiding_works_when_explicitly_enabled():
    bid, ask = flat_book(20, 100.0, 0.5)
    bid[6], ask[6] = 100.5, 101.0              # the add-on costs more
    sig = Signals(8)
    sig.position[0] = 1
    sig.position[2] = 2
    sig.position[6] = 0
    t = _run(bid, ask, sig, scaling=ScalingMode.FULL).trades[0]

    assert [l.reason for l in t.legs] == [
        LegReason.ENTRY, LegReason.SCALE_IN, LegReason.SIGNAL]
    assert [l.qty for l in t.legs] == [1, 1, 2]
    assert [l.position_after for l in t.legs] == [1, 2, 0]
    assert t.qty == 2 and t.peak_qty == 2
    assert t.entry_price == pytest.approx((100.5 + 101.0) / 2), \
        "a scale-in must move the volume-weighted entry price"
    assert t.legs[1].realised_pnl == 0.0
    assert t.gross_pnl == pytest.approx(2 * (100.0 - 100.75) * 100.0)


def test_strict_mode_refuses_any_same_sign_resize():
    bid, ask = flat_book(20, 100.0, 0.5)
    sig = Signals(8)
    sig.position[0] = 2
    sig.position[2] = 1
    with pytest.raises(SignalError, match="STRICT"):
        _run(bid, ask, sig, scaling=ScalingMode.STRICT)


# ------------------------------------------------------------- shorts
def test_scaling_a_short_crosses_the_other_way():
    bid, ask = flat_book(20, 100.0, 0.5)
    sig = Signals(8)
    sig.position[0] = -2
    sig.position[2] = -1
    sig.position[6] = 0
    t = _run(bid, ask, sig).trades[0]
    assert t.direction == -1
    assert [l.side for l in t.legs] == [Side.SELL, Side.BUY, Side.BUY]
    assert t.entry_price == 100.0              # short entry on the BID
    assert t.exit_price == 100.5               # both buy-backs on the ASK
    assert t.legs[1].realised_pnl == pytest.approx(-50.0)


# ------------------------------------------------------------- plumbing
def test_legs_are_exposed_for_a_leg_level_parity_gate():
    bid, ask = flat_book(20, 100.0, 0.5)
    sig = Signals(8)
    sig.position[0] = 2
    sig.position[2] = 1
    sig.position[6] = 0
    res = _run(bid, ask, sig)
    legs = res.legs()
    assert len(legs) == 3 == res.metrics()["n_legs"]
    assert res.metrics()["n_scaled_trades"] == 1
    recs = res.to_leg_records()
    assert recs[1]["reason"] == "scale_out" and recs[1]["side"] == "SELL"
    assert all(r["trade_idx"] == 0 for r in recs)
    assert [r["idx"] for r in recs] == [0, 1, 2]


def test_a_scaled_trade_still_reports_one_entry_and_one_flattening_leg():
    bid, ask = flat_book(20, 100.0, 0.5)
    sig = Signals(8)
    sig.position[0] = 3
    sig.position[2] = 2
    sig.position[4] = 1
    sig.position[6] = 0
    t = _run(bid, ask, sig).trades[0]
    opens = [l for l in t.legs if l.reason is LegReason.ENTRY]
    closes = [l for l in t.legs if l.reason.is_closing and l.position_after == 0]
    assert len(opens) == 1 and len(closes) == 1
    assert sum(l.qty for l in t.legs if l.side is Side.BUY) == \
           sum(l.qty for l in t.legs if l.side is Side.SELL) == t.qty


def test_scale_and_trail_runs_end_to_end_on_synthetic_tape():
    """The capability the ruling was about: bank part of the position, trail the
    rest. Runs on a real sweep-sized tape, not a hand-built fixture."""
    from engine import time_bars
    from engine.contract import synth_tape
    from engine.demo_strategies import ScaleAndTrail

    tape = synth_tape(4, rows_per_session=25_000, seed=31)
    bars = time_bars(tape, 15_000)
    res = Backtester(EngineConfig(), spec_for("GC")).run_strategy(
        ScaleAndTrail(fast=6, slow=25, qty=2, bank=1, first_ticks=15,
                      stop_ticks=25, trail_ticks=25, trail_lookback=12), bars)

    assert len(res.trades) > 10
    scaled = [t for t in res.trades if t.was_scaled]
    assert scaled, "the fixture must actually bank part of a position"
    for t in res.trades:
        assert t.qty == sum(l.qty for l in t.legs if l.side.sign == t.direction)
        assert t.legs[-1].position_after == 0
        assert t.gross_pnl == pytest.approx(sum(l.realised_pnl for l in t.legs))
        # the leg that closed the trade is never larger than what was still open
        assert t.legs[-1].qty == abs(t.legs[-2].position_after)
    assert res.metrics()["n_scaled_trades"] == len(scaled)


def test_fast_filter_is_a_pure_optimisation_for_scaled_trades():
    bid, ask = flat_book(20, 100.0, 0.5)
    bid[10], ask[10] = 99.0, 99.5
    sig = Signals(8)
    sig.position[0] = 3
    sig.position[2] = 2
    sig.position[4] = 1
    sig.sl_long[:] = 99.5

    def legs(fast):
        cfg = EngineConfig(commission_per_side=0.0, continuous=True,
                           fast_interval_filter=fast)
        r = Backtester(cfg, GC).run(_bars(bid, ask), sig)
        return [{k: v for k, v in l.to_dict().items() if k != "order_id"}
                for l in r.legs()]

    assert legs(True) == legs(False)
