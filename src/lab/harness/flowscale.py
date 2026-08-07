#!/usr/bin/env python3
"""flowscale — is signed order flow a random walk, and does the bar-count ladder actually say so?

WHY
---
The Tide size sweep on 2025-12-16 produced ~3x more bars per halving of the quantum, where a
DIFFUSION would give 4x (lattice crossings scale as 1/Δ²). Read naively that implies a persistent
flow path with Hurst H ≈ 0.62 -- and if true it matters more than any voter, because it would mean
the aggression path itself carries structure that is not a function of the OHLC it produced.

BUT THE NAIVE READING IS PROBABLY WRONG, AND THIS EXISTS TO FIND OUT
--------------------------------------------------------------------
A crossing count has TWO regimes and they bracket the observation:

  * Δ >> print size  -- the diffusive regime. Crossings ∝ V/Δ², so halving Δ gives 4x.
  * Δ ~  print size  -- the discrete regime. A single print jumps several lattice lines at once, so
                        crossings saturate at (total |signed volume|)/Δ ∝ 1/Δ, and halving gives 2x.

Anything measured BETWEEN those scales lands between 2x and 4x for purely mechanical reasons, with
no persistence involved at all. And the measured ratios already lean that way: 2.95, 3.04, 3.34 --
RISING with Δ, exactly the shape of a crossover toward the diffusive limit, not the flat line a
constant Hurst exponent would produce.

So the bar-count ladder cannot answer the question. It confounds the property of the tape with the
geometry of the measuring instrument. This module therefore runs two tests:

  (a) THE LADDER, with LOCAL slopes per adjacent pair, so a crossover is visible as a trend rather
      than being averaged into one misleading number.
  (b) VARIANCE-OF-INCREMENTS SCALING in TRADE TIME, which has no lattice in it at all:
          Var[CVD(i+n) - CVD(i)] ∝ n^(2H)
      Independent of Δ, so it cannot be fooled by the crossover. Trade time (n prints) rather than
      wall time is the natural clock for flow and removes the intraday activity cycle for free.

READING THE RESULT
------------------
  (a) drifts AND (b) says H ≈ 0.5  ->  the 3x was an instrument artefact. Flow is a random walk at
                                       these scales and the sweep says nothing about edge.
  (a) flat  AND (b) says H > 0.5   ->  persistent flow, and worth real work.
  they disagree                    ->  trust (b); it is the one without the confound.

Deliberately label-free: no outcomes, no holdout, nothing to overfit. It measures the tape's own
geometry, which is the one class of result that cheap search cannot manufacture.
"""
from __future__ import annotations

import argparse
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from .nrdcsv import ASK, BID, LAST, iter_l1  # noqa: E402
from .tide import EWMA_ALPHA, WINSOR_MULT, TideConfig, TideClock  # noqa: E402

DEFAULT_CSV_DIR = r"C:\Users\Administrator\Documents\NinjaTrader 8\db\replay.csv\GC 02-26"


def load_ticks(paths):
    """(ts_ns, price, volume, bid, ask) for every Last print, with the prevailing quote attached."""
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


def aggregate_same_instant(ticks):
    """Collapse prints sharing an exact timestamp into one.

    THE CONFOUND THIS TESTS. One aggressive order sweeping several resting orders is reported as
    several prints at the same instant, all on the same side. That manufactures short-lag
    persistence mechanically -- it is ONE decision, not a sequence of them -- and it is the leading
    alternative explanation for H ≈ 0.70 at short lags. If H collapses toward 0.5 once same-instant
    prints are merged, the short-horizon persistence was sweep geometry; if it survives, the
    persistence is between DISTINCT arrivals, which is the thing worth trading.
    """
    out = []
    for t in ticks:
        if out and out[-1][0] == t[0] and (t[1] >= t[4] > 0) == (out[-1][1] >= out[-1][4] > 0):
            ts, px, vol, bid, ask = out[-1]
            out[-1] = (ts, t[1], vol + t[2], bid, ask)
        else:
            out.append(t)
    return out


def signed_flow(ticks, winsorize):
    """Signed print sizes using Tide's own rule, so (b) measures the SAME series Tide clocks on."""
    sf = []
    ewma = 0.0
    last_px = 0.0
    last_sign = 0
    for _ts, px, vol, bid, ask in ticks:
        if vol <= 0:
            continue
        v = float(vol)
        ewma = v if ewma <= 0 else ewma + EWMA_ALPHA * (v - ewma)
        if winsorize:
            cap = ewma * WINSOR_MULT
            if cap > 0 and v > cap:
                v = cap
        if ask > 0 and bid > 0 and ask > bid:
            sign = 1 if px >= ask else (-1 if px <= bid else 0)
        else:
            sign = 1 if (last_px > 0 and px > last_px) else (-1 if (last_px > 0 and px < last_px) else last_sign)
        if sign != 0:
            last_sign = sign
            sf.append(sign * v)
        else:
            sf.append(0.0)
        last_px = px
    return sf


def ladder(ticks, sizes, tick_size):
    """(a) bar counts per quantum, FLOW closes only -- backstop bars carry no full quantum."""
    rows = []
    for s in sizes:
        clock = TideClock(TideConfig(s, tick_size))
        for ts, px, vol, bid, ask in ticks:
            clock.on_tick(ts, px, vol, bid, ask)
        flow = sum(1 for b in clock.bars if b.reason == "flow")
        rows.append((s, len(clock.bars), flow))
    return rows


def variance_scaling(sf, lags):
    """(b) Var of n-print CVD increments. Returns [(n, var, count)] for lags with enough samples."""
    n_total = len(sf)
    cum = [0.0] * (n_total + 1)
    run = 0.0
    for i, v in enumerate(sf):
        run += v
        cum[i + 1] = run

    out = []
    for n in lags:
        if n_total < n * 8:
            continue
        stride = max(1, n // 4)          # overlapping windows, decimated so cost stays linear-ish
        s1 = s2 = 0.0
        c = 0
        i = 0
        while i + n <= n_total:
            d = cum[i + n] - cum[i]
            s1 += d
            s2 += d * d
            c += 1
            i += stride
        if c < 30:
            continue
        mean = s1 / c
        var = s2 / c - mean * mean
        if var > 0:
            out.append((n, var, c))
    return out


def ols(xs, ys):
    n = len(xs)
    mx = sum(xs) / n
    my = sum(ys) / n
    sxx = sum((x - mx) ** 2 for x in xs)
    sxy = sum((x - mx) * (y - my) for x, y in zip(xs, ys))
    b = sxy / sxx if sxx else float("nan")
    a = my - b * mx
    ss_res = sum((y - (a + b * x)) ** 2 for x, y in zip(xs, ys))
    ss_tot = sum((y - my) ** 2 for y in ys)
    r2 = 1 - ss_res / ss_tot if ss_tot else float("nan")
    return a, b, r2


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(prog="harness.flowscale")
    ap.add_argument("--csv-dir", default=DEFAULT_CSV_DIR)
    ap.add_argument("--days", nargs="+", default=["20251215", "20251216", "20251217", "20251218", "20251219"])
    ap.add_argument("--tick", type=float, default=0.1)
    ap.add_argument("--sizes", default="12.5,25,50,100,200,400,800")
    ap.add_argument("--skip-ladder", action="store_true", help="run only the lattice-free test (b)")
    args = ap.parse_args(argv)

    paths = [os.path.join(args.csv_dir, d + ".csv") for d in args.days]
    paths = [p for p in paths if os.path.exists(p)]
    if not paths:
        print("no CSV files found in %s" % args.csv_dir)
        return 2
    print("loading %d day file(s)..." % len(paths))
    ticks = load_ticks(paths)
    print("  %d trade prints" % len(ticks))

    # ---- (a) the ladder, with LOCAL slopes -------------------------------------------------
    sizes = [float(x) for x in args.sizes.split(",")]
    rows = [] if args.skip_ladder else ladder(ticks, sizes, args.tick)
    if rows:
        print("\n(a) BAR-COUNT LADDER — local slope per adjacent pair")
        print("    diffusion => 4.00x per halving   ·   discrete/ballistic => 2.00x")
        print("    %-8s %9s %9s %8s %10s" % ("size", "bars", "flow", "backstop", "x vs next"))
    for i, (s, tot, flow) in enumerate(rows):
        bs = tot - flow
        ratio = ""
        if i + 1 < len(rows):
            nxt = rows[i + 1]
            steps = math.log(rows[i + 1][0] / s, 2)     # halvings between the two rungs
            if flow > 0 and nxt[2] > 0 and steps:
                ratio = "%.2fx" % ((flow / nxt[2]) ** (1.0 / steps))
        flag = "  <-- contaminated" if tot and bs > 0.05 * tot else ""
        print("    %-8g %9d %9d %8d %10s%s" % (s, tot, flow, bs, ratio, flag))
    clean = [(s, f) for s, t, f in rows if t and (t - f) <= 0.05 * t and f > 30]
    if len(clean) >= 3:
        a, b, r2 = ols([math.log(s) for s, _ in clean], [math.log(f) for _, f in clean])
        print("    pooled slope d(log flow bars)/d(log size) = %.3f  (diffusion -2.00, discrete -1.00), R2 %.4f"
              % (b, r2))
        if b < 0:
            print("    => implied H from the ladder = %.3f  [CONFOUNDED - see the module docstring]"
                  % (-1.0 / b))

    # ---- (b) variance scaling in trade time ------------------------------------------------
    lags = [2 ** k for k in range(1, 15)]
    agg = aggregate_same_instant(ticks)
    print("\n(b) VARIANCE-OF-INCREMENTS in TRADE TIME — no lattice, so no crossover confound")
    print("    same-instant merge: %d prints -> %d arrivals (%.1f%% were part of a sweep)"
          % (len(ticks), len(agg), 100.0 * (1 - len(agg) / max(1, len(ticks)))))
    for label, wins, src in (("raw prints             ", False, ticks),
                             ("winsorized (Tide's own)", True, ticks),
                             ("SWEEP-MERGED arrivals  ", True, agg)):
        sf = signed_flow(src, wins)
        pts = variance_scaling(sf, lags)
        if len(pts) < 4:
            print("    %s: too few usable lags" % label)
            continue
        a, b, r2 = ols([math.log(n) for n, _, _ in pts], [math.log(v) for _, v, _ in pts])
        H = b / 2.0
        print("    %s  slope %.4f => H = %.4f   (R2 %.4f, %d lags, n=%d prints)"
              % (label, b, H, r2, len(pts), len(sf)))
        if True:
            lo = [p for p in pts if p[0] <= 64]
            hi = [p for p in pts if p[0] >= 256]
            if len(lo) >= 3 and len(hi) >= 3:
                _, bl, _ = ols([math.log(n) for n, _, _ in lo], [math.log(v) for _, v, _ in lo])
                _, bh, _ = ols([math.log(n) for n, _, _ in hi], [math.log(v) for _, v, _ in hi])
                print("        short lags (<=64 prints)  H = %.4f" % (bl / 2))
                print("        long  lags (>=256 prints) H = %.4f" % (bh / 2))
                print("        (a single H should hold across both; a split means scale-dependent structure)")

    print("\n    H = 0.50 -> random walk, no exploitable structure in the flow PATH itself.")
    print("    H > 0.50 -> persistent. H < 0.50 -> mean-reverting (flow gets absorbed).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
