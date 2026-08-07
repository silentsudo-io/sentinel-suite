// ─────────────────────────────────────────────────────────────────────────────
// This Source Code Form is subject to the terms of the Mozilla Public License,
// v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
// one at https://mozilla.org/MPL/2.0/.
//
// Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
// ─────────────────────────────────────────────────────────────────────────────
// ═════════════════════════════════════════════════════════════════════════════
//  SentinelLogEngine — workspace-wide MAE/MFE trade-excursion logging engine
//  File: SentinelLogEngine_v1_0_0.cs
//  Engine version: v1.0.0
//  Schema: 1.0   (see MAE_Logger_Schema_Spec_v1.0.md — the authoritative contract)
// ─────────────────────────────────────────────────────────────────────────────
//  PURPOSE
//    A standalone, strategy-AGNOSTIC engine that records per-trade MAE/MFE excursion
//    to JSONL. Extracted from ConfluenceArchitect's embedded recorder (v0.6.1) and
//    generalized so ANY strategy — or the future zero-touch Add-On — can feed it.
//
//    The engine knows nothing about indicators, bars, or NinjaScript host state. The
//    CALLER feeds it primitives (prices, timestamps, optional opaque context). This is
//    what lets one engine serve every strategy and both logging tiers.
//
//  TWO TIERS (one schema — see spec §2):
//    • Tier 1 (zero-touch): Add-On feeds price-only excursion from account + market data.
//                           Produces core record, no ctx.
//    • Tier 2 (rich):       Instrumented strategy additionally supplies ctx + per-bar ext
//                           + atr. Produces core record PLUS context.
//
//  BASKET-READY (spec §1, §3.1, §3.5):
//    Every record carries account/strategy/instanceId/params identity, and every path
//    sample is wall-clock TIMESTAMPED so cross-strategy basket excursion can be computed
//    at analysis time (a time-overlap computation, not a sum of per-trade MAEs).
//
//  USAGE (tier-2 strategy, per trade):
//    1. OnEntry(account, dir, qty, entryPriceAvg, entryTimeUtc, tick, atrAtEntry, ctx)
//    2. OnBar(timeUtc, barOffset, high, low, close, atr, ext)   // each in-trade bar
//    3. OnExit(exitPriceAvg, exitTimeUtc, exitReason)            // writes one JSONL line
//
//  USAGE (tier-1 Add-On, per trade): same calls, but ctx/ext null, atr NaN, tier=1.
//
//  THREADING NOTE (spec §11.3): the engine itself does no UI work and is safe to call
//    from background data-event threads. Any DASHBOARD reading engine output must marshal
//    to the UI thread via Dispatcher.InvokeAsync (NOT the engine's concern, but noted so
//    callers don't mistake the engine for UI-thread-bound).
//
// ─────────────────────────────────────────────────────────────────────────────
//  CHANGELOG
//  v1.2.0 / schema 1.1 (2026-07-01) — EYE VERDICT CAPTURE (the profit keystone).
//    - OnEntry now snapshots the current SentinelEye verdict for the trade's instrument
//      (SentinelCore.GetEyeVerdict, no staleness filter) and freezes it with the trade.
//    - Every record gains an eye block: eyeHad, eyeDir, eyeScore, eyeSource, eyeAgeSec,
//      and eyeAligned (= did Eye qualify THIS trade's direction). This is what lets Lens
//      partition trades into Eye-endorsed vs not and prove whether the Eye filter adds edge.
//    - Additive/backward-compatible: schema bumped 1.0 → 1.1; old records simply lack the
//      eye fields and analysis treats them as null. No path/ctx/identity changes.
//    - Class name + filename intentionally UNCHANGED (SentinelLogEngine is a shared symbol,
//      edited in place like SentinelCore — a versioned copy would collide, CS0101).
//  sentinel-rebrand (2026-07-01) — MAEEngine → SentinelLogEngine; namespace MAELogging → Sentinel.
//             JSONL logs now under <UserDataDir>\Sentinel\Log (was "MAELogger"). Schema UNCHANGED
//             (still 1.0; JSON field names like maeTicksRaw are the DOMAIN term MAE — intentionally
//             NOT renamed). Consumed by GodTradesStrategy_v1_1_0 + ConfluenceArchitect_v0_7_0 (tier-2).
//  v1.1.0 — live-state surface + decoupled service registry (dashboard sees BOTH tiers).
//    - Added public Live* getters exposing the in-flight trade (account/strategy/inst/tier/
//      dir/entry/running MAE-MFE/last px). Display-only; reads the same values the JSONL uses.
//    - Added static hooks OnEngineTradeOpened / OnEngineTradeClosed. The capture service (if
//      loaded) subscribes them to union tier-2 strategy trades into its open-position
//      registry, so the dashboard shows tier-1 AND tier-2 live trades. No hard dependency:
//      if no service is present the hooks are null and the engine behaves exactly as before.
//      This keeps SentinelLogEngine both strategy- and service-agnostic.
//    - Record format unchanged (schema 1.0).
//  v1.0.1 — descriptive filename scheme + auto-derived paramHash.
//    - Filenames now: {UTCstamp}__{account}__{strategy}-{ver}__{inst}__t{tier}__p{hash}.jsonl
//      UTC ISO-basic timestamp first (lexical sort == chronological); "__" field
//      separators (parse-safe even when a field contains a single "_"); strategy version,
//      tier marker, and a 6-char param hash all visible at a glance. Solves the A/B case
//      (different configs => visibly different names) and the basket case (identity in name).
//    - paramHash now AUTO-DERIVED (deterministic FNV-1a) from the params JSON when the
//      caller doesn't supply one, so every strategy gets a correct, stable hash for free.
//      Same config => same hash (groups re-runs); different config => different hash.
//    - Record format UNCHANGED (schema still 1.0); only the filename and the hash-fill
//      behavior changed. Existing analysis code is unaffected.
//
//  v1.0.0 — initial extraction from ConfluenceArchitect v0.6.1.
//    - Lifted SamplePath / WriteTradeRecord / lifecycle logic verbatim in spirit, made
//      strategy-agnostic: caller passes prices+time+opaque bags instead of the engine
//      reaching into ConfluenceState / Close[0] / CurrentBar.
//    - Schema 1.0 additions over the embedded recorder:
//        * identity: account, strategy, stratVer, instanceId, params/paramHash, engineVer, tier
//        * path samples: wall-clock "t" timestamp (basket alignment)
//        * path samples: "atr" per sample (closes the ATR-replay-fidelity gap)
//        * excursion: maeTimeToMs / mfeTimeToMs (cross-bar-type basket alignment)
//        * ctx / ext are OPAQUE pass-through bags (engine stays strategy-agnostic)
//    - Running MAE/MFE tracked EVERY bar (before stride gate); only path SAMPLES thin.
//      (Same correctness guarantee as the embedded recorder.)
//    - v1 DEFERS tier-1/tier-2 in-engine merge (spec §4): records written separately,
//      reconciled in the Python analysis layer.
// ═════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace NinjaTrader.NinjaScript.AddOns.Sentinel
{
    // ─────────────────────────────────────────────────────────────────────────
    //  SentinelLogEngine — one instance per logged trade-source (strategy-instance or
    //  Add-On-tracked position). Holds the open-trade state, accumulates excursion,
    //  writes one JSONL line per completed trade.
    // ─────────────────────────────────────────────────────────────────────────
    public sealed class SentinelLogEngine
    {
        // engine identity (independent of any strategy version)
        public const string EngineVersion = "v1.2.0";
        public const string SchemaVersion = "1.1";

        // ── configuration (set once at construction) ──────────────────────────
        private readonly string  _account;        // basket grouping key (spec §3.1)
        private readonly string  _strategy;        // strategy name
        private readonly string  _stratVer;        // strategy version
        private readonly string  _instanceId;      // stable per-instance id
        private readonly string  _instrument;      // instrument name
        private readonly int     _tier;            // 1 = price-only, 2 = rich
        private readonly string  _paramsJson;      // pre-serialized params object or "null"
        private readonly string  _paramHash;       // short hash of params, or "null"
        private readonly string  _logPath;         // resolved output file
        private readonly bool    _pathSampling;    // capture per-bar path array?
        private readonly int     _pathStride;      // sample every N bars
        private readonly int     _pathMaxSamples;  // hard cap per trade
        private readonly Action<string> _errorSink; // where to report write errors (e.g. Print)

        // ── per-trade mutable state ───────────────────────────────────────────
        private bool     _tradeOpen;
        private int      _dir;                 // +1 long / -1 short
        private int      _qty;
        private double   _entryPx;
        private DateTime _entryTimeUtc;
        private int      _entryBarOffsetBase;   // bar offset reference (caller-relative)
        private double   _atrAtEntry;           // for ATR-unit MAE/MFE (NaN if unavailable)
        private double   _tick;
        private string   _ctxJson;              // frozen entry-context (tier-2) or null

        // Eye verdict frozen at ENTRY (the keystone for proving Eye's edge). Captured
        // regardless of staleness — we also record the age so analysis can filter.
        private bool     _eyeHad;               // was any Eye verdict published for this instrument?
        private int      _eyeDir;               // +1 long-qualified / -1 short-qualified / 0 neutral
        private double   _eyeScore;             // Eye's best-model score (NaN if none)
        private string   _eyeSource;            // qualifying row/model source (null if none)
        private double   _eyeAgeSec;            // verdict age at entry, seconds (NaN if none)

        // running excursion (tracked EVERY bar; spec §3.3 correctness guarantee)
        private double _maeTicksRaw, _mfeTicksRaw;
        private double _maeTicksHa,  _mfeTicksHa;
        private int    _barsToMae,   _barsToMfe;
        private long   _maeTimeToMs, _mfeTimeToMs;

        private StringBuilder _pathBuf;
        private int           _pathSampleCount;
        private int           _seq;

        // ─────────────────────────────────────────────────────────────────────
        //  Construct an engine for one trade source. logDirectory blank => default
        //  workspace folder. Filename encodes basket keys (spec §5).
        // ─────────────────────────────────────────────────────────────────────
        public SentinelLogEngine(
            string account, string strategy, string stratVer, string instanceId,
            string instrument, int tier,
            string paramsJson, string paramHash,
            string logDirectory,
            bool pathSampling, int pathStride, int pathMaxSamples,
            Action<string> errorSink)
        {
            _account        = Safe(account, "unknown");
            _strategy       = Safe(strategy, "unknown");
            _stratVer       = Safe(stratVer, "0");
            _instanceId     = Safe(instanceId, _strategy + "_" + _instrumentSafe(instrument));
            _instrument     = Safe(instrument, "unknown");
            _tier           = tier == 1 ? 1 : 2;
            _paramsJson     = string.IsNullOrEmpty(paramsJson) ? "null" : paramsJson;
            _pathSampling   = pathSampling;
            _pathStride     = Math.Max(1, pathStride);
            _pathMaxSamples = Math.Max(1, pathMaxSamples);
            _errorSink      = errorSink ?? (s => { });

            // paramHash: prefer caller-supplied; else derive deterministically from the
            // params JSON. Same config => same hash (groups re-runs); different config =>
            // different hash (the A/B disambiguator, visible in both record and filename).
            string rawHash = !string.IsNullOrEmpty(paramHash)
                ? paramHash
                : (_paramsJson == "null" ? null : ShortHash(_paramsJson));
            _paramHash = rawHash == null ? "null" : Quote(rawHash);   // JSON-ready (quoted)

            // Sentinel-homed: JSONL trade logs go to <UserDataDir>\Sentinel\Log (was "MAELogger").
            string dir = string.IsNullOrWhiteSpace(logDirectory)
                ? Path.Combine(SentinelCore.SettingsDir, "Log")
                : logDirectory;
            try { Directory.CreateDirectory(dir); } catch (Exception _sx) { SentinelCore.Swallow("SentinelLogEngine.SentinelLogEngine", _sx); }

            // Descriptive, sortable, A/B-distinguishing filename (spec §5, revised):
            //   {UTCstamp}__{account}__{strategy}-{ver}__{inst}__t{tier}__p{hash}.jsonl
            // - UTC ISO-basic timestamp FIRST => lexical sort == chronological sort.
            // - "__" separates fields; single chars allowed inside fields (parse on "__").
            // - strategy-version travels with the name; tier marks zero-touch vs rich;
            //   pHASH makes different configs visibly distinct in a folder listing.
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'",
                CultureInfo.InvariantCulture);
            string hashTag = rawHash == null ? "pNA" : "p" + rawHash;
            string fname = string.Format("{0}__{1}__{2}-{3}__{4}__t{5}__{6}.jsonl",
                stamp,
                _fieldSafe(_account),
                _fieldSafe(_strategy), _fieldSafe(_stratVer),
                _instrumentSafe(_instrument),
                _tier,
                hashTag);
            _logPath = Path.Combine(dir, fname);
        }

        public string LogPath { get { return _logPath; } }
        public bool   TradeOpen { get { return _tradeOpen; } }

        // ── live-state surface (read by the dashboard via the service registry) ───
        // Exposes the in-flight trade so a monitor can show live excursion for BOTH
        // tiers. These read the same running values the JSONL uses; display-only.
        public string  LiveAccount    { get { return _account; } }
        public string  LiveStrategy   { get { return _strategy; } }
        public string  LiveInstrument { get { return _instrument; } }
        public int     LiveTier       { get { return _tier; } }
        public int     LiveDir        { get { return _dir; } }
        public double  LiveEntryPx    { get { return _entryPx; } }
        public double  LiveMaeTicks   { get { return _maeTicksRaw; } }
        public double  LiveMfeTicks   { get { return _mfeTicksRaw; } }
        public double  LiveLastPx     { get { return _lastPx; } }
        public DateTime LiveEntryTimeUtc { get { return _entryTimeUtc; } }

        private double _lastPx;  // last price seen this trade (display)

        // ── decoupled registry (no hard dependency on the service) ────────────────
        // The capture service (if loaded) subscribes these hooks so it can union tier-2
        // engine trades into its open-position registry. If no service is present, these
        // are simply null and the engine behaves exactly as before. This keeps SentinelLogEngine
        // strategy- AND service-agnostic while letting the dashboard show everything.
        public static Action<SentinelLogEngine> OnEngineTradeOpened;
        public static Action<SentinelLogEngine> OnEngineTradeClosed;

        // ─────────────────────────────────────────────────────────────────────
        //  OnEntry — call when a position opens (flat -> open). Freezes entry state.
        //  ctxJson: pre-serialized opaque context object (tier-2) or null (tier-1).
        //  atrAtEntry: ATR in price units for ATR-unit excursion, or double.NaN.
        // ─────────────────────────────────────────────────────────────────────
        public void OnEntry(int dir, int qty, double entryPriceAvg, DateTime entryTimeUtc,
            double tick, double atrAtEntry, string ctxJson)
        {
            if (_tradeOpen) return; // already tracking; ignore re-entry noise

            _tradeOpen   = true;
            _dir         = dir >= 0 ? 1 : -1;
            _qty         = qty;
            _entryPx     = entryPriceAvg;
            _entryTimeUtc= entryTimeUtc;
            _tick        = tick > 0 ? tick : 0.1;
            _atrAtEntry  = atrAtEntry;
            _ctxJson     = ctxJson; // may be null (tier-1)

            _maeTicksRaw = _mfeTicksRaw = _maeTicksHa = _mfeTicksHa = 0;
            _barsToMae = _barsToMfe = 0;
            _maeTimeToMs = _mfeTimeToMs = 0;
            _pathSampleCount = 0;
            _pathBuf = _pathSampling ? new StringBuilder(8192) : null;
            _lastPx = entryPriceAvg;

            // ── Freeze the Eye verdict as it stood at entry ───────────────────────
            // This is the keystone that lets Lens prove whether Eye-endorsed trades
            // out-earn the rest. Capture the LAST verdict regardless of staleness
            // (maxAgeSec=0 = "never stale" per SentinelCore) and store its age so
            // analysis can filter on it. _instrument is the master name ("GC") —
            // the same key Eye publishes under.
            _eyeHad = false; _eyeDir = 0; _eyeScore = double.NaN; _eyeSource = null; _eyeAgeSec = double.NaN;
            try
            {
                var ev = SentinelCore.GetEyeVerdict(_instrument, 0);
                if (ev != null)
                {
                    _eyeHad    = true;
                    _eyeDir    = ev.Direction;
                    _eyeScore  = ev.Score;
                    _eyeSource = ev.Source;
                    _eyeAgeSec = (DateTime.UtcNow - ev.UpdatedUtc).TotalSeconds;
                }
            }
            catch { /* Eye registry absent — leave the null defaults */ }

            // notify the service registry (if loaded) so the dashboard sees this trade
            var h = OnEngineTradeOpened;
            if (h != null) { try { h(this); } catch (Exception _sx) { SentinelCore.Swallow("SentinelLogEngine.OnEntry", _sx); } }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  OnBar — call each in-trade bar/sample. Updates running MAE/MFE (always)
        //  and appends a path sample (subject to stride + cap).
        //    timeUtc:    wall-clock of this sample (basket alignment, spec §3.5)
        //    barOffset:  bars since entry (intra-strategy convenience)
        //    high/low/close: bar extremes & close (raw price domain)
        //    atr:        ATR at this sample (tier-2; NaN ok) — closes replay-fidelity gap
        //    extJson:    pre-serialized opaque per-bar strategy state (tier-2) or null
        // ─────────────────────────────────────────────────────────────────────
        public void OnBar(DateTime timeUtc, int barOffset, double high, double low,
            double close, double atr, string extJson)
        {
            if (!_tradeOpen) return;

            _lastPx = close;
            long msSinceEntry = (long)(timeUtc - _entryTimeUtc).TotalMilliseconds;

            // Raw-price excursion (true MAE/MFE). Long: adverse=low<entry, fav=high>entry.
            double adverseRaw, favorableRaw;
            if (_dir > 0)
            {
                adverseRaw   = (_entryPx - low)  / _tick;
                favorableRaw = (high - _entryPx) / _tick;
            }
            else
            {
                adverseRaw   = (high - _entryPx) / _tick;
                favorableRaw = (_entryPx - low)  / _tick;
            }
            // tracked EVERY bar regardless of stride (spec §3.3 correctness guarantee)
            if (adverseRaw   > _maeTicksRaw) { _maeTicksRaw = adverseRaw;   _barsToMae = barOffset; _maeTimeToMs = msSinceEntry; }
            if (favorableRaw > _mfeTicksRaw) { _mfeTicksRaw = favorableRaw; _barsToMfe = barOffset; _mfeTimeToMs = msSinceEntry; }

            // HA/close-based excursion (chart-consistent for smoothed bar types).
            double adverseHa = _dir > 0 ? (_entryPx - close) / _tick : (close - _entryPx) / _tick;
            double favHa     = _dir > 0 ? (close - _entryPx) / _tick : (_entryPx - close) / _tick;
            if (adverseHa > _maeTicksHa) _maeTicksHa = adverseHa;
            if (favHa     > _mfeTicksHa) _mfeTicksHa = favHa;

            // Path sample (stride + cap). Each sample is TIMESTAMPED for basket alignment.
            if (_pathSampling && _pathBuf != null
                && (barOffset % _pathStride == 0)
                && _pathSampleCount < _pathMaxSamples)
            {
                if (_pathSampleCount > 0) _pathBuf.Append(',');
                _pathBuf.Append('{')
                    .Append("\"t\":").Append(Quote(Iso(timeUtc))).Append(',')
                    .Append("\"o\":").Append(barOffset).Append(',')
                    .Append("\"hi\":").Append(F(high)).Append(',')
                    .Append("\"lo\":").Append(F(low)).Append(',')
                    .Append("\"c\":").Append(F(close)).Append(',')
                    .Append("\"maeR\":").Append(F(_maeTicksRaw)).Append(',')
                    .Append("\"mfeR\":").Append(F(_mfeTicksRaw)).Append(',')
                    .Append("\"atr\":").Append(F(atr));
                if (!string.IsNullOrEmpty(extJson))
                    _pathBuf.Append(",\"ext\":").Append(extJson);
                _pathBuf.Append('}');
                _pathSampleCount++;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  OnExit — call when the position goes flat. Finalizes MAE/MFE, writes one
        //  JSONL line, and resets for the next trade. exitReason per spec §3.2 enum.
        // ─────────────────────────────────────────────────────────────────────
        public void OnExit(double exitPriceAvg, DateTime exitTimeUtc, string exitReason)
        {
            if (!_tradeOpen) return;

            double pnlTicks = _dir > 0 ? (exitPriceAvg - _entryPx) / _tick
                                       : (_entryPx - exitPriceAvg) / _tick;

            double atrTicks = (!double.IsNaN(_atrAtEntry) && _atrAtEntry > 0)
                ? _atrAtEntry / _tick : double.NaN;
            string maeAtr = !double.IsNaN(atrTicks) ? F(_maeTicksRaw / atrTicks) : "null";
            string mfeAtr = !double.IsNaN(atrTicks) ? F(_mfeTicksRaw / atrTicks) : "null";

            var sb = new StringBuilder(16384);
            sb.Append('{')
              // identity block (spec §3.1)
              .Append("\"schema\":").Append(Quote(SchemaVersion)).Append(',')
              .Append("\"seq\":").Append(++_seq).Append(',')
              .Append("\"tier\":").Append(_tier).Append(',')
              .Append("\"account\":").Append(Quote(_account)).Append(',')
              .Append("\"strategy\":").Append(Quote(_strategy)).Append(',')
              .Append("\"stratVer\":").Append(Quote(_stratVer)).Append(',')
              .Append("\"instanceId\":").Append(Quote(_instanceId)).Append(',')
              .Append("\"inst\":").Append(Quote(_instrument)).Append(',')
              .Append("\"engineVer\":").Append(Quote(EngineVersion)).Append(',')
              .Append("\"params\":").Append(_paramsJson).Append(',')
              .Append("\"paramHash\":").Append(_paramHash).Append(',')
              // trade block (spec §3.2)
              .Append("\"dir\":").Append(_dir).Append(',')
              .Append("\"qty\":").Append(_qty).Append(',')
              .Append("\"entryTime\":").Append(Quote(Iso(_entryTimeUtc))).Append(',')
              .Append("\"exitTime\":").Append(Quote(Iso(exitTimeUtc))).Append(',')
              .Append("\"entryPx\":").Append(F(_entryPx)).Append(',')
              .Append("\"exitPx\":").Append(F(exitPriceAvg)).Append(',')
              .Append("\"pnlTicks\":").Append(F(pnlTicks)).Append(',')
              .Append("\"exitReason\":").Append(Quote(Safe(exitReason, "unknown"))).Append(',')
              // excursion block (spec §3.3)
              .Append("\"maeTicksRaw\":").Append(F(_maeTicksRaw)).Append(',')
              .Append("\"mfeTicksRaw\":").Append(F(_mfeTicksRaw)).Append(',')
              .Append("\"maeTicksHa\":").Append(F(_maeTicksHa)).Append(',')
              .Append("\"mfeTicksHa\":").Append(F(_mfeTicksHa)).Append(',')
              .Append("\"maeAtr\":").Append(maeAtr).Append(',')
              .Append("\"mfeAtr\":").Append(mfeAtr).Append(',')
              .Append("\"barsToMae\":").Append(_barsToMae).Append(',')
              .Append("\"barsToMfe\":").Append(_barsToMfe).Append(',')
              .Append("\"maeTimeToMs\":").Append(_maeTimeToMs).Append(',')
              .Append("\"mfeTimeToMs\":").Append(_mfeTimeToMs);

            // eye block (schema 1.1) — the Eye verdict as it stood at ENTRY. The keystone
            // for proving Eye's edge: partition trades by eyeAligned/eyeScore in Lens and
            // compare expectancy. eyeAligned = did Eye qualify THIS trade's direction?
            sb.Append(",\"eyeHad\":").Append(_eyeHad ? "true" : "false")
              .Append(",\"eyeDir\":").Append(_eyeHad ? _eyeDir.ToString(CultureInfo.InvariantCulture) : "null")
              .Append(",\"eyeScore\":").Append(F(_eyeScore))
              .Append(",\"eyeSource\":").Append(_eyeSource == null ? "null" : Quote(_eyeSource))
              .Append(",\"eyeAgeSec\":").Append(F(_eyeAgeSec))
              .Append(",\"eyeAligned\":").Append(_eyeHad ? ((_eyeDir == _dir) ? "true" : "false") : "null");

            // context block (spec §3.4) — tier-2 only; opaque pass-through
            if (!string.IsNullOrEmpty(_ctxJson))
                sb.Append(",\"ctx\":").Append(_ctxJson);

            // path block (spec §3.5) — optional
            if (_pathSampling && _pathBuf != null)
                sb.Append(",\"path\":[").Append(_pathBuf.ToString()).Append(']');

            sb.Append('}');

            try
            {
                File.AppendAllText(_logPath, sb.ToString() + Environment.NewLine);
            }
            catch (Exception ex)
            {
                _errorSink("[SentinelLogEngine] write failed: " + ex.Message);
            }

            // reset for next trade
            _tradeOpen = false;
            _dir = 0;
            _pathBuf = null;
            _ctxJson = null;

            // notify the service registry (if loaded) that this trade closed
            var hc = OnEngineTradeClosed;
            if (hc != null) { try { hc(this); } catch (Exception _sx) { SentinelCore.Swallow("SentinelLogEngine.OnExit", _sx); } }
        }

        // ── JSON helpers (compact, invariant-culture) ─────────────────────────

        // number -> compact invariant string; NaN/Inf -> JSON null
        private static string F(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "null";
            return Math.Round(v, 4).ToString(CultureInfo.InvariantCulture);
        }

        // ISO-8601 UTC (round-trip "o" format), normalized to UTC
        private static string Iso(DateTime dt)
        {
            return dt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        }

        // wrap a string as a JSON string literal with minimal escaping
        private static string Quote(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    default:   sb.Append(c);      break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        private static string Safe(string s, string fallback)
        {
            return string.IsNullOrWhiteSpace(s) ? fallback : s;
        }

        // sanitize an instrument name for a filename: keep only alphanumerics, so
        // "GC 08-26" -> "GC0826" (compact, no inner separators to confuse "__" parsing).
        private static string _instrumentSafe(string inst)
        {
            if (string.IsNullOrWhiteSpace(inst)) return "unknown";
            var sb = new StringBuilder(inst.Length);
            foreach (char c in inst)
                if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.Length > 0 ? sb.ToString() : "unknown";
        }

        // sanitize a filename field: strip the field separator and filesystem-hostile
        // chars so "__" reliably marks field boundaries when parsing names back apart.
        private static string _fieldSafe(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "unknown";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (c == '_' || c == ' ' || c == '/' || c == '\\' || c == ':'
                    || c == '*' || c == '?' || c == '"' || c == '<' || c == '>' || c == '|')
                    sb.Append('-');
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }

        // Deterministic short hash (FNV-1a, 32-bit -> 6 hex chars). Stable across runs
        // and machines (unlike string.GetHashCode), so identical params always yield the
        // same tag — re-runs group, different configs differ. Used for the filename pHASH
        // and the record paramHash when the caller doesn't supply one.
        private static string ShortHash(string s)
        {
            unchecked
            {
                const uint offset = 2166136261;
                const uint prime  = 16777619;
                uint h = offset;
                foreach (char c in s)
                {
                    h ^= (byte)(c & 0xFF);
                    h *= prime;
                    h ^= (byte)((c >> 8) & 0xFF);
                    h *= prime;
                }
                return h.ToString("x8", CultureInfo.InvariantCulture).Substring(0, 6);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  CtxBuilder — tiny helper for tier-2 strategies to assemble an opaque ctx or
    //  ext JSON object without hand-managing commas/quoting. The engine never parses
    //  these; it stores them verbatim. Strategies define whatever shape they like.
    //
    //  USAGE:
    //    var c = new CtxBuilder();
    //    c.Add("ratio", confluenceRatio).Add("htfDir", htfDir).Add("wcState", wcState);
    //    engine.OnEntry(..., c.ToJson());
    // ─────────────────────────────────────────────────────────────────────────
    public sealed class CtxBuilder
    {
        private readonly StringBuilder _sb = new StringBuilder("{");
        private bool _first = true;

        private void Sep()
        {
            if (!_first) _sb.Append(',');
            _first = false;
        }

        public CtxBuilder Add(string key, double v)
        {
            Sep();
            string val = (double.IsNaN(v) || double.IsInfinity(v))
                ? "null"
                : Math.Round(v, 4).ToString(CultureInfo.InvariantCulture);
            _sb.Append('"').Append(key).Append("\":").Append(val);
            return this;
        }

        public CtxBuilder Add(string key, int v)
        {
            Sep();
            _sb.Append('"').Append(key).Append("\":").Append(v);
            return this;
        }

        public CtxBuilder Add(string key, bool v)
        {
            Sep();
            _sb.Append('"').Append(key).Append("\":").Append(v ? "true" : "false");
            return this;
        }

        public CtxBuilder Add(string key, string v)
        {
            Sep();
            _sb.Append('"').Append(key).Append("\":");
            if (v == null) { _sb.Append("null"); return this; }
            _sb.Append('"');
            foreach (char c in v)
            {
                if (c == '"' || c == '\\') _sb.Append('\\');
                _sb.Append(c);
            }
            _sb.Append('"');
            return this;
        }

        public string ToJson()
        {
            return _sb.ToString() + "}";
        }
    }
}
