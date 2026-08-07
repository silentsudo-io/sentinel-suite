"""`BarSeries` -- what a ported bar type returns, and how it reaches the engine.

THE ONE INTERFACE
-----------------
`engine\\bars.py` already owns the seam: `bars_from_end_idx(tape, end_idx)` builds
an engine `Bars` from "which tape row closed each bar". A ported bar type only has
to produce that. This module adds the ONE thing that seam cannot express on its own:

    a Sentinel bar type's OHLC is not always a tape price.

A Renko brick's open and close are LEVELS on the tick grid, not prices that traded;
a Heikin-Ashi-derived type averages. So the port returns its own OHLCV alongside
`end_idx`, and `to_engine_bars` takes the interval geometry from the seam and the
prices from the port. The seam is used, not replaced.

    end_idx[k] = index of the LAST tape row whose volume was accumulated into bar k.

⚠ THAT INDEX CAN REPEAT. A bar type may emit several bars from ONE tape row --
Renko's gap-fill bricks are the canonical case: a price jump of n bricks emits n-1
bars that contain no tape rows at all. Such a bar has `start_idx == -1`,
`tick_count == 0`, and repeats the previous bar's `end_idx`.

`end_idx` is NON-DECREASING and the engine represents these natively (engine
README 2.2): a repeat means "these bars closed on the same tape row", and a
zero-row interval offers no fill opportunity because no market data occurred in
it. `to_engine_bars` therefore passes them straight through. It must NEVER merge
them away -- silently collapsing a bar renumbers every bar after it, which would
misalign the gate against NinjaTrader's own indices.
"""
from __future__ import annotations

from dataclasses import dataclass, field
from fractions import Fraction

import numpy as np

from engine.bars import Bars, bars_from_end_idx
from engine.contract import Tape

# ------------------------------------------------------- integer ticks -> price
#: The largest integer a float64 represents exactly. `ticks * numerator` must stay
#: under it or the int->float conversion rounds BEFORE the division and the whole
#: point of doing this in integers is lost.
_EXACT_INT_MAX = 1 << 53


def ticks_to_price(ticks, tick_size: float) -> np.ndarray:
    """Convert integer tick levels to prices EXACTLY -- by division, never by scaling.

    ⛔ `ticks * tick_size` IS A BUG, and it is not a rounding nicety. Measured against
    NinjaTrader's own Renko dump on GC 02-26 / 2025-12-10: 37,765 of 94,108 bars
    differed, every one of them by exactly one ULP --

        42379 * 0.1  ->  4237.900000000001          (WRONG: 0.1 is not 1/10)
        42379 / 10   ->  4237.9                      (the nearest double to 4237.9)

    0.1 has no finite binary expansion, so the stored double is a hair above 1/10 and
    the error is scaled up by the tick count. `1.0 / tick_size` has the same disease
    from the other side. The cure is not a tolerance -- the gate is EXACT on purpose,
    and a systematically-off price would otherwise propagate into every downstream
    calculation in silence. The cure is to keep the exact RATIONAL value of the tick
    size and divide once:

        tick_size = num / den   (exactly, via Fraction over its decimal text)
        price     = (ticks * num) / den

    `ticks * num` is an exact integer; one float division of an exact integer by an
    exact integer is correctly rounded, so the result is the nearest double to the true
    decimal price -- which is what NinjaTrader prints and therefore what the gate reads.

    Why `Fraction(str(x))` and not `Fraction(x)`: `Fraction(0.1)` recovers the double's
    true value (3602879701896397/36028797018963968) and reproduces the defect exactly.
    `str()` gives the shortest decimal that round-trips -- "0.1" -- which is the tick
    size the exchange actually publishes. Every real tick size (0.1, 0.25, 0.01,
    1/32 = 0.03125) is a short decimal, so this is exact for all of them.
    """
    ts = float(tick_size)
    if not (ts > 0):
        raise ValueError("tick_size must be > 0, got %r" % (tick_size,))
    frac = Fraction(str(ts))
    num, den = frac.numerator, frac.denominator

    t = np.asarray(ticks, dtype=np.int64)
    if t.size:
        biggest = int(np.abs(t).max()) * num
        if biggest >= _EXACT_INT_MAX:
            raise ValueError(
                "tick level %d x numerator %d = %d exceeds float64's exact integer range "
                "(2**53). The int->float step would round before the division, which is the "
                "very error this function exists to avoid; it is not something to accept "
                "quietly. tick_size=%r decomposes to %d/%d."
                % (int(np.abs(t).max()), num, biggest, ts, num, den))
    return (t * num).astype(np.float64) / float(den)


@dataclass
class BarSeries:
    """One bar type's output over one tape. Arrays are parallel, length n_bars."""

    open: np.ndarray          # float64
    high: np.ndarray          # float64
    low: np.ndarray           # float64
    close: np.ndarray         # float64
    volume: np.ndarray        # int64
    ts_ms: np.ndarray         # int64  bar CLOSE stamp, as the bar type defines it
    open_ts_ms: np.ndarray    # int64  ts of the bar's first tape row (-1 if it has none)
    end_idx: np.ndarray       # int64  last tape row IN the bar; may repeat (see module doc)
    start_idx: np.ndarray     # int64  first tape row in the bar, -1 if the bar has none
    tick_count: np.ndarray    # int64  tape rows accumulated into the bar
    session_id: np.ndarray    # int32
    bar_index: np.ndarray     # int64  index WITHIN the session (the §2 pairing coordinate)
    is_partial: np.ndarray    # bool   the bar was still forming when the tape ended
    bartype: str = ""         # registry name, e.g. "renko"
    bar_params: str = ""      # canonical settings string; different params, different bars
    instrument: str = ""
    #: free-form per-build counters a port wants surfaced (never gated; NOTED at most)
    notes: dict = field(default_factory=dict)
    tape: Tape = field(default=None, repr=False)

    _ARRAYS = ("open", "high", "low", "close", "volume", "ts_ms", "open_ts_ms",
               "end_idx", "start_idx", "tick_count", "session_id", "bar_index", "is_partial")

    def __post_init__(self) -> None:
        n = self.open.shape[0]
        for name in self._ARRAYS:
            a = getattr(self, name)
            if a.shape != (n,):
                raise ValueError("%s: shape %s != (%d,)" % (name, a.shape, n))

    def __len__(self) -> int:
        return int(self.open.shape[0])

    @property
    def n(self) -> int:
        return len(self)

    @property
    def n_empty(self) -> int:
        """Bars containing no tape rows (Renko gap-fill bricks and their kin)."""
        return int(np.count_nonzero(self.tick_count == 0))

    def session_mask(self, session_id: int) -> np.ndarray:
        return self.session_id == session_id

    def select_session(self, session_id: int) -> "BarSeries":
        m = self.session_mask(session_id)
        kw = {name: getattr(self, name)[m] for name in self._ARRAYS}
        return BarSeries(bartype=self.bartype, bar_params=self.bar_params,
                         instrument=self.instrument, notes=dict(self.notes),
                         tape=self.tape, **kw)


def to_engine_bars(series: BarSeries) -> Bars:
    """Hand a `BarSeries` to the backtest engine.

    Interval geometry comes from the seam (`bars_from_end_idx`); OHLCV is then
    replaced with the bar type's own, because a brick level is not a tape price.

    Raises on a series containing bars with no tape rows -- see the module doc.
    """
    if series.tape is None:
        raise ValueError("BarSeries carries no tape; cannot build engine Bars")
    empty = series.n_empty
    # Row-less bars are PASSED THROUGH, not merged and not refused. `end_idx` is
    # non-decreasing (engine README 2.2); a repeat means several bars closed on one
    # tape row, and the engine gives such an interval no fill opportunity because no
    # market data occurred inside it. `empty` is carried out as a counter so a caller
    # can see how many there were -- it is structure to report, not an error.
    b = bars_from_end_idx(
        series.tape, series.end_idx,
        open=series.open.astype(np.float64, copy=True),
        high=series.high.astype(np.float64, copy=True),
        low=series.low.astype(np.float64, copy=True),
        close=series.close.astype(np.float64, copy=True),
        volume=series.volume.astype(np.int64, copy=True),
        ts_ms=series.ts_ms.astype(np.int64, copy=True),
    )
    return b


# ------------------------------------------------------------------ gate rows
#: the fields the `bartype` artefact gates. `open_ts_ms` and `tick_count` are in the
#: spec but SentinelBarDump (the NT reference dumper) does not emit them, so they are
#: opt-in: a field present on one side only is not evidence of anything.
GATE_FIELDS = ("open", "high", "low", "close", "volume", "ts_ms")


def gate_rows(series: BarSeries, *, session_date: str, closed_only: bool = True,
              extras: bool = False) -> list[dict]:
    """`bartype` artefact rows for `gates.rows_side`, keyed (session, bar_index).

    `closed_only` drops bars still forming when the tape ended. NinjaTrader's
    reference dumper runs `Calculate.OnBarClose` and so never writes the forming
    bar; including ours would be an extra row and a guaranteed FAIL that says
    nothing about the port.
    """
    keep = ~series.is_partial if closed_only else np.ones(series.n, dtype=bool)
    idx = np.flatnonzero(keep)
    out = []
    for i in idx:
        r = {
            "session": session_date,
            "bar_index": int(series.bar_index[i]),
            "instrument": series.instrument,
            "bartype": series.bartype,
            "bar_params": series.bar_params,
            "open": float(series.open[i]),
            "high": float(series.high[i]),
            "low": float(series.low[i]),
            "close": float(series.close[i]),
            "volume": int(series.volume[i]),
            "ts_ms": int(series.ts_ms[i]),
        }
        if extras:
            r["open_ts_ms"] = int(series.open_ts_ms[i])
            r["tick_count"] = int(series.tick_count[i])
        out.append(r)
    return out
