---
layout: sentinel-ref
title: "Clock_v1_0_0.cs"
blurb: "Indicators · 1.0.0 · 309 lines"
---

# Clock_v1_0_0.cs

> `bin/Custom/Indicators/Clock_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 309 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `Clock_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel` |
| **Publishes seams** | `ClockState` |
| **Documented by** | _no doc tracks this artifact_ |
| **Depends on this** | [Council_v1_11_0.cs](council-v1-11-0-cs.md), [SentinelCandidateRecorder_v1_0_0.cs](sentinelcandidaterecorder-v1-0-0-cs.md), [SentinelCockpit_v0_1_0.cs](sentinelcockpit-v0-1-0-cs.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 Clock — the Sentinel SESSION-CONTEXT modulator                           |   Version v1.0.0
 File: Clock_v1_0_0.cs   |   namespace …Indicators.Sentinel   |   display Name "Clock"

 ⚠ NO ORDERS — read-only advisory indicator. Safe to run anywhere.

 WHAT THIS IS — the FIRST ORTHOGONAL axis feeding the Council (Docs/ROADMAP.md · memory
 signal-axes-plan). Every base rate in this suite is conditional on WHERE in the session we are —
 the open drive, the midday drift, and the close behave nothing alike — yet nothing published that
 context until now. Clock resolves the session window from the chart's TradingHours and publishes the
 per-instrument phase so the Council can MODULATE on it (damp conviction midday / gate the kill window)
 rather than treat every minute the same. It is a MODULATOR, not a directional voter — it never says
 long or short, it says WHEN.

 THE STATE (SentinelCore.ClockState, SentinelCore ≥ v1.8.0):
   • Phase          0 Closed/pre-open · 1 Open-drive · 2 Midday · 3 Close
   • MinsSinceOpen  minutes since the session opened (-1 if not in session)
   • MinsToClose    minutes until the session closes (-1 if not in session)
   • DayOfWeek      0=Sun .. 6=Sat
   • InSession      currently inside the trading session
   • InKillWindow   inside the near-close no-new-entries window

 SENTINEL WIRING (Docs/SENTINEL_DESIGN_SYSTEM.md §6c/§6d):
   • PUBLISH: SetClockState(...) each update (default ON — Clock exists to publish).
   • A SentinelSkin.Painter glass card + Sentinel palette + label remover. No plots (a modulator is
     consumed via the seam, not as a plottable scalar).
   • Session window comes from a SessionIterator over the chart's TradingHours; phase boundaries are
     configurable minutes. NOTE: "now" is the current bar time (bar-resolution), which is plenty for a
     modulator; a wall-clock refinement for tighter kill-window edges is a future tweak.

 CHANGELOG
   v1.0.0 (2026-07-07) — initial: session phase (open-drive / midday / close) + mins-since-open /
            mins-to-close / day-of-week / in-session / kill-window, published as SentinelCore.ClockState;
            Sentinel card/palette/label-remover. First orthogonal Council axis.
```

