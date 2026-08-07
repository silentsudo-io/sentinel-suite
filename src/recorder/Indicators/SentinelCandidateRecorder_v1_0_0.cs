// ─────────────────────────────────────────────────────────────────────────────
// This Source Code Form is subject to the terms of the Mozilla Public License,
// v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
// one at https://mozilla.org/MPL/2.0/.
//
// Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
// ─────────────────────────────────────────────────────────────────────────────
// ═════════════════════════════════════════════════════════════════════════════
//  SentinelCandidateRecorder — the CLOCK-native candidate corpus (the "second oven")
//  File: SentinelCandidateRecorder_v1_0_0.cs   ·   Version v1.3.0   ·   Schema cand.2 (sidecar ctick.4)   ·   namespace …Indicators.Sentinel
// ─────────────────────────────────────────────────────────────────────────────
//  WHAT THIS IS
//    A pure, no-orders recorder that tests one hypothesis: **the edge lives in the CLOCK, not the fused
//    voters.** It records the UNFILTERED, clock-native candidate population — **every brick close is one
//    candidate**, direction = the brick's own direction (the simplest possible CONTINUATION primitive), no
//    selection, no fusion, no gate. For each candidate it tracks (fire → EOD) the max fav/adv excursion
//    (MFE/MAE, ticks), 1/5/15/60-min milestones, the ATR-scaled FIRST-TOUCH label, the raw TICK path, and
//    **`runLength`** (consecutive same-direction bricks → makes EXHAUSTION a retroactive slice of the same
//    corpus). It stamps CONTEXT-at-fire (regime/ADX/RVOL/clock-phase/MTF/Flux) by CONSULTING the published
//    `…State` seams — read-only, never fused — so every gate is a Lab-side retroactive filter, never a
//    hardcoded `if`. One bake tests the whole (rule × regime × exit) grid; we derive labels LATE.
//
//    Why a NEW corpus (not the Council corpus): the Council excursion corpus is a FUSION-GATED, biased sample
//    ("the Council chose to fire") → it structurally cannot measure whether the CLOCK itself has a base rate.
//    Different question ⇒ different corpus. Rows land in Sentinel\Excursions\candidates\cand.2\ (signal tag
//    "CONT"), sidecars in candidates\ticks\ — a separate root so the population NEVER pools with COUNCIL rows.
//    Runs in PARALLEL with the Council 1.5 bake (two ovens, one apparatus).
//
//    Hypothesis + design: Docs\SENTINEL_CLOCK_EDGE_HYPOTHESIS.md (candid version: private _sidebar Entry 3).
//    Fidelity spine (realtime gate · tick-path capture · first-touch · window-streaming · provenance · scope)
//    is inherited verbatim from SentinelExcursionRecorder_v2_0_0 — the same checklist that keeps it trustworthy.
//
//  ✅ C6 BOUNDARY CLOSED in v1.2.0 (2026-07-22). It read: "firePx = Close[0] (a synthetic brick close on
//    HA/TBars/Flux, not a tradeable print)". That caveat was CORRECT and was never quantified — measurement
//    showed it put a ~9-tick optimistic offset on EVERY label. firePx is now the real last trade. What remains
//    of C6: still no orders/fills/slippage — this corpus grades SIGNAL-PATH quality. Grading exit policies on
//    the recorded tick path = Phase 1 (cheap, honest-enough). Real path-managed-EXIT validation = the
//    Ledger/execution rail (Phase 2, separate build). Don't mistake a Phase-1 exit curve for a tradeable one.
//
//  CHANGELOG
//    v1.3.0 (2026-07-23) — LATCHED BOUNDARIES AT FIRE (`brkUpper`/`brkLower` from BrickState) so limit-vs-market
//           entry is gradeable offline on every bar type at once — no new bar type needed (TBars boundaries are
//           already immutable within a bar; BrickState has published them per tick all along).
//           🐛 FIXED WHILE HERE: `GetFluxState` was read with the LANED `Scope()`. FluxState is a BAR-TYPE seam
//           published by the shared bars series, so it is keyed BARE — on any laned chart the read returned null
//           and fluxDir/fluxPressure/fluxDiverg silently stamped 0 forever. Same class of fault as the crashed-
//           sensor pattern: absence was indistinguishable from a real reading. Now uses BareScope().
//           Schema stays cand.2 (additive fields); recVer separates the batches.
//    v1.2.1 (2026-07-22) — ENTRY BACKFILL (council-recorder v2.2.1 parity). `_lastPx` is the trade BEFORE the one
//           that closed the brick (OnMarketData runs after OnBarUpdate), so a first path tick in the SAME
//           millisecond is adopted as the entry (`pxSrc="firsttick"`); a later one is not, since that would be
//           lookahead. This matters MOST here: brick CASCADES (measured on GC Renko 11v1x1) print several bricks
//           off one jump, and every one of them was inheriting the same pre-jump entry.
//    v1.2.0 (2026-07-22) — 🔴 THE HONEST ENTRY PRICE (parity with council recorder v2.2.0). Row cand.1 → cand.2,
//           sidecar ctick.3 → ctick.4. `FirePx` was `Close[0]` = the HEIKIN-ASHI SYNTHETIC close, a price that
//           NEVER TRADED — the C6 BOUNDARY above, flagged from day one and never measured. Measured 2026-07-22:
//           the gap is −9.36t mean, symmetric by side (the HA fingerprint), and since FirePx is the reference for
//           MFE/MAE/barrier/first-touch it made every label optimistic (council corpus: "target-first" 52.3%
//           recorded vs 21.1% TRUE, labels disagreeing on 44.6% of fires). FIX: latch the true last trade in
//           OnMarketData BEFORE the tick-path guards; `pxSrc` records how it resolved; `barClosePx` keeps the HA
//           close; `entryBid`/`entryAsk` let the Lab price the crossing cost. cand.1 kept, never pooled with
//           cand.2. Evidence: memory `firepx-is-synthetic-ha-close`.
//    v1.1.0 (2026-07-18) — ENTRY CONTEXT on the TICK SIDECAR HEADER (sidecar ctick.2 → ctick.3; ROW schema
//           UNCHANGED at cand.1). Parity with the council recorder v2.1.6 fix: the candidate sidecar now stamps the
//           full entry-context block (regime/adx/rvol/volZ/climax/dryUp/clockPhase/minsToClose/mtfBias/fluxDir/
//           fluxPressure/fluxDiverg) — all already captured on the Rec at fire, so emit-only. Makes every candidate
//           tick-path SELF-DESCRIBING so Lab\pathlab.py can gate the candidate corpus without a (inst,bartype,dir,
//           fireTime) join to cand.1 (that join lands ~97%, but the header is exact + join-free). Old ctick.2 sidecars
//           stay valid (readers fall back to the join). No order/Core change.
//    v1.0.0 (2026-07-17) — first cut. Every-brick-close CONT candidate; runLength + seam context; schema cand.1;
//           candidates\ corpus + ticks sidecars; provenance (recVer/coreVer/barLabel) + realtime gate + tick
//           path reused from the excursion recorder. No State seam (a recorder, not a sensor).
// ═════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Text;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.AddOns.Sentinel;

namespace NinjaTrader.NinjaScript.Indicators.Sentinel
{
    public class SentinelCandidateRecorder_v1_0_0 : Indicator
    {
        private static readonly int[] Milestones = { 1, 5, 15, 60 };   // minutes

        private const double BarrierAtrMult = 1.0;    // first-touch R = this × ATR(14), in ticks
        private const double BarrierMinTicks = 20.0;  // …but never below this (noise floor)

        private const long TickPathTailMs = 30000;    // flush a fire's tick sidecar 30s after first-touch
        private const long TickPathMaxMs  = 300000;   // hard cap: flush a never-resolving fire after 5 min

        // v1.2.0 — cand.2 = cand.1 + the HONEST ENTRY PRICE. FirePx changed MEANING, re-basing every MFE/MAE/
        // barrier/firstTouch, so the rows land in candidates\cand.2\ and can never pool with the optimistic cand.1.
        private const string SchemaVer = "cand.2";
        private const string RecVer    = "1.3.0";   // v1.3.0 = + latched brick boundaries at fire (schema stays cand.2; additive)

        private ADX adx;
        private ATR atr;
        private List<Rec> _open;
        private string _logPath;
        private string _ticksDir;
        private int  _fireSeq;
        private int  _lastBrickDir;   // for runLength (tracked through history so the first live fire is warm)
        private int  _runLength;

        // card state
        private SentinelSkin.Painter _sp;
        private int    _nCand;
        private int    _recorded;
        private bool   _writerDead;
        private bool   _writeFailed;
        private double _curAdx;
        private string _curRegime = "?";
        private int    _lastDir;
        private int    _lastRun;

        // v1.2.0 TAPE LATCH — closes the C6 BOUNDARY. Close[0] is the HA synthetic brick close and never trades;
        // the true last trade is visible only in OnMarketData. (Council-recorder v2.2.0 parity.)
        private double _lastPx;
        private double _lastBid, _lastAsk;

        private sealed class Rec
        {
            public string   Signal;
            public int      Dir;
            public int      RunLength;
            public DateTime FireTime;
            public double   FirePx;       // v1.2.0 — the REAL last-trade price (was the HA synthetic Close[0])
            public string   PxSrc;        // "last" | "barclose" fallback
            public double   BarClosePx;   // the bar's own close — NOT tradeable, kept for continuity
            public double   EntryBid, EntryAsk;
            public bool     PxFixed;      // v1.2.1 — one-shot entry-price backfill decided for this Rec
            // v1.3.0 — the FORMING bar's LATCHED boundaries at fire (BrickState.Upper/LowerPrice; 0 = seam absent).
            // The only prices known BEFORE they are reached ⇒ the levels a resting LIMIT could have been posted at.
            public double   BrkUpper, BrkLower;
            public int      FireBar;
            public double   MaxMFE, MaxMAE;
            public int      BarsToMFE, BarsToMAE;
            public long     MsToMFE, MsToMAE;
            public DateTime LastTime;
            public double[] MfeAt;
            public double[] MaeAt;
            public string   Regime;
            public double   Adx;
            // context at fire (consulted seams, read-only; NaN/0/-1 when the seam is absent)
            public double   Rvol, VolZ;
            public bool     Climax, DryUp;
            public int      ClockPhase, MinsToClose;
            public int      MtfBias;
            public int      FluxDir, FluxDiverg;
            public double   FluxPressure;
            // first-touch label
            public double   BarrierTicks;
            public int      FtFavBar, FtAdvBar;
            // raw-tick path
            public string        FireId;
            public StringBuilder TickBuf;
            public int           TickCount;
            public bool          PathTrunc;
            public double        MaxFavTick, MaxAdvTick;
            public long          MsToMaxFavTick, MsToMaxAdvTick;
            public long          FtFavMs, FtAdvMs, ResolvedMs;
            public bool          PathWritten;
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name        = "Sentinel Candidate Recorder v1.0.0";
                Description = "The clock-native candidate corpus: EVERY brick close is a continuation candidate. "
                            + "Tracks fire→EOD MFE/MAE, first-touch, tick path, runLength + seam context. No orders, "
                            + "no fusion. Tests whether the CLOCK carries edge. Writes Sentinel\\Excursions\\candidates\\.";
                IsOverlay                = true;
                Calculate                = Calculate.OnBarClose;   // one OnBarUpdate per brick close = one candidate
                DrawOnPricePanel         = true;
                IsSuspendedWhileInactive = false;
                ShowInfo                 = true;
                CardCorner               = SentinelCardCorner.TopRight;
                RecordTickPath           = true;   // capture the raw-tick path (Phase-1 exit labels). Toggle OFF on an ultra-fast chart.
                ShowIndicatorLabel       = false;
            }
            else if (State == State.DataLoaded)
            {
                if (!ShowIndicatorLabel) Name = string.Empty;

                adx = ADX(14);
                atr = ATR(14);
                _open = new List<Rec>();
                try
                {
                    string dir = Path.Combine(SentinelCore.SettingsDir, "Excursions", "candidates", SchemaVer);
                    Directory.CreateDirectory(dir);
                    string stamp = DateTime.Now.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture);
                    _logPath = Path.Combine(dir, stamp + "__" + InstName() + "__" + BarTag() + ".jsonl");
                    _ticksDir = Path.Combine(SentinelCore.SettingsDir, "Excursions", "candidates", "ticks");
                    if (RecordTickPath) Directory.CreateDirectory(_ticksDir);
                }
                catch (Exception ex)
                {
                    _logPath = null; _ticksDir = null; _writerDead = true;
                    try { SentinelCore.Log("Candidate", "WRITER DEAD — could not open the candidates corpus dir; NOTHING "
                        + "will be recorded this session (" + ex.Message + "). The card shows NO REC."); } catch (Exception _sx) { SentinelCore.Swallow("SentinelCandidateRec.OnStateChange", _sx); }
                }
            }
            else if (State == State.Terminated)
            {
                FlushAll("cutoff");
                if (_sp != null) { try { _sp.Dispose(); } catch (Exception _sx) { SentinelCore.Swallow("SentinelCandidateRec.OnStateChange", _sx); } _sp = null; }
                try { SentinelSkin.CardLayout.Release(this); } catch (Exception _sx) { SentinelCore.Swallow("SentinelCandidateRec.OnStateChange", _sx); }
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 25) return;   // ADX/ATR warmup

            if (Bars.IsFirstBarOfSession)
            {
                if (_open != null && _open.Count > 0) FlushAll("EOD");
                _nCand = 0;
            }

            double tick = TickSize;
            DateTime now = Time[0];
            _curAdx = 0; try { _curAdx = adx[0]; } catch (Exception _sx) { SentinelCore.Swallow("SentinelCandidateRec.OnBarUpdate", _sx); }
            _curRegime = Regime(_curAdx);

            // ── track every open candidate (verbatim excursion spine) ─────────────────────────────
            for (int i = _open.Count - 1; i >= 0; i--)
            {
                Rec r = _open[i];
                double fav, adv;
                if (r.Dir > 0) { fav = (High[0] - r.FirePx) / tick; adv = (r.FirePx - Low[0]) / tick; }
                else           { fav = (r.FirePx - Low[0]) / tick;  adv = (High[0] - r.FirePx) / tick; }
                if (fav > r.MaxMFE) { r.MaxMFE = fav; r.BarsToMFE = CurrentBar - r.FireBar; r.MsToMFE = (long)(now - r.FireTime).TotalMilliseconds; }
                if (adv > r.MaxMAE) { r.MaxMAE = adv; r.BarsToMAE = CurrentBar - r.FireBar; r.MsToMAE = (long)(now - r.FireTime).TotalMilliseconds; }
                if (r.FtFavBar < 0 && fav >= r.BarrierTicks) r.FtFavBar = CurrentBar - r.FireBar;
                if (r.FtAdvBar < 0 && adv >= r.BarrierTicks) r.FtAdvBar = CurrentBar - r.FireBar;

                double elapsedMin = (now - r.FireTime).TotalMinutes;
                for (int m = 0; m < Milestones.Length; m++)
                    if (double.IsNaN(r.MfeAt[m]) && elapsedMin >= Milestones[m]) { r.MfeAt[m] = r.MaxMFE; r.MaeAt[m] = r.MaxMAE; }
                r.LastTime = now;

                if (elapsedMin >= Milestones[Milestones.Length - 1])
                {
                    WriteRow(r, "window");
                    _open.RemoveAt(i);
                }
            }

            // ── FIRE: every brick close is a candidate ─────────────────────────────────────────────
            // runLength is tracked through history so the first REALTIME fire carries a warm run count;
            // candidates are OPENED realtime-only (the as-of/replay discipline — a historical open would
            // record a bar whose forward path we already know).
            int bdir = Close[0] > Open[0] ? 1 : (Close[0] < Open[0] ? -1 : 0);
            if (bdir != 0)
            {
                if (bdir == _lastBrickDir) _runLength++; else _runLength = 1;
                _lastBrickDir = bdir;
                if (State == State.Realtime) Fire("CONT", bdir);
            }
        }

        // RAW-TICK PATH — OnMarketData is the only place the true last-trade price is visible (Close[0] is a
        // synthetic brick close). Realtime/replay only, same data thread as OnBarUpdate. (Verbatim spine.)
        protected override void OnMarketData(MarketDataEventArgs e)
        {
            // v1.2.0 — TAPE LATCH, deliberately BEFORE the guards below: a fire must be priced from the real tape
            // even when path capture is off or no fire is open. Closes the C6 BOUNDARY noted in the header.
            if      (e.MarketDataType == MarketDataType.Last) _lastPx  = e.Price;
            else if (e.MarketDataType == MarketDataType.Ask)  _lastAsk = e.Price;
            else if (e.MarketDataType == MarketDataType.Bid)  _lastBid = e.Price;

            if (e.MarketDataType != MarketDataType.Last) return;
            if (!RecordTickPath || _open == null || _open.Count == 0) return;
            double px = e.Price, tick = TickSize;
            if (tick <= 0) return;
            for (int i = 0; i < _open.Count; i++)
            {
                Rec r = _open[i];
                if (r.PathWritten) continue;
                long ms = (long)(e.Time - r.FireTime).TotalMilliseconds;
                if (ms < 0) ms = 0;

                // v1.2.1 — ENTRY BACKFILL (council-recorder v2.2.1 parity), one-shot, before any use of FirePx.
                // OnMarketData runs after OnBarUpdate for the triggering tick, so _lastPx is the trade BEFORE the
                // one that closed the brick. A first path tick in the SAME millisecond IS that trade — adopt it.
                // NOT unconditional: a next-trade seconds away would import forward drift (lookahead). This matters
                // most HERE — Renko/brick CASCADES print several bricks off one jump, all sharing a stale entry.
                if (!r.PxFixed)
                {
                    r.PxFixed = true;
                    if (ms == 0) { r.FirePx = px; r.PxSrc = "firsttick"; }
                }

                double favT = r.Dir > 0 ? (px - r.FirePx) / tick : (r.FirePx - px) / tick;
                double advT = -favT;
                if (favT > r.MaxFavTick) { r.MaxFavTick = favT; r.MsToMaxFavTick = ms; }
                if (advT > r.MaxAdvTick) { r.MaxAdvTick = advT; r.MsToMaxAdvTick = ms; }
                if (r.FtFavMs < 0 && favT >= r.BarrierTicks) r.FtFavMs = ms;
                if (r.FtAdvMs < 0 && advT >= r.BarrierTicks) r.FtAdvMs = ms;
                if (r.ResolvedMs < 0 && (r.FtFavMs >= 0 || r.FtAdvMs >= 0)) r.ResolvedMs = ms;
                if (r.TickBuf != null && r.TickBuf.Length < 4000000)
                {
                    r.TickBuf.Append("{\"ms\":").Append(ms).Append(",\"px\":")
                             .Append(px.ToString("0.#####", CultureInfo.InvariantCulture)).Append("}\n");
                    r.TickCount++;
                }
                else if (r.TickBuf != null) r.PathTrunc = true;

                bool tailDone = r.ResolvedMs >= 0 && (ms - r.ResolvedMs) >= TickPathTailMs;
                if (tailDone || ms >= TickPathMaxMs) WriteTickPath(r);
            }
        }

        private void Fire(string signal, int dir)
        {
            double a = 0; try { a = adx[0]; } catch (Exception _sx) { SentinelCore.Swallow("SentinelCandidateRec.Fire", _sx); }

            // context at fire — consult the published seams, read-only, null-safe (absent seam ⇒ NaN/0/-1)
            double rvol = double.NaN, volz = double.NaN; bool climax = false, dryup = false;
            int clockPhase = -1, minsClose = -1, mtfBias = 0, fluxDir = 0, fluxDiv = 0; double fluxPress = double.NaN;
            try { var p  = SentinelCore.GetParticipationState(Scope(), 90); if (p  != null) { rvol = p.Rvol; volz = p.VolZ; climax = p.Climax; dryup = p.DryUp; } } catch (Exception _sx) { SentinelCore.Swallow("SentinelCandidateRec.Fire", _sx); }
            try { var c  = SentinelCore.GetClockState(InstName(), 90);       if (c  != null) { clockPhase = c.Phase; minsClose = c.MinsToClose; } } catch (Exception _sx) { SentinelCore.Swallow("SentinelCandidateRec.Fire", _sx); }
            try { var m  = SentinelCore.GetMtfState(Scope(), 90);            if (m  != null) { mtfBias = m.Bias; } } catch (Exception _sx) { SentinelCore.Swallow("SentinelCandidateRec.Fire", _sx); }
            try { var fx = SentinelCore.GetFluxState(BareScope(), 90);           if (fx != null) { fluxDir = fx.FlowDir; fluxPress = fx.Pressure; fluxDiv = fx.Divergence; } } catch (Exception _sx) { SentinelCore.Swallow("SentinelCandidateRec.Fire", _sx); }

            // v1.3.0 — latched boundaries of the bar now forming (bar-type seam ⇒ BARE scope).
            double brkUp = 0, brkDn = 0;
            try
            {
                var bs = SentinelCore.GetBrickState(BareScope(), 90);
                if (bs != null) { brkUp = bs.UpperPrice; brkDn = bs.LowerPrice; }
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelCandidateRec.Fire", _sx); }

            var r = new Rec
            {
                BrkUpper = brkUp, BrkLower = brkDn,
                Signal = signal, Dir = dir, RunLength = _runLength,
                // v1.2.0 — real tape price, not the HA synthetic close (see the changelog + C6 note in the header)
                FireTime = Time[0],
                FirePx = (_lastPx > 0 ? _lastPx : Close[0]),
                PxSrc  = (_lastPx > 0 ? "last" : "barclose"),
                BarClosePx = Close[0], EntryBid = _lastBid, EntryAsk = _lastAsk,
                FireBar = CurrentBar,
                MaxMFE = 0, MaxMAE = 0, LastTime = Time[0],
                MfeAt = new double[Milestones.Length], MaeAt = new double[Milestones.Length],
                Regime = Regime(a), Adx = a,
                Rvol = rvol, VolZ = volz, Climax = climax, DryUp = dryup,
                ClockPhase = clockPhase, MinsToClose = minsClose, MtfBias = mtfBias,
                FluxDir = fluxDir, FluxPressure = fluxPress, FluxDiverg = fluxDiv,
                BarrierTicks = FirstTouchBarrier(), FtFavBar = -1, FtAdvBar = -1,
                TickBuf = RecordTickPath ? new StringBuilder(8192) : null,
                MaxFavTick = 0, MaxAdvTick = 0, FtFavMs = -1, FtAdvMs = -1, ResolvedMs = -1
            };
            for (int m = 0; m < Milestones.Length; m++) { r.MfeAt[m] = double.NaN; r.MaeAt[m] = double.NaN; }
            r.FireId = Time[0].ToUniversalTime().ToString("yyyyMMdd'T'HHmmssfff", CultureInfo.InvariantCulture)
                     + "_" + InstName() + "_" + (dir > 0 ? "L" : "S") + "_" + (++_fireSeq);
            _open.Add(r);

            _nCand++;
            _lastDir = dir; _lastRun = _runLength;
        }

        private static string Regime(double a)
        {
            if (double.IsNaN(a) || a <= 0) return "?";
            if (a >= 25) return "trend";
            if (a <= 18) return "chop";
            return "mid";
        }

        private double FirstTouchBarrier()
        {
            double atrTicks = 0;
            try { if (atr != null && TickSize > 0) atrTicks = atr[0] / TickSize; } catch (Exception _sx) { SentinelCore.Swallow("SentinelCandidateRec.FirstTouchBarrier", _sx); }
            return Math.Max(BarrierMinTicks, BarrierAtrMult * atrTicks);
        }

        private void FlushAll(string reason)
        {
            if (_open == null || _open.Count == 0) return;
            var sb = new StringBuilder(4096);
            foreach (Rec r in _open) { sb.Append(ToJson(r, reason)).Append(Environment.NewLine); WriteTickPath(r); }
            int n = _open.Count;
            _open.Clear();
            if (_logPath == null) return;
            try { File.AppendAllText(_logPath, sb.ToString()); _recorded += n; } catch (Exception ex) { NoteWriteFail(ex); }
        }

        private void NoteWriteFail(Exception ex)
        {
            if (_writeFailed) return;
            _writeFailed = true;
            try { SentinelCore.Log("Candidate", "WRITE FAILED — candidate rows are being LOST (" + ex.Message + "). Fix the sink; the card flags it."); } catch (Exception _sx) { SentinelCore.Swallow("SentinelCandidateRec.NoteWriteFail", _sx); }
        }

        private void WriteRow(Rec r, string reason)
        {
            WriteTickPath(r);
            if (_logPath == null) return;
            try { File.AppendAllText(_logPath, ToJson(r, reason) + Environment.NewLine); _recorded++; } catch (Exception ex) { NoteWriteFail(ex); }
        }

        private void WriteTickPath(Rec r)
        {
            if (!RecordTickPath || r == null || r.PathWritten || r.TickBuf == null || r.TickCount == 0 || _ticksDir == null) return;
            try
            {
                Directory.CreateDirectory(_ticksDir);
                int ftT;
                if (r.FtFavMs >= 0 && r.FtAdvMs >= 0) ftT = r.FtFavMs < r.FtAdvMs ? 1 : (r.FtAdvMs < r.FtFavMs ? -1 : 0);
                else if (r.FtFavMs >= 0) ftT = 1;
                else if (r.FtAdvMs >= 0) ftT = -1;
                else ftT = 0;

                var sb = new StringBuilder(4096);
                sb.Append("{\"schema\":\"ctick.4\",\"kind\":\"candidate_tickpath\"")
                  .Append(",\"recVer\":").Append(Q(RecVer))
                  .Append(",\"coreVer\":").Append(Q(SentinelCore.Version))
                  .Append(",\"barLabel\":").Append(Q(SentinelCore.FriendlyBartag(BarTag())))
                  .Append(",\"fireId\":").Append(Q(r.FireId))
                  .Append(",\"scope\":").Append(Q(Scope()))
                  .Append(",\"inst\":").Append(Q(InstName()))
                  .Append(",\"bartype\":").Append(Q(BarTag()))
                  .Append(",\"signal\":").Append(Q(r.Signal))
                  .Append(",\"dir\":").Append(r.Dir)
                  .Append(",\"runLength\":").Append(r.RunLength)
                  .Append(",\"fireTime\":").Append(Q(Iso(r.FireTime)))
                  .Append(",\"firePx\":").Append(F(r.FirePx))
                  // v1.2.0 (ctick.4) — real tape entry + how it resolved + the untradeable HA close + the book
                  .Append(",\"pxSrc\":").Append(Q(r.PxSrc))
                  .Append(",\"barClosePx\":").Append(F(r.BarClosePx))
                  .Append(",\"entryBid\":").Append(F(r.EntryBid))
                  .Append(",\"entryAsk\":").Append(F(r.EntryAsk))
                  .Append(",\"brkUpper\":").Append(F(r.BrkUpper))
                  .Append(",\"brkLower\":").Append(F(r.BrkLower))
                  // v1.1.0 (ctick.3) — ENTRY CONTEXT on the sidecar header so every candidate tick-path is
                  // SELF-DESCRIBING (mirrors the council recorder v2.1.6 fix). Already captured on the Rec at fire;
                  // emit-only. Lets Lab\pathlab.py gate the candidate corpus without a fireTime join to cand.1.
                  .Append(",\"regime\":").Append(Q(r.Regime))
                  .Append(",\"adx\":").Append(F(r.Adx))
                  .Append(",\"rvol\":").Append(F(r.Rvol))
                  .Append(",\"volZ\":").Append(F(r.VolZ))
                  .Append(",\"climax\":").Append(r.Climax ? "true" : "false")
                  .Append(",\"dryUp\":").Append(r.DryUp ? "true" : "false")
                  .Append(",\"clockPhase\":").Append(r.ClockPhase)
                  .Append(",\"minsToClose\":").Append(r.MinsToClose)
                  .Append(",\"mtfBias\":").Append(r.MtfBias)
                  .Append(",\"fluxDir\":").Append(r.FluxDir)
                  .Append(",\"fluxPressure\":").Append(F(r.FluxPressure))
                  .Append(",\"fluxDiverg\":").Append(r.FluxDiverg)
                  .Append(",\"barrierTicks\":").Append(F(r.BarrierTicks))
                  .Append(",\"maxFavTicks\":").Append(F(r.MaxFavTick))
                  .Append(",\"maxAdvTicks\":").Append(F(r.MaxAdvTick))
                  .Append(",\"msToMaxFav\":").Append(r.MsToMaxFavTick)
                  .Append(",\"msToMaxAdv\":").Append(r.MsToMaxAdvTick)
                  .Append(",\"msToTargetR\":").Append(r.FtFavMs)
                  .Append(",\"msToStopR\":").Append(r.FtAdvMs)
                  .Append(",\"firstTouchTick\":").Append(ftT)
                  .Append(",\"ticks\":").Append(r.TickCount)
                  .Append(",\"trunc\":").Append(r.PathTrunc ? "true" : "false")
                  .Append("}\n");
                sb.Append(r.TickBuf);
                File.AppendAllText(Path.Combine(_ticksDir, r.FireId + ".jsonl"), sb.ToString());
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelCandidateRec.WriteTickPath", _sx); }
            r.PathWritten = true;
            r.TickBuf = null;
        }

        private string ToJson(Rec r, string reason)
        {
            var sb = new StringBuilder(560);
            sb.Append('{')
              .Append("\"schema\":\"").Append(SchemaVer).Append("\",\"kind\":\"candidate\"")
              .Append(",\"recVer\":").Append(Q(RecVer))
              .Append(",\"coreVer\":").Append(Q(SentinelCore.Version))
              .Append(",\"barLabel\":").Append(Q(SentinelCore.FriendlyBartag(BarTag())))
              .Append(",\"inst\":").Append(Q(InstName()))
              .Append(",\"bartype\":").Append(Q(BarTag()))
              .Append(",\"signal\":").Append(Q(r.Signal))
              .Append(",\"dir\":").Append(r.Dir)
              .Append(",\"runLength\":").Append(r.RunLength)
              .Append(",\"regime\":").Append(Q(r.Regime))
              .Append(",\"adx\":").Append(F(r.Adx))
              // context at fire (consulted seams)
              .Append(",\"rvol\":").Append(F(r.Rvol))
              .Append(",\"volZ\":").Append(F(r.VolZ))
              .Append(",\"climax\":").Append(r.Climax ? "true" : "false")
              .Append(",\"dryUp\":").Append(r.DryUp ? "true" : "false")
              .Append(",\"clockPhase\":").Append(r.ClockPhase)
              .Append(",\"minsToClose\":").Append(r.MinsToClose)
              .Append(",\"mtfBias\":").Append(r.MtfBias)
              .Append(",\"fluxDir\":").Append(r.FluxDir)
              .Append(",\"fluxPressure\":").Append(F(r.FluxPressure))
              .Append(",\"fluxDiverg\":").Append(r.FluxDiverg)
              .Append(",\"fireTime\":").Append(Q(Iso(r.FireTime)))
              .Append(",\"firePx\":").Append(F(r.FirePx))
              // v1.2.0 (cand.2) — every MFE/MAE/barrier/firstTouch below is now measured from a fillable price
              .Append(",\"pxSrc\":").Append(Q(r.PxSrc))
              .Append(",\"barClosePx\":").Append(F(r.BarClosePx))
              .Append(",\"entryBid\":").Append(F(r.EntryBid))
              .Append(",\"entryAsk\":").Append(F(r.EntryAsk))
              .Append(",\"brkUpper\":").Append(F(r.BrkUpper))
              .Append(",\"brkLower\":").Append(F(r.BrkLower))
              .Append(",\"maxMFE\":").Append(F(r.MaxMFE))
              .Append(",\"maxMAE\":").Append(F(r.MaxMAE))
              .Append(",\"barsToMFE\":").Append(r.BarsToMFE)
              .Append(",\"barsToMAE\":").Append(r.BarsToMAE)
              .Append(",\"msToMFE\":").Append(r.MsToMFE)
              .Append(",\"msToMAE\":").Append(r.MsToMAE)
              .Append(",\"bars\":").Append(r.FireBar >= 0 ? (CurrentBar - r.FireBar) : 0);
            for (int m = 0; m < Milestones.Length; m++)
                sb.Append(",\"mfe").Append(Milestones[m]).Append("\":").Append(F(r.MfeAt[m]))
                  .Append(",\"mae").Append(Milestones[m]).Append("\":").Append(F(r.MaeAt[m]));
            int firstTouch; bool ftAmbig = false;
            if (r.FtFavBar >= 0 && r.FtAdvBar >= 0)
            {
                if (r.FtFavBar < r.FtAdvBar) firstTouch = 1;
                else if (r.FtAdvBar < r.FtFavBar) firstTouch = -1;
                else { firstTouch = 0; ftAmbig = true; }
            }
            else if (r.FtFavBar >= 0) firstTouch = 1;
            else if (r.FtAdvBar >= 0) firstTouch = -1;
            else firstTouch = 0;
            sb.Append(",\"barrierTicks\":").Append(F(r.BarrierTicks))
              .Append(",\"barsToTargetR\":").Append(r.FtFavBar)
              .Append(",\"barsToStopR\":").Append(r.FtAdvBar)
              .Append(",\"firstTouch\":").Append(firstTouch)
              .Append(",\"ftAmbig\":").Append(ftAmbig ? "true" : "false")
              .Append(",\"endReason\":").Append(Q(reason))
              .Append(",\"endTime\":").Append(Q(Iso(r.LastTime)))
              .Append('}');
            return sb.ToString();
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

                const float cw = 258f, ch = 150f;
                var slot = SentinelSkin.CardLayout.Place(this, ChartPanel,
                    ChartPanel.X, ChartPanel.Y, ChartPanel.W, ChartPanel.H, CardCorner, cw, ch);

                int openCount = _open != null ? _open.Count : 0;
                bool live = !_writerDead;
                var edge = openCount > 0 ? SentinelSkin.CAccent : (live ? SentinelSkin.CLine : SentinelSkin.CDim);
                var r = _sp.Card(slot.X, slot.Y, cw, ch, edge);

                _sp.Dot(r.Left + 5f, r.Top + 8f, live ? SentinelSkin.CAccent : SentinelSkin.CMute, live);
                _sp.Text("SENTINEL CANDIDATE", r.Left + 16f, r.Top, r.Width - 66f, 16f, SentinelSkin.CInk, 11f, true);
                bool dead = _writerDead || _writeFailed;
                string st = dead ? "NO REC" : (openCount > 0 ? "REC" : "IDLE");
                var stCol = dead ? SentinelSkin.CDown : (openCount > 0 ? SentinelSkin.CAccent : SentinelSkin.CMute);
                _sp.Pill(st, r.Right, r.Top - 1f, stCol);

                var lead = SharpDX.DirectWrite.TextAlignment.Leading;

                _sp.Text("TRACKING", r.Left, r.Top + 24f, 90f, 12f, SentinelSkin.CMute, 9f, true);
                _sp.Text(openCount.ToString(CultureInfo.InvariantCulture), r.Left, r.Top + 32f, 90f, 28f, SentinelSkin.CAccent, 24f);

                var regCol = _curRegime == "trend" ? SentinelSkin.CAccent
                           : _curRegime == "chop"  ? SentinelSkin.CMute
                           : _curRegime == "mid"   ? SentinelSkin.CInk2 : SentinelSkin.CMute;
                _sp.Text("REGIME", r.Left + 96f, r.Top + 24f, 120f, 12f, SentinelSkin.CMute, 9f, true);
                _sp.Text(_curRegime, r.Left + 96f, r.Top + 33f, 120f, 20f, regCol, 15f, true);
                _sp.Text("ADX " + _curAdx.ToString("0.0", CultureInfo.InvariantCulture),
                    r.Left + 96f, r.Top + 53f, 120f, 12f, SentinelSkin.CMute, 9.5f, false, lead, true);

                float frac = (float)Math.Max(0, Math.Min(1, _curAdx / 40.0));
                _sp.Track(r.Left, r.Top + 68f, r.Width, frac, _curRegime == "trend" ? SentinelSkin.CAccent : SentinelSkin.CFaint, 5f);

                _sp.Text("CONT candidates  " + _nCand,
                    r.Left, r.Top + 78f, r.Width, 14f, SentinelSkin.CInk2, 10.5f, false, lead, true);

                if (openCount > 0)
                {
                    Rec last = _open[_open.Count - 1];
                    var dirCol = _lastDir > 0 ? SentinelSkin.CUp : SentinelSkin.CDown;
                    _sp.Text("CONT" + (_lastDir > 0 ? " ▲" : " ▼") + " ×" + _lastRun,
                        r.Left, r.Top + 94f, 70f, 16f, dirCol, 10.5f, true, lead, true);
                    _sp.Text("MFE", r.Left + 74f, r.Top + 96f, 26f, 14f, SentinelSkin.CMute, 9f, true);
                    _sp.Text(last.MaxMFE.ToString("0", CultureInfo.InvariantCulture) + "t",
                        r.Left + 100f, r.Top + 94f, 38f, 16f, SentinelSkin.CUp, 11f, false, lead, true);
                    _sp.Text("MAE", r.Left + 140f, r.Top + 96f, 26f, 14f, SentinelSkin.CMute, 9f, true);
                    _sp.Text(last.MaxMAE.ToString("0", CultureInfo.InvariantCulture) + "t",
                        r.Left + 166f, r.Top + 94f, 40f, 16f, SentinelSkin.CDown, 11f, false, lead, true);
                }
                else
                {
                    _sp.Text("— no open records", r.Left, r.Top + 94f, r.Width, 16f, SentinelSkin.CMute, 10f, false, lead, true);
                }

                _sp.Text("rec " + _recorded + "   " + InstName() + " · " + SentinelCore.FriendlyBartag(BarTag()),
                    r.Left, r.Top + 110f, r.Width, 12f, SentinelSkin.CMute, 9f, false, lead, true);

                _sp.End();
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelCandidateRec.OnRender", _sx); }
        }

        private string InstName()
        {
            try { return Instrument != null && Instrument.MasterInstrument != null ? Instrument.MasterInstrument.Name : "unknown"; }
            catch { return "unknown"; }
        }

        private string _scope;
        // v1.3.0 — BARE (un-laned) scope for BAR-TYPE seams (BrickState/FluxState). Those are published by the
        // shared bars series, so they are keyed bare; the laned Scope() below returns null for them on a laned chart.
        private string BareScope()
        {
            try { return SentinelCore.ScopeOf(Instrument, BarsPeriod); } catch { return null; }
        }

        private string Scope()
        {
            if (_scope == null)
            {
                try { _scope = SentinelCore.ScopeOf(Instrument, BarsPeriod, ChartControl); } catch (Exception _sx) { SentinelCore.Swallow("SentinelCandidateRec.Scope", _sx); }
            }
            return _scope;
        }

        private string BarTag()
        {
            try
            {
                string t = SentinelCore.BarTag(BarsPeriod);
                string ln = SentinelCore.LaneOf(ChartControl);
                return string.IsNullOrEmpty(ln) ? t : t + "@" + ln;
            }
            catch { return "unknown"; }
        }

        private static string F(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "null";
            return Math.Round(v, 4).ToString(CultureInfo.InvariantCulture);
        }
        private static string Iso(DateTime dt) { return dt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture); }
        private static string Q(string s)
        {
            if (s == null) return "null";
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        #region Properties
        [NinjaScriptProperty]
        [Display(Name = "Show info card", Description = "Draw the Sentinel glass-card readout (off = pure headless recorder).", GroupName = "Recorder", Order = 1)]
        public bool ShowInfo { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Card corner", Description = "Which chart corner the card docks to. Cards in the same corner auto-stack (never overlap).", GroupName = "Recorder", Order = 2)]
        public SentinelCardCorner CardCorner { get; set; }

        // NOT a [NinjaScriptProperty] — serializes to the workspace + shows in F6 but stays OUT of the generated
        // constructor (no codegen churn). Default TRUE = capture the raw-tick path of every candidate (Phase-1 exit labels).
        [Display(Name = "Record tick path", Description = "ON (default): capture the raw last-trade tick PATH of every candidate to Sentinel\\Excursions\\candidates\\ticks\\<fireId>.jsonl. Turn OFF on an ultra-fast chart to record rows only (cheaper).", GroupName = "Recorder", Order = 3)]
        public bool RecordTickPath { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show indicator label", Description = "Show NinjaTrader's chart name label. Sentinel default = OFF (clean chart); turn on to restore it.", GroupName = "Sentinel", Order = 100)]
        public bool ShowIndicatorLabel { get; set; }
        #endregion
    }
}
