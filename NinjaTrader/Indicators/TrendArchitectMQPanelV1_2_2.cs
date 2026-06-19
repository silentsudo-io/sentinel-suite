// ============================================================================
// TrendArchitectMQPanelV1_2_2
// ============================================================================
// A NinjaTrader 8 ChartTrader panel indicator that combines a full suite of
// one-click trade management buttons with the ability to arm automated market
// entries triggered by TrendArchitect's MQB (Marginal Quality Bull) and MQS
// (Marginal Quality Bear) signals.
//
// Credits
// -------
//   Button panel base        : Alighten (AlightenButtonPanelV0004)
//   TrendArchitect indicator : _Jason / B3AR
//   UI design system         : Khanh — DailyRangeBot (open source, used with credit)
//   V1.1 hardening           : Spoobie
//
// Usage
// -----
//   1. Add TrendArchitect to your chart.
//   2. Add this indicator — it injects a panel into the ChartTrader sidebar.
//   3. Click ARM MQB to arm a long entry on the next MQB signal dot.
//      Click ARM MQS to arm a short entry on the next MQS signal dot.
//   4. Entry quantity is read live from the ChartTrader quantity selector.
//   5. Use the Opposite Signal row to choose: Off / Close / Reverse.
//   6. Use the After Entry row to choose: Disarm / ReArm / ReArmMQB / ReArmMQS.
//   7. Click section headers to collapse/expand individual sections.
//   8. Click ↺ in the header to rebuild the panel after changing properties.
// ============================================================================
//
// V1.1 Changes (Spoobie)
// ----------------------
//   1. OrderEntry changed from Automated to Manual (OrderEntry.Manual) for all
//      order submissions — matches ChartTrader button behavior and avoids
//      blocks on live brokerage connections.
//   2. SubmitPanelOrders() — centralized order submission helper that validates
//      account connection status before every Submit() call and logs failures.
//   3. ValidateAccountForTrading() — pre-flight guard checking account is not
//      null and connection status is Connected before any order is attempted.
//   4. Quantity selector completely reworked with 4-tier fallback and
//      ValueChanged event listener for live tracking.
//   5. ResolveAccount() returns SelectedAccount directly.
//   6. ResolveInstrument() falls back to indicator's own Instrument property.
//   7. GetChartTraderWindow() null-safe helper extracted.
//   8. ARM button click handlers reset lastEntryBar to -1 on re-arm.
//   9. lastEntryBar stamped unconditionally on detection.
//  10. NinjaScript generated code block re-added with correct enum types.
//
// V1.2 Changes
// ------------
//   1. Collapsible sections — every section header is clickable; content
//      toggles Visible/Collapsed. Header label row always stays visible.
//   2. Persistent collapse state — open/closed state of each section is
//      stored in NinjaScriptProperty bools and survives chart reloads and
//      workspace saves.
//   3. Rebuild Panel button — ↺ button in the header, left of the version
//      chip. Calls DisposeWPFControls then CreateWPFControls so property
//      changes (e.g. ButtonHeight) apply immediately without reloading.
//   4. ButtonHeight property — user-defined height for action buttons,
//      mode toggle buttons, and ARM buttons. Range 18–40, default 26.
//      Takes effect on panel rebuild.
//   5. Self-contained version — enums nested inside the class; no shared
//      dependencies with any other version of this indicator.
//
// V1.2.1 Changes — Profit Trailing
// ---------------------------------
//   1. PROFIT TRAILING sub-section added inside TRADE MANAGEMENT.
//   2. Seven trail modes, each a toggle button (only one active at a time):
//        Trail Ticks  — fixed N-tick distance trail, every tick
//        BE +N        — one-shot: when up X ticks move stop to entry + N
//        Bar Low/High — stop follows prior bar low (long) / high (short)
//        N-Bar L/H    — stop follows N-bar lowest low / highest high
//        Trail ATR    — stop trails at price ± (multiplier × ATR(period))
//        Trend Magic  — ATR trail gated by CCI regime filter
//        Half + BE    — when up X ticks: close half, move stop to entry + N
//   3. Dynamic parameter panel — shows only relevant fields for active mode.
//      All fields editable at all times.
//   4. Trail runs on every tick in realtime via OnBarUpdate / OnMarketData.
//   5. Works with existing bracket stops — moves them via acct.Change().
//   6. Scope: chart instrument only.
//   7. Labels bolded throughout for readability across NT skin themes.
//
// V1.2.2 Changes — Dynamic Scrolling
// ------------------------------------
//   1. ScrollViewer now binds its MaxHeight to the ChartTrader grid's actual
//      available height via SizeChanged, so the panel always fills the
//      available space exactly without overflowing or getting clipped.
//   2. Mouse wheel scrolling explicitly wired — prevents NT chart from
//      consuming wheel events before the ScrollViewer can handle them.
//   3. Removed fixed MaxHeight = 700 cap.
//   4. ScrollViewer CanContentScroll = false for smooth pixel scrolling.
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
    public class TrendArchitectMQPanelV1_2_2 : Indicator
    {
        // ── Nested enums — self-contained, no cross-version conflicts ─────────
        public enum OppositeSignalMode { Off, Close, Reverse }
        public enum ReArmMode          { Disarm, ReArm, ReArmMQB, ReArmMQS }
        public enum TrailMode          { None, TrailTicks, BreakevenPlus, BarLowHigh, NBarLowHigh, TrailATR, TrendMagic, HalfPlusBE }

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

        // ── ChartTrader plumbing ──────────────────────────────────────────────
        private Chart        _ctChart;
        private Grid         _ctTraderGrid;
        private ScrollViewer _ctScrollViewer;
        private StackPanel   hudStack;
        private bool         _ctPanelActive  = false;
        private bool         uiPanelActive   = false;
        private RowDefinition _ctScrollRow    = null;  // V1.2.2: tracked for dynamic height

        private NinjaTrader.Gui.Tools.AccountSelector    xAcSelector;
        private NinjaTrader.Gui.Tools.InstrumentSelector xInSelector;
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

        // ── Section content panels (for collapse toggle) ──────────────────────
        private StackPanel spSignalArming;
        private StackPanel spOppositeSignal;
        private StackPanel spAfterEntry;
        private StackPanel spTradeMgmt;
        private StackPanel spProfitTrailing;
        private StackPanel spBreakeven;
        private StackPanel spTargets;
        private StackPanel spBracketStop;
        private StackPanel spSize;

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
        [Display(Name = "Section: Trade Mgmt Open",      GroupName = "Panel Layout", Order = 13)]
        public bool SecTradeMgmtOpen      { get; set; } = false;

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Section: Profit Trailing Open", GroupName = "Panel Layout", Order = 14)]
        public bool SecProfitTrailingOpen { get; set; } = false;

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Section: Breakeven Open",       GroupName = "Panel Layout", Order = 15)]
        public bool SecBreakevenOpen      { get; set; } = false;

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Section: Targets Open",         GroupName = "Panel Layout", Order = 16)]
        public bool SecTargetsOpen        { get; set; } = false;

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Section: Bracket/Stop Open",    GroupName = "Panel Layout", Order = 17)]
        public bool SecBracketStopOpen    { get; set; } = false;

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Section: Size Open",            GroupName = "Panel Layout", Order = 18)]
        public bool SecSizeOpen           { get; set; } = false;

        // ═════════════════════════════════════════════════════════════════════
        // Lifecycle
        // ═════════════════════════════════════════════════════════════════════
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description  = @"ChartTrader panel with MQB/MQS signal arming and trade management. Button base by Alighten; TrendArchitect by _Jason/B3AR; UI by Khanh/DailyRangeBot; hardening by Spoobie.";
                Name         = "TrendArchitectMQPanelV1_2_2";
                Calculate    = Calculate.OnEachTick;
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
                ChartControl.Dispatcher.BeginInvoke(new Action(CreateWPFControls));
            }
            else if (State == State.Terminated)
            {
                ChartControl?.Dispatcher.BeginInvoke(new Action(DisposeWPFControls));
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

            bool allowPrevBar = CurrentBar > firstRealtimeBar;
            int[] barsToCheck = allowPrevBar
                ? new[] { CurrentBar, CurrentBar - 1 }
                : new[] { CurrentBar };

            OppositeSignalMode liveOpp    = OppSignalMode;
            ReArmMode          liveReArm  = ReArmAfterEntry;
            ChartControl.Dispatcher.Invoke(() => { liveOpp = OppSignalMode; liveReArm = ReArmAfterEntry; });

            if (mqbArmed)
            {
                foreach (int bar in barsToCheck)
                {
                    if (bar < 0 || bar == lastMqbEntryBar) continue;
                    if (FindChartDrawObject("TA_SIG_" + bar + "_MQB") != null)
                    {
                        bool keepArmed = liveReArm == ReArmMode.ReArm || liveReArm == ReArmMode.ReArmMQB;
                        lastMqbEntryBar = bar;
                        bool didOpposite = HandleOppositePosition(OrderAction.Buy, "MQB", liveOpp);
                        if (!didOpposite) SubmitArmEntry(OrderAction.Buy, "MQB");
                        if (!keepArmed) mqbArmed = false;
                        ChartControl.Dispatcher.InvokeAsync(() => RefreshArmButtons());
                        break;
                    }
                }
            }

            if (mqsArmed)
            {
                foreach (int bar in barsToCheck)
                {
                    if (bar < 0 || bar == lastMqsEntryBar) continue;
                    if (FindChartDrawObject("TA_SIG_" + bar + "_MQS") != null)
                    {
                        bool keepArmed = liveReArm == ReArmMode.ReArm || liveReArm == ReArmMode.ReArmMQS;
                        lastMqsEntryBar = bar;
                        bool didOpposite = HandleOppositePosition(OrderAction.SellShort, "MQS", liveOpp);
                        if (!didOpposite) SubmitArmEntry(OrderAction.SellShort, "MQS");
                        if (!keepArmed) mqsArmed = false;
                        ChartControl.Dispatcher.InvokeAsync(() => RefreshArmButtons());
                        break;
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
            if (activeTrailMode == TrailMode.None) return;
            if (e.MarketDataType != MarketDataType.Last) return;
            ExecuteTrail();
        }

        // ═════════════════════════════════════════════════════════════════════
        // WPF — build / dispose
        // ═════════════════════════════════════════════════════════════════════
        private void CreateWPFControls()
        {
            try
            {
                if (_ctPanelActive) return;
                _ctChart = Window.GetWindow(ChartControl.Parent) as Chart;
                if (_ctChart == null) return;
                ChartTrader ct = _ctChart.FindFirst("ChartWindowChartTraderControl") as ChartTrader;
                if (ct?.Content == null) return;
                _ctTraderGrid = ct.Content as Grid;
                if (_ctTraderGrid == null) return;

                hudStack = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Background  = new SolidColorBrush(C_BG),
                    MinWidth    = 260,
                };

                BuildHeader();
                BuildCollapsibleSection("SIGNAL ARMING",   BuildSignalArmingContent,   ref spSignalArming,   () => SecSignalArmingOpen,   v => SecSignalArmingOpen   = v);
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

                // V1.2.2: Wire mouse wheel so NT chart doesn't consume it first
                _ctScrollViewer.PreviewMouseWheel += OnPanelMouseWheel;

                InsertPanel();
                uiPanelActive = true;
                WireChartTraderQuantitySelector();

                RefreshArmButtons();
                RefreshOppButtons();
                RefreshReArmButtons();
            }
            catch (Exception ex) { Print("[TrendArchitectMQPanelV1_2_2] CreateWPFControls error: " + ex.Message); }
        }

        private void DisposeWPFControls()
        {
            try
            {
                DetachChartTraderQuantitySelector();
                if (_ctScrollViewer != null) _ctScrollViewer.Content = null;
                RemovePanel();
                _ctScrollViewer = null;
                _ctTraderGrid   = null;
                _ctChart        = null;
                uiPanelActive   = false;
            }
            catch { }
        }

        private void RebuildPanel()
        {
            DisposeWPFControls();
            ChartControl.Dispatcher.BeginInvoke(new Action(CreateWPFControls));
        }

        // ── Panel insertion / removal ─────────────────────────────────────────
        private void InsertPanel()
        {
            if (_ctPanelActive || _ctTraderGrid == null || _ctScrollViewer == null) return;
            if (_ctTraderGrid.Children.Contains(_ctScrollViewer)) return;

            // V1.2.2: Use Star height so the row stretches to fill available space,
            // then bind ScrollViewer MaxHeight to the actual rendered row height.
            _ctScrollRow = new RowDefinition { Height = new GridLength(1, GridUnitType.Star) };
            _ctTraderGrid.RowDefinitions.Add(_ctScrollRow);
            Grid.SetRow(_ctScrollViewer, _ctTraderGrid.RowDefinitions.Count - 1);
            if (_ctTraderGrid.ColumnDefinitions.Count > 0)
                Grid.SetColumnSpan(_ctScrollViewer, _ctTraderGrid.ColumnDefinitions.Count);
            _ctTraderGrid.Children.Add(_ctScrollViewer);

            // Bind MaxHeight dynamically to whatever height NT gives the row
            _ctTraderGrid.SizeChanged += OnCtGridSizeChanged;
            UpdateScrollViewerHeight();

            _ctPanelActive = true;
        }

        // V1.2.2: Keep ScrollViewer height in sync with ChartTrader grid
        private void OnCtGridSizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateScrollViewerHeight();
        }

        private void UpdateScrollViewerHeight()
        {
            if (_ctScrollViewer == null || _ctTraderGrid == null) return;
            try
            {
                // Calculate how much height rows ABOVE our panel consume
                double usedHeight = 0;
                int ourRowIdx = _ctTraderGrid.RowDefinitions.Count - 1;
                for (int i = 0; i < ourRowIdx; i++)
                    usedHeight += _ctTraderGrid.RowDefinitions[i].ActualHeight;

                double available = _ctTraderGrid.ActualHeight - usedHeight;
                if (available > 50)   // sanity minimum
                    _ctScrollViewer.MaxHeight = available;
            }
            catch { }
        }

        // V1.2.2: Consume mouse wheel events so NT chart doesn't steal them
        private void OnPanelMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_ctScrollViewer == null) return;
            double newOffset = _ctScrollViewer.VerticalOffset - e.Delta;
            newOffset = Math.Max(0, Math.Min(newOffset, _ctScrollViewer.ScrollableHeight));
            _ctScrollViewer.ScrollToVerticalOffset(newOffset);
            e.Handled = true;
        }

        private void RemovePanel()
        {
            if (!_ctPanelActive || _ctTraderGrid == null) return;
            // V1.2.2: Detach event handlers before removing
            _ctTraderGrid.SizeChanged -= OnCtGridSizeChanged;
            if (_ctScrollViewer != null)
            {
                _ctScrollViewer.PreviewMouseWheel -= OnPanelMouseWheel;
                _ctTraderGrid.Children.Remove(_ctScrollViewer);
            }
            if (_ctScrollRow != null)
            {
                _ctTraderGrid.RowDefinitions.Remove(_ctScrollRow);
                _ctScrollRow = null;
            }
            else if (_ctTraderGrid.RowDefinitions.Count > 0)
            {
                _ctTraderGrid.RowDefinitions.RemoveAt(_ctTraderGrid.RowDefinitions.Count - 1);
            }
            _ctPanelActive = false;
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
            left.Children.Add(Tx("TREND ARCHITECT", 13, C_TEXT, bold: true));
            left.Children.Add(Tx("  MQ PANEL", 10, C_MUTED));

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
                Child        = Tx("v1.2.2", 10, C_MUTED),
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

        private void BuildSignalArmingContent(StackPanel sp)
        {
            var armRow = new Grid { Margin = new Thickness(6, 4, 6, 4) };
            armRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            armRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            btnArmMQB = new Button
            {
                Content         = "▲  ARM MQB",
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
                Content         = "▼  ARM MQS",
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
                if (mqbArmed) { lastMqbEntryBar = -1; activeTrailMode = TrailMode.None; trailStopLevel = double.MinValue; beTriggered = false; halfTriggered = false; }
                Print("ARM MQB ▶ " + (mqbArmed ? "ARMED" : "disarmed"));
                RefreshArmButtons();
            };
            btnArmMQS.Click += (s, e) =>
            {
                mqsArmed = !mqsArmed;
                if (mqsArmed) { lastMqsEntryBar = -1; activeTrailMode = TrailMode.None; trailStopLevel = double.MinValue; beTriggered = false; halfTriggered = false; }
                Print("ARM MQS ▶ " + (mqsArmed ? "ARMED" : "disarmed"));
                RefreshArmButtons();
            };
            Grid.SetColumn(btnArmMQB, 0);
            Grid.SetColumn(btnArmMQS, 1);
            armRow.Children.Add(btnArmMQB);
            armRow.Children.Add(btnArmMQS);
            sp.Children.Add(armRow);
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

            card.Child = inner;
            sp.Children.Add(card);

            RefreshTrailButtons();
            RefreshTrailParams();
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

        private void BuildBracketStopContent(StackPanel sp)
        {
            var card = Card();
            card.Child = TwoColBtns(
                ActionBtn("Bracket",  C_PURPLE, () => BracketOrder(BracketStopTicks, BracketProfitTicks)),
                ActionBtn("Add Stop", C_PURPLE, () => AddStopOrder(BracketStopTicks))
            );
            sp.Children.Add(card);
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
            if (btnArmMQB != null)
            {
                btnArmMQB.Background      = new SolidColorBrush(mqbArmed ? Color.FromArgb(80, C_GREEN.R, C_GREEN.G, C_GREEN.B) : Color.FromArgb(40, C_DIM.R, C_DIM.G, C_DIM.B));
                btnArmMQB.Foreground      = new SolidColorBrush(mqbArmed ? C_GREEN : C_MUTED);
                btnArmMQB.BorderBrush     = new SolidColorBrush(mqbArmed ? C_GREEN : C_BORDER);
                btnArmMQB.BorderThickness = new Thickness(mqbArmed ? 2 : 1);
                btnArmMQB.Content         = mqbArmed ? "■ ARMED MQB ▲" : "▲  ARM MQB";
            }
            if (btnArmMQS != null)
            {
                btnArmMQS.Background      = new SolidColorBrush(mqsArmed ? Color.FromArgb(80, C_RED.R, C_RED.G, C_RED.B) : Color.FromArgb(40, C_DIM.R, C_DIM.G, C_DIM.B));
                btnArmMQS.Foreground      = new SolidColorBrush(mqsArmed ? C_RED : C_MUTED);
                btnArmMQS.BorderBrush     = new SolidColorBrush(mqsArmed ? C_RED : C_BORDER);
                btnArmMQS.BorderThickness = new Thickness(mqsArmed ? 2 : 1);
                btnArmMQS.Content         = mqsArmed ? "■ ARMED MQS ▼" : "▼  ARM MQS";
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

        private IDrawingTool FindChartDrawObject(string tag)
        {
            try { foreach (var ind in ChartControl.Indicators) { var o = ind.DrawObjects[tag]; if (o != null) return o; } }
            catch { }
            return null;
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
                DetachChartTraderQuantitySelector();
                QuantityUpDown found = (_ctTraderGrid?.FindFirst("ChartTraderControlQuantitySelector")
                    ?? GetChartTraderWindow()?.FindFirst("ChartTraderControlQuantitySelector")) as QuantityUpDown;
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
                int fromSel = QuantityFromUpDown(cachedQtySelector ?? (_ctTraderGrid?.FindFirst("ChartTraderControlQuantitySelector") as QuantityUpDown));
                if (fromSel > 0) { lastKnownChartTraderQty = fromSel; source = "QuantitySelector"; return fromSel; }
                Grid grid = _ctTraderGrid;
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
        // Trail execution — called on every tick while a mode is active
        // ─────────────────────────────────────────────────────────────────────
        private void ExecuteTrail()
        {
            var acct  = ResolveAccount();    if (acct == null)  return;
            var instr = ResolveInstrument(); if (instr == null) return;
            var pos   = acct.Positions.FirstOrDefault(p => p.Instrument.FullName == instr.FullName);
            if (pos == null || pos.Quantity == 0) return;

            bool isLong  = pos.MarketPosition == MarketPosition.Long;
            double price = isLong ? Close[0] : Close[0];   // current last price
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
            MoveOrPlaceStop(acct, instr, pos, newStop);
        }

        private void MoveOrPlaceStop(NinjaTrader.Cbi.Account acct,
                                     NinjaTrader.Cbi.Instrument instr,
                                     Position pos, double stopPrice)
        {
            try
            {
                bool isLong = pos.MarketPosition == MarketPosition.Long;
                OrderAction stopAction = isLong ? OrderAction.Sell : OrderAction.BuyToCover;

                // Find existing stop orders (bracket or naked)
                var existingStops = acct.Orders
                    .Where(o => o.Instrument.FullName == instr.FullName
                             && (o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit)
                             && (o.OrderState == OrderState.Working || o.OrderState == OrderState.Accepted)
                             && o.OrderAction == stopAction)
                    .ToList();

                if (existingStops.Any())
                {
                    // Move existing stop(s)
                    foreach (var s in existingStops)
                    {
                        s.StopPriceChanged = stopPrice;
                        acct.Change(new[] { s });
                    }
                }
                else
                {
                    // No stop exists — place a new naked stop
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
            if (acct.Orders.Any(o => o.Instrument.FullName == instr.FullName && o.OrderType == OrderType.StopMarket && (o.OrderState == OrderState.Accepted || o.OrderState == OrderState.Working))) { Print("AddStop ▶ stop exists"); return; }
            bool isLong = pos.MarketPosition == MarketPosition.Long;
            double bp = instr.MasterInstrument.RoundToTickSize(lastClose);
            double sp = isLong ? bp - stopTicks * instr.MasterInstrument.TickSize : bp + stopTicks * instr.MasterInstrument.TickSize;
            SubmitPanelOrders(acct, new[] { acct.CreateOrder(instr, isLong ? OrderAction.Sell : OrderAction.BuyToCover, OrderType.StopMarket, PanelOrderEntry, TimeInForce.Gtc, pos.Quantity, 0, sp, "", Name + "_AddStop", Core.Globals.MaxDate, null) }, "AddStop");
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

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
    public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
    {
        private TrendArchitectMQPanelV1_2_2[] cacheTrendArchitectMQPanelV1_2_2;

        public TrendArchitectMQPanelV1_2_2 TrendArchitectMQPanelV1_2_2(int breakeven1PlusTicks, int breakeven2PlusTicks, int pricePlusTicks, int entryPlusTicks, int bracketStopTicks, int bracketProfitTicks, int flattenAllPause, int flattenAllTries, TrendArchitectMQPanelV1_2_2.OppositeSignalMode oppSignalMode, TrendArchitectMQPanelV1_2_2.ReArmMode reArmAfterEntry, int buttonHeight, bool secSignalArmingOpen, bool secOppositeSignalOpen, bool secAfterEntryOpen, bool secTradeMgmtOpen, bool secBreakevenOpen, bool secTargetsOpen, bool secBracketStopOpen, bool secSizeOpen)
        {
            return TrendArchitectMQPanelV1_2_2(Input, breakeven1PlusTicks, breakeven2PlusTicks, pricePlusTicks, entryPlusTicks, bracketStopTicks, bracketProfitTicks, flattenAllPause, flattenAllTries, oppSignalMode, reArmAfterEntry, buttonHeight, secSignalArmingOpen, secOppositeSignalOpen, secAfterEntryOpen, secTradeMgmtOpen, secBreakevenOpen, secTargetsOpen, secBracketStopOpen, secSizeOpen);
        }

        public TrendArchitectMQPanelV1_2_2 TrendArchitectMQPanelV1_2_2(ISeries<double> input, int breakeven1PlusTicks, int breakeven2PlusTicks, int pricePlusTicks, int entryPlusTicks, int bracketStopTicks, int bracketProfitTicks, int flattenAllPause, int flattenAllTries, TrendArchitectMQPanelV1_2_2.OppositeSignalMode oppSignalMode, TrendArchitectMQPanelV1_2_2.ReArmMode reArmAfterEntry, int buttonHeight, bool secSignalArmingOpen, bool secOppositeSignalOpen, bool secAfterEntryOpen, bool secTradeMgmtOpen, bool secBreakevenOpen, bool secTargetsOpen, bool secBracketStopOpen, bool secSizeOpen)
        {
            if (cacheTrendArchitectMQPanelV1_2_2 != null)
                for (int idx = 0; idx < cacheTrendArchitectMQPanelV1_2_2.Length; idx++)
                    if (cacheTrendArchitectMQPanelV1_2_2[idx] != null
                        && cacheTrendArchitectMQPanelV1_2_2[idx].Breakeven1PlusTicks == breakeven1PlusTicks
                        && cacheTrendArchitectMQPanelV1_2_2[idx].Breakeven2PlusTicks == breakeven2PlusTicks
                        && cacheTrendArchitectMQPanelV1_2_2[idx].PricePlusTicks      == pricePlusTicks
                        && cacheTrendArchitectMQPanelV1_2_2[idx].EntryPlusTicks      == entryPlusTicks
                        && cacheTrendArchitectMQPanelV1_2_2[idx].BracketStopTicks    == bracketStopTicks
                        && cacheTrendArchitectMQPanelV1_2_2[idx].BracketProfitTicks  == bracketProfitTicks
                        && cacheTrendArchitectMQPanelV1_2_2[idx].FlattenAllPause     == flattenAllPause
                        && cacheTrendArchitectMQPanelV1_2_2[idx].FlattenAllTries     == flattenAllTries
                        && cacheTrendArchitectMQPanelV1_2_2[idx].OppSignalMode       == oppSignalMode
                        && cacheTrendArchitectMQPanelV1_2_2[idx].ReArmAfterEntry     == reArmAfterEntry
                        && cacheTrendArchitectMQPanelV1_2_2[idx].ButtonHeight        == buttonHeight
                        && cacheTrendArchitectMQPanelV1_2_2[idx].SecTradeMgmtOpen   == secTradeMgmtOpen
                        && cacheTrendArchitectMQPanelV1_2_2[idx].EqualsInput(input))
                        return cacheTrendArchitectMQPanelV1_2_2[idx];

            return CacheIndicator<TrendArchitectMQPanelV1_2_2>(new TrendArchitectMQPanelV1_2_2
            {
                Breakeven1PlusTicks  = breakeven1PlusTicks,
                Breakeven2PlusTicks  = breakeven2PlusTicks,
                PricePlusTicks       = pricePlusTicks,
                EntryPlusTicks       = entryPlusTicks,
                BracketStopTicks     = bracketStopTicks,
                BracketProfitTicks   = bracketProfitTicks,
                FlattenAllPause      = flattenAllPause,
                FlattenAllTries      = flattenAllTries,
                OppSignalMode        = oppSignalMode,
                ReArmAfterEntry      = reArmAfterEntry,
                ButtonHeight         = buttonHeight,
                SecSignalArmingOpen  = secSignalArmingOpen,
                SecOppositeSignalOpen = secOppositeSignalOpen,
                SecAfterEntryOpen    = secAfterEntryOpen,
                SecTradeMgmtOpen     = secTradeMgmtOpen,
                SecBreakevenOpen     = secBreakevenOpen,
                SecTargetsOpen       = secTargetsOpen,
                SecBracketStopOpen   = secBracketStopOpen,
                SecSizeOpen          = secSizeOpen,
            }, input, ref cacheTrendArchitectMQPanelV1_2_2);
        }
    }
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
    public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
    {
        public Indicators.TrendArchitectMQPanelV1_2_2 TrendArchitectMQPanelV1_2_2(int breakeven1PlusTicks, int breakeven2PlusTicks, int pricePlusTicks, int entryPlusTicks, int bracketStopTicks, int bracketProfitTicks, int flattenAllPause, int flattenAllTries, TrendArchitectMQPanelV1_2_2.OppositeSignalMode oppSignalMode, TrendArchitectMQPanelV1_2_2.ReArmMode reArmAfterEntry, int buttonHeight, bool secSignalArmingOpen, bool secOppositeSignalOpen, bool secAfterEntryOpen, bool secTradeMgmtOpen, bool secBreakevenOpen, bool secTargetsOpen, bool secBracketStopOpen, bool secSizeOpen)
        { return indicator.TrendArchitectMQPanelV1_2_2(Input, breakeven1PlusTicks, breakeven2PlusTicks, pricePlusTicks, entryPlusTicks, bracketStopTicks, bracketProfitTicks, flattenAllPause, flattenAllTries, oppSignalMode, reArmAfterEntry, buttonHeight, secSignalArmingOpen, secOppositeSignalOpen, secAfterEntryOpen, secTradeMgmtOpen, secBreakevenOpen, secTargetsOpen, secBracketStopOpen, secSizeOpen); }

        public Indicators.TrendArchitectMQPanelV1_2_2 TrendArchitectMQPanelV1_2_2(ISeries<double> input, int breakeven1PlusTicks, int breakeven2PlusTicks, int pricePlusTicks, int entryPlusTicks, int bracketStopTicks, int bracketProfitTicks, int flattenAllPause, int flattenAllTries, TrendArchitectMQPanelV1_2_2.OppositeSignalMode oppSignalMode, TrendArchitectMQPanelV1_2_2.ReArmMode reArmAfterEntry, int buttonHeight, bool secSignalArmingOpen, bool secOppositeSignalOpen, bool secAfterEntryOpen, bool secTradeMgmtOpen, bool secBreakevenOpen, bool secTargetsOpen, bool secBracketStopOpen, bool secSizeOpen)
        { return indicator.TrendArchitectMQPanelV1_2_2(input, breakeven1PlusTicks, breakeven2PlusTicks, pricePlusTicks, entryPlusTicks, bracketStopTicks, bracketProfitTicks, flattenAllPause, flattenAllTries, oppSignalMode, reArmAfterEntry, buttonHeight, secSignalArmingOpen, secOppositeSignalOpen, secAfterEntryOpen, secTradeMgmtOpen, secBreakevenOpen, secTargetsOpen, secBracketStopOpen, secSizeOpen); }
    }
}

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
    {
        public Indicators.TrendArchitectMQPanelV1_2_2 TrendArchitectMQPanelV1_2_2(int breakeven1PlusTicks, int breakeven2PlusTicks, int pricePlusTicks, int entryPlusTicks, int bracketStopTicks, int bracketProfitTicks, int flattenAllPause, int flattenAllTries, TrendArchitectMQPanelV1_2_2.OppositeSignalMode oppSignalMode, TrendArchitectMQPanelV1_2_2.ReArmMode reArmAfterEntry, int buttonHeight, bool secSignalArmingOpen, bool secOppositeSignalOpen, bool secAfterEntryOpen, bool secTradeMgmtOpen, bool secBreakevenOpen, bool secTargetsOpen, bool secBracketStopOpen, bool secSizeOpen)
        { return indicator.TrendArchitectMQPanelV1_2_2(Input, breakeven1PlusTicks, breakeven2PlusTicks, pricePlusTicks, entryPlusTicks, bracketStopTicks, bracketProfitTicks, flattenAllPause, flattenAllTries, oppSignalMode, reArmAfterEntry, buttonHeight, secSignalArmingOpen, secOppositeSignalOpen, secAfterEntryOpen, secTradeMgmtOpen, secBreakevenOpen, secTargetsOpen, secBracketStopOpen, secSizeOpen); }

        public Indicators.TrendArchitectMQPanelV1_2_2 TrendArchitectMQPanelV1_2_2(ISeries<double> input, int breakeven1PlusTicks, int breakeven2PlusTicks, int pricePlusTicks, int entryPlusTicks, int bracketStopTicks, int bracketProfitTicks, int flattenAllPause, int flattenAllTries, TrendArchitectMQPanelV1_2_2.OppositeSignalMode oppSignalMode, TrendArchitectMQPanelV1_2_2.ReArmMode reArmAfterEntry, int buttonHeight, bool secSignalArmingOpen, bool secOppositeSignalOpen, bool secAfterEntryOpen, bool secTradeMgmtOpen, bool secBreakevenOpen, bool secTargetsOpen, bool secBracketStopOpen, bool secSizeOpen)
        { return indicator.TrendArchitectMQPanelV1_2_2(input, breakeven1PlusTicks, breakeven2PlusTicks, pricePlusTicks, entryPlusTicks, bracketStopTicks, bracketProfitTicks, flattenAllPause, flattenAllTries, oppSignalMode, reArmAfterEntry, buttonHeight, secSignalArmingOpen, secOppositeSignalOpen, secAfterEntryOpen, secTradeMgmtOpen, secBreakevenOpen, secTargetsOpen, secBracketStopOpen, secSizeOpen); }
    }
}

#endregion
