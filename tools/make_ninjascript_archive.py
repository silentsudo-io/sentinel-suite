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

# Only these map onto a real bin\Custom folder; anything else in a bundle
# (Docs/, Shared/, themes/) is not importable NinjaScript.
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


def collect(bundles: list[str]) -> list[tuple[Path, str]]:
    """Map src/<bundle>/<NTFolder>/<file>.cs -> <NTFolder>\\<file>.cs."""
    entries: list[tuple[Path, str]] = []
    seen: dict[str, Path] = {}

    for bundle in bundles:
        root = SRC / bundle
        if not root.is_dir():
            sys.exit(f"error: no such bundle: src/{bundle}")

        for path in sorted(root.rglob("*.cs")):
            rel = path.relative_to(root)
            if rel.parts[0] not in NT_FOLDERS:
                print(f"  skip (not a NinjaScript folder): {bundle}/{rel.as_posix()}")
                continue

            arc = "\\".join(rel.parts)
            if arc in seen:
                sys.exit(f"error: {arc} supplied by two bundles: {seen[arc]} and {path}")
            seen[arc] = path
            entries.append((path, arc))

    if not entries:
        sys.exit("error: nothing to package")
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
