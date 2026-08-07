#!/usr/bin/env python3
"""cvddrift — localise WHY the harness and NinjaTrader disagree, down to the individual print.

The equivalence gate reports 68% boundary agreement / 39% full agreement with a consistent bias:
the harness prints ~1% MORE bars every session, i.e. its CVD travels slightly further. That is a
sub-contract difference in the flow accumulator. This finds it.

TWO MODES, and they answer different questions.

  --residuals   Sample the harness's session CVD at each of NinjaTrader's bar-close timestamps.
                If the two implementations were identical, NinjaTrader's closes would land exactly
                on our lattice lines, so `cvd mod deltaPerBrick` would sit near zero (within one
                print's overshoot) forever. Watching that residual, and the running bar-count
                difference, separates the two candidate causes:
                  * residual grows steadily, bar-count difference climbs linearly
                       => a SCALING error. Every print is slightly too big: winsorization, or a
                          volume field we are reading differently.
                  * residual wanders, bar-count difference random-walks
                       => SIGN FLIPS. Individual prints are being attributed to the wrong side,
                          which points at quote/trade pairing rather than magnitude.

  --trace A B   Print-by-print trace between two timestamps: raw volume, the winsorized volume
                actually applied, prevailing bid/ask, the resulting sign, and running CVD, with the
                lattice crossings marked. Run it over the window containing the FIRST divergence
                and the offending print is visible directly. There is no inference left at that
                point -- you are reading the tape the decision was made on.

Timestamps are UTC, matching the SentinelBarDump rows ("HH:MM:SS" or full ISO).
"""
from __future__ import annotations

import argparse
import math
import os
import sys
from datetime import datetime, timedelta, timezone

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from .equivalence import _iso_ns, csv_files_for, load_dump, session_of  # noqa: E402
from .nrdcsv import ASK, BID, LAST, NT_TIMEZONE, iter_l1  # noqa: E402
from .tide import EWMA_ALPHA, WINSOR_MULT, TideConfig, TideClock  # noqa: E402

try:
    from zoneinfo import ZoneInfo
except ImportError:  # pragma: no cover
    ZoneInfo = None

DEFAULT_CSV_DIR = r"C:\Users\Administrator\Documents\NinjaTrader 8\db\replay.csv\GC 02-26"


def _hhmmss(ns):
    return datetime.fromtimestamp(ns / 1e9, tz=timezone.utc).strftime("%H:%M:%S.%f")[:-3]


def _parse_when(s, ref_ns):
    """Accept 'HH:MM:SS(.mmm)' (resolved against the reference day) or a full ISO timestamp."""
    if "T" in s or "-" in s:
        return _iso_ns(s)
    day = datetime.fromtimestamp(ref_ns / 1e9, tz=timezone.utc).strftime("%Y-%m-%d")
    frac = ".000" if "." not in s else ""
    return _iso_ns(day + "T" + s + frac + "Z")


def residuals(nt_ts, files, cfg, every):
    """Walk the tape once, sampling harness state at every NinjaTrader bar close."""
    clock = TideClock(cfg)
    pending = list(nt_ts)
    pending.reverse()          # pop() from the end = chronological
    out = []
    bid = ask = 0.0
    nt_i = 0
    for path in files:
        for t in iter_l1(path, types=(ASK, BID, LAST)):
            if t.kind == BID:
                bid = t.price
                continue
            if t.kind == ASK:
                ask = t.price
                continue
            clock.on_tick(t.ts_ns, t.price, t.volume, bid, ask)
            while pending and pending[-1] <= t.ts_ns:
                ts = pending.pop()
                nt_i += 1
                cvd = clock._cvd
                line = round(cvd / cfg.delta_per_brick) * cfg.delta_per_brick
                out.append((nt_i, ts, cvd, cvd - line, clock._level, len(clock.bars)))
    return out


def trace(a_ns, b_ns, files, cfg):
    """Every print in [a, b], with the winsorized volume actually applied and running CVD."""
    clock = TideClock(cfg)
    bid = ask = 0.0
    rows = []
    for path in files:
        for t in iter_l1(path, types=(ASK, BID, LAST)):
            if t.kind == BID:
                bid = t.price
                continue
            if t.kind == ASK:
                ask = t.price
                continue
            if t.ts_ns > b_ns:
                break
            before_cvd = clock._cvd
            before_bars = len(clock.bars)
            ewma_before = clock._vol_ewma
            clock.on_tick(t.ts_ns, t.price, t.volume, bid, ask)
            if t.ts_ns >= a_ns:
                applied = abs(clock._cvd - before_cvd)
                cap = ewma_before * WINSOR_MULT if ewma_before > 0 else 0.0
                sign = 0
                if clock._cvd > before_cvd:
                    sign = 1
                elif clock._cvd < before_cvd:
                    sign = -1
                rows.append({
                    "ts": t.ts_ns, "px": t.price, "vol": t.volume, "applied": applied,
                    "cap": cap, "bid": bid, "ask": ask, "sign": sign,
                    "cvd": clock._cvd, "closed": len(clock.bars) - before_bars,
                })
    return rows


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(prog="harness.cvddrift")
    ap.add_argument("--dump", required=True)
    ap.add_argument("--csv-dir", default=DEFAULT_CSV_DIR)
    ap.add_argument("--session", help="session label (default: the one with the most NT bars)")
    ap.add_argument("--size", type=float)
    ap.add_argument("--tick", type=float)
    ap.add_argument("--tz", default=NT_TIMEZONE)
    ap.add_argument("--session-open", default="17:00")
    ap.add_argument("--every", type=int, default=100, help="print every Nth sample (--residuals)")
    ap.add_argument("--residuals", action="store_true")
    ap.add_argument("--trace", nargs=2, metavar=("FROM", "TO"))
    args = ap.parse_args(argv)

    hdr, rows = load_dump(args.dump)
    tick = args.tick or hdr.get("tickSize") or 0.1
    size = args.size or hdr.get("baseValue") or 25
    hh, mm = (int(x) for x in args.session_open.split(":"))
    tz = ZoneInfo(args.tz) if ZoneInfo is not None else timezone.utc

    for r in rows:
        r["ts"] = _iso_ns(r["t"])
        r["session"] = session_of(r["ts"], tz, (hh, mm))
    by_sess = {}
    for r in rows:
        by_sess.setdefault(r["session"], []).append(r)
    sess = args.session or max(by_sess, key=lambda s: len(by_sess[s]))
    nt = by_sess[sess]
    files = csv_files_for([sess], args.csv_dir, tz, (hh, mm))
    cfg = TideConfig(size, tick, args.tz, (hh, mm))
    print("session %s · %d NinjaTrader bars · size %g · tick %g · %d day file(s)"
          % (sess, len(nt), size, tick, len(files)))

    if args.trace:
        a = _parse_when(args.trace[0], nt[0]["ts"])
        b = _parse_when(args.trace[1], nt[0]["ts"])
        tr = trace(a, b, files, cfg)
        print("\n%d prints in [%s, %s]\n" % (len(tr), _hhmmss(a), _hhmmss(b)))
        print("  %-13s %9s %6s %8s %8s %9s %9s %5s %11s"
              % ("time", "price", "vol", "applied", "cap", "bid", "ask", "sign", "cvd"))
        for r in tr:
            mark = "  CLOSE" * r["closed"]
            capped = "*" if r["applied"] > 0 and abs(r["applied"] - r["vol"]) > 1e-9 else " "
            edge = ""
            if r["ask"] > 0 and r["px"] >= r["ask"]:
                edge = "@ask"
            elif r["bid"] > 0 and r["px"] <= r["bid"]:
                edge = "@bid"
            else:
                edge = "INSIDE"
            print("  %-13s %9.2f %6d %7.2f%s %8.2f %9.2f %9.2f %5d %11.2f  %-6s%s"
                  % (_hhmmss(r["ts"]), r["px"], r["vol"], r["applied"], capped, r["cap"],
                     r["bid"], r["ask"], r["sign"], r["cvd"], edge, mark))
        ncap = sum(1 for r in tr if r["applied"] > 0 and abs(r["applied"] - r["vol"]) > 1e-9)
        print("\n  %d of %d prints were WINSORIZED (marked *)" % (ncap, len(tr)))
        return 0

    ts_list = [r["ts"] for r in nt]
    samples = residuals(ts_list, files, cfg, args.every)
    print("\n  sampling harness CVD at each NinjaTrader bar close")
    print("  %-6s %-13s %11s %10s %8s %9s %9s"
          % ("nt#", "time", "cvd", "residual", "level", "hz bars", "hz-nt"))
    for i, (n, ts, cvd, res, lvl, hzb) in enumerate(samples):
        if i % args.every and i != len(samples) - 1:
            continue
        print("  %-6d %-13s %11.2f %10.2f %8d %9d %+9d"
              % (n, _hhmmss(ts), cvd, res, lvl, hzb, hzb - n))
    if samples:
        res_abs = [abs(s[3]) for s in samples]
        drift = [s[5] - s[0] for s in samples]
        print("\n  |residual|  mean %.2f · median %.2f · p90 %.2f · max %.2f  (one print is O(1-15))"
              % (sum(res_abs) / len(res_abs), sorted(res_abs)[len(res_abs) // 2],
                 sorted(res_abs)[int(len(res_abs) * .9)], max(res_abs)))
        print("  bar-count drift (harness - nt): first %+d · last %+d · max %+d"
              % (drift[0], drift[-1], max(drift, key=abs)))
        first_bad = next((s for s in samples if abs(s[3]) > cfg.delta_per_brick * 0.4), None)
        if first_bad:
            print("  first sample off-lattice by >40%% of a brick: nt#%d at %s (residual %.2f)"
                  % (first_bad[0], _hhmmss(first_bad[1]), first_bad[3]))
            print("  -> trace it:  --trace %s %s"
                  % (_hhmmss(first_bad[1] - 240_000_000_000), _hhmmss(first_bad[1])))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
