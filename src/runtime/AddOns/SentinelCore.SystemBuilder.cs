// ─────────────────────────────────────────────────────────────────────────────
// This Source Code Form is subject to the terms of the Mozilla Public License,
// v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
// one at https://mozilla.org/MPL/2.0/.
//
// Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
// ─────────────────────────────────────────────────────────────────────────────
// ═════════════════════════════════════════════════════════════════════════════
//  SentinelCore — SYSTEM BUILDER layer  (partial)
//  File: SentinelCore.SystemBuilder.cs   |   part of `static partial class SentinelCore`
// ─────────────────────────────────────────────────────────────────────────────
//  Backs the System Builder (Docs/SENTINEL_SYSTEM_BUILDER_SPEC.md). Two additive pieces,
//  Phase 0 of that spec — nothing here changes existing behaviour until a consumer calls it:
//
//    • VoterCatalog — the tag → indicator-class / role / seam / defaults map. `Roster.conf`
//      speaks in voter TAGS ("TRND","CCI"…); a chart loads indicator CLASSES ("SentinelTrend_v1_0_0"…).
//      This is the one bridge between them. Seeded to mirror the Council's KnownVoters +
//      SetDefaults (Council v1.4.0) + the orthogonal context axes.
//
//    • RosterIO — ONE parser/writer for `Roster.conf`, so the Council (reader) and the
//      System Builder (writer) can never drift on format. Read() reproduces the Council's
//      exact cascade + parse; Write() serialises a RosterDoc back atomically.
//
//  DEPENDENCY: Foundation-layer only (SettingsDir, Log). Touches no seam, no Gate.
//  Reload note: the Council caches its roster at load, so a Write() takes effect on the
//  Council's NEXT reload (spec Phase 4 adds a hot-reload version stamp).
// ═════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace NinjaTrader.NinjaScript.AddOns.Sentinel
{
    public static partial class SentinelCore
    {
        // ═════════════════════════════════════════════════════════════════════
        //  VOTER KIND — the canonical classification (mirrors the Council's private
        //  enum). STATE voters always carry a reading and always dilute conviction;
        //  a TRIGGER reads 0 ("nothing to report") most bars and dilutes only when it
        //  fired or is absent. See [[state-vs-trigger-voters]] / Council v1.3.0.
        // ═════════════════════════════════════════════════════════════════════
        public enum VoterKind { State, Trigger }

        /// <summary>What a catalog entry IS to the Council — a weighted voter, or unweighted context.</summary>
        public enum SensorRole
        {
            Voter,      // declared in Roster.conf with a weight + kind (the 14 KnownVoters)
            Modulator,  // consulted to damp/scale conviction (Clock / Participation / MTF)
            Veto,       // can zero conviction (LiquidityWalls absorption / Location level-in-path)
        }

        // ═════════════════════════════════════════════════════════════════════
        //  CATALOG ENTRY — one row of the tag ↔ indicator map.
        // ═════════════════════════════════════════════════════════════════════
        public sealed class CatalogEntry
        {
            public string     Tag;            // the Roster.conf token, e.g. "TRND"
            public string     Display;        // human name, e.g. "SentinelTrend"
            public string     TypeName;       // the NT indicator class to load/template, e.g. "SentinelTrend_v1_0_0"
            public SensorRole Role;
            public VoterKind  DefaultKind;    // meaningful for Role.Voter
            public double     DefaultWeight;  // mirrors Council SetDefaults (Role.Voter only)
            public string     Seam;           // the published …State seam (informational; verify vs the Core getters before Phase 3)
            public string     Notes;

            public bool IsVoter { get { return Role == SensorRole.Voter; } }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  VOTER CATALOG — the single table. Order = Council weight Order (F6).
        //  ⚠ DefaultWeight/DefaultKind mirror Council_v1_0_0 SetDefaults + DefaultKind();
        //     keep in sync when the Council's starting weights change.
        //  ⚠ TypeName/Seam are used by the Builder's chart-materialisation (spec Phase 2/3),
        //     NOT by the Phase-1 roster editor — treat as best-effort until the Phase-3 spike
        //     confirms each class name + seam getter.
        // ═════════════════════════════════════════════════════════════════════
        public static class VoterCatalog
        {
            private static readonly List<CatalogEntry> _all = new List<CatalogEntry>
            {
                // ── the weighted voters (Roster.conf) ─────────────────────────────────────
                V("TRND", "SentinelTrend", "SentinelTrend_v1_0_0",              VoterKind.State,   1.0, "TrendState",      "Structural trailing-line trend."),
                V("CCI",  "Woodies CCI",   "WoodiesCCIPro_v1_0_0",              VoterKind.State,   0.8, "CciState",        "Woodies CCI trend bias (×1.5 strong)."),
                V("ADX",  "ADX Pro",       "ADXPro_v1_2_0",                     VoterKind.State,   0.6, "AdxState",        "Regime/strength confirmer when trend is ON."),
                V("ENV",  "Vol Envelope",  "VolEnvelope_v0_2_0",                VoterKind.State,   0.6, "EnvelopeState",   "VolEnvelope trend regime. Also drives the squeeze modulator."),
                V("BRK",  "Brick",         "SentinelTBars (bar type)",          VoterKind.State,   0.5, "BrickState",      "Adaptive brick micro-trend. Published by the SentinelTBars BAR TYPE, not a study — cannot be added as an indicator."),
                V("CMP",  "Compression",   "CompressionBase_v1_3_0",            VoterKind.Trigger, 0.7, "CompressionState","Held breakout direction off a compression base."),
                V("IMKT", "Intermarket",   "Intermarket_v1_0_0",                VoterKind.State,   0.6, "IntermarketState","Correlated-instrument macro lean (instrument-keyed by design)."),
                V("WAE",  "WAE",           "SentinelWAE_v2_0_0",                VoterKind.Trigger, 0.7, "WaeState",        "Waddah-Attar confirmed momentum-explosion breakout."),
                V("GREV", "God Reversal",  "SentinelGodReversal_v1_0_0",        VoterKind.Trigger, 0.9, "GodReversalState","Candle-grammar reversal — a MEAN-REVERSION voice, often counter-trend."),
                V("STF",  "Stoch Filter",  "SentinelStochasticTripleFilter_v1_0_0", VoterKind.State, 0.0, "StfState",   "Gaussian-Channel midline SLOPE. Default weight 0 = exploration primitive (recorded, no fusion impact). Also drives the CHOP veto."),
                V("FLOW", "Flow",          "SentinelFlow_v1_0_0",               VoterKind.State,   0.9, "FlowState",       "Tick-rule CVD regime — the one axis not derived from price. ⚠ port-harvest sensor; confirm it compiles/loads."),
                V("STRC", "Structure",     "SentinelStructure_v1_0_0",          VoterKind.State,   0.7, "StructureState",  "Swing HH/HL·LH/LL market structure. ⚠ port-harvest sensor."),
                V("EXH",  "Exhaustion",    "SentinelExhaustion_v1_0_0",         VoterKind.Trigger, 0.5, "ExhaustionState", "Leledc reversal (held direction) — a mean-reversion voice. ⚠ port-harvest sensor."),
                V("AVMA", "ADXVMA",        "SentinelADXVMA_v1_0_0",             VoterKind.State,   0.6, "AdxvmaState",     "ADX-volatility adaptive-MA trinary trend (neutral in chop). Candidate-library Tier-2 voter."),
                V("SPRT", "SuperTrend",    "SentinelSuperTrend_v1_0_0",         VoterKind.State,   0.7, "SuperTrendState", "ATR-band trailing-flip trend (always ±1). Candidate-library Tier-2 voter."),
                V("PSAR", "Parabolic SAR", "SentinelParabolicSAR_v1_0_0",       VoterKind.State,   0.5, "SarState",        "Wilder Parabolic SAR trend/stop (always ±1). Candidate-library Tier-2 voter."),
                V("ZSC",  "Z-Score",       "SentinelZScore_v1_0_0",             VoterKind.Trigger, 0.4, "ZScoreState",     "(Close−SMA)/StdDev mean-reversion — a fade voice, à la EXH/GREV. Candidate-library Tier-2 voter."),
                V("ARCH", "Trend Architect","SentinelTrendArchitect_v1_0_0",     VoterKind.State,   0.7, "TrendArchitectState","MPL Pine port — composite PRISM trend + Trend-Regime-Gate (fuses MFI/CCI/CVD/Hurst/KAMA). Candidate-library Tier-2 voter."),
                V("VDYA", "VIDYA",         "SentinelVIDYA_v1_0_0",              VoterKind.State,   0.5, "VidyaState",      "Chande-CMO-modulated adaptive-MA trend (VIDYA). Candidate-library novel-signals voter."),
                V("HARM", "Harmonic",      "SentinelHarmonic_v1_0_0",           VoterKind.Trigger, 0.4, "HarmonicState",   "Harmonic XABCD pattern completions (Gartley/Bat/Butterfly/Crab) — a reversal voice. Candidate-library novel-signals voter."),
                V("FLUX", "Flux",          "SentinelFlux (bar type)",           VoterKind.State,   0.7, "FluxState",       "Net ORDER-FLOW direction of the imbalance-driven bar close. Published by the SentinelFlux BAR TYPE (id 212203), not a study — the whole chart clock is order-flow-synchronized, so this is orthogonal to the price bloc. Distinct from FLOW (an overlay CVD indicator): FLUX is the substrate + native flow/price absorption divergence."),
                V("CVB",  "Conviction Bias","SentinelDrift (bar type)",          VoterKind.State,   0.6, "ConvictionState", "FLOW-CONFIRMED trend direction from the SentinelDrift BAR TYPE (id 212204): the structural brick direction, voted only when the aggregated tape (per-brick signed delta) confirms it, else abstains. Orthogonal to the price bloc — its conviction is order-flow-sourced. Sibling to FLUX (the Flux bar clock); CVB is the Drift clock's flow-gated trend."),
                V("CVD",  "CVD",           "SentinelCVD_v1_0_0",                VoterKind.State,   0.0, "CvdState",        "Session cumulative volume delta — direction from its SLOPE, plus divergence and EFFICIENCY (ticks of price per 1,000 contracts of net aggression, i.e. market impact). Works on ANY bar type, unlike FLUX which only exists where SentinelFlux is the clock. AUDITION at 0.0: recorded and graded, cannot move the verdict — fit it, do not nudge it. ⚠ This row was MISSING until 2026-07-30 while the Council both declared CVD in KnownVoters and fused it, so the Cockpit and System Builder could not see a voter that was voting."),
                V("BSP",  "Buy/Sell Pressure","BuySellVolumePressureMountain_v1_0_0", VoterKind.State, 0.0, "PressureState", "Dominant side of CLASSIFIED buy-vs-sell volume (smoothed share, outside a neutral band). Ported with a card but seamless until v1.45.0 — it had been computing an opinion nothing could consult. ⚠ AUDITION at weight 0: the 2026-07-26 re-test killed all 19 voters and every one was PRICE-derived, so a true bid/ask-classified voice is the untested family — graded before trusted. ⚠ TickBacked: OnMarketData is realtime-only, so a historical rebuild falls back to an OHLC candle-shape PROXY that is itself price-derived; never grade a proxy row as order flow."),

                // ── orthogonal context axes (NOT Roster.conf lines — toggled via the Council's Consult* settings) ──
                C("CLOCK",  "Clock",         "Clock_v1_0_0",          SensorRole.Modulator, "ClockState",        "Session phase / mins-to-close / kill window. Instrument-keyed by design."),
                C("PARTIC", "Participation", "Participation_v1_0_0",  SensorRole.Modulator, "ParticipationState","Time-normalised RVOL + climax/dry-up. ⚠ scope-keyed — load on every Council chart."),
                C("MTF",    "MTF",           "Mtf_v1_0_0",            SensorRole.Modulator, "MtfState",          "Higher-timeframe consensus ladder (counter-HTF penalty)."),
                C("LOC",    "Location",      "Location_v1_0_0",       SensorRole.Veto,      "LevelState",        "VWAP/PDH-PDL/OR/IB structural levels in the trade's path."),
                C("LIQ",    "Liquidity",     "LiquidityWalls_v1_0_0", SensorRole.Veto,      "LiquidityState",    "Order-flow absorption walls — vetoes an entry into a wall."),
            };

            private static CatalogEntry V(string tag, string disp, string type, VoterKind kind, double w, string seam, string notes)
            { return new CatalogEntry { Tag = tag, Display = disp, TypeName = type, Role = SensorRole.Voter, DefaultKind = kind, DefaultWeight = w, Seam = seam, Notes = notes }; }

            private static CatalogEntry C(string tag, string disp, string type, SensorRole role, string seam, string notes)
            { return new CatalogEntry { Tag = tag, Display = disp, TypeName = type, Role = role, DefaultKind = VoterKind.State, DefaultWeight = 0.0, Seam = seam, Notes = notes }; }

            /// <summary>All catalog entries, in F6/Council order.</summary>
            public static IList<CatalogEntry> All { get { return _all.AsReadOnly(); } }

            /// <summary>The 14 weighted voters (the Roster.conf surface).</summary>
            public static IEnumerable<CatalogEntry> Voters
            {
                get { foreach (var e in _all) if (e.Role == SensorRole.Voter) yield return e; }
            }

            /// <summary>The context axes (consulted, not declared in Roster.conf).</summary>
            public static IEnumerable<CatalogEntry> Context
            {
                get { foreach (var e in _all) if (e.Role != SensorRole.Voter) yield return e; }
            }

            /// <summary>Lookup by tag (case-insensitive); null if unknown.</summary>
            public static CatalogEntry ByTag(string tag)
            {
                if (string.IsNullOrEmpty(tag)) return null;
                string t = tag.ToUpperInvariant();
                foreach (var e in _all) if (e.Tag == t) return e;
                return null;
            }

            // ── SHARED-CATALOG EXPORT ──────────────────────────────────────────────────────
            // This C# class is the ONE source of truth for tag → role/kind/default-weight/seam.
            // The Python Lab (Sentinel\Lab) needs the SAME map to build its fit's feature columns
            // in the SAME canonical order, or the two drift (train.py used to hardcode a stale
            // 10-voter list against a 22-voter catalog). Rather than duplicate the list in Python,
            // we EMIT it to a flat file the Lab reads. Written on Council load; Lab falls back to
            // an embedded copy if absent. Flat pipe-delimited (no JSON writer in C#, per Model.conf).
            /// <summary>The shared-catalog path: Sentinel\Models\catalog.conf.</summary>
            public static string DefaultPath { get { return Path.Combine(SettingsDir, "Models", "catalog.conf"); } }

            /// <summary>Emit the catalog to the shared path (write-if-changed; best-effort, never throws).</summary>
            public static void Export() { ExportTo(DefaultPath); }

            /// <summary>Emit the catalog to <paramref name="path"/> as `tag|role|kind|defWeight|display|seam`.</summary>
            public static void ExportTo(string path)
            {
                try
                {
                    var sb = new StringBuilder();
                    sb.Append("# Sentinel VoterCatalog — AUTO-EMITTED by SentinelCore. Do not hand-edit.\n");
                    sb.Append("# Single source of truth shared by the C# Council and the Python Lab (Sentinel\\Lab).\n");
                    sb.Append("# schema=1  order=canonical (F6/Council)  cols: tag|role|kind|defWeight|display|seam\n");
                    foreach (var e in _all)
                    {
                        sb.Append(e.Tag).Append('|')
                          .Append(e.Role.ToString().ToLowerInvariant()).Append('|')
                          .Append(e.DefaultKind.ToString().ToLowerInvariant()).Append('|')
                          .Append(e.DefaultWeight.ToString("0.####", CultureInfo.InvariantCulture)).Append('|')
                          .Append(e.Display).Append('|')
                          .Append(e.Seam).Append('\n');
                    }
                    string next = sb.ToString();
                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    // write-if-changed: the catalog is static, so avoid churning the file on every chart load.
                    if (!File.Exists(path) || File.ReadAllText(path) != next)
                        File.WriteAllText(path, next, new UTF8Encoding(false));
                }
                catch { /* best-effort: a missing catalog just makes the Lab use its embedded fallback */ }
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  ROSTER MODEL — the in-memory form of a Roster.conf.
        // ═════════════════════════════════════════════════════════════════════
        public sealed class RosterLine
        {
            public string     Tag;      // upper-cased voter tag
            public double?    Weight;   // w= override, or null (use the Council's F6/base weight)
            public VoterKind? Kind;     // state/trigger override, or null (use the Council's DefaultKind)
            public string     Comment;  // inline "# …" trailing comment, preserved on round-trip
        }

        public sealed class RosterDoc
        {
            public readonly List<RosterLine> Lines = new List<RosterLine>();
            public string Source;   // the winning file path, or null when no file declared any voter

            public bool HasDeclarations { get { return Lines.Count > 0; } }

            public RosterLine Find(string tag)
            {
                if (string.IsNullOrEmpty(tag)) return null;
                string t = tag.ToUpperInvariant();
                foreach (var l in Lines) if (l.Tag == t) return l;
                return null;
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  ROSTER I/O — the single reader/writer.
        //  Cascade (most-specific wins):
        //      Sentinel\Models\<INST>\<bartag>\Roster.conf
        //    ▸ Sentinel\Models\<INST>\Roster.conf
        //    ▸ Sentinel\Models\Roster.conf
        //  Parse grammar (identical to Council_v1_0_0.ParseRoster):
        //      TAG [w=<val>] [state|trigger|kind=<val>]   # comment
        // ═════════════════════════════════════════════════════════════════════
        public static class RosterIO
        {
            private static string ModelsDir { get { return Path.Combine(SettingsDir, "Models"); } }

            /// <summary>The candidate paths, most-specific first, for a scope's roster.</summary>
            public static List<string> Candidates(string inst, string barTag)
            {
                var c = new List<string>();
                string models = ModelsDir;
                if (!string.IsNullOrEmpty(inst))
                {
                    if (!string.IsNullOrEmpty(barTag))
                    {
                        c.Add(Path.Combine(Path.Combine(Path.Combine(models, inst), barTag), "Roster.conf"));
                        // v1.33.0 — a LANED tag ("212202v6x24@A") inherits its BAR-TYPE baseline ("212202v6x24")
                        // before falling to the instrument default, so a fresh lane reads as the bar type's roster
                        // until you fork it (System Builder spec §14.2). Only adds a rung; PathFor (write) is unchanged.
                        int at = barTag.IndexOf('@');
                        if (at > 0)
                            c.Add(Path.Combine(Path.Combine(Path.Combine(models, inst), barTag.Substring(0, at)), "Roster.conf"));
                    }
                    c.Add(Path.Combine(Path.Combine(models, inst), "Roster.conf"));
                }
                c.Add(Path.Combine(models, "Roster.conf"));
                return c;
            }

            /// <summary>The most-specific path a Write() targets for this scope.</summary>
            public static string PathFor(string inst, string barTag)
            {
                string models = ModelsDir;
                if (!string.IsNullOrEmpty(inst) && !string.IsNullOrEmpty(barTag))
                    return Path.Combine(Path.Combine(Path.Combine(models, inst), barTag), "Roster.conf");
                if (!string.IsNullOrEmpty(inst))
                    return Path.Combine(Path.Combine(models, inst), "Roster.conf");
                return Path.Combine(models, "Roster.conf");
            }

            /// <summary>v1.39.0 — the winning path in the cascade WITHOUT parsing, so a caller can watch its write
            /// time cheaply (the Council's live config poll). Mirrors Read's rule exactly: first candidate that
            /// exists AND declares at least one voter wins. Null when nothing declares one.</summary>
            public static string Resolve(string inst, string barTag)
            {
                try
                {
                    foreach (string p in Candidates(inst, barTag))
                    {
                        if (!File.Exists(p)) continue;
                        if (Parse(File.ReadAllLines(p)).Count > 0) return p;
                    }
                }
                catch (Exception _sx) { SentinelCore.Swallow("SentinelSysBuilder.Resolve", _sx); }
                return null;
            }

            /// <summary>
            /// Resolve + parse the scope's roster. Returns a RosterDoc whose Source is the winning
            /// path (null + empty when no file declared any voter). NEVER throws — a broken/missing
            /// file yields an empty doc, exactly as the Council falls back to its default declaration.
            /// </summary>
            public static RosterDoc Read(string inst, string barTag)
            {
                var doc = new RosterDoc();
                try
                {
                    foreach (string p in Candidates(inst, barTag))
                    {
                        if (!File.Exists(p)) continue;
                        var parsed = Parse(File.ReadAllLines(p));
                        if (parsed.Count > 0)
                        {
                            doc.Lines.AddRange(parsed);
                            doc.Source = p;
                            break;
                        }
                    }
                }
                catch { doc.Lines.Clear(); doc.Source = null; }
                return doc;
            }

            /// <summary>Parse Roster.conf lines. Mirrors Council_v1_0_0.ParseRoster exactly (first tag wins on dup).</summary>
            public static List<RosterLine> Parse(string[] lines)
            {
                var outLines = new List<RosterLine>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                if (lines == null) return outLines;

                foreach (string raw in lines)
                {
                    if (raw == null) continue;
                    string line = raw;
                    string comment = null;
                    int hash = line.IndexOf('#');
                    if (hash >= 0) { comment = line.Substring(hash + 1).Trim(); line = line.Substring(0, hash); }
                    line = line.Trim();
                    if (line.Length == 0) continue;

                    string[] parts = line.Split(new[] { ' ', '\t', '=' }, StringSplitOptions.RemoveEmptyEntries);
                    string tag = parts[0].ToUpperInvariant();
                    if (seen.Contains(tag)) continue;
                    seen.Add(tag);

                    var rl = new RosterLine { Tag = tag, Comment = string.IsNullOrEmpty(comment) ? null : comment };
                    for (int i = 1; i < parts.Length; i++)
                    {
                        string p = parts[i];
                        if (p.Equals("w", StringComparison.OrdinalIgnoreCase) && i + 1 < parts.Length)
                        {
                            double val;
                            if (double.TryParse(parts[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out val))
                                rl.Weight = val;
                            continue;
                        }
                        if (p.Equals("state", StringComparison.OrdinalIgnoreCase))        rl.Kind = VoterKind.State;
                        else if (p.Equals("trigger", StringComparison.OrdinalIgnoreCase)) rl.Kind = VoterKind.Trigger;
                        else if (p.Equals("kind", StringComparison.OrdinalIgnoreCase) && i + 1 < parts.Length)
                        {
                            string kv = parts[i + 1];
                            if (kv.Equals("state", StringComparison.OrdinalIgnoreCase))        rl.Kind = VoterKind.State;
                            else if (kv.Equals("trigger", StringComparison.OrdinalIgnoreCase)) rl.Kind = VoterKind.Trigger;
                        }
                    }
                    outLines.Add(rl);
                }
                return outLines;
            }

            /// <summary>Serialise a RosterDoc to a Roster.conf text body (the exact grammar Parse reads back).</summary>
            public static string Serialize(RosterDoc doc, string headerNote)
            {
                var sb = new StringBuilder();
                sb.Append("# Sentinel Roster — the Council's declared voter set for this scope.").Append('\n');
                sb.Append("# Grammar:  TAG [w=<weight>] [state|trigger]   # comment").Append('\n');
                sb.Append("#   w=0 = the exploration primitive (recorded, contributes nothing to the fusion).").Append('\n');
                if (!string.IsNullOrEmpty(headerNote))
                    sb.Append("# ").Append(headerNote).Append('\n');
                sb.Append("# ⚠ Regenerated by the Sentinel System Builder — standalone comment lines are not preserved.").Append('\n');
                sb.Append('\n');

                if (doc != null)
                {
                    foreach (var l in doc.Lines)
                    {
                        if (l == null || string.IsNullOrEmpty(l.Tag)) continue;
                        sb.Append(l.Tag);
                        if (l.Weight.HasValue)
                            sb.Append(" w=").Append(l.Weight.Value.ToString("0.####", CultureInfo.InvariantCulture));
                        if (l.Kind.HasValue)
                            sb.Append(l.Kind.Value == VoterKind.Trigger ? " trigger" : " state");
                        if (!string.IsNullOrEmpty(l.Comment))
                            sb.Append("   # ").Append(l.Comment);
                        sb.Append('\n');
                    }
                }
                return sb.ToString();
            }

            /// <summary>
            /// Write the scope's roster to its most-specific path, ATOMICALLY (temp + File.Replace),
            /// creating the Models\&lt;INST&gt;\&lt;bartag&gt; directory as needed. Returns the path written.
            /// Throws on I/O failure (the caller — a UI Save — surfaces it; nothing trades on this path).
            /// </summary>
            public static string Write(string inst, string barTag, RosterDoc doc, string headerNote)
            {
                string dest = PathFor(inst, barTag);
                string dir = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string body = Serialize(doc, headerNote);
                string tmp = dest + ".tmp";
                File.WriteAllText(tmp, body, new UTF8Encoding(false));   // no BOM — matches the Council's File.ReadAllLines
                if (File.Exists(dest)) File.Replace(tmp, dest, null);
                else                   File.Move(tmp, dest);

                try { SentinelCore.Log("RosterIO", "wrote " + dest + " (" + (doc != null ? doc.Lines.Count : 0) + " voters)"); } catch (Exception _sx) { SentinelCore.Swallow("SentinelSysBuilder.Write", _sx); }
                return dest;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  LANE PROFILE (Lane.conf) — the per-lane SYSTEM overrides BEYOND the roster (v1.33.0)
        //  A lane's ROSTER (Roster.conf) is voters+weights+kind. A lane's PROFILE (Lane.conf, beside it) is the
        //  Council FUSION knobs the roster doesn't hold — ConvictionFloor, bias deadband, the consult toggles, the
        //  modulator damps. SPARSE: only keys PRESENT override; an absent key inherits the chart's F6 value. So a
        //  lane file lists ONLY what the experiment changes, and adding a knob later never breaks an old file.
        //  Grammar: one `key = value` per line, '#' comment, keys case-insensitive. Path (most-specific ONLY — a
        //  profile is deliberately not cascaded, so "what overrides my chart" is answerable from exactly one file):
        //      Sentinel\Models\<inst>\<bartag>@<lane>\Lane.conf   (beside Roster.conf)
        //  System Builder spec §14.7.
        // ─────────────────────────────────────────────────────────────────────
        public static class LaneIO
        {
            private static string ModelsDir { get { return Path.Combine(SettingsDir, "Models"); } }

            /// <summary>The Lane.conf path for a scope. This is the WRITE path — always the most specific location.
            /// Readers use <see cref="Resolve"/>, which cascades. </summary>
            public static string PathFor(string inst, string barTag)
            {
                string models = ModelsDir;
                if (!string.IsNullOrEmpty(inst) && !string.IsNullOrEmpty(barTag))
                    return Path.Combine(Path.Combine(Path.Combine(models, inst), barTag), "Lane.conf");
                if (!string.IsNullOrEmpty(inst))
                    return Path.Combine(Path.Combine(models, inst), "Lane.conf");
                return Path.Combine(models, "Lane.conf");
            }

            /// <summary>v1.39.0 — CASCADE: scope ▸ instrument ▸ global, FIRST MATCH WINS (identical rule to RosterIO,
            /// so the two config files finally behave the same way). Returns null when no file exists anywhere.
            ///
            /// WHY THIS CHANGED. Lane.conf used to be most-specific-ONLY, which meant a chart on a bar type nobody had
            /// used before found no file, silently kept its F6 ConvictionFloor, and recorded almost nothing — looking
            /// exactly like every sensor being dead. That one asymmetry (Roster cascades, Lane does not) is what made
            /// "new chart ▸ pick a bar type ▸ load the template ▸ run" impossible: every new bar type needed a
            /// hand-made directory first. Now one Models\&lt;INST&gt;\Lane.conf covers every bar type for that
            /// instrument, and a per-bartype file still overrides it when a test needs one.</summary>
            public static string Resolve(string inst, string barTag)
            {
                try
                {
                    if (!string.IsNullOrEmpty(inst) && !string.IsNullOrEmpty(barTag))
                    {
                        string p = PathFor(inst, barTag);
                        if (File.Exists(p)) return p;
                    }
                    if (!string.IsNullOrEmpty(inst))
                    {
                        string p = PathFor(inst, null);
                        if (File.Exists(p)) return p;
                    }
                    string g = PathFor(null, null);
                    if (File.Exists(g)) return g;
                }
                catch (Exception _sx) { SentinelCore.Swallow("SentinelSysBuilder.Resolve", _sx); }
                return null;
            }

            /// <summary>Read the lane's sparse override map (case-insensitive keys). Empty (never null) if no file
            /// exists anywhere in the cascade. Never throws.</summary>
            public static Dictionary<string,string> Read(string inst, string barTag)
            {
                var map = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    string p = Resolve(inst, barTag);
                    if (p == null) return map;
                    foreach (var raw in File.ReadAllLines(p))
                    {
                        string line = raw ?? "";
                        int h = line.IndexOf('#'); if (h >= 0) line = line.Substring(0, h);
                        line = line.Trim();
                        if (line.Length == 0) continue;
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        string k = line.Substring(0, eq).Trim();
                        string v = line.Substring(eq + 1).Trim();
                        if (k.Length > 0) map[k] = v;
                    }
                }
                catch (Exception _sx) { SentinelCore.Swallow("SentinelSysBuilder.Read", _sx); }
                return map;
            }

            /// <summary>Atomic write of the sparse override map (BOM-less UTF-8). Blank/null map still writes a header stub.</summary>
            public static string Write(string inst, string barTag, Dictionary<string,string> keys, string headerNote)
            {
                string dest = PathFor(inst, barTag);
                try
                {
                    string dir = Path.GetDirectoryName(dest);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    var sb = new StringBuilder();
                    sb.Append("# Sentinel Lane profile");
                    if (!string.IsNullOrEmpty(headerNote)) sb.Append(" — ").Append(headerNote);
                    sb.Append('\n');
                    sb.Append("# SPARSE overrides of the Council's F6 fusion knobs; absent keys inherit the chart's setting.\n");
                    if (keys != null)
                        foreach (var kv in keys)
                            if (!string.IsNullOrEmpty(kv.Key) && kv.Value != null)
                                sb.Append(kv.Key).Append(" = ").Append(kv.Value).Append('\n');
                    string tmp = dest + ".tmp";
                    File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
                    if (File.Exists(dest)) File.Replace(tmp, dest, null);
                    else                   File.Move(tmp, dest);
                    try { SentinelCore.Log("LaneIO", "wrote " + dest + " (" + (keys != null ? keys.Count : 0) + " overrides)"); } catch (Exception _sx) { SentinelCore.Swallow("SentinelSysBuilder.Write", _sx); }
                }
                catch (Exception _sx) { SentinelCore.Swallow("SentinelSysBuilder.Write", _sx); }
                return dest;
            }

            /// <summary>Parse a present key as a double (InvariantCulture). False (leaves val 0) when absent/unparseable — the
            /// caller then leaves its property at the F6 value. This is how "absent ⇒ inherit" is enforced at the seam.</summary>
            public static bool TryDouble(Dictionary<string,string> m, string key, out double val)
            {
                val = 0; string s;
                if (m == null || !m.TryGetValue(key, out s)) return false;
                return double.TryParse(s, System.Globalization.NumberStyles.Any,
                                       System.Globalization.CultureInfo.InvariantCulture, out val);
            }

            /// <summary>Parse a present key as a bool (true/1/yes/on · false/0/no/off). False when absent/unparseable.</summary>
            public static bool TryBool(Dictionary<string,string> m, string key, out bool val)
            {
                val = false; string s;
                if (m == null || !m.TryGetValue(key, out s)) return false;
                s = s.Trim().ToLowerInvariant();
                if (s == "true" || s == "1" || s == "yes" || s == "on")  { val = true;  return true; }
                if (s == "false" || s == "0" || s == "no" || s == "off") { val = false; return true; }
                return false;
            }
        }

        public static class LaneAssign
        {
            /// <summary>The single assignment file. One flat file, NOT under Models\ — the Models
            /// path already encodes the lane, so a lane-assignment map cannot live there.</summary>
            public static string PathFor() { return Path.Combine(SettingsDir, "Lanes.conf"); }

            /// <summary>Read the assignment map (case-insensitive keys). Empty, never null. Never throws.</summary>
            public static Dictionary<string,string> Read()
            {
                var map = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    string p = PathFor();
                    if (!File.Exists(p)) return map;
                    foreach (string raw in File.ReadAllLines(p))
                    {
                        string line = raw ?? "";
                        int h = line.IndexOf('#'); if (h >= 0) line = line.Substring(0, h);
                        line = line.Trim();
                        if (line.Length == 0) continue;
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        string k = line.Substring(0, eq).Trim();
                        string v = line.Substring(eq + 1).Trim();
                        if (k.Length > 0) map[k] = v;
                    }
                }
                catch (Exception _sx) { SentinelCore.Swallow("LaneAssign.Read", _sx); }
                return map;
            }

            /// <summary>Set ONE key's assignment, preserving every other line and any comments.
            /// Blank/null lane REMOVES the key. Returns false on IO failure (never throws).
            /// This is the Conductor's writer — it is what makes a job line's `lane=` real.</summary>
            public static bool Set(string key, string lane, string note)
            {
                if (string.IsNullOrEmpty(key)) return false;
                try
                {
                    string p = PathFor();
                    var kept = new List<string>();
                    if (File.Exists(p))
                        foreach (string raw in File.ReadAllLines(p))
                        {
                            string probe = raw ?? "";
                            int h = probe.IndexOf('#'); if (h >= 0) probe = probe.Substring(0, h);
                            int eq = probe.IndexOf('=');
                            // drop only the line that assigns THIS key; keep comments and everything else
                            if (eq > 0 && string.Equals(probe.Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase))
                                continue;
                            kept.Add(raw);
                        }

                    string clean = SanitizeLane(lane);
                    if (!string.IsNullOrEmpty(clean))
                        kept.Add(key + " = " + clean + (string.IsNullOrEmpty(note) ? "" : "   # " + note));

                    var sb = new StringBuilder();
                    sb.AppendLine("# Sentinel\\Lanes.conf — chart LANE ASSIGNMENT (SentinelCore.LaneAssign).");
                    sb.AppendLine("# Overrides the Council's F6 'Scope Lane'. Cascade: <inst>.<barTag> then <inst>.");
                    sb.AppendLine("# Written by the Conductor at job start; safe to hand-edit. ASCII only.");
                    bool anyBody = false;
                    foreach (string l in kept)
                    {
                        if (l != null && l.TrimStart().StartsWith("# Sentinel\\Lanes.conf")) continue;
                        if (l != null && l.TrimStart().StartsWith("# Overrides the Council")) continue;
                        if (l != null && l.TrimStart().StartsWith("# Written by the Conductor")) continue;
                        sb.AppendLine(l); anyBody = true;
                    }
                    if (!anyBody) sb.AppendLine();
                    File.WriteAllText(p, sb.ToString(), new UTF8Encoding(false));
                    return true;
                }
                catch (Exception _sx) { SentinelCore.Swallow("LaneAssign.Set", _sx); return false; }
            }
        }

    }
}
