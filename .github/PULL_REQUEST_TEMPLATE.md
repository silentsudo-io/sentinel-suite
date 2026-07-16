<!-- Thanks for contributing to the Sentinel Suite! Keep PRs focused: one tool or one fix. -->

## What & why
<!-- What does this change, and why? Link any issue. -->

## Which rung / layer
<!-- e.g. Rung 1 Sensor · Rung 4 Council · runtime Foundation. See Docs/PRODUCT_LADDER. -->

## Build verification
- [ ] Compiles **clean under NinjaTrader F5** (paste the outcome / that a fresh DLL was written)
- [ ] No duplicate `#region NinjaScript generated code` left on disk
- [ ] (optional) Headless sanity run; only known ghosts remained (`Energy.cs` CS0104, Alighten CS0234)

## If this adds/changes an indicator — compliance
<!-- Tick each; full list in SENSOR_COMPLIANCE_CHECKLIST.md -->
- [ ] 4-layer naming tell (file / class / `Name = "Sentinel <Thing>"` / `…Indicators.Sentinel`)
- [ ] Glass card + `CardCorner` + label remover; palette tokens only
- [ ] Publishes a `…State` seam (default `PublishState` ON) + wired into the Council, if it's a decision input
- [ ] Layering respected — no call into a Safety/order API from a sensor
- [ ] Version + in-file changelog; prior version left frozen

## Provenance & license
- [ ] Original work (or clean-room reimplementation of a publicly-published formula)
- [ ] No proprietary / third-party-engine-derived code
- [ ] I release this contribution for open-source use under this project; I've added myself to `AUTHORS`
