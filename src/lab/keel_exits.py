#!/usr/bin/env python
"""keel_exits - the D1 GIVEBACK CURVE and the D2 KNOCKOUT measure, over the recorded corpus.

WHY THIS EXISTS (NOW.md, "TWO EXIT DEFECTS, NOT ONE"):
  D1 GIVEBACK  a winner reaches a decent MFE, decays, and dies on a full stop.
  D2 KNOCKOUT  the stop fills INSIDE a live trend and there is no way back in, so the rest of
               that trend is forfeited. D2 is STRUCTURAL: `trendState` only changes on the
               filter's own motion, so a stop-out while the filter still points the trade's way
               leaves the strategy flat with no re-entry until the filter turns and turns back.

  These are DIFFERENT defects and a BE+ trigger cannot fix D2 at all. Measuring only D1 would
  tune a breakeven level against losses it structurally cannot address, then read the poor
  result as "BE+ doesn't help" -- a wrong conclusion from a correct experiment. So this tool
  reports them SEPARATELY and refuses to blend them into one "exit quality" number.

THE DELIVERABLE IS A CURVE, NOT A LEVEL. Guessing a BE+ threshold and then measuring the guess
makes a bad level and a bad idea indistinguishable. For each arm level we report the PAIRED
decomposition -- same path, two policies -- so the trade-off is legible:
    SAVED      baseline took a full stop; BE+ got out at/near scratch      (BE+ helped)
    SCRATCHED  baseline was a WINNER; BE+ armed, price retraced, scratched (BE+ cost us)
    UNCHANGED  same outcome either way
The crossover -- where SAVED gains stop outrunning SCRATCHED losses -- is the answer.
A BE+ trigger is a TRADE, not a free win.

DATA PATH: reads sentinel.db (canon: tools -> JSONL -> ingester -> DB -> analyzer), read-only.
  D1 runs TICK-TRUE off the `ticks` sidecar path (ms, fav_t).
  D2 runs off the ROW milestones (mfe/mae at 1/5/15/60 min + barsToMFE/barsToStopR), because
  the tick path is capped by TickPathMaxMs and would UNDER-count a long trend by construction.

R UNIT: R = `barrier_ticks`, the recorder's own barrier. That is the unit `ms_to_stop_r` and
`ms_to_target_r` are already defined against, so the policy sim and the corpus agree by
construction. Baseline bracket is expressed in that R via --stop-r / --target-r.
"""
from __future__ import annotations
import os, sys, json, sqlite3, argparse, collections

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from lab_faults import swallow  # noqa: E402

# Console guard: a gate that crashes on its own output is a gate crying wolf (keel_srcdiff hit this).
try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception as e:  # noqa: BLE001 - reported, never silent
    swallow("keel_exits.stdout_reconfigure", e)

DB = os.path.join(os.path.dirname(os.path.abspath(__file__)), "db", "sentinel.db")


def connect():
    """READ-ONLY on purpose. The ingester owns this DB; an analyzer that could write is a
    second writer waiting to happen."""
    return sqlite3.connect(f"file:{DB.replace(os.sep, '/')}?mode=ro", uri=True, timeout=30)


# ----------------------------------------------------------------- loading
def load_trades(c, signal, inst=None, bartype=None, since=None):
    q = ["SELECT trade_id,inst,bartype,bar_label,dir,entry_utc,barrier_ticks,max_fav_ticks,"
         "max_adv_ticks,ms_to_stop_r,ms_to_target_r,dur_s,milestones_json,end_reason "
         "FROM trades WHERE signal=? AND barrier_ticks>0"]
    args = [signal]
    if inst:
        q.append("AND inst=?"); args.append(inst)
    if bartype:
        q.append("AND bartype=?"); args.append(bartype)
    if since:
        q.append("AND entry_utc>=?"); args.append(since)
    q.append("ORDER BY entry_utc")
    cur = c.execute(" ".join(q), args)
    cols = [d[0] for d in cur.description]
    return [dict(zip(cols, r)) for r in cur.fetchall()]


def load_path(c, trade_id):
    """Favorable-tick series, ascending. fav_t is already dir-signed by the recorder."""
    return [(r[0], r[1]) for r in c.execute(
        "SELECT ms,fav_t FROM ticks WHERE trade_id=? ORDER BY ms", (trade_id,))]


# ----------------------------------------------------------------- D1: paired BE+ sim
def sim_bracket(path, B, stop_r, tgt_r, arm_r=None, be_r=0.0):
    """Exit result in R. arm_r=None -> the plain baseline bracket (no BE+).

    Deliberately the SAME walk for both arms of the comparison, so any difference is the BE+
    rule and nothing else. Fill model matches pathlab: exit AT the level touched, no intrabar
    path between recorded ticks, timeout -> mark to last.
    """
    if not path:
        return 0.0, "empty"
    peak = 0.0
    for ms, fav in path:
        peak = max(peak, fav)
        r = fav / B
        stop = -stop_r
        if arm_r is not None and peak / B >= arm_r:
            stop = be_r                     # armed: stop moves to breakeven (or be_r)
        if r >= tgt_r:
            return tgt_r, "target"
        if r <= stop:
            return stop, ("be" if (arm_r is not None and stop == be_r) else "stop")
    return path[-1][1] / B, "timeout"


def d1_curve(c, trades, stop_r, tgt_r, arms, be_r):
    """Paired baseline-vs-BE+ decomposition per arm level."""
    paths = {}
    for t in trades:
        p = load_path(c, t["trade_id"])
        if p:
            paths[t["trade_id"]] = p
    base = {}
    for t in trades:
        p = paths.get(t["trade_id"])
        if p:
            base[t["trade_id"]] = sim_bracket(p, t["barrier_ticks"], stop_r, tgt_r)

    rows = []
    for a in arms:
        saved = scratched = unchanged = 0
        d_saved = d_scratched = 0.0
        base_sum = armed_sum = 0.0
        n = 0
        for t in trades:
            p = paths.get(t["trade_id"])
            if not p:
                continue
            b_r, b_why = base[t["trade_id"]]
            a_r, a_why = sim_bracket(p, t["barrier_ticks"], stop_r, tgt_r, arm_r=a, be_r=be_r)
            base_sum += b_r; armed_sum += a_r; n += 1
            d = a_r - b_r
            if abs(d) < 1e-9:
                unchanged += 1
            elif d > 0:
                saved += 1; d_saved += d          # BE+ avoided part of a loss
            else:
                scratched += 1; d_scratched += d  # BE+ gave up a winner
        rows.append(dict(arm=a, n=n, saved=saved, scratched=scratched, unchanged=unchanged,
                         gain=d_saved, cost=d_scratched, net=d_saved + d_scratched,
                         base_exp=(base_sum / n if n else 0), armed_exp=(armed_sum / n if n else 0)))
    # how far the observed paths actually reach, so an untested arm can't masquerade as a neutral one
    peaks_R = [max((f for _, f in p), default=0.0) / t["barrier_ticks"]
               for t in trades if (p := paths.get(t["trade_id"]))]
    return rows, base, paths, peaks_R


# ----------------------------------------------------------------- D2: knockout
def d2_knockout(trades, stop_r):
    """A KNOCKOUT = the stop was touched AND the peak favorable excursion came AFTER it.

    Uses ROW milestones, not the tick path: the tick path is capped by TickPathMaxMs, so a
    long trend is truncated by construction and would silently under-count the very defect
    being measured. barsToMFE > barsToStopR is the exact recorded statement of "stopped out,
    and the move kept going" -- which is what leaves the strategy flat with trendState
    unchanged and no way back in.
    """
    out = []
    skipped = 0
    for t in trades:
        if not t.get("milestones_json"):
            skipped += 1
            continue
        try:
            m = json.loads(t["milestones_json"])
            b_stop = m.get("barsToStopR", -1)
            b_mfe = m.get("barsToMFE", -1)
            if b_stop is None or b_mfe is None or b_stop < 0:
                continue                                  # never stopped -> not a knockout
            B = t["barrier_ticks"]
            mfe60 = m.get("mfe60")
            if mfe60 is None:
                continue
            forfeited_R = (mfe60 / B) - stop_r            # what the trend offered, net of the stop taken
            out.append(dict(trade_id=t["trade_id"], entry=t["entry_utc"], dir=t["dir"],
                            bars_to_stop=b_stop, bars_to_mfe=b_mfe,
                            knockout=(b_mfe > b_stop), mfe60_R=mfe60 / B,
                            forfeited_R=max(0.0, forfeited_R)))
        except Exception as e:  # noqa: BLE001 - reported via lab_faults, never silent
            swallow("keel_exits.d2_milestones", e, detail=t.get("trade_id"))
    return out, skipped


# ----------------------------------------------------------------- reporting
def report(signal, trades, d1, d2, skipped, stop_r, tgt_r, be_r, peaks_R=None):
    print(f"\n{'='*78}\n  KEEL EXIT STUDY   signal={signal}   n_trades={len(trades)}"
          f"   baseline = stop {stop_r}R / target {tgt_r}R\n{'='*78}")
    if not trades:
        print("  no trades matched -- nothing to say. (Corpus empty for this filter.)")
        return

    print(f"\n-- D1  GIVEBACK CURVE  (BE+ moves stop to {be_r:+.2f}R once MFE reaches the arm level)")
    print("   paired per trade: same path, baseline vs BE+. 'net' is the whole argument.")
    # An arm level no path ever reaches produces a PERFECT zero row, which reads exactly like
    # "tested and made no difference" when it actually means "never tested". Say which it is.
    if peaks_R:
        pk = sorted(peaks_R)
        print(f"   tick-path peak MFE in R: median {pk[len(pk)//2]:.2f}  p90 {pk[int(len(pk)*.9)]:.2f}"
              f"  max {pk[-1]:.2f}   (arms above this are UNTESTED, not neutral)")
    print()
    print(f"   {'arm':>6} {'n':>5} {'saved':>6} {'scratch':>8} {'unch':>6} "
          f"{'gain R':>9} {'cost R':>9} {'NET R':>9} {'exp base':>10} {'exp BE+':>9}")
    print("   " + "-" * 88)
    best = None
    for r in d1:
        flag = ""
        if best is None or r["net"] > best["net"]:
            best = r
        print(f"   {r['arm']:>6.2f} {r['n']:>5} {r['saved']:>6} {r['scratched']:>8} "
              f"{r['unchanged']:>6} {r['gain']:>9.2f} {r['cost']:>9.2f} {r['net']:>9.2f} "
              f"{r['base_exp']:>10.3f} {r['armed_exp']:>9.3f}{flag}")
    if best:
        affected = best["saved"] + best["scratched"]
        per_trade = best["net"] / best["n"] if best["n"] else 0.0
        verdict = "HELPS" if best["net"] > 0 else "HURTS AT EVERY LEVEL TESTED"
        print(f"\n   -> best arm = {best['arm']:.2f}R, net {best['net']:+.2f}R "
              f"({best['saved']} saved / {best['scratched']} scratched)   BE+ {verdict}")
        # A big-looking total over a big n is a tiny per-trade effect. Say the per-trade number
        # out loud, and refuse to dress a small affected-set up as a result -- picking the best
        # arm from a sweep is itself a selection, so a thin sample flatters the winner.
        print(f"      per-trade effect {per_trade:+.4f}R across n={best['n']} "
              f"({affected} trades actually affected, {100*affected/best['n']:.0f}%)")
        if affected < 30:
            print(f"      *** THIN: only {affected} trades changed outcome. This is the BEST of "
                  f"{len(d1)} arms tried, so it is a max over noise. NOT a result -- rerun on the bake.")
        if best["net"] <= 0:
            print("      Read this as: on THIS corpus the giveback defect is not recoverable by a")
            print("      breakeven stop. It does NOT say the exit is fine -- see D2.")

    print(f"\n-- D2  KNOCKOUT  (stop touched, then the move continued: barsToMFE > barsToStopR)")
    if not d2:
        print(f"   no rows carried milestones ({skipped} skipped) -- D2 needs the row-side")
        print("   milestone block. Nothing measured; this is NOT evidence of no knockouts.")
        return
    ko = [x for x in d2 if x["knockout"]]
    stopped = len(d2)
    print(f"   stop-touched trades with milestones : {stopped}   (skipped, no milestones: {skipped})")
    if stopped:
        print(f"   KNOCKOUTS                           : {len(ko)}  "
              f"({100*len(ko)/stopped:.0f}% of stop-outs)")
    if ko:
        f = sorted(x["forfeited_R"] for x in ko)
        tot = sum(f)
        top2 = sum(f[-2:])
        print(f"   forfeited trend, in R   median {f[len(f)//2]:.2f}   "
              f"p90 {f[int(len(f)*.9)]:.2f}   max {f[-1]:.2f}   TOTAL {tot:.1f}R")
        print(f"   *** UPPER BOUND, not an expectation: forfeited = mfe60 - stop, i.e. what a")
        print(f"       PERFECT re-entry exiting at the 60-min peak would have recovered. No real")
        print(f"       rule captures it. Read the MEDIAN ({f[len(f)//2]:.2f}R) as the typical case.")
        if tot > 0 and top2 / tot > 0.30:
            print(f"   *** OUTLIER-DOMINATED: the top 2 trades are {100*top2/tot:.0f}% of the total."
                  f" Quote the median, not the sum.")
        print(f"\n   worst 5 knockouts (the '87-point run' shape):")
        for x in sorted(ko, key=lambda z: -z["forfeited_R"])[:5]:
            print(f"     {x['entry'][:19]}  dir={x['dir']:+d}  stop@bar {x['bars_to_stop']:>3}  "
                  f"MFE@bar {x['bars_to_mfe']:>3}  mfe60 {x['mfe60_R']:>5.2f}R  "
                  f"forfeited {x['forfeited_R']:>5.2f}R")
        print(f"\n   -> D2 affects {len(ko)} of {stopped} stop-outs, typical size {f[len(f)//2]:.2f}R")
        print(f"      (ceiling {tot:.1f}R total, unreachable). A BE+ trigger recovers NONE of it --")
        print("      these are stop-outs inside a continuing trend, so the fix is a re-entry rule")
        print("      or a stop that respects the filter, not a tighter stop.")


def main():
    ap = argparse.ArgumentParser(description="D1 giveback curve + D2 knockout over the corpus")
    ap.add_argument("--signal", default="KEEL", help="producer tag (KEEL | COUNCIL | CANDIDATE)")
    ap.add_argument("--inst", default=None)
    ap.add_argument("--bartype", default=None)
    ap.add_argument("--since", default=None, help="entry_utc lower bound, e.g. 2026-04-19")
    ap.add_argument("--stop-r", type=float, default=1.0, help="baseline stop in R")
    ap.add_argument("--target-r", type=float, default=2.0, help="baseline target in R")
    ap.add_argument("--be-r", type=float, default=0.0, help="where the armed stop goes (0 = breakeven)")
    ap.add_argument("--arms", default="0.25,0.5,0.75,1.0,1.25,1.5,2.0",
                    help="comma-separated BE+ arm levels in R")
    a = ap.parse_args()

    arms = [float(x) for x in a.arms.split(",") if x.strip()]
    c = connect()
    trades = load_trades(c, a.signal, a.inst, a.bartype, a.since)
    d1, _, paths, peaks_R = d1_curve(c, trades, a.stop_r, a.target_r, arms, a.be_r)
    d2, skipped = d2_knockout(trades, a.stop_r)
    c.close()

    n_paths = len(paths)
    if n_paths < len(trades):
        print(f"\n  NOTE: {len(trades)-n_paths} of {len(trades)} trades have NO tick path "
              f"-> excluded from D1 (D2 is unaffected, it reads the row).")
    report(a.signal, trades, d1, d2, skipped, a.stop_r, a.target_r, a.be_r, peaks_R)


if __name__ == "__main__":
    main()
