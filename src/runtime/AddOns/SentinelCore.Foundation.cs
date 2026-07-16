// ─────────────────────────────────────────────────────────────────────────────
// This Source Code Form is subject to the terms of the Mozilla Public License,
// v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
// one at https://mozilla.org/MPL/2.0/.
//
// Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
// ─────────────────────────────────────────────────────────────────────────────
// ═════════════════════════════════════════════════════════════════════════════
//  SentinelCore — FOUNDATION layer  (partial)
//  File: SentinelCore.Foundation.cs   |   part of `static partial class SentinelCore`
// ─────────────────────────────────────────────────────────────────────────────
//  PRODUCT-LADDER RUNTIME SPLIT — see Docs/PRODUCT_LADDER.md §4 (the runtime floor)
//  + §5 (the core-split finding). SentinelCore is being split into three co-operating
//  PARTIAL files so a distribution bundle can ship only the layers it needs:
//      • SentinelCore.Foundation.cs  (F)  — SettingsDir, Log, SeamStore<T>, ScopeOf,
//        BarTag, InstrumentRoot, Conditions, Alerts, + the context vetoes
//        (kill-switch / instrument-kill / news / rollover)
//      • SentinelCore.Bus.cs         (L1) — the …State publish/consult seam registry
//      • SentinelCore_v1_0_0.cs      (L2 + remainder, for now) — Gate/Ledger/State/governor
//
//  DEPENDENCY RULE (§4): a file may reference only its own layer or below. Nothing in
//  Foundation references L1 (Bus) or L2 (Safety). Verified: the seams reach DOWN into
//  Conditions/Log (Foundation); the Gate never reads a seam.
//
//  STATUS — the FOUNDATION partial, populated F5-verified per batch. DONE (batch 1, F5-clean):
//  SettingsDir / SettingsFile / LogFile / Log / WriteLogFile. (Ledger/State are also Foundation but
//  still sit in the main file for now — fine, same class.) Same class, same call sites -> zero churn.
// ═════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.Sentinel
{
    public static partial class SentinelCore
    {
        // ─────────────────────────────────────────────────────────────────────
        //  SETTINGS DIRECTORY — one folder all tools persist config into.
        //  <UserDataDir>\Sentinel\   (created on first access).
        // ─────────────────────────────────────────────────────────────────────
        public static string SettingsDir
        {
            get
            {
                string dir;
                try { dir = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "Sentinel"); }
                catch { dir = "Sentinel"; }
                try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); } catch { }
                return dir;
            }
        }

        /// <summary>Full path for a tool's settings file, e.g. SettingsFile("Copy") -> ...\Sentinel\Copy.json</summary>
        public static string SettingsFile(string toolName)
        {
            return Path.Combine(SettingsDir, (toolName ?? "tool") + ".json");
        }

        /// <summary>The rolling text log every Sentinel tool writes to (readable outside NT).</summary>
        public static string LogFile { get { return Path.Combine(SettingsDir, "sentinel.log"); } }

        // ─────────────────────────────────────────────────────────────────────
        //  LOG — tagged output. Tools call SentinelCore.Log("Copy", msg): the line
        //  goes to the NinjaScript Output window AND is appended (timestamped) to
        //  <UserDataDir>\Sentinel\sentinel.log so it's readable without a screenshot.
        // ─────────────────────────────────────────────────────────────────────
        private static readonly object _logLock = new object();

        public static void Log(string tool, string msg)
        {
            string line = "[Sentinel:" + (tool ?? "?") + "] " + msg;
            try
            {
                NinjaTrader.Code.Output.Process(line, NinjaTrader.NinjaScript.PrintTo.OutputTab1);
            }
            catch { }
            WriteLogFile(line);
        }

        private static void WriteLogFile(string line)
        {
            try
            {
                lock (_logLock)
                {
                    string path = LogFile;
                    // rotate once at ~5 MB so the file stays readable and bounded
                    try
                    {
                        var fi = new FileInfo(path);
                        if (fi.Exists && fi.Length > 5 * 1024 * 1024)
                        {
                            string bak = path + ".1";
                            if (File.Exists(bak)) File.Delete(bak);
                            File.Move(path, bak);
                        }
                    }
                    catch { }

                    // timestamp with millis; DateTime.Now is fine here (real NT runtime, not a workflow).
                    string stamped = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + line + Environment.NewLine;
                    File.AppendAllText(path, stamped, Encoding.UTF8);
                }
            }
            catch { }   // logging must never throw into a tool's trading path
        }
    }
}
