#!/usr/bin/env python
r"""version_check - does a .cs file's HEADER version agree with the version its CODE declares?

    python Lab\docs\version_check.py                  # report
    python Lab\docs\version_check.py --quiet          # only mismatches
  importable:  from version_check import scan_versions

WHY THIS EXISTS
    A suite file states its version in up to three places, and they drift independently:

      1. the FILENAME  (`SentinelExcursionRecorder_v2_0_0.cs`)  - a FROZEN identity
      2. the HEADER banner (`//  Version v2.5.0`)               - what a HUMAN reads
      3. a CODE const (`RecVer = "2.5.0"`)                      - what the MACHINE stamps on data

    On 2026-08-08 the recorder's banner read v2.3.0 while `RecVer`, the value stamped on every
    corpus row, already read 2.5.0 - two changelog entries had been written without bumping the
    banner. `memory\NOW.md` had then inherited the wrong number from the banner and reported the
    recorder as v2.3.0 for over a week. Nothing compared the two.

⛔ THIS DELIBERATELY DOES **NOT** COMPARE THE FILENAME.
    The versioning policy FREEZES the filename at the fork point and lets the header move:
    `SentinelBinds_v0_1_0.cs` is legitimately at 0.3.2, Bridge_v0_2_0 at 0.3.1, Cockpit_v0_1_0 at
    0.5.0. Comparing against the filename would flag ~8 files for a difference the design
    introduces on purpose - the single most repeated failure in this project's history, and the
    fastest way to teach a reader to ignore a check. Header-vs-const is the pair that must agree,
    because one is the claim and the other is the behaviour.

⚠ THE HEADER IS NOT AT THE TOP OF THE FILE. NinjaScript opens with `#region Using declarations`,
    so the banner sits AFTER the `#endregion`. An `\A`-anchored regex mis-reads the whole tree -
    that exact mistake made audit.py report 18 fully-documented smoothers as undocumented. Scan a
    window, not the first line.

SEVERITY
    MISMATCH (ERROR)  both exist and disagree - one of them is lying to somebody
    HEADER_ONLY(INFO) no const to cross-check; nothing to do, recorded for coverage
    CONST_ONLY (INFO) no banner claim; the const is authoritative by default
"""
from __future__ import annotations

import io
import os
import re
import sys

# ⚠ BUS FACTOR, not cosmetics. As a bare constant this tool runs on exactly one machine, and this
# repo has already learned that lesson once: muster.py hardcoded a fleet and the note recorded at the
# time was "the better reason was never security — as written, nobody else could run these tools at
# all." A published tool pinned to one operator's home directory is a tool a contributor cannot run.
# Resolution order: --custom > $SENTINEL_CUSTOM > walk up from this file > the historical default.
def _default_custom() -> str:
    env = os.environ.get("SENTINEL_CUSTOM")
    if env and os.path.isdir(env):
        return env
    here = os.path.dirname(os.path.abspath(__file__))          # …\Sentinel\Lab\docs
    guess = os.path.abspath(os.path.join(here, "..", "..", "..", "bin", "Custom"))
    if os.path.isdir(guess):
        return guess
    return r"C:\Users\Administrator\Documents\NinjaTrader 8\bin\Custom"


CUSTOM = _default_custom()
SKIP_DIRS = {"_archive", "obj", "bin", "__pycache__", ".git", ".claude", "_copier_samples", "_verify"}

# `const string <anything>Ver|Version = "1.2.3"` - the suite uses Version, RecVer, CouncilVer,
# KeelVersion, TapeVer, DumpVer. Matching the SUFFIX rather than a fixed list is what stops a new
# tool's const from being silently uncovered.
CONST_RE = re.compile(r'const\s+string\s+(\w*(?:Ver|Version))\s*=\s*"(\d+\.\d+\.\d+)"')
# A comment line that makes an explicit version CLAIM. Requires the word "version" so that a
# changelog line like "// v1.2.0 - did a thing" is not mistaken for the file's current version.
HEADER_RE = re.compile(r'^[ \t]*//.*?\b[Vv]ersion\b\s*:?\s*v?(\d+\.\d+\.\d+)', re.M)
HEADER_WINDOW = 6000       # generous: past the using-region and the banner, short of the changelog


def _read(path: str) -> str:
    try:
        return io.open(path, encoding="utf-8", errors="replace").read()
    except OSError:
        return ""


def scan_versions(root: str = CUSTOM):
    """-> list of dicts: {file, rel, header, consts, status}. Importable; no printing."""
    out = []
    for dirpath, dirs, names in os.walk(root):
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
        for n in sorted(names):
            if not n.endswith(".cs"):
                continue
            path = os.path.join(dirpath, n)
            text = _read(path)
            if not text:
                continue
            consts = CONST_RE.findall(text)
            m = HEADER_RE.search(text[:HEADER_WINDOW])
            header = m.group(1) if m else None
            if not consts and not header:
                continue
            if consts and header:
                status = "OK" if any(v == header for _, v in consts) else "MISMATCH"
            elif header:
                status = "HEADER_ONLY"
            else:
                status = "CONST_ONLY"
            out.append({
                "file": n,
                "rel": os.path.relpath(path, root),
                "header": header,
                "consts": consts,
                "status": status,
            })
    return out


def main(argv) -> int:
    quiet = "--quiet" in argv
    root = CUSTOM
    if "--custom" in argv:
        root = argv[argv.index("--custom") + 1]
    if not os.path.isdir(root):
        print("version_check: no bin\\Custom at %s\n"
              "  Set SENTINEL_CUSTOM or pass --custom <path>." % root)
        return 2
    rows = scan_versions(root)
    bad = [r for r in rows if r["status"] == "MISMATCH"]

    if not quiet:
        counts = {}
        for r in rows:
            counts[r["status"]] = counts.get(r["status"], 0) + 1
        print("version_check: %d files declare a version  (%s)"
              % (len(rows), " · ".join(f"{k} {v}" for k, v in sorted(counts.items()))))

    for r in bad:
        cs = ", ".join(f"{k}={v}" for k, v in r["consts"])
        print("  MISMATCH  %s" % r["rel"])
        print("            header says v%s · code says %s" % (r["header"], cs))
        print("            The const is what gets STAMPED ON DATA; the header is what a human reads.")

    if bad:
        print("\n%d file(s) state two different versions of themselves." % len(bad))
        return 1
    if not quiet:
        print("  no file states two different versions of itself")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
