---
layout: sentinel-ref
title: "SentinelParabolicSAR_v1_0_0.cs"
blurb: "Indicators · 1.0.0 · 415 lines"
---

# SentinelParabolicSAR_v1_0_0.cs

> `bin/Custom/Indicators/SentinelParabolicSAR_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 415 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelParabolicSAR_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors` |
| **Publishes seams** | `SarState` |
| **Documented by** | _no doc tracks this artifact_ |
| **Depends on this** | [Council_v1_11_0.cs](council-v1-11-0-cs.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 Sentinel Parabolic SAR — the STOP-AND-REVERSE trend axis (CLEAN-ROOM)     |   Version v1.0.0
 File: SentinelParabolicSAR_v1_0_0.cs   |   namespace …Indicators.Sentinel.Sensors (Tier-③ SENSOR)   |   Name "Sentinel Parabolic SAR"

 ⚠ NO ORDERS — read-only advisory indicator. Safe to run anywhere.

 CLEAN-ROOM ORIGINAL. Written from scratch off J. Welles Wilder's PUBLIC Parabolic SAR formula — a
 classic, non-copyrightable trend-following technique first published in "New Concepts in Technical
 Trading Systems" (1978) and reproduced in every reference text since. It uses NO third-party code.
 The installed amaParabolicSAR.cs (LizardIndicators, GPL) was NOT copied — it was surveyed only as a
 design reference; every line here is written fresh from Wilder's published recurrence. See the
 provenance audit + NOTICE.

 WHY IT MATTERS — the Council leans on momentum/volatility/structure voters. Parabolic SAR is the
 purest STOP-AND-REVERSE trend read: a single trailing dot that sits below price in an uptrend and
 above it in a downtrend, accelerating toward price as the move extends. Its FLIP is a clean,
 unambiguous regime-change pulse the Council can react to; its side is an always-on trend lean.

 THE PUBLIC FORMULA (Wilder):
   • State per bar: trend (long/short), SAR value, EP (extreme point of the current run), AF (accel).
   • Init on the first usable bar: trend from Close vs Open; SAR = the prior bar's extreme;
     AF = AccelStart; EP = current High (if long) / Low (if short).
   • Each bar:  SAR = SAR[1] + AF · (EP − SAR[1]).
        – uptrend  : clamp SAR ≤ min(Low[1], Low[2]).   If Low crosses SAR → FLIP short.
        – downtrend: clamp SAR ≥ max(High[1], High[2]). If High crosses SAR → FLIP long.
        – on FLIP  : SAR = EP, AF = AccelStart, EP = the new extreme.
        – else on a NEW extreme: update EP and AF += AccelStep, capped at AccelMax.
   • Bias / Signal = +1 uptrend (price above SAR) · −1 downtrend (price below SAR)  [a STATE voter, always ±1].
   • Flip          = ±1 PULSE on the reversal bar, else 0.
   • Sar           = the trailing SAR value.

 THE SENTINEL PLUMBING (our own code — makes it a suite member):
   • PUBLISHES SentinelCore.SarState (Bias / Sar / Flip / Signal).
   • WIRED INTO THE COUNCIL as a STATE trend voter on SarState.Signal.
   • Hidden ±1 "Signal" PLOT (Values[1], transparent) for the Deck SIGNAL ARM / generic consumers.
   • Trend-colored SAR Dot plot (Values[0]) + a SentinelSkin.Painter glass card + label remover +
     scope key + roster heartbeat.

 CHANGELOG
   v1.0.0 (2026-07-12) — NEW. Clean-room Parabolic SAR from Wilder's public recurrence (own C#; no
            third-party code). SarState publish, Council STATE trend voter, hidden Signal plot,
            trend-colored SAR dots, glass card, scope key + heartbeat.
```

