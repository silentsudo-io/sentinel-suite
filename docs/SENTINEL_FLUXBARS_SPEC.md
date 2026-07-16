# SentinelFlux — Order-Flow Imbalance Bars (spec)

**Status:** ✅ **BUILT & LIVE-VALIDATED (2026-07-14)** · **Author:** Sentinel Suite · **Date:** 2026-07-14
**Artifact:** `BarsTypes\SentinelFlux_v1_0_0.cs` · class `SentinelFlux_v1_0_0` · display `"SentinelFlux v1.0.0"`
**BarsPeriodType id:** `212203` (reserved Sentinel bars block 212200–212299)
**Seam:** `SentinelCore.FluxState` (v1.31.0) → the **FLUX voter** in the Council (v1.6.3, now 22 voters) + a `FluxAbsorbDamp` absorption modulator.

> **Built from scratch** as an order-flow imbalance bar type (NOT a fork of TBars) — it borrows only the TBars *discipline*
> (quote-rule signing, ATR/price/time/tick backstops, HA render, deterministic latch). Live-validated on a full 10/10 Council
> roster on `GC.212203v8`; two threshold fixes shipped same-day (canonical `θ* = fluxScale × E[|θ|]` + winsorization). Grading
> vs TBars is pre-registered as **EXP-0004**.

> One line: a bar closes when **accumulated signed order-flow imbalance** crosses an adaptive threshold — López de Prado's
> *information-driven bars* — stabilized by the **TBars** discipline (ATR-clamped threshold + price/time backstops + HA bodies +
> deterministic per-session latch) so it can never hit the runaway failure mode that makes naïve imbalance bars unusable.

---

## 1. Why this bar type, for THIS system

This is not a novelty bar. It is aimed directly at two documented, load-bearing weaknesses of the Sentinel learning loop.

**1a. The orthogonality gap.** The Council's own caveat, repeated across the notes: *"today's voters are nearly all
PRICE-DERIVED (ADX/CCI/Trend/Envelope/Brick echo the same OHLC), so conviction = agreement, NOT confirmation."* Every renko
family in the tree (Renko, TBars, BetterRenko, WickedRenko, RenkoATR) reduces the market to **price geometry** — *where* price
went. None encode **how much it cost to get there**. A bar whose *clock* is signed order flow does two things nothing else in
the suite does:
- **It orthogonalizes the whole chart.** Any price-derived sensor (ADX/CCI/Trend…) running on a flow-synchronized substrate is
  now implicitly reading order flow. The substrate itself becomes an orthogonal axis — not just one new voter.
- **It publishes a native order-flow seam** (`FluxState`) → a genuinely orthogonal Council voter sourced from the *aggregated
  tape*, complementary to `LiquidityState` (sourced from the *book/absorption*).

**1b. The label-fidelity gap.** The Council trains on **first-touch triple-barrier labels measured in bars**
([[sentinel-ml-lab]], schema 1.3). Time/tick/range bars carry wildly unequal information per bar, so a horizon of "N bars" is a
fuzzy blend of dead-market bars and event bars — the label's clock drifts. Information-driven bars are *constructed* so each bar
carries ≈ constant information; "N bars ahead" ≈ "constant information ahead." This sharpens the exact label the ConvictionFloor
and per-bar-type weight fit consume. This is López de Prado's founding argument for these bars (AFML ch. 2), and it lands 1:1 on
the Sentinel rig.

**1c. It costs nothing to test.** A bar type is just a scope tag (`GC.212203v…`). The recorder, ingester,
`Lab\compare_bartypes.py`, `train.py`, and `council_paths.py` already separate and grade any scope. SentinelFlux slots into the
measurement loop with **zero new plumbing** (see §9).

---

## 2. Prior art & the failure mode we must beat

**López de Prado, *Advances in Financial Machine Learning*, ch. 2 — information-driven bars.** Sign each trade with the
**tick rule** `b_t = b_{t-1} if Δp=0 else sign(Δp) ∈ {−1,+1}`, accumulate a signed imbalance, and close a bar when the
imbalance exceeds its *expectation*. Three flavours by what is accumulated:

| Flavour | Accumulator θ_T | Samples |
|---|---|---|
| **Tick Imbalance Bars (TIB)** | `Σ b_t` | net trade-direction pressure |
| **Volume Imbalance Bars (VIB)** | `Σ b_t·v_t` | net *contract* pressure (default here) |
| **Dollar Imbalance Bars (DIB)** | `Σ b_t·v_t·p_t` | net *notional* pressure (cross-contract stable) |

Bar closes when `|θ_T| ≥ E₀[T]·|E[b·v]|` (VIB form), where `E₀[T]` is the expected bar length and `E[b·v]` the expected
signed volume per tick — both estimated online by EWMA.

**The known failure mode (why a naïve port is a bad bar type).** Both EWMAs are fed back from realized bars. In a strong trend,
`P[buy]→1` so `|E[b·v]|` inflates *and* bars lengthen, so the threshold **explodes** → one gigantic bar swallows the move. In a
balanced chop, `E[b·v]→0` so the threshold **collapses** → bars fire every tick. LdP flags this instability explicitly; it is
the reason imbalance bars are famous in theory and rare in production.

**The Sentinel move:** we already *own* the cure. TBars solved the analogous renko instability with an **ATR floor**, a
**density controller with Min/Max rails**, and **forced time-bricks**. We transplant that exact stabilization layer onto the
imbalance clock (§5). That transplant — a theoretically-superior sampling clock hardened by a proven adaptive-brick discipline —
is the hybrid.

---

## 3. The hybrid — three axes fused

SentinelFlux is a hybrid on three independent axes:

1. **Sampling axis** — bars close on **order-flow imbalance** (information-driven), not time / tick / volume / range / price.
2. **Stabilization axis** — the imbalance threshold is **ATR-clamped + backstopped** (TBars density rails + force-close), so it
   is bounded above and below and always terminates.
3. **Rendering axis** — bodies are **Heikin-Ashi smoothed** with **real price wicks** (TBars aesthetic), so the suite's cards,
   skins, and eye read it as family.

Plus a microstructure upgrade the tick rule can't do (§4).

---

## 4. Trade signing — quote rule, tick-rule fallback

`OnDataPoint(... bid, ask ...)` gives us the prevailing quotes per trade, so we sign better than the tick rule:

**Lee–Ready / quote rule (primary):**
```
if close >= ask  → b = +1   (buyer-initiated / lifted the offer)
if close <= bid  → b = −1   (seller-initiated / hit the bid)
else             → b = tick-rule fallback   (sign of Δp; carry prior sign on Δp==0)
```
Fallback to the pure tick rule whenever `bid<=0 || ask<=0` (historical tick data without quotes) — keeps it **deterministic**
and reproducible on reload (critical for the corpus; the as-of / lookahead lesson [[seam-scope-migration]]).

Signed contribution per trade: `Δθ = b · w`, where `w = 1` (TIB), `w = volume` (VIB, default), `w = volume·price` (DIB).

---

## 5. Bar-close rule & the stabilization layer (the core)

**State per forming bar:** `θ` (signed imbalance), `buyVol`, `sellVol`, `nTicks`, `barOpen`, running H/L, `birthTime`.

**The threshold (as SHIPPED, updated once per CLOSED bar — never per tick, for determinism):**
```
imbEwma = EWMA(IntensityLen) of realized |θ| at each close      // E[|θ|] — the expected imbalance per bar
θ*      = max(1, fluxScale · imbEwma)                           // fluxScale = FluxSize / 8 (the one knob)
```
This is the **canonical López de Prado imbalance-bar rule**: a bar closes when accumulated imbalance reaches its *expectation*.
It is **self-consistent** — bars close *at* θ\*, so `imbEwma` EWMAs toward θ\* and the target is stable; bars that close early
on a backstop carry `|θ| < θ*` and pull it **down**, which makes imbalance the primary close reason (self-correcting). There is
**no ATR term in θ\*** — ATR drives only the price backstop (below).

> ⚠ **Hotfix (2026-07-14, first live GC load).** The initial build used `θ* = fluxScale · atrTicks · (|θ| / net-displacement)`.
> ATR is a *true-range* measure (includes wicks) while the intensity divided by *net displacement* (close−open); in chop both
> factors inflate together, so θ\* ballooned to ~2.5× realized |θ| and **every realtime bar closed on the 90 s TIME backstop**
> (θ ≈ 38 vs θ\* ≈ 90 — the imbalance clock was dormant). Replaced with the self-consistent `E[|θ|]` form above. Found by
> reading the per-bar close-reason log, not by reasoning — the pattern to repeat.

**Close condition (per tick):**
```
CLOSE the bar when ANY of:
  (a) |θ| ≥ θ*                                   // imbalance target hit  (the information event)
  (b) price displacement ≥ PriceBackstopMult · atr   // hard price backstop (prevents the "one giant bar")
  (c) (now − birthTime) ≥ ForceStagnationSeconds     // time backstop      (prevents "never closes in a dead tape")
  (d) nTicks ≥ MaxTicksPerBar                         // tick backstop      (belt-and-suspenders)
```
On close: brick **flow-direction** = `sign(θ)`; **price-direction** = `sign(close − barOpen)`. These can DIVERGE — that
divergence is *absorption* and is surfaced natively (§7). Then update `expT`/`expImb`/`atr`, roll HA bodies, seed the next bar.

**Micro-split & quiet hours** are inherited from TBars unchanged (optional).

**Why this is stable:** the EWMA gives the *information-adaptive* behaviour (bars sync to surprise); the ATR band gives a
*physically-bounded* size so it can neither explode nor collapse; the three backstops guarantee termination. The bar always
closes for a legible reason, logged in `BrickLog` (`reason ∈ {imb, price, time, tick}`) so the corpus can audit *why* each bar
formed — itself a feature.

---

## 6. Determinism (non-negotiable for the corpus)

Same discipline as SentinelTBars ([[sentinel-tbars-tool]]):
- `BuiltFrom = Tick` (need true tick timestamps + quotes).
- Config **latched once per session**, frozen within a session; reload to apply new params.
- EWMAs seeded deterministically on the first bar; expectations updated **once per closed bar**.
- **Realtime-only publish** (`Core.Globals.Now − time > 5 min → skip`) so a historical rebuild never stamps a stale
  `FluxState` as fresh (the as-of guard — [[state-vs-trigger-voters]], [[seam-scope-migration]]).
- Scope-keyed publish: `SentinelCore.ScopeOf(bars.Instrument, bars.BarsPeriod)` → two GC charts on different Flux params never
  clobber each other (the scope lesson).

---

## 7. New seam: `SentinelCore.FluxState` (the orthogonal voter)

Publish per closed bar (+ live countdown per tick, like BrickState). Proposed `FluxState` fields:

| Field | Meaning |
|---|---|
| `FlowDir` (int −1/0/+1) | `sign(θ)` of the just-closed bar (net order-flow direction) |
| `PriceDir` (int) | `sign(close−open)` — for divergence detection |
| `Pressure` (double 0..1) | `buyVol / (buyVol+sellVol)` — the buy/sell balance |
| `Theta`, `Threshold` (double) | live accumulator vs current `θ*` (→ `PercentToClose`) |
| `Cvd` (double) | running cumulative volume delta (session) — a bonus tape read |
| `Divergence` (int) | `1` when `FlowDir` opposes `PriceDir` with meaningful `θ` (absorption) |
| `+ scope / instr / atrTicks / UpdatedUtc` | standard seam envelope |

**Council wiring** ([[council]], [[state-vs-trigger-voters]]):
- **FLUX voter — KIND = STATE** (flow direction persists across bars), weight ≈ **0.7**, tagged **orthogonal** (order flow).
  Vote = `FlowDir`. Its whole value is that it *does not* echo the OHLC bloc.
- **Divergence modulator** — when `Divergence==1` against the intended side (flow absorbing the move you'd take), **damp
  `SizeMult`** (a soft veto), mirroring the LiquidityWalls wall veto but from aggregated tape rather than the book. Add to the
  Council `Reasons` audit (`flux:absorb`).
- Register in the declared **Roster** ([[declared-roster]]) as `FLUX`; `w=0` remains the exploration primitive.

This satisfies the standing protocol (design-system §9): every new signal indicator/bar must publish a `…State` seam, default
publish ON, and be wired into the Council with a Reasons line.

---

## 8. Parameters (F6 grid; latched per session)

| Param | Default | Role |
|---|---|---|
| `ImbalanceMode` | `Volume` (VIB) | Tick / Volume / Dollar accumulator |
| `SignMode` | `QuoteThenTick` | quote rule primary, tick-rule fallback |
| **`Flux Size`** (= `BaseBarsPeriodValue`) | `8` | **the one knob** — `fluxScale = FluxSize / 8` sets the INFORMATION per bar (raise to coarsen) |
| `IntensityLen` | `50` | EWMA length for `E[\|θ\|]` (the self-consistent close threshold `θ* = fluxScale × E[\|θ\|]`) |
| `WinsorMult` | `4.0` | block-trade guard: cap a bar's `E[\|θ\|]` contribution at `×` the running estimate |
| `AtrLength` | `14` | per-brick ATR EMA (shared with TBars logic) |
| `PriceBackstopMult` | `2.5` | force-close at `×ATR` price displacement |
| `ForceStagnationSeconds` | `90` | time backstop |
| `MaxTicksPerBar` | `5000` | tick backstop |
| `EnableMicroSplit` / `EnableQuietHours` | `true` | inherited TBars behaviour |
| `PublishFluxState` | `true` | publish the seam + wire the Council |
| `ShowIndicatorLabel` | `false` | Sentinel label-remover standard |

There is intentionally **one primary "size" feel** exposed the way TBars exposes *Speed Settings*: **`Flux Size`** sets how much
information a bar represents (θ\* self-tunes to the tape via `E[|θ|]`). The size-sweep axis for EXP-0004 (§9) is `Flux Size`
(the scope tag encodes it — `GC.212203v8` vs `v12` — so a sweep is graded separately).

---

## 9. Testability — falsifiable hypotheses + the exact experiment

Pre-registered as **EXP-0004** (EXP-0002/0003 established TBars 6/24 as the edge-bearing bar via the same rig
[[continue-here-2026-07-12b]]). Run SentinelFlux **side-by-side** with SentinelTBars on GC; scope tags separate them
automatically; bake clean schema-1.3 Council rows on a **multi-voter** chart; then grade.

**H1 — sharper labels.** Conviction becomes more predictive of first-touch on Flux bars than on TBars.
→ `AUC(conviction → firstTouch)` on `GC.212203v…` **>** on `GC.212201v6x24`, and materially above the ~0.48 baseline.
Tool: `train.py --inst GC --bartype 212203vXxY --barrier 20 --cost 1.5 --spend-holdout`.

**H2 — orthogonality is real.** The FLUX voter does not echo the price bloc.
→ From the recorded schema-1.3 **vote vector** (§2.1), `|corr(FLUX, {TRND,CCI,ADX,ENV})| < 0.5`.
Tool: `council_paths.py --scope GC.212203v…` (vote-vector join already implemented).

**H3 — fill fidelity holds.** Flux bars are finer during fast moves (imbalance fires quickly when one side dominates), so
stop/target first-touch fill fidelity ≥ TBars' proven ~99.5% ([[corpus-hygiene-and-fill-fidelity]]).
Tool: the tick-true recorder sidecars (`council\ticks\`) already measure first-touch at tick resolution.

**H4 — path quality.** Per-fire MFE/MAE ratio and favorable-dwell (from `council_paths.py`) are ≥ TBars at matched conviction.

**KILL LINE (honest, pre-registered):** if after ≥ 8 baked multi-voter sessions **H1 fails (AUC ≤ TBars) AND H2 fails
(corr ≥ 0.5)**, the orthogonality thesis is falsified for this instrument → archive SentinelFlux to `_archive\`, keep the
finding. No nudging the params to rescue it — *fit it, don't nudge it* is the standing rule.

**Confounds to control:** (1) match the barrier in *ticks* not bars across the two types; (2) same sessions / same clock window;
(3) require the same voter roster on both charts (else H2's correlation is measured over different voters); (4) beware the
STF-only single-voter chart — conviction pins at 1.00 there (the current corpus limitation), so EXP-0004 needs the full stack.

---

## 10. Risks & open questions

- **Historical tick quotes.** If replayed tick data lacks bid/ask, signing silently degrades to the tick rule — fine and
  deterministic, but the live-vs-replay `FlowDir` may differ. Gate: log the signing mode per session; compare replay==live
  ([[council-historical-replay]]).
- **EWMA seeding transient.** First `ExpEwmaBars` bars of a fresh load are pre-convergence. Mitigation: seed `expT`/`expImb`
  from the ATR band on bar 0 (already the clamp floor), and mark the first N bars `warmup=1` in `BrickLog` so the corpus can
  drop them.
- **Direction semantics.** Flow-dir ≠ price-dir bars are informative but visually unusual. Decision: color the HA body by
  **price-dir** (familiar), expose flow-dir only via the seam + an optional flow-tint outline. Confirm the visual on first F5.
- **Dollar mode cross-contract.** DIB is the stable choice for GC↔MGC or roll continuity; default stays VIB, but expose DIB for
  the Intermarket use case.
- **Is imbalance or *runs* the better primitive?** Runs bars (max buy-run / sell-run length) sample persistence rather than net
  pressure. Imbalance is the cleaner first cut and maps onto directional semantics; a `SignMode`/`Mode` switch can add a runs
  variant later if EXP-0004 motivates it.

---

## 11. Build plan (when approved)

1. *(As built — historical build plan.)* Written **from scratch** as `BarsTypes\SentinelFlux_v1_0_0.cs` (id `212203`),
   borrowing only the TBars *discipline* (ATR/price/time/tick backstops, HA render, deterministic per-session latch, publish
   seam) — not the fixed-brick geometry. Class/Name/`BarsPeriod` in sync (naming law [[naming-federation]]).
2. Add the quote-rule signing + θ accumulator + the §5 close rule.
3. Add `SentinelCore.FluxState` seam (Core version bump) + Council FLUX voter + divergence modulator + Roster `FLUX` entry.
4. `nt8bridge compile --type Indicator` (strip generated region first) → F5 authoritative ([[nt8-bridge-compile-loop]]).
5. Load beside TBars on a full-stack GC chart; verify signing (Output log), bar cadence, seam publish, Council `FLUX` in the
   Roster; bake ≥ 8 sessions; run the EXP-0004 grade.
6. Version + changelog in-file; add to csproj; `.html` sibling of this spec ([[docs-need-html-copies]]).

---

*Cross-refs: [[sentinel-tbars-tool]] (the discipline we transplant) · [[council]] + [[state-vs-trigger-voters]] (fusion) ·
[[sentinel-ml-lab]] (why the label sharpens) · [[declared-roster]] · [[corpus-hygiene-and-fill-fidelity]] ·
[[council-historical-replay]] (the grading rig).*
