"""test_parity — the proofs. `A gate that has never failed is not a gate.` (SPEC §2)

    cd "Sentinel\\Azimuth"
    C:\\ntbv\\Scripts\\python.exe -m pytest gates\\test_parity.py -q
    C:\\ntbv\\Scripts\\python.exe gates\\test_parity.py          # same proofs, no pytest

Every artefact kind in the registry is put through the six §2 failure modes plus eight more that
`gate3.py` learned the hard way. A kind added to `artefacts.py` without a fixture in `inject.py`
FAILS here rather than being skipped -- an ungated port must not be able to arrive quietly.
"""
from __future__ import annotations

import json
import os
import sqlite3
import sys
import tempfile

if __package__ in (None, ""):
    sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    __package__ = "gates"

from .artefacts import get, kinds  # noqa: E402
from .inject import INJECTIONS, fixture, prove, report  # noqa: E402
from .loaders import jsonl_side, rows_side, sqlite_side  # noqa: E402
from .parity import (EXACT, NON_NUMERIC, ArtefactSpec, Field, Side, SpecError,  # noqa: E402
                     run_gate)

KINDS = kinds()


# ---------------------------------------------------------------------------- the §2 six
def _run(kind, name):
    spec = get(kind)
    ref0, cmp0 = fixture(kind)
    built = dict(INJECTIONS)[name](spec, ref0, cmp0)
    assert built is not None, "%s: injection %r does not apply -- it must, for the §2 six" % (kind, name)
    ref, cmp, kw, want, want_exit, _detail = built
    v = run_gate(spec, ref, cmp, **kw)
    assert (v.verdict, v.exit_code) == (want, want_exit), \
        "%s/%s: wanted %s(%d), got %s(%d) -- %s" % (kind, name, want, want_exit, v.verdict,
                                                    v.exit_code, "; ".join(v.reasons))
    return v


def test_identical_passes():
    for k in KINDS:
        v = _run(k, "identical")
        assert v.matched == v.n_ref and v.differing == 0


def test_mutated_field_fails():
    for k in KINDS:
        v = _run(k, "mutated_field")
        assert v.gate_fails, "%s: FAIL with no gate_fails is not a diagnosis" % k


def test_missing_row_fails():
    for k in KINDS:
        v = _run(k, "missing_row")
        assert v.only_ref or v.size_mismatches


def test_extra_row_fails():
    for k in KINDS:
        v = _run(k, "extra_row")
        assert v.only_cmp or v.size_mismatches


def test_identity_skew_aborts():
    for k in KINDS:
        v = _run(k, "identity_skew")
        assert v.reasons and "identity key" in v.reasons[0]


def test_empty_side_aborts():
    for k in KINDS:
        v = _run(k, "empty_side")
        assert "ZERO rows" in v.reasons[0]
        assert v.verdict != "PASS"


# ---------------------------------------------------------------------------- the extras
def test_row_identity_skew_aborts():
    for k in KINDS:
        if dict(INJECTIONS)["row_identity_skew"](get(k), *fixture(k)) is not None:
            _run(k, "row_identity_skew")


def test_provenance_must_be_recorded():
    for k in KINDS:
        _run(k, "provenance_missing")


def test_unkeyable_row_aborts():
    for k in KINDS:
        _run(k, "unkeyable_row")


def test_vacuous_side_aborts():
    for k in KINDS:
        _run(k, "vacuous_side")


def test_noted_difference_never_fails():
    for k in KINDS:
        if dict(INJECTIONS)["noted_only"](get(k), *fixture(k)) is not None:
            v = _run(k, "noted_only")
            assert v.noted, "%s: a NOTED difference must be REPORTED, not just tolerated" % k


def test_tolerance_within_passes_but_degrades():
    for k in KINDS:
        v = _run(k, "tol_within")
        assert v.degraded, "%s: a tolerance that does not stamp DEGRADED is a silent tolerance" % k


def test_tolerance_is_not_a_blindfold():
    for k in KINDS:
        _run(k, "tol_exceeded")


def test_same_key_group_size_fails():
    ran = 0
    for k in KINDS:
        if dict(INJECTIONS)["group_size"](get(k), *fixture(k)) is not None:
            _run(k, "group_size")
            ran += 1
    assert ran, "no artefact fixture contains a same-key group -- the gate3 trap is untested"


def test_every_registered_kind_has_a_fixture():
    """An artefact whose gate has never been proven able to fail is not gated."""
    for k in KINDS:
        fixture(k)


def test_full_proof_matrix():
    results = prove()
    bad = [r for r in results if not r["ok"]]
    assert not bad, report(results)


# ---------------------------------------------------------------------------- structural guards
def test_cannot_pair_on_a_per_run_id():
    for bad_key in ("fireId", "episode_id", "episodeId", "trade_id", "runId"):
        try:
            ArtefactSpec(kind="x", pair_keys=(bad_key,), precondition=(), gate=(Field("v", EXACT),))
        except SpecError as e:
            assert "cross-run" in str(e)
        else:
            raise AssertionError("%r was accepted as a pairing key" % bad_key)


def test_tolerance_must_be_declared():
    try:
        Field("v")  # type: ignore[call-arg]
    except TypeError:
        pass
    else:
        raise AssertionError("a field was allowed to omit its tolerance")
    for bad in ("0.25", None, -1):
        try:
            Field("v", bad)  # type: ignore[arg-type]
        except SpecError:
            pass
        else:
            raise AssertionError("tol=%r was accepted" % bad)


def test_non_numeric_field_carrying_numbers_is_a_spec_defect():
    spec = ArtefactSpec(kind="t", pair_keys=("i",), precondition=(),
                        gate=(Field("label", NON_NUMERIC),), required_fields=("label",))
    ref = rows_side("A", [{"i": 1, "label": 5}], meta={"impl": "a", "impl_ver": "1"})
    cmp = rows_side("B", [{"i": 1, "label": 5}], meta={"impl": "b", "impl_ver": "1"})
    v = run_gate(spec, ref, cmp)
    assert v.exit_code == 2 and "SPEC DEFECT" in v.reasons[0], v.reasons


def test_a_gate_field_cannot_be_a_noted_field():
    try:
        ArtefactSpec(kind="t", pair_keys=("i",), precondition=(),
                     gate=(Field("v", EXACT),), noted=(Field("v", EXACT),))
    except SpecError as e:
        assert "more than one tier" in str(e)
    else:
        raise AssertionError("a field was allowed to be both evidence and noise")


def test_tol_override_on_an_unknown_field_aborts():
    """A typo'd override that silently did nothing would leave the operator believing the gate
    was loosened where it was not -- or, worse, tight where it was not."""
    spec = get("council")
    ref, cmp = fixture("council")
    v = run_gate(spec, ref, cmp, tol_overrides={"netscore": 1.0})
    assert v.exit_code == 2 and "not a gate field" in v.reasons[0]


def test_identity_check_can_be_waived_but_degrades():
    spec = get("bartype")
    ref, cmp = fixture("bartype")
    cmp.meta["tape_sha256"] = "deadbeef"
    v = run_gate(spec, ref, cmp, check_identity=False)
    assert v.exit_code == 0 and v.verdict == "PASS (DEGRADED)" and v.degraded


def test_both_sides_labelled_the_same_aborts():
    spec = get("sensor")
    ref, cmp = fixture("sensor")
    cmp.label = ref.label
    v = run_gate(spec, ref, cmp)
    assert v.exit_code == 2


def test_nan_on_some_rows_passes_and_is_counted():
    """Two sides both undefined on a warmup bar genuinely agree -- but the count must be visible."""
    spec = ArtefactSpec(kind="t2", pair_keys=("i",), precondition=(),
                        gate=(Field("v", EXACT),), required_fields=("v",))
    rows = [{"i": 1, "v": float("nan")}, {"i": 2, "v": 2.0}, {"i": 3, "v": 3.0},
            {"i": 4, "v": 4.0}]
    ref = rows_side("A", rows, meta={"impl": "a", "impl_ver": "1"})
    cmp = rows_side("B", rows, meta={"impl": "b", "impl_ver": "1"})
    v = run_gate(spec, ref, cmp)
    assert v.exit_code == 0, v.reasons
    assert v.nan_fields["v"] == {"nan_pairs": 1, "compared": 4, "pct": 25.0, "gated": True}
    assert "25.0%" in v.to_text() and "NaN on BOTH sides" in v.to_text()


def test_a_field_that_is_nan_on_both_sides_everywhere_aborts():
    """The vacuity guard in its second hat: a field NaN throughout compared NOTHING, and a check
    that can only pass one way is not a check."""
    spec = ArtefactSpec(kind="t3", pair_keys=("i",), precondition=(),
                        gate=(Field("v", EXACT),), required_fields=("v",))
    rows = [{"i": i, "v": float("nan")} for i in range(4)]
    ref = rows_side("A", rows, meta={"impl": "a", "impl_ver": "1"})
    cmp = rows_side("B", rows, meta={"impl": "b", "impl_ver": "1"})
    v = run_gate(spec, ref, cmp)
    assert v.exit_code == 2 and v.verdict == "ABORT", v.verdict
    assert "100%" in v.reasons[0] and "v 4/4" in v.reasons[0], v.reasons
    assert "tested NOTHING" in v.to_text()


def test_nan_vacuity_applies_only_to_gate_fields():
    """A NOTED field that is NaN throughout is noise, not evidence, and must not abort."""
    spec = ArtefactSpec(kind="t4", pair_keys=("i",), precondition=(),
                        gate=(Field("v", EXACT),), noted=(Field("n", EXACT),),
                        required_fields=("v",))
    rows = [{"i": i, "v": float(i), "n": float("nan")} for i in range(3)]
    ref = rows_side("A", rows, meta={"impl": "a", "impl_ver": "1"})
    cmp = rows_side("B", rows, meta={"impl": "b", "impl_ver": "1"})
    v = run_gate(spec, ref, cmp)
    assert v.exit_code == 0 and v.nan_fields["n"]["gated"] is False


def test_all_nan_field_aborts_for_every_artefact():
    for k in KINDS:
        v = _run(k, "nan_all_field")
        assert "100%" in v.reasons[0]


def test_partial_nan_passes_and_surfaces_the_count_for_every_artefact():
    for k in KINDS:
        v = _run(k, "nan_partial_field")
        assert v.nan_fields, "%s: a NaN pair must be COUNTED, not silently tolerated" % k
        f, d = next(iter(v.nan_fields.items()))
        assert 0 < d["pct"] < 100 and d["nan_pairs"] == 1 and d["gated"] is True, (k, f, d)


def test_alias_makes_a_python_column_comparable():
    spec = get("council")
    ref, _ = fixture("council")
    py_rows = [{("net_score" if k == "netScore" else k): v for k, v in r.items()}
               for r in ref.rows]
    cmp = Side("Azimuth", py_rows, meta=dict(ref.meta, impl="py", impl_ver="0.1.0"),
               alias={"net_score": "netScore"})
    v = run_gate(spec, ref, cmp)
    assert v.exit_code == 0, v.to_text()


def test_alias_collision_is_refused():
    try:
        Side("X", [{"netScore": 1, "net_score": 2}], alias={"net_score": "netScore"})
    except SpecError as e:
        assert "collides" in str(e)
    else:
        raise AssertionError("an alias silently overwrote a real field")


# ---------------------------------------------------------------------------- loaders
def test_jsonl_loader_counts_unreadable_rows():
    with tempfile.TemporaryDirectory() as td:
        p = os.path.join(td, "a.jsonl")
        with open(p, "w", encoding="utf-8") as fh:
            fh.write(json.dumps({"i": 1, "v": 1.0}) + "\n")
            fh.write("{not json\n")
            fh.write(json.dumps({"i": 2, "v": 2.0}) + "\n")
        side = jsonl_side("A", p)
        assert len(side.rows) == 2
        assert len(side.unreadable) == 1, "a broken line must be NAMED, not silently dropped"


def test_jsonl_first_line_mode_is_the_corpus_convention():
    with tempfile.TemporaryDirectory() as td:
        p = os.path.join(td, "fire.jsonl")
        with open(p, "w", encoding="utf-8") as fh:
            fh.write(json.dumps({"kind": "excursion", "fireTime": "t"}) + "\n")
            for i in range(5):
                fh.write(json.dumps({"px": i}) + "\n")     # the tick path
        side = jsonl_side("A", p, record="first-line")
        assert len(side.rows) == 1 and side.rows[0]["fireTime"] == "t"


def test_sqlite_loader_is_read_only():
    with tempfile.TemporaryDirectory() as td:
        db = os.path.join(td, "t.db")
        con = sqlite3.connect(db)
        con.execute("CREATE TABLE t (i INTEGER, v REAL)")
        con.execute("INSERT INTO t VALUES (1, 1.5)")
        con.commit()
        con.close()
        side = sqlite_side("A", db, "SELECT i, v FROM t")
        assert side.rows == [{"i": 1, "v": 1.5}]
        try:
            sqlite_side("A", db, "INSERT INTO t VALUES (2, 2.5)")
        except sqlite3.OperationalError as e:
            assert "readonly" in str(e).lower()
        else:
            raise AssertionError("the read-only connection accepted a write")


# ---------------------------------------------------------------------------- no-pytest runner
def _self_main() -> int:
    fns = [(n, f) for n, f in sorted(globals().items())
           if n.startswith("test_") and callable(f)]
    failed = []
    for name, fn in fns:
        try:
            fn()
            print("  ok    %s" % name)
        except AssertionError as e:
            failed.append((name, e))
            print("  FAIL  %s\n        %s" % (name, str(e).splitlines()[0][:150]))
        except Exception as e:            # never silent: an ERROR is a failure with a name
            failed.append((name, e))
            print("  ERROR %s\n        %s: %s" % (name, type(e).__name__, e))
    print()
    print(report(prove()))
    print()
    print("%d test(s), %d failed" % (len(fns), len(failed)))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(_self_main())
