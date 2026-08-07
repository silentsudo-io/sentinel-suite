# 📼 The Recorder + Log

**Rung 2 · capture.** The engines that write down what actually happened, over *any* strategy, so a
decision can be graded later instead of remembered.

## What's here
- **`SentinelExcursionRecorder_v2_0_0.cs`** — the corpus writer. One row per signal fire with MAE/MFE,
  milestones and the full vote vector. Schema 1.5.
- **`SentinelCandidateRecorder_v1_0_0.cs`** — records candidate setups that were *not* taken.
- **`SentinelTapeRecorder_v1_0_0.cs`** — raw tape capture.
- **`SentinelLogEngine` / `SentinelLogService`** — structured, tiered logging any tool can call.

## The invariant that matters
**One writer, one schema.** Two recorders writing the same corpus produce rows that cannot be compared,
and the damage stays invisible until you try to analyse it. If you add a writer, retire the old one.

⚠ Anything that RECORDS must gate on `State == State.Realtime`. A `…State` seam stamps its timestamp
from the wall clock even while replaying history, so a freshness check alone cannot tell replay from
live — that hole silently poisoned an early corpus with lookahead.

## Install
1. Copy [`../runtime/`](../runtime/) (shared runtime — once) and this folder's `Indicators/` into
   `Documents\NinjaTrader 8\bin\Custom\`.
2. Press **F5** in the NinjaScript Editor.
3. Right-click a chart → **Indicators** → **Sentinel**.

---

Part of the [Sentinel Suite](../../README.md) · MPL-2.0
