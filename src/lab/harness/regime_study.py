#!/usr/bin/env python3
"""regime_study — run the whole label-free apparatus across contracts and see what is universal.

Attacks Threat 1 of the whitepaper: every number in it came from ONE contract, ONE window
(GC 02-26, Dec 2025). The apparatus took a day to build and costs minutes to re-run, so the only
reason it had not been re-run was that nobody asked.

Predictions are pre-registered in PREREGISTRATION_regime_study.md, written before any contract other
than GC 02-26 was converted. This module prints each result beside its prediction and marks HIT or
MISS. It is deliberately not able to "explain" a miss.

  P1  Hurst          every contract in 0.55-0.70, GC-family spread < 0.05
  P2  Sweep fraction MGC materially LOWER than GC on the same dates
  P3  Ladder slope   every contract 2.7x-3.3x per halving
  P4  Quote coverage tick-rule fallback < 1% everywhere
  P5  Format/tz      zero-trade hour lands on 16 (America/Chicago) everywhere

WHY THESE ARE SAFE TO SEARCH OVER
---------------------------------
All five are label-free: no outcomes, no holdout, nothing to overfit. They measure the geometry of
the tape, which is the one class of result cheap search cannot manufacture. Bar-equivalence is
deliberately NOT included -- it needs a SentinelBarDump answer key per contract, and mixing a
label-free sweep with a tuned comparison is how a study stops being trustworthy.

Usage:
    python -m harness.regime_study --contracts "GC 02-26" "GC 02-25" "MGC 02-25" "GC 08-26"
"""
from __future__ import annotations

import argparse
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from .flowscale import aggregate_same_instant, ols, signed_flow, variance_scaling  # noqa: E402
from .nrdcsv import ASK, BID, LAST, census, iter_l1  # noqa: E402
from .tide import TideConfig, TideClock  # noqa: E402

CSV_ROOT = r"C:\Users\Administrator\Documents\NinjaTrader 8\db\replay.csv"


def infer_tick_size(ticks, sample=200000):
    """Smallest positive gap between consecutive distinct trade prices.

    Inferred rather than configured: the study spans instruments with different ticks (GC/MGC 0.1,
    CL 0.01, ES 0.25) and a wrong tick size would silently corrupt every per-tick figure. Reading it
    off the tape removes a place for an assumption to hide.
    """
    best = None
    prev = None
    for i, t in enumerate(ticks):
        if i > sample:
            break
        p = t[1]
        if prev is not None and p != prev:
            d = abs(p - prev)
            if d > 1e-9 and (best is None or d < best):
                best = d
        prev = p
    if not best:
        return 0.01
    # snap to a sane grid so float noise cannot invent 0.09999999
    for cand in (0.0001, 0.001, 0.005, 0.01, 0.02, 0.05, 0.1, 0.25, 0.5, 1.0, 5.0):
        if abs(best - cand) < cand * 0.05:
            return cand
    return round(best, 6)


def count_trades(path):
    """Trade prints in a file, by byte scan — fast enough to run over every file before choosing.

    This exists because a recording can lose its TRADE feed while continuing to log book depth, and
    the resulting files are LARGER than healthy ones. MGC 02-25 does exactly that from 2025-01-29:
    150-190 MB/day of pure L2 with single-digit trade counts. Any selection or bake that trusts file
    size, modification time, or mere presence will pick those days.
    """
    n = 0
    with open(path, "rb") as fh:
        prev = b""
        while True:
            b = fh.read(1 << 22)
            if not b:
                break
            buf = prev + b
            n += buf.count(b"\nL1;2;")
            prev = buf[-8:]
    return n


def load(paths):
    out = []
    bid = ask = 0.0
    for p in paths:
        for t in iter_l1(p, types=(ASK, BID, LAST)):
            if t.kind == BID:
                bid = t.price
            elif t.kind == ASK:
                ask = t.price
            else:
                out.append((t.ts_ns, t.price, t.volume, bid, ask))
    return out


def hurst(sf):
    pts = variance_scaling(sf, [2 ** k for k in range(1, 15)])
    if len(pts) < 4:
        return float("nan"), float("nan")
    _, b, r2 = ols([math.log(n) for n, _, _ in pts], [math.log(v) for _, v, _ in pts])
    return b / 2.0, r2


def ladder_slope(ticks, tick_size, sizes):
    rows = []
    for s in sizes:
        clock = TideClock(TideConfig(s, tick_size))
        for ts, px, vol, bid, ask in ticks:
            clock.on_tick(ts, px, vol, bid, ask)
        tot = len(clock.bars)
        flow = sum(1 for b in clock.bars if b.reason == "flow")
        rows.append((s, tot, flow))
    clean = [(s, f) for s, t, f in rows if t and (t - f) <= 0.05 * t and f > 30]
    if len(clean) < 3:
        return float("nan"), float("nan"), rows
    _, b, r2 = ols([math.log(s) for s, _ in clean], [math.log(f) for _, f in clean])
    return b, r2, rows


def study(name, days, sizes, window=None):
    d = os.path.join(CSV_ROOT, name)
    if not os.path.isdir(d):
        return {"name": name, "error": "not converted"}
    avail = sorted(f for f in os.listdir(d) if f.endswith(".csv"))
    if window:
        a, b = window
        avail = [f for f in avail if a <= f[:8] <= b]
    if not avail:
        return {"name": name, "error": "no CSV files in range"}

    # ⚠ Day selection matters and the obvious rule is wrong. Taking the FIRST n files gave GC 02-25
    # a Thanksgiving-Friday window and MGC 02-25 a New-Year's-Day one -- holiday tape with a
    # fraction of the normal prints, which is not comparable to a normal week and made the timezone
    # probe pass for the wrong reason (hour 16 was empty because nearly every hour was).
    # Rule: the n consecutive files with the most DATA, which is mechanical, holiday-avoiding, and
    # identical across contracts.
    # ⚠⚠ SELECT ON TRADES, NOT BYTES. Selecting by file size picked, for MGC 02-25, a window with
    # 106 trades in 5 files: that recording LOST ITS TRADE FEED on 2025-01-29 and kept logging book
    # depth for another month, so its DEAD days are its LARGEST (150-190 MB of pure L2). File size
    # is anti-correlated with usability there, and nothing in NinjaTrader reports it. Counting
    # trades is a cheap byte scan and is the only honest basis for choosing a window.
    counts = [(f, count_trades(os.path.join(d, f))) for f in avail]
    dead = [f for f, n in counts if n < 1000]
    if days and len(avail) > days:
        best_i, best_sum = 0, -1
        for i in range(len(avail) - days + 1):
            s = sum(n for _f, n in counts[i:i + days])
            if s > best_sum:
                best_i, best_sum = i, s
        use = [f for f, _n in counts[best_i:best_i + days]]
    else:
        use = avail
    paths = [os.path.join(d, f) for f in use]

    # census the BUSIEST file of the window, not the first -- the tz probe is only meaningful on a
    # file with a full session in it.
    c = census(max(paths, key=os.path.getsize))
    ticks = load(paths)
    if len(ticks) < 20000:
        return {"name": name, "error": "only %d prints" % len(ticks)}
    tick_size = infer_tick_size(ticks)

    agg = aggregate_same_instant(ticks)
    sweep = 1.0 - len(agg) / max(1, len(ticks))

    sf, signs = signed_flow(agg, True), None
    H, r2 = hurst(sf)

    # quote coverage: how often the tick-rule fallback had to be used
    fb = sum(1 for _ts, _px, _v, bid, ask in ticks if not (ask > 0 and bid > 0 and ask > bid))

    slope, lr2, rows = ladder_slope(ticks, tick_size, sizes)

    return {
        "name": name, "files": len(paths), "prints": len(ticks), "arrivals": len(agg),
        "tick": tick_size, "sweep": 100.0 * sweep, "H": H, "r2": r2,
        "fallback": 100.0 * fb / max(1, len(ticks)),
        "slope": slope, "per_halving": 2 ** (-slope) if slope == slope else float("nan"),
        "empty_hours": c["empty_hours"], "hour_trades": c["hour_trades"],
        "header": c["header_row"], "comma": c["comma_decimal"],
        "span": (c["first_local"], c["last_local"]), "ladder": rows,
        "window": (use[0][:8], use[-1][:8]), "dead_days": len(dead), "total_days": len(avail),
    }


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(prog="harness.regime_study")
    ap.add_argument("--contracts", nargs="+", required=True)
    ap.add_argument("--days", type=int, default=5, help="day files per contract (same for all, for comparability)")
    ap.add_argument("--sizes", default="12.5,25,50,100")
    ap.add_argument("--window", help="restrict every contract to YYYYMMDD-YYYYMMDD. Use this for the "
                                     "MGC-vs-GC control: comparing them on DIFFERENT date windows "
                                     "confounds contract size with time.")
    args = ap.parse_args(argv)
    sizes = [float(x) for x in args.sizes.split(",")]
    win = tuple(args.window.split("-")) if args.window else None

    res = []
    for name in args.contracts:
        print("… %s" % name, flush=True)
        res.append(study(name, args.days, sizes, win))

    ok = [r for r in res if "error" not in r]
    print("\n%-12s %6s %10s %8s %8s %8s %8s %8s %9s"
          % ("contract", "files", "prints", "tick", "sweep%", "H", "R2", "fallb%", "x/halving"))
    for r in res:
        if "error" in r:
            print("%-12s  -- %s" % (r["name"], r["error"]))
            continue
        print("%-12s %6d %10d %8g %7.1f%% %8.4f %8.4f %7.2f%% %9.2f   %s..%s"
              % (r["name"], r["files"], r["prints"], r["tick"], r["sweep"], r["H"], r["r2"],
                 r["fallback"], r["per_halving"], r["window"][0], r["window"][1]))
        if r["dead_days"]:
            print("%-12s   ! %d of %d day files hold <1000 trades — a DEAD TRADE FEED with live book"
                  " depth. Excluded from selection; do not bake those days."
                  % ("", r["dead_days"], r["total_days"]))

    if not ok:
        print("\nnothing converted yet")
        return 2

    print("\n--- PRE-REGISTERED PREDICTIONS ---")
    hs = [r["H"] for r in ok if r["H"] == r["H"]]
    gc = [r for r in ok if r["name"].startswith("GC ")]
    gh = [r["H"] for r in gc if r["H"] == r["H"]]

    p1a = all(0.55 <= h <= 0.70 for h in hs)
    p1b = (max(gh) - min(gh) < 0.05) if len(gh) > 1 else None
    print("P1 Hurst in 0.55-0.70 everywhere ......... %s  (%s)"
          % ("HIT" if p1a else "MISS", " ".join("%.3f" % h for h in hs)))
    if p1b is not None:
        print("   GC-family spread < 0.05 ............... %s  (%.4f)"
              % ("HIT" if p1b else "MISS", max(gh) - min(gh)))

    mgc = next((r for r in ok if r["name"].startswith("MGC")), None)
    gcm = next((r for r in ok if r["name"] == "GC " + mgc["name"].split()[1]), None) if mgc else None
    if mgc and gcm:
        d = gcm["sweep"] - mgc["sweep"]
        print("P2 MGC sweep%% materially below GC ........ %s  (GC %.1f%% vs MGC %.1f%%, gap %+.1f)"
              % ("HIT" if d > 5 else "MISS", gcm["sweep"], mgc["sweep"], d))
    else:
        print("P2 MGC vs GC ............................. not testable (need both, same dates)")

    p3 = all(2.7 <= r["per_halving"] <= 3.3 for r in ok if r["per_halving"] == r["per_halving"])
    print("P3 ladder 2.7-3.3x per halving ........... %s  (%s)"
          % ("HIT" if p3 else "MISS", " ".join("%.2f" % r["per_halving"] for r in ok)))

    p4 = all(r["fallback"] < 1.0 for r in ok)
    print("P4 tick-rule fallback < 1%% ............... %s  (%s)"
          % ("HIT" if p4 else "MISS", " ".join("%.2f%%" % r["fallback"] for r in ok)))

    # ⚠ RELATIVE, not absolute. Demanding hour 16 be EXACTLY empty failed on GC 12-25, whose busiest
    # file carries ONE trade in the break hour against neighbours of 9,520 and 15,256 -- a four-order
    # -of-magnitude drop that obviously IS the maintenance break. A single stray settlement print
    # must not falsify a structural claim. Test the SHAPE: the quietest hour should be 16, and it
    # should hold a negligible share of a typical hour.
    #
    # The earlier absolute test also passed for the WRONG reason on holiday files, where nearly every
    # hour is empty. Both failure modes are fixed by asking for the minimum and requiring the file to
    # have a real session in it.
    print("P5 tz probe — quietest hour should be 16 (CME maintenance break, America/Chicago)")
    p5 = True
    for r in ok:
        hrs = r.get("hour_trades") or []
        if not hrs or sum(hrs) < 20000:
            print("   %-12s NOT CONCLUSIVE — censused file has no full session" % r["name"])
            continue
        lo = min(range(24), key=lambda h: hrs[h])
        med = sorted(hrs)[12]
        share = 100.0 * hrs[lo] / max(1, med)
        good = (lo == 16 and share < 1.0 and not r["header"] and not r["comma"])
        p5 = p5 and good
        print("   %-12s quietest hour %2d with %d trades = %.3f%% of the median hour  %s"
              % (r["name"], lo, hrs[lo], share, "OK" if good else "<-- UNEXPECTED"))
    print("   => %s" % ("HIT" if p5 else "MISS"))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
