# 🏛️ The Council

**Rung 4 · one honest verdict.** The confluence arbiter. It reads the `…State` seams the sensors
publish, weights them, and emits a single directional verdict with a conviction score — instead of
a screen of indicators disagreeing in isolation.

## What's here
- **`Council_v1_11_0.cs`** — the arbiter. A kind-aware conviction denominator (a STATE voter always
  counts; a quiet TRIGGER contributes nothing), per-voter weights from `Roster.conf`, and context
  modulators that damp or veto.
- **`CouncilFusion.cs`** — the fuse block as a reusable AddOn, mirroring the indicator's maths so a
  consumer can fuse without hosting the indicator.

## The one thing to read first
**A voter at `w=0` still votes and is still recorded — it just cannot move the verdict.** That is the
audition primitive: add a sensor, grade it against your own outcomes, and only then give it weight.
Shipping a fitted `Roster.conf` would be shipping a *result*; this ships the mechanism.

⚠ **Measured, and stated plainly:** over one 44-day corpus, 0 of 19 voters beat 50% on direction and
the fused verdict was a coin flip gross. The Council is a good instrument for *combining* opinions.
It is not, by itself, evidence that the opinions are worth combining. Grade before you trust.

## Install
1. Copy [`../runtime/`](../runtime/) (shared runtime — once) and this folder's `Indicators/` into
   `Documents\NinjaTrader 8\bin\Custom\`.
2. Press **F5** in the NinjaScript Editor.
3. Right-click a chart → **Indicators** → **Sentinel**.

---

Part of the [Sentinel Suite](../../README.md) · MPL-2.0
