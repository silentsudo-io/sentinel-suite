"""Loading the real tape for bar-type work -- including the one clause it violates.

`engine.contract.load_session` REFUSES the shipped `tape\\GC 02-26` files. Measured
2026-08-05: `2025-12-09.parquet` carries 140 rows with `bid > ask` and validation
stops at the first one. That is SPEC §3.2's open defect -- crossed quotes are real L1
event granularity, the population is not yet characterised (two measurements disagree
by ~5x), and no policy has been agreed.

⛔ This module does NOT decide that policy and does not touch `engine\\` or `tape\\`.
It does the narrow thing a bar-type port needs:

  * every other contract clause is still enforced HARD -- dtypes, monotonic ts_ms,
    finite book, legal `kind`, zero size on quote rows, the bar-snap check -- by
    running `contract.validate` on a PROBE copy whose ask is widened to the bid;
  * the crossed rows are COUNTED and returned, never dropped and never repaired;
  * the tape handed back is the file, unmodified.

⭐ Why that is sound for THIS track and not a general licence: the tape must reach a
bar type as NinjaTrader saw it, crossed rows included, because the gate compares
against what NT actually produced -- not against a cleaner book NT never had.

⛔ CORRECTED 2026-08-05. This note previously claimed "Renko, TBars and Flux never
touch bid/ask, so a crossed quote cannot move a bar boundary." **That is FALSE for
Flux.** SentinelFlux classifies every trade by the Lee-Ready quote rule, which reads
bid and ask on each print; measured, a quote-less rebuild yields 3,261 vs 3,171 bars
on 2025-12-09 sharing only 15.6% of boundaries. The BEHAVIOUR here was right for the
wrong reason, and the wrong reason is the dangerous half: it invites a later
"simplification" that repairs the book on the way in and silently changes every Flux
boundary. Renko and TBars do read `last`/`size` only; Flux does not.

The fill model (§4.3) is a separate place the same defect bites, and that is the
engine's problem to resolve, not something to paper over here.
A silently dropped row is a silently changed fill.
"""
from __future__ import annotations

import json
import os
from dataclasses import dataclass

import numpy as np

from engine.contract import (
    KIND_TRADE,
    Tape,
    TapeContractError,
    _read_parquet,
    concat,
    validate,
    validate_sidecar,
)

TAPE_ROOT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "tape")


@dataclass
class LoadedTape:
    tape: Tape
    #: per-session sidecar dicts, index == session_id
    sidecars: list[dict]
    #: session_date per session_id, in load order
    session_dates: list[str]
    #: rows with bid > ask, per session_id (SPEC §3.2)
    crossed: list[int]

    @property
    def n_crossed(self) -> int:
        return int(sum(self.crossed))


def load_meta(path: str) -> dict:
    """Read a sidecar. Missing -> {}; CORRUPT -> an exception, never the same silence."""
    if not os.path.isfile(path):
        return {}
    with open(path, "r", encoding="utf-8") as fh:
        return json.load(fh)


def _load_one(path: str) -> tuple[Tape, dict, int]:
    cols = _read_parquet(path)
    meta_path = path.rsplit(".parquet", 1)[0] + ".meta.json"
    meta = load_meta(meta_path)
    if not meta:
        raise TapeContractError(
            "no provenance sidecar next to %s -- a tape file without its sidecar is not "
            "admissible to a gate (§3.1)" % path)
    validate_sidecar(meta, meta_path)

    n = cols["ts_ms"].shape[0]
    t = Tape(cols["ts_ms"], cols["bid"], cols["ask"], cols["last"], cols["size"],
             cols["bid_size"], cols["ask_size"], cols["kind"],
             np.zeros(n, dtype=np.int32), [meta], str(meta.get("instrument", "")))

    crossed = int(np.count_nonzero(t.ask < t.bid))
    # Every clause except the crossed-book one, enforced on a throwaway copy.
    probe = Tape(t.ts_ms, t.bid, np.maximum(t.ask, t.bid), t.last, t.size,
                 t.bid_size, t.ask_size, t.kind, t.session_id, t.sessions, t.instrument)
    validate(probe)
    return t, meta, crossed


def load_sessions(paths: list[str]) -> LoadedTape:
    """Load contract tape files in the order given, surfacing the crossed-quote count."""
    if not paths:
        raise TapeContractError("no session files given: an empty side is ABORT, not PASS")
    parts, metas, crossed = [], [], []
    for p in paths:
        t, m, c = _load_one(p)
        parts.append(t)
        metas.append(m)
        crossed.append(c)
    for a, b in zip(parts, parts[1:]):
        if a.ts_ms[-1] > b.ts_ms[0]:
            raise TapeContractError("sessions overlap or are out of order")
    if len(parts) == 1:
        tape = parts[0]
    else:
        # concat() re-validates; feed it books that are not crossed, then restore.
        widened = [Tape(t.ts_ms, t.bid, np.maximum(t.ask, t.bid), t.last, t.size,
                        t.bid_size, t.ask_size, t.kind, t.session_id, t.sessions, t.instrument)
                   for t in parts]
        tape = concat(widened)
        tape.ask = np.concatenate([t.ask for t in parts])
    dates = [str(m.get("session_date", os.path.basename(p).rsplit(".parquet", 1)[0]))
             for m, p in zip(metas, paths)]
    return LoadedTape(tape=tape, sidecars=metas, session_dates=dates, crossed=crossed)


def discover(instrument: str, root: str = TAPE_ROOT) -> list[str]:
    d = os.path.join(root, instrument)
    if not os.path.isdir(d):
        raise TapeContractError("no tape directory %s" % d)
    return sorted(os.path.join(d, f) for f in os.listdir(d) if f.endswith(".parquet"))


def session_path(instrument: str, session_date: str, root: str = TAPE_ROOT) -> str:
    p = os.path.join(root, instrument, session_date + ".parquet")
    if not os.path.isfile(p):
        raise TapeContractError("no tape file %s" % p)
    return p


def trade_rows(tape: Tape) -> np.ndarray:
    """Indices of the rows a tick-built bar type sees: trades with a finite price.

    NinjaTrader builds Renko from `BarsPeriodType.Tick`, i.e. the Last series. Quote
    rows are not data points to it and must not be counted, priced or volumed.
    """
    return np.flatnonzero((tape.kind == KIND_TRADE) & np.isfinite(tape.last))
