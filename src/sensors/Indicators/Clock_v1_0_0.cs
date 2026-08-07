// ─────────────────────────────────────────────────────────────────────────────
// This Source Code Form is subject to the terms of the Mozilla Public License,
// v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
// one at https://mozilla.org/MPL/2.0/.
//
// Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
// ─────────────────────────────────────────────────────────────────────────────
#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.AddOns.Sentinel;   // SentinelSkin (glass card) + SentinelCore (ClockState seam) + SentinelCardCorner
#endregion

// ═════════════════════════════════════════════════════════════════════════════
//  Clock — the Sentinel SESSION-CONTEXT modulator                           |   Version v1.0.0
//  File: Clock_v1_0_0.cs   |   namespace …Indicators.Sentinel   |   display Name "Clock"
//
//  ⚠ NO ORDERS — read-only advisory indicator. Safe to run anywhere.
//
//  WHAT THIS IS — the FIRST ORTHOGONAL axis feeding the Council (Docs/ROADMAP.md · memory
//  signal-axes-plan). Every base rate in this suite is conditional on WHERE in the session we are —
//  the open drive, the midday drift, and the close behave nothing alike — yet nothing published that
//  context until now. Clock resolves the session window from the chart's TradingHours and publishes the
//  per-instrument phase so the Council can MODULATE on it (damp conviction midday / gate the kill window)
//  rather than treat every minute the same. It is a MODULATOR, not a directional voter — it never says
//  long or short, it says WHEN.
//
//  THE STATE (SentinelCore.ClockState, SentinelCore ≥ v1.8.0):
//    • Phase          0 Closed/pre-open · 1 Open-drive · 2 Midday · 3 Close
//    • MinsSinceOpen  minutes since the session opened (-1 if not in session)
//    • MinsToClose    minutes until the session closes (-1 if not in session)
//    • DayOfWeek      0=Sun .. 6=Sat
//    • InSession      currently inside the trading session
//    • InKillWindow   inside the near-close no-new-entries window
//
//  SENTINEL WIRING (Docs/SENTINEL_DESIGN_SYSTEM.md §6c/§6d):
//    • PUBLISH: SetClockState(...) each update (default ON — Clock exists to publish).
//    • A SentinelSkin.Painter glass card + Sentinel palette + label remover. No plots (a modulator is
//      consumed via the seam, not as a plottable scalar).
//    • Session window comes from a SessionIterator over the chart's TradingHours; phase boundaries are
//      configurable minutes. NOTE: "now" is the current bar time (bar-resolution), which is plenty for a
//      modulator; a wall-clock refinement for tighter kill-window edges is a future tweak.
//
//  CHANGELOG
//    v1.0.0 (2026-07-07) — initial: session phase (open-drive / midday / close) + mins-since-open /
//             mins-to-close / day-of-week / in-session / kill-window, published as SentinelCore.ClockState;
//             Sentinel card/palette/label-remover. First orthogonal Council axis.
// ═════════════════════════════════════════════════════════════════════════════
namespace NinjaTrader.NinjaScript.Indicators.Sentinel
{
    public class Clock_v1_0_0 : Indicator
    {
        private static readonly string[] PhaseName = { "Closed", "Open Drive", "Midday", "Close" };
        private static readonly string[] DowName   = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };

        private SentinelSkin.Painter _sp;
        private SessionIterator      _sess;
        private bool _hasData;
        // cached state (computed in OnBarUpdate; drawn in OnRender)
        private int  _phase, _sinceOpen, _toClose, _dow;
        private bool _inSession, _killWindow;
        private int  _lastLoggedPhase = -999;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description              = "Sentinel session-context modulator — publishes the per-instrument session phase (open-drive / midday / close), minutes-since-open / to-close, day-of-week, and a near-close kill window as SentinelCore.ClockState so the Council can modulate on time-of-day.";
                Name                     = "Sentinel Clock v1.0.0";
                Calculate                = Calculate.OnPriceChange;
                IsOverlay                = true;
                IsSuspendedWhileInactive = true;
                DisplayInDataBox         = false;
                DrawOnPricePanel         = true;

                OpenDriveMin   = 45;    // first N minutes after open = "Open drive"
                CloseMin       = 45;    // last N minutes before close = "Close"
                KillWindowMin  = 5;     // last N minutes = no-new-entries kill window

                PublishState   = true;
                LogChanges     = true;
                ShowCard       = true;
                CardCorner     = SentinelCardCorner.TopRight;
                ShowIndicatorLabel = false;
            }
            else if (State == State.DataLoaded)
            {
                if (!ShowIndicatorLabel) Name = string.Empty;   // Sentinel label remover
                try { _sess = new SessionIterator(Bars); } catch { _sess = null; }
            }
            else if (State == State.Terminated)
            {
                if (_sp != null) { try { _sp.Dispose(); } catch (Exception _sx) { SentinelCore.Swallow("Clock.OnStateChange", _sx); } _sp = null; }
                try { SentinelSkin.CardLayout.Release(this); } catch (Exception _sx) { SentinelCore.Swallow("Clock.OnStateChange", _sx); }
            }
        }

        protected override void OnBarUpdate()
        {
            if (Instrument == null || Instrument.MasterInstrument == null || _sess == null) return;
            string inst = Instrument.MasterInstrument.Name;

            DateTime t = Times[0][0];
            bool inSession = false;
            int since = -1, toClose = -1, phase = 0;
            bool kill = false;

            try
            {
                _sess.GetNextSession(t, State == State.Realtime);
                DateTime b = _sess.ActualSessionBegin;
                DateTime e = _sess.ActualSessionEnd;
                inSession = t >= b && t < e;
                if (inSession)
                {
                    since   = (int)(t - b).TotalMinutes;
                    toClose = (int)(e - t).TotalMinutes;
                    if (since < OpenDriveMin) phase = 1;
                    else if (toClose <= CloseMin) { phase = 3; kill = toClose <= KillWindowMin; }
                    else phase = 2;
                }
            }
            catch (Exception _sx) { SentinelCore.Swallow("Clock.OnBarUpdate", _sx); }

            int dow = (int)t.DayOfWeek;

            _phase = phase; _sinceOpen = since; _toClose = toClose; _dow = dow;
            _inSession = inSession; _killWindow = kill;
            _hasData = true;

            if (PublishState)
            {
                try { SentinelCore.SetClockState(inst, phase, since, toClose, dow, inSession, kill, "Clock"); }
                catch (Exception _sx) { SentinelCore.Swallow("Clock.OnBarUpdate", _sx); }
            }

            if (LogChanges && State == State.Realtime && phase != _lastLoggedPhase)
            {
                _lastLoggedPhase = phase;
                try
                {
                    SentinelCore.Log("Clock", inst + " " + PhaseName[phase] +
                        (inSession ? " (+" + since + "m / -" + toClose + "m)" : " (out of session)") +
                        (kill ? " KILL-WINDOW" : ""));
                }
                catch (Exception _sx) { SentinelCore.Swallow("Clock.OnBarUpdate", _sx); }
            }
        }

        // ── Sentinel glass card ──
        protected override void OnRender(NinjaTrader.Gui.Chart.ChartControl chartControl, NinjaTrader.Gui.Chart.ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);
            if (!ShowCard || RenderTarget == null || ChartPanel == null) return;
            try
            {
                if (_sp == null) _sp = new SentinelSkin.Painter();
                _sp.Begin(RenderTarget);

                const float cw = 228f, ch = 138f;
                var slot = SentinelSkin.CardLayout.Place(this, ChartPanel,
                    ChartPanel.X, ChartPanel.Y, ChartPanel.W, ChartPanel.H, CardCorner, cw, ch);

                if (!_hasData)
                {
                    var rw = _sp.Card(slot.X, slot.Y, cw, ch, SentinelSkin.CLine);
                    _sp.Dot(rw.Left + 5f, rw.Top + 8f, SentinelSkin.CMute, false);
                    _sp.Text("CLOCK", rw.Left + 16f, rw.Top, rw.Width - 20f, 16f, SentinelSkin.CInk, 11f, true);
                    _sp.Text("resolving session…", rw.Left, rw.Top + 26f, rw.Width, 14f, SentinelSkin.CMute, 10.5f);
                    _sp.End();
                    return;
                }

                var trail = SharpDX.DirectWrite.TextAlignment.Trailing;
                var phaseCol = _phase == 1 ? SentinelSkin.CAccent
                             : _phase == 3 ? SentinelSkin.CWarn
                             : _phase == 2 ? SentinelSkin.CInk2 : SentinelSkin.CMute;
                var edge = _killWindow ? SentinelSkin.CWarn : (_inSession ? SentinelSkin.CAccent : SentinelSkin.CLine);
                var r = _sp.Card(slot.X, slot.Y, cw, ch, edge);

                // header — dot, title, day-of-week pill
                _sp.Dot(r.Left + 5f, r.Top + 8f, _inSession ? SentinelSkin.CAccent : SentinelSkin.CMute, _inSession);
                _sp.Text("CLOCK", r.Left + 16f, r.Top, r.Width - 70f, 16f, SentinelSkin.CInk, 11f, true);
                _sp.Pill(DowName[_dow], r.Right, r.Top - 1f, _inSession ? SentinelSkin.CAccent : SentinelSkin.CMute);

                // hero — phase name
                _sp.Text("PHASE", r.Left, r.Top + 24f, 100f, 12f, SentinelSkin.CMute, 8.5f, true);
                _sp.Text(PhaseName[_phase], r.Left, r.Top + 34f, r.Width, 24f, phaseCol, 17f, false);

                // stats — since open / to close
                _sp.Divider(r.Left, r.Top + 66f, r.Right);
                if (_inSession)
                {
                    _sp.Text("+" + _sinceOpen + "m since open", r.Left, r.Top + 72f, r.Width, 14f, SentinelSkin.CInk2, 10f);
                    _sp.Text("-" + _toClose + "m to close", r.Left, r.Top + 72f, r.Width, 14f,
                             _killWindow ? SentinelSkin.CWarn : SentinelSkin.CInk2, 10f, true, trail);
                }
                else
                {
                    _sp.Text("out of session", r.Left, r.Top + 72f, r.Width, 14f, SentinelSkin.CMute, 10f);
                }
                if (_killWindow)
                    _sp.Text("KILL WINDOW — no new entries", r.Left, r.Top + 92f, r.Width, 14f, SentinelSkin.CWarn, 10f, true);

                _sp.End();
            }
            catch (Exception _sx) { SentinelCore.Swallow("Clock.OnRender", _sx); }
        }

        #region Properties
        [Range(0, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Open-Drive Minutes", Description = "First N minutes after the session open counted as the 'Open drive' phase.", Order = 1, GroupName = "Phases")]
        public int OpenDriveMin { get; set; }

        [Range(0, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Close Minutes", Description = "Last N minutes before the session close counted as the 'Close' phase.", Order = 2, GroupName = "Phases")]
        public int CloseMin { get; set; }

        [Range(0, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Kill-Window Minutes", Description = "Last N minutes before close flagged as the no-new-entries kill window.", Order = 3, GroupName = "Phases")]
        public int KillWindowMin { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Publish Clock to Sentinel", Description = "Publish session phase as SentinelCore.ClockState so the Council/strategies can modulate on it. Needs SentinelCore ≥ v1.8.0.", Order = 10, GroupName = "Sentinel")]
        public bool PublishState { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Log Phase Changes", Description = "Write session-phase transitions to sentinel.log.", Order = 11, GroupName = "Sentinel")]
        public bool LogChanges { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Card", Order = 12, GroupName = "Sentinel")]
        public bool ShowCard { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Card Corner", Description = "Which chart corner the Sentinel card docks to. Cards in the same corner auto-stack.", Order = 13, GroupName = "Sentinel")]
        public SentinelCardCorner CardCorner { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show indicator label", Description = "Show NinjaTrader's chart name label. Sentinel default = OFF (clean chart); turn on to restore it.", GroupName = "Sentinel", Order = 100)]
        public bool ShowIndicatorLabel { get; set; }
        #endregion
    }
}
