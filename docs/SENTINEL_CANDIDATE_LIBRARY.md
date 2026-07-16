# Sentinel Candidate Library — what to pull from for future tests

> A curated catalog of the `Downloads\` + Au/ama sources, scored for **value to the Council/Replay-Lab test surface**
> and **license portability**. Built 2026-07-12b from a 6-agent survey of 90 `.cs` files. Companion to
> Docs/ReplayLab and the [Product Ladder](PRODUCT_LADDER.md).
>
> **UPDATE (2026-07-14): "nothing ported yet" is no longer true — the PORT MARATHON happened.** Many shortlist items
> shipped (SentinelFlow, Structure, ADXVMA, SuperTrend, PSAR, Z-Score, VIDYA, Harmonic, TrendArchitect…). Read this as
> the original read-only assessment; the checkmarks below now reflect *done*, not *proposed*.

## The thesis
The Council already fuses **22 voters** (as of 2026-07-14; was 8-10 trend/momentum ones at write time — Eye ·
SentinelTrend · Woodies · ADX · VolEnvelope · Brick · CompressionBase · WAE · StochFilter · GodReversal) — **most
price-derived echoes of the same OHLC.** Adding *more* trend or
momentum tools does not make it smarter; it just re-weights the same signal. **The value in this haul is the handful of
genuinely ORTHOGONAL axes the suite lacks** — order-flow, market profile, swing structure — plus a few **bar-type test
harnesses** that directly serve the Replay Lab. Everything else is a duplicate, an echo, a UI panel, or a strategy.

## Provenance rules (the hard gate — from the port skill)
| License found in the source | Action |
|---|---|
| **MPL-2.0 / MIT / BSD** | ✅ Portable — keep the license header + attribute the author |
| **GPL-3.0** (LizardIndicators/ama) · **CC-BY-NC-SA** (BigBeluga, dorschden) | ⚠ Incompatible with the MPL suite → **clean-room from the public formula only**, never copy the code |
| **unlicensed / no header** (most of Downloads) | ⚠ Treat as all-rights-reserved → clean-room the public method, or skip |
| **author-own** (GodTrades / GTrader / DD) | ✅ Clean — the user's own work |
| **protected DLL / bound to a proprietary dep** (IronRodSMI, Alighten `AUN_Indi`/`OptionsBridge`) | ⛔ Not portable — skip |

---

## THE LIBRARY, by role

### ① FLOW axis — order-flow / volume-delta  ⭐ THE BIGGEST GENUINE GAP (truly non-price-derived)
| Source | Lic | Val | → build |
|---|---|---|---|
| **AlightenFootprintOrderFlowV00013** | unlic | **HIGH** | Clean-room the imbalance/absorption/exhaustion logic → a **Footprint voter** (bid/ask/delta/stacked-imbalance). Richest genuine order-flow; beyond LiquidityWalls. |
| **AlightenVolumeDeltaSignalV0004** | unlic | MED-HIGH | Compact delta→OSR directional signal — the most **voter-shaped** flow file. Clean-room → **VolumeDelta voter**. |
| AlightenVolumeDeltaV0026 | unlic ("by Gill") | MED | Cumulative delta + divergence building block to mine alongside the above. |
| **BuySellVolumePressureMountainV002** | unlic (own?) | MED | Volume-directional *pressure* (buy/sell split → dominance). ⚠ OHLC-geometry-derived, not true bid/ask → *partly* orthogonal. Easy clean-room `PressureState` voter. |
| VIP / VIP_Optimized | 3rd-party (Kelly Ann/TraderOracle) | MED | Volume-imbalance bar coloring (MTF). V2 is a dup. |
| AlightenGEXViewerV0002 | unlic + external feed | HIGH*concept | Options **gamma/DEX/OI** exposure — the ONLY truly non-price axis in the haul, but needs the proprietary `AlightenOptionsBridge` CSV feed. **Reference design only** until the feed is licensable. |

### ② PROFILE / LOCATION axis  ⭐ (a named suite gap; converges with the ama TPO family)
| Source | Lic | Val | → build |
|---|---|---|---|
| **VolumeProfileWithNodeDetection** | unlic | **HIGH** | POC (regular+developing) · value area · HVN/LVN nodes. Clean-room → the **Profile axis** (`ProfileState`: POC / VA / above-below). |
| **RedTailSwingAnchoredVWAP** | author "RedTail", unlic | **HIGH** | Swing-anchored EWMA VWAP + Fib. Orthogonal to Location's *session* VWAP (anchored reclaim/rejection). |
| KrisBarPOCv4 | unlic | HIGH* | Per-bar developing POC — finer-grained. *Needs Volumetric bars loaded. |
| **ama Moving{Mean,Median,Mode}TPO + VWTPO** | GPL | HIGH | The TPO/Market-Profile family already picked from the ama pack → the Profile axis (clean-room). |
| AlightenHTFVP · PreviousHTFWithTickerSelect | unlic | MED | HTF volume profile / prev-HTF O-H-L-C-mid levels — extend Location. |
| Gamble5m | author-own | LOW-MED | Pivot high/low lines → Location. |

### ③ STRUCTURE voters — swing / pattern geometry (orthogonal to oscillators)
| Source | Lic | Val | → build |
|---|---|---|---|
| **PriceActionSwingPro (+ …Base)** | **CC-NC** (dorschden) | **HIGH** | ZigZag/Gann swings · ABC patterns · swing-volume/divergence. Genuinely different axis from our oscillators & from GodReversal's candle grammar. **Clean-room** the swing math → a **Structure voter**. |
| **VdubusPatternGenV2** | unlic (Pine port) | **HIGH** | Harmonic patterns (Gartley/Bat/Butterfly/Crab). A signal family we have **nothing** like. Clean-room from public harmonic formulas. |
| AlgoAlphaReversalSignals | unlic (Pine port) | MED | Reversal + stepped MA, ships a ready ±1 plot. Partial w/ GodReversal. |
| GannHiLoActivatorEnhanced v3 | unlic | MED | Gann HiLo trend-flip (non-repaint v3). Distinct flip mechanism. |
| PMStackedEMA | unlic | MED | Fib-EMA ribbon stack-alignment → cheap trend-alignment modulator (complements MTF). |

### ④ TREND / REGIME voters — portable + already queued
| Source | Lic | Val | → build |
|---|---|---|---|
| **TrendArchitect** | **MPL-2.0** ✅ | MED | The one *portable* asset. Keep its rendering; **publish PRISM bias + Trend-Regime-Gate as a seam → Council voter** (fuses MFI/CCI/CVD/Hurst/KAMA-fan). `MTFv001_11` shows the signal-on-any-bar-type pattern. |
| ParabolicSAR · ZScore (ama) | GPL→clean-room | HIGH/MED | Already queued: SAR trend/stop voter · Z-score mean-reversion voter. |
| VolumaticVIDYA | **CC-NC** (BigBeluga) | MED | Clean-room the VIDYA (Chande CMO-modulated EMA) → directional voter; drop the liquidity-zone overlay. |
| GTrader_v1_0_0 | author-own ✅ | MED | The user's GodTrades gap signal (SentinelLog-ready). Overlaps GTrader21/GREV → packaged reference, not a new axis. |
| DDEntry | author-own ✅ | MED | A mini-fusion (BSVP+neVs+WAE+NetPressure%) — reference for voter composition; needs its deps. |

### ⑤ SMOOTHERS — building blocks (already built or queued)
`Indicators.Sentinel.Smoothers` now holds **30** (24 Au + 6 ama: TillsonT3/Laguerre/AdaptiveLaguerre/Coral/SWMA/RWMA).
Nothing new worth adding from this haul (ElderAutoEnvelope/DeviationTrendProfile overlap VolEnvelope; the ama Coral/Laguerre
duplicates were skipped).

### ⑥ BAR-TYPE test harnesses — serve the Replay Lab directly (NOT voters)
| Source | Lic | Val | → use |
|---|---|---|---|
| **AdaptiveStrategyPerformanceGridGodTradesV002** | author-own ✅ | **HIGH (test)** | Runs GodTrades across many bar types/timeframes and **ranks** them — a ready-made in-platform companion to `compare_bartypes.py`. Mine its method; cross-check the Replay-Lab numbers. |
| **DDBarsPerSessionAdvisor** | author-own ✅ | MED (test) | Advises whether a tick/brick size is too small/large for a session target — directly useful for the **brick-size sweep** (EXP-0003's open question). |
| **DDLagCheck** | author-own ✅ | MED (test) | Lag injector + measurement → the offline rig to validate **SentinelRisk's** feed-lag watchdog (inject known lag, confirm kill-switch engages). |
| ⚠ **No novel BAR TYPES** | — | — | All 4 bar-type files (ERP/TBarsNext/TbarsCount/TBarsElse) are **TBars-family dups** of SentinelTBars (some collide on id 69696). Nothing new for the sweep — vary SentinelTBars' Speed Settings instead. |

---

## ⭐ RECOMMENDED BUILD SHORTLIST (ranked by orthogonality × value × portability)
**Tier 1 — new orthogonal axes (the real prize):**
1. **Flow voter** — clean-room `AlightenVolumeDeltaSignal` (+ Footprint absorption) → a `FlowState`/delta voter. *The single most orthogonal thing in the haul.* **(UPDATE 2026-07-14: the order-flow gap is now filled a deeper way — by the from-scratch `SentinelFlux` BAR TYPE (BarsPeriodType 212203, López de Prado imbalance bars + FLUX voter), the suite's first genuinely orthogonal axis. SentinelFlow shipped as the CVD voter alongside it.)**
2. **Profile axis** — clean-room `VolumeProfileWithNodeDetection` (POC/VA/nodes) + the ama **TPO** family → a `ProfileState` context axis. Fold in `RedTailSwingAnchoredVWAP` as an anchored-VWAP location signal.
3. **Structure voter** — clean-room `PriceActionSwingPro` swing/ABC math → a market-structure voter.

**Tier 2 — portable / queued voters:**
4. **TrendArchitect** (MPL) → PRISM/Regime seam voter. 5. **ParabolicSAR · ZScore · VIDYA** (queued). 6. **VdubusPatternGen** harmonic voter (novel but heavier).

**Tier 3 — Replay-Lab test harnesses (not voters, but serve the mandate directly):**
7. **AdaptiveStrategyPerformanceGrid** (bar-type ranking) · **DDBarsPerSessionAdvisor** (sizing) · **DDLagCheck** (Risk validation).

## SKIP (no reusable value)
Squeeze tools (amaSqueeze×2 GPL · TTMSqueezeTradeSaber · Zombie9Squeeze — all dup CompressionBase) · momentum echoes
(DMI×2 · TSI · QStick · neVsSignals) · WoodiesCCIProV001 (dup of our WoodiesCCIPro) · Elder/Deviation envelopes (dup
VolEnvelope) · all UI panels (Alighten ButtonPanel×3 · MQPanel · TradeWindow5 · ToolBarClock · IndicatorVisualStyleHelper ·
BullBearDataBox) · strategies (ConfluenceArchitect · ConfluenceMTF · AlightenGoldStrategy · IceIceBaby×2 · MoneyPress ·
Gamble5m-strat) · protected/bound (IronRodSMI DLL · TwoPoleOscillator missing-math · Alighten Mirror/Level ZigZag family =
one core, overlaps Location/MTF) · bar-type dups (ERP/TBarsNext/TbarsCount/TBarsElse) · VolumeAggregationBars (MPL but a viz
dup of NT Volume bars).

---

## 🧭 PRE-DESIGN — Tier-1 axes (build straight from this next session; all clean-room)

### ① SentinelFlow — order-flow / volume-delta VOTER  → `Indicators.Sentinel.Sensors`
**Clean-room from:** the public **CVD / tick-rule delta** method (identified in `AlightenVolumeDeltaSignal` — do NOT copy;
it's unlicensed). LiquidityWalls already computes tick-rule delta in-suite — reuse that *approach* (uptick vol = buy,
downtick = sell), or `OnMarketData` Bid/Ask if the feed carries it. ⚠ **tick-rule delta is a PROXY, not true exchange
bid/ask** — still far more orthogonal than the OHLC-only voters; note it honestly.
- **Compute (per bar):** `barDelta = buyVol − sellVol`; `CVD += barDelta`; `OSR = |barDelta| / max(barVol,1)` (0..1
  strength); `divergence` = price higher-high while CVD lower-high (bear) / mirror (bull).
- **Direction (STATE voter):** `sign(barDelta)` confirmed by CVD slope → +1/−1/0. Divergence flips/vetoes.
- **Seam (SentinelCore, bump ver):** `FlowState { Scope, Instrument, Direction, Cvd, Osr, Divergence(bool), BarTimeUtc,
  IsHistorical, UpdatedUtc }` + `SetFlowState/GetFlowState/AllFlowStates` via `SeamStore<FlowState>`. **SCOPE-keyed**
  (delta varies with bar type). Mirror the `WaeState` seam exactly.
- **Council:** add `FLOW` to KnownVoters (weight ~0.6 exploration), **STATE kind**, into the Reasons audit; hidden `Signal`
  plot (±1). Card: CVD sparkline + OSR + divergence dot.
- **Later refinement:** Footprint *absorption* (big delta, small price move) from AlightenFootprint → a second seam field.

### ② SentinelProfile — market-profile CONTEXT axis  → `Indicators.Sentinel` (context axis, sibling to Location; ⚠ confirm loose-in-.Sentinel w/ user at v1)
**Clean-room from:** the public **volume-profile / TPO** method (POC / Value Area are standard, not copyrightable) —
identified in `VolumeProfileWithNodeDetection` + the ama **TPO** family. Fold in **anchored VWAP** from `RedTailSwingAnchoredVWAP`.
- **Compute:** bin price into `tickSize`-buckets over a rolling window/session; accumulate volume/level → **POC** (max-vol
  price); **Value Area** = smallest band holding 70% of volume → **VAH/VAL**; **HVN/LVN** = local vol peaks/troughs.
  Anchored VWAP = EWMA VWAP anchored at the last swing.
- **Publish (MODULATOR, not directional):** `ProfileState { Scope, Poc, Vah, Val, PriceVsPoc(+1 above/−1 below/0 at),
  InValueArea(bool), NearHVN/NearLVN(bool), AnchoredVwap, PriceVsAvwap, UpdatedUtc }`. SCOPE-keyed. Bump Core.
- **Council role:** like Location — **damp SizeMult inside the value area** (acceptance/chop), **boost/veto at VA edges &
  POC** (don't fade into a POC; rejection at VAH/VAL = context for the directional voters). Add to Reasons. Card:
  POC/VAH/VAL rails + price-position pill.
- **Ordering:** ships after the FLOW voter; both are bigger than the queued SAR/ZScore voters — do the small queued voters
  first to warm up the seam+Council muscle, then these two.
