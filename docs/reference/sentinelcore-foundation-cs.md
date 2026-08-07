# SentinelCore.Foundation.cs

> `bin/Custom/AddOns/SentinelCore.Foundation.cs`

| | |
|---|---|
| **Family** | AddOns / runtime |
| **Version** | — |
| **Size** | 364 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Namespace** | `NinjaTrader.NinjaScript.AddOns.Sentinel` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 ASSEMBLY-GENERATION BEACON  (v1.40.0, 2026-07-24)

 WHY THIS EXISTS — the single most expensive bug of the corpus era.
 An F5 reloads the NinjaScript assembly and recreates every INDICATOR, but a chart's
 BARS TYPE instance is NOT recreated — it keeps executing the PREVIOUS assembly. Every
 seam store here is a `static Dictionary` on SentinelCore, and statics are per-assembly,
 so the surviving bars type publishes BrickState/FluxState/ConvictionState into the OLD
 assembly's dictionary while the rebuilt Council reads the NEW one. The write SUCCEEDS
 into a store nobody reads: guards report healthy, scope resolves, nothing throws, and
 BRK/FLUX/CVB simply never appear in the vote vector. It cost the 2026-07-23 audition
 bake (1,866 rows, zero bar-type voters) and days of misdiagnosis before the fingerprint
 was spotted: bars-type call counters never reset while Council instance ids did.
 ⇒ ONLY AN NT RESTART FIXES IT. A chart reload is NOT sufficient (measured 2026-07-24).

 WHAT THIS DOES — it cannot repair the split (that needs the restart), but it makes the
 split LOUD instead of silent, at chart load rather than 10 minutes into a bake.
 The beacon lives in the APPDOMAIN, which outlives assembly reloads, and carries ONLY
 strings — no custom type crosses the boundary, so there is no type-identity problem
 (the same reason a `bars.BarsType as SentinelTBars_v1_0_0` cast fails across
 generations: type identity includes assembly identity).

 A publisher beacons "generation G was alive for scope S at time T"; a consumer that
 finds a seam MISSING asks whether some OTHER generation is beaconing it. If so, the
 sensor is not absent — it is DECOUPLED, and the operator needs a restart, not a reload.
```

