#!/usr/bin/env python3
"""exp0005 — cross-correlate the HARNESS (raw tape) against the BAKE (NT's decisions).

**Pre-registration: `PREREGISTRATION_EXP0005_harness_x_bake.md`. Read it before changing a threshold.**

The harness and the bake are two independent measurements of the SAME 11 days of GC 08-26 tape. The
bake has 834 decision instants with tick-true sidecars but cannot check its own labels; the harness has
the raw tape but no decisions. Crossed, the harness is an external referee for the corpus.

The join is on the TIME AXIS and the tape, never on bars -- the bake is TBars `212201v6x24` and the
harness has only Tide `212207` ported, so no bar-for-bar comparison exists and none is attempted. That
makes every result here bar-type-agnostic.

    A   alignment control     first tape print at/after each fire == corpus firePx (>= 99%, <= 1 ms)
    A-  INVERTED control      the same test with +1 h injected. It MUST FAIL. A matcher that passes a
                              one-hour shift is measuring its own tolerance, not the data.
    B   path replication      harness path extremes vs the sidecar's (>= 95%)
    C   label adjudication    harness recomputes first_touch from the tape ALONE, then 3-way against
                              the sidecar (tick-true) and the row (bar-derived)

Stages gate: A fails -> stop; B fails -> that IS the finding, and C's label claims are not to be read
as if the sidecars were sound.

`nrdcsv.iter_l1` already yields UTC nanoseconds (Chicago->UTC internal, DST-ambiguity counted), so no
hand-rolled timezone math enters the join.
"""
from __future__ import annotations

import argparse
import array
import bisect
import json
import os
import sqlite3
import sys
from datetime import datetime, timezone

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from .equivalence import _iso_ns  # noqa: E402  (7-fractional-digit ISO -> exact integer ns)
from .nrdcsv import LAST, iter_l1  # noqa: E402
from .regime_study import CSV_ROOT  # noqa: E402

try:                                    # a Windows console is cp1252; the report is not
    sys.stdout.reconfigure(encoding="utf-8")
except (AttributeError, OSError):
    pass

SENT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
DB = os.path.join(SENT, "Lab", "db", "sentinel.db")
ROWS = os.path.join(SENT, "Excursions", "council", "1.5")

# --- the pre-registered subject ------------------------------------------------------------------
CONTRACT = "GC 08-26"
LANE = "212201v6x24@AUD0826"
ROW_FILE = "20260725T234907__GC__212201v6x24.jsonl"
DAY_LO, DAY_HI = "2026-06-29", "2026-07-10"
TICK = 0.1                      # GC
PX_TOL = 1.0 * TICK + 1e-9      # "within 1 tick", float-safe
HOUR_NS = 3_600_000_000_000

# Pre-registered thresholds. Changing one without changing the pre-registration is the failure mode
# this whole file exists to avoid.
A1_PASS, A2_PASS_MS, B1_PASS, C_H1_PASS = 0.99, 1.0, 0.95, 0.95


class Fire:
    __slots__ = ("tid", "ns", "dir", "px", "barrier", "sidecar_ft", "row_ft", "end_ns",
                 "path_dur_ms", "path_lo", "path_hi", "path_n")

    def __init__(self, **kw):
        for k, v in kw.items():
            setattr(self, k, v)


# =================================================================================================
# loading
# =================================================================================================
def load_tape(contract=CONTRACT, day_lo=DAY_LO, day_hi=DAY_HI, verbose=True):
    """All LAST prints for the window as two parallel arrays, ascending by ts.

    Trades only: the sidecar path is built from `OnMarketData` Last, so comparing against anything
    else would be comparing two different objects and calling the difference a finding.
    """
    d = os.path.join(CSV_ROOT, contract)
    lo, hi = day_lo.replace("-", ""), day_hi.replace("-", "")
    files = sorted(f for f in os.listdir(d)
                   if f.endswith(".csv") and lo <= f[:8] <= hi)
    ts = array.array("q")
    px = array.array("d")
    for f in files:
        for t in iter_l1(os.path.join(d, f), types=(LAST,)):
            ts.append(t.ts_ns)
            px.append(t.price)
    # Do not ASSUME the export is ordered -- a single inversion silently corrupts every bisect.
    inversions = sum(1 for i in range(1, len(ts)) if ts[i] < ts[i - 1])
    if verbose:
        print(f"tape: {len(files)} day files, {len(ts):,} LAST prints, {inversions} ts inversions")
        if len(ts):
            print(f"      coverage {_iso(ts[0])}  ->  {_iso(ts[-1])}")
    if inversions:
        # Sort rather than die: the files are per-day and only the seams should ever be out of order.
        order = sorted(range(len(ts)), key=ts.__getitem__)
        ts = array.array("q", (ts[i] for i in order))
        px = array.array("d", (px[i] for i in order))
        if verbose:
            print(f"      -> re-sorted ({inversions} inversions, all at day seams unless noted)")
    return ts, px


def load_fires(lane=LANE, row_file=ROW_FILE, day_lo=DAY_LO, day_hi=DAY_HI, verbose=True):
    """Fires in the overlap, carrying BOTH labels + the sidecar path summary.

    row JSONL  -> endTime, barrierTicks, firePx, dir, and the BAR-derived firstTouch
    sentinel.db-> the TICK-TRUE first_touch and the sidecar path (via the `ticks` table)
    """
    c = sqlite3.connect(DB)
    c.execute("PRAGMA busy_timeout=30000")
    q = ("SELECT trade_id, entry_utc, dir, entry_px, barrier_ticks, first_touch FROM trades "
         "WHERE source='council' AND bartype=? AND src<>'row' "
         "AND substr(entry_utc,1,10) BETWEEN ? AND ?")
    db = {r[1]: r for r in c.execute(q, (lane, day_lo, day_hi))}

    fires, missing_row = [], 0
    seen = set()
    with open(os.path.join(ROWS, row_file), encoding="utf-8") as fh:
        for line in fh:
            line = line.strip()
            if not line:
                continue
            o = json.loads(line)
            k = o.get("fireTime")
            if k not in db or k in seen:
                continue
            seen.add(k)
            tid, _, d, epx, bt, sft = db[k]
            if not (epx and bt and d):
                missing_row += 1
                continue
            fires.append(Fire(tid=tid, ns=_iso_ns(k), dir=int(d), px=float(epx),
                              barrier=float(bt), sidecar_ft=sft, row_ft=o.get("firstTouch"),
                              end_ns=_iso_ns(o["endTime"]) if o.get("endTime") else None,
                              path_dur_ms=None, path_lo=None, path_hi=None, path_n=None))
    # sidecar path summary in one pass (not 834 queries)
    agg = {r[0]: r[1:] for r in c.execute(
        "SELECT t.trade_id, MAX(t.ms), MIN(t.px), MAX(t.px), COUNT(*) FROM ticks t "
        "JOIN trades x ON x.trade_id=t.trade_id "
        "WHERE x.source='council' AND x.bartype=? AND substr(x.entry_utc,1,10) BETWEEN ? AND ? "
        "GROUP BY t.trade_id", (lane, day_lo, day_hi))}
    for f in fires:
        if f.tid in agg:
            f.path_dur_ms, f.path_lo, f.path_hi, f.path_n = agg[f.tid]
    if verbose:
        print(f"fires: {len(fires)} in {day_lo}..{day_hi}  "
              f"({sum(1 for f in fires if f.path_n) } with a sidecar path"
              + (f", {missing_row} dropped for missing px/barrier/dir)" if missing_row else ")"))
    return fires


def _iso(ns):
    return datetime.fromtimestamp(ns / 1e9, tz=timezone.utc).strftime("%Y-%m-%d %H:%M:%S")


def _pct(a, b):
    return 100.0 * a / b if b else float("nan")


def _covered(fires, ts, horizon=True):
    """Mechanical exclusions, declared in the pre-registration: a fire the tape cannot answer for.

    An export file for day X covers [X-1 23:00, X 23:00) LOCAL, so the window's edges are not
    calendar-aligned and some fires' forward horizon runs off the end of what we converted.
    """
    lo, hi = ts[0], ts[-1]
    keep, out_of_range, past_end = [], 0, 0
    for f in fires:
        if not (lo <= f.ns <= hi):
            out_of_range += 1
            continue
        if horizon and f.end_ns and f.end_ns > hi:
            past_end += 1
            continue
        keep.append(f)
    return keep, out_of_range, past_end


# =================================================================================================
# Stage A — alignment control (+ the inverted control)
# =================================================================================================
def _cluster(ts, px, i):
    """All prints sharing ts[i]'s exact timestamp."""
    j = k = i
    while j > 0 and ts[j - 1] == ts[i]:
        j -= 1
    while k + 1 < len(ts) and ts[k + 1] == ts[i]:
        k += 1
    return px[j:k + 1]


def stage_a(fires, ts, px, offset_ns=0, label="A"):
    """A1 (as pre-registered) and A1' (the amendment) are BOTH reported, always.

    A1 compares against the FIRST print of the fire's instant. On a tape where 45.6% of prints share
    a timestamp that criterion cannot be satisfied even by a perfect join, which is why it failed at
    96.92% on 2026-07-26 and why the amendment exists. The original stays on screen so the failure is
    never quietly replaced by the number that came after it.
    """
    matched = matched_c = exact0 = n = 0
    dts = []
    for f in fires:
        t = f.ns + offset_ns
        i = bisect.bisect_left(ts, t)
        if i >= len(ts):
            continue
        n += 1
        dts.append((ts[i] - t) / 1e6)
        if abs(px[i] - f.px) <= PX_TOL:
            matched += 1
        if ts[i] == t:
            exact0 += 1
            if any(abs(p - f.px) <= PX_TOL for p in _cluster(ts, px, i)):
                matched_c += 1
        elif abs(px[i] - f.px) <= PX_TOL:
            matched_c += 1
    dts.sort()
    med = dts[len(dts) // 2] if dts else float("nan")
    rate, rate_c = (matched / n if n else 0.0), (matched_c / n if n else 0.0)
    print(f"\n--- Stage {label}  (offset {offset_ns/HOUR_NS:+.0f} h) ---")
    print(f"  A1  firePx == first print of the instant : {matched}/{n} = {100*rate:.2f}%   "
          f"(pre-registered, pass >= {100*A1_PASS:.0f}%)  {'PASS' if rate>=A1_PASS else 'FAIL'}")
    print(f"  A1' firePx anywhere in that instant      : {matched_c}/{n} = {100*rate_c:.2f}%   "
          f"(amended,        pass >= {100*A1_PASS:.0f}%)  {'PASS' if rate_c>=A1_PASS else 'FAIL'}")
    print(f"  A2  median |dt| fire->print              : {med:.4f} ms  (pass <= {A2_PASS_MS} ms)")
    print(f"      prints landing exactly at the fire instant: {exact0}/{n} = {_pct(exact0,n):.1f}%")
    ok = rate_c >= A1_PASS and med <= A2_PASS_MS
    print(f"  => {'PASS' if ok else 'FAIL'}  (on A1')")
    return ok, rate, med


def diagnose_a(fires, ts, px):
    """Why did A1 miss? Test ONE named mechanism instead of guessing.

    45.6% of prints in this tape share their timestamp with another (sweep fragments), and
    `bisect_left` necessarily returns the FIRST member of such a cluster. If NT latched a DIFFERENT
    fragment of the same instant, the timestamps agree exactly while the prices differ -- alignment
    would be perfect and A1 would still miss. That is a falsifiable claim: the corpus price should
    then appear SOMEWHERE in the same-instant cluster.
    """
    miss = in_cluster = cluster_sz_gt1 = 0
    deltas = []
    examples = []
    for f in fires:
        i = bisect.bisect_left(ts, f.ns)
        if i >= len(ts) or abs(px[i] - f.px) <= PX_TOL:
            continue
        miss += 1
        j, k = i, i
        while j > 0 and ts[j - 1] == ts[i]:
            j -= 1
        while k + 1 < len(ts) and ts[k + 1] == ts[i]:
            k += 1
        clus = px[j:k + 1]
        if len(clus) > 1:
            cluster_sz_gt1 += 1
        hit = any(abs(p - f.px) <= PX_TOL for p in clus)
        in_cluster += hit
        deltas.append(abs(px[i] - f.px) / TICK)
        if len(examples) < 5:
            examples.append((_iso(f.ns), f.px, px[i], len(clus), hit))
    deltas.sort()
    print(f"\n--- Stage A diagnosis — the {miss} A1 misses ---")
    print(f"  corpus firePx found elsewhere in the SAME-INSTANT cluster : "
          f"{in_cluster}/{miss} = {_pct(in_cluster, miss):.1f}%")
    print(f"  misses whose instant holds >1 print                       : "
          f"{cluster_sz_gt1}/{miss} = {_pct(cluster_sz_gt1, miss):.1f}%")
    if deltas:
        print(f"  |firePx - firstPrint| in ticks: median {deltas[len(deltas)//2]:.1f}  "
              f"max {deltas[-1]:.1f}")
    print("       when                    firePx   1st print   clusterN   in-cluster")
    for e in examples:
        print(f"       {e[0]}   {e[1]:>8.1f}   {e[2]:>9.1f}   {e[3]:>8}   {e[4]}")
    if miss and in_cluster / miss >= 0.9:
        print("\n  => MECHANISM CONFIRMED: alignment is exact; NT latched a different fragment of the")
        print("     same instant. This is a price-selection difference inside one timestamp, not a")
        print("     join error. (Same mechanism as the 12-09 bar-close hunt.)")
    elif miss:
        print("\n  => NOT (only) the cluster mechanism — the corpus price is absent from the instant")
        print("     entirely on some fires. That needs its own explanation before Stage C is read.")
    return in_cluster, miss


def audit_horizon(fires):
    """Stage C depends entirely on `endTime`; if it were absent everywhere, C would score nothing
    and silently report n=0-ish. Check before trusting the exclusion count."""
    have = sum(1 for f in fires if f.end_ns)
    span = [(f.end_ns - f.ns) / 6e10 for f in fires if f.end_ns]
    span.sort()
    print(f"\n  horizon audit: {have}/{len(fires)} fires carry endTime; "
          f"median horizon {span[len(span)//2]:.1f} min, max {span[-1]:.1f} min"
          if span else "\n  horizon audit: NO endTime on any fire — Stage C cannot run")
    return have


# =================================================================================================
# Stage B — tick-path replication
# =================================================================================================
def stage_b(fires, ts, px):
    both = lo_ok = hi_ok = 0
    n = 0
    dn = []
    for f in fires:
        if not f.path_n or f.path_dur_ms is None:
            continue
        n += 1
        end = f.ns + int(f.path_dur_ms) * 1_000_000
        i = bisect.bisect_left(ts, f.ns)
        j = bisect.bisect_right(ts, end)
        if j <= i:
            dn.append(1.0)
            continue
        seg = px[i:j]
        h_lo, h_hi = min(seg), max(seg)
        l_ok = abs(h_lo - f.path_lo) <= PX_TOL
        h_ok = abs(h_hi - f.path_hi) <= PX_TOL
        lo_ok += l_ok
        hi_ok += h_ok
        both += (l_ok and h_ok)
        dn.append(abs((j - i) - f.path_n) / f.path_n)
    dn.sort()
    med_dn = dn[len(dn) // 2] if dn else float("nan")
    rate = both / n if n else 0.0
    print(f"\n--- Stage B — tick-path replication ---")
    print(f"  B1 path min AND max within 1 tick : {both}/{n} = {100*rate:.2f}%   "
          f"(pass >= {100*B1_PASS:.0f}%)")
    print(f"     min alone {_pct(lo_ok,n):.2f}%   max alone {_pct(hi_ok,n):.2f}%")
    print(f"  B2 median |dPrints|/n             : {100*med_dn:.2f}%   (reported, no threshold)")
    ok = rate >= B1_PASS
    print(f"  => {'PASS' if ok else 'FAIL'}")
    return ok, rate


# =================================================================================================
# Stage C — independent first-touch adjudication
# =================================================================================================
def harness_first_touch(f, ts, px):
    """first_touch from the TAPE ALONE, over the row's OWN horizon.

    Using the row's `endTime` (not a fresh 60-min clock) is deliberate: it isolates the price-source
    difference, so a disagreement cannot be blamed on a different window length.
    """
    if f.end_ns is None:
        return None
    tgt = f.px + f.dir * f.barrier * TICK
    stp = f.px - f.dir * f.barrier * TICK
    i = bisect.bisect_left(ts, f.ns)
    j = bisect.bisect_right(ts, f.end_ns)
    up, dn = (tgt, stp) if f.dir > 0 else (stp, tgt)
    for k in range(i, j):
        p = px[k]
        if p >= up:
            return 1 if f.dir > 0 else -1
        if p <= dn:
            return -1 if f.dir > 0 else 1
    return 0


def stage_c(fires, ts, px):
    from collections import Counter
    hs, hr = Counter(), Counter()
    agree_sc = agree_row = n = 0
    dg_row = dg_row_tot = 0
    for f in fires:
        h = harness_first_touch(f, ts, px)
        if h is None:
            continue
        n += 1
        hs[(f.sidecar_ft, h)] += 1
        hr[(f.row_ft, h)] += 1
        agree_sc += (h == f.sidecar_ft)
        agree_row += (h == f.row_ft)
        if f.row_ft in (1, -1):
            dg_row_tot += 1
            dg_row += (h == 0)
    print(f"\n--- Stage C — independent label adjudication  (n={n}) ---")
    print(f"  H1 harness == SIDECAR (tick-true) : {agree_sc}/{n} = {_pct(agree_sc,n):.2f}%   "
          f"(pass >= {100*C_H1_PASS:.0f}%)")
    print(f"     harness == ROW (bar-derived)   : {agree_row}/{n} = {_pct(agree_row,n):.2f}%")
    print(f"  H2 harness downgrades ROW touches : {dg_row}/{dg_row_tot} = "
          f"{_pct(dg_row,dg_row_tot):.1f}%   (measured sidecar rate 35.9%)")
    for title, tbl, other in (("sidecar -> harness", hs, "sidecar"), ("row -> harness", hr, "row")):
        print(f"\n     {title}")
        print(f"       {other:>8} -> harness   count")
        for a in (-1, 0, 1):
            for b in (-1, 0, 1):
                if tbl.get((a, b)):
                    tag = ""
                    if a in (1, -1) and b in (1, -1) and a != b:
                        tag = "   <-- DIRECTION DISAGREE"
                    elif a in (1, -1) and b == 0:
                        tag = "   <-- downgraded"
                    print(f"       {a:>8} -> {b:>7}   {tbl[(a,b)]:>5}{tag}")
    ok = (agree_sc / n if n else 0) >= C_H1_PASS
    print(f"\n  => H1 {'PASS' if ok else 'FAIL'}")
    if n and agree_row > agree_sc:
        print("  🔴 FALSIFIER TRIPPED: the harness sides with the ROW over the SIDECAR.")
        print("     Per the pre-registration this RETRACTS the 2026-07-26 label finding.")
    return ok


# =================================================================================================
def main(argv=None) -> int:
    ap = argparse.ArgumentParser(prog="harness.exp0005")
    ap.add_argument("--stage", default="abc", help="subset of 'abc' (default all, gated)")
    ap.add_argument("--no-gate", action="store_true", help="run later stages even if A/B fail")
    args = ap.parse_args(argv)

    ts, px = load_tape()
    if not len(ts):
        print("no tape — is the contract converted?")
        return 2
    fires = load_fires()
    fires, oor, past = _covered(fires, ts)
    print(f"       excluded: {oor} outside tape coverage, {past} whose horizon runs past its end")
    print(f"       -> {len(fires)} fires scored")
    if not fires:
        return 2

    ok_a = ok_b = True
    if "a" in args.stage:
        ok_a, _, _ = stage_a(fires, ts, px, 0, "A")
        ok_inv, _, _ = stage_a(fires, ts, px, HOUR_NS, "A-INVERTED (must FAIL)")
        if ok_inv:
            print("\n🔴 THE INVERTED CONTROL PASSED. The matcher cannot detect a one-hour shift, so it")
            print("   proves nothing about alignment. Stage A is VOID regardless of its own number.")
            ok_a = False
        else:
            print("\n  OK: inverted control failed as required — the matcher can tell alignment apart.")
        diagnose_a(fires, ts, px)       # always: the A1 misses are the finding, not an error path
        audit_horizon(fires)
        if not ok_a and not args.no_gate:
            print("\nSTOP: Stage A is the gate. Nothing downstream is interpretable.")
            return 1
    if "b" in args.stage:
        ok_b, _ = stage_b(fires, ts, px)
        if not ok_b:
            print("\n⚠ Stage B failed: this indicts NT's tick capture under replay load, which applies")
            print("  to EVERY sidecar in the corpus. Stage C's labels are not to be read as sound.")
    if "c" in args.stage:
        stage_c(fires, ts, px)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
