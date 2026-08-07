"""The strategy interface (§6): a strategy returns ALIGNED ARRAYS, one per bar.

    entry_long  exit_long  entry_short  exit_short
    sl_long  tp_long  sl_short  tp_short
    entry_limit_long  entry_limit_short
    block_entries  size  position
    + arbitrary `tag_name` bool arrays

A strategy computes; it does not execute. Everything about WHEN and AT WHAT
PRICE lives in the engine and the adapter, which is what makes one engine sit
behind chart, analyzer, optimizer and WFA, and what makes §5.2 possible: a tag
filter modifies `block_entries` and the engine RE-RUNS, so suppressing trade #3
genuinely frees the engine to take #4.

TIMING CONTRACT -- the rule that keeps this free of lookahead
--------------------------------------------------------------
Every array is indexed by BAR and read AT THAT BAR'S CLOSE. The decision taken
at bar k's close is worked over interval k == tape rows (end_idx[k], end_idx[k+1]].
`sl_long[k]` is therefore the stop in force from bar k's close to bar k+1's
close -- it is a TRAILING stop for free, and it can never be in force during
bar k, which the strategy had not finished seeing when it chose the price.
"""
from __future__ import annotations

from abc import ABC, abstractmethod
from dataclasses import dataclass, field

import numpy as np

from .bars import Bars
from .config import PositionMode, ScalingMode
from .contract import Tape

NAN = float("nan")

#: float arrays default to NaN (= "absent"), bool arrays to False, size to 0.
_FLOAT_FIELDS = ("sl_long", "tp_long", "sl_short", "tp_short",
                 "entry_limit_long", "entry_limit_short")
_BOOL_FIELDS = ("entry_long", "exit_long", "entry_short", "exit_short", "block_entries")


class SignalError(Exception):
    """The strategy returned arrays that the engine refuses to execute."""


@dataclass
class Signals:
    n: int
    entry_long: np.ndarray = None
    exit_long: np.ndarray = None
    entry_short: np.ndarray = None
    exit_short: np.ndarray = None
    sl_long: np.ndarray = None
    tp_long: np.ndarray = None
    sl_short: np.ndarray = None
    tp_short: np.ndarray = None
    entry_limit_long: np.ndarray = None
    entry_limit_short: np.ndarray = None
    block_entries: np.ndarray = None
    #: contracts per entry. 0 -> EngineConfig default (1).
    size: np.ndarray = None
    #: TARGET net position per bar. NaN = "no opinion". When populated it is
    #: authoritative and entry_*/exit_* must be absent (see PositionMode).
    position: np.ndarray = None
    #: arbitrary `tag_name -> bool[n]`; carried onto every trade and usable as
    #: a re-run filter (§5.2).
    tags: dict[str, np.ndarray] = field(default_factory=dict)

    # ---- filled in by `target_position_to_signals`, not by strategies -----
    #: `position` forward-filled to a concrete net target on every bar.
    target_position: np.ndarray | None = None
    #: bars where the target changes SIZE without changing sign (a scale leg).
    scale_bars: np.ndarray | None = None

    def __post_init__(self) -> None:
        for f in _BOOL_FIELDS:
            if getattr(self, f) is None:
                setattr(self, f, np.zeros(self.n, dtype=bool))
        for f in _FLOAT_FIELDS:
            if getattr(self, f) is None:
                setattr(self, f, np.full(self.n, NAN))
        if self.size is None:
            self.size = np.zeros(self.n, dtype=np.int64)
        if self.position is None:
            self.position = np.full(self.n, NAN)

    # ---- helpers --------------------------------------------------------
    def has_target_position(self) -> bool:
        return bool(np.isfinite(self.position).any())

    def mode(self, cfg_mode: PositionMode) -> PositionMode:
        if cfg_mode is not PositionMode.AUTO:
            return cfg_mode
        return PositionMode.TARGET if self.has_target_position() else PositionMode.SIGNALS

    def tag(self, name: str, arr: np.ndarray) -> "Signals":
        self.tags[name] = np.asarray(arr, dtype=bool)
        return self

    def validate(self, cfg_mode: PositionMode = PositionMode.AUTO) -> PositionMode:
        n = self.n
        for f in _BOOL_FIELDS:
            a = getattr(self, f)
            if a.shape != (n,):
                raise SignalError(f"{f}: shape {a.shape} != ({n},)")
            if a.dtype != np.bool_:
                raise SignalError(f"{f}: dtype {a.dtype} is not bool")
        for f in _FLOAT_FIELDS + ("position",):
            a = getattr(self, f)
            if a.shape != (n,):
                raise SignalError(f"{f}: shape {a.shape} != ({n},)")
            if a.dtype.kind != "f":
                raise SignalError(f"{f}: dtype {a.dtype} is not floating")
        if self.size.shape != (n,) or self.size.dtype.kind not in "iu":
            raise SignalError("size: must be integer, shape (n,)")
        if np.any(self.size < 0):
            raise SignalError("size: negative contract counts are not a short signal")
        for k, v in self.tags.items():
            if np.asarray(v).shape != (n,):
                raise SignalError(f"tag {k!r}: shape {np.asarray(v).shape} != ({n},)")

        # An inverted bracket is a strategy bug, and it is the one thing that
        # could make stop/target genuinely simultaneous on a single quote row.
        bad = np.isfinite(self.sl_long) & np.isfinite(self.tp_long) & (self.sl_long >= self.tp_long)
        if bad.any():
            raise SignalError(
                f"sl_long >= tp_long on {int(bad.sum())} bar(s), first at {int(np.flatnonzero(bad)[0])}: "
                f"an inverted long bracket has no unambiguous resolution"
            )
        bad = np.isfinite(self.sl_short) & np.isfinite(self.tp_short) & (self.sl_short <= self.tp_short)
        if bad.any():
            raise SignalError(
                f"sl_short <= tp_short on {int(bad.sum())} bar(s), first at {int(np.flatnonzero(bad)[0])}: "
                f"an inverted short bracket has no unambiguous resolution"
            )

        mode = self.mode(cfg_mode)
        if mode is PositionMode.TARGET:
            if (self.entry_long.any() or self.entry_short.any()
                    or self.exit_long.any() or self.exit_short.any()):
                raise SignalError(
                    "position[] is populated AND entry_*/exit_* are set. Two authorities "
                    "over the same decision is not a resolvable precedence -- pick one "
                    "(EngineConfig.position_mode forces the choice explicitly)."
                )
            p = self.position[np.isfinite(self.position)]
            if np.any(p != np.round(p)):
                raise SignalError("position: must be whole contracts")
        return mode


@dataclass
class MarketContext:
    """Everything a strategy may see. Read-only by convention."""

    tape: Tape
    bars: Bars

    @property
    def n(self) -> int:
        return self.bars.n

    def empty_signals(self) -> Signals:
        return Signals(self.bars.n)


class Strategy(ABC):
    """Plain object. `params` is what an optimizer sweeps."""

    def __init__(self, **params) -> None:
        self.params = dict(params)

    @property
    def name(self) -> str:
        return type(self).__name__

    @abstractmethod
    def generate(self, ctx: MarketContext) -> Signals:
        """Return aligned arrays for `ctx.bars`."""

    def describe(self) -> dict:
        return {"strategy": self.name, **self.params}


def target_position_to_signals(sig: Signals, scaling: ScalingMode) -> Signals:
    """Translate an authoritative `position[]` into entry / scale / exit intents.

    Semantics, stated because the spec lists `position` alongside `entry_*`
    without ranking them (see README "Underspecified"):
      * NaN means "no opinion" -> carry the previous target forward.
      * a change from 0 to +q is an entry_long of size q; 0 to -q an entry_short.
      * a change to 0 is an exit of whatever is open.
      * a SIGN FLIP is an exit AND an entry on the same bar; the exit fills
        first, then the entry, both on the same interval. One position at a
        time is preserved -- there is never a moment holding both.
      * a SAME-SIGN SIZE CHANGE is a SCALE LEG, not a new trade: the target is
        authoritative, so scaling is a change in that target rather than a new
        concept. `ScalingMode` decides which directions are permitted.

    ⭐ Scale legs are emitted at BAR resolution and filled at the market on the
    first tape row of the interval, exactly like every other decision here. A
    price-exact partial ("bank half AT +1R to the tick") would need a
    `scale_limit_*` array, which is not in the spec's list and is not invented
    here -- express the trigger in the strategy's bar logic instead.
    """
    src = sig.position
    # forward-fill "no opinion"; anything before the first opinion is flat
    idx = np.where(np.isfinite(src), np.arange(src.size), -1)
    np.maximum.accumulate(idx, out=idx)
    p = np.where(idx >= 0, src[np.maximum(idx, 0)], 0.0)
    prev = np.concatenate(([0.0], p[:-1]))

    out = Signals(sig.n, tags=sig.tags,
                  sl_long=sig.sl_long, tp_long=sig.tp_long,
                  sl_short=sig.sl_short, tp_short=sig.tp_short,
                  entry_limit_long=sig.entry_limit_long,
                  entry_limit_short=sig.entry_limit_short,
                  block_entries=sig.block_entries)

    scale = (np.sign(p) == np.sign(prev)) & (p != prev) & (p != 0) & (prev != 0)
    if scale.any():
        if scaling is ScalingMode.STRICT:
            raise SignalError(
                f"position[] changes size without changing sign at bar "
                f"{int(np.flatnonzero(scale)[0])}: EngineConfig.scaling is STRICT, "
                f"which guarantees one fill in and one fill out. Use "
                f"ScalingMode.SCALE_OUT (default) or FULL to allow it."
            )
        grew = scale & (np.abs(p) > np.abs(prev))
        if grew.any() and scaling is not ScalingMode.FULL:
            raise SignalError(
                f"position[] pyramids (grows an open position) at bar "
                f"{int(np.flatnonzero(grew)[0])}. Scaling IN is off by default so "
                f"nobody gets it by accident -- set EngineConfig.scaling = "
                f"ScalingMode.FULL to allow it."
            )

    # entries/exits are sign transitions only; same-sign resizes are scale legs
    out.exit_long = (prev > 0) & (p <= 0)
    out.exit_short = (prev < 0) & (p >= 0)
    out.entry_long = (p > 0) & (prev <= 0)
    out.entry_short = (p < 0) & (prev >= 0)
    out.size = np.abs(p).astype(np.int64)
    out.target_position = p
    out.scale_bars = scale
    return out
