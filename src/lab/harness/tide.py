#!/usr/bin/env python3
"""tide — SentinelTide reimplemented in Python, outside NinjaTrader.

Port of `bin\\Custom\\BarsTypes\\SentinelTide_v1_0_0.cs` (BarsPeriodType 212207). Tide is the
harness's first target deliberately: it is the simplest bar type in the suite -- pure arithmetic on
signed ticks, no ATR, no Heikin-Ashi, nothing adaptive -- so if it cannot be reproduced, nothing
downstream can be either. That is the whole point of doing it first.

THE CLOCK (identical to the C#)
-------------------------------
Session cumulative volume delta runs on a fixed lattice `cvdLine(k) = k * deltaPerBrick`. A bar
closes the moment CVD crosses an ADJACENT line, in a loop -- a burst carrying CVD through three
lines prints three bars, so no bar ever holds more than one quantum of flow. That invariant is
what makes bar HEIGHT comparable across bars (height per unit flow = market impact), so it is
enforced structurally here too, not assumed.

Signing is quote rule where a real bid/ask exists, tick rule otherwise, with single prints
winsorized at 4x their EWMA (SentinelFlux learned that the expensive way -- one block trade spiked
its threshold and left the clock dormant for hours). The `isBar` bar-proxy branch of the C# is
NOT ported: the harness always has true ticks, and the proxy path makes bar height a function of
price by construction, which is the exact circularity Tide refuses.

WHAT IS DELIBERATELY NOT IDENTICAL, AND WHY IT MATTERS FOR THE GATE
-------------------------------------------------------------------
  * SESSION BOUNDARY. The C# asks NT's `SessionIterator`, which reads the instrument's trading
    hours template. Here it is an explicit local wall-clock time (default 17:00 America/Chicago =
    the CME Globex open, which the data confirms: the 16:00-17:00 maintenance break is the one
    hour of the day with zero trades). CVD and the lattice index reset at that boundary, so a
    session-boundary disagreement moves EVERY bar in the session. If the gate fails, check this
    first -- it is the most likely single cause.
  * ROUNDING. The C# rounds bar prices to the tick grid on write. Ticks are already on the grid,
    so this is a no-op; heights and bodies are computed from raw prices in both.
  * LOGGING. The C# throttles its bar log to one line per 10 wall-seconds (~8% of bars, and not a
    random 8%). The harness emits EVERY bar. Any ratio taken from NT's log measures the sampler as
    much as the tape -- so the census here is the trustworthy one.

BACKSTOP BARS ARE NOT DATA
--------------------------
A bar closed by the time or tick backstop carries less than a full flow quantum, so its height is
NOT a valid impact reading. They are marked `reason != "flow"` and MUST be excluded before
grading. The 2026-07-26 bring-up found a default size ~10x too large where EVERY bar closed on the
time backstop and the chart still looked plausible -- the tell was the dates, never the chart.
Run `--census` before judging anything by eye.
"""
from __future__ import annotations

import json
import math
import os
import sys
from collections import namedtuple
from datetime import datetime, timedelta, timezone

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from lab_faults import swallow  # noqa: E402

from .nrdcsv import ASK, BID, LAST, NT_TIMEZONE, iter_l1  # noqa: E402

try:
    from zoneinfo import ZoneInfo
except ImportError:  # pragma: no cover
    ZoneInfo = None

__all__ = ["TideConfig", "TideBar", "TideClock", "run_files"]

# Defaults mirror the C# constants.
TIME_BACKSTOP_MINUTES = 30.0
TICK_BACKSTOP = 20000
WINSOR_MULT = 4.0
EWMA_ALPHA = 0.02

TideBar = namedtuple(
    "TideBar",
    "session ordinal open high low close ts_open_ns ts_close_ns "
    "dcvd flow_dir body_t height_t impact reason nticks volume",
)


class TideConfig:
    """One knob (delta_per_brick) plus the instrument's tick size, as in the C#.

    session_open_local is the harness's stand-in for NT's SessionIterator; see the module note.
    """

    def __init__(self, delta_per_brick=25, tick_size=0.1, tz=NT_TIMEZONE,
                 session_open_local=(17, 0), time_backstop_minutes=TIME_BACKSTOP_MINUTES,
                 tick_backstop=TICK_BACKSTOP, winsor_mult=WINSOR_MULT):
        self.delta_per_brick = max(1.0, float(delta_per_brick))
        self.tick_size = float(tick_size)
        self.tz = tz
        self.session_open_local = session_open_local
        self.time_backstop_ns = int(time_backstop_minutes * 60 * 1_000_000_000)
        self.tick_backstop = int(tick_backstop)
        self.winsor_mult = float(winsor_mult)


class TideClock:
    """Feed it Last ticks with the prevailing bid/ask; it emits TideBar objects.

    Usage:
        clock = TideClock(TideConfig(delta_per_brick=25, tick_size=0.1))
        clock.on_tick(ts_ns, price, volume, bid, ask)
        clock.bars
    """

    def __init__(self, cfg: TideConfig, on_bar=None):
        self.cfg = cfg
        self.bars: list = []
        self._on_bar = on_bar
        self._tz = ZoneInfo(cfg.tz) if ZoneInfo is not None else timezone.utc

        self._session = None            # local date string of the session open
        self._next_session_ns = None
        self._cvd = 0.0
        self._bar_cvd_open = 0.0
        self._level = 0
        self._vol_ewma = 0.0
        self._last_price = 0.0
        self._last_tick_sign = 0
        self._prev_bid = 0.0
        self._prev_ask = 0.0

        self._open = self._high = self._low = 0.0
        self._nticks = 0
        self._vol = 0
        self._birth_ns = 0
        self._ordinal = 0
        self.quote_signed = 0           # G1: prints signed by the quote rule
        self.tick_signed = 0            # ... and by the tick-rule fallback
        self.ambiguous = 0              # inside the spread -- counted as neither, by design
        self.max_print = 0.0            # largest WINSORIZED print applied -- the G3 tolerance

    # --- session -------------------------------------------------------------------------------
    def _session_bounds(self, ts_ns: int):
        """(session label, ns of the NEXT session open strictly after ts_ns)."""
        local = datetime.fromtimestamp(ts_ns / 1e9, tz=timezone.utc).astimezone(self._tz)
        hh, mm = self.cfg.session_open_local
        today_open = local.replace(hour=hh, minute=mm, second=0, microsecond=0)
        if local >= today_open:
            start, nxt = today_open, today_open + timedelta(days=1)
        else:
            start, nxt = today_open - timedelta(days=1), today_open
        return start.strftime("%Y-%m-%d"), int(nxt.timestamp()) * 1_000_000_000

    def _reset_session(self, ts_ns: int, price: float):
        self._session, self._next_session_ns = self._session_bounds(ts_ns)
        self._cvd = 0.0
        self._bar_cvd_open = 0.0
        self._level = 0
        self._open = self._high = self._low = price
        self._nticks = 0
        self._vol = 0
        self._birth_ns = ts_ns
        self._ordinal = 1               # the C# AddBar's session-opening bar
        self._last_price = price
        self._last_tick_sign = 0

    # --- flow ----------------------------------------------------------------------------------
    def _accumulate(self, price: float, volume: int, bid: float, ask: float):
        if volume <= 0:
            return
        if bid > 0:
            self._prev_bid = bid
        if ask > 0:
            self._prev_ask = ask

        vol = float(volume)
        # EWMA is fed the RAW print, then the print is capped -- same order as the C#, and it
        # matters: capping first would let the cap chase a block trade upward.
        self._vol_ewma = vol if self._vol_ewma <= 0 else self._vol_ewma + EWMA_ALPHA * (vol - self._vol_ewma)
        cap = self._vol_ewma * self.cfg.winsor_mult
        if cap > 0 and vol > cap:
            vol = cap

        if self._prev_ask > 0 and self._prev_bid > 0 and self._prev_ask > self._prev_bid:
            if price >= self._prev_ask:
                sign = 1
            elif price <= self._prev_bid:
                sign = -1
            else:
                sign = 0                # inside the spread: genuinely ambiguous, count neither
            if sign:
                self.quote_signed += 1
            else:
                self.ambiguous += 1
        else:
            if self._last_price > 0 and price > self._last_price:
                sign = 1
            elif self._last_price > 0 and price < self._last_price:
                sign = -1
            else:
                sign = self._last_tick_sign
            if sign:
                self.tick_signed += 1

        if sign != 0:
            self._last_tick_sign = sign
            self._cvd += sign * vol
            if vol > self.max_print:
                self.max_print = vol
        self._last_price = price

    # --- the clock -----------------------------------------------------------------------------
    def on_tick(self, ts_ns: int, price: float, volume: int, bid: float = 0.0, ask: float = 0.0):
        if self._session is None or ts_ns >= self._next_session_ns:
            # ⚠ NinjaTrader DISCARDS this tick from the flow accumulator. Its OnDataPoint takes the
            # new-session branch, calls ResetSession + AddBar, and `return`s BEFORE AccumulateFlow --
            # so the session's first print opens the bar but contributes no signed volume, and
            # `_nTicks` stays 0. Accumulating it here offsets the CVD lattice by one print for the
            # WHOLE session, which shifts every subsequent crossing. Found by the equivalence gate:
            # bar counts were within 1-4% and divergence began at bars 9 / 0 / 16 with sub-second
            # time offsets -- the signature of a small constant offset, not a structural difference.
            self._reset_session(ts_ns, price)
            self._vol = volume      # AddBar still receives the tick's volume
            self._nticks = 0        # ...but the `return` skips the _nTicks++
            return

        self._accumulate(price, volume, bid, ask)

        if price > self._high:
            self._high = price
        if price < self._low:
            self._low = price
        self._nticks += 1
        self._vol += volume

        # THE TIDE RULE -- one bar per lattice line crossed, in order.
        guard = 0
        while guard < 10000:
            guard += 1
            up = (self._level + 1) * self.cfg.delta_per_brick
            down = (self._level - 1) * self.cfg.delta_per_brick
            if self._cvd >= up:
                self._close_bar(price, ts_ns, 1, "flow")
                self._level += 1
                continue
            if self._cvd <= down:
                self._close_bar(price, ts_ns, -1, "flow")
                self._level -= 1
                continue
            break

        # Physical backstops -- escapes so a dead tape cannot freeze the chart, never price rules.
        time_hit = (ts_ns - self._birth_ns) >= self.cfg.time_backstop_ns
        tick_hit = self._nticks >= self.cfg.tick_backstop
        if time_hit or tick_hit:
            self._close_bar(price, ts_ns, 0, "time" if time_hit else "tick")
            self._level = int(math.floor(self._cvd / self.cfg.delta_per_brick + 1e-9))

    def _close_bar(self, close: float, ts_ns: int, flow_dir: int, reason: str):
        ts = max(self.cfg.tick_size, 1e-9)
        dcvd = self._cvd - self._bar_cvd_open
        height_t = (self._high - self._low) / ts
        body_t = (close - self._open) / ts
        # IMPACT: ticks of price per 1,000 contracts of net aggression. Comparable across bars only
        # BECAUSE every flow bar carries the same quantum -- which is why backstop bars are excluded.
        impact = height_t / (abs(dcvd) / 1000.0) if abs(dcvd) > 1 else 0.0

        bar = TideBar(self._session, self._ordinal, self._open, self._high, self._low, close,
                      self._birth_ns, ts_ns, dcvd, flow_dir, body_t, height_t, impact, reason,
                      self._nticks, self._vol)
        self.bars.append(bar)
        if self._on_bar is not None:
            self._on_bar(bar)

        self._ordinal += 1
        self._open = self._high = self._low = close
        self._bar_cvd_open = self._cvd
        self._nticks = 0
        self._vol = 0
        self._birth_ns = ts_ns


def run_files(paths, cfg: TideConfig, on_bar=None, merge_sweeps=False) -> TideClock:
    """Replay one or more exported CSV days through the Tide clock, in file order.

    Bid/Ask rows maintain the prevailing quote; each Last row is one call to `on_tick`. This is
    the harness's stand-in for NT's tick replay, and it is faithful for the same reason NT's is:
    the export preserves NT's own storage order, so quotes reach the signer before the trade they
    priced.
    """
    clock = TideClock(cfg, on_bar=on_bar)
    bid = ask = 0.0
    # merge_sweeps: collapse consecutive Last prints sharing an EXACT timestamp into one arrival.
    # HYPOTHESIS UNDER TEST -- 45.6% of GC prints are same-instant sweep fragments. If NinjaTrader's
    # playback delivers a sweep as ONE OnDataPoint call with summed volume, winsorization sees a
    # single large print and caps it, whereas twelve small fragments each pass uncapped. The harness
    # would then accumulate slightly MORE flow and print slightly MORE bars -- which is exactly the
    # ~1%-more-bars-every-session bias the equivalence gate measured.
    pend_ts = None
    pend_px = 0.0
    pend_vol = 0
    pend_bid = pend_ask = 0.0

    def flush():
        if pend_ts is not None:
            clock.on_tick(pend_ts, pend_px, pend_vol, pend_bid, pend_ask)

    for path in paths:
        for t in iter_l1(path, types=(ASK, BID, LAST)):
            if t.kind == BID:
                bid = t.price
            elif t.kind == ASK:
                ask = t.price
            elif not merge_sweeps:
                clock.on_tick(t.ts_ns, t.price, t.volume, bid, ask)
            elif pend_ts == t.ts_ns:
                pend_px = t.price          # last price of the sweep, as a single print would report
                pend_vol += t.volume
            else:
                flush()
                pend_ts, pend_px, pend_vol, pend_bid, pend_ask = t.ts_ns, t.price, t.volume, bid, ask
    flush()
    return clock


def _census(clock, cfg: TideConfig) -> dict:
    bars = clock.bars
    flow = [b for b in bars if b.reason == "flow"]
    reasons: dict = {}
    for b in bars:
        reasons[b.reason] = reasons.get(b.reason, 0) + 1
    sessions: dict = {}
    for b in bars:
        sessions[b.session] = sessions.get(b.session, 0) + 1
    # G3 -- every flow bar's |dCvd| must equal deltaPerBrick to within ONE PRINT (the gate's own
    # wording). Exact equality is the wrong test and would fail a correct implementation: a print
    # can carry CVD PAST the line, and `_barCvdOpen = _cvd` hands that overshoot to the next bar,
    # so |dCvd| <= deltaPerBrick with a shortfall bounded by the largest winsorized print. What
    # would be a real defect is a shortfall LARGER than any print could explain.
    tol = max(clock.max_print, 1e-6)
    dev = sorted(abs(abs(b.dcvd) - cfg.delta_per_brick) for b in flow)
    off = [b for b in flow if abs(abs(b.dcvd) - cfg.delta_per_brick) > tol]
    disagree = [b for b in flow if b.flow_dir != 0 and b.body_t != 0
                and (1 if b.body_t > 0 else -1) != b.flow_dir]
    imp = sorted(b.impact for b in flow if b.impact > 0)
    return {
        "bars": len(bars), "flow_bars": len(flow), "reasons": reasons,
        "sessions": len(sessions), "per_session": sessions,
        "g3_offlattice": len(off), "g3_worst": dev[-1] if dev else 0.0,
        "g3_tol": tol, "g3_med_dev": dev[len(dev) // 2] if dev else 0.0,
        "absorption": len(disagree),
        "impact_p10": imp[len(imp) // 10] if imp else 0, "impact_med": imp[len(imp) // 2] if imp else 0,
        "impact_p90": imp[len(imp) * 9 // 10] if imp else 0,
        "quote_signed": clock.quote_signed, "tick_signed": clock.tick_signed,
        "ambiguous": clock.ambiguous,
    }


def _main(argv) -> int:
    import argparse
    ap = argparse.ArgumentParser(prog="harness.tide", description="Run SentinelTide over exported NRD CSV.")
    ap.add_argument("files", nargs="+", help="exported CSV day files, in chronological order")
    ap.add_argument("--size", type=float, default=25, help="net delta per bar (BaseBarsPeriodValue)")
    ap.add_argument("--tick", type=float, default=0.1, help="instrument tick size (GC = 0.1)")
    ap.add_argument("--session-open", default="17:00", help="local session open, HH:MM (default CME 17:00 CT)")
    ap.add_argument("--tz", default=NT_TIMEZONE)
    ap.add_argument("--jsonl", help="write every bar to this file")
    ap.add_argument("--sweep", help="comma-separated sizes to compare bar counts (bring-up gate G2)")
    args = ap.parse_args(argv)

    hh, mm = (int(x) for x in args.session_open.split(":"))

    if args.sweep:
        print("%-10s %8s %8s %8s %10s" % ("size", "bars", "flow", "time/tick", "med impact"))
        for s in [float(x) for x in args.sweep.split(",")]:
            cfg = TideConfig(s, args.tick, args.tz, (hh, mm))
            c = _census(run_files(args.files, cfg), cfg)
            print("%-10g %8d %8d %8d %10.1f" % (
                s, c["bars"], c["flow_bars"], c["bars"] - c["flow_bars"], c["impact_med"]))
        return 0

    cfg = TideConfig(args.size, args.tick, args.tz, (hh, mm))
    out = None
    if args.jsonl:
        parent = os.path.dirname(os.path.abspath(args.jsonl))
        if parent:
            os.makedirs(parent, exist_ok=True)
        out = open(args.jsonl, "w", encoding="utf-8")

    def emit(b: TideBar):
        try:
            out.write(json.dumps({
                "session": b.session, "ordinal": b.ordinal,
                "tOpen": datetime.fromtimestamp(b.ts_open_ns / 1e9, tz=timezone.utc).isoformat(),
                "tClose": datetime.fromtimestamp(b.ts_close_ns / 1e9, tz=timezone.utc).isoformat(),
                "o": b.open, "h": b.high, "l": b.low, "c": b.close,
                "dCvd": round(b.dcvd, 4), "flowDir": b.flow_dir,
                "bodyT": round(b.body_t, 2), "heightT": round(b.height_t, 2),
                "impact": round(b.impact, 2), "reason": b.reason,
                "nTicks": b.nticks, "volume": b.volume,
            }) + "\n")
        except (OSError, TypeError, ValueError) as _swex:
            swallow("harness.tide.jsonl", _swex)

    clock = run_files(args.files, cfg, on_bar=(emit if out else None))
    if out:
        out.close()

    c = _census(clock, cfg)
    print("SentinelTide (python harness) — size %g, tick %g, session open %02d:%02d %s"
          % (cfg.delta_per_brick, cfg.tick_size, hh, mm, cfg.tz))
    print("  files            %d" % len(args.files))
    print("  sessions         %d" % c["sessions"])
    print("  bars             %d  (%.0f/session)" % (c["bars"], c["bars"] / max(1, c["sessions"])))
    print("  CLOSE REASONS    %s" % ", ".join("%s=%d (%.1f%%)" % (k, v, 100.0 * v / max(1, c["bars"]))
                                              for k, v in sorted(c["reasons"].items())))
    if c["flow_bars"] < 0.5 * c["bars"]:
        print("    !! under half the bars closed on FLOW -- the size is wrong for this tape.")
        print("       Backstop bars carry no full quantum, so their height is NOT an impact reading.")
    print("  G3 lattice       shortfall vs %g: median %.2f, worst %.2f; tolerance = largest print %.2f"
          % (cfg.delta_per_brick, c["g3_med_dev"], c["g3_worst"], c["g3_tol"]))
    print("                   %s -- %d flow bars exceed one print"
          % ("PASS" if c["g3_offlattice"] == 0 else "FAIL", c["g3_offlattice"]))
    print("  signing          quote %d · tick-rule %d · inside-spread %d"
          % (c["quote_signed"], c["tick_signed"], c["ambiguous"]))
    if c["tick_signed"] > c["quote_signed"] * 0.05:
        print("    !! tick-rule fallback is doing real work -- quote coverage is not clean.")
    print("  impact t/1k      p10 %.0f · median %.0f · p90 %.0f"
          % (c["impact_p10"], c["impact_med"], c["impact_p90"]))
    print("  absorption bars  %d (%.1f%% of flow bars: flow and price disagree)"
          % (c["absorption"], 100.0 * c["absorption"] / max(1, c["flow_bars"])))
    if args.jsonl:
        print("  wrote            %s" % args.jsonl)
    return 0


if __name__ == "__main__":
    raise SystemExit(_main(sys.argv[1:]))
