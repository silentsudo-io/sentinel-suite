#!/usr/bin/env python
r"""render_doc - Markdown -> themed HTML sibling, reusing the suite's existing doc chrome.

WHY: docs-health flags a .md whose .html sibling is missing or older ("stale_html"), and the
standing rule is that every Docs\*.md gets a theme-aware .html twin. Hand-authoring that chrome
per doc is how siblings drift; this lifts the <head>/wrapper/footer from an existing rendered doc
so every new page is byte-identical in styling to the ones already shipped.

usage:  python docs/render_doc.py <name.md> [more.md ...]
        (paths relative to bin\Custom\Docs, or absolute)
"""
import html, io, os, re, sys, markdown

# BUS FACTOR: a published tool pinned to one home directory is one a contributor cannot run.
# $SENTINEL_CUSTOM > walk up from this file > the historical default.
def _docs_dir():
    env = os.environ.get("SENTINEL_CUSTOM")
    if env and os.path.isdir(os.path.join(env, "Docs")):
        return os.path.join(env, "Docs")
    here = os.path.dirname(os.path.abspath(__file__))
    guess = os.path.abspath(os.path.join(here, "..", "..", "..", "bin", "Custom", "Docs"))
    if os.path.isdir(guess):
        return guess
    return 'C:\\Users\\Administrator\\Documents\\NinjaTrader 8\\bin\\Custom\\Docs'


DOCS = _docs_dir()
TEMPLATE = os.path.join(DOCS, "SENTINEL_CONDUCTOR_SPEC.html")


def chrome():
    s = io.open(TEMPLATE, encoding="utf-8").read()
    i = s.index("</head><body>") + len("</head><body>")
    j = s.index("<footer>")
    # everything between <body> and the first heading is the wrapper open (e.g. <div class="wrap">)
    m = re.search(r"<h1", s[i:j])
    head, wrap_open = s[:i], s[i:i + m.start()]
    tail = s[j:]
    return head, wrap_open, tail


def render(md_path):
    if not os.path.isabs(md_path):
        md_path = os.path.join(DOCS, md_path)
    src = io.open(md_path, encoding="utf-8").read()
    title_m = re.search(r"^#\s+(.+)$", src, re.M)
    title = title_m.group(1).strip() if title_m else os.path.basename(md_path)
    body = markdown.markdown(src, extensions=["tables", "fenced_code", "toc", "sane_lists", "attr_list"])
    head, wrap_open, tail = chrome()
    # The title is the doc's H1 — arbitrary author text landing inside markup, so it needs escaping:
    # an `&` produced a raw `&` in <title> (invalid), and a `<` would have truncated the tag outright.
    # ⛔ And the REPLACEMENT is passed through re.sub, where a backslash is a group reference — in a
    # tree whose docs routinely title themselves after Windows paths (`bin\Custom`), `\C` is a bad
    # escape and `\1` would splice in a capture group. A lambda makes the replacement literal.
    safe = html.escape(title, quote=True)
    head = re.sub(r"<title>.*?</title>", lambda _m: f"<title>Sentinel · {safe}</title>", head, flags=re.S)
    tail = re.sub(r"<footer><span>.*?</span><span>",
                  lambda _m: f"<footer><span>Sentinel Suite · <b>{safe}</b></span><span>", tail, flags=re.S)
    out = md_path[:-3] + ".html"
    io.open(out, "w", encoding="utf-8", newline="\n").write(head + wrap_open + body + "\n" + tail)
    print(f"rendered {os.path.basename(out)}  ({len(body):,} bytes of body)")


if __name__ == "__main__":
    # ⚠ BUS FACTOR — say WHICH knob fixes it, the same way version_check.py does. _docs_dir()'s last
    # resort is one operator's home directory, so on any other machine this silently resolved to a
    # path that does not exist and then died inside chrome() with a bare FileNotFoundError naming a
    # template — a stack trace that tells a contributor nothing about SENTINEL_CUSTOM. A default is
    # allowed to be wrong; it is not allowed to be wrong QUIETLY.
    if not os.path.isdir(DOCS):
        print("render_doc: no Docs directory at %s\n"
              "  Set SENTINEL_CUSTOM to your bin\\Custom path." % DOCS)
        sys.exit(2)
    if not os.path.isfile(TEMPLATE):
        print("render_doc: chrome template missing: %s\n"
              "  This tool lifts <head>/wrapper/footer from an already-rendered doc; without it\n"
              "  there is no styling to copy. Point SENTINEL_CUSTOM at a tree that has it." % TEMPLATE)
        sys.exit(2)
    for a in sys.argv[1:]:
        render(a)
