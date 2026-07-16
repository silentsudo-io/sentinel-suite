# Quick Reference Guide - NinjaTrader Advanced Suite

## 📌 INDICATORS AT A GLANCE

| Indicator | Purpose | Output | Best Used For |
|-----------|---------|--------|---------------|
| **AdaptiveRegimeDetector** | Market regime classification | 0-3 (Ranging/Trending/Breakout/Exhaustion) | Strategy filters, position sizing |
| **OrderFlowDivergence** | Price vs Delta divergences | ±1 or 0 (Bullish/Bearish/None) | Reversal signals, trend exhaustion |
| **LiquidityPoolMapper** | Unmitigated S/R zones | Visual zones on chart | Target areas, stop placement |
| **MTFConfluenceOscillator** | Multi-timeframe strength | -100 to +100 | Trade bias, entry filters |

---

## 🎯 STRATEGIES AT A GLANCE

| Strategy | Entry Style | Best Markets | Avg Holding Time | Risk Profile |
|----------|-------------|--------------|------------------|--------------|
| **RegimeAdaptiveStrategy** | Fractals + Breakouts | All conditions | Variable (regime-based) | Medium |
| **DeltaBreakoutStrategy** | Consolidation breakouts | Liquid futures (ES/NQ) | 10-30 minutes | Medium-High |
| **RenkoMeanReversionScalper** | Oversold/Overbought | Renko charts only | 5-15 Renko bars | Low-Medium |
| **WiseManV0002** | Fractal reversals | All (enhanced filters) | Variable | Medium |

---

## 🔥 QUICK SETUP GUIDE

### Setup 1: Trend Trader
```
Chart: 5-minute ES
Indicators:
  - AdaptiveRegimeDetector (background colors ON)
  - MTFConfluenceOscillator (panel below)
  
Strategy: RegimeAdaptiveStrategy
Settings:
  - FractalStrength: 5
  - TrendingMultiplier: 1.5
  - BreakoutMultiplier: 2.0
```

### Setup 2: Breakout Hunter
```
Chart: 3-minute NQ
Indicators:
  - LiquidityPoolMapper (show swept zones)
  - OrderFlowDivergence (enable alerts)
  
Strategy: DeltaBreakoutStrategy
Settings:
  - ConsolidationBars: 12
  - MinDeltaChange: 1500
  - Enable Tick Replay!
```

### Setup 3: Renko Scalper
```
Chart: 4-tick Renko ES
Indicators:
  - AdaptiveRegimeDetector (labels OFF, just background)
  - VWAP (built-in)
  
Strategy: RenkoMeanReversionScalper
Settings:
  - MaxADX: 22
  - ScalpTarget: 6 ticks
  - UseDynamicBands: true
```

---

## ⚙️ DEFAULT PARAMETERS CHEAT SHEET

### AdaptiveRegimeDetector
```
ADX Period: 14
ATR Period: 14
RSI Period: 14
Lookback: 50
Background Colors: ON
```

### OrderFlowDivergence
```
Swing Strength: 5
Lookback Bars: 50
Min Swing Size: 10 ticks
Show Lines: ON
Enable Alerts: OFF (turn ON for live trading)
```

### LiquidityPoolMapper
```
Swing Strength: 5
Min Volume Multiplier: 1.5
Max Zones: 50
Extension Bars: 100
Show Unmitigated: ON
Show Swept: ON
```

### MTFConfluenceOscillator
```
TF2 Multiplier: 3
TF3 Multiplier: 9
RSI Period: 14
MACD: 12/26/9
Weights: 50/30/20
Strong Threshold: 60
Moderate Threshold: 30
```

### RegimeAdaptiveStrategy
```
Fractal Strength: 5
Base Position: 1
Multipliers: 0.5/1.0/1.5/0.25
ATR Stop: 2.0x
Trailing Stop: 10 ticks
Breakeven: 8 ticks
```

### DeltaBreakoutStrategy
```
Consolidation Bars: 10
Range Contraction: 30%
Min Delta: 1000
Volume Spike: 1.5x
ATR Stop: 2.0x
Targets: 2.0x/3.5x/5.0x
Partial Exits: 33/33/34%
```

### RenkoMeanReversionScalper
```
RSI Period: 14
RSI OB/OS: 70/30
Max ADX: 25
Min ADX: 10
Band Deviation: 1.5
Scalp Target: 8 ticks
Extended Target: 15 ticks
Stop Loss: 12 ticks
Max Hold: 20 bars
```

---

## 🚨 COMMON ISSUES & FIXES

### Issue: Indicator not appearing in list
**Fix:** Compile all scripts (F5 in NinjaScript Editor)

### Issue: Strategy won't place orders
**Fix:** 
- Check BarsRequiredToTrade value
- Ensure EnableHistorical is ON for backtests
- Verify EnableLongTrades/EnableShortTrades

### Issue: OrderFlowDivergence showing no signals
**Fix:**
- Reduce SwingStrength (try 3)
- Reduce MinimumSwingSize (try 5)
- Enable tick replay for accurate delta

### Issue: DeltaBreakoutStrategy no entries
**Fix:**
- Reduce ConsolidationBars (try 8)
- Reduce MinDeltaChange (try 500)
- Check if RequireDeltaAlignment is too strict

### Issue: RenkoMeanReversionScalper too many trades
**Fix:**
- Increase MaxADX (try 20)
- Tighten RSI levels (75/25 instead of 70/30)
- Increase MinVolumeMultiplier

### Issue: MTFConfluenceOscillator always neutral
**Fix:**
- Check timeframe multipliers match chart type
- Adjust weights to favor primary timeframe
- Enable/disable components to find best mix

---

## 📊 OPTIMIZATION TIPS

### What to Optimize:
✅ Entry thresholds (RSI levels, ADX values)
✅ Stop loss and target multipliers
✅ Position size multipliers by regime
✅ Timeframe multipliers

### What NOT to Optimize:
❌ Indicator periods (use standard 14, 20, etc.)
❌ Too many parameters at once
❌ Over-fitting to specific date ranges
❌ Rare edge cases

### Optimization Process:
1. **Choose 2-3 parameters** to optimize
2. **Set reasonable ranges** (±30% of default)
3. **Use walk-forward analysis** (in-sample vs out-sample)
4. **Check multiple metrics:**
   - Profit factor > 1.5
   - Win rate > 50% (or high win-to-loss ratio)
   - Max drawdown < 20% of profit
   - Minimum trades > 50

---

## 💡 TRADING TIPS

### Best Practices:
1. **Start with ONE strategy** and master it
2. **Trade during liquid hours** (9:30-16:00 ET for US indices)
3. **Avoid major news events** initially
4. **Monitor regime changes** - be ready to stop trading in unsuitable conditions
5. **Use sim mode for 2+ weeks** before live trading
6. **Review trades daily** - what worked, what didn't
7. **Keep a trading journal** with screenshots

### When to Use Each Strategy:

**RegimeAdaptiveStrategy:**
- ✅ All market conditions (adapts automatically)
- ✅ When you want "set and forget"
- ❌ Avoid during major events (manual override)

**DeltaBreakoutStrategy:**
- ✅ Liquid markets with good tick data
- ✅ During RTH (Regular Trading Hours)
- ❌ Low volume periods
- ❌ Without tick replay enabled

**RenkoMeanReversionScalper:**
- ✅ Ranging/choppy markets (low ADX)
- ✅ On properly configured Renko charts
- ❌ Strong trending days
- ❌ During breakout conditions

---

## 📈 PERFORMANCE EXPECTATIONS

### Realistic Targets (per 100 trades):

**RegimeAdaptiveStrategy:**
- Win Rate: 45-55%
- Profit Factor: 1.4-1.8
- Avg Win/Loss: 1.5:1
- Max Drawdown: 15-25% of profit

**DeltaBreakoutStrategy:**
- Win Rate: 40-50%
- Profit Factor: 1.6-2.2
- Avg Win/Loss: 2:1
- Max Drawdown: 20-30% of profit

**RenkoMeanReversionScalper:**
- Win Rate: 55-65%
- Profit Factor: 1.3-1.6
- Avg Win/Loss: 1:1.2
- Max Drawdown: 10-20% of profit

**Note:** These are rough guidelines. Actual performance varies by market, timeframe, and parameters.

---

## 🎓 LEARNING PATH

### Week 1: Understanding
- Read full documentation
- Add all indicators to charts
- Observe behavior in different market conditions
- Take notes on regime changes

### Week 2: Backtesting
- Run Strategy Analyzer on each strategy
- Test different parameter sets
- Compare results across instruments
- Identify best setup for your trading style

### Week 3: Sim Trading
- Enable ONE strategy in simulation
- Monitor real-time performance
- Track actual fills vs backtest expectations
- Adjust parameters if needed

### Week 4: Refinement
- Review sim trading results
- Identify weaknesses (time of day, market conditions)
- Add custom filters if needed
- Final parameter optimization

### Week 5+: Live Trading
- Start with minimum position size
- Trade only during YOUR optimal hours
- Keep risk per trade at 1% max
- Scale up gradually as confidence builds

---

## 🔗 QUICK LINKS

**NinjaTrader Resources:**
- Support Forum: https://ninjatrader.com/support/forum/
- NinjaScript Documentation: https://ninjatrader.com/support/helpGuides/nt8/
- Video Tutorials: https://ninjatrader.com/support/videos/

**Trading Education:**
- Market regime concepts
- Order flow analysis
- Multi-timeframe analysis
- Mean reversion vs momentum

---

## ✅ PRE-LIVE CHECKLIST

Before trading live with any strategy:

- [ ] Backtested 3+ months of data
- [ ] Sim traded 2+ weeks
- [ ] Win rate meets expectations
- [ ] Max drawdown is acceptable
- [ ] Understand every parameter
- [ ] Can explain why each trade was taken
- [ ] Risk management is programmed correctly
- [ ] Stops and targets execute properly
- [ ] Commission/slippage included in tests
- [ ] Emergency stop procedure in place

---

## 🆘 EMERGENCY PROCEDURES

### If Strategy Goes Haywire:
1. **IMMEDIATELY:** Click "Disable" on strategy
2. **Flatten all positions** manually if needed
3. **Check Strategy Analyzer** logs
4. **Review last trades** in Trade Performance
5. **Identify root cause** before re-enabling

### If Indicators Malfunction:
1. **Remove from chart**
2. **Check for error messages** in Output Window
3. **Recompile** NinjaScript (F5)
4. **Re-add to chart**
5. **If persists:** Check parameters for invalid values

---

*Keep this guide handy for quick reference during trading hours!*

---

**Last Updated:** January 8, 2026
**Version:** 1.0
