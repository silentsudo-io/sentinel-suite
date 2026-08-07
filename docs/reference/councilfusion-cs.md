---
layout: sentinel-ref
title: "CouncilFusion.cs"
blurb: "AddOns / runtime · unversioned · 213 lines"
---

# CouncilFusion.cs

> `bin/Custom/AddOns/CouncilFusion.cs`

| | |
|---|---|
| **Family** | AddOns / runtime |
| **Version** | — |
| **Size** | 213 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `Config` |
| **Namespace** | `NinjaTrader.NinjaScript.AddOns.Sentinel` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
one at https://mozilla.org/MPL/2.0/.

Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
─────────────────────────────────────────────────────────────────────────────
═════════════════════════════════════════════════════════════════════════════
 CouncilFusion — the pure fusion core of the Sentinel Council (NT8, Sentinel Suite)
 File: CouncilFusion.cs   ·   namespace …AddOns.Sentinel
─────────────────────────────────────────────────────────────────────────────
 WHAT THIS IS
   The Council's decision math, extracted as a PURE FUNCTION with no NinjaTrader / seam / wall-clock
   dependency. Given a set of gathered votes + modulator readings + veto flag + config, it returns the
   verdict (bias · conviction · sizeMult · tally). It performs NO seam reads and NO I/O — every input is
   resolved by the caller.

 WHY IT EXISTS (Docs/SENTINEL_REPLAY_SPEC.md §3)
   So there can be TWO front-ends over ONE fusion truth:
     • the LIVE Council      — gathers votes + modulators FROM SEAMS, then calls Fuse().
     • the REPLAY harness     — gathers the same votes FROM HOSTED sensor instances (bar-by-bar, causal),
                                then calls the SAME Fuse().
   Identical math both places ⇒ a historical (replay) verdict equals the verdict that would have been live
   on that bar — the correctness gate that makes a replay-baked corpus trainable at all (§4). It is also
   the seam of the generic vote registry (a vote is a vote, seam or hosted — memory: council-custom-voters).

 PARITY
   This mirrors Council_v1_0_0.OnBarUpdate's fuse block (v1.8.0) line-for-line: kind-aware denomW,
   deadband→bias, conviction = |netScore| / denomW, the full context-damp chain (breadth · squeeze · clock ·
   participation · MTF · location · PROFILE/InValueDamp · REGIME/HighVolRegimeDamp · FLUX-absorb/FluxAbsorbDamp),
   then veto → sizeMult. As of Council v1.8.1 the Council CALLS this — this file is now the ONLY copy of the math.
   NOTE the account/seam VETO (kill · news · rollover · chop · liquidity WALL) stays in the front-end: it is
   resolved AFTER Fuse (the wall veto needs the fused bias) and applied by zeroing sizeMult+conviction, which is
   bit-identical to passing Vetoed=true into Fuse. Both front-ends (live Council + replay harness) do the same.
```

