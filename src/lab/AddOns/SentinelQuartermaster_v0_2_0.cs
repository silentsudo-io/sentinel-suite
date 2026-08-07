// ─────────────────────────────────────────────────────────────────────────────
// This Source Code Form is subject to the terms of the Mozilla Public License,
// v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
// one at https://mozilla.org/MPL/2.0/.
//
// Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
// ─────────────────────────────────────────────────────────────────────────────
// ═════════════════════════════════════════════════════════════════════════════
//  SentinelQuartermaster — the Sentinel Suite's raw market-data SUPPLY OFFICER (NT8)
//  File: SentinelQuartermaster_v0_2_0.cs   ·   Version v0.2.0   ·   namespace …AddOns.Sentinel
// ─────────────────────────────────────────────────────────────────────────────
//  WHAT THIS IS
//    The PROCUREMENT head of Quartermaster (spec: Docs/SENTINEL_QUARTERMASTER_SPEC.md §3). A Control
//    Center ▸ Tools window that downloads NT Market Replay data (.nrd). v0.2.0 = THE FLEET: a manifest of
//    many instruments, a ROLL ENGINE that auto-enumerates each root's per-expiry contracts + their front
//    windows, and a BOUNDED-CONCURRENT worker pool that grinds the whole list unattended with skip-existing
//    resume, retry/backoff, a disk-space guard, and per-file provenance for the Python catalog head.
//
//  ⚖ CLEAN-ROOM ORIGIN NOTE (mandatory — spec §8, same discipline as SentinelWAE v2 / LiquidityWalls)
//    NinjaTrader exposes NO public API for triggering Market Replay downloads. The METHOD SHAPE is a FACT
//    about NT's API, discovered by REFLECTING over NinjaTrader.Core.dll metadata (2026-07-18):
//        <adapter/HistoricalDataClient>.RequestMarketReplay(Cbi.Instrument, DateTime dateEst,
//            Action<Cbi.ErrorCode,string,object> callback, object state, …)  → output lands at
//        Core.Globals.UserDataDir\db\replay\<Instrument.FullName>\yyyyMMdd.nrd
//    The concrete overload VARIES by provider, so the invoke is built ADAPTIVELY from the resolved method's
//    actual parameters (BuildArgs). Reimplemented from OBSERVED behaviour — not one line of the unlicensed
//    reference (greybeard MultiDayDownload, all-rights-reserved) ships here. Suite dep = SentinelCore
//    Foundation only (Log + SettingsDir).
//
//  CHANGELOG
//    v0.2.0a (2026-07-18) — RETRY CLASSIFICATION (in-place patch). A "no market replay data available" panic is
//            a PERMANENT answer, not a transient fault: PROVEN (3 independent ways — fetch-log fingerprint, the
//            .nrd disk store, a manual GUI Get-Market-Replay pull) that Tradovate replay is a ~90-day ROLLING
//            floor, hard-stop at 2026-04-19. So FailJob no longer re-enqueues a permanent "not available" — it
//            was re-asking each dead date ~2× → ~16k wasted round-trips on the first 2023→now fleet run. TRANSPORT
//            faults (timeout / invoke-exception) still retry. Permanent misses now log "∅ … not available" (amber),
//            not "✗" (red), so the operator reads absence-of-data, not error. (Deep 2023 tick+quote = Databento.)
//    v0.2.0 (2026-07-18) — THE FLEET. Manifest (Sentinel\Quartermaster\Fetch.conf: roots + global window +
//            concurrency/attempts); ROLL ENGINE (root → per-expiry contracts over the range, front-month
//            tiling via 3rd-Friday roll dates; quarterly index/FX/rates · even-month metals · monthly energy);
//            BOUNDED-CONCURRENT pool (auto-tunes down on error bursts, up when clean); retry+re-enqueue;
//            per-request timeout sweep; DISK-SPACE GUARD (pause at a free-GB floor — never fill the drive);
//            most-recent-first ordering (freshest data lands before the floor); aggregate progress + STOP;
//            provenance JSONL unchanged. LIVE-PROVEN base = v0.1.0 (frozen): reflection self-test + adaptive
//            invoke + provenance, validated pulling real MNQ .nrd on legacy-node.
//    v0.1.0 (2026-07-18) — first cut (frozen checkpoint): Tools-menu window; version-guard self-test; single
//            instrument + date-range fetch; skip-existing; watchdog; provenance JSONL; Fetch.conf persist.
// ═════════════════════════════════════════════════════════════════════════════

#region Using declarations
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NinjaTrader.Cbi;
using NinjaTrader.Core;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.Sentinel
{
    // ── AddOn: adds "Sentinel Quartermaster" under Control Center ▸ Tools (fallback ▸ New) ──
    public class SentinelQuartermasterAddOn : NinjaTrader.NinjaScript.AddOnBase
    {
        private NTMenuItem _menuItem, _hostMenu;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name        = "SentinelQuartermaster";
                Description = "Sentinel Quartermaster — Market Replay data procurement fleet (Control Center ▸ Tools).";
            }
        }

        protected override void OnWindowCreated(Window window)
        {
            ControlCenter cc = window as ControlCenter;
            if (cc == null || _menuItem != null) return;
            _hostMenu = cc.FindFirst("ControlCenterMenuItemTools") as NTMenuItem
                     ?? cc.FindFirst("ControlCenterMenuItemNew")   as NTMenuItem;
            if (_hostMenu == null) return;
            _menuItem = new NTMenuItem { Header = "Sentinel Quartermaster", Style = Application.Current.TryFindResource("MainMenuItem") as Style };
            _menuItem.Click += OnMenuClick;
            _hostMenu.Items.Add(_menuItem);
        }

        protected override void OnWindowDestroyed(Window window)
        {
            if (_menuItem != null && window is ControlCenter)
            {
                if (_hostMenu != null && _hostMenu.Items.Contains(_menuItem)) _hostMenu.Items.Remove(_menuItem);
                _menuItem.Click -= OnMenuClick; _menuItem = null; _hostMenu = null;
            }
        }

        private void OnMenuClick(object sender, RoutedEventArgs e)
        {
            Globals.RandomDispatcher.InvokeAsync(new Action(() => { var w = new SentinelQuartermasterWindow(); w.Show(); w.Activate(); }));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    public class SentinelQuartermasterWindow : NTWindow
    {
        private const string Ver           = "v0.2.0";
        private const string LogTag        = "Quartermaster";
        private const long   MinValidBytes = 1024;
        private const int    WatchdogSec   = 180;   // a single .nrd can be 400 MB → generous per-request timeout
        private const double FreeFloorGB   = 400.0; // pause the fleet if the target drive drops below this — reserves headroom for sentinel.db + the excursion corpus
        private const int    MaxConcCap    = 6;     // never exceed this many concurrent replay requests
        private static readonly BindingFlags BF = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly string[] AllRoots =
            { "ES","NQ","YM","RTY","MES","MNQ","MYM","M2K","GC","MGC","SI","CL","NG","6E","6J","6B","ZB","ZN","ZF" };

        // theme
        private static Brush Bg=FB(Color.FromRgb(0x0A,0x0E,0x17)), Card=FB(Color.FromRgb(0x11,0x17,0x26)),
            Edge=FB(Color.FromRgb(0x1E,0x2A,0x3D)), Text=FB(Color.FromRgb(0xE9,0xEE,0xF7)), Ink2=FB(Color.FromRgb(0xAE,0xBA,0xCE)),
            Muted=FB(Color.FromRgb(0x6C,0x7A,0x92)), Accent=FB(Color.FromRgb(0x3F,0xD1,0xE0)), Green=FB(Color.FromRgb(0x25,0xD0,0x8B)),
            Red=FB(Color.FromRgb(0xFF,0x5C,0x6A)), Amber=FB(Color.FromRgb(0xF2,0xB3,0x4C));
        private static SolidColorBrush FB(Color c){ var b=new SolidColorBrush(c); b.Freeze(); return b; }
        private static void ApplyTheme()
        {
            try { SentinelSkin.MaybeRefreshTheme();
                Bg=FB(SentinelSkin.KVoid); Card=FB(SentinelSkin.KPanel); Edge=FB(SentinelSkin.KLine); Text=FB(SentinelSkin.KInk);
                Ink2=FB(SentinelSkin.KInk2); Muted=FB(SentinelSkin.KMute); Accent=FB(SentinelSkin.KAccent);
                Green=FB(SentinelSkin.KUp); Red=FB(SentinelSkin.KDown); Amber=FB(SentinelSkin.KWarn); } catch (Exception _sx) { SentinelCore.Swallow("SentinelQuartermaster.ApplyTheme", _sx); }
        }

        // reflection conduit
        private object _adapter; private MethodInfo _mi; private string _connName; private bool _ready, _sigLogged;

        // ── the fleet ──
        private sealed class Job { public Instrument Instr; public string Contract; public DateTime Date; public string Path; public int Attempts; }
        private readonly List<Job> _jobs = new List<Job>();
        private int _ji, _inFlight, _maxConc = 3, _attempts = 3;
        private int _total, _ok, _skip, _fail, _retry;
        private double _gb;
        private bool _running, _paused;
        private int _sinceTune, _errWindow;
        private readonly Dictionary<Job, DateTime> _flight = new Dictionary<Job, DateTime>();
        private DispatcherTimer _sweep;

        // UI
        private Border _dot; private TextBlock _statusTb, _summaryTb, _tuneTb;
        private Button _fetchBtn, _stopBtn, _verifyBtn;
        private StackPanel _logPanel; private ScrollViewer _logScroll;

        public SentinelQuartermasterWindow()
        {
            Caption = "Sentinel Quartermaster"; Width = 560; Height = 660;
            ApplyTheme(); Content = BuildLayout();
            EnsureDefaultManifest();
            Closed += (s,e) => { _running=false; try { if (_sweep!=null) _sweep.Stop(); } catch (Exception _sx) { SentinelCore.Swallow("SentinelQuartermaster.SentinelQuartermasterWindow", _sx); } };
            SelfTest();
        }

        // ── layout ──
        private FrameworkElement BuildLayout()
        {
            var root = new DockPanel { Background = Bg, LastChildFill = true };
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12,10,12,6) };
            head.Children.Add(new TextBlock { Text="QUARTERMASTER", Foreground=Text, FontWeight=FontWeights.Bold, FontSize=15, VerticalAlignment=VerticalAlignment.Center });
            head.Children.Add(Chip(Ver)); head.Children.Add(Chip("FLEET"));
            DockPanel.SetDock(head, Dock.Top); root.Children.Add(head);

            var statusRow = new StackPanel { Orientation=Orientation.Horizontal, Margin=new Thickness(12,0,12,6) };
            _dot = new Border { Width=10, Height=10, CornerRadius=new CornerRadius(5), Background=Muted, VerticalAlignment=VerticalAlignment.Center, Margin=new Thickness(0,0,7,0) };
            statusRow.Children.Add(_dot);
            _statusTb = new TextBlock { Text="checking…", Foreground=Ink2, FontSize=12, VerticalAlignment=VerticalAlignment.Center };
            statusRow.Children.Add(_statusTb);
            DockPanel.SetDock(statusRow, Dock.Top); root.Children.Add(statusRow);

            var info = new TextBlock { Foreground=Muted, FontSize=11, Margin=new Thickness(12,0,12,6), TextWrapping=TextWrapping.Wrap,
                Text="Fleet reads Sentinel\\Quartermaster\\Fetch.conf (roots + global window + concurrency). Roll engine auto-enumerates per-expiry contracts. Most-recent-first; pauses at the disk floor." };
            DockPanel.SetDock(info, Dock.Top); root.Children.Add(info);

            var btnRow = new StackPanel { Orientation=Orientation.Horizontal, Margin=new Thickness(12,2,12,6) };
            _fetchBtn = Btn("RUN FLEET", Accent, true); _fetchBtn.Click += (s,e) => StartFleet();
            _stopBtn  = Btn("STOP", Edge, false); _stopBtn.IsEnabled=false; _stopBtn.Click += (s,e) => { _running=false; SetStatus(Amber,"stopping — draining in-flight…"); };
            _verifyBtn= Btn("RE-TEST", Edge, false); _verifyBtn.Click += (s,e) => SelfTest();
            btnRow.Children.Add(_fetchBtn); btnRow.Children.Add(_stopBtn); btnRow.Children.Add(_verifyBtn);
            DockPanel.SetDock(btnRow, Dock.Top); root.Children.Add(btnRow);

            _summaryTb = new TextBlock { Text="", Foreground=Text, FontSize=12, Margin=new Thickness(12,0,12,2) };
            DockPanel.SetDock(_summaryTb, Dock.Top); root.Children.Add(_summaryTb);
            _tuneTb = new TextBlock { Text="", Foreground=Muted, FontSize=11, Margin=new Thickness(12,0,12,4) };
            DockPanel.SetDock(_tuneTb, Dock.Top); root.Children.Add(_tuneTb);

            _logPanel = new StackPanel { Margin=new Thickness(8,2,8,8) };
            _logScroll = new ScrollViewer { VerticalScrollBarVisibility=ScrollBarVisibility.Auto, Content=_logPanel, Background=Card, Margin=new Thickness(8) };
            root.Children.Add(_logScroll);
            return root;
        }

        // ── self-test (proven in v0.1.0): resolve RequestMarketReplay off Adapter/HistoricalDataClient/ClientConnection ──
        private void SelfTest()
        {
            _ready=false; _adapter=null; _mi=null; _connName=null;
            var diag = new List<string>();
            try
            {
                var conns = Connection.Connections;
                if (conns==null || conns.Count==0) diag.Add("no connections configured");
                else foreach (Connection c in conns)
                {
                    if (c==null) continue;
                    string nm = SafeName(c); diag.Add(nm + " · " + c.Status);
                    ProbeSource(c, nm, "Adapter",              GetProp(c,"Adapter"),              diag);
                    ProbeSource(c, nm, "HistoricalDataClient", GetProp(c,"HistoricalDataClient"), diag);
                    ProbeSource(c, nm, "ClientConnection",     GetProp(c,"ClientConnection"),     diag);
                }
            }
            catch (Exception ex) { diag.Add("self-test exception: " + ex.Message); }

            if (_logPanel!=null) { _logPanel.Children.Clear(); AddLog("— self-test —", Accent); foreach (string d in diag) AddLog("  "+d, Muted); }
            if (_ready) SetStatus(Green, "READY · " + _connName + " · RequestMarketReplay resolved");
            else { SetStatus(Red, "NOT READY · RequestMarketReplay not resolved — see the dump"); SentinelCore.Log(LogTag,"SELF-TEST FAIL | "+string.Join(" ; ",diag)); }
            if (_fetchBtn!=null) _fetchBtn.IsEnabled = _ready;
        }

        private void ProbeSource(Connection c, string nm, string label, object src, List<string> diag)
        {
            if (src==null) { diag.Add("   "+label+" = null"); return; }
            try
            {
                Type t = src.GetType(); var replay = new List<string>();
                foreach (MethodInfo m in t.GetMethods(BF)) if (m.Name.IndexOf("Replay",StringComparison.OrdinalIgnoreCase)>=0) replay.Add(m.Name+"("+m.GetParameters().Length+")");
                diag.Add("   "+label+" = "+t.FullName+(replay.Count>0?"  ["+string.Join(", ",replay)+"]":"  [no *Replay]"));
                if (!_ready)
                {
                    MethodInfo mi = t.GetMethod("RequestMarketReplay", BF, null,
                        new[]{ typeof(Instrument), typeof(DateTime), typeof(Action<ErrorCode,string,object>), typeof(object) }, null);
                    if (mi==null) { try { mi = t.GetMethod("RequestMarketReplay", BF); } catch (Exception _sx) { SentinelCore.Swallow("SentinelQuartermaster.ProbeSource", _sx); } }
                    if (mi!=null && c.Status==ConnectionStatus.Connected)
                    { _adapter=src; _mi=mi; _connName=nm; _ready=true; diag.Add("   → RESOLVED on "+label); }
                }
            }
            catch (Exception ex) { diag.Add("   "+label+" probe error: "+ex.Message); }
        }

        // ── fleet orchestration ──
        private void StartFleet()
        {
            if (!_ready || _running) return;
            _jobs.Clear(); _ji=0; _inFlight=0; _ok=_skip=_fail=_retry=0; _gb=0; _sigLogged=false; _paused=false; _sinceTune=0; _errWindow=0;
            _logPanel.Children.Clear();

            List<string> roots; DateTime from, to; int conc, att;
            if (!LoadManifest(out roots, out from, out to, out conc, out att)) { AddLog("✗ Fetch.conf parse failed", Red); return; }
            _maxConc = Math.Max(1, Math.Min(MaxConcCap, conc)); _attempts = Math.Max(1, att);

            AddLog("── planning fleet: "+roots.Count+" roots · "+from.ToString("yyyy-MM-dd")+" → "+to.ToString("yyyy-MM-dd"), Accent);
            foreach (string r in roots) EnumerateRoot(r, from, to);
            // most-recent-first: freshest, most-useful replay lands before the disk floor
            _jobs.Sort((a,b) => b.Date.CompareTo(a.Date));
            _total = _jobs.Count;
            if (_total==0) { AddLog("✗ nothing to fetch (no known contracts in range)", Red); return; }

            AddLog("── fleet READY: "+_total+" sessions queued · conc="+_maxConc+" · attempts="+_attempts, Accent);
            SentinelCore.Log(LogTag, "FLEET start "+_total+" sessions, "+roots.Count+" roots "+from.ToString("yyyyMMdd")+"-"+to.ToString("yyyyMMdd")+" conc="+_maxConc);
            _running=true; _fetchBtn.IsEnabled=false; _stopBtn.IsEnabled=true;

            _sweep = new DispatcherTimer(DispatcherPriority.Background, Dispatcher){ Interval=TimeSpan.FromSeconds(15) };
            _sweep.Tick += (s,e)=>SweepTimeouts();
            _sweep.Start();
            Pump();
        }

        private void Pump()
        {
            while (_running && !_paused && _inFlight < _maxConc && _ji < _jobs.Count)
            {
                // disk guard — never fill the drive
                if (FreeGB() < FreeFloorGB) { _paused=true; SetStatus(Amber,"PAUSED · disk floor ("+FreeFloorGB.ToString("0")+" GB) reached — free space + re-run to resume"); SentinelCore.Log(LogTag,"PAUSED disk floor"); break; }
                Job j = _jobs[_ji++];
                if (FileOk(j.Path)) { _skip++; Progress(); continue; }
                if (j.Instr==null) { j.Instr = SafeInstr(j.Contract); if (j.Instr==null) { _fail++; Progress(); continue; } }
                Fire(j);
            }
            if (_inFlight==0 && (_ji >= _jobs.Count || !_running || _paused)) FinishFleet();
        }

        private void Fire(Job j)
        {
            try
            {
                Action<ErrorCode,string,object> cb = OnDone;
                bool hasCb; object[] args = BuildArgs(_mi, j.Instr, j.Date, cb, j, out hasCb);
                if (!_sigLogged) { _sigLogged=true; LogResolvedSig(); }
                _flight[j] = DateTime.UtcNow; _inFlight++;
                _mi.Invoke(_adapter, args);
                Progress();
            }
            catch (Exception ex) { if (_flight.Remove(j)) _inFlight--; FailJob(j, "invoke: "+ex.Message); Pump(); }
        }

        private void OnDone(ErrorCode ec, string msg, object state)
        {
            Dispatcher.InvokeAsync(new Action(() =>
            {
                Job j = state as Job; if (j==null) return;
                if (!_flight.Remove(j)) return;   // late/duplicate callback — the watchdog already timed out + re-queued this attempt; ignore (no double-count)
                _inFlight--;
                long bytes = FileLen(j.Path);
                if (bytes >= MinValidBytes)
                {
                    _ok++; _gb += bytes/1073741824.0; RegTune(false);
                    WriteProvenance(j, bytes, true, ec.ToString());
                    if (_ok % 25 == 0 || _fail+_retry < 4) AddLog("✓ "+j.Contract+" "+j.Date.ToString("yyyy-MM-dd")+"  "+Mb(bytes), Green);
                }
                else { RegTune(true); FailJob(j, "no/short file · "+ec+(string.IsNullOrEmpty(msg)?"":" "+msg)); }
                Progress(); Pump();
            }));
        }

        // an authoritative "the archive never had this date" answer — retrying only wastes round-trips.
        // (contrast: "timeout" / invoke-exception are TRANSPORT faults that a retry can legitimately clear.)
        private static bool IsPermanent(string reason)
        {
            return reason != null &&
                   reason.IndexOf("no market replay data", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void FailJob(Job j, string reason)
        {
            j.Attempts++;
            bool perm = IsPermanent(reason);
            if (!perm && j.Attempts < _attempts && _running)
            { _retry++; _jobs.Add(j); AddLog("↻ "+j.Contract+" "+j.Date.ToString("yyyy-MM-dd")+"  retry "+j.Attempts+" ("+reason+")", Amber); }
            else
            {
                _fail++; WriteProvenance(j, FileLen(j.Path), false, reason);
                if (perm) AddLog("∅ "+j.Contract+" "+j.Date.ToString("yyyy-MM-dd")+"  not available (no retry)", Amber);
                else      AddLog("✗ "+j.Contract+" "+j.Date.ToString("yyyy-MM-dd")+"  "+reason, Red);
            }
        }

        private void SweepTimeouts()
        {
            if (!_running && _inFlight==0) { if (_sweep!=null) _sweep.Stop(); return; }
            var stale = new List<Job>();
            foreach (var kv in _flight) if ((DateTime.UtcNow-kv.Value).TotalSeconds > WatchdogSec) stale.Add(kv.Key);
            foreach (Job j in stale) { _flight.Remove(j); _inFlight--; RegTune(true); FailJob(j, "timeout"); }
            if (stale.Count>0) Pump();
        }

        // auto-tune: reduce concurrency on error bursts, nudge up when clean
        private void RegTune(bool err)
        {
            _errWindow += err ? 1 : 0; _sinceTune++;
            if (_sinceTune < 12) return;
            if (_errWindow >= 4 && _maxConc > 1) { _maxConc--; SentinelCore.Log(LogTag,"auto-tune ↓ conc="+_maxConc); }
            else if (_errWindow == 0 && _maxConc < MaxConcCap) { _maxConc++; }
            _sinceTune=0; _errWindow=0;
        }

        private void FinishFleet()
        {
            if (!_running || _paused) { }
            _running=false; if (_sweep!=null) _sweep.Stop();
            _fetchBtn.IsEnabled=_ready; _stopBtn.IsEnabled=false;
            string s = "done · "+_ok+" ok · "+_skip+" skipped · "+_fail+" failed · "+_retry+" retries · "+_gb.ToString("0.0")+" GB";
            AddLog("── "+s, Accent); Progress(); SentinelCore.Log(LogTag,"FLEET "+s);
        }

        // ── ROLL ENGINE: root → per-expiry contracts over [from,to], front-month tiled by 3rd-Friday ──
        private enum Roll { Quarterly, EvenMonths, Monthly }
        private static Roll RollFor(string root)
        {
            root = root.ToUpperInvariant();
            if (root=="GC"||root=="MGC"||root=="SI") return Roll.EvenMonths;
            if (root=="CL"||root=="NG") return Roll.Monthly;
            return Roll.Quarterly;   // index / FX / rates
        }
        private static int[] ExpiryMonths(Roll r)
        {
            if (r==Roll.Quarterly) return new[]{3,6,9,12};
            if (r==Roll.EvenMonths) return new[]{2,4,6,8,10,12};
            return new[]{1,2,3,4,5,6,7,8,9,10,11,12};
        }
        private static DateTime ThirdFriday(int y, int m)
        {
            var d = new DateTime(y,m,1); int f=0;
            while (true){ if (d.DayOfWeek==DayOfWeek.Friday){ f++; if (f==3) return d; } d=d.AddDays(1); }
        }
        private static string ContractName(string root, int y, int m)
        { return root + " " + m.ToString("00",CultureInfo.InvariantCulture) + "-" + (y%100).ToString("00",CultureInfo.InvariantCulture); }

        private void EnumerateRoot(string root, DateTime from, DateTime to)
        {
            Roll r = RollFor(root); int[] months = ExpiryMonths(r);
            // ordered expiry list (with one before `from` so the first window's start is right)
            var exp = new List<DateTime>();       // roll (3rd-Fri) dates
            var con = new List<string>();          // contract names
            for (int y = from.Year-1; y <= to.Year+1; y++)
                foreach (int m in months) { exp.Add(ThirdFriday(y,m)); con.Add(ContractName(root,y,m)); }
            for (int i = 0; i < exp.Count; i++)
            {
                DateTime winEnd = exp[i];
                DateTime winStart = i>0 ? exp[i-1].AddDays(1) : exp[i].AddMonths(-4);
                DateTime s = winStart > from ? winStart : from;
                DateTime e = winEnd   < to   ? winEnd   : to;
                if (e < s) continue;
                for (DateTime d = s.Date; d <= e.Date; d = d.AddDays(1))
                    if (d.DayOfWeek != DayOfWeek.Saturday)
                        _jobs.Add(new Job { Contract=con[i], Date=d, Path=NrdPath(con[i], d) });
            }
        }

        // ── manifest (Sentinel\Quartermaster\Fetch.conf) ──
        private string ConfPath(){ return Path.Combine(SentinelCore.SettingsDir, "Quartermaster", "Fetch.conf"); }
        private void EnsureDefaultManifest()
        {
            try
            {
                string p = ConfPath(); if (File.Exists(p)) return;
                Directory.CreateDirectory(Path.GetDirectoryName(p));
                var sb = new StringBuilder();
                sb.Append("# Sentinel Quartermaster — FLEET manifest\n");
                sb.Append("# roots = comma list; the roll engine auto-enumerates each root's per-expiry contracts.\n");
                sb.Append("# ⚠ the full set below across this window is ~terabytes — the disk guard pauses at the free-GB floor.\n");
                sb.Append("from = 2022-01-01\n");
                sb.Append("to   = " + DateTime.Today.ToString("yyyy-MM-dd") + "\n");
                sb.Append("concurrency = 3\n");
                sb.Append("attempts = 3\n");
                sb.Append("roots = " + string.Join(",", AllRoots) + "\n");
                File.WriteAllText(p, sb.ToString());
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelQuartermaster.EnsureDefaultManifest", _sx); }
        }
        private bool LoadManifest(out List<string> roots, out DateTime from, out DateTime to, out int conc, out int att)
        {
            roots = new List<string>(); from = DateTime.Today.AddYears(-3); to = DateTime.Today; conc = 3; att = 3;
            try
            {
                string p = ConfPath(); if (!File.Exists(p)) EnsureDefaultManifest();
                foreach (string line in File.ReadAllLines(p))
                {
                    string t = line.Trim(); if (t.Length==0 || t[0]=='#') continue;
                    int eq = t.IndexOf('='); if (eq<=0) continue;
                    string k = t.Substring(0,eq).Trim().ToLowerInvariant(), v = t.Substring(eq+1).Trim();
                    if (k=="from") DateTime.TryParseExact(v,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out from);
                    else if (k=="to") DateTime.TryParseExact(v,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out to);
                    else if (k=="concurrency") int.TryParse(v, out conc);
                    else if (k=="attempts") int.TryParse(v, out att);
                    else if (k=="roots") foreach (string r in v.Split(',')) { string rr=r.Trim(); if (rr.Length>0) roots.Add(rr); }
                }
                return true;
            }
            catch { return false; }
        }

        // ── adaptive invoke (proven v0.1.0): match the resolved overload's actual parameters ──
        private object[] BuildArgs(MethodInfo mi, Instrument instr, DateTime date, Action<ErrorCode,string,object> cb, object state, out bool hasCallback)
        {
            hasCallback=false; ParameterInfo[] pars = mi.GetParameters(); var args = new object[pars.Length]; bool stateSet=false;
            for (int i=0;i<pars.Length;i++)
            {
                Type pt = pars[i].ParameterType;
                if (typeof(Instrument).IsAssignableFrom(pt)) args[i]=instr;
                else if (pt==typeof(DateTime)) args[i]=date;
                else if (typeof(Delegate).IsAssignableFrom(pt)) { if (pt.IsAssignableFrom(cb.GetType())) { args[i]=cb; hasCallback=true; } else args[i]=null; }
                else if (pt==typeof(object) && !stateSet) { args[i]=state; stateSet=true; }
                else if (pt.IsValueType) args[i]=Activator.CreateInstance(pt);
                else args[i]=null;
            }
            return args;
        }
        private void LogResolvedSig()
        {
            try { var ps=new List<string>(); foreach (ParameterInfo p in _mi.GetParameters()) ps.Add(p.ParameterType.FullName+" "+p.Name);
                SentinelCore.Log(LogTag,"INVOKE SIG "+_mi.Name+"("+string.Join(", ",ps)+")"); } catch (Exception _sx) { SentinelCore.Swallow("SentinelQuartermaster.LogResolvedSig", _sx); }
        }

        // ── provenance ──
        private void WriteProvenance(Job j, long bytes, bool ok, string reason)
        {
            try
            {
                string dir = Path.Combine(SentinelCore.SettingsDir, "Quartermaster"); Directory.CreateDirectory(dir);
                string root = j.Contract.IndexOf(' ')>0 ? j.Contract.Substring(0,j.Contract.IndexOf(' ')) : j.Contract;
                var sb = new StringBuilder(256);
                sb.Append('{').Append("\"symbol\":").Append(Js(root)).Append(",\"contract\":").Append(Js(j.Contract))
                  .Append(",\"session\":\"").Append(j.Date.ToString("yyyy-MM-dd")).Append('"').Append(",\"path\":").Append(Js(j.Path))
                  .Append(",\"bytes\":").Append(bytes).Append(",\"ok\":").Append(ok?"true":"false").Append(",\"reason\":").Append(Js(reason))
                  .Append(",\"provider\":").Append(Js(_connName)).Append(",\"ntVersion\":").Append(Js(NtVersion()))
                  .Append(",\"fetchedUtc\":\"").Append(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")).Append('"').Append("}\n");
                File.AppendAllText(Path.Combine(dir,"fetch-log.jsonl"), sb.ToString());
            }
            catch (Exception ex) { SentinelCore.Log(LogTag,"provenance write failed: "+ex.Message); }
        }

        // ── helpers ──
        private static string NrdPath(string contract, DateTime d){ return Path.Combine(Globals.UserDataDir,"db","replay",contract,d.ToString("yyyyMMdd")+".nrd"); }
        private static Instrument SafeInstr(string name){ try { return Instrument.GetInstrument(name, false); } catch { return null; } }
        private static bool FileOk(string p){ return FileLen(p) >= MinValidBytes; }
        private static long FileLen(string p){ try { var fi=new FileInfo(p); return fi.Exists?fi.Length:0; } catch { return 0; } }
        private static string Mb(long b){ return (b/1048576.0).ToString("0.0",CultureInfo.InvariantCulture)+" MB"; }
        private static double FreeGB(){ try { return new DriveInfo(Path.GetPathRoot(Path.Combine(Globals.UserDataDir,"db")))?.AvailableFreeSpace/1073741824.0 ?? 0; } catch { return 1e9; } }
        private static string SafeName(Connection c){ try { return c.Options!=null?c.Options.Name:c.ToString(); } catch { return "connection"; } }
        private static string NtVersion(){ try { return typeof(Connection).Assembly.GetName().Version.ToString(); } catch { return "?"; } }
        private static object GetProp(object o, string name){ try { var p=o.GetType().GetProperty(name,BF); return p!=null?p.GetValue(o):null; } catch { return null; } }
        private static string Js(string s){ if (s==null) return "null"; var sb=new StringBuilder(s.Length+2); sb.Append('"'); foreach (char c in s){ if (c=='"'||c=='\\') sb.Append('\\').Append(c); else if (c=='\n') sb.Append("\\n"); else sb.Append(c);} return sb.Append('"').ToString(); }

        private void SetStatus(Brush dot, string t){ if (_dot!=null) _dot.Background=dot; if (_statusTb!=null){ _statusTb.Text=t; _statusTb.Foreground = dot==Red?Red:Ink2; } }
        private void Progress()
        {
            int done = _ok+_skip+_fail;
            if (_summaryTb!=null) _summaryTb.Text = done+"/"+_total+"  ·  ✓"+_ok+"  skip "+_skip+"  ✗"+_fail+"  ↻"+_retry+"  ·  "+_gb.ToString("0.0")+" GB";
            if (_tuneTb!=null) _tuneTb.Text = "in-flight "+_inFlight+"/"+_maxConc+"  ·  free "+FreeGB().ToString("0")+" GB" + (_paused?"  ·  PAUSED":"");
        }
        private void AddLog(string s, Brush color)
        {
            if (_logPanel==null) return;
            _logPanel.Children.Add(new TextBlock { Text=s, Foreground=color, FontSize=11, FontFamily=new FontFamily("Consolas"), TextWrapping=TextWrapping.Wrap, Margin=new Thickness(2,1,2,1) });
            while (_logPanel.Children.Count > 200) _logPanel.Children.RemoveAt(0);   // cap for a long fleet run
            if (_logScroll!=null) _logScroll.ScrollToEnd();
        }
        private Border Chip(string t){ return new Border { Background=Edge, CornerRadius=new CornerRadius(3), Margin=new Thickness(8,0,0,0), Padding=new Thickness(6,1,6,1), VerticalAlignment=VerticalAlignment.Center, Child=new TextBlock { Text=t, Foreground=Accent, FontSize=11 } }; }
        private Button Btn(string t, Brush bg, bool primary){ return new Button { Content=t, Foreground=primary?Bg:Text, Background=bg, BorderBrush=Edge, BorderThickness=new Thickness(1), Padding=new Thickness(14,5,14,5), Margin=new Thickness(0,0,8,0), FontWeight=primary?FontWeights.Bold:FontWeights.Normal, Cursor=System.Windows.Input.Cursors.Hand }; }
    }
}
