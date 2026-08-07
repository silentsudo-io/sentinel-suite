---
layout: sentinel-ref
title: "SentinelStructure_v1_0_0.cs"
blurb: "Indicators · 1.0.0 · 411 lines"
---

# SentinelStructure_v1_0_0.cs

> `bin/Custom/Indicators/SentinelStructure_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 411 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelStructure_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors` |
| **Publishes seams** | `StructureState` |
| **Documented by** | _no doc tracks this artifact_ |
| **Depends on this** | [Council_v1_11_0.cs](council-v1-11-0-cs.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 Sentinel Structure — the MARKET-STRUCTURE axis (CLEAN-ROOM)               |   Version v1.0.0
 File: SentinelStructure_v1_0_0.cs   |   namespace …Indicators.Sentinel.Sensors (Tier-③ SENSOR)   |   Name "Sentinel Structure"

 ⚠ NO ORDERS — read-only advisory indicator. Safe to run anywhere.

 CLEAN-ROOM ORIGINAL. Written from scratch off the PUBLIC market-structure method — a published,
 non-copyrightable price-action technique: swing-pivot detection → HH/HL/LH/LL classification →
 break-of-structure. It uses NO third-party code and does NOT call NinjaTrader's own Swing indicator;
 the pivot detector is self-contained. The installed PriceActionSwingPro.cs (CC BY-NC-SA) was NOT
 copied — only its CONCEPT (swing pivots + structure labels) was surveyed as a design reference.
 See the provenance audit + NOTICE.

 WHY IT MATTERS — most Council voters read momentum/volatility. MARKET STRUCTURE reads the SKELETON of
 price: the sequence of confirmed swing highs and lows. Higher-high + higher-low = an up-structure the
 Council can lean with; a break of the last confirmed swing = a regime-change PULSE it can react to.

 THE PUBLIC METHOD:
   • swing high (fractal) confirms at the bar `Strength` back when its High is strictly the maximum of
     the High over the symmetric window [ −Strength … +Strength ]; a swing low is the mirror on Low.
   • classify vs the PRIOR confirmed swing of the same kind:
        HH = swingHigh > prevSwingHigh   ·   LH = swingHigh < prevSwingHigh
        HL = swingLow  > prevSwingLow    ·   LL = swingLow  < prevSwingLow
   • Bias      = +1 up-structure (HH && HL) · −1 down-structure (LH && LL) · 0 mixed/unknown.
   • SwingType = the LAST swing classification: +2 HH · +1 HL · −1 LH · −2 LL · 0.
   • Bos (break-of-structure PULSE) = +1 when Close closes ABOVE the last confirmed swing-high price,
     −1 when it closes BELOW the last confirmed swing-low price; one-shot per break (latches until the
     OPPOSING level is taken), else 0.
   • Signal    = Bias (the confirmed structure direction).

 THE SENTINEL PLUMBING (our own code — makes it a suite member):
   • PUBLISHES SentinelCore.StructureState (Bias / SwingType / Bos / Signal).
   • WIRED INTO THE COUNCIL as the STRUCTURE voter (a STATE voter on StructureState.Signal).
   • Hidden ±1 "Signal" PLOT (Values[2], transparent) for the Deck SIGNAL ARM / generic consumers.
   • Muted confirmed-swing level lines (Values[0]/[1]) + a SentinelSkin.Painter glass card +
     label remover + roster heartbeat.

 CHANGELOG
   v1.0.0 (2026-07-12) — NEW. Clean-room market-structure axis (self-contained fractal pivots +
            HH/HL/LH/LL classification + break-of-structure). StructureState publish, Council STRUCTURE
            voter, hidden Signal plot, swing-level lines, glass card, scope key + heartbeat.
```

