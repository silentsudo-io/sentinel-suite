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
# Fenced and inline code are PROTECTED from token substitution, so a doc can document a literal
# {{token}} without the renderer eating it. ⚠ THIS WAS MISSING UNTIL 2026-08-08, and the two other
# implementations of this same transform both had it: md2atlas.py (the authoritative renderer,
# line ~185) and check_parity.normalise_doc, whose own comment says the rules are lifted from
# md2atlas DELIBERATELY because "if this normaliser and that renderer disagree, this tool reports
# drift that does not exist." This file was the third implementation and the only one without the
# rule, so it did BOTH harms at once: it published `v1.47.0` where the prose said `{{core_version}}`
# — turning a sentence ABOUT the token into a nonsense sentence — and it made check_parity report
# permanent 4-line drift on a file that was correctly published. Keep all three in step.
CODE = re.compile(r"```.*?```|`[^`\n]*`", re.S)


def load_facts(custom: str) -> dict:
    p = os.path.join(custom, "Docs", "_generated", "facts.json")
    if not os.path.exists(p):
        sys.exit("no facts.json at %s — run Lab\\docs\\facts.py first" % p)
    with open(p, encoding="utf-8") as f:
        return json.load(f)


def render(text: str, facts: dict, delink: bool = True) -> tuple[str, list[str]]:
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

    stash: list[str] = []

    def hide(m):
        stash.append(m.group(0))
        return "\x00%d\x00" % (len(stash) - 1)

    text = CODE.sub(hide, text)
    text = TOKEN.sub(sub, text)
    text = re.sub(r"\x00(\d+)\x00", lambda m: stash[int(m.group(1))], text)
    # The internal map file is not published; CONTRIBUTING.md is its public counterpart.
    text = text.replace("../CLAUDE.md", "../CONTRIBUTING.md").replace("`CLAUDE.md`", "`CONTRIBUTING.md`")
    text = text.replace("CLAUDE.md build rules", "CONTRIBUTING.md build rules")
    text = text.replace("the CLAUDE.md order-ownership", "the CONTRIBUTING.md order-ownership")
    text = text.replace("root `CLAUDE.md`", "root `CONTRIBUTING.md`")

    # ── DE-LINK ANY DOC THIS REPO DOES NOT SERVE ────────────────────────────────────────────────
    # Applies to EVERY doc, not just the index. The index filter (--index) was built first and
    # fixed only SENTINEL_DOCS; a site-wide count then found 9 dead internal links across 6 other
    # published docs — ROADMAP alone had 4, and one was introduced the same day by adding a
    # perfectly reasonable cross-reference to a spec that happens not to ship.
    #
    # ⇒ The failure was never index-specific. Any doc can link to a doc that stayed private or
    # simply has not shipped, and the author has no way to know which without checking the repo.
    # So the repo answers it: the link degrades to plain text and the prose survives.
    # Bare `Name.md` / `Name.html` only — a path with a slash (../CONTRIBUTING.md) or a URL is
    # never touched.
    # ⚠ ORDER IS LOAD-BEARING (2026-08-08). The index filter keys on LINKS, so de-linking
    # first destroys the very thing it matches: a row pointing at a PRIVATE doc became
    # plain text, stopped looking like a linking row, and SURVIVED the filter — putting
    # private doc names into the published index as prose. Second ordering break of the
    # same day (the scrub had to move after token substitution for the same reason).
    # ⇒ With --index, render() defers this and main() applies it AFTER filter_index.
    docs_dir = os.path.join(REPO, "docs")
    if delink and os.path.isdir(docs_dir):
        pub = {os.path.splitext(f)[0] for f in os.listdir(docs_dir) if f.endswith((".md", ".html"))}

        def _delink(m):
            if m.group(2) in pub:
                return m.group(0)
            unresolved_links.append(m.group(2))
            return m.group(1)

        unresolved_links: list[str] = []
        text = LINK.sub(_delink, text)
        if unresolved_links:
            print("  de-linked %d target(s) not served here: %s"
                  % (len(unresolved_links), ", ".join(sorted(set(unresolved_links)))))

    return text, unresolved


LINK = re.compile(r"\[([^\]]+)\]\(([A-Za-z0-9_.-]+)\.(md|html)\)")
TABLE_HDR = re.compile(r"^\s*\|[^|]*\|")          # any table row, header or data
TABLE_SEP = re.compile(r"^\s*\|[\s:-]+\|[\s:|-]*$")
HEADING = re.compile(r"^(#{2,4})\s")


def published_docs(repo_docs: str) -> set:
    """Doc stems that actually exist in the published tree — .md OR .html.

    ⚠ BOTH extensions, and this is not pedantry. Built from .md alone, the first version of this
    filter dropped SENTINEL_PROCESS_ATLAS, SENTINEL_ARCHITECTURE_MAP and SENTINEL_RUNTIME_TOPOLOGY,
    which ship as hand-authored HTML with no markdown source. Three front-door links would have
    vanished from the index on the grounds that the pages "do not exist".
    """
    return {os.path.splitext(f)[0] for f in os.listdir(repo_docs)
            if f.endswith((".md", ".html"))}


def filter_index(text: str, pub: set) -> tuple[str, dict]:
    r"""Reduce the canonical docs index to the docs that are actually published.

    WHY THIS EXISTS, and why the rule is "published" rather than "not secret".
    The canonical index lists every doc in Docs\ — 85 linking rows. Published verbatim, 49 of them
    point at files that are not in this repo. Only 7 are withheld for secrecy (the infra docs); the
    other 42 are ordinary public docs that simply have not shipped. So the dominant failure is not
    disclosure, it is a front door where half the links 404.

    Keying the filter on the PUBLISHED SET makes both problems the same problem, and makes it
    fail safe: a doc that is new, private, or merely unfinished is absent from this repo, so it is
    excluded by default. A denylist keyed on zones.conf would need editing every time a private doc
    is added — and would still have shipped all 42 dead links.

    Three cases, and the mixed one is why this is not a one-liner:
      * every link target unpublished  -> drop the row (it is entirely about absent docs)
      * some published, some not       -> DE-LINK the absent ones, keep the row. Dropping it would
                                          lose a published doc's only listing.
      * a section left with no rows    -> drop the heading and its table header too. A bare
                                          "### Infrastructure & ops" heading over an empty table
                                          advertises the withheld content by name, which is the
                                          disclosure this filter exists to prevent.
    """
    lines = text.splitlines(keepends=True)
    out, stats = [], {"rows_kept": 0, "rows_dropped": 0, "delinked": 0, "sections_dropped": 0}

    for line in lines:
        targets = LINK.findall(line)
        if targets and (line.lstrip().startswith("|") or line.lstrip().startswith("-")):
            stems = [t[1] for t in targets]
            if not any(s in pub for s in stems):
                stats["rows_dropped"] += 1
                continue
            if not all(s in pub for s in stems):
                def delink(m):
                    if m.group(2) in pub:
                        return m.group(0)
                    stats["delinked"] += 1
                    return m.group(1)
                line = LINK.sub(delink, line)
            stats["rows_kept"] += 1
        out.append(line)

    # SAFETY NET — de-link any surviving link to an unpublished doc, anywhere.
    # ⚠ The row pass above only inspects lines that START with | or -, and the first run of this
    # filter shipped a dead SENTINEL_BOUNDARY_INVENTORY link sitting on the CONTINUATION line of a
    # multi-line bullet. A row-shaped filter cannot see a row that wraps. This sweep is
    # structure-blind on purpose, so prose links are covered too.
    def sweep(m):
        if m.group(2) in pub:
            return m.group(0)
        stats["delinked"] += 1
        return m.group(1)

    out = [LINK.sub(sweep, ln) for ln in out]

    # second pass: drop headings whose section no longer has a single data row
    pruned, i = [], 0
    while i < len(out):
        line = out[i]
        m = HEADING.match(line)
        if m:
            j = i + 1
            has_row = False
            while j < len(out) and not HEADING.match(out[j]):
                s = out[j]
                if TABLE_HDR.match(s) and not TABLE_SEP.match(s) and "| doc | status |" not in s:
                    has_row = True
                    break
                if s.strip() and not TABLE_HDR.match(s) and not s.strip().startswith("|"):
                    has_row = True     # prose section, not a filtered table — always keep
                    break
                j += 1
            if not has_row:
                stats["sections_dropped"] += 1
                i = j
                continue
        pruned.append(line)
        i += 1

    # Say so. An index silently reduced to a subset reads as the whole map, and a reader who
    # cannot see that rows were removed has no way to ask for the ones that were.
    body = "".join(pruned)
    banner = ("> ℹ **This is the published subset.** The canonical index lists every document in the\n"
              "> working tree; rows pointing at documents that are not part of this repository have been\n"
              "> filtered out, so every link here resolves. Nothing is hidden by omission that is not\n"
              "> simply unpublished.\n\n")
    m = re.search(r"^# .*\n", body, re.M)
    if m:
        body = body[:m.end()] + "\n" + banner + body[m.end():].lstrip("\n")
    return body, stats


def check_renderers() -> int:
    r"""Verify every published .html still matches the renderer tools\renderers.conf declares.

    The two renderers emit identical chrome, so the only reliable discriminator is a body marker:
    render_doc emits <h1 id="…"> (python-markdown's toc extension), md2atlas emits a bare <h1>.
    A page whose marker stops matching its declaration means someone re-rendered it with the other
    tool — which rewrites the entire body and buries any real change in thousands of cosmetic lines.
    Catching that here is cheaper than catching it in a diff.
    """
    docs = os.path.join(REPO, "docs")
    conf = os.path.join(HERE, "renderers.conf")
    declared = {}
    with open(conf, encoding="utf-8") as f:
        for line in f:
            line = line.split("#")[0].strip()
            if line:
                stem, r = line.split()
                declared[stem] = r

    rc, checked = 0, 0
    for fn in sorted(os.listdir(docs)):
        if not fn.endswith(".html"):
            continue
        stem = fn[:-5]
        want = declared.get(stem)
        if want is None:
            print("  ⚠ %s — published but NOT DECLARED in renderers.conf" % fn)
            rc = 1
            continue
        has_md = os.path.exists(os.path.join(docs, stem + ".md"))
        with open(os.path.join(docs, fn), encoding="utf-8") as f:
            body = f.read()
        if want == "handwritten":
            if has_md:
                print("  ⚠ %s — declared handwritten but a .md source exists" % fn)
                rc = 1
            checked += 1
            continue
        got = "render_doc" if "<h1 id=" in body else "md2atlas"
        checked += 1
        if got != want:
            print("  ✗ %s — declared %s, looks like %s" % (fn, want, got))
            rc = 1
    print("  renderers: %d pages checked, %d declared%s"
          % (checked, len(declared), "" if rc == 0 else "  — MISMATCHES ABOVE"))
    return rc


def deliberate_names() -> dict:
    r"""check_parity's DELIBERATE registry — files whose published copy is INTENTIONALLY not local.

    Read from check_parity rather than duplicated, because a second copy of this list is exactly
    the "one rule, N implementations, N-1 have it" failure this repo spent 2026-08-08 removing.
    """
    try:
        import importlib.util
        p = os.path.join(HERE, "check_parity.py")
        spec = importlib.util.spec_from_file_location("_cp", p)
        m = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(m)
        return dict(getattr(m, "DELIBERATE", {}))
    except Exception:
        return {}


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--local", required=True, help="path to bin\\Custom")
    ap.add_argument("--check", action="store_true", help="report only, write nothing")
    ap.add_argument("--index", action="store_true",
                    help="treat the doc as the docs INDEX: drop rows pointing at unpublished docs")
    ap.add_argument("--force", action="store_true",
                    help="publish even a file registered as a DELIBERATE divergence")
    ap.add_argument("--check-renderers", action="store_true",
                    help="verify every published .html matches tools/renderers.conf; publishes nothing")
    ap.add_argument("docs", nargs="*")
    a = ap.parse_args()

    if a.check_renderers:
        return check_renderers()
    if not a.docs:
        ap.error("give at least one doc, or use --check-renderers")

    facts = load_facts(a.local)
    registry = deliberate_names()
    rc = 0
    for name in a.docs:
        # ⛔ EDITORIAL CUTS ARE PROTECTED BY NOTHING ELSE — REFUSE TO OVERWRITE ONE (2026-08-08).
        # A doc registered as DELIBERATE has a published copy a human deliberately made DIFFERENT:
        # SENTINEL_DATA_PLATFORM_SPEC withholds §15 because it documents an unshipped tool and
        # names internal hosts. Republishing regenerates from the canonical copy and silently puts
        # it back. That happened today, and — the part that matters — THE SECRET SCANNER PASSED IT:
        # the restored section's hits are nicknames, which are REVIEW severity, not BLOCK. The
        # editorial cut had no mechanical protection at all; the registry only ever REPORTED it.
        # ⚠ Registered ≠ unpublishable. It means the publish must be a deliberate act, so this
        # refuses by default and names the flag rather than warning into a scrollback nobody reads.
        if name in registry and not a.force:
            sys.stderr.write(
                "publish_doc: %s is registered as a DELIBERATE divergence — refusing.\n"
                "  Its published copy differs from local ON PURPOSE:\n"
                "    %s\n"
                "  Republishing would overwrite that. If the cut is genuinely obsolete, remove the\n"
                "  entry from check_parity.DELIBERATE first, then:  --force\n"
                % (name, registry[name][1][:400]))
            rc = 1
            continue
        src = os.path.join(a.local, "Docs", name)
        dst = os.path.join(REPO, "docs", name)
        if not os.path.exists(src):
            print("  MISSING canonical: %s" % src)
            rc = 1
            continue
        with open(src, encoding="utf-8") as f:
            out, unresolved = render(f.read(), facts, delink=not a.index)
        if a.index:
            out, st = filter_index(out, published_docs(os.path.join(REPO, "docs")))
            print("  index filter: %d rows kept · %d dropped · %d links de-linked · %d empty sections dropped"
                  % (st["rows_kept"], st["rows_dropped"], st["delinked"], st["sections_dropped"]))
            out, _ = render(out, facts, delink=True)   # de-link AFTER filtering, never before
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
