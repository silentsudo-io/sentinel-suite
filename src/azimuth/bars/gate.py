"""The §2 parity gate for any registered bar type. One driver, every port.

    C:\\ntbv\\Scripts\\python.exe -m bars.gate --bartype renko --instrument "GC 02-26" \\
        --session 2025-12-09 --param brick_ticks=1 --param tick_size=0.1

Exit codes are `gates`': 0 PASS, 1 FAIL, 2 ABORT. An ABORT is not a soft pass -- a
missing reference side, a session the chart never loaded, and a tape without its
sidecar all land here on purpose.

WHY THE PYTHON SIDE IS BUILT OVER EVERY SESSION AND THEN SLICED
--------------------------------------------------------------
NinjaTrader's chart holds several trading days at once, and stock Renko's session
handling REACHES BACKWARDS: on a new trading day it removes the previous session's
last bar and re-adds it flattened to a doji. Build one session in isolation and that
bar stays a brick, so the two sides differ on exactly one bar per session for a reason
that has nothing to do with the port. Building the whole tape and slicing reproduces
the chart's own context.
"""
from __future__ import annotations

import argparse
import json
import os
import sys

_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if _ROOT not in sys.path:
    sys.path.insert(0, _ROOT)

from gates import get as gate_spec                       # noqa: E402
from gates.loaders import rows_side, tape_meta           # noqa: E402
from gates.parity import run_gate                        # noqa: E402

from bars import get as bar_type                         # noqa: E402
from bars import gate_rows as py_gate_rows               # noqa: E402
from bars import ntdump, tapeio                          # noqa: E402

#: 0.3.0 -- the NT side's session is selected by the shared ET session window instead of
#: the tape's own first/last row stamp. That changes WHICH bars a verdict covers, so a
#: verdict recorded under 0.2.0 is not comparable to one recorded now.
IMPL_VER = "bars.gate/0.3.0"

#: What to select in NinjaTrader's Data Series dialog, per registered bar type.
#: This used to say "Renko" for every port, which would have sent someone to build
#: the wrong reference and then read the resulting mismatch as a port defect.
_NT_BAR_TYPE_LABEL = {
    "renko": "Renko",
    "tbars": "SentinelTBars v1.0.0",
    "flux":  "SentinelFlux v1.0.0",
}

#: Bar types that CLASSIFY TRADES AGAINST THE BOOK and therefore cannot be rebuilt
#: from a historical chart on this machine. Measured 2026-08-05: `db\tick` holds
#: *.Last.ncd ONLY -- zero .Bid.ncd / .Ask.ncd anywhere. SentinelFlux's SignTrade
#: guards on `ask > 0 && bid > 0`, so a quote-less rebuild degrades SILENTLY to the
#: tick rule: 3,261 vs 3,171 bars on 2025-12-09, sharing only 507 boundaries (15.6%).
#: The gate would fail on a DATA defect and read as a PORT defect.
_NEEDS_QUOTES = {
    "flux": True,
}


def _flux_bar_params(header: dict):
    from bars import flux as _flux
    return _flux.bar_params_from_dump_header(header)


def _tbars_bar_params(header: dict):
    """Canonical `bar_params` for SentinelTBars from a `bars.1` dump header.

    VALIDATES rather than assumes. `6/24` is not two knobs -- it is Speed Settings
    `baseValue`, from which `Configure` derives `periodValue = base/2` and
    `periodValue2 = base*2`. A header where that identity does not hold came from a
    chart configured differently than the name suggests, and catching it here as a
    PRECONDITION is far better than surfacing it as thousands of differing bars.
    """
    from bars import tbars as _tbars
    ptype = int(header.get("periodType", -1))
    if ptype != _tbars.BARS_PERIOD_TYPE:
        raise ntdump.DumpError(
            "dump header periodType=%d is not SentinelTBars (%d); this dump came from a "
            "different bar type and gating it against TBars would compare two experiments."
            % (ptype, _tbars.BARS_PERIOD_TYPE))
    base = int(header.get("baseValue", 0))
    pv, pv2 = int(header.get("periodValue", -1)), int(header.get("periodValue2", -1))
    if base <= 0 or pv != base // 2 or pv2 != base * 2:
        raise ntdump.DumpError(
            "dump header baseValue=%d does not derive periodValue=%d / periodValue2=%d "
            "(expected %d / %d). The chart's Speed Settings and the bartag disagree."
            % (base, pv, pv2, base // 2, base * 2))
    return _tbars.series_params_str(speed=base, tick_size=float(header["tickSize"]))


#: Per-bartype reader for the dump header's bar parameters. `ntdump.bar_params_of` is
#: Renko-only by construction (it raises unless periodType == 11), so every Sentinel
#: port supplies its own or the first real run aborts on a header it understands fine.
_BAR_PARAMS_READER = {
    "flux": _flux_bar_params,
    "tbars": _tbars_bar_params,
}


def _parse_params(pairs: list[str]) -> dict:
    out: dict = {}
    for p in pairs:
        if "=" not in p:
            raise SystemExit("--param wants name=value, got %r" % p)
        k, v = p.split("=", 1)
        try:
            out[k] = int(v)
        except ValueError:
            try:
                out[k] = float(v)
            except ValueError:
                out[k] = {"true": True, "false": False}.get(v.lower(), v)
    return out


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(prog="bars.gate", description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--bartype", required=True, help="registered name, e.g. renko")
    ap.add_argument("--instrument", required=True, help="tape folder, e.g. 'GC 02-26'")
    ap.add_argument("--session", required=True, help="session_date to compare, e.g. 2025-12-09")
    ap.add_argument("--param", action="append", default=[], metavar="NAME=VALUE",
                    help="a build parameter; repeatable")
    ap.add_argument("--nt-dump", default=None,
                    help="SentinelBarDump jsonl (default: newest match in Sentinel\\Harness\\bars)")
    ap.add_argument("--master-instrument", default=None,
                    help="name as NinjaTrader reports it (default: first word of --instrument)")
    ap.add_argument("--tape-root", default=tapeio.TAPE_ROOT)
    ap.add_argument("--json", default=None, help="write the verdict here")
    ap.add_argument("--one-session", action="store_true",
                    help="build the Python side from this session ALONE (see module doc: "
                         "this reproduces a chart that loaded exactly one day, and nothing else)")
    a = ap.parse_args(argv)

    params = _parse_params(a.param)
    bt = bar_type(a.bartype)
    master = a.master_instrument or a.instrument.split()[0]

    # ---- Python side -------------------------------------------------------
    if a.one_session:
        paths = [tapeio.session_path(a.instrument, a.session, a.tape_root)]
    else:
        paths = tapeio.discover(a.instrument, a.tape_root)
    loaded = tapeio.load_sessions(paths)
    if a.session not in loaded.session_dates:
        raise SystemExit("session %s is not in the tape for %s (have %s)"
                         % (a.session, a.instrument, ", ".join(loaded.session_dates)))
    sid = loaded.session_dates.index(a.session)

    series = bt.build(loaded.tape, instrument=master, **params)
    one = series.select_session(sid)
    py_rows = py_gate_rows(one, session_date=a.session, closed_only=True)

    sidecar = os.path.join(a.tape_root, a.instrument, a.session + ".meta.json")
    ident = tape_meta(sidecar)
    bar_params = bt.params_str(**params)
    py_meta = {
        "tape_sha256": ident["tape_sha256"],
        "instrument": master,
        "session": a.session,
        "bar_params": bar_params,
        "impl": "Azimuth/bars." + a.bartype,
        "impl_ver": IMPL_VER,
    }

    # ---- NinjaTrader side --------------------------------------------------
    dump_path = a.nt_dump
    if dump_path is None:
        want = bt.bartag(**params) if bt.bartag else None
        try:
            found = ntdump.find_dumps(master, want)
        except ntdump.DumpError as exc:
            return _abort(str(exc), a, py_rows, loaded)
        if not found:
            return _abort(
                "no SentinelBarDump file for instrument %r bartag %r in %s"
                % (master, want, ntdump.DUMP_DIR), a, py_rows, loaded)
        dump_path = found[-1]

    try:
        header, rows = ntdump.read_dump(dump_path)
        # `ntdump.bar_params_of` hard-raises unless periodType == 11 (stock Renko), so it
        # cannot read a Sentinel bar type's header and would abort the first real Flux run
        # for a reason that has nothing to do with parity. Each port that needs a different
        # reader declares one; Renko keeps the default.
        nt_bar_params = _BAR_PARAMS_READER.get(a.bartype, ntdump.bar_params_of)(header)
        # The SESSION WINDOW, not the tape's first/last row. `build_tape.py` filtered the
        # tape's rows to exactly this window and NinjaTrader's trading-hours template opens
        # the same session, so it is the one boundary both sides own -- see
        # `ntdump.gate_rows` for the measurement that moved the gate off the row stamps.
        win = ident.get("session_window_utc_ms")
        if not (isinstance(win, (list, tuple)) and len(win) == 2):
            raise ntdump.DumpError(
                "tape sidecar %s carries no `session_window_utc_ms`. The gate selects the "
                "dump's session by the ET session window both sides share; without it there "
                "is nothing to select on but the tape's own first and last row stamp, and "
                "that is the fragile anchor this was changed away from. Rebuild the tape "
                "with tape\\build_tape.py." % sidecar)
        nt_rows, counters = ntdump.gate_rows(
            header, rows, session_date=a.session,
            win_start_ms=int(win[0]), win_end_ms=int(win[1]),
            first_ts_ms=int(ident["first_ts_ms"]),
            bar_params=nt_bar_params, bartype=a.bartype)
    except (ntdump.DumpError, OSError) as exc:
        # OSError too: an explicit --nt-dump that is not there is the SAME situation as
        # no dump at all, and it must reach the same honest ABORT rather than a traceback.
        return _abort("%s: %s" % (dump_path, exc), a, py_rows, loaded)

    nt_meta = {
        "tape_sha256": ident["tape_sha256"],
        "instrument": str(header.get("inst", "")),
        "session": a.session,
        "bar_params": nt_bar_params,
        "impl": "NinjaTrader/" + str(header.get("barLabel", "")),
        "impl_ver": "SentinelBarDump %s / core %s" % (header.get("dumpVer"), header.get("coreVer")),
    }

    print("tape          %s  sessions=%d  crossed_quotes=%d (SPEC 3.2, not repaired)"
          % (a.instrument, len(loaded.session_dates), loaded.n_crossed))
    print("python side   %d bars (%d row-less, %d dropped as still forming)"
          % (len(py_rows), one.n_empty, one.n - len(py_rows)))
    print("nt side       %s" % dump_path)
    print("              " + "  ".join("%s=%s" % kv for kv in sorted(counters.items())))

    spec = gate_spec("bartype")
    ref = rows_side("NT", nt_rows, meta=nt_meta, origin=dump_path)
    cmp = rows_side("Azimuth", py_rows, meta=py_meta, origin="bars." + a.bartype)
    v = run_gate(spec, ref, cmp)
    print(v.to_text())
    if a.json:
        with open(a.json, "w", encoding="utf-8") as fh:
            json.dump(v.to_dict() if hasattr(v, "to_dict") else {"exit_code": v.exit_code},
                      fh, indent=2)
    return v.exit_code


def _abort(reason: str, a, py_rows, loaded) -> int:
    print("ABORT (2) -- the reference side does not exist.")
    print("  %s" % reason)
    print("")
    print("  The Azimuth side RAN: %d closed bars for %s %s over a tape of %d session(s)."
          % (len(py_rows), a.instrument, a.session, len(loaded.session_dates)))
    print("  It is NOT verified. An unrun gate is not a passing gate.")
    print("")
    print("  To produce the reference side (manual, inside NinjaTrader):")
    print("    1. Open a chart on %s." % a.instrument)
    print("    2. Data series: bar type %s, params %s, Break at EOD ON,"
          % (_NT_BAR_TYPE_LABEL.get(a.bartype, a.bartype), a.param or "(defaults)"))
    print("       Days to load >= the tape span so the session is fully built.")
    print("    3. Add indicator 'Sentinel Bar Dump v1.0.0' to that chart.")
    print("    4. It writes %s\\<stamp>__<inst>__<bartag>.jsonl on the historical" % ntdump.DUMP_DIR)
    print("       rebuild alone.")
    if _NEEDS_QUOTES.get(a.bartype):
        print("")
        print("    !! %s CLASSIFIES TRADES AGAINST THE BOOK, so it needs BID/ASK."
              % _NT_BAR_TYPE_LABEL.get(a.bartype, a.bartype))
        print("       db\\tick holds *.Last.ncd ONLY -- zero .Bid/.Ask anywhere. A historical")
        print("       rebuild therefore degrades silently to the tick rule and the gate would")
        print("       FAIL on a data defect while reading as a port defect.")
        print("       => Use MARKET REPLAY for this bar type, not a historical chart.")
    print("    5. Re-run this command.")
    return 2


if __name__ == "__main__":
    raise SystemExit(main())
