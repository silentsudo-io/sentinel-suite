# SentinelVIDYA_v1_0_0.cs

> `bin/Custom/Indicators/SentinelVIDYA_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 368 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelVIDYA_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors` |
| **Publishes seams** | `VidyaState` |
| **Documented by** | _no doc tracks this artifact_ |
| **Depends on this** | [Council_v1_11_0.cs](council-v1-11-0-cs.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 Sentinel VIDYA — the ADAPTIVE-MA TREND axis (CLEAN-ROOM)                  |   Version v1.0.0
 File: SentinelVIDYA_v1_0_0.cs   |   namespace …Indicators.Sentinel.Sensors (Tier-③ SENSOR)   |   Name "Sentinel VIDYA"

 ⚠ NO ORDERS — read-only advisory indicator. Safe to run anywhere.

 CLEAN-ROOM ORIGINAL. Written from scratch off the PUBLIC VIDYA formula (Variable Index Dynamic
 Average, Tushar Chande) — a classic, published, non-copyrightable adaptive moving average: a
 Chande-Momentum-Oscillator-modulated EMA whose smoothing speeds up in a trend and slows in chop.
 It uses NO third-party code. The installed volumaticVIDYA.cs / BigBeluga VolumaticVIDYA
 (CC-BY-NC-SA) were surveyed as DESIGN REFERENCES only — no code was copied and the BigBeluga
 liquidity-zone overlay is NOT reproduced. See the provenance audit + NOTICE.

 THE PUBLIC FORMULA:
   • Chande Momentum (over CmoPeriod):
       up  = Σ max(Close − Close[1], 0)      dn = Σ max(Close[1] − Close, 0)
       cmo = (up + dn) > 0 ? |(up − dn) / (up + dn)| : 0        (0..1)
   • alpha    = 2 / (Length + 1)
   • VIDYA[0] = Close[0]·(alpha·cmo) + VIDYA[1]·(1 − alpha·cmo)   (seed VIDYA = Close on bar 0)
     → the smoothing factor (alpha·cmo) is large when momentum is strong (fast follow) and small
       when momentum is weak (heavy smoothing), so the line hugs trends and floats through noise.
   • Bias/Signal = slope direction with a small hysteresis deadband (TickSize × SlopeTicks):
       +1 when the line rose beyond the band, −1 when it fell beyond it, HOLD the last side inside
       the band. A STATE voter — always carries a ±1 reading.

 THE SENTINEL PLUMBING (our own code — makes it a suite member):
   • PUBLISHES SentinelCore.VidyaState (Bias / VIDYA value / Signal).
   • Intended to be WIRED INTO THE COUNCIL as an adaptive-MA STATE voter on VidyaState.Signal.
   • Hidden ±1 "Signal" PLOT (Values[1], transparent) for the Deck SIGNAL ARM / generic consumers.
   • Green/red VIDYA line (Values[0], colored by slope) + a SentinelSkin.Painter glass card +
     label remover + roster heartbeat.

 CHANGELOG
   v1.0.0 (2026-07-12) — NEW. Clean-room CMO-modulated EMA (public VIDYA) adaptive-MA trend axis.
            VidyaState publish, hidden Signal plot, slope-hysteresis bias, green/red overlay line,
            glass card, scope key + heartbeat.
```

