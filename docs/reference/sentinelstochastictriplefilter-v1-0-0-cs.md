# SentinelStochasticTripleFilter_v1_0_0.cs

> `bin/Custom/Indicators/SentinelStochasticTripleFilter_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 534 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelStochasticTripleFilter_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors` |
| **Publishes seams** | `StfState` |
| **Documented by** | _no doc tracks this artifact_ |
| **Depends on this** | [Council_v1_11_0.cs](council-v1-11-0-cs.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 SentinelStochasticTripleFilter — the Sentinel STOCHASTIC-TRIPLE-FILTER sensor   |   Version v1.0.0 (DEV)
 File: SentinelStochasticTripleFilter_v1_0_0.cs   |   namespace …Indicators.Sentinel.Sensors   |   display "Sentinel Stochastic Triple Filter v1.0.0 (DEV)"
 ⚠ NAME FIDELITY (naming law, 2026-07-10): the Sentinel port keeps the FULL original name
    ("Stochastic Triple Filter") so the derivation from the standard StochasticTripleFilter is never lost.

 ⚠ NO ORDERS — read-only advisory indicator. Safe to run anywhere.

 LICENSE / PROVENANCE (MPL-2.0): ported from "Stochastic Triple Filter [ATP]" © AlgoTrade_Pro (Pine v6, MPL-2.0);
    Gaussian Channel math © DonovanWall; Choppiness Index (Dreiss) + Stochastic (Lane) are public-domain formulas.
    A port is a DERIVATIVE WORK — this file KEEPS MPL-2.0. Attribution recorded in the repo NOTICE.

 📁 TIER-③ SENSOR — lives in Indicators.Sentinel.Sensors (picker "Sentinel ▸ Sensors"). This is the FIRST tool
    into the Sensors subfolder — the folder-split pathfinder (Docs/SENTINEL_BOUNDARY_INVENTORY.md §1a).

 WHAT THIS IS — the Sentinel-plumbed port of "Stochastic Triple Filter [ATP]". The raw indicator fires a
 Stochastic %K/%D crossover only when a DonovanWall multi-pole Gaussian midline agrees on direction AND a
 Choppiness Index says the market is TRENDING. Two of those three are exactly the seams the Council was missing:
   • the GAUSSIAN-CHANNEL SLOPE is an independent TREND voice (a smoothed-price regime, not another CCI/ADX echo)
   • the CHOPPINESS INDEX is a genuine REGIME veto — "don't trade a ranging tape" — which the Council had no
     dedicated sensor for (only the VolEnvelope squeeze damp).

 THE STATE (SentinelCore.StfState, SentinelCore ≥ v1.22.0):
   • Bias      -1/0/+1  the Gaussian-Channel midline slope (the TREND vote: rising=+1 / falling=-1) — RAW, always published
   • Trending  bool     Choppiness Index below threshold (regime OK; false ⇒ choppy = the Council's chop veto) — RAW, always published
   • Chop      double   the Choppiness Index value (0..100)
   • Zone      -1/0/+1  Stochastic zone (oversold=-1 / mid=0 / overbought=+1)
   • Signal    -1/0/+1  the FULLY-FILTERED discrete signal this bar (long=+1 / short=-1) — also a hidden plot
 ⚠ UseGC / UseChop gate only THIS sensor's own Signal + on-chart marks — Bias and Trending are published RAW so the
   Council's own STF voter / VetoOnChop stay in charge of how the regime is used (turning a filter off here never
   silently disarms the Council).

 SENTINEL WIRING (Docs/SENTINEL_DESIGN_SYSTEM.md §6/§9):
   • PUBLISH: SetStfState(...) each update (default ON). Wired into the Council as a TREND VOTER ("STF",
     enters at weight 0 — the exploration primitive; promote via F6 "Weight — STF" or Roster.conf) plus a
     CHOP VETO (VetoOnChop, default ON — active the moment this sensor is loaded).
   • A hidden ±1 "Signal" plot (transparent) so Deck SIGNAL ARM / generic consumers read it (CompressionBase pattern).
   • A SentinelSkin.Painter glass card + Sentinel palette + label remover. cyan = live; green/red = direction.

 Faithfulness: the Pine true-range band path (trdata/filttr/mult) is DEAD CODE there (computed, never used)
 and is omitted with zero behavioral change. Gaussian pole coefficients use C(i,j) (identical values; also
 fixes the harmless _f7 guard typo in the original f_pole).

 CHANGELOG
   v1.0.0 (2026-07-12) — Sentinel port: Stochastic + DonovanWall Gaussian slope + Choppiness Index, published as
            SentinelCore.StfState (Core v1.22.0) and wired into the Council (trend voter "STF" at w=0 exploration
            + chop veto). Glass card, hidden Signal plot, label remover, versioned Name, scope-keyed publish + heartbeat.
            Carries UseGC/UseChop for full parity with the source (and logic parity with the plain StochasticTripleFilter
            baseline). Landed in Indicators.Sentinel.Sensors — the Tier-③ folder pathfinder. Supersedes an earlier
            broken cut (dropped the toggles, sat in Indicators.Sentinel) → archived. DEV until live-validated.
```

