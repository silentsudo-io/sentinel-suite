#!/usr/bin/env python3
"""Build a real NinjaScript import archive (Tools > Import > NinjaScript Add-On...).

A plain source zip is NOT importable: NinjaTrader rejects it with

    "Selected file was made from an older, incompatible version of
     NinjaTrader or is not a NinjaScript archive."

A NinjaScript archive is just a zip with two rules:

  1. ``Info.xml`` at the root, carrying the exporting NinjaTrader version.
  2. Every other entry pathed relative to ``bin\\Custom`` using BACKSLASHES
     (e.g. ``Indicators\\SentinelDeck_v0_2_5.cs``).

Only NinjaScript source belongs inside — docs and images are shipped in the
plain source zip instead, since the importer has nowhere to put them.

EVERY .cs UNDER A BUNDLE MUST REACH THE ARCHIVE. NinjaTrader compiles all of
bin\\Custom into ONE assembly, so a source file that is silently left out is not
a missing feature — it is a CS0246 that takes the user's whole tree down. This
script therefore has no "skip" path for .cs: a file either maps onto a real
NinjaScript folder or the build FAILS. (It shipped `sensors/Shared/
TbarsSudoV3Config.cs` — a compile-time dependency of SentinelTBars — into the
void for exactly as long as that skip was a `print` instead of an error.)

Usage:
    python tools/make_ninjascript_archive.py deck runtime -o dist/sentinel-deck-v0.2.5.zip
"""

from __future__ import annotations

import argparse
import sys
import zipfile
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
SRC = REPO / "src"

# Stamped into Info.xml. Deliberately a low 8.0.x rather than the version we
# happen to build on: NinjaTrader accepts an archive from an older 8.x but
# balks at one from a version newer than the importer's, and testers run
# whatever they run. Known-good against 8.1.7.2.
EXPORT_VERSION = "8.0.28.0"

INFO_XML = (
    '<?xml version="1.0" encoding="utf-8"?>\r\n'
    "<NinjaTrader>\r\n"
    "  <Export>\r\n"
    f"    <Version>{EXPORT_VERSION}</Version>\r\n"
    "  </Export>\r\n"
    "</NinjaTrader>"
)

# The real bin\Custom NinjaScript folders — an archive entry under any of these
# lands where NinjaTrader expects it.
NT_FOLDERS = {
    "AddOns",
    "BarsTypes",
    "ChartStyles",
    "DrawingTools",
    "Indicators",
    "MarketAnalyzerColumns",
    "ShareServices",
    "Strategies",
    "SuperDomColumns",
}

# Bundle folders that hold real compilable source but are NOT NinjaScript folders,
# and where their contents must be REDIRECTED to instead.
#
# `Shared/` is our own convention for suite-shared plain-C# types (currently just
# TbarsSudoV3Config, a compile-time dependency of SentinelTBars). NinjaTrader's own
# exporter only ever emits the folders above, so the importer's behaviour on a
# non-standard path is unverified — and a REJECTED import is worse than a misfiled
# one. These types are namespace-scoped, not folder-scoped, so AddOns\ compiles
# identically.
#
# ⚠ A user who previously hand-copied the file to bin\Custom\Shared\ and then
# imports will hold TWO copies and get CS0101. Delete the hand-placed one.
FOLDER_MAP = {"Shared": "AddOns"}


def collect(bundles: list[str]) -> list[tuple[Path, str]]:
    """Map src/<bundle>/<folder>/<file>.cs -> <NTFolder>\\<file>.cs.

    Every .cs is packaged or the build fails — see the module docstring.
    """
    entries: list[tuple[Path, str]] = []
    seen: dict[str, Path] = {}
    remapped: list[str] = []

    for bundle in bundles:
        root = SRC / bundle
        if not root.is_dir():
            sys.exit(f"error: no such bundle: src/{bundle}")

        for path in sorted(root.rglob("*.cs")):
            rel = path.relative_to(root)
            top = rel.parts[0]

            if top in NT_FOLDERS:
                dest = rel.parts
            elif top in FOLDER_MAP:
                dest = (FOLDER_MAP[top],) + rel.parts[1:]
                remapped.append(f"{bundle}/{rel.as_posix()} -> {chr(92).join(dest)}")
            else:
                sys.exit(
                    f"error: {bundle}/{rel.as_posix()} is source, but '{top}/' is neither a\n"
                    f"       NinjaScript folder nor in FOLDER_MAP. Leaving a .cs out of the\n"
                    f"       archive breaks the importer's whole compile (CS0246). Either move\n"
                    f"       it under a real folder or add a FOLDER_MAP entry."
                )

            # Flatten any nested subfolders — bin\Custom's folders are one level deep.
            arc = "\\".join((dest[0], dest[-1]))
            if arc in seen:
                sys.exit(f"error: {arc} supplied twice: {seen[arc]} and {path}")
            seen[arc] = path
            entries.append((path, arc))

    if not entries:
        sys.exit("error: nothing to package")
    if remapped:
        print("  remapped (non-NinjaScript source folder):")
        for r in remapped:
            print(f"    {r}")
    return entries


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("bundles", nargs="+", help="bundle names under src/ (e.g. deck runtime)")
    ap.add_argument("-o", "--out", required=True, help="output .zip path")
    args = ap.parse_args()

    entries = collect(args.bundles)

    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)

    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
        z.writestr("Info.xml", INFO_XML)
        for path, arc in entries:
            # ZipInfo's constructor rewrites os.sep to "/", so set the stored
            # name afterwards -- real NinjaTrader exports use backslashes.
            zi = zipfile.ZipInfo(arc.replace("\\", "/"))
            zi.filename = arc
            zi.compress_type = zipfile.ZIP_DEFLATED
            zi.external_attr = 0o600 << 16
            z.writestr(zi, path.read_bytes())

    print(f"\n{out}  ({out.stat().st_size:,} bytes, NT {EXPORT_VERSION})")
    for _, arc in entries:
        print(f"  {arc}")


if __name__ == "__main__":
    main()
