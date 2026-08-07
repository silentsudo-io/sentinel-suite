"""conductor_arm — deliberately authorise ONE unattended Conductor cold start, per node.

WHY THIS EXISTS
---------------
`autostart = true` in Run.conf used to mean "run on EVERY login, forever". On 2026-08-02 that fired
a cell nobody asked for on legacy-node: 144 minutes at 100x, no strategy loaded, zero corpus rows, and
nothing complained. Conductor v0.2.0 splits the two cases that both looked like autostart --
a RESUME of an in-flight bake still starts on its own, but a COLD START now needs a token that
EXPIRES and is CONSUMED on use. This writes that token.

    python conductor_arm.py status                      # who is armed, and what for
    python conductor_arm.py arm --node worker-1
    python conductor_arm.py arm --node all --ttl 6
    python conductor_arm.py disarm --node worker-3

WHAT MAKES THIS AN INTENT AND NOT ANOTHER STANDING FLAG
-------------------------------------------------------
  * it EXPIRES (`ttlHours`, default 12) -- an arm you forgot about stops being permission;
  * it is CONSUMED -- the Conductor renames it on use, so it cannot authorise the next restart;
  * it PINS THE MANIFEST -- the token carries a fingerprint of Run.conf's job lines, so editing
    what actually runs invalidates the arm. (Editing `heartbeatSec` does not, deliberately.)

⚠ Arming a node whose chart is not built is still a way to bake junk -- the token authorises, it
does not verify. That is what the productivity gate is for: it aborts a run that writes nothing.
Belt and braces, on purpose, because they fail differently.
"""
from __future__ import annotations

import argparse
import datetime
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "sync"))

from lab_faults import swallow  # noqa: E402
import muster  # noqa: E402

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

CONF_DIR = r"C:\Users\Administrator\Documents\NinjaTrader 8\Sentinel\Conductor"
TOKEN = CONF_DIR + r"\armed.token"
RUNCONF = CONF_DIR + r"\Run.conf"


# Fetch Run.conf as base64 and hash it HERE, in Python. Doing the arithmetic in PowerShell was the
# first attempt and it was a bad idea: PS has no int32 wraparound, so emulating C#'s `unchecked` needed
# hand-rolled sign correction, and any slip refuses every arm while looking like a Conductor bug.
READ_PS = r"""
$ErrorActionPreference='SilentlyContinue'
$p = '%s'
if (-not (Test-Path $p)) { 'ERR|no Run.conf'; exit }
'B64|' + [Convert]::ToBase64String([System.IO.File]::ReadAllBytes($p))
""" % RUNCONF


def fingerprint_of(text: str):
    """Byte-for-byte equivalent of SentinelConductor.ComputeManifestFingerprint().

    Same line filter as the Conductor's PASS 2 (skip blank, skip full-line '#', require a '|'),
    trimmed, joined with '\\n'. C# `int` overflow wraps to signed 32-bit; masking to 0xFFFFFFFF keeps
    the identical bit pattern, and the final 0x7FFFFFFF drops the sign bit exactly as C# does.
    """
    out, n = [], 0
    for raw in text.replace("\r\n", "\n").replace("\r", "\n").split("\n"):
        t = raw.strip()
        if not t or t[0] == "#" or "|" not in t:
            continue
        out.append(t)
        n += 1
    s = "".join(x + "\n" for x in out)
    h = 17
    for ch in s:
        h = (h * 31 + ord(ch)) & 0xFFFFFFFF
    return "%08x" % (h & 0x7FFFFFFF), n


def fingerprint(node: str):
    rc, out, err = muster.ps_remote(muster.NODES[node]["ssh"], READ_PS, timeout=120)
    if rc != 0:
        return None, 0, (err or "unreachable").strip()[:100]
    import base64
    for line in out.strip().splitlines():
        line = line.strip()
        if line.startswith("B64|"):
            try:
                text = base64.b64decode(line[4:]).decode("utf-8", "replace")
            except Exception as e:
                swallow("conductor_arm.decode", e, node)
                return None, 0, "Run.conf undecodable"
            fp, n = fingerprint_of(text)
            return fp, n, None
        if line.startswith("ERR|"):
            return None, 0, line[4:]
    return None, 0, "no content returned"


def cmd_status(args) -> int:
    nodes = muster.SENTRIES + ["legacy-node"] if args.node in (None, "all") else [args.node]
    ps = r"""
$ErrorActionPreference='SilentlyContinue'
$t = '%s'; $c = '%s'
$auto = ''
if (Test-Path $c) {
  foreach ($raw in [System.IO.File]::ReadAllLines($c, [System.Text.Encoding]::UTF8)) {
    $l = $raw; $h = $l.IndexOf('#'); if ($h -ge 0) { $l = $l.Substring(0,$h) }
    if ($l -match '^\s*autostart\s*=\s*(\S+)') { $auto = $Matches[1] }
    if ($l -match '^\s*requireArm\s*=\s*(\S+)') { $req = $Matches[1] }
  }
}
$o = 'autostart=' + $(if($auto){$auto}else{'(unset)'}) + ' requireArm=' + $(if($req){$req}else{'(default true)'})
if (Test-Path $t) {
  $age = [int]((Get-Date).ToUniversalTime() - (Get-Item $t).LastWriteTimeUtc).TotalMinutes
  $o += '  TOKEN present (' + $age + ' min old)'
} else { $o += '  no token' }
$used = (Get-ChildItem ($t + '.used-*') | Measure-Object).Count
$o += '  used=' + $used
$o
""" % (TOKEN, RUNCONF)
    for n in nodes:
        rc, out, err = muster.ps_remote(muster.NODES[n]["ssh"], ps, timeout=120)
        fp, njobs, ferr = fingerprint(n)
        line = (out.strip().splitlines() or [(err or "unreachable")])[-1]
        print("%-9s %s  manifest=%s jobs=%s" % (n, line.strip(), fp or ("? " + str(ferr)), njobs))
    return 0


def cmd_arm(args) -> int:
    nodes = muster.SENTRIES if args.node == "all" else [args.node]
    for n in nodes:
        if n not in muster.NODES:
            print("%-9s ✖ unknown node" % n)
            continue
        fp, njobs, ferr = fingerprint(n)
        if fp is None:
            print("%-9s ✖ cannot fingerprint Run.conf: %s" % (n, ferr))
            continue
        if njobs == 0:
            print("%-9s ✖ Run.conf declares ZERO jobs — arming this would authorise nothing" % n)
            continue

        st = muster.get_status(n)
        if muster.baking(st):
            print("%-9s ✖ looks MID-BAKE (rows %ss old). Arming now would queue a second cold start" % (n, st.get("row_age_s")))
            print("            behind a live one. A resume needs no token anyway. Use --force to override.")
            if not args.force:
                continue

        # UTC, ISO-8601 with Z -- the Conductor parses with AssumeUniversal, and a local stamp here
        # would silently shift the TTL by the box's offset (the estate spans zones by accident before).
        now = datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
        body = (
            "# Sentinel Conductor - ARMING TOKEN\n"
            "# Authorises exactly ONE unattended COLD START. Expires, and is CONSUMED on use\n"
            "# (renamed to armed.token.used-<stamp>). A RESUME of an in-flight bake needs no token.\n"
            "armedUtc = %s\n"
            "ttlHours = %d\n"
            "manifest = %s\n"
            "cell     = %s\n"
            "by       = %s\n"
            "note     = %s\n"
        ) % (now, args.ttl, fp, args.cell or ("%d job(s)" % njobs), args.by, args.note or "")

        ps = (
            "$ErrorActionPreference='Stop'\n"
            "$d = '" + CONF_DIR + "'\n"
            "if (-not (Test-Path $d)) { New-Item -ItemType Directory -Force -Path $d | Out-Null }\n"
            "$b = [Convert]::FromBase64String('" + __import__("base64").b64encode(body.encode()).decode() + "')\n"
            "[System.IO.File]::WriteAllBytes('" + TOKEN + "', $b)\n"
            "'WROTE|' + (Get-Item '" + TOKEN + "').Length\n"
        )
        rc, out, err = muster.ps_remote(muster.NODES[n]["ssh"], ps, timeout=120)
        ok = "WROTE|" in out
        print("%-9s %s  manifest=%s ttl=%dh jobs=%d"
              % (n, "✓ ARMED" if ok else ("✖ " + (err or out).strip()[:80]), fp, args.ttl, njobs))
    print("\n⚠ Arming authorises; it does not verify the chart is built. The productivity gate is")
    print("  what stops a run that writes nothing — they fail differently, which is the point.")
    return 0


def cmd_disarm(args) -> int:
    nodes = muster.SENTRIES if args.node == "all" else [args.node]
    ps = ("$ErrorActionPreference='SilentlyContinue'\n"
          "if (Test-Path '" + TOKEN + "') { Remove-Item '" + TOKEN + "' -Force; 'REMOVED' } else { 'none' }\n")
    for n in nodes:
        rc, out, err = muster.ps_remote(muster.NODES[n]["ssh"], ps, timeout=120)
        print("%-9s %s" % (n, (out.strip() or err.strip() or "rc=%d" % rc)))
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(description="conductor_arm — authorise ONE unattended Conductor cold start")
    sub = ap.add_subparsers(dest="cmd")

    s = sub.add_parser("status", help="arming state across the fleet")
    s.add_argument("--node", default="all")
    s.set_defaults(fn=cmd_status)

    a = sub.add_parser("arm", help="write a single-use, expiring token")
    a.add_argument("--node", required=True, help="a node name or 'all' (workers only)")
    a.add_argument("--ttl", type=int, default=12, help="hours the token stays valid (default 12)")
    a.add_argument("--cell", help="human label for what this authorises")
    a.add_argument("--by", default=os.environ.get("USERNAME", "operator"))
    a.add_argument("--note")
    a.add_argument("--force", action="store_true", help="arm even if the node looks mid-bake (say why)")
    a.set_defaults(fn=cmd_arm)

    d = sub.add_parser("disarm", help="remove an unused token")
    d.add_argument("--node", required=True)
    d.set_defaults(fn=cmd_disarm)

    args = ap.parse_args()
    if not getattr(args, "fn", None):
        ap.print_help()
        return 2
    return args.fn(args)


if __name__ == "__main__":
    sys.exit(main())
