# SentinelZeroLagTEMA_v1_0_0.cs

> `bin/Custom/Indicators/SentinelZeroLagTEMA_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 252 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelZeroLagTEMA_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel.Smoothers` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 Sentinel ZeroLagTEMA — Zero-Lag Triple EMA (Sentinel smoother building block)   |   Version v1.0.0
 File: SentinelZeroLagTEMA_v1_0_0.cs  |  namespace …Indicators.Sentinel.Smoothers  |  display "Sentinel ZeroLagTEMA"

 ⚠ NO ORDERS · NO STATE SEAM — a read-only SMOOTHER building block, not a Council voter. It draws a
 smoothed line + a Sentinel glass card; it publishes nothing.

 PROVENANCE / LICENSE: CLEAN-ROOM. Reimplemented from the public TEMA (Patrick Mulloy) + zero-lag
 error-correction construction — mathematical methods, not copyrightable. NO third-party code, variable
 names, or structure were copied; the "Au" filter pack was read ONLY to identify the variant.
 Method (de-lag by echoing the lag of TEMA back onto itself):
   α    = 2 / (Period + 1)
   ema1 = EMA(Input) ; ema2 = EMA(ema1) ; ema3 = EMA(ema2)
   tema = 3·ema1 − 3·ema2 + ema3                      // TEMA of the input
   then TEMA the TEMA:  f1=EMA(tema) ; f2=EMA(f1) ; f3=EMA(f2)
   temaOfTema = 3·f1 − 3·f2 + f3
   zl   = tema + (tema − temaOfTema)  ==  2·tema − temaOfTema

 ASSUMPTIONS / NOTES:
   • The identified "Au" variant is the double-TEMA error-correction form  2·TEMA − TEMA(TEMA)  (equivalently
     tema + (tema − tema_of_tema)); this build reproduces that variant with hand-rolled EMA recurrences.
   • EMAs are seeded with the first input value on bar 0 (standard NT EMA seeding).

 CHANGELOG
   v1.0.0 (2026-07-12) — clean-room Zero-Lag TEMA (2·TEMA − TEMA∘TEMA) + Sentinel plumbing (naming law, glass card, label remover).
```

