---
layout: sentinel-ref
title: "SentinelDashboard_v1_0_0.cs"
blurb: "AddOns / runtime · 1.0.0 · 3763 lines"
---

# SentinelDashboard_v1_0_0.cs

> `bin/Custom/AddOns/SentinelDashboard_v1_0_0.cs`

| | |
|---|---|
| **Family** | AddOns / runtime |
| **Version** | 1.0.0 |
| **Size** | 3763 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelDashboardAddOn` |
| **Namespace** | `NinjaTrader.NinjaScript.AddOns.Sentinel` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
one at https://mozilla.org/MPL/2.0/.

Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
─────────────────────────────────────────────────────────────────────────────
═════════════════════════════════════════════════════════════════════════════
 SentinelDashboard — unified config/control window for the Sentinel Suite (NT8)
 File: SentinelDashboard_v1_0_0.cs
 Version: v1.1.9   (Accounts tab exposes the FULL governor — manual cap / reset hour / trailing DD / auto-flatten — no conf editing)
─────────────────────────────────────────────────────────────────────────────
 WHAT THIS IS  (see Docs/ROADMAP.md, memory: sentinel-suite-architecture)
   The ONE window for the whole suite. Adds "Sentinel Suite" under Control Center > New.
   A TabControl with one tab per Sentinel tool; each tab ATTACHES to that tool's headless
   service singleton (e.g. SentinelCopierService_v0_1_0.Instance) — it does NOT own the
   service, and closing this window never stops any service. Same attach pattern as
   MAEDashboard → MAECaptureService.

   A shared TOP BAR carries the suite-wide KILL-SWITCH (SentinelCore) and live status,
   visible from every tab.

 VERIFIED SCAFFOLDING (from in-repo BuySellVolumePressureDashboardAddOnV001 + MAEDashboard):
   • Menu: OnWindowCreated(Window) → (window as ControlCenter).FindFirst("ControlCenterMenuItemNew")
     as NTMenuItem; add an NTMenuItem child; open the window from Core.Globals.RandomDispatcher.
   • Window derives from NTWindow (NinjaTrader.Gui.Tools); set Caption/Width/Height/Content.
   • UI built in code (Grid/StackPanel/Border/TextBlock/Button/ComboBox/TextBox/CheckBox).
   • Cross-thread brushes are Freeze()'d; background→UI updates via Dispatcher.InvokeAsync.
   • Unsubscribe every handler in the Closed event.

 CHANGELOG
   v1.1.9 — (in-place) Accounts tab now exposes the FULL governor so nothing needs hand-editing Profiles.conf:
            added Manual daily cap $ (0=R×target), Daily reset hour 0–23 (global — when the day rolls; 17=5pm
            prop-firm), Trailing DD $ + DD type (trailing/static/eod) + DD flatten buffer $, and an Auto-flatten
            (hardEnforce) checkbox. Save now MERGES into the account's existing conf line (preserves any field the
            editor doesn't manage — previously a Save rebuilt the line from scratch and could silently wipe e.g.
            ddFlat). ddFlat/resetHour aren't on AccountProfile so they load from the raw conf line / global state.
   v1.1.8 — (in-place) EXCURSION TAB REDESIGN (was an early, cluttered single column): now a two-column
            master/detail that uses the window width — compact header (controls · plain-language status
            "N records · N signals · N files" w/ path tooltip · tiles · live configs), then LEFT = the ranked
            edge list (clickable rows drive the detail; selected row highlights cyan + hover), RIGHT = the
            selected signal's deep-dive (growth · outcome cloud · TP/SL grid · referees · Apply ◆). Removed the
            redundant "Signal → excursion medians" wall-of-text table (duplicated the chart+detail) and the
            long footer paragraph. The detail charts (growth line · outcome cloud · TP/SL grid) are now
            RESPONSIVE — a ResponsiveHost redraws each at the right column's live width (fills the column,
            tracks window resize; the right column constrains to viewport). No analytics lost — presentation only.
   v1.1.7 — (in-place) new HOME tab (front page; first tab, selected on open). First item: a RED FOLDER NEWS
            readout of the economic-calendar event veto — reads Sentinel\News.conf (written by the native
            SentinelNewsService) directly, re-parsing only on mtime change but recomputing countdowns every
            tick: tiles (protection LOCKED/CLEAR/STALE · next event countdown · windows loaded), the upcoming
            red-folder schedule (local time + countdown + window, the active lockout highlighted), and a footer
            (feeder status + News.conf freshness + source line). "Red folder" = impact:High from the feed — the
            service already filters to HIGH, so News.conf IS the red-folder list. Room to grow more front-page items.
   v1.1.6 — (in-place) Accounts tab clarity: GovernorCard shows live OPEN (unrealized) P&L inline + a sub-line
            with the account's RAW realized ("matches NT" — g.DailyPnl is realized-SINCE-a-baseline for the
            consistency rule, which reads ~0 after mid-day F5s; the raw figure mirrors NT's column so the card
            is never confusingly blank). "Live P&L · today" tile now folds in open P&L. Pairs with
            SentinelRiskService v1.0.8 persisting the governor daily baseline (survives F5/restart).
   v1.1.5 — (in-place) Excursion tab: new ⑤ CONVICTION REFEREE in the per-signal detail — for COUNCIL
            groups only, shows HIGH/MID/LOW conviction buckets (from SentinelExcursions v1.0.5 ByConviction)
            + a "does conviction pay?" verdict (ConvictionVerdictCode) that names the SentinelBridge
            MinConviction floor to set. The COUNCIL group + Apply ◆ (→ <inst>_COUNCIL_<dir>.conf) already
            worked via the generic by-signal code. Pairs with SentinelExcursionRecorder_v1_4 (schema 1.2).
   v1.1.4 — (in-place) MORE VISUALS — reusable WPF chart primitives (dataviz method + Sentinel palette:
            cyan=magnitude, green/red=money+polarity, no rainbow categorical) added near Track(): HBars
            (horizontal magnitude), HDivBars (diverging around a center baseline, Canvas-positioned) and
            Columns (vertical time histogram). Marks per spec: thin bars, rounded DATA end (square at the
            baseline), recessive Faint/Edge gridlines, values/labels in TEXT tokens (never the bar hue).
            Applied: SLIPPAGE = avg-slip-per-instrument diverging bars (red adverse / green improvement);
            JOURNAL = activity histogram (events per hour today / per day for a window); LENS = net-ticks
            diverging bars per strategy + instrument; EYE = signed-score diverging bars (green long / red
            short); ARC = day-P&L per fleet slot; RISK = data-lag per feed (green/amber/red by threshold);
            ACCOUNTS = fleet day-P&L per governed account. Charts sit ABOVE the existing text rows (chart +
            table, per the accessibility rule). SignedBars auto-picks MAGNITUDE bars (full width) for
            one-sided data and DIVERGING only when signs are mixed, so a chart never wastes half its width.
   v1.1.3 — (in-place) new TEST tab — the "prove the safety system" surface: (1) ALERT CHANNEL config
            (Enabled / Play-Info / Push-on-Info / throttle / wav paths / push cmd) with Save-&-apply
            (SentinelAlertService.Apply → writes Alerts.conf + live-applies, no restart), Reload, and
            Test Info/Critical buttons that fire a REAL alert (sound + push + ledger + Risk display);
            (2) DRY-RUN ENTRY PROBE — GateEntry + SizeForRisk + TickValue for an account/instr/qty/stop/
            risk with NO order submitted (engage the kill → gate returns HARD, safely); (3) SAFE SELF-
            CHECKS — scoped-kill isolation (fake roots), sizer unaffordable→0/generous→≥1, TickValue>0,
            green/red; (4) LEDGER AUDIT — today's kill/flatten/alert/restore/fill counts. Also: Journal
            tab "▶ Live" toggle (2s auto-refresh tail; stopped on close). SentinelAlertService→v1.0.1.
   v1.1.2 — (in-place) new SLIPPAGE tab + FILL events in Journal (Substrate 2, execution-quality view).
            SentinelCore v1.1.0 gained Ledger.Fill (records intended-vs-actual fill price → adverse
            slip ticks); GTrader21 (in-place, observability-only) now logs every realtime fill.
            SLIPPAGE tab: window (Today / 7 / 30) → tiles (fills · avg slip · worst · adverse % · est.
            $ impact via SentinelCore.TickValue), per-instrument drag (sorted), and worst individual
            fills. Only stop/limit fills (a comparable intended price) count — pure-market fills are
            excluded. Stop-fill slippage = the prop risk this surfaces. JOURNAL tab gained a Fills
            tile + "Fills" filter + fill rows (colored by execution quality: adverse=red, improvement
            =green). All still one stream, many views — no parallel journal.
   v1.1.1 — (in-place) new JOURNAL tab (hardening Substrate 2 read side): a blotter + action-audit
            VIEW of the SentinelCore.Ledger JSONL event stream. Window selector (Today / 7 / 30 local
            days) + type filter (All / Orders / Actions / Alerts); hero tiles (events / orders /
            actions / alerts / accounts / instruments); chronological newest-first rows — orders
            colored by side (buy=green, sell=red) with instr·qty·type·px·acct·tag, actions/alerts
            colored by kind (kill/flatten/crit=red, alert/block/halt=amber). Reads via new
            Ledger.ReadRecent()/ReadDay()/Parse() (SentinelCore v1.1.0). On-demand, read-only; cached
            parse so filter buttons don't re-hit disk. No parallel journal — one stream, many views.
   v1.1.0 — VISUAL RESKIN (phase 1 — theme + chrome) to the "flight-instrument" design language (see
            the design-direction mockup + the redesigned GTrader21 risk card). Repaletted all brushes to
            the mockup tokens (void/panel/line/ink/mute + Green/Red=money, Amber=caution) and added a
            cyan ACCENT (=live/watching) + Ink2/Faint/Card2. New TOP BAR: Sentinel "eye" brand mark
            (glow), SENTINEL SUITE title, and the kill-switch as a rounded status pill (dot+label, red
            when engaged). PILL TABS via a TabItem ControlTemplate (transparent idle, panel-toned +
            cyan underline when selected, hover = ink2). All tab CONTENT inherits the new palette;
            per-tab card/tile restyle is phase 2.
   v1.0.9 — (in-place) new ACCOUNTS tab: per-account profile editor (account + firm-preset dropdown +
            ratio/target/daily-loss/size/contracts/session) that writes Sentinel\Profiles.conf (upserts
            the account's line); firm preset prefills ratio/loss; a live list of current profiles from
            SentinelCore.AllAccountProfiles(). Feeds the Governor + (future) sizing.
   v1.0.8 — (in-place) Risk tab: a "Consistency governor" section (per-account daily P&L vs cap/
            loss-stop + status, from SentinelCore's governor registry). Excursion tab: a "Sync all
            ◆ configs" button (write every confident +EV signal's ◆ config in one click; refactored
            ApplyBestRespToGTrader → WriteConfigFor). EyeVerdictCode now delegates to Group.
   v1.0.7 — (in-place) Eye referee → ACTIONABLE: a green/amber recommendation line ("Eye-gate ON/OFF
            for this signal") from EyeVerdictCode; and "Apply ◆" now writes useEyeGate=true/false into
            the .conf when the referee is conclusive (GTrader21 v0.1.6 applies it). Closes the
            referee→config→strategy loop for the Eye filter.
   v1.0.6 — (in-place) Excursion tab: "Active lab configs" live section (which running GTrader21
            instance is on which .conf + TP/SL, from SentinelCore's config-use registry, refreshed
            on the timer); and a ④ "Eye referee" in the per-signal detail (endorsed vs not-endorsed
            medians/expectancy + a plain-English verdict — fills in as Eye data accrues).
   v1.0.5 — (in-place) Excursion viz: ★ mark now ORANGE / ◆ GREEN (colored Runs); per-signal
            FIRE-RATE (n/day + days, in the text rows + detail header — a +EV signal that fires
            rarely isn't a business); scatter EYE-ENDORSEMENT overlay (hollow rings on Eye-endorsed
            fires + legend, accrues once Eye runs); "Apply ◆ to GTrader21 config" button writes the
            best-responsible TP/SL to Sentinel\GTraderConfigs\<inst>_<signal>_<dir>.conf.
   v1.0.4 — (in-place) Excursion viz CONFIDENCE + R:R honesty: edge chart dims small-sample rows
            (n<30) + a "Confident only (n≥30)" filter on the chart & detail selector; expectancy
            grid now shows R:R per config, marks the best RESPONSIBLE (stop≤TP) config with ◆
            distinct from the raw ★, and dims wide-stop mirages; scatter faintly shades the win zone
            (MFE≥TP & MAE<SL). Alias-safe (ShapeLine/Ellipse/Polyline; win-zone uses a Border).
   v1.0.3 — (in-place) Excursion tab PER-SIGNAL DETAIL (WPF-drawn, System.Windows.Shapes): a signal
            selector drives three linked visuals — ① growth line (median MFE/MAE at 5/15/60 min),
            ② outcome scatter (each fire MAE15×MFE15, colored by regime, with dashed TP/SL overlay
            from the trend Best; strided to stay snappy on big clouds), ③ TP/stop expectancy grid
            (all 12 configs from SentinelExcursions.TpStopGrid, diverging bars, ★ best). Summary is
            cached so the selector redraws without reloading.
   v1.0.2 — (in-place) Risk tab: a "Scoped instrument halts" section listing per-instrument kills
            (SentinelRisk v1.0.4 now halts one root, not the whole suite); top line relabeled
            "Global kill-switch". Excursion tab: a new EDGE CHART — a diverging bar per signal group
            (trend regime), green median MFE to the right vs red median MAE to the left at 15 min,
            ranked by edge — the at-a-glance "which signal has an edge" view above the text rows.
   v1.0.1 — (in-place) Risk tab live-phase additions: "Re-request feeds" button
            (SentinelRiskService.ReRequestAllFeeds), a contract-rollover countdown section
            (days-to-roll per instrument, red when entries are blocked), and a news-lockout
            section (active windows + next upcoming from Sentinel\News.conf). Feed rows now
            tag watch-registered feeds and show recovery-attempt counts. Excursion status line
            now reports UNIQUE record count + dupe-fire / legacy-v1.0 skips (SentinelExcursions v1.0.1).
   v1.0.0 — initial dashboard. Control Center menu entry; NTWindow + TabControl shell;
            shared kill-switch top bar (bound to SentinelCore); LIVE "Copy" tab that edits
            the copier config (leader, provider policy, follower rows with per-follower
            instrument-map DSL + multiplier) and pushes it via Reconfigure(). Placeholder
            tabs for Log/Risk/Lens/Arc/Eye. Persistence (save/load config) is a follow-up —
            Apply configures the LIVE service only.
```

