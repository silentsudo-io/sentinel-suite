"""§1.1.1 -- a REAL order model, not a vectorised shortcut that only yields P&L."""
from __future__ import annotations

import pytest

from engine.orders import (Fill, Order, OrderState, OrderStateError, OrderType,
                           Position, Purpose, Side)


def mk(qty=3, side=Side.BUY) -> Order:
    return Order(side=side, qty=qty, type=OrderType.MARKET, purpose=Purpose.ENTRY,
                 created_ts_ms=1, created_tape_idx=0, created_bar=0)


def test_orders_have_identity_and_a_lifecycle():
    a, b = mk(), mk()
    assert a.id != b.id
    assert a.state is OrderState.PENDING_NEW
    a.transition(OrderState.WORKING)
    assert a.state.is_working and not a.state.is_terminal


@pytest.mark.parametrize("frm,to", [
    (OrderState.PENDING_NEW, OrderState.FILLED),
    (OrderState.FILLED, OrderState.CANCELLED),
    (OrderState.CANCELLED, OrderState.WORKING),
    (OrderState.EXPIRED, OrderState.FILLED),
])
def test_illegal_transitions_raise(frm, to):
    o = mk()
    o.state = frm
    with pytest.raises(OrderStateError, match="illegal transition"):
        o.transition(to)


def test_partial_fills_accumulate_a_volume_weighted_average():
    o = mk(qty=3)
    o.transition(OrderState.WORKING)
    o.apply_fill(Fill(o.id, 10, 0, 100.0, 1, Side.BUY, "taker"))
    assert o.state is OrderState.PARTIALLY_FILLED and o.remaining == 2
    o.apply_fill(Fill(o.id, 11, 1, 101.0, 2, Side.BUY, "taker"))
    assert o.state is OrderState.FILLED and o.remaining == 0
    assert o.avg_fill_price == pytest.approx((100.0 + 2 * 101.0) / 3)
    assert len(o.fills) == 2


def test_overfill_is_refused():
    o = mk(qty=1)
    o.transition(OrderState.WORKING)
    with pytest.raises(OrderStateError, match="outside remaining"):
        o.apply_fill(Fill(o.id, 10, 0, 100.0, 2, Side.BUY, "taker"))


def test_fill_on_a_terminal_order_is_refused():
    o = mk(qty=1)
    o.transition(OrderState.WORKING)
    o.transition(OrderState.CANCELLED)
    with pytest.raises(OrderStateError, match="fill on a cancelled order"):
        o.apply_fill(Fill(o.id, 10, 0, 100.0, 1, Side.BUY, "taker"))


def test_amendments_are_recorded_and_no_ops_are_not():
    o = mk()
    o.amend("stop_price", 99.0, ts_ms=1, tape_idx=0)
    o.amend("stop_price", 99.0, ts_ms=2, tape_idx=1)
    o.amend("stop_price", 99.5, ts_ms=3, tape_idx=2)
    assert [a.new for a in o.amendments] == [99.0, 99.5]


def test_position_is_signed_and_flat_by_default():
    p = Position()
    assert p.is_flat and p.side is None and p.direction == 0
    assert Position(qty=-2).direction == -1 and Position(qty=-2).side is Side.SELL
