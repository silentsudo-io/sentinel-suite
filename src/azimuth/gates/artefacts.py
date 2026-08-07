"""artefacts — the four ported kinds of §2, and the registry a fifth is added to.

Each spec is the WRITTEN-DOWN answer to two questions §2 asks of every port:

    what does the gate compare, and what pairs a record on one side with a record on the other?

The field names are §2's own, verbatim, which means they are inconsistently cased (`bar_ts` and
`netScore` in the same table). That is deliberate: the reference column is the NinjaScript corpus
and its keys ARE camelCase, while the tape contract (§3.1) is snake_case. Rather than invent a
third convention, the canonical name is whatever §2 wrote, and a side that spells it differently
declares a `Side.alias` — which is printed on every run, so a rename can never hide a field.

TOLERANCES ARE DECLARED HERE AND NOWHERE ELSE
---------------------------------------------
Every gate field below carries an explicit tolerance and every one of them is `EXACT` (0.0).
That is a judgement, and it is the strict one on purpose: a bar boundary is a tape price copied,
not computed, and a seam value is a deterministic function of the same bars. If a port cannot be
bit-identical, the honest response is to find out WHY, not to open a window and stop looking.
Loosening one is possible (`--tol field=0.25`) and stamps DEGRADED on the verdict, exactly as
`gate3 --tol-ticks` does.
"""
from __future__ import annotations

import os
import sys

from .parity import EXACT, NON_NUMERIC, ArtefactSpec, Field, SpecError, swallow

__all__ = ["SPECS", "register", "get", "kinds", "describe"]

SPECS: dict = {}


def register(spec: ArtefactSpec, *, replace: bool = False) -> ArtefactSpec:
    if spec.kind in SPECS and not replace:
        raise SpecError("artefact kind %r is already registered. Two specs under one name is how "
                        "a gate ends up testing something other than what its verdict claims."
                        % spec.kind)
    SPECS[spec.kind] = spec
    return spec


def get(kind: str) -> ArtefactSpec:
    if kind not in SPECS:
        raise SpecError("unknown artefact kind %r (known: %s)" % (kind, ", ".join(sorted(SPECS))))
    return SPECS[kind]


def kinds() -> list:
    return sorted(SPECS)


# ============================================================================ 1 · BAR TYPE
# §2: "bar boundaries: open/high/low/close/volume/ts per bar over one session", key (session, bar_index)
#
# Bar types are NON-STATIONARY (`sentinel-offline-harness`) -- the same tape replayed through the
# same definition must still produce the same boundaries, which is precisely what makes a
# boundary diff meaningful and a boundary tolerance meaningless. Hence EXACT throughout.
register(ArtefactSpec(
    kind="bartype",
    doc="A Sentinel bar type (TBars, Flux, BRK, CVB, Drift, Flow) built from one session of tape.",
    pair_keys=("session", "bar_index"),
    identity_meta=("tape_sha256", "instrument", "session", "bar_params"),
    precondition=(
        Field("instrument", NON_NUMERIC, "a bar of a different instrument is a different experiment"),
        Field("bartype", NON_NUMERIC, "the bar-type id, e.g. SentinelFlux"),
        Field("bar_params", NON_NUMERIC, "the settings string; different params, different bars"),
    ),
    gate=(
        Field("open", EXACT, "a tape price copied, not computed"),
        Field("high", EXACT),
        Field("low", EXACT),
        Field("close", EXACT),
        Field("volume", EXACT),
        Field("ts_ms", EXACT, "bar stamp, unix ms UTC (EXACT also covers an ISO string)"),
        Field("open_ts_ms", EXACT, "first tick of the bar, where the port publishes it"),
        Field("tick_count", EXACT, "for count-driven types this IS the boundary"),
    ),
    noted=(
        Field("bar_id", NON_NUMERIC, "per-run id; never a key"),
        Field("builder", NON_NUMERIC),
    ),
    required_fields=("open", "high", "low", "close"),
))

# ============================================================================ 2 · SENSOR / VOTER
# §2: "the published seam value per bar", key (scope, bar_ts)
#
# SCOPE, NOT INSTRUMENT. A scope is "<masterInstrument>.<barTag>" -- one chart's worth of context,
# and exactly the coordinate a model is defined over (`seam-scope-migration`). Pairing on the
# instrument alone would silently compare two charts.
register(ArtefactSpec(
    kind="sensor",
    doc="One Sentinel sensor/voter's published seam value, bar by bar.",
    pair_keys=("scope", "bar_ts"),
    identity_meta=("tape_sha256", "scope", "sensor", "sensor_params"),
    precondition=(
        Field("sensor", NON_NUMERIC, "which voter this is"),
        Field("sensor_params", NON_NUMERIC, "period/threshold settings"),
        Field("bar_label", NON_NUMERIC),
    ),
    gate=(
        Field("value", EXACT, "the seam's numeric reading"),
        Field("vote", EXACT, "-1 / 0 / +1 as the Council consumes it"),
        Field("state", NON_NUMERIC, "regime/phase string where the seam publishes one"),
        Field("dir", EXACT),
        Field("stale", EXACT, "bools take the equality path; the tolerance is inert but declared"),
    ),
    noted=(
        # UpdatedUtc is stamped DateTime.UtcNow even while the publisher replays historical bars,
        # so it carries no as-of semantics and cannot be evidence of anything
        # (`state-seam-freshness-heartbeat`; it is how lookahead got into the corpus).
        Field("updated_utc", NON_NUMERIC, "a wall-clock stamp with no as-of meaning -- NEVER a gate field"),
        Field("seq", EXACT, "per-run counter"),
    ),
    required_fields=(("value", "state"),),
))

# ============================================================================ 3 · COUNCIL
# §2: "netScore, activeW, conviction, veto/damp flags, fire decision", key (scope, bar_ts)
register(ArtefactSpec(
    kind="council",
    doc="The confluence arbiter's per-bar verdict.",
    pair_keys=("scope", "bar_ts"),
    identity_meta=("tape_sha256", "scope", "model_id"),
    precondition=(
        Field("model_id", NON_NUMERIC, "the weights model. Two models is two councils."),
        Field("roster", NON_NUMERIC, "the declared voter roster; a missing voter abstains silently"),
        Field("bar_label", NON_NUMERIC),
    ),
    gate=(
        Field("netScore", EXACT),
        Field("activeW", EXACT),
        Field("conviction", EXACT),
        Field("veto", EXACT, "bool"),
        Field("vetoReason", NON_NUMERIC),
        Field("damp", EXACT, "bool"),
        Field("dampMult", EXACT),
        Field("signal", NON_NUMERIC, "the fire decision"),
        Field("sizeMult", EXACT),
        # Extensions beyond §2's literal list, because they are behaviour and gate3 gates them:
        # a vote tally that drifts while netScore agrees means two voters cancelled out.
        Field("agree", EXACT),
        Field("disagree", EXACT),
        Field("voters", EXACT),
    ),
    noted=(
        Field("votes_json", NON_NUMERIC, "recorded; too brittle to gate as text"),
        Field("updated_utc", NON_NUMERIC),
    ),
    required_fields=("netScore", "conviction"),
))

# ============================================================================ 4 · STRATEGY
# §2: "trades: entry/exit time, direction, price, exit reason, stop", key (fireTime, dir, signal)
#
# THE KEY IS GATE3'S KEY, INCLUDING ITS TRAP. Two fires genuinely can share a stamp (the corpus
# holds `..._GC_S_2` and `..._GC_S_3` at one fireTime), so a key holds several records; they are
# ordered inside the group by `fireId`'s trailing counter -- which is ORDERING ONLY. `fireId`
# itself can never be a pairing key (`episode-id-not-a-cross-run-key`), and `ArtefactSpec`
# refuses one structurally.
register(ArtefactSpec(
    kind="strategy",
    doc="A strategy's trade list: the §2 trade fields, portable across the two columns.",
    pair_keys=("fireTime", "dir", "signal"),
    seq_field="fireId",
    identity_meta=("tape_sha256", "instrument", "strategy", "strategy_params"),
    precondition=(
        Field("inst", NON_NUMERIC),
        Field("bartype", NON_NUMERIC),
        Field("scope", NON_NUMERIC),
    ),
    gate=(
        Field("entryTime", EXACT, "EXACT covers an ISO string and an epoch-ms int alike"),
        Field("entryPx", EXACT),
        Field("exitTime", EXACT),
        Field("exitPx", EXACT),
        Field("endReason", NON_NUMERIC, "Target / Stop / Flip / SessionEnd ..."),
        Field("stopPx", EXACT),
        Field("stopTicks", EXACT),
        # Extensions beyond §2's literal list: the target is the other half of the bracket, and a
        # size or P&L that drifts while prices agree is a real divergence in the order model.
        Field("targetPx", EXACT),
        Field("targetTicks", EXACT),
        Field("qty", EXACT),
        Field("pnlTicks", EXACT),
    ),
    noted=(
        Field("fireId", NON_NUMERIC, "per-run counter; ORDERS a group, never keys one"),
        Field("tradeId", NON_NUMERIC),
        Field("episodeId", NON_NUMERIC),
        Field("ticks", EXACT, "tick-path length"),
        Field("trunc", EXACT),
    ),
    required_fields=("entryPx", "exitPx", "endReason"),
))


# ============================================================================ 4b · STRATEGY (corpus)
# The same artefact when BOTH sides speak the corpus schema -- i.e. the Azimuth re-emitting
# `cand.*` / schema-1.x excursion rows, or a fleet determinism check. Its field lists are
# gate3's, IMPORTED LIVE rather than copied, so a field added to the fleet gate arrives here too.
# The vendored fallback exists only for a box without the Lab tree, and the verdict says which
# one was used (`spec: ...`), because a gate whose definition you cannot name is not evidence.
_VENDORED_PRE = ("recVer", "coreVer", "barLabel", "inst", "bartype", "scope", "schema")
_VENDORED_GATE = (
    "firePx", "pxSrc", "barClosePx", "entryBid", "entryAsk", "entryPx",
    "regime", "adx", "runLength", "clockPhase", "minsToClose", "mtfBias",
    "rvol", "volZ", "climax", "dryUp", "fluxDir", "fluxPressure", "fluxDiverg",
    "brkUpper", "brkLower", "barrierTicks", "conviction", "netScore", "sizeMult",
    "agree", "disagree", "voters",
    "maxFavTicks", "maxAdvTicks", "msToMaxFav", "msToMaxAdv", "msToTargetR", "msToStopR",
    "firstTouchTick", "firstTouch", "maxMFE", "maxMAE", "msToMFE", "msToMAE", "bars",
    "mfe1", "mae1", "mfe5", "mae5", "mfe15", "mae15", "mfe60", "mae60",
    "barsToMFE", "barsToMAE", "barsToTargetR", "barsToStopR", "endReason", "endTime",
    "exitTime", "exitPx",
)
_VENDORED_NOTED = ("fireId", "tradeId", "ticks", "trunc", "episodeId")


def _gate3_lists():
    """(precondition, gate, noted, source). Imports Lab\\gate3.py; falls back, loudly, to a copy."""
    lab = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                       "..", "..", "Lab"))
    try:
        if lab not in sys.path:
            sys.path.insert(0, lab)
        import gate3  # type: ignore
        return (tuple(gate3.PRECONDITION), tuple(gate3.GATE), tuple(gate3.NOTED),
                "Lab\\gate3.py (imported live)")
    except Exception as e:
        swallow("gates.import_gate3", e, lab)
        return (_VENDORED_PRE, _VENDORED_GATE, _VENDORED_NOTED,
                "vendored copy of gate3's field lists (Lab tree not importable)")


_p, _g, _n, _src = _gate3_lists()
register(ArtefactSpec(
    kind="strategy_corpus",
    doc="A strategy when BOTH sides speak the corpus schema. gate3's field lists, its tiering, "
        "its key. Use `strategy` for a cross-column trade-list comparison.",
    pair_keys=("fireTime", "dir", "signal"),
    seq_field="fireId",
    identity_meta=("cell",),
    precondition=tuple(Field(n, NON_NUMERIC) for n in _p),
    # gate3 runs zero tolerance; --tol-ticks is diagnosis only and stamps DEGRADED. Same here.
    gate=tuple(Field(n, EXACT) for n in _g),
    noted=tuple(Field(n, NON_NUMERIC if n in ("fireId", "tradeId", "episodeId") else EXACT)
                for n in _n),
    required_fields=("entryPx", "endReason"),
    source=_src,
))


def describe(kind: str) -> str:
    """The spec, in full, as text. `python -m gates describe --artefact council`."""
    s = get(kind)
    L = ["ARTEFACT  %s" % s.kind, "  %s" % s.doc, "",
         "  spec source     %s" % s.source,
         "  pairing key     (%s)" % ", ".join(s.pair_keys),
         "  order in group  %s" % (s.seq_field or "(load order)"),
         "  identity (meta, must be EQUAL, else ABORT)",
         "      " + (", ".join(s.identity_meta) or "(none)"),
         "  provenance (meta, must be PRESENT, recorded not compared)",
         "      " + (", ".join(s.provenance_meta) or "(none)"),
         "  required on >=1 row of each side (the vacuity guard)",
         "      " + (", ".join("any of (%s)" % ", ".join(r) if isinstance(r, tuple) else r
                               for r in s.required_fields) or "(none)"),
         ""]
    for tier, name, consequence in ((s.precondition, "PRECONDITION", "ABORT (2)"),
                                    (s.gate, "GATE", "FAIL (1)"),
                                    (s.noted, "NOTED", "never fails")):
        L.append("  %-12s on difference: %s" % (name, consequence))
        for f in tier:
            L.append("      %-18s %-12s %s" % (f.name, f.tol_text, f.note))
        L.append("")
    return "\n".join(L)
