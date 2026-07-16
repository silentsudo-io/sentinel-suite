# VolEnvelope — Build Spec (v0.1.0)

**An honest volatility envelope.** A ground-up rewrite of Bollinger Bands that answers the
question BB *can't*: **"given this regime, how far can price plausibly go, how confident am I in
that, and does a band touch here mean fade or follow?"**

- **File / class / `Name`:** `VolEnvelope_v0_1_0`
- **Namespace:** `NinjaTrader.NinjaScript.Indicators.Sentinel` (→ clusters under the "Sentinel"
  picker folder)
- **Lane:** Edge (observes; no orders). Advisory-only.
- **Reference implementation to mirror:** `Indicators\CompressionBase_v1_3_0.cs` (skeleton, render
  card, label-remover, versioning). Design rules: `Docs/SENTINEL_DESIGN_SYSTEM.md` §1/§3/§4b/§7.

---

## 0. Why this exists — the seven BB gaps it closes

| # | Bollinger Bands flaw | VolEnvelope fix |
|---|---|---|
| 1 | `2σ` assumes Gaussian; fat tails breach far more than 5% | **Empirically-calibrated width** — multiplier is the real quantile of standardized returns for *this* instrument |
| 2 | Symmetric around the mean; markets are skewed | **Asymmetric bands** — separate upside/downside semi-deviation |
| 3 | SMA "drop-off" jerks the band when an old bar leaves the window | **EWMA center** — recency-weighted, no cliff |
| 4 | Close-only stdev lags the regime change you actually care about | **Range-based vol** (Yang-Zhang / Rogers-Satchell) — uses full OHLC, reacts sooner |
| 5 | No notion of regime — can't tell a fade-touch from a trend-walk | **Native regime state** (SQUEEZE / RANGE / TREND / EXPANSION) + **trend-aware %b** |
| 6 | Draws the vol estimate as if it were exact | **Error band** — SE(σ) drawn as a faint band-of-band |
| 7 | A snapshot of where price *has been*, not where it's *going* | **Forward cone** — probabilistic projection h bars ahead |

---

## 1. Math (the engine)

All vols are computed on **log returns of typical price** unless noted. Let `TP = (H+L+C)/3`,
`r_t = ln(TP_t / TP_{t-1})`. Config period `P` (default 20), calibration lookback `L` (default 500).

### 1.1 Center — EWMA (fixes gap #3)
```
alpha = 2 / (P + 1)
mid_t = alpha*TP_t + (1-alpha)*mid_{t-1}      // seed mid = SMA(TP, P) at first valid bar
```
Optional robustness: `mid = 0.5*EWMA(TP) + 0.5*Median(TP, P)` behind a `RobustCenter` flag
(default off — EWMA alone is the baseline).

### 1.2 Volatility — Yang-Zhang, RS fallback (fixes gap #4)
Per-bar estimators (drift-independent, use the whole bar):
```
o = ln(O_t / C_{t-1})            // overnight / inter-bar gap
u = ln(H_t / O_t),  d = ln(L_t / O_t),  c = ln(C_t / O_t)
RS_t = u*(u-c) + d*(d-c)         // Rogers-Satchell, per bar
```
Yang-Zhang combines overnight, open, and RS variances over window `P`:
```
sigmaO²  = Var(o, P)                         // overnight variance
sigmaC²  = Var(c, P)                         // open-to-close variance
sigmaRS² = mean(RS, P)
k        = 0.34 / (1.34 + (P+1)/(P-1))
sigmaYZ² = sigmaO² + k*sigmaC² + (1-k)*sigmaRS²
sigma    = sqrt(sigmaYZ²)                     // per-bar return stdev
```
**Fallback:** when bars carry no meaningful gap (`|o|` ~ 0 across the window → tick/Renko/constant-
volume bars), drop the overnight term and use **Rogers-Satchell only**: `sigma = sqrt(mean(RS,P))`.
Expose the choice as `VolMode { YangZhang, RogersSatchell, Auto }`, default `Auto`.

### 1.3 Empirical, asymmetric width (fixes gaps #1 & #2)
Standardize returns and calibrate the multiplier from the **actual tail**, per side, over `L`:
```
z_t = r_t / sigma_t
multUp   = Quantile( { z : z > 0 }, q )       // q default 0.95
multDown = Quantile( { |z| : z < 0 }, q )
```
Semi-deviations for the per-side scale:
```
sigmaUp   = sqrt( mean( r² for r>0, P ) )
sigmaDown = sqrt( mean( r² for r<0, P ) )
```
Bands (asymmetric, in price):
```
upper = mid + mid * sigmaUp   * multUp
lower = mid - mid * sigmaDown * multDown
```
> `mid * sigma` converts a log-return vol into a price offset (first-order). Keep `q` a property so
> the user reads "the band contains q of history for THIS instrument," not a hardcoded 95%.

### 1.4 Regime state (fixes gap #5)
```
bandwidth      = (upper - lower) / mid
bwPctile       = PercentRank(bandwidth, over last B bars)   // B default 125
slope          = (mid_t - mid_{t-P/2}) / (mid * ...)        // normalized center slope
adx            = ADX(AdxPeriod)[0]                          // regime tag only
```
Regime enum (priority order):
```
SQUEEZE     if bwPctile <= squeezePctile (default 0.20)
EXPANSION   if bwPctile >= 0.80 AND bandwidth rising
TREND_UP    if adx >= trendAdx (default 25) AND slope > 0
TREND_DOWN  if adx >= trendAdx AND slope < 0
RANGE       otherwise
```

### 1.5 Trend-aware %b (fixes gap #5, the false-signal killer)
```
percentB   = (Close - lower) / (upper - lower)     // classic, still exposed
stretch    = signed distance beyond the near band in units of that side's sigma:
             above: (Close - upper) / (mid*sigmaUp)   when Close>upper else 0
             below: (Close - lower) / (mid*sigmaDown) when Close<lower else 0
isExtreme  = |stretch| > 0 AND regime ∈ {RANGE}      // fade-worthy
isRiding   = |stretch| > 0 AND regime ∈ {TREND_UP,TREND_DOWN}  // follow, NOT a reversal
```
This is the whole point: a band breach in RANGE is `isExtreme` (mean-revert candidate); the *same*
breach in a TREND is `isRiding` (continuation). BB conflates them.

### 1.6 Error band — uncertainty of the estimate (fixes gap #6)
Standard error of a stdev from `n` effective samples: `SE(sigma) ≈ sigma / sqrt(2n)`. EWMA effective
sample count `n_eff ≈ (2-alpha)/alpha`. Draw a faint band-of-band:
```
upperHi/Lo = upper ± mid * multUp   * SE(sigmaUp)
lowerHi/Lo = lower ± mid * multDown * SE(sigmaDown)
```
Right after a regime flip `n_eff` is small → the fuzz visibly widens. That fuzz *is* the honesty.

### 1.7 Forward cone (fixes gap #7)
Project `h` bars ahead (`ForecastBars`, default 10) from the last bar. Center drifts at `slope`
(or flat if `ConeFlat`); half-width grows with the √-of-time rule:
```
for j in 1..h:
    midF_j   = mid + slope * j                 // or just mid if ConeFlat
    hwUp_j   = mid * sigmaUp   * multUp   * sqrt(j)
    hwDn_j   = mid * sigmaDown * multDown * sqrt(j)
    upperF_j = midF_j + hwUp_j ;  lowerF_j = midF_j - hwDn_j
```
Rendered as a translucent polygon to the RIGHT of the last bar (see §3.3).

---

## 2. Plots & consumable surface

Three plots via `AddPlot` (use `PlotStyle.Line` for bands; **`PlotStyle.Dot` if any value-jump
artifact appears** — see `ninjascript-plot-config-override` memory):
```
Values[0] Upper   (up-tinted line)
Values[1] Lower   (down-tinted line)
Values[2] Mid     (mute/ink line)
```
Consumable "current" surface (all `[Browsable(false)] [XmlIgnore]`, for strategies/Copier to read):
```
double  Upper, Lower, Mid                 => Values[0/1/2][0]
double  Bandwidth, BandwidthPctile
double  PercentB, Stretch
double  SigmaReturn, MultUp, MultDown
EnvRegime Regime                          // enum below
bool    IsSqueeze  => Regime==SQUEEZE
bool    IsExtreme, IsRiding
```
Historical series for backtest/recorder consumers (allocated in `DataLoaded`):
`Series<double> RegimeSeries, StretchSeries, BandwidthSeries`.

Custom enum — **declare in the class's own `Indicators.Sentinel` namespace** and `using` it at file
top (generated host-wrapper shares the file's usings, references the enum bare — see
`sentinel-namespace-and-naming`):
```csharp
public enum EnvRegime { Squeeze, Range, TrendUp, TrendDown, Expansion }
public enum VolMode   { Auto, YangZhang, RogersSatchell }
```

---

## 3. Rendering

### 3.1 Bands + error fuzz (price panel)
Bands come off the three plots. Draw the error band as two faint filled regions
(`Draw.Region` between `upperHi/upperLo` and between `lowerHi/lowerLo`) in `CFaint`/`CLine` — or,
cleaner and thread-safe, paint them in `OnRender` as low-alpha SharpDX rects. Keep alpha low
(~20/255) so the solid band lines dominate.

### 3.2 The Sentinel glass card (HUD) — mirror CompressionBase
Field `private SentinelSkin.Painter _sp;`, lazily built on first render, whole body in `try/catch`,
docked anti-overlap via `CardLayout.Place`. Card contents:
```
header:  Dot(live=regime!=Squeeze? but "watching" is always cyan) + "VOL ENVELOPE" + Pill(regime)
hero:    %b as a big number OR the Stretch value; color = CAccent watching,
         CUp/CDown only when isExtreme/isRiding carries direction
track:   BandwidthPctile as the Track fill (cyan) — shows squeeze visually (low = coiled)
rows:    "σ {sigma:0.000}  mult ↑{multUp:0.0} ↓{multDown:0.0}"   (mono)
         "regime {REGIME}   %b {percentB:0.00}   stretch {stretch:+0.0}σ"
         BarTag()
```
Regime → pill color: `Squeeze`=CWarn (amber, coiled/caution), `Range`=CMute, `TrendUp`=CUp,
`TrendDown`=CDown, `Expansion`=CAccent. **Cyan stays the "watching" accent; green/red only ever
attach to direction/money** (design rule §0).

### 3.3 Forward cone (OnRender, right of last bar)
Map projected values to pixels with `chartScale.GetYByValue(v)` and
`chartControl.GetXByTime(...)` (or extrapolate X by bar-width from the last two bars). Build a
`SharpDX.PathGeometry` polygon `upperF[1..h]` forward then `lowerF[h..1]` back; fill at ~28/255
alpha `CAccent`, stroke the two edges at ~120/255. Gate behind `ShowCone` (default on).

### 3.4 Cleanup
`Terminated`: `_sp?.Dispose()`, `SentinelSkin.CardLayout.Release(this)`.

---

## 4. Properties (`[NinjaScriptProperty]`, grouped)

| Group | Property | Type | Default | Range |
|---|---|---|---|---|
| Center | `Period` | int | 20 | 2.. |
| Center | `RobustCenter` | bool | false | |
| Volatility | `VolMode` | VolMode | Auto | |
| Calibration | `CalibrationLookback` | int | 500 | 50.. |
| Calibration | `Quantile` | double | 0.95 | 0.80–0.999 |
| Regime | `SqueezePctile` | double | 0.20 | 0.05–0.50 |
| Regime | `BandwidthWindow` | int | 125 | 20.. |
| Regime | `TrendAdx` | int | 25 | 10–50 |
| Regime | `AdxPeriod` | int | 14 | 2.. |
| Forecast | `ShowCone` | bool | true | |
| Forecast | `ForecastBars` | int | 10 | 1–100 |
| Forecast | `ConeFlat` | bool | false | |
| Display | `ShowInfo` | bool | true | |
| Display | `ShowErrorBand` | bool | true | |
| Display | `CardCorner` | SentinelCardCorner | TopRight | |
| Sentinel | `PublishRegime` | bool | false | (see §5) |
| Sentinel | `ShowIndicatorLabel` | bool | false | (Order=100) |

**Label remover (mandatory):** `Name = "VolEnvelope_v0_1_0"` in `SetDefaults`; **first line of
`DataLoaded`:** `if (!ShowIndicatorLabel) Name = string.Empty;`.

---

## 5. SentinelCore integration (consult + optional publish)

- **Consult (always):** color the trend context by cross-checking `SentinelCore.GetEyeVerdict(instr, 0)`
  — if Eye's `Direction` agrees with the regime, tint the pill; else keep neutral. Same pattern
  CompressionBase uses. Wrapped in `try/catch`.
- **Publish (opt-in, `PublishRegime`) — BUILT in v0.2.0:** `SentinelCore.SetEnvelopeState(instr, regime,
  stretch, bwPctile, multUp, multDown, source)` + `GetEnvelopeState(instr, maxAgeSec)` / `AllEnvelopeStates()`,
  mirroring the `EyeVerdict` publish/consult seam. `EnvelopeState` carries regime as an **int**
  (0=Squeeze 1=Range 2=TrendUp 3=TrendDown 4=Expansion) so SentinelCore never couples to the indicator's
  enum; convenience `IsSqueeze`/`IsTrend`. VolEnvelope calls it each bar in `OnBarUpdate` when
  `PublishRegime` is on (guarded try/catch, keyed by master-instrument name). **Consumers** (Copier/Arc/
  strategies): `var s = SentinelCore.GetEnvelopeState("GC", 0); if (s != null && s.IsSqueeze) …` to gate.
  *(v0.1.0 shipped the flag as a no-op; v0.2.0 wired it. v0_1_0 is frozen in `…\_archive\Indicators`.)*

**Fail policy:** advisory-only indicator — never blocks anything. Regime is *advice*; amber = caution.

---

## 6. Build order (phased — build closed, validate live)

Full vision is the target, but land it in verifiable slices so each can be F5-checked:

1. **Core envelope** — EWMA center + YZ/RS vol + asymmetric empirical bands (3 plots). *This alone
   is already a strictly-better BB.* Verify bands render, no NaN storms on warmup.
2. **Regime + trend-aware %b** — enum, `bwPctile`, `stretch`, `isExtreme/isRiding` + the glass card.
3. **Error band** — faint band-of-band.
4. **Forward cone** — the OnRender projection polygon.
5. **Consumable surface + Eye consult** — expose properties, wire `GetEyeVerdict`.
6. **(v0.2) Publish seam** — ✅ DONE. `SetEnvelopeState`/`GetEnvelopeState` in SentinelCore + wired in
   `VolEnvelope_v0_2_0`. Remaining: Copier/Arc consumption (gate on `GetEnvelopeState(...).IsSqueeze`).

Each slice: bump nothing (still v0.1.0 until first freeze), update the in-file changelog, F5 in NT
(authoritative), watch for warmup NaNs and the vertical-connector plot artifact.

---

## 7. Header / changelog block (carry the CompressionBase format)

```
// ═════════════════════════════════════════════════════════════════════════════
//  VolEnvelope — honest volatility envelope (a Bollinger rewrite)   [Edge lane, no orders]
//  File: VolEnvelope_v0_1_0.cs   |   Version: v0.1.0   |   namespace …Indicators.Sentinel
// ─────────────────────────────────────────────────────────────────────────────
//  WHY: BB draws SMA±2σ — Gaussian, symmetric, close-only, regime-blind, exact-looking.
//  This draws an EWMA center, range-based (Yang-Zhang) vol, empirically-calibrated
//  ASYMMETRIC width, a native regime state (squeeze/range/trend), trend-aware %b,
//  an error band (SE of the vol estimate), and a forward cone. See Docs/SENTINEL_VOLENVELOPE_SPEC.md.
//
//  CHANGELOG
//    v0.1.0 — Initial. EWMA center + YZ/RS vol + asymmetric empirical bands + regime
//             + trend-aware %b + error band + forward cone + Sentinel glass card.
// ═════════════════════════════════════════════════════════════════════════════
```

## 8. Verify checklist
- [ ] F5 in NT compiles clean (authoritative — not headless; csproj is stale per `headless-csproj-stale`).
- [ ] Warmup: no exceptions before `CalibrationLookback` bars; bands NaN-safe until seeded.
- [ ] Bands asymmetric on a skewed instrument (visually wider on the trending side).
- [ ] Squeeze lights amber when bandwidth is in its low percentile; card Track collapses.
- [ ] Band touch in a trend reads `isRiding` (not extreme); touch in a range reads `isExtreme`.
- [ ] Cone renders right of the last bar, widens with √t, no clipping (compute from `ChartPanel`, not `ActualHeight`).
- [ ] Add `<Compile Include>` entry; dedupe if NT already appended it (CS2002).
