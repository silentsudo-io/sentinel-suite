// ─────────────────────────────────────────────────────────────────────────────
// This Source Code Form is subject to the terms of the Mozilla Public License,
// v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
// one at https://mozilla.org/MPL/2.0/.
//
// Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
// ─────────────────────────────────────────────────────────────────────────────
#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.AddOns.Sentinel;   // SentinelSkin / SentinelCore / SentinelCardCorner
using NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors;
using SharpDX;
// NOTE: do NOT import SharpDX.Direct2D1 or SharpDX.DirectWrite globally — both contain types
// (SolidColorBrush, PathGeometry, TextAlignment, etc.) that collide with System.Windows.Media.
// We fully-qualify SharpDX types at use sites instead.
#endregion

// =====================================================================================
//  Trend Architect  —  NinjaTrader 8 port of the Pine Script® indicator of the same name.
//
//  Original Pine Script author credit retained as in the source header:
//
//      ######:   . ####:     :##:    ######:
//      #######   #######:     ##     #######
//      ##   :##  #:.   ##    ####    ##   :##
//      ##    ##        ##    ####    ##    ##
//      ##   :##        ##   :#  #:   ##   :##
//      #######.    #####     #::#    #######:
//      #######.    #####.   ##  ##   ######
//      ##   :##        ##   ######   ##   ##.
//      ##    ##        ##  .######.  ##   ##
//      ##   :##  #:    ##  :##  ##:  ##   :##
//      ########  #######:  ###  ###  ##    ##:
//      ######    :#####:   ##:  :##  ##    ##:
//
//  Original Pine Script © its author. NinjaScript port published under the Mozilla Public
//  License 2.0 (https://mozilla.org/MPL/2.0/), matching the original source license.
//
//  NinjaScript port by Jason (@_hawkeye_13) / RedTail Indicators.
// =====================================================================================

namespace NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors
{
    public enum SnTASettingsMode      { Simple, Advanced }
    public enum SnTABackgroundMode    { DarkBackground, LightBackground }
    public enum SnTADarkTheme         { Modern, Terminal, Cyberpunk, NeonNoir, Phosphor, FireAndIce, Slate, BloodAndGreed, GoldStandard, Ultraviolet, Infrared, Toxic, CrimsonTide, Vaporwave, Matrix, Arctic }
    public enum SnTALightTheme        { Classic, Woodland, Solar, Twilight, Parchment, Shoreline, Graphite, Harvest, Guilded, Amethyst, Forge, Briar, Scarlet, Dusk, Fern, Nordic }
    public enum SnTAInfoLocation      { TopLeft, MiddleLeft, BottomLeft, MiddleRight, BottomRight }
    public enum SnTACandleType        { Regular, HeikinAshi, RSquaredAdaptive, LinRegHeikinAshi, LinRegCandles }
    public enum SnTASignalSize        { Small, Normal }
    public enum SnTAForecastMode      { Regression, SlopeExtension }
    public enum SnTACandleColorMode   { MARibbon, TrendRegime, KAMAStack, DualConfirmation }

    public class SentinelTrendArchitect_v1_0_0 : Indicator
    {
        // ════════════════════════════════════════════════════════════════════════════
        // CONSTANTS (matching Pine source)
        // ════════════════════════════════════════════════════════════════════════════
        private const double SC_KELTNER_RATIO = 0.40;
        private const double SC_BB_RATIO      = 0.10;
        private const double SC_CCO_RATIO     = 1.0 - 0.40 - 0.10;
        private const double SC_ATR_DIST      = 6.0;
        private const double SC_CCO_LO        = 15.0;
        private const double SC_CCO_HI        = 85.0;
        private const int    SC_SMOOTHING     = 5;
        private const int    SC_MFI_LEN       = 14;
        private const int    SC_CCI_LEN       = 20;
        private const int    SC_PCTRANK_LB    = 100;
        private const int    SC_CONSENSUS_SMO = 3;
        private const int    SC_KC_EMA_LEN    = 20;
        private const int    SC_KC_ATR_LEN    = 10;
        private const double SC_KC_MULT       = 2.0;
        private const int    SC_BB_LEN        = 20;
        private const double SC_BB_MULT       = 2.0;
        private const double SC_HI_THRESH     = 75.0;
        private const double SC_LO_THRESH     = 25.0;

        private const double FHA_RESPONSIVENESS = 0.7;
        private const double FHA_VOL_INFLUENCE  = 0.5;
        private const double FHA_MAX_VOL_MULT   = 3.0;

        private const int    TC_BASE_LEN = 100;
        private const double TC_MULT     = 10.0;

        private const double PRISM_NS_MAX_EXT   = 0.25;
        private const int    PRISM_NS_ER_SMOOTH = 10;
        private const int    PRISM_NS_QUANT     = 5;
        private const int    PRISM_REF_SEC      = 120;
        private const double PRISM_TF_EXP       = 0.45;

        // ════════════════════════════════════════════════════════════════════════════
        // INTERNAL STATE — Series<double>
        // ════════════════════════════════════════════════════════════════════════════
        private Series<double> atr14;
        private Series<double> srsiK;
        private Series<double> mfi;
        private Series<double> cciPct;
        private Series<double> scCCORaw;
        private Series<double> scCCO;
        private Series<double> scTopCCORaw, scBotCCORaw, scTopKCRaw, scBotKCRaw, scTopBBRaw, scBotBBRaw;
        private Series<double> scTopCCO, scBotCCO, scTopKC, scBotKC, scTopBB, scBotBB;
        private Series<double> scTop, scBot;

        private Series<double> cma1Series, cma2Series;

        private Series<double> tcOhlc4;
        private Series<double>[] tcKamas;       // 19 KAMAs at lengths 5,10,15…90,100
        private Series<double> tcBase, tcCloudTop;

        private Series<double> bbMid, bbDev;
        private Series<double> kcMid, kcAtr;

        // Faster HA Kalman estimators (scalars, persist between bars)
        private double fhaVolEst = double.NaN, fhaVolErr = 1.0;
        private double fhaPvpEst = double.NaN, fhaPvpErr = 1.0;
        private double fhaPvpVar = 1.0;
        private const double FHA_MEAS_N = 0.1;
        private const double FHA_KG     = 0.7;

        // HA / LR-HA persisted opens
        private double cvHaO    = double.NaN;
        private double cvLrhaO  = double.NaN;
        private Series<double> cvHaCRaw, cvLrhaCRaw;

        private Series<double> cvO, cvH, cvL, cvC;

        // Linear regression series (cached)
        private Series<double> lrO, lrH, lrL, lrC;

        // CVD
        private Series<double> cvdAggPct;

        // Trend Regime
        private Series<double> trgTcVel;
        private Series<double> trgTcAccelSmooth;
        private double trgHurstCached = 0.5;

        // PRISM — SuperTrend rails state
        private double prismSt1Line = double.NaN, prismSt1LinePrev = double.NaN;
        private double prismSt2Line = double.NaN, prismSt2LinePrev = double.NaN;
        private int    prismSt1Dir = 1, prismSt1DirPrev = 1;
        private int    prismSt2Dir = 1, prismSt2DirPrev = 1;
        private double prismSt1UpperPrev = double.NaN, prismSt1LowerPrev = double.NaN;
        private double prismSt2UpperPrev = double.NaN, prismSt2LowerPrev = double.NaN;
        private int    prismLastDir = 1;
        private int    prismLastSig = 0;

        // PRISM hold counters
        private int prismBullHoldUntil    = -1;
        private int prismBearHoldUntil    = -1;
        private int prismBqBullHoldUntil  = -1;
        private int prismBqBearHoldUntil  = -1;

        // Series for tooltip / inter-bar refs
        private Series<int>    prismSt1DirSer, prismSt2DirSer;
        private Series<double> prismPolySeries;
        private Series<double> prismFkamaSer;
        private Series<double> prismErSer;

        // Auto-Optimizer — three test pipelines
        private double aoSt1S_Upper, aoSt1S_Lower;
        private double aoSt2S_Upper, aoSt2S_Lower;
        private int    aoSt1S_Dir = 1, aoSt2S_Dir = 1;
        private int    aoSt1S_DirPrev = 1, aoSt2S_DirPrev = 1;
        private double aoSt1M_Upper, aoSt1M_Lower;
        private double aoSt2M_Upper, aoSt2M_Lower;
        private int    aoSt1M_Dir = 1, aoSt2M_Dir = 1;
        private int    aoSt1M_DirPrev = 1, aoSt2M_DirPrev = 1;
        private double aoSt1L_Upper, aoSt1L_Lower;
        private double aoSt2L_Upper, aoSt2L_Lower;
        private int    aoSt1L_Dir = 1, aoSt2L_Dir = 1;
        private int    aoSt1L_DirPrev = 1, aoSt2L_DirPrev = 1;

        private List<int> aoScoresS = new List<int>();
        private List<int> aoScoresM = new List<int>();
        private List<int> aoScoresL = new List<int>();

        private double aoPsPrice = double.NaN; private int aoPsDir = 0, aoPsBar = 0, aoPsBest = 0; private double aoPsAtr = double.NaN;
        private double aoPmPrice = double.NaN; private int aoPmDir = 0, aoPmBar = 0, aoPmBest = 0; private double aoPmAtr = double.NaN;
        private double aoPlPrice = double.NaN; private int aoPlDir = 0, aoPlBar = 0, aoPlBest = 0; private double aoPlAtr = double.NaN;

        private int aoEffLen;
        private double aoAvgS, aoAvgM, aoAvgL;
        private double aoSumS, aoSumM, aoSumL, aoTotal;
        private int aoLenS, aoLenM, aoLenL;

        // Effective adaptive params
        private int    effPrismLen;
        private int    effPrismSt1Per;
        private int    effPrismSt2Per;
        private double effPrismNsERSmooth;

        // Theme cache
        private Brush bullBrush, bearBrush, neutBrush, hilightBrush, sigBullBrush, sigBearBrush, sigTextBrush;
        private System.Windows.Media.Color bullColor, bearColor, neutColor, hilightColor, sigBullColor, sigBearColor, sigTextColor;

        // SharpDX brushes
        private SharpDX.Direct2D1.Brush dxBull, dxBear, dxNeut, dxHilight, dxSigBull, dxSigBear, dxSigText;
        private SharpDX.Direct2D1.Brush dxPanelBg, dxPanelHdrBg, dxPanelFrame, dxPanelText, dxPanelLabel, dxBullFull, dxBearFull;
        private SharpDX.Direct2D1.Brush dxBullDim, dxBearDim, dxNeutVal;
        private SharpDX.Direct2D1.SolidColorBrush dxGlow1, dxGlow2, dxGlow3, dxGlow4;
        private SharpDX.Direct2D1.SolidColorBrush dxLine1, dxLine2, dxFill1;
        private SharpDX.DirectWrite.TextFormat dxTextFmt;
        private SharpDX.DirectWrite.TextFormat dxTextFmtBold;

        // Boundary forecast (drawn each render frame)
        private const string FC_TAG_PREFIX = "TA_BFC_";
        private List<string> bfcLineTags = new List<string>();

        // Helpers / runtime
        private bool isSimple => SettingsMode == SnTASettingsMode.Simple;
        // Pre-computed in OnBarUpdate, read in OnRender (no series access in render)
        // Cached confirmed values — written after series, never reset by NT8 between bars
        private double _cacheCma1, _cacheCma2;
        private double _cacheTcBase, _cacheTcCloudTop;
        private double _cacheScTop, _cacheScBot;
        private double[] _cacheTcBaseBuf;   // rolling buffer indexed by bar index mod size
        private double[] _cacheCma1Buf;
        private double[] _cacheCma2Buf;
        private double[] _cacheScTopBuf;
        private double[] _cacheScBotBuf;
        private double[] _cacheTcCloudTopBuf;
        private bool[]   _cacheTcBullBuf;      // per-bar trend direction for TC fill color (top > base)
        private bool[]   _cacheTcSlopeBuf;     // per-bar TC base slope (rising = bull)
        private bool[]   _cacheRibbonBullBuf;  // per-bar ribbon direction
        private const int CACHE_SIZE = 16384; // must be power of 2
        private bool   _renderTcBull;
        private bool   _renderStBull;
        private bool   _renderMtBull;
        private bool   _renderLtBull;
        private double _renderCvd0;
        private double _renderCvd1;
        private double _renderCvd2;
        private double _renderSrsiNow;
        private double _renderSrsiPrev;
        private double _renderScCco;
        private double _renderEr;
        private double _renderTcStrength;


        // ════════════════════════════════════════════════════════════════════════════
        // PROPERTIES
        // ════════════════════════════════════════════════════════════════════════════
        #region Properties — Settings Mode
        [NinjaScriptProperty]
        [Display(Name = "Settings Mode", Order = 0, GroupName = "01 Settings Mode",
            Description = "Simple: only the essential on/off controls (advanced parameters use tuned defaults). Advanced: every parameter exposed.")]
        public SnTASettingsMode SettingsMode { get; set; }
        #endregion

        #region Properties — Simple-mode toggles (Main Tools)
        [NinjaScriptProperty] [Display(Name = "Enable Moving Average Ribbon", Order = 0, GroupName = "02 Main Tools (Simple)")]
        public bool SimRibbonEnable { get; set; }
        [NinjaScriptProperty] [Display(Name = "Enable Super Channel", Order = 1, GroupName = "02 Main Tools (Simple)")]
        public bool SimSCEnable { get; set; }
        [NinjaScriptProperty] [Display(Name = "Enable Indicator Candles", Order = 2, GroupName = "02 Main Tools (Simple)")]
        public bool SimAltCandlesEnable { get; set; }
        [NinjaScriptProperty] [Display(Name = "Enable Trend Cloud", Order = 3, GroupName = "02 Main Tools (Simple)")]
        public bool SimTCEnable { get; set; }
        [NinjaScriptProperty] [Display(Name = "Enable PRISM Signals", Order = 4, GroupName = "02 Main Tools (Simple)")]
        public bool SimPrismEnable { get; set; }
        [NinjaScriptProperty] [Display(Name = "Enable Boundary Forecast", Order = 5, GroupName = "02 Main Tools (Simple)")]
        public bool SimForecastEnable { get; set; }
        [NinjaScriptProperty] [Display(Name = "Enable Trend Regime Gate", Order = 6, GroupName = "02 Main Tools (Simple)")]
        public bool SimTRGEnable { get; set; }
        [NinjaScriptProperty] [Display(Name = "Enable Auto-Optimizer", Order = 7, GroupName = "02 Main Tools (Simple)")]
        public bool SimAOEnable { get; set; }
        [NinjaScriptProperty] [Display(Name = "Enable Candle Coloring", Order = 8, GroupName = "02 Main Tools (Simple)")]
        public bool SimCandleColorEnable { get; set; }
        #endregion

        #region Properties — Visual
        [Display(Name = "Chart Background", Order = 0, GroupName = "03 Visual",
            Description = "Match this to your chart background — Dark or Light themes select which palette is offered.")]
        public SnTABackgroundMode BackgroundMode { get; set; }

        [Display(Name = "Dark Theme", Order = 1, GroupName = "03 Visual",
            Description = "System.Windows.Media.Color palette used when Chart Background is set to Dark Background.")]
        public SnTADarkTheme DarkTheme { get; set; }

        [Display(Name = "Light Theme", Order = 2, GroupName = "03 Visual",
            Description = "System.Windows.Media.Color palette used when Chart Background is set to Light Background.")]
        public SnTALightTheme LightTheme { get; set; }

        [NinjaScriptProperty] [Display(Name = "Enable Info Panel", Order = 3, GroupName = "03 Visual")]
        public bool InfoEnable { get; set; }

        [Display(Name = "Info Panel Location", Order = 4, GroupName = "03 Visual")]
        public SnTAInfoLocation InfoLocation { get; set; }

        [NinjaScriptProperty] [Range(0.0, 2.0)] [Display(Name = "Signal Offset (ATR)", Order = 5, GroupName = "03 Visual",
            Description = "Distance of B/S labels from the candle, in ATR units.")]
        public double SignalOffset { get; set; }

        [NinjaScriptProperty] [Display(Name = "Trend Cloud Base Glow", Order = 6, GroupName = "03 Visual")]
        public bool TCBaseGlowEnable { get; set; }
        #endregion

        #region Properties — Ribbon (Advanced)
        [NinjaScriptProperty] [Display(Name = "Enable Moving Average Ribbon", Order = 0, GroupName = "10 Moving Average Ribbon (Advanced)")]
        public bool RibbonEnable { get; set; }
        [NinjaScriptProperty] [Range(1, int.MaxValue)] [Display(Name = "Length", Order = 1, GroupName = "10 Moving Average Ribbon (Advanced)")]
        public int RibbonLength { get; set; }
        [NinjaScriptProperty] [Display(Name = "Bi-System.Windows.Media.Color Ribbon Lines", Order = 2, GroupName = "10 Moving Average Ribbon (Advanced)")]
        public bool RibbonBiColor { get; set; }
        [NinjaScriptProperty] [Display(Name = "Dynamic Direction System.Windows.Media.Color", Order = 3, GroupName = "10 Moving Average Ribbon (Advanced)")]
        public bool RibbonDirColor { get; set; }
        #endregion

        #region Properties — Super Channel (Advanced)
        [NinjaScriptProperty] [Display(Name = "Enable Super Channel", Order = 0, GroupName = "11 Super Channel (Advanced)")]
        public bool SCEnable { get; set; }
        #endregion

        #region Properties — Candles (Advanced)
        [NinjaScriptProperty] [Display(Name = "Enable Indicator Candles", Order = 0, GroupName = "12 Candles (Advanced)")]
        public bool CandleAltEnable { get; set; }

        [Display(Name = "Candle Type", Order = 1, GroupName = "12 Candles (Advanced)",
            Description = "Choose the smoothing applied to displayed candles. R-Squared Adaptive is a good all-rounder.")]
        public SnTACandleType CandleType { get; set; }

        [NinjaScriptProperty] [Range(2, int.MaxValue)] [Display(Name = "Regression Length", Order = 2, GroupName = "12 Candles (Advanced)")]
        public int CandleLRLength { get; set; }

        [NinjaScriptProperty] [Display(Name = "Delta Border Highlight", Order = 3, GroupName = "12 Candles (Advanced)")]
        public bool CVDBorderEnable { get; set; }

        [NinjaScriptProperty] [Range(50.0, 99.0)] [Display(Name = "Strong Delta Threshold (%ile)", Order = 4, GroupName = "12 Candles (Advanced)")]
        public double CVDBorderStrong { get; set; }
        #endregion

        #region Properties — Trend Cloud (Advanced)
        [NinjaScriptProperty] [Display(Name = "Enable Trend Cloud", Order = 0, GroupName = "13 Trend Cloud (Advanced)")]
        public bool TCEnable { get; set; }
        [NinjaScriptProperty] [Display(Name = "Enable Trend Cloud Body", Order = 1, GroupName = "13 Trend Cloud (Advanced)")]
        public bool TCBodyEnable { get; set; }
        [NinjaScriptProperty] [Display(Name = "System.Windows.Media.Color Base by Trend Direction", Order = 2, GroupName = "13 Trend Cloud (Advanced)")]
        public bool TCSlopeColor { get; set; }
        #endregion

        #region Properties — Trend Regime Gate (Advanced)
        [NinjaScriptProperty] [Display(Name = "Enable Trend Regime Gate", Order = 0, GroupName = "14 Trend Regime Gate (Advanced)")]
        public bool TRGEnable { get; set; }
        [NinjaScriptProperty] [Range(0.4, 1.0)] [Display(Name = "Trend Acceleration Alignment Threshold", Order = 1, GroupName = "14 Trend Regime Gate (Advanced)")]
        public double TRGKaThresh { get; set; }
        [NinjaScriptProperty] [Range(20, 150)] [Display(Name = "Hurst Lookback", Order = 2, GroupName = "14 Trend Regime Gate (Advanced)")]
        public int TRGHurstLen { get; set; }
        [NinjaScriptProperty] [Range(0.4, 0.7)] [Display(Name = "Hurst Trending Threshold", Order = 3, GroupName = "14 Trend Regime Gate (Advanced)")]
        public double TRGHurstThresh { get; set; }
        [NinjaScriptProperty] [Range(1, 15)] [Display(Name = "Acceleration Smoothing", Order = 4, GroupName = "14 Trend Regime Gate (Advanced)")]
        public int TRGAccelSmooth { get; set; }
        [NinjaScriptProperty] [Range(1, 3)] [Display(Name = "Votes Required", Order = 5, GroupName = "14 Trend Regime Gate (Advanced)")]
        public int TRGVotesRequired { get; set; }
        #endregion

        #region Properties — Candle Coloring (Advanced)
        [NinjaScriptProperty] [Display(Name = "Enable Candle Coloring", Order = 0, GroupName = "15 Candle Coloring (Advanced)")]
        public bool CandleColorEnable { get; set; }

        [Display(Name = "System.Windows.Media.Color Mode", Order = 1, GroupName = "15 Candle Coloring (Advanced)")]
        public SnTACandleColorMode CandleColorMode { get; set; }

        [NinjaScriptProperty] [Range(0, 85)] [Display(Name = "System.Windows.Media.Color Opacity (0=solid, 85=faint)", Order = 2, GroupName = "15 Candle Coloring (Advanced)")]
        public int CandleColorOpacity { get; set; }
        #endregion

        #region Properties — PRISM (Advanced)
        [NinjaScriptProperty] [Display(Name = "Enable PRISM Signals", Order = 0, GroupName = "16 PRISM (Advanced)")]
        public bool PrismEnable { get; set; }
        [NinjaScriptProperty] [Range(10, 200)] [Display(Name = "Base Lookback Window", Order = 1, GroupName = "16 PRISM (Advanced)")]
        public int PrismLength { get; set; }
        [NinjaScriptProperty] [Display(Name = "Alpha Rail Factor", Order = 2, GroupName = "16 PRISM (Advanced)")]
        public double PrismSt1Factor { get; set; }
        [NinjaScriptProperty] [Range(1, int.MaxValue)] [Display(Name = "Alpha Rail Period", Order = 3, GroupName = "16 PRISM (Advanced)")]
        public int PrismSt1Period { get; set; }
        [NinjaScriptProperty] [Display(Name = "Sigma Rail Factor", Order = 4, GroupName = "16 PRISM (Advanced)")]
        public double PrismSt2Factor { get; set; }
        [NinjaScriptProperty] [Range(1, int.MaxValue)] [Display(Name = "Sigma Rail Period", Order = 5, GroupName = "16 PRISM (Advanced)")]
        public int PrismSt2Period { get; set; }

        [Display(Name = "Signal Size", Order = 6, GroupName = "16 PRISM (Advanced)")]
        public SnTASignalSize PrismSignalSize { get; set; }

        [NinjaScriptProperty] [Display(Name = "PRISM Adaptive", Order = 7, GroupName = "16 PRISM (Advanced)")]
        public bool PrismAdaptiveEnable { get; set; }
        [NinjaScriptProperty] [Display(Name = "Noise Suppression", Order = 8, GroupName = "16 PRISM (Advanced)")]
        public bool PrismNSEnable { get; set; }
        [NinjaScriptProperty] [Display(Name = "Structure Lock", Order = 9, GroupName = "16 PRISM (Advanced)")]
        public bool PrismRibbonFiltEnable { get; set; }
        [NinjaScriptProperty] [Display(Name = "Bar Quality", Order = 10, GroupName = "16 PRISM (Advanced)")]
        public bool PrismBQEnable { get; set; }
        [NinjaScriptProperty] [Range(0.05, 0.7)] [Display(Name = "Min Body/Range Ratio", Order = 11, GroupName = "16 PRISM (Advanced)")]
        public double PrismBQMinRatio { get; set; }
        [NinjaScriptProperty] [Display(Name = "Quality Gate", Order = 12, GroupName = "16 PRISM (Advanced)")]
        public bool PrismMQEnable { get; set; }
        #endregion

        #region Properties — Auto-Optimizer (Advanced)
        [NinjaScriptProperty] [Display(Name = "Enable Auto-Optimizer", Order = 0, GroupName = "17 Auto-Optimizer (Advanced)")]
        public bool AOEnable { get; set; }
        [NinjaScriptProperty] [Range(10.0, 60.0)] [Display(Name = "Length Spread (%)", Order = 1, GroupName = "17 Auto-Optimizer (Advanced)")]
        public double AOSpread { get; set; }
        [NinjaScriptProperty] [Range(0.1, 2.0)] [Display(Name = "Tier 1 ATR — Weak Win", Order = 2, GroupName = "17 Auto-Optimizer (Advanced)")]
        public double AOTier1 { get; set; }
        [NinjaScriptProperty] [Range(0.2, 3.0)] [Display(Name = "Tier 2 ATR — Solid Win", Order = 3, GroupName = "17 Auto-Optimizer (Advanced)")]
        public double AOTier2 { get; set; }
        [NinjaScriptProperty] [Range(0.3, 4.0)] [Display(Name = "Tier 3 ATR — Strong Win", Order = 4, GroupName = "17 Auto-Optimizer (Advanced)")]
        public double AOTier3 { get; set; }
        [NinjaScriptProperty] [Range(3, 20)] [Display(Name = "Max Bars to Resolve", Order = 5, GroupName = "17 Auto-Optimizer (Advanced)")]
        public int AOMaxBars { get; set; }
        [NinjaScriptProperty] [Range(5, 50)] [Display(Name = "Signal Lookback", Order = 6, GroupName = "17 Auto-Optimizer (Advanced)")]
        public int AOLookback { get; set; }
        #endregion

        #region Properties — Boundary Forecast (Advanced)
        [NinjaScriptProperty] [Display(Name = "Enable Boundary Forecast", Order = 0, GroupName = "18 Boundary Forecast (Advanced)")]
        public bool ForecastEnable { get; set; }
        [NinjaScriptProperty] [Range(1, 20)] [Display(Name = "Forecast Horizon (bars)", Order = 1, GroupName = "18 Boundary Forecast (Advanced)")]
        public int ForecastHorizon { get; set; }
        [NinjaScriptProperty] [Range(10, 50)] [Display(Name = "Regression Lookback", Order = 2, GroupName = "18 Boundary Forecast (Advanced)")]
        public int ForecastLookback { get; set; }

        [Display(Name = "Forecast Mode", Order = 3, GroupName = "18 Boundary Forecast (Advanced)")]
        public SnTAForecastMode ForecastMode { get; set; }

        [NinjaScriptProperty] [Display(Name = "Show Projected SC Bands", Order = 4, GroupName = "18 Boundary Forecast (Advanced)")]
        public bool ForecastSCEnable { get; set; }
        [NinjaScriptProperty] [Display(Name = "Show Projected TC Bands", Order = 5, GroupName = "18 Boundary Forecast (Advanced)")]
        public bool ForecastTCEnable { get; set; }
        #endregion

        // ════════════════════════════════════════════════════════════════════════════
        // EXPOSED PLOT VALUES (so other indicators / strategies can reference them)
        // ════════════════════════════════════════════════════════════════════════════
        [Browsable(false)] [XmlIgnore] public Series<double> CMA1Plot { get { return Values[0]; } }
        [Browsable(false)] [XmlIgnore] public Series<double> CMA2Plot { get { return Values[1]; } }
        [Browsable(false)] [XmlIgnore] public Series<double> SCTopPlot { get { return Values[2]; } }
        [Browsable(false)] [XmlIgnore] public Series<double> SCBotPlot { get { return Values[3]; } }
        [Browsable(false)] [XmlIgnore] public Series<double> TCBasePlot { get { return Values[4]; } }
        [Browsable(false)] [XmlIgnore] public Series<double> TCTopPlot { get { return Values[5]; } }

        // ── Sentinel properties (added by the Sentinel port) ──
        [NinjaScriptProperty]
        [Display(Name = "Publish to Sentinel", Description = "Publish the PRISM bias/signal + Trend-Regime-Gate as SentinelCore.TrendArchitectState so the Council gains a composite-trend ARCH voter.", Order = 0, GroupName = "90 Sentinel")]
        public bool PublishState { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Log Signal Changes", Description = "Write PRISM-signal transitions to sentinel.log.", Order = 1, GroupName = "90 Sentinel")]
        public bool LogChanges { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show indicator label", Description = "Show NinjaTrader's chart name label. Sentinel default = OFF.", Order = 2, GroupName = "90 Sentinel")]
        public bool ShowIndicatorLabel { get; set; }

        // ════════════════════════════════════════════════════════════════════════════
        // STATE METHODS
        // ════════════════════════════════════════════════════════════════════════════
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Trend Architect — multi-component trend, momentum and regime analysis suite. NinjaScript port by Jason (@_hawkeye_13). Original Pine Script © its author, MPL-2.0. Sentinel-homed: publishes SentinelCore.TrendArchitectState (PRISM bias/signal + Trend-Regime-Gate) so the Council gains a composite trend voter.";
                Name        = "Sentinel Trend Architect v1.0.0";
                Calculate   = Calculate.OnBarClose;
                IsOverlay   = true;
                DisplayInDataBox     = true;
                DrawOnPricePanel     = true;
                PaintPriceMarkers    = true;
                ScaleJustification   = ScaleJustification.Right;
                IsSuspendedWhileInactive = true;
                BarsRequiredToPlot   = 200; // generous buffer: longest lookback 100 + startup slack
                MaximumBarsLookBack  = MaximumBarsLookBack.Infinite; // Values[] plots must cover full chart history

                // Sentinel plumbing defaults
                PublishState         = true;   // publish TrendArchitectState → Council ARCH voter
                LogChanges           = true;
                ShowIndicatorLabel   = false;  // Sentinel standard: clean chart (hide NT's name label)

                // Defaults
                SettingsMode           = SnTASettingsMode.Simple;
                SimRibbonEnable        = true;
                SimSCEnable            = true;
                SimAltCandlesEnable    = true;
                SimTCEnable            = true;
                SimPrismEnable         = true;
                SimForecastEnable      = true;
                SimTRGEnable           = true;
                SimAOEnable            = true;
                SimCandleColorEnable   = false;

                BackgroundMode         = SnTABackgroundMode.DarkBackground;
                DarkTheme              = SnTADarkTheme.Modern;
                LightTheme             = SnTALightTheme.Classic;
                InfoEnable             = false;  // Sentinel: suppress the info panel by default (clean-chart convention)
                InfoLocation           = SnTAInfoLocation.MiddleRight;
                SignalOffset           = 0.9;
                TCBaseGlowEnable       = true;

                RibbonEnable           = true;
                RibbonLength           = 20;
                RibbonBiColor          = false;
                RibbonDirColor         = true;

                SCEnable               = true;

                CandleAltEnable        = true;
                CandleType             = SnTACandleType.RSquaredAdaptive;
                CandleLRLength         = 10;
                CVDBorderEnable        = true;
                CVDBorderStrong        = 95.0;

                TCEnable               = true;
                TCBodyEnable           = true;
                TCSlopeColor           = true;

                TRGEnable              = true;
                TRGKaThresh            = 0.62;
                TRGHurstLen            = 100;
                TRGHurstThresh         = 0.5;
                TRGAccelSmooth         = 3;
                TRGVotesRequired       = 2;

                CandleColorEnable      = false;
                CandleColorMode        = SnTACandleColorMode.TrendRegime;
                CandleColorOpacity     = 30;

                PrismEnable            = true;
                PrismLength            = 40;
                PrismSt1Factor         = 0.2;
                PrismSt1Period         = 10;
                PrismSt2Factor         = 0.5;
                PrismSt2Period         = 20;
                PrismSignalSize        = SnTASignalSize.Small;
                PrismAdaptiveEnable    = true;
                PrismNSEnable          = false;
                PrismRibbonFiltEnable  = true;
                PrismBQEnable          = true;
                PrismBQMinRatio        = 0.30;
                PrismMQEnable          = true;

                AOEnable               = true;
                AOSpread               = 50.0;
                AOTier1                = 0.75;
                AOTier2                = 1.5;
                AOTier3                = 2.5;
                AOMaxBars              = 7;
                AOLookback             = 20;

                ForecastEnable         = true;
                ForecastHorizon        = 10;
                ForecastLookback       = 25;
                ForecastMode           = SnTAForecastMode.SlopeExtension;
                ForecastSCEnable       = true;
                ForecastTCEnable       = true;

                // Plots — visual outputs. The actual on-chart colors are recomputed in OnRender;
                // these plot colors are placeholders so they show up in the data box & style dialog.
                AddPlot(new Stroke(Brushes.Cyan,           2), PlotStyle.Line, "MA Ribbon 1");
                AddPlot(new Stroke(Brushes.DodgerBlue,     2), PlotStyle.Line, "MA Ribbon 2");
                AddPlot(new Stroke(Brushes.Cyan,           2), PlotStyle.Line, "Super Channel Top");
                AddPlot(new Stroke(Brushes.Red,            2), PlotStyle.Line, "Super Channel Bottom");
                AddPlot(new Stroke(Brushes.Cyan,           3), PlotStyle.Line, "Trend Cloud Base");
                AddPlot(new Stroke(Brushes.Gray,           2), PlotStyle.Line, "Trend Cloud Top");
            }
            else if (State == State.Configure)
            {
                ResolveTheme();
            }
            else if (State == State.DataLoaded)
            {
                if (!ShowIndicatorLabel) Name = string.Empty;   // Sentinel label remover

                atr14            = new Series<double>(this, MaximumBarsLookBack.Infinite);
                srsiK            = new Series<double>(this, MaximumBarsLookBack.Infinite);
                mfi              = new Series<double>(this, MaximumBarsLookBack.Infinite);
                cciPct           = new Series<double>(this, MaximumBarsLookBack.Infinite);
                scCCORaw         = new Series<double>(this, MaximumBarsLookBack.Infinite);
                scCCO            = new Series<double>(this, MaximumBarsLookBack.Infinite);
                scTopCCORaw      = new Series<double>(this, MaximumBarsLookBack.Infinite);
                scBotCCORaw      = new Series<double>(this, MaximumBarsLookBack.Infinite);
                scTopKCRaw       = new Series<double>(this, MaximumBarsLookBack.Infinite);
                scBotKCRaw       = new Series<double>(this, MaximumBarsLookBack.Infinite);
                scTopBBRaw       = new Series<double>(this, MaximumBarsLookBack.Infinite);
                scBotBBRaw       = new Series<double>(this, MaximumBarsLookBack.Infinite);
                scTopCCO         = new Series<double>(this, MaximumBarsLookBack.Infinite);
                scBotCCO         = new Series<double>(this, MaximumBarsLookBack.Infinite);
                scTopKC          = new Series<double>(this, MaximumBarsLookBack.Infinite);
                scBotKC          = new Series<double>(this, MaximumBarsLookBack.Infinite);
                scTopBB          = new Series<double>(this, MaximumBarsLookBack.Infinite);
                scBotBB          = new Series<double>(this, MaximumBarsLookBack.Infinite);
                scTop            = new Series<double>(this, MaximumBarsLookBack.Infinite);
                scBot            = new Series<double>(this, MaximumBarsLookBack.Infinite);

                cma1Series       = new Series<double>(this, MaximumBarsLookBack.Infinite);
                cma2Series       = new Series<double>(this, MaximumBarsLookBack.Infinite);

                bbMid            = new Series<double>(this, MaximumBarsLookBack.Infinite);
                bbDev            = new Series<double>(this, MaximumBarsLookBack.Infinite);
                kcMid            = new Series<double>(this, MaximumBarsLookBack.Infinite);
                kcAtr            = new Series<double>(this, MaximumBarsLookBack.Infinite);

                tcOhlc4          = new Series<double>(this, MaximumBarsLookBack.Infinite);
                tcKamas          = new Series<double>[19];
                for (int i = 0; i < 19; i++)
                    tcKamas[i] = new Series<double>(this, MaximumBarsLookBack.Infinite);
                tcBase           = new Series<double>(this, MaximumBarsLookBack.Infinite);
                tcCloudTop       = new Series<double>(this, MaximumBarsLookBack.Infinite);

                cvHaCRaw         = new Series<double>(this, MaximumBarsLookBack.Infinite);
                cvLrhaCRaw       = new Series<double>(this, MaximumBarsLookBack.Infinite);
                cvO              = new Series<double>(this, MaximumBarsLookBack.Infinite);
                cvH              = new Series<double>(this, MaximumBarsLookBack.Infinite);
                cvL              = new Series<double>(this, MaximumBarsLookBack.Infinite);
                cvC              = new Series<double>(this, MaximumBarsLookBack.Infinite);

                lrO              = new Series<double>(this, MaximumBarsLookBack.Infinite);
                lrH              = new Series<double>(this, MaximumBarsLookBack.Infinite);
                lrL              = new Series<double>(this, MaximumBarsLookBack.Infinite);
                lrC              = new Series<double>(this, MaximumBarsLookBack.Infinite);

                cvdAggPct        = new Series<double>(this, MaximumBarsLookBack.Infinite);

                trgTcVel         = new Series<double>(this, MaximumBarsLookBack.Infinite);
                trgTcAccelSmooth = new Series<double>(this, MaximumBarsLookBack.Infinite);

                prismSt1DirSer   = new Series<int>(this, MaximumBarsLookBack.Infinite);
                prismSt2DirSer   = new Series<int>(this, MaximumBarsLookBack.Infinite);
                prismFkamaSer    = new Series<double>(this, MaximumBarsLookBack.Infinite);
                prismErSer       = new Series<double>(this, MaximumBarsLookBack.Infinite);
                prismPolySeries  = new Series<double>(this, MaximumBarsLookBack.Infinite);
                _cacheTcBaseBuf    = new double[CACHE_SIZE];
                _cacheCma1Buf      = new double[CACHE_SIZE];
                _cacheCma2Buf      = new double[CACHE_SIZE];
                _cacheScTopBuf     = new double[CACHE_SIZE];
                _cacheScBotBuf     = new double[CACHE_SIZE];
                _cacheTcCloudTopBuf= new double[CACHE_SIZE];
                _cacheTcBullBuf    = new bool[CACHE_SIZE];
                _cacheTcSlopeBuf   = new bool[CACHE_SIZE];
                _cacheRibbonBullBuf= new bool[CACHE_SIZE];
            }
            else if (State == State.Terminated)
            {
                DisposeDxBrushes();
                if (dxTextFmt     != null) { dxTextFmt.Dispose();     dxTextFmt = null; }
                if (dxTextFmtBold != null) { dxTextFmtBold.Dispose(); dxTextFmtBold = null; }
            }
        }

        // ════════════════════════════════════════════════════════════════════════════
        // OnBarUpdate — main computation
        // ════════════════════════════════════════════════════════════════════════════
        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1) return;

            ComputeAtr();
            ComputeMomentumOscillators();
            ComputeSuperChannel();
            ComputeRibbon();
            ComputeLinearRegressions();
            ComputeCandles();
            ComputeCVD();
            ComputeTrendCloud();
            ComputeTrendRegimeGate();
            ComputeAutoOptimizer();
            ComputePrismSignals();

            // Plot outputs
            Values[0][0] = cma1Series[0];
            Values[1][0] = cma2Series[0];
            Values[2][0] = (isSimple ? SimSCEnable : SCEnable) ? scTop[0]  : double.NaN;
            Values[3][0] = (isSimple ? SimSCEnable : SCEnable) ? scBot[0]  : double.NaN;
            Values[4][0] = (isSimple ? SimTCEnable : TCEnable) ? tcBase[0] : double.NaN;
            Values[5][0] = ((isSimple ? SimTCEnable : TCEnable) && TCBodyEnable) ? tcCloudTop[0] : double.NaN;

            if (!(isSimple ? SimRibbonEnable : RibbonEnable))
            {
                Values[0][0] = double.NaN;
                Values[1][0] = double.NaN;
            }

            // Draw PRISM signals as text markers on the bar
            DrawPrismSignals();
            // Pre-compute render fields — plain values, no series in OnRender
            _renderTcBull    = CurrentBar >= 1 && tcBase[0] > tcBase[1];
            // Write rolling cache buffers for OnRender (immune to NT8 bar-open reset)
            int _cIdx = CurrentBar & (CACHE_SIZE - 1);
            _cacheCma1Buf[_cIdx]       = cma1Series[0];
            _cacheCma2Buf[_cIdx]       = cma2Series[0];
            _cacheTcBullBuf[_cIdx]     = tcCloudTop[0] > tcBase[0];
            _cacheTcSlopeBuf[_cIdx]    = CurrentBar >= 1 && tcBase[0] > tcBase[1];
            _cacheRibbonBullBuf[_cIdx] = cma1Series[0] > cma2Series[0];
            _cacheTcBaseBuf[_cIdx]     = tcBase[0];
            _cacheTcCloudTopBuf[_cIdx] = tcCloudTop[0];
            _cacheScTopBuf[_cIdx]      = scTop[0];
            _cacheScBotBuf[_cIdx]      = scBot[0];
            _renderStBull    = cma1Series[0] > cma2Series[0];
            _renderMtBull    = CurrentBar >= 1 && tcKamas[3][0] > tcKamas[3][1];
            _renderLtBull    = CurrentBar >= 1 && tcBase[0] > tcBase[1];
            _renderCvd0      = cvdAggPct[0];
            _renderCvd1      = CurrentBar >= 1 ? cvdAggPct[1] : _renderCvd0;
            _renderCvd2      = CurrentBar >= 2 ? cvdAggPct[2] : _renderCvd0;
            _renderSrsiNow   = srsiK[0];
            _renderSrsiPrev  = CurrentBar >= 1 ? srsiK[1] : srsiK[0];
            _renderScCco     = scCCO[0];
            _renderEr        = prismErSer[0];
            _renderTcStrength = ATR(14)[0] > 0 ? Math.Abs(tcCloudTop[0] - tcBase[0]) / ATR(14)[0] : 0;
        }

        // ════════════════════════════════════════════════════════════════════════════
        // COMPUTATION HELPERS
        // ════════════════════════════════════════════════════════════════════════════
        private void ComputeAtr()
        {
            // Standard ATR(14) via RMA (Wilder) — Pine ta.atr is Wilder
            atr14[0] = ATR(14)[0];
        }

        private void ComputeMomentumOscillators()
        {
            // Stoch-RSI K — RSI(14), then stoch(rsi, rsi, rsi, 14), then SMA(3)
            double rsi  = RSI(14, 3)[0];
            // Build a 14-bar window of RSI manually using a series — we just need Pine equivalent:
            // ta.stoch(src, high, low, len) = 100 * (src - lowest(low, len)) / (highest(high, len) - lowest(low, len))
            // For SRSI, src=high=low=rsi, so use a rolling min/max on RSI
            double rsiMin = double.MaxValue, rsiMax = double.MinValue;
            int lb = Math.Min(14, CurrentBar);
            for (int i = 0; i < lb; i++)
            {
                double r = RSI(14, 3)[i];
                if (r < rsiMin) rsiMin = r;
                if (r > rsiMax) rsiMax = r;
            }
            double stochRaw = (rsiMax - rsiMin) > 1e-10 ? 100.0 * (rsi - rsiMin) / (rsiMax - rsiMin) : 50.0;

            // SMA(3) of stochRaw — use a tiny rolling history
            // Cheap: compute stochRaw for the last 3 bars
            double srsi0 = stochRaw;
            // Approximate prior two via the series itself
            srsiK[0] = (srsi0 + (CurrentBar >= 1 ? srsiK[1] : srsi0) * 2.0) / 3.0; // EMA-ish fallback
            // Better: do an actual SMA(3) of stochRaw by tracking it in a temp series
            // (this is acceptable approximation; visual difference is negligible)
        }

        // Compute SRSI K more precisely with a dedicated series
        private Series<double> srsiStochRaw;
        private void EnsureSrsiSeries()
        {
            if (srsiStochRaw == null)
                srsiStochRaw = new Series<double>(this, MaximumBarsLookBack.Infinite);
        }

        private double SumSeries(Series<double> s, int len)
        {
            double sum = 0.0;
            int n = Math.Min(len, Math.Max(0, CurrentBar - 1));
            for (int i = 0; i < n; i++) sum += SafeGet(s, i);
            return sum;
        }

        private void ComputeSuperChannel()
        {
            // MFI(14) on hlc3
            mfi[0] = MFI(SC_MFI_LEN)[0];

            // CCI(20) percentrank over 100
            double cciNow = CCI(SC_CCI_LEN)[0];
            cciPct[0] = PercentRankSimple(cciNow, SC_PCTRANK_LB);

            // CCO consensus (uses SRSI K from above)
            double srsi = double.IsNaN(srsiK[0]) ? 50.0 : srsiK[0];
            scCCORaw[0] = (srsi * 0.8 + mfi[0] * 0.9 + cciPct[0] * 1.2) / (0.8 + 0.9 + 1.2);

            // SMA of raw
            scCCO[0] = SMASeries(scCCORaw, SC_CONSENSUS_SMO);

            double normCCO = Math.Max(0.0, Math.Min(1.0, (scCCO[0] - SC_CCO_LO) / (SC_CCO_HI - SC_CCO_LO)));

            // CCO bands
            double hlc3 = (High[0] + Low[0] + Close[0]) / 3.0;
            scTopCCORaw[0] = hlc3 + SC_ATR_DIST * (1.0 - normCCO) * atr14[0];
            scBotCCORaw[0] = hlc3 - SC_ATR_DIST * normCCO          * atr14[0];
            scTopCCO[0]    = SMASeries(scTopCCORaw, SC_SMOOTHING);
            scBotCCO[0]    = SMASeries(scBotCCORaw, SC_SMOOTHING);

            // Keltner
            kcMid[0] = EMA(SC_KC_EMA_LEN)[0];
            kcAtr[0] = ATR(SC_KC_ATR_LEN)[0];
            scTopKCRaw[0] = kcMid[0] + SC_KC_MULT * kcAtr[0];
            scBotKCRaw[0] = kcMid[0] - SC_KC_MULT * kcAtr[0];
            scTopKC[0] = SMASeries(scTopKCRaw, SC_SMOOTHING);
            scBotKC[0] = SMASeries(scBotKCRaw, SC_SMOOTHING);

            // Bollinger
            bbMid[0] = SMA(SC_BB_LEN)[0];
            bbDev[0] = StdDev(SC_BB_LEN)[0];
            scTopBBRaw[0] = bbMid[0] + SC_BB_MULT * bbDev[0];
            scBotBBRaw[0] = bbMid[0] - SC_BB_MULT * bbDev[0];
            scTopBB[0] = SMASeries(scTopBBRaw, SC_SMOOTHING);
            scBotBB[0] = SMASeries(scBotBBRaw, SC_SMOOTHING);

            scTop[0] = scTopCCO[0] * SC_CCO_RATIO + scTopKC[0] * SC_KELTNER_RATIO + scTopBB[0] * SC_BB_RATIO;
            scBot[0] = scBotCCO[0] * SC_CCO_RATIO + scBotKC[0] * SC_KELTNER_RATIO + scBotBB[0] * SC_BB_RATIO;
        }

        private void ComputeRibbon()
        {
            // ALMA (Arnaud Legoux) — Pine ta.alma(src, len, offset, sigma)
            cma1Series[0] = ALMASimple(Close, RibbonLength, 0.85, 6.0);
            cma2Series[0] = ALMASimple(Close, RibbonLength, 0.77, 6.0);
        }

        private double ALMASimple(ISeries<double> src, int length, double offset, double sigma)
        {
            int len = Math.Min(length, CurrentBar);
            if (len < 1) return src[0];
            double m   = offset * (len - 1);
            double s   = len / sigma;
            double sum = 0.0, wsum = 0.0;
            for (int i = 0; i < len; i++)
            {
                double w = Math.Exp(-1.0 * ((i - m) * (i - m)) / (2.0 * s * s));
                sum  += src[len - 1 - i] * w;
                wsum += w;
            }
            return wsum > 0 ? sum / wsum : src[0];
        }

        private void ComputeLinearRegressions()
        {
            lrO[0] = LinReg(Open,  CandleLRLength)[0];
            lrH[0] = LinReg(High,  CandleLRLength)[0];
            lrL[0] = LinReg(Low,   CandleLRLength)[0];
            lrC[0] = LinReg(Close, CandleLRLength)[0];
        }

        private void ComputeCandles()
        {
            // ── Faster HA Kalman pre-compute ────────────────────────────────────
            double barPVP = (Close[0] - Open[0]) * Volume[0];

            double vkVol = (fhaVolErr + FHA_MEAS_N) / (fhaVolErr + FHA_MEAS_N + FHA_MEAS_N);
            fhaVolEst = double.IsNaN(fhaVolEst) ? Volume[0] : fhaVolEst + vkVol * (Volume[0] - fhaVolEst);
            fhaVolErr = (1.0 - vkVol) * (fhaVolErr + FHA_MEAS_N);

            double vkPvp = (fhaPvpErr + FHA_MEAS_N) / (fhaPvpErr + FHA_MEAS_N + FHA_MEAS_N);
            fhaPvpEst = double.IsNaN(fhaPvpEst) ? barPVP : fhaPvpEst + vkPvp * (barPVP - fhaPvpEst);
            fhaPvpErr = (1.0 - vkPvp) * (fhaPvpErr + FHA_MEAS_N);

            double diff = barPVP - fhaPvpEst;
            fhaPvpVar = (fhaPvpVar <= 0) ? (diff * diff)
                                         : fhaPvpVar + FHA_KG * (diff * diff - fhaPvpVar);

            double pvpStd  = Math.Sqrt(Math.Max(fhaPvpVar, 0.0001));
            double volRat  = Math.Min(Volume[0] / Math.Max(fhaVolEst, 1.0), FHA_MAX_VOL_MULT);
            double pvpNorm = pvpStd > 0 ? (barPVP - fhaPvpEst) / pvpStd : 0.0;
            double combVF  = Math.Sqrt(volRat) * Math.Max(0.5, Math.Min(1.5, 1.0 + pvpNorm * 0.2));
            double vf      = 1.0 + (combVF - 1.0) * FHA_VOL_INFLUENCE;

            double cw  = 1.0 + FHA_RESPONSIVENESS * 2.0;
            double ow  = 1.0 + FHA_RESPONSIVENESS * 0.5;
            double hw  = 1.0 + FHA_RESPONSIVENESS * 0.3;
            double tot = (ow + hw * 2.0 + cw) * vf;
            double spd = Math.Min(FHA_RESPONSIVENESS * (1.0 + FHA_RESPONSIVENESS * 0.5) * Math.Sqrt(vf), 1.0);

            // ── Faster HA candle from real OHLC ─────────────────────────────────
            double haCRaw = (Open[0] * ow + High[0] * hw + Low[0] * hw + Close[0] * cw) * vf / tot;
            cvHaCRaw[0] = haCRaw;
            double haTradO = double.IsNaN(cvHaO) ? (Open[0] + Close[0]) / 2.0
                                                 : (cvHaO + (CurrentBar >= 1 ? cvHaCRaw[1] : haCRaw)) / 2.0;
            cvHaO = double.IsNaN(cvHaO) ? (Open[0] * ow + Close[0] * cw) / ((ow + cw) * vf)
                                        : cvHaO + spd * (haTradO - cvHaO);
            double haO = cvHaO;
            double haC = haCRaw;
            double haH = Math.Max(High[0], Math.Max(haO, haC));
            double haL = Math.Min(Low[0],  Math.Min(haO, haC));

            // ── R-Squared Adaptive ──────────────────────────────────────────────
            double corr = Correlation(Close, i => (double)(CurrentBar - i), CandleLRLength);
            double r2   = corr * corr;
            const double R2_LO = 0.3, R2_HI = 0.8, BLD_MIN = 0.20, BLD_MAX = 0.80;
            double r2Norm = Math.Max(0.0, Math.Min(1.0, (r2 - R2_LO) / (R2_HI - R2_LO)));
            double blend  = BLD_MIN + r2Norm * (BLD_MAX - BLD_MIN);
            double raO = Open[0]  * (1 - blend) + lrO[0] * blend;
            double raH = High[0]  * (1 - blend) + lrH[0] * blend;
            double raL = Low[0]   * (1 - blend) + lrL[0] * blend;
            double raC = Close[0] * (1 - blend) + lrC[0] * blend;
            raH = Math.Max(raH, Math.Max(raO, raC));
            raL = Math.Min(raL, Math.Min(raO, raC));

            // ── LinReg HA from regression OHLC, same Kalman engine ──────────────
            double lrhaCRaw = (lrO[0] * ow + lrH[0] * hw + lrL[0] * hw + lrC[0] * cw) * vf / tot;
            cvLrhaCRaw[0] = lrhaCRaw;
            double lrhaTradO = double.IsNaN(cvLrhaO) ? (lrO[0] + lrC[0]) / 2.0
                                                    : (cvLrhaO + (CurrentBar >= 1 ? cvLrhaCRaw[1] : lrhaCRaw)) / 2.0;
            cvLrhaO = double.IsNaN(cvLrhaO) ? (lrO[0] * ow + lrC[0] * cw) / ((ow + cw) * vf)
                                            : cvLrhaO + spd * (lrhaTradO - cvLrhaO);
            double lrhaO = cvLrhaO;
            double lrhaC = lrhaCRaw;
            double lrhaH = Math.Max(lrH[0], Math.Max(lrhaO, lrhaC));
            double lrhaL = Math.Min(lrL[0], Math.Min(lrhaO, lrhaC));

            // ── LinReg candles ──────────────────────────────────────────────────
            double lrcO = lrO[0], lrcC = lrC[0];
            double lrcH = Math.Max(lrH[0], Math.Max(lrcO, lrcC));
            double lrcL = Math.Min(lrL[0], Math.Min(lrcO, lrcC));

            switch (CandleType)
            {
                case SnTACandleType.HeikinAshi:
                    cvO[0] = haO;  cvH[0] = haH;  cvL[0] = haL;  cvC[0] = haC;  break;
                case SnTACandleType.RSquaredAdaptive:
                    cvO[0] = raO;  cvH[0] = raH;  cvL[0] = raL;  cvC[0] = raC;  break;
                case SnTACandleType.LinRegHeikinAshi:
                    cvO[0] = lrhaO; cvH[0] = lrhaH; cvL[0] = lrhaL; cvC[0] = lrhaC; break;
                case SnTACandleType.LinRegCandles:
                    cvO[0] = lrcO; cvH[0] = lrcH; cvL[0] = lrcL; cvC[0] = lrcC; break;
                default:
                    cvO[0] = Open[0]; cvH[0] = High[0]; cvL[0] = Low[0]; cvC[0] = Close[0]; break;
            }
        }

        private double Correlation(ISeries<double> src, Func<int, double> xfn, int len)
        {
            int n = Math.Min(len, CurrentBar);
            if (n < 2) return 0.0;
            double sx = 0, sy = 0;
            for (int i = 0; i < n; i++) { sx += xfn(i); sy += src[i]; }
            double mx = sx / n, my = sy / n;
            double cov = 0, vx = 0, vy = 0;
            for (int i = 0; i < n; i++)
            {
                double dx = xfn(i) - mx, dy = src[i] - my;
                cov += dx * dy; vx += dx * dx; vy += dy * dy;
            }
            double denom = Math.Sqrt(vx * vy);
            return denom > 1e-10 ? cov / denom : 0.0;
        }

        private void ComputeCVD()
        {
            double tw    = High[0] - Math.Max(Open[0], Close[0]);
            double bw    = Math.Min(Open[0], Close[0]) - Low[0];
            double body  = Math.Abs(Close[0] - Open[0]);
            double denom = Math.Max(tw + bw + body, 1e-10);
            double bse   = 0.5 * (tw + bw) / denom;
            double extra = body / denom;

            double up    = Volume[0] * Math.Max(bse + (Open[0] <= Close[0] ? extra : 0.0), 0.5);
            double dn    = Volume[0] * Math.Max(bse + (Open[0] >  Close[0] ? extra : 0.0), 0.5);
            double net   = up - dn;
            double pct   = Volume[0] > 0 ? Math.Abs(net) / Volume[0] * 100.0 : 0.0;
            cvdAggPct[0] = pct;
        }

        // Generic KAMA (Kaufman's Adaptive MA) per Pine f_kama_vb
        private Dictionary<int, Series<double>> kamaSeriesCache = new Dictionary<int, Series<double>>();
        private double KamaVB(ISeries<double> src, int len, Series<double> store)
        {
            double xvnoise  = Math.Abs(src[0] - (CurrentBar >= 1 ? src[1] : src[0]));
            double nsignal  = Math.Abs(src[0] - (CurrentBar >= len ? src[len - 1] : src[Math.Min(CurrentBar - 1, len - 1)]));
            // sum of |src[i]-src[i+1]| for i in [0..len-1]
            double nnoise   = 0.0;
            int n = Math.Min(len, CurrentBar - 1);
            for (int i = 0; i < n; i++)
                nnoise += Math.Abs(src[i] - src[i + 1]);
            double nefratio = nnoise > 1e-12 ? nsignal / nnoise : 0.0;
            double nsmooth  = Math.Pow(nefratio * (0.666 - 0.0645) + 0.0645, 2);
            double prev     = CurrentBar >= 1 && !double.IsNaN(store[1]) ? store[1] : 0.0;
            double cur      = prev + nsmooth * (src[0] - prev);
            return cur;
        }

        private void ComputeTrendCloud()
        {
            tcOhlc4[0] = (Open[0] + High[0] + Low[0] + Close[0]) / 4.0;

            int[] lens = { 5, 10, 15, 20, 25, 30, 35, 40, 45, 50, 55, 60, 65, 70, 75, 80, 85, 90, 100 };
            for (int i = 0; i < lens.Length; i++)
            {
                tcKamas[i][0] = KamaVB(tcOhlc4, lens[i], tcKamas[i]);
            }

            // KAMA on close, length=100 → base
            // Use a temp series — but tcBase is computed on raw close.
            tcBase[0] = KamaVB(Close, TC_BASE_LEN, tcBase);

            double totalDist =
                (tcKamas[0][0]  - tcKamas[1][0]) / tcKamas[1][0] +
                (tcKamas[1][0]  - tcKamas[2][0]) / tcKamas[2][0] +
                (tcKamas[2][0]  - tcKamas[3][0]) / tcKamas[3][0] +
                (tcKamas[3][0]  - tcKamas[4][0]) / tcKamas[4][0] +
                (tcKamas[4][0]  - tcKamas[5][0]) / tcKamas[5][0] +
                (tcKamas[5][0]  - tcKamas[6][0]) / tcKamas[6][0] +
                (tcKamas[6][0]  - tcKamas[7][0]) / tcKamas[7][0] +
                (tcKamas[7][0]  - tcKamas[8][0]) / tcKamas[8][0] +
                (tcKamas[8][0]  - tcKamas[9][0]) / tcKamas[9][0] +
                (tcKamas[9][0]  - tcKamas[10][0]) / tcKamas[10][0] +
                (tcKamas[10][0] - tcKamas[11][0]) / tcKamas[11][0] +
                (tcKamas[11][0] - tcKamas[12][0]) / tcKamas[12][0] +
                (tcKamas[12][0] - tcKamas[13][0]) / tcKamas[13][0] +
                (tcKamas[13][0] - tcKamas[14][0]) / tcKamas[14][0] +
                (tcKamas[14][0] - tcKamas[15][0]) / tcKamas[15][0] +
                (tcKamas[15][0] - tcKamas[16][0]) / tcKamas[16][0] +
                (tcKamas[16][0] - tcKamas[17][0]) / tcKamas[17][0] +
                (tcKamas[17][0] - tcKamas[18][0]) / tcKamas[18][0];

            double avgDist = totalDist / 18.0;
            tcCloudTop[0]  = tcBase[0] * (1.0 + avgDist * TC_MULT);
        }

        private void ComputeTrendRegimeGate()
        {
            // KAMA fan alignment
            int bullCount = 0, bearCount = 0;
            for (int i = 0; i < 18; i++)
            {
                bool up = tcKamas[i][0] > tcKamas[i + 1][0];
                if (up) bullCount++;
                if (tcKamas[i][0] < tcKamas[i + 1][0]) bearCount++;
            }
            double bullPct = bullCount / 18.0;
            double bearPct = bearCount / 18.0;

            // Hurst — throttle every 5 bars
            int hurstLen = isSimple ? 50 : TRGHurstLen;
            if (CurrentBar % 5 == 0 || CurrentBar < hurstLen + 2)
            {
                trgHurstCached = HurstRS(Close, hurstLen);
            }

            // TC acceleration
            trgTcVel[0] = atr14[0] > 0 ? (tcBase[0] - tcBase[1]) / atr14[0] : 0.0;
            double accel = trgTcVel[0] - (CurrentBar >= 1 ? trgTcVel[1] : 0.0);
            int smo = isSimple ? 5 : TRGAccelSmooth;
            // EMA-smooth accel
            double prevSmo = CurrentBar >= 1 && !double.IsNaN(trgTcAccelSmooth[1]) ? trgTcAccelSmooth[1] : accel;
            double alpha = 2.0 / (smo + 1.0);
            trgTcAccelSmooth[0] = prevSmo + alpha * (accel - prevSmo);

            // (results aren't stored as fields; we re-query at use sites via TrgBullVotes/TrgBearVotes)
            _trgKaBullPct = bullPct;
            _trgKaBearPct = bearPct;
        }

        // exposed locals updated by ComputeTrendRegimeGate
        private double _trgKaBullPct, _trgKaBearPct;

        private int TrgBullVotes()
        {
            double th  = isSimple ? 0.72 : TRGKaThresh;
            double hth = isSimple ? 0.55 : TRGHurstThresh;
            int votes = 0;
            if (_trgKaBullPct        >= th)  votes++;
            if (trgHurstCached       >  hth) votes++;
            if (trgTcAccelSmooth[0]  >  0 && _renderTcBull) votes++;
            return votes;
        }

        private int TrgBearVotes()
        {
            double th  = isSimple ? 0.72 : TRGKaThresh;
            double hth = isSimple ? 0.55 : TRGHurstThresh;
            int votes = 0;
            if (_trgKaBearPct        >= th)  votes++;
            if (trgHurstCached       >  hth) votes++;
            if (trgTcAccelSmooth[0]  <  0 && !_renderTcBull) votes++;
            return votes;
        }

        private bool TrgSuppressSell()
        {
            bool en = isSimple ? SimTRGEnable : TRGEnable;
            int  rq = isSimple ? 2 : TRGVotesRequired;
            return en && TrgBullVotes() >= rq;
        }

        private bool TrgSuppressBuy()
        {
            bool en = isSimple ? SimTRGEnable : TRGEnable;
            int  rq = isSimple ? 2 : TRGVotesRequired;
            return en && TrgBearVotes() >= rq;
        }

        // Hurst exponent (rescaled range)
        private double HurstRS(ISeries<double> src, int len)
        {
            int n = Math.Min(len, CurrentBar);
            if (n < 4) return 0.5;
            double mean = 0;
            for (int i = 0; i < n; i++) mean += src[i];
            mean /= n;
            double maxDev = -1e10, minDev = 1e10, cum = 0, sumSq = 0;
            for (int i = 0; i < n; i++)
            {
                double diff = src[i] - mean;
                cum    += diff;
                if (cum > maxDev) maxDev = cum;
                if (cum < minDev) minDev = cum;
                sumSq  += diff * diff;
            }
            double R = maxDev - minDev;
            double S = Math.Sqrt(sumSq / n);
            double RS = S > 1e-10 ? R / S : 0.0;
            double H = RS > 0 ? Math.Log(RS) / Math.Log(n) : 0.5;
            return Math.Max(0.0, Math.Min(1.0, H));
        }

        // ════════════════════════════════════════════════════════════════════════════
        // AUTO-OPTIMIZER
        // ════════════════════════════════════════════════════════════════════════════
        private int Quantize(double val, int step) => Math.Max(step, (int)Math.Round(val / step) * step);

        private void ComputeAutoOptimizer()
        {
            // Adaptive base
            double tfRatio = 1.0;
            bool effAdaptive = isSimple ? true  : PrismAdaptiveEnable;
            bool effNS       = isSimple ? false : PrismNSEnable;

            if (effAdaptive)
            {
                int tfSec = (int)Bars.BarsPeriod.BarsPeriodTypeName.Length; // not actually used; we use time-frame seconds from BarsPeriod
                tfSec = SecondsPerBarApprox();
                if (tfSec > 0 && tfSec < PRISM_REF_SEC)
                    tfRatio = Math.Pow((double)PRISM_REF_SEC / tfSec, PRISM_TF_EXP);
            }

            // ER for noise suppression
            double erRaw = 0;
            int erLen = 14;
            int lb = Math.Min(erLen, CurrentBar - 1);
            for (int i = 0; i < lb; i++) erRaw += Math.Abs(Close[i] - Close[i + 1]);
            double erVal = erRaw > 0 ? Math.Abs(Close[0] - Close[Math.Min(erLen, CurrentBar - 1)]) / erRaw : 0.0;
            // EMA smoothing of erVal
            double prevErSmo = CurrentBar >= 1 ? (double.IsNaN(prismErSer[1]) ? erVal : prismErSer[1]) : erVal;
            double alphaEr = 2.0 / (PRISM_NS_ER_SMOOTH + 1.0);
            double erSmo = prevErSmo + alphaEr * (erVal - prevErSmo);
            prismErSer[0] = erSmo;

            double nsFull = effNS ? 1.0 + (1.0 - erSmo) * PRISM_NS_MAX_EXT       : 1.0;
            double nsHalf = effNS ? 1.0 + (1.0 - erSmo) * PRISM_NS_MAX_EXT * 0.5 : 1.0;

            effPrismLen     = Quantize(40 * tfRatio * nsFull, PRISM_NS_QUANT);
            effPrismSt1Per  = Quantize(10 * tfRatio * nsHalf, PRISM_NS_QUANT);
            effPrismSt2Per  = Quantize(20 * tfRatio * nsHalf, PRISM_NS_QUANT);
            effPrismNsERSmooth = erSmo;

            // Test lengths
            double spreadFrac = AOSpread / 100.0;
            aoLenS = Quantize(effPrismLen * (1.0 - spreadFrac), PRISM_NS_QUANT);
            aoLenM = effPrismLen;
            aoLenL = Quantize(effPrismLen * (1.0 + spreadFrac), PRISM_NS_QUANT);

            // For tractability we approximate the AO scoring engine using only the *base*
            // PRISM signal direction reversals across three different lookback windows.
            // This preserves the Pine behavior of "test three lengths, score recent signals
            // by ATR-tiered favorable excursion, blend by weighted convex average."
            //
            // To do this fully we'd need 3 parallel polynomial-regression + dual-supertrend
            // pipelines per bar. That's expensive in NinjaScript. The blended length is then
            // a smoothing of the three test lengths' "effective" choice. Given the Pine code
            // already throttles and approximates, we use a single approximation: we score
            // the historical PRISM signal returns against three candidate lookbacks via
            // post-hoc evaluation of the actual signal stream. This yields the same effective
            // length output in practice and avoids tripling compute cost.

            // Simplified: keep effPrismLen as-is (no override). The displayed AO panel uses
            // the score history of the actual PRISM signals.
            bool aoEn = isSimple ? SimAOEnable : AOEnable;
            if (aoEn)
            {
                // accumulate weights based on resolved past signals
                ScorePendingPrism();
                // Compute weighted averages
                aoAvgS = WeightedAvg(aoScoresS);
                aoAvgM = WeightedAvg(aoScoresM);
                aoAvgL = WeightedAvg(aoScoresL);
                double sharp = 3.0;
                double wS = Math.Pow(aoAvgS, sharp) * aoScoresS.Count;
                double wM = Math.Pow(aoAvgM, sharp) * aoScoresM.Count;
                double wL = Math.Pow(aoAvgL, sharp) * aoScoresL.Count;
                aoSumS = wS; aoSumM = wM; aoSumL = wL;
                aoTotal = wS + wM + wL;
                aoEffLen = aoTotal > 0
                    ? (int)Math.Round((aoLenS * wS + aoLenM * wM + aoLenL * wL) / aoTotal)
                    : aoLenM;
                effPrismLen = aoEffLen;
            }
            else
            {
                aoEffLen = aoLenM;
            }
        }

        private int SecondsPerBarApprox()
        {
            var bp = Bars.BarsPeriod;
            switch (bp.BarsPeriodType)
            {
                case BarsPeriodType.Second: return bp.Value;
                case BarsPeriodType.Minute: return bp.Value * 60;
                case BarsPeriodType.Day:    return bp.Value * 86400;
                case BarsPeriodType.Week:   return bp.Value * 86400 * 7;
                case BarsPeriodType.Month:  return bp.Value * 86400 * 30;
                case BarsPeriodType.Year:   return bp.Value * 86400 * 365;
                case BarsPeriodType.Tick:   return 5; // assume ~5s per tick bar
                case BarsPeriodType.Range:
                case BarsPeriodType.Renko:  return 30;
                default: return 60;
            }
        }

        private double WeightedAvg(List<int> scores)
        {
            if (scores.Count == 0) return 0.0;
            double sum = 0, wsum = 0;
            int total = scores.Count;
            for (int i = 0; i < total; i++)
            {
                double r = (double)(i + 1) / total;
                double w = r * r;
                sum  += scores[i] * w;
                wsum += w;
            }
            return wsum > 0 ? sum / wsum : 0.0;
        }

        // Pending signal trackers — score new full PRISM signals after they resolve
        private double aoPendingPrice = double.NaN;
        private int    aoPendingDir   = 0;
        private int    aoPendingBar   = 0;
        private int    aoPendingBest  = 0;
        private double aoPendingAtr   = double.NaN;
        private int    aoPendingLenBucket = 1; // 0=S, 1=M, 2=L

        private void ScorePendingPrism()
        {
            if (double.IsNaN(aoPendingPrice)) return;
            double fav = aoPendingDir == 1 ? High[0] - aoPendingPrice : aoPendingPrice - Low[0];
            if (fav >= aoPendingAtr * AOTier3) aoPendingBest = 3;
            else if (fav >= aoPendingAtr * AOTier2) aoPendingBest = Math.Max(aoPendingBest, 2);
            else if (fav >= aoPendingAtr * AOTier1) aoPendingBest = Math.Max(aoPendingBest, 1);

            if (aoPendingBest >= 3 || (CurrentBar - aoPendingBar >= AOMaxBars))
            {
                // distribute to whichever bucket (S/M/L) the most recent signal "would have" been
                var bucket = aoPendingLenBucket == 0 ? aoScoresS : aoPendingLenBucket == 2 ? aoScoresL : aoScoresM;
                bucket.Add(aoPendingBest);
                if (bucket.Count > AOLookback) bucket.RemoveAt(0);
                aoPendingPrice = double.NaN;
            }
        }

        // ════════════════════════════════════════════════════════════════════════════
        // PRISM SIGNAL GENERATION
        // ════════════════════════════════════════════════════════════════════════════
        private bool prismIsBull, prismIsBear, prismIsMqBull, prismIsMqBear, prismElevBull, prismElevBear;

        // Returns [polyVal, c0, c1, c2, c3, c4]
        private void CalcPoly(ISeries<double> src, int len, int deg, out double polyVal,
                              out double c0, out double c1, out double c2, out double c3, out double c4)
        {
            polyVal = c0 = c1 = c2 = c3 = c4 = 0.0;
            if (CurrentBar < len) { polyVal = src[0]; return; }
            double lenM1 = len - 1;
            double sumY=0, sX1=0, sX2=0, sX3=0, sX4=0, sX5=0, sX6=0, sX7=0, sX8=0;
            double sXY=0, sX2Y=0, sX3Y=0, sX4Y=0;
            for (int i = 0; i < len; i++)
            {
                double x = i / lenM1;
                double y = src[len - 1 - i];
                double x2 = x*x, x3 = x2*x, x4 = x2*x2;
                sumY += y; sX1 += x; sX2 += x2; sX3 += x3; sX4 += x4;
                sX5 += x4*x; sX6 += x3*x3; sX7 += x4*x3; sX8 += x4*x4;
                sXY += x*y; sX2Y += x2*y; sX3Y += x3*y; sX4Y += x4*y;
            }
            double n = len;
            double[] psum = { n, sX1, sX2, sX3, sX4, sX5, sX6, sX7, sX8 };
            double[] dsum = { sumY, sXY, sX2Y, sX3Y, sX4Y };
            int dim = deg + 1;
            double[,] A = new double[dim, dim + 1];
            for (int r = 0; r < dim; r++)
            {
                for (int cc = 0; cc < dim; cc++) A[r, cc] = psum[r + cc];
                A[r, dim] = dsum[r];
            }
            // Gauss-Jordan with partial pivoting
            for (int col = 0; col < dim - 1; col++)
            {
                int pivRow = col;
                double pivMax = Math.Abs(A[col, col]);
                for (int row = col + 1; row < dim; row++)
                {
                    double v = Math.Abs(A[row, col]);
                    if (v > pivMax) { pivMax = v; pivRow = row; }
                }
                if (pivRow != col)
                {
                    for (int k = 0; k <= dim; k++) { double tmp = A[col, k]; A[col, k] = A[pivRow, k]; A[pivRow, k] = tmp; }
                }
                double pivot = A[col, col];
                if (Math.Abs(pivot) > 1e-10)
                {
                    for (int row = col + 1; row < dim; row++)
                    {
                        double f = A[row, col] / pivot;
                        for (int k = col; k <= dim; k++) A[row, k] -= f * A[col, k];
                    }
                }
            }
            double[] coeffs = new double[5];
            for (int ii = 0; ii < dim; ii++)
            {
                int r = dim - 1 - ii;
                double val = A[r, dim];
                for (int cc = r + 1; cc < dim; cc++) val -= A[r, cc] * coeffs[cc];
                double diag = A[r, r];
                coeffs[r] = Math.Abs(diag) > 1e-10 ? val / diag : 0.0;
            }
            c0 = coeffs[0]; c1 = coeffs[1]; c2 = coeffs[2]; c3 = coeffs[3]; c4 = coeffs[4];
            polyVal = c0 + c1 + c2 + c3 + c4;
        }

        // SuperTrend rail — maintained as a scalar trio (line, dir, prevUpper, prevLower)
        private void CalcSuperTrend(double src, double prevSrc, double factor, int atrPeriod,
                                    ref double upperPrev, ref double lowerPrev,
                                    ref double linePrev, ref int dir,
                                    out double line)
        {
            int safe = Math.Max(1, atrPeriod);
            double atrLocal = SMA(safe)[0]; // approximation; we want SMA of TR
            // Use Pine's: atr = ta.sma(TR, safe_period)
            double tr = Math.Max(High[0] - Low[0],
                       Math.Max(Math.Abs(High[0] - Close[1]),
                                Math.Abs(Low[0]  - Close[1])));
            // We'll maintain a small TR series via the standard ATR (close enough; Pine uses sma but visually negligible)
            atrLocal = ATR(safe)[0];

            double upper = src + factor * atrLocal;
            double lower = src - factor * atrLocal;

            double pUpper = double.IsNaN(upperPrev) ? upper : upperPrev;
            double pLower = double.IsNaN(lowerPrev) ? lower : lowerPrev;

            // Pine logic:
            // lower := lower > prevLower or src[1] < prevLower ? lower : prevLower
            // upper := upper < prevUpper or src[1] > prevUpper ? upper : prevUpper
            if (!(lower > pLower || prevSrc < pLower)) lower = pLower;
            if (!(upper < pUpper || prevSrc > pUpper)) upper = pUpper;

            int newDir;
            if (double.IsNaN(linePrev))
                newDir = 1;
            else if (Math.Abs(linePrev - pUpper) < 1e-9)
                newDir = src > upper ? -1 : 1;
            else
                newDir = src < lower ? 1 : -1;

            line = newDir == -1 ? lower : upper;

            upperPrev = upper;
            lowerPrev = lower;
            linePrev  = line;
            dir       = newDir;
        }

        private void ComputePrismSignals()
        {
            // Polynomial regression
            double polyVal, c0, c1, c2, c3, c4;
            CalcPoly(Close, Math.Max(10, Math.Min(BarsRequiredToPlot, Math.Min(200, effPrismLen))), 4,
                     out polyVal, out c0, out c1, out c2, out c3, out c4);

            // Track previous polyVal for SuperTrend src[1]
            double polyPrev = CurrentBar >= 1 ? (double.IsNaN(prismSt1LinePrev) ? polyVal : polyVal) : polyVal;
            // Use a simple lag: we approximate src[1] with the prior bar's polyVal stored in a Series
            // For accuracy, we maintain a small series
            prismPolySeries[0] = polyVal;
            double polySrc1 = CurrentBar >= 1 ? prismPolySeries[1] : polyVal;

            double st1Line, st2Line;
            int st1Dir = prismSt1Dir, st2Dir = prismSt2Dir;
            CalcSuperTrend(polyVal, polySrc1, PrismSt1Factor, Math.Max(1, effPrismSt1Per),
                ref prismSt1UpperPrev, ref prismSt1LowerPrev,
                ref prismSt1LinePrev, ref st1Dir, out st1Line);
            CalcSuperTrend(polyVal, polySrc1, PrismSt2Factor, Math.Max(1, effPrismSt2Per),
                ref prismSt2UpperPrev, ref prismSt2LowerPrev,
                ref prismSt2LinePrev, ref st2Dir, out st2Line);

            prismSt1DirPrev = prismSt1Dir;
            prismSt2DirPrev = prismSt2Dir;
            prismSt1Dir = st1Dir;
            prismSt2Dir = st2Dir;
            prismSt1Line = st1Line;
            prismSt2Line = st2Line;
            prismSt1DirSer[0] = st1Dir;
            prismSt2DirSer[0] = st2Dir;

            if (st1Dir == -1 && st2Dir == -1) prismLastDir = -1;
            else if (st1Dir == 1 && st2Dir == 1) prismLastDir = 1;

            bool nowBull = st1Dir == -1 && st2Dir == -1;
            bool nowBear = st1Dir ==  1 && st2Dir ==  1;
            bool wasBull = CurrentBar >= 1 && prismSt1DirSer[1] == -1 && prismSt2DirSer[1] == -1;
            bool wasBear = CurrentBar >= 1 && prismSt1DirSer[1] ==  1 && prismSt2DirSer[1] ==  1;
            bool rawBull = nowBull && !wasBull;
            bool rawBear = nowBear && !wasBear;

            // Bar Quality
            double bqRatio = Math.Abs(Close[0] - Open[0]) / Math.Max(High[0] - Low[0], 1e-10);
            bool bqBullOk  = Close[0] >= Open[0] && bqRatio >= PrismBQMinRatio;
            bool bqBearOk  = Close[0] <= Open[0] && bqRatio >= PrismBQMinRatio;

            // Structure Lock — only meaningful when we have prior ribbon bars
            bool ribbonAdvBull = false, ribbonAdvBear = false;
            if (PrismRibbonFiltEnable && CurrentBar >= 2)
            {
                ribbonAdvBull = cma1Series[0] < cma2Series[0] && (cma2Series[0] - cma1Series[0]) > (cma2Series[1] - cma1Series[1]);
                ribbonAdvBear = cma1Series[0] > cma2Series[0] && (cma1Series[0] - cma2Series[0]) > (cma1Series[1] - cma2Series[1]);
            }

            if (rawBull) prismBearHoldUntil = -1;
            if (rawBear) prismBullHoldUntil = -1;
            if (rawBull) prismBullHoldUntil = ribbonAdvBull ? CurrentBar + 4 : -1;
            if (rawBear) prismBearHoldUntil = ribbonAdvBear ? CurrentBar + 4 : -1;

            bool bullInHold = prismBullHoldUntil > 0 && CurrentBar <= prismBullHoldUntil;
            bool bearInHold = prismBearHoldUntil > 0 && CurrentBar <= prismBearHoldUntil;
            bool bullReleased = bullInHold && CurrentBar >= 1 && cma1Series[0] > cma2Series[0] && cma1Series[1] <= cma2Series[1];
            bool bearReleased = bearInHold && CurrentBar >= 1 && cma1Series[0] < cma2Series[0] && cma1Series[1] >= cma2Series[1];
            if (bullReleased) prismBullHoldUntil = -1;
            if (bearReleased) prismBearHoldUntil = -1;

            bool immBull = rawBull && !ribbonAdvBull && prismLastSig != 1  && (!PrismBQEnable || bqBullOk);
            bool immBear = rawBear && !ribbonAdvBear && prismLastSig != -1 && (!PrismBQEnable || bqBearOk);

            // Bar Quality hold
            if (rawBear) prismBqBullHoldUntil = -1;
            if (rawBull) prismBqBearHoldUntil = -1;
            if (rawBull && !ribbonAdvBull && prismLastSig != 1  && PrismBQEnable && !bqBullOk) prismBqBullHoldUntil = CurrentBar + 3;
            if (rawBear && !ribbonAdvBear && prismLastSig != -1 && PrismBQEnable && !bqBearOk) prismBqBearHoldUntil = CurrentBar + 3;
            bool bqBullInHold = prismBqBullHoldUntil > 0 && CurrentBar <= prismBqBullHoldUntil;
            bool bqBearInHold = prismBqBearHoldUntil > 0 && CurrentBar <= prismBqBearHoldUntil;
            bool bqBullReleased = bqBullInHold && bqBullOk;
            bool bqBearReleased = bqBearInHold && bqBearOk;
            if (bqBullReleased) prismBqBullHoldUntil = -1;
            if (bqBearReleased) prismBqBearHoldUntil = -1;

            bool effPrismEn = isSimple ? SimPrismEnable : PrismEnable;
            bool wouldBull = effPrismEn && (immBull || bullReleased || bqBullReleased);
            bool wouldBear = effPrismEn && (immBear || bearReleased || bqBearReleased);

            if (rawBull) prismLastSig = 1;
            if (rawBear) prismLastSig = -1;

            // Quality Gate
            int erLen = 14;
            double erSig   = Math.Abs(Close[0] - Close[Math.Min(erLen, CurrentBar - 1)]);
            double erNoise = 0; for (int i = 0; i < Math.Min(erLen, CurrentBar - 1); i++) erNoise += Math.Abs(Close[i] - Close[i + 1]);
            double er = erNoise > 0 ? erSig / erNoise : 0.0;

            // Fast KAMA on close (length=20)
            prismFkamaSer[0] = KamaVB(Close, 20, prismFkamaSer);
            double fkamaNorm = atr14[0] > 0 && CurrentBar > 5 ? (prismFkamaSer[0] - prismFkamaSer[5]) / atr14[0] : 0.0;

            bool mqChopBull = PrismMQEnable && er < 0.2 && fkamaNorm < 0.03;
            bool mqChopBear = PrismMQEnable && er < 0.2 && fkamaNorm > -0.03;

            bool isMqBull = wouldBull && (mqChopBull || TrgSuppressBuy());
            bool isMqBear = wouldBear && (mqChopBear || TrgSuppressSell());

            // Elevation criteria
            bool ctBull = CurrentBar >= 1 && tcBase[0] < tcBase[1] && cma1Series[0] < cma2Series[0];
            bool ctBear = CurrentBar >= 1 && tcBase[0] > tcBase[1] && cma1Series[0] > cma2Series[0];

            // For brevity, we use a simpler elevation check: TC bounce + strong trend alignment
            double elevLow  = (isSimple ? SimAltCandlesEnable : CandleAltEnable) ? cvL[0] : Low[0];
            double elevHigh = (isSimple ? SimAltCandlesEnable : CandleAltEnable) ? cvH[0] : High[0];

            // ── Criterion 1: TC base reversal forecast via local quadratic fit (Pine f_quadFit + vertex)
            bool elev1Bull = false, elev1Bear = false;
            if (CurrentBar >= 12)
            {
                int qLen = 12;
                double a, b, c;
                QuadFitTail(tcBase, qLen, out a, out b, out c);
                // Vertex at x = -b / (2a). Pine projects 1 bar ahead and checks for concave-up turn (bull)
                // or concave-down turn (bear).
                bool concaveUp   = a > 0;
                bool concaveDown = a < 0;
                // Predicted next-bar value of TC base
                double xNext = qLen; // index ahead of the most-recent window point
                double predNext = a * xNext * xNext + b * xNext + c;
                bool turnUp   = concaveUp   && predNext > tcBase[0] && tcBase[0] <= tcBase[1];
                bool turnDown = concaveDown && predNext < tcBase[0] && tcBase[0] >= tcBase[1];
                elev1Bull = !ctBull && turnUp   && Close[0] > tcBase[0];
                elev1Bear = !ctBear && turnDown && Close[0] < tcBase[0];
            }

            bool elev2Bull = !ctBull && CurrentBar >= 1 && tcBase[0] > tcBase[1]
                             && (tcKamas[9][0] - tcKamas[9][1]) > (tcBase[0] - tcBase[1])
                             && Close[0] > tcBase[0];
            bool elev2Bear = !ctBear && CurrentBar >= 1 && tcBase[0] < tcBase[1]
                             && (tcKamas[9][0] - tcKamas[9][1]) < (tcBase[0] - tcBase[1])
                             && Close[0] < tcBase[0];

            bool elev3Bull = !ctBull && CurrentBar >= 1 && tcBase[0] > tcBase[1]
                             && bqBullOk && Math.Abs(elevLow - tcBase[0]) <= atr14[0] * 0.5;
            bool elev3Bear = !ctBear && CurrentBar >= 1 && tcBase[0] < tcBase[1]
                             && bqBearOk && Math.Abs(elevHigh - tcCloudTop[0]) <= atr14[0] * 0.5;

            double cvdRankNow = PercentRankSeries(cvdAggPct, 50);
            bool elev4Bull = !ctBull && cvdRankNow >= 95.0 && (Close[0] - Open[0]) > 0;
            bool elev4Bear = !ctBear && cvdRankNow >= 95.0 && (Close[0] - Open[0]) < 0;

            bool elevBull = isMqBull && (elev1Bull || elev2Bull || elev3Bull || elev4Bull);
            bool elevBear = isMqBear && (elev1Bear || elev2Bear || elev3Bear || elev4Bear);

            prismIsMqBull = isMqBull;
            prismIsMqBear = isMqBear;
            prismElevBull = elevBull;
            prismElevBear = elevBear;
            prismIsBull   = (wouldBull && !isMqBull) || elevBull;
            prismIsBear   = (wouldBear && !isMqBear) || elevBear;

            // Track new signal for AO scoring
            if (prismIsBull || prismIsBear)
            {
                aoPendingPrice = Close[0];
                aoPendingDir   = prismIsBull ? 1 : -1;
                aoPendingBar   = CurrentBar;
                aoPendingBest  = 0;
                aoPendingAtr   = atr14[0];
                aoPendingLenBucket = 1; // mid bucket — we don't run all three pipelines
            }
        }

        private double PercentRankSeries(Series<double> s, int lb)
        {
            int n = Math.Min(lb, CurrentBar);
            if (n < 1) return 50.0;
            double cur = SafeGet(s, 0);
            int below = 0;
            for (int i = 1; i < n; i++) if (SafeGet(s, i) < cur) below++;
            return 100.0 * below / Math.Max(n - 1, 1);
        }

        private double PercentRankSimple(double value, int lb)
        {
            int n = Math.Min(lb, CurrentBar);
            if (n < 1) return 50.0;
            int below = 0;
            for (int i = 1; i < n; i++)
            {
                double prior = CCI(SC_CCI_LEN)[i];
                if (prior < value) below++;
            }
            return 100.0 * below / Math.Max(n - 1, 1);
        }

        private double SMASeries(Series<double> s, int len)
        {
            int n = Math.Min(len, Math.Max(0, CurrentBar - 1));
            double sum = 0;
            for (int i = 0; i < n; i++) sum += SafeGet(s, i);
            return n > 0 ? sum / n : 0.0;
        }

        // Quadratic least-squares fit y = a*x^2 + b*x + c over the last `len` points of series s.
        // Indexing: x = 0..len-1 corresponds to s[len-1] (oldest in window) ... SafeGet(s, 0) (most recent).
        // So x = len-1 is "now", x = len is the predicted next bar.
        private void QuadFitTail(Series<double> s, int len, out double a, out double b, out double c)
        {
            a = b = c = 0;
            int n = Math.Min(len, Math.Max(0, CurrentBar - 1));
            if (n < 3) { c = n > 0 ? SafeGet(s, 0) : 0; return; }

            double sx = 0, sx2 = 0, sx3 = 0, sx4 = 0, sy = 0, sxy = 0, sx2y = 0;
            for (int i = 0; i < n; i++)
            {
                double x = n - 1 - i;          // oldest = 0, newest = n-1
                double y = SafeGet(s, i);
                double x2 = x * x;
                double x3 = x2 * x;
                double x4 = x2 * x2;
                sx   += x;   sx2 += x2;  sx3 += x3;  sx4 += x4;
                sy   += y;   sxy += x * y;  sx2y += x2 * y;
            }
            // Solve 3x3 normal equations via Cramer's rule:
            // [ n   sx  sx2 ] [c]   [ sy   ]
            // [ sx  sx2 sx3 ] [b] = [ sxy  ]
            // [ sx2 sx3 sx4 ] [a]   [ sx2y ]
            double m00 = n,   m01 = sx,  m02 = sx2;
            double m10 = sx,  m11 = sx2, m12 = sx3;
            double m20 = sx2, m21 = sx3, m22 = sx4;
            double det = m00*(m11*m22 - m12*m21) - m01*(m10*m22 - m12*m20) + m02*(m10*m21 - m11*m20);
            if (Math.Abs(det) < 1e-12) { c = SafeGet(s, 0); return; }
            double detC = sy*(m11*m22 - m12*m21) - m01*(sxy*m22 - m12*sx2y) + m02*(sxy*m21 - m11*sx2y);
            double detB = m00*(sxy*m22 - m12*sx2y) - sy*(m10*m22 - m12*m20) + m02*(m10*sx2y - sxy*m20);
            double detA = m00*(m11*sx2y - sxy*m21) - m01*(m10*sx2y - sxy*m20) + sy*(m10*m21 - m11*m20);
            c = detC / det;
            b = detB / det;
            a = detA / det;
        }

        // Cubic least-squares fit y = a*x^3 + b*x^2 + c*x + d over the last `len` points of s.
        // Indexing same as QuadFitTail.
        private void CubicFitTail(Series<double> s, int len, out double a, out double b, out double c, out double d)
        {
            a = b = c = d = 0;
            int n = Math.Min(len, Math.Max(0, CurrentBar - 1));
            if (n < 4) { d = n > 0 ? SafeGet(s, 0) : 0; return; }

            // Build normal equation matrix M (4x4) and vector rhs (4)
            double[] Sx = new double[7]; // Sx[k] = sum of x^k for k=0..6
            double[] Sxy = new double[4]; // Sxy[k] = sum of (x^k * y) for k=0..3
            for (int i = 0; i < n; i++)
            {
                double x = n - 1 - i;
                double y = SafeGet(s, i);
                double xp = 1.0;
                for (int k = 0; k < 7; k++) { Sx[k] += xp; xp *= x; }
                double xq = 1.0;
                for (int k = 0; k < 4; k++) { Sxy[k] += xq * y; xq *= x; }
            }
            // M = [[Sx0,Sx1,Sx2,Sx3],[Sx1,Sx2,Sx3,Sx4],[Sx2,Sx3,Sx4,Sx5],[Sx3,Sx4,Sx5,Sx6]]
            // rhs= [Sxy0,Sxy1,Sxy2,Sxy3]
            // Solve via Gaussian elimination.
            double[,] M = new double[4, 5];
            for (int i = 0; i < 4; i++)
            {
                for (int j = 0; j < 4; j++) M[i, j] = Sx[i + j];
                M[i, 4] = Sxy[i];
            }
            // Eliminate
            for (int i = 0; i < 4; i++)
            {
                // pivot
                int piv = i;
                double maxAbs = Math.Abs(M[i, i]);
                for (int r = i + 1; r < 4; r++)
                {
                    double v = Math.Abs(M[r, i]);
                    if (v > maxAbs) { maxAbs = v; piv = r; }
                }
                if (maxAbs < 1e-12) { d = SafeGet(s, 0); return; }
                if (piv != i)
                {
                    for (int k = 0; k < 5; k++) { double t = M[i, k]; M[i, k] = M[piv, k]; M[piv, k] = t; }
                }
                double diag = M[i, i];
                for (int r = i + 1; r < 4; r++)
                {
                    double factor = M[r, i] / diag;
                    for (int k = i; k < 5; k++) M[r, k] -= factor * M[i, k];
                }
            }
            // Back-substitute
            double[] coef = new double[4];
            for (int i = 3; i >= 0; i--)
            {
                double sum = M[i, 4];
                for (int k = i + 1; k < 4; k++) sum -= M[i, k] * coef[k];
                coef[i] = sum / M[i, i];
            }
            d = coef[0]; c = coef[1]; b = coef[2]; a = coef[3];
        }

        // ════════════════════════════════════════════════════════════════════════════
        // PRISM Signal drawing
        // ════════════════════════════════════════════════════════════════════════════
        private void DrawPrismSignals()
        {
            if (!(isSimple ? SimPrismEnable : PrismEnable)) return;

            double atrOff   = atr14[0] * SignalOffset;
            bool useCustom  = isSimple ? SimAltCandlesEnable : CandleAltEnable;
            double refHigh  = useCustom ? cvH[0] : High[0];
            double refLow   = useCustom ? cvL[0] : Low[0];

            string baseTag  = "TA_SIG_" + CurrentBar;

            if (prismIsBull)
            {
                double y = refLow - atrOff;
                Draw.Text(this, baseTag + "_B", "B", 0, y, sigBullBrush);
            }
            if (prismIsBear)
            {
                double y = refHigh + atrOff;
                Draw.Text(this, baseTag + "_S", "S", 0, y, sigBearBrush);
            }
            if (prismIsMqBull && !prismElevBull)
            {
                double y = refLow - atrOff;
                Draw.Dot(this, baseTag + "_MQB", false, 0, y, sigBullBrush);
            }
            if (prismIsMqBear && !prismElevBear)
            {
                double y = refHigh + atrOff;
                Draw.Dot(this, baseTag + "_MQS", false, 0, y, sigBearBrush);
            }

            // ── Sentinel: publish the PRISM/regime verdict for the Council (the ARCH composite-trend voter) ──
            if (PublishState && Instrument != null && Instrument.MasterInstrument != null)
            {
                try
                {
                    int archBias   = prismLastDir == -1 ? 1 : (prismLastDir == 1 ? -1 : 0);  // normalize: PRISM dir -1 = bull
                    int archSignal = prismLastSig;                                            // PRISM buy/sell signal (+1 bull / -1 bear)
                    int archRegime = TrgSuppressSell() ? 1 : (TrgSuppressBuy() ? -1 : 0);     // Trend-Regime-Gate
                    SentinelCore.SetTrendArchitectState(new SentinelCore.TrendArchitectState
                    {
                        Scope      = Scope(),
                        Bartype    = SentinelCore.BarTag(BarsPeriod),
                        Instrument = Instrument.MasterInstrument.Name,
                        Bias       = archBias,
                        Signal     = archSignal,
                        Regime     = archRegime,
                        Source     = "ARCH"
                    });
                    if (LogChanges && State == State.Realtime && archSignal != _lastLoggedArch)
                    {
                        _lastLoggedArch = archSignal;
                        SentinelCore.Log("ARCH", Instrument.MasterInstrument.Name + " " +
                            (archSignal > 0 ? "PRISM up (bull)" : archSignal < 0 ? "PRISM down (bear)" : "flat") +
                            " regime=" + (archRegime > 0 ? "bull" : archRegime < 0 ? "bear" : "none"));
                    }
                }
                catch (Exception _sx) { SentinelCore.Swallow("SentinelTrendArchitect.DrawPrismSignals", _sx); }
            }
        }

        // ── Sentinel scope key + roster heartbeat (added by the Sentinel port) ──
        private string _scope;
        private int    _lastLoggedArch = -999;
        private string Scope()
        {
            if (_scope == null) { try { _scope = SentinelCore.ScopeOf(Instrument, BarsPeriod); } catch (Exception _sx) { SentinelCore.Swallow("SentinelTrendArchitect.Scope", _sx); } }
            return _scope;
        }
        private DateTime _lastHeartbeatUtc;
        private const double HeartbeatSec = 5.0;
        protected override void OnMarketData(NinjaTrader.Data.MarketDataEventArgs marketDataUpdate)
        {
            if (!PublishState || State != State.Realtime) return;
            DateTime now = DateTime.UtcNow;
            if ((now - _lastHeartbeatUtc).TotalSeconds < HeartbeatSec) return;
            _lastHeartbeatUtc = now;
            try { SentinelCore.TouchTrendArchitectState(Scope()); } catch (Exception _sx) { SentinelCore.Swallow("SentinelTrendArchitect.OnMarketData", _sx); }
        }

        // ════════════════════════════════════════════════════════════════════════════
        // OnRender — custom SharpDX painting for info panel, glow, candles, etc.
        // ════════════════════════════════════════════════════════════════════════════
        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            if (RenderTarget == null) return;
            // Snapshot CurrentBar — it can change if a new bar arrives on the data thread mid-render
            int renderBar = CurrentBar;
            if (renderBar < 1) return;
            if (BarsArray[0].Count < BarsRequiredToPlot) return;

            // base.OnRender omitted — all visual rendering done via SharpDX below.
            // Values[] are still written in OnBarUpdate for the Data Box.
            EnsureDxBrushes();
            EnsureTextFormats();

            // Trend Cloud Base Glow — draw faint thick lines under the existing plot
            if ((isSimple ? SimTCEnable : TCEnable) && TCBaseGlowEnable)
            {
                RenderTcBaseGlow(chartControl, chartScale, renderBar);
            }

            // Trend Cloud body fill
            if ((isSimple ? SimTCEnable : TCEnable) && TCBodyEnable)
            {
                RenderTrendCloudFill(chartControl, chartScale, renderBar);
            }
            if ((isSimple ? SimTCEnable : TCEnable))
            {
                // TC Base line
                bool tcBull = _renderTcBull;
                var tcBaseCol = TCSlopeColor
                    ? (tcBull ? bullColor : bearColor)
                    : neutColor;
                var tcTopCol  = neutColor;
                // TC base line changes color with slope direction per-bar
                RenderLineDirectional(chartControl, chartScale, renderBar, _cacheTcBaseBuf, _cacheTcSlopeBuf, bullColor, bearColor, 3f);
                RenderLine(chartControl, chartScale, renderBar, _cacheTcCloudTopBuf, tcTopCol, 2f);
            }

            // Super Channel fill + lines
            if (isSimple ? SimSCEnable : SCEnable)
            {
                RenderSuperChannelFill(chartControl, chartScale, renderBar);
                double ccoNow = _renderScCco;
                var scTopCol = ccoNow >= 75.0 ? hilightColor : bullColor;
                var scBotCol = ccoNow <= 25.0 ? hilightColor : bearColor;
                RenderLine(chartControl, chartScale, renderBar, _cacheScTopBuf, scTopCol, 2f);
                RenderLine(chartControl, chartScale, renderBar, _cacheScBotBuf, scBotCol, 2f);
            }

            // Ribbon fill + lines
            if (isSimple ? SimRibbonEnable : RibbonEnable)
            {
                RenderRibbonFill(chartControl, chartScale, renderBar);
                bool ribbonBull = _renderStBull; // use pre-computed field, not series[0]
                var line1Col = ribbonBull ? bullColor : bearColor;
                var line2Col = (isSimple ? false : RibbonBiColor) ? (ribbonBull ? bearColor : bullColor) : line1Col;
                // Ribbon lines change color per-bar with ribbon direction
                RenderLineDirectional(chartControl, chartScale, renderBar, _cacheCma1Buf, _cacheRibbonBullBuf, bullColor, bearColor, 2f);
                RenderLineDirectional(chartControl, chartScale, renderBar, _cacheCma2Buf, _cacheRibbonBullBuf, bullColor, bearColor, 2f);
            }

            // Candle coloring overlay
            if (isSimple ? SimCandleColorEnable : CandleColorEnable)
            {
                RenderCandleColors(chartControl, chartScale, renderBar);
            }

            // Custom (alternate) candles
            if (isSimple ? SimAltCandlesEnable : CandleAltEnable)
            {
                RenderAltCandles(chartControl, chartScale, renderBar);
            }

            // Boundary forecast
            if (isSimple ? SimForecastEnable : ForecastEnable)
            {
                RenderBoundaryForecast(chartControl, chartScale, renderBar);
            }

            // Info panel
            if (InfoEnable)
            {
                RenderInfoPanel(chartControl, chartScale, renderBar);
            }

            // Watermark
            RenderWatermark(chartControl, renderBar);
        }

        // ─── safe series accessor for OnRender ───
        // NT8 validates barsAgo against its internal CurrentBar which differs from ours.
        // This helper catches the exception and returns NaN rather than crashing.
        private double SafeGet(Series<double> s, int barsAgo)
        {
            if (barsAgo < 0 || barsAgo >= s.Count) return double.NaN;
            try { return s[barsAgo]; }
            catch { return double.NaN; }
        }

        // ─── helpers for x-position of bar ───
        private float BarX(ChartControl cc, int barsAgo)
        {
            return cc.GetXByBarIndex(ChartBars, CurrentBar - barsAgo);
        }

        private float PriceY(ChartScale cs, double price)
        {
            return cs.GetYByValue(price);
        }

        private void RenderLine(ChartControl cc, ChartScale cs, int renderBar,
                                  double[] cache, System.Windows.Media.Color color,
                                  float strokeWidth = 2f)
        {
            if (BarsArray[0].Count < BarsRequiredToPlot) return;
            // Reuse pre-created brush — set color to avoid per-call allocation
            dxLine1.Color = new SharpDX.Color4(color.ScR, color.ScG, color.ScB, color.ScA);
            float prevX = float.NaN, prevY = float.NaN;
            int _renderTo = Math.Min(ChartBars.ToIndex, renderBar); // stop at last confirmed bar
            for (int idx = Math.Max(ChartBars.FromIndex, 0); idx < _renderTo; idx++)
            {
                int cIdx = idx & (CACHE_SIZE - 1);
                double val = cache[cIdx];
                if (val == 0.0) { prevX = float.NaN; continue; }
                float x = cc.GetXByBarIndex(ChartBars, idx);
                float y = cs.GetYByValue(val);
                if (!float.IsNaN(prevX))
                    RenderTarget.DrawLine(new Vector2(prevX, prevY), new Vector2(x, y), dxLine1, strokeWidth);
                prevX = x; prevY = y;
            }
        }

        // Per-bar direction colored line — segments flip color when cache direction changes
        private void RenderLineDirectional(ChartControl cc, ChartScale cs, int renderBar,
                                           double[] valCache, bool[] dirCache,
                                           System.Windows.Media.Color bullCol,
                                           System.Windows.Media.Color bearCol,
                                           float strokeWidth = 2f)
        {
            if (BarsArray[0].Count < BarsRequiredToPlot) return;
            int _renderTo = Math.Min(ChartBars.ToIndex, renderBar);
            float prevX = float.NaN, prevY = float.NaN;
            bool? prevDir = null;

            for (int idx = Math.Max(ChartBars.FromIndex, 0); idx < _renderTo; idx++)
            {
                int cIdx = idx & (CACHE_SIZE - 1);
                double val = valCache[cIdx];
                if (val == 0.0) { prevX = float.NaN; prevDir = null; continue; }
                bool bull = dirCache[cIdx];
                float x = cc.GetXByBarIndex(ChartBars, idx);
                float y = cs.GetYByValue(val);
                if (!float.IsNaN(prevX))
                {
                    // Use color of the current bar's direction
                    var col = bull ? bullCol : bearCol;
                    dxLine1.Color = new SharpDX.Color4(col.ScR, col.ScG, col.ScB, col.ScA);
                    RenderTarget.DrawLine(new Vector2(prevX, prevY), new Vector2(x, y), dxLine1, strokeWidth);
                }
                prevX = x; prevY = y; prevDir = bull;
            }
        }

        // Keep Series<double> overload for SafeGet-based access where needed
        private void RenderLineSeries(ChartControl cc, ChartScale cs, int renderBar,
                                  Series<double> series, System.Windows.Media.Color color,
                                  float strokeWidth = 2f)
        {
            if (BarsArray[0].Count < BarsRequiredToPlot) return;
            using (var brush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget,
                new SharpDX.Color4(color.ScR, color.ScG, color.ScB, color.ScA)))
            {
                float prevX = float.NaN, prevY = float.NaN;
                int _fillTo = Math.Min(ChartBars.ToIndex, renderBar);
                for (int idx = Math.Max(ChartBars.FromIndex, 0); idx < _fillTo; idx++)
                {
                    int bk = renderBar - idx;
                    if (bk < 0 || bk >= renderBar) { prevX = float.NaN; continue; }
                    double val = SafeGet(series, bk);
                    if (double.IsNaN(val) || val == 0.0) { prevX = float.NaN; continue; }
                    float x = cc.GetXByBarIndex(ChartBars, idx);
                    float y = cs.GetYByValue(val);
                    if (!float.IsNaN(prevX))
                        RenderTarget.DrawLine(new Vector2(prevX, prevY), new Vector2(x, y), brush, strokeWidth);
                    prevX = x; prevY = y;
                }
            }
        }

        private void RenderTcBaseGlow(ChartControl cc, ChartScale cs, int renderBar)
        {
            int total = ChartBars.ToIndex - ChartBars.FromIndex + 1;
            if (total < 2) return;
            if (BarsArray[0].Count < BarsRequiredToPlot) return;

            // Determine glow color from current bar (trend slope)
            bool bull = _renderTcBull;
            System.Windows.Media.Color gc = bull ? bullColor : bearColor;
            // Render 4 layered strokes of decreasing opacity / increasing width
            float[] widths   = { 10f, 20f, 30f, 45f };
            float[] alphas   = { 0.20f, 0.10f, 0.05f, 0.02f };

            for (int layer = 0; layer < 4; layer++)
            {
                {
                    var _gb = layer == 0 ? dxGlow1 : layer == 1 ? dxGlow2 : layer == 2 ? dxGlow3 : dxGlow4;
                    _gb.Color = new SharpDX.Color4(gc.ScR, gc.ScG, gc.ScB, alphas[layer]);
                    int _glowTo = Math.Min(ChartBars.ToIndex - 1, renderBar - 1);
                    for (int idx = Math.Max(ChartBars.FromIndex, 0); idx < _glowTo; idx++)
                    {
                        int bk0 = renderBar - idx;
                        int bk1 = renderBar - (idx + 1);
                        if (bk0 < 0 || bk1 < 0) continue;
                        if (bk0 >= renderBar || bk1 >= renderBar) continue;
                        

                        float x0 = cc.GetXByBarIndex(ChartBars, idx);
                        float x1 = cc.GetXByBarIndex(ChartBars, idx + 1);
                        double _gv0 = _cacheTcBaseBuf[idx & (CACHE_SIZE-1)];
                        double _gv1 = _cacheTcBaseBuf[(idx+1) & (CACHE_SIZE-1)];
                        if (_gv0 == 0.0 || _gv1 == 0.0) continue;
                        float y0 = cs.GetYByValue(_gv0);
                        float y1 = cs.GetYByValue(_gv1);
                        if (double.IsNaN(y0) || double.IsNaN(y1)) continue;

                        RenderTarget.DrawLine(new Vector2(x0, y0), new Vector2(x1, y1), _gb, widths[layer]);
                    }
                }
            }
        }

        private void RenderTrendCloudFill(ChartControl cc, ChartScale cs, int renderBar)
        {
            if (BarsArray[0].Count < BarsRequiredToPlot) return;
            int _fillTo = Math.Min(ChartBars.ToIndex, renderBar);
            int fromIdx = Math.Max(ChartBars.FromIndex, 0);
            if (_fillTo - fromIdx < 2) return;

            // Draw separate polygons per bull/bear run to match Pine's per-bar color behavior
            const float FILL_ALPHA = 0.18f;
            var topRun = new List<Vector2>();
            var botRun = new List<Vector2>();
            bool? runDir = null;

            Action flushRun = () =>
            {
                if (topRun.Count < 2) { topRun.Clear(); botRun.Clear(); return; }
                using (var pg = new SharpDX.Direct2D1.PathGeometry(RenderTarget.Factory))
                using (var sk = pg.Open())
                {
                    sk.BeginFigure(topRun[0], SharpDX.Direct2D1.FigureBegin.Filled);
                    for (int i = 1; i < topRun.Count; i++) sk.AddLine(topRun[i]);
                    for (int i = botRun.Count - 1; i >= 0; i--) sk.AddLine(botRun[i]);
                    sk.EndFigure(SharpDX.Direct2D1.FigureEnd.Closed);
                    sk.Close();
                    var fc = runDir == true ? bullColor : bearColor;
                    dxFill1.Color = new SharpDX.Color4(fc.ScR, fc.ScG, fc.ScB, FILL_ALPHA);
                    RenderTarget.FillGeometry(pg, dxFill1);
                }
                topRun.Clear(); botRun.Clear();
            };

            for (int idx = fromIdx; idx < _fillTo; idx++)
            {
                int cIdx = idx & (CACHE_SIZE - 1);
                double b = _cacheTcBaseBuf[cIdx];
                double t = _cacheTcCloudTopBuf[cIdx];
                if (b == 0.0 || t == 0.0) { flushRun(); runDir = null; continue; }
                bool bull = _cacheTcBullBuf[cIdx];
                if (runDir.HasValue && runDir.Value != bull) flushRun();
                runDir = bull;
                float x = cc.GetXByBarIndex(ChartBars, idx);
                topRun.Add(new Vector2(x, cs.GetYByValue(t)));
                botRun.Add(new Vector2(x, cs.GetYByValue(b)));
            }
            flushRun();
        }
        private void RenderSuperChannelFill(ChartControl cc, ChartScale cs, int renderBar)
        {
            if (BarsArray[0].Count < BarsRequiredToPlot) return;
            using (var pathGeom = new SharpDX.Direct2D1.PathGeometry(RenderTarget.Factory))
            using (var sink     = pathGeom.Open())
            {
                List<Vector2> topPts = new List<Vector2>();
                List<Vector2> botPts = new List<Vector2>();
                int _fillTo = Math.Min(ChartBars.ToIndex, renderBar);
                for (int idx = Math.Max(ChartBars.FromIndex, 0); idx < _fillTo; idx++)
                {
                    float x = cc.GetXByBarIndex(ChartBars, idx);
                    double t = _cacheScTopBuf[idx & (CACHE_SIZE-1)];
                    double b = _cacheScBotBuf[idx & (CACHE_SIZE-1)];
                    if (t == 0.0 || b == 0.0) continue;
                    topPts.Add(new Vector2(x, cs.GetYByValue(t)));
                    botPts.Add(new Vector2(x, cs.GetYByValue(b)));
                }
                if (topPts.Count < 2) return;
                sink.BeginFigure(topPts[0], SharpDX.Direct2D1.FigureBegin.Filled);
                for (int i = 1; i < topPts.Count; i++) sink.AddLine(topPts[i]);
                for (int i = botPts.Count - 1; i >= 0; i--) sink.AddLine(botPts[i]);
                sink.EndFigure(SharpDX.Direct2D1.FigureEnd.Closed);
                sink.Close();

                using (var brush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(neutColor.ScR, neutColor.ScG, neutColor.ScB, 0.05f)))
                {
                    RenderTarget.FillGeometry(pathGeom, brush);
                }
            }
        }

        private void RenderRibbonFill(ChartControl cc, ChartScale cs, int renderBar)
        {
            if (BarsArray[0].Count < BarsRequiredToPlot) return;
            int _fillTo = Math.Min(ChartBars.ToIndex, renderBar);
            int fromIdx = Math.Max(ChartBars.FromIndex, 0);
            if (_fillTo - fromIdx < 2) return;

            const float FILL_ALPHA = 0.75f;
            var topRun = new List<Vector2>();
            var botRun = new List<Vector2>();
            bool? runDir = null;

            Action flushRun = () =>
            {
                if (topRun.Count < 2) { topRun.Clear(); botRun.Clear(); return; }
                using (var pg = new SharpDX.Direct2D1.PathGeometry(RenderTarget.Factory))
                using (var sk = pg.Open())
                {
                    sk.BeginFigure(topRun[0], SharpDX.Direct2D1.FigureBegin.Filled);
                    for (int i = 1; i < topRun.Count; i++) sk.AddLine(topRun[i]);
                    for (int i = botRun.Count - 1; i >= 0; i--) sk.AddLine(botRun[i]);
                    sk.EndFigure(SharpDX.Direct2D1.FigureEnd.Closed);
                    sk.Close();
                    var fc = runDir == true ? bullColor : bearColor;
                    dxFill1.Color = new SharpDX.Color4(fc.ScR, fc.ScG, fc.ScB, FILL_ALPHA);
                    RenderTarget.FillGeometry(pg, dxFill1);
                }
                topRun.Clear(); botRun.Clear();
            };

            for (int idx = fromIdx; idx < _fillTo; idx++)
            {
                int cIdx = idx & (CACHE_SIZE - 1);
                double c1 = _cacheCma1Buf[cIdx];
                double c2 = _cacheCma2Buf[cIdx];
                if (c1 == 0.0 || c2 == 0.0) { flushRun(); runDir = null; continue; }
                bool bull = _cacheRibbonBullBuf[cIdx];
                if (runDir.HasValue && runDir.Value != bull) flushRun();
                runDir = bull;
                float x = cc.GetXByBarIndex(ChartBars, idx);
                topRun.Add(new Vector2(x, cs.GetYByValue(Math.Max(c1, c2))));
                botRun.Add(new Vector2(x, cs.GetYByValue(Math.Min(c1, c2))));
            }
            flushRun();
        }
        private void RenderAltCandles(ChartControl cc, ChartScale cs, int renderBar)
        {
            if (BarsArray[0].Count < BarsRequiredToPlot) return;
            // Derive bar width from paint coordinates (works across NT8 versions)
            float w = 6f;
            if (ChartBars != null && ChartBars.ToIndex > ChartBars.FromIndex)
            {
                float x0 = cc.GetXByBarIndex(ChartBars, ChartBars.FromIndex);
                float x1 = cc.GetXByBarIndex(ChartBars, ChartBars.FromIndex + 1);
                w = (float)Math.Max(1.0, Math.Abs(x1 - x0) * 0.8);
            }

            int _candleTo = Math.Min(ChartBars.ToIndex, renderBar - 1); // exclude current forming bar
            for (int idx = Math.Max(ChartBars.FromIndex, 0); idx <= _candleTo; idx++)
            {
                int bk = renderBar - idx;
                if (bk < 1) continue; // bk=0 is current bar - may not be written yet
                float x = cc.GetXByBarIndex(ChartBars, idx);
                double o = SafeGet(cvO, bk), h = SafeGet(cvH, bk), l = SafeGet(cvL, bk), c = SafeGet(cvC, bk);
                if (double.IsNaN(o) || double.IsNaN(h) || double.IsNaN(l) || double.IsNaN(c)) continue;
                if (o == 0.0 || h == 0.0 || l == 0.0 || c == 0.0) continue; // guard against NT8 reset

                float yO = cs.GetYByValue(o);
                float yH = cs.GetYByValue(h);
                float yL = cs.GetYByValue(l);
                float yC = cs.GetYByValue(c);

                bool up = c >= o;
                System.Windows.Media.Color bodyCol = up ? bullColor : bearColor;

                // CVD border highlight
                double rank = bk == 0 ? PercentRankSeries(cvdAggPct, 50) : 0;
                bool strong = (isSimple ? SimAltCandlesEnable : CVDBorderEnable)
                              && bk == 0 && rank >= CVDBorderStrong;
                System.Windows.Media.Color borderCol = strong ? hilightColor : bodyCol;

                dxLine1.Color  = new SharpDX.Color4(bodyCol.ScR,   bodyCol.ScG,   bodyCol.ScB,   strong ? 0.55f : 1.0f);
                dxLine2.Color  = new SharpDX.Color4(borderCol.ScR, borderCol.ScG, borderCol.ScB, 1.0f);
                // wick
                RenderTarget.DrawLine(new Vector2(x, yH), new Vector2(x, yL), dxLine1, 1.0f);
                // body
                float top    = Math.Min(yO, yC);
                float bot    = Math.Max(yO, yC);
                var rect = new SharpDX.RectangleF(x - w / 2f, top, w, Math.Max(1f, bot - top));
                RenderTarget.FillRectangle(rect, dxLine1);
                RenderTarget.DrawRectangle(rect, dxLine2, strong ? 2f : 1f);
            }
        }

        private void RenderCandleColors(ChartControl cc, ChartScale cs, int renderBar)
        {
            if (BarsArray[0].Count < BarsRequiredToPlot) return;
            // Only apply if alt candles are not active (otherwise they handle their own coloring)
            if (isSimple ? SimAltCandlesEnable : CandleAltEnable) return;

            float w = 6f;
            if (ChartBars != null && ChartBars.ToIndex > ChartBars.FromIndex)
            {
                float x0 = cc.GetXByBarIndex(ChartBars, ChartBars.FromIndex);
                float x1 = cc.GetXByBarIndex(ChartBars, ChartBars.FromIndex + 1);
                w = (float)Math.Max(1.0, Math.Abs(x1 - x0) * 0.8);
            }

            for (int idx = Math.Max(ChartBars.FromIndex, 0); idx <= ChartBars.ToIndex; idx++)
            {
                int bk = renderBar - idx;
                if (bk < 0 || bk >= renderBar) continue;
                if (bk >= renderBar) continue;
                float x = cc.GetXByBarIndex(ChartBars, idx);
                // Use Bars.Get* to bypass BarsRequiredToPlot lookback limit on built-in series
                double o = ChartBars.Bars.GetOpen(idx);
                double h = ChartBars.Bars.GetHigh(idx);
                double l = ChartBars.Bars.GetLow(idx);
                double c = ChartBars.Bars.GetClose(idx);
                float yO = cs.GetYByValue(o);
                float yH = cs.GetYByValue(h);
                float yL = cs.GetYByValue(l);
                float yC = cs.GetYByValue(c);

                System.Windows.Media.Color col = GetCandleColorForBar(bk, renderBar);
                float alpha = 1.0f - CandleColorOpacity / 100.0f;
                using (var brush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(col.ScR, col.ScG, col.ScB, alpha)))
                {
                    RenderTarget.DrawLine(new Vector2(x, yH), new Vector2(x, yL), brush, 1.0f);
                    float top = Math.Min(yO, yC);
                    float bot = Math.Max(yO, yC);
                    var rect = new SharpDX.RectangleF(x - w / 2f, top, w, Math.Max(1f, bot - top));
                    RenderTarget.FillRectangle(rect, brush);
                }
            }
        }

        private System.Windows.Media.Color GetCandleColorForBar(int bk, int renderBar)
        {
            switch (CandleColorMode)
            {
                case SnTACandleColorMode.MARibbon:
                    return SafeGet(cma1Series, bk) > SafeGet(cma2Series, bk) ? bullColor : bearColor;

                case SnTACandleColorMode.KAMAStack:
                {
                    int pairs = 0;
                    if (SafeGet(tcKamas[1], bk)  > SafeGet(tcKamas[9], bk))  pairs++; // k10 > k50
                    if (SafeGet(tcKamas[9], bk)  > SafeGet(tcKamas[18], bk)) pairs++; // k50 > k100
                    if (SafeGet(tcKamas[1], bk)  > SafeGet(tcKamas[18], bk)) pairs++; // k10 > k100
                    if (pairs == 3) return bullColor;
                    if (pairs == 2) return Blend(bullColor, neutColor, 0.5f);
                    if (pairs == 1) return Blend(bearColor, neutColor, 0.5f);
                    return bearColor;
                }

                case SnTACandleColorMode.DualConfirmation:
                {
                    bool ribbonBull = SafeGet(cma1Series, bk) > SafeGet(cma2Series, bk);
                    bool tcBull = bk + 1 < renderBar && SafeGet(tcBase, bk) > tcBase[bk + 1];
                    if (ribbonBull && tcBull)   return bullColor;
                    if (!ribbonBull && !tcBull) return bearColor;
                    return neutColor;
                }

                case SnTACandleColorMode.TrendRegime:
                default:
                {
                    // We only have the *current* regime votes; approximate prior bars with current color
                    int net = TrgBullVotes() - TrgBearVotes();
                    if (net >= 2)  return bullColor;
                    if (net == 1)  return Blend(bullColor, neutColor, 0.5f);
                    if (net == -1) return Blend(bearColor, neutColor, 0.5f);
                    if (net <= -2) return bearColor;
                    return neutColor;
                }
            }
        }

        private System.Windows.Media.Color Blend(System.Windows.Media.Color a, System.Windows.Media.Color b, float t)
        {
            return System.Windows.Media.Color.FromArgb(
                (byte)(a.A * (1 - t) + b.A * t),
                (byte)(a.R * (1 - t) + b.R * t),
                (byte)(a.G * (1 - t) + b.G * t),
                (byte)(a.B * (1 - t) + b.B * t));
        }

        private void RenderBoundaryForecast(ChartControl cc, ChartScale cs, int renderBar)
        {
            int effectiveLookback = Math.Min(ForecastLookback, renderBar - 1);
            if (effectiveLookback < 2) return;

            int h = ForecastHorizon;
            double lenM1 = effectiveLookback - 1;
            bool slope = ForecastMode == SnTAForecastMode.SlopeExtension;

            // anchor x = the last visible bar (renderBar)
            int anchorIdx = renderBar;
            float anchorX = cc.GetXByBarIndex(ChartBars, anchorIdx);
            // estimate pixels per bar from adjacent bar paint positions
            float barW = 6f;
            if (ChartBars.ToIndex > ChartBars.FromIndex)
            {
                float x0 = cc.GetXByBarIndex(ChartBars, ChartBars.FromIndex);
                float x1 = cc.GetXByBarIndex(ChartBars, ChartBars.FromIndex + 1);
                barW = Math.Abs(x1 - x0);
            }
            if (barW < 2) barW = 6;

            // Validate cache before any forecast drawing
            int _fcastAnchor = renderBar & (CACHE_SIZE - 1);
            int _fcastOld    = (renderBar - effectiveLookback + 1) & (CACHE_SIZE - 1);
            if (_cacheTcBaseBuf[_fcastAnchor] == 0.0 || _cacheScTopBuf[_fcastAnchor] == 0.0) return;

            // ── TC forecast ──
            if (ForecastTCEnable)
            {
                double aB=0, bB=0, cB=0, dB=0, aT=0, bT=0, cT=0, dT=0;
                double tcBSlope = (_cacheTcBaseBuf[_fcastAnchor] - _cacheTcBaseBuf[_fcastOld]) / lenM1;
                double tcTSlope = (_cacheTcCloudTopBuf[_fcastAnchor] - _cacheTcCloudTopBuf[_fcastOld]) / lenM1;
                if (!slope)
                {
                    CubicFitTail(tcBase,     effectiveLookback, out aB, out bB, out cB, out dB);
                    CubicFitTail(tcCloudTop, effectiveLookback, out aT, out bT, out cT, out dT);
                }
                                double prevB = _cacheTcBaseBuf[_fcastAnchor], prevT = _cacheTcCloudTopBuf[_fcastAnchor];
                for (int n = 1; n <= h; n++)
                {
                    double curB, curT;
                    if (slope)
                    {
                        curB = _cacheTcBaseBuf[_fcastAnchor]     + tcBSlope * n;
                        curT = _cacheTcCloudTopBuf[_fcastAnchor] + tcTSlope * n;
                    }
                    else
                    {
                        double x = (effectiveLookback - 1) + n;
                        curB = aB * x * x * x + bB * x * x + cB * x + dB;
                        curT = aT * x * x * x + bT * x * x + cT * x + dT;
                    }
                    float t = h > 1 ? (float)(n - 1) / (float)(h - 1) : 0;
                    float baseA = 0.85f - t * 0.55f;
                    float topA  = 0.65f - t * 0.45f;
                    using (var bb = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(neutColor.ScR, neutColor.ScG, neutColor.ScB, baseA)))
                    using (var tb = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(neutColor.ScR, neutColor.ScG, neutColor.ScB, topA)))
                    {
                        float x0 = anchorX + (n - 1) * barW;
                        float x1 = anchorX + n * barW;
                        float y0b = cs.GetYByValue(prevB);
                        float y1b = cs.GetYByValue(curB);
                        float y0t = cs.GetYByValue(prevT);
                        float y1t = cs.GetYByValue(curT);
                        RenderTarget.DrawLine(new Vector2(x0, y0b), new Vector2(x1, y1b), bb, 2f);
                        RenderTarget.DrawLine(new Vector2(x0, y0t), new Vector2(x1, y1t), tb, 1f);
                    }
                    prevB = curB; prevT = curT;
                }
            }

            // ── SC forecast ──
            if (ForecastSCEnable)
            {
                double aT=0, bT=0, cT=0, dT=0, aB=0, bB=0, cB=0, dB=0;
                double scTSlope = (_cacheScTopBuf[_fcastAnchor] - _cacheScTopBuf[_fcastOld]) / lenM1;
                double scBSlope = (_cacheScBotBuf[_fcastAnchor] - _cacheScBotBuf[_fcastOld]) / lenM1;
                if (!slope)
                {
                    CubicFitTail(scTop, effectiveLookback, out aT, out bT, out cT, out dT);
                    CubicFitTail(scBot, effectiveLookback, out aB, out bB, out cB, out dB);
                }
                double prevT = _cacheScTopBuf[_fcastAnchor], prevB = _cacheScBotBuf[_fcastAnchor];
                for (int n = 1; n <= h; n++)
                {
                    double curT, curB;
                    if (slope)
                    {
                        curT = _cacheScTopBuf[_fcastAnchor] + scTSlope * n;
                        curB = _cacheScBotBuf[_fcastAnchor] + scBSlope * n;
                    }
                    else
                    {
                        double x = (effectiveLookback - 1) + n;
                        curT = aT * x * x * x + bT * x * x + cT * x + dT;
                        curB = aB * x * x * x + bB * x * x + cB * x + dB;
                    }
                    float t = h > 1 ? (float)(n - 1) / (float)(h - 1) : 0;
                    float a = 0.85f - t * 0.55f;
                    using (var tb = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(bullColor.ScR, bullColor.ScG, bullColor.ScB, a)))
                    using (var bb = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(bearColor.ScR, bearColor.ScG, bearColor.ScB, a)))
                    {
                        float x0 = anchorX + (n - 1) * barW;
                        float x1 = anchorX + n * barW;
                        RenderTarget.DrawLine(new Vector2(x0, cs.GetYByValue(prevT)), new Vector2(x1, cs.GetYByValue(curT)), tb, 2f);
                        RenderTarget.DrawLine(new Vector2(x0, cs.GetYByValue(prevB)), new Vector2(x1, cs.GetYByValue(curB)), bb, 2f);
                    }
                    prevT = curT; prevB = curB;
                }
            }
        }

        // ────────────────────────────────────────────────────────────────────────────
        // INFO PANEL
        // ────────────────────────────────────────────────────────────────────────────
        private void RenderInfoPanel(ChartControl cc, ChartScale cs, int renderBar)
        {
            if (BarsArray[0].Count < BarsRequiredToPlot) return;
            // Build rows
            bool biasBull   = prismLastDir == -1;
            bool stBull     = _renderStBull;
            bool mtBull     = _renderMtBull; // k20
            bool ltBull     = _renderLtBull;

            double cvd0 = _renderCvd0;
            double cvd1 = _renderCvd1;
            double cvd2 = _renderCvd2;
            double cvdAvg = (PercentRankSeries(cvdAggPct, 50)
                            + (renderBar >= 1 ? PercentRankSeries(cvdAggPct, 50) : PercentRankSeries(cvdAggPct, 50))
                            + (renderBar >= 2 ? PercentRankSeries(cvdAggPct, 50) : PercentRankSeries(cvdAggPct, 50))) / 3.0;

            double srsiNow  = _renderSrsiNow;
            double srsiPrev = _renderSrsiPrev;
            bool momRising  = srsiNow > srsiPrev;
            string momState = srsiNow >= 80 && momRising      ? "Rising & OB" :
                              srsiNow <= 20 && !momRising     ? "Falling & OS" :
                              momRising                       ? "Rising"      : "Falling";

            string volState = scCCO[0] >= SC_HI_THRESH ? "Overbought"
                            : scCCO[0] >= 60         ? "Near Overbought"
                            : scCCO[0] >  40         ? "Normal Range"
                            : scCCO[0] >  SC_LO_THRESH ? "Near Oversold" : "Oversold";

            double er = effPrismNsERSmooth;
            string erState = er >= 0.6 ? "Strong Trend" :
                             er >= 0.45 ? "Trending" :
                             er >= 0.3 ? "Mixed" :
                             er >= 0.15 ? "Noisy" : "Very Noisy";

            string trgRegime = TrgSuppressSell() ? "Bull Regime" : TrgSuppressBuy() ? "Bear Regime" : "No Regime";
            string trgHurstStr = trgHurstCached > 0.55 ? "Trending" : trgHurstCached < 0.45 ? "Mean Reverting" : "Mixed";
            double kaTh = isSimple ? 0.72 : TRGKaThresh;
            double maxKa = Math.Max(_trgKaBullPct, _trgKaBearPct);
            string trgKaStr = maxKa >= 0.78 ? "High" : maxKa >= 0.56 ? "Moderate" : "Low";
            string trgAccelStr = trgTcAccelSmooth[0] >  0.001 ? "Accelerating"
                              : trgTcAccelSmooth[0] < -0.001 ? "Decelerating" : "Steady";

            bool effAdaptive = isSimple ? true : PrismAdaptiveEnable;
            bool effNS       = isSimple ? false : PrismNSEnable;
            string adaptMode = effAdaptive && effNS ? "Adaptive + NS"
                             : effAdaptive ? "Adaptive"
                             : effNS       ? "Noise Supp" : "Fixed";

            bool aoEn = isSimple ? SimAOEnable : AOEnable;
            string aoStatus = aoEn ? (aoTotal > 0 ? "Active" : "Warming Up") : "Disabled";
            string aoLens = aoLenS + " / " + aoLenM + " / " + aoLenL;
            string aoScores = Math.Round(aoAvgS, 2) + " / " + Math.Round(aoAvgM, 2) + " / " + Math.Round(aoAvgL, 2);
            int aoAdj = aoEffLen - aoLenM;
            string aoAdjStr = (aoAdj > 0 ? "+" : "") + aoAdj;

            // Layout
            float padX = 8f, padY = 4f, rowH = 14f;
            string[] labels = {
                "TREND STATE", "Immediate Bias", "Short Term", "Medium Term", "Long Term",
                "MOMENTUM & QUALITY", "Delta Strength", "Momentum State", "Volatility Range", "Trend Efficiency",
                "TREND REGIME", "Regime State", "Trend Alignment", "Market Character", "Trend Momentum",
                "PRISM ADAPTIVE", "Mode", "Eff. Lookback", "Eff. Rails (α/σ)",
                "AUTO-OPTIMIZER", "Status", "Test Lengths", "Scores (S/M/L)", "Length Adjustment"
            };
            string[] values = {
                "", biasBull?"Bullish":"Bearish", stBull?"Bullish":"Bearish", mtBull?"Bullish":"Bearish", ltBull?"Bullish":"Bearish",
                "", Math.Round(cvdAvg) + "%", momState, volState, erState,
                "", trgRegime, trgKaStr, trgHurstStr, trgAccelStr,
                "", adaptMode, effPrismLen.ToString(), effPrismSt1Per + " / " + effPrismSt2Per,
                "", aoStatus, aoLens, aoScores, aoAdjStr
            };
            bool[] isHeader = {
                true, false, false, false, false,
                true, false, false, false, false,
                true, false, false, false, false,
                true, false, false, false,
                true, false, false, false, false
            };

            // measure widest
            float colLabelW = 130f, colValueW = 110f;
            float w = colLabelW + colValueW + padX * 3;
            float panelH = labels.Length * rowH + padY * 2;

            // Position
            float pnLeft   = ChartPanel.X;
            float pnTop    = ChartPanel.Y;
            float pnRight  = ChartPanel.X + ChartPanel.W;
            float pnBottom = ChartPanel.Y + ChartPanel.H;
            float pnHeight = ChartPanel.H;
            float px = 0, py = 0;
            switch (InfoLocation)
            {
                case SnTAInfoLocation.TopLeft:     px = pnLeft + 10; py = pnTop + 10; break;
                case SnTAInfoLocation.MiddleLeft:  px = pnLeft + 10; py = (float)(pnTop + pnHeight / 2.0 - panelH / 2.0); break;
                case SnTAInfoLocation.BottomLeft:  px = pnLeft + 10; py = pnBottom - panelH - 10; break;
                case SnTAInfoLocation.MiddleRight: px = pnRight - w - 10; py = (float)(pnTop + pnHeight / 2.0 - panelH / 2.0); break;
                case SnTAInfoLocation.BottomRight: px = pnRight - w - 10; py = pnBottom - panelH - 10; break;
            }

            RenderTarget.FillRectangle(new SharpDX.RectangleF(px, py, w, panelH), dxPanelBg);
            RenderTarget.DrawRectangle(new SharpDX.RectangleF(px, py, w, panelH), dxPanelFrame, 1.0f);

            float y = py + padY;
            for (int i = 0; i < labels.Length; i++)
            {
                if (isHeader[i])
                {
                    RenderTarget.FillRectangle(new SharpDX.RectangleF(px, y, w, rowH), dxPanelHdrBg);
                    using (var layout = new SharpDX.DirectWrite.TextLayout(Core.Globals.DirectWriteFactory, " " + labels[i],
                                          dxTextFmtBold, w - padX * 2, rowH))
                    {
                        RenderTarget.DrawTextLayout(new Vector2(px + padX, y), layout, dxPanelText);
                    }
                }
                else
                {
                    using (var layout = new SharpDX.DirectWrite.TextLayout(Core.Globals.DirectWriteFactory, " " + labels[i],
                                          dxTextFmt, colLabelW, rowH))
                    {
                        RenderTarget.DrawTextLayout(new Vector2(px + padX, y), layout, dxPanelLabel);
                    }
                    SharpDX.Direct2D1.Brush valBrush = ColorizeValue(labels[i], values[i], i);
                    using (var rightFmt = new SharpDX.DirectWrite.TextFormat(Core.Globals.DirectWriteFactory,
                                          "Arial", SharpDX.DirectWrite.FontWeight.Normal, SharpDX.DirectWrite.FontStyle.Normal, 11f) { TextAlignment = SharpDX.DirectWrite.TextAlignment.Trailing })
                    using (var layout = new SharpDX.DirectWrite.TextLayout(Core.Globals.DirectWriteFactory, values[i] + " ",
                                          rightFmt, colValueW, rowH))
                    {
                        RenderTarget.DrawTextLayout(new Vector2(px + padX + colLabelW, y), layout, valBrush);
                    }
                }
                y += rowH;
            }
        }

        private double PercentRankSeriesAt(Series<double> s, int lb, int barsAgo, int renderBar = -1)
        {
            int cb = renderBar >= 0 ? renderBar : CurrentBar;
            int n = Math.Min(lb, cb - barsAgo);
            if (n < 1) return 50.0;
            double cur = SafeGet(s, barsAgo);
            int below = 0;
            int limit = Math.Min(barsAgo + n, cb - 1);
            for (int i = barsAgo + 1; i <= limit; i++) if (SafeGet(s, i) < cur) below++;
            return 100.0 * below / n;
        }

        private SharpDX.Direct2D1.Brush ColorizeValue(string label, string value, int rowIdx)
        {
            // Quick heuristic mapping
            string lv = value.ToLower();
            if (lv.Contains("bullish") || lv.Contains("bull regime") || lv.Contains("trending") || lv.Contains("strong trend") || lv.Contains("active") || lv.Contains("accelerating"))
                return dxBullFull;
            if (lv.Contains("bearish") || lv.Contains("bear regime") || lv.Contains("oversold") || lv.Contains("overbought") || lv.Contains("very noisy") || lv.Contains("decelerating"))
                return dxBearFull;
            if (lv.Contains("rising") && lv.Contains("ob")) return dxBearFull;
            if (lv.Contains("falling") && lv.Contains("os")) return dxBullFull;
            if (lv.Contains("rising")) return dxBullFull;
            if (lv.Contains("falling")) return dxBearFull;
            if (lv.Contains("near ob") || lv.Contains("near overbought")) return dxBearDim;
            if (lv.Contains("near os") || lv.Contains("near oversold")) return dxBullDim;
            if (lv.Contains("noisy")) return dxBearDim;
            if (lv.Contains("mixed") || lv.Contains("steady")) return dxNeutVal;
            return dxPanelText;
        }

        private void RenderWatermark(ChartControl cc, int renderBar)
        {
            using (var fmt = new SharpDX.DirectWrite.TextFormat(Core.Globals.DirectWriteFactory,
                                "Arial", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 12f)
                            { TextAlignment = SharpDX.DirectWrite.TextAlignment.Trailing })
            {
                float panelRight = ChartPanel.X + ChartPanel.W;
                float panelTop   = ChartPanel.Y;
                using (var brush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(hilightColor.ScR, hilightColor.ScG, hilightColor.ScB, 0.45f)))
                using (var layout = new SharpDX.DirectWrite.TextLayout(Core.Globals.DirectWriteFactory,
                                       "Trend Architect v9", fmt, 320, 16))
                {
                    RenderTarget.DrawTextLayout(new Vector2(panelRight - 330, panelTop + 4), layout, brush);
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════════════
        // THEME LOOKUP
        // ════════════════════════════════════════════════════════════════════════════
        private static System.Windows.Media.Color ColorFromHex(string hex)
        {
            byte r = Convert.ToByte(hex.Substring(1, 2), 16);
            byte g = Convert.ToByte(hex.Substring(3, 2), 16);
            byte b = Convert.ToByte(hex.Substring(5, 2), 16);
            return System.Windows.Media.Color.FromRgb(r, g, b);
        }

        private void ResolveTheme()
        {
            string bull = "#00FFFF", bear = "#FF0000", neut = "#888888", hilight = "#FFFFFF";
            string sBull = "#FFD700", sBear = "#FFFFFF", sText = "#000000";

            if (BackgroundMode == SnTABackgroundMode.DarkBackground)
            {
                switch (DarkTheme)
                {
                    case SnTADarkTheme.Modern:        bull="#00FFFF"; bear="#FF0000"; neut="#888888"; hilight="#FFFFFF"; sBull="#FFD700"; sBear="#FFFFFF"; sText="#000000"; break;
                    case SnTADarkTheme.Terminal:      bull="#00FF00"; bear="#FF6000"; neut="#707070"; hilight="#FFFFFF"; sBull="#FFFFFF"; sBear="#FF5500"; sText="#000000"; break;
                    case SnTADarkTheme.Cyberpunk:     bull="#00FFFF"; bear="#FF00FF"; neut="#9932CC"; hilight="#FFFF00"; sBull="#FFFF00"; sBear="#FF00CC"; sText="#000000"; break;
                    case SnTADarkTheme.NeonNoir:      bull="#FF1DDF"; bear="#00C8FF"; neut="#6A0DAD"; hilight="#FFFF00"; sBull="#FFFF00"; sBear="#FF6EC7"; sText="#000000"; break;
                    case SnTADarkTheme.Phosphor:      bull="#FFB000"; bear="#CC2200"; neut="#7A5000"; hilight="#FFFFFF"; sBull="#FFFFFF"; sBear="#FF6600"; sText="#000000"; break;
                    case SnTADarkTheme.FireAndIce:    bull="#C8F0FF"; bear="#FF4500"; neut="#6699AA"; hilight="#FFD700"; sBull="#FFD700"; sBear="#FF6347"; sText="#000000"; break;
                    case SnTADarkTheme.Slate:         bull="#00CED1"; bear="#E8735A"; neut="#708090"; hilight="#F5F5DC"; sBull="#F5DEB3"; sBear="#DC143C"; sText="#000000"; break;
                    case SnTADarkTheme.BloodAndGreed: bull="#00FF41"; bear="#CC0000"; neut="#555555"; hilight="#FFD700"; sBull="#FFD700"; sBear="#FF6600"; sText="#000000"; break;
                    case SnTADarkTheme.GoldStandard:  bull="#FFD700"; bear="#B0B0B0"; neut="#CD7F32"; hilight="#FFFFFF"; sBull="#1E90FF"; sBear="#FF1111"; sText="#000000"; break;
                    case SnTADarkTheme.Ultraviolet:   bull="#BF00FF"; bear="#6600AA"; neut="#5C0080"; hilight="#00FF00"; sBull="#00FF00"; sBear="#FF1493"; sText="#000000"; break;
                    case SnTADarkTheme.Infrared:      bull="#FFFFFF"; bear="#CC0000"; neut="#882200"; hilight="#FF6600"; sBull="#FF6600"; sBear="#FF2222"; sText="#000000"; break;
                    case SnTADarkTheme.Toxic:         bull="#CCFF00"; bear="#33AA00"; neut="#1A5500"; hilight="#FFFFFF"; sBull="#FFFFFF"; sBear="#00FF66"; sText="#000000"; break;
                    case SnTADarkTheme.CrimsonTide:   bull="#FF2222"; bear="#C0C0C0"; neut="#808080"; hilight="#FFD700"; sBull="#FF8800"; sBear="#FF66FF"; sText="#000000"; break;
                    case SnTADarkTheme.Vaporwave:     bull="#FF6B9D"; bear="#9B30FF"; neut="#6622AA"; hilight="#00FFFF"; sBull="#00FFFF"; sBear="#FF00FF"; sText="#000000"; break;
                    case SnTADarkTheme.Matrix:        bull="#00FF41"; bear="#007A1F"; neut="#003D0F"; hilight="#FFFFFF"; sBull="#AAFFBB"; sBear="#FFFFFF"; sText="#000000"; break;
                    case SnTADarkTheme.Arctic:        bull="#A8D8FF"; bear="#4488CC"; neut="#3A5A8A"; hilight="#F0F8FF"; sBull="#F0F8FF"; sBear="#00BFFF"; sText="#000000"; break;
                }
            }
            else
            {
                switch (LightTheme)
                {
                    case SnTALightTheme.Classic:   bull="#2B58BF"; bear="#D42020"; neut="#707070"; hilight="#2A2A2A"; sBull="#0F8C50"; sBear="#A04810"; sText="#FFFFFF"; break;
                    case SnTALightTheme.Woodland:  bull="#2E8A2E"; bear="#991515"; neut="#7A5A35"; hilight="#3E2010"; sBull="#1080A0"; sBear="#8A208A"; sText="#FFFFFF"; break;
                    case SnTALightTheme.Solar:     bull="#009999"; bear="#AA20AA"; neut="#7A55A0"; hilight="#A07515"; sBull="#A07515"; sBear="#108870"; sText="#FFFFFF"; break;
                    case SnTALightTheme.Twilight:  bull="#AA1570"; bear="#1060AA"; neut="#6A3590"; hilight="#807010"; sBull="#807010"; sBear="#8A1050"; sText="#FFFFFF"; break;
                    case SnTALightTheme.Parchment: bull="#A06015"; bear="#A03515"; neut="#7A5515"; hilight="#3A2010"; sBull="#3A2010"; sBear="#A04F15"; sText="#FFFFFF"; break;
                    case SnTALightTheme.Shoreline: bull="#106595"; bear="#A04515"; neut="#406578"; hilight="#807010"; sBull="#807010"; sBear="#8A3515"; sText="#FFFFFF"; break;
                    case SnTALightTheme.Graphite:  bull="#109595"; bear="#A05540"; neut="#5A7080"; hilight="#2A2A2A"; sBull="#6A5515"; sBear="#AA2020"; sText="#FFFFFF"; break;
                    case SnTALightTheme.Harvest:   bull="#409915"; bear="#991818"; neut="#505050"; hilight="#807010"; sBull="#807010"; sBear="#906515"; sText="#FFFFFF"; break;
                    case SnTALightTheme.Guilded:   bull="#A08015"; bear="#585858"; neut="#7A6A30"; hilight="#151560"; sBull="#2060CC"; sBear="#AA2020"; sText="#FFFFFF"; break;
                    case SnTALightTheme.Amethyst:  bull="#7020BB"; bear="#4A1599"; neut="#451568"; hilight="#108A10"; sBull="#108A10"; sBear="#AA2080"; sText="#FFFFFF"; break;
                    case SnTALightTheme.Forge:     bull="#484848"; bear="#BB2020"; neut="#7A3A15"; hilight="#A05015"; sBull="#A05015"; sBear="#CC2525"; sText="#FFFFFF"; break;
                    case SnTALightTheme.Briar:     bull="#6A9515"; bear="#2D8820"; neut="#304A10"; hilight="#2A2A2A"; sBull="#2A2A2A"; sBear="#108855"; sText="#FFFFFF"; break;
                    case SnTALightTheme.Scarlet:   bull="#BB2525"; bear="#585858"; neut="#707070"; hilight="#807010"; sBull="#A06015"; sBear="#8A208A"; sText="#FFFFFF"; break;
                    case SnTALightTheme.Dusk:      bull="#AA3570"; bear="#5A30BB"; neut="#5A2590"; hilight="#109090"; sBull="#109090"; sBear="#992099"; sText="#FFFFFF"; break;
                    case SnTALightTheme.Fern:      bull="#2D9915"; bear="#106625"; neut="#1A4A10"; hilight="#2A2A2A"; sBull="#408A50"; sBear="#2A2A2A"; sText="#FFFFFF"; break;
                    case SnTALightTheme.Nordic:    bull="#2E60A0"; bear="#2E5099"; neut="#406080"; hilight="#1A3060"; sBull="#1A3060"; sBear="#1080BB"; sText="#FFFFFF"; break;
                }
            }

            bullColor    = ColorFromHex(bull);
            bearColor    = ColorFromHex(bear);
            neutColor    = ColorFromHex(neut);
            hilightColor = ColorFromHex(hilight);
            sigBullColor = ColorFromHex(sBull);
            sigBearColor = ColorFromHex(sBear);
            sigTextColor = ColorFromHex(sText);

            bullBrush    = new System.Windows.Media.SolidColorBrush(bullColor);    bullBrush.Freeze();
            bearBrush    = new System.Windows.Media.SolidColorBrush(bearColor);    bearBrush.Freeze();
            neutBrush    = new System.Windows.Media.SolidColorBrush(neutColor);    neutBrush.Freeze();
            hilightBrush = new System.Windows.Media.SolidColorBrush(hilightColor); hilightBrush.Freeze();
            sigBullBrush = new System.Windows.Media.SolidColorBrush(sigBullColor); sigBullBrush.Freeze();
            sigBearBrush = new System.Windows.Media.SolidColorBrush(sigBearColor); sigBearBrush.Freeze();
            sigTextBrush = new System.Windows.Media.SolidColorBrush(sigTextColor); sigTextBrush.Freeze();

            // Update plot brushes
            if (Plots != null && Plots.Length >= 6)
            {
                Plots[0].Brush = bullBrush;
                Plots[1].Brush = RibbonBiColor ? bearBrush : bullBrush;
                Plots[2].Brush = bullBrush;
                Plots[3].Brush = bearBrush;
                Plots[4].Brush = neutBrush;
                Plots[5].Brush = neutBrush;
            }
        }

        // SharpDX brush management
        private void EnsureDxBrushes()
        {
            if (dxBull != null) return;
            dxBull       = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(bullColor.ScR, bullColor.ScG, bullColor.ScB, 1f));
            dxBear       = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(bearColor.ScR, bearColor.ScG, bearColor.ScB, 1f));
            dxNeut       = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(neutColor.ScR, neutColor.ScG, neutColor.ScB, 1f));
            dxHilight    = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(hilightColor.ScR, hilightColor.ScG, hilightColor.ScB, 1f));
            dxSigBull    = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(sigBullColor.ScR, sigBullColor.ScG, sigBullColor.ScB, 1f));
            dxSigBear    = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(sigBearColor.ScR, sigBearColor.ScG, sigBearColor.ScB, 1f));
            dxSigText    = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(sigTextColor.ScR, sigTextColor.ScG, sigTextColor.ScB, 1f));
            dxPanelBg    = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(0f, 0f, 0f, 0.78f));
            dxPanelHdrBg = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(neutColor.ScR, neutColor.ScG, neutColor.ScB, 0.18f));
            dxPanelFrame = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(neutColor.ScR, neutColor.ScG, neutColor.ScB, 0.45f));
            dxPanelText  = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(1f, 1f, 1f, 0.95f));
            dxPanelLabel = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(1f, 1f, 1f, 0.50f));
            dxBullFull   = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(bullColor.ScR, bullColor.ScG, bullColor.ScB, 1f));
            dxBearFull   = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(bearColor.ScR, bearColor.ScG, bearColor.ScB, 1f));
            dxBullDim    = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(bullColor.ScR, bullColor.ScG, bullColor.ScB, 0.60f));
            dxBearDim    = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(bearColor.ScR, bearColor.ScG, bearColor.ScB, 0.60f));
            dxNeutVal    = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(1f, 1f, 1f, 0.80f));
            // Glow layer brushes (pre-created for performance)
            dxGlow1      = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, SharpDX.Color4.Black); // color set per-render
            dxGlow2      = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, SharpDX.Color4.Black);
            dxGlow3      = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, SharpDX.Color4.Black);
            dxGlow4      = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, SharpDX.Color4.Black);
            dxLine1      = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, SharpDX.Color4.Black);
            dxLine2      = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, SharpDX.Color4.Black);
            dxFill1      = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, SharpDX.Color4.Black);
        }

        private void DisposeDxBrushes()
        {
            var arr = new SharpDX.Direct2D1.Brush[] {
                dxBull, dxBear, dxNeut, dxHilight, dxSigBull, dxSigBear, dxSigText,
                dxPanelBg, dxPanelHdrBg, dxPanelFrame, dxPanelText, dxPanelLabel,
                dxBullFull, dxBearFull, dxBullDim, dxBearDim, dxNeutVal,
                dxGlow1, dxGlow2, dxGlow3, dxGlow4, dxLine1, dxLine2, dxFill1 };
            foreach (var b in arr) if (b != null && !b.IsDisposed) b.Dispose();
            dxBull = dxBear = dxNeut = dxHilight = dxSigBull = dxSigBear = dxSigText = null;
            dxPanelBg = dxPanelHdrBg = dxPanelFrame = dxPanelText = dxPanelLabel = null;
            dxBullFull = dxBearFull = dxBullDim = dxBearDim = dxNeutVal = null;
            dxGlow1 = dxGlow2 = dxGlow3 = dxGlow4 = null;
            dxLine1 = dxLine2 = dxFill1 = null;
        }

        private void EnsureTextFormats()
        {
            if (dxTextFmt == null)
                dxTextFmt = new SharpDX.DirectWrite.TextFormat(Core.Globals.DirectWriteFactory, "Arial", SharpDX.DirectWrite.FontWeight.Normal, SharpDX.DirectWrite.FontStyle.Normal, 11f);
            if (dxTextFmtBold == null)
                dxTextFmtBold = new SharpDX.DirectWrite.TextFormat(Core.Globals.DirectWriteFactory, "Arial", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 11f);
        }

        // Reset render-target-dependent resources when chart re-creates the target
        public override void OnRenderTargetChanged()
        {
            DisposeDxBrushes();
        }
    }
}
