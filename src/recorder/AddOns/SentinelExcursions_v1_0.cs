// ─────────────────────────────────────────────────────────────────────────────
// This Source Code Form is subject to the terms of the Mozilla Public License,
// v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
// one at https://mozilla.org/MPL/2.0/.
//
// Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
// ─────────────────────────────────────────────────────────────────────────────
// ═════════════════════════════════════════════════════════════════════════════
//  SentinelExcursions — signal-excursion analytics for the Sentinel Suite (NT8)
//  File: SentinelExcursions_v1_0.cs   ·   Version v1.0
// ─────────────────────────────────────────────────────────────────────────────
//  WHAT  (pairs with Indicators\SignalExcursionRecorder_v1_0.cs; see sentinel-roadmap)
//    Reads the RAW signal-excursion records the Recorder writes to <UserDataDir>\Sentinel\
//    Excursions\*.jsonl (schema 1.0, "kind":"excursion") and turns them into the actionable
//    truth: per (instrument × signal × direction), the DISTRIBUTIONS of max MFE / max MAE and
//    the fixed-horizon milestone curves (1/5/15/60 min), as percentiles. That directly answers
//    "what's a responsible base-hit TP (an MFE percentile) and stop (an MAE percentile) for this
//    signal?" — uncontaminated by execution (the Recorder takes no orders).
//
//    Static, on-demand (the dashboard Excursion tab calls it on a button click) — like Lens.
//    Targeted JSONL field extraction; null milestones (signal didn't reach that horizon) are
//    EXCLUDED from the percentile (not treated as 0).
//
//  CHANGELOG
//    v1.0.5 — COUNCIL support (pairs with SentinelExcursionRecorder_v1_4, schema 1.2): schema 1.2 already
//             passes the 1.0-only filter, so the "COUNCIL" signal group appears automatically with the full
//             metric set. NEW: ByConviction partition (LOW/MID/HIGH buckets from convBucket) + CouncilCount +
//             ConvictionVerdictCode (+1 HIGH-conviction fires out-earn LOW at 15m / -1 worse / 0 inconclusive)
//             — the "does higher conviction actually pay?" referee for the dashboard + the Bridge floor.
//    v1.0.4 — (REMOVED 2026-08-11) a per-signal referee getter (+1 adds edge / -1 hurts / 0 inconclusive) — the shared
//             Eye-referee verdict used by both the dashboard ④ section and the State writer's eye block.
//    v1.0.3 — FIRE-RATE: Group tracks distinct fire dates (FireDates) → FiresPerDay = N/days, so the
//             dashboard can show "signals/day" (a +EV signal that fires twice a month isn't a business).
//    v1.0.2 — VIZ SUPPORT (for the dashboard Excursion visuals): Group.Compute now also computes
//             MaeMed5/MaeMed60 (median adverse at 5/60 min) for the growth-line plot; new public
//             TpStopGrid(pts) returns ALL 12 TP/stop configs' estimates (the expectancy-curve viz;
//             BestTpStop is the max-Exp of these). Pctl is already public (scatter axis scaling).
//    v1.0.1 — CORRECTNESS: (a) DEDUPE — the Recorder rewrites its FULL history to a NEW per-load file
//             on every F5/re-add, so the same signal fire appeared in many files and was counted many
//             times (seen ~9× inflation: 36,668 lines → 4,177 unique). Now keyed by
//             inst|bartype|signal|dir|fireTime across all files, so each fire counts once. (b) drop the
//             legacy schema-1.0 recorder output (no regime; superseded by 1.1). Summary gains Deduped +
//             SchemaSkipped counts (surfaced in the dashboard status). No change to the math/percentiles.
//    v1.0 — initial. Group by inst×signal×dir; percentiles of maxMFE/maxMAE + mfe/mae@5/15/60.
// ═════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace NinjaTrader.NinjaScript.AddOns.Sentinel
{
    public static class SentinelExcursions_v1_0
    {
        public sealed class Group
        {
            public string Instrument, Signal;
            public int    Dir;              // +1 / -1
            public int    N;
            // collected (ticks); nulls excluded
            public readonly List<double> MaxMFE = new List<double>();
            public readonly List<double> MaxMAE = new List<double>();
            public readonly List<double> Mfe5 = new List<double>(), Mfe15 = new List<double>(), Mfe60 = new List<double>();
            public readonly List<double> Mae5 = new List<double>(), Mae15 = new List<double>(), Mae60 = new List<double>();

            // computed for display (medians + a spread + tail)
            public double MfeMed5, MfeMed15, MfeMed60;
            public double Mfe15P75;
            public double MaeMed5, MaeMed15, MaeMed60;   // v1.0.2: 5/60 added for the growth-line viz
            public double Mae15P75;
            public double MaxMaeP90, MaxMfeMed;

            // v1.1 partitions (each Sub holds mfe15/mae15 at 15m — the edge lens)
            public readonly Dictionary<string, Sub> ByRegime = new Dictionary<string, Sub>(StringComparer.OrdinalIgnoreCase);
            // v1.4: council conviction buckets (LOW/MID/HIGH) — only council records populate this
            public readonly Dictionary<string, Sub> ByConviction = new Dictionary<string, Sub>(StringComparer.OrdinalIgnoreCase);
            public readonly List<Pt> Pts = new List<Pt>();
            public int CouncilCount;   // v1.4: # of COUNCIL fires in this group
            public TpStop Best { get { return BestTpStop(Pts); } }

            // v1.0.3: fire-rate — distinct calendar dates this signal fired on → signals/day
            public readonly HashSet<string> FireDates = new HashSet<string>(StringComparer.Ordinal);
            public double FiresPerDay;   // N / distinct-days (a +EV signal firing twice a month isn't a business)

            public string Key { get { return Instrument + "·" + Signal + "·" + (Dir > 0 ? "L" : "S"); } }
            public bool   HasEdge { get { return MfeMed15 > MaeMed15; } }   // favorable > adverse at 15m


            // v1.4: conviction referee — +1 = HIGH-conviction council fires out-earn LOW at 15m by ≥3 ticks
            // (conviction is paying → gate the Bridge higher), -1 = HIGH does WORSE (conviction inverts —
            // investigate the weights), 0 = inconclusive/insufficient (need HIGH n≥10 & LOW n≥5).
            public int ConvictionVerdictCode
            {
                get
                {
                    if (CouncilCount == 0) return 0;
                    Sub hi, lo;
                    if (!ByConviction.TryGetValue("HIGH", out hi) || hi == null || hi.N < 10) return 0;
                    if (!ByConviction.TryGetValue("LOW",  out lo) || lo == null || lo.N < 5)  return 0;
                    double delta = (hi.MfeMed15 - hi.MaeMed15) - (lo.MfeMed15 - lo.MaeMed15);
                    return delta >= 3 ? 1 : (delta <= -3 ? -1 : 0);
                }
            }

            public void Compute()
            {
                N        = MaxMFE.Count;
                MfeMed5  = Pctl(Mfe5, 50);   MfeMed15 = Pctl(Mfe15, 50);  MfeMed60 = Pctl(Mfe60, 50);
                Mfe15P75 = Pctl(Mfe15, 75);
                MaeMed5  = Pctl(Mae5, 50);   MaeMed15 = Pctl(Mae15, 50);  MaeMed60 = Pctl(Mae60, 50);
                Mae15P75 = Pctl(Mae15, 75);
                FiresPerDay = FireDates.Count > 0 ? (double)N / FireDates.Count : 0;
                MaxMaeP90 = Pctl(MaxMAE, 90); MaxMfeMed = Pctl(MaxMFE, 50);
            }
        }

        // one record's 15-min excursion + the times its EOD max-MFE/MAE occurred (order proxy)
        public sealed class Pt { public double Mfe15, Mae15, MsMFE, MsMAE; }

        // a TP/stop recommendation (both in ticks) + its ESTIMATED outcome on the 15m window
        public sealed class TpStop
        {
            public double Tp, Stop, HitRate, Exp;   // Exp = est. ticks/trade
            public int    N;
            public bool   Ok { get { return N >= 15; } }
        }

        // a partition bucket (by regime, or by eye-endorsement) — the edge lens at 15m
        public sealed class Sub
        {
            public string Name;
            public readonly List<double> Mfe15 = new List<double>();
            public readonly List<double> Mae15 = new List<double>();
            public readonly List<Pt>     Pts   = new List<Pt>();
            public int    N        { get { return Mfe15.Count; } }
            public double MfeMed15 { get { return Pctl(Mfe15, 50); } }
            public double MaeMed15 { get { return Pctl(Mae15, 50); } }
            public bool   HasEdge  { get { return MfeMed15 > MaeMed15; } }
            public TpStop Best     { get { return BestTpStop(Pts); } }
        }

        public sealed class Summary
        {
            public int          FilesRead, Records;
            public int          Deduped, SchemaSkipped;   // v1.0.1: dropped duplicate fires / legacy schema-1.0 lines
            public List<Group>  Groups = new List<Group>();
            public string       Error, Dir;
        }

        public static string ExcDir { get { return Path.Combine(SentinelCore.SettingsDir, "Excursions"); } }

        public static Summary LoadSummary()
        {
            var sum = new Summary { Dir = ExcDir };
            try
            {
                if (!Directory.Exists(sum.Dir)) { sum.Error = "no Sentinel\\Excursions folder yet (recorder not run)"; return sum; }
                string[] files;
                try { files = Directory.GetFiles(sum.Dir, "*.jsonl"); } catch { files = new string[0]; }

                var map = new Dictionary<string, Group>(StringComparer.OrdinalIgnoreCase);
                // v1.0.1: dedupe fires ACROSS files — the Recorder rewrites full history to a new file on
                // every load, so one signal fire recurs in many files. Key by identity so it counts once.
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (string file in files)
                {
                    sum.FilesRead++;
                    string[] lines;
                    try { lines = File.ReadAllLines(file); } catch { continue; }
                    foreach (string line in lines)
                    {
                        if (string.IsNullOrEmpty(line) || line.IndexOf("\"kind\":\"excursion\"", StringComparison.Ordinal) < 0) continue;
                        // v1.0.1: drop legacy schema-1.0 output (no regime; superseded by 1.1)
                        if (GetStr(line, "schema") == "1.0") { sum.SchemaSkipped++; continue; }
                        string inst = GetStr(line, "inst");
                        string sig  = GetStr(line, "signal");
                        int dir     = (int)GetNumOr(line, "dir", 0);
                        if (inst == "?" || sig == "?" || dir == 0) continue;
                        // v1.0.1: dedupe by fire identity (inst|bartype|signal|dir|fireTime)
                        string ft = GetStr(line, "fireTime");
                        if (!seen.Add(inst + "|" + GetStr(line, "bartype") + "|" + sig + "|" + dir + "|" + ft))
                        { sum.Deduped++; continue; }

                        string key = inst + "|" + sig + "|" + dir;
                        Group g;
                        if (!map.TryGetValue(key, out g)) { g = new Group { Instrument = inst, Signal = sig, Dir = dir }; map[key] = g; }
                        if (ft != null && ft.Length >= 10) g.FireDates.Add(ft.Substring(0, 10));   // "yyyy-MM-dd" → fire-rate

                        double mfe15 = GetNum(line, "mfe15"), mae15 = GetNum(line, "mae15");
                        Add(g.MaxMFE, GetNum(line, "maxMFE"));
                        Add(g.MaxMAE, GetNum(line, "maxMAE"));
                        Add(g.Mfe5,  GetNum(line, "mfe5"));   Add(g.Mfe15, mfe15); Add(g.Mfe60, GetNum(line, "mfe60"));
                        Add(g.Mae5,  GetNum(line, "mae5"));   Add(g.Mae15, mae15); Add(g.Mae60, GetNum(line, "mae60"));

                        // paired point for the TP/stop simulator (needs mfe15 + mae15 both present)
                        Pt pt = null;
                        if (!double.IsNaN(mfe15) && !double.IsNaN(mae15))
                            pt = new Pt { Mfe15 = mfe15, Mae15 = mae15, MsMFE = GetNum(line, "msToMFE"), MsMAE = GetNum(line, "msToMAE") };
                        if (pt != null) g.Pts.Add(pt);

                        // v1.1 partitions (by regime)
                        var rs = GetSub(g.ByRegime, GetStr(line, "regime"));
                        if (!double.IsNaN(mfe15)) rs.Mfe15.Add(mfe15);
                        if (!double.IsNaN(mae15)) rs.Mae15.Add(mae15);
                        if (pt != null) rs.Pts.Add(pt);
                        // v1.4: council conviction partition (only COUNCIL records carry council=true + convBucket)
                        if (GetBool(line, "council"))
                        {
                            g.CouncilCount++;
                            string cb = GetStr(line, "convBucket");
                            if (cb != "?" && !string.IsNullOrEmpty(cb))
                            {
                                var cs = GetSub(g.ByConviction, cb);
                                if (!double.IsNaN(mfe15)) cs.Mfe15.Add(mfe15);
                                if (!double.IsNaN(mae15)) cs.Mae15.Add(mae15);
                                if (pt != null) cs.Pts.Add(pt);
                            }
                        }
                        sum.Records++;
                    }
                }

                foreach (var g in map.Values) g.Compute();
                // most data first, then best 15-min favorable
                sum.Groups = map.Values.OrderBy(g => g.Instrument, StringComparer.OrdinalIgnoreCase)
                                        .ThenByDescending(g => g.MfeMed15).ToList();
            }
            catch (Exception ex) { sum.Error = ex.Message; }
            return sum;
        }

        private static void Add(List<double> l, double v) { if (!double.IsNaN(v) && !double.IsInfinity(v)) l.Add(v); }

        // linear-interpolated percentile (p in 0..100); NaN if empty
        public static double Pctl(List<double> v, double p)
        {
            if (v == null || v.Count == 0) return double.NaN;
            var s = new List<double>(v); s.Sort();
            if (s.Count == 1) return s[0];
            double idx = (p / 100.0) * (s.Count - 1);
            int lo = (int)Math.Floor(idx), hi = (int)Math.Ceiling(idx);
            if (lo == hi) return s[lo];
            return s[lo] + (s[hi] - s[lo]) * (idx - lo);
        }

        private static Sub GetSub(Dictionary<string, Sub> d, string key)
        {
            key = string.IsNullOrEmpty(key) ? "?" : key;
            Sub s;
            if (!d.TryGetValue(key, out s)) { s = new Sub { Name = key }; d[key] = s; }
            return s;
        }

        private static bool GetBool(string line, string key)
        {
            string tok = "\"" + key + "\":";
            int i = line.IndexOf(tok, StringComparison.Ordinal);
            if (i < 0) return false;
            i += tok.Length;
            while (i < line.Length && line[i] == ' ') i++;
            return i < line.Length && line[i] == 't';   // "true"
        }

        // Estimate the best TP/stop over a small grid: TP = 33/50/67th-pct MFE15; stop = 0.5/0.75/1.0/1.5×TP
        // (a FRACTION of the TP, so R:R is structurally capped — the sim can't farm a pathologically wide
        // stop to fake positive expectancy on an edgeless signal). Per record over the 15-min window: TP hit
        // if mfe15≥TP, stop hit if mae15≥stop; if BOTH, the EOD max-times are a crude which-came-first
        // tiebreak (default pessimistic = stop first); neither = scratch (0). It's an ESTIMATE, NOT a
        // path-level backtest — the real number comes from applying these TP/stops in the execution Log.
        public static TpStop BestTpStop(List<Pt> pts)
        {
            if (pts == null || pts.Count < 15) return new TpStop { N = pts == null ? 0 : pts.Count };
            var mfe = new List<double>(pts.Count);
            foreach (var p in pts) mfe.Add(p.Mfe15);

            double[] tpPcts = { 33, 50, 67 };
            double[] slFrac = { 0.5, 0.75, 1.0, 1.5 };   // stop as a FRACTION of TP → R:R capped (no wide-stop farming)
            TpStop best = null;
            foreach (double tpP in tpPcts)
            {
                double tp = Pctl(mfe, tpP);
                if (double.IsNaN(tp) || tp <= 0) continue;
                foreach (double f in slFrac)
                {
                    double sl = tp * f;
                    int wins = 0, n = 0; double pnl = 0;
                    foreach (var p in pts)
                    {
                        if (double.IsNaN(p.Mfe15) || double.IsNaN(p.Mae15)) continue;
                        n++;
                        bool hitTp = p.Mfe15 >= tp, hitSl = p.Mae15 >= sl;
                        if (hitTp && !hitSl) { wins++; pnl += tp; }
                        else if (hitSl && !hitTp) { pnl -= sl; }
                        else if (hitTp && hitSl)
                        {
                            if (!double.IsNaN(p.MsMFE) && !double.IsNaN(p.MsMAE) && p.MsMFE <= p.MsMAE) { wins++; pnl += tp; }
                            else { pnl -= sl; }   // default pessimistic: stop first
                        }
                        // neither → scratch (0)
                    }
                    if (n == 0) continue;
                    double exp = pnl / n;
                    if (best == null || exp > best.Exp)
                        best = new TpStop { Tp = tp, Stop = sl, HitRate = (double)wins / n, Exp = exp, N = n };
                }
            }
            return best ?? new TpStop { N = pts.Count };
        }

        // The FULL TP/stop grid (all 12 configs), for the dashboard's expectancy viz. Same rules as
        // BestTpStop; returns every config's estimate (empty if n<15). BestTpStop = the max-Exp of these.
        public static List<TpStop> TpStopGrid(List<Pt> pts)
        {
            var outl = new List<TpStop>();
            if (pts == null || pts.Count < 15) return outl;
            var mfe = new List<double>(pts.Count);
            foreach (var p in pts) mfe.Add(p.Mfe15);
            double[] tpPcts = { 33, 50, 67 };
            double[] slFrac = { 0.5, 0.75, 1.0, 1.5 };
            foreach (double tpP in tpPcts)
            {
                double tp = Pctl(mfe, tpP);
                if (double.IsNaN(tp) || tp <= 0) continue;
                foreach (double f in slFrac)
                {
                    double sl = tp * f;
                    int wins = 0, n = 0; double pnl = 0;
                    foreach (var p in pts)
                    {
                        if (double.IsNaN(p.Mfe15) || double.IsNaN(p.Mae15)) continue;
                        n++;
                        bool hitTp = p.Mfe15 >= tp, hitSl = p.Mae15 >= sl;
                        if (hitTp && !hitSl) { wins++; pnl += tp; }
                        else if (hitSl && !hitTp) { pnl -= sl; }
                        else if (hitTp && hitSl)
                        {
                            if (!double.IsNaN(p.MsMFE) && !double.IsNaN(p.MsMAE) && p.MsMFE <= p.MsMAE) { wins++; pnl += tp; }
                            else { pnl -= sl; }
                        }
                    }
                    if (n == 0) continue;
                    outl.Add(new TpStop { Tp = tp, Stop = sl, HitRate = (double)wins / n, Exp = pnl / n, N = n });
                }
            }
            return outl;
        }

        // ── targeted JSONL extraction (null → NaN, so it's excluded from percentiles) ──
        private static double GetNum(string line, string key)
        {
            string tok = "\"" + key + "\":";
            int i = line.IndexOf(tok, StringComparison.Ordinal);
            if (i < 0) return double.NaN;
            i += tok.Length;
            while (i < line.Length && line[i] == ' ') i++;
            if (i + 4 <= line.Length && line.Substring(i, 4) == "null") return double.NaN;
            int j = i;
            while (j < line.Length)
            {
                char c = line[j];
                if (char.IsDigit(c) || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E') j++;
                else break;
            }
            double v;
            return double.TryParse(line.Substring(i, j - i), NumberStyles.Any, CultureInfo.InvariantCulture, out v) ? v : double.NaN;
        }

        private static double GetNumOr(string line, string key, double fallback)
        {
            double v = GetNum(line, key);
            return double.IsNaN(v) ? fallback : v;
        }

        private static string GetStr(string line, string key)
        {
            string tok = "\"" + key + "\":";
            int i = line.IndexOf(tok, StringComparison.Ordinal);
            if (i < 0) return "?";
            i += tok.Length;
            while (i < line.Length && line[i] == ' ') i++;
            if (i >= line.Length || line[i] != '"') return "?";
            i++;
            int j = line.IndexOf('"', i);
            if (j < 0) return "?";
            return line.Substring(i, j - i);
        }
    }
}
