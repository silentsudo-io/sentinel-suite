# VolEnvelope_v0_2_0.cs

> `bin/Custom/Indicators/VolEnvelope_v0_2_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 0.2.0 |
| **Size** | 726 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `VolEnvelope_v0_2_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors` |
| **Publishes seams** | `EnvelopeState` |
| **Documented by** | _no doc tracks this artifact_ |
| **Depends on this** | [Council_v1_11_0.cs](council-v1-11-0-cs.md), [SentinelCockpit_v0_1_0.cs](sentinelcockpit-v0-1-0-cs.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 VolEnvelope — honest volatility envelope (a ground-up Bollinger rewrite)   [Edge lane, no orders]
 File: VolEnvelope_v0_2_0.cs   |   Version: v0.2.0   |   namespace …Indicators.Sentinel
─────────────────────────────────────────────────────────────────────────────
 WHY: Bollinger draws SMA ± 2σ — Gaussian, symmetric, close-only, regime-blind, and drawn as if
 the vol estimate were exact. This closes all of that (see Docs/SENTINEL_VOLENVELOPE_SPEC.md):
   • CENTER  = EWMA of typical price (no SMA "drop-off" jerk).
   • VOL     = range-based Yang-Zhang (Rogers-Satchell fallback for gapless tick/Renko bars) —
               uses the whole bar, reacts to expansion sooner than close-only stdev.
   • WIDTH   = empirically calibrated PER SIDE — the multiplier is the real quantile of this
               instrument's standardized returns, computed separately up/down → asymmetric + fat-tail-honest.
   • REGIME  = native Squeeze / Range / TrendUp / TrendDown / Expansion.
   • %b      = TREND-AWARE — a band breach in RANGE reads EXTREME (fade); the same breach in a
               TREND reads RIDING (follow). The one thing classic BB structurally cannot do.
   • ERROR   = faint band-of-band from SE(σ) ≈ σ/√(2P), widened right after a regime flip. The fuzz is the honesty.
   • CONE    = √t-growing forward projection right of the last bar.
 Advisory-only (Edge lane, submits nothing). Consults the Eye verdict for trend context, and — when
 Publish regime is on — PUBLISHES its regime/stretch via SentinelCore.SetEnvelopeState so the Copier /
 Arc / strategies can gate on it (e.g. "don't ADD in a squeeze"). Consume via GetEnvelopeState(instr, age).
 Sentinel-homed: Indicators.Sentinel namespace (→ "Sentinel" picker folder), glass card via SentinelSkin.Painter,
 CardLayout anti-overlap docking, label-remover (clean chart by default).

 CHANGELOG
   v0.2.0a (2026-07-07) — PublishRegime now DEFAULTS ON so VolEnvelope feeds the Council's EnvelopeState
            voter out of the box. In-place patch (no rename); existing placements keep their serialized value.
   v0.2.0 — PUBLISH SEAM wired: Publish regime (opt-in) → SentinelCore.SetEnvelopeState(instr, regime,
            stretch, bwPctile, multUp, multDown, source); new SentinelCore.EnvelopeState +
            Get/AllEnvelopeStates consult API (regime as int; publish/consult mirrors EyeVerdict).
            v0_1_0 ARCHIVED out of the tree (…\_archive\Indicators) — new type identity, re-add on charts.
   v0.1.0 — [frozen, archived as VolEnvelope_v0_1_0] Initial. EWMA center + YZ/RS vol + asymmetric empirical
            bands (per-side quantile) + regime + trend-aware %b + error band + forward cone + glass card.
            Live-GC fixes: cone/card in SEPARATE try/catch (first-fail → sentinel.log); cone uses
            Bars.GetTime ABSOLUTE indexing (barsAgo Time[1] throws in render); Mid plot → dashed.
```

