---
layout: sentinel-ref
title: "flux.py"
blurb: "Azimuth (Python) · unversioned · 1560 lines"
---

# flux.py

> `Sentinel/Azimuth/bars/flux.py`

| | |
|---|---|
| **Family** | Azimuth (Python) |
| **Version** | — |
| **Size** | 1560 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | [SENTINEL_AZIMUTH_SPEC](../../SENTINEL_AZIMUTH_SPEC.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
SentinelFlux — order-flow IMBALANCE bars, the Python column (Azimuth §1, Phase 4).

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
computes this automatically and raises a ``WARNING`` counter. On the 2026
```

