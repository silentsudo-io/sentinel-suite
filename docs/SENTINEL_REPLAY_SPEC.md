# Sentinel — Historical Replay & the Fusion Core

How to run the learning loop on **historical data** so a corpus can bake with the market closed — without
poisoning it with lookahead. Companion to the [Thesis](SENTINEL_THESIS.html) (the *why* of the loop) and the
[ML Spec](SENTINEL_ML_SPEC.html) (the corpus schema + the Lab). Reference chapter of the
[docs](SENTINEL_DOCS.html).

---

## 1. The problem, and the reframe

The Recorder records Council fires **realtime only**. With the market closed, the corpus stops growing — and
the ConvictionFloor can't be fit until it does. We need to bake in the background.

But "realtime only" is widely misread. Two facts from the code:

- The Council **only publishes its verdict in `State.Realtime`** and stamps each verdict `IsHistorical =
  (State == State.Historical)`.
- During a normal chart's **history load**, the Council publishes *nothing* and the Recorder records
  *nothing*.

So the guard is not refusing historical data — it is refusing **silent lookahead** through the seam, which
has no as-of clock (`SetCouncilState` stamps wall-clock `UtcNow` even while replaying old bars, so a consumer
can't tell a replayed verdict from a live one). **Realtime-only ≠ live-market-only.** Historical *replay* is
still on the table; we just cannot do it through the bar-history-load path.

---

## 2. Three tiers (cheapest first)

### Tier 1 — NT Market Replay (today, zero code)
NinjaTrader's **Playback connection** feeds historical *tick* data through the **realtime** pipeline. During
the replayed portion `State == State.Realtime`, so the Council publishes and the full-range Recorder records —
**tick-accurate first-touch and all.** The guard passes because replay genuinely *is* the realtime code path.

*Use it:* download a replay day for the instrument, put the Council + Recorder + the sensor stack on a
Playback chart, run unattended. *Limits:* replay-data-gated per instrument/day; runs at replay speed (fine
overnight). *Validate:* confirm COUNCIL rows land with realistic barriers.

### Tier 2 — the self-contained Council Replay harness (the feature)
A historical-first Council. One NinjaScript unit that **hosts its sensors on its own series, fuses them, and
records excursions in a single causal bar pass — no cross-process seam.** It runs on plain historical bars
(fast, no replay-data dependency) and is **deterministic** — it removes the cross-instance processing-order
race that makes the seam path replay-unsafe. This is §3.

### Tier 3 — Databento (the data-quality axis, "Phase 4b")
For honest intrabar fills at scale and deep history. The clean split of responsibility:

- **NinjaTrader owns the brain.** One sensor implementation, in NinjaScript. **Never port the sensors to
  Python** — two implementations drift, and the model then learns the drift.
- **Databento `tbbo`/trades owns the honest outcome ruler.** NT emits fire events (scope · time · price ·
  verdict); an offline pipeline resolves target-vs-stop **first-touch on tick truth**.

This is the [backtest-fill-resolution lesson](SENTINEL_ML_SPEC.html) made infrastructure: bar-level
first-touch is *optimistic* (it took CompressionBase from 81% → 37.5% when it moved to tick fills). Feeds the
same schema-1.3 corpus the Lab already reads.

---

## 3. The fusion core (the enabling refactor)

The Council's `OnBarUpdate` does two separable things:

1. **Gather** — read each sensor's fresh reading from its seam and derive a directional vote
   (`AddVote(tag, dir, weight, …)`), plus the modulator states (Clock, Participation, MTF, Location, squeeze)
   and the hard-veto flags.
2. **Fuse** — pure math: kind-aware `denomW` → `bias` (deadband) → `conviction = |netScore| / denomW` →
   context damping → `sizeMult`, plus the agree/disagree tally.

**Step 2 is a pure function of its inputs.** Extract it:

```
CouncilFusion.Fuse(
    votes:      list of { tag, dir, weight, kind, counted }
    declared:   the roster (for the kind-aware denominator)
    modulators: { squeeze, clockPhase, inSession, rvol, mtfBias, lvlInPath, voters }
    veto:       { vetoed, reason }              // resolved by the front-end (seam/account reads)
    config:     { biasDeadband, convictionFloor, damp factors, minVoters }
) -> { bias, conviction, sizeMult, agree, disagree, contextMult }
```

Then there are **two front-ends over one core**:

- **Live Council** (unchanged behaviour) — gathers votes + modulators **from seams**, calls `Fuse`.
- **Replay harness** — gathers the same votes **from hosted sensor instances** (read each sensor's directional
  value directly, bar-by-bar), calls the *same* `Fuse`.

One fusion truth, exercised historically and run live. (This is also the seam of the **generic vote
registry** — a vote is a vote whether it arrived from a seam or a hosted sensor; see
[council-custom-voters].)

---

## 4. The correctness gate (non-negotiable)

A historical corpus is trainable **only if the verdict computed on bar X equals the verdict that would have
been live at bar X.** That requires:

- **Causal sensors** — no repaint, no forward-looking state. A sensor that looks ahead poisons every row.
- **One fusion core** — §3, so there is no live-vs-replay math drift.
- **Deterministic order** — hosting sensors in-process fixes the vote/modulator ordering the multi-instance
  seam path leaves racy.

**Validation:** run the harness over a window we *also* have live Recorder rows for, and confirm the verdicts
match (same bias, conviction within rounding). **If replay ≠ live, the corpus is fiction** — do not train on
it. Same *correctness-precedes-collection* discipline as the Thesis.

---

## 5. Build sequence

1. **Tier 1 now** — bake a first corpus via Market Replay while the rest is built.
2. **Extract `CouncilFusion.Fuse`** (§3) as a pure core; rewire the live Council to call it; **F5 + verify live
   behaviour is byte-for-byte unchanged** (careful surgery — market-closed is the right time).
3. **Build the replay harness** — hosts the sensor stack, gathers votes from the hosted instances, calls
   `Fuse`, records excursions causally on historical bars.
4. **Run the correctness gate** (§4) on an overlap window; only then trust replay-baked rows.
5. **Tier 3 / Databento** — the honest-fill pipeline for scale and depth.

*Status (2026-07-11): specified. Tier 1 usable now. Tier 2 core extraction is the next build.*
