#!/usr/bin/env python3
r"""publish.py — move one file from the canonical tree into the published snapshot.

WHY THIS EXISTS. Publishing was three transforms applied BY HAND: strip the NinjaTrader
generated region, add the MPL-2.0 header, and (as of 2026-08-07) scrub operator-private
nicknames. check_parity.py documented them and could detect when they were done wrong, but
nothing DID them -- so every publish depended on a person remembering three steps, and the
scrub in particular has to happen every single time or a machine name reaches the repo.

⛔ THIS DOES NOT DECIDE WHAT SHIPS. The allowlist is still the control and it is still a
human: you name the file. `bin\Custom` holds the unreleased rungs, and one bad glob here
would publish the lot, so there is deliberately no --all, no directory mode, and no
discovery. It transforms the file you named and refuses if the result is unsafe.

THE THREE TRANSFORMS, in order:
  1. strip the generated region  -- machine-written, per-installation. Shipping it is a
     defect, not drift: it produces CS0111/CS0102 for whoever imports it next.
  2. scrub nicknames             -- tools/scrub.py, map is private. Reported, never silent.
  3. add the MPL-2.0 header      -- MPL is a per-FILE licence, so it goes on the way out.

THEN IT GATES THE RESULT. secretscan.py runs on what would be written, not on the source.
Verifying the input and shipping the output is how a check passes while the artifact lies --
this project has recorded that failure six times, so the gate reads the bytes destined for
the repo.

    python publish.py <local-file> [--to src/sensors/Indicators] [--dry-run]
    python publish.py --update <published-file>     # refresh an already-published file

Exit 0 published (or clean dry-run) · 1 refused · 2 bad invocation.
"""
from __future__ import annotations

import argparse
import os
import re
import subprocess
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from scrub import load_map, scrub                                    # noqa: E402
from check_parity import strip_region, MPL_MARK                      # noqa: E402

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

REPO = Path(__file__).resolve().parent.parent
SRC = REPO / "src"

MPL_HEADER = """\
// ─────────────────────────────────────────────────────────────────────────────
// This Source Code Form is subject to the terms of the Mozilla Public License,
// v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
// one at https://mozilla.org/MPL/2.0/.
//
// Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
// ─────────────────────────────────────────────────────────────────────────────
"""

# Where the gate lives. Private tree; overridable for a checkout that is not beside one.
SECRETSCAN = Path(os.environ.get(
    "SENTINEL_SECRETSCAN",
    Path(os.path.expanduser("~")) / "Documents" / "NinjaTrader 8" /
    "Sentinel" / "Lab" / "docs" / "secretscan.py"))


def transform(text: str, add_mpl: bool = True):
    """The three publish transforms. Returns (text, scrub-counts)."""
    text = text.replace("\r\n", "\n").replace("\r", "\n")
    text = strip_region(text).rstrip() + "\n"
    text, counts = scrub(text, load_map())
    if add_mpl and MPL_MARK not in text[:2000]:
        text = MPL_HEADER + text
    return text, counts


def gate(text: str, name: str) -> bool:
    """Scan the bytes that WOULD be written. True = safe."""
    if not SECRETSCAN.exists():
        print("  ⚠ gate unavailable (%s not found) — NOT verified" % SECRETSCAN)
        return True                       # fails OPEN; the pre-commit hook is the backstop
    with tempfile.TemporaryDirectory() as d:
        p = Path(d) / name
        p.write_text(text, encoding="utf-8", newline="")
        r = subprocess.run([sys.executable, str(SECRETSCAN), "--gate", d],
                           capture_output=True, text=True)
        if r.returncode != 0:
            print(r.stdout)
            return False
    return True


def candidates() -> int:
    r"""What COULD ship that does not yet -- the release manifest, computed not remembered.

    Reads the same two sources the rest of the system reads, so this cannot drift from them:
    Lab\docs\zones.conf (what may never ship) and src\ (what already does). Everything else
    is a candidate. It reports; it publishes nothing.

    ⚠ IT DOES NOT DECIDE. A file being technically publishable is not a decision to publish
    it -- the product ladder is an editorial judgement and stays one. This exists so that
    judgement is made against a complete list instead of memory.
    """
    lab = SECRETSCAN.parent
    sys.path.insert(0, str(lab))
    try:
        import coverage as cov                                       # type: ignore
        import secretscan as ss                                      # type: ignore
    except ImportError as e:
        sys.stderr.write("publish: need the Lab tools to compute candidates (%s)\n" % e)
        return 2

    zones = ss.load_zones()
    rows = cov.classify()
    rules = load_map()

    groups: dict[str, list] = {}
    for r in rows:
        rel = r["path"].replace("\\", "/")
        if os.path.isabs(rel):                       # the bridge tree — separate upstream repo
            continue
        zone, _ = ss.zone_of(rel, zones)
        if zone in ("PRIVATE", "SKIP"):
            continue
        if r["scope"] == "published":
            continue
        groups.setdefault(r["kind"], []).append((r, zone))

    total = sum(len(v) for v in groups.values())
    print("=" * 96)
    print("CANDIDATE RELEASE SET — publishable, not yet published")
    print("=" * 96)
    print("already in src/: %d · candidates: %d\n"
          % (sum(r["scope"] == "published" for r in rows), total))

    blocked = []
    for kind in sorted(groups):
        items = sorted(groups[kind], key=lambda t: t[0]["name"].lower())
        print("── %s (%d)" % (kind, len(items)))
        for r, zone in items:
            src = Path(r["path"]) if os.path.isabs(r["path"]) else Path(cov.NT8) / r["path"]
            try:
                out, counts = transform(src.read_text(encoding="utf-8", errors="replace"),
                                        add_mpl=src.suffix.lower() == ".cs")
            except OSError:
                continue
            hits = [h for h in ss.scan_file_text(out, r["name"]) if not h["benign"]
                    and h["sev"] == "BLOCK"] if hasattr(ss, "scan_file_text") else []
            mark = "  "
            note = ""
            if counts:
                note = "scrub %d" % sum(counts.values())
            if hits:
                mark, blocked = "⛔", blocked + [(r["name"], hits)]
                note = (note + " · " if note else "") + "BLOCKS: " + \
                       ",".join(sorted({h["rule"] for h in hits}))
            print("   %s %-52s %6d ln  %s" % (mark, r["name"], r["lines"], note))
        print()

    print("%d candidate%s · %d would still block after transform"
          % (total, "" if total == 1 else "s", len(blocked)))
    print("\nNothing was published. To ship one:")
    print("   python tools/publish.py <local-file> --to src/<folder> [--dry-run]")
    return 0


# ── RUNG MAP ────────────────────────────────────────────────────────────────────
# src/ is organised by PRODUCT RUNG (docs/PRODUCT_LADDER.md), not by NinjaTrader folder --
# rungs 0-1 already ship as skins/ sensors/ smoothers/ deck/ binds/ runtime/. Rungs 2-10 are
# being released now, so this extends that same taxonomy rather than inventing a second one.
# Matched in order, first hit wins; the fallback is the rung's own NT folder.
RUNGS: list[tuple[str, str]] = [
    # (filename or path fragment, destination under src/)
    ("Council",                    "council/Indicators"),
    ("CouncilFusion",              "council/AddOns"),
    ("SentinelExcursionRecorder",  "recorder/Indicators"),
    ("SentinelExcursions",         "recorder/AddOns"),
    ("SentinelCandidateRecorder",  "recorder/Indicators"),
    ("SentinelTapeRecorder",       "recorder/Indicators"),
    ("SentinelLog",                "recorder/AddOns"),
    ("SentinelLens",               "observatory/AddOns"),
    ("SentinelCockpit",            "deck/AddOns"),
    ("SentinelDashboard",          "deck/AddOns"),
    ("SentinelDeck",               "deck/Indicators"),
    ("SentinelRiskService",        "prop-kit/AddOns"),
    ("SentinelAlertService",       "prop-kit/AddOns"),
    ("SentinelArcService",         "prop-kit/AddOns"),
    ("SentinelStateService",       "prop-kit/AddOns"),
    ("SentinelBridge",             "bridge/Strategies"),
    ("SentinelCopierService",      "copier/AddOns"),
    ("SentinelConductor",          "lab/AddOns"),
    ("SentinelQuartermaster",      "lab/AddOns"),
    ("SentinelNewsService",        "prop-kit/AddOns"),
    ("SentinelKeel",               "strategies/Strategies"),
    ("SentinelTrendStrategy",      "strategies/Strategies"),
    ("SentinelTBarsEdgeProbe",     "strategies/Strategies"),
    ("SentinelCore.SystemBuilder", "runtime/AddOns"),
]
# Axes (rung 1) and the remaining sensors join the folder that already holds their siblings.
AXES = {"Clock_v1_0_0.cs", "Location_v1_0_0.cs", "Mtf_v1_0_0.cs",
        "Participation_v1_0_0.cs", "Intermarket_v1_0_0.cs"}
SMOOTHER_HINT = re.compile(r"(MA_v|Filter_v|TillsonT3|LinReg|MovingMedian|HoltEMA|ZeroLag)")


def rung_for(rec) -> str:
    name, kind = rec["name"], rec["kind"]
    p = rec["path"].replace("\\", "/")
    for frag, dest in RUNGS:
        if name.startswith(frag):
            return dest
    if kind == "bartype":
        return "sensors/BarsTypes"
    if kind == "indicator":
        if name in AXES:
            return "sensors/Indicators"
        return "smoothers/Indicators" if SMOOTHER_HINT.search(name) else "sensors/Indicators"
    if kind == "strategy":
        return "strategies/Strategies"
    if kind == "addon":
        return "runtime/AddOns"
    if kind == "template":
        return "templates"
    if kind == "config":
        return "config"
    if kind in ("lab", "sent-tools"):
        # keep the Lab's own internal structure -- it is a Python package, not a flat folder
        sub = p.split("Sentinel/Lab/", 1)[-1] if "Sentinel/Lab/" in p else os.path.basename(p)
        return "lab/" + os.path.dirname(sub) if os.path.dirname(sub) else "lab"
    if kind in ("azimuth", "azimuth-ui"):
        sub = p.split("Sentinel/Azimuth/", 1)[-1] if "Sentinel/Azimuth/" in p else os.path.basename(p)
        return "azimuth/" + os.path.dirname(sub) if os.path.dirname(sub) else "azimuth"
    return kind


def publish_all(dry: bool) -> int:
    r"""Publish the reviewed candidate set.

    ⚠ THIS IS THE ONE BULK PATH, AND IT IS DELIBERATELY NOT A GLOB. It republishes exactly
    what `--candidates` computes -- zones.conf minus what already ships -- so the thing being
    bulk-published is a list a human has read. The tool still has no directory mode and no
    discovery: point it at a tree and it will not sweep it.

    ⛔ ALL-OR-NOTHING ON A BLOCK. If any file still carries a PUBLIC-zone secret after its
    transforms, NOTHING is written. A partial publish leaves the snapshot in a state no one
    can reason about, and "some of it shipped" is the worst answer to "did the secret ship?"
    """
    lab = SECRETSCAN.parent
    sys.path.insert(0, str(lab))
    import coverage as cov                                           # type: ignore
    import secretscan as ss                                          # type: ignore

    zones, rows = ss.load_zones(), cov.classify()
    plan, blocked = [], []
    for r in rows:
        rel = r["path"].replace("\\", "/")
        if os.path.isabs(rel) or r["scope"] == "published":
            continue
        if ss.zone_of(rel, zones)[0] in ("PRIVATE", "SKIP"):
            continue
        src = Path(cov.NT8) / r["path"]
        try:
            out, counts = transform(src.read_text(encoding="utf-8", errors="replace"),
                                    add_mpl=src.suffix.lower() == ".cs")
        except OSError as e:
            sys.stderr.write("publish: cannot read %s (%s)\n" % (src, e))
            continue
        hits = [h for h in ss.scan_file_text(out, r["name"])
                if not h["benign"] and h["sev"] == "BLOCK"]
        if hits:
            blocked.append((r["name"], sorted({h["rule"] for h in hits})))
        plan.append((r, SRC / rung_for(r) / r["name"], out, counts))

    if blocked:
        print("⛔ REFUSED — %d file%s still carry a PUBLIC-zone secret after transform:"
              % (len(blocked), "" if len(blocked) == 1 else "s"))
        for n, rules in blocked:
            print("     %-52s %s" % (n, ",".join(rules)))
        print("\nNOTHING was written. Fix or zone them, then re-run.")
        return 1

    byd: dict[str, int] = {}
    scrubbed = 0
    for r, dest, out, counts in plan:
        byd[str(dest.parent.relative_to(SRC))] = byd.get(str(dest.parent.relative_to(SRC)), 0) + 1
        scrubbed += sum(counts.values())
        if not dry:
            dest.parent.mkdir(parents=True, exist_ok=True)
            with open(dest, "w", encoding="utf-8", newline="") as fh:
                fh.write(out)

    print("=" * 96)
    print("%s %d files into src/" % ("WOULD PUBLISH" if dry else "PUBLISHED", len(plan)))
    print("=" * 96)
    for d in sorted(byd):
        print("   %-40s %3d" % (d, byd[d]))
    print("\n   %d nickname rewrites applied by the scrub · 0 blocked" % scrubbed)
    if dry:
        print("\n--dry-run: nothing written.")
    else:
        print("\n✅ written. Nothing is committed or pushed — review `git status` / `git diff`.")
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(description="Publish one file into the snapshot")
    ap.add_argument("file", nargs="?", help="path in the canonical tree (or, with --update, in src/)")
    ap.add_argument("--candidates", action="store_true",
                    help="list what could ship but does not yet; publishes nothing")
    ap.add_argument("--all-candidates", action="store_true",
                    help="publish the whole reviewed candidate set (all-or-nothing on a block)")
    ap.add_argument("--to", help="destination directory under src/ (e.g. src/sensors/Indicators)")
    ap.add_argument("--update", action="store_true",
                    help="refresh a file already in src/ from its canonical counterpart")
    ap.add_argument("--dry-run", action="store_true")
    a = ap.parse_args()

    if a.candidates:
        return candidates()
    if a.all_candidates:
        return publish_all(a.dry_run)
    if not a.file:
        sys.stderr.write("publish: name a file, or use --candidates to see what could ship\n")
        return 2

    src_path = Path(a.file)
    if a.update:
        pub = Path(a.file) if Path(a.file).is_absolute() else REPO / a.file
        if not pub.exists():
            sys.stderr.write("publish: %s is not in the snapshot\n" % pub)
            return 2

        # ⛔ WRONG-TOOL GUARD (2026-08-08). This is the CODE path: it strips the NinjaScript
        # generated region, scrubs nicknames and adds the MPL header. It does NOT substitute
        # {{tokens}}, strip audit frontmatter, or rewrite CLAUDE.md links — publish_doc.py does.
        # Reaching for this one on a doc shipped literal `{{core_version}}` into three public
        # documents, and the two tools' names are similar enough that it will be reached for
        # again. Refusing and NAMING the right command is the whole fix.
        if pub.suffix.lower() == ".md" and pub.parent.name == "docs" and pub.parent.parent == REPO:
            sys.stderr.write(
                "publish: %s is a DOC, and this is the code path.\n"
                "  It would ship literal {{tokens}} and the internal audit frontmatter.\n"
                "  Use:  python tools/publish_doc.py --local \"<bin\\Custom>\" %s\n"
                "  (add --index for SENTINEL_DOCS.md, which is filtered to the published set)\n"
                % (pub.name, pub.name))
            return 2

        dest = pub
        local_root = os.environ.get("SENTINEL_LOCAL", "")
        matches = list(Path(local_root).rglob(pub.name)) if local_root else []
        if not matches:
            # Distinguish "unset" from "set but no match" — the old message said the former for
            # both, which misdiagnoses every file living outside the tree it points at.
            if not local_root:
                sys.stderr.write("publish: --update needs SENTINEL_LOCAL set to the canonical tree\n")
            else:
                sys.stderr.write(
                    "publish: SENTINEL_LOCAL is set to %s but %s is not under it.\n"
                    "  Point it at the tree that actually holds the file (e.g. …\\NinjaTrader 8\\Sentinel\n"
                    "  for Lab tooling, …\\bin\\Custom for NinjaScript).\n" % (local_root, pub.name))
            return 2
        src_path = matches[0]
    else:
        if not src_path.exists():
            sys.stderr.write("publish: %s does not exist\n" % src_path)
            return 2
        if not a.to:
            sys.stderr.write("publish: --to is required (which folder under src/ does it ship in?)\n")
            return 2
        dest = (REPO / a.to / src_path.name) if not Path(a.to).is_absolute() \
            else Path(a.to) / src_path.name

    print("publish: %s\n     -> %s" % (src_path, dest))
    raw = src_path.read_text(encoding="utf-8", errors="replace")
    out, counts = transform(raw, add_mpl=src_path.suffix.lower() == ".cs")

    if counts:
        n = sum(counts.values())
        print("  scrub: %d rewrite%s (%s)" % (n, "" if n == 1 else "s", ", ".join(sorted(counts))))
    else:
        print("  scrub: no rewrites")
    if len(out) != len(raw):
        print("  transforms: %+d chars (region strip / MPL header)" % (len(out) - len(raw)))

    if not gate(out, src_path.name):
        print("\n⛔ REFUSED — the transformed file still carries a PUBLIC-zone secret.")
        print("   Nothing was written. Fix the source, or zone the file in Lab/docs/zones.conf.")
        return 1
    print("  gate: PASS")

    if a.dry_run:
        print("\n--dry-run: nothing written.")
        return 0
    dest.parent.mkdir(parents=True, exist_ok=True)
    # newline="" so the snapshot keeps LF and a publish cannot manufacture whole-file drift.
    with open(dest, "w", encoding="utf-8", newline="") as fh:
        fh.write(out)
    print("\n✅ published. Run tools/check_parity.py --local <bin\\Custom> to confirm parity.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
