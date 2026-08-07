# SentinelCore.SystemBuilder.cs

> `bin/Custom/AddOns/SentinelCore.SystemBuilder.cs`

| | |
|---|---|
| **Family** | AddOns / runtime |
| **Version** | — |
| **Size** | 618 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `CatalogEntry` |
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
 SentinelCore — SYSTEM BUILDER layer  (partial)
 File: SentinelCore.SystemBuilder.cs   |   part of `static partial class SentinelCore`
─────────────────────────────────────────────────────────────────────────────
 Backs the System Builder (Docs/SENTINEL_SYSTEM_BUILDER_SPEC.md). Two additive pieces,
 Phase 0 of that spec — nothing here changes existing behaviour until a consumer calls it:

   • VoterCatalog — the tag → indicator-class / role / seam / defaults map. `Roster.conf`
     speaks in voter TAGS ("EYE","TRND"…); a chart loads indicator CLASSES ("Eye_v1_1_0"…).
     This is the one bridge between them. Seeded to mirror the Council's KnownVoters +
     SetDefaults (Council v1.4.0) + the orthogonal context axes.

   • RosterIO — ONE parser/writer for `Roster.conf`, so the Council (reader) and the
     System Builder (writer) can never drift on format. Read() reproduces the Council's
     exact cascade + parse; Write() serialises a RosterDoc back atomically.

 DEPENDENCY: Foundation-layer only (SettingsDir, Log). Touches no seam, no Gate.
 Reload note: the Council caches its roster at load, so a Write() takes effect on the
 Council's NEXT reload (spec Phase 4 adds a hot-reload version stamp).
```

