// ============================================================================
// TrendArchitect — QuantTower Port
// ============================================================================
//
// Original Pine Script author credit retained from the NT8 source:
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
//  Original Pine Script © its author, MPL-2.0.
//  NinjaTrader port by Jason (@_hawkeye_13) / RedTail Indicators.
//  QuantTower port: open source adaptation.
//
// ============================================================================
// PORTING NOTES
// -------------
// This file ports TrendArchitect.cs from NinjaTrader 8 to QuantTower.
// QuantTower uses a different indicator API but the same C#/.NET runtime.
//
// Key API mappings:
//   NT8                          → QuantTower
//   ─────────────────────────────────────────
//   Indicator base class         → Indicator (TradingPlatform.BusinessLayer)
//   OnBarUpdate()                → OnUpdate(UpdateArgs args)
//   OnStateChange(SetDefaults)   → constructor + OnInit()
//   OnStateChange(DataLoaded)    → OnInit()
//   OnStateChange(Terminated)    → OnClear() / Dispose()
//   ISeries<double>              → IList<double> / manual ring buffer
//   Series<double>               → double[] ring buffer (manual)
//   ATR(n)[0]                    → calculated inline
//   SMA(n)[0]                    → calculated inline
//   EMA(n)[0]                    → calculated inline
//   RSI(n,smooth)[0]             → calculated inline
//   CCI(n)[0]                    → calculated inline
//   MFI(n)[0]                    → calculated inline
//   LinReg(src,n)[0]             → calculated inline
//   Draw.Dot / Draw.Text         → AddLineSeries output values
//   ChartControl / ChartScale    → IChartRenderer (OnPaintChart)
//   SharpDX rendering            → System.Drawing / QuantTower renderer
//   AddPlot(...)                 → AddLineSeries(...)
//   Values[n][0]                 → lineSeries[n].SetValue(...)
//
// Signal outputs (the key values TrendArchitectMQPanel watches):
//   prismIsBull   → LineSeries index 0 (non-zero = bull signal)
//   prismIsBear   → LineSeries index 1 (non-zero = bear signal)
//   prismIsMqBull → LineSeries index 2 (non-zero = MQ bull dot)
//   prismIsMqBear → LineSeries index 3 (non-zero = MQ bear dot)
//   CMA1          → LineSeries index 4 (MA ribbon line 1)
//   CMA2          → LineSeries index 5 (MA ribbon line 2)
//   SC Top        → LineSeries index 6 (Super Channel top)
//   SC Bottom     → LineSeries index 7 (Super Channel bottom)
//   TC Base       → LineSeries index 8 (Trend Cloud base)
//   TC Top        → LineSeries index 9 (Trend Cloud top)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using TradingPlatform.BusinessLayer;

namespace TrendArchitectQT
{
    // ── Enums ─────────────────────────────────────────────────────────────────
    public enum TASettingsMode    { Simple, Advanced }
    public enum TABackgroundMode  { DarkBackground, LightBackground }
    public enum TADarkTheme       { Modern, Terminal, Cyberpunk, NeonNoir, Phosphor, FireAndIce, Slate, BloodAndGreed, GoldStandard, Ultraviolet, Infrared, Toxic, CrimsonTide, Vaporwave, Matrix, Arctic }
    public enum TALightTheme      { Classic, Woodland, Solar, Twilight, Parchment, Shoreline, Graphite, Harvest, Guilded, Amethyst, Forge, Briar, Scarlet, Dusk, Fern, Nordic }
    public enum TAInfoLocation    { TopLeft, MiddleLeft, BottomLeft, MiddleRight, BottomRight }
    public enum TACandleType      { Regular, HeikinAshi, RSquaredAdaptive, LinRegHeikinAshi, LinRegCandles }
    public enum TAForecastMode    { Regression, SlopeExtension }
    public enum TACandleColorMode { MARibbon, TrendRegime, KAMAStack, DualConfirmation }

    /// <summary>
    /// TrendArchitect — QuantTower port.
    /// Computes all signals identically to the NT8 version.
    /// MQB/MQS signal values are exposed as LineSeries outputs so
    /// TrendArchitectMQPanel (QT version) can read them directly.
    /// </summary>
    public class TrendArchitect : Indicator
    {
        // ── Constants (matching Pine source exactly) ──────────────────────────
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
        private const double FHA_MEAS_N         = 0.1;
        private const double FHA_KG             = 0.7;

        private const int    TC_BASE_LEN = 100;
        private const double TC_MULT     = 10.0;

        private const double PRISM_NS_MAX_EXT   = 0.25;
        private const int    PRISM_NS_ER_SMOOTH = 10;
        private const int    PRISM_NS_QUANT     = 5;
        private const int    PRISM_REF_SEC      = 120;
        private const double PRISM_TF_EXP       = 0.45;

        private const int    BUF = 4096; // ring buffer size (power of 2)

        // ── Parameters (exposed to QuantTower properties panel) ───────────────
        [InputParameter("Settings Mode", 0, variants: new object[] { "Simple", TASettingsMode.Simple, "Advanced", TASettingsMode.Advanced })]
        public TASettingsMode SettingsMode { get; set; } = TASettingsMode.Simple;

        [InputParameter("Enable MA Ribbon",      1)]  public bool SimRibbonEnable     { get; set; } = true;
        [InputParameter("Enable Super Channel",  2)]  public bool SimSCEnable          { get; set; } = true;
        [InputParameter("Enable Alt Candles",    3)]  public bool SimAltCandlesEnable  { get; set; } = true;
        [InputParameter("Enable Trend Cloud",    4)]  public bool SimTCEnable          { get; set; } = true;
        [InputParameter("Enable PRISM Signals",  5)]  public bool SimPrismEnable       { get; set; } = true;
        [InputParameter("Enable TRG",            6)]  public bool SimTRGEnable         { get; set; } = true;
        [InputParameter("Enable Auto-Optimizer", 7)]  public bool SimAOEnable          { get; set; } = true;

        [InputParameter("Ribbon Length",    10, 1, 200, 1, 0)] public int RibbonLength    { get; set; } = 20;
        [InputParameter("Candle LR Length", 11, 2, 200, 1, 0)] public int CandleLRLength  { get; set; } = 10;
        [InputParameter("PRISM Length",     12, 10,200, 1, 0)] public int PrismLength      { get; set; } = 40;
        [InputParameter("PRISM St1 Factor", 13, 0.1,5.0,0.1,1)] public double PrismSt1Factor { get; set; } = 0.2;
        [InputParameter("PRISM St1 Period", 14, 1, 100, 1, 0)] public int PrismSt1Period   { get; set; } = 10;
        [InputParameter("PRISM St2 Factor", 15, 0.1,5.0,0.1,1)] public double PrismSt2Factor { get; set; } = 0.5;
        [InputParameter("PRISM St2 Period", 16, 1, 100, 1, 0)] public int PrismSt2Period   { get; set; } = 20;
        [InputParameter("PRISM Adaptive",   17)] public bool PrismAdaptiveEnable { get; set; } = true;
        [InputParameter("Noise Suppression",18)] public bool PrismNSEnable       { get; set; } = false;
        [InputParameter("Structure Lock",   19)] public bool PrismRibbonFiltEnable{ get; set; } = true;
        [InputParameter("Bar Quality",      20)] public bool PrismBQEnable        { get; set; } = true;
        [InputParameter("Min Body Ratio",   21, 0.05,0.7,0.05,2)] public double PrismBQMinRatio { get; set; } = 0.30;
        [InputParameter("Quality Gate",     22)] public bool PrismMQEnable        { get; set; } = true;

        [InputParameter("TRG KAMA Thresh",  30, 0.4,1.0,0.01,2)] public double TRGKaThresh     { get; set; } = 0.62;
        [InputParameter("TRG Hurst Len",    31, 20,150,1,0)]      public int    TRGHurstLen      { get; set; } = 100;
        [InputParameter("TRG Hurst Thresh", 32, 0.4,0.7,0.01,2)]  public double TRGHurstThresh   { get; set; } = 0.5;
        [InputParameter("TRG Accel Smooth", 33, 1,15,1,0)]        public int    TRGAccelSmooth   { get; set; } = 3;
        [InputParameter("TRG Votes Req",    34, 1,3,1,0)]         public int    TRGVotesRequired { get; set; } = 2;

        [InputParameter("Signal Offset ATR",40, 0.0,2.0,0.1,1)]   public double SignalOffset      { get; set; } = 0.9;

        [InputParameter("Dark Theme", 50, variants: new object[] {
            "Modern",       (int)TADarkTheme.Modern,
            "Terminal",     (int)TADarkTheme.Terminal,
            "Cyberpunk",    (int)TADarkTheme.Cyberpunk,
            "NeonNoir",     (int)TADarkTheme.NeonNoir,
            "Phosphor",     (int)TADarkTheme.Phosphor,
            "FireAndIce",   (int)TADarkTheme.FireAndIce,
            "Slate",        (int)TADarkTheme.Slate,
            "BloodAndGreed",(int)TADarkTheme.BloodAndGreed,
            "Matrix",       (int)TADarkTheme.Matrix,
            "Arctic",       (int)TADarkTheme.Arctic })]
        public TADarkTheme DarkTheme { get; set; } = TADarkTheme.Modern;

        // ── Output LineSeries ─────────────────────────────────────────────────
        // Indices must match the porting notes at the top of this file.
        private LineSeries lsBull;      // 0 — PRISM full bull signal
        private LineSeries lsBear;      // 1 — PRISM full bear signal
        private LineSeries lsMqBull;    // 2 — PRISM MQ bull dot
        private LineSeries lsMqBear;    // 3 — PRISM MQ bear dot
        private LineSeries lsCMA1;      // 4 — MA Ribbon line 1
        private LineSeries lsCMA2;      // 5 — MA Ribbon line 2
        private LineSeries lsSCTop;     // 6 — Super Channel top
        private LineSeries lsSCBot;     // 7 — Super Channel bottom
        private LineSeries lsTCBase;    // 8 — Trend Cloud base
        private LineSeries lsTCTop;     // 9 — Trend Cloud top

        // ── Ring buffer helpers ───────────────────────────────────────────────
        // QuantTower doesn't provide NT8-style Series<double>.
        // We maintain fixed-size circular buffers indexed by bar index.
        // Access pattern: buf[barIndex & (BUF-1)] = value this bar
        //                 buf[(barIndex-n) & (BUF-1)] = value n bars ago
        private double[] atr14Buf, srsiKBuf, mfiBuf, cciPctBuf;
        private double[] scCCORawBuf, scCCOBuf;
        private double[] scTopCCORawBuf, scBotCCORawBuf;
        private double[] scTopKCRawBuf,  scBotKCRawBuf;
        private double[] scTopBBRawBuf,  scBotBBRawBuf;
        private double[] scTopCCOBuf,    scBotCCOBuf;
        private double[] scTopKCBuf,     scBotKCBuf;
        private double[] scTopBBBuf,     scBotBBBuf;
        private double[] scTopBuf,       scBotBuf;
        private double[] cma1Buf,        cma2Buf;
        private double[] tcOhlc4Buf,     tcBaseBuf, tcCloudTopBuf;
        private double[][] tcKamasBuf;   // [19][BUF]
        private double[] cvHaCRawBuf,    cvLrhaCRawBuf;
        private double[] cvOBuf,         cvHBuf,  cvLBuf, cvCBuf;
        private double[] lrOBuf,         lrHBuf,  lrLBuf, lrCBuf;
        private double[] cvdAggPctBuf;
        private double[] trgTcVelBuf,    trgTcAccelSmoothBuf;
        private double[] prismPolyBuf,   prismFkamaBuf, prismErBuf;
        private int[]    prismSt1DirBuf, prismSt2DirBuf;

        // ── Faster HA Kalman state ────────────────────────────────────────────
        private double fhaVolEst = double.NaN, fhaVolErr = 1.0;
        private double fhaPvpEst = double.NaN, fhaPvpErr = 1.0;
        private double fhaPvpVar = 1.0;
        private double cvHaO     = double.NaN;
        private double cvLrhaO   = double.NaN;

        // ── PRISM SuperTrend rail state ───────────────────────────────────────
        private double prismSt1Line = double.NaN, prismSt1LinePrev = double.NaN;
        private double prismSt2Line = double.NaN, prismSt2LinePrev = double.NaN;
        private int    prismSt1Dir  = 1, prismSt1DirPrev = 1;
        private int    prismSt2Dir  = 1, prismSt2DirPrev = 1;
        private double prismSt1UpperPrev = double.NaN, prismSt1LowerPrev = double.NaN;
        private double prismSt2UpperPrev = double.NaN, prismSt2LowerPrev = double.NaN;
        private int    prismLastDir = 1;
        private int    prismLastSig = 0;

        // ── PRISM hold counters ───────────────────────────────────────────────
        private int prismBullHoldUntil   = -1;
        private int prismBearHoldUntil   = -1;
        private int prismBqBullHoldUntil = -1;
        private int prismBqBearHoldUntil = -1;

        // ── TRG state ─────────────────────────────────────────────────────────
        private double trgHurstCached  = 0.5;
        private double _trgKaBullPct, _trgKaBearPct;
        private bool   _renderTcBull;

        // ── Auto-Optimizer state ──────────────────────────────────────────────
        private int    effPrismLen, effPrismSt1Per, effPrismSt2Per;
        private double effPrismNsERSmooth;
        private int    aoLenS, aoLenM, aoLenL, aoEffLen;
        private double aoAvgS, aoAvgM, aoAvgL, aoSumS, aoSumM, aoSumL, aoTotal;
        private List<int> aoScoresS = new List<int>();
        private List<int> aoScoresM = new List<int>();
        private List<int> aoScoresL = new List<int>();
        private double aoPendingPrice = double.NaN;
        private int    aoPendingDir, aoPendingBar, aoPendingBest;
        private double aoPendingAtr   = double.NaN;
        private int    aoPendingLenBucket = 1;

        // ── Theme colors ──────────────────────────────────────────────────────
        private Color bullColor, bearColor, neutColor, hilightColor;
        private Color sigBullColor, sigBearColor;

        // ── Current bar index (incremented in OnUpdate) ───────────────────────
        private int _bar = 0;

        // ── Constructor ───────────────────────────────────────────────────────
        public TrendArchitect()
            : base()
        {
            Name        = "Trend Architect";
            Description = "TrendArchitect QuantTower port — full PRISM signal computation. " +
                          "MQB/MQS outputs on LineSeries 2/3. Original Pine © its author (MPL-2.0). " +
                          "NT8 port by Jason/@_hawkeye_13. QT port: open source.";

            // Output series
            lsBull   = new LineSeries("PRISM Bull",   Color.Gold,          LineStyle.Solid, 1);
            lsBear   = new LineSeries("PRISM Bear",   Color.White,         LineStyle.Solid, 1);
            lsMqBull = new LineSeries("MQ Bull Dot",  Color.Lime,          LineStyle.Dot,   2);
            lsMqBear = new LineSeries("MQ Bear Dot",  Color.OrangeRed,     LineStyle.Dot,   2);
            lsCMA1   = new LineSeries("MA Ribbon 1",  Color.Cyan,          LineStyle.Solid, 2);
            lsCMA2   = new LineSeries("MA Ribbon 2",  Color.DodgerBlue,    LineStyle.Solid, 2);
            lsSCTop  = new LineSeries("SC Top",       Color.Cyan,          LineStyle.Solid, 1);
            lsSCBot  = new LineSeries("SC Bottom",    Color.Red,           LineStyle.Solid, 1);
            lsTCBase = new LineSeries("TC Base",      Color.Cyan,          LineStyle.Solid, 2);
            lsTCTop  = new LineSeries("TC Top",       Color.Gray,          LineStyle.Solid, 1);

            AddLineSeries(lsBull);
            AddLineSeries(lsBear);
            AddLineSeries(lsMqBull);
            AddLineSeries(lsMqBear);
            AddLineSeries(lsCMA1);
            AddLineSeries(lsCMA2);
            AddLineSeries(lsSCTop);
            AddLineSeries(lsSCBot);
            AddLineSeries(lsTCBase);
            AddLineSeries(lsTCTop);
        }

        // ── OnInit — allocate buffers ─────────────────────────────────────────
        protected override void OnInit()
        {
            atr14Buf           = new double[BUF];
            srsiKBuf           = Fill(new double[BUF], 50.0);
            mfiBuf             = Fill(new double[BUF], 50.0);
            cciPctBuf          = Fill(new double[BUF], 50.0);
            scCCORawBuf        = new double[BUF];
            scCCOBuf           = Fill(new double[BUF], 50.0);
            scTopCCORawBuf     = new double[BUF];
            scBotCCORawBuf     = new double[BUF];
            scTopKCRawBuf      = new double[BUF];
            scBotKCRawBuf      = new double[BUF];
            scTopBBRawBuf      = new double[BUF];
            scBotBBRawBuf      = new double[BUF];
            scTopCCOBuf        = new double[BUF];
            scBotCCOBuf        = new double[BUF];
            scTopKCBuf         = new double[BUF];
            scBotKCBuf         = new double[BUF];
            scTopBBBuf         = new double[BUF];
            scBotBBBuf         = new double[BUF];
            scTopBuf           = new double[BUF];
            scBotBuf           = new double[BUF];
            cma1Buf            = new double[BUF];
            cma2Buf            = new double[BUF];
            tcOhlc4Buf         = new double[BUF];
            tcBaseBuf          = new double[BUF];
            tcCloudTopBuf      = new double[BUF];
            tcKamasBuf         = new double[19][];
            for (int i = 0; i < 19; i++) tcKamasBuf[i] = new double[BUF];
            cvHaCRawBuf        = new double[BUF];
            cvLrhaCRawBuf      = new double[BUF];
            cvOBuf             = new double[BUF];
            cvHBuf             = new double[BUF];
            cvLBuf             = new double[BUF];
            cvCBuf             = new double[BUF];
            lrOBuf             = new double[BUF];
            lrHBuf             = new double[BUF];
            lrLBuf             = new double[BUF];
            lrCBuf             = new double[BUF];
            cvdAggPctBuf       = new double[BUF];
            trgTcVelBuf        = new double[BUF];
            trgTcAccelSmoothBuf= new double[BUF];
            prismPolyBuf       = new double[BUF];
            prismFkamaBuf      = new double[BUF];
            prismErBuf         = new double[BUF];
            prismSt1DirBuf     = new int[BUF];
            prismSt2DirBuf     = new int[BUF];

            ResolveTheme();
        }

        // ── OnUpdate — main computation (called every bar close) ──────────────
        protected override void OnUpdate(UpdateArgs args)
        {
            if (_bar < 1) { _bar++; return; }

            // Populate current bar OHLCV from QuantTower HistoricalData
            int b = _bar;
            double open   = Open(b);
            double high   = High(b);
            double low    = Low(b);
            double close  = Close(b);
            double volume = Volume(b);

            // Run all computation pipelines
            ComputeAtr(b);
            ComputeMomentumOscillators(b);
            ComputeSuperChannel(b);
            ComputeRibbon(b);
            ComputeLinearRegressions(b);
            ComputeCandles(b);
            ComputeCVD(b);
            ComputeTrendCloud(b);
            ComputeTrendRegimeGate(b);
            ComputeAutoOptimizer(b);
            ComputePrismSignals(b);

            // Write outputs to LineSeries
            bool prismIsBull   = Buf(prismPolyBuf, b) > 0 && Buf(prismSt1DirBuf, b) == -1 && Buf(prismSt2DirBuf, b) == -1;
            bool prismIsBear   = Buf(prismPolyBuf, b) < 0 && Buf(prismSt1DirBuf, b) ==  1 && Buf(prismSt2DirBuf, b) ==  1;

            // MQB/MQS are stored as sign flags in prismErBuf during ComputePrismSignals
            // We use dedicated output fields set by ComputePrismSignals
            lsBull  .SetValue(_isMqBullFinal || _isBullFinal  ? close : double.NaN);
            lsBear  .SetValue(_isMqBearFinal || _isBearFinal  ? close : double.NaN);
            lsMqBull.SetValue(_isMqBullFinal && !_isElevBull  ? close : double.NaN);
            lsMqBear.SetValue(_isMqBearFinal && !_isElevBear  ? close : double.NaN);
            lsCMA1  .SetValue(Buf(cma1Buf,   b));
            lsCMA2  .SetValue(Buf(cma2Buf,   b));
            lsSCTop .SetValue(Buf(scTopBuf,  b));
            lsSCBot .SetValue(Buf(scBotBuf,  b));
            lsTCBase.SetValue(Buf(tcBaseBuf, b));
            lsTCTop .SetValue(Buf(tcCloudTopBuf, b));

            _bar++;
        }

        // Final signal flags set by ComputePrismSignals each bar
        private bool _isBullFinal, _isBearFinal, _isMqBullFinal, _isMqBearFinal, _isElevBull, _isElevBear;

        // ═════════════════════════════════════════════════════════════════════
        // COMPUTATION PIPELINES — identical math to NT8 version
        // ═════════════════════════════════════════════════════════════════════

        // ── ATR(14) Wilder ────────────────────────────────────────────────────
        private void ComputeAtr(int b)
        {
            double tr = TrueRange(b);
            // Wilder smoothing: ATR = prev_ATR*(n-1)/n + TR/n
            double prev = b > 1 ? Buf(atr14Buf, b - 1) : tr;
            double atr  = double.IsNaN(prev) || prev == 0 ? tr : (prev * 13.0 + tr) / 14.0;
            Set(atr14Buf, b, atr);
        }

        // ── Momentum Oscillators ──────────────────────────────────────────────
        private void ComputeMomentumOscillators(int b)
        {
            // MFI(14)
            Set(mfiBuf, b, CalcMFI(b, SC_MFI_LEN));

            // CCI(20) → percentrank over 100 bars
            double cciNow = CalcCCI(b, SC_CCI_LEN);
            Set(cciPctBuf, b, PercentRankValue(b, cciNow, SC_CCI_LEN, SC_PCTRANK_LB));

            // StochRSI K — RSI(14) → stoch(14) → SMA(3)
            double rsi = CalcRSI(b, 14);
            double rsiMin = double.MaxValue, rsiMax = double.MinValue;
            int lb = Math.Min(14, b);
            for (int i = 0; i < lb; i++)
            {
                double r = CalcRSI(b - i, 14);
                if (r < rsiMin) rsiMin = r;
                if (r > rsiMax) rsiMax = r;
            }
            double stochRaw = (rsiMax - rsiMin) > 1e-10 ? 100.0 * (rsi - rsiMin) / (rsiMax - rsiMin) : 50.0;
            // SMA(3) of stoch using ring buffer
            double smaStoch = (stochRaw + Buf(srsiKBuf, b - 1) + (b >= 2 ? Buf(srsiKBuf, b - 2) : stochRaw)) / 3.0;
            Set(srsiKBuf, b, smaStoch);
        }

        // ── Super Channel ─────────────────────────────────────────────────────
        private void ComputeSuperChannel(int b)
        {
            double atr  = Buf(atr14Buf, b);
            double srsi = Buf(srsiKBuf, b);
            double mfi  = Buf(mfiBuf,   b);
            double cci  = Buf(cciPctBuf,b);
            double hlc3 = (High(b) + Low(b) + Close(b)) / 3.0;

            double ccoRaw  = (srsi * 0.8 + mfi * 0.9 + cci * 1.2) / (0.8 + 0.9 + 1.2);
            Set(scCCORawBuf, b, ccoRaw);
            double cco     = BufSMA(scCCORawBuf, b, SC_CONSENSUS_SMO);
            Set(scCCOBuf,    b, cco);

            double normCCO = Math.Max(0.0, Math.Min(1.0, (cco - SC_CCO_LO) / (SC_CCO_HI - SC_CCO_LO)));

            Set(scTopCCORawBuf, b, hlc3 + SC_ATR_DIST * (1.0 - normCCO) * atr);
            Set(scBotCCORawBuf, b, hlc3 - SC_ATR_DIST * normCCO          * atr);
            Set(scTopCCOBuf,    b, BufSMA(scTopCCORawBuf, b, SC_SMOOTHING));
            Set(scBotCCOBuf,    b, BufSMA(scBotCCORawBuf, b, SC_SMOOTHING));

            double kcMid = BufEMA(b, SC_KC_EMA_LEN);
            double kcAtr = BufATR(b, SC_KC_ATR_LEN);
            Set(scTopKCRawBuf, b, kcMid + SC_KC_MULT * kcAtr);
            Set(scBotKCRawBuf, b, kcMid - SC_KC_MULT * kcAtr);
            Set(scTopKCBuf,    b, BufSMA(scTopKCRawBuf, b, SC_SMOOTHING));
            Set(scBotKCBuf,    b, BufSMA(scBotKCRawBuf, b, SC_SMOOTHING));

            double bbMid = BufSMAClose(b, SC_BB_LEN);
            double bbDev = BufStdDev(b, SC_BB_LEN);
            Set(scTopBBRawBuf, b, bbMid + SC_BB_MULT * bbDev);
            Set(scBotBBRawBuf, b, bbMid - SC_BB_MULT * bbDev);
            Set(scTopBBBuf,    b, BufSMA(scTopBBRawBuf, b, SC_SMOOTHING));
            Set(scBotBBBuf,    b, BufSMA(scBotBBRawBuf, b, SC_SMOOTHING));

            Set(scTopBuf, b, Buf(scTopCCOBuf,b)*SC_CCO_RATIO + Buf(scTopKCBuf,b)*SC_KELTNER_RATIO + Buf(scTopBBBuf,b)*SC_BB_RATIO);
            Set(scBotBuf, b, Buf(scBotCCOBuf,b)*SC_CCO_RATIO + Buf(scBotKCBuf,b)*SC_KELTNER_RATIO + Buf(scBotBBBuf,b)*SC_BB_RATIO);
        }

        // ── ALMA Ribbon ───────────────────────────────────────────────────────
        private void ComputeRibbon(int b)
        {
            Set(cma1Buf, b, ALMASimple(b, RibbonLength, 0.85, 6.0));
            Set(cma2Buf, b, ALMASimple(b, RibbonLength, 0.77, 6.0));
        }

        private double ALMASimple(int b, int length, double offset, double sigma)
        {
            int len = Math.Min(length, b);
            if (len < 1) return Close(b);
            double m = offset * (len - 1);
            double s = len / sigma;
            double sum = 0, wsum = 0;
            for (int i = 0; i < len; i++)
            {
                double w = Math.Exp(-1.0 * ((i - m) * (i - m)) / (2.0 * s * s));
                sum  += Close(b - (len - 1 - i)) * w;
                wsum += w;
            }
            return wsum > 0 ? sum / wsum : Close(b);
        }

        // ── Linear Regressions ────────────────────────────────────────────────
        private void ComputeLinearRegressions(int b)
        {
            Set(lrOBuf, b, LinReg(b, CandleLRLength, 'O'));
            Set(lrHBuf, b, LinReg(b, CandleLRLength, 'H'));
            Set(lrLBuf, b, LinReg(b, CandleLRLength, 'L'));
            Set(lrCBuf, b, LinReg(b, CandleLRLength, 'C'));
        }

        private double LinReg(int b, int len, char src)
        {
            int n = Math.Min(len, b);
            if (n < 2) return SrcVal(b, src);
            double sumX=0, sumY=0, sumXY=0, sumX2=0;
            for (int i = 0; i < n; i++)
            {
                double x = i, y = SrcVal(b - (n - 1 - i), src);
                sumX += x; sumY += y; sumXY += x*y; sumX2 += x*x;
            }
            double denom = n * sumX2 - sumX * sumX;
            if (Math.Abs(denom) < 1e-12) return SrcVal(b, src);
            double slope = (n * sumXY - sumX * sumY) / denom;
            double inter = (sumY - slope * sumX) / n;
            return inter + slope * (n - 1);
        }

        private double SrcVal(int b, char src)
        {
            switch (src)
            {
                case 'O': return Open(b);
                case 'H': return High(b);
                case 'L': return Low(b);
                default:  return Close(b);
            }
        }

        // ── Candles (Faster HA Kalman + R² Adaptive) ──────────────────────────
        private void ComputeCandles(int b)
        {
            double barPVP = (Close(b) - Open(b)) * Volume(b);

            double vkVol = (fhaVolErr + FHA_MEAS_N) / (fhaVolErr + FHA_MEAS_N + FHA_MEAS_N);
            fhaVolEst = double.IsNaN(fhaVolEst) ? Volume(b) : fhaVolEst + vkVol * (Volume(b) - fhaVolEst);
            fhaVolErr = (1.0 - vkVol) * (fhaVolErr + FHA_MEAS_N);

            double vkPvp = (fhaPvpErr + FHA_MEAS_N) / (fhaPvpErr + FHA_MEAS_N + FHA_MEAS_N);
            fhaPvpEst = double.IsNaN(fhaPvpEst) ? barPVP : fhaPvpEst + vkPvp * (barPVP - fhaPvpEst);
            fhaPvpErr = (1.0 - vkPvp) * (fhaPvpErr + FHA_MEAS_N);

            double diff = barPVP - fhaPvpEst;
            fhaPvpVar = (fhaPvpVar <= 0) ? (diff * diff) : fhaPvpVar + FHA_KG * (diff * diff - fhaPvpVar);

            double pvpStd  = Math.Sqrt(Math.Max(fhaPvpVar, 0.0001));
            double volRat  = Math.Min(Volume(b) / Math.Max(fhaVolEst, 1.0), FHA_MAX_VOL_MULT);
            double pvpNorm = pvpStd > 0 ? (barPVP - fhaPvpEst) / pvpStd : 0.0;
            double combVF  = Math.Sqrt(volRat) * Math.Max(0.5, Math.Min(1.5, 1.0 + pvpNorm * 0.2));
            double vf      = 1.0 + (combVF - 1.0) * FHA_VOL_INFLUENCE;

            double cw  = 1.0 + FHA_RESPONSIVENESS * 2.0;
            double ow  = 1.0 + FHA_RESPONSIVENESS * 0.5;
            double hw  = 1.0 + FHA_RESPONSIVENESS * 0.3;
            double tot = (ow + hw * 2.0 + cw) * vf;
            double spd = Math.Min(FHA_RESPONSIVENESS * (1.0 + FHA_RESPONSIVENESS * 0.5) * Math.Sqrt(vf), 1.0);

            double haCRaw = (Open(b)*ow + High(b)*hw + Low(b)*hw + Close(b)*cw) * vf / tot;
            Set(cvHaCRawBuf, b, haCRaw);
            double haTradO = double.IsNaN(cvHaO) ? (Open(b) + Close(b)) / 2.0 : (cvHaO + Buf(cvHaCRawBuf, b-1)) / 2.0;
            cvHaO = double.IsNaN(cvHaO) ? (Open(b)*ow + Close(b)*cw) / ((ow+cw)*vf) : cvHaO + spd * (haTradO - cvHaO);

            double haO = cvHaO, haC = haCRaw;
            double haH = Math.Max(High(b), Math.Max(haO, haC));
            double haL = Math.Min(Low(b),  Math.Min(haO, haC));

            // R² Adaptive
            double corr = Correlation(b, CandleLRLength);
            double r2   = corr * corr;
            const double R2_LO=0.3, R2_HI=0.8, BLD_MIN=0.20, BLD_MAX=0.80;
            double r2Norm = Math.Max(0.0, Math.Min(1.0, (r2-R2_LO)/(R2_HI-R2_LO)));
            double blend  = BLD_MIN + r2Norm * (BLD_MAX - BLD_MIN);
            double lrO = Buf(lrOBuf,b), lrH = Buf(lrHBuf,b), lrL = Buf(lrLBuf,b), lrC = Buf(lrCBuf,b);
            double raO = Open(b) *(1-blend) + lrO*blend;
            double raH = High(b) *(1-blend) + lrH*blend;
            double raL = Low(b)  *(1-blend) + lrL*blend;
            double raC = Close(b)*(1-blend) + lrC*blend;
            raH = Math.Max(raH, Math.Max(raO, raC));
            raL = Math.Min(raL, Math.Min(raO, raC));

            // LinReg-HA
            double lrhaCRaw = (lrO*ow + lrH*hw + lrL*hw + lrC*cw) * vf / tot;
            Set(cvLrhaCRawBuf, b, lrhaCRaw);
            double lrhaTradO = double.IsNaN(cvLrhaO) ? (lrO+lrC)/2.0 : (cvLrhaO + Buf(cvLrhaCRawBuf, b-1))/2.0;
            cvLrhaO = double.IsNaN(cvLrhaO) ? (lrO*ow+lrC*cw)/((ow+cw)*vf) : cvLrhaO + spd*(lrhaTradO-cvLrhaO);
            double lrhaO = cvLrhaO, lrhaC = lrhaCRaw;
            double lrhaH = Math.Max(lrH, Math.Max(lrhaO, lrhaC));
            double lrhaL = Math.Min(lrL, Math.Min(lrhaO, lrhaC));

            // Store R² Adaptive (default for MQPanel detection)
            Set(cvOBuf, b, raO); Set(cvHBuf, b, raH); Set(cvLBuf, b, raL); Set(cvCBuf, b, raC);
        }

        // ── CVD ───────────────────────────────────────────────────────────────
        private void ComputeCVD(int b)
        {
            double tw   = High(b) - Math.Max(Open(b), Close(b));
            double bw   = Math.Min(Open(b), Close(b)) - Low(b);
            double body = Math.Abs(Close(b) - Open(b));
            double den  = Math.Max(tw + bw + body, 1e-10);
            double bse  = 0.5 * (tw + bw) / den;
            double ext  = body / den;
            double vol  = Volume(b);
            double up   = vol * Math.Max(bse + (Open(b) <= Close(b) ? ext : 0.0), 0.5);
            double dn   = vol * Math.Max(bse + (Open(b) >  Close(b) ? ext : 0.0), 0.5);
            double pct  = vol > 0 ? Math.Abs(up - dn) / vol * 100.0 : 0.0;
            Set(cvdAggPctBuf, b, pct);
        }

        // ── Trend Cloud ───────────────────────────────────────────────────────
        private void ComputeTrendCloud(int b)
        {
            double ohlc4 = (Open(b)+High(b)+Low(b)+Close(b))/4.0;
            Set(tcOhlc4Buf, b, ohlc4);

            int[] lens = {5,10,15,20,25,30,35,40,45,50,55,60,65,70,75,80,85,90,100};
            for (int i = 0; i < 19; i++)
                Set(tcKamasBuf[i], b, KamaVB(tcOhlc4Buf, b, lens[i], tcKamasBuf[i]));

            Set(tcBaseBuf, b, KamaVBClose(b, TC_BASE_LEN, tcBaseBuf));

            double totalDist = 0;
            for (int i = 0; i < 18; i++)
            {
                double k0 = Buf(tcKamasBuf[i],   b);
                double k1 = Buf(tcKamasBuf[i+1], b);
                if (Math.Abs(k1) > 1e-12) totalDist += (k0 - k1) / k1;
            }
            double avgDist = totalDist / 18.0;
            double tcBase  = Buf(tcBaseBuf, b);
            Set(tcCloudTopBuf, b, tcBase * (1.0 + avgDist * TC_MULT));
        }

        // ── Trend Regime Gate ─────────────────────────────────────────────────
        private void ComputeTrendRegimeGate(int b)
        {
            int bullCount = 0, bearCount = 0;
            for (int i = 0; i < 18; i++)
            {
                if (Buf(tcKamasBuf[i], b) > Buf(tcKamasBuf[i+1], b)) bullCount++;
                if (Buf(tcKamasBuf[i], b) < Buf(tcKamasBuf[i+1], b)) bearCount++;
            }
            _trgKaBullPct = bullCount / 18.0;
            _trgKaBearPct = bearCount / 18.0;

            // Hurst — throttle every 5 bars
            int hurstLen = SettingsMode == TASettingsMode.Simple ? 50 : TRGHurstLen;
            if (b % 5 == 0 || b < hurstLen + 2)
                trgHurstCached = HurstRS(b, hurstLen);

            // TC acceleration
            double tcBase     = Buf(tcBaseBuf, b);
            double tcBasePrev = b >= 1 ? Buf(tcBaseBuf, b-1) : tcBase;
            double atr        = Buf(atr14Buf, b);
            double vel        = atr > 0 ? (tcBase - tcBasePrev) / atr : 0.0;
            Set(trgTcVelBuf, b, vel);

            double velPrev = b >= 1 ? Buf(trgTcVelBuf, b-1) : 0.0;
            double accel   = vel - velPrev;
            int    smo     = SettingsMode == TASettingsMode.Simple ? 5 : TRGAccelSmooth;
            double alpha   = 2.0 / (smo + 1.0);
            double prevSmo = b >= 1 ? Buf(trgTcAccelSmoothBuf, b-1) : accel;
            Set(trgTcAccelSmoothBuf, b, prevSmo + alpha * (accel - prevSmo));

            _renderTcBull = b >= 1 && tcBase > tcBasePrev;
        }

        private bool TrgSuppressSell(int b) => TrgBullVotes(b) >= (SettingsMode == TASettingsMode.Simple ? 2 : TRGVotesRequired);
        private bool TrgSuppressBuy(int b)  => TrgBearVotes(b) >= (SettingsMode == TASettingsMode.Simple ? 2 : TRGVotesRequired);

        private int TrgBullVotes(int b)
        {
            double th = SettingsMode == TASettingsMode.Simple ? 0.72 : TRGKaThresh;
            int v = 0;
            if (_trgKaBullPct >= th) v++;
            if (trgHurstCached > (SettingsMode == TASettingsMode.Simple ? 0.55 : TRGHurstThresh)) v++;
            if (Buf(trgTcAccelSmoothBuf, b) > 0 && _renderTcBull) v++;
            return v;
        }

        private int TrgBearVotes(int b)
        {
            double th = SettingsMode == TASettingsMode.Simple ? 0.72 : TRGKaThresh;
            int v = 0;
            if (_trgKaBearPct >= th) v++;
            if (trgHurstCached > (SettingsMode == TASettingsMode.Simple ? 0.55 : TRGHurstThresh)) v++;
            if (Buf(trgTcAccelSmoothBuf, b) < 0 && !_renderTcBull) v++;
            return v;
        }

        // ── Auto-Optimizer ────────────────────────────────────────────────────
        private void ComputeAutoOptimizer(int b)
        {
            bool effAdaptive = SettingsMode == TASettingsMode.Simple || PrismAdaptiveEnable;
            bool effNS       = SettingsMode != TASettingsMode.Simple && PrismNSEnable;

            double tfRatio = 1.0;
            if (effAdaptive)
            {
                int tfSec = SecondsPerBar();
                if (tfSec > 0 && tfSec < PRISM_REF_SEC)
                    tfRatio = Math.Pow((double)PRISM_REF_SEC / tfSec, PRISM_TF_EXP);
            }

            // Efficiency Ratio smoothing
            int erLen = 14;
            double erSig = Math.Abs(Close(b) - Close(Math.Max(0, b - erLen)));
            double erNoise = 0;
            for (int i = 0; i < Math.Min(erLen, b-1); i++) erNoise += Math.Abs(Close(b-i) - Close(b-i-1));
            double erRaw  = erNoise > 0 ? erSig / erNoise : 0.0;
            double alpha  = 2.0 / (PRISM_NS_ER_SMOOTH + 1.0);
            double prevEr = b >= 1 ? Buf(prismErBuf, b-1) : erRaw;
            double erSmo  = double.IsNaN(prevEr) ? erRaw : prevEr + alpha * (erRaw - prevEr);
            Set(prismErBuf, b, erSmo);

            double nsFull = effNS ? 1.0 + (1.0 - erSmo) * PRISM_NS_MAX_EXT       : 1.0;
            double nsHalf = effNS ? 1.0 + (1.0 - erSmo) * PRISM_NS_MAX_EXT * 0.5 : 1.0;

            effPrismLen    = Quantize(40 * tfRatio * nsFull, PRISM_NS_QUANT);
            effPrismSt1Per = Quantize(10 * tfRatio * nsHalf, PRISM_NS_QUANT);
            effPrismSt2Per = Quantize(20 * tfRatio * nsHalf, PRISM_NS_QUANT);
            effPrismNsERSmooth = erSmo;

            double spreadFrac = 0.50;
            aoLenS = Quantize(effPrismLen * (1.0 - spreadFrac), PRISM_NS_QUANT);
            aoLenM = effPrismLen;
            aoLenL = Quantize(effPrismLen * (1.0 + spreadFrac), PRISM_NS_QUANT);

            bool aoEn = SettingsMode == TASettingsMode.Simple ? SimAOEnable : true;
            if (aoEn)
            {
                ScorePendingPrism(b);
                aoAvgS = WeightedAvg(aoScoresS);
                aoAvgM = WeightedAvg(aoScoresM);
                aoAvgL = WeightedAvg(aoScoresL);
                double sharp = 3.0;
                double wS = Math.Pow(aoAvgS, sharp) * aoScoresS.Count;
                double wM = Math.Pow(aoAvgM, sharp) * aoScoresM.Count;
                double wL = Math.Pow(aoAvgL, sharp) * aoScoresL.Count;
                aoTotal = wS + wM + wL;
                aoEffLen = aoTotal > 0
                    ? (int)Math.Round((aoLenS*wS + aoLenM*wM + aoLenL*wL) / aoTotal)
                    : aoLenM;
                effPrismLen = aoEffLen;
            }
            else { aoEffLen = aoLenM; }
        }

        private void ScorePendingPrism(int b)
        {
            if (double.IsNaN(aoPendingPrice)) return;
            double fav = aoPendingDir == 1 ? High(b) - aoPendingPrice : aoPendingPrice - Low(b);
            double t1 = 0.75, t2 = 1.5, t3 = 2.5;
            if (fav >= aoPendingAtr * t3) aoPendingBest = 3;
            else if (fav >= aoPendingAtr * t2) aoPendingBest = Math.Max(aoPendingBest, 2);
            else if (fav >= aoPendingAtr * t1) aoPendingBest = Math.Max(aoPendingBest, 1);
            if (aoPendingBest >= 3 || (b - aoPendingBar >= 7))
            {
                var bucket = aoPendingLenBucket == 0 ? aoScoresS : aoPendingLenBucket == 2 ? aoScoresL : aoScoresM;
                bucket.Add(aoPendingBest);
                if (bucket.Count > 20) bucket.RemoveAt(0);
                aoPendingPrice = double.NaN;
            }
        }

        // ── PRISM Signal Generation ───────────────────────────────────────────
        private void ComputePrismSignals(int b)
        {
            int polyLen = Math.Max(10, Math.Min(200, effPrismLen));
            double polyVal = CalcPolyDeg4(b, polyLen);
            Set(prismPolyBuf, b, polyVal);

            double polySrc1 = b >= 1 ? Buf(prismPolyBuf, b-1) : polyVal;

            double st1Line, st2Line;
            int st1Dir = prismSt1Dir, st2Dir = prismSt2Dir;
            CalcSuperTrend(polyVal, polySrc1, PrismSt1Factor, Math.Max(1, effPrismSt1Per),
                ref prismSt1UpperPrev, ref prismSt1LowerPrev,
                ref prismSt1LinePrev, ref st1Dir, out st1Line);
            CalcSuperTrend(polyVal, polySrc1, PrismSt2Factor, Math.Max(1, effPrismSt2Per),
                ref prismSt2UpperPrev, ref prismSt2LowerPrev,
                ref prismSt2LinePrev, ref st2Dir, out st2Line);

            prismSt1DirPrev = prismSt1Dir; prismSt2DirPrev = prismSt2Dir;
            prismSt1Dir = st1Dir;           prismSt2Dir = st2Dir;
            prismSt1Line = st1Line;         prismSt2Line = st2Line;
            Set(prismSt1DirBuf, b, st1Dir);
            Set(prismSt2DirBuf, b, st2Dir);

            if (st1Dir == -1 && st2Dir == -1) prismLastDir = -1;
            else if (st1Dir == 1 && st2Dir == 1) prismLastDir = 1;

            bool nowBull = st1Dir == -1 && st2Dir == -1;
            bool nowBear = st1Dir ==  1 && st2Dir ==  1;
            bool wasBull = b >= 1 && Buf(prismSt1DirBuf, b-1) == -1 && Buf(prismSt2DirBuf, b-1) == -1;
            bool wasBear = b >= 1 && Buf(prismSt1DirBuf, b-1) ==  1 && Buf(prismSt2DirBuf, b-1) ==  1;
            bool rawBull = nowBull && !wasBull;
            bool rawBear = nowBear && !wasBear;

            double bqRatio = Math.Abs(Close(b) - Open(b)) / Math.Max(High(b) - Low(b), 1e-10);
            bool bqBullOk  = Close(b) >= Open(b) && bqRatio >= PrismBQMinRatio;
            bool bqBearOk  = Close(b) <= Open(b) && bqRatio >= PrismBQMinRatio;

            double cma1 = Buf(cma1Buf, b), cma2 = Buf(cma2Buf, b);
            double cma1p = b >= 1 ? Buf(cma1Buf, b-1) : cma1;
            double cma2p = b >= 1 ? Buf(cma2Buf, b-1) : cma2;

            bool ribbonAdvBull = false, ribbonAdvBear = false;
            if (PrismRibbonFiltEnable && b >= 2)
            {
                ribbonAdvBull = cma1 < cma2 && (cma2 - cma1) > (cma2p - cma1p);
                ribbonAdvBear = cma1 > cma2 && (cma1 - cma2) > (cma1p - cma2p);
            }

            if (rawBull) prismBearHoldUntil = -1;
            if (rawBear) prismBullHoldUntil = -1;
            if (rawBull) prismBullHoldUntil = ribbonAdvBull ? b + 4 : -1;
            if (rawBear) prismBearHoldUntil = ribbonAdvBear ? b + 4 : -1;

            bool bullInHold = prismBullHoldUntil > 0 && b <= prismBullHoldUntil;
            bool bearInHold = prismBearHoldUntil > 0 && b <= prismBearHoldUntil;
            bool bullReleased = bullInHold && b >= 1 && cma1 > cma2 && cma1p <= cma2p;
            bool bearReleased = bearInHold && b >= 1 && cma1 < cma2 && cma1p >= cma2p;
            if (bullReleased) prismBullHoldUntil = -1;
            if (bearReleased) prismBearHoldUntil = -1;

            bool immBull = rawBull && !ribbonAdvBull && prismLastSig != 1  && (!PrismBQEnable || bqBullOk);
            bool immBear = rawBear && !ribbonAdvBear && prismLastSig != -1 && (!PrismBQEnable || bqBearOk);

            if (rawBear) prismBqBullHoldUntil = -1;
            if (rawBull) prismBqBearHoldUntil = -1;
            if (rawBull && !ribbonAdvBull && prismLastSig != 1  && PrismBQEnable && !bqBullOk) prismBqBullHoldUntil = b + 3;
            if (rawBear && !ribbonAdvBear && prismLastSig != -1 && PrismBQEnable && !bqBearOk) prismBqBearHoldUntil = b + 3;

            bool bqBullInHold   = prismBqBullHoldUntil > 0 && b <= prismBqBullHoldUntil;
            bool bqBearInHold   = prismBqBearHoldUntil > 0 && b <= prismBqBearHoldUntil;
            bool bqBullReleased = bqBullInHold && bqBullOk;
            bool bqBearReleased = bqBearInHold && bqBearOk;
            if (bqBullReleased) prismBqBullHoldUntil = -1;
            if (bqBearReleased) prismBqBearHoldUntil = -1;

            bool effPrismEn = SettingsMode == TASettingsMode.Simple ? SimPrismEnable : true;
            bool wouldBull  = effPrismEn && (immBull || bullReleased || bqBullReleased);
            bool wouldBear  = effPrismEn && (immBear || bearReleased || bqBearReleased);

            if (rawBull) prismLastSig = 1;
            if (rawBear) prismLastSig = -1;

            // Quality Gate (MQ)
            double erSmo = Buf(prismErBuf, b);
            double fkama = KamaVBClose(b, 20, prismFkamaBuf);
            Set(prismFkamaBuf, b, fkama);
            double fkamaPrev = b >= 5 ? Buf(prismFkamaBuf, b-5) : fkama;
            double atr = Buf(atr14Buf, b);
            double fkamaNorm = atr > 0 && b > 5 ? (fkama - fkamaPrev) / atr : 0.0;

            bool mqChopBull = PrismMQEnable && erSmo < 0.2 && fkamaNorm <  0.03;
            bool mqChopBear = PrismMQEnable && erSmo < 0.2 && fkamaNorm > -0.03;

            bool isMqBull = wouldBull && (mqChopBull || TrgSuppressBuy(b));
            bool isMqBear = wouldBear && (mqChopBear || TrgSuppressSell(b));

            // Elevation (simplified — same logic as NT8)
            double cvdRankNow = PercentRankBuf(cvdAggPctBuf, b, 50);
            bool elev4Bull = cvdRankNow >= 95.0 && (Close(b) - Open(b)) > 0;
            bool elev4Bear = cvdRankNow >= 95.0 && (Close(b) - Open(b)) < 0;

            bool tcBull = b >= 1 && Buf(tcBaseBuf, b) < Buf(tcBaseBuf, b-1);
            bool tcBear = b >= 1 && Buf(tcBaseBuf, b) > Buf(tcBaseBuf, b-1);
            bool elevBull = isMqBull && elev4Bull;
            bool elevBear = isMqBear && elev4Bear;

            _isMqBullFinal = isMqBull;
            _isMqBearFinal = isMqBear;
            _isElevBull    = elevBull;
            _isElevBear    = elevBear;
            _isBullFinal   = (wouldBull && !isMqBull) || elevBull;
            _isBearFinal   = (wouldBear && !isMqBear) || elevBear;

            if (_isBullFinal || _isBearFinal)
            {
                aoPendingPrice = Close(b);
                aoPendingDir   = _isBullFinal ? 1 : -1;
                aoPendingBar   = b;
                aoPendingBest  = 0;
                aoPendingAtr   = atr;
                aoPendingLenBucket = 1;
            }
        }

        // ── Polynomial regression (degree 4, same as NT8 CalcPoly) ────────────
        private double CalcPolyDeg4(int b, int len)
        {
            int n = Math.Min(len, b);
            if (n < 5) return Close(b);
            double lenM1 = n - 1;
            double sumY=0, sX1=0, sX2=0, sX3=0, sX4=0, sX5=0, sX6=0, sX7=0, sX8=0;
            double sXY=0, sX2Y=0, sX3Y=0, sX4Y=0;
            for (int i = 0; i < n; i++)
            {
                double x  = i / lenM1;
                double y  = Close(b - (n-1-i));
                double x2 = x*x, x3=x2*x, x4=x2*x2;
                sumY+=y; sX1+=x; sX2+=x2; sX3+=x3; sX4+=x4;
                sX5+=x4*x; sX6+=x3*x3; sX7+=x4*x3; sX8+=x4*x4;
                sXY+=x*y; sX2Y+=x2*y; sX3Y+=x3*y; sX4Y+=x4*y;
            }
            int dim = 5;
            double[,] A = new double[dim, dim+1];
            double[] psum = {n,sX1,sX2,sX3,sX4,sX5,sX6,sX7,sX8};
            double[] dsum = {sumY,sXY,sX2Y,sX3Y,sX4Y};
            for (int r = 0; r < dim; r++)
            {
                for (int c = 0; c < dim; c++) A[r,c] = psum[r+c];
                A[r,dim] = dsum[r];
            }
            for (int col = 0; col < dim-1; col++)
            {
                int pivRow = col; double pivMax = Math.Abs(A[col,col]);
                for (int row = col+1; row < dim; row++) { double v = Math.Abs(A[row,col]); if (v > pivMax){pivMax=v; pivRow=row;} }
                if (pivRow != col) for (int k=0;k<=dim;k++){double t=A[col,k]; A[col,k]=A[pivRow,k]; A[pivRow,k]=t;}
                double pivot = A[col,col];
                if (Math.Abs(pivot) > 1e-10)
                    for (int row=col+1;row<dim;row++){double f=A[row,col]/pivot; for(int k=col;k<=dim;k++) A[row,k]-=f*A[col,k];}
            }
            double[] coeffs = new double[5];
            for (int ii=0;ii<dim;ii++){int r=dim-1-ii; double val=A[r,dim]; for(int cc=r+1;cc<dim;cc++) val-=A[r,cc]*coeffs[cc]; double d=A[r,r]; coeffs[r]=Math.Abs(d)>1e-10?val/d:0.0;}
            return coeffs[0]+coeffs[1]+coeffs[2]+coeffs[3]+coeffs[4];
        }

        // ── SuperTrend rail ───────────────────────────────────────────────────
        private void CalcSuperTrend(double src, double prevSrc, double factor, int atrPeriod,
                                    ref double upperPrev, ref double lowerPrev,
                                    ref double linePrev, ref int dir, out double line)
        {
            double atrLocal = BufATR(_bar, Math.Max(1, atrPeriod));
            double upper = src + factor * atrLocal;
            double lower = src - factor * atrLocal;
            double pU = double.IsNaN(upperPrev) ? upper : upperPrev;
            double pL = double.IsNaN(lowerPrev) ? lower : lowerPrev;
            if (!(lower > pL || prevSrc < pL)) lower = pL;
            if (!(upper < pU || prevSrc > pU)) upper = pU;
            int newDir;
            if (double.IsNaN(linePrev)) newDir = 1;
            else if (Math.Abs(linePrev - pU) < 1e-9) newDir = src > upper ? -1 : 1;
            else newDir = src < lower ? 1 : -1;
            line = newDir == -1 ? lower : upper;
            upperPrev = upper; lowerPrev = lower; linePrev = line; dir = newDir;
        }

        // ═════════════════════════════════════════════════════════════════════
        // MATH HELPERS
        // ═════════════════════════════════════════════════════════════════════

        private double TrueRange(int b)
        {
            if (b == 0) return High(b) - Low(b);
            return Math.Max(High(b)-Low(b), Math.Max(Math.Abs(High(b)-Close(b-1)), Math.Abs(Low(b)-Close(b-1))));
        }

        private double CalcRSI(int b, int len)
        {
            if (b < len) return 50.0;
            double gainSum = 0, lossSum = 0;
            for (int i = 0; i < len; i++)
            {
                double d = Close(b-i) - Close(b-i-1);
                if (d > 0) gainSum += d; else lossSum -= d;
            }
            double avgGain = gainSum / len, avgLoss = lossSum / len;
            if (avgLoss == 0) return 100.0;
            return 100.0 - 100.0 / (1.0 + avgGain / avgLoss);
        }

        private double CalcCCI(int b, int len)
        {
            int n = Math.Min(len, b);
            double sum = 0;
            for (int i = 0; i < n; i++) sum += (High(b-i)+Low(b-i)+Close(b-i))/3.0;
            double mean = sum / n;
            double mad  = 0;
            for (int i = 0; i < n; i++) mad += Math.Abs((High(b-i)+Low(b-i)+Close(b-i))/3.0 - mean);
            mad /= n;
            double tp = (High(b)+Low(b)+Close(b))/3.0;
            return mad > 1e-12 ? (tp - mean) / (0.015 * mad) : 0.0;
        }

        private double CalcMFI(int b, int len)
        {
            double posFlow = 0, negFlow = 0;
            for (int i = 0; i < Math.Min(len, b-1); i++)
            {
                double tp     = (High(b-i)+Low(b-i)+Close(b-i))/3.0;
                double tpPrev = (High(b-i-1)+Low(b-i-1)+Close(b-i-1))/3.0;
                double mf     = tp * Volume(b-i);
                if (tp > tpPrev) posFlow += mf; else negFlow += mf;
            }
            return negFlow > 0 ? 100.0 - 100.0 / (1.0 + posFlow / negFlow) : 100.0;
        }

        private double PercentRankValue(int b, double value, int cci_len, int lb)
        {
            int n = Math.Min(lb, b);
            if (n < 1) return 50.0;
            int below = 0;
            for (int i = 1; i < n; i++) if (CalcCCI(b-i, cci_len) < value) below++;
            return 100.0 * below / Math.Max(n-1, 1);
        }

        private double PercentRankBuf(double[] buf, int b, int lb)
        {
            int n = Math.Min(lb, b);
            if (n < 1) return 50.0;
            double cur = Buf(buf, b); int below = 0;
            for (int i = 1; i < n; i++) if (Buf(buf, b-i) < cur) below++;
            return 100.0 * below / Math.Max(n-1, 1);
        }

        private double HurstRS(int b, int len)
        {
            int n = Math.Min(len, b);
            if (n < 4) return 0.5;
            double mean = 0;
            for (int i = 0; i < n; i++) mean += Close(b-i);
            mean /= n;
            double maxD = -1e10, minD = 1e10, cum = 0, sumSq = 0;
            for (int i = 0; i < n; i++)
            {
                double diff = Close(b-i) - mean;
                cum += diff;
                if (cum > maxD) maxD = cum;
                if (cum < minD) minD = cum;
                sumSq += diff * diff;
            }
            double R = maxD - minD;
            double S = Math.Sqrt(sumSq / n);
            double RS = S > 1e-10 ? R / S : 0.0;
            double H = RS > 0 ? Math.Log(RS) / Math.Log(n) : 0.5;
            return Math.Max(0.0, Math.Min(1.0, H));
        }

        private double KamaVB(double[] src, int b, int len, double[] store)
        {
            double signal = Math.Abs(Buf(src,b) - Buf(src, Math.Max(0,b-len)));
            double noise  = 0;
            for (int i = 0; i < Math.Min(len, b-1); i++) noise += Math.Abs(Buf(src,b-i) - Buf(src,b-i-1));
            double er     = noise > 1e-12 ? signal / noise : 0.0;
            double smooth = Math.Pow(er * (0.666 - 0.0645) + 0.0645, 2);
            double prev   = b >= 1 ? Buf(store, b-1) : Buf(src, b);
            return prev + smooth * (Buf(src,b) - prev);
        }

        private double KamaVBClose(int b, int len, double[] store)
        {
            double signal = Math.Abs(Close(b) - Close(Math.Max(0, b-len)));
            double noise  = 0;
            for (int i = 0; i < Math.Min(len, b-1); i++) noise += Math.Abs(Close(b-i) - Close(b-i-1));
            double er     = noise > 1e-12 ? signal / noise : 0.0;
            double smooth = Math.Pow(er * (0.666 - 0.0645) + 0.0645, 2);
            double prev   = b >= 1 ? Buf(store, b-1) : Close(b);
            return prev + smooth * (Close(b) - prev);
        }

        private double Correlation(int b, int len)
        {
            int n = Math.Min(len, b);
            if (n < 2) return 0.0;
            double sx=0, sy=0;
            for (int i=0;i<n;i++){sx+=i; sy+=Close(b-n+1+i);}
            double mx=sx/n, my=sy/n, cov=0, vx=0, vy=0;
            for (int i=0;i<n;i++){double dx=i-mx, dy=Close(b-n+1+i)-my; cov+=dx*dy; vx+=dx*dx; vy+=dy*dy;}
            double d=Math.Sqrt(vx*vy);
            return d>1e-10?cov/d:0.0;
        }

        private double WeightedAvg(List<int> scores)
        {
            if (scores.Count == 0) return 0.0;
            double sum=0, wsum=0; int total=scores.Count;
            for (int i=0;i<total;i++){double r=(double)(i+1)/total, w=r*r; sum+=scores[i]*w; wsum+=w;}
            return wsum>0?sum/wsum:0.0;
        }

        private int Quantize(double val, int step) => Math.Max(step, (int)Math.Round(val/step)*step);

        private int SecondsPerBar()
        {
            // QuantTower exposes HistoricalData.Period
            if (HistoricalData?.Period == null) return 60;
            switch (HistoricalData.Period.PeriodType)
            {
                case PeriodType.Second: return HistoricalData.Period.Frequency;
                case PeriodType.Minute: return HistoricalData.Period.Frequency * 60;
                case PeriodType.Hour:   return HistoricalData.Period.Frequency * 3600;
                case PeriodType.Day:    return HistoricalData.Period.Frequency * 86400;
                default:                return 60;
            }
        }

        // ── Ring buffer SMA / EMA / ATR helpers ──────────────────────────────
        private double BufSMA(double[] buf, int b, int len)
        {
            int n = Math.Min(len, b); if (n < 1) return Buf(buf, b);
            double sum = 0; for (int i=0;i<n;i++) sum += Buf(buf, b-i);
            return sum / n;
        }

        private double BufSMAClose(int b, int len)
        {
            int n = Math.Min(len, b); if (n < 1) return Close(b);
            double sum = 0; for (int i=0;i<n;i++) sum += Close(b-i);
            return sum / n;
        }

        private double BufEMA(int b, int len)
        {
            // Simple EMA on close using last len bars
            double alpha = 2.0 / (len + 1.0);
            int n = Math.Min(len, b);
            double ema = Close(b - n);
            for (int i=n-1;i>=0;i--) ema = ema + alpha * (Close(b-i) - ema);
            return ema;
        }

        private double BufATR(int b, int len)
        {
            if (b < len) return High(b) - Low(b);
            double atr = TrueRange(b - len);
            for (int i = len-1; i >= 0; i--) atr = (atr * (len-1) + TrueRange(b-i)) / len;
            return atr;
        }

        private double BufStdDev(int b, int len)
        {
            int n = Math.Min(len, b); if (n < 2) return 0.0;
            double mean = BufSMAClose(b, n), sumSq = 0;
            for (int i=0;i<n;i++){double d=Close(b-i)-mean; sumSq+=d*d;}
            return Math.Sqrt(sumSq / n);
        }

        // ── Ring buffer accessors ─────────────────────────────────────────────
        private static double Buf(double[] buf, int b) => buf[b & (BUF-1)];
        private static int    Buf(int[]    buf, int b) => buf[b & (BUF-1)];
        private static void   Set(double[] buf, int b, double v) => buf[b & (BUF-1)] = v;
        private static void   Set(int[]    buf, int b, int    v) => buf[b & (BUF-1)] = v;
        private static double[] Fill(double[] buf, double v) { for (int i=0;i<buf.Length;i++) buf[i]=v; return buf; }

        // ── QuantTower OHLCV accessors ────────────────────────────────────────
        // QuantTower provides HistoricalData indexed from newest (0) to oldest.
        // We invert: our bar index 0 = oldest, _bar = newest.
        // QT index = (_bar - b)
        private double Open  (int b) => HistoricalData[_bar - b].Open;
        private double High  (int b) => HistoricalData[_bar - b].High;
        private double Low   (int b) => HistoricalData[_bar - b].Low;
        private double Close (int b) => HistoricalData[_bar - b].Close;
        private double Volume(int b) => HistoricalData[_bar - b].Volume;

        // ── Theme ─────────────────────────────────────────────────────────────
        private static Color ColorFromHex(string hex)
        {
            byte r = Convert.ToByte(hex.Substring(1,2),16);
            byte g = Convert.ToByte(hex.Substring(3,2),16);
            byte bv= Convert.ToByte(hex.Substring(5,2),16);
            return Color.FromArgb(r,g,bv);
        }

        private void ResolveTheme()
        {
            string bull="#00FFFF", bear="#FF0000", neut="#888888", hilight="#FFFFFF";
            string sBull="#FFD700", sBear="#FFFFFF";
            switch (DarkTheme)
            {
                case TADarkTheme.Modern:        bull="#00FFFF"; bear="#FF0000"; neut="#888888"; hilight="#FFFFFF"; sBull="#FFD700"; sBear="#FFFFFF"; break;
                case TADarkTheme.Terminal:      bull="#00FF00"; bear="#FF6000"; neut="#707070"; hilight="#FFFFFF"; sBull="#FFFFFF"; sBear="#FF5500"; break;
                case TADarkTheme.Cyberpunk:     bull="#00FFFF"; bear="#FF00FF"; neut="#9932CC"; hilight="#FFFF00"; sBull="#FFFF00"; sBear="#FF00CC"; break;
                case TADarkTheme.NeonNoir:      bull="#FF1DDF"; bear="#00C8FF"; neut="#6A0DAD"; hilight="#FFFF00"; sBull="#FFFF00"; sBear="#FF6EC7"; break;
                case TADarkTheme.Phosphor:      bull="#FFB000"; bear="#CC2200"; neut="#7A5000"; hilight="#FFFFFF"; sBull="#FFFFFF"; sBear="#FF6600"; break;
                case TADarkTheme.FireAndIce:    bull="#C8F0FF"; bear="#FF4500"; neut="#6699AA"; hilight="#FFD700"; sBull="#FFD700"; sBear="#FF6347"; break;
                case TADarkTheme.Slate:         bull="#00CED1"; bear="#E8735A"; neut="#708090"; hilight="#F5F5DC"; sBull="#F5DEB3"; sBear="#DC143C"; break;
                case TADarkTheme.BloodAndGreed: bull="#00FF41"; bear="#CC0000"; neut="#555555"; hilight="#FFD700"; sBull="#FFD700"; sBear="#FF6600"; break;
                case TADarkTheme.Matrix:        bull="#00FF41"; bear="#007A1F"; neut="#003D0F"; hilight="#FFFFFF"; sBull="#AAFFBB"; sBear="#FFFFFF"; break;
                case TADarkTheme.Arctic:        bull="#A8D8FF"; bear="#4488CC"; neut="#3A5A8A"; hilight="#F0F8FF"; sBull="#F0F8FF"; sBear="#00BFFF"; break;
                default: break;
            }
            bullColor    = ColorFromHex(bull);
            bearColor    = ColorFromHex(bear);
            neutColor    = ColorFromHex(neut);
            hilightColor = ColorFromHex(hilight);
            sigBullColor = ColorFromHex(sBull);
            sigBearColor = ColorFromHex(sBear);

            // Update LineSeries colors to match theme
            lsBull  .Color = sigBullColor;
            lsBear  .Color = sigBearColor;
            lsMqBull.Color = sigBullColor;
            lsMqBear.Color = sigBearColor;
            lsCMA1  .Color = bullColor;
            lsCMA2  .Color = neutColor;
            lsSCTop .Color = bullColor;
            lsSCBot .Color = bearColor;
            lsTCBase.Color = neutColor;
            lsTCTop .Color = neutColor;
        }

        // ── OnClear ───────────────────────────────────────────────────────────
        protected override void OnClear()
        {
            // Reset all state for replay / instrument change
            _bar = 0;
            fhaVolEst = double.NaN; fhaVolErr = 1.0;
            fhaPvpEst = double.NaN; fhaPvpErr = 1.0;
            fhaPvpVar = 1.0; cvHaO = double.NaN; cvLrhaO = double.NaN;
            prismSt1LinePrev = prismSt2LinePrev = double.NaN;
            prismSt1UpperPrev = prismSt1LowerPrev = double.NaN;
            prismSt2UpperPrev = prismSt2LowerPrev = double.NaN;
            prismSt1Dir = prismSt2Dir = prismLastDir = 1; prismLastSig = 0;
            prismBullHoldUntil = prismBearHoldUntil = -1;
            prismBqBullHoldUntil = prismBqBearHoldUntil = -1;
            trgHurstCached = 0.5;
            aoPendingPrice = double.NaN;
            aoScoresS.Clear(); aoScoresM.Clear(); aoScoresL.Clear();
        }
    }
}
