"""Trades, the run result, and the metrics -- including the mandatory crossing-cost axis.

⭐ §5.4: "Crossing cost is a required axis." Every trade carries `spread_cost_ticks`
and `spread_cost_ccy` measured as the ADVERSE distance from the mid on both legs,
so the optimizer can plot it and the analyzer can colour by it. It is not a
derived nicety -- THE HORIZON says the P&L *is* the crossing cost, so the engine
records it per trade at the moment of the fill and never reconstructs it later.
"""
from __future__ import annotations

from dataclasses import dataclass, field

import numpy as np

from .config import ExitReason, InstrumentSpec, LegReason
from .orders import Order, Side


@dataclass(frozen=True)
class Leg:
    """One execution inside a trade.

    A TRADE is entry -> flat. A LEG is a single fill that changed the net
    position: the opening leg, any scale-in or scale-out, and the closing leg.

    ⚠ PARITY: two engines can agree on entry, exit and net P&L while disagreeing
    on every partial in between. A parity gate over a scaling strategy MUST
    compare legs, not just trades (see README §10).
    """

    idx: int                 # position within the trade, 0-based
    trade_idx: int
    ts_ms: int
    tape_idx: int
    bar: int
    side: Side
    qty: int                 # always positive
    price: float
    qty_delta: int           # signed change to the net position
    position_after: int      # signed net position after this leg
    reason: LegReason
    order_id: int
    liquidity: str
    #: adverse distance from the mid on THIS leg, in currency
    spread_cost_ccy: float
    #: P&L realised BY this leg (0 on opening/scale-in legs)
    realised_pnl: float
    commission: float
    ambiguous: bool = False

    def to_dict(self) -> dict:
        d = {k: getattr(self, k) for k in self.__dataclass_fields__}
        d["side"] = self.side.name
        d["reason"] = self.reason.value
        return d


@dataclass
class Trade:
    idx: int
    direction: int                # +1 long, -1 short
    #: total contracts opened over the life of the trade (== total closed)
    qty: int
    #: largest absolute net position held at any point
    peak_qty: int

    entry_bar: int
    entry_tape_idx: int
    entry_ts_ms: int
    #: volume-weighted average of every OPENING leg
    entry_price: float

    exit_bar: int
    exit_tape_idx: int
    exit_ts_ms: int
    #: volume-weighted average of every CLOSING leg (including scale-outs)
    exit_price: float
    #: reason of the LAST leg -- what finally flattened it
    exit_reason: ExitReason

    sl_at_entry: float
    tp_at_entry: float
    entry_was_limit: bool

    gross_pnl: float
    commission: float
    net_pnl: float

    #: adverse distance from the mid, summed over every leg
    spread_cost_ticks: float
    spread_cost_ccy: float
    slippage_ccy: float

    #: tick-true excursions measured on the price you could actually have EXITED at
    mfe_ticks: float
    mae_ticks: float

    entry_order_id: int
    exit_order_id: int
    ambiguous_exit: bool
    legs: list[Leg] = field(default_factory=list)
    tags: dict[str, bool] = field(default_factory=dict)

    @property
    def duration_ms(self) -> int:
        return int(self.exit_ts_ms - self.entry_ts_ms)

    @property
    def n_legs(self) -> int:
        return len(self.legs)

    @property
    def was_scaled(self) -> bool:
        return any(l.reason in (LegReason.SCALE_IN, LegReason.SCALE_OUT) for l in self.legs)

    def to_dict(self) -> dict:
        skip = {"tags", "legs"}
        d = {k: getattr(self, k) for k in self.__dataclass_fields__ if k not in skip}
        d["exit_reason"] = self.exit_reason.name
        d["duration_ms"] = self.duration_ms
        d["n_legs"] = self.n_legs
        d["was_scaled"] = self.was_scaled
        d.update({f"tag_{k}": v for k, v in self.tags.items()})
        return d


@dataclass
class BacktestResult:
    trades: list[Trade]
    orders: list[Order]
    spec: InstrumentSpec
    n_bars: int
    n_tape_rows: int
    ambiguous_exits: int = 0
    entries_blocked_conflict: int = 0
    entries_blocked_warmup: int = 0
    entries_blocked_filter: int = 0
    limit_entries_expired: int = 0
    #: intervals containing no tape rows (Renko gap-fill bricks and their kin).
    #: STRUCTURE, not ambiguity: a zero-row interval has exactly one lawful
    #: reading -- no fill opportunity -- so it is reported, never guessed.
    zero_row_intervals: int = 0
    params: dict = field(default_factory=dict)

    # ---- vectors --------------------------------------------------------
    def pnl(self) -> np.ndarray:
        return np.array([t.net_pnl for t in self.trades], dtype=np.float64)

    def equity(self) -> np.ndarray:
        return np.cumsum(self.pnl())

    def metrics(self) -> dict:
        p = self.pnl()
        n = p.size
        if n == 0:
            return {
                "n_trades": 0, "net_pnl": 0.0, "gross_pnl": 0.0, "commission": 0.0,
                "spread_cost_ccy": 0.0, "win_rate": 0.0, "expectancy": 0.0,
                "profit_factor": 0.0, "max_drawdown": 0.0, "sharpe": 0.0,
                "avg_win": 0.0, "avg_loss": 0.0, "ambiguous_exits": self.ambiguous_exits,
                "n_legs": 0, "n_scaled_trades": 0,
            }
        eq = np.cumsum(p)
        dd = eq - np.maximum.accumulate(eq)
        wins, losses = p[p > 0], p[p <= 0]
        gw, gl = float(wins.sum()), float(-losses.sum())
        return {
            "n_trades": int(n),
            "net_pnl": float(p.sum()),
            "gross_pnl": float(sum(t.gross_pnl for t in self.trades)),
            "commission": float(sum(t.commission for t in self.trades)),
            "spread_cost_ccy": float(sum(t.spread_cost_ccy for t in self.trades)),
            "win_rate": float(100.0 * wins.size / n),
            "expectancy": float(p.mean()),
            "profit_factor": float(gw / gl) if gl else float("inf"),
            "max_drawdown": float(dd.min()) if dd.size else 0.0,
            "sharpe": float(p.mean() / p.std(ddof=1) * np.sqrt(n)) if n > 1 and p.std(ddof=1) else 0.0,
            "avg_win": float(wins.mean()) if wins.size else 0.0,
            "avg_loss": float(losses.mean()) if losses.size else 0.0,
            "ambiguous_exits": self.ambiguous_exits,
            "n_legs": sum(t.n_legs for t in self.trades),
            "n_scaled_trades": sum(1 for t in self.trades if t.was_scaled),
        }

    def legs(self) -> list[Leg]:
        """Every execution leg across every trade, in chronological order.

        ⚠ This, not `trades`, is what a parity gate must compare when a strategy
        scales -- see README §10.
        """
        return [l for t in self.trades for l in t.legs]

    def to_leg_records(self) -> list[dict]:
        return [l.to_dict() for l in self.legs()]

    def exit_reason_counts(self) -> dict[str, int]:
        out: dict[str, int] = {}
        for t in self.trades:
            out[t.exit_reason.name] = out.get(t.exit_reason.name, 0) + 1
        return out

    def to_records(self) -> list[dict]:
        return [t.to_dict() for t in self.trades]

    def __repr__(self) -> str:
        m = self.metrics()
        return (f"<BacktestResult n={m['n_trades']} net={m['net_pnl']:+.2f} "
                f"spread_cost={m['spread_cost_ccy']:.2f} wr={m['win_rate']:.1f}% "
                f"ambiguous={self.ambiguous_exits}>")
