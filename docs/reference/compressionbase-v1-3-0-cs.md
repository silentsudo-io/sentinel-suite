---
layout: sentinel-ref
title: "CompressionBase_v1_3_0.cs"
blurb: "Indicators · 1.3.0 · 775 lines"
---

# CompressionBase_v1_3_0.cs

> `bin/Custom/Indicators/CompressionBase_v1_3_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.3.0 |
| **Size** | 775 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `CompressionBase_v1_3_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors` |
| **Publishes seams** | `CompressionState` |
| **Documented by** | _no doc tracks this artifact_ |
| **Depends on this** | [Council_v1_11_0.cs](council-v1-11-0-cs.md), [SentinelCockpit_v0_1_0.cs](sentinelcockpit-v0-1-0-cs.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 CompressionBase — coil base detector + EXCURSION RECORDER w/ TP/SL first-touch grid  (Sentinel-homed)
 File: CompressionBase_v1_3_0.cs   |   Version: v1.3.3   |   namespace …Indicators.Sentinel
─────────────────────────────────────────────────────────────────────────────
 FIRST TOOL TO ADOPT THE SENTINEL NAMESPACE CONVENTION (design-system §7):
   • REHOMED to namespace  NinjaTrader.NinjaScript.Indicators.Sentinel  → the NT indicator picker groups
     sub-namespaces into expandable FOLDERS, so this appears under a "Sentinel" folder automatically (verified).
   • Class name kept CLEAN (no "Sentinel" prefix) — the folder supplies the context: reads "Sentinel › CompressionBase".
 Behaviour is IDENTICAL to CompressionBase_v1_2_4 — CardLayout-docked glass card + all detector / recording /
 box-breakout logic unchanged. NT's codegen is namespace-aware, so it's still hostable by simple name.
 ⚠ NEW TYPE IDENTITY: different type than CompressionBase_v1_2_4 (namespace + version changed) — re-add on charts.
   Existing placements keep using the frozen v1_2_4. See memory sentinel-namespace-and-naming.

 (See v1.2.3/v1.2.4 headers + memory sentinel-edge-lanes / backtest-fill-resolution-lesson for full context.)
 Levels (ticks): 10,20,30,40,50,60,80,100. fT<L>/aT<L> = bars to first favorable/adverse touch.
 File: <SentinelCore.SettingsDir>\Excursions\{localStamp}__{inst}__{bartag}.jsonl · signal "CBRK".

 CHANGELOG
   v1.3.3 (in-place, fix 2026-07-16) — CADENCE-INDEPENDENT TIGHTNESS GATE (indicator punch list; the whole chart
            read as one compression base on SentinelFlux). ROOT CAUSE: the coil metric is container/Σ(barRange) — a
            RATIO that assumes small, non-overlapping bars (Renko/TBars bricks). Event-driven Flux bars are large +
            overlapping, so that ratio stays chronically ≤ threshold and the base's run-extension never terminated —
            the box swelled to span the entire price range. FIX: a base must ALSO be physically TIGHT — its box
            height ≤ `BaseMaxAtrMult` × ATR(14) (new `Tight()`), gating BOTH base formation and run-extension. ATR
            adapts to the bar type, so this is construction-independent: a genuine TBars coil IS tight (unchanged),
            while the Flux runaway is capped (it ARMs instead of swelling). New "Base max ATR mult" param (default
            8.0 — safe for TBars; try 4-6 to tighten Flux). ⚠ This also un-poisons the Council's CMP voter on Flux
            scopes. Kept IN-PLACE (class/file identity v1_3_0 — no re-add on charts).
   v1.3.2 (in-place, additive 2026-07-11) — CBRK baseline SCHEMA 1.1 -> 1.3 (ML spec sec 2.3): adds the ATR-scaled
            FIRST-TOUCH label (barrierTicks / barsToTargetR / barsToStopR / firstTouch / ftAmbig), mirroring the
            Council ExcursionRecorder, so CBRK baselines carry the SAME unambiguous target-or-stop-first label and
            become fittable. WRITER-ONLY (RecordExcursions still default OFF; rows still land in Excursions\_baselines\n//             cbrk\<schema>\, OUT of the Council corpus). The fixed-level fT*/aT* touch grid is unchanged. Kept IN-PLACE.
   v1.3.1 (in-place, additive 2026-07-07) — COUNCIL VOTER: publishes SentinelCore.CompressionState
            (breakout PULSE + a HELD BreakDir for BreakHoldBars + coil/compressed/armed). PublishState
            defaults ON. The Council now fuses the breakout as a voter (it previously only saw the hidden
            Signal plot). No behaviour change to detection/rendering. Needs SentinelCore ≥ v1.11.0.
   v1.3.0 (in-place, additive 2026-07-05) — Exposes its breakout as a HIDDEN "Signal" plot (Values[2]):
            +1 on the exact BreakUp bar, -1 on BreakDown, 0 otherwise (mirrors the triangles MarkBreak draws).
            Lets the Sentinel Deck's SIGNAL ARM read the REAL breakout generically (plot, not drawing-scrape).
            Plot is transparent + IsAutoScale=false so the ±1 values never render or squash the price panel.
            Backward-compatible (new plot only) → no version fork, like the Deck's in-place fill-capture add.
   v1.3.0 — Rehomed to Indicators.Sentinel (→ groups under the "Sentinel" picker folder). Clean class name
            (no prefix — the folder gives context). No behaviour change vs v1_2_4. First namespace-convention
            adopter (§7). v1_2_4 frozen.
   v1.2.4 — [frozen, as CompressionBase_v1_2_4] Card docks via SentinelSkin.CardLayout (anti-overlap) + Card
            corner property.
   v1.2.3 — [frozen] SENTINEL GLASS-CARD readout (Painter). DASHED cyan base BOX + box-anchored breakout
            TRIANGLES + ShowBox/ShowBreakouts. BaseHigh/BaseLow → cyan DOT (ninjascript-plot-config-override fix).
   v1.2.2 — [frozen] Legible bar-tag (TBC<Value>-<Value2>-<TypeId>).
   v1.2.1 — [frozen] TP/SL first-touch grid (fT*/aT*).
   v1.2.0 — [frozen] excursion recorder (max-over-horizon MFE/MAE + context).
   v1.1.1 — [frozen] coil detector, run-coil maintenance.
```

