# 🗂️ Chart Templates

**NinjaTrader chart templates** that lay out a working Sentinel chart in one step, instead of adding
fifteen indicators by hand and hoping the roster matches.

## What's here
- **`Sentinel Core Components.xml`** — the standard sensor set on one chart.
- **`SENTINEL CLOCK EDGE BASE.xml`** — the clock-edge research layout.
- **`Sentinel Validation.xml`** — the layout used to validate a build.

## Install
Copy into `Documents\NinjaTrader 8\templates\Chart\`, then right-click a chart →
**Templates** → **Load**.

⭐ **A template is not cosmetic — it defines the roster.** The Council counts *declared* voters in its
conviction denominator, so a chart missing four sensors does not merely lose four opinions: every
verdict on it is computed against a different denominator. If the Council reports `roster 6/22`, the
template is the fix.

---

Part of the [Sentinel Suite](../../README.md) · MPL-2.0
