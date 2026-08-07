---
layout: sentinel-ref
title: "SentinelBridge_v0_2_0.cs"
blurb: "Strategies · 0.2.0 · 1046 lines"
---

# SentinelBridge_v0_2_0.cs

> `bin/Custom/Strategies/SentinelBridge_v0_2_0.cs`

| | |
|---|---|
| **Family** | Strategies |
| **Version** | 0.2.0 |
| **Size** | 1046 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelBridge_v0_2_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Strategies` |
| **Publishes seams** | `HelmState` |
| **Consumes seams** | `CouncilState`, `GodReversalState` |
| **Documented by** | [SENTINEL_STRATEGY_INTEGRATION_SPEC](../../SENTINEL_STRATEGY_INTEGRATION_SPEC.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
one at https://mozilla.org/MPL/2.0/.

Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
─────────────────────────────────────────────────────────────────────────────
═════════════════════════════════════════════════════════════════════════════
 SentinelBridge — the automated Council-consumer + on-chart control surface (Sentinel Suite, NT8)
 File: SentinelBridge_v0_2_0.cs   ·   Version v0.3.1   ·   namespace …Strategies (BASE — sub-ns hides strategies)
─────────────────────────────────────────────────────────────────────────────
 WHAT  (see Docs/BRIDGE_SPEC.md — Phase 2, the control surface)
   The autopilot counterpart to the manual Deck. Trades the fused SentinelCore.CouncilState verdict
   (Council-as-signal) through the authoritative GateEntry (fail-CLOSED), managed bracket, and RECORDS
   the verdict on every fire so Lens can grade the weights. v0.2.0 adds the ON-CHART CONTROL SURFACE:
   a Sentinel glass card (live verdict + strategy state) with a clickable **ARM BRIDGE** button.

   SAFETY — automation is OFF on load and can ONLY be armed by a deliberate CLICK on the card's ARM
   BRIDGE button (never silently auto-arm — the Deck lesson; the armed flag is runtime-only, never
   persisted). DISARMED = the card shows the live verdict but the engine places NO orders (behaves like
   the headless v0.1.0). ARMED = it fires per the P1 logic. No chart (headless) = no button = stays
   disarmed = safe no-trade (this is a CHART trader by design).

   Engine (unchanged from v0.1.0): edge-detected bias flip into an aligned edge (one-shot per verdict
   episode, flat-only), size baseQty × SizeMult, GateEntry (enter only on IsClear), managed bracket
   (SetStopLoss/SetProfitTarget in ticks), record verdict (Ledger.Order + "bridge-fire" Action).
   v0.2.0 adds an optional Council-flip EXIT (close if the Council stops supporting the open side).
   TP/SL hand-set from the COUNCIL ◆ in the Excursion tab. MANAGED (never close a managed position with
   raw orders — ExitLong/ExitShort is the managed-safe close).

 CHANGELOG
   v0.3.1 (in-place, INSTRUMENTATION 2026-07-28) — THE FILL-COST REFERENCE PRICE. Measurement only: the order
            path, sizing, gate, bracket and Helm logic are byte-for-byte v0.3.0. Fixes the reason Question 1
            ("what does a fill actually cost us?") could not be answered from the corpus we already own.
            TWO defects, both of which recorded a number that LOOKED like a measurement and was not:
              (1) `OnExecutionUpdate` set `intended = price` for a MARKET order, so `slip = fill − fill = 0`
                  IDENTICALLY. All 39 market entries in the Ledger read slip 0 — an artifact of this line, not
                  an observation of the market. Entry crossing cost had never once been measured.
              (2) `RecordFire` logged `Close[0]` as the order's decision price. On a Heikin-Ashi bar type
                  (SentinelTBars, which is what the Bridge runs on) Close[0] is the (O+H+L+C)/4 AVERAGE — a
                  price that NEVER TRADED. Joining order→fill over the 39 live entries showed the signature
                  exactly: longs "filled" a median +11.0 ticks above and shorts −11.0 ticks below the recorded
                  price, symmetric by direction. Same defect, same magnitude as the excursion recorder's
                  "9-tick bleed" (memory firepx-is-synthetic-ha-close), fixed there at schema 1.5 and still
                  live here. So BOTH the fill row and the order row were unusable, for two different reasons.
            FIX: capture the TRADEABLE quote on the side being crossed (buy → ASK, sell → BID) at the moment
            of SUBMISSION, at all five market-order sites (entry · council-flip exit · Helm flatten · Helm
            scale-down ×2), and use it as the reference for both the Ledger order row and `intended`.
            Stop/limit exits are UNCHANGED — their `intended` was already the real trigger/limit price and is
            the one honest execution number the Ledger has ever held (prop accounts: median +1.0 tick adverse).
            Fails soft: no quote ⇒ 0 ⇒ falls back to the v0.3.0 behaviour rather than record an indefensible
            number. Historical/replay returns the bar close, so the measurement is meaningful REALTIME only.
            ⚠ FORWARD-MEASURING ONLY — this recovers nothing about past fills; those 39 entries stay unusable.
            ⚠ KNOWN, DELIBERATELY NOT CHANGED: the `_liveStopPrice`/`_liveTargetPrice` seeds in TryEnter still
            use Close[0] and so inherit the same HA offset. They are transient (recomputed exactly from
            Position.AveragePrice once filled) and feed only the card + Helm's tighten/widen classify, so
            correcting them would alter interdiction behaviour — out of scope for an instrumentation bump.
   (in-place, 2026-07-25) — RECORDED CATCHES: 28 empty `catch {}` migrated to SentinelCore.Swallow
            (Core >= v1.41.0). Runtime behaviour is IDENTICAL — Swallow never rethrows — but a fault in
            the order path is now counted, rate-limited and logged instead of vanishing. Silent catches
            are the proven mechanism of every expensive bug in this suite; see Docs/SENTINEL_ADVERSARIAL_REVIEW.md §2.
   v0.3.0 (in-place, 2026-07-15) — HELM INTERDICTION CONSUMER (Phase 5; SentinelCore ≥ v1.34.0; memory
            helm-interdiction-layer). The Bridge is now the FIRST owner that obeys Helm intents — a human can grab
            the wheel of this running autopilot WITHOUT stopping it. It drains SentinelCore.TakeHelmIntent(InstanceKey())
            on every tick (new OnMarketData, realtime) + a bar-close backstop, executes the intent with its OWN order
            handles (never a raw order), publishes HelmState back so a Helm surface renders reality, and writes EVERY
            intent to the Ledger ("helm-intent" Action, stamped with the Council EpisodeId + instanceKey) + marks the
            episode HumanOverride so the Lab can exclude/model interdicted trades (recording the human keeps the model
            honest). ASYMMETRIC GATE: risk-REDUCING verbs (FlattenNow/Pause/SkipNext/BreakevenNow/tighten-stop/
            scale-down) are fail-OPEN; risk-ADDING (Resume/widen-stop/HandBack) validate through GateEntry fail-CLOSED.
            MANAGED-MODE HONESTY: Scale-UP is REFUSED (single-entry managed can't scale-in without desync — use the
            Deck); TakeOver/HandBack map to stand-down/resume (a managed position can't transfer order ownership
            without disable/re-enable). New [Display]-only "Obey Helm intents" toggle (default ON). No ctor churn, no
            change to the entry engine when no intent is pending ⇒ byte-for-byte the v0.2.4 trade path. Keeps the
            v0_2_0 class/file identity (no serialization break for the live chart instance).
   v0.2.4 (in-place, 2026-07-14) — PER-CHART LANE (Core ≥ v1.32.0). New [Display]-only "Scope Lane" param: set it to
            match the "Scope Lane" on THIS chart's Council so the Bridge reads the correct lane's CouncilState
            (via ScopeOfLane — a strategy has no shared ChartControl, so it targets the lane explicitly). Blank =
            bare scope (back-compat). Lets the Bridge auto-trade one of two same-bartype A/B test lanes without
            consuming the other's brain. No ctor churn (not [NinjaScriptProperty]).
   v0.2.3 (in-place, IDENTITY 2026-07-11) — ACTOR IDENTITY + EPISODE JOIN (ML spec §10; SentinelCore ≥ v1.25.0).
            (1) Derived `InstanceKey()` = "SentinelBridge#<scope>@<account>". (2) The ARM button is now an INTERLOCK:
            it `RegisterActor`s the key and REFUSES to arm on a collision ("NAME TAKEN") — two live Bridges on one
            scope+account is the managed-position desync hazard, so the ambiguous config is blocked, not merely
            warned. Released (reference-checked) on disarm + Terminated. (3) Every fire now stamps the Council's
            `EpisodeId` + the instanceKey onto the Ledger Order/Action/Fill, so Lens can join a FILL → its EPISODE →
            the verdict that caused it. Behaviour of the trade path itself is UNCHANGED (sizing/gate/bracket as v0.2.2).
   v0.2.2 (in-place, SAFETY 2026-07-10) — SIZING ROUTES THROUGH `SentinelCore.SizedQuantity()`. The Bridge stopped
            at `Math.Max(1, baseQty × SizeMult)` and never called it, so THREE things were silently ignored:
              • the account profile's `size=` (SizeScale) from Profiles.conf;
              • the governor's `RecommendedSize()` — a governor telling this strategy to size DOWN was NOT OBEYED;
              • `ContractLimit`, which hard-BLOCKED every entry when BaseContracts exceeded it, instead of clamping.
            Now: baseQty → × SizeMult → SizedQuantity (SizeScale × RecommendedSize, clamped to ContractLimit,
            never < 1) → GateEntry(riskDollars=0) VALIDATES. That is the order SentinelCore's own header
            prescribes — "SizedQuantity is the one place sizing math lives." A resize is logged.
            ⚠ RESOLUTION CAVEAT, now documented at the call site: with BaseContracts = 1 the Council's SizeMult
            CANNOT scale a position down — 1 × 0.19 rounds to 0 and the Max(1,…) floor restores a 1-lot, because
            a fifth of a contract does not exist. SizeMult only has resolution at BaseContracts ≥ 2 (≥ 4 to
            resolve its typical 0.2–1.0 range). ConvictionFloor (SizeMult = 0) is what expresses "do not trade";
            SizeMult is not a substitute for it. Pair this with Council v1.2.1's floor of 0.20.
   v0.2.1 (in-place, SAFETY 2026-07-09) — the card is now `pinned: true` in CardLayout.Place. It carries the
            ARM BRIDGE button (`_armBtn` is hit-tested against this rect), so if a crowded column overflowed and
            the card were hidden, the ONLY on-chart arm/disarm control for an order-placing strategy would
            silently vanish. CardLayout never hides a pinned card. No order-logic change.
   v0.2.1 (in-place, COSMETIC 2026-07-09) — LABEL REMOVER. NT drew this strategy's top-left chart label from
            Name, dumping the whole parameter list over the chart. Adopted the suite's indicator standard:
            new "Show strategy label" toggle (default OFF) blanks Name in State.DataLoaded. Order routing is
            UNAFFECTED — order tags come from the separate `_tag` const, never from Name. The toggle is
            [Display]-only (not [NinjaScriptProperty]) so the generated-region ctor signature is unchanged and
            saved workspaces keep loading. ⚠ If the Control Center Strategies grid ever shows a blank row,
            flip it ON — you must always be able to find and disable a running strategy.
   v0.2.1 (in-place, additive 2026-07-08) — GOD REVERSAL ENTRY TRIGGER (opt-in, default OFF; the Council-bias ×
            reversal-trigger loop from the God Reversal doctrine §7). When UseGodReversalTrigger is ON, an entry
            also requires a FRESH SentinelCore.GodReversalState (≤ GrevMaxAgeSec) whose held Dir is ALIGNED with
            the Council bias and whose Quality ≥ GrevMinQuality — the Council supplies bias/edge/size, the reversal
            supplies the entry TIMING (the doctrine's "reversal at a predictable place"). The trigger read (dir/
            setup/quality) is appended to the Ledger fire record + sentinel.log so Lens can grade the reversal
            weight, and shows on the card when armed+flat. Default OFF ⇒ byte-for-byte the v0.2.0 engine. Keeps
            the v0_2_0 class/file identity (no serialization break for the live chart instance) — Council-GREV
            in-place-additive precedent. Needs Sentinel God Reversal on the chart + SentinelCore ≥ v1.14.0.
            Also (observability): the fire record now carries the BRACKET (tp=/sl= ticks) so a grader can
            classify each trade TP-hit / SL-hit / flatten by comparing the exit fill to entry ± the bracket.
            HONEST STALE MESSAGE (in-place 2026-07-08): the card now distinguishes a genuinely ABSENT Council
            ("add the Council to this chart") from a STALE one ("Council verdict STALE — no fresh read for Ns —
            slow bars"). Pairs with the Council's OnMarketData heartbeat: the old flat "no verdict" was misleading
            in dry-up markets where the Council IS present but its published state aged past StaleSec.
   v0.2.0 — on-chart glass card + clickable ARM BRIDGE button (ChartControl mouse hit-test); arming gates
            all firing; optional ExitOnCouncilFlip; ShowCard/CardCorner. Supersedes v0.1.0 (archived).
            FIXES (post first live render): (a) the card reads CouncilState LIVE every render (was bar-close only
            → "no verdict" on slow bars); (b) gate on SizeMult>0 (SizeMult=0 = Council floor/veto = no trade),
            else min a 1-lot; (c) pass riskDollars=0 to GateEntry — the Bridge sizes itself, the Gate only
            VALIDATES; passing RiskDollars flipped it into risk-sizing → "risk too small for a 1-lot" Advisory →
            fail-closed block (the first live test never fired). Base `…Strategies` ns (sub-ns hid the strategy).
            FEATURE — .conf AUTO-READ: UseSentinelConfig reads <inst>_COUNCIL_<Long|Short>.conf (written by the
            Excursion tab's Apply ◆, fed by SentinelExcursionRecorder_v1_4) for per-direction TP/SL, overriding
            the manual ticks (re-reads on enable/recompile). Closes the lab → Bridge "real numbers" loop.
   v0.1.0 — [archived] headless engine: consume CouncilState → GateEntry (fail-closed) → managed bracket
            → record verdict on fire.
```

