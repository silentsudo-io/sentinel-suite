# Changelog

All notable changes to the Sentinel Suite (as an open-source distribution) are recorded here.
Format follows [Keep a Changelog](https://keepachangelog.com); the suite uses **per-bundle SemVer** over the
per-file `_vX_Y_Z` versions inside the source.

> Note: individual tools also carry their own in-file changelogs (that is the per-tool source-of-truth). This
> file tracks the **release bundles**.

## [Unreleased] — P1 scaffolding
The first release is being assembled: the **non-execution beachhead** (draws on charts, never places an order).

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
