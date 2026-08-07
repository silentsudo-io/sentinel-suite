"""The REFERENCE column: reading NinjaTrader's own bars out of `SentinelBarDump`.

`bin\\Custom\\Indicators\\SentinelBarDump_v1_0_0.cs` (schema `bars.1`) is already
installed and already compiled. Loaded on a chart it writes every COMPLETED bar --
time, OHLC, volume -- to `Sentinel\\Harness\\bars\\<stamp>__<inst>__<bartag>.jsonl`,
historical rebuild included, unthrottled. That file is the answer key §2 requires, and
it is the only way found to get NinjaTrader's bars out of NinjaTrader: the bridge has
no bar-series command (`chartseries` only MUTATES a chart's series, `histdump` exports
depth, `histget` fetches `.nrd`), and bars are derived on the fly, never stored.

WHAT THIS MODULE HAS TO RECONCILE
---------------------------------
* `i` is the chart-global `CurrentBar`, but §2 pairs on `(session, bar_index)`. Both
  sides renumber from each session's first bar, found via the dump's own
  `newSession` flag (`Bars.IsFirstBarOfSession`).
* `t` is ISO-8601 UTC at 100 ns resolution; the tape is integer ms. Both sides FLOOR
  (`build_tape.py` does `ticks // 1_000_000`), so the conversion is lossless in the
  sense that matters -- but a rounding disagreement here would look like a bar-boundary
  disagreement, which is why it is done in one place and said out loud.
* The dumper is `Calculate.OnBarClose`, so the forming bar is never written. The Python
  side drops its trailing bar to match (`gate_rows(closed_only=True)`).
* A repeated `i` means NinjaTrader rebuilt a bar (`RemoveLastBar` -- which is exactly
  what Renko does on every brick). LAST WINS, and the count is surfaced, never hidden:
  a dump with unexpected rebuilds is telling you something about the bar type.
"""
from __future__ import annotations

import glob
import json
import os

SENTINEL_DIR = os.path.join(os.path.expanduser("~"), "Documents", "NinjaTrader 8", "Sentinel")
DUMP_DIR = os.path.join(SENTINEL_DIR, "Harness", "bars")

_EPOCH_DAYS = 719468  # days from 0000-03-01 to 1970-01-01, for the civil-date algorithm


class DumpError(Exception):
    """The NT dump cannot serve as a reference side."""


def iso_to_ms(s: str) -> int:
    """`2026-07-20T22:05:30.7480000Z` -> unix ms UTC, FLOORED.

    Written out rather than delegated: `datetime.fromisoformat` refuses 7-digit
    fractions on this interpreter, and a silent parse fallback in a gate loader is
    how a whole column ends up off by a second with nothing saying so.
    """
    if not s or s[-1] != "Z":
        raise DumpError("bar time %r is not UTC ISO-8601 ending in Z" % s)
    body = s[:-1]
    if "." in body:
        stamp, frac = body.split(".", 1)
    else:
        stamp, frac = body, ""
    date_s, time_s = stamp.split("T")
    y, mo, d = (int(x) for x in date_s.split("-"))
    hh, mm, ss = (int(x) for x in time_s.split(":"))
    # days from civil date (Howard Hinnant's algorithm), no tz database involved
    yy = y - (1 if mo <= 2 else 0)
    era = (yy if yy >= 0 else yy - 399) // 400
    yoe = yy - era * 400
    doy = (153 * (mo + (-3 if mo > 2 else 9)) + 2) // 5 + d - 1
    doe = yoe * 365 + yoe // 4 - yoe // 100 + doy
    days = era * 146097 + doe - _EPOCH_DAYS
    ms = (days * 86400 + hh * 3600 + mm * 60 + ss) * 1000
    if frac:
        ms += int((frac + "000")[:3])
    return ms


def read_dump(path: str) -> tuple[dict, list[dict]]:
    """Return (header, rows). Rows keep the dump's own field names."""
    header = None
    rows: list[dict] = []
    with open(path, "r", encoding="utf-8") as fh:
        for lineno, line in enumerate(fh, 1):
            line = line.strip()
            if not line:
                continue
            try:
                obj = json.loads(line)
            except ValueError as exc:
                raise DumpError("%s:%d is not JSON (%s). A gate loader that skips "
                                "unparseable lines is inventing its own reference."
                                % (path, lineno, exc)) from exc
            if obj.get("hdr"):
                if header is not None:
                    raise DumpError("%s carries two header lines; two runs were appended "
                                    "to one file and their bar indices are not comparable" % path)
                header = obj
            else:
                rows.append(obj)
    if header is None:
        raise DumpError("%s has no `hdr` line -- schema bars.1 requires one and the tick "
                        "size cannot be guessed" % path)
    if header.get("schema") != "bars.1":
        raise DumpError("%s: schema %r, expected bars.1" % (path, header.get("schema")))
    if not rows:
        raise DumpError("%s has a header and no bars. An empty side is ABORT, not PASS." % path)
    return header, rows


def find_dumps(instrument: str | None = None, bartag: str | None = None,
               root: str = DUMP_DIR) -> list[str]:
    """Dump files matching `<stamp>__<inst>__<bartag>.jsonl`, newest last."""
    if not os.path.isdir(root):
        raise DumpError(
            "no NinjaTrader bar dumps at %s. The reference side of a bartype gate does not "
            "exist until SentinelBarDump has been loaded on a chart of the right instrument "
            "and bar type -- see bars/README.md." % root)
    pat = "*__%s__%s.jsonl" % (instrument or "*", bartag or "*")
    return sorted(glob.glob(os.path.join(root, pat)))


def bar_params_of(header: dict) -> str:
    """The canonical `bar_params` string from a dump header.

    Must equal what the port's own `params_str` produces or the gate ABORTs on
    identity. That is the intended behaviour: two different parameterisations are two
    different experiments, and finding that out at PRECONDITION beats finding it out
    as 8,000 differing bars.
    """
    ptype = int(header.get("periodType", -1))
    if ptype != 11:
        raise DumpError("dump header periodType=%d is not Renko (11); this loader's "
                        "bar_params mapping is bar-type specific" % ptype)
    return "renko brick=%d tick=%s" % (int(header["periodValue"]), _fmt(header["tickSize"]))


def _fmt(x) -> str:
    s = ("%.10f" % float(x)).rstrip("0").rstrip(".")
    return s or "0"


#: How far BEFORE the tape's first row the dump's session-anchor bar may close and still
#: be accepted as the same bar. See `gate_rows` for the measurement that fixes it.
ANCHOR_LEAD_MS = 2000


def gate_rows(header: dict, rows: list[dict], *, session_date: str,
              win_start_ms: int, win_end_ms: int, first_ts_ms: int,
              bar_params: str, bartype: str,
              anchor_lead_ms: int = ANCHOR_LEAD_MS) -> tuple[list[dict], dict]:
    """`bartype` artefact rows for ONE session, plus the counters that matter.

    The session is selected by TIME, not by counting blocks: the dump may hold several
    trading days and the tape file is one. A bar belongs to this session if its close
    stamp lands in the half-open SESSION WINDOW `[win_start_ms, win_end_ms)`, and the
    `newSession` flag inside that window fixes `bar_index = 0`.

    ⭐ THE WINDOW IS THE SESSION'S DEFINITION, NOT THE TAPE'S FIRST AND LAST ROW.
    This used to select on `[first_ts_ms, last_ts_ms]` -- the stamps of the tape's own
    first and last row -- and that made a whole session's gate depend on whether NT's
    first brick happened to close after our first tick. Measured 2026-08-05 over the
    21 session starts in `20260805T015237__GC__11v1x1.jsonl` against the 17 `GC 02-26`
    tape sidecars: the dump's anchor bar closes anywhere from 121 ms BEFORE the tape's
    first row to 472 ms after it, and 2025-12-09 / 12-11 / 12-22 / 12-12 / 12-26 /
    12-29 / 12-30 / 12-31 landed on the wrong side and ABORTed. The anchor is right in
    every one of those cases -- its OPEN equals the tape's first trade price exactly,
    and its volume equals that trade's -- only its close STAMP disagrees, and it is the
    ONLY bar of 94,108 in a session whose stamp does.

    Both sides already agree on what a session IS: `build_tape.py` filters rows to
    `[D-1 18:00 ET, D 17:00 ET)` and NinjaTrader's "Nymex Metals - Energy ETH" template
    opens the same session (all 21 dump anchors close at 18:00:00 ET + under a second).
    `first_ts_ms` was only ever a proxy for that boundary, and it is a proxy made of
    data -- the arrival time of one tick. Selecting on the definition instead is not a
    widening; it is the two sides using the SAME window rather than one of them using a
    sample from inside it.

    ⛔ `first_ts_ms` is still required, as an explicit BOUND rather than a filter: the
    anchor may not close more than `anchor_lead_ms` before the tape's first row. N =
    2000 ms is 16x the largest lead measured (-121 ms on 2025-12-30) and three orders of
    magnitude under the tape builder's 60 s data-gap threshold, so it is loose enough
    that feed-latency skew between two recordings of one session open can never trip it
    and tight enough that it cannot reach a different bar: GC's session-open burst puts
    the next brick 200-600 ms later. A LATE anchor is not bounded, and must not be --
    the anchor bar stays open until its first brick completes, which on a quiet open is
    legitimately minutes.
    """
    if win_end_ms <= win_start_ms:
        raise DumpError("session window [%d, %d) for %s is empty or inverted"
                        % (win_start_ms, win_end_ms, session_date))

    seen: dict[int, dict] = {}
    dupes = 0
    for r in rows:
        i = int(r["i"])
        if i in seen:
            dupes += 1
        seen[i] = r
    ordered = [seen[i] for i in sorted(seen)]

    picked = []
    for r in ordered:
        ms = iso_to_ms(r["t"])
        if win_start_ms <= ms < win_end_ms:
            picked.append((ms, r))
    if not picked:
        raise DumpError(
            "no bar in the dump closes inside the %s session window "
            "[%d, %d). The chart did not load this session -- an empty side is ABORT, "
            "not PASS." % (session_date, win_start_ms, win_end_ms))

    starts = [k for k, (_, r) in enumerate(picked) if r.get("newSession")]
    if len(starts) > 1:
        raise DumpError("%d session starts inside one session window (%s); the dump's "
                        "trading-hours template does not agree with the tape's session "
                        "boundaries and pairing on bar_index would be meaningless"
                        % (len(starts), session_date))
    if not starts:
        # No first-of-session bar in the window: the chart's history began mid-session, so
        # bar 0 of the tape has no counterpart. Say so; do not renumber and pretend.
        raise DumpError(
            "the dump contains no first-of-session bar inside the %s window [%d, %d): the "
            "chart's loaded history starts mid-session, so bar_index 0 on the two sides is "
            "not the same bar. Load more days on the chart and re-dump."
            % (session_date, win_start_ms, win_end_ms))
    base = starts[0]

    anchor_ms = picked[base][0]
    lead = int(first_ts_ms) - anchor_ms          # >0 means the anchor closes EARLY
    if lead > anchor_lead_ms:
        raise DumpError(
            "the dump's %s session-anchor bar closes at %d, %d ms BEFORE the tape's first "
            "row (%d) -- more than the %d ms allowed. An anchor that far ahead of any tick "
            "we hold is not the same bar as ours, and pairing on bar_index would compare "
            "two different bricks. Do NOT widen this bound to make a session run."
            % (session_date, anchor_ms, lead, first_ts_ms, anchor_lead_ms))

    out = []
    for k in range(base, len(picked)):
        ms, r = picked[k]
        out.append({
            "session": session_date,
            "bar_index": k - base,
            "instrument": str(header.get("inst", "")),
            "bartype": bartype,
            "bar_params": bar_params,
            "open": float(r["o"]),
            "high": float(r["h"]),
            "low": float(r["l"]),
            "close": float(r["c"]),
            "volume": int(r["v"]),
            "ts_ms": ms,
        })
    counters = {
        "dump_rows": len(rows),
        "rebuilt_bars": dupes,
        "in_window": len(picked),
        "dropped_before_session_start": base,
        "realtime_bars": sum(1 for _, r in picked if r.get("rt")),
        # Always surfaced, never only on failure: this is the number whose sign used to
        # decide silently whether a session ran at all. A bound nobody can read is a
        # bound nobody can audit.
        "anchor_lead_ms": lead,
    }
    return out, counters
