# Sentinel ML — Schema 1.3 Instrumentation + The Offline Lab

> **Status:** SPEC (2026-07-09). Nothing in this doc changes a trading decision.
> Phase 1 is behavior-neutral instrumentation; Phase 2 is offline Python; Phase 3 is an
> opt-in, default-OFF consumption flag. Read alongside `Docs/SENTINEL_DESIGN_SYSTEM.md` §6
> (SentinelCore seams) and the `council` + `backtest-fill-resolution-lesson` memories.

---

## 0. The thesis in one paragraph

`Council` computes `netScore = Σ(voteᵢ × wᵢ)`, `Bias = sign(netScore)` past a deadband,
`Conviction = |netScore| / declaredW`. That is a **normalized linear model over signed
features with hand-set coefficients**. The weights (Eye 1.4, Trend 1.0, CCI 0.8 …), the
conviction floor, and the 0.15 deadband are model parameters set by intuition. Fitting them to the
suite's own recorded outcomes *is* the machine-learning project. Everything below exists to make that
fit possible, honest, and reversible.

> **⚠ Roster size (2026-07-14).** This spec was written against the original **10-voter** roster
> (`EYE TRND CCI ADX ENV BRK CMP IMKT WAE GREV`). The Council now fuses **22 voters** (Council v1.6.3,
> incl. the order-flow FLUX voter). The 10-tag lists below are the ORIGINAL set, not the current one; the
> fit machinery (ridge over the signed vote vector) is unchanged and scales to whatever roster is declared.

> **⚠ UPDATED 2026-07-10 — this spec predates three changes to the model. Read before trusting any number below.**
>
> 1. **The denominator is `declaredW`, not `Σ(active weight)`** (Council v1.1.0). It used to be the summed weight of
>    the *present, directional* voters — so a missing voter did not dilute conviction, it **vanished from the
>    denominator**. One awake sensor of weight 0.6 scored `0.6/0.6 = 1.0`. The fewer sensors awake, the more certain
>    the Council sounded. Absence now dilutes. **Every conviction figure recorded before this date is on the old
>    scale and is not comparable.**
> 2. **Conviction is now pure agreement** (Council v1.2.0). Context modulators (Clock · MTF · Participation ·
>    Location · squeeze · breadth) used to multiply *into* conviction before the floor test; they now scale
>    `SizeMult` instead. The floor gates on agreement alone.
> 3. **The floor is 0.20, not 0.35** (Council v1.2.1, interim). Under the new scale only 1 of 97 live verdicts
>    cleared 0.35. ⚠ **The floor sits on a cliff** — 0.25→11% of verdicts, 0.20→41%, 0.15→60%. Fit it; do not nudge it.
>
> **And the finding that supersedes the floor question entirely:** `declaredW` conflates **STATE** voters
> (`TRND ADX ENV IMKT` — always directional) with **TRIGGER** voters (`EYE BRK CMP WAE GREV` — ±1 only on the bar
> they fire). The triggers carry **4.2 of the 7.80 total weight**, parked at zero on a typical bar, pinning
> conviction near 0.16. *A trigger that has not fired is an absence of evidence, not evidence against.* The fix — a
> voter **kind** in `Roster.conf`, whose weight joins `declaredW` only when it fires — is **not built**, and it
> should be settled **before** any weight fit, because it changes what the features mean.
>
> The §11 empirical tables below (conviction bands vs win rate) were computed on the **old** scale and on a corpus
> now known to be contaminated (pre-scope-keys, pre-as-of-guard). They are kept as method, not as findings.

**The blocker:** we record the model's *output* (`conviction`, `convBucket`) but never its *inputs*.
The per-voter direction vector lives in `Council._votes` at decision time and is discarded. Today we
can **grade** the Council; we cannot **fit** it.

---

## 1. Three findings that shaped this spec

### 1.1 Two Councils cannot run side-by-side. (This answers "can I fork it?")

`SentinelCore` keys `CouncilState` **by instrument name only**:

```csharp
private static readonly Dictionary<string, CouncilState> _council = …   // key = instrument
public static void SetCouncilState(string instrument, …) { _council[instrument] = s; }
```

Two Council indicators on the same instrument — say `Council_v1_0_0` and a hypothetical
`SentinelCouncil_v1_1_0` — are **last-writer-wins**. They stomp each other on every tick, and every
consumer (Bridge, GTrader21, Cockpit, the Recorder) reads whichever fired most recently. There is no
`Source` scoping in the key.

So the natural "run the old build while developing the new one" instinct **cannot be satisfied by
forking the Council**. It can, however, be satisfied a better way — see §4.

The **Recorder** is the opposite case: it writes per-instance files (historically
`<stamp>__<inst>__<bartype>.jsonl`; the current `SentinelExcursionRecorder_v2_0_0` writes schema-1.3 rows
under `Excursions\council\1.3\` + tick sidecars under `council\ticks\`) and already disambiguates two
instances on one chart. Two recorders coexist cleanly.

### 1.2 The orthogonal axes never reach the training set.

The Council computes `_clockPhase`, `_pRvol`, `_mtfBias`, `_lvlInPath` and folds them into conviction
as modulators. None of them are published on `CouncilState`, so none reach the JSONL. These are
precisely the **feature-independent** signals — the ones most likely to survive regularization
against the collinear price-derived voter block. Omitting them would train a model on the weakest
half of the evidence.

### 1.3 The current schema cannot resolve barrier ORDER.

`maxMFE` and `maxMAE` are running maxima to EOD. `msToMFE` / `msToMAE` are the times of those
*maxima*, **not** the first touch of any level. So given `maxMFE = 30t`, `maxMAE = 25t`, and a 20-tick
barrier, **we cannot tell whether the target or the stop was hit first** — which is the entire label.

The 1/5/15/60-minute milestone grid partially rescues this (a horizon label at 15 min is well-defined),
but ambiguity remains whenever both barriers are breached inside one horizon. Schema 1.3 fixes this
directly with a **first-touch** record.

---

## 2. Schema 1.3 — the additive changes

All three changes are **additive**. Old readers ignore new fields; the trainer reads mixed 1.2/1.3
files and simply has fewer features on old rows.

### 2.1 `SentinelCore` v1.14.0 → **v1.15.0** — `CouncilState` gains the decision vector

`SentinelCore` is a single static class in a single assembly. **It can never be forked.** Additive
only. Add to `CouncilState`:

```csharp
public sealed class CouncilState
{
    …existing fields unchanged…

    // v1.15.0 — the DECISION VECTOR (what the Council actually saw). Machine-readable
    // counterpart to the human-readable Reasons string. Nulls when the publisher predates 1.15.0.
    public Dictionary<string,int> Votes;      // tag → -1/0/+1, ONLY fresh voters (abstainers absent)
    public Dictionary<string,double> VoteW;   // tag → effective weight applied this update
    public double NetScore;                   // Σ(dir × w)  — SIGNED, pre-normalization
    public double ActiveW;                    // Σ(w) over voters that cast a direction
    // modulator context (the orthogonal axes — currently invisible to consumers)
    public int    ClockPhase;                 // -1 unknown · 0 Closed · 1 OpenDrive · 2 Midday · 3 Close
    public double Rvol;                       // ParticipationState rvol, NaN = none
    public int    MtfBias;                    // MtfState consensus (0 = none/agree)
    public bool   LevelInPath;                // a structural level lies in the bias's path
    public string LevelName;                  // that level's name
}
```

`Votes` / `VoteW` keyed by the Council's existing chip tags — `EYE TRND CCI ADX ENV BRK CMP IMKT WAE
GREV`. Use a stable tag order; the trainer treats an absent key as **abstain**, which is *not* the
same as a zero vote and must not be imputed as one.

Add an **overload** rather than editing the 11-arg `SetCouncilState` signature, so any caller that
predates this compiles untouched:

```csharp
public static void SetCouncilState(string instrument, int bias, double conviction, double sizeMult,
                                   int agree, int disagree, int voters, bool vetoed, string vetoReason,
                                   string reasons, string source)
    => SetCouncilState(instrument, bias, conviction, sizeMult, agree, disagree, voters,
                       vetoed, vetoReason, reasons, source, null, null, 0, 0, -1, double.NaN, 0, false, null);

public static void SetCouncilState(string instrument, int bias, double conviction, double sizeMult,
                                   int agree, int disagree, int voters, bool vetoed, string vetoReason,
                                   string reasons, string source,
                                   Dictionary<string,int> votes, Dictionary<string,double> voteW,
                                   double netScore, double activeW,
                                   int clockPhase, double rvol, int mtfBias, bool levelInPath, string levelName)
{ … }
```

> **Copy the dictionaries into the state object.** The Council reuses `_votes` every update; handing
> out a live reference publishes a struct that mutates under the reader's feet on the next tick.

### 2.2 `Council_v1_0_0` — populate the vector (behavior-neutral, IN-PLACE)

`AddVote` already receives `(tag, dir, weight)` and already maintains `_votes`. The change is to
carry `netScore` / `activeW` through to the publish call and project `_votes` into the two
dictionaries. **No change to `netScore` arithmetic, the deadband, conviction, `SizeMult`, or any
veto.** Diffable proof of neutrality: nothing upstream of `SetCouncilState` moves.

Also add a `LabelBarrierTicks` property (default `20`, `[Display]`-only — **not**
`[NinjaScriptProperty]`, per the design-system rule about not touching the generated region).

### 2.3 `SentinelExcursionRecorder_v1_4` — schema `"1.2"` → `"1.3"`

> **⚠ Current recorder (2026-07-14):** the live writer is now **`SentinelExcursionRecorder_v2_0_0` (internal
> v2.1.2)** — Council-only, and it writes schema-1.3 **row** files to `Excursions\council\1.3\` plus per-fire
> **tick-path sidecars** to `Excursions\council\ticks\` (v2.1.2 streams each row to disk on excursion-window
> completion, ~60 min, for crash-safety). The field list below is the schema-1.3 definition and still applies.

Emit, in addition to today's fields:

| Field | Type | Notes |
|---|---|---|
| `id` | string | stable row id: `sha1(inst + fireTime + signal + dir)[..12]`. Needed to join + to dedupe re-runs. |
| `votes` | object | `{"EYE":1,"TRND":1,"ADX":-1,…}` — **fresh voters only** |
| `voteW` | object | `{"EYE":1.4,"CCI":1.2,…}` — effective weight (post `×1.5` strong-CCI etc.) |
| `netScore` | number | signed |
| `activeW` | number | |
| `clockPhase` | number | -1 / 0-3 |
| `rvol` | number\|null | |
| `mtfBias` | number | |
| `levelInPath` | bool | |
| `levelName` | string\|null | |
| `barrierTicks` | number | the `R` used for the first-touch fields below |
| `firstTouch` | number | `+1` target hit first · `-1` stop first · `0` neither touched by EOD |
| `msToFirstTouch` | number\|null | |
| `trigger` | string | `"flip"` (today's edge-detect) — reserved for `"snap"`, see §5.3 |
| `costTicks` | number | commission + assumed slippage, in ticks, at fire |

**`firstTouch` is the single most valuable addition.** It is what makes a clean, unambiguous label
possible. Implement it in the running-update loop: once `fav >= barrierTicks` or `adv >= barrierTicks`,
latch whichever crossed first and stop updating it.

> **Fill realism (non-negotiable).** `firstTouch` is resolved from the same bar stream the recorder
> already walks. On a coarse bar type it will be **optimistic**, exactly as CompressionBase was
> (81% → 37.5% when moved from bar to tick fills — see the `backtest-fill-resolution-lesson` memory).
> Record `bartype` (already present) and **train per bartype**, or run the recorder on a tick-based
> series. Do not pool a Renko-labeled dataset with a minute-labeled one.

---

## 3. The label

Do not train on "did the trade win." Train on a barrier outcome, which exists for **every verdict**,
including ones no strategy took.

```
R          = barrierTicks
cost       = costTicks
label y    = 1  if firstTouch == +1 and (R - cost) > 0
             0  if firstTouch == -1
           drop if firstTouch ==  0   (censored — never resolved by EOD)
```

Censored rows are **dropped, not labeled zero.** A verdict that drifted sideways to the close is not
a loss; treating it as one teaches the model to fear chop that never cost anything.

`sample_weight` = **uniqueness** — the reciprocal of how many other rows' label windows overlap this
row's `[fireTime, fireTime + msToFirstTouch]`. Two verdicts 90 seconds apart share most of their
forward window and are **not** independent observations. Without this, effective N is wildly
overstated and every significance test lies.

---

## 4. The fork question — three lanes, only one of which is in the NT tree

**Git branches do not give you isolation here.** NinjaTrader compiles the *working tree* of
`bin\Custom` into one assembly. A branch checkout swaps the files under the running platform; you
cannot run `master` while editing a branch. NT's real branching mechanism is the **version-suffixed
filename**, and even that fails for the Council (§1.1 — shared seam key).

So do not fork the tree. Split by **lane**:

### Lane A — in-tree instrumentation (Phase 1). Merge to head, no fork.
`SentinelCore` +overload · `Council_v1_0_0` populate · Recorder → schema 1.3.
Behavior-neutral: no decision path is touched. One F5, then it accumulates training data
**while you keep trading the build exactly as it is.** This is the whole point — the data starts
building today and costs you nothing.

Precedent for patching the head in place rather than version-forking: GTrader21 v0.1.7's *"in-place
hardening: Ledger fill capture"* and Deck v0.2.2's *"kept IN-PLACE; freeze v0.2.3 once auto-fire
passes SIM."* Freeze `SentinelCouncil_v1_1_0` at the Phase-3 boundary — which is also the right
moment to pay the naming-law rename, since **that** is the change that legitimately breaks
serialization identity and drops the indicator off saved charts.

### Lane B — the offline Lab (Phase 2). Lives OUTSIDE `bin\Custom`. Fork freely.
`Documents\NinjaTrader 8\Sentinel\Lab\` — Python. **NinjaTrader never compiles it**, so no CS0101, no
ghost errors, no F5, no risk to a live account. `Sentinel\` is already the config-git repo, so the Lab
gets real branches, real history, and model artifacts versioned next to the configs that produced them.

This is where the parallelism you want actually lives. Iterate on the trainer for weeks while the
platform runs untouched.

> **Update (2026-07-14):** the Lab is no longer just a trainer script — it is now a BUILT SQLite **data
> platform** (`Lab\db\sentinel.db` + a live-watch JSONL→SQLite ingester that folds the `council\1.3` vote
> vectors in, + a Streamlit explorer on :8501 and Grafana on :3000). ~5,300 trades ingested, ~99% carrying
> the decision vector.

### Lane C — consumption (Phase 3). Fork by **flag**, not by file.
`Council` gains `UseLearnedWeights` (default **OFF**) and reads `Sentinel\Model.conf`. Absent, stale,
wrong-instrument, or malformed model ⇒ **fall back to the hand-set weights** — fail-open, matching the
suite's abstention semantics everywhere else.

Precedent: `UseCouncilGate` in GTrader21 v0.1.7 (default OFF; ON + `UseEyeGate` OFF = fully decoupled)
and `hardEnforce` on the Gate. The suite already knows how to introduce a new brain behind a switch.

**This gives you a true A/B**: flip the flag on one chart, leave it off on another instrument, and the
Recorder tags every row with which weight set produced it.

---

## 5. Phases

### 5.1 Phase 1 — Calibrate (needs *no* schema change; start now)
Fit a monotone map `conviction → P(win)` (isotonic, or Platt with 2 parameters). Two effective
parameters, converges on a few hundred rows, and **schema 1.2 already has everything it needs.**

Payoff: the 0.35 conviction floor stops being a guess. *(The `0.35` here predates the conviction rescale;
§0's **0.20 interim** floor is the authoritative current value — fittable, not yet fitted.)* Set it where calibrated expectancy crosses
zero after `costTicks`. Ship as `calib.*` in `Model.conf`; the Cockpit's why-line can then say
*"below floor (0.31 → 47% est.)"* instead of a bare number.

### 5.2 Phase 2 — Fit the weights (needs schema 1.3 + ~3 months of flips)
**Ridge-regularized logistic regression** on the signed voter vector. L2 is not optional: ADX, CCI,
Trend, Envelope and Brick are all price-derived and echo the same OHLC (the Council's own header
admits this). Unregularized, collinear coefficients thrash — huge positive on one twin, huge negative
on the other, superb in-sample, worthless out.

The fitted coefficients answer the question the suite was built to ask: **which voters carry
independent information.** My prior is that Clock, Participation, Intermarket and Location survive
shrinkage better than half the price-derived trend block.

**Sample volume, honestly.** The Recorder edge-detects on `v.Bias != _lastCouncilBias`, so it is one
row **per bias flip**. Measured on the existing data (§9): **~150 rows/day on one GC chart** — plenty
of rows, but the median gap between fires is **0.4 minutes** against a 15-minute label window, so
**effective N is a small fraction of row count.** Volume was never the constraint. Independence is.
Fit 10 ridge coefficients, not 40, and quote `nEffective`, never `nSamples`. *(The roster has since grown to
**22 voters** — Council v1.6.3; the coefficient count is whatever the declared roster holds, but the same
independence caution applies, now more so.)*

The one genuinely lucky property: the Recorder fires on **every** `HasEdge` flip, including verdicts
below the conviction floor that no strategy ever traded. **The training set contains the
counterfactual.** That is why the floor can be *learned* rather than merely *validated* — and it is
worth far more than any model-class upgrade.

### 5.3 Phase 3 — Regime-conditional weights, and only then trees
Separate weight vectors per coarse context bucket (`clockPhase`, ADX on/off, rvol high/low). A shallow
tree over the orthogonal axes with a linear model at each leaf. Keep buckets few — parameter count
multiplies by bucket count and §5.2's N does not.

If you later want more label density for calibration specifically, add `trigger:"snap"` rows (periodic
verdict snapshots). **They inflate N without inflating information** — heavily autocorrelated — so they
are admissible only under uniqueness weighting, and only for the calibration curve, never for weights.

Gradient-boosted trees are the realistic ceiling for tabular market data, not deep learning. But the
gain over §5.3 is usually modest and it **destroys the `Reasons` audit string**, which the entire
Cockpit why-line depends on. Reach for them last, if at all.

### 5.4 Explicitly out of scope
- **RL for entry/sizing.** Sample-inefficient, and it needs a simulator — which brings back the
  81%→37.5% fill-resolution collapse. An RL agent trained on an optimistic simulator learns to exploit
  the simulator. It will look magnificent and lose money.
- **Sequence models on raw price.** Signal-to-noise is too low; they fit microstructure and calendar
  artifacts. The edge here is five *orthogonal axes* most traders don't have — feed those to a simple
  model.
- **Training in-process.** NinjaScript is .NET Framework 4.8 on the UI/data thread. A GC pause in
  `OnBarUpdate` is a missed fill. Score inline (a dot product); train offline, always.

---

## 6. Validation protocol — decide it *before* looking at results

1. **Purged walk-forward CV with an embargo.** Never random k-fold. Overlapping forward windows mean
   adjacent rows share outcomes; random k-fold leaks the test set into training and is the single most
   common way people convince themselves they have an edge they don't.
   (López de Prado, *Advances in Financial Machine Learning*, ch. 7 — worth reading specifically
   because it is about this failure mode rather than about models.)
2. **Purge** any training row whose label window overlaps the test fold. **Embargo** a fixed span after
   each test fold before training resumes.
3. **Uniqueness-weight** every sample (§3).
4. **Hold out a final untouched period.** Spend it once. Every hyperparameter tried burns significance.
5. **Baseline is the hand-set weights**, not a coin flip. If ridge cannot beat the current Council on a
   purged walk-forward split, the honest report is *"the hand-set weights win"* — and you still gain
   the coefficient ranking, which tells you which sensors are dead weight and where to build next.

---

## 7. `Model.conf` — the artifact

Flat `key=value`, matching the suite's existing `.conf` idiom (`Profiles.conf`, `Alerts.conf`,
`News.conf`) so the C# side needs **no JSON parser**. Lands in the config-git repo, so every model
version is committed and diffable against the P&L it produced.

```ini
schema=1
trainedUtc=2026-10-02T14:11:03Z
expiresUtc=2026-11-02T00:00:00Z     # stale ⇒ Council falls back to hand weights (fail-open)
instrument=GC
bartype=SentinelTBars
nSamples=1180
nEffective=412                       # after uniqueness weighting — the honest N
auc=0.578
brier=0.238
baselineAuc=0.561                    # the hand-set weights, same purged split

# fitted voter weights (replaces WeightEye / WeightTrend / …)
w.EYE=1.22
w.TRND=0.61
w.CCI=0.09
w.ADX=0.44
w.ENV=0.12
w.BRK=0.31
w.CMP=0.83
w.IMKT=0.77
w.WAE=0.55
w.GREV=0.68
w.intercept=-0.14

# calibration: P(win) = 1 / (1 + exp(-(a * netScoreNorm + b)))
calib.a=2.31
calib.b=-0.27
calib.floor=0.42                     # conviction where expectancy crosses zero after cost
                                     # (illustrative — the live interim floor is 0.20 per §0, pending the fit)
```

**Guards the Council must enforce before trusting it:** `schema` matches · `instrument` and `bartype`
match the chart · `expiresUtc` in the future · every `w.*` tag present. Any failure ⇒ log once, use
hand weights, keep trading. A model file is never allowed to stop the platform.

---

## 8. Build order

> **STATUS (2026-07-12):** items **2–4 SHIPPED** (CouncilState vector + overload · Council populate · Recorder schema 1.3
> + first-touch) — bridge-verified, needs one F5. Item 1 (offline calibration trainer) exists in `Sentinel\Lab\`. See §11.5.

| # | Lane | Item | Risk | Blocks |
|---|---|---|---|---|
| 1 | B | Trainer reads schema 1.2 → **calibration curve** | none (offline) | — |
| 2 | A | `SentinelCore` v1.15.0 `CouncilState` vector + overload | none (additive) | 3, 4 |
| 3 | A | `Council_v1_0_0` populate (behavior-neutral) | none | 5 |
| 4 | A | Recorder schema 1.3 + `firstTouch` latch | none (writer only) | 5 |
| 5 | — | **Accumulate ~1 quarter of flips.** Keep trading the build as-is. | none | 6 |
| 6 | B | Ridge logistic + purged walk-forward vs. hand-weight baseline | none (offline) | 7 |
| 7 | C | `UseLearnedWeights` flag + `Model.conf` reader, default OFF | **gated** | — |
| 8 | C | Freeze `SentinelCouncil_v1_1_0` (pay the naming-law rename here) | serialization break | — |

Items 1–4 are a single evening and change nothing about how the platform trades. Item 5 is patience.
Everything with real risk is item 7, behind a default-OFF flag, on a decoupling pattern the suite has
already run twice.

---

## 9. Measured baseline — what `Sentinel\Excursions\` actually contains (2026-07-09)

1,258 rows across 11 files: 878 `CBRK`, **332 `COUNCIL`**, 61 `OBR`. All schema 1.2. Of the Council
rows, 308 carry a usable 15-minute milestone. Effectively all of it is **GC / `69697v6`** (303 rows);
`NQ` has 11 and a second GC bartype has 5. Four facts fell out, and each one is load-bearing:

**① The Council's bias chatters.** Median gap between fires: **0.4 minutes** (~150 rows/day). Against a
15-minute label window every row overlaps ~35 neighbours. **303 rows is nowhere near 303 observations**
— the honest effective N is in the tens. This is why §3's uniqueness weighting is not a refinement but
a prerequisite. It also suggests the Council itself wants a **minimum-dwell debounce** before it flips
bias: `netScore` hovers near the deadband and oscillates across it. Recommend the Recorder additionally
emit `msSinceLastFlip`, and that a dwell filter be evaluated (offline first — it changes nothing to
*measure* it).

**② `EYE` and `BRK` never vote.** Across 332 verdicts the `reasons` string contains `TRND` (330),
`ADX` (312), `WAE` (310), `ENV` (310), `CMP` (310), `CCI` (310), `GREV` (306), `IMKT` (305) — and
**zero** occurrences of the Eye or the Brick. 285 of 332 verdicts have exactly 8 fresh voters.
So `WeightEye = 1.4`, nominally *"the strongest single voice,"* has never once been applied on this
chart. Before fitting anything: either load the Eye, or accept that its weight is dead code and that
any model fitted here estimates **8 of 10 coefficients**.

**③ Conviction does not currently rank outcomes — it ranks them slightly backwards.**
On GC/`69697v6`, `P(mfe15 > mae15)` by conviction band:

| conviction | n | P(MFE > MAE) |
|---|---|---|
| 0.00 – 0.20 | 158 | 52.5% |
| 0.20 – 0.35 | 94 | 51.1% |
| 0.35 – 1.01 | 51 | **43.1%** |

The band the 0.35 floor *admits* is the worst-performing band. Given ① the effective N here is tiny and
this is a descriptive in-sample peek — it is **not** evidence the Council is broken. It is evidence that
**nobody has ever checked**, and that the floor was never derived from anything. Exactly the gap Phase 1
closes. Do not act on this table; reproduce it under purged CV with uniqueness weights.

**④ The schema-1.2 fallback label is unusable at realistic barriers — empirically, not just in theory.**
Applying §3's pessimistic both-breached-⇒-loss rule to the 15-minute milestone:

| R (ticks) | win | loss | censored | win rate | breakeven @ 1.5t cost |
|---|---|---|---|---|---|
| 5 | 38 | 270 | 0 | 12.3% | 65.0% |
| 20 | 75 | 231 | 2 | 24.5% | 53.8% |
| 40 | 123 | 175 | 10 | 41.3% | 51.9% |
| 80 | 89 | 104 | 115 | 46.1% | 50.9% |

`mae15` exceeds any small R almost always, so the pessimistic rule calls nearly everything a loss. This
is **not** a finding about the Council — it is the direct, measured consequence of §1.3: *schema 1.2
cannot resolve barrier order.* The `firstTouch` latch is what converts this table from noise into a
label. It is the single highest-value line of C# in this document.

> **Corollary:** run the Recorder on a chart whose bartype gives fine forward resolution, and set
> `barrierTicks` from ATR rather than from a constant. The `maxMAE` distribution here (mean ~60t at 15
> min on gold) says a 20-tick barrier is inside the noise.

---

## 10. Identity, roster, and the multi-contract fleet

> The sensing layer is **deliberately provisional**. The voters are scaffolding erected to find the
> right inputs, and they will churn. A design that only works once the voter set settles is the wrong
> design. What follows makes *churn* the normal case and *attribution* the invariant.

### 10.1 Three identities are currently collapsed into one

`SentinelBridge_v0_2_0` submits every order with `_tag = "SentinelBridge"` — a `const`. `Ledger.Write`
carries only `acct` and that free `tag`. The verdict is recorded as **prose** in
`Ledger.Action("bridge-fire", acct, detail)`. The excursion JSONL lives in a different file with **no
shared key**.

**Five** distinct things wear one name:

| Identity | What it answers | Today |
|---|---|---|
| **Actor class** | which code submitted the order | `"SentinelBridge"` |
| **Actor instance** | *which running Bridge* — this chart's, on this account | **nowhere** |
| **Decider (model)** | what produced the verdict — voters, versions, params, weights, floor, deadband, bartype | **nowhere** |
| **Policy** | *how it acted* — TP / SL / size / exit rules | **nowhere** |
| **Episode** | which decision this fill belongs to | **nowhere** (timestamp proximity only) |

The consequence is blunt: **Lens cannot join a fill to the verdict that caused it**, and no artifact
anywhere records *what the model was*. The claim that the Bridge "records the verdict so Lens can grade
the weights" is true only in the sense that it writes a sentence a human can read.

Fix all five. They are cheap, additive, and independent of any ML. §§10.2–10.4 handle the *decider* and
the *episode*; **§§10.9–10.11 handle the actor and the policy**, which an earlier draft of this document
wrongly treated as solved by `modelId`. They are not: `modelId` fingerprints the **Council's** config, and
says nothing about the thing that acted on it.

### 10.2 `EpisodeId` — the missing primary key

The Council's verdict updates continuously, but the meaningful unit is the **episode**: a maximal run
of constant `Bias`. The Bridge fires once per episode. The Recorder opens one `Rec` per episode. They
are already talking about the same object — it just has no name.

Give it one. `CouncilState.EpisodeId`, e.g. `GC-20260709-0042`, incremented on each bias change
(**not** each tick), stable across the episode's life.

Then it threads everywhere, and the joins close:

- Recorder writes `"episodeId"` into the excursion row.
- Bridge tags orders `SentinelBridge|GC-20260709-0042` **and** puts `episodeId` in the Ledger row.
- Fills inherit it via the order tag.

`fills → episode → verdict → excursion outcome` becomes a real join on a real key. This one field is
worth more to Lens than any model.

### 10.3 `ModelId` — a content-addressed fingerprint of the decision config

```
modelId = "gc-tbars-" + sha1(canonical(config))[0..7]
```

`canonical(config)` is a **decision-relevant allowlist**, sorted, stable-serialized:

```
instrument · bartype · councilVersion · coreVersion
weights[tag→w]  (sorted by tag)
deadband · convictionFloor · staleSec
roster[(voterTag, indicatorType, indicatorVersion, paramHash)]  (sorted)
modulatorsEnabled · vetoSet
```

**It must be an allowlist, never "hash everything."** `ShowCard`, `CardCorner`, `ShowIndicatorLabel`,
`LogChanges` are cosmetics; if they enter the hash, every UI tweak orphans your history and the fleet
fragments into unjoinable singletons. This rule is the whole game — write it down and enforce it in one
function.

`Sentinel\Models\<modelId>.json` is the **model card**: the full expansion of that hash. Content-addressed
⇒ **write-once, never mutated.** Same config ⇒ same id ⇒ same file. Different config ⇒ new id, new file,
and every prior row still resolves to the exact model that produced it.

### 10.4 Declare the roster; record the deviation

**This is the deepest problem, and §9's finding ② is its symptom.** The Council's roster is *emergent* —
it votes with whatever seams happen to be fresh. So `EYE` and `BRK` silently never voted for 332
verdicts and nothing anywhere said so. With a deliberately churning voter set, an emergent roster makes
attribution impossible: you cannot distinguish *the model* from *what happened to be loaded on the chart*.

Declare it. `Sentinel\Models\<INST>\<bartype>\Roster.conf` lists the **expected** voters. On load the
Council resolves the declaration against reality:

- expected **and** fresh → votes
- expected **and** absent/stale → `RosterComplete = false`, `missing = [EYE, BRK]`
- present but **not** declared → flag `unexpected`; do not silently fold it in

Then:

- **`modelId` fingerprints the declaration** — stable, intentional.
- **each verdict carries a `rosterMask`** — which declared voters actually spoke *this time*.

Fingerprint the intent; record the reality. Training can then filter on `rosterComplete`, or better,
*learn from* the abstention structure — which voter was missing is itself information.

The Cockpit gains the line that would have caught finding ② on day one:
**`Roster 8/10 — EYE, BRK missing`**. Same class of bug as the stale-vs-absent confusion it was built for.

> **Weight-0 voters are the exploration primitive.** Add a candidate sensor to `Roster.conf` with
> `w = 0`. It votes, it is recorded in `votes`/`voteW`, and it contributes **nothing** to `netScore` or
> `activeW`. You accumulate its full history — and can measure exactly what it would have contributed —
> **before it ever influences a single trade.** Adding and retiring sensors becomes a config change with
> zero risk and zero code. This is the workflow a provisional sensing layer actually needs.

### 10.5 Champion / challenger — how to explore *while* running

We established (§4) that two Councils cannot coexist: `CouncilState` is keyed by instrument, so they
stomp each other. But you never needed a second Council. **Scoring K weight-vectors against one voter
vector is K dot products** — nanoseconds, no allocation, no new indicator.

So the Council loads `Sentinel\Models\<INST>\challengers\*.conf`, computes the **active** verdict from
the champion (or the hand weights), and *also* evaluates every challenger on the identical voter vector.
It **publishes only the champion** — every downstream consumer is untouched — and **records all of them**:

```json
"challengers": { "gc-tbars-a3f91c2": {"bias": 1, "conv": 0.42},
                 "gc-tbars-77b0e41": {"bias": 0, "conv": 0.11} }
```

Every live bar now grades every candidate model on real data, at zero capital risk, with no fork and no
second chart. And because champion and challenger see the **same episode**, the offline comparison is
**paired** — it cancels the market-regime variance term entirely. Given §9's brutal effective-N problem,
a paired comparison is worth more than several extra months of unpaired data.

Promotion becomes a config edit. Demotion is instant. Neither touches code.

### 10.6 The fleet: per-instrument artifacts, *jointly* fitted

You are right that GC, NQ, ES and CL want different models. But the naive reading — four independent
pipelines — is the wrong one, and §9 says why: effective N per instrument is already in the tens. Split
that four ways and every model is noise.

The standard answer is **partial pooling**. Fit one joint model with a shared global weight vector plus
per-instrument deviations, penalizing the deviations hard:

```
w_inst = w_global + δ_inst        minimize  loss + λ‖w_global‖² + λ_δ‖δ_inst‖²      (λ_δ ≫ λ)
```

An instrument with little data is shrunk toward the global consensus; one with lots of data is allowed
to depart from it. You get per-instrument models **without** per-instrument overfitting, and you find out
*which* instruments genuinely differ rather than assuming they all do.

The C# side stays dumb: **`Model.conf` remains per-instrument, flat, fully expanded.** Only the *fitting*
is joint. Resolution chain in the Council: `(inst, bartype)` → `(inst, *)` → hand weights, fail-open, log
once.

> **Cross-contract features must be scale-free.** Ticks are not comparable across GC, NQ, ES and CL —
> neither in size nor in dollar value. Pooling raw tick MFE/MAE is meaningless. Schema 1.3 must therefore
> also record **`atrAtFire`** and **`tickValue`**, so barriers and excursions become **R-multiples in ATR
> units**. Without this, joint fitting is not merely inaccurate — it is nonsense. It is also, separately,
> the right way to set `barrierTicks` (§9 corollary).

### 10.7 Ledger schema — three fields, additive

`Ledger.Write(evt, account, data)` gains a context block, emitted on `order` / `action` / `fill` alike:

```json
"strat": "SentinelBridge_v0_2_0",
"model": "gc-tbars-a3f91c2",
"episode": "GC-20260709-0042"
```

`tag` stays for backward compatibility; `Entry` gains three parsed fields. Old rows simply carry nulls.
Every actor that submits an order — Bridge, Deck, GTrader21, Copier — passes the same context. **Actor,
decider, and episode become separable for the first time.**

### 10.9 Actor instance + policy — the two identities `modelId` does not supply

Walk the collisions. Two Bridges on **different instruments** are distinguishable today, but only by
accident: the Ledger happens to record `instr`. Two Bridges on the **same instrument, different bartype**
write identical `instr` (`GC 08-26`), `acct` and `tag` — only `modelId` separates them, since scope carries
bartype. That case §10.3 covers.

But two Bridges on the **same scope with different TP/SL** — the A/B you most want to run — share a
`modelId`, share an `episodeId`, and are **indistinguishable**. So is one Bridge before and after you widen
its stop: the Ledger cannot tell the two eras apart.

Two more keys close it:

```
instanceKey = "SentinelBridge#GC.TBC6-24-69697@SIM-LAB-A"   // class # scope @ account — DERIVED, stable
policyId    = "pol-" + sha1(canonical(execConfig))[0..3]    // tp · sl · baseQty · exitOnCouncilFlip · …
```

> **Delimiters are load-bearing.** Scope is `GC.TBC6-24-69697`, **not** `GC|TBC6-24-69697` — `|` is
> `Profiles.conf`'s field separator and cannot appear in a composite key. Account names are governed by
> `Docs/SENTINEL_NAMING_FEDERATION.md` §7 (`SIM-<LANE>-<SLOT>`, charset `[A-Z0-9-]`), which forbids every
> character used as a delimiter anywhere in the suite. The Gate verifies the `SIM-` prefix against the live
> connection, refuses unprofiled accounts, and treats `Sim101` as permanent quarantine.

Splitting **policy** from **model** is what makes A/B a comparison rather than a filing system. Two Bridges
on one chart running `TP=40` and `TP=60` share `modelId` **and** `episodeId` and differ only in `policyId` —
which is exactly right: *same decision, two policies, a paired comparison.* Same statistical trick as
champion/challenger (§10.5), one layer down.

`policyId` obeys the same allowlist discipline as `modelId` (§10.3): execution parameters in, cosmetics out.

### 10.10 The name is an interlock, not a label

`SentinelCore.RegisterActor(instanceKey, account, instrument)` returns **false** on collision, and the
Bridge **refuses to arm** when it cannot claim its key.

This is not bookkeeping hygiene. Consider what an `instanceKey` collision *is*: two Bridges on the same
scope **and the same account**. NT's account position is shared across strategies, and a managed strategy
whose account position moves underneath it desyncs and blocks all new entries until disable/re-enable (see
the managed-position lesson in `CONTRIBUTING.md`). **The configuration that is ambiguous in the Ledger is the
same configuration that is dangerous in the account.** Naming and safety are one mechanism; the second
falls out free.

Consequence for A/B testing: two *armed* Bridges on one instrument need **separate accounts** (`Sim101` /
`Sim102`), or one must run **shadow-record** — evaluating and writing its hypothetical fire to the Ledger
without submitting an order. Today a disarmed Bridge records *nothing*, which throws away the cheapest
data in the system. Shadow-record extends champion/challenger from Council weights up to execution policy.

**NT mechanics, two real traps.**

- Register in `State.Realtime`, never `DataLoaded` — otherwise a historical/replay instance claims the key.
- On `Terminated`, unregister **only if the registry entry is still this object** (reference check). NT
  re-enables a strategy by constructing a new instance and terminating the old one, and the terminate can
  land *after* the new registration. Unregistering blindly silently releases a live actor's key. Same shape
  as the "re-adopt refs, never fabricate" lesson.

### 10.11 Arc has the same disease

```csharp
private static readonly Dictionary<string, FleetSlot> _fleet;   // key = master instrument name
public sealed class FleetSlot { public string Instrument; public string Strategy; /* a LABEL, not a key */ … }
```

`SlotLive("GC")` returns **one answer for every GC strategy**. Two Bridges on GC share one slot, one plan,
one supervision record — so Arc cannot enable one and idle the other, and its `FillsToday` / `DayPnl` /
`Health` are silently summed across both. `FleetSlot.Strategy` already exists as a descriptive label;
promote it into the key. `_fleet[instanceKey]`, and `SlotLive(instanceKey)` with the existing fail-open
(no slot ⇒ true) preserved.

### 10.12 The trader's interface — names, not hashes

> **A design this structured will be under-used if the interface is not straightforward.** The content-
> addressed IDs above are machine-perfect and human-hostile. No trader will ever type `gc-tbars-a3f91c2`.

Four rules, in priority order.

**① The system must be fully usable with zero naming.** Naming is an optional refinement, never a setup
step. Every alias has an auto-derived, human-readable default:

| Thing | Auto-default | Trader may override to |
|---|---|---|
| instance | `GC · TBars 6-24 · Sim101` | `"GC Morning"` |
| model | `Hand weights` (or `Model.conf`'s `alias=`) | `"Balanced v3"` |
| policy | `TP40/SL20` (or the lab `.conf` filename) | `"Tight"` |

A trader who never opens the Cockpit still gets readable Ledger rows. **A hash is never shown by default.**

**② Alias ≠ identity.** The trader renames freely; nothing re-keys. `instanceKey` / `modelId` / `policyId`
are derived and immutable; the alias is a mutable display string bound to one. Rename "GC Morning" →
"GC Morning (old)" and every historical row still resolves. Store aliases in **`Sentinel\Aliases.conf`**
(`instanceKey = alias`), which lands in the config-git repo. The Ledger writes **both** — the id as the
join key, the alias denormalized alongside it so a raw JSONL line is readable without a lookup.

**③ One human name, not three.** The trader configures *a chart*. Call that a **Setup**. The Setup name is
the single durable human thread; `modelId` and `policyId` are **automatically-versioned eras inside it.**
Change the stop on "GC Morning" and the policy fingerprint changes while the Setup name does not — so Lens
reports *"GC Morning, policy changed 2026-08-14"*, which is exactly how a trader already thinks about it.
This is the whole UX thesis: **humans name the thing they can see; the machine versions everything under it.**

**④ Name where uniqueness is visible.** Uniqueness is obvious when you can see the whole fleet at once, and
invisible in a per-chart property grid. So:

- **F6 property grid** — `Setup name`, a `[Display]`-only string (not `[NinjaScriptProperty]`, so it
  serializes without touching the generated region — the Deck-presets precedent). A seed, not the store.
- **The Cockpit** — the real naming surface. It already re-reads every seam; it lists each discovered
  `instanceKey` with its alias, scope, brain, policy, armed state, roster completeness and day P&L, and lets
  you edit the alias in place. This is where the fleet is legible.
- **The on-chart card** — displays, never edits. Setup name large; derived scope small beneath it; brain and
  policy aliases on their own rows; then the existing verdict block and ARM button.

Precedence: `Aliases.conf` > F6 `Setup name` > auto-derived default.

**Collision behaviour differs by kind, and this distinction matters:**

- a duplicate **`instanceKey`** is the *dangerous* case (§10.10) → **block the arm**, card goes red, reason
  shown verbatim: `NAME TAKEN — "GC Morning" is armed on Sim101 (chart 2)`.
- a duplicate **alias** is merely confusing → **warn**, never block. Two setups may share a nickname; they
  cannot share an identity.

```
┌──────────────────────────────┐
│ GC MORNING            ● LIVE │   setup alias      (human)
│ GC · TBars 6-24 · Sim101     │   derived scope    (machine, shown small)
│ Brain   Balanced v3          │   model alias
│ Play    Tight 20/40          │   policy alias
│ ──────────────────────────── │
│ LONG    conv 0.62   size 1×  │
│ Roster  8/10  ⚠ EYE, BRK     │
│        [ ARM BRIDGE ]        │
└──────────────────────────────┘
```

**This extends the Federated Naming Law with a fourth axis.** The law governs *class / file / display /
namespace* — all **build-time** names. `Setup name` is a **runtime instance alias**, a different kind of
thing, and every actor in the suite (Bridge, Deck, GTrader21, Copier) should expose it identically.

### 10.13 Revised build order

> **STATUS (2026-07-12):** 0a–0c (scope keys · as-of guard · corpus cut) done earlier. This session shipped **item 1**
> (`EpisodeId`), **item 3** (Ledger `episode`/`instance` context), the **collision-refusal half of item 4**
> (`RegisterActor` + Bridge instanceKey refuse-to-arm — which also settles the §10.11 hazard), and **item 9's vector**
> (schema-1.3 votes/voteW/netScore/activeW; `atrAtFire`/`tickValue` still pending). Item 8 (Arc `_fleet[instanceKey]`)
> DEFERRED as a genuine Arc+GTrader21 redesign. See §11.5.

Items 2–4 of §8 absorb this; the identity work is a **prerequisite** to Phase 2, not a follow-on.

| # | Lane | Item | Why it comes first |
|---|---|---|---|
| 0a | A | **Scope-key every seam** (`GC\|TBC6-24-69697`), ambiguity-guarded compat shim | two charts on one instrument currently overwrite each other |
| 0b | A | **As-of guard** — `BarTimeUtc` + gate recording on `State.Realtime` | historical bars are stamped with realtime verdicts |
| 0c | — | **Regenerate the excursion corpus** | the existing rows are contaminated by 0a + 0b |
| 1 | A | `EpisodeId` on `CouncilState`; Recorder + Bridge stamp it | without it there is no join key at all |
| 2 | A | **`_tag` → derived `instanceKey`** (one line; no Core change) | 80% of attribution for 10 minutes of work |
| 3 | A | Ledger `strat`/`instance`/`model`/`policy`/`episode` context | separates the five identities of §10.1 |
| 4 | A | `RegisterActor` interlock + refuse-to-arm on collision | the ambiguous config *is* the dangerous config |
| 5 | A | `Roster.conf` + `rosterMask` + `RosterComplete` | makes the model *declared* rather than emergent |
| 6 | A | `modelId` + `policyId` fingerprints + write-once model card | makes history addressable |
| 7 | A | `Aliases.conf` + `Setup name` + Cockpit fleet roster | **the interface** — without it none of this gets used |
| 8 | A | Arc `_fleet[instanceKey]` (§10.11) | Arc cannot currently supervise two GC charts |
| 9 | A | schema 1.3 vector + `firstTouch` + `atrAtFire` + `tickValue` | the training set |
| 10 | A | challenger + shadow-record loop (no orders) | paired grading at zero risk |
| 11 | B | joint partial-pooled fit → per-instrument `Model.conf` | the fleet |
| 12 | C | `UseLearnedWeights`, default OFF | the switch |

**Items 0a–8 carry no ML at all** and are worth doing even if the modelling is abandoned entirely: 0a–0c
are outright correctness fixes, and 1–8 turn the Ledger from a diary into a database. Item 7 is not
decoration — a design this structured is **under-used if the interface is not straightforward**, and the
trader must never meet a hash.

---

## 11. IMPLEMENTATION LOG — what actually shipped (2026-07-09)

Phases 0 and 1.1–1.3 are **built and F5-verified**. Three corrections to this spec fell out of building it.

### 11.1 The real bar tag is `69697v6x24`, not `TBC6-24-69697`

`TBC6-24-69697` is a **legacy filename tag**. The Recorder's own comment explains why it changed: a custom bar
type's *name* resolves inconsistently by load state, so it now uses the numeric `(int)BarsPeriodType` id. The
canonical tag is `<typeId>v<Value>[x<Value2>]`.

**`SentinelCore.BarTag(bp)` additionally folds in `Value2`, which the Recorder's private version omitted** — so
TBars 6-24 and TBars 6-48 produced an *identical* tag and would have shared a scope. Real scopes look like:

```
GC.69697v6x24        // instrument . typeId v Value x Value2
GC.0v150             // 150-tick
```

### 11.2 Scope is necessary but **not sufficient** — hence contention detection

Scope separates GC from NQ, and GC-TBars from GC-150-tick. **It cannot separate two charts that share instrument
*and* bartype** — which is exactly what was live: two GC charts, both TBars 6-24, differing only in which sensors
were loaded, therefore computing *different* verdicts into one key.

Rather than invent a synthetic chart id, apply §10.10's principle: **two publishers claiming one key is a
misconfiguration to surface, never to silently permit.** Each Council carries a per-instance publisher id
(`Council#a3f9`); `SetCouncilState` logs once when a *fresh* (<5 s) entry from a different source is overwritten:

> `SCOPE CONTENTION: two live publishers for 'GC.69697v6x24' … consumers read whichever wrote last.`

`ClearCouncilScope(scope, source)` lets a publisher release **its own** entry on teardown, so a closed chart never
leaves a stale verdict and an F5 doesn't false-trip the detector against its own replacement.

**Open decision:** different bar types · close one · or fold the **roster + Setup name** into the scope (§10.4,
§10.12) — which is what Phase 3 was already going to build, and is the principled answer.

### 11.3 The as-of guard, and the proof it was needed

`SetCouncilState` stamps `UpdatedUtc = DateTime.UtcNow` **unconditionally**, including while the Council replays
historical bars. So the freshness gate could never distinguish a replayed verdict from a live one, and the Recorder
stamped whatever the live seam happened to hold onto bars from days earlier.

**Measured proof:** in `20260709T002037__GC__0v150.jsonl`, five COUNCIL rows with fire times spanning three days
all carry `conviction = 0.1541` — identical to four decimals. A freshly-computed verdict cannot do that.

Fix: `CouncilState` gains `BarTimeUtc` + `IsHistorical`; the Recorder records Council fires **only in
`State.Realtime`**, with the `IsHistorical` flag as belt-and-braces. **The existing corpus is unsalvageable** —
no field distinguishes the contaminated rows — so it gets archived at step 1.5, not repaired.

### 11.4 Bugs surfaced while building

| # | Bug | Consequence |
|---|---|---|
| 1 | **`Eye_v1_1_0` throws on `OnStateChange`** (`AddDataSeries: no implementation for this BarsPeriod`) | it never publishes ⇒ **`EYE` appears in ZERO of 332 verdicts.** `WeightEye = 1.4`, the heaviest voter, has never voted |
| 2 | `SentinelBridge` never calls `SizedQuantity()` | `Profiles.conf` `size=` **and** the governor's `RecommendedSize()` are silently ignored |
| 3 | `ContractLimit` **rejects** rather than clamps | a `BaseQty` above the limit hard-blocks *every* entry rather than trading small |
| 4 | 164 `ALERT-CRIT` Ledger rows carry `acct=""` | every `NAKED POSITION` alert is un-attributable |
| 5 | live prop account's loss stop is **advisory** | it ran to **−$3,230 against a −$1,500 stop**, alerting eight times |

Bug 1 vindicates §10.4: **a crashed sensor is indistinguishable from a quiet one** under fail-open abstention. The
declared roster (`RosterComplete`, `Roster 8/10 — EYE, BRK missing`) would have caught it on day one.

---

## 11.5 IMPLEMENTATION LOG — 2026-07-12 (Phase 2 instrumentation SHIPPED, bridge-verified)

Everything below **compiles clean against NinjaTrader's own compiler** (driven headless via the `cli-nt-bridge` AddOn —
authoritative F5-equivalent, no ghost errors) and is **additive / behaviour-neutral** except the Bridge arm interlock.
It needs **one F5** in the NinjaScript editor to hot-load (generated regions were stripped on disk during editing; NT
regenerates one clean copy on F5). No type renames → saved charts/workspaces are unaffected.

**Live versions after this session:** SentinelCore **v1.25.0** · Council **v1.4.0** · ExcursionRecorder v1_4 (schema 1.3,
+vote vector) + v2_0_0 · SentinelBridge **v0.2.3** · CompressionBase **v1.3.2**.
*(Point-in-time snapshot; kept as the historical log. Current as of 2026-07-14: SentinelCore **v1.31.0**, Council
**v1.6.3** fusing **22 voters** incl. FLUX. The live recorder is now **SentinelExcursionRecorder_v2_0_0 (internal
v2.1.2)** — Council-only, schema-1.3 rows in `Excursions\council\1.3\` + per-fire tick sidecars in `council\ticks\`.)*

**§2.1–2.3 — the decision VECTOR (DONE).** `CouncilState` now carries `Votes`/`VoteW`/`NetScore`/`ActiveW` + the modulator
context (`ClockPhase`/`Rvol`/`MtfBias`/`LevelInPath`/`LevelName`). A **new full `SetCouncilState` overload** carries them;
every prior overload delegates with vector defaults (Core v1.24.0). The Council projects `_votes`→dicts at publish and
**caches them for the `OnMarketData` heartbeat republish**. Both recorders emit `votes`/`voteW`/`netScore`/`activeW` +
modulators into the schema-1.3 JSONL. *The training set now contains the model's INPUTS — the weight fit (§5.2), not just
the floor calibration (§5.1), is unblocked.* One deviation from §2.2: the vestigial `LabelBarrierTicks` constant was
**not** added — the recorder's ATR-scaled `FirstTouchBarrier()` (the §10.6 corollary's own recommendation) supersedes it.

**§10.2 — `EpisodeId` (DONE).** `CouncilState.EpisodeId = "<inst>-<yyyymmdd>-<NNNN>"`, bumped **only on a bias flip**;
published + recorded (`episodeId`) by both recorders.

**§10.9–10.10 — the actor INTERLOCK (DONE, Core v1.25.0).** `RegisterActor`/`UnregisterActor`/`AllActors` back an
`ActorReg` registry keyed by `instanceKey`. The **Bridge (v0.2.3)** derives `InstanceKey()` = `SentinelBridge#<scope>@<account>`
and its **ARM button REFUSES to arm on a collision** ("NAME TAKEN"), with a reference-checked release on disarm/Terminated
(the re-enable race). **This closes the exact hazard §10.11 targets** (two actors on one scope+account = managed-position
desync) — so the safety goal is met without the Arc rekey.

**§10.7 — Ledger context (DONE).** `Ledger.Order/Action/Fill` gained optional `episode`/`instance` params (additive); the
Bridge stamps both on every fire. `fill → episode → verdict` is now a real join.

**Bug #2 (SizedQuantity) — was ALREADY FIXED** in Bridge v0.2.2 (2026-07-10); verified this session.

**CBRK baseline → schema 1.3 (DONE, CompressionBase v1.3.2).** The CBRK per-sensor baseline now writes the ATR-scaled
first-touch label (`barrierTicks`/`barsToTargetR`/`barsToStopR`/`firstTouch`/`ftAmbig`), matching the Council corpus so the
two are comparable. Writer-only; `RecordExcursions` still default OFF; still lands in `Excursions\_baselines\cbrk\<schema>\`.

**⚠ CORRECTION — VolEnvelope has NO excursion writer.** Earlier notes ("VolEnvelope still 1.2") are **wrong**:
`VolEnvelope_v0_2_0` records nothing; the only per-sensor baseline writer in the tree is CompressionBase (CBRK). Giving
VolEnvelope first-touch baselines would be a **net-new recorder**, not an uplift — an open decision, not done.

**DEFERRED — Arc `_fleet[instanceKey]` (§10.11).** Not a mechanical rekey: Arc's fleet plan is *instrument+strategy*-scoped
while `instanceKey` is *scope+account*-scoped, so a faithful change reworks Arc's config model **and** GTrader21's
`SlotLive()` consult (order-adjacent). Its own focused step. The safety motive is already covered by the actor interlock.

---

## Changelog

- **2026-07-12 — v1.5** — added §11.5: **Phase 2 instrumentation SHIPPED** (bridge-verified, needs one F5). The decision
  VECTOR (§2.1–2.3), `EpisodeId` (§10.2), the actor INTERLOCK (§10.9–10.10 — Bridge refuses to arm on an instanceKey
  collision, which closes §10.11's hazard), Ledger episode/instance context (§10.7), and the CBRK baseline first-touch
  uplift. Corrected the VolEnvelope "still 1.2" error (it has no recorder). Bug #2 was already fixed in v0.2.2. Arc rekey
  deferred (genuine redesign). Live: Core v1.25.0 · Council v1.4.0 · Bridge v0.2.3 · CompressionBase v1.3.2.
- **2026-07-09 — v1.4** — added §11 implementation log. Corrected the bar tag (`69697v6x24`, numeric type id +
  `Value2`; `TBC6-24-69697` was a legacy filename artifact). Recorded that **scope alone cannot separate two charts
  sharing instrument + bartype** → per-publisher id + `SCOPE CONTENTION` detection + `ClearCouncilScope`. Documented
  the as-of guard and its measured proof (five rows over three days, `conviction=0.1541` to 4dp). Logged five bugs
  found while building, incl. the Eye never loading — which explains why the heaviest voter never voted.
- **2026-07-09 — v1.3** — **closed the actor-identity gap v1.2 wrongly declared solved.** `modelId`
  fingerprints the *Council's* config and says nothing about the thing that acted on it, so two Bridges on
  one scope with different TP/SL were indistinguishable. Added §10.9 (`instanceKey` + `policyId`; splitting
  policy from model is what makes A/B a *paired* comparison), §10.10 (`RegisterActor` — **the name is an
  interlock, not a label**: an `instanceKey` collision is precisely the managed-position hazard, so refuse
  to arm; plus the Realtime-register / reference-checked-unregister traps), §10.11 (Arc's `_fleet` is keyed
  by instrument too — `SlotLive("GC")` answers for every GC strategy at once), and **§10.12, the trader's
  interface**: usable with zero naming · alias ≠ identity · one human "Setup" name with machine-versioned
  eras inside it · name in the Cockpit where uniqueness is visible · block on `instanceKey` collision, warn
  on alias collision. Extends the Federated Naming Law with a fourth, **runtime** axis. Rebuilt the build
  order around the two correctness prerequisites (0a–0c) and the ten-minute `_tag` win.
- **2026-07-09 — v1.2** — added §10: identity, roster, and the multi-contract fleet. Diagnosed the
  actor/decider/episode collapse (the Bridge tags every order with a `const`, the verdict is stored as
  prose, and the excursion file shares no key — so Lens cannot actually join a fill to its verdict).
  Introduced `EpisodeId` (the join key), content-addressed `modelId` + write-once model cards (with the
  allowlist rule), the **declared roster** + `rosterMask` + weight-0 voters as the exploration primitive,
  **champion/challenger shadow scoring** as the way to explore without forking, and **partial-pooled joint
  fitting** with ATR-normalized features for the GC/NQ/ES/CL fleet. Revised the build order: identity work
  precedes modelling.
- **2026-07-09 — v1.1** — added §9 measured baseline from 1,258 live excursion rows. Four findings:
  bias chatter at 0.4-min cadence (effective N ≪ row count); Eye + Brick never vote (8 of 10
  coefficients estimable); conviction ranks outcomes slightly *backwards* on this sample; and the 1.2
  fallback label is empirically unusable, confirming `firstTouch` as the critical field. Corrected the
  §5.2 volume estimate — volume was never the constraint, independence is.
- **2026-07-09 — v1.0** — initial spec. Schema 1.3 decision-vector instrumentation; the three-lane
  fork model (in-tree behavior-neutral / offline Lab / flag-gated consumption); label definition with
  `firstTouch` + censoring + uniqueness weighting; purged walk-forward protocol; `Model.conf` artifact.
