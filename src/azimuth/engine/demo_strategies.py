"""Reference strategies. They exist to EXERCISE and BENCHMARK the engine.

⚠ These are not trading ideas and no conclusion should ever be drawn from them.
The suite already knows why: DIRECTION IS DEAD, and a moving-average cross is a
coin flip gross. They are here because a throughput number needs a realistic
signal-generation cost attached to it, and because the semantics tests need a
strategy that produces brackets, limits and flips.

The real Sentinel strategies (Keel) arrive via the §2 parity gate, not here.
"""
from __future__ import annotations

import numpy as np

from .strategy import MarketContext, Signals, Strategy


def sma(x: np.ndarray, n: int) -> np.ndarray:
    if n <= 1:
        return x.astype(np.float64)
    c = np.concatenate(([0.0], np.cumsum(x, dtype=np.float64)))
    out = np.full(x.size, np.nan)
    out[n - 1:] = (c[n:] - c[:-n]) / n
    return out


def true_range(bars) -> np.ndarray:
    prev_close = np.concatenate(([bars.close[0]], bars.close[:-1]))
    return np.maximum.reduce([
        bars.high - bars.low,
        np.abs(bars.high - prev_close),
        np.abs(bars.low - prev_close),
    ])


class MaCrossBracket(Strategy):
    """MA cross with a fixed-tick bracket. The sweep workhorse.

    params: fast, slow, stop_ticks, target_ticks, tick_size
    """

    def generate(self, ctx: MarketContext) -> Signals:
        p = self.params
        b = ctx.bars
        tick = float(p.get("tick_size", 0.1))
        f = sma(b.close, int(p.get("fast", 10)))
        s = sma(b.close, int(p.get("slow", 40)))
        d = f - s
        prev = np.concatenate(([np.nan], d[:-1]))
        up = (d > 0) & (prev <= 0)
        dn = (d < 0) & (prev >= 0)

        sig = Signals(b.n)
        sig.entry_long = np.nan_to_num(up, nan=False).astype(bool)
        sig.entry_short = np.nan_to_num(dn, nan=False).astype(bool)

        st = float(p.get("stop_ticks", 20)) * tick
        tg = float(p.get("target_ticks", 40)) * tick
        # A static bracket anchored on the signal bar's close, held for the life
        # of the trade: forward-filled so `sl_*[k]` is a real price on every
        # interval the position is open (per the timing contract in strategy.py).
        anchor_l = _hold(np.where(sig.entry_long, b.close, np.nan))
        anchor_s = _hold(np.where(sig.entry_short, b.close, np.nan))
        sig.sl_long, sig.tp_long = anchor_l - st, anchor_l + tg
        sig.sl_short, sig.tp_short = anchor_s + st, anchor_s - tg

        sig.tag("wide_range", true_range(b) > np.nanmean(true_range(b)))
        return sig


class MaCrossTrail(MaCrossBracket):
    """Same entries plus a rolling-window chandelier trail.

    The stop MOVES every bar, which is exactly what exercises the engine's
    per-interval protective repricing and the amendment ledger.
    """

    def generate(self, ctx: MarketContext) -> Signals:
        sig = super().generate(ctx)
        b, p = ctx.bars, self.params
        tick = float(p.get("tick_size", 0.1))
        k = float(p.get("trail_ticks", 30)) * tick
        w = int(p.get("trail_lookback", 20))
        sig.sl_long = np.maximum(sig.sl_long, roll_max(b.high, w) - k)
        sig.sl_short = np.minimum(sig.sl_short, roll_min(b.low, w) + k)
        # A trail that climbs past its own target is an inverted bracket, which
        # the engine refuses to execute (there is no unambiguous resolution).
        # Clamp it here, in the strategy, where the intent lives.
        sig.sl_long = np.minimum(sig.sl_long, sig.tp_long - tick)
        sig.sl_short = np.maximum(sig.sl_short, sig.tp_short + tick)
        return sig


class LimitPullback(Strategy):
    """Entry LIMIT `offset_ticks` inside the market, valid for `lifetime` bars.

    params: fast, slow, offset_ticks, stop_ticks, target_ticks, tick_size
    """

    def generate(self, ctx: MarketContext) -> Signals:
        p = self.params
        b = ctx.bars
        tick = float(p.get("tick_size", 0.1))
        f = sma(b.close, int(p.get("fast", 10)))
        s = sma(b.close, int(p.get("slow", 40)))
        d = f - s
        prev = np.concatenate(([np.nan], d[:-1]))
        sig = Signals(b.n)
        sig.entry_long = np.nan_to_num((d > 0) & (prev <= 0), nan=False).astype(bool)
        sig.entry_short = np.nan_to_num((d < 0) & (prev >= 0), nan=False).astype(bool)

        off = float(p.get("offset_ticks", 5)) * tick
        sig.entry_limit_long = np.where(sig.entry_long, b.close - off, np.nan)
        sig.entry_limit_short = np.where(sig.entry_short, b.close + off, np.nan)

        st = float(p.get("stop_ticks", 20)) * tick
        tg = float(p.get("target_ticks", 40)) * tick
        al = _hold(sig.entry_limit_long)
        as_ = _hold(sig.entry_limit_short)
        sig.sl_long, sig.tp_long = al - st, al + tg
        sig.sl_short, sig.tp_short = as_ + st, as_ - tg
        return sig


class ScaleAndTrail(Strategy):
    """Enter `qty`, BANK `bank` contracts once the move has gone `first_ticks`,
    trail the rest. `pathlab.py`'s `scale_trail` family, expressed on this engine.

    It is here as an existence proof that the capability the engine gained is
    the one the live thesis needs -- 61% of stop-outs are knocked out with the
    move still running, and scale-and-trail is the obvious candidate fix. No
    conclusion should be drawn from the MA cross underneath it.

    params: fast, slow, qty, bank, first_ticks, stop_ticks, trail_ticks,
            trail_lookback, tick_size
    """

    def generate(self, ctx: MarketContext) -> Signals:
        p, b = self.params, ctx.bars
        tick = float(p.get("tick_size", 0.1))
        qty = int(p.get("qty", 2))
        bank = int(p.get("bank", 1))
        f = sma(b.close, int(p.get("fast", 10)))
        s = sma(b.close, int(p.get("slow", 40)))
        d = f - s
        prev = np.concatenate(([np.nan], d[:-1]))
        up = np.nan_to_num((d > 0) & (prev <= 0), nan=False).astype(bool)
        dn = np.nan_to_num((d < 0) & (prev >= 0), nan=False).astype(bool)

        # ⚠ The cross can fire the SAME direction twice with no opposite cross in
        # between (d touches exactly 0 and recovers). Treated naively that re-arms
        # the anchor and pushes the target from 1 back to 2, which the engine
        # correctly rejects as pyramiding. A trade starts only when the direction
        # actually CHANGES.
        side = _hold(np.where(up, 1.0, np.where(dn, -1.0, np.nan)))
        prev_side = np.concatenate(([np.nan], side[:-1]))
        start = np.isfinite(side) & (~np.isfinite(prev_side) | (side != prev_side))
        anchor = _hold(np.where(start, b.close, np.nan))
        R = float(p.get("first_ticks", 20)) * tick
        hit = np.where(side > 0, b.high >= anchor + R, b.low <= anchor - R)
        armed = _any_since(hit & np.isfinite(anchor), start)

        sig = Signals(b.n)
        sig.position = side * (qty - bank * armed)

        st = float(p.get("stop_ticks", 25)) * tick
        k = float(p.get("trail_ticks", 30)) * tick
        w = int(p.get("trail_lookback", 20))
        sig.sl_long = np.where(side > 0,
                               np.maximum(anchor - st, roll_max(b.high, w) - k), np.nan)
        sig.sl_short = np.where(side < 0,
                                np.minimum(anchor + st, roll_min(b.low, w) + k), np.nan)
        sig.tag("banked", armed)
        return sig


def _any_since(x: np.ndarray, start: np.ndarray) -> np.ndarray:
    """Has `x` been true at or after the most recent `start`? Vectorised.

    The usual reset-accumulate problem, solved by comparing "index of the last
    True" against "index of the last segment start" -- both plain cummaxes.
    """
    i = np.arange(x.size)
    last_true = np.maximum.accumulate(np.where(x, i, -1))
    last_start = np.maximum.accumulate(np.where(start, i, -1))
    return (last_start >= 0) & (last_true >= last_start)


def roll_max(x: np.ndarray, w: int) -> np.ndarray:
    """Trailing max over the last `w` bars, inclusive. Vectorised, no lookahead."""
    from numpy.lib.stride_tricks import sliding_window_view

    w = max(1, int(w))
    pad = np.concatenate((np.full(w - 1, x[0]), x))
    return sliding_window_view(pad, w).max(axis=1)


def roll_min(x: np.ndarray, w: int) -> np.ndarray:
    from numpy.lib.stride_tricks import sliding_window_view

    w = max(1, int(w))
    pad = np.concatenate((np.full(w - 1, x[0]), x))
    return sliding_window_view(pad, w).min(axis=1)


def _hold(x: np.ndarray) -> np.ndarray:
    """Forward-fill non-NaN values."""
    idx = np.where(np.isfinite(x), np.arange(x.size), -1)
    np.maximum.accumulate(idx, out=idx)
    return np.where(idx >= 0, x[np.maximum(idx, 0)], np.nan)
