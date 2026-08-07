---
layout: sentinel-ref
title: "SentinelWAE_v2_0_0.cs"
blurb: "Indicators · 2.0.0 · 451 lines"
---

# SentinelWAE_v2_0_0.cs

> `bin/Custom/Indicators/SentinelWAE_v2_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 2.0.0 |
| **Size** | 451 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelWAE_v2_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors` |
| **Publishes seams** | `WaeState` |
| **Documented by** | _no doc tracks this artifact_ |
| **Depends on this** | [Council_v1_11_0.cs](council-v1-11-0-cs.md), [SentinelCockpit_v0_1_0.cs](sentinelcockpit-v0-1-0-cs.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 Sentinel WAE — Waddah Attar Explosion (CLEAN-ROOM)                        |   Version v2.0.0
 File: SentinelWAE_v2_0_0.cs   |   namespace …Indicators.Sentinel   |   display Name "Sentinel WAE"

 ⚠ NO ORDERS — read-only advisory indicator. Safe to run anywhere.

 CLEAN-ROOM ORIGINAL. This is written from scratch off the PUBLIC Waddah Attar Explosion method — a
 published, non-copyrightable trading formula — using NinjaTrader's own EMA / StdDev / ATR. It uses NO
 third-party code. It REPLACES SentinelWAE_v1_0_0, which descended from unlicensed NT-community /
 TradingView code (LazyBear → shayankm → donto → karmic913) and is therefore not clearable for
 open-source release. See the provenance audit + NOTICE.

 THE PUBLIC FORMULA (canonical parameters: fast 20 / slow 40 / channel 20 / mult 2.0):
   • momentum  t1        = ( MACD(now) − MACD(prev) ) × Sensitivity,  MACD = EMA(fast) − EMA(slow)
   • explosion (BB width)= BBupper − BBlower = 2 · Mult · StdDev(channel)     [the "explosion" line]
   • dead zone           = ATR(deadZoneLength) × DeadZoneMult                 [Wilder ATR = rma(TR,n)]
   • histogram split      = TrendUp = max(t1,0), TrendDown = max(−t1,0)
 The classic WAE trigger — the colored histogram exceeds BOTH the explosion line AND the dead zone —
 becomes a directional momentum-BREAKOUT signal the Council can vote on.
 (The NT-port's extra fast/slow double-smoothing is DROPPED here; this is the plain public formula.)

 THE SENTINEL PLUMBING (our own code — makes it a suite member):
   • PUBLISHES SentinelCore.WaeState: Bias (histogram side), Power (|histogram|), Explosion (BB width),
     DeadZone (ATR), IsExploding (Power > Explosion > DeadZone), Signal (= IsExploding ? Bias : 0).
   • WIRED INTO THE COUNCIL as the WAE momentum-trigger voter on WaeState.Signal.
   • Hidden ±1 "Signal" PLOT (Values[4], transparent) for the Deck SIGNAL ARM / generic consumers.
   • A SentinelSkin.Painter glass card + sub-panel plot skin + label remover.

 CHANGELOG
   v2.0.0 (2026-07-11) — CLEAN-ROOM rewrite of the WAE math from the public formula (original C# via
            NT EMA/StdDev/ATR; no double-smoothing; canonical 20/40/20 defaults). Sentinel plumbing
            (WaeState publish, Council voter, hidden Signal plot, glass card, plot skin, label remover,
            scope key + heartbeat) carried over from v1.0.0 (our own code). v1.0.0 retired (unlicensed lineage).
```

