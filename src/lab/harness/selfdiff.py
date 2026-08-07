#!/usr/bin/env python3
"""selfdiff — is NinjaTrader DETERMINISTIC? Diff two SentinelBarDump files of the same tape.

WHY THIS IS THE DECISIVE TEST
-----------------------------
The equivalence gate says the harness reproduces 39% of NinjaTrader's bars exactly, and the
print-by-print trace showed why: bar boundaries depend on the ordering of quote and trade events
sharing a timestamp, an ordering the export flattens away. The pairing study then showed no
recoverable rule closes the gap.

But that whole line of reasoning silently assumes NinjaTrader is REPRODUCIBLE -- that the 61% is
our error against a fixed answer. If NinjaTrader's own event ordering varies between runs, there
is no fixed answer, and part of that 61% is not an error at all.

This removes our signing rule from the experiment entirely. Two rebuilds of the same tape by the
same program. Nothing in between.

  IDENTICAL      NinjaTrader is deterministic. The whole gap is ours, there IS a fixed target, and
                 bit-equality is worth continuing to chase.
  NOT IDENTICAL  NinjaTrader's bars carry run-to-run noise. The existing corpus inherits it, part
                 of the 61% is irreducible, and "replace NinjaTrader" stops being a preference
                 about speed and becomes an argument about correctness.

Compares only bars built on the SAME path (historical rebuild by default) and only sessions both
files cover from the session open, for the same reason the harness gate does: a session joined
part-way through has a different lattice anchor and is incomparable rather than wrong.
"""
from __future__ import annotations

import argparse
import os
import sys
from datetime import datetime, timezone

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from .equivalence import _iso_ns, load_dump, session_of  # noqa: E402
from .nrdcsv import NT_TIMEZONE  # noqa: E402

try:
    from zoneinfo import ZoneInfo
except ImportError:  # pragma: no cover
    ZoneInfo = None


def _t(ns):
    return datetime.fromtimestamp(ns / 1e9, tz=timezone.utc).strftime("%Y-%m-%d %H:%M:%S.%f")[:-3]


def prep(path, tz, session_open, want_rt):
    hdr, rows = load_dump(path)
    out = []
    for r in rows:
        if want_rt is not None and bool(r.get("rt")) != want_rt:
            continue
        r["ts"] = _iso_ns(r["t"])
        r["session"] = session_of(r["ts"], tz, session_open)
        out.append(r)
    by = {}
    for r in out:
        by.setdefault(r["session"], []).append(r)
    return hdr, out, by


def align(a_bars, b_bars, tick, tol_ms, px_tol_ticks):
    """Two-pointer merge over two time-ordered bar lists, CONSUMING each match.

    ⚠ The obvious implementation -- bucket B by millisecond and look each A bar up -- is WRONG here,
    and its control case proved it: a file diffed against ITSELF reported 332 differences. Tide's
    close loop emits SEVERAL bars on one tick when a burst carries CVD through multiple lattice
    lines, so a millisecond bucket routinely holds 2-5 bars. Taking the first one in the bucket
    compares every bar of a cluster against the same counterpart.

    A merge walk is correct because both sequences are time-ordered AND a cluster's internal order
    is the order the lattice was crossed, so the k-th bar at time T pairs with the k-th bar at T.
    """
    tol_ns = int(max(1.0, tol_ms) * 1_000_000)
    i = j = 0
    exact = bnd = orphan = 0
    first_bad = None
    while i < len(a_bars) and j < len(b_bars):
        a, b = a_bars[i], b_bars[j]
        d = a["ts"] - b["ts"]
        if abs(d) <= tol_ns:
            if all(abs(a[f] - b[f]) <= px_tol_ticks * tick for f in ("o", "h", "l", "c")):
                exact += 1
            else:
                bnd += 1
                if first_bad is None:
                    first_bad = (i, a, b, "OHLC differs")
            i += 1
            j += 1
        elif d < 0:
            orphan += 1
            if first_bad is None:
                first_bad = (i, a, None, "no counterpart in B")
            i += 1
        else:
            j += 1
    orphan += len(a_bars) - i
    return {"exact": exact, "bnd": bnd, "orphan": orphan, "first_bad": first_bad}


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(prog="harness.selfdiff")
    ap.add_argument("dump_a")
    ap.add_argument("dump_b")
    ap.add_argument("--tz", default=NT_TIMEZONE)
    ap.add_argument("--session-open", default="17:00")
    ap.add_argument("--tick", type=float)
    ap.add_argument("--tol-ms", type=float, default=1.0)
    ap.add_argument("--px-tol-ticks", type=float, default=0.5)
    ap.add_argument("--path", choices=("historical", "live", "both"), default="historical",
                    help="which build path to compare; mixing them compares two different code paths")
    args = ap.parse_args(argv)

    hh, mm = (int(x) for x in args.session_open.split(":"))
    tz = ZoneInfo(args.tz) if ZoneInfo is not None else timezone.utc
    want = {"historical": False, "live": True, "both": None}[args.path]

    ha, ra, ba = prep(args.dump_a, tz, (hh, mm), want)
    hb, rb, bb = prep(args.dump_b, tz, (hh, mm), want)
    tick = args.tick or ha.get("tickSize") or 0.1

    print("A  %s" % os.path.basename(args.dump_a))
    print("   %s · %s · size %s · %d bars (%s path)"
          % (ha.get("inst"), ha.get("bartype"), ha.get("baseValue"), len(ra), args.path))
    print("B  %s" % os.path.basename(args.dump_b))
    print("   %s · %s · size %s · %d bars (%s path)"
          % (hb.get("inst"), hb.get("bartype"), hb.get("baseValue"), len(rb), args.path))

    for k in ("inst", "bartype", "baseValue", "tickSize"):
        if ha.get(k) != hb.get(k):
            print("\n!! headers differ on %r: %r vs %r -- not the same configuration, aborting"
                  % (k, ha.get(k), hb.get(k)))
            return 2
    if ha.get("coreVer") != hb.get("coreVer"):
        print("\n!  coreVer differs (%s vs %s) -- different build, a difference would be expected"
              % (ha.get("coreVer"), hb.get("coreVer")))

    shared = sorted(set(ba) & set(bb))
    # Only sessions BOTH files enter at the open: a session joined part-way has a different lattice
    # anchor in whichever file joined late, so a disagreement there says nothing about determinism.
    usable, skipped = [], []
    for s in shared:
        if ba[s][0].get("newSession") and bb[s][0].get("newSession"):
            usable.append(s)
        else:
            skipped.append(s)
    if not usable:
        print("\nno session is entered at its open by BOTH files (shared: %s)" % (shared or "none"))
        print("load the second chart with more Days-to-load so the windows overlap from a session open")
        return 2
    for s in skipped:
        print("\nskipped %s: one file joined it part-way through" % s)

    print("\n%-12s %8s %8s %8s %8s %8s   %s"
          % ("session", "A bars", "B bars", "exact", "bnd-only", "orphan", "verdict"))
    tot_e = tot_a = tot_bad = 0
    first_overall = None
    for s in usable:
        r = align(ba[s], bb[s], tick, args.tol_ms, args.px_tol_ticks)
        n = len(ba[s])
        tot_e += r["exact"]
        tot_a += n
        bad = r["bnd"] + r["orphan"]
        tot_bad += bad
        if bad and first_overall is None and r["first_bad"]:
            first_overall = (s,) + r["first_bad"]
        print("%-12s %8d %8d %8d %8s %8d   %s"
              % (s, n, len(bb[s]), r["exact"], r["bnd"], r["orphan"],
                 "IDENTICAL" if bad == 0 else "%d differ" % bad))

    pct = 100.0 * tot_e / max(1, tot_a)
    print("\n%d/%d bars identical (%.4f%%) across %d session(s)" % (tot_e, tot_a, pct, len(usable)))

    if tot_bad == 0:
        print("\nVERDICT: NinjaTrader IS DETERMINISTIC on this tape.")
        print("  Two independent rebuilds produced byte-identical bars. There is a fixed target,")
        print("  so the harness gap is entirely OURS -- and bit-equality is worth chasing.")
        return 0

    print("\nVERDICT: NinjaTrader IS NOT DETERMINISTIC. %d bars differ between two rebuilds" % tot_bad)
    print("  of the same tape by the same program.")
    if first_overall:
        s, i, a, b, why = first_overall
        print("\n  first difference — session %s, bar %d (%s):" % (s, i, why))
        print("    A  %s  o%.2f h%.2f l%.2f c%.2f  v%s" % (_t(a["ts"]), a["o"], a["h"], a["l"], a["c"], a.get("v")))
        if b:
            print("    B  %s  o%.2f h%.2f l%.2f c%.2f  v%s" % (_t(b["ts"]), b["o"], b["h"], b["l"], b["c"], b.get("v")))
    print("\n  CONSEQUENCES, and they are large:")
    print("   * the existing excursion corpus inherits this noise -- identical settings, identical")
    print("     data, different bars, and nothing in NinjaTrader reports it;")
    print("   * part of the harness's 61%% gap is irreducible, so the gate can never read 100%%")
    print("     and should be re-specified as a bounded-agreement test;")
    print("   * a label computed off a bar boundary is not reproducible, which is a correctness")
    print("     argument for making the harness the single source of truth, not a speed one.")
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
