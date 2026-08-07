"""The engine (§6). One engine behind chart, analyzer, optimizer and WFA.

READ `bars.py` "THE INTERVAL GEOMETRY" FIRST -- everything below is expressed in it.

WHAT THIS ENGINE GUARANTEES
---------------------------
1. ONE POSITION AT A TIME. Never two, never scaled.
2. SAME-BAR CONFLICT. `entry_long[k] and entry_short[k]` -> NEITHER triggers, and
   the bar is counted in `entries_blocked_conflict`. It is not "long wins because
   the `if` came first".
3. FILLS AT THE CROSSING PRICE. Buy at the ask, sell at the bid, always, in
   `adapter.py`. No code path in this package computes a mid for a fill.
4. MILLISECOND TIMESTAMPS THAT NEVER SNAP TO A BAR BOUNDARY. Every fill carries
   the `ts_ms` of the tape row it happened on. The bar index is bookkeeping; the
   tape row is the truth.
5. A DECLARED SL/TP RESOLUTION ORDER. `config.EXIT_PRIORITY` +
   `config.TouchResolution`, applied in one place, counted in
   `BacktestResult.ambiguous_exits`.
6. LIMIT ENTRIES WITH A LIFETIME. Placed -> filled or EXPIRED, as real order
   objects with real state transitions.
7. WARMUP DAYS, CONTINUOUS MODE, AND FORCE-FLAT at session end, contract
   rollover, tape gap and end of data.

WHAT IT DELIBERATELY DOES NOT DO
--------------------------------
It does not scale in or out, it does not hold two instruments, and it does not
route to anything but `BacktestAdapter` (§1.1.3).
"""
from __future__ import annotations

import numpy as np

from .adapter import BacktestAdapter, ExecEvent, ExecutionAdapter, make_adapter
from .bars import Bars
from .config import (EngineConfig, ExitReason, InstrumentSpec, LegReason,
                     PositionMode, ScalingMode, spec_for)
from .contract import Tape
from .orders import (Amendment, Order, OrderState, OrderType, Position, Purpose, Side)
from .results import BacktestResult, Leg, Trade
from .strategy import (MarketContext, Signals, Strategy, target_position_to_signals)

_SCALE_PURPOSES = (Purpose.SCALE_IN, Purpose.SCALE_OUT)

_CHUNK0 = 512
_CHUNK_MAX = 1 << 20


def _changed(a: np.ndarray, b: np.ndarray) -> np.ndarray:
    """Elementwise "value changed", treating NaN -> NaN as unchanged."""
    return (a != b) & ~(np.isnan(a) & np.isnan(b))


class Backtester:
    """Stateless between runs; hold one and call `run` per parameter combo."""

    def __init__(self, cfg: EngineConfig | None = None,
                 spec: InstrumentSpec | None = None,
                 adapter: ExecutionAdapter | None = None) -> None:
        self.cfg = cfg or EngineConfig()
        self.spec = spec
        self._adapter_arg = adapter

    # ------------------------------------------------------------------ api
    def run_strategy(self, strategy: Strategy, bars: Bars,
                     spec: InstrumentSpec | None = None) -> BacktestResult:
        ctx = MarketContext(bars.tape, bars)
        sig = strategy.generate(ctx)
        res = self.run(bars, sig, spec=spec)
        res.params = strategy.describe()
        return res

    def run(self, bars: Bars, signals: Signals,
            spec: InstrumentSpec | None = None) -> BacktestResult:
        cfg = self.cfg
        spec = spec or self.spec or spec_for(bars.tape.instrument or "GC")
        mode = signals.validate(cfg.position_mode)
        if mode is PositionMode.TARGET:
            signals = target_position_to_signals(signals, cfg.scaling)
            signals.validate(PositionMode.SIGNALS)

        self._init_run(bars, signals, spec)
        if self._n_iv <= 0:
            return self._result()
        self._loop()
        return self._result()

    # ------------------------------------------------------------- set-up
    def _init_run(self, bars: Bars, sig: Signals, spec: InstrumentSpec) -> None:
        cfg = self.cfg
        self.bars, self.sig, self.spec_run = bars, sig, spec
        self.tape: Tape = bars.tape
        n = bars.n
        self._n_iv = n_iv = max(0, n - 1)

        self.adapter = self._adapter_arg or make_adapter(BacktestAdapter.name)
        self.adapter.bind(self.tape, spec, cfg)

        # ---- zero-row intervals (Renko & friends; see bars.py) ------------
        self.iv_nonempty = (bars.iv_nonempty if bars.iv_nonempty is not None
                            else np.ones(max(n_iv, 0), dtype=bool))
        self.nonempty_idx = np.flatnonzero(self.iv_nonempty).astype(np.int64)
        self._zero_row_intervals = int(np.count_nonzero(~self.iv_nonempty))

        # ---- force-flat geometry (indexed by INTERVAL) -------------------
        ff_raw = np.full(max(n_iv, 0), -1, dtype=np.int8)
        if n_iv:
            last_of_sess = bars.last_bar_of_session()[1:n]
            roll = bars.contract_changes_after()[1:n]
            gap = bars.gap_after(cfg.force_flat_gap_ms)[1:n]
            sess_flat = (last_of_sess | gap) if not cfg.continuous else gap
            ff_raw[sess_flat] = ExitReason.FORCE_FLAT_SESSION
            if cfg.force_flat_on_rollover:
                ff_raw[roll] = ExitReason.FORCE_FLAT_ROLLOVER
            ff_raw[n_iv - 1] = (ff_raw[n_iv - 1] if ff_raw[n_iv - 1] >= 0
                                else ExitReason.FORCE_FLAT_EOD)
        # A force-flat landing on a ZERO-ROW interval has nothing to fill against.
        # It rolls BACK to the nearest preceding interval that has rows -- whose
        # last row is the same tape row (consecutive row-less bars all share it),
        # so the position still flattens on the session's genuine final row.
        ff_reason = np.full(max(n_iv, 0), -1, dtype=np.int8)
        if n_iv:
            prev_ne = np.maximum.accumulate(
                np.where(self.iv_nonempty, np.arange(n_iv), -1))
            for k in np.flatnonzero(ff_raw >= 0):
                j = int(k) if self.iv_nonempty[k] else int(prev_ne[k])
                if j >= 0 and ff_reason[j] < 0:
                    ff_reason[j] = ff_raw[k]
        self.ff_reason = ff_reason
        self.force_flat_iv = ff_reason >= 0
        #: entries are blocked wherever a flatten was ASKED for or actually HAPPENS,
        #: so an entry can never be submitted into the dead zone before a session
        #: boundary and then carried across it.
        self.no_entry_iv = (ff_raw >= 0) | self.force_flat_iv

        # ---- entry eligibility ------------------------------------------
        el, es = sig.entry_long[:n_iv], sig.entry_short[:n_iv]
        conflict = el & es
        warm = bars.warmup_mask(cfg.warmup_days)[:n_iv]
        blocked = sig.block_entries[:n_iv]
        wanted = el | es
        ok = ~conflict & ~warm & ~blocked & ~self.no_entry_iv
        self.el_long = el & ok
        self.el_short = es & ok
        self.entry_iv = np.flatnonzero(self.el_long | self.el_short).astype(np.int64)

        self._blk_conflict = int(np.count_nonzero(conflict))
        self._blk_warm = int(np.count_nonzero(wanted & ~conflict & warm))
        self._blk_filter = int(np.count_nonzero(wanted & ~conflict & ~warm & blocked))
        self._limit_expired = 0

        # ---- run state ---------------------------------------------------
        self.pos = Position()
        self.trades: list[Trade] = []
        self.orders: list[Order] = []
        self.entry_order: Order | None = None
        self.entry_deadline_iv = -1
        self.stop_order: Order | None = None
        self.target_order: Order | None = None
        self.exit_order: Order | None = None
        self.scale_order: Order | None = None
        self._pending_ambiguous = False
        self._entry_sl = float("nan")
        self._entry_tp = float("nan")
        self._entry_was_limit = False
        self._reset_legs()

    def _reset_legs(self) -> None:
        self._legs: list[Leg] = []
        self._avg_entry = float("nan")
        self._opened_qty = 0          # total contracts opened over the trade
        self._closed_qty = 0
        self._peak_qty = 0
        self._realised = 0.0
        self._spread_ccy = 0.0
        self._commission = 0.0
        self._slip_ccy = 0.0
        self._open_notional = 0.0     # for the volume-weighted entry price
        self._close_notional = 0.0

    def _result(self) -> BacktestResult:
        amb = getattr(self.adapter, "ambiguous_count", 0) if hasattr(self, "adapter") else 0
        return BacktestResult(
            trades=self.trades if hasattr(self, "trades") else [],
            orders=self.orders if (hasattr(self, "orders") and self.cfg.record_orders) else [],
            spec=self.spec_run if hasattr(self, "spec_run") else (self.spec or spec_for("GC")),
            n_bars=self.bars.n if hasattr(self, "bars") else 0,
            n_tape_rows=len(self.tape) if hasattr(self, "tape") else 0,
            ambiguous_exits=int(amb),
            entries_blocked_conflict=getattr(self, "_blk_conflict", 0),
            entries_blocked_warmup=getattr(self, "_blk_warm", 0),
            entries_blocked_filter=getattr(self, "_blk_filter", 0),
            limit_entries_expired=getattr(self, "_limit_expired", 0),
            zero_row_intervals=getattr(self, "_zero_row_intervals", 0),
        )

    # --------------------------------------------------------- main loop
    def _loop(self) -> None:
        n_iv = self._n_iv
        k = self._next_entry_iv(0)
        if k < 0:
            return
        while 0 <= k < n_iv:
            self._process_interval(k)
            k = self._next_interval(k)

    def _next_entry_iv(self, frm: int) -> int:
        j = int(np.searchsorted(self.entry_iv, frm, side="left"))
        return int(self.entry_iv[j]) if j < self.entry_iv.size else -1

    def _next_nonempty(self, frm: int) -> int:
        """First interval at or after `frm` that contains tape rows."""
        j = int(np.searchsorted(self.nonempty_idx, frm, side="left"))
        return int(self.nonempty_idx[j]) if j < self.nonempty_idx.size else -1

    def _has_working_carry(self) -> bool:
        """A submitted order still waiting for a row to fill against."""
        for o in (self.exit_order, self.scale_order, self.entry_order):
            if o is not None and o.state.is_working:
                return True
        return False

    def _next_interval(self, k: int) -> int:
        """Where the engine must look next. Purely an optimisation when
        `fast_interval_filter` is on: with it off the engine visits every
        interval and must produce identical trades (asserted by test)."""
        if not self.cfg.fast_interval_filter:
            return k + 1
        if self._has_working_carry():
            # a live order fills on the next row that EXISTS -- zero-row
            # intervals offer nothing to fill against, so skip straight to one
            # that has rows rather than "expiring" the order into a vacuum
            return self._next_nonempty(k + 1)
        if not self.pos.is_flat:
            return self._next_exit_candidate(k + 1)
        return self._next_entry_iv(k + 1)

    def _exit_cond(self, lo: int, hi: int) -> np.ndarray:
        """Intervals in [lo, hi) where SOMETHING could close the position.

        Conservative by construction: it is the OR of "the book's extreme for
        that interval reached the protective price", the strategy's exit signal
        and force-flat. NaN protective prices compare False, i.e. absent.
        """
        s = slice(lo, hi)
        b, sig = self.bars, self.sig
        if self.pos.direction > 0:
            c = ((b.iv_bid_min[s] <= sig.sl_long[s])
                 | (b.iv_bid_max[s] >= sig.tp_long[s])
                 | sig.exit_long[s])
        else:
            c = ((b.iv_ask_max[s] >= sig.sl_short[s])
                 | (b.iv_ask_min[s] <= sig.tp_short[s])
                 | sig.exit_short[s])
        c = c | self.force_flat_iv[s]
        if sig.scale_bars is not None:
            # a change in the net target is a leg, so the interval must be visited
            c = c | sig.scale_bars[s]
        return c

    def _next_exit_candidate(self, frm: int) -> int:
        """Vectorised first-true over intervals, searched in growing chunks so a
        long hold costs O(hold) and not O(remaining tape)."""
        n_iv, i, step = self._n_iv, frm, _CHUNK0
        while i < n_iv:
            j = min(n_iv, i + step)
            c = self._exit_cond(i, j)
            t = int(c.argmax())
            if c.size and bool(c[t]):
                return i + t
            i = j
            step = min(step * 4, _CHUNK_MAX)
        return -1

    # ---------------------------------------------------- one interval
    def _process_interval(self, k: int) -> None:
        b = self.bars
        lo, hi = int(b.iv_start[k]), int(b.iv_end[k])
        # A ZERO-ROW interval (lo > hi): decisions are still TAKEN at this bar's
        # close -- orders are submitted below and simply find nothing to fill
        # against, so they carry to the next interval that has rows. Nothing
        # here may execute, expire or force-flat.
        has_rows = lo <= hi
        cursor = lo

        # (1) orders due at bar k's close
        if not self.pos.is_flat:
            self._sync_protective(k)
            self._maybe_scale(k)
            self._maybe_signal_exit(k)
        elif self.entry_order is None:
            self._maybe_submit_entry(k)

        # (2) drain the venue
        cursor = self._drain(k, cursor, hi)

        # (3) the entry limit's lifetime. Counted in intervals that HAVE ROWS:
        #     expiring an order across a run of row-less bars would retire it
        #     without ever giving it a quote to fill against.
        if self.entry_order is not None and k >= self.entry_deadline_iv and has_rows:
            self._expire_entry(hi)
            cursor = self._drain(k, cursor, hi)

        # (4) force flat on the interval's LAST row (session end / rollover /
        #     gap / end of data). Protective orders had priority: they were
        #     already worked over the whole interval above. Force-flat never
        #     lands on a zero-row interval -- it was rolled back to the nearest
        #     interval with rows in _init_run.
        if not self.pos.is_flat and self.force_flat_iv[k] and has_rows:
            reason = ExitReason(int(self.ff_reason[k]))
            o = self._new_order(
                side=Side.SELL if self.pos.direction > 0 else Side.BUY,
                qty=abs(self.pos.qty), type=OrderType.MARKET,
                purpose=Purpose.EXIT_FORCE_FLAT, bar=k, tape_idx=hi,
                exit_reason=reason,
            )
            self.exit_order = o
            self.adapter.submit(o)
            self._drain(k, hi, hi)

    def _drain(self, k: int, cursor: int, hi: int) -> int:
        passes = 0
        while cursor <= hi:
            evs = self.adapter.work(cursor, hi)
            if not evs:
                break
            last = evs[-1]
            step = 1
            if (last.order.purpose is Purpose.ENTRY
                    and not self.cfg.ignore_exit_on_entry_row):
                step = 0
            cursor = last.tape_idx + step
            self._apply(evs, k)
            passes += 1
            if passes > 8:
                raise RuntimeError(
                    f"interval {k}: more than 8 execution passes. One position at a "
                    f"time plus at most one scale bounds this well below 8; the "
                    f"invariant is broken."
                )
        return cursor

    # ------------------------------------------------------------ orders
    def _new_order(self, *, side: Side, qty: int, type: OrderType, purpose: Purpose,
                   bar: int, tape_idx: int, limit: float = float("nan"),
                   stop: float = float("nan"), lifetime: int = -1,
                   exit_reason: ExitReason | None = None) -> Order:
        o = Order(side=side, qty=int(qty), type=type, purpose=purpose,
                  created_ts_ms=int(self.tape.ts_ms[tape_idx]), created_tape_idx=int(tape_idx),
                  created_bar=int(bar), limit_price=float(limit), stop_price=float(stop),
                  lifetime_bars=lifetime, exit_reason=exit_reason)
        if self.cfg.record_orders:
            self.orders.append(o)
        return o

    def _maybe_submit_entry(self, k: int) -> None:
        long_ = bool(self.el_long[k])
        short_ = bool(self.el_short[k])
        if not (long_ or short_):
            return
        sig, b = self.sig, self.bars
        qty = int(sig.size[k]) or 1
        lim = float(sig.entry_limit_long[k] if long_ else sig.entry_limit_short[k])
        is_limit = np.isfinite(lim)
        o = self._new_order(
            side=Side.BUY if long_ else Side.SELL, qty=qty,
            type=OrderType.LIMIT if is_limit else OrderType.MARKET,
            purpose=Purpose.ENTRY, bar=k, tape_idx=int(b.end_idx[k]),
            limit=lim if is_limit else float("nan"),
            lifetime=self.cfg.entry_limit_lifetime_bars if is_limit else 1,
        )
        self.entry_order = o
        self.entry_deadline_iv = k + (self.cfg.entry_limit_lifetime_bars if is_limit else 1) - 1
        self._entry_was_limit = is_limit
        self.adapter.submit(o)

    def _maybe_scale(self, k: int) -> None:
        """Resize an OPEN position toward `position[k]` without changing its sign.

        Scaling is a change in the authoritative net target, not a new concept:
        `position[]` says how many contracts to hold, and any same-sign change
        emits one leg. Scaling never reaches zero -- that is an exit, handled by
        `exit_long`/`exit_short`.
        """
        tgt_arr = self.sig.target_position
        if tgt_arr is None or self.pos.is_flat:
            return
        if self.scale_order is not None and self.scale_order.state.is_working:
            return
        t = tgt_arr[k]
        if not np.isfinite(t):
            return
        tgt, cur = int(round(float(t))), int(self.pos.qty)
        if tgt == 0 or tgt == cur or (tgt > 0) != (cur > 0):
            return
        growing = abs(tgt) > abs(cur)
        if growing and self.cfg.scaling is not ScalingMode.FULL:
            return                      # already rejected at conversion time
        if not growing and self.cfg.scaling is ScalingMode.STRICT:
            return
        delta = tgt - cur
        o = self._new_order(
            side=Side.BUY if delta > 0 else Side.SELL, qty=abs(delta),
            type=OrderType.MARKET,
            purpose=Purpose.SCALE_IN if growing else Purpose.SCALE_OUT,
            bar=k, tape_idx=int(self.bars.end_idx[k]),
        )
        self.scale_order = o
        self.adapter.submit(o)

    def _expire_entry(self, tape_idx: int) -> None:
        o = self.entry_order
        if o is None:
            return
        if o.state.is_working:
            self.adapter.cancel(o, int(self.tape.ts_ms[tape_idx]), tape_idx,
                                expired=True, note="entry limit lifetime elapsed")
            self._limit_expired += 1
        self.entry_order = None
        if o.filled_qty > 0 and self.pos.is_flat:
            self._open_position(o, o.fills[-1].tape_idx, o.created_bar)

    def _sync_protective(self, k: int) -> None:
        """Set the protective prices in force for interval k (trailing for free)."""
        sig = self.sig
        long_ = self.pos.direction > 0
        sl = float(sig.sl_long[k] if long_ else sig.sl_short[k])
        tp = float(sig.tp_long[k] if long_ else sig.tp_short[k])
        side = Side.SELL if long_ else Side.BUY
        qty = abs(self.pos.qty)
        tidx = int(self.bars.end_idx[k])

        self.stop_order = self._sync_one(
            self.stop_order, np.isfinite(sl), OrderType.STOP_MARKET, Purpose.EXIT_STOP,
            ExitReason.STOP, side, qty, k, tidx, stop=sl)
        self.target_order = self._sync_one(
            self.target_order, np.isfinite(tp), OrderType.LIMIT, Purpose.EXIT_TARGET,
            ExitReason.TARGET, side, qty, k, tidx, limit=tp)

    def _sync_one(self, o: Order | None, want: bool, otype: OrderType, purpose: Purpose,
                  reason: ExitReason, side: Side, qty: int, k: int, tidx: int,
                  *, stop: float = float("nan"), limit: float = float("nan")) -> Order | None:
        if not want:
            if o is not None and o.state.is_working:
                self.adapter.cancel(o, int(self.tape.ts_ms[tidx]), tidx,
                                    note="protective price withdrawn by strategy")
            return None
        if o is None or not o.state.is_working or o.qty != qty:
            if o is not None and o.state.is_working:
                # ⭐ A protective order ALWAYS covers the remaining quantity. After
                # a scale-out the old bracket is cancelled and replaced at the new
                # size -- it is never left oversized, which would flip the position.
                self.adapter.cancel(o, int(self.tape.ts_ms[tidx]), tidx,
                                    note=f"resized to remaining quantity {qty}")
            o = self._new_order(side=side, qty=qty, type=otype, purpose=purpose,
                                bar=k, tape_idx=tidx, stop=stop, limit=limit,
                                exit_reason=reason)
            self.adapter.submit(o)
            return o
        # amendments are reconstructed exactly at trade close (see
        # _record_amendments) so the ledger does not depend on which intervals
        # the fast filter chose to visit.
        o.stop_price, o.limit_price = float(stop), float(limit)
        return o

    def _maybe_signal_exit(self, k: int) -> None:
        if self.exit_order is not None and self.exit_order.state.is_working:
            return
        if self.cfg.ignore_exit_on_entry_bar and self.pos.entry_bar == k:
            return
        want = bool(self.sig.exit_long[k] if self.pos.direction > 0 else self.sig.exit_short[k])
        if not want:
            return
        o = self._new_order(
            side=Side.SELL if self.pos.direction > 0 else Side.BUY,
            qty=abs(self.pos.qty), type=OrderType.MARKET, purpose=Purpose.EXIT_SIGNAL,
            bar=k, tape_idx=int(self.bars.end_idx[k]), exit_reason=ExitReason.SIGNAL)
        self.exit_order = o
        self.adapter.submit(o)

    # ------------------------------------------------------------ events
    def _apply(self, evs: list[ExecEvent], k: int) -> None:
        o = evs[0].order
        if any(e.ambiguous for e in evs):
            self._pending_ambiguous = True

        if o.purpose is Purpose.ENTRY:
            if o.state is OrderState.FILLED:
                self.entry_order = None
                self._open_position(o, evs[-1].tape_idx, k)
                self._sync_protective(k)
            return

        if o.purpose in _SCALE_PURPOSES:
            self.scale_order = None
            reason = (LegReason.SCALE_IN if o.purpose is Purpose.SCALE_IN
                      else LegReason.SCALE_OUT)
            for e in evs:
                self._add_leg(o, e.fill, k, reason, e.ambiguous)
            # the bracket now covers the REMAINING quantity, not the old one
            self._sync_protective(k)
            return

        # a closing order executed
        reason = LegReason.from_exit(
            o.exit_reason if o.exit_reason is not None else ExitReason.SIGNAL)
        for e in evs:
            self._add_leg(o, e.fill, k, reason, e.ambiguous)
        self._close_position(o, evs[-1].tape_idx, k)

    # ------------------------------------------------------------- legs
    def _add_leg(self, o: Order, fill, k: int, reason: LegReason,
                 ambiguous: bool) -> None:
        """Record one execution and move the net position by it.

        This is where the trade's arithmetic actually happens: opening legs
        build the volume-weighted entry price, closing legs realise P&L against
        it. A trade is the sum over its legs, so a scale-out needs no special
        case at the trade level.
        """
        spec, cfg, t = self.spec_run, self.cfg, self.tape
        idx = int(fill.tape_idx)
        qty, px = int(fill.qty), float(fill.price)
        delta = qty * o.side.sign
        before = int(self.pos.qty)
        after = before + delta
        opening = before == 0 or (delta > 0) == (before > 0)

        realised = 0.0
        if opening:
            self._open_notional += px * qty
            self._opened_qty += qty
            self._avg_entry = self._open_notional / self._opened_qty
        else:
            d = 1 if before > 0 else -1
            realised = d * (px - self._avg_entry) * spec.point_value * qty
            self._realised += realised
            self._close_notional += px * qty
            self._closed_qty += qty

        # crossing cost: a BUY pays (price - mid), a SELL pays (mid - price).
        # Direction-agnostic, and correct for opening AND closing legs alike.
        cost_px = (px - t.mid[idx]) if o.side is Side.BUY else (t.mid[idx] - px)
        cost_ccy = float(cost_px) * spec.point_value * qty
        comm = cfg.commission_per_side * qty
        slip = (cfg.slippage_ticks * spec.tick_size * spec.point_value * qty
                if fill.liquidity == "taker" else 0.0)
        self._spread_ccy += cost_ccy
        self._commission += comm
        self._slip_ccy += slip

        self.pos.qty = after
        self._peak_qty = max(self._peak_qty, abs(after))
        if opening:
            self.pos.avg_price = self._avg_entry

        self._legs.append(Leg(
            idx=len(self._legs), trade_idx=len(self.trades),
            ts_ms=int(fill.ts_ms), tape_idx=idx, bar=int(k),
            side=o.side, qty=qty, price=px, qty_delta=delta, position_after=after,
            reason=reason, order_id=o.id, liquidity=fill.liquidity,
            spread_cost_ccy=cost_ccy, realised_pnl=realised, commission=comm,
            ambiguous=ambiguous,
        ))

    def _open_position(self, o: Order, tape_idx: int, k: int) -> None:
        self._reset_legs()
        # the trade begins at its FIRST fill, which is not the last one when the
        # entry walked the book across several rows
        first = int(o.fills[0].tape_idx)
        self.pos = Position(
            qty=0, avg_price=float("nan"),
            entry_ts_ms=int(self.tape.ts_ms[first]), entry_tape_idx=first,
            entry_bar=int(o.created_bar), entry_order_id=o.id,
        )
        self._entry_order_ref = o
        for f in o.fills:
            self._add_leg(o, f, k, LegReason.ENTRY, False)
        self._entry_sl = float(self.sig.sl_long[k] if self.pos.direction > 0
                               else self.sig.sl_short[k])
        self._entry_tp = float(self.sig.tp_long[k] if self.pos.direction > 0
                               else self.sig.tp_short[k])

    def _close_position(self, o: Order, tape_idx: int, k: int) -> None:
        pos, spec, t = self.pos, self.spec_run, self.tape
        if not pos.is_flat:
            raise RuntimeError(
                f"close on a position that is still {pos.qty} contracts: a closing "
                f"order must be sized to the whole remaining position"
            )
        legs = self._legs
        d = 1 if legs[0].side is Side.BUY else -1
        ei, xi = pos.entry_tape_idx, int(tape_idx)
        entry_px = self._open_notional / self._opened_qty
        exit_px = self._close_notional / self._closed_qty

        exit_side_px = t.bid[ei:xi + 1] if d > 0 else t.ask[ei:xi + 1]
        if d > 0:
            mfe = (float(exit_side_px.max()) - entry_px) / spec.tick_size
            mae = (entry_px - float(exit_side_px.min())) / spec.tick_size
        else:
            mfe = (entry_px - float(exit_side_px.min())) / spec.tick_size
            mae = (float(exit_side_px.max()) - entry_px) / spec.tick_size

        self._record_amendments(pos.entry_bar, k)
        trade_idx = len(self.trades)
        self.trades.append(Trade(
            idx=trade_idx, direction=d, qty=self._opened_qty, peak_qty=self._peak_qty,
            entry_bar=pos.entry_bar, entry_tape_idx=ei, entry_ts_ms=pos.entry_ts_ms,
            entry_price=entry_px,
            exit_bar=int(k), exit_tape_idx=xi, exit_ts_ms=int(t.ts_ms[xi]),
            exit_price=exit_px,
            # NOT `o.exit_reason or ...`: ExitReason.STOP is an IntEnum whose
            # value is 0, i.e. falsy. That shortcut reported every stop-out as
            # a signal exit.
            exit_reason=(o.exit_reason if o.exit_reason is not None else ExitReason.SIGNAL),
            sl_at_entry=self._entry_sl, tp_at_entry=self._entry_tp,
            entry_was_limit=self._entry_order_ref.type is OrderType.LIMIT,
            gross_pnl=self._realised, commission=self._commission,
            net_pnl=self._realised - self._commission,
            spread_cost_ticks=self._spread_ccy / spec.point_value / self._opened_qty
                              / spec.tick_size,
            spread_cost_ccy=self._spread_ccy,
            slippage_ccy=self._slip_ccy,
            mfe_ticks=max(0.0, mfe), mae_ticks=max(0.0, mae),
            entry_order_id=pos.entry_order_id, exit_order_id=o.id,
            ambiguous_exit=self._pending_ambiguous,
            legs=legs,
            tags={name: bool(arr[pos.entry_bar]) for name, arr in self.sig.tags.items()},
        ))

        # tear down the bracket
        for ref in ("stop_order", "target_order", "exit_order", "scale_order"):
            oo = getattr(self, ref)
            if oo is not None and oo.state.is_working:
                self.adapter.cancel(oo, int(t.ts_ms[xi]), xi, note="bracket cancelled on exit")
            setattr(self, ref, None)
        entry_bar = pos.entry_bar
        self.pos = Position()
        self._pending_ambiguous = False
        self._reset_legs()

        # a flip: the entry signal for THIS bar may now be acted on, once.
        if k >= 0 and k < self._n_iv and self.entry_order is None:
            if (bool(self.el_long[k]) or bool(self.el_short[k])) and entry_bar != k:
                self._maybe_submit_entry(k)

    def _record_amendments(self, e: int, x: int) -> None:
        """Reconstruct the protective price path over [e, x] onto the live orders.

        Exact and independent of `fast_interval_filter`: the strategy's own
        per-bar array IS the amendment history, because `sl_*[k]` is by contract
        the price in force during interval k.
        """
        if x <= e:
            return
        b, sig = self.bars, self.sig
        long_ = True if self._entry_order_ref.side is Side.BUY else False
        for order, arr, fld in (
            (self.stop_order, sig.sl_long if long_ else sig.sl_short, "stop_price"),
            (self.target_order, sig.tp_long if long_ else sig.tp_short, "limit_price"),
        ):
            if order is None:
                continue
            seg = arr[e:x + 1]
            if seg.size < 2:
                continue
            ch = np.flatnonzero(_changed(seg[:-1], seg[1:]))
            for i in ch:
                kk = e + int(i) + 1
                order.amendments.append(Amendment(
                    int(b.ts_ms[kk]), int(b.end_idx[kk]), fld,
                    float(seg[i]), float(seg[i + 1])))
