# SentinelEhlersFilter_v1_0_0.cs

> `bin/Custom/Indicators/SentinelEhlersFilter_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 221 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelEhlersFilter_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel.Smoothers` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 Sentinel EhlersFilter — Ehlers Non-Linear Filter (Sentinel smoother building block)   |   Version v1.0.0
 File: SentinelEhlersFilter_v1_0_0.cs  |  namespace …Indicators.Sentinel.Smoothers  |  display "Sentinel EhlersFilter"

 ⚠ NO ORDERS · NO STATE SEAM — a read-only SMOOTHER building block, not a Council voter. It draws a
 smoothed line + a Sentinel glass card; it publishes nothing (a filter has no verdict). A baseline the
 signal tools can consume, and a Sentinel-branded Ehlers filter in its own right.

 PROVENANCE / LICENSE: CLEAN-ROOM. Reimplemented from John F. Ehlers' public "Non-Linear Filters" method
 (forex-tsd.com/…/ehlers_-_non_linear_filters.pdf) — a mathematical method, not copyrightable. NO third-party
 code, variable names, or structure were copied; the "Au" filter pack was read ONLY to identify the variant.
 Canonical recurrence: coefficientᵢ = (Input[0] − Input[i])² over lag i∈[0,Period); the filter output is the
 coefficient-weighted average  Value = Σ(coefᵢ · Input[i]) / Σ(coefᵢ)  (Σ=0 ⇒ Value = Input[0]).

 ASSUMPTIONS / NOTES:
   • The "Au" source pre-smoothed the input with a short TMA and used a full pairwise-distance coefficient
     (Σ over every lag pair). This clean-room build implements the canonical distance-from-current-bar
     coefficient DIRECTLY on Input (no pre-smoothing) per the specified canonical form — output differs
     slightly from the Au variant by design.
   • Early bars use a shrinking window n = min(CurrentBar+1, Period); the i=0 term contributes coef 0.

 CHANGELOG
   v1.0.0 (2026-07-12) — clean-room Ehlers Non-Linear Filter + Sentinel plumbing (naming law, glass card, label remover).
```

