#!/usr/bin/env python3
"""Build the plain SOURCE zip that ships alongside a NinjaScript import archive.

Two assets go out per release and they are not interchangeable:

  * ``make_ninjascript_archive.py`` -> the IMPORTABLE archive. ``Info.xml`` at the
    root, backslash-pathed entries, **source only** -- the importer has nowhere to
    put a README.
  * this script -> the READABLE zip. Foldered like the repo, docs included, LICENCE
    and NOTICE at the root, for someone who wants to read before installing.

WHY IT EXISTS
  The deck preview's source zip was hand-rolled once and then never rebuilt. When
  the published runtime went v1.36.0 -> v1.45.0, BOTH deck assets silently kept
  shipping the old core, because a bundle zip EMBEDS the runtime. The importable
  one had a builder and was regenerated in seconds; this one did not, so it rotted.

  A release asset with no builder is a release asset that will go stale.

USAGE
    python tools/make_source_zip.py deck runtime \\
        --name sentinel-deck-preview -o dist/sentinel-deck-preview-v0.2.5.zip
"""
from __future__ import annotations

import argparse
import sys
import zipfile
from pathlib import Path

import _console                                                     # noqa: E402
_console.unbreakable_output()   # a guard that cannot PRINT its failure has no failure

REPO = Path(__file__).resolve().parent.parent
SRC = REPO / "src"

# What a reader wants: the code and the words about it. Screenshots are on the
# website; they only bloat a source download.
INCLUDE_SUFFIXES = {".cs", ".md", ".xaml"}

# Shipped at the root of the zip. MPL is a per-FILE licence, but a copy at the
# root is what makes the archive self-describing.
ROOT_FILES = ("LICENSE", "NOTICE")


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("bundles", nargs="+", help="bundle folder names under src/")
    ap.add_argument("--name", required=True, help="root folder name inside the zip")
    ap.add_argument("-o", "--out", required=True, help="output .zip path")
    args = ap.parse_args()

    entries: list[tuple[Path, str]] = []

    for rf in ROOT_FILES:
        p = REPO / rf
        if not p.is_file():
            sys.exit(f"error: {rf} not found at repo root -- a source zip must be self-describing")
        entries.append((p, f"{args.name}/{rf}"))

    for bundle in args.bundles:
        root = SRC / bundle
        if not root.is_dir():
            sys.exit(f"error: no such bundle: src/{bundle}")
        found = 0
        for path in sorted(root.rglob("*")):
            if not path.is_file() or path.suffix.lower() not in INCLUDE_SUFFIXES:
                continue
            rel = path.relative_to(root).as_posix()
            entries.append((path, f"{args.name}/{bundle}/{rel}"))
            found += 1
        if not found:
            sys.exit(f"error: bundle src/{bundle} contributed no files -- refusing to ship an empty folder")

    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
        for path, arc in entries:
            z.write(path, arc)

    print(f"{out}  ({out.stat().st_size:,} bytes, {len(entries)} entries)")
    for _p, arc in entries:
        print(f"  {arc}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
