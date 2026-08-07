#!/usr/bin/env python3
r"""Sentinel WIKI — generate the per-artifact reference layer from the code itself.

THE MODEL. Authored docs (THESIS, the specs, the doctrine) explain WHY and stay hand-written --
generating prose like that would make it worse. This generates the other half, the part nobody
can keep current by hand: one reference page per artifact, built from what the artifact already
declares about itself.

WHY GENERATE RATHER THAN WRITE. Documentation here lives in four homes -- Docs\*.md, the public
repo's folder READMEs, the memory dir, and in-file changelogs -- and on 2026-08-07 they disagreed
about SentinelCore's version three ways (1.44.0 in five doc headers, 1.45.0 in NOW.md, 1.47.0 in
the artifact). A fifth hand-maintained home becomes the fifth number. Every C# file in this suite
already carries a meticulous changelog; that IS the per-file documentation, it is simply trapped
where nothing indexes it. This lifts it out. The index is computed, so it cannot drift.

⛔ THE PUBLICATION BOUNDARY IS THE WHOLE RISK. bin\Custom holds unreleased rungs (Council, Bridge,
Keel, Conductor, Copier, Helm) plus infra hostnames and account identifiers. Publishing the full
set would leak all of it, and this repo has already had to redact real account numbers once. So:

  * scope defaults to PUBLIC -- the safe direction is the easy one. Generating the private set
    takes an explicit `--scope private|all`. (The inverse cost a fleet-wide near-miss once, when
    `dialog --close` silently NOMATCHed unless `--all` was also passed: the safe action was the
    harder one to reach.)
  * "published" is not a list maintained here. It is read from the public repo's src/ tree by
    coverage.py, so the manifest has exactly one home and cannot be re-declared wrongly.
  * every page states its scope, so a leaked page is identifiable after the fact.

    python wiki.py                      # public set -> Docs\_generated\wiki
    python wiki.py --scope all --out X  # everything, for internal use
    python wiki.py --check              # report what WOULD be written, write nothing

Companion to coverage.py (which classifies) and audit.py (which polices drift).
"""
from __future__ import annotations
import os, re, io, argparse, collections, datetime as dt
import sys as _sys, os as _os
_LAB_ROOT = _os.path.abspath(_os.path.join(_os.path.dirname(_os.path.abspath(__file__)), ".."))
if _LAB_ROOT not in _sys.path:
    _sys.path.insert(0, _LAB_ROOT)
from lab_faults import swallow
import coverage as cov

OUT_DEFAULT = os.path.join(cov.CUSTOM, "Docs", "_generated", "wiki")

FAMILY_TITLE = {
    "indicator": "Indicators", "strategy": "Strategies", "addon": "AddOns / runtime",
    "bartype": "Bar types", "chartstyle": "Chart styles", "drawtool": "Drawing tools",
    "lab": "Lab (Python)", "azimuth": "Azimuth (Python)", "azimuth-ui": "Azimuth (front end)",
    "sent-tools": "Sentinel tools", "bridge": "NT8 bridge", "template": "Chart templates",
    "config": "Config files",
}

# ⚠ The banner is NOT at the top of the file. NinjaScript files open with
# `#region Using declarations`, so the documentation block sits AFTER the #endregion. An
# \A-anchored match reported 18 smoothers as "carries no changelog" when every one of them
# has a full header — a generated page confidently stating a false absence, which is worse
# than no page. Take the LONGEST run of consecutive // lines instead of the first.
CS_RUN    = re.compile(r"(?:^[ \t]*//[^\n]*\n){4,}", re.M)
CS_CHANGE = re.compile(r"^\s*//\s*Changelog\s*$(.*?)(?=^\s*//\s*=====|\Z)", re.M | re.S)
PY_DOCSTR = re.compile(r'\A\s*(?:#![^\n]*\n)?\s*r?("""|\'\'\')(.*?)\1', re.S)
MPL_LINE  = re.compile(r"Mozilla Public License|mozilla\.org/MPL|This Source Code Form|"
                       r"one at https|Copyright \(c\) \d{4}|^[─═\s]*$")


def _decomment(block):
    """Strip the // prefix, drop the MPL boilerplate and leading/trailing rules."""
    out = [re.sub(r"^[ \t]*//[ \t]?", "", ln) for ln in block.splitlines()]
    while out and MPL_LINE.search(out[0]):
        out.pop(0)
    while out and MPL_LINE.search(out[-1]):
        out.pop()
    return "\n".join(out).strip("\n")


def _slug(s):
    return re.sub(r"[^a-z0-9]+", "-", s.lower()).strip("-")


def changelog_of(path, kind):
    """The artifact's own words about itself: C# changelog block, or Python module docstring."""
    text = cov._read(path)
    if kind in ("indicator", "strategy", "addon", "bartype", "chartstyle", "drawtool"):
        m = CS_CHANGE.search(text)
        if m:
            return _decomment(m.group(1))
        runs = CS_RUN.findall(text)
        return _decomment(max(runs, key=len)) if runs else ""
    m = PY_DOCSTR.match(text)
    return m.group(2).strip("\n") if m else ""


def abs_path(a, prefer_published=False):
    r"""Where to READ this artifact from.

    ⛔ THE DEFECT THIS CLOSES. The generator rendered every page from the CANONICAL
    bin\Custom file, including at --scope public. But the published snapshot is deliberately
    divergent: `SentinelCore.ResolveLane` is GUTTED on the way out, because its real body
    calls LaneAssign.Read() and LaneAssign lives in the unreleased System Builder rung --
    publishing the full body would be CS0246 for every user. So a public page built from the
    canonical file documented a method the shipped file does not have.

    That is a worse failure than an out-of-date page. It is documentation that is wrong
    ONLY for the people who can actually run the code, and right for everyone who cannot
    check it. At --scope public the body therefore comes from the repo's src/ copy -- the
    file the reader is holding.
    """
    if prefer_published and a.get("pubpath") and os.path.exists(a["pubpath"]):
        return a["pubpath"]
    p = a["path"]
    return p if os.path.isabs(p) else os.path.join(cov.NT8, p)


def page(a, backlinks, from_published=False):
    """One artifact's reference page."""
    L = []
    L.append("# %s" % a["name"])
    L.append("")
    L.append("> `%s`" % a["path"].replace("\\", "/"))
    L.append("")
    rows = [("Family", FAMILY_TITLE.get(a["kind"], a["kind"])),
            ("Version", a["ver"] or "—"),
            ("Size", "%d lines" % a["lines"]),
            ("Scope", "**public** — ships in `sentinel-suite`" if a["scope"] == "published"
                      else "private — not published")]
    if a["cls"]:
        rows.append(("Class", "`%s`" % a["cls"]))
    if a["ns"]:
        rows.append(("Namespace", "`%s`" % a["ns"]))
    if a["publishes"]:
        rows.append(("Publishes seams", ", ".join("`%sState`" % s for s in a["publishes"])))
    if a["consumes"]:
        rows.append(("Consumes seams", ", ".join("`%sState`" % s for s in a["consumes"])))
    rows.append(("Documented by", ", ".join("[%s](../../%s)" % (d[:-3], d) for d in a["docs"])
                 if a["docs"] else "_no doc tracks this artifact_"))
    if backlinks:
        rows.append(("Depends on this", ", ".join("[%s](%s.md)" % (b, _slug(b)) for b in backlinks)))
    L.append("| | |")
    L.append("|---|---|")
    for k, v in rows:
        L.append("| **%s** | %s |" % (k, v))
    L.append("")

    src = abs_path(a, prefer_published=from_published)
    reading_published = from_published and src == a.get("pubpath")
    if reading_published:
        rows_note = ("Rendered from the **published** copy in `sentinel-suite/src/`, not the "
                     "author's private tree — so this page describes the file you actually have.")
        L.append("> " + rows_note)
        L.append("")
    body = changelog_of(src, a["kind"])
    if body:
        L.append("## What the file says about itself")
        L.append("")
        L.append("```text")
        L.append(body[:20000])
        L.append("```")
    else:
        L.append("## What the file says about itself")
        L.append("")
        L.append("_This artifact carries no changelog or module docstring._ "
                 "That is the gap to close first — a generated page can only surface what the "
                 "file declares.")
    L.append("")
    return "\n".join(L) + "\n"


def index(rows, scope):
    L = ["# Sentinel — artifact reference", "",
         "_Generated from the code by `Lab/docs/wiki.py`. Do not hand-edit: every page is rebuilt "
         "from the artifact's own changelog, so edits here are lost and, worse, become a fifth "
         "version of the truth._", "",
         "**Scope:** %s · **%d artifacts** · generated %s" %
         (scope, len(rows), dt.datetime.now().strftime("%Y-%m-%d")), ""]
    dark = [r for r in rows if r["state"] == "DARK"]
    nodoc = [r for r in rows if not r["selfdoc"]]
    if dark or nodoc:
        L += ["> ⚠ **%d** of these are named in no authored doc, and **%d** carry no in-file "
              "changelog or docstring — their pages will be thin until that is fixed."
              % (len(dark), len(nodoc)), ""]
    by = collections.defaultdict(list)
    for r in rows:
        by[r["kind"]].append(r)
    for k in sorted(by, key=lambda k: FAMILY_TITLE.get(k, k)):
        L.append("## %s (%d)" % (FAMILY_TITLE.get(k, k), len(by[k])))
        L.append("")
        L.append("| artifact | version | lines | documented by |")
        L.append("|---|---|---:|---|")
        for r in sorted(by[k], key=lambda r: r["name"].lower()):
            docs = ", ".join(d[:-3] for d in r["docs"]) or ("—" if r["state"] == "DARK"
                                                            else "_mentioned only_")
            L.append("| [%s](%s.md) | %s | %d | %s |"
                     % (r["name"], _slug(r["name"]), r["ver"] or "—", r["lines"], docs))
        L.append("")
    return "\n".join(L) + "\n"


def main():
    ap = argparse.ArgumentParser(description="Generate the Sentinel artifact reference")
    ap.add_argument("--scope", choices=["public", "private", "all"], default="public",
                    help="which artifacts to render (default: public — the safe direction)")
    ap.add_argument("--out", default=OUT_DEFAULT)
    ap.add_argument("--family", help="restrict to one family")
    ap.add_argument("--check", action="store_true", help="report only, write nothing")
    a = ap.parse_args()

    rows = cov.classify()
    if a.scope == "public":
        rows = [r for r in rows if r["scope"] == "published"]
    elif a.scope == "private":
        rows = [r for r in rows if r["scope"] != "published"]
    if a.family:
        rows = [r for r in rows if r["kind"] == a.family]

    # Backlinks: who consumes a seam this artifact publishes.
    # ⛔ SCOPED TO THE RENDERED SET, and that is a disclosure control, not tidiness. Computed
    # across the whole tree, SentinelTBars' public page listed Council and Cockpit as consumers
    # — both PRIVATE. A page can leak a private tool by naming it as a dependant even when the
    # tool's own page is correctly withheld, so the graph is restricted to artifacts that are
    # themselves being rendered.
    rendered = {r["name"] for r in rows}
    pubmap = collections.defaultdict(set)
    for r in cov.classify():
        if r["name"] not in rendered:
            continue
        for s in r["consumes"]:
            pubmap[s].add(r["name"])
    back = {r["name"]: sorted({n for s in r["publishes"] for n in pubmap.get(s, ())} - {r["name"]})
            for r in rows}

    print("scope=%s  artifacts=%d  dark=%d  no-selfdoc=%d"
          % (a.scope, len(rows), sum(r["state"] == "DARK" for r in rows),
             sum(not r["selfdoc"] for r in rows)))
    if a.check:
        for r in sorted(rows, key=lambda r: (r["kind"], r["name"])):
            print("   %-11s %-46s %s" % (r["kind"], r["name"],
                                         "DARK" if r["state"] == "DARK" else r["state"].lower()))
        return 0

    os.makedirs(a.out, exist_ok=True)
    from_pub = a.scope == "public"
    missing = [r["name"] for r in rows if from_pub and not r.get("pubpath")]
    if missing:
        print("⚠ %d public-scope artifacts have no published counterpart to read from; "
              "rendering those from the canonical tree: %s"
              % (len(missing), ", ".join(missing[:5]) + ("…" if len(missing) > 5 else "")))
    for r in rows:
        p = os.path.join(a.out, _slug(r["name"]) + ".md")
        try:
            io.open(p, "w", encoding="utf-8").write(
                page(r, back.get(r["name"], []), from_published=from_pub))
        except OSError as _swex:
            swallow("docs.wiki.write", _swex)
    io.open(os.path.join(a.out, "index.md"), "w", encoding="utf-8").write(index(rows, a.scope))
    print("wrote %d pages + index.md -> %s" % (len(rows), a.out))
    return 0


if __name__ == "__main__":
    try:
        _sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except Exception as _swex:
        swallow("docs.wiki.stdout", _swex)
    raise SystemExit(main())
