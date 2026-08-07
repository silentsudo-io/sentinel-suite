# SentinelProfile_v1_0_0.cs

> `bin/Custom/Indicators/SentinelProfile_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 441 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelProfile_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel` |
| **Publishes seams** | `ProfileState` |
| **Documented by** | _no doc tracks this artifact_ |
| **Depends on this** | [Council_v1_11_0.cs](council-v1-11-0-cs.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 Sentinel Profile — the VOLUME-PROFILE (LOCATION) context axis (CLEAN-ROOM)  |   Version v1.0.0
 File: SentinelProfile_v1_0_0.cs   |   namespace …Indicators.Sentinel (Context AXIS)   |   Name "Sentinel Profile"

 ⚠ NO ORDERS — read-only advisory indicator. Safe to run anywhere.

 CLEAN-ROOM ORIGINAL. Written from scratch off the PUBLIC volume-profile method — a published,
 non-copyrightable market-structure technique — using only NinjaTrader's own bar OHLCV. It uses NO
 third-party code. The installed RedTail / Alighten profile tools were surveyed as design references
 only; none of their code was copied. See the provenance audit + NOTICE.

 WHY IT MATTERS — most Council voters are momentum/trend derived. VOLUME PROFILE answers a different
 question: WHERE is price relative to where volume has actually traded? Point of Control (fairest price),
 the 70% Value Area, and high/low-volume nodes give the Council a LOCATION axis for acceptance vs. rejection.

 THE PUBLIC METHOD (developing SESSION profile):
   • bins            — a Dictionary keyed by (long)round(price / TickSize). Each bar distributes Volume[0]
                       evenly across every tick level from Low[0]..High[0]. Reset on the first bar of session.
   • POC             — the price bin holding the maximum volume (the "fairest" / most-traded price).
   • Value Area (70%)— start at the POC, repeatedly annex whichever adjacent bin (above VAH or below VAL)
                       holds more volume, until cumulative ≥ ValueAreaPct of total → VAH (top) / VAL (bottom).
   • HVN / LVN       — a bin is a High-Volume Node if its volume is a LOCAL MAX above the mean bin volume;
                       a Low-Volume Node if a LOCAL MIN below the mean. Near = Close within NodeProximityTicks.
   • Location        = Close>VAH ? +1 (above value) : Close<VAL ? −1 (below value) : 0 (inside value).
   • Signal          = POC-reversion / mean-reversion lean: Close>VAH ? −1 (fade the push up) :
                       Close<VAL ? +1 (fade the push down) : 0.
   • DistPocTicks    = (Close − POC) / TickSize — signed distance to fair value.

 THE SENTINEL PLUMBING (our own code — makes it a suite member):
   • PUBLISHES SentinelCore.ProfileState (Poc / Vah / Val / Location / Signal / DistPocTicks / NearHVN / NearLVN).
   • Overlay plots — Poc (cyan), Vah / Val (muted) drawn on the price panel; hidden ±1 "Signal" plot.
   • A SentinelSkin.Painter glass card + label remover + roster heartbeat.

 CHANGELOG
   v1.0.0 (2026-07-12) — NEW. Clean-room developing-session volume profile (POC / 70% value area / HVN-LVN).
            ProfileState publish, overlay POC/VAH/VAL lines, hidden Signal plot, glass card, scope key + heartbeat.
```

