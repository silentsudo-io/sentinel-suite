"""Engine configuration and the DECLARED semantic choices.

Everything in this module that could otherwise be "an accident of code order" is
named, defaulted and testable here. If a fidelity question has more than one
defensible answer, it becomes an enum in this file -- not an `if` buried in a loop.

Spec: Docs/SENTINEL_AZIMUTH_SPEC.md  §1.1, §4.3, §6
"""
from __future__ import annotations

from dataclasses import dataclass, field
from enum import Enum, IntEnum


class ExitReason(IntEnum):
    """Why a position closed.

    The integer VALUE is the tie-break priority when two exits resolve to the
    same tape row: LOWER WINS. It is deliberately an explicit, orderable number
    so `EXIT_PRIORITY` below is a decision you can read, not an emergent one.
    """

    STOP = 0
    TARGET = 1
    SIGNAL = 2
    FORCE_FLAT_SESSION = 3
    FORCE_FLAT_ROLLOVER = 4
    FORCE_FLAT_EOD = 5      # end of tape, position still open


#: Documented resolution order when several exits land on the SAME tape row.
#: Read it top-down: the first reason in this tuple wins.
EXIT_PRIORITY: tuple[ExitReason, ...] = (
    ExitReason.STOP,
    ExitReason.TARGET,
    ExitReason.SIGNAL,
    ExitReason.FORCE_FLAT_SESSION,
    ExitReason.FORCE_FLAT_ROLLOVER,
    ExitReason.FORCE_FLAT_EOD,
)


class TouchResolution(Enum):
    """How the engine resolves stop AND target being touched in the same interval.

    At TICK resolution this is usually not ambiguous at all: a quote row carries
    one bid and one ask, so for a long, `bid <= sl` and `bid >= tp` cannot both
    be true on the same row (unless sl >= tp, which is rejected as invalid). The
    ambiguity survives in exactly three places:

      1. two tape rows sharing the same `ts_ms` (the contract permits ties),
      2. a coarse tape where a whole bar's extremes straddle both levels,
      3. an unobserved path BETWEEN two consecutive rows that crossed both.

    The engine detects each of those, resolves by this policy, and COUNTS it
    (`BacktestResult.ambiguous_exits`). A run that reports 0 is a run whose exits
    were never guessed.
    """

    STOP_FIRST = "stop_first"        # pessimistic. DEFAULT.
    TARGET_FIRST = "target_first"    # optimistic. For sensitivity analysis only.
    ROW_ORDER = "row_order"          # trust the tape's row order within the ms;
                                     # falls back to STOP_FIRST on an identical row.
    STRICT = "strict"                # raise AmbiguousExit -- prove it never happens.


class LegReason(Enum):
    """Why a single execution leg happened.

    A TRADE is entry -> flat. A LEG is one execution inside it. Opening,
    scaling and closing are all legs; only the last leg of a trade carries a
    closing reason.
    """

    ENTRY = "entry"
    SCALE_IN = "scale_in"
    SCALE_OUT = "scale_out"
    STOP = "stop"
    TARGET = "target"
    SIGNAL = "signal"
    FORCE_FLAT_SESSION = "force_flat_session"
    FORCE_FLAT_ROLLOVER = "force_flat_rollover"
    FORCE_FLAT_EOD = "force_flat_eod"

    @property
    def is_closing(self) -> bool:
        return self in _CLOSING_LEGS

    @staticmethod
    def from_exit(r: "ExitReason") -> "LegReason":
        return LegReason[r.name]


_CLOSING_LEGS = frozenset({
    LegReason.SCALE_OUT, LegReason.STOP, LegReason.TARGET, LegReason.SIGNAL,
    LegReason.FORCE_FLAT_SESSION, LegReason.FORCE_FLAT_ROLLOVER, LegReason.FORCE_FLAT_EOD,
})


class ScalingMode(Enum):
    """Whether a trade may change size without changing sign.

    `position[]` is the authoritative net target, so scaling is a change in that
    target, not a new concept. Each change emits a LEG; the trade still runs
    entry -> flat.

    ⚠ `STRICT` is the pre-scaling guarantee, kept available for callers that
    want "one fill in, one fill out" and want a loud failure if a strategy
    quietly starts scaling. It is NOT the default.
    """

    STRICT = "strict"          # any same-sign size change raises
    SCALE_OUT = "scale_out"    # DEFAULT. May reduce; increasing (pyramiding) raises.
    FULL = "full"              # may reduce AND pyramid


class PositionMode(Enum):
    """Precedence between the `entry_*/exit_*` arrays and the `position` array."""

    SIGNALS = "signals"      # entry_*/exit_* are authoritative; `position` ignored
    TARGET = "target"        # `position` is authoritative; entry_*/exit_* rejected
    AUTO = "auto"            # TARGET if `position` is populated, else SIGNALS


@dataclass(frozen=True)
class EngineConfig:
    # ---- execution model -------------------------------------------------
    #: Adverse ticks added to MARKET and STOP fills. Limit fills do NOT slip in
    #: price (a limit slips in *whether* it fills, not at what price).
    slippage_ticks: float = 0.0
    #: Commission charged per contract per SIDE, in account currency.
    commission_per_side: float = 2.0
    #: Model queue depletion on entry orders using bid_size/ask_size.
    partial_fills: bool = False
    #: Bars an entry LIMIT stays working before it is cancelled. 1 = the interval
    #: it was placed in only.
    entry_limit_lifetime_bars: int = 1
    #: A resting limit needs the book to trade THROUGH the price (strict <),
    #: not merely touch it (<=). Touch is the default; through is the harsher model.
    limit_requires_through: bool = False

    # ---- semantics -------------------------------------------------------
    touch_resolution: TouchResolution = TouchResolution.STOP_FIRST
    position_mode: PositionMode = PositionMode.AUTO
    #: Whether an open position may change size. Scaling OUT is on (the live
    #: thesis is the exit policy and scale-and-trail is the obvious candidate
    #: family); scaling IN (pyramiding) is off so nobody gets it by accident.
    scaling: ScalingMode = ScalingMode.SCALE_OUT
    #: An exit signal on the SAME bar that opened the position is ignored.
    ignore_exit_on_entry_bar: bool = True
    #: A position may never be closed on the same tape row it opened on.
    ignore_exit_on_entry_row: bool = True

    # ---- session / continuity -------------------------------------------
    warmup_days: int = 0
    #: True  -> a position may be carried across a session boundary.
    #: False -> force flat on the last row of every session.
    continuous: bool = False
    #: Force flat on the last row before the contract changes. Always on: a
    #: rollover is a different instrument, not a gap.
    force_flat_on_rollover: bool = True
    #: A tape gap this long or longer forces flat even mid-session.
    force_flat_gap_ms: int = 0     # 0 = disabled

    # ---- performance -----------------------------------------------------
    #: Skip intervals that provably cannot execute anything, using precomputed
    #: per-interval bid/ask extremes. Purely an optimisation: `tests/
    #: test_engine_semantics.py::test_fast_filter_is_a_pure_optimisation`
    #: asserts filtered and unfiltered runs produce byte-identical trades.
    fast_interval_filter: bool = True
    #: Keep the full Order ledger (every order object, every state transition).
    #: Off makes long sweeps cheaper; the trade list is unaffected.
    record_orders: bool = True

    def __post_init__(self) -> None:
        if self.entry_limit_lifetime_bars < 1:
            raise ValueError("entry_limit_lifetime_bars must be >= 1")
        if self.slippage_ticks < 0:
            raise ValueError("slippage_ticks must be >= 0")
        if self.warmup_days < 0:
            raise ValueError("warmup_days must be >= 0")


@dataclass(frozen=True)
class InstrumentSpec:
    """Contract specification. Prices are in price units; PnL is in currency."""

    symbol: str
    tick_size: float
    tick_value: float
    #: currency per 1.0 of price movement per contract
    point_value: float = field(default=0.0)

    def __post_init__(self) -> None:
        if self.tick_size <= 0:
            raise ValueError("tick_size must be > 0")
        if self.point_value == 0.0:
            object.__setattr__(self, "point_value", self.tick_value / self.tick_size)


#: The instruments the suite actually trades (pathlab.py TICK/TICKVAL, same numbers).
KNOWN_INSTRUMENTS = {
    "GC": InstrumentSpec("GC", 0.1, 10.0),
    "MGC": InstrumentSpec("MGC", 0.1, 1.0),
    "NQ": InstrumentSpec("NQ", 0.25, 5.0),
    "MNQ": InstrumentSpec("MNQ", 0.25, 0.5),
    "ES": InstrumentSpec("ES", 0.25, 12.5),
    "MES": InstrumentSpec("MES", 0.25, 1.25),
}


def spec_for(symbol: str) -> InstrumentSpec:
    root = str(symbol).upper().split()[0]
    for n in (3, 2):
        if root[:n] in KNOWN_INSTRUMENTS:
            return KNOWN_INSTRUMENTS[root[:n]]
    raise KeyError(
        f"no InstrumentSpec for {symbol!r}; pass one explicitly rather than guessing "
        f"a tick size (known: {sorted(KNOWN_INSTRUMENTS)})"
    )


class AmbiguousExit(Exception):
    """Both protective levels resolved to the same tape row under TouchResolution.STRICT."""
