---
layout: sentinel-ref
title: "SentinelSuperTrend_v1_0_0.cs"
blurb: "Indicators · 1.0.0 · 379 lines"
---

# SentinelSuperTrend_v1_0_0.cs

> `bin/Custom/Indicators/SentinelSuperTrend_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 379 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelSuperTrend_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors` |
| **Publishes seams** | `SuperTrendState` |
| **Documented by** | _no doc tracks this artifact_ |
| **Depends on this** | [Council_v1_11_0.cs](council-v1-11-0-cs.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 Sentinel SuperTrend — the ATR-BAND TREND axis (CLEAN-ROOM)                |   Version v1.0.0
 File: SentinelSuperTrend_v1_0_0.cs   |   namespace …Indicators.Sentinel.Sensors (Tier-③ SENSOR)   |   Name "Sentinel SuperTrend"

 ⚠ NO ORDERS — read-only advisory indicator. Safe to run anywhere.

 CLEAN-ROOM ORIGINAL. Written from scratch off the PUBLIC SuperTrend formula — a published,
 non-copyrightable trend-following method: ATR bands around the median price with a trailing flip.
 It uses NO third-party code. The installed AuSuperTrendU11.cs (unlicensed "Au" pack) was surveyed
 as a DESIGN REFERENCE only — no code was copied. See the provenance audit + NOTICE.

 ⚠ v1.0.0 uses an INLINE ATR baseline (HL2 ± Multiplier×ATR) — it does NOT reproduce Au's full
   20-moving-average library selector (that is DEFERRED to a later version).

 THE PUBLIC FORMULA:
   • hl2        = (High + Low) / 2
   • atr        = ATR(AtrPeriod)
   • upperBasic = hl2 + Multiplier·atr        ·  lowerBasic = hl2 − Multiplier·atr
   • trailing bands (standard clamp):
       finalUpper = (upperBasic < finalUpper[1] || Close[1] > finalUpper[1]) ? upperBasic : finalUpper[1]
       finalLower = (lowerBasic > finalLower[1] || Close[1] < finalLower[1]) ? lowerBasic : finalLower[1]
   • direction flip:
       if prev SuperTrend == finalUpper[1]:  dir = Close > finalUpper ? +1 : −1
       else                                :  dir = Close < finalLower ? −1 : +1
     SuperTrend line = dir>0 ? finalLower : finalUpper.
   • Bias/Signal = dir (±1) — a STATE voter (always ±1).
   • Flip        = +1/−1 pulse on the bar the direction changes, else 0.
   • Line        = the SuperTrend trailing-line value.

 THE SENTINEL PLUMBING (our own code — makes it a suite member):
   • PUBLISHES SentinelCore.SuperTrendState (Bias / Line / Flip / Signal).
   • WIRED INTO THE COUNCIL as the SUPERTREND voter (a STATE voter on SuperTrendState.Signal).
   • Hidden ±1 "Signal" PLOT (Values[1], transparent) for the Deck SIGNAL ARM / generic consumers.
   • Green/red SuperTrend trailing line (Values[0]) + a SentinelSkin.Painter glass card +
     label remover + roster heartbeat.

 CHANGELOG
   v1.0.0 (2026-07-12) — NEW. Clean-room ATR-band SuperTrend trend axis (inline HL2 ± Multiplier×ATR
            baseline; full 20-MA library selector deferred). SuperTrendState publish, Council SUPERTREND
            voter, hidden Signal plot, green/red trailing line, glass card, scope key + heartbeat.
```

