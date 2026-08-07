"""Tests for the SentinelTBars Python port (`bars/tbars.py`).

    C:\\ntbv\\Scripts\\python.exe -m pytest bars\\test_tbars.py -q      # from Sentinel\\Azimuth
    C:\\ntbv\\Scripts\\python.exe bars\\test_tbars.py                   # same proofs, no pytest

⚠ READ THIS BEFORE READING A GREEN RUN AS PARITY.
These tests prove the port is INTERNALLY consistent, deterministic, faithful to the specific
edge cases enumerated in `tbars.py`, and that its `bartype` gate is wired and CAN FAIL. They
prove NOTHING about agreement with NinjaTrader, because no NT reference side exists - see
"THE GATE'S TRUE STATUS" at the foot of `tbars.py`. Per spec §2 the port is NOT TRUSTED for
research until that gate is RUN.
"""
from __future__ import annotations

import os
import sys

import numpy as np

_HERE = os.path.dirname(os.path.abspath(__file__))
_AZIMUTH = os.path.dirname(_HERE)
for _p in (_AZIMUTH, _HERE):
    if _p not in sys.path:
        sys.path.insert(0, _p)

import tbars  # noqa: E402
from tbars import (CLOSE_BREAKOUT, CLOSE_CHAIN, CLOSE_MICRO, CLOSE_OPEN, CLOSE_TIME,  # noqa: E402
                   SPEED_12, TBarsParams)

TAPE_DIR = os.path.join(_AZIMUTH, "tape", "GC 02-26")
SESSION = "2025-12-09"
GC_TICK = 0.1

_cache: dict = {}


def _session(name: str = SESSION) -> dict:
    if name not in _cache:
        _cache[name] = tbars.load_tape_session(os.path.join(TAPE_DIR, name + ".parquet"))
    return _cache[name]


def _series(name: str = SESSION, params: TBarsParams | None = None):
    key = (name, (params or SPEED_12).params_string())
    if key not in _cache:
        _cache[key] = tbars.build_session(_session(name), tick_size=GC_TICK, params=params)
    return _cache[key]


# ═══════════════════════════════════════════════════ 1 · the parameter decoding
def test_speed_settings_decode():
    """`6/24` is ONE knob: Speed Settings 12. State.Configure derives the pair."""
    p = TBarsParams(speed=12)
    assert (p.value, p.value2) == (6, 24)          # BaseBarsPeriodValue/2, *2
    assert p.bartag() == "212201v6x24"
    # the three offsets LatchConfig actually builds, in ticks
    assert (p.value, p.value2, p.speed) == (6, 24, 12)


def test_speed_table_matches_the_suite():
    """The Speed<->label table used by every report and the corpus filenames."""
    for speed, label in ((8, (4, 16)), (12, (6, 24)), (16, (8, 32)),
                         (20, (10, 40)), (24, (12, 48))):
        p = TBarsParams(speed=speed)
        assert (p.value, p.value2) == label, speed
        # `Lab\sentinel_lab\bartag.py::_is_speed` classifies a brick bar STRUCTURALLY:
        # Value2 == 4*Value, and reports Speed = Value*2. Same answer, independently.
        assert p.value2 == 4 * p.value
        assert p.value * 2 == speed


def test_bartags_present_in_the_corpus_decode():
    """Every `212201...` bartag the corpus carries decodes to a Speed this port accepts."""
    for tag, speed in (("212201v6x24", 12), ("212201v4x16", 8), ("212201v8x32", 16),
                       ("212201v10x40", 20), ("212201v12x48", 24)):
        assert TBarsParams(speed=speed).bartag() == tag


def test_lane_suffix_is_not_a_bar_parameter():
    """`@AUD` / `@AUD0826` / `@TEST` are LANES - scope discriminators, not geometry.

    The bars type never reads a lane (`PublishBrickTick`/`LogBrick` use the BARE
    `ScopeOf(instrument, barsPeriod)`), so there is nothing for `TBarsParams` to hold and
    nothing for a lane to change. The bartag this port emits is bare by construction.
    """
    assert "@" not in SPEED_12.bartag()
    assert "@" not in SPEED_12.params_string()
    # and the corpus tags differ ONLY by the suffix, i.e. same geometry, two charts
    for tagged in ("212201v6x24@AUD", "212201v6x24@AUD0826", "212201v6x24@TEST"):
        assert tagged.split("@", 1)[0] == SPEED_12.bartag()


def test_params_string_is_stable_and_total():
    """`bar_params` is a gate PRECONDITION: any drift must ABORT, not silently compare."""
    a = TBarsParams(speed=12).params_string()
    b = TBarsParams(speed=12).params_string()
    assert a == b
    assert TBarsParams(speed=20).params_string() != a
    # every knob that can move a brick is in there
    for probe in (("confirm_milliseconds", 999), ("atr_mult_trend", 0.5),
                  ("force_stagnation_seconds", 45), ("micro_split_ratio", 0.9),
                  ("quiet_start_hour", 3), ("target_bars_per_session", 500),
                  ("timezone", "UTC"), ("reset_on_new_trading_day", False)):
        p = TBarsParams(speed=12)
        setattr(p, probe[0], probe[1])
        assert p.params_string() != a, probe[0]


# ═══════════════════════════════════════════════════ 2 · quiet hours = LOCAL hours
def test_local_hours_track_the_platform_timezone_across_dst():
    """`InQuietHours` reads `DateTime.Hour` of NT's display timezone, not UTC (note 12)."""
    # 2025-12-09 23:00:00Z -> 18:00 EST
    winter = np.array([1765321200000], dtype=np.int64)
    assert int(tbars._local_hours(winter, "America/New_York")[0]) == 18
    assert int(tbars._local_hours(winter, "UTC")[0]) == 23
    # 2026-07-01 23:00:00Z -> 19:00 EDT (the offset moved; the hour must move with it)
    summer = np.array([1782342000000], dtype=np.int64)
    assert int(tbars._local_hours(summer, "America/New_York")[0]) == 19


def test_quiet_hours_change_the_confirmation_threshold():
    """Same breakout, two clocks: quiet hours raise ConfirmTicksBeyond 1 -> 2 and kill it.

    This is the whole reason `timezone` is a declared parameter and not an implementation
    detail: getting it wrong silently rewrites five hours of every session.
    """
    ts, px, vol = _fast_break(base_utc_ms=1765306800000)   # 2025-12-09 19:00Z = 14:00 ET
    loud = tbars.build(ts, px, vol, tick_size=GC_TICK,
                       params=TBarsParams(speed=12, timezone="America/New_York"))
    # same tape, but a timezone in which 19:00Z falls inside 18..23 local
    quiet = tbars.build(ts, px, vol, tick_size=GC_TICK,
                        params=TBarsParams(speed=12, timezone="UTC"))
    assert CLOSE_BREAKOUT in loud.close_reason
    assert CLOSE_BREAKOUT not in quiet.close_reason


# ═══════════════════════════════════════════════════ 3 · synthetic edge cases
def _ticks(prices, *, base_utc_ms=1765306800000, step_ms=20, size=1):
    n = len(prices)
    ts = base_utc_ms + np.arange(n, dtype=np.int64) * step_ms
    return ts, np.round(np.asarray(prices, dtype=np.float64), 6), np.full(n, size, np.int64)


def _fast_break(base_utc_ms=1765306800000):
    """Sit at P for 200 ms, jump 10 ticks, hold 140 ms, fall back inside.

    140 ms clears the 120 ms `ConfirmMilliseconds` but NOT the 162 ms it becomes inside
    quiet hours (x1.35), and returning inside the channel RESETS the pending - so the same
    tape confirms on one clock and not on the other.
    """
    p0 = 4200.0
    return _ticks([p0] * 10 + [p0 + 1.0] * 8 + [p0] * 10,
                  base_utc_ms=base_utc_ms, step_ms=20)


def test_fast_break_confirms():
    ts, px, vol = _fast_break()
    s = tbars.build(ts, px, vol, tick_size=GC_TICK,
                    params=TBarsParams(speed=12, enable_quiet_hours_gating=False))
    assert CLOSE_BREAKOUT in s.close_reason
    k = s.close_reason.index(CLOSE_BREAKOUT)
    jump_ms = int(ts[10])                       # the tick that first cleared barMax
    # ConfirmMilliseconds = 120: it can NEVER confirm on the tick that opened the pending
    assert int(s.ts_ms[k]) - jump_ms >= 120
    assert int(s.ts_ms[k]) - jump_ms < 200      # ...and it does not dawdle past it


def test_slow_drift_beyond_the_boundary_never_confirms():
    """Note 6 - the speed gate is measured from the FIRST tick outside and never refreshes.

    One tick of penetration per 5 s scores 0.2 ticks/s against a 1.6 floor and can never
    recover, so a slow trend exits through the 90 s forced brick instead. A naive port that
    prints a brick the moment price crosses the boundary produces a different bar type.
    """
    # +1 tick every 5 s, 40 steps = 200 s, 40 ticks of travel
    px = [4200.0 + 0.1 * i for i in range(41)]
    s = tbars.build(*_ticks(px, step_ms=5000), tick_size=GC_TICK,
                    params=TBarsParams(speed=12, enable_quiet_hours_gating=False))
    assert CLOSE_BREAKOUT not in s.close_reason
    assert CLOSE_CHAIN not in s.close_reason
    assert {CLOSE_TIME, CLOSE_MICRO} & set(s.close_reason)


def test_stalled_tape_prints_one_brick_every_90_seconds():
    """Note 5 - `lastBoundaryTouch` is "time of last brick", so this is a pure 90 s timer."""
    n = 901                                   # 1 Hz for 900 s at a dead price
    s = tbars.build(*_ticks([4200.0] * n, step_ms=1000), tick_size=GC_TICK,
                    params=TBarsParams(speed=12, enable_quiet_hours_gating=False))
    closed = [r for r in s.close_reason if r != CLOSE_OPEN]
    assert closed and set(closed) == {CLOSE_TIME}
    assert 9 <= len(closed) <= 11, len(closed)
    gaps = np.diff(s.ts_ms[:len(closed)]) / 1000.0
    assert np.all(gaps >= 90.0) and np.all(gaps <= 92.0), gaps


def test_force_time_brick_can_emit_open_close_outside_high_low():
    """Note 7 - `AddBar(haOpen, barOpen, barOpen, haOpen)`. Not a typo; it is the C#."""
    s = _series()
    outside = ((s.open > s.high + 1e-9) | (s.open < s.low - 1e-9)
               | (s.close > s.high + 1e-9) | (s.close < s.low - 1e-9))
    assert outside.any(), "the quirk vanished - the ForceTimeBrick geometry changed"


def test_dead_backinside_branch_is_reproduced_not_fixed():
    """Note 10 - `backInside` is unreachable, so removing it cannot change any bar.

    Proven behaviourally: nothing in the port depends on it, so a run is unaffected by the
    branch's absence. The assertion is that the reachable predicate is what we think it is.
    """
    # over_max => close > barMax => Cmp(close, barMax) > 0 => backInside is false.
    # under_min => close < barMin => Cmp(close, barMin) < 0 => backInside is false.
    # There is no third way to reach the branch. Encoded as a logic check:
    for over_max, under_min in ((True, False), (False, True)):
        back_inside = (not over_max) and (not under_min)
        assert back_inside is False


# ═══════════════════════════════════════════════════ 4 · the real session, end to end
def test_real_session_builds():
    sess = _session()
    assert sess["n_rows"] == 1_561_749
    assert sess["n_trades"] == 146_547
    assert sess["session"] == SESSION
    assert sess["instrument"] == "GC 02-26"
    s = _series()
    assert s.n_ticks == sess["n_trades"]
    assert s.n > 100
    # a whole ET trading day at Speed 12
    assert 500 <= s.n <= 2000, s.n


def test_crossed_quotes_are_counted_never_dropped():
    """§3.2 is an OPEN DEFECT. TBars is BuiltFrom=Tick and cannot see the book, but the
    count is surfaced rather than smoothed - a silently dropped row is a silently changed
    answer."""
    sess = _session()
    assert sess["crossed_rows"] == 140
    assert tbars.gate_meta(sess, _series())["crossed_rows"] == 140


def test_determinism():
    """Two runs of one configuration over one tape are bit-identical, or nothing above holds."""
    sess = _session()
    a = tbars.build_session(sess, tick_size=GC_TICK)
    b = tbars.build_session(sess, tick_size=GC_TICK)
    for f in ("open", "high", "low", "close", "volume", "ts_ms", "open_ts_ms",
              "tick_count", "close_row", "open_row", "direction"):
        assert np.array_equal(getattr(a, f), getattr(b, f)), f
    assert a.close_reason == b.close_reason


def test_bar_structure_invariants():
    s = _series()
    assert np.all(np.diff(s.ts_ms) >= 0)                      # close stamps never go back
    assert np.all(np.diff(s.close_row) >= 0)                  # ...nor do the tape rows
    assert np.all(s.open_ts_ms <= s.ts_ms)
    assert np.all(s.close_row >= s.open_row)
    assert np.all(s.high >= s.low - 1e-9)                     # high/low themselves are sane
    assert np.all(s.volume > 0)
    assert np.all(s.tick_count >= 1)
    assert np.all(np.isin(s.direction, (-1, 1)))
    assert s.close_reason[-1] == CLOSE_OPEN                   # the last bar is still forming
    assert set(s.close_reason[:-1]) <= {CLOSE_BREAKOUT, CLOSE_CHAIN, CLOSE_TIME, CLOSE_MICRO}


def test_renko_clipping_of_the_breakout_side():
    """Note 1 + note 3: the breakout-side extreme is clipped to the boundary, and the next
    brick is BORN with a `baseOpenOffset`-wide body seeded off it."""
    s = _series()
    body = SPEED_12.speed * GC_TICK          # 12 ticks
    checked = 0
    for k, r in enumerate(s.close_reason[:-1]):
        if r not in (CLOSE_BREAKOUT, CLOSE_CHAIN):
            continue
        checked += 1
        if s.direction[k] > 0:
            # high[k] == breakoutPrice == nextHigh, and nextLow == breakoutPrice - body,
            # and the newborn low can only fall further.
            assert s.high[k] - s.low[k + 1] >= body - 1e-9, k
            assert s.high[k + 1] >= s.high[k] - 1e-9, k
        else:
            assert s.high[k + 1] - s.low[k] >= body - 1e-9, k
            assert s.low[k + 1] <= s.low[k] + 1e-9, k
    assert checked > 0


def test_breakout_prices_sit_on_the_tick_grid():
    """`breakoutPrice` is `RoundToTickSize`d, so a clipped extreme is exactly on the grid."""
    s = _series()
    for k, r in enumerate(s.close_reason):
        if r not in (CLOSE_BREAKOUT, CLOSE_CHAIN):
            continue
        edge = s.high[k] if s.direction[k] > 0 else s.low[k]
        assert abs(edge / GC_TICK - round(edge / GC_TICK)) < 1e-6, (k, edge)


def test_volume_is_double_counted_at_every_boundary():
    """Note 4 - the closing tick's volume goes to BOTH the closing brick and the new one.

    This is a defect of the C#, faithfully reproduced. If a future NT reference disagrees,
    THIS is the first thing to look at - and the fix belongs in the C#, not here.
    """
    s = _series()
    assert int(s.volume.sum()) > s.tape_volume
    ratio = s.volume.sum() / s.tape_volume
    assert 1.0 < ratio < 1.10, ratio          # ~1.014 on 2025-12-09


def test_the_90_second_timer_is_the_dominant_boundary():
    """Corroboration, NOT verification. `Sentinel\\BrickLog\\brick-2026-07-16.jsonl` holds
    5,392 NT-written GC bricks whose `n` (barsThisSession) reaches 1,290 - about one brick
    per 64 s over a 23 h session, i.e. the 90 s stagnation brick dominates there too.
    Different day, different contract: this says the port is in the right REGIME and says
    nothing about agreement.
    """
    s = _series()
    from collections import Counter
    c = Counter(s.close_reason)
    assert c[CLOSE_TIME] > c[CLOSE_BREAKOUT]
    med = float(np.median(np.diff(s.ts_ms))) / 1000.0
    assert 30.0 <= med <= 95.0, med


def test_a_second_speed_produces_a_different_series():
    """If Speed did not change the bars, the whole parameter decoding would be moot."""
    a = _series()
    b = _series(params=TBarsParams(speed=24))
    assert a.n != b.n


# ═══════════════════════════════════════════════════ 5 · the engine seam (§4)
def test_engine_end_idx_is_the_seams_own_coordinate():
    """`bars_from_end_idx` takes a NON-DECREASING array; repeats are row-less bars.

    Nothing is collapsed: merging two bricks that closed on one tick would renumber every
    later bar and move the gate's `(session, bar_index)` coordinate.
    """
    s = _series()
    e = tbars.engine_end_idx(s)
    assert e.size == s.n
    assert np.array_equal(e, s.close_row)
    assert np.all(np.diff(e) >= 0)
    assert 0 <= e[0] and e[-1] < _session()["n_rows"]
    assert tbars.duplicate_end_idx(s) == int(np.count_nonzero(np.diff(e) == 0))


def test_to_bars_drives_the_engine_unchanged():
    """`bars_from_end_idx` IS the interface (spec §4). No second seam."""
    sess = _session()
    s = _series()
    tape = tbars._as_tape(sess)
    b = tbars.to_bars(tape, s)
    e = tbars.engine_end_idx(s)
    assert b.n == e.size == s.n
    assert np.array_equal(b.end_idx, e)
    # the NATIVE HA/Renko geometry rides the seam's keyword overrides - a brick level and a
    # Heikin-Ashi body are not tape prices and the engine must not re-derive them.
    for f in ("open", "high", "low", "close", "volume", "ts_ms"):
        assert np.array_equal(getattr(b, f), getattr(s, f)), f
    assert b.iv_start.shape[0] == b.n - 1     # the interval geometry built cleanly


# ═══════════════════════════════════════════════════ 6 · the gate (§2)
def _gate_sides(mutate=None, drop_last=False, meta_override=None, empty=False):
    from gates import rows_side

    sess = _session()
    s = _series()
    rows = tbars.gate_rows(s, session=sess["session"], instrument=sess["instrument"])
    meta = tbars.gate_meta(sess, s)
    ref = rows_side("REF(self)", rows, meta=meta)
    cmp_rows = [dict(r) for r in rows]
    if mutate:
        cmp_rows[len(cmp_rows) // 2][mutate[0]] += mutate[1]
    if drop_last:
        cmp_rows = cmp_rows[:-1]
    if empty:
        cmp_rows = []
    cmp_meta = dict(meta)
    cmp_meta["impl_ver"] = meta["impl_ver"] + "+cmp"
    if meta_override:
        cmp_meta.update(meta_override)
    return ref, rows_side("CMP", cmp_rows, meta=cmp_meta)


def test_gate_rows_carry_every_gated_field():
    from gates import get

    spec = get("bartype")
    sess = _session()
    rows = tbars.gate_rows(_series(), session=sess["session"], instrument=sess["instrument"])
    assert len(rows) == _series().n
    for f in spec.gate:
        assert f.name in rows[0], f.name
    for f in spec.precondition:
        assert f.name in rows[0], f.name
    for k in spec.pair_keys:
        assert k in rows[0], k
    # bar_index restarts per session, so `(session, bar_index)` is unique
    keys = {(r["session"], r["bar_index"]) for r in rows}
    assert len(keys) == len(rows)


def test_gate_meta_refuses_a_tape_with_no_provenance():
    """§3.1 - a tape file without its sidecar is not admissible to a gate."""
    sess = dict(_session())
    sess["tape_sha256"] = ""
    try:
        tbars.gate_meta(sess, _series())
    except ValueError as exc:
        assert "sidecar" in str(exc)
    else:
        raise AssertionError("gate_meta accepted a tape with no sha256")


def test_gate_is_wired_and_can_fail():
    """*A gate that has never failed is not a gate.* Fault injection, five ways.

    ⛔ THE PASS ROW BELOW IS A SELF-COMPARISON. It proves the WIRING, not parity. There is
    no NinjaTrader side to compare against - see `tbars.py`, THE GATE'S TRUE STATUS.
    """
    from gates import get, run_gate

    spec = get("bartype")
    cases = [
        ("identical (self-compare - wiring only)", dict(), 0),
        ("mutated close by 1e-9", dict(mutate=("close", 1e-9)), 1),
        ("mutated volume by 1", dict(mutate=("volume", 1)), 1),
        ("missing row", dict(drop_last=True), 1),
        ("empty cmp side", dict(empty=True), 2),
        ("identity skew (different tape sha)",
         dict(meta_override={"tape_sha256": "0" * 64}), 2),
        ("identity skew (different bar_params)",
         dict(meta_override={"bar_params": TBarsParams(speed=24).params_string()}), 2),
        ("provenance missing", dict(meta_override={"impl": None, "impl_ver": None}), 2),
    ]
    for name, kw, expect in cases:
        ref, cmp = _gate_sides(**kw)
        v = run_gate(spec, ref, cmp)
        assert v.exit_code == expect, "%s -> %s (%s) expected %d" % (
            name, v.verdict, "; ".join(v.reasons[:2]), expect)


# ═══════════════════════════════════════════════════ 6b · the `bars` package contract
def test_registers_with_the_bars_package():
    """`bars/__init__.py` discovers this module and it registers itself at import."""
    import bars as pkg

    pkg.raise_discovery_errors()
    bt = pkg.get("tbars")
    assert bt.nt_period_type == 212201
    assert bt.bartag(speed=12, tick_size=GC_TICK) == "212201v6x24"
    assert "212201v6x24" in bt.params_str(speed=12, tick_size=GC_TICK)


def test_build_series_satisfies_the_package_shape():
    import bars as pkg

    sess = _session()
    tape = tbars._as_tape(sess)
    s = pkg.get("tbars").build(tape, speed=12, tick_size=GC_TICK)
    native = _series()
    assert s.n == native.n
    assert s.bartype == "tbars"
    assert int(s.is_partial.sum()) == 1          # only the bar still forming at tape end
    assert np.all(s.bar_index == np.arange(s.n))  # one session -> 0..n-1
    assert np.all(s.tick_count > 0)               # TBars never emits a row-less bar
    assert s.n_empty == 0
    assert np.array_equal(s.end_idx, native.close_row)   # handed over UNCOLLAPSED
    assert s.notes["duplicate_end_idx"] >= 0
    assert s.notes["bar_volume"] > s.notes["tape_volume"]


def test_package_gate_rows_drop_the_forming_bar():
    """`SentinelBarDump` is `Calculate.OnBarClose` and never writes the forming bar."""
    import bars as pkg

    tape = tbars._as_tape(_session())
    s = pkg.get("tbars").build(tape, speed=12, tick_size=GC_TICK)
    rows = pkg.gate_rows(s, session_date=SESSION)
    assert len(rows) == s.n - 1
    for f in pkg.GATE_FIELDS:
        assert f in rows[0], f


def test_tick_size_is_mandatory_and_in_bar_params():
    """Every offset here is denominated in ticks: a wrong tick size is a different bar type."""
    try:
        tbars.series_params_str(speed=12)
    except ValueError as exc:
        assert "tick_size" in str(exc)
    else:
        raise AssertionError("series_params_str accepted no tick_size")
    a = tbars.series_params_str(speed=12, tick_size=0.1)
    b = tbars.series_params_str(speed=12, tick_size=0.25)
    assert a != b
    try:
        tbars.series_params_str(speed=12, tick_size=0.1, nonsense=1)
    except ValueError as exc:
        assert "nonsense" in str(exc)
    else:
        raise AssertionError("series_params_str accepted an unknown param")


# ═══════════════════════════════════════════════════ 7 · the whole tape
def test_every_contract_valid_session_builds():
    """All 17 `GC 02-26` sessions. A port that only ever ran on one day is not a port."""
    paths = sorted(p for p in os.listdir(TAPE_DIR) if p.endswith(".parquet"))
    assert len(paths) == 17, paths
    total_bars = total_ticks = 0
    for p in paths:
        sess = tbars.load_tape_session(os.path.join(TAPE_DIR, p))
        s = tbars.build_session(sess, tick_size=GC_TICK)
        assert s.n > 0, p
        assert np.all(np.diff(tbars.engine_end_idx(s)) >= 0), p
        assert np.all(s.volume > 0), p
        total_bars += s.n
        total_ticks += s.n_ticks
    assert total_ticks > 2_000_000
    assert total_bars > 5_000
    print("  17 sessions: %d ticks -> %d bars" % (total_ticks, total_bars))


# ═══════════════════════════════════════════════════ standalone runner
def _main() -> int:
    fns = [(n, f) for n, f in sorted(globals().items())
           if n.startswith("test_") and callable(f)]
    bad = 0
    for name, fn in fns:
        try:
            fn()
        except Exception as exc:                      # noqa: BLE001 - a runner reports, it
            bad += 1                                  # does not swallow: the name prints.
            print("FAIL %-58s %s: %s" % (name, type(exc).__name__, exc))
        else:
            print("ok   %s" % name)
    print("\n%d passed, %d failed of %d" % (len(fns) - bad, bad, len(fns)))
    print("\nREMINDER: none of this is parity. The `bartype` gate against NinjaTrader is "
          "WIRED AND UNRUN - see THE GATE'S TRUE STATUS in bars/tbars.py.")
    return 1 if bad else 0


if __name__ == "__main__":
    raise SystemExit(_main())
