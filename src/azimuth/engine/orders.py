"""The order model (§1.1.1) -- real order objects with a real state machine.

    "The engine's order model is a REAL order model -- order objects with state,
     working/filled/cancelled transitions, partial fills, one position at a time,
     an SL/TP cascade -- not a vectorised shortcut that only produces P&L."

Every transition goes through `Order.transition()`, which refuses illegal moves.
That is deliberate: the reason this model exists is so a broker adapter is a
later implementation of the same interface rather than a rewrite, and a broker
will absolutely hand you a WORKING -> PARTIALLY_FILLED -> CANCELLED sequence that
a P&L-only shortcut has no vocabulary for.
"""
from __future__ import annotations

import itertools
from dataclasses import dataclass, field
from enum import Enum

from .config import ExitReason


class Side(Enum):
    BUY = 1
    SELL = -1

    @property
    def sign(self) -> int:
        return self.value

    @property
    def opposite(self) -> "Side":
        return Side.SELL if self is Side.BUY else Side.BUY


class OrderType(Enum):
    MARKET = "market"
    LIMIT = "limit"
    STOP_MARKET = "stop_market"       # protective stop; becomes a market order on touch


class OrderState(Enum):
    PENDING_NEW = "pending_new"
    WORKING = "working"
    PARTIALLY_FILLED = "partially_filled"
    FILLED = "filled"
    CANCELLED = "cancelled"
    REJECTED = "rejected"
    EXPIRED = "expired"                # lifetime elapsed with no fill

    @property
    def is_terminal(self) -> bool:
        return self in (OrderState.FILLED, OrderState.CANCELLED,
                        OrderState.REJECTED, OrderState.EXPIRED)

    @property
    def is_working(self) -> bool:
        return self in (OrderState.WORKING, OrderState.PARTIALLY_FILLED)


_ALLOWED: dict[OrderState, frozenset[OrderState]] = {
    OrderState.PENDING_NEW: frozenset({OrderState.WORKING, OrderState.REJECTED,
                                       OrderState.CANCELLED}),
    OrderState.WORKING: frozenset({OrderState.PARTIALLY_FILLED, OrderState.FILLED,
                                   OrderState.CANCELLED, OrderState.EXPIRED}),
    OrderState.PARTIALLY_FILLED: frozenset({OrderState.PARTIALLY_FILLED, OrderState.FILLED,
                                            OrderState.CANCELLED, OrderState.EXPIRED}),
    OrderState.FILLED: frozenset(),
    OrderState.CANCELLED: frozenset(),
    OrderState.REJECTED: frozenset(),
    OrderState.EXPIRED: frozenset(),
}


class OrderStateError(Exception):
    """An illegal order state transition was attempted."""


class Purpose(Enum):
    ENTRY = "entry"
    #: change the size of an OPEN position without changing its sign. A partial
    #: exit is a partial fill of the trade, which is why it lives in the order
    #: model rather than in a post-hoc P&L adjustment (spec §1.1.1).
    SCALE_IN = "scale_in"
    SCALE_OUT = "scale_out"
    EXIT_STOP = "exit_stop"
    EXIT_TARGET = "exit_target"
    EXIT_SIGNAL = "exit_signal"
    EXIT_FORCE_FLAT = "exit_force_flat"


@dataclass(frozen=True)
class Fill:
    order_id: int
    ts_ms: int
    tape_idx: int
    price: float
    qty: int
    side: Side
    #: 'taker' crossed the spread; 'maker' rested and was hit.
    liquidity: str


@dataclass(frozen=True)
class Amendment:
    ts_ms: int
    tape_idx: int
    field: str
    old: float
    new: float


_ids = itertools.count(1)


@dataclass
class Order:
    side: Side
    qty: int
    type: OrderType
    purpose: Purpose
    created_ts_ms: int
    created_tape_idx: int
    #: bar whose CLOSE produced this order (the decision bar)
    created_bar: int
    limit_price: float = float("nan")
    stop_price: float = float("nan")
    #: bars the order may stay working; -1 = until cancelled
    lifetime_bars: int = -1
    exit_reason: ExitReason | None = None

    id: int = field(default_factory=lambda: next(_ids))
    state: OrderState = OrderState.PENDING_NEW
    filled_qty: int = 0
    avg_fill_price: float = float("nan")
    fills: list[Fill] = field(default_factory=list)
    amendments: list[Amendment] = field(default_factory=list)
    terminal_ts_ms: int = -1
    terminal_tape_idx: int = -1
    note: str = ""

    # ---- state machine --------------------------------------------------
    def transition(self, to: OrderState, *, ts_ms: int = -1, tape_idx: int = -1,
                   note: str = "") -> None:
        if to not in _ALLOWED[self.state]:
            raise OrderStateError(
                f"order {self.id} ({self.purpose.value}): illegal transition "
                f"{self.state.value} -> {to.value}"
            )
        self.state = to
        if note:
            self.note = note
        if to.is_terminal:
            self.terminal_ts_ms = ts_ms
            self.terminal_tape_idx = tape_idx

    @property
    def remaining(self) -> int:
        return self.qty - self.filled_qty

    def apply_fill(self, fill: Fill) -> None:
        if not self.state.is_working:
            raise OrderStateError(
                f"order {self.id}: fill on a {self.state.value} order"
            )
        if fill.qty <= 0 or fill.qty > self.remaining:
            raise OrderStateError(
                f"order {self.id}: fill qty {fill.qty} outside remaining {self.remaining}"
            )
        notional = (0.0 if self.filled_qty == 0
                    else self.avg_fill_price * self.filled_qty)
        self.filled_qty += fill.qty
        self.avg_fill_price = (notional + fill.price * fill.qty) / self.filled_qty
        self.fills.append(fill)
        self.transition(
            OrderState.FILLED if self.remaining == 0 else OrderState.PARTIALLY_FILLED,
            ts_ms=fill.ts_ms, tape_idx=fill.tape_idx,
        )

    def amend(self, field_name: str, new: float, *, ts_ms: int, tape_idx: int) -> None:
        old = getattr(self, field_name)
        if old == new or (old != old and new != new):     # NaN == NaN guard
            return
        setattr(self, field_name, new)
        self.amendments.append(Amendment(ts_ms, tape_idx, field_name, old, new))

    def to_dict(self) -> dict:
        return {
            "id": self.id, "side": self.side.name, "type": self.type.value,
            "purpose": self.purpose.value, "state": self.state.value,
            "qty": self.qty, "filled_qty": self.filled_qty,
            "limit_price": self.limit_price, "stop_price": self.stop_price,
            "avg_fill_price": self.avg_fill_price,
            "created_ts_ms": self.created_ts_ms, "created_bar": self.created_bar,
            "terminal_ts_ms": self.terminal_ts_ms,
            "n_fills": len(self.fills), "n_amendments": len(self.amendments),
            "note": self.note,
        }


@dataclass
class Position:
    """One position at a time. `qty` is signed: >0 long, <0 short, 0 flat."""

    qty: int = 0
    avg_price: float = float("nan")
    entry_ts_ms: int = -1
    entry_tape_idx: int = -1
    entry_bar: int = -1
    entry_order_id: int = -1

    @property
    def is_flat(self) -> bool:
        return self.qty == 0

    @property
    def side(self) -> Side | None:
        if self.qty > 0:
            return Side.BUY
        if self.qty < 0:
            return Side.SELL
        return None

    @property
    def direction(self) -> int:
        return 0 if self.qty == 0 else (1 if self.qty > 0 else -1)
