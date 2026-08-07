"""
Barrier labels, censoring, and uniqueness weights.

Why not "did the trade win": a trade label only exists for verdicts a strategy
actually took -- i.e. those above the conviction floor. Barrier labels exist for
EVERY verdict, including the ones we stood down on. That counterfactual is the
whole reason the floor can be LEARNED rather than merely validated.
"""
from __future__ import annotations

import numpy as np
import pandas as pd

WIN, LOSS, CENSORED = 1, 0, -1


def make_labels(df: pd.DataFrame, horizon_min: int = 15) -> pd.DataFrame:
    """
    Returns a frame with columns: y (1/0/-1), t0, t1, mode.

    Prefers schema-1.3 `firstTouch`. Falls back to a PESSIMISTIC milestone label
    for 1.2 rows -- because maxMFE/maxMAE are running maxima to EOD and msToMFE /
    msToMAE are the times of those MAXIMA, not of first touch. When both barriers
    were breached inside the horizon, 1.2 genuinely cannot say which came first.
    We resolve those to LOSS: conservative, and it matches the direction of the
    bar-resolution optimism we already know we have.
    """
    t0 = df["fireTime"]

    if "firstTouch" in df.columns and df["firstTouch"].notna().any():
        ft = pd.to_numeric(df["firstTouch"], errors="coerce")
        raw_ms = df["msToFirstTouch"] if "msToFirstTouch" in df.columns \
            else pd.Series(np.nan, index=df.index)
        ms = pd.to_numeric(raw_ms, errors="coerce")
        y = np.where(ft > 0, WIN, np.where(ft < 0, LOSS, CENSORED))
        # Censored rows never resolved -- give them the full horizon for the overlap
        # calculation, then drop them from training.
        dur = ms.fillna(horizon_min * 60_000.0)
        return pd.DataFrame({
            "y": y,
            "t0": t0,
            "t1": t0 + pd.to_timedelta(dur, unit="ms"),
            "mode": "firsttouch",
        })

    # ---- schema 1.2 fallback -------------------------------------------------------------
    if horizon_min not in (1, 5, 15, 60):
        raise ValueError("1.2 fallback horizons are the recorder milestones: 1, 5, 15, 60")
    mfe = pd.to_numeric(df[f"mfe{horizon_min}"], errors="coerce")
    mae = pd.to_numeric(df[f"mae{horizon_min}"], errors="coerce")
    bt = df["barrierTicks"] if "barrierTicks" in df.columns else pd.Series(20.0, index=df.index)
    R = pd.to_numeric(bt, errors="coerce").fillna(20.0)

    hit_t = mfe >= R
    hit_s = mae >= R
    y = np.where(hit_t & ~hit_s, WIN,
        np.where(hit_s, LOSS, CENSORED))          # ambiguous (both) -> LOSS, pessimistic
    y = np.where(mfe.isna() | mae.isna(), CENSORED, y)

    return pd.DataFrame({
        "y": y,
        "t0": t0,
        "t1": t0 + pd.Timedelta(minutes=horizon_min),
        "mode": f"horizon{horizon_min}m",
    })


def uniqueness_weights(t0: pd.Series, t1: pd.Series) -> np.ndarray:
    """
    Average uniqueness per AFML ch. 4.

    Two verdicts 90 seconds apart share most of their forward window. They are NOT
    independent observations. Without this weight, effective N is wildly overstated
    and every significance test downstream lies.

    Concurrency is piecewise-constant with breakpoints at the window endpoints, so
    this is exact (not a grid approximation) and runs O(n log n).
    """
    a = t0.astype("int64").to_numpy() / 1e9
    b = t1.astype("int64").to_numpy() / 1e9
    b = np.maximum(b, a + 1e-6)                       # guard zero-length windows

    breaks = np.unique(np.concatenate([a, b]))
    seg_len = np.diff(breaks)

    # concurrency on each segment, via a difference array over the sweep
    delta = np.zeros(len(breaks), dtype=np.int64)
    np.add.at(delta, np.searchsorted(breaks, a), 1)
    np.add.at(delta, np.searchsorted(breaks, b), -1)
    conc = np.cumsum(delta)[:-1]                      # concurrency on segment k
    conc = np.maximum(conc, 1)

    prefix = np.concatenate([[0.0], np.cumsum(seg_len / conc)])

    i0 = np.searchsorted(breaks, a)
    i1 = np.searchsorted(breaks, b)
    u = (prefix[i1] - prefix[i0]) / (b - a)
    return np.clip(u, 1e-6, 1.0)


def effective_n(weights: np.ndarray) -> float:
    """
    Concurrency-adjusted sample size = SUM of average uniqueness (AFML ch. 4).

    NOT the Kish formula ((Σw)²/Σw²). Kish measures how much *unequal weighting* cost
    you -- with equal weights it returns n no matter how small those weights are, which
    for overlapping labels reports ~n independent observations when there may be a
    handful. What we want is "how many non-overlapping observations is this equivalent
    to", and that is Σū.
    """
    return float(np.asarray(weights, dtype=float).sum())


def kish_n(weights: np.ndarray) -> float:
    """Kish ESS -- reported only as a secondary diagnostic of weight dispersion."""
    w = np.asarray(weights, dtype=float)
    return float(w.sum() ** 2 / np.square(w).sum())


def breakeven_probability(barrier_ticks: float, cost_ticks: float) -> float:
    """
    A symmetric +-R barrier with round-trip cost c has expectancy
        EV = p(R - c) + (1 - p)(-R - c) = 2pR - R - c
    so EV > 0  <=>  p > (R + c) / 2R.
    """
    return (barrier_ticks + cost_ticks) / (2.0 * barrier_ticks)


def expectancy_floor(conv: np.ndarray, p_hat: np.ndarray,
                     barrier_ticks: float, cost_ticks: float) -> float:
    """
    Lowest conviction at which the CALIBRATED win probability clears breakeven --
    and stays clear above it. This is the honest replacement for the hand-set 0.35.
    Returns nan when no conviction level is profitable (a real and useful answer).
    """
    p_star = breakeven_probability(barrier_ticks, cost_ticks)
    order = np.argsort(conv)
    c_sorted, p_sorted = conv[order], p_hat[order]

    profitable = p_sorted >= p_star
    if not profitable.any():
        return float("nan")
    # walk down from the top; the floor is where it last became true and stayed true
    idx = len(profitable) - 1
    while idx > 0 and profitable[idx - 1]:
        idx -= 1
    return float(c_sorted[idx])
