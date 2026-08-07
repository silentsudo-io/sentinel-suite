#!/usr/bin/env python3
"""
Compare the Council's decisions ACROSS bar types on one instrument.

Answers the operator question "how much does bar type matter?" from the per-scope
excursion corpus a Market-Replay session produces (one JSONL per scope). It reuses
the Lab's own parsing/label logic (sentinel_lab.dataset + labels) so the numbers are
consistent with train.py -- this is a lens on the SAME corpus, not a second pipeline.

Three views:
  1. PER-BARTYPE -- fire count, cadence, direction balance, conviction, and the
                    first-touch WIN rate (the outcome that actually pays). eff_n is
                    the AFML concurrency-adjusted N -- trust it over the raw count.
  2. CO-FIRE     -- when two bar types fire within a tolerance window, do they AGREE
                    on direction? (decision consistency -- do they see the same call?)
  3. OVERLAP     -- how often do they fire at the same time at all? (do they even see
                    the same setups, or is each bar type its own market?)

Usage:
  python compare_bartypes.py --inst GC [--dir ../Excursions] [--tol-min 3]
                             [--since 2026-07-13] [--until 2026-07-14] [--horizon 15]

Reads only the corpus; never touches bin\\Custom.
"""
from __future__ import annotations

import argparse
import itertools
import os
import sys

import numpy as np
import pandas as pd

from sentinel_lab import dataset, labels
from lab_faults import swallow

try:
    sys.stdout.reconfigure(encoding="utf-8")   # the Windows cp1252 console chokes on the report glyphs
except Exception as _swex:
    swallow("compare_bartypes.module", _swex)

HERE = os.path.dirname(os.path.abspath(__file__))


def per_bartype(cr: pd.DataFrame, horizon_min: int) -> pd.DataFrame:
    out = []
    for bt, g in cr.groupby("bartype"):
        g = g.sort_values("fireTime").reset_index(drop=True)
        lab = labels.make_labels(g, horizon_min)
        y = lab["y"].to_numpy()
        w = labels.uniqueness_weights(lab["t0"], lab["t1"])
        conv = dataset.conviction(g).to_numpy()
        resolved = y[y != labels.CENSORED]
        span = (g["fireTime"].max() - g["fireTime"].min()).total_seconds() / 3600.0
        out.append(dict(
            bartype=str(bt),
            scope=str(g["scope"].iloc[0]) if "scope" in g.columns else "-",
            n=len(g),
            fires_per_h=round(len(g) / span, 2) if span and span > 0 else float("nan"),
            pct_long=round(100 * float(np.mean(g["dir"] > 0)), 1),
            conv_med=round(float(np.median(conv)), 3) if len(conv) else float("nan"),
            conv_mean=round(float(np.mean(conv)), 3) if len(conv) else float("nan"),
            resolved=int(len(resolved)),
            win_pct=round(100 * float(np.mean(resolved == labels.WIN)), 1) if len(resolved) else float("nan"),
            censored=int(np.sum(y == labels.CENSORED)),
            eff_n=round(labels.effective_n(w), 1),
        ))
    return pd.DataFrame(out).sort_values("bartype").reset_index(drop=True)


def cofire(cr: pd.DataFrame, tol_min: int) -> pd.DataFrame:
    tol = pd.Timedelta(minutes=tol_min)
    bts = sorted(cr["bartype"].astype(str).unique())
    out = []
    for a, b in itertools.combinations(bts, 2):
        ga = cr[cr["bartype"].astype(str) == a][["fireTime", "dir"]].sort_values("fireTime")
        gb = cr[cr["bartype"].astype(str) == b][["fireTime", "dir"]].sort_values("fireTime")
        if ga.empty or gb.empty:
            continue
        m = pd.merge_asof(ga, gb, on="fireTime", direction="nearest",
                          tolerance=tol, suffixes=("_a", "_b"))
        paired = m.dropna(subset=["dir_b"])
        n = len(paired)
        agree = round(100 * float(np.mean(paired["dir_a"] == paired["dir_b"])), 1) if n else float("nan")
        opp = round(100 * float(np.mean(paired["dir_a"] == -paired["dir_b"])), 1) if n else float("nan")
        out.append(dict(pair=f"{a}  vs  {b}", co_fires=n,
                        a_covered=f"{n}/{len(ga)}", agree_pct=agree, opposite_pct=opp))
    return pd.DataFrame(out)


def overlap(cr: pd.DataFrame, tol_min: int) -> pd.DataFrame:
    """For each ordered pair (A->B): what fraction of A's fires have ANY B fire within tol."""
    tol = pd.Timedelta(minutes=tol_min)
    bts = sorted(cr["bartype"].astype(str).unique())
    out = []
    for a, b in itertools.permutations(bts, 2):
        ga = cr[cr["bartype"].astype(str) == a][["fireTime", "dir"]].sort_values("fireTime")
        gb = cr[cr["bartype"].astype(str) == b][["fireTime", "dir"]].sort_values("fireTime")
        if ga.empty or gb.empty:
            continue
        m = pd.merge_asof(ga, gb, on="fireTime", direction="nearest", tolerance=tol)
        cov = round(100 * float(m["dir_y"].notna().mean()), 1) if "dir_y" in m else \
              round(100 * float(m.iloc[:, -1].notna().mean()), 1)
        out.append(dict(**{"A fires": a, "has a nearby B": b, "coverage_pct": cov}))
    return pd.DataFrame(out)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--inst", default="GC")
    ap.add_argument("--dir", default=os.path.join(HERE, "..", "Excursions"))
    ap.add_argument("--signal", default="COUNCIL",
                    help="which recorded signal to compare (COUNCIL = the Council verdict; the replay must run "
                         "Excursion Recorder v2). Pass e.g. OBR/FC/BG to compare a GodTrades signal instead.")
    ap.add_argument("--tol-min", type=int, default=3, help="co-fire alignment window (minutes)")
    ap.add_argument("--horizon", type=int, default=15, help="label horizon for 1.2 fallback rows")
    ap.add_argument("--since", default=None, help="keep fires on/after this UTC date (YYYY-MM-DD)")
    ap.add_argument("--until", default=None, help="keep fires before this UTC date (YYYY-MM-DD)")
    a = ap.parse_args()

    df = dataset.load_jsonl(os.path.abspath(a.dir), instrument=a.inst)
    if a.since:
        df = df[df["fireTime"] >= pd.Timestamp(a.since, tz="UTC")]
    if a.until:
        df = df[df["fireTime"] < pd.Timestamp(a.until, tz="UTC")]
    cr = df[df["signal"].astype(str) == a.signal].reset_index(drop=True)
    if cr.empty:
        import collections
        avail = dict(collections.Counter(df["signal"].astype(str)))
        raise SystemExit(
            f"no '{a.signal}' rows after filtering (inst={a.inst}). signals present: {avail}\n"
            f"  -> for the Council test, run the Council + Excursion Recorder v2 (writes signal=COUNCIL).")

    win = f"{cr['fireTime'].min()}  ->  {cr['fireTime'].max()}"
    print(f"\n=== {a.inst}  |  {len(cr)} {a.signal} fires across {cr['bartype'].nunique()} bar types  |  {win} ===\n")

    pb = per_bartype(cr, a.horizon)
    print("① PER-BARTYPE (eff_n = concurrency-adjusted; win% over RESOLVED first-touch only)")
    print(pb.to_string(index=False), "\n")

    cf = cofire(cr, a.tol_min)
    print(f"② CO-FIRE DIRECTION AGREEMENT (fires paired within +-{a.tol_min} min)")
    print((cf.to_string(index=False) if not cf.empty else "  (no co-fires in window)"), "\n")

    ov = overlap(cr, a.tol_min)
    print(f"③ SETUP OVERLAP (share of A's fires with any B fire within +-{a.tol_min} min)")
    print((ov.to_string(index=False) if not ov.empty else "  (nothing to compare)"), "\n")

    # the punchline
    if not pb["win_pct"].dropna().empty:
        lo = pb.loc[pb["win_pct"].idxmin()]
        hi = pb.loc[pb["win_pct"].idxmax()]
        spread = hi["win_pct"] - lo["win_pct"]
        print("HOW MUCH IT MATTERS:")
        print(f"  win% spans {lo['win_pct']}% ({lo['bartype']})  ->  {hi['win_pct']}% ({hi['bartype']})"
              f"  = {round(spread,1)} pts across bar types")
        if not cf.empty and cf["agree_pct"].notna().any():
            print(f"  co-firing bar types agreed on direction {round(cf['agree_pct'].mean(),1)}% of the time (avg over pairs)")
        print("  ⚠ read eff_n before trusting any win% -- one replay session is a small, overlapping sample.\n")


if __name__ == "__main__":
    main()
