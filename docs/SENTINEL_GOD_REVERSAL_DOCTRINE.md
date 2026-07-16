# God Reversal — Doctrine & Sensor Spec

**Source:** "god trade masterclass" — YouTube video (id `Saa2YTtIV7E`), channel *Trading for Rent Money*
(the creator of the "God Trades" method this suite's `GodTrades21` / `Eye` lineage descends from).
Transcribed + formalized 2026-07-08.

This doc is the **durable reference** for the reversal grammar taught in that masterclass, and the spec for
the Sentinel sensor that encodes it: **`SentinelGodReversal_v1_0_0`** (display *"Sentinel God Reversal"*).

> **One-sentence thesis (his words):** *"A god trade is a reversal at a predictable place."*
> Two conditions must co-occur — a **predictable place** (a line/zone or a Bollinger-band edge, ideally with a
> volume imbalance there) **and** a **reversal signal** printing *at* that place. Everything else is filtering.

---

## 1. Why this sensor exists (the gap it fills)

The existing `GodTrades21` indicator (which `Eye` runs across ~24 bar-types and scores) already encodes the
**structural / Bollinger** half of the method:

| GodTrades21 already has | Plot |
|---|---|
| Bollinger-band gap ("BB gap") | `BollingerGapLong/Short` |
| Fill-&-continue | `ContinuationLong/Short` |
| Outside-bar reversal | `OutsideBarReversalSignal` |
| Volume-imbalance ("gap") tracking + spiderweb clutter veto | `ActiveGapCount`, `SpiderwebWarning`, … |

What `GodTrades21` does **not** encode — and what the masterclass spends most of its time on — is the
**candle-grammar reversal vocabulary**:

- **shaved close / shaved open** (no wick on the close = that side dominated, "price in a hurry")
- **engulfing at the level** (reversal body > prior opposite body)
- **equal high / equal low** (prior close == reversal open at the *exact* level, jammed against the band)
- **doji-cluster exhaustion** ("shitty green / shitty red", a green doji in a red push = trend death)
- **no-snapback** (a forming candle that doesn't retrace = domination) — *intrabar, approximated on closes*
- **attack angle** (steep approach into the level = tradeable; sideways grind into it = skip)
- **volume-imbalance fill count** into the level (filling 1–3 stacked VIs = the "late bloomer")

`SentinelGodReversal` is that missing recognizer. It is a **read-only chart indicator (no orders)** that marks
where the grammar triggers, scores the confluence, and publishes a `GodReversalState` seam so the **Council**
(and later the **Bridge** strategy) can consult the same reversal read.

---

## 2. The hard rules (the doctrine spine)

These are management rules for the *strategy* that trades the signal — they live in the Bridge / GTrader21
exit logic, **not** in the sensor. Recorded here so they aren't lost.

| Rule | Detail |
|---|---|
| **Stop** | Single candle — the **back of the entry (reversal) candle** (its Low for a long, High for a short). Never widen. The sensor exposes this as `SuggestedStop` + draws it. |
| **Break-even is the enemy** | Moving to BE early gets you swiped out. He claims ~**20% of profit** is lost to premature BE. Let it ride to the target (the next predictable place / opposite band). |
| **Expectancy** | Expect **3–4 full single-candle stops per 6.5 h session** — fine, because the average winner should clear **> 3 candles**. If you can't net 3 candles across 5–10 setups/ticker, something's wrong. |
| **Leverage** | If a *single-candle* stop can blow the account, you're over-levered. Size so 3 stops is a non-event. |
| **Attention** | Ignore price entirely unless it's approaching a band edge or a line. Mid-band + no VI = no trade. |
| **Exit** | At the next predictable place (opposite line/zone, session low) — or hold to the **opposite side of the Bollinger band.** |
| **Don't clutter** | Only stack **bounce / prediction lines** as confirmation. Volume profile etc. = "more shit to think about." |

---

## 3. The reversal-signal vocabulary (what the sensor detects)

All measured on **closed** candles. `body = |Close−Open|`, `range = High−Low`,
`upperWick = High−max(O,C)`, `lowerWick = min(O,C)−Low`. Ticks via `TickSize`.

| Signal | Long (bullish reversal) | Short (bearish reversal) |
|---|---|---|
| **Shaved close** | `upperWick ≤ shaveTicks` (closed on the high) | `lowerWick ≤ shaveTicks` (closed on the low) |
| **Engulfing** | bull body > prior red body, `Close>priorOpen && Open<priorClose` | mirror |
| **Equal level** | prior red `Close ≈ Open[0]` within `equalTol` ticks (equal low) | prior green `Close ≈ Open[0]` (equal high) |
| **Doji** | `body ≤ dojiTicks` (or `body/range ≤ dojiRatio`) | same |
| **Exhaustion** | ≥1 doji **or** shrinking counter-bodies ("shitty red") in the last `ClutterLookback` bars | mirror |
| **VI fill** | a bearish→bullish 2-bar gap (`Low[k] > High[k+1]`) was filled into the level | mirror |
| **Attack angle** | `|Close[0]−Close[K]| / Σ range[0..K] ≥ attackMin` (clean approach, not a grind) | same |

### Location gate ("predictable place")
- **Built-in:** Bollinger band edge — price within `bandProximityTicks` of the lower band (long) / upper band
  (short). Self-contained (`Bollinger(stdDev, period)`).
- **Optional booster:** `SentinelCore.GetLevelState` (the **Location** indicator) — a structural level
  (VWAP / PDH-PDL / OR / IB / session H-L) within reach adds to the score. Soft-dependency; absent ⇒ ignored.

### No-trade guards
- **Endless-doji chop** — if ≥ `⌈0.6 × ClutterLookback⌉` of the last bars are dojis, suppress (his "terrible
  place to trade, endless dojis").
- **Sideways grind into the level** — `attack angle < attackMin` and no hard signal (engulf/equal) ⇒ suppress
  ("price hanging out for three years in the same seven points" → skip).

---

## 4. Setup taxonomy (his six named setups → how the sensor labels them)

| Masterclass setup | Sensor `Setup` label | Recognized by |
|---|---|---|
| BB gap (momentum continuation) | *(owned by GodTrades21 `BollingerGap`)* | — |
| Equal high / equal low | `equalLevel` | equal-level + at band |
| Late bloomer | `lateBloomer` | VI-fill(s) → chop → engulfing shaved-close at a level |
| Line bounce / trampoline | `lineBounce` | repeated equal-highs tagging a level, then reversal candle |
| Fill & continue | *(owned by GodTrades21 `Continuation`)* | — |
| No trade | *(suppressed — no marker)* | fails a no-trade guard |

The sensor fires on the **close of the reversal candle** (non-repainting; entry is the next bar, matching the
suite's Deck-SIGNAL-ARM convention). It emits a **quality score 0..1** = normalized sum of stacked confirmations,
and only marks when `score ≥ MinQuality`.

---

## 5. Sentinel plumbing (per the standing protocols)

- **Naming:** class `SentinelGodReversal_v1_0_0`, display `Name = "Sentinel God Reversal"`, namespace
  `NinjaTrader.NinjaScript.Indicators.Sentinel` (clusters under the "Sentinel" picker folder). No custom enum
  params (dodges the bare-enum codegen saga) — all `[NinjaScriptProperty]`s are bool/int/double.
- **State seam:** publishes `SentinelCore.GodReversalState` (Signal pulse ±1, HELD Dir for `HoldBars`, Quality,
  Setup, AtBand, Exhausted) — `SentinelCore` v1.14.0. **PublishState default ON.**
- **Council voter:** wired as `GREV` (Weight — God Reversal, default 0.9). ⚠ **Load-bearing caveat:** the
  Council is **trend-heavy** (Trend/CCI/ADX/Env/Brick/MTF all trend-follow). A reversal-at-a-band vote is often
  *counter* to those, so it will frequently be out-voted / damped. That's arguably correct (don't catch a knife
  the panel hates) — but it means the reversal is best used as an **entry trigger** consulted *alongside* the
  Council bias (the Bridge's job), not as a lone Council swing vote. Watch this interaction live; let **Lens**
  grade the weight.
- **Hidden `Signal` plot** (±1 on the trigger bar, transparent, `IsAutoScale=false`) so the Deck SIGNAL ARM and
  any generic consumer can read it without drawing-scrape.
- **Glass card + label remover** per `SENTINEL_DESIGN_SYSTEM.md` (§4b Painter, §7 naming).

---

## 6. On-chart rendering (visual verification — the point of building it as an indicator)

- **Reversal marker** at the trigger bar: `Draw.TriangleUp`/`TriangleDown` (green/red), anchored past the wick.
- **Score + setup label** next to the marker (e.g. `GR▲ .82 shv+eng`).
- **Suggested stop line** = the back of the reversal candle (the single-candle stop).
- **VI boxes** (optional) = faint rectangles on detected volume imbalances so you can see what it keyed on.
- **Glass card** = last signal (dir/type/score), fired counts, and the *current* location read
  ("AT LOWER BAND" / "mid-band idle") so you can confirm the location gate live.

Verification workflow: load it on a GC / NQ chart against the masterclass examples and confirm the markers land
on the shaved-close / equal-high / late-bloomer bars he points at — and that mid-band chop stays unmarked.

---

## 7. Status & open items

- **Built:** `SentinelGodReversal_v1_0_0.cs` + `SentinelCore` v1.14.0 `GodReversalState` seam + Council `GREV`
  voter. **Needs an F5** (authoritative compile).
- **Not encoded (by design — management, not signal):** the stop / break-even / expectancy rules → Bridge / GTrader21.
- **Honest caveat:** thresholds (`shaveTicks`, `equalTol`, `dojiTicks`, `attackMin`, `bandProximityTicks`,
  `MinQuality`) are **first-guess defaults**. Tune against the video's examples + a forward sample; grade with Lens.
- **Next:** the **Bridge** strategy consuming `CouncilState` bias × `GodReversalState` trigger, recording each
  fire to the `SentinelCore.Ledger` so the weights become learnable.
