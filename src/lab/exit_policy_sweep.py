#!/usr/bin/env python
"""exit_policy_sweep - rank EXIT POLICIES over the banked KEEL corpus, in two honest tiers.

WHY THIS EXISTS (NOW.md, user's direction 2026-07-31):
  "we will figure out a way to stop using the current stop and tp through this data ...
   I know we are smart enough to capture the profit from these entries."
  => the working hypothesis is that the ENTRIES are fine and the EXIT POLICY is what loses.
  D2 (61% of stop-outs had the peak still ahead) is the evidence for exactly that.

WHY IT IS NOT JUST "SIMULATE THE POLICIES":
  The corpus CANNOT resolve most of the interesting policy space, and a sweep that quietly
  returns a number for a policy it cannot resolve is the fifth instance of the pattern named in
  NOW.md -- a truncated measurement that presents as a plausible NUMBER rather than an error.
  Measured on the bake cohort (n=1435) before this tool was written:

      tick paths : p50 max-favorable 1.01R   only 31% ever reach +1.25R   6% reach +2R
      row 60-min : p50 MFE           3.40R        69% reach +2R          33% reach +5R
      1125 of 1372 trades (82%) have their true 60-min MFE OUTSIDE the tick path

  Cause: SentinelExcursionRecorder v2.4.0 releases the tick buffer TickPathTailMs (30s) after
  the FIRST +-1R barrier touch -- a RESOLUTION recorder, not a WINDOW recorder. So the tick
  corpus is blind above ~1R, which is where every interesting target lives.

THE TWO TIERS, NEVER BLENDED:
  TIER 1  TICK-TRUE   stop <= 1R and target <= 1R. The whole policy resolves inside the tick
                      path. Walked tick by tick. A real expectancy.
  TIER 2  BAR-BOUNDED anything wider. Resolved from the row milestone ENVELOPES (mfe/mae at
                      1/5/15/60 min). Touch ORDER is only knowable when one side is satisfied
                      at a horizon and the other is not, so each trade lands in
                      DEFINITE-TARGET / DEFINITE-STOP / AMBIGUOUS / OPEN, and the result is a
                      [lower, upper] BOUND with the ambiguous fraction stated out loud.

  A Tier-2 row with a wide bound is not a ranking. It is the corpus saying "ask me again after
  the D2 bake" (tail ~= 3600000 ms, Recorder v2.5.0).

DATA PATH: sentinel.db, READ-ONLY (the ingester owns it; an analyzer that could write is a
second writer waiting to happen).

R UNIT: R = `barrier_ticks`, the recorder's own ATR barrier resolved at fire. `barsToStopR` /
`barsToTargetR` are first-touch of exactly -+1 x barrier_ticks (recorder v2_0_0.cs:413-414),
-1 = never. Everything here is denominated in that same R so the sim and the corpus agree by
construction.

COST: commission is REAL and these expectancies are small. $4.36/RT on NQ at $5.00/tick =
0.872 ticks round turn, which is 0.872/barrier_ticks in R -- a different R-cost per trade,
because the barrier is ATR-scaled. Applied per trade, never as a flat R constant.
"""
from __future__ import annotations
import os, sys, json, sqlite3, argparse

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from lab_faults import swallow  # noqa: E402

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception as e:  # noqa: BLE001 - reported, never silent
    swallow("exit_policy_sweep.stdout_reconfigure", e)

DB = os.path.join(os.path.dirname(os.path.abspath(__file__)), "db", "sentinel.db")
HORIZONS = (1, 5, 15, 60)          # minutes, the only milestones the recorder writes


def connect():
    return sqlite3.connect(f"file:{DB.replace(os.sep, '/')}?mode=ro", uri=True, timeout=30)


# ----------------------------------------------------------------- loading
def load_trades(c, signal, since, inst=None, bartype=None):
    q = ["SELECT trade_id,inst,bartype,bar_label,dir,entry_utc,barrier_ticks,dur_s,"
         "milestones_json,end_reason,rec_ver FROM trades WHERE signal=? AND barrier_ticks>0"]
    args = [signal]
    if since:
        q.append("AND entry_utc>=?"); args.append(since)
    if inst:
        q.append("AND inst=?"); args.append(inst)
    if bartype:
        q.append("AND bartype=?"); args.append(bartype)
    q.append("ORDER BY entry_utc")
    cur = c.execute(" ".join(q), args)
    cols = [d[0] for d in cur.description]
    out = []
    for r in cur.fetchall():
        t = dict(zip(cols, r))
        t["ms"] = {}
        if t["milestones_json"]:
            try:
                t["ms"] = json.loads(t["milestones_json"])
            except Exception as e:  # noqa: BLE001
                swallow("exit_policy_sweep.milestones", e, detail=t["trade_id"])
        out.append(t)
    return out


def load_paths(c, signal, since):
    """All tick paths for the cohort in ONE query. fav_t is already dir-signed by the recorder."""
    q = ("SELECT k.trade_id,k.ms,k.fav_t FROM ticks k JOIN trades t ON t.trade_id=k.trade_id "
         "WHERE t.signal=?")
    args = [signal]
    if since:
        q += " AND t.entry_utc>=?"; args.append(since)
    q += " ORDER BY k.trade_id,k.ms"
    paths = {}
    for tid, ms, fav in c.execute(q, args):
        paths.setdefault(tid, []).append((ms, fav))
    return paths


# ----------------------------------------------------------------- TIER 1: tick-true
def sim_tick(path, B, stop_r, tgt_r, arm_r=None, be_r=0.0, trail_r=None):
    """Walk the recorded ticks. Exit AT the level touched; no intrabar path is invented.

    Returns (R, why). why='open' means the policy had NOT exited when the recorder released
    the buffer -- which is NOT the same as a timeout at the end of the trade, and the caller
    must not treat it as a realised result.
    """
    if not path:
        return None, "nopath"
    peak = 0.0
    for _ms, fav in path:
        peak = max(peak, fav)
        r = fav / B
        stop = -stop_r
        if arm_r is not None and peak / B >= arm_r:
            stop = max(stop, be_r)
        if trail_r is not None and peak / B >= trail_r:
            stop = max(stop, peak / B - trail_r)
        if tgt_r is not None and r >= tgt_r:
            return tgt_r, "target"
        if r <= stop:
            return stop, "stop"
    return None, "open"


# ----------------------------------------------------------------- TIER 2: bar-bounded
def classify_bar(ms, B, stop_r, tgt_r):
    """Resolve a bracket from the milestone ENVELOPES alone.

    The only order information the envelopes carry: if at horizon T one side is satisfied and
    the other is not, that side was touched FIRST. Use the EARLIEST horizon that separates
    them. If no horizon separates them, the order is genuinely unknown -> AMBIGUOUS, and we
    return a bound, not a number.
    """
    if not ms:
        return "nodata", None, None
    for h in HORIZONS:
        f, a = ms.get(f"mfe{h}"), ms.get(f"mae{h}")
        if f is None or a is None:
            continue
        hit_t = (tgt_r is not None and f / B >= tgt_r)
        hit_s = (a / B >= stop_r)
        if hit_t and not hit_s:
            return "target", tgt_r, tgt_r
        if hit_s and not hit_t:
            return "stop", -stop_r, -stop_r
        if hit_t and hit_s:
            return "ambiguous", -stop_r, tgt_r
    # never separated and never both -> whichever (if either) was reached by the last horizon
    f, a = ms.get("mfe60"), ms.get("mae60")
    if f is None or a is None:
        return "nodata", None, None
    hit_t = (tgt_r is not None and f / B >= tgt_r)
    hit_s = (a / B >= stop_r)
    if hit_t and not hit_s:
        return "target", tgt_r, tgt_r
    if hit_s and not hit_t:
        return "stop", -stop_r, -stop_r
    if hit_t and hit_s:
        return "ambiguous", -stop_r, tgt_r
    return "open", -a / B, f / B          # still running at the window edge; bound by the envelope


# ----------------------------------------------------------------- sweep
def sweep(trades, paths, stops, targets, cost_r_of):
    t1, t2 = [], []
    for s in stops:
        for tg in targets:
            tick_ok = (s <= 1.0 and tg is not None and tg <= 1.0)
            if tick_ok:
                tot = n = 0.0
                openn = nopath = 0
                for t in trades:
                    r, why = sim_tick(paths.get(t["trade_id"], []), t["barrier_ticks"], s, tg)
                    if why == "nopath":
                        nopath += 1; continue
                    if why == "open":
                        openn += 1; continue
                    tot += r - cost_r_of(t); n += 1
                t1.append(dict(stop=s, tgt=tg, n=int(n), exp=(tot / n if n else 0.0),
                               open=openn, nopath=nopath))
            else:
                lo = hi = 0.0
                n = amb = openc = nod = 0
                for t in trades:
                    kind, l, h = classify_bar(t["ms"], t["barrier_ticks"], s, tg)
                    if kind == "nodata":
                        nod += 1; continue
                    cst = cost_r_of(t)
                    lo += l - cst; hi += h - cst; n += 1
                    if kind == "ambiguous":
                        amb += 1
                    elif kind == "open":
                        openc += 1
                t2.append(dict(stop=s, tgt=tg, n=n, lo=(lo / n if n else 0.0),
                               hi=(hi / n if n else 0.0), amb=amb, open=openc, nodata=nod))
    return t1, t2


# ----------------------------------------------------------------- reporting
def fmt_tgt(t):
    return "hold" if t is None else f"{t:.2f}"


def report(trades, paths, t1, t2, cohort, cost_desc):
    print(f"\n{'='*84}\n  EXIT POLICY SWEEP   {cohort}   n_trades={len(trades)}\n"
          f"  cost applied per trade: {cost_desc}\n{'='*84}")
    if not trades:
        print("  no trades matched -- nothing to say. (Corpus empty for this filter.)")
        return

    # ---- coverage assertion FIRST. The analyzer states what it can answer before it answers.
    B = [t["barrier_ticks"] for t in trades]
    favR, m60 = [], []
    for t in trades:
        p = paths.get(t["trade_id"])
        if p:
            favR.append(max(f for _, f in p) / t["barrier_ticks"])
        f = t["ms"].get("mfe60")
        if f is not None:
            m60.append(f / t["barrier_ticks"])

    def q(v, p):
        v = sorted(v); return v[min(len(v) - 1, int(len(v) * p))] if v else float("nan")

    print("\n-- COVERAGE ASSERTION  (what this corpus can and cannot resolve)")
    print(f"   R unit = barrier_ticks (ATR): p50 {q(B,.5):.1f} ticks   min {min(B):.1f}   max {max(B):.1f}")
    print(f"   tick paths present : {len(favR)}/{len(trades)}   "
          f"max-favorable reach in R: p50 {q(favR,.5):.2f}  p90 {q(favR,.9):.2f}  max {max(favR):.2f}")
    print(f"   row 60-min MFE in R: p50 {q(m60,.5):.2f}  p90 {q(m60,.9):.2f}  max {max(m60):.2f}   n={len(m60)}")
    outside = sum(1 for t in trades
                  if (p := paths.get(t["trade_id"])) and t["ms"].get("mfe60") is not None
                  and t["ms"]["mfe60"] > max(f for _, f in p) + 1e-9)
    print(f"   *** {outside} of {len(trades)} trades ({100*outside/len(trades):.0f}%) have their true 60-min")
    print(f"       MFE OUTSIDE the tick path. The tick corpus is blind above ~1R by construction")
    print(f"       (TickPathTailMs = 30s past the FIRST +-1R touch). Tier 2 is therefore a BOUND.")

    print(f"\n-- TIER 1  TICK-TRUE   (stop <= 1R AND target <= 1R: the policy resolves inside the path)")
    print(f"   {'stop':>6} {'target':>7} {'n':>6} {'open':>6} {'exp R/trade':>13}")
    print("   " + "-" * 46)
    for r in sorted(t1, key=lambda z: -z["exp"]):
        print(f"   {r['stop']:>6.2f} {fmt_tgt(r['tgt']):>7} {r['n']:>6} {r['open']:>6} {r['exp']:>13.4f}")
    if t1:
        b = max(t1, key=lambda z: z["exp"])
        print(f"\n   -> best TICK-TRUE arm: stop {b['stop']:.2f}R / target {fmt_tgt(b['tgt'])}R "
              f"= {b['exp']:+.4f}R per trade over n={b['n']}")
        print(f"      {b['open']} trades never resolved inside the tick path and are EXCLUDED, not")
        print(f"      counted as scratches. This whole tier is confined to a +-1R box -- it is the")
        print(f"      region the corpus can prove, NOT the region the trades actually live in.")

    print(f"\n-- TIER 2  BAR-BOUNDED   (anything wider: milestone envelopes, touch order often unknown)")
    print(f"   {'stop':>6} {'target':>7} {'n':>6} {'amb':>6} {'open':>6} {'lower R':>10} {'upper R':>10} {'width':>8}")
    print("   " + "-" * 70)
    for r in sorted(t2, key=lambda z: -(z["lo"] + z["hi"]) / 2):
        w = r["hi"] - r["lo"]
        flag = "  <-- UNUSABLE" if w > 0.5 else ""
        print(f"   {r['stop']:>6.2f} {fmt_tgt(r['tgt']):>7} {r['n']:>6} {r['amb']:>6} {r['open']:>6} "
              f"{r['lo']:>10.4f} {r['hi']:>10.4f} {w:>8.3f}{flag}")
    tight = [r for r in t2 if (r["hi"] - r["lo"]) <= 0.5]
    print()
    if tight:
        b = max(tight, key=lambda z: z["lo"])
        print(f"   -> best DECIDABLE wide arm (bound width <= 0.5R): stop {b['stop']:.2f}R / "
              f"target {fmt_tgt(b['tgt'])}R")
        print(f"      in [{b['lo']:+.4f}, {b['hi']:+.4f}] R per trade, {b['amb']} of {b['n']} ambiguous")
    else:
        print("   -> NO wide arm has a bound tighter than 0.5R. The corpus cannot rank ANY of them.")
    print("   *** A wide bound is not a weak ranking, it is the ABSENCE of one. These rows are")
    print("       waiting on the D2 bake (TickPathTailMs ~= 3600000, Recorder v2.5.0), which is")
    print("       the only thing that makes the >1R region tick-decidable.")


def main():
    ap = argparse.ArgumentParser(description="Two-tier exit-policy sweep over the banked corpus")
    ap.add_argument("--signal", default="KEEL")
    ap.add_argument("--since", default="2026-01", help="entry_utc lower bound (excludes the 2025-09 smoke run)")
    ap.add_argument("--inst", default=None)
    ap.add_argument("--bartype", default=None)
    ap.add_argument("--stops", default="0.5,0.75,1.0,1.5,2.0")
    ap.add_argument("--targets", default="0.5,0.75,1.0,1.5,2.0,3.0,5.0")
    ap.add_argument("--commission-usd", type=float, default=4.36, help="round turn, per NOW.md")
    ap.add_argument("--tick-value", type=float, default=5.0, help="USD per tick (NQ = 5.00)")
    a = ap.parse_args()

    stops = [float(x) for x in a.stops.split(",") if x.strip()]
    targets = [float(x) for x in a.targets.split(",") if x.strip()]

    cost_ticks = a.commission_usd / a.tick_value if a.tick_value else 0.0
    cost_desc = (f"${a.commission_usd:.2f}/RT / ${a.tick_value:.2f} per tick = {cost_ticks:.3f} ticks, "
                 f"converted per trade at that trade's own ATR barrier")

    c = connect()
    trades = load_trades(c, a.signal, a.since, a.inst, a.bartype)
    paths = load_paths(c, a.signal, a.since)
    c.close()

    def cost_r_of(t):
        return cost_ticks / t["barrier_ticks"] if t["barrier_ticks"] else 0.0

    t1, t2 = sweep(trades, paths, stops, targets, cost_r_of)
    labels = sorted({t["bar_label"] for t in trades if t["bar_label"]})
    cohort = f"signal={a.signal} since={a.since} bars={','.join(labels) or '?'}"
    report(trades, paths, t1, t2, cohort, cost_desc)


if __name__ == "__main__":
    main()
