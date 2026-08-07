#!/usr/bin/env python3
"""equivalence — does the offline harness build the SAME bars NinjaTrader does?

This is the gate the whole harness stands on. A harness not yet proven equal to NT is just a
faster way to be wrong, and the corpus's entire value is that it is trustworthy -- so this runs
BEFORE anything is allowed to depend on harness output.

  ANSWER KEY   `SentinelBarDump` on an NT chart -> Sentinel\\Harness\\bars\\*.jsonl
  CANDIDATE    `harness.tide` over the same tape -> the same bars, or not

WHY IT COMPARES PER SESSION
---------------------------
Tide resets its CVD lattice at every session open, so a session is a self-contained experiment:
its first bar depends on nothing before it. That makes each session independently comparable and
localises any disagreement -- if session A matches and session B does not, the fault is inside B,
not upstream of it. It also sidesteps the chart's arbitrary left edge: NT's first session is
truncated by the lookback window and is skipped rather than reported as a mismatch.

WHAT COUNTS AS A MATCH
----------------------
Bar close time to the millisecond, and O/H/L/C to half a tick. Volume is compared but NOT
required: NT's per-bar volume includes prints the signer skips (inside-spread trades still carry
volume), so a volume disagreement is expected and is reported for information rather than scored.

READING A FAILURE
-----------------
`first divergence` is the number that matters. If a session matches for 400 bars and then splits,
the cause is at bar 400 -- a single print signed differently -- and not a structural difference.
If a session diverges at bar 1, suspect the session boundary (`--session-open`) or the size, which
move every bar at once. Those two failure shapes have completely different causes; the report
separates them on purpose.
"""
from __future__ import annotations

import argparse
import json
import os
import re
import sys
from datetime import datetime, timedelta, timezone

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from lab_faults import swallow  # noqa: E402

from .nrdcsv import NT_TIMEZONE  # noqa: E402
from .tide import TideConfig, run_files  # noqa: E402

try:
    from zoneinfo import ZoneInfo
except ImportError:  # pragma: no cover
    ZoneInfo = None


def load_dump(path):
    """Return (header dict, [bar dicts]) from a SentinelBarDump JSONL."""
    hdr, rows = {}, []
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        for line in fh:
            line = line.strip()
            if not line:
                continue
            try:
                obj = json.loads(line)
            except json.JSONDecodeError as _swex:
                swallow("harness.equiv.parse", _swex, os.path.basename(path))
                continue
            if obj.get("hdr"):
                hdr = obj
            else:
                rows.append(obj)
    return hdr, rows


_FRAC = re.compile(r"\.(\d+)")


def _iso_ns(s):
    """ISO-8601 -> integer nanoseconds UTC, preserving NT's full precision.

    C# `DateTime.ToString("o")` emits SEVEN fractional digits (100-ns ticks). Python's
    `fromisoformat` accepts 1-6 and raises on 7, so parsing a BarDump timestamp naively fails --
    and `float(ts)*1e9` would round away the sub-microsecond digits that distinguish two ticks in
    the same millisecond. Split the fraction out, parse the rest, and re-add it as exact integer
    nanoseconds.
    """
    s = s.strip()
    if s.endswith("Z"):
        s = s[:-1] + "+00:00"
    frac_ns = 0
    m = _FRAC.search(s)
    if m:
        frac_ns = int((m.group(1) + "000000000")[:9])
        s = s[:m.start()] + s[m.end():]
    dt = datetime.fromisoformat(s)
    if dt.tzinfo is None:
        dt = dt.replace(tzinfo=timezone.utc)
    return int(dt.timestamp()) * 1_000_000_000 + frac_ns


def session_of(ts_ns, tz, session_open):
    """Local session label for a UTC timestamp -- the same rule the Tide port uses."""
    local = datetime.fromtimestamp(ts_ns / 1e9, tz=timezone.utc).astimezone(tz)
    hh, mm = session_open
    open_today = local.replace(hour=hh, minute=mm, second=0, microsecond=0)
    start = open_today if local >= open_today else open_today - timedelta(days=1)
    return start.strftime("%Y-%m-%d")


def csv_files_for(sessions, csv_dir, tz, session_open):
    """Exported day files covering these sessions.

    A session opens at 17:00 local on day D and runs to 16:00 on D+1, while an export file for day
    X covers [X-1 23:00, X 23:00) local. So a session spans exactly the files for D and D+1.
    """
    want = set()
    for s in sessions:
        d = datetime.strptime(s, "%Y-%m-%d")
        want.add(d.strftime("%Y%m%d"))
        want.add((d + timedelta(days=1)).strftime("%Y%m%d"))
    out = []
    for name in sorted(want):
        p = os.path.join(csv_dir, name + ".csv")
        if os.path.exists(p):
            out.append(p)
    return out


def align_session(nt_bars, hz_bars, tick, time_tol_ms, px_tol_ticks):
    """Agreement measured by MATCHING BAR BOUNDARIES, not by list index.

    WHY THIS EXISTS. Index comparison is only valid while both sides have produced the same NUMBER
    of bars. The instant one side emits an extra bar, every later index is off by one and identical
    bars score as mismatches -- which is exactly what happened here: bars 98-100 were byte-identical
    and counted as failures because 96/97 had shifted them. The reported 21.5% was an artefact of
    the ruler, not a property of the harness.

    A Tide bar is identified by the tick that closed it, so the close TIME is the natural key. Two
    bars are the same bar if they closed on the same tick; then OHLC is checked independently. That
    separates the two questions worth asking: do we cut the tape in the same places, and given the
    same cut, do we agree on the prices?
    """
    # ⚠ CORRECTED. The first implementation bucketed the harness bars by millisecond and took the
    # FIRST bar in the bucket. Tide's close loop emits SEVERAL bars on one tick whenever a burst
    # carries CVD through multiple lattice lines, so a bucket routinely holds 2-5 bars and every
    # one of them was scored against the same counterpart. That UNDERSTATED agreement. Caught by
    # selfdiff's control case: a dump compared against ITSELF reported 332 differences.
    #
    # A merge walk is correct: both sequences are time-ordered, and within one timestamp the bars
    # appear in the order the lattice was crossed, so the k-th bar at time T pairs with the k-th.
    tol_ns = int(max(1.0, time_tol_ms) * 1_000_000)
    i = j = 0
    exact = boundary_only = orphan_nt = 0
    while i < len(nt_bars) and j < len(hz_bars):
        a, b = nt_bars[i], hz_bars[j]
        d = a["ts"] - b.ts_close_ns
        if abs(d) <= tol_ns:
            if all(abs(a[f] - v) <= px_tol_ticks * tick
                   for f, v in (("o", b.open), ("h", b.high), ("l", b.low), ("c", b.close))):
                exact += 1
            else:
                boundary_only += 1
            i += 1
            j += 1
        elif d < 0:
            orphan_nt += 1
            i += 1
        else:
            j += 1
    orphan_nt += len(nt_bars) - i
    return {
        "exact": exact, "boundary_only": boundary_only, "orphan_nt": orphan_nt,
        "orphan_hz": max(0, len(hz_bars) - (exact + boundary_only)),
    }


def compare_session(nt_bars, hz_bars, tick, time_tol_ms, px_tol_ticks):
    """Walk both sequences in lockstep; report the FIRST place they part company."""
    n = min(len(nt_bars), len(hz_bars))
    matched = 0
    first_div = None
    div_reason = ""
    worst_dt = 0
    vol_mismatch = 0
    for i in range(n):
        a, b = nt_bars[i], hz_bars[i]
        dt_ms = abs(a["ts"] - b.ts_close_ns) / 1e6
        worst_dt = max(worst_dt, dt_ms)
        px_ok = all(abs(a[k] - v) <= px_tol_ticks * tick
                    for k, v in (("o", b.open), ("h", b.high), ("l", b.low), ("c", b.close)))
        if a.get("v") is not None and abs(a["v"] - b.volume) > 0.5:
            vol_mismatch += 1
        if dt_ms <= time_tol_ms and px_ok:
            matched += 1
            continue
        if first_div is None:
            first_div = i
            if dt_ms > time_tol_ms:
                div_reason = ("time off by %.1f ms (nt %s vs harness %s)" % (
                    dt_ms,
                    datetime.fromtimestamp(a["ts"] / 1e9, tz=timezone.utc).strftime("%H:%M:%S.%f")[:-3],
                    datetime.fromtimestamp(b.ts_close_ns / 1e9, tz=timezone.utc).strftime("%H:%M:%S.%f")[:-3]))
            else:
                div_reason = ("price: nt o%.2f h%.2f l%.2f c%.2f vs harness o%.2f h%.2f l%.2f c%.2f"
                              % (a["o"], a["h"], a["l"], a["c"], b.open, b.high, b.low, b.close))
    return {
        "nt": len(nt_bars), "hz": len(hz_bars), "compared": n, "matched": matched,
        "first_div": first_div, "div_reason": div_reason, "worst_dt_ms": worst_dt,
        "vol_mismatch": vol_mismatch,
    }


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(prog="harness.equivalence",
                                 description="Diff harness bars against a SentinelBarDump answer key.")
    ap.add_argument("--dump", required=True, help="Sentinel\\Harness\\bars\\*.jsonl written by SentinelBarDump")
    ap.add_argument("--csv-dir", required=True, help="exported NRD CSV dir for the SAME instrument")
    ap.add_argument("--size", type=float, help="net delta per bar (default: from the dump header)")
    ap.add_argument("--tick", type=float, help="tick size (default: from the dump header)")
    ap.add_argument("--session-open", default="17:00")
    ap.add_argument("--tz", default=NT_TIMEZONE)
    ap.add_argument("--time-tol-ms", type=float, default=1.0)
    ap.add_argument("--px-tol-ticks", type=float, default=0.5)
    ap.add_argument("--skip-partial", action="store_true", default=True,
                    help="skip sessions truncated by the chart's left edge (default on)")
    ap.add_argument("--explain", type=int, default=0, metavar="N",
                    help="print N bars either side of the first divergence, both sides, so the shape "
                         "of the disagreement is visible (extra bar / missing bar / shifted boundary)")
    ap.add_argument("--winsor", type=float, default=4.0,
                    help="winsorization multiplier (C# WinsorMult=4.0). 0 disables clipping. The "
                         "cap is EWMA*mult, so this probes whether a clipped block print is what "
                         "decides which print crosses a lattice line.")
    ap.add_argument("--merge-sweeps", action="store_true",
                    help="collapse same-instant prints into one arrival before clocking (tests whether "
                         "NinjaTrader's playback delivers a sweep as a single OnDataPoint call)")
    ap.add_argument("--realtime-only", action="store_true",
                    help="score only bars built LIVE (rt=true). Use when the chart ran Market Replay "
                         "playback without Tick Replay: the historical rebuild then used the price-body "
                         "proxy and is not comparable, but played bars are true tick-built bars.")
    args = ap.parse_args(argv)

    hdr, rows = load_dump(args.dump)
    if not rows:
        print("no bars in %s -- is SentinelBarDump loaded on the chart?" % args.dump)
        return 2

    tick = args.tick or hdr.get("tickSize") or 0.1
    size = args.size or hdr.get("baseValue") or hdr.get("periodValue")
    if not size:
        print("no bar size in the dump header and none given; pass --size")
        return 2

    print("ANSWER KEY  %s" % os.path.basename(args.dump))
    print("  %s · %s · tickSize %g · size %g · dumpVer %s · coreVer %s"
          % (hdr.get("inst", "?"), hdr.get("barLabel") or hdr.get("bartype", "?"), tick, size,
             hdr.get("dumpVer", "?"), hdr.get("coreVer", "?")))
    print("  %d bars, %d built live / %d rebuilt"
          % (len(rows), sum(1 for r in rows if r.get("rt")), sum(1 for r in rows if not r.get("rt"))))

    hh, mm = (int(x) for x in args.session_open.split(":"))
    tz = ZoneInfo(args.tz) if ZoneInfo is not None else timezone.utc

    if args.realtime_only:
        live = [r for r in rows if r.get("rt")]
        print("  realtime-only: keeping %d of %d bars (built live, not rebuilt)" % (len(live), len(rows)))
        if not live:
            print("  no live bars in this dump -- the chart never played, or Bar Dump saw only history")
            return 2
        rows = live

    for r in rows:
        r["ts"] = _iso_ns(r["t"])
        r["session"] = session_of(r["ts"], tz, (hh, mm))

    nt_sessions: dict = {}
    for r in rows:
        nt_sessions.setdefault(r["session"], []).append(r)

    # A session is only comparable if NinjaTrader's copy of it STARTS at the session open. Tide
    # anchors its CVD lattice there, so a session joined part-way through -- by the chart's left
    # edge, or by playback starting mid-session -- has a different anchor and its bars are not
    # merely offset, they are incomparable. `newSession` on the first bar is the exact test, and it
    # replaces guessing from bar counts. Dropped sessions are always named, never silently omitted.
    incomplete = []
    for s, bars in list(nt_sessions.items()):
        if not bars[0].get("newSession"):
            incomplete.append((s, len(bars)))
    if incomplete and len(incomplete) < len(nt_sessions):
        for s, n in incomplete:
            del nt_sessions[s]

    files = csv_files_for(nt_sessions.keys(), args.csv_dir, tz, (hh, mm))
    if not files:
        print("\nno exported CSV in %s covers the dump's sessions (%s)"
              % (args.csv_dir, ", ".join(sorted(nt_sessions))))
        return 2
    print("\nCANDIDATE   harness.tide over %d exported day file(s)" % len(files))

    # winsor 0 = disable clipping entirely (an effectively infinite cap)
    cfg = TideConfig(size, tick, args.tz, (hh, mm),
                     winsor_mult=(args.winsor if args.winsor > 0 else 1e12))
    clock = run_files(files, cfg, merge_sweeps=args.merge_sweeps)
    if args.merge_sweeps:
        print("            merge-sweeps ON: same-instant prints collapsed into one arrival")
    hz_sessions: dict = {}
    for b in clock.bars:
        hz_sessions.setdefault(b.session, []).append(b)

    # The chart's left edge can start mid-session. When it does, NT's Tide anchored its CVD lattice
    # at the left edge instead of the session open, so those bars are not merely offset -- they are
    # incomparable, and scoring them would report a fault that is really an artefact of the window.
    #
    # But the skip must be EARNED, not assumed. An earlier version dropped the earliest session
    # unconditionally, which silently discarded the ONLY session of a one-day dump and reported
    # "no shared session" -- the self-test's control case caught it. Skip only on evidence
    # (materially fewer bars than the harness built for the same session), never leave nothing, and
    # always say out loud what was dropped.
    both = sorted(set(nt_sessions) & set(hz_sessions))
    skipped = []

    # ...and the SAME test on the harness side, which the first version missed. The export is
    # partitioned into day files; if the file holding a session's OPEN is absent, the harness joins
    # that session late and anchors its lattice to the export's edge instead of the session. Scored
    # blindly it produced a 6.02-hour "disagreement" that was really the CST offset between NT's
    # full session and our truncated one -- a real gap in the export reported as a harness fault.
    hz_late = []
    for s in list(both):
        gap_min = (hz_sessions[s][0].ts_close_ns - nt_sessions[s][0]["ts"]) / 6e10
        if gap_min > 5:
            hz_late.append((s, gap_min))
            both.remove(s)
    if args.skip_partial and len(both) > 1:
        earliest = min(both)
        if len(nt_sessions[earliest]) < 0.9 * len(hz_sessions[earliest]):
            both.remove(earliest)
            skipped.append((earliest, len(nt_sessions[earliest]), len(hz_sessions[earliest])))
    if not both:
        print("\nno session is present in BOTH sets -- nt %s vs harness %s"
              % (sorted(nt_sessions), sorted(hz_sessions)))
        return 2
    for s, g in hz_late:
        print("\nskipped %s: the EXPORT does not cover this session's open -- the harness joins it"
              " %.1f min late, so its lattice is anchored to the export edge, not the session."
              " Convert the preceding day file to score it." % (s, g))
    for s, n in incomplete:
        print("\nskipped %s: NinjaTrader joined this session part-way through (%d bars, first bar is"
              " not a session open) -- its CVD lattice is anchored to the window, not the session" % (s, n))
    for s, n, h in skipped:
        print("\nskipped %s: truncated by the chart's left edge (nt %d bars vs harness %d) --"
              " its lattice anchor is the window, not the session open" % (s, n, h))

    print("\n%-12s %6s %6s %8s %9s  %s" % ("session", "nt", "harness", "matched", "first div", "note"))
    total_m = total_c = 0
    failed = 0
    explained = False
    for s in both:
        res = compare_session(nt_sessions[s], hz_sessions[s], tick, args.time_tol_ms, args.px_tol_ticks)
        total_m += res["matched"]
        total_c += res["compared"]
        ok = res["first_div"] is None and res["nt"] == res["hz"]
        if not ok:
            failed += 1
        note = "OK" if ok else (("count %d vs %d" % (res["nt"], res["hz"])) if res["first_div"] is None
                                else res["div_reason"][:60])
        print("%-12s %6d %6d %8d %9s  %s"
              % (s, res["nt"], res["hz"], res["matched"],
                 "-" if res["first_div"] is None else str(res["first_div"]), note))
        if res["vol_mismatch"]:
            print("%-12s %s" % ("", "  (volume differs on %d bars -- expected, not scored)" % res["vol_mismatch"]))

        if args.explain and res["first_div"] is not None and not explained:
            explained = True
            d = res["first_div"]
            lo, hi = max(0, d - args.explain), d + args.explain + 1
            nb, hb = nt_sessions[s], hz_sessions[s]
            print("\n  --- first divergence, session %s, bar %d ---" % (s, d))
            print("  %-4s | %-14s %8s %8s %8s %8s | %-14s %8s %8s %8s %8s %7s"
                  % ("i", "NT time", "o", "h", "l", "c", "harness time", "o", "h", "l", "c", "dCvd"))
            for i in range(lo, min(hi, max(len(nb), len(hb)))):
                a = nb[i] if i < len(nb) else None
                b = hb[i] if i < len(hb) else None
                at = (datetime.fromtimestamp(a["ts"] / 1e9, tz=timezone.utc).strftime("%H:%M:%S.%f")[:-3]
                      if a else "-")
                bt = (datetime.fromtimestamp(b.ts_close_ns / 1e9, tz=timezone.utc).strftime("%H:%M:%S.%f")[:-3]
                      if b else "-")
                mark = "  " if (a and b and abs(a["ts"] - b.ts_close_ns) < 1e6) else "->"
                print("%s%-4d | %-14s %8.2f %8.2f %8.2f %8.2f | %-14s %8.2f %8.2f %8.2f %8.2f %7.1f"
                      % (mark, i, at,
                         a["o"] if a else 0, a["h"] if a else 0, a["l"] if a else 0, a["c"] if a else 0,
                         bt, b.open if b else 0, b.high if b else 0, b.low if b else 0,
                         b.close if b else 0, b.dcvd if b else 0))
            print()

    print("\nBOUNDARY-ALIGNED AGREEMENT (matched on bar close time, immune to index drift)")
    print("  %-12s %8s %8s %10s %10s %8s" % ("session", "nt bars", "exact", "bnd-only", "nt-orphan", "agree%"))
    tot_e = tot_n = 0
    for s in both:
        al = align_session(nt_sessions[s], hz_sessions[s], tick, args.time_tol_ms, args.px_tol_ticks)
        n = len(nt_sessions[s])
        tot_e += al["exact"]
        tot_n += n
        print("  %-12s %8d %8d %10d %10d %7.2f%%"
              % (s, n, al["exact"], al["boundary_only"], al["orphan_nt"], 100.0 * al["exact"] / max(1, n)))
    print("  %-12s %8d %8d %10s %10s %7.2f%%" % ("TOTAL", tot_n, tot_e, "", "", 100.0 * tot_e / max(1, tot_n)))

    pct = 100.0 * total_m / max(1, total_c)
    print("\n%d/%d bars identical (%.3f%%) across %d shared session(s)" % (total_m, total_c, pct, len(both)))
    if failed == 0 and pct == 100.0:
        print("EQUIVALENCE GATE: PASS — the harness reproduces NinjaTrader's bars exactly.")
        return 0
    print("EQUIVALENCE GATE: FAIL — %d of %d sessions disagree." % (failed, len(both)))
    print("  Diverging at bar 1 of a session => session boundary or size (moves every bar).")
    print("  Diverging mid-session => one print signed differently (localised).")
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
