# Changelog

All notable changes to the Sentinel Suite (as an open-source distribution) are recorded here.
Format follows [Keep a Changelog](https://keepachangelog.com); the suite uses **per-bundle SemVer** over the
per-file `_vX_Y_Z` versions inside the source.

> Note: individual tools also carry their own in-file changelogs (that is the per-tool source-of-truth). This
> file tracks the **release bundles**.

## [Unreleased] — P1 scaffolding
The first release is being assembled: the **non-execution beachhead** (draws on charts, never places an order).

### Changed — runtime and docs brought back in step with the canonical tree (2026-08-04)
- **`SentinelCore` v1.45.0 → v1.47.0.** Adds the **signal-fire intake** (v1.46.0): `NoteSignalFire` /
  `DrainSignalFires` / `SignalFireStats`, which let a non-Council strategy reach the excursion corpus
  through the same path and schema the Council uses. Deliberately an event **queue**, not a state seam —
  a seam is last-value-wins, so two fires inside one bar would silently drop one, and a fire is exactly
  the thing that must not be lost. Historical fires are rejected at the door rather than queued (a
  queued historical fire drained at realtime is lookahead contamination), the queue is bounded with
  counted drops, and `SignalFireStats()` exists so a dropped or never-drained fire is visible instead of
  silent. v1.47.0 is a catalog row that lives outside this file; only the version string moves here.
  - ⚠ `ResolveLane` remains **deliberately reduced** in the published cut and always will be: the full
    body calls `LaneAssign.Read()`, and `LaneAssign` lives in the unreleased System Builder rung, so
    shipping it would be a `CS0246` for every user. Returning the caller's F6 value is exactly what the
    full version does when there is no `Lanes.conf`, and nothing published writes one.
- **Docs refreshed** against the canonical tree: Roadmap, Design System, Data Platform Spec and the
  documentation index, with `{{core_version}}` / `{{voter_count}}` re-substituted from code (v1.47.0,
  25 voters) so the published numbers cannot drift from the source they describe.
- **`QuickReferenceGuide` retired.** Pre-Sentinel, dated January 2026, never updated; superseded by the
  Suite Manual. Archived upstream on 2026-08-03 and now removed here rather than left as a stale page.

### Added
- **`tools/publish_doc.py`** — renders a canonical doc into its published form: strips the internal audit
  frontmatter, substitutes `{{tokens}}` from `facts.json`, and rewrites internal links. Closing doc drift
  was never a copy, and doing it by hand shipped a literal `{{core_version}}` exactly once in testing.


### Fixed — from the first wave of public installs (2026-07-27)
Everything in this block was found by people installing rungs 0-1 for real. That is the point of shipping.
- **Import archives were missing a compile-time dependency.** `tools/make_ninjascript_archive.py` *silently
  skipped* any `.cs` outside the nine NinjaScript folders, so `Shared/TbarsSudoV3Config.cs` — which
  `SentinelTBars` needs to compile — never reached the zip. Since NinjaTrader builds all of `bin\Custom` into
  one assembly, that is a `CS0246` that takes down the user's **entire** tree, not just Sentinel. The folder is
  now remapped to `AddOns\`, and **a `.cs` that maps nowhere is a hard build failure** rather than a printed
  warning. ⚠ If you previously hand-copied that file to `bin\Custom\Shared\`, **delete it before re-importing**
  or you will have two definitions and a `CS0101`.
  *(The dependency checker was right all along — it validates `src/`, while the archive shipped a subset of
  `src/`, and nothing validated the archive. The bug lived in the gap between two correct tools.)*
- **Cards collapsed to bare chips on crowded charts.** `CardLayout.MinCardScale` was `0.80`, which left so
  little travel that a column with many stacked sub-panels skipped scale-to-fit entirely and went straight to a
  22px chip. Now `0.60`, plus a `Sentinel\min-card-scale.txt` runtime tunable (no re-import needed).
  **Diagnosed and first implemented by sneaky_zekey** — see AUTHORS.
- **Bar types render Heikin-Ashi bodies, and candle colour is NOT brick direction.** `SentinelTBars` smooths
  bodies (`(O+H+L+C)/4`) while wicks stay real, so near a turn the body and the printed brick routinely
  disagree. The authoritative direction is `SentinelCore.BrickState.Direction`, never the pixel — and an HA
  close is a price that never traded, so never record one as a fill. This was true since v1.0.0 and undocumented
  until a tester hit it. Now carried in the `SentinelTBars` header.
- **Docs: the Council fusion formula on the Field Manual was superseded.** It documented
  `Conviction = |netScore| / activeW` — dividing by the *awake* voters, which reads **100% conviction from a
  single attached sensor** with nothing able to disagree. The shipping Council divides by `denomW` (the
  **declared** roster, kind-aware), so absence dilutes. A reader reimplemented the page faithfully and inherited
  a bug we had already fixed twice; the manual now documents the current formula, the conviction/`contextMult`
  split, and both traps explicitly.

### Added — 2026-07-28
- **Sentinel Binds bundle** (Rung 0) — window **snapping** (exact and seamless, applied on release), named
  **binds** whose members drag as one group, and named **layouts** you re-apply in a click. Settings live in
  `Sentinel\Binds\`. **No market data, no orders, no `SentinelCore` seam** — the most isolated component in
  the suite, and deliberately so: it is the easiest thing here to read, fork and extend.
  ⚠ Two honest limits are documented rather than papered over: **layouts arrange, they do not spawn**
  (unmatched windows are reported by name, never half-applied silently), and **snap happens on release**, not
  during the drag, because NT re-assigns position from the mouse on every move and anything written mid-drag
  is overwritten.
- **`tools/check_runtime_api.py`** — a **member-level** API checker. `check_bundle_deps.py` catches a missing
  *type*; it cannot catch a missing *member*, and the member is what kept biting: code backported verbatim
  from the private tree called `SentinelCore.Swallow`, which existed locally (v1.41.0) but **not** in the
  published core (v1.36.0) — a `CS0117` on every public install. That was caught by hand twice. It is now
  mechanical: every `SentinelCore.X` / `SentinelSkin.X` a bundle references must be declared in the runtime
  that ships beside it. Validated with a negative control (injected bogus members → detected, exit 1).

### Added — Sentinel Deck v0.2.6 (2026-07-28)
- **The Deck now records what a market fill actually cost.** Until now, only *limit and stop* fills carried
  execution-cost information; a **market** order recorded none — the reference price was `0`, so the `slip`
  field was omitted entirely and market order rows logged `px:0`. The most common way to enter a trade was
  the one you could not measure. v0.2.6 latches the live bid/ask and stamps the tradeable price on the side
  being crossed (**ASK** when buying, **BID** when selling) at submission, so every fill row now carries
  `slip` in ticks, adverse-signed:
  - **limit / stop** → measured against the limit or trigger price (*stop slippage*). Unchanged.
  - **market** → measured against the quote you crossed (*crossing cost*).
- Covers all five of the Deck's market-order sites: deck Buy/Sell, Close, Reverse, Half, half-at-breakeven.
- ⚠ **Upgrading requires re-adding the Deck to your charts.** Namespace + class name are a NinjaScript
  indicator's serialization identity, so v0.2.6 is a *different* indicator to NinjaTrader: existing v0.2.5
  instances keep running and do not inherit anything. Both versions can coexist. See the tester's guide §1.
- ⚠ **Realtime-only** (the reference comes from live market data) and **forward-measuring** — it recovers
  nothing about fills already taken.

### Changed — publish drift closed (2026-07-28)
- **48 files brought up to the canonical tree.** The published snapshot had been sitting behind the private
  tree; most of that was the **`Swallow` migration**, which was *required* to stay behind while the published
  core was v1.36.0 and had no `SentinelCore.Swallow` — a verbatim backport would have been `CS0117` for every
  installer. Shipping the core at v1.45.0 retired that constraint, so:
  - 23 smoothers + ~15 sensors move from bare `catch {}` to `SentinelCore.Swallow` — a fault in a card or a
    seam is now counted and logged instead of vanishing. Behaviour is identical; `Swallow` never rethrows.
  - `SentinelFlux` / `SentinelTbarsCount` gain **`ReplayMode`** (`Globals.Now` is wall-clock even in Playback,
    so the freshness guard returned on *every* tick and the seam never published during a replay), the
    generation beacon, and a wall-clock throttle that actually throttles during a historical rebuild.
  - `VolEnvelope` now honours the cards-off render kill switch — it was the one tool still drawing when cards
    were off.
- ⚠ **Every bundle now requires `SentinelCore` v1.41.0 or newer.** This floor used to apply to `Sentinel Binds`
  alone; it is now suite-wide. The downgrade hazard is unchanged, it just applies everywhere: copying an older
  bundle's `runtime/` over a newer one breaks the compile for your **whole** `bin\Custom` tree, not just Sentinel.
- `tools/check_parity.py` now separates **DELIBERATE** publish-time differences from real **DRIFT**, so the one
  permanent exception (`ResolveLane`, below) is labelled rather than sitting in the drift list forever — a check
  that is always red is one nobody reads. The exemption pins the expected line count, not just the filename, so
  new divergence landing in an exempted file still surfaces.

### Changed — 2026-07-28
- **Published runtime: `SentinelCore` v1.36.0 → v1.45.0.** The published core had drifted nine versions
  behind, which meant public installs carried real defects that were already fixed privately:
  - **v1.42.0 — log retention.** `sentinel.log` rotation kept exactly **one** generation and deleted it on
    every roll. At the rates this log actually hits, that is minutes of history; it destroyed a forensic
    window twice in one night. Now keeps six generations.
  - **v1.40.0 — assembly-generation beacon.** After an F5, a chart's bars-type instance stays on the *old*
    assembly and publishes into an orphaned seam store. Consumers can now say **"decoupled — restart NT"**
    instead of reporting the sensor as simply absent.
  - **v1.45.0 — `PressureState`.** `BuySellVolumePressureMountain` shipped with a card but no `…State` seam,
    so it computed an order-flow opinion nothing could consult. *(Reported by a public installer.)*
  - Also: `Swallow()`/`Faults()` (v1.41.0, the recorded empty catch), `CvdState` (v1.43.0), `ConvictionState`
    (v1.37.0), and `ReplayMode` (v1.38.0).
  - ⚠ **One deliberate publish-time difference:** `ResolveLane` returns the caller's F6 value unchanged. Its
    full body reads `Sentinel\Lanes.conf` via `LaneAssign`, which lives in the unreleased System Builder rung;
    rather than ship half that substrate, the published build returns exactly what the full version returns
    when there is no `Lanes.conf` and no matching entry — and nothing in the released bundles writes one. The
    signature is preserved so the API does not move when the rung ships.
- **Both import archives rebuilt** against the new runtime. The Deck archive **embeds** the runtime, so a core
  bump makes it stale even though the Deck itself did not change.

### Added
- **Repository scaffold** — README, NOTICE, AUTHORS, LICENSE (TBD), CONTRIBUTING (the Platform Contract),
  SENSOR_COMPLIANCE_CHECKLIST, CODE_OF_CONDUCT, SECURITY, and GitHub issue/PR templates.
- **Sentinel Skins bundle** (Rung 0, pure L0) — the `SentinelSkin` drawing framework + `SentinelWallpaper`
  + six platform themes (Dark / Light / Silver / Obsidian / Blueprint / Amber).
- **Sentinel Sensors bundle** (Rung 1) — 8 hero indicators (SentinelTrend, ADXPro, WoodiesCCIPro, VolEnvelope,
  CompressionBase, LiquidityWalls, God Reversal, WAE) + BSVPMountain (volume-pressure) + the Sentinel bar types
  + BrickCounter, over the
  runtime **without the Safety layer** (`SentinelCore` Foundation + Bus only). Includes a per-sensor reference.

### Architecture
- **Runtime split** — `SentinelCore` became a `partial class` across `Foundation` / `Safety` / main (Bus)
  files (Core **v1.23.0**), so the Sensors bundle ships **without** the account-risk/order-gate code and is
  verified to compile without it.

### Provenance
- Every shipped sensor is original work, an original implementation of a **public method**, or an
  **MPL-2.0** component with attribution. **LiquidityWalls** ships under MPL-2.0 (© TradingIQ, attributed);
  **WAE** was rewritten **clean-room** from the public Waddah Attar formula (the earlier unlicensed port was
  retired). Provenance was audited per sensor on 2026-07-11. See `NOTICE`.

### Pending before the P1 release
- Visual showcase (theme gallery + sensor screenshots + a demo workspace).
- Paste the full canonical MPL-2.0 text into `LICENSE` (the license itself is **chosen: MPL-2.0**).
- Contributor names recorded in NOTICE / AUTHORS.

---

*Future phases (not yet released): P2 Intelligence (Recorder, Observatory, Council) · P3 Execution
(Prop-Survival Kit, Deck, Bridge, Copier, Helm) · P4 the ML Lab. See Docs/PRODUCT_LADDER.*
