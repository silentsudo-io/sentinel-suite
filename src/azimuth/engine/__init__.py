"""Sentinel Azimuth -- the engine (spec §6, §1.1, §4.3, §3.1).

One engine behind chart, analyzer, optimizer and WFA. A real order model, an
execution adapter with exactly one shipped implementation, fills at the crossing
price, millisecond timestamps that never snap to a bar boundary.

    from engine import Backtester, EngineConfig, Signals, Strategy
    from engine.contract import synth_tape        # fixtures until tape supply lands
    from engine.bars import time_bars

⛔ `adapter.make_adapter` ships with exactly one entry ("backtest") and no
   runtime way to add another. A live adapter is gated behind the hardening
   surface (kill switch, governor, session gates, prop rules) existing on this
   side -- see adapter.py.
"""
from .adapter import (BacktestAdapter, ExecEvent, ExecutionAdapter,
                      available_adapters, make_adapter)
from .bars import Bars, tick_bars, time_bars
from .config import (AmbiguousExit, EXIT_PRIORITY, EngineConfig, ExitReason,
                     InstrumentSpec, LegReason, PositionMode, ScalingMode,
                     TouchResolution, spec_for)
from .contract import Tape, TapeContractError, load_sessions, synth_tape, validate
from .engine import Backtester
from .orders import (Fill, Order, OrderState, OrderStateError, OrderType,
                     Position, Purpose, Side)
from .results import BacktestResult, Leg, Trade
from .strategy import MarketContext, SignalError, Signals, Strategy

__version__ = "0.1.0"

__all__ = [
    "Backtester", "EngineConfig", "InstrumentSpec", "spec_for",
    "TouchResolution", "PositionMode", "ScalingMode", "LegReason",
    "ExitReason", "EXIT_PRIORITY", "AmbiguousExit",
    "Signals", "Strategy", "MarketContext", "SignalError",
    "Order", "OrderState", "OrderType", "OrderStateError", "Side", "Purpose",
    "Fill", "Position",
    "ExecutionAdapter", "BacktestAdapter", "ExecEvent", "make_adapter",
    "available_adapters",
    "Tape", "TapeContractError", "load_sessions", "synth_tape", "validate",
    "Bars", "time_bars", "tick_bars",
    "BacktestResult", "Trade", "Leg",
    "__version__",
]
