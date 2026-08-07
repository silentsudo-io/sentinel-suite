#!/usr/bin/env python3
"""l2book — reconstruct the REAL order book from the export's L2 depth stream.

WHY THIS EXISTS
---------------
The L1 `Bid`/`Ask` rows are a DEGRADED summary. Measured on GC 08-26 2026-07-02, latching L1 quotes
gives a median spread of 3-5 ticks in EVERY hour -- including the two busiest, where GC is a 1-tick
market -- and 20% of trades print outside the resulting book, a fraction that does NOT improve with
liquidity. That is not a wide market, it is an incomplete feed.

The same file carries **3.1M L2 rows against 894k L1 rows**: a full depth ladder at 1-tick increments
with a position index and size at every level. That is the real book, and it is what every execution
question needs -- you cannot measure the cost of crossing a spread you cannot see, and you cannot model
a passive fill without queue position.

NT semantics being assumed, and VALIDATED rather than trusted (`--validate`):
    kind      0 = Ask ladder, 1 = Bid ladder      (MarketDataType)
    op        0 = Add, 1 = Update, 2 = Remove      (NT Operation enum)
    pos       0 = top of book, ascending into the ladder
If any of that is wrong the ladder goes incoherent within seconds -- crossed books, non-monotonic
levels -- so the coherence report IS the test of the assumption, not a formality.
"""
from __future__ import annotations

import argparse
import os
import sys
from collections import Counter

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from .nrdcsv import iter_l2  # noqa: E402
from .regime_study import CSV_ROOT  # noqa: E402

try:
    sys.stdout.reconfigure(encoding="utf-8")
except (AttributeError, OSError):
    pass

ASK_SIDE, BID_SIDE = 0, 1
ADD, UPDATE, REMOVE = 0, 1, 2


class Book:
    """Two ladders as position-indexed lists. Deliberately literal: NT hands us a position, so we
    honour it rather than re-sorting by price and hiding a disagreement we would want to see."""

    __slots__ = ("asks", "bids", "bad_ops")

    def __init__(self):
        self.asks = []          # [[price, volume], ...] index == NT position, ascending price
        self.bids = []          # index == NT position, DESCENDING price
        self.bad_ops = 0

    def _side(self, kind):
        return self.asks if kind == ASK_SIDE else self.bids

    def apply(self, r):
        lad = self._side(r.kind)
        if r.op == ADD:
            if 0 <= r.pos <= len(lad):
                lad.insert(r.pos, [r.price, r.volume])
            else:                                   # gap: pad rather than silently drop
                while len(lad) < r.pos:
                    lad.append([0.0, 0])
                lad.append([r.price, r.volume])
        elif r.op == UPDATE:
            if 0 <= r.pos < len(lad):
                lad[r.pos] = [r.price, r.volume]
            else:
                while len(lad) < r.pos:
                    lad.append([0.0, 0])
                lad.append([r.price, r.volume])
                self.bad_ops += 1
        elif r.op == REMOVE:
            if 0 <= r.pos < len(lad):
                lad.pop(r.pos)
            else:
                self.bad_ops += 1
        else:
            self.bad_ops += 1

    @property
    def best_bid(self):
        return self.bids[0] if self.bids else None

    @property
    def best_ask(self):
        return self.asks[0] if self.asks else None

    def top(self):
        """(bid_px, bid_sz, ask_px, ask_sz) or None while either side is empty."""
        if not self.bids or not self.asks:
            return None
        b, a = self.bids[0], self.asks[0]
        if b[0] <= 0 or a[0] <= 0:
            return None
        return (b[0], b[1], a[0], a[1])


def replay(path, on_change=None):
    """Stream the day, applying every depth event. Yields (ts_ns, book) after each event."""
    book = Book()
    for r in iter_l2(path):
        book.apply(r)
        if on_change is not None:
            on_change(r.ts_ns, book)
        yield r.ts_ns, book


def validate(path, tick=0.1, max_rows=None):
    """Coherence report. This is what decides whether the assumed NT semantics are right."""
    book = Book()
    n = crossed = locked = ok = empty = 0
    spreads = Counter()
    ask_mono = bid_mono = mono_n = 0
    depth_ask = depth_bid = 0
    for i, r in enumerate(iter_l2(path)):
        if max_rows and i >= max_rows:
            break
        book.apply(r)
        n += 1
        t = book.top()
        if t is None:
            empty += 1
            continue
        b, _bs, a, _as = t
        if b > a:
            crossed += 1
        elif b == a:
            locked += 1
        else:
            ok += 1
            spreads[round((a - b) / tick)] += 1
        if len(book.asks) >= 3 and len(book.bids) >= 3:
            mono_n += 1
            ask_mono += all(book.asks[k][0] < book.asks[k + 1][0] for k in range(2))
            bid_mono += all(book.bids[k][0] > book.bids[k + 1][0] for k in range(2))
        depth_ask += len(book.asks)
        depth_bid += len(book.bids)

    print(f"\n{os.path.basename(path)}   {n:,} depth events")
    print(f"  book state : two-sided {ok+crossed+locked:,}  (one side empty {empty:,})")
    print(f"  CROSSED    : {crossed:,} ({100*crossed/max(1,n):.3f}%)   "
          f"LOCKED: {locked:,} ({100*locked/max(1,n):.3f}%)")
    print(f"  ladder monotonic (top 3): ask {100*ask_mono/max(1,mono_n):.2f}%   "
          f"bid {100*bid_mono/max(1,mono_n):.2f}%")
    print(f"  mean depth levels held : ask {depth_ask/max(1,n):.1f}   bid {depth_bid/max(1,n):.1f}")
    print(f"  malformed ops          : {book.bad_ops:,}")
    tot = sum(spreads.values())
    print(f"\n  TRUE SPREAD (ticks), from the depth ladder:")
    for k in sorted(spreads)[:8]:
        print(f"    {k:>3} tick : {100*spreads[k]/tot:6.2f}%   ({spreads[k]:,})")
    med = None
    run = 0
    for k in sorted(spreads):
        run += spreads[k]
        if med is None and run >= tot / 2:
            med = k
    print(f"    median   : {med} tick(s)")
    healthy = (crossed / max(1, n) < 0.01 and ask_mono / max(1, mono_n) > 0.99
               and bid_mono / max(1, mono_n) > 0.99)
    print(f"\n  => semantics {'CONFIRMED' if healthy else 'WRONG — the assumed op/position codes do not hold'}")
    return healthy, med


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(prog="harness.l2book")
    ap.add_argument("--contract", default="GC 08-26")
    ap.add_argument("--day", default=None, help="yyyymmdd (default: 4th converted day)")
    ap.add_argument("--max-rows", type=int, default=None)
    a = ap.parse_args(argv)
    d = os.path.join(CSV_ROOT, a.contract)
    files = sorted(f for f in os.listdir(d) if f.endswith(".csv"))
    f = (a.day + ".csv") if a.day else files[3]
    validate(os.path.join(d, f), max_rows=a.max_rows)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
