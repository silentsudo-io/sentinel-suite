// ─────────────────────────────────────────────────────────────────────────────
// This Source Code Form is subject to the terms of the Mozilla Public License,
// v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
// one at https://mozilla.org/MPL/2.0/.
//
// Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
// ─────────────────────────────────────────────────────────────────────────────
// ═════════════════════════════════════════════════════════════════════════════
//  SentinelBinds — window snapping + named multi-window layouts
//  File: SentinelBinds_v0_1_0.cs   ·   Version v0.3.2   ·   namespace …AddOns.Sentinel
//  Spec: Docs/SENTINEL_BINDS_SPEC.md
// ─────────────────────────────────────────────────────────────────────────────
//  WHAT THIS IS
//    Two things NinjaTrader does not do, in the spirit of Quantower's window binds:
//      1. SNAP — drag or resize any NT window and it clicks into alignment with every other NT window
//         and the monitor work area ON RELEASE. Alignment is exact, so windows abut with no seam.
//      2. LAYOUTS — capture the arrangement of open windows under a name, re-apply it in one click.
//
//  ⚠⚠ READ THIS BEFORE CHANGING ANY GEOMETRY CODE — NT IS MULTI-UI-THREADED
//    **NinjaTrader runs each window on its OWN dispatcher thread.** Reading `window.Left`,
//    `.ActualWidth`, `.IsVisible` or `.WindowState` for a window you do not own throws
//    `InvalidOperationException: The calling thread cannot access this object because a different
//    thread owns it.` — every time, on every window, from any central loop.
//
//    That is not a tuning problem, it makes the whole WPF-property approach unusable here. v0.1.x
//    used WPF geometry, threw on every single snap attempt, and the exception was swallowed — so it
//    presented as "nothing happens" and cost three wrong theories about NT's drag implementation.
//
//    ⇒ **ALL geometry is Win32 on the HWND**: GetWindowRect / SetWindowPos / IsWindowVisible /
//    IsIconic / IsZoomed / GetAsyncKeyState. Those are thread-agnostic by design. The ONLY things
//    touched through WPF are event subscription and the HWND lookup, both of which happen on the
//    window's own thread inside its own callback.
//    **If you find yourself typing `someOtherWindow.Left`, stop.**
//
//  COORDINATE SPACE — one space, no conversions
//    Everything (snapping AND layouts) is PHYSICAL PIXELS from GetWindowRect. The DPI question that
//    plagued the WPF version simply does not arise: we never mix DIPs in. Layout files therefore
//    store physical pixels.
//
//  ⚠ HONEST LIMIT — LAYOUTS ARRANGE, THEY DO NOT SPAWN
//    Apply moves/resizes windows that are ALREADY OPEN; it does not open missing ones. NT exposes no
//    supported API to open "a chart on GC with this template", and driving menus by UI automation is
//    the brittle window-poking that has cost this project nights. Unmatched windows are REPORTED BY
//    NAME — a layout that half-applied and said nothing would be worse than one that refused.
//
//  WHY IT IS NOT A REIMPLEMENTATION OF WORKSPACES
//    Workspaces persist layout ACROSS SESSIONS. The gap is WITHIN a session: alignment on release and
//    instant re-arrangement between named setups. The moment this saves indicators and templates it
//    has become a worse Workspaces.
//
//  ⚠ SNAP HAPPENS ON RELEASE, not during the drag. NT drags by assigning position from the absolute
//    mouse position every mouse-move, so anything written mid-drag is overwritten on the next message
//    — we would be fighting the drag handler and lose. Release-snap is deterministic and holds.
//
//  NOT A SENSOR — no market data, no orders, no SentinelCore seam, no Council wiring. That isolation
//  is deliberate: it makes this the safest component in the suite for an outside contributor.
//
//  HOW TO USE IT
//    Control Center ▸ New ▸ "Sentinel Binds". Settings persist to Sentinel\Binds\binds.conf;
//    layouts to Sentinel\Binds\<name>.layout. "Diagnose" writes full state to sentinel.log.
//
//  CHANGELOG
//    v0.3.2 (2026-07-26) — PICKER showed 3 of 5 windows and read as a "3-window limit" (there is none).
//           Two causes, both mine: the list was built in the CONSTRUCTOR, before the window was visible,
//           so IsWindowVisible filtered the Binds window out of its own list; and it reused Usable(),
//           which excludes maximized windows. Split the predicate — **Listable** (offer as a member:
//           real + visible + titled) vs **Usable** (may be moved: also restored). A maximized window is
//           a legitimate bind member, it simply sits out moves until restored; it now shows greyed with
//           a tooltip. List builds on Loaded, re-sweeps for new windows, and keeps ticks across Refresh.
//    v0.3.0 (2026-07-26) — THE BIND ITSELF. A saved bind can now be LINKED: drag any member and the
//           whole group travels with it, live, holding relative positions. Tick members in the WINDOWS
//           list → name → Save bind → select → Link.
//           ⭐ Live works here although live SNAPPING could not, and the distinction is the point:
//           snapping had to rewrite the position of the window NT was actively dragging (NT recomputes
//           that from the absolute mouse position every mouse-move and always wins), whereas a bind
//           moves the OTHER members, which nothing else is writing to.
//    v0.3.1 (2026-07-26) — THE DRIVER LATCH. v0.3.0 shipped with a cascade and it threw the windows
//           clean off the monitor on the first drag: moving the other members raised THEIR
//           LocationChanged, they are members too, so each re-broadcast the same delta to everyone
//           including the dragged window — 83 group translations from one gesture.
//           ⛔ v0.3.0's claim that "refreshing every last-known rect cancels the echo by ARITHMETIC" is
//           WRONG and is retracted. The echoes arrive on three different UI threads at arbitrary times,
//           so there is always an interleaving where one reads a stale rect and re-applies the delta.
//           No amount of resync ordering fixes a race; the answer is ONE WRITER. The first member to
//           move under a held button becomes the driver and is the only one allowed to translate the
//           group until release. Plus a >4000px single-event backstop so a future regression parks the
//           group instead of launching it. ⭐ Recovery if it ever happens again: select the bind, Apply.
//    v0.2.0 (2026-07-26) — ALL-WIN32 GEOMETRY. Root cause of "won't snap" found by instrumenting the
//           swallowed exception: NT is MULTI-UI-THREADED, so every cross-window WPF property read threw
//           (see the block above). Rewrote geometry onto HWND Win32 calls; mouse/Shift state now via
//           GetAsyncKeyState (WPF's Mouse/Keyboard are also thread-affine). Layout files are now
//           physical pixels. Diagnosis history: v0.1.0 hooked WM_MOVING (never fires — NT custom
//           chrome), v0.1.1 moved to LocationChanged + release watchdog (right idea, still WPF
//           geometry), v0.1.2 fixed live-dictionary enumeration + surfaced the real exception, which
//           is what finally named the cause. Every step was found by MEASURING, never by reasoning.
//    v0.1.3 — 🔴 AttachExisting() HAD NEVER ATTACHED ANYTHING. `Application.Current.Windows` is
//           thread-affine, so reading it off the Application's dispatcher threw InvalidOperationException
//           before the loop was ever entered; the OUTER catch swallowed it and the function returned 0 on
//           every call since v0.1.1. The Diagnose button reported "rescan +0", which reads as "nothing new
//           to attach" rather than "this has never worked".
//           ⭐ A FAIL-OPEN PATH THAT RETURNS ZERO IS INDISTINGUISHABLE FROM SUCCESS — same shape as a
//           crashed sensor abstaining silently. The count was there; nothing ever asked whether 0 was right.
//           Fix: marshal to the app dispatcher for the collection, attach each window on ITS OWN dispatcher
//           (NT is multi-UI-threaded), bound the foreign-dispatcher wait at 500ms so it cannot deadlock a
//           UI thread, and lock-guard _windows because attach now runs on many threads.
//           ⭐ AND THE SWEEP NOW LOGS WHAT IT DID ("attached N of M; tracking K"). The defect hid
//           for weeks because the only output was a return value nobody printed — fixing the throw
//           without fixing the silence would leave the NEXT failure just as invisible.
//    v0.1.2 — .ToList() snapshots on every _windows enumeration; catch writes the exception into
//           LastWhy; LocationChanged only arms while the mouse is down (749 events / 156 releases
//           fired with nothing moving).
//    v0.1.1 — release-snap via LocationChanged + mouse-up watchdog; AttachExisting() sweep
//           (OnWindowCreated never fires for already-open windows); live diagnostic counters.
//    v0.1.0 — initial: WM_MOVING/WM_SIZING hook, layouts, Shift bypass, config persistence.
// ═════════════════════════════════════════════════════════════════════════════

#region Using declarations
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.AddOns.Sentinel;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.Sentinel
{
    // ═════════════════════════════════════════════════════════════════════════
    //  Win32 surface. Every call here is thread-agnostic — that is the entire reason it exists.
    // ═════════════════════════════════════════════════════════════════════════
    internal static class W32
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }

        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] internal static extern bool IsZoomed(IntPtr h);
        [DllImport("user32.dll")] internal static extern IntPtr MonitorFromRect(ref RECT r, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] internal static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO mi);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(IntPtr h, StringBuilder s, int max);
        [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int vKey);

        internal const uint SWP_NOSIZE = 0x0001, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
        internal const uint MONITOR_DEFAULTTONEAREST = 2;
        internal const int VK_LBUTTON = 0x01, VK_SHIFT = 0x10;

        internal static bool LeftMouseDown { get { return (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0; } }
        internal static bool ShiftDown     { get { return (GetAsyncKeyState(VK_SHIFT)   & 0x8000) != 0; } }

        internal static string TitleOf(IntPtr h)
        {
            try
            {
                var sb = new StringBuilder(320);
                int n = GetWindowText(h, sb, sb.Capacity);
                return n > 0 ? sb.ToString() : "";
            }
            catch (Exception ex) { SentinelCore.Swallow("SentinelBinds.TitleOf", ex); return ""; }
        }

        /// <summary>Worth OFFERING as a bind member: a real, visible, titled window. Deliberately
        /// laxer than Usable — a maximized window is a legitimate thing to want in a bind, it just
        /// cannot be moved until it is restored. Filtering it out of the PICKER made the tool look
        /// like it had a 3-window limit when it has none.</summary>
        internal static bool Listable(IntPtr h)
        {
            try { return h != IntPtr.Zero && IsWindow(h) && IsWindowVisible(h) && TitleOf(h).Length > 0; }
            catch (Exception ex) { SentinelCore.Swallow("SentinelBinds.Listable", ex); return false; }
        }

        /// <summary>Usable for MOVING: a real, visible, restored top-level window.</summary>
        internal static bool Usable(IntPtr h)
        {
            try { return h != IntPtr.Zero && IsWindow(h) && IsWindowVisible(h) && !IsIconic(h) && !IsZoomed(h); }
            catch (Exception ex) { SentinelCore.Swallow("SentinelBinds.Usable", ex); return false; }
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  The snapping engine. One window manager, so static.
    // ═════════════════════════════════════════════════════════════════════════
    internal static class BindsEngine
    {
        // HWND is the identity throughout. A Window reference is kept ONLY to unsubscribe on detach —
        // it is never dereferenced for geometry (see the multi-thread warning in the file header).
        private static readonly Dictionary<IntPtr, Window> _windows = new Dictionary<IntPtr, Window>();
        // ⛔ _windows is now written from SEVERAL dispatcher threads (see AttachExisting): NT gives
        //    each window its own UI thread, so attach/detach no longer happen on one. A plain
        //    Dictionary corrupts under concurrent write+read — the .ToList() snapshots added in
        //    v0.1.2 narrow that window but do not close it, because the snapshot itself walks the
        //    buckets. Every access now takes _winLock.
        private static readonly object _winLock = new object();
        // The first sweep of the process ALWAYS reports, even a zero. See v0.1.3: a silent zero here
        // read as "nothing to do" for weeks while the sweep was in fact throwing on its first line.
        private static bool _sweptOnce;

        internal static bool Enabled = true;
        internal static int  Threshold = 12;      // physical pixels

        internal static long LocEvents, Releases, Snaps;
        internal static string LastWhy = "-";

        internal static int Count { get { lock (_winLock) return _windows.Count; } }
        internal static List<IntPtr> Handles { get { lock (_winLock) return _windows.Keys.ToList(); } }

        // ═════════════════════════════════════════════════════════════════
        //  THE BIND — a named set of windows glued into one movable unit.
        //
        //  ⭐ WHY THIS CAN BE LIVE WHEN SNAPPING COULD NOT: snapping had to rewrite the position of the
        //  window NT was actively dragging, and NT recomputes that from the absolute mouse position on
        //  every mouse-move, so it always won. Here we move the OTHER members — nothing else is writing
        //  to them, so there is no fight and the group tracks the drag in real time.
        //
        //  Feedback is self-cancelling rather than flag-guarded: right after moving the others we
        //  refresh EVERY member's last-known rect, so the LocationChanged those moves raise (on their
        //  own threads, arriving whenever they like) computes a zero delta and does nothing. A bool
        //  guard would race against those late cross-thread events; arithmetic cannot.
        // ═════════════════════════════════════════════════════════════════
        internal static string LinkedName = null;
        private static readonly List<IntPtr> _group = new List<IntPtr>();
        private static readonly Dictionary<IntPtr, W32.RECT> _last = new Dictionary<IntPtr, W32.RECT>();

        internal static int GroupCount { get { return _group.Count; } }
        internal static bool IsLinked { get { return LinkedName != null && _group.Count > 1; } }

        /// <summary>Glue the windows whose titles match. Returns (linked, missing titles).</summary>
        internal static Tuple<int, List<string>> Link(string name, List<string> titles)
        {
            Unlink();
            var missing = new List<string>();
            var live = Handles.Where(W32.Usable).ToList();
            var used = new HashSet<IntPtr>();
            foreach (var t in titles)
            {
                IntPtr hit = IntPtr.Zero;
                foreach (var h in live)
                    if (!used.Contains(h) && W32.TitleOf(h) == t) { hit = h; break; }
                if (hit == IntPtr.Zero) { missing.Add(t); continue; }
                used.Add(hit);
                _group.Add(hit);
            }
            RefreshLast();
            LinkedName = _group.Count > 1 ? name : null;
            return Tuple.Create(_group.Count, missing);
        }

        internal static void Unlink()
        {
            _group.Clear(); _last.Clear(); LinkedName = null; _driver = IntPtr.Zero;
        }

        // ⚠ THE DRIVER LATCH — do not remove, this is what stops the group flying off the screen.
        //
        // Moving the other members raises THEIR LocationChanged. They are group members too, so each
        // one re-broadcast the same delta to everybody else, including the window being dragged, and
        // the whole group accelerated away: one drag produced 83 group translations and the windows
        // left the monitor.
        //
        // Refreshing last-known rects was meant to zero those echoes out, but it CANNOT — the echoes
        // arrive on three different UI threads whenever they like, so there is always an interleaving
        // where an echo reads a stale rect and re-applies the delta. The fix is not tighter timing, it
        // is ONE writer: the first window to move while the button is down becomes the driver, and
        // ONLY the driver may translate the group until the mouse is released.
        private static IntPtr _driver;

        private static W32.RECT Rect(IntPtr h)
        {
            W32.RECT r;
            W32.GetWindowRect(h, out r);
            return r;
        }

        private static void RefreshLast()
        {
            foreach (var h in _group)
            {
                W32.RECT r;
                if (W32.GetWindowRect(h, out r)) _last[h] = r;
            }
        }

        /// <summary>One member moved — carry the rest with it.</summary>
        private static void TranslateGroup(IntPtr moved)
        {
            try
            {
                W32.RECT now;
                if (!W32.GetWindowRect(moved, out now)) return;
                W32.RECT was;
                if (!_last.TryGetValue(moved, out was)) { _last[moved] = now; return; }

                int dx = now.Left - was.Left, dy = now.Top - was.Top;
                if (dx == 0 && dy == 0) return;          // includes every echo of our own writes

                // Backstop. With the driver latch this should never fire; if a future change lets the
                // echo loop back in, this stops the group leaving the desktop instead of letting it.
                if (Math.Abs(dx) > 4000 || Math.Abs(dy) > 4000)
                {
                    LastWhy = "runaway delta " + dx + "/" + dy + " — ignored";
                    RefreshLast();
                    return;
                }

                foreach (var h in _group.ToList())
                {
                    if (h == moved || !W32.Usable(h)) continue;
                    W32.RECT r;
                    if (!W32.GetWindowRect(h, out r)) continue;
                    W32.SetWindowPos(h, IntPtr.Zero, r.Left + dx, r.Top + dy, 0, 0,
                        W32.SWP_NOSIZE | W32.SWP_NOZORDER | W32.SWP_NOACTIVATE);
                }
                RefreshLast();                            // makes the echoes compute zero
                GroupMoves++;
            }
            catch (Exception ex)
            {
                LastWhy = "EX(group) " + ex.GetType().Name + ": " + ex.Message;
                SentinelCore.Swallow("SentinelBinds.TranslateGroup", ex);
            }
        }

        internal static long GroupMoves;

        private static IntPtr _pending;
        private static bool   _applying;
        private static System.Windows.Threading.DispatcherTimer _watchdog;

        // ── attach / detach ──────────────────────────────────────────────────
        internal static void Attach(Window w)
        {
            if (w == null) return;
            try
            {
                var h = new WindowInteropHelper(w).Handle;
                if (h == IntPtr.Zero) { w.SourceInitialized += OnSourceInit; return; }
                Hook(w, h);
            }
            catch (Exception ex) { SentinelCore.Swallow("SentinelBinds.Attach", ex); }
        }

        private static void OnSourceInit(object sender, EventArgs e)
        {
            var w = sender as Window;
            if (w == null) return;
            w.SourceInitialized -= OnSourceInit;
            try { Hook(w, new WindowInteropHelper(w).Handle); }
            catch (Exception ex) { SentinelCore.Swallow("SentinelBinds.SourceInit", ex); }
        }

        private static void Hook(Window w, IntPtr h)
        {
            if (h == IntPtr.Zero) return;
            lock (_winLock)
            {
                if (_windows.ContainsKey(h)) return;
                _windows[h] = w;
            }
            // These fire ON THE WINDOW'S OWN THREAD, which is the one place WPF access here is legal.
            w.LocationChanged += OnMoved;
            w.SizeChanged     += OnResized;
            EnsureWatchdog();
        }

        internal static void Detach(Window w)
        {
            if (w == null) return;
            try
            {
                w.LocationChanged -= OnMoved;
                w.SizeChanged     -= OnResized;
            }
            catch (Exception ex) { SentinelCore.Swallow("SentinelBinds.Detach.events", ex); }
            try
            {
                IntPtr found = IntPtr.Zero;
                foreach (var kv in SnapshotPairs())
                    if (ReferenceEquals(kv.Value, w)) { found = kv.Key; break; }
                if (found != IntPtr.Zero)
                {
                    lock (_winLock) { _windows.Remove(found); }
                    _group.Remove(found); _last.Remove(found);
                }
                if (_pending == found) _pending = IntPtr.Zero;
            }
            catch (Exception ex) { SentinelCore.Swallow("SentinelBinds.Detach", ex); }
        }

        /// <summary>Hook windows that are ALREADY open. `OnWindowCreated` only fires for windows created
        /// after the AddOn loads, so without this an F5 leaves the engine tracking almost nothing.</summary>
        internal static int AttachExisting()
        {
            try
            {
                Application app = Application.Current;
                if (app == null) return 0;

                // ⛔ THREAD AFFINITY IS THE WHOLE BUG (fixed v0.1.3, 2026-08-11).
                //    `Application.Current.Windows` is thread-affine: read from any thread but the
                //    Application's own it throws InvalidOperationException BEFORE the loop is entered.
                //    That is why every fault logged here was the OUTER catch and the returned count was
                //    always 0 — this sweep had never attached a single window since v0.1.1.
                if (!app.Dispatcher.CheckAccess())
                    return (int)app.Dispatcher.Invoke(new Func<int>(AttachExisting));

                List<Window> snapshot = new List<Window>();
                foreach (Window w in app.Windows) snapshot.Add(w);   // legal: we are on the app thread

                int n = 0;
                foreach (Window w in snapshot)
                {
                    Window win = w;
                    try
                    {
                        // ⛔ NT IS MULTI-UI-THREADED — each window may own a DIFFERENT dispatcher, and
                        //    BOTH WindowInteropHelper(w).Handle and Hook()'s event subscriptions are
                        //    thread-affine. The comment that stood here claimed taking the HWND was safe
                        //    "because we only take the HWND, never a geometry property". That reasoning
                        //    was wrong: the WINDOW is the affine object, not the property.
                        if (win.Dispatcher.CheckAccess())
                        {
                            if (AttachOne(win)) n++;
                        }
                        else
                        {
                            // Bounded wait, deliberately. A blocking Invoke onto a foreign dispatcher
                            // deadlocks if that thread is itself waiting on this one — and this runs on
                            // a UI thread. A window missed this sweep is picked up by the next one; a
                            // frozen NT is not recoverable.
                            DispatcherOperation<bool> op = win.Dispatcher.InvokeAsync<bool>(() => AttachOne(win));
                            if (op.Task.Wait(500)) { if (op.Task.Result) n++; }
                            else SentinelCore.Log("Binds", "AttachExisting: a window's dispatcher did not "
                                + "answer in 500ms — SKIPPED this sweep (not attached). It is retried next sweep.");
                        }
                    }
                    catch (Exception ex) { SentinelCore.Swallow("SentinelBinds.AttachExisting.one", ex); }
                }

                // ⭐ SAY WHAT THE SWEEP DID. The v0.1.3 bug survived because this function's only
                //    output was a return value nobody printed: it reported 0 while never having run.
                //    A sweep that attaches nothing is now DISTINGUISHABLE from one that never ran.
                if (n > 0 || !_sweptOnce)
                {
                    _sweptOnce = true;
                    SentinelCore.Log("Binds", "sweep: attached " + n + " of " + snapshot.Count
                        + " open window(s); now tracking " + Count + ".");
                }
                return n;
            }
            catch (Exception ex) { SentinelCore.Swallow("SentinelBinds.AttachExisting", ex); return 0; }
        }

        /// <summary>Attach ONE window. ⛔ MUST run on that window's OWN dispatcher — every WPF touch
        /// below is thread-affine. True if this call newly tracked (or deferred) the window.</summary>
        private static bool AttachOne(Window w)
        {
            IntPtr h = new WindowInteropHelper(w).Handle;
            if (h == IntPtr.Zero) { Attach(w); return true; }   // no HWND yet -> defers to SourceInitialized
            lock (_winLock) { if (_windows.ContainsKey(h)) return false; }
            Hook(w, h);
            return true;
        }

        /// <summary>Locked snapshot of the tracked pairs.</summary>
        private static List<KeyValuePair<IntPtr, Window>> SnapshotPairs()
        {
            lock (_winLock) return _windows.ToList();
        }

        // ── drag detection ───────────────────────────────────────────────────
        private static void EnsureWatchdog()
        {
            if (_watchdog != null) return;
            _watchdog = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(70) };
            _watchdog.Tick += (s, e) =>
            {
                if (_pending == IntPtr.Zero) return;
                if (W32.LeftMouseDown) return;                 // still dragging
                _driver = IntPtr.Zero;                         // gesture over — next drag re-elects
                RefreshLast();                                 // resync before the next gesture
                var h = _pending; _pending = IntPtr.Zero;
                Releases++;
                SnapOnRelease(h);
            };
            _watchdog.Start();
        }

        private static void Arm(object sender)
        {
            if (!Enabled || _applying) return;
            if (W32.ShiftDown) { _pending = IntPtr.Zero; return; }
            LocEvents++;
            // Only a real drag arms a snap: NT fires LocationChanged constantly with nothing moving
            // (measured 749 events / 156 releases, every window's position unchanged).
            if (!W32.LeftMouseDown) return;
            var w = sender as Window;
            if (w == null) return;
            try
            {
                var h = new WindowInteropHelper(w).Handle;   // own thread — legal
                if (h != IntPtr.Zero) _pending = h;
            }
            catch (Exception ex) { SentinelCore.Swallow("SentinelBinds.Arm", ex); }
        }

        private static void OnMoved(object sender, EventArgs e)
        {
            // Group drag runs BEFORE the snap arming and independently of it: a bind should carry its
            // members whether or not the drag ends anywhere near a snap target.
            if (_group.Count > 1 && W32.LeftMouseDown)
            {
                try
                {
                    var w = sender as Window;
                    if (w != null)
                    {
                        var h = new WindowInteropHelper(w).Handle;   // own thread — legal
                        if (h != IntPtr.Zero && _group.Contains(h))
                        {
                            // ONE WRITER. First member to move under a held button owns the drag; every
                            // other member's event this gesture is an echo of our own SetWindowPos and is
                            // ignored outright. Cleared on release by the watchdog.
                            if (_driver == IntPtr.Zero) { _driver = h; _last[h] = Rect(h); }
                            if (_driver == h) TranslateGroup(h);
                        }
                    }
                }
                catch (Exception ex) { SentinelCore.Swallow("SentinelBinds.OnMoved.group", ex); }
            }
            Arm(sender);
        }
        private static void OnResized(object sender, SizeChangedEventArgs e) { Arm(sender); }

        // ── the snap ─────────────────────────────────────────────────────────
        private static void SnapOnRelease(IntPtr self)
        {
            try
            {
                if (!W32.Usable(self)) { LastWhy = "self not usable (min/max/hidden)"; return; }

                W32.RECT me;
                if (!W32.GetWindowRect(self, out me)) { LastWhy = "GetWindowRect failed"; return; }

                var xs = new List<int>();   // where my LEFT may land
                var xe = new List<int>();   // where my RIGHT may land
                var ys = new List<int>();
                var ye = new List<int>();

                foreach (var h in Handles)                    // locked snapshot: the dict mutates under us
                {
                    if (h == self || !W32.Usable(h)) continue;
                    W32.RECT o;
                    if (!W32.GetWindowRect(h, out o)) continue;
                    xs.Add(o.Right); xs.Add(o.Left);           // abut their right, or align lefts
                    xe.Add(o.Left);  xe.Add(o.Right);
                    ys.Add(o.Bottom); ys.Add(o.Top);
                    ye.Add(o.Top);    ye.Add(o.Bottom);
                }

                var mi = new W32.MONITORINFO { cbSize = Marshal.SizeOf(typeof(W32.MONITORINFO)) };
                IntPtr mon = W32.MonitorFromRect(ref me, W32.MONITOR_DEFAULTTONEAREST);
                if (mon != IntPtr.Zero && W32.GetMonitorInfo(mon, ref mi))
                {
                    xs.Add(mi.rcWork.Left); xe.Add(mi.rcWork.Right);
                    ys.Add(mi.rcWork.Top);  ye.Add(mi.rcWork.Bottom);
                }

                int dl, dr, dt, db;
                bool okL = Best(me.Left, xs, out dl), okR = Best(me.Right, xe, out dr);
                bool okT = Best(me.Top, ys, out dt),  okB = Best(me.Bottom, ye, out db);

                int dx = okL && (!okR || Math.Abs(dl) <= Math.Abs(dr)) ? dl : (okR ? dr : 0);
                int dy = okT && (!okB || Math.Abs(dt) <= Math.Abs(db)) ? dt : (okB ? db : 0);

                if (dx == 0 && dy == 0)
                {
                    int near = int.MaxValue;
                    foreach (var v in xs) near = Math.Min(near, Math.Abs(v - me.Left));
                    foreach (var v in xe) near = Math.Min(near, Math.Abs(v - me.Right));
                    foreach (var v in ys) near = Math.Min(near, Math.Abs(v - me.Top));
                    foreach (var v in ye) near = Math.Min(near, Math.Abs(v - me.Bottom));
                    LastWhy = xs.Count == 0 ? "no targets"
                        : string.Format(CultureInfo.InvariantCulture, "nearest {0}px > {1}", near, Threshold);
                    return;
                }

                _applying = true;
                try
                {
                    W32.SetWindowPos(self, IntPtr.Zero, me.Left + dx, me.Top + dy, 0, 0,
                        W32.SWP_NOSIZE | W32.SWP_NOZORDER | W32.SWP_NOACTIVATE);
                }
                finally { _applying = false; }

                Snaps++;
                LastWhy = string.Format(CultureInfo.InvariantCulture, "snapped {0:+0;-0;0}/{1:+0;-0;0}", dx, dy);
            }
            catch (Exception ex)
            {
                // A blank diagnostic on the one case you most need it is worse than none — this is what
                // finally named the multi-thread cause.
                LastWhy = "EX " + ex.GetType().Name + ": " + ex.Message;
                SentinelCore.Swallow("SentinelBinds.SnapOnRelease", ex);
            }
        }

        private static bool Best(int value, List<int> targets, out int delta)
        {
            delta = 0;
            int bestAbs = Threshold + 1;
            foreach (int t in targets)
            {
                int d = t - value, a = Math.Abs(d);
                if (a < bestAbs) { bestAbs = a; delta = d; }
            }
            return bestAbs <= Threshold;
        }

        internal static void LogState(string tag)
        {
            try
            {
                SentinelCore.Log("Binds", string.Format(CultureInfo.InvariantCulture,
                    "{0} tracked={1} loc={2} rel={3} snap={4} enabled={5} thr={6} watchdog={7} why={8}",
                    tag, Count, LocEvents, Releases, Snaps, Enabled, Threshold,
                    _watchdog != null && _watchdog.IsEnabled, LastWhy));

                foreach (var h in Handles)
                {
                    try
                    {
                        W32.RECT r;
                        bool got = W32.GetWindowRect(h, out r);
                        SentinelCore.Log("Binds", string.Format(CultureInfo.InvariantCulture,
                            "   hwnd {0} vis={1,-5} min={2,-5} max={3,-5} L={4,6} T={5,6} R={6,6} B={7,6} '{8}'",
                            h.ToInt64(), W32.IsWindowVisible(h), W32.IsIconic(h), W32.IsZoomed(h),
                            got ? r.Left : 0, got ? r.Top : 0, got ? r.Right : 0, got ? r.Bottom : 0,
                            W32.TitleOf(h)));
                    }
                    catch (Exception ex) { SentinelCore.Swallow("SentinelBinds.LogState.win", ex); }
                }
            }
            catch (Exception ex) { SentinelCore.Swallow("SentinelBinds.LogState", ex); }
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Layouts — physical pixels, Win32 only (same thread rule as the engine).
    // ═════════════════════════════════════════════════════════════════════════
    internal static class BindsLayout
    {
        internal static string Dir
        {
            get
            {
                string d = Path.Combine(SentinelCore.SettingsDir, "Binds");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        internal static List<string> Names()
        {
            var l = new List<string>();
            try { foreach (var f in Directory.GetFiles(Dir, "*.layout")) l.Add(Path.GetFileNameWithoutExtension(f)); }
            catch (Exception ex) { SentinelCore.Swallow("SentinelBinds.Names", ex); }
            l.Sort();
            return l;
        }

        /// <summary>Save a bind from an explicit member list — the windows the user ticked.
        /// Order is preserved so the first ticked window reads as the group's anchor in the file.</summary>
        internal static int CaptureTitles(string name, List<string> titles)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Sentinel Binds bind · " + DateTime.Now.ToString("s", CultureInfo.InvariantCulture));
            sb.AppendLine("# title|left|top|right|bottom   (PHYSICAL pixels)");
            int n = 0;
            var used = new HashSet<IntPtr>();
            foreach (var t in titles)
            {
                try
                {
                    IntPtr hit = IntPtr.Zero;
                    foreach (var h in BindsEngine.Handles)
                        if (!used.Contains(h) && W32.Usable(h) && W32.TitleOf(h) == t) { hit = h; break; }
                    if (hit == IntPtr.Zero) continue;
                    used.Add(hit);
                    W32.RECT r;
                    if (!W32.GetWindowRect(hit, out r)) continue;
                    sb.Append(t.Replace('|', '/')).Append('|')
                      .Append(r.Left).Append('|').Append(r.Top).Append('|')
                      .Append(r.Right).Append('|').Append(r.Bottom).AppendLine();
                    n++;
                }
                catch (Exception ex) { SentinelCore.Swallow("SentinelBinds.CaptureTitles", ex); }
            }
            File.WriteAllText(Path.Combine(Dir, Safe(name) + ".layout"), sb.ToString());
            return n;
        }

        internal static int Capture(string name)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Sentinel Binds layout · " + DateTime.Now.ToString("s", CultureInfo.InvariantCulture));
            sb.AppendLine("# title|left|top|right|bottom   (PHYSICAL pixels)");
            int n = 0;
            foreach (var h in BindsEngine.Handles)
            {
                try
                {
                    if (!W32.Usable(h)) continue;
                    W32.RECT r;
                    if (!W32.GetWindowRect(h, out r)) continue;
                    string t = W32.TitleOf(h);
                    if (string.IsNullOrEmpty(t)) continue;
                    sb.Append(t.Replace('|', '/')).Append('|')
                      .Append(r.Left).Append('|').Append(r.Top).Append('|')
                      .Append(r.Right).Append('|').Append(r.Bottom).AppendLine();
                    n++;
                }
                catch (Exception ex) { SentinelCore.Swallow("SentinelBinds.Capture", ex); }
            }
            File.WriteAllText(Path.Combine(Dir, Safe(name) + ".layout"), sb.ToString());
            return n;
        }

        /// <summary>Returns (applied, unmatched titles). Unmatched are NAMED, never swallowed.</summary>
        internal static Tuple<int, List<string>> Apply(string name)
        {
            var unmatched = new List<string>();
            int applied = 0;
            string p = Path.Combine(Dir, Safe(name) + ".layout");
            if (!File.Exists(p)) return Tuple.Create(0, unmatched);

            var live = BindsEngine.Handles.Where(W32.Usable).ToList();
            var used = new HashSet<IntPtr>();

            foreach (var line in File.ReadAllLines(p))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                var f = line.Split('|');
                if (f.Length < 5) continue;
                string title = f[0];
                int l = I(f[1]), t = I(f[2]), r = I(f[3]), b = I(f[4]);

                IntPtr hit = IntPtr.Zero;
                foreach (var h in live)
                    if (!used.Contains(h) && W32.TitleOf(h) == title) { hit = h; break; }
                if (hit == IntPtr.Zero)     // fall back to a prefix match — chart captions drift
                    foreach (var h in live)
                    {
                        var ttl = W32.TitleOf(h);
                        if (!used.Contains(h) && ttl.Length > 0 && title.Length > 0
                            && (ttl.StartsWith(Head(title)) || title.StartsWith(Head(ttl)))) { hit = h; break; }
                    }
                if (hit == IntPtr.Zero) { unmatched.Add(title); continue; }

                used.Add(hit);
                try
                {
                    W32.SetWindowPos(hit, IntPtr.Zero, l, t, Math.Max(1, r - l), Math.Max(1, b - t),
                        W32.SWP_NOZORDER | W32.SWP_NOACTIVATE);
                    applied++;
                }
                catch (Exception ex) { SentinelCore.Swallow("SentinelBinds.Apply", ex); unmatched.Add(title); }
            }
            return Tuple.Create(applied, unmatched);
        }

        /// <summary>The window titles a saved bind refers to, in file order.</summary>
        internal static List<string> Members(string name)
        {
            var l = new List<string>();
            try
            {
                string p = Path.Combine(Dir, Safe(name) + ".layout");
                if (!File.Exists(p)) return l;
                foreach (var line in File.ReadAllLines(p))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    var f = line.Split('|');
                    if (f.Length >= 5 && f[0].Length > 0) l.Add(f[0]);
                }
            }
            catch (Exception ex) { SentinelCore.Swallow("SentinelBinds.Members", ex); }
            return l;
        }

        internal static void Delete(string name)
        {
            try
            {
                string p = Path.Combine(Dir, Safe(name) + ".layout");
                if (File.Exists(p)) File.Delete(p);
            }
            catch (Exception ex) { SentinelCore.Swallow("SentinelBinds.Delete", ex); }
        }

        internal static void LoadConf()
        {
            try
            {
                string p = Path.Combine(Dir, "binds.conf");
                if (!File.Exists(p)) return;
                foreach (var line in File.ReadAllLines(p))
                {
                    var kv = line.Split('=');
                    if (kv.Length != 2) continue;
                    if (kv[0].Trim() == "enabled") BindsEngine.Enabled = kv[1].Trim() == "true";
                    if (kv[0].Trim() == "threshold")
                    { int t; if (int.TryParse(kv[1].Trim(), out t)) BindsEngine.Threshold = Math.Max(1, Math.Min(60, t)); }
                }
            }
            catch (Exception ex) { SentinelCore.Swallow("SentinelBinds.LoadConf", ex); }
        }

        internal static void SaveConf()
        {
            try
            {
                File.WriteAllText(Path.Combine(Dir, "binds.conf"),
                    "enabled=" + (BindsEngine.Enabled ? "true" : "false") + Environment.NewLine +
                    "threshold=" + BindsEngine.Threshold.ToString(CultureInfo.InvariantCulture) + Environment.NewLine);
            }
            catch (Exception ex) { SentinelCore.Swallow("SentinelBinds.SaveConf", ex); }
        }

        private static string Head(string s) { int i = s.IndexOf(" - "); return i > 0 ? s.Substring(0, i + 3) : s; }
        private static int I(string s) { int v; return int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out v) ? v : 0; }
        private static string Safe(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "layout";
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Trim();
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  AddOn registration — Control Center ▸ New ▸ "Sentinel Binds"
    // ═════════════════════════════════════════════════════════════════════════
    public class SentinelBindsAddOn : NinjaTrader.NinjaScript.AddOnBase
    {
        private NTMenuItem _menuItem;
        private NTMenuItem _newMenu;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "SentinelBinds";
                Description = "Sentinel Binds — window snapping + named multi-window layouts (Control Center ▸ New).";
                BindsLayout.LoadConf();
            }
        }

        protected override void OnWindowCreated(Window window)
        {
            BindsEngine.Attach(window);
            BindsEngine.AttachExisting();     // sweep up anything that predates the AddOn

            ControlCenter cc = window as ControlCenter;
            if (cc == null || _menuItem != null) return;

            _newMenu = cc.FindFirst("ControlCenterMenuItemNew") as NTMenuItem;
            if (_newMenu == null) return;

            _menuItem = new NTMenuItem
            {
                Header = "Sentinel Binds",
                Style  = Application.Current.TryFindResource("MainMenuItem") as Style
            };
            _menuItem.Click += OnMenuClick;
            _newMenu.Items.Add(_menuItem);
        }

        protected override void OnWindowDestroyed(Window window)
        {
            BindsEngine.Detach(window);

            if (_menuItem != null && window is ControlCenter)
            {
                if (_newMenu != null && _newMenu.Items.Contains(_menuItem))
                    _newMenu.Items.Remove(_menuItem);
                _menuItem.Click -= OnMenuClick;
                _menuItem = null;
                _newMenu  = null;
            }
        }

        private void OnMenuClick(object sender, RoutedEventArgs e)
        {
            NinjaTrader.Core.Globals.RandomDispatcher.InvokeAsync(new Action(() =>
            {
                var w = new SentinelBindsWindow();
                w.Show();
                w.Activate();
            }));
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  The Binds window.
    // ═════════════════════════════════════════════════════════════════════════
    public class SentinelBindsWindow : NTWindow
    {
        private static readonly Brush Bg     = new SolidColorBrush(Color.FromRgb(0x0A, 0x0E, 0x17));
        private static readonly Brush Card   = new SolidColorBrush(Color.FromRgb(0x11, 0x17, 0x26));
        private static readonly Brush Ink    = new SolidColorBrush(Color.FromRgb(0xE6, 0xED, 0xF7));
        private static readonly Brush Mute   = new SolidColorBrush(Color.FromRgb(0x6B, 0x7A, 0x90));
        private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0x2D, 0xD4, 0xE8));
        private static readonly Brush Line   = new SolidColorBrush(Color.FromRgb(0x1E, 0x2A, 0x3D));

        private ListBox   _list;
        private ListBox   _wins;
        private TextBox   _name;
        private TextBlock _status;
        private TextBlock _diag;
        private System.Windows.Threading.DispatcherTimer _timer;

        public SentinelBindsWindow()
        {
            Caption = "Sentinel Binds";
            Width = 380; Height = 640;
            Background = Bg;

            var root = new Grid { Margin = new Thickness(12) };
            for (int i = 0; i < 7; i++) root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions[2].Height = new GridLength(1.2, GridUnitType.Star);   // WINDOWS picker
            root.RowDefinitions[4].Height = new GridLength(1, GridUnitType.Star);     // BINDS

            // ── snapping ──
            var snapBox = Section("SNAPPING");
            var sp = new StackPanel { Margin = new Thickness(10, 26, 10, 10) };
            var enabled = new CheckBox { Content = "Snap to edges on release", Foreground = Ink,
                                         IsChecked = BindsEngine.Enabled, Margin = new Thickness(0, 0, 0, 8) };
            enabled.Checked   += (s, e) => { BindsEngine.Enabled = true;  BindsLayout.SaveConf(); Status("snapping on"); };
            enabled.Unchecked += (s, e) => { BindsEngine.Enabled = false; BindsLayout.SaveConf(); Status("snapping off"); };
            sp.Children.Add(enabled);
            sp.Children.Add(new TextBlock { Text = "Hold SHIFT while dragging to place freely.",
                                            Foreground = Mute, FontSize = 10.5, Margin = new Thickness(0, 0, 0, 8) });

            var tRow = new DockPanel();
            var tLab = new TextBlock { Text = "Threshold", Foreground = Mute, FontSize = 11, Width = 70, VerticalAlignment = VerticalAlignment.Center };
            var tVal = new TextBlock { Text = BindsEngine.Threshold + " px", Foreground = Accent, FontSize = 11, Width = 44,
                                       VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
            var thresh = new Slider { Minimum = 2, Maximum = 40, Value = BindsEngine.Threshold,
                                      IsSnapToTickEnabled = true, TickFrequency = 1, VerticalAlignment = VerticalAlignment.Center };
            thresh.ValueChanged += (s, e) => { BindsEngine.Threshold = (int)e.NewValue; tVal.Text = BindsEngine.Threshold + " px"; BindsLayout.SaveConf(); };
            DockPanel.SetDock(tLab, Dock.Left); DockPanel.SetDock(tVal, Dock.Right);
            tRow.Children.Add(tLab); tRow.Children.Add(tVal); tRow.Children.Add(thresh);
            sp.Children.Add(tRow);
            snapBox.Children.Add(sp);
            Grid.SetRow(snapBox, 0); root.Children.Add(snapBox);

            // ── status + live diagnostics ──
            _status = new TextBlock { Foreground = Mute, FontSize = 10.5, TextWrapping = TextWrapping.Wrap };
            _diag   = new TextBlock { Foreground = Accent, FontSize = 10.5, FontFamily = new FontFamily("Consolas"), TextWrapping = TextWrapping.Wrap };
            var diagRow = new DockPanel { Margin = new Thickness(0, 2, 0, 0) };
            var bDiag = Button("Diagnose", (s, e) =>
            {
                int n = BindsEngine.AttachExisting();
                BindsEngine.LogState("MANUAL");
                Status("rescan +" + n + " · state written to sentinel.log");
            });
            DockPanel.SetDock(bDiag, Dock.Right);
            diagRow.Children.Add(bDiag); diagRow.Children.Add(_diag);
            var statHost = new StackPanel();
            statHost.Children.Add(_status); statHost.Children.Add(diagRow);
            Grid.SetRow(statHost, 1); root.Children.Add(statHost);

            // ── WINDOWS picker: tick the members of a bind ──
            var winBox = Section("WINDOWS  (tick the members)");
            var wp = new DockPanel { Margin = new Thickness(10, 26, 10, 10) };
            _wins = new ListBox { Background = Bg, Foreground = Ink, BorderBrush = Line, MinHeight = 120 };
            wp.Children.Add(_wins);
            winBox.Children.Add(wp);
            Grid.SetRow(winBox, 2); root.Children.Add(winBox);

            // ── BINDS ──
            var layBox = Section("BINDS");
            var lp = new DockPanel { Margin = new Thickness(10, 26, 10, 10) };
            _list = new ListBox { Background = Bg, Foreground = Ink, BorderBrush = Line, MinHeight = 90 };
            lp.Children.Add(_list);
            layBox.Children.Add(lp);
            Grid.SetRow(layBox, 4); root.Children.Add(layBox);

            var cap = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            _name = new TextBox { Background = Card, Foreground = Ink, BorderBrush = Line, Padding = new Thickness(6, 3, 6, 3) };
            var bRe = Button("Refresh", (s, e) => { RefreshWindows(); Status("window list refreshed"); });
            var bCap = Button("Save bind", (s, e) =>
            {
                var picked = PickedTitles();
                if (picked.Count < 2) { Status("tick at least 2 windows to bind"); return; }
                string n = string.IsNullOrWhiteSpace(_name.Text) ? "bind " + (BindsLayout.Names().Count + 1) : _name.Text;
                int c = BindsLayout.CaptureTitles(n, picked);
                Refresh(); Status("saved \"" + n + "\" with " + c + " window(s)");
            });
            DockPanel.SetDock(bCap, Dock.Right); DockPanel.SetDock(bRe, Dock.Right);
            cap.Children.Add(bCap); cap.Children.Add(bRe); cap.Children.Add(_name);
            Grid.SetRow(cap, 3); root.Children.Add(cap);

            var act = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            act.Children.Add(Button("Link", (s, e) =>
            {
                var sel = _list.SelectedItem as string;
                if (sel == null) { Status("select a bind first"); return; }
                if (BindsEngine.IsLinked && BindsEngine.LinkedName == sel)
                { BindsEngine.Unlink(); Status("unlinked"); return; }
                var r = BindsEngine.Link(sel, BindsLayout.Members(sel));
                Status(r.Item2.Count == 0
                    ? "LINKED " + r.Item1 + " windows - drag any one, they all follow"
                    : "linked " + r.Item1 + "; NOT OPEN: " + string.Join(", ", r.Item2.Take(2)));
            }));
            act.Children.Add(Button("Apply", (s, e) =>
            {
                var sel = _list.SelectedItem as string;
                if (sel == null) { Status("select a bind first"); return; }
                var r = BindsLayout.Apply(sel);
                Status(r.Item2.Count == 0
                    ? "applied " + r.Item1 + " window(s)"
                    : "applied " + r.Item1 + "; NOT OPEN: " + string.Join(", ", r.Item2.Take(3))
                      + (r.Item2.Count > 3 ? " +" + (r.Item2.Count - 3) : ""));
            }));
            act.Children.Add(Button("Delete", (s, e) =>
            {
                var sel = _list.SelectedItem as string;
                if (sel == null) { Status("select a bind first"); return; }
                if (BindsEngine.LinkedName == sel) BindsEngine.Unlink();
                BindsLayout.Delete(sel); Refresh(); Status("deleted \"" + sel + "\"");
            }));
            Grid.SetRow(act, 6); root.Children.Add(act);

            Content = root;
            BindsEngine.AttachExisting();
            Refresh();
            // Built on Loaded, not in the constructor: an unshown window is not yet visible to
            // IsWindowVisible, so building here would silently omit this window and any other that
            // had not finished opening.
            Loaded += (s, e) => RefreshWindows();
            Status(BindsEngine.Count + " window(s) tracked");

            _timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _timer.Tick += (s, e) => _diag.Text = string.Format(CultureInfo.InvariantCulture,
                "trk {0} · snap {1} · grp {2} · LINK {3}\nwhy: {4}",
                BindsEngine.Count, BindsEngine.Snaps, BindsEngine.GroupMoves,
                BindsEngine.IsLinked ? BindsEngine.LinkedName + "(" + BindsEngine.GroupCount + ")" : "off",
                BindsEngine.LastWhy);
            _timer.Start();
            Closed += (s, e) => { if (_timer != null) _timer.Stop(); };
        }

        private Grid Section(string title)
        {
            var g = new Grid { Background = Card, Margin = new Thickness(0, 0, 0, 8) };
            g.Children.Add(new TextBlock { Text = title, Foreground = Mute, FontSize = 10, FontWeight = FontWeights.Bold,
                                           Margin = new Thickness(10, 7, 0, 0), VerticalAlignment = VerticalAlignment.Top });
            return g;
        }

        private Button Button(string text, RoutedEventHandler onClick)
        {
            var b = new Button { Content = text, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(12, 4, 12, 4),
                                 Background = Card, Foreground = Ink, BorderBrush = Line };
            b.Click += onClick;
            return b;
        }

        private void Refresh()
        {
            _list.Items.Clear();
            foreach (var n in BindsLayout.Names()) _list.Items.Add(n);
        }

        /// <summary>Every usable window as a checkbox. TITLES are the identity throughout this tool:
        /// an HWND dies with the window, a caption survives a close-and-reopen.</summary>
        private void RefreshWindows()
        {
            var wasTicked = new HashSet<string>(PickedTitles());   // survive a refresh
            _wins.Items.Clear();
            BindsEngine.AttachExisting();
            foreach (var h in BindsEngine.Handles)
            {
                if (!W32.Listable(h)) continue;
                string t = W32.TitleOf(h);
                bool movable = W32.Usable(h);
                var cb = new CheckBox
                {
                    Content    = t,
                    Foreground = movable ? Ink : Mute,
                    FontSize   = 11,
                    IsChecked  = wasTicked.Contains(t),
                    // Maximized/minimized windows can be BOUND, they just will not move until restored.
                    ToolTip    = movable ? null : "maximized or minimized — restore it to move with the bind"
                };
                _wins.Items.Add(cb);
            }
        }

        private List<string> PickedTitles()
        {
            var l = new List<string>();
            foreach (var o in _wins.Items)
            {
                var cb = o as CheckBox;
                if (cb != null && cb.IsChecked == true) l.Add(cb.Content as string);
            }
            return l;
        }

        private void Status(string s) { if (_status != null) _status.Text = s; }
    }
}
