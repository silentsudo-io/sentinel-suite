"""SentinelFlux — order-flow IMBALANCE bars, the Python column (Azimuth §1, Phase 4).

PORT OF   ``bin\\Custom\\BarsTypes\\SentinelFlux_v1_0_0.cs``   (BarsPeriodType id **212203**).
The C# is the source of truth and is READ ONLY. Where the design doc
(``Docs\\SENTINEL_FLUXBARS_SPEC.md``) disagrees with the code, the CODE WINS and the
disagreement is recorded in DOC-VS-CODE below.

⚠ NOTHING HERE IS GATED AGAINST NINJATRADER. See THE GATE at the bottom of this
  docstring for exactly what blocks it. A plausible-looking Flux port is the easiest
  wrong thing in this tree to believe, so read that section before trusting a number.

────────────────────────────────────────────────────────────────────────────────
THE CRUX — how a trade is classified buy- or sell-initiated
────────────────────────────────────────────────────────────────────────────────
Everything else is bookkeeping. Get this wrong and every bar boundary is wrong.
``SentinelFlux_v1_0_0.SignTrade`` (C# lines 216-229), verbatim in behaviour:

    if useQuoteRule and ask > 0 and bid > 0:
        if price >= ask:  lastTradeSign = +1; return +1     # lifted the offer
        if price <= bid:  lastTradeSign = -1; return -1     # hit the bid
    cmp = price <=> rawClose                                # TICK-RULE FALLBACK
    if cmp > 0: lastTradeSign = +1
    elif cmp < 0: lastTradeSign = -1
    return lastTradeSign                                    # cmp == 0 -> carry

Four things a casual reading loses, all of which change bar boundaries:

1. **The quote test is ``>= ask`` / ``<= bid``, not ``>``/``<``.** A trade AT the
   ask is a buy; a trade AT the bid is a sell. Nearly every futures print is one
   or the other, so the tick rule almost never runs.
2. **``rawClose`` is the PREVIOUS TRADE's price, and it survives a bar close.**
   ``OnDataPoint`` calls ``SignTrade(close, ...)`` BEFORE assigning ``rawClose = close``,
   and ``CloseBar`` re-seeds with ``rawBarOpen = rawHigh = rawLow = rawClose = rawClose``
   (the same value). So the tick rule's reference price is continuous across bars —
   it is NOT reset to the bar open.
3. **``lastTradeSign`` is a carry that the QUOTE rule also writes.** A quote-rule
   hit updates the fallback state, so a later inside-the-spread print inherits the
   last quote-classified direction, not the last tick-classified one.
4. **On a CROSSED book (bid > ask) the ask test is evaluated FIRST**, so any price
   in ``[ask, bid]`` classifies **+1 (buy)**. That is not a considered rule — it is
   what the ordering of two ``if``s produces — but it IS the behaviour NinjaTrader
   ran, so the port reproduces it exactly (``sign_trade`` below, and
   ``CrossedPolicy.RAW``).

Weight per trade (``TradeWeight``): ``w = max(1, volume)``; mode Volume (VIB, the
default) uses ``w``, Tick uses ``1.0``, Dollar uses ``w * price``. Contribution is
``Δθ = sign * w``.

────────────────────────────────────────────────────────────────────────────────
THE PARAMETER — what "8" in ``212203v8`` means, and what "@AUD" is not
────────────────────────────────────────────────────────────────────────────────
Evidence chain, all read, none guessed:

* ``SentinelCore.BarTag(bp)`` (SentinelCore_v1_0_0.cs:486) = ``str(int(BarsPeriodType)) +
  "v" + str(Value)`` and appends ``"x" + Value2`` **only when Value2 != 0**.
* ``SentinelFlux.OnStateChange/State.Configure`` sets ``BarsPeriod.Value =
  BarsPeriod.BaseBarsPeriodValue`` and ``BarsPeriod.Value2 = 0``, and renames
  ``BaseBarsPeriodValue`` to the UI label **"Flux Size"**.
* ``ApplyDefaultBasePeriodValue``/``ApplyDefaultValue`` set it to ``FluxRefSize = 8``.
* ``LatchConfig``: ``fluxScale = BaseBarsPeriodValue / FluxRefSize`` (fallback 8).

⇒ ``212203v8``  =  SentinelFlux, **Flux Size 8**, ``fluxScale = 8/8 = 1.0``, and the
absent ``x`` segment is not a missing field — it is ``Value2 == 0`` by construction.
Flux Size is a **multiplier on E[|θ|]**, i.e. the information per bar; raising it
coarsens the chart. ``θ* = max(1.0, fluxScale * imbEwma)``.

``@AUD`` is **not a bar parameter at all.** ``SentinelCore.ComposeLane`` (line 575)
appends ``"@" + lane`` to the scope, and ``Sentinel\\Models\\GC\\212203v8@AUD\\Lane.conf``
is the **AUDITION lane** — ``floor=0``, ``deadband=0``, vetoes off, 21 voters at ``w=1``,
"the fused verdict is THROWAWAY". It changes which Council rows get RECORDED and
nothing whatsoever about bar construction. ``212203v8`` and ``212203v8@AUD`` are the
SAME bars; a gate must never treat them as two bar types.

────────────────────────────────────────────────────────────────────────────────
DOC-VS-CODE — where SENTINEL_FLUXBARS_SPEC.md disagrees with the C#
────────────────────────────────────────────────────────────────────────────────
1. **§8 lists eleven F6 parameters. The shipped class exposes ONE.** ``ImbalanceMode``,
   ``SignMode``, ``IntensityLen``, ``WinsorMult``, ``AtrLength``, ``PriceBackstopMult``,
   ``ForceStagnationSeconds``, ``MaxTicksPerBar``, ``PublishFluxState`` and
   ``ShowIndicatorLabel`` are **private fields with hardcoded values**, not properties;
   ``State.Configure`` in fact REMOVES ``Value``/``Value2``/``ReversalType`` from the grid.
   Only ``BaseBarsPeriodValue`` ("Flux Size") is settable. This port mirrors the C#
   defaults and lets a caller override them in ``FluxParams`` — but a differing value
   is NOT something NinjaTrader could have produced, so the gate's ``bar_params``
   precondition carries the full string.
2. **§8/§5 name ``EnableMicroSplit`` and ``EnableQuietHours`` as inherited TBars
   behaviour. Neither exists in the code.** There is no micro-split and no quiet-hours
   handling in SentinelFlux at all.
3. **§5 says "update ``expT``/``expImb``/``atr``".** There is no ``expT`` (expected bar
   LENGTH) anywhere in the shipped class; the López de Prado ``E₀[T]·|E[b·v]|`` form
   described in §2 was replaced by ``θ* = fluxScale · E[|θ|]`` in the same-day hotfix.
   §5 records the hotfix and then still lists ``expT``. Only ``imbEwma`` and ``atrEma`` exist.
4. **§3/§5 say the threshold is "ATR-clamped" with "density rails".** It is not: the
   hotfix removed ATR from θ* entirely (C# line 242-253). ATR now drives ONLY the price
   backstop. §5's own hotfix note says this; the surrounding prose was not updated.
5. **``Lab\\sentinel_lab\\bartag.py`` renders ``212203v8`` as "SentinelFlux thr 8"**,
   while its own docstring (twice) promises ``"SentinelFlux 8"``. The label is also
   substantively wrong: its comment calls the parameter "the imbalance THRESHOLD", but
   the C# makes it a SCALE on the measured threshold, not the threshold. Display-only,
   but it is the label a human reads next to 67k corpus rows.

Not a contradiction, but a defect worth naming: the closing tick's volume is counted
**three times** — see VOLUME below.

────────────────────────────────────────────────────────────────────────────────
VOLUME — a real accounting quirk, reproduced deliberately
────────────────────────────────────────────────────────────────────────────────
``BarsType.UpdateBar(bars, high, low, close, time, volume)`` **ADDS** the volume
argument to the current bar. Proof from NinjaTrader's own shipped source in this tree:
``BarsTypes\\@VolumeBarsType.cs:52`` computes
``volumeTmp = Math.Min(barsPeriodValue - bars.GetVolume(bars.Count - 1), volume)``
and passes that to ``UpdateBar`` — an argument that is only meaningful if UpdateBar
accumulates. ``AddBar`` SETS the new bar's volume.

Consequence in ``OnDataPoint``: on the tick that closes a bar, ``UpdateFormingBar``
adds ``v`` to the closing bar, ``CloseBar`` calls ``UpdateBar`` again and adds ``v`` a
second time, and then ``AddBar`` seeds the NEXT bar with ``v`` a third time. So

    bar.volume = v[seed] + Σ v[ticks after seed .. close] + v[close]

The port reproduces this (``volume_semantics="nt"``). ``volume_semantics="once"`` counts
each tick exactly once and exists so the difference is measurable when the gate runs;
it is NOT what NinjaTrader stored.

────────────────────────────────────────────────────────────────────────────────
CROSSED QUOTES (spec §3.2) — the declared policy, applied in ONE place
────────────────────────────────────────────────────────────────────────────────
``apply_crossed_policy`` is the only function in this module that touches bid/ask, and
it returns a ``CrossedReport`` that ``build`` carries onto the result, so a repaired
row is never silent. Measured on this tape (``tape\\GC 02-26``, 17 sessions,
35,345,097 rows, 2,951,690 trades): **3,775 crossed rows = 0.0107%**, of which **58 land
on a TRADE row** — 0.0020% of all trades, but NOT evenly spread: 12 sessions carry 0 or 1,
2025-12-26 carries 4 and **2025-12-31 carries 43**. Crossed QUOTE rows cannot reach
``sign_trade`` at all, because a Tick-built BarsType only ever sees trades.

**Those 58 rows are enough to move bars, and that is measured, not assumed.** On
2025-12-31, if the classifier is allowed to read the repaired book, hold-last-valid
yields **3397** bars against the faithful **3395**, and DROP yields **3424** — from
deleting 43 real trades. §3.2's warning is literal: a silently repaired or dropped row
is a silently different bar.

⇒ **The concern is SPLIT, because the two halves have different right answers.**

* The **BOOK** the engine prices fills against is repaired, so the tape satisfies §3.1
  and spread cost can never go negative (§4.3 / §5.4).
* The **SIGNING** path always reads the **RAW, unrepaired** quotes (``build(...,
  sign_book="raw")``, the default), because NinjaTrader saw the crossed book and the
  parity gate can only ever be faithful to what NinjaTrader saw. Repairing the book for
  the classifier would make the port disagree with its own reference by construction.

With that split, hold-last-valid and raw produce **identical bars on every session** —
the repair costs nothing precisely because it is kept out of the classifier.

Policies: ``HOLD_LAST_VALID`` (default; carry the last uncrossed (bid, ask) pair as a
unit, so the book stays internally consistent — a leading crossed row with no valid
predecessor falls back to WIDEN_TO_TOUCH and is counted separately), ``WIDEN_TO_TOUCH``
(bid = ask = mid, tick-rounded), ``DROP`` (remove the row; deletes real trades, changes
the tape length and therefore every tape index — never the default), and ``RAW`` (no
repair at all; the resulting Tape deliberately FAILS ``engine.contract.validate`` and the
report says so).

Note that the §3.2 "shell track" figure — 0.05%, 716 rows, concentrated at session
start on 2025-12-09 — is **not reproducible on the current tape files**: that session
carries 140 crossed rows (0.0090%), of which 12 fall in the first 1% of rows and 2
within the first 60 seconds. The tape-track figure is the one that survives
re-measurement. The 5× disagreement is not "unresolved population"; on this data it is
a stale number.

────────────────────────────────────────────────────────────────────────────────
TICK ROUNDING — was the top parity risk, now MEASURED (2026-08-05)
────────────────────────────────────────────────────────────────────────────────
``RoundToTick`` calls ``MasterInstrument.RoundToTickSize``, whose midpoint rule is not
readable from this side. Heikin-Ashi closes are means of four prices on a 0.1 grid, so
exact-half cases are COMMON — this was never academic. The first gate run settled it
empirically against 558 NinjaTrader bars whose ``open/high/low/volume/ts`` all matched
exactly (so the bar composition was known-correct) by reconstructing the unrounded
``HaClose`` from the tape and testing each candidate rule:

    half-even, x * (1/tick)   87 mismatches   <- what this port shipped first
    half-even, x / tick       59 mismatches
    half-AWAY, x / tick        0 mismatches   <- NinjaTrader

⇒ ``MasterInstrument.RoundToTickSize`` is **``MidpointRounding.AwayFromZero``**, and the
division must be a **division**. Two independent defects, both fixed:

1. ``rounding`` now defaults to ``"half_away"``. ``"half_even"`` (.NET's bare
   ``Math.Round(double)``) is retained only so the measurement above can be re-run.
2. The hot path multiplied by ``1.0 / tick``. ``x / 0.1`` and ``x * (1/0.1)`` disagree at
   the last ULP on **35.5%** of GC-range prices (measured over 200,000 samples) — and the
   last ULP is precisely what decides a midpoint. This is the sibling TBars agent's
   *"divide by tickSize, never multiply by its reciprocal"* and the Renko
   ``4237.900000000001`` failure, in a third costume. **It is a family, not a coincidence:
   never reach for the reciprocal in tick-grid code.**

⭐ Why an 87-in-558 defect produced 2,837 differing rows out of 3,259: **Heikin-Ashi is
RECURSIVE.** ``haOpen`` is ``(prev haOpen + prev haClose)/2`` and the stored open is the
ROUNDED value, so one wrong close propagates into every later bar's open, high and low
until a coincidence resynchronises it.

────────────────────────────────────────────────────────────────────────────────
GATE RESULT — RUN 2026-08-05 · FAIL(1) · the port is NOT verified
────────────────────────────────────────────────────────────────────────────────
Reference: ``Sentinel\\Harness\\bars\\20260805T020053__GC__212203v8.jsonl`` — a
SentinelBarDump over a **Market Replay** chart (all rows ``rt=0``, a pure historical
rebuild, but replay supplied bid/ask so the quote rule was live: 3,259 bars, versus
3,171 for a tick-rule-only build — the faithful number).

    bars 0..558   IDENTICAL on ts_ms, open, high, low, close AND volume
    bar  558      boundary diverges; the difference cascades
    verdict       FAIL(1) — 559 matched, 2,700 differ, 1 extra (was 422 / 2,837)

**What the exact prefix proves.** 559 consecutive bars agreeing on every field is not a
weak signal — it independently confirms quote-rule signing (ask tested first), the θ
accumulator, E[|θ|]'s EWMA and winsorization, the ATR and all four backstops, the
Heikin-Ashi geometry, the ``bar_index`` convention, and the triple-count volume model
(``seed + body + closing tick`` matched NinjaTrader on **557/557** bars; the three
alternative models matched **0**).

**The residual, characterised — and NOT fixed.** At bar 558 my θ reaches **−11** against
a threshold of **10.7328**: it crosses by **0.2672**, i.e. by a single 1-lot trade, on
the tightest margin the clock can produce (median |θ|/θ\* at close across all imbalance
bars is **1.0326** — this bar type *always* closes barely over the line, so one contract
decides a boundary). NinjaTrader closed the same bar one trade later. Ruled out by
measurement, not by argument:

* **not a tick-rule ambiguity** — of 146,547 trades in the session, **0** fall strictly
  inside the spread and **0** lack a quote, so every sign is forced by the quote rule;
* **not a volume-model error** — see 557/557 above, and NinjaTrader's bar-558 volume (63)
  matches a bar ending at MY closing row, not at its own stamp (which would give 64);
* **not a stamp convention** — "stamp = closing tick" explains 558/559 bars; the rival
  "stamp = next trade" explains only 331;
* **not an accumulation bug** — θ = −11 reproduces from the tape two independent ways.

⇒ What remains is that **NinjaTrader's replay tick stream and the ``.nrd``-derived tape
disagree by one 1-lot print (or by one trade's prevailing quote) at that instant.** That
is a TAPE-fidelity question, owned by the tape track, and it is not resolvable read-only
from this side. ⛔ Do not "fix" it by nudging a threshold or a comparison operator; that
would fit the port to one session's noise and destroy the evidence above.

────────────────────────────────────────────────────────────────────────────────
THE GATE — how to run it
────────────────────────────────────────────────────────────────────────────────
⚠ **CORRECTION (2026-08-05).** An earlier draft of this docstring said "NinjaTrader has
no read-only bars export" and concluded the gate needed tape from July 2026. **That was
wrong on both counts and the correction matters more than the original claim** — a false
"impossible" in a doc stops the next reader from looking. The gate has since RUN; see
GATE RESULT above.

**The export exists: ``bin\\Custom\\Indicators\\SentinelBarDump_v1_0_0.cs``** (schema
``bars.1``, compiled, already used once — ``Sentinel\\Harness\\bars\\*__GC__212207v25.jsonl``).
Verified by reading it: ``Calculate.OnBarClose``, one JSONL row per COMPLETED bar
(``i, t, o, h, l, c, v, rt, newSession``) plus a self-describing header carrying
``periodType / periodValue / periodValue2 / baseValue / tickSize / tradingHours``. It reads
generic ``Time/Open/High/Low/Close/Volume``, so it works on a Flux chart unchanged, and it
**deliberately has no realtime gate** — the historical rebuild alone produces a full answer
key. That is exactly the ``bartype`` artefact's ``open/high/low/close/volume/ts``.
``bars\\ntdump.py`` (sibling-owned) reads it; ``read_dump`` / ``find_dumps`` / ``iso_to_ms`` /
``gate_rows`` are bar-type agnostic and reused here. Its ``bar_params_of`` is **not** — it
hard-refuses ``periodType != 11`` (Renko), so Flux gets ``bar_params_from_dump_header``
below. My earlier rejection of ``Sentinel\\BrickLog`` was still right for the right reason
(θ/thr/reason, no OHLCV) — I simply aimed at the wrong artefact.

**And the July-2026 data problem dissolves.** The gate does not need a session on which
Flux happened to run live; it needs a Flux chart over any window the tape covers.

⇒ **THE RECIPE — one manual chart, ~10 minutes, no code.** (Do not automate it:
programmatic indicator attach does not work on this build, and ``chartseries`` mutates.)

1. **Connections → Playback / Market Replay.** ⭐ Use Market Replay, **not** a plain
   historical chart. Measured reason, below.
2. Replay data: **``GC 02-26``**, date **2025-12-09** (any of the 17 tape sessions works;
   ``db\\replay\\GC 02-26`` holds all 23 days incl. 20260102).
3. New chart, instrument **``GC 02-26``**, bar type **``SentinelFlux v1.0.0``**,
   **Flux Size = 8** (the only knob; gives bartag ``212203v8``).
4. Chart properties: **Break at EOD = True** (the port's ``reset_on_new_session`` default,
   and the C#'s ``IsResetOnNewTradingDay`` branch), trading hours **``Nymex Metals -
   Energy ETH``** (the tape's session window is 18:00→17:00 ET).
5. Add indicator **``Sentinel Bar Dump v1.0.0``** (Indicators → Sentinel). Defaults are fine.
6. Run the replay through the whole session. The card shows BARS WRITTEN; the file lands
   in ``Sentinel\\Harness\\bars\\<stamp>__GC__212203v8.jsonl``.
7. ``run_bartype_gate(reference_rows_from_dump(path, session=...), build_session(...))``.

⚠ **WHY MARKET REPLAY AND NOT A HISTORICAL CHART — two measurements.**

* **The local historical tick cache has no quotes.** ``db\\tick\\GC 02-26`` holds **321
  files, every one ``*.Last.ncd``**; there is **not a single ``.Bid.ncd`` or ``.Ask.ncd``
  anywhere under ``db\\tick``**. ``SignTrade``'s guard is ``ask > 0 && bid > 0``, so on a
  quote-less rebuild NinjaTrader would silently fall back to the **pure tick rule** while
  this port signs by the quote rule — the exact degradation SENTINEL_FLUXBARS_SPEC §10
  warns about. Measured cost on 2025-12-09: quote rule **3261** bars vs tick-rule-only
  **3171**, sharing only **507 boundaries (15.6%)**. The gate would FAIL on a data defect
  and look like a port defect.
* **The cache does not even cover the window**: it ends **2025-12-18**, and 2026-01-02 has
  **zero** files — 9 of the 17 tape sessions, quote-less.
* Market Replay feeds bid/ask **and** last from the same ``.nrd`` files the tape was built
  from (``2025-12-09.meta.json`` cites ``20251208.nrd`` + ``20251209.nrd``), so §2's
  "input tape identical" PRECONDITION becomes *provable* rather than assumed.

⇒ **Built-in diagnostic:** if the dump lands near **3171** bars with ~15% boundary overlap,
the chart had no quotes — re-run under Market Replay before blaming the port. ``gate_session``
computes this automatically and raises a ``WARNING`` counter. On the 2026-08-05 dump it read
``closer_to_tick_rule: False``, which is how we know replay supplied the book.

**This port is NOT verified.** The gate has run and it FAILS (see GATE RESULT). The numbers
in ``test_flux.py`` are self-consistency evidence except where a test reads the dump — those,
and only those, are parity evidence.
"""
from __future__ import annotations

import glob
import json
import math
import os
import sys
from dataclasses import dataclass, field

import numpy as np

# The Azimuth root, so this module imports standalone (`import flux`) and as a
# package member (`from bars import flux`) without depending on bars/__init__.py,
# which a sibling owns.
_AZIMUTH_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if _AZIMUTH_ROOT not in sys.path:
    sys.path.insert(0, _AZIMUTH_ROOT)

from engine.bars import Bars, bars_from_end_idx                      # noqa: E402
from engine.contract import (KIND_TRADE, TAPE_COLUMNS, Tape,          # noqa: E402
                             TapeContractError, validate, validate_sidecar)

__all__ = [
    "BARS_PERIOD_TYPE_ID", "BARTYPE_NAME", "CrossedPolicy", "CrossedReport",
    "FluxParams", "FluxResult", "apply_crossed_policy", "bar_params_from_dump_header",
    "bartag", "build", "build_session", "build_sessions", "discover", "gate_rows",
    "gate_session", "infer_tick_size", "load_flux_tape", "parse_bartag",
    "reference_rows_from_dump", "replace_params", "round_to_tick", "run_bartype_gate",
    "session_window", "sign_trade",
]

#: reserved Sentinel bars block 212200-212299; 212203 == SentinelFlux
BARS_PERIOD_TYPE_ID = 212203
BARTYPE_NAME = "SentinelFlux"
PORT_OF = "SentinelFlux_v1_0_0.cs"
BUILDER = "Azimuth.bars.flux/1.0.0"

# ImbMode in the C# (private enum, Volume is the shipped default)
MODE_TICK, MODE_VOLUME, MODE_DOLLAR = "tick", "volume", "dollar"
_MODES = (MODE_TICK, MODE_VOLUME, MODE_DOLLAR)


class CrossedPolicy:
    """§3.2 policies. Declared, applied in one place, counted."""

    HOLD_LAST_VALID = "hold-last-valid"
    WIDEN_TO_TOUCH = "widen-to-touch"
    DROP = "drop"
    RAW = "raw"
    ALL = (HOLD_LAST_VALID, WIDEN_TO_TOUCH, DROP, RAW)


class FluxError(Exception):
    """A Flux port precondition was violated. Never downgraded to a warning."""


def _fmt_num(x) -> str:
    s = ("%.10f" % float(x)).rstrip("0").rstrip(".")
    return s or "0"


# ─────────────────────────────────────────────────────────────── tick rounding
def _tick_decimals(tick_size: float) -> int:
    """Decimal places implied by a tick size (0.1 -> 1, 0.25 -> 2, 1 -> 0)."""
    s = ("%.10f" % float(tick_size)).rstrip("0")
    return len(s.split(".")[1]) if "." in s else 0


def round_to_tick(price: float, tick_size: float, mode: str = "half_even") -> float:
    """Reconstruction of ``MasterInstrument.RoundToTickSize``.

    ``half_even`` matches .NET ``Math.Round(double)``; ``half_away`` matches
    ``MidpointRounding.AwayFromZero``. The midpoint rule NinjaTrader actually uses is
    not observable from this side -- see KNOWN PARITY RISK in the module docstring.
    """
    if tick_size <= 0:
        raise FluxError("tick_size must be > 0, got %r" % (tick_size,))
    q = price / tick_size
    if mode == "half_even":
        r = float(round(q))          # ties-to-even on the double, as .NET Math.Round does
    elif mode == "half_away":
        r = math.floor(q + 0.5) if q >= 0 else math.ceil(q - 0.5)
    else:
        raise FluxError("rounding must be 'half_even' or 'half_away', got %r" % (mode,))
    return round(r * tick_size, _tick_decimals(tick_size))


def infer_tick_size(tape: Tape, *, max_rows: int = 400_000) -> float:
    """Smallest positive price granularity observable on the tape.

    Diagnostic only -- ``FluxParams.tick_size`` is never inferred silently, because a
    wrong tick size moves the price backstop and every rounded price at once.
    """
    px = np.concatenate([tape.bid[:max_rows], tape.ask[:max_rows]])
    px = px[np.isfinite(px)]
    if px.size < 2:
        raise FluxError("not enough finite prices to infer a tick size")
    d = np.abs(np.diff(np.unique(np.round(px, 8))))
    d = d[d > 1e-9]
    if d.size == 0:
        raise FluxError("tape carries a single price; tick size is not observable")
    return float(np.min(d))


# ────────────────────────────────────────────────────────────────── parameters
@dataclass(frozen=True)
class FluxParams:
    """The C#'s constants. Only ``flux_size`` is settable in NinjaTrader (see DOC-VS-CODE 1)."""

    flux_size: int = 8                    # BaseBarsPeriodValue, UI label "Flux Size"
    flux_ref_size: int = 8                # C# FluxRefSize
    mode: str = MODE_VOLUME               # ImbMode.Volume (VIB)
    use_quote_rule: bool = True           # Lee-Ready primary, tick-rule fallback
    atr_length: int = 14
    intensity_len: int = 50
    winsor_mult: float = 4.0
    divergence_frac: float = 0.5
    price_backstop_mult: float = 2.5
    force_stagnation_secs: float = 90.0
    max_ticks_per_bar: int = 5000
    tick_size: float = 0.1                # GC. NOT inferred -- see infer_tick_size.
    reset_on_new_session: bool = True     # bars.IsResetOnNewTradingDay
    #: MEASURED against NinjaTrader, not chosen -- see KNOWN PARITY RISK (RESOLVED).
    rounding: str = "half_away"
    volume_semantics: str = "nt"          # "nt" (faithful, triple-counts) | "once"

    def __post_init__(self) -> None:
        if self.flux_size <= 0:
            raise FluxError("flux_size must be > 0")
        if self.flux_ref_size <= 0:
            raise FluxError("flux_ref_size must be > 0")
        if self.mode not in _MODES:
            raise FluxError("mode must be one of %s, got %r" % (_MODES, self.mode))
        if self.volume_semantics not in ("nt", "once"):
            raise FluxError("volume_semantics must be 'nt' or 'once'")
        if self.tick_size <= 0:
            raise FluxError("tick_size must be > 0")
        round_to_tick(1.0, self.tick_size, self.rounding)  # validates `rounding`

    @property
    def flux_scale(self) -> float:
        """C# LatchConfig: BaseBarsPeriodValue / FluxRefSize, floored at 1.0 if <= 0."""
        s = float(self.flux_size) / float(self.flux_ref_size)
        return s if s > 0 else 1.0

    @property
    def atr_alpha(self) -> float:
        return 2.0 / (max(1, self.atr_length) + 1.0)

    @property
    def imb_alpha(self) -> float:
        return 2.0 / (max(1, self.intensity_len) + 1.0)

    def bartag(self) -> str:
        """``SentinelCore.BarTag``: '212203v8'. Value2 is 0, so there is no 'x' segment."""
        return "%dv%d" % (BARS_PERIOD_TYPE_ID, self.flux_size)

    def scope(self, instrument: str, lane: str = "") -> str:
        """``SentinelCore.ScopeOf`` / ``ComposeLane``: 'GC.212203v8' or 'GC.212203v8@AUD'."""
        s = "%s.%s" % (instrument, self.bartag())
        return "%s@%s" % (s, lane) if lane else s

    def bar_params(self) -> str:
        """The gate's ``bar_params`` PRECONDITION field — CANONICAL, and derivable from
        BOTH columns.

        It has to be, or the gate ABORTs on identity before it compares a single bar. The
        NinjaTrader side can only state what its dump header carries (``periodType``,
        ``periodValue``, ``baseValue``, ``tickSize``), so the shared string is exactly that
        — see ``bar_params_from_dump_header``. Everything else in the port's configuration
        is unsettable in NinjaTrader (DOC-VS-CODE 1) and rides along in ``builder``, a
        NOTED field, where it is recorded on every run and can never silently fail a gate.
        """
        return "flux size=%d base=%d tick=%s" % (
            self.flux_size, self.flux_size, _fmt_num(self.tick_size))

    def params_string(self) -> str:
        """The FULL settings string — audit provenance, not an identity key."""
        return ("fluxSize=%d;ref=%d;mode=%s;quoteRule=%s;atrLen=%d;intensityLen=%d;"
                "winsor=%g;divFrac=%g;priceMult=%g;stagnSecs=%g;maxTicks=%d;"
                "tickSize=%g;resetOnSession=%s;rounding=%s;vol=%s"
                % (self.flux_size, self.flux_ref_size, self.mode, self.use_quote_rule,
                   self.atr_length, self.intensity_len, self.winsor_mult,
                   self.divergence_frac, self.price_backstop_mult,
                   self.force_stagnation_secs, self.max_ticks_per_bar, self.tick_size,
                   self.reset_on_new_session, self.rounding, self.volume_semantics))


def bartag(flux_size: int = 8) -> str:
    return "%dv%d" % (BARS_PERIOD_TYPE_ID, flux_size)


def parse_bartag(tag: str) -> dict:
    """'GC.212203v8@AUD' / '212203v8' -> the decoded parts.

    The lane is separated out explicitly because it is NOT a bar parameter -- see
    THE PARAMETER in the module docstring.
    """
    s = str(tag)
    lane = ""
    if "@" in s:
        s, lane = s.rsplit("@", 1)
    inst = ""
    if "." in s:
        inst, _, s = s.rpartition(".")
    if "v" not in s:
        raise FluxError("not a bartag: %r" % (tag,))
    head, _, rest = s.partition("v")
    val2 = 0
    if "x" in rest:
        rest, _, v2 = rest.partition("x")
        val2 = int(v2)
    return {"instrument": inst, "bars_period_type": int(head), "value": int(rest),
            "value2": val2, "lane": lane,
            "is_flux": int(head) == BARS_PERIOD_TYPE_ID,
            "flux_size": int(rest) if int(head) == BARS_PERIOD_TYPE_ID else None}


# ────────────────────────────────────────────────────── the crux: trade signing
def sign_trade(price: float, bid: float, ask: float, prev_trade_px: float,
               last_sign: int, use_quote_rule: bool = True) -> int:
    """C# ``SignTrade`` -- Lee-Ready quote rule, tick-rule fallback.

    ``prev_trade_px`` is the C#'s ``rawClose`` AT CALL TIME, i.e. the PREVIOUS trade's
    price (continuous across bar closes -- see THE CRUX note 2). Returns the sign; the
    caller must persist it as the new ``last_sign`` carry, exactly as the C# writes
    ``lastTradeSign`` from BOTH branches.

    On a crossed book the ``>= ask`` test runs first, so a price in [ask, bid]
    classifies +1. That is reproduced, not corrected.
    """
    if use_quote_rule and ask > 0 and bid > 0:
        if price >= ask:
            return 1
        if price <= bid:
            return -1
    if price > prev_trade_px:
        return 1
    if price < prev_trade_px:
        return -1
    return last_sign


def _trade_weight(volume: int, price: float, mode: str) -> float:
    v = float(max(1, int(volume)))
    if mode == MODE_TICK:
        return 1.0
    if mode == MODE_DOLLAR:
        return v * price
    return v


# ───────────────────────────────────────────────── crossed quotes, in ONE place
@dataclass
class CrossedReport:
    """What the crossed-quote policy did. Carried onto every result; never silent."""

    policy: str
    n_rows: int
    n_crossed: int
    n_crossed_trade_rows: int
    n_repaired: int
    n_dropped: int
    n_leading_unrepairable: int          # crossed with no earlier valid quote to hold
    min_spread: float
    contract_valid: bool                 # does engine.contract.validate accept the result?
    #: the PRE-repair book, aligned to the tape actually returned. This is what NinjaTrader
    #: saw, and therefore the only book a faithful trade classifier may read.
    raw_bid: np.ndarray | None = field(default=None, repr=False)
    raw_ask: np.ndarray | None = field(default=None, repr=False)

    def to_dict(self) -> dict:
        return {k: v for k, v in self.__dict__.items() if not k.startswith("raw_")}

    def __str__(self) -> str:
        return ("crossed-quote policy %s: %d/%d rows crossed (%.4f%%), %d on trade rows, "
                "%d repaired, %d dropped, %d leading-unrepairable, min spread %.4g, "
                "contract_valid=%s"
                % (self.policy, self.n_crossed, self.n_rows,
                   100.0 * self.n_crossed / max(1, self.n_rows), self.n_crossed_trade_rows,
                   self.n_repaired, self.n_dropped, self.n_leading_unrepairable,
                   self.min_spread, self.contract_valid))


def apply_crossed_policy(bid: np.ndarray, ask: np.ndarray, kind: np.ndarray,
                         policy: str = CrossedPolicy.HOLD_LAST_VALID,
                         *, tick_size: float = 0.1, rounding: str = "half_even"):
    """THE ONE PLACE crossed quotes are handled (§3.2).

    Returns ``(bid, ask, keep_mask, report)``. ``keep_mask`` is all-True except under
    DROP. The arrays are copies; the inputs are never mutated.
    """
    if policy not in CrossedPolicy.ALL:
        raise FluxError("policy must be one of %s, got %r" % (CrossedPolicy.ALL, policy))
    n = int(bid.shape[0])
    crossed = ask < bid
    n_crossed = int(np.count_nonzero(crossed))
    n_crossed_trades = int(np.count_nonzero(crossed & (kind == KIND_TRADE)))
    min_spread = float(np.min(ask - bid)) if n else float("nan")
    keep = np.ones(n, dtype=bool)
    b, a = bid.copy(), ask.copy()
    repaired = dropped = leading = 0

    if n_crossed:
        if policy == CrossedPolicy.RAW:
            pass
        elif policy == CrossedPolicy.DROP:
            keep = ~crossed
            dropped = n_crossed
        elif policy == CrossedPolicy.WIDEN_TO_TOUCH:
            mid = (bid[crossed] + ask[crossed]) * 0.5
            touch = np.array([round_to_tick(m, tick_size, rounding) for m in mid])
            b[crossed] = touch
            a[crossed] = touch
            repaired = n_crossed
        else:  # HOLD_LAST_VALID
            valid = ~crossed
            src = np.where(valid, np.arange(n), -1)
            np.maximum.accumulate(src, out=src)
            lead = crossed & (src < 0)
            body = crossed & (src >= 0)
            if np.any(body):
                idx = src[body]
                b[body] = bid[idx]
                a[body] = ask[idx]
                repaired = int(np.count_nonzero(body))
            if np.any(lead):
                mid = (bid[lead] + ask[lead]) * 0.5
                touch = np.array([round_to_tick(m, tick_size, rounding) for m in mid])
                b[lead] = touch
                a[lead] = touch
                leading = int(np.count_nonzero(lead))
                repaired += leading

    still = bool(np.any(a[keep] < b[keep]))
    rep = CrossedReport(policy=policy, n_rows=n, n_crossed=n_crossed,
                        n_crossed_trade_rows=n_crossed_trades, n_repaired=repaired,
                        n_dropped=dropped, n_leading_unrepairable=leading,
                        min_spread=min_spread, contract_valid=not still,
                        raw_bid=bid[keep].copy(), raw_ask=ask[keep].copy())
    return b, a, keep, rep


def load_flux_tape(path: str, *, policy: str = CrossedPolicy.HOLD_LAST_VALID,
                   tick_size: float = 0.1, rounding: str = "half_even",
                   require_sidecar: bool = True):
    """Load one §3.1 session parquet, apply the crossed-quote policy, return ``(Tape, CrossedReport)``.

    ``engine.contract.load_session`` cannot be used on this tape: ``validate()`` REFUSES a
    crossed book outright, so every real GC 02-26 session raises there. That refusal is
    correct -- it is the contract doing its job -- and this loader is the declared
    §3.2 policy point that makes the tape admissible, with the count surfaced. Under
    ``RAW`` the tape is deliberately left crossed and ``validate`` is skipped;
    ``report.contract_valid`` says so.
    """
    import pyarrow.parquet as pq

    tbl = pq.read_table(path)
    have = set(tbl.column_names)
    missing = [c for c in TAPE_COLUMNS if c not in have]
    if missing:
        raise TapeContractError("%s: missing contract columns %s" % (path, missing))
    cols = {}
    for col, dt in TAPE_COLUMNS.items():
        arr = tbl.column(col).to_numpy(zero_copy_only=False)
        if dt.startswith("int") and arr.dtype.kind == "f":
            arr = np.nan_to_num(arr, nan=0.0)
        cols[col] = np.ascontiguousarray(arr, dtype=dt)

    meta_path = path.rsplit(".parquet", 1)[0] + ".meta.json"
    if os.path.exists(meta_path):
        with open(meta_path, encoding="utf-8") as fh:
            meta = json.load(fh)
        validate_sidecar(meta, meta_path)
    elif require_sidecar:
        raise TapeContractError(
            "no provenance sidecar next to %s; §3.1 makes it inadmissible to a gate" % path)
    else:
        meta = {"instrument": "", "contract": "", "source": "unknown"}

    b, a, keep, rep = apply_crossed_policy(cols["bid"], cols["ask"], cols["kind"], policy,
                                           tick_size=tick_size, rounding=rounding)
    cols["bid"], cols["ask"] = b, a
    if not keep.all():
        cols = {k: v[keep] for k, v in cols.items()}
    n = cols["ts_ms"].shape[0]
    t = Tape(cols["ts_ms"], cols["bid"], cols["ask"], cols["last"], cols["size"],
             cols["bid_size"], cols["ask_size"], cols["kind"],
             np.zeros(n, dtype=np.int32), [meta], str(meta.get("instrument", "")))
    if rep.contract_valid:
        validate(t)
    return t, rep


def discover(tape_root: str, instrument: str) -> list:
    d = os.path.join(tape_root, instrument)
    if not os.path.isdir(d):
        raise FluxError("no tape directory %s" % d)
    return sorted(glob.glob(os.path.join(d, "*.parquet")))


# ────────────────────────────────────────────────────────────────── the result
@dataclass
class FluxResult:
    """One run of the Flux clock over a tape.

    ``end_idx`` is THE SEAM (``engine.bars.bars_from_end_idx``): the tape row that closed
    each bar. Everything else is the NinjaTrader-faithful bar record the parity gate
    compares -- note that ``open/high/low/close`` here are the HEIKIN-ASHI-smoothed
    geometry NinjaTrader stores, which is deliberately NOT what ``bars_from_end_idx``
    recomputes from raw tape prices for the engine.
    """

    params: FluxParams
    tape: Tape = field(repr=False)
    end_idx: np.ndarray
    start_idx: np.ndarray                 # tape row that seeded the bar (AddBar)
    open: np.ndarray
    high: np.ndarray
    low: np.ndarray
    close: np.ndarray
    volume: np.ndarray
    ts_ms: np.ndarray                     # bar close stamp
    open_ts_ms: np.ndarray
    tick_count: np.ndarray                # C# nTicks at close
    session_id: np.ndarray
    session_date: list
    reason: list                          # imb | price | time | tick | open
    closed: np.ndarray
    theta: np.ndarray
    threshold: np.ndarray                 # 0.0 where warmup (C# logs MaxValue as 0.0)
    flow_dir: np.ndarray
    price_dir: np.ndarray
    pressure: np.ndarray
    cvd: np.ndarray
    atr_ticks: np.ndarray
    diverge: np.ndarray
    bar_in_session: np.ndarray            # C# barsThisSession, as LogFluxBar stamps it
    crossed: CrossedReport | None = None
    skipped_nonfinite_trades: int = 0

    @property
    def n(self) -> int:
        return int(self.end_idx.shape[0])

    def close_reason_counts(self) -> dict:
        out: dict = {}
        for r in self.reason:
            out[r] = out.get(r, 0) + 1
        return out

    def seam_end_idx(self) -> np.ndarray:
        """Strictly-increasing bar-close rows for ``bars_from_end_idx``.

        A trailing bar with zero accumulated trades (possible when a bar closes on the
        session's final trade and the seeded successor never receives one) carries no
        interval and is dropped HERE, loudly documented, rather than being smuggled
        past the seam's strictly-increasing check.
        """
        e = self.end_idx
        keep = np.ones(e.shape[0], dtype=bool)
        if e.shape[0] > 1:
            keep[1:] = np.diff(e) > 0
        out = e[keep]
        if out.size and np.any(np.diff(out) <= 0):
            raise FluxError("bar close rows are not strictly increasing after dedup")
        return out.astype(np.int64)

    def to_bars(self) -> Bars:
        """Hand the seam to the engine (spec §7.2: 'that is the whole interface')."""
        return bars_from_end_idx(self.tape, self.seam_end_idx())

    def gate_rows(self, *, instrument: str = "", closed_only: bool = True,
                  nt_compat: bool = True, session: str | None = None) -> list:
        """Rows for the ``bartype`` artefact spec (``gates/artefacts.py``).

        ``closed_only``  drop the trailing forming bar. ``SentinelBarDump`` is
                         ``Calculate.OnBarClose``, so NinjaTrader never writes a forming
                         bar; keeping ours would guarantee one unpaired row per session.
        ``nt_compat``    emit only what the NT dump can also state. ``open_ts_ms`` and
                         ``tick_count`` are dropped -- parity skips a field absent from
                         BOTH sides, and a field present on one side only is not evidence.
        ``session``      keep just this session_date.

        ⚠ ``bar_index`` is **0-based within the session**, matching ``ntdump.gate_rows``'s
        ``k - base``. It is NOT the C#'s 1-based ``barsThisSession`` (that stays on
        ``bar_in_session``, and is what the BrickLog stamps as ``n``). Two conventions
        would pair every row against its neighbour and report a total mismatch.
        """
        inst = instrument or self.tape.instrument or ""
        params = self.params.bar_params()
        builder = "%s %s" % (BUILDER, self.params.params_string())
        bartype = "%s %s" % (BARTYPE_NAME, self.params.bartag())
        rows = []
        base: dict = {}
        for i in range(self.n):
            if closed_only and not bool(self.closed[i]):
                continue
            sd = self.session_date[i]
            if session is not None and sd != session:
                continue
            if sd not in base:
                base[sd] = int(self.bar_in_session[i])
            row = {
                "session": sd,
                "bar_index": int(self.bar_in_session[i]) - base[sd],
                "instrument": inst,
                "bartype": bartype,
                "bar_params": params,
                "open": float(self.open[i]),
                "high": float(self.high[i]),
                "low": float(self.low[i]),
                "close": float(self.close[i]),
                "volume": int(self.volume[i]),
                "ts_ms": int(self.ts_ms[i]),
                "builder": builder,
            }
            if not nt_compat:
                row["open_ts_ms"] = int(self.open_ts_ms[i])
                row["tick_count"] = int(self.tick_count[i])
            rows.append(row)
        if not rows:
            raise FluxError("no gate rows produced (session=%r): an empty side is ABORT, "
                            "not PASS" % (session,))
        return rows


def gate_rows(result: FluxResult, **kw) -> list:
    return result.gate_rows(**kw)


# ──────────────────────────────────────── the REFERENCE column: SentinelBarDump
def bar_params_from_dump_header(header: dict) -> str:
    """Canonical ``bar_params`` from a ``bars.1`` dump header.

    ``ntdump.bar_params_of`` cannot be used: it raises unless ``periodType == 11`` (Renko).
    This is the Flux mapping, and it VALIDATES rather than assumes -- a dump from the wrong
    bar type, or from a chart whose Flux Size was not what you think, is caught here at
    PRECONDITION instead of surfacing as thousands of differing bars.
    """
    ptype = int(header.get("periodType", -1))
    if ptype != BARS_PERIOD_TYPE_ID:
        raise FluxError(
            "dump header periodType=%d is not SentinelFlux (%d). This dump came from a "
            "different bar type; gating it against Flux would compare two experiments."
            % (ptype, BARS_PERIOD_TYPE_ID))
    value = int(header["periodValue"])
    base = int(header["baseValue"])
    v2 = int(header.get("periodValue2", 0) or 0)
    if v2 != 0:
        raise FluxError("dump header periodValue2=%d; SentinelFlux forces it to 0 in "
                        "State.Configure, so this chart is not a stock Flux chart" % v2)
    if value != base:
        raise FluxError(
            "dump header periodValue=%d != baseValue=%d. State.Configure sets "
            "Value = BaseBarsPeriodValue, so these must agree; they do not, which means "
            "the chart was not configured by this bar type's own code path." % (value, base))
    return "flux size=%d base=%d tick=%s" % (value, base, _fmt_num(header["tickSize"]))


def reference_rows_from_dump(path: str, *, session_date: str, win_start_ms: int,
                             win_end_ms: int, first_ts_ms: int,
                             params: FluxParams | None = None):
    """NinjaTrader's own bars for one session, as ``bartype`` rows. Returns ``(rows, header,
    counters)``.

    Thin on purpose: the reading, the ISO->ms flooring, the ``CurrentBar``-rebuild dedup and
    the session renumbering all live in the sibling-owned ``ntdump`` and are NOT duplicated
    here -- two loaders would be a second place for the reference to drift.
    """
    ntdump = _sibling_module("ntdump")

    header, rows = ntdump.read_dump(path)
    params = params or FluxParams()
    nt_params = bar_params_from_dump_header(header)
    if nt_params != params.bar_params():
        raise FluxError(
            "the dump was built with %r but the port is configured %r. Two "
            "parameterisations are two experiments (§2 PRECONDITION)."
            % (nt_params, params.bar_params()))
    out, counters = ntdump.gate_rows(
        header, rows, session_date=session_date, win_start_ms=win_start_ms,
        win_end_ms=win_end_ms, first_ts_ms=first_ts_ms, bar_params=nt_params,
        bartype="%s %s" % (BARTYPE_NAME, params.bartag()))
    counters["nt_bars"] = len(out)
    return out, header, counters


def session_window(path: str) -> tuple:
    """``(session_date, win_start_ms, win_end_ms, first_ts_ms)`` from a tape sidecar.

    ⭐ The SESSION WINDOW (`session_window_utc_ms`, the ET trading-day definition both
    columns already agree on), not the tape's first and last row stamp. `ntdump.gate_rows`
    measured why: NinjaTrader's session-anchor bar can close up to 121 ms BEFORE our first
    tick, so selecting on the tape's own first row made a whole session ABORT on the
    arrival time of one tick. `first_ts_ms` is still returned -- `ntdump` uses it as an
    explicit BOUND on how early the anchor may close, not as a filter.
    """
    meta_path = path.rsplit(".parquet", 1)[0] + ".meta.json"
    if not os.path.exists(meta_path):
        raise FluxError("no sidecar next to %s; §3.1 makes it inadmissible to a gate" % path)
    with open(meta_path, encoding="utf-8") as fh:
        m = json.load(fh)
    win = m.get("session_window_utc_ms")
    if not win or len(win) != 2:
        raise FluxError(
            "sidecar %s carries no `session_window_utc_ms`. The session window is the "
            "session's DEFINITION; falling back to the tape's first/last row would make "
            "the gate depend on the arrival time of one tick (see ntdump.gate_rows)."
            % meta_path)
    return (str(m["session_date"]), int(win[0]), int(win[1]), int(m["first_ts_ms"]))


# ───────────────────────────────────────────────────────────────── the builder
def build(tape: Tape, params: FluxParams | None = None, *,
          crossed: CrossedReport | None = None, sign_book: str = "raw",
          include_open_bar: bool = True) -> FluxResult:
    """Run the SentinelFlux clock over a tape.

    ``sign_book`` decides which book ``sign_trade`` reads (see CROSSED QUOTES):
    ``"raw"`` (default) uses the PRE-repair quotes carried on ``crossed`` — the book
    NinjaTrader actually saw, and the only one a parity gate can be faithful to;
    ``"policy"`` uses the repaired book on the tape. Asking for ``"raw"`` when the
    report carries no raw book RAISES rather than silently falling back, because a
    silent fallback here is exactly the "silently different bar" §3.2 warns about.

    Only ``kind == KIND_TRADE`` rows are fed to the state machine: the C# declares
    ``BuiltFrom = BarsPeriodType.Tick``, so ``OnDataPoint`` fires once per TRADE with the
    prevailing bid/ask as arguments -- quote-only rows never reach it. A trade row with
    a non-finite ``last`` is not a data point NinjaTrader could have received; those are
    skipped and COUNTED on the result.

    A session boundary is ``tape.session_id`` changing. With
    ``params.reset_on_new_session`` (NinjaTrader's ``bars.IsResetOnNewTradingDay``) the C#
    takes the ``InitializeFirstBar`` branch, which resets EVERY EWMA, ``cvd``, the HA carry
    and ``warmup`` -- so each session starts cold and its first bar always closes on a
    physical backstop.
    """
    p = params or FluxParams()
    tick = p.tick_size
    rmode = p.rounding
    dec = _tick_decimals(tick)
    scale_inv = 1.0 / tick

    def rt(x: float) -> float:
        # ⛔ DIVIDE by the tick size. NEVER multiply by its reciprocal: `x / 0.1` and
        # `x * (1/0.1)` disagree at the last ULP on 35.5% of GC-range prices (measured),
        # and that ULP is exactly what decides a midpoint case.
        q = x / tick
        r = float(round(q)) if rmode == "half_even" else (
            math.floor(q + 0.5) if q >= 0 else math.ceil(q - 0.5))
        return round(r * tick, dec)

    ts_all = tape.ts_ms
    last_all = tape.last
    size_all = tape.size
    sess_all = tape.session_id

    if sign_book not in ("raw", "policy"):
        raise FluxError("sign_book must be 'raw' or 'policy', got %r" % (sign_book,))
    bid_all, ask_all = tape.bid, tape.ask
    if sign_book == "raw":
        have_raw = crossed is not None and crossed.raw_bid is not None
        if have_raw and crossed.raw_bid.shape[0] != len(tape):
            raise FluxError("the raw signing book (%d rows) does not align with the tape "
                            "(%d rows)" % (crossed.raw_bid.shape[0], len(tape)))
        if have_raw:
            bid_all, ask_all = crossed.raw_bid, crossed.raw_ask
        elif crossed is not None and crossed.n_crossed > 0:
            raise FluxError(
                "sign_book='raw' needs the pre-repair book, but the CrossedReport for a "
                "tape with %d crossed rows carries none. Signing from a repaired book "
                "would disagree with NinjaTrader by construction; pass "
                "sign_book='policy' explicitly if that is really what you want."
                % crossed.n_crossed)
        # crossed is None (a synthetic/already-clean tape): the tape book IS the raw book.

    rows = np.flatnonzero(tape.kind == KIND_TRADE)
    finite = np.isfinite(last_all[rows])
    skipped = int(np.count_nonzero(~finite))
    rows = rows[finite]
    if rows.size == 0:
        raise FluxError("tape carries no usable trade rows: an empty side is ABORT, not PASS")

    sess_dates = []
    for s in tape.sessions:
        sess_dates.append(str(s.get("session_date", s.get("contract", ""))))

    atr_alpha = p.atr_alpha
    imb_alpha = p.imb_alpha
    max_ticks = int(p.max_ticks_per_bar)
    stagn_ms = float(p.force_stagnation_secs) * 1000.0
    price_mult = float(p.price_backstop_mult)
    flux_scale = p.flux_scale
    winsor = float(p.winsor_mult)
    div_frac = float(p.divergence_frac)
    mode = p.mode
    quote_rule = p.use_quote_rule
    vol_nt = p.volume_semantics == "nt"

    # emitted bars
    o_end, o_start, o_o, o_h, o_l, o_c, o_v = [], [], [], [], [], [], []
    o_ts, o_ots, o_nt, o_sid, o_reason, o_closed = [], [], [], [], [], []
    o_theta, o_thr, o_fd, o_pd, o_pres, o_cvd, o_atrt, o_div, o_n = \
        [], [], [], [], [], [], [], [], []

    # forming-bar state (names mirror the C#)
    theta = buy_vol = sell_vol = 0.0
    n_ticks = 0
    raw_open = raw_high = raw_low = raw_close = 0.0
    birth_ms = 0
    atr_ema = imb_ewma = 0.0
    last_sign = 1
    prev_bar_close = 0.0
    ha_prev_open = ha_prev_close = 0.0
    cvd = 0.0
    bars_this_session = 0
    warmup = True
    cur_sess = -1
    have_bar = False
    o_last_row = int(rows[0])
    # current bar as NinjaTrader stores it
    b_open = b_high = b_low = b_close = 0.0
    b_vol = 0
    b_start = b_open_ts = 0
    b_ts = 0

    def emit(reason: str, closed: bool, thr_out: float, flow: int, pdir: int,
             diverge: int, end_row: int) -> None:
        tot = buy_vol + sell_vol
        o_end.append(end_row)
        o_start.append(b_start)
        o_o.append(b_open)
        o_h.append(b_high)
        o_l.append(b_low)
        o_c.append(b_close)
        o_v.append(b_vol)
        o_ts.append(b_ts)
        o_ots.append(b_open_ts)
        o_nt.append(n_ticks)
        o_sid.append(cur_sess)
        o_reason.append(reason)
        o_closed.append(closed)
        o_theta.append(theta)
        o_thr.append(thr_out)
        o_fd.append(flow)
        o_pd.append(pdir)
        o_pres.append(buy_vol / tot if tot > 0 else 0.5)
        o_cvd.append(cvd)
        o_atrt.append(atr_ema / tick if tick > 0 else 0.0)
        o_div.append(diverge)
        o_n.append(bars_this_session)

    for r in rows:
        r = int(r)
        px = float(last_all[r])
        vol = int(size_all[r])
        t_ms = int(ts_all[r])
        sid = int(sess_all[r])
        new_session = sid != cur_sess

        # ── InitializeFirstBar: first data point, or a session reset ──
        if not have_bar or (new_session and p.reset_on_new_session):
            if have_bar and include_open_bar:
                # NinjaTrader does NOT close the outgoing session's forming bar; it just
                # starts a new one. Emit it as it stood.
                emit("open", False, 0.0, int(np.sign(theta)),
                     int(np.sign(raw_close - raw_open)), 0, int(o_last_row))
            cur_sess = sid
            cvd = 0.0
            # C#: atrEma = max(|high - low|, tickSize). A Tick data point has
            # open == high == low == close, so this is always the tick size.
            atr_ema = tick
            imb_ewma = 0.0
            warmup = True
            last_sign = 1
            theta = buy_vol = sell_vol = 0.0
            n_ticks = 0
            raw_open = raw_high = raw_low = raw_close = px
            prev_bar_close = px
            birth_ms = t_ms
            ha_open = (px + px) * 0.5                       # (open + close) * 0.5
            ha_close = (px + px + px + px) * 0.25           # HaClose(o,h,l,c)
            b_open, b_high, b_low, b_close = rt(ha_open), rt(px), rt(px), rt(ha_close)
            b_vol = vol
            b_start = r
            b_open_ts = t_ms
            b_ts = t_ms
            ha_prev_open, ha_prev_close = ha_open, ha_close
            bars_this_session = 1
            have_bar = True
            o_last_row = r
            continue

        cur_sess = sid
        o_last_row = r

        # ── 1. sign + accumulate ──
        sign = sign_trade(px, float(bid_all[r]), float(ask_all[r]), raw_close,
                          last_sign, quote_rule)
        last_sign = sign
        w = _trade_weight(vol, px, mode)
        dw = sign * w
        theta += dw
        if sign > 0:
            buy_vol += w
        elif sign < 0:
            sell_vol += w
        cvd += dw
        n_ticks += 1

        if px > raw_high:
            raw_high = px
        if px < raw_low:
            raw_low = px
        raw_close = px

        # UpdateFormingBar
        ha_close = (raw_open + raw_high + raw_low + raw_close) * 0.25
        ha_open = b_open                                    # bars.GetOpen(last): ROUNDED
        b_high = rt(max(raw_high, max(ha_open, ha_close)))
        b_low = rt(min(raw_low, min(ha_open, ha_close)))
        b_close = rt(ha_close)
        b_ts = t_ms
        b_vol += vol                                        # UpdateBar ADDS

        # ── 2. close on the imbalance target or any physical backstop ──
        threshold = (float("inf") if (warmup or imb_ewma <= 0)
                     else max(1.0, flux_scale * imb_ewma))
        formed = n_ticks >= 2
        imb_hit = (not warmup) and formed and abs(theta) >= threshold
        price_hit = formed and abs(raw_close - raw_open) >= price_mult * max(atr_ema, tick)
        time_hit = (t_ms - birth_ms) >= stagn_ms and n_ticks > 0
        tick_hit = n_ticks >= max_ticks

        if not (imb_hit or price_hit or time_hit or tick_hit):
            continue

        reason = "imb" if imb_hit else ("price" if price_hit else
                                        ("time" if time_hit else "tick"))

        # ── CloseBar ──
        ha_close = (raw_open + raw_high + raw_low + raw_close) * 0.25
        ha_open = b_open
        b_high = rt(max(raw_high, max(ha_open, ha_close)))
        b_low = rt(min(raw_low, min(ha_open, ha_close)))
        b_close = rt(ha_close)
        b_ts = t_ms
        if vol_nt:
            b_vol += vol                                    # the second UpdateBar

        tr = max(raw_high - raw_low, abs(raw_high - prev_bar_close),
                 abs(raw_low - prev_bar_close))
        if tr <= 0:
            tr = tick
        atr_ema = tr if atr_ema <= 0 else atr_ema + atr_alpha * (tr - atr_ema)

        abs_theta = abs(theta)
        if abs_theta > 0:
            ewma_in = min(abs_theta, imb_ewma * winsor) if imb_ewma > 0 else abs_theta
            imb_ewma = ewma_in if imb_ewma <= 0 else imb_ewma + imb_alpha * (ewma_in - imb_ewma)
            warmup = False

        flow_dir = int(np.sign(theta))
        price_dir = int(np.sign(raw_close - raw_open))
        diverge = int(math.isfinite(threshold) and flow_dir != 0 and price_dir != 0
                      and flow_dir != price_dir and abs(theta) >= div_frac * threshold)

        emit(reason, True, 0.0 if not math.isfinite(threshold) else threshold,
             flow_dir, price_dir, diverge, r)

        # HA carry, then seed the next forming bar
        ha_prev_open = ha_open                              # the ROUNDED stored open
        ha_prev_close = ha_close                            # unrounded
        prev_bar_close = raw_close
        bars_this_session += 1
        next_ha_open = (ha_prev_open + ha_prev_close) * 0.5
        seed = rt(raw_close)
        b_open, b_high, b_low, b_close = rt(next_ha_open), seed, seed, seed
        b_vol = vol                                         # AddBar SETS
        b_start = r
        b_open_ts = t_ms
        b_ts = t_ms
        ha_prev_open = next_ha_open
        theta = buy_vol = sell_vol = 0.0
        n_ticks = 0
        raw_open = raw_high = raw_low = raw_close           # rawClose unchanged
        birth_ms = t_ms

    if have_bar and include_open_bar:
        emit("open", False, 0.0, int(np.sign(theta)),
             int(np.sign(raw_close - raw_open)), 0, int(o_last_row))

    n = len(o_end)
    if n == 0:
        raise FluxError("no bars produced: an empty side is ABORT, not PASS")

    sid_arr = np.asarray(o_sid, dtype=np.int32)
    return FluxResult(
        params=p, tape=tape,
        end_idx=np.asarray(o_end, dtype=np.int64),
        start_idx=np.asarray(o_start, dtype=np.int64),
        open=np.asarray(o_o, dtype=np.float64),
        high=np.asarray(o_h, dtype=np.float64),
        low=np.asarray(o_l, dtype=np.float64),
        close=np.asarray(o_c, dtype=np.float64),
        volume=np.asarray(o_v, dtype=np.int64),
        ts_ms=np.asarray(o_ts, dtype=np.int64),
        open_ts_ms=np.asarray(o_ots, dtype=np.int64),
        tick_count=np.asarray(o_nt, dtype=np.int64),
        session_id=sid_arr,
        session_date=[sess_dates[i] if 0 <= i < len(sess_dates) else str(i) for i in sid_arr],
        reason=list(o_reason),
        closed=np.asarray(o_closed, dtype=bool),
        theta=np.asarray(o_theta, dtype=np.float64),
        threshold=np.asarray(o_thr, dtype=np.float64),
        flow_dir=np.asarray(o_fd, dtype=np.int8),
        price_dir=np.asarray(o_pd, dtype=np.int8),
        pressure=np.asarray(o_pres, dtype=np.float64),
        cvd=np.asarray(o_cvd, dtype=np.float64),
        atr_ticks=np.asarray(o_atrt, dtype=np.float64),
        diverge=np.asarray(o_div, dtype=np.int8),
        bar_in_session=np.asarray(o_n, dtype=np.int64),
        crossed=crossed,
        skipped_nonfinite_trades=skipped,
    )


def build_session(path: str, params: FluxParams | None = None, *,
                  policy: str = CrossedPolicy.HOLD_LAST_VALID, sign_book: str = "raw",
                  require_sidecar: bool = True) -> FluxResult:
    """Load one session parquet under the declared crossed policy and run the clock."""
    p = params or FluxParams()
    tape, rep = load_flux_tape(path, policy=policy, tick_size=p.tick_size,
                               rounding=p.rounding, require_sidecar=require_sidecar)
    return build(tape, p, crossed=rep, sign_book=sign_book)


def build_sessions(paths, params: FluxParams | None = None, *,
                   policy: str = CrossedPolicy.HOLD_LAST_VALID, sign_book: str = "raw",
                   require_sidecar: bool = True) -> FluxResult:
    """Several sessions in one run, with the raw signing book stitched in lockstep.

    The raw book must be concatenated alongside the tape or ``sign_book="raw"`` would
    silently misalign quotes with trades -- which is why this exists instead of leaving
    callers to concat tapes by hand.
    """
    from engine.contract import concat

    paths = list(paths)
    if not paths:
        raise FluxError("no session files given: an empty side is ABORT, not PASS")
    p = params or FluxParams()
    tapes, reps = [], []
    for path in paths:
        t, rep = load_flux_tape(path, policy=policy, tick_size=p.tick_size,
                                rounding=p.rounding, require_sidecar=require_sidecar)
        tapes.append(t)
        reps.append(rep)
    tape = concat(tapes) if len(tapes) > 1 else tapes[0]
    merged = CrossedReport(
        policy=policy,
        n_rows=sum(r.n_rows for r in reps),
        n_crossed=sum(r.n_crossed for r in reps),
        n_crossed_trade_rows=sum(r.n_crossed_trade_rows for r in reps),
        n_repaired=sum(r.n_repaired for r in reps),
        n_dropped=sum(r.n_dropped for r in reps),
        n_leading_unrepairable=sum(r.n_leading_unrepairable for r in reps),
        min_spread=min(r.min_spread for r in reps),
        contract_valid=all(r.contract_valid for r in reps),
        raw_bid=np.concatenate([r.raw_bid for r in reps]),
        raw_ask=np.concatenate([r.raw_ask for r in reps]),
    )
    return build(tape, p, crossed=merged, sign_book=sign_book)


# ────────────────────────────────────────────────────────────────────── the gate
def run_bartype_gate(reference_rows, port_result: FluxResult, *,
                     ref_label: str = "NinjaTrader", cmp_label: str = "Azimuth",
                     instrument: str = "", tape_sha256: str = "",
                     ref_impl: str = "NinjaScript " + PORT_OF, ref_impl_ver: str = "1.0.0",
                     ref_meta: dict | None = None, check_identity: bool = True,
                     session: str | None = None):
    """Run the §2 ``bartype`` parity gate: NinjaTrader's bars vs this port's.

    ``reference_rows`` come from ``reference_rows_from_dump`` (SentinelBarDump). Get them
    with the recipe in THE GATE above; never fabricate them -- a PASS against an invented
    reference is worse than no gate at all.
    """
    from gates.artefacts import get
    from gates.loaders import rows_side
    from gates.parity import run_gate

    spec = get("bartype")
    inst = instrument or port_result.tape.instrument or ""
    meta = {"tape_sha256": tape_sha256, "instrument": inst,
            "session": session or (port_result.session_date[0]
                                   if port_result.session_date else ""),
            "bar_params": port_result.params.bar_params()}
    # provenance_meta: recorded, never compared -- a verdict that cannot name the two
    # implementations it blessed is not evidence (parity.py:157).
    ref_side = rows_side(ref_label, reference_rows,
                         dict(meta, impl=ref_impl, impl_ver=ref_impl_ver,
                              **(ref_meta or {})),
                         origin="NinjaTrader (%s)" % PORT_OF)
    cmp_side = rows_side(cmp_label,
                         port_result.gate_rows(instrument=inst, session=session),
                         dict(meta, impl=BUILDER, impl_ver="1.0.0"),
                         origin=BUILDER)
    return run_gate(spec, ref_side, cmp_side, check_identity=check_identity)


def gate_session(tape_path: str, dump_path: str, params: FluxParams | None = None, *,
                 policy: str = CrossedPolicy.HOLD_LAST_VALID, sign_book: str = "raw"):
    """END TO END: tape + a SentinelBarDump file -> a parity verdict for one session.

    This is the whole gate in one call, and it is UNRUN only because the dump does not
    exist yet -- see THE GATE for the ten-minute chart recipe that produces it.

    Returns ``(verdict, counters)``. ``counters`` carries the NT-side census (rebuilt bars,
    rows outside the window, realtime vs rebuild) plus a **quote-degradation check**: if
    NinjaTrader's bar count sits far closer to a tick-rule-only run than to the quote-rule
    run, the chart had no bid/ask and the verdict is about the DATA, not the port.
    """
    p = params or FluxParams()
    session_date, win_a, win_b, first_ms = session_window(tape_path)
    port = build_session(tape_path, p, policy=policy, sign_book=sign_book)
    ref, header, counters = reference_rows_from_dump(
        dump_path, session_date=session_date, win_start_ms=win_a, win_end_ms=win_b,
        first_ts_ms=first_ms, params=p)

    port_rows = port.gate_rows(instrument=header.get("inst", ""), session=session_date)
    counters["port_bars"] = len(port_rows)
    if p.use_quote_rule:
        tick_only = build_session(tape_path, replace_params(p, use_quote_rule=False),
                                  policy=policy, sign_book=sign_book)
        counters["port_bars_tick_rule_only"] = int(tick_only.n)
        counters["closer_to_tick_rule"] = bool(
            abs(counters["nt_bars"] - tick_only.n) < abs(counters["nt_bars"] - len(port_rows)))
        if counters["closer_to_tick_rule"]:
            counters["WARNING"] = (
                "NinjaTrader's bar count is closer to a TICK-RULE-ONLY run (%d) than to the "
                "quote-rule run (%d). The chart most likely had no bid/ask -- db\\tick is "
                "Last-only. Re-run under Market Replay before reading this verdict as a "
                "port defect." % (tick_only.n, len(port_rows)))

    verdict = run_bartype_gate(
        ref, port, instrument=str(header.get("inst", "")),
        tape_sha256=_tape_sha(tape_path), session=session_date,
        ref_impl="NinjaScript " + PORT_OF,
        ref_impl_ver=str(header.get("dumpVer", "?")),
        ref_meta={"nt_core_ver": header.get("coreVer"),
                  "trading_hours": header.get("tradingHours")})
    return verdict, counters


def replace_params(p: FluxParams, **kw) -> FluxParams:
    """A copy of `p` with fields replaced (FluxParams is frozen)."""
    import dataclasses
    return dataclasses.replace(p, **kw)


def _tape_sha(tape_path: str) -> str:
    meta_path = tape_path.rsplit(".parquet", 1)[0] + ".meta.json"
    with open(meta_path, encoding="utf-8") as fh:
        return str(json.load(fh).get("source_file_sha256", ""))


# ═══════════════════════════════════════════════════ the package contract
# `bars/__init__.py` (sibling-owned) defines a bar type as a callable
# `build(tape, **params) -> BarSeries`, and `bars/gate.py` drives every port through
# exactly that one signature. The native `build` above returns a `FluxResult` and takes
# no `instrument=`, so the adapter below is what gets REGISTERED; `build` stays exported
# for callers who want the full result (theta, threshold, reasons, pressure, cvd).
#
# ⚠ THREE THINGS THIS ADAPTER MUST NOT GET WRONG -- each is a silent, total-looking failure:
#
# 1. `bar_index` is **0-based within the session**, matching `ntdump.gate_rows`'s
#    `k - base`. The C#'s `barsThisSession` is 1-BASED and rides along in `notes`
#    only. Off by one here pairs every row against its neighbour and the gate reports a
#    wholesale mismatch that is really an indexing bug.
# 2. `is_partial` is `~closed`, i.e. the trailing forming bar of each session -- exactly
#    the bar `Calculate.OnBarClose` means NinjaTrader never writes. `gate_rows(...,
#    closed_only=True)` drops it; keeping it is a guaranteed unpaired row per session.
# 3. **Flux's OHLC is Heikin-Ashi-smoothed geometry, not tape prices** (see the C#'s
#    `HaClose`/`HaOpen` and `CloseBar`). `bars_from_end_idx` deliberately re-derives raw
#    price bars for the engine, so the adapter passes the port's own arrays into
#    `BarSeries` and `series.to_engine_bars` hands them through as keyword overrides.
#    Letting them be re-derived would silently gate the wrong numbers.
#
# ⭐ THE SIGNING BOOK IS RAW HERE, BY CONSTRUCTION. `tapeio.load_sessions` hands back the
# tape UNMODIFIED (it counts crossed rows and repairs nothing), which is precisely the
# pre-repair book `sign_trade` must read to be faithful to NinjaTrader. The adapter
# therefore passes `crossed=None, sign_book="raw"` and RE-COUNTS the crossed rows on the
# tape it was given, into `notes`. A run that was handed a pre-repaired book reports
# `crossed_rows=0` and is therefore distinguishable from one that was not.


def _sibling_module(name: str):
    """A sibling `bars` module, whether THIS module was imported as `bars.flux` or as a
    bare `flux`.

    Both spellings must resolve to the SAME module object or the package ends up with two
    `BarSeries` classes and two `FluxError`s -- `isinstance` then fails and an `except`
    silently misses. The relative import is tried FIRST for exactly that reason; the
    sys.path fallback exists only for a genuinely standalone `import flux`.
    """
    if __package__:
        import importlib
        return importlib.import_module("." + name, __package__)
    here = os.path.dirname(os.path.abspath(__file__))
    if here not in sys.path:
        sys.path.insert(0, here)
    import importlib
    return importlib.import_module(name)


def _series_module():
    return _sibling_module("series")


def _split_params(params: dict) -> tuple[FluxParams, str | None]:
    """`(FluxParams, instrument)`.

    `instrument` is a LABEL the gate driver passes to `build` and deliberately NOT to
    `params_str` -- it names the series, it does not change a bar. An unknown name is an
    error, never ignored: a silently dropped parameter is a silently different bar type.

    `tick_size` is NOT required (unlike tbars): it defaults to GC's 0.1 and it is IN
    `bar_params`, so a chart on a different grid ABORTs at the identity PRECONDITION
    against the dump header's own `tickSize` rather than comparing two experiments.
    """
    kw = dict(params)
    instrument = kw.pop("instrument", None)
    unknown = set(kw) - set(FluxParams.__dataclass_fields__)
    if unknown:
        raise FluxError(
            "unknown flux params %s (known: %s). A silently dropped parameter is a "
            "silently different bar type."
            % (sorted(unknown), ", ".join(sorted(FluxParams.__dataclass_fields__))))
    return FluxParams(**kw), (None if instrument is None else str(instrument))


def series_params_str(**params) -> str:
    """Canonical `bar_params` for the gate's identity PRECONDITION.

    Both columns must produce the SAME string: this one, and
    `bar_params_from_dump_header` off NinjaTrader's own dump header. Everything else in
    `FluxParams` is unsettable in NinjaTrader (DOC-VS-CODE 1) and rides in `notes`.
    """
    p, _ = _split_params(params)
    return p.bar_params()


def series_bartag(**params) -> str:
    """The bartag NinjaTrader would report, so the gate can FIND the reference dump
    (`SentinelBarDump` names its file `<stamp>__<inst>__<bartag>.jsonl`)."""
    p, _ = _split_params(params)
    return p.bartag()


def build_series(tape, **params):
    """The package entry point: `build(tape, **params) -> bars.series.BarSeries`."""
    p, inst = _split_params(params)
    r = build(tape, p, crossed=None, sign_book="raw")
    return _to_bar_series(r, instrument=inst)


def _to_bar_series(r: FluxResult, *, instrument: str | None = None):
    m = _series_module()
    n = r.n

    # bar_index restarts at 0 each session -- ntdump's convention, NOT barsThisSession's.
    bar_index = np.zeros(n, dtype=np.int64)
    counts: dict[int, int] = {}
    for k in range(n):
        s = int(r.session_id[k])
        bar_index[k] = counts.get(s, 0)
        counts[s] = bar_index[k] + 1

    tape = r.tape
    crossed_mask = tape.ask < tape.bid
    crossed_rows = int(np.count_nonzero(crossed_mask))
    crossed_trades = int(np.count_nonzero(crossed_mask & (tape.kind == KIND_TRADE)))
    dup = int(np.count_nonzero(np.diff(r.end_idx) == 0)) if n > 1 else 0

    return m.BarSeries(
        # HA-smoothed geometry, passed through -- never re-derived from tape prices.
        open=r.open, high=r.high, low=r.low, close=r.close,
        volume=r.volume, ts_ms=r.ts_ms, open_ts_ms=r.open_ts_ms,
        end_idx=r.end_idx, start_idx=r.start_idx, tick_count=r.tick_count,
        session_id=r.session_id, bar_index=bar_index,
        is_partial=~r.closed.astype(bool),
        bartype="flux", bar_params=r.params.bar_params(),
        instrument=(instrument if instrument is not None
                    else getattr(tape, "instrument", "")),
        notes={
            # §3.2 -- a run that discarded or repaired rows must never look identical to
            # one that did not. Counted on the tape actually handed to the classifier.
            "crossed_rows": crossed_rows,
            "crossed_trade_rows": crossed_trades,
            "crossed_policy": (r.crossed.policy if r.crossed else "none (tape as given)"),
            "sign_book": "raw",
            "skipped_nonfinite_trades": int(r.skipped_nonfinite_trades),
            "close_reasons": r.close_reason_counts(),
            "duplicate_end_idx": dup,
            "quote_rule": bool(r.params.use_quote_rule),
            "tick_size": float(r.params.tick_size),
            "flux_scale": float(r.params.flux_scale),
            "rounding": r.params.rounding,
            "volume_semantics": r.params.volume_semantics,
            "full_params": r.params.params_string(),
            # start_idx is the SEED row (NinjaTrader's AddBar), whose volume NT counts in
            # this bar but whose trade is not in `tick_count` -- the triple-count quirk.
            "start_idx_is_seed_row": True,
            "bar_index_base": 0,
            "bars_this_session_base": 1,
        },
        tape=tape,
    )


# --- registry wiring -------------------------------------------------------
# Without this the module imports cleanly, does NOT appear in `bars.kinds()`,
# and `bars.gate --bartype flux` cannot resolve it -- i.e. it looks absent
# rather than broken. Registration is the difference.

#: True once this module has joined `bars`' registry. False under a standalone
#: `import flux`, where there is no package to join - recorded, never swallowed.
#: Mirrors tbars.py so the two behave identically under both import styles.
REGISTERED = False
REGISTRATION_SKIPPED: str = ""
try:
    from . import register as _register
except ImportError as _exc:                       # standalone import; no registry exists
    REGISTRATION_SKIPPED = "%s: %s" % (type(_exc).__name__, _exc)
else:
    _register(
        "flux", build_series,
        params_str=series_params_str,
        nt_period_type=BARS_PERIOD_TYPE_ID,
        bartag=series_bartag,
        doc="SentinelFlux - order-flow-imbalance clock (SentinelFlux_v1_0_0.cs). "
            "`flux_size` is the chart's Flux Size; 8 -> the corpus's 212203v8. It is a "
            "SCALE on E[|theta|] (fluxScale = size/8), NOT a threshold. The `@AUD` suffix "
            "in the corpus is a SentinelCore recording LANE, not a bar parameter -- "
            "212203v8 and 212203v8@AUD are the SAME bars. "
            "NEEDS BID/ASK: it classifies every trade against the book (Lee-Ready), so a "
            "quote-less historical rebuild silently degrades to the tick rule and builds "
            "materially different bars -- gate it under MARKET REPLAY.",
    )
    REGISTERED = True
