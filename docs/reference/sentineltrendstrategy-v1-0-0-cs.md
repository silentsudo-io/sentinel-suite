---
layout: sentinel-ref
title: "SentinelTrendStrategy_v1_0_0.cs"
blurb: "Strategies · 1.0.0 · 297 lines"
---

# SentinelTrendStrategy_v1_0_0.cs

> `bin/Custom/Strategies/SentinelTrendStrategy_v1_0_0.cs`

| | |
|---|---|
| **Family** | Strategies |
| **Version** | 1.0.0 |
| **Size** | 297 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelTrendStrategy_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Strategies` |
| **Consumes seams** | `AdxState` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 SentinelTrendStrategy — risk-managed trend-flip strategy on SentinelTrend   |   Version v1.0.0
 File: SentinelTrendStrategy_v1_0_0.cs   |   namespace …Strategies

 WHAT THIS IS — the single, risk-managed replacement for the five NT-Wizard TrendMagic strategies
 (TMEntry / TMEntry50 / TMXEntryExit / TMSquared / TripleTM). Those either had NO stop (TMEntry),
 a target with NO stop (TMEntry50 — tiny wins, unbounded losses), or only painted the background
 and placed no trades at all (TMSquared / TripleTM). This one fixes the money side.

 WHY IT IS SUPERIOR:
   • Trades the CORRECTED SentinelTrend line (true ATR + CCI hysteresis) instead of the whipsawing
     original — fewer, cleaner flips (see SentinelTrend_v1_0_0.cs header).
   • REAL RISK MANAGEMENT: ATR-sized stop + R-multiple target, with an optional trail that rides the
     SentinelTrend line. Every entry is bracketed; you can never sit in an unbounded loss.
   • SENTINEL GATING: every entry asks SentinelCore.CanEnter (kill-switch + feed health + daily
     governor + account-session + rollover + news) and is fail-CLOSED (automated → refuse on block).
   • RISK-BASED SIZING: optionally size each entry to a fixed $ risk over the ATR stop
     (SentinelCore.SizeForRisk), else profile-scaled base qty (SentinelCore.SizedQuantity).
   • OPTIONAL ADX ALIGNMENT: require ADXPro to confirm trend ON + bias agrees before entering.
   • Stop-and-reverse on the opposite flip, but ALWAYS flattens on a flip even if the reverse entry
     is gated off — so a blocked reverse never strands you in a stale position.

 Managed order framework (SetStopLoss / SetProfitTarget). Registers its traded instrument with
 SentinelRisk's watch registry so a stalled feed is caught even while flat.

 CHANGELOG
   v1.0.0 — initial: SentinelTrend flip entries, ATR stop + R target + line trail, CanEnter gate,
            risk sizing, ADX-align filter. Supersedes the TMEntry/TMEntry50/TMX/TMSquared/TripleTM set.
```

