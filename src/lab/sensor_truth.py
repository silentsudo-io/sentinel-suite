#!/usr/bin/env python3
"""sensor_truth — the SENSOR TRUTH TABLE: grade every voter STANDALONE, tick-true, per bar type.

The 2026-07-22 pivot: the fused Council was scaffolding and does not survive real fills. The question
now is which individual voters carry edge on their own. This answers exactly that and nothing else.

CONSTRUCTION (the part that matters)
  Each council row is one FIRE with a direction `dir` and a tick-true `first_touch` label
  (+1 target-first, -1 stop-first, 0 neither) measured against a symmetric ATR barrier from an
  HONEST entry price (schema 1.5 — see memory firepx-is-synthetic-ha-close).
  A voter V has its own vote v in votes_json. We grade V on ITS OWN call, not on the Council's:
      V is RIGHT  when (v == dir and first_touch == +1) or (v == -dir and first_touch == -1)
      V is WRONG  when (v == dir and first_touch == -1) or (v == -dir and first_touch == +1)
  So V gets credit for being correctly CONTRARIAN on a fire that went against the Council.
  v == 0 is abstention and is excluded from V's sample entirely — absence of evidence is not
  evidence against (see memory state-vs-trigger-voters).

GUARDRAILS (each one is here because it has already burned this project)
  • PER BAR TYPE, always. Base rate by bar type dominates every voter effect [[weight-fit-findings]].
  • 70/30 TIME holdout, split per lane. An in-sample edge here means nothing; the OOF number is the
    number. pathlab already killed an in-sample "best" exit this way.
  • COST BAR. On a symmetric ±1R barrier, expR = 2p-1. GC costs ≈ 0.12R ($4 RT + 1 tick/side), so a
    voter must clear p ≈ 0.56 to be net-profitable. 52% is not an edge, it is noise with good manners.
  • SELECTION BIAS is stated, not hidden: every row is a fire the COUNCIL chose to take, so this
    measures voters conditioned on Council agreement, not on the unconditional tape.

Usage:  python sensor_truth.py [--db PATH] [--min-n 30] [--lane SUBSTR]
"""
import sqlite3, json, argparse, collections, os, math
from lab_faults import swallow

DB_DEFAULT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "db", "sentinel.db")
COST_R     = 0.12          # GC round turn + 1 tick/side slippage, expressed in R on a +-1R barrier
HOLDOUT    = 0.30          # last 30% of each lane's timeline is the test set


def wilson_lo(k, n, z=1.96):
    """Lower bound of the 95% Wilson interval — an honest floor on a small-N hit rate."""
    if n == 0:
        return 0.0
    p = k / n
    d = 1 + z * z / n
    c = p + z * z / (2 * n)
    m = z * math.sqrt(p * (1 - p) / n + z * z / (4 * n * n))
    return (c - m) / d


def load(db, lane_filter=None):
    c = sqlite3.connect("file:" + db + "?mode=ro", uri=True, timeout=30)
    c.execute("PRAGMA busy_timeout=30000")
    q = ("SELECT bartype, entry_utc, dir, first_touch, votes_json, conviction "
         "FROM trades WHERE votes_json IS NOT NULL AND first_touch IS NOT NULL AND dir IS NOT NULL")
    rows = []
    for bt, ts, d, ft, vj, conv in c.execute(q):
        if lane_filter and lane_filter not in (bt or ""):
            continue
        try:
            votes = json.loads(vj)
        except Exception as _swex:
            swallow("sensor_truth.load", _swex)
            continue
        if not isinstance(votes, dict):
            continue
        rows.append((bt, ts, int(d), int(ft), votes, conv))
    return rows


def grade(rows):
    """-> {lane: {voter: {'train': [right, total], 'test': [right, total]}}} plus lane base rates."""
    bylane = collections.defaultdict(list)
    for r in rows:
        bylane[r[0]].append(r)

    out, base = {}, {}
    for lane, rs in bylane.items():
        rs.sort(key=lambda r: r[1] or "")             # chronological — the split must be by TIME
        cut = int(len(rs) * (1 - HOLDOUT))
        tally = collections.defaultdict(lambda: {"train": [0, 0], "test": [0, 0]})
        b = {"train": [0, 0], "test": [0, 0]}
        for i, (_, _, d, ft, votes, _c) in enumerate(rs):
            part = "train" if i < cut else "test"
            if ft != 0:                                # base rate = Council's own decided win rate
                b[part][1] += 1
                if ft == 1:
                    b[part][0] += 1
            for v, val in votes.items():
                try:
                    val = int(val)
                except Exception as _swex:
                    swallow("sensor_truth.grade", _swex)
                    continue
                if val == 0 or ft == 0:                # abstention / undecided -> not V's sample
                    continue
                right = (val == d and ft == 1) or (val == -d and ft == -1)
                tally[v][part][1] += 1
                tally[v][part][0] += 1 if right else 0
        out[lane] = tally
        base[lane] = b
    return out, base


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--db", default=DB_DEFAULT)
    ap.add_argument("--min-n", type=int, default=30, help="suppress voters with a test sample below this")
    ap.add_argument("--lane", default=None, help="substring filter on bartype, e.g. AUD")
    a = ap.parse_args()

    rows = load(a.db, a.lane)
    if not rows:
        print("no rows matched — nothing to grade"); return
    tab, base = grade(rows)

    breakeven = (1 + COST_R) / 2
    print("SENSOR TRUTH TABLE   rows=%d   holdout=%d%% by time   cost=%.2fR  =>  break-even p=%.3f"
          % (len(rows), int(HOLDOUT * 100), COST_R, breakeven))
    print("A voter must clear the BREAK-EVEN column out of sample to be worth anything.\n")

    for lane in sorted(tab):
        btr, bte = base[lane]["train"], base[lane]["test"]
        print("=" * 108)
        print("LANE %s    decided: train %d / test %d    Council base rate: train %.3f  test %.3f"
              % (lane, btr[1], bte[1],
                 (btr[0] / btr[1]) if btr[1] else 0.0, (bte[0] / bte[1]) if bte[1] else 0.0))
        print("%-8s %7s %7s %7s %7s %8s %8s  %s"
              % ("voter", "n_tr", "p_tr", "n_te", "p_te", "expR_te", "wils_lo", "verdict"))
        print("-" * 108)

        scored = []
        for v, d in tab[lane].items():
            ntr, nte = d["train"][1], d["test"][1]
            if nte < a.min_n:
                continue
            ptr = d["train"][0] / ntr if ntr else 0.0
            pte = d["test"][0] / nte
            exp = 2 * pte - 1 - COST_R
            lo = wilson_lo(d["test"][0], nte)
            scored.append((exp, v, ntr, ptr, nte, pte, lo))

        for exp, v, ntr, ptr, nte, pte, lo in sorted(scored, reverse=True):
            if lo > breakeven:
                verdict = "SURVIVES (lower bound clears cost)"
            elif exp > 0:
                verdict = "positive but not significant"
            elif pte > 0.5:
                verdict = "edge < cost"
            else:
                verdict = "no edge"
            print("%-8s %7d %7.3f %7d %7.3f %8.3f %8.3f  %s"
                  % (v, ntr, ptr, nte, pte, exp, lo, verdict))
        if not scored:
            print("  (no voter reached the --min-n %d test threshold)" % a.min_n)
        print()

    print("NOTE: every row is a fire the COUNCIL chose to take, so these are voter hit rates CONDITIONED")
    print("on Council agreement — not unconditional tape edge. A voter that abstains often has a small n.")


if __name__ == "__main__":
    main()
