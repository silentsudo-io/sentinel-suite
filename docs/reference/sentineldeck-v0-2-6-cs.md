---
layout: sentinel-ref
title: "SentinelDeck_v0_2_6.cs"
blurb: "Indicators · 0.2.6 · 3469 lines"
---

# SentinelDeck_v0_2_6.cs

> `bin/Custom/Indicators/SentinelDeck_v0_2_6.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 0.2.6 |
| **Size** | 3469 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelDeck_v0_2_6` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel` |
| **Consumes seams** | `GovernorState` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
one at https://mozilla.org/MPL/2.0/.

Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
─────────────────────────────────────────────────────────────────────────────
============================================================================
Sentinel Deck  manual discretionary order deck + account-tracking risk card
============================================================================
A NinjaTrader 8 INDICATOR (drop it on any chart with ChartTrader open). It is
the Sentinel Suite's manual-trading tool: its OWN Buy/Sell deck with full order
types (Market / Limit / Stop / Stop-Limit), three ways to set a working price
(tick-offset  editable box  click-on-chart), and a FLATTEN that is scoped to
ONLY this chart's instrument on the selected account. A flight-instrument risk
card (SharpDX) tracks the account live: day P&L, open position, uP&L, open risk.

DESIGN: identical "flight-instrument" language as SentinelDashboard + GTrader21
(void ground, ONE cyan accent = live/watching, green/red reserved for money +
direction). Seamless with the Sentinel skin.

ORDERS: account-level UNMANAGED orders via Account.CreateOrder/Submit  the deck
fully OWNS its orders (no strategy-position desync; none of the managed-framework
landmines). Account + instrument come from the native ChartTrader selectors.

SENTINEL: reads SentinelCore for kill / governor / feed / rollover / news as an
ADVISORY readout only  it NEVER blocks a click. A human must always be able to
act, especially to exit. (User decision, 2026-07-03.)

 v0.2.0  validate on a SIM account before going live. Live order submission.

CHANGELOG
  v0.2.6 (2026-07-28, INSTRUMENTATION ONLY — the fill-cost reference price; no order-logic change)
    Brings the Deck to the standard set by SentinelBridge v0.3.1 the same day. The Deck could not answer
    "what does a fill actually cost us?" either, but it failed HONESTLY where the Bridge failed silently:
      • `OnDeckExecution` set `intended = 0` for a MARKET order, and Ledger.Fill writes the `slip` field only
        when `intended > 0` ⇒ market fills carried NO slip field at all. An absence, not a false zero — but
        still 75 of the Ledger's 114 order rows with zero execution-cost information.
      • `SubmitDeckOrder` logged `lim > 0 ? lim : stp` as the order price, which for a MARKET order is 0/0
        ⇒ every Deck market order row reads `px:0`, so order→fill could not be joined either.
    FIX (mirrors the Bridge): latch the live BID/ASK in OnMarketData — the Deck already overrides it and
    already learned this lesson once, in v0.2.4, for the tape — then stamp the TRADEABLE quote on the side
    being crossed (buy → ASK, sell → BID) at SUBMISSION, and use it as the reference for both the Ledger
    order row and `intended`. Stop/limit are UNCHANGED: their trigger/limit price was always correct.
    ⚠ WHY THE LATCH AND NOT GetCurrentBid/Ask: SubmitDeckOrder runs on the WPF UI THREAD (button click) and
    OnDeckExecution on the account/execution thread — neither is the data thread. The latch is written in
    OnMarketData and read by both, exactly as `_lastTradePx` already is.
    ⚠ WHY A DICTIONARY AND NOT ONE FIELD: the Deck has FIVE market sites (deck buy/sell · Close · Reverse ·
    Half · HalfBE). A single shared field would let a `_Half` fill consume the stamp left by an earlier
    entry and silently report a fabricated cost. Keyed by order name, bounded like `_seenDeckExecIds`.
    ⚠ NOT stamped, correctly: FlattenThisChart uses NT's atomic `Account.Flatten`, whose order the Deck does
    not create and whose name fails the `_tag + "_"` filter in OnDeckExecution — so it never reaches this path.
    ⚠ THIS IS A REAL VERSION FORK, NOT AN IN-PLACE EDIT — file, class, header chip and DeckVersion all move
    to v0.2.6 together, and SentinelDeck_v0_2_5.cs is left FROZEN as the fallback checkpoint. The first cut
    of this change kept the v0_2_5 identity to avoid dropping the Deck off saved charts; that was the wrong
    call and the user corrected it: WE ARE DEVELOPING, AND VERSION BUMPS ARE PART OF THE GIG. Re-attaching an
    indicator is a dev-box inconvenience; a build whose BEHAVIOUR changed while its VERSION did not is how a
    bug report becomes unanswerable — and on a production trading box that ambiguity is the expensive one.
    ⚠ RE-ATTACH REQUIRED: namespace+class is an indicator's serialization identity, so v0.2.6 does NOT inherit
    v0.2.5's saved settings — it must be added to charts/workspaces afresh. Known, accepted cost of the fork.
    ⚠ Realtime only (the latch fills from live market data) and FORWARD-MEASURING — recovers nothing past.
   (in-place, 2026-07-25) — RECORDED CATCHES: 71 empty `catch {}` migrated to SentinelCore.Swallow
            (Core >= v1.41.0). Behaviour IDENTICAL (Swallow never rethrows); faults are now counted +
            logged. The Deck had the worst ratio in the suite (71 of 107 catches silent) and is the one
            tool in testers' hands, where an unreportable fault is worst. Class/version deliberately
            UNCHANGED — namespace+class is serialization identity and this build is published.
  v0.2.5r (2026-07-21, RENAME to the federated naming law — ZERO logic change)  file/class/Name restored to the
    "Sentinel <Thing>" tell: Deck_v0_2_2.cs → SentinelDeck_v0_2_6.cs, class Deck_v0_2_2 → SentinelDeck_v0_2_6,
    Name "Sentinel Deck v0.2.2" → "Sentinel Deck", header chip → v0.2.5. The FILENAME HAD BEEN LYING — it said
    v0_2_2 while the code was v0.2.5, which would have made every tester bug report ambiguous.
    This RESTORES the original name: the tool shipped as SentinelDeck_v0_1_0/v0_2_0, was renamed to Deck_v0_2_1
    under the 2026-07-05 "drop the prefix" convention, and that convention was REVERSED 2026-07-07 by the
    FEDERATED NAMING LAW (Docs/SENTINEL_NAMING_FEDERATION.md).
    ⚠ DONE NOW, BEFORE the public testers' preview, and deliberately not later: namespace + class name are an
    indicator's SERIALIZATION IDENTITY, so renaming after distribution silently drops the Deck off every tester's
    saved chart with no migration path. Cost of doing it now = re-add it once on your own charts.
    Display Name follows naming-federation §9 (ratified 2026-07-10): "Sentinel Deck v0.2.5 (DEV)". The (DEV)
    marker is CORRECT for the public testers' preview — this is the unfrozen head, auto-fire is still
    un-live-validated, and testers may run it on LIVE accounts, so it must never read as a frozen build in
    the picker. DROPPING " (DEV)" IS THE FREEZE STEP when it graduates to a supported rung.
  v0.2.2 (2026-07-09, in-place  THEME ONLY, no order-logic change)  The header THEME button now cycles SEVEN
    modes: auto → dark → light → silver → OBSIDIAN (true-black OLED) → BLUEPRINT (cyanotype) → AMBER (warm dark).
    ⚠ "amber" and "auto" both start with 'A', so the old first-letter button face BROKE. Faces are now an
    explicit ThemeGlyphs array; "auto" shows '~' (it isn't a theme — it means FOLLOW THE ACTIVE SKIN) and each
    theme keeps its initial: ~/D/L/S/O/B/A. The word→Theme if-chain is gone — CycleTheme now calls the public
    SentinelSkin.TryParseTheme, so a future theme needs no Deck logic change (only its glyph + mode word).
    Pairs with templates\Skins\Sentinel {Obsidian,Blueprint,Amber}\ + SentinelSkin.Theme.*.
  v0.2.5 (2026-07-14, order-line WPF OVERLAY)  New "Order lines ALWAYS ON TOP (overlay)" toggle (default OFF).
    SharpDX OnRender loses z-order to other indicators' cards, so the order-line pills render UNDER them. This
    draws the ENTRY/STOP/TARGET lines + pills on a hit-transparent WPF Canvas ABOVE all SharpDX rendering, so they
    can never be hidden. Drag/hover is chart-mouse-event based (OnChartMouseMove) → 100% UNTOUCHED; the overlay is
    purely the visual layer. Line DATA is shared via ComputeOrderLines() so the SharpDX + WPF paths never diverge.
    Default ON (validated live 2026-07-14). Also made the ORDER-LINE DRAG + hover-attach + click-to-set-price
    DPI-AWARE (DpiScale): the chart scale works in device px but WPF mouse events are DIPs, so at >100% display
    scaling the drag hit-test / snap-to-indicator grabbed where the line ISN'T. Now every mouse-Y is converted to
    device px before the chart-scale calls — works at any scaling (no-op at 100%). ENTRY shows the pill only (no
    line — NT draws its own). + a 1-per-bar Magic-trail diagnostic (Deck:trail): logs cci/atr/cand vs the lock so
    "not trailing" is diagnosable (it was the ratchet correctly holding a tighter lock, not a bug).
  v0.2.4 (2026-07-13, RAW-TICK tape)  The Log Tick Path capture now records the TRUE last-trade price via an
    OnMarketData override, not the synthetic brick Close[0] it read before. On an HA/TBars/Renko chart Close[0] is
    the (averaged) brick close — GC px came out as 4004.13345, off the 0.1 grid — so MFE/MAE were quantized/biased.
    OnMarketData is the only place the raw last-trade is visible; appends are now driven from there (every real
    trade, at its real price) while OnBarUpdate keeps the begin/end/reversal lifecycle. Entry/exit px also fall back
    to the last trade. Sidecar schema → "tick.2" ("src":"last"); the ingester keys off `kind` so it's compatible.
  v0.2.3 (2026-07-13, TAPE + Flatten fix)  (1) A "RECORD ▸ Log Tick Path" toggle (default OFF, persisted) that
    PASSIVELY captures the tick-by-tick price path of any manual trade on the chart instrument — begins on a
    Flat→in-position transition (account-level, so it catches native Chart Trader fills too), appends every tick
    while in, writes Sentinel\Excursions\ticks\<id>.jsonl on the return to Flat. NEVER touches the order path.
    Feeds the excursion management sandbox (grade ATR/Magic/BE+ trails over your REAL entries). (2) FLATTEN FIX:
    FlattenThisChart now uses NT's atomic Account.Flatten instead of a cancel-then-loop-Sleep(250) that RACED on a
    lagging feed and OVER-flattened into an OPPOSITE position.
  v0.2.2 (2026-07-05, SIGNAL persist + PRESETS)  Signal config now PERSISTS across F5/workspace save (serialized
    props under "4. Signal (saved)"); "Signal watch" is deliberately NOT persisted (auto-fire never silently re-arms).
    + in-Deck PRESET library (top of SIGNAL ARM): named presets capturing signal + entry (src A/B, rule, cadence,
    invert, mode, threshold, qty, stop/target tk, auto-on-entry). Pick from the dropdown to LOAD (forces watch OFF),
    type a name + Save to store, Delete to remove. Stored in SignalPresetsBlob (US/RS-delimited, serialized in-Deck).
  v0.2.2 (2026-07-05, SIGNAL ARM read-race fix)  BAR CLOSE cadence now reads the JUST-CLOSED bar (barsAgo=1), not
    the new in-progress bar. A one-bar PULSE plot (e.g. CompressionBase Signal = -1 on exactly the breakdown bar)
    was being missed: the Deck is Calculate.OnEachTick, so on the new bar's first tick the foreign indicator's
    CURRENT bar isn't computed yet (→ read 0). Now fires reliably on the bar AFTER the signal (confirmed/non-repaint).
    Added a live status readout ("watching · A=<val> · <DIR>") — A=n/a means the source ref didn't resolve.
  v0.2.2 (2026-07-05, SIGNAL ARM UX)  Moved SIGNAL ARM to the TOP of the panel (above ORDER TYPE) + made it a
    COLLAPSED-by-default collapsible section (Section() gained a `collapsed` arg; "+" chevron). Source A/B are now
    real DROPDOWNS (ComboBox, full plot names, no truncation) instead of cycle buttons — B has a "(none)" option;
    lists (re)fill on Rescan / watch-on / build. Selection resolves the ref on the UI thread (attach pattern).
  v0.2.2 (2026-07-05, +SIGNAL ARM)  New "SIGNAL ARM" section: arm/auto-fire Long/Short off ANY loaded indicator's
    PLOT — no hardcoded signals. Sources are discovered from ChartControl.Indicators at runtime (same plumbing as
    hover-attach). Pick Source A (+ optional B for a cross), a Rule (Sign(>0) / Rising / A x B / Threshold), Invert,
    a Mode and a Cadence. MODE: ARM (default) highlights the primed BUY/SELL button + status "ARMED LONG — click
    BUY" and a human confirms; AUTO-FIRE (opt-in, amber) submits automatically — FAIL-CLOSED through
    SentinelCore.GateEntry (a Hard reason BLOCKS, unlike a manual click), one-shot per bar, flat-only, opposite
    signal = REVERSE (never stacks), and it suppresses the state-at-enable so it only acts on a real change.
    CADENCE: Bar-close (default) / every Tick. Fires the existing SubmitDeckOrder path (automated flag → gate
    fail-closed + forced MARKET), so it inherits qty / risk-sizing / auto-bracket / fill-capture. Source keys are
    stable (Type#ordinal|plotIdx) so they survive indicator reloads; "Rescan sources" re-reads the chart.
    ⚠ AUTO-FIRE modifies live orders → SIM-validate. Companion: CompressionBase_v1_3_0 now exposes a hidden
    "Signal" plot (+1 BreakUp / -1 BreakDown) so its REAL breakout is a first-class source (Sign rule).
  v0.2.2 (2026-07-05, in-place UI fix)  TRAILING mode pills no longer clip their text: zeroed the button Padding
    + centered content + shortened the 4-col labels to uniform short forms (Trail / BE+ / BarHL / NBar / ATR /
    Magic / HalfBE). Labels are display-only (mode is bound via the enum arg, not the string) - purely cosmetic.
  v0.2.2 (2026-07-05)  DRAG-TO-ADJUST + HOVER-ATTACH on the on-chart order lines (⚠ MODIFIES LIVE ORDERS from
    the chart - SIM-validate before live):
     * DRAG: hover a STOP/TARGET line (resize cursor); left-drag to re-price the working order live (preview chip
       updates $/R/ticks); release re-prices via the proven o.StopPriceChanged/LimitPriceChanged + Account.Change
       path (same as Breakeven). Esc cancels. ENTRY line is read-only. Master toggle "Enable order-line drag".
     * HOVER-ATTACH: while dragging, if the line nears an OVERLAY indicator plot (MA/VWAP/CompressionBase base
       levels, ...), it snaps + BINDS the order to that plot; the order then re-prices each tick to follow the
       plot (throttled to >=1 tick moves). Attached line renders DASHED with a "-> <Indicator>" tag. Drag off to
       detach. "Attached stop: only-improve" prop (default off = free-follow). Drag is independent of attach
       (attach fails safe). v0.2.1 frozen (aligned + on-chart visuals checkpoint).
  v0.2.1 (2026-07-05)  SUITE-CONVENTION ALIGNMENT (no order-LOGIC change; validate on SIM):
    * REHOMED to namespace NinjaTrader.NinjaScript.Indicators.Sentinel + RENAMED class/file/Name = Deck_v0_2_1
      (strict naming; drops the redundant "Sentinel" prefix - the picker's "Sentinel" folder supplies it).
    * LABEL REMOVER (Sentinel standard): hides NT's chart name label by default (Name blanked at DataLoaded)
      with a "Show indicator label" toggle. NOTE: order tags were DECOUPLED from Name into a stable _tag field
      (captured before the blank) so blanking Name can't corrupt order identity / fill-capture matching.
    * Risk card now docks via SentinelSkin.CardLayout + the shared SentinelCardCorner enum (was a local
      RiskCardCornerPos + hand-rolled corner switch) - identical positions, now anti-overlap + stackable.
    + NEW FEATURE - ON-CHART ORDER VISUALS ("Show order lines", default on): draws the live position's
      ENTRY (cyan) / STOP (red) / TARGET (green) as horizontal chart lines with a left-anchored chip showing
      R-multiple, $ and tick-distance (STOP = -1R, TARGET = R/$ from the working bracket TP). READ-ONLY (no
      order path touched); target = nearest exit-side working Limit; all math on the data thread, drawn in
      OnRender under the risk card. Labels default to the RIGHT ("Order line label side"); lines default full
      width, editable via "Order line width %" (5-100, measured from the label side). Chip width is MEASURED
      (DirectWrite) so it never truncates. Chip sits ABOVE its line (clears NT's own on-line order label - the
      Deck's STOP/TARGET = NT's Sell STP / Sell LMT at the same price). Next: optional drag-to-adjust (Phase 2).
    + RISK CARD: BAR TIMER / tick counter row (bar-completion progress bar + bar-type-aware label: tick/vol/range
      count e.g. "87 / 150t", or time remaining for minute/second bars).
    + UI POLISH: collapsible management sections EXPANDED by default (- cyan chevron); dock button narrower
      ("[]" @ 22px, was "[ ]" @ 30); order-type pills wider gap (margin 3, 9.5px) so they don't run together.
    Old SentinelDeck_v0_1_0 / v0_2_0 archived out of the tree. See design-system 4b/7 + sentinel-namespace-and-naming.
  v0.2.0+ (2026-07-04, in-place  OBSERVABILITY ONLY, no version fork)  Sentinel Ledger fill capture.
    Pins an ExecutionUpdate subscription to the SELECTED account (re-points on selector change, dropped
    on Terminated) and logs DECK-originated fills (order name "<Name>_...") to SentinelCore.Ledger.Fill
    (intended = order stop/limit price vs actual fill  adverse slip ticks; 0/market  slip omitted).
    Feeds the dashboard Slippage view so MANUAL deck trades get execution-quality analysis too. Bounded
    ExecutionId dedupe. Pure observation in try/catch  never touches the order path, so it stays v0.2.0.
  v0.1.0 (2026-07-03)  FROZEN checkpoint (simple entry deck). Order-entry deck
    (Mkt/Lmt/Stp/StpLmt), qty stepper + presets, price entry (offset/editable/
    click-chart), BUY/SELL/REVERSE/CLOSE, FLATTEN-THIS-CHART, account risk card,
    Sentinel advisory. See SentinelDeck_v0_1_0.cs.
  v0.2.0 (2026-07-03)  FULL trade-management suite (ports GTrader21's engine):
     BRACKET / STOP  attach OCO (stop+target) or a protective stop to the open
      position; auto-on-entry cycle Off / Stop-only / Bracket.
     BREAKEVEN  move stops to entry  offset (manual + auto when profit  trigger).
     TRAILING  all 7 GTrader modes: TrailTicks  Breakeven+  Bar H/L  N-Bar H/L 
      ATR  TrendMagic (ATR gated by CCI regime)  Half+BE (scale half  BE). Manual
      arm + auto-trail-on-entry. Tick-level execution via OnMarketData; stop only ever
      improves (never moves against the position).
     SCALE  close half. Orders remain account-level UNMANAGED (the deck owns them).
    Engine adapted verbatim-in-spirit from GTrader21v_0_1_6Panel (no strategy-owned-
    order guards needed  the deck has no strategy).  validate on SIM before live.
    + POP-OUT / DOCK toggle in the header ([ ] / ><) - floats the deck into its own
      resizable window (geometry remembered) and docks it back into ChartTrader.
    + $ RISK sizing (Order Gate, hardening Phase 1): toggle "$ RISK", type $-risk, and the
      qty is computed from the Bracket "Stop tk" via SentinelCore.SizeForRisk. Every entry now
      routes through SentinelCore.GateEntry (kill/loss-stop/rate/qty-cap = Hard, surfaced loudly;
      feed/session/rollover/news = Advisory) -- the Deck fails OPEN (never traps a human) and
      records each submit for the fat-finger rate guard. See Docs/SENTINEL_HARDENING_FRAMEWORK.md.
============================================================================
```

