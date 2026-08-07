"""inject — the proof that each artefact's gate CAN fail.

    "A gate that has never failed is not a gate." -- SENTINEL_AZIMUTH_SPEC §2

`gate3.py` earned its authority by being proven four ways before anyone trusted a PASS out of it.
This module is the same discipline, applied to every artefact kind in the registry: for each kind
it builds a pair of identical sides, then damages one of them in a named, minimal way and asserts
the verdict the law requires.

    identical              -> PASS   (0)   the gate does not cry wolf
    mutated_field          -> FAIL   (1)   one gate field, by 1e-9. EXACT means EXACT.
    missing_row            -> FAIL   (1)   a record the port never produced
    extra_row              -> FAIL   (1)   a record the port invented
    identity_skew          -> ABORT  (2)   different tape / instrument / model = different experiment
    empty_side             -> ABORT  (2)   never a PASS, whatever the other side holds
    row_identity_skew      -> ABORT  (2)   the per-row version of the same thing
    provenance_missing     -> ABORT  (2)   a verdict that cannot name what it blessed
    unkeyable_row          -> ABORT  (2)   a row that cannot be paired must not vanish
    vacuous_side           -> ABORT  (2)   pairing keys and nothing else is not a PASS
    noted_only             -> PASS   (0)   per-run ids differ; that is expected, not drift
    tol_within             -> PASS(DEGRADED)  an override is allowed and is STAMPED
    tol_exceeded           -> FAIL   (1)   an override does not become a blindfold
    group_size             -> FAIL   (1)   a same-key group whose member count differs
    nan_all_field          -> ABORT  (2)   NaN==NaN on 100% of rows tested nothing
    nan_partial_field      -> PASS   (0)   some rows undefined is legitimate -- and is COUNTED
    forbidden_pair_key     -> SpecError     `fireId` can never become a key by accident

Run it:  python -m gates selftest        (or `pytest test_parity.py`, which calls straight in)
"""
from __future__ import annotations

import copy
import json

from .artefacts import get, kinds as all_kinds
from .parity import EXACT, NON_NUMERIC, ArtefactSpec, Field, SpecError, Side, run_gate
from .loaders import rows_side

__all__ = ["fixture", "INJECTIONS", "prove", "report"]

_SHA = "9f" * 32


# ---------------------------------------------------------------------------- fixtures
def _bartype_rows():
    base = dict(instrument="GC", bartype="SentinelFlux", bar_params="Flux(6,24)",
                session="2026-07-31")
    out = []
    for i in range(4):
        px = 2410.0 + i * 0.5
        out.append(dict(base, bar_index=i, open=px, high=px + 1.2, low=px - 0.8,
                        close=px + 0.4, volume=1200 + i * 13, ts_ms=1753920000000 + i * 60000,
                        open_ts_ms=1753919940000 + i * 60000, tick_count=340 + i,
                        bar_id="GC-2026-07-31-%d" % i, builder="flux-0.1.0"))
    return out


def _sensor_rows():
    out = []
    for i in range(4):
        out.append(dict(scope="GC.69697v6x24", bar_ts=1753920000000 + i * 60000,
                        sensor="SentinelTrend", sensor_params="len=14,mult=2.5",
                        bar_label="Flux 6x24", value=0.4213 + i * 0.01, vote=1 if i % 2 else -1,
                        state="TREND_UP" if i % 2 else "CHOP", dir=1 if i % 2 else -1,
                        stale=False, updated_utc="2026-08-04T12:00:%02dZ" % i, seq=i))
    return out


def _council_rows():
    out = []
    for i in range(4):
        out.append(dict(scope="GC.69697v6x24", bar_ts=1753920000000 + i * 60000,
                        model_id="Model.conf@none", roster="roster-v3", bar_label="Flux 6x24",
                        netScore=0.315 + i * 0.02, activeW=4.6, conviction=0.1541 + i * 0.001,
                        veto=(i == 2), vetoReason="CHOP" if i == 2 else "",
                        damp=(i == 3), dampMult=0.5 if i == 3 else 1.0,
                        signal="LONG" if i == 1 else "NONE", sizeMult=1.0,
                        agree=5, disagree=2, voters=7,
                        votes_json=json.dumps({"trend": 1, "adx": 0}),
                        updated_utc="2026-08-04T12:00:%02dZ" % i))
    return out


def _strategy_rows():
    """Includes the gate3 trap: TWO fires sharing one `fireTime`, ordered only by `fireId`."""
    base = dict(inst="GC", bartype="SentinelFlux", scope="GC.69697v6x24")
    rows = []
    # Rows 1 and 2 share fireTime AND dir AND signal -- the whole pairing key. That is the corpus
    # case gate3 records (`..._GC_S_2` and `..._GC_S_3` at one stamp) and the reason a same-key
    # group must be size-checked rather than merely matched member by member.
    stamps = ["2026-07-31T14:31:00Z", "2026-07-31T15:02:00Z", "2026-07-31T15:02:00Z"]
    for i, ft in enumerate(stamps):
        rows.append(dict(base, fireTime=ft, dir="L", signal="COUNCIL",
                         fireId="20260731_GC_L_%d" % (i + 1),
                         entryTime=ft, entryPx=2410.5 + i, exitTime="2026-07-31T15:%02d:00Z" % (40 + i),
                         exitPx=2413.5 + i, endReason="Target" if i != 1 else "Stop",
                         stopPx=2407.5 + i, stopTicks=30, targetPx=2413.5 + i, targetTicks=30,
                         qty=1, pnlTicks=30 if i != 1 else -30,
                         tradeId="t%d" % i, episodeId="ep%d" % i, ticks=812 + i, trunc=0))
    return rows


def _corpus_rows():
    """A strategy_corpus pair, generated from gate3's own field lists so the fixture cannot
    drift away from the spec it is proving."""
    spec = get("strategy_corpus")
    rows = []
    for i in range(3):
        # rows 1 and 2 share the whole pairing key, as the real corpus does
        r = {"fireTime": "2026-07-31T14:%02d:00Z" % (31 + min(i, 1)), "dir": "L", "signal": "COUNCIL",
             "fireId": "20260731_GC_L_%d" % (i + 1), "kind": "excursion"}
        for f in spec.precondition:
            r[f.name] = "%s-v1" % f.name
        for n, f in enumerate(spec.gate):
            r[f.name] = round(100.0 + i * 3 + n * 0.25, 4)
        r["endReason"] = "Target"
        for f in spec.noted:
            r[f.name] = "%s-%d" % (f.name, i) if f.tol == NON_NUMERIC else i
        rows.append(r)
    return rows


_FIXTURES = {
    "bartype": (_bartype_rows,
                dict(tape_sha256=_SHA, instrument="GC", session="2026-07-31",
                     bar_params="Flux(6,24)")),
    "sensor": (_sensor_rows,
               dict(tape_sha256=_SHA, scope="GC.69697v6x24", sensor="SentinelTrend",
                    sensor_params="len=14,mult=2.5")),
    "council": (_council_rows,
                dict(tape_sha256=_SHA, scope="GC.69697v6x24", model_id="Model.conf@none")),
    "strategy": (_strategy_rows,
                 dict(tape_sha256=_SHA, instrument="GC", strategy="SentinelKeel",
                      strategy_params="14,AtrBracket,1")),
    "strategy_corpus": (_corpus_rows, dict(cell="G3")),
}


def fixture(kind: str):
    """(ref Side, cmp Side) -- two columns that genuinely agree, for `kind`.

    The two sides carry DIFFERENT `impl` / `impl_ver` on purpose. That is the normal case for the
    Azimuth: NinjaScript and Python are different implementations by construction, and a gate
    that demanded equal version strings would abort on every real comparison it was built for.
    """
    if kind not in _FIXTURES:
        raise KeyError("no fixture for artefact kind %r -- add one in inject.py when you add the "
                       "spec, or its gate has never been proven able to fail" % kind)
    make, ident = _FIXTURES[kind]
    rows = make()
    ref = rows_side("NT", copy.deepcopy(rows),
                    meta=dict(ident, impl="NinjaScript", impl_ver="Core v1.41.0"))
    cmp = rows_side("Azimuth", copy.deepcopy(rows),
                    meta=dict(ident, impl="Azimuth/python", impl_ver="0.1.0"))
    return ref, cmp


# ---------------------------------------------------------------------------- mutation helpers
def _numeric_exact_gate_field(spec: ArtefactSpec, rows) -> str:
    for f in spec.gate:
        if f.tol != EXACT:
            continue
        for r in rows:
            v = r.get(f.name)
            if isinstance(v, (int, float)) and not isinstance(v, bool):
                return f.name
    raise SpecError("%s: no numeric EXACT gate field in the fixture -- nothing to mutate, so this "
                    "gate cannot be proven able to fail" % spec.kind)


def _noted_field(spec: ArtefactSpec, rows) -> str | None:
    for f in spec.noted:
        for r in rows:
            if f.name in r:
                return f.name
    return None


def _bump(v, delta=1e-9):
    return v + (1 if isinstance(v, int) and not isinstance(v, bool) else delta)


def _side_like(side: Side, rows=None, meta=None) -> Side:
    return Side(label=side.label, rows=rows if rows is not None else copy.deepcopy(side.rows),
                meta=dict(meta if meta is not None else side.meta), origin=side.origin)


# ---------------------------------------------------------------------------- the injections
# Each returns (ref, cmp, kwargs, expected_verdict_prefix, expected_exit, note) or None when the
# injection does not apply to this kind (and says why -- a silent skip in a proof is worthless).
def _inj_identical(spec, ref, cmp):
    return ref, cmp, {}, "PASS", 0, "the two columns agree"


def _inj_mutated_field(spec, ref, cmp):
    f = _numeric_exact_gate_field(spec, cmp.rows)
    rows = copy.deepcopy(cmp.rows)
    i = next(i for i, r in enumerate(rows) if isinstance(r.get(f), (int, float))
             and not isinstance(r.get(f), bool))
    old = rows[i][f]
    rows[i][f] = _bump(old)
    return ref, _side_like(cmp, rows), {}, "FAIL", 1, "%s %r -> %r on row %d" % (f, old, rows[i][f], i)


def _inj_missing_row(spec, ref, cmp):
    rows = copy.deepcopy(cmp.rows)
    dropped = rows.pop(0)
    return ref, _side_like(cmp, rows), {}, "FAIL", 1, "dropped %s" % list(
        dropped.get(k) for k in spec.pair_keys)


def _inj_extra_row(spec, ref, cmp):
    rows = copy.deepcopy(cmp.rows)
    extra = copy.deepcopy(rows[-1])
    for k in spec.pair_keys:
        v = extra.get(k)
        extra[k] = v + 1 if isinstance(v, int) and not isinstance(v, bool) else str(v) + "X"
    rows.append(extra)
    return ref, _side_like(cmp, rows), {}, "FAIL", 1, "invented %s" % list(
        extra.get(k) for k in spec.pair_keys)


def _inj_identity_skew(spec, ref, cmp):
    if not spec.identity_meta:
        return None
    k = spec.identity_meta[0]
    meta = dict(cmp.meta)
    meta[k] = str(meta.get(k)) + "-OTHER"
    return ref, _side_like(cmp, meta=meta), {}, "ABORT", 2, "%s skewed" % k


def _inj_empty_side(spec, ref, cmp):
    return ref, _side_like(cmp, rows=[]), {}, "ABORT", 2, "the Azimuth side produced nothing"


def _inj_row_identity_skew(spec, ref, cmp):
    if not spec.precondition:
        return None
    f = next((f for f in spec.precondition if any(f.name in r for r in cmp.rows)), None)
    if f is None:
        return None
    rows = copy.deepcopy(cmp.rows)
    rows[0][f.name] = str(rows[0].get(f.name)) + "-OTHER"
    return ref, _side_like(cmp, rows), {}, "ABORT", 2, "row 0 %s skewed" % f.name


def _inj_provenance_missing(spec, ref, cmp):
    meta = dict(cmp.meta)
    meta.pop(spec.provenance_meta[-1], None)
    return ref, _side_like(cmp, meta=meta), {}, "ABORT", 2, "no %s" % spec.provenance_meta[-1]


def _inj_unkeyable_row(spec, ref, cmp):
    rows = copy.deepcopy(cmp.rows)
    rows[0].pop(spec.pair_keys[-1], None)
    return ref, _side_like(cmp, rows), {}, "ABORT", 2, "row 0 has no %s" % spec.pair_keys[-1]


def _inj_vacuous_side(spec, ref, cmp):
    keep = set(spec.pair_keys) | {f.name for f in spec.precondition}
    rows = [{k: v for k, v in r.items() if k in keep} for r in copy.deepcopy(cmp.rows)]
    return ref, _side_like(cmp, rows), {}, "ABORT", 2, "pairing keys only"


def _inj_noted_only(spec, ref, cmp):
    f = _noted_field(spec, cmp.rows)
    if f is None:
        return None
    rows = copy.deepcopy(cmp.rows)
    for r in rows:
        if f in r:
            v = r[f]
            r[f] = _bump(v) if isinstance(v, (int, float)) and not isinstance(v, bool) else str(v) + "-run2"
    return ref, _side_like(cmp, rows), {}, "PASS", 0, "%s differs on every row" % f


def _inj_tol_within(spec, ref, cmp):
    f = _numeric_exact_gate_field(spec, cmp.rows)
    rows = copy.deepcopy(cmp.rows)
    i = next(i for i, r in enumerate(rows) if isinstance(r.get(f), (int, float))
             and not isinstance(r.get(f), bool))
    rows[i][f] = rows[i][f] + 0.2
    return (ref, _side_like(cmp, rows), {"tol_overrides": {f: 0.25}}, "PASS (DEGRADED)", 0,
            "%s off by 0.2, --tol %s=0.25" % (f, f))


def _inj_tol_exceeded(spec, ref, cmp):
    f = _numeric_exact_gate_field(spec, cmp.rows)
    rows = copy.deepcopy(cmp.rows)
    i = next(i for i, r in enumerate(rows) if isinstance(r.get(f), (int, float))
             and not isinstance(r.get(f), bool))
    rows[i][f] = rows[i][f] + 0.5
    return (ref, _side_like(cmp, rows), {"tol_overrides": {f: 0.25}}, "FAIL", 1,
            "%s off by 0.5, --tol %s=0.25" % (f, f))


def _inj_nan_all_field(spec, ref, cmp):
    """A gate field that is NaN on BOTH sides on every row compared NOTHING.

    NaN==NaN is agreement -- two undefined warmup bars genuinely agree -- but a field that is
    undefined everywhere would ride that all the way to a green gate having tested nothing. Same
    hole as a vacuous side, different hat.
    """
    f = _numeric_exact_gate_field(spec, cmp.rows)
    rr, cr = copy.deepcopy(ref.rows), copy.deepcopy(cmp.rows)
    for rows in (rr, cr):
        for r in rows:
            if f in r:
                r[f] = float("nan")
    return (_side_like(ref, rr), _side_like(cmp, cr), {}, "ABORT", 2,
            "%s NaN on both sides, 100%% of rows" % f)


def _inj_nan_partial_field(spec, ref, cmp):
    """Some rows undefined is legitimate. It must PASS -- and it must be COUNTED, not silent."""
    f = _numeric_exact_gate_field(spec, cmp.rows)
    rr, cr = copy.deepcopy(ref.rows), copy.deepcopy(cmp.rows)
    i = next(i for i, r in enumerate(cr) if f in r)
    rr[i][f] = cr[i][f] = float("nan")
    return (_side_like(ref, rr), _side_like(cmp, cr), {}, "PASS", 0,
            "%s NaN on both sides, 1 row only" % f)


def _inj_group_size(spec, ref, cmp):
    """Drop ONE member of a same-key group. Every remaining member still matches something --
    which is exactly why gate3 counts a group whose SIZE differs as a failure."""
    from collections import Counter
    c = Counter(tuple(r.get(k) for k in spec.pair_keys) for r in cmp.rows)
    dup = next((k for k, n in c.items() if n > 1), None)
    if dup is None:
        return None
    rows = copy.deepcopy(cmp.rows)
    for i, r in enumerate(rows):
        if tuple(r.get(k) for k in spec.pair_keys) == dup:
            rows.pop(i)
            break
    return ref, _side_like(cmp, rows), {}, "FAIL", 1, "group %s: 2 -> 1" % list(dup)


INJECTIONS = [
    ("identical", _inj_identical),
    ("mutated_field", _inj_mutated_field),
    ("missing_row", _inj_missing_row),
    ("extra_row", _inj_extra_row),
    ("identity_skew", _inj_identity_skew),
    ("empty_side", _inj_empty_side),
    ("row_identity_skew", _inj_row_identity_skew),
    ("provenance_missing", _inj_provenance_missing),
    ("unkeyable_row", _inj_unkeyable_row),
    ("vacuous_side", _inj_vacuous_side),
    ("noted_only", _inj_noted_only),
    ("tol_within", _inj_tol_within),
    ("tol_exceeded", _inj_tol_exceeded),
    ("group_size", _inj_group_size),
    ("nan_all_field", _inj_nan_all_field),
    ("nan_partial_field", _inj_nan_partial_field),
]


# ---------------------------------------------------------------------------- the proof
def prove(kinds=None) -> list:
    """Run every applicable injection against every artefact kind.

    Returns a list of dicts: kind, injection, expected, got, exit, ok, detail. A kind with no
    fixture is itself a FAILING row -- "we have not proven this one yet" is a result, not an
    omission to be quietly skipped.
    """
    results = []
    for kind in (kinds or all_kinds()):
        spec = get(kind)
        try:
            ref0, cmp0 = fixture(kind)
        except KeyError as e:
            results.append(dict(kind=kind, injection="(fixture)", expected="a fixture",
                                got="NONE", exit=None, ok=False, detail=str(e)))
            continue
        for name, fn in INJECTIONS:
            built = fn(spec, ref0, cmp0)
            if built is None:
                results.append(dict(kind=kind, injection=name, expected="n/a", got="n/a",
                                    exit=None, ok=True,
                                    detail="does not apply to this artefact (no such field/group)"))
                continue
            ref, cmp, kw, want, want_exit, detail = built
            v = run_gate(spec, ref, cmp, **kw)
            ok = (v.verdict == want and v.exit_code == want_exit)
            results.append(dict(kind=kind, injection=name, expected="%s(%d)" % (want, want_exit),
                                got="%s(%d)" % (v.verdict, v.exit_code), exit=v.exit_code, ok=ok,
                                detail=detail,
                                reason=(v.reasons[0] if v.reasons else "")))
    # The structural guard: a spec can never name a per-run id as a pairing key.
    try:
        ArtefactSpec(kind="_bad", pair_keys=("fireId",), precondition=(),
                     gate=(Field("x", EXACT),))
        results.append(dict(kind="(all)", injection="forbidden_pair_key", expected="SpecError",
                            got="accepted", exit=None, ok=False,
                            detail="a spec was allowed to pair on fireId"))
    except SpecError as e:
        results.append(dict(kind="(all)", injection="forbidden_pair_key", expected="SpecError",
                            got="SpecError", exit=None, ok=True, detail=str(e)[:90]))
    return results


def report(results) -> str:
    L = ["FAULT INJECTION -- proving each artefact's gate CAN fail", ""]
    L.append("  %-16s %-19s %-16s %-16s %s" % ("artefact", "injection", "expected", "got", "detail"))
    L.append("  " + "-" * 104)
    for r in results:
        L.append("  %-16s %-19s %-16s %-16s %s %s"
                 % (r["kind"], r["injection"], r["expected"], r["got"],
                    "ok " if r["ok"] else "BAD", r["detail"][:46]))
    bad = [r for r in results if not r["ok"]]
    L.append("")
    L.append("%d checks, %d unexpected" % (len(results), len(bad)))
    L.append("PROVEN -- every artefact's gate passes when it should and fails when it must."
             if not bad else "NOT PROVEN -- see the BAD rows above.")
    return "\n".join(L)
