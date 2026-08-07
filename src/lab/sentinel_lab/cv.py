"""
Purged walk-forward cross-validation with an embargo.

NEVER use random k-fold here. Overlapping forward label windows mean adjacent rows
share outcomes; random k-fold leaks the test fold into training and is the single
most common way people convince themselves they have an edge they do not have.
(Lopez de Prado, *Advances in Financial Machine Learning*, ch. 7.)

Expanding window, strictly forward:

    train:  rows whose LABEL WINDOW closed at least `embargo` before the fold opens
            -- i.e. t1 <= test_start - embargo.  A row that fired before the fold but
            whose outcome resolves inside it is PURGED: its label is contaminated.
    test:   rows firing inside the fold.
"""
from __future__ import annotations

import numpy as np
import pandas as pd


def purged_walk_forward(t0: pd.Series, t1: pd.Series, n_splits: int = 5,
                        embargo: pd.Timedelta = pd.Timedelta(minutes=60),
                        min_train: int = 100):
    """Yield (train_idx, test_idx) index arrays, oldest fold first."""
    t0 = pd.Series(t0).reset_index(drop=True)
    t1 = pd.Series(t1).reset_index(drop=True)
    n = len(t0)
    if n < min_train + n_splits:
        raise SystemExit(f"only {n} rows -- not enough for {n_splits} purged folds")

    # Fold boundaries by row count over the (already time-sorted) frame, but the
    # purge is applied in TIME, which is what actually matters.
    start = min_train
    edges = np.linspace(start, n, n_splits + 1).astype(int)

    for k in range(n_splits):
        lo, hi = edges[k], edges[k + 1]
        if hi - lo < 1:
            continue
        test_idx = np.arange(lo, hi)
        test_start = t0.iloc[lo]

        eligible = t1 <= (test_start - embargo)
        train_idx = np.flatnonzero(eligible.to_numpy())
        train_idx = train_idx[train_idx < lo]        # strictly forward

        if len(train_idx) < min_train:
            continue
        yield train_idx, test_idx


def holdout_split(df: pd.DataFrame, frac: float = 0.2,
                  embargo: pd.Timedelta = pd.Timedelta(hours=24)):
    """
    Carve a FINAL untouched period off the end. Spend it once, at the very end.
    Every hyperparameter you try burns significance; this is the only unburnt number
    you will have.
    """
    n = len(df)
    cut = int(n * (1 - frac))
    hold_start = df["t0"].iloc[cut]
    dev = df.index[(df["t1"] <= hold_start - embargo)]
    hold = df.index[df["t0"] >= hold_start]
    return np.asarray(dev), np.asarray(hold)
