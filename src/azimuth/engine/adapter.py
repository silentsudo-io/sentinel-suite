"""Execution adapters (§1.1.2) and the FROZEN adapter registry (§1.1.3).

    "Execution sits behind an adapter interface. `BacktestAdapter` is the first
     implementation. A broker adapter is a later implementation of the same
     interface, not a rewrite."

    ⛔ "Until then the adapter registry ships with exactly one entry and no way
       to add another at runtime."

That prohibition is enforced here, not merely observed: `_ADAPTERS` is a
`MappingProxyType`, there is no `register()`, no entry-point scan, no plugin
path, and `make_adapter` rejects any name that is not the one entry. A live
adapter requires the hardening surface (kill switch, governor, session gates,
prop rules) to exist on this side first; when it does, it is added HERE, in
source, in a reviewed diff.

THE FILL CONVENTION -- the reason the engine exists
---------------------------------------------------
    buy  fills at the ASK      sell fills at the BID

The suite has measured that its replay fills are unfaithful (0.00% of trades
print inside the spread) and that the P&L *is* the crossing cost. Nothing in
this file ever computes a mid price for a fill, and
`tests/test_fills.py::test_a_mid_price_fill_would_fail_this_test` fails loudly
if that ever changes.

    market / stop fills  -> taker, crossing price, `slippage_ticks` ADVERSE
    limit fills          -> maker, AT the limit price, no price slippage
                            (a limit slips in WHETHER it fills, not at what price)
    stop fills           -> `min(stop, bid)` long / `max(stop, ask)` short, so a
                            gap through the stop fills at the gapped price, never
                            better than the stop
"""
from __future__ import annotations

from abc import ABC, abstractmethod
from dataclasses import dataclass
from types import MappingProxyType

import numpy as np

from .config import AmbiguousExit, EXIT_PRIORITY, EngineConfig, InstrumentSpec, TouchResolution
from .contract import Tape
from .orders import Fill, Order, OrderState, OrderType, Purpose, Side


def first_true(mask: np.ndarray) -> int:
    """Index of the first True, or -1. O(n) at C speed, no Python loop."""
    if mask.size == 0:
        return -1
    i = int(mask.argmax())
    return i if bool(mask[i]) else -1


@dataclass(frozen=True)
class ExecEvent:
    kind: str                 # 'fill' | 'expire' | 'cancel'
    order: Order
    tape_idx: int
    ts_ms: int
    fill: Fill | None = None
    #: True when this exit's tape row was shared with the competing protective
    #: order and the outcome came from `TouchResolution`, not from the tape.
    ambiguous: bool = False


class ExecutionAdapter(ABC):
    """The seam between the engine's intent and a venue's reality.

    A backtest resolves intent against a recorded tape; a broker resolves it
    against a matching engine. Both answer the same four questions.
    """

    name: str = "abstract"
    is_live: bool = True     # subclasses that are NOT live must say so explicitly

    @abstractmethod
    def bind(self, tape: Tape, spec: InstrumentSpec, cfg: EngineConfig) -> None:
        """Attach to a market. Called once per run."""

    @abstractmethod
    def submit(self, order: Order) -> None:
        """PENDING_NEW -> WORKING (or REJECTED)."""

    @abstractmethod
    def cancel(self, order: Order, ts_ms: int, tape_idx: int,
               *, expired: bool = False, note: str = "") -> ExecEvent:
        """Working -> CANCELLED/EXPIRED."""

    @abstractmethod
    def work(self, lo: int, hi: int) -> list[ExecEvent]:
        """Advance the venue over tape rows [lo, hi] INCLUSIVE.

        Returns the chronologically first execution and nothing after it: the
        caller applies it, adjusts its working set, and calls again. Returns []
        when nothing executed in the window. A multi-fill sequence for a single
        order (partial fills) is returned as one list.
        """

    @abstractmethod
    def working(self) -> list[Order]:
        """Every order currently WORKING or PARTIALLY_FILLED."""


class BacktestAdapter(ExecutionAdapter):
    """The only shipped adapter. Resolves orders against a recorded tape."""

    name = "backtest"
    is_live = False

    def __init__(self) -> None:
        self._tape: Tape | None = None
        self._spec: InstrumentSpec | None = None
        self._cfg: EngineConfig | None = None
        self._working: list[Order] = []
        self.ambiguous_count = 0

    # ---- lifecycle ------------------------------------------------------
    def bind(self, tape: Tape, spec: InstrumentSpec, cfg: EngineConfig) -> None:
        self._tape, self._spec, self._cfg = tape, spec, cfg
        self._working = []
        self.ambiguous_count = 0

    def submit(self, order: Order) -> None:
        if order.type is OrderType.LIMIT and not np.isfinite(order.limit_price):
            order.transition(OrderState.REJECTED, note="limit order with no limit price")
            return
        if order.type is OrderType.STOP_MARKET and not np.isfinite(order.stop_price):
            order.transition(OrderState.REJECTED, note="stop order with no stop price")
            return
        order.transition(OrderState.WORKING)
        self._working.append(order)

    def cancel(self, order: Order, ts_ms: int, tape_idx: int,
               *, expired: bool = False, note: str = "") -> ExecEvent:
        order.transition(
            OrderState.EXPIRED if expired else OrderState.CANCELLED,
            ts_ms=ts_ms, tape_idx=tape_idx, note=note,
        )
        if order in self._working:
            self._working.remove(order)
        return ExecEvent("expire" if expired else "cancel", order, tape_idx, ts_ms)

    def working(self) -> list[Order]:
        return list(self._working)

    # ---- trigger geometry ----------------------------------------------
    def _trigger(self, o: Order, lo: int, hi: int) -> int:
        """First tape row in [lo, hi] where `o` would execute, or -1."""
        t, cfg = self._tape, self._cfg
        if lo > hi:
            return -1
        if o.type is OrderType.MARKET:
            return lo
        s = slice(lo, hi + 1)
        if o.type is OrderType.LIMIT:
            # A resting BUY limit fills when the offer comes down to it; a resting
            # SELL limit fills when the bid comes up to it.
            if o.side is Side.BUY:
                m = t.ask[s] < o.limit_price if cfg.limit_requires_through else t.ask[s] <= o.limit_price
            else:
                m = t.bid[s] > o.limit_price if cfg.limit_requires_through else t.bid[s] >= o.limit_price
        else:  # STOP_MARKET
            # A protective stop is triggered by the price you would EXIT at.
            if o.side is Side.SELL:                 # protecting a long
                m = t.bid[s] <= o.stop_price
            else:                                   # protecting a short
                m = t.ask[s] >= o.stop_price
        i = first_true(m)
        return -1 if i < 0 else lo + i

    def _fill_price(self, o: Order, idx: int) -> tuple[float, str]:
        t, cfg, spec = self._tape, self._cfg, self._spec
        slip = cfg.slippage_ticks * spec.tick_size
        if o.type is OrderType.LIMIT:
            # AT the limit. No price improvement modelled (conservative), no
            # slippage (a limit does not slip in price).
            return float(o.limit_price), "maker"
        if o.type is OrderType.STOP_MARKET:
            if o.side is Side.SELL:
                px = min(float(o.stop_price), float(t.bid[idx])) - slip
            else:
                px = max(float(o.stop_price), float(t.ask[idx])) + slip
            return px, "taker"
        # MARKET -- cross the spread.
        if o.side is Side.BUY:
            return float(t.ask[idx]) + slip, "taker"
        return float(t.bid[idx]) - slip, "taker"

    # ---- priority -------------------------------------------------------
    @staticmethod
    def _priority(o: Order) -> int:
        if o.purpose is Purpose.ENTRY:
            return -1     # an entry logically precedes any exit on the same row
        if o.exit_reason is None:
            return len(EXIT_PRIORITY)
        return EXIT_PRIORITY.index(o.exit_reason)

    def _resolve_tie(self, a: tuple[int, Order], b: tuple[int, Order]) -> tuple[int, Order]:
        """Stop AND target both resolved inside the same millisecond.

        `a` is the earlier candidate by (row, priority). Every branch here is a
        DECLARED policy from `TouchResolution` -- none of it is code order.
        """
        cfg = self._cfg
        self.ambiguous_count += 1
        if cfg.touch_resolution is TouchResolution.STRICT:
            raise AmbiguousExit(
                f"stop (order {a[1].id}) and target (order {b[1].id}) both triggered inside "
                f"ts_ms={int(self._tape.ts_ms[a[0]])} (rows {a[0]}/{b[0]}); "
                f"TouchResolution.STRICT refuses to guess"
            )
        if cfg.touch_resolution is TouchResolution.ROW_ORDER and a[0] != b[0]:
            return a
        want = (Purpose.EXIT_STOP if cfg.touch_resolution in
                (TouchResolution.STOP_FIRST, TouchResolution.ROW_ORDER)
                else Purpose.EXIT_TARGET)
        return a if a[1].purpose is want else b

    # ---- the venue clock -------------------------------------------------
    def work(self, lo: int, hi: int) -> list[ExecEvent]:
        t, cfg = self._tape, self._cfg
        if t is None:
            raise RuntimeError("adapter not bound")
        if lo > hi or not self._working:
            return []

        cands: list[tuple[int, int, Order]] = []
        for o in self._working:
            idx = self._trigger(o, lo, hi)
            if idx >= 0:
                cands.append((idx, self._priority(o), o))
        if not cands:
            return []

        cands.sort(key=lambda c: (c[0], c[1]))
        idx, _, winner = cands[0]

        ambiguous = False
        if len(cands) > 1:
            j_idx, _, other = cands[1]
            protective = {Purpose.EXIT_STOP, Purpose.EXIT_TARGET}
            same_ms = int(t.ts_ms[j_idx]) == int(t.ts_ms[idx])
            if (winner.purpose in protective and other.purpose in protective
                    and winner.purpose is not other.purpose and same_ms):
                ambiguous = True
                idx, winner = self._resolve_tie((idx, winner), (j_idx, other))

        return self._execute(winner, idx, hi, ambiguous)

    def _execute(self, o: Order, idx: int, hi: int, ambiguous: bool) -> list[ExecEvent]:
        t, cfg = self._tape, self._cfg
        if not (cfg.partial_fills and o.purpose is Purpose.ENTRY):
            px, liq = self._fill_price(o, idx)
            f = Fill(o.id, int(t.ts_ms[idx]), idx, px, o.remaining, o.side, liq)
            o.apply_fill(f)
            self._working.remove(o)
            return [ExecEvent("fill", o, idx, int(t.ts_ms[idx]), f, ambiguous)]

        # ---- partial fills: consume the resting size on the far side ------
        depth = t.ask_size if o.side is Side.BUY else t.bid_size
        events: list[ExecEvent] = []
        i = idx
        while i <= hi and o.remaining > 0:
            j = self._trigger(o, i, hi)
            if j < 0:
                break
            avail = int(depth[j])
            if avail <= 0:
                i = j + 1
                continue
            q = min(avail, o.remaining)
            px, liq = self._fill_price(o, j)
            f = Fill(o.id, int(t.ts_ms[j]), j, px, q, o.side, liq)
            o.apply_fill(f)
            events.append(ExecEvent("fill", o, j, int(t.ts_ms[j]), f, ambiguous))
            i = j + 1
        if o.state is OrderState.FILLED and o in self._working:
            self._working.remove(o)
        return events


# ------------------------------------------------------------------ registry
#
# ⛔ EXACTLY ONE ENTRY, AND NO RUNTIME WAY TO ADD ANOTHER (spec §1.1.3).
#
#    A live adapter does not ship until the hardening surface exists on this
#    side: kill switch, governor, session gates, prop rules. Those exist in
#    NinjaTrader and would have to be ported or fronted. There is deliberately
#    no `register()`, no plugin discovery and no environment override below --
#    adding an adapter is a source change in a reviewed diff, by design.
#
_ADAPTERS = MappingProxyType({BacktestAdapter.name: BacktestAdapter})


def available_adapters() -> tuple[str, ...]:
    return tuple(_ADAPTERS)


def make_adapter(name: str = BacktestAdapter.name) -> ExecutionAdapter:
    cls = _ADAPTERS.get(name)
    if cls is None:
        raise KeyError(
            f"unknown execution adapter {name!r}. The registry ships with exactly one "
            f"entry ({available_adapters()!r}) and there is no runtime way to add "
            f"another -- a live adapter is gated behind the hardening surface (spec §1.1.3)."
        )
    return cls()
