---
layout: sentinel-ref
title: "SentinelFlow_v1_0_0.cs"
blurb: "Indicators · 1.0.0 · 414 lines"
---

# SentinelFlow_v1_0_0.cs

> `bin/Custom/Indicators/SentinelFlow_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 414 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelFlow_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel` |
| **Publishes seams** | `FlowState` |
| **Documented by** | _no doc tracks this artifact_ |
| **Depends on this** | [Council_v1_11_0.cs](council-v1-11-0-cs.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 Sentinel Flow — the ORDER-FLOW axis (CLEAN-ROOM)                          |   Version v1.0.0
 File: SentinelFlow_v1_0_0.cs   |   namespace …Indicators.Sentinel (Context AXIS)   |   Name "Sentinel Flow"

 ⚠ NO ORDERS — read-only advisory indicator. Safe to run anywhere.

 CLEAN-ROOM ORIGINAL. Written from scratch off PUBLIC, non-copyrightable methods — the tick (uptick/downtick)
 trade-classification rule, cumulative volume delta, and ordinary-least-squares linear regression. It uses NO
 third-party code. It is the suite's answer to the acknowledged FLOW gap; the installed Alighten/vga CVD family
 (all descended from "Volume Delta by Gill", 2019, unlicensed) and RedTail profile tools were surveyed as design
 references only — none of their code was copied. See the provenance audit + NOTICE.

 WHY IT MATTERS — every other Council voter is price-derived (ADX/CCI/Trend/Envelope/Brick echo the same OHLC).
 CUMULATIVE VOLUME DELTA is the one axis built from the transaction tape, so it can CONFIRM or DIVERGE from price.

 THE PUBLIC METHOD:
   • tick rule       — a trade printing above the prior trade is buyer-initiated (+vol); below is seller (−vol);
                       an unchanged print carries the prior sign. Session CVD = running sum of signed volume.
   • flow regime     — OLS regression of the last N session-CVD samples vs bar index → Slope + R² (fit quality).
   • strength (0..1) = R² × min(1, |slope| / mean|ΔCVD|) — how convincingly, and how cleanly, flow leans.
   • divergence      — price change vs CVD change over the window disagree (price up while CVD falls = bearish).
   • Signal          = Bias (= sign of Slope) once R² ≥ gate AND strength ≥ gate, else 0 — the CONFIRMED flow.

 THE SENTINEL PLUMBING (our own code — makes it a suite member):
   • PUBLISHES SentinelCore.FlowState (Bias / Cvd / Slope / RSquared / Strength / Divergence / Signal).
   • WIRED INTO THE COUNCIL as the FLOW voter (a STATE voter on FlowState.Signal).
   • Hidden ±1 "Signal" PLOT (Values[1], transparent) for the Deck SIGNAL ARM / generic consumers.
   • A SentinelSkin.Painter glass card + label remover + roster heartbeat.

 CHANGELOG
   v1.0.0 (2026-07-12) — NEW. Clean-room CVD/order-flow axis (tick-rule CVD + OLS regime + divergence).
            FlowState publish, Council FLOW voter, hidden Signal plot, glass card, scope key + heartbeat.
```

