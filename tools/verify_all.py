#!/usr/bin/env python3
r"""verify_all - run every publish/doc guard in this repo, and say plainly what failed.

    python tools/verify_all.py --local "C:\Users\<you>\Documents\NinjaTrader 8\bin\Custom"

WHY ONE COMMAND AND NOT FIVE
    Each guard here was built because a specific defect reached the public repo. Individually they
    work. Collectively they had a problem: nothing named them in one place, so knowing to run them
    required already knowing they existed — which is the same failure the guards themselves exist
    to remove. A checklist a newcomer cannot find is a checklist that does not run.

    So: one entry point, and it is the thing a session is told to run. If you add a guard, add it
    to CHECKS below. If it is not here, assume nobody will ever run it.

WHAT IT DOES NOT DO
    It does not publish, fix, or write anything. Every check is read-only, so running it can never
    be the wrong move.

⚠ IT REPORTS WHAT IT COULD NOT RUN. A guard that fails to start is reported as UNTESTED, never
    silently skipped — "no output" and "nothing wrong" must not look the same. That distinction is
    this project's most expensive recurring lesson: a crashed sensor is indistinguishable from a
    quiet one unless something insists on the difference.
"""
from __future__ import annotations

import argparse
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, ".."))

# (label, argv-builder, what a failure means)
CHECKS = [
    ("doc transforms",
     lambda local: [sys.executable, os.path.join(HERE, "test_doc_transforms.py")],
     "a transform rewrites the inside of an inline-code span (or has gone inert)"),

    ("renderer ownership",
     lambda local: [sys.executable, os.path.join(HERE, "publish_doc.py"),
                    "--local", local, "--check-renderers"],
     "a page was re-rendered by the wrong renderer, silently rewriting its whole body"),

    ("snapshot parity",
     lambda local: [sys.executable, os.path.join(HERE, "check_parity.py"), "--local", local],
     "drift · a STALE REGISTRATION · or a CLOSED DIVERGENCE (something withheld got published)"),

    ("version self-consistency",
     lambda local: [sys.executable,
                    os.path.join(os.path.dirname(local), "..", "Sentinel", "Lab", "docs",
                                 "version_check.py")],
     "a file states two different versions of itself; the const is what stamps the data"),
]


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--local", required=True, help=r"path to bin\Custom")
    ap.add_argument("-v", "--verbose", action="store_true", help="show each check's full output")
    a = ap.parse_args()
    local = os.path.abspath(a.local)

    print(f"verify_all — {len(CHECKS)} guards\n")
    failed, untested = [], []

    for label, argv_of, meaning in CHECKS:
        argv = argv_of(local)
        target = argv[1] if len(argv) > 1 else ""
        if target.endswith(".py") and not os.path.exists(target):
            print(f"  ?  {label:<26} UNTESTED — not found: {target}")
            untested.append(label)
            continue
        try:
            r = subprocess.run(argv, capture_output=True, text=True, encoding="utf-8",
                               errors="replace")
        except Exception as e:                       # noqa: BLE001 - reported, never swallowed
            print(f"  ?  {label:<26} UNTESTED — {type(e).__name__}: {e}")
            untested.append(label)
            continue

        out = (r.stdout or "") + (r.stderr or "")
        if r.returncode == 0:
            print(f"  ok {label:<26} {out.strip().splitlines()[-1][:80] if out.strip() else ''}")
        else:
            print(f"  ✗  {label:<26} FAILED — {meaning}")
            failed.append(label)
        if a.verbose or r.returncode != 0:
            for line in out.rstrip().splitlines():
                print(f"       {line}")

    print()
    if untested:
        print(f"⚠ {len(untested)} guard(s) could NOT RUN: {', '.join(untested)}")
        print("  Untested is not passed. Fix the path before trusting this report.")
    if failed:
        print(f"FAIL — {len(failed)} guard(s): {', '.join(failed)}")
        return 1
    if untested:
        return 2
    print("PASS — every guard green")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
