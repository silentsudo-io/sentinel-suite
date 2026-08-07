#!/usr/bin/env python3
"""nrdcsv — reader for the CSV that `gbNRDtoCSV` exports out of NinjaTrader's `.nrd` tick store.

This is layer 1 of the offline harness: it turns NT's replay data into a plain Python tick stream
so bar types / sensors / fusion can run with no GUI, no render thread, no connection, and across
all cores. `gbNRDtoCSV` (MIT, (c) 2021 Yevgeny Iliyn) is vendored at `bin\\Custom\\AddOns\\gbNRDtoCSV.cs`
and runs INSIDE NT via Tools -> NRD to CSV, so NT reads its own proprietary file once and
everything downstream is free of NT forever. Do not write a `.nrd` parser.

FORMAT -- MEASURED on GC 02-26 (2025-12-08 .. 2026-01-02), not taken from documentation
---------------------------------------------------------------------------------------
Semicolon-delimited, NO header row, decimal separator `.` (VERIFY per export -- it follows the
machine's regional setting). L1 and L2 rows are INTERLEAVED in the same file and are told apart by
a leading tag, which earlier notes here omitted:

    L1;<mdType>;<yyyyMMddHHmmss>;<subsec>;<price>;<volume>
    L2;<mdType>;<yyyyMMddHHmmss>;<subsec>;<op>;<pos>;<marketMaker>;<price>;<volume>

  mdType   NT's MarketDataType enum: 0 Ask · 1 Bid · 2 Last · 3 DailyHigh · 4 DailyLow ·
           5 DailyVolume · 6 LastClose · 7 Opening · 8 OpenInterest.
           0/1/2 are the three that matter: Last is the trade, Bid/Ask are what quote-rule
           signing needs. Without them a flow-clocked bar (Flux/Drift/Tide/CVD) is unreproducible.
  subsec   sub-second remainder in 100-ns ticks (0 .. 9_999_999), NOT microseconds.
  op/pos   L2 only: Operation (0 Add, 1 Update, 2 Remove) and book Position.

TIMEZONE -- the trap, and how it was established
------------------------------------------------
Timestamps are in NT's LOCAL display timezone (America/Chicago here), NOT UTC. The corpus JSONL is
UTC, so every consumer must convert or it will silently mis-join by six hours.

Proven by measurement rather than assumption, three independent ways:
  * the CME maintenance break (16:00-17:00 CT) shows up as an hour with EXACTLY ZERO trades at
    file-hour 16 -- hour 22 (the UTC candidate) and hour 17 (the ET candidate) are both busy;
  * every Friday file stops at exactly 16:00:00 -- the GC weekly close, 16:00 CT;
  * 2025-12-24 stops early (holiday session) and 2025-12-25 starts late.
`census()` below re-runs the zero-trade-hour probe on any file, so this is checkable, not folklore.

  WARNING -- DST fall-back is genuinely lossy. Local 01:00-01:59 on the November transition occurs
  twice and the export keeps no offset, so those rows cannot be placed on the UTC line without
  guessing. We resolve them as the FIRST pass (fold=0) and count them; `census()` reports the count.
  Do not run an equivalence gate across that hour.

DAY PARTITION
-------------
File `D.csv` spans local `[D-1 23:00:00, D 23:00:00)`. So a Sunday file holds only the pre-open
daily-high/low rows; the Sunday session open lands in MONDAY's file. Range a window by content,
never by filename.
"""
from __future__ import annotations

import os
import sys
from collections import namedtuple
from datetime import datetime, timedelta, timezone

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from lab_faults import swallow  # noqa: E402  (the Lab's SentinelCore.Swallow -- never a silent except)

try:
    from zoneinfo import ZoneInfo
except ImportError:  # pragma: no cover - Python < 3.9
    ZoneInfo = None

__all__ = [
    "ASK", "BID", "LAST", "DAILY_HIGH", "DAILY_LOW", "DAILY_VOLUME",
    "LAST_CLOSE", "OPENING", "OPEN_INTEREST", "MD_NAMES",
    "Tick", "iter_l1", "iter_l2", "census", "NT_TIMEZONE",
]

# --- NT's MarketDataType enum -------------------------------------------------------------------
ASK, BID, LAST = 0, 1, 2
DAILY_HIGH, DAILY_LOW, DAILY_VOLUME = 3, 4, 5
LAST_CLOSE, OPENING, OPEN_INTEREST = 6, 7, 8
MD_NAMES = {
    ASK: "Ask", BID: "Bid", LAST: "Last", DAILY_HIGH: "DailyHigh", DAILY_LOW: "DailyLow",
    DAILY_VOLUME: "DailyVolume", LAST_CLOSE: "LastClose", OPENING: "Opening",
    OPEN_INTEREST: "OpenInterest",
}
QUOTE_TYPES = (ASK, BID, LAST)

# The timezone the export is written in. Override with SENTINEL_NT_TZ if a bake node is set
# to something else -- and re-run `census --probe-tz` there before trusting it.
NT_TIMEZONE = os.environ.get("SENTINEL_NT_TZ", "America/Chicago")

_TICKS_PER_SECOND = 10_000_000          # NT sub-second field is in 100-ns ticks
_NS_PER_TICK = 100
_EPOCH = datetime(1970, 1, 1, tzinfo=timezone.utc)

# One tick record. ts_ns is nanoseconds since the UNIX epoch, UTC -- integer on purpose, so bar
# boundaries and equivalence diffs never depend on float rounding.
Tick = namedtuple("Tick", "kind ts_ns price volume")


class _HourClock:
    """Local `yyyyMMddHH` -> epoch-nanoseconds at the top of that hour.

    Converting per row through zoneinfo costs more than the parse itself, and the offset can only
    change on an hour boundary, so resolve once per hour and cache. Also counts rows landing in the
    ambiguous DST fall-back hour instead of hiding them.
    """

    def __init__(self, tzname: str = NT_TIMEZONE):
        self.tzname = tzname
        self.tz = ZoneInfo(tzname) if ZoneInfo is not None else None
        self._cache: dict[str, int] = {}
        self.ambiguous_hours: set[str] = set()
        self.ambiguous_rows = 0

    def base_ns(self, key: str) -> int:
        """key = 'yyyyMMddHH' local."""
        ns = self._cache.get(key)
        if ns is not None:
            if key in self.ambiguous_hours:
                self.ambiguous_rows += 1
            return ns
        naive = datetime(int(key[0:4]), int(key[4:6]), int(key[6:8]), int(key[8:10]))
        if self.tz is None:
            aware = naive.replace(tzinfo=timezone.utc)
        else:
            aware = naive.replace(tzinfo=self.tz, fold=0)
            # An hour whose two folds differ is the repeated fall-back hour: the export carries no
            # offset, so it is unrecoverable. Record it rather than quietly picking one.
            if aware.utcoffset() != naive.replace(tzinfo=self.tz, fold=1).utcoffset():
                self.ambiguous_hours.add(key)
                self.ambiguous_rows += 1
        ns = int((aware - _EPOCH).total_seconds()) * 1_000_000_000
        self._cache[key] = ns
        return ns


def _open(path):
    return open(path, "r", encoding="utf-8", errors="replace", newline="")


def iter_l1(path, types=QUOTE_TYPES, tzname: str = NT_TIMEZONE, comma_decimal=None):
    """Stream L1 rows as `Tick`, oldest first, UTC nanoseconds.

    types           mdTypes to keep. Default (Ask, Bid, Last) = everything signing needs.
                    Pass None for all L1 rows.
    comma_decimal   None auto-detects from the first price seen; force True/False to be explicit.
    """
    clock = _HourClock(tzname)
    want = None if types is None else set(types)
    swap = comma_decimal
    bad = 0
    with _open(path) as fh:
        for line in fh:
            if not line.startswith("L1;"):
                continue
            f = line.rstrip("\r\n").split(";")
            try:
                kind = int(f[1])
                if want is not None and kind not in want:
                    continue
                stamp = f[2]
                px = f[4]
                if swap is None:
                    swap = ("," in px)
                if swap:
                    px = px.replace(",", ".")
                ts_ns = (clock.base_ns(stamp[0:10])
                         + (int(stamp[10:12]) * 60 + int(stamp[12:14])) * 1_000_000_000
                         + int(f[3]) * _NS_PER_TICK)
                yield Tick(kind, ts_ns, float(px), int(f[5]))
            except (ValueError, IndexError) as _swex:
                bad += 1
                swallow("harness.nrdcsv.l1", _swex, os.path.basename(path))
                continue
    if bad or clock.ambiguous_rows:
        swallow("harness.nrdcsv.summary", None,
                "%s: %d unparsable L1 rows, %d rows in an ambiguous DST hour"
                % (os.path.basename(path), bad, clock.ambiguous_rows))


L2Row = namedtuple("L2Row", "kind ts_ns op pos maker price volume")


def iter_l2(path, tzname: str = NT_TIMEZONE, comma_decimal=None):
    """Stream L2 (book depth) rows. Raw material for a real order-book model; unused by Tide."""
    clock = _HourClock(tzname)
    swap = comma_decimal
    with _open(path) as fh:
        for line in fh:
            if not line.startswith("L2;"):
                continue
            f = line.rstrip("\r\n").split(";")
            try:
                stamp = f[2]
                px = f[7]
                if swap is None:
                    swap = ("," in px)
                if swap:
                    px = px.replace(",", ".")
                ts_ns = (clock.base_ns(stamp[0:10])
                         + (int(stamp[10:12]) * 60 + int(stamp[12:14])) * 1_000_000_000
                         + int(f[3]) * _NS_PER_TICK)
                yield L2Row(int(f[1]), ts_ns, int(f[4]), int(f[5]), f[6], float(px), int(f[8]))
            except (ValueError, IndexError) as _swex:
                swallow("harness.nrdcsv.l2", _swex, os.path.basename(path))
                continue


def census(path, tzname: str = NT_TIMEZONE) -> dict:
    """Single pass sanity report -- run this on file one of any new export.

    Answers the questions that have to be settled before the data is trusted: is there a header
    row, which decimal separator, are Bid/Ask actually present, and which timezone the stamps are
    in (via the zero-trade maintenance-break hour). Reasoning about any of these has been wrong
    before; this measures them.
    """
    tally: dict[str, int] = {}
    hour_trades = [0] * 24
    first = last = None
    comma = False
    header = None
    lines = 0
    with _open(path) as fh:
        for line in fh:
            lines += 1
            if header is None:
                header = not (line.startswith("L1;") or line.startswith("L2;"))
            f = line.rstrip("\r\n").split(";")
            if len(f) < 5:
                continue
            key = f[0] + ";" + f[1]
            tally[key] = tally.get(key, 0) + 1
            stamp = f[2]
            if first is None:
                first = stamp
            last = stamp
            if f[0] == "L1" and f[1] == str(LAST):
                try:
                    hour_trades[int(stamp[8:10])] += 1
                except (ValueError, IndexError) as _swex:
                    swallow("harness.nrdcsv.census.hour", _swex, os.path.basename(path))
                if not comma and "," in f[4]:
                    comma = True

    clock = _HourClock(tzname)

    def _utc(stamp):
        if not stamp:
            return None
        ns = (clock.base_ns(stamp[0:10])
              + (int(stamp[10:12]) * 60 + int(stamp[12:14])) * 1_000_000_000)
        return datetime.fromtimestamp(ns / 1e9, tz=timezone.utc)

    empty = [h for h in range(24) if hour_trades[h] == 0 and sum(hour_trades)]
    return {
        "path": path, "lines": lines, "header_row": header, "comma_decimal": comma,
        "types": dict(sorted(tally.items())), "first_local": first, "last_local": last,
        "first_utc": _utc(first), "last_utc": _utc(last),
        "trades": sum(hour_trades), "hour_trades": hour_trades,
        "empty_hours": empty, "ambiguous_dst_hours": sorted(clock.ambiguous_hours),
    }


def _main(argv) -> int:
    if not argv:
        print(__doc__)
        print("usage: python -m harness.nrdcsv <file.csv> [--head N]")
        return 0
    path = argv[0]
    if not os.path.exists(path):
        print("no such file: %s" % path)
        return 1

    if "--head" in argv:
        n = int(argv[argv.index("--head") + 1])
        for i, t in enumerate(iter_l1(path)):
            print("%s  %-4s %10.2f  x%d" % (
                datetime.fromtimestamp(t.ts_ns / 1e9, tz=timezone.utc).isoformat(),
                MD_NAMES.get(t.kind, t.kind), t.price, t.volume))
            if i + 1 >= n:
                break
        return 0

    c = census(path)
    print("%s" % c["path"])
    print("  lines            %d" % c["lines"])
    print("  header row       %s" % ("YES -- format changed, fix the reader" if c["header_row"] else "no (expected)"))
    print("  decimal          %s" % ("COMMA -- regional separator, reader will swap" if c["comma_decimal"] else "period (expected)"))
    print("  local span       %s -> %s   (tz %s)" % (c["first_local"], c["last_local"], NT_TIMEZONE))
    print("  utc span         %s -> %s" % (c["first_utc"], c["last_utc"]))
    print("  trades (L1 Last) %d" % c["trades"])
    print("  row types:")
    for k, v in c["types"].items():
        tag, md = k.split(";")
        print("    %-3s %-13s %10d" % (tag, MD_NAMES.get(int(md), md), v))
    print("  timezone probe -- hours with zero trades: %s" % (c["empty_hours"] or "none"))
    if 16 in c["empty_hours"]:
        print("    -> 16 is empty = the 16:00-17:00 CT maintenance break. Stamps are CENTRAL.")
    elif c["empty_hours"]:
        print("    -> 16 is NOT the empty hour. Do not assume Central; establish the tz before using this file.")
    for md in (BID, ASK):
        if c["types"].get("L1;%d" % md, 0) == 0:
            print("  !! no %s rows -- quote-rule signing is impossible on this file" % MD_NAMES[md])
    if c["ambiguous_dst_hours"]:
        print("  !! ambiguous DST fall-back hours present: %s" % c["ambiguous_dst_hours"])
    return 0


if __name__ == "__main__":
    raise SystemExit(_main(sys.argv[1:]))
