#!/usr/bin/env python
r"""render_doc - Markdown -> themed HTML sibling, reusing the suite's existing doc chrome.

WHY: docs-health flags a .md whose .html sibling is missing or older ("stale_html"), and the
standing rule is that every Docs\*.md gets a theme-aware .html twin. Hand-authoring that chrome
per doc is how siblings drift; this lifts the <head>/wrapper/footer from an existing rendered doc
so every new page is byte-identical in styling to the ones already shipped.

usage:  python docs/render_doc.py <name.md> [more.md ...]
        (paths relative to bin\Custom\Docs, or absolute)
"""
import io, os, re, sys, markdown

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
    head = re.sub(r"<title>.*?</title>", f"<title>Sentinel · {title}</title>", head, flags=re.S)
    tail = re.sub(r"<footer><span>.*?</span><span>", f"<footer><span>Sentinel Suite · <b>{title}</b></span><span>", tail, flags=re.S)
    out = md_path[:-3] + ".html"
    io.open(out, "w", encoding="utf-8", newline="\n").write(head + wrap_open + body + "\n" + tail)
    print(f"rendered {os.path.basename(out)}  ({len(body):,} bytes of body)")


if __name__ == "__main__":
    for a in sys.argv[1:]:
        render(a)
