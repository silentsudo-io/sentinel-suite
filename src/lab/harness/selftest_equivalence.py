#!/usr/bin/env python3
"""selftest_equivalence — prove the equivalence gate can FAIL before we trust it to PASS.

A differ that has only ever been run on data it agrees with is not evidence of anything. The
2026-07-24 lesson was exactly this shape: `verify_votes.py` windowed on fireTime, so it silently
skipped the entire replay corpus it was built to audit and would have passed the very bake it
existed to catch. It went green on good data only after that was found. Same discipline here --
build the answer key from the harness itself (so a PASS is guaranteed), then INJECT known faults
one at a time and require the gate to catch each one, at the right bar, for the right reason.

Faults injected:
  1. price   -- one bar's close moved by one tick        -> FAIL, mid-session, "price:"
  2. time    -- one bar's close time moved by 5 ms       -> FAIL, mid-session, "time off by"
  3. missing -- one bar deleted                          -> FAIL (count mismatch and/or shift)
  4. size    -- answer key built at a different quantum  -> FAIL at bar 1 (structural, not local)

Fault 4 is the important one: it is the failure shape that means "everything moved", and the gate
must distinguish it from a single mis-signed print. If 1 and 4 look the same in the report, the
report cannot direct the debugging and is worth little.

Run:  python -m harness.selftest_equivalence [--csv-dir DIR] [--session YYYY-MM-DD]
"""
from __future__ import annotations

import argparse
import io
import json
import os
import sys
from contextlib import redirect_stdout
from datetime import datetime, timedelta, timezone

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from . import equivalence  # noqa: E402
from .tide import TideConfig, run_files  # noqa: E402

DEFAULT_CSV_DIR = r"C:\Users\Administrator\Documents\NinjaTrader 8\db\replay.csv\GC 02-26"


def iso_nt(ns: int) -> str:
    """Format like C# DateTime.ToString("o") -- SEVEN fractional digits, which is precisely the
    format Python's fromisoformat rejects. Emitting it here is deliberate: it keeps the parser
    honest instead of testing against a convenient format NT never writes."""
    dt = datetime.fromtimestamp(ns // 1_000_000_000, tz=timezone.utc)
    frac = ns % 1_000_000_000
    return dt.strftime("%Y-%m-%dT%H:%M:%S") + ".%07d" % (frac // 100) + "Z"


def write_dump(path, bars, tick, size, inst="GC", bartag="212207v25"):
    with open(path, "w", encoding="utf-8") as fh:
        fh.write(json.dumps({
            "hdr": 1, "schema": "bars.1", "dumpVer": "1.0.0", "coreVer": "selftest",
            "inst": inst, "bartype": bartag, "barLabel": "GC · SentinelTide 25",
            "tickSize": tick, "pointValue": 100.0, "periodType": 212207.0,
            "periodValue": size, "periodValue2": 0.0, "baseValue": size,
            "tradingHours": "selftest", "openedUtc": "2026-07-26T00:00:00.0000000Z",
        }) + "\n")
        for i, b in enumerate(bars):
            fh.write(json.dumps({
                "i": i, "t": iso_nt(b["ts"]), "o": b["o"], "h": b["h"], "l": b["l"], "c": b["c"],
                "v": b["v"], "rt": False, "newSession": i == 0,
            }) + "\n")


def run_gate(dump, csv_dir, size, tick):
    buf = io.StringIO()
    with redirect_stdout(buf):
        try:
            rc = equivalence.main(["--dump", dump, "--csv-dir", csv_dir,
                                   "--size", str(size), "--tick", str(tick)])
        except SystemExit as e:
            rc = e.code
    return rc, buf.getvalue()


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(prog="harness.selftest_equivalence")
    ap.add_argument("--csv-dir", default=DEFAULT_CSV_DIR)
    ap.add_argument("--session", default=None, help="session label to test (default: the busiest complete one)")
    ap.add_argument("--size", type=float, default=25)
    ap.add_argument("--tick", type=float, default=0.1)
    ap.add_argument("--keep", action="store_true", help="keep the generated dump files")
    args = ap.parse_args(argv)

    if not os.path.isdir(args.csv_dir):
        print("no CSV dir: %s" % args.csv_dir)
        return 2

    # Build the reference bars from two consecutive day files so at least one session is complete.
    files = [os.path.join(args.csv_dir, n) for n in ("20251216.csv", "20251217.csv")]
    files = [f for f in files if os.path.exists(f)]
    if len(files) < 2:
        print("need 20251216.csv + 20251217.csv in %s" % args.csv_dir)
        return 2

    cfg = TideConfig(args.size, args.tick)
    clock = run_files(files, cfg)
    by_session = {}
    for b in clock.bars:
        by_session.setdefault(b.session, []).append(b)
    session = args.session or max(by_session, key=lambda s: len(by_session[s]))
    ref = by_session[session]
    print("reference: session %s, %d bars (from %d day files)" % (session, len(ref), len(files)))
    if len(ref) < 50:
        print("session too small to test meaningfully")
        return 2

    rows = [{"ts": b.ts_close_ns, "o": b.open, "h": b.high, "l": b.low, "c": b.close, "v": b.volume}
            for b in ref]
    mid = len(rows) // 2
    out_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")
    os.makedirs(out_dir, exist_ok=True)

    cases = []

    def case(name, mutate, expect_pass, expect_div=None, expect_reason=None, size=None):
        data = [dict(r) for r in rows]
        if mutate:
            mutate(data)
        p = os.path.join(out_dir, "_selftest_%s.jsonl" % name)
        write_dump(p, data, args.tick, size or args.size)
        rc, text = run_gate(p, args.csv_dir, size or args.size, args.tick)
        passed = ("EQUIVALENCE GATE: PASS" in text)
        ok = (passed == expect_pass)
        detail = ""
        if not expect_pass:
            line = next((l for l in text.splitlines() if l.startswith(session)), "")
            cols = line.split()
            div = cols[4] if len(cols) > 4 else "?"
            if expect_div is not None and div != str(expect_div):
                ok = False
                detail = "expected first div %s, got %s" % (expect_div, div)
            if expect_reason and expect_reason not in line:
                ok = False
                detail = (detail + "; " if detail else "") + "expected reason %r" % expect_reason
            if not detail:
                detail = "first div %s" % div
        cases.append((name, ok, "PASS" if passed else "FAIL", detail))
        if not args.keep:
            try:
                os.remove(p)
            except OSError:
                pass

    # 0 -- control. The answer key IS the harness output, so anything but PASS means the gate is
    # broken independently of NinjaTrader, and no result from it would mean anything.
    case("control", None, True)

    # 1 -- one price, one tick, mid-session.
    def bad_price(d):
        d[mid]["c"] = round(d[mid]["c"] + args.tick, 6)
    case("price", bad_price, False, expect_div=mid, expect_reason="price:")

    # 2 -- one timestamp, 5 ms, mid-session.
    def bad_time(d):
        d[mid]["ts"] += 5_000_000
    case("time", bad_time, False, expect_div=mid, expect_reason="time off by")

    # 3 -- a bar vanishes. Everything after it shifts, so the FIRST divergence must be at `mid`.
    def drop_bar(d):
        del d[mid]
    case("missing", drop_bar, False, expect_div=mid)

    # 4 -- structural: the key was built at a different quantum. Must diverge at bar 1, not mid.
    case("wrong-size", None, False, expect_div=0, size=args.size * 2)

    print("\n%-12s %-8s %-6s %s" % ("case", "verdict", "ok", "detail"))
    for name, ok, verdict, detail in cases:
        print("%-12s %-8s %-6s %s" % (name, verdict, "yes" if ok else "NO", detail))

    bad = [c for c in cases if not c[1]]
    if bad:
        print("\nSELFTEST FAILED on %d case(s): %s" % (len(bad), ", ".join(c[0] for c in bad)))
        print("The equivalence gate cannot be trusted until these pass.")
        return 1
    print("\nSELFTEST PASSED — the gate detects price, time, missing-bar and structural faults,")
    print("and separates a local fault (mid-session) from a structural one (bar 1).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
