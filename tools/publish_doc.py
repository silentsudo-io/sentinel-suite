#!/usr/bin/env python3
r"""Render a canonical doc from bin\Custom\Docs into its published form.

WHY THIS EXISTS
    `check_parity.py` tells you a doc has drifted. Closing that drift is NOT a copy: the canonical
    copy carries three things the published copy must not, and every one of them was rediscovered
    the hard way by diffing published-vs-local by hand and getting the wrong answer three times.

      1. YAML AUDIT FRONTMATTER  (`tracks:` / `verified-against:` / `last-audited:`) — internal
         Docs-Health metadata. Meaningless to a reader who cannot run the auditor.
      2. {{TOKENS}}  (`{{core_version}}`, `{{voter_count}}`, `{{ports_table}}`) — substituted at
         render time from Docs\_generated\facts.json, which is computed by grepping the live .cs.
         ⚠ Copying a canonical doc WITHOUT substituting ships a literal `{{core_version}}` to the
         public. Single-sourcing from code is the whole reason volatile numbers cannot drift.
      3. `CLAUDE.md` LINKS — the internal rules/map file, which is not published. Its public
         counterpart is CONTRIBUTING.md.

    Run `Lab\docs\facts.py` first so the tokens reflect current code, then this.

        python tools/publish_doc.py --local "<bin\Custom>" ROADMAP.md [more.md ...]
        python tools/publish_doc.py --local "<...>" --check ROADMAP.md      # print, write nothing

⚠ WHAT THIS DOES NOT DO: decide whether a doc's new content SHOULD be public. A section describing
a tool that only exists in the private tree, or naming internal hosts, is an editorial call and is
left to a human. This closes mechanical drift only.
"""
from __future__ import annotations

import argparse
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, ".."))

FRONTMATTER = re.compile(r"\A---\r?\n.*?\r?\n---\r?\n", re.S)
TOKEN = re.compile(r"\{\{(\w+)\}\}")


def load_facts(custom: str) -> dict:
    p = os.path.join(custom, "Docs", "_generated", "facts.json")
    if not os.path.exists(p):
        sys.exit("no facts.json at %s — run Lab\\docs\\facts.py first" % p)
    with open(p, encoding="utf-8") as f:
        return json.load(f)


def render(text: str, facts: dict) -> tuple[str, list[str]]:
    text = FRONTMATTER.sub("", text, count=1)

    unresolved: list[str] = []

    def sub(m):
        k = m.group(1)
        v = facts.get(k)
        if v is None:
            # Leave it visible and REPORT it. Silently emitting a literal {{token}} to a public
            # site is exactly the failure this function exists to prevent.
            unresolved.append(k)
            return m.group(0)
        return str(v)

    text = TOKEN.sub(sub, text)
    # The internal map file is not published; CONTRIBUTING.md is its public counterpart.
    text = text.replace("../CLAUDE.md", "../CONTRIBUTING.md").replace("`CLAUDE.md`", "`CONTRIBUTING.md`")
    text = text.replace("CLAUDE.md build rules", "CONTRIBUTING.md build rules")
    text = text.replace("the CLAUDE.md order-ownership", "the CONTRIBUTING.md order-ownership")
    text = text.replace("root `CLAUDE.md`", "root `CONTRIBUTING.md`")
    return text, unresolved


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--local", required=True, help="path to bin\\Custom")
    ap.add_argument("--check", action="store_true", help="report only, write nothing")
    ap.add_argument("docs", nargs="+")
    a = ap.parse_args()

    facts = load_facts(a.local)
    rc = 0
    for name in a.docs:
        src = os.path.join(a.local, "Docs", name)
        dst = os.path.join(REPO, "docs", name)
        if not os.path.exists(src):
            print("  MISSING canonical: %s" % src)
            rc = 1
            continue
        with open(src, encoding="utf-8") as f:
            out, unresolved = render(f.read(), facts)
        if unresolved:
            print("  ⚠ %s — UNRESOLVED tokens: %s" % (name, ", ".join(sorted(set(unresolved)))))
            rc = 1
        if "CLAUDE.md" in out:
            print("  ⚠ %s — still references CLAUDE.md after rewrite" % name)
            rc = 1
        if a.check:
            print("  [check] %s — %d lines would be written" % (name, out.count("\n") + 1))
            continue
        with open(dst, "w", encoding="utf-8", newline="\n") as f:
            f.write(out)
        print("  published %s (%d lines)" % (name, out.count("\n") + 1))
    return rc


if __name__ == "__main__":
    raise SystemExit(main())
