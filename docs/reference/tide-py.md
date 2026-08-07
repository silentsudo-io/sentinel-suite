---
layout: sentinel-ref
title: "tide.py"
blurb: "Lab (Python) · unversioned · 430 lines"
---

# tide.py

> `Sentinel/Lab/harness/tide.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 430 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
tide — SentinelTide reimplemented in Python, outside NinjaTrader.

Port of `bin\\Custom\\BarsTypes\\SentinelTide_v1_0_0.cs` (BarsPeriodType 212207). Tide is the
harness's first target deliberately: it is the simplest bar type in the suite -- pure arithmetic on
signed ticks, no ATR, no Heikin-Ashi, nothing adaptive -- so if it cannot be reproduced, nothing
downstream can be either. That is the whole point of doing it first.

THE CLOCK (identical to the C#)
-------------------------------
Session cumulative volume delta runs on a fixed lattice `cvdLine(k) = k * deltaPerBrick`. A bar
closes the moment CVD crosses an ADJACENT line, in a loop -- a burst carrying CVD through three
lines prints three bars, so no bar ever holds more than one quantum of flow. That invariant is
what makes bar HEIGHT comparable across bars (height per unit flow = market impact), so it is
enforced structurally here too, not assumed.

Signing is quote rule where a real bid/ask exists, tick rule otherwise, with single prints
winsorized at 4x their EWMA (SentinelFlux learned that the expensive way -- one block trade spiked
its threshold and left the clock dormant for hours). The `isBar` bar-proxy branch of the C# is
NOT ported: the harness always has true ticks, and the proxy path makes bar height a function of
price by construction, which is the exact circularity Tide refuses.

WHAT IS DELIBERATELY NOT IDENTICAL, AND WHY IT MATTERS FOR THE GATE
-------------------------------------------------------------------
  * SESSION BOUNDARY. The C# asks NT's `SessionIterator`, which reads the instrument's trading
    hours template. Here it is an explicit local wall-clock time (default 17:00 America/Chicago =
    the CME Globex open, which the data confirms: the 16:00-17:00 maintenance break is the one
    hour of the day with zero trades). CVD and the lattice index reset at that boundary, so a
    session-boundary disagreement moves EVERY bar in the session. If the gate fails, check this
    first -- it is the most likely single cause.
  * ROUNDING. The C# rounds bar prices to the tick grid on write. Ticks are already on the grid,
    so this is a no-op; heights and bodies are computed from raw prices in both.
  * LOGGING. The C# throttles its bar log to one line per 10 wall-seconds (~8% of bars, and not a
    random 8%). The harness emits EVERY bar. Any ratio taken from NT's log measures the sampler as
    much as the tape -- so the census here is the trustworthy one.

BACKSTOP BARS ARE NOT DATA
--------------------------
A bar closed by the time or tick backstop carries less than a full flow quantum, so its height is
NOT a valid impact reading. They are marked `reason != "flow"` and MUST be excluded before
grading. The 2026-07-26 bring-up found a default size ~10x too large where EVERY bar closed on the
time backstop and the chart still looked plausible -- the tell was the dates, never the chart.
Run `--census` before judging anything by eye.
```

