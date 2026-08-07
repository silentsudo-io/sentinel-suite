#!/usr/bin/env python3
"""Model-free voter edge — is a voter CONFIRMING or CONTRARIAN, and on how much data?

    python voter_edge.py --inst GC --bartypes 212201v6x24 0v150x1 9v1x1

The ridge weights in train.py are confounded by heavy collinearity among the price-derived
voters, so a negative coefficient can be an artifact of another voter stealing the credit.
This looks at each voter UNIVARIATELY: fold x = vote*dir (+1 = agreed with the taken side),
then the uniqueness-weighted first-touch win rate when it AGREED vs DISAGREED. A real
confirming voter: wr(agree) > base > wr(disagree). Contrarian: the reverse. n_agree /
n_disagree is the data behind the claim -- a trigger that rarely fires can't be trusted, and
'(thin)' flags when either bucket is under 40. Read this BEFORE believing any learned weight.
"""
from __future__ import annotations

import argparse
import os

import numpy as np
import pandas as pd

from sentinel_lab import dataset, labels

HERE = os.path.dirname(os.path.abspath(__file__))


def wmean(y, w, mask):
    m = mask.to_numpy() if hasattr(mask, "to_numpy") else mask
    if m.sum() == 0:
        return float("nan"), 0
    return float(np.average(y[m], weights=w[m])), int(m.sum())


def edge_for(council_dir, inst, bt, barrier, cost, horizon):
    try:
        raw = dataset.load_jsonl(council_dir, inst, bt)
    except SystemExit:
        print(f"\n### {bt}: no rows"); return
    df = dataset.council_rows(raw)
    lab = labels.make_labels(df, horizon_min=horizon)
    df = pd.concat([df, lab], axis=1)
    df = df[df["y"] != labels.CENSORED].reset_index(drop=True)
    has = dataset.has_decision_vector(df)
    d = df[has.to_numpy()].reset_index(drop=True)
    if len(d) < 50:
        print(f"\n### {bt}: only {len(d)} vector rows (need >=50)"); return
    y = d["y"].to_numpy().astype(float)
    w = labels.uniqueness_weights(d["t0"], d["t1"])
    n_eff = labels.effective_n(w)
    base, _ = wmean(y, w, pd.Series(True, index=d.index))
    be = labels.breakeven_probability(barrier, cost)
    tags, _ = dataset.active_voter_tags(d, dataset.load_catalog())

    print(f"\n### {bt}   vec_rows={len(d)}  effN={n_eff:.0f}  base_wr={base:.3f}  breakeven={be:.3f}")
    print(f"    {'voter':6} {'n_agr':>6} {'wr_agr':>7} {'n_dis':>6} {'wr_dis':>7} {'lift':>7}  verdict")
    rows = []
    for t in tags:
        x = (d["votes"].apply(lambda m, t=t: float(m.get(t, 0)) if isinstance(m, dict) else 0.0)
             * d["dir"].astype(float))
        wr_a, n_a = wmean(y, w, x > 0)
        wr_d, n_d = wmean(y, w, x < 0)
        lift = (wr_a - wr_d) if (n_a and n_d) else float("nan")
        rows.append((t, n_a, wr_a, n_d, wr_d, lift))
    for t, n_a, wr_a, n_d, wr_d, lift in sorted(rows, key=lambda r: -(r[5] if r[5] == r[5] else -9)):
        verdict = ""
        if lift == lift:
            verdict = "CONFIRM" if lift > 0.03 else ("CONTRARIAN" if lift < -0.03 else "flat")
        thin = "  (thin)" if (n_a < 40 or n_d < 40) else ""
        print(f"    {t:6} {n_a:6d} {wr_a:7.3f} {n_d:6d} {wr_d:7.3f} {lift:7.3f}  {verdict}{thin}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--excursions", default=os.path.join(HERE, "..", "Excursions"))
    ap.add_argument("--schema", default="1.3")
    ap.add_argument("--inst", default="GC")
    ap.add_argument("--bartypes", nargs="+", required=True, help="one or more; NEVER pooled")
    ap.add_argument("--barrier", type=float, default=20.0)
    ap.add_argument("--cost", type=float, default=1.5)
    ap.add_argument("--horizon", type=int, default=15, choices=[1, 5, 15, 60])
    a = ap.parse_args()
    council_dir = os.path.join(a.excursions, "council", a.schema)
    for bt in a.bartypes:
        edge_for(council_dir, a.inst, bt, a.barrier, a.cost, a.horizon)


if __name__ == "__main__":
    main()
