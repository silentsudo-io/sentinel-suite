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

# This tool's own output is full of the characters the project writes in — arrows, rules, warning
# signs. On a Windows console defaulting to cp1252 that is not a mojibake nuisance, it is a hard
# UnicodeEncodeError that kills the run: adding one "⚠" to a DELIBERATE reason crashed the gate
# outright. A checker that dies on the text of its own explanation is worse than no checker.
try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

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
    # ⛔ SentinelCore_v1_0_0.cs WAS registered here (ResolveLane gutted because LaneAssign lived in
    #    the unreleased System Builder rung). REMOVED 2026-08-08: SentinelCore.SystemBuilder.cs —
    #    including `public static class LaneAssign` with Read() — shipped in d6c1aea, the full-suite
    #    release, so the premise "publishing it would be CS0246 for every user" stopped being true.
    #    The published Core now carries the real body. ⭐ Nobody noticed the reason had expired
    #    because the entry kept the file quietly out of the drift report — a registry entry whose
    #    justification dies goes on suppressing forever. That is why CLOSED DIVERGENCE now reports.
    # ── EDITORIAL CUTS (2026-08-04) ─────────────────────────────────────────────────────────────
    #  These are not stale files. They are places where the canonical doc says MORE than the public
    #  one should, and they are registered here for one reason: left in the DRIFT list, the obvious
    #  next action is to "close" them, and closing them republishes exactly what was withheld.
    #  A checker that reports an intentional difference as drift teaches its reader to ignore it.
    "SENTINEL_DATA_PLATFORM_SPEC.md": (35,
        "§15 (corpus EGRESS from a bake node) is withheld. It documents Lab\\sync\\corpus_pull.py, "
        "which is NOT shipped publicly, and names internal hosts and corpus file counts. A reader "
        "of this repo has neither the tool nor the machines, so the section is both a disclosure "
        "and useless to them. Everything else in the spec is published verbatim."),

    # 🔴 THIRD MANUAL BUMP (65→62 on 08-08, 62→56, 56→57 on 08-09). Each one was verified by
    #    reading the --diff and confirming the divergence is still only the withheld rows — and
    #    each one will rot again the next time a doc is added, because the canonical index grows
    #    by one row per doc while the published subset does not. A count is the WRONG KEY for a
    #    divergence that is "published is a SUBSET of canonical".
    #    ⇒ OWED, with the design already settled: key this entry on the SHAPE instead — every
    #      extra line must be a registry row (or the banner) whose link target this repo does not
    #      serve. `drifted` currently carries only (rel, n, loc), so the diff lines have to be
    #      threaded through to make that checkable. Until then this stays a count and stays rotting.
    "SENTINEL_DOCS.md": (57,
        "The canonical index registers every doc in Docs\\; this repo ships a subset. Publishing it "
        "verbatim would not merely break links, it would publish a table of contents for the "
        "private estate — infrastructure spec, rack runbook, PKI, the replay fleet — each with a "
        "status line describing it. Registry rows are filtered to docs this repo actually serves. "
        "⚠ Filter on BOTH .md and .html: several docs (Architecture Map, Process Atlas) ship as "
        "HTML with no .md sibling, and a .md-only filter silently drops their rows. "
        "AS OF 2026-08-08 THIS IS NO LONGER HAND-FILTERED: `publish_doc.py --index` generates it, "
        "keyed on the published set rather than on a secrecy denylist, so a doc that is new, private "
        "or merely unfinished is excluded by default. The generated result links to exactly the same "
        "24 documents the hand-curated version did. Regenerate rather than hand-edit; this line count "
        "moves whenever the canonical index grows, and a stale count here drops the file back into "
        "DRIFT — which is how it got reported as unresolved drift on 2026-08-08."),

    # ⛔ ROADMAP.md WAS registered here (a prose note naming the private _archive\ path without
    #    linking it). REMOVED 2026-08-08: publish_doc now de-links any target this repo does not
    #    serve, and normalise_doc mirrors that on the local side, so the difference is a mechanical
    #    transform rather than an editorial cut. The registry is for what a human decided to
    #    withhold; anything a transform explains does not belong in it.
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


# ── THIRD PUBLISH TRANSFORM: the nickname scrub ─────────────────────────────────
# The published copy has operator-private machine nicknames rewritten to generic ones
# (tools/scrub.py). Like the MPL header and the generated region, that is an INTENDED
# difference, so it must be normalised away here or every scrubbed file reports as drift
# forever -- and a report that is wrong on purpose is one people stop reading.
#
# Applied to the LOCAL side only: local still says the real name, published already says
# the generic one, so scrubbing local brings the two into the same coordinate system.
# A missing map makes this a no-op and scrub.py says so on stderr.
try:
    from scrub import load_map as _load_scrub, scrub as _apply_scrub
except ImportError:                                     # tool used standalone
    def _load_scrub(*_a, **_k):
        return []

    def _apply_scrub(t, _r=None):
        return t, {}

_SCRUB_RULES = None


def _scrub_rules():
    global _SCRUB_RULES
    if _SCRUB_RULES is None:
        _SCRUB_RULES = _load_scrub()
    return _SCRUB_RULES


def normalise(text: str, local_side: bool = False) -> list[str]:
    text = text.replace("\r\n", "\n").replace("\r", "\n")
    text = strip_mpl_header(strip_region(text))
    if local_side:
        text, _ = _apply_scrub(text, _scrub_rules())
    return [ln.rstrip() for ln in text.rstrip().split("\n")]


# ── DOCS ────────────────────────────────────────────────────────────────────────
# `docs/` is published prose, and it drifts exactly like `src/` does -- but nothing was
# watching it. That gap is the same shape as two failures already on the record: the
# dependency checker validated `src/` while the builder shipped a subset of `src/` and
# nobody validated the archive; and the Field Manual documented a superseded conviction
# formula until a stranger implemented it faithfully and inherited the bug. A published
# doc is an API, so it gets the same monitor the code gets.
#
# Docs carry their OWN publish-time transforms, and normalising them away is what makes
# the report readable (a naive diff calls every doc "drifted" forever):
#   1. YAML frontmatter  -- the docs-health block (`tracks:`, `verified-against:`) is
#      tooling metadata, stripped on the way out.
#   2. {{tokens}}        -- substituted from Docs/_generated/facts.json so volatile
#      numbers cannot drift. PROSE ONLY: fenced and inline code are protected, so a doc
#      can document a literal {{token}}.
#   3. CLAUDE.md         -- the private project map; the public equivalent is
#      CONTRIBUTING.md. A published doc pointing at CLAUDE.md would dangle.
# 1 and 2 are lifted from Sentinel\tools\md2atlas.py DELIBERATELY: if this normaliser and
# that renderer disagree, this tool reports drift that does not exist.
FRONTMATTER_RE = re.compile(r'^﻿?---\r?\n.*?\r?\n---\r?\n', re.S)
TOKEN_RE = re.compile(r'\{\{([a-z0-9_]+)\}\}')
CODE_RE = re.compile(r'```.*?```|`[^`\n]*`', re.S)
CLAUDEMD_RE = re.compile(r'`?CLAUDE\.md`?')


def _load_facts(local_root: Path) -> dict:
    try:
        import json as _json
        p = local_root / "Docs" / "_generated" / "facts.json"
        return _json.loads(p.read_text(encoding="utf-8"))
    except Exception:
        return {}


def normalise_doc(text: str, facts: dict, local_side: bool = False) -> list[str]:
    text = text.replace("\r\n", "\n").replace("\r", "\n")
    text = FRONTMATTER_RE.sub("", text, count=1)
    stash: list[str] = []

    def hide(m):
        stash.append(m.group(0))
        return "\x00%d\x00" % (len(stash) - 1)

    text = CODE_RE.sub(hide, text)
    text = TOKEN_RE.sub(lambda m: str(facts.get(m.group(1), m.group(0))), text)
    text = re.sub(r"\x00(\d+)\x00", lambda m: stash[int(m.group(1))], text)
    # AFTER restoring code spans, and backtick-preserving. `CLAUDE.md` is usually written as
    # INLINE CODE, so running this while code is stashed would silently miss every backticked
    # reference -- which is exactly what the first cut did, and the tool caught it as 2 lines of
    # phantom drift on a file that had just been published.
    text = CLAUDEMD_RE.sub(
        lambda m: "`CONTRIBUTING.md`" if m.group(0).startswith("`") else "CONTRIBUTING.md", text)
    # ⚠ SCRUB LAST, AFTER TOKEN SUBSTITUTION — order is load-bearing here, not stylistic.
    # The first cut scrubbed at the top of this function and a mention still drifted: the line
    # was a {{ports_table}} token, so substitution ran AFTERWARDS and put the private name
    # straight back from facts.json. A transform that runs before a substitution does not
    # survive it. (The value itself is fixed at source in facts.py; this is the belt.)
    if local_side:
        text, _ = _apply_scrub(text, _scrub_rules())
        # ⚠ FOURTH PUBLISH-TIME TRANSFORM, normalised here for the same reason as the other three
        # (frontmatter, tokens, scrub): publish_doc de-links any `Name.md` this repo does not
        # serve, so the published copy legitimately says `Runbook` where local says
        # `[Runbook](SENTINEL_RUNBOOK.md)`. Without this the six docs carrying such a link read as
        # permanent drift, and the obvious "fix" is to register them as deliberate divergences —
        # which would be the wrong primitive entirely: they are not editorial cuts, they are a
        # mechanical transform, and the registry is for things a human decided to withhold.
        text = _DOCLINK_RE.sub(
            lambda m: m.group(0) if m.group(2) in _served_docs() else m.group(1), text)
    # prose reflows freely; compare on content, not on where a line happened to wrap
    return [ln.rstrip() for ln in text.rstrip().split("\n") if ln.strip()]


_DOCLINK_RE = re.compile(r"\[([^\]]+)\]\(([A-Za-z0-9_.-]+)\.(?:md|html)\)")
_SERVED_CACHE: set | None = None


def _served_docs() -> set:
    """Doc stems this repo actually serves — .md OR .html (several ship HTML-only)."""
    global _SERVED_CACHE
    if _SERVED_CACHE is None:
        d = REPO / "docs"
        _SERVED_CACHE = {p.stem for p in d.iterdir()
                         if p.suffix in (".md", ".html")} if d.is_dir() else set()
    return _SERVED_CACHE


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
    compared: set = set()          # every published file actually reached, for the closed-divergence check

    # ── docs/ ── published prose, same canonical-ahead model as src/, own transforms.
    facts = _load_facts(local_root)
    doc_dir = REPO / "docs"
    local_docs = local_root / "Docs"
    if doc_dir.is_dir() and local_docs.is_dir():
        for pub in sorted(doc_dir.glob("*.md")):
            rel = pub.relative_to(REPO)
            loc = local_docs / pub.name
            if not loc.exists():
                # NOT orphaned: plenty of published docs are written for the public tree
                # and have no private counterpart. Silence beats a false alarm here.
                continue
            a = normalise_doc(pub.read_text(encoding="utf-8", errors="replace"), facts)
            b = normalise_doc(loc.read_text(encoding="utf-8", errors="replace"), facts,
                              local_side=True)
            if args.diff and pub.name == args.diff:
                print("\n".join(difflib.unified_diff(
                    a, b, fromfile=f"published/{pub.name}", tofile=f"local/{pub.name}",
                    lineterm="")))
                return 0
            compared.add(pub.name)
            if a == b:
                clean += 1
            else:
                n = sum(1 for l in difflib.unified_diff(a, b, n=0, lineterm="")
                        if l[:1] in "+-" and l[:3] not in ("+++", "---"))
                drifted.append((rel, n, loc))

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
        b = normalise(loc.read_text(encoding="utf-8", errors="replace"), local_side=True)

        if args.diff and pub.name == args.diff:
            print("\n".join(difflib.unified_diff(
                a, b, fromfile=f"published/{pub.name}", tofile=f"local/{pub.name}", lineterm="")))
            return 0

        compared.add(pub.name)
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

    # Only exempt a known file at its known SIZE. A changed count means new divergence landed on
    # top of the documented one, so it must resurface — that safety property is deliberate.
    #
    # ⚠ WHAT WAS WRONG WAS THE FAILURE MODE, NOT THE RULE (fixed 2026-08-08). When the count went
    # stale the file dropped back into DRIFT *anonymously*: the report said "66 lines
    # SENTINEL_DOCS.md" and nothing said it was a REGISTERED divergence whose count had merely
    # moved. The obvious next action on a DRIFT row is to close it by publishing — which for that
    # file would republish a table of contents for the private estate. A registry keyed on an
    # exact number decays every time the source grows, so the decay has to ANNOUNCE ITSELF.
    def registration(d):
        return DELIBERATE.get(Path(d[0]).name)

    deliberate = [d for d in drifted if registration(d) and registration(d)[0] == d[1]]
    stale_reg = [d for d in drifted if registration(d) and registration(d)[0] != d[1]]
    drifted = [d for d in drifted if not registration(d)]

    # ⛔ A REGISTERED DIVERGENCE THAT IS NOW FAITHFUL MEANS THE WITHHELD THING GOT PUBLISHED.
    # This is the dangerous direction and it was invisible until 2026-08-08: `publish.py --update`
    # overwrites the published copy from the canonical one, which silently CLOSES a divergence the
    # registry says must not be closed. It happened to SentinelCore that day. Drift is loud; the
    # absence of drift is not, so a divergence disappearing has to be announced too.
    drifted_names = {Path(d[0]).name for d in drifted} | {Path(d[0]).name for d in deliberate} \
        | {Path(d[0]).name for d in stale_reg}
    published_names = compared
    closed_reg = [n for n in DELIBERATE
                  if n not in drifted_names and (not published_names or n in published_names)]

    if closed_reg:
        print(f"CLOSED DIVERGENCE ({len(closed_reg)}) — registered as DELIBERATE, now IDENTICAL to local.")
        print("    The withheld difference is no longer withheld: something published it, most likely")
        print("    `publish.py --update`, which overwrites the snapshot from the canonical copy.")
        print("    Confirm the content was safe to ship, then DELETE the DELIBERATE entry — leaving a")
        print("    stale one is how the registry rots into a blind spot.\n")
        for n in sorted(closed_reg):
            print(f"    {n}")
            print(f"             was: {DELIBERATE[n][1][:150]}…")
        print()

    if stale_reg:
        print(f"STALE REGISTRATION ({len(stale_reg)}) — registered as DELIBERATE, but the size moved.")
        print("    These are NOT ordinary drift. Do NOT close one by publishing it — re-read the")
        print("    reason in DELIBERATE, confirm the divergence is still only what it describes,")
        print("    then update the line count there.\n")
        for rel, n, loc in sorted(stale_reg, key=lambda r: -r[1]):
            exp = DELIBERATE[Path(rel).name]
            print(f"    {n:>6} lines   {rel}   (registered at {exp[0]})")
            print(f"             {exp[1][:150]}…")
        print()

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
    # ⛔ stale_reg and closed_reg are FATAL, and were not until 2026-08-10. They were printed —
    # in full, with instructions — and then the exit code said 0, so `verify_all` reported
    # "ok snapshot parity / PASS — every guard green" with a STALE REGISTRATION on screen. The
    # ledger's guarantee table claims a green verify_all means "no drift · no STALE REGISTRATION ·
    # no CLOSED DIVERGENCE"; two of those three were unenforced.
    # ⭐ This is the house pattern, third time recorded: `rt` was COUNTED but not refused, the tag
    # filters MEASURED occupancy they never applied, and here the divergence registry is CHECKED
    # and not enforced. **Printing a hazard is not refusing it** — and a gate that prints while
    # passing is worse than no gate, because the summary line is what gets read.
    # CLOSED DIVERGENCE is the dangerous one: it means withheld content was republished.
    return 1 if (drifted or errors or orphaned or stale_reg or closed_reg) else 0


if __name__ == "__main__":
    raise SystemExit(main())
