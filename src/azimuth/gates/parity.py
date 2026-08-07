"""parity — the Azimuth's equivalence harness (SENTINEL_AZIMUTH_SPEC §2, THE PARITY LAW).

WHY THIS EXISTS
---------------
The Azimuth re-implements Sentinel bar types, sensors, the Council and strategies in Python
alongside the NinjaScript originals. **Two implementations of one definition can silently
disagree** — and a research surface that computes something slightly different from the live
system is researching a different system. §2 is the law that makes the second column safe:

    Anything implemented in both columns must pass an equivalence gate before the Python side
    is trusted for research. No exceptions, no "we'll gate it later."

This module is `Lab\\gate3.py` generalised from *one strategy, two boxes* to *any Sentinel
artefact, two columns*. Everything below that reads like paranoia was paid for once already;
the provenance is named at each rule.

THE THREE TIERS, AND WHY THEY ARE NOT ONE TIER  (gate3, verbatim discipline)
---------------------------------------------------------------------------
  PRECONDITION  identity + provenance: the tape, the instrument, the session, the scope, the
                model — and, per row, the identity fields. A mismatch here means you compared
                two DIFFERENT EXPERIMENTS, so the honest answer is neither pass nor fail; it is
                ABORT (exit 2). Reporting FAIL would send someone to debug a port when the real
                defect is that the two sides read different tape.
  GATE          the behaviour fields. A difference on any of them = FAIL (exit 1).
  NOTED         printed, never fails: per-run counters and ids, and `updated_utc` — a seam
                stamp that is *known* to carry no as-of semantics (see the memory
                `state-seam-freshness-heartbeat` / the lookahead poisoning of the corpus).

WHAT WOULD MAKE THIS GATE LIE, AND WHAT IS DONE ABOUT IT
--------------------------------------------------------
  * **An empty side.** Zero rows on either side is ABORT, never PASS. A port that never ran and
    a port that ran and produced nothing are indistinguishable from here — the same shape as
    *a crashed sensor is indistinguishable from a quiet one*.
  * **A vacuous side.** Two sides that carry the pairing keys and none of the compared fields
    would pair perfectly and PASS. `required_fields` makes that an ABORT. (New here; gate3
    never needed it because both its sides were the same code emitting the same schema.)
  * **Leading with counts.** 1,488 vs 1,488 can be two disjoint sets. The summary leads with
    matched/differing and prints counts underneath, labelled as not being the test.
  * **Pairing on a per-run id.** `fireId` / `episode_id` are NOT cross-run keys
    (`episode-id-not-a-cross-run-key`). A spec that names one as a pairing key raises
    `SpecError` — the trap is closed structurally, not by remembering.
  * **A tolerance quietly creeping in.** Every compared field declares its tolerance, and the
    declaration is printed on every run. Loosening one at the command line stamps DEGRADED on
    the verdict and on the verdict file, because a gate passed with a tolerance is not the gate.
  * **A gate that has never failed.** `inject.py` proves each artefact kind CAN fail, six ways.

EXIT CODES:  0 = PASS   1 = FAIL   2 = could not run the test (abort / precondition)
"""
from __future__ import annotations

import math
import os
import sys
from dataclasses import dataclass, field as _dc_field

__all__ = [
    "EXACT", "NON_NUMERIC", "FORBIDDEN_PAIR_KEYS",
    "Field", "ArtefactSpec", "Side", "Verdict", "SpecError",
    "run_gate", "compare_value", "swallow",
]

# ---------------------------------------------------------------------------- fault recording
# The Lab's ledger is the house standard (`lab-faults-swallow`: never add a silent `except` to a
# Lab file). If the Lab tree is not importable — the Azimuth must be runnable on a bare box —
# fall back to a local recorder that still COUNTS and still says something on stderr. What is
# forbidden is silence, not the specific ledger.
_LAB = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                    "..", "..", "Lab"))
_local_faults: dict = {}

try:
    if _LAB not in sys.path:
        sys.path.insert(0, _LAB)
    from lab_faults import swallow  # type: ignore  # noqa: E402
except Exception as _e:  # the Lab tree moved or is absent — say so, do not pretend
    sys.stderr.write("[gates] lab_faults unavailable (%s: %s) — using the local fault recorder\n"
                     % (type(_e).__name__, _e))

    def swallow(tag: str, exc: BaseException | None = None, detail: str | None = None) -> None:
        """Local stand-in for lab_faults.swallow. Records and reports; never raises."""
        try:
            _local_faults[tag] = _local_faults.get(tag, 0) + 1
            sys.stderr.write("[gates-fault] %s: %s%s\n"
                             % (tag, exc, (" | %s" % detail) if detail else ""))
        except Exception:
            pass  # the recursion guard, and the only deliberate silence in this file


# ---------------------------------------------------------------------------- tolerance vocabulary
EXACT = 0.0
#: A field declared to hold no numbers. If numbers turn up on both sides anyway that is a SPEC
#: DEFECT, not a comparison — the harness aborts and tells you to declare a tolerance. This is
#: how "float comparison needs an explicit, declared tolerance" is enforced rather than hoped for.
NON_NUMERIC = "non-numeric"

#: Pairing on any of these is the §2 trap. Compared case- and underscore-insensitively.
FORBIDDEN_PAIR_KEYS = frozenset({
    "fireid", "episodeid", "tradeid", "runid", "orderid", "rowid", "id", "file", "seq",
})


def _norm_key(name: str) -> str:
    return str(name).replace("_", "").replace("-", "").lower()


class SpecError(ValueError):
    """The artefact spec itself is wrong. Always an ABORT, never a FAIL."""


# ---------------------------------------------------------------------------- the field
@dataclass(frozen=True)
class Field:
    """One compared field and — mandatorily — its declared tolerance.

    `tol` is REQUIRED. There is no default, on purpose: the one thing §2 asks of float
    comparison is that the choice is explicit and visible, so `EXACT` (0.0, bit-identical) has
    to be typed out exactly like `0.25` does.
    """
    name: str
    tol: float | str
    note: str = ""

    def __post_init__(self):
        if self.tol is NON_NUMERIC or self.tol == NON_NUMERIC:
            return
        if isinstance(self.tol, bool) or not isinstance(self.tol, (int, float)):
            raise SpecError("field %r: tol must be EXACT, a positive number, or NON_NUMERIC "
                            "(got %r)" % (self.name, self.tol))
        if self.tol < 0:
            raise SpecError("field %r: a negative tolerance (%r) is meaningless"
                            % (self.name, self.tol))

    @property
    def tol_text(self) -> str:
        if self.tol == NON_NUMERIC:
            return "non-numeric"
        if self.tol == 0.0:
            return "EXACT (0)"
        return "%g" % float(self.tol)


# ---------------------------------------------------------------------------- the artefact spec
@dataclass(frozen=True)
class ArtefactSpec:
    """What one KIND of ported thing is, and how two columns of it are compared.

    kind             short id: "bartype", "sensor", "council", "strategy", ...
    pair_keys        §2's pairing key. Validated against FORBIDDEN_PAIR_KEYS.
    precondition     identity fields carried on each ROW. Mismatch => ABORT.
    gate             the behaviour fields. Mismatch => FAIL.
    noted            per-run counters/ids/stamps. Printed, never fails.
    identity_meta    RUN-level keys that must be EQUAL on both sides (tape sha, instrument,
                     session, ...). This is §2's "input tape identity" precondition.
    provenance_meta  RUN-level keys that must be PRESENT on both sides and are recorded but NOT
                     compared. The two columns are different implementations by construction, so
                     their version strings differ by design; what is inadmissible is a verdict
                     that cannot say WHICH two implementations it compared.
    required_fields  gate fields that must appear on at least one row of EACH side. The vacuity
                     guard: without it, two sides carrying only pairing keys PASS.
    seq_field        used ONLY to order records inside a same-key group. Never a pairing key.
    """
    kind: str
    pair_keys: tuple
    precondition: tuple
    gate: tuple
    noted: tuple = ()
    identity_meta: tuple = ()
    provenance_meta: tuple = ("impl", "impl_ver")
    required_fields: tuple = ()
    seq_field: str | None = None
    doc: str = ""
    source: str = "azimuth"

    def __post_init__(self):
        if not self.pair_keys:
            raise SpecError("%s: a spec with no pairing key cannot pair anything" % self.kind)
        for k in self.pair_keys:
            if _norm_key(k) in FORBIDDEN_PAIR_KEYS:
                raise SpecError(
                    "%s: %r may not be a pairing key. Per-run ids are not cross-run keys "
                    "(episode-id-not-a-cross-run-key). Pair on the artefact's coordinates; use "
                    "seq_field if you need to order records inside one key." % (self.kind, k))
        if not self.gate:
            raise SpecError("%s: a spec with no gate fields can only ever PASS" % self.kind)
        names = [f.name for f in self.all_fields]
        dupes = sorted({n for n in names if names.count(n) > 1})
        if dupes:
            raise SpecError("%s: field(s) %s declared in more than one tier — a field cannot be "
                            "both evidence and noise" % (self.kind, ", ".join(dupes)))
        gate_names = {f.name for f in self.gate}
        # An entry may be a name, or a tuple meaning "at least one of these". The any-of form
        # exists because a state-only sensor publishes no numeric `value` and must still be
        # protected from a vacuous pass.
        missing = [n for r in self.required_fields
                   for n in ((r,) if isinstance(r, str) else tuple(r))
                   if n not in gate_names]
        if missing:
            raise SpecError("%s: required_fields %s are not gate fields"
                            % (self.kind, ", ".join(missing)))
        if self.seq_field and self.seq_field in self.pair_keys:
            raise SpecError("%s: seq_field %r is also a pairing key" % (self.kind, self.seq_field))

    @property
    def all_fields(self) -> tuple:
        return tuple(self.precondition) + tuple(self.gate) + tuple(self.noted)

    def field(self, name: str) -> Field | None:
        for f in self.all_fields:
            if f.name == name:
                return f
        return None

    def tolerance_table(self) -> dict:
        return {f.name: (f.tol if f.tol != NON_NUMERIC else NON_NUMERIC) for f in self.gate}


# ---------------------------------------------------------------------------- a side
@dataclass
class Side:
    """One column's output: rows, run-level metadata, and what could not be read.

    `alias` maps THIS SIDE's actual key -> the spec's canonical name, so a Python port emitting
    `net_score` can be gated against a NinjaScript corpus emitting `netScore` without either side
    being rewritten to please the harness. It is applied once, at construction, and printed on
    every run — a rename that happens silently is a rename that can hide a missing field.

    `unreadable` is COUNTED AND NAMED, never dropped quietly (gate3's rule): a side that silently
    lost 40 rows otherwise presents as a clean smaller set and the diff blames the port.
    """
    label: str
    rows: list
    meta: dict = _dc_field(default_factory=dict)
    unreadable: list = _dc_field(default_factory=list)
    origin: str = ""
    alias: dict = _dc_field(default_factory=dict)

    def __post_init__(self):
        if self.alias:
            self.rows = [self._apply_alias(r) for r in self.rows]
            self.meta = self._apply_alias(self.meta)

    def _apply_alias(self, row: dict) -> dict:
        out = dict(row)
        for actual, canonical in self.alias.items():
            if actual in out:
                if canonical in out and out[canonical] != out[actual]:
                    raise SpecError(
                        "%s: alias %r -> %r collides — the row already carries a different %r"
                        % (self.label, actual, canonical, canonical))
                out[canonical] = out.pop(actual)
        return out


# ---------------------------------------------------------------------------- comparison
def _is_num(v) -> bool:
    return isinstance(v, (int, float)) and not isinstance(v, bool)


def compare_value(f: Field, a, b, tol_override: float | None = None):
    """(differs, how) for one field. `how` names the rule that decided, for the record.

    Rules, inherited from gate3.differs() and then made explicit:
      * both absent        -> equal
      * one absent         -> DIFFERS ("present/absent"). A port that stopped emitting a gate
                              field must fail, not be skipped.
      * numeric vs not     -> DIFFERS ("type"). 1 and "1" are not the same answer.
      * numeric            -> tolerance, declared. NaN==NaN is treated as equal and flagged.
      * anything else      -> plain equality (bools included; bool is not a tolerance-bearing type)
    """
    if a is None and b is None:
        return False, "both-absent"
    if (a is None) != (b is None):
        return True, "present/absent"

    an, bn = _is_num(a), _is_num(b)
    if an != bn:
        return True, "type"

    if an:
        if f.tol == NON_NUMERIC:
            raise SpecError(
                "field %r is declared NON_NUMERIC but both sides carry numbers (%r, %r). "
                "Declare a tolerance — EXACT if it must be bit-identical." % (f.name, a, b))
        tol = float(f.tol if tol_override is None else tol_override)
        fa, fb = float(a), float(b)
        if math.isnan(fa) and math.isnan(fb):
            return False, "nan==nan"
        if tol == 0.0:
            return fa != fb, "exact"
        return abs(fa - fb) > tol, "tol=%g" % tol

    return a != b, "equality"


def _compare_row(spec: ArtefactSpec, ra: dict, rb: dict, tol_overrides: dict):
    """(precondition diffs, gate diffs, noted diffs, seen, nans) for one paired record.

    A field absent from BOTH sides is skipped — gate3's rule, and the reason a spec may carry
    fields an early port has not implemented yet. The vacuity that opens up is closed by
    `required_fields`, not by comparing absences to absences.

    `seen` and `nans` are the per-field census the caller needs to close the OTHER vacuity hole:
    a field that is NaN on both sides on every row it appears would compare nothing and report
    agreement. Counting is done here; the ruling is in `run_gate`.
    """
    pre, gate, noted, seen, nans = [], [], [], [], []
    for tier, out in ((spec.precondition, pre), (spec.gate, gate), (spec.noted, noted)):
        for f in tier:
            if f.name not in ra and f.name not in rb:
                continue
            a, b = ra.get(f.name), rb.get(f.name)
            # Only GATE fields may be loosened. An identity field compared with slack is not an
            # identity check, and a NOTED field never fails so slack is meaningless there.
            is_gate = out is gate
            ov = tol_overrides.get(f.name) if is_gate else None
            d, how = compare_value(f, a, b, ov)
            seen.append((f.name, is_gate))
            if how == "nan==nan":
                nans.append(f.name)
            if d:
                out.append((f.name, a, b, how))
    return pre, gate, noted, seen, nans


# ---------------------------------------------------------------------------- keys and groups
def _key_of(spec: ArtefactSpec, row: dict):
    return tuple(row.get(k) for k in spec.pair_keys)


def _seq_of(spec: ArtefactSpec, row: dict) -> tuple:
    """Order INSIDE a same-key group. Never pairing.

    Two records genuinely can share a key — gate3 saw `..._GC_S_2` and `..._GC_S_3` at one
    `fireTime` — so a key can hold several records. They are ordered by `seq_field`'s trailing
    counter where one exists, and by load order otherwise; a group whose SIZE differs is a
    failure even though every member matched something.
    """
    if not spec.seq_field:
        return (0,)
    v = row.get(spec.seq_field)
    if v is None:
        return (0,)
    if _is_num(v):
        return (float(v),)
    tail = str(v).rsplit("_", 1)[-1]
    try:
        return (float(tail),)
    except ValueError:
        return (0.0,)


def _group(spec: ArtefactSpec, rows: list):
    g: dict = {}
    unkeyable = []
    for i, r in enumerate(rows):
        k = _key_of(spec, r)
        if any(v is None for v in k):
            unkeyable.append((i, k))
            continue
        g.setdefault(k, []).append((i, r))
    for k in g:
        g[k].sort(key=lambda ir: (_seq_of(spec, ir[1]), ir[0]))
    return {k: [r for _i, r in v] for k, v in g.items()}, unkeyable


def _sortable(k) -> tuple:
    return tuple("" if v is None else str(v) for v in k)


# ---------------------------------------------------------------------------- the verdict
@dataclass
class Verdict:
    """The machine-readable answer. `to_json()` is the record; `to_text()` is for a human."""
    artefact: str
    ref_label: str
    cmp_label: str
    verdict: str = "ABORT"
    exit_code: int = 2
    reasons: list = _dc_field(default_factory=list)      # why it aborted
    matched: int = 0
    differing: int = 0
    n_ref: int = 0
    n_cmp: int = 0
    only_ref: list = _dc_field(default_factory=list)
    only_cmp: list = _dc_field(default_factory=list)
    size_mismatches: list = _dc_field(default_factory=list)
    gate_fails: list = _dc_field(default_factory=list)
    pre_fails: list = _dc_field(default_factory=list)
    noted: dict = _dc_field(default_factory=dict)
    nan_fields: dict = _dc_field(default_factory=dict)
    tolerances: dict = _dc_field(default_factory=dict)
    tol_overrides: dict = _dc_field(default_factory=dict)
    degraded: list = _dc_field(default_factory=list)
    identity: dict = _dc_field(default_factory=dict)
    provenance: dict = _dc_field(default_factory=dict)
    pair_keys: tuple = ()
    unreadable: dict = _dc_field(default_factory=dict)
    aliases: dict = _dc_field(default_factory=dict)
    spec_source: str = ""

    @property
    def passed(self) -> bool:
        return self.exit_code == 0

    def to_json(self) -> dict:
        d = dict(self.__dict__)
        d["pair_keys"] = list(self.pair_keys)
        d["only_ref"] = [list(k) for k in self.only_ref]
        d["only_cmp"] = [list(k) for k in self.only_cmp]
        d["size_mismatches"] = [[list(k), a, b] for k, a, b in self.size_mismatches]
        d["gate_fails"] = [{"key": list(k), "i": i,
                            "fields": [{"field": f, "ref": a, "cmp": b, "rule": how}
                                       for f, a, b, how in fl]}
                           for k, i, fl in self.gate_fails]
        d["pre_fails"] = [{"key": list(k), "i": i,
                           "fields": [{"field": f, "ref": a, "cmp": b, "rule": how}
                                      for f, a, b, how in fl]}
                          for k, i, fl in self.pre_fails]
        return d

    def _nan_lines(self) -> list:
        """Every NaN-paired field, with its count and percentage.

        A field quietly going 90% NaN between two columns is a port that stopped computing, and
        it must be VISIBLE rather than merely tolerated -- the count is the difference between
        "these agree" and "these agreed about nothing, most of the time".
        """
        if not self.nan_fields:
            return []
        out = ["NaN on BOTH sides -- counted as agreement (two undefined warmup bars do agree),",
               "  but a field is only evidence on the rows where it is DEFINED:"]
        for f, d in sorted(self.nan_fields.items()):
            pct = d.get("pct")
            flag = ""
            if pct is not None and pct >= 100.0:
                flag = "   <-- 100%: this field tested NOTHING"
            elif pct is not None and pct >= 50.0:
                flag = "   <-- majority"
            out.append("    %-18s %d of %d compared row(s)  %s%%%s"
                       % (f, d.get("nan_pairs", 0), d.get("compared", 0),
                          "?" if pct is None else ("%.1f" % pct),
                          flag + ("" if d.get("gated") else "  (not a gate field)")))
        return out

    # ---- human summary -------------------------------------------------------------------
    # ASCII only, deliberately. The Windows console is cp1252 and `keel_srcdiff.py` records what
    # happens when a check raises UnicodeEncodeError printing its own PASS: the process exits 1
    # and a passing gate reports as failing. A gate that cries wolf on its own output is worse
    # than no gate, so nothing here can be un-encodable.
    def to_text(self, show: int = 10) -> str:
        L = []
        w = L.append
        w("PARITY GATE  [%s]   %s  vs  %s" % (self.artefact, self.ref_label, self.cmp_label))
        w("pair key: (%s)   spec: %s" % (", ".join(self.pair_keys), self.spec_source))
        if self.identity:
            w("identity: " + "  ".join("%s=%r" % (k, v) for k, v in sorted(self.identity.items())))
        if self.provenance:
            for side, p in sorted(self.provenance.items()):
                w("impl:     %-10s %s" % (side, "  ".join("%s=%r" % (k, v)
                                                          for k, v in sorted(p.items()))))
        if self.aliases:
            for side, a in sorted(self.aliases.items()):
                if a:
                    w("alias:    %-10s %s" % (side, ", ".join("%s->%s" % kv for kv in sorted(a.items()))))
        if self.tolerances:
            w("tolerances (declared, per field):")
            for name, tol in sorted(self.tolerances.items()):
                mark = ""
                if name in self.tol_overrides:
                    mark = "   <-- OVERRIDDEN at the command line to %g" % self.tol_overrides[name]
                w("    %-18s %s%s" % (name, "EXACT (0)" if tol == 0.0 else
                                      ("non-numeric" if tol == NON_NUMERIC else "%g" % tol), mark))
        for side, files in sorted(self.unreadable.items()):
            if files:
                w("WARN  %s: %d unreadable input(s), e.g. %s"
                  % (side, len(files), ", ".join(files[:3])))
        w("")

        if self.verdict == "ABORT":
            w("ABORT -- the test could not be run.")
            for r in self.reasons:
                w("  * " + r)
            for line in self._nan_lines():
                w(line)
            for k, i, fl in self.pre_fails[:show]:
                w("  key %s #%d" % (list(k), i))
                for f, a, b, _how in fl:
                    w("      %-16s %s: %r   %s: %r" % (f, self.ref_label, a, self.cmp_label, b))
            w("")
            w("A parity verdict on this pair would mean nothing. Fix the precondition first.")
            return "\n".join(L)

        w("MATCHED   %d record(s) identical on every gate field" % self.matched)
        w("DIFFERING %d" % self.differing)
        if self.gate_fails:
            w("   %d paired record(s) differ on a gate field" % len(self.gate_fails))
        if self.only_ref:
            w("   %d only in %s  (missing from %s)" % (len(self.only_ref), self.ref_label, self.cmp_label))
        if self.only_cmp:
            w("   %d only in %s  (extra vs %s)" % (len(self.only_cmp), self.cmp_label, self.ref_label))
        if self.size_mismatches:
            w("   %d same-key group(s) with a different member count" % len(self.size_mismatches))
        w("(counts: %s %d, %s %d -- equal counts are NOT the test)"
          % (self.ref_label, self.n_ref, self.cmp_label, self.n_cmp))

        for k, i, fl in self.gate_fails[:show]:
            w("")
            w("  FAIL %s #%d" % (list(k), i))
            for f, a, b, how in fl[:12]:
                w("      %-16s %s: %-18r %s: %-18r [%s]"
                  % (f, self.ref_label, a, self.cmp_label, b, how))
            if len(fl) > 12:
                w("      ... and %d more field(s)" % (len(fl) - 12))
        if len(self.gate_fails) > show:
            w("")
            w("  ... and %d more differing record(s) (--show N)" % (len(self.gate_fails) - show))
        for k in self.only_ref[:show]:
            w("  FAIL only in %-10s %s" % (self.ref_label, list(k)))
        for k in self.only_cmp[:show]:
            w("  FAIL only in %-10s %s" % (self.cmp_label, list(k)))
        for k, a, b in self.size_mismatches[:show]:
            w("  FAIL group %s: %s has %d, %s has %d" % (list(k), self.ref_label, a, self.cmp_label, b))

        if self.noted:
            w("")
            w("noted (never fails the gate): "
              + ", ".join("%s x%d" % (f, n) for f, n in sorted(self.noted.items())))
        for line in self._nan_lines():
            w(line)

        w("")
        if self.verdict.startswith("PASS") and self.degraded:
            w("PASS (DEGRADED) -- every record matched, but this was not the full gate:")
            for d in self.degraded:
                w("    " + d)
            w("  Re-run clean before calling this artefact gated.")
        elif self.verdict == "PASS":
            w("PASS -- %d of %d records identical, 0 differing." % (self.matched, self.n_ref))
            w("  %s reproduces %s for this artefact." % (self.cmp_label, self.ref_label))
        else:
            w("FAIL -- %d differing. %s does NOT reproduce %s."
              % (self.differing, self.cmp_label, self.ref_label))
            w("  The Python side is NOT trusted for research on this artefact until this is understood.")
        return "\n".join(L)


def _abort(v: Verdict, *reasons) -> Verdict:
    v.verdict, v.exit_code = "ABORT", 2
    v.reasons.extend(reasons)
    return v


# ---------------------------------------------------------------------------- the gate
def run_gate(spec: ArtefactSpec, ref: Side, cmp: Side, *,
             tol_overrides: dict | None = None,
             check_identity: bool = True,
             check_provenance: bool = True) -> Verdict:
    """Run one artefact's parity gate. Returns a Verdict; never raises on data, only on nothing.

    Order matters and is the point: every ABORT condition is proven BEFORE any behaviour is
    compared, so a verdict of FAIL can only ever mean "the two implementations disagree".
    """
    tol_overrides = dict(tol_overrides or {})
    v = Verdict(artefact=spec.kind, ref_label=ref.label, cmp_label=cmp.label,
                pair_keys=tuple(spec.pair_keys), spec_source=spec.source,
                tolerances=spec.tolerance_table(), tol_overrides=tol_overrides,
                n_ref=len(ref.rows), n_cmp=len(cmp.rows),
                unreadable={ref.label: list(ref.unreadable), cmp.label: list(cmp.unreadable)},
                aliases={ref.label: dict(ref.alias), cmp.label: dict(cmp.alias)},
                provenance={ref.label: {}, cmp.label: {}})

    if ref.label == cmp.label:
        return _abort(v, "both sides are labelled %r -- a verdict that cannot name its two "
                         "columns is not evidence" % ref.label)

    # -- spec sanity: an override for a field nobody gates is a typo, and a typo that silently
    #    does nothing is how a gate ends up looser than its operator believes.
    gate_names = {f.name for f in spec.gate}
    for name in tol_overrides:
        if name not in gate_names:
            return _abort(v, "--tol %s: %r is not a gate field of %r (gate fields: %s)"
                          % (name, name, spec.kind, ", ".join(sorted(gate_names))))
    for name, val in tol_overrides.items():
        declared = spec.field(name)
        if declared is not None and declared.tol != NON_NUMERIC and float(val) > float(declared.tol):
            v.degraded.append("--tol %s=%g loosens the declared tolerance %s"
                              % (name, val, declared.tol_text))

    # -- PRECONDITION 1: the same experiment (identity meta) -------------------------------
    if check_identity:
        for k in spec.identity_meta:
            a, b = ref.meta.get(k), cmp.meta.get(k)
            v.identity[k] = a if a == b else {ref.label: a, cmp.label: b}
            if a is None or b is None:
                return _abort(v, "identity key %r is missing on %s -- cannot prove the two sides "
                                 "read the same input. (%s=%r, %s=%r)"
                              % (k, ref.label if a is None else cmp.label,
                                 ref.label, a, cmp.label, b))
            if a != b:
                return _abort(v, "identity key %r differs: %s=%r, %s=%r -- these are two "
                                 "different experiments" % (k, ref.label, a, cmp.label, b))
    elif spec.identity_meta:
        v.degraded.append("--no-identity-check: the two sides were NOT proven to share an input "
                          "(%s)" % ", ".join(spec.identity_meta))

    # -- PRECONDITION 2: provenance is recorded --------------------------------------------
    # NOT compared. The two columns are different implementations by construction, so equal
    # version strings would be the surprising outcome. What is inadmissible is a verdict that
    # cannot say which two implementations produced it.
    if check_provenance:
        for side in (ref, cmp):
            for k in spec.provenance_meta:
                if side.meta.get(k) in (None, ""):
                    return _abort(v, "%s carries no %r -- a parity verdict that cannot name the "
                                     "implementation it blessed is not evidence" % (side.label, k))
                v.provenance[side.label][k] = side.meta.get(k)
    else:
        v.degraded.append("--no-provenance-check: this verdict does not record what it compared")

    # -- PRECONDITION 3: an empty side is ABORT, not PASS -----------------------------------
    for side in (ref, cmp):
        if not side.rows:
            return _abort(v,
                          "%s produced ZERO rows. A port that never ran and a port that ran and "
                          "found nothing are indistinguishable from here, so this is not a PASS."
                          % side.label)

    # -- PRECONDITION 4: every row can be keyed ---------------------------------------------
    gref, bad_ref = _group(spec, ref.rows)
    gcmp, bad_cmp = _group(spec, cmp.rows)
    for side, bad in ((ref, bad_ref), (cmp, bad_cmp)):
        if bad:
            return _abort(v, "%s: %d row(s) carry no value for part of the pairing key (%s); "
                             "e.g. row %d -> %r. Unpairable rows would silently vanish from the "
                             "comparison." % (side.label, len(bad), ", ".join(spec.pair_keys),
                                              bad[0][0], list(bad[0][1])))

    # -- PRECONDITION 5: the vacuity guard ---------------------------------------------------
    for side, rows in ((ref, ref.rows), (cmp, cmp.rows)):
        for req in spec.required_fields:
            wanted = (req,) if isinstance(req, str) else tuple(req)
            if not any(n in r for r in rows for n in wanted):
                return _abort(v, "%s carries %s on no row. This gate would pair every record and "
                                 "compare nothing -- a vacuous PASS."
                              % (side.label,
                                 "none of (%s)" % ", ".join(wanted) if len(wanted) > 1
                                 else repr(wanted[0])))

    # -- compare ------------------------------------------------------------------------------
    only_ref = sorted(set(gref) - set(gcmp), key=_sortable)
    only_cmp = sorted(set(gcmp) - set(gref), key=_sortable)
    shared = sorted(set(gref) & set(gcmp), key=_sortable)

    matched = 0
    compared_ct: dict = {}      # field -> paired rows on which it was actually compared
    nan_ct: dict = {}           # field -> of those, how many were NaN on BOTH sides
    gate_field_seen: set = set()
    try:
        for k in shared:
            a, b = gref[k], gcmp[k]
            if len(a) != len(b):
                v.size_mismatches.append((k, len(a), len(b)))
            for i in range(min(len(a), len(b))):
                pre, gt, noted, seen, nans = _compare_row(spec, a[i], b[i], tol_overrides)
                if pre:
                    v.pre_fails.append((k, i, pre))
                if gt:
                    v.gate_fails.append((k, i, gt))
                else:
                    matched += 1
                for f, _a, _b, _how in noted:
                    v.noted[f] = v.noted.get(f, 0) + 1
                for f, is_gate in seen:
                    compared_ct[f] = compared_ct.get(f, 0) + 1
                    if is_gate:
                        gate_field_seen.add(f)
                for f in nans:
                    nan_ct[f] = nan_ct.get(f, 0) + 1
    except SpecError as e:
        swallow("gates.spec_error", e, spec.kind)
        return _abort(v, "SPEC DEFECT -- %s" % e)

    v.matched = matched
    v.only_ref, v.only_cmp = only_ref, only_cmp
    v.nan_fields = {f: {"nan_pairs": n, "compared": compared_ct.get(f, 0),
                        "pct": round(100.0 * n / compared_ct[f], 1) if compared_ct.get(f) else None,
                        "gated": f in gate_field_seen}
                    for f, n in sorted(nan_ct.items())}

    # -- PRECONDITION 6: per-row identity ------------------------------------------------------
    if v.pre_fails:
        return _abort(v, "%d paired record(s) differ on identity/version fields -- the two sides "
                         "are not the same experiment." % len(v.pre_fails))

    # -- PRECONDITION 7: the NaN vacuity guard -------------------------------------------------
    # NaN==NaN is counted as agreement, and that is right: two sides both undefined on a warmup
    # bar genuinely agree. But a gate field that is NaN on BOTH sides on EVERY row it appears on
    # compared NOTHING and would report agreement anyway -- the same hole `required_fields`
    # closes, wearing a different hat. Warmup-only windows, an unported sensor branch returning
    # NaN throughout, a column the Python side never populates: all of them produce a green gate
    # over a field that was never tested. A check that can only pass one way is not a check.
    vacuous_nan = sorted(f for f, n in nan_ct.items()
                         if f in gate_field_seen and n == compared_ct.get(f))
    if vacuous_nan:
        return _abort(v, "gate field(s) %s are NaN on BOTH sides on 100%% of compared rows (%s). "
                         "Nothing was tested there, so agreement on them is not evidence. Either "
                         "gate a window where the field is defined, or stop declaring it."
                      % (", ".join(vacuous_nan),
                         ", ".join("%s %d/%d" % (f, nan_ct[f], compared_ct[f]) for f in vacuous_nan)))

    v.differing = len(v.gate_fails) + len(only_ref) + len(only_cmp) + len(v.size_mismatches)
    if v.differing == 0:
        v.verdict = "PASS (DEGRADED)" if v.degraded else "PASS"
        v.exit_code = 0
    else:
        v.verdict = "FAIL"
        v.exit_code = 1
    return v
