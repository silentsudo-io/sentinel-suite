# Changelog

All notable changes to the Sentinel Suite (as an open-source distribution) are recorded here.
Format follows [Keep a Changelog](https://keepachangelog.com); the suite uses **per-bundle SemVer** over the
per-file `_vX_Y_Z` versions inside the source.

> Note: individual tools also carry their own in-file changelogs (that is the per-tool source-of-truth). This
> file tracks the **release bundles**.

## [Unreleased] — P1 scaffolding
The first release is being assembled: the **non-execution beachhead** (draws on charts, never places an order).

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
