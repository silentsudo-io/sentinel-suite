# SentinelCVD_v1_0_0.cs

> `bin/Custom/Indicators/SentinelCVD_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 411 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelCVD_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel` |
| **Publishes seams** | `CvdState` |
| **Documented by** | [SENTINEL_FLOWBARS_SPEC](../../SENTINEL_FLOWBARS_SPEC.md) |
| **Depends on this** | [Council_v1_11_0.cs](council-v1-11-0-cs.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 SentinelCVD — session cumulative volume delta, and the part nobody plots    |   Version v1.0.0
 File: SentinelCVD_v1_0_0.cs  |  namespace …Indicators.Sentinel  |  display Name "Sentinel CVD"

 ⚠ NO ORDERS — read-only advisory indicator. Safe to run anywhere.

 WHY THIS EXISTS
 ---------------
 The suite already CLOCKS on cumulative delta: SentinelFlux's θ is exactly that, and it closes a bar
 when |θ| reaches its expectation (López de Prado imbalance bars). But θ RESETS EVERY BAR — it is the
 clock, not a state — and `FluxState.Cvd`, the session-running figure, was computed, published, and
 read by nobody. It is also only available where Flux is the chart's bar type.

 SentinelCVD makes the session-scale read a first-class citizen on ANY bar type, and publishes the
 three things that actually carry information:

   1. SLOPE (+ z-score) — direction. The CVD LEVEL is close to meaningless: the session anchor is
      arbitrary, so only the change matters. Anyone reading the level is reading an offset.
   2. DIVERGENCE — price up while flow is down (or vice versa). The classic absorption tell.
   3. EFFICIENCY — ⭐ the one nobody plots. Ticks of price bought per 1,000 contracts of NET
      aggression. This is market IMPACT — Kyle's lambda in retail clothing. Rising CVD with LOW
      efficiency means heavy buying that is going nowhere, i.e. someone is quietly filling into it.
      High efficiency means a thin book where a little flow travels a long way. A CVD line alone
      cannot show you either, and it is orthogonal to every price-derived voter in the Council —
      which is the documented core problem ("conviction = agreement, not confirmation").

 HONEST LIMITS — read before trusting a number
 ---------------------------------------------
   • CVD measures WHO CROSSED THE SPREAD, not net positioning. Every contract has a buyer and a
     seller; "delta" is aggressor side only. It is a flow-pressure proxy, never a position.
   • Signing is inferred. Quote rule where a real bid/ask is present, tick rule as fallback. Both are
     estimators, and both are wrong on some prints.
   • The level is anchor-dependent and accumulates signing error all session. Use slope/divergence.
   • Block prints distort everything downstream (SentinelFlux had to winsorize E[|θ|] at 4× for
     exactly this). Per-print volume is winsorized here for the same reason.
   • WITHOUT TICK DATA the signing degrades to a bar-level proxy (close vs open) — far weaker.
     `TickBacked=false` says so on the seam and the card shows a "bar-proxy" warning rather than
     pretending. A degraded read that announces itself is worth having; one that does not, is not.

 SENTINEL WIRING (Docs/SENTINEL_DESIGN_SYSTEM.md §6c/§6d)
   • PUBLISH: SetCvdState(...) per update (default ON — needs SentinelCore ≥ v1.43.0).
   • Council: the CVD voter (STATE) reads this seam. Wired in Council v1.9.x.
   • Hidden ±1 `Signal` plot (transparent, IsAutoScale off) so the Deck SIGNAL ARM can read it
     generically, per the suite convention for signal-emitting tools.
   • Visible CVD line on its own panel + a SentinelSkin.Painter glass card + label remover.

 CHANGELOG
   v1.0.0 (2026-07-25) — initial. Session CVD from quote-rule signed tape (tick-rule fallback,
            winsorized prints), slope EMA + z-score, flow-vs-price divergence, and the EFFICIENCY /
            impact read. Publishes SentinelCore.CvdState. Panel plot + glass card + hidden signal plot.
```

