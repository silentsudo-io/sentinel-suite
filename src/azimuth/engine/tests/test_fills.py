"""THE FILL CONVENTION -- buy at the ASK, sell at the BID.

This project has measured that its replay fills are unfaithful (0.00% of trades
print inside the spread) and that the P&L *is* the crossing cost. These tests are
the reason the engine exists; if any of them go green on a mid-price model,
delete the engine.
"""
from __future__ import annotations

import numpy as np
import pytest

from engine import (Backtester, EngineConfig, ExitReason, Signals, spec_for,
                    available_adapters, make_adapter)
from engine.bars import bars_from_end_idx
from engine.orders import OrderState, OrderType
from fixtures import flat_book, mk_tape

GC = spec_for("GC")            # tick 0.1, tick value $10 -> $100 per point
END = [1, 4, 7, 11]            # interval 0 = rows 2..4, 1 = rows 5..7, 2 = rows 8..11


def _bars(bid, ask):
    return bars_from_end_idx(mk_tape(bid, ask), END)


def _run(bid, ask, sig, **cfgkw):
    cfg = EngineConfig(commission_per_side=0.0, continuous=True, **cfgkw)
    return Backtester(cfg, GC).run(_bars(bid, ask), sig), _bars(bid, ask)


def _long_round_trip(bid, ask, **cfgkw):
    sig = Signals(4)
    sig.entry_long[0] = True
    sig.exit_long[1] = True
    return _run(bid, ask, sig, **cfgkw)


# ---------------------------------------------------------------- the crux
def test_a_mid_price_fill_would_fail_this_test():
    """Buy and sell a static book: you must lose EXACTLY the spread.

    bid 100.0 / ask 100.5 on every row. A mid-price model fills both legs at
    100.25 and reports 0.00. Crossing the spread costs 0.5 points = $50 on GC.
    The assertion below is written so that the mid-price answer is named and
    rejected explicitly.
    """
    bid, ask = flat_book(12, 100.0, 0.5)
    res, _ = _long_round_trip(bid, ask)
    t = res.trades[0]

    mid_price_answer = 0.0
    assert t.net_pnl != mid_price_answer, "fills collapsed to the mid"
    assert t.entry_price == 100.5, "long entry did not fill at the ASK"
    assert t.exit_price == 100.0, "long exit did not fill at the BID"
    assert t.net_pnl == pytest.approx(-50.0)
    assert t.spread_cost_ccy == pytest.approx(50.0)
    assert t.spread_cost_ticks == pytest.approx(5.0)


def test_short_round_trip_crosses_the_other_way():
    bid, ask = flat_book(12, 100.0, 0.5)
    sig = Signals(4)
    sig.entry_short[0] = True
    sig.exit_short[1] = True
    res, _ = _run(bid, ask, sig)
    t = res.trades[0]
    assert t.entry_price == 100.0, "short entry did not fill at the BID"
    assert t.exit_price == 100.5, "short exit did not fill at the ASK"
    assert t.net_pnl == pytest.approx(-50.0)
    assert t.spread_cost_ccy == pytest.approx(50.0)


def test_wider_spread_costs_strictly_more():
    narrow, _ = _long_round_trip(*flat_book(12, 100.0, 0.1))
    wide, _ = _long_round_trip(*flat_book(12, 100.0, 1.0))
    assert wide.trades[0].net_pnl < narrow.trades[0].net_pnl
    assert wide.trades[0].spread_cost_ccy == pytest.approx(100.0)


def test_slippage_is_adverse_on_both_taker_legs():
    bid, ask = flat_book(12, 100.0, 0.5)
    res, _ = _long_round_trip(bid, ask, slippage_ticks=2.0)
    t = res.trades[0]
    assert t.entry_price == pytest.approx(100.5 + 0.2)
    assert t.exit_price == pytest.approx(100.0 - 0.2)
    assert t.net_pnl == pytest.approx(-90.0)
    assert t.slippage_ccy == pytest.approx(2 * 0.2 * 100.0)


def test_fill_timestamps_are_tape_rows_not_bar_closes():
    bid, ask = flat_book(12, 100.0, 0.5)
    res, bars = _long_round_trip(bid, ask)
    t = res.trades[0]
    assert t.entry_tape_idx == 2 and t.exit_tape_idx == 5
    assert t.entry_ts_ms not in set(bars.ts_ms.tolist())
    assert t.exit_ts_ms not in set(bars.ts_ms.tolist())


# ---------------------------------------------------------------- limits
def test_limit_entry_fills_at_the_limit_and_never_slips():
    bid, ask = flat_book(12, 100.0, 0.5)
    ask[3] = 99.9                       # the offer comes down to us
    bid[3] = 99.4
    sig = Signals(4)
    sig.entry_long[0] = True
    sig.entry_limit_long[0] = 99.9
    sig.exit_long[1] = True
    res, _ = _run(bid, ask, sig, slippage_ticks=3.0)
    t = res.trades[0]
    assert t.entry_was_limit
    assert t.entry_tape_idx == 3
    assert t.entry_price == 99.9, "a limit must fill AT its price, never slipped"
    assert res.orders[0].fills[0].liquidity == "maker"


def test_limit_entry_expires_when_the_market_never_comes():
    bid, ask = flat_book(12, 100.0, 0.5)
    sig = Signals(4)
    sig.entry_long[0] = True
    sig.entry_limit_long[0] = 99.0
    res, _ = _run(bid, ask, sig)
    assert res.trades == []
    assert res.limit_entries_expired == 1
    o = res.orders[0]
    assert o.type is OrderType.LIMIT and o.state is OrderState.EXPIRED
    assert o.filled_qty == 0


def test_limit_entry_lifetime_spans_the_configured_bars():
    bid, ask = flat_book(12, 100.0, 0.5)
    ask[6] = 99.9                        # only reachable in interval 1
    bid[6] = 99.4
    sig = Signals(4)
    sig.entry_long[0] = True
    sig.entry_limit_long[0] = 99.9

    dead, _ = _run(bid, ask, sig, entry_limit_lifetime_bars=1)
    assert dead.trades == [] and dead.limit_entries_expired == 1

    alive, _ = _run(bid, ask, sig, entry_limit_lifetime_bars=3)
    assert len(alive.orders[0].fills) == 1
    assert alive.orders[0].fills[0].tape_idx == 6


def test_limit_requires_through_is_stricter_than_touch():
    bid, ask = flat_book(12, 100.0, 0.5)
    ask[3] = 99.9
    bid[3] = 99.4
    sig = Signals(4)
    sig.entry_long[0] = True
    sig.entry_limit_long[0] = 99.9
    touch, _ = _run(bid, ask, sig)
    through, _ = _run(bid, ask, sig, limit_requires_through=True)
    assert touch.orders[0].filled_qty == 1
    assert through.orders[0].filled_qty == 0


# ---------------------------------------------------------------- protective
def test_stop_gaps_through_and_fills_at_the_gapped_price():
    bid, ask = flat_book(12, 100.0, 0.5)
    bid[5], ask[5] = 99.0, 99.5          # gaps straight through a 99.5 stop
    sig = Signals(4)
    sig.entry_long[0] = True
    sig.sl_long[:] = 99.5
    res, _ = _run(bid, ask, sig)
    t = res.trades[0]
    assert t.exit_reason is ExitReason.STOP
    assert t.exit_price == 99.0, "a stop must not fill better than the market gapped to"


def test_target_never_gets_price_improvement():
    bid, ask = flat_book(12, 100.0, 0.5)
    bid[5], ask[5] = 101.5, 102.0        # blows past a 101.0 target
    sig = Signals(4)
    sig.entry_long[0] = True
    sig.tp_long[:] = 101.0
    res, _ = _run(bid, ask, sig)
    t = res.trades[0]
    assert t.exit_reason is ExitReason.TARGET
    assert t.exit_price == 101.0, "a resting limit fills at its price, not better"


def test_short_stop_triggers_on_the_ask():
    bid, ask = flat_book(12, 100.0, 0.5)
    bid[5], ask[5] = 100.6, 101.1
    sig = Signals(4)
    sig.entry_short[0] = True
    sig.sl_short[:] = 101.0
    res, _ = _run(bid, ask, sig)
    t = res.trades[0]
    assert t.exit_reason is ExitReason.STOP
    assert t.exit_price == pytest.approx(101.1), "short stop must fill on the ASK"


# ---------------------------------------------------------------- partials
def test_partial_fills_walk_the_book_and_average():
    bid = np.full(12, 100.0)
    ask = np.full(12, 100.5)
    t = mk_tape(bid, ask, depth=1)
    t.ask_size = np.ones(12, dtype=np.int32)
    bars = bars_from_end_idx(t, END)
    sig = Signals(4)
    sig.entry_long[0] = True
    sig.size[0] = 3
    sig.exit_long[1] = True
    cfg = EngineConfig(commission_per_side=0.0, continuous=True, partial_fills=True)
    res = Backtester(cfg, GC).run(bars, sig)
    entry = res.orders[0]
    assert len(entry.fills) == 3, "one contract per row of depth"
    assert entry.state is OrderState.FILLED
    assert [f.tape_idx for f in entry.fills] == [2, 3, 4]
    assert res.trades[0].qty == 3


def test_partial_entry_that_never_completes_expires_with_a_position():
    bid = np.full(12, 100.0)
    ask = np.full(12, 100.5)
    t = mk_tape(bid, ask, depth=1)
    t.ask_size = np.ones(12, dtype=np.int32)
    bars = bars_from_end_idx(t, END)
    sig = Signals(4)
    sig.entry_long[0] = True
    sig.size[0] = 9                       # interval 0 only has 3 rows of depth
    sig.exit_long[1] = True
    cfg = EngineConfig(commission_per_side=0.0, continuous=True, partial_fills=True)
    res = Backtester(cfg, GC).run(bars, sig)
    entry = res.orders[0]
    assert entry.state is OrderState.EXPIRED
    assert entry.filled_qty == 3
    assert res.trades[0].qty == 3, "the position is what actually filled"


# ---------------------------------------------------------------- registry
def test_adapter_registry_has_exactly_one_entry_and_no_way_to_add_another():
    import engine.adapter as A

    assert available_adapters() == ("backtest",)
    assert not hasattr(A, "register")
    assert not hasattr(A, "register_adapter")
    with pytest.raises(TypeError):
        A._ADAPTERS["live"] = object          # MappingProxyType is read-only
    with pytest.raises(KeyError, match="hardening surface"):
        make_adapter("live")
    assert make_adapter().is_live is False
