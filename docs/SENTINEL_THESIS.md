# The Sentinel Thesis

*What this project actually is, why it is built the way it is, and where it is going — at altitude,
readable without the code. The engineering sequence lives in [ROADMAP.md](ROADMAP.md); the product shape in
[PRODUCT_LADDER.md](PRODUCT_LADDER.md); the ML mechanics in [SENTINEL_ML_SPEC.md](SENTINEL_ML_SPEC.md). This
is the one-page "why."*

---

## 1. Abstract

**A chart full of indicators is a room full of experts talking over each other. Sentinel makes them vote —
then checks who was right.**

It is a suite of NinjaTrader 8 tools that turns a screen of independent indicators into **one honest, fitted
decision** — and then grades that decision against what the market actually did, so the decision can improve
itself over time.

Most trading indicators shout in isolation. Sentinel's premise is that the value is not in any one signal
but in **fusing many orthogonal ones into a single verdict**, measuring that verdict's real outcome, and
**feeding the measurement back** into how the fusion is weighted. The end state is not a fixed strategy —
it is a **learning loop** whose parameters are fit from evidence rather than guessed, and which adapts as the
market's character changes.

The whole system is designed so it works at **any depth of adoption**: a single pretty indicator, a fused
verdict you read but don't trade, an automated consumer, or the full self-tuning loop. Everything is
open-source (MPL-2.0); the ladder is depth-of-adoption, not price.

### The pieces (a two-minute glossary)

| Name | What it is | Its one job |
|---|---|---|
| **Sensors** | indicators (Trend, ADX, Woodies, VolEnvelope, Compression, Liquidity, GodReversal, WAE, + the axes) | *watch* one thing well and **publish** a reading |
| **Seams** | typed `…State` values on a shared bus (`SentinelCore`) | let tools **read each other without knowing each other** |
| **Council** | a read-only chart tool | **fuse** every fresh seam into one verdict (bias · conviction · size) |
| **Deck / Bridge** | manual trader / automated consumer | **act** on the verdict (the only pieces that place orders) |
| **Gate** | the pre-submit safety choke point | **guard** — kill switch, governor, sizing, session/news vetoes |
| **Recorder** | a no-orders characterization tool | **grade** — write each verdict's real forward outcome to disk |
| **Lab** | offline Python, outside NinjaTrader — now a stood-up data platform (SQLite corpus + JSONL ingester + Streamlit + Grafana) | **learn** — fit the Council's weights + floor from the graded corpus |

The rest of this document is how those pieces form a single loop, and why the order they were built in matters.

---

## 2. The thesis: the Council is an unfitted linear model

At the center is the **Council** — a read-only chart tool that fuses every sensor's published reading into
one verdict. Strip away the presentation and the Council computes exactly this:

```
netScore    = Σ ( voteᵢ × weightᵢ )        # each sensor votes −1 / 0 / +1
conviction  = | netScore | / declaredWeight  # how aligned the awake voters are, 0..1
bias        = sign(netScore)  if |netScore| > deadband   else 0   (FLAT)
sizeMult    = 0  if vetoed OR bias == 0 OR conviction < floor    else conviction × contextDamping
```

That is a **normalized linear model**. And here is the load-bearing observation: **its coefficients were
chosen by hand and never fit.** `WeightEye = 1.4`, `WeightTrend = 1.0`, …, the `0.20` conviction floor, the
`0.15` bias deadband — every one is an educated guess. They have never been checked against whether verdicts
that scored high actually did better than verdicts that scored low.

So "adding machine learning to Sentinel" is not bolting on a neural net. It is the far more modest and honest
act of **fitting the parameters the Council already has** from the Council's own recorded outcomes. The model
already exists. We are going to stop guessing its numbers.

### The voters are inputs, not fixtures

The Council today fuses a specific set of sensors — Trend, ADX, Woodies CCI, VolEnvelope, Compression,
Liquidity, GodReversal, WAE, plus the orthogonal axes. **That set was chosen to build and prove the fusion
process, not because the Council is defined by it.** The Council is a fusion *engine*; its voters are an
*input*.

The design principle: **any Sentinel-compliant signal is a valid Council input, and the roster is the user's
to choose and weight.** "Compliant" is a contract, not a lock-in — an indicator earns a seat by (1) following
the naming law and drawing to the skin, (2) publishing a normalized vote seam (direction + kind + freshness),
and (3) being declared in the roster config. Meet the contract and you are a voter; the specific indicators we
happened to test with are not privileged. A user should be able to drop in their own compliant indicator, or
remove one of ours, without touching the Council's code.

Today this is only half-true: voters are wired into the fusion in code and weighted by hand, so changing the
roster still means a code edit. The near-term architecture closes that gap — a **generic vote registry** where
the Council fuses *whatever* compliant votes are published for its scope, with weights read from the roster
config (`Roster.conf` already declares the voter set and per-voter weights; it is the seam this grows from).
When that lands, "customize your Council" becomes a config choice, and the learning loop below fits the weights
for *whatever roster the user assembled* — not just ours.

---

## 3. The learning loop (the spine)

Everything in the suite is a stage in one loop:

```
   ┌─────────────────────────────────────────────────────────────────────────┐
   │                                                                          │
   ▼                                                                          │
① PUBLISH      each sensor computes its read and publishes a …State seam       │
   │           (Trend, ADX, CCI, VolEnvelope, Compression, Liquidity, WAE,     │
   │            GodReversal + the orthogonal axes Clock/Participation/          │
   │            Location/MTF/Intermarket + the order-flow axis SentinelFlux —   │
   │            the first genuinely orthogonal one, a flow-synchronised bar     │
   │            clock)                                                          │
   ▼                                                                          │
② FUSE         the Council reads every fresh seam, weights it, sums to a       │
   │           verdict: bias · conviction · sizeMult · a "why" audit           │
   ▼                                                                          │
③ DECIDE       the verdict is ADVISORY — a single honest number, not an order  │
   ▼                                                                          │
④ ACT + GRADE  ┌ the Bridge (opt-in) may act on the verdict                    │
   │           └ the Recorder ALWAYS grades it: from each verdict it tracks     │
   │             the real forward outcome (which barrier — target or stop —     │
   │             is touched first) → an append-only corpus on disk              │
   ▼                                                                          │
⑤ LEARN        the offline Lab reads the corpus and FITS the parameters the    │
   │           Council guessed: first the conviction floor (where expectancy    │
   │           actually crosses breakeven), then the voter weights              │
   ▼                                                                          │
⑥ ADOPT        the fitted parameters FEED BACK into the live Council, which    │
   │           now decides with evidence-based numbers instead of guesses ──────┘
   (dynamic management — the loop closes and repeats, sharper each turn)
```

**Stage ⑥ is what makes it a loop rather than a report.** Without the feedback, the Lab produces a nice
number in a file and nothing changes. With it, the Council re-reads its fitted `Model.conf`, decides better,
those better decisions get graded, and the next fit is cleaner still. This is the **dynamic management**
target: the system tunes *itself* from its own measured results, and — because markets drift — keeps
re-tuning as their character changes.

Today the loop is **open**: stages ①–⑤ exist and run; stage ⑥ is the near-term goal. The floor and weights
are still hand-set, and the Lab's output is not yet wired back. Closing ⑥ is the point of the current work.

### One verdict, end to end

To make it concrete — a single verdict on a gold chart, from seams to graded row:

```
① PUBLISH   Trend +1 (w 1.0) · ADX +1 (0.6) · Woodies +1 (0.8) · Compression +1 (0.7)
            · VolEnvelope −1 (0.6) · GodReversal 0 (quiet trigger — abstains)
② FUSE      netScore   = 1.0 + 0.6 + 0.8 + 0.7 − 0.6            = 2.5
            declaredW  = 1.0 + 0.6 + 0.8 + 0.7 + 0.6            = 3.7   (quiet trigger excluded)
            conviction = |2.5| / 3.7                            = 0.68
            deadband   = 0.15 × 3.7 = 0.56  →  netScore 2.5 clears it  →  bias = +1 (LONG)
            floor 0.20 →  0.68 ≥ 0.20  →  sizeMult = 0.68 × context(≈1.0) = 0.68
③ DECIDE    verdict published:  LONG · conviction 0.68 · size× 0.68
④ GRADE     Recorder opens a row at fire price; barrier R = max(20t, ATR).
            Six bars later the +R target is touched before the −R stop → firstTouch = +1 (win).
            Row on disk: conviction 0.68 · sizeMult 0.68 · firstTouch +1
⑤ LEARN     this row joins every other verdict near conviction≈0.68 → P(win | conviction) curve
⑥ ADOPT     once fit, the floor/weights that produced this verdict are replaced by the ones the
            outcomes justify — and the next identical setup is scored a little more truthfully
```

Note what nobody did: no sensor knew the Council existed, the Council knew nothing about *which* sensors
voted (it read the bus), and the Recorder graded the verdict without any opinion about whether it was good.
Each piece does one job and publishes; the coupling is the bus, not the code.

---

## 4. The discipline: correctness precedes collection precedes learning

The temptation is to skip to stage ⑤ and fit something today. That is exactly the trap, because **a model
fit on a contaminated corpus learns the contamination.** Most of this project's engineering is not the model
— it is making the corpus *trustworthy* before a single number is fit. Three hard-won examples:

- **Scope keys.** A verdict must be tied to the exact chart context it was computed for (instrument *and* bar
  type). Before this, two charts of the same instrument silently overwrote each other's verdict every tick —
  the corpus was joining outcomes to the wrong decisions.
- **The as-of guard.** A published seam has no history: it is stamped with wall-clock time even while the
  Council replays historical bars. Recording during replay therefore stamped *today's* verdict onto bars from
  days ago — silent lookahead. The Recorder now records realtime only.
- **First-touch labels + below-floor recording.** "Did it win?" is only meaningful if you know whether the
  target or the stop was hit *first* (not just that both were eventually touched), and if you record verdicts
  *below* the current floor too — otherwise the corpus is censored at the floor and can never reveal whether
  the floor is set too high. A floor cannot be fit from data that only exists above it.

The principle: **correctness precedes collection precedes learning.** Each is a prerequisite for the next,
and getting them out of order produces a confident model that is confidently wrong.

---

## 5. Why this is honest-hard

Fitting a trading model is where people fool themselves most easily. Sentinel's Lab is built around the ways
that happens:

- **Overlapping outcomes leak.** Two verdicts 90 seconds apart share almost all of their forward window, so a
  random train/test split trains on the future. The Lab uses purged, embargoed, walk-forward splits — never
  random k-fold.
- **Effective N, not row count.** Those overlapping rows are not independent; the honest sample size is far
  smaller than the row count, and every significance test that ignores this lies.
- **Censored rows are dropped, not called losses.** A verdict that drifted sideways into the close is not a
  loss; labeling it one poisons the fit.
- **The baseline is the hand-set weights, not a coin flip.** If the fitted model can't beat the guesses on an
  honest split, the honest answer is "the guesses win" — and you still learn which sensors are dead weight.

The goal is not a model that looks good. It is a model whose edge survives the tests designed to destroy a
fake one.

---

## 6. Dynamic management: where stage ⑥ goes

Closing the loop is not one switch; it is a progression, each rung safer and more adaptive than the last:

1. **Fit the floor** (now unblocked). Map conviction → probability-of-win from the clean corpus, solve for
   where expectancy crosses breakeven after cost, and replace the hand-set `0.20`. One number, but the one
   the whole size decision pivots on.
2. **Fit the weights.** Ridge-regularized regression on the direction-folded voter vector replaces
   `WeightEye = 1.4, …`. This is where "which sensors actually carry information" stops being opinion.
3. **Feed it back — static adoption.** The Council reads a fitted `Model.conf` on load. Deterministic,
   auditable, reversible: a flat key=value file the C# side reads with no parser, versioned next to the
   configs that produced it.
4. **Per-context models.** One global fit is a blunt instrument. The same seam means different things in a
   trend vs a chop, at the open vs midday, on gold vs an index. The scope keys already carve the data into
   the coordinates a per-regime / per-scope model is defined over.
5. **Guarded online adaptation.** The end state: the model re-fits on a rolling window and updates the live
   parameters within hard, human-set bounds — never sizing beyond a cap, never overriding a safety veto,
   always logging what it changed and why. Adaptation lives *inside* the safety envelope, never around it.

Throughout, one rule holds: **the learned model advises size and selection; it never touches the safety
layer.** The kill switch, the governor, the order gate, the news and rollover vetoes are not things the model
is allowed to learn its way around. Dynamic management makes the system *smarter*; the safety substrate keeps
it *survivable*. Those are separate jobs and stay separate.

---

## 7. Where we are

- **①–④ built and running.** Sensors publish; the Council fuses and publishes a verdict; the Bridge can
  consume it; the Recorder grades it with first-touch labels across the full conviction range.
- **⑤ unblocked, data-ready.** The Lab exists and runs; the clean corpus has now accumulated — the SQLite
  DB holds ~5,300 trades, ~99% of them carrying the decision (vote) vector. Fitting the floor is the next
  concrete result and is no longer waiting on data — it is waiting on the fit being run.
- **⑥ is the horizon.** Feeding the fit back — dynamic management — is the point of everything upstream. The
  plumbing (scope keys, seams, the config-git repo, `Model.conf`) was built so that when the fit is
  trustworthy, adopting it is a small, safe, reversible step.
- **The roster opens up in parallel.** The generic vote registry (§2, *"the voters are inputs, not fixtures"*)
  turns the fixed sensor set into a user-assembled one — and because the loop fits weights for *whatever*
  roster is declared, "customize your Council" and "learn the customized Council" are the same machinery. Our
  test sensors were never the product; the fusion engine is.

The one-sentence version: **Sentinel fuses many honest signals into one fitted decision, grades that decision
against reality, and feeds the grade back into the fusion — inside a safety envelope it is never allowed to
learn around.**
