# Contributing to the Sentinel Suite

Thanks for wanting to build on Sentinel. This is a **platform**, not just a set of tools — anything you
add can carry the same tell and plug into the same seams. This guide is the **Platform Contract**: the
rules that keep every Sentinel tool seamless.

> By contributing, you affirm your contribution is your own original work (or a clean-room
> reimplementation of a *publicly-published* formula), that you have the right to release it, and that
> you release it for open-source use under this project. **Do not** submit code derived from proprietary
> or third-party engines. Add yourself to `AUTHORS`.

---

## Ground truth about the build

- **NinjaTrader 8 is required.** This is NinjaScript source; NinjaTrader's own assemblies are not shipped.
- **NinjaTrader compiles *everything* under `bin\Custom` into ONE assembly.** A single broken file (e.g. a
  duplicate class, `CS0101`) blocks the *whole* compile — if your new tool "doesn't show up," suspect a
  compile break somewhere else first.
- **NinjaTrader's F5 is authoritative.** Headless `dotnet build` is flaky and emits *ghost* errors NT does
  not (see "Build & verify"). Never trust headless over F5.
- **Never round-trip a `.cs` through PowerShell `Get/Set-Content`** — it double-encodes UTF-8 as cp1252 and
  silently corrupts non-ASCII glyphs. Use `[System.IO.File]::ReadAllText/WriteAllText` with
  `UTF8Encoding($false)` if you must script edits.

## The Platform Contract (the 4-layer tell)

Every Sentinel **indicator** carries the "Sentinel &lt;Thing&gt;" identity on four layers:

1. **File** — `Sentinel<Thing>_vX_Y_Z.cs`
2. **Class** — `Sentinel<Thing>_vX_Y_Z`
3. **Display** — `Name = "Sentinel <Thing>"`
4. **Namespace** — `NinjaTrader.NinjaScript.Indicators.Sentinel` (they cluster under a **Sentinel** picker folder)

⚠ **Strategies are the exception** — NT's Strategy selector *hides* sub-namespaced strategies, so a strategy
stays in the **base** `NinjaTrader.NinjaScript.Strategies` namespace and carries the tell via **class-name
prefix + display Name only.**

Plus, every Sentinel indicator:
- draws a **glass card** via `SentinelSkin.Painter` + `SentinelSkin.CardLayout` (with a `CardCorner` property),
- includes the **label remover** (`ShowIndicatorLabel` toggle; set `Name = string.Empty` first in `State.DataLoaded`),
- uses **`SentinelSkin` palette tokens** (no hardcoded colors), and
- if it emits a signal / regime / bias / context, **publishes a `…State` seam to `SentinelCore`**
  (default `PublishState` **ON**) so the Council can fuse it. A hidden plot alone is not enough.

The full checklist is **[SENSOR_COMPLIANCE_CHECKLIST.md](SENSOR_COMPLIANCE_CHECKLIST.md)** — a PR that adds
an indicator must satisfy it.

## Building with Claude Code (optional)

If you use [Claude Code](https://claude.com/claude-code), this repo ships agent **skills** in
[`.claude/skills/`](.claude/skills/) that automate the workflows above and enforce this contract for you —
they load automatically when you open the repo:

- **`port-sentinel-indicator`** — convert a raw NT indicator into a compliant Sentinel sensor (license/provenance
  gate first, the four-layer naming law, glass card + card-render rules, and a published `SentinelCore` state seam).
- **`build-sentinel-skin`** — add a new theme end-to-end (the `SentinelSkin` palette + the 16-file platform skin folder).

More skills land as the suite grows. They're a convenience, not a requirement — the contract above is the source of truth.

## The layering rule

The runtime is layered — **L0 Skin · F Foundation · L1 Bus · L2 Safety** (see the Product Ladder §4). The one
rule: **a file may only reference files in its own layer or below.** A *sensor* (L1) must never call a
*Safety* (L2) API (`GateEntry`, `CanEnter`, governor, sizing…) — that keeps the sensor bundles shippable
without the execution layer.

## Versioning

- Per-file: `_vX_Y_Z`. **Old versions are frozen checkpoints — never edit them**; bump to a new file/name.
- Keep file name, class name, `Name`, and any version-suffixed enums **in sync** on a bump, and update the
  **in-file changelog**.
- Released bundles use **SemVer** (e.g. Sentinel Sensors v1.0.0) over the per-file versions.

## Build & verify (there is no conventional CI)

Because NT is one assembly and F5 is authoritative, "CI" here is a **recipe + a manual checklist**, not an
automated gate:

1. **Headless sanity (optional, noisy):** `dotnet build NinjaTrader.Custom.csproj -p:UseWPF=false
   -p:ImportWindowsDesktopTargets=false`, then grep for `error CS` **in the files you touched**. Known
   *ghosts to ignore*: `Indicators\Energy.cs` `CS0104` ×8, `@@AlightenGEXViewer…` `CS0234` ×2. The csproj is
   often stale (omits recent files) — add your edited files explicitly if you rely on it.
2. **Authoritative:** open the NinjaScript Editor and press **F5**. A fresh `bin\Custom\NinjaTrader.Custom.dll`
   is written **only on success** — that's your green light.
3. ⚠ **Editing a `.cs` while NT is running re-appends the generated region** → strip any duplicate
   `#region NinjaScript generated code` to zero before F5. And a recompile does **not** reload running
   indicator instances — restart NT to see new behavior live.

## Pull requests

1. Fork, branch from `main`.
2. Make the change; **F5-verify it compiles clean** (paste the result in the PR).
3. If you added/changed an indicator, tick every box in the compliance checklist.
4. Add yourself to `AUTHORS`; note in the PR that you release the work for open-source use.
5. Keep PRs focused — one tool or one fix.

Questions or a design idea? Open an issue first (see the templates) so we can place it on the ladder.
