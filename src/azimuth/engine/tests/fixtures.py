"""Hand-built, contract-valid tapes. Every price in these is chosen, not random."""
from __future__ import annotations

import numpy as np

from engine.contract import KIND_QUOTE, KIND_TRADE, Tape, validate

BASE_TS = 1_785_000_000_137        # not a round second; nothing here snaps


def mk_tape(bid, ask, *, ts=None, contract="GC 12-26", instrument="GC",
            session_date="2026-07-20", depth=10, session_id=None) -> Tape:
    bid = np.asarray(bid, dtype=np.float64)
    ask = np.asarray(ask, dtype=np.float64)
    n = bid.size
    if ts is None:
        ts = BASE_TS + np.arange(n, dtype=np.int64) * 7
    ts = np.asarray(ts, dtype=np.int64)
    kind = np.full(n, KIND_TRADE, dtype=np.int8)
    last = (bid + ask) * 0.5
    size = np.ones(n, dtype=np.int32)
    meta = {
        "source": "test-fixture", "source_file_sha256": "0" * 64,
        "instrument": instrument, "contract": contract, "session_date": session_date,
        "row_count": int(n), "first_ts_ms": int(ts[0]), "last_ts_ms": int(ts[-1]),
        "gaps": [], "builder_version": "tests.fixtures/1", "built_utc": "1970-01-01T00:00:00Z",
    }
    t = Tape(ts, bid, ask, last, size,
             np.full(n, depth, dtype=np.int32), np.full(n, depth, dtype=np.int32),
             kind,
             np.zeros(n, dtype=np.int32) if session_id is None
             else np.asarray(session_id, dtype=np.int32),
             [meta], instrument)
    validate(t)
    return t


def two_sessions(bid0, ask0, bid1, ask1, *, contract0="GC 12-26",
                 contract1="GC 12-26", gap_ms=8 * 3_600_000) -> Tape:
    """Two sessions concatenated, with an explicit overnight gap between them."""
    a = mk_tape(bid0, ask0, contract=contract0, session_date="2026-07-20")
    n0 = len(a)
    ts1 = a.ts_ms[-1] + gap_ms + np.arange(len(bid1), dtype=np.int64) * 7
    b = mk_tape(bid1, ask1, ts=ts1, contract=contract1, session_date="2026-07-21")
    from engine.contract import concat

    out = concat([a, b])
    assert int(out.session_id[n0]) == 1
    return out


def flat_book(n: int, bid: float = 100.0, spread: float = 0.5):
    return np.full(n, bid), np.full(n, bid + spread)
