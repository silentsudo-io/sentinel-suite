// ─────────────────────────────────────────────────────────────────────────────
// This Source Code Form is subject to the terms of the Mozilla Public License,
// v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
// one at https://mozilla.org/MPL/2.0/.
//
// Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
// ─────────────────────────────────────────────────────────────────────────────
// ═════════════════════════════════════════════════════════════════════════════
//  SentinelAlertService — the audible/push channel for Sentinel alerts (NT8)
//  File: SentinelAlertService_v1_0_0.cs
//  Version: v1.0.0
// ─────────────────────────────────────────────────────────────────────────────
//  WHAT THIS IS  (Hardening Substrate 3 — the "learn about it when you're NOT staring
//    at the screen" half of Alerts; see Docs/SENTINEL_HARDENING_FRAMEWORK.md)
//    A headless, always-on AddOnBase singleton that subscribes to SentinelCore.Alerts.Raised
//    and turns each alert into something you can NOTICE away from the chart:
//      • SOUND — a .wav (SoundPlayer) or a System sound fallback. Critical always; Info opt-in.
//      • PUSH  — an optional shell command run on the alert (e.g. curl to Pushover/ntfy/Slack),
//                so a phone push is a config line, not hardcoded to any provider. Opt-in, empty=off.
//    Over-alerting trains you to ignore alerts, so this mirrors Alerts' two-tier design: Critical
//    is rare by construction and gets the loud treatment; Info is quiet/opt-in. A per-level throttle
//    stops a burst from machine-gunning the speaker.
//
//  CONFIG  <UserDataDir>\Sentinel\Alerts.conf  (key=value; re-read on NT restart). All optional:
//      enabled=true          # master switch for the channel
//      playInfo=false        # also play a (soft) sound on Info alerts
//      throttleSec=3         # min seconds between sounds of the same level
//      critWav=              # path to a .wav for Critical; empty → SystemSounds.Hand
//      infoWav=              # path to a .wav for Info;     empty → SystemSounds.Asterisk
//      pushCommand=          # shell command run on an alert; {level} {title} {detail} substituted
//      pushOnInfo=false      # run pushCommand on Info too (default: Critical only)
//    Missing file = sensible defaults (Critical sound ON, Info off, no push).
//
//  SAFETY: never throws into NT. The Raised handler is wrapped; sound + push run on the thread pool
//    (never block the alert path); teardown sets a flag first so in-flight callbacks bail. Sound is
//    a NOTIFICATION only — it does not act on the account (that's the Gate / auto-flatten's job).
//
//  CHANGELOG
//    v1.0.1 — (in-place) LIVE CONFIG API for the dashboard Test tab: GetConfig()/Apply(cfg)/Reload().
//             Apply persists to Alerts.conf AND updates the running service with no NT restart. Test-tab
//             buttons fire a real Alerts.Info/Critical to exercise the whole path (sound+push+ledger).
//    v1.0.0 — initial: subscribe SentinelCore.Alerts.Raised → sound (wav/SystemSounds) + optional push
//             shell command, two-tier (Critical loud / Info quiet-opt-in), per-level throttle, Alerts.conf.
// ═════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NinjaTrader.NinjaScript;

namespace NinjaTrader.NinjaScript.AddOns.Sentinel
{
    public class SentinelAlertService_v1_0_0 : NinjaTrader.NinjaScript.AddOnBase
    {
        public static SentinelAlertService_v1_0_0 Instance { get; private set; }

        private bool _started;
        private volatile bool _stopping;
        private Action<SentinelCore.AlertItem> _handler;

        // config (loaded on Start)
        private bool   _enabled = true;
        private bool   _playInfo = false;
        private double _throttleSec = 3;
        private string _critWav = null;
        private string _infoWav = null;
        private string _pushCommand = null;
        private bool   _pushOnInfo = false;

        // per-level sound throttle
        private DateTime _lastCritSound = DateTime.MinValue;
        private DateTime _lastInfoSound = DateTime.MinValue;

        private static string ConfPath { get { return Path.Combine(SentinelCore.SettingsDir, "Alerts.conf"); } }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "SentinelAlertService";
                Description = "Sentinel Suite — audible + push channel for alerts (subscribes SentinelCore.Alerts). "
                            + "Critical sound on by default; config in Sentinel\\Alerts.conf. Runs always.";
            }
            else if (State == State.Active)     Start();
            else if (State == State.Terminated) Stop();
        }

        private void Start()
        {
            if (_started) return;
            _stopping = false;
            _started = true;
            Instance = this;
            LoadConfig();
            _handler = OnAlert;
            SentinelCore.Alerts.Raised += _handler;
            SentinelCore.Log("AlertCh", "SentinelAlertService started (enabled=" + _enabled
                + ", playInfo=" + _playInfo + ", push=" + (!string.IsNullOrEmpty(_pushCommand)) + ").");
        }

        private void Stop()
        {
            if (!_started) return;
            _stopping = true;
            _started = false;
            if (_handler != null) { try { SentinelCore.Alerts.Raised -= _handler; } catch (Exception _sx) { SentinelCore.Swallow("SentinelAlert.Stop", _sx); } _handler = null; }
            if (Instance == this) Instance = null;
            SentinelCore.Log("AlertCh", "SentinelAlertService stopped.");
        }

        // ── the whole engine: alert → sound + push ───────────────────────────────
        private void OnAlert(SentinelCore.AlertItem a)
        {
            if (_stopping || !_started || !_enabled || a == null) return;
            try
            {
                bool crit = a.Level == SentinelCore.AlertLevel.Critical;

                // SOUND (two-tier + throttle)
                if (crit || _playInfo)
                {
                    DateTime now = DateTime.UtcNow;
                    DateTime last = crit ? _lastCritSound : _lastInfoSound;
                    if ((now - last).TotalSeconds >= _throttleSec)
                    {
                        if (crit) _lastCritSound = now; else _lastInfoSound = now;
                        string wav = crit ? _critWav : _infoWav;
                        System.Threading.Tasks.Task.Run(() => PlaySound(wav, crit));
                    }
                }

                // PUSH (opt-in shell command; Critical only unless pushOnInfo)
                if (!string.IsNullOrEmpty(_pushCommand) && (crit || _pushOnInfo))
                {
                    string cmd = _pushCommand
                        .Replace("{level}", crit ? "CRITICAL" : "INFO")
                        .Replace("{title}", a.Title ?? "")
                        .Replace("{detail}", a.Detail ?? "");
                    System.Threading.Tasks.Task.Run(() => RunPush(cmd));
                }
            }
            catch (Exception ex) { try { SentinelCore.Log("AlertCh", "OnAlert error: " + ex.Message); } catch (Exception _sx) { SentinelCore.Swallow("SentinelAlert.OnAlert", _sx); } }
        }

        private void PlaySound(string wav, bool crit)
        {
            try
            {
                if (!string.IsNullOrEmpty(wav) && File.Exists(wav))
                {
                    using (var p = new System.Media.SoundPlayer(wav)) p.PlaySync();
                    return;
                }
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelAlert.PlaySound", _sx); }
            // fallback: a distinct system sound per tier (no external asset needed)
            try
            {
                if (crit) System.Media.SystemSounds.Hand.Play();
                else      System.Media.SystemSounds.Asterisk.Play();
            }
            catch (Exception _sx) { SentinelCore.Swallow("SentinelAlert.PlaySound", _sx); }
        }

        private void RunPush(string command)
        {
            try
            {
                // run through the shell so users can write a full pipeline (curl … | …) in one line
                var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c " + command)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex) { try { SentinelCore.Log("AlertCh", "push failed: " + ex.Message); } catch (Exception _sx) { SentinelCore.Swallow("SentinelAlert.RunPush", _sx); } }
        }

        // ── tiny key=value config reader ─────────────────────────────────────────
        private void LoadConfig()
        {
            try
            {
                if (!File.Exists(ConfPath)) return;
                foreach (var raw in File.ReadAllLines(ConfPath))
                {
                    string line = raw == null ? "" : raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string val = line.Substring(eq + 1).Trim();
                    // strip trailing inline comment for scalar keys (leave push/wav paths intact)
                    switch (key)
                    {
                        case "enabled":     _enabled = ParseBool(val, _enabled); break;
                        case "playinfo":    _playInfo = ParseBool(val, _playInfo); break;
                        case "pushoninfo":  _pushOnInfo = ParseBool(val, _pushOnInfo); break;
                        case "throttlesec": { double d; if (double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out d) && d >= 0) _throttleSec = d; break; }
                        case "critwav":     _critWav = EmptyToNull(val); break;
                        case "infowav":     _infoWav = EmptyToNull(val); break;
                        case "pushcommand": _pushCommand = EmptyToNull(val); break;
                    }
                }
            }
            catch (Exception ex) { try { SentinelCore.Log("AlertCh", "config read error: " + ex.Message); } catch (Exception _sx) { SentinelCore.Swallow("SentinelAlert.LoadConfig", _sx); } }
        }

        private static string EmptyToNull(string s) { return string.IsNullOrWhiteSpace(s) ? null : s; }
        private static bool ParseBool(string s, bool dflt)
        {
            if (string.IsNullOrEmpty(s)) return dflt;
            s = s.Trim().ToLowerInvariant();
            if (s == "true" || s == "1" || s == "yes" || s == "on") return true;
            if (s == "false" || s == "0" || s == "no" || s == "off") return false;
            return dflt;
        }

        // ── LIVE CONFIG API (the dashboard Test tab reads/edits this — no NT restart needed) ──
        public sealed class AlertChannelConfig
        {
            public bool Enabled = true, PlayInfo = false, PushOnInfo = false;
            public double ThrottleSec = 3;
            public string CritWav = "", InfoWav = "", PushCommand = "";
        }

        /// <summary>Snapshot the live config (for the dashboard to display).</summary>
        public AlertChannelConfig GetConfig()
        {
            return new AlertChannelConfig
            {
                Enabled = _enabled, PlayInfo = _playInfo, PushOnInfo = _pushOnInfo,
                ThrottleSec = _throttleSec, CritWav = _critWav ?? "", InfoWav = _infoWav ?? "", PushCommand = _pushCommand ?? ""
            };
        }

        /// <summary>Apply a config to the LIVE service and persist it to Alerts.conf (no restart needed).</summary>
        public void Apply(AlertChannelConfig c)
        {
            if (c == null) return;
            _enabled = c.Enabled; _playInfo = c.PlayInfo; _pushOnInfo = c.PushOnInfo;
            _throttleSec = c.ThrottleSec < 0 ? 0 : c.ThrottleSec;
            _critWav = EmptyToNull(c.CritWav); _infoWav = EmptyToNull(c.InfoWav); _pushCommand = EmptyToNull(c.PushCommand);
            WriteConfig(c);
            SentinelCore.Log("AlertCh", "config applied via dashboard (enabled=" + _enabled + ", playInfo=" + _playInfo
                + ", push=" + (!string.IsNullOrEmpty(_pushCommand)) + ").");
        }

        /// <summary>Re-read Alerts.conf into the live service (if it was hand-edited).</summary>
        public void Reload() { LoadConfig(); }

        private void WriteConfig(AlertChannelConfig c)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# Sentinel Alerts channel — saved by the dashboard Test tab. Safe to hand-edit; re-read on NT restart or the Test tab's Reload.");
                sb.AppendLine("enabled=" + (c.Enabled ? "true" : "false"));
                sb.AppendLine("playInfo=" + (c.PlayInfo ? "true" : "false"));
                sb.AppendLine("throttleSec=" + c.ThrottleSec.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("critWav=" + (c.CritWav ?? ""));
                sb.AppendLine("infoWav=" + (c.InfoWav ?? ""));
                sb.AppendLine("# pushCommand: {level} {title} {detail} substituted; runs via cmd /c. Examples:");
                sb.AppendLine("#   ntfy: curl -s -H \"Title: Sentinel {level}\" -d \"{title} {detail}\" https://ntfy.sh/YOUR_TOPIC");
                sb.AppendLine("pushCommand=" + (c.PushCommand ?? ""));
                sb.AppendLine("pushOnInfo=" + (c.PushOnInfo ? "true" : "false"));
                File.WriteAllText(ConfPath, sb.ToString());
            }
            catch (Exception ex) { try { SentinelCore.Log("AlertCh", "config write error: " + ex.Message); } catch (Exception _sx) { SentinelCore.Swallow("SentinelAlert.WriteConfig", _sx); } }
        }
    }
}
