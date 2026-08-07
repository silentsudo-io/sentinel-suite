# SentinelZeroLagHATEMA_v1_0_0.cs

> `bin/Custom/Indicators/SentinelZeroLagHATEMA_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 264 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelZeroLagHATEMA_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel.Smoothers` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 Sentinel ZeroLagHATEMA — Zero-Lag TEMA on Heikin-Ashi price (Sentinel smoother building block)   |   Version v1.0.0
 File: SentinelZeroLagHATEMA_v1_0_0.cs  |  namespace …Indicators.Sentinel.Smoothers  |  display "Sentinel ZeroLagHATEMA"

 ⚠ NO ORDERS · NO STATE SEAM — a read-only SMOOTHER building block, not a Council voter. It draws a
 smoothed line + a Sentinel glass card; it publishes nothing.

 PROVENANCE / LICENSE: CLEAN-ROOM. Reimplemented from the public Heikin-Ashi transform + TEMA (Patrick
 Mulloy) + zero-lag error-correction construction — mathematical methods, not copyrightable. NO third-party
 code, variable names, or structure were copied; the "Au" filter pack was read ONLY to identify the variant.
 Method:
   Heikin-Ashi close:  haClose = (Open + High + Low + Close) / 4
   Heikin-Ashi open:   haOpen  = (haOpen[1] + haClose[1]) / 2      (seeded (Open+Close)/2 on bar 0)
   Then the same zero-lag TEMA as SentinelZeroLagTEMA, applied to the haClose series:
     α = 2/(Period+1) ; tema = 3·EMA1 − 3·EMA2 + EMA3 of haClose ; then TEMA the tema →
     zl = tema + (tema − tema_of_tema)  ==  2·tema − tema_of_tema

 ASSUMPTIONS / NOTES:
   • The "Au" source used a NON-canonical HA close (a 4-component smoothed haClose incorporating haOpen and
     Max/Min guards) and derived haOpen from the PRIOR bar's OHLC average. This clean-room build uses the
     CANONICAL Heikin-Ashi definitions above (haClose=(O+H+L+C)/4, haOpen=(haOpen[1]+haClose[1])/2) per the
     specified form — HA values therefore differ slightly from the Au variant by design.
   • The smoother is applied to the HA CLOSE series (haOpen is computed for HA correctness but not smoothed).
   • Operates on the chart's OHLC price bars (not an arbitrary Input series).
   • EMAs are seeded with haClose on bar 0.

 CHANGELOG
   v1.0.0 (2026-07-12) — clean-room Zero-Lag TEMA on canonical Heikin-Ashi close + Sentinel plumbing (naming law, glass card, label remover).
```

