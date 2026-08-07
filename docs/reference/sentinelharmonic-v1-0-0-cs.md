---
layout: sentinel-ref
title: "SentinelHarmonic_v1_0_0.cs"
blurb: "Indicators · 1.0.0 · 477 lines"
---

# SentinelHarmonic_v1_0_0.cs

> `bin/Custom/Indicators/SentinelHarmonic_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 477 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelHarmonic_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors` |
| **Publishes seams** | `HarmonicState` |
| **Documented by** | _no doc tracks this artifact_ |
| **Depends on this** | [Council_v1_11_0.cs](council-v1-11-0-cs.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 Sentinel Harmonic — XABCD harmonic-pattern reversal detector (CLEAN-ROOM)   |   Version v1.0.0
 File: SentinelHarmonic_v1_0_0.cs   |   namespace …Indicators.Sentinel.Sensors   |   display Name "Sentinel Harmonic"

 ⚠ NO ORDERS — read-only advisory indicator. Safe to run anywhere.

 CLEAN-ROOM ORIGINAL. Written from scratch off the PUBLIC harmonic-pattern definitions
 (Gartley / Bat / Butterfly / Crab) — the standard, non-copyrightable Fibonacci XABCD ratio tables that
 are published in every technical-analysis reference. It uses NO third-party code. The installed
 VdubusPatternGenV2_NT8.cs (an unlicensed Pine port) in the tree was surveyed as a design reference only —
 none of its code was copied; the ratio windows below are typed fresh from the public tables. See the
 provenance audit + NOTICE.

 WHY IT MATTERS — this is a GEOMETRIC MEAN-REVERSION / turning-point voter, orthogonal to the suite's
 trend/momentum sensors: it flags when five alternating swing pivots trace a completed harmonic figure whose
 D point is a high-probability reversal zone. It CONFIRMS or CONTRADICTS the trend axes at exhaustion turns.

 THE PUBLIC METHOD:
   • A self-contained fractal pivot detector confirms a swing HIGH/LOW `Strength` bars back when that bar is
     the extreme of the symmetric ±Strength window. Pivots are kept ALTERNATING (H,L,H,L,…); the most recent
     five become X, A, B, C, D.
   • Leg retracement ratios (absolute price differences): AB/XA, BC/AB, CD/BC, AD/XA — matched against the
     PUBLIC windows (tolerance `Tol`):
       Gartley:   AB/XA≈0.618 · BC/AB∈[0.382,0.886] · CD/BC∈[1.13,1.618]  · AD/XA≈0.786
       Bat:       AB/XA∈[0.382,0.5] · BC/AB∈[0.382,0.886] · CD/BC∈[1.618,2.618] · AD/XA≈0.886
       Butterfly: AB/XA≈0.786 · BC/AB∈[0.382,0.886] · CD/BC∈[1.618,2.24]  · AD/XA∈[1.27,1.618]
       Crab:      AB/XA∈[0.382,0.618] · BC/AB∈[0.382,0.886] · CD/BC∈[2.618,3.618] · AD/XA≈1.618
   • DIRECTION from the D pivot: D is a swing LOW ⇒ BULLISH (Signal +1, expect up); D is a swing HIGH ⇒
     BEARISH (Signal −1). Only the most recent valid match fires — ONE-SHOT per new D pivot.
   • Signal = the pulse on the confirmation bar (+1/−1/0). Dir = last non-zero Signal, HELD HoldBars bars
     then decays to 0.

 THE SENTINEL PLUMBING (our own code — makes it a suite member):
   • PUBLISHES SentinelCore.HarmonicState (Signal / Dir / Pattern).
   • Draws the XABCD skeleton (Draw.Line) + a labelled reversal marker (triangle + pattern text) at D.
   • Hidden ±1 "Signal" PLOT (Values[0], transparent) for the Deck SIGNAL ARM / generic consumers.
   • A SentinelSkin.Painter glass card + label remover + roster heartbeat + scope key.

 CHANGELOG
   v1.0.0 (2026-07-12) — NEW. Clean-room XABCD harmonic detector (fractal pivots + public Gartley/Bat/
            Butterfly/Crab ratio tables; direction from the D pivot). HarmonicState publish, XABCD skeleton
            + labelled reversal marker, hidden Signal plot, glass card, scope key + heartbeat, label remover.
```

