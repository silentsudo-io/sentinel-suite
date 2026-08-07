#!/usr/bin/env python
"""pathlab — tick-true PATH & EXIT-POLICY analysis over the Sentinel tick sidecar.

The excursion corpus (`Excursions\{council\1.4, candidates\cand.1}\*.jsonl`) records the
first-touch LABEL on a symmetric ATR barrier — a deliberately lossy 1-bit summary. The tick
SIDECAR (`Excursions\{council,candidates}\ticks\*.jsonl`) records the full millisecond price
PATH per trade + a pre-computed header fingerprint. This tool reads the paths and answers the
real question: how much path structure is sitting there waiting to be harvested by trade
management, and HOW should management be applied per cohort.

Three rungs (see NOW.md clock-edge thesis):
  1. characterize path ARCHETYPES per cohort (heat/run timing + magnitude)   [descriptive]
  2. score EXIT POLICIES tick-true over the sidecar -> expectancy per cohort  [the lift]
  3. condition management on ENTRY CONTEXT (does context predict archetype?)  [the payoff]

DISPLAY uses friendly speed labels (sentinel_lab.bartag). Raw scope stays the machine key.
Read-only. No NinjaTrader. Fill model = point-per-tick, stop/target fill AT the level touched
(no intrabar path between recorded ticks), timeout = mark-to-last.
"""
from __future__ import annotations
import json, glob, os, sys, math, collections, argparse

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from lab_faults import swallow
from sentinel_lab.bartag import friendly_bartag  # noqa: E402

BASE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Excursions")

# min tick size per instrument root (price units per tick)
TICK = {"GC": 0.1, "MGC": 0.1, "NQ": 0.25, "MNQ": 0.25, "ES": 0.25, "MES": 0.25}
# $ value of one tick per instrument root
TICKVAL = {"GC": 10.0, "MGC": 1.0, "NQ": 5.0, "MNQ": 0.5, "ES": 12.5, "MES": 1.25}
# realistic round-trip friction: commission ($/contract RT) + slippage (ticks PER SIDE)
COST = {"commission_rt": 4.0, "slip_ticks": 1.0, "on": False}


def tick_size(inst):
    return TICK.get(str(inst).upper()[:3], TICK.get(str(inst).upper()[:2], 0.1))


def tick_val(inst):
    return TICKVAL.get(str(inst).upper()[:3], TICKVAL.get(str(inst).upper()[:2], 10.0))


def cost_R(h):
    """Round-trip friction expressed in R (barrier units). 0 when costs are off."""
    if not COST["on"]:
        return 0.0
    B = h.get("barrierTicks") or 1.0
    tv = tick_val(h.get("inst"))
    comm_ticks = COST["commission_rt"] / tv          # commission -> ticks
    cost_ticks = comm_ticks + 2 * COST["slip_ticks"]  # + slippage both sides
    return cost_ticks / B


# ---------------------------------------------------------------- loading
def load_paths(kind="council", day=None, scopes=None):
    """Yield (header:dict, path:list[(ms,favTicks)]) for each sidecar trade.
    favTicks = dir*(px-firePx)/tick  (favorable positive, adverse negative)."""
    tdir = os.path.join(BASE, "council" if kind == "council" else "candidates", "ticks")
    for fp in glob.glob(os.path.join(tdir, "*.jsonl")):
        try:
            with open(fp, encoding="utf-8") as fh:
                first = fh.readline().strip()
                if not first:
                    continue
                h = json.loads(first)
                if day and not str(h.get("fireTime", "")).startswith(day):
                    continue
                if scopes and h.get("scope") not in scopes:
                    continue
                inst = h.get("inst", "GC")
                ts = tick_size(inst)
                fpx = h.get("firePx")
                d = h.get("dir", 1) or 1
                path = []
                for line in fh:
                    line = line.strip()
                    if not line:
                        continue
                    r = json.loads(line)
                    if "px" not in r:
                        continue
                    path.append((r["ms"], d * (r["px"] - fpx) / ts))
                yield h, path
        except (json.JSONDecodeError, KeyError, OSError) as _swex:
            swallow("pathlab.load_paths", _swex)
            continue


def scope_of(h):
    return h.get("scope") or f"{h.get('inst')}.{h.get('bartype')}"


# ---------------------------------------------------------------- rung 1: archetypes
def archetype(h):
    """Interpretable path-shape label from the tick-true header fingerprint."""
    B = h.get("barrierTicks") or 1.0
    rMFE = (h.get("maxFavTicks") or 0) / B
    rMAE = (h.get("maxAdvTicks") or 0) / B
    tFav = h.get("msToMaxFav", -1)
    tAdv = h.get("msToMaxAdv", -1)
    tTgt = h.get("msToTargetR", -1)   # first time fav hit +B (-1 = never)
    tStp = h.get("msToStopR", -1)     # first time adv hit -B (-1 = never)
    heat_first = (tAdv >= 0 and (tFav < 0 or tAdv < tFav))
    hit_tgt = tTgt >= 0
    hit_stp = tStp >= 0
    # early = within first 20% of the observed horizon
    horizon = max(tFav, tAdv, tTgt, tStp, 1)
    if hit_stp and (not hit_tgt or tStp < tTgt) and tStp < 0.2 * horizon and rMFE < 0.5:
        return "immediate_fail"
    if hit_tgt and not heat_first and rMAE < 0.5:
        return "pop_and_go"
    if rMFE >= 1.0 and heat_first:
        return "grind_up"          # survived early heat, then ran
    if 0.4 <= rMFE < 1.0 and (hit_stp or rMAE >= 0.8):
        return "chop_then_fail"    # offered some, then rolled over
    if not hit_tgt and not hit_stp and rMFE < 0.8 and rMAE < 0.8:
        return "chop_timeout"
    return "mixed"


# ---------------------------------------------------------------- rung 2: exit policies
def sim_policy(path, B, policy):
    """Return exit result in R (barrier units). path=list[(ms,favTicks)] ascending.
    Policies are pure functions of the running favorable-tick series."""
    if not path:
        return 0.0
    kind = policy["kind"]
    peak = 0.0
    scaled = False
    scaled_R = 0.0
    for ms, fav in path:
        peak = max(peak, fav)
        r = fav / B
        if kind == "sym":
            if r >= policy["tgt"]:
                return policy["tgt"]
            if r <= -policy["stop"]:
                return -policy["stop"]
        elif kind == "asym":
            if r >= policy["tgt"]:
                return policy["tgt"]
            if r <= -policy["stop"]:
                return -policy["stop"]
        elif kind == "be":
            stop = -policy["stop"]
            if peak / B >= policy["arm"]:
                stop = policy.get("be", 0.0)
            if r >= policy["tgt"]:
                return policy["tgt"]
            if r <= stop:
                return stop
        elif kind == "trail":
            # trail only engages once fav>0; stop = max(initial, peak - k)
            k = policy["k"]
            stop_R = max(-policy["stop"], (peak / B) - k) if peak > 0 else -policy["stop"]
            if policy.get("tgt") and r >= policy["tgt"]:
                return policy["tgt"]
            if r <= stop_R:
                return stop_R
        elif kind == "scale_trail":
            k = policy["k"]
            if not scaled and r >= policy["first"]:
                scaled = True
                scaled_R = policy["first"] * policy["frac"]
            stop_R = max(-policy["stop"], (peak / B) - k) if peak > 0 else -policy["stop"]
            if r <= stop_R:
                rem = (1 - policy["frac"]) if scaled else 1.0
                return scaled_R + rem * stop_R
        elif kind == "time":
            stop = -policy["stop"]
            if r <= stop:
                return stop
            if policy.get("tgt") and r >= policy["tgt"]:
                return policy["tgt"]
            if ms >= policy["mins"] * 60_000:
                return r  # mark-to-market exit at time limit
    # end of path: timeout -> mark to last, but respect a scale that already banked
    last = path[-1][1] / B
    if kind == "scale_trail" and scaled:
        return scaled_R + (1 - policy["frac"]) * last
    return last


POLICIES = [
    {"name": "baseline_sym_1R",   "kind": "sym",  "tgt": 1.0, "stop": 1.0},
    {"name": "asym_2R/1R",        "kind": "asym", "tgt": 2.0, "stop": 1.0},
    {"name": "asym_3R/1R",        "kind": "asym", "tgt": 3.0, "stop": 1.0},
    {"name": "asym_1.5R/1R",      "kind": "asym", "tgt": 1.5, "stop": 1.0},
    {"name": "asym_1R/1.5R",      "kind": "asym", "tgt": 1.0, "stop": 1.5},
    {"name": "BE@1R_tgt3R",       "kind": "be",   "arm": 1.0, "be": 0.0, "stop": 1.0, "tgt": 3.0},
    {"name": "BE@0.5R_tgt2R",     "kind": "be",   "arm": 0.5, "be": 0.0, "stop": 1.0, "tgt": 2.0},
    {"name": "trail_1R",          "kind": "trail","k": 1.0, "stop": 1.0},
    {"name": "trail_1.5R",        "kind": "trail","k": 1.5, "stop": 1.0},
    {"name": "trail_0.75R",       "kind": "trail","k": 0.75, "stop": 1.0},
    {"name": "scale.5@1R+trail1R","kind": "scale_trail", "first": 1.0, "frac": 0.5, "k": 1.0, "stop": 1.0},
    {"name": "time_30m_stop1R",   "kind": "time", "mins": 30, "stop": 1.0},
    {"name": "time_60m_stop1R",   "kind": "time", "mins": 60, "stop": 1.0},
]


# ---------------------------------------------------------------- reporting
def stats(rs):
    n = len(rs)
    if n == 0:
        return dict(n=0, exp=0, wr=0, avgW=0, avgL=0, pf=0)
    wins = [r for r in rs if r > 0]
    losses = [r for r in rs if r <= 0]
    gw = sum(wins); gl = -sum(losses)
    return dict(n=n, exp=sum(rs) / n, wr=100 * len(wins) / n,
                avgW=(gw / len(wins) if wins else 0),
                avgL=(gl / len(losses) if losses else 0),
                pf=(gw / gl if gl else float("inf")))


def run(kind, day, top_scopes=None):
    by_scope = collections.defaultdict(list)  # scope -> list of (header, path)
    for h, path in load_paths(kind, day=day):
        by_scope[scope_of(h)].append((h, path))

    # order cohorts by trade count
    order = sorted(by_scope, key=lambda s: -len(by_scope[s]))
    if top_scopes:
        order = order[:top_scopes]

    print(f"\n{'='*92}\nPATHLAB  kind={kind}  day={day or 'ALL'}  cohorts={len(by_scope)}\n{'='*92}")

    for scope in order:
        rows = by_scope[scope]
        inst = rows[0][0].get("inst")
        label = f"{inst} · {friendly_bartag(rows[0][0].get('bartype'))}"
        B0 = [ (h.get('barrierTicks') or 0) for h,_ in rows ]

        # ---- rung 1: archetype mix
        arch = collections.Counter(archetype(h) for h, _ in rows)
        n = len(rows)
        print(f"\n■ {label}   (n={n} tick-paths, avg barrier={sum(B0)/n:.1f}t)")
        amix = "  ".join(f"{a}:{100*c/n:.0f}%" for a, c in arch.most_common())
        print(f"  archetypes: {amix}")

        # ---- rung 2: policy sweep (net of costs when COST['on'])
        results = {}
        for pol in POLICIES:
            rs = [sim_policy(path, (h.get("barrierTicks") or 1.0), pol) - cost_R(h) for h, path in rows]
            results[pol["name"]] = stats(rs)
        base = results["baseline_sym_1R"]["exp"]
        print(f"  {'policy':22s} {'expR':>7s} {'Δbase':>7s} {'wr%':>6s} {'avgW':>6s} {'avgL':>6s} {'PF':>5s}")
        for pol in POLICIES:
            s = results[pol["name"]]
            pf = f"{s['pf']:.2f}" if s['pf'] != float('inf') else "inf"
            star = " *" if s["exp"] == max(r["exp"] for r in results.values()) else ""
            print(f"  {pol['name']:22s} {s['exp']:+7.3f} {s['exp']-base:+7.3f} {s['wr']:6.1f} "
                  f"{s['avgW']:6.2f} {s['avgL']:6.2f} {pf:>5s}{star}")


# ---------------------------------------------------------------- rung 3: entry-context conditioning
# Row-corpus schemas, NEWEST FIRST. pathlab reads the newest schema that actually has files, and says so —
# it never silently pools, because the older schemas' LABELS are not comparable: before council 1.5 / cand.2,
# `firePx` was the Heikin-Ashi SYNTHETIC bar close (a price that never traded) and it is the reference for
# MFE / MAE / barrier / firstTouch, making every label ~9 ticks optimistic.
# Proven 2026-07-22 — memory `firepx-is-synthetic-ha-close`.
ROW_SCHEMAS = {"council":    [("council", "1.5"), ("council", "1.4"), ("council", "1.3")],
               "candidates": [("candidates", "cand.2"), ("candidates", "cand.1")]}
CONTAM_LABEL_SCHEMAS = {"1.3", "1.4", "cand.1"}


def resolve_row_dir(kind):
    """Newest row schema for `kind` that has files. Warns LOUDLY when it falls back to a schema whose
    labels are measured from the untradeable synthetic close — a silent fallback is how bad labels
    get re-used after they have already been diagnosed."""
    for sub in ROW_SCHEMAS[kind]:
        if glob.glob(os.path.join(BASE, sub[0], sub[1], "*.jsonl")):
            if sub[1] in CONTAM_LABEL_SCHEMAS:
                print(f"  !! {kind}: no {ROW_SCHEMAS[kind][0][1]} rows yet - falling back to {sub[1]}, whose "
                      f"labels are measured from the SYNTHETIC bar close (~9t optimistic). Bake, then re-run.")
            return sub
    return ROW_SCHEMAS[kind][0]


def load_row_context(kind):
    """Return two join indexes over the row corpus for `kind`:
      by_ep  : episodeId -> row            (council only; candidate rows have no episodeId)
      by_ftk : (inst,bartype,dir,fireTime) -> row  (both; the reliable key — 97% on candidates)."""
    by_ep, by_ftk = {}, {}
    sub = resolve_row_dir(kind)
    for fp in glob.glob(os.path.join(BASE, sub[0], sub[1], "*.jsonl")):
        for line in open(fp, encoding="utf-8"):
            line = line.strip()
            if not line:
                continue
            try:
                d = json.loads(line)
            except json.JSONDecodeError as _swex:
                swallow("pathlab.load_row_context", _swex)
                continue
            if d.get("episodeId"):
                by_ep[d["episodeId"]] = d
            by_ftk[(d.get("inst"), d.get("bartype"), d.get("dir"), d.get("fireTime"))] = d
    return by_ep, by_ftk


def ctx_of(h, by_ep, by_ftk):
    """Entry context for a tick-path: prefer the SELF-DESCRIBING header (ctick.3+), else the
    episodeId-joined row, else the (inst,bartype,dir,fireTime)-joined row. Carries both council
    (conviction) and candidate (runLength/rvol/climax/dryUp/flux) context so the gate is kind-agnostic."""
    j = by_ep.get(h.get("episodeId")) or by_ftk.get(
        (h.get("inst"), h.get("bartype"), h.get("dir"), h.get("fireTime"))) or {}

    def pick(k):
        return h[k] if k in h else j.get(k)
    return dict(
        regime=pick("regime"), clock=pick("clockPhase"), conv=pick("conviction"),
        netScore=pick("netScore"), runLength=pick("runLength"), rvol=pick("rvol"),
        climax=pick("climax"), dryUp=pick("dryUp"), fluxDir=pick("fluxDir"),
        mtfBias=pick("mtfBias"), joined=bool(j),
    )


def conviction_bucket(c):
    if c is None:
        return "?"
    if c < 0.2:
        return "conv<0.2"
    if c < 0.35:
        return "conv0.2-0.35"
    if c < 0.5:
        return "conv0.35-0.5"
    return "conv>=0.5"


def rung3(scope, day=None, kind="council"):
    """Entry-context conditioning + time-split honesty check for one scope."""
    by_ep, by_ftk = load_row_context(kind)
    rows = []
    for h, path in load_paths(kind, day=day, scopes={scope}):
        c = ctx_of(h, by_ep, by_ftk)
        B = h.get("barrierTicks") or 1.0
        base = sim_policy(path, B, POLICIES[0]) - cost_R(h)
        rows.append(dict(h=h, path=path, B=B, base=base, ft=h.get("fireTime", ""),
                         regime=c["regime"], clock=c["clock"],
                         conv=c["conv"], arch=archetype(h)))
    if not rows:
        print(f"  (no {kind} paths for {scope})")
        return
    rows.sort(key=lambda r: r["ft"])
    lbl = f"{rows[0]['h'].get('inst')} · {friendly_bartag(rows[0]['h'].get('bartype'))}"
    print(f"\n{'='*92}\nRUNG 3  {lbl}   (n={len(rows)})\n{'='*92}")

    def slice_exp(rs, key):
        buckets = collections.defaultdict(list)
        for r in rs:
            buckets[r[key]].append(r["base"])
        for b in sorted(buckets, key=lambda x: -stats(buckets[x])["exp"]):
            s = stats(buckets[b])
            print(f"    {str(b):16s} n={s['n']:4d}  baseExpR={s['exp']:+.3f}  wr={s['wr']:.1f}%")

    print("  baseline expR by REGIME:")
    slice_exp(rows, "regime")
    print("  baseline expR by CLOCK phase:")
    slice_exp(rows, "clock")
    print("  baseline expR by CONVICTION bucket:")
    for r in rows:
        r["cbk"] = conviction_bucket(r["conv"])
    slice_exp(rows, "cbk")
    print("  archetype mix by regime:")
    reg = collections.defaultdict(collections.Counter)
    for r in rows:
        reg[r["regime"]][r["arch"]] += 1
    for rg, cc in reg.items():
        tot = sum(cc.values())
        mix = " ".join(f"{a}:{100*n//tot}%" for a, n in cc.most_common(3))
        print(f"    {str(rg):16s} n={tot:4d}  {mix}")

    # ---- honesty check: time-split, pick best policy on train, score on test
    cut = int(len(rows) * 0.7)
    train, test = rows[:cut], rows[cut:]
    print(f"\n  HONESTY CHECK  train n={len(train)} (older)  test n={len(test)} (newer):")
    def pexp(rs, p):
        return stats([sim_policy(r["path"], r["B"], p) - cost_R(r["h"]) for r in rs])["exp"]
    train_exp = {p["name"]: pexp(train, p) for p in POLICIES}
    best = max(train_exp, key=train_exp.get)
    for nm in ("baseline_sym_1R", best):
        p = next(pp for pp in POLICIES if pp["name"] == nm)
        te, se = pexp(train, p), pexp(test, p)
        tag = " <- best-on-train" if nm == best else " (baseline)"
        print(f"    {nm:22s} train={te:+.3f}  test={se:+.3f}  gap={se-te:+.3f}{tag}")


def gate(scope, day=None, kind="council"):
    """GATE analysis: does filtering on entry context lift NET expectancy, and does it hold out of
    sample? COUNCIL gates on conviction (its fused number); CANDIDATES have no conviction, so they
    gate on runLength + participation (climax/dryUp) — the clock-edge question on the raw base rate."""
    by_ep, by_ftk = load_row_context(kind)
    rows, njoin = [], 0
    for h, path in load_paths(kind, day=day, scopes={scope}):
        c = ctx_of(h, by_ep, by_ftk)
        njoin += 1 if c["joined"] else 0
        rows.append(dict(h=h, path=path, base=sim_policy(path, h.get("barrierTicks") or 1.0, POLICIES[0]) - cost_R(h),
                         ft=h.get("fireTime", ""), regime=c["regime"], conv=c["conv"],
                         runLength=c["runLength"], climax=c["climax"], dryUp=c["dryUp"]))
    if not rows:
        print(f"  (no {kind} paths for {scope})")
        return
    rows.sort(key=lambda r: r["ft"])
    lbl = f"{rows[0]['h'].get('inst')} · {friendly_bartag(rows[0]['h'].get('bartype'))}"
    net = "NET of costs" if COST["on"] else "GROSS (no costs)"
    cov = 100 * njoin / len(rows)
    print(f"\n{'='*92}\nGATE [{kind}]  {lbl}   (n={len(rows)}, {net}, context coverage {cov:.0f}%)\n{'='*92}")

    def summary(rs):
        s = stats([r["base"] for r in rs])
        return f"n={s['n']:4d}  expR={s['exp']:+.3f}  wr={s['wr']:.1f}%  totalR={sum(r['base'] for r in rs):+.1f}"

    print(f"  UNGATED (take all):                {summary(rows)}")
    print("  regime gate (where populated):")
    for rg in ("trend", "mid", "chop"):
        sub = [r for r in rows if r["regime"] == rg]
        if sub:
            print(f"    regime={rg:6s}                       {summary(sub)}")

    if kind == "council":
        print("  conviction-floor gate:")
        for fl in (0.20, 0.35, 0.50):
            print(f"    conv>= {fl:.2f}                        {summary([r for r in rows if (r['conv'] or 0) >= fl])}")
        print(f"  combined trend AND conv>=0.35:    {summary([r for r in rows if r['regime']=='trend' and (r['conv'] or 0)>=0.35])}")
        gatefn = lambda r, x: (r["conv"] or 0) >= x
        opts = [0.0, 0.20, 0.35, 0.50]
        glabel = "conv>="
    else:  # candidates: runLength (momentum persistence at entry) is the native gate
        rl = [r["runLength"] for r in rows if r["runLength"] is not None]
        print("  runLength gate (momentum persistence at entry):")
        for lo in (2, 4, 8):
            print(f"    runLength>= {lo:<2d}                     {summary([r for r in rows if (r['runLength'] or 0) >= lo])}")
        print(f"  participation: climax fires        {summary([r for r in rows if r['climax']])}")
        print(f"  participation: dry-up fires        {summary([r for r in rows if r['dryUp']])}")
        gatefn = lambda r, x: (r["runLength"] or 0) >= x
        opts = [0, 2, 4, 8]
        glabel = "runLength>="

    # honesty: pick the single best gate threshold on train, apply to test
    cut = int(len(rows) * 0.7)
    train, test = rows[:cut], rows[cut:]
    tr = {x: stats([r["base"] for r in train if gatefn(r, x)])["exp"] for x in opts}
    bx = max(opts, key=lambda x: (tr[x] if sum(1 for r in train if gatefn(r, x)) >= 20 else -9))
    print(f"\n  HONESTY CHECK ({glabel} gate, train n={len(train)} / test n={len(test)}):")
    for x in (opts[0], bx):
        te = stats([r["base"] for r in train if gatefn(r, x)])
        se = stats([r["base"] for r in test if gatefn(r, x)])
        tag = " <- best on train" if x == bx and x != opts[0] else (" (ungated)" if x == opts[0] else "")
        print(f"    {glabel}{x:<5} train expR={te['exp']:+.3f} (n={te['n']})  test expR={se['exp']:+.3f} (n={se['n']}){tag}")


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--kind", choices=["council", "candidates"], default="council")
    ap.add_argument("--day", default=None, help="YYYY-MM-DD fireTime filter (default ALL)")
    ap.add_argument("--top", type=int, default=None, help="only top-N cohorts by count")
    ap.add_argument("--rung3", default=None, help="scope key to run entry-context conditioning on")
    ap.add_argument("--gate", default=None, help="scope key to run the conviction/regime gate on")
    ap.add_argument("--costs", action="store_true", help="subtract commission+slippage (net expectancy)")
    a = ap.parse_args()
    COST["on"] = a.costs
    if a.gate:
        gate(a.gate, a.day, a.kind)
    elif a.rung3:
        rung3(a.rung3, a.day, a.kind)
    else:
        run(a.kind, a.day, a.top)
