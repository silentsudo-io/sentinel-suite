"""SentinelTBars -> Python. The Azimuth's port of `BarsTypes\\SentinelTBars_v1_0_0.cs`.

SPEC: SENTINEL_AZIMUTH_SPEC.md 1 (the two columns) - 2 (THE PARITY LAW) - 3.1 (tape contract).
GATE: artefact kind `bartype`, pairing key `(session, bar_index)`.

    ONE definition, TWO implementations. This file is the second one. It is NOT trusted
    for research until the `bartype` gate has been RUN against NinjaTrader, and as of
    this writing it has NOT been - see "THE GATE'S TRUE STATUS" at the bottom.

WHAT IT IS
----------
SentinelTBars is an adaptive Renko-hybrid: Renko bricks with Heikin-Ashi BODIES and real
price wicks, wrapped in an ATR floor, a breakout-confirmation gate, trend hysteresis, a
per-brick density controller, quiet-hours gating, forced time bricks and micro-splits.
It is a per-TICK state machine and there is no vectorised form of it: every brick's
geometry depends on the ATR and the density scale left behind by the brick before it.

THE PARAMETERS - DECODED FROM THE CODE, NOT FROM THE NAME
---------------------------------------------------------
The corpus bartag is `212201v6x24[@LANE]`. It decomposes as `<barTypeId>v<Value>x<Value2>`
(SentinelCore.BarTag, SentinelCore_v1_0_0.cs). 212201 is SentinelTBars' reserved
BarsPeriodType id.

* `6/24` is NOT two knobs. The chart exposes ONE field - "Speed Settings", which is
  NT's `BaseBarsPeriodValue` renamed by `SetPropertyName` in `State.Configure`. The same
  block derives the pair (SentinelTBars_v1_0_0.cs, State.Configure):

      BarsPeriod.Value  = BaseBarsPeriodValue / 2      # integer division
      BarsPeriod.Value2 = BaseBarsPeriodValue * 2

  so `6/24` == **Speed Settings 12**, and the three offsets that actually drive the bars
  (`LatchConfig`) are:

      baseTrendOffset    = Value                * tickSize =  6 ticks   (with-trend)
      baseReversalOffset = Value2               * tickSize = 24 ticks   (counter-trend)
      baseOpenOffset     = BaseBarsPeriodValue  * tickSize = 12 ticks   (brick body seed)

  Cross-checked against `Lab\\sentinel_lab\\bartag.py::_is_speed`, which classifies a brick
  bar STRUCTURALLY by `Value2 == 4 * Value` and reports `Speed = Value * 2` -> 12. Same
  answer from an independent implementation.

* `@AUD` / `@AUD0826` / `@AUD0626` / `@TEST` / `@STB20FCA` are **LANES, not parameters.**
  A lane is a per-chart scope discriminator: `SentinelCore.ComposeLane` appends `"@" + lane`
  to a bare scope, `SanitizeLane` strips it to alphanumerics, and it is sourced from
  `Sentinel\\Lanes.conf` or the F6 `ScopeLane` property. It exists so two charts on the SAME
  instrument and SAME bars type do not overwrite each other's seams.
  **The bars type never reads it** - `PublishBrickTick`/`LogBrick` call
  `SentinelCore.ScopeOf(bars.Instrument, bars.BarsPeriod)`, the BARE two-part form, with no
  lane overload anywhere in the file. Confirmed by the corpus itself: it carries both
  `212201v6x24` and `212201v6x24@AUD`, which are the same geometry recorded on two charts.
  So `@AUD` changes WHICH ROWS you select, never HOW THE BARS ARE BUILT.
  (`@STB20FCA` sits on `212201v10x40` and `@STB24FCA` on `212201v12x48` - the "20"/"24" in
  the lane name is a human echo of Speed 20 / Speed 24, redundant with the numeric part.
  That redundancy is the tell that the lane is a label.)

WHAT A NAIVE PORT GETS WRONG - every one of these is a real behaviour of the C#
-------------------------------------------------------------------------------
 1. **The closing brick's extreme is CLIPPED to the boundary, the other extreme is not.**
    `CreateBreakoutBar`: `barHigh = overMax ? breakoutPrice : GetHigh(last)`. On an up-brick
    the real high (which by construction exceeded barMax) is DISCARDED and replaced by the
    boundary; the low survives. A port that keeps real OHLC produces different bars.
 2. **`breakoutPrice` is always the boundary**, never the trade price: `min(close, barMax)`
    when close > barMax is barMax. The overshoot is deliberately handed to the next brick.
 3. **A new brick is BORN with a 12-tick body**, not flat. `AddBar(nextHaOpen, nextHigh,
    nextLow, ...)` with `nextLow = syntheticOpen = breakoutPrice - baseOpenOffset*dir`.
    That is what `baseOpenOffset` is for, and it feeds straight into the micro-split ratio.
 4. **Volume is DOUBLE-COUNTED at every brick boundary.** The closing tick's volume is
    passed to `UpdateBar` (which accumulates) AND to `AddBar` for the new brick - and on a
    confirming tick `UpdateExistingBar` runs twice more around the chain. So
    `sum(bar volumes) > sum(tick volumes)`, by construction. Faithfully reproduced.
 5. **`lastBoundaryTouch` does not mean what it says.** It is only ever assigned in
    `CreateBreakoutBar`/`ForceTimeBrick`, i.e. it is "time of last brick". With
    ForceStagnationSeconds=90 > MinBarLifeSeconds=10 the whole `ShouldForceTimeBrick`
    predicate collapses to "90 s since the last brick".
 6. **A slow drift beyond the boundary NEVER prints a breakout brick.** The speed gate is
    `penetrationTicks / elapsedSeconds >= 1.6`, and elapsed is measured from the FIRST tick
    beyond. One tick of penetration after 5 s scores 0.2 and can never recover, because
    `pendingStartTime` is not refreshed while price stays outside. Such a move exits via the
    90-second forced time brick instead. This dominates quiet tape.
 7. **`ForceTimeBrick` emits a candle whose open/close sit OUTSIDE its high/low.**
    `AddBar(bars, haOpen, barOpen, barOpen, haOpen, ...)` -> (open, high, low, close) =
    (haOpen, close, close, haOpen). high == low == the real price, open == close == the HA
    value. Not a typo in the port; it is what the C# does.
 8. **`ForceTimeBrick` flips `barDirection` without touching `sameDirCount`**, so the
    hysteresis run-length survives a direction change through that path.
 9. **The HA chain is re-anchored every tick.** `UpdateExistingBar` ends with
    `haPrevOpen = bars.GetOpen(last)` - the bar's actual open, discarding the smoothed
    value. And `CreateBreakoutBar` applies `GetHeikinAshiOpen` TWICE (once into `haPrevOpen`,
    then again to form `nextHaOpen`). Both reproduced literally.
10. **The `backInside` branch is DEAD CODE.** It is only reached when `overMax || underMin`
    against unchanged barMax/barMin, so `backInside` is always false. Reproduced as written
    rather than "fixed", because fixing it changes the bars.
11. **`InitializeFirstBar` does not round `barMax`/`barMin` to tick size**, unlike every
    other assignment to them.
12. **Quiet hours read `DateTime.Hour` of the platform's display timezone**, not UTC. The
    tape is UTC ms. See `TBarsParams.timezone` - this is a PARAMETER, and getting it wrong
    silently changes the confirmation thresholds for 5 hours of every session.
13. **`ConfirmTicksBeyond = 1` actually requires TWO ticks of penetration**, because
    `Math.Abs((pendingFarthest - pendingBoundary) / tickSize)` on a genuine one-tick move
    evaluates to **0.999999999994543**, not 1.0, and the test is `penetrationTicks <
    ticksThresh -> reject`. Measured, not assumed. A port that "tidies" this into
    `round((farthest - boundary) / tick)` confirms a tick earlier than NinjaTrader on every
    breakout and is a different bar type. Reproduced by using the same expression.
14. **DIVIDE by tickSize; never multiply by its reciprocal.** For binary64 `x / 0.1` and
    `x * 10.0` differ at the last ULP on ~18% of values (measured over 200,000 samples).
    The C# divides, so this does. The `bartype` gate declares EXACT (0.0) - one ULP FAILS.

THE ENGINE SEAM
---------------
`engine\\bars.py::bars_from_end_idx(tape, end_idx)` is the ONE interface (spec 4). This
module produces `end_idx`; it does not invent a second seam. Note the split:

  * `TBarsSeries.close_row` - the tape row that closed each NATIVE brick. That IS `end_idx`,
    handed to the seam unchanged. It is NON-DECREASING, not strictly increasing: chaining and
    micro-splits close several bricks on ONE tick, and the seam represents those as row-less
    bars. Collapsing them would renumber every later bar and move the gate's
    `(session, bar_index)` coordinate, so they are passed through and COUNTED
    (`duplicate_end_idx`).
  * `to_bars` hands the NATIVE HA/Renko OHLCV through the seam's keyword overrides, because
    a brick LEVEL and a Heikin-Ashi body are not tape prices and must not be re-derived.

TAPE
----
Reads the 3.1 parquet directly rather than through `engine.contract.load_session`, because
`validate()` refuses the real `GC 02-26` tape over 3.2's crossed quotes (140 rows on
2025-12-09) and TBars is BuiltFrom=Tick - it never touches bid/ask, so a crossed book cannot
affect a single brick. The crossed count is COUNTED AND RETURNED, never dropped silently.
"""
from __future__ import annotations

import json
import math
import os
import sys
from dataclasses import dataclass, field, replace
from datetime import datetime, timezone

import numpy as np

__all__ = [
    "TBarsParams", "TBarsSeries", "SPEED_12",
    "build", "build_session", "from_tape", "load_tape_session",
    "engine_end_idx", "duplicate_end_idx", "to_bars", "gate_rows", "gate_meta", "bartag",
    "CLOSE_BREAKOUT", "CLOSE_CHAIN", "CLOSE_TIME", "CLOSE_MICRO", "CLOSE_OPEN",
]

IMPL = "azimuth.bars.tbars"
IMPL_VER = "0.1.0"
SOURCE_OF_TRUTH = "BarsTypes/SentinelTBars_v1_0_0.cs"
BARS_PERIOD_TYPE = 212201

# why each bar ended - NOTED, never a gate field
CLOSE_BREAKOUT = "breakout"   # confirmed (or unconfirmed-mode) boundary break
CLOSE_CHAIN = "chain"         # ChainWhileBeyond, same tick as a breakout
CLOSE_TIME = "time"           # ShouldForceTimeBrick -> ForceTimeBrick (90 s stagnation)
CLOSE_MICRO = "micro"         # MaybeMicroSplit -> ForceTimeBrick
CLOSE_OPEN = "open"           # still forming when the tape/session ended


# --------------------------------------------------------------------------- params
@dataclass
class TBarsParams:
    """Every tunable in `SentinelTBars_v1_0_0.cs`, at its `SetDefaults` value.

    `speed` is the chart's "Speed Settings" field (NT's `BaseBarsPeriodValue`); `value` and
    `value2` derive from it exactly as `State.Configure` does. Pass them explicitly only to
    model a chart whose BarsPeriod was set some other way.

    The `TbarsSudoV3Registry` live-tuning override in `LatchConfig` has no Python analogue -
    it is a running-NT store. This port is the cfg == null path, which is what every replay
    chart and every corpus row at `212201v6x24` was built with.
    """

    # -- geometry (the "6/24" the bartag reports) --
    speed: int = 12
    value: int | None = None            # trend offset, ticks  (default speed // 2)
    value2: int | None = None           # reversal offset, ticks (default speed * 2)

    # -- breakout confirmation --
    use_breakout_confirmation: bool = True
    confirm_ticks_beyond: int = 1
    confirm_milliseconds: int = 120
    min_speed_ticks_per_second: float = 1.6
    max_wick_giveback_ratio: float = 0.65
    min_volume_in_window: int = 0

    # -- ATR floor + hysteresis --
    atr_length: int = 14
    atr_mult_trend: float = 0.80
    atr_mult_reversal: float = 1.10
    confirm_trend_bricks: int = 2
    hysteresis_reversal_mult: float = 1.50

    # -- quiet hours (LOCAL hours of `timezone`, see note 12) --
    enable_quiet_hours_gating: bool = True
    quiet_start_hour: int = 18
    quiet_end_hour: int = 23
    quiet_ticks_add: float = 1.0
    quiet_ms_mult: float = 1.35
    quiet_speed_mult: float = 0.75

    # -- density controller --
    target_bars_per_session: int = 1000
    assumed_session_hours: float = 23.0
    min_scale: float = 0.25
    max_scale: float = 2.5
    scale_smoothing: float = 0.15

    # -- forced bricks --
    force_stagnation_seconds: int = 90
    min_bar_life_seconds: int = 10
    micro_split_ratio: float = 0.55
    enable_micro_split: bool = True

    # -- host settings, not bar-type fields --
    #: NT stamps bar times in the platform's display timezone; `InQuietHours` reads
    #: `DateTime.Hour` off that. This is an ASSUMPTION the gate must pin, not a fact.
    timezone: str = "America/New_York"
    #: NT's Data Series "Break at EOD" (`Bars.IsResetOnNewTradingDay`). True => the whole
    #: state machine restarts at every session boundary via `InitializeFirstBar`.
    reset_on_new_trading_day: bool = True

    # class constants from the C# (not tunable there either)
    density_deadband: float = 0.10
    density_gain: float = 0.50
    density_max_step: float = 0.05

    def __post_init__(self) -> None:
        if self.value is None:
            self.value = self.speed // 2          # C#: BaseBarsPeriodValue / 2, integer
        if self.value2 is None:
            self.value2 = self.speed * 2
        if self.speed <= 0:
            raise ValueError("speed (Speed Settings / BaseBarsPeriodValue) must be > 0")
        # LatchConfig's degenerate-config guards, ported verbatim
        if self.atr_length < 1:
            self.atr_length = 1
        if self.min_scale <= 0:
            self.min_scale = 0.01
        if self.max_scale < self.min_scale:
            self.max_scale = self.min_scale

    # -- identity ----------------------------------------------------------
    def bartag(self) -> str:
        """`SentinelCore.BarTag(bp)` -> `<id>v<Value>x<Value2>` -> `212201v6x24`."""
        return "%dv%dx%d" % (BARS_PERIOD_TYPE, self.value, self.value2)

    def params_string(self) -> str:
        """The gate's `bar_params` PRECONDITION field: different params, different bars.

        Every field that can change a brick, in a fixed order, so two runs of the same
        configuration produce byte-identical strings and any drift ABORTs the gate instead
        of quietly comparing two different experiments.
        """
        return ";".join([
            "tag=%s" % self.bartag(),
            "speed=%d" % self.speed,
            "offsets=%d/%d/%d" % (self.value, self.value2, self.speed),
            "confirm=%s/%d/%d/%g/%g/%d" % (
                int(self.use_breakout_confirmation), self.confirm_ticks_beyond,
                self.confirm_milliseconds, self.min_speed_ticks_per_second,
                self.max_wick_giveback_ratio, self.min_volume_in_window),
            "atr=%d/%g/%g" % (self.atr_length, self.atr_mult_trend, self.atr_mult_reversal),
            "hyst=%d/%g" % (self.confirm_trend_bricks, self.hysteresis_reversal_mult),
            "quiet=%s/%d-%d/%g/%g/%g" % (
                int(self.enable_quiet_hours_gating), self.quiet_start_hour,
                self.quiet_end_hour, self.quiet_ticks_add, self.quiet_ms_mult,
                self.quiet_speed_mult),
            "dens=%d/%g/%g-%g/%g" % (
                self.target_bars_per_session, self.assumed_session_hours,
                self.min_scale, self.max_scale, self.scale_smoothing),
            "force=%d/%d" % (self.force_stagnation_seconds, self.min_bar_life_seconds),
            "micro=%s/%g" % (int(self.enable_micro_split), self.micro_split_ratio),
            "tz=%s" % self.timezone,
            "eod=%d" % int(self.reset_on_new_trading_day),
        ])


#: The configuration behind ~65,800 corpus rows: bartag `212201v6x24`, "SentinelTBars 6/24",
#: chart field "Speed Settings" = 12.
SPEED_12 = TBarsParams(speed=12)


def bartag(params: TBarsParams | None = None) -> str:
    return (params or SPEED_12).bartag()


# --------------------------------------------------------------------------- result
@dataclass
class TBarsSeries:
    """The NATIVE SentinelTBars series - HA bodies, Renko-clipped wicks.

    `open`/`close` are HEIKIN-ASHI (synthetic; a price that never traded - see the firePx
    incident). `high`/`low` are real traded prices EXCEPT where `CreateBreakoutBar` clipped
    one of them to the brick boundary, and except on `ForceTimeBrick`'s newborn bar where
    open/close can sit outside high/low entirely (note 7).
    """

    open: np.ndarray
    high: np.ndarray
    low: np.ndarray
    close: np.ndarray
    volume: np.ndarray
    ts_ms: np.ndarray            # bar CLOSE stamp (time of the tick that closed it)
    open_ts_ms: np.ndarray       # time of the tick the bar was AddBar'd on
    tick_count: np.ndarray       # distinct tape rows that touched the bar
    close_row: np.ndarray        # tape row index that closed the bar (may repeat!)
    open_row: np.ndarray         # tape row index the bar was born on
    session_id: np.ndarray
    direction: np.ndarray        # barDirection at the moment the bar closed
    close_reason: list[str]
    # diagnostics, NOTED never gated
    atr: np.ndarray = field(default=None, repr=False)
    trend_offset: np.ndarray = field(default=None, repr=False)
    reversal_offset: np.ndarray = field(default=None, repr=False)
    dyn_scale: np.ndarray = field(default=None, repr=False)
    params: TBarsParams = field(default=None, repr=False)
    tick_size: float = 0.0
    #: rows the state machine consumed (kind==trade with a finite `last`)
    n_ticks: int = 0
    #: sum of the tape volume it consumed - compare against `volume.sum()` (note 4)
    tape_volume: int = 0

    @property
    def n(self) -> int:
        return int(self.open.shape[0])

    def __len__(self) -> int:
        return self.n


# --------------------------------------------------------------------------- time
def _local_hours(ts_ms: np.ndarray, tzname: str) -> np.ndarray:
    """Hour-of-day in `tzname` for each row - what `DateTime.Hour` reads in NT.

    Exact across DST: the offset is evaluated once per distinct UTC hour bucket, and every
    IANA transition falls on an hour boundary, so no row can straddle one.
    """
    from zoneinfo import ZoneInfo

    tz = ZoneInfo(tzname)
    sec = (ts_ms // 1000).astype(np.int64)
    bucket = sec // 3600
    uniq = np.unique(bucket)
    offs = np.empty(uniq.shape[0], dtype=np.int64)
    for i, h in enumerate(uniq):
        dt = datetime.fromtimestamp(int(h) * 3600, timezone.utc).astimezone(tz)
        offs[i] = int(dt.utcoffset().total_seconds())
    off = offs[np.searchsorted(uniq, bucket)]
    return (((sec + off) // 3600) % 24).astype(np.int8)


# --------------------------------------------------------------------------- tape
def load_tape_session(path: str, *, require_sidecar: bool = True) -> dict:
    """One 3.1 session parquet, as the arrays this port needs, plus its sidecar.

    Deliberately NOT `engine.contract.load_session`: `validate()` refuses the real
    `GC 02-26` tape over 3.2's crossed quotes. TBars is BuiltFrom=Tick and reads only the
    trade stream, so a crossed book cannot move a brick - but the count is RETURNED
    (`crossed_rows`) rather than dropped, because a silently dropped row is a silently
    changed answer.
    """
    import pyarrow.parquet as pq

    tbl = pq.read_table(path)
    need = ("ts_ms", "bid", "ask", "last", "size", "kind")
    missing = [c for c in need if c not in tbl.column_names]
    if missing:
        raise ValueError("%s: missing 3.1 tape columns %s" % (path, missing))

    def col(name, dt):
        a = tbl.column(name).to_numpy(zero_copy_only=False)
        if dt.startswith("int") and a.dtype.kind == "f":
            a = np.nan_to_num(a, nan=0.0)
        return np.ascontiguousarray(a, dtype=dt)

    ts = col("ts_ms", "int64")
    bid = col("bid", "float64")
    ask = col("ask", "float64")
    last = col("last", "float64")
    size = col("size", "int32")
    kind = col("kind", "int8")

    if ts.shape[0] == 0:
        raise ValueError("%s: empty tape; an empty side is ABORT, not PASS" % path)
    if np.any(np.diff(ts) < 0):
        bad = int(np.flatnonzero(np.diff(ts) < 0)[0])
        raise ValueError("%s: ts_ms not monotonic at row %d" % (path, bad + 1))

    meta_path = path.rsplit(".parquet", 1)[0] + ".meta.json"
    if os.path.isfile(meta_path):
        with open(meta_path, encoding="utf-8") as fh:
            meta = json.load(fh)
    elif require_sidecar:
        raise FileNotFoundError(
            "no provenance sidecar next to %s. 3.1: a tape file without its sidecar is not "
            "admissible to a gate." % path)
    else:
        meta = {}

    trade = (kind == 1) & np.isfinite(last)
    rows = np.flatnonzero(trade).astype(np.int64)
    return {
        "path": path,
        "meta": meta,
        "ts_ms": ts, "bid": bid, "ask": ask, "last": last, "size": size, "kind": kind,
        "trade_rows": rows,
        "n_rows": int(ts.shape[0]),
        "n_trades": int(rows.shape[0]),
        "crossed_rows": int(np.count_nonzero(ask < bid)),
        "session": str(meta.get("session_date", "")),
        "instrument": str(meta.get("instrument", "")),
        "tape_sha256": str(meta.get("source_file_sha256", "")),
    }


def _as_tape(sess: dict):
    """A `engine.contract.Tape` over the loaded session, bypassing `validate()`.

    Constructed directly rather than through `load_session` for the 3.2 reason above. The
    bypass is stated here and reported by `load_tape_session`'s `crossed_rows`; it is not
    a silent coercion.
    """
    _ensure_azimuth_on_path()
    from engine.contract import Tape

    n = sess["n_rows"]
    zeros = np.zeros(n, dtype=np.int32)
    return Tape(
        sess["ts_ms"], sess["bid"], sess["ask"], sess["last"], sess["size"],
        zeros, zeros, sess["kind"], np.zeros(n, dtype=np.int32),
        [sess["meta"]], str(sess["meta"].get("instrument", "")),
    )


def _ensure_azimuth_on_path() -> None:
    """Make `engine.*` importable when this module is imported standalone.

    The sibling `bars\\__init__.py` is owned by another track; this module must not depend
    on it existing.
    """
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    if root not in sys.path:
        sys.path.insert(0, root)


# --------------------------------------------------------------------------- the port
def build(
    ts_ms,
    price,
    volume,
    *,
    tick_size: float,
    params: TBarsParams | None = None,
    row_index=None,
    session_id=None,
) -> TBarsSeries:
    """Run `SentinelTBars_v1_0_0.OnDataPoint` over a TRADE-tick stream.

    `ts_ms`/`price`/`volume` are the trade ticks only, in time order - that is exactly what
    NT feeds a `BuiltFrom = BarsPeriodType.Tick` bars type (`open==high==low==close==price`,
    `volume == trade size`, `isBar == false`). `row_index[i]` is tick i's row in the FULL
    tape, so `close_row` indexes the tape the engine will run on. `session_id` drives
    `SessionIterator.IsNewSession`; our tape is one ET trading day per file, so a caller
    with one session may omit it.
    """
    p = params or SPEED_12
    ts_ms = np.ascontiguousarray(ts_ms, dtype=np.int64)
    price = np.ascontiguousarray(price, dtype=np.float64)
    volume = np.ascontiguousarray(volume, dtype=np.int64)
    n = ts_ms.shape[0]
    if not (price.shape[0] == volume.shape[0] == n):
        raise ValueError("ts_ms, price and volume must be the same length")
    if n == 0:
        raise ValueError("no trade ticks: an empty side is ABORT, not PASS")
    if tick_size <= 0:
        raise ValueError("tick_size must be > 0")
    if row_index is None:
        row_index = np.arange(n, dtype=np.int64)
    else:
        row_index = np.ascontiguousarray(row_index, dtype=np.int64)
        if row_index.shape[0] != n:
            raise ValueError("row_index must be the same length as ts_ms")
    if session_id is None:
        session_id = np.zeros(n, dtype=np.int32)
    else:
        session_id = np.ascontiguousarray(session_id, dtype=np.int32)
        if session_id.shape[0] != n:
            raise ValueError("session_id must be the same length as ts_ms")

    hours = (_local_hours(ts_ms, p.timezone) if p.enable_quiet_hours_gating
             else np.zeros(n, dtype=np.int8))

    tick = float(tick_size)
    eps = tick * 1e-8          # MasterInstrument.Compare's tick-scaled epsilon
    # NOTE: DIVIDE by tick, never multiply by 1/tick. The C# writes `price / TickSize`
    # and `(a - b) / tickSize`, and for binary64 `x / 0.1 != x * 10.0` on ~18% of
    # values at the last ULP. The gate declares EXACT (0.0); one ULP is a FAIL.
    floor = math.floor

    # ---- tunables into locals (the C# reads fields; this is the same values) ----
    use_conf = bool(p.use_breakout_confirmation)
    conf_ticks = float(p.confirm_ticks_beyond)
    conf_ms = float(p.confirm_milliseconds)
    min_tps = float(p.min_speed_ticks_per_second)
    max_giveback = float(p.max_wick_giveback_ratio)
    min_vol_win = int(p.min_volume_in_window)
    atr_alpha = 2.0 / (p.atr_length + 1.0)
    atr_m_trend = float(p.atr_mult_trend)
    atr_m_rev = float(p.atr_mult_reversal)
    conf_trend_bricks = int(p.confirm_trend_bricks)
    hyst_mult = float(p.hysteresis_reversal_mult)
    quiet_on = bool(p.enable_quiet_hours_gating)
    q_start, q_end = int(p.quiet_start_hour), int(p.quiet_end_hour)
    q_ticks_add, q_ms_mult, q_speed_mult = (
        float(p.quiet_ticks_add), float(p.quiet_ms_mult), float(p.quiet_speed_mult))
    target_bars = int(p.target_bars_per_session)
    session_seconds = max(1.0, float(p.assumed_session_hours) * 3600.0)
    min_scale, max_scale = float(p.min_scale), float(p.max_scale)
    smoothing = float(p.scale_smoothing)
    deadband, gain, max_step = (
        float(p.density_deadband), float(p.density_gain), float(p.density_max_step))
    force_stag = float(p.force_stagnation_seconds)
    min_life = float(p.min_bar_life_seconds)
    micro_ratio = float(p.micro_split_ratio)
    micro_on = bool(p.enable_micro_split)
    base_trend = p.value * tick
    base_rev = p.value2 * tick
    base_open = p.speed * tick
    reset_eod = bool(p.reset_on_new_trading_day)

    # ---- bar store (lists; the C# reads GetX(last) and GetClose(last-1)) ----
    b_open: list[float] = []
    b_high: list[float] = []
    b_low: list[float] = []
    b_close: list[float] = []
    b_vol: list[int] = []
    b_ts: list[int] = []
    b_open_ts: list[int] = []
    b_open_row: list[int] = []
    b_close_row: list[int] = []
    b_ticks: list[int] = []
    b_sess: list[int] = []
    b_dir: list[int] = []
    b_reason: list[str] = []
    b_atr: list[float] = []
    b_toff: list[float] = []
    b_roff: list[float] = []
    b_dyn: list[float] = []
    last_touch_row = -1        # for tick_count of the forming bar

    # ---- state ----
    bar_open = brick_basis = bar_max = bar_min = synthetic_open = 0.0
    bar_direction = 1
    ha_prev_open = ha_prev_close = 0.0
    atr_ema = 0.0
    same_dir = 0
    trend_off = rev_off = 0.0
    dyn_scale = 1.0
    session_start_ms = 0
    bars_this_session = 0
    last_boundary_touch_ms = -1     # DateTime.MinValue
    last_bar_birth_ms = -1
    pending = False
    pending_dir = 0
    pending_boundary = pending_farthest = 0.0
    pending_start_ms = -1
    pending_vol = 0
    prev_session = -1
    started = False
    tape_volume = 0

    # ---- per-tick inputs, rebound each iteration; the helpers below close over them ----
    t_ms = 0
    close = 0.0
    vol = 0
    row = 0
    sess = 0

    # =========================================================================
    # The C# helpers, one Python function each, called exactly where OnDataPoint calls
    # their originals. Defined ONCE (not per tick) - they close over the loop variables
    # above, which the loop rebinds.
    # =========================================================================
    def _update_existing_bar():
        """UpdateExistingBar - accumulates volume, re-anchors the HA chain (note 9)."""
        nonlocal ha_prev_close, ha_prev_open, last_touch_row
        j = len(b_high) - 1
        nh = close if close > b_high[j] else b_high[j]
        nl = close if close < b_low[j] else b_low[j]
        hc = (b_open[j] + nh + nl + close) * 0.25
        b_high[j] = nh; b_low[j] = nl; b_close[j] = hc
        b_ts[j] = t_ms; b_close_row[j] = row
        b_vol[j] += vol                      # NT's Bars.Update ACCUMULATES (note 4)
        if last_touch_row != row:
            b_ticks[j] += 1
            last_touch_row = row
        ha_prev_close = hc
        ha_prev_open = b_open[j]

    def _adjust_and_refresh():
        """AdjustScaleForDensityPerBrick + RefreshDynamicOffsets."""
        nonlocal dyn_scale, trend_off, rev_off
        elapsed = (t_ms - session_start_ms) / 1000.0
        if elapsed < 1.0:
            elapsed = 1.0
        progress = elapsed / session_seconds
        if progress > 1.0:
            progress = 1.0
        expected = target_bars * progress
        if expected < 1.0:
            expected = 1.0
        error = bars_this_session / expected - 1.0
        if error > deadband or error < -deadband:
            step = error * gain
            if step > max_step:
                step = max_step
            elif step < -max_step:
                step = -max_step
            target_scale = dyn_scale * (1.0 + step)
            if target_scale < min_scale:
                target_scale = min_scale
            elif target_scale > max_scale:
                target_scale = max_scale
            dyn_scale += (target_scale - dyn_scale) * smoothing
        trend_off = base_trend * dyn_scale
        f = atr_m_trend * atr_ema
        if f > trend_off:
            trend_off = f
        rev_off = base_rev * dyn_scale
        f = atr_m_rev * atr_ema
        if f > rev_off:
            rev_off = f
        if trend_off < tick:
            trend_off = tick
        if rev_off < tick:
            rev_off = tick

    def _update_atr(prev_close, h, l):
        nonlocal atr_ema
        tr = h - l
        a = h - prev_close
        if a < 0:
            a = -a
        b = l - prev_close
        if b < 0:
            b = -b
        if a > tr:
            tr = a
        if b > tr:
            tr = b
        if tr <= 0:
            tr = tick
        atr_ema = tr if atr_ema <= 0 else atr_ema + atr_alpha * (tr - atr_ema)

    def _create_breakout_bar(reason):
        """CreateBreakoutBar - clips the breakout-side extreme, seeds the next brick."""
        nonlocal bar_direction, same_dir, synthetic_open, ha_prev_open, ha_prev_close
        nonlocal bar_max, bar_min, brick_basis, bar_open, bars_this_session
        nonlocal last_boundary_touch_ms, last_bar_birth_ms, last_touch_row
        j = len(b_high) - 1
        over_max = (close - bar_max) > eps
        under_min = (close - bar_min) < -eps

        bp = (close if close < bar_max else bar_max) if over_max else \
             (close if close > bar_min else bar_min)
        bp = floor(bp / tick + 0.5) * tick        # RoundToTickSize

        bar_high = bp if over_max else b_high[j]      # note 1: clip ONE side only
        bar_low = bp if under_min else b_low[j]

        prev_brick_close = b_close[j - 1] if j >= 1 else b_close[j]
        _update_atr(prev_brick_close, bar_high, bar_low)
        _adjust_and_refresh()

        ha_close_break = (b_open[j] + bar_high + bar_low + bp) * 0.25
        b_high[j] = bar_high; b_low[j] = bar_low; b_close[j] = ha_close_break
        b_ts[j] = t_ms; b_close_row[j] = row
        b_vol[j] += vol
        if last_touch_row != row:
            b_ticks[j] += 1
            last_touch_row = row

        new_dir = 1 if over_max else -1
        if new_dir == bar_direction:
            same_dir += 1
        else:
            bar_direction = new_dir
            same_dir = 1
        b_dir[j] = bar_direction
        b_reason[j] = reason
        b_atr[j] = atr_ema; b_toff[j] = trend_off; b_roff[j] = rev_off
        b_dyn[j] = dyn_scale

        synthetic_open = floor((bp - base_open * bar_direction) / tick + 0.5) * tick

        ha_prev_open = (ha_prev_open + ha_prev_close) * 0.5      # note 9, first pass
        ha_prev_close = ha_close_break

        eff_rev = rev_off * (hyst_mult if same_dir >= conf_trend_bricks else 1.0)
        if bar_direction > 0:
            bar_max = floor((bp + trend_off) / tick + 0.5) * tick
            bar_min = floor((bp - eff_rev) / tick + 0.5) * tick
        else:
            bar_max = floor((bp + eff_rev) / tick + 0.5) * tick
            bar_min = floor((bp - trend_off) / tick + 0.5) * tick

        brick_basis = bp
        bar_open = close

        next_ha_open = (ha_prev_open + ha_prev_close) * 0.5      # note 9, second pass
        next_high = bp if over_max else synthetic_open
        next_low = bp if under_min else synthetic_open           # note 3: 12-tick body
        next_ha_close = (next_ha_open + next_high + next_low + bp) * 0.25

        b_open.append(next_ha_open); b_high.append(next_high); b_low.append(next_low)
        b_close.append(next_ha_close); b_vol.append(vol); b_ts.append(t_ms)
        b_open_ts.append(t_ms); b_open_row.append(row); b_close_row.append(row)
        b_ticks.append(1); b_sess.append(sess); b_dir.append(bar_direction)
        b_reason.append(CLOSE_OPEN)
        b_atr.append(atr_ema); b_toff.append(trend_off); b_roff.append(rev_off)
        b_dyn.append(dyn_scale)
        last_touch_row = row

        ha_prev_open, ha_prev_close = next_ha_open, next_ha_close
        bars_this_session += 1
        last_boundary_touch_ms = last_bar_birth_ms = t_ms

    def _force_time_brick(reason):
        """ForceTimeBrick - the 90 s stagnation / micro-split exit (notes 7 and 8)."""
        nonlocal bar_direction, synthetic_open, ha_prev_open, ha_prev_close
        nonlocal brick_basis, bar_open, bar_max, bar_min, bars_this_session
        nonlocal last_boundary_touch_ms, last_bar_birth_ms, last_touch_row
        j = len(b_high) - 1
        high = b_high[j]
        low = b_low[j]

        prev_brick_close = b_close[j - 1] if j >= 1 else b_close[j]
        _update_atr(prev_brick_close, high, low)
        _adjust_and_refresh()

        ha_close = (b_open[j] + high + low + close) * 0.25
        b_high[j] = high; b_low[j] = low; b_close[j] = ha_close
        b_ts[j] = t_ms; b_close_row[j] = row
        b_vol[j] += vol
        if last_touch_row != row:
            b_ticks[j] += 1
            last_touch_row = row

        bar_direction = 1 if close >= brick_basis else -1   # note 8: same_dir untouched
        b_dir[j] = bar_direction
        b_reason[j] = reason
        b_atr[j] = atr_ema; b_toff[j] = trend_off; b_roff[j] = rev_off
        b_dyn[j] = dyn_scale

        synthetic_open = close
        ha_prev_open = (ha_prev_open + ha_prev_close) * 0.5
        ha_prev_close = ha_close
        brick_basis = bar_open = close

        if bar_direction > 0:
            bar_max = floor((bar_open + trend_off) / tick + 0.5) * tick
            bar_min = floor((bar_open - rev_off) / tick + 0.5) * tick
        else:
            bar_max = floor((bar_open + rev_off) / tick + 0.5) * tick
            bar_min = floor((bar_open - trend_off) / tick + 0.5) * tick

        ha_open = (ha_prev_open + ha_prev_close) * 0.5
        # AddBar(haOpen, barOpen, barOpen, haOpen) -> open/close can sit OUTSIDE
        # high/low. Note 7. This is what the C# does.
        b_open.append(ha_open); b_high.append(bar_open); b_low.append(bar_open)
        b_close.append(ha_open); b_vol.append(vol); b_ts.append(t_ms)
        b_open_ts.append(t_ms); b_open_row.append(row); b_close_row.append(row)
        b_ticks.append(1); b_sess.append(sess); b_dir.append(bar_direction)
        b_reason.append(CLOSE_OPEN)
        b_atr.append(atr_ema); b_toff.append(trend_off); b_roff.append(rev_off)
        b_dyn.append(dyn_scale)
        last_touch_row = row

        ha_prev_open = ha_prev_close = ha_open
        bars_this_session += 1
        last_boundary_touch_ms = last_bar_birth_ms = t_ms

    for i in range(n):
        t_ms = int(ts_ms[i])
        close = float(price[i])
        vol = int(volume[i])
        row = int(row_index[i])
        sess = int(session_id[i])
        hour = int(hours[i])
        tape_volume += vol

        new_session = (sess != prev_session)
        prev_session = sess
        if new_session:
            session_start_ms = t_ms
            bars_this_session = 0

        # ---------------- InitializeFirstBar ----------------
        if (not started) or (new_session and reset_eod):
            # LatchConfig: base offsets are constants here (cfg == null path)
            dyn_scale = 1.0
            atr_ema = tick                      # max(|high-low|, tickSize) on a tick == tickSize
            # RefreshDynamicOffsets
            trend_off = base_trend * dyn_scale
            f = atr_m_trend * atr_ema
            if f > trend_off:
                trend_off = f
            rev_off = base_rev * dyn_scale
            f = atr_m_rev * atr_ema
            if f > rev_off:
                rev_off = f
            if trend_off < tick:
                trend_off = tick
            if rev_off < tick:
                rev_off = tick

            brick_basis = bar_open = close
            bar_max = bar_open + trend_off      # NOT rounded (note 11)
            bar_min = bar_open - trend_off

            ha_open0 = close                    # (open + close) * 0.5, and open == close
            ha_close0 = close                   # (o+h+l+c)/4 on a tick
            b_open.append(ha_open0); b_high.append(close); b_low.append(close)
            b_close.append(ha_close0); b_vol.append(vol); b_ts.append(t_ms)
            b_open_ts.append(t_ms); b_open_row.append(row); b_close_row.append(row)
            b_ticks.append(1); b_sess.append(sess); b_dir.append(1)
            b_reason.append(CLOSE_OPEN)
            b_atr.append(atr_ema); b_toff.append(trend_off); b_roff.append(rev_off)
            b_dyn.append(dyn_scale)
            last_touch_row = row

            ha_prev_open, ha_prev_close = ha_open0, ha_close0
            bars_this_session = 1
            last_boundary_touch_ms = last_bar_birth_ms = t_ms
            bar_direction = 1
            same_dir = 1
            pending = False
            pending_dir = 0
            pending_boundary = pending_farthest = 0.0
            pending_start_ms = -1
            pending_vol = 0
            started = True
            continue

        # (a new session that does NOT reset just re-latches; cfg == null => no-op here)

        # ---------------- quiet-hours multipliers for this tick ----------------
        if quiet_on:
            if q_start <= q_end:
                in_quiet = q_start <= hour <= q_end
            else:
                in_quiet = hour >= q_start or hour <= q_end
        else:
            in_quiet = False
        ms_thresh = conf_ms * (q_ms_mult if in_quiet else 1.0)
        ticks_thresh = conf_ticks + (q_ticks_add if in_quiet else 0.0)
        speed_thresh = min_tps * (q_speed_mult if in_quiet else 1.0)

        # ---------------- ShouldForceTimeBrick ----------------
        if last_boundary_touch_ms >= 0 and last_bar_birth_ms >= 0:
            if ((t_ms - last_boundary_touch_ms) / 1000.0 > force_stag
                    and (t_ms - last_bar_birth_ms) / 1000.0 > min_life):
                _force_time_brick(CLOSE_TIME)

        # ---------------- Process ----------------
        if use_conf:
            over_max = (close - bar_max) > eps
            under_min = (close - bar_min) < -eps
            if not over_max and not under_min:
                if pending:
                    pending = False; pending_dir = 0
                    pending_boundary = pending_farthest = 0.0
                    pending_start_ms = -1; pending_vol = 0
                _update_existing_bar()
            else:
                boundary = bar_max if over_max else bar_min
                d = 1 if over_max else -1
                if (not pending) or pending_dir != d:
                    pending = True
                    pending_dir = d
                    pending_boundary = boundary
                    pending_farthest = close
                    pending_start_ms = t_ms
                    pending_vol = vol if vol > 0 else 0
                    _update_existing_bar()
                else:
                    pending_vol += vol if vol > 0 else 0
                    if d > 0:
                        if close > pending_farthest:
                            pending_farthest = close
                    else:
                        if close < pending_farthest:
                            pending_farthest = close
                    _update_existing_bar()

                    # ---- ShouldConfirmBreakout ----
                    el_ms = float(t_ms - pending_start_ms)
                    ok = el_ms >= ms_thresh
                    if ok:
                        pen = pending_farthest - pending_boundary
                        if pen < 0:
                            pen = -pen
                        pen /= tick
                        ok = pen >= ticks_thresh
                    if ok:
                        el_s = el_ms / 1000.0
                        if el_s < 0.001:
                            el_s = 0.001
                        ok = (pen / el_s) >= speed_thresh
                    if ok:
                        gb = pending_farthest - close
                        if gb < 0:
                            gb = -gb
                        gb /= tick
                        ok = (1.0 if pen <= 0 else gb / pen) <= max_giveback
                    if ok and min_vol_win > 0:
                        ok = pending_vol >= min_vol_win

                    if ok:
                        _create_breakout_bar(CLOSE_BREAKOUT)
                        pending = False; pending_dir = 0
                        pending_boundary = pending_farthest = 0.0
                        pending_start_ms = -1; pending_vol = 0
                        # ChainWhileBeyond (FIX #5) - safety cap 50, verbatim
                        for _ in range(50):
                            if not ((close - bar_max) > eps or (close - bar_min) < -eps):
                                break
                            _create_breakout_bar(CLOSE_CHAIN)
                        _update_existing_bar()
                    # else: the `backInside` branch is DEAD CODE (note 10) - reproduced by
                    # doing nothing, which is what it does.
        else:
            for _ in range(50):
                if not ((close - bar_max) > eps or (close - bar_min) < -eps):
                    break
                _create_breakout_bar(CLOSE_CHAIN)
            _update_existing_bar()

        # ---------------- MaybeMicroSplit ----------------
        if micro_on:
            j = len(b_high) - 1
            range_so_far = b_high[j] - b_low[j]
            if range_so_far < 0:
                range_so_far = -range_so_far
            target_range = bar_max - bar_min
            if target_range < 0:
                target_range = -target_range
            if target_range > 0:
                if (range_so_far / target_range >= micro_ratio
                        and (t_ms - last_bar_birth_ms) / 1000.0 > min_life / 2.0):
                    _force_time_brick(CLOSE_MICRO)

    return TBarsSeries(
        open=np.array(b_open, dtype=np.float64),
        high=np.array(b_high, dtype=np.float64),
        low=np.array(b_low, dtype=np.float64),
        close=np.array(b_close, dtype=np.float64),
        volume=np.array(b_vol, dtype=np.int64),
        ts_ms=np.array(b_ts, dtype=np.int64),
        open_ts_ms=np.array(b_open_ts, dtype=np.int64),
        tick_count=np.array(b_ticks, dtype=np.int64),
        close_row=np.array(b_close_row, dtype=np.int64),
        open_row=np.array(b_open_row, dtype=np.int64),
        session_id=np.array(b_sess, dtype=np.int32),
        direction=np.array(b_dir, dtype=np.int8),
        close_reason=b_reason,
        atr=np.array(b_atr, dtype=np.float64),
        trend_offset=np.array(b_toff, dtype=np.float64),
        reversal_offset=np.array(b_roff, dtype=np.float64),
        dyn_scale=np.array(b_dyn, dtype=np.float64),
        params=replace(p),
        tick_size=tick,
        n_ticks=n,
        tape_volume=int(tape_volume),
    )


def build_session(sess: dict, *, tick_size: float,
                  params: TBarsParams | None = None) -> TBarsSeries:
    """`build` over a session dict from `load_tape_session`."""
    rows = sess["trade_rows"]
    if rows.shape[0] == 0:
        raise ValueError("%s: no trade rows on the tape" % sess["path"])
    return build(
        sess["ts_ms"][rows], sess["last"][rows], sess["size"][rows],
        tick_size=tick_size, params=params, row_index=rows,
    )


def from_tape(tape, *, tick_size: float, params: TBarsParams | None = None) -> TBarsSeries:
    """`build` over an `engine.contract.Tape` (trade rows only, sessions respected)."""
    trade = (tape.kind == 1) & np.isfinite(tape.last)
    rows = np.flatnonzero(trade).astype(np.int64)
    if rows.shape[0] == 0:
        raise ValueError("tape has no trade rows: an empty side is ABORT, not PASS")
    return build(
        tape.ts_ms[rows], tape.last[rows], tape.size[rows],
        tick_size=tick_size, params=params, row_index=rows,
        session_id=tape.session_id[rows],
    )


# --------------------------------------------------------------------------- seam
def engine_end_idx(series: TBarsSeries) -> np.ndarray:
    """`end_idx` for `engine.bars.bars_from_end_idx` - `series.close_row`, validated.

    The seam takes a NON-DECREASING array; a repeat means two bricks closed on ONE tape row,
    which SentinelTBars does whenever `ChainWhileBeyond` fires or a micro-split lands on the
    same tick as a breakout. Repeats are handed over AS THEY ARE: collapsing them would
    renumber every later bar and so quietly move the gate's `(session, bar_index)`
    coordinate. Use `duplicate_end_idx` to see how many there are.
    """
    e = series.close_row
    if e.shape[0] == 0:
        raise ValueError("no bars: an empty side is ABORT, not PASS")
    if np.any(np.diff(e) < 0):
        raise ValueError("close_row decreased; bar closes must be non-decreasing")
    return e


def duplicate_end_idx(series: TBarsSeries) -> int:
    """Bricks that closed on a tape row a previous brick had already closed on.

    Surfaced, never hidden: it is the count of bars the engine prices as row-less, and a
    change in it is the tell that the chaining / micro-split path moved.
    """
    e = series.close_row
    return int(np.count_nonzero(np.diff(e) == 0)) if e.shape[0] > 1 else 0


def to_bars(tape, series: TBarsSeries):
    """The engine seam (spec 4): a `Bars` the backtest engine can run on unchanged.

    Interval geometry comes from the seam; the OHLCV goes through the keyword overrides as
    the NATIVE HA/Renko geometry, because a brick LEVEL and a Heikin-Ashi body are not tape
    prices and the engine must not re-derive them from the tape.
    """
    _ensure_azimuth_on_path()
    from engine.bars import bars_from_end_idx

    return bars_from_end_idx(
        tape, engine_end_idx(series),
        open=series.open, high=series.high, low=series.low, close=series.close,
        volume=series.volume, ts_ms=series.ts_ms,
    )


# --------------------------------------------------------------------------- gate
def gate_rows(series: TBarsSeries, *, session: str, instrument: str,
              include_open_bar: bool = True) -> list[dict]:
    """Rows for the `bartype` artefact - pairing key `(session, bar_index)`.

    Field names are the gate spec's own (`gates/artefacts.py`). `bar_index` is the ordinal
    WITHIN the session, so a reference exporter only has to number NT's bars from 0.
    """
    p = series.params or SPEED_12
    tag = p.bartag()
    par = p.params_string()
    out: list[dict] = []
    n = series.n
    idx_in_session: dict[int, int] = {}
    for k in range(n):
        if not include_open_bar and series.close_reason[k] == CLOSE_OPEN:
            continue
        s = int(series.session_id[k])
        bi = idx_in_session.get(s, 0)
        idx_in_session[s] = bi + 1
        out.append({
            "session": session,
            "bar_index": bi,
            "instrument": instrument,
            "bartype": tag,
            "bar_params": par,
            "open": float(series.open[k]),
            "high": float(series.high[k]),
            "low": float(series.low[k]),
            "close": float(series.close[k]),
            "volume": int(series.volume[k]),
            "ts_ms": int(series.ts_ms[k]),
            "open_ts_ms": int(series.open_ts_ms[k]),
            "tick_count": int(series.tick_count[k]),
            "builder": "%s/%s" % (IMPL, IMPL_VER),
        })
    if not out:
        raise ValueError("no gate rows: an empty side is ABORT, not PASS")
    return out


def gate_meta(sess: dict, series: TBarsSeries) -> dict:
    """Identity + provenance metadata for a `Side`.

    identity   `tape_sha256`, `instrument`, `session`, `bar_params` - must be EQUAL on both
               sides or the gate ABORTS (2). This is what stops two different experiments
               from being reported as a parity failure.
    provenance `impl`, `impl_ver` - must be PRESENT, recorded, never compared. A verdict
               that cannot name the two implementations it blessed is not evidence.
    """
    p = series.params or SPEED_12
    sha = sess.get("tape_sha256", "")
    if not sha:
        raise ValueError(
            "no tape_sha256 in the sidecar for %s. 3.1: a tape file without its sidecar is "
            "not admissible to a gate." % sess.get("path", "<unknown>"))
    return {
        "tape_sha256": sha,
        "instrument": sess.get("instrument", ""),
        "session": sess.get("session", ""),
        "bar_params": p.params_string(),
        "impl": IMPL,
        "impl_ver": IMPL_VER,
        "source_of_truth": SOURCE_OF_TRUTH,
        "tick_size": series.tick_size,
        "crossed_rows": sess.get("crossed_rows", 0),
    }


# --------------------------------------------------------------- package contract
# `bars/__init__.py` (sibling-owned) defines the registry: a bar type is a callable
# `build(tape, **params) -> BarSeries`, and a module registers itself AT IMPORT. The two
# functions below are that adapter; nothing above them depends on the package existing.
def _split_params(params: dict) -> tuple[float, TBarsParams, str | None]:
    """`(tick_size, TBarsParams, instrument)`.

    `instrument` is a LABEL the gate driver passes to `build` and deliberately NOT to
    `params_str` - it names the series, it does not change a brick. `tick_size` does change
    every brick, so it belongs in `bar_params`. An unknown name is an error, never ignored:
    a silently dropped parameter is a silently different bar type.
    """
    kw = dict(params)
    instrument = kw.pop("instrument", None)
    if "tick_size" not in kw:
        raise ValueError(
            "tbars needs tick_size: every offset in this bar type is denominated in ticks, "
            "so a wrong tick size is a different bar type, not a rounding difference.")
    tick = float(kw.pop("tick_size"))
    unknown = set(kw) - set(TBarsParams.__dataclass_fields__)
    if unknown:
        raise ValueError("unknown tbars params %s" % sorted(unknown))
    return tick, TBarsParams(**kw), (None if instrument is None else str(instrument))


def series_params_str(**params) -> str:
    """Canonical settings string for the gate's `bar_params` PRECONDITION.

    `tick_size` is IN it: every offset here is denominated in ticks, so two tick sizes are
    two bar types. Both columns run this same function, so a drift ABORTs rather than
    quietly comparing two different experiments.
    """
    tick, p, _ = _split_params(params)
    return p.params_string() + ";tick=%.10g" % tick


def series_bartag(**params) -> str:
    """The bartag NinjaTrader would report, so the gate can FIND the right reference dump
    (`SentinelBarDump` names its file `<stamp>__<inst>__<bartag>.jsonl`)."""
    _, p, _inst = _split_params(params)
    return p.bartag()


def build_series(tape, **params):
    """The package entry point: `build(tape, **params) -> bars.series.BarSeries`."""
    tick, p, inst = _split_params(params)
    return _to_bar_series(
        from_tape(tape, tick_size=tick, params=p), tape, params, instrument=inst)


def _series_module():
    """`bars.series`, whether this module was imported as a package member or standalone."""
    try:
        from . import series as m
        return m
    except ImportError:
        pass
    _ensure_azimuth_on_path()
    here = os.path.dirname(os.path.abspath(__file__))
    if here not in sys.path:
        sys.path.insert(0, here)
    import series as m
    return m


def _to_bar_series(ser: TBarsSeries, tape, params: dict, *, instrument: str | None = None):
    """`TBarsSeries` -> `bars.series.BarSeries` (the package's shared shape).

    `end_idx` is handed over UNCOLLAPSED, per that module's contract ("that index can
    repeat"), so no bar is silently merged and every `bar_index` downstream is the one the
    gate will pair on. `notes["duplicate_end_idx"]` counts the collisions, because
    `to_engine_bars` will refuse such a series and the operator should know why.
    """
    m = _series_module()
    n = ser.n
    # bar_index restarts at each session (the §2 pairing coordinate)
    bar_index = np.zeros(n, dtype=np.int64)
    counts: dict[int, int] = {}
    for k in range(n):
        s = int(ser.session_id[k])
        bar_index[k] = counts.get(s, 0)
        counts[s] = bar_index[k] + 1
    is_partial = np.zeros(n, dtype=bool)
    if n:
        is_partial[-1] = True          # still forming when the tape ended
    dup = int(np.count_nonzero(np.diff(ser.close_row) == 0)) if n > 1 else 0
    tick, p, _ = _split_params(params)
    return m.BarSeries(
        open=ser.open, high=ser.high, low=ser.low, close=ser.close,
        volume=ser.volume, ts_ms=ser.ts_ms, open_ts_ms=ser.open_ts_ms,
        end_idx=ser.close_row, start_idx=ser.open_row, tick_count=ser.tick_count,
        session_id=ser.session_id, bar_index=bar_index, is_partial=is_partial,
        bartype="tbars", bar_params=series_params_str(**params),
        instrument=instrument if instrument is not None else getattr(tape, "instrument", ""),
        notes={
            "n_ticks": ser.n_ticks,
            "tape_volume": ser.tape_volume,
            "bar_volume": int(ser.volume.sum()),
            "duplicate_end_idx": dup,
            "close_reasons": {r: ser.close_reason.count(r)
                              for r in sorted(set(ser.close_reason))},
            "tick_size": tick,
            "timezone": p.timezone,
        },
        tape=tape,
    )


#: True once this module has joined `bars`' registry. False under a standalone
#: `import tbars`, where there is no package to join - recorded rather than swallowed.
REGISTERED = False
REGISTRATION_SKIPPED: str = ""
try:
    from . import register as _register
except ImportError as _exc:                       # standalone import; no registry exists
    REGISTRATION_SKIPPED = "%s: %s" % (type(_exc).__name__, _exc)
else:
    _register(
        "tbars", build_series,
        params_str=series_params_str,
        nt_period_type=BARS_PERIOD_TYPE,
        bartag=series_bartag,
        doc="SentinelTBars - adaptive HA/Renko brick (ATR floor + breakout confirmation "
            "+ per-brick density + 90 s forced bricks). `speed` IS the chart's "
            "\"Speed Settings\"; 12 -> the corpus's 212201v6x24.",
    )
    REGISTERED = True


# ═════════════════════════════════════════════════════════════════════════════
#  THE GATE'S TRUE STATUS - 2026-08-05
# ═════════════════════════════════════════════════════════════════════════════
#  The `bartype` gate is WIRED (see `gate_rows` / `gate_meta`, and
#  `test_tbars.py::test_gate_is_wired_and_can_fail`) and it is **NOT RUN**, because there is
#  no reference side. Per spec 2 this port is therefore **NOT TRUSTED FOR RESEARCH**.
#
#  What was checked, read-only, against the running NinjaTrader:
#    * `nt8bridge` exposes 26 commands (NT8BridgeServer.cs dispatch). NONE returns a chart's
#      BAR SERIES. `chartseries` SETS a chart's instrument/period and returns no bars;
#      `histdump` exports market DEPTH from `.nrd`; `histget` DOWNLOADS `.nrd`. All three
#      are tape-side, not bar-side.
#    * `Sentinel\BrickLog\brick-*.jsonl` IS an NT-side per-brick record written by
#      `SentinelTBars.LogBrick` - 270,135 records, and it carries `o/h/l/c/vol` (5 of the
#      gate's 8 fields) already in the HA/Renko geometry this port reproduces. It is still
#      NOT admissible:
#        - its `ts` is `DateTime.UtcNow` (WALL CLOCK at write time), not the bar's close
#          stamp, so the `ts_ms` gate field has no reference value;
#        - it carries no session marker, so `(session, bar_index)` cannot be formed;
#        - it carries no `tape_sha256`, so the identity PRECONDITION ABORTS;
#        - and no file covers the `GC 02-26` 2025-12-09..2026-01-02 tape anyway (the GC runs
#          are live July 2026; the only Speed-12 evidence in them is `trendT:6, revT:24`,
#          which independently CONFIRMS the 6/24 decoding above).
#
#  ⭐ THE EXPORTER ALREADY EXISTS - and it is the ONE missing step, not a build.
#    A sibling track found `bin\Custom\Indicators\SentinelBarDump_v1_0_0.cs` (schema
#    `bars.1`) INSTALLED AND COMPILED. On a chart it writes every COMPLETED bar to
#    `Sentinel\Harness\bars\<stamp>__<inst>__<bartag>.jsonl`, historical rebuild included:
#        {"i":0,"t":"...Z","o":..,"h":..,"l":..,"c":..,"v":..,"rt":false,"newSession":true}
#    plus a header carrying `bartype`, `tickSize`, `periodValue`, `periodValue2`,
#    `baseValue` and `tradingHours`. That is EVERY gate field including `ts_ms` (`t` is UTC
#    ISO), the session marker `(newSession)` the pairing key needs, and - on a TBars chart -
#    an NT-side confirmation of the 6/24/12 decoding straight out of the header.
#    `bars\ntdump.py` already reads it. Load with it, pass `gate_rows(...)` as the cmp side.
#
#  ⛔ WHAT BLOCKS IT, EXACTLY: `Sentinel\Harness\bars\` holds 11 dumps and ALL ELEVEN are
#    `212207v25` (a different bar type). **There is no `212201v6x24` dump.** Producing one
#    means attaching `SentinelBarDump` to a `SentinelTBars v1.0.0` chart with Speed Settings
#    = 12 and letting it load the sessions - a chart MUTATION on a NinjaTrader that is
#    running and in use, and `chart` / `playbackctl` / `strategy` are off-limits to this
#    agent. It is a two-minute manual step, not a build:
#      1. open a `GC 02-26` chart, Data Series -> bars type `SentinelTBars v1.0.0`,
#         Speed Settings 12, days-to-load covering a session we hold tape for;
#      2. add the `SentinelBarDump` indicator to it;
#      3. confirm `Sentinel\Harness\bars\*__GC__212201v6x24.jsonl` appeared;
#      4. `python -m bars.gate --bartype tbars --instrument "GC 02-26" --session <date>
#         --param speed=12 --param tick_size=0.1`.
#    ⚠ Two things to check on that first run, both of which would show up as a bar-boundary
#    disagreement that is really a settings disagreement: the chart's **"Break at EOD"**
#    (`reset_on_new_trading_day`, which decides whether the state machine restarts each
#    session) and NT's **display time zone** (`timezone`, which decides where quiet hours
#    fall - note 12). Read both off the dump header/`tradingHours` and the platform, do not
#    assume the defaults this module ships.
#
#  Until that dump exists: **the gate is wired and unrun. Do not treat this port as
#  verified.** A green light nobody can trust is worse than "not run, here is why".
# ═════════════════════════════════════════════════════════════════════════════
