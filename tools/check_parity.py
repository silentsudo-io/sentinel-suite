#!/usr/bin/env python3
"""
check_parity.py — is the published snapshot still faithful to the source of truth?

THE MODEL
  `bin\\Custom` (the private NinjaTrader tree) is CANONICAL. It is what NinjaTrader
  compiles and what F5 verifies. This repo's `src/` is a PUBLISHED SNAPSHOT of the
  subset we have chosen to release. Drift is therefore always "local moved ahead",
  never a merge -- so this tool reports, it never writes.

  It deliberately does NOT sync. `bin\\Custom` also holds the unreleased rungs
  (Council, Bridge, GTrader21, Copier, the Lab). What ships is an editorial decision
  gated by the product ladder, and one bad glob in a sync script would publish the
  lot. Copy the file yourself, once you have decided it should ship.

WHY A NAIVE DIFF IS USELESS HERE
  52 of 54 published files "differ" from local, and almost all of that is two
  expected publish-time transforms:

    1. MPL header   -- published files carry a 7-line MPL-2.0 header; the private
                       tree does not. MPL is a per-FILE licence, so this is added
                       on the way out and its ABSENCE upstream is correct.
    2. Generated region -- NinjaTrader appends `#region NinjaScript generated code`
                       to every Indicator it compiles. It is machine-written and
                       per-installation, so it must be stripped on the way out.

  Both are normalised away here, so what is left is REAL divergence: the short list
  worth looking at before cutting a release.

ALSO CHECKED
  * A published file that still carries a generated region -- that is a defect, not
    drift: it produces CS0111/CS0102 for whoever imports it next. Reported as ERROR.
  * A published file with no MPL header -- MPL is per-file, so this is a licence gap.
  * A published file with no counterpart in the local tree -- it has been renamed,
    archived or deleted upstream and the snapshot is now orphaned.

USAGE
    python tools/check_parity.py --local "C:/Users/<you>/Documents/NinjaTrader 8/bin/Custom"
    python tools/check_parity.py --local "<...>" --diff SentinelSkin.cs   # show one file

EXIT CODES
    0 = snapshot faithful (no drift, no errors)
    1 = drift and/or errors found
    2 = bad invocation
"""
from __future__ import annotations

import argparse
import difflib
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
SRC = REPO / "src"

# Anchored at line start ON PURPOSE. A bare substring search also matches a COMMENT
# that merely mentions the region, and cutting from there deletes real code -- that
# bug has bitten this project twice, once across ~700 files.
REGION_RE = re.compile(r"^[ \t]*#region NinjaScript generated code", re.M)

MPL_MARK = "Mozilla Public License"


def strip_region(text: str) -> str:
    """Drop the NinjaTrader generated region (first anchored match -> EOF)."""
    m = REGION_RE.search(text)
    return text[: m.start()] if m else text


# A rule line closing the MPL block: `// ────…` or `// ----…` (3+ of either).
RULE_RE = re.compile(r"^\s*//\s*[─\-=_]{3,}\s*$")


def strip_mpl_header(text: str) -> str:
    """Drop ONLY the leading MPL-2.0 comment block, if present.

    ⚠ Must strip the MPL block and NOTHING ELSE. Every Sentinel file follows its
    licence header immediately with its own doc header, as one unbroken run of `//`
    lines. An earlier version walked that whole run, so on a published file (which
    has the MPL) it also removed 14 lines of the tool's real header, while the local
    file (no MPL, so no strip) kept them -- manufacturing drift that did not exist.
    Caught because adding a header to two files moved the faithful count the wrong
    way. Bound the cut to the licence block itself.
    """
    lines = text.split("\n")
    scan = lines[:12]
    hit = next((i for i, l in enumerate(scan) if MPL_MARK in l), None)
    if hit is None:
        return text
    # Cut through the rule line that closes the block; else through the copyright line.
    end = next((i for i in range(hit + 1, len(scan)) if RULE_RE.match(scan[i])), None)
    if end is None:
        end = next((i for i in range(hit + 1, len(scan))
                    if "Copyright" in scan[i]), hit)
    return "\n".join(lines[end + 1:])


def normalise(text: str) -> list[str]:
    text = text.replace("\r\n", "\n").replace("\r", "\n")
    text = strip_mpl_header(strip_region(text))
    return [ln.rstrip() for ln in text.rstrip().split("\n")]


def find_local(local_root: Path, published: Path) -> Path | None:
    """src/<bundle>/<Folder>/<f>.cs -> <local>/<Folder>/<f>.cs, else search the tree."""
    folder, name = published.parent.name, published.name
    direct = local_root / folder / name
    if direct.is_file():
        return direct
    for p in local_root.rglob(name):
        if "_archive" not in p.parts and "_verify" not in p.parts:
            return p
    return None


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--local", required=True, help='path to the canonical bin\\Custom tree')
    ap.add_argument("--diff", metavar="FILE", help="print the normalised diff for one file name")
    args = ap.parse_args()

    local_root = Path(args.local)
    if not local_root.is_dir():
        print(f"error: --local is not a directory: {local_root}", file=sys.stderr)
        return 2

    drifted, errors, orphaned, clean = [], [], [], 0

    for pub in sorted(SRC.rglob("*.cs")):
        rel = pub.relative_to(REPO)
        raw = pub.read_text(encoding="utf-8", errors="replace")

        if REGION_RE.search(raw):
            errors.append((rel, "ships a generated region (CS0111 on import)"))
        if MPL_MARK not in raw[:2000]:
            errors.append((rel, "no MPL-2.0 header (MPL is a per-file licence)"))

        loc = find_local(local_root, pub)
        if loc is None:
            orphaned.append(rel)
            continue

        a = normalise(raw)
        b = normalise(loc.read_text(encoding="utf-8", errors="replace"))

        if args.diff and pub.name == args.diff:
            print("\n".join(difflib.unified_diff(
                a, b, fromfile=f"published/{pub.name}", tofile=f"local/{pub.name}", lineterm="")))
            return 0

        if a == b:
            clean += 1
        else:
            n = sum(1 for d in difflib.ndiff(a, b) if d[0] in "+-")
            drifted.append((rel, n, loc))

    if args.diff:
        print(f"error: no published file named {args.diff}", file=sys.stderr)
        return 2

    print(f"Canonical: {local_root}")
    print(f"Snapshot:  {SRC}\n")

    if errors:
        print(f"ERRORS ({len(errors)}) — defects in the snapshot itself:")
        for rel, why in errors:
            print(f"    {rel}  —  {why}")
        print()

    if orphaned:
        print(f"ORPHANED ({len(orphaned)}) — published, but no longer in the local tree:")
        for rel in orphaned:
            print(f"    {rel}")
        print("    (renamed, archived or deleted upstream — the snapshot is stale)\n")

    if drifted:
        print(f"DRIFT ({len(drifted)}) — local has moved ahead; decide per file whether it ships:")
        for rel, n, loc in sorted(drifted, key=lambda r: -r[1]):
            print(f"    {n:>6} lines   {rel}")
        print("\n    Inspect one with:  python tools/check_parity.py --local <...> --diff <File.cs>")
        print("    To publish: copy the local file over the snapshot, ADD the MPL header,")
        print("    and STRIP the generated region. Then re-run this.\n")

    print(f"{clean} of {clean + len(drifted) + len(orphaned)} published files are faithful.")
    return 1 if (drifted or errors or orphaned) else 0


if __name__ == "__main__":
    raise SystemExit(main())
