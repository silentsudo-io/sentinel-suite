# SentinelDrift_v0_1_0.cs

> `bin/Custom/BarsTypes/SentinelDrift_v0_1_0.cs`

| | |
|---|---|
| **Family** | Bar types |
| **Version** | 0.1.0 |
| **Size** | 690 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelDrift_v0_1_0` |
| **Namespace** | `NinjaTrader.NinjaScript.BarsTypes` |
| **Publishes seams** | `BrickState`, `ConvictionState` |
| **Documented by** | _no doc tracks this artifact_ |
| **Depends on this** | [Council_v1_11_0.cs](council-v1-11-0-cs.md), [SentinelBrickCounter_v1_0_0.cs](sentinelbrickcounter-v1-0-0-cs.md), [SentinelCandidateRecorder_v1_0_0.cs](sentinelcandidaterecorder-v1-0-0-cs.md), [SentinelCockpit_v0_1_0.cs](sentinelcockpit-v0-1-0-cs.md), [SentinelExcursionRecorder_v2_0_0.cs](sentinelexcursionrecorder-v2-0-0-cs.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
one at https://mozilla.org/MPL/2.0/.

Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
─────────────────────────────────────────────────────────────────────────────
═════════════════════════════════════════════════════════════════════════════
 SentinelDrift — asymmetric, conviction-adaptive "drift brick" BARS TYPE  [PROTOTYPE]
 File: SentinelDrift_v0_1_0.cs        Class/Type: SentinelDrift_v0_1_0
 Display Name: "SentinelDrift v0.1.0"  ·  BarsPeriodType id: 212204 (reserved Sentinel bars block 212200–212299)
─────────────────────────────────────────────────────────────────────────────
 WHAT THIS IS  (and why it exists)
   A Renko-style brick whose TWO thresholds are FIRST-CLASS and asymmetric:
     • continuation edge — cheap: how far price must go to PRINT-in-trend.
     • reversal edge      — expensive: how far AGAINST the trend to flip.
   "Drift" = the directional (trend) term of a diffusion. The brick drifts with
   the trend easily and RESISTS reversal in proportion to conviction.

   This idea already lives, HIDDEN, inside SentinelTBars: its "Speed Settings"
   knob collapses to trend=SS/2, reversal=SS*2 — a fixed, un-tunable 4:1 bias,
   plus a binary hysteresis latch (reversal ×1.5 after N same-way bricks).
   SentinelDrift makes that asymmetry a DIAL and replaces the on/off latch with
   a SMOOTH conviction curve:
     • Trend Bias    (Value)              reversal ÷ continuation. 1 = symmetric
                                          renko; 4 = TBars' default; higher = trend-rider.
     • Conviction×10 (Value2)             ceiling on how much a persistent run
                                          widens the reversal edge. 10 = OFF (pure
                                          static bias); 15 = up to 1.5× at full run.
     • Speed Settings(BaseBarsPeriodValue) overall brick size (continuation = SS/2 ticks).

   It keeps the PROVEN TBars core (ATR-adaptive floor, breakout confirmation,
   Heikin-Ashi bodies + real wicks, stagnation time-brick) and DROPS the parts
   orthogonal to the asymmetry story (density controller, quiet-hours, micro-split,
   the live registry latch) so the ONLY behavioural difference vs TBars is the bias
   + conviction curve — which makes A/B measurement clean.

   Publishes to SentinelCore.BrickState under its OWN scope (distinct BarsPeriodType
   id ⇒ distinct bartag ⇒ no collision with a TBars chart), so nothing else changes.

 ⚠⚠ CANDLE COLOUR IS NOT BRICK DIRECTION — Drift inherits TBars' HEIKIN-ASHI bodies,
     so a body is coloured by the smoothed average, not by the brick that printed;
     near a turn they routinely disagree. Authoritative direction is
     SentinelCore.BrickState.Direction, never the pixel. Wicks are real prices; the
     BODY is synthetic — never record an HA close as a fill. Full note in the
     SentinelTBars_v1_0_0.cs header. (Reported by sneaky_zekey, who had to write this
     warning into his own tool because ours did not carry it.)

 ⏭ STAGE-2 HOOK (not in this prototype): fold ORDER-FLOW agreement into the
   conviction multiplier — flow confirming the trend makes the brick stickier,
   absorption against it makes it twitchier. See ApplyConviction() below.

 ⚠ BARS TYPES ARE STICKY ACROSS A COMPILE. nt8bridge/F5 VALIDATES but does not
   hot-swap a live bars-type instance — after changing this file you must
   Editor-F5 AND reload the chart (see [[sentinel-flux-tool]] bring-up lesson).

 CHANGELOG
   v0.1.0 (2026-07-19) — first prototype. Trend Bias dial + smooth conviction
                         hysteresis, on the TBars-proven construction core.
   v0.1.0 (same day)    — DIAL-BUG FIX: the original stagnation ForceTimeBrick re-derived direction + reset the
                         run, flipping the trend on a cheap move and MASKING the Trend Bias dial (bias 4 vs 20
                         looked identical). Made it CONTINUATION-ONLY (keeps barDirection + run; reversal edge
                         stays effRev away). Dial now scales as designed: bias 4 → 24t reversal, bias 20 → 120t.
   v0.1.0 (Stage 2)     — FLOW-ADAPTIVE REVERSAL: sign the tape (quote rule → tick-rule) into per-brick delta;
                         a flow factor (~0.6x..1.4x, self-calibrated) scales the reversal edge — flow confirming
                         the trend widens it (sticky), absorption tightens it (twitchy). ⚠ needs TICK data; gated
                         inert on isBar/no-tick rebuilds. NEXT: 2b = publish ConvictionBias → Council voter.
```

