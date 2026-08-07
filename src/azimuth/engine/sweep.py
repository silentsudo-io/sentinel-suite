"""Parameter sweeps + the THROUGHPUT MEASUREMENT that sets the Rust bar.

    "Python + NumPy first. Rust only if measurement proves it necessary --
     650 combos in 8.9 s is *their* Rust number; find ours before assuming we
     need theirs. Set the bar before writing the Rust." (spec §6, §10.3)

Run it:

    C:\\ntbv\\Scripts\\python.exe -m engine.sweep --sessions 5 --combos 648

The number it prints is the bar. Rust is justified only when a real workload
misses it, and the workload has to be named when that claim is made.
"""
from __future__ import annotations

import argparse
import itertools
import os
import time
from concurrent.futures import ProcessPoolExecutor
from dataclasses import dataclass, field

from .bars import Bars, time_bars
from .config import EngineConfig, InstrumentSpec, spec_for
from .contract import Tape, load_sessions, synth_tape
from .engine import Backtester
from .strategy import MarketContext, Strategy


def param_grid(**axes) -> list[dict]:
    """Cartesian product of named axes, in a stable order."""
    keys = list(axes)
    return [dict(zip(keys, vals)) for vals in itertools.product(*(axes[k] for k in keys))]


@dataclass(frozen=True)
class TapeSource:
    """A picklable description of where a run's tape comes from.

    Workers rebuild the tape from this instead of receiving 100 MB of arrays
    through a pipe -- which on Windows (spawn, no fork) is the difference
    between a parallel sweep and a slower one.
    """

    kind: str                       # 'synth' | 'parquet'
    sessions: int = 5
    rows_per_session: int = 90_000
    mean_gap_ms: int = 900
    seed: int = 0
    paths: tuple[str, ...] = ()
    instrument: str = "GC"
    bar_period_ms: int = 30_000

    def build(self) -> tuple[Tape, Bars]:
        if self.kind == "synth":
            t = synth_tape(self.sessions, rows_per_session=self.rows_per_session,
                           seed=self.seed, mean_gap_ms=self.mean_gap_ms)
        elif self.kind == "parquet":
            t = load_sessions(list(self.paths))
        else:
            raise ValueError(f"unknown tape source kind {self.kind!r}")
        return t, time_bars(t, self.bar_period_ms)


@dataclass
class SweepResult:
    rows: list[dict] = field(default_factory=list)
    elapsed_s: float = 0.0
    n_bars: int = 0
    n_tape_rows: int = 0
    workers: int = 1

    @property
    def combos_per_s(self) -> float:
        return len(self.rows) / self.elapsed_s if self.elapsed_s else float("inf")

    def best(self, key: str = "net_pnl") -> dict:
        return max(self.rows, key=lambda r: r.get(key, float("-inf")))


def run_sweep(strategy_cls: type[Strategy], grid: list[dict], bars: Bars,
              cfg: EngineConfig | None = None,
              spec: InstrumentSpec | None = None) -> list[dict]:
    """Every combo, in-process. The per-tape interval extremes are computed once
    (they live on `Bars`) and reused by every combo -- that reuse is where most
    of the engine's speed comes from."""
    cfg = cfg or EngineConfig()
    spec = spec or spec_for(bars.tape.instrument or "GC")
    bt = Backtester(cfg, spec)
    ctx = MarketContext(bars.tape, bars)
    out = []
    for p in grid:
        strat = strategy_cls(**p)
        res = bt.run(bars, strat.generate(ctx), spec=spec)
        out.append({**p, **res.metrics()})
    return out


# -------------------------------------------------------------- parallel
_W: dict = {}


def _init(src: TapeSource, cfg: EngineConfig, spec: InstrumentSpec,
          strategy_module: str, strategy_name: str) -> None:
    import importlib

    tape, bars = src.build()
    _W["bars"] = bars
    _W["ctx"] = MarketContext(tape, bars)
    _W["bt"] = Backtester(cfg, spec)
    _W["spec"] = spec
    _W["cls"] = getattr(importlib.import_module(strategy_module), strategy_name)


def _work(chunk: list[dict]) -> list[dict]:
    out = []
    for p in chunk:
        strat = _W["cls"](**p)
        res = _W["bt"].run(_W["bars"], strat.generate(_W["ctx"]), spec=_W["spec"])
        out.append({**p, **res.metrics()})
    return out


def run_sweep_parallel(strategy_cls: type[Strategy], grid: list[dict],
                       src: TapeSource, cfg: EngineConfig | None = None,
                       spec: InstrumentSpec | None = None,
                       workers: int | None = None) -> list[dict]:
    cfg = cfg or EngineConfig()
    spec = spec or spec_for(src.instrument)
    workers = workers or max(1, (os.cpu_count() or 2) - 1)
    n = max(1, len(grid) // (workers * 4))
    chunks = [grid[i:i + n] for i in range(0, len(grid), n)]
    out: list[dict] = []
    with ProcessPoolExecutor(
        max_workers=workers, initializer=_init,
        initargs=(src, cfg, spec, strategy_cls.__module__, strategy_cls.__name__),
    ) as ex:
        for part in ex.map(_work, chunks):
            out.extend(part)
    return out


# ------------------------------------------------------------------ bench
def bench(sessions: int = 5, combos: int = 648, rows_per_session: int = 90_000,
          bar_period_ms: int = 30_000, workers: int | None = None,
          quiet: bool = False) -> dict:
    from .demo_strategies import MaCrossBracket

    src = TapeSource("synth", sessions=sessions, rows_per_session=rows_per_session,
                     seed=42, bar_period_ms=bar_period_ms)
    t0 = time.perf_counter()
    tape, bars = src.build()
    build_s = time.perf_counter() - t0

    grid = param_grid(fast=[3, 5, 8, 13, 21, 34],
                      slow=[20, 30, 40, 55, 70, 90],
                      stop_ticks=[15, 20, 30],
                      target_ticks=[25, 35, 45, 60, 80, 100])[:combos]

    spec = spec_for("GC")
    cfg = EngineConfig()

    # signal generation alone, so the engine's share is honest
    ctx = MarketContext(tape, bars)
    t0 = time.perf_counter()
    sigs = [MaCrossBracket(**p).generate(ctx) for p in grid]
    sig_s = time.perf_counter() - t0

    bt = Backtester(cfg, spec)
    t0 = time.perf_counter()
    n_trades = 0
    for s in sigs:
        n_trades += len(bt.run(bars, s, spec=spec).trades)
    eng_s = time.perf_counter() - t0

    t0 = time.perf_counter()
    rows_par = run_sweep_parallel(MaCrossBracket, grid, src, cfg, spec, workers=workers)
    par_s = time.perf_counter() - t0

    out = {
        "sessions": sessions, "tape_rows": len(tape), "bars": bars.n,
        "combos": len(grid), "trades_total": n_trades,
        "tape_build_s": build_s,
        "signal_gen_s": sig_s, "engine_s": eng_s,
        "serial_total_s": sig_s + eng_s,
        "serial_combos_per_s": len(grid) / (sig_s + eng_s),
        "engine_only_combos_per_s": len(grid) / eng_s,
        "parallel_s": par_s, "parallel_combos_per_s": len(rows_par) / par_s,
        "workers": workers or max(1, (os.cpu_count() or 2) - 1),
        "cpu_count": os.cpu_count(),
    }
    if not quiet:
        print(f"\n  tape          {out['tape_rows']:,} rows over {sessions} sessions "
              f"-> {out['bars']:,} bars ({bar_period_ms/1000:g}s)")
        print(f"  built in      {build_s:.2f}s (once, shared by every combo)")
        print(f"  combos        {out['combos']}   trades produced {n_trades:,}")
        print(f"  signal gen    {sig_s:.2f}s")
        print(f"  ENGINE        {eng_s:.2f}s   -> {out['engine_only_combos_per_s']:.1f} combos/s")
        print(f"  SERIAL total  {out['serial_total_s']:.2f}s   "
              f"-> {out['serial_combos_per_s']:.1f} combos/s")
        print(f"  PARALLEL      {par_s:.2f}s on {out['workers']} workers   "
              f"-> {out['parallel_combos_per_s']:.1f} combos/s")
        print(f"\n  reference: Quant Charts (Rust) quotes 650 combos in 8.9 s "
              f"= 73.0 combos/s\n")
    return out


if __name__ == "__main__":
    ap = argparse.ArgumentParser(description="Sentinel Azimuth engine throughput")
    ap.add_argument("--sessions", type=int, default=5)
    ap.add_argument("--combos", type=int, default=648)
    ap.add_argument("--rows", type=int, default=90_000, help="tape rows per session")
    ap.add_argument("--bar-ms", type=int, default=30_000)
    ap.add_argument("--workers", type=int, default=None)
    a = ap.parse_args()
    bench(a.sessions, a.combos, a.rows, a.bar_ms, a.workers)
