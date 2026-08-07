---
layout: sentinel-ref
title: "SentinelCore.Safety.cs"
blurb: "AddOns / runtime · unversioned · 458 lines"
---

# SentinelCore.Safety.cs

> `bin/Custom/AddOns/SentinelCore.Safety.cs`

| | |
|---|---|
| **Family** | AddOns / runtime |
| **Version** | — |
| **Size** | 458 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `GovernorState` |
| **Namespace** | `NinjaTrader.NinjaScript.AddOns.Sentinel` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
one at https://mozilla.org/MPL/2.0/.

Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
────────────────────────────────────────────────────────────────────────────
=============================================================================
 SentinelCore - SAFETY layer  (partial)
 File: SentinelCore.Safety.cs   |   part of static partial class SentinelCore
-----------------------------------------------------------------------------
 PRODUCT-LADDER RUNTIME SPLIT - see Docs/PRODUCT_LADDER.md sec 4-5.
 L2 SAFETY = the account-risk DECISION logic (feed-health, CanEnter, governor,
 drawdown, account profiles, session, sizing, order guards, GateEntry). This is
 the ONE file the Skins/Sensors bundles OMIT (they never place an order).
 Depends DOWNWARD on Foundation only (news/rollover/InstrumentRoot/Ledger).
 Migrated 2026-07-10 batch 2: governor/drawdown/profiles/sizing/gate block.
 Same class, same call sites -> zero consumer churn.
=============================================================================
```

