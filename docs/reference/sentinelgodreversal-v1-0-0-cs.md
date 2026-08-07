# SentinelGodReversal_v1_0_0.cs

> `bin/Custom/Indicators/SentinelGodReversal_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 642 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelGodReversal_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors` |
| **Publishes seams** | `GodReversalState` |
| **Consumes seams** | `LevelState` |
| **Documented by** | _no doc tracks this artifact_ |
| **Depends on this** | [Council_v1_11_0.cs](council-v1-11-0-cs.md), [SentinelBridge_v0_2_0.cs](sentinelbridge-v0-2-0-cs.md), [SentinelCockpit_v0_1_0.cs](sentinelcockpit-v0-1-0-cs.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 Sentinel God Reversal — the candle-grammar REVERSAL recognizer   [displayed as "Sentinel › God Reversal"]
 File: SentinelGodReversal_v1_0_0.cs  |  Version: v1.0.0  |  class SentinelGodReversal_v1_0_0  |  ns …Indicators.Sentinel
─────────────────────────────────────────────────────────────────────────────
 WHAT IT IS  (see Docs/SENTINEL_GOD_REVERSAL_DOCTRINE.md)
   Encodes the reversal grammar from the "god trade masterclass" (Trading for Rent Money) that GodTrades21
   does NOT: shaved close/open · engulfing-at-level · equal high/low · doji-cluster exhaustion · VI-fill ·
   attack-angle — gated on a "predictable place" (a Bollinger-band edge, optionally boosted by a Location
   structural level). Fires on the CLOSE of the reversal candle (non-repaint; entry = next bar).
   NO ORDERS — a read-only sensor. It:
     • MARKS each trigger on the chart (triangle + score/setup label + single-candle STOP line + optional VI box)
     • exposes a hidden ±1 "Signal" plot (Deck SIGNAL ARM / generic consumers)
     • publishes SentinelCore.GodReversalState (pulse + HELD dir + quality + setup) → the Council's GREV voter
     • draws a Sentinel glass card (Painter) with the live location read

 DEPS: SentinelCore ≥ v1.14.0 (GodReversalState seam) + SentinelSkin (card). Location consult is a SOFT dep.
 NO CUSTOM ENUM PARAMS (dodges the bare-enum codegen saga) — every [NinjaScriptProperty] is bool/int/double.

 CHANGELOG
   v1.0.0 (2026-07-08) — first build. Grammar: shaved/engulf/equal/doji-exhaustion/VI/attack + BB-edge gate +
            no-trade guards (endless-doji chop, sideways grind). Publishes GodReversalState; Council GREV voter.
            HONEST CAVEAT: thresholds are first-guess defaults — tune vs the video's examples + let Lens grade.
```

