#!/usr/bin/env python3
"""retest_direction — DOES ANYTHING HERE PREDICT DIRECTION? Re-run of the 2026-07-22 pivot verdict
on labels that are actually correct.

WHY THIS EXISTS
---------------
The pivot measured 3,777 fires and concluded the Council loses and no sensor survives standalone. That
verdict was measured with a broken ruler: schema<=1.4 priced entries at a synthetic Heikin-Ashi close
that never traded, and `sentinel.db.first_touch` goes blind after 5 minutes
(`TickPathMaxMs=300000`) while the label horizon is ~60 min. So the sensors were never actually
cleared or convicted -- they were measured badly. This re-runs the question on:

  population A  ROW labels, both audition legs, 19-voter roster, schema 1.5 (n~2,965)
                -- bar-derived but externally validated at 98.4% by EXP-0005
  population B  HARNESS labels, GC 08-26 only (n~813)
                -- recomputed from raw tape, full horizon, no 5-min cap, no synthetic price

Agreement between A and B is the robustness check. A result that appears in one and not the other is
not a result.

THE ARITHMETIC
--------------
Barriers are SYMMETRIC (`firePx +- barrierTicks`), so a coin flip gives 50% and expectancy per resolved
fire is simply `barrier * (2p - 1)` ticks, before cost. `COST_TICKS` charges the crossing, matching
observatory.py, because a gross edge smaller than the spread is not an edge.

`firstTouch=+1` means the FAVORABLE barrier was hit first (recorder line 769) = the trade worked.

HONEST n
--------
94% of fires start inside the prior fire's 60-min horizon, so fires are NOT independent -- their
forward tape overlaps ~9 ways. Every interval here is a **day-block bootstrap** (resample whole days
with replacement), which respects that clustering. A naive binomial CI would be ~3x too narrow and is
deliberately not offered.

MULTIPLE COMPARISONS
--------------------
19 voters are scored. Testing 19 things at 95% means ~1 looks significant by luck. The per-voter table
prints a Bonferroni-adjusted interval alongside the raw one; read the adjusted one.
"""
from __future__ import annotations

import argparse
import bisect
import json
import os
import random
import sys
from collections import defaultdict

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from .exp0005 import (DAY_HI, DAY_LO, LANE, ROWS, TICK, Fire, _covered, _iso_ns,  # noqa: E402
                      harness_first_touch, load_fires, load_tape)

try:
    sys.stdout.reconfigure(encoding="utf-8")
except (AttributeError, OSError):
    pass

LEGS = [("AUD0626", "20260725T150115__GC__212201v6x24.jsonl"),
        ("AUD0826", "20260725T234907__GC__212201v6x24.jsonl")]
COST_TICKS = 2.0          # crossing cost, same charge observatory.py applies
BOOT = 2000
SEED = 20260726


class Rec:
    __slots__ = ("day", "dir", "ft", "barrier", "votes")

    def __init__(self, day, d, ft, barrier, votes):
        self.day, self.dir, self.ft, self.barrier, self.votes = day, d, ft, barrier, votes

    @property
    def outcome_dir(self):
        """The direction that actually paid inside the barrier, or None if unresolved."""
        if self.ft == 1:
            return self.dir
        if self.ft == -1:
            return -self.dir
        return None


def load_rows(legs=LEGS):
    out = []
    for _lane, fn in legs:
        with open(os.path.join(ROWS, fn), encoding="utf-8") as fh:
            for line in fh:
                line = line.strip()
                if not line:
                    continue
                o = json.loads(line)
                v = o.get("votes")
                if not isinstance(v, dict) or not o.get("dir") or not o.get("barrierTicks"):
                    continue
                out.append(Rec(o["fireTime"][:10], int(o["dir"]), o.get("firstTouch"),
                               float(o["barrierTicks"]), v))
    return out


def load_harness_labelled():
    """Population B: the same fires, relabelled from raw tape over the row's own horizon."""
    ts, px = load_tape(verbose=False)
    fires = load_fires(verbose=False)
    fires, _, _ = _covered(fires, ts)
    byft = {}
    for f in fires:
        h = harness_first_touch(f, ts, px)
        if h is not None:
            byft[f.ns] = h
    out = []
    with open(os.path.join(ROWS, LEGS[1][1]), encoding="utf-8") as fh:
        for line in fh:
            line = line.strip()
            if not line:
                continue
            o = json.loads(line)
            ns = _iso_ns(o["fireTime"])
            if ns not in byft:
                continue
            v = o.get("votes")
            if not isinstance(v, dict) or not o.get("dir") or not o.get("barrierTicks"):
                continue
            out.append(Rec(o["fireTime"][:10], int(o["dir"]), byft[ns],
                           float(o["barrierTicks"]), v))
    return out


# ---------------------------------------------------------------------------------------------
def _by_day(recs):
    d = defaultdict(list)
    for r in recs:
        d[r.day].append(r)
    return d


def boot_ci(recs, fn, b=BOOT, lo=2.5, hi=97.5):
    """Day-block bootstrap: resample whole DAYS, because fires within a day share forward tape."""
    days = _by_day(recs)
    keys = list(days)
    rng = random.Random(SEED)
    vals = []
    for _ in range(b):
        samp = []
        for _ in range(len(keys)):
            samp.extend(days[keys[rng.randrange(len(keys))]])
        v = fn(samp)
        if v is not None:
            vals.append(v)
    if not vals:
        return (float("nan"), float("nan"))
    vals.sort()
    return (vals[int(len(vals) * lo / 100)], vals[min(len(vals) - 1, int(len(vals) * hi / 100))])


def win_rate(recs):
    res = [r for r in recs if r.ft in (1, -1)]
    return (sum(1 for r in res if r.ft == 1) / len(res)) if res else None


def expectancy(recs):
    """Ticks per fire, net of the crossing. Unresolved fires count as 0, not as excluded."""
    if not recs:
        return None
    tot = 0.0
    for r in recs:
        if r.ft == 1:
            tot += r.barrier - COST_TICKS
        elif r.ft == -1:
            tot += -r.barrier - COST_TICKS
        else:
            tot += -COST_TICKS
    return tot / len(recs)


def voter_acc(recs, name):
    hit = n = 0
    for r in recs:
        v = r.votes.get(name, 0)
        od = r.outcome_dir
        if not v or od is None:
            continue
        n += 1
        hit += (v > 0) == (od > 0)
    return (hit / n) if n else None


def report(title, recs):
    print("\n" + "=" * 82)
    print(f"{title}   n={len(recs)} fires, {len(_by_day(recs))} days")
    print("=" * 82)
    res = [r for r in recs if r.ft in (1, -1)]
    nores = len(recs) - len(res)
    wr = win_rate(recs)
    lo, hi = boot_ci(recs, win_rate)
    ex = expectancy(recs)
    elo, ehi = boot_ci(recs, expectancy)
    med_b = sorted(r.barrier for r in recs)[len(recs) // 2]
    print(f"  resolved {len(res)}   unresolved {nores}   median barrier {med_b:.1f} ticks")
    print(f"\n  COUNCIL DIRECTION")
    print(f"    target-first rate : {100*wr:.2f}%   95% CI [{100*lo:.2f}, {100*hi:.2f}]   "
          f"(coin flip = 50.00%)")
    edge = "EDGE" if lo > 0.50 else ("ANTI-EDGE" if hi < 0.50 else "no detectable edge")
    print(f"    verdict           : {edge}")
    print(f"    expectancy/fire   : {ex:+.2f} ticks net of {COST_TICKS} tick cost   "
          f"95% CI [{elo:+.2f}, {ehi:+.2f}]")

    # time holdout: first 70% of days train-equivalent, last 30% held out
    days = sorted(_by_day(recs))
    cut = int(len(days) * 0.7)
    early = [r for r in recs if r.day in set(days[:cut])]
    late = [r for r in recs if r.day in set(days[cut:])]
    if early and late:
        print(f"    holdout           : first {cut}d {100*win_rate(early):.2f}%  |  "
              f"last {len(days)-cut}d {100*win_rate(late):.2f}%")

    names = sorted({k for r in recs for k in r.votes})
    print(f"\n  PER-VOTER STANDALONE  ({len(names)} voters; Bonferroni-adjusted CI is the one to read)")
    adj = 2.5 / len(names)
    print(f"    {'voter':<7}{'n':>7}{'acc%':>9}{'95% CI':>18}{'Bonf CI':>20}   verdict")
    rows = []
    for nm in names:
        a = voter_acc(recs, nm)
        if a is None:
            continue
        n = sum(1 for r in recs if r.votes.get(nm, 0) and r.outcome_dir is not None)
        l1, h1 = boot_ci(recs, lambda s, _n=nm: voter_acc(s, _n))
        l2, h2 = boot_ci(recs, lambda s, _n=nm: voter_acc(s, _n), lo=adj, hi=100 - adj)
        rows.append((a, nm, n, l1, h1, l2, h2))
    for a, nm, n, l1, h1, l2, h2 in sorted(rows, reverse=True):
        v = "EDGE" if l2 > 0.50 else ("ANTI" if h2 < 0.50 else "-")
        print(f"    {nm:<7}{n:>7}{100*a:>8.2f}%   [{100*l1:>5.1f},{100*h1:>5.1f}]"
              f"     [{100*l2:>5.1f},{100*h2:>5.1f}]   {v}")
    surv = [r for r in rows if r[5] > 0.50]
    print(f"\n  => voters clearing 50% after multiple-comparison adjustment: "
          f"{len(surv)}/{len(rows)}" + (f"  ({', '.join(r[1] for r in surv)})" if surv else ""))
    return wr, ex, len(surv)


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(prog="harness.retest_direction")
    ap.add_argument("--skip-harness", action="store_true")
    ap.parse_args(argv)

    a = load_rows()
    ra = report("POPULATION A — ROW labels, both legs (validated 98.4% by EXP-0005)", a)
    rb = None
    if not ap.parse_args(argv).skip_harness:
        b = load_harness_labelled()
        if b:
            rb = report("POPULATION B — HARNESS labels, GC 08-26, raw tape, full horizon, no cap", b)

    print("\n" + "=" * 82)
    print("VERDICT")
    print("=" * 82)
    print(f"  A  target-first {100*ra[0]:.2f}%   expectancy {ra[1]:+.2f} t/fire   "
          f"voters with edge: {ra[2]}")
    if rb:
        print(f"  B  target-first {100*rb[0]:.2f}%   expectancy {rb[1]:+.2f} t/fire   "
              f"voters with edge: {rb[2]}")
        print("\n  A and B are different labellings of overlapping fires. They should agree; where")
        print("  they do not, believe B (raw tape, full horizon) and distrust the conclusion.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
