# Sentinel Suite

A family of open-source NinjaTrader 8 trading tools built around intelligent
signal arming, trade management, risk control, and trade logging.

## Products

| Product | Status | Description |
|---------|--------|-------------|
| **Sentinel** | 🔨 In Development | Floating trade panel — signal arming, order management, risk, trailing |
| **Sentinel Log** | 📋 Planned | Trade journal — JSONL logger, MAE/MFE, context capture |
| **Sentinel Risk** | 📋 Planned | Standalone session risk monitor |
| **Sentinel Lens** | 📋 Planned | Analytics overlay — equity curve, win rate, trade stats |
| **Sentinel Arc** | 📋 Planned | Strategy automation layer |
| **Sentinel Eye** | 📋 Planned | Multi-instrument signal scanner |

## Installation

### NinjaTrader 8 Indicators
Copy files from `NinjaTrader/Indicators/` into:
```
Documents\NinjaTrader 8\bin\Custom\Indicators\
```
Then recompile via Tools → NinjaScript Editor → Compile.

### TrendArchitect Skin
Copy the `NinjaTrader/Skin/TrendArchitect/` folder into:
```
Documents\NinjaTrader 8\bin\Custom\Skins\
```
Then apply via Tools → Options → General → Skin → TrendArchitect.

### QuantTower
Copy files from `QuantTower/` into your QuantTower indicators folder.

## Credits

- TrendArchitect indicator: _Jason / B3AR
- UI design system: Khanh — DailyRangeBot (open source)
- V1.1 hardening: Spoobie
- V1.4 optimizations: Spoobie
- V1.5 features: Spoobie

## License

Open source. See individual file headers for attribution details.
