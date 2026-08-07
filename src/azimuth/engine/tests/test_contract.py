"""§3.1 tape contract: the validator must REFUSE, not coerce."""
from __future__ import annotations

import json
import os

import numpy as np
import pytest

from engine.contract import (Tape, TapeContractError, load_session, synth_session,
                             synth_tape, validate, validate_sidecar, write_session)
from fixtures import BASE_TS, flat_book, mk_tape


def test_synth_tape_is_contract_valid():
    t = synth_tape(3, rows_per_session=2_000, seed=7)
    validate(t)
    assert len(t) == 6_000
    assert t.sessions[0]["source"] == "synthetic"
    assert np.all(np.diff(t.ts_ms) >= 0)


def test_rejects_non_monotonic_timestamps():
    b, a = flat_book(5)
    ts = BASE_TS + np.array([0, 7, 14, 9, 21], dtype=np.int64)
    with pytest.raises(TapeContractError, match="monotonic"):
        mk_tape(b, a, ts=ts)


def test_rejects_crossed_book():
    b, a = flat_book(5)
    a[2] = b[2] - 0.1
    with pytest.raises(TapeContractError, match="crossed book"):
        mk_tape(b, a)


def test_rejects_bar_snapped_timestamps():
    """"ms timestamps that never snap to a bar boundary" is enforced, not hoped."""
    b, a = flat_book(100)
    ts = 1_785_000_000_000 + np.arange(100, dtype=np.int64) * 1000
    with pytest.raises(TapeContractError, match="bar-snapped"):
        mk_tape(b, a, ts=ts)


def test_rejects_empty_tape():
    with pytest.raises(TapeContractError, match="ABORT, not PASS"):
        validate(Tape(*(np.zeros(0, dtype=d) for d in
                        (np.int64, np.float64, np.float64, np.float64,
                         np.int32, np.int32, np.int32, np.int8, np.int32)), [], "GC"))


def test_sidecar_fields_are_mandatory():
    with pytest.raises(TapeContractError, match="missing"):
        validate_sidecar({"source": "x"})


def test_parquet_round_trip_and_sidecar_requirement(tmp_path):
    s = synth_session(session_date="2026-07-20", rows=500, seed=1)
    p = str(tmp_path / "20260720.parquet")
    write_session(s, p)
    back = load_session(p)
    assert np.array_equal(back.ts_ms, s.ts_ms)
    assert np.array_equal(back.bid, s.bid)
    assert np.array_equal(back.ask, s.ask)

    os.remove(str(tmp_path / "20260720.meta.json"))
    with pytest.raises(TapeContractError, match="provenance sidecar"):
        load_session(p)
    load_session(p, require_sidecar=False)      # explicit opt-out, fixtures only


def test_quote_rows_must_have_zero_size():
    from engine.contract import KIND_QUOTE

    b, a = flat_book(5)
    t = mk_tape(b, a)
    t.kind = np.full(5, KIND_QUOTE, dtype=np.int8)
    with pytest.raises(TapeContractError, match="size must be 0"):
        validate(t)
