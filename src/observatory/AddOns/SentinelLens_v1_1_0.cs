// ─────────────────────────────────────────────────────────────────────────────
// This Source Code Form is subject to the terms of the Mozilla Public License,
// v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
// one at https://mozilla.org/MPL/2.0/.
//
// Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
// ─────────────────────────────────────────────────────────────────────────────
// ═════════════════════════════════════════════════════════════════════════════
//  SentinelLens — trade analytics over the Sentinel Log JSONL (NT8, Sentinel Suite)
//  File: SentinelLens_v1_1_0.cs
//  Version: v1.1.0
// ─────────────────────────────────────────────────────────────────────────────
//  WHAT THIS IS  (see memory: sentinel-log-integration, sentinel-eye-tool, profit-plan-and-accounts)
//    The suite's ANALYTICS layer. It reads the MAE/MFE trade records that Sentinel Log
//    writes to <UserDataDir>\Sentinel\Log\*.jsonl and aggregates them into win rate,
//    profit factor, net/avg ticks, average heat (MAE), average favorable (MFE), and
//    MFE-capture efficiency — overall and broken down by strategy and instrument.
//
//    v1.1.0 adds the EYE-PARTITION analysis — the profit keystone's payoff. Sentinel Log
//    schema 1.1 stamps each trade with the SentinelEye verdict that stood at entry
//    (eyeHad/eyeDir/eyeScore/eyeAligned/eyeAgeSec). Lens now partitions trades by that
//    verdict and answers the one question the whole suite rests on:
//        Do Eye-ENDORSED trades out-earn the rest?  (i.e. does the Eye filter add edge?)
//    Two views: (1) endorsement partition — Endorsed / NotEndorsed / NoVerdict; and
//    (2) a score-band curve (0-20 … 80-100) so we can SEE where expectancy turns positive
//    and set the qualify threshold on evidence, not a guess. Plus a plain-English verdict.
//
//    UNLIKE Copy/Log/Risk, Lens is NOT an always-on AddOnBase service — it's a read-only,
//    on-demand analyzer (a static class the dashboard's "Lens" tab calls on a button click).
//
//  PARSING: targeted field extraction (no JSON library dependency). We only pull the top-level
//    scalar fields we need per record and IGNORE the nested "path":[…] array entirely, so a
//    hand parser is safe against the known, self-produced schema. Fields (SentinelLogEngine):
//    account, strategy, inst, dir, qty, tier, pnlTicks, maeTicksRaw, mfeTicksRaw, exitReason,
//    + (schema 1.1) eyeHad, eyeDir, eyeScore, eyeSource, eyeAgeSec, eyeAligned.
//
//  CHANGELOG
//    v1.1.0 — EYE-PARTITION analysis. Trade gains eye fields; Summary gains ByEye (Endorsed/
//             NotEndorsed/NoVerdict), ByEyeScoreBand (20-wide bands over trades with a verdict),
//             and EyeVerdict (human-readable edge conclusion, expectancy-based). Back-compatible:
//             schema-1.0 records (no eye block) fall into NoVerdict. New file; v1_0_0 frozen.
//    v1.0.0 — initial: LoadSummary() reads all Sentinel\Log\*.jsonl, parses records,
//             aggregates Overall + ByStrategy + ByInstrument. Defensive (skips unparseable lines).
// ═════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace NinjaTrader.NinjaScript.AddOns.Sentinel
{
    public static class SentinelLens_v1_1_0
    {
        // minimum Eye-endorsed trades before the filter's edge can be judged with a straight face
        private const int EyeVerdictMinN = 10;

        public sealed class Trade
        {
            public string Account, Strategy, Instrument, ExitReason;
            public int    Dir, Qty, Tier;
            public double PnlTicks, MaeTicks, MfeTicks;
            // schema 1.1 — Eye verdict frozen at entry
            public bool   EyeHad, EyeAligned;
            public int    EyeDir;
            public double EyeScore, EyeAgeSec;
        }

        // one aggregation bucket (overall, or per strategy/instrument/eye-category/score-band)
        public sealed class Group
        {
            public string Key = "?";
            public int    Trades, Wins, Losses, Scratch;
            public double NetTicks, GrossWinTicks, GrossLossTicks;  // GrossLossTicks is negative
            public double SumMae, SumMfe, SumMfeCapture;

            public double WinRate      { get { return Trades > 0 ? 100.0 * Wins / Trades : 0; } }
            public double ProfitFactor { get { return GrossLossTicks != 0 ? GrossWinTicks / Math.Abs(GrossLossTicks) : (GrossWinTicks > 0 ? double.PositiveInfinity : 0); } }
            public double AvgMae       { get { return Trades > 0 ? SumMae / Trades : 0; } }
            public double AvgMfe       { get { return Trades > 0 ? SumMfe / Trades : 0; } }
            public double AvgNet       { get { return Trades > 0 ? NetTicks / Trades : 0; } }
            public double MfeCapturePct{ get { return Trades > 0 ? 100.0 * SumMfeCapture / Trades : 0; } }
        }

        public sealed class Summary
        {
            public int          FilesRead, TradesParsed, TierSkipped;
            public Group        Overall = new Group { Key = "ALL" };
            public List<Group>  ByStrategy   = new List<Group>();
            public List<Group>  ByInstrument = new List<Group>();
            // ── Eye-partition analysis (v1.1.0) ──
            public List<Group>  ByEye          = new List<Group>();  // Endorsed / NotEndorsed / NoVerdict
            public List<Group>  ByEyeScoreBand = new List<Group>();  // 00-20 … 80-100 (trades with a verdict)
            public int          EyeCoverage;                          // # trades that had a verdict
            public string       EyeVerdict;                           // plain-English edge conclusion
            public string       Error;
            public string       LogDir;
        }

        public static string LogDir { get { return Path.Combine(SentinelCore.SettingsDir, "Log"); } }

        public static Summary LoadSummary()
        {
            var sum = new Summary { LogDir = LogDir };
            try
            {
                if (!Directory.Exists(sum.LogDir)) { sum.Error = "no Sentinel\\Log directory yet (no trades captured)"; return sum; }

                var byStrat = new Dictionary<string, Group>(StringComparer.OrdinalIgnoreCase);
                var byInst  = new Dictionary<string, Group>(StringComparer.OrdinalIgnoreCase);
                var byEye   = new Dictionary<string, Group>(StringComparer.OrdinalIgnoreCase);
                var byBand  = new Dictionary<string, Group>(StringComparer.OrdinalIgnoreCase);

                string[] files;
                try { files = Directory.GetFiles(sum.LogDir, "*.jsonl"); } catch { files = new string[0]; }

                foreach (string file in files)
                {
                    sum.FilesRead++;
                    string[] lines;
                    try { lines = File.ReadAllLines(file); } catch { continue; }
                    foreach (string line in lines)
                    {
                        Trade t = ParseTrade(line);
                        if (t == null) continue;
                        // Dedup: a strategy trade produces BOTH a tier-1 zero-touch record AND a
                        // tier-2 strategy record for the same fill. Analyze tier-1 only — the uniform
                        // single source of truth (captures every fill, manual or strategy, with the
                        // same entry-time eye tags) — so each trade is counted exactly once.
                        if (t.Tier != 1) { sum.TierSkipped++; continue; }
                        sum.TradesParsed++;
                        Accum(sum.Overall, t);
                        Accum(GetGroup(byStrat, t.Strategy), t);
                        Accum(GetGroup(byInst, t.Instrument), t);

                        // Eye endorsement partition (the money question)
                        string eyeCat = !t.EyeHad ? "NoVerdict" : (t.EyeAligned ? "Endorsed" : "NotEndorsed");
                        Accum(GetGroup(byEye, eyeCat), t);

                        // Score-band curve — only trades that actually had a verdict
                        if (t.EyeHad)
                        {
                            sum.EyeCoverage++;
                            Accum(GetGroup(byBand, ScoreBand(t.EyeScore)), t);
                        }
                    }
                }

                sum.ByStrategy   = byStrat.Values.OrderByDescending(g => g.Trades).ToList();
                sum.ByInstrument = byInst.Values.OrderByDescending(g => g.Trades).ToList();
                sum.ByEye          = OrderEye(byEye);
                sum.ByEyeScoreBand = byBand.Values.OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
                sum.EyeVerdict     = BuildEyeVerdict(byEye);
            }
            catch (Exception ex) { sum.Error = ex.Message; }
            return sum;
        }

        // ── Eye verdict: does the filter add edge? (expectancy-based, sample-honest) ──
        private static string BuildEyeVerdict(Dictionary<string, Group> byEye)
        {
            Group end, not;
            byEye.TryGetValue("Endorsed", out end);
            byEye.TryGetValue("NotEndorsed", out not);

            int endN = end != null ? end.Trades : 0;
            if (endN < EyeVerdictMinN)
                return string.Format(
                    "Not enough Eye-ENDORSED trades yet ({0}/{1}). The filter can't be judged until Eye " +
                    "qualifies signals (score ≥ threshold, direction matching). Keep the sample running — " +
                    "meanwhile the score-band curve below hints where expectancy turns positive.",
                    endN, EyeVerdictMinN);

            double endAvg = end.AvgNet;
            double notAvg = not != null ? not.AvgNet : 0;
            double delta  = endAvg - notAvg;
            string headline = delta > 0
                ? string.Format("✓ Eye ADDS EDGE: endorsed trades earn {0:+0.0;-0.0} ticks/trade MORE than the rest.", delta)
                : string.Format("✗ Eye shows NO edge yet: endorsed trades earn {0:+0.0;-0.0} ticks/trade vs the rest — retune Eye or the qualify threshold.", delta);

            return string.Format(
                "{0}\n   Endorsed:     {1} trades | win {2:0.0}% | PF {3} | {4:+0.00;-0.00} t/trade\n" +
                "   Not-endorsed: {5} trades | win {6:0.0}% | PF {7} | {8:+0.00;-0.00} t/trade",
                headline,
                endN, end.WinRate, Pf(end.ProfitFactor), endAvg,
                (not != null ? not.Trades : 0), (not != null ? not.WinRate : 0), Pf(not != null ? not.ProfitFactor : 0), notAvg);
        }

        private static string Pf(double pf)
        {
            if (double.IsPositiveInfinity(pf)) return "∞";
            return pf.ToString("0.00", CultureInfo.InvariantCulture);
        }

        // 20-wide score band label, e.g. 30.18 -> "40-60"? no: floor to band. 30.18 -> "20-40".
        private static string ScoreBand(double score)
        {
            if (double.IsNaN(score)) return "n/a";
            int lo = (int)(Math.Floor(score / 20.0) * 20);
            if (lo < 0)  lo = 0;
            if (lo > 80) lo = 80;
            return string.Format("{0:00}-{1:00}", lo, lo + 20);
        }

        // Endorsed first, then NotEndorsed, then NoVerdict (stable, meaningful reading order)
        private static List<Group> OrderEye(Dictionary<string, Group> byEye)
        {
            var order = new[] { "Endorsed", "NotEndorsed", "NoVerdict" };
            var outp = new List<Group>();
            foreach (var k in order) { Group g; if (byEye.TryGetValue(k, out g)) outp.Add(g); }
            return outp;
        }

        // ── aggregation ──────────────────────────────────────────────────────────
        private static Group GetGroup(Dictionary<string, Group> map, string key)
        {
            key = string.IsNullOrEmpty(key) ? "?" : key;
            Group g;
            if (!map.TryGetValue(key, out g)) { g = new Group { Key = key }; map[key] = g; }
            return g;
        }

        private static void Accum(Group g, Trade t)
        {
            g.Trades++;
            g.NetTicks += t.PnlTicks;
            g.SumMae   += t.MaeTicks;
            g.SumMfe   += t.MfeTicks;
            // MFE-capture: how much of the favorable excursion the exit kept (0..1, clamped)
            if (t.MfeTicks > 0)
            {
                double cap = t.PnlTicks / t.MfeTicks;
                if (cap < 0) cap = 0; if (cap > 1) cap = 1;
                g.SumMfeCapture += cap;
            }
            if (t.PnlTicks > 0)      { g.Wins++;   g.GrossWinTicks  += t.PnlTicks; }
            else if (t.PnlTicks < 0) { g.Losses++; g.GrossLossTicks += t.PnlTicks; }
            else                       g.Scratch++;
        }

        // ── targeted JSONL field extraction (ignores the nested path[] array) ─────
        private static Trade ParseTrade(string line)
        {
            if (string.IsNullOrEmpty(line) || line.IndexOf("\"schema\"", StringComparison.Ordinal) < 0) return null;
            var t = new Trade
            {
                Account    = GetStr(line, "account"),
                Strategy   = GetStr(line, "strategy"),
                Instrument = GetStr(line, "inst"),
                ExitReason = GetStr(line, "exitReason"),
                Dir        = (int)GetNum(line, "dir"),
                Qty        = (int)GetNum(line, "qty"),
                Tier       = (int)GetNum(line, "tier"),
                PnlTicks   = GetNum(line, "pnlTicks"),
                MaeTicks   = GetNum(line, "maeTicksRaw"),
                MfeTicks   = GetNum(line, "mfeTicksRaw"),
                // schema 1.1 eye block (absent on 1.0 records => EyeHad=false => NoVerdict)
                EyeHad     = GetBool(line, "eyeHad"),
                EyeAligned = GetBool(line, "eyeAligned"),
                EyeDir     = (int)GetNum(line, "eyeDir"),
                EyeScore   = GetNum(line, "eyeScore"),
                EyeAgeSec  = GetNum(line, "eyeAgeSec")
            };
            return t;
        }

        private static double GetNum(string line, string key)
        {
            string tok = "\"" + key + "\":";
            int i = line.IndexOf(tok, StringComparison.Ordinal);
            if (i < 0) return 0;
            i += tok.Length;
            while (i < line.Length && line[i] == ' ') i++;
            int j = i;
            while (j < line.Length)
            {
                char c = line[j];
                if (char.IsDigit(c) || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E') j++;
                else break;
            }
            double v;
            return double.TryParse(line.Substring(i, j - i), NumberStyles.Any, CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        // true only for a literal JSON  "key":true  ; false for false/null/absent
        private static bool GetBool(string line, string key)
        {
            string tok = "\"" + key + "\":";
            int i = line.IndexOf(tok, StringComparison.Ordinal);
            if (i < 0) return false;
            i += tok.Length;
            while (i < line.Length && line[i] == ' ') i++;
            return i < line.Length && line[i] == 't';   // "true"
        }

        private static string GetStr(string line, string key)
        {
            string tok = "\"" + key + "\":";
            int i = line.IndexOf(tok, StringComparison.Ordinal);
            if (i < 0) return "?";
            i += tok.Length;
            while (i < line.Length && line[i] == ' ') i++;
            if (i >= line.Length || line[i] != '"') return "?";   // null / non-string
            i++;
            int j = line.IndexOf('"', i);
            if (j < 0) return "?";
            return line.Substring(i, j - i);
        }
    }
}
