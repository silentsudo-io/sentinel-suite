#!/usr/bin/env python3
"""pairing_study — the complete read on trade/quote pairing: does it explain the gate, and does it
                   threaten anything we have already claimed?

THE FINDING THIS INVESTIGATES
-----------------------------
A print-by-print trace of the first divergence showed the same price signing +1, then -1, then +1
inside 8 ms, because the quote oscillated underneath it -- and seven prints sharing ONE timestamp
with the quote moving mid-cluster. Quote-rule signing is order-sensitive at sub-millisecond
resolution, the export flattens NinjaTrader's event ordering into row order, and we cannot observe
what that ordering was. So bit-equality may be unreachable for reasons that are not a defect in
either implementation.

That reframes the question from "are we identical?" to two answerable ones:

  Q1  Does some OTHER pairing rule reproduce NinjaTrader? If one jumps to high agreement, we have
      simply been pairing differently, and it is fixable after all.
  Q2  If not -- how much does the choice MATTER? Every flow-derived result we have claimed
      (H ~= 0.60, the sweep fraction, the bar-count ladder) rests on signed flow. If those numbers
      move when the pairing rule changes, they were artefacts of an arbitrary choice and must be
      withdrawn. If they are stable across all four rules, they are properties of the tape and
      survive regardless of how the gate lands.

Q2 is the important one. Q1 only decides how good the harness is at imitation; Q2 decides whether
anything in the whitepaper is true.

THE FOUR RULES
--------------
  stream  quote updated in row order; a trade signs against the most recent quote. (What we ship.)
  pre     every trade in a same-timestamp cluster signs against the quote as it stood BEFORE the
          cluster -- i.e. quotes at timestamp T are not visible to trades at T.
  post    ...against the quote AFTER the whole cluster is applied. The opposite extreme.
  tick    quotes ignored entirely; Lee-Ready tick rule only. Not a pairing at all -- a floor. If a
          result survives even this, it cannot be an artefact of quote handling.

`pre` and `post` BRACKET every possible within-timestamp ordering, so the spread between them is a
genuine bound on the ambiguity, not a sample of it.
"""
from __future__ import annotations

import argparse
import math
import os
import sys
from datetime import timezone

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from .equivalence import _iso_ns, align_session, csv_files_for, load_dump, session_of  # noqa: E402
from .flowscale import ols, variance_scaling  # noqa: E402
from .nrdcsv import ASK, BID, LAST, NT_TIMEZONE, iter_l1  # noqa: E402
from .tide import EWMA_ALPHA, WINSOR_MULT, TideConfig, TideClock  # noqa: E402

try:
    from zoneinfo import ZoneInfo
except ImportError:  # pragma: no cover
    ZoneInfo = None

DEFAULT_CSV_DIR = r"C:\Users\Administrator\Documents\NinjaTrader 8\db\replay.csv\GC 02-26"
MODES = ("stream", "pre", "post", "tick")


def build_feed(paths, mode):
    """Materialise (ts, px, vol, bid, ask) for every Last print under one pairing rule.

    Held in memory rather than re-read per use so that the bar clock and the flow statistics see
    byte-identical input -- comparing two results computed from two separate passes would leave a
    hole exactly where this study needs certainty.
    """
    out = []
    bid = ask = 0.0
    if mode == "stream" or mode == "tick":
        for path in paths:
            for t in iter_l1(path, types=(ASK, BID, LAST)):
                if t.kind == BID:
                    bid = t.price
                elif t.kind == ASK:
                    ask = t.price
                elif mode == "tick":
                    out.append((t.ts_ns, t.price, t.volume, 0.0, 0.0))
                else:
                    out.append((t.ts_ns, t.price, t.volume, bid, ask))
        return out

    # pre / post: buffer each same-timestamp cluster, then attach the quote from the chosen edge.
    cluster_ts = None
    trades = []
    pre_bid = pre_ask = 0.0

    def flush():
        q = (pre_bid, pre_ask) if mode == "pre" else (bid, ask)
        for (ts, px, vol) in trades:
            out.append((ts, px, vol, q[0], q[1]))

    for path in paths:
        for t in iter_l1(path, types=(ASK, BID, LAST)):
            if t.ts_ns != cluster_ts:
                if trades:
                    flush()
                trades = []
                cluster_ts = t.ts_ns
                pre_bid, pre_ask = bid, ask
            if t.kind == BID:
                bid = t.price
            elif t.kind == ASK:
                ask = t.price
            else:
                trades.append((t.ts_ns, t.price, t.volume))
    if trades:
        flush()
    return out


def sign_of(px, bid, ask, last_px, last_sign):
    if ask > 0 and bid > 0 and ask > bid:
        if px >= ask:
            return 1
        if px <= bid:
            return -1
        return 0
    if last_px > 0 and px > last_px:
        return 1
    if last_px > 0 and px < last_px:
        return -1
    return last_sign


def signed_series(feed, winsorize=True):
    """The signed, winsorized print sizes Tide clocks on -- and the raw signs, for comparison."""
    sf, signs = [], []
    ewma = 0.0
    last_px = 0.0
    last_sign = 0
    for ts, px, vol, bid, ask in feed:
        if vol <= 0:
            signs.append(0)
            sf.append(0.0)
            continue
        v = float(vol)
        ewma = v if ewma <= 0 else ewma + EWMA_ALPHA * (v - ewma)
        if winsorize:
            cap = ewma * WINSOR_MULT
            if cap > 0 and v > cap:
                v = cap
        s = sign_of(px, bid, ask, last_px, last_sign)
        if s != 0:
            last_sign = s
        last_px = px
        signs.append(s)
        sf.append(s * v)
    return sf, signs


def run_clock(feed, cfg):
    clock = TideClock(cfg)
    for ts, px, vol, bid, ask in feed:
        clock.on_tick(ts, px, vol, bid, ask)
    return clock


def hurst(sf):
    pts = variance_scaling(sf, [2 ** k for k in range(1, 15)])
    if len(pts) < 4:
        return float("nan"), float("nan"), 0
    _, b, r2 = ols([math.log(n) for n, _, _ in pts], [math.log(v) for _, v, _ in pts])
    return b / 2.0, r2, len(pts)


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(prog="harness.pairing_study")
    ap.add_argument("--dump", help="SentinelBarDump answer key (omit to skip the NT comparison)")
    ap.add_argument("--csv-dir", default=DEFAULT_CSV_DIR)
    ap.add_argument("--days", nargs="+",
                    default=["20251208", "20251209", "20251210", "20251211", "20251212"])
    ap.add_argument("--size", type=float, default=25)
    ap.add_argument("--tick", type=float, default=0.1)
    ap.add_argument("--tz", default=NT_TIMEZONE)
    ap.add_argument("--session-open", default="17:00")
    args = ap.parse_args(argv)

    hh, mm = (int(x) for x in args.session_open.split(":"))
    tz = ZoneInfo(args.tz) if ZoneInfo is not None else timezone.utc
    cfg = TideConfig(args.size, args.tick, args.tz, (hh, mm))

    paths = [os.path.join(args.csv_dir, d + ".csv") for d in args.days]
    paths = [p for p in paths if os.path.exists(p)]
    if not paths:
        print("no CSV files found")
        return 2

    # --- NinjaTrader's answer key, if we have one -----------------------------------------------
    nt_sessions = {}
    if args.dump:
        _, rows = load_dump(args.dump)
        for r in rows:
            r["ts"] = _iso_ns(r["t"])
            r["session"] = session_of(r["ts"], tz, (hh, mm))
            nt_sessions.setdefault(r["session"], []).append(r)

    print("PAIRING RULE STUDY — %d day file(s), size %g, tick %g" % (len(paths), args.size, args.tick))
    print("pre/post BRACKET every possible within-timestamp ordering.\n")

    base_signs = None
    results = []
    for mode in MODES:
        feed = build_feed(paths, mode)
        sf, signs = signed_series(feed)
        clock = run_clock(feed, cfg)

        buys = sum(1 for s in signs if s > 0)
        sells = sum(1 for s in signs if s < 0)
        zero = len(signs) - buys - sells
        if base_signs is None:
            base_signs = signs
            flips = 0
        else:
            flips = sum(1 for a, b in zip(base_signs, signs) if a != b)

        H, r2, nlag = hurst(sf)

        agree = None
        if nt_sessions:
            hz = {}
            for b in clock.bars:
                hz.setdefault(b.session, []).append(b)
            tot_e = tot_n = 0
            for s in sorted(set(nt_sessions) & set(hz)):
                gap_min = (hz[s][0].ts_close_ns - nt_sessions[s][0]["ts"]) / 6e10
                if gap_min > 5:
                    continue          # export does not cover this session's open
                al = align_session(nt_sessions[s], hz[s], args.tick, 1.0, 0.5)
                tot_e += al["exact"]
                tot_n += len(nt_sessions[s])
            agree = 100.0 * tot_e / max(1, tot_n)

        results.append({
            "mode": mode, "prints": len(signs), "buys": buys, "sells": sells, "zero": zero,
            "flips": flips, "bars": len(clock.bars),
            "flow_bars": sum(1 for b in clock.bars if b.reason == "flow"),
            "H": H, "r2": r2, "agree": agree,
            "cvd_end": sum(sf),
        })
        del feed, sf, signs, clock

    print("%-8s %9s %7s %7s %7s %11s %8s %9s %8s %8s"
          % ("mode", "prints", "buy%", "sell%", "none%", "flips-vs-str", "bars", "NT-agree", "H", "R2"))
    for r in results:
        p = max(1, r["prints"])
        print("%-8s %9d %6.1f%% %6.1f%% %6.1f%% %10.2f%% %8d %8s %8.4f %8.4f"
              % (r["mode"], r["prints"], 100.0 * r["buys"] / p, 100.0 * r["sells"] / p,
                 100.0 * r["zero"] / p, 100.0 * r["flips"] / p, r["bars"],
                 ("%.2f%%" % r["agree"]) if r["agree"] is not None else "-",
                 r["H"], r["r2"]))

    # `post` lets a trade see quotes that arrived AFTER it -- lookahead, and it shows: the quote has
    # already widened past the trade price, so most prints fall inside the spread and sign as ZERO.
    # It is a BRACKET on the ordering, never a candidate rule, and pooling it into a spread or an
    # ambiguity rate overstates both. Judge stability on the PLAUSIBLE rules and report the bracket
    # separately.
    degenerate = [r for r in results if r["zero"] > 0.25 * r["prints"]]
    plausible = [r for r in results if r not in degenerate]
    for r in degenerate:
        print("\n  ! '%s' is DEGENERATE (%.1f%% of prints sign as inside-spread) -- it is a bound on"
              " the ordering, not a candidate rule. Excluded from the stability verdict."
              % (r["mode"], 100.0 * r["zero"] / max(1, r["prints"])))
    qs = plausible
    hs = [r["H"] for r in plausible if not math.isnan(r["H"])]
    hs_all = [r["H"] for r in results if not math.isnan(r["H"])]
    print("\n--- Q1: does another pairing rule reproduce NinjaTrader? ---")
    if any(r["agree"] is not None for r in results):
        best = max((r for r in results if r["agree"] is not None), key=lambda r: r["agree"])
        base = next(r for r in results if r["mode"] == "stream")
        print("  best = %-6s at %.2f%%   (shipped 'stream' = %.2f%%)" % (best["mode"], best["agree"], base["agree"]))
        if best["agree"] - base["agree"] > 15:
            print("  => MATERIALLY BETTER. We have been pairing differently from NinjaTrader; adopt it.")
        else:
            print("  => NO rule reproduces it. The ordering is internal to NinjaTrader and not")
            print("     recoverable from the export. Bit-equality is out of reach; bound it instead.")

    print("\n--- Q2: do the flow findings survive the choice? ---")
    if len(hs) >= 2:
        lo, hi = min(hs), max(hs)
        print("  H over PLAUSIBLE rules: %s   spread %.4f" % (" · ".join("%.4f" % h for h in hs), hi - lo))
        print("  H including the degenerate bracket: %.4f .. %.4f" % (min(hs_all), max(hs_all)))
        if all(h > 0.55 for h in hs_all):
            print("  => The DIRECTION survives every rule, including the quote-free tick rule:")
            print("     flow is persistent (H > 0.55) no matter how trades are paired to quotes.")
        if hi - lo < 0.02:
            print("  => The VALUE is stable to +/-%.3f across plausible rules." % ((hi - lo) / 2))
            print("     Quote it as H = %.2f +/- %.2f, never to four decimals." % ((hi + lo) / 2, (hi - lo) / 2 + 0.005))
        else:
            print("  => The VALUE is NOT stable (spread %.4f). Quote the RANGE, not a point." % (hi - lo))

    # The honest ambiguity rate is between the two PLAUSIBLE orderings. Measuring it against the
    # degenerate rule would report ~72%, which is not a statement about the tape at all -- it is a
    # statement about how badly that rule mis-signs.
    pre = next((r for r in results if r["mode"] == "pre"), None)
    if pre:
        print("\n  within-timestamp ambiguity: %.2f%% of prints sign differently between the two"
              " plausible orderings (stream vs pre)" % (100.0 * pre["flips"] / max(1, pre["prints"])))
        print("  that is the real error bar on any single print's side.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
