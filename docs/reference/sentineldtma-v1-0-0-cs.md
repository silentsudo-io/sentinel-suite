# SentinelDTMA_v1_0_0.cs

> `bin/Custom/Indicators/SentinelDTMA_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 240 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelDTMA_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel.Smoothers` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 Sentinel DTMA — de-lagged Double Triangular Moving Average (smoother block) |   Version v1.0.0
 File: SentinelDTMA_v1_0_0.cs  |  namespace …Indicators.Sentinel.Smoothers  |  display "Sentinel DTMA"

 ⚠ NO ORDERS · NO STATE SEAM — a read-only SMOOTHER building block, not a Council voter. It draws a
 smoothed line + a Sentinel glass card and publishes nothing.

 IDENTITY OF THE SOURCE: the "AuDTMA" source (Description "DTMA — Double Triangular Moving Average")
 computes  y = 2·TMA(x,P) − TMA(TMA(x,P),P)  — a "twicing" / de-lagged Triangular MA (the TMA analogue
 of DEMA), where TMA (Triangular MA) is itself a double SMA. This port reimplements THAT method.

 PROVENANCE / LICENSE: CLEAN-ROOM. Reimplemented from PUBLIC, standard constructions — the Triangular
 Moving Average (TMA = SMA∘SMA, a triangular-weighted window) and the "twicing" de-lag
 (double_MA = 2·MA − MA∘MA). Mathematical methods, not copyrightable. No third-party code, variable
 names, or structure were copied; the "Au" source was read ONLY to identify the method (Double TMA).

 MATH:  m   = (P+1)/2  (integer sub-window)
        TMA(z) = SMA( SMA(z, m), m )          (triangular weighting)
        t1  = TMA(input);  t2 = TMA(t1)
        Value = 2·t1 − t2                      (de-lagged double TMA)

 ASSUMPTIONS:
   • TMA is implemented as the canonical double-SMA with sub-window m = (P+1)/2. Different platforms round
     the even-P sub-window slightly differently (some use ceil(P/2) vs floor(P/2)+1); (P+1)/2 is the
     common, symmetric choice and reproduces the intended triangular smoothing. Choice noted here.
   • Warm-up uses shrinking windows (min(CurrentBar+1, m)) so the line is defined from bar 0.
   • Intermediate SMA passes are held in private Series so each pass smooths the actual running prior pass.

 CHANGELOG
   v1.0.0 (2026-07-12) — clean-room Double TMA (de-lag) + Sentinel plumbing (naming law, glass card, label remover).
```

