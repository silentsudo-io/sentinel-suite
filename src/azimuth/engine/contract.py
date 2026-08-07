"""The §3.1 tape contract -- loader, validator, and contract-valid fixture synthesis.

    tape/<instrument>/<session_date>.parquet      session_date = ET trading day
    tape/<instrument>/<session_date>.meta.json    provenance sidecar

    ts_ms     int64    unix ms UTC, monotonic non-decreasing, NEVER bar-snapped
    bid       float64  best bid
    ask       float64  best ask
    last      float64  trade price, null on a quote-only row
    size      int32    trade size, 0 on a quote-only row
    bid_size  int32    nullable
    ask_size  int32    nullable
    kind      int8     0 = quote, 1 = trade

The tape files are produced by a sibling track. This module owns only the
CONSUMER side of the contract: it validates hard (a tape that violates the
contract is refused, not silently coerced) and it can synthesise a
contract-valid tape so the engine can be tested and benchmarked before supply
lands.

⚠ Provenance is not optional (§3.1). `load_session` refuses a tape with no
sidecar unless `require_sidecar=False` is passed EXPLICITLY -- that argument
exists for synthetic fixtures, not for real data.
"""
from __future__ import annotations

import hashlib
import json
import os
from dataclasses import dataclass, field

import numpy as np

TAPE_COLUMNS: dict[str, str] = {
    "ts_ms": "int64",
    "bid": "float64",
    "ask": "float64",
    "last": "float64",
    "size": "int32",
    "bid_size": "int32",
    "ask_size": "int32",
    "kind": "int8",
}

KIND_QUOTE = 0
KIND_TRADE = 1

SIDECAR_FIELDS = (
    "source", "source_file_sha256", "instrument", "contract", "row_count",
    "first_ts_ms", "last_ts_ms", "gaps", "builder_version", "built_utc",
)


class TapeContractError(Exception):
    """The tape does not satisfy §3.1. Never downgraded to a warning."""


@dataclass
class Tape:
    """One or more contract-valid sessions, concatenated in time order."""

    ts_ms: np.ndarray
    bid: np.ndarray
    ask: np.ndarray
    last: np.ndarray
    size: np.ndarray
    bid_size: np.ndarray
    ask_size: np.ndarray
    kind: np.ndarray
    #: per-row session ordinal (0-based, in load order)
    session_id: np.ndarray
    #: per-session metadata, index == session_id
    sessions: list[dict] = field(default_factory=list)
    instrument: str = ""

    # -- derived, computed once ------------------------------------------
    _mid: np.ndarray | None = field(default=None, repr=False)

    def __len__(self) -> int:
        return int(self.ts_ms.shape[0])

    @property
    def mid(self) -> np.ndarray:
        if self._mid is None:
            self._mid = (self.bid + self.ask) * 0.5
        return self._mid

    @property
    def contracts(self) -> list[str]:
        return [str(s.get("contract", "")) for s in self.sessions]

    def slice(self, lo: int, hi: int) -> "Tape":
        """Rows [lo, hi) as a new Tape (sidecars carried by reference)."""
        s = slice(lo, hi)
        return Tape(
            self.ts_ms[s], self.bid[s], self.ask[s], self.last[s], self.size[s],
            self.bid_size[s], self.ask_size[s], self.kind[s], self.session_id[s],
            self.sessions, self.instrument,
        )


# ---------------------------------------------------------------- validation
def validate(tape: Tape, *, max_bar_snap_frac: float = 0.05) -> None:
    """Raise TapeContractError on any §3.1 violation.

    `max_bar_snap_frac` enforces the contract's "never bar-snapped" clause
    operationally: if more than this fraction of rows land exactly on a whole
    second, the timestamps have been rounded somewhere upstream and every
    ms-resolution conclusion drawn from them is fiction.
    """
    n = len(tape)
    if n == 0:
        raise TapeContractError("empty tape: an empty side is ABORT, not PASS")

    for name, dt in (("ts_ms", np.int64), ("bid", np.float64), ("ask", np.float64),
                     ("last", np.float64)):
        arr = getattr(tape, name)
        if arr.shape != (n,):
            raise TapeContractError(f"{name}: shape {arr.shape} != ({n},)")
        if arr.dtype != dt:
            raise TapeContractError(f"{name}: dtype {arr.dtype} != {np.dtype(dt)}")

    if np.any(np.diff(tape.ts_ms) < 0):
        bad = int(np.flatnonzero(np.diff(tape.ts_ms) < 0)[0])
        raise TapeContractError(
            f"ts_ms not monotonic non-decreasing at row {bad + 1} "
            f"({tape.ts_ms[bad]} -> {tape.ts_ms[bad + 1]})"
        )
    if not np.isfinite(tape.bid).all() or not np.isfinite(tape.ask).all():
        raise TapeContractError("bid/ask contain NaN or inf; the book is never absent on the tape")
    crossed = np.flatnonzero(tape.ask < tape.bid)
    if crossed.size:
        raise TapeContractError(
            f"crossed book on {crossed.size} row(s), first at {int(crossed[0])}: "
            f"bid={tape.bid[crossed[0]]} ask={tape.ask[crossed[0]]}"
        )
    if not np.isin(tape.kind, (KIND_QUOTE, KIND_TRADE)).all():
        raise TapeContractError("kind must be 0 (quote) or 1 (trade)")
    quote_rows = tape.kind == KIND_QUOTE
    if np.any(tape.size[quote_rows] != 0):
        raise TapeContractError("size must be 0 on quote-only rows")

    snapped = int(np.count_nonzero(tape.ts_ms % 1000 == 0))
    if snapped / n > max_bar_snap_frac:
        raise TapeContractError(
            f"{100 * snapped / n:.1f}% of ts_ms land exactly on a whole second -- the tape "
            f"is bar-snapped and cannot support tick-level slippage (§4.3)"
        )


def validate_sidecar(meta: dict, path: str = "") -> None:
    missing = [f for f in SIDECAR_FIELDS if f not in meta]
    if missing:
        raise TapeContractError(
            f"sidecar {path or '<inline>'} missing {missing}; a tape file without a "
            f"complete sidecar is not admissible to a gate (§3.1)"
        )


# ---------------------------------------------------------------- loading
def _read_parquet(path: str) -> dict[str, np.ndarray]:
    import pyarrow.parquet as pq

    tbl = pq.read_table(path)
    have = set(tbl.column_names)
    missing = [c for c in TAPE_COLUMNS if c not in have]
    if missing:
        raise TapeContractError(f"{path}: missing contract columns {missing}")
    out: dict[str, np.ndarray] = {}
    for col, dt in TAPE_COLUMNS.items():
        a = tbl.column(col).to_numpy(zero_copy_only=False)
        if dt.startswith("int") and a.dtype.kind == "f":
            # nullable int arrived as float with NaN for null; contract says
            # bid_size/ask_size are nullable -> 0 means "unknown", never guessed.
            a = np.nan_to_num(a, nan=0.0)
        out[col] = np.ascontiguousarray(a, dtype=dt)
    return out


def load_session(path: str, *, require_sidecar: bool = True) -> Tape:
    """Load one `<session_date>.parquet` plus its sidecar."""
    cols = _read_parquet(path)
    meta_path = path.rsplit(".parquet", 1)[0] + ".meta.json"
    if os.path.exists(meta_path):
        with open(meta_path, encoding="utf-8") as fh:
            meta = json.load(fh)
        validate_sidecar(meta, meta_path)
    elif require_sidecar:
        raise TapeContractError(
            f"no provenance sidecar next to {path}. `replay.csv` is 61 GB of data nobody "
            f"can now say the origin of -- pass require_sidecar=False only for fixtures."
        )
    else:
        meta = {"instrument": "", "contract": "", "source": "unknown"}

    n = cols["ts_ms"].shape[0]
    t = Tape(
        cols["ts_ms"], cols["bid"], cols["ask"], cols["last"], cols["size"],
        cols["bid_size"], cols["ask_size"], cols["kind"],
        np.zeros(n, dtype=np.int32), [meta], str(meta.get("instrument", "")),
    )
    validate(t)
    return t


def load_sessions(paths: list[str], *, require_sidecar: bool = True) -> Tape:
    """Concatenate sessions in the order given. Refuses out-of-order sessions."""
    if not paths:
        raise TapeContractError("no session files given: an empty side is ABORT, not PASS")
    parts = [load_session(p, require_sidecar=require_sidecar) for p in paths]
    for a, b in zip(parts, parts[1:]):
        if a.ts_ms[-1] > b.ts_ms[0]:
            raise TapeContractError("sessions overlap or are out of order")
    return concat(parts)


def concat(parts: list[Tape]) -> Tape:
    sess = np.concatenate(
        [np.full(len(p), i, dtype=np.int32) for i, p in enumerate(parts)]
    )
    out = Tape(
        np.concatenate([p.ts_ms for p in parts]),
        np.concatenate([p.bid for p in parts]),
        np.concatenate([p.ask for p in parts]),
        np.concatenate([p.last for p in parts]),
        np.concatenate([p.size for p in parts]),
        np.concatenate([p.bid_size for p in parts]),
        np.concatenate([p.ask_size for p in parts]),
        np.concatenate([p.kind for p in parts]),
        sess,
        [p.sessions[0] if p.sessions else {} for p in parts],
        parts[0].instrument,
    )
    validate(out)
    return out


def discover(tape_root: str, instrument: str) -> list[str]:
    """Every `<session_date>.parquet` for an instrument, in session order."""
    d = os.path.join(tape_root, instrument)
    if not os.path.isdir(d):
        raise TapeContractError(f"no tape directory {d}")
    return sorted(
        os.path.join(d, f) for f in os.listdir(d) if f.endswith(".parquet")
    )


# ---------------------------------------------------------------- fixtures
def synth_session(
    *,
    session_date: str,
    instrument: str = "GC",
    contract: str = "GC 12-26",
    start_ts_ms: int | None = None,
    rows: int = 90_000,
    start_px: float = 4155.2,
    tick_size: float = 0.1,
    spread_ticks: int = 1,
    trade_frac: float = 0.35,
    vol_ticks: float = 0.45,
    mean_gap_ms: int = 120,
    seed: int = 0,
) -> Tape:
    """Synthesise ONE contract-valid session.

    ⚠ This is a FIXTURE, not data. It exists so the engine can be tested and
    benchmarked before the tape supply track lands; its sidecar says
    `source="synthetic"` so it can never be mistaken for a real tape or admitted
    to a parity gate. Timestamps are deliberately irregular (Poisson inter-arrival,
    1..250 ms) so nothing snaps to a bar boundary.
    """
    rng = np.random.default_rng(seed)
    if start_ts_ms is None:
        # 18:00 ET the prior evening, expressed crudely as 22:00 UTC on the date.
        y, m, d = (int(x) for x in session_date.split("-"))
        import datetime as _dt

        start_ts_ms = int(
            _dt.datetime(y, m, d, 22, 0, 0, 137, tzinfo=_dt.timezone.utc).timestamp() * 1000
        )

    gaps_ms = 1 + rng.poisson(mean_gap_ms, size=rows).astype(np.int64)
    ts = start_ts_ms + np.cumsum(gaps_ms)

    steps = rng.normal(0.0, vol_ticks, size=rows)
    steps[0] = 0.0
    mid_ticks = np.round(np.cumsum(steps))
    mid = start_px + mid_ticks * tick_size

    half = 0.5 * spread_ticks * tick_size
    # spread widens occasionally -- 1 or 2 ticks, so mid != bid != ask always.
    wide = rng.random(rows) < 0.12
    half_arr = np.where(wide, half + 0.5 * tick_size, half)
    bid = np.round((mid - half_arr) / tick_size) * tick_size
    ask = np.round((mid + half_arr) / tick_size) * tick_size
    ask = np.maximum(ask, bid + tick_size)

    kind = (rng.random(rows) < trade_frac).astype(np.int8)
    last = np.where(kind == KIND_TRADE, np.where(rng.random(rows) < 0.5, bid, ask), np.nan)
    size = np.where(kind == KIND_TRADE, 1 + rng.poisson(2.0, size=rows), 0).astype(np.int32)
    bid_size = (1 + rng.poisson(14.0, size=rows)).astype(np.int32)
    ask_size = (1 + rng.poisson(14.0, size=rows)).astype(np.int32)

    meta = {
        "source": "synthetic",
        "source_file_sha256": hashlib.sha256(
            f"{instrument}|{session_date}|{seed}|{rows}".encode()
        ).hexdigest(),
        "instrument": instrument,
        "contract": contract,
        "session_date": session_date,
        "row_count": int(rows),
        "first_ts_ms": int(ts[0]),
        "last_ts_ms": int(ts[-1]),
        "gaps": [],
        "builder_version": "engine.contract.synth_session/1",
        "built_utc": "1970-01-01T00:00:00Z",
    }
    t = Tape(
        ts.astype(np.int64), bid.astype(np.float64), ask.astype(np.float64),
        last.astype(np.float64), size, bid_size, ask_size, kind,
        np.zeros(rows, dtype=np.int32), [meta], instrument,
    )
    validate(t)
    return t


def synth_tape(
    n_sessions: int = 5, *, rows_per_session: int = 90_000, seed: int = 0, **kw
) -> Tape:
    """`n_sessions` contract-valid synthetic sessions, one ET trading day apart."""
    parts = []
    for i in range(n_sessions):
        parts.append(
            synth_session(
                session_date=f"2026-07-{20 + i:02d}",
                seed=seed + i,
                rows=rows_per_session,
                start_px=kw.pop("start_px", 4155.2) if i == 0 else float(parts[-1].mid[-1]),
                **kw,
            )
        )
    return concat(parts)


def write_session(tape: Tape, path: str) -> None:
    """Write a Tape (single session) as contract parquet + sidecar."""
    import pyarrow as pa
    import pyarrow.parquet as pq

    os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
    tbl = pa.table({c: getattr(tape, c) for c in TAPE_COLUMNS})
    pq.write_table(tbl, path)
    meta_path = path.rsplit(".parquet", 1)[0] + ".meta.json"
    meta = dict(tape.sessions[0])
    validate_sidecar(meta, meta_path)
    with open(meta_path, "w", encoding="utf-8") as fh:
        json.dump(meta, fh, indent=2)
