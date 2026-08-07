// ─────────────────────────────────────────────────────────────────────────────
// This Source Code Form is subject to the terms of the Mozilla Public License,
// v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
// one at https://mozilla.org/MPL/2.0/.
//
// Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
// ─────────────────────────────────────────────────────────────────────────────
// ═════════════════════════════════════════════════════════════════════════════
//  SentinelCockpit — the Sentinel Suite command surface (NT8 AddOn window)
//  File: SentinelCockpit_v0_1_0.cs   ·   Version v0.5.0   ·   namespace …AddOns.Sentinel
//  (AddOn windows bump IN-PLACE — file/class identity is not chart-serialized. Same precedent as
//   SentinelDashboard_v1_0_0.cs @ v1.1.9 and SentinelCore_v1_0_0.cs @ v1.14.0.)
// ─────────────────────────────────────────────────────────────────────────────
//  WHAT THIS IS  (spec: Docs/SENTINEL_COCKPIT_SPEC.md · memory: sentinel-backlog "THE SENTINEL COCKPIT")
//    ONE dockable/floatable, always-on-top rail that RE-READS the published SentinelCore …State seams so the
//    operator never opens the Indicator dialog or greps the log to answer "is my brain alive, and why isn't it
//    trading?". Opens from Control Center ▸ New ▸ "Sentinel Cockpit". It's a plain WPF NTWindow (like the
//    Dashboard) — so float + always-on-top pin come free — and it OWNS no service and consults no chart render
//    target: it only reads SentinelCore.Get…State. The on-chart SharpDX cards are untouched (this is a parallel,
//    opt-in readout). Theme follows the active skin via SentinelSkin.K* brushes (rebuilt at window open).
//
//  ARCHITECTURE (why WPF-reads-the-seams, not a SharpDX rail): every sensor already publishes a …State seam, so
//    the Cockpit is just another consumer — the same one the Council is. Dock/undock/float/pin are native to WPF
//    windows (proven by the Deck). See spec §2.
//
//  PHASE 1 + 2 (this file): ① DECISION (CouncilState + the computed "why no trade" line) · ② GATE (kill-switch +
//    per-account Governor) · ③ CONTEXT (the modulators) · ④ VOTERS (the confluence, unburied). Float + pin +
//    instrument picker + theme + collapsible sections. NO change to any existing tool and NO new SentinelCore seam
//    (this only READS Get…State) → the F5 that compiles it is safe for a running test.
//    Phase 3 (next) = ChartTrader dock + follow-active-chart. (Spec §7.)
//
//  THE THREE SEAM STATES (the honesty rule this window exists to enforce):
//    FRESH   — seen within its StaleSec           → green dot, live values
//    STALE   — seen, but not recently             → amber dot + "Ns" (the dry-up flicker: it is QUIET, not GONE)
//    ABSENT  — never published this session       → faint dot + "— not loaded" (the sensor isn't on the chart)
//    Conflating STALE with ABSENT is exactly the bug that made the Council look dead while it was running.
//
//  CHANGELOG
//    v0.5.0 (2026-07-15) — ⑤ HELM · INTERDICT (Phase 5; needs SentinelCore ≥ v1.34.0; memory helm-interdiction-layer).
//             A new monitor-rail section that lets the operator GRAB THE WHEEL of a running actor without stopping it:
//             it reads AllHelmStates() → shows the interdictable actor for the picked instrument (instanceKey · status ·
//             position · live stop/target · paused/override · freshness dot) and PUBLISHES HelmIntents via
//             SentinelCore.SetHelmIntent — Pause/Resume/Skip/Flatten/Breakeven, MoveStop/MoveTarget (type a price →
//             Stop→/Tgt→), Scale-down (reduce N), TakeOver/HandBack. The Bridge (v0.3.0, 'Obey Helm' on) executes each
//             with its OWN order handles — this surface never touches an order (risk-adding verbs pass the Bridge's
//             GateEntry). Persistent controls (not rebuilt each tick, so a typed price survives). The Cockpit was
//             already a writer (BUILD writes Roster.conf); this writes INTENTS. Reads/publishes seams only, no order
//             path → the F5 is test-safe.
//    v0.4.0 (2026-07-14) — PER-LANE AUTHORING (System Builder spec §14; needs SentinelCore ≥ v1.33.0). BUILD mode gains
//             a "lane" field: the roster + a new PROFILE editor target Models\<inst>\<bartag>@<lane>\. (1) Roster.conf
//             is written to the LANE folder (RosterIO write; a laned READ inherits the bar-type baseline until you Save
//             = fork). (2) A Lane.conf PROFILE editor (floor + deadband first-class, plus a raw key=value box for the
//             consult toggles / modulator damps) writes the fusion-knob overrides beside the roster (LaneIO); blank =
//             inherit F6. Selecting a laned scope auto-fills the lane field; Save writes both files → two same-bartype
//             A/B charts run different SYSTEMS on identical bars, authored from the GUI. Applies on the Council's reload.
//    v0.3.0 (2026-07-12) — ⑤ BUILD MODE (System Builder Phase 1; needs SentinelCore ≥ v1.27.0 for VoterCatalog +
//             RosterIO). A header "BUILD" toggle swaps the body from the monitor rail to a per-scope ROSTER EDITOR:
//             the 14 catalog voters as rows (include ✓ · weight · state/trigger · live-seam dot), seeded from the
//             scope's Roster.conf (via RosterIO.Read) or the Council's default declaration when no file exists. Live
//             preview recomputes declaredW + stateW (the quiet-bar denominator) and predicts RosterComplete against
//             the live Council's roster mask (who is actually speaking) as you edit. Save writes Roster.conf
//             atomically (RosterIO.Write) — applies on the Council's next reload (hot-reload is a later phase).
//             This is the WRITE-SIDE twin of the Decision readout: the same RosterComplete the hero card shows is
//             what the editor predicts. Spec: Docs/SENTINEL_SYSTEM_BUILDER_SPEC.md. Reads/writes config only — no
//             new seam, no order path → the F5 is test-safe.
//    v0.2.2 (2026-07-09) — ROSTER LINE (exec plan 3.1; needs SentinelCore ≥ v1.16.0). Renders the Council's
//             declared-vs-actual roster under the tally — `Roster 8/10 — EYE, BRK missing`, amber when incomplete
//             or when an undeclared sensor spoke. Given its OWN row rather than a why-line rung: an incomplete
//             roster degrades TRUST without BLOCKING, so folding it into the kill ▸ governor ▸ veto ▸ stale ▸ floor
//             chain would mask a real blocker beneath it. The one concession — a READY verdict fused from a partial
//             declaration reads "READY (roster 8/10)" in amber, because that is not the verdict the model describes.
//    v0.2.1 (2026-07-09) — SCOPE PICKER. The picker seeded BARE instrument names from WatchedInstruments alongside
//             the scoped ones, and Cockpit.conf still held a bare "GC" from before scope keys. With two GC charts
//             live, GetCouncilState("GC") fails CLOSED (logging AMBIGUOUS SCOPE) — so the hero card read "waiting
//             for Council" while the Council was in fact publishing two healthy verdicts. Now: a bare name is only
//             offered for instruments with NO scope yet (and pruned once a scope appears); a bare selection that
//             resolves to exactly one scope is silently upgraded to it; a bare selection with several scopes renders
//             an explicit "on N charts — pick a scope" list instead of a false absence. Patched in place (v0.2.0
//             never froze). Reads seams only — SentinelCore stays v1.15.0.
//    v0.2.0 (2026-07-08) — PHASE 2. New ③ CONTEXT section (Clock · Participation · Location · MTF · Intermarket —
//             the modulators, i.e. why conviction is damped) and ④ VOTERS section (Eye · Trend · CCI · ADX ·
//             VolEnvelope · Brick · Compression · WAE · GodReversal, each with dir arrow, strength detail, and an
//             agree/dissent mark vs the Council bias; plus a Liquidity-walls VETO row). AGE DOTS everywhere via a
//             single fresh/stale/absent classifier. Sections ②③④ collapse on header click (hero ① never does) and
//             the collapsed set persists. New "⊘" header toggle hides ABSENT seams (declutter). Cockpit.conf gains
//             hideAbsent= + collapsed=. Collapsed sections skip their rebuild.
//             NOTE: the spec's full per-seam settings sheet is DEFERRED — the hide-absent toggle covers the real
//             decluttering need at a fraction of the UI surface.
//    v0.1.0 (2026-07-08) — initial: window + registration; Decision (Council verdict + why-line: kill ▸ governor ▸
//             veto ▸ stale ▸ floor ▸ size ▸ edge) + Gate (kill + governor cards); instrument picker (from
//             AllCouncilStates); pin=Topmost; Cockpit.conf persistence (instrument + pin); K* theme.
// ═════════════════════════════════════════════════════════════════════════════

#region Using declarations
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.Sentinel
{
    // ── AddOn: adds "Sentinel Cockpit" under Control Center ▸ New (same pattern as SentinelDashboard) ──
    public class SentinelCockpitAddOn : NinjaTrader.NinjaScript.AddOnBase
    {
        private NTMenuItem _menuItem;
        private NTMenuItem _newMenu;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "SentinelCockpit";
                Description = "Sentinel Cockpit — one dockable/floatable rail reading every published state seam (Control Center ▸ New).";
            }
        }

        protected override void OnWindowCreated(Window window)
        {
            ControlCenter cc = window as ControlCenter;
            if (cc == null || _menuItem != null) return;

            _newMenu = cc.FindFirst("ControlCenterMenuItemNew") as NTMenuItem;
            if (_newMenu == null) return;

            _menuItem = new NTMenuItem
            {
                Header = "Sentinel Cockpit",
                Style  = Application.Current.TryFindResource("MainMenuItem") as Style
            };
            _menuItem.Click += OnMenuClick;
            _newMenu.Items.Add(_menuItem);
        }

        protected override void OnWindowDestroyed(Window window)
        {
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
                var w = new SentinelCockpitWindow();
                w.Show();
                w.Activate();
            }));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  The Cockpit window.
    // ─────────────────────────────────────────────────────────────────────────
    public class SentinelCockpitWindow : NTWindow
    {
        private const double StaleSec = 90.0;   // a verdict older than this reads STALE (matches the Bridge's window)
        private const double BigAge   = 1e7;     // "give me the last verdict regardless of age" (we compute freshness ourselves)

        // Per-seam staleness budgets. We ALWAYS fetch with BigAge and judge freshness here, so an old reading shows
        // as STALE (amber, with its age) rather than vanishing — see "THE THREE SEAM STATES" in the header.
        private const double SeamStale = 120.0;  // per-bar sensors (Trend/CCI/ADX/Env/Brick/Comp/WAE/GREV/Eye/Liq)
        private const double SlowStale = 300.0;  // slow-cadence context (Clock / MTF / Intermarket / Location)

        // theme-aware palette (rebuilt from SentinelSkin by ApplyTheme at window open; dark fallbacks below)
        private static Brush Bg     = new SolidColorBrush(Color.FromRgb(0x0A, 0x0E, 0x17));
        private static Brush Card   = new SolidColorBrush(Color.FromRgb(0x11, 0x17, 0x26));
        private static Brush Card2  = new SolidColorBrush(Color.FromRgb(0x0E, 0x14, 0x20));
        private static Brush Edge   = new SolidColorBrush(Color.FromRgb(0x1E, 0x2A, 0x3D));
        private static Brush Text   = new SolidColorBrush(Color.FromRgb(0xE9, 0xEE, 0xF7));
        private static Brush Ink2   = new SolidColorBrush(Color.FromRgb(0xAE, 0xBA, 0xCE));
        private static Brush Muted  = new SolidColorBrush(Color.FromRgb(0x6C, 0x7A, 0x92));
        private static Brush Faint  = new SolidColorBrush(Color.FromRgb(0x26, 0x34, 0x4C));
        private static Brush Accent = new SolidColorBrush(Color.FromRgb(0x3F, 0xD1, 0xE0));
        private static Brush Green  = new SolidColorBrush(Color.FromRgb(0x25, 0xD0, 0x8B));
        private static Brush Red    = new SolidColorBrush(Color.FromRgb(0xFF, 0x5C, 0x6A));
        private static Brush Amber  = new SolidColorBrush(Color.FromRgb(0xF2, 0xB3, 0x4C));

        private static SolidColorBrush FB(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        private static void ApplyTheme()
        {
            try
            {
                SentinelSkin.MaybeRefreshTheme();
                Bg    = FB(SentinelSkin.KVoid);  Card  = FB(SentinelSkin.KPanel); Card2 = FB(SentinelSkin.KCard);
                Edge  = FB(SentinelSkin.KLine);  Text  = FB(SentinelSkin.KInk);   Ink2  = FB(SentinelSkin.KInk2);
                Muted = FB(SentinelSkin.KMute);  Faint = FB(SentinelSkin.KFaint); Accent = FB(SentinelSkin.KAccent);
                Green = FB(SentinelSkin.KUp);    Red   = FB(SentinelSkin.KDown);  Amber = FB(SentinelSkin.KWarn);
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelCockpit.ApplyTheme", _sx); }
        }

        private ComboBox        _instrument;
        private TextBlock       _pinGlyph, _hideGlyph;
        private StackPanel      _decisionBody, _gateBody, _contextBody, _votersBody;
        private DispatcherTimer _timer;
        private bool            _pinned;
        private bool            _hideAbsent;                       // ⊘ toggle — hide seams that were never published
        private SentinelCore.CouncilState _lastCouncil;            // set by RefreshDecision, read by RefreshVoters (agree/dissent)

        // ── ⑤ Helm · interdict (Phase 5; needs SentinelCore ≥ v1.34.0) — publish HelmIntents to a RUNNING actor +
        //    render its HelmState. The Cockpit is already a writer (BUILD writes Roster.conf); Helm writes INTENTS the
        //    Bridge consumes — it still never touches an order. Persistent controls (NOT rebuilt each 750ms tick, so a
        //    half-typed price survives): RefreshHelm only updates the readout labels + freshness dot + the target
        //    instanceKey the static button handlers act on.
        private StackPanel _helmBody;
        private string     _helmKey;                              // the actor the buttons currently target (null = none live)
        private TextBlock  _helmTargetTb, _helmPosTb, _helmActionTb;
        private Border     _helmDot;
        private TextBox    _helmPrice, _helmQty;
        private DateTime   _lastHelmActionUtc;

        // collapsible sections (hero ① is deliberately not collapsible)
        private sealed class Sect
        {
            public string    Key;
            public Border    BodyWrap;
            public TextBlock Chevron;
            public bool      Collapsed;
        }
        private readonly List<Sect> _sections = new List<Sect>();
        private readonly HashSet<string> _collapsedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ── ⑤ BUILD mode (System Builder Phase 1) — a per-scope Roster.conf editor ──
        private bool           _buildMode;
        private Grid           _bodyHost;
        private ScrollViewer   _monitorScroll, _buildScroll;
        private Border         _buildToggleBtn;
        private TextBlock      _buildToggleGlyph;
        private StackPanel     _buildRowsPanel;
        private TextBlock       _buildScopeLine, _buildDenomLine, _buildRosterLine, _buildStatusLine;
        private string         _buildScope;               // the scope the panel is currently seeded for
        // v0.4.0 — per-LANE authoring (System Builder spec §14): a lane field → target Roster.conf AND Lane.conf under
        // Models\<inst>\<bartag>@<lane>\; profile controls edit the Lane.conf fusion-knob overrides (Phase 2).
        private TextBox        _buildLane;                // the target lane ("" = bare scope)
        private TextBox        _buildFloor, _buildDeadband, _buildProfileRaw;   // Lane.conf profile editors
        private TextBlock      _buildProfileSrc;          // "Lane.conf: new / loaded" truth line
        private SentinelCore.CouncilState _lastBuildCs;    // cached each build-refresh so edit handlers can recompute the live prediction
        private Dictionary<string, VoterEdit> _edit;       // per-tag editable state
        private Dictionary<string, string>    _loadedComments; // preserve inline "# …" comments on round-trip
        private readonly List<BuildRow> _buildRows = new List<BuildRow>();

        private sealed class VoterEdit
        {
            public bool                  Included;
            public double                Weight;
            public SentinelCore.VoterKind Kind;
        }
        private sealed class BuildRow
        {
            public string    Tag;
            public CheckBox  Chk;
            public TextBox   Wt;
            public TextBlock Name;
            public TextBlock KindTb;
            public Border    Dot;
            public VoterEdit Model;
        }

        public SentinelCockpitWindow()
        {
            Caption = "Sentinel Cockpit";
            Width = 330; Height = 720;

            ApplyTheme();
            LoadConf();                 // instrument + pin
            Content = BuildLayout();
            Topmost = _pinned;
            UpdatePinGlyph();
            UpdateHideGlyph();

            _timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(750) };
            _timer.Tick += (s, e) => Refresh();
            _timer.Start();

            Closed += OnClosedCleanup;
            RefreshInstruments();
            Refresh();
        }

        private void OnClosedCleanup(object sender, EventArgs e)
        {
            try { if (_timer != null) { _timer.Stop(); _timer = null; } } catch (Exception _sx) { SentinelCore.Swallow("SentinelCockpit.OnClosedCleanup", _sx); }
            SaveConf();
        }

        // ── layout ────────────────────────────────────────────────────────────
        private FrameworkElement BuildLayout()
        {
            var root = new DockPanel { Background = Bg, LastChildFill = true };

            // header
            var head = new Grid { Margin = new Thickness(10, 9, 8, 9) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // helmet
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // title
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // instr
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // build toggle
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // hide-absent
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // pin

            FrameworkElement helm;
            try { helm = SentinelSkin.HelmetMark(17, Accent); } catch { helm = new TextBlock { Text = "◆", Foreground = Accent }; }
            helm.VerticalAlignment = VerticalAlignment.Center; helm.Margin = new Thickness(0, 0, 8, 0);
            Grid.SetColumn(helm, 0); head.Children.Add(helm);

            var title = new TextBlock { Text = "COCKPIT", Foreground = Text, FontFamily = new FontFamily("Segoe UI"),
                FontSize = 12.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(title, 1); head.Children.Add(title);

            _instrument = new ComboBox { IsEditable = true, MinWidth = 96, HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(10, 0, 8, 0), FontFamily = new FontFamily("Consolas"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            _instrument.SelectionChanged += (s, e) => Refresh();
            _instrument.LostFocus += (s, e) => Refresh();
            if (!string.IsNullOrEmpty(_pendingInstr)) _instrument.Text = _pendingInstr;   // remembered from Cockpit.conf
            Grid.SetColumn(_instrument, 2); head.Children.Add(_instrument);

            _buildToggleBtn = new Border { Width = 44, Height = 24, CornerRadius = new CornerRadius(6), Background = Card,
                BorderBrush = Edge, BorderThickness = new Thickness(1), Cursor = System.Windows.Input.Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0),
                ToolTip = "Build a sensor system — edit this scope's Council roster" };
            _buildToggleGlyph = new TextBlock { Text = "BUILD", Foreground = Ink2, FontFamily = new FontFamily("Consolas"),
                FontSize = 9, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center };
            _buildToggleBtn.Child = _buildToggleGlyph;
            _buildToggleBtn.MouseLeftButtonUp += (s, e) => ToggleMode();
            Grid.SetColumn(_buildToggleBtn, 3); head.Children.Add(_buildToggleBtn);

            var hide = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(6), Background = Card,
                BorderBrush = Edge, BorderThickness = new Thickness(1), Cursor = System.Windows.Input.Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0),
                ToolTip = "Hide seams that aren't loaded" };
            _hideGlyph = new TextBlock { Text = "⊘", Foreground = Ink2, FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            hide.Child = _hideGlyph;
            hide.MouseLeftButtonUp += (s, e) => { _hideAbsent = !_hideAbsent; UpdateHideGlyph(); Refresh(); SaveConf(); };
            Grid.SetColumn(hide, 4); head.Children.Add(hide);

            var pin = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(6), Background = Card,
                BorderBrush = Edge, BorderThickness = new Thickness(1), Cursor = System.Windows.Input.Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center, ToolTip = "Always on top" };
            _pinGlyph = new TextBlock { Text = "📌", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center };
            pin.Child = _pinGlyph;
            pin.MouseLeftButtonUp += (s, e) => { _pinned = !_pinned; Topmost = _pinned; UpdatePinGlyph(); SaveConf(); };
            Grid.SetColumn(pin, 5); head.Children.Add(pin);

            DockPanel.SetDock(head, Dock.Top);
            root.Children.Add(head);

            var sep = new Border { Height = 1, Background = Edge };
            DockPanel.SetDock(sep, Dock.Top);
            root.Children.Add(sep);

            // body (scroll)
            var col = new StackPanel { Margin = new Thickness(10, 10, 10, 12) };
            _decisionBody = new StackPanel();
            _gateBody     = new StackPanel();
            _contextBody  = new StackPanel();
            _votersBody   = new StackPanel();
            col.Children.Add(Section("decision", "① Decision · Council",  _decisionBody, Red,  false)); // hero: never collapses (edge recolors live)
            col.Children.Add(Section("gate",     "② Gate · cleared?",     _gateBody,     null, true));
            col.Children.Add(Section("context",  "③ Context · modulators", _contextBody, null, true));
            col.Children.Add(Section("voters",   "④ Voters · confluence",  _votersBody,  null, true));
            _helmBody = BuildHelmSection();                                                     // Phase 5 — interdict a running actor
            col.Children.Add(Section("helm",     "⑤ Helm · interdict",    _helmBody,    null, true));

            var foot = new TextBlock { Text = "● fresh   ● stale (quiet, not gone)   ● not loaded    ·    click a header to collapse",
                Foreground = Muted, FontFamily = new FontFamily("Consolas"), FontSize = 9,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 10, 0, 0) };
            col.Children.Add(foot);

            _monitorScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = col, Background = Bg };
            _buildScroll   = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = BuildBuildPanel(),
                                                Background = Bg, Visibility = Visibility.Collapsed };
            _bodyHost = new Grid();
            _bodyHost.Children.Add(_monitorScroll);
            _bodyHost.Children.Add(_buildScroll);
            root.Children.Add(_bodyHost);
            return root;
        }

        private Border _heroCard;   // kept so the hero edge can recolor to the live bias
        private Border Section(string key, string title, UIElement body, Brush edgeTint, bool collapsible)
        {
            var outer = new Border
            {
                CornerRadius = new CornerRadius(10), Background = Card,
                BorderBrush = edgeTint ?? Edge, BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 9)
            };
            var stack = new StackPanel();

            var hdr = new Border { Background = Card2, Padding = new Thickness(10, 6, 10, 6) };
            if (collapsible) hdr.Cursor = System.Windows.Input.Cursors.Hand;
            var hg = new Grid();
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(13) });
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var chev = new TextBlock { Text = "▾", Foreground = Muted, FontFamily = new FontFamily("Consolas"),
                FontSize = 9, VerticalAlignment = VerticalAlignment.Center };
            if (!collapsible) chev.Opacity = 0;    // keep the title aligned with the collapsible sections
            Grid.SetColumn(chev, 0); hg.Children.Add(chev);
            var tt = new TextBlock { Text = title, Foreground = Muted, FontFamily = new FontFamily("Consolas"),
                FontSize = 10, FontWeight = FontWeights.SemiBold };
            Grid.SetColumn(tt, 1); hg.Children.Add(tt);
            hdr.Child = hg;
            stack.Children.Add(hdr);

            var bodyWrap = new Border { Padding = new Thickness(11, 9, 11, 11) };
            bodyWrap.Child = body;
            stack.Children.Add(bodyWrap);
            outer.Child = stack;
            if (edgeTint != null) _heroCard = outer;

            var sect = new Sect { Key = key, BodyWrap = bodyWrap, Chevron = chev,
                                  Collapsed = collapsible && _collapsedKeys.Contains(key) };
            if (collapsible)
                hdr.MouseLeftButtonUp += (s, e) => { sect.Collapsed = !sect.Collapsed; ApplyCollapse(sect); Refresh(); SaveConf(); };
            _sections.Add(sect);
            ApplyCollapse(sect);
            return outer;
        }

        private void ApplyCollapse(Sect s)
        {
            if (s == null) return;
            s.BodyWrap.Visibility = s.Collapsed ? Visibility.Collapsed : Visibility.Visible;
            s.Chevron.Text = s.Collapsed ? "▸" : "▾";
            if (s.Collapsed) _collapsedKeys.Add(s.Key); else _collapsedKeys.Remove(s.Key);
        }

        private bool IsCollapsed(string key)
        {
            foreach (var s in _sections) if (s.Key == key) return s.Collapsed;
            return false;
        }

        private void UpdatePinGlyph()
        {
            if (_pinGlyph == null) return;
            _pinGlyph.Opacity = _pinned ? 1.0 : 0.4;
        }

        private void UpdateHideGlyph()
        {
            if (_hideGlyph == null) return;
            _hideGlyph.Opacity    = _hideAbsent ? 1.0 : 0.4;
            _hideGlyph.Foreground = _hideAbsent ? Accent : Ink2;
        }

        // ── refresh ─────────────────────────────────────────────────────────────
        private void RefreshInstruments()
        {
            if (_instrument == null) return;
            try
            {
                var names = new List<string>();
                var scoped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // instruments that already have a scope
                Action<string> add = n => { if (!string.IsNullOrEmpty(n) && !names.Contains(n)) names.Add(n); };
                // 1) SCOPES with a live Council verdict (v1.15.0). One entry per CHART ("GC.69697v6"), because
                //    two charts on one instrument publish two verdicts and a bare-instrument lookup is ambiguous.
                foreach (var cs in SentinelCore.AllCouncilStates())
                {
                    if (cs == null) continue;
                    add(cs.Scope ?? cs.Instrument);
                    if (!string.IsNullOrEmpty(cs.Instrument)) scoped.Add(cs.Instrument);
                }
                // 2) seed from watched instruments too, so the picker is usable BEFORE the Council warms up post-F5 —
                //    but NEVER offer a bare instrument that already has scopes. GetCouncilState fails CLOSED on an
                //    ambiguous bare lookup, so such an entry is a trap: it can only ever render "no verdict".
                try { foreach (var ins in SentinelCore.WatchedInstruments())
                        if (ins != null && ins.MasterInstrument != null && !scoped.Contains(ins.MasterInstrument.Name))
                            add(ins.MasterInstrument.Name); } catch (Exception _sx) { SentinelCore.Swallow("SentinelCockpit.RefreshInstruments", _sx); }
                names.Sort(StringComparer.OrdinalIgnoreCase);

                string keep = (_instrument.Text ?? "").Trim();
                // Drop bare entries that a second chart has since made ambiguous (they may be left over from an
                // earlier refresh, or from a Cockpit.conf written before scope keys existed).
                for (int i = _instrument.Items.Count - 1; i >= 0; i--)
                {
                    string it = _instrument.Items[i] as string;
                    if (it != null && !names.Contains(it) && scoped.Contains(it)) _instrument.Items.RemoveAt(i);
                }
                foreach (string n in names) if (!_instrument.Items.Contains(n)) _instrument.Items.Add(n);
                if (string.IsNullOrEmpty(keep) && names.Count > 0) _instrument.Text = names[0];
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelCockpit.RefreshInstruments", _sx); }
        }

        /// <summary>The picker's text. Since v1.15.0 this is a SCOPE ("GC.69697v6") once the Council warms up,
        /// but it may still be a bare instrument ("GC") when seeded from WatchedInstruments before that.
        /// GetCouncilState accepts either.</summary>
        private string CurrentInstrument()
        {
            string s = _instrument != null ? (_instrument.Text ?? "").Trim() : "";
            return s;
        }

        // v1.18.0: hand every seam the SCOPE. Migrated seams (Adx/Trend/Cci/Envelope) hit it exactly; the rest are
        // still instrument-keyed and Core's scope→instrument shim resolves them, so ONE key works across a
        // half-migrated tree. Passing the bare instrument instead would fail CLOSED on every migrated seam the
        // moment a second chart on that instrument publishes — the Cockpit would go blind exactly when it matters.
        // RefreshDecision runs first (see Refresh()) and caches the scope off the resolved verdict.
        private string _seamKey;
        private string InstNameForSeams()
        {
            if (!string.IsNullOrEmpty(_seamKey)) return _seamKey;
            return CurrentInstrument();   // pre-warmup the picker may still hold a bare instrument name
        }

        /// <summary>Every Council scope currently publishing for a bare instrument name, sorted.</summary>
        private static List<string> ScopesFor(string instrument)
        {
            var found = new List<string>();
            try
            {
                foreach (var cs in SentinelCore.AllCouncilStates())
                    if (cs != null && !string.IsNullOrEmpty(cs.Scope)
                        && string.Equals(cs.Instrument, instrument, StringComparison.OrdinalIgnoreCase))
                        found.Add(cs.Scope);
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelCockpit.ScopesFor", _sx); }
            found.Sort(StringComparer.OrdinalIgnoreCase);
            return found;
        }

        // Rewriting the picker's text re-enters Refresh() through SelectionChanged, so it is deferred off the
        // layout pass we are in the middle of building.
        private bool _selecting;
        private void SelectScopeDeferred(string scope)
        {
            if (_instrument == null || _selecting) return;
            _selecting = true;
            _instrument.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (!_instrument.Items.Contains(scope)) _instrument.Items.Add(scope);
                    if (!string.Equals(_instrument.Text, scope, StringComparison.Ordinal)) _instrument.Text = scope;
                }
                catch (Exception _sx) { SentinelCore.Swallow("SentinelCockpit.SelectScopeDeferred", _sx); }
                finally { _selecting = false; }
            }));
        }

        private void Refresh()
        {
            try
            {
                RefreshInstruments();
                if (_buildMode) { RefreshBuildLive(); return; }     // build mode owns the body — skip the monitor rebuild
                RefreshDecision();                                  // always (hero) — also caches _lastCouncil
                if (!IsCollapsed("gate"))    RefreshGate();         // a collapsed section skips its rebuild
                if (!IsCollapsed("context")) RefreshContext();
                if (!IsCollapsed("voters"))  RefreshVoters();
                if (!IsCollapsed("helm"))    RefreshHelm();

            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelCockpit.Refresh", _sx); }
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  ⑤ BUILD MODE — a per-scope Roster.conf editor (System Builder Phase 1).
        //  Writes what the Council READS (RosterIO), and predicts RosterComplete against
        //  the SAME live roster mask the hero card shows. Config only — no order path.
        // ═══════════════════════════════════════════════════════════════════════
        private void ToggleMode()
        {
            _buildMode = !_buildMode;
            if (_monitorScroll != null) _monitorScroll.Visibility = _buildMode ? Visibility.Collapsed : Visibility.Visible;
            if (_buildScroll   != null) _buildScroll.Visibility   = _buildMode ? Visibility.Visible   : Visibility.Collapsed;
            if (_buildToggleGlyph != null)
            {
                _buildToggleGlyph.Text       = _buildMode ? "VIEW" : "BUILD";
                _buildToggleGlyph.Foreground = _buildMode ? Accent : Ink2;
            }
            if (_buildToggleBtn != null) _buildToggleBtn.BorderBrush = _buildMode ? Tint(Accent, 0.6) : Edge;
            if (_buildMode) SeedBuild();
            else            Refresh();
        }

        private FrameworkElement BuildBuildPanel()
        {
            var col = new StackPanel { Margin = new Thickness(10, 10, 10, 12) };

            col.Children.Add(new TextBlock { Text = "⑤ BUILD · sensor system", Foreground = Accent,
                FontFamily = new FontFamily("Consolas"), FontSize = 11, FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 2) });
            col.Children.Add(Mono("pick the voters + weights the Council fuses for this scope. Save writes Roster.conf.",
                Muted, 9, new Thickness(0, 0, 0, 8), true));

            _buildScopeLine = Mono("scope —", Ink2, 10, new Thickness(0, 0, 0, 4), true);
            col.Children.Add(_buildScopeLine);

            // v0.4.0 — LANE field: target a per-chart lane's Roster.conf + Lane.conf. Blank = the bare scope.
            var laneRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            laneRow.Children.Add(Mono("lane", Muted, 9, new Thickness(0, 4, 6, 0)));
            _buildLane = new TextBox { Width = 84, FontFamily = new FontFamily("Consolas"), FontSize = 11,
                Background = Card, Foreground = Ink2, BorderBrush = Edge, BorderThickness = new Thickness(1),
                Padding = new Thickness(4, 1, 4, 1) };
            _buildLane.LostFocus += (s, e) => { _buildScope = null; SeedBuild(); };   // retarget on edit
            laneRow.Children.Add(_buildLane);
            laneRow.Children.Add(Mono("blank = bare · A/B = test lane (matches the chart's Council Scope Lane)", Muted, 8.5,
                new Thickness(8, 4, 0, 0)));
            col.Children.Add(laneRow);

            var hg = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            TextBlock h0 = Mono("on", Muted, 9), h1 = Mono("voter", Muted, 9), h2 = Mono("weight", Muted, 9), h3 = Mono("kind", Muted, 9);
            Grid.SetColumn(h0, 0); Grid.SetColumn(h1, 1); Grid.SetColumn(h2, 2); Grid.SetColumn(h3, 3);
            hg.Children.Add(h0); hg.Children.Add(h1); hg.Children.Add(h2); hg.Children.Add(h3);
            col.Children.Add(hg);

            _buildRowsPanel = new StackPanel();
            col.Children.Add(_buildRowsPanel);

            col.Children.Add(new Border { Height = 1, Background = Edge, Margin = new Thickness(0, 8, 0, 8) });

            _buildDenomLine  = Mono("declaredW — · stateW —", Ink2,  10, new Thickness(0, 0, 0, 2), true);
            _buildRosterLine = Mono("—",                      Muted, 10, new Thickness(0, 0, 0, 8), true);
            col.Children.Add(_buildDenomLine);
            col.Children.Add(_buildRosterLine);

            // v0.4.0 — LANE PROFILE (Lane.conf) editor (Phase 2): the Council fusion knobs Roster.conf doesn't hold.
            // Blank = inherit the chart's F6. Only saved when non-empty, so a bare chart stays untouched.
            col.Children.Add(new TextBlock { Text = "lane profile · Lane.conf (blank = inherit F6)", Foreground = Accent,
                FontFamily = new FontFamily("Consolas"), FontSize = 9.5, FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 3) });
            var profRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            profRow.Children.Add(Mono("floor", Muted, 9, new Thickness(0, 4, 5, 0)));
            _buildFloor = new TextBox { Width = 50, FontFamily = new FontFamily("Consolas"), FontSize = 11,
                Background = Card, Foreground = Ink2, BorderBrush = Edge, BorderThickness = new Thickness(1),
                Padding = new Thickness(4, 1, 4, 1) };
            profRow.Children.Add(_buildFloor);
            profRow.Children.Add(Mono("deadband", Muted, 9, new Thickness(12, 4, 5, 0)));
            _buildDeadband = new TextBox { Width = 50, FontFamily = new FontFamily("Consolas"), FontSize = 11,
                Background = Card, Foreground = Ink2, BorderBrush = Edge, BorderThickness = new Thickness(1),
                Padding = new Thickness(4, 1, 4, 1) };
            profRow.Children.Add(_buildDeadband);
            col.Children.Add(profRow);
            col.Children.Add(Mono("extra overrides (key = value per line: consultmtf=false · fluxabsorbdamp=0.5 · leveldamp=0.7 …)",
                Muted, 8.5, new Thickness(0, 0, 0, 2)));
            _buildProfileRaw = new TextBox { AcceptsReturn = true, MinHeight = 46, MaxHeight = 90, TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Consolas"), FontSize = 10,
                Background = Card, Foreground = Ink2, BorderBrush = Edge, BorderThickness = new Thickness(1),
                Padding = new Thickness(4, 2, 4, 2), Margin = new Thickness(0, 0, 0, 3) };
            col.Children.Add(_buildProfileRaw);
            _buildProfileSrc = Mono("", Muted, 8.5, new Thickness(0, 0, 0, 8), true);
            col.Children.Add(_buildProfileSrc);

            col.Children.Add(new Border { Height = 1, Background = Edge, Margin = new Thickness(0, 0, 0, 8) });

            var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 6) };
            btns.Children.Add(Btn("Save",   Green,  (s, e) => SaveBuild()));
            btns.Children.Add(Btn("Reload", Accent, (s, e) => SeedBuild()));
            col.Children.Add(btns);

            _buildStatusLine = Mono("", Muted, 9, new Thickness(0, 2, 0, 0), true);
            col.Children.Add(_buildStatusLine);

            col.Children.Add(Mono("changes apply on the Council's next chart reload (hot-reload is a later phase).",
                Muted, 8.5, new Thickness(0, 8, 0, 0), true));
            return col;
        }

        private FrameworkElement BuildVoterRow(SentinelCore.CatalogEntry entry, VoterEdit model)
        {
            var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });

            var row = new BuildRow { Tag = entry.Tag, Model = model };

            var chk = new CheckBox { IsChecked = model.Included, VerticalAlignment = VerticalAlignment.Center };
            chk.Checked   += (s, e) => { model.Included = true;  UpdateRowDim(row); RecomputeBuild(_lastBuildCs); };
            chk.Unchecked += (s, e) => { model.Included = false; UpdateRowDim(row); RecomputeBuild(_lastBuildCs); };
            Grid.SetColumn(chk, 0); g.Children.Add(chk); row.Chk = chk;

            var name = new TextBlock { Text = entry.Display, Foreground = Text, FontFamily = new FontFamily("Consolas"),
                FontSize = 11, VerticalAlignment = VerticalAlignment.Center, ToolTip = entry.Tag + " — " + entry.Notes };
            Grid.SetColumn(name, 1); g.Children.Add(name); row.Name = name;

            var wt = new TextBox { Text = model.Weight.ToString("0.##", CultureInfo.InvariantCulture), Width = 42,
                Background = Card2, Foreground = Text, BorderBrush = Edge, BorderThickness = new Thickness(1),
                FontFamily = new FontFamily("Consolas"), FontSize = 10, Padding = new Thickness(3, 1, 3, 1),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            wt.TextChanged += (s, e) =>
            {
                double v;
                if (double.TryParse(wt.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                { model.Weight = Math.Max(0, v); RecomputeBuild(_lastBuildCs); }
            };
            Grid.SetColumn(wt, 2); g.Children.Add(wt); row.Wt = wt;

            var kind = new Border { CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 1, 6, 1), Cursor = System.Windows.Input.Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left,
                ToolTip = "STATE always dilutes conviction · TRIGGER dilutes only when it fires or is absent" };
            var kindTb = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 9 };
            kind.Child = kindTb; row.KindTb = kindTb;
            StyleKind(row);
            kind.MouseLeftButtonUp += (s, e) =>
            {
                model.Kind = model.Kind == SentinelCore.VoterKind.State ? SentinelCore.VoterKind.Trigger : SentinelCore.VoterKind.State;
                StyleKind(row); RecomputeBuild(_lastBuildCs);
            };
            Grid.SetColumn(kind, 3); g.Children.Add(kind);

            var dot = Dot(Faint);
            dot.VerticalAlignment = VerticalAlignment.Center; dot.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(dot, 4); g.Children.Add(dot); row.Dot = dot;

            _buildRows.Add(row);
            UpdateRowDim(row);
            return g;
        }

        private void StyleKind(BuildRow row)
        {
            bool trig = row.Model.Kind == SentinelCore.VoterKind.Trigger;
            row.KindTb.Text = trig ? "TRIG" : "STATE";
            Brush c = trig ? Accent : Ink2;
            row.KindTb.Foreground = c;
            var b = row.KindTb.Parent as Border;
            if (b != null) b.BorderBrush = Tint(c, 0.5);
        }

        private void UpdateRowDim(BuildRow row)
        {
            double op = row.Model.Included ? 1.0 : 0.4;
            if (row.Name != null) row.Name.Opacity = op;
            if (row.Wt   != null) row.Wt.Opacity   = op;
            if (row.KindTb != null && row.KindTb.Parent is UIElement) ((UIElement)row.KindTb.Parent).Opacity = op;
        }

        /// <summary>Resolve the picked instrument to (inst, barTag, live CouncilState); returns the scope string.</summary>
        private string ResolveCouncil(out string inst, out string barTag, out SentinelCore.CouncilState cs)
        {
            inst = null; barTag = null; cs = null;
            string sel = CurrentInstrument();
            if (!string.IsNullOrEmpty(sel) && sel.IndexOf('.') < 0)
            {
                var scopes = ScopesFor(sel);
                if (scopes.Count == 1) sel = scopes[0];              // unambiguous bare name → its scope
            }
            if (!string.IsNullOrEmpty(sel)) { try { cs = SentinelCore.GetCouncilState(sel, BigAge); } catch { cs = null; } }
            if (cs != null) { inst = cs.Instrument; barTag = cs.Bartype; return cs.Scope ?? sel; }
            // no live Council — derive inst/barTag from the text so an offline scope can still be edited
            if (!string.IsNullOrEmpty(sel))
            {
                int dot = sel.IndexOf('.');
                if (dot > 0) { inst = sel.Substring(0, dot); barTag = sel.Substring(dot + 1); }
                else inst = sel;
            }
            return sel;
        }

        /// <summary>(Re)seed the editor from the scope's Roster.conf — or the Council's default declaration when no file exists.</summary>
        // v0.4.0 — the sanitized lane text (alnum only, matching SentinelCore.SanitizeLane) and the EFFECTIVE tag
        // (bare bartag + "@lane") that targets a lane's Roster.conf + Lane.conf. Blank lane ⇒ the bare tag.
        private string LaneText()
        {
            string s = _buildLane != null ? (_buildLane.Text ?? "").Trim() : "";
            var sb = new StringBuilder(); foreach (char c in s) if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }
        private string EffTag(string barTag)
        {
            if (string.IsNullOrEmpty(barTag)) return barTag;
            string ln = LaneText(); return ln.Length == 0 ? barTag : barTag + "@" + ln;
        }

        // v0.4.0 — load the lane's Lane.conf into the profile editors (floor/deadband first-class, the rest raw).
        private void LoadProfile(string inst, string effTag)
        {
            string floor = "", dead = ""; var extra = new StringBuilder(); int n = 0;
            try
            {
                var m = SentinelCore.LaneIO.Read(inst, effTag);
                if (m != null) { n = m.Count; foreach (var kv in m) {
                    string k = kv.Key.ToLowerInvariant();
                    if (k == "floor") floor = kv.Value;
                    else if (k == "deadband") dead = kv.Value;
                    else extra.Append(kv.Key).Append(" = ").Append(kv.Value).Append('\n'); } }
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelCockpit.LoadProfile", _sx); }
            if (_buildFloor != null)      _buildFloor.Text = floor;
            if (_buildDeadband != null)   _buildDeadband.Text = dead;
            if (_buildProfileRaw != null) _buildProfileRaw.Text = extra.ToString().TrimEnd('\n');
            if (_buildProfileSrc != null) _buildProfileSrc.Text = n > 0 ? ("Lane.conf: " + n + " override(s)") : "Lane.conf: none (inherits F6)";
        }

        // v0.4.0 — write the profile editors to the lane's Lane.conf. Empty profile ⇒ header stub (Council inherits F6)
        // ONLY when a file already exists (to CLEAR it); we never create an empty Lane.conf for an untouched lane.
        private void SaveProfile(string inst, string effTag, string scope)
        {
            try
            {
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string fl = _buildFloor != null ? (_buildFloor.Text ?? "").Trim() : "";
                string db = _buildDeadband != null ? (_buildDeadband.Text ?? "").Trim() : "";
                if (fl.Length > 0) map["floor"] = fl;
                if (db.Length > 0) map["deadband"] = db;
                if (_buildProfileRaw != null)
                    foreach (var raw in (_buildProfileRaw.Text ?? "").Replace("\r", "").Split('\n'))
                    {
                        string line = raw; int h = line.IndexOf('#'); if (h >= 0) line = line.Substring(0, h);
                        line = line.Trim(); if (line.Length == 0) continue;
                        int eq = line.IndexOf('='); if (eq <= 0) continue;
                        string k = line.Substring(0, eq).Trim(); string v = line.Substring(eq + 1).Trim();
                        if (k.Length > 0) map[k] = v;
                    }
                bool exists = System.IO.File.Exists(SentinelCore.LaneIO.PathFor(inst, effTag));
                if (map.Count > 0 || exists) SentinelCore.LaneIO.Write(inst, effTag, map, "scope " + scope);
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelCockpit.SaveProfile", _sx); }
        }

        private void SeedBuild()
        {
            if (_buildRowsPanel == null) return;
            string inst, barTag; SentinelCore.CouncilState cs;
            string scope = ResolveCouncil(out inst, out barTag, out cs);
            // v0.4.0 — auto-fill the lane field from a laned SELECTION when the field is empty (pick @A → targets A).
            if (!string.IsNullOrEmpty(scope) && LaneText().Length == 0 && _buildLane != null)
            {
                int at = scope.IndexOf('@');
                if (at >= 0) _buildLane.Text = scope.Substring(at + 1);
            }
            string effTag = EffTag(barTag);
            _buildScope = scope; _lastBuildCs = cs;

            var doc = SentinelCore.RosterIO.Read(inst, effTag);
            bool hasFile = doc != null && doc.HasDeclarations;

            _edit = new Dictionary<string, VoterEdit>(StringComparer.Ordinal);
            _loadedComments = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var e in SentinelCore.VoterCatalog.Voters)
            {
                var line = hasFile ? doc.Find(e.Tag) : null;
                var m = new VoterEdit();
                if (line != null)
                {
                    m.Included = true;
                    m.Weight   = line.Weight.HasValue ? line.Weight.Value : e.DefaultWeight;
                    m.Kind     = line.Kind.HasValue   ? line.Kind.Value   : e.DefaultKind;
                    if (!string.IsNullOrEmpty(line.Comment)) _loadedComments[e.Tag] = line.Comment;
                }
                else if (!hasFile)   // no Roster.conf → mirror the Council's default declaration (KnownVoters where BaseWeight>0)
                {
                    m.Included = e.DefaultWeight > 0;
                    m.Weight   = e.DefaultWeight;
                    m.Kind     = e.DefaultKind;
                }
                else                 // a file exists but omits this tag → the user's roster excludes it
                {
                    m.Included = false;
                    m.Weight   = e.DefaultWeight;
                    m.Kind     = e.DefaultKind;
                }
                _edit[e.Tag] = m;
            }

            // v0.4.0 — show the EFFECTIVE (laned) scope the editor is targeting + whether it inherits the bar-type baseline
            string effScope = string.IsNullOrEmpty(inst) ? scope : (inst + "." + (string.IsNullOrEmpty(effTag) ? barTag : effTag));
            bool laneNew = hasFile && LaneText().Length > 0 && doc.Source != null && doc.Source.IndexOf('@') < 0;   // reading the baseline
            _buildScopeLine.Text = "scope " + (string.IsNullOrEmpty(effScope) ? "—" : effScope) + "   ·   " +
                (hasFile ? (ShortPath(doc.Source) + (laneNew ? "  (inherits baseline — Save forks this lane)" : ""))
                         : "default (no Roster.conf yet)");

            _buildRows.Clear();
            _buildRowsPanel.Children.Clear();
            foreach (var e in SentinelCore.VoterCatalog.Voters)
                _buildRowsPanel.Children.Add(BuildVoterRow(e, _edit[e.Tag]));

            LoadProfile(inst, effTag);   // v0.4.0 — Lane.conf → the profile editors

            RecomputeBuild(cs);
            SetBuildStatus(hasFile ? ("loaded " + CountIncluded() + " voters from Roster.conf")
                                   : "seeded from Council defaults (no file yet)", Muted);
        }

        /// <summary>Recompute declaredW/stateW from the edit model + predict RosterComplete against the live roster mask.</summary>
        private void RecomputeBuild(SentinelCore.CouncilState cs)
        {
            if (_edit == null || _buildDenomLine == null) return;
            double declaredW = 0, stateW = 0; int incl = 0;
            foreach (var e in SentinelCore.VoterCatalog.Voters)
            {
                VoterEdit m; if (!_edit.TryGetValue(e.Tag, out m) || !m.Included) continue;
                incl++;
                double w = Math.Max(0, m.Weight);
                declaredW += w;
                if (m.Kind == SentinelCore.VoterKind.State) stateW += w;
            }
            _buildDenomLine.Text = "declaredW " + declaredW.ToString("0.00", CultureInfo.InvariantCulture) +
                " · stateW " + stateW.ToString("0.00", CultureInfo.InvariantCulture) + "   (quiet-bar denom)";

            string mask = (cs != null && cs.Roster != null) ? cs.Roster.Mask : null;
            if (mask == null)
            {
                _buildRosterLine.Text = incl + " voters declared · no live Council on this scope to check presence";
                _buildRosterLine.Foreground = Muted;
                return;
            }
            var silent = new List<string>(); int present = 0;
            foreach (var e in SentinelCore.VoterCatalog.Voters)
            {
                VoterEdit m; if (!_edit.TryGetValue(e.Tag, out m) || !m.Included) continue;
                if (MaskHas(mask, e.Tag)) present++; else silent.Add(e.Tag);
            }
            _buildRosterLine.Text = "live: " + present + "/" + incl + " speaking" +
                (silent.Count > 0 ? " · silent: " + string.Join(", ", silent.ToArray()) : "");
            _buildRosterLine.Foreground = silent.Count > 0 ? Amber : Green;
        }

        /// <summary>Timer tick while in build mode: reseed on a scope change, else refresh the live dots + prediction only.</summary>
        private void RefreshBuildLive()
        {
            if (!_buildMode || _buildRowsPanel == null) return;
            string inst, barTag; SentinelCore.CouncilState cs;
            string scope = ResolveCouncil(out inst, out barTag, out cs);
            if (!string.Equals(scope, _buildScope, StringComparison.Ordinal)) { SeedBuild(); return; }  // switched scope → reseed
            _lastBuildCs = cs;
            string mask = (cs != null && cs.Roster != null) ? cs.Roster.Mask : null;
            foreach (var row in _buildRows)
            {
                bool speaking = mask != null && MaskHas(mask, row.Tag);
                row.Dot.Background = speaking ? Green : (row.Model.Included ? Amber : Faint);
            }
            RecomputeBuild(cs);
        }

        private void SaveBuild()
        {
            if (_edit == null) { SetBuildStatus("nothing to save", Amber); return; }
            string inst, barTag; SentinelCore.CouncilState cs;
            string scope = ResolveCouncil(out inst, out barTag, out cs);
            if (string.IsNullOrEmpty(inst)) { SetBuildStatus("pick an instrument first", Amber); return; }

            var doc = new SentinelCore.RosterDoc();
            foreach (var e in SentinelCore.VoterCatalog.Voters)
            {
                VoterEdit m; if (!_edit.TryGetValue(e.Tag, out m) || !m.Included) continue;
                var line = new SentinelCore.RosterLine { Tag = e.Tag, Weight = Math.Max(0, m.Weight) };
                if (m.Kind != e.DefaultKind) line.Kind = m.Kind;      // only pin kind when it differs from the catalog default
                string c; if (_loadedComments != null && _loadedComments.TryGetValue(e.Tag, out c)) line.Comment = c;
                doc.Lines.Add(line);
            }

            try
            {
                string effTag = EffTag(barTag);   // v0.4.0 — write to the LANE's folder when a lane is set
                string effScope = inst + "." + (string.IsNullOrEmpty(effTag) ? barTag : effTag);
                string note = "scope " + effScope + " · saved " + DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                SentinelCore.RosterIO.Write(inst, effTag, doc, note);
                SaveProfile(inst, effTag, effScope);   // v0.4.0 — write the Lane.conf profile alongside the roster
                SetBuildStatus("saved " + doc.Lines.Count + " voters + profile → " + effScope + " (applies on next chart reload)", Green);
                _buildScope = null;   // force a reseed so the source path + round-trip reflect the written file
                SeedBuild();
            }
            catch (Exception ex) { SetBuildStatus("save failed: " + ex.Message, Red); }
        }

        private void SetBuildStatus(string msg, Brush col)
        {
            if (_buildStatusLine == null) return;
            _buildStatusLine.Text = msg; _buildStatusLine.Foreground = col;
        }

        private int CountIncluded()
        {
            int n = 0;
            if (_edit != null) foreach (var kv in _edit) if (kv.Value.Included) n++;
            return n;
        }

        private static bool MaskHas(string mask, string tag)
        {
            if (string.IsNullOrEmpty(mask) || string.IsNullOrEmpty(tag)) return false;
            foreach (string p in mask.Split(','))
                if (string.Equals(p.Trim(), tag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string ShortPath(string p)
        {
            if (string.IsNullOrEmpty(p)) return "default";
            int i = p.IndexOf("Models", StringComparison.OrdinalIgnoreCase);
            return i >= 0 ? p.Substring(i) : System.IO.Path.GetFileName(p);
        }

        private Border Btn(string text, Brush col, System.Windows.Input.MouseButtonEventHandler onClick)
        {
            var b = new Border { CornerRadius = new CornerRadius(6), Background = Tint(col, 0.16), BorderBrush = Tint(col, 0.5),
                BorderThickness = new Thickness(1), Padding = new Thickness(14, 5, 14, 5), Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(0, 0, 7, 0),
                Child = new TextBlock { Text = text, Foreground = col, FontFamily = new FontFamily("Consolas"),
                    FontSize = 11, FontWeight = FontWeights.SemiBold } };
            b.MouseLeftButtonUp += onClick;
            return b;
        }

        // ① DECISION — the Council verdict + the computed "why" line
        private void RefreshDecision()
        {
            if (_decisionBody == null) return;
            _decisionBody.Children.Clear();

            string inst = CurrentInstrument();

            // The picker can still hold a BARE instrument — persisted in Cockpit.conf before scope keys existed, or
            // seeded from WatchedInstruments pre-warmup. GetCouncilState fails CLOSED when two scopes share it, which
            // renders as "waiting for Council" when the truthful answer is "say which chart you mean". Upgrade to the
            // scope when it is unambiguous; name the choices when it is not.
            List<string> ambiguous = null;
            if (!string.IsNullOrEmpty(inst) && inst.IndexOf('.') < 0)
            {
                var scopes = ScopesFor(inst);
                if (scopes.Count == 1) { inst = scopes[0]; SelectScopeDeferred(inst); }
                else if (scopes.Count > 1) ambiguous = scopes;
            }

            if (ambiguous != null)
            {
                _lastCouncil = null; _seamKey = inst;
                _decisionBody.Children.Add(Mono("— \"" + inst + "\" is on " + ambiguous.Count + " charts. Pick a scope:", Amber, 11, null, true));
                foreach (string sc in ambiguous)
                    _decisionBody.Children.Add(Mono("    " + sc, Muted, 10, null, true));
                if (_heroCard != null) _heroCard.BorderBrush = Edge;
                return;
            }

            SentinelCore.CouncilState cs = null;
            double age = 0; bool stale = false;
            if (!string.IsNullOrEmpty(inst))
            {
                try { cs = SentinelCore.GetCouncilState(inst, BigAge); } catch { cs = null; }
                if (cs != null) { age = (DateTime.UtcNow - cs.UpdatedUtc).TotalSeconds; stale = age > StaleSec; }
            }
            _lastCouncil = cs;   // the Voters section marks agree/dissent against this bias
            // Cache the SCOPE for the seam lookups (Context/Voters run after this). v1.18.0: scope, not instrument —
            // migrated seams need it exactly, un-migrated ones resolve through Core's shim.
            _seamKey = (cs != null && !string.IsNullOrEmpty(cs.Scope)) ? cs.Scope : inst;

            if (cs == null)
            {
                _decisionBody.Children.Add(Mono(string.IsNullOrEmpty(inst)
                    ? "— pick or type an instrument above (e.g. GC)"
                    : "— waiting for Council on \"" + inst + "\" …", Muted, 11, null, true));
                if (!string.IsNullOrEmpty(inst))
                    _decisionBody.Children.Add(Mono("is the Council on a chart with Publish state = on?", Muted, 9, new Thickness(0, 4, 0, 0), true));
                if (_heroCard != null) _heroCard.BorderBrush = Edge;
                return;
            }

            int bias = cs.Bias;
            string side = bias > 0 ? "LONG" : (bias < 0 ? "SHORT" : "FLAT");
            Brush sideCol = bias > 0 ? Green : (bias < 0 ? Red : Muted);
            if (_heroCard != null) _heroCard.BorderBrush = MixEdge(sideCol);

            // row: bias pill · conv · size · age dot
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var pill = Pill(side, sideCol); Grid.SetColumn(pill, 0); row.Children.Add(pill);
            var conv = Mono("conv " + Pct(cs.Conviction), Ink2, 12); conv.VerticalAlignment = VerticalAlignment.Center;
            conv.Margin = new Thickness(9, 0, 0, 0); Grid.SetColumn(conv, 1); row.Children.Add(conv);
            var size = Mono("size ×" + cs.SizeMult.ToString("0.00", CultureInfo.InvariantCulture), cs.SizeMult > 0 ? Accent : Muted, 12);
            size.HorizontalAlignment = HorizontalAlignment.Right; size.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(size, 2); row.Children.Add(size);
            var dot = Dot(stale ? Amber : Green); dot.VerticalAlignment = VerticalAlignment.Center;
            dot.Margin = new Thickness(8, 0, 0, 0); Grid.SetColumn(dot, 3); row.Children.Add(dot);
            _decisionBody.Children.Add(row);

            // conviction bar (colored by bias)
            _decisionBody.Children.Add(Bar(cs.Conviction, sideCol, 7, new Thickness(0, 8, 0, 2)));

            // the WHY line
            Brush wcol; string why = WhyLine(cs, stale, age, out wcol);
            _decisionBody.Children.Add(WhyBox(why, wcol));

            // tally + reasons
            _decisionBody.Children.Add(Mono("▲" + cs.Agree + "  ▼" + cs.Disagree + "  ·  " + cs.Voters + " voters"
                + (stale ? "   ·   stale " + ((int)age) + "s" : ""), Muted, 10, new Thickness(0, 8, 0, 0)));

            // ROSTER — declared vs actual. This is the line that would have caught the Eye on day one: for 332
            // verdicts EYE simply never voted and the emergent roster had no way to say so. It gets its OWN row
            // rather than a why-line rung, because an incomplete roster degrades TRUST without BLOCKING — folding
            // it into the priority chain would mask a real blocker beneath it.
            if (cs.Roster != null)
            {
                bool ok = cs.Roster.Complete && string.IsNullOrEmpty(cs.Roster.Unexpected);
                _decisionBody.Children.Add(Mono("Roster  " + cs.Roster.ToString(), ok ? Muted : Amber, 10,
                                                new Thickness(0, 3, 0, 0), true));
            }

            if (!string.IsNullOrEmpty(cs.Reasons))
                _decisionBody.Children.Add(Mono(cs.Reasons, Muted, 9.5, new Thickness(0, 5, 0, 0), true));
        }

        // the priority-resolved plain-language answer to "why isn't it trading?"
        private string WhyLine(SentinelCore.CouncilState cs, bool stale, double age, out Brush col)
        {
            if (SentinelCore.KillSwitchEngaged) { col = Red; return "BLOCKED — kill-switch engaged"; }

            // any governed account halted/complete?
            try
            {
                foreach (var g in SentinelCore.AllGovernorStates())
                    if (g != null && !g.Allowed) { col = Red; return "BLOCKED — " + g.Account + " day " + StatusWord(g.Status); }
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelCockpit.WhyLine", _sx); }

            if (cs.Vetoed) { col = Amber; return "VETOED — " + (string.IsNullOrEmpty(cs.VetoReason) ? "hard gate" : cs.VetoReason); }
            if (stale)     { col = Amber; return "STALE — no fresh verdict for " + ((int)age) + "s (slow bars)"; }
            if (cs.Bias == 0 || !cs.HasEdge) { col = Muted; return "NO EDGE — waiting"; }
            if (cs.SizeMult <= 0) { col = Amber; return "STAND DOWN — conviction " + Pct(cs.Conviction) + " · size 0 (below the Council floor)"; }

            // Ready, but say so honestly: a verdict fused from a PARTIAL declaration is not the verdict the model
            // describes, and the operator should see that before it trades — without it being called a blocker.
            if (cs.Roster != null && !cs.Roster.Complete)
            {
                col = Amber;
                return "READY (roster " + cs.Roster.Present + "/" + cs.Roster.Declared + ") — "
                     + (cs.Bias > 0 ? "LONG" : "SHORT") + " · size ×" + cs.SizeMult.ToString("0.00", CultureInfo.InvariantCulture);
            }

            col = Green;
            return "READY — " + (cs.Bias > 0 ? "LONG" : "SHORT") + " · size ×" + cs.SizeMult.ToString("0.00", CultureInfo.InvariantCulture);
        }

        // ② GATE — kill-switch + per-account governor
        private void RefreshGate()
        {
            if (_gateBody == null) return;
            _gateBody.Children.Clear();

            // kill-switch row
            bool kill = false; try { kill = SentinelCore.KillSwitchEngaged; } catch (Exception _sx) { SentinelCore.Swallow("SentinelCockpit.RefreshGate", _sx); }
            var krow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            krow.Children.Add(Mono("Kill-switch", Muted, 10.5, new Thickness(0, 0, 8, 0)));
            krow.Children.Add(Chip(kill ? "ENGAGED" : "CLEAR", kill ? Red : Green));
            _gateBody.Children.Add(krow);

            List<SentinelCore.GovernorState> govs = null;
            try { govs = SentinelCore.AllGovernorStates(); } catch (Exception _sx) { SentinelCore.Swallow("SentinelCockpit.RefreshGate", _sx); }
            if (govs == null || govs.Count == 0)
            {
                _gateBody.Children.Add(Mono("no accounts governed — add profiles in the Dashboard ▸ Accounts tab", Muted, 9.5, new Thickness(0, 4, 0, 0), true));
                return;
            }
            govs.Sort((a, b) => string.Compare(a.Account, b.Account, StringComparison.OrdinalIgnoreCase));

            foreach (var g in govs)
            {
                if (g == null) continue;
                _gateBody.Children.Add(new Border { Height = 1, Background = Faint, Margin = new Thickness(0, 7, 0, 7) });

                var top = new StackPanel { Orientation = Orientation.Horizontal };
                top.Children.Add(Mono(g.Account, Text, 12, new Thickness(0, 0, 8, 0)));
                Brush sc = g.Status == "DayHalted" ? Red : (g.Status == "DayComplete" ? Amber : Green);
                top.Children.Add(Chip(StatusWord(g.Status).ToUpperInvariant(), sc));
                var day = new TextBlock { Text = "   " + Money(g.DailyPnl), Foreground = g.DailyPnl >= 0 ? Green : Red,
                    FontFamily = new FontFamily("Consolas"), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
                top.Children.Add(day);
                _gateBody.Children.Add(top);

                // day → cap track
                if (g.Cap > 0)
                {
                    double capFrac = g.DailyPnl > 0 ? g.DailyPnl / g.Cap : 0;
                    var cr = KvTrack("day → cap", capFrac, Green, Money(g.DailyPnl) + " / " + Money(g.Cap));
                    _gateBody.Children.Add(cr);
                }
                if (g.LossStop > 0)
                {
                    double lossFrac = g.DailyPnl < 0 ? (-g.DailyPnl) / g.LossStop : 0;
                    var lr = KvTrack("loss stop", lossFrac, Red, "−" + Money(g.LossStop));
                    _gateBody.Children.Add(lr);
                }
                if (!g.Allowed && !string.IsNullOrEmpty(g.Reason))
                    _gateBody.Children.Add(Mono(g.Reason, Muted, 9, new Thickness(0, 3, 0, 0), true));
            }
        }

        // ③ CONTEXT — the MODULATORS (why conviction is damped). These do not vote; they scale/veto.
        //    (Council split, kept faithful: modulators = Clock · Participation · MTF · Location.)
        private void RefreshContext()
        {
            if (_contextBody == null) return;
            _contextBody.Children.Clear();

            string inst = InstNameForSeams();   // v1.15.0: bare instrument — these seams are not scope-keyed
            if (string.IsNullOrEmpty(inst)) { _contextBody.Children.Add(Mono("— pick an instrument above", Muted, 10, null, true)); return; }
            int bias = _lastCouncil != null ? _lastCouncil.Bias : 0;
            int shown = 0;

            var ck = Safe(() => SentinelCore.GetClockState(inst, BigAge));
            shown += SeamRow(_contextBody, "CLOCK", ck == null ? null
                    : PhaseName(ck.Phase) + (ck.InSession ? "  " + ck.MinsToClose + "m→close" : "  closed") + (ck.InKillWindow ? "  KILL-WIN" : ""),
                    (ck == null ? (DateTime?)null : ck.UpdatedUtc), SlowStale,
                    0, false, bias, false, (ck != null && ck.InKillWindow) ? Amber : null) ? 1 : 0;

            var pa = Safe(() => SentinelCore.GetParticipationState(inst, BigAge));
            shown += SeamRow(_contextBody, "PARTIC", pa == null ? null
                    : "rvol " + F(pa.Rvol, "0.00") + "  z" + F(pa.VolZ, "+0.0;-0.0;0.0") + (pa.Climax ? "  CLIMAX" : "") + (pa.DryUp ? "  DRY-UP" : ""),
                    (pa == null ? (DateTime?)null : pa.UpdatedUtc), SeamStale,
                    0, false, bias, false, (pa != null && (pa.DryUp || pa.Climax)) ? Amber : null) ? 1 : 0;

            // MTF is a MODULATOR (counter-higher-TF trades get damped) — but it IS directional, so the agree
            // mark is the fastest read of "am I fighting the higher timeframes?"
            var mt = Safe(() => SentinelCore.GetMtfState(inst, BigAge));
            shown += SeamRow(_contextBody, "MTF", mt == null ? null
                    : BiasWord(mt.Bias) + "  " + mt.AlignedCount + "/" + mt.TfCount + (string.IsNullOrEmpty(mt.Dirs) ? "" : "  " + mt.Dirs),
                    (mt == null ? (DateTime?)null : mt.UpdatedUtc), SlowStale,
                    mt == null ? 0 : mt.Bias, true, bias, true, null) ? 1 : 0;

            var lv = Safe(() => SentinelCore.GetLevelState(inst, BigAge));
            shown += SeamRow(_contextBody, "LOCATN", lv == null ? null
                    : (string.IsNullOrEmpty(lv.NearestName) ? "—" : lv.NearestName) + " " + Tk(lv.NearestDistTicks)
                      + "  vwap " + (lv.VwapSide > 0 ? "above" : (lv.VwapSide < 0 ? "below" : "—")),
                    (lv == null ? (DateTime?)null : lv.UpdatedUtc), SlowStale,
                    0, false, bias, false, null) ? 1 : 0;

            if (shown == 0)
                _contextBody.Children.Add(Mono(_hideAbsent ? "— none loaded (⊘ hiding absent seams)" : "— no context seams loaded", Muted, 9.5, null, true));
        }

        // ④ VOTERS — the confluence, unburied. Ten voters exactly as the Council fuses them
        //    (Eye · Trend · CCI · ADX · VolEnvelope · Brick · Compression · Intermarket · WAE · GodReversal),
        //    then the Liquidity-walls VETO. Weights are the Council indicator's params (not published on the
        //    seam), so they are deliberately NOT shown here rather than guessed.
        private void RefreshVoters()
        {
            if (_votersBody == null) return;
            _votersBody.Children.Clear();

            string inst = InstNameForSeams();   // v1.15.0: bare instrument — these seams are not scope-keyed
            if (string.IsNullOrEmpty(inst)) { _votersBody.Children.Add(Mono("— pick an instrument above", Muted, 10, null, true)); return; }
            var cs = _lastCouncil;
            int bias = cs != null ? cs.Bias : 0;
            int shown = 0;

            var ey = Safe(() => SentinelCore.GetEyeVerdict(inst, BigAge));
            shown += SeamRow(_votersBody, "EYE", ey == null ? null : "score " + F(ey.Score, "0.00") + (string.IsNullOrEmpty(ey.Source) ? "" : "  " + ey.Source),
                    (ey == null ? (DateTime?)null : ey.UpdatedUtc), SeamStale, ey == null ? 0 : ey.Direction, true, bias, true, null) ? 1 : 0;

            var tr = Safe(() => SentinelCore.GetTrendState(inst, BigAge));
            shown += SeamRow(_votersBody, "TREND", tr == null ? null : "bars " + tr.BarsInTrend + "  " + Tk(tr.DistanceTicks) + (tr.Flipped ? "  FLIP" : ""),
                    (tr == null ? (DateTime?)null : tr.UpdatedUtc), SeamStale, tr == null ? 0 : tr.Direction, true, bias, true, null) ? 1 : 0;

            var cc = Safe(() => SentinelCore.GetCciState(inst, BigAge));
            shown += SeamRow(_votersBody, "CCI", cc == null ? null : "cci " + F(cc.MainCci, "0") + (cc.Strong ? "  strong" : "") + (cc.Weakening ? "  weakening" : ""),
                    (cc == null ? (DateTime?)null : cc.UpdatedUtc), SeamStale, cc == null ? 0 : cc.Bias, true, bias, true,
                    (cc != null && cc.Weakening) ? Amber : null) ? 1 : 0;

            var ad = Safe(() => SentinelCore.GetAdxState(inst, BigAge));
            shown += SeamRow(_votersBody, "ADX", ad == null ? null : "adx " + F(ad.Adx, "0.0") + (ad.Strong ? "  strong" : "") + (ad.Building ? "  bldg" : "  fade"),
                    (ad == null ? (DateTime?)null : ad.UpdatedUtc), SeamStale, ad == null ? 0 : ad.Bias, true, bias, true, null) ? 1 : 0;

            var en = Safe(() => SentinelCore.GetEnvelopeState(inst, BigAge));
            shown += SeamRow(_votersBody, "ENV", en == null ? null : RegimeName(en.Regime) + "  stretch " + F(en.Stretch, "+0.0;-0.0;0.0"),
                    (en == null ? (DateTime?)null : en.UpdatedUtc), SeamStale, en == null ? 0 : RegimeDir(en.Regime), true, bias, true,
                    (en != null && en.IsSqueeze) ? Amber : null) ? 1 : 0;

            var bk = Safe(() => SentinelCore.GetBrickState(inst, BigAge));
            shown += SeamRow(_votersBody, "BRICK", bk == null ? null : "run " + bk.SameDirCount + "  next " + F(bk.NearestTicksRemaining, "0") + "t" + (bk.PendingBreakout ? "  pending" : ""),
                    (bk == null ? (DateTime?)null : bk.UpdatedUtc), SeamStale, bk == null ? 0 : bk.Direction, true, bias, true, null) ? 1 : 0;

            var cp = Safe(() => SentinelCore.GetCompressionState(inst, BigAge));
            shown += SeamRow(_votersBody, "COMP", cp == null ? null : "coil " + F(cp.Coil, "0.00") + (cp.Compressed ? "  compressed" : "") + (cp.Armed ? "  ARMED" : ""),
                    (cp == null ? (DateTime?)null : cp.UpdatedUtc), SeamStale, cp == null ? 0 : cp.BreakDir, true, bias, true,
                    (cp != null && cp.Armed) ? Accent : null) ? 1 : 0;

            var im = Safe(() => SentinelCore.GetIntermarketState(inst, BigAge));
            shown += SeamRow(_votersBody, "INTMKT", im == null ? null : "score " + F(im.Score, "+0.00;-0.00;0.00") + (string.IsNullOrEmpty(im.Refs) ? "" : "  " + im.Refs),
                    (im == null ? (DateTime?)null : im.UpdatedUtc), SlowStale, im == null ? 0 : im.Lean, true, bias, true, null) ? 1 : 0;

            var wa = Safe(() => SentinelCore.GetWaeState(inst, BigAge));
            shown += SeamRow(_votersBody, "WAE", wa == null ? null : "power " + F(wa.Power, "0.0") + (wa.IsExploding ? "  EXPLODING" : "  quiet"),
                    (wa == null ? (DateTime?)null : wa.UpdatedUtc), SeamStale, wa == null ? 0 : wa.Signal, true, bias, true,
                    (wa != null && wa.IsExploding) ? Accent : null) ? 1 : 0;

            // GREV is a MEAN-REVERSION voice — it often dissents from the trend voters BY DESIGN. A ✗ here is
            // not a malfunction; doctrine §7 uses it as an entry TRIGGER alongside the bias, not a swing vote.
            var gr = Safe(() => SentinelCore.GetGodReversalState(inst, BigAge));
            shown += SeamRow(_votersBody, "GREV", gr == null ? null
                    : "q " + F(gr.Quality, "0.00") + (string.IsNullOrEmpty(gr.Setup) ? "" : "  " + gr.Setup) + (gr.AtBand ? "  at-band" : "") + "  (mean-rev)",
                    (gr == null ? (DateTime?)null : gr.UpdatedUtc), SeamStale, gr == null ? 0 : gr.Dir, true, bias, true, null) ? 1 : 0;

            // ── the VETO (not a vote): liquidity walls ──
            _votersBody.Children.Add(new Border { Height = 1, Background = Faint, Margin = new Thickness(0, 4, 0, 6) });
            var lq = Safe(() => SentinelCore.GetLiquidityState(inst, BigAge));
            bool wallVeto = cs != null && cs.Vetoed && !string.IsNullOrEmpty(cs.VetoReason)
                            && cs.VetoReason.IndexOf("wall", StringComparison.OrdinalIgnoreCase) >= 0;
            SeamRow(_votersBody, "LIQ", lq == null ? null
                    : "walls " + lq.ActiveWalls + "  ↑" + Dist(lq.DistAboveTicks) + "  ↓" + Dist(lq.DistBelowTicks) + (wallVeto ? "  VETO" : ""),
                    (lq == null ? (DateTime?)null : lq.UpdatedUtc), SeamStale, 0, false, bias, false, wallVeto ? Red : null);

            if (shown == 0)
                _votersBody.Children.Add(Mono(_hideAbsent ? "— none loaded (⊘ hiding absent seams)" : "— no voter seams loaded", Muted, 9.5, null, true));

            // Cross-check line: what the COUNCIL itself counted (its tally is authoritative — the Cockpit only
            // re-reads the same seams and may classify freshness on a slightly different clock).
            if (cs != null)
                _votersBody.Children.Add(Mono("Council counted " + cs.Voters + " fresh  ·  ▲" + cs.Agree + " ▼" + cs.Disagree,
                    Muted, 9, new Thickness(0, 6, 0, 0)));
            _votersBody.Children.Add(Mono("most voters are price-derived — agreement ≠ confirmation", Muted, 8.5, new Thickness(0, 3, 0, 0), true));
        }

        // ── one seam row: [dot] NAME [▲▼~] detail ......... [✓/✗] ─────────────────
        //  updated == null  → ABSENT (never published) — visually distinct from STALE, which is the whole point.
        private bool SeamRow(StackPanel host, string name, string detail, DateTime? updated, double staleSec,
                             int dir, bool showArrow, int councilBias, bool showAgree, Brush detailTint)
        {
            bool absent = !updated.HasValue;
            if (absent && _hideAbsent) return false;

            double age = 0; bool stale = false;
            if (!absent) { age = (DateTime.UtcNow - updated.Value).TotalSeconds; stale = age > staleSec; }

            var g = new Grid { Margin = new Thickness(0, 0, 0, 5) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                       // dot
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });                    // name
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(13) });                    // arrow
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });  // detail
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                       // agree

            var dot = Dot(absent ? Faint : (stale ? Amber : Green));
            dot.Width = 6; dot.Height = 6; dot.CornerRadius = new CornerRadius(3);
            dot.VerticalAlignment = VerticalAlignment.Center; dot.Margin = new Thickness(0, 0, 7, 0);
            Grid.SetColumn(dot, 0); g.Children.Add(dot);

            var nm = Mono(name, absent ? Muted : Ink2, 10); nm.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(nm, 1); g.Children.Add(nm);

            if (showArrow && !absent)
            {
                var ar = Mono(Arrow(dir), dir > 0 ? Green : (dir < 0 ? Red : Muted), 10);
                ar.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(ar, 2); g.Children.Add(ar);
            }

            string dtxt = absent ? "— not loaded" : ((detail ?? "") + (stale ? "   stale " + ((int)age) + "s" : ""));
            var dt = Mono(dtxt, absent ? Muted : (stale ? Muted : (detailTint ?? Ink2)), 9.5);
            dt.VerticalAlignment = VerticalAlignment.Center;
            dt.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(dt, 3); g.Children.Add(dt);

            if (showAgree && !absent && councilBias != 0 && dir != 0)
            {
                bool agree = (dir > 0) == (councilBias > 0);
                var mk = Mono(agree ? "✓" : "✗", agree ? Accent : Muted, 10);
                mk.VerticalAlignment = VerticalAlignment.Center; mk.Margin = new Thickness(6, 0, 0, 0);
                mk.ToolTip = agree ? "agrees with the Council bias" : "dissents from the Council bias";
                Grid.SetColumn(mk, 4); g.Children.Add(mk);
            }

            host.Children.Add(g);
            return true;
        }

        // ── small WPF builders ──────────────────────────────────────────────────
        // ═══════════════════════════════════════════════════════════════════════
        //  ⑤ HELM — interdict a RUNNING actor (grab the wheel without stopping the car).
        //  Buttons PUBLISH a HelmIntent (SentinelCore.SetHelmIntent) to the actor's instanceKey; the actor (a Sentinel
        //  Bridge with 'Obey Helm' on) executes it with its own order handles. Risk-reducing verbs are fail-open on the
        //  consumer; risk-adding (Resume/widen/HandBack) pass its GateEntry. This surface never touches an order.
        //  Persistent controls: RefreshHelm updates only the readout + dot + target key, so a typed price is not wiped.
        // ═══════════════════════════════════════════════════════════════════════
        private StackPanel BuildHelmSection()
        {
            var p = new StackPanel();

            // target line + freshness dot
            var hdr = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            _helmDot = Dot(Faint); _helmDot.VerticalAlignment = VerticalAlignment.Center; _helmDot.Margin = new Thickness(0, 0, 7, 0);
            hdr.Children.Add(_helmDot);
            _helmTargetTb = Mono("— no actor", Muted, 10);
            _helmTargetTb.VerticalAlignment = VerticalAlignment.Center;
            _helmTargetTb.TextTrimming = TextTrimming.CharacterEllipsis;
            hdr.Children.Add(_helmTargetTb);
            p.Children.Add(hdr);

            _helmPosTb = Mono("—", Ink2, 11, new Thickness(0, 0, 0, 7), true);
            p.Children.Add(_helmPosTb);

            // Row A — primary reducers (fail-open on the consumer)
            var rowA = new WrapPanel { Margin = new Thickness(0, 0, 0, 5) };
            rowA.Children.Add(Btn("PAUSE",   Amber, (s, e) => SendHelm(SentinelCore.HelmVerb.Pause,      0, 0)));
            rowA.Children.Add(Btn("RESUME",  Green, (s, e) => SendHelm(SentinelCore.HelmVerb.Resume,     0, 0)));
            rowA.Children.Add(Btn("SKIP",    Ink2,  (s, e) => SendHelm(SentinelCore.HelmVerb.SkipNext,   0, 0)));
            rowA.Children.Add(Btn("FLATTEN", Red,   (s, e) => SendHelm(SentinelCore.HelmVerb.FlattenNow, 0, 0)));
            p.Children.Add(rowA);

            // Row B — bracket: type a price, then Stop→ / Tgt→ ; BE needs no price
            var rowB = new WrapPanel { Margin = new Thickness(0, 0, 0, 5) };
            _helmPrice = new TextBox { Width = 64, FontFamily = new FontFamily("Consolas"), FontSize = 11,
                Margin = new Thickness(0, 0, 7, 0), VerticalContentAlignment = VerticalAlignment.Center,
                Background = Card, Foreground = Text, BorderBrush = Edge, ToolTip = "Price for Stop→ / Tgt→" };
            rowB.Children.Add(_helmPrice);
            rowB.Children.Add(Btn("stop→", Accent, (s, e) => SendHelmPrice(SentinelCore.HelmVerb.MoveStop)));
            rowB.Children.Add(Btn("tgt→",  Accent, (s, e) => SendHelmPrice(SentinelCore.HelmVerb.MoveTarget)));
            rowB.Children.Add(Btn("BE",    Green,  (s, e) => SendHelm(SentinelCore.HelmVerb.BreakevenNow, 0, 0)));
            p.Children.Add(rowB);

            // Row C — scale DOWN (reduce N) + ownership stand-down / resume
            var rowC = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            _helmQty = new TextBox { Width = 32, Text = "1", FontFamily = new FontFamily("Consolas"), FontSize = 11,
                Margin = new Thickness(0, 0, 7, 0), VerticalContentAlignment = VerticalAlignment.Center,
                Background = Card, Foreground = Text, BorderBrush = Edge, ToolTip = "Contracts to reduce (scale-down)" };
            rowC.Children.Add(_helmQty);
            rowC.Children.Add(Btn("reduce",    Amber, (s, e) => SendHelmScale()));
            rowC.Children.Add(Btn("take over", Amber, (s, e) => SendHelm(SentinelCore.HelmVerb.TakeOver, 0, 0)));
            rowC.Children.Add(Btn("hand back", Green, (s, e) => SendHelm(SentinelCore.HelmVerb.HandBack, 0, 0)));
            p.Children.Add(rowC);

            _helmActionTb = Mono("", Accent, 9, new Thickness(0, 2, 0, 0), true);
            p.Children.Add(_helmActionTb);

            return p;
        }

        // Update the readout for the actor targeting the current instrument (prefer an exact scope match). Never
        // rebuilds the controls (so the price box keeps focus/text); only sets labels + the target key.
        private void RefreshHelm()
        {
            if (_helmBody == null || _helmTargetTb == null) return;
            string inst = CurrentInstrument();
            string instBare = string.IsNullOrEmpty(inst) ? "" : (inst.IndexOf('.') >= 0 ? inst.Substring(0, inst.IndexOf('.')) : inst);

            SentinelCore.HelmState best = null;
            try
            {
                var all = SentinelCore.AllHelmStates();
                if (all != null)
                    foreach (var hs in all)
                    {
                        if (hs == null) continue;
                        bool scopeMatch = !string.IsNullOrEmpty(_seamKey) && string.Equals(hs.Scope, _seamKey, StringComparison.OrdinalIgnoreCase);
                        bool instMatch  = !string.IsNullOrEmpty(instBare) && string.Equals(hs.Instrument, instBare, StringComparison.OrdinalIgnoreCase);
                        if (scopeMatch) { best = hs; break; }
                        if (instMatch && best == null) best = hs;
                    }
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelCockpit.RefreshHelm", _sx); }

            if (best == null)
            {
                _helmKey = null;
                _helmDot.Background = Faint;
                _helmTargetTb.Text = "— no interdictable actor" + (string.IsNullOrEmpty(instBare) ? "" : " on " + instBare);
                _helmTargetTb.Foreground = Muted;
                _helmPosTb.Text = "arm a Sentinel Bridge with 'Obey Helm intents' on";
                _helmPosTb.Foreground = Muted;
                return;
            }

            _helmKey = best.InstanceKey;
            double age = (DateTime.UtcNow - best.UpdatedUtc).TotalSeconds;
            _helmDot.Background = age < 6 ? Green : (age < 30 ? Amber : Red);
            _helmTargetTb.Text = ShortKey(best.InstanceKey) + "  ·  " + (best.Status ?? "");
            _helmTargetTb.Foreground = best.HumanOverride ? Amber : Ink2;

            string pos;
            if (best.PositionQty == 0) pos = "flat";
            else
            {
                pos = (best.PositionQty > 0 ? "LONG " : "SHORT ") + Math.Abs(best.PositionQty)
                    + " @ " + best.AvgPrice.ToString("0.#####", CultureInfo.InvariantCulture);
                if (best.StopPrice   > 0) pos += "  · stop " + best.StopPrice.ToString("0.#####", CultureInfo.InvariantCulture);
                if (best.TargetPrice > 0) pos += "  · tgt " + best.TargetPrice.ToString("0.#####", CultureInfo.InvariantCulture);
            }
            if (best.Paused)        pos += "   ⏸ PAUSED";
            if (best.HumanOverride) pos += "   ✋ override";
            _helmPosTb.Text = pos;
            _helmPosTb.Foreground = best.Paused ? Amber : (best.PositionQty != 0 ? Text : Ink2);

            // expire the transient action feedback
            if (_helmActionTb != null && !string.IsNullOrEmpty(_helmActionTb.Text)
                && (DateTime.UtcNow - _lastHelmActionUtc).TotalSeconds > 6)
                _helmActionTb.Text = "";
        }

        private void SendHelm(SentinelCore.HelmVerb verb, double price, int qtyDelta)
        {
            if (string.IsNullOrEmpty(_helmKey)) { SetHelmStatus("no actor to command", Red); return; }
            try
            {
                SentinelCore.SetHelmIntent(_helmKey, new SentinelCore.HelmIntent
                    { Verb = verb, Price = price, QtyDelta = qtyDelta, Reason = "cockpit" });
                SetHelmStatus(verb.ToString() + " sent → " + ShortKey(_helmKey), Accent);
            }
            catch (Exception ex) { SetHelmStatus("send failed: " + ex.Message, Red); }
        }

        private void SendHelmPrice(SentinelCore.HelmVerb verb)
        {
            double px;
            if (_helmPrice == null || !double.TryParse((_helmPrice.Text ?? "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out px) || px <= 0)
            { SetHelmStatus("enter a valid price first", Red); return; }
            SendHelm(verb, px, 0);
        }

        private void SendHelmScale()
        {
            int q;
            if (_helmQty == null || !int.TryParse((_helmQty.Text ?? "").Trim(), out q) || q <= 0)
            { SetHelmStatus("enter contracts to reduce", Red); return; }
            SendHelm(SentinelCore.HelmVerb.Scale, 0, -q);   // negative delta = reduce (scale-up is refused by the consumer)
        }

        private void SetHelmStatus(string msg, Brush col)
        {
            _lastHelmActionUtc = DateTime.UtcNow;
            if (_helmActionTb != null) { _helmActionTb.Text = msg; _helmActionTb.Foreground = col; }
        }

        private static string ShortKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            return key.StartsWith("Sentinel", StringComparison.Ordinal) ? key.Substring("Sentinel".Length) : key;
        }

        private TextBlock Mono(string t, Brush col, double size, Thickness? m = null, bool wrap = false)
        {
            var tb = new TextBlock { Text = t, Foreground = col, FontFamily = new FontFamily("Consolas"), FontSize = size };
            if (m.HasValue) tb.Margin = m.Value;
            if (wrap) tb.TextWrapping = TextWrapping.Wrap;
            return tb;
        }

        private Border Pill(string t, Brush col)
        {
            return new Border
            {
                CornerRadius = new CornerRadius(6), Background = Tint(col, 0.16),
                BorderBrush = Tint(col, 0.45), BorderThickness = new Thickness(1),
                Padding = new Thickness(9, 2, 9, 2),
                Child = new TextBlock { Text = t, Foreground = col, FontFamily = new FontFamily("Consolas"),
                    FontSize = 12, FontWeight = FontWeights.Bold }
            };
        }

        private Border Chip(string t, Brush col)
        {
            return new Border
            {
                CornerRadius = new CornerRadius(5), BorderBrush = Tint(col, 0.42), BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 1, 6, 1), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = t, Foreground = col, FontFamily = new FontFamily("Consolas"), FontSize = 9 }
            };
        }

        private Border Dot(Brush col)
        {
            return new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = col };
        }

        private Border WhyBox(string t, Brush col)
        {
            return new Border
            {
                CornerRadius = new CornerRadius(7), Background = Tint(col, 0.12),
                BorderBrush = Tint(col, 0.35), BorderThickness = new Thickness(1),
                Padding = new Thickness(9, 6, 9, 6), Margin = new Thickness(0, 10, 0, 0),
                Child = new TextBlock { Text = t, Foreground = col, FontFamily = new FontFamily("Consolas"),
                    FontSize = 11, TextWrapping = TextWrapping.Wrap }
            };
        }

        // a horizontal fractional bar via star columns (no pixel-width needed)
        private FrameworkElement Bar(double frac, Brush fill, double height, Thickness margin)
        {
            frac = Math.Max(0, Math.Min(1, frac));
            var outer = new Border { Height = height, CornerRadius = new CornerRadius(height / 2), Background = Faint, Margin = margin };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(frac, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - frac, GridUnitType.Star) });
            var f = new Border { CornerRadius = new CornerRadius(height / 2), Background = fill };
            Grid.SetColumn(f, 0); grid.Children.Add(f);
            outer.Child = grid;
            return outer;
        }

        private FrameworkElement KvTrack(string label, double frac, Brush fill, string right)
        {
            var g = new Grid { Margin = new Thickness(0, 5, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(66) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var k = Mono(label, Muted, 10); k.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(k, 0); g.Children.Add(k);
            var bar = Bar(frac, fill, 5, new Thickness(0)); bar.VerticalAlignment = VerticalAlignment.Center;
            ((Border)bar).MaxWidth = 130; Grid.SetColumn(bar, 1); g.Children.Add(bar);
            var r = Mono(right, Muted, 9.5); r.VerticalAlignment = VerticalAlignment.Center; r.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(r, 2); g.Children.Add(r);
            return g;
        }

        // ── seam helpers ──────────────────────────────────────────────────────
        /// <summary>Read a seam without letting one bad publisher take the whole window down.</summary>
        private static T Safe<T>(Func<T> f) where T : class { try { return f(); } catch { return null; } }

        private static string Arrow(int dir) { return dir > 0 ? "▲" : (dir < 0 ? "▼" : "~"); }
        private static string BiasWord(int b) { return b > 0 ? "long" : (b < 0 ? "short" : "flat"); }

        private static string F(double v, string fmt)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "—";
            return v.ToString(fmt, CultureInfo.InvariantCulture);
        }
        /// <summary>Signed tick distance, or an em-dash when the publisher had nothing (NaN).</summary>
        private static string Tk(double ticks)
        {
            if (double.IsNaN(ticks) || double.IsInfinity(ticks)) return "—";
            return ticks.ToString("+0;-0;0", CultureInfo.InvariantCulture) + "t";
        }
        /// <summary>Unsigned wall distance ("—" when there is no wall on that side).</summary>
        private static string Dist(double ticks)
        {
            if (double.IsNaN(ticks) || double.IsInfinity(ticks)) return "—";
            return Math.Round(ticks).ToString("0", CultureInfo.InvariantCulture) + "t";
        }

        private static string PhaseName(int p)
        {
            switch (p)
            {
                case 1:  return "open-drive";
                case 2:  return "midday";
                case 3:  return "close";
                default: return "pre-open";
            }
        }
        // EnvelopeState.Regime travels as an INT: 0=Squeeze 1=Range 2=TrendUp 3=TrendDown 4=Expansion
        private static string RegimeName(int r)
        {
            switch (r)
            {
                case 0:  return "squeeze";
                case 1:  return "range";
                case 2:  return "trend-up";
                case 3:  return "trend-dn";
                case 4:  return "expansion";
                default: return "?";
            }
        }
        private static int RegimeDir(int r) { return r == 2 ? 1 : (r == 3 ? -1 : 0); }

        // ── helpers ───────────────────────────────────────────────────────────
        private static string StatusWord(string s)
        {
            if (s == "DayComplete") return "complete";
            if (s == "DayHalted")   return "halted";
            return string.IsNullOrEmpty(s) ? "trading" : s.ToLowerInvariant();
        }
        private static string Pct(double f) { return Math.Round(f * 100).ToString(CultureInfo.InvariantCulture) + "%"; }
        private static string Money(double v) { return (v >= 0 ? "$" : "−$") + Math.Abs(Math.Round(v)).ToString("#,0", CultureInfo.InvariantCulture); }

        private static Brush Tint(Brush b, double a)
        {
            var s = b as SolidColorBrush; if (s == null) return b;
            var c = s.Color; var t = new SolidColorBrush(Color.FromArgb((byte)Math.Round(a * 255), c.R, c.G, c.B)); t.Freeze(); return t;
        }
        private static Brush MixEdge(Brush accent)
        {
            var s = accent as SolidColorBrush; if (s == null) return Edge;
            var c = s.Color; var t = new SolidColorBrush(Color.FromArgb(0x70, c.R, c.G, c.B)); t.Freeze(); return t;
        }

        // ── Cockpit.conf persistence (instrument + pin) ─────────────────────────
        private void LoadConf()
        {
            try
            {
                string path = System.IO.Path.Combine(SentinelCore.SettingsDir, "Cockpit.conf");
                if (!System.IO.File.Exists(path)) return;
                foreach (string raw in System.IO.File.ReadAllLines(path))
                {
                    string line = (raw ?? "").Trim();
                    int e = line.IndexOf('=');
                    if (e <= 0) continue;
                    string k = line.Substring(0, e).Trim(), v = line.Substring(e + 1).Trim();
                    if (k.Equals("pin", StringComparison.OrdinalIgnoreCase)) _pinned = v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
                    else if (k.Equals("instrument", StringComparison.OrdinalIgnoreCase)) _pendingInstr = v;
                    else if (k.Equals("hideAbsent", StringComparison.OrdinalIgnoreCase)) _hideAbsent = v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
                    else if (k.Equals("collapsed", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (string key in v.Split(','))
                            if (!string.IsNullOrEmpty(key.Trim())) _collapsedKeys.Add(key.Trim());
                    }
                }
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelCockpit.LoadConf", _sx); }
        }
        private string _pendingInstr;

        private void SaveConf()
        {
            try
            {
                string path = System.IO.Path.Combine(SentinelCore.SettingsDir, "Cockpit.conf");
                string inst = CurrentInstrument();
                var collapsed = new List<string>();
                foreach (var s in _sections) if (s.Collapsed) collapsed.Add(s.Key);
                System.IO.File.WriteAllText(path,
                    "# Sentinel Cockpit — remembered UI state\r\n" +
                    "instrument=" + inst + "\r\n" +
                    "pin=" + (_pinned ? "1" : "0") + "\r\n" +
                    "hideAbsent=" + (_hideAbsent ? "1" : "0") + "\r\n" +
                    "collapsed=" + string.Join(",", collapsed.ToArray()) + "\r\n");
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelCockpit.SaveConf", _sx); }
        }
    }
}
