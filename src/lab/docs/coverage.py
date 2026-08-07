#!/usr/bin/env python3
r"""Sentinel COVERAGE — the missing half of docs-health (code -> doc).

audit.py asks "is what a doc SAYS still true?".  This asks the opposite and, until now,
unasked question: **does this artifact have a doc AT ALL?**

WHY THIS EXISTS. On 2026-08-07 a census of the whole Sentinel surface found 351 artifacts
against 68 docs: 78 under a `tracks:` contract, 172 named only in passing prose, and 101
named NOWHERE. The drift monitor was healthy the entire time, because a doc that does not
exist cannot drift. Coverage and freshness are different failures and only one of them was
being measured -- the suite's own lesson that "silence is not evidence"
([[measure-dont-infer]]) applied to its own documentation.

WHAT IT CLASSIFIES, per artifact:
    TRACKED    a doc names it in `tracks:` -- it is under contract, audit.py polices it
    MENTIONED  named somewhere in the doc corpus, but no doc OWNS it
    DARK       named in no doc at all

PUBLICATION SCOPE. The public repo's `src/` tree is the manifest of what ships, so it is
read as data rather than re-declared here -- one bad list and a private tool is documented
in public. Each artifact is stamped `published` / `private` accordingly, and every consumer
(notably wiki.py) must filter on it rather than assume.

STATIC + READ-ONLY: reads files only. Never edits a doc, never touches NT, needs nothing
running. Importable -- audit.py calls scan_coverage() to fold findings into docs_finding.

    python coverage.py                # full table + rollups
    python coverage.py --dark         # only the artifacts no doc names
    python coverage.py --family lab   # one family
    python coverage.py --json out.json

Spec: bin\Custom\Docs\SENTINEL_DOCS_HEALTH_SPEC.md.
"""
from __future__ import annotations
import os, re, io, json, argparse, collections
import sys as _sys, os as _os
_LAB_ROOT = _os.path.abspath(_os.path.join(_os.path.dirname(_os.path.abspath(__file__)), ".."))
if _LAB_ROOT not in _sys.path:
    _sys.path.insert(0, _LAB_ROOT)
from lab_faults import swallow

HERE   = os.path.dirname(os.path.abspath(__file__))
LAB    = os.path.abspath(os.path.join(HERE, ".."))
SENT   = os.path.abspath(os.path.join(LAB, ".."))
NT8    = os.path.abspath(os.path.join(SENT, ".."))
CUSTOM = os.path.join(NT8, "bin", "Custom")
DOCS   = os.path.join(CUSTOM, "Docs")
BRIDGE = r"C:\ntbv-src"
# The publishing clone. ⚠ NOT Sentinel\suite-oss (a stale staging copy) -- see the
# "TWO OSS PATHS" warning in memory\NOW.md; work written to the wrong one has been
# reported as published before.
REPO   = os.path.join(os.path.expanduser("~"), "Documents", "GitHub", "sentinel-suite")

SKIP_DIR = {"__pycache__", ".git", "node_modules", "site-packages", "_archive", "obj",
            "bin", "Lib", "Include", "Scripts", "share", "_generated", "dist", "build",
            "target", ".venv", "venv", "_copier_samples", "backup"}

# An artifact family: (label, root, extensions, sentinel-only?)
FAMILIES = [
    ("indicator",  os.path.join(CUSTOM, "Indicators"),      (".cs",),  True),
    ("strategy",   os.path.join(CUSTOM, "Strategies"),      (".cs",),  True),
    ("addon",      os.path.join(CUSTOM, "AddOns"),          (".cs",),  True),
    ("bartype",    os.path.join(CUSTOM, "BarsTypes"),       (".cs",),  True),
    ("chartstyle", os.path.join(CUSTOM, "ChartStyles"),     (".cs",),  True),
    ("drawtool",   os.path.join(CUSTOM, "DrawingTools"),    (".cs",),  True),
    ("lab",        os.path.join(SENT, "Lab"),               (".py",),  False),
    ("azimuth",    os.path.join(SENT, "Azimuth"),           (".py",),  False),
    ("azimuth-ui", os.path.join(SENT, "Azimuth", "app"),    (".ts", ".tsx", ".rs"), False),
    ("sent-tools", os.path.join(SENT, "tools"),             (".py",),  False),
    ("bridge",     BRIDGE,                                  (".py",),  False),
    ("template",   os.path.join(NT8, "templates"),          (".xml",), True),
    ("config",     SENT,                                    (".conf",), False),
]

VER_CONST = re.compile(r'(?:const\s+string\s+\w*Version\w*|__version__|version)\s*=\s*"([\d]+\.[\d.]+)"')
VER_NAME  = re.compile(r"_v(\d+)_(\d+)_(\d+)$")
CHANGELOG = re.compile(r"^\s*(?://|#)?\s*Changelog", re.M | re.I)
# ⚠ NinjaScript opens with `#region Using declarations`, so the header banner sits AFTER the
# #endregion, not at the top of the file. Requiring the word "Changelog" (or an \A-anchored
# comment) called 18 fully-documented smoothers self-undocumented. A run of 4+ consecutive
# comment lines is what a real header actually looks like here.
CS_RUN    = re.compile(r"(?:^[ \t]*//[^\n]*\n){4,}", re.M)
NAMESPACE = re.compile(r"^\s*namespace\s+([\w.]+)", re.M)
CLASSDECL = re.compile(r"^\s*public\s+(?:partial\s+)?(?:sealed\s+)?class\s+(\w+)", re.M)
# A …State seam publish/consume, the suite's actual coupling graph
SEAM_SET  = re.compile(r"SentinelCore\.Set(\w+?)State\s*\(")
SEAM_GET  = re.compile(r"SentinelCore\.Get(\w+?)State\s*\(")
PY_DOC    = ('"""', "'''", 'r"""', "r'''")


def _read(p):
    try:
        return io.open(p, encoding="utf-8", errors="replace").read()
    except OSError as _swex:
        swallow("docs.coverage._read", _swex)
        return ""


def _walk(root, exts):
    if not os.path.isdir(root):
        return
    for dp, dirs, names in os.walk(root):
        dirs[:] = [d for d in dirs if d not in SKIP_DIR]
        for n in names:
            if n.endswith(exts):
                yield os.path.join(dp, n)


def _is_sentinel_cs(name, text):
    """A .cs file belongs to the suite if it is named for it or speaks the runtime's API."""
    return (name.startswith(("Sentinel", "Council"))
            or "SentinelCore." in text or "AddOns.Sentinel" in text)


def published_index():
    """basename(lower) -> FULL PATH of the published copy in the public repo's src/.

    The manifest of what ships, read as data rather than re-declared. Returns the PATH, not
    just the name, because a consumer that renders documentation must read the PUBLISHED
    file: the snapshot is deliberately divergent in places (SentinelCore.ResolveLane is
    gutted so the released tree compiles without the unreleased System Builder), so a page
    generated from the canonical file would describe behaviour the published file does not
    have -- documentation that is wrong only for the people who can actually run the code.
    """
    out = {}
    src = os.path.join(REPO, "src")
    if not os.path.isdir(src):
        return out
    for dp, dirs, names in os.walk(src):
        dirs[:] = [d for d in dirs if d not in SKIP_DIR]
        for n in names:
            out.setdefault(n.lower(), os.path.join(dp, n))
    return out


def _published_index():
    return set(published_index())


def artifacts():
    """Every Sentinel artifact, with the identity a reference page needs."""
    pub = published_index()
    seen, out = set(), []
    for kind, root, exts, sentinel_only in FAMILIES:
        for p in _walk(root, exts):
            rp = os.path.realpath(p)
            if rp in seen:
                continue
            name = os.path.basename(p)
            text = _read(p)
            if kind == "template" and "sentinel" not in os.path.relpath(p, NT8).lower():
                continue
            if sentinel_only and exts == (".cs",) and not _is_sentinel_cs(name, text):
                continue
            if name == "__init__.py" and len(text) < 200:
                continue
            seen.add(rp)
            stem = os.path.splitext(name)[0]
            vm = VER_CONST.search(text)
            nm = VER_NAME.search(stem)
            ver = vm.group(1) if vm else (".".join(nm.groups()) if nm else "")
            ns = NAMESPACE.search(text)
            cls = CLASSDECL.search(text)
            lead = text.lstrip()
            documented_in_file = (bool(CHANGELOG.search(text[:8000]))
                                  or bool(CS_RUN.search(text[:12000]))
                                  or lead.startswith(PY_DOC))
            out.append(dict(
                kind=kind, name=name, stem=re.sub(r"_v\d+_\d+_\d+$", "", stem),
                path=os.path.relpath(p, NT8) if not p.startswith(BRIDGE) else p,
                ver=ver, lines=text.count("\n") + 1,
                selfdoc=documented_in_file,
                ns=ns.group(1) if ns else "", cls=cls.group(1) if cls else "",
                publishes=sorted(set(SEAM_SET.findall(text))),
                consumes=sorted(set(SEAM_GET.findall(text))),
                scope="published" if name.lower() in pub else "private",
                pubpath=pub.get(name.lower(), ""),
            ))
    return out


def docs_index():
    """(tracks-key -> [doc]), the whole corpus lowercased for mention search, and per-doc text."""
    contracted, per, blob = {}, {}, []
    if not os.path.isdir(DOCS):
        return contracted, "", per
    for n in sorted(os.listdir(DOCS)):
        if not n.endswith(".md"):
            continue
        t = _read(os.path.join(DOCS, n))
        per[n] = t
        blob.append(t)
        m = re.search(r"^tracks:\s*\[(.+?)\]", t, re.M | re.S)
        if not m:
            continue
        for item in m.group(1).split(","):
            item = item.strip().strip("'\"")
            if item:
                contracted.setdefault(item.replace("\\", "/").lower(), []).append(n)
    return contracted, "\n".join(blob).lower(), per


def classify(arts=None, idx=None):
    """Stamp every artifact TRACKED / MENTIONED / DARK. Returns the artifact rows."""
    arts = arts if arts is not None else artifacts()
    contracted, blob, _ = idx if idx is not None else docs_index()
    for a in arts:
        rel = a["path"].replace("\\", "/").lower()
        rel_custom = rel.replace("bin/custom/", "")
        docs = []
        for key, dl in contracted.items():
            if key and (key in rel or key in rel_custom or rel_custom.startswith(key.rstrip("/"))):
                docs += dl
        # A "mention" needs a distinctive token: a bare stem like `cv` or `db` matches prose.
        mention = a["name"].lower() in blob or (len(a["stem"]) > 6 and a["stem"].lower() in blob)
        a["docs"] = sorted(set(docs))
        a["state"] = "TRACKED" if docs else ("MENTIONED" if mention else "DARK")
    return arts


# --------------------------------------------------------------------------- audit.py hook

def scan_coverage(add):
    """Fold coverage findings into an audit.py Findings collector. Returns a rollup dict.

    Severity is deliberately graded, not uniform: a DARK artifact that ships PUBLICLY is a
    WARN (a stranger meets undocumented code), a dark private one is INFO (we know what it
    is). A nag that fires the same way for both teaches its reader to ignore both.
    """
    rows = classify()
    counts = collections.Counter(r["state"] for r in rows)
    dark_public = 0
    for r in rows:
        if r["state"] != "DARK":
            continue
        pub = r["scope"] == "published"
        dark_public += pub
        add(r["name"], "undocumented", "WARN" if pub else "INFO",
            "%s %s (%d ln) is named in no doc%s" %
            (r["kind"], r["path"], r["lines"], " -- and it SHIPS PUBLICLY" if pub else ""))
    for r in rows:
        if r["state"] == "DARK" or r["selfdoc"]:
            continue
        add(r["name"], "no_selfdoc", "INFO",
            "%s %s has no in-file changelog/docstring" % (r["kind"], r["path"]))
    return dict(artifacts=len(rows), tracked=counts["TRACKED"], mentioned=counts["MENTIONED"],
                dark=counts["DARK"], dark_public=dark_public)


# --------------------------------------------------------------------------- cli

def main():
    ap = argparse.ArgumentParser(description="Sentinel doc-coverage audit (code -> doc)")
    ap.add_argument("--dark", action="store_true", help="only artifacts no doc names")
    ap.add_argument("--family", help="restrict to one family (indicator, lab, bartype, ...)")
    ap.add_argument("--scope", choices=["published", "private"], help="restrict by publication scope")
    ap.add_argument("--json", metavar="PATH", help="write the full records as JSON")
    a = ap.parse_args()

    rows = classify()
    if a.family:
        rows = [r for r in rows if r["kind"] == a.family]
    if a.scope:
        rows = [r for r in rows if r["scope"] == a.scope]

    by = collections.defaultdict(collections.Counter)
    for r in rows:
        by[r["kind"]][r["state"]] += 1
        by[r["kind"]]["_n"] += 1
        by[r["kind"]]["_pub"] += r["scope"] == "published"
        by[r["kind"]]["_nodoc"] += not r["selfdoc"]

    print("=" * 96)
    print("SENTINEL DOC-COVERAGE  (code -> doc)   %d artifacts" % len(rows))
    print("=" * 96)
    print("%-13s%7s%9s%11s%7s%9s%9s" % ("family", "files", "TRACKED", "MENTIONED", "DARK",
                                        "public", "no-hdr"))
    tot = collections.Counter()
    for k in sorted(by):
        c = by[k]
        tot.update(c)
        print("%-13s%7d%9d%11d%7d%9d%9d" % (k, c["_n"], c["TRACKED"], c["MENTIONED"],
                                            c["DARK"], c["_pub"], c["_nodoc"]))
    print("%-13s%7d%9d%11d%7d%9d%9d" % ("TOTAL", tot["_n"], tot["TRACKED"], tot["MENTIONED"],
                                        tot["DARK"], tot["_pub"], tot["_nodoc"]))

    dark_pub = [r for r in rows if r["state"] == "DARK" and r["scope"] == "published"]
    if dark_pub:
        print("\n⚠ %d DARK artifacts SHIP PUBLICLY — a stranger meets undocumented code:" % len(dark_pub))
        for r in sorted(dark_pub, key=lambda r: -r["lines"]):
            print("    %6d ln  %s" % (r["lines"], r["path"]))

    if a.dark:
        print("\nDARK — named in no doc:")
        for k in sorted({r["kind"] for r in rows if r["state"] == "DARK"}):
            d = sorted((r for r in rows if r["kind"] == k and r["state"] == "DARK"),
                       key=lambda r: -r["lines"])
            print("\n  [%s] %d" % (k, len(d)))
            for r in d:
                print("     %6d ln  %-12s %s" % (r["lines"], r["scope"], r["path"]))

    if a.json:
        json.dump(rows, io.open(a.json, "w", encoding="utf-8"), indent=1)
        print("\nwrote %s" % a.json)
    return 0


if __name__ == "__main__":
    try:
        _sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except Exception as _swex:
        swallow("docs.coverage.stdout", _swex)
    raise SystemExit(main())
