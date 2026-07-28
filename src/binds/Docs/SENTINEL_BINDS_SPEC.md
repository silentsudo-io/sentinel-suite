---
tracks: [AddOns/SentinelBinds_v0_1_0.cs]
verified-against: v0.3.2
last-audited: 2026-07-26
---

# Sentinel Binds — window snapping and movable window groups

**Status (2026-07-26): BUILT, LIVE-VALIDATED, working.** Snap-on-release, named binds, and live
group-drag all confirmed on a real desktop. `nt8bridge compile` → `ok:true, errors:[]`.

Two things NinjaTrader does not do, in the spirit of Quantower's window binds:

1. **SNAP** — drag or resize any NT window and it clicks into exact alignment with the other NT
   windows and the monitor work area when you release.
2. **BINDS** — name a set of windows, **Link** it, and dragging any member carries the whole group
   live, holding relative positions.

---

## How to use it

Open from **Control Center ▸ New ▸ Sentinel Binds**. The window has four parts, top to bottom:
**SNAPPING**, the live status line, **WINDOWS (tick the members)**, and **BINDS**.

### Snapping — nothing to set up

| control | what it does |
|---|---|
| **Snap to edges on release** (checkbox) | Master on/off. Drag or resize any NT window; on release it clicks to the nearest edge of any other window or of the monitor work area. Saved to `binds.conf` immediately. |
| **Threshold** (slider, 2–40 px) | How close an edge must be before it grabs. Small = precise, large = eager. Saved on every change. |
| **Hold SHIFT while dragging** | Bypasses snapping for that gesture — for when you want a deliberate offset. |

That is the whole feature. Nothing to save, no bind required.

### Making a bind

1. Tick two or more windows in **WINDOWS (tick the members)**. Press **Refresh** if a window you just
   opened is not listed. A greyed row is maximized or minimized — it is still a legitimate member, it
   simply sits out moves until you restore it.
2. Type a name in the box beside **Save bind**. Leave it blank and you get `bind 1`, `bind 2`, …
3. Press **Save bind**. Fewer than two ticked windows is refused (`tick at least 2 windows to bind`).
   It writes `Sentinel\Binds\<name>.layout`, and the bind appears in the **BINDS** list.

### Using a bind — select it in BINDS first

All three buttons act on the **selected** bind; with nothing selected they answer `select a bind first`.

| button | what it does |
|---|---|
| **Link** | Glues the members: drag any one and the rest follow live, holding relative positions. Press it again on the same bind to **unlink** — it is a toggle, not a one-way switch. Reports `LINKED n windows`, or names any member that is `NOT OPEN`. |
| **Apply** | Moves the members back to the absolute positions stored in the file. Windows that are not open are **reported by name**, never skipped silently. |
| **Delete** | Removes the `.layout` file, unlinking first if that bind was the linked one. ⚠ **No confirmation and no undo** — see §8. |

⚠ **A link does not survive an F5 or an NT restart**, and a member that was closed and reopened needs
a re-**Link** even though its title still matches — the old HWND is dead. The *file* persists; the live
link does not.

### Reading the status line

Below SNAPPING sit two lines: a plain-English status, and a live diagnostic refreshed every 400 ms.

```
trk 9 · snap 14 · grp 0 · LINK mybind(3)
why: snapped left→right of Chart - GC 12-26
```

| field | meaning |
|---|---|
| `trk` | windows currently tracked |
| `snap` | snaps performed this session |
| `grp` | group translations performed (i.e. linked drags) |
| `LINK` | the linked bind and its member count, or `off` |
| `why` | what the last event did — **or the exception text, if something threw** |

**Read `why` first when something seems dead.** It is the field that broke the original bring-up: a
swallowed exception presents as "the feature does nothing", which fits a hundred design mistakes, while
`EX InvalidOperationException: The calling thread cannot access this object…` fits exactly one (§7).

**Diagnose** rescans for windows that were missed and writes full state to `Sentinel\sentinel.log`.
Reach for it when a window will not appear in the picker — and attach its output when reporting a bug.

### If it goes wrong

A bind can throw windows off-screen. See **§6 Recovery** — the rescue script lives outside this tool on
purpose, because the antidote must not depend on the thing that caused the problem.

---

## 0. The finding that matters beyond this tool

> **NinjaTrader runs EACH WINDOW on its own dispatcher thread.**

Reading `window.Left`, `.ActualWidth`, `.IsVisible` or `.WindowState` for a window you do not own
throws `InvalidOperationException: The calling thread cannot access this object because a different
thread owns it.` — every time, from any central loop. `Mouse.LeftButton` and `Keyboard.Modifiers` are
thread-affine for the same reason.

**Any future cross-window tool in this suite must use Win32 on the HWND.** Memory:
[[nt-is-multi-ui-threaded]].

| need | use |
|---|---|
| geometry | `GetWindowRect` / `SetWindowPos` |
| visible / minimised / maximised | `IsWindowVisible` / `IsIconic` / `IsZoomed` |
| still alive | `IsWindow` |
| caption | `GetWindowText` |
| mouse + modifiers | `GetAsyncKeyState(VK_LBUTTON / VK_SHIFT)` |
| monitor work area | `MonitorFromRect` + `GetMonitorInfo` |

WPF is legal **only** inside a window's own callback on its own thread — event subscription and
`new WindowInteropHelper(w).Handle`. Take the HWND there and work in Win32 afterwards.

⭐ Side benefit: one coordinate space. Everything is **physical pixels**, so the DIP-vs-device
conversion hazard never arises. Layout files store physical pixels.

---

## 1. Architecture

| piece | role |
|---|---|
| `W32` | the thread-agnostic Win32 surface. `Listable()` vs `Usable()` live here. |
| `BindsEngine` | window registry (HWND→Window), snap maths, the linked group, diagnostics. |
| `BindsLayout` | `.layout` file read/write, capture, apply, config. |
| `SentinelBindsAddOn` | `AddOnBase` — attaches windows, adds the Control Center ▸ New menu item. |
| `SentinelBindsWindow` | the `NTWindow` UI. |

**Identity is the window TITLE, not the HWND.** An HWND dies with the window; a caption survives a
close-and-reopen. HWNDs are the runtime handle, titles are what gets persisted.

### `Listable` vs `Usable` — deliberately different questions
- **`Listable`** = may I OFFER this as a bind member? real + visible + titled.
- **`Usable`** = may I MOVE it? also requires restored (not minimised/maximised).

Conflating them made the picker show 3 of 5 windows and read as a "3-window limit" (v0.3.2). A
maximized window is a legitimate bind member — it just sits out moves until restored.

---

## 2. Snapping — and why it is ON RELEASE

NT drags a window by assigning its position from the **absolute mouse position on every mouse-move**.
Anything written mid-drag is overwritten on the next message: we would be fighting the drag handler
and losing, which produces jitter, not magnetism.

**Release-snap is deterministic and holds.** `LocationChanged`/`SizeChanged` arm a pending snap; a
70 ms watchdog waits for `GetAsyncKeyState(VK_LBUTTON)` to go up, then snaps once.

Targets: every other `Usable` window's four edges (both *abut* — my left to your right — and *align* —
my left to your left), plus the monitor work area. Nearest edge within `Threshold` wins per axis.
**Shift suppresses snapping** — an escape hatch is mandatory or magnetism becomes an obstacle the
first time you want a deliberate offset.

⚠ The `WM_MOVING`/`WM_SIZING` hook from v0.1.0 is **kept but never fires on NT windows** (custom
chrome does not enter the standard modal move loop). It remains for any window type that does.

---

## 3. Binds — and the one-writer rule

A bind is a `.layout` file used two ways: **Apply** restores absolute positions; **Link** glues the
members so dragging one moves all.

⭐ **Live group-drag works although live snapping could not**, and the distinction is the whole design
insight: snapping had to rewrite the window NT was *actively dragging*; a bind moves the **other**
members, which nothing else is writing to. No contention, so it tracks in real time.

### 🔴 THE DRIVER LATCH — do not remove
v0.3.0 shipped without it and **threw the windows off the monitor on the first drag**. Moving the
other members raises *their* `LocationChanged`; they are members too, so each re-broadcast the same
delta to everyone including the dragged window. One gesture produced **83 group translations** and the
windows ended at `-32768, 32767` — `short.MinValue`/`short.MaxValue`, i.e. it overflowed the 16-bit
coordinate range.

⛔ **v0.3.0's claim that "refreshing every last-known rect cancels the echo by ARITHMETIC" is WRONG and
is retracted.** The echoes arrive on several UI threads at arbitrary times, so there is always an
interleaving where one reads a stale rect and re-applies the delta. **No resync ordering beats a race.**

**The fix is ONE WRITER.** The first member to move under a held button becomes the `_driver`; only the
driver may translate the group until release. Every other member's event that gesture is an echo of
our own `SetWindowPos` and is ignored outright. Plus a **>4000 px single-event backstop** so a future
regression parks the group instead of launching it.

---

## 4. Files

```
Sentinel\Binds\binds.conf        enabled=true|false · threshold=<px>
Sentinel\Binds\<name>.layout     # comments
                                 title|left|top|right|bottom      (PHYSICAL pixels)
```

Apply matches on exact title first, then a `" - "`-prefix match so a chart whose caption drifted still
lands. **Unmatched windows are reported BY NAME** — a layout that half-applied silently would be worse
than one that refused.

---

## 5. Honest limits

- **Layouts arrange, they do not spawn.** Apply moves windows that are already open. NT exposes no
  supported API to open "a chart on GC with this template", and menu UI-automation is the brittle
  window-poking that has cost this project nights.
- **Link does not survive an F5 or restart.** The file persists, the live link does not. Re-Link.
- **A closed-and-reopened member needs a re-Link** — the old HWND is dead even though the title matches.
- **Duplicate titles are ambiguous.** Two charts with identical captions may bind the wrong one.
- **Maximized/minimized members sit out moves** until restored (shown greyed).
- Not a Workspaces replacement: Workspaces persist across sessions; this is within-session alignment
  and instant re-arrangement. The moment it saves indicators and templates it is a worse Workspaces.

---

## 6. Recovery

If a bind ever throws windows off-screen — including past the point where the Control Center is
reachable, so no F5 is possible:

```powershell
# report only
& "$env:USERPROFILE\Documents\NinjaTrader 8\Sentinel\tools\rescue-windows.ps1"
# move every off-screen NT window back to the primary monitor
& "$env:USERPROFILE\Documents\NinjaTrader 8\Sentinel\tools\rescue-windows.ps1" -Apply
```

Pure Win32, enumerates the NinjaTrader process's top-level windows, does not need NT's UI at all.
**Keep it outside the tool** — this is a hazard the tool can create by design, so the antidote must not
depend on the tool being usable. In-app, `Apply` on the saved bind does the same job.

---

## 7. The debugging record — kept because the method is the lesson

Four iterations. Three real bugs found and fixed that were **not** the cause. Every genuine step came
from measuring, never from reasoning about what NinjaTrader "probably" does. [[measure-dont-infer]]

| version | finding | true? | the cause? |
|---|---|---|---|
| v0.1.0 | `WM_MOVING` never fires (NT custom chrome) | ✅ | ❌ |
| v0.1.1 | `OnWindowCreated` skips already-open windows | ✅ | ❌ |
| v0.1.2 | live `_windows` dictionary mutated during enumeration | ✅ | ❌ |
| **v0.2.0** | **NT is multi-UI-threaded** | ✅ | **✅** |

**What broke the deadlock was not a better theory.** It was making the swallowed `catch` write the
exception text into the visible diagnostic. The counters said `tracked 9 · moves 0 · snaps 0` — which
fits a dozen explanations. `EX InvalidOperationException: The calling thread cannot access this
object…` fits exactly one.

⭐ **A swallowed exception in a UI path presents as "the feature does nothing", which is
indistinguishable from a hundred design mistakes. Surface the exception FIRST.**

The live diagnostic (`trk · snap · grp · LINK · why`) is a permanent part of the tool for the same
reason, and it is what caught the cascade too (`grp 83`).

---

## 8. Why this is the contributor on-ramp

Zero order risk, no market data, no `SentinelCore` seam beyond `Swallow`, no Council wiring, no bake
interaction. Someone can be productive here without understanding the corpus or anything that can lose
money. It is the recommended first issue for outside contributors.

**Open items** (good first issues):
1. Delete has **no confirmation** and no undo — rename to `<name>.layout.bak` instead of unlinking.
2. Snap to a bind's **outer bounding box**, not just to individual members.
3. Resize-snap is armed but only edge-snaps the dragged edge; **resizing a bind member could push or
   pull its neighbours** (true tiling).
4. Multi-monitor: an explicit "move bind to monitor N".
5. Duplicate-title disambiguation (fall back on position or an index suffix).
