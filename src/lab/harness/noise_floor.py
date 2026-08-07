#!/usr/bin/env python3
"""noise_floor — how precise is each observable, before anyone interprets a difference in it?

THE MISTAKE THIS PREVENTS, WHICH WAS ALREADY MADE ONCE
------------------------------------------------------
§5.6b of the whitepaper reported that H differs between GC and MGC on matched dates, concluded flow
persistence tracks participant mix, and was published. Two more matched windows flipped the sign. The
"effect" was 0.027 against a window-to-window spread that turned out to be ~0.05 within a single
contract — i.e. it was never resolvable, and nobody had measured the noise floor to notice.

So: measure each observable's WITHIN-CONTRACT, ACROSS-WINDOW dispersion first. That number is the
resolution limit of the instrument. Any between-group difference smaller than it is uninterpretable
no matter how clean the arithmetic looks, and any difference much larger than it is real.

Reported per observable:
    mean, SD across windows, and the DETECTION FLOOR (2 SD) -- the smallest difference worth a
    sentence.

Different observables computed from the SAME data can have wildly different precision. Establishing
that separately is the point: it tells you which of your findings you are entitled to believe.
"""
from __future__ import annotations

import argparse
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from .flowscale import aggregate_same_instant, ols, signed_flow, variance_scaling  # noqa: E402
from .nrdcsv import ASK, BID, LAST, iter_l1  # noqa: E402
from .regime_study import CSV_ROOT, count_trades  # noqa: E402


def measure(paths):
    ticks = []
    bid = ask = 0.0
    for p in paths:
        for t in iter_l1(p, types=(ASK, BID, LAST)):
            if t.kind == BID:
                bid = t.price
            elif t.kind == ASK:
                ask = t.price
            else:
                ticks.append((t.ts_ns, t.price, t.volume, bid, ask))
    if len(ticks) < 20000:
        return None
    agg = aggregate_same_instant(ticks)
    sweep = 100.0 * (1 - len(agg) / len(ticks))
    sf = signed_flow(agg, True)
    pts = variance_scaling(sf, [2 ** k for k in range(1, 15)])
    if len(pts) < 4:
        return None
    _, b, _ = ols([math.log(n) for n, _, _ in pts], [math.log(v) for _, v, _ in pts])
    return {"prints": len(ticks), "sweep": sweep, "H": b / 2.0}


def stats(xs):
    n = len(xs)
    m = sum(xs) / n
    sd = math.sqrt(sum((x - m) ** 2 for x in xs) / (n - 1)) if n > 1 else float("nan")
    return m, sd


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(prog="harness.noise_floor")
    ap.add_argument("--contracts", nargs="+", required=True)
    ap.add_argument("--windows", type=int, default=4, help="non-overlapping windows per contract")
    ap.add_argument("--days", type=int, default=5)
    args = ap.parse_args(argv)

    rows = []
    for name in args.contracts:
        d = os.path.join(CSV_ROOT, name)
        if not os.path.isdir(d):
            print("… %s: not converted" % name)
            continue
        files = sorted(f for f in os.listdir(d) if f.endswith(".csv"))
        live = [f for f in files if count_trades(os.path.join(d, f)) >= 1000]
        got = []
        for k in range(args.windows):
            lo = k * args.days
            if lo + args.days > len(live):
                break
            m = measure([os.path.join(d, f) for f in live[lo:lo + args.days]])
            if m:
                m["window"] = live[lo][:8] + ".." + live[lo + args.days - 1][:8]
                got.append(m)
        if not got:
            print("… %s: no usable window" % name)
            continue
        print("\n%s  (%d live day files)" % (name, len(live)))
        print("  %-20s %10s %8s %8s" % ("window", "prints", "sweep%", "H"))
        for m in got:
            print("  %-20s %10d %7.2f%% %8.4f" % (m["window"], m["prints"], m["sweep"], m["H"]))
        if len(got) > 1:
            sm, ss = stats([m["sweep"] for m in got])
            hm, hs = stats([m["H"] for m in got])
            print("  %-20s %10s %7.2f%% %8.4f   <- mean" % ("", "", sm, hm))
            print("  %-20s %10s %7.2f%% %8.4f   <- SD" % ("", "", ss, hs))
            rows.append((name, len(got), sm, ss, hm, hs))

    if not rows:
        return 2
    print("\n=== DETECTION FLOORS (2 SD within a contract) ===")
    print("%-12s %5s %10s %10s %10s %10s" % ("contract", "n", "sweep mean", "sweep 2SD", "H mean", "H 2SD"))
    for name, n, sm, ss, hm, hs in rows:
        print("%-12s %5d %9.2f%% %9.2f%% %10.4f %10.4f" % (name, n, sm, 2 * ss, hm, 2 * hs))

    sw2 = sum(2 * r[3] for r in rows) / len(rows)
    h2 = sum(2 * r[5] for r in rows) / len(rows)
    print("\npooled detection floor:  sweep %.2f points   ·   H %.4f" % (sw2, h2))
    print("\nA between-group difference SMALLER than its floor is not interpretable, however clean")
    print("the arithmetic. Compare against the claims on record:")
    print("  sweep, 19-month trend      13.7 points   -> %s" % ("RESOLVABLE" if 13.7 > sw2 else "NOT resolvable"))
    print("  sweep, GC vs MGC size       4.4 points   -> %s" % ("RESOLVABLE" if 4.4 > sw2 else "NOT resolvable"))
    print("  H, GC vs MGC (retracted)      0.027      -> %s" % ("RESOLVABLE" if 0.027 > h2 else "NOT resolvable"))
    print("  H, GC regime spread           0.021      -> %s" % ("RESOLVABLE" if 0.021 > h2 else "NOT resolvable"))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
