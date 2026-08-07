"""Tests for the SentinelFlux Python port, over REAL §3.1 tape.

Run:  C:\\ntbv\\Scripts\\python.exe -m pytest bars/test_flux.py -q
 or:  C:\\ntbv\\Scripts\\python.exe bars/test_flux.py        (no pytest needed)

⚠ READ THIS BEFORE QUOTING A NUMBER FROM HERE. Everything below is SELF-CONSISTENCY
evidence: it proves the port does what ``SentinelFlux_v1_0_0.cs`` says it does, on real
GC tape, deterministically. It is **not parity evidence.** No NinjaTrader reference bars
exist for any session this tape covers -- see THE GATE in ``flux.py``'s docstring for the
measurement that establishes that. ``test_gate_is_wired_and_can_fail`` proves the parity
gate is wired and CAN fail; it does not and cannot prove the port is correct.
"""
from __future__ import annotations

import math
import os
import sys

import numpy as np

_HERE = os.path.dirname(os.path.abspath(__file__))
_ROOT = os.path.dirname(_HERE)
for _p in (_HERE, _ROOT):
    if _p not in sys.path:
        sys.path.insert(0, _p)

# ⚠ IMPORT THROUGH THE PACKAGE, ALWAYS. `import flux` and `bars.flux` are two distinct
# module objects with two distinct `FluxError`s and two distinct `BarSeries`es, so
# `isinstance` fails and `except FluxError` silently misses -- which is how a test
# passes while testing a different class than the one the driver uses. The bars dir
# stays on sys.path only so `flux.py`'s own standalone path is still exercised.
import bars  # noqa: E402
from bars import flux, gate, ntdump, series as bars_series, tapeio  # noqa: E402
from bars.series import BarSeries  # noqa: E402
from engine.contract import KIND_TRADE, TapeContractError, load_session, validate  # noqa: E402

TAPE_DIR = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                        "tape", "GC 02-26")
SESSION = os.path.join(TAPE_DIR, "2025-12-09.parquet")
SESSION_B = os.path.join(TAPE_DIR, "2025-12-10.parquet")
#: the worst session for crossed quotes: 43 crossed TRADE rows, 34x the next worst.
SESSION_CROSSED = os.path.join(TAPE_DIR, "2025-12-31.parquet")

_CACHE: dict = {}


def _session(path=SESSION, policy=flux.CrossedPolicy.HOLD_LAST_VALID, params=None,
             sign_book="raw"):
    key = (path, policy, sign_book, params.params_string() if params else "")
    if key not in _CACHE:
        _CACHE[key] = flux.build_session(path, params, policy=policy, sign_book=sign_book)
    return _CACHE[key]


# ════════════════════════════════════════════════ 1 · the parameter decode
def test_bartag_decode_is_evidence_not_a_guess():
    """'212203v8' == SentinelFlux, Flux Size 8, fluxScale 1.0, Value2 absent because 0."""
    p = flux.FluxParams(flux_size=8)
    assert p.bartag() == "212203v8"
    assert p.flux_scale == 1.0
    assert flux.BARS_PERIOD_TYPE_ID == 212203

    d = flux.parse_bartag("212203v8")
    assert d["bars_period_type"] == 212203 and d["value"] == 8
    assert d["value2"] == 0, "State.Configure sets BarsPeriod.Value2 = 0, so BarTag emits no 'x'"
    assert d["flux_size"] == 8

    # fluxScale = FluxSize / FluxRefSize(8): the one knob, a MULTIPLIER on E[|theta|].
    assert flux.FluxParams(flux_size=16).flux_scale == 2.0
    assert flux.FluxParams(flux_size=4).flux_scale == 0.5


def test_at_aud_is_a_council_lane_not_a_bar_parameter():
    """'@AUD' comes from SentinelCore.ComposeLane; it cannot change a bar boundary."""
    bare = flux.parse_bartag("GC.212203v8")
    laned = flux.parse_bartag("GC.212203v8@AUD")
    assert laned["lane"] == "AUD" and bare["lane"] == ""
    for k in ("bars_period_type", "value", "value2", "flux_size", "instrument"):
        assert bare[k] == laned[k], "the lane must not leak into any bar parameter"
    assert flux.FluxParams().scope("GC", "AUD") == "GC.212203v8@AUD"

    # And the lane's config really is a RECORDING lane, not a bar setting.
    lane_conf = os.path.join(os.path.dirname(TAPE_DIR), "..", "..", "Models", "GC",
                             "212203v8@AUD", "Lane.conf")
    if os.path.isfile(lane_conf):
        txt = open(lane_conf, encoding="utf-8", errors="replace").read().lower()
        assert "floor" in txt and "deadband" in txt
        assert "flux size" not in txt and "fluxsize" not in txt


# ════════════════════════════════════════════════ 2 · THE CRUX: trade signing
def test_quote_rule_is_inclusive_at_the_touch():
    """>= ask is a BUY and <= bid is a SELL -- not > / <. Most futures prints are one."""
    assert flux.sign_trade(100.2, 100.0, 100.2, 100.1, 1) == 1     # at the ask
    assert flux.sign_trade(100.3, 100.0, 100.2, 100.1, 1) == 1     # through the ask
    assert flux.sign_trade(100.0, 100.0, 100.2, 100.1, 1) == -1    # at the bid
    assert flux.sign_trade(99.9, 100.0, 100.2, 100.1, 1) == -1     # through the bid


def test_tick_rule_fallback_compares_to_the_previous_trade_and_carries():
    """Inside the spread -> sign(price - rawClose), carrying the prior sign on equality."""
    assert flux.sign_trade(100.15, 100.0, 100.3, 100.10, -1) == 1    # up-tick
    assert flux.sign_trade(100.05, 100.0, 100.3, 100.10, 1) == -1    # down-tick
    assert flux.sign_trade(100.10, 100.0, 100.3, 100.10, 1) == 1     # zero-tick, carry +1
    assert flux.sign_trade(100.10, 100.0, 100.3, 100.10, -1) == -1   # zero-tick, carry -1


def test_quote_rule_is_skipped_when_a_side_is_absent():
    """C#: `useQuoteRule && ask > 0 && bid > 0`. Missing quotes -> pure tick rule."""
    assert flux.sign_trade(100.5, 0.0, 0.0, 100.4, -1) == 1
    assert flux.sign_trade(100.3, 0.0, 0.0, 100.4, 1) == -1
    # and the switch itself
    assert flux.sign_trade(100.2, 100.0, 100.2, 100.4, 1, use_quote_rule=False) == -1


def test_crossed_book_classifies_buy_because_the_ask_test_runs_first():
    """bid > ask: any price in [ask, bid] hits `price >= ask` first -> +1.

    This is reproduced, not corrected. It is what NinjaTrader ran.
    """
    bid, ask = 100.5, 100.0                     # crossed by 0.5
    for px in (100.0, 100.2, 100.5):
        assert flux.sign_trade(px, bid, ask, 99.0, -1) == 1, px
    # below the (crossed) ask still resolves to the bid test
    assert flux.sign_trade(99.9, bid, ask, 99.0, -1) == -1


def test_trade_weight_modes():
    assert flux._trade_weight(7, 100.0, flux.MODE_VOLUME) == 7.0
    assert flux._trade_weight(7, 100.0, flux.MODE_TICK) == 1.0
    assert flux._trade_weight(7, 100.0, flux.MODE_DOLLAR) == 700.0
    assert flux._trade_weight(0, 100.0, flux.MODE_VOLUME) == 1.0, "C#: Math.Max(1L, volume)"


# ════════════════════════════════════════════════ 3 · crossed quotes (§3.2)
def test_engine_loader_refuses_the_real_tape_which_is_why_this_module_has_its_own():
    """engine.contract.load_session REFUSES every real GC 02-26 session (crossed book).

    The refusal is the contract working. It is also the reason a §3.2 policy point has
    to exist somewhere, and it exists in exactly one place: apply_crossed_policy.
    """
    try:
        load_session(SESSION)
        raise AssertionError("expected a TapeContractError for the crossed book")
    except TapeContractError as e:
        assert "crossed book" in str(e)


def test_crossed_policy_is_measured_and_surfaced():
    r = _session()
    c = r.crossed
    assert c is not None, "a repaired row must never be silent"
    assert c.policy == flux.CrossedPolicy.HOLD_LAST_VALID
    assert c.n_rows == 1_561_749
    assert c.n_crossed == 140, "measured on this tape file"
    assert c.n_crossed_trade_rows == 1, (
        "crossed rows are overwhelmingly QUOTE rows, which a Tick-built BarsType never "
        "sees -- but see SESSION_CROSSED, where 43 of them land on trades")
    assert c.raw_bid is not None and c.raw_bid.shape[0] == c.n_rows
    assert c.n_repaired == c.n_crossed and c.n_dropped == 0
    assert c.n_leading_unrepairable == 2, "rows 0 and 1 have no earlier valid quote to hold"
    assert c.min_spread < 0 and c.contract_valid is True


def test_crossed_rate_contradicts_the_spec_shell_figure():
    """§3.2 records 0.05% / 716 rows / 'concentrated at session start' for 2025-12-09.

    Re-measured on the current tape file that is 140 rows / 0.0090%, and they are NOT
    concentrated at the open. The tape-track figure (0.006-0.011%) is the one that
    survives; the 5x disagreement is a stale number, not an uncharacterised population.
    """
    import pyarrow.parquet as pq
    t = pq.read_table(SESSION)
    bid = t.column("bid").to_numpy(zero_copy_only=False)
    ask = t.column("ask").to_numpy(zero_copy_only=False)
    ts = t.column("ts_ms").to_numpy(zero_copy_only=False)
    idx = np.flatnonzero(ask < bid)
    assert idx.size == 140
    assert 0.006 <= 100.0 * idx.size / bid.size <= 0.011
    assert idx.size != 716
    within_60s = int(np.count_nonzero(ts[idx] - ts[0] < 60_000))
    assert within_60s <= 5, "not a session-start cluster: %d of %d" % (within_60s, idx.size)


def test_raw_policy_leaves_the_tape_crossed_and_says_so():
    tape, rep = flux.load_flux_tape(SESSION, policy=flux.CrossedPolicy.RAW)
    assert rep.contract_valid is False, "RAW must not claim contract validity"
    assert rep.n_repaired == 0 and rep.n_dropped == 0
    try:
        validate(tape)
        raise AssertionError("RAW tape should still fail the §3.1 validator")
    except TapeContractError:
        pass


def test_every_policy_produces_an_uncrossed_book_except_raw():
    for pol in (flux.CrossedPolicy.HOLD_LAST_VALID, flux.CrossedPolicy.WIDEN_TO_TOUCH,
                flux.CrossedPolicy.DROP):
        tape, rep = flux.load_flux_tape(SESSION, policy=pol)
        assert rep.contract_valid is True, pol
        validate(tape)                       # raises if the policy left anything crossed
        if pol == flux.CrossedPolicy.DROP:
            assert len(tape) == rep.n_rows - rep.n_crossed
            assert rep.n_dropped == 140
        else:
            assert len(tape) == rep.n_rows


def test_repairing_the_book_is_free_when_signing_stays_raw():
    """The SPLIT, on the worst session in the corpus for crossed quotes.

    2025-12-31 carries 43 crossed TRADE rows. With `sign_book="raw"` the classifier reads
    the pre-repair quotes, so the book repair -- whichever one -- cannot move a boundary.
    """
    hold = _session(SESSION_CROSSED, flux.CrossedPolicy.HOLD_LAST_VALID)
    raw = _session(SESSION_CROSSED, flux.CrossedPolicy.RAW)
    wide = _session(SESSION_CROSSED, flux.CrossedPolicy.WIDEN_TO_TOUCH)
    assert hold.crossed.n_crossed_trade_rows == 43
    assert hold.n == raw.n == wide.n
    assert np.array_equal(hold.end_idx, raw.end_idx)
    assert np.array_equal(hold.close, raw.close)
    assert np.array_equal(hold.end_idx, wide.end_idx)
    # and the same on a quiet session
    a, b = _session(SESSION, flux.CrossedPolicy.HOLD_LAST_VALID), _session(
        SESSION, flux.CrossedPolicy.RAW)
    assert a.n == b.n == 3261 and np.array_equal(a.end_idx, b.end_idx)


def test_repairing_the_book_DOES_move_bars_if_the_classifier_reads_it():
    """Why the split exists. `sign_book="policy"` is the plausible wrong choice.

    Same session, same policy, only the classifier's book changes: the faithful 3395
    bars become 3397. §3.2's warning is literal, and this is the number that makes it so.
    """
    faithful = _session(SESSION_CROSSED, flux.CrossedPolicy.HOLD_LAST_VALID,
                        sign_book="raw")
    repaired = _session(SESSION_CROSSED, flux.CrossedPolicy.HOLD_LAST_VALID,
                        sign_book="policy")
    assert faithful.n == 3395
    assert repaired.n == 3397
    assert faithful.n != repaired.n, "if these ever agree, re-derive the policy"


def test_drop_deletes_real_trades_and_is_therefore_never_the_default():
    """DROP removes the crossed TRADE rows too, so theta, E[|theta|] and every later
    boundary shift. 3395 bars become 3424 from deleting 43 trades."""
    ref = _session(SESSION_CROSSED, flux.CrossedPolicy.HOLD_LAST_VALID)
    drop = _session(SESSION_CROSSED, flux.CrossedPolicy.DROP)
    assert drop.crossed.n_dropped == 255 and drop.crossed.n_crossed_trade_rows == 43
    assert ref.n == 3395 and drop.n == 3424


def test_sign_book_raw_refuses_to_fall_back_silently():
    """Asking for the raw book when it is absent must RAISE, not quietly use the repaired one."""
    tape, rep = flux.load_flux_tape(SESSION)
    stripped = flux.CrossedReport(**rep.to_dict())          # raw_bid/raw_ask dropped
    assert stripped.raw_bid is None and stripped.n_crossed == 140
    try:
        flux.build(tape, flux.FluxParams(), crossed=stripped, sign_book="raw")
        raise AssertionError("expected FluxError: no pre-repair book to sign from")
    except flux.FluxError as e:
        assert "pre-repair book" in str(e)
    # misaligned raw book is caught too
    bad = flux.CrossedReport(**rep.to_dict(), raw_bid=rep.raw_bid[:10],
                             raw_ask=rep.raw_ask[:10])
    try:
        flux.build(tape, flux.FluxParams(), crossed=bad, sign_book="raw")
        raise AssertionError("expected FluxError for a misaligned signing book")
    except flux.FluxError as e:
        assert "does not align" in str(e)


# ════════════════════════════════════════════════ 4 · a full session, end to end
def test_full_session_end_to_end():
    r = _session()
    assert r.n == 3261, "2025-12-09, GC, Flux Size 8, hold-last-valid"
    assert r.skipped_nonfinite_trades == 0

    counts = r.close_reason_counts()
    assert set(counts) <= {"imb", "price", "time", "tick", "open"}
    assert counts.get("open", 0) == 1, "exactly one unclosed forming bar at session end"
    # The whole point of the same-day threshold hotfix: imbalance must be the PRIMARY
    # close reason. Before it, every realtime bar closed on the 90 s time backstop.
    closed = r.n - counts.get("open", 0)
    assert counts["imb"] / closed > 0.80, counts
    assert counts.get("tick", 0) == 0, "the 5000-tick backstop should never be reached"

    assert np.all(np.diff(r.end_idx) >= 0)
    assert np.all(r.high >= r.low)
    assert np.all(r.high >= r.close) and np.all(r.low <= r.close)
    assert np.all(r.high >= r.open) and np.all(r.low <= r.open)
    assert np.all(r.volume > 0)
    assert np.all(np.diff(r.ts_ms) >= 0)
    assert np.all(r.ts_ms >= r.open_ts_ms)
    assert np.all(r.bar_in_session == np.arange(1, r.n + 1)), "1-based, matches BrickLog `n`"
    assert np.all(r.pressure >= 0.0) and np.all(r.pressure <= 1.0)
    assert set(np.unique(r.flow_dir)) <= {-1, 0, 1}


def test_warmup_first_bar_cannot_close_on_imbalance():
    """E[|theta|] is unseeded at session start, so theta* is +inf until the first close."""
    r = _session()
    assert r.reason[0] in ("price", "time", "tick"), r.reason[0]
    assert r.threshold[0] == 0.0, "C# logs double.MaxValue as 0.0"
    assert r.threshold[1] > 0.0, "seeded by the first close"
    assert np.all(r.threshold[r.threshold > 0] >= 1.0), "theta* = max(1.0, ...)"


def test_theta_star_is_self_consistent_not_runaway():
    """The failure mode the design exists to beat: theta* must not explode or collapse."""
    r = _session()
    thr = r.threshold[r.threshold > 0]
    assert thr.size > 3000
    # E[|theta|] EWMAs toward realised |theta|, so the two must track each other.
    med_thr = float(np.median(thr))
    med_theta = float(np.median(np.abs(r.theta[r.threshold > 0])))
    assert 0.5 < med_thr / med_theta < 2.0, (med_thr, med_theta)
    assert float(np.max(thr)) / med_thr < 20.0, "winsorization should bound the excursions"


def test_winsorization_bounds_a_block_trade():
    """WinsorMult=4: one 2000-lot print must not redefine 'typical' (the live incident)."""
    r = _session()
    thr = r.threshold[r.threshold > 0]
    ratio = np.diff(thr) / thr[:-1]
    # one bar can move E[|theta|] by at most alpha*(winsor-1) = 2/51 * 3 ~= 0.1176
    alpha = flux.FluxParams().imb_alpha
    assert float(np.max(ratio)) <= alpha * (4.0 - 1.0) + 1e-9, float(np.max(ratio))


def test_flux_size_is_a_monotone_coarseness_knob():
    """fluxScale multiplies theta*, so a bigger Flux Size must mean fewer, bigger bars."""
    n = {}
    for size in (4, 8, 16):
        r = _session(params=flux.FluxParams(flux_size=size))
        n[size] = r.n
    assert n[4] > n[8] > n[16], n
    r8 = _session(params=flux.FluxParams(flux_size=8))
    r16 = _session(params=flux.FluxParams(flux_size=16))
    assert float(np.median(r16.tick_count)) > float(np.median(r8.tick_count))


def test_determinism():
    """Same tape, same params, same bars -- the property that makes a boundary diff mean something."""
    a = flux.build_session(SESSION, flux.FluxParams())
    b = flux.build_session(SESSION, flux.FluxParams())
    assert np.array_equal(a.end_idx, b.end_idx)
    assert np.array_equal(a.close, b.close)
    assert np.array_equal(a.volume, b.volume)


def test_volume_reproduces_the_nt_updatebar_triple_count():
    """The closing tick's volume lands in the closed bar twice and the next bar once.

    Faithful to `@VolumeBarsType.cs:52`, which proves UpdateBar ADDS. `once` semantics
    exist only so the size of the quirk is measurable.
    """
    nt = _session(params=flux.FluxParams(volume_semantics="nt"))
    once = _session(params=flux.FluxParams(volume_semantics="once"))
    assert np.array_equal(nt.end_idx, once.end_idx), "volume must not move a boundary"
    assert int(nt.volume.sum()) > int(once.volume.sum())
    # exactly one extra copy of each closing tick's volume, over the closed bars
    closed = nt.closed
    extra = int(nt.volume[closed].sum() - once.volume[closed].sum())
    seed_vol = nt.tape.size[nt.end_idx[closed]].astype(np.int64).sum()
    assert extra == int(seed_vol), (extra, int(seed_vol))


def test_multi_session_resets_every_ewma():
    """IsResetOnNewTradingDay -> InitializeFirstBar -> a cold start each session."""
    r = flux.build_sessions([SESSION, SESSION_B])
    assert r.crossed.n_crossed == 140 + 189
    assert set(np.unique(r.session_id)) == {0, 1}
    for sid in (0, 1):
        m = r.session_id == sid
        first = int(np.flatnonzero(m)[0])
        assert r.threshold[first] == 0.0, "session %d starts in warmup" % sid
        assert r.reason[first] in ("price", "time", "tick")
        assert r.bar_in_session[first] == 1
    assert len(set(r.session_date)) == 2


# ════════════════════════════════════════════════ 5 · the engine seam
def test_seam_hands_bars_to_the_engine_unchanged():
    """spec §7.2: a ported bar type only has to say which tape row closed each bar."""
    r = _session()
    e = r.seam_end_idx()
    assert e.dtype == np.int64
    assert np.all(np.diff(e) > 0), "bars_from_end_idx requires strictly increasing"
    assert int(e[-1]) < len(r.tape)

    b = r.to_bars()
    assert b.n in (e.size, e.size + 1), "_from_breaks appends the tape's final row"
    assert b.iv_start is not None and b.iv_bid_min is not None
    assert np.all(b.high >= b.low)
    # The engine recomputes RAW price geometry; NinjaTrader stores HA-smoothed bars.
    # They are different by construction and the gate compares the NT-faithful set.
    assert not np.allclose(b.close[:r.n - 1], r.close[:r.n - 1])


def test_only_trade_rows_drive_the_clock():
    """BuiltFrom = Tick: OnDataPoint fires once per TRADE, never on a quote-only row."""
    r = _session()
    assert np.all(r.tape.kind[r.end_idx[r.closed]] == KIND_TRADE)
    n_trades = int(np.count_nonzero(r.tape.kind == KIND_TRADE))
    assert int(r.tick_count.sum()) < n_trades


# ════════════════════════════════════════════════ 6 · rounding
def test_round_to_tick_modes_differ_exactly_where_it_matters():
    assert flux.round_to_tick(4220.05, 0.1, "half_away") == 4220.1
    assert flux.round_to_tick(4220.04, 0.1, "half_even") == 4220.0
    assert flux.round_to_tick(4220.06, 0.1, "half_even") == 4220.1
    assert flux.round_to_tick(-4220.05, 0.1, "half_away") == -4220.1
    assert flux._tick_decimals(0.1) == 1 and flux._tick_decimals(0.25) == 2
    assert flux._tick_decimals(1.0) == 0


def test_rounding_mode_can_move_a_bar_and_is_therefore_a_real_parity_risk():
    """Named so nobody dismisses the open question in flux.py as pedantry."""
    a = _session(params=flux.FluxParams(rounding="half_even"))
    b = _session(params=flux.FluxParams(rounding="half_away"))
    diff = int(np.count_nonzero(a.close != b.close)) if a.n == b.n else -1
    assert diff != 0, ("if the two modes agreed everywhere the risk would be closed; "
                       "they do not, so only the gate can settle it (diff=%r)" % diff)


def test_tick_size_is_never_inferred_silently_but_can_be_checked():
    tape, _ = flux.load_flux_tape(SESSION)
    assert abs(flux.infer_tick_size(tape) - 0.1) < 1e-9, "GC trades on a 0.1 grid"
    assert flux.FluxParams().tick_size == 0.1


# ════════════════════════════════════════════════ 7 · guards
def test_bad_parameters_raise_rather_than_degrade():
    for kw in ({"flux_size": 0}, {"mode": "runs"}, {"tick_size": 0.0},
               {"rounding": "floor"}, {"volume_semantics": "sum"}):
        try:
            flux.FluxParams(**kw)
            raise AssertionError("expected FluxError for %r" % kw)
        except flux.FluxError:
            pass
    try:
        flux.apply_crossed_policy(np.zeros(1), np.zeros(1), np.zeros(1, np.int8), "ignore")
        raise AssertionError("expected FluxError for an unknown crossed policy")
    except flux.FluxError:
        pass


def test_a_tape_with_no_trades_aborts_rather_than_returning_zero_bars():
    from engine.contract import synth_session
    t = synth_session(session_date="2026-07-20", rows=2000, trade_frac=0.0)
    try:
        flux.build(t, flux.FluxParams())
        raise AssertionError("an empty side is ABORT, not PASS")
    except flux.FluxError as e:
        assert "no usable trade rows" in str(e)


# ════════════════════════════════════════════════ 8 · THE GATE
def test_gate_rows_carry_every_field_the_bartype_spec_declares():
    from gates.artefacts import get
    spec = get("bartype")
    # nt_compat=False is the full artefact: every declared field is produced.
    rows = _session().gate_rows(instrument="GC 02-26", nt_compat=False)
    have = set(rows[0])
    for f in spec.precondition + spec.gate:
        assert f.name in have, "gate field %r missing from the port's rows" % f.name
    for k in spec.pair_keys:
        assert k in have, k

    # nt_compat=True drops exactly the two SentinelBarDump cannot supply, and no others.
    nt = _session().gate_rows(instrument="GC 02-26")
    assert have - set(nt[0]) == {"open_ts_ms", "tick_count"}
    keys = {(r["session"], r["bar_index"]) for r in nt}
    assert len(keys) == len(nt), "(session, bar_index) must be unique"


def test_gate_is_wired_and_can_fail():
    """⭐ A gate that has never failed is not a gate (§2).

    Both sides here are THIS port -- so a PASS says the wiring is sound and says NOTHING
    about NinjaTrader. The perturbed run proves the gate can fail; the NT reference does
    not exist (see THE GATE in flux.py) and is deliberately not invented here.
    """
    r = _session()
    rows = r.gate_rows(instrument="GC 02-26")
    sha = "b" * 64

    v = flux.run_bartype_gate(rows, r, instrument="GC 02-26", tape_sha256=sha)
    assert v.passed, v.to_text()

    bad = [dict(x) for x in rows]
    bad[1200]["close"] = round(bad[1200]["close"] + 0.1, 4)     # one tick, one bar
    v2 = flux.run_bartype_gate(bad, r, instrument="GC 02-26", tape_sha256=sha)
    assert not v2.passed, "a one-tick difference on one bar MUST fail an EXACT gate"

    bad2 = [dict(x) for x in rows]
    bad2[7]["volume"] = int(bad2[7]["volume"]) + 1
    assert not flux.run_bartype_gate(bad2, r, instrument="GC 02-26",
                                     tape_sha256=sha).passed

    missing = [dict(x) for x in rows[:-40]]                     # a side that lost 40 bars
    assert not flux.run_bartype_gate(missing, r, instrument="GC 02-26",
                                     tape_sha256=sha).passed


def test_the_nt_bars_export_exists_and_is_bar_type_agnostic():
    """CORRECTION of an earlier claim that NinjaTrader has no bars export. It does.

    Asserted against the SHIPPED indicator source, not against a memory of it.
    """
    src = os.path.join(os.path.dirname(TAPE_DIR), "..", "..", "..", "bin", "Custom",
                       "Indicators", "SentinelBarDump_v1_0_0.cs")
    src = os.path.normpath(src)
    assert os.path.isfile(src), src
    txt = open(src, encoding="utf-8", errors="replace").read()
    assert 'SchemaVer = "bars.1"' in txt
    assert "Calculate.OnBarClose" in txt, "one row per COMPLETED bar"
    for f in ('"i\\":', '"t\\":', '"o\\":', '"h\\":', '"l\\":', '"c\\":', '"v\\":'):
        assert f.replace("\\", "") in txt.replace('\\"', '"'), f
    # the deliberate absence of a realtime gate is what makes a historical rebuild usable
    assert "NO REALTIME GATE" in txt
    assert "State.Realtime" in txt and "if (State == State.Realtime) return" not in txt
    # header fields the Flux bar_params mapping depends on
    for k in ("periodType", "periodValue", "periodValue2", "baseValue", "tickSize"):
        assert '"%s\\":' % k in txt or '\\"%s\\":' % k in txt or ('"' + k + '\\":') in txt, k


def test_the_sibling_dump_reader_works_on_a_real_dump():
    """`ntdump.py` reads a real bars.1 file. Verified against the one already on disk."""
    import glob
    root = os.path.join(os.path.dirname(TAPE_DIR), "..", "..", "Harness", "bars")
    root = os.path.normpath(root)
    files = sorted(glob.glob(os.path.join(root, "*.jsonl")))
    if not files:
        return                       # nothing dumped on this box yet; nothing to verify
    header, rows = ntdump.read_dump(files[-1])
    assert header["schema"] == "bars.1"
    assert rows and all(k in rows[0] for k in ("i", "t", "o", "h", "l", "c", "v"))
    assert ntdump.iso_to_ms("1970-01-01T00:00:00.0000000Z") == 0
    assert ntdump.iso_to_ms(rows[0]["t"]) > 0
    # ...and its bar_params mapping REFUSES a non-Renko header, which is why flux.py
    # carries its own.
    try:
        ntdump.bar_params_of(header)
    except ntdump.DumpError:
        pass


def test_flux_bar_params_mapping_validates_the_dump_header():
    hdr = {"periodType": 212203, "periodValue": 8, "periodValue2": 0,
           "baseValue": 8, "tickSize": 0.1}
    assert flux.bar_params_from_dump_header(hdr) == "flux size=8 base=8 tick=0.1"
    assert flux.FluxParams(flux_size=8).bar_params() == "flux size=8 base=8 tick=0.1"
    assert flux.FluxParams(flux_size=16).bar_params() == "flux size=16 base=16 tick=0.1"

    for bad, why in (({"periodType": 212201}, "wrong bar type"),
                     ({"periodValue2": 24}, "Value2 must be 0"),
                     ({"baseValue": 12}, "Value != BaseBarsPeriodValue")):
        h = dict(hdr, **bad)
        try:
            flux.bar_params_from_dump_header(h)
            raise AssertionError("expected FluxError: %s" % why)
        except flux.FluxError:
            pass


def test_gate_rows_pair_on_the_same_convention_as_ntdump():
    """ntdump renumbers each session from 0 (`k - base`). Ours must too, or every row
    pairs against its neighbour and the gate reports a total mismatch."""
    r = _session()
    rows = r.gate_rows(instrument="GC")
    assert rows[0]["bar_index"] == 0, "0-based, like ntdump.gate_rows"
    assert [x["bar_index"] for x in rows[:5]] == [0, 1, 2, 3, 4]
    assert all(not x for x in [("open_ts_ms" in rows[0]), ("tick_count" in rows[0])]), (
        "nt_compat must omit fields SentinelBarDump cannot supply")
    assert len(rows) == r.n - 1, "the trailing forming bar is dropped (OnBarClose)"
    assert rows[0]["bar_params"] == "flux size=8 base=8 tick=0.1"
    assert "vol=nt" in rows[0]["builder"], "full settings ride in the NOTED field"

    full = r.gate_rows(instrument="GC", nt_compat=False, closed_only=False)
    assert len(full) == r.n and "tick_count" in full[0]


def test_session_window_is_the_session_definition_not_the_first_tape_row():
    """`ntdump` measured why: NT's anchor bar can close BEFORE our first tick, so keying
    on the tape's own first row makes a session ABORT on the arrival time of one tick."""
    d, win_a, win_b, first = flux.session_window(SESSION)
    assert d == "2025-12-09"
    assert (win_a, win_b) == (1765234800000, 1765317600000), "session_window_utc_ms"
    assert first == 1765234800387
    assert win_a < first, "the window opens before the first tick, which is the point"


def _newest_flux_dump():
    try:
        found = ntdump.find_dumps("GC", "212203v8")
    except ntdump.DumpError:
        return None
    return found[-1] if found else None


def test_rounding_rule_is_pinned_against_real_ninjatrader_bars():
    """⭐ THE REGRESSION GUARD FOR THE ROOT CAUSE.

    Reconstructs the unrounded HeikinAshi close from the tape for the bars whose
    open/high/low/volume/ts NinjaTrader confirms exactly, and asserts that ONLY
    half-away-from-zero-with-division reproduces NT's close. Both of the rules this port
    originally shipped are asserted to FAIL, so neither can creep back.
    """
    dump = _newest_flux_dump()
    if dump is None:
        return
    sd, wa, wb, first = flux.session_window(SESSION)
    nt, _hdr, _c = flux.reference_rows_from_dump(
        dump, session_date=sd, win_start_ms=wa, win_end_ms=wb, first_ts_ms=first)
    r = _session()
    tick = 0.1
    tape = r.tape
    n = min(400, len(nt))
    bad = {"half_even_mul": 0, "half_even_div": 0, "half_away_div": 0}
    for k in range(n):
        if int(nt[k]["ts_ms"]) != int(r.ts_ms[k]):
            break
        s, e = int(r.start_idx[k]), int(r.end_idx[k])
        seg = np.flatnonzero((tape.kind[s:e + 1] == KIND_TRADE)
                             & np.isfinite(tape.last[s:e + 1])) + s
        px = tape.last[seg]
        ha = (float(tape.last[s]) + float(px.max()) + float(px.min())
              + float(tape.last[e])) * 0.25
        ntc = float(nt[k]["close"])
        cands = {
            "half_even_mul": round(round(ha * (1.0 / tick)) * tick, 1),
            "half_even_div": round(round(ha / tick) * tick, 1),
            "half_away_div": flux.round_to_tick(ha, tick, "half_away"),
        }
        for name, v in cands.items():
            if abs(v - ntc) > 1e-9:
                bad[name] += 1
    assert bad["half_away_div"] == 0, (
        "half-away + division must reproduce NinjaTrader exactly; %d mismatches"
        % bad["half_away_div"])
    assert bad["half_even_mul"] > 0, "the original rule must still be demonstrably wrong"
    assert bad["half_even_div"] > 0, "so must half-even with division"
    assert flux.FluxParams().rounding == "half_away"


def test_the_gate_has_run_and_the_prefix_reproduces_ninjatrader_exactly():
    """The gate against REAL NinjaTrader output. It FAILS -- and here is exactly how far
    the port gets before it does, pinned so a regression is visible.

    ⛔ This is not a passing gate and must never be described as one.
    """
    dump = _newest_flux_dump()
    if dump is None:
        return
    v, c = flux.gate_session(SESSION, dump)
    assert v.exit_code == 1, "FAIL(1) -- not ABORT(2), not a traceback"
    assert c["closer_to_tick_rule"] is False, "the chart HAD quotes (Market Replay)"
    assert c["nt_bars"] > 3000 and c["port_bars"] > 3000

    sd, wa, wb, first = flux.session_window(SESSION)
    nt, _h, _c = flux.reference_rows_from_dump(
        dump, session_date=sd, win_start_ms=wa, win_end_ms=wb, first_ts_ms=first)
    mine = _session().gate_rows(instrument="GC")
    exact = 0
    for k in range(min(len(nt), len(mine))):
        if all(nt[k][f] == mine[k][f] for f in
               ("ts_ms", "open", "high", "low", "close", "volume")):
            exact += 1
        else:
            break
    # 422 exact before the rounding fix; 559 after (dump 20260805T020053).
    assert exact >= 550, (
        "the leading run of bit-identical bars shrank to %d -- the rounding fix or the "
        "volume model has regressed" % exact)


def test_the_nt_volume_model_is_confirmed_by_ninjatrader():
    """`seed + body + closing tick` -- the triple count inferred from @VolumeBarsType.cs,
    now confirmed against NT on every bar of the exact prefix."""
    dump = _newest_flux_dump()
    if dump is None:
        return
    sd, wa, wb, first = flux.session_window(SESSION)
    nt, _h, _c = flux.reference_rows_from_dump(
        dump, session_date=sd, win_start_ms=wa, win_end_ms=wb, first_ts_ms=first)
    r = _session()
    tape = r.tape
    tr = np.flatnonzero((tape.kind == KIND_TRADE) & np.isfinite(tape.last))
    sz = tape.size.astype(np.int64)
    ok = alt = 0
    for k in range(1, 400):
        if int(nt[k]["ts_ms"]) != int(r.ts_ms[k]):
            break
        s, e = int(r.start_idx[k]), int(r.end_idx[k])
        body = int(sz[tr[(tr > s) & (tr <= e)]].sum())
        if int(sz[s]) + body + int(sz[e]) == int(nt[k]["volume"]):
            ok += 1
        if int(sz[s]) + body == int(nt[k]["volume"]):
            alt += 1
    assert ok >= 390 and alt == 0, (ok, alt)


def test_signing_on_this_tape_is_fully_determined_by_the_quote_rule():
    """No trade is ambiguous: the tick-rule fallback never runs on this session, and no
    trade is missing a quote. So a boundary divergence cannot be a tick-rule defect."""
    r = _session()
    tape = r.tape
    tr = np.flatnonzero((tape.kind == KIND_TRADE) & np.isfinite(tape.last))
    px, bid, ask = tape.last[tr], tape.bid[tr], tape.ask[tr]
    assert len(tr) == 146547
    assert int(np.count_nonzero((px > bid) & (px < ask))) == 0, "inside-spread trades"
    assert int(np.count_nonzero((bid <= 0) | (ask <= 0))) == 0, "quote-less trades"


def _write_dump(path, rows, *, header_over=None, mutate=None):
    """A bars.1 file shaped exactly like SentinelBarDump's, from arbitrary rows.

    ⚠ FIXTURE, NOT DATA. It is written to a temp dir, never to Sentinel\\Harness\\bars, so
    it can never be mistaken for a NinjaTrader dump or picked up by the gate for real.
    """
    import json as _json
    hdr = {"hdr": 1, "schema": "bars.1", "dumpVer": "1.0.0", "coreVer": "1.45.0",
           "inst": "GC", "bartype": "212203v8", "barLabel": "SentinelFlux thr 8",
           "tickSize": 0.1, "pointValue": 100, "periodType": 212203, "periodValue": 8,
           "periodValue2": 0, "baseValue": 8,
           "tradingHours": "Nymex Metals - Energy ETH", "openedUtc": "1970-01-01T00:00:00Z"}
    hdr.update(header_over or {})

    def iso(ms):
        d, rem = divmod(int(ms), 1000)
        import time as _t
        t = _t.gmtime(d)
        return "%04d-%02d-%02dT%02d:%02d:%02d.%07dZ" % (
            t.tm_year, t.tm_mon, t.tm_mday, t.tm_hour, t.tm_min, t.tm_sec, rem * 10000)

    with open(path, "w", encoding="utf-8") as fh:
        fh.write(_json.dumps(hdr) + "\n")
        for k, x in enumerate(rows):
            o = {"i": k, "t": iso(x["ts_ms"]), "o": x["open"], "h": x["high"],
                 "l": x["low"], "c": x["close"], "v": x["volume"], "rt": False,
                 "newSession": k == 0}
            if mutate:
                mutate(k, o)
            fh.write(_json.dumps(o) + "\n")
    return path


def test_gate_session_runs_end_to_end_on_a_bars_1_file():
    """⭐ The WHOLE pipeline: tape + a bars.1 dump -> a verdict. Proven runnable.

    Both sides are this port, so the PASS proves PLUMBING and nothing about NinjaTrader:
    read_dump -> bar_params validation -> ntdump's session windowing -> run_bartype_gate.
    When the real dump lands (THE GATE recipe), the only thing that changes is the file.
    """
    import tempfile
    r = _session()
    rows = r.gate_rows(instrument="GC")
    with tempfile.TemporaryDirectory() as td:
        p = _write_dump(os.path.join(td, "20251209T000000__GC__212203v8.jsonl"), rows)

        v, c = flux.gate_session(SESSION, p)
        assert v.passed, v.to_text()
        assert c["nt_bars"] == c["port_bars"] == 3260
        assert c["rebuilt_bars"] == 0 and c["dropped_before_session_start"] == 0
        # the quote-degradation diagnostic fires only when it should
        assert c["port_bars_tick_rule_only"] == 3171
        assert c["closer_to_tick_rule"] is False and "WARNING" not in c

        # ...and it CAN fail: one tick on one bar.
        bad = _write_dump(os.path.join(td, "bad.jsonl"), rows,
                          mutate=lambda k, o: o.update(c=round(o["c"] + 0.1, 4))
                          if k == 900 else None)
        v2, _ = flux.gate_session(SESSION, bad)
        assert not v2.passed

        # ...and a dump from the WRONG bar type ABORTS at precondition, not at bar 4000.
        wrong = _write_dump(os.path.join(td, "wrong.jsonl"), rows,
                            header_over={"periodType": 212201})
        try:
            flux.gate_session(SESSION, wrong)
            raise AssertionError("expected FluxError for a non-Flux dump header")
        except flux.FluxError as e:
            assert "not SentinelFlux" in str(e)


def test_gate_session_flags_a_quote_less_chart_instead_of_blaming_the_port():
    """If NT's dump looks tick-rule-shaped, say so. This is the failure mode the
    Last-only .ncd cache would actually produce."""
    import tempfile
    tick_only = _session(params=flux.FluxParams(use_quote_rule=False))
    rows = tick_only.gate_rows(instrument="GC")
    with tempfile.TemporaryDirectory() as td:
        p = _write_dump(os.path.join(td, "q.jsonl"), rows)
        v, c = flux.gate_session(SESSION, p)
        assert not v.passed, "different bars must fail"
        assert c["closer_to_tick_rule"] is True
        assert "WARNING" in c and "no bid/ask" in c["WARNING"]
        assert "Market Replay" in c["WARNING"]


def test_the_reference_side_now_exists():
    """It did not on 2026-08-05 morning; a Market Replay chart produced it that evening.
    Kept as the marker that the gate is RUNNABLE, which it was once claimed not to be."""
    dump = _newest_flux_dump()
    assert dump is not None, "no *__GC__212203v8.jsonl -- see THE GATE for the recipe"
    hdr, rows = ntdump.read_dump(dump)
    assert hdr["periodType"] == 212203 and hdr["periodValue"] == 8
    assert hdr["baseValue"] == 8 and hdr["tickSize"] == 0.1
    assert flux.bar_params_from_dump_header(hdr) == flux.FluxParams().bar_params()
    assert all(not r.get("rt") for r in rows), "a pure historical rebuild"


def test_the_local_tick_cache_cannot_supply_quotes():
    """Why the recipe says Market Replay, not a historical chart. Measured, not assumed.

    db\\tick holds Last-only .ncd, so SignTrade's `ask > 0 && bid > 0` guard fails and NT
    would sign by the pure tick rule -- which builds materially different bars.
    """
    import glob
    root = os.path.normpath(os.path.join(os.path.dirname(TAPE_DIR), "..", "..", "..",
                                         "db", "tick", "GC 02-26"))
    if os.path.isdir(root):
        names = os.listdir(root)
        assert names, root
        assert all(n.endswith(".Last.ncd") for n in names), "expected Last-only tick cache"
        assert not glob.glob(os.path.join(os.path.dirname(root), "**", "*.Bid.ncd"),
                             recursive=True), "quotes appeared -- re-check the recipe"

    # the cost of that degradation, on real tape
    q = _session(params=flux.FluxParams(use_quote_rule=True))
    t = _session(params=flux.FluxParams(use_quote_rule=False))
    assert q.n == 3261 and t.n == 3171
    overlap = len(set(q.end_idx.tolist()) & set(t.end_idx.tolist()))
    assert overlap == 507, overlap
    assert overlap / q.n < 0.20, (
        "the signing rule dominates bar structure -- a quote-less chart is a different "
        "bar type, not a slightly different one")


# ════════════════════════════════════════════ 9 · the package contract
def _loaded_tape(path=SESSION):
    """The tape exactly as `bars.gate` supplies it: `tapeio`, unrepaired."""
    return tapeio.load_sessions([path])


def test_registered_in_the_package_registry():
    bars.raise_discovery_errors()          # a broken sibling must fail, not just be absent
    assert "flux" in bars.kinds()
    bt = bars.get("flux")
    assert bt.nt_period_type == flux.BARS_PERIOD_TYPE_ID == 212203
    assert bt.params_str() == "flux size=8 base=8 tick=0.1"
    assert bt.bartag() == "212203v8"
    assert bt.bartag(flux_size=16) == "212203v16"
    assert bt.build is flux.build_series
    assert flux.REGISTERED and not flux.REGISTRATION_SKIPPED


def test_build_series_satisfies_the_one_driver_signature():
    """`bt.build(tape, instrument=..., **params) -> BarSeries` -- what gate.py calls."""
    loaded = _loaded_tape()
    s = bars.build("flux", loaded.tape, instrument="GC")
    assert isinstance(s, BarSeries)
    assert s.bartype == "flux" and s.instrument == "GC"
    assert s.bar_params == "flux size=8 base=8 tick=0.1"
    assert len(s) == 3261
    assert s.tape is loaded.tape


def test_series_bar_index_is_zero_based_per_session_like_ntdump():
    """The off-by-one that would report a wholesale mismatch that is really an index bug."""
    s = bars.build("flux", _loaded_tape().tape, instrument="GC")
    assert int(s.bar_index[0]) == 0
    assert [int(x) for x in s.bar_index[:5]] == [0, 1, 2, 3, 4]
    assert int(s.bar_index[-1]) == len(s) - 1
    # the C#'s 1-based barsThisSession is NOT the pairing coordinate, and is declared
    assert s.notes["bar_index_base"] == 0 and s.notes["bars_this_session_base"] == 1


def test_series_is_partial_marks_exactly_the_forming_bar():
    s = bars.build("flux", _loaded_tape().tape, instrument="GC")
    assert int(np.count_nonzero(s.is_partial)) == 1, "one unclosed bar per session"
    assert bool(s.is_partial[-1])
    rows = bars_series.gate_rows(s, session_date="2025-12-09", closed_only=True)
    assert len(rows) == len(s) - 1 == 3260


def test_series_ohlc_is_the_ha_geometry_and_is_never_re_derived():
    """Flux's OHLC is Heikin-Ashi-smoothed; `bars_from_end_idx` re-derives RAW tape prices.

    The adapter must hand the port's own arrays through, or the gate compares numbers the
    port never produced.
    """
    loaded = _loaded_tape()
    s = bars.build("flux", loaded.tape, instrument="GC")
    native = flux.build(loaded.tape, flux.FluxParams(), crossed=None, sign_book="raw")
    for f in ("open", "high", "low", "close", "volume", "ts_ms", "end_idx", "tick_count"):
        assert np.array_equal(getattr(s, f), getattr(native, f)), f

    b = bars_series.to_engine_bars(s)
    assert np.array_equal(b.close, s.close), "the engine must receive the HA close"
    # ...and that really is different from what the raw seam would have produced
    from engine.bars import bars_from_end_idx
    raw = bars_from_end_idx(loaded.tape, s.end_idx)
    assert not np.allclose(raw.close[:len(s) - 1], s.close[:len(s) - 1])


def test_the_two_gate_paths_agree_bar_for_bar():
    """`series.gate_rows` (driver) and `FluxResult.gate_rows` (native) must not diverge --
    two row builders under one port is two ports."""
    loaded = _loaded_tape()
    s = bars.build("flux", loaded.tape, instrument="GC")
    a = bars_series.gate_rows(s, session_date="2025-12-09", closed_only=True)
    b = _session().gate_rows(instrument="GC")
    assert len(a) == len(b) == 3260
    for x, y in zip(a, b):
        for k in ("session", "bar_index", "instrument", "bar_params",
                  "open", "high", "low", "close", "volume", "ts_ms"):
            assert x[k] == y[k], (k, x, y)


def test_series_notes_surface_what_was_discarded_or_repaired():
    """A run that silently discarded rows must not look identical to one that did not."""
    s = bars.build("flux", _loaded_tape().tape, instrument="GC")
    n = s.notes
    assert n["crossed_rows"] == 140 and n["crossed_trade_rows"] == 1
    assert n["sign_book"] == "raw", "tapeio hands back the unrepaired book, by design"
    assert n["crossed_policy"] == "none (tape as given)"
    assert n["skipped_nonfinite_trades"] == 0
    assert n["close_reasons"]["imb"] == 2911
    assert n["quote_rule"] is True and n["tick_size"] == 0.1
    assert n["flux_scale"] == 1.0 and "vol=nt" in n["full_params"]


def test_series_params_str_is_the_string_the_dump_header_must_also_produce():
    bt = bars.get("flux")
    hdr = {"periodType": 212203, "periodValue": 8, "periodValue2": 0,
           "baseValue": 8, "tickSize": 0.1}
    assert bt.params_str() == flux.bar_params_from_dump_header(hdr)
    assert bt.params_str(flux_size=16) != flux.bar_params_from_dump_header(hdr)


def test_unknown_build_param_raises_rather_than_being_dropped():
    try:
        bars.build("flux", _loaded_tape().tape, instrument="GC", speed=12)
        raise AssertionError("expected FluxError for an unknown param")
    except flux.FluxError as e:
        assert "unknown flux params" in str(e)


def test_gate_cli_reaches_a_real_verdict():
    """`python -m bars.gate --bartype flux ...` must reach a VERDICT, not a traceback.

    It returns 1 (FAIL) while the reference dump is present and the residual boundary
    divergence stands; it returned 2 (ABORT) before the dump existed. Either is a real
    exit code from the driver -- a TypeError is not.
    """
    import contextlib
    import io
    buf = io.StringIO()
    with contextlib.redirect_stdout(buf):
        rc = gate.main(["--bartype", "flux", "--instrument", "GC 02-26",
                        "--session", "2025-12-09", "--one-session"])
    out = buf.getvalue()
    assert rc in (1, 2), out
    assert "3260 bars" in out or "3260 closed bars" in out, out
    if rc == 2:
        assert "MARKET REPLAY" in out, "the quote warning must fire for flux"
    else:
        assert "bartype" in out and "Azimuth" in out
        assert "crossed_quotes=140" in out


# ════════════════════════════════════════════════ runner
def _main() -> int:
    fns = [(n, f) for n, f in sorted(globals().items())
           if n.startswith("test_") and callable(f)]
    bad = 0
    for name, fn in fns:
        try:
            fn()
            print("PASS  %s" % name)
        except AssertionError as e:
            bad += 1
            print("FAIL  %s\n      %s" % (name, str(e)[:400]))
        except Exception as e:                       # a broken test is not a passing test
            bad += 1
            print("ERROR %s\n      %s: %s" % (name, type(e).__name__, str(e)[:400]))
    print("\n%d/%d passed" % (len(fns) - bad, len(fns)))
    return 1 if bad else 0


if __name__ == "__main__":
    raise SystemExit(_main())
