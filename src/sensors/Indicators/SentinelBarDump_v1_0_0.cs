// ─────────────────────────────────────────────────────────────────────────────
// This Source Code Form is subject to the terms of the Mozilla Public License,
// v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
// one at https://mozilla.org/MPL/2.0/.
//
// Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
// ─────────────────────────────────────────────────────────────────────────────
// ═════════════════════════════════════════════════════════════════════════════
//  SentinelBarDump — the EQUIVALENCE GATE's ground truth
//  File: SentinelBarDump_v1_0_0.cs   ·   Version v1.1.0   ·   Schema bars.2   ·   namespace …Indicators.Sentinel
// ─────────────────────────────────────────────────────────────────────────────
//  WHAT THIS IS
//    A transcript of what NinjaTrader's bar construction ACTUALLY produced: every completed bar's
//    time + OHLC + volume, unthrottled, written to JSONL. It exists to answer exactly one question —
//    **does the offline harness build the same bars NT does?** Load it on a chart, and the file it
//    writes is the answer key.
//
//  WHY IT HAD TO BE BUILT (2026-07-26)
//    The harness's first equivalence target is SentinelTide, and the plan was to compare against the
//    `[Sentinel:Tide]` lines in sentinel.log. That log cannot do the job, and the reason is worth
//    recording so nobody tries again:
//      • it is throttled to one line per 10 WALL-seconds, so a rebuild logs ~8% of bars — and not a
//        random 8%, since bar time advances thousands of times faster than wall time;
//      • it is REALTIME-GATED, so a static historical chart is silent;
//      • it carries NO BAR TIMESTAMP, only the wall-clock instant of the log write;
//      • `_lastLog` is per-instance, so several bars-type instances interleave and the `N bars`
//        session ordinal jumps between them (measured: 34 lines total, ordinals 1 / 429 / 58, with
//        four byte-identical lines 0.24s apart).
//    A sample with no join key is not ground truth. This writes every bar, with its own time.
//
//  ⚠ DELIBERATE EXCEPTION: NO REALTIME GATE
//    Every recorder in this suite gates on `State == State.Realtime`, because a seam has no as-of
//    semantics and a replayed verdict stamped onto an old bar is lookahead contamination. That
//    reasoning does not apply here and applying it anyway would destroy the tool: a bar transcript
//    contains no labels, no seam reads and nothing forward-looking — it records only what a bar
//    ALREADY IS at its own close. Historical bars are precisely what the gate needs to compare.
//    Every row carries `rt` (true = built live, false = built during the historical rebuild) so the
//    Lab can split them if it ever matters. This also fixes the defect flagged in FLOWBARS §3b:
//    "G1 must be checkable on a static chart."
//
//  BAR-TYPE AGNOSTIC ON PURPOSE
//    It reads Time/Open/High/Low/Close/Volume, so it works unchanged on Tide, TBars, TbarsCount,
//    Flux, Drift, Lattice, Effort — and on stock NT bar types. Every future harness equivalence
//    gate uses this same file; it is not a Tide-specific probe.
//
//  NOT A SENSOR — the …State publish protocol does NOT apply
//    Design system §9 item 6 requires every new signal/regime/bias/context indicator to publish a
//    `…State` seam and be wired into the Council. This is a diagnostic exporter: it has no opinion
//    about direction, nothing to fuse, and no consumer inside the platform. Publishing a seam would
//    add a publisher to the very seam-store whose failure modes this tool exists to investigate.
//
//  HOW TO USE IT
//    1. Add it to the chart whose bar type you want to reproduce. It starts writing immediately —
//       the historical rebuild alone gives a full answer key, no replay needed.
//    2. The file lands in Sentinel\Harness\bars\<stamp>__<inst>__<bartag>.jsonl. Line 1 is a HEADER
//       object (schema, instrument, bar tag, tick size, bars-period values, versions) so the file is
//       self-describing and the Lab never has to guess the tick size or the period.
//    3. Diff it against the harness with Lab\harness\equivalence.py.
//
//  CHANGELOG
//    v1.1.0 (2026-08-09) — SCHEMA bars.2: the header now carries `resetOnNewTradingDay`
//           (NinjaTrader's `Bars.IsResetOnNewTradingDay`, the Data Series "Break at EOD", the Python
//           port's `reset_on_new_session`). It changes where EVERY bar boundary falls, so without it
//           the two columns cannot honestly be compared — it was a *stated precondition* of the
//           bar-type parity gate rather than a compared field, which means a settings mismatch would
//           have surfaced as a bar disagreement and been debugged as a porting bug.
//           ⛔ It cannot be set instead of recorded: measured on two NT builds, the property is
//           READ-ONLY and appears in neither `BarsProperties`, `BarsPeriod`, nor any chart template.
//           Recording it is the only defence there is. 1 = on, 0 = off, **-1 = UNKNOWN** (not the
//           same claim as off — the gate must treat -1 as unknown, never as "EOD was off").
//           ⚠ Deliberately edited IN PLACE rather than forked to `_v1_1_0`: the type name is the
//           indicator's serialization identity, and a rename would orphan it in every chart template
//           and workspace that already names it. The version that consumers key on is `schema`.
//    v1.0.0 (2026-07-26) — initial. Every-bar JSONL transcript, self-describing header, buffered
//           writes with periodic flush, no realtime gate (see above), glass card + label remover.
// ═════════════════════════════════════════════════════════════════════════════

using System;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Text;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.AddOns.Sentinel;

namespace NinjaTrader.NinjaScript.Indicators.Sentinel
{
    public class SentinelBarDump_v1_0_0 : Indicator
    {
        private const string SchemaVer = "bars.2";
        private const string DumpVer   = "1.1.0";

        private string        _logPath;
        private StringBuilder _buf;
        private int           _pending;
        private long          _written;
        private bool          _writerDead;
        private bool          _writeFailed;
        private string        _lastErr;

        // card state
        private SentinelSkin.Painter _sp;
        private bool     _liveBars;
        private DateTime _lastBarTime;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name        = "Sentinel Bar Dump v1.1.0";
                Description = "Writes EVERY completed bar (time + OHLC + volume) to JSONL as the answer key for the "
                            + "offline harness equivalence gate. Bar-type agnostic, unthrottled, historical bars "
                            + "included on purpose. No orders, no seam, no opinion. Writes Sentinel\\Harness\\bars\\.";
                IsOverlay                = true;
                Calculate                = Calculate.OnBarClose;   // one call per COMPLETED bar — the forming bar is not a bar
                DrawOnPricePanel         = true;
                IsSuspendedWhileInactive = false;
                ShowInfo                 = true;
                CardCorner               = SentinelCardCorner.TopRight;
                FlushEveryBars           = 500;
                ShowIndicatorLabel       = false;
            }
            else if (State == State.DataLoaded)
            {
                if (!ShowIndicatorLabel) Name = string.Empty;

                _buf = new StringBuilder(1 << 16);
                try
                {
                    string dir = Path.Combine(SentinelCore.SettingsDir, "Harness", "bars");
                    Directory.CreateDirectory(dir);
                    string stamp = DateTime.Now.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture);
                    _logPath = Path.Combine(dir, stamp + "__" + InstName() + "__" + BarTag() + ".jsonl");
                    WriteHeader();
                }
                catch (Exception ex)
                {
                    _logPath = null; _writerDead = true; _lastErr = ex.Message;
                    try
                    {
                        SentinelCore.Log("BarDump", "WRITER DEAD — could not open Sentinel\\Harness\\bars; NOTHING will "
                            + "be recorded this session (" + ex.Message + "). The card shows NO REC.");
                    }
                    catch (Exception _sx) { SentinelCore.Swallow("SentinelBarDump.OnStateChange", _sx); }
                }
            }
            else if (State == State.Terminated)
            {
                Flush();
                if (_sp != null)
                {
                    try { _sp.Dispose(); } catch (Exception _sx) { SentinelCore.Swallow("SentinelBarDump.OnStateChange", _sx); }
                    _sp = null;
                }
                try { SentinelSkin.CardLayout.Release(this); } catch (Exception _sx) { SentinelCore.Swallow("SentinelBarDump.OnStateChange", _sx); }
            }
        }

        /// <summary>Line 1 = everything a reader needs to interpret the rows without guessing.
        /// The tick size in particular: heights and bodies are meaningless in ticks without it, and
        /// a Lab-side default is exactly the kind of silent assumption this project keeps paying for.</summary>
        private void WriteHeader()
        {
            var sb = new StringBuilder(512);
            sb.Append("{\"hdr\":1")
              .Append(",\"schema\":").Append(Q(SchemaVer))
              .Append(",\"dumpVer\":").Append(Q(DumpVer))
              .Append(",\"coreVer\":").Append(Q(SafeCoreVer()))
              .Append(",\"inst\":").Append(Q(InstName()))
              .Append(",\"bartype\":").Append(Q(BarTag()))
              .Append(",\"barLabel\":").Append(Q(SafeFriendly()))
              .Append(",\"tickSize\":").Append(F(SafeTickSize()))
              .Append(",\"pointValue\":").Append(F(SafePointValue()))
              .Append(",\"periodType\":").Append(F(SafePeriodTypeId()))
              .Append(",\"periodValue\":").Append(F(SafePeriodValue()))
              .Append(",\"periodValue2\":").Append(F(SafePeriodValue2()))
              .Append(",\"baseValue\":").Append(F(SafeBaseValue()))
              .Append(",\"tradingHours\":").Append(Q(SafeTradingHours()))
              // Schema bars.2 — see SafeResetOnNewTradingDay(). 1 = on, 0 = off, -1 = UNKNOWN.
              .Append(",\"resetOnNewTradingDay\":").Append(F(SafeResetOnNewTradingDay()))
              .Append(",\"openedUtc\":").Append(Q(Iso(DateTime.UtcNow)))
              .Append("}");
            Append(sb.ToString());
        }

        protected override void OnBarUpdate()
        {
            if (_writerDead || CurrentBar < 0) return;

            _liveBars = State == State.Realtime;
            _lastBarTime = Time[0];

            // One row per completed bar. `t` is the bar's OWN close time in UTC -- the join key the
            // Tide log never had. Prices are emitted raw (already on the tick grid); the Lab derives
            // height/body from them and the header's tickSize rather than trusting a second source.
            var sb = new StringBuilder(160);
            sb.Append("{\"i\":").Append(CurrentBar.ToString(CultureInfo.InvariantCulture))
              .Append(",\"t\":").Append(Q(Iso(Time[0])))
              .Append(",\"o\":").Append(F(Open[0]))
              .Append(",\"h\":").Append(F(High[0]))
              .Append(",\"l\":").Append(F(Low[0]))
              .Append(",\"c\":").Append(F(Close[0]))
              .Append(",\"v\":").Append(F(Volume[0]))
              .Append(",\"rt\":").Append(_liveBars ? "true" : "false")
              .Append(",\"newSession\":").Append(SafeFirstOfSession() ? "true" : "false")
              .Append("}");
            Append(sb.ToString());
        }

        private void Append(string json)
        {
            if (_logPath == null || _buf == null) return;
            _buf.Append(json).Append(Environment.NewLine);
            _written++;
            _pending++;
            // Buffered because a historical rebuild can produce six figures of bars and one file
            // append per bar would put disk latency inside the bar path. Flushed often enough that
            // a crash costs a few hundred rows, not the run.
            if (_pending >= Math.Max(1, FlushEveryBars)) Flush();
        }

        private void Flush()
        {
            if (_logPath == null || _buf == null || _buf.Length == 0) return;
            try
            {
                File.AppendAllText(_logPath, _buf.ToString());
                _buf.Length = 0;
                _pending = 0;
                _writeFailed = false;
            }
            catch (Exception ex)
            {
                // Say so on the card rather than silently producing a short file -- a truncated
                // answer key that looks complete is worse than no answer key.
                _writeFailed = true;
                _lastErr = ex.Message;
                SentinelCore.Swallow("SentinelBarDump.Flush", ex);
            }
        }

        // ── the Sentinel glass card ─────────────────────────────────────────────────────────────
        protected override void OnRender(NinjaTrader.Gui.Chart.ChartControl chartControl, NinjaTrader.Gui.Chart.ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);
            if (!ShowInfo || RenderTarget == null || ChartPanel == null) return;
            try
            {
                if (_sp == null) _sp = new SentinelSkin.Painter();
                _sp.Begin(RenderTarget);

                const float cw = 258f, ch = 104f;
                var slot = SentinelSkin.CardLayout.Place(this, ChartPanel,
                    ChartPanel.X, ChartPanel.Y, ChartPanel.W, ChartPanel.H, CardCorner, cw, ch);

                bool dead = _writerDead || _writeFailed;
                bool live = !dead;
                var edge = dead ? SentinelSkin.CDown : (_written > 0 ? SentinelSkin.CAccent : SentinelSkin.CLine);
                var r = _sp.Card(slot.X, slot.Y, cw, ch, edge);

                _sp.Dot(r.Left + 5f, r.Top + 8f, live ? SentinelSkin.CAccent : SentinelSkin.CMute, live);
                _sp.Text("SENTINEL BAR DUMP", r.Left + 16f, r.Top, r.Width - 66f, 16f, SentinelSkin.CInk, 11f, true);
                string st = dead ? "NO REC" : (_liveBars ? "LIVE" : "REBUILD");
                var stCol = dead ? SentinelSkin.CDown : (_liveBars ? SentinelSkin.CAccent : SentinelSkin.CInk2);
                _sp.Pill(st, r.Right, r.Top - 1f, stCol);

                var lead = SharpDX.DirectWrite.TextAlignment.Leading;

                _sp.Text("BARS WRITTEN", r.Left, r.Top + 24f, 130f, 12f, SentinelSkin.CMute, 9f, true);
                _sp.Text(_written.ToString("N0", CultureInfo.InvariantCulture),
                    r.Left, r.Top + 32f, 130f, 28f, dead ? SentinelSkin.CDown : SentinelSkin.CAccent, 24f);

                _sp.Text("BUFFERED", r.Left + 140f, r.Top + 24f, 110f, 12f, SentinelSkin.CMute, 9f, true);
                _sp.Text(_pending.ToString(CultureInfo.InvariantCulture),
                    r.Left + 140f, r.Top + 33f, 110f, 20f, SentinelSkin.CInk2, 15f, true);

                if (dead)
                {
                    _sp.Text(_lastErr == null ? "write failed" : Trunc(_lastErr, 46),
                        r.Left, r.Top + 64f, r.Width, 14f, SentinelSkin.CDown, 9.5f, false, lead, true);
                }
                else
                {
                    _sp.Text("last bar  " + _lastBarTime.ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                        r.Left, r.Top + 64f, r.Width, 14f, SentinelSkin.CInk2, 9.5f, false, lead, true);
                }

                _sp.Text(InstName() + " · " + SafeFriendly(),
                    r.Left, r.Top + 80f, r.Width, 12f, SentinelSkin.CMute, 9f, false, lead, true);

                _sp.End();
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelBarDump.OnRender", _sx); }
        }

        // ── small safe accessors (every one of these can throw on a half-built chart) ───────────
        private string InstName()
        {
            try { return Instrument != null && Instrument.MasterInstrument != null ? Instrument.MasterInstrument.Name : "unknown"; }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelBarDump.InstName", _sx); return "unknown"; }
        }

        private string BarTag()
        {
            try
            {
                string t = SentinelCore.BarTag(BarsPeriod);
                string ln = SentinelCore.LaneOf(ChartControl);
                return string.IsNullOrEmpty(ln) ? t : t + "@" + ln;
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelBarDump.BarTag", _sx); return "unknown"; }
        }

        private string SafeFriendly()
        {
            try { return SentinelCore.FriendlyBartag(BarTag()); }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelBarDump.Friendly", _sx); return BarTag(); }
        }
        private string SafeCoreVer()
        {
            try { return SentinelCore.Version; }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelBarDump.CoreVer", _sx); return "?"; }
        }
        private double SafeTickSize()
        {
            try { return Instrument.MasterInstrument.TickSize; }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelBarDump.TickSize", _sx); return double.NaN; }
        }
        private double SafePointValue()
        {
            try { return Instrument.MasterInstrument.PointValue; }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelBarDump.PointValue", _sx); return double.NaN; }
        }
        private double SafePeriodTypeId()
        {
            try { return (int)BarsPeriod.BarsPeriodType; }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelBarDump.PeriodType", _sx); return double.NaN; }
        }
        private double SafePeriodValue()
        {
            try { return BarsPeriod.Value; }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelBarDump.PeriodValue", _sx); return double.NaN; }
        }
        private double SafePeriodValue2()
        {
            try { return BarsPeriod.Value2; }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelBarDump.PeriodValue2", _sx); return double.NaN; }
        }
        private double SafeBaseValue()
        {
            try { return BarsPeriod.BaseBarsPeriodValue; }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelBarDump.BaseValue", _sx); return double.NaN; }
        }
        private string SafeTradingHours()
        {
            try { return Bars != null && Bars.TradingHours != null ? Bars.TradingHours.Name : "?"; }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelBarDump.TradingHours", _sx); return "?"; }
        }

        // ⭐⭐ "BREAK AT EOD", AND WHY IT MUST BE RECORDED RATHER THAN ASSUMED.
        // This is NinjaTrader's `Bars.IsResetOnNewTradingDay`, and the Python port's
        // `reset_on_new_session`. It changes where EVERY bar boundary falls, so the two columns
        // cannot be compared without it — and until now the dump did not carry it, which made it a
        // *stated precondition* of the bar-type parity gate instead of a compared field
        // (`Azimuth/bars/renko.py`: "a field only one side can see cannot be part of a shared
        // identity"). ⇒ A settings mismatch would surface as a BAR DISAGREEMENT and be debugged as a
        // porting bug.
        // ⛔ It cannot simply be set to a known value either: measured 2026-08-09 on two NT builds,
        //    `IsResetOnNewTradingDay` is READ-ONLY, and it appears nowhere in `BarsProperties`, in
        //    `BarsPeriod`, or in any chart template. **Recording it is the only defence available.**
        // Emitted as a nullable-ish flag: -1 means "could not read", which is NOT the same claim as
        // false, and the gate must treat it as unknown rather than as "EOD off".
        private double SafeResetOnNewTradingDay()
        {
            try { return Bars != null ? (Bars.IsResetOnNewTradingDay ? 1 : 0) : -1; }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelBarDump.ResetOnNewTradingDay", _sx); return -1; }
        }
        private bool SafeFirstOfSession()
        {
            try { return Bars.IsFirstBarOfSession; }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelBarDump.FirstOfSession", _sx); return false; }
        }

        private static string Trunc(string s, int n) { return s == null ? "" : (s.Length <= n ? s : s.Substring(0, n)); }
        private static string F(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "null";
            return Math.Round(v, 6).ToString(CultureInfo.InvariantCulture);
        }
        private static string Iso(DateTime dt) { return dt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture); }
        private static string Q(string s)
        {
            if (s == null) return "null";
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        #region Properties
        [NinjaScriptProperty]
        [Display(Name = "Show info card", Description = "Draw the Sentinel glass-card readout (off = pure headless dumper).", GroupName = "Bar Dump", Order = 1)]
        public bool ShowInfo { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Card corner", Description = "Which chart corner the card docks to. Cards in the same corner auto-stack (never overlap).", GroupName = "Bar Dump", Order = 2)]
        public SentinelCardCorner CardCorner { get; set; }

        // NOT a [NinjaScriptProperty] — serializes to the workspace + shows in F6 but stays OUT of the
        // generated constructor (no codegen churn).
        [Display(Name = "Flush every N bars", Description = "Write the buffer to disk every N bars. Lower = less lost on a crash, more disk churn during a rebuild.", GroupName = "Bar Dump", Order = 3)]
        public int FlushEveryBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show indicator label", Description = "Show NinjaTrader's chart name label. Sentinel default = OFF (clean chart); turn on to restore it.", GroupName = "Sentinel", Order = 100)]
        public bool ShowIndicatorLabel { get; set; }
        #endregion
    }
}
