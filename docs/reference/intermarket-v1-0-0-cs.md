# Intermarket_v1_0_0.cs

> `bin/Custom/Indicators/Intermarket_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 351 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `Intermarket_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel` |
| **Publishes seams** | `IntermarketState` |
| **Documented by** | _no doc tracks this artifact_ |
| **Depends on this** | [Council_v1_11_0.cs](council-v1-11-0-cs.md), [SentinelCockpit_v0_1_0.cs](sentinelcockpit-v0-1-0-cs.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 Intermarket — the Sentinel CORRELATED-INSTRUMENT axis                    |   Version v1.0.0
 File: Intermarket_v1_0_0.cs   |   namespace …Indicators.Sentinel   |   display Name "Intermarket"

 ⚠ NO ORDERS — read-only advisory indicator. Safe to run anywhere.

 WHAT THIS IS — the FIFTH orthogonal axis feeding the Council (Docs/ROADMAP.md · memory signal-axes-plan).
 The single-instrument price sensors can't see the MACRO cross-currents that drive a market. Intermarket
 reads a configurable set of CORRELATED instruments and publishes a net directional LEAN for the chart
 instrument — genuinely independent information (e.g. bonds up / real-yields down is gold-supportive).

 INSTRUMENT-AGNOSTIC BY DESIGN — the correlation SIGN differs by market, so it's config, not hardcoded:
   • GOLD (GC/MGC): Ref = ZN (10y note), positive — bonds up (yields down) ⇒ gold up. (ZB works too.)
   • ES/NQ: the bond↔equity sign is regime-dependent — set your own partner + polarity (e.g. the sister
     index for lead/lag), or leave the ref blank to disable the axis on that chart.
 Two reference slots, each with an INVERSE toggle (positive vs negative correlation). Empty slot = off.

 THE STATE (SentinelCore.IntermarketState, SentinelCore ≥ v1.12.0):
   Lean (-1/0/1 for the chart instrument) · Score (-1..1 sign-adjusted) · RefCount · Refs ("ZN:+ ZB:+").

 TREND PER REF — anchored to the canonical trend def: hosts SentinelTrend on each reference series
 (SentinelTrend_v1_0_0(BarsArray[i], …) card/publish/signals OFF), reads its Direction, applies the ref's
 sign, and averages. Reference series are added at RefMinutes (a higher TF so macro noise doesn't whipsaw).

 SENTINEL WIRING (Docs/SENTINEL_DESIGN_SYSTEM.md §6c/§6d + §9 Council protocol):
   • PUBLISH: SetIntermarketState(...) each update (default ON). No plots (consumed via the seam).
   • Council fuses it as a directional VOTER (IMKT). A SentinelSkin.Painter glass card + label remover.

 CHANGELOG
   v1.0.0 (2026-07-07) — initial: configurable correlated-instrument lean (default ZN+ for gold), hosted
            SentinelTrend per ref, published as SentinelCore.IntermarketState; Sentinel card. Fifth Council axis.
```

