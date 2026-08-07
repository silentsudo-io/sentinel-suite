"""Throughput -- the number that sets the Rust bar (§6, §10.3).

The full measurement lives in `engine.sweep.bench`; run it directly for the
headline figure. What is asserted here is a FLOOR, so a refactor that quietly
turns a vectorised scan into a Python loop fails CI instead of being discovered
by a slow sweep six weeks later.
"""
from __future__ import annotations

import time

import pytest

from engine import Backtester, EngineConfig, spec_for
from engine.demo_strategies import MaCrossBracket
from engine.strategy import MarketContext
from engine.sweep import TapeSource, param_grid, run_sweep, run_sweep_parallel

GC = spec_for("GC")

#: Deliberately far below the measured figure (see README). This catches an
#: order-of-magnitude regression, not noise on a busy box.
FLOOR_COMBOS_PER_S = 12.0


def test_engine_meets_its_throughput_floor():
    src = TapeSource("synth", sessions=2, rows_per_session=60_000, seed=5)
    tape, bars = src.build()
    grid = param_grid(fast=[3, 5, 8, 13],
                      slow=[20, 40, 70],
                      stop_ticks=[20, 30],
                      target_ticks=[40, 60])
    assert len(grid) == 48
    t0 = time.perf_counter()
    rows = run_sweep(MaCrossBracket, grid, bars, EngineConfig(), GC)
    elapsed = time.perf_counter() - t0
    rate = len(grid) / elapsed
    assert sum(r["n_trades"] for r in rows) > 0, "a sweep with no trades measures nothing"
    assert rate > FLOOR_COMBOS_PER_S, (
        f"{rate:.1f} combos/s over {len(tape):,} tape rows / {bars.n:,} bars "
        f"is below the {FLOOR_COMBOS_PER_S} floor"
    )


def test_the_sweep_is_deterministic():
    src = TapeSource("synth", sessions=1, rows_per_session=30_000, seed=9)
    tape, bars = src.build()
    grid = param_grid(fast=[5, 8], slow=[30, 50], stop_ticks=[20], target_ticks=[40])
    a = run_sweep(MaCrossBracket, grid, bars, EngineConfig(), GC)
    b = run_sweep(MaCrossBracket, grid, bars, EngineConfig(), GC)
    assert a == b


@pytest.mark.slow
def test_parallel_sweep_matches_the_serial_one():
    src = TapeSource("synth", sessions=1, rows_per_session=20_000, seed=9)
    tape, bars = src.build()
    grid = param_grid(fast=[5, 8, 13], slow=[30, 50], stop_ticks=[20], target_ticks=[40])
    serial = run_sweep(MaCrossBracket, grid, bars, EngineConfig(), GC)
    par = run_sweep_parallel(MaCrossBracket, grid, src, EngineConfig(), GC, workers=2)
    key = lambda r: (r["fast"], r["slow"], r["stop_ticks"], r["target_ticks"])
    assert sorted(serial, key=key) == sorted(par, key=key)
