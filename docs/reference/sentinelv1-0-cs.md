---
layout: sentinel-ref
title: "SentinelV1_0.cs"
blurb: "Indicators · unversioned · 3575 lines"
---

# SentinelV1_0.cs

> `bin/Custom/Indicators/SentinelV1_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | — |
| **Size** | 3575 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelV1_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
one at https://mozilla.org/MPL/2.0/.

Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
─────────────────────────────────────────────────────────────────────────────
============================================================================
SentinelV1_0
============================================================================
Sentinel is the first product in the Sentinel Suite — a family of
NinjaTrader 8 trading tools built around intelligent signal arming,
trade management, risk control, and trade logging.

Sentinel V1.0 is a floating trade panel that detaches from the ChartTrader
sidebar and lives as a free-standing ToolWindow. It retains the full feature
set of TrendArchitectMQPanel V1.6.1 and adds:
  - Floating window architecture (WindowStyle.ToolWindow, owned by chart)
  - SentinelSignalSource enum — configurable signal detection:
      TrendArchitectMQ  : TrendArchitect MQB/MQS draw objects (default)
      DrawObjectTag     : any indicator's Draw.X with a configurable tag prefix
      IndicatorSeries   : any indicator's output series by name + index
      ManualOnly        : no auto-arm — buttons only
  - Window position persisted via WindowLeft / WindowTop properties
  - Window width configurable via PanelWidth property
  - No ChartTrader grid injection — panel is fully independent

Sentinel Suite Product Family
-----------------------------
  Sentinel      : Floating trade panel (this file)
  Sentinel Log  : Trade journal — JSONL logger, MAE/MFE
  Sentinel Risk : Standalone session risk monitor
  Sentinel Lens : Analytics overlay — equity curve, trade stats
  Sentinel Arc  : Strategy automation layer
  Sentinel Eye  : Multi-instrument signal scanner

Credits
-------
  Original panel base      : Alighten (AlightenButtonPanelV0004)
  TrendArchitect indicator : _Jason / B3AR
  UI design system         : Khanh — DailyRangeBot (open source)
  V1.1 hardening           : Spoobie
  V1.4 optimizations       : Spoobie
  V1.5 features            : Spoobie
  Floating window pattern  : TradeWindow5 (open source reference)

Usage
-----
  1. Add this indicator to any chart — a floating Sentinel window appears.
  2. The window is owned by the chart and minimizes/restores with it.
  3. For TrendArchitectMQ signals: also add TrendArchitect to the chart.
  4. For custom signals: set SentinelSignalSource and configure the tag or series.
  5. ARM BULL / ARM BEAR to arm entries on the next qualifying signal.
  6. All trade management, trailing, and session risk features from
     TrendArchitectMQPanel V1.6.1 are fully carried over.
============================================================================

V1.0 — Initial Sentinel Release
--------------------------------
  Forked from TrendArchitectMQPanel V1.6.1. All V1.1–V1.6.1 changes
  from the MQPanel lineage are incorporated as the baseline.

  New in V1.0:
  1. FLOATING WINDOW — panel is a standalone WPF ToolWindow owned by
     the chart. No ChartTrader grid injection. Window persists position
     between indicator reloads via WindowLeft/WindowTop properties.
     Width configurable via PanelWidth property (default 280).

  2. SentinelSignalSource enum — four configurable signal detection modes:
       TrendArchitectMQ  — DrawObject scan for TA_SIG_N_MQB/MQS tags
                           (same as MQPanel — default behavior)
       DrawObjectTag     — watch for Draw.X with user-defined tag prefix
                           on BullTagPrefix / BearTagPrefix properties
       IndicatorSeries   — watch named indicator output series by index;
                           non-zero = signal. Configure SignalIndicatorName,
                           BullSeriesIndex, BearSeriesIndex
       ManualOnly        — ARM buttons only, no automatic signal detection

  3. ARM button labels generalized — "ARM BULL" / "ARM BEAR" instead of
     "ARM MQB" / "ARM MQS" to reflect signal-agnostic operation.

  4. Window lifecycle — CreateSentinelWindow() / DisposeSentinelWindow()
     replace CreateWPFControls() / DisposeWPFControls(). Window is
     created in State.DataLoaded, disposed in State.Terminated.
     closingWindowFromIndicator flag prevents re-entry on user close.

  5. Quantity reading — reads from ChartTrader quantity selector via
     the same 4-tier fallback as MQPanel. The ChartTrader remains open
     and functional alongside the Sentinel window.

  6. Enum self-containment — all enums nested inside the class.
     No cross-version dependencies. Compiles on all NT8 builds.
============================================================================
```

