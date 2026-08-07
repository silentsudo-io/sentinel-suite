# ⚙️ Configuration

**Reference configuration files.** Copy into `Documents\NinjaTrader 8\Sentinel\` and edit.

## What's here
`Alerts.conf` · `Arc.conf` · `binds.conf` · `catalog.conf` · `Cockpit.conf` · `Copy.conf` ·
`event_tiers.conf` · `Fetch.conf` · `News.conf` · `News.history.conf` · `NewsService.conf` ·
`Profiles.conf`

## What is deliberately NOT here
**No fitted `Roster.conf` or `Model.conf`.** A roster is a *result* — which voters are live, at what
weight, fitted against one operator's outcomes on one instrument. Publishing one would be publishing a
conclusion and inviting you to inherit it.

What ships instead is the **mechanism**: the parser, the STATE/TRIGGER kinds, and the `w=0` audition
primitive that lets a voter run and be recorded without moving the verdict. Fit your own.

---

Part of the [Sentinel Suite](../../README.md) · MPL-2.0
