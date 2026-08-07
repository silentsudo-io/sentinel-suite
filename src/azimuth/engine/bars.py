"""Derived bars + the INTERVAL geometry the engine's fidelity rests on.

⚠ This is NOT the Sentinel bar-type port. TBars / Flux / BRK / CVB / Drift / Flow
live in the sibling `Azimuth\\bars\\` track with their own parity gates (spec §4).
What lives here is the minimum a backtest engine needs to exist: time bars and
tick bars derived from the tape, plus the index geometry below. Any bar type from
the sibling track can drive this engine by handing it a `Bars` with a valid
`end_idx` -- that is the whole interface.

⭐ Bars are always DERIVED, never stored as truth (§3.1).

THE INTERVAL GEOMETRY -- read this before reading engine.py
-----------------------------------------------------------
`end_idx[k]` is the index of the LAST tape row belonging to bar k.

    interval k  ==  tape rows (end_idx[k], end_idx[k+1]]     for k in [0, n_bars-2)

A decision taken at bar k's CLOSE is worked over interval k. This is the single
rule that keeps the engine free of lookahead: `sl_long[k]` is the stop that was
in force from bar k's close until bar k+1's close -- never during bar k itself,
which is data the strategy had not seen when it chose the price.

Bar k+1's rows ARE exactly interval k's rows, so the per-interval bid/ask extremes
below are the same numbers as bar k+1's high/low of the book. They are precomputed
ONCE per tape and shared across every combo of a sweep; they are what makes the
engine skip 99% of intervals without ever guessing.

⭐ `end_idx` IS NON-DECREASING, NOT STRICTLY INCREASING
-------------------------------------------------------
A threshold-crossing bar clock -- Renko, brick, range, and plausibly Flux and
TBars -- prints SEVERAL bars from ONE tape row when price jumps far enough to
break multiple levels at once. Those bars carry zero rows and zero volume.
Measured on real tape, **35.7% of Renko 1/1 bars are row-less** (672,685 of
1,885,078) and Renko 1/1 is the largest bartag in the corpus. A strictly
increasing `end_idx` quietly assumes every bar contains market data, and that
assumption is false for most of the clocks this suite actually trades.

So equal consecutive `end_idx` values are LEGAL and mean "these bars closed on
the same tape row". The consequences are physical, not conventions:

  * **A zero-row interval offers NO fill opportunity.** No entry, no exit, no
    stop and no target can trigger inside it -- a fill needs a quote or a trade
    to fill against, and there was none.
  * **A decision taken at a zero-row bar's close CARRIES FORWARD** to the next
    interval that has rows, where it is worked normally. It is not discarded and
    it never fills at the previous row's price.
  * **It is unambiguous.** There is exactly one lawful answer, so it is reported
    as structure (`Bars.n_empty_intervals`, `BacktestResult.zero_row_intervals`)
    and NOT as an `ambiguous_exit`.

A DECREASING `end_idx` is still malformed input and still refuses loudly.
"""
from __future__ import annotations

from dataclasses import dataclass, field

import numpy as np

from .contract import KIND_TRADE, Tape


@dataclass
class Bars:
    """Bars over a tape, plus the derived interval geometry."""

    #: index of the last tape row in each bar; strictly increasing
    end_idx: np.ndarray
    ts_ms: np.ndarray        # bar close timestamp (== tape.ts_ms[end_idx])
    open: np.ndarray
    high: np.ndarray
    low: np.ndarray
    close: np.ndarray
    volume: np.ndarray
    session_id: np.ndarray   # per bar
    tape: Tape = field(repr=False)

    # ---- interval geometry, indexed 0..n_bars-2 -------------------------
    iv_start: np.ndarray = field(default=None, repr=False)     # first tape row
    iv_end: np.ndarray = field(default=None, repr=False)       # last tape row (inclusive)
    iv_bid_min: np.ndarray = field(default=None, repr=False)
    iv_bid_max: np.ndarray = field(default=None, repr=False)
    iv_ask_min: np.ndarray = field(default=None, repr=False)
    iv_ask_max: np.ndarray = field(default=None, repr=False)
    #: interval k contains at least one tape row (iv_start <= iv_end)
    iv_nonempty: np.ndarray = field(default=None, repr=False)

    def __post_init__(self) -> None:
        if self.iv_start is None:
            self._build_intervals()

    @property
    def n(self) -> int:
        return int(self.end_idx.shape[0])

    def _build_intervals(self) -> None:
        e = self.end_idx
        if e.shape[0] < 2:
            z = np.zeros(0, dtype=np.int64)
            zf = np.zeros(0, dtype=np.float64)
            self.iv_start, self.iv_end = z, z
            self.iv_bid_min = self.iv_bid_max = self.iv_ask_min = self.iv_ask_max = zf
            self.iv_nonempty = np.zeros(0, dtype=bool)
            return
        starts = (e[:-1] + 1).astype(np.int64)
        ends = e[1:].astype(np.int64)
        # ends == starts - 1  <=>  end_idx repeated  <=>  a zero-row interval: legal.
        # ends <  starts - 1  <=>  end_idx decreased: malformed, still refused.
        bad = np.flatnonzero(ends < starts - 1)
        if bad.size:
            i = int(bad[0])
            raise ValueError(
                f"end_idx decreases at bar {i + 1} ({int(e[i])} -> {int(e[i + 1])}); "
                f"it must be non-decreasing. (Equal values are legal and mean the bars "
                f"closed on the same tape row.)"
            )
        self.iv_start, self.iv_end = starts, ends
        nonempty = starts <= ends
        self.iv_nonempty = nonempty

        m = starts.shape[0]
        # A zero-row interval can never satisfy any price comparison, so its
        # extremes are seeded to values no protective price can reach. That is
        # the whole enforcement of "no fill opportunity" on the fast path.
        self.iv_bid_min = np.full(m, np.inf)
        self.iv_bid_max = np.full(m, -np.inf)
        self.iv_ask_min = np.full(m, np.inf)
        self.iv_ask_max = np.full(m, -np.inf)

        ne = np.flatnonzero(nonempty)
        if ne.size == 0:
            return
        # Non-empty intervals are CONTIGUOUS once the empties are dropped: if
        # intervals j+1..j+p are empty then end_idx[j+1] == end_idx[j+p+1], so
        # starts[j+p+1] == ends[j] + 1. reduceat over the compacted starts
        # therefore covers exactly the right rows.
        t = self.tape
        s2 = starts[ne]
        stop = int(ends[ne[-1]]) + 1
        self.iv_bid_min[ne] = np.minimum.reduceat(t.bid[:stop], s2)
        self.iv_bid_max[ne] = np.maximum.reduceat(t.bid[:stop], s2)
        self.iv_ask_min[ne] = np.minimum.reduceat(t.ask[:stop], s2)
        self.iv_ask_max[ne] = np.maximum.reduceat(t.ask[:stop], s2)

    @property
    def n_empty_intervals(self) -> int:
        """Intervals containing no tape rows (Renko gap-fill bricks and their kin)."""
        return 0 if self.iv_nonempty is None else int(np.count_nonzero(~self.iv_nonempty))

    # ---- session structure ---------------------------------------------
    def last_bar_of_session(self) -> np.ndarray:
        m = np.zeros(self.n, dtype=bool)
        if self.n:
            m[:-1] = np.diff(self.session_id) != 0
            m[-1] = True
        return m

    def contract_changes_after(self) -> np.ndarray:
        """True on bar k when the contract differs between session k and k+1."""
        m = np.zeros(self.n, dtype=bool)
        cons = self.tape.contracts
        if self.n and len(cons) > 1:
            nxt = np.diff(self.session_id) != 0
            idx = np.flatnonzero(nxt)
            for i in idx:
                a, b = int(self.session_id[i]), int(self.session_id[i + 1])
                if a < len(cons) and b < len(cons) and cons[a] != cons[b]:
                    m[i] = True
        return m

    def gap_after(self, gap_ms: int) -> np.ndarray:
        """True on bar k when the tape jumps >= gap_ms between bar k and k+1."""
        m = np.zeros(self.n, dtype=bool)
        if gap_ms > 0 and self.n > 1:
            t = self.tape
            # clipped: a row-less bar's end_idx+1 can run off the tape, and a gap
            # measured across a bar with no rows is meaningless anyway
            nxt = np.minimum(self.end_idx[:-1] + 1, len(t) - 1)
            m[:-1] = ((t.ts_ms[nxt] - self.ts_ms[:-1]) >= gap_ms) & self.iv_nonempty
        return m

    def session_dates(self) -> list[str]:
        return [
            str(s.get("session_date", s.get("contract", i)))
            for i, s in enumerate(self.tape.sessions)
        ]

    def warmup_mask(self, warmup_days: int) -> np.ndarray:
        """True on bars inside the first `warmup_days` sessions: signals are still
        computed there (indicators need the history) but entries are blocked."""
        if warmup_days <= 0:
            return np.zeros(self.n, dtype=bool)
        first = np.unique(self.session_id)[:warmup_days]
        return np.isin(self.session_id, first)


def _price_series(tape: Tape) -> np.ndarray:
    """Trade price where the row is a trade and it is finite, else the mid.

    A quote-only tape has no `last` at all; falling back to the mid keeps bar
    construction total instead of failing on a legal contract shape.
    """
    p = tape.mid.copy()
    tr = (tape.kind == KIND_TRADE) & np.isfinite(tape.last)
    p[tr] = tape.last[tr]
    return p


def _from_breaks(tape: Tape, brk: np.ndarray) -> Bars:
    """`brk[i] == True` -> tape row i is the last row of a bar."""
    end_idx = np.flatnonzero(brk).astype(np.int64)
    if end_idx.size == 0 or end_idx[-1] != len(tape) - 1:
        end_idx = np.append(end_idx, len(tape) - 1)
    starts = np.concatenate(([0], end_idx[:-1] + 1)).astype(np.int64)
    px = _price_series(tape)
    return Bars(
        end_idx=end_idx,
        ts_ms=tape.ts_ms[end_idx],
        open=px[starts],
        high=np.maximum.reduceat(px, starts),
        low=np.minimum.reduceat(px, starts),
        close=px[end_idx],
        volume=np.add.reduceat(tape.size.astype(np.int64), starts),
        session_id=tape.session_id[end_idx],
        tape=tape,
    )


def _ffill(x: np.ndarray) -> np.ndarray:
    idx = np.where(np.isfinite(x), np.arange(x.size), -1)
    np.maximum.accumulate(idx, out=idx)
    return np.where(idx >= 0, x[np.maximum(idx, 0)], np.nan)


def bars_from_end_idx(tape: Tape, end_idx, *, open=None, high=None, low=None,
                      close=None, volume=None, ts_ms=None) -> Bars:
    """Build Bars from explicit bar-close row indices.

    This IS the interface for the sibling `Azimuth\\bars\\` track: a ported
    Sentinel bar type only has to say which tape row closed each bar, and the
    engine runs on it unchanged.

    `end_idx` is NON-DECREASING -- repeats mean row-less bars (see the module
    doc). A row-less bar carries the previous close forward as its OHLC with
    zero volume, which is only a placeholder: a bar type whose OHLC is not a
    tape price (a Renko brick LEVEL, a Heikin-Ashi average) passes its own
    arrays through the keyword overrides, and should.
    """
    e = np.asarray(end_idx, dtype=np.int64)
    if e.size == 0:
        raise ValueError("end_idx is empty")
    d = np.diff(e)
    bad = np.flatnonzero(d < 0)
    if bad.size:
        i = int(bad[0])
        raise ValueError(
            f"end_idx decreases at bar {i + 1} ({int(e[i])} -> {int(e[i + 1])}); "
            f"it must be non-decreasing. (Equal values are legal and mean the bars "
            f"closed on the same tape row.)"
        )
    if e[0] < 0 or e[-1] >= len(tape):
        raise ValueError("end_idx runs outside the tape")

    n = e.size
    bar_start = np.concatenate(([0], e[:-1] + 1)).astype(np.int64)
    nonempty = bar_start <= e            # bar 0 always contains at least one row
    ne = np.flatnonzero(nonempty)

    px = _price_series(tape)
    o = np.full(n, np.nan)
    h = np.full(n, np.nan)
    lo_ = np.full(n, np.nan)
    c = np.full(n, np.nan)
    v = np.zeros(n, dtype=np.int64)

    s2 = bar_start[ne]
    stop = int(e[ne[-1]]) + 1
    o[ne] = px[s2]
    h[ne] = np.maximum.reduceat(px[:stop], s2)
    lo_[ne] = np.minimum.reduceat(px[:stop], s2)
    c[ne] = px[e[ne]]
    v[ne] = np.add.reduceat(tape.size[:stop].astype(np.int64), s2)

    # a row-less bar has no prices of its own; carry the last close forward
    c = _ffill(c)
    o = np.where(nonempty, o, c)
    h = np.where(nonempty, h, c)
    lo_ = np.where(nonempty, lo_, c)

    return Bars(
        end_idx=e,
        ts_ms=(tape.ts_ms[e] if ts_ms is None else np.asarray(ts_ms, dtype=np.int64)),
        open=o if open is None else np.asarray(open, dtype=np.float64),
        high=h if high is None else np.asarray(high, dtype=np.float64),
        low=lo_ if low is None else np.asarray(low, dtype=np.float64),
        close=c if close is None else np.asarray(close, dtype=np.float64),
        volume=v if volume is None else np.asarray(volume, dtype=np.int64),
        session_id=tape.session_id[e],
        tape=tape,
    )


def time_bars(tape: Tape, period_ms: int) -> Bars:
    """Wall-clock bars. Bars never span a session boundary."""
    if period_ms <= 0:
        raise ValueError("period_ms must be > 0")
    bucket = tape.ts_ms // period_ms
    brk = np.zeros(len(tape), dtype=bool)
    brk[:-1] = (np.diff(bucket) != 0) | (np.diff(tape.session_id) != 0)
    brk[-1] = True
    return _from_breaks(tape, brk)


def tick_bars(tape: Tape, n_ticks: int, *, trades_only: bool = True) -> Bars:
    """N-tick bars. `trades_only` counts only kind==1 rows, as NT does."""
    if n_ticks <= 0:
        raise ValueError("n_ticks must be > 0")
    counted = (tape.kind == KIND_TRADE) if trades_only else np.ones(len(tape), dtype=bool)
    run = np.cumsum(counted)
    bucket = (run - 1) // n_ticks
    brk = np.zeros(len(tape), dtype=bool)
    brk[:-1] = (np.diff(bucket) != 0) | (np.diff(tape.session_id) != 0)
    brk[-1] = True
    return _from_breaks(tape, brk)
