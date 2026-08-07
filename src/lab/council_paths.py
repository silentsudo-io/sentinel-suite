#!/usr/bin/env python3
"""
Council PATH-QUALITY analysis - does conviction buy better-SHAPED trades?

The redirection thesis (memory sentinel-ml-lab): "conviction is noise" (OOF AUC ~0.477) was
computed against a COARSE win/loss binary that discards the trade PATH. Two fires with the same
first-touch label can have opposite shapes - clean run vs chop, heat-before-it-worked, quick vs
slow to target, how much of the peak was given back. If higher conviction buys better-SHAPED
paths even at a similar win rate, that's real edge the binary can't see. This measures the path.

Reads Sentinel\\Lab\\db\\sentinel.db (source='council', populated by ingest\\ingest.py from the
Recorder's council\\ticks\\ sidecars). Computes per-fire path features from the raw tick path,
then relates CONVICTION to them, PER scope/bartype (never pool bartypes - the Council's edge is
bar-type-dependent; memory sentinel-ml-lab).

    python council_paths.py                              # every council fire, grouped by scope
    python council_paths.py --inst GC --bartype 212201v6x24
    python council_paths.py --scope GC.2016v2x8          # one scope
    python council_paths.py --buckets quantile           # 4 conviction quantiles instead of LOW/MID/HIGH

Run via the Lab venv (has numpy/pandas): Sentinel\\Lab\\.venv\\Scripts\\python.exe council_paths.py
"""
from __future__ import annotations
import os, sqlite3, argparse, glob, json
import numpy as np
import pandas as pd
from lab_faults import swallow

HERE = os.path.dirname(os.path.abspath(__file__))
DB   = os.path.join(HERE, "db", "sentinel.db")
SENT = os.path.abspath(os.path.join(HERE, ".."))
C13  = os.path.join(SENT, "Excursions", "council", "1.3")   # the vote-vector lives in the row corpus, not the sidecars


def load(con, where_sql, params):
    trades = pd.read_sql_query(
        f"SELECT * FROM trades WHERE source='council' {where_sql} ORDER BY entry_utc", con, params=params)
    if trades.empty:
        return trades, pd.DataFrame()
    ids = tuple(trades["trade_id"].tolist())
    ph = ",".join("?" for _ in ids)
    ticks = pd.read_sql_query(
        f"SELECT trade_id, ms, px, fav_t FROM ticks WHERE trade_id IN ({ph}) ORDER BY trade_id, ms",
        con, params=ids)
    return trades, ticks


def load_votes() -> dict:
    """fireTime -> {tag: dir} from the council ROW corpus (schema 1.3). The per-voter vote vector lives in the
    rows, not the tick sidecars, so #3's per-voter view joins here on fireTime (exact ISO, 1:1 per fire)."""
    out = {}
    for p in glob.glob(os.path.join(C13, "*.jsonl")):   # non-recursive: skips _archive / _exp subdirs
        try:
            with open(p, encoding="utf-8") as fh:
                for line in fh:
                    line = line.strip()
                    if not line:
                        continue
                    d = json.loads(line)
                    ft, v = d.get("fireTime"), d.get("votes")
                    if ft and isinstance(v, dict):
                        out[ft] = v
        except (OSError, json.JSONDecodeError) as _swex:
            swallow("council_paths.load_votes", _swex)
    return out


def report_voters(t: pd.DataFrame):
    """Per-voter: when a voter agrees with the fire direction, are the resulting paths better-SHAPED? The vote
    vector is the input the Council fused; this is the seed of weight-fitting (which voters buy clean paths)."""
    if "votes" not in t.columns:
        return
    rows = t[t["votes"].apply(lambda v: isinstance(v, dict) and len(v) > 0)]
    if rows.empty:
        print("\n  (no vote vectors joined - the row corpus has no matching fireTimes for these fires)")
        return
    tags = sorted({tag for v in rows["votes"] for tag in v})
    if len(tags) < 2:
        print(f"\n  per-voter: only 1 voter present ({tags}) - need a multi-voter chart for the per-voter view.")
        return
    print(f"\n  per-voter shape (voter AGREES with fire dir vs not) - {len(rows)} fires w/ votes, {len(tags)} voters:")
    print(f"  {'voter':<7}{'agree_n':>8}{'agree_win%':>11}{'agree_MFE/MAE':>14}{'agree_dwell':>12}"
          f"{'other_win%':>11}{'other_MFE/MAE':>14}")
    for tag in tags:
        ag = rows[rows.apply(lambda r: np.sign(r["votes"].get(tag, 0)) == np.sign(r["dir"]) and r["votes"].get(tag, 0) != 0, axis=1)]
        ot = rows[rows.apply(lambda r: not (np.sign(r["votes"].get(tag, 0)) == np.sign(r["dir"]) and r["votes"].get(tag, 0) != 0), axis=1)]
        def _wr(g):
            res = g[g["first_touch"] != 0]
            return (res["win"].mean() * 100) if len(res) else float("nan")
        print(f"  {tag:<7}{len(ag):>8}{_wr(ag):>11.0f}{ag['mfe_mae'].median():>14.2f}{ag['run_frac'].median():>12.2f}"
              f"{_wr(ot):>11.0f}{ot['mfe_mae'].median():>14.2f}")


def path_features(fav: np.ndarray, ms: np.ndarray) -> dict:
    """Shape metrics from one fire's favorable-excursion path (ticks, ordered by ms)."""
    if fav.size == 0:
        return dict(mfe=0.0, mae=0.0, final=0.0, giveback=np.nan, efficiency=np.nan, chop=0, run_frac=np.nan)
    mfe = float(max(0.0, fav.max()))
    mae = float(max(0.0, -fav.min()))
    final = float(fav[-1])
    giveback = mfe - final                              # peak-to-end drawdown (how much of MFE was kept)
    run_frac = float((fav > 0).mean())                  # fraction of ticks favorable (robust to tick noise)

    # efficiency + chop measure MACRO shape, so downsample to ~1s (raw tick-to-tick diffs are just
    # microstructure noise: a 0.1-tick oscillation is not a "reversal"). Take the last fav of each second.
    if ms.size > 1:
        sec = ms // 1000
        idx = np.append(np.where(np.diff(sec) != 0)[0], ms.size - 1)
        ds = fav[idx]
    else:
        ds = fav
    d = np.diff(ds)
    gross = float(np.abs(d).sum())                       # total distance travelled (up+down), 1s grid
    efficiency = (abs(final) / gross) if gross > 1e-9 else np.nan   # 1.0 = straight line; ~0 = chop
    sign = np.sign(d[np.abs(d) >= 1.0])                  # only moves >= 1 full tick count as a real direction
    chop = int((np.diff(sign) != 0).sum()) if sign.size > 1 else 0  # macro direction reversals
    return dict(mfe=mfe, mae=mae, final=final, giveback=giveback,
                efficiency=efficiency, chop=chop, run_frac=run_frac)


def enrich(trades: pd.DataFrame, ticks: pd.DataFrame) -> pd.DataFrame:
    feats = {}
    for tid, g in ticks.groupby("trade_id"):
        feats[tid] = path_features(g["fav_t"].to_numpy(), g["ms"].to_numpy())
    fdf = pd.DataFrame.from_dict(feats, orient="index")
    t = trades.set_index("trade_id").join(fdf)
    t["scope"] = t["inst"].astype(str) + "." + t["bartype"].astype(str)
    t["win"] = (t["first_touch"] == 1).astype(int)          # tick-true target-first
    t["loss"] = (t["first_touch"] == -1).astype(int)        # tick-true stop-first
    t["censored"] = (t["first_touch"] == 0).astype(int)     # neither barrier by capture end
    # a single composite "path quality" score in [0,1]-ish: reward efficiency + favorable dwell + MFE/MAE, penalize giveback
    t["mfe_mae"] = t["max_fav_ticks"] / t["max_adv_ticks"].replace(0, np.nan)
    return t.reset_index()


def bucket(conv: pd.Series, mode: str) -> pd.Series:
    if mode == "quantile":
        try:
            return pd.qcut(conv, 4, labels=["Q1", "Q2", "Q3", "Q4"], duplicates="drop")
        except (ValueError, IndexError) as _swex:
            swallow("council_paths.bucket", _swex)
            return pd.Series(["all"] * len(conv), index=conv.index)
    b = pd.cut(conv, [-0.01, 0.50, 0.70, 1.01], labels=["LOW", "MID", "HIGH"])
    return b


def spearman(a: pd.Series, b: pd.Series):
    m = a.notna() & b.notna()
    if m.sum() < 3 or a[m].nunique() < 2 or b[m].nunique() < 2:
        return np.nan
    return float(a[m].rank().corr(b[m].rank()))


def report_scope(scope: str, t: pd.DataFrame, mode: str):
    n = len(t)
    print(f"\n{'='*78}\n  SCOPE {scope}   |   {n} council fires")
    print(f"{'-'*78}")
    ft = t["first_touch"]
    resolved = t[ft != 0]
    wr = (resolved["win"].mean() * 100) if len(resolved) else float("nan")
    print(f"  outcomes:  win {t['win'].sum()}  loss {t['loss'].sum()}  censored {t['censored'].sum()}"
          f"   |  resolved win-rate {wr:.0f}%   |  median barrier {t['barrier_ticks'].median():.0f}t")

    convs = t["conviction"].dropna()
    if convs.nunique() < 2:
        cval = convs.iloc[0] if len(convs) else float("nan")
        print(f"  ! conviction has no variation here (all = {cval:.2f}) - this scope can't test conviction-vs-shape.")
        print(f"    (expected for a single-voter roster like STF-only. Need a multi-voter chart for the thesis.)")
    else:
        print(f"  conviction range {convs.min():.2f}-{convs.max():.2f}   (Spearman r vs conviction, OOF-naive):")
        for col, lbl in [("win", "win"), ("mfe_mae", "MFE/MAE"), ("efficiency", "path-efficiency"),
                         ("run_frac", "favorable-dwell"), ("giveback", "giveback(-)"), ("chop", "chop(-)")]:
            r = spearman(t["conviction"], t[col])
            print(f"      r(conviction, {lbl:<16}) = {r:+.2f}" if not np.isnan(r) else f"      r(conviction, {lbl:<16}) = n/a")

    # per-bucket shape table
    t = t.copy()
    t["bucket"] = bucket(t["conviction"], mode)
    print(f"\n  {'bucket':<8}{'N':>4}{'win%':>6}{'MFE':>6}{'MAE':>6}{'MFE/MAE':>9}{'eff':>6}{'dwell':>7}{'give':>6}{'chop':>6}")
    order = ["LOW", "MID", "HIGH"] if mode != "quantile" else ["Q1", "Q2", "Q3", "Q4"]
    for bk in order:
        g = t[t["bucket"] == bk]
        if g.empty:
            continue
        res = g[g["first_touch"] != 0]
        wr = (res["win"].mean() * 100) if len(res) else float("nan")
        print(f"  {bk:<8}{len(g):>4}{wr:>6.0f}{g['mfe'].median():>6.0f}{g['mae'].median():>6.0f}"
              f"{g['mfe_mae'].median():>9.2f}{g['efficiency'].median():>6.2f}{g['run_frac'].median():>7.2f}"
              f"{g['giveback'].median():>6.0f}{g['chop'].median():>6.0f}")

    if n <= 12:
        print(f"\n  per-fire (small N - shown in full):")
        cols = ["conviction", "dir", "first_touch", "mfe", "mae", "mfe_mae", "efficiency", "run_frac", "giveback", "chop"]
        show = t[cols].copy()
        show["conviction"] = show["conviction"].round(2)
        for c in ["mfe_mae", "efficiency", "run_frac"]:
            show[c] = show[c].round(2)
        print(show.to_string(index=False))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--inst")
    ap.add_argument("--bartype")
    ap.add_argument("--scope", help="inst.bartype, e.g. GC.212201v6x24 (overrides --inst/--bartype)")
    ap.add_argument("--buckets", choices=["fixed", "quantile"], default="fixed")
    a = ap.parse_args()

    if not os.path.exists(DB):
        print(f"no DB at {DB} - run ingest\\ingest.py first."); return
    con = sqlite3.connect(DB)

    where, params = "", []
    if a.scope:
        inst, _, bt = a.scope.partition(".")
        where += " AND inst=? AND bartype=?"; params += [inst, bt]
    else:
        if a.inst:    where += " AND inst=?";    params.append(a.inst)
        if a.bartype: where += " AND bartype=?"; params.append(a.bartype)

    trades, ticks = load(con, where, params)
    if trades.empty:
        print("no council fires match - bake some Council sessions (Recorder v2.1+ writes council\\ticks\\)."); return
    t = enrich(trades, ticks)
    votes = load_votes()                                        # fireTime -> {tag: dir} from the row corpus
    t["votes"] = t["entry_utc"].map(votes)                      # 1:1 join on the exact ISO fire time

    print(f"\nCOUNCIL PATH-QUALITY  |  {len(t)} fires across {t['scope'].nunique()} scope(s)  |  db {os.path.basename(DB)}")
    print("does higher conviction buy better-SHAPED paths? (eff=path efficiency 0..1, dwell=frac ticks favorable,")
    print(" give=ticks given back from peak, chop=direction reversals - lower give/chop = cleaner)")
    for scope, g in t.groupby("scope"):
        report_scope(scope, g, a.buckets)
        report_voters(g)
    print()


if __name__ == "__main__":
    main()
