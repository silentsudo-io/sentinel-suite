// ─────────────────────────────────────────────────────────────────────────────
// This Source Code Form is subject to the terms of the Mozilla Public License,
// v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
// one at https://mozilla.org/MPL/2.0/.
//
// Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
// ─────────────────────────────────────────────────────────────────────────────
// ═════════════════════════════════════════════════════════════════════════════
//  CouncilFusion — the pure fusion core of the Sentinel Council (NT8, Sentinel Suite)
//  File: CouncilFusion.cs   ·   namespace …AddOns.Sentinel
// ─────────────────────────────────────────────────────────────────────────────
//  WHAT THIS IS
//    The Council's decision math, extracted as a PURE FUNCTION with no NinjaTrader / seam / wall-clock
//    dependency. Given a set of gathered votes + modulator readings + veto flag + config, it returns the
//    verdict (bias · conviction · sizeMult · tally). It performs NO seam reads and NO I/O — every input is
//    resolved by the caller.
//
//  WHY IT EXISTS (Docs/SENTINEL_REPLAY_SPEC.md §3)
//    So there can be TWO front-ends over ONE fusion truth:
//      • the LIVE Council      — gathers votes + modulators FROM SEAMS, then calls Fuse().
//      • the REPLAY harness     — gathers the same votes FROM HOSTED sensor instances (bar-by-bar, causal),
//                                 then calls the SAME Fuse().
//    Identical math both places ⇒ a historical (replay) verdict equals the verdict that would have been live
//    on that bar — the correctness gate that makes a replay-baked corpus trainable at all (§4). It is also
//    the seam of the generic vote registry (a vote is a vote, seam or hosted — memory: council-custom-voters).
//
//  PARITY
//    This mirrors Council_v1_0_0.OnBarUpdate's fuse block (v1.8.0) line-for-line: kind-aware denomW,
//    deadband→bias, conviction = |netScore| / denomW, the full context-damp chain (breadth · squeeze · clock ·
//    participation · MTF · location · PROFILE/InValueDamp · REGIME/HighVolRegimeDamp · FLUX-absorb/FluxAbsorbDamp),
//    then veto → sizeMult. As of Council v1.8.1 the Council CALLS this — this file is now the ONLY copy of the math.
//    NOTE the account/seam VETO (kill · news · rollover · chop · liquidity WALL) stays in the front-end: it is
//    resolved AFTER Fuse (the wall veto needs the fused bias) and applied by zeroing sizeMult+conviction, which is
//    bit-identical to passing Vetoed=true into Fuse. Both front-ends (live Council + replay harness) do the same.
// ═════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;

namespace NinjaTrader.NinjaScript.AddOns.Sentinel
{
    public static class CouncilFusion
    {
        public enum Kind { State, Trigger }

        /// <summary>One gathered vote. Built by the front-end (from a seam, or from a hosted sensor).
        /// Dir = -1/0/+1; W = the resolved weight (Roster override or F6 property, incl. any per-sensor
        /// strength multiplier); Counted = the weight was &gt; 0 (a w=0 explorer is recorded but inert).</summary>
        public struct Vote
        {
            public string Tag;
            public int    Dir;
            public double W;
            public bool   Counted;
            public Vote(string tag, int dir, double w)
            {
                Tag = tag; Dir = Math.Sign(dir); W = w; Counted = w > 0;
            }
        }

        /// <summary>Fusion policy — supplied by the front-end from its F6 properties so the two front-ends
        /// share the numbers as well as the math. Defaults mirror the Council's shipped defaults.</summary>
        public sealed class Config
        {
            public double BiasDeadband      = 0.15;   // net must exceed this fraction of denomW to pick a side
            public double ConvictionFloor   = 0.20;   // below this, sizeMult = 0 (no actionable edge)
            public int    MinVoters         = 3;      // breadth-damp threshold
            public bool   DampenOnSqueeze   = true;
            public double SqueezeDamp       = 0.6;
            public double OffSessionDamp    = 0.50;
            public double MiddayDamp        = 0.85;
            public double RvolDampFloor     = 0.50;
            public double MtfCounterDamp    = 0.60;
            public double LevelDamp         = 0.70;
            public double InValueDamp       = 0.75;   // Profile: price ACCEPTED inside the value area (chop context)
            public double HighVolRegimeDamp = 0.70;   // Regime: high-volatility / chaotic regime
            public double FluxAbsorbDamp    = 0.60;   // Flux tape ABSORBING against the bias (soft veto on size)
        }

        /// <summary>The resolved modulator + veto readings for one bar. The front-end fills whatever it has;
        /// absent readings must be passed as their NEUTRAL value (documented per field) so they don't damp.</summary>
        public sealed class Inputs
        {
            public List<Vote> Votes = new List<Vote>();

            // roster (for the kind-aware denominator). Declared == null ⇒ pre-roster fallback (uses activeW).
            public List<string>                  Declared;
            public Dictionary<string, double>    WeightOf = new Dictionary<string, double>(StringComparer.Ordinal);
            public Dictionary<string, Kind>      KindOf   = new Dictionary<string, Kind>(StringComparer.Ordinal);
            public HashSet<string>               Spoke    = new HashSet<string>(StringComparer.Ordinal);

            // modulators (neutral = no damp)
            public bool   Squeeze   = false;   // neutral false
            public bool   InSession = true;    // neutral true (no off-session damp)
            public int    ClockPhase = -1;     // 2 = midday (damp); anything else = no clock damp
            public double Rvol      = double.NaN;  // NaN = no participation reading
            public int    MtfBias   = 0;       // 0 = no higher-timeframe reading (no counter-trend damp)
            public bool   LvlInPathLong  = false; // resistance within range ABOVE (damps a LONG) — Fuse picks by bias
            public bool   LvlInPathShort = false; // support within range BELOW (damps a SHORT) — Fuse picks by bias
            public bool   InValue       = false;  // neutral false — price ACCEPTED inside the value area (Profile chop damp)
            public bool   HighVolRegime = false;  // neutral false — high-volatility / chaotic regime damp
            public int    FluxFlowDir   = 0;      // net order-flow dir of the Flux tape (0 = no reading; absorption when it OPPOSES bias)
            public int    FluxDiverge   = 0;      // Flux flow-vs-price divergence magnitude (0 = none)

            // hard veto — the front-end resolves it (kill / news / rollover / wall …); neutral false
            public bool   Vetoed    = false;
        }

        public struct Result
        {
            public int    Bias;
            public double Conviction;
            public double SizeMult;
            public double ContextMult;
            public double NetScore;
            public double DenomW;
            public int    Agree, Disagree, Voters;
        }

        /// <summary>Fuse the gathered inputs into a verdict. Pure: no seam reads, no I/O, no wall-clock.</summary>
        public static Result Fuse(Inputs x, Config cfg)
        {
            var r = new Result();
            if (x == null) return r;
            if (cfg == null) cfg = new Config();

            // ── derive netScore / activeW / voters from the votes (AddVote's accumulation) ──
            double netScore = 0, activeW = 0;
            int voters = 0;
            var votes = x.Votes ?? new List<Vote>();
            foreach (var v in votes)
            {
                if (!v.Counted) continue;                 // w=0 explorer / undeclared — recorded but inert
                voters++;                                  // a fresh, counting reading existed (even if neutral)
                if (v.Dir != 0) { netScore += Math.Sign(v.Dir) * v.W; activeW += v.W; }
            }

            // ── kind-aware denominator (absence dilutes; a quiet TRIGGER does not) ──
            double declaredW = 0;
            if (x.Declared != null)
                foreach (string t in x.Declared) declaredW += Math.Max(0.0, WeightOf(x, t));
            double denomW = declaredW > 0 ? declaredW : activeW;   // static fallback (pre-roster)
            if (x.Declared != null)
            {
                double eff = 0;
                foreach (string t in x.Declared)
                {
                    double w = Math.Max(0.0, WeightOf(x, t));
                    if (w <= 0) continue;                                  // w=0 explorer never enters the denominator
                    if (KindOf(x, t) == Kind.State) { eff += w; continue; }// STATE always dilutes
                    if (!x.Spoke.Contains(t)) { eff += w; continue; }      // absent TRIGGER → unknown → dilute
                    int d = 0;
                    foreach (var v in votes) if (v.Tag == t) { d = v.Dir; break; }
                    if (d != 0) eff += w;                                   // fired TRIGGER counts (also in netScore)
                    // present & QUIET trigger → contributes nothing
                }
                if (eff > 0) denomW = eff;
            }

            // ── bias + conviction ──
            int bias = 0;
            double conviction = 0;
            if (denomW > 0)
            {
                double deadband = cfg.BiasDeadband * denomW;
                if (netScore > deadband) bias = 1;
                else if (netScore < -deadband) bias = -1;
                conviction = Math.Min(1.0, Math.Abs(netScore) / denomW);
            }

            // ── context damping (applies to SIZE, never to agreement) ──
            double contextMult = 1.0;
            if (cfg.MinVoters > 1 && voters < cfg.MinVoters) contextMult *= (double)voters / cfg.MinVoters;
            if (cfg.DampenOnSqueeze && x.Squeeze) contextMult *= cfg.SqueezeDamp;
            if (!x.InSession) contextMult *= cfg.OffSessionDamp;
            else if (x.ClockPhase == 2) contextMult *= cfg.MiddayDamp;
            if (!double.IsNaN(x.Rvol)) contextMult *= Math.Min(1.0, Math.Max(cfg.RvolDampFloor, x.Rvol));
            if (bias != 0 && x.MtfBias != 0 && x.MtfBias != bias) contextMult *= cfg.MtfCounterDamp;
            if ((bias > 0 && x.LvlInPathLong) || (bias < 0 && x.LvlInPathShort)) contextMult *= cfg.LevelDamp;
            if (x.InValue) contextMult *= cfg.InValueDamp;                                   // Profile: price accepted in value → chop
            if (x.HighVolRegime) contextMult *= cfg.HighVolRegimeDamp;                       // Regime: high-vol / chaotic
            if (bias != 0 && x.FluxDiverge != 0 && x.FluxFlowDir != 0 &&                     // Flux tape ABSORBING against the bias (soft veto on size)
                ((bias > 0 && x.FluxFlowDir < 0) || (bias < 0 && x.FluxFlowDir > 0)))
                contextMult *= cfg.FluxAbsorbDamp;

            conviction  = Math.Max(0.0, Math.Min(1.0, conviction));
            contextMult = Math.Max(0.0, Math.Min(1.0, contextMult));

            // ── agree / disagree tally (counted, directional voters only) ──
            int agree = 0, disagree = 0;
            foreach (var v in votes)
            {
                if (v.Dir == 0 || !v.Counted) continue;
                if (bias != 0 && v.Dir == bias) agree++;
                else if (bias != 0 && v.Dir == -bias) disagree++;
            }

            // ── veto → sizeMult (the floor gates AGREEMENT; context shrinks the position) ──
            double sizeMult = (x.Vetoed || bias == 0 || conviction < cfg.ConvictionFloor) ? 0.0 : conviction * contextMult;
            if (x.Vetoed) conviction = 0.0;

            r.Bias = bias; r.Conviction = conviction; r.SizeMult = sizeMult; r.ContextMult = contextMult;
            r.NetScore = netScore; r.DenomW = denomW;
            r.Agree = agree; r.Disagree = disagree; r.Voters = voters;
            return r;
        }

        private static double WeightOf(Inputs x, string tag)
        {
            double w;
            return x.WeightOf != null && x.WeightOf.TryGetValue(tag, out w) ? w : 0.0;
        }
        private static Kind KindOf(Inputs x, string tag)
        {
            Kind k;
            return x.KindOf != null && x.KindOf.TryGetValue(tag, out k) ? k : Kind.State;
        }
    }
}
