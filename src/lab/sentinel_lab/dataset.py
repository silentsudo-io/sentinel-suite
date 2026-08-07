"""
Load Sentinel excursion JSONL into a training frame.

Schema-tolerant: reads 1.2 (no decision vector) and 1.3 (with it). Rows that
predate 1.3 simply carry NaN in the voter columns and are dropped by the weight
trainer -- but they remain fully usable for the calibration curve, which needs
nothing but `conviction`.

The one modelling decision that lives here: FOLD BY DIRECTION.

    x_i = vote_i * dir

A long verdict with EYE=+1 and a short verdict with EYE=-1 are the SAME evidence
("the Eye agreed with the taken side"). Folding halves the feature space, doubles
effective N, and -- because `dir == sign(netScore)` for any verdict -- makes the
fitted coefficient directly comparable to the Council's hand-set `WeightEye`.
It drops straight into Model.conf.
"""
from __future__ import annotations

import collections
import glob
import json
import os

import numpy as np
import pandas as pd
from lab_faults import swallow

CLOCK_PHASES = {0: "closed", 1: "opendrive", 2: "midday", 3: "close"}

# ── SHARED VOTER CATALOG ──────────────────────────────────────────────────────────────
# The canonical tag → role/kind/default-weight/seam map is OWNED by the C# VoterCatalog
# (AddOns\SentinelCore.SystemBuilder.cs) and EMITTED to Sentinel\Models\catalog.conf on
# Council load. The Lab reads that file so the fit's feature columns are built in the SAME
# order from the SAME source of truth — no more hardcoded 10-voter list drifting behind a
# 22-voter Council. If the file is absent (e.g. NT never ran since the last catalog change),
# we fall back to this embedded copy — kept in sync by regenerating from the C# source.
# cols: tag, role(voter|modulator|veto), kind(state|trigger), defWeight, display, seam
_EMBEDDED_CATALOG = [
    ("EYE",  "voter", "trigger", 1.4, "Eye",             "EyeVerdict"),
    ("TRND", "voter", "state",   1.0, "SentinelTrend",   "TrendState"),
    ("CCI",  "voter", "state",   0.8, "Woodies CCI",     "CciState"),
    ("ADX",  "voter", "state",   0.6, "ADX Pro",         "AdxState"),
    ("ENV",  "voter", "state",   0.6, "Vol Envelope",    "EnvelopeState"),
    ("BRK",  "voter", "state",   0.5, "Brick",           "BrickState"),
    ("CMP",  "voter", "trigger", 0.7, "Compression",     "CompressionState"),
    ("IMKT", "voter", "state",   0.6, "Intermarket",     "IntermarketState"),
    ("WAE",  "voter", "trigger", 0.7, "WAE",             "WaeState"),
    ("GREV", "voter", "trigger", 0.9, "God Reversal",    "GodReversalState"),
    ("STF",  "voter", "state",   0.0, "Stoch Filter",    "StfState"),
    ("FLOW", "voter", "state",   0.9, "Flow",            "FlowState"),
    ("STRC", "voter", "state",   0.7, "Structure",       "StructureState"),
    ("EXH",  "voter", "trigger", 0.5, "Exhaustion",      "ExhaustionState"),
    ("AVMA", "voter", "state",   0.6, "ADXVMA",          "AdxvmaState"),
    ("SPRT", "voter", "state",   0.7, "SuperTrend",      "SuperTrendState"),
    ("PSAR", "voter", "state",   0.5, "Parabolic SAR",   "SarState"),
    ("ZSC",  "voter", "trigger", 0.4, "Z-Score",         "ZScoreState"),
    ("ARCH", "voter", "state",   0.7, "Trend Architect", "TrendArchitectState"),
    ("VDYA", "voter", "state",   0.5, "VIDYA",           "VidyaState"),
    ("HARM", "voter", "trigger", 0.4, "Harmonic",        "HarmonicState"),
    ("FLUX", "voter", "state",   0.7, "Flux",            "FluxState"),
    ("CLOCK",  "modulator", "state", 0.0, "Clock",         "ClockState"),
    ("PARTIC", "modulator", "state", 0.0, "Participation", "ParticipationState"),
    ("MTF",    "modulator", "state", 0.0, "MTF",           "MtfState"),
    ("LOC",    "veto",      "state", 0.0, "Location",      "LevelState"),
    ("LIQ",    "veto",      "state", 0.0, "Liquidity",     "LiquidityState"),
]

# module-relative default: Sentinel\Lab\sentinel_lab\dataset.py  ->  Sentinel\Models\catalog.conf
_DEFAULT_CATALOG_PATH = os.path.join(os.path.dirname(__file__), "..", "..", "Models", "catalog.conf")


def load_catalog(path: str | None = None) -> list[dict]:
    """Read the shared voter catalog (Models\\catalog.conf). Ordered list of dicts, canonical
    (F6/Council) order. Falls back to the embedded copy if the file is missing/empty."""
    rows: list[dict] = []
    p = path or _DEFAULT_CATALOG_PATH
    try:
        with open(p, "r", encoding="utf-8") as fh:
            for line in fh:
                line = line.strip()
                if not line or line.startswith("#"):
                    continue
                parts = line.split("|")
                if len(parts) < 6:
                    continue
                tag, role, kind, w, disp, seam = parts[:6]
                try:
                    wv = float(w)
                except ValueError:
                    wv = 0.0
                rows.append({"tag": tag.upper(), "role": role.lower(), "kind": kind.lower(),
                             "defWeight": wv, "display": disp, "seam": seam})
    except OSError:
        rows = []
    if not rows:
        rows = [{"tag": t, "role": r, "kind": k, "defWeight": w, "display": d, "seam": s}
                for (t, r, k, w, d, s) in _EMBEDDED_CATALOG]
    return rows


def catalog_voter_tags(catalog: list[dict] | None = None) -> list[str]:
    """The weighted-voter tags, in canonical order (role == 'voter')."""
    cat = catalog if catalog is not None else load_catalog()
    return [e["tag"] for e in cat if e["role"] == "voter"]


def catalog_weights(catalog: list[dict] | None = None) -> dict:
    """{tag: default weight} for the weighted voters."""
    cat = catalog if catalog is not None else load_catalog()
    return {e["tag"]: e["defWeight"] for e in cat if e["role"] == "voter"}


# Back-compat: the full canonical voter list from the catalog (was a hardcoded 10; now 22).
VOTER_TAGS = catalog_voter_tags()


def active_voter_tags(df: pd.DataFrame, catalog: list[dict] | None = None,
                      min_frac: float = 0.05, min_abs: int = 30):
    """The voter tags to actually FIT for this frame.

    Per-bartype rosters diverge wildly (2..18 voters recorded), and the Lab fits per-bartype,
    so the feature set must be DATA-DRIVEN — the catalog voters that this corpus actually
    records above a support threshold AND with at least one non-neutral vote. Returns
    (tags_in_catalog_order, report). The report names thin (under-supported) and undeclared
    (present-but-not-in-catalog) tags so drift in EITHER direction is visible, never silent.
    """
    cat = catalog if catalog is not None else load_catalog()
    voter_order = [e["tag"] for e in cat if e["role"] == "voter"]
    known = set(voter_order)

    votes = df["votes"] if "votes" in df.columns else pd.Series([{}] * len(df), index=df.index)
    present = collections.Counter()
    nonzero = collections.Counter()
    n_vec = 0
    for m in votes:
        if isinstance(m, dict) and m:
            n_vec += 1
            for k, v in m.items():
                present[k.upper()] += 1
                try:
                    if float(v) != 0.0:
                        nonzero[k.upper()] += 1
                except (TypeError, ValueError) as _swex:
                    swallow("sentinel_lab.dataset.active_voter_tags", _swex)

    thresh = max(min_abs, int(min_frac * n_vec))
    admitted, thin, undeclared = [], [], []
    for tag in voter_order:
        c = present.get(tag, 0)
        if c >= thresh and nonzero.get(tag, 0) > 0:
            admitted.append(tag)
        elif c > 0:
            thin.append((tag, c))
    for tag in present:
        if tag not in known:
            undeclared.append((tag, present[tag]))

    report = {"n_vec": n_vec, "thresh": thresh, "admitted": admitted,
              "thin": thin, "undeclared": undeclared}
    return admitted, report


def load_jsonl(excursions_dir: str, instrument: str | None = None,
               bartype: str | None = None) -> pd.DataFrame:
    """Read every *.jsonl under excursions_dir into one frame."""
    rows = []
    for path in sorted(glob.glob(os.path.join(excursions_dir, "*.jsonl"))):
        with open(path, "r", encoding="utf-8") as fh:
            for lineno, line in enumerate(fh, 1):
                line = line.strip()
                if not line:
                    continue
                try:
                    rec = json.loads(line)
                except json.JSONDecodeError:
                    # A crash mid-append can truncate the final line. Skip it, don't die.
                    print(f"  ! bad json {os.path.basename(path)}:{lineno} -- skipped")
                    continue
                if rec.get("kind") != "excursion":
                    continue
                rec["_file"] = os.path.basename(path)
                rows.append(rec)

    if not rows:
        raise SystemExit(f"no excursion rows found under {excursions_dir}")

    df = pd.DataFrame(rows)
    df["fireTime"] = pd.to_datetime(df["fireTime"], utc=True, format="ISO8601")
    df = df.sort_values("fireTime").reset_index(drop=True)

    # NEVER pool bartypes. Bar granularity determines label optimism -- a Renko-labelled
    # row and a minute-labelled row do not describe the same world.
    # (see: backtest-fill-resolution-lesson -- CompressionBase 81% -> 37.5%)
    if instrument:
        df = df[df["inst"] == instrument]
    if bartype:
        df = df[df["bartype"] == bartype]

    if df.empty:
        raise SystemExit(f"no rows after filtering inst={instrument} bartype={bartype}")
    return df.reset_index(drop=True)


def council_rows(df: pd.DataFrame) -> pd.DataFrame:
    """Council verdicts only -- the rows whose weights we are fitting."""
    return df[df["signal"] == "COUNCIL"].reset_index(drop=True)


def has_decision_vector(df: pd.DataFrame) -> pd.Series:
    """True where the row was written by a schema-1.3+ recorder."""
    if "votes" not in df.columns:
        return pd.Series(False, index=df.index)
    return df["votes"].apply(lambda v: isinstance(v, dict) and len(v) > 0)


def _col(df: pd.DataFrame, name: str, default) -> pd.Series:
    """`df.get(name, default)` returns a SCALAR when the column is absent -- which is exactly
    the schema-1.2 case these call sites exist to handle. Always hand back a Series."""
    if name in df.columns:
        return df[name]
    return pd.Series(default, index=df.index)


def build_features(df: pd.DataFrame, tags: list[str] | None = None,
                   include_active_flags: bool = False) -> pd.DataFrame:
    """
    Direction-folded feature matrix for the ridge-logistic weight fit.

    `tags` is the voter set to build columns for — pass the DATA-DRIVEN set from
    active_voter_tags(df) so the feature matrix matches THIS bartype's recorded roster
    (2..18 voters), not a fixed list. Defaults to the full catalog voter list.

    An ABSENT voter contributes 0 to netScore, so 0 is the arithmetically correct
    encoding of an abstention *for the linear score*. It is NOT the same event as a
    present-but-neutral voter, though, and the distinction can matter. `activeW` and
    `voters` carry the abstention mass; set include_active_flags=True to also emit
    per-voter presence indicators (costs one more parameter each -- see the N budget in
    the spec before you turn it on).
    """
    if tags is None:
        tags = VOTER_TAGS
    d = df["dir"].astype(float)
    out = pd.DataFrame(index=df.index)

    votes = df["votes"]
    for tag in tags:
        v = votes.apply(lambda m, t=tag: float(m.get(t, 0)) if isinstance(m, dict) else 0.0)
        out[f"v_{tag}"] = v * d                       # +1 = agreed with the taken side
        if include_active_flags:
            out[f"a_{tag}"] = votes.apply(
                lambda m, t=tag: 1.0 if isinstance(m, dict) and t in m else 0.0)

    # --- the orthogonal axes (modulators) -------------------------------------------------
    # These are the feature-INDEPENDENT signals. The price-derived voter block above is
    # heavily collinear; expect these to survive L2 shrinkage better than half of it.
    clock = pd.to_numeric(_col(df, "clockPhase", -1), errors="coerce").fillna(-1).astype(int)
    for code, name in CLOCK_PHASES.items():
        if name == "midday":
            continue                                   # reference level -- drop one, avoid dummy trap
        out[f"clk_{name}"] = (clock == code).astype(float)

    rvol = pd.to_numeric(_col(df, "rvol", np.nan), errors="coerce")
    out["log_rvol"] = np.log(rvol.clip(lower=0.05)).fillna(0.0)
    out["rvol_missing"] = rvol.isna().astype(float)

    mtf = pd.to_numeric(_col(df, "mtfBias", 0), errors="coerce").fillna(0.0)
    out["mtf_agree"] = mtf * d                         # fold: did the ladder agree with the side?

    # A structural level in the path is a headwind regardless of direction -- do NOT fold.
    out["level_in_path"] = _col(df, "levelInPath", False).fillna(False).astype(float)

    # Direction asymmetry: longs and shorts genuinely differ in a trending regime.
    out["dir"] = d

    # Abstention mass. activeW low => few voters spoke => conviction means less.
    out["active_w"] = pd.to_numeric(_col(df, "activeW", np.nan), errors="coerce").fillna(0.0)
    out["n_voters"] = pd.to_numeric(_col(df, "voters", 0), errors="coerce").fillna(0.0)

    return out.astype(float)


def conviction(df: pd.DataFrame) -> pd.Series:
    """
    Signed-agreement magnitude, normalized. For any verdict dir == sign(netScore),
    so netScore*dir == |netScore| and this is exactly the Council's published
    `conviction`. Prefer recomputing from netScore/activeW when 1.3 fields exist
    (no rounding loss); fall back to the published field for 1.2 rows.
    """
    if "netScore" in df.columns and "activeW" in df.columns:
        ns = pd.to_numeric(df["netScore"], errors="coerce")
        aw = pd.to_numeric(df["activeW"], errors="coerce")
        recomputed = (ns * df["dir"]).abs() / aw.replace(0, np.nan)
        return recomputed.fillna(pd.to_numeric(df["conviction"], errors="coerce"))
    return pd.to_numeric(df["conviction"], errors="coerce")
