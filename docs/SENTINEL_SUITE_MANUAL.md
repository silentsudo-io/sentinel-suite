# The Sentinel Suite — Field Manual

> **⚠ SUPERSEDED — merged into `Docs/SENTINEL_PROCESS_ATLAS.html`; facts here are stale.** Current state (2026-07-14):
> **SentinelCore v1.31.0 · Council v1.6.3 / 22 voters · SentinelFlux is now the first genuinely orthogonal axis — NOT
> Intermarket.** This prose was folded into the canonical, illustrated field manual (the flight-instrument HTML with the
> diagrams *and* this prose in one document). **Edit the HTML from now on; this `.md` is kept as the prose source of
> record only** — do not trust its version/voter/axis details. Anything not yet carried across (the full per-tool catalog
> prose, the exhaustive open-questions log) still lives here for reference.

> **STATUS: DRAFT v0.1 — for us to read and argue with.** This is the seed of the official
> Sentinel Suite manual. It is written to take a reader from *"I have no idea what any of this
> is"* all the way to *"I can run it, extend it, and trust it."* Everything technical here was
> pulled straight from the source on 2026-07-07 (SentinelCore internal **v1.13.0** as of the
> 2026-07-08 update — the **Bridge** shipped and the WAE voter landed since the first draft), so
> signatures are real, not remembered. Where something is **planned but not built**, it is labelled 📝. Where
> it needs live validation, it is labelled ⚠. Mark it up freely — the open questions are collected
> at the very end.

---

## Table of contents

0. [Part 0 — 5-minute quickstart (see it work before any theory)](#part-0--5-minute-quickstart)
1. [Part I — The Vision (from zero)](#part-i--the-vision-from-zero)
2. [Part II — The mental model: how it all fits](#part-ii--the-mental-model-how-it-all-fits)
3. [Part III — The component catalog (every file, what it does, how it connects)](#part-iii--the-component-catalog)
4. [Part IV — The seam bus reference (the `…State` contracts)](#part-iv--the-seam-bus-reference)
5. [Part V — Extending Sentinel: hook in a new voter + get logging working](#part-v--extending-sentinel)
6. [Part VI — Rapid MAE/MFE testing with the Excursion lab](#part-vi--rapid-maemfe-testing-with-the-excursion-lab)
7. [Part VII — The safety model (Gate · Ledger · Alerts)](#part-vii--the-safety-model)
8. [Part VIII — A worked example: GC FC-Short, raw signal → graded edge](#part-viii--a-worked-example)
9. [Part IX — Glossary, file index, and open questions](#part-ix--glossary-file-index-and-open-questions)

---

# How to read this manual

This is **one manual with two reading paths** — we deliberately did *not* split it into separate
Trader and Coder books, because the best trader-users eventually want to peek under the hood and the
coders need the trader's mental model first. Follow the path that fits you today; cross over whenever
you're curious.

```
        ╔═══════════════════════════════════════════════════════════════╗
        ║  EVERYONE starts here:  Part 0 (quickstart) → Part I → Part II ║
        ╚═══════════════════════════════════════════════════════════════╝
                          │                                │
              ┌───────────┘                                └───────────┐
              ▼                                                        ▼
   ┌───────────────────────────┐                      ┌───────────────────────────────┐
   │  TRADER PATH              │                      │  CODER PATH                    │
   │  III  component catalog   │                      │  IV   the seam bus reference   │
   │  VI   MAE/MFE lab         │                      │  V    extending Sentinel       │
   │  VIII worked example      │                      │  VII  the safety model         │
   │  VII  safety (operate it) │                      │  VIII worked example (the loop)│
   └───────────────────────────┘                      └───────────────────────────────┘
              └────────────────────┬───────────────────────────┘
                                   ▼
                        IX  glossary · file index · open questions
```

- **"I just want to run it."** → Part 0, then I–III, then VI (find your edge) and VIII (see the whole
  loop), then the *operate-it* half of VII. Skip IV–V.
- **"I'm going to write code for it."** → Part 0 and I–II for the model, then IV (the contracts), V
  (the recipes), VII (fail-open/closed policy), and VIII to see a real end-to-end trace.

---

# Part 0 — 5-minute quickstart

> **Goal:** see the suite *breathe* before you read a word of theory. In five minutes you'll have
> sensors publishing, the Council fusing them into one verdict, and a glass card on your chart
> updating live. No orders, no risk — this is read-only.

**You need:** NinjaTrader 8 with the Custom tree compiled clean (F5 in the NinjaScript Editor —
green, no errors). A GC (gold) chart is the reference instrument used throughout this manual.

### The steps

1. **Open a chart.** New → Chart → **GC** (front month), any bar type. A 100-tick or 1-minute chart
   is fine to start.

2. **Add four sensors.** Right-click the chart → Indicators. In the picker, open the **`Sentinel`
   folder** (suite tools cluster there) and add:
   - **Eye** — publishes `EyeVerdict` (the adaptive GodTrades scanner's directional qualification;
     the Council's **heaviest voter**, weight 1.4).
   - **SentinelTrend** — publishes `TrendState` (direction).
   - **ADXPro** — publishes `AdxState` (is there a trend, and which way).
   - **WoodiesCCIPro** — publishes `CciState` (momentum bias).

   > All four publish by default (`PublishState` / `PublishRegime` is ON out of the box). You've just
   > put four voters on the bus without configuring anything — the Eye leading, three price-lenses
   > behind it.
   >
   > **Why the Eye is here:** the other three are all price-derived, so they tend to nod along with
   > each other. The Eye is your qualifier — it's what makes the opening card more than three echoes
   > of the same candle. (See the independence caveat in Part II.)

3. **Add the brain.** Add **Council** from the same `Sentinel` folder. It immediately starts reading
   whatever sensors are publishing for GC and draws a **glass card** in the top-right corner.

4. **Read the card.** The Council card shows, live:
   - a **LONG / SHORT / FLAT** pill (the fused `Bias`),
   - a big **conviction %** and a **size ×** multiplier,
   - a row of **voter chips** (green = agrees with the bias, red = disagrees, grey = neutral),
   - a footer tally `▲agree ▼disagree · N voters`,
   - and, when something blocks it, a red **`VETO: …`** line.

5. **Watch it change.** As price moves, sensors republish, chips flip color, and conviction breathes
   up and down. When the sensors line up, conviction climbs; when they fight, it drops toward the
   floor and `Bias` goes FLAT. **That's the whole idea in one card:** many signals → one honest
   verdict.

6. **(Optional) open the dashboard.** Control Center → New → **SentinelDashboard**. Twelve tabs; for
   now just note the **Excursion** and **Test** tabs — you'll use them in Parts VI and VII.

### What you just proved

- Sensors don't know the Council exists — they just **publish** (`Set…State`).
- The Council doesn't know your sensors — it just **reads what's on the bus** (`Get…State`) and
  **fuses** it.
- Add or remove a sensor and the Council's voter count changes automatically. **Nothing is wired by
  hand.** That decoupling is the entire architecture, and you're now looking at it.

Everything past this point explains *why* it's built this way, *what* every piece does, and *how* to
extend it. If you only ever use the suite as a trader, Parts I–III and VI are your world. If you'll
write code for it, add Parts IV–V and VII.

---

# Part I — The Vision (from zero)

## What problem is this solving?

A serious NinjaTrader chart ends up with a dozen indicators screaming at once — a trend line, a
CCI, an ADX, a volatility band, a volume-profile, a session clock — plus a strategy trying to
trade, plus (if you're funded) a prop firm's rules hanging over your head like a guillotine.
Nothing on that chart *talks to anything else.* The trend indicator doesn't know the CCI
disagrees. The strategy doesn't know a news blackout starts in four minutes. The copier mirroring
your fills doesn't know the kill-switch just tripped. Every tool is an island.

**The Sentinel Suite is the plumbing that turns that pile of islands into one nervous system.**

It is not one indicator. It is a *design* — a shared spine that every tool plugs into so they
share a vocabulary, a safety layer, a memory, and (as of 2026-07-07) a single fused opinion about
what to do next. The name captures the intent: a **sentinel** watches, guards, and warns.

## The four roles every trading system needs

The whole suite is organized around four jobs. Every file belongs to one of them:

| # | Role | Plain-English job | Suite tools |
|---|---|---|---|
| 1 | **Signal sources** | "Is there an edge, and which way?" | SentinelTrend, WoodiesCCIPro, ADXPro, VolEnvelope, CompressionBase, Sentinel WAE, the Eye, the orthogonal axes (Clock/Participation/Location/MTF/Intermarket) |
| 2 | **Decision** | "Given *all* the signals, what's the verdict?" | **The Council** (the brain) |
| 3 | **Execution** | "Put the trade on and manage its life." | Deck (manual), GTrader21 (auto), Bridge (auto ✅) |
| 4 | **Observation & safety** | "Keep us alive, remember everything, prove it later." | SentinelCore (Gate/Ledger/Alerts), Risk, Dashboard, State/Alert services, Copier, Log, Lens, Arc |

## The one big idea: a decoupled seam bus

Here is the single architectural decision the whole suite hangs on:

> **Tools never call each other directly. They publish small typed facts to a shared bus
> (`SentinelCore`), and read facts back off it.** A sensor doesn't know the Council exists; it
> just publishes `TrendState`. The Council doesn't know GTrader21 exists; it just publishes
> `CouncilState`. GTrader21 doesn't re-derive confluence; it just reads `CouncilState`.

This is the "leader/execution decoupling" principle that emerged from the copier work, generalized
to the entire suite. It buys three things:

- **Fault isolation** — one tool crashing can't take another down; a missing publisher just means
  its fact is absent, and every reader treats *absent = abstain*.
- **Independent evolution** — you can rewrite the Eye's internals completely; as long as it still
  publishes an `EyeVerdict`, nothing downstream cares.
- **One decision, many consumers** — the Council fuses once; the strategy, the Deck, and any
  future Bridge all read the *same* verdict instead of each re-inventing it (and disagreeing).

## The nautical naming (so you're not lost later)

The team names execution surfaces after a ship:

- **Deck** — where you trade *by hand* (the manual order deck).
- **Bridge** ✅ — where you command the *autopilot* (the automated chart trader; **built** — consumes
  `CouncilState`, live-validated on SIM).
- **Council** — the advisors who vote on what to do (the confluence brain).
- **Helm** ✅ — where you *grab the wheel* of a running autopilot without stopping the car (the
  interdiction layer; **built** — commands a live actor via published intents, plumbing-validated 2026-07-15).

And the safety/sensor layer is the **Sentinel** — the watch that never sleeps.

---

# Part II — The mental model: how it all fits

## The layer cake

Read this bottom-up. Each layer only knows about the one below it via the bus.

```
┌──────────────────────────────────────────────────────────────────────────┐
│  EXECUTION      Deck (manual)      GTrader21 (auto)      Bridge ✅         │
│                    │  reads CouncilState / consults Gate at submit         │
├──────────────────────────────────────────────────────────────────────────┤
│  DECISION         The Council   ── fuses 9 voters + 6 modulators/vetoes    │
│                    │  publishes ONE CouncilState (bias·conviction·size)     │
├──────────────────────────────────────────────────────────────────────────┤
│  SENSING     9 voters  +  5 orthogonal axes   (each publishes its …State)  │
│              Eye·Trend·CCI·ADX·VolEnv·Brick·Compression·Intermarket·WAE     │
│              + Clock·Participation·Location·MTF (modulators/veto)          │
├──────────────────────────────────────────────────────────────────────────┤
│  BUS + SAFETY   ★ SentinelCore ★   seams · Gate · Ledger · State · Alerts  │
│                 kill-switch · feed-health · governor · news · rollover     │
├──────────────────────────────────────────────────────────────────────────┤
│  PRESENTATION   SentinelSkin (glass cards) · SentinelDashboard (12 tabs)   │
│                 SentinelSkin platform theme (candles/chrome)               │
└──────────────────────────────────────────────────────────────────────────┘
```

## The core data flow (follow one signal from birth to graded outcome)

```
   ┌── SENSORS ──┐        Set…State()          ┌──────────────────────┐
   │ SentinelTrend│ ───────────────────────▶   │                      │
   │ WoodiesCCI   │ ───────────────────────▶   │    SentinelCore      │
   │ ADXPro       │ ───────────────────────▶   │    (the seam bus)    │
   │ VolEnvelope  │ ───────────────────────▶   │                      │
   │ Compression  │ ───────────────────────▶   │  keyed by root sym,  │
   │ Intermarket  │ ───────────────────────▶   │  staleness-gated     │
   │ Clock/Part/  │ ───────────────────────▶   │                      │
   │ Location/MTF │                            └───────┬──────────────┘
   └──────────────┘                                    │ Get…State(inst, maxAgeSec)
                                                        ▼
                                              ┌──────────────────────┐
                                              │     THE COUNCIL      │
                                              │  weighted vote →     │
                                              │  Bias / Conviction / │
                                              │  SizeMult + Reasons  │
                                              └───────┬──────────────┘
                                                      │ SetCouncilState()
                                                      ▼
                              ┌───────────────────────────────────────┐
                              │            SentinelCore                │
                              └───────┬───────────────────────────────┘
             GetCouncilState()        │
                    ┌─────────────────┘
                    ▼
        ┌────────────────────────┐   gate on HasEdge + Aligned(dir)
        │  EXECUTOR (GTrader21 / │   size = baseQty × SizeMult
        │  Bridge / Deck ARM)    │──────────────┐
        └───────────┬────────────┘              ▼
                    │                   SentinelCore.GateEntry()  ← final choke point
                    │                   (fail-CLOSED for autos)
                    ▼ FIRE                        │ IsClear?
        ┌────────────────────────┐               ▼
        │ order submitted; on     │        submit to broker
        │ fill → Ledger.Fill()    │
        │ + record verdict to     │───────▶  SentinelCore.Ledger (daily JSONL)
        │ Ledger/Log on FIRE      │                 │
        └─────────────────────────┘                 ▼
                                            Lens / Dashboard grade it later
                                            (did agreement actually pay?)
```

Three ideas to lock in from that diagram:

1. **Publish → fuse → consume → record.** That loop is the whole suite. Everything else is detail.
2. **The seam is advisory; the Gate is authoritative.** Reading `CouncilState` tells you *whether
   there's an edge*. It does **not** put the trade on safely — you still call `GateEntry` at
   submit time. (See [Part VII](#part-vii--the-safety-model).)
3. **Every fire is recorded so it can be graded.** The point of writing the verdict into the
   Ledger on each fire is that **Lens** can later answer *"does the Council's confidence actually
   make money?"* — the weights are a hypothesis until the data says otherwise.

## The honest caveat you must keep repeating

Today, most of the Council's voters are **price-derived** — ADX, CCI, Trend, VolEnvelope, and
Brick all ultimately echo the same OHLC stream. When they "agree," that is not five witnesses
confirming a fact; it's one witness saying the same thing five ways.

> **Conviction measures AGREEMENT, not CONFIRMATION.**

The suite only gets genuinely smarter as **orthogonal** axes (independent information) are added.
That is exactly why the five orthogonal axes — **Clock, Participation, Location, MTF,
Intermarket** — were built. Intermarket in particular (correlated instruments like ZN/ZB for
gold) is real outside information, not another lens on the same candles. Keep this caveat loud in
every design conversation.

---

# Part III — The component catalog

Every entry: **what it is**, its **file + status**, what it **publishes/consumes**, and how it
**connects**. Status legend: ✅ shipped · 🔨 building · 📝 designed/planned · 💡 idea · ⚠ needs
live validation · ❄️ frozen checkpoint.

## 3.0 The two files everything depends on

Every subscribed Sentinel indicator hard-references **exactly two** AddOn files. That's the entire
compile dependency — no service stack required.

### ★ SentinelCore — the bus + the safety substrates
- **File:** `AddOns/SentinelCore_v1_0_0.cs` · **Status:** ✅ internal **v1.13.0** (added the `WaeState`
  voter seam)
- **Namespace/symbol:** `NinjaTrader.NinjaScript.AddOns.Sentinel.SentinelCore` — a `static class`
  with a **deliberately unversioned name** (other tools bind to the stable symbol; two copies =
  CS0101). Edited **in place**; the version lives in a `const string Version`.
- **What it holds:** the `…State` seam registry (§IV), the **kill-switch** (global + per-instrument
  scoped), **feed-health** gate, the combined entry gates (`CanEnter`/`CanAct`/`CanActInstrument`),
  the **Order Gate** (`GateEntry`/`SizeForRisk`/`TickValue`), the **Ledger** (daily JSONL event
  stream), the **State** store (restart-surviving blobs), **Alerts** (2-tier), and the account-level
  registries: **governor**, **trailing drawdown**, **account profiles**, **news lockout**,
  **rollover**, **config-use**, **fleet slots** (Arc), **manual-assist tickets**, **watch list**.
- **Connects to:** *everything.* It is the one cross-tool dependency by design.

### ★ SentinelSkin — the look
- **File:** `AddOns/SentinelSkin.cs` · **Status:** ✅ (internal v1.1, CardLayout)
- **What it holds:** the `SentinelSkin.Painter` (SharpDX glass-card primitives: `Card`, `Dot`,
  `Text`, `Pill`, `Money`, `Track`, `Gauge`, `Sparkline`, `Divider`), the color palette tokens
  (`CVoid/CPanel/CCard/CLine/CInk/CAccent/CUp/CDown/CWarn/…`), and **`CardLayout`** — a shared
  anti-overlap registry so cards from different tools docked to the same corner auto-stack instead
  of covering each other. Also defines `enum SentinelCardCorner`.
- **The one design rule:** **cyan = live/watching (the only accent); green/red = money +
  direction.** Palette: `void #0A0E17`, `panel #111726`, `ink #E9EEF7`, **`accent` cyan
  `#3FD1E0`**, **`up` green `#25D08B`**, **`down` red `#FF5C6A`**, `warn` amber `#F2B34C`.
- **Separately:** a platform **skin** at `templates/Skins/Sentinel/` themes the whole NT chrome
  (candles, caption bar, selection wash). That's a theme, not a code dependency.

> **Minimal shippable unit:** any single Sentinel indicator = **3 files** (itself + these two).
> With the services absent, every `Get…State` returns no-data and the tool degrades to **neutral,
> throwing nothing.** That "standalone-safe" property is a hard rule.

## 3.1 The Decision layer

### The Council — the brain
- **File:** `Indicators/Council_v1_0_0.cs` (`Indicators.Sentinel`) · **Status:** ✅ v1.0.0, live on GC
- **What it is:** a **read-only chart indicator that places NO ORDERS.** It is the fusion point the
  suite was missing — every sensor already published a state, but nothing *combined* them.
- **What it does:** reads every published sensor seam for its instrument, runs a **weighted vote**,
  and produces one verdict — **Bias** (−1/0/+1), **Conviction** (0..1 = how aligned the fresh
  voters are), **SizeMult** (0..1, 0 when vetoed), agree/disagree/voter tallies, and a
  human-readable **Reasons** audit string (e.g. `EYE▲ TRND▲ CCI▼ ADX~ ENV▲ · clk:Midday · vol×0.8
  · into VWAP`).
- **Publishes:** `SentinelCore.CouncilState` (so any executor consults the *same* decision) + hidden
  transparent `Bias`/`Conviction` plots (so the Deck's generic plot-reader can arm off it). Draws a
  glass card; logs verdict **changes** (not per-tick) to `sentinel.log`.
- **The 9 voters + 6 modulators/vetoes** are detailed in §V (that's also where you learn to add one).
- **Connects to:** *upward* every sensor seam; *downward* to GTrader21/Bridge/Deck via `CouncilState`.

## 3.2 The Sensing layer — the 9 voters

Each is a `NinjaTrader` indicator in `Indicators.Sentinel`, publishes a `…State` seam (default ON),
and is wired into the Council. The parenthesized number is its **default Council weight.**

| Sensor | File | Publishes | Weight | The read it contributes |
|---|---|---|---|---|
| **Eye** | `Eye_v1_1_0.cs` | `EyeVerdict` | 1.4 | Adaptive GodTrades scanner's directional qualification (+1/−1/0) |
| **SentinelTrend** | `SentinelTrend_v1_0_0.cs` | `TrendState` | 1.0 | ATR trailing-line direction (supersedes the whole TrendMagic family) |
| **WoodiesCCIPro** | `WoodiesCCIPro_v1_0_0.cs` | `CciState` | 0.8 (×1.5 if strong) | Woodies CCI trend state −2..+2 |
| **ADXPro** | `ADXPro_v1_2_0.cs` | `AdxState` | 0.6 (×1.25 if strong) | ADX trend on/off + DI bias |
| **VolEnvelope** | `VolEnvelope_v0_2_0.cs` | `EnvelopeState` | 0.6 | "Honest Bollinger" regime (squeeze/trend/expansion) |
| **Brick** (bar type) | `BarsTypes/SentinelTBars_v1_0_0.cs` | `BrickState` | 0.5 | Adaptive HA/Renko brick direction |
| **CompressionBase** | `CompressionBase_v1_3_0.cs` | `CompressionState` | 0.7 | Coil-base breakout direction ±1 |
| **Intermarket** | `Intermarket_v1_0_0.cs` | `IntermarketState` | 0.6 | Correlated-instrument lean (ZN/ZB for gold) — **the one truly orthogonal voter** |
| **Sentinel WAE** | `SentinelWAE_v1_0_0.cs` | `WaeState` | 0.7 | Waddah Attar Explosion — confirmed momentum-explosion breakout ±1 (fires only when power > explosion line > dead zone) |

## 3.3 The Sensing layer — the 5 orthogonal axes

These are the independence engine. Four **modulate** the Council's conviction rather than vote;
Intermarket (above) is the fifth and it *does* vote.

| Axis | File | Publishes | Council role |
|---|---|---|---|
| **Clock** | `Clock_v1_0_0.cs` | `ClockState` (session phase / mins-to-close / kill window) | midday & off-session **damp** + kill-window **veto** |
| **Participation** | `Participation_v1_0_0.cs` | `ParticipationState` (time-normalized RVOL + climax/dry-up) | RVOL **damp** (thin tape → cut conviction) |
| **Location** | `Location_v1_0_0.cs` | `LevelState` (VWAP/PDH-PDL/OR/IB/session H-L + nearest level) | into-a-level **damp** (don't trade into the wall) |
| **MTF** | `Mtf_v1_0_0.cs` | `MtfState` (1/5/15/60/240 ladder; per-TF = hosted SentinelTrend) | counter-higher-TF **damp** |
| **Intermarket** | `Intermarket_v1_0_0.cs` | `IntermarketState` | **8th VOTER** (0.6) |

**Parked orthogonal axes** (kept on the list, not built): breadth internals ($TICK/$ADD — feed is
delayed and it's an ES/NQ tool), book/spread microstructure, VIX/vol-term-structure regime, and a
dedicated `EventState` news seam beyond the existing News.conf → news-lockout path.

### The veto sensor — LiquidityWalls
- **File:** `Indicators/LiquidityWalls_v1_0_0.cs` · **Status:** ✅ built (⚠ needs live order-flow test)
- **What it is:** an order-flow **absorption** detector (tick-rule delta + OLS regression + z-score)
  that maps ATR-sized **wall zones** — prices where large resting liquidity is absorbing aggression.
- **Publishes:** `LiquidityState` (z-score, `AbsorbSide`, nearest wall above/below + distances). It
  isn't a voter — it's a **hard veto**: the Council calls `LiquidityState.BlocksEntry(bias, ticks)`
  and zeroes conviction if a wall sits on the intended side ("don't buy straight into resistance").
- **Connects to:** the Council's veto chain; any executor that wants to check for a wall before firing.

## 3.4 The Execution layer

### GTrader21 — the automated GodTrades strategy
- **File:** `Strategies/GTrader21v_0_1_7.cs` · **Status:** 🔨 v0.1.7 active (v0.1.6/earlier ❄️)
- **What it is:** a GodTrades-signal strategy (BG/FC/OBR) with a ported TrendArchitect WPF trade
  panel, on-chart risk card, and data-lag safety. **Unmanaged** order engine (owns its own exits).
- **Sentinel wiring:** consults `CanEnter` (fail-CLOSED), registers for feed-watch, gates on Arc/
  Eye/Trend, **auto-reads lab `.conf` files** (§VI) and reports back, does profile-aware sizing +
  session gating. Fill capture → `Ledger.Fill`; position-state persist/restore across restart.
- **Eye→Council decouple ✅ (v0.1.7):** GTrader21 now has the opt-in **`UseCouncilGate`** — it consults
  the Council's *fused* verdict instead of the Eye directly, so it's "one strategy among many." (Off by
  default; the Eye-gate path still works for a frozen fallback.)
- **⚠ Read the `gtrader21-panel-integration` memory before touching its order path** — long,
  hard-won history of managed-vs-unmanaged order-ownership.

### Deck — the manual order deck
- **File:** `Indicators/Deck_v0_2_2.cs` (`Indicators.Sentinel`) · **Status:** ⚠ SIM-validate
- **What it is:** a discretionary manual order deck + account risk card: all order types, all
  price-entry methods, full trade management (bracket/OCO, breakeven, 7 trail modes, scale),
  chart-scoped flatten, pop-out/dock, a $-risk sizer, and **on-chart order visuals** with
  drag-to-adjust + hover-attach.
- **The SIGNAL ARM section:** the Deck can **arm or auto-fire off ANY loaded indicator's plot**,
  discovered at runtime from `ChartControl.Indicators` (no hardcoded signals). ARM highlights
  BUY/SELL for a human to confirm; AUTO-FIRE goes fail-CLOSED through the Gate, one-shot per bar,
  flat-only + opposite-reverse, forces MARKET. Because the Council exposes `Bias`/`Conviction`
  plots, **the Deck can arm directly off the Council.**
- **Gate policy:** manual actions fail **OPEN** (the human is in the loop); auto-fire fails CLOSED.

### Bridge — the autopilot ✅
- **File:** `Strategies/SentinelBridge_v0_2_0.cs` · **Status:** ✅ v0.2.0 (v0.1.0 was headless), **live-validated
  on SIM** (fired a GC short off the Council, captured in the Ledger/Slippage tab).
- **⚠ Namespace exception:** the Bridge lives in the **BASE `NinjaTrader.NinjaScript.Strategies`
  namespace — NOT `.Strategies.Sentinel`.** NT's Strategy selector *hides* sub-namespaced strategies
  (verified this session), so the FEDERATED NAMING LAW's namespace layer is deliberately waived for
  strategies (see §V). Display name **"Sentinel Bridge"**.
- **What it is:** the automated counterpart to the Deck — a Council-consuming chart trader. It reads
  `GetCouncilState` (**Council-as-signal**: edge-detected, flat-only, one-shot), **sizes ×`SizeMult`**,
  routes through `GateEntry` (**fail-CLOSED**), places a **managed bracket**, and **records the verdict
  on every fire** to the Ledger so Lens can grade the weights.
- **v0.2.0 adds:** an on-chart Sentinel **glass card** with a clickable **ARM BRIDGE** button (arming
  gates firing — nothing fires until you arm it), and **`UseSentinelConfig`** which auto-reads the
  lab's `<inst>_COUNCIL_<dir>.conf` (written by the Excursion tab's Apply ◆) for its TP/SL.
- **Connects to:** *upward* `CouncilState`; *downward* the Gate + Ledger. See [bridge-plan memory].

### Helm — the interdiction layer ✅
- **Seam:** `SentinelCore` v1.34.0 · **Reference consumer:** `SentinelBridge` v0.3.0 · **Surface:** the
  Cockpit's **⑤ Helm · interdict** rail (`SentinelCockpit` v0.5.0) · **Status:** ✅ plumbing tier
  live-validated 2026-07-15 (Pause + Resume round-tripped Cockpit → Bridge → `HelmState` → Ledger in
  ~350 ms). **Position tier** (Flatten / BE / MoveStop on a real trade) pending — see the Helm test punchlist.
- **What it is (trader):** Helm lets you **grab the wheel of a running Bridge without stopping it** —
  move a stop, take breakeven, cut size, flatten now, skip the next entry, pause/resume — while the
  strategy keeps running and stays the sole owner of its orders. That completes the trio: **Deck** (you
  drive) · **Bridge** (it drives) · **Helm** (you grab the wheel *without stopping the car*). It
  supersedes the frozen **GTrader21** prototype.
- **How you use it:** open the **Cockpit** (Control Center ▸ New ▸ Sentinel Cockpit), go to the **⑤ Helm ·
  interdict** section, **pick the instrument** — type it in (e.g. `NQ`) if there's no Council on that
  chart — then hit a button: **Pause / Resume / Skip / Flatten / BE**, type a price for **Stop→ / Tgt→**,
  **reduce N** contracts, or **TakeOver / HandBack**. Each button publishes an intent to the running
  actor, which executes it with its own order handles.
- **The asymmetric idea (why it's safe):** **cutting risk is instant** — Flatten, Pause, Skip, tightening
  a stop all fail **OPEN**, so a panic click is never blocked by a stale seam or an offline service.
  **Adding risk back must clear the Gate** — Resume, widening a stop, scaling up, and HandBack fail
  **CLOSED** through `GateEntry`, exactly like an automated entry. The Gate doesn't care whose finger it was.
- **Why Helm never touches an order (the constraint):** a managed position closed by a panel desyncs
  `Position` from the account; the managed framework re-asserts `SetStopLoss`; and `Account.Change`/`Cancel`
  on a strategy order throws and can *terminate* the strategy. So **Helm owns nothing** — it publishes an
  intent addressed to a running actor's `instanceKey`, and the actor stays the sole owner.
- **For coders:** the seam is one-shot and idempotent. Helm publishes with
  `SentinelCore.SetHelmIntent(instanceKey, new HelmIntent { Verb = HelmVerb.MoveStop, Price = …, Id, ExpiryUtc })`;
  a consumer **drains it in its loop** with `var i = SentinelCore.TakeHelmIntent(_instanceKey)` (consumes
  it, expiry-guarded so a stale intent never fires twice) and **publishes `HelmState` back** so the
  Cockpit can read live truth (`Set/Get/AllHelmStates`, `ClearHelm`; all keyed by `instanceKey`). The
  10 verbs are `Pause · Resume · SkipNext · FlattenNow · MoveStop · MoveTarget · BreakevenNow · Scale ·
  TakeOver · HandBack`; `MoveStop` is context-classified (tighten = risk-reducing, widen = risk-adding).
- **Reference consumer caveats (Bridge v0.3.0):** it obeys all 10 verbs, drains every tick
  (`OnMarketData`) with an `OnBarUpdate` backstop, publishes `HelmState`, Ledgers every intent
  (`"helm-intent"` Action + `EpisodeId` + `instanceKey`) and marks the episode `HumanOverride` so the ML
  Lab excludes/separately-models interdicted trades. **Managed-mode honesty:** a single-entry managed
  Bridge **refuses Scale-up** (it can't scale-in), and **TakeOver/HandBack collapse to stand-down/resume**
  (managed mode can't transfer order ownership). "Obey Helm intents" defaults **ON**. The unmanaged-Bridge
  path is deferred.
- **Connects to:** *upward* the Cockpit surface publishing intents; *downward* the running actor + the
  Ledger. See [helm-interdiction-layer memory].

## 3.5 The Observation & Safety layer

| Tool | File | Status | Role |
|---|---|---|---|
| **SentinelDashboard** | `AddOns/SentinelDashboard_v1_0_0.cs` | ✅ v1.1.4 | Control-center window; **12 tabs**: Copy · Log · Risk · Journal · Slippage · Lens · Eye · Arc · Assist · **Excursion** · Accounts · **Test**. Each tab attaches to a service's `.Instance`. Reusable WPF chart primitives. |
| **SentinelRiskService** | `AddOns/SentinelRiskService_v1_0_0.cs` | ✅ v1.0 | Feed lag/stall + connection watchdog with hysteresis + auto-recovery; auto-engages the kill-switch (scoped); rollover countdown; hosts the news-lockout + consistency-governor feeds. Fills `SentinelCore.FeedHealthProbe`. |
| **SentinelAlertService** | `AddOns/SentinelAlertService_v1_0_0.cs` | ✅ v1.0.1 | Subscribes `SentinelCore.Alerts.Raised` → sound (wav/SystemSounds) + optional push shell command. Config `Sentinel\Alerts.conf`; the Test tab edits it live. |
| **SentinelStateService** | `AddOns/SentinelStateService_v1_0_0.cs` | ✅ v1.0.7 | 2-second snapshot of accounts/positions/orders/P&L/governor → `Sentinel\state.json` (readable *outside* NT). Observability, **not** restore. |
| **Sentinel Copy (Copier)** | `AddOns/SentinelCopierService_v0_1_0.cs` | ✅ core validated live (v0.1.0h) | Headless fill-mirror: Primary→Followers, same-provider prop rule, GC↔MGC cross-trading, kill/governor/session/Eye gates, manual-assist tickets, copy-slippage capture. |
| **Sentinel Log** | `AddOns/SentinelLogEngine_v1_0_0.cs` + `SentinelLogService_v1_0_0.cs` | ✅ | Zero-touch per-**trade** MAE/MFE + analytics logging → `Sentinel\Log\*.jsonl`; the Log tab is the live monitor. (Distinct from the per-**signal** Excursion lab.) |
| **Sentinel Lens** | `AddOns/SentinelLens_v1_1_0.cs` | 🔨 v1.1 | On-demand analytics over the Log JSONL (winrate/PF/MAE/MFE, per strategy+instrument). The tool that will **grade the Council's weights.** Static, no service. |
| **Sentinel Arc** | `AddOns/SentinelArcService_v0_1_0.cs` | ✅ (gate validated live) | Fleet orchestration via publish/consult: `FleetSlot` + the `SlotLive` entry gate + leader supervision. |
| **Sentinel Eye** | `Indicators/Eye_v1_1_0.cs` | ✅ | (Also a voter, §3.2.) Adaptive GodTrades scanner that *qualifies* setups; the Copier mirrors only Eye-qualified fills. Being decoupled from GTrader21. |

### Bar types (Brick source)
- `BarsTypes/SentinelTBars_v1_0_0.cs` — Sentinel-graded adaptive HA/Renko bar type (publishes
  `BrickState`; supersedes the TbarsSudo family).
- `BarsTypes/SentinelTbarsCount_v1_0_0.cs` — plain brick counter variant.
- `Indicators/SentinelBrickCounter_v1_0_0.cs` — "ticks to next brick" HUD off `BrickState`.

---

# Part IV — The seam bus reference

Every `…State` seam on `SentinelCore` follows **one identical shape**:

```csharp
void        Set<X>State(…);                    // publisher (sensor calls this)
<X>State    Get<X>State(string instrument, double maxAgeSec);   // consult; null if absent OR stale
List<X>     All<X>States();                     // snapshot of every instrument
```

- **Keyed by root symbol** (`Instrument.MasterInstrument.Name`, e.g. `"GC"`), case-insensitive,
  lock-guarded.
- **Staleness-gated:** `Get…` returns `null` if nothing was published *or* the entry is older than
  `maxAgeSec` seconds. Pass `0` to disable expiry. **Absent/stale = the consumer abstains** — this
  is the fail-open backbone.
- **The read always travels as `int` / `double` / `bool`** — the bus never couples to any
  indicator's private enum. A sensor publishes `(int)`; the reader interprets it.
- Two setter styles coexist: older seams take flat scalar args; the newer axes take a payload
  object (`Set…State(new SentinelCore.XState { … })`).

## The seam table (payload highlights + `.Aligned(dir)` convention)

| Seam | Publisher | Key payload fields (type) | Convenience props |
|---|---|---|---|
| `EyeVerdict` | Eye | `Direction`(int ±1/0), `Score`(double), `Source` | — |
| `TrendState` | SentinelTrend | `Direction`(int), `TrendPrice`, `DistanceTicks`, `BarsInTrend`, `Flipped`(bool), `Cci`, `AdxAligned`(bool) | `IsUp/IsDown/Aligned` |
| `CciState` | WoodiesCCIPro | `TrendState`(int −2..+2), `MainCci`, `TurboCci`, `MainSlope`, `Signal`(int), `Weakening`(bool) | `Bias`, `Strong`, `TrendOn`, `Aligned` |
| `AdxState` | ADXPro | `Adx`, `DiPlus`, `DiMinus`, `Bias`(int), `Slope5`, `Strong`(bool) | `TrendOn`, `Building`, `Aligned` |
| `EnvelopeState` | VolEnvelope | `Regime`(int 0-4), `Stretch`, `BandwidthPctile`, `MultUp/MultDown` | `IsSqueeze`, `IsTrend` |
| `BrickState` | SentinelTBars | `Direction`(int), `Atr`, `DensityScale`, `SameDirCount`, `PendingBreakout`(bool), `TicksToUpper/Lower`, `NearestTicksRemaining` | `IsUp/IsDown`, `AtrTicks()`, `Aligned` |
| `CompressionState` | CompressionBase | `Signal`(int pulse), `BreakDir`(int held), `Coil`, `Compressed`(bool), `Armed`(bool) | `JustBroke`, `Aligned` |
| `IntermarketState` | Intermarket | `Lean`(int), `Score`(−1..1), `RefCount`, `Refs`(string) | `Aligned` |
| `WaeState` | Sentinel WAE | `Signal`(int ±1 confirmed), `Power`, `Explosion`, `DeadZone`, `IsExploding`(bool) | `Aligned` |
| `ClockState` | Clock | `Phase`(int 0-3), `MinsSinceOpen`, `MinsToClose`, `DayOfWeek`, `InSession`(bool), `InKillWindow`(bool) | `IsOpenDrive/IsMidday/IsClose` |
| `ParticipationState` | Participation | `Rvol`, `VolZ`, `Climax`(bool), `DryUp`(bool), `TypicalVol` | `Backed` |
| `LevelState` | Location | `Vwap±bands`, `Pdh/Pdl`, `Orh/Orl`, `Ibh/Ibl`, `SessHigh/Low`, `NearestName`, `NearestDistTicks`, `NearestDistAtr` | `Near()`, `InPath()` |
| `MtfState` | MTF | `Bias`(int), `AlignmentScore`(−1..1), `AlignedCount`, `TfCount`, `AllAgree`(bool), `Dirs`(string) | `Aligned` |
| `CouncilState` | Council | `Bias`(int), `Conviction`(0..1), `SizeMult`(0..1), `Agree/Disagree/Voters`(int), `Vetoed`(bool), `VetoReason`, `Reasons` | `IsLong/IsShort`, **`HasEdge`**, `Aligned` |
| `LiquidityState` | LiquidityWalls | `Zscore`, `AbsorbSide`(int), `WallAbove/Below`, `DistAbove/BelowTicks`, `ActiveWalls` | `NearWall()`, **`BlocksEntry(dir,ticks)`** |

**`Aligned(int dir)` convention:** `+1`=long, `−1`=short, `0`=flat. Most seams treat `0` as
"require neutral"; `BrickState.Aligned(0)` returns true ("don't care").

## Account-level registries (not per-instrument seams)

Also on `SentinelCore`, keyed by account (or global): **kill-switch** (`KillSwitchEngaged`,
`SetKillSwitch`), **scoped kill** (`SetInstrumentKill`/`InstrumentKillEngaged`), **governor**
(`GetGovernorState`, `TradingAllowedToday`, `RecommendedSize`), **trailing drawdown**
(`DrawdownAllowsEntry`), **account profiles** (`GetAccountProfile`, `InAccountSession`,
`SizedQuantity`), **news lockout** (`SetNewsLockouts`/`NewsLockoutActive`), **rollover**
(`RolloverBlocked`), **config-use**, **fleet slots** (`SlotLive`), **assist tickets**, **watch
list**. The combined entry gate `CanEnter` = kill + scoped-kill + feed + governor + drawdown +
session + rollover + news.

---

# Part V — Extending Sentinel

This is the chapter you'll come back to. It answers: *"I built a custom indicator — how do I make
it a Council voter, and how do I get logging?"*

## The Council Protocol (mandatory — memorize this)

> **Any new signal / regime / bias / context indicator MUST:**
> **(a)** publish a `…State` seam to `SentinelCore` carrying its read as **int/double/bool** (never
> couple to an enum — publish `(int)`);
> **(b)** gate publishing behind a `PublishState` property that **DEFAULTS ON** — do **not** ship it
> dark (three voters shipped off-by-default once and silently contributed nothing);
> **(c)** be **wired into the Council** as a **voter** (directional → `AddVote`), a **modulator**
> (context that scales conviction), or a **veto** (hard gate), and appear in the Council's Reasons
> audit.
>
> **A hidden plot alone is NOT enough — the Council reads seams, not plots.**

## Step 1 — publish your `…State` seam

### 1a. The default-ON property (in `SetDefaults`)

```csharp
[Display(Name = "Publish to Sentinel",
    Description = "Publish my read as SentinelCore.MyState so the Council gains a voter.",
    Order = 20, GroupName = "Sentinel")]
public bool PublishState { get; set; }

// in OnStateChange → State.SetDefaults:
PublishState = true;   // ON out of the box — never ship dark
```

### 1b. The publish call (end of the compute path in `OnBarUpdate`)

Object-passing style (the newer convention — Compression/Location/MTF/Intermarket use it):

```csharp
if (PublishState && Instrument != null && Instrument.MasterInstrument != null)
{
    try
    {
        SentinelCore.SetMyState(new SentinelCore.MyState
        {
            Instrument = Instrument.MasterInstrument.Name,   // ROOT symbol, not FullName
            Direction  = (int)myDir,                          // publish int, never an enum
            Source     = "MySensor"
        });
    }
    catch { }   // publishing must NEVER throw into OnBarUpdate
}
```

Scalar-arg style (older seams like ADX/Trend) is identical in spirit —
`SentinelCore.SetAdxState(name, adx, diPlus, diMinus, bias, slope5, strong, "ADXPro");`.

**Rules that bite if you ignore them:**
- Key on `Instrument.MasterInstrument.Name` (root), because every consumer reads by root.
- Publish **every processed bar** so the seam stays fresh — consumers staleness-gate, so a
  seam you stop refreshing goes stale and your voter silently abstains.
- Warmup-guard reads that need history (`if (PublishState && warm > 0)`).
- If your `…State` type doesn't exist yet, you add it to `SentinelCore` following the existing
  pattern (a `sealed class` payload + `Set/Get/All` trio) — that's a Core version bump.

## Step 2 — wire it into the Council

Adding a **voter** touches essentially one line of logic (the plumbing — tallies, chips, reasons,
publish — is generic and iterates the internal `_votes` list, so it picks up your voter for free).

**2a. Add a weight property** (Weights group) and its `SetDefaults` default:

```csharp
WeightMyNew = 0.6;   // in SetDefaults

[Range(0.0, double.MaxValue)]
[NinjaScriptProperty]
[Display(Name = "Weight — MyNew", Order = 9, GroupName = "Weights")]
public double WeightMyNew { get; set; }
```

**2b. Read the seam and cast the vote** — inside the gather `try{}` in `OnBarUpdate`, alongside the
existing voters:

```csharp
var mine = SentinelCore.GetMyState(inst, StaleSec);
if (mine != null)
    AddVote("MINE", mine.Direction, WeightMyNew, ref netScore, ref activeW, ref voters);
```

That's it. `AddVote` collapses the value to `Math.Sign(dir)`, adds `sign×weight` to `netScore`,
adds `weight` to `activeW`, increments the fresh-voter count, and appends to `_votes` (which drives
the agree/disagree tally, the card chip, and the `MINE▲/▼/~` token in the Reasons string).

**Contract for a voter:**
- Pass a **raw signed value** as `dir` (AddVote takes the sign; magnitude is discarded).
- Pass **`0`** when your sensor is present-but-neutral, so it still counts toward breadth
  (`MinVoters`) without moving the score.
- Fold "strength" into the **weight**, like CCI/ADX do:
  `WeightMyNew * (mine.Strong ? 1.5 : 1.0)`.
- Keep it in the shared `try/catch` so an absent seam abstains fail-open.

**If your sensor is context, not direction** → make it a **modulator** instead: after the base
conviction is computed, multiply it (`conviction *= myDamp;`) and add a suffix to `BuildReasons()`.
**If it's a hard gate** → add a branch to the veto `if/else if` chain that sets `vetoed=true`.

## How the vote actually fuses (so weights make sense)

```
per voter:   vote = sign(dir) × weight        (neutral/absent → no score contribution)

netScore = Σ vote                              activeW = Σ weight (directional voters only)

Bias        = +1 if netScore >  deadband·activeW
              −1 if netScore < −deadband·activeW      (else 0 = FLAT)   deadband default 0.15

Conviction  = |netScore| / activeW             ∈ [0,1]   (1 = unanimous)
              × breadth-damp × squeeze × clock × rvol × mtf × location   → clamp [0,1]

SizeMult    = (Vetoed OR Conviction < floor) ? 0 : Conviction            floor default 0.35
```

The **modulators** (all multiply conviction, all fail-open): breadth (`voters/MinVoters` if short),
VolEnvelope squeeze (`×0.6`), Clock (`×OffSessionDamp 0.50` off-session / `×MiddayDamp 0.85`
midday), Participation (`×min(1, max(RvolDampFloor, rvol))` — thin tape damps, never inflates), MTF
(`×MtfCounterDamp 0.60` when higher TF disagrees), Location (`×LevelDamp 0.70` when price is heading
into a level).

The **hard vetoes** (first match wins, each zeroes conviction): global kill → scoped kill →
rollover → news lockout → clock kill-window → **liquidity wall on the intended side**
(`LiquidityState.BlocksEntry(bias, ticks)`).

> **The weights ARE the edge.** Tune them, ship them ON, then let **Lens** grade whether agreement
> actually paid. That grading loop is why every executor must record the verdict on each fire.

## Step 3 — consume a seam (executor side)

The whole point of the Council is that a strategy reads *one* seam instead of re-deriving
confluence:

```csharp
var v = SentinelCore.GetCouncilState(instrument, maxAgeSec);
if (v != null && v.HasEdge && v.Aligned(myDir))          // HasEdge = !Vetoed && Bias≠0 && Conviction>0
{
    int qty = (int)Math.Round(baseQty * v.SizeMult);      // SizeMult ∈ 0..1; 0 when vetoed
    // ...then STILL gate at submit (next line) — the seam is advisory, not the safety gate
}
```

Sizing helpers: `SizeForRisk(Account, Instrument, stopTicks, riskDollars)` (contracts for a $-risk)
and `SizedQuantity(Account, baseQty)` (profile-scaled).

## Step 4 — logging: two facilities, don't conflate them

### 4a. `sentinel.log` — human/audit text (use this, never `Print`)

```csharp
SentinelCore.Log("MyTool", "GC LONG conv=0.72 size=0.72 | EYE▲ TRND▲ …");
```

Writes `[Sentinel:MyTool] …` to the Output window **and** appends (timestamped) to
`…\Sentinel\sentinel.log` (rotates ~5 MB). **Convention: log CHANGES only, never per-tick.** The
Council logs only on a bias flip or veto toggle; rate-limit anything chatty.

### 4b. `SentinelCore.Ledger` — the daily JSONL event stream (the single source of truth)

This is the one event stream the Dashboard Journal/Slippage/audit views read. **Never build a
second journal.**

```csharp
SentinelCore.Ledger.Order(acct, instr, action, type, qty, price, tag);        // a submission
SentinelCore.Ledger.Action(kind, acct, detail);                               // a state change
SentinelCore.Ledger.Fill(acct, instr, action, qty, intended, fill, tickSize, tag);  // → slip ticks
SentinelCore.NoteOrderSubmitted(account);                                     // + rate guard

// read back as typed rows:
List<SentinelCore.Ledger.Entry> rows = SentinelCore.Ledger.ReadRecent(days);
```

Call `Fill` from `OnExecutionUpdate` (it computes intended-vs-actual **slip ticks**, feeding the
Slippage view). **On every FIRE, record the Council verdict** (bias/conviction/reasons) into the
Ledger/Log context — that's what lets Lens answer *"did the confluence pay?"*

## Step 5 — draw the glass card (SentinelSkin)

Hold one `Painter`, `Begin()` per frame, dock via `CardLayout`, `Dispose()` in `Terminated`:

```csharp
private readonly SentinelSkin.Painter _sp = new SentinelSkin.Painter();

protected override void OnRender(ChartControl cc, ChartScale cs)
{
    base.OnRender(cc, cs);
    if (RenderTarget == null) return;
    _sp.Begin(RenderTarget);
    var slot = SentinelSkin.CardLayout.Place(this, ChartPanel,
        ChartPanel.X, ChartPanel.Y, ChartPanel.W, ChartPanel.H, CardCorner, 244f, 148f);
    var r = _sp.Card(slot.X, slot.Y, 244f, 148f, active ? SentinelSkin.CAccent : SentinelSkin.CWarn);
    _sp.Dot(r.Left + 4f, r.Top + 8f, SentinelSkin.CAccent);
    _sp.Text("MY TOOL", r.Left + 15f, r.Top - 1f, 160f, 18f, SentinelSkin.CInk, 12f, semibold:true);
    _sp.Pill(state, r.Right, r.Top - 2f, dotColor);
    _sp.Track(r.Left, r.Top + 96f, r.Width, frac, SentinelSkin.CAccent);
    _sp.End();
}
protected override void OnStateChange()
{
    // ...
    if (State == State.Terminated) { _sp.Dispose(); SentinelSkin.CardLayout.Release(this); }
}
```

- Expose `[NinjaScriptProperty] SentinelCardCorner CardCorner` (default `TopRight`) so cards spread
  across corners; same-corner cards **auto-stack** so they never cover each other.
- **⚠ Position from `ChartPanel.X/Y/W/H`, NOT `chartControl.ActualHeight`** — ActualHeight includes
  subpanels + time axis, so a bottom-anchored card lands off the price panel and clips invisibly.

## Step 6 — the label remover (mandatory) + naming

```csharp
[NinjaScriptProperty]
[Display(Name = "Show indicator label", GroupName = "Sentinel", Order = 100)]
public bool ShowIndicatorLabel { get; set; }
// SetDefaults:  Name = "MyTool_v1_0_0"; ShowIndicatorLabel = false;
// DataLoaded (FIRST line):  if (!ShowIndicatorLabel) Name = string.Empty;
```

The chart's top-left label reads the runtime `Name` (not `ToString()` — that override is inert).
Blank it at `DataLoaded` for a clean chart; identity/serialization still use the `SetDefaults`
value. **⚠ Order-submitting tools must first capture `Name` into a stable `_tag` field** before
blanking it, or order-tag identity breaks.

**Naming/namespace rules — the FEDERATED NAMING LAW (ratified 2026-07-07; 4 layers):**
- **① Display `Name` = `"Sentinel <Thing>"`** (mandatory prefix; display-only, so safe to set/retrofit
  any time). **② Namespace** → `…{Indicators|Strategies}.Sentinel` (picker "Sentinel" folder).
  **③ Class + file** → `Sentinel<Thing>_vX_Y_Z` (lowercase `v`, three parts — e.g. `SentinelTrend_v1_0_0`).
  **④ Runtime** → cyan glass card + label remover (+ publish a `…State` seam for sensors).
- Layers ②③ are **serialization identity** — adopt at a tool's **next version bump only** (changing
  namespace/class drops it off saved charts). Layer ① is display-only and can be retrofitted now.
  See the design system §7 (canonical) — this supersedes the earlier "drop the prefix" convention.
- **⚠ AMENDMENT (2026-07-08) — STRATEGIES STAY IN THE BASE NAMESPACE.** Layer ② applies to indicators
  only. A **strategy** must remain in **`NinjaTrader.NinjaScript.Strategies`** (NOT `.Strategies.Sentinel`) —
  NT's **Strategy selector hides sub-namespaced strategies** (verified building the Bridge). Keep the
  display-`Name` prefix (`"Sentinel Bridge"`) and the `Sentinel<Thing>` class name; just leave the
  namespace at base. (Indicators still cluster under the `.Sentinel` picker folder as before.)
- **Namespace + class name = serialization identity.** Changing either drops the tool off saved
  charts. Adopt `.Sentinel` only at a **version bump**; old versions stay frozen in their old
  namespace and get archived out of the tree.
- **Custom `[NinjaScriptProperty]` enum gotcha:** declare the enum in the class's own `.Sentinel`
  namespace **and** add `using …Indicators.Sentinel;` at the file top — NT's generated host-wrapper
  references the class *qualified* but leaves the enum param *bare*, and it shares this file's
  `using` directives.
- **Fork gotcha:** after `cp`-ing a versioned tool, strip **all** NT generated `#region`s to EOF, or
  a running NT re-appends them and you get CS0111/CS0102.

---

# Part VI — Rapid MAE/MFE testing with the Excursion lab

This is the workflow that turns "I think FC signals are good" into "FC-Short on GC has a +6t/trade
edge at a 40t TP / 30t stop, firing 1.8×/day" — and one click writes that into the strategy.

## The concept: measure the price path, don't trade it

**MAE** = Max Adverse Excursion (how far a signal went against you before working out).
**MFE** = Max Favorable Excursion (how far it went in your favor).

If you know the *distribution* of MFE and MAE for a signal, you can pick a take-profit and stop that
actually match how that signal behaves — instead of guessing. The trick is to record the **full,
untruncated** price path after every signal, so you can *simulate* any TP/SL afterward against real
excursions.

## The three pieces

```
   SentinelExcursionRecorder_v1_4 →   *.jsonl in \Sentinel\Excursions\
   (indicator; records every signal's         │   (now incl. the COUNCIL verdict
    full MFE/MAE path, tagged)                 ▼    as a "COUNCIL" signal, schema 1.2)
                                    SentinelExcursions_v1_0  (analytics engine)
                                    expectancy grid · best TP/SL · Eye + Conviction referees
                                                 │
                                                 ▼
                                    Dashboard → Excursion tab (charts + ◆ + Apply)
                                                 │  writes
                                                 ▼
                                    \Sentinel\GTraderConfigs\<inst>_<sig>_<dir>.conf
                                       (Council fires → <inst>_COUNCIL_<dir>.conf)
                                                 │  auto-read (UseSentinelConfig)
                                                 ▼
                                    GTrader21 v0.1.7   ·   Sentinel Bridge (reads COUNCIL.conf)
```

## Piece 1 — `SentinelExcursionRecorder_v1_4` (the recorder)

- **File:** `Indicators/SentinelExcursionRecorder_v1_4.cs` (`Indicators.Sentinel`) — **renamed from
  `SignalExcursionRecorder_v1_3`** when it gained Council-fire recording.
- **What it does:** hosts its own **GodTrades21** (fixed GTrader21-v0.1.4 params → identical
  signals) + an `ADX(14)`. Edge-detects six signals — **BG** (Bollinger gap), **FC** (continuation),
  **OBR** (outside-bar reversal), each Long/Short. It places **no orders and never truncates the
  path.**
- **✅ Also records the COUNCIL verdict** as its own **"COUNCIL"** signal (schema **1.2**): every time
  the Council's bias edges, it logs that fire's full MFE/MAE path tagged with the **conviction bucket**
  (LOW / MID / HIGH). This is what lets the lab characterize the Council itself and write a
  `<inst>_COUNCIL_<dir>.conf` that the **Sentinel Bridge** auto-reads for its real TP/SL.
- **Per signal it records** (in ticks, relative to the fire price): ratcheted `MaxMFE`/`MaxMAE`,
  bars/ms to each, and **milestone snapshots at 1/5/15/60 minutes** (`mfe15`, `mae15`, …).
  Milestones not reached = `null`.
- **Window:** fire → **EOD** (deliberately the superset, so any shorter horizon can be sliced later).
- **Tags each record with:** `regime` (from ADX — trend ≥25 / chop ≤18 / mid), raw `adx`, **Eye
  verdict** (realtime fires: `eyeHad/eyeScore/eyeDir/eyeAligned`), `inst`, and `bartype`.
- **Writes:** JSONL to `…\Sentinel\Excursions\{timestamp}__{inst}__{bartype}.jsonl` (a **new file
  each load/F5** — the analytics dedupes). Schema `"1.2"` (was `"1.1"`; 1.2 adds the COUNCIL signal +
  conviction bucket).
- **Config:** there are **no TP/SL/horizon inputs** — it records everything and you slice in
  analysis. Properties are just presentation (`ShowInfo` card, `CardCorner`, `ShowUnderlyingIndicator`,
  `ShowIndicatorLabel`). *Which* signal you analyze is chosen later in the dashboard.

## Piece 2 — `SentinelExcursions_v1_0` (the analytics)

Reads every `*.jsonl`, **dedupes** by `inst|bartype|signal|dir|fireTime`, drops legacy schema, and
groups by `inst|signal|dir`. Per group it computes (all in ticks):

- Median MFE/MAE at 5/15/60 min; 75th-pct at 15m; `MaxMaeP90` (the tail — "why a stop is
  essential"); `HasEdge = MfeMed15 > MaeMed15`.
- **Fire-rate:** `FiresPerDay` over distinct fire-dates ("a +EV signal firing twice a month isn't a
  business").
- **Partitions** by regime (trend/mid/chop) and by Eye (endorsed / not).
- **Eye referee:** does Eye-endorsed out-earn the rest at 15m by ≥3 ticks? → +1 / −1 / 0.
- **The TP/SL simulator (`TpStopGrid`):** **12 configs** — TP ∈ {33rd, 50th, 67th pct of MFE15};
  Stop = TP × {0.5, 0.75, 1.0, 1.5}. Stop is a **fraction of TP** so R:R can't be gamed with wide
  stops. Per record over the 15-min window it decides win(+tp)/loss(−sl)/scratch and reports
  `HitRate` + `Exp` (est ticks/trade). **It's an estimate, not a path-level backtest** — the real
  number comes from applying it in execution.

## Piece 3 — the Excursion dashboard tab (read → decide → apply)

- **`★` = best raw EV** (often a wide-stop mirage). **`◆` = best RESPONSIBLE** — highest-Exp config
  with **Stop ≤ TP** (R:R ≥ 1). **The ◆ is what you apply.**
- **Confidence:** `n ≥ 30` = confident; the edge chart dims small-sample rows.
- Panels per signal: **growth line** (median MFE vs MAE at 5/15/60), **outcome scatter** (each fire's
  MAE15 vs MFE15, colored by regime, hollow ring = Eye-endorsed, dashed TP/SL lines), the **12-config
  expectancy grid**, and the **Eye referee** verdict.
- **✅ ⑤ the Conviction referee** — for the **COUNCIL** signal group, the tab now grades whether the
  HIGH-conviction bucket out-earns MID/LOW (does the Council's confidence actually pay?), the same way
  the Eye referee grades Eye-endorsement.
- **Apply ◆ for a Council group** writes **`<inst>_COUNCIL_<dir>.conf`** — which the **Sentinel
  Bridge** auto-reads (`UseSentinelConfig`) as its live TP/SL. This closes the recorder → ◆ → `.conf`
  → Bridge loop.

## The end-to-end workflow (step by step)

1. **Record.** Add `SentinelExcursionRecorder_v1_4` to each chart/instrument/bar-type you want to
   characterize (e.g. GC 100T, NQ 1000T, ES 500T). Leave GTrader21 off — or run it on a
   `GTrader21_Measurement` template with **all** TP/SL/reverse/BE/trailing/cutoffs **OFF** so nothing
   truncates the path. Let it run; it flushes each signal at EOD to `…\Sentinel\Excursions\`.
2. **Load.** Dashboard → **Excursion** tab → **"Load / Refresh excursions."** Status shows unique
   records / files / groups (and dupes/legacy skipped). Optionally tick **"Confident only (n≥30)."**
3. **Scan the edge chart.** One diverging bar per signal group (trend regime): green MFE vs red MAE
   at 15m, ranked by edge, "✓" when `HasEdge`.
4. **Drill in.** Pick a group in **"Detail signal"** → read the growth line, the outcome scatter, the
   expectancy grid, and the Eye referee.
5. **Find the ◆ and apply.** Click **"Apply ◆ to GTrader21 config"** → writes
   `…\Sentinel\GTraderConfigs\<inst>_<signal>_<dir>.conf` (e.g. `GC_FC_Short.conf`) with the ◆ TP/SL
   (+ `useEyeGate` if the referee is conclusive). Or **"Sync all ◆ configs"** to write one for every
   confident group with positive Exp.
6. **Strategy auto-reads it.** On the target chart's **GTrader21 v0.1.7** (group "14. Sentinel
   Integration"): set **`UseSentinelConfig = true`** and **`SentinelConfigName = "GC_FC_Short"`**. On
   `DataLoaded` it overrides the dialog's TP/SL/trend-filter/Eye-gate with the lab values *before any
   trade*, and republishes what it loaded so it appears in the dashboard's "Active lab configs" list.

### The `.conf` format (what Apply writes / the strategy reads)

`key = value`, one per line, `#` comments. Only five keys are *applied* by the strategy (the rest are
informational):

```
instrument = GC
signal = FC
direction = Short
useTrendFilter = true        # → UseTrendFilter
trendAdxThreshold = 25       # → TrendAdxThreshold
useEyeGate = false           # → UseEyeGate  (only emitted when the Eye referee is conclusive)
takeProfitTicks = 40         # → ProfitTargetTicks + UseProfitTarget = true
stopLossTicks = 30           # → StopLossTicks + UseStopLoss = true
```

> **First real finding from this lab:** signal quality is **FC > BG > OBR**, and **FC-in-trend
> (ADX ≥ 25) is the first cross-instrument edge; OBR ≈ noise** — which is why every generated
> `.conf` hard-codes `useTrendFilter = true` / `trendAdxThreshold = 25`.

> **⚠ Fill-resolution caveat (hard-won):** validate the chosen TP/SL at **High/Tick fill
> resolution**, not bar-level. Bar-level excursion analysis is optimistic — one strategy's apparent
> 81% win rate collapsed to 37.5% at tick fills. The Excursion grid is a *screen*, not a verdict.

---

# Part VII — The safety model

The suite is being hardened to be safe for a **funded/prop** account. Three substrates live in
`SentinelCore`, plus one policy that governs how everything uses them.

## The policy: fail-OPEN vs fail-CLOSED (know which you are)

| Actor | On an ambiguous/absent signal | Why |
|---|---|---|
| **Manual tools** (Deck manual actions) | fail **OPEN** (allow) | a human is in the loop |
| **Automated tools** (GTrader21, Copier, Deck auto-fire, Bridge) | fail **CLOSED** (block) | no human to catch a mistake |
| **Exits** | never gate | you must always be able to get out |
| **A thrown exception** | fail **OPEN** | resilience ≠ gate bypass; a bug must not freeze exits |

## Substrate 1 — the Order Gate (the one pre-submit choke point)

```csharp
var gate = SentinelCore.GateEntry(Account, Instrument, requestedQty, stopTicks, riskDollars, Instrument);
// gate.Level ∈ { Clear, Advisory, Hard };  gate.Size = risk-clamped qty
// automated tool: submit only if gate.IsClear
// manual tool:    surface gate.IsHard loudly, but submit anyway (fail-open)
```

**HARD** = kill / scoped-kill / loss-stop / rate-cap / qty-cap / contract-limit. **ADVISORY** =
feed / target-done / session / rollover / news / undersized. There's an opt-in **hard auto-flatten**
(`hardEnforce=true`) and a **fat-finger rate guard** (`SetOrderGuards`, `NoteOrderSubmitted`).
Risk-sizing: `SizeForRisk(acct, instr, stopTicks, riskDollars)`, `TickValue(instr)`.

## Substrate 2 — the Ledger + State store (memory)

- **Ledger** — append-only **daily JSONL** at `…\Sentinel\Ledger\ledger-YYYY-MM-DD.jsonl`, recording
  `Order` / `Action` / `Fill` events (Fill computes slip ticks). Read API: `ReadRecent(days)`,
  `ReadDay(date)`, `Parse(line)` → typed `Entry`. Wired into **all three** order sources
  (GTrader21/Deck/Copier). This is the audit trail and the Dashboard Journal/Slippage source.
- **State** — keyed **atomic blob store** at `…\Sentinel\State\<key>.json`, survives restart. Used
  for GTrader21 **position-state persist/restore** (restore is opt-in, safe 3-case, never dup-stops)
  and reconnect reconciliation (naked-position detect + alert).

## Substrate 3 — Alerts (health)

`SentinelCore.Alerts.Critical/Info/Raise` (2-tier) → the `Raised` event → **SentinelAlertService**
turns it into sound + optional push shell command (`Sentinel\Alerts.conf`). Plus the clock/TZ reset
(`resetHour`), the pre-trade **readiness** view, and a config-git repo under `Documents\NinjaTrader
8\Sentinel\`.

## The account-level gates (what `CanEnter` actually checks)

```
CanEnter(instrument, account) =
    NOT global kill
  · NOT scoped instrument kill
  · feed healthy (FeedHealthProbe)
  · governor: TradingAllowedToday (daily cap / loss-stop)
  · trailing drawdown: DrawdownAllowsEntry (cushion not thin/breached)
  · inside the account-profile session window
  · NOT in rollover blackout
  · NOT in a news lockout
```

Feed health is filled by **Sentinel Risk**; news lockouts come from `News.conf` (fed by the user's
`EconomicCalendar.py`); the governor + drawdown + profiles come from `Profiles.conf`/`Governor.conf`.

## Current safety status

**The entire offline hardening build is COMPLETE and compiles clean.** What remains is **live,
market-open validation** via the Dashboard **Test** tab and the test tracker: kill-switch proof,
alert sound, GTrader21 restore (no dup stop), stop-fill slippage, auto-flatten, and reconnect
naked-position alert. Until those pass live, treat the safety layer as *built but unproven.*

## Operate it: proving the safety system

Building a safety net is worthless until you've watched it catch something. The dashboard **Test
tab** is the "prove it" surface, and most of it works **with the market closed** — do these before
you ever risk a funded dollar. (Full checklist: `SENTINEL_TEST_TRACKER.md`; this is the operator's
short form.)

### Do these now (market closed / SIM)

1. **Alert channel.** Test tab → **Test Critical** → you hear a sound (needs `SentinelAlertService`
   loaded via F5) + a row appears in Risk ▸ Recent alerts + a line in the ledger audit. Set a
   `pushCommand` (ntfy/Pushover/Slack) → **Test Critical** → confirm the phone push. **Save & apply**
   and re-fire — it should take effect with **no NT restart.**
2. **Dry-run gate probe.** Pick an account, type an instrument (`MES 03-25`), qty/stop/risk →
   **Evaluate** → expect **GATE = CLEAR** (green) + a sized qty + TickValue + SizeForRisk. Now engage
   the top-bar **KILL** → Evaluate again → **GATE = HARD** (red, "kill-switch engaged"). Release kill.
   **No order is ever sent** — this is classification, safely.
3. **Self-checks.** Set a valid account+instrument, **Run checks** → expect **3/3 PASS** (scoped-kill
   isolation · sizer unaffordable→0 / generous→≥1 · TickValue > 0).
4. **Ledger is flowing.** Fire a Deck order on SIM, toggle the kill-switch → open
   `…\Sentinel\Ledger\ledger-YYYY-MM-DD.jsonl` → one JSON line per order + per kill toggle. Then the
   **Journal** tab ▸ **Today** should show the order row and a `KILL-ENGAGED`/`KILL-RELEASED` action
   row; toggle **▶ Live** and fire a test alert → it appears within ~2s.
5. **Risk sizer (Deck).** Drop the Deck, toggle **$ RISK** (cyan), set Stop tk + Risk $ → the
   `= N @ $X/c ($Y risk)` line computes; a tiny Risk $ with a wide stop shows the amber **"<1 lot"**
   warning (it will **not** silently over-size).
6. **Clock/TZ.** Risk tab → governor section shows `⏱ resets HH:00 local`. Set `resetHour=17` in
   `Profiles.conf` → confirm it matches your prop firm's reset. **A wrong reset silently breaks the
   daily rule** — this is a five-second check that saves an account.

### Do these live / on SIM with the market open (the tracker's bucket B)

- **① Kill-switch — the #1 proof.** In a SIM position with working orders, engage the kill-switch →
  new entries blocked **everywhere**; confirm what it does to existing orders (with auto-flatten
  armed vs not).
- **② The fail-open/closed split, for real.** Kill ON → **GTrader21 and the Copier refuse new
  entries** (fail-closed) while the **Deck still lets a human click** (fail-open). This is the core
  policy, observed live.
- **③ Hard daily-loss auto-flatten.** SIM account with **HardEnforce armed** + a small `dailyLoss` →
  trade into the stop → the account **auto-flattens and locks out** (governor DayHalted); exits still
  allowed. With HardEnforce **off** → advisory only (the safe default).
- **④ GTrader21 restore, no duplicate stop.** Turn on **"Restore Position State On Restart"** (group
  14). Take a SIM trade headless, let BE arm / trail advance, then **F5 or disable→re-enable**
  mid-position → confirm one of the three logged outcomes (restored / partial / unowned) and — the
  key check — **NO second stop is ever created.**
- **⑤ Stop-fill slippage.** Run GTrader21 on SIM with a stop/target, let a protective order **fill**
  → the **Slippage** tab shows `evt:"fill"` with intended/fill/slip; a stop that filled worse shows
  **red (positive) slip**, a target with improvement shows **green (negative).**
- **⑥ Reconnect / naked position.** On a governed account, open a SIM position with no stop → a
  critical **⛔ NAKED POSITION** alert (it's an alert, not a block — you can still exit). Add a stop →
  it clears. The real test: pull the ethernet mid-position to drop the stop, then reconnect.

> **The funded-account gate:** don't trade real prop money until ①–⑥ have each shown ☑. A safety
> system you haven't watched fire is a story you're telling yourself.

## Appendix — the Consistency Governor & prop-firm rules

If you trade a funded/prop account, two rules can end it that have nothing to do with your strategy:
the **trailing drawdown** and the **consistency rule**. The suite handles them as a division of labor:

- **Sentinel Risk** owns the **trailing-drawdown** breach (the real-time floor vs. the firm's
  −$4,500-type limit) + feed health. It drives the kill-switch.
- **The Consistency Governor** (hosted in Risk, published on `SentinelCore`) owns the **daily
  distribution** — it stops you from *overtrading a green day* into a rule violation, and from
  bleeding out past a daily loss stop.

They compose; neither duplicates the other.

### The reframe that makes it painless

The consistency rule (Lucid 20% / Bulenox 40% / TPT 50%) says *"no single day may exceed R × your
total cycle profit."* Rather than track a ratio intraday, the Governor enforces the equivalent, simpler
invariant: **cap each day at `DailyCap = R × ProfitTarget`.** If no day ever exceeds that, then when
your total hits target, no day can exceed R × total — **compliance falls out by construction.**

| Firm | R (ratio) | Target | **DailyCap** | Firm preset |
|---|---|---|---|---|
| Lucid | 0.20 | $9,000 | **$1,800/day** | `lucid` |
| Apex | 0.30 | — | — | `apex` |
| Bulenox | 0.40 | $9,000 | **$3,600/day** | `bulenox` |
| TPT | 0.50 | $9,000 | **$4,500/day** | `tpt` |

### How it gates (two daily triggers, both reset at session rollover)

1. **Profit cap** — today's realized ≥ `DailyCap` → **DayComplete** → entries stop for the session
   (bank the base hit, don't give it back).
2. **Loss stop** — today's realized ≤ −`DailyLossStop` → **DayHalted** → entries stop (set *inside*
   the firm's trailing DD).

Exits are always allowed. An entry fires only if
`SlotLive(Arc) && EyeQualified(if gated) && TradingAllowedToday(Governor) && CanEnter(kill/feed/…)`.

### Where you configure it

Per-account **profiles** live in `Sentinel\Profiles.conf`, edited via the dashboard **Accounts tab**.
A profile carries the firm preset (or custom) `ratio/target/dailyLoss/session/contracts/size/ddType`,
and drives **sizing** (`SizedQuantity` = baseQty × SizeScale × RecommendedSize, clamped to
`ContractLimit`) and **per-account session gating**. Set the **`resetHour`** to your firm's daily
rollover (TPT 5 PM ET, etc.) — a wrong reset silently breaks the daily rule (see the Test-tab §6 check).

> **⚠ Prop rules drift constantly.** The numbers above are a snapshot; the raw per-firm research
> (TPT, Bulenox, Lucid — drawdown mechanics, buffers, payout rules, news lockouts) lives in
> **`Docs/PropFirmRules.md`**, and the governor's design in **`Docs/CONSISTENCY_GOVERNOR_SPEC.md`**.
> **Always verify against the firm's live knowledge base before trading.** The single most common
> way a funded account dies isn't strategy — it's the **EOD→intraday drawdown switch** (TPT PRO) or
> the **real-time trailing floor ratcheting on an unrealized peak** (Bulenox Option 1). Mark your
> open positions against the trailing floor on every tick; that's exactly what Risk is for.
>
> **Not yet built (be honest with yourself):** the phase-5 SizeScale/dilution after an over-cap day
> (`RecommendedSize` is 1.0 for now), and **live validation** of caps/loss-stop/session on a Sim
> account (Test tracker bucket B). Validate the P&L-reset semantics against how *your* firm reports.

---

# Part VIII — A worked example

> One trade signal, followed from the moment it's born to the moment Lens tells you whether it was
> worth taking. This is the whole suite in a single story. Numbers below are illustrative (to make
> the mechanics concrete); the *process* is exact.

**The setup:** you trade **GC** (gold). You suspect the GodTrades **FC** (continuation) signal is
your edge, but only in a trend. Here's how the suite turns that hunch into a graded, live,
risk-gated strategy.

### Act 1 — Measure the signal (no trading yet)

You put **`SentinelExcursionRecorder_v1_4`** on a GC 100-tick chart and leave it for a couple of weeks
(or run it over history). It never places an order — it just watches its internal GodTrades engine
fire FC/BG/OBR signals and records the **full price path** after each one: how far it ran your way
(**MFE**) and against you (**MAE**), in ticks, at the 1/5/15/60-minute marks, tagged with the
**regime** (from ADX) and the **Eye** verdict.

Every signal becomes one JSONL line in `…\Sentinel\Excursions\`. After two weeks you have a few
hundred FC-Short fires with their real excursions.

```
FC-Short fire  →  firePx 2043.6  →  path recorded  →  mfe15=52t  mae15=18t  regime=trend  eye=aligned
                                                       (…hundreds of these…)
```

### Act 2 — Find the responsible edge (Excursion tab)

Open the dashboard → **Excursion** → **Load / Refresh**. The edge chart ranks your signal groups.
**GC · FC · Short (trend)** shows green MFE ≫ red MAE at 15 minutes — a real edge. You pick it in
"Detail signal" and read the **12-config TP/SL grid**:

- The **★** (best raw EV) sits at a wide stop — a mirage; big EV bought with catastrophic risk.
- The **◆** (best *responsible*, stop ≤ TP) lands at, say, **TP 40t / Stop 30t**, ~**+6t/trade**
  expectancy at a **58% hit rate**, firing **1.8×/day**, `n = 140` (confident).
- The **Eye referee** says endorsed fires out-earn the rest → recommends the **Eye-gate ON**.

You click **"Apply ◆ to GTrader21 config."** It writes:

```
# …\Sentinel\GTraderConfigs\GC_FC_Short.conf
instrument = GC
signal = FC
direction = Short
useTrendFilter = true
trendAdxThreshold = 25
useEyeGate = true
takeProfitTicks = 40
stopLossTicks = 30
```

> **Reality check (don't skip):** the grid is a *screen*, not a verdict. Before trusting that 58%,
> re-validate it at **High/Tick fill resolution** — bar-level excursion is optimistic (§VI caveat).

### Act 3 — Hand it to the strategy

> **The executor is pluggable — it doesn't have to be GTrader21.** GTrader21 is used here because
> it's the strategy that **auto-reads lab `.conf` files today** (`UseSentinelConfig`). The suite is
> deliberately built so a **purpose-built strategy** (or the now-built **Sentinel Bridge**) can wear
> the same two hooks — read the `.conf`, gate on the seam — and slot straight into this loop. When you
> swap in that other strategy, only this Act changes; Acts 1–2 and 4–6 are identical. **Treat
> "GTrader21" below as "your Sentinel-consuming strategy."** The **Bridge** already does exactly this
> — it consumes `CouncilState` and reads `<inst>_COUNCIL_<dir>.conf` — so the loop below is now real,
> not hypothetical (it fired a SIM GC short off the Council this session).

On your live GC chart, add **GTrader21 v0.1.7** (or your chosen executor). Under group
**"14. Sentinel Integration"** set **`UseSentinelConfig = true`** and
**`SentinelConfigName = "GC_FC_Short"`**. On load it reads the `.conf` and **overrides its own
dialog** — TP 40 / Stop 30 / trend filter on / Eye-gate on — *before it can place a single trade.*
It republishes what it loaded, so the dashboard's **"Active lab configs"** list now shows this
instance running `GC_FC_Short`. The lab result is now the live rule.

> **The two hooks a new executor needs** to join this loop: (1) an opt-in **config reader** —
> `UseSentinelConfig`/`SentinelConfigName` that applies the five `.conf` keys (§VI) before trading;
> (2) a **seam consult + record** — gate on `GetCouncilState` (or a specific sensor), size via
> `SizeMult` → `GateEntry`, and write the verdict to the Ledger on each fire. That's the whole
> contract. See §V (consume a seam) and the **Bridge** thread.

### Act 4 — A signal fires (the gauntlet)

Price is trending down on GC. GodTrades fires **FC-Short**. Watch every gate the trade must clear:

```
FC-Short fires
   │
   ├─ Trend filter?  ADX 27 ≥ 25 ................................ ✔ (trend confirmed)
   ├─ Eye gate?      GetEyeVerdict("GC").Direction == -1 ........ ✔ (short-qualified)
   ├─ Council?  ✅    GetCouncilState("GC"): Bias -1, HasEdge,
   │                  Conviction 0.71 → size × 0.71 ............. ✔ (fused agreement)
   │                  (GTrader21 v0.1.7 UseCouncilGate consults the
   │                   Council, not the Eye directly — the decouple;
   │                   the Bridge gates on this seam by default)
   │
   ▼ intends to sell
   ├─ CanEnter("GC", acct)?  kill / feed / governor / drawdown /
   │                          session / rollover / news ......... ✔ all clear
   │
   ▼ at submit
   └─ GateEntry(acct, GC, qty=1, stopTicks=30, risk=$300)
        Level = Clear,  Size = 1 (risk-sized) ................... ✔  → SUBMIT to broker
```

If *any* HARD condition were true — kill-switch, a news blackout, a liquidity wall on the sell side,
the daily loss-stop — the trade is blocked and the reason is logged. Because GTrader21 is
**automated, it fails CLOSED**: ambiguity blocks. (If this were you clicking the **Deck**, it would
fail OPEN — you're the safety.)

### Act 5 — Fill, record, and manage

The sell fills at 2041.2 (intended 2041.4 → **0.2t adverse slip**). GTrader21:

- writes the fill to **`SentinelCore.Ledger.Fill(…)`** — captured with slip, visible in the
  dashboard **Slippage** tab;
- **records the Council verdict on this fire** (Bias −1, Conviction 0.71, Reasons
  `EYE▼ TRND▼ CCI▼ ADX▼ · trend`) into the Ledger context — this is the seed Lens will grade;
- sets its **40t target / 30t stop** (unmanaged — it owns its own exits);
- persists position-state to **`SentinelCore.State`** so a mid-trade recompile/restart re-adopts the
  stop instead of duplicating it.

The target hits. +40t, minus the slip. One clean, fully-audited round trip.

### Act 6 — Grade it (Lens closes the loop)

Days later you've got 30+ of these. Open the dashboard → **Lens**. It reads the trade log and answers
the only question that matters:

> **When the Council was confident (0.7+) on FC-Short, did those trades actually pay more than the
> low-conviction ones?**

- **If yes** → conviction is real edge. Lean into it; maybe raise the Eye or Trend weight.
- **If no** → "agreement" was just correlated price-lenses nodding along (the independence caveat,
  made concrete). The fix isn't more of the same voters — it's an **orthogonal** axis
  (Intermarket, Participation) carrying information the others don't have.

**That is the full Sentinel loop:** *measure → find the responsible edge → hand it to the strategy →
run it through the safety gauntlet → record every verdict → grade whether the confidence paid → tune
the weights → repeat.* Every part of this manual is one station on that loop.

> **Where this is today:** Acts 1–5 are **built and running** — and the loop is now closed both ways:
> the **Sentinel Bridge** consumes `CouncilState` and fired a SIM GC short off the Council this
> session (captured in the Ledger/Slippage tab), and **GTrader21 v0.1.7** gained the opt-in
> `UseCouncilGate` decouple (consults the Council, not the Eye directly). Act 6's grading loop is what
> **Lens** finishes now that every Bridge fire records the verdict. The gauntlet's live proofs (kill,
> auto-flatten, restore, reconnect) are still ⚠ pending market-open validation (§VII), and the
> illustrative numbers await an overnight recorder run (see below).

---

# Part IX — Glossary, file index, and open questions

## Glossary

- **Seam / bus** — a small typed fact published to `SentinelCore` (`…State`) that any tool can read.
- **Voter / modulator / veto** — a Council input that adds a directional vote / scales conviction /
  hard-blocks.
- **Bias / Conviction / SizeMult** — the Council's fused direction (−1/0/+1) / alignment strength
  (0..1) / position-size multiplier (0..1, 0 when vetoed).
- **MFE / MAE** — max favorable / adverse excursion; the raw material of TP/SL selection.
- **★ vs ◆** — best raw-EV config vs best *responsible* config (R:R ≥ 1). Apply the ◆.
- **Fail-open / fail-closed** — allow vs block when a signal is ambiguous/absent.
- **Root symbol** — `Instrument.MasterInstrument.Name` (e.g. `GC`); the seam registry key.

## File index (the load-bearing files)

| File | Role |
|---|---|
| `AddOns/SentinelCore_v1_0_0.cs` | The bus + Gate/Ledger/State/Alerts + all registries |
| `AddOns/SentinelSkin.cs` | Glass-card Painter + palette + CardLayout |
| `Indicators/Council_v1_0_0.cs` | The fusion brain |
| `Indicators/{Eye,SentinelTrend,WoodiesCCIPro,ADXPro,VolEnvelope,CompressionBase,Intermarket,SentinelWAE}_*.cs` + `BarsTypes/SentinelTBars_*` | The 9 voters (Brick = the bar type below) |
| `Indicators/{Clock,Participation,Location,Mtf}_v1_0_0.cs` | The orthogonal modulator axes |
| `BarsTypes/SentinelTBars_v1_0_0.cs` | Brick bar type → `BrickState` |
| `Indicators/SentinelExcursionRecorder_v1_4.cs` + `AddOns/SentinelExcursions_v1_0.cs` | The MAE/MFE lab (records the Council too) |
| `Strategies/GTrader21v_0_1_7.cs` | The automated executor (auto-reads lab configs; `UseCouncilGate` decouple) |
| `Strategies/SentinelBridge_v0_2_0.cs` | The autopilot — consumes `CouncilState`, records every fire |
| `Indicators/Deck_v0_2_2.cs` | The manual order deck + SIGNAL ARM |
| `AddOns/SentinelDashboard_v1_0_0.cs` | The 12-tab control center |
| `AddOns/Sentinel{Risk,Alert,State,Copier,LogEngine,LogService,Lens,Arc}Service*.cs` | The service layer |
| `Docs/SENTINEL_DESIGN_SYSTEM.md` · `SENTINEL-CONTRACTS.md` · `ROADMAP.md` | The specs this manual condenses |

## Screenshots to capture (placeholders for the polished manual)

A real manual needs pictures. These are the shots to grab (SIM/replay is fine); each slots into the
section noted. Drop them in a `Docs/img/` folder and we'll wire the `![]()` links.

| # | Shot | Goes in | What it must show |
|---|---|---|---|
| S1 | **The Council card, live** | Part 0, Part II | Bias pill, conviction %, size×, colored voter chips, tally footer |
| S2 | **A Council VETO** | Part V, Part VII | the red `VETO: …` line (e.g. news or a liquidity wall) |
| S3 | **The `Sentinel` picker folder** | Part 0, Part V | the indicator picker expanded, suite tools clustered |
| S4 | **Stacked glass cards (CardLayout)** | Part III, §3.0 | two+ Sentinel cards in one corner, auto-stacked, not overlapping |
| S5 | **Excursion tab — edge chart** | Part VI | diverging MFE/MAE bars ranked, ✓ on the edges |
| S6 | **Excursion tab — TP/SL grid** | Part VI, Part VIII | the 12 configs with ★ and ◆ marked |
| S7 | **Excursion tab — outcome scatter** | Part VI | MAE15 vs MFE15 dots, regime-colored, Eye rings, dashed TP/SL |
| S8 | **Dashboard — Test tab** | Part VII | the dry-run gate probe showing CLEAR then HARD |
| S9 | **Dashboard — Journal + Slippage** | Part VII | a fill row with intended/fill/slip, red vs green slip |
| S10 | **Deck with on-chart order visuals** | Part III | entry(cyan)/stop(red)/target(green) lines + R/$/tick chips |
| S11 | **The `.conf` handoff** | Part VI, Part VIII | GTrader21 group 14 `UseSentinelConfig` + the Active-lab-configs list |

## Open questions for us to resolve (before this becomes "the" manual)

1. **Audience split.** ✅ **CONFIRMED (user-approved 2026-07-07):** **ONE manual with two reading
   paths** (see "How to read this manual" up top) — no physical fork.
2. **The independence caveat.** ✅ ADDRESSED: framed in Part II, reinforced in the Part 0 "why the
   Eye is here" note and made concrete in Act 6 of the worked example. *Review:* prominent enough for
   a trader, or do we want a standing callout box on the Council card's own §3.1 entry?
3. **Bridge.** ✅ **BUILT (2026-07-08):** `SentinelBridge.cs` (base `…Strategies` namespace, display
   "Sentinel Bridge") v0.2.0 — consumes `CouncilState`, sizes ×`SizeMult` → `GateEntry`, records every
   fire, reads `<inst>_COUNCIL_<dir>.conf`. Live-validated on SIM (fired a GC short off the Council).
   Now a full §3.4 catalog entry, in the layer diagrams, and the real spine of the Part VIII loop.
4. **Screenshots.** ✅ STRUCTURED: the **"Screenshots to capture"** table above (S1–S11) lists every
   shot and where it lands. *Action on you:* capture them (SIM/replay fine) → drop in `Docs/img/`.
5. **A 5-minute quickstart.** ✅ DONE: Part 0 now leads with **4 sensors including the Eye** (per your
   call) + a note on *why* the Eye anchors the starter set, and points at the Excursion/Test tabs.
6. **Worked example.** ✅ DONE + made **executor-agnostic** (may not be GTrader21). *Still open (needs
   you):* the illustrative numbers (TP40/Stop30, +6t, 58%, 1.8×/day) are placeholders — but the **path
   to real numbers is now fully wired** (recorder records the Council → ◆ writes `<inst>_COUNCIL_<dir>.conf`
   → the Bridge auto-reads it). All that's left is an **overnight recorder run** on GC to swap the
   placeholders for measured output.
7. **Missing tools.** ✅ DONE: LiquidityWalls has a catalog entry (§3.3) and the **Consistency Governor
   + prop-firm rules** are folded into the Part VII appendix (grounded in the two specs, with the
   "verify against live docs" caveat). *Nothing open unless we want more firms.*
8. **Depth of the safety chapter.** ✅ DONE: Part VII has the "Operate it" section + the Governor/prop
   appendix. *Review:* enough for a prop trader, or split into a standalone runbook later?

### Still genuinely needing YOU (not things I can finish alone)
- **Real worked-example numbers** (Q6) — the pipe is wired (recorder → ◆ → `.conf` → Bridge); just
  needs an **overnight recorder run** on GC to produce measured TP/SL/expectancy.
- **The screenshots** (Q4) — S1–S11 (add S12: the Bridge card + ARM BRIDGE button when captured).
- ~~Confirm the structural calls~~ ✅ **user-approved 2026-07-07** (one-manual-two-paths,
  Eye-in-quickstart, executor-agnostic framing — all locked; manual reviewed).
- **Direction:** the **Bridge is built and SIM-validated** — next is **live/market-open validation**
  of the safety gauntlet (§VII) + the overnight recorder run to grade the Council's weights via Lens.
```
