// ─────────────────────────────────────────────────────────────────────────────
// This Source Code Form is subject to the terms of the Mozilla Public License,
// v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
// one at https://mozilla.org/MPL/2.0/.
//
// Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
// ─────────────────────────────────────────────────────────────────────────────
#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Xml.Serialization;
using System.Windows.Media;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors;
using NinjaTrader.NinjaScript.AddOns.Sentinel;
#endregion

// ═════════════════════════════════════════════════════════════════════════════
//  WoodiesCCIPro — Woodies CCI / Turbo-CCI trend-filter oscillator   (Sentinel-graded)
//  File: WoodiesCCIPro_v1_0_0.cs   |   Version: v1.0.0   |   namespace …Indicators.Sentinel
// ─────────────────────────────────────────────────────────────────────────────
//  The Sentinel-grade upgrade of WoodiesCCIProV002. ALL of the trend/signal engine is preserved
//  verbatim (raw + persisted trend state machine, turbo/slope/persistence/strict/neutral-suppression
//  confirmation, weakening/strengthening logic, ZLR + Hook signals, bar coloring, and the full 22-plot
//  Strategy-Builder series map — same indices). What changed is the *surface*:
//    • REHOMED to namespace  NinjaTrader.NinjaScript.Indicators.Sentinel  → clusters under the "Sentinel"
//      indicator-picker folder. Clean class name (design-system §7). NEW type identity vs V002 (namespace +
//      name changed) → re-add on charts; V002 stays a FROZEN fallback.
//    • SENTINEL PALETTE — default plot/line/bar brushes remapped to the Sentinel tokens (cyan = the primary
//      watched line; green/red = bull/bear DIRECTION; mute = neutral; amber-dim = weakening). Still fully
//      user-customizable via the Brush properties.
//    • SENTINEL GLASS CARD (SentinelSkin.Painter) docked in the oscillator panel via CardLayout — trend-state
//      pill + Main/Turbo CCI hero + strength track + slope/signal row. Never overlaps another Sentinel card.
//    • LABEL REMOVER (mandatory) — NT's chart name-label hidden by default (ShowIndicatorLabel to restore).
//    • SENTINEL PUBLISH — broadcasts SentinelCore.CciState (SetCciState) each bar so GTrader21/Eye/strategies
//      can consult "Woodies trend is bull and not weakening" (SentinelCore v1.5.0 seam).
//
//  Edge lane: NO orders — a trend-filter/observer only. The Values[] signal plots feed Strategy Builder.
//
//  CHANGELOG
//    v1.0.0b (in-place 2026-07-07) — SENTINEL PLOT SKIN: OnRender paints a glass PanelWash (covers stock plots)
//             + a bottom TREND RIBBON (per-bar state) + themed 0/±100 reference lines + glowing Main (cyan) /
//             Turbo (mute) CCI lines. Toggle SentinelPlotSkin (default ON); stock gridlines off. §4c. No logic change.
//    v1.0.0a (2026-07-06) — default CardCorner TopLeft → TopRight (card docks on the right by default;
//             in-place patch, NOT a rename — a rename would drop it off saved charts. Existing placements keep
//             their serialized corner; flip the "Card corner" property or re-add to move an existing one.)
//    v1.0.0 — Sentinel-grade fork of WoodiesCCIProV002 (frozen). Rehomed to Indicators.Sentinel, clean name.
//             Sentinel palette defaults, glass card (CardLayout), label remover, and SentinelCore CciState
//             publish seam. Trend/signal LOGIC + plot indices unchanged (drop-in for the V002 Strategy Builder
//             series). ⚠ New serialization identity — existing V002 placements keep using V002.
// ═════════════════════════════════════════════════════════════════════════════

namespace NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors
{
    public class WoodiesCCIPro_v1_0_0 : Indicator
    {
        private CCI mainCCI;
        private CCI turboCCI;

        private int finalTrendState;
        private int candidateTrendState;
        private int candidateTrendCount;

        private Brush strongBullBrushFrozen;
        private Brush bullBrushFrozen;
        private Brush neutralBrushFrozen;
        private Brush bearBrushFrozen;
        private Brush strongBearBrushFrozen;
        private Brush bullWeakeningBrushFrozen;
        private Brush bearWeakeningBrushFrozen;

        private Brush mainCciBrushFrozen;
        private Brush turboCciBrushFrozen;
        private Brush trendStateBrushFrozen;
        private Brush signalBrushFrozen;

        // Sentinel glass-card readout (SharpDX via SentinelSkin.Painter, drawn in OnRender)
        private SentinelSkin.Painter _sp;

        // latest computed values (for the card + publish)
        private int    lastTrendState;
        private double lastMainCci, lastTurboCci, lastMainSlope;
        private int    lastSignal;      // +1 long / -1 short / 0 (ZLR/Hook this bar)
        private string lastSignalTag = "";
        private bool   lastWeakening;

        // Sentinel token brushes (frozen SolidColorBrush from exact palette hex)
        private static Brush SB(byte r, byte g, byte b)
        { var br = new SolidColorBrush(Color.FromRgb(r, g, b)); br.Freeze(); return br; }

        #region OnStateChange
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                                 = "Woodies CCI Pro (Sentinel-graded) — trend-filter CCI/Turbo-CCI with persistence, slope, weakening logic, ZLR/Hook signals, bar coloring, Strategy-Builder plots + SentinelCore publish.";
                Name                                        = "Sentinel Woodies CCI Pro v1.0.0";
                Calculate                                   = Calculate.OnBarClose;
                IsOverlay                                   = false;
                DisplayInDataBox                            = true;
                DrawOnPricePanel                            = false;
                DrawHorizontalGridLines                     = false;   // plot skin paints its own wash + reference lines
                DrawVerticalGridLines                       = false;
                PaintPriceMarkers                           = true;
                ScaleJustification                          = ScaleJustification.Right;
                IsSuspendedWhileInactive                    = true;

                // Core CCI defaults
                MainCCIPeriod                               = 144;
                TurboCCIPeriod                              = 34;

                // Core behavior
                EnableBarColoring                           = true;
                EnableTrendState                            = true;
                EnableTurboConfirmation                     = true;
                EnableSlopeConfirmation                     = true;
                EnablePersistenceConfirmation               = true;
                EnableStrictTrendMode                       = false;
                EnableNeutralSuppression                    = false;
                EnableWeakeningColoring                     = true;
                EnableZLRSignals                            = true;
                EnableHookSignals                           = true;

                // Thresholds
                StrongTrendLevel                            = 100.0;
                TurboStrongTrendLevel                       = 50.0;
                NeutralZoneLevel                            = 25.0;
                NeutralSuppressionLevel                     = 35.0;
                MinCCISlope                                 = 0.0;
                SlopeLookback                               = 1;
                PersistenceBars                             = 2;

                ExtremeHookLevel                            = 200.0;
                NearZeroLevel                               = 35.0;
                ZLRTrendThreshold                           = 50.0;

                // Visual controls
                ShowMainCCI                                 = true;
                ShowTurboCCI                                = true;
                ShowTrendStatePlot                          = true;
                ShowSignalPlots                             = true;

                // Sentinel surface
                ShowInfo                                    = true;
                SentinelPlotSkin                            = true;   // render the panel to the Sentinel plot standard
                PublishState                                = true;
                CardCorner                                  = SentinelCardCorner.TopRight;   // v1.0.0a: default to the right side
                ShowIndicatorLabel                          = false;   // Sentinel standard: clean chart

                // Bar colors — Sentinel tokens: green/red = bull/bear direction, mute = neutral, dim = weakening
                StrongBullBrush                             = SB(37, 208, 139);    // up (bright)
                BullBrush                                   = SB(30, 150, 105);    // up (dimmer)
                NeutralBrush                                = SB(108, 122, 146);   // mute
                BearBrush                                   = SB(190, 74, 84);     // down (dimmer)
                StrongBearBrush                             = SB(255, 92, 106);    // down (bright)
                BullWeakeningBrush                          = SB(24, 104, 76);     // up (very dim — fading)
                BearWeakeningBrush                          = SB(150, 58, 66);     // down (very dim — fading)

                // Plot colors — cyan = the primary watched line; ink2-grey = the turbo line; polarity elsewhere
                MainCCIPlotBrush                            = SB(63, 209, 224);    // accent (cyan)
                TurboCCIPlotBrush                           = SB(174, 186, 206);   // ink2
                TrendStatePlotBrush                         = SB(233, 238, 247);   // ink
                SignalPlotBrush                             = SB(63, 209, 224);    // accent

                // Level lines in Sentinel neutrals (the skin also tints gridlines)
                AddLine(SB(108, 122, 146),  0,    "Zero");
                AddLine(SB(38, 52, 76),     100,  "Plus100");
                AddLine(SB(38, 52, 76),    -100,  "Minus100");
                AddLine(SB(30, 42, 61),     200,  "Plus200");
                AddLine(SB(30, 42, 61),    -200,  "Minus200");

                AddPlot(SB(63, 209, 224),   "MainCCI");              // 0
                AddPlot(SB(174, 186, 206),  "TurboCCI");             // 1
                AddPlot(SB(233, 238, 247),  "TrendState");           // 2
                AddPlot(SB(233, 238, 247),  "TrendDirection");       // 3
                AddPlot(SB(233, 238, 247),  "TrendStrength");        // 4

                AddPlot(SB(37, 208, 139),   "LongTrendFilter");      // 5
                AddPlot(SB(255, 92, 106),   "ShortTrendFilter");     // 6

                AddPlot(SB(37, 208, 139),   "StrongBullTrend");      // 7
                AddPlot(SB(30, 150, 105),   "BullTrend");            // 8
                AddPlot(SB(108, 122, 146),  "NeutralTrend");         // 9
                AddPlot(SB(190, 74, 84),    "BearTrend");            // 10
                AddPlot(SB(255, 92, 106),   "StrongBearTrend");      // 11

                AddPlot(SB(24, 104, 76),    "BullWeakening");        // 12
                AddPlot(SB(150, 58, 66),    "BearWeakening");        // 13
                AddPlot(SB(37, 208, 139),   "BullStrengthening");    // 14
                AddPlot(SB(255, 92, 106),   "BearStrengthening");    // 15

                AddPlot(SB(63, 209, 224),   "ZLRLong");              // 16
                AddPlot(SB(242, 179, 76),   "ZLRShort");             // 17
                AddPlot(SB(63, 209, 224),   "HookLong");             // 18
                AddPlot(SB(242, 179, 76),   "HookShort");            // 19

                AddPlot(SB(37, 208, 139),   "BullishSignal");        // 20
                AddPlot(SB(255, 92, 106),   "BearishSignal");        // 21
            }
            else if (State == State.DataLoaded)
            {
                if (!ShowIndicatorLabel) Name = string.Empty;   // Sentinel label remover (NT draws the chart label from Name)

                mainCCI  = CCI(MainCCIPeriod);
                turboCCI = CCI(TurboCCIPeriod);

                finalTrendState     = 0;
                candidateTrendState = 0;
                candidateTrendCount = 0;

                strongBullBrushFrozen     = CopyAndFreeze(StrongBullBrush);
                bullBrushFrozen           = CopyAndFreeze(BullBrush);
                neutralBrushFrozen        = CopyAndFreeze(NeutralBrush);
                bearBrushFrozen           = CopyAndFreeze(BearBrush);
                strongBearBrushFrozen     = CopyAndFreeze(StrongBearBrush);
                bullWeakeningBrushFrozen  = CopyAndFreeze(BullWeakeningBrush);
                bearWeakeningBrushFrozen  = CopyAndFreeze(BearWeakeningBrush);

                mainCciBrushFrozen        = CopyAndFreeze(MainCCIPlotBrush);
                turboCciBrushFrozen       = CopyAndFreeze(TurboCCIPlotBrush);
                trendStateBrushFrozen     = CopyAndFreeze(TrendStatePlotBrush);
                signalBrushFrozen         = CopyAndFreeze(SignalPlotBrush);
            }
            else if (State == State.Terminated)
            {
                if (_sp != null) { try { _sp.Dispose(); } catch { } _sp = null; }
                try { SentinelSkin.CardLayout.Release(this); } catch { }
            }
        }
        #endregion

        #region OnBarUpdate
        protected override void OnBarUpdate()
        {
            int barsNeeded = Math.Max(MainCCIPeriod, TurboCCIPeriod) + Math.Max(3, SlopeLookback + 2);

            if (CurrentBar < barsNeeded)
            {
                SetAllPlotsToZero();
                return;
            }

            double cciM  = mainCCI[0];
            double cciT  = turboCCI[0];

            double cciM1 = mainCCI[1];
            double cciT1 = turboCCI[1];

            double mainSlope  = cciM - mainCCI[SlopeLookback];
            double turboSlope = cciT - turboCCI[SlopeLookback];

            Values[0][0] = ShowMainCCI  ? cciM : double.NaN;
            Values[1][0] = ShowTurboCCI ? cciT : double.NaN;

            PlotBrushes[0][0] = mainCciBrushFrozen;
            PlotBrushes[1][0] = turboCciBrushFrozen;

            int rawTrendState = GetRawTrendState(cciM, cciT, mainSlope, turboSlope);
            int trendState    = GetPersistedTrendState(rawTrendState, cciM);

            bool isBullish        = trendState > 0;
            bool isBearish        = trendState < 0;
            bool isStrongBullish  = trendState == 2;
            bool isStrongBearish  = trendState == -2;
            bool isNeutral        = trendState == 0;

            bool bullWeakening     = isBullish && IsBullWeakening(cciM, cciT, cciM1, cciT1);
            bool bearWeakening     = isBearish && IsBearWeakening(cciM, cciT, cciM1, cciT1);
            bool bullStrengthening = isBullish && IsBullStrengthening(cciM, cciT, cciM1, cciT1);
            bool bearStrengthening = isBearish && IsBearStrengthening(cciM, cciT, cciM1, cciT1);

            bool bullishSignal = cciM > 0 && (!EnableTurboConfirmation || cciT > 0);
            bool bearishSignal = cciM < 0 && (!EnableTurboConfirmation || cciT < 0);

            bool zlrLong  = false;
            bool zlrShort = false;

            if (EnableZLRSignals)
            {
                zlrLong =
                    trendState > 0 &&
                    mainCCI[2] > ZLRTrendThreshold &&
                    cciM1 < mainCCI[2] &&
                    cciM1 >= -NearZeroLevel &&
                    cciM1 <= NearZeroLevel &&
                    cciM > cciM1 &&
                    cciM > 0;

                zlrShort =
                    trendState < 0 &&
                    mainCCI[2] < -ZLRTrendThreshold &&
                    cciM1 > mainCCI[2] &&
                    cciM1 >= -NearZeroLevel &&
                    cciM1 <= NearZeroLevel &&
                    cciM < cciM1 &&
                    cciM < 0;
            }

            bool hookLong  = false;
            bool hookShort = false;

            if (EnableHookSignals)
            {
                hookLong =
                    trendState > 0 &&
                    cciM1 <= -ExtremeHookLevel &&
                    cciM > cciM1 &&
                    cciM < 0;

                hookShort =
                    trendState < 0 &&
                    cciM1 >= ExtremeHookLevel &&
                    cciM < cciM1 &&
                    cciM > 0;
            }

            Values[2][0]  = ShowTrendStatePlot && EnableTrendState ? trendState : 0;
            Values[3][0]  = ShowSignalPlots ? Math.Sign(trendState) : 0;
            Values[4][0]  = ShowSignalPlots ? Math.Abs(trendState) : 0;

            Values[5][0]  = ShowSignalPlots && isBullish ? 1 : 0;
            Values[6][0]  = ShowSignalPlots && isBearish ? -1 : 0;

            Values[7][0]  = ShowSignalPlots && isStrongBullish ? 1 : 0;
            Values[8][0]  = ShowSignalPlots && trendState == 1 ? 1 : 0;
            Values[9][0]  = ShowSignalPlots && isNeutral ? 1 : 0;
            Values[10][0] = ShowSignalPlots && trendState == -1 ? -1 : 0;
            Values[11][0] = ShowSignalPlots && isStrongBearish ? -1 : 0;

            Values[12][0] = ShowSignalPlots && bullWeakening ? 1 : 0;
            Values[13][0] = ShowSignalPlots && bearWeakening ? -1 : 0;
            Values[14][0] = ShowSignalPlots && bullStrengthening ? 1 : 0;
            Values[15][0] = ShowSignalPlots && bearStrengthening ? -1 : 0;

            Values[16][0] = ShowSignalPlots && zlrLong ? 1 : 0;
            Values[17][0] = ShowSignalPlots && zlrShort ? -1 : 0;
            Values[18][0] = ShowSignalPlots && hookLong ? 1 : 0;
            Values[19][0] = ShowSignalPlots && hookShort ? -1 : 0;

            Values[20][0] = ShowSignalPlots && bullishSignal ? 1 : 0;
            Values[21][0] = ShowSignalPlots && bearishSignal ? -1 : 0;

            for (int i = 2; i <= 21; i++)
                PlotBrushes[i][0] = signalBrushFrozen;

            PlotBrushes[2][0] = trendStateBrushFrozen;

            if (EnableBarColoring)
                ApplyBarColor(trendState, bullWeakening, bearWeakening);

            // ── stash for the card + publish ──
            lastTrendState = trendState;
            lastMainCci    = cciM;
            lastTurboCci   = cciT;
            lastMainSlope  = mainSlope;
            lastWeakening  = bullWeakening || bearWeakening;
            if      (zlrLong)  { lastSignal = 1;  lastSignalTag = "ZLR ▲"; }
            else if (hookLong) { lastSignal = 1;  lastSignalTag = "HOOK ▲"; }
            else if (zlrShort) { lastSignal = -1; lastSignalTag = "ZLR ▼"; }
            else if (hookShort){ lastSignal = -1; lastSignalTag = "HOOK ▼"; }
            else               { lastSignal = 0;  lastSignalTag = ""; }

            if (PublishState)
            {
                try
                {
                    // SentinelCore ≥ v1.18.0 — keyed by SCOPE, not instrument: two charts on one instrument used
                    // to overwrite each other's CCI reading every bar.
                    SentinelCore.SetCciState(Scope(), SentinelCore.BarTag(BarsPeriod), InstName(),
                        trendState, cciM, cciT, mainSlope,
                        lastSignal, lastWeakening, Name.Length == 0 ? "WoodiesCCIPro" : Name);
                }
                catch { }
            }
        }
        #endregion

        #region Trend Logic
        private int GetRawTrendState(double cciM, double cciT, double mainSlope, double turboSlope)
        {
            if (!EnableTrendState)
                return 0;

            bool mainBull = cciM > NeutralZoneLevel;
            bool mainBear = cciM < -NeutralZoneLevel;

            bool turboBull = !EnableTurboConfirmation || cciT > 0;
            bool turboBear = !EnableTurboConfirmation || cciT < 0;

            bool mainSlopeBull = !EnableSlopeConfirmation || mainSlope > MinCCISlope;
            bool mainSlopeBear = !EnableSlopeConfirmation || mainSlope < -MinCCISlope;

            bool turboSlopeBull = !EnableSlopeConfirmation || !EnableTurboConfirmation || turboSlope > MinCCISlope;
            bool turboSlopeBear = !EnableSlopeConfirmation || !EnableTurboConfirmation || turboSlope < -MinCCISlope;

            bool bullBase = mainBull && turboBull;
            bool bearBase = mainBear && turboBear;

            bool bullSlopeOk = mainSlopeBull && turboSlopeBull;
            bool bearSlopeOk = mainSlopeBear && turboSlopeBear;

            bool strongBull = cciM >= StrongTrendLevel &&
                              (!EnableTurboConfirmation || cciT >= TurboStrongTrendLevel);

            bool strongBear = cciM <= -StrongTrendLevel &&
                              (!EnableTurboConfirmation || cciT <= -TurboStrongTrendLevel);

            if (EnableStrictTrendMode)
            {
                if (strongBull && bullBase && bullSlopeOk)
                    return 2;

                if (strongBear && bearBase && bearSlopeOk)
                    return -2;

                if (bullBase && bullSlopeOk)
                    return 1;

                if (bearBase && bearSlopeOk)
                    return -1;

                return 0;
            }

            if (strongBull && bullBase)
                return 2;

            if (strongBear && bearBase)
                return -2;

            if (bullBase && bullSlopeOk)
                return 1;

            if (bearBase && bearSlopeOk)
                return -1;

            return 0;
        }

        private int GetPersistedTrendState(int rawTrendState, double cciM)
        {
            if (!EnablePersistenceConfirmation)
            {
                finalTrendState = rawTrendState;
                return finalTrendState;
            }

            if (rawTrendState == 0)
            {
                candidateTrendState = 0;
                candidateTrendCount = 0;

                if (EnableNeutralSuppression && finalTrendState != 0 && Math.Abs(cciM) <= NeutralSuppressionLevel)
                    return finalTrendState;

                finalTrendState = 0;
                return finalTrendState;
            }

            if (finalTrendState != 0 && Math.Sign(rawTrendState) == Math.Sign(finalTrendState))
            {
                finalTrendState = rawTrendState;
                candidateTrendState = 0;
                candidateTrendCount = 0;
                return finalTrendState;
            }

            if (rawTrendState == candidateTrendState)
                candidateTrendCount++;
            else
            {
                candidateTrendState = rawTrendState;
                candidateTrendCount = 1;
            }

            if (candidateTrendCount >= PersistenceBars)
            {
                finalTrendState = rawTrendState;
                candidateTrendState = 0;
                candidateTrendCount = 0;
            }

            return finalTrendState;
        }
        #endregion

        #region Weakening / Strengthening Logic
        private bool IsBullWeakening(double cciM, double cciT, double cciM1, double cciT1)
        {
            if (!EnableWeakeningColoring)
                return false;

            if (EnableTurboConfirmation)
                return cciM < cciM1 || cciT < cciT1;

            return cciM < cciM1;
        }

        private bool IsBearWeakening(double cciM, double cciT, double cciM1, double cciT1)
        {
            if (!EnableWeakeningColoring)
                return false;

            if (EnableTurboConfirmation)
                return cciM > cciM1 || cciT > cciT1;

            return cciM > cciM1;
        }

        private bool IsBullStrengthening(double cciM, double cciT, double cciM1, double cciT1)
        {
            if (EnableTurboConfirmation)
                return cciM > cciM1 && cciT > cciT1;

            return cciM > cciM1;
        }

        private bool IsBearStrengthening(double cciM, double cciT, double cciM1, double cciT1)
        {
            if (EnableTurboConfirmation)
                return cciM < cciM1 && cciT < cciT1;

            return cciM < cciM1;
        }
        #endregion

        #region Visual Helpers
        private void ApplyBarColor(int trendState, bool bullWeakening, bool bearWeakening)
        {
            if (trendState == 2)
            {
                BarBrush = bullWeakening ? bullWeakeningBrushFrozen : strongBullBrushFrozen;
            }
            else if (trendState == 1)
            {
                BarBrush = bullWeakening ? bullWeakeningBrushFrozen : bullBrushFrozen;
            }
            else if (trendState == -2)
            {
                BarBrush = bearWeakening ? bearWeakeningBrushFrozen : strongBearBrushFrozen;
            }
            else if (trendState == -1)
            {
                BarBrush = bearWeakening ? bearWeakeningBrushFrozen : bearBrushFrozen;
            }
            else
            {
                BarBrush = neutralBrushFrozen;
            }

            CandleOutlineBrush = BarBrush;
        }

        private Brush CopyAndFreeze(Brush input)
        {
            if (input == null)
                return null;

            Brush clone = input.Clone();
            clone.Freeze();
            return clone;
        }

        private void SetAllPlotsToZero()
        {
            for (int i = 0; i < Values.Length; i++)
                Values[i][0] = 0;
        }

        private string InstName() { return (Instrument != null && Instrument.MasterInstrument != null) ? Instrument.MasterInstrument.Name : "unknown"; }

        // ── scope (SentinelCore v1.18.0 · execution plan 1.4) ──
        // "<masterInstrument>.<barTag>" — ONE CHART's worth of context. Resolved lazily and cached; a null scope
        // no-ops the publish, the right fail-silent for an indicator that is not yet configured.
        private string _scope;
        private string Scope()
        {
            if (_scope == null) { try { _scope = SentinelCore.ScopeOf(Instrument, BarsPeriod); } catch { } }
            return _scope;
        }
        #endregion

        #region Sentinel glass card (SharpDX)
        // The Sentinel "flight-instrument" readout, docked in THIS oscillator's panel via CardLayout so it
        // never covers another Sentinel card. Compact: state pill + Main/Turbo hero + strength track + slope/signal.
        protected override void OnRender(NinjaTrader.Gui.Chart.ChartControl chartControl, NinjaTrader.Gui.Chart.ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);
            if (RenderTarget == null || ChartPanel == null) return;
            if (_sp == null) _sp = new SentinelSkin.Painter();
            _sp.Begin(RenderTarget);
            try { if (SentinelPlotSkin) RenderPlotSkin(chartControl, chartScale); } catch { }
            try { if (ShowInfo) RenderCard(); } catch { }
            try { _sp.End(); } catch { }
        }

        // Sentinel PLOT STANDARD for the Woodies panel: glass wash + a bottom TREND RIBBON (per-bar trend
        // state from the strong/normal flags) + themed 0 / ±100 reference lines + glowing Main (cyan) and
        // Turbo (mute) CCI lines. Series read by ABSOLUTE bar index (render-safe).
        private void RenderPlotSkin(NinjaTrader.Gui.Chart.ChartControl chartControl, NinjaTrader.Gui.Chart.ChartScale chartScale)
        {
            if (Bars == null || Bars.Count < 2 || ChartBars == null) return;
            float px = ChartPanel.X, py = ChartPanel.Y, pw = ChartPanel.W, ph = ChartPanel.H;
            _sp.PanelWash(px, py, pw, ph);

            int from = ChartBars.FromIndex, to = ChartBars.ToIndex;
            if (from < 0) from = 0;
            if (to > Bars.Count - 1) to = Bars.Count - 1;
            if (to < from) return;

            float dx = to > from ? (chartControl.GetXByBarIndex(ChartBars, to) - chartControl.GetXByBarIndex(ChartBars, to - 1)) : 6f;
            if (dx <= 0f || float.IsNaN(dx) || float.IsInfinity(dx)) dx = 6f;
            float halfW = Math.Max(0.8f, dx * 0.5f);

            float ribTop = py + ph - 14f, ribH = 11f;
            var mainPts  = new System.Collections.Generic.List<SharpDX.Vector2>();
            var turboPts = new System.Collections.Generic.List<SharpDX.Vector2>();
            for (int idx = from; idx <= to; idx++)
            {
                float x = chartControl.GetXByBarIndex(ChartBars, idx);

                // per-bar trend state from the strong/normal flags (−2..+2)
                int tstate = 0;
                if (Values[11].IsValidDataPointAt(idx) && Values[11].GetValueAt(idx) < 0) tstate = -2;
                else if (Values[10].IsValidDataPointAt(idx) && Values[10].GetValueAt(idx) < 0) tstate = -1;
                else if (Values[7].IsValidDataPointAt(idx) && Values[7].GetValueAt(idx) > 0) tstate = 2;
                else if (Values[8].IsValidDataPointAt(idx) && Values[8].GetValueAt(idx) > 0) tstate = 1;
                var rc = tstate > 0 ? SentinelSkin.CUp : (tstate < 0 ? SentinelSkin.CDown : SentinelSkin.CMute);
                float a = Math.Abs(tstate) >= 2 ? 0.85f : (tstate != 0 ? 0.5f : 0.16f);
                _sp.RegimeShade(x - halfW, ribTop, halfW * 2f, ribH, rc, a);

                if (Values[0].IsValidDataPointAt(idx)) { double v = Values[0].GetValueAt(idx); if (!double.IsNaN(v)) mainPts.Add(new SharpDX.Vector2(x, chartScale.GetYByValue(v))); }
                if (Values[1].IsValidDataPointAt(idx)) { double v = Values[1].GetValueAt(idx); if (!double.IsNaN(v)) turboPts.Add(new SharpDX.Vector2(x, chartScale.GetYByValue(v))); }
            }

            // themed reference lines (zero baseline + faint ±100)
            _sp.Baseline(px, px + pw, chartScale.GetYByValue(0), SentinelSkin.CInk);
            _sp.Line(px, chartScale.GetYByValue(100),  px + pw, chartScale.GetYByValue(100),  SentinelSkin.Alpha(SentinelSkin.CInk, 0.05f), 1f);
            _sp.Line(px, chartScale.GetYByValue(-100), px + pw, chartScale.GetYByValue(-100), SentinelSkin.Alpha(SentinelSkin.CInk, 0.05f), 1f);

            // turbo under, main over
            if (turboPts.Count > 1) _sp.GlowLine(turboPts, SentinelSkin.CMute,   1.3f, 0.08f);
            if (mainPts.Count  > 1) _sp.GlowLine(mainPts,  SentinelSkin.CAccent, 1.8f, 0.20f);
        }

        // Sentinel glass card (content unchanged; painted inside the shared Begin/End frame).
        private void RenderCard()
        {
            const float cw = 236f, ch = 120f;
            var slot = SentinelSkin.CardLayout.Place(this, ChartPanel,
                ChartPanel.X, ChartPanel.Y, ChartPanel.W, ChartPanel.H, CardCorner, cw, ch);

            int ts = lastTrendState;
                var dir = ts > 0 ? SentinelSkin.CUp : (ts < 0 ? SentinelSkin.CDown : SentinelSkin.CMute);
                bool live = ts != 0;
                var edge = live ? dir : SentinelSkin.CDim;
                var r = _sp.Card(slot.X, slot.Y, cw, ch, edge);

                // header: dot + title + state pill
                _sp.Dot(r.Left + 5f, r.Top + 8f, live ? (lastWeakening ? SentinelSkin.CWarn : dir) : SentinelSkin.CMute, live);
                _sp.Text("WOODIES CCI", r.Left + 16f, r.Top, r.Width - 84f, 16f, SentinelSkin.CInk, 11f, true);
                string st = ts == 2 ? "S.BULL" : ts == 1 ? "BULL" : ts == -1 ? "BEAR" : ts == -2 ? "S.BEAR" : "NEUTRAL";
                _sp.Pill(st, r.Right, r.Top - 1f, dir);

                // main CCI hero (colored by polarity) + turbo beside it
                var mCol = lastMainCci > 0 ? SentinelSkin.CUp : (lastMainCci < 0 ? SentinelSkin.CDown : SentinelSkin.CInk2);
                _sp.Text("MAIN", r.Left, r.Top + 26f, 60f, 12f, SentinelSkin.CMute, 9f, true);
                _sp.Text(lastMainCci.ToString("0"), r.Left, r.Top + 35f, 100f, 26f, mCol, 22f);
                var tCol = lastTurboCci > 0 ? SentinelSkin.CUp : (lastTurboCci < 0 ? SentinelSkin.CDown : SentinelSkin.CInk2);
                _sp.Text("turbo " + lastTurboCci.ToString("0"), r.Left + 92f, r.Top + 45f, 130f, 14f, tCol, 10.5f);

                // strength track: |main| vs StrongTrendLevel
                float frac = StrongTrendLevel > 0 ? (float)Math.Max(0, Math.Min(1, Math.Abs(lastMainCci) / StrongTrendLevel)) : 0f;
                _sp.Track(r.Left, r.Top + 66f, r.Width, frac, live ? dir : SentinelSkin.CFaint, 5f);

                // slope + signal row (mono)
                var lead = SharpDX.DirectWrite.TextAlignment.Leading;
                string wk = lastWeakening ? "  weak" : "";
                string sig = lastSignalTag.Length > 0 ? "   " + lastSignalTag : "";
                var sigCol = lastSignal > 0 ? SentinelSkin.CUp : (lastSignal < 0 ? SentinelSkin.CDown : SentinelSkin.CMute);
                _sp.Text("slope " + lastMainSlope.ToString("+0;-0;0") + wk,
                    r.Left, r.Top + 78f, r.Width - 70f, 14f, SentinelSkin.CInk2, 10.5f, false, lead, true);
                if (sig.Length > 0)
                    _sp.Text(lastSignalTag, r.Left, r.Top + 78f, r.Width, 14f, sigCol, 10.5f, true, SharpDX.DirectWrite.TextAlignment.Trailing, true);
                _sp.Text(InstName(), r.Left, r.Top + 92f, r.Width, 12f, SentinelSkin.CMute, 9f, false, lead, true);
        }
        #endregion

        #region Properties

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Main CCI Period", GroupName = "01. CCI Parameters", Order = 0)]
        public int MainCCIPeriod
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Turbo CCI Period", GroupName = "01. CCI Parameters", Order = 1)]
        public int TurboCCIPeriod
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Bar Coloring", GroupName = "02. Behavior", Order = 10)]
        public bool EnableBarColoring
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Trend State", GroupName = "02. Behavior", Order = 11)]
        public bool EnableTrendState
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Turbo Confirmation", GroupName = "02. Behavior", Order = 12)]
        public bool EnableTurboConfirmation
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Slope Confirmation", GroupName = "02. Behavior", Order = 13)]
        public bool EnableSlopeConfirmation
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Persistence Confirmation", GroupName = "02. Behavior", Order = 14)]
        public bool EnablePersistenceConfirmation
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Strict Trend Mode", GroupName = "02. Behavior", Order = 15)]
        public bool EnableStrictTrendMode
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Neutral Suppression", GroupName = "02. Behavior", Order = 16)]
        public bool EnableNeutralSuppression
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Weakening Coloring", GroupName = "02. Behavior", Order = 17)]
        public bool EnableWeakeningColoring
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable ZLR Signals", GroupName = "02. Behavior", Order = 18)]
        public bool EnableZLRSignals
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Hook Signals", GroupName = "02. Behavior", Order = 19)]
        public bool EnableHookSignals
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Main CCI", GroupName = "03. Visual", Order = 20)]
        public bool ShowMainCCI
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Turbo CCI", GroupName = "03. Visual", Order = 21)]
        public bool ShowTurboCCI
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Trend State Plot", GroupName = "03. Visual", Order = 22)]
        public bool ShowTrendStatePlot
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Signal Plots", GroupName = "03. Visual", Order = 23)]
        public bool ShowSignalPlots
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Strong Trend Level", GroupName = "04. Thresholds", Order = 30)]
        public double StrongTrendLevel
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Turbo Strong Trend Level", GroupName = "04. Thresholds", Order = 31)]
        public double TurboStrongTrendLevel
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Neutral Zone Level", GroupName = "04. Thresholds", Order = 32)]
        public double NeutralZoneLevel
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Neutral Suppression Level", GroupName = "04. Thresholds", Order = 33)]
        public double NeutralSuppressionLevel
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Minimum CCI Slope", GroupName = "04. Thresholds", Order = 34)]
        public double MinCCISlope
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Slope Lookback", GroupName = "04. Thresholds", Order = 35)]
        public int SlopeLookback
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Persistence Bars", GroupName = "04. Thresholds", Order = 36)]
        public int PersistenceBars
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Extreme Hook Level", GroupName = "05. Pattern Thresholds", Order = 40)]
        public double ExtremeHookLevel
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Near Zero Level", GroupName = "05. Pattern Thresholds", Order = 41)]
        public double NearZeroLevel
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ZLR Trend Threshold", GroupName = "05. Pattern Thresholds", Order = 42)]
        public double ZLRTrendThreshold
        { get; set; }

        [XmlIgnore]
        [Display(Name = "Strong Bull Brush", GroupName = "06. Bar Brushes", Order = 50)]
        public Brush StrongBullBrush
        { get; set; }

        [Browsable(false)]
        public string StrongBullBrushSerializable
        {
            get { return Serialize.BrushToString(StrongBullBrush); }
            set { StrongBullBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Bull Brush", GroupName = "06. Bar Brushes", Order = 51)]
        public Brush BullBrush
        { get; set; }

        [Browsable(false)]
        public string BullBrushSerializable
        {
            get { return Serialize.BrushToString(BullBrush); }
            set { BullBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Neutral Brush", GroupName = "06. Bar Brushes", Order = 52)]
        public Brush NeutralBrush
        { get; set; }

        [Browsable(false)]
        public string NeutralBrushSerializable
        {
            get { return Serialize.BrushToString(NeutralBrush); }
            set { NeutralBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Bear Brush", GroupName = "06. Bar Brushes", Order = 53)]
        public Brush BearBrush
        { get; set; }

        [Browsable(false)]
        public string BearBrushSerializable
        {
            get { return Serialize.BrushToString(BearBrush); }
            set { BearBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Strong Bear Brush", GroupName = "06. Bar Brushes", Order = 54)]
        public Brush StrongBearBrush
        { get; set; }

        [Browsable(false)]
        public string StrongBearBrushSerializable
        {
            get { return Serialize.BrushToString(StrongBearBrush); }
            set { StrongBearBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Bull Weakening Brush", GroupName = "06. Bar Brushes", Order = 55)]
        public Brush BullWeakeningBrush
        { get; set; }

        [Browsable(false)]
        public string BullWeakeningBrushSerializable
        {
            get { return Serialize.BrushToString(BullWeakeningBrush); }
            set { BullWeakeningBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Bear Weakening Brush", GroupName = "06. Bar Brushes", Order = 56)]
        public Brush BearWeakeningBrush
        { get; set; }

        [Browsable(false)]
        public string BearWeakeningBrushSerializable
        {
            get { return Serialize.BrushToString(BearWeakeningBrush); }
            set { BearWeakeningBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Main CCI Plot Brush", GroupName = "07. Plot Brushes", Order = 60)]
        public Brush MainCCIPlotBrush
        { get; set; }

        [Browsable(false)]
        public string MainCCIPlotBrushSerializable
        {
            get { return Serialize.BrushToString(MainCCIPlotBrush); }
            set { MainCCIPlotBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Turbo CCI Plot Brush", GroupName = "07. Plot Brushes", Order = 61)]
        public Brush TurboCCIPlotBrush
        { get; set; }

        [Browsable(false)]
        public string TurboCCIPlotBrushSerializable
        {
            get { return Serialize.BrushToString(TurboCCIPlotBrush); }
            set { TurboCCIPlotBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Trend State Plot Brush", GroupName = "07. Plot Brushes", Order = 62)]
        public Brush TrendStatePlotBrush
        { get; set; }

        [Browsable(false)]
        public string TrendStatePlotBrushSerializable
        {
            get { return Serialize.BrushToString(TrendStatePlotBrush); }
            set { TrendStatePlotBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Signal Plot Brush", GroupName = "07. Plot Brushes", Order = 63)]
        public Brush SignalPlotBrush
        { get; set; }

        [Browsable(false)]
        public string SignalPlotBrushSerializable
        {
            get { return Serialize.BrushToString(SignalPlotBrush); }
            set { SignalPlotBrush = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Display(Name = "Show info readout", GroupName = "08. Sentinel", Order = 70)]
        public bool ShowInfo
        { get; set; }

        // Not [NinjaScriptProperty] — serializes without a constructor param (no generated-region churn).
        [Display(Name = "Sentinel Plot Skin", Description = "Render the panel to the Sentinel plot standard (glass wash + trend ribbon + glowing Main/Turbo CCI lines) instead of NT's stock plots.", GroupName = "08. Sentinel", Order = 71)]
        public bool SentinelPlotSkin { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Publish Sentinel state", GroupName = "08. Sentinel", Order = 71)]
        public bool PublishState
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Card corner", GroupName = "08. Sentinel", Order = 72)]
        public SentinelCardCorner CardCorner
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show indicator label", Description = "Show NinjaTrader's chart name label. Sentinel default = OFF (clean chart); turn on to restore it.", GroupName = "08. Sentinel", Order = 100)]
        public bool ShowIndicatorLabel
        { get; set; }

        #endregion

        #region Strategy Builder Series

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> MainCCI
        {
            get { return Values[0]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> TurboCCI
        {
            get { return Values[1]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> TrendState
        {
            get { return Values[2]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> TrendDirection
        {
            get { return Values[3]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> TrendStrength
        {
            get { return Values[4]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> LongTrendFilter
        {
            get { return Values[5]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> ShortTrendFilter
        {
            get { return Values[6]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> StrongBullTrend
        {
            get { return Values[7]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> BullTrend
        {
            get { return Values[8]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> NeutralTrend
        {
            get { return Values[9]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> BearTrend
        {
            get { return Values[10]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> StrongBearTrend
        {
            get { return Values[11]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> BullWeakening
        {
            get { return Values[12]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> BearWeakening
        {
            get { return Values[13]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> BullStrengthening
        {
            get { return Values[14]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> BearStrengthening
        {
            get { return Values[15]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> ZLRLong
        {
            get { return Values[16]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> ZLRShort
        {
            get { return Values[17]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> HookLong
        {
            get { return Values[18]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> HookShort
        {
            get { return Values[19]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> BullishSignal
        {
            get { return Values[20]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> BearishSignal
        {
            get { return Values[21]; }
        }

        #endregion

        // ── HEARTBEAT (SentinelCore v1.19.0) ─────────────────────────────────────────────────
        // An OnBarClose publisher only refreshes its seam when a bar closes. In a quiet market bars close
        // slowly, the seam ages past the Council's StaleSec, and a perfectly healthy voter silently drops
        // out of the roster — observed live as a FULLY LOADED chart reporting "roster 3/10". The Council
        // already heartbeats its own verdict; its sensors need the same. Re-stamp the cached reading on
        // incoming quotes: no recompute, realtime only (a historical re-stamp would fake freshness onto a
        // replayed bar), throttled.
        private DateTime _lastHeartbeatUtc;
        private const double HeartbeatSec = 5.0;
        protected override void OnMarketData(NinjaTrader.Data.MarketDataEventArgs marketDataUpdate)
        {
            if (!PublishState || State != State.Realtime) return;
            DateTime now = DateTime.UtcNow;
            if ((now - _lastHeartbeatUtc).TotalSeconds < HeartbeatSec) return;
            _lastHeartbeatUtc = now;
            try { SentinelCore.TouchCciState(Scope()); } catch { }
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private Sentinel.Sensors.WoodiesCCIPro_v1_0_0[] cacheWoodiesCCIPro_v1_0_0;
		public Sentinel.Sensors.WoodiesCCIPro_v1_0_0 WoodiesCCIPro_v1_0_0(int mainCCIPeriod, int turboCCIPeriod, bool enableBarColoring, bool enableTrendState, bool enableTurboConfirmation, bool enableSlopeConfirmation, bool enablePersistenceConfirmation, bool enableStrictTrendMode, bool enableNeutralSuppression, bool enableWeakeningColoring, bool enableZLRSignals, bool enableHookSignals, bool showMainCCI, bool showTurboCCI, bool showTrendStatePlot, bool showSignalPlots, double strongTrendLevel, double turboStrongTrendLevel, double neutralZoneLevel, double neutralSuppressionLevel, double minCCISlope, int slopeLookback, int persistenceBars, double extremeHookLevel, double nearZeroLevel, double zLRTrendThreshold, bool showInfo, bool publishState, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return WoodiesCCIPro_v1_0_0(Input, mainCCIPeriod, turboCCIPeriod, enableBarColoring, enableTrendState, enableTurboConfirmation, enableSlopeConfirmation, enablePersistenceConfirmation, enableStrictTrendMode, enableNeutralSuppression, enableWeakeningColoring, enableZLRSignals, enableHookSignals, showMainCCI, showTurboCCI, showTrendStatePlot, showSignalPlots, strongTrendLevel, turboStrongTrendLevel, neutralZoneLevel, neutralSuppressionLevel, minCCISlope, slopeLookback, persistenceBars, extremeHookLevel, nearZeroLevel, zLRTrendThreshold, showInfo, publishState, cardCorner, showIndicatorLabel);
		}

		public Sentinel.Sensors.WoodiesCCIPro_v1_0_0 WoodiesCCIPro_v1_0_0(ISeries<double> input, int mainCCIPeriod, int turboCCIPeriod, bool enableBarColoring, bool enableTrendState, bool enableTurboConfirmation, bool enableSlopeConfirmation, bool enablePersistenceConfirmation, bool enableStrictTrendMode, bool enableNeutralSuppression, bool enableWeakeningColoring, bool enableZLRSignals, bool enableHookSignals, bool showMainCCI, bool showTurboCCI, bool showTrendStatePlot, bool showSignalPlots, double strongTrendLevel, double turboStrongTrendLevel, double neutralZoneLevel, double neutralSuppressionLevel, double minCCISlope, int slopeLookback, int persistenceBars, double extremeHookLevel, double nearZeroLevel, double zLRTrendThreshold, bool showInfo, bool publishState, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			if (cacheWoodiesCCIPro_v1_0_0 != null)
				for (int idx = 0; idx < cacheWoodiesCCIPro_v1_0_0.Length; idx++)
					if (cacheWoodiesCCIPro_v1_0_0[idx] != null && cacheWoodiesCCIPro_v1_0_0[idx].MainCCIPeriod == mainCCIPeriod && cacheWoodiesCCIPro_v1_0_0[idx].TurboCCIPeriod == turboCCIPeriod && cacheWoodiesCCIPro_v1_0_0[idx].EnableBarColoring == enableBarColoring && cacheWoodiesCCIPro_v1_0_0[idx].EnableTrendState == enableTrendState && cacheWoodiesCCIPro_v1_0_0[idx].EnableTurboConfirmation == enableTurboConfirmation && cacheWoodiesCCIPro_v1_0_0[idx].EnableSlopeConfirmation == enableSlopeConfirmation && cacheWoodiesCCIPro_v1_0_0[idx].EnablePersistenceConfirmation == enablePersistenceConfirmation && cacheWoodiesCCIPro_v1_0_0[idx].EnableStrictTrendMode == enableStrictTrendMode && cacheWoodiesCCIPro_v1_0_0[idx].EnableNeutralSuppression == enableNeutralSuppression && cacheWoodiesCCIPro_v1_0_0[idx].EnableWeakeningColoring == enableWeakeningColoring && cacheWoodiesCCIPro_v1_0_0[idx].EnableZLRSignals == enableZLRSignals && cacheWoodiesCCIPro_v1_0_0[idx].EnableHookSignals == enableHookSignals && cacheWoodiesCCIPro_v1_0_0[idx].ShowMainCCI == showMainCCI && cacheWoodiesCCIPro_v1_0_0[idx].ShowTurboCCI == showTurboCCI && cacheWoodiesCCIPro_v1_0_0[idx].ShowTrendStatePlot == showTrendStatePlot && cacheWoodiesCCIPro_v1_0_0[idx].ShowSignalPlots == showSignalPlots && cacheWoodiesCCIPro_v1_0_0[idx].StrongTrendLevel == strongTrendLevel && cacheWoodiesCCIPro_v1_0_0[idx].TurboStrongTrendLevel == turboStrongTrendLevel && cacheWoodiesCCIPro_v1_0_0[idx].NeutralZoneLevel == neutralZoneLevel && cacheWoodiesCCIPro_v1_0_0[idx].NeutralSuppressionLevel == neutralSuppressionLevel && cacheWoodiesCCIPro_v1_0_0[idx].MinCCISlope == minCCISlope && cacheWoodiesCCIPro_v1_0_0[idx].SlopeLookback == slopeLookback && cacheWoodiesCCIPro_v1_0_0[idx].PersistenceBars == persistenceBars && cacheWoodiesCCIPro_v1_0_0[idx].ExtremeHookLevel == extremeHookLevel && cacheWoodiesCCIPro_v1_0_0[idx].NearZeroLevel == nearZeroLevel && cacheWoodiesCCIPro_v1_0_0[idx].ZLRTrendThreshold == zLRTrendThreshold && cacheWoodiesCCIPro_v1_0_0[idx].ShowInfo == showInfo && cacheWoodiesCCIPro_v1_0_0[idx].PublishState == publishState && cacheWoodiesCCIPro_v1_0_0[idx].CardCorner == cardCorner && cacheWoodiesCCIPro_v1_0_0[idx].ShowIndicatorLabel == showIndicatorLabel && cacheWoodiesCCIPro_v1_0_0[idx].EqualsInput(input))
						return cacheWoodiesCCIPro_v1_0_0[idx];
			return CacheIndicator<Sentinel.Sensors.WoodiesCCIPro_v1_0_0>(new Sentinel.Sensors.WoodiesCCIPro_v1_0_0(){ MainCCIPeriod = mainCCIPeriod, TurboCCIPeriod = turboCCIPeriod, EnableBarColoring = enableBarColoring, EnableTrendState = enableTrendState, EnableTurboConfirmation = enableTurboConfirmation, EnableSlopeConfirmation = enableSlopeConfirmation, EnablePersistenceConfirmation = enablePersistenceConfirmation, EnableStrictTrendMode = enableStrictTrendMode, EnableNeutralSuppression = enableNeutralSuppression, EnableWeakeningColoring = enableWeakeningColoring, EnableZLRSignals = enableZLRSignals, EnableHookSignals = enableHookSignals, ShowMainCCI = showMainCCI, ShowTurboCCI = showTurboCCI, ShowTrendStatePlot = showTrendStatePlot, ShowSignalPlots = showSignalPlots, StrongTrendLevel = strongTrendLevel, TurboStrongTrendLevel = turboStrongTrendLevel, NeutralZoneLevel = neutralZoneLevel, NeutralSuppressionLevel = neutralSuppressionLevel, MinCCISlope = minCCISlope, SlopeLookback = slopeLookback, PersistenceBars = persistenceBars, ExtremeHookLevel = extremeHookLevel, NearZeroLevel = nearZeroLevel, ZLRTrendThreshold = zLRTrendThreshold, ShowInfo = showInfo, PublishState = publishState, CardCorner = cardCorner, ShowIndicatorLabel = showIndicatorLabel }, input, ref cacheWoodiesCCIPro_v1_0_0);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.Sentinel.Sensors.WoodiesCCIPro_v1_0_0 WoodiesCCIPro_v1_0_0(int mainCCIPeriod, int turboCCIPeriod, bool enableBarColoring, bool enableTrendState, bool enableTurboConfirmation, bool enableSlopeConfirmation, bool enablePersistenceConfirmation, bool enableStrictTrendMode, bool enableNeutralSuppression, bool enableWeakeningColoring, bool enableZLRSignals, bool enableHookSignals, bool showMainCCI, bool showTurboCCI, bool showTrendStatePlot, bool showSignalPlots, double strongTrendLevel, double turboStrongTrendLevel, double neutralZoneLevel, double neutralSuppressionLevel, double minCCISlope, int slopeLookback, int persistenceBars, double extremeHookLevel, double nearZeroLevel, double zLRTrendThreshold, bool showInfo, bool publishState, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return indicator.WoodiesCCIPro_v1_0_0(Input, mainCCIPeriod, turboCCIPeriod, enableBarColoring, enableTrendState, enableTurboConfirmation, enableSlopeConfirmation, enablePersistenceConfirmation, enableStrictTrendMode, enableNeutralSuppression, enableWeakeningColoring, enableZLRSignals, enableHookSignals, showMainCCI, showTurboCCI, showTrendStatePlot, showSignalPlots, strongTrendLevel, turboStrongTrendLevel, neutralZoneLevel, neutralSuppressionLevel, minCCISlope, slopeLookback, persistenceBars, extremeHookLevel, nearZeroLevel, zLRTrendThreshold, showInfo, publishState, cardCorner, showIndicatorLabel);
		}

		public Indicators.Sentinel.Sensors.WoodiesCCIPro_v1_0_0 WoodiesCCIPro_v1_0_0(ISeries<double> input , int mainCCIPeriod, int turboCCIPeriod, bool enableBarColoring, bool enableTrendState, bool enableTurboConfirmation, bool enableSlopeConfirmation, bool enablePersistenceConfirmation, bool enableStrictTrendMode, bool enableNeutralSuppression, bool enableWeakeningColoring, bool enableZLRSignals, bool enableHookSignals, bool showMainCCI, bool showTurboCCI, bool showTrendStatePlot, bool showSignalPlots, double strongTrendLevel, double turboStrongTrendLevel, double neutralZoneLevel, double neutralSuppressionLevel, double minCCISlope, int slopeLookback, int persistenceBars, double extremeHookLevel, double nearZeroLevel, double zLRTrendThreshold, bool showInfo, bool publishState, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return indicator.WoodiesCCIPro_v1_0_0(input, mainCCIPeriod, turboCCIPeriod, enableBarColoring, enableTrendState, enableTurboConfirmation, enableSlopeConfirmation, enablePersistenceConfirmation, enableStrictTrendMode, enableNeutralSuppression, enableWeakeningColoring, enableZLRSignals, enableHookSignals, showMainCCI, showTurboCCI, showTrendStatePlot, showSignalPlots, strongTrendLevel, turboStrongTrendLevel, neutralZoneLevel, neutralSuppressionLevel, minCCISlope, slopeLookback, persistenceBars, extremeHookLevel, nearZeroLevel, zLRTrendThreshold, showInfo, publishState, cardCorner, showIndicatorLabel);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.Sentinel.Sensors.WoodiesCCIPro_v1_0_0 WoodiesCCIPro_v1_0_0(int mainCCIPeriod, int turboCCIPeriod, bool enableBarColoring, bool enableTrendState, bool enableTurboConfirmation, bool enableSlopeConfirmation, bool enablePersistenceConfirmation, bool enableStrictTrendMode, bool enableNeutralSuppression, bool enableWeakeningColoring, bool enableZLRSignals, bool enableHookSignals, bool showMainCCI, bool showTurboCCI, bool showTrendStatePlot, bool showSignalPlots, double strongTrendLevel, double turboStrongTrendLevel, double neutralZoneLevel, double neutralSuppressionLevel, double minCCISlope, int slopeLookback, int persistenceBars, double extremeHookLevel, double nearZeroLevel, double zLRTrendThreshold, bool showInfo, bool publishState, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return indicator.WoodiesCCIPro_v1_0_0(Input, mainCCIPeriod, turboCCIPeriod, enableBarColoring, enableTrendState, enableTurboConfirmation, enableSlopeConfirmation, enablePersistenceConfirmation, enableStrictTrendMode, enableNeutralSuppression, enableWeakeningColoring, enableZLRSignals, enableHookSignals, showMainCCI, showTurboCCI, showTrendStatePlot, showSignalPlots, strongTrendLevel, turboStrongTrendLevel, neutralZoneLevel, neutralSuppressionLevel, minCCISlope, slopeLookback, persistenceBars, extremeHookLevel, nearZeroLevel, zLRTrendThreshold, showInfo, publishState, cardCorner, showIndicatorLabel);
		}

		public Indicators.Sentinel.Sensors.WoodiesCCIPro_v1_0_0 WoodiesCCIPro_v1_0_0(ISeries<double> input , int mainCCIPeriod, int turboCCIPeriod, bool enableBarColoring, bool enableTrendState, bool enableTurboConfirmation, bool enableSlopeConfirmation, bool enablePersistenceConfirmation, bool enableStrictTrendMode, bool enableNeutralSuppression, bool enableWeakeningColoring, bool enableZLRSignals, bool enableHookSignals, bool showMainCCI, bool showTurboCCI, bool showTrendStatePlot, bool showSignalPlots, double strongTrendLevel, double turboStrongTrendLevel, double neutralZoneLevel, double neutralSuppressionLevel, double minCCISlope, int slopeLookback, int persistenceBars, double extremeHookLevel, double nearZeroLevel, double zLRTrendThreshold, bool showInfo, bool publishState, SentinelCardCorner cardCorner, bool showIndicatorLabel)
		{
			return indicator.WoodiesCCIPro_v1_0_0(input, mainCCIPeriod, turboCCIPeriod, enableBarColoring, enableTrendState, enableTurboConfirmation, enableSlopeConfirmation, enablePersistenceConfirmation, enableStrictTrendMode, enableNeutralSuppression, enableWeakeningColoring, enableZLRSignals, enableHookSignals, showMainCCI, showTurboCCI, showTrendStatePlot, showSignalPlots, strongTrendLevel, turboStrongTrendLevel, neutralZoneLevel, neutralSuppressionLevel, minCCISlope, slopeLookback, persistenceBars, extremeHookLevel, nearZeroLevel, zLRTrendThreshold, showInfo, publishState, cardCorner, showIndicatorLabel);
		}
	}
}

#endregion
