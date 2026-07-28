# 🪟 Sentinel Binds

**Rung 0 · Beauty.** Window snapping, named layouts, and linked window groups for NinjaTrader —
the two things NT does not do, in the spirit of Quantower's window binds.

## What's here

- **SNAP** — drag or resize any NT window and it clicks into alignment with every other NT window
  and the monitor work area **on release**. Alignment is exact, so windows abut with no seam.
  Hold **Shift** to bypass.
- **BIND** — save a set of windows as a named bind and **link** them: drag any member and the rest
  follow, keeping their relative geometry.
- **LAYOUTS** — capture the arrangement of open windows under a name, re-apply it in one click.

Settings persist to `Documents\NinjaTrader 8\Sentinel\Binds\binds.conf`; layouts to
`Sentinel\Binds\<name>.layout`. **Diagnose** writes full window state to `sentinel.log`.

**[Full spec →](Docs/SENTINEL_BINDS_SPEC.md)**

## ⚠ Honest limits

- **Layouts arrange, they do not spawn.** Apply moves and resizes windows that are **already open**;
  it does not open missing ones. NT exposes no supported API to open "a chart on GC with this
  template", and driving menus by UI automation is brittle. Unmatched windows are **reported by
  name** — a layout that half-applied and said nothing would be worse than one that refused.
- **This is not a Workspaces replacement.** Workspaces persist layout *across sessions*. The gap this
  fills is *within* a session: alignment on release, and instant re-arrangement between named setups.
- **Snap happens on release**, not during the drag. NT drags by assigning position from the absolute
  mouse position on every mouse-move, so anything written mid-drag is overwritten on the next
  message. Release-snap is deterministic and holds.
- **A maximized window** is a legitimate bind member but sits out moves until restored. It shows
  greyed in the picker with a tooltip.

## Safety

**No market data. No orders. No `SentinelCore` seam. No Council wiring.** Binds only moves windows.
That isolation is deliberate — it makes this the safest component in the suite to read, fork, and
extend.

## Install

1. Copy [`../runtime/`](../runtime/) (shared runtime — once) and this folder's `AddOns/` into
   `Documents\NinjaTrader 8\bin\Custom\`.
2. Press **F5** in the NinjaScript Editor.
3. **Control Center ▸ New ▸ Sentinel Binds**.

### ⚠ Requires `SentinelCore` v1.41.0 or newer

Binds records swallowed faults through `SentinelCore.Swallow`, which arrived in **v1.41.0**. It is the
only bundle in the suite with a runtime floor — every other one compiles against any version.

That matters because **every bundle ships its own copy of `../runtime/`**. Copying an **older** bundle
over this one downgrades `SentinelCore`, and since NinjaTrader compiles all of `bin\Custom` into **one
assembly**, Binds then fails with `CS0117` and takes **your whole tree** down — not just Sentinel.

**Install the oldest bundle first and the newest last**, or simply re-copy the newest `runtime/` after
any install. Releases in this repo always carry the current runtime; a zip from elsewhere may not.

To check what you have, open `runtime/AddOns/SentinelCore_v1_0_0.cs` and look for
`public const string Version`.

## ⚠ For contributors — NT is multi-UI-threaded

**NinjaTrader runs each window on its own dispatcher thread.** Reading `window.Left`,
`.ActualWidth`, `.IsVisible` or `.WindowState` for a window you do not own throws
`InvalidOperationException` — every time, from any central loop. `new WindowInteropHelper(w).Handle`
throws off-thread too, so you cannot even get the HWND that way.

⇒ **All geometry here is Win32 on the HWND** (`GetWindowRect` / `SetWindowPos` / `IsWindowVisible` /
`IsIconic` / `IsZoomed`), which is thread-agnostic by design. The only things touched through WPF are
event subscription and the HWND lookup, both on the window's own thread inside its own callback.

v0.1.x used WPF geometry, threw on every snap attempt, and the exception was swallowed — so it
presented as *"nothing happens"* and cost three wrong theories. **If you find yourself typing
`someOtherWindow.Left`, stop.**

That history is also why this file ships a `BindsFault` fault recorder rather than a bare `catch {}`:
see the comment at the top of `AddOns/SentinelBinds_v0_1_0.cs`.

## Recovery

If a layout or a bad drag puts a window off-screen where you cannot reach it, Binds is not required
to get it back — any Win32 window-rescue utility will do, including a plain PowerShell
`SetWindowPos` call. The suite's own `rescue-windows.ps1` works even when the Control Center itself
is off-screen and no F5 is possible.

## Licence

MPL-2.0, like the rest of the suite. Original work — no ported or third-party source.
