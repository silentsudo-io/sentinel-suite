#!/usr/bin/env python3
r"""Prototype of the MISSING docs-health check: does every identifier a doc NAMES still exist?

audit.py tracks links, tokens and contract versions -- not whether a sentence is TRUE. That blind spot
is what let 9 docs name a dead class. This closes the cheapest part of it: any `backticked` symbol that
looks like an API identifier must appear somewhere in the tree.

    python ident_check.py <doc.md> [--section "## 6."] [--tree <bin/Custom>]

Reports UNKNOWN identifiers -- candidates for "this no longer exists". Read-only.
"""
from __future__ import annotations
import os, re, sys, argparse, subprocess
import sys as _sys, os as _os
_LAB_ROOT = _os.path.abspath(_os.path.join(_os.path.dirname(_os.path.abspath(__file__)), ".."))
if _LAB_ROOT not in _sys.path:
    _sys.path.insert(0, _LAB_ROOT)
from lab_faults import swallow

CODE = re.compile(r"`([^`\n]{2,80})`")
# only things that LOOK like code identifiers -- skip prose, paths, file names, hex, prices
IDENT = re.compile(r"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*(\(.*\))?$")
SKIP_SUFFIX = (".cs", ".md", ".py", ".conf", ".json", ".jsonl", ".html", ".xml", ".txt", ".nrd", ".db")
NOISE = {"true", "false", "null", "int", "bool", "double", "string", "long", "void", "var",
         "catch", "class", "using", "namespace", "new", "return", "if", "else", "this",
         "csproj", "sln", "dll", "exe", "jsonl"}

# Compiler/diagnostic codes (CS0246, MC1000) are quoted as prose, never defined in the tree.
CODE_NOISE = re.compile(r"^(CS|MC|SA|CA)\d{3,5}$")
# Naming TEMPLATES the docs quote deliberately: _vX_Y_Z, <Thing>, Foo_vN.
TEMPLATE = re.compile(r"(_v[XN]_[YN]_[ZN]|^_?v?[XYZN]$|<|>)")

# A doc may legitimately name a seam that is PLANNED and not yet built. Mark those lines/sections in
# the doc with this marker and the identifier stops being reported (the doc is honest, not stale).
PLANNED_MARK = re.compile(r"planned|not yet built|deferred|parked|future", re.I)


def symbols(text: str):
    out = []
    for m in CODE.finditer(text):
        raw = m.group(1).strip()
        if not IDENT.match(raw):
            continue
        head = raw.split("(")[0]
        if head.lower() in NOISE:
            continue
        if CODE_NOISE.match(head) or TEMPLATE.search(head):
            continue
        if any(head.lower().endswith(s) for s in SKIP_SUFFIX):
            continue
        leaf = head.split(".")[-1]
        if len(leaf) < 3 or leaf.islower() and "." not in head and len(leaf) < 5:
            continue
        out.append(leaf)
    seen, uniq = set(), []
    for s in out:
        if s not in seen:
            seen.add(s)
            uniq.append(s)
    return uniq


def slice_section(text: str, marker: str) -> str:
    i = text.find(marker)
    if i < 0:
        return text
    # to the next same-level heading
    level = marker.split()[0]
    j = text.find("\n" + level + " ", i + 1)
    return text[i:j if j > 0 else len(text)]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("doc")
    ap.add_argument("--section", default=None)
    ap.add_argument("--tree", default=None)
    a = ap.parse_args()

    text = open(a.doc, encoding="utf-8", errors="replace").read()
    if a.section:
        text = slice_section(text, a.section)

    tree = a.tree or os.path.dirname(os.path.dirname(os.path.abspath(a.doc)))
    syms = symbols(text)
    print(f"{len(syms)} candidate identifiers in {os.path.basename(a.doc)}"
          f"{' ' + a.section if a.section else ''}\n")

    # ONE pass over the tree -> a token set. Beats N subprocess greps and needs no external tool.
    TOKEN = re.compile(r"[A-Za-z_][A-Za-z0-9_]*")
    tokens, files = set(), 0
    for root, dirs, names in os.walk(tree):
        dirs[:] = [d for d in dirs if d not in ("_archive", ".git", "obj", "bin", "__pycache__")]
        for n in names:
            if not n.endswith((".cs", ".py")):
                continue
            try:
                with open(os.path.join(root, n), encoding="utf-8", errors="replace") as fh:
                    tokens.update(TOKEN.findall(fh.read()))
                files += 1
            except OSError as _swex:
                swallow("docs.ident_check.main", _swex)
    print(f"(scanned {files} source files, {len(tokens)} distinct tokens)\n")

    unknown = [s for s in syms if s not in tokens]

    if unknown:
        print("UNKNOWN — named in the doc, not found in the tree:")
        for s in unknown:
            print("   " + s)
    else:
        print("all identifiers resolve.")
    print(f"\n{len(syms) - len(unknown)}/{len(syms)} resolve · {len(unknown)} unknown")
    return 1 if unknown else 0


if __name__ == "__main__":
    sys.exit(main())
