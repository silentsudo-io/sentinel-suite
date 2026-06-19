// ============================================================================
// TrendArchitectMQPanelV1_1
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
//   V1.1 hardening             : Spoobie
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
// ============================================================================
//
// V1.1 Changes (Spoobie)
// ----------------------
//   1. OrderEntry changed from Automated to Manual (OrderEntry.Manual) for all
//      order submissions — matches ChartTrader button behavior and avoids
//      blocks on live brokerage connections.
//
//   2. SubmitPanelOrders() — centralized order submission helper that validates
//      account connection status before every Submit() call and logs failures.
//
//   3. ValidateAccountForTrading() — pre-flight guard checking account is not
//      null and connection status is Connected before any order is attempted.
//
//   4. Quantity selector completely reworked:
//        - Wires a ValueChanged event listener to the native QuantityUpDown
//          control at panel creation (WireChartTraderQuantitySelector).
//        - Caches the last known quantity in lastKnownChartTraderQty.
//        - ReadChartTraderQuantity() now uses a 4-tier fallback:
//            QuantitySelector → QuantityEdit → lastKnown → ChartTrader.Quantity
//        - Logs which source was used on each entry submission.
//        - TryParseQuantityText() handles comma-formatted and decimal strings.
//        - DetachChartTraderQuantitySelector() cleanly unhooks the event on
//          indicator disposal to prevent memory leaks.
//
//   5. ResolveAccount() now returns xAcSelector.SelectedAccount directly
//      rather than doing a fragile display-name string match against Account.All.
//
//   6. ResolveInstrument() falls back to the indicator's own Instrument
//      property if the ChartTrader selector lookup returns null.
//
//   7. GetChartTraderWindow() — null-safe helper extracted from repeated
//      Window.GetWindow(ChartControl.Parent) call sites.
//
//   8. ARM button click handlers now reset lastMqbEntryBar / lastMqsEntryBar
//      to -1 on manual re-arm, ensuring a fresh arm always detects the next
//      signal dot cleanly.
//
//   9. lastMqbEntryBar / lastMqsEntryBar stamped unconditionally on detection
//      (regardless of ReArm mode) to prevent OnEachTick from re-submitting
//      on every tick while the signal dot exists on the current bar.
//
//  10. NinjaScript generated code block re-added with correct enum-typed
//      parameters (OppositeSignalMode, ReArmMode) for cache/wrapper methods.
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
    public class TrendArchitectMQPanelV1_1 : Indicator
    {
        // ── Nested enums — self-contained per version, no cross-version conflicts ──
        // Each version of this indicator declares its own copies so multiple
        // versions can coexist in the indicators folder without CS0101 errors.
        public enum OppositeSignalMode { Off, Close, Reverse }
        public enum ReArmMode          { Disarm, ReArm, ReArmMQB, ReArmMQS }

        // ── Dark terminal palette (matches DailyRangeBot / EliteAT) ──────────
        private static readonly Color C_BG     = Color.FromRgb(13,  15,  20);
        private static readonly Color C_CARD   = Color.FromRgb(19,  22,  30);
        private static readonly Color C_BORDER = Color.FromRgb(30,  35,  48);
        private static readonly Color C_DIM    = Color.FromRgb(40,  45,  58);
        private static readonly Color C_MUTED  = Color.FromRgb(90,  96, 112);
        private static readonly Color C_TEXT   = Color.FromRgb(232, 234, 240);
        private static readonly Color C_GREEN  = Color.FromRgb(0,   212, 160);
        private static readonly Color C_RED    = Color.FromRgb(255,  77, 106);
        private static readonly Color C_AMBER  = Color.FromRgb(245, 158,  11);
        private static readonly Color C_BLUE   = Color.FromRgb(59,  130, 246);
        private static readonly Color C_PURPLE = Color.FromRgb(167, 105, 255);

        // ── ChartTrader plumbing ─────────────────────────────────────────────
        private Chart        _ctChart;
        private Grid         _ctTraderGrid;
        private ScrollViewer _ctScrollViewer;
        private StackPanel   hudStack;
        private bool         _ctPanelActive = false;

        private NinjaTrader.Gui.Tools.AccountSelector  xAcSelector;
        private NinjaTrader.Gui.Tools.InstrumentSelector xInSelector;
        private QuantityUpDown cachedQtySelector;
        private RoutedEventHandler cachedQtyValueChangedHandler;
        private volatile int   lastKnownChartTraderQty = 1;

        private double   lastClose      = 0;
        private volatile bool isFlatteningAll = false;

        // ── MQB / MQS arming state ───────────────────────────────────────────
        private Button   btnArmMQB;
        private Button   btnArmMQS;

        // Opposite Signal toggle buttons
        private Button   btnOppOff;
        private Button   btnOppClose;
        private Button   btnOppReverse;

        // After Entry toggle buttons
        private Button   btnReArmDisarm;
        private Button   btnReArmReArm;
        private Button   btnReArmMQB;
        private Button   btnReArmMQS;

        private volatile bool mqbArmed      = false;
        private volatile bool mqsArmed      = false;

        private int lastMqbEntryBar  = -1;
        private int lastMqsEntryBar  = -1;
        private int firstRealtimeBar = -1;

        // ── Parameters — Trading ─────────────────────────────────────────────
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

        // ── Parameters — Signal Arming ───────────────────────────────────────
        [NinjaScriptProperty]
        [Display(Name = "Opposite Signal Mode", Order = 0, GroupName = "Signal Arming")]
        public OppositeSignalMode OppSignalMode { get; set; } = OppositeSignalMode.Close;

        [NinjaScriptProperty]
        [Display(Name = "Re-Arm Mode", Order = 1, GroupName = "Signal Arming")]
        public ReArmMode ReArmAfterEntry { get; set; } = ReArmMode.Disarm;

        // ═════════════════════════════════════════════════════════════════════
        // Lifecycle
        // ═════════════════════════════════════════════════════════════════════
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description  = @"ChartTrader panel with MQB/MQS signal arming and trade management. Button base by Alighten; TrendArchitect by _Jason/B3AR; UI by DailyRangeBot.";
                Name         = "TrendArchitectMQPanelV1_1";
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
        // OnBarUpdate — signal detection
        // ═════════════════════════════════════════════════════════════════════
        protected override void OnBarUpdate()
        {
            lastClose = Close[0];
            if (State != State.Realtime) return;

            if (firstRealtimeBar < 0)
                firstRealtimeBar = CurrentBar;

            bool allowPrevBar = CurrentBar > firstRealtimeBar;
            int[] barsToCheck = allowPrevBar
                ? new[] { CurrentBar, CurrentBar - 1 }
                : new[] { CurrentBar };

            // Read live mode values from UI thread
            OppositeSignalMode liveOpp  = OppSignalMode;
            ReArmMode          liveReArm = ReArmAfterEntry;
            ChartControl.Dispatcher.Invoke(() =>
            {
                liveOpp   = OppSignalMode;
                liveReArm = ReArmAfterEntry;
            });

            // ── MQB detection ─────────────────────────────────────────────────
            if (mqbArmed)
            {
                foreach (int bar in barsToCheck)
                {
                    if (bar < 0 || bar == lastMqbEntryBar) continue;
                    var mqbDot = FindChartDrawObject("TA_SIG_" + bar + "_MQB");
                    if (mqbDot != null)
                    {
                        bool keepArmed = liveReArm == ReArmMode.ReArm || liveReArm == ReArmMode.ReArmMQB;
                        // One action per signal bar (OnEachTick would otherwise submit every tick while dot exists).
                        lastMqbEntryBar = bar;

                        bool didOpposite = HandleOppositePosition(OrderAction.Buy, "MQB", liveOpp);
                        if (!didOpposite) SubmitArmEntry(OrderAction.Buy, "MQB");

                        if (!keepArmed) mqbArmed = false;
                        ChartControl.Dispatcher.InvokeAsync(() => RefreshArmButtons());
                        break;
                    }
                }
            }

            // ── MQS detection ─────────────────────────────────────────────────
            if (mqsArmed)
            {
                foreach (int bar in barsToCheck)
                {
                    if (bar < 0 || bar == lastMqsEntryBar) continue;
                    var mqsDot = FindChartDrawObject("TA_SIG_" + bar + "_MQS");
                    if (mqsDot != null)
                    {
                        bool keepArmed = liveReArm == ReArmMode.ReArm || liveReArm == ReArmMode.ReArmMQS;
                        // V1.1 (Spoobie): Always stamp the bar — same reason as MQB above.
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

        // ═════════════════════════════════════════════════════════════════════
        // WPF — build
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
                BuildDivider("SIGNAL ARMING");
                BuildArmSection();
                BuildDivider("TRADE MANAGEMENT");
                BuildTradeManagementSection();

                _ctScrollViewer = new ScrollViewer
                {
                    Content                       = hudStack,
                    VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    MaxHeight                     = 700,
                    Background                    = new SolidColorBrush(C_BG),
                };

                InsertPanel();
                uiPanelActive = true;
                WireChartTraderQuantitySelector();
            }
            catch (Exception ex) { Print("[TrendArchitectMQPanelV1_1] CreateWPFControls error: " + ex.Message); }
        }

        private bool uiPanelActive = false;

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

        // ── Panel insertion / removal ─────────────────────────────────────────
        private void InsertPanel()
        {
            if (_ctPanelActive || _ctTraderGrid == null || _ctScrollViewer == null) return;
            if (_ctTraderGrid.Children.Contains(_ctScrollViewer)) return;
            _ctTraderGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(_ctScrollViewer, _ctTraderGrid.RowDefinitions.Count - 1);
            if (_ctTraderGrid.ColumnDefinitions.Count > 0)
                Grid.SetColumnSpan(_ctScrollViewer, _ctTraderGrid.ColumnDefinitions.Count);
            _ctTraderGrid.Children.Add(_ctScrollViewer);
            _ctPanelActive = true;
        }

        private void RemovePanel()
        {
            if (!_ctPanelActive || _ctTraderGrid == null) return;
            if (_ctScrollViewer != null) _ctTraderGrid.Children.Remove(_ctScrollViewer);
            if (_ctTraderGrid.RowDefinitions.Count > 0)
                _ctTraderGrid.RowDefinitions.RemoveAt(_ctTraderGrid.RowDefinitions.Count - 1);
            _ctPanelActive = false;
        }

        // ═════════════════════════════════════════════════════════════════════
        // UI sections
        // ═════════════════════════════════════════════════════════════════════

        // ── Header ────────────────────────────────────────────────────────────
        private void BuildHeader()
        {
            var g = new Grid { Margin = new Thickness(8, 6, 8, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel { Orientation = Orientation.Horizontal,
                                        VerticalAlignment = VerticalAlignment.Center };
            var dot = new System.Windows.Shapes.Ellipse
            {
                Width  = 8, Height = 8,
                Fill   = new SolidColorBrush(C_GREEN),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 7, 0),
            };
            left.Children.Add(dot);
            left.Children.Add(Tx("TREND ARCHITECT", 13, C_TEXT, bold: true));
            left.Children.Add(Tx("  MQ PANEL", 10, C_MUTED));

            var chip = new Border
            {
                Background   = new SolidColorBrush(C_DIM),
                CornerRadius = new CornerRadius(4),
                Padding      = new Thickness(6, 3, 6, 3),
                Child        = Tx("v1.1", 10, C_MUTED),
            };

            Grid.SetColumn(left, 0); Grid.SetColumn(chip, 1);
            g.Children.Add(left); g.Children.Add(chip);
            hudStack.Children.Add(g);
            hudStack.Children.Add(HRule());
        }

        // ── Signal Arming section ─────────────────────────────────────────────
        private void BuildArmSection()
        {
            // ── ARM MQB / ARM MQS row ─────────────────────────────────────────
            var armRow = new Grid { Margin = new Thickness(6, 4, 6, 2) };
            armRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            armRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            btnArmMQB = new Button
            {
                Content         = "▲  ARM MQB",
                FontSize        = 12,
                FontWeight      = FontWeights.Bold,
                Padding         = new Thickness(0, 6, 0, 6),
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
                Padding         = new Thickness(0, 6, 0, 6),
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
                if (mqbArmed) lastMqbEntryBar = -1; // V1.1 (Spoobie): Reset on re-arm so next signal is always detected fresh
                Print("ARM MQB ▶ " + (mqbArmed ? "ARMED" : "disarmed"));
                RefreshArmButtons();
            };
            btnArmMQS.Click += (s, e) =>
            {
                mqsArmed = !mqsArmed;
                if (mqsArmed) lastMqsEntryBar = -1; // V1.1 (Spoobie): Reset on re-arm so next signal is always detected fresh
                Print("ARM MQS ▶ " + (mqsArmed ? "ARMED" : "disarmed"));
                RefreshArmButtons();
            };
            Grid.SetColumn(btnArmMQB, 0);
            Grid.SetColumn(btnArmMQS, 1);
            armRow.Children.Add(btnArmMQB);
            armRow.Children.Add(btnArmMQS);
            hudStack.Children.Add(armRow);

            // ── Opposite Signal row ───────────────────────────────────────────
            var oppCard = Card();
            var oppSp   = new StackPanel();
            oppSp.Children.Add(Tx("OPPOSITE SIGNAL", 9, C_MUTED));
            oppSp.Children.Add(new Border
            {
                BorderBrush = new SolidColorBrush(C_BORDER),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Margin = new Thickness(0, 3, 0, 4),
            });

            var oppRow = new Grid();
            oppRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            oppRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            oppRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            btnOppOff     = ModeBtn("Off");
            btnOppClose   = ModeBtn("Close");
            btnOppReverse = ModeBtn("Reverse");

            btnOppOff.Click     += (s, e) => { OppSignalMode = OppositeSignalMode.Off;     RefreshOppButtons(); };
            btnOppClose.Click   += (s, e) => { OppSignalMode = OppositeSignalMode.Close;   RefreshOppButtons(); };
            btnOppReverse.Click += (s, e) => { OppSignalMode = OppositeSignalMode.Reverse; RefreshOppButtons(); };

            Grid.SetColumn(btnOppOff,     0);
            Grid.SetColumn(btnOppClose,   1);
            Grid.SetColumn(btnOppReverse, 2);
            oppRow.Children.Add(btnOppOff);
            oppRow.Children.Add(btnOppClose);
            oppRow.Children.Add(btnOppReverse);
            oppSp.Children.Add(oppRow);
            oppCard.Child = oppSp;
            hudStack.Children.Add(oppCard);
            RefreshOppButtons();

            // ── After Entry row ───────────────────────────────────────────────
            var reArmCard = Card();
            var reArmSp   = new StackPanel();
            reArmSp.Children.Add(Tx("AFTER ENTRY", 9, C_MUTED));
            reArmSp.Children.Add(new Border
            {
                BorderBrush = new SolidColorBrush(C_BORDER),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Margin = new Thickness(0, 3, 0, 4),
            });

            // Row 1: Disarm | ReArm
            var reArmRow1 = new Grid { Margin = new Thickness(0, 0, 0, 2) };
            reArmRow1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            reArmRow1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            btnReArmDisarm = ModeBtn("Disarm");
            btnReArmReArm  = ModeBtn("ReArm");
            btnReArmDisarm.Click += (s, e) => { ReArmAfterEntry = ReArmMode.Disarm; RefreshReArmButtons(); };
            btnReArmReArm.Click  += (s, e) => { ReArmAfterEntry = ReArmMode.ReArm;  RefreshReArmButtons(); };
            Grid.SetColumn(btnReArmDisarm, 0);
            Grid.SetColumn(btnReArmReArm,  1);
            reArmRow1.Children.Add(btnReArmDisarm);
            reArmRow1.Children.Add(btnReArmReArm);

            // Row 2: ReArmMQB | ReArmMQS
            var reArmRow2 = new Grid();
            reArmRow2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            reArmRow2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            btnReArmMQB = ModeBtn("ReArm MQB");
            btnReArmMQS = ModeBtn("ReArm MQS");
            btnReArmMQB.Click += (s, e) => { ReArmAfterEntry = ReArmMode.ReArmMQB; RefreshReArmButtons(); };
            btnReArmMQS.Click += (s, e) => { ReArmAfterEntry = ReArmMode.ReArmMQS; RefreshReArmButtons(); };
            Grid.SetColumn(btnReArmMQB, 0);
            Grid.SetColumn(btnReArmMQS, 1);
            reArmRow2.Children.Add(btnReArmMQB);
            reArmRow2.Children.Add(btnReArmMQS);

            reArmSp.Children.Add(reArmRow1);
            reArmSp.Children.Add(reArmRow2);
            reArmCard.Child = reArmSp;
            hudStack.Children.Add(reArmCard);
            RefreshReArmButtons();
        }

        // ── Trade Management section ──────────────────────────────────────────
        private void BuildTradeManagementSection()
        {
            // FLATTEN EVERYTHING — full width, high danger style
            var flattenBtn = new Button
            {
                Content         = "⬛  FLATTEN EVERYTHING",
                FontSize        = 12,
                FontWeight      = FontWeights.Bold,
                Padding         = new Thickness(0, 6, 0, 6),
                Margin          = new Thickness(6, 4, 6, 3),
                Background      = new SolidColorBrush(Color.FromArgb(70, C_RED.R, C_RED.G, C_RED.B)),
                Foreground      = new SolidColorBrush(C_RED),
                BorderBrush     = new SolidColorBrush(C_RED),
                BorderThickness = new Thickness(2),
                Cursor          = Cursors.Hand,
            };
            flattenBtn.Click += (s, e) => FlattenEverythingAllAccounts();
            hudStack.Children.Add(flattenBtn);

            // ── Breakeven row ─────────────────────────────────────────────────
            var beCard = Card();
            var beSp   = new StackPanel();
            beSp.Children.Add(Tx("BREAKEVEN", 9, C_MUTED));
            beSp.Children.Add(new Border { BorderBrush = new SolidColorBrush(C_BORDER), BorderThickness = new Thickness(0,1,0,0), Margin = new Thickness(0,4,0,6) });
            var beRow = TwoColBtns(
                ActionBtn($"BE + {Breakeven1PlusTicks}",  C_AMBER,  () => StopsToBreakeven(Breakeven1PlusTicks)),
                ActionBtn($"BE + {Breakeven2PlusTicks}",  C_AMBER,  () => StopsToBreakeven(Breakeven2PlusTicks))
            );
            beSp.Children.Add(beRow);
            beCard.Child = beSp;
            hudStack.Children.Add(beCard);

            // ── Target row ────────────────────────────────────────────────────
            var tgtCard = Card();
            var tgtSp   = new StackPanel();
            tgtSp.Children.Add(Tx("TARGETS", 9, C_MUTED));
            tgtSp.Children.Add(new Border { BorderBrush = new SolidColorBrush(C_BORDER), BorderThickness = new Thickness(0,1,0,0), Margin = new Thickness(0,4,0,6) });
            tgtSp.Children.Add(TwoColBtns(
                ActionBtn($"Price + {PricePlusTicks}",  C_BLUE, () => TargetToPricePlus(PricePlusTicks)),
                ActionBtn($"Entry + {EntryPlusTicks}",  C_BLUE, () => TargetToEntryPlus(EntryPlusTicks))
            ));
            tgtCard.Child = tgtSp;
            hudStack.Children.Add(tgtCard);

            // ── Bracket / Stop row ────────────────────────────────────────────
            var brkCard = Card();
            var brkSp   = new StackPanel();
            brkSp.Children.Add(Tx("BRACKET / STOP", 9, C_MUTED));
            brkSp.Children.Add(new Border { BorderBrush = new SolidColorBrush(C_BORDER), BorderThickness = new Thickness(0,1,0,0), Margin = new Thickness(0,4,0,6) });
            brkSp.Children.Add(TwoColBtns(
                ActionBtn("Bracket",  C_PURPLE, () => BracketOrder(BracketStopTicks, BracketProfitTicks)),
                ActionBtn("Add Stop", C_PURPLE, () => AddStopOrder(BracketStopTicks))
            ));
            brkCard.Child = brkSp;
            hudStack.Children.Add(brkCard);

            // ── Size management row ───────────────────────────────────────────
            var sizeCard = Card();
            var sizeSp   = new StackPanel();
            sizeSp.Children.Add(Tx("SIZE", 9, C_MUTED));
            sizeSp.Children.Add(new Border { BorderBrush = new SolidColorBrush(C_BORDER), BorderThickness = new Thickness(0,1,0,0), Margin = new Thickness(0,4,0,6) });
            sizeSp.Children.Add(TwoColBtns(
                ActionBtn("Half",   Color.FromRgb(155, 75,  0), () => RemoveHalfPosition()),
                ActionBtn("Double", Color.FromRgb(0,  95, 155), () => DoublePosition())
            ));
            sizeSp.Children.Add(new Border { Height = 2 });
            sizeSp.Children.Add(TwoColBtns(
                ActionBtn("Naked", Color.FromRgb(60, 60, 60), () => RemoveStopsAndTargets()),
                ActionBtn("Split", Color.FromRgb(60, 60, 60), () => SplitStopsAndTargets())
            ));
            sizeCard.Child = sizeSp;
            hudStack.Children.Add(sizeCard);

            // Bottom padding
            hudStack.Children.Add(new Border { Height = 4 });
        }

        // ═════════════════════════════════════════════════════════════════════
        // ARM button visual refresh
        // ═════════════════════════════════════════════════════════════════════
        private void RefreshArmButtons()
        {
            // MQB
            if (btnArmMQB != null)
            {
                btnArmMQB.Background      = mqbArmed
                    ? new SolidColorBrush(Color.FromArgb(80, C_GREEN.R, C_GREEN.G, C_GREEN.B))
                    : new SolidColorBrush(Color.FromArgb(40, C_DIM.R, C_DIM.G, C_DIM.B));
                btnArmMQB.Foreground      = new SolidColorBrush(mqbArmed ? C_GREEN : C_MUTED);
                btnArmMQB.BorderBrush     = new SolidColorBrush(mqbArmed ? C_GREEN : C_BORDER);
                btnArmMQB.BorderThickness = new Thickness(mqbArmed ? 2 : 1);
                btnArmMQB.Content         = mqbArmed ? "■ ARMED MQB ▲" : "▲  ARM MQB";
            }
            // MQS
            if (btnArmMQS != null)
            {
                btnArmMQS.Background      = mqsArmed
                    ? new SolidColorBrush(Color.FromArgb(80, C_RED.R, C_RED.G, C_RED.B))
                    : new SolidColorBrush(Color.FromArgb(40, C_DIM.R, C_DIM.G, C_DIM.B));
                btnArmMQS.Foreground      = new SolidColorBrush(mqsArmed ? C_RED : C_MUTED);
                btnArmMQS.BorderBrush     = new SolidColorBrush(mqsArmed ? C_RED : C_BORDER);
                btnArmMQS.BorderThickness = new Thickness(mqsArmed ? 2 : 1);
                btnArmMQS.Content         = mqsArmed ? "■ ARMED MQS ▼" : "▼  ARM MQS";
            }
        }

        // ── Opposite Signal button refresh ───────────────────────────────────
        private void RefreshOppButtons()
        {
            SetModeBtn(btnOppOff,     OppSignalMode == OppositeSignalMode.Off,     C_MUTED);
            SetModeBtn(btnOppClose,   OppSignalMode == OppositeSignalMode.Close,   C_AMBER);
            SetModeBtn(btnOppReverse, OppSignalMode == OppositeSignalMode.Reverse, C_BLUE);
        }

        // ── After Entry button refresh ────────────────────────────────────────
        private void RefreshReArmButtons()
        {
            SetModeBtn(btnReArmDisarm, ReArmAfterEntry == ReArmMode.Disarm,   C_MUTED);
            SetModeBtn(btnReArmReArm,  ReArmAfterEntry == ReArmMode.ReArm,    C_PURPLE);
            SetModeBtn(btnReArmMQB,    ReArmAfterEntry == ReArmMode.ReArmMQB, C_GREEN);
            SetModeBtn(btnReArmMQS,    ReArmAfterEntry == ReArmMode.ReArmMQS, C_RED);
        }

        // ═════════════════════════════════════════════════════════════════════
        // WPF factory helpers  (DailyRangeBot design system)
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

        private void BuildDivider(string label)
        {
            var g = new Grid { Margin = new Thickness(6, 5, 6, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            var lbl  = Tx(label, 9, C_DIM);
            lbl.Margin = new Thickness(0, 0, 6, 0);
            var line = new Border
            {
                BorderBrush       = new SolidColorBrush(C_BORDER),
                BorderThickness   = new Thickness(0, 1, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(lbl,  0);
            Grid.SetColumn(line, 1);
            g.Children.Add(lbl);
            g.Children.Add(line);
            hudStack.Children.Add(g);
        }

        // Colored border mode toggle button (inactive state by default)
        private Button ModeBtn(string label)
            => new Button
            {
                Content         = label,
                Height          = 24,
                FontSize        = 10,
                FontWeight      = FontWeights.Bold,
                FontFamily      = new FontFamily("Consolas, Courier New"),
                Margin          = new Thickness(2),
                Background      = new SolidColorBrush(Color.FromRgb(C_DIM.R, C_DIM.G, C_DIM.B)),
                Foreground      = new SolidColorBrush(C_MUTED),
                BorderBrush     = new SolidColorBrush(C_BORDER),
                BorderThickness = new Thickness(1),
                Cursor          = Cursors.Hand,
            };

        // Apply active/inactive visual state to a mode toggle button
        private void SetModeBtn(Button btn, bool active, Color activeColor)
        {
            if (btn == null) return;
            btn.Background      = active
                ? new SolidColorBrush(Color.FromArgb(60, activeColor.R, activeColor.G, activeColor.B))
                : new SolidColorBrush(C_DIM);
            btn.Foreground      = new SolidColorBrush(active ? activeColor : C_MUTED);
            btn.BorderBrush     = new SolidColorBrush(active ? activeColor : C_BORDER);
            btn.BorderThickness = new Thickness(active ? 2 : 1);
        }

        // Action button with colored border (trade management buttons)
        private Button ActionBtn(string label, Color accentColor, Action onClick)
        {
            var b = new Button
            {
                Content         = label,
                Height          = 26,
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

        // Two-column button row
        private Grid TwoColBtns(Button left, Button right)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(left,  0);
            Grid.SetColumn(right, 1);
            g.Children.Add(left);
            g.Children.Add(right);
            return g;
        }

        // ═════════════════════════════════════════════════════════════════════
        // Signal logic helpers
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
                    Print("Arm " + label + " ▶ opposite position exists, mode=Off — ignoring");
                    return true;
                case OppositeSignalMode.Close:
                    CloseCurrentPosition(acct, instr, pos, label);
                    Print("Arm " + label + " ▶ opposite position closed (mode=Close)");
                    return true;
                case OppositeSignalMode.Reverse:
                    CloseCurrentPosition(acct, instr, pos, label);
                    Print("Arm " + label + " ▶ opposite position closed, reversing (mode=Reverse)");
                    return false;
                default:
                    return false;
            }
        }

        private void CloseCurrentPosition(NinjaTrader.Cbi.Account acct,
                                          NinjaTrader.Cbi.Instrument instr,
                                          Position pos, string label)
        {
            try
            {
                var exits = acct.Orders
                    .Where(o => o.Instrument.FullName == instr.FullName &&
                               (o.OrderState == OrderState.Working || o.OrderState == OrderState.Accepted))
                    .ToArray();
                if (exits.Length > 0) acct.Cancel(exits);

                OrderAction closeAction = pos.MarketPosition == MarketPosition.Long
                    ? OrderAction.Sell : OrderAction.BuyToCover;
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
                var acct  = ResolveAccount();
                if (!ValidateAccountForTrading(acct, "Arm " + signalLabel, out string acctReason))
                {
                    Print("Arm " + signalLabel + " ▶ " + acctReason);
                    return;
                }
                var instr = ResolveInstrument(); if (instr == null) { Print("Arm " + signalLabel + " ▶ no instrument"); return; }

                int qty = 0;
                string qtySource = "";
                ChartControl.Dispatcher.Invoke(() => { qty = ReadChartTraderQuantity(out qtySource); });
                if (qty <= 0) { Print("Arm " + signalLabel + " ▶ quantity 0, skipping"); return; }

                var pos = acct.Positions.FirstOrDefault(p => p.Instrument.FullName == instr.FullName);
                if (pos != null && pos.Quantity > 0)
                {
                    bool sameSide = (pos.MarketPosition == MarketPosition.Long  && action == OrderAction.Buy) ||
                                    (pos.MarketPosition == MarketPosition.Short && action == OrderAction.SellShort);
                    if (sameSide) { Print("Arm " + signalLabel + " ▶ already same direction, skipping"); return; }
                }

                SubmitPanelOrders(acct, new[] { acct.CreateOrder(instr, action, OrderType.Market,
                    PanelOrderEntry, TimeInForce.Gtc, qty, 0, 0, "",
                    Name + "_Arm_" + signalLabel, Core.Globals.MaxDate, null) }, "Arm " + signalLabel);
                Print("Arm " + signalLabel + " ▶ submitted market " + action + " x" + qty + " (" + qtySource + ") on " + acct.Name);
            }
            catch (Exception ex) { Print("Arm " + signalLabel + " ▶ ERROR: " + ex.Message); }
        }

        private IDrawingTool FindChartDrawObject(string tag)
        {
            try { foreach (var ind in ChartControl.Indicators) { var o = ind.DrawObjects[tag]; if (o != null) return o; } }
            catch { }
            return null;
        }

        private Grid GetChartTraderGrid()
        {
            var window = GetChartTraderWindow();
            if (window == null) return null;
            var ct = window.FindFirst("ChartWindowChartTraderControl") as ChartTrader;
            return ct?.Content as Grid;
        }

        private static bool TryParseQuantityText(string text, out int qty)
        {
            qty = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string cleaned = text.Trim().Replace(",", "");
            if (int.TryParse(cleaned, NumberStyles.Integer, CultureInfo.InvariantCulture, out qty) && qty > 0)
                return true;
            if (double.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out double d) && d > 0)
            {
                qty = (int)Math.Round(d);
                return qty > 0;
            }
            return false;
        }

        private static int QuantityFromUpDown(QuantityUpDown selector)
        {
            if (selector == null) return 0;
            try
            {
                int v = selector.Value;
                return v > 0 ? v : 0;
            }
            catch
            {
                return 0;
            }
        }

        // V1.1 (Spoobie): Attach ValueChanged listener to QuantityUpDown at panel creation
        // so lastKnownChartTraderQty stays current without polling on every tick.
        private void WireChartTraderQuantitySelector()
        {
            try
            {
                DetachChartTraderQuantitySelector();

                QuantityUpDown found = null;
                if (_ctTraderGrid != null)
                    found = _ctTraderGrid.FindFirst("ChartTraderControlQuantitySelector") as QuantityUpDown;

                if (found == null)
                {
                    var window = GetChartTraderWindow();
                    found = window?.FindFirst("ChartTraderControlQuantitySelector") as QuantityUpDown;
                }

                if (found == null)
                    return;

                cachedQtySelector = found;
                lastKnownChartTraderQty = Math.Max(1, QuantityFromUpDown(found));

                cachedQtyValueChangedHandler = OnChartTraderQuantityChanged;
                cachedQtySelector.ValueChanged += cachedQtyValueChangedHandler;
            }
            catch (Exception ex) { Print("WireQty ▶ " + ex.Message); }
        }

        private void OnChartTraderQuantityChanged(object sender, RoutedEventArgs e)
        {
            if (cachedQtySelector != null)
                lastKnownChartTraderQty = Math.Max(1, QuantityFromUpDown(cachedQtySelector));
        }

        private void DetachChartTraderQuantitySelector()
        {
            try
            {
                if (cachedQtySelector != null && cachedQtyValueChangedHandler != null)
                    cachedQtySelector.ValueChanged -= cachedQtyValueChangedHandler;
            }
            catch { }
            cachedQtySelector = null;
            cachedQtyValueChangedHandler = null;
        }

        private QuantityUpDown FindChartTraderQuantitySelector()
        {
            if (cachedQtySelector != null)
                return cachedQtySelector;

            Grid grid = _ctTraderGrid ?? GetChartTraderGrid();
            if (grid != null)
            {
                var fromGrid = grid.FindFirst("ChartTraderControlQuantitySelector") as QuantityUpDown;
                if (fromGrid != null)
                    return fromGrid;
            }

            return GetChartTraderWindow()?.FindFirst("ChartTraderControlQuantitySelector") as QuantityUpDown;
        }

        // V1.1 (Spoobie): 4-tier quantity fallback — QuantitySelector → QuantityEdit
        // → lastKnown → ChartTrader.Quantity. Logs which source was used.
        private int ReadChartTraderQuantity(out string source)
        {
            source = "fallback";
            try
            {
                if (cachedQtySelector == null)
                    WireChartTraderQuantitySelector();

                // 1) Cached/live QuantityUpDown (what the user clicks in Chart Trader).
                var qtyUpDown = FindChartTraderQuantitySelector();
                int fromSelector = QuantityFromUpDown(qtyUpDown);
                if (fromSelector > 0)
                {
                    lastKnownChartTraderQty = fromSelector;
                    source = "QuantitySelector";
                    return fromSelector;
                }

                // 2) Quantity text edit on Chart Trader grid.
                Grid grid = _ctTraderGrid ?? GetChartTraderGrid();
                if (grid != null)
                {
                    var qtyEdit = grid.FindFirst("ChartTraderControlQuantityEdit") as TextBox;
                    if (qtyEdit != null && TryParseQuantityText(qtyEdit.Text, out int qEdit))
                    {
                        lastKnownChartTraderQty = qEdit;
                        source = "QuantityEdit";
                        return qEdit;
                    }

                    var qtyLegacy = grid.FindFirst("ChartTraderControlQuantityTextBox") as TextBox;
                    if (qtyLegacy != null && TryParseQuantityText(qtyLegacy.Text, out int qLegacy))
                    {
                        lastKnownChartTraderQty = qLegacy;
                        source = "QuantityTextBox";
                        return qLegacy;
                    }
                }

                // 3) Last value from ValueChanged while panel was on chart.
                if (lastKnownChartTraderQty > 1)
                {
                    source = "lastKnown";
                    return lastKnownChartTraderQty;
                }

                // 4) ChartTrader.Quantity — often default 1; do not prefer over UI.
                var ownerChart = ChartControl?.OwnerChart;
                if (ownerChart?.ChartTrader != null)
                {
                    int ctQty = ownerChart.ChartTrader.Quantity;
                    if (ctQty > 0)
                    {
                        source = "ChartTrader.Quantity";
                        return ctQty;
                    }
                }
            }
            catch (Exception ex) { Print("ReadQty ▶ " + ex.Message); }

            source = "default";
            Print("ReadQty ▶ could not read Chart Trader quantity — using 1. Set quantity in Chart Trader before arming.");
            return 1;
        }

        // V1.1 (Spoobie): OrderEntry.Manual matches ChartTrader button behavior.
        // OrderEntry.Automated is frequently rejected on live brokerage connections.
        private const OrderEntry PanelOrderEntry = OrderEntry.Manual;

        // ═════════════════════════════════════════════════════════════════════
        // Account / Instrument resolution
        // ═════════════════════════════════════════════════════════════════════
        private Window GetChartTraderWindow()
        {
            if (ChartControl?.Parent == null) return null;
            return Window.GetWindow(ChartControl.Parent);
        }

        // V1.1 (Spoobie): Returns SelectedAccount directly; avoids fragile display-name
        // string matching against Account.All which breaks on some live account labels.
        private NinjaTrader.Cbi.Account ResolveAccount()
        {
            NinjaTrader.Cbi.Account acct = null;
            if (ChartControl == null) return null;

            ChartControl.Dispatcher.Invoke(() =>
            {
                var window = GetChartTraderWindow();
                if (window == null) return;
                xAcSelector = window.FindFirst("ChartTraderControlAccountSelector") as NinjaTrader.Gui.Tools.AccountSelector;
                acct = xAcSelector?.SelectedAccount;
            });

            if (acct != null)
                return acct;

            // Fallback only if selector reference is missing (display-name match — fragile on live account labels).
            string name = null;
            ChartControl.Dispatcher.Invoke(() => { name = xAcSelector?.SelectedAccount?.Name; });
            if (string.IsNullOrWhiteSpace(name))
                return null;
            return NinjaTrader.Cbi.Account.All.FirstOrDefault(a =>
                string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        // V1.1 (Spoobie): Pre-flight account guard — null check + connection status check.
        private bool ValidateAccountForTrading(NinjaTrader.Cbi.Account acct, string context, out string reason)
        {
            reason = null;
            if (acct == null)
            {
                reason = "Chart Trader account not selected";
                return false;
            }
            if (acct.Connection == null || acct.Connection.Status != ConnectionStatus.Connected)
            {
                reason = "Account '" + acct.Name + "' is not connected (status="
                    + (acct.Connection?.Status.ToString() ?? "none") + ")";
                return false;
            }
            return true;
        }

        // V1.1 (Spoobie): Centralized submission — validates connection before every Submit().
        private void SubmitPanelOrders(NinjaTrader.Cbi.Account acct, NinjaTrader.Cbi.Order[] orders, string context)
        {
            if (orders == null || orders.Length == 0) return;
            if (!ValidateAccountForTrading(acct, context, out string reason))
            {
                Print(context + " ▶ " + reason);
                return;
            }
            try
            {
                acct.Submit(orders);
            }
            catch (Exception ex)
            {
                Print(context + " ▶ submit failed on '" + acct.Name + "': " + ex.Message);
            }
        }

        // V1.1 (Spoobie): Falls back to indicator's own Instrument if selector lookup
        // returns null (e.g. during chart reload or ChartTrader not yet visible).
        private NinjaTrader.Cbi.Instrument ResolveInstrument()
        {
            NinjaTrader.Cbi.Instrument instr = null;
            if (ChartControl == null) return Instrument;

            ChartControl.Dispatcher.Invoke(() =>
            {
                var window = GetChartTraderWindow();
                if (window == null) return;
                xInSelector = window.FindFirst("ChartWindowInstrumentSelector") as NinjaTrader.Gui.Tools.InstrumentSelector;
                instr = xInSelector?.Instrument;
            });
            return instr ?? Instrument;
        }

        // ═════════════════════════════════════════════════════════════════════
        // Trading methods
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
                    int totalCancelled = 0;
                    foreach (var acct in allAccts)
                    {
                        try
                        {
                            var cancels = acct.Orders.Where(o => o != null &&
                                (o.OrderState == OrderState.Working || o.OrderState == OrderState.Accepted || o.OrderState == OrderState.PartFilled)).ToArray();
                            if (cancels.Length > 0) { acct.Cancel(cancels); totalCancelled += cancels.Length; }
                        }
                        catch (Exception ex) { Print("Flatten ▶ cancel error: " + ex.Message); }
                    }
                    System.Threading.Thread.Sleep(FlattenAllPause);
                    int passes = 0, totalSub = 0;
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
                                        Name + "_Flatten_" + p.Instrument.FullName, Core.Globals.MaxDate, null))
                                    .ToArray();
                                if (outs.Length > 0)
                                {
                                    ChartControl?.Dispatcher.Invoke(() => SubmitPanelOrders(acct, outs, "Flatten"));
                                    totalSub += outs.Length;
                                }
                            }
                            catch (Exception ex) { Print("Flatten ▶ market error: " + ex.Message); }
                        }
                        totalSub += 0;
                        if (rem == 0) break;
                        System.Threading.Thread.Sleep(250);
                    }
                    Print("Flatten ▶ done — cancelled " + totalCancelled + ", submitted " + totalSub + " market order(s)");
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
            {
                double newStop = pos.AveragePrice + (pos.MarketPosition == MarketPosition.Long ? +ticks : -ticks) * instr.MasterInstrument.TickSize;
                o.StopPriceChanged = newStop;
                acct.Change(new[] { o });
            }
        }

        private void TargetToPricePlus(int ticks)
        {
            var acct = ResolveAccount(); if (acct == null) return;
            var instr = ResolveInstrument(); if (instr == null) return;
            var pos = acct.Positions.FirstOrDefault(p => p.Instrument.FullName == instr.FullName);
            if (pos == null || pos.Quantity == 0) { Print("PricePlus ▶ no position"); return; }
            var targets = acct.Orders.Where(o => o.Instrument.FullName == instr.FullName &&
                o.OrderType == OrderType.Limit &&
                (o.OrderState == OrderState.Working || o.OrderState == OrderState.Accepted)).ToList();
            if (targets.Count == 0) { Print("PricePlus ▶ no targets"); return; }
            bool isLong = pos.MarketPosition == MarketPosition.Long;
            double basePrice = instr.MasterInstrument.RoundToTickSize(lastClose);
            double newPrice  = isLong ? basePrice + ticks * instr.MasterInstrument.TickSize
                                      : basePrice - ticks * instr.MasterInstrument.TickSize;
            foreach (var o in targets) o.LimitPriceChanged = newPrice;
            acct.Change(targets.ToArray());
        }

        private void TargetToEntryPlus(int ticks)
        {
            var acct = ResolveAccount(); if (acct == null) return;
            var instr = ResolveInstrument(); if (instr == null) return;
            var pos = acct.Positions.FirstOrDefault(p => p.Instrument.FullName == instr.FullName);
            if (pos == null || pos.Quantity == 0) { Print("EntryPlus ▶ no position"); return; }
            var targets = acct.Orders.Where(o => o.Instrument.FullName == instr.FullName &&
                o.OrderType == OrderType.Limit &&
                (o.OrderState == OrderState.Working || o.OrderState == OrderState.Accepted)).ToList();
            if (targets.Count == 0) { Print("EntryPlus ▶ no targets"); return; }
            bool isLong = pos.MarketPosition == MarketPosition.Long;
            double newPrice = isLong ? pos.AveragePrice + ticks * instr.MasterInstrument.TickSize
                                     : pos.AveragePrice - ticks * instr.MasterInstrument.TickSize;
            foreach (var o in targets) o.LimitPriceChanged = newPrice;
            acct.Change(targets.ToArray());
        }

        private void BracketOrder(int stopTicks, int profitTicks)
        {
            var acct = ResolveAccount(); if (acct == null) return;
            var instr = ResolveInstrument(); if (instr == null) return;
            var pos = acct.Positions.FirstOrDefault(p => p.Instrument.FullName == instr.FullName);
            if (pos == null || pos.Quantity == 0) { Print("Bracket ▶ no position"); return; }
            bool hasBracket = acct.Orders.Any(o => o.Instrument.FullName == instr.FullName &&
                !string.IsNullOrEmpty(o.Oco) && (o.OrderState == OrderState.Accepted || o.OrderState == OrderState.Working));
            if (hasBracket) { Print("Bracket ▶ already exists"); return; }
            bool   isLong   = pos.MarketPosition == MarketPosition.Long;
            double basePrice = instr.MasterInstrument.RoundToTickSize(lastClose);
            double tickSz    = instr.MasterInstrument.TickSize;
            double stopPx    = isLong ? basePrice - stopTicks   * tickSz : basePrice + stopTicks   * tickSz;
            double targetPx  = isLong ? basePrice + profitTicks * tickSz : basePrice - profitTicks * tickSz;
            string ocoId     = Guid.NewGuid().ToString();
            SubmitPanelOrders(acct, new[]
            {
                acct.CreateOrder(instr, isLong ? OrderAction.Sell : OrderAction.BuyToCover,
                    OrderType.StopMarket, PanelOrderEntry, TimeInForce.Gtc,
                    pos.Quantity, 0, stopPx, ocoId, Name + "_SL", Core.Globals.MaxDate, null),
                acct.CreateOrder(instr, isLong ? OrderAction.Sell : OrderAction.BuyToCover,
                    OrderType.Limit, PanelOrderEntry, TimeInForce.Gtc,
                    pos.Quantity, targetPx, 0, ocoId, Name + "_TP", Core.Globals.MaxDate, null),
            }, "Bracket");
            Print("Bracket ▶ SL=" + stopPx + "  TP=" + targetPx);
        }

        private void AddStopOrder(int stopTicks)
        {
            var acct = ResolveAccount(); if (acct == null) return;
            var instr = ResolveInstrument(); if (instr == null) return;
            var pos = acct.Positions.FirstOrDefault(p => p.Instrument.FullName == instr.FullName);
            if (pos == null || pos.Quantity == 0) { Print("AddStop ▶ no position"); return; }
            bool hasStop = acct.Orders.Any(o => o.Instrument.FullName == instr.FullName &&
                o.OrderType == OrderType.StopMarket &&
                (o.OrderState == OrderState.Accepted || o.OrderState == OrderState.Working));
            if (hasStop) { Print("AddStop ▶ stop already exists"); return; }
            bool isLong = pos.MarketPosition == MarketPosition.Long;
            double basePrice = instr.MasterInstrument.RoundToTickSize(lastClose);
            double stopPx    = isLong ? basePrice - stopTicks * instr.MasterInstrument.TickSize
                                      : basePrice + stopTicks * instr.MasterInstrument.TickSize;
            SubmitPanelOrders(acct, new[] { acct.CreateOrder(instr,
                isLong ? OrderAction.Sell : OrderAction.BuyToCover,
                OrderType.StopMarket, PanelOrderEntry, TimeInForce.Gtc,
                pos.Quantity, 0, stopPx, "", Name + "_AddStop", Core.Globals.MaxDate, null) }, "AddStop");
            Print("AddStop ▶ Stop=" + stopPx);
        }

        private void RemoveHalfPosition()
        {
            var acct = ResolveAccount(); if (acct == null) return;
            var instr = ResolveInstrument(); if (instr == null) return;
            var pos = acct.Positions.FirstOrDefault(p => p.Instrument.FullName == instr.FullName);
            if (pos == null || pos.Quantity <= 1) { Print("Half ▶ nothing to remove"); return; }
            int total = pos.Quantity, toRemove = total / 2;
            var exitOrders = acct.Orders.Where(o => o.Instrument.FullName == instr.FullName &&
                (o.OrderType == OrderType.Limit || o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit) &&
                (o.OrderState == OrderState.Working || o.OrderState == OrderState.Accepted)).ToList();
            var bracketGroups = exitOrders.Where(o => !string.IsNullOrEmpty(o.Oco))
                .GroupBy(o => o.Oco).ToList();
            if (bracketGroups.Any())
            {
                int bracketTotal = bracketGroups.Sum(g => g.Min(o => o.Quantity));
                int bracketRemove = bracketTotal / 2;
                if (bracketRemove == 0) { Print("Half ▶ only 1 contract in bracket"); return; }
                var mktAction = pos.MarketPosition == MarketPosition.Long ? OrderAction.Sell : OrderAction.BuyToCover;
                SubmitPanelOrders(acct, new[] { acct.CreateOrder(instr, mktAction, OrderType.Market,
                    PanelOrderEntry, TimeInForce.Gtc, bracketRemove, 0, 0, "",
                    Name + "_Half", Core.Globals.MaxDate, null) }, "Half");
                int rem = bracketRemove;
                foreach (var grp in bracketGroups)
                {
                    if (rem == 0) break;
                    var legs = grp.ToList();
                    int grpQty = grp.Min(o => o.Quantity);
                    int removeHere = Math.Min(grpQty, rem), keepHere = grpQty - removeHere;
                    if (grp.Any(o => o.Quantity > 1))
                    {
                        if (keepHere > 0) { foreach (var o in legs) o.QuantityChanged = keepHere; acct.Change(legs.ToArray()); }
                        else acct.Cancel(legs.ToArray());
                    }
                    else
                    {
                        var toCancel = new List<NinjaTrader.Cbi.Order>();
                        var stops   = legs.Where(o => o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit);
                        var targets = legs.Where(o => o.OrderType == OrderType.Limit);
                        if (pos.MarketPosition == MarketPosition.Long)
                        {
                            toCancel.AddRange(stops.OrderByDescending(o => o.StopPrice).Take(removeHere));
                            toCancel.AddRange(targets.OrderBy(o => o.LimitPrice).Take(removeHere));
                        }
                        else
                        {
                            toCancel.AddRange(stops.OrderBy(o => o.StopPrice).Take(removeHere));
                            toCancel.AddRange(targets.OrderByDescending(o => o.LimitPrice).Take(removeHere));
                        }
                        if (toCancel.Any()) acct.Cancel(toCancel.ToArray());
                    }
                    rem -= removeHere;
                }
                return;
            }
            var mktAction2 = pos.MarketPosition == MarketPosition.Long ? OrderAction.Sell : OrderAction.BuyToCover;
            SubmitPanelOrders(acct, new[] { acct.CreateOrder(instr, mktAction2, OrderType.Market,
                PanelOrderEntry, TimeInForce.Gtc, toRemove, 0, 0, "",
                Name + "_Half", Core.Globals.MaxDate, null) }, "Half");
            var standalone = exitOrders.Where(o => string.IsNullOrEmpty(o.Oco)).ToList();
            if (standalone.Any()) { foreach (var o in standalone) o.QuantityChanged = total - toRemove; acct.Change(standalone.ToArray()); }
        }

        private void DoublePosition()
        {
            var acct = ResolveAccount(); if (acct == null) return;
            var instr = ResolveInstrument(); if (instr == null) return;
            var pos = acct.Positions.FirstOrDefault(p => p.Instrument.FullName == instr.FullName);
            if (pos == null || pos.Quantity <= 0) { Print("Double ▶ no position"); return; }
            bool isLong = pos.MarketPosition == MarketPosition.Long;
            SubmitPanelOrders(acct, new[] { acct.CreateOrder(instr,
                isLong ? OrderAction.Buy : OrderAction.SellShort,
                OrderType.Market, PanelOrderEntry, TimeInForce.Gtc,
                pos.Quantity, 0, 0, "", Name + "_Double", Core.Globals.MaxDate, null) }, "Double");
            foreach (var grp in acct.Orders
                .Where(o => o.Instrument.FullName == instr.FullName && !string.IsNullOrEmpty(o.Oco) &&
                    (o.OrderState == OrderState.Accepted || o.OrderState == OrderState.Working) &&
                    (o.OrderType == OrderType.Limit || o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit))
                .GroupBy(o => o.Oco))
            {
                int preQty = grp.Min(o => o.Quantity), newQty = preQty * 2;
                foreach (var o in grp) o.QuantityChanged = newQty;
                acct.Change(grp.ToArray());
            }
        }

        private void RemoveStopsAndTargets()
        {
            var acct = ResolveAccount(); if (acct == null) return;
            var instr = ResolveInstrument(); if (instr == null) return;
            var exits = acct.Orders.Where(o => o.Instrument.FullName == instr.FullName &&
                (o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.Limit) &&
                (o.OrderState == OrderState.Working || o.OrderState == OrderState.Accepted)).ToArray();
            if (exits.Length == 0) { Print("Naked ▶ no exits to remove"); return; }
            acct.Cancel(exits);
            Print("Naked ▶ removed " + exits.Length + " order(s)");
        }

        private void SplitStopsAndTargets()
        {
            var acct = ResolveAccount(); if (acct == null) return;
            var instr = ResolveInstrument(); if (instr == null) return;
            var bracketLegs = acct.Orders.Where(o => o.Instrument.FullName == instr.FullName &&
                !string.IsNullOrEmpty(o.Oco) &&
                (o.OrderState == OrderState.Working || o.OrderState == OrderState.Accepted) &&
                (o.OrderType == OrderType.Limit || o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit)).ToList();
            if (bracketLegs.Count == 0) { Print("Split ▶ no brackets to split"); return; }
            var groups = bracketLegs.GroupBy(o => o.Oco).ToList();
            bool allSingle = groups.All(g => g.All(o => o.Quantity == 1));
            if (allSingle && groups.Count > 1)
            {
                int totalQty   = groups.Count;
                var first      = groups[0].ToList();
                var stopLeg    = first.First(o => o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit);
                var tgtLeg     = first.First(o => o.OrderType == OrderType.Limit);
                bool isLong    = stopLeg.OrderAction == OrderAction.Sell;
                double tickSz  = instr.MasterInstrument.TickSize;
                double baseStop = stopLeg.StopPrice, baseTgt = tgtLeg.LimitPrice;
                acct.Cancel(bracketLegs.ToArray());
                var newOrders = new List<NinjaTrader.Cbi.Order>(totalQty * 2);
                for (int i = 0; i < totalQty; i++)
                {
                    string legOco = Guid.NewGuid().ToString();
                    newOrders.Add(acct.CreateOrder(instr, isLong ? OrderAction.Sell : OrderAction.BuyToCover,
                        OrderType.StopMarket, PanelOrderEntry, TimeInForce.Gtc, 1, 0, baseStop,
                        legOco, Name + "_Stop" + (i+1), Core.Globals.MaxDate, null));
                    newOrders.Add(acct.CreateOrder(instr, isLong ? OrderAction.Sell : OrderAction.BuyToCover,
                        OrderType.Limit, PanelOrderEntry, TimeInForce.Gtc, 1,
                        isLong ? baseTgt + i * tickSz : baseTgt - i * tickSz, 0,
                        legOco, Name + "_Split" + (i+1), Core.Globals.MaxDate, null));
                }
                SubmitPanelOrders(acct, newOrders.ToArray(), "Split");
                Print("Split ▶ created " + totalQty + " stop+target pairs");
                return;
            }
            foreach (var group in groups)
            {
                var legs = group.ToList();
                if (!legs.Any(o => o.Quantity > 1)) { Print("Split ▶ OCO=" + group.Key + " already per-contract"); continue; }
                var stopLeg  = legs.First(o => o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit);
                var limitLeg = legs.First(o => o.OrderType == OrderType.Limit);
                int qty = limitLeg.Quantity; bool isLong = stopLeg.OrderAction == OrderAction.Sell;
                double baseStop = stopLeg.StopPrice, baseTgt = limitLeg.LimitPrice;
                double tickSz   = instr.MasterInstrument.TickSize;
                acct.Cancel(legs.ToArray());
                var newOrders = new List<NinjaTrader.Cbi.Order>(qty * 2);
                for (int i = 0; i < qty; i++)
                {
                    string legOco = Guid.NewGuid().ToString();
                    newOrders.Add(acct.CreateOrder(instr, isLong ? OrderAction.Sell : OrderAction.BuyToCover,
                        OrderType.StopMarket, PanelOrderEntry, TimeInForce.Gtc, 1, 0, baseStop,
                        legOco, Name + "_Stop" + (i+1), Core.Globals.MaxDate, null));
                    newOrders.Add(acct.CreateOrder(instr, isLong ? OrderAction.Sell : OrderAction.BuyToCover,
                        OrderType.Limit, PanelOrderEntry, TimeInForce.Gtc, 1,
                        isLong ? baseTgt + i * tickSz : baseTgt - i * tickSz, 0,
                        legOco, Name + "_Split" + (i+1), Core.Globals.MaxDate, null));
                }
                SubmitPanelOrders(acct, newOrders.ToArray(), "Split");
                Print("Split ▶ created " + qty + " stop+target pairs for OCO=" + group.Key);
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
		private TrendArchitectMQPanelV1_1[] cacheTrendArchitectMQPanelV1_1;
		public TrendArchitectMQPanelV1_1 TrendArchitectMQPanelV1_1(int breakeven1PlusTicks, int breakeven2PlusTicks, int pricePlusTicks, int entryPlusTicks, int bracketStopTicks, int bracketProfitTicks, int flattenAllPause, int flattenAllTries, TrendArchitectMQPanelV1_1.OppositeSignalMode oppSignalMode, TrendArchitectMQPanelV1_1.ReArmMode reArmAfterEntry)
		{
			return TrendArchitectMQPanelV1_1(Input, breakeven1PlusTicks, breakeven2PlusTicks, pricePlusTicks, entryPlusTicks, bracketStopTicks, bracketProfitTicks, flattenAllPause, flattenAllTries, oppSignalMode, reArmAfterEntry);
		}

		public TrendArchitectMQPanelV1_1 TrendArchitectMQPanelV1_1(ISeries<double> input, int breakeven1PlusTicks, int breakeven2PlusTicks, int pricePlusTicks, int entryPlusTicks, int bracketStopTicks, int bracketProfitTicks, int flattenAllPause, int flattenAllTries, TrendArchitectMQPanelV1_1.OppositeSignalMode oppSignalMode, TrendArchitectMQPanelV1_1.ReArmMode reArmAfterEntry)
		{
			if (cacheTrendArchitectMQPanelV1_1 != null)
				for (int idx = 0; idx < cacheTrendArchitectMQPanelV1_1.Length; idx++)
					if (cacheTrendArchitectMQPanelV1_1[idx] != null && cacheTrendArchitectMQPanelV1_1[idx].Breakeven1PlusTicks == breakeven1PlusTicks && cacheTrendArchitectMQPanelV1_1[idx].Breakeven2PlusTicks == breakeven2PlusTicks && cacheTrendArchitectMQPanelV1_1[idx].PricePlusTicks == pricePlusTicks && cacheTrendArchitectMQPanelV1_1[idx].EntryPlusTicks == entryPlusTicks && cacheTrendArchitectMQPanelV1_1[idx].BracketStopTicks == bracketStopTicks && cacheTrendArchitectMQPanelV1_1[idx].BracketProfitTicks == bracketProfitTicks && cacheTrendArchitectMQPanelV1_1[idx].FlattenAllPause == flattenAllPause && cacheTrendArchitectMQPanelV1_1[idx].FlattenAllTries == flattenAllTries && cacheTrendArchitectMQPanelV1_1[idx].OppSignalMode == oppSignalMode && cacheTrendArchitectMQPanelV1_1[idx].ReArmAfterEntry == reArmAfterEntry && cacheTrendArchitectMQPanelV1_1[idx].EqualsInput(input))
						return cacheTrendArchitectMQPanelV1_1[idx];
			return CacheIndicator<TrendArchitectMQPanelV1_1>(new TrendArchitectMQPanelV1_1(){ Breakeven1PlusTicks = breakeven1PlusTicks, Breakeven2PlusTicks = breakeven2PlusTicks, PricePlusTicks = pricePlusTicks, EntryPlusTicks = entryPlusTicks, BracketStopTicks = bracketStopTicks, BracketProfitTicks = bracketProfitTicks, FlattenAllPause = flattenAllPause, FlattenAllTries = flattenAllTries, OppSignalMode = oppSignalMode, ReArmAfterEntry = reArmAfterEntry }, input, ref cacheTrendArchitectMQPanelV1_1);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.TrendArchitectMQPanelV1_1 TrendArchitectMQPanelV1_1(int breakeven1PlusTicks, int breakeven2PlusTicks, int pricePlusTicks, int entryPlusTicks, int bracketStopTicks, int bracketProfitTicks, int flattenAllPause, int flattenAllTries, TrendArchitectMQPanelV1_1.OppositeSignalMode oppSignalMode, TrendArchitectMQPanelV1_1.ReArmMode reArmAfterEntry)
		{
			return indicator.TrendArchitectMQPanelV1_1(Input, breakeven1PlusTicks, breakeven2PlusTicks, pricePlusTicks, entryPlusTicks, bracketStopTicks, bracketProfitTicks, flattenAllPause, flattenAllTries, oppSignalMode, reArmAfterEntry);
		}

		public Indicators.TrendArchitectMQPanelV1_1 TrendArchitectMQPanelV1_1(ISeries<double> input , int breakeven1PlusTicks, int breakeven2PlusTicks, int pricePlusTicks, int entryPlusTicks, int bracketStopTicks, int bracketProfitTicks, int flattenAllPause, int flattenAllTries, TrendArchitectMQPanelV1_1.OppositeSignalMode oppSignalMode, TrendArchitectMQPanelV1_1.ReArmMode reArmAfterEntry)
		{
			return indicator.TrendArchitectMQPanelV1_1(input, breakeven1PlusTicks, breakeven2PlusTicks, pricePlusTicks, entryPlusTicks, bracketStopTicks, bracketProfitTicks, flattenAllPause, flattenAllTries, oppSignalMode, reArmAfterEntry);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.TrendArchitectMQPanelV1_1 TrendArchitectMQPanelV1_1(int breakeven1PlusTicks, int breakeven2PlusTicks, int pricePlusTicks, int entryPlusTicks, int bracketStopTicks, int bracketProfitTicks, int flattenAllPause, int flattenAllTries, TrendArchitectMQPanelV1_1.OppositeSignalMode oppSignalMode, TrendArchitectMQPanelV1_1.ReArmMode reArmAfterEntry)
		{
			return indicator.TrendArchitectMQPanelV1_1(Input, breakeven1PlusTicks, breakeven2PlusTicks, pricePlusTicks, entryPlusTicks, bracketStopTicks, bracketProfitTicks, flattenAllPause, flattenAllTries, oppSignalMode, reArmAfterEntry);
		}

		public Indicators.TrendArchitectMQPanelV1_1 TrendArchitectMQPanelV1_1(ISeries<double> input , int breakeven1PlusTicks, int breakeven2PlusTicks, int pricePlusTicks, int entryPlusTicks, int bracketStopTicks, int bracketProfitTicks, int flattenAllPause, int flattenAllTries, TrendArchitectMQPanelV1_1.OppositeSignalMode oppSignalMode, TrendArchitectMQPanelV1_1.ReArmMode reArmAfterEntry)
		{
			return indicator.TrendArchitectMQPanelV1_1(input, breakeven1PlusTicks, breakeven2PlusTicks, pricePlusTicks, entryPlusTicks, bracketStopTicks, bracketProfitTicks, flattenAllPause, flattenAllTries, oppSignalMode, reArmAfterEntry);
		}
	}
}

#endregion
