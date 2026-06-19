// ============================================================================
// SentinelV1_0
// ============================================================================
// Sentinel is the first product in the Sentinel Suite — a family of
// NinjaTrader 8 trading tools built around intelligent signal arming,
// trade management, risk control, and trade logging.
//
// Sentinel V1.0 is a floating trade panel that detaches from the ChartTrader
// sidebar and lives as a free-standing ToolWindow. It retains the full feature
// set of TrendArchitectMQPanel V1.6.1 and adds:
//   - Floating window architecture (WindowStyle.ToolWindow, owned by chart)
//   - SignalSource enum — configurable signal detection:
//       TrendArchitectMQ  : TrendArchitect MQB/MQS draw objects (default)
//       DrawObjectTag     : any indicator's Draw.X with a configurable tag prefix
//       IndicatorSeries   : any indicator's output series by name + index
//       ManualOnly        : no auto-arm — buttons only
//   - Window position persisted via WindowLeft / WindowTop properties
//   - Window width configurable via PanelWidth property
//   - No ChartTrader grid injection — panel is fully independent
//
// Sentinel Suite Product Family
// -----------------------------
//   Sentinel      : Floating trade panel (this file)
//   Sentinel Log  : Trade journal — JSONL logger, MAE/MFE
//   Sentinel Risk : Standalone session risk monitor
//   Sentinel Lens : Analytics overlay — equity curve, trade stats
//   Sentinel Arc  : Strategy automation layer
//   Sentinel Eye  : Multi-instrument signal scanner
//
// Credits
// -------
//   Original panel base      : Alighten (AlightenButtonPanelV0004)
//   TrendArchitect indicator : _Jason / B3AR
//   UI design system         : Khanh — DailyRangeBot (open source)
//   V1.1 hardening           : Spoobie
//   V1.4 optimizations       : Spoobie
//   V1.5 features            : Spoobie
//   Floating window pattern  : TradeWindow5 (open source reference)
//
// Usage
// -----
//   1. Add this indicator to any chart — a floating Sentinel window appears.
//   2. The window is owned by the chart and minimizes/restores with it.
//   3. For TrendArchitectMQ signals: also add TrendArchitect to the chart.
//   4. For custom signals: set SignalSource and configure the tag or series.
//   5. ARM BULL / ARM BEAR to arm entries on the next qualifying signal.
//   6. All trade management, trailing, and session risk features from
//      TrendArchitectMQPanel V1.6.1 are fully carried over.
// ============================================================================
//
// V1.0 — Initial Sentinel Release
// --------------------------------
//   Forked from TrendArchitectMQPanel V1.6.1. All V1.1–V1.6.1 changes
//   from the MQPanel lineage are incorporated as the baseline.
//
//   New in V1.0:
//   1. FLOATING WINDOW — panel is a standalone WPF ToolWindow owned by
//      the chart. No ChartTrader grid injection. Window persists position
//      between indicator reloads via WindowLeft/WindowTop properties.
//      Width configurable via PanelWidth property (default 280).
//
//   2. SignalSource enum — four configurable signal detection modes:
//        TrendArchitectMQ  — DrawObject scan for TA_SIG_N_MQB/MQS tags
//                            (same as MQPanel — default behavior)
//        DrawObjectTag     — watch for Draw.X with user-defined tag prefix
//                            on BullTagPrefix / BearTagPrefix properties
//        IndicatorSeries   — watch named indicator output series by index;
//                            non-zero = signal. Configure SignalIndicatorName,
//                            BullSeriesIndex, BearSeriesIndex
//        ManualOnly        — ARM buttons only, no automatic signal detection
//
//   3. ARM button labels generalized — "ARM BULL" / "ARM BEAR" instead of
//      "ARM MQB" / "ARM MQS" to reflect signal-agnostic operation.
//
//   4. Window lifecycle — CreateSentinelWindow() / DisposeSentinelWindow()
//      replace CreateWPFControls() / DisposeWPFControls(). Window is
//      created in State.DataLoaded, disposed in State.Terminated.
//      closingWindowFromIndicator flag prevents re-entry on user close.
//
//   5. Quantity reading — reads from ChartTrader quantity selector via
//      the same 4-tier fallback as MQPanel. The ChartTrader remains open
//      and functional alongside the Sentinel window.
//
//   6. Enum self-containment — all enums nested inside the class.
//      No cross-version dependencies. Compiles on all NT8 builds.
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
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.Core;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class SentinelV1_0 : Indicator
    {
        // ── Nested enums — fully self-contained, no cross-version dependencies ──
        // V1.5 fix: All enums nested inside the class. Previous versions relied on
        // namespace-level enums from other compiled files which caused CS0246/CS0103
        // on clean installs and CS0426 on environments with older versions present.
        // Nesting is the only approach that compiles on all NT8 builds.
        public enum OppositeSignalMode { Off, Close, Reverse }
        public enum ReArmMode          { Disarm, ReArm, ReArmMQB, ReArmMQS }
        public enum RiskCardCornerPos  { TopLeft, TopRight, BottomLeft, BottomRight }
        public enum TrailMode          { None, TrailTicks, BreakevenPlus, BarLowHigh, NBarLowHigh, TrailATR, TrendMagic, HalfPlusBE }
        // V1.6: 3-state auto-entry exit mode
        public enum AutoEntryMode      { None, StopOnly, Bracket }

        // V1.0 (Sentinel): configurable signal detection source
        public enum SignalSource
        {
            TrendArchitectMQ,  // Default — TA_SIG_N_MQB / TA_SIG_N_MQS draw objects
            DrawObjectTag,     // Any Draw.X with user-configurable tag prefix
            IndicatorSeries,   // Named indicator output series (non-zero = signal)
            ManualOnly,        // No auto-detection — ARM buttons only
        }

        // ── Dark terminal palette ─────────────────────────────────────────────
        private static readonly Color C_BG     = Color.FromRgb(13,  15,  20);
        private static readonly Color C_CARD   = Color.FromRgb(19,  22,  30);
        private static readonly Color C_BORDER = Color.FromRgb(30,  35,  48);
        private static readonly Color C_DIM    = Color.FromRgb(40,  45,  58);  // card bg
        private static readonly Color C_LABEL  = Color.FromRgb(120, 128, 148); // section labels — readable on all NT skins
        private static readonly Color C_MUTED  = Color.FromRgb(90,  96, 112);
        private static readonly Color C_TEXT   = Color.FromRgb(232, 234, 240);
        private static readonly Color C_GREEN  = Color.FromRgb(0,   212, 160);
        private static readonly Color C_RED    = Color.FromRgb(255,  77, 106);
        private static readonly Color C_AMBER  = Color.FromRgb(245, 158,  11);
        private static readonly Color C_BLUE   = Color.FromRgb(59,  130, 246);
        private static readonly Color C_PURPLE = Color.FromRgb(167, 105, 255);

        // ── Floating window (V1.0 Sentinel) ──────────────────────────────────
        private Window       _sentinelWindow;
        private ScrollViewer _ctScrollViewer;
        private StackPanel   hudStack;
        private bool         uiPanelActive             = false;
        private bool         closingWindowFromIndicator = false;

        private NinjaTrader.Gui.Tools.AccountSelector    xAcSelector;
        private NinjaTrader.Gui.Tools.InstrumentSelector xInSelector;
        private NinjaTrader.Cbi.Account  cachedAccount    = null;  // V1.3: cached for data-thread P&L reads
        private NinjaTrader.Cbi.Instrument cachedInstrument = null;  // V1.3: cached for data-thread use
        private QuantityUpDown        cachedQtySelector;
        private RoutedEventHandler    cachedQtyValueChangedHandler;
        private volatile int          lastKnownChartTraderQty = 1;

        private double       lastClose       = 0;
        private volatile bool isFlatteningAll = false;

        // ── ARM buttons ───────────────────────────────────────────────────────
        private Button btnArmMQB;
        private Button btnArmMQS;

        // ── Mode toggle buttons ───────────────────────────────────────────────
        private Button btnOppOff, btnOppClose, btnOppReverse;
        private Button btnReArmDisarm, btnReArmReArm, btnReArmMQB, btnReArmMQS;

        // ── ARM state ─────────────────────────────────────────────────────────
        private volatile bool mqbArmed = false;
        private volatile bool mqsArmed = false;
        private int lastMqbEntryBar    = -1;
        private int lastMqsEntryBar    = -1;
        private int firstRealtimeBar   = -1;

        // ── Trail state ───────────────────────────────────────────────────────
        private volatile TrailMode activeTrailMode = TrailMode.None;
        private bool   beTriggered    = false;   // BE+N: has one-shot fired?
        private bool   halfTriggered  = false;   // Half+BE: has half-close fired?
        private double trailStopLevel = double.MinValue; // current trailing stop price
        private volatile bool trailStopSubmitPending = false; // V1.5: guard against duplicate trail submits

        // V1.6: Auto-entry exit toggle (3-state)
        private System.Windows.Shapes.Ellipse autoStopToggleDot;
        private Border   autoStopToggleHost;
        private TextBox  tbBracketStopTicks;
        private TextBox  tbAutoTargetTicks;       // V1.6: inline profit target ticks
        private string   currentEntryOcoId;
        private bool     orderUpdateSubscribed;

        // V1.6: Auto-trail on entry
        private Button   btnAutoTrail;
        private bool     autoTrailEnabled    = false;
        private bool     autoTrailActivated  = false; // true once trail armed for current trade

        // V1.6: Trade stats
        private int      sessionTradeCount   = 0;
        private int      sessionWins         = 0;
        private int      sessionLosses       = 0;
        private double   lastRealizedPnL     = double.NaN; // for per-trade delta calc

        // V1.5: Time filter UI
        private StackPanel spTimeFilter;
        private CheckBox   cbTimeFilter1, cbTimeFilter2, cbTimeFilter3;
        private TextBox    tbStartTime1, tbEndTime1, tbStartTime2, tbEndTime2, tbStartTime3, tbEndTime3;
        private TextBlock  txTimeFilterStatus;
        private bool       lastWithinTradingWindow = true;
        private int        lastMqbTimeFilterSkipBar = -1;
        private int        lastMqsTimeFilterSkipBar = -1;

        // Trail mode buttons
        private Button btnTrailTicks, btnBEPlus, btnBarLH, btnNBarLH,
                       btnTrailATR, btnTrendMagic, btnHalfBE;

        // Trail parameter text boxes
        private TextBox tbTrailTicks, tbBETrigger, tbBEBuffer,
                        tbBarLookback, tbATRPeriod, tbATRMult,
                        tbHalfTrigger, tbHalfBuffer;

        // Parameter row containers (for show/hide)
        private Border rowTrailTicks, rowBETrigger, rowBEBuffer,
                       rowBarLookback, rowATRPeriod, rowATRMult,
                       rowHalfTrigger, rowHalfBuffer;

        // ── Session Risk state ────────────────────────────────────────────────
        private double  sessionPnL        = 0;
        // V1.4 (Spoobie): Track last displayed values — gate UI refresh to changes only
        private double  lastRiskUiPnLCents   = double.NaN;
        private bool    lastRiskUiPnlMaxHit;
        private bool    lastRiskUiDdLimitHit;
        private NinjaTrader.Gui.NinjaScript.IndicatorRenderBase cachedSignalSource; // V1.4 (Spoobie): cached TrendArchitect ref — skips indicator scan after first hit
        private SharpDX.DirectWrite.Factory riskTextFactory; // V1.4 (Spoobie): cached — avoids per-frame allocation in OnRender
        private bool    pnlMaxHit         = false;
        private bool    ddLimitHit        = false;
        private bool    riskSoundPlayed   = false;
        // V1.6.1: gates limit checks — prevents false trigger on bogus first-bar P&L read
        private bool    riskPnLValid      = false;
        private double  lastCheckedPnLMax = 0;   // tracks last limit value to detect raise/unlock
        private double  lastCheckedDDLim  = 0;

        // Risk panel UI refs
        private TextBox  tbRiskPnLMax;
        private TextBox  tbRiskDDLimit;
        private Button   btnFlattenOnDD;
        private TextBlock txRiskPnLDisplay;
        private TextBlock txRiskStatusDisplay;

        // ── Section content panels (for collapse toggle) ──────────────────────
        private StackPanel spSignalArming;
        private StackPanel spOppositeSignal;
        private StackPanel spAfterEntry;
        private StackPanel spSessionRisk;
        private StackPanel spTradeMgmt;
        private StackPanel spProfitTrailing;
        private StackPanel spBreakeven;
        private StackPanel spTargets;
        private StackPanel spBracketStop;
        private StackPanel spSize;

        // ── Parameters — Sentinel Signal Source ──────────────────────────────
        [NinjaScriptProperty]
        [Display(Name = "Signal Source", Order = 0, GroupName = "Sentinel Signal")]
        public SignalSource SentinelSignalSource { get; set; } = SignalSource.TrendArchitectMQ;

        [NinjaScriptProperty]
        [Display(Name = "Bull Tag Prefix (DrawObjectTag)", Order = 1, GroupName = "Sentinel Signal")]
        public string BullTagPrefix { get; set; } = "TA_SIG_";

        [NinjaScriptProperty]
        [Display(Name = "Bear Tag Prefix (DrawObjectTag)", Order = 2, GroupName = "Sentinel Signal")]
        public string BearTagPrefix { get; set; } = "TA_SIG_";

        [NinjaScriptProperty]
        [Display(Name = "Bull Tag Suffix (DrawObjectTag)", Order = 3, GroupName = "Sentinel Signal")]
        public string BullTagSuffix { get; set; } = "_MQB";

        [NinjaScriptProperty]
        [Display(Name = "Bear Tag Suffix (DrawObjectTag)", Order = 4, GroupName = "Sentinel Signal")]
        public string BearTagSuffix { get; set; } = "_MQS";

        [NinjaScriptProperty]
        [Display(Name = "Signal Indicator Name (IndicatorSeries)", Order = 5, GroupName = "Sentinel Signal")]
        public string SignalIndicatorName { get; set; } = "";

        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(Name = "Bull Series Index (IndicatorSeries)", Order = 6, GroupName = "Sentinel Signal")]
        public int BullSeriesIndex { get; set; } = 0;

        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(Name = "Bear Series Index (IndicatorSeries)", Order = 7, GroupName = "Sentinel Signal")]
        public int BearSeriesIndex { get; set; } = 1;

        // ── Parameters — Sentinel Window ──────────────────────────────────────
        [NinjaScriptProperty]
        [Range(220, 600)]
        [Display(Name = "Panel Width", Order = 0, GroupName = "Sentinel Window")]
        public int PanelWidth { get; set; } = 280;

        [NinjaScriptProperty]
        [Display(Name = "Window Left (px, -1 = auto)", Order = 1, GroupName = "Sentinel Window")]
        public double WindowLeft { get; set; } = -1;

        [NinjaScriptProperty]
        [Display(Name = "Window Top (px, -1 = auto)", Order = 2, GroupName = "Sentinel Window")]
        public double WindowTop { get; set; } = -1;

        // ── Parameters — Trading ──────────────────────────────────────────────
        [NinjaScriptProperty]
        [Display(Name = "Breakeven1 + X Ticks", Order = 0, GroupName = "Parameters")]
        public int Breakeven1PlusTicks { get; set; } = 3;

        [NinjaScriptProperty]
        [Display(Name = "Breakeven2 + X Ticks", Order = 1, GroupName = "Parameters")]
        public int Breakeven2PlusTicks { get; set; } = 6;

        [NinjaScriptProperty]
        [Display(Name = "Price + X Ticks", Order = 2, GroupName = "Parameters")]
        public int PricePlusTicks { get; set; } = 40;

        [NinjaScriptProperty]
        [Display(Name = "Entry + X Ticks", Order = 3, GroupName = "Parameters")]
        public int EntryPlusTicks { get; set; } = 3;

        [NinjaScriptProperty]
        [Display(Name = "Bracket Stop (Ticks)", Order = 4, GroupName = "Parameters")]
        public int BracketStopTicks { get; set; } = 40;

        [NinjaScriptProperty]
        [Display(Name = "Bracket Profit (Ticks)", Order = 5, GroupName = "Parameters")]
        public int BracketProfitTicks { get; set; } = 40;

        [NinjaScriptProperty]
        [Display(Name = "Flatten All Pause (ms)", Order = 6, GroupName = "Parameters")]
        public int FlattenAllPause { get; set; } = 50;

        [NinjaScriptProperty]
        [Display(Name = "Flatten All Tries", Order = 7, GroupName = "Parameters")]
        public int FlattenAllTries { get; set; } = 6;

        // ── Parameters — Signal Arming ────────────────────────────────────────
        [NinjaScriptProperty]
        [Display(Name = "Opposite Signal Mode", Order = 0, GroupName = "Signal Arming")]
        public OppositeSignalMode OppSignalMode { get; set; } = OppositeSignalMode.Close;

        [NinjaScriptProperty]
        [Display(Name = "Re-Arm Mode", Order = 1, GroupName = "Signal Arming")]
        public ReArmMode ReArmAfterEntry { get; set; } = ReArmMode.Disarm;

        // V1.6: 3-state auto-exit mode (None / StopOnly / Bracket)
        [NinjaScriptProperty]
        [Display(Name = "Auto Entry Mode", Order = 2, GroupName = "Signal Arming")]
        public AutoEntryMode AutoEntryExitMode { get; set; } = AutoEntryMode.None;

        // V1.6: Profit target ticks for auto-bracket and manual Bracket button
        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "Auto Target Ticks", Order = 3, GroupName = "Signal Arming")]
        public int AutoTargetTicks { get; set; } = 40;

        // V1.6: Show trade stats on the on-chart risk card
        [NinjaScriptProperty]
        [Display(Name = "Show Trade Stats", Order = 4, GroupName = "Signal Arming")]
        public bool ShowTradeStats { get; set; } = true;

        // ── Parameters — Time Filter ──────────────────────────────────────────
        [NinjaScriptProperty]
        [Display(Name = "Enable Time Filter 1", Order = 0, GroupName = "Time Filter")]
        public bool EnableTimeFilter1 { get; set; } = false;

        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Start Time 1", Order = 1, GroupName = "Time Filter")]
        public DateTime StartTime1 { get; set; } = DateTime.Parse("09:30", CultureInfo.InvariantCulture);

        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "End Time 1", Order = 2, GroupName = "Time Filter")]
        public DateTime EndTime1 { get; set; } = DateTime.Parse("11:30", CultureInfo.InvariantCulture);

        [NinjaScriptProperty]
        [Display(Name = "Enable Time Filter 2", Order = 3, GroupName = "Time Filter")]
        public bool EnableTimeFilter2 { get; set; } = false;

        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Start Time 2", Order = 4, GroupName = "Time Filter")]
        public DateTime StartTime2 { get; set; } = DateTime.Parse("13:00", CultureInfo.InvariantCulture);

        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "End Time 2", Order = 5, GroupName = "Time Filter")]
        public DateTime EndTime2 { get; set; } = DateTime.Parse("15:00", CultureInfo.InvariantCulture);

        [NinjaScriptProperty]
        [Display(Name = "Enable Time Filter 3", Order = 6, GroupName = "Time Filter")]
        public bool EnableTimeFilter3 { get; set; } = false;

        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Start Time 3", Order = 7, GroupName = "Time Filter")]
        public DateTime StartTime3 { get; set; } = DateTime.Parse("15:00", CultureInfo.InvariantCulture);

        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "End Time 3", Order = 8, GroupName = "Time Filter")]
        public DateTime EndTime3 { get; set; } = DateTime.Parse("16:00", CultureInfo.InvariantCulture);

        // ── Parameters — Session Risk ─────────────────────────────────────────
        [NinjaScriptProperty]
        [Display(Name = "Session P&L Max ($)", Order = 0, GroupName = "Session Risk")]
        public double SessionPnLMax { get; set; } = 0;

        [NinjaScriptProperty]
        [Display(Name = "Session DD Limit ($, negative)", Order = 1, GroupName = "Session Risk")]
        public double SessionDDLimit { get; set; } = 0;

        [NinjaScriptProperty]
        [Display(Name = "Flatten on DD Limit", Order = 2, GroupName = "Session Risk")]
        public bool FlattenOnDDLimit { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Risk Card Corner", Order = 3, GroupName = "Session Risk")]
        public RiskCardCornerPos RiskCardCorner { get; set; } = RiskCardCornerPos.BottomRight;

        [NinjaScriptProperty]
        [Display(Name = "Sound File (blank = default)", Order = 4, GroupName = "Session Risk")]
        public string SoundFile { get; set; } = "";

        // ── Parameters — Profit Trailing ─────────────────────────────────────
        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "Trail: Ticks", Order = 0, GroupName = "Profit Trailing")]
        public int TrailTicks { get; set; } = 10;

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "Trail: BE Trigger Ticks", Order = 1, GroupName = "Profit Trailing")]
        public int BETriggerTicks { get; set; } = 20;

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Trail: BE Buffer Ticks", Order = 2, GroupName = "Profit Trailing")]
        public int BEBufferTicks { get; set; } = 2;

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "Trail: Bar Lookback", Order = 3, GroupName = "Profit Trailing")]
        public int TrailBarLookback { get; set; } = 1;

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Trail: ATR Period", Order = 4, GroupName = "Profit Trailing")]
        public int TrailATRPeriod { get; set; } = 14;

        [NinjaScriptProperty]
        [Range(0.1, 10.0)]
        [Display(Name = "Trail: ATR Multiplier", Order = 5, GroupName = "Profit Trailing")]
        public double TrailATRMult { get; set; } = 2.0;

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "Trail: Half Trigger Ticks", Order = 6, GroupName = "Profit Trailing")]
        public int HalfTriggerTicks { get; set; } = 20;

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Trail: Half Buffer Ticks", Order = 7, GroupName = "Profit Trailing")]
        public int HalfBufferTicks { get; set; } = 2;

        // ── Parameters — Panel Layout ─────────────────────────────────────────
        [NinjaScriptProperty]
        [Range(18, 40)]
        [Display(Name = "Button Height", Order = 0, GroupName = "Panel Layout")]
        public int ButtonHeight { get; set; } = 26;

        // ── Parameters — Collapse State (persisted) ───────────────────────────
        // V1.2: Each section's open/closed state is a NinjaScriptProperty so it
        // survives chart reloads, workspace saves, and template saves.
        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Section: Signal Arming Open",   GroupName = "Panel Layout", Order = 10)]
        public bool SecSignalArmingOpen   { get; set; } = true;

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Section: Opposite Signal Open", GroupName = "Panel Layout", Order = 11)]
        public bool SecOppositeSignalOpen { get; set; } = true;

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Section: After Entry Open",     GroupName = "Panel Layout", Order = 12)]
        public bool SecAfterEntryOpen     { get; set; } = true;

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Section: Time Filter Open",     GroupName = "Panel Layout", Order = 13)]
        public bool SecTimeFilterOpen     { get; set; } = false;

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Section: Session Risk Open",    GroupName = "Panel Layout", Order = 14)]
        public bool SecSessionRiskOpen    { get; set; } = false;

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Section: Trade Mgmt Open",      GroupName = "Panel Layout", Order = 15)]
        public bool SecTradeMgmtOpen      { get; set; } = false;

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Section: Profit Trailing Open", GroupName = "Panel Layout", Order = 16)]
        public bool SecProfitTrailingOpen { get; set; } = false;

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Section: Breakeven Open",       GroupName = "Panel Layout", Order = 17)]
        public bool SecBreakevenOpen      { get; set; } = false;

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Section: Targets Open",         GroupName = "Panel Layout", Order = 18)]
        public bool SecTargetsOpen        { get; set; } = false;

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Section: Bracket/Stop Open",    GroupName = "Panel Layout", Order = 19)]
        public bool SecBracketStopOpen    { get; set; } = false;

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Section: Size Open",            GroupName = "Panel Layout", Order = 20)]
        public bool SecSizeOpen           { get; set; } = false;

        // ═════════════════════════════════════════════════════════════════════
        // Lifecycle
        // ═════════════════════════════════════════════════════════════════════
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description  = @"Sentinel — floating trade panel with configurable signal arming, trade management, profit trailing, and session risk. Part of the Sentinel Suite.";
                Name         = "SentinelV1_0";
                // V1.4 (Spoobie): OnBarClose is correct — MQ dots are drawn at bar close.
                // Trail execution remains tick-level via OnMarketData override below.
                Calculate    = Calculate.OnBarClose;
                IsOverlay    = true;
                DisplayInDataBox         = true;
                DrawOnPricePanel         = true;
                DrawHorizontalGridLines  = true;
                DrawVerticalGridLines    = true;
                PaintPriceMarkers        = true;
                ScaleJustification       = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsSuspendedWhileInactive = true;
            }
            else if (State == State.DataLoaded)
            {
                ChartControl.Dispatcher.BeginInvoke(new Action(CreateSentinelWindow));
            }
            else if (State == State.Realtime)
            {
                SubscribeChartTraderOrderUpdate();
            }
            else if (State == State.Terminated)
            {
                UnsubscribeChartTraderOrderUpdate();
                ChartControl?.Dispatcher.BeginInvoke(new Action(DisposeSentinelWindow));
                try { riskTextFactory?.Dispose(); riskTextFactory = null; } catch { }
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // OnBarUpdate
        // ═════════════════════════════════════════════════════════════════════
        protected override void OnBarUpdate()
        {
            lastClose = Close[0];
            if (State != State.Realtime) return;

            if (firstRealtimeBar < 0) firstRealtimeBar = CurrentBar;

            // Session risk check every bar
            CheckSessionRisk();

            bool withinWindow = IsWithinTradingWindow();
            if (withinWindow != lastWithinTradingWindow)
            {
                lastWithinTradingWindow = withinWindow;
                if (IsTimeFilterActive())
                    Print("TIME FILTER ▶ " + (withinWindow ? "WINDOW OPEN — entries allowed" : "OUTSIDE WINDOW — MQ arm entries blocked")
                        + " | " + BuildTimeFilterLogContext());
                ChartControl?.Dispatcher.InvokeAsync(() =>
                {
                    RefreshTimeFilterStatus();
                    RefreshArmButtons();
                });
            }

            // V1.5: Time filter gates MQ arm entries only
            if (!withinWindow)
            {
                if (mqbArmed || mqsArmed)
                {
                    int[] skipBars = CurrentBar > firstRealtimeBar
                        ? new[] { CurrentBar, CurrentBar - 1 }
                        : new[] { CurrentBar };
                    foreach (int bar in skipBars)
                    {
                        if (bar < 0) continue;
                        if (mqbArmed && bar != lastMqbEntryBar && FindChartDrawObject("TA_SIG_" + bar + "_MQB") != null
                            && bar != lastMqbTimeFilterSkipBar)
                        {
                            lastMqbTimeFilterSkipBar = bar;
                            Print(BuildTimeFilterSkipMessage("MQB", bar));
                        }
                        if (mqsArmed && bar != lastMqsEntryBar && FindChartDrawObject("TA_SIG_" + bar + "_MQS") != null
                            && bar != lastMqsTimeFilterSkipBar)
                        {
                            lastMqsTimeFilterSkipBar = bar;
                            Print(BuildTimeFilterSkipMessage("MQS", bar));
                        }
                    }
                }
                return;
            }

            lastMqbTimeFilterSkipBar = -1;
            lastMqsTimeFilterSkipBar = -1;

            bool allowPrevBar = CurrentBar > firstRealtimeBar;
            int[] barsToCheck = allowPrevBar
                ? new[] { CurrentBar, CurrentBar - 1 }
                : new[] { CurrentBar };

            // V1.4 (Spoobie): Read value-type properties directly — no Dispatcher needed
            OppositeSignalMode liveOpp   = OppSignalMode;
            ReArmMode          liveReArm = ReArmAfterEntry;

            // V1.0 (Sentinel): signal detection — all SignalSource modes
            if (SentinelSignalSource != SignalSource.ManualOnly)
            {
                if (mqbArmed)
                {
                    bool bullFired = false;
                    if (SentinelSignalSource == SignalSource.IndicatorSeries)
                    {
                        bullFired = CheckIndicatorSeriesSignal(true) && CurrentBar != lastMqbEntryBar;
                    }
                    else
                    {
                        foreach (int bar in barsToCheck)
                        {
                            if (bar < 0 || bar == lastMqbEntryBar) continue;
                            string tag = SentinelSignalSource == SignalSource.TrendArchitectMQ
                                ? "TA_SIG_" + bar + "_MQB"
                                : BullTagPrefix + bar + BullTagSuffix;
                            if (FindChartDrawObject(tag) != null) { bullFired = true; lastMqbEntryBar = bar; break; }
                        }
                    }
                    if (bullFired)
                    {
                        if (SentinelSignalSource == SignalSource.IndicatorSeries) lastMqbEntryBar = CurrentBar;
                        bool keepArmed = liveReArm == ReArmMode.ReArm || liveReArm == ReArmMode.ReArmMQB;
                        bool didOpposite = HandleOppositePosition(OrderAction.Buy, "BULL", liveOpp);
                        if (!didOpposite) SubmitArmEntry(OrderAction.Buy, "BULL");
                        if (!keepArmed) mqbArmed = false;
                        ChartControl.Dispatcher.InvokeAsync(() => RefreshArmButtons());
                    }
                }

                if (mqsArmed)
                {
                    bool bearFired = false;
                    if (SentinelSignalSource == SignalSource.IndicatorSeries)
                    {
                        bearFired = CheckIndicatorSeriesSignal(false) && CurrentBar != lastMqsEntryBar;
                    }
                    else
                    {
                        foreach (int bar in barsToCheck)
                        {
                            if (bar < 0 || bar == lastMqsEntryBar) continue;
                            string tag = SentinelSignalSource == SignalSource.TrendArchitectMQ
                                ? "TA_SIG_" + bar + "_MQS"
                                : BearTagPrefix + bar + BearTagSuffix;
                            if (FindChartDrawObject(tag) != null) { bearFired = true; lastMqsEntryBar = bar; break; }
                        }
                    }
                    if (bearFired)
                    {
                        if (SentinelSignalSource == SignalSource.IndicatorSeries) lastMqsEntryBar = CurrentBar;
                        bool keepArmed = liveReArm == ReArmMode.ReArm || liveReArm == ReArmMode.ReArmMQS;
                        bool didOpposite = HandleOppositePosition(OrderAction.SellShort, "BEAR", liveOpp);
                        if (!didOpposite) SubmitArmEntry(OrderAction.SellShort, "BEAR");
                        if (!keepArmed) mqsArmed = false;
                        ChartControl.Dispatcher.InvokeAsync(() => RefreshArmButtons());
                    }
                }
            }
        }


        // ─────────────────────────────────────────────────────────────────────
        // OnMarketData — tick-level trail execution
        // ─────────────────────────────────────────────────────────────────────
        protected override void OnMarketData(MarketDataEventArgs e)
        {
            if (State != State.Realtime) return;
            if (e.MarketDataType != MarketDataType.Last) return;
            lastClose = e.Price; // V1.4 (Spoobie): always use true last tick price for trail calculations
            if (activeTrailMode == TrailMode.None) return;
            ExecuteTrail();
        }

        // ═════════════════════════════════════════════════════════════════════
        // WPF — build / dispose
        // ═════════════════════════════════════════════════════════════════════
        // ── Floating window creation (V1.0 Sentinel) ─────────────────────────
        private void CreateSentinelWindow()
        {
            try
            {
                if (_sentinelWindow != null) return;
                if (ChartControl?.Parent == null) { Print("[Sentinel] ChartControl.Parent is null"); return; }

                hudStack = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Background  = new SolidColorBrush(C_BG),
                    MinWidth    = PanelWidth,
                    MaxWidth    = PanelWidth,
                };

                BuildHeader();
                BuildCollapsibleSection("SESSION RISK",    BuildSessionRiskContent,    ref spSessionRisk,    () => SecSessionRiskOpen,    v => SecSessionRiskOpen    = v);
                BuildCollapsibleSection("SIGNAL ARMING",   BuildSignalArmingContent,   ref spSignalArming,   () => SecSignalArmingOpen,   v => SecSignalArmingOpen   = v);
                BuildCollapsibleSection("TIME FILTER",     BuildTimeFilterContent,     ref spTimeFilter,     () => SecTimeFilterOpen,     v => SecTimeFilterOpen     = v);
                BuildCollapsibleSection("OPPOSITE SIGNAL", BuildOppositeSignalContent,  ref spOppositeSignal, () => SecOppositeSignalOpen, v => SecOppositeSignalOpen = v);
                BuildCollapsibleSection("AFTER ENTRY",     BuildAfterEntryContent,      ref spAfterEntry,     () => SecAfterEntryOpen,     v => SecAfterEntryOpen     = v);
                BuildFlattenButton();
                BuildCollapsibleSection("TRADE MANAGEMENT", BuildTradeMgmtContent,      ref spTradeMgmt,      () => SecTradeMgmtOpen,      v => SecTradeMgmtOpen      = v);
                hudStack.Children.Add(new Border { Height = 4 });

                _ctScrollViewer = new ScrollViewer
                {
                    Content                       = hudStack,
                    VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    HorizontalAlignment           = HorizontalAlignment.Stretch,
                    VerticalAlignment             = VerticalAlignment.Stretch,
                    CanContentScroll              = false,   // smooth pixel scrolling
                    Background                    = new SolidColorBrush(C_BG),
                    Focusable                     = true,
                };



        private void DisposeSentinelWindow()
        {
            try
            {
                DetachChartTraderQuantitySelector();
                if (_ctScrollViewer != null) _ctScrollViewer.Content = null;
                _ctScrollViewer = null;
                if (_sentinelWindow != null)
                {
                    closingWindowFromIndicator = true;
                    _sentinelWindow.Close();
                    closingWindowFromIndicator = false;
                    _sentinelWindow = null;
                }
                uiPanelActive = false;
            }
            catch { }
        }

        private void RebuildPanel()
        {
            riskPnLValid = false;
            DisposeSentinelWindow();
            ChartControl.Dispatcher.BeginInvoke(new Action(CreateSentinelWindow));
        }

        // V1.0 (Sentinel): Mouse wheel on the floating scroll viewer
        private void OnPanelMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_ctScrollViewer == null) return;
            double newOffset = _ctScrollViewer.VerticalOffset - e.Delta;
            newOffset = Math.Max(0, Math.Min(newOffset, _ctScrollViewer.ScrollableHeight));
            _ctScrollViewer.ScrollToVerticalOffset(newOffset);
            e.Handled = true;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Cache account + instrument on UI thread for safe data-thread access
        // ─────────────────────────────────────────────────────────────────────
        private void CacheAccountAndInstrument()
        {
            try
            {
                xAcSelector  = GetChartTraderWindow()?.FindFirst("ChartTraderControlAccountSelector")
                    as NinjaTrader.Gui.Tools.AccountSelector;
                cachedAccount = xAcSelector?.SelectedAccount;

                xInSelector  = GetChartTraderWindow()?.FindFirst("ChartWindowInstrumentSelector")
                    as NinjaTrader.Gui.Tools.InstrumentSelector;
                cachedInstrument = xInSelector?.Instrument ?? Instrument;

                // Re-cache if account selection changes
                if (xAcSelector != null)
                    xAcSelector.SelectionChanged += (s, e) =>
                    {
                        UnsubscribeChartTraderOrderUpdate();
                        cachedAccount = xAcSelector.SelectedAccount;
                        if (State == State.Realtime)
                            SubscribeChartTraderOrderUpdate();
                    };

                Print("[MQPanel] Cached account: " + (cachedAccount?.Name ?? "none")
                    + "  instrument: " + (cachedInstrument?.FullName ?? "none"));
            }
            catch (Exception ex) { Print("[MQPanel] CacheAccountAndInstrument error: " + ex.Message); }
        }

        // ═════════════════════════════════════════════════════════════════════
        // UI — Header
        // ═════════════════════════════════════════════════════════════════════
        private void BuildHeader()
        {
            var g = new Grid { Margin = new Thickness(8, 6, 8, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Left: dot + title
            var left = new StackPanel { Orientation = Orientation.Horizontal,
                                        VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 8, Height = 8,
                Fill  = new SolidColorBrush(C_GREEN),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 7, 0),
            });
            left.Children.Add(Tx("SENTINEL", 13, C_TEXT, bold: true));
            left.Children.Add(Tx("  SUITE", 10, C_MUTED));

            // V1.2: ↺ Rebuild button — left of version chip
            var rebuildBtn = new Button
            {
                Content         = "↺",
                Width           = 18,
                Height          = 18,
                FontSize        = 11,
                Background      = new SolidColorBrush(C_DIM),
                Foreground      = new SolidColorBrush(C_MUTED),
                BorderBrush     = new SolidColorBrush(C_BORDER),
                BorderThickness = new Thickness(1),
                Margin          = new Thickness(0, 0, 4, 0),
                Padding         = new Thickness(0),
                VerticalContentAlignment   = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                ToolTip         = "Rebuild panel (applies ButtonHeight and property changes)",
                Cursor          = Cursors.Hand,
                Style           = null,   // strip NT default style — it adds padding that inflates the button
            };
            rebuildBtn.Click += (s, e) => RebuildPanel();

            // Version chip
            var chip = new Border
            {
                Background   = new SolidColorBrush(C_DIM),
                CornerRadius = new CornerRadius(4),
                Padding      = new Thickness(6, 3, 6, 3),
                Child        = Tx("v1.0", 10, C_MUTED),
            };

            Grid.SetColumn(left,       0);
            Grid.SetColumn(rebuildBtn, 1);
            Grid.SetColumn(chip,       2);
            g.Children.Add(left);
            g.Children.Add(rebuildBtn);
            g.Children.Add(chip);
            hudStack.Children.Add(g);
            hudStack.Children.Add(HRule());
        }

        // ═════════════════════════════════════════════════════════════════════
        // UI — Collapsible section builder
        // V1.2: Each section consists of a clickable header row and a content
        // StackPanel. Clicking the header toggles Visible/Collapsed on the
        // content panel and persists the state via the provided getter/setter.
        // ═════════════════════════════════════════════════════════════════════
        private void BuildCollapsibleSection(
            string label,
            Action<StackPanel> contentBuilder,
            ref StackPanel contentPanel,
            Func<bool> getOpen,
            Action<bool> setOpen)
        {
            bool isOpen = getOpen();

            // ── Header row ────────────────────────────────────────────────────
            var headerGrid = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
                Margin     = new Thickness(6, 5, 6, 0),
                Cursor     = Cursors.Hand,
            };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition());

            var chevron = Tx(isOpen ? "▼" : "▶", 8, C_LABEL);
            chevron.Margin = new Thickness(0, 0, 5, 0);
            chevron.VerticalAlignment = VerticalAlignment.Center;

            var lbl  = Tx(label, 9, C_LABEL, bold: true);
            lbl.VerticalAlignment = VerticalAlignment.Center;

            var line = new Border
            {
                BorderBrush       = new SolidColorBrush(C_BORDER),
                BorderThickness   = new Thickness(0, 1, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(6, 0, 0, 0),
            };

            Grid.SetColumn(chevron, 0);
            Grid.SetColumn(lbl,     1);
            Grid.SetColumn(line,    2);
            headerGrid.Children.Add(chevron);
            headerGrid.Children.Add(lbl);
            headerGrid.Children.Add(line);
            hudStack.Children.Add(headerGrid);

            // ── Content panel ─────────────────────────────────────────────────
            var sp = new StackPanel
            {
                Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed,
            };
            contentBuilder(sp);
            hudStack.Children.Add(sp);

            // Wire local ref for RefreshXxx methods that need to access buttons
            contentPanel = sp;

            // ── Toggle handler ────────────────────────────────────────────────
            headerGrid.MouseLeftButtonUp += (s, e) =>
            {
                bool nowOpen = sp.Visibility != Visibility.Visible;
                sp.Visibility = nowOpen ? Visibility.Visible : Visibility.Collapsed;
                chevron.Text  = nowOpen ? "▼" : "▶"; // color already set at creation
                setOpen(nowOpen);
            };
        }

        // ═════════════════════════════════════════════════════════════════════
        // UI — Section content builders
        // ═════════════════════════════════════════════════════════════════════


        // ─────────────────────────────────────────────────────────────────────
        // SESSION RISK — UI builder
        // ─────────────────────────────────────────────────────────────────────
        private void BuildSessionRiskContent(StackPanel sp)
        {
            var card = Card();
            var inner = new StackPanel();

            // ── P&L Max row ───────────────────────────────────────────────────
            inner.Children.Add(RiskParamRow("P&L Max ($)",    SessionPnLMax.ToString("F2"),  out tbRiskPnLMax));
            inner.Children.Add(RiskParamRow("DD Limit ($)",   SessionDDLimit.ToString("F2"), out tbRiskDDLimit));

            // ── Flatten on DD toggle ──────────────────────────────────────────
            btnFlattenOnDD = new Button
            {
                Content         = FlattenOnDDLimit ? "✓ Flatten on DD" : "Flatten on DD",
                Height          = ButtonHeight,
                FontSize        = 10,
                FontWeight      = FontWeights.Bold,
                FontFamily      = new FontFamily("Consolas, Courier New"),
                Margin          = new Thickness(2, 4, 2, 2),
                Cursor          = Cursors.Hand,
            };
            SetRiskFlattenBtn();
            btnFlattenOnDD.Click += (s, e) =>
            {
                FlattenOnDDLimit = !FlattenOnDDLimit;
                SetRiskFlattenBtn();
            };
            inner.Children.Add(btnFlattenOnDD);

            // ── Apply button ──────────────────────────────────────────────────
            var applyBtn = new Button
            {
                Content         = "Apply Limits",
                Height          = ButtonHeight,
                FontSize        = 10,
                FontWeight      = FontWeights.Bold,
                FontFamily      = new FontFamily("Consolas, Courier New"),
                Margin          = new Thickness(2, 2, 2, 2),
                Background      = new SolidColorBrush(Color.FromArgb(60, C_BLUE.R, C_BLUE.G, C_BLUE.B)),
                Foreground      = new SolidColorBrush(C_BLUE),
                BorderBrush     = new SolidColorBrush(C_BLUE),
                BorderThickness = new Thickness(1),
                Cursor          = Cursors.Hand,
            };
            applyBtn.Click += (s, e) => ApplyRiskLimits();
            inner.Children.Add(applyBtn);

            // ── Live P&L display ──────────────────────────────────────────────
            var pnlRow = new Grid { Margin = new Thickness(0, 6, 0, 2) };
            pnlRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            pnlRow.ColumnDefinitions.Add(new ColumnDefinition());
            pnlRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var pnlLbl = new TextBlock
            {
                Text       = "Session P&L",
                FontSize   = 10,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(C_LABEL),
                FontFamily = new FontFamily("Consolas, Courier New"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin     = new Thickness(0, 0, 6, 0),
            };
            txRiskPnLDisplay = new TextBlock
            {
                Text       = "$0.00",
                FontSize   = 11,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(C_MUTED),
                FontFamily = new FontFamily("Consolas, Courier New"),
                VerticalAlignment  = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            txRiskStatusDisplay = new TextBlock
            {
                Text       = "",
                FontSize   = 9,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(C_MUTED),
                FontFamily = new FontFamily("Consolas, Courier New"),
                VerticalAlignment  = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin     = new Thickness(6, 0, 0, 0),
            };

            Grid.SetColumn(pnlLbl,            0);
            Grid.SetColumn(txRiskPnLDisplay,   1);
            Grid.SetColumn(txRiskStatusDisplay,2);
            pnlRow.Children.Add(pnlLbl);
            pnlRow.Children.Add(txRiskPnLDisplay);
            pnlRow.Children.Add(txRiskStatusDisplay);
            inner.Children.Add(pnlRow);

            card.Child = inner;
            sp.Children.Add(card);
        }

        private Border RiskParamRow(string label, string defaultVal, out TextBox tb)
        {
            var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var lbl = new TextBlock
            {
                Text              = label,
                FontSize          = 10,
                FontWeight        = FontWeights.Bold,
                Foreground        = new SolidColorBrush(C_LABEL),
                FontFamily        = new FontFamily("Consolas, Courier New"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(0, 0, 4, 0),
            };
            var box = new TextBox
            {
                Text                     = defaultVal,
                FontSize                 = 10,
                Height                   = ButtonHeight - 2,
                Background               = new SolidColorBrush(C_DIM),
                Foreground               = new SolidColorBrush(C_TEXT),
                BorderBrush              = new SolidColorBrush(C_BORDER),
                BorderThickness          = new Thickness(1),
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding                  = new Thickness(4, 0, 4, 0),
                FontFamily               = new FontFamily("Consolas, Courier New"),
            };
            box.PreviewKeyDown += TrailTextBox_PreviewKeyDown;
            Grid.SetColumn(lbl, 0); Grid.SetColumn(box, 1);
            g.Children.Add(lbl); g.Children.Add(box);
            tb = box;
            return new Border { Child = g };
        }

        private void SetRiskFlattenBtn()
        {
            if (btnFlattenOnDD == null) return;
            btnFlattenOnDD.Background      = new SolidColorBrush(FlattenOnDDLimit
                ? Color.FromArgb(60, C_RED.R, C_RED.G, C_RED.B) : C_DIM);
            btnFlattenOnDD.Foreground      = new SolidColorBrush(FlattenOnDDLimit ? C_RED : C_MUTED);
            btnFlattenOnDD.BorderBrush     = new SolidColorBrush(FlattenOnDDLimit ? C_RED : C_BORDER);
            btnFlattenOnDD.BorderThickness = new Thickness(FlattenOnDDLimit ? 2 : 1);
            btnFlattenOnDD.Content         = FlattenOnDDLimit ? "✓ Flatten on DD" : "Flatten on DD";
        }

        private void ApplyRiskLimits()
        {
            if (tbRiskPnLMax  != null && double.TryParse(tbRiskPnLMax.Text,
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out double pm))
                SessionPnLMax = pm;
            if (tbRiskDDLimit != null && double.TryParse(tbRiskDDLimit.Text,
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out double dl))
                SessionDDLimit = dl;

            // Unlock if limits were raised above current P&L
            CheckRiskUnlock();
            Print("Risk limits applied — PnL Max: " + SessionPnLMax.ToString("C2")
                + "  DD Limit: " + SessionDDLimit.ToString("C2"));
        }

        // Update the inline P&L display on the panel
        private void UpdateRiskDisplay()
        {
            if (!uiPanelActive) return;
            if (txRiskPnLDisplay == null) return;
            string pnlText  = sessionPnL >= 0
                ? "+$" + sessionPnL.ToString("N2")
                : "-$" + Math.Abs(sessionPnL).ToString("N2");
            Color  pnlColor = sessionPnL >= 0 ? C_GREEN : C_RED;
            string status   = pnlMaxHit  ? "MAX HIT" : ddLimitHit ? "DD HIT" : "";
            Color  stColor  = pnlMaxHit  ? C_AMBER   : ddLimitHit ? C_RED    : C_MUTED;

            txRiskPnLDisplay.Text       = pnlText;
            txRiskPnLDisplay.Foreground = new SolidColorBrush(pnlColor);
            if (txRiskStatusDisplay != null)
            {
                txRiskStatusDisplay.Text       = status;
                txRiskStatusDisplay.Foreground = new SolidColorBrush(stColor);
            }
        }

        private void BuildSignalArmingContent(StackPanel sp)
        {
            var armRow = new Grid { Margin = new Thickness(6, 4, 6, 4) };
            armRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            armRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            btnArmMQB = new Button
            {
                Content         = "▲  ARM BULL",
                FontSize        = 12,
                FontWeight      = FontWeights.Bold,
                Height          = ButtonHeight + 4,
                Margin          = new Thickness(0, 0, 3, 0),
                Background      = new SolidColorBrush(Color.FromArgb(40, C_DIM.R, C_DIM.G, C_DIM.B)),
                Foreground      = new SolidColorBrush(C_MUTED),
                BorderBrush     = new SolidColorBrush(C_BORDER),
                BorderThickness = new Thickness(2),
                Cursor          = Cursors.Hand,
            };
            btnArmMQS = new Button
            {
                Content         = "▼  ARM BEAR",
                FontSize        = 12,
                FontWeight      = FontWeights.Bold,
                Height          = ButtonHeight + 4,
                Margin          = new Thickness(3, 0, 0, 0),
                Background      = new SolidColorBrush(Color.FromArgb(40, C_DIM.R, C_DIM.G, C_DIM.B)),
                Foreground      = new SolidColorBrush(C_MUTED),
                BorderBrush     = new SolidColorBrush(C_BORDER),
                BorderThickness = new Thickness(2),
                Cursor          = Cursors.Hand,
            };
            btnArmMQB.Click += (s, e) =>
            {
                mqbArmed = !mqbArmed;
                if (mqbArmed)
                {
                    lastMqbEntryBar = -1;
                    lastMqbTimeFilterSkipBar = -1;
                    activeTrailMode = TrailMode.None; trailStopLevel = double.MinValue; beTriggered = false; halfTriggered = false;
                    if (IsTimeFilterActive() && !IsWithinTradingWindow())
                        Print("ARM MQB ▶ armed — TIME FILTER blocks entries until window opens | " + BuildTimeFilterLogContext());
                }
                Print("ARM BULL ▶ " + (mqbArmed ? "ARMED" : "disarmed"));
                RefreshArmButtons();
            };
            btnArmMQS.Click += (s, e) =>
            {
                mqsArmed = !mqsArmed;
                if (mqsArmed)
                {
                    lastMqsEntryBar = -1;
                    lastMqsTimeFilterSkipBar = -1;
                    activeTrailMode = TrailMode.None; trailStopLevel = double.MinValue; beTriggered = false; halfTriggered = false;
                    if (IsTimeFilterActive() && !IsWithinTradingWindow())
                        Print("ARM MQS ▶ armed — TIME FILTER blocks entries until window opens | " + BuildTimeFilterLogContext());
                }
                Print("ARM BEAR ▶ " + (mqsArmed ? "ARMED" : "disarmed"));
                RefreshArmButtons();
            };
            Grid.SetColumn(btnArmMQB, 0);
            Grid.SetColumn(btnArmMQS, 1);
            armRow.Children.Add(btnArmMQB);
            armRow.Children.Add(btnArmMQS);
            sp.Children.Add(armRow);
        }

        // ─────────────────────────────────────────────────────────────────────
        // TIME FILTER — UI builder (V1.5)
        // ─────────────────────────────────────────────────────────────────────
        private void BuildTimeFilterContent(StackPanel sp)
        {
            var card = Card();
            var inner = new StackPanel();

            inner.Children.Add(TimeFilterRow(1, EnableTimeFilter1, StartTime1, EndTime1, out cbTimeFilter1, out tbStartTime1, out tbEndTime1));
            inner.Children.Add(TimeFilterRow(2, EnableTimeFilter2, StartTime2, EndTime2, out cbTimeFilter2, out tbStartTime2, out tbEndTime2));
            inner.Children.Add(TimeFilterRow(3, EnableTimeFilter3, StartTime3, EndTime3, out cbTimeFilter3, out tbStartTime3, out tbEndTime3));

            var applyBtn = new Button
            {
                Content         = "Apply Windows",
                Height          = ButtonHeight,
                FontSize        = 10,
                FontWeight      = FontWeights.Bold,
                FontFamily      = new FontFamily("Consolas, Courier New"),
                Margin          = new Thickness(2, 4, 2, 2),
                Background      = new SolidColorBrush(Color.FromArgb(60, C_BLUE.R, C_BLUE.G, C_BLUE.B)),
                Foreground      = new SolidColorBrush(C_BLUE),
                BorderBrush     = new SolidColorBrush(C_BLUE),
                BorderThickness = new Thickness(1),
                Cursor          = Cursors.Hand,
            };
            applyBtn.Click += (s, e) => ApplyTimeFilterSettings();
            inner.Children.Add(applyBtn);

            txTimeFilterStatus = new TextBlock
            {
                Text       = "WINDOW OPEN",
                FontSize   = 10,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(C_GREEN),
                FontFamily = new FontFamily("Consolas, Courier New"),
                Margin     = new Thickness(2, 4, 2, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            inner.Children.Add(txTimeFilterStatus);

            card.Child = inner;
            sp.Children.Add(card);
            RefreshTimeFilterStatus();
        }

        private Border TimeFilterRow(int num, bool enabled, DateTime start, DateTime end,
            out CheckBox cb, out TextBox tbStart, out TextBox tbEnd)
        {
            var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            cb = new CheckBox
            {
                IsChecked         = enabled,
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(0, 0, 6, 0),
                ToolTip           = "Enable window " + num,
            };
            tbStart = TimeFilterTimeBox(FormatTimeForBox(start));
            tbEnd   = TimeFilterTimeBox(FormatTimeForBox(end));

            var dash = Tx("–", 10, C_MUTED);
            dash.VerticalAlignment = VerticalAlignment.Center;
            dash.Margin = new Thickness(4, 0, 4, 0);

            Grid.SetColumn(cb,      0);
            Grid.SetColumn(tbStart, 1);
            Grid.SetColumn(dash,    2);
            Grid.SetColumn(tbEnd,   3);
            g.Children.Add(cb);
            g.Children.Add(tbStart);
            g.Children.Add(dash);
            g.Children.Add(tbEnd);
            return new Border { Child = g };
        }

        private TextBox TimeFilterTimeBox(string defaultVal)
        {
            var box = new TextBox
            {
                Text                     = defaultVal,
                FontSize                 = 10,
                Height                   = ButtonHeight - 2,
                Background               = new SolidColorBrush(C_DIM),
                Foreground               = new SolidColorBrush(C_TEXT),
                BorderBrush              = new SolidColorBrush(C_BORDER),
                BorderThickness          = new Thickness(1),
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding                  = new Thickness(4, 0, 4, 0),
                FontFamily               = new FontFamily("Consolas, Courier New"),
                ToolTip                  = "HH:mm (24h)",
            };
            box.PreviewKeyDown += TrailTextBox_PreviewKeyDown;
            return box;
        }

        private static string FormatTimeForBox(DateTime dt)
            => dt.Hour.ToString("00", CultureInfo.InvariantCulture) + ":"
             + dt.Minute.ToString("00", CultureInfo.InvariantCulture);

        private void ApplyTimeFilterSettings()
        {
            EnableTimeFilter1 = cbTimeFilter1?.IsChecked == true;
            EnableTimeFilter2 = cbTimeFilter2?.IsChecked == true;
            EnableTimeFilter3 = cbTimeFilter3?.IsChecked == true;
            if (TryParseTimeBox(tbStartTime1?.Text, out DateTime s1)) StartTime1 = s1;
            if (TryParseTimeBox(tbEndTime1?.Text,   out DateTime e1)) EndTime1   = e1;
            if (TryParseTimeBox(tbStartTime2?.Text, out DateTime s2)) StartTime2 = s2;
            if (TryParseTimeBox(tbEndTime2?.Text,   out DateTime e2)) EndTime2   = e2;
            if (TryParseTimeBox(tbStartTime3?.Text, out DateTime s3)) StartTime3 = s3;
            if (TryParseTimeBox(tbEndTime3?.Text,   out DateTime e3)) EndTime3   = e3;
            lastWithinTradingWindow = IsWithinTradingWindow();
            RefreshTimeFilterStatus();
            RefreshArmButtons();
            Print("Time filter applied — Window 1: " + (EnableTimeFilter1 ? FormatTimeForBox(StartTime1) + "–" + FormatTimeForBox(EndTime1) : "off")
                + "  Window 2: " + (EnableTimeFilter2 ? FormatTimeForBox(StartTime2) + "–" + FormatTimeForBox(EndTime2) : "off")
                + "  Window 3: " + (EnableTimeFilter3 ? FormatTimeForBox(StartTime3) + "–" + FormatTimeForBox(EndTime3) : "off"));
        }

        private static bool TryParseTimeBox(string text, out DateTime result)
        {
            result = DateTime.MinValue;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string[] parts = text.Trim().Split(':');
            if (parts.Length < 2) return false;
            if (!int.TryParse(parts[0], out int h) || !int.TryParse(parts[1], out int m)) return false;
            if (h < 0 || h > 23 || m < 0 || m > 59) return false;
            result = DateTime.Today.AddHours(h).AddMinutes(m);
            return true;
        }

        private bool IsWithinTradingWindow()
        {
            if (CurrentBar < 0) return true;
            int t = ToTime(Time[0]);
            bool w1 = EnableTimeFilter1 && t >= ToTime(StartTime1) && t < ToTime(EndTime1);
            bool w2 = EnableTimeFilter2 && t >= ToTime(StartTime2) && t < ToTime(EndTime2);
            bool w3 = EnableTimeFilter3 && t >= ToTime(StartTime3) && t < ToTime(EndTime3);
            bool anyEnabled = EnableTimeFilter1 || EnableTimeFilter2 || EnableTimeFilter3;
            return !anyEnabled || w1 || w2 || w3;
        }

        private bool IsTimeFilterActive()
            => EnableTimeFilter1 || EnableTimeFilter2 || EnableTimeFilter3;

        private static string FormatToTime(int t)
        {
            int h = t / 10000;
            int m = (t / 100) % 100;
            return h.ToString("00", CultureInfo.InvariantCulture) + ":"
                 + m.ToString("00", CultureInfo.InvariantCulture);
        }

        private string SummarizeEnabledTimeWindows()
        {
            if (!IsTimeFilterActive()) return "filter off (all windows disabled)";
            var parts = new List<string>();
            parts.Add(EnableTimeFilter1
                ? "W1 " + FormatTimeForBox(StartTime1) + "–" + FormatTimeForBox(EndTime1)
                : "W1 off");
            parts.Add(EnableTimeFilter2
                ? "W2 " + FormatTimeForBox(StartTime2) + "–" + FormatTimeForBox(EndTime2)
                : "W2 off");
            parts.Add(EnableTimeFilter3
                ? "W3 " + FormatTimeForBox(StartTime3) + "–" + FormatTimeForBox(EndTime3)
                : "W3 off");
            return string.Join(", ", parts);
        }

        private string BuildTimeFilterLogContext()
        {
            string barTime = CurrentBar >= 0 ? FormatToTime(ToTime(Time[0])) : "n/a";
            return "bar time " + barTime + " | enabled windows: " + SummarizeEnabledTimeWindows();
        }

        private string BuildTimeFilterSkipMessage(string signalLabel, int bar)
            => "Arm " + signalLabel + " ▶ skipped — TIME FILTER: signal on bar " + bar
             + " is outside all enabled windows | " + BuildTimeFilterLogContext();

        private void RefreshTimeFilterStatus()
        {
            if (txTimeFilterStatus == null) return;
            bool open = IsWithinTradingWindow();
            bool anyEnabled = EnableTimeFilter1 || EnableTimeFilter2 || EnableTimeFilter3;
            if (!anyEnabled)
            {
                txTimeFilterStatus.Text       = "FILTER OFF";
                txTimeFilterStatus.Foreground = new SolidColorBrush(C_MUTED);
                return;
            }
            txTimeFilterStatus.Text       = open ? "WINDOW OPEN" : "OUTSIDE WINDOW";
            txTimeFilterStatus.Foreground = new SolidColorBrush(open ? C_GREEN : C_AMBER);
            txTimeFilterStatus.ToolTip    = SummarizeEnabledTimeWindows();
        }

        private void BuildOppositeSignalContent(StackPanel sp)
        {
            var card = Card();
            var inner = new StackPanel();
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            btnOppOff     = ModeBtn("Off");
            btnOppClose   = ModeBtn("Close");
            btnOppReverse = ModeBtn("Reverse");
            btnOppOff.Click     += (s, e) => { OppSignalMode = OppositeSignalMode.Off;     RefreshOppButtons(); };
            btnOppClose.Click   += (s, e) => { OppSignalMode = OppositeSignalMode.Close;   RefreshOppButtons(); };
            btnOppReverse.Click += (s, e) => { OppSignalMode = OppositeSignalMode.Reverse; RefreshOppButtons(); };

            Grid.SetColumn(btnOppOff,     0);
            Grid.SetColumn(btnOppClose,   1);
            Grid.SetColumn(btnOppReverse, 2);
            row.Children.Add(btnOppOff);
            row.Children.Add(btnOppClose);
            row.Children.Add(btnOppReverse);
            inner.Children.Add(row);
            card.Child = inner;
            sp.Children.Add(card);
        }

        private void BuildAfterEntryContent(StackPanel sp)
        {
            var card = Card();
            var inner = new StackPanel();

            var row1 = new Grid { Margin = new Thickness(0, 0, 0, 2) };
            row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            btnReArmDisarm = ModeBtn("Disarm");
            btnReArmReArm  = ModeBtn("ReArm");
            btnReArmDisarm.Click += (s, e) => { ReArmAfterEntry = ReArmMode.Disarm; RefreshReArmButtons(); };
            btnReArmReArm.Click  += (s, e) => { ReArmAfterEntry = ReArmMode.ReArm;  RefreshReArmButtons(); };
            Grid.SetColumn(btnReArmDisarm, 0);
            Grid.SetColumn(btnReArmReArm,  1);
            row1.Children.Add(btnReArmDisarm);
            row1.Children.Add(btnReArmReArm);

            var row2 = new Grid();
            row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            btnReArmMQB = ModeBtn("ReArm MQB");
            btnReArmMQS = ModeBtn("ReArm MQS");
            btnReArmMQB.Click += (s, e) => { ReArmAfterEntry = ReArmMode.ReArmMQB; RefreshReArmButtons(); };
            btnReArmMQS.Click += (s, e) => { ReArmAfterEntry = ReArmMode.ReArmMQS; RefreshReArmButtons(); };
            Grid.SetColumn(btnReArmMQB, 0);
            Grid.SetColumn(btnReArmMQS, 1);
            row2.Children.Add(btnReArmMQB);
            row2.Children.Add(btnReArmMQS);

            inner.Children.Add(row1);
            inner.Children.Add(row2);
            card.Child = inner;
            sp.Children.Add(card);
        }

        private void BuildFlattenButton()
        {
            var flattenBtn = new Button
            {
                Content         = "⬛  FLATTEN EVERYTHING",
                FontSize        = 12,
                FontWeight      = FontWeights.Bold,
                Height          = ButtonHeight + 4,
                Margin          = new Thickness(6, 4, 6, 3),
                Background      = new SolidColorBrush(Color.FromArgb(70, C_RED.R, C_RED.G, C_RED.B)),
                Foreground      = new SolidColorBrush(C_RED),
                BorderBrush     = new SolidColorBrush(C_RED),
                BorderThickness = new Thickness(2),
                Cursor          = Cursors.Hand,
            };
            flattenBtn.Click += (s, e) => FlattenEverythingAllAccounts();
            hudStack.Children.Add(flattenBtn);
        }

        private void BuildTradeMgmtContent(StackPanel sp)
        {
            // Nested collapsible sub-sections — indented, slightly smaller labels
            BuildNestedSection(sp, "PROFIT TRAILING", BuildProfitTrailingContent, ref spProfitTrailing, () => SecProfitTrailingOpen, v => SecProfitTrailingOpen = v);
            BuildNestedSection(sp, "BREAKEVEN",      BuildBreakevenContent,   ref spBreakeven,   () => SecBreakevenOpen,   v => SecBreakevenOpen   = v);
            BuildNestedSection(sp, "TARGETS",        BuildTargetsContent,     ref spTargets,     () => SecTargetsOpen,     v => SecTargetsOpen     = v);
            BuildNestedSection(sp, "BRACKET / STOP", BuildBracketStopContent, ref spBracketStop, () => SecBracketStopOpen, v => SecBracketStopOpen = v);
            BuildNestedSection(sp, "SIZE",           BuildSizeContent,        ref spSize,        () => SecSizeOpen,        v => SecSizeOpen        = v);
        }

        // V1.2: Nested collapsible section — indented, dimmer chevron/label to
        // visually distinguish sub-sections from top-level sections.
        private void BuildNestedSection(
            StackPanel parent,
            string label,
            Action<StackPanel> contentBuilder,
            ref StackPanel contentPanel,
            Func<bool> getOpen,
            Action<bool> setOpen)
        {
            bool isOpen = getOpen();

            // Header row — indented by 16px left margin
            var headerGrid = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
                Margin     = new Thickness(22, 3, 6, 0),
                Cursor     = Cursors.Hand,
            };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition());

            // Slightly smaller chevron, C_MUTED color to distinguish from top-level
            var chevron = Tx(isOpen ? "▼" : "▶", 7, C_MUTED);
            chevron.Margin = new Thickness(0, 0, 5, 0);
            chevron.VerticalAlignment = VerticalAlignment.Center;

            // Slightly smaller label, C_LABEL color, bold for readability
            var lbl = Tx(label, 8, C_LABEL, bold: true);
            lbl.VerticalAlignment = VerticalAlignment.Center;

            var line = new Border
            {
                BorderBrush       = new SolidColorBrush(C_BORDER),
                BorderThickness   = new Thickness(0, 1, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(6, 0, 0, 0),
                Opacity           = 0.5,
            };

            Grid.SetColumn(chevron, 0);
            Grid.SetColumn(lbl,     1);
            Grid.SetColumn(line,    2);
            headerGrid.Children.Add(chevron);
            headerGrid.Children.Add(lbl);
            headerGrid.Children.Add(line);
            parent.Children.Add(headerGrid);

            var sp = new StackPanel
            {
                Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed,
                Margin     = new Thickness(10, 0, 0, 0),  // indent content
            };
            contentBuilder(sp);
            parent.Children.Add(sp);
            contentPanel = sp;

            headerGrid.MouseLeftButtonUp += (s, e) =>
            {
                bool nowOpen   = sp.Visibility != Visibility.Visible;
                sp.Visibility  = nowOpen ? Visibility.Visible : Visibility.Collapsed;
                chevron.Text   = nowOpen ? "▼" : "▶";
                setOpen(nowOpen);
            };
        }


        // ─────────────────────────────────────────────────────────────────────
        // PROFIT TRAILING — UI builder
        // ─────────────────────────────────────────────────────────────────────
        private void BuildProfitTrailingContent(StackPanel sp)
        {
            var card = Card();
            var inner = new StackPanel();

            // ── Mode buttons — 3 columns, 3 rows ─────────────────────────────
            inner.Children.Add(BoldLabel("TRAIL MODE"));

            var row1 = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            var row2 = new Grid { Margin = new Thickness(0, 0, 0, 2) };
            var row3 = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            for (int i = 0; i < 3; i++)
            {
                row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row3.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            btnTrailTicks  = TrailBtn("Trail Ticks",  TrailMode.TrailTicks);
            btnBEPlus      = TrailBtn("BE +N",         TrailMode.BreakevenPlus);
            btnBarLH       = TrailBtn("Bar L/H",       TrailMode.BarLowHigh);
            btnNBarLH      = TrailBtn("N-Bar L/H",     TrailMode.NBarLowHigh);
            btnTrailATR    = TrailBtn("Trail ATR",     TrailMode.TrailATR);
            btnTrendMagic  = TrailBtn("Trend Magic",   TrailMode.TrendMagic);
            btnHalfBE      = TrailBtn("Half + BE",     TrailMode.HalfPlusBE);

            Grid.SetColumn(btnTrailTicks, 0); Grid.SetColumn(btnBEPlus,   1); Grid.SetColumn(btnBarLH,      2);
            Grid.SetColumn(btnNBarLH,     0); Grid.SetColumn(btnTrailATR, 1); Grid.SetColumn(btnTrendMagic, 2);
            Grid.SetColumn(btnHalfBE,     0);

            row1.Children.Add(btnTrailTicks); row1.Children.Add(btnBEPlus);   row1.Children.Add(btnBarLH);
            row2.Children.Add(btnNBarLH);     row2.Children.Add(btnTrailATR); row2.Children.Add(btnTrendMagic);
            row3.Children.Add(btnHalfBE);

            inner.Children.Add(row1);
            inner.Children.Add(row2);
            inner.Children.Add(row3);

            // ── Parameter rows ────────────────────────────────────────────────
            inner.Children.Add(new Border
            {
                BorderBrush     = new SolidColorBrush(C_BORDER),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Margin          = new Thickness(0, 2, 0, 6),
            });
            inner.Children.Add(BoldLabel("PARAMETERS"));

            rowTrailTicks  = TrailParamRow("Trail Ticks",    TrailTicks.ToString(),        out tbTrailTicks);
            rowBETrigger   = TrailParamRow("BE Trigger",     BETriggerTicks.ToString(),    out tbBETrigger);
            rowBEBuffer    = TrailParamRow("BE Buffer",      BEBufferTicks.ToString(),     out tbBEBuffer);
            rowBarLookback = TrailParamRow("Bar Lookback",   TrailBarLookback.ToString(),  out tbBarLookback);
            rowATRPeriod   = TrailParamRow("ATR Period",     TrailATRPeriod.ToString(),    out tbATRPeriod);
            rowATRMult     = TrailParamRow("ATR Mult",       TrailATRMult.ToString("F1"),  out tbATRMult);
            rowHalfTrigger = TrailParamRow("Half Trigger",   HalfTriggerTicks.ToString(),  out tbHalfTrigger);
            rowHalfBuffer  = TrailParamRow("Half Buffer",    HalfBufferTicks.ToString(),   out tbHalfBuffer);

            inner.Children.Add(rowTrailTicks);
            inner.Children.Add(rowBETrigger);
            inner.Children.Add(rowBEBuffer);
            inner.Children.Add(rowBarLookback);
            inner.Children.Add(rowATRPeriod);
            inner.Children.Add(rowATRMult);
            inner.Children.Add(rowHalfTrigger);
            inner.Children.Add(rowHalfBuffer);

            // V1.6: AUTO TRAIL toggle button
            var autoTrailRow = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            autoTrailRow.ColumnDefinitions.Add(new ColumnDefinition());
            btnAutoTrail = new Button
            {
                Content         = "AUTO TRAIL: OFF",
                Height          = ButtonHeight,
                FontSize        = 10,
                FontWeight      = FontWeights.Bold,
                FontFamily      = new FontFamily("Consolas, Courier New"),
                Margin          = new Thickness(2),
                Background      = new SolidColorBrush(C_DIM),
                Foreground      = new SolidColorBrush(C_MUTED),
                BorderBrush     = new SolidColorBrush(C_BORDER),
                BorderThickness = new Thickness(1),
                Cursor          = Cursors.Hand,
                ToolTip         = "When ON: activates the selected trail mode automatically on arm entry fill",
            };
            btnAutoTrail.Click += (s, e) =>
            {
                autoTrailEnabled   = !autoTrailEnabled;
                autoTrailActivated = false;
                RefreshAutoTrailButton();
                Print("Auto-trail ▶ " + (autoTrailEnabled ? "ON" : "OFF"));
            };
            Grid.SetColumn(btnAutoTrail, 0);
            autoTrailRow.Children.Add(btnAutoTrail);
            inner.Children.Add(autoTrailRow);

            card.Child = inner;
            sp.Children.Add(card);

            RefreshTrailButtons();
            RefreshTrailParams();
            RefreshAutoTrailButton();
        }

        private void RefreshAutoTrailButton()
        {
            if (btnAutoTrail == null) return;
            if (autoTrailEnabled)
            {
                btnAutoTrail.Background      = new SolidColorBrush(Color.FromArgb(60, C_AMBER.R, C_AMBER.G, C_AMBER.B));
                btnAutoTrail.Foreground      = new SolidColorBrush(C_AMBER);
                btnAutoTrail.BorderBrush     = new SolidColorBrush(C_AMBER);
                btnAutoTrail.BorderThickness = new Thickness(2);
                btnAutoTrail.Content         = "AUTO TRAIL: ON";
            }
            else
            {
                btnAutoTrail.Background      = new SolidColorBrush(C_DIM);
                btnAutoTrail.Foreground      = new SolidColorBrush(C_MUTED);
                btnAutoTrail.BorderBrush     = new SolidColorBrush(C_BORDER);
                btnAutoTrail.BorderThickness = new Thickness(1);
                btnAutoTrail.Content         = "AUTO TRAIL: OFF";
            }
        }

        // Bold section sub-label
        private TextBlock BoldLabel(string text)
            => new TextBlock
            {
                Text       = text,
                FontSize   = 9,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(C_LABEL),
                FontFamily = new FontFamily("Consolas, Courier New"),
                Margin     = new Thickness(0, 0, 0, 3),
            };

        // Trail mode toggle button factory
        private Button TrailBtn(string label, TrailMode mode)
        {
            var b = new Button
            {
                Content         = label,
                Height          = ButtonHeight,
                FontSize        = 10,
                FontWeight      = FontWeights.Bold,
                FontFamily      = new FontFamily("Consolas, Courier New"),
                Margin          = new Thickness(2),
                Background      = new SolidColorBrush(C_DIM),
                Foreground      = new SolidColorBrush(C_MUTED),
                BorderBrush     = new SolidColorBrush(C_BORDER),
                BorderThickness = new Thickness(1),
                Cursor          = Cursors.Hand,
            };
            b.Click += (s, e) =>
            {
                if (activeTrailMode == mode)
                {
                    // Toggle off
                    activeTrailMode  = TrailMode.None;
                    trailStopLevel   = double.MinValue;
                    beTriggered      = false;
                    halfTriggered    = false;
                    Print("Trail ▶ deactivated");
                }
                else
                {
                    activeTrailMode  = mode;
                    trailStopLevel   = double.MinValue;
                    beTriggered      = false;
                    halfTriggered    = false;
                    ReadTrailParams();
                    Print("Trail ▶ activated: " + mode);
                }
                RefreshTrailButtons();
                RefreshTrailParams();
            };
            return b;
        }

        // Parameter label + text box row
        private Border TrailParamRow(string label, string defaultVal, out TextBox tb)
        {
            var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var lbl = new TextBlock
            {
                Text              = label,
                FontSize          = 10,
                FontWeight        = FontWeights.Bold,
                Foreground        = new SolidColorBrush(C_LABEL),
                FontFamily        = new FontFamily("Consolas, Courier New"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(0, 0, 4, 0),
            };

            var box = new TextBox
            {
                Text                    = defaultVal,
                FontSize                = 10,
                Height                  = ButtonHeight - 2,
                Background              = new SolidColorBrush(C_DIM),
                Foreground              = new SolidColorBrush(C_TEXT),
                BorderBrush             = new SolidColorBrush(C_BORDER),
                BorderThickness         = new Thickness(1),
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding                 = new Thickness(4, 0, 4, 0),
                FontFamily              = new FontFamily("Consolas, Courier New"),
            };
            box.PreviewKeyDown += TrailTextBox_PreviewKeyDown;

            Grid.SetColumn(lbl, 0); Grid.SetColumn(box, 1);
            g.Children.Add(lbl); g.Children.Add(box);
            tb = box;

            return new Border { Child = g };
        }

        private void TrailTextBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            var tb = sender as TextBox;
            if (tb == null) return;
            if (e.Key == Key.Tab || e.Key == Key.Escape) return;
            if (e.Key == Key.Enter || e.Key == Key.Return) { Keyboard.ClearFocus(); e.Handled = true; return; }
            e.Handled = true;
            if (e.Key == Key.Left)  { if (tb.CaretIndex > 0) tb.CaretIndex--; return; }
            if (e.Key == Key.Right) { if (tb.CaretIndex < tb.Text.Length) tb.CaretIndex++; return; }
            if (e.Key == Key.Home)  { tb.CaretIndex = 0; return; }
            if (e.Key == Key.End)   { tb.CaretIndex = tb.Text.Length; return; }
            if (e.Key == Key.Back)
            {
                if (tb.SelectionLength > 0) { int s = tb.SelectionStart; tb.Text = tb.Text.Remove(s, tb.SelectionLength); tb.CaretIndex = s; }
                else if (tb.CaretIndex > 0) { int i = tb.CaretIndex; tb.Text = tb.Text.Remove(i-1, 1); tb.CaretIndex = i-1; }
                return;
            }
            if (e.Key == Key.Delete)
            {
                if (tb.SelectionLength > 0) { int s = tb.SelectionStart; tb.Text = tb.Text.Remove(s, tb.SelectionLength); tb.CaretIndex = s; }
                else if (tb.CaretIndex < tb.Text.Length) tb.Text = tb.Text.Remove(tb.CaretIndex, 1);
                return;
            }
            if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control) { tb.SelectAll(); return; }
            // Numeric + decimal + minus
            string ch = null;
            if (e.Key >= Key.D0 && e.Key <= Key.D9) ch = ((char)('0' + (e.Key - Key.D0))).ToString();
            else if (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9) ch = ((char)('0' + (e.Key - Key.NumPad0))).ToString();
            else if (e.Key == Key.OemPeriod || e.Key == Key.Decimal) ch = ".";
            else if (e.Key == Key.OemMinus  || e.Key == Key.Subtract) ch = "-";
            if (ch != null)
            {
                if (tb.SelectionLength > 0) { int s = tb.SelectionStart; tb.Text = tb.Text.Remove(s, tb.SelectionLength); tb.CaretIndex = s; }
                int pos = tb.CaretIndex; tb.Text = tb.Text.Insert(pos, ch); tb.CaretIndex = pos + ch.Length;
            }
        }

        // Read current text box values back into properties
        private void ReadTrailParams()
        {
            if (tbTrailTicks  != null && int.TryParse(tbTrailTicks.Text,    out int tt))  TrailTicks        = tt;
            if (tbBETrigger   != null && int.TryParse(tbBETrigger.Text,     out int bet)) BETriggerTicks    = bet;
            if (tbBEBuffer    != null && int.TryParse(tbBEBuffer.Text,      out int beb)) BEBufferTicks     = beb;
            if (tbBarLookback != null && int.TryParse(tbBarLookback.Text,   out int bl))  TrailBarLookback  = bl;
            if (tbATRPeriod   != null && int.TryParse(tbATRPeriod.Text,     out int ap))  TrailATRPeriod    = ap;
            if (tbATRMult     != null && double.TryParse(tbATRMult.Text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out double am)) TrailATRMult = am;
            if (tbHalfTrigger != null && int.TryParse(tbHalfTrigger.Text,   out int ht))  HalfTriggerTicks  = ht;
            if (tbHalfBuffer  != null && int.TryParse(tbHalfBuffer.Text,    out int hb))  HalfBufferTicks   = hb;
        }

        // Show/hide parameter rows based on active trail mode
        private void RefreshTrailParams()
        {
            if (rowTrailTicks  == null) return;
            bool isTT  = activeTrailMode == TrailMode.TrailTicks;
            bool isBE  = activeTrailMode == TrailMode.BreakevenPlus;
            bool isNB  = activeTrailMode == TrailMode.NBarLowHigh;
            bool isATR = activeTrailMode == TrailMode.TrailATR || activeTrailMode == TrailMode.TrendMagic;
            bool isH   = activeTrailMode == TrailMode.HalfPlusBE;

            rowTrailTicks .Visibility = (isTT)        ? Visibility.Visible : Visibility.Collapsed;
            rowBETrigger  .Visibility = (isBE || isH) ? Visibility.Visible : Visibility.Collapsed;
            rowBEBuffer   .Visibility = (isBE || isH) ? Visibility.Visible : Visibility.Collapsed;
            rowBarLookback.Visibility = (isNB)        ? Visibility.Visible : Visibility.Collapsed;
            rowATRPeriod  .Visibility = (isATR)       ? Visibility.Visible : Visibility.Collapsed;
            rowATRMult    .Visibility = (isATR)       ? Visibility.Visible : Visibility.Collapsed;
            rowHalfTrigger.Visibility = (isH)         ? Visibility.Visible : Visibility.Collapsed;
            rowHalfBuffer .Visibility = (isH)         ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RefreshTrailButtons()
        {
            SetTrailBtn(btnTrailTicks,  activeTrailMode == TrailMode.TrailTicks,    C_AMBER);
            SetTrailBtn(btnBEPlus,      activeTrailMode == TrailMode.BreakevenPlus, C_AMBER);
            SetTrailBtn(btnBarLH,       activeTrailMode == TrailMode.BarLowHigh,    C_BLUE);
            SetTrailBtn(btnNBarLH,      activeTrailMode == TrailMode.NBarLowHigh,   C_BLUE);
            SetTrailBtn(btnTrailATR,    activeTrailMode == TrailMode.TrailATR,      C_PURPLE);
            SetTrailBtn(btnTrendMagic,  activeTrailMode == TrailMode.TrendMagic,    C_PURPLE);
            SetTrailBtn(btnHalfBE,      activeTrailMode == TrailMode.HalfPlusBE,    C_GREEN);
        }

        private void SetTrailBtn(Button btn, bool active, Color activeColor)
        {
            if (btn == null) return;
            btn.Background      = new SolidColorBrush(active ? Color.FromArgb(60, activeColor.R, activeColor.G, activeColor.B) : C_DIM);
            btn.Foreground      = new SolidColorBrush(active ? activeColor : C_MUTED);
            btn.BorderBrush     = new SolidColorBrush(active ? activeColor : C_BORDER);
            btn.BorderThickness = new Thickness(active ? 2 : 1);
        }

        private void BuildBreakevenContent(StackPanel sp)
        {
            var card = Card();
            card.Child = TwoColBtns(
                ActionBtn($"BE + {Breakeven1PlusTicks}", C_AMBER, () => StopsToBreakeven(Breakeven1PlusTicks)),
                ActionBtn($"BE + {Breakeven2PlusTicks}", C_AMBER, () => StopsToBreakeven(Breakeven2PlusTicks))
            );
            sp.Children.Add(card);
        }

        private void BuildTargetsContent(StackPanel sp)
        {
            var card = Card();
            card.Child = TwoColBtns(
                ActionBtn($"Price + {PricePlusTicks}", C_BLUE, () => TargetToPricePlus(PricePlusTicks)),
                ActionBtn($"Entry + {EntryPlusTicks}", C_BLUE, () => TargetToEntryPlus(EntryPlusTicks))
            );
            sp.Children.Add(card);
        }

        // V1.6: BRACKET / STOP row with inline stop+target ticks and 3-state toggle
        private void BuildBracketStopContent(StackPanel sp)
        {
            var card  = Card();
            var outer = new StackPanel();

            // ── Row 1: Bracket / Add Stop buttons ────────────────────────────
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var btnBracket = ActionBtn("Bracket", C_PURPLE, () =>
                BracketOrder(ReadBracketStopTicksFromPanel(), ReadAutoTargetTicksFromPanel()));
            var btnAddStop = ActionBtn("Add Stop", C_PURPLE, () =>
                AddStopOrder(ReadBracketStopTicksFromPanel()));

            tbBracketStopTicks = new TextBox
            {
                Text                       = BracketStopTicks.ToString(CultureInfo.InvariantCulture),
                Width                      = 38,
                FontSize                   = 10,
                Height                     = ButtonHeight - 2,
                Margin                     = new Thickness(4, 2, 1, 2),
                Background                 = new SolidColorBrush(C_DIM),
                Foreground                 = new SolidColorBrush(C_TEXT),
                BorderBrush                = new SolidColorBrush(C_BORDER),
                BorderThickness            = new Thickness(1),
                VerticalContentAlignment   = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Padding                    = new Thickness(2, 0, 2, 0),
                FontFamily                 = new FontFamily("Consolas, Courier New"),
                ToolTip                    = "Stop ticks — Add Stop, Bracket SL, auto-exit",
            };
            tbBracketStopTicks.PreviewKeyDown += TrailTextBox_PreviewKeyDown;
            tbBracketStopTicks.LostFocus += (s, e) => SyncBracketStopTicksFromPanel(logChange: true);

            // V1.6: Profit target ticks inline box
            tbAutoTargetTicks = new TextBox
            {
                Text                       = AutoTargetTicks.ToString(CultureInfo.InvariantCulture),
                Width                      = 38,
                FontSize                   = 10,
                Height                     = ButtonHeight - 2,
                Margin                     = new Thickness(1, 2, 4, 2),
                Background                 = new SolidColorBrush(C_DIM),
                Foreground                 = new SolidColorBrush(C_TEXT),
                BorderBrush                = new SolidColorBrush(C_BORDER),
                BorderThickness            = new Thickness(1),
                VerticalContentAlignment   = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Padding                    = new Thickness(2, 0, 2, 0),
                FontFamily                 = new FontFamily("Consolas, Courier New"),
                ToolTip                    = "Target ticks — Bracket TP, auto-bracket target",
            };
            tbAutoTargetTicks.PreviewKeyDown += TrailTextBox_PreviewKeyDown;
            tbAutoTargetTicks.LostFocus += (s, e) =>
            {
                int prev = AutoTargetTicks;
                int t    = ReadAutoTargetTicksFromPanel();
                if (t != prev) Print("Target ticks ▶ " + t);
            };

            autoStopToggleHost = AutoStopToggleHost();

            Grid.SetColumn(btnBracket,         0);
            Grid.SetColumn(btnAddStop,         1);
            Grid.SetColumn(tbBracketStopTicks, 2);
            Grid.SetColumn(tbAutoTargetTicks,  3);
            Grid.SetColumn(autoStopToggleHost, 4);
            g.Children.Add(btnBracket);
            g.Children.Add(btnAddStop);
            g.Children.Add(tbBracketStopTicks);
            g.Children.Add(tbAutoTargetTicks);
            g.Children.Add(autoStopToggleHost);

            outer.Children.Add(g);
            card.Child = outer;
            sp.Children.Add(card);
            RefreshAutoStopToggle();
        }

        private int ReadBracketStopTicksFromPanel()
        {
            if (tbBracketStopTicks != null
                && int.TryParse(tbBracketStopTicks.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int t)
                && t > 0)
            {
                BracketStopTicks = t;
                return t;
            }
            return Math.Max(1, BracketStopTicks);
        }

        private void SyncBracketStopTicksFromPanel(bool logChange)
        {
            int prev = BracketStopTicks;
            int ticks = ReadBracketStopTicksFromPanel();
            if (tbBracketStopTicks != null)
                tbBracketStopTicks.Text = ticks.ToString(CultureInfo.InvariantCulture);
            RefreshAutoStopToggle();
            if (logChange && ticks != prev)
                Print("Stop ticks ▶ " + ticks + " (Add Stop / Bracket SL / auto-stop)");
        }

        // V1.6: 3-state cycle toggle — Off (○) → Stop Only (●) → Bracket (◉) → Off
        private Border AutoStopToggleHost()
        {
            autoStopToggleDot = new System.Windows.Shapes.Ellipse
            {
                Width               = 14,
                Height              = 14,
                Stroke              = new SolidColorBrush(C_BORDER),
                StrokeThickness     = 2,
                Fill                = Brushes.Transparent,
                Cursor              = Cursors.Hand,
                VerticalAlignment   = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var host = new Border
            {
                Width           = ButtonHeight + 4,
                Margin          = new Thickness(4, 2, 2, 2),
                Background      = new SolidColorBrush(C_DIM),
                BorderBrush     = new SolidColorBrush(C_BORDER),
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(ButtonHeight),
                Cursor          = Cursors.Hand,
                Child           = autoStopToggleDot,
            };
            host.MouseLeftButtonUp += (s, e) =>
            {
                // Cycle: None → StopOnly → Bracket → None
                AutoEntryExitMode = AutoEntryExitMode == AutoEntryMode.None    ? AutoEntryMode.StopOnly
                                  : AutoEntryExitMode == AutoEntryMode.StopOnly ? AutoEntryMode.Bracket
                                  : AutoEntryMode.None;
                RefreshAutoStopToggle();
                Print("Auto-exit mode ▶ " + AutoEntryExitMode);
            };
            return host;
        }

        private void RefreshAutoStopToggle()
        {
            if (autoStopToggleDot == null) return;
            switch (AutoEntryExitMode)
            {
                case AutoEntryMode.None:
                    autoStopToggleDot.Fill           = Brushes.Transparent;
                    autoStopToggleDot.Stroke         = new SolidColorBrush(C_MUTED);
                    autoStopToggleDot.StrokeThickness = 2;
                    if (autoStopToggleHost != null)
                    { autoStopToggleHost.BorderBrush = new SolidColorBrush(C_BORDER); autoStopToggleHost.ToolTip = "Auto-exit: OFF — click to enable Stop Only"; }
                    break;
                case AutoEntryMode.StopOnly:
                    autoStopToggleDot.Fill           = new SolidColorBrush(C_GREEN);
                    autoStopToggleDot.Stroke         = new SolidColorBrush(C_GREEN);
                    autoStopToggleDot.StrokeThickness = 0;
                    if (autoStopToggleHost != null)
                    { autoStopToggleHost.BorderBrush = new SolidColorBrush(C_GREEN); autoStopToggleHost.ToolTip = "Auto-exit: STOP ONLY (●) — click for Bracket"; }
                    break;
                case AutoEntryMode.Bracket:
                    autoStopToggleDot.Fill           = new SolidColorBrush(C_PURPLE);
                    autoStopToggleDot.Stroke         = new SolidColorBrush(C_PURPLE);
                    autoStopToggleDot.StrokeThickness = 0;
                    if (autoStopToggleHost != null)
                    { autoStopToggleHost.BorderBrush = new SolidColorBrush(C_PURPLE); autoStopToggleHost.ToolTip = "Auto-exit: BRACKET (◉) — click to turn OFF"; }
                    break;
            }
        }

        private void BuildSizeContent(StackPanel sp)
        {
            var card = Card();
            var inner = new StackPanel();
            inner.Children.Add(TwoColBtns(
                ActionBtn("Half",   Color.FromRgb(155, 75,  0), () => RemoveHalfPosition()),
                ActionBtn("Double", Color.FromRgb(0,  95, 155), () => DoublePosition())
            ));
            inner.Children.Add(new Border { Height = 2 });
            inner.Children.Add(TwoColBtns(
                ActionBtn("Naked", Color.FromRgb(60, 60, 60), () => RemoveStopsAndTargets()),
                ActionBtn("Split", Color.FromRgb(60, 60, 60), () => SplitStopsAndTargets())
            ));
            card.Child = inner;
            sp.Children.Add(card);
        }

        // ═════════════════════════════════════════════════════════════════════
        // Visual refresh helpers
        // ═════════════════════════════════════════════════════════════════════
        private void RefreshArmButtons()
        {
            bool outsideWindow = !IsWithinTradingWindow() && (EnableTimeFilter1 || EnableTimeFilter2 || EnableTimeFilter3);
            if (btnArmMQB != null)
            {
                Color border = mqbArmed ? (outsideWindow ? C_AMBER : C_GREEN) : C_BORDER;
                btnArmMQB.Background      = new SolidColorBrush(mqbArmed ? Color.FromArgb(80, C_GREEN.R, C_GREEN.G, C_GREEN.B) : Color.FromArgb(40, C_DIM.R, C_DIM.G, C_DIM.B));
                btnArmMQB.Foreground      = new SolidColorBrush(mqbArmed ? C_GREEN : C_MUTED);
                btnArmMQB.BorderBrush     = new SolidColorBrush(border);
                btnArmMQB.BorderThickness = new Thickness(mqbArmed ? 2 : 1);
                btnArmMQB.Content         = mqbArmed ? "■ ARMED BULL ▲" : "▲  ARM BULL";
            }
            if (btnArmMQS != null)
            {
                Color border = mqsArmed ? (outsideWindow ? C_AMBER : C_RED) : C_BORDER;
                btnArmMQS.Background      = new SolidColorBrush(mqsArmed ? Color.FromArgb(80, C_RED.R, C_RED.G, C_RED.B) : Color.FromArgb(40, C_DIM.R, C_DIM.G, C_DIM.B));
                btnArmMQS.Foreground      = new SolidColorBrush(mqsArmed ? C_RED : C_MUTED);
                btnArmMQS.BorderBrush     = new SolidColorBrush(border);
                btnArmMQS.BorderThickness = new Thickness(mqsArmed ? 2 : 1);
                btnArmMQS.Content         = mqsArmed ? "■ ARMED BEAR ▼" : "▼  ARM BEAR";
            }
        }

        private void RefreshOppButtons()
        {
            SetModeBtn(btnOppOff,     OppSignalMode == OppositeSignalMode.Off,     C_MUTED);
            SetModeBtn(btnOppClose,   OppSignalMode == OppositeSignalMode.Close,   C_AMBER);
            SetModeBtn(btnOppReverse, OppSignalMode == OppositeSignalMode.Reverse, C_BLUE);
        }

        private void RefreshReArmButtons()
        {
            SetModeBtn(btnReArmDisarm, ReArmAfterEntry == ReArmMode.Disarm,   C_MUTED);
            SetModeBtn(btnReArmReArm,  ReArmAfterEntry == ReArmMode.ReArm,    C_PURPLE);
            SetModeBtn(btnReArmMQB,    ReArmAfterEntry == ReArmMode.ReArmMQB, C_GREEN);
            SetModeBtn(btnReArmMQS,    ReArmAfterEntry == ReArmMode.ReArmMQS, C_RED);
        }

        // ═════════════════════════════════════════════════════════════════════
        // WPF factory helpers
        // ═════════════════════════════════════════════════════════════════════
        private TextBlock Tx(string text, double size, Color color, bool bold = false)
            => new TextBlock
            {
                Text       = text,
                FontSize   = size,
                Foreground = new SolidColorBrush(color),
                FontFamily = new FontFamily("Consolas, Courier New"),
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            };

        private Border Card()
            => new Border
            {
                Background      = new SolidColorBrush(C_CARD),
                BorderBrush     = new SolidColorBrush(C_BORDER),
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(6),
                Padding         = new Thickness(8, 5, 8, 5),
                Margin          = new Thickness(6, 2, 6, 2),
            };

        private Border HRule(Thickness? margin = null)
            => new Border
            {
                BorderBrush     = new SolidColorBrush(C_BORDER),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Margin          = margin ?? new Thickness(0),
            };

        private Button ModeBtn(string label)
            => new Button
            {
                Content         = label,
                Height          = ButtonHeight,
                FontSize        = 10,
                FontWeight      = FontWeights.Bold,
                FontFamily      = new FontFamily("Consolas, Courier New"),
                Margin          = new Thickness(2),
                Background      = new SolidColorBrush(C_DIM),
                Foreground      = new SolidColorBrush(C_MUTED),
                BorderBrush     = new SolidColorBrush(C_BORDER),
                BorderThickness = new Thickness(1),
                Cursor          = Cursors.Hand,
            };

        private void SetModeBtn(Button btn, bool active, Color activeColor)
        {
            if (btn == null) return;
            btn.Background      = new SolidColorBrush(active ? Color.FromArgb(60, activeColor.R, activeColor.G, activeColor.B) : C_DIM);
            btn.Foreground      = new SolidColorBrush(active ? activeColor : C_MUTED);
            btn.BorderBrush     = new SolidColorBrush(active ? activeColor : C_BORDER);
            btn.BorderThickness = new Thickness(active ? 2 : 1);
        }

        private Button ActionBtn(string label, Color accentColor, Action onClick)
        {
            var b = new Button
            {
                Content         = label,
                Height          = ButtonHeight,
                FontSize        = 11,
                FontWeight      = FontWeights.Bold,
                FontFamily      = new FontFamily("Consolas, Courier New"),
                Margin          = new Thickness(2),
                Background      = new SolidColorBrush(Color.FromArgb(50, accentColor.R, accentColor.G, accentColor.B)),
                Foreground      = new SolidColorBrush(accentColor),
                BorderBrush     = new SolidColorBrush(accentColor),
                BorderThickness = new Thickness(1),
                Cursor          = Cursors.Hand,
            };
            b.Click += (s, e) => onClick();
            return b;
        }

        private Grid TwoColBtns(Button left, Button right)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(left, 0); Grid.SetColumn(right, 1);
            g.Children.Add(left); g.Children.Add(right);
            return g;
        }

        // ═════════════════════════════════════════════════════════════════════
        // Signal logic helpers (carried from V1.1)
        // ═════════════════════════════════════════════════════════════════════
        private bool HandleOppositePosition(OrderAction newAction, string label, OppositeSignalMode mode)
        {
            var acct  = ResolveAccount();    if (acct == null)  return false;
            var instr = ResolveInstrument(); if (instr == null) return false;
            var pos   = acct.Positions.FirstOrDefault(p => p.Instrument.FullName == instr.FullName);
            if (pos == null || pos.Quantity == 0) return false;
            bool isOpposite =
                (newAction == OrderAction.Buy       && pos.MarketPosition == MarketPosition.Short) ||
                (newAction == OrderAction.SellShort && pos.MarketPosition == MarketPosition.Long);
            if (!isOpposite) return false;
            switch (mode)
            {
                case OppositeSignalMode.Off:
                    Print("Arm " + label + " ▶ opposite exists, mode=Off — ignoring"); return true;
                case OppositeSignalMode.Close:
                    CloseCurrentPosition(acct, instr, pos, label);
                    Print("Arm " + label + " ▶ closed opposite (mode=Close)"); return true;
                case OppositeSignalMode.Reverse:
                    CloseCurrentPosition(acct, instr, pos, label);
                    Print("Arm " + label + " ▶ closed opposite, reversing (mode=Reverse)"); return false;
                default: return false;
            }
        }

        private void CloseCurrentPosition(NinjaTrader.Cbi.Account acct,
                                          NinjaTrader.Cbi.Instrument instr,
                                          Position pos, string label)
        {
            try
            {
                var exits = acct.Orders.Where(o => o.Instrument.FullName == instr.FullName &&
                    (o.OrderState == OrderState.Working || o.OrderState == OrderState.Accepted)).ToArray();
                if (exits.Length > 0) acct.Cancel(exits);
                OrderAction closeAction = pos.MarketPosition == MarketPosition.Long ? OrderAction.Sell : OrderAction.BuyToCover;
                SubmitPanelOrders(acct, new[] { acct.CreateOrder(instr, closeAction, OrderType.Market,
                    PanelOrderEntry, TimeInForce.Gtc, pos.Quantity, 0, 0, "",
                    Name + "_Close_" + label, Core.Globals.MaxDate, null) }, "Close_" + label);
            }
            catch (Exception ex) { Print("ClosePosition ▶ ERROR: " + ex.Message); }
        }

        private void SubmitArmEntry(OrderAction action, string signalLabel)
        {
            try
            {
                var acct = ResolveAccount();
                if (!ValidateAccountForTrading(acct, "Arm " + signalLabel, out string reason))
                { Print("Arm " + signalLabel + " ▶ " + reason); return; }
                var instr = ResolveInstrument();
                if (instr == null) { Print("Arm " + signalLabel + " ▶ no instrument"); return; }
                int qty = 0; string qtySource = "";
                ChartControl.Dispatcher.Invoke(() => { qty = ReadChartTraderQuantity(out qtySource); });
                if (qty <= 0) { Print("Arm " + signalLabel + " ▶ qty 0, skipping"); return; }
                var pos = acct.Positions.FirstOrDefault(p => p.Instrument.FullName == instr.FullName);
                if (pos != null && pos.Quantity > 0)
                {
                    bool same = (pos.MarketPosition == MarketPosition.Long  && action == OrderAction.Buy) ||
                                (pos.MarketPosition == MarketPosition.Short && action == OrderAction.SellShort);
                    if (same) { Print("Arm " + signalLabel + " ▶ already same direction, skipping"); return; }
                }
                SubmitPanelOrders(acct, new[] { acct.CreateOrder(instr, action, OrderType.Market,
                    PanelOrderEntry, TimeInForce.Gtc, qty, 0, 0, "",
                    Name + "_Arm_" + signalLabel, Core.Globals.MaxDate, null) }, "Arm " + signalLabel);
                Print("Arm " + signalLabel + " ▶ submitted " + action + " x" + qty + " (" + qtySource + ") on " + acct.Name);
            }
            catch (Exception ex) { Print("Arm " + signalLabel + " ▶ ERROR: " + ex.Message); }
        }

        // V1.0 (Sentinel): signal detection — supports all SignalSource modes
        private IDrawingTool FindChartDrawObject(string tag)
        {
            try
            {
                if (ChartControl == null) return null;

                if (SentinelSignalSource == SignalSource.TrendArchitectMQ
                    || SentinelSignalSource == SignalSource.DrawObjectTag)
                {
                    // Cache the source indicator on first lookup
                    if (cachedSignalSource == null)
                    {
                        string targetName = SentinelSignalSource == SignalSource.TrendArchitectMQ
                            ? "TrendArchitect" : "";
                        foreach (var ind in ChartControl.Indicators)
                        {
                            if (string.IsNullOrEmpty(targetName)
                                || string.Equals(ind.GetType().Name, targetName, StringComparison.Ordinal))
                            {
                                cachedSignalSource = ind;
                                break;
                            }
                        }
                    }
                    if (cachedSignalSource != null)
                    {
                        var hit = cachedSignalSource.DrawObjects[tag];
                        if (hit != null) return hit;
                    }
                    // Fallback: scan all indicators
                    foreach (var ind in ChartControl.Indicators)
                    {
                        var hit = ind.DrawObjects[tag];
                        if (hit != null) return hit;
                    }
                }
            }
            catch { }
            return null;
        }

        // V1.0 (Sentinel): check indicator series signal (IndicatorSeries mode)
        private bool CheckIndicatorSeriesSignal(bool bull)
        {
            if (SentinelSignalSource != SignalSource.IndicatorSeries) return false;
            if (string.IsNullOrWhiteSpace(SignalIndicatorName)) return false;
            try
            {
                if (ChartControl == null) return false;
                if (cachedSignalSource == null)
                {
                    foreach (var ind in ChartControl.Indicators)
                    {
                        if (string.Equals(ind.Name, SignalIndicatorName, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(ind.GetType().Name, SignalIndicatorName, StringComparison.OrdinalIgnoreCase))
                        {
                            cachedSignalSource = ind;
                            break;
                        }
                    }
                }
                if (cachedSignalSource == null) return false;
                int idx = bull ? BullSeriesIndex : BearSeriesIndex;
                if (idx >= cachedSignalSource.Values.Length) return false;
                double val = cachedSignalSource.Values[idx][0];
                return !double.IsNaN(val) && Math.Abs(val) > 1e-10;
            }
            catch { return false; }
        }

        // ═════════════════════════════════════════════════════════════════════
        // Quantity / Account / Instrument helpers (carried from V1.1 — Spoobie)
        // ═════════════════════════════════════════════════════════════════════
        private const OrderEntry PanelOrderEntry = OrderEntry.Manual;

        private static bool TryParseQuantityText(string text, out int qty)
        {
            qty = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string cleaned = text.Trim().Replace(",", "");
            if (int.TryParse(cleaned, NumberStyles.Integer, CultureInfo.InvariantCulture, out qty) && qty > 0) return true;
            if (double.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out double d) && d > 0)
            { qty = (int)Math.Round(d); return qty > 0; }
            return false;
        }

        private static int QuantityFromUpDown(QuantityUpDown sel)
        {
            if (sel == null) return 0;
            try { int v = sel.Value; return v > 0 ? v : 0; } catch { return 0; }
        }

        private void WireChartTraderQuantitySelector()
        {
            try
            {
                if (!uiPanelActive) return;
                DetachChartTraderQuantitySelector();
                QuantityUpDown found = GetChartTraderWindow()?.FindFirst("ChartTraderControlQuantitySelector") as QuantityUpDown;
                if (found == null) return;
                cachedQtySelector = found;
                lastKnownChartTraderQty = Math.Max(1, QuantityFromUpDown(found));
                cachedQtyValueChangedHandler = (s, e) =>
                {
                    if (cachedQtySelector != null)
                        lastKnownChartTraderQty = Math.Max(1, QuantityFromUpDown(cachedQtySelector));
                };
                cachedQtySelector.ValueChanged += cachedQtyValueChangedHandler;
            }
            catch (Exception ex) { Print("WireQty ▶ " + ex.Message); }
        }

        private void DetachChartTraderQuantitySelector()
        {
            try { if (cachedQtySelector != null && cachedQtyValueChangedHandler != null) cachedQtySelector.ValueChanged -= cachedQtyValueChangedHandler; }
            catch { }
            cachedQtySelector = null; cachedQtyValueChangedHandler = null;
        }

        private int ReadChartTraderQuantity(out string source)
        {
            source = "fallback";
            try
            {
                if (cachedQtySelector == null) WireChartTraderQuantitySelector();
                int fromSel = QuantityFromUpDown(cachedQtySelector ?? (GetChartTraderWindow()?.FindFirst("ChartTraderControlQuantitySelector") as QuantityUpDown));
                if (fromSel > 0) { lastKnownChartTraderQty = fromSel; source = "QuantitySelector"; return fromSel; }
                Grid grid = GetChartTraderWindow()?.FindFirst("ChartWindowChartTraderControl") as Grid;
                if (grid != null)
                {
                    var edit = grid.FindFirst("ChartTraderControlQuantityEdit") as TextBox;
                    if (edit != null && TryParseQuantityText(edit.Text, out int qE)) { lastKnownChartTraderQty = qE; source = "QuantityEdit"; return qE; }
                    var legacy = grid.FindFirst("ChartTraderControlQuantityTextBox") as TextBox;
                    if (legacy != null && TryParseQuantityText(legacy.Text, out int qL)) { lastKnownChartTraderQty = qL; source = "QuantityTextBox"; return qL; }
                }
                if (lastKnownChartTraderQty > 1) { source = "lastKnown"; return lastKnownChartTraderQty; }
                int ctQty = ChartControl?.OwnerChart?.ChartTrader?.Quantity ?? 0;
                if (ctQty > 0) { source = "ChartTrader.Quantity"; return ctQty; }
            }
            catch (Exception ex) { Print("ReadQty ▶ " + ex.Message); }
            source = "default"; return 1;
        }

        private Window GetChartTraderWindow()
        {
            if (ChartControl?.Parent == null) return null;
            return Window.GetWindow(ChartControl.Parent);
        }

        private NinjaTrader.Cbi.Account ResolveAccount()
        {
            NinjaTrader.Cbi.Account acct = null;
            if (ChartControl == null) return null;
            ChartControl.Dispatcher.Invoke(() =>
            {
                xAcSelector = GetChartTraderWindow()?.FindFirst("ChartTraderControlAccountSelector") as NinjaTrader.Gui.Tools.AccountSelector;
                acct = xAcSelector?.SelectedAccount;
            });
            if (acct != null) return acct;
            string name = null;
            ChartControl.Dispatcher.Invoke(() => { name = xAcSelector?.SelectedAccount?.Name; });
            return string.IsNullOrWhiteSpace(name) ? null
                : NinjaTrader.Cbi.Account.All.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        private bool ValidateAccountForTrading(NinjaTrader.Cbi.Account acct, string context, out string reason)
        {
            reason = null;
            if (acct == null) { reason = "no account selected"; return false; }
            if (acct.Connection == null || acct.Connection.Status != ConnectionStatus.Connected)
            { reason = "account '" + acct.Name + "' not connected (" + (acct.Connection?.Status.ToString() ?? "none") + ")"; return false; }
            return true;
        }

        private void SubmitPanelOrders(NinjaTrader.Cbi.Account acct, NinjaTrader.Cbi.Order[] orders, string context)
        {
            if (orders == null || orders.Length == 0) return;
            if (!ValidateAccountForTrading(acct, context, out string reason)) { Print(context + " ▶ " + reason); return; }
            try { acct.Submit(orders); }
            catch (Exception ex) { Print(context + " ▶ submit failed: " + ex.Message); }
        }

        private NinjaTrader.Cbi.Instrument ResolveInstrument()
        {
            NinjaTrader.Cbi.Instrument instr = null;
            if (ChartControl == null) return Instrument;
            ChartControl.Dispatcher.Invoke(() =>
            {
                xInSelector = GetChartTraderWindow()?.FindFirst("ChartWindowInstrumentSelector") as NinjaTrader.Gui.Tools.InstrumentSelector;
                instr = xInSelector?.Instrument;
            });
            return instr ?? Instrument;
        }




        // ─────────────────────────────────────────────────────────────────────
        // OnRender — on-chart session risk card
        // Only draws when at least one limit is non-zero.
        // Corner position controlled by RiskCardCorner property.
        // ─────────────────────────────────────────────────────────────────────
        // V1.3: On-chart risk card rendered via OnRender
        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            try { base.OnRender(chartControl, chartScale); } catch { }

            if (SessionPnLMax == 0 && SessionDDLimit == 0) return;
            if (RenderTarget == null) return;
            if (chartControl == null) return;
            if (!uiPanelActive) return;

            try
            {
                float panelW  = (float)chartControl.ActualWidth;
                float panelH  = (float)chartControl.ActualHeight;
                float cardW   = 210f;
                float lineH   = 17f;
                int   lines   = 2 + (SessionPnLMax != 0 ? 1 : 0) + (SessionDDLimit != 0 ? 1 : 0)
                              + (ShowTradeStats ? 2 : 0);
                float cardH   = 14f + lines * lineH + 8f;
                float padding = 10f;
                float margin  = 14f;

                float x, y;
                switch (RiskCardCorner)
                {
                    case RiskCardCornerPos.TopLeft:    x = margin;                  y = margin;                  break;
                    case RiskCardCornerPos.TopRight:   x = panelW - cardW - margin; y = margin;                  break;
                    case RiskCardCornerPos.BottomLeft: x = margin;                  y = panelH - cardH - margin; break;
                    default:                           x = panelW - cardW - margin; y = panelH - cardH - margin; break;
                }

                var rt = RenderTarget;

                // Background
                var bgCol = new SharpDX.Color4(13f/255f, 15f/255f, 20f/255f, 0.88f);
                var bdCol = pnlMaxHit  ? new SharpDX.Color4(245f/255f, 158f/255f, 11f/255f,  1f)
                          : ddLimitHit ? new SharpDX.Color4(1f,         77f/255f, 106f/255f,  1f)
                          :              new SharpDX.Color4(30f/255f,   35f/255f,  48f/255f,  1f);

                using (var bgBrush = new SharpDX.Direct2D1.SolidColorBrush(rt, bgCol))
                using (var bdBrush = new SharpDX.Direct2D1.SolidColorBrush(rt, bdCol))
                {
                    var rect = new SharpDX.RectangleF(x, y, cardW, cardH);
                    rt.FillRectangle(rect, bgBrush);
                    rt.DrawRectangle(rect, bdBrush, pnlMaxHit || ddLimitHit ? 2f : 1f);
                }

                // V1.4 (Spoobie): Lazy-init cached factory — one allocation for lifetime of indicator
                if (riskTextFactory == null)
                    riskTextFactory = new SharpDX.DirectWrite.Factory();
                var dwf = riskTextFactory;

                float ty = y + padding;

                // Header label
                DrawRiskText(rt, dwf, "SESSION RISK", x + padding, ty, cardW - padding * 2, lineH,
                    new SharpDX.Color4(120f/255f, 128f/255f, 148f/255f, 1f), 9f);
                ty += lineH;

                // P&L line
                string pnlStr = sessionPnL >= 0
                    ? "+$" + sessionPnL.ToString("N2")
                    : "-$" + Math.Abs(sessionPnL).ToString("N2");
                var pnlCol = sessionPnL >= 0
                    ? new SharpDX.Color4(0f, 212f/255f, 160f/255f, 1f)
                    : new SharpDX.Color4(1f, 77f/255f,  106f/255f, 1f);
                DrawRiskText(rt, dwf, "P&L    " + pnlStr, x + padding, ty,
                    cardW - padding * 2, lineH, pnlCol, 11f);
                ty += lineH;

                // P&L Max line
                if (SessionPnLMax != 0)
                {
                    var maxCol = pnlMaxHit
                        ? new SharpDX.Color4(245f/255f, 158f/255f, 11f/255f, 1f)
                        : new SharpDX.Color4(90f/255f,   96f/255f, 112f/255f, 1f);
                    DrawRiskText(rt, dwf,
                        "MAX    $" + SessionPnLMax.ToString("N2") + (pnlMaxHit ? "  HIT" : ""),
                        x + padding, ty, cardW - padding * 2, lineH, maxCol, 10f);
                    ty += lineH;
                }

                // DD Limit line
                if (SessionDDLimit != 0)
                {
                    var ddCol = ddLimitHit
                        ? new SharpDX.Color4(1f, 77f/255f, 106f/255f, 1f)
                        : new SharpDX.Color4(90f/255f, 96f/255f, 112f/255f, 1f);
                    DrawRiskText(rt, dwf,
                        "DD     -$" + Math.Abs(SessionDDLimit).ToString("N2") + (ddLimitHit ? "  HIT" : ""),
                        x + padding, ty, cardW - padding * 2, lineH, ddCol, 10f);
                    ty += lineH;
                }

                // V1.6: Trade stats lines
                if (ShowTradeStats)
                {
                    var statsCol = new SharpDX.Color4(120f/255f, 128f/255f, 148f/255f, 1f);
                    string tradesStr = "TRADES  " + sessionTradeCount
                        + "   " + sessionWins + "W/" + sessionLosses + "L";
                    DrawRiskText(rt, dwf, tradesStr,
                        x + padding, ty, cardW - padding * 2, lineH, statsCol, 10f);
                    ty += lineH;
                    int winPct = sessionTradeCount > 0
                        ? (int)Math.Round(sessionWins * 100.0 / sessionTradeCount) : 0;
                    DrawRiskText(rt, dwf, "WIN%    " + winPct + "%",
                        x + padding, ty, cardW - padding * 2, lineH, statsCol, 10f);
                }
            }
            catch (Exception ex) { Print("OnRender ▶ " + ex.Message); }
        }

        private void DrawRiskText(SharpDX.Direct2D1.RenderTarget rt,
                                  SharpDX.DirectWrite.Factory dwf,
                                  string text, float x, float y, float w, float h,
                                  SharpDX.Color4 color, float fontSize = 10f)
        {
            using (var fmt = new SharpDX.DirectWrite.TextFormat(dwf, "Consolas",
                SharpDX.DirectWrite.FontWeight.Bold,
                SharpDX.DirectWrite.FontStyle.Normal,
                SharpDX.DirectWrite.FontStretch.Normal, fontSize))
            using (var brush = new SharpDX.Direct2D1.SolidColorBrush(rt, color))
            {
                rt.DrawText(text, fmt, new SharpDX.RectangleF(x, y, w, h), brush);
            }
        }


        // ─────────────────────────────────────────────────────────────────────
        // Session Risk — logic
        // ─────────────────────────────────────────────────────────────────────
        private void CheckSessionRisk()
        {
            // Skip if both limits disabled
            if (SessionPnLMax == 0 && SessionDDLimit == 0) return;
            // Skip during historical playback
            if (State != State.Realtime) return;

            // Use cached account — safe to access from data thread without Dispatcher
            var acct = cachedAccount;
            if (acct == null) return;

            try
            {
                // acct.Get() is thread-safe in NT8 — no Dispatcher needed
                double realized   = acct.Get(AccountItem.RealizedProfitLoss,   Currency.UsDollar);
                double unrealized = acct.Get(AccountItem.UnrealizedProfitLoss, Currency.UsDollar);

                // V1.6.1 fix: guard NaN / sentinel values.
                // AccountItem.UnrealizedProfitLoss returns a large negative sentinel
                // (not 0.0) when no position is open on some NT8 account types.
                // Any non-finite or extreme value (> ±$1M) is treated as 0.0.
                if (double.IsNaN(realized)   || double.IsInfinity(realized)   || Math.Abs(realized)   > 1_000_000) realized   = 0.0;
                if (double.IsNaN(unrealized) || double.IsInfinity(unrealized) || Math.Abs(unrealized) > 1_000_000) unrealized = 0.0;

                sessionPnL = realized + unrealized;
            }
            catch (Exception ex)
            {
                Print("CheckSessionRisk ▶ P&L read error: " + ex.Message);
                return;
            }

            // Check for unlock (pure data — no UI touch)
            CheckRiskUnlock();

            // V1.6: Track win/loss via realized P&L delta after each trade cycle
            if (ShowTradeStats && !double.IsNaN(lastRealizedPnL))
            {
                double realized = sessionPnL - (acct.Get(AccountItem.UnrealizedProfitLoss, Currency.UsDollar));
                double delta    = realized - lastRealizedPnL;
                // Delta > 1 cent = a closed trade result became available
                if (Math.Abs(delta) > 0.01)
                {
                    if (delta > 0) sessionWins++;
                    else           sessionLosses++;
                    lastRealizedPnL = realized;
                }
            }

            // ── P&L Max check — V1.6.1: gated on riskPnLValid to prevent false trigger ─
            if (SessionPnLMax != 0 && !pnlMaxHit && riskPnLValid && sessionPnL >= SessionPnLMax)
            {
                pnlMaxHit = true;
                mqbArmed  = false;
                mqsArmed  = false;
                PlayRiskAlert();
                Print("SESSION RISK ▶ P&L MAX HIT — both arms disarmed (" + sessionPnL.ToString("C2") + ")");
            }

            // ── DD Limit check — V1.6.1: gated on riskPnLValid to prevent false trigger ─
            if (SessionDDLimit != 0 && !ddLimitHit && riskPnLValid && sessionPnL <= SessionDDLimit)
            {
                ddLimitHit = true;
                mqbArmed   = false;
                mqsArmed   = false;
                PlayRiskAlert();
                Print("SESSION RISK ▶ DD LIMIT HIT — both arms disarmed (" + sessionPnL.ToString("C2") + ")");
                if (FlattenOnDDLimit) FlattenChartAccount();
            }

            // V1.4 (Spoobie): Throttle UI refresh — only dispatch when values actually change.
            // Avoids unnecessary Dispatcher.InvokeAsync on every bar update.
            double pnlCents = Math.Round(sessionPnL, 2);
            bool hitStateChanged = pnlMaxHit != lastRiskUiPnlMaxHit || ddLimitHit != lastRiskUiDdLimitHit;
            bool riskDisplayChanged = double.IsNaN(lastRiskUiPnLCents)
                || pnlCents != lastRiskUiPnLCents
                || hitStateChanged;
            if (!riskDisplayChanged) return;

            lastRiskUiPnLCents   = pnlCents;
            lastRiskUiPnlMaxHit  = pnlMaxHit;
            lastRiskUiDdLimitHit = ddLimitHit;

            ChartControl?.Dispatcher.InvokeAsync(() =>
            {
                if (hitStateChanged)
                {
                    try { RefreshArmButtons(); } catch { }
                }
                try { UpdateRiskDisplay(); } catch { }
                try { ChartControl?.InvalidateVisual(); } catch { }
            });
        }

        // Unlock if trader raises limit above current P&L
        private void CheckRiskUnlock()
        {
            // P&L Max unlock: limit raised above current P&L
            if (pnlMaxHit && (SessionPnLMax == 0 || sessionPnL < SessionPnLMax))
            {
                pnlMaxHit      = false;
                riskSoundPlayed = false;
                Print("SESSION RISK ▶ P&L Max limit raised — unlocked");
            }
            // DD Limit unlock: limit lowered (more negative) below current P&L or disabled
            if (ddLimitHit && (SessionDDLimit == 0 || sessionPnL > SessionDDLimit))
            {
                ddLimitHit      = false;
                riskSoundPlayed = false;
                Print("SESSION RISK ▶ DD Limit raised — unlocked");
            }
            lastCheckedPnLMax = SessionPnLMax;
            lastCheckedDDLim  = SessionDDLimit;
        }

        // Disarm both MQB and MQS with a reason log
        private void DisarmAll(string reason)
        {
            mqbArmed = false;
            mqsArmed = false;
            // If on UI thread already, refresh directly; otherwise dispatch
            if (ChartControl?.Dispatcher.CheckAccess() == true)
            {
                try { RefreshArmButtons(); } catch { }
                try { UpdateRiskDisplay();  } catch { }
            }
            else
            {
                ChartControl?.Dispatcher.InvokeAsync(() =>
                {
                    try { RefreshArmButtons(); } catch { }
                    try { UpdateRiskDisplay();  } catch { }
                });
            }
            Print("SESSION RISK ▶ DISARMED — " + reason);
        }

        // Play alert sound — built-in default with optional user override
        private void PlayRiskAlert()
        {
            if (riskSoundPlayed) return;
            riskSoundPlayed = true;
            try
            {
                string soundPath = string.IsNullOrWhiteSpace(SoundFile)
                    ? System.IO.Path.Combine(NinjaTrader.Core.Globals.InstallDir, "sounds", "Alert1.wav")
                    : SoundFile;
                if (System.IO.File.Exists(soundPath))
                    NinjaTrader.Core.Globals.PlaySound(soundPath);
                else
                    Print("SESSION RISK ▶ sound file not found: " + soundPath);
            }
            catch (Exception ex) { Print("SESSION RISK ▶ sound error: " + ex.Message); }
        }

        // Flatten strictly the ChartTrader selected account
        private void FlattenChartAccount()
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var acct  = ResolveAccount();    if (acct == null)  { Print("FlattenChartAccount ▶ no account"); return; }
                    var instr = ResolveInstrument(); if (instr == null) { Print("FlattenChartAccount ▶ no instrument"); return; }

                    // Cancel all working orders on this account + instrument
                    var exits = acct.Orders
                        .Where(o => o.Instrument.FullName == instr.FullName &&
                                   (o.OrderState == OrderState.Working ||
                                    o.OrderState == OrderState.Accepted ||
                                    o.OrderState == OrderState.PartFilled))
                        .ToArray();
                    if (exits.Length > 0) { acct.Cancel(exits); }

                    System.Threading.Thread.Sleep(FlattenAllPause);

                    // Market out any open position
                    var pos = acct.Positions.FirstOrDefault(p => p.Instrument.FullName == instr.FullName
                                                               && p.Quantity != 0);
                    if (pos == null) { Print("FlattenChartAccount ▶ no open position to flatten"); return; }

                    OrderAction closeAction = pos.MarketPosition == MarketPosition.Long
                        ? OrderAction.Sell : OrderAction.BuyToCover;

                    for (int attempt = 0; attempt < FlattenAllTries; attempt++)
                    {
                        var closeOrder = acct.CreateOrder(instr, closeAction, OrderType.Market,
                            PanelOrderEntry, TimeInForce.Gtc, Math.Abs(pos.Quantity), 0, 0, "",
                            Name + "_DDFlatten", Core.Globals.MaxDate, null);
                        ChartControl?.Dispatcher.Invoke(() =>
                            SubmitPanelOrders(acct, new[] { closeOrder }, "DDFlatten"));
                        System.Threading.Thread.Sleep(250);

                        // Check if flat
                        var check = acct.Positions.FirstOrDefault(p => p.Instrument.FullName == instr.FullName);
                        if (check == null || check.Quantity == 0) break;
                    }
                    Print("SESSION RISK ▶ FlattenChartAccount complete");
                }
                catch (Exception ex) { Print("FlattenChartAccount ▶ ERROR: " + ex.Message); }
            });
        }

        // ─────────────────────────────────────────────────────────────────────
        // Trail execution — called on every tick while a mode is active
        // ─────────────────────────────────────────────────────────────────────
        private void ExecuteTrail()
        {
            // V1.4 (Spoobie): Use cached refs — avoids Dispatcher calls from market data thread
            var acct  = cachedAccount ?? ResolveAccount();    if (acct == null)  return;
            var instr = cachedInstrument ?? ResolveInstrument(); if (instr == null) return;
            var pos   = acct.Positions.FirstOrDefault(p => p.Instrument.FullName == instr.FullName);
            if (pos == null || pos.Quantity == 0)
            {
                ResetTrailStateOnFlat();
                return;
            }

            bool isLong  = pos.MarketPosition == MarketPosition.Long;
            double price = lastClose > 0 ? lastClose : Close[0];
            double tick  = instr.MasterInstrument.TickSize;
            double entry = pos.AveragePrice;

            double newStop = double.MinValue;

            switch (activeTrailMode)
            {
                case TrailMode.TrailTicks:
                    newStop = isLong
                        ? price - TrailTicks * tick
                        : price + TrailTicks * tick;
                    break;

                case TrailMode.BreakevenPlus:
                    if (beTriggered) return;
                    double beProfit = isLong ? price - entry : entry - price;
                    if (beProfit >= BETriggerTicks * tick)
                    {
                        newStop    = isLong ? entry + BEBufferTicks * tick : entry - BEBufferTicks * tick;
                        beTriggered = true;
                        Print("Trail BE+N ▶ triggered at " + price.ToString("F2") + " → stop " + newStop.ToString("F2"));
                    }
                    break;

                case TrailMode.BarLowHigh:
                    if (CurrentBar < 1) return;
                    newStop = isLong ? Low[1] : High[1];
                    break;

                case TrailMode.NBarLowHigh:
                    if (CurrentBar < TrailBarLookback) return;
                    newStop = isLong
                        ? Enumerable.Range(0, TrailBarLookback).Min(i => Low[i])
                        : Enumerable.Range(0, TrailBarLookback).Max(i => High[i]);
                    break;

                case TrailMode.TrailATR:
                {
                    double atr    = ATR(TrailATRPeriod)[0];
                    newStop = isLong
                        ? price - TrailATRMult * atr
                        : price + TrailATRMult * atr;
                    break;
                }

                case TrailMode.TrendMagic:
                {
                    // ATR band gated by CCI regime:
                    // Long trail only when CCI > 0; Short trail only when CCI < 0
                    double atr = ATR(TrailATRPeriod)[0];
                    double cci = CCI(TrailATRPeriod)[0];
                    bool cciConfirms = isLong ? cci > 0 : cci < 0;
                    if (!cciConfirms) return;  // freeze stop — regime not aligned
                    newStop = isLong
                        ? price - TrailATRMult * atr
                        : price + TrailATRMult * atr;
                    break;
                }

                case TrailMode.HalfPlusBE:
                {
                    double profit = isLong ? price - entry : entry - price;
                    if (!halfTriggered && profit >= HalfTriggerTicks * tick)
                    {
                        halfTriggered = true;
                        // Close half position
                        int halfQty = Math.Max(1, pos.Quantity / 2);
                        OrderAction closeHalf = isLong ? OrderAction.Sell : OrderAction.BuyToCover;
                        SubmitPanelOrders(acct, new[] { acct.CreateOrder(instr, closeHalf,
                            OrderType.Market, PanelOrderEntry, TimeInForce.Gtc, halfQty, 0, 0, "",
                            Name + "_HalfBE", Core.Globals.MaxDate, null) }, "HalfBE");
                        Print("Trail Half+BE ▶ closed half (" + halfQty + " contract(s))");
                        // Move stop to breakeven + buffer
                        newStop = isLong ? entry + HalfBufferTicks * tick : entry - HalfBufferTicks * tick;
                    }
                    else if (!halfTriggered) return;
                    else return; // half already done, stop already set
                    break;
                }

                default: return;
            }

            if (newStop == double.MinValue) return;

            // Never move stop against position
            bool stopImproved = isLong
                ? newStop > trailStopLevel
                : (trailStopLevel == double.MinValue || newStop < trailStopLevel);

            if (!stopImproved) return;

            trailStopLevel = newStop;
            MoveOrPlaceStop(acct, instr, pos, newStop, isLong);
        }

        // V1.5: Reset trail tracking when flat
        private void ResetTrailStateOnFlat()
        {
            trailStopLevel           = double.MinValue;
            beTriggered              = false;
            halfTriggered            = false;
            trailStopSubmitPending   = false;
        }

        private static bool IsActiveExitOrder(Order o)
        {
            if (o == null) return false;
            return o.OrderState == OrderState.Working
                || o.OrderState == OrderState.Accepted
                || o.OrderState == OrderState.Submitted
                || o.OrderState == OrderState.ChangeSubmitted
                || o.OrderState == OrderState.PartFilled;
        }

        private void MoveOrPlaceStop(NinjaTrader.Cbi.Account acct,
                                     NinjaTrader.Cbi.Instrument instr,
                                     Position pos, double stopPrice, bool isLong)
        {
            try
            {
                stopPrice = instr.MasterInstrument.RoundToTickSize(stopPrice);
                if (trailStopSubmitPending) return;

                OrderAction stopAction = isLong ? OrderAction.Sell : OrderAction.BuyToCover;

                var existingStops = acct.Orders
                    .Where(o => o.Instrument.FullName == instr.FullName
                             && (o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit)
                             && IsActiveExitOrder(o)
                             && o.OrderAction == stopAction)
                    .ToList();

                if (existingStops.Count > 1)
                {
                    var keep = isLong
                        ? existingStops.OrderByDescending(o => o.StopPrice).First()
                        : existingStops.OrderBy(o => o.StopPrice).First();
                    var extras = existingStops.Where(o => o != keep).ToArray();
                    if (extras.Length > 0) acct.Cancel(extras);
                    existingStops = new List<Order> { keep };
                }

                if (existingStops.Any())
                {
                    foreach (var s in existingStops)
                    {
                        s.StopPriceChanged = stopPrice;
                        if (s.Quantity != pos.Quantity)
                            s.QuantityChanged = pos.Quantity;
                    }
                    acct.Change(existingStops.ToArray());
                    trailStopSubmitPending = false;
                }
                else
                {
                    trailStopSubmitPending = true;
                    SubmitPanelOrders(acct, new[] { acct.CreateOrder(instr, stopAction,
                        OrderType.StopMarket, PanelOrderEntry, TimeInForce.Gtc,
                        pos.Quantity, 0, stopPrice, "",
                        Name + "_Trail", Core.Globals.MaxDate, null) }, "Trail");
                }
            }
            catch (Exception ex) { Print("Trail MoveStop ▶ ERROR: " + ex.Message); }
        }

        // ═════════════════════════════════════════════════════════════════════
        // Trading methods (carried from V1.1)
        // ═════════════════════════════════════════════════════════════════════
        #region Trading Methods

        private void FlattenEverythingAllAccounts()
        {
            if (isFlatteningAll) { Print("Flatten ▶ already in progress"); return; }
            isFlatteningAll = true;
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var allAccts = NinjaTrader.Cbi.Account.All?.ToList() ?? new List<NinjaTrader.Cbi.Account>();
                    if (allAccts.Count == 0) { Print("Flatten ▶ no accounts found"); return; }
                    int cancelled = 0;
                    foreach (var acct in allAccts)
                    {
                        try
                        {
                            var c = acct.Orders.Where(o => o != null && (o.OrderState == OrderState.Working || o.OrderState == OrderState.Accepted || o.OrderState == OrderState.PartFilled)).ToArray();
                            if (c.Length > 0) { acct.Cancel(c); cancelled += c.Length; }
                        }
                        catch (Exception ex) { Print("Flatten ▶ cancel error: " + ex.Message); }
                    }
                    System.Threading.Thread.Sleep(FlattenAllPause);
                    int passes = 0, submitted = 0;
                    while (passes < FlattenAllTries)
                    {
                        passes++; int rem = 0;
                        foreach (var acct in allAccts)
                        {
                            try
                            {
                                var open = acct.Positions.Where(p => p != null && p.Quantity != 0).ToList();
                                rem += open.Count;
                                if (open.Count == 0) continue;
                                var outs = open.Where(p => p.Instrument != null && Math.Abs(p.Quantity) > 0)
                                    .Select(p => acct.CreateOrder(p.Instrument,
                                        p.MarketPosition == MarketPosition.Long ? OrderAction.Sell : OrderAction.BuyToCover,
                                        OrderType.Market, PanelOrderEntry, TimeInForce.Gtc,
                                        Math.Abs(p.Quantity), 0, 0, "",
                                        Name + "_Flatten_" + p.Instrument.FullName, Core.Globals.MaxDate, null)).ToArray();
                                if (outs.Length > 0)
                                {
                                    ChartControl?.Dispatcher.Invoke(() => SubmitPanelOrders(acct, outs, "Flatten"));
                                    submitted += outs.Length;
                                }
                            }
                            catch (Exception ex) { Print("Flatten ▶ market error: " + ex.Message); }
                        }
                        if (rem == 0) break;
                        System.Threading.Thread.Sleep(250);
                    }
                    Print("Flatten ▶ done — cancelled " + cancelled + ", submitted " + submitted + " market order(s)");
                }
                catch (Exception ex) { Print("Flatten ▶ ERROR: " + ex.Message); }
                finally { isFlatteningAll = false; }
            });
        }

        private void StopsToBreakeven(int ticks)
        {
            var acct = ResolveAccount(); if (acct == null) return;
            var instr = ResolveInstrument(); if (instr == null) return;
            var pos = acct.Positions.FirstOrDefault(p => p.Instrument.FullName == instr.FullName);
            if (pos == null || pos.Quantity == 0) return;
            foreach (var o in acct.Orders.Where(o => o.Instrument.FullName == instr.FullName &&
                (o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit) &&
                o.OrderState != OrderState.Cancelled && o.OrderState != OrderState.Filled))
            { o.StopPriceChanged = pos.AveragePrice + (pos.MarketPosition == MarketPosition.Long ? +ticks : -ticks) * instr.MasterInstrument.TickSize; acct.Change(new[] { o }); }
        }

        private void TargetToPricePlus(int ticks)
        {
            var acct = ResolveAccount(); if (acct == null) return;
            var instr = ResolveInstrument(); if (instr == null) return;
            var pos = acct.Positions.FirstOrDefault(p => p.Instrument.FullName == instr.FullName);
            if (pos == null || pos.Quantity == 0) { Print("PricePlus ▶ no position"); return; }
            var targets = acct.Orders.Where(o => o.Instrument.FullName == instr.FullName && o.OrderType == OrderType.Limit && (o.OrderState == OrderState.Working || o.OrderState == OrderState.Accepted)).ToList();
            if (targets.Count == 0) { Print("PricePlus ▶ no targets"); return; }
            bool isLong = pos.MarketPosition == MarketPosition.Long;
            double bp = instr.MasterInstrument.RoundToTickSize(lastClose);
            double np = isLong ? bp + ticks * instr.MasterInstrument.TickSize : bp - ticks * instr.MasterInstrument.TickSize;
            foreach (var o in targets) o.LimitPriceChanged = np;
            acct.Change(targets.ToArray());
        }

        private void TargetToEntryPlus(int ticks)
        {
            var acct = ResolveAccount(); if (acct == null) return;
            var instr = ResolveInstrument(); if (instr == null) return;
            var pos = acct.Positions.FirstOrDefault(p => p.Instrument.FullName == instr.FullName);
            if (pos == null || pos.Quantity == 0) { Print("EntryPlus ▶ no position"); return; }
            var targets = acct.Orders.Where(o => o.Instrument.FullName == instr.FullName && o.OrderType == OrderType.Limit && (o.OrderState == OrderState.Working || o.OrderState == OrderState.Accepted)).ToList();
            if (targets.Count == 0) { Print("EntryPlus ▶ no targets"); return; }
            bool isLong = pos.MarketPosition == MarketPosition.Long;
            double np = isLong ? pos.AveragePrice + ticks * instr.MasterInstrument.TickSize : pos.AveragePrice - ticks * instr.MasterInstrument.TickSize;
            foreach (var o in targets) o.LimitPriceChanged = np;
            acct.Change(targets.ToArray());
        }

        private void BracketOrder(int stopTicks, int profitTicks)
        {
            var acct = ResolveAccount(); if (acct == null) return;
            var instr = ResolveInstrument(); if (instr == null) return;
            var pos = acct.Positions.FirstOrDefault(p => p.Instrument.FullName == instr.FullName);
            if (pos == null || pos.Quantity == 0) { Print("Bracket ▶ no position"); return; }
            if (acct.Orders.Any(o => o.Instrument.FullName == instr.FullName && !string.IsNullOrEmpty(o.Oco) && (o.OrderState == OrderState.Accepted || o.OrderState == OrderState.Working))) { Print("Bracket ▶ already exists"); return; }
            bool isLong = pos.MarketPosition == MarketPosition.Long;
            double bp = instr.MasterInstrument.RoundToTickSize(lastClose), ts = instr.MasterInstrument.TickSize;
            double stopPx = isLong ? bp - stopTicks * ts : bp + stopTicks * ts;
            double tgtPx  = isLong ? bp + profitTicks * ts : bp - profitTicks * ts;
            string oco = Guid.NewGuid().ToString();
            SubmitPanelOrders(acct, new[]
            {
                acct.CreateOrder(instr, isLong ? OrderAction.Sell : OrderAction.BuyToCover, OrderType.StopMarket, PanelOrderEntry, TimeInForce.Gtc, pos.Quantity, 0, stopPx, oco, Name + "_SL", Core.Globals.MaxDate, null),
                acct.CreateOrder(instr, isLong ? OrderAction.Sell : OrderAction.BuyToCover, OrderType.Limit, PanelOrderEntry, TimeInForce.Gtc, pos.Quantity, tgtPx, 0, oco, Name + "_TP", Core.Globals.MaxDate, null),
            }, "Bracket");
            Print("Bracket ▶ SL=" + stopPx + " TP=" + tgtPx);
        }

        private void AddStopOrder(int stopTicks)
        {
            var acct = ResolveAccount(); if (acct == null) return;
            var instr = ResolveInstrument(); if (instr == null) return;
            var pos = acct.Positions.FirstOrDefault(p => p.Instrument.FullName == instr.FullName);
            if (pos == null || pos.Quantity == 0) { Print("AddStop ▶ no position"); return; }
            double refPx = lastClose > 0 ? lastClose : Close[0];
            PlaceStopOrder(acct, instr, pos, stopTicks, refPx, "", Name + "_AddStop", "AddStop");
        }

        // V1.5: Shared stop placement for Add Stop and auto-stop on entry
        private bool PlaceStopOrder(NinjaTrader.Cbi.Account acct, NinjaTrader.Cbi.Instrument instr, Position pos,
            int stopTicks, double referencePrice, string ocoId, string orderName, string context)
        {
            if (pos == null || pos.Quantity == 0) { Print(context + " ▶ no position"); return false; }
            bool isLong = pos.MarketPosition == MarketPosition.Long;
            OrderAction stopAction = isLong ? OrderAction.Sell : OrderAction.BuyToCover;
            if (HasActiveStopOrder(acct, instr, stopAction))
            {
                Print(context + " ▶ stop already exists");
                return false;
            }
            double tick = instr.MasterInstrument.TickSize;
            double stopPx = instr.MasterInstrument.RoundToTickSize(
                isLong ? referencePrice - stopTicks * tick : referencePrice + stopTicks * tick);
            SubmitPanelOrders(acct, new[] { acct.CreateOrder(instr, stopAction, OrderType.StopMarket,
                PanelOrderEntry, TimeInForce.Gtc, pos.Quantity, 0, stopPx,
                ocoId ?? "", orderName, Core.Globals.MaxDate, null) }, context);
            Print(context + " ▶ Stop=" + stopPx);
            return true;
        }

        private bool HasActiveStopOrder(NinjaTrader.Cbi.Account acct, NinjaTrader.Cbi.Instrument instr, OrderAction stopAction)
        {
            return acct.Orders.Any(o => o.Instrument.FullName == instr.FullName
                && (o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit)
                && IsActiveExitOrder(o)
                && o.OrderAction == stopAction);
        }

        private void CancelPanelExitOrders(NinjaTrader.Cbi.Account acct, NinjaTrader.Cbi.Instrument instr)
        {
            var exits = acct.Orders.Where(o => o.Instrument.FullName == instr.FullName
                && (o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit || o.OrderType == OrderType.Limit)
                && IsActiveExitOrder(o)).ToArray();
            if (exits.Length > 0) acct.Cancel(exits);
        }

        // V1.5: ChartTrader account OrderUpdate — auto-stop on arm entry fill
        private void SubscribeChartTraderOrderUpdate()
        {
            if (orderUpdateSubscribed) return;
            var acct = cachedAccount ?? ResolveAccount();
            if (acct == null) return;
            acct.OrderUpdate += OnChartTraderOrderUpdate;
            orderUpdateSubscribed = true;
        }

        private void UnsubscribeChartTraderOrderUpdate()
        {
            if (!orderUpdateSubscribed) return;
            try
            {
                var acct = cachedAccount ?? ResolveAccount();
                if (acct != null)
                    acct.OrderUpdate -= OnChartTraderOrderUpdate;
            }
            catch { }
            orderUpdateSubscribed = false;
        }

        private void OnChartTraderOrderUpdate(object sender, OrderEventArgs e)
        {
            try
            {
                Order order = e?.Order;
                if (order == null) return;
                var instr = cachedInstrument ?? Instrument;
                if (instr == null || order.Instrument.FullName != instr.FullName) return;

                if (order.Name != null && (order.Name.Contains("_Trail") || order.Name.Contains("_AutoStop") || order.Name.Contains("_AddStop")))
                {
                    if (order.OrderState == OrderState.Working || order.OrderState == OrderState.Accepted)
                        trailStopSubmitPending = false;
                }

                string armPrefix = Name + "_Arm_";
                bool isArmFill = order.Name != null
                    && order.Name.StartsWith(armPrefix, StringComparison.Ordinal)
                    && order.OrderState == OrderState.Filled;

                if (isArmFill)
                {
                    var acctCopy  = cachedAccount ?? ResolveAccount();
                    var instrCopy = instr;

                    // V1.6: Auto-exit (stop / bracket)
                    if (AutoEntryExitMode != AutoEntryMode.None)
                        ChartControl?.Dispatcher.InvokeAsync(() => TryPlaceAutoExitAfterArmFill(acctCopy, instrCopy));

                    // V1.6: Auto-trail
                    if (autoTrailEnabled && activeTrailMode != TrailMode.None && !autoTrailActivated)
                    {
                        autoTrailActivated = true;
                        Print("Auto-trail ▶ activated: " + activeTrailMode);
                    }
                    else if (autoTrailEnabled && activeTrailMode == TrailMode.None)
                        Print("Auto-trail ▶ skipped — no trail mode selected");

                    // V1.6: Trade stats — record fill, compute delta from last realized P&L
                    ChartControl?.Dispatcher.InvokeAsync(() => RecordArmFillForStats(acctCopy));
                }
            }
            catch (Exception ex) { Print("OrderUpdate ▶ ERROR: " + ex.Message); }
        }

        // V1.6: Place auto-exit (StopOnly or Bracket) after arm entry fill
        private void TryPlaceAutoExitAfterArmFill(NinjaTrader.Cbi.Account acct, NinjaTrader.Cbi.Instrument instr)
        {
            try
            {
                if (AutoEntryExitMode == AutoEntryMode.None || acct == null || instr == null) return;
                var pos = acct.Positions.FirstOrDefault(p => p.Instrument.FullName == instr.FullName);
                if (pos == null || pos.Quantity == 0) return;

                CancelPanelExitOrders(acct, instr);
                currentEntryOcoId = Guid.NewGuid().ToString();

                bool isLong     = pos.MarketPosition == MarketPosition.Long;
                double avgPrice = pos.AveragePrice;
                double tickSz   = instr.MasterInstrument.TickSize;
                int    stopTicks = ReadBracketStopTicksFromPanel();
                int    tgtTicks  = ReadAutoTargetTicksFromPanel();

                if (AutoEntryExitMode == AutoEntryMode.StopOnly)
                {
                    // Stop only — same as V1.5 behavior
                    PlaceStopOrder(acct, instr, pos, stopTicks, avgPrice,
                        currentEntryOcoId, Name + "_AutoStop", "AutoStop");
                    Print("Auto-exit ▶ Stop Only placed — stop=" +
                        instr.MasterInstrument.RoundToTickSize(isLong
                            ? avgPrice - stopTicks * tickSz
                            : avgPrice + stopTicks * tickSz).ToString("F2"));
                }
                else if (AutoEntryExitMode == AutoEntryMode.Bracket)
                {
                    // OCO bracket anchored to AveragePrice
                    double stopPx = instr.MasterInstrument.RoundToTickSize(
                        isLong ? avgPrice - stopTicks * tickSz : avgPrice + stopTicks * tickSz);
                    double tgtPx  = instr.MasterInstrument.RoundToTickSize(
                        isLong ? avgPrice + tgtTicks  * tickSz : avgPrice - tgtTicks  * tickSz);

                    OrderAction exitAction = isLong ? OrderAction.Sell : OrderAction.BuyToCover;
                    var stopOrder = acct.CreateOrder(instr, exitAction, OrderType.StopMarket,
                        PanelOrderEntry, TimeInForce.Gtc, pos.Quantity, 0, stopPx,
                        currentEntryOcoId, Name + "_AutoStop", Core.Globals.MaxDate, null);
                    var tgtOrder  = acct.CreateOrder(instr, exitAction, OrderType.Limit,
                        PanelOrderEntry, TimeInForce.Gtc, pos.Quantity, tgtPx, 0,
                        currentEntryOcoId, Name + "_AutoTarget", Core.Globals.MaxDate, null);
                    SubmitPanelOrders(acct, new[] { stopOrder, tgtOrder }, "AutoBracket");
                    Print("Auto-exit ▶ Bracket placed — stop=" + stopPx.ToString("F2")
                        + " target=" + tgtPx.ToString("F2"));
                }
            }
            catch (Exception ex) { Print("AutoExit ▶ ERROR: " + ex.Message); }
        }

        // V1.6: Read inline auto-target ticks from panel text box
        private int ReadAutoTargetTicksFromPanel()
        {
            if (tbAutoTargetTicks != null
                && int.TryParse(tbAutoTargetTicks.Text, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int t) && t > 0)
            {
                AutoTargetTicks = t;
                return t;
            }
            return Math.Max(1, AutoTargetTicks);
        }

        // V1.6: Record arm fill for trade stats
        private void RecordArmFillForStats(NinjaTrader.Cbi.Account acct)
        {
            if (!ShowTradeStats || acct == null) return;
            try
            {
                // Snapshot current realized P&L — delta on next exit = trade result
                double realized = acct.Get(AccountItem.RealizedProfitLoss, Currency.UsDollar);
                lastRealizedPnL = realized;
                sessionTradeCount++;
            }
            catch { }
        }

        private void RemoveHalfPosition()
        {
            var acct = ResolveAccount(); if (acct == null) return;
            var instr = ResolveInstrument(); if (instr == null) return;
            var pos = acct.Positions.FirstOrDefault(p => p.Instrument.FullName == instr.FullName);
            if (pos == null || pos.Quantity <= 1) { Print("Half ▶ nothing to remove"); return; }
            int total = pos.Quantity, toRemove = total / 2;
            var exits = acct.Orders.Where(o => o.Instrument.FullName == instr.FullName && (o.OrderType == OrderType.Limit || o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit) && (o.OrderState == OrderState.Working || o.OrderState == OrderState.Accepted)).ToList();
            var bGroups = exits.Where(o => !string.IsNullOrEmpty(o.Oco)).GroupBy(o => o.Oco).ToList();
            if (bGroups.Any())
            {
                int bTotal = bGroups.Sum(g => g.Min(o => o.Quantity)), bRemove = bTotal / 2;
                if (bRemove == 0) { Print("Half ▶ only 1 contract in bracket"); return; }
                var ma = pos.MarketPosition == MarketPosition.Long ? OrderAction.Sell : OrderAction.BuyToCover;
                SubmitPanelOrders(acct, new[] { acct.CreateOrder(instr, ma, OrderType.Market, PanelOrderEntry, TimeInForce.Gtc, bRemove, 0, 0, "", Name + "_Half", Core.Globals.MaxDate, null) }, "Half");
                int rem = bRemove;
                foreach (var grp in bGroups)
                {
                    if (rem == 0) break;
                    var legs = grp.ToList(); int gq = grp.Min(o => o.Quantity), rh = Math.Min(gq, rem), kh = gq - rh;
                    if (grp.Any(o => o.Quantity > 1)) { if (kh > 0) { foreach (var o in legs) o.QuantityChanged = kh; acct.Change(legs.ToArray()); } else acct.Cancel(legs.ToArray()); }
                    else
                    {
                        var tc = new List<NinjaTrader.Cbi.Order>();
                        var st = legs.Where(o => o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit);
                        var tg = legs.Where(o => o.OrderType == OrderType.Limit);
                        if (pos.MarketPosition == MarketPosition.Long) { tc.AddRange(st.OrderByDescending(o => o.StopPrice).Take(rh)); tc.AddRange(tg.OrderBy(o => o.LimitPrice).Take(rh)); }
                        else { tc.AddRange(st.OrderBy(o => o.StopPrice).Take(rh)); tc.AddRange(tg.OrderByDescending(o => o.LimitPrice).Take(rh)); }
                        if (tc.Any()) acct.Cancel(tc.ToArray());
                    }
                    rem -= rh;
                }
                return;
            }
            var ma2 = pos.MarketPosition == MarketPosition.Long ? OrderAction.Sell : OrderAction.BuyToCover;
            SubmitPanelOrders(acct, new[] { acct.CreateOrder(instr, ma2, OrderType.Market, PanelOrderEntry, TimeInForce.Gtc, toRemove, 0, 0, "", Name + "_Half", Core.Globals.MaxDate, null) }, "Half");
            var sa = exits.Where(o => string.IsNullOrEmpty(o.Oco)).ToList();
            if (sa.Any()) { foreach (var o in sa) o.QuantityChanged = total - toRemove; acct.Change(sa.ToArray()); }
        }

        private void DoublePosition()
        {
            var acct = ResolveAccount(); if (acct == null) return;
            var instr = ResolveInstrument(); if (instr == null) return;
            var pos = acct.Positions.FirstOrDefault(p => p.Instrument.FullName == instr.FullName);
            if (pos == null || pos.Quantity <= 0) { Print("Double ▶ no position"); return; }
            bool isLong = pos.MarketPosition == MarketPosition.Long;
            SubmitPanelOrders(acct, new[] { acct.CreateOrder(instr, isLong ? OrderAction.Buy : OrderAction.SellShort, OrderType.Market, PanelOrderEntry, TimeInForce.Gtc, pos.Quantity, 0, 0, "", Name + "_Double", Core.Globals.MaxDate, null) }, "Double");
            foreach (var grp in acct.Orders.Where(o => o.Instrument.FullName == instr.FullName && !string.IsNullOrEmpty(o.Oco) && (o.OrderState == OrderState.Accepted || o.OrderState == OrderState.Working) && (o.OrderType == OrderType.Limit || o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit)).GroupBy(o => o.Oco))
            { int pq = grp.Min(o => o.Quantity), nq = pq * 2; foreach (var o in grp) o.QuantityChanged = nq; acct.Change(grp.ToArray()); }
        }

        private void RemoveStopsAndTargets()
        {
            var acct = ResolveAccount(); if (acct == null) return;
            var instr = ResolveInstrument(); if (instr == null) return;
            var ex = acct.Orders.Where(o => o.Instrument.FullName == instr.FullName && (o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.Limit) && (o.OrderState == OrderState.Working || o.OrderState == OrderState.Accepted)).ToArray();
            if (ex.Length == 0) { Print("Naked ▶ no exits"); return; }
            acct.Cancel(ex); Print("Naked ▶ removed " + ex.Length);
        }

        private void SplitStopsAndTargets()
        {
            var acct = ResolveAccount(); if (acct == null) return;
            var instr = ResolveInstrument(); if (instr == null) return;
            var legs = acct.Orders.Where(o => o.Instrument.FullName == instr.FullName && !string.IsNullOrEmpty(o.Oco) && (o.OrderState == OrderState.Working || o.OrderState == OrderState.Accepted) && (o.OrderType == OrderType.Limit || o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit)).ToList();
            if (legs.Count == 0) { Print("Split ▶ no brackets"); return; }
            var groups = legs.GroupBy(o => o.Oco).ToList();
            bool allSingle = groups.All(g => g.All(o => o.Quantity == 1));
            if (allSingle && groups.Count > 1)
            {
                int tot = groups.Count;
                var f = groups[0].ToList();
                var sl = f.First(o => o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit);
                var tl = f.First(o => o.OrderType == OrderType.Limit);
                bool il = sl.OrderAction == OrderAction.Sell;
                double ts = instr.MasterInstrument.TickSize, bs = sl.StopPrice, bt = tl.LimitPrice;
                acct.Cancel(legs.ToArray());
                var no = new List<NinjaTrader.Cbi.Order>(tot * 2);
                for (int i = 0; i < tot; i++)
                {
                    string oco = Guid.NewGuid().ToString();
                    no.Add(acct.CreateOrder(instr, il ? OrderAction.Sell : OrderAction.BuyToCover, OrderType.StopMarket, PanelOrderEntry, TimeInForce.Gtc, 1, 0, bs, oco, Name + "_Stop" + (i+1), Core.Globals.MaxDate, null));
                    no.Add(acct.CreateOrder(instr, il ? OrderAction.Sell : OrderAction.BuyToCover, OrderType.Limit, PanelOrderEntry, TimeInForce.Gtc, 1, il ? bt + i * ts : bt - i * ts, 0, oco, Name + "_Split" + (i+1), Core.Globals.MaxDate, null));
                }
                SubmitPanelOrders(acct, no.ToArray(), "Split"); Print("Split ▶ " + tot + " pairs"); return;
            }
            foreach (var grp in groups)
            {
                var gl = grp.ToList();
                if (!gl.Any(o => o.Quantity > 1)) { Print("Split ▶ OCO=" + grp.Key + " already per-contract"); continue; }
                var sl = gl.First(o => o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit);
                var ll = gl.First(o => o.OrderType == OrderType.Limit);
                int qty = ll.Quantity; bool il = sl.OrderAction == OrderAction.Sell;
                double bs = sl.StopPrice, bt = ll.LimitPrice, ts = instr.MasterInstrument.TickSize;
                acct.Cancel(gl.ToArray());
                var no = new List<NinjaTrader.Cbi.Order>(qty * 2);
                for (int i = 0; i < qty; i++)
                {
                    string oco = Guid.NewGuid().ToString();
                    no.Add(acct.CreateOrder(instr, il ? OrderAction.Sell : OrderAction.BuyToCover, OrderType.StopMarket, PanelOrderEntry, TimeInForce.Gtc, 1, 0, bs, oco, Name + "_Stop" + (i+1), Core.Globals.MaxDate, null));
                    no.Add(acct.CreateOrder(instr, il ? OrderAction.Sell : OrderAction.BuyToCover, OrderType.Limit, PanelOrderEntry, TimeInForce.Gtc, 1, il ? bt + i * ts : bt - i * ts, 0, oco, Name + "_Split" + (i+1), Core.Globals.MaxDate, null));
                }
                SubmitPanelOrders(acct, no.ToArray(), "Split"); Print("Split ▶ " + qty + " pairs for OCO=" + grp.Key);
            }
        }

        #endregion
    }
}


