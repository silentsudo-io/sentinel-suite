---
layout: sentinel-ref
title: "SentinelADXVMA_v1_0_0.cs"
blurb: "Indicators · 1.0.0 · 408 lines"
---

# SentinelADXVMA_v1_0_0.cs

> `bin/Custom/Indicators/SentinelADXVMA_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 408 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelADXVMA_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors` |
| **Publishes seams** | `AdxvmaState` |
| **Documented by** | _no doc tracks this artifact_ |
| **Depends on this** | [Council_v1_11_0.cs](council-v1-11-0-cs.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 Sentinel ADXVMA — the ADX Volatility Moving Average axis (CLEAN-ROOM)      |   Version v1.0.0
 File: SentinelADXVMA_v1_0_0.cs   |   namespace …Indicators.Sentinel.Sensors (Tier-③ SENSOR)   |   Name "Sentinel ADXVMA"

 ⚠ NO ORDERS — read-only advisory indicator. Safe to run anywhere.

 CLEAN-ROOM ORIGINAL. Written from scratch off the PUBLIC ADXVMA (ADX Volatility Moving Average)
 method — a published, non-copyrightable adaptive-moving-average formula. It uses NO third-party code.
 The installed AuADXVMA.cs / auADXVMASignalMod.cs (an unlicensed "Au" pack) were surveyed as design
 references for the CONCEPT ONLY — the recursion below is a fresh implementation of the public formula;
 no code was copied. See the provenance audit + NOTICE.

 WHY IT MATTERS — a plain EMA lags equally in trend and in chop. ADXVMA drives its smoothing constant
 from a Wilder-smoothed directional-volatility index: when direction is strong the MA snaps toward price;
 when the tape is indecisive the MA nearly freezes. The result is a self-adaptive trend rail the Council
 can lean with, plus a clean chop read (the MA goes flat) that context modulators care about.

 THE PUBLIC FORMULA:
   • up / down moves    : upMove = max(Close−Close[1], 0) · downMove = max(Close[1]−Close, 0)
   • Wilder smooth (k=1/Period, i.e. rma): up  = (1−k)·up[1]  + k·upMove
                                           down = (1−k)·down[1]+ k·downMove
   • directional idx    : DI+ = up/(up+down) · DI− = down/(up+down)
   • DX                 : |DI+ − DI−| / (DI+ + DI−)                                   [in 0…1]
   • volatility index vi: Wilder-smoothed DX → vi = (1−k)·vi[1] + k·DX                 [in 0…1]
   • adaptive MA        : adxvma = adxvma[1] + (vi^K)·(Close − adxvma[1])              [K≈2 sharpens response]
   • TREND (trinary, ATR-deadband + hysteresis): let band = ATR(AtrLength)·DeadbandMult;
       slope = adxvma − adxvma[1].  slope > +band → +1 (rising)  ·  slope < −band → −1 (falling) ·
       inside the band → HOLD the last non-flat side (hysteresis carries the trend through minor
       pullbacks) UNLESS the MA has gone genuinely flat over the ATR window → 0 (CHOP).
       A reversal must therefore cross the OPPOSITE band, never flip +1↔−1 directly.
   • Bias / Signal      = that trinary trend (−1/0/+1). STATE voter (always a reading; neutral in chop).

 THE SENTINEL PLUMBING (our own code — makes it a suite member):
   • PUBLISHES SentinelCore.AdxvmaState (Bias / Value = the MA / Signal).
   • WIRED INTO THE COUNCIL as the ADXVMA voter (a STATE voter on AdxvmaState.Signal).
   • Hidden ±1 "Signal" PLOT (Values[1], transparent) for the Deck SIGNAL ARM / generic consumers.
   • Trend-colored adaptive-MA line on the price panel + a SentinelSkin.Painter glass card +
     label remover + roster heartbeat.

 CHANGELOG
   v1.0.0 (2026-07-12) — NEW. Clean-room ADXVMA axis (Wilder directional-volatility index → vi^K adaptive
            MA + ATR-deadband hysteresis trend). AdxvmaState publish, Council ADXVMA voter, hidden Signal
            plot, trend-colored MA line, glass card, scope key + heartbeat.
```

