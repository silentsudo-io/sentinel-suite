#!/usr/bin/env python3
"""
Sentinel offline trainer.

    python train.py --inst GC --bartype SentinelTBars --barrier 20 --cost 1.5

Phase 1 (calibration) runs on schema 1.2 -- start it today, no NinjaTrader change.
Phase 2 (weights) needs the schema-1.3 decision vector; it is skipped with a loud
notice until those rows exist.

Emits Sentinel\\Model.conf. Nothing here ever touches bin\\Custom.
"""
from __future__ import annotations

import argparse
import datetime as dt
import os

import numpy as np
import pandas as pd
from sklearn.isotonic import IsotonicRegression
from sklearn.linear_model import LogisticRegression
from sklearn.metrics import brier_score_loss, roc_auc_score

from sentinel_lab import cv, dataset, labels
from lab_faults import swallow

def recorded_baseline_weights(df, tags, catalog):
    """The baseline we must beat, PER TAG = the MEDIAN voteW actually RECORDED in this corpus --
    the real per-scope Roster.conf weight the Council fused with, not a global hardcoded guess.
    Falls back to the catalog default when a tag carried no recorded weight. Covers whatever
    voter set the bartype recorded, so it never lags behind the roster the way a fixed dict did."""
    cat_w = dataset.catalog_weights(catalog)
    vw = df["voteW"] if "voteW" in df.columns else None
    out = {}
    for t in tags:
        vals = []
        if vw is not None:
            for m in vw:
                if isinstance(m, dict) and m.get(t) is not None:
                    try:
                        vals.append(float(m[t]))
                    except (TypeError, ValueError) as _swex:
                        swallow("train.recorded_baseline_weights", _swex)
        out[t] = float(np.median(vals)) if vals else float(cat_w.get(t, 0.0))
    return out


CONTINUOUS = ["log_rvol", "active_w", "n_voters"]
C_GRID = [0.01, 0.03, 0.1, 0.3, 1.0, 3.0]


def standardize(X: pd.DataFrame, cols, stats=None):
    """Scale only the continuous columns. The v_* votes are already in {-1,0,1}, and
    leaving them unscaled is what keeps a fitted coefficient comparable to WeightEye."""
    X = X.copy()
    if stats is None:
        stats = {c: (X[c].mean(), X[c].std() or 1.0) for c in cols if c in X}
    for c, (mu, sd) in stats.items():
        X[c] = (X[c] - mu) / sd
    return X, stats


def oof_predict(X, y, w, t0, t1, C, n_splits, embargo):
    """Out-of-fold probabilities under purged walk-forward. NaN where never tested."""
    p = np.full(len(y), np.nan)
    for tr, te in cv.purged_walk_forward(t0, t1, n_splits=n_splits, embargo=embargo):
        Xtr, stats = standardize(X.iloc[tr], CONTINUOUS)
        Xte, _ = standardize(X.iloc[te], CONTINUOUS, stats)
        m = LogisticRegression(penalty="l2", C=C, max_iter=2000, solver="lbfgs")
        m.fit(Xtr, y[tr], sample_weight=w[tr])
        p[te] = m.predict_proba(Xte)[:, 1]
    return p


def score(y, p, w):
    ok = ~np.isnan(p)
    if ok.sum() < 30 or len(np.unique(y[ok])) < 2:
        return float("nan"), float("nan")
    return (roc_auc_score(y[ok], p[ok], sample_weight=w[ok]),
            brier_score_loss(y[ok], p[ok], sample_weight=w[ok]))


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--excursions", default=os.path.join("..", "Excursions"))
    ap.add_argument("--schema", default="1.3",
                    help="corpus schema subfolder — reads <excursions>/council/<schema>/. "
                         "Bump when the recorder's SchemaVer bumps; never pool schemas.")
    ap.add_argument("--inst", required=True)
    ap.add_argument("--bartype", default=None, help="NEVER pool bartypes; pass one")
    ap.add_argument("--catalog", default=None,
                    help="shared voter catalog (default: ..\\Models\\catalog.conf, emitted by the Council). "
                         "Falls back to dataset.py's embedded copy if absent.")
    ap.add_argument("--barrier", type=float, default=20.0, help="R, in ticks")
    ap.add_argument("--cost", type=float, default=1.5, help="round-trip commission+slippage, ticks")
    ap.add_argument("--horizon", type=int, default=15, choices=[1, 5, 15, 60])
    ap.add_argument("--splits", type=int, default=5)
    ap.add_argument("--embargo-min", type=int, default=60)
    # NEVER default to the live path the Council reads. A trainer that ships an artifact
    # when it found nothing is a hazard; promotion is a deliberate act.
    ap.add_argument("--out", default=os.path.join("..", "Model.candidate.conf"))
    ap.add_argument("--promote", action="store_true",
                    help="also write ..\\Model.conf (the live path). Refused when status=reject.")
    ap.add_argument("--spend-holdout", action="store_true",
                    help="score the final untouched period. Do this ONCE, at the end.")
    a = ap.parse_args()

    embargo = pd.Timedelta(minutes=a.embargo_min)

    # ---- load ---------------------------------------------------------------------------
    # Corpus lives at Excursions\council\<schema>\ (signal-scoped + schema-versioned; one writer per folder).
    corpus_dir = os.path.join(a.excursions, "council", a.schema)
    raw = dataset.load_jsonl(corpus_dir, a.inst, a.bartype)
    df = dataset.council_rows(raw)
    lab = labels.make_labels(df, horizon_min=a.horizon)
    df = pd.concat([df, lab], axis=1)

    n_all = len(df)
    df = df[df["y"] != labels.CENSORED].reset_index(drop=True)
    has_vec = dataset.has_decision_vector(df)   # recompute AFTER the reset -- reindexing the
                                                # pre-filter mask by the new 0..n index silently
                                                # selects the wrong rows.
    if df.empty:
        raise SystemExit("every row censored -- barrier never touched. Lower --barrier.")

    y = df["y"].to_numpy().astype(int)
    w = labels.uniqueness_weights(df["t0"], df["t1"])
    n_eff = labels.effective_n(w)

    print(f"\n  {a.inst} / {a.bartype or 'ALL BARTYPES (!)'}   label={lab['mode'].iloc[0]}")
    print(f"  rows {n_all}  ->  {len(df)} resolved ({n_all - len(df)} censored, dropped)")
    # ASCII only: the Windows console is cp1252 and a stray sigma kills the whole run.
    print(f"  effective N {n_eff:.0f}  = sum(avg uniqueness)  <- the N you may quote"
          f"   [kish {labels.kish_n(w):.0f}, dispersion only]")
    print(f"  win rate {y.mean():.3f}   breakeven needs "
          f"{labels.breakeven_probability(a.barrier, a.cost):.3f}")
    print(f"  schema 1.3 decision vector on {int(has_vec.sum())}/{len(df)} rows")

    if n_eff < 200:
        print("\n  ** effective N is small. Treat everything below as directional, not decisive. **")

    conv = dataset.conviction(df).fillna(0.0).to_numpy()

    # ---- Phase 1: CALIBRATION (schema 1.2 is enough) -------------------------------------
    print("\n--- Phase 1: calibration -------------------------------------------------")
    p_oof = np.full(len(y), np.nan)
    for tr, te in cv.purged_walk_forward(df["t0"], df["t1"], a.splits, embargo):
        platt = LogisticRegression(max_iter=1000)
        platt.fit(conv[tr].reshape(-1, 1), y[tr], sample_weight=w[tr])
        p_oof[te] = platt.predict_proba(conv[te].reshape(-1, 1))[:, 1]

    auc_c, brier_c = score(y, p_oof, w)
    print(f"  conviction -> P(win)   OOF auc {auc_c:.3f}   brier {brier_c:.3f}")

    ok = ~np.isnan(p_oof)
    floor = labels.expectancy_floor(conv[ok], p_oof[ok], a.barrier, a.cost)
    if np.isnan(floor):
        print("  !! NO conviction level clears breakeven after cost.")
        print("     That is a real answer: at this barrier and cost, the Council does not pay.")
    else:
        print(f"  learned conviction floor {floor:.3f}   (hand-set: 0.350)")

    iso = IsotonicRegression(out_of_bounds="clip").fit(conv[ok], y[ok], sample_weight=w[ok])
    grid = np.linspace(0.1, 0.95, 8)
    print("  IN-SAMPLE isotonic shape (overfit; for eyeballing only, NOT evidence): " +
          "  ".join(f"{c:.2f}->{p:.2f}" for c, p in zip(grid, iso.predict(grid))))
    if not np.isnan(auc_c) and auc_c < 0.52:
        print("  ^^ the OOF auc above says conviction does NOT predict. Believe the OOF, not the curve.")

    final_platt = LogisticRegression(max_iter=1000).fit(conv.reshape(-1, 1), y, sample_weight=w)
    calib_a = float(final_platt.coef_[0][0])
    calib_b = float(final_platt.intercept_[0])

    # ---- Phase 2: WEIGHTS (needs schema 1.3) ---------------------------------------------
    learned = None
    auc_m = brier_m = float("nan")
    # The hand-set weights ARE the conviction ranking -- conviction = |sum w.v| / sum w.
    # Scored on ALL resolved rows here, but re-scored on the decision-vector subset inside
    # Phase 2, because comparing a model fitted on subset S against a baseline measured on
    # a superset of S is not a comparison at all.
    auc_base, _ = score(y, conv, w)

    if has_vec.sum() < 150:
        print("\n--- Phase 2: weights ------------------------------------------------------")
        print(f"  SKIPPED -- only {int(has_vec.sum())} rows carry the decision vector.")
        print("  Ship the schema-1.3 instrumentation (Docs/SENTINEL_ML_SPEC.md #2), then wait.")
    else:
        print("\n--- Phase 2: weights ------------------------------------------------------")
        d = df[has_vec.to_numpy()].reset_index(drop=True)
        yv = d["y"].to_numpy().astype(int)
        wv = labels.uniqueness_weights(d["t0"], d["t1"])

        # DATA-DRIVEN voter set: this bartype's recorded roster, not a fixed list. Per-bartype
        # rosters range 2..18 voters, so fitting a fixed 10 (or 22) would drop real voters or
        # fit all-zero columns. active_voter_tags admits catalog voters present above support.
        catalog = dataset.load_catalog(a.catalog)
        tags, rep = dataset.active_voter_tags(d, catalog)
        base_w = recorded_baseline_weights(d, tags, catalog)
        print(f"  voter set (data-driven): fitting {len(tags)} of "
              f"{len(dataset.catalog_voter_tags(catalog))} catalog voters"
              f"  [{rep['n_vec']} vector rows, support thresh {rep['thresh']}]")
        print(f"    fit: {' '.join(tags)}")
        if rep["thin"]:
            print("    thin  (excluded, under-supported): "
                  + ", ".join(f"{t}({c})" for t, c in rep["thin"]))
        if rep["undeclared"]:
            print("    !! UNDECLARED (in corpus, NOT in catalog — catalog is stale): "
                  + ", ".join(f"{t}({c})" for t, c in rep["undeclared"]))
        X = dataset.build_features(d, tags)

        # like-for-like: baseline re-measured on the SAME rows the model is fitted on
        conv_v = dataset.conviction(d).fillna(0.0).to_numpy()
        auc_base, _ = score(yv, conv_v, wv)
        print(f"  baseline (recorded voteW, ranked by conviction)  auc {auc_base:.3f}"
              f"   [n={len(d)}]")

        best = (-1.0, None)
        for C in C_GRID:
            p = oof_predict(X, yv, wv, d["t0"], d["t1"], C, a.splits, embargo)
            auc, _ = score(yv, p, wv)
            print(f"    C={C:<5}  OOF auc {auc:.3f}")
            if auc > best[0]:
                best = (auc, C)
        auc_m, C_best = best
        print(f"  best C={C_best}  auc {auc_m:.3f}   (baseline {auc_base:.3f})")
        print("  NOTE: 6 values of C were tried. That grid burned significance -- the")
        print("        holdout is the only clean number left.")

        p_best = oof_predict(X, yv, wv, d["t0"], d["t1"], C_best, a.splits, embargo)
        _, brier_m = score(yv, p_best, wv)

        Xs, _ = standardize(X, CONTINUOUS)
        final = LogisticRegression(penalty="l2", C=C_best, max_iter=2000).fit(Xs, yv, sample_weight=wv)
        coef = pd.Series(final.coef_[0], index=X.columns)

        raw_w = {t: float(coef.get(f"v_{t}", 0.0)) for t in tags}
        # Conviction is scale-invariant (|sum w.v| / sum |w|), so rescale to the current
        # total for a like-for-like conviction distribution -- and a transferable floor.
        scale = sum(abs(v) for v in base_w.values()) / max(
            sum(abs(v) for v in raw_w.values()), 1e-9)
        learned = {t: v * scale for t, v in raw_w.items()}

        print("\n  voter           recorded  learned   delta")
        for t in tags:
            h, l = base_w[t], learned[t]
            flag = "  <-- CONTRARIAN" if l < -0.05 else ("  <-- dead weight" if abs(l) < 0.10 else "")
            print(f"  {t:<12} {h:>6.2f}  {l:>+7.2f}  {l - h:>+6.2f}{flag}")

        print("\n  modulators (orthogonal axes):")
        for c in [c for c in coef.index if not c.startswith("v_")]:
            print(f"    {c:<16} {coef[c]:>+7.3f}")

        if any(v < -0.05 for v in learned.values()):
            print("\n  ** A NEGATIVE weight means that voter agreeing is BAD news (contrarian).")
            print("     Council_v1_0_0's AddVote assumes weight >= 0 (activeW += weight).")
            print("     To honour a negative w: vote with sign(w)*dir, accumulate abs(w) into")
            print("     activeW. Amend the spec before wiring Phase 3.")

    # ---- final holdout -------------------------------------------------------------------
    if a.spend_holdout:
        dev, hold = cv.holdout_split(df, frac=0.2)
        print(f"\n--- HOLDOUT (spend once) --  dev {len(dev)}  hold {len(hold)} ------------")
        m = LogisticRegression(max_iter=1000).fit(
            conv[dev].reshape(-1, 1), y[dev], sample_weight=w[dev])
        ph = m.predict_proba(conv[hold].reshape(-1, 1))[:, 1]
        auc_h, brier_h = score(y[hold], ph, w[hold])
        print(f"  calibration on untouched period:  auc {auc_h:.3f}  brier {brier_h:.3f}")

    # ---- emit the candidate ---------------------------------------------------------------
    # A model that does not beat the hand-set weights, or that has no profitable conviction
    # level, is REJECTED. The file is still written (for the record) but marked, and the
    # Council must refuse any file whose status != ok.
    beat_baseline = (not np.isnan(auc_m)) and auc_m > auc_base + 0.01
    has_floor     = not np.isnan(floor)
    status = "ok" if (has_floor and (beat_baseline or learned is None)) else "reject"
    reasons = []
    if not has_floor:                          reasons.append("no profitable conviction level")
    if learned is not None and not beat_baseline: reasons.append("did not beat hand-set weights")

    now = dt.datetime.now(dt.timezone.utc)
    lines = [
        "# Sentinel Model.conf -- emitted by Lab/train.py. Flat key=value: no JSON parser in C#.",
        "# The Council must FAIL OPEN to its hand-set weights if any guard below fails.",
        f"status={status}" + (("   # " + "; ".join(reasons)) if reasons else ""),
        "schema=1",
        f"trainedUtc={now.strftime('%Y-%m-%dT%H:%M:%SZ')}",
        f"expiresUtc={(now + dt.timedelta(days=30)).strftime('%Y-%m-%dT%H:%M:%SZ')}",
        f"instrument={a.inst}",
        f"bartype={a.bartype or 'UNSPECIFIED'}",
        f"label={lab['mode'].iloc[0]}",
        f"barrierTicks={a.barrier}",
        f"costTicks={a.cost}",
        f"nSamples={len(df)}",
        f"nEffective={n_eff:.0f}",
        f"auc={auc_m if not np.isnan(auc_m) else auc_c:.4f}",
        f"brier={brier_m if not np.isnan(brier_m) else brier_c:.4f}",
        f"baselineAuc={auc_base:.4f}",
        "",
        "# calibration: P(win) = 1 / (1 + exp(-(calib.a * conviction + calib.b)))",
        f"calib.a={calib_a:.4f}",
        f"calib.b={calib_b:.4f}",
        f"calib.floor={floor:.4f}" if not np.isnan(floor) else "calib.floor=nan",
        "",
    ]
    if learned:
        lines.append("# fitted voter weights -- replace Council WeightXxx when UseLearnedWeights=ON")
        lines += [f"w.{t}={learned[t]:.4f}" for t in learned]   # data-driven fit set, catalog order
    else:
        lines.append("# no decision vector yet -- catalog default weights retained (calibration only)")
        _cat_w = dataset.catalog_weights()
        lines += [f"w.{t}={_cat_w[t]:.4f}" for t in dataset.catalog_voter_tags()]

    with open(a.out, "w", encoding="utf-8") as fh:
        fh.write("\n".join(lines) + "\n")
    print(f"\n  status={status.upper()}" + (("  (" + "; ".join(reasons) + ")") if reasons else ""))
    print(f"  wrote candidate  {os.path.abspath(a.out)}")

    if a.promote:
        if status != "ok":
            print("  REFUSED to promote: status=reject. The live Model.conf is untouched.")
        else:
            live = os.path.join(os.path.dirname(a.out) or ".", "Model.conf")
            with open(live, "w", encoding="utf-8") as fh:
                fh.write("\n".join(lines) + "\n")
            print(f"  PROMOTED to      {os.path.abspath(live)}")
    else:
        print("  (not promoted. Re-run with --promote to write the live Model.conf.)")

    if not np.isnan(auc_m) and auc_m <= auc_base + 0.01:
        print("\n  VERDICT: ridge did NOT beat the hand-set weights on a purged split.")
        print("  That is a legitimate result. Keep UseLearnedWeights=OFF. The coefficient")
        print("  ranking above still tells you which sensors are dead weight.")


if __name__ == "__main__":
    main()
