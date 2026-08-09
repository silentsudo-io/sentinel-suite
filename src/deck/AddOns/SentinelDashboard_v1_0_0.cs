// ─────────────────────────────────────────────────────────────────────────────
// This Source Code Form is subject to the terms of the Mozilla Public License,
// v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
// one at https://mozilla.org/MPL/2.0/.
//
// Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
// ─────────────────────────────────────────────────────────────────────────────
// ═════════════════════════════════════════════════════════════════════════════
//  SentinelDashboard — unified config/control window for the Sentinel Suite (NT8)
//  File: SentinelDashboard_v1_0_0.cs
//  Version: v1.1.9   (Accounts tab exposes the FULL governor — manual cap / reset hour / trailing DD / auto-flatten — no conf editing)
// ─────────────────────────────────────────────────────────────────────────────
//  WHAT THIS IS  (see Docs/ROADMAP.md, memory: sentinel-suite-architecture)
//    The ONE window for the whole suite. Adds "Sentinel Suite" under Control Center > New.
//    A TabControl with one tab per Sentinel tool; each tab ATTACHES to that tool's headless
//    service singleton (e.g. SentinelCopierService_v0_2_0.Instance) — it does NOT own the
//    service, and closing this window never stops any service. Same attach pattern as
//    MAEDashboard → MAECaptureService.
//
//    A shared TOP BAR carries the suite-wide KILL-SWITCH (SentinelCore) and live status,
//    visible from every tab.
//
//  VERIFIED SCAFFOLDING (from in-repo BuySellVolumePressureDashboardAddOnV001 + MAEDashboard):
//    • Menu: OnWindowCreated(Window) → (window as ControlCenter).FindFirst("ControlCenterMenuItemNew")
//      as NTMenuItem; add an NTMenuItem child; open the window from Core.Globals.RandomDispatcher.
//    • Window derives from NTWindow (NinjaTrader.Gui.Tools); set Caption/Width/Height/Content.
//    • UI built in code (Grid/StackPanel/Border/TextBlock/Button/ComboBox/TextBox/CheckBox).
//    • Cross-thread brushes are Freeze()'d; background→UI updates via Dispatcher.InvokeAsync.
//    • Unsubscribe every handler in the Closed event.
//
//  CHANGELOG
//    v1.1.9 — (in-place) Accounts tab now exposes the FULL governor so nothing needs hand-editing Profiles.conf:
//             added Manual daily cap $ (0=R×target), Daily reset hour 0–23 (global — when the day rolls; 17=5pm
//             prop-firm), Trailing DD $ + DD type (trailing/static/eod) + DD flatten buffer $, and an Auto-flatten
//             (hardEnforce) checkbox. Save now MERGES into the account's existing conf line (preserves any field the
//             editor doesn't manage — previously a Save rebuilt the line from scratch and could silently wipe e.g.
//             ddFlat). ddFlat/resetHour aren't on AccountProfile so they load from the raw conf line / global state.
//    v1.1.8 — (in-place) EXCURSION TAB REDESIGN (was an early, cluttered single column): now a two-column
//             master/detail that uses the window width — compact header (controls · plain-language status
//             "N records · N signals · N files" w/ path tooltip · tiles · live configs), then LEFT = the ranked
//             edge list (clickable rows drive the detail; selected row highlights cyan + hover), RIGHT = the
//             selected signal's deep-dive (growth · outcome cloud · TP/SL grid · referees · Apply ◆). Removed the
//             redundant "Signal → excursion medians" wall-of-text table (duplicated the chart+detail) and the
//             long footer paragraph. The detail charts (growth line · outcome cloud · TP/SL grid) are now
//             RESPONSIVE — a ResponsiveHost redraws each at the right column's live width (fills the column,
//             tracks window resize; the right column constrains to viewport). No analytics lost — presentation only.
//    v1.1.7 — (in-place) new HOME tab (front page; first tab, selected on open). First item: a RED FOLDER NEWS
//             readout of the economic-calendar event veto — reads Sentinel\News.conf (written by the native
//             SentinelNewsService) directly, re-parsing only on mtime change but recomputing countdowns every
//             tick: tiles (protection LOCKED/CLEAR/STALE · next event countdown · windows loaded), the upcoming
//             red-folder schedule (local time + countdown + window, the active lockout highlighted), and a footer
//             (feeder status + News.conf freshness + source line). "Red folder" = impact:High from the feed — the
//             service already filters to HIGH, so News.conf IS the red-folder list. Room to grow more front-page items.
//    v1.1.6 — (in-place) Accounts tab clarity: GovernorCard shows live OPEN (unrealized) P&L inline + a sub-line
//             with the account's RAW realized ("matches NT" — g.DailyPnl is realized-SINCE-a-baseline for the
//             consistency rule, which reads ~0 after mid-day F5s; the raw figure mirrors NT's column so the card
//             is never confusingly blank). "Live P&L · today" tile now folds in open P&L. Pairs with
//             SentinelRiskService v1.0.8 persisting the governor daily baseline (survives F5/restart).
//    v1.1.5 — (in-place) Excursion tab: new ⑤ CONVICTION REFEREE in the per-signal detail — for COUNCIL
//             groups only, shows HIGH/MID/LOW conviction buckets (from SentinelExcursions v1.0.5 ByConviction)
//             + a "does conviction pay?" verdict (ConvictionVerdictCode) that names the SentinelBridge
//             MinConviction floor to set. The COUNCIL group + Apply ◆ (→ <inst>_COUNCIL_<dir>.conf) already
//             worked via the generic by-signal code. Pairs with SentinelExcursionRecorder_v1_4 (schema 1.2).
//    v1.1.4 — (in-place) MORE VISUALS — reusable WPF chart primitives (dataviz method + Sentinel palette:
//             cyan=magnitude, green/red=money+polarity, no rainbow categorical) added near Track(): HBars
//             (horizontal magnitude), HDivBars (diverging around a center baseline, Canvas-positioned) and
//             Columns (vertical time histogram). Marks per spec: thin bars, rounded DATA end (square at the
//             baseline), recessive Faint/Edge gridlines, values/labels in TEXT tokens (never the bar hue).
//             Applied: SLIPPAGE = avg-slip-per-instrument diverging bars (red adverse / green improvement);
//             JOURNAL = activity histogram (events per hour today / per day for a window); LENS = net-ticks
//             diverging bars per strategy + instrument; EYE = signed-score diverging bars (green long / red
//             short); ARC = day-P&L per fleet slot; RISK = data-lag per feed (green/amber/red by threshold);
//             ACCOUNTS = fleet day-P&L per governed account. Charts sit ABOVE the existing text rows (chart +
//             table, per the accessibility rule). SignedBars auto-picks MAGNITUDE bars (full width) for
//             one-sided data and DIVERGING only when signs are mixed, so a chart never wastes half its width.
//    v1.1.3 — (in-place) new TEST tab — the "prove the safety system" surface: (1) ALERT CHANNEL config
//             (Enabled / Play-Info / Push-on-Info / throttle / wav paths / push cmd) with Save-&-apply
//             (SentinelAlertService.Apply → writes Alerts.conf + live-applies, no restart), Reload, and
//             Test Info/Critical buttons that fire a REAL alert (sound + push + ledger + Risk display);
//             (2) DRY-RUN ENTRY PROBE — GateEntry + SizeForRisk + TickValue for an account/instr/qty/stop/
//             risk with NO order submitted (engage the kill → gate returns HARD, safely); (3) SAFE SELF-
//             CHECKS — scoped-kill isolation (fake roots), sizer unaffordable→0/generous→≥1, TickValue>0,
//             green/red; (4) LEDGER AUDIT — today's kill/flatten/alert/restore/fill counts. Also: Journal
//             tab "▶ Live" toggle (2s auto-refresh tail; stopped on close). SentinelAlertService→v1.0.1.
//    v1.1.2 — (in-place) new SLIPPAGE tab + FILL events in Journal (Substrate 2, execution-quality view).
//             SentinelCore v1.1.0 gained Ledger.Fill (records intended-vs-actual fill price → adverse
//             slip ticks); GTrader21 (in-place, observability-only) now logs every realtime fill.
//             SLIPPAGE tab: window (Today / 7 / 30) → tiles (fills · avg slip · worst · adverse % · est.
//             $ impact via SentinelCore.TickValue), per-instrument drag (sorted), and worst individual
//             fills. Only stop/limit fills (a comparable intended price) count — pure-market fills are
//             excluded. Stop-fill slippage = the prop risk this surfaces. JOURNAL tab gained a Fills
//             tile + "Fills" filter + fill rows (colored by execution quality: adverse=red, improvement
//             =green). All still one stream, many views — no parallel journal.
//    v1.1.1 — (in-place) new JOURNAL tab (hardening Substrate 2 read side): a blotter + action-audit
//             VIEW of the SentinelCore.Ledger JSONL event stream. Window selector (Today / 7 / 30 local
//             days) + type filter (All / Orders / Actions / Alerts); hero tiles (events / orders /
//             actions / alerts / accounts / instruments); chronological newest-first rows — orders
//             colored by side (buy=green, sell=red) with instr·qty·type·px·acct·tag, actions/alerts
//             colored by kind (kill/flatten/crit=red, alert/block/halt=amber). Reads via new
//             Ledger.ReadRecent()/ReadDay()/Parse() (SentinelCore v1.1.0). On-demand, read-only; cached
//             parse so filter buttons don't re-hit disk. No parallel journal — one stream, many views.
//    v1.1.0 — VISUAL RESKIN (phase 1 — theme + chrome) to the "flight-instrument" design language (see
//             the design-direction mockup + the redesigned GTrader21 risk card). Repaletted all brushes to
//             the mockup tokens (void/panel/line/ink/mute + Green/Red=money, Amber=caution) and added a
//             cyan ACCENT (=live/watching) + Ink2/Faint/Card2. New TOP BAR: Sentinel "eye" brand mark
//             (glow), SENTINEL SUITE title, and the kill-switch as a rounded status pill (dot+label, red
//             when engaged). PILL TABS via a TabItem ControlTemplate (transparent idle, panel-toned +
//             cyan underline when selected, hover = ink2). All tab CONTENT inherits the new palette;
//             per-tab card/tile restyle is phase 2.
//    v1.0.9 — (in-place) new ACCOUNTS tab: per-account profile editor (account + firm-preset dropdown +
//             ratio/target/daily-loss/size/contracts/session) that writes Sentinel\Profiles.conf (upserts
//             the account's line); firm preset prefills ratio/loss; a live list of current profiles from
//             SentinelCore.AllAccountProfiles(). Feeds the Governor + (future) sizing.
//    v1.0.8 — (in-place) Risk tab: a "Consistency governor" section (per-account daily P&L vs cap/
//             loss-stop + status, from SentinelCore's governor registry). Excursion tab: a "Sync all
//             ◆ configs" button (write every confident +EV signal's ◆ config in one click; refactored
//             ApplyBestRespToGTrader → WriteConfigFor). EyeVerdictCode now delegates to Group.
//    v1.0.7 — (in-place) Eye referee → ACTIONABLE: a green/amber recommendation line ("Eye-gate ON/OFF
//             for this signal") from EyeVerdictCode; and "Apply ◆" now writes useEyeGate=true/false into
//             the .conf when the referee is conclusive (GTrader21 v0.1.6 applies it). Closes the
//             referee→config→strategy loop for the Eye filter.
//    v1.0.6 — (in-place) Excursion tab: "Active lab configs" live section (which running GTrader21
//             instance is on which .conf + TP/SL, from SentinelCore's config-use registry, refreshed
//             on the timer); and a ④ "Eye referee" in the per-signal detail (endorsed vs not-endorsed
//             medians/expectancy + a plain-English verdict — fills in as Eye data accrues).
//    v1.0.5 — (in-place) Excursion viz: ★ mark now ORANGE / ◆ GREEN (colored Runs); per-signal
//             FIRE-RATE (n/day + days, in the text rows + detail header — a +EV signal that fires
//             rarely isn't a business); scatter EYE-ENDORSEMENT overlay (hollow rings on Eye-endorsed
//             fires + legend, accrues once Eye runs); "Apply ◆ to GTrader21 config" button writes the
//             best-responsible TP/SL to Sentinel\GTraderConfigs\<inst>_<signal>_<dir>.conf.
//    v1.0.4 — (in-place) Excursion viz CONFIDENCE + R:R honesty: edge chart dims small-sample rows
//             (n<30) + a "Confident only (n≥30)" filter on the chart & detail selector; expectancy
//             grid now shows R:R per config, marks the best RESPONSIBLE (stop≤TP) config with ◆
//             distinct from the raw ★, and dims wide-stop mirages; scatter faintly shades the win zone
//             (MFE≥TP & MAE<SL). Alias-safe (ShapeLine/Ellipse/Polyline; win-zone uses a Border).
//    v1.0.3 — (in-place) Excursion tab PER-SIGNAL DETAIL (WPF-drawn, System.Windows.Shapes): a signal
//             selector drives three linked visuals — ① growth line (median MFE/MAE at 5/15/60 min),
//             ② outcome scatter (each fire MAE15×MFE15, colored by regime, with dashed TP/SL overlay
//             from the trend Best; strided to stay snappy on big clouds), ③ TP/stop expectancy grid
//             (all 12 configs from SentinelExcursions.TpStopGrid, diverging bars, ★ best). Summary is
//             cached so the selector redraws without reloading.
//    v1.0.2 — (in-place) Risk tab: a "Scoped instrument halts" section listing per-instrument kills
//             (SentinelRisk v1.0.4 now halts one root, not the whole suite); top line relabeled
//             "Global kill-switch". Excursion tab: a new EDGE CHART — a diverging bar per signal group
//             (trend regime), green median MFE to the right vs red median MAE to the left at 15 min,
//             ranked by edge — the at-a-glance "which signal has an edge" view above the text rows.
//    v1.0.1 — (in-place) Risk tab live-phase additions: "Re-request feeds" button
//             (SentinelRiskService.ReRequestAllFeeds), a contract-rollover countdown section
//             (days-to-roll per instrument, red when entries are blocked), and a news-lockout
//             section (active windows + next upcoming from Sentinel\News.conf). Feed rows now
//             tag watch-registered feeds and show recovery-attempt counts. Excursion status line
//             now reports UNIQUE record count + dupe-fire / legacy-v1.0 skips (SentinelExcursions v1.0.1).
//    v1.0.0 — initial dashboard. Control Center menu entry; NTWindow + TabControl shell;
//             shared kill-switch top bar (bound to SentinelCore); LIVE "Copy" tab that edits
//             the copier config (leader, provider policy, follower rows with per-follower
//             instrument-map DSL + multiplier) and pushes it via Reconfigure(). Placeholder
//             tabs for Log/Risk/Lens/Arc/Eye. Persistence (save/load config) is a follow-up —
//             Apply configures the LIVE service only.
// ═════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
// alias the Shapes types (NinjaTrader.Gui also defines Line → ambiguous if we import the whole namespace)
using ShapeLine     = System.Windows.Shapes.Line;
using ShapeEllipse  = System.Windows.Shapes.Ellipse;
using ShapePolyline = System.Windows.Shapes.Polyline;
using ShapePolygon  = System.Windows.Shapes.Polygon;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.AddOns.SentinelCopier;   // the copier service + config types

namespace NinjaTrader.NinjaScript.AddOns.Sentinel
{
    // ─────────────────────────────────────────────────────────────────────────
    //  AddOn: registers the Control Center menu item that opens the dashboard.
    // ─────────────────────────────────────────────────────────────────────────
    public class SentinelDashboardAddOn : NinjaTrader.NinjaScript.AddOnBase
    {
        private NTMenuItem _menuItem;
        private NTMenuItem _newMenu;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "SentinelDashboard";
                Description = "Sentinel Suite — unified config/control window (Control Center > New).";
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
                Header = "Sentinel Suite",
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
                var w = new SentinelDashboardWindow();
                w.Show();
                w.Activate();
            }));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  The dashboard window.
    // ─────────────────────────────────────────────────────────────────────────
    public class SentinelDashboardWindow : NTWindow
    {
        // "flight-instrument" palette (v1.1 reskin — matches the design-direction mockup; frozen for
        // cross-thread safety). ONE cyan accent = live/watching; Green/Red reserved for money; Amber = caution.
        // THEME-AWARE (Dark/Light/Silver): rebuilt from SentinelSkin by ApplyTheme() at window open (before
        // BuildLayout). Dark values below are the fallback. Reopen the window to re-theme (build-time, like the Deck).
        private static Brush Bg     = new SolidColorBrush(Color.FromRgb(0x0A, 0x0E, 0x17)); // void
        private static Brush Card   = new SolidColorBrush(Color.FromRgb(0x11, 0x17, 0x26)); // panel
        private static Brush Card2  = new SolidColorBrush(Color.FromRgb(0x0E, 0x14, 0x20)); // panel deep (gradient bottom)
        private static Brush Edge   = new SolidColorBrush(Color.FromRgb(0x1E, 0x2A, 0x3D)); // hairline
        private static Brush Text   = new SolidColorBrush(Color.FromRgb(0xE9, 0xEE, 0xF7)); // ink
        private static Brush Ink2   = new SolidColorBrush(Color.FromRgb(0xAE, 0xBA, 0xCE)); // secondary ink
        private static Brush Muted  = new SolidColorBrush(Color.FromRgb(0x6C, 0x7A, 0x92)); // muted labels
        private static Brush Faint  = new SolidColorBrush(Color.FromRgb(0x26, 0x34, 0x4C)); // tracks / dividers
        private static Brush Accent = new SolidColorBrush(Color.FromRgb(0x3F, 0xD1, 0xE0)); // cyan — live/watching
        private static Brush Green  = new SolidColorBrush(Color.FromRgb(0x25, 0xD0, 0x8B)); // up / money
        private static Brush Red    = new SolidColorBrush(Color.FromRgb(0xFF, 0x5C, 0x6A)); // down / money
        private static Brush Amber  = new SolidColorBrush(Color.FromRgb(0xF2, 0xB3, 0x4C)); // caution
        private static LinearGradientBrush CardBg = new LinearGradientBrush(Color.FromRgb(0x13, 0x1A, 0x28), Color.FromRgb(0x0E, 0x14, 0x20), 90); // glass card fill

        private static SolidColorBrush FB(System.Windows.Media.Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        /// <summary>Rebuild the palette brushes from SentinelSkin's active theme (Dark/Light/Silver). Call before BuildLayout.</summary>
        private static void ApplyTheme()
        {
            try
            {
                SentinelSkin.MaybeRefreshTheme();
                Bg    = FB(SentinelSkin.KVoid);  Card  = FB(SentinelSkin.KPanel); Card2  = FB(SentinelSkin.KCard);
                Edge  = FB(SentinelSkin.KLine);  Text  = FB(SentinelSkin.KInk);   Ink2   = FB(SentinelSkin.KInk2);
                Muted = FB(SentinelSkin.KMute);  Faint = FB(SentinelSkin.KFaint); Accent = FB(SentinelSkin.KAccent);
                Green = FB(SentinelSkin.KUp);    Red   = FB(SentinelSkin.KDown);  Amber  = FB(SentinelSkin.KWarn);
                var cg = new LinearGradientBrush(SentinelSkin.KPanel, SentinelSkin.KCard, 90); cg.Freeze(); CardBg = cg;
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelDashboard.ApplyTheme", _sx); }
        }

        static SentinelDashboardWindow()
        {
            Bg.Freeze(); Card.Freeze(); Card2.Freeze(); Edge.Freeze(); Text.Freeze(); Ink2.Freeze();
            Muted.Freeze(); Faint.Freeze(); Accent.Freeze(); Green.Freeze(); Red.Freeze(); Amber.Freeze(); CardBg.Freeze();
        }

        // top-bar widgets
        private Border _killPill;
        private ShapeEllipse _killDot;
        private TextBlock _killPillText, _statusText;

        // Copy-tab widgets
        private ComboBox _leaderCombo;
        private ComboBox _policyCombo;
        private CheckBox _eyeGateCheck;
        private StackPanel _followersPanel;
        private readonly List<FollowerRow> _followerRows = new List<FollowerRow>();

        // Log-tab widgets (MAE/MFE monitor — attaches to SentinelLogService)
        private StackPanel _logTiles;
        private StackPanel _logOpen;
        private StackPanel _logRailList;
        private TextBlock _logStatus;
        private System.Windows.Threading.DispatcherTimer _logTimer;
        private Action _logTradeClosed;

        // Risk-tab widgets (feed-lag watchdog — attaches to SentinelRiskService)
        private StackPanel _riskTiles;   // hero feed-health tiles (top of Risk tab)
        private TextBlock  _readyLine;   // pre-trade readiness verdict
        private StackPanel _alertsPanel; // recent Sentinel alerts
        private StackPanel _riskScoped;  // per-instrument scoped kills currently engaged
        private StackPanel _riskGov;     // per-account consistency-governor state
        private TextBlock _riskStatus;
        private StackPanel _riskFeeds;
        private StackPanel _riskConns;
        private StackPanel _riskRoll;    // rollover countdown per instrument
        private StackPanel _riskNews;    // active + next news lockouts

        // ── HOME (front page) — Red Folder News readout ──
        private StackPanel _homeNewsTiles;
        private StackPanel _homeNewsList;
        private TextBlock  _homeNewsFoot;
        private TextBox    _homeBefore, _homeAfter;   // editable no-trade window (minutes)
        private DateTime   _homeNewsMtime = DateTime.MinValue;      // News.conf mtime we last parsed
        private string     _homeNewsSource = "";                    // the "# source=… fetched_local=…" header line
        private readonly List<HomeNewsEv> _homeNewsCache = new List<HomeNewsEv>();
        private sealed class HomeNewsEv { public DateTime When; public string Name; public int Before; public int After; }

        // Lens-tab widgets (on-demand analytics over Sentinel\Log JSONL)
        private TextBlock _lensStatus;
        private StackPanel _lensTiles;
        private StackPanel _lensStrat;
        private StackPanel _lensInst;
        private TextBlock _lensEyeVerdict;   // plain-English "does Eye add edge?" conclusion
        private StackPanel _lensEye;         // Endorsed / NotEndorsed / NoVerdict rows
        private StackPanel _lensBand;        // score-band expectancy curve

        // Journal-tab widgets (blotter/audit — a VIEW of the SentinelCore.Ledger JSONL stream)
        private TextBlock _journalStatus;
        private StackPanel _journalTiles;
        private StackPanel _journalList;
        private int _journalDays = 1;                 // window: 1 / 7 / 30 local days
        private string _journalFilter = "All";        // All | Orders | Fills | Actions | Alerts
        private List<SentinelCore.Ledger.Entry> _journalEntries;   // cached parse so filters don't re-read disk
        private System.Windows.Threading.DispatcherTimer _journalLiveTimer;   // "▶ Live" auto-refresh tail
        private StackPanel _journalHist;              // activity histogram (events per hour/day)

        // Slippage-tab widgets (execution-quality VIEW of the ledger's fill events)
        private TextBlock _slipStatus;
        private StackPanel _slipTiles;
        private StackPanel _slipByInst;
        private StackPanel _slipWorst;
        private int _slipDays = 7;                    // window: 1 / 7 / 30 local days

        // Test-tab widgets (prove-the-safety-system surface: alert channel · dry-run gate probe · self-checks · ledger audit)
        private CheckBox _tAlEnabled, _tAlPlayInfo, _tAlPushOnInfo;
        private TextBox _tAlThrottle, _tAlCritWav, _tAlInfoWav, _tAlPush;
        private TextBlock _tAlStatus;
        private ComboBox _tProbeAcct;
        private TextBox _tProbeInstr, _tProbeQty, _tProbeStop, _tProbeRisk;
        private TextBlock _tProbeResult;
        private StackPanel _tChecks;
        private TextBlock _tChecksStatus;
        private StackPanel _tVerify;
        private TextBlock _tVerifyStatus;

        // Eye-tab widgets (per-instrument GodTrades qualification from SentinelEye charts)
        private TextBlock _eyeStatus;
        private StackPanel _eyeTiles;
        private StackPanel _eyePanel;

        // Arc-tab widgets (fleet orchestration board — read SentinelCore fleet registry)
        private TextBlock _arcStatus;
        private StackPanel _arcTiles;
        private StackPanel _arcPanel;

        // Assist-tab widgets (manual-assist ticket queue — read SentinelCore assist registry)
        private TextBlock _assistStatus;
        private StackPanel _assistTiles;
        private StackPanel _assistPanel;

        // Excursion-tab widgets (signal-excursion analytics over Sentinel\Excursions JSONL)
        private StackPanel _excTiles;   // hero tiles (signals / best edge / records) atop the Excursion tab
        private StackPanel _excChart;   // diverging MFE/MAE edge bars (trend-regime, sorted by edge)
        private StackPanel _excConfigs; // live: which running strategy instance is on which lab config
        private ComboBox _excSel;       // per-signal detail selector
        private CheckBox _excConfOnly;  // filter edge chart + selector to confident (n≥ExcConfidentN) groups
        private StackPanel _excDetail;  // growth line + scatter + expectancy grid for the selected signal
        private SentinelExcursions_v1_0.Summary _excSummary;   // cached so the selector redraws without reloading
        private const int ExcConfidentN = 30;   // below this a signal's edge is too small-sample to trust

        // Accounts-tab widgets (per-account profiles → Profiles.conf → Governor + sizing)
        private ComboBox _apAccount, _apFirm, _apDdType;
        private TextBox _apRatio, _apTarget, _apDailyLoss, _apSize, _apContracts, _apSession;
        private TextBox _apManualDaily, _apDdAmt, _apDdFlat, _apResetHour;   // v1.1.9: full governor fields in-tab
        private CheckBox _apHardEnforce;
        private TextBlock _apStatus;
        private StackPanel _apList, _apTiles;
        private TextBlock _excStatus;

        private Action<bool> _killHandler;

        public SentinelDashboardWindow()
        {
            Caption = "Sentinel Suite";
            Width = 820; Height = 640;

            ApplyTheme();            // rebuild the palette brushes from the active theme BEFORE building the UI
            Content = BuildLayout();

            // reflect external kill-switch changes on our button
            _killHandler = engaged => Dispatcher.InvokeAsync(new Action(() => RefreshKillButton(engaged)));
            SentinelCore.KillSwitchChanged += _killHandler;

            // Log tab: refresh timer on THIS window's UI thread + immediate refresh on trade close.
            _logTimer = new System.Windows.Threading.DispatcherTimer(
                System.Windows.Threading.DispatcherPriority.Background, Dispatcher);
            _logTimer.Interval = TimeSpan.FromMilliseconds(750);
            _logTimer.Tick += (s, e) => { RefreshHomeLive(); RefreshLogLive(); RefreshRiskLive(); RefreshEyeLive(); RefreshArcLive(); RefreshAssistLive(); RefreshActiveConfigsLive(); RefreshApProfilesLive(); };
            _logTimer.Start();
            var logSvc = SentinelLogService.Instance;
            if (logSvc != null)
            {
                _logTradeClosed = () => Dispatcher.InvokeAsync(new Action(RefreshLogLive));
                logSvc.TradeClosed += _logTradeClosed;
            }

            Closed += OnClosedCleanup;

            RefreshAccounts();
            LoadFromLiveConfig();
            RefreshStatus();
            RefreshKillButton(SentinelCore.KillSwitchEngaged);
            RefreshHomeLive();
            RefreshLogLive();
            RefreshRiskLive();
            RefreshLensLoad();
            RefreshEyeLive();
            RefreshArcLive();
            RefreshAssistLive();
            RefreshActiveConfigsLive();
            RefreshApProfilesLive();
        }

        // ── layout: top bar + tab control ────────────────────────────────────
        private FrameworkElement BuildLayout()
        {
            var root = new Grid { Background = Bg };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                     // top bar
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // tabs
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                     // footer / credits

            root.Children.Add(BuildTopBar());

            var tabs = new TabControl { Background = Bg, BorderThickness = new Thickness(0), Margin = new Thickness(6, 2, 6, 6), Padding = new Thickness(0) };
            tabs.ItemContainerStyle = PillTabStyle();
            tabs.Items.Add(new TabItem { Header = "Home",  Content = BuildHomeTab() });
            tabs.Items.Add(new TabItem { Header = "Copy",  Content = BuildCopyTab() });
            tabs.Items.Add(new TabItem { Header = "Log",   Content = BuildLogTab() });
            tabs.Items.Add(new TabItem { Header = "Risk",  Content = BuildRiskTab() });
            tabs.Items.Add(new TabItem { Header = "Journal", Content = BuildJournalTab() });
            tabs.Items.Add(new TabItem { Header = "Slippage", Content = BuildSlippageTab() });
            tabs.Items.Add(new TabItem { Header = "Lens",  Content = BuildLensTab() });
            tabs.Items.Add(new TabItem { Header = "Eye",    Content = BuildEyeTab() });
            tabs.Items.Add(new TabItem { Header = "Arc",    Content = BuildArcTab() });
            tabs.Items.Add(new TabItem { Header = "Assist", Content = BuildAssistTab() });
            tabs.Items.Add(new TabItem { Header = "Excursion", Content = BuildExcursionTab() });
            tabs.Items.Add(new TabItem { Header = "Accounts", Content = BuildAccountsTab() });
            tabs.Items.Add(new TabItem { Header = "Test", Content = BuildTestTab() });
            Grid.SetRow(tabs, 1);
            root.Children.Add(tabs);

            // footer — open-licence attribution for the helmet brand mark (CC BY 3.0 requires a visible credit)
            var footer = new Border
            {
                BorderBrush = Edge, BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(14, 5, 14, 6),
                Child = new TextBlock
                {
                    Text = "Helmet mark: Lorc · game-icons.net · CC BY 3.0",
                    Foreground = Muted, FontFamily = new FontFamily("Consolas"), FontSize = 10.5,
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center
                }
            };
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);
            return root;
        }

        private Border BuildTopBar()
        {
            var grid = new Grid { Margin = new Thickness(14, 9, 12, 9) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                    // brand
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // status
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                    // kill

            var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            brand.Children.Add(BuildBrandMark());
            brand.Children.Add(new TextBlock { Text = "SENTINEL", Foreground = Text, FontFamily = new FontFamily("Segoe UI"),
                FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            brand.Children.Add(new TextBlock { Text = "SUITE", Foreground = Muted, FontFamily = new FontFamily("Segoe UI"),
                FontSize = 14, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(brand, 0); grid.Children.Add(brand);

            _statusText = new TextBlock { Text = "…", Foreground = Ink2, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(22, 0, 0, 0), FontFamily = new FontFamily("Consolas"), FontSize = 12 };
            Grid.SetColumn(_statusText, 1); grid.Children.Add(_statusText);

            // always-on-top pin (accent when engaged) + kill pill, right-aligned
            var pin = new Button
            {
                Content = "^", Width = 26, Height = 26, FontSize = 13, FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Consolas"), Style = null, Padding = new Thickness(0), MinWidth = 0, MinHeight = 0,
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), Cursor = System.Windows.Input.Cursors.Hand,
                Background = Card, Foreground = Topmost ? Accent : Muted, BorderBrush = Topmost ? Accent : Edge, BorderThickness = new Thickness(1),
                ToolTip = "Keep the Sentinel Suite window always on top"
            };
            pin.Click += (s, e) => { Topmost = !Topmost; pin.Foreground = Topmost ? Accent : Muted; pin.BorderBrush = Topmost ? Accent : Edge; };
            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            right.Children.Add(pin);
            right.Children.Add(BuildKillPill());
            Grid.SetColumn(right, 2); grid.Children.Add(right);

            var bg = new LinearGradientBrush(Color.FromRgb(0x14, 0x1C, 0x2C), Color.FromRgb(0x0F, 0x15, 0x22), 90);
            var border = new Border { Background = bg, BorderBrush = Edge, BorderThickness = new Thickness(0, 0, 0, 1), Child = grid };
            Grid.SetRow(border, 0);
            return border;
        }

        // a minimal Sentinel "eye" mark — an elongated ring + iris + pupil, cyan, with a soft glow
        // The Sentinel brand mark = the shared Spartan helmet from SentinelSkin (one source of truth).
        private FrameworkElement BuildBrandMark() => SentinelSkin.HelmetMark(22, Accent);

        // the kill-switch as a rounded status pill (dot + label); red when engaged
        private FrameworkElement BuildKillPill()
        {
            _killDot = new ShapeEllipse { Width = 8, Height = 8, Fill = Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            _killPillText = new TextBlock { Text = "KILL-SWITCH", Foreground = Muted, FontFamily = new FontFamily("Segoe UI"),
                FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(_killDot); sp.Children.Add(_killPillText);
            _killPill = new Border { Background = Card, BorderBrush = Edge, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(999), Padding = new Thickness(13, 6, 15, 6),
                Cursor = System.Windows.Input.Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, Child = sp };
            _killPill.MouseLeftButtonUp += (s, e) => SentinelCore.SetKillSwitch(!SentinelCore.KillSwitchEngaged, "Dashboard");
            return _killPill;
        }

        // pill-style tabs: transparent when idle (muted text), panel-toned + cyan underline when selected
        private Style PillTabStyle()
        {
            var tmpl = new ControlTemplate(typeof(TabItem));
            var pill = new FrameworkElementFactory(typeof(Border), "Pill");
            pill.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            pill.SetValue(Border.CornerRadiusProperty, new CornerRadius(9, 9, 0, 0));
            pill.SetValue(Border.MarginProperty, new Thickness(2, 0, 2, 0));
            pill.SetValue(Border.PaddingProperty, new Thickness(14, 8, 14, 7));

            var stack = new FrameworkElementFactory(typeof(StackPanel));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.ContentSourceProperty, "Header");
            cp.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            stack.AppendChild(cp);
            var under = new FrameworkElementFactory(typeof(Border), "Underline");
            under.SetValue(Border.HeightProperty, 2.0);
            under.SetValue(Border.MarginProperty, new Thickness(0, 7, 0, 0));
            under.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
            under.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            stack.AppendChild(under);
            pill.AppendChild(stack);
            tmpl.VisualTree = pill;

            var sel = new Trigger { Property = TabItem.IsSelectedProperty, Value = true };
            sel.Setters.Add(new Setter(Border.BackgroundProperty, Bg, "Pill"));
            sel.Setters.Add(new Setter(Border.BackgroundProperty, Accent, "Underline"));
            sel.Setters.Add(new Setter(Control.ForegroundProperty, Text));
            tmpl.Triggers.Add(sel);
            var hov = new Trigger { Property = TabItem.IsMouseOverProperty, Value = true };
            hov.Setters.Add(new Setter(Control.ForegroundProperty, Ink2));
            tmpl.Triggers.Add(hov);

            var style = new Style(typeof(TabItem));
            style.Setters.Add(new Setter(Control.TemplateProperty, tmpl));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Muted));
            style.Setters.Add(new Setter(Control.FontSizeProperty, 12.5));
            style.Setters.Add(new Setter(Control.FontFamilyProperty, new FontFamily("Segoe UI")));
            return style;
        }

        // ── shared "flight-instrument" card helpers (phase-2 reskin — reused across tabs) ──
        private Border MakeCard(UIElement child)
        {
            return new Border { Background = CardBg, BorderBrush = Edge, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12), Padding = new Thickness(15, 13, 15, 13),
                Margin = new Thickness(0, 0, 0, 12), Child = child };
        }
        private Border StatTile(string label, string value, Brush valueBrush, string sub)
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = label.ToUpperInvariant(), Foreground = Muted, FontFamily = new FontFamily("Segoe UI"), FontSize = 10, FontWeight = FontWeights.SemiBold });
            sp.Children.Add(new TextBlock { Text = value, Foreground = valueBrush, FontFamily = new FontFamily("Segoe UI"), FontSize = 27, FontWeight = FontWeights.Light, Margin = new Thickness(0, 5, 0, 0) });
            if (!string.IsNullOrEmpty(sub)) sp.Children.Add(new TextBlock { Text = sub, Foreground = Ink2, FontFamily = new FontFamily("Consolas"), FontSize = 11, Margin = new Thickness(0, 3, 0, 0) });
            return new Border { Background = CardBg, BorderBrush = Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14, 12, 14, 12), MinWidth = 150, Margin = new Thickness(0, 0, 12, 0), Child = sp };
        }
        private FrameworkElement Track(double frac, Brush fill)
        {
            frac = Math.Max(0, Math.Min(1, frac));
            var grid = new Grid { Height = 7 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(frac, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - frac, GridUnitType.Star) });
            var bgb = new Border { CornerRadius = new CornerRadius(4), Background = Faint };
            Grid.SetColumnSpan(bgb, 2); grid.Children.Add(bgb);
            var fillb = new Border { CornerRadius = new CornerRadius(4), Background = fill };
            Grid.SetColumn(fillb, 0); grid.Children.Add(fillb);
            return grid;
        }
        // ═══ REUSABLE CHART PRIMITIVES (dataviz method + Sentinel palette) ═══════════════════════════
        //  Sentinel palette instance: cyan(Accent)=magnitude/live · Green/Red=money+polarity(diverging) ·
        //  Amber=caution. No rainbow categorical (the suite reserves one accent) → identity comes from the
        //  direct row label, magnitude from bar length. Mark specs: thin bars, rounded DATA end (square at
        //  the baseline), recessive Faint/Edge gridlines, values/labels in TEXT tokens (never the bar hue).

        // Horizontal MAGNITUDE bars — one row per (label,value); length ∝ |value|/max; single hue.
        private FrameworkElement HBars(IList<string> labels, IList<double> values, IList<Brush> hues,
            Func<double, string> fmt, double labelW = 150, double plotW = 230)
        {
            var wrap = new StackPanel();
            if (labels == null || labels.Count == 0) { wrap.Children.Add(MonoLine("—", Muted)); return wrap; }
            double max = 1e-9; foreach (var v in values) max = Math.Max(max, Math.Abs(v));
            max = NiceCeil(max);
            for (int i = 0; i < labels.Count; i++)
            {
                double v = values[i];
                double px = v == 0 ? 0 : Math.Max(3, Math.Abs(v) / max * plotW);
                Brush hue = hues != null && i < hues.Count ? hues[i] : Accent;
                var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelW) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(plotW) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var lbl = new TextBlock { Text = labels[i], Foreground = Ink2, FontFamily = new FontFamily("Consolas"), FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                Grid.SetColumn(lbl, 0); row.Children.Add(lbl);
                var barGrid = new Grid { Height = 13 };
                barGrid.Children.Add(new Border { Background = Faint, Height = 6, Opacity = 0.5, CornerRadius = new CornerRadius(3),
                    HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center });   // baseline track
                barGrid.Children.Add(new Border { Background = hue, Width = px, Height = 10, CornerRadius = new CornerRadius(0, 5, 5, 0),
                    HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center });
                Grid.SetColumn(barGrid, 1); row.Children.Add(barGrid);
                var val = new TextBlock { Text = fmt != null ? fmt(v) : v.ToString("0.##"), Foreground = Muted,
                    FontFamily = new FontFamily("Consolas"), FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 0, 0) };
                Grid.SetColumn(val, 2); row.Children.Add(val);
                wrap.Children.Add(row);
            }
            return wrap;
        }

        // Horizontal DIVERGING bars — center baseline; value>0 → right, value<0 → left. posIsGood picks
        // which side is green (money/polarity). Canvas-positioned for pixel accuracy.
        private FrameworkElement HDivBars(IList<string> labels, IList<double> values, Func<double, string> fmt,
            bool posIsGood, double labelW = 150, double plotW = 230)
        {
            var wrap = new StackPanel();
            if (labels == null || labels.Count == 0) { wrap.Children.Add(MonoLine("—", Muted)); return wrap; }
            double max = 1e-9; foreach (var v in values) max = Math.Max(max, Math.Abs(v));
            max = NiceCeil(max);
            double half = plotW / 2.0;
            Brush posCol = posIsGood ? Green : Red, negCol = posIsGood ? Red : Green;
            for (int i = 0; i < labels.Count; i++)
            {
                double v = values[i];
                double px = v == 0 ? 0 : Math.Max(2, Math.Abs(v) / max * half);
                var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelW) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(plotW) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var lbl = new TextBlock { Text = labels[i], Foreground = Ink2, FontFamily = new FontFamily("Consolas"), FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                Grid.SetColumn(lbl, 0); row.Children.Add(lbl);
                var canvas = new Canvas { Width = plotW, Height = 14 };
                canvas.Children.Add(new ShapeLine { X1 = half, Y1 = 0, X2 = half, Y2 = 14, Stroke = Edge, StrokeThickness = 1 });   // center baseline
                if (px > 0)
                {
                    bool right = v > 0;
                    var b = new Border { Background = right ? posCol : negCol, Width = px, Height = 10,
                        CornerRadius = right ? new CornerRadius(0, 5, 5, 0) : new CornerRadius(5, 0, 0, 5) };
                    Canvas.SetLeft(b, right ? half : half - px); Canvas.SetTop(b, 2); canvas.Children.Add(b);
                }
                Grid.SetColumn(canvas, 1); row.Children.Add(canvas);
                var val = new TextBlock { Text = fmt != null ? fmt(v) : v.ToString("0.##"), Foreground = Muted,
                    FontFamily = new FontFamily("Consolas"), FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 0, 0) };
                Grid.SetColumn(val, 2); row.Children.Add(val);
                wrap.Children.Add(row);
            }
            return wrap;
        }

        // Smart signed bars: DIVERGING when values have mixed signs (polarity matters), else MAGNITUDE
        // bars anchored left (full width, colored by sign) so one-sided data doesn't waste half the plot.
        private FrameworkElement SignedBars(IList<string> labels, IList<double> values, Func<double, string> fmt,
            bool posIsGood, double labelW = 150, double plotW = 230)
        {
            bool hasPos = false, hasNeg = false;
            foreach (var v in values) { if (v > 1e-6) hasPos = true; else if (v < -1e-6) hasNeg = true; }
            if (hasPos && hasNeg) return HDivBars(labels, values, fmt, posIsGood, labelW, plotW);
            var hues = new List<Brush>();
            foreach (var v in values)
            {
                bool good = posIsGood ? v >= 0 : v <= 0;
                hues.Add(Math.Abs(v) < 1e-6 ? Muted : (good ? Green : Red));
            }
            return HBars(labels, values, hues, fmt, labelW, plotW);
        }

        // Vertical COLUMN histogram — change-over-time (activity per bucket). Single hue; rounded top (data end).
        private FrameworkElement Columns(IList<double> values, IList<string> axisLabels, Brush hue, double height = 64)
        {
            var outer = new StackPanel();
            if (values == null || values.Count == 0) { outer.Children.Add(MonoLine("—", Muted)); return outer; }
            double max = 1e-9; foreach (var v in values) max = Math.Max(max, v);
            var g = new Grid { Height = height, Margin = new Thickness(0, 2, 0, 0) };
            for (int i = 0; i < values.Count; i++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < values.Count; i++)
            {
                double bh = values[i] <= 0 ? 0 : Math.Max(2, values[i] / max * (height - 2));
                var bar = new Border { Background = values[i] > 0 ? hue : Faint, Height = Math.Max(bh, values[i] > 0 ? 2 : 1),
                    VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(2, 2, 0, 0), Margin = new Thickness(1, 0, 1, 0) };
                Grid.SetColumn(bar, i); g.Children.Add(bar);
            }
            outer.Children.Add(g);
            if (axisLabels != null && axisLabels.Count == values.Count)
            {
                var ax = new Grid();
                for (int i = 0; i < values.Count; i++) ax.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                for (int i = 0; i < values.Count; i++)
                {
                    if (string.IsNullOrEmpty(axisLabels[i])) continue;
                    var t = new TextBlock { Text = axisLabels[i], Foreground = Muted, FontFamily = new FontFamily("Consolas"), FontSize = 8,
                        HorizontalAlignment = HorizontalAlignment.Center };
                    Grid.SetColumn(t, i); ax.Children.Add(t);
                }
                outer.Children.Add(ax);
            }
            return outer;
        }

        private Border Chip(string text, Brush col)
        {
            Color c = (col as SolidColorBrush) != null ? ((SolidColorBrush)col).Color : Color.FromRgb(0x6C, 0x7A, 0x92);
            return new Border {
                Background = new SolidColorBrush(Color.FromArgb(28, c.R, c.G, c.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(120, c.R, c.G, c.B)), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7), Padding = new Thickness(8, 2, 8, 2), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = text, Foreground = col, FontFamily = new FontFamily("Segoe UI"), FontSize = 10, FontWeight = FontWeights.SemiBold } };
        }
        // one account's profile + live governor state as a card (matches the design mockup)
        private Border GovernorCard(SentinelCore.AccountProfile p, SentinelCore.GovernorState g)
        {
            double cap = p.Ratio * p.ProfitTarget; if (p.ManualDailyTarget > 0) cap = Math.Min(p.ManualDailyTarget, cap);
            double day = g != null ? g.DailyPnl : 0;
            bool allowed = g == null || g.Allowed;
            string status = g != null ? g.Status : null;

            var head = new DockPanel { LastChildFill = true };
            Brush stCol = allowed ? Green : (status == "DayComplete" ? Accent : Red);
            string stTxt = g == null ? "IDLE" : (allowed ? "TRADING" : (status == "DayComplete" ? "DAY COMPLETE" : "HALTED"));
            var pill = Chip(stTxt, stCol); DockPanel.SetDock(pill, Dock.Right); pill.Margin = new Thickness(6, 0, 0, 0); head.Children.Add(pill);
            var firmBadge = Chip((p.Firm ?? "custom").ToUpperInvariant(), Accent); DockPanel.SetDock(firmBadge, Dock.Right); head.Children.Add(firmBadge);
            head.Children.Add(new TextBlock { Text = p.Account, Foreground = Text, FontFamily = new FontFamily("Consolas"), FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });

            double open = OpenPnl(p.Account);   // live unrealized (open positions) — realized is g.DailyPnl
            var pnlRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            pnlRow.Children.Add(new TextBlock { Text = (day >= 0 ? "+$" : "-$") + Math.Abs(Math.Round(day)), Foreground = day >= 0 ? Green : Red,
                FontFamily = new FontFamily("Segoe UI"), FontSize = 25, FontWeight = FontWeights.Light });
            if (Math.Abs(open) >= 0.5)
                pnlRow.Children.Add(new TextBlock { Text = "   open " + (open >= 0 ? "+$" : "-$") + Math.Abs(Math.Round(open)),
                    Foreground = open >= 0 ? Green : Red, FontFamily = new FontFamily("Consolas"), FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 4), Opacity = 0.9 });

            double real = AcctRealized(p.Account);   // raw account realized == NT's "Realized PnL" column (not baseline-adjusted)
            var acctLine = new TextBlock {
                Text = "day = since reset   ·   acct realized " + (real >= 0 ? "+$" : "-$") + Math.Abs(Math.Round(real)) + "  (matches NT)",
                Foreground = Muted, FontFamily = new FontFamily("Consolas"), FontSize = 9, Margin = new Thickness(0, 1, 0, 0) };

            var capRow = new DockPanel { Margin = new Thickness(0, 12, 0, 6) };
            var capAmt = new TextBlock { Text = "$" + Math.Round(day) + " / $" + Math.Round(cap), Foreground = Ink2, FontFamily = new FontFamily("Consolas"), FontSize = 11 };
            DockPanel.SetDock(capAmt, Dock.Right); capRow.Children.Add(capAmt);
            capRow.Children.Add(new TextBlock { Text = "DAILY CAP", Foreground = Muted, FontFamily = new FontFamily("Segoe UI"), FontSize = 10, FontWeight = FontWeights.SemiBold });

            Brush fillCol = !allowed ? (status == "DayComplete" ? Accent : Red) : (cap > 0 && day / cap >= 0.9 ? Amber : Green);
            var track = Track(cap > 0 ? day / cap : 0, fillCol);

            var foot = new TextBlock {
                Text = "size " + p.SizeScale.ToString("0.##") + "×" + (p.ContractLimit > 0 ? "   ≤" + p.ContractLimit + "c" : "")
                     + "   " + (string.IsNullOrEmpty(p.Session) ? "24h" : p.Session) + "   ·   loss −$" + Math.Round(p.DailyLossStop),
                Foreground = Muted, FontFamily = new FontFamily("Consolas"), FontSize = 10, Margin = new Thickness(0, 10, 0, 0) };

            var sp = new StackPanel();
            sp.Children.Add(head); sp.Children.Add(pnlRow); sp.Children.Add(acctLine); sp.Children.Add(capRow); sp.Children.Add(track); sp.Children.Add(foot);
            return MakeCard(sp);
        }

        private TabItem Placeholder(string name)
        {
            return new TabItem
            {
                Header = name,
                Content = new TextBlock
                {
                    Text = name + " — coming soon (headless " + name + " service + this tab).",
                    Foreground = Muted, Margin = new Thickness(16)
                }
            };
        }

        // ── COPY TAB ─────────────────────────────────────────────────────────
        // ── HOME TAB (front page) — first item: RED FOLDER NEWS ─────────────────────
        //    "Red folder" = ForexFactory HIGH-impact events (impact:"High"). The native
        //    SentinelNewsService already filters to HIGH, so Sentinel\News.conf IS the red-folder
        //    list — this tab is a live READOUT of it (schedule + countdown + active lockout + freshness).
        private FrameworkElement BuildHomeTab()
        {
            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new TextBlock { Text = "Front Page", Foreground = Text,
                FontFamily = new FontFamily("Segoe UI"), FontSize = 18, FontWeight = FontWeights.Light, Margin = new Thickness(0, 0, 0, 2) });
            panel.Children.Add(new TextBlock { Text = "at-a-glance suite status", Foreground = Muted,
                FontFamily = new FontFamily("Segoe UI"), FontSize = 11, Margin = new Thickness(0, 0, 0, 16) });

            // header: a REAL red dot (the emoji renders monochrome) + section title
            var hdr = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            hdr.Children.Add(new TextBlock { Text = "●", Foreground = Red, FontSize = 14,
                Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });
            hdr.Children.Add(new TextBlock { Text = "Red Folder News — high-impact economic events (event veto)",
                Foreground = Text, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(hdr);

            // editable NO-TRADE WINDOW (props typically want ~10-15 min each side)
            var cfg = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 2), VerticalAlignment = VerticalAlignment.Center };
            cfg.Children.Add(new TextBlock { Text = "No-trade window:", Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            _homeBefore = HomeNumBox(SentinelNewsService_v1_0_0.BeforeMin);
            cfg.Children.Add(_homeBefore);
            cfg.Children.Add(new TextBlock { Text = "min before   /", Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) });
            _homeAfter = HomeNumBox(SentinelNewsService_v1_0_0.AfterMin);
            cfg.Children.Add(_homeAfter);
            cfg.Children.Add(new TextBlock { Text = "min after", Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 10, 0) });
            cfg.Children.Add(MakeButton("Save", OnSaveNewsWindow));
            panel.Children.Add(cfg);

            _homeNewsTiles = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 12) };
            panel.Children.Add(_homeNewsTiles);
            _homeNewsList = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            panel.Children.Add(_homeNewsList);
            _homeNewsFoot = new TextBlock { Foreground = Muted, FontFamily = new FontFamily("Consolas"), FontSize = 10,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
            panel.Children.Add(_homeNewsFoot);

            return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel, Background = Bg };
        }

        private void RefreshHomeLive()
        {
            if (_homeNewsTiles == null) return;
            try { RefreshHomeNews(); } catch (Exception _sx) { SentinelCore.Swallow("SentinelDashboard.RefreshHomeLive", _sx); }
        }

        private void RefreshHomeNews()
        {
            string path  = System.IO.Path.Combine(SentinelCore.SettingsDir, "News.conf");
            bool exists  = System.IO.File.Exists(path);
            DateTime mtime = exists ? System.IO.File.GetLastWriteTime(path) : DateTime.MinValue;
            if (exists && mtime != _homeNewsMtime) { _homeNewsMtime = mtime; ParseHomeNews(path); }
            else if (!exists) { _homeNewsCache.Clear(); _homeNewsSource = ""; _homeNewsMtime = DateTime.MinValue; }

            DateTime now = NinjaTrader.Core.Globals.Now;
            bool stale = exists && (now - mtime).TotalHours > 26.0;

            HomeNewsEv active = null, next = null;
            var upcoming = new List<HomeNewsEv>();
            foreach (var ev in _homeNewsCache)
            {
                DateTime start = ev.When.AddMinutes(-ev.Before), end = ev.When.AddMinutes(ev.After);
                if (end < now) continue;                        // fully in the past
                upcoming.Add(ev);
                if (now >= start && now <= end) { if (active == null) active = ev; }
                else if (start > now && next == null) next = ev;
            }

            // ── tiles ──
            _homeNewsTiles.Children.Clear();
            string pv; Brush pc; string ps;
            if (!exists)          { pv = "NO DATA"; pc = Amber; ps = "run the feeder"; }
            else if (active != null) { pv = "LOCKED"; pc = Red; ps = NewsCut(active.Name, 22); }
            else if (stale)       { pv = "STALE";   pc = Amber; ps = "refresh calendar"; }
            else                  { pv = "CLEAR";   pc = Green; ps = upcoming.Count > 0 ? "armed" : "no events"; }
            _homeNewsTiles.Children.Add(StatTile("Protection", pv, pc, ps));

            if (next != null)
            {
                bool soon = (next.When - now).TotalMinutes <= 60;
                _homeNewsTiles.Children.Add(StatTile("Next red folder", "in " + FmtSpan(next.When - now), soon ? Amber : Text, NewsCut(next.Name, 22)));
            }
            else _homeNewsTiles.Children.Add(StatTile("Next red folder", "—", Muted, active != null ? "lockout live" : "none upcoming"));

            _homeNewsTiles.Children.Add(StatTile("Windows", upcoming.Count.ToString(), upcoming.Count > 0 ? Text : Muted, "loaded (High/USD)"));

            // ── schedule list ──
            _homeNewsList.Children.Clear();
            if (!exists)
                _homeNewsList.Children.Add(MonoLine("News.conf not found — the feeder hasn't written yet (give SentinelNewsService ~20s after start).", Muted));
            else if (upcoming.Count == 0)
                _homeNewsList.Children.Add(MonoLine("no upcoming red-folder (high-impact) events loaded — clear session.", Muted));
            else
            {
                int shown = 0;
                foreach (var ev in upcoming)
                {
                    if (shown++ >= 8) break;
                    DateTime start = ev.When.AddMinutes(-ev.Before), end = ev.When.AddMinutes(ev.After);
                    bool isActive = now >= start && now <= end;
                    string cd; Brush b;
                    if (isActive) { cd = "● LOCKOUT — ends in " + FmtSpan(end - now); b = Red; }
                    else { TimeSpan tt = ev.When - now; cd = "in " + FmtSpan(tt); b = tt.TotalMinutes <= 60 ? Amber : Text; }
                    string line = ev.When.ToString("ddd HH:mm").PadRight(11) + cd.PadRight(26)
                                + ev.Name + "  (-" + ev.Before + "/+" + ev.After + "m)";
                    _homeNewsList.Children.Add(MonoLine(line, b));
                }
                if (upcoming.Count > 8)
                    _homeNewsList.Children.Add(MonoLine("… +" + (upcoming.Count - 8) + " more this week", Muted));
            }

            // ── footer: feeder status + freshness + source ──
            bool running = SentinelNewsService_v1_0_0.Instance != null;
            string l1 = running
                ? "feeder: running — " + SentinelNewsService_v1_0_0.Currencies + " ≥ " + SentinelNewsService_v1_0_0.MinImpact
                  + ", every " + SentinelNewsService_v1_0_0.RefreshMinutes + "m"
                : "feeder: NOT running — compile/enable SentinelNewsService";
            string l2 = exists ? "News.conf updated " + FmtSpan(now - mtime) + " ago"
                  + (stale ? "  ⚠ STALE (>26h — protection may be missing)" : "") : "News.conf: missing";
            _homeNewsFoot.Text = l1 + "\n" + l2 + (string.IsNullOrEmpty(_homeNewsSource) ? "" : "\n" + _homeNewsSource);
            _homeNewsFoot.Foreground = (stale || !running) ? Amber : Muted;
        }

        private void ParseHomeNews(string path)
        {
            _homeNewsCache.Clear(); _homeNewsSource = "";
            try
            {
                foreach (string raw in System.IO.File.ReadAllLines(path))
                {
                    string line = (raw ?? "").Trim();
                    if (line.Length == 0) continue;
                    if (line[0] == '#' || line.StartsWith("//"))
                    {
                        if (_homeNewsSource.Length == 0 && line.IndexOf("source=", StringComparison.OrdinalIgnoreCase) >= 0)
                            _homeNewsSource = line.TrimStart('#', ' ');
                        continue;
                    }
                    string[] p = line.Split('|');
                    if (p.Length < 2) continue;
                    DateTime when;
                    if (!DateTime.TryParseExact(p[0].Trim(), "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None, out when)) continue;
                    int b = 5, a = 20, iv;
                    if (p.Length >= 4 && int.TryParse(p[3].Trim(), out iv)) b = iv;
                    if (p.Length >= 5 && int.TryParse(p[4].Trim(), out iv)) a = iv;
                    _homeNewsCache.Add(new HomeNewsEv { When = when, Name = p[1].Trim(), Before = b, After = a });
                }
                _homeNewsCache.Sort((x, y) => DateTime.Compare(x.When, y.When));
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelDashboard.ParseHomeNews", _sx); }
        }

        private static string FmtSpan(TimeSpan t)
        {
            if (t.Ticks < 0) t = t.Negate();
            if (t.TotalDays  >= 1) return ((int)t.TotalDays) + "d" + t.Hours + "h";
            if (t.TotalHours >= 1) return t.Hours + "h" + t.Minutes.ToString("00") + "m";
            if (t.TotalMinutes >= 1) return t.Minutes + "m";
            return ((int)t.TotalSeconds) + "s";
        }

        private static string NewsCut(string s, int n)
        {
            return string.IsNullOrEmpty(s) || s.Length <= n ? s : s.Substring(0, n);
        }

        private TextBox HomeNumBox(int v)
        {
            return new TextBox { Text = v.ToString(), Width = 46, Background = Card, Foreground = Text,
                BorderBrush = Edge, BorderThickness = new Thickness(1), Padding = new Thickness(4, 2, 4, 2),
                TextAlignment = TextAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
        }

        // Apply the edited no-trade window: set the service config, persist it, and re-emit News.conf from the
        // last fetch INSTANTLY (no network). Props typically run ~10-15 min each side of a red-folder event.
        private void OnSaveNewsWindow(object sender, RoutedEventArgs e)
        {
            int b, a;
            if (!int.TryParse((_homeBefore.Text ?? "").Trim(), out b) || b < 0) b = SentinelNewsService_v1_0_0.BeforeMin;
            if (!int.TryParse((_homeAfter.Text ?? "").Trim(), out a) || a < 0) a = SentinelNewsService_v1_0_0.AfterMin;
            if (b > 240) b = 240;
            if (a > 240) a = 240;
            SentinelNewsService_v1_0_0.BeforeMin = b;
            SentinelNewsService_v1_0_0.AfterMin  = a;
            SentinelNewsService_v1_0_0.SaveConfig();
            var inst = SentinelNewsService_v1_0_0.Instance;
            if (inst != null) inst.RewriteFromCache();
            _homeBefore.Text = b.ToString();
            _homeAfter.Text  = a.ToString();
            _homeNewsMtime = DateTime.MinValue;   // force a re-parse of the just-rewritten News.conf
            try { RefreshHomeNews(); } catch (Exception _sx) { SentinelCore.Swallow("SentinelDashboard.OnSaveNewsWindow", _sx); }
        }

        private FrameworkElement BuildCopyTab()
        {
            var panel = new StackPanel { Margin = new Thickness(12) };

            panel.Children.Add(Label("Leader (signal) account", true));
            _leaderCombo = new ComboBox { MinWidth = 240, Margin = new Thickness(0, 2, 0, 10), HorizontalAlignment = HorizontalAlignment.Left,
                IsEditable = true, IsTextSearchEnabled = true, StaysOpenOnEdit = true };   // type to filter 169 accounts
            panel.Children.Add(_leaderCombo);

            panel.Children.Add(Label("Same-provider policy", true));
            _policyCombo = new ComboBox { MinWidth = 160, Margin = new Thickness(0, 2, 0, 10), HorizontalAlignment = HorizontalAlignment.Left };
            _policyCombo.Items.Add(ProviderPolicy.Off.ToString());
            _policyCombo.Items.Add(ProviderPolicy.Warn.ToString());
            _policyCombo.Items.Add(ProviderPolicy.Block.ToString());
            _policyCombo.SelectedIndex = 1; // Warn
            panel.Children.Add(_policyCombo);

            _eyeGateCheck = new CheckBox {
                Content = "Eye-gate — mirror only SentinelEye-qualified ENTRIES (exits always mirror)",
                IsChecked = false, Foreground = Text, Margin = new Thickness(0, 2, 0, 12) };
            panel.Children.Add(_eyeGateCheck);

            panel.Children.Add(Label("Followers  (map DSL: \"GC>MGC*10, CL>MCL*10\" — blank = same instrument)", true));
            _followersPanel = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
            panel.Children.Add(_followersPanel);

            var rowBtns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 12) };
            rowBtns.Children.Add(MakeButton("+ Add follower", (s, e) => AddFollowerRow(null)));
            rowBtns.Children.Add(MakeButton("Refresh accounts", (s, e) => RefreshAccounts()));
            panel.Children.Add(rowBtns);

            var applyBtns = new StackPanel { Orientation = Orientation.Horizontal };
            applyBtns.Children.Add(MakeButton("Apply to live copier", (s, e) => ApplyConfig()));
            applyBtns.Children.Add(MakeButton("Reload Copy.conf", (s, e) => ReloadConfig()));
            panel.Children.Add(applyBtns);

            var note = new TextBlock
            {
                Text = "Apply saves + configures the running copier (persists to Copy.conf, survives restarts). "
                     + "Reload re-reads a hand-edited Copy.conf without an F5.",
                Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0)
            };
            panel.Children.Add(note);

            return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel, Background = Bg };
        }

        // one follower editor row
        private sealed class FollowerRow
        {
            public Border Root;
            public ComboBox Account;
            public CheckBox Enabled;
            public TextBox Mult;
            public TextBox Map;
        }

        private void AddFollowerRow(FollowerConfig prefill)
        {
            var row = new FollowerRow();
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2) };

            row.Account = new ComboBox { MinWidth = 170, Margin = new Thickness(0, 0, 6, 0),
                IsEditable = true, IsTextSearchEnabled = true, StaysOpenOnEdit = true };   // type to filter
            foreach (string n in AccountNames()) row.Account.Items.Add(n);
            if (prefill != null && prefill.AccountName != null) row.Account.SelectedItem = prefill.AccountName;
            sp.Children.Add(row.Account);

            row.Enabled = new CheckBox { Content = "on", IsChecked = prefill == null || prefill.Enabled, VerticalAlignment = VerticalAlignment.Center, Foreground = Text, Margin = new Thickness(0, 0, 6, 0) };
            sp.Children.Add(row.Enabled);

            sp.Children.Add(new TextBlock { Text = "x", Foreground = Muted, VerticalAlignment = VerticalAlignment.Center });
            row.Mult = new TextBox { Text = (prefill != null ? prefill.Multiplier : 1.0).ToString(CultureInfo.InvariantCulture), Width = 46, Margin = new Thickness(2, 0, 6, 0) };
            sp.Children.Add(row.Mult);

            row.Map = new TextBox { Text = prefill != null ? SentinelCopierService_v0_2_0.MapToDsl(prefill.InstrumentMap) : "", MinWidth = 220, Margin = new Thickness(0, 0, 6, 0) };
            sp.Children.Add(row.Map);

            var remove = MakeButton("×", (s, e) =>
            {
                _followersPanel.Children.Remove(row.Root);
                _followerRows.Remove(row);
            });
            remove.MinWidth = 26; remove.Margin = new Thickness(0);
            sp.Children.Add(remove);

            row.Root = new Border { Background = Card, BorderBrush = Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(4), Margin = new Thickness(0, 0, 0, 4), Child = sp };
            _followersPanel.Children.Add(row.Root);
            _followerRows.Add(row);
        }

        // ── apply UI → live copier config ────────────────────────────────────
        private void ApplyConfig()
        {
            var svc = SentinelCopierService_v0_2_0.Instance;
            if (svc == null) { SetStatus("copier service not running — compile SentinelCopierService (F5)."); return; }

            var cfg = new CopierConfig();
            cfg.LeaderAccount = ComboText(_leaderCombo);
            ProviderPolicy pol;
            cfg.Policy = Enum.TryParse(_policyCombo.SelectedItem as string, out pol) ? pol : ProviderPolicy.Warn;
            cfg.UseEyeGate = _eyeGateCheck != null && _eyeGateCheck.IsChecked == true;

            foreach (FollowerRow r in _followerRows)
            {
                string acct = ComboText(r.Account);
                if (string.IsNullOrEmpty(acct)) continue;

                var f = new FollowerConfig { AccountName = acct, Enabled = r.Enabled.IsChecked == true };
                double m;
                f.Multiplier = double.TryParse(r.Mult.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out m) ? m : 1.0;
                SentinelCopierService_v0_2_0.ParseMapDsl(r.Map.Text, f.InstrumentMap);
                cfg.Followers.Add(f);
            }

            svc.Reconfigure(cfg);
            SentinelCopierService_v0_2_0.SaveConfig(cfg);   // persist → survives NT recompiles/restarts
            SetStatus("applied + saved: leader='" + (cfg.LeaderAccount ?? "<none>") + "', followers=" + cfg.Followers.Count
                + ", policy=" + cfg.Policy + ", eye-gate=" + (cfg.UseEyeGate ? "ON" : "off"));
        }

        private void ReloadConfig()
        {
            var svc = SentinelCopierService_v0_2_0.Instance;
            if (svc == null) { SetStatus("copier service not running"); return; }
            var cfg = SentinelCopierService_v0_2_0.LoadConfig();
            if (cfg == null) { SetStatus("no Copy.conf on disk to reload"); return; }
            svc.Reconfigure(cfg);
            LoadFromLiveConfig();
            SetStatus("reloaded Copy.conf: leader='" + (cfg.LeaderAccount ?? "<none>") + "', followers=" + cfg.Followers.Count
                + ", eye-gate=" + (cfg.UseEyeGate ? "ON" : "off"));
        }

        private void LoadFromLiveConfig()
        {
            var svc = SentinelCopierService_v0_2_0.Instance;
            CopierConfig cfg = svc != null ? svc.CurrentConfig : null;
            if (cfg == null) return;

            if (cfg.LeaderAccount != null) _leaderCombo.SelectedItem = cfg.LeaderAccount;
            _policyCombo.SelectedItem = cfg.Policy.ToString();
            if (_eyeGateCheck != null) _eyeGateCheck.IsChecked = cfg.UseEyeGate;
            _followersPanel.Children.Clear();
            _followerRows.Clear();
            foreach (FollowerConfig f in cfg.Followers) AddFollowerRow(f);
        }

        // editable combo: prefer the selected item; fall back to typed text (typeahead may leave text)
        private static string ComboText(ComboBox c)
        {
            if (c == null) return null;
            string s = c.SelectedItem as string;
            if (!string.IsNullOrEmpty(s)) return s;
            return string.IsNullOrEmpty(c.Text) ? null : c.Text.Trim();
        }

        // (instrument-map DSL parse/format now live canonically on SentinelCopierService_v0_2_0,
        //  reused here so the dashboard and the persisted Copy.conf always agree.)

        // ── helpers ──────────────────────────────────────────────────────────
        private void RefreshAccounts()
        {
            List<string> names = AccountNames();
            string keepLeader = _leaderCombo != null ? _leaderCombo.SelectedItem as string : null;
            if (_leaderCombo != null)
            {
                _leaderCombo.Items.Clear();
                foreach (string n in names) _leaderCombo.Items.Add(n);
                if (keepLeader != null) _leaderCombo.SelectedItem = keepLeader;
            }
            foreach (FollowerRow r in _followerRows)
            {
                string keep = r.Account.SelectedItem as string;
                r.Account.Items.Clear();
                foreach (string n in names) r.Account.Items.Add(n);
                if (keep != null) r.Account.SelectedItem = keep;
            }
            RefreshStatus();
        }

        private static List<string> AccountNames()
        {
            var list = new List<string>();
            lock (Account.All)
            {
                foreach (Account a in Account.All)
                    if (a != null && a.Name != null) list.Add(a.Name);
            }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        // live open (unrealized) P&L for an account, read straight off the account (a synchronous getter — UI-safe).
        // Realized day-P&L is g.DailyPnl (Governor); this complements it so the card isn't blank while a trade is open.
        private double OpenPnl(string account) { return AcctItem(account, AccountItem.UnrealizedProfitLoss); }
        // the account's actual Realized P&L (== NT's Accounts-tab "Realized PnL" column). The governor's g.DailyPnl
        // is realized-SINCE-a-day-baseline (for the consistency rule); this raw figure mirrors NT so the card is
        // never blank/confusing when the baseline is fresh.
        private double AcctRealized(string account) { return AcctItem(account, AccountItem.RealizedProfitLoss); }
        private double AcctItem(string account, AccountItem item)
        {
            if (string.IsNullOrEmpty(account)) return 0;
            try
            {
                lock (Account.All)
                    foreach (Account a in Account.All)
                        if (a != null && a.Name == account)
                            return a.Get(item, Currency.UsDollar);
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelDashboard.AcctItem", _sx); }
            return 0;
        }

        private void RefreshStatus()
        {
            bool copierUp = SentinelCopierService_v0_2_0.Instance != null;
            SetStatus("Copier service: " + (copierUp ? "running" : "NOT running (F5 to compile)")
                + "   •   accounts: " + AccountNames().Count);
        }

        private void SetStatus(string msg)
        {
            if (_statusText != null) _statusText.Text = msg;
        }

        private void RefreshKillButton(bool engaged)
        {
            if (_killPill == null) return;
            _killPill.Background  = engaged ? new SolidColorBrush(Color.FromRgb(0x2E, 0x12, 0x16)) : Card;
            _killPill.BorderBrush = engaged ? Red : Edge;
            _killPillText.Text    = engaged ? "KILL-SWITCH ON" : "KILL-SWITCH";
            _killPillText.Foreground = engaged ? Red : Muted;
            _killDot.Fill         = engaged ? Red : Muted;
        }

        private TextBlock Label(string t, bool section)
        {
            return new TextBlock { Text = t, Foreground = section ? Text : Muted, Margin = new Thickness(0, 6, 0, 0), FontWeight = section ? FontWeights.SemiBold : FontWeights.Normal };
        }

        private Button MakeButton(string text, RoutedEventHandler onClick)
        {
            var b = new Button { Content = text, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 8, 0), MinWidth = 90 };
            b.Click += onClick;
            return b;
        }

        // ── LOG TAB (MAE/MFE monitor) — attaches to SentinelLogService.Instance ──────
        private FrameworkElement BuildLogTab()
        {
            var grid = new Grid { Background = Bg };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // left rail: live trade sources (account · strategy · tier)
            var rail = new StackPanel { Background = Card, Width = 180, Margin = new Thickness(0, 0, 6, 0) };
            rail.Children.Add(new TextBlock { Text = "Sources", Foreground = Muted,
                FontFamily = new FontFamily("Consolas"), FontSize = 11, Margin = new Thickness(8, 8, 8, 4) });
            _logRailList = new StackPanel { Margin = new Thickness(6, 0, 6, 6) };
            rail.Children.Add(_logRailList);
            Grid.SetColumn(rail, 0);
            grid.Children.Add(rail);

            // main: tiles + open-position list + status
            var main = new Grid();
            main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _logTiles = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 8, 4, 8) };
            Grid.SetRow(_logTiles, 0);
            main.Children.Add(_logTiles);

            _logOpen = new StackPanel();
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _logOpen, Margin = new Thickness(4, 0, 4, 4) };
            Grid.SetRow(scroll, 1);
            main.Children.Add(scroll);

            _logStatus = new TextBlock { Foreground = Muted, FontFamily = new FontFamily("Consolas"),
                FontSize = 10, Margin = new Thickness(4, 0, 4, 6) };
            Grid.SetRow(_logStatus, 2);
            main.Children.Add(_logStatus);

            Grid.SetColumn(main, 1);
            grid.Children.Add(main);
            return grid;
        }

        private void RefreshLogLive()
        {
            if (_logTiles == null) return;   // tab not built yet
            var svc = SentinelLogService.Instance;
            if (svc == null)
            {
                _logStatus.Text = "log service not running (F5 to compile SentinelLogService)";
                _logTiles.Children.Clear(); _logOpen.Children.Clear(); _logRailList.Children.Clear();
                return;
            }

            List<SentinelLogService.OpenSnapshot> open = svc.GetOpenSnapshots();
            double heat = 0, fav = 0;
            foreach (var o in open) { heat += o.MaeTicks; fav += o.MfeTicks; }

            _logTiles.Children.Clear();
            _logTiles.Children.Add(LogTile("Open positions", open.Count.ToString(), Text));
            _logTiles.Children.Add(LogTile("Live heat (ticks)", "-" + (int)heat, Amber));
            _logTiles.Children.Add(LogTile("Live favorable", "+" + (int)fav, Green));

            _logOpen.Children.Clear();
            if (open.Count == 0)
                _logOpen.Children.Add(new TextBlock { Text = "no open positions", Foreground = Muted,
                    FontFamily = new FontFamily("Consolas"), FontSize = 11, Margin = new Thickness(4, 6, 0, 0) });
            foreach (var o in open) _logOpen.Children.Add(LogOpenRow(o));

            _logStatus.Text = "live · " + open.Count + " open · " + DateTime.Now.ToString("HH:mm:ss");

            _logRailList.Children.Clear();
            if (open.Count == 0)
                _logRailList.Children.Add(new TextBlock { Text = "no active sources", Foreground = Muted,
                    FontFamily = new FontFamily("Consolas"), FontSize = 10, Margin = new Thickness(4, 4, 0, 0) });
            else
            {
                var seen = new HashSet<string>();
                foreach (var o in open)
                {
                    if (!seen.Add(o.Account + "|" + o.Strategy + "|" + o.Instrument)) continue;
                    _logRailList.Children.Add(LogRailRow(o));
                }
            }
        }

        private Border LogTile(string label, string value, Brush valueBrush)
        {
            var sp = new StackPanel { Margin = new Thickness(9, 7, 9, 7) };
            sp.Children.Add(new TextBlock { Text = label, Foreground = Muted,
                FontFamily = new FontFamily("Consolas"), FontSize = 10 });
            sp.Children.Add(new TextBlock { Text = value, Foreground = valueBrush,
                FontFamily = new FontFamily("Consolas"), FontSize = 19, Margin = new Thickness(0, 2, 0, 0) });
            return new Border { Background = Card, BorderBrush = Edge, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 8, 0), MinWidth = 120, Child = sp };
        }

        private Border LogRailRow(SentinelLogService.OpenSnapshot o)
        {
            var sp = new StackPanel { Margin = new Thickness(8, 6, 8, 6) };
            var top = new DockPanel();
            var badge = new Border { Background = o.Tier == 2 ? Green : Edge, CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(0, 0, 6, 0),
                Child = new TextBlock { Text = "t" + o.Tier, Foreground = o.Tier == 2 ? Bg : Muted,
                    FontFamily = new FontFamily("Consolas"), FontSize = 9 } };
            DockPanel.SetDock(badge, Dock.Left);
            top.Children.Add(badge);
            top.Children.Add(new TextBlock { Text = o.Strategy, Foreground = Text,
                FontFamily = new FontFamily("Consolas"), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
            sp.Children.Add(top);
            sp.Children.Add(new TextBlock { Text = o.Instrument + " · " + o.Account, Foreground = Muted,
                FontFamily = new FontFamily("Consolas"), FontSize = 9, Margin = new Thickness(0, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis });
            return new Border { Background = Bg, BorderBrush = o.Tier == 2 ? Green : Edge,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 0, 0, 6), Child = sp };
        }

        private Border LogOpenRow(SentinelLogService.OpenSnapshot o)
        {
            var outer = new StackPanel { Margin = new Thickness(10, 7, 10, 7) };
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var left = new TextBlock {
                Text = "[t" + o.Tier + "] " + o.Strategy + " · " + o.Account + " · " + o.Instrument + "  " + (o.Dir > 0 ? "▲ long" : "▼ short"),
                Foreground = o.Dir > 0 ? Green : Red, FontFamily = new FontFamily("Consolas"), FontSize = 11 };
            var right = new TextBlock {
                Text = "MAE -" + (int)o.MaeTicks + "t · MFE +" + (int)o.MfeTicks + "t",
                Foreground = Muted, FontFamily = new FontFamily("Consolas"), FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Right };
            DockPanel.SetDock(right, Dock.Right);
            head.Children.Add(right); head.Children.Add(left);
            outer.Children.Add(head);

            double scale = 4.0;
            double advPx = Math.Min(o.MaeTicks * scale, 300);
            double favPx = Math.Min(o.MfeTicks * scale, 300);
            var barGrid = new Grid { Height = 14, Background = Bg };
            barGrid.Children.Add(new Border { Background = Red, Width = advPx, Height = 10,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(-advPx, 0, 0, 0), CornerRadius = new CornerRadius(2) });
            barGrid.Children.Add(new Border { Background = Green, Width = favPx, Height = 10,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(favPx, 0, 0, 0), CornerRadius = new CornerRadius(2) });
            barGrid.Children.Add(new Border { Width = 1, Background = Edge, HorizontalAlignment = HorizontalAlignment.Center });
            outer.Children.Add(barGrid);

            return new Border { Background = Card, BorderBrush = Edge, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 0, 7), Padding = new Thickness(4), Child = outer };
        }

        // ── RISK TAB (feed-lag watchdog) — attaches to SentinelRiskService.Instance ──
        private FrameworkElement BuildRiskTab()
        {
            var panel = new StackPanel { Margin = new Thickness(12) };
            _riskStatus = new TextBlock { Foreground = Text, FontFamily = new FontFamily("Consolas"),
                FontSize = 12, Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap };
            panel.Children.Add(_riskStatus);

            // PRE-TRADE READINESS — one-glance "cleared to trade?" verdict
            _readyLine = new TextBlock { Text = "checking…", FontFamily = new FontFamily("Consolas"), FontSize = 13,
                FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap, Foreground = Muted };
            panel.Children.Add(_readyLine);

            _riskTiles = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            panel.Children.Add(_riskTiles);

            panel.Children.Add(Label("Recent alerts (critical = kill / auto-flatten / loss-stop / naked position)", true));
            _alertsPanel = new StackPanel { Margin = new Thickness(0, 4, 0, 10) };
            panel.Children.Add(_alertsPanel);

            var riskBtns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            riskBtns.Children.Add(MakeButton("Re-request feeds", (s, e) =>
            {
                var svc = SentinelRiskService_v1_0_0.Instance;
                if (svc == null) { _riskStatus.Text = "risk service not running"; return; }
                int n = svc.ReRequestAllFeeds();
                _riskStatus.Text = "re-requested " + n + " feed(s) — watch for ticks resuming. If a feed stays "
                    + "stuck, disable/re-enable the strategy (the guaranteed fix).";
            }));
            panel.Children.Add(riskBtns);

            panel.Children.Add(Label("Scoped instrument halts (per-feed — blocks only that instrument)", true));
            _riskScoped = new StackPanel { Margin = new Thickness(0, 4, 0, 10) };
            panel.Children.Add(_riskScoped);

            panel.Children.Add(Label("Consistency governor — per-account daily cap / loss-stop (Sentinel\\Governor.conf)", true));
            _riskGov = new StackPanel { Margin = new Thickness(0, 4, 0, 10) };
            panel.Children.Add(_riskGov);

            panel.Children.Add(Label("Monitored feeds (held + watch-registered instruments)", true));
            _riskFeeds = new StackPanel { Margin = new Thickness(0, 4, 0, 10) };
            panel.Children.Add(_riskFeeds);

            panel.Children.Add(Label("Contract rollover — days to roll (blocks new entries ≤ block buffer)", true));
            _riskRoll = new StackPanel { Margin = new Thickness(0, 4, 0, 10) };
            panel.Children.Add(_riskRoll);

            panel.Children.Add(Label("News lockout (Sentinel\\News.conf — blocks entries in the window)", true));
            _riskNews = new StackPanel { Margin = new Thickness(0, 4, 0, 10) };
            panel.Children.Add(_riskNews);

            panel.Children.Add(Label("Connections", true));
            _riskConns = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            panel.Children.Add(_riskConns);

            return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel, Background = Bg };
        }

        // Pre-trade readiness verdict + recent alerts (top of the Risk tab).
        private void RefreshReadiness(SentinelRiskService_v1_0_0.RiskSnapshot s, int breach)
        {
            var fails = new List<string>();
            if (s.KillEngaged) fails.Add("kill-switch ON");
            if (breach > 0) fails.Add(breach + " feed breach");
            if (s.InstrumentKills != null && s.InstrumentKills.Count > 0) fails.Add(s.InstrumentKills.Count + " scoped halt");
            if (s.NewsActive != null && s.NewsActive.Count > 0) fails.Add("news lockout");
            int halted = 0; foreach (var g in SentinelCore.AllGovernorStates()) if (g != null && g.Status == "DayHalted") halted++;
            if (halted > 0) fails.Add(halted + " acct loss-halted");

            if (_readyLine != null)
            {
                if (fails.Count == 0)
                {
                    _readyLine.Text = "✓ CLEARED TO TRADE   ·   " + SentinelCore.GovernorResetLabel;
                    _readyLine.Foreground = Green;
                }
                else
                {
                    _readyLine.Text = "✗ NOT READY — " + string.Join("  ·  ", fails);
                    _readyLine.Foreground = (s.KillEngaged || halted > 0) ? Red : Amber;
                }
            }

            if (_alertsPanel != null)
            {
                _alertsPanel.Children.Clear();
                var alerts = SentinelCore.Alerts.Recent(6);
                if (alerts.Count == 0) { _alertsPanel.Children.Add(MonoLine("no alerts", Muted)); return; }
                foreach (var a in alerts)
                {
                    if (a == null) continue;
                    string age; try { double sec = (DateTime.UtcNow - a.TimeUtc).TotalSeconds; age = sec < 90 ? ((int)sec) + "s" : ((int)(sec / 60)) + "m"; } catch { age = "?"; }
                    Brush b = a.Level == SentinelCore.AlertLevel.Critical ? Red : Amber;
                    _alertsPanel.Children.Add(MonoLine((a.Level == SentinelCore.AlertLevel.Critical ? "⛔ " : "• ") + a.Title
                        + (a.Detail != null ? " — " + a.Detail : "") + "   (" + age + " ago)", b));
                }
            }
        }

        private void RefreshRiskLive()
        {
            if (_riskFeeds == null) return;
            var svc = SentinelRiskService_v1_0_0.Instance;
            if (svc == null)
            {
                _riskStatus.Text = "risk service not running (F5 to compile SentinelRiskService)";
                _riskFeeds.Children.Clear(); _riskConns.Children.Clear();
                if (_riskTiles != null) _riskTiles.Children.Clear();
                if (_readyLine != null) { _readyLine.Text = "risk service not running"; _readyLine.Foreground = Muted; }
                if (_alertsPanel != null) _alertsPanel.Children.Clear();
                if (_riskScoped != null) _riskScoped.Children.Clear();
                if (_riskGov != null) _riskGov.Children.Clear();
                return;
            }
            var s = svc.GetSnapshot();

            // ── hero tiles ──
            if (_riskTiles != null)
            {
                _riskTiles.Children.Clear();
                int healthy = 0, breach = 0, waiting = 0;
                foreach (var f in s.Feeds) { if (!f.GotTick) waiting++; else if (f.Healthy) healthy++; else breach++; }
                RefreshReadiness(s, breach);
                _riskTiles.Children.Add(StatTile("Kill-switch",
                    s.KillEngaged ? "ENGAGED" : "CLEAR", s.KillEngaged ? Red : Green,
                    s.AutoKill ? "auto-kill on" : "auto-kill off"));
                _riskTiles.Children.Add(StatTile("Feeds healthy",
                    healthy + " / " + s.Feeds.Count, breach > 0 ? Red : Green,
                    breach + " breach · " + waiting + " waiting"));
                _riskTiles.Children.Add(StatTile("Scoped halts",
                    s.InstrumentKills.Count.ToString(), s.InstrumentKills.Count > 0 ? Amber : Text,
                    "lag " + s.MaxLag + "s · stall " + s.MaxStall + "s"));
            }

            _riskStatus.Text = "Global kill-switch: " + (s.KillEngaged ? "ON" : "off")
                + "   ·   auto-kill " + (s.AutoKill ? "ON (per-instrument)" : "off")
                + "   ·   thresholds: lag " + s.MaxLag + "s / stall " + s.MaxStall + "s"
                + (s.InstrumentKills.Count > 0 ? "   ·   " + s.InstrumentKills.Count + " scoped halt(s)" : "");

            // ── per-instrument scoped kills (a bad feed halts only its own instrument now) ──
            _riskScoped.Children.Clear();
            if (s.InstrumentKills.Count == 0)
                _riskScoped.Children.Add(MonoLine("none — all monitored instruments clear", Muted));
            foreach (var k in s.InstrumentKills)
                _riskScoped.Children.Add(MonoLine("⛔ " + k, Red));

            // ── consistency governor (per-account daily cap/loss; from SentinelCore) ──
            if (_riskGov != null)
            {
                _riskGov.Children.Clear();
                // daily-reset clock — confirm this matches your prop firm (a wrong TZ silently breaks the rule)
                _riskGov.Children.Add(MonoLine("⏱ " + SentinelCore.GovernorResetLabel, SentinelCore.GovernorResetHour == 0 ? Amber : Muted));
                var govs = SentinelCore.AllGovernorStates();
                if (govs.Count == 0)
                    _riskGov.Children.Add(MonoLine("no accounts governed — add lines to Sentinel\\Governor.conf (account=..|firm=..|target=..|dailyLossStop=..|resetHour=17|hardEnforce=true)", Muted));
                govs.Sort((a, b) => string.Compare(a.Account, b.Account, StringComparison.OrdinalIgnoreCase));
                foreach (var ggs in govs)
                {
                    if (ggs == null) continue;
                    Brush gb = ggs.Allowed ? Green : (ggs.Status == "DayComplete" ? Amber : Red);
                    string txt = (ggs.Account ?? "?").PadRight(14) + "  " + (ggs.Status ?? "?")
                        + "  day $" + Math.Round(ggs.DailyPnl) + " / cap $" + Math.Round(ggs.Cap)
                        + " / stop -$" + Math.Round(ggs.LossStop)
                        + (ggs.Allowed ? "" : "   ⛔ " + (ggs.Reason ?? "no new entries"));
                    _riskGov.Children.Add(MonoLine(txt, gb));
                }
            }

            _riskFeeds.Children.Clear();
            if (s.Feeds.Count == 0) _riskFeeds.Children.Add(MonoLine("no held or watch-registered instruments to monitor", Muted));

            // ── VISUAL: data-lag per feed (green healthy · amber approaching · red BREACH · gray waiting) ──
            bool anyTick = false; foreach (var f in s.Feeds) if (f.GotTick) { anyTick = true; break; }
            if (anyTick)
            {
                var fLabels = new List<string>(); var fVals = new List<double>(); var fHues = new List<Brush>();
                foreach (var f in s.Feeds)
                {
                    fLabels.Add(f.Instrument + (f.FromWatch ? " (w)" : ""));
                    fVals.Add(f.GotTick ? f.LagSec : 0);
                    fHues.Add(!f.GotTick ? Muted : (!f.Healthy ? Red : (s.MaxLag > 0 && f.LagSec >= 0.7 * s.MaxLag ? Amber : Green)));
                }
                _riskFeeds.Children.Add(new TextBlock { Text = "data lag per feed (s) — red = breach, amber = approaching " + s.MaxLag.ToString("0.#") + "s limit",
                    Foreground = Muted, FontFamily = new FontFamily("Consolas"), FontSize = 9, Margin = new Thickness(0, 0, 0, 3) });
                _riskFeeds.Children.Add(HBars(fLabels, fVals, fHues, v => v.ToString("0.0") + "s", 130, 200));
                _riskFeeds.Children.Add(new Border { Height = 1, Background = Faint, Margin = new Thickness(0, 8, 0, 6) });
            }

            foreach (var f in s.Feeds)
            {
                Brush b = !f.GotTick ? Muted : (f.Healthy ? Green : Red);
                string txt = f.Instrument + (f.FromWatch ? " (watch)" : "")
                    + "   lag " + (f.GotTick ? f.LagSec.ToString("0.0") + "s" : "—")
                    + " · stall " + (f.GotTick ? f.StallSec.ToString("0") + "s" : "—")
                    + (f.Healthy ? "" : "   ⚠ BREACH")
                    + (f.RecoverAttempts > 0 ? "   ↻ recovery " + f.RecoverAttempts : "");
                _riskFeeds.Children.Add(MonoLine(txt, b));
            }

            // ── contract rollover countdown ──
            _riskRoll.Children.Clear();
            if (s.Rollovers.Count == 0)
                _riskRoll.Children.Add(MonoLine("no futures monitored yet (needs a held/watched contract)", Muted));
            foreach (var r in s.Rollovers)
            {
                Brush rb = r.Blocked ? Red : (r.Warn ? Amber : Green);
                string txt = (r.Root ?? "?").PadRight(4) + "  " + r.Days.ToString("0") + "d to roll"
                    + "  (roll " + r.RollDate.ToString("MMM d") + ", " + (r.Contract ?? "?") + ")"
                    + (r.Blocked ? "   ⛔ ENTRIES BLOCKED" : (r.Warn ? "   ⚠ approaching" : ""));
                _riskRoll.Children.Add(MonoLine(txt, rb));
            }

            // ── news lockout ──
            _riskNews.Children.Clear();
            if (s.NewsActive.Count == 0)
                // upcoming red-folder event → RED so it catches the eye; genuinely nothing loaded → muted
                _riskNews.Children.Add(MonoLine(s.NewsNext != null ? "⚠ next red folder: " + s.NewsNext : "clear · no upcoming events loaded",
                    s.NewsNext != null ? Red : Muted));
            foreach (var n in s.NewsActive)
                _riskNews.Children.Add(MonoLine("⛔ LOCKOUT  " + n, Red));

            _riskConns.Children.Clear();
            if (s.Connections.Count == 0) _riskConns.Children.Add(MonoLine("no connection events yet", Muted));
            foreach (var c in s.Connections)
                _riskConns.Children.Add(MonoLine(c, c.EndsWith("Connected", StringComparison.Ordinal) ? Green : Amber));
        }

        private TextBlock MonoLine(string t, Brush b)
        {
            return new TextBlock { Text = t, Foreground = b, FontFamily = new FontFamily("Consolas"),
                FontSize = 11, Margin = new Thickness(0, 2, 0, 0) };
        }

        // ── JOURNAL TAB (blotter + action audit) — a VIEW of the SentinelCore.Ledger JSONL stream ──
        //    Substrate 2's read side: the one append-only event stream every tool writes (orders,
        //    fills, kill/governor/gate/auto-flatten actions, alerts) rendered chronologically. No
        //    second journal — this reads Ledger.ReadRecent(). On-demand + read-only. See
        //    Docs/SENTINEL_HARDENING_FRAMEWORK.md (Substrate 2).
        private FrameworkElement BuildJournalTab()
        {
            var panel = new StackPanel { Margin = new Thickness(12) };

            // window selector + refresh
            var ctl = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            ctl.Children.Add(MakeButton("Today",   (s, e) => { _journalDays = 1;  RefreshJournalLoad(); }));
            ctl.Children.Add(MakeButton("7 days",  (s, e) => { _journalDays = 7;  RefreshJournalLoad(); }));
            ctl.Children.Add(MakeButton("30 days", (s, e) => { _journalDays = 30; RefreshJournalLoad(); }));
            ctl.Children.Add(MakeButton("Refresh", (s, e) => RefreshJournalLoad()));
            var live = new CheckBox { Content = "▶ Live", Foreground = Accent, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
            live.Checked   += (s, e) => StartJournalLive();
            live.Unchecked += (s, e) => StopJournalLive();
            ctl.Children.Add(live);
            panel.Children.Add(ctl);

            // event-type filter (re-renders the cached parse; no disk re-read)
            var flt = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            foreach (var f in new[] { "All", "Orders", "Fills", "Actions", "Alerts" })
            {
                var name = f;
                flt.Children.Add(MakeButton(name, (s, e) => { _journalFilter = name; RenderJournalList(); }));
            }
            panel.Children.Add(flt);

            _journalStatus = new TextBlock { Foreground = Muted, FontFamily = new FontFamily("Consolas"),
                FontSize = 10, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
            panel.Children.Add(_journalStatus);

            _journalTiles = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            panel.Children.Add(_journalTiles);

            _journalHist = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            panel.Children.Add(_journalHist);

            panel.Children.Add(Label("Event stream (newest first)", true));
            _journalList = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            _journalList.Children.Add(MonoLine("— pick a window (Today / 7 / 30 days) to load —", Muted));
            panel.Children.Add(_journalList);

            return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel, Background = Bg };
        }

        private void RefreshJournalLoad()
        {
            if (_journalTiles == null) return;
            List<SentinelCore.Ledger.Entry> entries;
            try { entries = SentinelCore.Ledger.ReadRecent(_journalDays); }
            catch (Exception ex)
            {
                _journalStatus.Text = "ledger read failed: " + ex.Message;
                _journalTiles.Children.Clear();
                _journalEntries = null; RenderJournalList();
                return;
            }
            _journalEntries = entries;

            int orders = 0, fills = 0, actions = 0, alerts = 0, crit = 0;
            var accts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var insts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var en in entries)
            {
                if (en.IsOrder) { orders++; if (!string.IsNullOrEmpty(en.Instrument)) insts.Add(en.Instrument); }
                else if (en.IsFill) { fills++; if (!string.IsNullOrEmpty(en.Instrument)) insts.Add(en.Instrument); }
                else { actions++; if (en.IsAlert) { alerts++; if (en.IsCritical) crit++; } }
                if (!string.IsNullOrEmpty(en.Account)) accts.Add(en.Account);
            }

            _journalTiles.Children.Clear();
            _journalTiles.Children.Add(LogTile("Events", entries.Count.ToString(), Text));
            _journalTiles.Children.Add(LogTile("Orders", orders.ToString(), Accent));
            _journalTiles.Children.Add(LogTile("Fills", fills.ToString(), Green));
            _journalTiles.Children.Add(LogTile("Actions", actions.ToString(), Text));
            _journalTiles.Children.Add(LogTile("Alerts", alerts.ToString(), crit > 0 ? Red : (alerts > 0 ? Amber : Muted)));
            _journalTiles.Children.Add(LogTile("Accounts", accts.Count.ToString(), Text));
            _journalTiles.Children.Add(LogTile("Instruments", insts.Count.ToString(), Text));

            string label = _journalDays == 1 ? "today" : ("last " + _journalDays + " days");
            _journalStatus.Text = "loaded " + entries.Count + " events (" + label + ")  ·  " + SentinelCore.Ledger.Dir;

            // ── VISUAL: activity histogram (events per hour today, per day for a multi-day window) ──
            _journalHist.Children.Clear();
            if (entries.Count > 0)
            {
                int n = _journalDays == 1 ? 24 : _journalDays;
                var buckets = new double[n]; var axis = new string[n];
                DateTime today = DateTime.Now.Date;
                foreach (var en in entries)
                {
                    var t = en.TimeLocal;
                    int idx = _journalDays == 1 ? t.Hour : (n - 1 - (int)(today - t.Date).TotalDays);
                    if (idx >= 0 && idx < n) buckets[idx] += 1;
                }
                for (int i = 0; i < n; i++)
                    axis[i] = _journalDays == 1 ? (i % 6 == 0 ? i.ToString() : "") : (i == 0 || i == n - 1 ? today.AddDays(-(n - 1 - i)).ToString("M/d") : "");
                _journalHist.Children.Add(new TextBlock { Text = _journalDays == 1 ? "events per hour (local)" : "events per day",
                    Foreground = Muted, FontFamily = new FontFamily("Consolas"), FontSize = 9, Margin = new Thickness(0, 0, 0, 2) });
                _journalHist.Children.Add(Columns(buckets, axis, Accent, 56));
            }

            RenderJournalList();
            SentinelCore.Log("Journal", "loaded " + entries.Count + " ledger events (" + label + "): "
                + orders + " orders · " + fills + " fills · " + actions + " actions · " + alerts + " alerts (" + crit + " crit)");
        }

        private void RenderJournalList()
        {
            if (_journalList == null) return;
            _journalList.Children.Clear();
            var src = _journalEntries;
            if (src == null) { _journalList.Children.Add(MonoLine("— pick a window (Today / 7 / 30 days) to load —", Muted)); return; }
            if (src.Count == 0) { _journalList.Children.Add(MonoLine("— no events in this window —", Muted)); return; }

            const int Max = 500;
            int total = 0, shown = 0;
            for (int i = src.Count - 1; i >= 0; i--)   // newest first
            {
                var en = src[i];
                if (!PassesJournalFilter(en)) continue;
                total++;
                if (shown < Max) { _journalList.Children.Add(JournalRow(en)); shown++; }
            }
            string head = "filter: " + _journalFilter + "  ·  " + total + " event" + (total == 1 ? "" : "s")
                        + (total > Max ? "  (showing newest " + Max + ")" : "");
            _journalList.Children.Insert(0, MonoLine(head, Muted));
            if (total == 0) _journalList.Children.Add(MonoLine("— no events match filter '" + _journalFilter + "' —", Muted));
        }

        private bool PassesJournalFilter(SentinelCore.Ledger.Entry en)
        {
            switch (_journalFilter)
            {
                case "Orders":  return en.IsOrder;
                case "Fills":   return en.IsFill;
                case "Actions": return en.IsAction;
                case "Alerts":  return en.IsAlert;
                default:        return true;   // All
            }
        }

        // One blotter/audit row: time · type chip · primary line. Orders color by side (buy=green,
        // sell=red); actions/alerts color by kind (kill/flatten/crit=red, alert/block=amber).
        private Border JournalRow(SentinelCore.Ledger.Entry en)
        {
            string time = en.TimeUtc == default(DateTime) ? "--:--:--" : en.TimeLocal.ToString("HH:mm:ss");
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };

            var tb = new TextBlock { Text = time, Foreground = Muted, FontFamily = new FontFamily("Consolas"),
                FontSize = 11, Width = 66, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(tb, Dock.Left); row.Children.Add(tb);

            string chipTxt; Brush chipCol; string primary;
            if (en.IsOrder)
            {
                bool buy = en.Action != null && en.Action.IndexOf("Buy", StringComparison.OrdinalIgnoreCase) >= 0;
                bool sell = en.Action != null && en.Action.IndexOf("Sell", StringComparison.OrdinalIgnoreCase) >= 0;
                chipCol = buy ? Green : (sell ? Red : Accent);
                chipTxt = string.IsNullOrEmpty(en.Action) ? "ORDER" : en.Action.ToUpperInvariant();
                string px = en.Price > 0 ? " @ " + en.Price.ToString("0.#####", CultureInfo.InvariantCulture) : "";
                primary = (en.Instrument ?? "?") + "  " + en.Qty + "x " + (en.Type ?? "") + px
                        + "   " + (string.IsNullOrEmpty(en.Account) ? "" : en.Account)
                        + (string.IsNullOrEmpty(en.Tag) ? "" : "  · " + en.Tag);
            }
            else if (en.IsFill)
            {
                // color the FILL by execution quality: adverse slip = red, price improvement = green
                chipCol = en.HasSlip ? (en.SlipTicks > 0.0001 ? Red : (en.SlipTicks < -0.0001 ? Green : Ink2)) : Accent;
                chipTxt = "FILL";
                string slip = en.HasSlip
                    ? "  slip " + (en.SlipTicks >= 0 ? "+" : "") + en.SlipTicks.ToString("0.##", CultureInfo.InvariantCulture) + "t"
                    : "";
                string prices = en.HasSlip
                    ? en.IntendedPrice.ToString("0.#####", CultureInfo.InvariantCulture) + "→" + en.FillPrice.ToString("0.#####", CultureInfo.InvariantCulture)
                    : "@ " + en.FillPrice.ToString("0.#####", CultureInfo.InvariantCulture);
                primary = (en.Instrument ?? "?") + "  " + en.Qty + "x " + (en.Action ?? "") + "  " + prices + slip
                        + "   " + (string.IsNullOrEmpty(en.Account) ? "" : en.Account)
                        + (string.IsNullOrEmpty(en.Tag) ? "" : "  · " + en.Tag);
            }
            else
            {
                chipTxt = string.IsNullOrEmpty(en.Kind) ? "ACTION" : en.Kind.ToUpperInvariant();
                chipCol = JournalActionColor(en);
                primary = (en.Detail ?? "")
                        + (string.IsNullOrEmpty(en.Account) ? "" : "   " + en.Account);
            }

            var chip = Chip(chipTxt, chipCol);
            chip.Margin = new Thickness(0, 0, 8, 0); DockPanel.SetDock(chip, Dock.Left); row.Children.Add(chip);

            row.Children.Add(new TextBlock { Text = primary, Foreground = Ink2, FontFamily = new FontFamily("Consolas"),
                FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            return new Border { BorderBrush = Faint, BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 1, 0, 3), Child = row };
        }

        private Brush JournalActionColor(SentinelCore.Ledger.Entry en)
        {
            string k = en.Kind ?? "";
            if (en.IsCritical || k.IndexOf("flatten", StringComparison.OrdinalIgnoreCase) >= 0
                              || k.Equals("kill-engaged", StringComparison.OrdinalIgnoreCase)) return Red;
            if (k.Equals("kill-released", StringComparison.OrdinalIgnoreCase)) return Green;
            if (en.IsAlert || k.IndexOf("block", StringComparison.OrdinalIgnoreCase) >= 0
                           || k.IndexOf("halt", StringComparison.OrdinalIgnoreCase) >= 0) return Amber;
            return Accent;
        }

        // "▶ Live" tail: re-read the ledger every 2s on this window's UI thread so safety events stream in
        // during a live test. Cheap (small daily file); auto-stops on window close.
        private void StartJournalLive()
        {
            if (_journalLiveTimer == null)
            {
                _journalLiveTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                _journalLiveTimer.Tick += (s, e) => { try { RefreshJournalLoad(); } catch (Exception _sx) { SentinelCore.Swallow("SentinelDashboard.StartJournalLive", _sx); } };
            }
            RefreshJournalLoad();
            _journalLiveTimer.Start();
        }
        private void StopJournalLive() { if (_journalLiveTimer != null) _journalLiveTimer.Stop(); }

        // ── SLIPPAGE TAB (execution quality) — a VIEW of the ledger's FILL events ────
        //    Intended-vs-actual fill from the stream: adverse slip in ticks + $ impact, overall and
        //    per instrument, plus the worst individual fills. Only fills that carry a comparable
        //    intended price (stop/limit) count — pure-market fills have nothing to compare to. Stop-fill
        //    slippage is the prop-account risk this surfaces. See Docs/SENTINEL_HARDENING_FRAMEWORK.md.
        private sealed class SlipAgg
        {
            public int Fills, Adverse, QtySum;
            public double SumAdverse, Worst = double.NegativeInfinity, DollarImpact;
        }

        private FrameworkElement BuildSlippageTab()
        {
            var panel = new StackPanel { Margin = new Thickness(12) };

            var ctl = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            ctl.Children.Add(MakeButton("Today",   (s, e) => { _slipDays = 1;  RefreshSlippageLoad(); }));
            ctl.Children.Add(MakeButton("7 days",  (s, e) => { _slipDays = 7;  RefreshSlippageLoad(); }));
            ctl.Children.Add(MakeButton("30 days", (s, e) => { _slipDays = 30; RefreshSlippageLoad(); }));
            ctl.Children.Add(MakeButton("Refresh", (s, e) => RefreshSlippageLoad()));
            panel.Children.Add(ctl);

            _slipStatus = new TextBlock { Foreground = Muted, FontFamily = new FontFamily("Consolas"),
                FontSize = 10, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
            panel.Children.Add(_slipStatus);

            _slipTiles = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            panel.Children.Add(_slipTiles);

            panel.Children.Add(Label("By instrument", true));
            _slipByInst = new StackPanel { Margin = new Thickness(0, 4, 0, 10) };
            panel.Children.Add(_slipByInst);

            panel.Children.Add(Label("Worst fills", true));
            _slipWorst = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            panel.Children.Add(_slipWorst);

            return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel, Background = Bg };
        }

        private void RefreshSlippageLoad()
        {
            if (_slipTiles == null) return;
            List<SentinelCore.Ledger.Entry> entries;
            try { entries = SentinelCore.Ledger.ReadRecent(_slipDays); }
            catch (Exception ex) { _slipStatus.Text = "ledger read failed: " + ex.Message; return; }

            var fills = entries.FindAll(en => en.IsFill && en.HasSlip);
            string label = _slipDays == 1 ? "today" : ("last " + _slipDays + " days");
            _slipTiles.Children.Clear(); _slipByInst.Children.Clear(); _slipWorst.Children.Clear();

            if (fills.Count == 0)
            {
                _slipStatus.Text = "no measurable fills (" + label + ")  ·  slippage is measured on stop/limit "
                    + "fills (an intended price to compare to); pure-market fills don't qualify.  ·  " + SentinelCore.Ledger.Dir;
                _slipByInst.Children.Add(MonoLine("—", Muted));
                _slipWorst.Children.Add(MonoLine("—", Muted));
                return;
            }

            var byInst = new Dictionary<string, SlipAgg>(StringComparer.OrdinalIgnoreCase);
            var overall = new SlipAgg();
            foreach (var f in fills)
            {
                string inst = string.IsNullOrEmpty(f.Instrument) ? "?" : f.Instrument;
                SlipAgg a; if (!byInst.TryGetValue(inst, out a)) { a = new SlipAgg(); byInst[inst] = a; }
                double dollars = f.SlipTicks * Math.Max(1, f.Qty) * TickValueFor(inst);   // + adverse ticks → $ cost
                Accumulate(a, f, dollars);
                Accumulate(overall, f, dollars);
            }

            double avg = overall.SumAdverse / overall.Fills;
            double advPct = 100.0 * overall.Adverse / overall.Fills;
            _slipTiles.Children.Add(LogTile("Fills", overall.Fills.ToString(), Text));
            _slipTiles.Children.Add(LogTile("Avg slip", (avg >= 0 ? "+" : "") + avg.ToString("0.##") + "t", avg > 0.01 ? Red : (avg < -0.01 ? Green : Text)));
            _slipTiles.Children.Add(LogTile("Worst", "+" + overall.Worst.ToString("0.##") + "t", overall.Worst > 0.01 ? Red : Text));
            _slipTiles.Children.Add(LogTile("Adverse %", advPct.ToString("0") + "%", advPct >= 50 ? Red : (advPct >= 25 ? Amber : Green)));
            _slipTiles.Children.Add(LogTile("Est. $ impact", DollarStr(overall.DollarImpact), overall.DollarImpact > 0.5 ? Red : (overall.DollarImpact < -0.5 ? Green : Text)));

            // per instrument, biggest total drag first
            var insts = new List<string>(byInst.Keys);
            insts.Sort((x, y) => byInst[y].SumAdverse.CompareTo(byInst[x].SumAdverse));

            // ── VISUAL: diverging bar of AVG slip per instrument (right/red = adverse, left/green = improvement) ──
            var chLabels = new List<string>(); var chVals = new List<double>();
            foreach (var inst in insts) { chLabels.Add(inst); chVals.Add(byInst[inst].SumAdverse / byInst[inst].Fills); }
            _slipByInst.Children.Add(new TextBlock { Text = "avg slip (ticks) — right/red = adverse, left/green = price improvement",
                Foreground = Muted, FontFamily = new FontFamily("Consolas"), FontSize = 9, Margin = new Thickness(0, 0, 0, 3) });
            _slipByInst.Children.Add(SignedBars(chLabels, chVals, v => (v >= 0 ? "+" : "") + v.ToString("0.##") + "t", false, 90, 220));
            _slipByInst.Children.Add(new Border { Height = 1, Background = Faint, Margin = new Thickness(0, 8, 0, 6) });

            foreach (var inst in insts)
            {
                var a = byInst[inst];
                double iAvg = a.SumAdverse / a.Fills;
                double iAdv = 100.0 * a.Adverse / a.Fills;
                string line = inst.PadRight(10) + "  " + a.Fills + " fill" + (a.Fills == 1 ? " " : "s")
                    + " · avg " + (iAvg >= 0 ? "+" : "") + iAvg.ToString("0.##") + "t"
                    + " · worst +" + a.Worst.ToString("0.##") + "t"
                    + " · adverse " + iAdv.ToString("0") + "%"
                    + " · " + DollarStr(a.DollarImpact);
                _slipByInst.Children.Add(MonoLine(line, a.SumAdverse > 0.01 ? Red : (a.SumAdverse < -0.01 ? Green : Ink2)));
            }

            // worst individual fills
            fills.Sort((x, y) => y.SlipTicks.CompareTo(x.SlipTicks));
            int show = Math.Min(12, fills.Count);
            for (int i = 0; i < show; i++)
            {
                var f = fills[i];
                string t = f.TimeUtc == default(DateTime) ? "--:--" : f.TimeLocal.ToString("MM-dd HH:mm");
                string line = t + "  " + (f.Instrument ?? "?") + " " + f.Qty + "x " + (f.Action ?? "")
                    + "  " + f.IntendedPrice.ToString("0.#####", CultureInfo.InvariantCulture) + "→" + f.FillPrice.ToString("0.#####", CultureInfo.InvariantCulture)
                    + "  slip " + (f.SlipTicks >= 0 ? "+" : "") + f.SlipTicks.ToString("0.##") + "t"
                    + (string.IsNullOrEmpty(f.Tag) ? "" : "  · " + f.Tag);
                _slipWorst.Children.Add(MonoLine(line, f.SlipTicks > 0.01 ? Red : (f.SlipTicks < -0.01 ? Green : Ink2)));
            }

            _slipStatus.Text = "measured " + overall.Fills + " stop/limit fills (" + label + ")  ·  net "
                + (overall.SumAdverse >= 0 ? "+" : "") + overall.SumAdverse.ToString("0.#") + " adverse ticks  ·  " + SentinelCore.Ledger.Dir;
            SentinelCore.Log("Slippage", "measured " + overall.Fills + " fills (" + label + "): avg "
                + avg.ToString("0.##") + "t, worst +" + overall.Worst.ToString("0.##") + "t, adverse "
                + advPct.ToString("0") + "%, $ impact " + DollarStr(overall.DollarImpact));
        }

        private static void Accumulate(SlipAgg a, SentinelCore.Ledger.Entry f, double dollars)
        {
            a.Fills++;
            a.SumAdverse += f.SlipTicks;
            if (f.SlipTicks > a.Worst) a.Worst = f.SlipTicks;
            if (f.SlipTicks > 0.0001) a.Adverse++;
            a.QtySum += Math.Max(1, f.Qty);
            a.DollarImpact += dollars;
        }

        // "$ impact" = a COST to the trader (adverse ticks). Shown negative (a loss) when net adverse.
        private static string DollarStr(double adverseDollars)
        {
            if (Math.Abs(adverseDollars) < 0.005) return "$0";
            return (adverseDollars > 0 ? "-$" : "+$") + Math.Abs(adverseDollars).ToString("0.##", CultureInfo.InvariantCulture);
        }

        private readonly Dictionary<string, double> _tickValCache = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private double TickValueFor(string instrName)
        {
            if (string.IsNullOrEmpty(instrName)) return 0;
            double v;
            if (_tickValCache.TryGetValue(instrName, out v)) return v;
            v = 0;
            try
            {
                var instr = NinjaTrader.Cbi.Instrument.GetInstrument(instrName);
                if (instr != null) v = SentinelCore.TickValue(instr);
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelDashboard.TickValueFor", _sx); }
            _tickValCache[instrName] = v;
            return v;
        }

        // ── TEST TAB — prove the safety system: alert channel · dry-run gate probe · self-checks · ledger audit.
        //    The dry-run probe + self-checks exercise the in-memory Gate/sizer/scoped-kill with NO order and
        //    NO live global-state toggling; the ledger audit reads what actually fired. See the hardening
        //    framework's "Definition of done". This is the surface to VALIDATE the whole safety stack.
        private FrameworkElement BuildTestTab()
        {
            var panel = new StackPanel { Margin = new Thickness(12) };

            // ═══ 1. Alert channel (config + fire a real test alert = sound + push + ledger) ═══
            panel.Children.Add(Label("Alert channel — sound + push", true));
            _tAlStatus = new TextBlock { Foreground = Muted, FontFamily = new FontFamily("Consolas"), FontSize = 10, Margin = new Thickness(0, 2, 0, 6), TextWrapping = TextWrapping.Wrap };
            panel.Children.Add(_tAlStatus);

            _tAlEnabled = TestCheck("Enabled"); _tAlPlayInfo = TestCheck("Play Info sounds"); _tAlPushOnInfo = TestCheck("Push on Info");
            var alRow1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            alRow1.Children.Add(_tAlEnabled); alRow1.Children.Add(_tAlPlayInfo); alRow1.Children.Add(_tAlPushOnInfo);
            panel.Children.Add(alRow1);

            _tAlThrottle = TestBox(60); _tAlCritWav = TestBox(240); _tAlInfoWav = TestBox(240); _tAlPush = TestBox(380);
            panel.Children.Add(TestField("Throttle (s)", _tAlThrottle));
            panel.Children.Add(TestField("Crit wav", _tAlCritWav));
            panel.Children.Add(TestField("Info wav", _tAlInfoWav));
            panel.Children.Add(TestField("Push cmd", _tAlPush));

            var alBtns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 10) };
            alBtns.Children.Add(MakeButton("Save & apply", (s, e) => SaveAlertConfig()));
            alBtns.Children.Add(MakeButton("Reload file", (s, e) => LoadAlertConfig(true)));
            alBtns.Children.Add(MakeButton("Test Info", (s, e) => FireTestAlert(false)));
            alBtns.Children.Add(MakeButton("Test Critical", (s, e) => FireTestAlert(true)));
            panel.Children.Add(alBtns);

            // ═══ 2. Dry-run entry probe (gate + sizer; NO order) ═══
            panel.Children.Add(Label("Dry-run entry probe — gate + sizer, NO order submitted", true));
            _tProbeAcct = new ComboBox { MinWidth = 170, IsEditable = true, IsTextSearchEnabled = true, StaysOpenOnEdit = true, Margin = new Thickness(0, 0, 8, 0) };
            foreach (var n in AccountNames()) _tProbeAcct.Items.Add(n);
            _tProbeInstr = TestBox(120); _tProbeInstr.Text = "MES 03-25";
            _tProbeQty = TestBox(46); _tProbeQty.Text = "1";
            _tProbeStop = TestBox(46); _tProbeStop.Text = "0";
            _tProbeRisk = TestBox(56); _tProbeRisk.Text = "0";
            var pr = new WrapPanel { Margin = new Thickness(0, 2, 0, 6) };
            pr.Children.Add(FieldInline("Account", _tProbeAcct));
            pr.Children.Add(FieldInline("Instrument", _tProbeInstr));
            pr.Children.Add(FieldInline("Qty", _tProbeQty));
            pr.Children.Add(FieldInline("Stop tk", _tProbeStop));
            pr.Children.Add(FieldInline("Risk $", _tProbeRisk));
            pr.Children.Add(MakeButton("Evaluate", (s, e) => RunProbe()));
            panel.Children.Add(pr);
            _tProbeResult = new TextBlock { Foreground = Ink2, FontFamily = new FontFamily("Consolas"), FontSize = 11, Margin = new Thickness(0, 0, 0, 4), TextWrapping = TextWrapping.Wrap };
            panel.Children.Add(_tProbeResult);
            panel.Children.Add(new TextBlock { Text = "tip: engage the top-bar KILL (or hit a governor halt) then Evaluate → the gate should return HARD. No order is ever sent.",
                Foreground = Muted, FontFamily = new FontFamily("Consolas"), FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });

            // ═══ 3. Automated self-checks (safe) ═══
            panel.Children.Add(Label("Automated self-checks — safe (no live orders / no global state toggled)", true));
            var scBtns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 4) };
            scBtns.Children.Add(MakeButton("Run checks", (s, e) => RunSelfChecks()));
            panel.Children.Add(scBtns);
            _tChecksStatus = new TextBlock { Foreground = Muted, FontFamily = new FontFamily("Consolas"), FontSize = 10, Margin = new Thickness(0, 0, 0, 2) };
            panel.Children.Add(_tChecksStatus);
            _tChecks = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            panel.Children.Add(_tChecks);

            // ═══ 4. Ledger audit (today) ═══
            panel.Children.Add(Label("Today's safety events — ledger audit", true));
            var vBtns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 4) };
            vBtns.Children.Add(MakeButton("Scan today", (s, e) => RunLedgerAudit()));
            panel.Children.Add(vBtns);
            _tVerifyStatus = new TextBlock { Foreground = Muted, FontFamily = new FontFamily("Consolas"), FontSize = 10, Margin = new Thickness(0, 0, 0, 2) };
            panel.Children.Add(_tVerifyStatus);
            _tVerify = new StackPanel();
            panel.Children.Add(_tVerify);

            LoadAlertConfig(false);
            return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel, Background = Bg };
        }

        private CheckBox TestCheck(string label) => new CheckBox { Content = label, Foreground = Text, Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
        private TextBox TestBox(double w) => new TextBox { Width = w, Margin = new Thickness(0, 0, 8, 0) };
        private FrameworkElement TestField(string label, FrameworkElement input)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            sp.Children.Add(new TextBlock { Text = label, Foreground = Muted, Width = 90, VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Consolas"), FontSize = 11 });
            sp.Children.Add(input);
            return sp;
        }
        private FrameworkElement FieldInline(string label, FrameworkElement input)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 4), VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new TextBlock { Text = label, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0), FontFamily = new FontFamily("Consolas"), FontSize = 11 });
            sp.Children.Add(input);
            return sp;
        }

        private void LoadAlertConfig(bool fromFile)
        {
            var svc = SentinelAlertService_v1_0_0.Instance;
            if (svc == null) { if (_tAlStatus != null) _tAlStatus.Text = "SentinelAlertService not loaded — F5 to compile it in (test alerts still fire; you just won't hear them)."; return; }
            if (fromFile) svc.Reload();
            var c = svc.GetConfig();
            _tAlEnabled.IsChecked = c.Enabled; _tAlPlayInfo.IsChecked = c.PlayInfo; _tAlPushOnInfo.IsChecked = c.PushOnInfo;
            _tAlThrottle.Text = c.ThrottleSec.ToString(CultureInfo.InvariantCulture);
            _tAlCritWav.Text = c.CritWav ?? ""; _tAlInfoWav.Text = c.InfoWav ?? ""; _tAlPush.Text = c.PushCommand ?? "";
            _tAlStatus.Text = (fromFile ? "reloaded from " : "loaded ") + SentinelCore.SettingsDir + "\\Alerts.conf";
        }

        private void SaveAlertConfig()
        {
            var svc = SentinelAlertService_v1_0_0.Instance;
            if (svc == null) { _tAlStatus.Text = "service not loaded — can't apply (F5 to compile it in)."; return; }
            double thr; double.TryParse(_tAlThrottle.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out thr);
            var c = new SentinelAlertService_v1_0_0.AlertChannelConfig
            {
                Enabled = _tAlEnabled.IsChecked == true, PlayInfo = _tAlPlayInfo.IsChecked == true, PushOnInfo = _tAlPushOnInfo.IsChecked == true,
                ThrottleSec = thr, CritWav = _tAlCritWav.Text, InfoWav = _tAlInfoWav.Text, PushCommand = _tAlPush.Text
            };
            svc.Apply(c);
            _tAlStatus.Text = "saved + applied live  ·  " + SentinelCore.SettingsDir + "\\Alerts.conf";
        }

        private void FireTestAlert(bool crit)
        {
            if (crit) SentinelCore.Alerts.Critical("Test alert (Critical)", "fired from the dashboard Test tab");
            else SentinelCore.Alerts.Info("Test alert (Info)", "fired from the dashboard Test tab");
            bool svcOn = SentinelAlertService_v1_0_0.Instance != null;
            if (_tAlStatus != null) _tAlStatus.Text = "fired a " + (crit ? "Critical" : "Info") + " test alert → "
                + (svcOn ? ("expect sound" + (crit ? " + push (if configured)" : (_tAlPlayInfo.IsChecked == true ? "" : " — Info sound is OFF")) + ",") : "alert service NOT loaded (no sound),")
                + " a row in Risk ▸ Recent alerts, and a line in today's ledger audit below.";
        }

        private void RunProbe()
        {
            if (_tProbeResult == null) return;
            Account acct = ResolveAccountByName(_tProbeAcct.SelectedItem as string ?? _tProbeAcct.Text);
            if (acct == null) { _tProbeResult.Foreground = Amber; _tProbeResult.Text = "pick a valid account."; return; }
            string instrName = (_tProbeInstr.Text ?? "").Trim();
            NinjaTrader.Cbi.Instrument instr = null;
            try { if (!string.IsNullOrEmpty(instrName)) instr = NinjaTrader.Cbi.Instrument.GetInstrument(instrName); } catch (Exception _sx) { SentinelCore.Swallow("SentinelDashboard.RunProbe", _sx); }
            int qty; if (!int.TryParse(_tProbeQty.Text, out qty) || qty < 1) qty = 1;
            double stopTk; double.TryParse(_tProbeStop.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out stopTk);
            double risk; double.TryParse(_tProbeRisk.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out risk);

            var gate = SentinelCore.GateEntry(acct, instrName, qty, stopTk, risk, instr);
            int sfr = (instr != null && stopTk > 0 && risk > 0) ? SentinelCore.SizeForRisk(acct, instr, stopTk, risk) : -1;
            Brush col = gate.Level == SentinelCore.GateLevel.Hard ? Red : (gate.Level == SentinelCore.GateLevel.Advisory ? Amber : Green);
            string line1 = "GATE: " + gate.Level.ToString().ToUpperInvariant() + " — " + (gate.Reason ?? "clear to enter") + "   → sized qty " + gate.Size;
            string line2 = (instr != null ? "TickValue $" + SentinelCore.TickValue(instr).ToString("0.##", CultureInfo.InvariantCulture)
                                           : "instrument '" + instrName + "' not resolved — TickValue/SizeForRisk unavailable")
                + (sfr >= 0 ? "   ·   SizeForRisk($" + risk.ToString("0.##", CultureInfo.InvariantCulture) + " / " + stopTk.ToString("0.#", CultureInfo.InvariantCulture) + "tk) = " + sfr : "");
            _tProbeResult.Foreground = col;
            _tProbeResult.Text = line1 + "\n" + line2;
            SentinelCore.Log("Test", "probe " + acct.Name + " " + instrName + " q" + qty + " → " + gate.Level + " (" + gate.Reason + "), size " + gate.Size);
        }

        private void RunSelfChecks()
        {
            if (_tChecks == null) return;
            _tChecks.Children.Clear();
            int pass = 0, total = 0;

            // 1. scoped-kill isolation — fake roots only (never touches a real instrument)
            total++;
            try
            {
                const string A = "ZZSELFTESTA", B = "ZZSELFTESTB";
                SentinelCore.SetInstrumentKill(A, true, "selftest");
                bool aKilled = SentinelCore.InstrumentKillEngaged(A + " 03-25");
                bool bKilled = SentinelCore.InstrumentKillEngaged(B + " 03-25");
                SentinelCore.SetInstrumentKill(A, false, "selftest");
                bool ok = aKilled && !bKilled; if (ok) pass++;
                _tChecks.Children.Add(CheckRow(ok, "Scoped kill isolates one root",
                    ok ? "killing " + A + " blocked only it, not " + B : "expected A blocked & B clear (got " + aKilled + "/" + bKilled + ")"));
            }
            catch (Exception ex) { _tChecks.Children.Add(CheckRow(false, "Scoped kill isolates one root", "error: " + ex.Message)); }

            // shared probe account + instrument for the sizer/tick checks
            Account acct = ResolveAccountByName(_tProbeAcct != null ? (_tProbeAcct.SelectedItem as string ?? _tProbeAcct.Text) : null);
            NinjaTrader.Cbi.Instrument instr = null;
            try { if (_tProbeInstr != null && !string.IsNullOrEmpty(_tProbeInstr.Text)) instr = NinjaTrader.Cbi.Instrument.GetInstrument(_tProbeInstr.Text.Trim()); } catch (Exception _sx) { SentinelCore.Swallow("SentinelDashboard.RunSelfChecks", _sx); }

            // 2. risk sizer: unaffordable → 0, generous → ≥1
            total++;
            if (instr == null || acct == null)
                _tChecks.Children.Add(CheckRow(false, "Risk sizer: unaffordable → 0, generous → ≥1", "set a valid Account + Instrument in the probe above, then re-run"));
            else
            {
                try
                {
                    int affordable = SentinelCore.SizeForRisk(acct, instr, 10, 100000);
                    int tiny = SentinelCore.SizeForRisk(acct, instr, 10, 0.01);
                    bool ok = tiny == 0 && affordable >= 1; if (ok) pass++;
                    _tChecks.Children.Add(CheckRow(ok, "Risk sizer: unaffordable → 0, generous → ≥1",
                        "$0.01→" + tiny + ", $100k→" + affordable + " (tick $" + SentinelCore.TickValue(instr).ToString("0.##", CultureInfo.InvariantCulture) + ")"));
                }
                catch (Exception ex) { _tChecks.Children.Add(CheckRow(false, "Risk sizer", "error: " + ex.Message)); }
            }

            // 3. TickValue resolves > 0
            total++;
            if (instr != null)
            {
                double tv = 0; try { tv = SentinelCore.TickValue(instr); } catch (Exception _sx) { SentinelCore.Swallow("SentinelDashboard.RunSelfChecks", _sx); }
                bool ok = tv > 0; if (ok) pass++;
                _tChecks.Children.Add(CheckRow(ok, "TickValue resolves > 0", instr.FullName + " = $" + tv.ToString("0.####", CultureInfo.InvariantCulture)));
            }
            else _tChecks.Children.Add(CheckRow(false, "TickValue resolves > 0", "no instrument set"));

            _tChecks.Children.Add(MonoLine("• Global kill / governor halt: engage it (top bar) then use the probe above → gate returns HARD. (Not auto-tested, to avoid toggling live safety controls.)", Muted));

            _tChecksStatus.Text = pass + "/" + total + " checks passed";
            _tChecksStatus.Foreground = pass == total ? Green : (pass > 0 ? Amber : Red);
            SentinelCore.Log("Test", "self-checks " + pass + "/" + total + " passed");
        }

        private Border CheckRow(bool ok, string title, string detail)
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var badge = Chip(ok ? "PASS" : "FAIL", ok ? Green : Red); badge.Margin = new Thickness(0, 0, 8, 0);
            DockPanel.SetDock(badge, Dock.Left); row.Children.Add(badge);
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = title, Foreground = Text, FontFamily = new FontFamily("Consolas"), FontSize = 11 });
            if (!string.IsNullOrEmpty(detail)) sp.Children.Add(new TextBlock { Text = detail, Foreground = Muted, FontFamily = new FontFamily("Consolas"), FontSize = 10, TextWrapping = TextWrapping.Wrap });
            row.Children.Add(sp);
            return new Border { Padding = new Thickness(0, 1, 0, 3), Child = row };
        }

        private void RunLedgerAudit()
        {
            if (_tVerify == null) return;
            _tVerify.Children.Clear();
            List<SentinelCore.Ledger.Entry> es;
            try { es = SentinelCore.Ledger.ReadRecent(1); }
            catch (Exception ex) { _tVerifyStatus.Text = "ledger read failed: " + ex.Message; return; }

            int orders = 0, fills = 0, killEng = 0, killRel = 0, flatten = 0, critAlert = 0, infoAlert = 0, restore = 0;
            foreach (var e in es)
            {
                if (e.IsOrder) { orders++; continue; }
                if (e.IsFill) { fills++; continue; }
                string k = e.Kind ?? "";
                if (k == "kill-engaged") killEng++;
                else if (k == "kill-released") killRel++;
                else if (k.IndexOf("flatten", StringComparison.OrdinalIgnoreCase) >= 0) flatten++;
                else if (k == "ALERT-CRIT") critAlert++;
                else if (k == "alert") infoAlert++;
                else if (k.StartsWith("restore", StringComparison.OrdinalIgnoreCase)) restore++;
            }
            _tVerify.Children.Add(AuditRow("Orders submitted", orders, Accent));
            _tVerify.Children.Add(AuditRow("Fills captured", fills, Green));
            _tVerify.Children.Add(AuditRow("Kill engaged", killEng, killEng > 0 ? Red : Muted));
            _tVerify.Children.Add(AuditRow("Kill released", killRel, Muted));
            _tVerify.Children.Add(AuditRow("Auto-flatten fired", flatten, flatten > 0 ? Red : Muted));
            _tVerify.Children.Add(AuditRow("Critical alerts", critAlert, critAlert > 0 ? Red : Muted));
            _tVerify.Children.Add(AuditRow("Info alerts", infoAlert, Muted));
            _tVerify.Children.Add(AuditRow("Position restores", restore, restore > 0 ? Amber : Muted));
            _tVerifyStatus.Text = es.Count + " events today  ·  " + SentinelCore.Ledger.FileFor(DateTime.Now.Date);
            _tVerifyStatus.Foreground = Muted;
        }

        private Border AuditRow(string label, int count, Brush countCol)
        {
            var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
            var c = new TextBlock { Text = count.ToString(), Foreground = countCol, FontFamily = new FontFamily("Consolas"), FontSize = 12, FontWeight = FontWeights.SemiBold, Width = 44 };
            DockPanel.SetDock(c, Dock.Left); row.Children.Add(c);
            row.Children.Add(new TextBlock { Text = label, Foreground = Ink2, FontFamily = new FontFamily("Consolas"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
            return new Border { Child = row };
        }

        private static Account ResolveAccountByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            lock (Account.All)
            {
                foreach (Account a in Account.All)
                    if (a != null && string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)) return a;
            }
            return null;
        }

        // ── LENS TAB (analytics over Sentinel\Log JSONL) — on-demand, read-only ──────
        private FrameworkElement BuildLensTab()
        {
            var panel = new StackPanel { Margin = new Thickness(12) };

            var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            btns.Children.Add(MakeButton("Load / Refresh analytics", (s, e) => RefreshLensLoad()));
            panel.Children.Add(btns);

            _lensStatus = new TextBlock { Foreground = Muted, FontFamily = new FontFamily("Consolas"),
                FontSize = 10, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
            panel.Children.Add(_lensStatus);

            _lensTiles = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            panel.Children.Add(_lensTiles);

            // ── Eye-partition: the profit question — do Eye-endorsed trades out-earn the rest? ──
            panel.Children.Add(Label("Eye filter — does it add edge?", true));
            _lensEyeVerdict = new TextBlock { Foreground = Text, FontFamily = new FontFamily("Consolas"),
                FontSize = 11, Margin = new Thickness(0, 2, 0, 6), TextWrapping = TextWrapping.Wrap };
            panel.Children.Add(_lensEyeVerdict);
            _lensEye = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            panel.Children.Add(_lensEye);
            panel.Children.Add(new TextBlock { Text = "Score-band curve (where does expectancy turn positive?)",
                Foreground = Muted, FontFamily = new FontFamily("Consolas"), FontSize = 10, Margin = new Thickness(0, 2, 0, 2) });
            _lensBand = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            panel.Children.Add(_lensBand);

            panel.Children.Add(Label("By strategy", true));
            _lensStrat = new StackPanel { Margin = new Thickness(0, 4, 0, 10) };
            panel.Children.Add(_lensStrat);

            panel.Children.Add(Label("By instrument", true));
            _lensInst = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            panel.Children.Add(_lensInst);

            return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel, Background = Bg };
        }

        private void RefreshLensLoad()
        {
            if (_lensTiles == null) return;
            var s = SentinelLens_v1_1_0.LoadSummary();
            if (s.Error != null)
            {
                _lensStatus.Text = s.Error;
                _lensTiles.Children.Clear(); _lensStrat.Children.Clear(); _lensInst.Children.Clear();
                _lensEye.Children.Clear(); _lensBand.Children.Clear(); _lensEyeVerdict.Text = "";
                return;
            }
            _lensStatus.Text = "loaded " + s.TradesParsed + " trades from " + s.FilesRead + " file(s)  ·  " + s.LogDir;

            var o = s.Overall;
            _lensTiles.Children.Clear();
            _lensTiles.Children.Add(LogTile("Trades", o.Trades.ToString(), Text));
            _lensTiles.Children.Add(LogTile("Win rate", o.WinRate.ToString("0") + "%", o.WinRate >= 50 ? Green : Amber));
            _lensTiles.Children.Add(LogTile("Net ticks", (o.NetTicks >= 0 ? "+" : "") + o.NetTicks.ToString("0"), o.NetTicks >= 0 ? Green : Red));
            _lensTiles.Children.Add(LogTile("Profit factor", Pf(o.ProfitFactor), o.ProfitFactor >= 1.5 ? Green : (o.ProfitFactor >= 1 ? Amber : Red)));
            _lensTiles.Children.Add(LogTile("Avg MAE", "-" + o.AvgMae.ToString("0.0") + "t", Amber));
            _lensTiles.Children.Add(LogTile("Avg MFE", "+" + o.AvgMfe.ToString("0.0") + "t", Green));
            _lensTiles.Children.Add(LogTile("MFE capture", o.MfeCapturePct.ToString("0") + "%", Text));

            // Eye-partition — the headline profit question
            _lensEyeVerdict.Text = s.EyeVerdict ?? "";
            _lensEye.Children.Clear();
            if (s.ByEye.Count == 0) _lensEye.Children.Add(MonoLine("—", Muted));
            foreach (var g in s.ByEye) _lensEye.Children.Add(EyeGroupLine(g));

            _lensBand.Children.Clear();
            if (s.ByEyeScoreBand.Count == 0)
                _lensBand.Children.Add(MonoLine("— (no trades carried an Eye verdict yet)", Muted));
            foreach (var g in s.ByEyeScoreBand) _lensBand.Children.Add(GroupLine(g));

            _lensStrat.Children.Clear();
            if (s.ByStrategy.Count == 0) _lensStrat.Children.Add(MonoLine("—", Muted));
            else { _lensStrat.Children.Add(LensNetChart(s.ByStrategy)); _lensStrat.Children.Add(LensDivider()); }
            foreach (var g in s.ByStrategy) _lensStrat.Children.Add(GroupLine(g));

            _lensInst.Children.Clear();
            if (s.ByInstrument.Count == 0) _lensInst.Children.Add(MonoLine("—", Muted));
            else { _lensInst.Children.Add(LensNetChart(s.ByInstrument)); _lensInst.Children.Add(LensDivider()); }
            foreach (var g in s.ByInstrument) _lensInst.Children.Add(GroupLine(g));

            _lensStatus.Text += "  ·  Eye verdict on " + s.EyeCoverage + "/" + s.TradesParsed + " trades";
            if (s.TierSkipped > 0) _lensStatus.Text += "  ·  " + s.TierSkipped + " tier-2 dupes skipped (tier-1 = source of truth)";
            SentinelCore.Log("Lens", "loaded " + s.TradesParsed + " trades: winrate " + o.WinRate.ToString("0")
                + "%, net " + o.NetTicks.ToString("0") + "t, PF " + Pf(o.ProfitFactor)
                + ", avgMAE " + o.AvgMae.ToString("0.0") + ", avgMFE " + o.AvgMfe.ToString("0.0")
                + ", eyeCoverage " + s.EyeCoverage + "/" + s.TradesParsed);
        }

        private static string Pf(double pf) { return double.IsPositiveInfinity(pf) ? "∞" : pf.ToString("0.00"); }

        private TextBlock GroupLine(SentinelLens_v1_1_0.Group g)
        {
            string txt = g.Key + "   " + g.Trades + " tr · " + g.WinRate.ToString("0") + "% · net "
                + (g.NetTicks >= 0 ? "+" : "") + g.NetTicks.ToString("0") + "t · PF " + Pf(g.ProfitFactor)
                + " · MAE -" + g.AvgMae.ToString("0.0") + " · MFE +" + g.AvgMfe.ToString("0.0");
            return MonoLine(txt, g.NetTicks >= 0 ? Green : Red);
        }

        // VISUAL: net-ticks diverging bar per group (green = profitable, red = losing) — the at-a-glance edge.
        private FrameworkElement LensNetChart(List<SentinelLens_v1_1_0.Group> groups)
        {
            var labels = new List<string>(); var vals = new List<double>();
            foreach (var g in groups) { labels.Add(g.Key + " (" + g.Trades + ")"); vals.Add(g.NetTicks); }
            var wrap = new StackPanel();
            wrap.Children.Add(new TextBlock { Text = "net ticks — green = profitable, red = losing", Foreground = Muted,
                FontFamily = new FontFamily("Consolas"), FontSize = 9, Margin = new Thickness(0, 0, 0, 3) });
            wrap.Children.Add(SignedBars(labels, vals, v => (v >= 0 ? "+" : "") + v.ToString("0") + "t", true, 150, 210));
            return wrap;
        }
        private Border LensDivider() => new Border { Height = 1, Background = Faint, Margin = new Thickness(0, 8, 0, 6) };

        // Eye-partition row: category-colored, leads with avg ticks/trade (the expectancy that matters)
        private TextBlock EyeGroupLine(SentinelLens_v1_1_0.Group g)
        {
            string label = g.Key == "Endorsed" ? "Endorsed    " : (g.Key == "NotEndorsed" ? "Not-endorsed" : "No-verdict  ");
            string txt = label + "  " + g.Trades + " tr · " + g.WinRate.ToString("0") + "% · "
                + (g.AvgNet >= 0 ? "+" : "") + g.AvgNet.ToString("0.00") + " t/trade · PF " + Pf(g.ProfitFactor)
                + " · net " + (g.NetTicks >= 0 ? "+" : "") + g.NetTicks.ToString("0") + "t";
            var color = g.Key == "Endorsed" ? (g.AvgNet >= 0 ? Green : Red)
                      : (g.Key == "NotEndorsed" ? Amber : Muted);
            return MonoLine(txt, color);
        }

        // ── EYE TAB (SentinelEye qualification verdicts per instrument) ──────────────
        private FrameworkElement BuildEyeTab()
        {
            var panel = new StackPanel { Margin = new Thickness(12) };
            _eyeStatus = new TextBlock { Foreground = Text, FontFamily = new FontFamily("Consolas"),
                FontSize = 12, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
            panel.Children.Add(_eyeStatus);
            _eyeTiles = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 10) };
            panel.Children.Add(_eyeTiles);

            panel.Children.Add(Label("Qualified verdicts (per instrument, from SentinelEye charts)", true));
            _eyePanel = new StackPanel { Margin = new Thickness(0, 4, 0, 10) };
            panel.Children.Add(_eyePanel);

            panel.Children.Add(new TextBlock {
                Text = "Copier Eye-gate: set eyeGate=on in Copy.conf to mirror only Eye-qualified ENTRIES "
                     + "(exits always mirror). OFF by default — Eye is informational until you trust it.",
                Foreground = Muted, FontFamily = new FontFamily("Consolas"), FontSize = 10,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });

            return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel, Background = Bg };
        }

        private void RefreshEyeLive()
        {
            if (_eyePanel == null) return;
            var verdicts = SentinelCore.AllEyeVerdicts();
            _eyeStatus.Text = "SentinelEye: " + verdicts.Count + " instrument(s) publishing";
            if (_eyeTiles != null)
            {
                _eyeTiles.Children.Clear();
                int lng = 0, sht = 0, neu = 0;
                foreach (var v in verdicts) { if (v == null) continue; if (v.Direction > 0) lng++; else if (v.Direction < 0) sht++; else neu++; }
                _eyeTiles.Children.Add(StatTile("Publishing", verdicts.Count.ToString(), Text, "instruments"));
                _eyeTiles.Children.Add(StatTile("Qualified", (lng + sht).ToString(), (lng + sht) > 0 ? Accent : Muted, lng + " long · " + sht + " short"));
                _eyeTiles.Children.Add(StatTile("Neutral", neu.ToString(), Muted, "not qualified"));
            }
            _eyePanel.Children.Clear();
            if (verdicts.Count == 0)
            {
                _eyePanel.Children.Add(MonoLine("no SentinelEye indicator running — add SentinelEyeV1_0 to a chart", Muted));
                return;
            }

            // ── VISUAL: signed score per instrument — right/green = qualified LONG, left/red = qualified SHORT ──
            var eLabels = new List<string>(); var eVals = new List<double>();
            foreach (var v in verdicts) { if (v == null) continue; eLabels.Add(v.Instrument); eVals.Add(v.Direction * Math.Abs(v.Score)); }
            _eyePanel.Children.Add(new TextBlock { Text = "qualification — right/green = LONG, left/red = SHORT, bar = score",
                Foreground = Muted, FontFamily = new FontFamily("Consolas"), FontSize = 9, Margin = new Thickness(0, 0, 0, 3) });
            _eyePanel.Children.Add(HDivBars(eLabels, eVals, v => (v > 0 ? "L " : (v < 0 ? "S " : "· ")) + Math.Abs(v).ToString("0"), true, 120, 210));
            _eyePanel.Children.Add(new Border { Height = 1, Background = Faint, Margin = new Thickness(0, 8, 0, 6) });

            foreach (var v in verdicts)
            {
                if (v == null) continue;
                string dir = v.Direction > 0 ? "QUALIFIED LONG" : (v.Direction < 0 ? "QUALIFIED SHORT" : "neutral (not qualified)");
                Brush b = v.Direction > 0 ? Green : (v.Direction < 0 ? Red : Muted);
                double ageSec = 0; try { ageSec = (DateTime.Now.ToUniversalTime() - v.UpdatedUtc).TotalSeconds; } catch (Exception _sx) { SentinelCore.Swallow("SentinelDashboard.RefreshEyeLive", _sx); }
                _eyePanel.Children.Add(MonoLine(
                    v.Instrument + "   " + dir + "   score " + v.Score.ToString("0") + "   src " + v.Source
                    + "   (" + ageSec.ToString("0") + "s)", b));
            }
        }

        // ── ARC TAB (fleet orchestration — read SentinelCore fleet registry) ─────────
        private FrameworkElement BuildArcTab()
        {
            var panel = new StackPanel { Margin = new Thickness(12) };

            var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            btns.Children.Add(MakeButton("Reload Arc.conf", (s, e) =>
            {
                var svc = SentinelArcService_v0_1_0.Instance;
                if (svc != null) svc.ReloadFromFile();
                RefreshArcLive();
            }));
            panel.Children.Add(btns);

            _arcStatus = new TextBlock { Foreground = Text, FontFamily = new FontFamily("Consolas"),
                FontSize = 12, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
            panel.Children.Add(_arcStatus);
            _arcTiles = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 10) };
            panel.Children.Add(_arcTiles);

            panel.Children.Add(Label("Fleet (live) — enable/disable gates entries for Sentinel-aware strategies", true));
            _arcPanel = new StackPanel { Margin = new Thickness(0, 4, 0, 10) };
            panel.Children.Add(_arcPanel);

            panel.Children.Add(new TextBlock
            {
                Text = "Arc publishes this plan; GTrader21 obeys it via SlotLive() once wired (v0.2.0). "
                     + "Add/remove slots by editing Sentinel\\Arc.conf then Reload. Health: OFF / "
                     + "CLOSED (out of session) / IDLE (waiting) / LIVE (in position) / DARK (leader offline).",
                Foreground = Muted, FontFamily = new FontFamily("Consolas"), FontSize = 10,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0)
            });

            return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel, Background = Bg };
        }

        private void RefreshArcLive()
        {
            if (_arcPanel == null) return;
            var svc = SentinelArcService_v0_1_0.Instance;
            var slots = SentinelCore.AllFleetSlots();
            string leader = svc != null ? svc.CurrentConfig().Leader : "?";
            _arcStatus.Text = "SentinelArc: leader '" + leader + "'  ·  " + slots.Count + " slot(s) in the fleet plan"
                + (svc == null ? "   (service not running)" : "");
            if (_arcTiles != null)
            {
                _arcTiles.Children.Clear();
                int live = 0, on = 0, dark = 0; double fleetPnl = 0;
                foreach (var fs in slots) { if (fs == null) continue; if (fs.Enabled) on++; if (fs.Health == "LIVE") live++; if (fs.Health == "DARK") dark++; fleetPnl += fs.DayPnl; }
                _arcTiles.Children.Add(StatTile("Fleet slots", on + " / " + slots.Count, Text, "enabled"));
                _arcTiles.Children.Add(StatTile("Live", live.ToString(), live > 0 ? Green : Muted, dark + " dark"));
                _arcTiles.Children.Add(StatTile("Fleet P&L", (fleetPnl >= 0 ? "+$" : "-$") + Math.Abs(Math.Round(fleetPnl)), fleetPnl >= 0 ? Green : Red, "leader " + leader));
            }
            _arcPanel.Children.Clear();
            if (slots.Count == 0)
            {
                _arcPanel.Children.Add(MonoLine("no slots — edit Sentinel\\Arc.conf (e.g. slot=GC|GTrader21|on|1|24h) then Reload", Muted));
                return;
            }
            slots.Sort((a, b) => string.Compare(a.Instrument, b.Instrument, StringComparison.OrdinalIgnoreCase));

            // ── VISUAL: day P&L per fleet slot (green = up, red = down) ──
            bool anyPnl = false; foreach (var fs in slots) if (fs != null && fs.DayPnl != 0) { anyPnl = true; break; }
            if (anyPnl)
            {
                var aLabels = new List<string>(); var aVals = new List<double>();
                foreach (var fs in slots) { if (fs == null) continue; aLabels.Add(fs.Instrument + (fs.PositionQty != 0 ? " (" + (fs.PositionQty > 0 ? "+" : "") + fs.PositionQty + ")" : "")); aVals.Add(fs.DayPnl); }
                _arcPanel.Children.Add(new TextBlock { Text = "day P&L per slot", Foreground = Muted, FontFamily = new FontFamily("Consolas"), FontSize = 9, Margin = new Thickness(0, 0, 0, 3) });
                _arcPanel.Children.Add(SignedBars(aLabels, aVals, v => (v >= 0 ? "+$" : "-$") + Math.Abs(v).ToString("0"), true, 130, 200));
                _arcPanel.Children.Add(new Border { Height = 1, Background = Faint, Margin = new Thickness(0, 8, 0, 6) });
            }

            foreach (var fs in slots)
                if (fs != null) _arcPanel.Children.Add(BuildArcRow(fs));
        }

        private FrameworkElement BuildArcRow(SentinelCore.FleetSlot fs)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };

            string inst = fs.Instrument;
            var toggle = MakeButton(fs.Enabled ? "Disable" : "Enable", (s, e) => ToggleArcSlot(inst));
            toggle.MinWidth = 72; toggle.Margin = new Thickness(0, 0, 8, 0);
            row.Children.Add(toggle);

            string health = fs.Health ?? "?";
            Brush hb = health == "LIVE" ? Green : (health == "DARK" ? Red : (health == "IDLE" ? Text : Muted));
            string pos = fs.PositionQty == 0 ? "flat" : ((fs.PositionQty > 0 ? "+" : "") + fs.PositionQty);
            string pnl = fs.DayPnl == 0 ? "" : ("  ·  " + (fs.DayPnl >= 0 ? "+$" : "-$") + Math.Abs(fs.DayPnl).ToString("0"));
            string sess = SentinelArcService_v0_1_0.SessionText(fs.SessionStartMin, fs.SessionEndMin)
                + (fs.InSession ? "" : " (closed)");

            row.Children.Add(new TextBlock
            {
                Text = (inst ?? "?").PadRight(4) + "  " + (fs.Strategy ?? "?") + "  ·  " + sess + "  ·  " + health
                     + "  ·  pos " + pos + "  ·  " + fs.FillsToday + "f" + pnl,
                Foreground = hb, FontFamily = new FontFamily("Consolas"), FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });
            return row;
        }

        private void ToggleArcSlot(string instrument)
        {
            var svc = SentinelArcService_v0_1_0.Instance;
            if (svc == null) return;
            var cfg = svc.CurrentConfig();
            foreach (var s in cfg.Slots)
                if (string.Equals(s.Instrument, instrument, StringComparison.OrdinalIgnoreCase)) { s.Enabled = !s.Enabled; break; }
            svc.Reconfigure(cfg);
            RefreshArcLive();
        }

        // ── ASSIST TAB (manual-assist ticket queue — place these by hand) ────────────
        private FrameworkElement BuildAssistTab()
        {
            var panel = new StackPanel { Margin = new Thickness(12) };
            _assistStatus = new TextBlock { Foreground = Text, FontFamily = new FontFamily("Consolas"),
                FontSize = 12, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
            panel.Children.Add(_assistStatus);
            _assistTiles = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 10) };
            panel.Children.Add(_assistTiles);

            panel.Children.Add(Label("Place-by-hand tickets (newest first)", true));
            _assistPanel = new StackPanel { Margin = new Thickness(0, 4, 0, 10) };
            panel.Children.Add(_assistPanel);

            panel.Children.Add(new TextBlock
            {
                Text = "Manual-assist: set a follower to 'manual' in Copy.conf (follower=<label>|manual|<mult>|<map>) "
                     + "for prop accounts that bar automated copy-trading. The Copier emits a ticket here instead "
                     + "of submitting — you place it on your prop platform. Eye-gate still applies to entries.",
                Foreground = Muted, FontFamily = new FontFamily("Consolas"), FontSize = 10,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0)
            });

            return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel, Background = Bg };
        }

        private void RefreshAssistLive()
        {
            if (_assistPanel == null) return;
            var tickets = SentinelCore.RecentAssistTickets(25);
            _assistStatus.Text = "Manual-assist: " + tickets.Count + " recent ticket(s)";
            if (_assistTiles != null)
            {
                _assistTiles.Children.Clear();
                int entries = 0; string newest = "—";
                foreach (var t in tickets) { if (t != null && t.IsEntry) entries++; }
                if (tickets.Count > 0 && tickets[0] != null)
                {
                    double ageSec = 0; try { ageSec = (DateTime.Now.ToUniversalTime() - tickets[0].TimeUtc).TotalSeconds; } catch (Exception _sx) { SentinelCore.Swallow("SentinelDashboard.RefreshAssistLive", _sx); }
                    newest = ageSec < 90 ? ((int)ageSec) + "s ago" : ((int)(ageSec / 60)) + "m ago";
                }
                _assistTiles.Children.Add(StatTile("Tickets", tickets.Count.ToString(), tickets.Count > 0 ? Accent : Muted, "to place by hand"));
                _assistTiles.Children.Add(StatTile("Entries", entries.ToString(), Text, (tickets.Count - entries) + " exits"));
                _assistTiles.Children.Add(StatTile("Newest", newest, Muted, "last ticket"));
            }
            _assistPanel.Children.Clear();
            if (tickets.Count == 0)
            {
                _assistPanel.Children.Add(MonoLine("no tickets yet — trade the leader with a 'manual' follower configured", Muted));
                return;
            }
            foreach (var t in tickets)
            {
                if (t == null) continue;
                double ageSec = 0; try { ageSec = (DateTime.Now.ToUniversalTime() - t.TimeUtc).TotalSeconds; } catch (Exception _sx) { SentinelCore.Swallow("SentinelDashboard.RefreshAssistLive", _sx); }
                // entries colored by side (Buy green / SellShort red); exits muted
                Brush b = !t.IsEntry ? Muted : (t.Action == "Buy" ? Green : Red);
                string age = ageSec < 90 ? ((int)ageSec) + "s" : ((int)(ageSec / 60)) + "m";
                _assistPanel.Children.Add(MonoLine(
                    "▶ " + t.Action.ToUpperInvariant() + " " + t.Qty + " " + t.Instrument
                    + "  on " + t.Account + "   [" + t.Context + "]   " + age + " ago", b));
            }
        }

        // ── EXCURSION TAB (signal characterization — max MAE/MFE per signal, no execution) ──
        private FrameworkElement BuildExcursionTab()
        {
            var root = new Grid { Background = Bg };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // ── header: controls · status · tiles · live configs ──
            var head = new StackPanel { Margin = new Thickness(12, 10, 12, 6) };

            var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            btns.Children.Add(MakeButton("Load / Refresh", (s, e) => RefreshExcursionLoad()));
            _excConfOnly = new CheckBox { Content = "Confident only (n≥" + ExcConfidentN + ")", IsChecked = false,
                Foreground = Text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            _excConfOnly.Checked   += (s, e) => RefreshExcursionLoad();
            _excConfOnly.Unchecked += (s, e) => RefreshExcursionLoad();
            btns.Children.Add(_excConfOnly);
            btns.Children.Add(MakeButton("Sync all ◆ configs", (s, e) => SyncAllConfigs()));
            head.Children.Add(btns);

            _excStatus = new TextBlock { Foreground = Muted, FontFamily = new FontFamily("Consolas"),
                FontSize = 11, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
            head.Children.Add(_excStatus);

            _excTiles = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            head.Children.Add(_excTiles);

            head.Children.Add(Label("Live lab configs — running strategies auto-reading a .conf", true));
            _excConfigs = new StackPanel { Margin = new Thickness(0, 2, 0, 0) };
            head.Children.Add(_excConfigs);

            Grid.SetRow(head, 0);
            root.Children.Add(head);

            // ── body: two columns — edge list (master) | detail (deep-dive) ──
            var body = new Grid { Margin = new Thickness(12, 4, 12, 10) };
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(500) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // LEFT — ranked edge list; click a signal to drive the detail
            var left = new Grid();
            left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var lHead = new TextBlock { Text = "EDGE  ·  median MFE vs MAE @ 15m  ·  ranked  ·  click a signal",
                Foreground = Text, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Segoe UI"),
                FontSize = 12, Margin = new Thickness(0, 0, 0, 6) };
            Grid.SetRow(lHead, 0); left.Children.Add(lHead);
            _excChart = new StackPanel();
            var leftScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _excChart };
            Grid.SetRow(leftScroll, 1); left.Children.Add(leftScroll);
            Grid.SetColumn(left, 0); body.Children.Add(left);

            var divider = new Border { Width = 1, Background = Edge, Margin = new Thickness(14, 4, 14, 0) };
            Grid.SetColumn(divider, 1); body.Children.Add(divider);

            // RIGHT — the selected signal's deep-dive
            var right = new Grid();
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var rHead = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            rHead.Children.Add(new TextBlock { Text = "DETAIL ▸ ", Foreground = Text, FontWeight = FontWeights.SemiBold,
                FontFamily = new FontFamily("Segoe UI"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            _excSel = new ComboBox { MinWidth = 150 };
            _excSel.SelectionChanged += OnExcSelChanged;
            rHead.Children.Add(_excSel);
            Grid.SetRow(rHead, 0); right.Children.Add(rHead);
            _excDetail = new StackPanel();
            var rightScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _excDetail };
            Grid.SetRow(rightScroll, 1); right.Children.Add(rightScroll);
            Grid.SetColumn(right, 2); body.Children.Add(right);

            Grid.SetRow(body, 1);
            root.Children.Add(body);

            return root;
        }

        private void RefreshExcursionLoad()
        {
            if (_excChart == null) return;
            var s = SentinelExcursions_v1_0.LoadSummary();
            if (s.Error != null)
            {
                _excStatus.Text = s.Error;
                if (_excChart != null) _excChart.Children.Clear();
                if (_excDetail != null) _excDetail.Children.Clear();
                if (_excTiles != null) _excTiles.Children.Clear();
                return;
            }
            _excStatus.Text = string.Format("{0:n0} records · {1} signals · {2} files", s.Records, s.Groups.Count, s.FilesRead);
            _excStatus.ToolTip = s.Dir;
            if (_excTiles != null)
            {
                _excTiles.Children.Clear();
                string bestKey = null; double bestEdge = double.NegativeInfinity; int edgePos = 0, edgeN = 0;
                foreach (var g in s.Groups)
                {
                    if (g == null || g.ByRegime == null) continue;
                    SentinelExcursions_v1_0.Sub tr;
                    if (!g.ByRegime.TryGetValue("trend", out tr) || tr == null || tr.N < 8) continue;
                    edgeN++;
                    double edge = tr.MfeMed15 - tr.MaeMed15;
                    if (edge > 0) edgePos++;
                    if (edge > bestEdge) { bestEdge = edge; bestKey = g.Key; }
                }
                _excTiles.Children.Add(StatTile("Signal groups", s.Groups.Count.ToString(), Text, s.Records + " records · " + s.FilesRead + " files"));
                _excTiles.Children.Add(StatTile("Best edge (15m trend)",
                    bestKey == null ? "—" : "+" + Math.Round(bestEdge) + "t",
                    bestEdge > 0 ? Green : Muted, bestKey ?? "need n≥8 trend samples"));
                _excTiles.Children.Add(StatTile("Positive edge",
                    edgeN == 0 ? "—" : edgePos + " / " + edgeN, edgePos > 0 ? Green : Muted,
                    "MFE beats MAE at 15m"));
            }
            // cache, (re)populate the selector (preserving the pick), then draw the chart + detail
            _excSummary = s;
            if (_excSel != null)
            {
                bool confOnly = _excConfOnly != null && _excConfOnly.IsChecked == true;
                string keep = _excSel.SelectedItem as string;
                _excSel.SelectionChanged -= OnExcSelChanged; // avoid redraw storms while repopulating
                _excSel.Items.Clear();
                foreach (var g in s.Groups) { if (confOnly && g.N < ExcConfidentN) continue; _excSel.Items.Add(g.Key); }
                int idx = keep != null ? _excSel.Items.IndexOf(keep) : -1;
                _excSel.SelectedIndex = idx >= 0 ? idx : (_excSel.Items.Count > 0 ? 0 : -1);
                _excSel.SelectionChanged += OnExcSelChanged;
            }
            if (_excChart != null) { _excChart.Children.Clear(); _excChart.Children.Add(BuildExcursionChart(s)); }
            RedrawExcDetail();
        }

        // live: which running GTrader21 instance is on which lab config (SentinelCore config-use registry)
        private void RefreshActiveConfigsLive()
        {
            if (_excConfigs == null) return;
            var uses = SentinelCore.AllConfigUses();
            _excConfigs.Children.Clear();
            if (uses.Count == 0)
            {
                _excConfigs.Children.Add(MonoLine("none — turn on a GTrader21 v0.1.6 instance's 'Auto-read Sentinel Config' (it reports here on load)", Muted));
                return;
            }
            uses.Sort((a, b) => string.Compare(a.Instrument, b.Instrument, StringComparison.OrdinalIgnoreCase));
            foreach (var c in uses)
            {
                if (c == null) continue;
                double ageSec = 0; try { ageSec = (DateTime.Now.ToUniversalTime() - c.UpdatedUtc).TotalSeconds; } catch (Exception _sx) { SentinelCore.Swallow("SentinelDashboard.RefreshActiveConfigsLive", _sx); }
                string age = ageSec < 90 ? ((int)ageSec) + "s" : ((int)(ageSec / 60)) + "m";
                _excConfigs.Children.Add(MonoLine("▶ " + (c.Instrument ?? "?") + "  " + (c.Strategy ?? "?") + " · " + (c.Account ?? "?")
                    + "   → " + (c.ConfigName ?? "?") + "  TP" + c.Tp + " SL" + c.Sl + "   (" + age + " ago)", Green));
            }
        }

        // ── EDGE CHART: one diverging bar per signal group (trend regime), green MFE→ vs ←red MAE
        //    at 15 min, ranked by edge (MFE−MAE). The at-a-glance "which signal has an edge" view. ──
        private FrameworkElement BuildExcursionChart(SentinelExcursions_v1_0.Summary s)
        {
            var wrap = new StackPanel();
            bool confOnly = _excConfOnly != null && _excConfOnly.IsChecked == true;
            var rows = new List<KeyValuePair<SentinelExcursions_v1_0.Group, SentinelExcursions_v1_0.Sub>>();
            double maxMed = 1;
            foreach (var g in s.Groups)
            {
                SentinelExcursions_v1_0.Sub trend;
                if (g.ByRegime != null && g.ByRegime.TryGetValue("trend", out trend) && trend != null && trend.N >= 8
                    && (!confOnly || trend.N >= ExcConfidentN))
                {
                    rows.Add(new KeyValuePair<SentinelExcursions_v1_0.Group, SentinelExcursions_v1_0.Sub>(g, trend));
                    maxMed = Math.Max(maxMed, Math.Max(trend.MfeMed15, trend.MaeMed15));
                }
            }
            if (rows.Count == 0)
            {
                wrap.Children.Add(MonoLine("— need ≥8 trend-regime fires in a signal group to chart", Muted));
                return wrap;
            }
            rows.Sort((a, b) => (b.Value.MfeMed15 - b.Value.MaeMed15).CompareTo(a.Value.MfeMed15 - a.Value.MaeMed15));

            const double plotW = 300.0, half = 150.0;
            double niceMax = NiceCeil(maxMed);
            double scale = half / niceMax;   // px per tick, per side

            // ── scale ruler (aligned to the fixed-width plot): center 0 + quartile gridline ticks ──
            var ruler = new Grid { Margin = new Thickness(0, 2, 0, 3) };
            ruler.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            ruler.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(plotW) });
            ruler.Children.Add(new TextBlock { Text = "← MAE   |   MFE →", Foreground = Muted, FontFamily = new FontFamily("Consolas"),
                FontSize = 9, VerticalAlignment = VerticalAlignment.Bottom });
            var rc = new Canvas { Width = plotW, Height = 15 };
            for (int k = -2; k <= 2; k++)
            {
                double gx = half + k * (half / 2.0);
                rc.Children.Add(new ShapeLine { X1 = gx, Y1 = 2, X2 = gx, Y2 = 12, Stroke = k == 0 ? Edge : Faint, StrokeThickness = 1 });
                double val = Math.Abs(k) * (niceMax / 2.0);
                var t = new TextBlock { Text = Math.Round(val).ToString(), Foreground = Muted, FontFamily = new FontFamily("Consolas"), FontSize = 8 };
                Canvas.SetLeft(t, gx - (k == 0 ? 3 : 7)); Canvas.SetTop(t, 0); rc.Children.Add(t);
            }
            Grid.SetColumn(rc, 1); ruler.Children.Add(rc);
            wrap.Children.Add(ruler);

            string selKey = _excSel != null ? _excSel.SelectedItem as string : null;
            foreach (var kv in rows)
            {
                var g = kv.Key; var t = kv.Value;
                double maePx = Math.Max(0, t.MaeMed15 * scale), mfePx = Math.Max(0, t.MfeMed15 * scale);

                string gkey = g.Key;
                bool selected = gkey != null && gkey == selKey;
                var row = new Grid { Margin = new Thickness(0, 1, 0, 1), Opacity = t.N < ExcConfidentN ? 0.45 : 1.0,  // dim small-sample
                    Background = selected ? Card : System.Windows.Media.Brushes.Transparent,
                    Cursor = System.Windows.Input.Cursors.Hand };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(plotW) });
                row.MouseLeftButtonUp += (bs, be) => { if (_excSel != null) _excSel.SelectedItem = gkey; };
                if (!selected)
                {
                    row.MouseEnter += (bs, be) => { row.Background = Faint; };
                    row.MouseLeave += (bs, be) => { row.Background = System.Windows.Media.Brushes.Transparent; };
                }

                string tag = (g.Instrument + "·" + g.Signal + "·" + (g.Dir > 0 ? "L" : "S"));
                var lbl = new TextBlock { Text = tag.PadRight(11) + " n" + t.N,
                    Foreground = selected ? Accent : (t.HasEdge ? Green : Muted), FontFamily = new FontFamily("Consolas"),
                    FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(lbl, 0); row.Children.Add(lbl);

                // faint quartile gridlines behind the bars (Canvas, aligned to the fixed plot width)
                var barGrid = new Grid { Height = 16, Background = Bg };
                var guides = new Canvas { Width = plotW, Height = 16 };
                for (int k = -2; k <= 2; k++)
                {
                    double gx = half + k * (half / 2.0);
                    guides.Children.Add(new ShapeLine { X1 = gx, Y1 = 1, X2 = gx, Y2 = 15, Stroke = k == 0 ? Edge : Faint, StrokeThickness = 1 });
                }
                barGrid.Children.Add(guides);
                barGrid.Children.Add(new Border { Background = Red, Width = maePx, Height = 11,
                    HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(-maePx, 0, 0, 0), CornerRadius = new CornerRadius(2) });
                barGrid.Children.Add(new Border { Background = Green, Width = mfePx, Height = 11,
                    HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(mfePx, 0, 0, 0), CornerRadius = new CornerRadius(2) });
                barGrid.Children.Add(new TextBlock { Text = "MFE " + Math.Round(t.MfeMed15) + " / MAE " + Math.Round(t.MaeMed15) + (t.HasEdge ? "  ✓" : ""),
                    Foreground = Muted, FontFamily = new FontFamily("Consolas"), FontSize = 9,
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
                Grid.SetColumn(barGrid, 1); row.Children.Add(barGrid);
                wrap.Children.Add(row);
            }
            return wrap;
        }

        // ══ PER-SIGNAL DETAIL (growth line + outcome scatter + expectancy grid) ═══════════════════
        private void OnExcSelChanged(object sender, SelectionChangedEventArgs e) { RedrawExcDetail(); RebuildExcChart(); }

        // rebuild the edge list so the selected signal's row highlights (cheap; ~24 rows)
        private void RebuildExcChart()
        {
            if (_excChart == null || _excSummary == null) return;
            _excChart.Children.Clear();
            _excChart.Children.Add(BuildExcursionChart(_excSummary));
        }

        private void RedrawExcDetail()
        {
            if (_excDetail == null) return;
            _excDetail.Children.Clear();
            var s = _excSummary;
            string key = _excSel != null ? _excSel.SelectedItem as string : null;
            SentinelExcursions_v1_0.Group g = null;
            if (s != null && key != null)
                foreach (var gg in s.Groups) if (gg.Key == key) { g = gg; break; }
            if (g == null) { _excDetail.Children.Add(MonoLine("load, then pick a signal to see its detail", Muted)); return; }

            // header: fire-rate context (a +EV signal that fires twice a month isn't a business)
            var hdr = new TextBlock { Foreground = Text, FontFamily = new FontFamily("Consolas"), FontSize = 12, Margin = new Thickness(0, 2, 0, 0) };
            hdr.Inlines.Add(new System.Windows.Documents.Run(g.Key) { FontWeight = FontWeights.SemiBold });
            hdr.Inlines.Add(new System.Windows.Documents.Run("   " + g.N + " fires · " + g.FiresPerDay.ToString("0.0")
                + "/day · " + g.FireDates.Count + " days") { Foreground = Muted });
            _excDetail.Children.Add(hdr);

            _excDetail.Children.Add(MonoCaption("① Excursion growth — median favorable (green) vs adverse (red), ticks at 5 / 15 / 60 min"));
            _excDetail.Children.Add(ResponsiveHost(w => DrawMilestoneLine(g, w), 1100));
            _excDetail.Children.Add(MonoCaption("② Outcome cloud — each fire: MAE15 (→ x) vs MFE15 (↑ y), by regime (○ = Eye-endorsed); dashed = recommended TP/SL"));
            _excDetail.Children.Add(ResponsiveHost(w => DrawScatter(g, w), 760));
            _excDetail.Children.Add(MonoCaption("③ TP/stop expectancy grid — est. ticks/trade  ·  ★ best raw EV  ·  ◆ best RESPONSIBLE (stop ≤ TP)  ·  dim = wide stop (rarely triggers, inflates EV)"));
            _excDetail.Children.Add(ResponsiveHost(w => DrawExpectancyBars(g, w), 980));
            _excDetail.Children.Add(MonoCaption("④ Eye referee — do Eye-endorsed fires out-earn the rest at 15 min? (realtime-only; accrues as SentinelEye runs)"));
            _excDetail.Children.Add(DrawEyeReferee(g));
            // ⑤ Conviction referee — only for COUNCIL fires (sets the Bridge's MinConviction floor)
            if (g.CouncilCount > 0)
            {
                _excDetail.Children.Add(MonoCaption("⑤ Conviction referee — do HIGH-conviction Council fires out-earn LOW at 15 min? (sets SentinelBridge's MinConviction floor)"));
                _excDetail.Children.Add(DrawConvictionReferee(g));
            }

            // Apply the ◆ responsible config to a per-instrument GTrader21 config file
            var applyRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            var gForBtn = g;
            applyRow.Children.Add(MakeButton("Apply ◆ to GTrader21 config", (bs, be) => ApplyBestRespToGTrader(gForBtn)));
            applyRow.Children.Add(new TextBlock { Text = "writes Sentinel\\GTraderConfigs\\<inst>_<signal>_<dir>.conf (a recommendation you set on the chart)",
                Foreground = Muted, FontFamily = new FontFamily("Consolas"), FontSize = 9, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
            _excDetail.Children.Add(applyRow);
        }

        // Host a fixed-drawing chart so it REDRAWS at the column's live width (fills the right column, tracks
        // window resizing). Redraws only on a real width change (skips our own height-driven SizeChanged re-fires).
        private FrameworkElement ResponsiveHost(Func<double, FrameworkElement> draw, double maxW)
        {
            var host = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 2) };
            double last = -1;
            host.SizeChanged += (s, e) =>
            {
                if (e.NewSize.Width < 50) return;
                double w = Math.Min(maxW, e.NewSize.Width);
                if (Math.Abs(w - last) < 1.0) return;
                last = w;
                host.Children.Clear();
                try { host.Children.Add(draw(w)); } catch (Exception _sx) { SentinelCore.Swallow("SentinelDashboard.ResponsiveHost", _sx); }
            };
            return host;
        }

        private SentinelExcursions_v1_0.TpStop BestResponsible(SentinelExcursions_v1_0.Group g)
        {
            SentinelExcursions_v1_0.TpStop best = null;
            foreach (var t in SentinelExcursions_v1_0.TpStopGrid(g.Pts))
                if (t.Stop <= t.Tp && (best == null || t.Exp > best.Exp)) best = t;
            return best;
        }

        private void ApplyBestRespToGTrader(SentinelExcursions_v1_0.Group g)
        {
            string file = WriteConfigFor(g);
            if (_excStatus != null) _excStatus.Text = file != null
                ? "✔ wrote " + file
                : "no responsible (stop ≤ TP) config for " + (g != null ? g.Key : "?") + " — need n≥15";
        }

        // Writes Sentinel\GTraderConfigs\<inst>_<signal>_<dir>.conf for a group's ◆ best-responsible
        // config; returns the written path, or null if no responsible config exists / the write fails.
        private string WriteConfigFor(SentinelExcursions_v1_0.Group g)
        {
            if (g == null) return null;
            var r = BestResponsible(g);
            if (r == null) return null;
            SentinelExcursions_v1_0.Sub trend;
            int tn = (g.ByRegime != null && g.ByRegime.TryGetValue("trend", out trend) && trend != null) ? trend.N : g.N;
            string dirWord = g.Dir > 0 ? "Long" : "Short";
            double rr = r.Stop > 0 ? r.Tp / r.Stop : 0;
            // Eye-gate recommendation from the referee: only write the key when the verdict is conclusive
            int eyeCode = EyeVerdictCode(g);
            string eyeLine = eyeCode == 1 ? "useEyeGate = true\r\n" : (eyeCode == -1 ? "useEyeGate = false\r\n" : "");
            string txt =
                "# GTrader21 lab config — generated by Sentinel Excursion analysis (◆ best responsible, stop <= TP)\r\n"
              + "# " + g.Key + "  ·  trend-regime n=" + tn + "  ·  R:R " + rr.ToString("0.0")
              + "  ·  est " + (r.Exp >= 0 ? "+" : "") + Math.Round(r.Exp) + "t/trade @ " + Math.Round(r.HitRate * 100) + "% hit"
              + "  ·  " + g.FiresPerDay.ToString("0.0") + " fires/day"
              + (eyeCode != 0 ? "  ·  Eye referee: " + (eyeCode == 1 ? "endorse (gate ON)" : "no help (gate OFF)") : "") + "\r\n"
              + "instrument = " + g.Instrument + "\r\n"
              + "signal = " + g.Signal + "\r\n"
              + "direction = " + dirWord + "\r\n"
              + "useTrendFilter = true\r\n"
              + "trendAdxThreshold = 25\r\n"
              + eyeLine
              + "takeProfitTicks = " + Math.Round(r.Tp) + "\r\n"
              + "stopLossTicks = " + Math.Round(r.Stop) + "\r\n";
            try
            {
                string dir = System.IO.Path.Combine(SentinelCore.SettingsDir, "GTraderConfigs");
                System.IO.Directory.CreateDirectory(dir);
                string file = System.IO.Path.Combine(dir, g.Instrument + "_" + g.Signal + "_" + dirWord + ".conf");
                System.IO.File.WriteAllText(file, txt);
                SentinelCore.Log("Excursion", "wrote ◆ " + g.Key + " → TP" + Math.Round(r.Tp) + " SL" + Math.Round(r.Stop));
                return file;
            }
            catch { return null; }
        }

        // Sync-all: write every CONFIDENT (n≥ExcConfidentN) signal's ◆ config that has +EV, in one click.
        private void SyncAllConfigs()
        {
            var s = _excSummary;
            if (s == null || s.Groups == null) { if (_excStatus != null) _excStatus.Text = "load excursions first"; return; }
            int wrote = 0, skipped = 0;
            foreach (var g in s.Groups)
            {
                if (g == null) continue;
                var r = (g.N >= ExcConfidentN) ? BestResponsible(g) : null;
                if (r != null && r.Exp > 0 && WriteConfigFor(g) != null) wrote++;
                else skipped++;
            }
            if (_excStatus != null) _excStatus.Text = "✔ synced " + wrote + " confident +EV ◆ config(s) → Sentinel\\GTraderConfigs  (skipped " + skipped + " low-n/edgeless)";
            SentinelCore.Log("Excursion", "sync-all: wrote " + wrote + " config(s), skipped " + skipped);
        }

        private TextBlock MonoCaption(string t)
        {
            return new TextBlock { Text = t, Foreground = Muted, FontFamily = new FontFamily("Consolas"),
                FontSize = 10, Margin = new Thickness(0, 9, 0, 3), TextWrapping = TextWrapping.Wrap };
        }

        private static double N0(double v) { return double.IsNaN(v) ? 0 : v; }

        // ① growth line: 3-point median MFE (green) & MAE (red) over 5/15/60-min horizons
        private FrameworkElement DrawMilestoneLine(SentinelExcursions_v1_0.Group g, double W)
        {
            double H = 150;
            var canvas = new Canvas { Width = W, Height = H, Background = Bg };
            double[] xs  = { 0.15 * W, 0.5 * W, 0.85 * W };
            double[] mfe = { N0(g.MfeMed5), N0(g.MfeMed15), N0(g.MfeMed60) };
            double[] mae = { N0(g.MaeMed5), N0(g.MaeMed15), N0(g.MaeMed60) };
            double maxV = 1;
            for (int i = 0; i < 3; i++) maxV = Math.Max(maxV, Math.Max(mfe[i], mae[i]));
            double niceMax = NiceCeil(maxV);
            double top = 14, bot = H - 18;
            Func<double, double> Y = v => bot - (v / niceMax) * (bot - top);
            double xL = xs[0] - 14, xR = xs[2] + 14;

            // faint horizontal grid at 0 / ¼ / ½ / ¾ / max, each with a right-edge tick value
            for (int k = 0; k <= 4; k++)
            {
                double gv = niceMax * k / 4.0, gy = Y(gv);
                canvas.Children.Add(new ShapeLine { X1 = xL, Y1 = gy, X2 = xR, Y2 = gy,
                    Stroke = k == 0 ? Edge : Faint, StrokeThickness = 1 });
                if (k > 0) AddText(canvas, xR + 1, gy - 6, Math.Round(gv).ToString(), Muted, 8);
            }

            // translucent area fill under the MFE curve (baseline → line) so the favorable band reads at a glance
            AddArea(canvas, xs, mfe, Y, bot, Green, 0.12);
            AddPolyline(canvas, xs, mfe, Y, Green);
            AddPolyline(canvas, xs, mae, Y, Red);
            string[] lab = { "5m", "15m", "60m" };
            for (int i = 0; i < 3; i++)
            {
                bool end = i == 2;   // emphasize the 60-min endpoint (the horizon that matters most)
                if (end) { AddHalo(canvas, xs[i], Y(mfe[i]), Green); AddHalo(canvas, xs[i], Y(mae[i]), Red); }
                AddDot(canvas, xs[i], Y(mfe[i]), Green, end ? 7 : 6);
                AddDot(canvas, xs[i], Y(mae[i]), Red,   end ? 7 : 6);
                AddText(canvas, xs[i] - 12, Y(mfe[i]) - 14, "+" + Math.Round(mfe[i]), Green, end ? 10 : 9);
                AddText(canvas, xs[i] - 12, Y(mae[i]) + 3,  "-" + Math.Round(mae[i]), Red, end ? 10 : 9);
                AddText(canvas, xs[i] - 8,  bot + 3, lab[i], Muted, 9);
            }
            return canvas;
        }

        // ② scatter: each fire as a dot at (MAE15, MFE15), colored by regime, with TP/SL overlay
        private FrameworkElement DrawScatter(SentinelExcursions_v1_0.Group g, double W)
        {
            if (g.Pts == null || g.Pts.Count == 0) return MonoLine("— no paired 15-min points for this signal", Muted);
            double H = Math.Max(236, Math.Min(440, W * 0.62)), pad = 28;
            var canvas = new Canvas { Width = W, Height = H, Background = Bg, ClipToBounds = true };

            var allMae = new List<double>(); var allMfe = new List<double>();
            foreach (var p in g.Pts) { allMae.Add(p.Mae15); allMfe.Add(p.Mfe15); }
            double maxX = NiceCeil(Math.Max(1, SentinelExcursions_v1_0.Pctl(allMae, 95)));
            double maxY = NiceCeil(Math.Max(1, SentinelExcursions_v1_0.Pctl(allMfe, 95)));
            double plotW = W - pad - 6, plotH = H - pad - 6;
            Func<double, double> X = v => pad + Math.Min(v, maxX) / maxX * plotW;
            Func<double, double> Y = v => (H - pad) - Math.Min(v, maxY) / maxY * plotH;

            // faint grid with numeric ticks (¼/½/¾ on each axis; max is implied by the plot edge) —
            // gives the cloud a readable scale without the corner labels colliding with axis titles
            for (int k = 1; k <= 3; k++)
            {
                double gx = X(maxX * k / 4.0), gy = Y(maxY * k / 4.0);
                canvas.Children.Add(new ShapeLine { X1 = gx, Y1 = 4, X2 = gx, Y2 = H - pad, Stroke = Faint, StrokeThickness = 1 });
                canvas.Children.Add(new ShapeLine { X1 = pad, Y1 = gy, X2 = W - 4, Y2 = gy, Stroke = Faint, StrokeThickness = 1 });
                AddText(canvas, gx - 6, H - pad + 3, Math.Round(maxX * k / 4.0).ToString(), Muted, 8);
                AddText(canvas, 3, gy - 6, Math.Round(maxY * k / 4.0).ToString(), Muted, 8);
            }
            canvas.Children.Add(new ShapeLine { X1 = pad, Y1 = H - pad, X2 = W - 4, Y2 = H - pad, Stroke = Edge, StrokeThickness = 1 });
            canvas.Children.Add(new ShapeLine { X1 = pad, Y1 = 4, X2 = pad, Y2 = H - pad, Stroke = Edge, StrokeThickness = 1 });
            double dEnd = Math.Min(maxX, maxY);
            canvas.Children.Add(new ShapeLine { X1 = X(0), Y1 = Y(0), X2 = X(dEnd), Y2 = Y(dEnd),
                Stroke = Muted, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 2, 3 } });   // MFE=MAE reference

            // faint WIN-ZONE shading (MFE15 ≥ TP  AND  MAE15 < SL) — dots landing here are wins
            var best = TrendOrOverallBest(g);
            if (best != null && best.Ok)
            {
                var winZone = new Border { Width = Math.Max(0, X(best.Stop) - pad), Height = Math.Max(0, Y(best.Tp) - 4),
                    Background = Green, Opacity = 0.08 };
                Canvas.SetLeft(winZone, pad); Canvas.SetTop(winZone, 4); canvas.Children.Add(winZone);
            }

            AddScatterPts(canvas, RegimePts(g, "chop"),  X, Y, Muted);
            AddScatterPts(canvas, RegimePts(g, "mid"),   X, Y, Amber);
            AddScatterPts(canvas, RegimePts(g, "trend"), X, Y, Green);

            // Eye-endorsement overlay: hollow rings on the fires SentinelEye endorsed (realtime-only →
            // accrues forward; empty until Eye runs). Rings sit ON TOP of the regime dots.
            SentinelExcursions_v1_0.Sub eyeEnd;
            if (g.EyeCount > 0 && g.ByEye != null && g.ByEye.TryGetValue("endorsed", out eyeEnd) && eyeEnd != null)
                foreach (var p in eyeEnd.Pts)
                {
                    var ring = new ShapeEllipse { Width = 9, Height = 9, Stroke = Text, StrokeThickness = 1 };  // hollow (no Fill)
                    Canvas.SetLeft(ring, X(p.Mae15) - 4.5); Canvas.SetTop(ring, Y(p.Mfe15) - 4.5); canvas.Children.Add(ring);
                }

            if (best != null && best.Ok)
            {
                double ty = Y(best.Tp);
                canvas.Children.Add(new ShapeLine { X1 = pad, Y1 = ty, X2 = W - 4, Y2 = ty, Stroke = Green, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 3 } });
                double sx = X(best.Stop);
                canvas.Children.Add(new ShapeLine { X1 = sx, Y1 = 4, X2 = sx, Y2 = H - pad, Stroke = Red, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 3 } });
                AddText(canvas, Math.Min(sx + 2, W - 42), 3, "SL" + Math.Round(best.Stop), Red, 9);
                AddText(canvas, W - 60, Math.Max(ty - 12, 2), "TP" + Math.Round(best.Tp), Green, 9);
            }
            AddText(canvas, W - 74, H - 15, "MAE15 →", Muted, 9);
            AddText(canvas, 3, 2, "MFE15 ↑", Muted, 9);

            var box = new StackPanel();
            box.Children.Add(canvas);
            var legend = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(pad, 3, 0, 0) };
            legend.Children.Add(LegendDot("trend", Green)); legend.Children.Add(LegendDot("mid", Amber)); legend.Children.Add(LegendDot("chop", Muted));
            if (g.EyeCount > 0) legend.Children.Add(LegendRing("Eye-endorsed"));
            box.Children.Add(legend);
            return box;
        }

        // ③ expectancy grid: est. ticks/trade for each of the 12 TP/stop configs, diverging from zero
        private FrameworkElement DrawExpectancyBars(SentinelExcursions_v1_0.Group g, double W)
        {
            var wrap = new StackPanel();
            var grid = SentinelExcursions_v1_0.TpStopGrid(g.Pts);
            if (grid.Count == 0) { wrap.Children.Add(MonoLine("— n<15, not enough fires to grid TP/stop", Muted)); return wrap; }
            grid.Sort((a, b) => b.Exp.CompareTo(a.Exp));
            // best RESPONSIBLE config = highest EV among those with stop ≤ TP (R:R ≥ 1) — what the
            // base-hit doctrine actually wants; the raw ★ best is often a wide-stop mirage.
            SentinelExcursions_v1_0.TpStop bestResp = null;
            foreach (var t in grid) if (t.Stop <= t.Tp && (bestResp == null || t.Exp > bestResp.Exp)) bestResp = t;
            double maxAbs = 1; foreach (var t in grid) maxAbs = Math.Max(maxAbs, Math.Abs(t.Exp));
            double plotW = Math.Max(220, W - 216);   // label column is 210 + a small gap
            double half  = plotW / 2.0;
            double niceMax = NiceCeil(maxAbs);
            double barScale = half / niceMax;

            // scale ruler: −max … 0 … +max ticks/trade, quartile gridline ticks
            var ruler = new Grid { Margin = new Thickness(0, 1, 0, 2) };
            ruler.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
            ruler.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(plotW) });
            ruler.Children.Add(new TextBlock { Text = "est ticks/trade →", Foreground = Muted, FontFamily = new FontFamily("Consolas"), FontSize = 9, VerticalAlignment = VerticalAlignment.Bottom });
            var rc = new Canvas { Width = plotW, Height = 14 };
            for (int k = -2; k <= 2; k++)
            {
                double gx = half + k * (half / 2.0);
                rc.Children.Add(new ShapeLine { X1 = gx, Y1 = 2, X2 = gx, Y2 = 11, Stroke = k == 0 ? Edge : Faint, StrokeThickness = 1 });
                var tv = new TextBlock { Text = (k < 0 ? "-" : k > 0 ? "+" : "") + Math.Round(Math.Abs(k) * (niceMax / 2.0)),
                    Foreground = Muted, FontFamily = new FontFamily("Consolas"), FontSize = 8 };
                Canvas.SetLeft(tv, gx - (k == 0 ? 3 : 8)); Canvas.SetTop(tv, 0); rc.Children.Add(tv);
            }
            Grid.SetColumn(rc, 1); ruler.Children.Add(rc);
            wrap.Children.Add(ruler);

            for (int i = 0; i < grid.Count; i++)
            {
                var t = grid[i];
                bool responsible = t.Stop <= t.Tp;
                bool isBestResp  = ReferenceEquals(t, bestResp);
                string mark = isBestResp ? "◆ " : (i == 0 ? "★ " : "  ");
                double rr = t.Stop > 0 ? t.Tp / t.Stop : 0;
                var row = new Grid { Margin = new Thickness(0, 1, 0, 1), Opacity = responsible ? 1.0 : 0.5 };  // dim wide-stop mirage
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(plotW) });
                var lbl = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
                Brush markBrush = isBestResp ? Green : (i == 0 ? Amber : Muted);   // ★ orange, ◆ green
                lbl.Inlines.Add(new System.Windows.Documents.Run(mark) { Foreground = markBrush });
                lbl.Inlines.Add(new System.Windows.Documents.Run("TP" + Math.Round(t.Tp) + " SL" + Math.Round(t.Stop)
                        + "  " + Math.Round(t.HitRate * 100) + "%  R:R " + rr.ToString("0.0"))
                    { Foreground = isBestResp ? Green : (responsible ? Text : Muted) });
                Grid.SetColumn(lbl, 0); row.Children.Add(lbl);

                var barGrid = new Grid { Height = 15, Background = Bg };
                var guides = new Canvas { Width = plotW, Height = 15 };
                for (int k = -2; k <= 2; k++)
                {
                    double gx = half + k * (half / 2.0);
                    guides.Children.Add(new ShapeLine { X1 = gx, Y1 = 1, X2 = gx, Y2 = 14, Stroke = k == 0 ? Edge : Faint, StrokeThickness = 1 });
                }
                barGrid.Children.Add(guides);
                double px = Math.Min(half, Math.Abs(t.Exp) * barScale);
                barGrid.Children.Add(new Border { Background = t.Exp >= 0 ? Green : Red, Width = px, Height = 10,
                    HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(t.Exp >= 0 ? px : -px, 0, 0, 0), CornerRadius = new CornerRadius(2) });
                barGrid.Children.Add(new TextBlock { Text = (t.Exp >= 0 ? "+" : "") + Math.Round(t.Exp) + "t",
                    Foreground = Muted, FontFamily = new FontFamily("Consolas"), FontSize = 9,
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
                Grid.SetColumn(barGrid, 1); row.Children.Add(barGrid);
                wrap.Children.Add(row);
            }
            return wrap;
        }

        // ④ Eye referee: endorsed vs not-endorsed medians/expectancy + a plain-English verdict
        private FrameworkElement DrawEyeReferee(SentinelExcursions_v1_0.Group g)
        {
            var wrap = new StackPanel();
            if (g.EyeCount == 0 || g.ByEye == null)
            {
                wrap.Children.Add(MonoLine("— no Eye verdicts recorded yet (Eye is realtime-only; this fills in as SentinelEye runs)", Muted));
                return wrap;
            }
            SentinelExcursions_v1_0.Sub end, nott;
            g.ByEye.TryGetValue("endorsed", out end);
            g.ByEye.TryGetValue("not", out nott);
            wrap.Children.Add(EyeRow("endorsed", end));
            wrap.Children.Add(EyeRow("not-endorsed", nott));

            int code = EyeVerdictCode(g);
            string verdict; Brush vb;
            if (end == null || end.N < 10) { verdict = "verdict: not enough endorsed fires yet (need n≥10)"; vb = Muted; }
            else if (nott == null || nott.N == 0) { verdict = "verdict: no not-endorsed fires to compare against"; vb = Muted; }
            else
            {
                double endEdge = end.MfeMed15 - end.MaeMed15, notEdge = nott.MfeMed15 - nott.MaeMed15, delta = endEdge - notEdge;
                if (code == 1)       { verdict = "verdict: Eye ADDS edge — endorsed net-median " + Sgn(endEdge) + "t vs " + Sgn(notEdge) + "t  (+" + Math.Round(delta) + "t)"; vb = Green; }
                else if (code == -1) { verdict = "verdict: Eye HURTS — endorsed " + Sgn(endEdge) + "t vs not " + Sgn(notEdge) + "t  (" + Math.Round(delta) + "t)"; vb = Red; }
                else                 { verdict = "verdict: no clear Eye effect — endorsed " + Sgn(endEdge) + "t vs not " + Sgn(notEdge) + "t"; vb = Amber; }
            }
            var tv = MonoLine(verdict, vb); tv.Margin = new Thickness(0, 3, 0, 0);
            wrap.Children.Add(tv);
            if (code == 1)  wrap.Children.Add(MonoLine("   → recommend: Eye-gate ON for this signal ('Apply ◆' writes useEyeGate=true; GTrader21 applies it)", Green));
            if (code == -1) wrap.Children.Add(MonoLine("   → recommend: keep Eye-gate OFF here (endorsement doesn't help)", Amber));
            return wrap;
        }

        // ⑤ Conviction referee: HIGH vs MID vs LOW conviction buckets + a plain-English "does conviction pay?" verdict
        private FrameworkElement DrawConvictionReferee(SentinelExcursions_v1_0.Group g)
        {
            var wrap = new StackPanel();
            if (g.CouncilCount == 0 || g.ByConviction == null)
            {
                wrap.Children.Add(MonoLine("— no Council fires recorded yet (run the Council + recorder on this chart)", Muted));
                return wrap;
            }
            SentinelExcursions_v1_0.Sub lo, mid, hi;
            g.ByConviction.TryGetValue("HIGH", out hi);
            g.ByConviction.TryGetValue("MID",  out mid);
            g.ByConviction.TryGetValue("LOW",  out lo);
            wrap.Children.Add(EyeRow("HIGH ≥.70", hi));   // reuse the generic label+Sub row
            wrap.Children.Add(EyeRow("MID .50-.70", mid));
            wrap.Children.Add(EyeRow("LOW <.50", lo));

            int code = g.ConvictionVerdictCode;
            string verdict; Brush vb;
            if (hi == null || hi.N < 10)      { verdict = "verdict: not enough HIGH-conviction fires yet (need n≥10)"; vb = Muted; }
            else if (lo == null || lo.N < 5)  { verdict = "verdict: not enough LOW-conviction fires to compare against"; vb = Muted; }
            else
            {
                double hiEdge = hi.MfeMed15 - hi.MaeMed15, loEdge = lo.MfeMed15 - lo.MaeMed15, delta = hiEdge - loEdge;
                if (code == 1)       { verdict = "verdict: conviction PAYS — HIGH net-median " + Sgn(hiEdge) + "t vs LOW " + Sgn(loEdge) + "t  (+" + Math.Round(delta) + "t)"; vb = Green; }
                else if (code == -1) { verdict = "verdict: conviction INVERTS — HIGH " + Sgn(hiEdge) + "t vs LOW " + Sgn(loEdge) + "t  (" + Math.Round(delta) + "t) — check the weights"; vb = Red; }
                else                 { verdict = "verdict: no clear conviction gradient — HIGH " + Sgn(hiEdge) + "t vs LOW " + Sgn(loEdge) + "t"; vb = Amber; }
            }
            var tv = MonoLine(verdict, vb); tv.Margin = new Thickness(0, 3, 0, 0);
            wrap.Children.Add(tv);
            if (code == 1)  wrap.Children.Add(MonoLine("   → recommend: gate SentinelBridge at MinConviction ≥ 0.70 (trade the HIGH bucket)", Green));
            if (code == -1) wrap.Children.Add(MonoLine("   → recommend: don't size on conviction here — investigate the Council weights (Lens)", Amber));
            return wrap;
        }

        // shared verdict now lives on the Group (used by the headless State writer too)
        private int EyeVerdictCode(SentinelExcursions_v1_0.Group g) { return g == null ? 0 : g.EyeVerdictCode; }
        private TextBlock EyeRow(string label, SentinelExcursions_v1_0.Sub s)
        {
            if (s == null || s.N == 0) return MonoLine("  " + label.PadRight(12) + " n0", Muted);
            var best = s.Best;
            string bs = (best != null && best.Ok)
                ? "   best TP" + Math.Round(best.Tp) + " SL" + Math.Round(best.Stop) + " " + Math.Round(best.HitRate * 100) + "% " + (best.Exp >= 0 ? "+" : "") + Math.Round(best.Exp) + "t"
                : "";
            return MonoLine("  " + label.PadRight(12) + " n" + s.N + "   MFE15 " + Math.Round(s.MfeMed15)
                + "  MAE15 " + Math.Round(s.MaeMed15) + (s.HasEdge ? "  ✓" : "") + bs, s.HasEdge ? Green : Muted);
        }
        private static string Sgn(double v) { return (v >= 0 ? "+" : "") + Math.Round(v); }

        // ── small drawing helpers ─────────────────────────────────────────────────────
        private void AddPolyline(Canvas c, double[] xs, double[] vals, Func<double, double> Y, Brush stroke)
        {
            var pl = new ShapePolyline { Stroke = stroke, StrokeThickness = 2 };
            var pts = new PointCollection();
            for (int i = 0; i < xs.Length; i++) pts.Add(new Point(xs[i], Y(vals[i])));
            pl.Points = pts;
            c.Children.Add(pl);
        }
        private void AddDot(Canvas c, double x, double y, Brush b, double d = 6)
        {
            var e = new ShapeEllipse { Width = d, Height = d, Fill = b };
            Canvas.SetLeft(e, x - d / 2); Canvas.SetTop(e, y - d / 2); c.Children.Add(e);
        }
        // soft halo behind an emphasized endpoint dot (translucent glow, drawn first)
        private void AddHalo(Canvas c, double x, double y, Brush b)
        {
            Color col = (b as SolidColorBrush) != null ? ((SolidColorBrush)b).Color : Color.FromRgb(0x6C, 0x7A, 0x92);
            var e = new ShapeEllipse { Width = 15, Height = 15,
                Fill = new SolidColorBrush(Color.FromArgb(60, col.R, col.G, col.B)) };
            Canvas.SetLeft(e, x - 7.5); Canvas.SetTop(e, y - 7.5); c.Children.Add(e);
        }
        // translucent area fill from a baseline up to a 3-point curve
        private void AddArea(Canvas c, double[] xs, double[] vals, Func<double, double> Y, double baseY, Brush fill, double opacity)
        {
            Color col = (fill as SolidColorBrush) != null ? ((SolidColorBrush)fill).Color : Color.FromRgb(0x25, 0xD0, 0x8B);
            var poly = new ShapePolygon { Fill = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), col.R, col.G, col.B)) };
            var pts = new PointCollection { new Point(xs[0], baseY) };
            for (int i = 0; i < xs.Length; i++) pts.Add(new Point(xs[i], Y(vals[i])));
            pts.Add(new Point(xs[xs.Length - 1], baseY));
            poly.Points = pts;
            c.Children.Add(poly);
        }
        // round a max up to a clean axis ceiling (1/2/5 × 10ⁿ) so gridline ticks read nicely
        private static double NiceCeil(double v)
        {
            if (v <= 0) return 1;
            double mag = Math.Pow(10, Math.Floor(Math.Log10(v)));
            double n = v / mag;
            double step = n <= 1 ? 1 : n <= 2 ? 2 : n <= 5 ? 5 : 10;
            return step * mag;
        }
        private void AddText(Canvas c, double x, double y, string t, Brush b, double size)
        {
            var tb = new TextBlock { Text = t, Foreground = b, FontFamily = new FontFamily("Consolas"), FontSize = size };
            Canvas.SetLeft(tb, x); Canvas.SetTop(tb, y); c.Children.Add(tb);
        }
        private void AddScatterPts(Canvas c, List<SentinelExcursions_v1_0.Pt> pts, Func<double, double> X, Func<double, double> Y, Brush b)
        {
            if (pts == null || pts.Count == 0) return;
            int step = pts.Count > 350 ? pts.Count / 350 : 1;   // stride so a 1000-point cloud stays snappy
            for (int i = 0; i < pts.Count; i += step)
            {
                var p = pts[i];
                var e = new ShapeEllipse { Width = 4, Height = 4, Fill = b, Opacity = 0.75 };
                Canvas.SetLeft(e, X(p.Mae15) - 2); Canvas.SetTop(e, Y(p.Mfe15) - 2); c.Children.Add(e);
            }
        }
        private List<SentinelExcursions_v1_0.Pt> RegimePts(SentinelExcursions_v1_0.Group g, string r)
        {
            SentinelExcursions_v1_0.Sub sub;
            if (g.ByRegime != null && g.ByRegime.TryGetValue(r, out sub) && sub != null) return sub.Pts;
            return new List<SentinelExcursions_v1_0.Pt>();
        }
        private SentinelExcursions_v1_0.TpStop TrendOrOverallBest(SentinelExcursions_v1_0.Group g)
        {
            SentinelExcursions_v1_0.Sub trend;
            if (g.ByRegime != null && g.ByRegime.TryGetValue("trend", out trend) && trend != null)
            { var b = trend.Best; if (b != null && b.Ok) return b; }
            return g.Best;
        }
        private FrameworkElement LegendDot(string label, Brush b)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 0) };
            sp.Children.Add(new ShapeEllipse { Width = 7, Height = 7, Fill = b, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 3, 0) });
            sp.Children.Add(new TextBlock { Text = label, Foreground = Muted, FontFamily = new FontFamily("Consolas"), FontSize = 9, VerticalAlignment = VerticalAlignment.Center });
            return sp;
        }
        private FrameworkElement LegendRing(string label)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 0) };
            sp.Children.Add(new ShapeEllipse { Width = 8, Height = 8, Stroke = Text, StrokeThickness = 1, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 3, 0) });
            sp.Children.Add(new TextBlock { Text = label, Foreground = Muted, FontFamily = new FontFamily("Consolas"), FontSize = 9, VerticalAlignment = VerticalAlignment.Center });
            return sp;
        }

        // "  ↳ TP/stop  overall: TP18t SL15t → 54% +6t   ·   trend: TP22t SL14t → 61% +9t"
        private TextBlock TpStopLine(SentinelExcursions_v1_0.Group g)
        {
            var o = g.Best;
            string s = "   ↳ " + "TP/stop".PadRight(7);
            s += (o != null && o.Ok) ? "overall TP" + T(o.Tp) + " SL" + T(o.Stop) + " → " + Pct(o.HitRate) + " " + Exp(o.Exp)
                                     : "overall (n<15)";
            SentinelExcursions_v1_0.Sub trend;
            if (g.ByRegime.TryGetValue("trend", out trend))
            {
                var bt = trend.Best;
                if (bt != null && bt.Ok)
                    s += "   ·   trend TP" + T(bt.Tp) + " SL" + T(bt.Stop) + " → " + Pct(bt.HitRate) + " " + Exp(bt.Exp);
            }
            var tb = MonoLine(s, (o != null && o.Ok && o.Exp > 0) ? Green : Muted);
            tb.FontSize = 11;
            return tb;
        }
        private static string Pct(double r) { return Math.Round(r * 100).ToString("0") + "%"; }
        private static string Exp(double e) { return (e >= 0 ? "+" : "") + Math.Round(e).ToString("0") + "t"; }

        // compact partition line: "  ↳ regime  trend n120 45t:30t ✓   chop n90 30t:40t"
        private TextBlock SubLine(System.Collections.Generic.Dictionary<string, SentinelExcursions_v1_0.Sub> subs,
                                  string[] order, string label, int minN)
        {
            var parts = new System.Collections.Generic.List<string>();
            bool anyEdge = false;
            foreach (var name in order)
            {
                SentinelExcursions_v1_0.Sub s;
                if (subs.TryGetValue(name, out s) && s.N >= minN)
                {
                    parts.Add(name + " n" + s.N + " " + T(s.MfeMed15) + ":" + T(s.MaeMed15) + (s.HasEdge ? " ✓" : ""));
                    if (s.HasEdge) anyEdge = true;
                }
            }
            if (parts.Count == 0) return null;
            var tb = MonoLine("   ↳ " + label.PadRight(7) + string.Join("   ", parts), anyEdge ? Green : Muted);
            tb.FontSize = 11;
            return tb;
        }

        private TextBlock ExcRow(SentinelExcursions_v1_0.Group g)
        {
            string txt = g.Key.PadRight(11) + " n" + g.N + " (" + g.FiresPerDay.ToString("0.0") + "/d)"
                + "   MFE 5/15/60m: " + T(g.MfeMed5) + "/" + T(g.MfeMed15) + "/" + T(g.MfeMed60)
                + "   MAE15 med/p75: " + T(g.MaeMed15) + "/" + T(g.Mae15P75)
                + "   tail(maxMAE p90): " + T(g.MaxMaeP90);
            return MonoLine(txt, g.HasEdge ? Green : Muted);
        }

        private static string T(double v) { return (double.IsNaN(v) ? "–" : Math.Round(v).ToString("0")) + "t"; }

        // ══ ACCOUNTS TAB — per-account profiles (firm preset or custom) → Sentinel\Profiles.conf ══════
        private FrameworkElement BuildAccountsTab()
        {
            var panel = new StackPanel { Margin = new Thickness(12) };

            // ── the editor form, wrapped in a glass card to match the tiles below ──
            var form = new StackPanel();
            form.Children.Add(Label("Account profile — pick a firm preset or go custom; feeds the Governor + sizing", true));

            var row1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 6) };
            row1.Children.Add(ApLabel("Account"));
            _apAccount = new ComboBox { MinWidth = 180, IsEditable = true, IsTextSearchEnabled = true, StaysOpenOnEdit = true, Margin = new Thickness(0, 0, 12, 0) };
            foreach (string n in AccountNames()) _apAccount.Items.Add(n);
            _apAccount.SelectionChanged += (s, e) => LoadProfileIntoFields();
            row1.Children.Add(_apAccount);
            row1.Children.Add(ApLabel("Firm"));
            _apFirm = new ComboBox { MinWidth = 100 };
            foreach (string f in new[] { "custom", "lucid", "bulenox", "tpt", "apex" }) _apFirm.Items.Add(f);
            _apFirm.SelectedIndex = 0;
            _apFirm.SelectionChanged += (s, e) => OnApFirmChanged();
            row1.Children.Add(_apFirm);
            form.Children.Add(row1);

            _apRatio = ApTb("0.30"); _apTarget = ApTb("9000"); _apDailyLoss = ApTb("1500");
            _apSize = ApTb("1.0"); _apContracts = ApTb("0"); _apSession = ApTb("24h");
            form.Children.Add(ApRow("Consistency ratio R", _apRatio, "Eval target $", _apTarget));
            form.Children.Add(ApRow("Daily loss stop $", _apDailyLoss, "Size mult", _apSize));
            form.Children.Add(ApRow("Max contracts (0=none)", _apContracts, "Session (HHmm-HHmm / 24h)", _apSession));

            // ── v1.1.9: the rest of the governor (was conf-file-only) ──
            _apManualDaily = ApTb("0"); _apResetHour = ApTb("0"); _apDdAmt = ApTb("0"); _apDdFlat = ApTb("0");
            form.Children.Add(ApRowE("Manual daily cap $ (0=R×target)", _apManualDaily, "Daily reset hour 0–23 (global)", _apResetHour));
            _apDdType = new ComboBox { MinWidth = 72, Margin = new Thickness(0, 0, 4, 0) };
            foreach (string t in new[] { "trailing", "static", "eod" }) _apDdType.Items.Add(t);
            _apDdType.SelectedIndex = 0;
            form.Children.Add(ApRowE("Trailing DD $ (0=off)", _apDdAmt, "DD type", _apDdType));
            _apHardEnforce = new CheckBox { Content = "Auto-flatten at loss/DD stop", Foreground = Text, VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Consolas"), FontSize = 11 };
            form.Children.Add(ApRowE("DD flatten buffer $ (above floor)", _apDdFlat, null, _apHardEnforce));

            var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 2) };
            btns.Children.Add(MakeButton("Save profile", (s, e) => SaveProfile()));
            btns.Children.Add(MakeButton("Refresh accounts", (s, e) => RefreshApAccounts()));
            form.Children.Add(btns);
            panel.Children.Add(MakeCard(form));

            _apStatus = new TextBlock { Foreground = Muted, FontFamily = new FontFamily("Consolas"), FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 8, 0, 6) };
            panel.Children.Add(_apStatus);

            _apTiles = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 14) };
            panel.Children.Add(_apTiles);
            panel.Children.Add(Label("Accounts & governor (live — Sentinel\\Profiles.conf)", true));
            _apList = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            panel.Children.Add(_apList);

            panel.Children.Add(new TextBlock
            {
                Text = "Firm preset fills ratio + loss defaults you can override. Cap/day = min(manual daily, ratio × target) "
                     + "— you stay inside the firm's consistency rule by construction; the loss-stop halts the day at −$loss. "
                     + "Reset hour is when the day rolls (17 = 5pm for most prop firms; it's global — the last saved account wins). "
                     + "Trailing DD $ arms the equity-floor tracker; Auto-flatten closes the account once at the loss/DD stop. "
                     + "Everything here writes Sentinel\\Profiles.conf for you (no manual editing) — Risk reloads within ~2s of Save.",
                Foreground = Muted, FontFamily = new FontFamily("Consolas"), FontSize = 9, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0)
            });

            return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel, Background = Bg };
        }

        private TextBlock ApLabel(string t) { return new TextBlock { Text = t + ": ", Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Consolas"), FontSize = 11, Margin = new Thickness(0, 0, 4, 0) }; }
        private TextBox ApTb(string t) { return new TextBox { Text = t, Width = 72, Margin = new Thickness(0, 0, 4, 0) }; }
        private FrameworkElement ApRow(string l1, TextBox t1, string l2, TextBox t2)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            var a = ApLabel(l1); a.MinWidth = 150; sp.Children.Add(a); sp.Children.Add(t1);
            var b = ApLabel(l2); b.MinWidth = 150; b.Margin = new Thickness(16, 0, 4, 0); sp.Children.Add(b); sp.Children.Add(t2);
            return sp;
        }
        // like ApRow but accepts any control (ComboBox / CheckBox); a null second label/control leaves that slot empty
        private FrameworkElement ApRowE(string l1, FrameworkElement c1, string l2, FrameworkElement c2)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            var a = ApLabel(l1); a.MinWidth = 150; sp.Children.Add(a); if (c1 != null) sp.Children.Add(c1);
            if (l2 != null) { var b = ApLabel(l2); b.MinWidth = 150; b.Margin = new Thickness(16, 0, 4, 0); sp.Children.Add(b); }
            if (c2 != null) { c2.Margin = new Thickness(l2 == null ? 16 : 0, 0, 4, 0); sp.Children.Add(c2); }
            return sp;
        }

        private void OnApFirmChanged()
        {
            string firm = _apFirm != null ? _apFirm.SelectedItem as string : null;
            if (firm == null || firm == "custom") return;
            double ratio, loss;
            switch (firm)
            {
                case "lucid":   ratio = 0.20; loss = 1000; break;
                case "bulenox": ratio = 0.40; loss = 1000; break;
                case "tpt":     ratio = 0.50; loss = 1500; break;
                case "apex":    ratio = 0.30; loss = 1500; break;
                default: return;
            }
            if (_apRatio != null) _apRatio.Text = ratio.ToString("0.00", CultureInfo.InvariantCulture);
            if (_apDailyLoss != null) _apDailyLoss.Text = loss.ToString("0", CultureInfo.InvariantCulture);
        }

        private void LoadProfileIntoFields()
        {
            string acct = ComboText(_apAccount);
            if (string.IsNullOrEmpty(acct)) return;
            var p = SentinelCore.GetAccountProfile(acct);
            if (p == null) return;
            if (_apFirm != null) _apFirm.SelectedItem = p.Firm ?? "custom";
            if (_apRatio != null) _apRatio.Text = p.Ratio.ToString("0.00", CultureInfo.InvariantCulture);
            if (_apTarget != null) _apTarget.Text = p.ProfitTarget.ToString("0", CultureInfo.InvariantCulture);
            if (_apDailyLoss != null) _apDailyLoss.Text = p.DailyLossStop.ToString("0", CultureInfo.InvariantCulture);
            if (_apSize != null) _apSize.Text = p.SizeScale.ToString("0.##", CultureInfo.InvariantCulture);
            if (_apContracts != null) _apContracts.Text = p.ContractLimit.ToString(CultureInfo.InvariantCulture);
            if (_apSession != null) _apSession.Text = string.IsNullOrEmpty(p.Session) ? "24h" : p.Session;
            // v1.1.9 — the rest of the governor
            if (_apManualDaily != null) _apManualDaily.Text = p.ManualDailyTarget.ToString("0", CultureInfo.InvariantCulture);
            if (_apDdAmt != null) _apDdAmt.Text = p.DdAmount.ToString("0", CultureInfo.InvariantCulture);
            if (_apDdType != null) _apDdType.SelectedItem = string.IsNullOrEmpty(p.DdType) ? "trailing" : p.DdType;
            if (_apHardEnforce != null) _apHardEnforce.IsChecked = p.HardEnforce;
            if (_apResetHour != null) _apResetHour.Text = SentinelCore.GovernorResetHour.ToString(CultureInfo.InvariantCulture);   // global (not on AccountProfile)
            if (_apDdFlat != null) _apDdFlat.Text = RawProfileField(acct, "ddFlat", "0");   // not on AccountProfile → read raw
        }

        // Read one account's raw Profiles.conf line as a key→value map (for fields not carried on AccountProfile,
        // e.g. ddFlat, AND to MERGE-preserve any field the editor doesn't manage on Save). Empty map if absent.
        private static Dictionary<string, string> RawProfileKv(string acct)
        {
            var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string path = System.IO.Path.Combine(SentinelCore.SettingsDir, "Profiles.conf");
                if (!System.IO.File.Exists(path)) return kv;
                foreach (string raw in System.IO.File.ReadAllLines(path))
                {
                    string line = (raw ?? "").Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//")) continue;
                    if (!string.Equals(LineAccount(line), acct, StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (string part in line.Split('|'))
                    {
                        int e = part.IndexOf('=');
                        if (e > 0) kv[part.Substring(0, e).Trim()] = part.Substring(e + 1).Trim();
                    }
                    break;
                }
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelDashboard.LoadProfileIntoFields", _sx); }
            return kv;
        }
        private static string RawProfileField(string acct, string key, string dflt)
        {
            string v; return RawProfileKv(acct).TryGetValue(key, out v) && !string.IsNullOrEmpty(v) ? v : dflt;
        }

        private void RefreshApAccounts()
        {
            if (_apAccount == null) return;
            string keep = ComboText(_apAccount);
            _apAccount.Items.Clear();
            foreach (string n in AccountNames()) _apAccount.Items.Add(n);
            if (keep != null) _apAccount.SelectedItem = keep;
        }

        private void SaveProfile()
        {
            string acct = ComboText(_apAccount);
            if (string.IsNullOrEmpty(acct)) { if (_apStatus != null) _apStatus.Text = "pick an account first"; return; }
            string firm = (_apFirm != null ? _apFirm.SelectedItem as string : null) ?? "custom";
            // MERGE into the existing raw line so any field the editor doesn't manage is preserved (not wiped).
            var kv = RawProfileKv(acct);
            kv["account"] = acct; kv["firm"] = firm;
            kv["ratio"] = F(_apRatio); kv["target"] = F(_apTarget); kv["dailyLoss"] = F(_apDailyLoss);
            kv["size"] = F(_apSize); kv["contracts"] = F(_apContracts); kv["session"] = F(_apSession);
            kv["manualDaily"] = F(_apManualDaily); kv["ddAmt"] = F(_apDdAmt); kv["ddFlat"] = F(_apDdFlat);
            kv["ddType"] = (_apDdType != null ? _apDdType.SelectedItem as string : null) ?? "trailing";
            kv["hardEnforce"] = (_apHardEnforce != null && _apHardEnforce.IsChecked == true) ? "true" : "false";
            kv["resetHour"] = F(_apResetHour);
            string line = SerializeProfileKv(kv);
            try
            {
                string path = System.IO.Path.Combine(SentinelCore.SettingsDir, "Profiles.conf");
                var lines = System.IO.File.Exists(path)
                    ? new List<string>(System.IO.File.ReadAllLines(path))
                    : new List<string> { "# Sentinel per-account profiles — edit here or via the dashboard Accounts tab." };
                lines.RemoveAll(l => string.Equals(LineAccount(l), acct, StringComparison.OrdinalIgnoreCase));
                lines.Add(line);
                System.IO.File.WriteAllText(path, string.Join("\r\n", lines) + "\r\n");
                if (_apStatus != null) _apStatus.Text = "✔ saved " + acct + " (" + firm + ") → Profiles.conf — Risk reloads within ~2s";
                SentinelCore.Log("Accounts", "saved profile " + acct + " firm=" + firm);
            }
            catch (Exception ex) { if (_apStatus != null) _apStatus.Text = "save failed: " + ex.Message; }
        }

        private static string F(TextBox t) { return t == null ? "" : (t.Text ?? "").Trim(); }
        // serialize a profile kv map to a Profiles.conf line: known keys in a stable order, then any extras preserved
        private static string SerializeProfileKv(Dictionary<string, string> kv)
        {
            var order = new[] { "account", "firm", "ratio", "target", "dailyLoss", "manualDaily",
                                "size", "contracts", "ddType", "ddAmt", "ddFlat", "hardEnforce", "resetHour", "session" };
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var parts = new List<string>();
            foreach (string k in order) { string v; if (kv.TryGetValue(k, out v)) { parts.Add(k + "=" + v); seen.Add(k); } }
            foreach (var p in kv) if (!seen.Contains(p.Key)) parts.Add(p.Key + "=" + p.Value);   // preserve unmanaged extras
            return string.Join("|", parts);
        }
        private static string LineAccount(string line)
        {
            if (line == null) return null;
            foreach (string part in line.Split('|'))
            {
                int e = part.IndexOf('=');
                if (e > 0 && part.Substring(0, e).Trim().Equals("account", StringComparison.OrdinalIgnoreCase))
                    return part.Substring(e + 1).Trim();
            }
            return null;
        }

        private void RefreshApProfilesLive()
        {
            if (_apList == null) return;
            var ps = SentinelCore.AllAccountProfiles();
            var gmap = new System.Collections.Generic.Dictionary<string, SentinelCore.GovernorState>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in SentinelCore.AllGovernorStates()) if (g != null && g.Account != null) gmap[g.Account] = g;

            // ── hero tiles ──
            if (_apTiles != null)
            {
                _apTiles.Children.Clear();
                double liveP = 0, nearestFrac = 0; int trading = 0, done = 0, halted = 0;
                string nearestTxt = "—"; Brush nearestCol = Muted;
                foreach (var p in ps)
                {
                    if (p == null) continue;
                    SentinelCore.GovernorState g; gmap.TryGetValue(p.Account, out g);
                    double day = g != null ? g.DailyPnl : 0; liveP += day + OpenPnl(p.Account);   // today's realized-since-reset + live open
                    bool allowed = g == null || g.Allowed;
                    if (g == null) { }
                    else if (!allowed) { if (g.Status == "DayComplete") done++; else halted++; }
                    else trading++;
                    double cap = p.Ratio * p.ProfitTarget; if (p.ManualDailyTarget > 0) cap = Math.Min(p.ManualDailyTarget, cap);
                    double capFrac = cap > 0 ? day / cap : 0;
                    double lossFrac = p.DailyLossStop > 0 ? (-day) / p.DailyLossStop : 0;
                    double f = Math.Max(capFrac, lossFrac);
                    if (f > nearestFrac) { nearestFrac = f; nearestTxt = (day >= 0 ? "+$" : "-$") + Math.Abs(Math.Round(day)); nearestCol = lossFrac > capFrac ? Amber : Green; }
                }
                _apTiles.Children.Add(StatTile("Live P&L · today", (liveP >= 0 ? "+$" : "-$") + Math.Abs(Math.Round(liveP)), liveP >= 0 ? Green : Red, ps.Count + " governed"));
                _apTiles.Children.Add(StatTile("Trading", trading + " / " + ps.Count, Text, done + " done · " + halted + " halted"));
                _apTiles.Children.Add(StatTile("Nearest limit", nearestTxt, nearestCol, ps.Count == 0 ? "—" : Math.Round(nearestFrac * 100) + "% to a stop"));
            }

            // ── governor cards ──
            _apList.Children.Clear();
            if (ps.Count == 0)
            {
                _apList.Children.Add(MonoLine("no profiles yet — fill the fields above and Save (writes Sentinel\\Profiles.conf)", Muted));
                return;
            }
            ps.Sort((a, b) => string.Compare(a.Account, b.Account, StringComparison.OrdinalIgnoreCase));

            // ── VISUAL: fleet day-P&L per governed account (green up / red down) — the at-a-glance overview ──
            var glabels = new List<string>(); var gvals = new List<double>();
            foreach (var p in ps) { if (p == null) continue; SentinelCore.GovernorState gg; if (gmap.TryGetValue(p.Account, out gg) && gg != null) { glabels.Add(p.Account); gvals.Add(gg.DailyPnl); } }
            if (glabels.Count > 1)
            {
                _apList.Children.Add(new TextBlock { Text = "day P&L across governed accounts", Foreground = Muted, FontFamily = new FontFamily("Consolas"), FontSize = 9, Margin = new Thickness(0, 0, 0, 3) });
                _apList.Children.Add(SignedBars(glabels, gvals, v => (v >= 0 ? "+$" : "-$") + Math.Abs(v).ToString("0"), true, 150, 200));
                _apList.Children.Add(new Border { Height = 1, Background = Faint, Margin = new Thickness(0, 8, 0, 8) });
            }

            foreach (var p in ps)
            {
                if (p == null) continue;
                SentinelCore.GovernorState g; gmap.TryGetValue(p.Account, out g);
                _apList.Children.Add(GovernorCard(p, g));
            }
        }

        private void OnClosedCleanup(object sender, EventArgs e)
        {
            if (_killHandler != null) SentinelCore.KillSwitchChanged -= _killHandler;
            _killHandler = null;

            if (_logTimer != null) { _logTimer.Stop(); _logTimer = null; }
            if (_journalLiveTimer != null) { _journalLiveTimer.Stop(); _journalLiveTimer = null; }
            var logSvc = SentinelLogService.Instance;
            if (logSvc != null && _logTradeClosed != null) logSvc.TradeClosed -= _logTradeClosed;
            _logTradeClosed = null;
        }
    }
}
