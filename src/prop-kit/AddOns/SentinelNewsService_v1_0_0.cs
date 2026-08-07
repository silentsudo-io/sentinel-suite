// ─────────────────────────────────────────────────────────────────────────────
// This Source Code Form is subject to the terms of the Mozilla Public License,
// v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
// one at https://mozilla.org/MPL/2.0/.
//
// Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
// ─────────────────────────────────────────────────────────────────────────────
// ═════════════════════════════════════════════════════════════════════════════
//  SentinelNewsService — native C# economic-calendar → News.conf feeder (Sentinel Suite, NT8)
//  File: SentinelNewsService_v1_0_0.cs   ·   Version v1.0.0   ·   namespace …AddOns.Sentinel
// ─────────────────────────────────────────────────────────────────────────────
//  WHAT  (the event-veto axis, fully AUTOMATED, NO PYTHON — see economic-calendar-event-veto memory)
//    A headless AddOn that, on a timer inside NinjaTrader (which is always running), FETCHES the
//    high-impact economic calendar itself and WRITES the Sentinel\News.conf managed block that
//    SentinelRiskService already reads → SetNewsLockouts → CanEnter + the Council's news veto.
//    This REPLACES the external EconomicCalendar.py → sentinel_newsconf.py chain with one native
//    service (no Python, no schtasks). It writes the IDENTICAL managed-block format the Python bridge
//    wrote, so it is a drop-in — the RiskService reader, the v1.0.9 freshness guard, and the dashboard
//    Risk-view news section all consume it unchanged.
//
//  DATA  — ForexFactory weekly JSON (https://nfs.faireconomy.media/ff_calendar_thisweek.json): a flat
//    array of {title,country,date(ISO w/ offset),impact,forecast,previous}. We keep only the configured
//    currencies (default USD — macro USD hits ES/NQ/GC alike) at/above the min impact, convert each
//    event's offset-aware time to MACHINE-LOCAL wall time (matches Core.Globals.Now, which the RiskService
//    compares against), and emit `YYYY-MM-DD HH:mm | Event | all | beforeMin | afterMin` lines.
//
//  DELIBERATELY NOT WRITTEN: the directional bias. Only the BLACKOUT WINDOWS are universal; the equity
//    bias_score is NOT (hot CPI → hawkish → higher real yields → often BEARISH gold, opposite of equities).
//    scope is always "all" (a spike halts every instrument); direction stays out of News.conf (caveat #2).
//
//  SAFETY  — fully fail-SAFE: any fetch/parse error leaves the existing News.conf UNTOUCHED and logs a
//    warning; the RiskService freshness guard (v1.0.9) then makes the silent fail-OPEN visible. Network I/O
//    runs on the timer threadpool thread with a reentrancy guard; every path is wrapped, nothing throws into
//    NT. Manual News.conf lines OUTSIDE the ECONCAL markers are always preserved.
//
//  CONFIG (optional Sentinel\NewsService.conf, key=value; sensible defaults if absent):
//    enabled=true  minImpact=HIGH  currencies=USD  beforeMin=5  afterMin=20  refreshMinutes=240  url=<override>
//
//  CHANGELOG
//    v1.0.0 (2026-07-08) — initial native feeder. Timer fetch (ForexFactory weekly JSON) → filter (currency +
//             min impact) → ET/offset → local → managed-block write into News.conf (byte-compatible with the
//             Python bridge's markers/format). Optional NewsService.conf overrides. Fail-safe; no Python.
//             LIVE-VALIDATED 2026-07-08: fetched + parsed the real FF payload, wrote "FOMC Meeting Minutes 13:00".
//             + MinRefetchMinutes backoff (default 60) — skip the fetch if News.conf was refreshed recently so a
//             rapid F5/restart storm can't hammer the feed's CDN into a 429 (observed on repeated recompiles).
//             + no-trade WINDOW is dashboard-editable: SaveConfig() persists to NewsService.conf; RewriteFromCache()
//             re-emits News.conf from the last fetch with the new before/after INSTANTLY (no network). Props often
//             want ~10-15m each side — set it on the Home tab.
// ═════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using NinjaTrader.NinjaScript;

namespace NinjaTrader.NinjaScript.AddOns.Sentinel
{
    public class SentinelNewsService_v1_0_0 : NinjaTrader.NinjaScript.AddOnBase
    {
        public static SentinelNewsService_v1_0_0 Instance { get; private set; }

        // ── config (static so the dashboard can tune live; NewsService.conf overrides at start) ──
        public static bool     Enabled        = true;
        public static string   MinImpact      = "HIGH";                 // HIGH | MEDIUM
        public static string   Currencies     = "USD";                  // comma list
        public static int      BeforeMin      = 15;                     // lockout minutes before an event (prop-typical default)
        public static int      AfterMin       = 15;                     // lockout minutes after an event  (prop-typical default)
        public static int      RefreshMinutes = 240;                    // re-fetch cadence
        public static int      MinRefetchMinutes = 60;                  // skip a fetch if News.conf was refreshed within this (avoids hammering the feed on rapid F5/restart → 429)
        public static string   FeedUrl        = "https://nfs.faireconomy.media/ff_calendar_thisweek.json";

        private const string BlockStart = "# >>> SENTINEL ECONCAL (auto-generated; edits between the markers are overwritten) >>>";
        private const string BlockEnd   = "# <<< SENTINEL ECONCAL <<<";
        private static readonly Dictionary<string, int> ImpactRank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        { { "LOW", 0 }, { "MEDIUM", 1 }, { "HIGH", 2 } };

        private Timer _timer;
        private volatile bool _started, _stopping;
        private int _running;                                            // reentrancy guard for the fetch tick
        private List<Ev> _lastEvents;                                    // last successful fetch — lets a window change re-apply with NO network call

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name        = "SentinelNewsService";
                Description = "Sentinel Suite — native economic-calendar feeder. Fetches high-impact events and "
                            + "writes Sentinel\\News.conf (the event-veto windows the Risk service + Council consume). "
                            + "No Python. Runs always.";
            }
            else if (State == State.Active)     Start();
            else if (State == State.Terminated) Stop();
        }

        private void Start()
        {
            if (_started) return;
            _stopping = false;
            _started  = true;
            Instance  = this;
            try { LoadConfig(); } catch (Exception _sx) { SentinelCore.Swallow("SentinelNews.Start", _sx); }
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch (Exception _sx) { SentinelCore.Swallow("SentinelNews.Start", _sx); }
            // first fetch ~20s after load (let the platform settle), then every RefreshMinutes
            int period = Math.Max(15, RefreshMinutes) * 60 * 1000;
            _timer = new Timer(OnTick, null, 20 * 1000, period);
            try { SentinelCore.Log("News", "SentinelNewsService started — " + (Enabled ? "feeding" : "DISABLED")
                + " (" + Currencies + " ≥ " + MinImpact + ", -" + BeforeMin + "/+" + AfterMin + "m, every " + RefreshMinutes + "m)."); } catch (Exception _sx) { SentinelCore.Swallow("SentinelNews.Start", _sx); }
        }

        private void Stop()
        {
            if (!_started) return;
            _stopping = true;
            _started  = false;
            if (_timer != null)
            {
                try { var done = new ManualResetEvent(false); if (_timer.Dispose(done)) done.WaitOne(500); done.Close(); }
                catch { try { _timer.Dispose(); } catch (Exception _sx) { SentinelCore.Swallow("SentinelNews.Stop", _sx); } }
                _timer = null;
            }
            if (Instance == this) Instance = null;
            try { SentinelCore.Log("News", "SentinelNewsService stopped."); } catch (Exception _sx) { SentinelCore.Swallow("SentinelNews.Stop", _sx); }
        }

        // ── the fetch/write tick (threadpool; never overlaps, never throws) ──
        private void OnTick(object _)
        {
            if (_stopping || !_started || !Enabled) return;
            if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return;
            try
            {
                // backoff: if News.conf was refreshed within MinRefetchMinutes, skip the fetch (a rapid F5/restart
                // storm otherwise fires a fetch each time → the feed's CDN rate-limits with 429). The periodic
                // RefreshMinutes tick is always older than this, so the real cadence is unaffected.
                try
                {
                    string cur = Path.Combine(SentinelCore.SettingsDir, "News.conf");
                    if (MinRefetchMinutes > 0 && File.Exists(cur)
                        && (NinjaTrader.Core.Globals.Now - File.GetLastWriteTime(cur)).TotalMinutes < MinRefetchMinutes)
                    {
                        try { SentinelCore.Log("News", "skip fetch — News.conf refreshed < " + MinRefetchMinutes + "m ago (backoff)."); } catch (Exception _sx) { SentinelCore.Swallow("SentinelNews.OnTick", _sx); }
                        return;
                    }
                }
                catch (Exception _sx) { SentinelCore.Swallow("SentinelNews.OnTick", _sx); }

                string json = Fetch(FeedUrl);
                if (string.IsNullOrEmpty(json)) { Warn("empty response from the calendar feed"); return; }

                List<Ev> events = Parse(json);
                if (events == null) { Warn("could not parse the calendar feed"); return; }
                _lastEvents = events;                                     // cache for instant re-apply on a config change

                var block = BuildBlock(events, out int written);
                MergeIntoNewsConf(block);
                try { SentinelCore.Log("News", "News.conf updated — " + written + " window(s) ("
                    + Currencies + " ≥ " + MinImpact + ", -" + BeforeMin + "/+" + AfterMin + "m, LOCAL)."); } catch (Exception _sx) { SentinelCore.Swallow("SentinelNews.OnTick", _sx); }
            }
            catch (Exception ex) { Warn("fetch/write failed: " + ex.Message); }
            finally { Interlocked.Exchange(ref _running, 0); }
        }

        private void Warn(string why)
        {
            // fail-SAFE: do not touch News.conf; the RiskService freshness guard surfaces the stale window.
            try { SentinelCore.Log("News", "⚠ " + why + " — leaving News.conf unchanged (freshness guard will flag if stale)."); } catch (Exception _sx) { SentinelCore.Swallow("SentinelNews.Warn", _sx); }
        }

        // ── fetch ────────────────────────────────────────────────────────────────
        private string Fetch(string url)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method    = "GET";
                req.Timeout   = 15000;
                req.UserAgent = "SentinelNewsService/1.0 (+NinjaTrader)";
                req.Accept    = "application/json";
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return sr.ReadToEnd();
            }
            catch (Exception ex) { Warn("http error: " + ex.Message); return null; }
        }

        // ── parse (dependency-free: the FF feed is a flat array of flat string-valued objects) ──
        private struct Ev { public DateTimeOffset When; public string Title; public string Country; public string Impact; }

        private static readonly Regex RxTitle   = new Regex("\"title\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"", RegexOptions.Compiled);
        private static readonly Regex RxCountry = new Regex("\"country\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.Compiled);
        private static readonly Regex RxDate    = new Regex("\"date\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.Compiled);
        private static readonly Regex RxImpact  = new Regex("\"impact\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.Compiled);

        private List<Ev> Parse(string json)
        {
            try
            {
                var list = new List<Ev>();
                int lb = json.IndexOf('['), rb = json.LastIndexOf(']');
                if (lb < 0 || rb <= lb) return list;                    // no array → zero events (valid)
                string body = json.Substring(lb + 1, rb - lb - 1);
                foreach (string chunk in SplitObjects(body))
                {
                    var mDate = RxDate.Match(chunk);
                    if (!mDate.Success) continue;
                    DateTimeOffset when;
                    if (!DateTimeOffset.TryParse(mDate.Groups[1].Value, CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind, out when)) continue;
                    var mCty = RxCountry.Match(chunk);
                    var mImp = RxImpact.Match(chunk);
                    var mTit = RxTitle.Match(chunk);
                    list.Add(new Ev
                    {
                        When    = when,
                        Country = mCty.Success ? mCty.Groups[1].Value : "",
                        Impact  = mImp.Success ? mImp.Groups[1].Value : "",
                        Title   = mTit.Success ? Unescape(mTit.Groups[1].Value) : "event"
                    });
                }
                return list;
            }
            catch { return null; }
        }

        // split "a},{b},{c" (array interior) into object chunks; quote-aware so a "}" inside a string
        // value doesn't split a record.
        private static IEnumerable<string> SplitObjects(string body)
        {
            int depth = 0, start = -1; bool inStr = false, esc = false;
            for (int i = 0; i < body.Length; i++)
            {
                char c = body[i];
                if (inStr) { if (esc) esc = false; else if (c == '\\') esc = true; else if (c == '"') inStr = false; continue; }
                if (c == '"') { inStr = true; continue; }
                if (c == '{') { if (depth == 0) start = i; depth++; }
                else if (c == '}') { depth--; if (depth == 0 && start >= 0) { yield return body.Substring(start, i - start + 1); start = -1; } }
            }
        }

        private static string Unescape(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Replace("\\/", "/").Replace("\\\"", "\"").Replace("\\\\", "\\").Replace("|", "/").Trim();
        }

        // ── build the managed block ────────────────────────────────────────────────
        private List<string> BuildBlock(List<Ev> events, out int written)
        {
            written = 0;
            var lines = new List<string>();
            var wanted = new HashSet<string>((Currencies ?? "USD")
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim().ToUpperInvariant()),
                StringComparer.OrdinalIgnoreCase);
            int rm;
            int rankMin = ImpactRank.TryGetValue((MinImpact ?? "HIGH").Trim().ToUpperInvariant(), out rm) ? rm : 2;
            DateTime now = NinjaTrader.Core.Globals.Now;

            lines.Add(BlockStart);
            lines.Add("#   source=ForexFactory  fetched_local=" + now.ToString("yyyy-MM-dd HH:mm")
                + "  currencies=" + string.Join(",", wanted) + "  minImpact=" + (MinImpact ?? "HIGH").ToUpperInvariant());

            foreach (var ev in events.OrderBy(e => e.When))
            {
                if (wanted.Count > 0 && !wanted.Contains(ev.Country ?? "")) continue;
                int r;
                int rank = ImpactRank.TryGetValue((ev.Impact ?? "").Trim().ToUpperInvariant(), out r) ? r : -1;
                if (rank < rankMin) continue;
                DateTime local = ev.When.LocalDateTime;                 // offset-aware → machine-local wall time
                if (local.AddMinutes(AfterMin) < now) continue;         // skip windows already fully in the past
                string name = string.IsNullOrEmpty(ev.Title) ? "event" : ev.Title;
                lines.Add(local.ToString("yyyy-MM-dd HH:mm") + " | " + name + " | all | " + BeforeMin + " | " + AfterMin);
                written++;
            }

            lines.Add("#   " + written + " upcoming window(s)");
            lines.Add(BlockEnd);
            return lines;
        }

        // replace the managed block in News.conf, preserving every other (manual) line
        private void MergeIntoNewsConf(List<string> block)
        {
            string dir  = SentinelCore.SettingsDir;
            string path = Path.Combine(dir, "News.conf");
            var kept = new List<string>();
            if (File.Exists(path))
            {
                bool skipping = false;
                foreach (string ln in File.ReadAllLines(path))
                {
                    string s = (ln ?? "").Trim();
                    if (s == BlockStart) { skipping = true; continue; }
                    if (s == BlockEnd)   { skipping = false; continue; }
                    if (!skipping) kept.Add(ln);
                }
            }
            while (kept.Count > 0 && kept[kept.Count - 1].Trim().Length == 0) kept.RemoveAt(kept.Count - 1);
            if (kept.Count > 0) kept.Add("");
            kept.AddRange(block);

            try { Directory.CreateDirectory(dir); } catch (Exception _sx) { SentinelCore.Swallow("SentinelNews.MergeIntoNewsConf", _sx); }
            File.WriteAllText(path, string.Join(Environment.NewLine, kept) + Environment.NewLine, new UTF8Encoding(false));
        }

        // Re-emit News.conf from the LAST fetched calendar using the CURRENT window config — instant, no network.
        // Called after the dashboard edits the no-trade window so the change applies without waiting for a re-fetch.
        public void RewriteFromCache()
        {
            try
            {
                var ev = _lastEvents;
                if (ev == null) { try { SentinelCore.Log("News", "config saved — no cached calendar yet; the new window applies on the next fetch."); } catch (Exception _sx) { SentinelCore.Swallow("SentinelNews.RewriteFromCache", _sx); } return; }
                int written;
                var block = BuildBlock(ev, out written);
                MergeIntoNewsConf(block);
                try { SentinelCore.Log("News", "News.conf rewritten — " + written + " window(s) (-" + BeforeMin + "/+" + AfterMin + "m, config change)."); } catch (Exception _sx) { SentinelCore.Swallow("SentinelNews.RewriteFromCache", _sx); }
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelNews.RewriteFromCache", _sx); }
        }

        // Persist the current static config to NewsService.conf (so a dashboard edit survives restart).
        public static void SaveConfig()
        {
            try
            {
                string path = Path.Combine(SentinelCore.SettingsDir, "NewsService.conf");
                var sb = new StringBuilder();
                sb.AppendLine("# SentinelNewsService config — editable here or from the dashboard Home tab.");
                sb.AppendLine("enabled=" + (Enabled ? "true" : "false"));
                sb.AppendLine("minImpact=" + MinImpact);
                sb.AppendLine("currencies=" + Currencies);
                sb.AppendLine("beforeMin=" + BeforeMin);
                sb.AppendLine("afterMin=" + AfterMin);
                sb.AppendLine("refreshMinutes=" + RefreshMinutes);
                sb.AppendLine("minRefetchMinutes=" + MinRefetchMinutes);
                try { Directory.CreateDirectory(SentinelCore.SettingsDir); } catch (Exception _sx) { SentinelCore.Swallow("SentinelNews.SaveConfig", _sx); }
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelNews.SaveConfig", _sx); }
        }

        // ── optional NewsService.conf overrides ─────────────────────────────────────
        private void LoadConfig()
        {
            string path = Path.Combine(SentinelCore.SettingsDir, "NewsService.conf");
            if (!File.Exists(path)) return;
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = (raw ?? "").Trim();
                if (line.Length == 0 || line[0] == '#' || line.StartsWith("//")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string k = line.Substring(0, eq).Trim().ToLowerInvariant();
                string v = line.Substring(eq + 1).Trim();
                int iv; bool bv;
                switch (k)
                {
                    case "enabled":        if (bool.TryParse(v, out bv)) Enabled = bv; break;
                    case "minimpact":      if (v.Length > 0) MinImpact = v.ToUpperInvariant(); break;
                    case "currencies":     if (v.Length > 0) Currencies = v.ToUpperInvariant(); break;
                    case "beforemin":      if (int.TryParse(v, out iv) && iv >= 0) BeforeMin = iv; break;
                    case "aftermin":       if (int.TryParse(v, out iv) && iv >= 0) AfterMin = iv; break;
                    case "refreshminutes": if (int.TryParse(v, out iv) && iv >= 15) RefreshMinutes = iv; break;
                    case "minrefetchminutes": if (int.TryParse(v, out iv) && iv >= 0) MinRefetchMinutes = iv; break;
                    case "url":            if (v.Length > 0) FeedUrl = v; break;
                }
            }
        }
    }
}
