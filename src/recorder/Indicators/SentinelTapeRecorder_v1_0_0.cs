// ─────────────────────────────────────────────────────────────────────────────
// This Source Code Form is subject to the terms of the Mozilla Public License,
// v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
// one at https://mozilla.org/MPL/2.0/.
//
// Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
// ─────────────────────────────────────────────────────────────────────────────
// ═════════════════════════════════════════════════════════════════════════════
//  SentinelTapeRecorder — capture a REAL order book, live, because the replay store has none
//  File: SentinelTapeRecorder_v1_0_0.cs   ·   Version v1.0.0   ·   Schema = gbNRDtoCSV L1/L2
//  namespace …Indicators.Sentinel
// ─────────────────────────────────────────────────────────────────────────────
//  WHY THIS EXISTS (2026-07-26) — read this before "improving" it
//    Execution research needs a book: the true touch, the size resting on it, and where you would
//    sit in the queue. NinjaTrader's MARKET REPLAY store cannot supply one. Measured on the
//    gbNRDtoCSV export of GC 08-26 (memory `replay-depth-not-fit-for-execution`):
//      • 0.00% of 103,157 trades printed INSIDE the spread — structurally impossible in a real book
//        when three empty price levels sit between bid and ask;
//      • median size at the touch was 1 CONTRACT (real front-month GC holds dozens);
//      • the spread was 4-5 ticks and stayed 5 ticks in QUIET moments (530,310 samples, no trade for
//        >5 s), so staleness and row-ordering cannot explain it;
//      • the L1 Bid/Ask latch and the fully reconstructed L2 ladder agree exactly (20.1% of trades
//        outside the book either way) ⇒ both encodings share one degraded source.
//    The reader was not the problem: `Lab\harness\l2book.py` reconstructs those ladders at 100.00%
//    monotonic with zero malformed ops. The DATA is the problem.
//
//    Real-time depth is genuine — it is only the recorded replay store that is degraded. So the fix
//    is to capture the live tape ourselves, starting now, because every day not recording is a day
//    of book data that cannot be bought back later.
//
//  ⚠ DELIBERATE: THIS ONE **IS** REALTIME-GATED — the opposite of SentinelBarDump
//    BarDump deliberately records historical bars, because a bar transcript contains nothing
//    forward-looking and the rebuild is exactly what its gate needs. The reasoning inverts here.
//    Historical/replay depth IS the degraded data this tool exists to escape; recording it would
//    re-poison the well with the very artifact we just spent a day proving is unusable. Nothing is
//    written until `State == State.Realtime`, and the card says WAITING until then.
//
//  FORMAT — byte-identical to gbNRDtoCSV, on purpose
//    Semicolon-delimited, NO header row, NT-LOCAL timestamps, sub-second as 100-ns ticks:
//      L1;{mdType};{yyyyMMddHHmmss};{subsec100ns};{price};{volume}
//      L2;{kind};{yyyyMMddHHmmss};{subsec100ns};{op};{pos};{maker};{price};{volume}
//    mdType/kind = NT MarketDataType (0=Ask 1=Bid 2=Last).  op = NT Operation (0=Add 1=Update
//    2=Remove).  pos = depth position, 0 = top of book.
//    ⇒ `nrdcsv.iter_l1`, `nrdcsv.iter_l2` and `l2book.py` read these files with ZERO changes, and
//    `CSV_ROOT` can simply be pointed at the tape directory. Matching an existing format beat
//    inventing a better one: it makes every tool already written work on day one.
//    Line 1 is a `#META;` comment — both parsers filter on the `L1;`/`L2;` prefix, so it is
//    invisible to them while keeping the file self-describing.
//
//  LAYOUT + ROTATION
//    Sentinel\Tape\<Instrument FullName>\yyyyMMdd.csv — one file per NT-LOCAL date, mirroring the
//    export's own layout so the Lab's day-file logic (`regime_study`, `noise_floor`) works unchanged.
//
//  ⚠ DISK. This is not a small file. GC alone produced 6.7M depth events in one day; expect roughly
//    **300 MB per instrument per day** uncompressed, and L2 is ~3.5x the row count of L1. Set
//    `RecordDepth = false` if you only need trades. Watch free space before leaving it unattended.
//
//  NOT A SENSOR — the …State publish protocol does NOT apply
//    Design system §9 item 6 requires new signal/regime/bias/context indicators to publish a `…State`
//    seam and wire into the Council. This is a capture device: no opinion, nothing to fuse, no
//    in-platform consumer. It writes files and draws a card.
//
//  HOW TO USE IT
//    1. Put it on a chart of the instrument you want taped. Any bar type — it never reads bars.
//    2. Leave the chart open and connected. The card shows LIVE + running row counts.
//    3. Point the Lab at Sentinel\Tape\ and every existing harness reader just works.
//
//  CHANGELOG
//    v1.0.0 (2026-07-26) — initial. Live L1 + L2 capture in gbNRDtoCSV format, per-day rotation,
//           realtime-gated on purpose, buffered writes, glass card + label remover.
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
    public class SentinelTapeRecorder_v1_0_0 : Indicator
    {
        private const string TapeVer = "1.0.0";

        private string        _dir;
        private string        _path;
        private string        _day;          // yyyyMMdd of the file currently open
        private StringBuilder _buf;
        private int           _pending;
        private long          _l1;
        private long          _l2;
        private bool          _writerDead;
        private bool          _writeFailed;
        private string        _lastErr;
        private bool          _live;
        private DateTime      _lastEvent;
        private double        _bid, _ask;    // top of book, for the card only

        // card
        private SentinelSkin.Painter _sp;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name        = "Sentinel Tape Recorder v1.0.0";
                Description = "Captures the LIVE tape — every L1 trade/quote and every L2 depth event — to "
                            + "Sentinel\\Tape\\ in gbNRDtoCSV format, so the offline harness reads it unchanged. "
                            + "Exists because NT's replay depth store is not a usable order book. Realtime only. "
                            + "⚠ ~300 MB per instrument per day.";
                IsOverlay                = true;
                Calculate                = Calculate.OnBarClose;   // bars are irrelevant here; keep OnBarUpdate cheap
                DrawOnPricePanel         = true;
                IsSuspendedWhileInactive = false;                  // MUST stay false or capture stops when unfocused
                ShowInfo                 = true;
                CardCorner               = SentinelCardCorner.TopRight;
                RecordDepth              = true;
                FlushEveryRows           = 4000;
                ShowIndicatorLabel       = false;
            }
            else if (State == State.DataLoaded)
            {
                if (!ShowIndicatorLabel) Name = string.Empty;

                _buf = new StringBuilder(1 << 18);
                try
                {
                    _dir = Path.Combine(SentinelCore.SettingsDir, "Tape", SafeFullName());
                    Directory.CreateDirectory(_dir);
                }
                catch (Exception ex)
                {
                    _dir = null; _writerDead = true; _lastErr = ex.Message;
                    try
                    {
                        SentinelCore.Log("TapeRec", "WRITER DEAD — could not create Sentinel\\Tape\\" + SafeFullName()
                            + "; NOTHING will be recorded this session (" + ex.Message + "). The card shows NO REC.");
                    }
                    catch (Exception _sx) { SentinelCore.Swallow("SentinelTapeRecorder.OnStateChange", _sx); }
                }
            }
            else if (State == State.Terminated)
            {
                Flush();
                if (_sp != null)
                {
                    try { _sp.Dispose(); } catch (Exception _sx) { SentinelCore.Swallow("SentinelTapeRecorder.OnStateChange", _sx); }
                    _sp = null;
                }
                try { SentinelSkin.CardLayout.Release(this); } catch (Exception _sx) { SentinelCore.Swallow("SentinelTapeRecorder.OnStateChange", _sx); }
            }
        }

        protected override void OnBarUpdate() { /* deliberately empty — this tool never reads bars */ }

        // ── L1: trades and top-of-book quotes ───────────────────────────────────────────────────
        protected override void OnMarketData(MarketDataEventArgs e)
        {
            if (_writerDead || e == null) return;
            // THE GATE. Replay/historical depth is the degraded data this tool exists to escape.
            if (State != State.Realtime) return;
            _live = true;

            try
            {
                int kind = (int)e.MarketDataType;
                if (kind == 1) _bid = e.Price;
                else if (kind == 0) _ask = e.Price;
                _lastEvent = e.Time;

                var sb = new StringBuilder(48);
                sb.Append("L1;").Append(kind.ToString(CultureInfo.InvariantCulture)).Append(';');
                Stamp(sb, e.Time);
                sb.Append(P(e.Price)).Append(';').Append(e.Volume.ToString(CultureInfo.InvariantCulture));
                Append(sb.ToString());
                _l1++;
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelTapeRecorder.OnMarketData", _sx); }
        }

        // ── L2: the whole point — real depth, with position and size at every level ─────────────
        protected override void OnMarketDepth(MarketDepthEventArgs e)
        {
            if (_writerDead || !RecordDepth || e == null) return;
            if (State != State.Realtime) return;
            _live = true;

            try
            {
                var sb = new StringBuilder(64);
                sb.Append("L2;").Append(((int)e.MarketDataType).ToString(CultureInfo.InvariantCulture)).Append(';');
                Stamp(sb, e.Time);
                sb.Append(((int)e.Operation).ToString(CultureInfo.InvariantCulture)).Append(';')
                  .Append(e.Position.ToString(CultureInfo.InvariantCulture)).Append(';')
                  .Append(e.MarketMaker == null ? "" : e.MarketMaker).Append(';')
                  .Append(P(e.Price)).Append(';')
                  .Append(e.Volume.ToString(CultureInfo.InvariantCulture));
                Append(sb.ToString());
                _l2++;
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelTapeRecorder.OnMarketDepth", _sx); }
        }

        /// <summary>`yyyyMMddHHmmss;{subsec in 100-ns ticks};` — gbNRDtoCSV's exact stamp encoding.
        /// NT-LOCAL time on purpose: the export is local, `nrdcsv._HourClock` converts local->UTC, and
        /// emitting UTC here would silently shift every row by the offset.</summary>
        private static void Stamp(StringBuilder sb, DateTime t)
        {
            sb.Append(t.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)).Append(';')
              .Append((t.Ticks % TimeSpan.TicksPerSecond).ToString(CultureInfo.InvariantCulture)).Append(';');
        }

        private static string P(double v)
        {
            return v.ToString("0.##########", CultureInfo.InvariantCulture);
        }

        private void Append(string row)
        {
            if (_dir == null || _buf == null) return;

            // Roll at the NT-LOCAL date boundary so files line up with the export's own day layout.
            string d = _lastEvent.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            if (_day == null || d != _day)
            {
                Flush();
                _day  = d;
                _path = Path.Combine(_dir, d + ".csv");
                WriteMeta();
            }

            _buf.Append(row).Append('\n');
            _pending++;
            if (_pending >= Math.Max(200, FlushEveryRows)) Flush();
        }

        /// <summary>A `#META;` line. Both harness parsers filter on the `L1;`/`L2;` prefix, so this is
        /// invisible to them — the file stays format-compatible while still saying what produced it.</summary>
        private void WriteMeta()
        {
            if (_path != null && File.Exists(_path)) return;   // appending to today's file after a restart
            _buf.Append("#META;tapeVer=").Append(TapeVer)
                .Append(";inst=").Append(SafeFullName())
                .Append(";tickSize=").Append(P(SafeTickSize()))
                .Append(";tz=local")
                .Append(";openedUtc=").Append(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture))
                .Append('\n');
        }

        private void Flush()
        {
            if (_path == null || _buf == null || _buf.Length == 0) return;
            try
            {
                File.AppendAllText(_path, _buf.ToString());
                _buf.Length = 0;
                _pending = 0;
                _writeFailed = false;
            }
            catch (Exception ex)
            {
                // Say so on the card. A silently short tape that looks complete is the worst outcome —
                // it is exactly how the replay store's degraded book went unnoticed for months.
                _writeFailed = true;
                _lastErr = ex.Message;
                SentinelCore.Swallow("SentinelTapeRecorder.Flush", ex);
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

                const float cw = 262f, ch = 118f;
                var slot = SentinelSkin.CardLayout.Place(this, ChartPanel,
                    ChartPanel.X, ChartPanel.Y, ChartPanel.W, ChartPanel.H, CardCorner, cw, ch);

                bool dead = _writerDead || _writeFailed;
                var edge = dead ? SentinelSkin.CDown : (_live ? SentinelSkin.CAccent : SentinelSkin.CLine);
                var r = _sp.Card(slot.X, slot.Y, cw, ch, edge);

                _sp.Dot(r.Left + 5f, r.Top + 8f, _live && !dead ? SentinelSkin.CAccent : SentinelSkin.CMute, _live && !dead);
                _sp.Text("SENTINEL TAPE RECORDER", r.Left + 16f, r.Top, r.Width - 66f, 16f, SentinelSkin.CInk, 11f, true);
                string st = dead ? "NO REC" : (_live ? "LIVE" : "WAITING");
                var stCol = dead ? SentinelSkin.CDown : (_live ? SentinelSkin.CAccent : SentinelSkin.CInk2);
                _sp.Pill(st, r.Right, r.Top - 1f, stCol);

                var lead = SharpDX.DirectWrite.TextAlignment.Leading;

                _sp.Text("L1 ROWS", r.Left, r.Top + 24f, 120f, 12f, SentinelSkin.CMute, 9f, true);
                _sp.Text(_l1.ToString("N0", CultureInfo.InvariantCulture),
                    r.Left, r.Top + 32f, 120f, 24f, dead ? SentinelSkin.CDown : SentinelSkin.CAccent, 19f);

                _sp.Text(RecordDepth ? "L2 DEPTH" : "L2 OFF", r.Left + 132f, r.Top + 24f, 120f, 12f, SentinelSkin.CMute, 9f, true);
                _sp.Text(_l2.ToString("N0", CultureInfo.InvariantCulture),
                    r.Left + 132f, r.Top + 32f, 120f, 24f,
                    RecordDepth ? SentinelSkin.CInk2 : SentinelSkin.CMute, 19f);

                if (dead)
                {
                    _sp.Text(_lastErr == null ? "write failed" : Trunc(_lastErr, 46),
                        r.Left, r.Top + 60f, r.Width, 14f, SentinelSkin.CDown, 9.5f, false, lead, true);
                }
                else if (!_live)
                {
                    _sp.Text("realtime only — nothing written during a rebuild",
                        r.Left, r.Top + 60f, r.Width, 14f, SentinelSkin.CInk2, 9f, false, lead, true);
                }
                else
                {
                    string top = (_bid > 0 && _ask > 0)
                        ? P(_bid) + " / " + P(_ask) + "   (" + Spread() + "t)"
                        : "book not yet seen";
                    _sp.Text(top, r.Left, r.Top + 60f, r.Width, 14f, SentinelSkin.CInk2, 9.5f, false, lead, true);
                }

                _sp.Text("buffered " + _pending.ToString(CultureInfo.InvariantCulture)
                         + "   file " + (_day == null ? "—" : _day),
                    r.Left, r.Top + 78f, r.Width, 12f, SentinelSkin.CMute, 9f, false, lead, true);
                _sp.Text(SafeFullName(), r.Left, r.Top + 94f, r.Width, 12f, SentinelSkin.CMute, 9f, false, lead, true);

                _sp.End();
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelTapeRecorder.OnRender", _sx); }
        }

        private string Spread()
        {
            try
            {
                double ts = SafeTickSize();
                if (ts <= 0 || _ask <= _bid) return "?";
                return Math.Round((_ask - _bid) / ts).ToString(CultureInfo.InvariantCulture);
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelTapeRecorder.Spread", _sx); return "?"; }
        }

        private string SafeFullName()
        {
            try { return Instrument != null ? Instrument.FullName : "unknown"; }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelTapeRecorder.FullName", _sx); return "unknown"; }
        }
        private double SafeTickSize()
        {
            try { return Instrument.MasterInstrument.TickSize; }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelTapeRecorder.TickSize", _sx); return double.NaN; }
        }
        private static string Trunc(string s, int n) { return s == null ? "" : (s.Length <= n ? s : s.Substring(0, n)); }

        #region Properties
        [NinjaScriptProperty]
        [Display(Name = "Record L2 depth", Description = "Capture the full depth ladder. This is the whole point — turn off only if you need trades alone (L2 is ~3.5x the rows).", GroupName = "Tape Recorder", Order = 1)]
        public bool RecordDepth { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show info card", Description = "Draw the Sentinel glass-card readout (off = pure headless recorder).", GroupName = "Tape Recorder", Order = 2)]
        public bool ShowInfo { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Card corner", Description = "Which chart corner the card docks to. Cards in the same corner auto-stack (never overlap).", GroupName = "Tape Recorder", Order = 3)]
        public SentinelCardCorner CardCorner { get; set; }

        // NOT a [NinjaScriptProperty] — serializes to the workspace + shows in F6 but stays OUT of the
        // generated constructor (no codegen churn).
        [Display(Name = "Flush every N rows", Description = "Write the buffer to disk every N rows. Lower = less lost on a crash, more disk churn. Minimum 200.", GroupName = "Tape Recorder", Order = 4)]
        public int FlushEveryRows { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show indicator label", Description = "Show NinjaTrader's chart name label. Sentinel default = OFF (clean chart); turn on to restore it.", GroupName = "Sentinel", Order = 100)]
        public bool ShowIndicatorLabel { get; set; }
        #endregion
    }
}
