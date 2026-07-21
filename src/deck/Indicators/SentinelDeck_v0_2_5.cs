// ============================================================================
// Sentinel Deck  manual discretionary order deck + account-tracking risk card
// ============================================================================
// A NinjaTrader 8 INDICATOR (drop it on any chart with ChartTrader open). It is
// the Sentinel Suite's manual-trading tool: its OWN Buy/Sell deck with full order
// types (Market / Limit / Stop / Stop-Limit), three ways to set a working price
// (tick-offset  editable box  click-on-chart), and a FLATTEN that is scoped to
// ONLY this chart's instrument on the selected account. A flight-instrument risk
// card (SharpDX) tracks the account live: day P&L, open position, uP&L, open risk.
//
// DESIGN: identical "flight-instrument" language as SentinelDashboard + GTrader21
// (void ground, ONE cyan accent = live/watching, green/red reserved for money +
// direction). Seamless with the Sentinel skin.
//
// ORDERS: account-level UNMANAGED orders via Account.CreateOrder/Submit  the deck
// fully OWNS its orders (no strategy-position desync; none of the managed-framework
// landmines). Account + instrument come from the native ChartTrader selectors.
//
// SENTINEL: reads SentinelCore for kill / governor / feed / rollover / news as an
// ADVISORY readout only  it NEVER blocks a click. A human must always be able to
// act, especially to exit. (User decision, 2026-07-03.)
//
//  v0.2.0  validate on a SIM account before going live. Live order submission.
//
// CHANGELOG
//   v0.2.5r (2026-07-21, RENAME to the federated naming law — ZERO logic change)  file/class/Name restored to the
//     "Sentinel <Thing>" tell: Deck_v0_2_2.cs → SentinelDeck_v0_2_5.cs, class Deck_v0_2_2 → SentinelDeck_v0_2_5,
//     Name "Sentinel Deck v0.2.2" → "Sentinel Deck", header chip → v0.2.5. The FILENAME HAD BEEN LYING — it said
//     v0_2_2 while the code was v0.2.5, which would have made every tester bug report ambiguous.
//     This RESTORES the original name: the tool shipped as SentinelDeck_v0_1_0/v0_2_0, was renamed to Deck_v0_2_1
//     under the 2026-07-05 "drop the prefix" convention, and that convention was REVERSED 2026-07-07 by the
//     FEDERATED NAMING LAW (Docs/SENTINEL_NAMING_FEDERATION.md).
//     ⚠ DONE NOW, BEFORE the public testers' preview, and deliberately not later: namespace + class name are an
//     indicator's SERIALIZATION IDENTITY, so renaming after distribution silently drops the Deck off every tester's
//     saved chart with no migration path. Cost of doing it now = re-add it once on your own charts.
//     Display Name follows naming-federation §9 (ratified 2026-07-10): "Sentinel Deck v0.2.5 (DEV)". The (DEV)
//     marker is CORRECT for the public testers' preview — this is the unfrozen head, auto-fire is still
//     un-live-validated, and testers may run it on LIVE accounts, so it must never read as a frozen build in
//     the picker. DROPPING " (DEV)" IS THE FREEZE STEP when it graduates to a supported rung.
//   v0.2.2 (2026-07-09, in-place  THEME ONLY, no order-logic change)  The header THEME button now cycles SEVEN
//     modes: auto → dark → light → silver → OBSIDIAN (true-black OLED) → BLUEPRINT (cyanotype) → AMBER (warm dark).
//     ⚠ "amber" and "auto" both start with 'A', so the old first-letter button face BROKE. Faces are now an
//     explicit ThemeGlyphs array; "auto" shows '~' (it isn't a theme — it means FOLLOW THE ACTIVE SKIN) and each
//     theme keeps its initial: ~/D/L/S/O/B/A. The word→Theme if-chain is gone — CycleTheme now calls the public
//     SentinelSkin.TryParseTheme, so a future theme needs no Deck logic change (only its glyph + mode word).
//     Pairs with templates\Skins\Sentinel {Obsidian,Blueprint,Amber}\ + SentinelSkin.Theme.*.
//   v0.2.5 (2026-07-14, order-line WPF OVERLAY)  New "Order lines ALWAYS ON TOP (overlay)" toggle (default OFF).
//     SharpDX OnRender loses z-order to other indicators' cards, so the order-line pills render UNDER them. This
//     draws the ENTRY/STOP/TARGET lines + pills on a hit-transparent WPF Canvas ABOVE all SharpDX rendering, so they
//     can never be hidden. Drag/hover is chart-mouse-event based (OnChartMouseMove) → 100% UNTOUCHED; the overlay is
//     purely the visual layer. Line DATA is shared via ComputeOrderLines() so the SharpDX + WPF paths never diverge.
//     Default ON (validated live 2026-07-14). Also made the ORDER-LINE DRAG + hover-attach + click-to-set-price
//     DPI-AWARE (DpiScale): the chart scale works in device px but WPF mouse events are DIPs, so at >100% display
//     scaling the drag hit-test / snap-to-indicator grabbed where the line ISN'T. Now every mouse-Y is converted to
//     device px before the chart-scale calls — works at any scaling (no-op at 100%). ENTRY shows the pill only (no
//     line — NT draws its own). + a 1-per-bar Magic-trail diagnostic (Deck:trail): logs cci/atr/cand vs the lock so
//     "not trailing" is diagnosable (it was the ratchet correctly holding a tighter lock, not a bug).
//   v0.2.4 (2026-07-13, RAW-TICK tape)  The Log Tick Path capture now records the TRUE last-trade price via an
//     OnMarketData override, not the synthetic brick Close[0] it read before. On an HA/TBars/Renko chart Close[0] is
//     the (averaged) brick close — GC px came out as 4004.13345, off the 0.1 grid — so MFE/MAE were quantized/biased.
//     OnMarketData is the only place the raw last-trade is visible; appends are now driven from there (every real
//     trade, at its real price) while OnBarUpdate keeps the begin/end/reversal lifecycle. Entry/exit px also fall back
//     to the last trade. Sidecar schema → "tick.2" ("src":"last"); the ingester keys off `kind` so it's compatible.
//   v0.2.3 (2026-07-13, TAPE + Flatten fix)  (1) A "RECORD ▸ Log Tick Path" toggle (default OFF, persisted) that
//     PASSIVELY captures the tick-by-tick price path of any manual trade on the chart instrument — begins on a
//     Flat→in-position transition (account-level, so it catches native Chart Trader fills too), appends every tick
//     while in, writes Sentinel\Excursions\ticks\<id>.jsonl on the return to Flat. NEVER touches the order path.
//     Feeds the excursion management sandbox (grade ATR/Magic/BE+ trails over your REAL entries). (2) FLATTEN FIX:
//     FlattenThisChart now uses NT's atomic Account.Flatten instead of a cancel-then-loop-Sleep(250) that RACED on a
//     lagging feed and OVER-flattened into an OPPOSITE position.
//   v0.2.2 (2026-07-05, SIGNAL persist + PRESETS)  Signal config now PERSISTS across F5/workspace save (serialized
//     props under "4. Signal (saved)"); "Signal watch" is deliberately NOT persisted (auto-fire never silently re-arms).
//     + in-Deck PRESET library (top of SIGNAL ARM): named presets capturing signal + entry (src A/B, rule, cadence,
//     invert, mode, threshold, qty, stop/target tk, auto-on-entry). Pick from the dropdown to LOAD (forces watch OFF),
//     type a name + Save to store, Delete to remove. Stored in SignalPresetsBlob (US/RS-delimited, serialized in-Deck).
//   v0.2.2 (2026-07-05, SIGNAL ARM read-race fix)  BAR CLOSE cadence now reads the JUST-CLOSED bar (barsAgo=1), not
//     the new in-progress bar. A one-bar PULSE plot (e.g. CompressionBase Signal = -1 on exactly the breakdown bar)
//     was being missed: the Deck is Calculate.OnEachTick, so on the new bar's first tick the foreign indicator's
//     CURRENT bar isn't computed yet (→ read 0). Now fires reliably on the bar AFTER the signal (confirmed/non-repaint).
//     Added a live status readout ("watching · A=<val> · <DIR>") — A=n/a means the source ref didn't resolve.
//   v0.2.2 (2026-07-05, SIGNAL ARM UX)  Moved SIGNAL ARM to the TOP of the panel (above ORDER TYPE) + made it a
//     COLLAPSED-by-default collapsible section (Section() gained a `collapsed` arg; "+" chevron). Source A/B are now
//     real DROPDOWNS (ComboBox, full plot names, no truncation) instead of cycle buttons — B has a "(none)" option;
//     lists (re)fill on Rescan / watch-on / build. Selection resolves the ref on the UI thread (attach pattern).
//   v0.2.2 (2026-07-05, +SIGNAL ARM)  New "SIGNAL ARM" section: arm/auto-fire Long/Short off ANY loaded indicator's
//     PLOT — no hardcoded signals. Sources are discovered from ChartControl.Indicators at runtime (same plumbing as
//     hover-attach). Pick Source A (+ optional B for a cross), a Rule (Sign(>0) / Rising / A x B / Threshold), Invert,
//     a Mode and a Cadence. MODE: ARM (default) highlights the primed BUY/SELL button + status "ARMED LONG — click
//     BUY" and a human confirms; AUTO-FIRE (opt-in, amber) submits automatically — FAIL-CLOSED through
//     SentinelCore.GateEntry (a Hard reason BLOCKS, unlike a manual click), one-shot per bar, flat-only, opposite
//     signal = REVERSE (never stacks), and it suppresses the state-at-enable so it only acts on a real change.
//     CADENCE: Bar-close (default) / every Tick. Fires the existing SubmitDeckOrder path (automated flag → gate
//     fail-closed + forced MARKET), so it inherits qty / risk-sizing / auto-bracket / fill-capture. Source keys are
//     stable (Type#ordinal|plotIdx) so they survive indicator reloads; "Rescan sources" re-reads the chart.
//     ⚠ AUTO-FIRE modifies live orders → SIM-validate. Companion: CompressionBase_v1_3_0 now exposes a hidden
//     "Signal" plot (+1 BreakUp / -1 BreakDown) so its REAL breakout is a first-class source (Sign rule).
//   v0.2.2 (2026-07-05, in-place UI fix)  TRAILING mode pills no longer clip their text: zeroed the button Padding
//     + centered content + shortened the 4-col labels to uniform short forms (Trail / BE+ / BarHL / NBar / ATR /
//     Magic / HalfBE). Labels are display-only (mode is bound via the enum arg, not the string) - purely cosmetic.
//   v0.2.2 (2026-07-05)  DRAG-TO-ADJUST + HOVER-ATTACH on the on-chart order lines (⚠ MODIFIES LIVE ORDERS from
//     the chart - SIM-validate before live):
//      * DRAG: hover a STOP/TARGET line (resize cursor); left-drag to re-price the working order live (preview chip
//        updates $/R/ticks); release re-prices via the proven o.StopPriceChanged/LimitPriceChanged + Account.Change
//        path (same as Breakeven). Esc cancels. ENTRY line is read-only. Master toggle "Enable order-line drag".
//      * HOVER-ATTACH: while dragging, if the line nears an OVERLAY indicator plot (MA/VWAP/CompressionBase base
//        levels, ...), it snaps + BINDS the order to that plot; the order then re-prices each tick to follow the
//        plot (throttled to >=1 tick moves). Attached line renders DASHED with a "-> <Indicator>" tag. Drag off to
//        detach. "Attached stop: only-improve" prop (default off = free-follow). Drag is independent of attach
//        (attach fails safe). v0.2.1 frozen (aligned + on-chart visuals checkpoint).
//   v0.2.1 (2026-07-05)  SUITE-CONVENTION ALIGNMENT (no order-LOGIC change; validate on SIM):
//     * REHOMED to namespace NinjaTrader.NinjaScript.Indicators.Sentinel + RENAMED class/file/Name = Deck_v0_2_1
//       (strict naming; drops the redundant "Sentinel" prefix - the picker's "Sentinel" folder supplies it).
//     * LABEL REMOVER (Sentinel standard): hides NT's chart name label by default (Name blanked at DataLoaded)
//       with a "Show indicator label" toggle. NOTE: order tags were DECOUPLED from Name into a stable _tag field
//       (captured before the blank) so blanking Name can't corrupt order identity / fill-capture matching.
//     * Risk card now docks via SentinelSkin.CardLayout + the shared SentinelCardCorner enum (was a local
//       RiskCardCornerPos + hand-rolled corner switch) - identical positions, now anti-overlap + stackable.
//     + NEW FEATURE - ON-CHART ORDER VISUALS ("Show order lines", default on): draws the live position's
//       ENTRY (cyan) / STOP (red) / TARGET (green) as horizontal chart lines with a left-anchored chip showing
//       R-multiple, $ and tick-distance (STOP = -1R, TARGET = R/$ from the working bracket TP). READ-ONLY (no
//       order path touched); target = nearest exit-side working Limit; all math on the data thread, drawn in
//       OnRender under the risk card. Labels default to the RIGHT ("Order line label side"); lines default full
//       width, editable via "Order line width %" (5-100, measured from the label side). Chip width is MEASURED
//       (DirectWrite) so it never truncates. Chip sits ABOVE its line (clears NT's own on-line order label - the
//       Deck's STOP/TARGET = NT's Sell STP / Sell LMT at the same price). Next: optional drag-to-adjust (Phase 2).
//     + RISK CARD: BAR TIMER / tick counter row (bar-completion progress bar + bar-type-aware label: tick/vol/range
//       count e.g. "87 / 150t", or time remaining for minute/second bars).
//     + UI POLISH: collapsible management sections EXPANDED by default (- cyan chevron); dock button narrower
//       ("[]" @ 22px, was "[ ]" @ 30); order-type pills wider gap (margin 3, 9.5px) so they don't run together.
//     Old SentinelDeck_v0_1_0 / v0_2_0 archived out of the tree. See design-system 4b/7 + sentinel-namespace-and-naming.
//   v0.2.0+ (2026-07-04, in-place  OBSERVABILITY ONLY, no version fork)  Sentinel Ledger fill capture.
//     Pins an ExecutionUpdate subscription to the SELECTED account (re-points on selector change, dropped
//     on Terminated) and logs DECK-originated fills (order name "<Name>_...") to SentinelCore.Ledger.Fill
//     (intended = order stop/limit price vs actual fill  adverse slip ticks; 0/market  slip omitted).
//     Feeds the dashboard Slippage view so MANUAL deck trades get execution-quality analysis too. Bounded
//     ExecutionId dedupe. Pure observation in try/catch  never touches the order path, so it stays v0.2.0.
//   v0.1.0 (2026-07-03)  FROZEN checkpoint (simple entry deck). Order-entry deck
//     (Mkt/Lmt/Stp/StpLmt), qty stepper + presets, price entry (offset/editable/
//     click-chart), BUY/SELL/REVERSE/CLOSE, FLATTEN-THIS-CHART, account risk card,
//     Sentinel advisory. See SentinelDeck_v0_1_0.cs.
//   v0.2.0 (2026-07-03)  FULL trade-management suite (ports GTrader21's engine):
//      BRACKET / STOP  attach OCO (stop+target) or a protective stop to the open
//       position; auto-on-entry cycle Off / Stop-only / Bracket.
//      BREAKEVEN  move stops to entry  offset (manual + auto when profit  trigger).
//      TRAILING  all 7 GTrader modes: TrailTicks  Breakeven+  Bar H/L  N-Bar H/L 
//       ATR  TrendMagic (ATR gated by CCI regime)  Half+BE (scale half  BE). Manual
//       arm + auto-trail-on-entry. Tick-level execution via OnMarketData; stop only ever
//       improves (never moves against the position).
//      SCALE  close half. Orders remain account-level UNMANAGED (the deck owns them).
//     Engine adapted verbatim-in-spirit from GTrader21v_0_1_6Panel (no strategy-owned-
//     order guards needed  the deck has no strategy).  validate on SIM before live.
//     + POP-OUT / DOCK toggle in the header ([ ] / ><) - floats the deck into its own
//       resizable window (geometry remembered) and docks it back into ChartTrader.
//     + $ RISK sizing (Order Gate, hardening Phase 1): toggle "$ RISK", type $-risk, and the
//       qty is computed from the Bracket "Stop tk" via SentinelCore.SizeForRisk. Every entry now
//       routes through SentinelCore.GateEntry (kill/loss-stop/rate/qty-cap = Hard, surfaced loudly;
//       feed/session/rollover/news = Advisory) -- the Deck fails OPEN (never traps a human) and
//       records each submit for the fat-finger rate guard. See Docs/SENTINEL_HARDENING_FRAMEWORK.md.
// ============================================================================

#region Using declarations
using System;
using System.Globalization;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.AddOns.Sentinel;   // SentinelCore advisory
#endregion

namespace NinjaTrader.NinjaScript.Indicators.Sentinel
{
    public class SentinelDeck_v0_2_5 : Indicator
    {
        public enum DeckOrderType     { Market, Limit, Stop, StopLimit }
        public enum DeckTrailMode     { None, TrailTicks, BreakevenPlus, BarLowHigh, NBarLowHigh, TrailATR, TrendMagic, HalfPlusBE }
        public enum AutoBracketMode   { Off, StopOnly, Bracket }
        public enum LineLabelSide     { Right, Left }   // which end the ENTRY/STOP/TARGET chips sit on
        // v0.2.2: SIGNAL ARM — turn any loaded indicator's plot into a directional signal
        public enum SignalRule    { Sign, RisingFalling, CrossPlotB, Threshold }
        public enum SignalCadence { BarClose, Tick }

        // Order-tag identity, DECOUPLED from the display Name so the label remover can blank Name safely
        // (v0.2.1) AND so display-Name polish never churns live order tags (federated naming, 2026-07-07).
        // A stable, space-free literal; all order tags use this, never Name.
        private string _tag = "SentinelDeck";

        //  flight-instrument palette — now THEME-AWARE: reads SentinelSkin's active palette (Dark/Light/Silver)
        //  so the Deck follows the suite theme when its panel is (re)built (was a hardcoded dark copy).
        private static Color C_BG     => SentinelSkin.KVoid;    // void
        private static Color C_PANEL  => SentinelSkin.KPanel;   // panel
        private static Color C_CARD2  => SentinelSkin.KCard;    // deeper card
        private static Color C_BORDER => SentinelSkin.KLine;    // line
        private static Color C_DIM    => SentinelSkin.KDim;     // control bg
        private static Color C_LABEL  => SentinelSkin.KInk2;    // ink2
        private static Color C_MUTED  => SentinelSkin.KMute;    // mute
        private static Color C_TEXT   => SentinelSkin.KInk;     // ink
        private static Color C_ACCENT => SentinelSkin.KAccent;  // cyan = live
        private static Color C_GREEN  => SentinelSkin.KUp;      // up / long
        private static Color C_RED    => SentinelSkin.KDown;    // down / short
        private static Color C_AMBER  => SentinelSkin.KWarn;    // warn

        //  ChartTrader hosting plumbing (proven pattern) 
        private Chart           _ctChart;
        private Grid            _ctTraderGrid;
        private ScrollViewer    _ctScrollViewer;
        private RowDefinition   _ctScrollRow;
        private StackPanel      hudStack;
        private bool            uiPanelActive;
        private AccountSelector    xAcSelector;
        private InstrumentSelector xInSelector;

        // pop-out / dock (v0.2.0)
        private bool            panelFloating;
        private Window          floatWindow;
        private System.Windows.Controls.DockPanel floatHost;
        private Button          btnDock;
        private double          floatLeft = double.NaN, floatTop = double.NaN, floatWidth, floatHeight;

        //  deck state 
        private DeckOrderType   deckType = DeckOrderType.Market;
        private int             deckQty  = 1;
        private bool            clickArm;                 // "click chart to set price" armed
        private double          lastClose;

        //  deck controls 
        private Button          btnTypeMkt, btnTypeLmt, btnTypeStp, btnTypeSL;
        private TextBox         tbQty, tbPrice, tbLimit;
        private Border          rowPrice, rowLimit;
        private TextBlock       lblPrice, lblLimit, deckStatus, advisoryText;
        private Button          btnClickArm;

        // risk-based sizing (Gate substrate 1): type a $-risk, size from the stop
        private TextBox         tbRisk;
        private Button          btnRiskMode;
        private TextBlock       lblRiskCalc;
        private bool            riskMode;
        private double          pRisk = 100;

        //  trade-management state (v0.2.0)  executed on the data thread 
        private volatile DeckTrailMode activeTrail = DeckTrailMode.None;
        private double          trailStopLevel = double.MinValue;
        private int             _trailLogBar = -1;   // v0.2.5: 1-per-bar Magic-trail diagnostic (Deck:trail)
        private bool            beTriggered, halfTriggered, trailPending;
        private AutoBracketMode autoBracket = AutoBracketMode.Off;
        private bool            autoTrail, autoBE;
        private int             lastPosQty;          // fresh-entry detector for auto-on-entry
        private Account         cachedAccount;       // cached refs for the tick path (no Dispatcher)
        private Instrument      cachedInstrument;
        private int             _tickCtr;
        private Account         _fillAccount;        // account whose ExecutionUpdate we log to the Sentinel Ledger (Slippage view)
        private readonly System.Collections.Generic.HashSet<string> _seenDeckExecIds = new System.Collections.Generic.HashSet<string>();
        // params (mirrored from the UI textboxes so the data thread never touches WPF)
        private int             pStop = 30, pTarget = 60, pBETrig = 20, pBEOff = 2,
                                pTrailTk = 20, pTrailBars = 3, pAtrPer = 14, pHalfTrig = 20, pHalfOff = 2;
        private double          pAtrMult = 2.0;

        //  trade-management controls 
        private TextBox         tbStop, tbTarget, tbBETrig, tbBEOff, tbTrailTk, tbTrailBars, tbAtrPer, tbAtrMult, tbHalfTrig, tbHalfOff;
        private Button          btnAutoBracket, btnAutoTrail, btnAutoBE;
        private Button          btnTrailTicks, btnBEPlus, btnBarLH, btnNBarLH, btnATRb, btnTMagic, btnHalfBE;

        //  card data (filled on the data thread, read by OnRender) 
        private volatile string cardAcct = "";
        private volatile int    cardQty;
        private MarketPosition  cardPos = MarketPosition.Flat;
        private double          cardAvg, cardDayPnl, cardUnreal, cardStopPx, cardOpenRisk;

        // v0.2.3: TAPE — passive tick-path capture of manual trades (position-transition triggered; never touches orders)
        private MarketPosition  _tapePrevPos = MarketPosition.Flat;
        private bool            _tapeActive, _tapePartial;
        private int             _tapeTicks;
        private DateTime        _tapeEntryTime;
        private double          _tapeEntryPx;
        private int             _tapeDir;
        private string          _tapeId;
        private System.Text.StringBuilder _tapeBuf;
        private double          _tapeMaxFav, _tapeMaxAdv;
        private Button          btnLogTape;
        // v0.2.4: RAW-TICK — last actual trade (OnMarketData), so the tape records true last-trade px, NOT the
        // synthetic HA/TBars brick Close[0]. Both OnMarketData + OnBarUpdate run on the same data thread → no lock.
        private double          _lastTradePx;
        private DateTime        _lastTradeTime;
        // v0.2.1: on-chart order visuals (entry/stop/target lines) — filled on the data thread, read by OnRender
        private double          cardTargetPx, cardStopTicks, cardTargetTicks, cardTargetProfit, cardTargetR;
        // v0.2.1: bar timer / tick counter (data thread → card)
        private float           cardBarPct;
        private volatile string cardBarText = "";

        // v0.2.2: drag-to-adjust + hover-attach state (UI thread)
        private int    _hoverLine;                 // 0 none, 1 stop, 2 target — for cursor/highlight
        private int    _dragLine;                  // 0 none, 1 stop, 2 target
        private bool   _dragging;
        private double _dragPrice;                 // live dragged price (preview)
        // attach candidate under the cursor mid-drag
        private NinjaTrader.Gui.NinjaScript.IndicatorRenderBase _attachCand;
        private int    _attachCandPlot = -1;
        private string _attachCandName;
        private DateTime _attachLogTime;   // v0.2.5: throttle the Deck:attach diagnostic (1/sec while dragging)
        // committed attachments: the order rides this indicator plot (re-priced each tick)
        private NinjaTrader.Gui.NinjaScript.IndicatorRenderBase _stopAttInd, _tgtAttInd;
        private int    _stopAttPlot = -1, _tgtAttPlot = -1;
        private string _stopAttName, _tgtAttName;

        // v0.2.2: SIGNAL ARM state — read an indicator plot as a directional signal, then ARM or auto-fire
        private bool          sigEnabled;                 // master "watch" on/off
        private bool          sigAutoFire;                // false = ARM (human confirms); true = auto-submit (fail-closed)
        private SignalRule    sigRule     = SignalRule.Sign;
        private SignalCadence sigCadence  = SignalCadence.BarClose;
        private bool          sigInvert;
        private double        sigThreshold;               // for the Threshold rule
        private string        sigSrcA, sigSrcB;           // stable source keys "Type#ord|plotIdx" (survive indicator reload)
        private NinjaTrader.Gui.NinjaScript.IndicatorRenderBase _sigIndA, _sigIndB;   // resolved on the UI thread; .Values read on data thread (like attach)
        private int           _sigPlotA = -1, _sigPlotB = -1;
        private int           _armedDir;                  // 0 none, +1 long armed, -1 short armed (ARM mode)
        private int           _sigLastDir;                // last computed signal dir (transition detection)
        private int           _sigFiredBar = -1;          // last bar auto-fired (one-shot per bar)
        private bool          _sigPrimed;                 // suppress the very first eval after enable (don't fire the state-at-enable)
        private double        _sigDbgA = double.NaN;      // last raw source-A value read (live status readout / diagnostics)
        private int           _sigLastBar  = -1;          // bar-change tracker for BarClose cadence
        private readonly List<SigSrc> _sigSources = new List<SigSrc>();   // discovered indicator plots (rescanned on demand)
        private Button        buyBtnRef, sellBtnRef;      // refs so ARM can highlight the primed side
        private Button        btnSigEnable, btnSigRule, btnSigMode, btnSigCadence, btnSigInvert, btnSigRescan;
        private ComboBox      cbSigSrcA, cbSigSrcB;        // v0.2.2: dropdown source pickers (replaced the cycle buttons)
        private bool          _sigComboUpdating;           // guard SelectionChanged re-entrancy during programmatic fills
        private TextBlock      sigStatus;

        private sealed class SigSrc { public string Key; public string Display; }

        // v0.2.2: in-Deck signal PRESETS (signal + entry settings), serialized with the workspace via SignalPresetsBlob
        private readonly List<SigPreset> _presets = new List<SigPreset>();
        private ComboBox      cbPreset;
        private TextBox       tbPresetName;
        private Button        btnPresetSave, btnPresetDelete;
        private bool          _presetUpdating;

        private sealed class SigPreset
        {
            public string Name = "";
            public string SrcA, SrcB;
            public int    Rule, Cadence, AutoBracket;
            public bool   Invert, AutoFire;
            public double Threshold;
            public int    Qty = 1, StopTk = 30, TargetTk = 60;
        }

        private volatile bool   cardGovOn;
        private double          cardGovDay, cardGovCap;
        private volatile string cardAdvisory = "clear";
        private volatile bool   cardAdvisoryOk = true;

        //  SharpDX 
        private SharpDX.DirectWrite.Factory riskTextFactory;

        // v0.2.5: ORDER-LINE WPF OVERLAY (opt-in). SharpDX OnRender loses z-order to other indicators' cards, so the
        // order-line pills render UNDER them. A hit-transparent WPF Canvas sits ABOVE all SharpDX rendering → always on
        // top, regardless of indicator z-order. The drag/hover logic is chart-mouse-event based (see OnChartMouseMove),
        // so it is 100% UNTOUCHED — this overlay is purely the VISUAL layer. Lazily built on first render.
        private System.Windows.Controls.Canvas _olCanvas;
        private System.Windows.Shapes.Line[]   _olLines;   // [0]=entry [1]=stop [2]=target
        private System.Windows.Controls.Border[] _olPills;
        private System.Windows.Controls.TextBlock[] _olTxts;
        private double _olDpi = 1.0;
        private bool   _dpiDone;   // v0.2.5: display-scale factor (render px ↔ WPF DIP) — shared by the overlay AND the drag hit-test
        private ChartScale      _lastScale;

        #region Properties
        [NinjaScriptProperty]
        [Display(Name = "Default quantity", Order = 1, GroupName = "1. Deck")]
        public int DefaultQty { get; set; } = 1;

        [NinjaScriptProperty]
        [Display(Name = "Price offset (ticks)", Order = 2, GroupName = "1. Deck")]
        public int TickOffset { get; set; } = 4;

        [Display(Name = "Show risk card", Order = 1, GroupName = "2. Risk Card")]
        public bool ShowRiskCard { get; set; } = true;

        [Display(Name = "Risk card corner", Order = 2, GroupName = "2. Risk Card")]
        public SentinelCardCorner RiskCardCorner { get; set; } = SentinelCardCorner.BottomRight;

        [Display(Name = "Show order lines", Description = "Draw live ENTRY / STOP / TARGET lines on the chart with R-multiple, $ and tick-distance labels.", Order = 3, GroupName = "2. Risk Card")]
        public bool ShowOrderLines { get; set; } = true;

        [Display(Name = "Order lines ALWAYS ON TOP (overlay)", Description = "Draw the ENTRY/STOP/TARGET lines + pills on a WPF overlay ABOVE the chart so they can't be hidden under other indicators' cards (SharpDX render order can't beat cross-indicator z-order). Drag/hover is unaffected. Default ON (validated live 2026-07-14).", Order = 3, GroupName = "2. Risk Card")]
        public bool OrderLineOverlay { get; set; } = true;

        [Display(Name = "Order line label side", Description = "Which end of the ENTRY/STOP/TARGET lines the labels sit on.", Order = 4, GroupName = "2. Risk Card")]
        public LineLabelSide OrderLineLabelSide { get; set; } = LineLabelSide.Right;

        [Range(5, 100)]
        [Display(Name = "Order line width %", Description = "How far the lines span across the price panel, measured from the label side (100 = full width).", Order = 5, GroupName = "2. Risk Card")]
        public int OrderLineWidthPct { get; set; } = 100;

        [Display(Name = "Enable order-line drag", Description = "Hover a STOP/TARGET line and drag to re-price the working order; drop on an overlay indicator plot to ATTACH (order follows it).", Order = 6, GroupName = "2. Risk Card")]
        public bool EnableOrderDrag { get; set; } = true;

        [Display(Name = "Attached stop: only-improve", Description = "When a STOP is attached to an indicator: ON = only tighten (never loosen); OFF = free-follow the plot both ways.", Order = 7, GroupName = "2. Risk Card")]
        public bool AttachedStopOnlyImprove { get; set; } = false;

        [Display(Name = "Drag/attach debug log", Description = "Log Deck:drag + Deck:attach diagnostics to Sentinel\\sentinel.log (for debugging attach-to-indicator). Default OFF.", Order = 8, GroupName = "2. Risk Card")]
        public bool DeckDragDebug { get; set; } = false;

        [Display(Name = "Consult Sentinel (advisory)", Order = 1, GroupName = "3. Sentinel")]
        public bool ConsultSentinel { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Show indicator label", Description = "Show NinjaTrader's chart name label. Sentinel default = OFF (clean chart); turn on to restore it.", GroupName = "3. Sentinel", Order = 2)]
        public bool ShowIndicatorLabel { get; set; }

        // v0.2.2: SIGNAL ARM persisted config — these WRAP the runtime fields so they serialize with the workspace/
        // template (survive F5 + save). "Signal watch" (sigEnabled) is intentionally NOT persisted → auto-fire never
        // silently re-arms on reload; you always flip watch ON yourself. Source keys are Browsable(false) (ugly, hidden).
        [Browsable(false)] public string       SignalSourceA      { get { return sigSrcA; }     set { sigSrcA = value; } }
        [Browsable(false)] public string       SignalSourceB      { get { return sigSrcB; }     set { sigSrcB = value; } }
        [Display(Name = "Signal rule",      Order = 1, GroupName = "4. Signal (saved)")]
        public SignalRule    SignalRuleSaved    { get { return sigRule; }     set { sigRule = value; } }
        [Display(Name = "Signal cadence",   Order = 2, GroupName = "4. Signal (saved)")]
        public SignalCadence SignalCadenceSaved { get { return sigCadence; }  set { sigCadence = value; } }
        [Display(Name = "Signal invert",    Order = 3, GroupName = "4. Signal (saved)")]
        public bool          SignalInvertSaved  { get { return sigInvert; }   set { sigInvert = value; } }
        [Display(Name = "Signal auto-fire", Order = 4, GroupName = "4. Signal (saved)")]
        public bool          SignalAutoFireSaved{ get { return sigAutoFire; } set { sigAutoFire = value; } }
        [Display(Name = "Signal threshold", Order = 5, GroupName = "4. Signal (saved)")]
        public double        SignalThresholdSaved{ get { return sigThreshold; } set { sigThreshold = value; } }
        [Browsable(false)] public string SignalPresetsBlob { get { return EncodePresets(); } set { DecodePresets(value); } }   // in-Deck preset library (serialized)
        [Browsable(false)] public bool   DeckFloatPinned    { get; set; }   // always-on-top for the floated deck (header ^ pin); persists to the workspace
        [Browsable(false)] public bool   LogTickPath        { get; set; }   // v0.2.3: TAPE — persist the manual tick-path capture toggle (default OFF)
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description              = "Sentinel Deck  manual order deck + account-tracking risk card. Advisory-only Sentinel gates; chart-scoped flatten.";
                Name                     = DeckVersion;
                Calculate                = Calculate.OnEachTick;
                IsOverlay                = true;
                DisplayInDataBox         = false;
                DrawOnPricePanel         = true;
                PaintPriceMarkers        = false;
                IsSuspendedWhileInactive = false;
                ShowIndicatorLabel       = false;   // Sentinel standard: clean chart (NT name label removed)
                deckQty                  = Math.Max(1, DefaultQty);
            }
            else if (State == State.DataLoaded)
            {
                // _tag is a stable literal (see field decl); no longer captured from Name (federated naming 2026-07-07)
                if (!ShowIndicatorLabel) Name = string.Empty;    // Sentinel label remover (NT draws the label from Name; orders use _tag)
                deckQty = Math.Max(1, DefaultQty);
                ChartControl?.Dispatcher.BeginInvoke(new Action(CreateWPFControls));
            }
            else if (State == State.Terminated)
            {
                ChartControl?.Dispatcher.BeginInvoke(new Action(DisposeWPFControls));
                try { riskTextFactory?.Dispose(); riskTextFactory = null; } catch { }
                EnsureFillSubscription(null);   // drop the Ledger fill-capture subscription
                ChartControl?.Dispatcher.BeginInvoke(new Action(StopWarnHeartbeat));
                try { SentinelSkin.CardLayout.Release(this); } catch { }
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 0) return;
            lastClose = Close[0];
            if (State != State.Realtime && State != State.Historical) return;

            // Cache account/instrument periodically  ResolveAccount does a Dispatcher.Invoke,
            // too costly to run every tick. Refresh every 25 ticks (picks up a selector change
            // within seconds) so the tick-path engine can use the cached refs with no marshalling.
            if (cachedAccount == null || cachedInstrument == null || (++_tickCtr % 25) == 0)
            {
                cachedAccount    = ResolveAccount();
                cachedInstrument = ResolveInstrument();
            }

            // keep the Ledger fill-capture pinned to the selected account (realtime only)
            if (State == State.Realtime) EnsureFillSubscription(cachedAccount);

            UpdateCardData();
            try { UpdateWarnBand(cachedAccount); } catch { }   // v0.2.5: preview safety band (the 1s UI timer is the primary driver)
            try { TapeOnTick(); } catch { }   // v0.2.3: passive tick-path capture (cardPos is fresh after UpdateCardData)
            UpdateBarTimer();
            if (!_dragging) ApplyAttachments(false);   // v0.2.2: attached orders follow their indicator plot
            HandleAutoOnEntry();
            EvaluateSignal();   // v0.2.2: SIGNAL ARM — read indicator plots → arm / auto-fire
            if (activeTrail != DeckTrailMode.None) ExecuteTrail();
        }

        // v0.2.4: RAW-TICK capture. OnMarketData is the ONLY place the true last-trade price is visible — Close[0] on
        // an HA/TBars/Renko chart is a synthetic brick close (e.g. GC ticks come out as 4004.13345, off the 0.1 grid).
        // Fires realtime only; same data thread as OnBarUpdate. We drive the tape append from here so every actual
        // trade is recorded at its real price (the excursion/MFE-MAE fuel is only honest at raw-tick resolution).
        protected override void OnMarketData(MarketDataEventArgs e)
        {
            if (e.MarketDataType != MarketDataType.Last) return;
            _lastTradePx   = e.Price;
            _lastTradeTime = e.Time;
            if (_tapeActive) { try { TapeAppend(e.Price); } catch { } }
        }

        // v0.2.1: bar-completion progress + a bar-type-aware label (tick/vol/range count, or time remaining)
        private void UpdateBarTimer()
        {
            try
            {
                double p = Bars != null ? Bars.PercentComplete : 0;
                if (double.IsNaN(p) || double.IsInfinity(p)) p = 0;
                cardBarPct = (float)Math.Max(0, Math.Min(1, p));
                var bp = BarsPeriod;
                if (bp == null) { cardBarText = ""; return; }
                int v = bp.Value, done = (int)Math.Round(p * v);
                switch (bp.BarsPeriodType)
                {
                    case BarsPeriodType.Tick:   cardBarText = done + " / " + v + "t"; break;
                    case BarsPeriodType.Volume: cardBarText = done + " / " + v + "v"; break;
                    case BarsPeriodType.Range:  cardBarText = done + " / " + v + "r"; break;
                    case BarsPeriodType.Second: cardBarText = (int)Math.Ceiling((1 - p) * v) + "s"; break;
                    case BarsPeriodType.Minute: { int r = (int)Math.Ceiling((1 - p) * v * 60); cardBarText = (r / 60) + ":" + (r % 60).ToString("00"); break; }
                    default:                    cardBarText = (int)Math.Round(p * 100) + "%"; break;
                }
            }
            catch { cardBarPct = 0; cardBarText = ""; }
        }

        // 
        // Card data  read account/position/P&L + Sentinel advisory (data thread)
        // 
        private void UpdateCardData()
        {
            try
            {
                var acct  = cachedAccount;
                var instr = cachedInstrument;
                if (acct == null || instr == null) { cardAcct = ""; cardPos = MarketPosition.Flat; return; }
                cardAcct = acct.Name;

                double realized = SafePnl(acct.Get(AccountItem.RealizedProfitLoss, Currency.UsDollar));
                cardDayPnl = realized;

                var pos = acct.Positions.FirstOrDefault(p => p.Instrument != null
                    && p.Instrument.FullName == instr.FullName && p.Quantity != 0);
                if (pos == null)
                {
                    cardPos = MarketPosition.Flat; cardQty = 0; cardAvg = 0;
                    cardUnreal = 0; cardStopPx = 0; cardOpenRisk = 0;
                    cardTargetPx = 0; cardStopTicks = 0; cardTargetTicks = 0; cardTargetProfit = 0; cardTargetR = 0;
                }
                else
                {
                    cardPos = pos.MarketPosition;
                    cardQty = Math.Abs(pos.Quantity);
                    cardAvg = pos.AveragePrice;
                    try { cardUnreal = pos.GetUnrealizedProfitLoss(PerformanceUnit.Currency, lastClose); }
                    catch { cardUnreal = SafePnl(acct.Get(AccountItem.UnrealizedProfitLoss, Currency.UsDollar)); }

                    double pv = instr.MasterInstrument.PointValue;
                    double ts = instr.MasterInstrument.TickSize;

                    // open risk from the nearest working stop for this instrument
                    var stop = acct.Orders.FirstOrDefault(o => o.Instrument != null
                        && o.Instrument.FullName == instr.FullName
                        && (o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit)
                        && (o.OrderState == OrderState.Working || o.OrderState == OrderState.Accepted));
                    if (stop != null)
                    {
                        cardStopPx    = stop.StopPrice;
                        cardOpenRisk  = Math.Abs(cardAvg - cardStopPx) * pv * cardQty;
                        cardStopTicks = ts > 0 ? Math.Abs(cardAvg - cardStopPx) / ts : 0;
                    }
                    else { cardStopPx = 0; cardOpenRisk = 0; cardStopTicks = 0; }

                    // v0.2.1: working target = nearest exit-side Limit for this instrument (the bracket TP)
                    var tgt = acct.Orders.FirstOrDefault(o => o.Instrument != null
                        && o.Instrument.FullName == instr.FullName
                        && o.OrderType == OrderType.Limit
                        && (o.OrderState == OrderState.Working || o.OrderState == OrderState.Accepted)
                        && ((cardPos == MarketPosition.Long  && o.OrderAction == OrderAction.Sell)
                         || (cardPos == MarketPosition.Short && (o.OrderAction == OrderAction.BuyToCover || o.OrderAction == OrderAction.Buy))));
                    if (tgt != null)
                    {
                        cardTargetPx     = tgt.LimitPrice;
                        cardTargetTicks  = ts > 0 ? Math.Abs(cardTargetPx - cardAvg) / ts : 0;
                        cardTargetProfit = Math.Abs(cardTargetPx - cardAvg) * pv * cardQty;
                        cardTargetR      = cardStopTicks > 0 ? cardTargetTicks / cardStopTicks : 0;
                    }
                    else { cardTargetPx = 0; cardTargetTicks = 0; cardTargetProfit = 0; cardTargetR = 0; }
                }

                // governor (day cap)  advisory
                cardGovOn = false; cardGovDay = 0; cardGovCap = 0;
                cardAdvisory = "clear"; cardAdvisoryOk = true;
                if (ConsultSentinel)
                {
                    try
                    {
                        var g = SentinelCore.GetGovernorState(acct.Name);
                        if (g != null && g.Cap > 0) { cardGovOn = true; cardGovDay = g.DailyPnl; cardGovCap = g.Cap; }
                    }
                    catch { }
                    try
                    {
                        string why;
                        bool ok = SentinelCore.CanEnter(instr.FullName, acct, out why);
                        cardAdvisoryOk = ok;
                        cardAdvisory   = ok ? "clear" : (string.IsNullOrEmpty(why) ? "blocked" : why);
                    }
                    catch { cardAdvisory = "clear"; cardAdvisoryOk = true; }
                }
            }
            catch { /* informational only */ }
        }

        // NT8 sim/live accounts can return a large-magnitude sentinel or NaN for P&L when flat.
        private static double SafePnl(double v)
            => (double.IsNaN(v) || double.IsInfinity(v) || Math.Abs(v) > 1e7) ? 0.0 : v;

        // 
        // WPF deck  build / dispose (hosted in the ChartTrader sidebar)
        // 
        private void CreateWPFControls()
        {
            try
            {
                if (uiPanelActive) return;
                if (ChartControl?.Parent == null) return;
                _ctChart = Window.GetWindow(ChartControl.Parent) as Chart;
                if (_ctChart == null) return;
                var ct = _ctChart.FindFirst("ChartWindowChartTraderControl") as ChartTrader;
                if (ct == null) { Print("[Sentinel Deck] ChartTrader not open  open ChartTrader to use the deck."); return; }
                if (ct.Content == null) return;
                _ctTraderGrid = ct.Content as Grid;
                if (_ctTraderGrid == null) return;

                hudStack = new StackPanel { Orientation = Orientation.Vertical,
                    Background = new SolidColorBrush(C_BG), HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 210 };

                BuildDeck();

                _ctScrollViewer = new ScrollViewer
                {
                    Content = hudStack,
                    VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment   = VerticalAlignment.Stretch,
                    CanContentScroll    = false,
                    Background           = new SolidColorBrush(C_BG),
                };
                InsertPanel();
                uiPanelActive = true;

                // click-on-chart price setting
                ChartControl.PreviewMouseLeftButtonDown += OnChartClickForPrice;
                // v0.2.2: order-line drag-to-adjust + hover-attach
                ChartControl.PreviewMouseLeftButtonDown += OnChartMouseDown;
                ChartControl.PreviewMouseMove          += OnChartMouseMove;
                ChartControl.PreviewMouseLeftButtonUp  += OnChartMouseUp;
                ChartControl.PreviewKeyDown            += OnChartKeyDown;   // Esc cancels a drag

                RefreshTypeButtons();
                RefreshAdvisory();
                UpdateDockButton();
            }
            catch (Exception ex) { Print("[Sentinel Deck] CreateWPFControls: " + ex.Message); }
        }

        private void DisposeWPFControls()
        {
            try
            {
                try { if (ChartControl != null) ChartControl.PreviewMouseLeftButtonDown -= OnChartClickForPrice; } catch { }
                try { if (ChartControl != null) { ChartControl.PreviewMouseLeftButtonDown -= OnChartMouseDown; ChartControl.PreviewMouseMove -= OnChartMouseMove; ChartControl.PreviewMouseLeftButtonUp -= OnChartMouseUp; ChartControl.PreviewKeyDown -= OnChartKeyDown; } } catch { }
                // if floating, tear the window down first (the scroll viewer lives in it, not the grid)
                if (floatWindow != null)
                {
                    panelFloating = false;
                    try { floatWindow.Content = null; floatWindow.Close(); } catch { }
                    floatWindow = null; floatHost = null;
                }
                if (_ctScrollViewer != null) _ctScrollViewer.Content = null;
                RemovePanel();
                RemoveOverlay();   // v0.2.5: tear down the WPF order-line overlay
                _ctScrollViewer = null; _ctTraderGrid = null; _ctChart = null; uiPanelActive = false;
            }
            catch { }
        }

        // -- pop-out / dock (ported from GTrader21) --
        private void TogglePanelFloat()
        {
            if (panelFloating) DockDeck();
            else               FloatDeck();
        }

        private void FloatDeck()
        {
            if (panelFloating || _ctScrollViewer == null) return;
            try
            {
                RemovePanel();                                  // detach from the ChartTrader grid (keeps the viewer alive)
                _ctScrollViewer.MaxHeight = double.PositiveInfinity;

                var tagBar = new Border { Height = 6, Background = new SolidColorBrush(C_ACCENT) };   // cyan chart-tie bar
                floatHost = new System.Windows.Controls.DockPanel { LastChildFill = true };
                System.Windows.Controls.DockPanel.SetDock(tagBar, System.Windows.Controls.Dock.Top);
                floatHost.Children.Add(tagBar);
                floatHost.Children.Add(_ctScrollViewer);

                floatWindow = new Window
                {
                    Title         = "Sentinel Deck",
                    Content       = floatHost,
                    Background    = new SolidColorBrush(C_BG),
                    WindowStyle   = WindowStyle.ToolWindow,
                    ResizeMode    = ResizeMode.CanResize,
                    SizeToContent = SizeToContent.Manual,
                    Width         = floatWidth  > 0 ? floatWidth  : 300,
                    Height        = floatHeight > 0 ? floatHeight : 660,
                    MinWidth      = 220, MinHeight = 220,
                    ShowInTaskbar = false, Topmost = DeckFloatPinned,   // always-on-top pin (toggled from the header)
                };
                if (_ctChart != null) floatWindow.Owner = _ctChart;
                if (!double.IsNaN(floatLeft) && !double.IsNaN(floatTop))
                {
                    floatWindow.WindowStartupLocation = WindowStartupLocation.Manual;
                    floatWindow.Left = floatLeft; floatWindow.Top = floatTop;
                }
                else floatWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;

                floatWindow.LocationChanged += (s, ev) => SaveFloatGeometry();
                floatWindow.SizeChanged     += (s, ev) => SaveFloatGeometry();
                floatWindow.Closing         += (s, ev) => { if (panelFloating) { ev.Cancel = true; DockDeck(); } };

                floatWindow.Show();
                panelFloating = true;
                UpdateDockButton();
            }
            catch (Exception ex) { Print("[Sentinel Deck] FloatDeck: " + ex.Message); }
        }

        private void DockDeck()
        {
            try
            {
                if (floatWindow != null)
                {
                    SaveFloatGeometry();
                    panelFloating = false;                      // set before Close so the Closing handler no-ops
                    floatWindow.Content = null;
                    var w = floatWindow; floatWindow = null;
                    try { w.Close(); } catch { }
                }
                panelFloating = false;
                if (floatHost != null) { floatHost.Children.Remove(_ctScrollViewer); floatHost = null; }
                if (_ctScrollViewer != null) InsertPanel();     // re-add the grid row
                UpdateDockButton();
            }
            catch (Exception ex) { Print("[Sentinel Deck] DockDeck: " + ex.Message); }
        }

        private void SaveFloatGeometry()
        {
            try
            {
                if (floatWindow == null || floatWindow.WindowState != WindowState.Normal) return;
                floatLeft = floatWindow.Left; floatTop = floatWindow.Top;
                floatWidth = floatWindow.Width; floatHeight = floatWindow.Height;
            }
            catch { }
        }

        private void UpdateDockButton()
        {
            if (btnDock == null) return;
            btnDock.Content = panelFloating ? "><" : "[]";
            btnDock.ToolTip = panelFloating ? "Dock the deck back into ChartTrader"
                                            : "Pop the deck out to a floating window";
        }

        private void InsertPanel()
        {
            if (_ctTraderGrid == null || _ctScrollViewer == null) return;
            if (_ctTraderGrid.Children.Contains(_ctScrollViewer)) return;
            _ctScrollRow = new RowDefinition { Height = new GridLength(1, GridUnitType.Star) };
            _ctTraderGrid.RowDefinitions.Add(_ctScrollRow);
            Grid.SetRow(_ctScrollViewer, _ctTraderGrid.RowDefinitions.Count - 1);
            if (_ctTraderGrid.ColumnDefinitions.Count > 0)
                Grid.SetColumnSpan(_ctScrollViewer, _ctTraderGrid.ColumnDefinitions.Count);
            _ctTraderGrid.Children.Add(_ctScrollViewer);
        }

        private void RemovePanel()
        {
            try
            {
                if (_ctTraderGrid != null && _ctScrollViewer != null && _ctTraderGrid.Children.Contains(_ctScrollViewer))
                    _ctTraderGrid.Children.Remove(_ctScrollViewer);
                if (_ctTraderGrid != null && _ctScrollRow != null && _ctTraderGrid.RowDefinitions.Contains(_ctScrollRow))
                    _ctTraderGrid.RowDefinitions.Remove(_ctScrollRow);
                _ctScrollRow = null;
            }
            catch { }
        }

        // ── THEME toggle (header button): cycle auto → dark → light → silver → obsidian → blueprint → amber,
        //    persist to Sentinel\theme.txt, apply the on-chart layer instantly, and rebuild this WPF panel so its
        //    own colors follow. ──
        //    ⚠ The face used to be the mode's FIRST LETTER, which broke the moment "amber" joined "auto" — both
        //    would render 'A'. So the glyphs are an EXPLICIT parallel array. "auto" gets '~' (a squiggle, not a
        //    letter) because it isn't a theme at all: it means FOLLOW THE ACTIVE SKIN. Every theme keeps its
        //    initial. Keep ThemeGlyphs the same length as ThemeModes.
        private static readonly string[] ThemeModes  = { "auto", "dark", "light", "silver", "obsidian", "blueprint", "amber", "neon" };
        private static readonly string[] ThemeGlyphs = { "~",    "D",    "L",     "S",      "O",        "B",         "A",     "N"    };
        private static string ThemeGlyph(string mode)
        {
            int i = System.Array.IndexOf(ThemeModes, mode);
            return (i >= 0 && i < ThemeGlyphs.Length) ? ThemeGlyphs[i] : "~";
        }
        private static string ThemePath() => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NinjaTrader 8", "Sentinel", "theme.txt");
        private static string CurrentThemeMode()
        {
            try { var p = ThemePath(); if (System.IO.File.Exists(p)) { var s = System.IO.File.ReadAllText(p).Trim().ToLowerInvariant();
                if (System.Array.IndexOf(ThemeModes, s) >= 0) return s; } } catch { }
            return "auto";
        }
        private void CycleTheme()
        {
            try
            {
                int i = System.Array.IndexOf(ThemeModes, CurrentThemeMode()); if (i < 0) i = 0;
                string next = ThemeModes[(i + 1) % ThemeModes.Length];
                try { System.IO.File.WriteAllText(ThemePath(), next); } catch { }
                // One source of truth for the word→Theme map (SentinelSkin.TryParseTheme); "auto" doesn't parse,
                // which is exactly the signal to fall back to the glue and re-resolve from the active skin.
                SentinelSkin.Theme t;
                if (SentinelSkin.TryParseTheme(next, out t)) SentinelSkin.SetTheme(t);
                else                                         SentinelSkin.ForceThemeRecheck();
                ForceRefresh();   // re-render the on-chart SharpDX layer (cards + risk card) now
                // rebuild the WPF panel so ITS colors follow (colors are read at build time)
                ChartControl?.Dispatcher.BeginInvoke(new Action(() => { try { DisposeWPFControls(); CreateWPFControls(); } catch { } }));
            }
            catch { }
        }

        //  deck layout
        private void BuildDeck()
        {
            SentinelSkin.MaybeRefreshTheme();   // load the active theme (Dark/Light/Silver) before reading C_* colors
            // header: cyan eye + title + dock button + version chip
            var hg = new Grid { Margin = new Thickness(8, 7, 8, 5) };
            hg.ColumnDefinitions.Add(new ColumnDefinition());
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // theme
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // pin
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // dock
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // chip
            var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var deckMark = SentinelSkin.HelmetMark(17, new SolidColorBrush(C_ACCENT));   // shared Spartan brand mark
            deckMark.Margin = new Thickness(0, 0, 5, 0);
            left.Children.Add(deckMark);
            left.Children.Add(Tx("SENTINEL", 13, C_TEXT, true));
            left.Children.Add(Tx(" DECK", 13, C_MUTED));
            Grid.SetColumn(left, 0); hg.Children.Add(left);
            // THEME cycle button (auto → dark → light → silver → obsidian → blueprint → amber) — writes Sentinel\theme.txt + re-themes now
            var btnTheme = new Button { Content = ThemeGlyph(CurrentThemeMode()), Width = 20, Height = 18, FontSize = 10, FontFamily = new FontFamily("Consolas"),
                Style = null, Padding = new Thickness(0), MinWidth = 0, MinHeight = 0, HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(C_DIM), Foreground = new SolidColorBrush(C_ACCENT),
                BorderBrush = new SolidColorBrush(C_BORDER), BorderThickness = new Thickness(1), Cursor = Cursors.Hand,
                ToolTip = "Theme: " + CurrentThemeMode() + "   (~ = auto: follow the active skin)"
                        + "\nClick to cycle: auto → dark → light → silver → obsidian → blueprint → amber" };
            btnTheme.Click += (s, e) => CycleTheme();
            Grid.SetColumn(btnTheme, 1); hg.Children.Add(btnTheme);
            // ALWAYS-ON-TOP pin (accent when pinned) — applies to the floating window
            var btnPin = new Button { Content = "^", Width = 20, Height = 18, FontSize = 11, FontWeight = FontWeights.Bold, FontFamily = new FontFamily("Consolas"),
                Style = null, Padding = new Thickness(0), MinWidth = 0, MinHeight = 0, HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(C_DIM), Foreground = new SolidColorBrush(DeckFloatPinned ? C_ACCENT : C_MUTED),
                BorderBrush = new SolidColorBrush(DeckFloatPinned ? C_ACCENT : C_BORDER), BorderThickness = new Thickness(1), Cursor = Cursors.Hand,
                ToolTip = "Keep the floating deck ALWAYS ON TOP (applies when popped out)" };
            btnPin.Click += (s, e) =>
            {
                DeckFloatPinned = !DeckFloatPinned;
                if (floatWindow != null) { try { floatWindow.Topmost = DeckFloatPinned; } catch { } }
                btnPin.Foreground = new SolidColorBrush(DeckFloatPinned ? C_ACCENT : C_MUTED);
                btnPin.BorderBrush = new SolidColorBrush(DeckFloatPinned ? C_ACCENT : C_BORDER);
            };
            Grid.SetColumn(btnPin, 2); hg.Children.Add(btnPin);
            // pop-out / dock toggle
            btnDock = new Button { Content = "[]", Width = 20, Height = 18, FontSize = 10, FontFamily = new FontFamily("Consolas"),
                Style = null, Padding = new Thickness(0), MinWidth = 0, MinHeight = 0, HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(C_DIM), Foreground = new SolidColorBrush(C_MUTED),
                BorderBrush = new SolidColorBrush(C_BORDER), BorderThickness = new Thickness(1), Cursor = Cursors.Hand,
                ToolTip = "Pop the deck out to a floating window / dock it back" };
            btnDock.Click += (s, e) => TogglePanelFloat();
            Grid.SetColumn(btnDock, 3); hg.Children.Add(btnDock);
            var chip = new Border { Background = new SolidColorBrush(Tint(C_ACCENT, 0.10)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(90, C_ACCENT.R, C_ACCENT.G, C_ACCENT.B)), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5), Padding = new Thickness(6, 3, 6, 3), VerticalAlignment = VerticalAlignment.Center,
                Child = Tx("v0.2.5", 10, C_ACCENT) };
            Grid.SetColumn(chip, 4); hg.Children.Add(chip);
            hudStack.Children.Add(hg);
            hudStack.Children.Add(HRule());

            // PREVIEW SAFETY BAND — directly under the header, above every control. See UpdateWarnBand().
            warnText = Tx("SIM · preview (DEV) build — validate here before going live", 9, C_MUTED);
            warnText.TextWrapping = TextWrapping.Wrap;
            warnBand = new Border { Background = new SolidColorBrush(Tint(C_MUTED, 0.08)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(70, C_MUTED.R, C_MUTED.G, C_MUTED.B)), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 3, 6, 3), Margin = new Thickness(0, 0, 0, 4),
                Child = warnText };
            hudStack.Children.Add(warnBand);

            // Bug-report rail: one click -> one attachable text file. Sits with the preview band because
            // it belongs to the same job (make a stranger's feedback usable), and it is deliberately
            // quiet-styled -- it must be findable, not compete with the order controls.
            btnDiag = new Button { Content = "Export diagnostics for a bug report", Height = 20, FontSize = 10,
                Style = null, Padding = new Thickness(0), MinWidth = 0, MinHeight = 0, Margin = new Thickness(0, 0, 0, 4),
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(C_DIM), Foreground = new SolidColorBrush(C_LABEL),
                BorderBrush = new SolidColorBrush(C_BORDER), BorderThickness = new Thickness(1), Cursor = Cursors.Hand,
                ToolTip = "Write a diagnostics file and open its folder. Sentinel/Support/deck-diag-<time>.txt : version, account + classification, signal/auto-fire config, the Deck log trail and today's order ledger. Attach it to your issue." };
            btnDiag.Click += (s, e) => ExportDiagnostics();
            hudStack.Children.Add(btnDiag);

            StartWarnHeartbeat();   // the band tracks the selector, not the tape

            BuildSignalSection();   // v0.2.2: SIGNAL ARM — top of the panel, collapsible (default collapsed)

            // ORDER TYPE  4 segmented pills
            hudStack.Children.Add(SecLabel("ORDER TYPE"));
            var typeRow = new Grid { Margin = new Thickness(6, 3, 6, 4) };
            for (int i = 0; i < 4; i++) typeRow.ColumnDefinitions.Add(new ColumnDefinition());
            btnTypeMkt = TypePill("MKT",  DeckOrderType.Market);    Grid.SetColumn(btnTypeMkt, 0); typeRow.Children.Add(btnTypeMkt);
            btnTypeLmt = TypePill("LMT",  DeckOrderType.Limit);     Grid.SetColumn(btnTypeLmt, 1); typeRow.Children.Add(btnTypeLmt);
            btnTypeStp = TypePill("STP",  DeckOrderType.Stop);      Grid.SetColumn(btnTypeStp, 2); typeRow.Children.Add(btnTypeStp);
            btnTypeSL  = TypePill("STLM", DeckOrderType.StopLimit); Grid.SetColumn(btnTypeSL, 3);  typeRow.Children.Add(btnTypeSL);
            hudStack.Children.Add(typeRow);

            // QTY  stepper + presets
            hudStack.Children.Add(SecLabel("QUANTITY"));
            var qtyRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 3, 6, 4) };
            qtyRow.Children.Add(StepBtn("-", () => SetQty(deckQty - 1)));
            tbQty = new TextBox { Text = deckQty.ToString(), Width = 44, Margin = new Thickness(3, 0, 3, 0), TextAlignment = TextAlignment.Center,
                Background = new SolidColorBrush(C_DIM), Foreground = new SolidColorBrush(C_TEXT), BorderBrush = new SolidColorBrush(C_BORDER),
                FontFamily = new FontFamily("Consolas"), FontSize = 12, VerticalContentAlignment = VerticalAlignment.Center };
            tbQty.LostFocus += (s, e) => { if (int.TryParse(tbQty.Text, out int q)) SetQty(q); else tbQty.Text = deckQty.ToString(); };
            qtyRow.Children.Add(tbQty);
            qtyRow.Children.Add(StepBtn("+", () => SetQty(deckQty + 1)));
            foreach (int p in new[] { 1, 2, 5, 10 }) { int pp = p; qtyRow.Children.Add(PresetBtn(pp.ToString(), () => SetQty(pp))); }
            hudStack.Children.Add(qtyRow);

            // RISK-BASED SIZING: qty = $risk / (stop-ticks x $/tick). Stop = the Bracket "Stop tk".
            var riskRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 6, 2) };
            btnRiskMode = new Button { Content = "$ RISK", Width = 58, Height = 24, FontSize = 10, FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Consolas"), Background = new SolidColorBrush(C_DIM), Foreground = new SolidColorBrush(C_MUTED),
                BorderBrush = new SolidColorBrush(C_BORDER), BorderThickness = new Thickness(1), Cursor = Cursors.Hand,
                ToolTip = "Size the order by $-risk instead of a fixed qty (uses the Bracket Stop tk as the stop distance)" };
            btnRiskMode.Click += (s, e) => { riskMode = !riskMode; StyleToggle(btnRiskMode, riskMode, "$ RISK"); RecomputeRiskSize(); };
            riskRow.Children.Add(btnRiskMode);
            tbRisk = new TextBox { Text = pRisk.ToString("0"), Width = 60, Margin = new Thickness(4, 0, 4, 0), TextAlignment = TextAlignment.Center,
                Background = new SolidColorBrush(C_DIM), Foreground = new SolidColorBrush(C_TEXT), BorderBrush = new SolidColorBrush(C_BORDER),
                FontFamily = new FontFamily("Consolas"), FontSize = 12, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "$ risk per trade" };
            tbRisk.LostFocus += (s, e) => { pRisk = ParseD(tbRisk, pRisk); RecomputeRiskSize(); };
            riskRow.Children.Add(tbRisk);
            lblRiskCalc = new TextBlock { Text = "", Foreground = new SolidColorBrush(C_MUTED), FontFamily = new FontFamily("Consolas"),
                FontSize = 10, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            riskRow.Children.Add(lblRiskCalc);
            hudStack.Children.Add(riskRow);

            // PRICE (working orders)  editable + offset + click-arm
            lblPrice = Tx("PRICE", 9, C_LABEL, true);
            rowPrice = PriceRow(lblPrice, out tbPrice, true);
            hudStack.Children.Add(rowPrice);
            lblLimit = Tx("LIMIT", 9, C_LABEL, true);
            rowLimit = PriceRow(lblLimit, out tbLimit, false);
            hudStack.Children.Add(rowLimit);

            // BUY / SELL
            var bs = new Grid { Margin = new Thickness(6, 6, 6, 3) };
            bs.ColumnDefinitions.Add(new ColumnDefinition()); bs.ColumnDefinitions.Add(new ColumnDefinition());
            var buy  = BigBtn("BUY",  C_GREEN); buy.Click  += (s, e) => SubmitDeckOrder(OrderAction.Buy);
            var sell = BigBtn("SELL", C_RED);   sell.Click += (s, e) => SubmitDeckOrder(OrderAction.SellShort);
            buyBtnRef = buy; sellBtnRef = sell;   // v0.2.2: SIGNAL ARM highlights the primed side on these
            buy.Margin = new Thickness(0, 0, 3, 0); sell.Margin = new Thickness(3, 0, 0, 0);
            Grid.SetColumn(buy, 0); Grid.SetColumn(sell, 1); bs.Children.Add(buy); bs.Children.Add(sell);
            hudStack.Children.Add(bs);

            // REVERSE / CLOSE
            var rc = new Grid { Margin = new Thickness(6, 0, 6, 3) };
            rc.ColumnDefinitions.Add(new ColumnDefinition()); rc.ColumnDefinitions.Add(new ColumnDefinition());
            var rev = SmallBtn("Reverse", C_AMBER); rev.Click += (s, e) => ReversePosition();
            var cls = SmallBtn("Close",     C_LABEL); cls.Click += (s, e) => ClosePosition();
            rev.Margin = new Thickness(0, 0, 3, 0); cls.Margin = new Thickness(3, 0, 0, 0);
            Grid.SetColumn(rev, 0); Grid.SetColumn(cls, 1); rc.Children.Add(rev); rc.Children.Add(cls);
            hudStack.Children.Add(rc);

            // FLATTEN THIS CHART (scoped)
            var flat = BigBtn("FLATTEN THIS CHART", C_RED);
            flat.FontSize = 12; flat.Margin = new Thickness(6, 2, 6, 4);
            flat.Click += (s, e) => FlattenThisChart();
            hudStack.Children.Add(flat);

            //  trade management (v0.2.0)  collapsible sections 
            BuildTradeManagement();

            // advisory + status
            advisoryText = new TextBlock { Text = "Sentinel: clear", Foreground = new SolidColorBrush(C_MUTED),
                FontFamily = new FontFamily("Consolas"), FontSize = 10, Margin = new Thickness(8, 2, 8, 0), TextWrapping = TextWrapping.Wrap };
            hudStack.Children.Add(advisoryText);
            deckStatus = new TextBlock { Text = "ready", Foreground = new SolidColorBrush(C_MUTED),
                FontFamily = new FontFamily("Consolas"), FontSize = 9, Margin = new Thickness(8, 2, 8, 6), TextWrapping = TextWrapping.Wrap };
            hudStack.Children.Add(deckStatus);
        }

        //  deck helpers
        private void SetQty(int q)
        {
            deckQty = Math.Max(1, q);
            if (tbQty != null) tbQty.Text = deckQty.ToString();
        }

        // Risk-based sizing: qty = $risk / (stopTicks x $/tick), via the Gate. Stop = Bracket "Stop tk".
        private void RecomputeRiskSize()
        {
            if (lblRiskCalc == null) return;
            if (!riskMode) { lblRiskCalc.Text = ""; return; }
            var acct  = cachedAccount ?? ResolveAccount();
            var instr = cachedInstrument ?? ResolveInstrument();
            double tv = instr != null ? SentinelCore.TickValue(instr) : 0.0;
            if (instr == null || tv <= 0 || pStop <= 0 || pRisk <= 0)
            {
                lblRiskCalc.Text = "= set Risk $ + Bracket Stop tk";
                lblRiskCalc.Foreground = new SolidColorBrush(C_MUTED);
                return;
            }
            int q = SentinelCore.SizeForRisk(acct, instr, pStop, pRisk);
            double perC = tv * pStop;
            if (q < 1)
            {
                lblRiskCalc.Text = "= <1 lot: $" + Math.Round(perC) + "/c > $" + Math.Round(pRisk) + " risk (widen risk / tighten stop)";
                lblRiskCalc.Foreground = new SolidColorBrush(C_AMBER);
                deckQty = 1; if (tbQty != null) tbQty.Text = "1";
                return;
            }
            deckQty = q; if (tbQty != null) tbQty.Text = q.ToString();
            lblRiskCalc.Text = "= " + q + " @ $" + Math.Round(perC) + "/c ($" + Math.Round(q * perC) + " risk, " + pStop + "tk)";
            lblRiskCalc.Foreground = new SolidColorBrush(C_ACCENT);
        }

        private void OnTypeSelected(DeckOrderType t)
        {
            deckType = t;
            RefreshTypeButtons();
        }

        private void RefreshTypeButtons()
        {
            StyleType(btnTypeMkt, deckType == DeckOrderType.Market);
            StyleType(btnTypeLmt, deckType == DeckOrderType.Limit);
            StyleType(btnTypeStp, deckType == DeckOrderType.Stop);
            StyleType(btnTypeSL,  deckType == DeckOrderType.StopLimit);

            // price rows: Market hides both; Limit/Stop show PRICE; StopLimit shows STOP + LIMIT
            bool showPrice = deckType != DeckOrderType.Market;
            bool showLimit = deckType == DeckOrderType.StopLimit;
            if (rowPrice != null) rowPrice.Visibility = showPrice ? Visibility.Visible : Visibility.Collapsed;
            if (rowLimit != null) rowLimit.Visibility = showLimit ? Visibility.Visible : Visibility.Collapsed;
            if (lblPrice != null) lblPrice.Text = deckType == DeckOrderType.Stop || deckType == DeckOrderType.StopLimit ? "STOP" : "LIMIT";

            // seed the price box at market if empty
            if (showPrice && tbPrice != null && string.IsNullOrWhiteSpace(tbPrice.Text) && lastClose > 0)
                tbPrice.Text = lastClose.ToString("0.#####");
        }

        private void StyleType(Button b, bool on)
        {
            if (b == null) return;
            b.Background      = new SolidColorBrush(on ? Tint(C_ACCENT, 0.22) : C_DIM);
            b.Foreground      = new SolidColorBrush(on ? C_ACCENT : C_MUTED);
            b.BorderBrush     = new SolidColorBrush(on ? C_ACCENT : C_BORDER);
            b.BorderThickness = new Thickness(on ? 1.5 : 1);
        }

        private Button TypePill(string label, DeckOrderType t)
        {
            var b = new Button { Content = label, Height = 24, FontSize = 9.5, FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Consolas"), Margin = new Thickness(2, 0, 2, 0), Padding = new Thickness(0),
                MinWidth = 0, HorizontalContentAlignment = HorizontalAlignment.Center,   // strip the skin's MinWidth so 4 pills fit the panel
                Background = new SolidColorBrush(C_DIM), Foreground = new SolidColorBrush(C_MUTED),
                BorderBrush = new SolidColorBrush(C_BORDER), BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
            b.Click += (s, e) => OnTypeSelected(t);
            return b;
        }

        private Border PriceRow(TextBlock lbl, out TextBox tb, bool primary)
        {
            var g = new Grid { Margin = new Thickness(6, 2, 6, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            lbl.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(lbl, 0); g.Children.Add(lbl);

            tb = new TextBox { Width = double.NaN, Margin = new Thickness(2, 0, 4, 0), TextAlignment = TextAlignment.Center,
                Background = new SolidColorBrush(C_DIM), Foreground = new SolidColorBrush(C_TEXT), BorderBrush = new SolidColorBrush(C_BORDER),
                FontFamily = new FontFamily("Consolas"), FontSize = 12, VerticalContentAlignment = VerticalAlignment.Center };
            Grid.SetColumn(tb, 1); g.Children.Add(tb);

            var box = tb;   // capture for lambdas
            var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            tools.Children.Add(NudgeBtn("-", () => NudgePrice(box, -1)));
            tools.Children.Add(NudgeBtn("+", () => NudgePrice(box, +1)));
            tools.Children.Add(NudgeBtn("@mkt", () => { if (lastClose > 0) box.Text = RoundTickStr(lastClose); }));
            if (primary)
            {
                btnClickArm = new Button { Content = "o", Width = 22, Height = 20, FontSize = 11, Margin = new Thickness(2, 0, 0, 0),
                    Background = new SolidColorBrush(C_DIM), Foreground = new SolidColorBrush(C_MUTED),
                    BorderBrush = new SolidColorBrush(C_BORDER), BorderThickness = new Thickness(1), Cursor = Cursors.Hand,
                    ToolTip = "Arm: click the chart to set this price" };
                btnClickArm.Click += (s, e) => ToggleClickArm();
                tools.Children.Add(btnClickArm);
            }
            Grid.SetColumn(tools, 2); g.Children.Add(tools);
            return new Border { Child = g, Visibility = Visibility.Collapsed };
        }

        private void ToggleClickArm()
        {
            clickArm = !clickArm;
            if (btnClickArm != null)
            {
                btnClickArm.Background  = new SolidColorBrush(clickArm ? Tint(C_ACCENT, 0.22) : C_DIM);
                btnClickArm.Foreground  = new SolidColorBrush(clickArm ? C_ACCENT : C_MUTED);
                btnClickArm.BorderBrush  = new SolidColorBrush(clickArm ? C_ACCENT : C_BORDER);
            }
            Status(clickArm ? "click the chart to set the price" : "click-to-price off");
        }

        private void OnChartClickForPrice(object sender, MouseButtonEventArgs e)
        {
            if (!clickArm || _lastScale == null || tbPrice == null) return;
            try
            {
                var pt = e.GetPosition(ChartControl);
                double px = _lastScale.GetValueByY((float)(pt.Y * DpiScale()));   // DIP mouse → device px for the chart scale
                var instr = ResolveInstrument();
                if (instr != null) px = instr.MasterInstrument.RoundToTickSize(px);
                tbPrice.Text = px.ToString("0.#####");
                Status("price set " + tbPrice.Text);
            }
            catch { }
            finally { clickArm = false; ToggleClickArmVisualOff(); }
        }

        // ── v0.2.2: order-line drag-to-adjust + hover-attach ──────────────────────────────────────
        private float LineY(double price) { try { return _lastScale != null ? _lastScale.GetYByValue(price) : float.NaN; } catch { return float.NaN; } }

        // which order line (1 stop / 2 target) is within tolerance of a chart Y — 0 = none
        private int HitLine(double mouseY)
        {
            if (_lastScale == null || cardPos == MarketPosition.Flat) return 0;
            float tol = 6f * (float)DpiScale();   // mouseY arrives in device px; keep the grab feel constant across scaling
            if (cardTargetPx > 0) { float y = LineY(cardTargetPx); if (!float.IsNaN(y) && Math.Abs((float)mouseY - y) <= tol) return 2; }
            if (cardStopPx   > 0) { float y = LineY(cardStopPx);   if (!float.IsNaN(y) && Math.Abs((float)mouseY - y) <= tol) return 1; }
            return 0;
        }

        private void OnChartMouseMove(object sender, MouseEventArgs e)
        {
            if (!EnableOrderDrag) { if (_hoverLine != 0) { _hoverLine = 0; ChartControl?.InvalidateVisual(); } return; }
            try
            {
                double my = e.GetPosition(ChartControl).Y * DpiScale();   // DIP → device px (matches the chart scale + LineY)
                if (_dragging)
                {
                    _dragPrice = _lastScale != null ? _lastScale.GetValueByY((float)my) : _dragPrice;
                    FindAttachCandidate((float)my);          // snap-to-indicator preview (also snaps _dragPrice)
                    ChartControl?.InvalidateVisual();
                    e.Handled = true;
                    return;
                }
                int h = HitLine(my);
                if (h != _hoverLine) { _hoverLine = h; ChartControl?.InvalidateVisual(); }
                if (ChartControl != null) ChartControl.Cursor = h != 0 ? Cursors.SizeNS : null;
            }
            catch { }
        }

        private void OnChartMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!EnableOrderDrag || clickArm) return;   // click-to-set-price takes precedence
            try
            {
                double my = e.GetPosition(ChartControl).Y * DpiScale();   // DIP → device px (matches the chart scale + LineY)
                int h = HitLine(my);
                // v0.2.5 diagnostic (opt-in): shows if the grab is reached + coordinates line up.
                if (DeckDragDebug)
                try { SentinelCore.Log("Deck:drag", "down drag=" + EnableOrderDrag + " clickArm=" + clickArm
                    + " myDev=" + my.ToString("0") + " hit=" + h + " dpi=" + DpiScale().ToString("0.##")
                    + " tgtY=" + (cardTargetPx > 0 ? LineY(cardTargetPx).ToString("0") : "-")
                    + " stpY=" + (cardStopPx > 0 ? LineY(cardStopPx).ToString("0") : "-")); } catch { }
                if (h == 0) return;
                _dragging = true; _dragLine = h;
                _dragPrice = _lastScale != null ? _lastScale.GetValueByY((float)my) : 0;
                _attachCand = null; _attachCandPlot = -1; _attachCandName = null;
                try { ChartControl.CaptureMouse(); } catch { }
                e.Handled = true;                          // stop NT pan/select while dragging
            }
            catch { }
        }

        private void OnChartMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging) return;
            try
            {
                try { ChartControl.ReleaseMouseCapture(); } catch { }
                int line = _dragLine; double px = _dragPrice;
                var cand = _attachCand; int candPlot = _attachCandPlot; string candName = _attachCandName;
                _dragging = false; _dragLine = 0; _attachCand = null; _attachCandPlot = -1;
                if (ChartControl != null) { ChartControl.Cursor = null; ChartControl.InvalidateVisual(); }
                e.Handled = true;

                if (cand != null && candPlot >= 0)
                {
                    if (line == 1) { _stopAttInd = cand; _stopAttPlot = candPlot; _stopAttName = candName; }
                    else           { _tgtAttInd  = cand; _tgtAttPlot  = candPlot; _tgtAttName  = candName; }
                    Status((line == 1 ? "STOP" : "TARGET") + " attached -> " + candName);
                    ApplyAttachments(true);                // immediate re-price onto the plot
                }
                else if (line == 1) { _stopAttInd = null; _stopAttPlot = -1; RepriceStop(px); }
                else                { _tgtAttInd  = null; _tgtAttPlot  = -1; RepriceTarget(px); }
            }
            catch (Exception ex) { Status("drag: " + ex.Message); }
        }

        private void OnChartKeyDown(object sender, KeyEventArgs e)
        {
            if (!_dragging || e.Key != Key.Escape) return;
            _dragging = false; _dragLine = 0; _attachCand = null; _attachCandPlot = -1;
            try { ChartControl.ReleaseMouseCapture(); } catch { }
            if (ChartControl != null) { ChartControl.Cursor = null; ChartControl.InvalidateVisual(); }
            Status("drag cancelled");
            e.Handled = true;
        }

        // re-price the working STOP (proven path: StopPriceChanged + Account.Change, same as Breakeven)
        private void RepriceStop(double px)
        {
            var acct = cachedAccount; var instr = cachedInstrument;
            if (acct == null || instr == null) return;
            px = instr.MasterInstrument.RoundToTickSize(px);
            var stops = acct.Orders.Where(o => o.Instrument != null && o.Instrument.FullName == instr.FullName
                && (o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit) && IsActiveExitOrder(o)).ToList();
            if (stops.Count == 0) { Status("no stop to move"); return; }
            foreach (var o in stops) o.StopPriceChanged = px;
            try { acct.Change(stops.ToArray()); }
            catch (Exception ex) { Status("stop move: " + ex.Message); }
        }

        private void RepriceTarget(double px)
        {
            var acct = cachedAccount; var instr = cachedInstrument;
            if (acct == null || instr == null) return;
            px = instr.MasterInstrument.RoundToTickSize(px);
            var tgts = acct.Orders.Where(o => o.Instrument != null && o.Instrument.FullName == instr.FullName
                && o.OrderType == OrderType.Limit && IsActiveExitOrder(o)
                && ((cardPos == MarketPosition.Long  && o.OrderAction == OrderAction.Sell)
                 || (cardPos == MarketPosition.Short && (o.OrderAction == OrderAction.BuyToCover || o.OrderAction == OrderAction.Buy)))).ToList();
            if (tgts.Count == 0) { Status("no target to move"); return; }
            foreach (var o in tgts) o.LimitPriceChanged = px;
            try { acct.Change(tgts.ToArray()); }
            catch (Exception ex) { Status("target move: " + ex.Message); }
        }

        // find an OVERLAY indicator plot near the cursor Y (snap/attach candidate)
        private void FindAttachCandidate(float mouseY)
        {
            _attachCand = null; _attachCandPlot = -1; _attachCandName = null;
            if (ChartControl == null || _lastScale == null) return;
            float tol = 6f * (float)DpiScale();   // mouseY arrives in device px (see OnChartMouseMove)
            int nOverlay = 0; double bestDist = double.MaxValue; string bestName = null; double bestV = double.NaN;
            var scanned = new System.Text.StringBuilder();
            try
            {
                foreach (var ind in ChartControl.Indicators)
                {
                    if (ind == null || ReferenceEquals(ind, this) || !ind.IsOverlay) continue;
                    nOverlay++;
                    var vals = ind.Values;
                    int nplots = vals != null ? vals.Length : 0;
                    if (scanned.Length < 300) scanned.Append(SafeIndName(ind)).Append('(').Append(nplots).Append("p) ");
                    if (vals == null) continue;
                    for (int i = 0; i < vals.Length; i++)
                    {
                        double v = PlotVal(ind, i); if (double.IsNaN(v)) continue;
                        float y = LineY(v);
                        if (float.IsNaN(y)) continue;
                        double dist = Math.Abs(mouseY - y);
                        if (dist < bestDist) { bestDist = dist; bestName = SafeIndName(ind); bestV = v; }
                        if (dist <= tol)
                        {
                            _attachCand = ind; _attachCandPlot = i;
                            _attachCandName = SafeIndName(ind);
                            _dragPrice = v;                    // snap preview to the plot
                            return;
                        }
                    }
                }
                // no snap → one-per-second diagnostic (opt-in) so we can SEE why (0 overlays? plot no Values? too far? DPI?).
                if (DeckDragDebug && Core.Globals.Now - _attachLogTime > TimeSpan.FromSeconds(1))
                {
                    _attachLogTime = Core.Globals.Now;
                    try { SentinelCore.Log("Deck:attach", "no snap · overlays=" + nOverlay
                        + " nearest=" + (bestName ?? "none") + (double.IsNaN(bestV) ? "" : " v=" + bestV.ToString("0.##"))
                        + " dist=" + (bestDist == double.MaxValue ? "n/a" : bestDist.ToString("0")) + "px tol=" + tol.ToString("0")
                        + " dpi=" + DpiScale().ToString("0.##") + " · scan: " + scanned.ToString().Trim()); } catch { }
                }
            }
            catch { }
        }

        private double PlotVal(NinjaTrader.Gui.NinjaScript.IndicatorRenderBase ind, int plot)
        {
            try
            {
                var vals = ind.Values; if (vals == null || plot < 0 || plot >= vals.Length) return double.NaN;
                var s = vals[plot]; if (s == null || s.Count == 0) return double.NaN;
                // Prefer the JUST-CLOSED bar (barsAgo 1..3): a foreign indicator's [0] is the racy/forming value and can
                // read garbage (Coral's [0] came back 4106 vs its real ~4031) — see memory nt-consume-indicator-plots,
                // same reason the SIGNAL ARM reads barsAgo=1. Fall back to [0] only if there's no closed bar yet.
                for (int a = 1; a <= 3 && a < s.Count; a++)
                    if (s.IsValidDataPoint(a)) return s[a];
                if (s.IsValidDataPoint(0)) return s[0];
                return double.NaN;
            }
            catch { return double.NaN; }
        }

        private static string SafeIndName(NinjaTrader.Gui.NinjaScript.IndicatorRenderBase ind)
        {
            // Sentinel tools BLANK their Name (label remover) → fall back to the class name (version suffix stripped).
            try
            {
                var n = ind.Name;
                if (string.IsNullOrEmpty(n)) n = PrettyType(ind);
                return string.IsNullOrEmpty(n) ? "indicator" : n;
            }
            catch { return "indicator"; }
        }

        private static string PrettyType(NinjaTrader.Gui.NinjaScript.IndicatorRenderBase ind)
        {
            try
            {
                string n = ind.GetType().Name;
                int vi = n.LastIndexOf("_v", StringComparison.OrdinalIgnoreCase);   // strip a trailing _v1_3_0 / _V0003 version tag
                if (vi > 0 && vi + 2 < n.Length && char.IsDigit(n[vi + 2])) n = n.Substring(0, vi);
                return n;
            }
            catch { return null; }
        }

        // follow-loop: re-price attached orders to their bound plot value (throttled to >= 1 tick moves)
        private void ApplyAttachments(bool force)
        {
            var instr = cachedInstrument; if (instr == null) return;
            if (cardPos == MarketPosition.Flat) { _stopAttInd = null; _tgtAttInd = null; _stopAttPlot = _tgtAttPlot = -1; return; }
            double ts = instr.MasterInstrument.TickSize; if (ts <= 0) return;
            if (_stopAttInd != null && _stopAttPlot >= 0)
            {
                double v = PlotVal(_stopAttInd, _stopAttPlot);
                if (!double.IsNaN(v))
                {
                    double px = instr.MasterInstrument.RoundToTickSize(v);
                    if (AttachedStopOnlyImprove && cardStopPx > 0)
                    {
                        bool isLong = cardPos == MarketPosition.Long;
                        if ((isLong && px < cardStopPx) || (!isLong && px > cardStopPx)) px = cardStopPx;   // never loosen
                    }
                    if (force || cardStopPx <= 0 || Math.Abs(px - cardStopPx) >= ts - 1e-9) RepriceStop(px);
                }
            }
            if (_tgtAttInd != null && _tgtAttPlot >= 0)
            {
                double v = PlotVal(_tgtAttInd, _tgtAttPlot);
                if (!double.IsNaN(v))
                {
                    double px = instr.MasterInstrument.RoundToTickSize(v);
                    if (force || cardTargetPx <= 0 || Math.Abs(px - cardTargetPx) >= ts - 1e-9) RepriceTarget(px);
                }
            }
        }

        private void ToggleClickArmVisualOff()
        {
            if (btnClickArm == null) return;
            btnClickArm.Background = new SolidColorBrush(C_DIM);
            btnClickArm.Foreground = new SolidColorBrush(C_MUTED);
            btnClickArm.BorderBrush = new SolidColorBrush(C_BORDER);
        }

        private void NudgePrice(TextBox box, int dir)
        {
            var instr = ResolveInstrument();
            double ts = instr != null ? instr.MasterInstrument.TickSize : 0.25;
            double cur;
            if (!double.TryParse(box.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out cur))
                cur = lastClose > 0 ? lastClose : 0;
            cur += dir * ts;
            if (instr != null) cur = instr.MasterInstrument.RoundToTickSize(cur);
            box.Text = cur.ToString("0.#####");
        }

        private string RoundTickStr(double px)
        {
            var instr = ResolveInstrument();
            if (instr != null) px = instr.MasterInstrument.RoundToTickSize(px);
            return px.ToString("0.#####");
        }

        private void RefreshAdvisory()
        {
            if (advisoryText == null) return;
            advisoryText.Text = "Sentinel: " + (cardAdvisory ?? "clear");
            advisoryText.Foreground = new SolidColorBrush(cardAdvisoryOk ? C_MUTED : C_AMBER);
        }

        private void Status(string s)
        {
            if (deckStatus != null) ChartControl?.Dispatcher.InvokeAsync(() => { deckStatus.Text = s; });
        }

        // 
        // Order actions (account-level, unmanaged  the deck OWNS its orders)
        // 
        // automated=false → MANUAL click: gate fails OPEN (a human may always act). automated=true → SIGNAL auto-fire:
        // gate fails CLOSED (a Hard reason BLOCKS) and the entry is forced to MARKET (a signal acts now).
        private void SubmitDeckOrder(OrderAction action, bool automated = false)
        {
            var acct  = ResolveAccount();    if (acct == null)  { Status("no account selected"); return; }
            var instr = ResolveInstrument(); if (instr == null) { Status("no instrument"); return; }
            if (!ValidateAccountForTrading(acct, "Deck", out string reason)) { Status(reason); return; }

            OrderType ot = OrderType.Market; double lim = 0, stp = 0;
            if (!automated)
            switch (deckType)
            {
                case DeckOrderType.Limit:     ot = OrderType.Limit;      if (!TryPrice(tbPrice, instr, out lim)) { Status("enter a limit price"); return; } break;
                case DeckOrderType.Stop:      ot = OrderType.StopMarket; if (!TryPrice(tbPrice, instr, out stp)) { Status("enter a stop price"); return; } break;
                case DeckOrderType.StopLimit: ot = OrderType.StopLimit;
                    if (!TryPrice(tbPrice, instr, out stp)) { Status("enter a stop (trigger) price"); return; }
                    if (!TryPrice(tbLimit, instr, out lim)) { Status("enter a limit price"); return; } break;
            }

            // THE ORDER GATE -- risk-size + classify. Manual = fail OPEN (loud). Automated = fail CLOSED (blocks).
            int q = Math.Max(1, deckQty);
            if (ConsultSentinel)
            {
                try
                {
                    double stopTicks = riskMode ? pStop : 0;
                    double riskD     = riskMode ? pRisk : 0;
                    var gate = SentinelCore.GateEntry(acct, instr.FullName, q, stopTicks, riskD, instr);
                    if (riskMode && gate.Size >= 1) q = gate.Size;   // trust the risk-sized qty
                    if (gate.Level == SentinelCore.GateLevel.Hard)
                    {
                        if (automated) { Status("auto-fire BLOCKED: " + gate.Reason); Print("[Sentinel Deck] auto-fire blocked: " + gate.Reason); return; }   // FAIL-CLOSED
                        Status("!! " + gate.Reason + " -- submitting anyway (manual)");   // loud, but never trap a human
                    }
                    else if (gate.Level == SentinelCore.GateLevel.Advisory)
                        Status("advisory: " + gate.Reason);
                    if (gate.Reason != null) Print("[Sentinel Deck] gate(" + gate.Level + "): " + gate.Reason);
                }
                catch { if (automated) { Status("auto-fire BLOCKED: gate error"); return; } /* manual: fail open */ }
            }

            try
            {
                var o = acct.CreateOrder(instr, action, ot, OrderEntry.Manual, TimeInForce.Day, q, lim, stp, "",
                    _tag + (automated ? "_SIG_" : "_") + action, Core.Globals.MaxDate, null);
                acct.Submit(new[] { o });
                try { SentinelCore.NoteOrderSubmitted(acct.Name); } catch { }   // feed the rate guard
                try { SentinelCore.Ledger.Order(acct.Name, instr.FullName, action.ToString(), ot.ToString(), q, lim > 0 ? lim : stp, automated ? "Deck:signal" : "Deck"); } catch { }
                Status((automated ? "SIGNAL " : "") + (action == OrderAction.Buy ? "BUY " : "SELL ") + q + " " + ot
                    + (ot == OrderType.Market ? "" : " @ " + (deckType == DeckOrderType.StopLimit ? stp + "/" + lim : (lim > 0 ? lim : stp).ToString())));
            }
            catch (Exception ex) { Status("submit failed: " + ex.Message); Print("[Sentinel Deck] submit: " + ex.Message); }
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // v0.2.2: SIGNAL ARM — read any loaded indicator's PLOT as a directional signal, then ARM
        // (highlight BUY/SELL for a human) or AUTO-FIRE (fail-closed via the Gate). No hardcoded
        // signals: sources are discovered from ChartControl.Indicators at runtime (same plumbing as attach).
        // ─────────────────────────────────────────────────────────────────────────────
        private void RescanSignalSources()
        {
            _sigSources.Clear();
            try
            {
                if (ChartControl == null) return;
                var byType = new Dictionary<string, int>();
                foreach (var ind in ChartControl.Indicators)
                {
                    if (ind == null || ReferenceEquals(ind, this)) continue;
                    string tn = ind.GetType().Name;
                    int ord = byType.TryGetValue(tn, out int c) ? c : 0; byType[tn] = ord + 1;
                    string disp = SafeIndName(ind); if (string.IsNullOrEmpty(disp)) disp = tn;
                    if (ord > 0) disp = disp + " #" + (ord + 1);   // disambiguate a 2nd+ instance of the same indicator
                    int nPlots = 0; try { nPlots = ind.Values != null ? ind.Values.Length : 0; } catch { }
                    for (int p = 0; p < nPlots; p++)
                        _sigSources.Add(new SigSrc { Key = tn + "#" + ord + "|" + p, Display = disp + " > " + PlotName(ind, p) });
                }
            }
            catch { }
        }

        private string PlotName(NinjaTrader.Gui.NinjaScript.IndicatorRenderBase ind, int p)
        {
            try { var pl = ind.Plots[p]; if (pl != null && !string.IsNullOrEmpty(pl.Name)) return pl.Name; } catch { }   // indexer + try/catch (avoid .Count: method group on NT's plot collection → CS0019)
            return "plot " + p;
        }

        // resolve a stable key "Type#ordinal|plotIdx" to the live indicator + plot (survives indicator reloads)
        private bool ResolveSigSrc(string key, out NinjaTrader.Gui.NinjaScript.IndicatorRenderBase ind, out int plotIdx)
        {
            ind = null; plotIdx = -1;
            if (string.IsNullOrEmpty(key) || ChartControl == null) return false;
            try
            {
                int bar = key.IndexOf('|'); if (bar < 0) return false;
                if (!int.TryParse(key.Substring(bar + 1), out plotIdx)) return false;
                string left = key.Substring(0, bar);
                int hash = left.IndexOf('#'); if (hash < 0) return false;
                string tn = left.Substring(0, hash);
                if (!int.TryParse(left.Substring(hash + 1), out int ord)) return false;
                int seen = 0;
                foreach (var i in ChartControl.Indicators)
                {
                    if (i == null || ReferenceEquals(i, this) || i.GetType().Name != tn) continue;
                    if (seen == ord) { ind = i; return ind.Values != null && plotIdx >= 0 && plotIdx < ind.Values.Length; }
                    seen++;
                }
            }
            catch { }
            return false;
        }

        private double PlotAt(NinjaTrader.Gui.NinjaScript.IndicatorRenderBase ind, int plot, int barsAgo)
        {
            try
            {
                var vals = ind.Values; if (vals == null || plot < 0 || plot >= vals.Length) return double.NaN;
                var s = vals[plot]; if (s == null || s.Count <= barsAgo || !s.IsValidDataPoint(barsAgo)) return double.NaN;
                return s[barsAgo];
            }
            catch { return double.NaN; }
        }

        // raw signal direction from the configured source + rule (+1 long / -1 short / 0 none), read at bar offset `ago`.
        // uses UI-thread-resolved refs (_sigIndA/_sigIndB) — only READS .Values here on the data thread (attach pattern).
        // `ago` = 1 on BAR CLOSE so we read the JUST-CLOSED bar (a foreign indicator's CURRENT bar may be uncomputed → race).
        private int ComputeSignal(int ago)
        {
            _sigDbgA = double.NaN;
            var indA = _sigIndA; int pa = _sigPlotA;
            if (indA == null || pa < 0) return 0;
            double a0 = PlotAt(indA, pa, ago); _sigDbgA = a0; if (double.IsNaN(a0)) return 0;
            int dir = 0;
            switch (sigRule)
            {
                case SignalRule.Sign:      dir = a0 > 0 ? 1 : a0 < 0 ? -1 : 0; break;
                case SignalRule.Threshold: dir = a0 > sigThreshold ? 1 : a0 < sigThreshold ? -1 : 0; break;
                case SignalRule.RisingFalling:
                {
                    double a1 = PlotAt(indA, pa, ago + 1); if (double.IsNaN(a1)) return 0;
                    dir = a0 > a1 ? 1 : a0 < a1 ? -1 : 0; break;
                }
                case SignalRule.CrossPlotB:
                {
                    var indB = _sigIndB; int pb = _sigPlotB;
                    if (indB == null || pb < 0) return 0;
                    double b0 = PlotAt(indB, pb, ago); if (double.IsNaN(b0)) return 0;
                    dir = a0 > b0 ? 1 : a0 < b0 ? -1 : 0; break;
                }
            }
            if (sigInvert) dir = -dir;
            return dir;
        }

        // (re)resolve the cached source refs — ALWAYS on the UI thread (enable / cycle / rescan)
        private void ReresolveSigRefs()
        {
            if (!ResolveSigSrc(sigSrcA, out _sigIndA, out _sigPlotA)) { _sigIndA = null; _sigPlotA = -1; }
            if (!ResolveSigSrc(sigSrcB, out _sigIndB, out _sigPlotB)) { _sigIndB = null; _sigPlotB = -1; }
        }

        // called each OnBarUpdate (every tick) — fire only on a TRANSITION to a non-zero direction.
        // BAR CLOSE reads the JUST-CLOSED bar (ago=1) and RE-CHECKS EVERY TICK, so a bar-boundary processing-order
        // race with the source indicator self-heals on the next tick (fire stays one-shot per bar via _sigFiredBar).
        private void EvaluateSignal()
        {
            if (!sigEnabled || string.IsNullOrEmpty(sigSrcA)) return;
            bool newBar = CurrentBar != _sigLastBar; _sigLastBar = CurrentBar;

            int ago = sigCadence == SignalCadence.BarClose ? 1 : 0;
            int dir = ComputeSignal(ago);
            if (newBar) UpdateSigStatusText("watching · A=" + (double.IsNaN(_sigDbgA) ? "n/a" : _sigDbgA.ToString("0.###")) + " · " + Dir(dir) + (sigAutoFire ? " [auto]" : ""));   // live readout
            bool transition = dir != 0 && dir != _sigLastDir;
            bool firstEval = !_sigPrimed; _sigPrimed = true;
            _sigLastDir = dir;
            if (!transition) return;

            try { SentinelCore.Log("Deck:sig", "transition dir=" + dir + " A=" + _sigDbgA.ToString("0.###") + " autofire=" + sigAutoFire + " first=" + firstEval + " bar=" + CurrentBar); } catch { }
            _armedDir = dir;
            if (sigAutoFire)
            {
                if (firstEval) { UpdateSigStatusText("watching " + Dir(dir) + " (waiting for a change)"); return; }   // don't fire the state-at-enable
                TryAutoFire(dir);
            }
            else
            {
                PulseArm(dir);
                Status("ARMED " + Dir(dir) + " — " + SrcShort(sigSrcA) + " · click " + (dir > 0 ? "BUY" : "SELL"));
                UpdateSigStatusText("ARMED " + Dir(dir) + " — click " + (dir > 0 ? "BUY" : "SELL"));
            }
        }

        private static string Dir(int d) => d > 0 ? "LONG" : d < 0 ? "SHORT" : "FLAT";

        // AUTO-FIRE: fail-closed, flat-only, opposite signal = reverse (never stacks / adds)
        private void TryAutoFire(int dir)
        {
            if (_sigFiredBar == CurrentBar) { try { SentinelCore.Log("Deck:sig", "autofire skip: already fired this bar"); } catch { } return; }
            var acct = cachedAccount; var instr = cachedInstrument;
            if (acct == null || instr == null) { Status("auto-fire: no account/instrument"); try { SentinelCore.Log("Deck:sig", "autofire abort: no acct/instr"); } catch { } return; }
            var pos = acct.Positions.FirstOrDefault(p => p.Instrument != null && p.Instrument.FullName == instr.FullName && p.Quantity != 0);
            int curDir = pos == null ? 0 : (pos.MarketPosition == MarketPosition.Long ? 1 : -1);
            if (curDir == dir) { try { SentinelCore.Log("Deck:sig", "autofire skip: already " + Dir(dir)); } catch { } return; }

            // fail-closed gate pre-check (covers both the reverse and the flat-entry branch)
            if (ConsultSentinel)
            {
                try
                {
                    var g = SentinelCore.GateEntry(acct, instr.FullName, Math.Max(1, deckQty), riskMode ? pStop : 0, riskMode ? pRisk : 0, instr);
                    if (g.Level == SentinelCore.GateLevel.Hard) { Status("auto-fire BLOCKED: " + g.Reason); try { SentinelCore.Log("Deck:sig", "autofire BLOCKED: " + g.Reason); } catch { } return; }
                }
                catch (Exception gex) { Status("auto-fire BLOCKED: gate error"); try { SentinelCore.Log("Deck:sig", "autofire gate error: " + gex.Message); } catch { } return; }
            }

            _sigFiredBar = CurrentBar;
            try { SentinelCore.Log("Deck:sig", "autofire FIRE dir=" + dir + " curDir=" + curDir + " qty=" + deckQty + " acct=" + acct.Name); } catch { }
            if (curDir != 0) { Status("SIGNAL REVERSE -> " + Dir(dir)); ReversePosition(); }
            else             { SubmitDeckOrder(dir > 0 ? OrderAction.Buy : OrderAction.SellShort, true); }
        }

        // ARM highlight — brighten the primed BUY/SELL button (UI thread)
        private void PulseArm(int dir)
        {
            Ui(() =>
            {
                ClearArmVisualCore();
                var b = dir > 0 ? buyBtnRef : sellBtnRef; if (b == null) return;
                var accent = dir > 0 ? C_GREEN : C_RED;
                b.Background = new SolidColorBrush(Tint(accent, 0.55));
                b.BorderBrush = new SolidColorBrush(C_ACCENT); b.BorderThickness = new Thickness(2.5);
            });
        }
        private void ClearArmVisual() { Ui(ClearArmVisualCore); }
        private void ClearArmVisualCore()
        {
            if (buyBtnRef  != null) { buyBtnRef.Background  = new SolidColorBrush(Tint(C_GREEN, 0.30)); buyBtnRef.BorderBrush  = new SolidColorBrush(C_GREEN); buyBtnRef.BorderThickness  = new Thickness(1.5); }
            if (sellBtnRef != null) { sellBtnRef.Background = new SolidColorBrush(Tint(C_RED, 0.30));   sellBtnRef.BorderBrush = new SolidColorBrush(C_RED);   sellBtnRef.BorderThickness = new Thickness(1.5); }
        }
        private void Ui(Action a) { try { ChartControl?.Dispatcher.InvokeAsync(a); } catch { } }

        // ── section controls (UI-thread click handlers) ──
        // v0.2.2: DROPDOWN source pickers (replaced the cycle buttons — full plot names, no truncation) ──────
        private ComboBox MakeSourceCombo(bool isA)
        {
            var cb = new ComboBox
            {
                Height = 22, FontSize = 10, FontFamily = new FontFamily("Consolas"), Margin = new Thickness(0),
                Background = new SolidColorBrush(C_DIM), Foreground = new SolidColorBrush(C_TEXT),
                BorderBrush = new SolidColorBrush(C_BORDER), MaxDropDownHeight = 260
            };
            cb.SelectionChanged += (s, e) => OnSourceComboChanged(isA);
            return cb;
        }

        private FrameworkElement ComboRow(string label, FrameworkElement cb)
        {
            var g = new Grid { Margin = new Thickness(6, 2, 6, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            var l = Tx(label, 9, C_MUTED); l.VerticalAlignment = VerticalAlignment.Center; l.Margin = new Thickness(0, 0, 5, 0); Grid.SetColumn(l, 0); g.Children.Add(l);
            Grid.SetColumn(cb, 1); g.Children.Add(cb);
            return g;
        }

        private ComboBoxItem MakeSrcItem(string display, string key)
            => new ComboBoxItem { Content = display, Tag = key, FontFamily = new FontFamily("Consolas"), FontSize = 10,
                Background = new SolidColorBrush(C_DIM), Foreground = new SolidColorBrush(C_TEXT) };

        private void PopulateSourceCombos() { PopulateOneCombo(cbSigSrcA, true); PopulateOneCombo(cbSigSrcB, false); }

        private void PopulateOneCombo(ComboBox cb, bool isA)
        {
            if (cb == null) return;
            _sigComboUpdating = true;
            try
            {
                cb.Items.Clear();
                if (!isA) cb.Items.Add(MakeSrcItem("(none)", null));   // B is optional (only the cross rule needs it)
                foreach (var src in _sigSources) cb.Items.Add(MakeSrcItem(src.Display, src.Key));
                string cur = isA ? sigSrcA : sigSrcB;
                ComboBoxItem match = null;
                foreach (ComboBoxItem it in cb.Items) if ((it.Tag as string) == cur) { match = it; break; }
                cb.SelectedItem = match;   // null → nothing selected (fresh)
            }
            catch { }
            finally { _sigComboUpdating = false; }
        }

        private void OnSourceComboChanged(bool isA)
        {
            if (_sigComboUpdating) return;
            var cb = isA ? cbSigSrcA : cbSigSrcB;
            string key = (cb?.SelectedItem as ComboBoxItem)?.Tag as string;
            if (isA) { sigSrcA = key; ResolveSigSrc(sigSrcA, out _sigIndA, out _sigPlotA); }
            else     { sigSrcB = key; ResolveSigSrc(sigSrcB, out _sigIndB, out _sigPlotB); }
            _sigLastDir = 0; _sigPrimed = false; ClearArmVisualCore();
            UpdateSigStatusText("source " + (isA ? "A" : "B") + " = " + (SrcDisplay(key) ?? "none"));
        }

        private void CycleRule()
        {
            sigRule = (SignalRule)(((int)sigRule + 1) % 4);
            btnSigRule.Content = "Rule: " + RuleLabel(sigRule);
            _sigLastDir = 0; _sigPrimed = false;
            UpdateSigStatusText("rule = " + RuleLabel(sigRule) + (sigRule == SignalRule.CrossPlotB ? " (set source B)" : ""));
        }
        private static string RuleLabel(SignalRule r)
            => r == SignalRule.Sign ? "Sign(>0)" : r == SignalRule.RisingFalling ? "Rising" : r == SignalRule.CrossPlotB ? "A x B" : "Thresh";

        private void StyleSigMode()
        {
            bool af = sigAutoFire;
            btnSigMode.Content     = af ? "Mode: AUTO-FIRE" : "Mode: ARM (confirm)";
            btnSigMode.Background  = new SolidColorBrush(af ? Tint(C_AMBER, 0.28) : C_DIM);
            btnSigMode.Foreground  = new SolidColorBrush(af ? C_AMBER : C_MUTED);
            btnSigMode.BorderBrush = new SolidColorBrush(af ? C_AMBER : C_BORDER);
            if (!af) ClearArmVisualCore();
            UpdateSigStatusText(af ? "AUTO-FIRE (fail-closed · flat-only · opposite=reverse)" : "ARM mode — you confirm the fill");
        }
        private void StyleSigCadence()
        {
            btnSigCadence.Content = sigCadence == SignalCadence.BarClose ? "Eval: BAR CLOSE" : "Eval: TICK";
        }

        private string SrcDisplay(string key) { var s = _sigSources.FirstOrDefault(x => x.Key == key); return s?.Display; }
        private string SrcShort(string key)   => SrcDisplay(key) ?? "src";
        private void UpdateSigStatusText(string s) { Ui(() => { if (sigStatus != null) sigStatus.Text = s; }); }

        private void ClosePosition()
        {
            var acct  = ResolveAccount();    if (acct == null)  { Status("no account"); return; }
            var instr = ResolveInstrument(); if (instr == null) return;
            var pos = acct.Positions.FirstOrDefault(p => p.Instrument != null && p.Instrument.FullName == instr.FullName && p.Quantity != 0);
            if (pos == null) { Status("flat  nothing to close"); return; }
            var act = pos.MarketPosition == MarketPosition.Long ? OrderAction.Sell : OrderAction.BuyToCover;
            try
            {
                var o = acct.CreateOrder(instr, act, OrderType.Market, OrderEntry.Manual, TimeInForce.Day,
                    Math.Abs(pos.Quantity), 0, 0, "", _tag + "_Close", Core.Globals.MaxDate, null);
                acct.Submit(new[] { o });
                Status("closing " + Math.Abs(pos.Quantity));
            }
            catch (Exception ex) { Status("close failed: " + ex.Message); }
        }

        private void ReversePosition()
        {
            var acct  = ResolveAccount();    if (acct == null)  { Status("no account"); return; }
            var instr = ResolveInstrument(); if (instr == null) return;
            var pos = acct.Positions.FirstOrDefault(p => p.Instrument != null && p.Instrument.FullName == instr.FullName && p.Quantity != 0);
            if (pos == null) { Status("flat  nothing to reverse"); return; }
            bool isLong = pos.MarketPosition == MarketPosition.Long;
            int flip = Math.Abs(pos.Quantity) * 2;   // net flip to the same size, opposite side
            var act = isLong ? OrderAction.SellShort : OrderAction.Buy;
            try
            {
                // cancel this instrument's working orders first (stale stops/targets)
                var work = acct.Orders.Where(o => o.Instrument != null && o.Instrument.FullName == instr.FullName
                    && (o.OrderState == OrderState.Working || o.OrderState == OrderState.Accepted)).ToArray();
                if (work.Length > 0) acct.Cancel(work);
                var o = acct.CreateOrder(instr, act, OrderType.Market, OrderEntry.Manual, TimeInForce.Day, flip, 0, 0, "",
                    _tag + "_Reverse", Core.Globals.MaxDate, null);
                acct.Submit(new[] { o });
                Status("reversing  " + (isLong ? "SHORT " : "LONG ") + Math.Abs(pos.Quantity));
            }
            catch (Exception ex) { Status("reverse failed: " + ex.Message); }
        }

        // Cancel working orders + market-close  SCOPED to this chart's instrument + selected account only.
        private void FlattenThisChart()
        {
            var acctRef  = ResolveAccount();
            var instrRef = ResolveInstrument();
            if (acctRef == null || instrRef == null) { Status("no account/instrument"); return; }
            Status("flattening this chart");
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    // NT's ATOMIC flatten: cancels working orders for the instrument AND closes the position with one
                    // managed market order — no re-submit race. (The old cancel-then-loop-Sleep(250) re-read the position
                    // and re-fired before the fill propagated on a lagging feed → OVER-flattened into an OPPOSITE trade.)
                    acctRef.Flatten(new[] { instrRef });
                    System.Threading.Thread.Sleep(300);
                    // straggler sweep — cancel any orphaned stop/target that survived the flatten
                    var work = acctRef.Orders.Where(o => o.Instrument != null && o.Instrument.FullName == instrRef.FullName
                        && (o.OrderState == OrderState.Working || o.OrderState == OrderState.Accepted || o.OrderState == OrderState.PartFilled)).ToArray();
                    if (work.Length > 0) acctRef.Cancel(work);
                    Status("flatten complete");
                }
                catch (Exception ex) { Status("flatten failed: " + ex.Message); }
            });
        }

        private bool TryPrice(TextBox box, Instrument instr, out double px)
        {
            px = 0;
            if (box == null || !double.TryParse(box.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out px) || px <= 0) return false;
            px = instr.MasterInstrument.RoundToTickSize(px);
            return true;
        }

        // ── PREVIEW SAFETY BAND (v0.2.5, public testers' preview) ────────────────────────────
        //  This build ships with NO SIM lock — a human must always be able to act, especially to
        //  exit (design decision 2026-07-03, reaffirmed 2026-07-21). That makes this band the ONLY
        //  mitigation on the manual path, so it is built to two rules:
        //    ① IMPOSSIBLE TO MISS — it sits directly under the header, above every control.
        //    ② FAIL TOWARD "LIVE" — an account we cannot classify is shown as REAL MONEY. Warning
        //       too often is a nuisance; warning too little is somebody's account.
        //  Classification uses NT's OWN provider (Playback/Simulator), not a name-prefix guess:
        //  a live account can be named "Sim-whatever" and a name test would wave it through.
        //  The version string lives HERE, not in `Name`: the label remover blanks Name in DataLoaded
        //  (Sentinel standard), so Name is empty by the time anything reads it at runtime -- which is
        //  exactly how the first diagnostics export shipped with a blank version field, in the tool
        //  built to stop ambiguous bug reports. Drop " (DEV)" here as the freeze step (naming law S9).
        private const string DeckVersion = "Sentinel Deck v0.2.5 (DEV)";
        private Border    warnBand;
        private Button    btnDiag;
        private TextBlock warnText;
        private string    _warnLastKey;   // only touch WPF when the state actually changes

        //  ⚠ CORRECTED 2026-07-21 by MEASURING (v1 was wrong twice over). v1 tested
        //  `acct.Connection.Options.Provider`, which describes the CONNECTION SUPPLYING DATA, not the
        //  account's own nature — so `Sim101` classified correctly as SIM while in Playback, then flipped
        //  to "LIVE" the moment a prop connection was active, because NT re-homes Sim101's data feed to
        //  whatever is connected. A false LIVE on Sim101 is the worst outcome for a warnings-only build:
        //  it is alarm fatigue, and a band nobody believes protects nobody.
        //  `Account.Provider` (reflected off NinjaTrader.Core.dll — it exists on Account itself, distinct
        //  from Connection.Options.Provider) is the account's OWN provider and stays Simulator/Playback
        //  for NT's internal accounts regardless of the active feed.
        //  ⚠ We still FAIL TOWARD REAL: Unknown, null, or a throw ⇒ treated as a broker account.
        private static bool IsSimulatedAccount(Account acct)
        {
            try
            {
                if (acct == null) return false;                       // no account → assume real
                Provider p = acct.Provider;
                return p == Provider.Simulator || p == Provider.Playback;
            }
            catch { return false; }
        }

        //  One line per (account, provider) seen — so the classification is VERIFIABLE from sentinel.log
        //  instead of taken on faith. If a tester ever reports a wrong band, this says exactly why.
        private static readonly HashSet<string> _acctProviderLogged = new HashSet<string>();
        private static void LogAccountClass(Account acct)
        {
            try
            {
                if (acct == null) return;
                string key = acct.Name + "|" + acct.Provider;
                if (!_acctProviderLogged.Add(key)) return;
                SentinelCore.Log("Deck:acct", "account '" + acct.Name + "' Provider=" + acct.Provider
                    + " connProvider=" + (acct.Connection != null && acct.Connection.Options != null
                        ? acct.Connection.Options.Provider.ToString() : "n/a")
                    + " => " + (IsSimulatedAccount(acct) ? "SIMULATED" : "REAL-ORDER"));
            }
            catch { }
        }

        //  ⚠ THE BAND MUST NOT DEPEND ON ORDER FLOW (found live 2026-07-21, and it is the worst bug of the three).
        //  v1 refreshed only from OnBarUpdate, where `cachedAccount` is re-resolved every 25 TICKS. In a quiet or
        //  closed market no ticks arrive, so the band kept naming the PREVIOUSLY selected account indefinitely —
        //  switching Sim101 → a funded account could leave "SIM" on screen while real orders are one click away.
        //  A safety readout gated on market activity is not a safety readout. It now runs on its own 1s UI timer
        //  (plus the tick path, harmlessly), so it tracks the ChartTrader selector regardless of the tape.
        private System.Windows.Threading.DispatcherTimer _warnTimer;

        //  Called ON the UI thread (the timer's thread) — read the selector directly, no marshalling.
        private Account ResolveAccountUi()
        {
            try
            {
                if (xAcSelector == null)
                    xAcSelector = GetChartTraderWindow()?.FindFirst("ChartTraderControlAccountSelector") as AccountSelector;
                return xAcSelector != null ? xAcSelector.SelectedAccount : null;
            }
            catch { return null; }
        }

        private void StartWarnHeartbeat()
        {
            try
            {
                if (_warnTimer != null) return;
                _warnTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background)
                    { Interval = TimeSpan.FromSeconds(1) };
                _warnTimer.Tick += (s, e) => { try { UpdateWarnBand(ResolveAccountUi()); } catch { } };
                _warnTimer.Start();
            }
            catch { }
        }

        private void StopWarnHeartbeat()
        {
            try { if (_warnTimer != null) { _warnTimer.Stop(); _warnTimer = null; } } catch { }
        }

        private void UpdateWarnBand(Account acct)
        {
            if (warnBand == null || warnText == null) return;
            LogAccountClass(acct);
            bool live  = !IsSimulatedAccount(acct);
            bool armed = sigEnabled && sigAutoFire;
            string acctName = acct != null ? acct.Name : "(none)";
            string key = (live ? "L" : "S") + (armed ? "A" : "-") + acctName;
            if (key == _warnLastKey) return;                 // per-tick WPF churn is not free
            _warnLastKey = key;

            string msg; Color fg;
            if (live && armed) { msg = "⚠ " + acctName + " · AUTO-FIRE ARMED — places REAL orders unattended"; fg = C_RED; }
            // Live-but-idle is AMBER, not red, for two reasons: the design-system colour law reserves
            // green/red for money + direction, and — more practically — a red band on every live session
            // is alarm fatigue. Red is spent ONLY on the state that can act without you.
            else if (live)     { msg = "⚠ " + acctName + " — REAL orders (prop eval or funded) · PREVIEW (DEV), unvalidated";                 fg = C_AMBER; }
            else if (armed)    { msg = "SIM (" + acctName + ") · auto-fire armed — preview (DEV) build";                                         fg = C_AMBER; }
            else               { msg = "SIM (" + acctName + ") · no money at risk — preview (DEV) build";                         fg = C_MUTED; }

            ChartControl?.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    warnText.Text       = msg;
                    warnText.Foreground = new SolidColorBrush(fg);
                    warnBand.Background  = new SolidColorBrush(Tint(fg, live ? 0.16 : 0.08));
                    warnBand.BorderBrush = new SolidColorBrush(Color.FromArgb(live ? (byte)160 : (byte)70, fg.R, fg.G, fg.B));
                }
                catch { }
            });
        }

        // -- DIAGNOSTICS EXPORT (the bug-report rail for the public preview) ------------------
        //  Auto-fire is shipping to testers UNVALIDATED on purpose: their runs are how it earns a
        //  green light. That only works if a run produces EVIDENCE. Everything needed already gets
        //  recorded -- Ledger rows tagged "Deck:signal" vs "Deck", order names carrying _SIG_, and the
        //  Deck:sig decision trail (fired / already-fired-this-bar / already-in-dir / BLOCKED+reason).
        //  The missing piece was never instrumentation, it was RETRIEVAL: all of it sits in a log on
        //  the tester's machine. This collapses it into ONE text file they can attach to an issue.
        //  Single .txt on purpose -- no zip assembly, nothing for a stranger to get wrong.
        private void ExportDiagnostics()
        {
            try
            {
                string dir = System.IO.Path.Combine(SentinelCore.SettingsDir, "Support");
                System.IO.Directory.CreateDirectory(dir);
                string path = System.IO.Path.Combine(dir, "deck-diag-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");

                Account acct = ResolveAccountUi() ?? cachedAccount;
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("SENTINEL DECK - DIAGNOSTICS EXPORT");
                sb.AppendLine("generated      : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("deck           : " + DeckVersion + "   (label shown on chart: " + ShowIndicatorLabel + ")");
                sb.AppendLine("account        : " + (acct != null ? acct.Name : "(none)")
                    + "  provider=" + (acct != null ? acct.Provider.ToString() : "n/a")
                    + "  class=" + (IsSimulatedAccount(acct) ? "SIMULATED" : "REAL-ORDER"));
                sb.AppendLine("instrument     : " + (cachedInstrument != null ? cachedInstrument.FullName : "(none)"));
                sb.AppendLine("bars           : " + (BarsPeriod != null ? BarsPeriod.ToString() : "(none)"));
                sb.AppendLine();
                //  FULL STATE DUMP, organised to MIRROR THE PANEL sections. A tester writes "the trail
                //  didn't move" or "the line is offset at 150% scaling"; without the active trail mode or
                //  the DPI this file cannot answer either. Auto-fire is what we are grading, but it is not
                //  the only thing they will break.
                sb.AppendLine("-- ENVIRONMENT -----------------------------------------------------");
                sb.AppendLine("theme          : " + SentinelSkin.Active);
                sb.AppendLine("display scale  : " + DpiScale().ToString("0.00") + "   (order-line overlay dpi=" + _olDpi.ToString("0.00") + ")");
                sb.AppendLine("chart trader   : " + (GetChartTraderWindow() != null ? "present" : "NOT FOUND (the deck needs it for account/instrument)"));
                sb.AppendLine("panel          : " + (floatWindow != null ? "floating" : "docked") + "   pinned=" + DeckFloatPinned);
                sb.AppendLine();
                sb.AppendLine("-- POSITION / CARD -------------------------------------------------");
                sb.AppendLine("position       : " + cardPos + " qty=" + cardQty + " avg=" + cardAvg.ToString("0.#####"));
                sb.AppendLine("day pnl        : " + cardDayPnl.ToString("0.##") + "   unrealised=" + cardUnreal.ToString("0.##"));
                sb.AppendLine("stop px        : " + cardStopPx.ToString("0.#####") + "   open risk=" + cardOpenRisk.ToString("0.##"));
                sb.AppendLine();
                sb.AppendLine("-- ENTRY -----------------------------------------------------------");
                sb.AppendLine("order type     : " + deckType);
                sb.AppendLine("quantity       : " + deckQty + (riskMode ? "   [$RISK MODE: risk=" + pRisk.ToString("0") + " -> size from stop]" : ""));
                sb.AppendLine("price offset   : " + TickOffset + " ticks");
                sb.AppendLine();
                sb.AppendLine("-- BRACKET / STOP --------------------------------------------------");
                sb.AppendLine("stop / target  : " + pStop + " / " + pTarget + " ticks");
                sb.AppendLine("auto on entry  : " + autoBracket);
                sb.AppendLine();
                sb.AppendLine("-- BREAKEVEN -------------------------------------------------------");
                sb.AppendLine("trigger/offset : " + pBETrig + " / " + pBEOff + " ticks     autoBE=" + autoBE);
                sb.AppendLine();
                sb.AppendLine("-- TRAILING --------------------------------------------------------");
                sb.AppendLine("active mode    : " + activeTrail + "     autoTrail=" + autoTrail);
                sb.AppendLine("trail ticks    : " + pTrailTk + "     bars=" + pTrailBars);
                sb.AppendLine("atr            : period=" + pAtrPer + " mult=" + pAtrMult.ToString("0.##"));
                sb.AppendLine("half+BE        : trigger=" + pHalfTrig + " offset=" + pHalfOff);
                sb.AppendLine();
                sb.AppendLine("-- ORDER LINES / ATTACHMENTS ---------------------------------------");
                sb.AppendLine("wpf overlay    : " + (OrderLineOverlay ? "ON (always-on-top)" : "OFF (SharpDX, can sit under other cards)"));
                sb.AppendLine("dragging       : " + _dragging + "   hoverLine=" + _hoverLine + "   dragLine=" + _dragLine);
                sb.AppendLine("stop attached  : " + (_stopAttInd != null ? (_stopAttName + " plot#" + _stopAttPlot) : "(none)"));
                sb.AppendLine("target attached: " + (_tgtAttInd  != null ? (_tgtAttName  + " plot#" + _tgtAttPlot) : "(none)"));
                sb.AppendLine("   NOTE: drag-to-attach SNAP is a known-open issue in this preview -- grab works, snap can fail.");
                sb.AppendLine();
                sb.AppendLine("-- RECORD (tick-path tape) -----------------------------------------");
                sb.AppendLine("log tick path  : " + (LogTickPath ? "ON" : "OFF")
                    + "   capturing=" + _tapeActive + "   ticks=" + _tapeTicks + "   partial=" + _tapePartial);
                sb.AppendLine();
                sb.AppendLine("-- SIGNAL ARM / AUTO-FIRE (the feature under test) ------------------");
                sb.AppendLine("watch          : " + sigEnabled);
                sb.AppendLine("mode           : " + (sigAutoFire ? "AUTO-FIRE (submits by itself)" : "ARM (human confirms)"));
                sb.AppendLine("rule           : " + sigRule + "   invert=" + sigInvert + "   threshold=" + sigThreshold);
                sb.AppendLine("cadence        : " + sigCadence);
                sb.AppendLine("source A       : " + (sigSrcA ?? "(none)") + "  resolved=" + (_sigIndA != null) + "  plotIdx=" + _sigPlotA);
                sb.AppendLine("source B       : " + (sigSrcB ?? "(none)") + "  resolved=" + (_sigIndB != null) + "  plotIdx=" + _sigPlotB);
                sb.AppendLine("last A value   : " + (double.IsNaN(_sigDbgA) ? "n/a" : _sigDbgA.ToString("0.#####")));
                sb.AppendLine("last dir       : " + _sigLastDir + "   firedBar=" + _sigFiredBar + "   currentBar=" + CurrentBar);
                sb.AppendLine();
                //  The #1 SIGNAL ARM failure is "source A didn't resolve" -- and that is unanswerable without
                //  knowing what was actually loaded on the chart, with which plots. Enumerating
                //  ChartControl.Indicators is UI-thread-only; the export runs from a button click, so we are
                //  already on it (the same rule the attach/source plumbing follows).
                sb.AppendLine("-- INDICATORS ON THIS CHART (signal sources available) --------------");
                try
                {
                    if (ChartControl == null) sb.AppendLine("(no chart control)");
                    else
                    {
                        var seen = new Dictionary<string, int>();
                        foreach (var ind in ChartControl.Indicators)
                        {
                            if (ind == null || ReferenceEquals(ind, this)) continue;
                            string tn = ind.GetType().Name;
                            int ord = seen.TryGetValue(tn, out int c) ? c : 0; seen[tn] = ord + 1;
                            int nPlots = 0; try { nPlots = ind.Values != null ? ind.Values.Length : 0; } catch { }
                            sb.Append("  " + tn + "#" + ord + "  plots=" + nPlots + "  [");
                            for (int q = 0; q < nPlots; q++) sb.Append((q > 0 ? ", " : "") + q + ":" + PlotName(ind, q));
                            sb.AppendLine("]");
                        }
                        if (seen.Count == 0) sb.AppendLine("  (none -- SIGNAL ARM has nothing to read)");
                    }
                }
                catch (Exception ex) { sb.AppendLine("  (enumeration failed: " + ex.Message + ")"); }
                sb.AppendLine();
                sb.AppendLine("-- sentinel.log (Deck entries, most recent last) --------------------");
                foreach (string ln in TailLines(SentinelCore.LogFile, 400, "[Sentinel:Deck")) sb.AppendLine(ln);
                sb.AppendLine();
                sb.AppendLine("-- ledger today (order/fill trail; Deck:signal = AUTO-FIRE) ---------");
                foreach (string ln in TailLines(SentinelCore.Ledger.FileFor(DateTime.Now), 300, null)) sb.AppendLine(ln);

                System.IO.File.WriteAllText(path, sb.ToString(), new System.Text.UTF8Encoding(false));
                Status("diagnostics written -> " + path);
                try { SentinelCore.Log("Deck", "diagnostics exported -> " + path); } catch { }

                //  Open Explorer with the file SELECTED. A tester should never have to hunt for the artefact we
                //  just asked them to send -- and NT's user folder is not always under Documents (it can be
                //  relocated, or redirected by OneDrive), so "it's in Documents\NinjaTrader 8" is not reliable
                //  guidance. Best-effort only: a failure here must never look like an export failure.
                try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\""); } catch { }
            }
            catch (Exception ex) { Status("diag export FAILED: " + ex.Message); }
        }

        //  Read the tail of a file that NT may be writing to right now (FileShare.ReadWrite is required).
        private static List<string> TailLines(string file, int max, string mustContain)
        {
            var outp = new List<string>();
            try
            {
                if (!System.IO.File.Exists(file)) { outp.Add("(no file: " + file + ")"); return outp; }
                var all = new List<string>();
                using (var fs = new System.IO.FileStream(file, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
                using (var sr = new System.IO.StreamReader(fs))
                {
                    string ln;
                    while ((ln = sr.ReadLine()) != null)
                        if (mustContain == null || ln.IndexOf(mustContain, StringComparison.Ordinal) >= 0) all.Add(ln);
                }
                int start = Math.Max(0, all.Count - max);
                for (int i = start; i < all.Count; i++) outp.Add(all[i]);
                if (start > 0) outp.Insert(0, "... (" + start + " earlier lines omitted)");
            }
            catch (Exception ex) { outp.Add("(read failed: " + ex.Message + ")"); }
            return outp;
        }

        private bool ValidateAccountForTrading(Account acct, string ctx, out string reason)
        {
            reason = null;
            if (acct == null) { reason = "no account selected"; return false; }
            if (acct.Connection == null || acct.Connection.Status != ConnectionStatus.Connected)
            { reason = "'" + acct.Name + "' not connected"; return false; }
            return true;
        }

        private Account ResolveAccount()
        {
            if (ChartControl == null) return null;
            Account acct = null;
            ChartControl.Dispatcher.Invoke(() =>
            {
                xAcSelector = GetChartTraderWindow()?.FindFirst("ChartTraderControlAccountSelector") as AccountSelector;
                acct = xAcSelector?.SelectedAccount;
            });
            return acct;
        }

        // ── Ledger FILL CAPTURE (feeds the dashboard Slippage view) ─────────────────────────
        //  Pin an ExecutionUpdate subscription to the SELECTED account; re-point when the selector
        //  changes; drop on Terminated. We log only DECK-originated fills (order name "<Name>_...").
        //  Pure observation — never touches the order path.
        private void EnsureFillSubscription(Account acct)
        {
            if (ReferenceEquals(acct, _fillAccount)) return;
            if (_fillAccount != null) { try { _fillAccount.ExecutionUpdate -= OnDeckExecution; } catch { } }
            _fillAccount = acct;
            if (_fillAccount != null) { try { _fillAccount.ExecutionUpdate += OnDeckExecution; } catch { } }
        }

        private void OnDeckExecution(object sender, ExecutionEventArgs e)
        {
            try
            {
                Execution exec = e.Execution;
                if (exec == null || exec.Order == null || exec.Instrument == null) return;

                // Deck-originated only — our orders are named "<Name>_<...>"; ignore other tools' / platform fills
                string on = exec.Order.Name;
                if (string.IsNullOrEmpty(on) || !on.StartsWith(_tag + "_", StringComparison.Ordinal)) return;

                OrderState st = exec.Order.OrderState;
                if (st != OrderState.Filled && st != OrderState.PartFilled) return;
                int qty = exec.Quantity;
                if (qty <= 0) return;

                // defensive dedupe (ExecutionUpdate can re-fire); bounded so a long session can't grow unbounded
                if (!string.IsNullOrEmpty(exec.ExecutionId))
                {
                    if (_seenDeckExecIds.Count > 2000) _seenDeckExecIds.Clear();
                    if (!_seenDeckExecIds.Add(exec.ExecutionId)) return;
                }

                double intended = (exec.Order.OrderType == OrderType.Limit || exec.Order.OrderType == OrderType.StopLimit) ? exec.Order.LimitPrice
                                : (exec.Order.OrderType == OrderType.StopMarket ? exec.Order.StopPrice : 0);
                double tick = exec.Instrument.MasterInstrument != null ? exec.Instrument.MasterInstrument.TickSize : 0;
                SentinelCore.Ledger.Fill(
                    exec.Account != null ? exec.Account.Name : (_fillAccount != null ? _fillAccount.Name : "?"),
                    exec.Instrument.FullName, exec.Order.OrderAction.ToString(), qty, intended, exec.Price, tick, "Deck:" + on);
            }
            catch { }
        }

        private Instrument ResolveInstrument()
        {
            if (ChartControl == null) return Instrument;
            Instrument instr = null;
            ChartControl.Dispatcher.Invoke(() =>
            {
                xInSelector = GetChartTraderWindow()?.FindFirst("ChartWindowInstrumentSelector") as InstrumentSelector;
                instr = xInSelector?.Instrument;
            });
            return instr ?? Instrument;
        }

        private Window GetChartTraderWindow()
        {
            if (ChartControl?.Parent == null) return null;
            return Window.GetWindow(ChartControl.Parent);
        }

        // 
        // Risk card (SharpDX / Direct2D)  account tracking, flight-instrument
        // 
        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            try { base.OnRender(chartControl, chartScale); } catch { }
            _lastScale = chartScale;
            SentinelSkin.MaybeRefreshTheme();   // theme-aware risk card (this render path doesn't go through Painter.Begin)
            // v0.2.5: WPF overlay handled here (before any early-return), so it also HIDES when lines shouldn't show.
            if (OrderLineOverlay && chartScale != null)
                UpdateOrderOverlay(chartScale, ShowOrderLines && cardPos != MarketPosition.Flat);
            else if (_olCanvas != null)
                try { ChartControl?.Dispatcher?.InvokeAsync(RemoveOverlay); } catch { }   // toggled off → tear the overlay down
            if (RenderTarget == null || chartControl == null || (!ShowRiskCard && !ShowOrderLines)) return;
            ChartControl?.Dispatcher.InvokeAsync(RefreshAdvisory);

            var pool = new List<System.IDisposable>();
            try
            {
                var rt = RenderTarget;
                if (riskTextFactory == null) riskTextFactory = new SharpDX.DirectWrite.Factory();
                var dwf = riskTextFactory;

                System.Func<SharpDX.Color4, SharpDX.Direct2D1.SolidColorBrush> B = c =>
                { var b = new SharpDX.Direct2D1.SolidColorBrush(rt, c); pool.Add(b); return b; };

                var cInk = SentinelSkin.CInk; var cInk2 = SentinelSkin.CInk2; var cMute = SentinelSkin.CMute;
                var cAccent = SentinelSkin.CAccent; var cUp = SentinelSkin.CUp; var cDown = SentinelSkin.CDown;
                var cWarn = SentinelSkin.CWarn;  var cLine = SentinelSkin.CLine;   var cFaint = SentinelSkin.CFaint;

                // v0.2.1: on-chart order lines (drawn first; the risk card renders on top).
                // v0.2.5: skip the SharpDX lines when the WPF overlay owns them (handled above OnRender's early-return).
                if (ShowOrderLines && cardPos != MarketPosition.Flat && !OrderLineOverlay)
                    DrawOrderLines(rt, dwf, B, chartScale, cAccent, cUp, cDown, cInk, cMute);
                if (!ShowRiskCard) return;   // lines-only mode; the finally still disposes the pool

                bool inPos = cardPos != MarketPosition.Flat;
                float cardW = 300f, margin = 14f, pad = 15f;
                float yHdr = 24f, yPnl = 44f, yPos = inPos ? 46f : 34f, yBar = 22f, yGov = cardGovOn ? 28f : 0f, yFoot = 20f;
                float cardH = pad + yHdr + yPnl + yPos + yBar + yGov + yFoot;

                // v0.2.1 FIX: position within the PRICE PANEL (ChartPanel), not the whole chart. Using
                // chartControl.ActualHeight included subpanels (e.g. an ADX pane) so a BottomRight card landed
                // BELOW the price panel and got clipped → invisible. ChartPanel bounds keep it on the price panel
                // and let it co-stack with the other Sentinel price-panel cards via the shared CardLayout.
                // 2026-07-10 FLICKER FIX: this was the ONLY card keyed on `ChartPanel ?? (object)chartControl`.
                // When ChartPanel was momentarily null the Deck registered in a DIFFERENT CardLayout bucket; its slot
                // in the real panel then went stale (2s) and was pruned, so the top columns' reserved space for this
                // bottom-anchored card vanished (budget 430 → 625px) and every sensor card expanded — then collapsed
                // again the instant ChartPanel returned. That 0-collapsed ⇄ 6-collapsed swing is what flashed the
                // chart. A card with no panel has no place on the panel: skip it, and keep ONE bucket per panel.
                if (ChartPanel == null) return;
                float px = ChartPanel.X, py = ChartPanel.Y, pw = ChartPanel.W, ph = ChartPanel.H;
                // pinned: this is the RISK card (position / P&L / governor). It must never be collapsed or shrunk by
                // a crowded column — an unreadable risk readout is worse than a missing sensor. (It also draws its own
                // rounded body rather than Painter.Card, so it is outside the scale-to-fit path regardless.)
                var slot = SentinelSkin.CardLayout.Place(this, ChartPanel, px, py, pw, ph, RiskCardCorner, cardW, cardH, margin, pinned: true);
                float x = slot.X, y = slot.Y;

                // glass card
                var rr = new SharpDX.Direct2D1.RoundedRectangle { Rect = new SharpDX.RectangleF(x, y, cardW, cardH), RadiusX = 13f, RadiusY = 13f };
                var gsc = new SharpDX.Direct2D1.GradientStopCollection(rt, new[] {
                    new SharpDX.Direct2D1.GradientStop { Color = SentinelSkin.CGlassTop, Position = 0f },
                    new SharpDX.Direct2D1.GradientStop { Color = SentinelSkin.CGlassBot, Position = 1f } });
                pool.Add(gsc);
                var glass = new SharpDX.Direct2D1.LinearGradientBrush(rt,
                    new SharpDX.Direct2D1.LinearGradientBrushProperties { StartPoint = new SharpDX.Vector2(x, y), EndPoint = new SharpDX.Vector2(x, y + cardH) }, gsc);
                pool.Add(glass);
                rt.FillRoundedRectangle(rr, glass);
                var borderC = cardAdvisoryOk ? cLine : cWarn;
                rt.DrawRoundedRectangle(rr, B(borderC), 1.2f);
                rt.DrawLine(new SharpDX.Vector2(x + 13f, y + 1.4f), new SharpDX.Vector2(x + cardW - 13f, y + 1.4f), B(new SharpDX.Color4(cInk.Red, cInk.Green, cInk.Blue, 0.06f)), 1f);

                float ix = x + pad, iw = cardW - pad * 2, iy = y + pad;

                // header: live dot  title  account  state pill
                var dotC = cardAdvisoryOk ? cAccent : cWarn;
                rt.FillEllipse(new SharpDX.Direct2D1.Ellipse(new SharpDX.Vector2(ix + 4f, iy + 8f), 6.5f, 6.5f), B(new SharpDX.Color4(dotC.Red, dotC.Green, dotC.Blue, 0.26f)));
                rt.FillEllipse(new SharpDX.Direct2D1.Ellipse(new SharpDX.Vector2(ix + 4f, iy + 8f), 3.2f, 3.2f), B(dotC));
                DT(rt, dwf, "SENTINEL DECK", ix + 15f, iy - 1f, 150f, 18f, B(cInk), 12f, true, SharpDX.DirectWrite.TextAlignment.Leading);
                DT(rt, dwf, cardAcct ?? "", ix, iy + 1f, iw, 14f, B(cMute), 9.5f, false, SharpDX.DirectWrite.TextAlignment.Trailing);
                iy += yHdr;

                // big day P&L (kerned currency mark)
                var pnlC = cardDayPnl >= 0 ? cUp : cDown;
                DT(rt, dwf, (cardDayPnl >= 0 ? "+$" : "-$"), ix - 2f, iy + 16f, 24f, 20f, B(pnlC), 15f, false, SharpDX.DirectWrite.TextAlignment.Trailing);
                DT(rt, dwf, Math.Abs(cardDayPnl).ToString("N0"), ix + 24f, iy - 3f, 200f, 46f, B(pnlC), 36f, false, SharpDX.DirectWrite.TextAlignment.Leading);
                DT(rt, dwf, "DAY P&L", ix, iy + 2f, iw, 12f, B(cMute), 8.5f, true, SharpDX.DirectWrite.TextAlignment.Trailing);
                iy += yPnl;

                // position / uP&L   or FLAT
                if (inPos)
                {
                    bool isLong = cardPos == MarketPosition.Long;
                    DT(rt, dwf, "POSITION", ix, iy, iw, 12f, B(cMute), 8.5f, true, SharpDX.DirectWrite.TextAlignment.Leading);
                    DT(rt, dwf, (isLong ? "LONG " : "SHORT ") + cardQty + " @ " + cardAvg.ToString("0.##"),
                        ix, iy + 12f, iw, 18f, B(isLong ? cUp : cDown), 13f, false, SharpDX.DirectWrite.TextAlignment.Leading);
                    var uC = cardUnreal >= 0 ? cUp : cDown;
                    DT(rt, dwf, "OPEN", ix, iy, iw, 12f, B(cMute), 8.5f, true, SharpDX.DirectWrite.TextAlignment.Trailing);
                    DT(rt, dwf, (cardUnreal >= 0 ? "+$" : "-$") + Math.Abs(cardUnreal).ToString("N0"),
                        ix, iy + 12f, iw, 18f, B(uC), 13f, false, SharpDX.DirectWrite.TextAlignment.Trailing);
                    string riskS = cardStopPx > 0 ? "risk $" + cardOpenRisk.ToString("N0") + "  stop " + cardStopPx.ToString("0.##") : "NO STOP";
                    DT(rt, dwf, riskS, ix, iy + 30f, iw, 14f, B(cardStopPx > 0 ? cInk2 : cDown), 11f, false, SharpDX.DirectWrite.TextAlignment.Leading);
                }
                else
                {
                    DT(rt, dwf, "FLAT", ix, iy + 6f, iw, 22f, B(cInk2), 15f, false, SharpDX.DirectWrite.TextAlignment.Center);
                }
                iy += yPos;

                // v0.2.1: bar timer / tick counter — completion of the current bar + a bar-type-aware label
                DT(rt, dwf, "BAR", ix, iy, iw, 12f, B(cMute), 8.5f, true, SharpDX.DirectWrite.TextAlignment.Leading);
                DT(rt, dwf, cardBarText ?? "", ix, iy, iw, 12f, B(cInk2), 9.5f, false, SharpDX.DirectWrite.TextAlignment.Trailing);
                var btrk = new SharpDX.Direct2D1.RoundedRectangle { Rect = new SharpDX.RectangleF(ix, iy + 14f, iw, 5f), RadiusX = 2.5f, RadiusY = 2.5f };
                rt.FillRoundedRectangle(btrk, B(cFaint));
                float bfw = iw * Math.Max(0f, Math.Min(1f, cardBarPct));
                if (bfw > 2f) { var bfb = new SharpDX.Direct2D1.RoundedRectangle { Rect = new SharpDX.RectangleF(ix, iy + 14f, bfw, 5f), RadiusX = 2.5f, RadiusY = 2.5f }; rt.FillRoundedRectangle(bfb, B(cAccent)); }
                iy += yBar;

                // governor day-cap bar (advisory)
                if (cardGovOn)
                {
                    double frac = cardGovCap > 0 ? Math.Max(0, Math.Min(1, cardGovDay / cardGovCap)) : 0;
                    DT(rt, dwf, "DAY CAP", ix, iy, iw, 12f, B(cMute), 8.5f, true, SharpDX.DirectWrite.TextAlignment.Leading);
                    DT(rt, dwf, "$" + Math.Round(cardGovDay) + " / $" + Math.Round(cardGovCap), ix, iy, iw, 12f, B(cInk2), 9.5f, false, SharpDX.DirectWrite.TextAlignment.Trailing);
                    var trk = new SharpDX.Direct2D1.RoundedRectangle { Rect = new SharpDX.RectangleF(ix, iy + 15f, iw, 6f), RadiusX = 3f, RadiusY = 3f };
                    rt.FillRoundedRectangle(trk, B(cFaint));
                    float fw = (float)(iw * frac);
                    if (fw > 3f) { var fb = new SharpDX.Direct2D1.RoundedRectangle { Rect = new SharpDX.RectangleF(ix, iy + 15f, fw, 6f), RadiusX = 3f, RadiusY = 3f }; rt.FillRoundedRectangle(fb, B(cAccent)); }
                    iy += yGov;
                }

                // footer: advisory
                rt.DrawLine(new SharpDX.Vector2(ix, iy + 3f), new SharpDX.Vector2(ix + iw, iy + 3f), B(new SharpDX.Color4(cInk.Red, cInk.Green, cInk.Blue, 0.06f)), 1f);
                var advC = cardAdvisoryOk ? cUp : cWarn;
                DT(rt, dwf, "SENTINEL", ix, iy + 7f, 70f, 14f, B(cMute), 8.5f, true, SharpDX.DirectWrite.TextAlignment.Leading);
                DT(rt, dwf, cardAdvisory ?? "clear", ix + 60f, iy + 6f, iw - 60f, 14f, B(advC), 10f, false, SharpDX.DirectWrite.TextAlignment.Trailing);
            }
            catch (Exception ex) { Print("[Sentinel Deck] OnRender: " + ex.Message); }
            finally { foreach (var d in pool) { try { d.Dispose(); } catch { } } }
        }

        // v0.2.1: draw the live position's ENTRY / STOP / TARGET as horizontal chart lines with R / $ / tick labels.
        private void DrawOrderLines(SharpDX.Direct2D1.RenderTarget rt, SharpDX.DirectWrite.Factory dwf,
            System.Func<SharpDX.Color4, SharpDX.Direct2D1.SolidColorBrush> B, ChartScale chartScale,
            SharpDX.Color4 cAccent, SharpDX.Color4 cUp, SharpDX.Color4 cDown, SharpDX.Color4 cInk, SharpDX.Color4 cMute)
        {
            if (ChartPanel == null || chartScale == null) return;
            // line span measured from the label side, honouring "Order line width %"
            float px = ChartPanel.X, pw = ChartPanel.W;
            float len = pw * (Math.Max(5, Math.Min(100, OrderLineWidthPct)) / 100f);
            bool rightSide = OrderLineLabelSide == LineLabelSide.Right;
            float lineX0 = rightSide ? (px + pw - len) : px;
            float lineX1 = rightSide ? (px + pw)       : (px + len);
            foreach (var d in ComputeOrderLines())
                OrderLine(rt, dwf, B, lineX0, lineX1, rightSide, chartScale, d.Id, d.Price,
                    d.Role == 1 ? cUp : d.Role == 2 ? cDown : cAccent, d.Tag, d.Detail, d.Attach);
        }

        // v0.2.5: the ENTRY/STOP/TARGET line DATA (price · color role · label), computed once so the SharpDX renderer
        // and the WPF overlay draw the SAME thing (no divergence). Role: 0 accent · 1 up/green · 2 down/red.
        private struct OLData { public int Id; public double Price; public int Role; public string Tag; public string Detail; public string Attach; }

        private List<OLData> ComputeOrderLines()
        {
            var list = new List<OLData>(3);
            bool isLong = cardPos == MarketPosition.Long;

            list.Add(new OLData { Id = 0, Price = cardAvg, Role = 0, Tag = "ENTRY",
                Detail = (isLong ? "LONG " : "SHORT ") + cardQty + " @ " + cardAvg.ToString("0.##"), Attach = null });

            if (cardStopPx > 0 || (_dragging && _dragLine == 1))
            {
                bool drag = _dragging && _dragLine == 1;
                double p = drag ? _dragPrice : cardStopPx;
                // SIGN-AWARE stop pill: a stop trailed PAST entry (long above / short below) is LOCKED PROFIT, not risk.
                bool stopLocked = isLong ? (cardStopPx > cardAvg) : (cardStopPx < cardAvg);
                double stopR = pStop > 0 ? cardStopTicks / (double)pStop : 0;   // magnitude in R (configured stop tick = 1R)
                string d = drag ? (_attachCandName != null ? "-> " + _attachCandName : "@ " + p.ToString("0.##"))
                                : stopLocked
                                    ? "LOCK +$" + cardOpenRisk.ToString("N0") + "   +" + stopR.ToString("0.0") + "R   " + cardStopTicks.ToString("0") + "t"
                                    : "-$" + cardOpenRisk.ToString("N0") + "   -" + stopR.ToString("0.0") + "R   " + cardStopTicks.ToString("0") + "t";
                list.Add(new OLData { Id = 1, Price = p, Role = stopLocked ? 1 : 2, Tag = "STOP", Detail = d, Attach = _stopAttName });
            }

            if (cardTargetPx > 0 || (_dragging && _dragLine == 2))
            {
                bool drag = _dragging && _dragLine == 2;
                double p = drag ? _dragPrice : cardTargetPx;
                string d = drag ? (_attachCandName != null ? "-> " + _attachCandName : "@ " + p.ToString("0.##"))
                                : "+$" + cardTargetProfit.ToString("N0") + "   " + cardTargetR.ToString("0.0") + "R   " + cardTargetTicks.ToString("0") + "t";
                list.Add(new OLData { Id = 2, Price = p, Role = 1, Tag = "TARGET", Detail = d, Attach = _tgtAttName });
            }
            return list;
        }

        // v0.2.5: the display-scale factor (device px ÷ this = WPF DIP). chartScale.GetYByValue / GetValueByY work in
        // device px; WPF mouse positions (e.GetPosition) are DIPs. At >100% scaling the two diverge by this factor, so
        // BOTH the overlay (render→DIP) and the drag hit-test (DIP mouse→render) must go through it. 1.0 at 100%.
        private double DpiScale()
        {
            if (!_dpiDone)
            {
                try { var s = System.Windows.PresentationSource.FromVisual(ChartControl);
                      if (s != null) { _olDpi = s.CompositionTarget.TransformToDevice.M11; _dpiDone = true; } } catch { }
            }
            return _olDpi > 0 ? _olDpi : 1.0;
        }

        // ── v0.2.5: WPF ORDER-LINE OVERLAY (always-on-top, hit-transparent) ─────────────────────────
        // Render thread: compute each line's Y (price->pixel) + label, then marshal ONE update to the UI thread.
        // Drag/hover is unaffected (chart mouse events, see OnChartMouseMove) — this is purely the visual layer.
        private void UpdateOrderOverlay(ChartScale scale, bool show)
        {
            if (ChartPanel == null) return;
            float px = ChartPanel.X, pw = ChartPanel.W, pyTop = ChartPanel.Y, pyBot = ChartPanel.Y + ChartPanel.H;
            float len = pw * (Math.Max(5, Math.Min(100, OrderLineWidthPct)) / 100f);
            bool rightSide = OrderLineLabelSide == LineLabelSide.Right;
            float lineX0 = rightSide ? (px + pw - len) : px;
            float lineX1 = rightSide ? (px + pw)       : (px + len);

            var vis = new bool[3]; var ys = new float[3]; var texts = new string[3];
            var roles = new int[3]; var hovers = new bool[3];
            if (show)
            {
                foreach (var d in ComputeOrderLines())
                {
                    if (d.Id < 0 || d.Id > 2 || d.Price <= 0) continue;
                    float y; try { y = scale.GetYByValue(d.Price); } catch { continue; }
                    if (float.IsNaN(y) || float.IsInfinity(y) || y < pyTop - 2f || y > pyBot + 2f) continue;
                    int i = d.Id;
                    vis[i] = true; ys[i] = y; roles[i] = d.Role;
                    hovers[i] = _hoverLine == d.Id || (_dragging && _dragLine == d.Id);
                    texts[i] = d.Tag + "   " + d.Detail
                             + (!string.IsNullOrEmpty(d.Attach) && !(_dragging && _dragLine == d.Id) ? "   -> " + d.Attach : "");
                }
            }
            try { ChartControl?.Dispatcher?.InvokeAsync(() =>
                ApplyOverlay(vis, ys, texts, roles, hovers, lineX0, lineX1, pyTop, rightSide)); } catch { }
        }

        // UI thread: lazily build the hit-transparent Canvas + 3 reusable line/pill slots, above all SharpDX rendering.
        private void EnsureOverlay()
        {
            if (_olCanvas != null) return;
            var host = ChartControl?.Parent as System.Windows.Controls.Panel;   // the chart grid
            if (host == null) return;
            _olCanvas = new System.Windows.Controls.Canvas { IsHitTestVisible = false };
            System.Windows.Controls.Panel.SetZIndex(_olCanvas, 10000);          // above every indicator's SharpDX layer
            host.Children.Add(_olCanvas);
            DpiScale();   // ensure the shared render-px↔DIP factor is computed
            _olLines = new System.Windows.Shapes.Line[3];
            _olPills = new System.Windows.Controls.Border[3];
            _olTxts  = new System.Windows.Controls.TextBlock[3];
            for (int i = 0; i < 3; i++)
            {
                var ln = new System.Windows.Shapes.Line { SnapsToDevicePixels = true, Visibility = System.Windows.Visibility.Collapsed };
                var tb = new System.Windows.Controls.TextBlock { FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                    FontWeight = System.Windows.FontWeights.SemiBold, FontSize = 11.5 };
                var bd = new System.Windows.Controls.Border { CornerRadius = new System.Windows.CornerRadius(4),
                    BorderThickness = new System.Windows.Thickness(1), Padding = new System.Windows.Thickness(6, 1, 6, 1),
                    Child = tb, Visibility = System.Windows.Visibility.Collapsed,
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(230, C_BG.R, C_BG.G, C_BG.B)) };
                _olLines[i] = ln; _olPills[i] = bd; _olTxts[i] = tb;
                _olCanvas.Children.Add(ln); _olCanvas.Children.Add(bd);
            }
        }

        // UI thread: position/color/text the 3 slots. Coords are render pixels → divide by DPI for WPF DIPs (calibratable).
        private void ApplyOverlay(bool[] vis, float[] ys, string[] texts, int[] roles, bool[] hovers,
                                  float lineX0, float lineX1, float pyTop, bool rightSide)
        {
            try
            {
                EnsureOverlay();
                if (_olCanvas == null) return;
                double m = _olDpi;
                for (int i = 0; i < 3; i++)
                {
                    var ln = _olLines[i]; var bd = _olPills[i]; var tb = _olTxts[i];
                    if (!vis[i]) { ln.Visibility = System.Windows.Visibility.Collapsed; bd.Visibility = System.Windows.Visibility.Collapsed; continue; }
                    var col = roles[i] == 1 ? C_GREEN : roles[i] == 2 ? C_RED : C_ACCENT;
                    var brush = new System.Windows.Media.SolidColorBrush(col);
                    double y = ys[i] / m, x0 = lineX0 / m, x1 = lineX1 / m;
                    ln.X1 = x0; ln.X2 = x1; ln.Y1 = y; ln.Y2 = y;
                    ln.Stroke = brush; ln.StrokeThickness = hovers[i] ? 2.2 : 1.3; ln.Opacity = hovers[i] ? 0.95 : 0.62;
                    // v0.2.5: ENTRY (i==0) shows the PILL only — NT already draws its own entry line, so ours is redundant.
                    ln.Visibility = i == 0 ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

                    tb.Text = texts[i] ?? ""; tb.Foreground = brush; bd.BorderBrush = brush;
                    bd.Measure(new System.Windows.Size(4000, 40));
                    double w = bd.DesiredSize.Width, h = bd.DesiredSize.Height;
                    double lx = rightSide ? (x1 - 6 - w) : (x0 + 6);
                    double ly = y - h - 2; if (ly < pyTop / m + 1) ly = y + 2;
                    System.Windows.Controls.Canvas.SetLeft(bd, lx); System.Windows.Controls.Canvas.SetTop(bd, ly);
                    bd.Visibility = System.Windows.Visibility.Visible;
                }
            }
            catch { }
        }

        private void RemoveOverlay()
        {
            try
            {
                if (_olCanvas != null && ChartControl?.Parent is System.Windows.Controls.Panel host
                    && host.Children.Contains(_olCanvas))
                    host.Children.Remove(_olCanvas);
            }
            catch { }
            _olCanvas = null; _olLines = null; _olPills = null; _olTxts = null;
        }

        // one horizontal price line + a label chip; highlights on hover/drag, tags "-> <ind>" when attached
        private void OrderLine(SharpDX.Direct2D1.RenderTarget rt, SharpDX.DirectWrite.Factory dwf,
            System.Func<SharpDX.Color4, SharpDX.Direct2D1.SolidColorBrush> B, float lineX0, float lineX1, bool rightSide,
            ChartScale chartScale, int lineId, double price, SharpDX.Color4 col, string tag, string detail, string attachName)
        {
            if (price <= 0) return;
            float y;
            try { y = chartScale.GetYByValue(price); } catch { return; }
            if (float.IsNaN(y) || float.IsInfinity(y)) return;
            if (y < ChartPanel.Y - 2f || y > ChartPanel.Y + ChartPanel.H + 2f) return;   // off-panel → skip

            bool hover    = _hoverLine == lineId || (_dragging && _dragLine == lineId);
            bool attached = !string.IsNullOrEmpty(attachName);
            float lineA   = hover ? 0.95f : (attached ? 0.8f : 0.55f);
            float lineW   = hover ? 2.2f  : (attached ? 1.7f : 1.3f);
            // v0.2.5: ENTRY (lineId 0) shows the PILL only — NT draws its own entry line, so ours is redundant.
            if (lineId != 0)
                rt.DrawLine(new SharpDX.Vector2(lineX0, y), new SharpDX.Vector2(lineX1, y),
                    B(new SharpDX.Color4(col.Red, col.Green, col.Blue, lineA)), lineW);

            // label chip (measured width → never truncates); sits ABOVE the line to clear NT's own on-line label.
            string txt = tag + "   " + detail + (attached && !(_dragging && _dragLine == lineId) ? "   -> " + attachName : "");
            float tw = MeasureText(dwf, txt, 9.5f, true);
            float w = tw + 16f, h = 17f;
            float lx = rightSide ? (lineX1 - 6f - w) : (lineX0 + 6f);
            float ly = y - h - 2f;
            if (ly < ChartPanel.Y + 1f) ly = y + 2f;
            var chip = new SharpDX.Direct2D1.RoundedRectangle { Rect = new SharpDX.RectangleF(lx, ly, w, h), RadiusX = 4f, RadiusY = 4f };
            rt.FillRoundedRectangle(chip, B(SentinelSkin.Alpha(SentinelSkin.CVoid, 0.9f)));   // theme-aware chip bg
            rt.DrawRoundedRectangle(chip, B(new SharpDX.Color4(col.Red, col.Green, col.Blue, hover ? 0.95f : 0.6f)), hover ? 1.5f : 1f);
            DT(rt, dwf, txt, lx + 8f, ly, tw + 4f, h, B(col), 9.5f, true, SharpDX.DirectWrite.TextAlignment.Leading);
        }

        // exact rendered text width (DirectWrite) so chips size to their content and never clip
        private float MeasureText(SharpDX.DirectWrite.Factory dwf, string txt, float size, bool semibold)
        {
            if (string.IsNullOrEmpty(txt)) return 0f;
            try
            {
                using (var fmt = new SharpDX.DirectWrite.TextFormat(dwf, "Segoe UI",
                    semibold ? SharpDX.DirectWrite.FontWeight.SemiBold : SharpDX.DirectWrite.FontWeight.Normal,
                    SharpDX.DirectWrite.FontStyle.Normal, size))
                using (var layout = new SharpDX.DirectWrite.TextLayout(dwf, txt, fmt, 4000f, size + 6f))
                    return layout.Metrics.WidthIncludingTrailingWhitespace;
            }
            catch { return txt.Length * 7.4f; }   // fallback estimate (generous)
        }

        // draw text helper (per-frame TextFormat, pooled via using)
        private void DT(SharpDX.Direct2D1.RenderTarget rt, SharpDX.DirectWrite.Factory dwf, string text,
            float x, float y, float w, float h, SharpDX.Direct2D1.Brush brush, float size, bool semibold, SharpDX.DirectWrite.TextAlignment align)
        {
            if (string.IsNullOrEmpty(text)) return;
            using (var fmt = new SharpDX.DirectWrite.TextFormat(dwf, "Segoe UI",
                semibold ? SharpDX.DirectWrite.FontWeight.SemiBold : SharpDX.DirectWrite.FontWeight.Normal,
                SharpDX.DirectWrite.FontStyle.Normal, size))
            {
                fmt.TextAlignment = align;
                fmt.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center;
                rt.DrawText(text, fmt, new SharpDX.RectangleF(x, y, w, h), brush);
            }
        }

        private static SharpDX.Color4 RC(int r, int g, int b) => new SharpDX.Color4(r / 255f, g / 255f, b / 255f, 1f);

        //  WPF factory helpers 
        private TextBlock Tx(string t, double size, Color c, bool bold = false)
            => new TextBlock { Text = t, FontSize = size, Foreground = new SolidColorBrush(c),
                FontFamily = new FontFamily("Consolas"), FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center };

        private TextBlock SecLabel(string t)
        {
            var tb = Tx(t, 9, C_LABEL, true);
            tb.Margin = new Thickness(8, 6, 8, 0);
            return tb;
        }

        private Border HRule() => new Border { BorderBrush = new SolidColorBrush(C_BORDER), BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(6, 2, 6, 2) };

        private Button StepBtn(string t, Action a)
        {
            var b = new Button { Content = t, Width = 26, Height = 24, FontSize = 13, FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(C_DIM), Foreground = new SolidColorBrush(C_LABEL),
                BorderBrush = new SolidColorBrush(C_BORDER), BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
            b.Click += (s, e) => a();
            return b;
        }

        private Button PresetBtn(string t, Action a)
        {
            var b = new Button { Content = t, MinWidth = 24, Height = 24, FontSize = 10, Margin = new Thickness(3, 0, 0, 0),
                FontFamily = new FontFamily("Consolas"), Background = new SolidColorBrush(C_DIM), Foreground = new SolidColorBrush(C_MUTED),
                BorderBrush = new SolidColorBrush(C_BORDER), BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
            b.Click += (s, e) => a();
            return b;
        }

        private Button NudgeBtn(string t, Action a)
        {
            var b = new Button { Content = t, MinWidth = 22, Height = 20, FontSize = 10, Margin = new Thickness(1, 0, 0, 0),
                FontFamily = new FontFamily("Consolas"), Background = new SolidColorBrush(C_DIM), Foreground = new SolidColorBrush(C_LABEL),
                BorderBrush = new SolidColorBrush(C_BORDER), BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
            b.Click += (s, e) => a();
            return b;
        }

        private Button BigBtn(string t, Color accent)
            => new Button { Content = t, Height = 34, FontSize = 14, FontWeight = FontWeights.Bold, FontFamily = new FontFamily("Consolas"),
                Background = new SolidColorBrush(Tint(accent, 0.30)), Foreground = new SolidColorBrush(accent),
                BorderBrush = new SolidColorBrush(accent), BorderThickness = new Thickness(1.5), Cursor = Cursors.Hand };

        private Button SmallBtn(string t, Color accent)
            => new Button { Content = t, Height = 24, FontSize = 11, FontWeight = FontWeights.Bold, FontFamily = new FontFamily("Consolas"),
                Background = new SolidColorBrush(Tint(accent, 0.16)), Foreground = new SolidColorBrush(accent),
                BorderBrush = new SolidColorBrush(accent), BorderThickness = new Thickness(1), Cursor = Cursors.Hand };

        private Color Tint(Color accent, double k) => Blend(C_BG, accent, k);
        private static Color Blend(Color a, Color b, double t)
        {
            if (t < 0) t = 0; else if (t > 1) t = 1;
            return Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
        }

        // 
        // TRADE MANAGEMENT UI (v0.2.0)  Bracket/Stop  Breakeven  Trailing  Scale
        // 
        private void BuildTradeManagement()
        {
            hudStack.Children.Add(HRule());

            // BRACKET / STOP
            var brk = Section("BRACKET / STOP");
            tbStop   = NumBox(pStop.ToString());   tbStop.LostFocus   += (s, e) => { pStop = ParseI(tbStop, pStop); RecomputeRiskSize(); };
            tbTarget = NumBox(pTarget.ToString());  tbTarget.LostFocus += (s, e) => pTarget = ParseI(tbTarget, pTarget);
            brk.Children.Add(LabeledRow("Stop tk", tbStop, "Target tk", tbTarget));
            var attach  = SmallBtn("Attach Bracket", C_ACCENT); attach.Click  += (s, e) => BracketOrder(pStop, pTarget);
            var addStop = SmallBtn("Add Stop",       C_AMBER);  addStop.Click += (s, e) => AddStopOrder(pStop);
            brk.Children.Add(Row2(attach, addStop));
            btnAutoBracket = WideToggle("Auto on entry: OFF"); btnAutoBracket.Click += (s, e) => CycleAutoBracket();
            brk.Children.Add(btnAutoBracket);

            // BREAKEVEN
            var be = Section("BREAKEVEN");
            tbBETrig = NumBox(pBETrig.ToString()); tbBETrig.LostFocus += (s, e) => pBETrig = ParseI(tbBETrig, pBETrig);
            tbBEOff  = NumBox(pBEOff.ToString());  tbBEOff.LostFocus  += (s, e) => pBEOff  = ParseI(tbBEOff,  pBEOff);
            be.Children.Add(LabeledRow("Trigger tk", tbBETrig, "Offset tk", tbBEOff));
            var toBE = SmallBtn("-> Breakeven", C_GREEN); toBE.Click += (s, e) => StopsToBreakeven(pBEOff);
            btnAutoBE = WideToggle("Auto BE: OFF"); btnAutoBE.Click += (s, e) => { autoBE = !autoBE; StyleToggle(btnAutoBE, autoBE, "Auto BE"); };
            be.Children.Add(Row2(toBE, btnAutoBE));

            // TRAILING  all 7 modes
            var tr = Section("TRAILING");
            btnTrailTicks = ModePill("Trail",  DeckTrailMode.TrailTicks);
            btnBEPlus     = ModePill("BE+",    DeckTrailMode.BreakevenPlus);
            btnBarLH      = ModePill("BarHL",  DeckTrailMode.BarLowHigh);
            btnNBarLH     = ModePill("NBar",   DeckTrailMode.NBarLowHigh);
            btnATRb       = ModePill("ATR",    DeckTrailMode.TrailATR);
            btnTMagic     = ModePill("Magic",  DeckTrailMode.TrendMagic);
            btnHalfBE     = ModePill("HalfBE", DeckTrailMode.HalfPlusBE);
            tr.Children.Add(Grid4(btnTrailTicks, btnBEPlus, btnBarLH, btnNBarLH));
            tr.Children.Add(Grid4(btnATRb, btnTMagic, btnHalfBE, null));
            tbTrailTk   = NumBox(pTrailTk.ToString());   tbTrailTk.LostFocus   += (s, e) => pTrailTk   = ParseI(tbTrailTk,   pTrailTk);
            tbTrailBars = NumBox(pTrailBars.ToString()); tbTrailBars.LostFocus += (s, e) => pTrailBars = ParseI(tbTrailBars, pTrailBars);
            tbAtrPer    = NumBox(pAtrPer.ToString());    tbAtrPer.LostFocus    += (s, e) => pAtrPer    = ParseI(tbAtrPer,    pAtrPer);
            tbAtrMult   = NumBox(pAtrMult.ToString("0.#")); tbAtrMult.LostFocus += (s, e) => pAtrMult  = ParseD(tbAtrMult,  pAtrMult);
            tbHalfTrig  = NumBox(pHalfTrig.ToString());  tbHalfTrig.LostFocus  += (s, e) => pHalfTrig  = ParseI(tbHalfTrig,  pHalfTrig);
            tbHalfOff   = NumBox(pHalfOff.ToString());   tbHalfOff.LostFocus   += (s, e) => pHalfOff   = ParseI(tbHalfOff,   pHalfOff);
            tr.Children.Add(LabeledRow("Trail tk", tbTrailTk, "N bars", tbTrailBars));
            tr.Children.Add(LabeledRow("ATR per", tbAtrPer, "ATR x", tbAtrMult));
            tr.Children.Add(LabeledRow("Half trig", tbHalfTrig, "Half off", tbHalfOff));
            btnAutoTrail = WideToggle("Auto-trail on entry: OFF"); btnAutoTrail.Click += (s, e) => { autoTrail = !autoTrail; StyleToggle(btnAutoTrail, autoTrail, "Auto-trail on entry"); };
            tr.Children.Add(btnAutoTrail);

            // SCALE
            var sc = Section("SCALE");
            var half = SmallBtn("Close Half", C_LABEL); half.Click += (s, e) => CloseHalf();
            half.Margin = new Thickness(6, 2, 6, 2);
            sc.Children.Add(half);

            // v0.2.3: RECORD — passive tick-path capture of MANUAL trades (feeds the excursion sandbox). Default OFF.
            var rectn = Section("RECORD");
            btnLogTape = WideToggle("Log Tick Path: OFF");
            btnLogTape.Click += (s, e) => { LogTickPath = !LogTickPath; StyleTapeBtn(); };
            rectn.Children.Add(btnLogTape);
            StyleTapeBtn();

            RefreshTrailButtons();
        }

        // v0.2.2: SIGNAL ARM — arm/auto-fire Long/Short off any loaded indicator's plot (no hardcoded signals)
        private void BuildSignalSection()
        {
            var sg = Section("SIGNAL ARM", true);   // top of panel, COLLAPSED by default (expand via header)

            // v0.2.2: PRESET library (signal + entry settings) — pick to load; name + Save to store; Delete to remove.
            sg.Children.Add(SecLabel("PRESETS"));
            cbPreset = new ComboBox { Height = 22, FontSize = 10, FontFamily = new FontFamily("Consolas"), Margin = new Thickness(6, 2, 6, 2),
                Background = new SolidColorBrush(C_DIM), Foreground = new SolidColorBrush(C_TEXT), BorderBrush = new SolidColorBrush(C_BORDER), MaxDropDownHeight = 240 };
            cbPreset.SelectionChanged += (s, e) => OnPresetSelected();
            sg.Children.Add(cbPreset);
            tbPresetName = new TextBox { Text = "", FontFamily = new FontFamily("Consolas"), FontSize = 10, Margin = new Thickness(0),
                Background = new SolidColorBrush(C_DIM), Foreground = new SolidColorBrush(C_TEXT), BorderBrush = new SolidColorBrush(C_BORDER), VerticalContentAlignment = VerticalAlignment.Center };
            btnPresetSave = SmallBtn("Save", C_ACCENT); btnPresetSave.Click += (s, e) => SavePreset();
            sg.Children.Add(ComboRow("name", tbPresetName));
            btnPresetDelete = SmallBtn("Delete preset", C_AMBER); btnPresetDelete.Click += (s, e) => DeletePreset();
            sg.Children.Add(Row2(btnPresetSave, btnPresetDelete));
            RefreshPresetCombo();

            btnSigEnable = WideToggle("Signal watch: OFF");
            btnSigEnable.Click += (s, e) =>
            {
                sigEnabled = !sigEnabled;
                if (sigEnabled) { RescanSignalSources(); ReresolveSigRefs(); PopulateSourceCombos();
                    try { SentinelCore.Log("Deck:sig", "watch ON srcA=" + sigSrcA + " indA=" + (_sigIndA != null) + " plotA=" + _sigPlotA + " rule=" + sigRule + " cadence=" + sigCadence + " autofire=" + sigAutoFire); } catch { } }
                StyleToggle(btnSigEnable, sigEnabled, "Signal watch");
                _sigLastDir = 0; _sigPrimed = false; ClearArmVisual();
                UpdateSigStatusText(sigEnabled ? "watching" : "off");
            };
            sg.Children.Add(btnSigEnable);

            // v0.2.2: DROPDOWN pickers for source A + B (replaced the truncating cycle buttons). Rescan on open.
            cbSigSrcA = MakeSourceCombo(true);
            cbSigSrcB = MakeSourceCombo(false);
            sg.Children.Add(ComboRow("A", cbSigSrcA));
            sg.Children.Add(ComboRow("B", cbSigSrcB));

            btnSigRule = SmallBtn("Rule: Sign",      C_LABEL);  btnSigRule.Click += (s, e) => CycleRule();
            btnSigInvert = WideToggle("Invert: OFF"); btnSigInvert.Click += (s, e) => { sigInvert = !sigInvert; StyleToggle(btnSigInvert, sigInvert, "Invert"); };
            sg.Children.Add(Row2(btnSigRule, btnSigInvert));

            btnSigMode = WideToggle("Mode: ARM (confirm)"); btnSigMode.Click += (s, e) => { sigAutoFire = !sigAutoFire; StyleSigMode(); };
            btnSigCadence = WideToggle("Eval: BAR CLOSE"); btnSigCadence.Click += (s, e) => { sigCadence = sigCadence == SignalCadence.BarClose ? SignalCadence.Tick : SignalCadence.BarClose; StyleSigCadence(); };
            sg.Children.Add(Row2(btnSigMode, btnSigCadence));

            btnSigRescan = SmallBtn("Rescan sources", C_ACCENT);
            btnSigRescan.Click += (s, e) => { RescanSignalSources(); PopulateSourceCombos(); ReresolveSigRefs(); UpdateSigStatusText(_sigSources.Count + " plot(s) found"); };
            sg.Children.Add(btnSigRescan);

            sigStatus = Tx("idle", 9, C_MUTED); sigStatus.Margin = new Thickness(6, 3, 6, 4); sigStatus.TextWrapping = TextWrapping.Wrap;
            sg.Children.Add(sigStatus);

            RescanSignalSources(); SyncSignalUi();   // reflect any PERSISTED config (rule/mode/cadence/invert + source A/B) into the controls
        }

        // v0.2.2: push the (possibly persisted/loaded) signal config into the panel controls + resolve source refs.
        private void SyncSignalUi()
        {
            if (btnSigRule != null) btnSigRule.Content = "Rule: " + RuleLabel(sigRule);
            if (btnSigMode != null) StyleSigMode();
            if (btnSigCadence != null) StyleSigCadence();
            if (btnSigInvert != null) StyleToggle(btnSigInvert, sigInvert, "Invert");
            ReresolveSigRefs();
            PopulateSourceCombos();   // selects the persisted source A/B if those plots are present on the chart
            UpdateSigStatusText(string.IsNullOrEmpty(sigSrcA) ? "idle — pick source A" : "ready — flip Signal watch ON");
        }

        // ── v0.2.2: preset library (signal + entry settings), stored in-Deck via SignalPresetsBlob ──────────────
        private const char PFS = (char)31;   // unit separator (never appears in keys/names)
        private const char PRS = (char)30;   // record separator
        private static string San(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace(PFS, ' ').Replace(PRS, ' ').Trim();
        private static string NoNull(string s) => s ?? "";
        private static string NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;
        private static int    PI(string s) { return int.TryParse(s, out int v) ? v : 0; }
        private static double PD(string s) { return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double v) ? v : 0; }

        private string EncodePresets()
        {
            string blob = "";
            foreach (var p in _presets)
            {
                if (blob.Length > 0) blob += PRS;
                blob += San(p.Name) + PFS + NoNull(p.SrcA) + PFS + NoNull(p.SrcB) + PFS
                      + p.Rule + PFS + p.Cadence + PFS + (p.Invert ? 1 : 0) + PFS + (p.AutoFire ? 1 : 0) + PFS
                      + p.Threshold.ToString(CultureInfo.InvariantCulture) + PFS
                      + p.Qty + PFS + p.StopTk + PFS + p.TargetTk + PFS + p.AutoBracket;
            }
            return blob;
        }

        private void DecodePresets(string blob)
        {
            _presets.Clear();
            if (string.IsNullOrEmpty(blob)) return;
            try
            {
                foreach (var rec in blob.Split(PRS))
                {
                    var f = rec.Split(PFS);
                    if (f.Length < 12 || string.IsNullOrEmpty(f[0])) continue;
                    _presets.Add(new SigPreset
                    {
                        Name = f[0], SrcA = NullIfEmpty(f[1]), SrcB = NullIfEmpty(f[2]),
                        Rule = PI(f[3]), Cadence = PI(f[4]), Invert = f[5] == "1", AutoFire = f[6] == "1",
                        Threshold = PD(f[7]), Qty = Math.Max(1, PI(f[8])), StopTk = Math.Max(1, PI(f[9])),
                        TargetTk = Math.Max(1, PI(f[10])), AutoBracket = PI(f[11])
                    });
                }
            }
            catch { }
        }

        private SigPreset CaptureCurrent(string name) => new SigPreset
        {
            Name = name, SrcA = sigSrcA, SrcB = sigSrcB, Rule = (int)sigRule, Cadence = (int)sigCadence,
            Invert = sigInvert, AutoFire = sigAutoFire, Threshold = sigThreshold,
            Qty = deckQty, StopTk = pStop, TargetTk = pTarget, AutoBracket = (int)autoBracket
        };

        // apply a preset to the LIVE config + refresh every affected control. Forces watch OFF (safe: you re-arm).
        private void ApplyPreset(SigPreset p)
        {
            if (p == null) return;
            sigSrcA = p.SrcA; sigSrcB = p.SrcB;
            sigRule = (SignalRule)p.Rule; sigCadence = (SignalCadence)p.Cadence;
            sigInvert = p.Invert; sigAutoFire = p.AutoFire; sigThreshold = p.Threshold;
            SetQty(p.Qty);
            pStop = Math.Max(1, p.StopTk);   if (tbStop   != null) tbStop.Text   = pStop.ToString();
            pTarget = Math.Max(1, p.TargetTk); if (tbTarget != null) tbTarget.Text = pTarget.ToString();
            autoBracket = (AutoBracketMode)p.AutoBracket; RefreshAutoBracketBtn();
            sigEnabled = false; if (btnSigEnable != null) StyleToggle(btnSigEnable, false, "Signal watch");   // load never auto-arms
            _sigLastDir = 0; _sigPrimed = false; ClearArmVisualCore();
            SyncSignalUi();
            Status("preset loaded: " + p.Name);
        }

        private void OnPresetSelected()
        {
            if (_presetUpdating) return;
            string name = (cbPreset?.SelectedItem as ComboBoxItem)?.Tag as string;
            if (name == null) return;
            var p = _presets.FirstOrDefault(x => x.Name == name);
            if (p != null) { ApplyPreset(p); if (tbPresetName != null) tbPresetName.Text = p.Name; }
        }

        private void SavePreset()
        {
            string name = San(tbPresetName != null ? tbPresetName.Text : "");
            if (string.IsNullOrEmpty(name)) name = "Preset " + (_presets.Count + 1);
            var existing = _presets.FirstOrDefault(x => x.Name == name);
            var cap = CaptureCurrent(name);
            if (existing != null) _presets[_presets.IndexOf(existing)] = cap; else _presets.Add(cap);
            RefreshPresetCombo(); SelectPresetInCombo(name);
            if (tbPresetName != null) tbPresetName.Text = name;
            Status("preset saved: " + name);
        }

        private void DeletePreset()
        {
            string name = (cbPreset?.SelectedItem as ComboBoxItem)?.Tag as string;
            if (name == null) name = San(tbPresetName != null ? tbPresetName.Text : "");
            var p = _presets.FirstOrDefault(x => x.Name == name);
            if (p != null) { _presets.Remove(p); RefreshPresetCombo(); Status("preset deleted: " + name); }
            else Status("no preset selected to delete");
        }

        private void RefreshPresetCombo()
        {
            if (cbPreset == null) return;
            _presetUpdating = true;
            try
            {
                cbPreset.Items.Clear();
                cbPreset.Items.Add(new ComboBoxItem { Content = "— load preset —", Tag = null, FontFamily = new FontFamily("Consolas"), FontSize = 10, Background = new SolidColorBrush(C_DIM), Foreground = new SolidColorBrush(C_MUTED) });
                foreach (var p in _presets)
                    cbPreset.Items.Add(new ComboBoxItem { Content = p.Name, Tag = p.Name, FontFamily = new FontFamily("Consolas"), FontSize = 10, Background = new SolidColorBrush(C_DIM), Foreground = new SolidColorBrush(C_TEXT) });
                cbPreset.SelectedIndex = 0;
            }
            catch { }
            finally { _presetUpdating = false; }
        }

        private void SelectPresetInCombo(string name)
        {
            if (cbPreset == null) return;
            _presetUpdating = true;
            try { foreach (ComboBoxItem it in cbPreset.Items) if ((it.Tag as string) == name) { cbPreset.SelectedItem = it; break; } }
            catch { }
            finally { _presetUpdating = false; }
        }

        private StackPanel Section(string title, bool collapsed = false)
        {
            var content = new StackPanel { Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible };   // v0.2.1: sections EXPANDED by default (v0.2.2: opt-in collapsed)
            var hdr = new Grid { Margin = new Thickness(6, 5, 6, 0), Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)), Cursor = Cursors.Hand };
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hdr.ColumnDefinitions.Add(new ColumnDefinition());
            var chev = Tx(collapsed ? "+" : "-", 9, collapsed ? C_MUTED : C_ACCENT); chev.Margin = new Thickness(0, 0, 6, 0);   // "-" open (cyan) / "+" collapsed (muted)
            var lbl = Tx(title, 9, C_LABEL, true);
            var rule = new Border { BorderBrush = new SolidColorBrush(C_BORDER), BorderThickness = new Thickness(0, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            Grid.SetColumn(chev, 0); Grid.SetColumn(lbl, 1); Grid.SetColumn(rule, 2);
            hdr.Children.Add(chev); hdr.Children.Add(lbl); hdr.Children.Add(rule);
            hdr.MouseLeftButtonUp += (s, e) =>
            {
                bool open = content.Visibility != Visibility.Visible;
                content.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
                chev.Text = open ? "-" : "+";   // minus when open, plus when collapsed (ASCII-safe)
                chev.Foreground = new SolidColorBrush(open ? C_ACCENT : C_MUTED);
            };
            hudStack.Children.Add(hdr);
            hudStack.Children.Add(content);
            return content;
        }

        private TextBox NumBox(string initial)
            => new TextBox { Text = initial, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = new SolidColorBrush(C_DIM), Foreground = new SolidColorBrush(C_TEXT), BorderBrush = new SolidColorBrush(C_BORDER),
                FontFamily = new FontFamily("Consolas"), FontSize = 11, Margin = new Thickness(2, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center };

        private FrameworkElement LabeledRow(string l1, TextBox t1, string l2, TextBox t2)
        {
            var g = new Grid { Margin = new Thickness(6, 2, 6, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            var a = Tx(l1, 9, C_MUTED); a.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(a, 0); g.Children.Add(a);
            Grid.SetColumn(t1, 1); g.Children.Add(t1);
            var b = Tx(l2, 9, C_MUTED); b.VerticalAlignment = VerticalAlignment.Center; b.Margin = new Thickness(8, 0, 0, 0); Grid.SetColumn(b, 2); g.Children.Add(b);
            Grid.SetColumn(t2, 3); g.Children.Add(t2);
            return g;
        }

        private Grid Row2(UIElement a, UIElement b)
        {
            var g = new Grid { Margin = new Thickness(6, 3, 6, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition());
            if (a is FrameworkElement fa) fa.Margin = new Thickness(0, 0, 3, 0);
            if (b is FrameworkElement fb) fb.Margin = new Thickness(3, 0, 0, 0);
            Grid.SetColumn(a, 0); Grid.SetColumn(b, 1); g.Children.Add(a); g.Children.Add(b);
            return g;
        }

        private Grid Grid4(UIElement a, UIElement b, UIElement c, UIElement d)
        {
            var g = new Grid { Margin = new Thickness(6, 3, 6, 0) };
            for (int i = 0; i < 4; i++) g.ColumnDefinitions.Add(new ColumnDefinition());
            var arr = new[] { a, b, c, d };
            for (int i = 0; i < 4; i++) { if (arr[i] == null) continue; Grid.SetColumn(arr[i], i); g.Children.Add(arr[i]); }
            return g;
        }

        private Button ModePill(string label, DeckTrailMode mode)
        {
            var b = new Button { Content = label, Height = 22, FontSize = 9.5, FontWeight = FontWeights.Bold, FontFamily = new FontFamily("Consolas"),
                Style = null, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), Margin = new Thickness(2, 0, 2, 0),
                Background = new SolidColorBrush(C_DIM), Foreground = new SolidColorBrush(C_MUTED),
                BorderBrush = new SolidColorBrush(C_BORDER), BorderThickness = new Thickness(1), Cursor = Cursors.Hand,
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
            b.Click += (s, e) => ToggleTrail(mode);
            return b;
        }

        private void ToggleTrail(DeckTrailMode mode)
        {
            if (activeTrail == mode) activeTrail = DeckTrailMode.None;
            else { activeTrail = mode; beTriggered = false; halfTriggered = false; trailStopLevel = double.MinValue; }
            RefreshTrailButtons();
            Status(activeTrail == DeckTrailMode.None ? "trailing off" : "trailing: " + activeTrail);
        }

        private void RefreshTrailButtons()
        {
            SetTrail(btnTrailTicks, activeTrail == DeckTrailMode.TrailTicks);
            SetTrail(btnBEPlus,     activeTrail == DeckTrailMode.BreakevenPlus);
            SetTrail(btnBarLH,      activeTrail == DeckTrailMode.BarLowHigh);
            SetTrail(btnNBarLH,     activeTrail == DeckTrailMode.NBarLowHigh);
            SetTrail(btnATRb,       activeTrail == DeckTrailMode.TrailATR);
            SetTrail(btnTMagic,     activeTrail == DeckTrailMode.TrendMagic);
            SetTrail(btnHalfBE,     activeTrail == DeckTrailMode.HalfPlusBE);
        }

        private void SetTrail(Button b, bool on)
        {
            if (b == null) return;
            b.Background      = new SolidColorBrush(on ? Tint(C_ACCENT, 0.22) : C_DIM);
            b.Foreground      = new SolidColorBrush(on ? C_ACCENT : C_MUTED);
            b.BorderBrush     = new SolidColorBrush(on ? C_ACCENT : C_BORDER);
            b.BorderThickness = new Thickness(on ? 1.5 : 1);
        }

        private Button WideToggle(string text)
            => new Button { Content = text, Height = 24, FontSize = 10, FontWeight = FontWeights.Bold, FontFamily = new FontFamily("Consolas"),
                Margin = new Thickness(6, 3, 6, 2), Background = new SolidColorBrush(C_DIM), Foreground = new SolidColorBrush(C_MUTED),
                BorderBrush = new SolidColorBrush(C_BORDER), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, HorizontalContentAlignment = HorizontalAlignment.Center };

        private void StyleToggle(Button b, bool on, string label)
        {
            if (b == null) return;
            b.Content    = label + ": " + (on ? "ON" : "OFF");
            b.Background  = new SolidColorBrush(on ? Tint(C_ACCENT, 0.22) : C_DIM);
            b.Foreground  = new SolidColorBrush(on ? C_ACCENT : C_MUTED);
            b.BorderBrush  = new SolidColorBrush(on ? C_ACCENT : C_BORDER);
        }

        // ── v0.2.3: TAPE — passive tick-path capture of manual trades (never touches the order path) ──
        private void StyleTapeBtn()
        {
            if (btnLogTape == null) return;
            string sub = _tapeActive ? " · capturing" : (LogTickPath ? " · idle" : "");
            btnLogTape.Content    = "Log Tick Path: " + (LogTickPath ? "ON" : "OFF") + sub;
            btnLogTape.Background  = new SolidColorBrush(LogTickPath ? Tint(C_ACCENT, 0.22) : C_DIM);
            btnLogTape.Foreground  = new SolidColorBrush(LogTickPath ? C_ACCENT : C_MUTED);
            btnLogTape.BorderBrush  = new SolidColorBrush(LogTickPath ? C_ACCENT : C_BORDER);
        }

        // TapeBegin/End run on the DATA thread; WPF throws if you repaint a control off the UI thread → marshal it.
        private void UiRefreshTape() { try { ChartControl?.Dispatcher?.InvokeAsync(StyleTapeBtn); } catch { } }

        // Called each tick from OnBarUpdate (Realtime only). Begin whenever ON+in-position (no gap; partial if armed
        // mid-trade), append every tick, end on →Flat, and SPLIT on a reversal so each leg is its own path.
        private void TapeOnTick()
        {
            if (State != State.Realtime) { _tapePrevPos = cardPos; return; }
            if (!_tapeActive && LogTickPath && cardPos != MarketPosition.Flat)
                TapeBegin(_tapePrevPos == MarketPosition.Flat);   // full path on a clean Flat→entry; partial if armed mid-trade
            if (_tapeActive)
            {
                int curDir = cardPos == MarketPosition.Long ? 1 : cardPos == MarketPosition.Short ? -1 : 0;
                if      (curDir == 0)         TapeEnd();                        // returned to flat
                else if (curDir != _tapeDir) { TapeEnd(); TapeBegin(false); }   // reversal → close this leg, open the next
                // else: same direction — path points are appended from OnMarketData (raw last-trade), not here (v0.2.4)
            }
            _tapePrevPos = cardPos;
        }

        private void TapeBegin(bool fullEntry)
        {
            _tapeActive    = true;
            _tapePartial   = !fullEntry;
            _tapeEntryTime = Core.Globals.Now;
            _tapeEntryPx   = cardAvg > 0 ? cardAvg : (_lastTradePx > 0 ? _lastTradePx : lastClose);   // v0.2.4: raw last-trade fallback
            _tapeDir       = cardPos == MarketPosition.Long ? 1 : -1;
            string inst    = cachedInstrument != null && cachedInstrument.MasterInstrument != null ? cachedInstrument.MasterInstrument.Name : "X";
            _tapeId        = _tapeEntryTime.ToString("yyyyMMdd'T'HHmmssfff", CultureInfo.InvariantCulture) + "_" + inst + "_" + (_tapeDir > 0 ? "L" : "S");
            _tapeBuf       = new System.Text.StringBuilder(8192);
            _tapeMaxFav = 0; _tapeMaxAdv = 0; _tapeTicks = 0;
            Status("tick path: capturing " + (_tapeDir > 0 ? "long" : "short") + (_tapePartial ? " (partial)" : ""));
            // AUDIT TRAIL — every capture is logged so you never have to trust the button. Grep sentinel.log for Deck:tape.
            try { NinjaTrader.NinjaScript.AddOns.Sentinel.SentinelCore.Log("Deck:tape", "capturing " + (_tapeDir > 0 ? "long" : "short") + " @ " + _tapeEntryPx.ToString("0.#####", CultureInfo.InvariantCulture) + " id=" + _tapeId + (_tapePartial ? " ⚠PARTIAL (armed mid-trade — entry not seen)" : "")); } catch { }
            UiRefreshTape();
        }

        // v0.2.4: px is the RAW last-trade price (from OnMarketData), not the synthetic brick Close[0].
        private void TapeAppend(double px)
        {
            if (_tapeBuf == null || _tapeBuf.Length > 6000000) return;   // ~6MB memory cap
            double tick = cachedInstrument != null && cachedInstrument.MasterInstrument != null ? cachedInstrument.MasterInstrument.TickSize : 0;
            double fav  = _tapeDir > 0 ? (px - _tapeEntryPx) : (_tapeEntryPx - px);
            double favT = tick > 0 ? fav / tick : 0;
            if (favT  > _tapeMaxFav) _tapeMaxFav = favT;
            if (-favT > _tapeMaxAdv) _tapeMaxAdv = -favT;
            long ms = (long)(Core.Globals.Now - _tapeEntryTime).TotalMilliseconds;
            _tapeBuf.Append("{\"ms\":").Append(ms).Append(",\"px\":").Append(px.ToString("0.#####", CultureInfo.InvariantCulture)).Append("}\n");
            _tapeTicks++;
        }

        private void TapeEnd()
        {
            _tapeActive = false;
            try
            {
                double exitPx = _lastTradePx > 0 ? _lastTradePx : lastClose;   // v0.2.4: raw last-trade, not brick close
                string dir    = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NinjaTrader 8", "Sentinel", "Excursions", "ticks");
                System.IO.Directory.CreateDirectory(dir);
                string bartag = ""; try { bartag = NinjaTrader.NinjaScript.AddOns.Sentinel.SentinelCore.BarTag(BarsPeriod); } catch { }
                string inst   = cachedInstrument != null && cachedInstrument.MasterInstrument != null ? cachedInstrument.MasterInstrument.Name : "X";
                var sb = new System.Text.StringBuilder();
                sb.Append("{\"schema\":\"tick.2\",\"kind\":\"manual_tickpath\",\"src\":\"last\"")
                  .Append(",\"tradeId\":\"").Append(_tapeId).Append('"')
                  .Append(",\"inst\":\"").Append(inst).Append('"')
                  .Append(",\"bartype\":\"").Append(bartag).Append('"')
                  .Append(",\"account\":\"").Append(cardAcct ?? "").Append('"')
                  .Append(",\"dir\":").Append(_tapeDir)
                  .Append(",\"entryTime\":\"").Append(_tapeEntryTime.ToString("o", CultureInfo.InvariantCulture)).Append('"')
                  .Append(",\"entryPx\":").Append(_tapeEntryPx.ToString("0.#####", CultureInfo.InvariantCulture))
                  .Append(",\"exitTime\":\"").Append(Core.Globals.Now.ToString("o", CultureInfo.InvariantCulture)).Append('"')
                  .Append(",\"exitPx\":").Append(exitPx.ToString("0.#####", CultureInfo.InvariantCulture))
                  .Append(",\"maxFavTicks\":").Append(_tapeMaxFav.ToString("0.##", CultureInfo.InvariantCulture))
                  .Append(",\"maxAdvTicks\":").Append(_tapeMaxAdv.ToString("0.##", CultureInfo.InvariantCulture))
                  .Append(",\"partial\":").Append(_tapePartial ? "true" : "false")
                  .Append(",\"ticks\":").Append(_tapeTicks)
                  .Append("}\n");
                if (_tapeBuf != null) sb.Append(_tapeBuf);
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, _tapeId + ".jsonl"), sb.ToString());
                Status("tick path saved: " + _tapeId + " (" + _tapeTicks + "t)");
                try { NinjaTrader.NinjaScript.AddOns.Sentinel.SentinelCore.Log("Deck:tape", "saved " + _tapeId + " · " + _tapeTicks + " ticks · MFE " + _tapeMaxFav.ToString("0.#", CultureInfo.InvariantCulture) + "t MAE " + _tapeMaxAdv.ToString("0.#", CultureInfo.InvariantCulture) + "t" + (_tapePartial ? " ⚠PARTIAL" : "")); } catch { }
            }
            catch (Exception ex) { Status("tick save failed: " + ex.Message); try { NinjaTrader.NinjaScript.AddOns.Sentinel.SentinelCore.Log("Deck:tape", "SAVE FAILED " + _tapeId + ": " + ex.Message); } catch { } }
            _tapeBuf = null; _tapeTicks = 0;
            UiRefreshTape();
        }

        private void CycleAutoBracket()
        {
            autoBracket = (AutoBracketMode)(((int)autoBracket + 1) % 3);
            RefreshAutoBracketBtn();
        }

        private void RefreshAutoBracketBtn()
        {
            if (btnAutoBracket == null) return;
            bool on = autoBracket != AutoBracketMode.Off;
            string t = autoBracket == AutoBracketMode.Off ? "OFF" : autoBracket == AutoBracketMode.StopOnly ? "STOP" : "BRACKET";
            btnAutoBracket.Content    = "Auto on entry: " + t;
            btnAutoBracket.Background  = new SolidColorBrush(on ? Tint(C_ACCENT, 0.22) : C_DIM);
            btnAutoBracket.Foreground  = new SolidColorBrush(on ? C_ACCENT : C_MUTED);
            btnAutoBracket.BorderBrush  = new SolidColorBrush(on ? C_ACCENT : C_BORDER);
        }

        private int ParseI(TextBox tb, int fb) { return (tb != null && int.TryParse(tb.Text, out int v)) ? v : fb; }
        private double ParseD(TextBox tb, double fb) { return (tb != null && double.TryParse(tb.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double v)) ? v : fb; }

        // 
        // TRADE MANAGEMENT ENGINE (v0.2.0)  account-level unmanaged, data thread
        // Ported from GTrader21v_0_1_6Panel (no strategy-owned-order guards needed).
        // 
        private void BracketOrder(int stopTicks, int profitTicks)
        {
            var acct = cachedAccount ?? ResolveAccount(); if (acct == null) { Status("no account"); return; }
            var instr = cachedInstrument ?? ResolveInstrument(); if (instr == null) return;
            var pos = acct.Positions.FirstOrDefault(p => p.Instrument != null && p.Instrument.FullName == instr.FullName && p.Quantity != 0);
            if (pos == null) { Status("no position to bracket"); return; }
            if (acct.Orders.Any(o => o.Instrument != null && o.Instrument.FullName == instr.FullName && !string.IsNullOrEmpty(o.Oco)
                && (o.OrderState == OrderState.Accepted || o.OrderState == OrderState.Working))) { Status("bracket already exists"); return; }
            bool isLong = pos.MarketPosition == MarketPosition.Long;
            double bp = pos.AveragePrice, ts = instr.MasterInstrument.TickSize;
            double stopPx = instr.MasterInstrument.RoundToTickSize(isLong ? bp - stopTicks * ts : bp + stopTicks * ts);
            double tgtPx  = instr.MasterInstrument.RoundToTickSize(isLong ? bp + profitTicks * ts : bp - profitTicks * ts);
            var exit = isLong ? OrderAction.Sell : OrderAction.BuyToCover;
            string oco = Guid.NewGuid().ToString();
            try
            {
                acct.Submit(new[]
                {
                    acct.CreateOrder(instr, exit, OrderType.StopMarket, OrderEntry.Manual, TimeInForce.Day, pos.Quantity, 0, stopPx, oco, _tag + "_SL", Core.Globals.MaxDate, null),
                    acct.CreateOrder(instr, exit, OrderType.Limit,      OrderEntry.Manual, TimeInForce.Day, pos.Quantity, tgtPx, 0, oco, _tag + "_TP", Core.Globals.MaxDate, null),
                });
                Status("bracket SL " + stopPx.ToString("0.##") + " / TP " + tgtPx.ToString("0.##"));
            }
            catch (Exception ex) { Status("bracket failed: " + ex.Message); }
        }

        private void AddStopOrder(int stopTicks)
        {
            var acct = cachedAccount ?? ResolveAccount(); if (acct == null) { Status("no account"); return; }
            var instr = cachedInstrument ?? ResolveInstrument(); if (instr == null) return;
            var pos = acct.Positions.FirstOrDefault(p => p.Instrument != null && p.Instrument.FullName == instr.FullName && p.Quantity != 0);
            if (pos == null) { Status("no position"); return; }
            bool isLong = pos.MarketPosition == MarketPosition.Long;
            var stopAction = isLong ? OrderAction.Sell : OrderAction.BuyToCover;
            if (acct.Orders.Any(o => o.Instrument != null && o.Instrument.FullName == instr.FullName
                && (o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit) && IsActiveExitOrder(o) && o.OrderAction == stopAction))
            { Status("stop already exists"); return; }
            double ts = instr.MasterInstrument.TickSize;
            double stopPx = instr.MasterInstrument.RoundToTickSize(isLong ? pos.AveragePrice - stopTicks * ts : pos.AveragePrice + stopTicks * ts);
            try { acct.Submit(new[] { acct.CreateOrder(instr, stopAction, OrderType.StopMarket, OrderEntry.Manual, TimeInForce.Day, pos.Quantity, 0, stopPx, "", _tag + "_AddStop", Core.Globals.MaxDate, null) }); Status("stop @ " + stopPx.ToString("0.##")); }
            catch (Exception ex) { Status("stop failed: " + ex.Message); }
        }

        private void StopsToBreakeven(int offsetTicks)
        {
            var acct = cachedAccount ?? ResolveAccount(); if (acct == null) { Status("no account"); return; }
            var instr = cachedInstrument ?? ResolveInstrument(); if (instr == null) return;
            var pos = acct.Positions.FirstOrDefault(p => p.Instrument != null && p.Instrument.FullName == instr.FullName && p.Quantity != 0);
            if (pos == null) { Status("no position"); return; }
            bool isLong = pos.MarketPosition == MarketPosition.Long;
            double ts = instr.MasterInstrument.TickSize;
            double bePx = instr.MasterInstrument.RoundToTickSize(pos.AveragePrice + (isLong ? offsetTicks : -offsetTicks) * ts);
            var stops = acct.Orders.Where(o => o.Instrument != null && o.Instrument.FullName == instr.FullName
                && (o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit) && IsActiveExitOrder(o)).ToList();
            if (stops.Count == 0) { Status("no stop to move  Add Stop first"); return; }
            foreach (var o in stops) o.StopPriceChanged = bePx;
            try
            {
                acct.Change(stops.ToArray()); beTriggered = true;
                // sync the trail's ratchet floor so an active auto-trail treats BE as the new floor and never loosens it back
                trailStopLevel = (trailStopLevel == double.MinValue) ? bePx : (isLong ? Math.Max(trailStopLevel, bePx) : Math.Min(trailStopLevel, bePx));
                Status("stop -> breakeven " + bePx.ToString("0.##"));
            }
            catch (Exception ex) { Status("BE failed: " + ex.Message); }
        }

        private void CloseHalf()
        {
            var acct = cachedAccount ?? ResolveAccount(); if (acct == null) { Status("no account"); return; }
            var instr = cachedInstrument ?? ResolveInstrument(); if (instr == null) return;
            var pos = acct.Positions.FirstOrDefault(p => p.Instrument != null && p.Instrument.FullName == instr.FullName && p.Quantity != 0);
            if (pos == null) { Status("no position"); return; }
            int half = Math.Max(1, Math.Abs(pos.Quantity) / 2);
            var act = pos.MarketPosition == MarketPosition.Long ? OrderAction.Sell : OrderAction.BuyToCover;
            try { acct.Submit(new[] { acct.CreateOrder(instr, act, OrderType.Market, OrderEntry.Manual, TimeInForce.Day, half, 0, 0, "", _tag + "_Half", Core.Globals.MaxDate, null) }); Status("closing half (" + half + ")"); }
            catch (Exception ex) { Status("half failed: " + ex.Message); }
        }

        // fresh-entry hook: apply auto-bracket/stop + arm auto-trail; run auto-BE while in position
        private void HandleAutoOnEntry()
        {
            var acct = cachedAccount; var instr = cachedInstrument;
            if (acct == null || instr == null) return;
            var pos = acct.Positions.FirstOrDefault(p => p.Instrument != null && p.Instrument.FullName == instr.FullName);
            int qty = (pos != null) ? Math.Abs(pos.Quantity) : 0;
            if (qty == 0) { lastPosQty = 0; ResetTrailStateOnFlat(); return; }

            if (lastPosQty == 0)   // just went from flat  in a position
            {
                if (autoBracket == AutoBracketMode.Bracket) BracketOrder(pStop, pTarget);
                else if (autoBracket == AutoBracketMode.StopOnly) AddStopOrder(pStop);
                if (autoTrail && activeTrail != DeckTrailMode.None) { beTriggered = false; halfTriggered = false; trailStopLevel = double.MinValue; }
            }
            lastPosQty = qty;

            if (autoBE && !beTriggered && pos != null)
            {
                bool isLong = pos.MarketPosition == MarketPosition.Long;
                double price = lastClose > 0 ? lastClose : Close[0];
                double tick = instr.MasterInstrument.TickSize;
                double profit = isLong ? price - pos.AveragePrice : pos.AveragePrice - price;
                if (profit >= pBETrig * tick) StopsToBreakeven(pBEOff);   // sets beTriggered
            }
        }

        private void ExecuteTrail()
        {
            var acct = cachedAccount; var instr = cachedInstrument;
            if (acct == null || instr == null) return;
            var pos = acct.Positions.FirstOrDefault(p => p.Instrument != null && p.Instrument.FullName == instr.FullName);
            if (pos == null || pos.Quantity == 0) { ResetTrailStateOnFlat(); return; }

            bool isLong = pos.MarketPosition == MarketPosition.Long;
            double price = lastClose > 0 ? lastClose : Close[0];
            double tick  = instr.MasterInstrument.TickSize;
            double entry = pos.AveragePrice;
            double newStop = double.MinValue;

            switch (activeTrail)
            {
                case DeckTrailMode.TrailTicks:
                    newStop = isLong ? price - pTrailTk * tick : price + pTrailTk * tick; break;
                case DeckTrailMode.BreakevenPlus:
                    if (beTriggered) return;
                    double beP = isLong ? price - entry : entry - price;
                    if (beP >= pBETrig * tick) { newStop = isLong ? entry + pBEOff * tick : entry - pBEOff * tick; beTriggered = true; }
                    break;
                case DeckTrailMode.BarLowHigh:
                    if (CurrentBar < 1) return;
                    newStop = isLong ? Low[1] : High[1]; break;
                case DeckTrailMode.NBarLowHigh:
                    if (CurrentBar < pTrailBars) return;
                    newStop = isLong ? Enumerable.Range(0, pTrailBars).Min(i => Low[i]) : Enumerable.Range(0, pTrailBars).Max(i => High[i]); break;
                case DeckTrailMode.TrailATR:
                    if (CurrentBar < pAtrPer) return;
                    { double atr = ATR(pAtrPer)[0]; newStop = isLong ? price - pAtrMult * atr : price + pAtrMult * atr; } break;
                case DeckTrailMode.TrendMagic:
                    if (CurrentBar < pAtrPer) return;
                    { double atr = ATR(pAtrPer)[0]; double cci = CCI(pAtrPer)[0];
                      bool frozen = isLong ? cci <= 0 : cci >= 0;   // regime not aligned → freeze stop
                      double cand = isLong ? price - pAtrMult * atr : price + pAtrMult * atr;
                      // v0.2.5 diagnostic (1/bar): shows WHY Magic held — regime freeze vs stop-below-lock (ratchet).
                      if (CurrentBar != _trailLogBar)
                      {
                          _trailLogBar = CurrentBar;
                          try { SentinelCore.Log("Deck:trail", "Magic cci=" + cci.ToString("0") + " atr=" + atr.ToString("0.##")
                                + (frozen ? " FROZEN(regime: cci not aligned)"
                                          : " cand=" + cand.ToString("0.##") + " curStop=" + cardStopPx.ToString("0.##")
                                            + ((isLong ? cand > cardStopPx : cand < cardStopPx) ? " -> MOVE" : " HELD(cand not past lock)"))); } catch { }
                      }
                      if (frozen) return;
                      newStop = cand; } break;
                case DeckTrailMode.HalfPlusBE:
                    { double profit = isLong ? price - entry : entry - price;
                      if (!halfTriggered && profit >= pHalfTrig * tick)
                      {
                          halfTriggered = true;
                          int halfQty = Math.Max(1, pos.Quantity / 2);
                          var closeHalf = isLong ? OrderAction.Sell : OrderAction.BuyToCover;
                          try { acct.Submit(new[] { acct.CreateOrder(instr, closeHalf, OrderType.Market, OrderEntry.Manual, TimeInForce.Day, halfQty, 0, 0, "", _tag + "_HalfBE", Core.Globals.MaxDate, null) }); } catch { }
                          newStop = isLong ? entry + pHalfOff * tick : entry - pHalfOff * tick;
                      }
                      else return; }
                    break;
                default: return;
            }

            if (newStop == double.MinValue) return;
            // RATCHET — a stop only ever moves in the PROTECTIVE direction, never loosens. The floor is the MOST-
            // protective of the trail's own high-water AND the ACTUAL working stop (cardStopPx), so a manual BE (or a
            // hand-drag) that tightened the stop is NEVER walked back out by the trail. THIS is the BE→trail-jump fix:
            // when BE set the stop tighter than the trail's line, the trail now holds instead of loosening it back.
            double curStop = cardStopPx;   // live working stop for this instrument (0 = none)
            double floor   = trailStopLevel;
            if (curStop > 0)
                floor = (floor == double.MinValue) ? curStop : (isLong ? Math.Max(floor, curStop) : Math.Min(floor, curStop));
            bool improved = (floor == double.MinValue) || (isLong ? newStop > floor : newStop < floor);
            if (!improved) return;
            trailStopLevel = newStop;
            MoveOrPlaceStop(acct, instr, pos, newStop, isLong);
        }

        private void MoveOrPlaceStop(Account acct, Instrument instr, Position pos, double stopPrice, bool isLong)
        {
            try
            {
                stopPrice = instr.MasterInstrument.RoundToTickSize(stopPrice);
                if (trailPending) return;
                var stopAction = isLong ? OrderAction.Sell : OrderAction.BuyToCover;
                var stops = acct.Orders.Where(o => o.Instrument != null && o.Instrument.FullName == instr.FullName
                    && (o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit) && IsActiveExitOrder(o) && o.OrderAction == stopAction).ToList();
                if (stops.Count > 1)
                {
                    var keep = isLong ? stops.OrderByDescending(o => o.StopPrice).First() : stops.OrderBy(o => o.StopPrice).First();
                    var extras = stops.Where(o => o != keep).ToArray();
                    if (extras.Length > 0) acct.Cancel(extras);
                    stops = new List<Order> { keep };
                }
                if (stops.Any())
                {
                    foreach (var s in stops) { s.StopPriceChanged = stopPrice; if (s.Quantity != pos.Quantity) s.QuantityChanged = pos.Quantity; }
                    acct.Change(stops.ToArray());
                    trailPending = false;
                }
                else
                {
                    trailPending = true;
                    acct.Submit(new[] { acct.CreateOrder(instr, stopAction, OrderType.StopMarket, OrderEntry.Manual, TimeInForce.Day, pos.Quantity, 0, stopPrice, "", _tag + "_Trail", Core.Globals.MaxDate, null) });
                }
            }
            catch (Exception ex) { Print("[Sentinel Deck] MoveStop: " + ex.Message); }
        }

        private void ResetTrailStateOnFlat()
        {
            trailStopLevel = double.MinValue; beTriggered = false; halfTriggered = false; trailPending = false;
        }

        private static bool IsActiveExitOrder(Order o)
        {
            if (o == null) return false;
            return o.OrderState == OrderState.Working || o.OrderState == OrderState.Accepted
                || o.OrderState == OrderState.Submitted || o.OrderState == OrderState.ChangeSubmitted || o.OrderState == OrderState.PartFilled;
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private Sentinel.SentinelDeck_v0_2_5[] cacheSentinelDeck_v0_2_5;
		public Sentinel.SentinelDeck_v0_2_5 SentinelDeck_v0_2_5(int defaultQty, int tickOffset, bool showIndicatorLabel)
		{
			return SentinelDeck_v0_2_5(Input, defaultQty, tickOffset, showIndicatorLabel);
		}

		public Sentinel.SentinelDeck_v0_2_5 SentinelDeck_v0_2_5(ISeries<double> input, int defaultQty, int tickOffset, bool showIndicatorLabel)
		{
			if (cacheSentinelDeck_v0_2_5 != null)
				for (int idx = 0; idx < cacheSentinelDeck_v0_2_5.Length; idx++)
					if (cacheSentinelDeck_v0_2_5[idx] != null && cacheSentinelDeck_v0_2_5[idx].DefaultQty == defaultQty && cacheSentinelDeck_v0_2_5[idx].TickOffset == tickOffset && cacheSentinelDeck_v0_2_5[idx].ShowIndicatorLabel == showIndicatorLabel && cacheSentinelDeck_v0_2_5[idx].EqualsInput(input))
						return cacheSentinelDeck_v0_2_5[idx];
			return CacheIndicator<Sentinel.SentinelDeck_v0_2_5>(new Sentinel.SentinelDeck_v0_2_5(){ DefaultQty = defaultQty, TickOffset = tickOffset, ShowIndicatorLabel = showIndicatorLabel }, input, ref cacheSentinelDeck_v0_2_5);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.Sentinel.SentinelDeck_v0_2_5 SentinelDeck_v0_2_5(int defaultQty, int tickOffset, bool showIndicatorLabel)
		{
			return indicator.SentinelDeck_v0_2_5(Input, defaultQty, tickOffset, showIndicatorLabel);
		}

		public Indicators.Sentinel.SentinelDeck_v0_2_5 SentinelDeck_v0_2_5(ISeries<double> input , int defaultQty, int tickOffset, bool showIndicatorLabel)
		{
			return indicator.SentinelDeck_v0_2_5(input, defaultQty, tickOffset, showIndicatorLabel);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.Sentinel.SentinelDeck_v0_2_5 SentinelDeck_v0_2_5(int defaultQty, int tickOffset, bool showIndicatorLabel)
		{
			return indicator.SentinelDeck_v0_2_5(Input, defaultQty, tickOffset, showIndicatorLabel);
		}

		public Indicators.Sentinel.SentinelDeck_v0_2_5 SentinelDeck_v0_2_5(ISeries<double> input , int defaultQty, int tickOffset, bool showIndicatorLabel)
		{
			return indicator.SentinelDeck_v0_2_5(input, defaultQty, tickOffset, showIndicatorLabel);
		}
	}
}

#endregion
