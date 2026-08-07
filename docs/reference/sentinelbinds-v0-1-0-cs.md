---
layout: sentinel-ref
title: "SentinelBinds_v0_1_0.cs"
blurb: "AddOns / runtime · 0.1.0 · 1049 lines"
---

# SentinelBinds_v0_1_0.cs

> `bin/Custom/AddOns/SentinelBinds_v0_1_0.cs`

| | |
|---|---|
| **Family** | AddOns / runtime |
| **Version** | 0.1.0 |
| **Size** | 1049 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelBindsAddOn` |
| **Namespace** | `NinjaTrader.NinjaScript.AddOns.Sentinel` |
| **Documented by** | [SENTINEL_BINDS_SPEC](../../SENTINEL_BINDS_SPEC.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
one at https://mozilla.org/MPL/2.0/.

Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
─────────────────────────────────────────────────────────────────────────────
═════════════════════════════════════════════════════════════════════════════
 SentinelBinds — window snapping + named multi-window layouts
 File: SentinelBinds_v0_1_0.cs   ·   Version v0.3.2   ·   namespace …AddOns.Sentinel
 Spec: Docs/SENTINEL_BINDS_SPEC.md
─────────────────────────────────────────────────────────────────────────────
 WHAT THIS IS
   Two things NinjaTrader does not do, in the spirit of Quantower's window binds:
     1. SNAP — drag or resize any NT window and it clicks into alignment with every other NT window
        and the monitor work area ON RELEASE. Alignment is exact, so windows abut with no seam.
     2. LAYOUTS — capture the arrangement of open windows under a name, re-apply it in one click.

 ⚠⚠ READ THIS BEFORE CHANGING ANY GEOMETRY CODE — NT IS MULTI-UI-THREADED
   **NinjaTrader runs each window on its OWN dispatcher thread.** Reading `window.Left`,
   `.ActualWidth`, `.IsVisible` or `.WindowState` for a window you do not own throws
   `InvalidOperationException: The calling thread cannot access this object because a different
   thread owns it.` — every time, on every window, from any central loop.

   That is not a tuning problem, it makes the whole WPF-property approach unusable here. v0.1.x
   used WPF geometry, threw on every single snap attempt, and the exception was swallowed — so it
   presented as "nothing happens" and cost three wrong theories about NT's drag implementation.

   ⇒ **ALL geometry is Win32 on the HWND**: GetWindowRect / SetWindowPos / IsWindowVisible /
   IsIconic / IsZoomed / GetAsyncKeyState. Those are thread-agnostic by design. The ONLY things
   touched through WPF are event subscription and the HWND lookup, both of which happen on the
   window's own thread inside its own callback.
   **If you find yourself typing `someOtherWindow.Left`, stop.**

 COORDINATE SPACE — one space, no conversions
   Everything (snapping AND layouts) is PHYSICAL PIXELS from GetWindowRect. The DPI question that
   plagued the WPF version simply does not arise: we never mix DIPs in. Layout files therefore
   store physical pixels.

 ⚠ HONEST LIMIT — LAYOUTS ARRANGE, THEY DO NOT SPAWN
   Apply moves/resizes windows that are ALREADY OPEN; it does not open missing ones. NT exposes no
   supported API to open "a chart on GC with this template", and driving menus by UI automation is
   the brittle window-poking that has cost this project nights. Unmatched windows are REPORTED BY
   NAME — a layout that half-applied and said nothing would be worse than one that refused.

 WHY IT IS NOT A REIMPLEMENTATION OF WORKSPACES
   Workspaces persist layout ACROSS SESSIONS. The gap is WITHIN a session: alignment on release and
   instant re-arrangement between named setups. The moment this saves indicators and templates it
   has become a worse Workspaces.

 ⚠ SNAP HAPPENS ON RELEASE, not during the drag. NT drags by assigning position from the absolute
   mouse position every mouse-move, so anything written mid-drag is overwritten on the next message
   — we would be fighting the drag handler and lose. Release-snap is deterministic and holds.

 NOT A SENSOR — no market data, no orders, no SentinelCore seam, no Council wiring. That isolation
 is deliberate: it makes this the safest component in the suite for an outside contributor.

 HOW TO USE IT
   Control Center ▸ New ▸ "Sentinel Binds". Settings persist to Sentinel\Binds\binds.conf;
   layouts to Sentinel\Binds\<name>.layout. "Diagnose" writes full state to sentinel.log.

 CHANGELOG
   v0.3.2 (2026-07-26) — PICKER showed 3 of 5 windows and read as a "3-window limit" (there is none).
          Two causes, both mine: the list was built in the CONSTRUCTOR, before the window was visible,
          so IsWindowVisible filtered the Binds window out of its own list; and it reused Usable(),
          which excludes maximized windows. Split the predicate — **Listable** (offer as a member:
          real + visible + titled) vs **Usable** (may be moved: also restored). A maximized window is
          a legitimate bind member, it simply sits out moves until restored; it now shows greyed with
          a tooltip. List builds on Loaded, re-sweeps for new windows, and keeps ticks across Refresh.
   v0.3.0 (2026-07-26) — THE BIND ITSELF. A saved bind can now be LINKED: drag any member and the
          whole group travels with it, live, holding relative positions. Tick members in the WINDOWS
          list → name → Save bind → select → Link.
          ⭐ Live works here although live SNAPPING could not, and the distinction is the point:
          snapping had to rewrite the position of the window NT was actively dragging (NT recomputes
          that from the absolute mouse position every mouse-move and always wins), whereas a bind
          moves the OTHER members, which nothing else is writing to.
   v0.3.1 (2026-07-26) — THE DRIVER LATCH. v0.3.0 shipped with a cascade and it threw the windows
          clean off the monitor on the first drag: moving the other members raised THEIR
          LocationChanged, they are members too, so each re-broadcast the same delta to everyone
          including the dragged window — 83 group translations from one gesture.
          ⛔ v0.3.0's claim that "refreshing every last-known rect cancels the echo by ARITHMETIC" is
          WRONG and is retracted. The echoes arrive on three different UI threads at arbitrary times,
          so there is always an interleaving where one reads a stale rect and re-applies the delta.
          No amount of resync ordering fixes a race; the answer is ONE WRITER. The first member to
          move under a held button becomes the driver and is the only one allowed to translate the
          group until release. Plus a >4000px single-event backstop so a future regression parks the
          group instead of launching it. ⭐ Recovery if it ever happens again: select the bind, Apply.
   v0.2.0 (2026-07-26) — ALL-WIN32 GEOMETRY. Root cause of "won't snap" found by instrumenting the
          swallowed exception: NT is MULTI-UI-THREADED, so every cross-window WPF property read threw
          (see the block above). Rewrote geometry onto HWND Win32 calls; mouse/Shift state now via
          GetAsyncKeyState (WPF's Mouse/Keyboard are also thread-affine). Layout files are now
          physical pixels. Diagnosis history: v0.1.0 hooked WM_MOVING (never fires — NT custom
          chrome), v0.1.1 moved to LocationChanged + release watchdog (right idea, still WPF
          geometry), v0.1.2 fixed live-dictionary enumeration + surfaced the real exception, which
          is what finally named the cause. Every step was found by MEASURING, never by reasoning.
   v0.1.2 — .ToList() snapshots on every _windows enumeration; catch writes the exception into
          LastWhy; LocationChanged only arms while the mouse is down (749 events / 156 releases
          fired with nothing moving).
   v0.1.1 — release-snap via LocationChanged + mouse-up watchdog; AttachExisting() sweep
          (OnWindowCreated never fires for already-open windows); live diagnostic counters.
   v0.1.0 — initial: WM_MOVING/WM_SIZING hook, layouts, Shift bypass, config persistence.
```

