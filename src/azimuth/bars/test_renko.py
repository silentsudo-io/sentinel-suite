"""Tests for the Renko port and the `bars` package plumbing.

    C:\\ntbv\\Scripts\\python.exe -m pytest bars\\test_renko.py -q
    C:\\ntbv\\Scripts\\python.exe bars\\test_renko.py          # same proofs, no pytest

Two kinds of test, and both are needed. The HAND WALK is the only thing that pins the
five behaviours the C# actually has (wickless bricks, the forming bar's stamp and
volume, row-less gap bricks, the session doji, the two-brick reversal) -- an invariant
suite would happily pass on a wrong-but-self-consistent Renko. The INVARIANTS over the
real `GC 02-26` tape are what say the port survives 1.5 million rows of real data.

⚠ Neither is the gate. Passing these does NOT mean the port matches NinjaTrader; only
`bars.gate` can say that. It HAS now been run (2026-08-05, all 17 `GC 02-26` sessions
against `20260805T015237__GC__11v1x1.jsonl`) and it does NOT yet pass -- see README.
Everything it caught that this file could have caught is now pinned below.
"""
from __future__ import annotations

import os
import sys

import numpy as np

_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if _ROOT not in sys.path:
    sys.path.insert(0, _ROOT)

import bars                                   # noqa: E402
from bars import ntdump, renko as renko_mod, series as series_mod, tapeio   # noqa: E402
from engine.contract import Tape              # noqa: E402

TICK = 0.1
INSTRUMENT = "GC 02-26"
SESSION = "2025-12-09"


# ---------------------------------------------------------------- fixtures
def tape_from_ticks(price_ticks, sizes=None, ts_ms=None, session_id=None,
                    tick_size: float = TICK) -> Tape:
    """A minimal all-trade tape at the given integer tick prices."""
    p = np.asarray(price_ticks, dtype=np.int64)
    n = p.shape[0]
    px = p.astype(np.float64) * tick_size
    sz = np.asarray(sizes if sizes is not None else np.ones(n), dtype=np.int32)
    ts = np.asarray(ts_ms if ts_ms is not None else np.arange(n) * 1000 + 1, dtype=np.int64)
    sid = np.asarray(session_id if session_id is not None else np.zeros(n), dtype=np.int32)
    return Tape(
        ts_ms=ts,
        bid=px - tick_size, ask=px + tick_size, last=px,
        size=sz,
        bid_size=np.ones(n, dtype=np.int32), ask_size=np.ones(n, dtype=np.int32),
        kind=np.ones(n, dtype=np.int8),
        session_id=sid,
        sessions=[{"session_date": "1970-01-01", "contract": "TEST"}] * (int(sid.max()) + 1),
        instrument="TEST",
    )


_REAL = {}


def real_session():
    """The real tape, loaded once. Skipped (not silently passed) if absent."""
    if "tape" not in _REAL:
        path = tapeio.session_path(INSTRUMENT, SESSION)
        _REAL["loaded"] = tapeio.load_sessions([path])
        _REAL["tape"] = _REAL["loaded"].tape
    return _REAL["tape"]


def real_bars(brick=1):
    key = "bars%d" % brick
    if key not in _REAL:
        _REAL[key] = bars.build("renko", real_session(), brick_ticks=brick,
                                tick_size=TICK, instrument="GC")
    return _REAL[key]


# ---------------------------------------------------------------- the hand walk
#: Walked by hand against @RenkoBarsType.cs line by line, brick = 1 tick.
#: prices (ticks): 1000, 1001, 1003, 1002, 1000, 998   sizes: 1..6
HAND_PRICES = [1000, 1001, 1003, 1002, 1000, 998]
HAND_SIZES = [1, 2, 3, 4, 5, 6]
HAND_TS = [10, 20, 30, 40, 50, 60]
#: (open, high, low, close, volume, ts, tick_count)
HAND_EXPECT = [
    (1000, 1001, 1000, 1001, 1, 10, 1),   # first brick: forming bar REWRITTEN, stamp t0
    (1001, 1002, 1001, 1002, 2, 20, 1),
    (1002, 1003, 1002, 1003, 0, 30, 0),   # gap fill: NO ticks, NO volume
    (1002, 1002, 1001, 1001, 7, 40, 2),   # reversal brick: opens 1002, not 1003
    (1001, 1001, 1000, 1000, 0, 50, 0),   # gap fill, down
    (1000, 1000,  999,  999, 5, 50, 1),
    ( 999,  999,  998,  998, 0, 60, 0),
    ( 998,  998,  998,  998, 6, 60, 1),   # still forming when the tape ended
]


def test_hand_walk():
    s = bars.build("renko", tape_from_ticks(HAND_PRICES, HAND_SIZES, HAND_TS),
                   brick_ticks=1, tick_size=TICK)
    assert s.n == len(HAND_EXPECT), "%d bars, expected %d" % (s.n, len(HAND_EXPECT))
    for i, (o, h, l, c, v, ts, nt) in enumerate(HAND_EXPECT):
        got = (round(s.open[i] / TICK), round(s.high[i] / TICK), round(s.low[i] / TICK),
               round(s.close[i] / TICK), int(s.volume[i]), int(s.ts_ms[i]), int(s.tick_count[i]))
        assert got == (o, h, l, c, v, ts, nt), "bar %d: %s != %s" % (i, got, (o, h, l, c, v, ts, nt))
    assert bool(s.is_partial[-1]) and not s.is_partial[:-1].any()


def test_brick_carries_the_forming_bars_stamp_not_the_breaking_ticks():
    """Behaviour 2. Bar 1 closes at t=20 -- the stamp of the tick BEFORE the one that
    broke it (t=30). Getting this wrong is invisible in OHLC and fails the ts gate."""
    s = bars.build("renko", tape_from_ticks(HAND_PRICES, HAND_SIZES, HAND_TS),
                   brick_ticks=1, tick_size=TICK)
    assert int(s.ts_ms[1]) == 20
    assert int(s.volume[1]) == 2          # the breaking tick's size (3) went to the next bar


def test_gap_bricks_carry_no_tape_rows():
    """Behaviour 3, and the reason `end_idx` can repeat."""
    s = bars.build("renko", tape_from_ticks(HAND_PRICES, HAND_SIZES, HAND_TS),
                   brick_ticks=1, tick_size=TICK)
    empty = s.tick_count == 0
    assert empty.sum() == 3
    assert (s.volume[empty] == 0).all()
    assert (s.start_idx[empty] == -1).all()
    assert (np.diff(s.end_idx) >= 0).all()
    assert (np.diff(s.end_idx) == 0).any()          # it really does repeat


def test_session_reset_flattens_the_last_bar_to_a_doji():
    """Behaviour 4: the last bar of a session is never a brick."""
    prices = [1000, 1001, 1002, 2000, 2001, 2002]
    sid = [0, 0, 0, 1, 1, 1]
    ts = [10, 20, 30, 40, 50, 60]
    s = bars.build("renko", tape_from_ticks(prices, [1] * 6, ts, sid),
                   brick_ticks=1, tick_size=TICK)
    last_of_s0 = int(np.flatnonzero(s.session_id == 0)[-1])
    o, h, l, c = (s.open[last_of_s0], s.high[last_of_s0], s.low[last_of_s0], s.close[last_of_s0])
    assert o == h == l == c, "session-close bar %d is %s, not a doji" % (last_of_s0, (o, h, l, c))
    assert s.notes["session_resets"] == 1
    # and with the reset off, it is a normal bar
    s2 = bars.build("renko", tape_from_ticks(prices, [1] * 6, ts, sid),
                    brick_ticks=1, tick_size=TICK, reset_on_new_session=False)
    assert s2.notes["session_resets"] == 0


def test_reversal_costs_two_bricks():
    """Behaviour 5. Up to 1003, then down: the first down brick spans 1002->1001, so
    it does NOT open where the up brick closed. Textbook Renko would open at 1003."""
    s = bars.build("renko", tape_from_ticks(HAND_PRICES, HAND_SIZES, HAND_TS),
                   brick_ticks=1, tick_size=TICK)
    assert round(s.close[2] / TICK) == 1003
    assert round(s.open[3] / TICK) == 1002


def test_off_grid_price_is_an_error():
    t = tape_from_ticks([1000, 1001])
    t.last = t.last.copy()
    t.last[1] = 100.13
    try:
        bars.build("renko", t, brick_ticks=1, tick_size=TICK)
    except ValueError as exc:
        assert "tick grid" in str(exc)
    else:
        raise AssertionError("an off-grid price must raise, not be rounded away")


def test_brick_size_2_is_nts_default():
    s = bars.build("renko", tape_from_ticks([1000, 1002, 1004]), tick_size=TICK)
    assert s.bar_params == "renko brick=2 tick=0.1"
    closed = ~s.is_partial
    heights = np.abs(s.close[closed] - s.open[closed]) / TICK
    assert np.allclose(heights, 2.0)


# -------------------------------------------------- the ULP defect, pinned shut
# ⭐ THE FIRST REAL GATE RUN FAILED HERE, and nothing below existed to catch it.
# `bars.gate --bartype renko --session 2025-12-10` produced 94,108 bars on BOTH sides
# with every boundary agreeing, and still reported 37,765 differing records -- every one
# a single ULP, because `renko.py` ended with `ticks * tick_size`. These tests fail on
# that form and on the `ticks * (1.0 / tick_size)` variant the TBars port hit, so the
# defect cannot come back wearing either face.

#: (tick level, tick size, the price as a human writes it). Chosen from the actual
#: failures in the 2025-12-10 verdict.
ULP_CASES = [
    (42379, 0.1, "4237.9"),
    (42374, 0.1, "4237.4"),
    (42364, 0.1, "4236.4"),
    (42354, 0.1, "4235.4"),
    # and one at a second tick size, so the case is not an artefact of 0.1 alone
    (50002, 0.01, "500.02"),
]


def test_ticks_to_price_is_exact_where_multiplying_is_not():
    for level, tick, text in ULP_CASES:
        want = float(text)
        got = float(series_mod.ticks_to_price([level], tick)[0])
        assert got == want, "%d x %s: %r != %r" % (level, tick, got, want)
        # and the test is DISCRIMINATING: the broken form really does miss this value,
        # so a green run here means something. An assertion that passes on the bug too
        # is not a regression test.
        assert level * tick != want, \
            "%d * %s already equals %r -- pick a case where multiplying is wrong" % (level, tick, want)


def test_ticks_to_price_is_exact_across_a_whole_price_range():
    levels = np.arange(40000, 45000, dtype=np.int64)         # GC's December range
    exact = series_mod.ticks_to_price(levels, 0.1)
    n_bad = int(np.count_nonzero(levels.astype(np.float64) * 0.1 != exact))
    assert n_bad > 1000, \
        "only %d of %d levels differ from the multiply form -- this range no longer " \
        "exercises the defect" % (n_bad, levels.size)
    # every value is the nearest double to a decimal with one place, which is what
    # NinjaTrader's `Math.Round(v, 6).ToString(...)` writes and the loader parses back
    for v in exact:
        assert float(v) == float("%.1f" % float(v))


def test_the_snap_back_to_ticks_divides_too():
    """The other direction, which the sibling TBars port flagged: snap prices to levels
    by DIVIDING by the tick size, never by multiplying by its reciprocal.

    Measured over the first 200,000 GC levels: `p / 0.1` and `p * (1/0.1)` disagree on
    66,892 of them (33%) -- but the disagreement is always below the rounding boundary,
    so `np.rint` recovers the same level from either. `renko()` divides anyway. This
    test records that the port is not merely lucky: it pins the snap's OUTPUT, so a tick
    size whose reciprocal is NOT exact cannot quietly start snapping to the wrong level.
    """
    levels = np.arange(1, 200000, dtype=np.int64)
    px = series_mod.ticks_to_price(levels, 0.1)
    assert (np.rint(px / 0.1).astype(np.int64) == levels).all()
    n_split = int(np.count_nonzero((px / 0.1) != (px * (1.0 / 0.1))))
    assert n_split > 10000, "the two snap forms no longer diverge; the case is untested"


def test_ticks_to_price_refuses_to_round_before_it_divides():
    """Past 2**53 the int->float step is itself lossy. That is the same class of error,
    so it RAISES rather than returning a quietly-wrong price."""
    try:
        series_mod.ticks_to_price([1 << 54], 0.1)
    except ValueError as exc:
        assert "2**53" in str(exc)
    else:
        raise AssertionError("an inexact tick level must raise, not be scaled anyway")


def test_renko_prices_land_exactly_on_the_grid_the_dump_prints():
    """End to end: a brick walk across a run of ULP-hostile levels comes out bit-exact.

    Every OHLC value here is a level NinjaTrader's dumper writes as plain decimal text
    (`Math.Round(v, 6).ToString(...)`), so the gate reads the nearest double to that
    decimal. Anything else is a FAIL on an EXACT tolerance."""
    levels = list(range(42350, 42385))
    s = bars.build("renko", tape_from_ticks(levels), brick_ticks=1, tick_size=TICK)
    for name in ("open", "high", "low", "close"):
        a = getattr(s, name)
        lv = np.rint(a / TICK).astype(np.int64)
        assert (a == series_mod.ticks_to_price(lv, TICK)).all(), \
            "%s is not on the exact tick grid" % name
        n_bad = int(np.count_nonzero(lv.astype(np.float64) * TICK != a))
        assert n_bad > 0, \
            "%s never lands on a level where `* tick_size` is wrong -- this walk would " \
            "have passed on the bug and proves nothing" % name


def test_real_tape_prices_are_bit_exact_not_merely_close():
    s = real_bars(1)
    for name in ("open", "high", "low", "close"):
        a = getattr(s, name)
        lv = np.rint(a / TICK).astype(np.int64)
        assert (a == series_mod.ticks_to_price(lv, TICK)).all()
        n_bad = int(np.count_nonzero(lv.astype(np.float64) * TICK != a))
        assert n_bad > 0, "%s: the real tape no longer exercises the ULP case" % name


# ---------------------------------------------------------------- plumbing
def test_registry_and_discovery():
    assert "renko" in bars.kinds()
    bars.raise_discovery_errors()               # a broken sibling module fails HERE
    bt = bars.get("renko")
    assert bt.nt_period_type == 11
    assert bt.params_str(brick_ticks=1, tick_size=0.1) == "renko brick=1 tick=0.1"
    assert bt.bartag(brick_ticks=1) == "11v1x1"       # the corpus's largest bartag


def test_unknown_bar_type_names_what_is_registered():
    try:
        bars.get("nope")
    except bars.BarTypeError as exc:
        assert "renko" in str(exc)
    else:
        raise AssertionError("an unknown bar type must raise")


def test_gate_rows_drop_the_forming_bar():
    s = bars.build("renko", tape_from_ticks(HAND_PRICES, HAND_SIZES, HAND_TS),
                   brick_ticks=1, tick_size=TICK)
    rows = bars.gate_rows(s, session_date="1970-01-01")
    assert len(rows) == s.n - 1
    assert [r["bar_index"] for r in rows] == list(range(s.n - 1))
    assert rows[0]["bar_params"] == "renko brick=1 tick=0.1"
    assert set(series_mod.GATE_FIELDS) <= set(rows[0])


def test_to_engine_bars_passes_row_less_bars_through_without_merging():
    """Row-less bars survive the handoff intact -- neither refused nor collapsed.

    This test previously asserted the opposite. The engine's `end_idx` is now
    NON-DECREASING (engine README 2.2), so a repeat legally means "these bars
    closed on the same tape row" and the engine gives that interval no fill
    opportunity. Merging remains forbidden for a different reason than before:
    collapsing a bar renumbers every bar after it, which would silently misalign
    the gate against NinjaTrader's own bar indices.
    """
    s = bars.build("renko", tape_from_ticks(HAND_PRICES, HAND_SIZES, HAND_TS),
                   brick_ticks=1, tick_size=TICK)
    assert s.n_empty > 0, "this fixture is meant to contain row-less bricks"

    b = bars.to_engine_bars(s)

    assert b.n == s.n, "bar count must survive -- merging renumbers the gate"
    assert np.array_equal(b.end_idx, s.end_idx)
    assert np.array_equal(b.close, s.close)          # the port's levels, not tape prices
    assert np.array_equal(b.volume, s.volume)
    # the row-less ones really are row-less, and really did repeat an index
    assert int(np.sum(np.diff(b.end_idx) == 0)) >= s.n_empty - 1


def test_to_engine_bars_works_when_every_bar_has_rows():
    s = bars.build("renko", tape_from_ticks([1000, 1001, 1002, 1001, 1000, 1001]),
                   brick_ticks=1, tick_size=TICK)
    assert s.n_empty == 0
    b = bars.to_engine_bars(s)
    assert b.n == s.n
    assert np.array_equal(b.close, s.close)          # the port's prices, not the tape's
    assert np.array_equal(b.end_idx, s.end_idx)


def test_iso_to_ms():
    # SentinelBarDump writes 7 fractional digits; both columns FLOOR to ms.
    assert ntdump.iso_to_ms("1970-01-01T00:00:00.0000000Z") == 0
    assert ntdump.iso_to_ms("2026-07-20T22:05:30.7480000Z") == 1784585130748
    assert ntdump.iso_to_ms("2025-12-09T00:20:00.3879999Z") == 1765239600387
    try:
        ntdump.iso_to_ms("2026-07-20T22:05:30.748")
    except ntdump.DumpError:
        pass
    else:
        raise AssertionError("a non-UTC stamp must raise, not be assumed UTC")


# ---------------------------------------------------------------- real tape
def test_real_tape_completed_bricks_are_exact():
    s = real_bars(1)
    closed = ~s.is_partial
    o, h, l, c = s.open[closed], s.high[closed], s.low[closed], s.close[closed]
    brick = TICK
    # every completed bar is either a brick of exactly one brick height, or the doji a
    # session reset leaves behind (no reset inside a single session, so: all bricks)
    height = np.abs(c - o)
    assert np.allclose(height, brick, atol=1e-12), \
        "%d completed bars are not exactly one brick high" % int((~np.isclose(height, brick)).sum())
    assert np.allclose(h, np.maximum(o, c), atol=1e-12), "a completed brick has no upper wick"
    assert np.allclose(l, np.minimum(o, c), atol=1e-12), "a completed brick has no lower wick"


def test_real_tape_conserves_volume_and_ticks():
    s = real_bars(1)
    t = real_session()
    rows = tapeio.trade_rows(t)
    assert int(s.volume.sum()) == int(t.size[rows].sum())
    assert int(s.tick_count.sum()) == int(rows.size)


def test_real_tape_rows_are_partitioned_in_order():
    s = real_bars(1)
    assert (np.diff(s.end_idx) >= 0).all(), "end_idx must never go backwards"
    has = s.tick_count > 0
    # every bar that owns rows owns a contiguous block, and the blocks are in order
    assert (s.end_idx[has] >= s.start_idx[has]).all()
    assert (np.diff(s.start_idx[has]) > 0).all()


def test_real_tape_stamps_are_monotonic_and_inside_the_session():
    s = real_bars(1)
    t = real_session()
    assert (np.diff(s.ts_ms) >= 0).all(), "NT never lets a bar stamp go backwards"
    assert int(s.ts_ms[0]) >= int(t.ts_ms[0])
    assert int(s.ts_ms[-1]) <= int(t.ts_ms[-1])


def test_real_tape_bigger_bricks_make_fewer_bars_and_fewer_gaps():
    a, b = real_bars(1), real_bars(10)
    assert b.n < a.n
    assert b.n_empty / b.n < a.n_empty / a.n


def test_real_tape_bar_index_restarts_per_session():
    s = real_bars(1)
    assert int(s.bar_index[0]) == 0
    assert int(s.bar_index[-1]) == s.n - 1          # one session in this tape


# ------------------------------------------------- the gate's PLUMBING, proven
# ⚠⚠ READ THIS BEFORE QUOTING THE RESULT. The two tests below are a SELF-DIFF: the
# "NT" side is written FROM the Python port. They prove the driver runs end to end and
# that it CAN FAIL -- §2's "a gate that has never failed is not a gate" applied to the
# wiring. They are NOT parity evidence and must never be reported as a passing gate.
# The real reference side does not exist yet; `bars/README.md` says exactly why.
import json          # noqa: E402
import tempfile      # noqa: E402

from bars import gate as gate_mod    # noqa: E402


def _iso(ms: int) -> str:
    import datetime as _dt
    d = _dt.datetime.fromtimestamp(ms // 1000, _dt.timezone.utc)
    return d.strftime("%Y-%m-%dT%H:%M:%S") + ".%03d0000Z" % (ms % 1000)


def _write_fake_dump(path: str, rows: list, mutate=None) -> None:
    """A `bars.1`-shaped file with the port's own numbers in it."""
    hdr = {"hdr": 1, "schema": "bars.1", "dumpVer": "0.0.0-selfdiff", "coreVer": "n/a",
           "inst": "GC", "bartype": "11v1x1", "barLabel": "Renko 1/1", "tickSize": 0.1,
           "pointValue": 100, "periodType": 11, "periodValue": 1, "periodValue2": 1,
           "baseValue": 0, "tradingHours": "selfdiff", "openedUtc": "1970-01-01T00:00:00Z"}
    with open(path, "w", encoding="utf-8") as fh:
        fh.write(json.dumps(hdr) + "\n")
        for k, r in enumerate(rows):
            o = {"i": k, "t": _iso(r["ts_ms"]), "o": r["open"], "h": r["high"],
                 "l": r["low"], "c": r["close"], "v": r["volume"],
                 "rt": False, "newSession": k == 0}
            if mutate is not None:
                mutate(k, o)
            fh.write(json.dumps(o) + "\n")


def _run_gate_against(dump_path: str) -> int:
    return gate_mod.main(["--bartype", "renko", "--instrument", INSTRUMENT,
                          "--session", SESSION, "--param", "brick_ticks=1",
                          "--param", "tick_size=0.1", "--nt-dump", dump_path,
                          "--one-session"])


def _one_session_rows():
    s = bars.build("renko", real_session(), brick_ticks=1, tick_size=TICK, instrument="GC")
    return bars.gate_rows(s, session_date=SESSION)


def test_gate_driver_runs_end_to_end():
    rows = _one_session_rows()
    with tempfile.TemporaryDirectory() as d:
        p = os.path.join(d, "19700101T000000__GC__11v1x1.jsonl")
        _write_fake_dump(p, rows)
        assert _run_gate_against(p) == 0, "the driver could not compare two identical sides"


def test_gate_driver_can_fail():
    """One bar's close moved by ONE TICK must come back FAIL (1), not PASS."""
    rows = _one_session_rows()

    def bend(k, o):
        if k == 500:
            o["c"] = round(o["c"] + 0.1, 6)

    with tempfile.TemporaryDirectory() as d:
        p = os.path.join(d, "19700101T000000__GC__11v1x1.jsonl")
        _write_fake_dump(p, rows, mutate=bend)
        assert _run_gate_against(p) == 1, "a one-tick difference must FAIL the gate"


# ------------------------------------------------- the session anchor, bounded
# The second half of the first real gate run: 2025-12-09, 12-11 and 12-22 ABORTed with
# "the dump contains no first-of-session bar inside the window" while 12-10 and 12-15
# ran. Measured cause -- NinjaTrader's anchor brick closes 121 ms BEFORE to 472 ms after
# the tape's first row, and `gate_rows` was selecting on `first_ts_ms <= ms <= last_ts_ms`,
# so whether a session gated at all came down to the sign of that skew. Selection moved
# to the ET session window both sides define; the skew is now an explicit BOUND.
_HDR = {"hdr": 1, "schema": "bars.1", "dumpVer": "0", "coreVer": "0", "inst": "GC",
        "bartype": "11v1x1", "barLabel": "Renko 1/1", "tickSize": 0.1, "periodType": 11,
        "periodValue": 1, "periodValue2": 1}
_WIN0 = 1765234800000        # 2025-12-08T23:00:00Z -- the 2025-12-09 session opens
_WIN1 = _WIN0 + 82_800_000   # 2025-12-09T22:00:00Z -- and closes
_FIRST_ROW = _WIN0 + 387     # the tape's first row, as `2025-12-09.meta.json` records it


def _dump_rows(anchor_ms: int, n: int = 5, step: int = 200):
    """A dump-shaped session: an anchor brick then `n-1` more, `step` ms apart."""
    return [{"i": k, "t": _iso(anchor_ms + k * step), "o": 4219.6 + k * 0.1,
             "h": 4219.7 + k * 0.1, "l": 4219.6 + k * 0.1, "c": 4219.7 + k * 0.1,
             "v": 1, "rt": False, "newSession": k == 0} for k in range(n)]


def _pick(anchor_ms: int, **kw):
    return ntdump.gate_rows(_HDR, _dump_rows(anchor_ms), session_date=SESSION,
                            win_start_ms=_WIN0, win_end_ms=_WIN1, first_ts_ms=_FIRST_ROW,
                            bar_params="renko brick=1 tick=0.1", bartype="renko", **kw)


def test_anchor_may_close_before_the_tapes_first_row():
    """2025-12-09, exactly: NT's anchor closes 39 ms ahead of our first tick. It is the
    same bar -- same open price, same volume -- and it must gate, not ABORT."""
    out, counters = _pick(_FIRST_ROW - 39)
    assert len(out) == 5
    assert out[0]["bar_index"] == 0
    assert counters["anchor_lead_ms"] == 39, "the skew must be REPORTED, not just tolerated"
    assert counters["dropped_before_session_start"] == 0


def test_anchor_may_close_well_after_the_tapes_first_row():
    """2026-01-02: +472 ms, because the anchor brick stays open until it completes. A
    late anchor is normal and is deliberately NOT bounded."""
    out, counters = _pick(_FIRST_ROW + 472)
    assert len(out) == 5 and counters["anchor_lead_ms"] == -472


def test_anchor_too_far_ahead_of_the_tape_is_an_abort():
    """The bound is a bound.

    Note where this bites: on GC the tape's first row is ~400 ms after the open, so the
    window START already bounds the anchor more tightly than 2000 ms ever could. The
    bound is for the case the window cannot cover -- a session whose first row we hold
    is minutes into the session, where an anchor "just before it" could be an entirely
    different brick. First row a minute after the open, anchor 2001 ms ahead of it: both
    are comfortably inside the window, and it must still ABORT."""
    late_first = _WIN0 + 60_000
    try:
        ntdump.gate_rows(_HDR, _dump_rows(late_first - 2001), session_date=SESSION,
                         win_start_ms=_WIN0, win_end_ms=_WIN1, first_ts_ms=late_first,
                         bar_params="renko brick=1 tick=0.1", bartype="renko")
    except ntdump.DumpError as exc:
        assert "2001 ms BEFORE" in str(exc) and "2000" in str(exc)
    else:
        raise AssertionError("an anchor beyond ANCHOR_LEAD_MS must ABORT, not be paired")


def test_a_session_with_no_anchor_still_aborts():
    """Widening the selection must NOT have cost the mid-session detection: a chart whose
    history begins after the open has no bar 0, and that is still ABORT."""
    rows = _dump_rows(_FIRST_ROW, n=4)
    for r in rows:
        r["newSession"] = False
    try:
        ntdump.gate_rows(_HDR, rows, session_date=SESSION, win_start_ms=_WIN0,
                         win_end_ms=_WIN1, first_ts_ms=_FIRST_ROW,
                         bar_params="renko brick=1 tick=0.1", bartype="renko")
    except ntdump.DumpError as exc:
        assert "no first-of-session bar" in str(exc)
    else:
        raise AssertionError("a mid-session chart must ABORT, not be renumbered")


def test_the_session_window_is_half_open():
    """`build_tape.py` keeps rows on `[start, end)`. A bar closing exactly at the close
    stamp belongs to no session on either side, and must not be pulled into this one."""
    rows = _dump_rows(_WIN1 - 200, n=3, step=200)      # bars at end-200, end, end+200
    out, _ = ntdump.gate_rows(_HDR, rows, session_date=SESSION, win_start_ms=_WIN0,
                              win_end_ms=_WIN1, first_ts_ms=_WIN1 - 200,
                              bar_params="renko brick=1 tick=0.1", bartype="renko")
    assert len(out) == 1, "the bar AT win_end_ms is outside a half-open window"


def test_gate_aborts_without_a_reference_side():
    """The honest outcome when NinjaTrader has produced nothing: ABORT (2), never PASS."""
    with tempfile.TemporaryDirectory() as d:
        rc = gate_mod.main(["--bartype", "renko", "--instrument", INSTRUMENT,
                            "--session", SESSION, "--param", "brick_ticks=1",
                            "--param", "tick_size=0.1", "--one-session",
                            "--nt-dump", os.path.join(d, "does-not-exist.jsonl")])
    assert rc == 2


# ---------------------------------------------------------------- runner
def _run():
    fns = [(k, v) for k, v in sorted(globals().items())
           if k.startswith("test_") and callable(v)]
    bad = 0
    for name, fn in fns:
        try:
            fn()
            print("PASS  %s" % name)
        except Exception as exc:                    # noqa: BLE001 -- reported, not swallowed
            bad += 1
            print("FAIL  %s: %s: %s" % (name, type(exc).__name__, exc))
    print("\n%d passed, %d failed" % (len(fns) - bad, bad))
    return 1 if bad else 0


if __name__ == "__main__":
    raise SystemExit(_run())
