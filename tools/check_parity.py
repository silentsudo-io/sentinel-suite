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
import json
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
SRC = REPO / "src"

# Files whose divergence from the canonical tree is INTENDED and must survive. These are
# not staleness and closing them would ship a broken build, so they are reported in their
# own section rather than sitting in the drift list forever.
#
# A check that is always red is a check nobody reads -- the same principle that made the
# daily parity task report the DELTA instead of the absolute. Keep this list SHORT, and
# only ever add to it with the reason written out: an unexplained entry here is
# indistinguishable from a file somebody quietly gave up on.
# The line count is PINNED, not just the filename. Exempting a whole file would also
# silence any NEW divergence that lands in it later -- turning a safety note into a blind
# spot. If the count moves, something other than the known exception changed and it is
# reported as drift again.
DELIBERATE = {
    "SentinelCore_v1_0_0.cs": (37,
        "ResolveLane is gutted on purpose: the real body calls LaneAssign.Read(), and "
        "LaneAssign lives in SentinelCore.SystemBuilder.cs — part of the UNRELEASED System "
        "Builder rung. Publishing it would be CS0246 for every user. Returning the caller's "
        "F6 value is EXACTLY what the full version does when there is no Lanes.conf, and "
        "nothing published writes one. Signature preserved so the API does not move."),
}

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


def report_delta(state_path: Path, drifted, errors, orphaned, clean: int) -> int:
    """Report only what CHANGED since the previous run, then record the new state.

    WHY THIS MODE EXISTS. `bin\\Custom` is canonical and always ahead, so the absolute
    answer is permanently "52 files differ". A check that is red every single day is a
    check nobody reads -- the same reason this project refused to ship a linter that
    failed on day one. The signal worth a human's attention is the DELTA: a file that
    started drifting, or one whose drift grew. Everything else is the expected state
    of a healthy snapshot.

    Exit 0 means "nothing new", NOT "no drift".
    """
    now = {str(rel).replace("\\", "/"): n for rel, n, _ in drifted}
    prev, first_run = {}, True
    if state_path.is_file():
        try:
            prev = json.loads(state_path.read_text(encoding="utf-8")).get("drift", {})
            first_run = False
        except Exception:
            pass  # unreadable state -> treat as first run, re-baseline below

    new = sorted(k for k in now if k not in prev)
    grew = sorted((k, prev[k], now[k]) for k in now if k in prev and now[k] > prev[k])
    gone = sorted(k for k in prev if k not in now)

    state_path.write_text(json.dumps(
        {"drift": now, "errors": len(errors), "orphaned": len(orphaned), "clean": clean},
        indent=2), encoding="utf-8")

    if first_run:
        print(f"PARITY baseline recorded: {len(now)} drifting, {len(errors)} errors, {clean} faithful.")
        print("No delta to report on a first run. Subsequent runs report only what changed.")
        return 0

    if errors:
        print(f"PARITY ERRORS ({len(errors)}) — defects in the published snapshot:")
        for rel, why in errors:
            print(f"    {rel}  —  {why}")

    if new:
        print(f"\nNEWLY DRIFTING ({len(new)}) — local changed and the snapshot did not:")
        for k in new:
            print(f"    {now[k]:>6} lines   {k}")
    if grew:
        print(f"\nDRIFT GREW ({len(grew)}):")
        for k, o, n in grew:
            print(f"    {o:>6} -> {n:<6} {k}")
    if gone:
        print(f"\nRESOLVED ({len(gone)}) — published caught up, or the file left the tree:")
        for k in gone:
            print(f"    {k}")

    if not (new or grew or errors):
        print(f"PARITY unchanged — {len(now)} files drifting (expected), {clean} faithful, 0 errors.")
        return 0

    print("\n    Review:  python tools/check_parity.py --local <bin\\Custom> --diff <File.cs>")
    print("    Publish: copy local over the snapshot, ADD the MPL header, STRIP the region.")
    return 1


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--local", required=True, help='path to the canonical bin\\Custom tree')
    ap.add_argument("--diff", metavar="FILE", help="print the normalised diff for one file name")
    ap.add_argument("--since-last", action="store_true",
                    help="report only what CHANGED since the previous run (see WHY below), "
                         "then record the new state. This is what the daily task uses.")
    ap.add_argument("--state", default=str(REPO / ".parity-state.json"),
                    help="where --since-last keeps its snapshot")
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

    if args.since_last:
        return report_delta(Path(args.state), drifted, errors, orphaned, clean)

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

    # Only exempt a known file at its known SIZE. A changed count means new divergence
    # landed on top of the documented one, so it goes back into the drift list.
    def is_deliberate(d):
        exp = DELIBERATE.get(Path(d[0]).name)
        return exp is not None and exp[0] == d[1]

    deliberate = [d for d in drifted if is_deliberate(d)]
    drifted = [d for d in drifted if not is_deliberate(d)]

    if drifted:
        print(f"DRIFT ({len(drifted)}) — local has moved ahead; decide per file whether it ships:")
        for rel, n, loc in sorted(drifted, key=lambda r: -r[1]):
            print(f"    {n:>6} lines   {rel}")
        print("\n    Inspect one with:  python tools/check_parity.py --local <...> --diff <File.cs>")
        print("    To publish: copy the local file over the snapshot, ADD the MPL header,")
        print("    and STRIP the generated region. Then re-run this.\n")

    if deliberate:
        print(f"DELIBERATE ({len(deliberate)}) — publish-time differences that must NOT be closed:")
        for rel, n, loc in sorted(deliberate, key=lambda r: -r[1]):
            print(f"    {n:>6} lines   {rel}")
            print(f"             {DELIBERATE[Path(rel).name][1]}")
        print()

    print(f"{clean} of {clean + len(drifted) + len(deliberate) + len(orphaned)} "
          f"published files are faithful"
          + (f", {len(deliberate)} deliberately divergent." if deliberate else "."))
    return 1 if (drifted or errors or orphaned) else 0


if __name__ == "__main__":
    raise SystemExit(main())
