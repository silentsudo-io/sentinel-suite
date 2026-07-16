# Sentinel Dataset Dictionary

**The authoritative reference for the syntax and nomenclature of every Sentinel dataset** — the
training corpus the Council learns from. If you are about to read, write, ingest, or fit against a
`.jsonl` under `Sentinel\Excursions\`, this is the map.

This doc is generated from live source (`BarsTypes\SentinelFlux_v1_0_0.cs`,
`AddOns\SentinelCore.SystemBuilder.cs` `VoterCatalog`, `Indicators\Council_v1_0_0.cs`, and
`Sentinel\Lab\`). When those change, regenerate. Related: [ML spec](SENTINEL_ML_SPEC.md) ·
[Flux bars spec](SENTINEL_FLUXBARS_SPEC.md) · [Process Atlas](SENTINEL_PROCESS_ATLAS.html).

---

## 1 · The naming grammar

Everything keys off a **scope**. A scope is *one chart's worth of decision context* — exactly the
coordinate a model is defined over.

```
GC  .  212201v12x48  @  STB24FCA
─┬─    ─────┬──────     ───┬────
inst      bartag          lane
└──────── scope ──────────┘        scope = inst . bartag [ @lane ]
```

| Token | Example | Meaning |
|---|---|---|
| **inst** | `GC` | Master instrument (the NT `MasterInstrument.Name`). |
| **bartag** | `212201v12x48` | The *bar construction* = `<BarsPeriodTypeId>v<Value>x<Value2>`. |
| **@lane** | `@STB24FCA` | Optional human-named **per-chart lane**. Absent ⇒ bare scope. Sanitized to letters/digits. |

**Why bartag and not "5-minute":** bar granularity determines label optimism. A Renko-labelled row
and a minute-labelled row do not describe the same world, so the Lab **never pools bartypes** — the
bartag is a hard partition key, not a cosmetic tag.

Two IDs stitch a single fire's records together across files:

| Key | Example | Grammar | Role |
|---|---|---|---|
| **episodeId** | `GC-20260715-0053` | `inst-yyyyMMdd-####` | Join key across corpus row ⇄ tick sidecar ⇄ Ledger. |
| **fireId** | `20260715T175912741_GC_L_29` | `ts(ms)_inst_<L\|S>_<sessionSeq>` | Unique per fire; also the tick-sidecar filename stem. `L`/`S` = long/short. |

---

## 2 · Bar-type ID registry

The `212200–212299` block is **reserved for the Sentinel bar-type family** (declared in every
Sentinel bars header). Those three are the ones that build a real corpus:

| Id | Tool | `vNxM` decode | Notes |
|---|---|---|---|
| **212201** | **SentinelTBars** | `v(SS/2)x(SS×2)` — `v6x24` = Speed Setting **12** | The adaptive HA/Renko brick engine. Also publishes `BrickState` → the `BRK` voter. |
| **212202** | **SentinelTbarsCount** | same speed pair (`v6x24`) | Plain brick + ticks-to-next-brick HUD. |
| **212203** | **SentinelFlux** | `v8` = imbalance threshold scale | Order-flow imbalance bars (López de Prado × TBars). Publishes `FluxState` → the `FLUX` voter. |
| 212204+ | *(reserved)* | — | Future Sentinel bars. |

**Other IDs seen in older corpus files** (present for completeness — these are not the Sentinel
family and several are frozen/legacy):

| Id | Tool | Source |
|---|---|---|
| `0` | Tick (native NT) | `0v150x1` = a **150-tick** chart (`Value`=ticks, `Value2`=1). |
| `2016` | ERP_Type_Bars | `BarsTypes\ERP_Type_Bars.cs` |
| `54321` | EdsRetraceBarsV2 | `BarsTypes\EdsRetraceBarsV2.cs` |
| `2018` | ReversalRenko | `BarsTypes\ReversalRenko.cs` |
| `2085` | RenkoATR | `BarsTypes\RenkoATRBarsType.cs` |
| `69696` / `69697` | TBarsElse / TbarsCount (legacy) | pre-Sentinel TBars lineage |
| `69700–69799` | TBarsNext family | reserved block |
| `212121–212123` | TbarsSudo v1/2/3 | frozen checkpoints |

---

## 3 · The three dataset families

All live under `Sentinel\Excursions\`. **One writer, one schema per folder** — the Council recorder
(`SentinelExcursionRecorder_v2_0_0`) owns the `council\` tree; legacy per-sensor recorders default OFF.

| Path | Family | Filename grammar | Written when |
|---|---|---|---|
| `council\1.3\` | **Labeled corpus** (schema `1.3`) | `<sessionStart>__<inst>__<bartag[@lane]>.jsonl` | One row **per fire**, streamed on 60-min window completion (`endReason="window"`). |
| `council\ticks\` | **Raw-tick path** (schema `ctick.1`) | `<fireId>.jsonl` = `<ts_ms>_<inst>_<L\|S>_<seq>.jsonl` | One file **per fire**: a header line + the tick tape from fire to resolution. |
| `Excursions\` root | **Legacy per-sensor baselines** | `<sessionStart>__<inst>__<bartag>.jsonl` | CBRK (CompressionBase) first-touch baseline; default off elsewhere. |

**Filename anatomy** (corpus): `20260715T090945__GC__212201v12x48@STB24FCA.jsonl`
- `20260715T090945` — when the **recorder instance loaded** (session), `yyyyMMddThhmmss`. *Not* per-row.
- `__` — double-underscore delimiter.
- `212201v12x48@STB24FCA` — the bartag, carrying `@lane` when the chart is laned.
- `.jsonl` — **one JSON object per line**.

---

## 4 · Schema-1.3 corpus row

One row = one Council fire and everything that happened to it. Fields group into four blocks.

### 4a · Identity / keying

| Field | Example | Meaning |
|---|---|---|
| `schema` | `"1.4"` | Row schema version. **`1.4` = `1.3` + the provenance block (`recVer`/`coreVer`/`barLabel`).** `1.4` rows live in `council\1.4\`; the frozen pre-provenance `1.3` corpus stays in `council\1.3\`. **Never pool schemas across a fit.** |
| `recVer` / `coreVer` / `cnclVer` | `"2.1.5"` / `"1.36.0"` / `"1.8.0"` | **PROVENANCE (schema ≥ 1.4).** Which recorder + SentinelCore + **Council** LOGIC wrote the row, so a logic change can't pool old/new rows invisibly. `cnclVer` is finer than `coreVer` (it catches a Council-only change that didn't bump Core). **A null/absent `coreVer` = pre-provenance (old logic); a null `cnclVer` on a 1.4 row = pre-v2.1.5.** |
| `barLabel` | `"SentinelFlux 8"` | **Human bartag** (schema ≥ 1.4) — the display name of `bartype`, denormalized for readers. Machine key stays `bartype`; see §2. |
| `kind` | `"excursion"` | Discriminator (ingesters skip non-`excursion` lines). |
| `signal` | `"COUNCIL"` | Which decision source emitted it. The Lab fits only `COUNCIL` rows. |
| `inst` / `bartype` | `GC` / `212201v12x48@STB24FCA` | Partition keys (bartype carries `@lane`). The **machine key** — never changed; render for humans via `SentinelCore.FriendlyBartag`/`FriendlyScope` → `barLabel`. |
| `episodeId` | `GC-20260715-0053` | Join key to the Ledger. NB: per-episode, **not** per-fire (a fire↔sidecar 1:1 join uses `inst`+`fireTime`+`firePx`+`dir`). |
| `fireTime` / `firePx` | `2026-…T16:25:21Z` / `4043` | Entry timestamp (UTC) + entry price. ⚠ **Realtime-only rule (§9): a `fireTime` far in the past = a REPLAY leak** — the `corpus_probe` reports these as `stale_dated_rows`. |

### 4b · The decision — what the Council said at fire

| Field | Example | Meaning |
|---|---|---|
| `dir` | `-1` | Side: `+1` long, `-1` short, `0` flat. |
| `conviction` | `0.5976` | Signed-agreement magnitude, `0–1`. `= |netScore| / denom`. |
| `convBucket` | `"MID"` | `LOW` / `MID` / `HIGH`. |
| `sizeMult` | `0.5079` | Context-damped size multiplier, `0–1`. `0` = vetoed / below floor. |
| `voters` / `agree` / `disagree` | `10` / `4` / `2` | Roster tally. |
| `netScore` | `-2.45` | `Σ(voteᵢ × wᵢ)` — the raw fusion score. |
| `activeW` | `4.65` | The kind-aware denominator conviction was divided by. |
| `votes{}` | `{EYE:0, TRND:-1, …}` | **Per-voter direction** `-1/0/+1`. The Phase-2 feature vector. |
| `voteW{}` | `{EYE:1.4, CCI:1.2, …}` | **Per-voter weight** at fire (Roster.conf / F6 override of the catalog default). |
| `reasons` | `EYE~ TRND▼ … denom 4.1/7.8` | Human audit string (see §6). |

### 4c · Context modulators at fire

| Field | Meaning |
|---|---|
| `regime` / `adx` | Trend-vs-chop label + ADX value. |
| `clockPhase` | `0` closed · `1` opendrive · `2` midday · `3` close. |
| `rvol` | Time-normalized relative volume. |
| `mtfBias` | Higher-timeframe ladder consensus `-1/0/+1`. |
| `levelInPath` / `levelName` | A structural level sits in the trade's path (headwind). |
| `eyeHad` / `eyeScore` / `eyeDir` / `eyeAligned` | Eye-specific legacy fields (may be null). |

### 4d · The OUTCOME / label — what happened after (all excursions in **ticks**)

| Field | Meaning |
|---|---|
| `maxMFE` / `maxMAE` | Max favorable / adverse excursion over the window. |
| `barsToMFE` / `barsToMAE` · `msToMFE` / `msToMAE` | When those maxima occurred. |
| `mfe1 mae1 … mfe60 mae60` | MFE/MAE at the **1 / 5 / 15 / 60-min** milestones. |
| `barrierTicks` | The ATR-scaled ±1R barrier used for first-touch. |
| `barsToTargetR` / `barsToStopR` | Bars to touch `+1R` / `-1R` (`-1` = never). |
| **`firstTouch`** | **THE label**: `+1` target hit first · `-1` stop first · `0` neither by window end. |
| `ftAmbig` | Both barriers touched the **same bar** (ambiguous — resolved pessimistically to loss). |
| `endReason` / `endTime` | `"window"` = clean 60-min completion. |

---

## 5 · Tick sidecar (`ctick.2`)

A two-part file, one per fire. **Line 1 = header** (join keys + tick-resolution outcome), **lines 2…N =
the path**. (`ctick.2` = `ctick.1` + the `recVer`/`coreVer`/`barLabel` provenance block, matching the row
schema-1.4 bump; `ctick.1` sidecars stay valid and load unchanged.)

Header fields of note: **`recVer`/`coreVer`/`barLabel`** (provenance, ctick.2) · `fireId` · `episodeId` ·
`scope` · `firePx` · `conviction`/`sizeMult` · `barrierTicks` · `maxFavTicks`/`maxAdvTicks` ·
`msToMaxFav`/`msToMaxAdv` · `msToTargetR`/`msToStopR` · **`firstTouchTick`** / `ftAmbigTick` ·
`ticks` (count) · `trunc` (buffer overflowed).

Path rows: `{"ms": <ms since fireTime>, "px": <last-trade price>}`.

> The row's `firstTouch` (bar resolution) and the sidecar's `firstTouchTick` (tick resolution) are the
> **same label at two fidelities**. The Lab prefers `firstTouch`; the tick path is for grading fill
> quality and `msToFirstTouch`.

---

## 6 · Decoding the `reasons` audit string

Built in `Council_v1_0_0.BuildReasons()`. Example:

```
EYE~ TRND▼ CCI▼ ADX▼ ENV▼ BRK▲ CMP~ IMKT▲ WAE~ GREV~ STF▼* · clk:Midday · vol×1.3 · roster 10/10 ?STF · denom 4.1/7.8
```

| Token | Meaning |
|---|---|
| `TAG▲` / `TAG▼` / `TAG~` | Voter direction: bullish (`Dir>0`) / bearish (`Dir<0`) / neutral (`Dir=0`). |
| `TAG*` | **Heard but NOT counted** — `w=0` explorer, or undeclared (drift). |
| `· clk:<phase>` | Clock phase (Closed/OpenDrive/Midday/Close). |
| `· vol×N` | RVOL size multiplier. |
| `· vs-MTF` | Higher-timeframe ladder disagrees with the side. |
| `· into <level>` | A structural level is in the path. |
| `· chop N` | STF chop reading triggered the chop veto. |
| `· in-value` · `· hi-vol` | In value-area · high-volatility regime. |
| `· +flowDiv` / `-flowDiv` · `· flux:absorb` / `· flux▲/▼` | Flow-divergence / Flux absorption or flow direction. |
| `· roster P/D` | **P**resent of **D**eclared voters. |
| `⚠TAG,…` | Declared but **MISSING** (crashed/stale/not loaded). |
| `?TAG,…` | Present but **UNEXPECTED** (loaded, not in `Roster.conf` → excluded from fusion). |
| `· denom X/Y` | Effective **kind-aware** denominator `X` vs static declared weight `Y`. `conviction = |netScore|/X`. |

---

## 7 · The voter catalog

The source of truth is `SentinelCore.SystemBuilder.VoterCatalog`, which the Council **emits on load**
to the shared file **`Sentinel\Models\catalog.conf`** (`tag|role|kind|defWeight|display|seam`, one line
each). The Python Lab reads that file, so both sides build the fit's feature columns from the *same*
map — no more hardcoded voter list drifting behind the Council. **Weights below are catalog DEFAULTS** —
a scope's `Roster.conf` (and the Council's F6) override them (e.g. live `GC` runs `CCI` at `1.2`, not
`0.8`), and the Lab's fit baseline uses the **recorded `voteW{}`** per scope, not these defaults. `KIND`
sets how a voter counts toward the conviction denominator: **STATE** always dilutes; **TRIGGER** dilutes
only when it fired or is absent (a quiet trigger is *absence of evidence*, not evidence against).

### 7a · Weighted voters (the `Roster.conf` surface)

| Tag | Display | Kind | Def w | Seam | Note |
|---|---|---|---|---|---|
| `EYE` | Eye | Trigger | 1.4 | `EyeVerdict` | GodTrades qualifier — strongest single voice. |
| `TRND` | SentinelTrend | State | 1.0 | `TrendState` | Structural trailing-line trend. |
| `CCI` | Woodies CCI | State | 0.8 | `CciState` | Woodies CCI bias (×1.5 strong). |
| `ADX` | ADX Pro | State | 0.6 | `AdxState` | Regime/strength confirmer. |
| `ENV` | Vol Envelope | State | 0.6 | `EnvelopeState` | Trend regime + drives the squeeze modulator. |
| `BRK` | Brick | State | 0.5 | `BrickState` | Adaptive brick micro-trend (from the SentinelTBars **bar type**). |
| `CMP` | Compression | Trigger | 0.7 | `CompressionState` | Held breakout off a compression base. |
| `IMKT` | Intermarket | State | 0.6 | `IntermarketState` | Correlated-instrument lean (instrument-keyed). |
| `WAE` | WAE | Trigger | 0.7 | `WaeState` | Waddah-Attar momentum-explosion breakout. |
| `GREV` | God Reversal | Trigger | 0.9 | `GodReversalState` | Candle-grammar reversal (mean-reversion voice). |
| `STF` | Stoch Filter | State | 0.0 | `StfState` | Gaussian midline slope. `w=0` = exploration; drives the chop veto. |
| `FLOW` | Flow | State | 0.9 | `FlowState` | Tick-rule CVD regime — not price-derived. |
| `STRC` | Structure | State | 0.7 | `StructureState` | Swing HH/HL·LH/LL structure. |
| `EXH` | Exhaustion | Trigger | 0.5 | `ExhaustionState` | Leledc reversal (mean-reversion). |
| `AVMA` | ADXVMA | State | 0.6 | `AdxvmaState` | ADX-vol adaptive-MA trinary trend. |
| `SPRT` | SuperTrend | State | 0.7 | `SuperTrendState` | ATR-band trailing flip (always ±1). |
| `PSAR` | Parabolic SAR | State | 0.5 | `SarState` | Wilder SAR trend/stop (always ±1). |
| `ZSC` | Z-Score | Trigger | 0.4 | `ZScoreState` | `(Close−SMA)/σ` fade voice. |
| `ARCH` | Trend Architect | State | 0.7 | `TrendArchitectState` | Composite PRISM trend + regime gate. |
| `VDYA` | VIDYA | State | 0.5 | `VidyaState` | Chande-CMO adaptive-MA trend. |
| `HARM` | Harmonic | Trigger | 0.4 | `HarmonicState` | XABCD pattern completions (reversal). |
| `FLUX` | Flux | State | 0.7 | `FluxState` | Net order-flow direction of the imbalance bar (from the SentinelFlux **bar type**). |

### 7b · Context axes (consulted, NOT in `Roster.conf` — toggled via the Council's `Consult*` settings)

| Tag | Display | Role | Seam | Note |
|---|---|---|---|---|
| `CLOCK` | Clock | Modulator | `ClockState` | Session phase / mins-to-close (instrument-keyed). |
| `PARTIC` | Participation | Modulator | `ParticipationState` | RVOL + climax/dry-up (**scope-keyed — load on every Council chart**). |
| `MTF` | MTF | Modulator | `MtfState` | HTF consensus ladder (counter-HTF penalty). |
| `LOC` | Location | Veto | `LevelState` | VWAP/PDH-PDL/OR/IB levels in the path. |
| `LIQ` | Liquidity | Veto | `LiquidityState` | Order-flow absorption walls. |

---

## 8 · How `train.py` consumes it

`Sentinel\Lab\train.py` — the offline fitter. Nothing here touches `bin\Custom`; it reads the corpus
and emits `Sentinel\Model.candidate.conf` (gated to `Model.conf` only with `--promote`).

```
python train.py --inst GC --bartype 212201v6x24 --barrier 20 --cost 1.5
```

**Pipeline** (`dataset.py` → `labels.py` → `train.py`):

1. **Load** — `load_jsonl(council\<schema>\)` reads every row where `kind=="excursion"`, then
   **filters by `inst` + `bartype`**. *Never pools bartypes* (§1). `council_rows` keeps `signal=="COUNCIL"`.
2. **Label** — `make_labels`: prefers **`firstTouch`** (`+1→WIN`, `-1→LOSS`, `0→CENSORED`); for old 1.2
   rows falls back to the pessimistic `mfeN/maeN` milestone. Censored rows are dropped from the fit.
3. **Uniqueness weight** — `uniqueness_weights` (AFML ch. 4): two fires 90 s apart share most of their
   forward window and are **not** independent. `effective_n = Σ(avg uniqueness)` is the N you may quote.
4. **Fold by direction** — the model feature is `x_i = voteᵢ × dir`: "did the voter agree with the
   *taken* side." Halves the feature space and makes a fitted coefficient directly comparable to
   `WeightEye` — it drops straight into `Model.conf`.
5. **Phase 1 — calibration** (needs only `conviction`, so schema 1.2 is enough): Platt-scale
   `conviction → P(win)` under **purged walk-forward CV**; `expectancy_floor` = the lowest conviction
   whose *calibrated* win-prob clears breakeven after cost → **the learned `ConvictionFloor`** (replaces
   the hand-set `0.20`). `nan` = "no conviction level pays" — a real answer.
6. **Phase 2 — weights** (needs ≥150 rows carrying the `votes{}` vector): the voter set is **data-driven
   per fit** — `active_voter_tags` admits the catalog voters this bartype actually records above a
   support threshold (`max(30, 5% of vector rows)`) with ≥1 non-neutral vote, in catalog order. Rosters
   range 2→18 voters across bartypes, so a fixed list would drop real voters (e.g. STF) or fit all-zero
   columns; under-supported and undeclared tags are reported, not silently dropped. Then ridge-logistic
   on the folded features + orthogonal axes (`clockPhase`, `log_rvol`, `mtfBias×dir`, `levelInPath`,
   `activeW`, `voters`); grid over `C`; OOF AUC vs the **recorded-`voteW` baseline**. Fitted `w.TAG`
   values rescaled to the current weight total (conviction is scale-invariant) → the learned weights.
7. **Emit + gate** — writes `status=ok|reject` (`reject` if no profitable floor, or the fit didn't beat
   the hand weights). `--promote` **refuses** to overwrite the live `Model.conf` on `reject`.

### Which fields feed which stage

| Field(s) | Consumed by |
|---|---|
| `inst`, `bartype` | Partition (never pooled). |
| `firstTouch` / `mfeN`,`maeN`,`barrierTicks` | Label (`y`). |
| `fireTime` + resolution time | Uniqueness weight / purged CV windows. |
| `conviction` (or recomputed `netScore/activeW`) | Phase 1 calibration + the floor. |
| `votes{}`, `dir` | Phase 2 folded feature vector. |
| `clockPhase`, `rvol`, `mtfBias`, `levelInPath`, `activeW`, `voters` | Phase 2 orthogonal-axis features. |
| `votes{}` keys | Data-driven voter set (which `w.TAG` columns the fit builds). |
| `voteW{}` | Per-scope baseline weight (median) + compared against the fitted `w.TAG`. |

---

## 9 · Quick rules

- **Never round-trip a `.jsonl` through PowerShell `Get-Content`/`Set-Content`** — it double-encodes
  UTF-8 (`▼`→`â–¼`). The files are clean UTF-8; the mangling is a display artifact of the cp1252 console.
- **Never pool bartypes or schemas** in a fit — different bar construction = different label optimism.
- **Never pool `coreVer`s** (schema ≥ 1.4) — a different SentinelCore/recorder version = potentially different
  fusion logic. A null `coreVer` = pre-provenance `1.3`; treat it as its own (older, unknown-logic) cohort.
- **One writer per corpus folder.** The Council recorder owns `council\`; legacy recorders stay OFF.
- A `.jsonl` row is only trustworthy when written in `State.Realtime` — replayed (historical) verdicts carry
  lookahead. ⚠ **This is NOT self-enforcing:** the schema has no realtime flag, so a replay leak (a `fireTime`
  far in the past) sits in the corpus looking identical to a live row. **Verify with `Lab\health\corpus_probe.py`**
  (`stale_dated_rows`), and window-scope the fit by `fireTime` — do not assume the folder is clean.

## 9a · Proving the corpus is trustworthy — `corpus_probe.py`

`Lab\health\corpus_probe.py` (read-only, mirrors `probe.py`) is the "prove the recording" surface — it never
opens the corpus at record time, so nothing else validates it. Run `python corpus_probe.py` (or `--loop 300`).
It writes `corpus_integrity` / `corpus_folder` / `corpus_events` to `sentinel.db` (Grafana-readable) with:
**reconciliation** (Ledger fires ↔ excursion rows ↔ tick sidecars, joined on `inst+fireTime+firePx+dir`; a
`recon_gap`, not pass/fail) · **schema hygiene** (mixed-schema / unexpected-for-folder, `rows_13` vs `rows_14`) ·
**provenance coverage** (`prov_coverage_pct` + distinct `coreVer`s — a silent logic change shows as two versions
in the pool) · **silent-loss** (`trunc` sidecars, malformed lines) · **`stale_dated_rows`** (the realtime-leak
detector). First live run (2026-07-16) flagged **~7,671 stale-dated rows (~83% of the 1.3 corpus, oldest
`fireTime` 2025-12-09)** — a replay/backfill leak that had been invisible.

## 9b · The DB warehouse (`sentinel.db`) columns

`ingest.py` reads BOTH `council\1.3\` and `council\1.4\` (+ `ctick.1`/`ctick.2` sidecars). The `trades` table
carries provenance (`rec_ver`, `core_ver`, `bar_label`, `scope`) + the context that used to be dropped
(`regime`, `adx`, `conv_bucket`, `agree`, `disagree`, `voters`, `end_reason`, and the milestone curves +
`barsTo*` folded into one `milestones_json` blob). Indexed on `(inst,bartype)`, `episode_id`, `src`, `schema`,
`bartype`, `entry_utc`. The ingester now prints a **drop tally by reason** (silent loss made visible; a
`row_no_votes` count = legacy pre-instrumentation rows). `train.py` reads the JSONL directly (`--schema 1.4`),
not the DB — the DB is the derived warehouse (see `SENTINEL_DB_MIGRATION_SPEC`).
