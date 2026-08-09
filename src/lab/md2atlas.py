# -*- coding: utf-8 -*-
"""Render a Markdown doc into the Sentinel 'Field Manual' (Atlas) house style.
Single source of truth for styling = SENTINEL_PROCESS_ATLAS.html (its <style> + helm SVG are extracted).
Usage: python md2atlas.py FILE.md [FILE2.md ...]
"""
import io, re, sys, html as _html, os, json
try:                                   # Windows console is cp1252 — a '→' in a title else crashes the print
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

DOCS = r"c:/Users/Administrator/Documents/NinjaTrader 8/bin/Custom/Docs"
ATLAS = DOCS + "/SENTINEL_PROCESS_ATLAS.html"

def extract_atlas():
    t = io.open(ATLAS, encoding="utf-8").read()
    style = re.search(r"<style>.*?</style>", t, re.S).group(0)
    helm  = re.search(r'<svg width="0".*?</svg>', t, re.S).group(0)
    water = re.search(r'<svg class="watermark".*?</svg>', t, re.S).group(0)
    return style, helm, water

# generic prose rules layered on the Atlas tokens (the Atlas styles mostly via classes;
# bare markdown elements need these). Uses the same --vars so light/dark just work.
PROSE = """<style>
  .wrap{max-width:900px}
  .kicker .navsep{opacity:.45;margin:0 3px}
  .kicker a.navhome{color:var(--ink2);border-bottom:0;text-decoration:none}
  .kicker a.navhome:hover{color:var(--accent)}
  .doc h2{font-family:var(--mono);font-weight:700;font-size:15px;letter-spacing:.2em;text-transform:uppercase;
    color:var(--ink);margin:64px 0 4px;padding-bottom:12px;border-bottom:1px solid var(--line)}
  .doc h2 .hn{color:var(--accent);margin-right:12px}
  .doc h3{margin:34px 0 8px}
  .doc p{font-size:15.5px;color:var(--ink);margin:0 0 15px;max-width:78ch}
  .doc a{color:var(--accent);text-decoration:none;border-bottom:1px solid color-mix(in srgb,var(--accent) 40%,transparent)}
  .doc a:hover{border-bottom-color:var(--accent)}
  .doc strong{color:var(--ink);font-weight:670}
  .doc em{color:var(--ink2);font-style:italic}
  .doc ul,.doc ol{max-width:78ch;padding-left:1.3em;margin:0 0 15px}
  .doc li{margin:.42em 0;font-size:15px;color:var(--ink)}
  .doc li::marker{color:var(--accent)}
  .doc hr{border:0;border-top:1px solid var(--line);margin:8px 0}
  .doc blockquote{margin:18px 0;padding:14px 18px;border:1px solid var(--line);border-left:3px solid var(--accent);
    border-radius:0 12px 12px 0;background:var(--surface);color:var(--ink2);max-width:80ch}
  .doc blockquote p{color:var(--ink2);margin:0}
  .doc blockquote strong{color:var(--ink)}
  .doc pre{background:var(--surface2);border:1px solid var(--line);border-radius:10px;padding:14px 16px;
    overflow-x:auto;font-family:var(--mono);font-size:12px;color:var(--ink);line-height:1.6;margin:16px 0;white-space:pre}
  .doc pre code{background:none;border:0;padding:0;font-size:1em;color:inherit}
</style>"""

def inline(s):
    r"""Markdown inline -> HTML.

    ⚠ INLINE CODE IS STASHED BEFORE EMPHASIS RUNS, and that ordering is the whole point.
    Until 2026-08-08 this converted `code` to <code> and then ran the * and ** rules over the
    WHOLE string, contents included — so a literal asterisk inside inline code was treated as an
    emphasis marker and paired with the next one, anywhere on the line. In
    SENTINEL_DATA_PLATFORM_SPEC that turned the glob `Excursions\ticks\*.jsonl` … `council\ticks\*`
    into an <em> span straddling two code spans, producing BOTH a corrupted file path (the
    asterisks vanish) and malformed nesting: <code>…<em>.jsonl</code> … <code>…</em>.jsonl.
    One page on the public site carries it today.

    This is the same failure as publish_doc.py substituting {{tokens}} inside backticks, fixed the
    same morning: a transform that does not protect code will eat code that looks like syntax.
    ⇒ Stash first, transform, restore last. Keep this idiom whenever a rule is added below.
    """
    s = _html.escape(s, quote=False)
    codes = []

    def _stash(m):
        codes.append(m.group(1))
        return "\x01%d\x01" % (len(codes) - 1)

    s = re.sub(r'`([^`]+)`', _stash, s)
    s = re.sub(r'\[([^\]]+)\]\(([^)]+)\)', r'<a href="\2">\1</a>', s)
    s = re.sub(r'\*\*(.+?)\*\*', r'<strong>\1</strong>', s)   # non-greedy so a nested *italic* survives
    s = re.sub(r'(?<!\w)\*([^*]+)\*(?!\w)', r'<em>\1</em>', s)
    s = re.sub(r'\x01(\d+)\x01', lambda m: '<code>%s</code>' % codes[int(m.group(1))], s)
    return s

def split_row(line):
    line = line.strip()
    if line.startswith("|"): line = line[1:]
    if line.endswith("|"): line = line[:-1]
    return [c.strip() for c in line.split("|")]

def convert(md):
    lines = md.split("\n")
    n = len(lines); i = 0
    title = "Sentinel"; lede = ""; out = []
    # pull first H1 -> title, and the first following non-blank paragraph -> lede
    while i < n and lines[i].strip() == "": i += 1
    if i < n:
        m = re.match(r'^#\s+(.*)$', lines[i])
        if m:
            title = m.group(1).strip(); i += 1
            while i < n and lines[i].strip() == "": i += 1
            if i < n and not re.match(r'^(#{1,6}\s|>|\s*[-*]\s|\d+\.\s|```|\||---$)', lines[i]):
                buf = []
                while i < n and lines[i].strip() != "" and not re.match(r'^(#{1,6}\s|>|```|\|)', lines[i]):
                    buf.append(lines[i]); i += 1
                lede = inline(" ".join(buf))
    # smallest body-heading depth (fence-aware) → docs whose sections start at '#'
    # (e.g. the Field Manual) map '#'→h2; '##'-based docs are unchanged (offset 0).
    MINLVL = 6; _fence = False
    for _k in range(i, n):
        _l = lines[_k]
        if _l.startswith("```"): _fence = not _fence; continue
        if _fence: continue
        _hm = re.match(r'^(#{1,6})\s', _l)
        if _hm: MINLVL = min(MINLVL, len(_hm.group(1)))
    if MINLVL == 6: MINLVL = 2
    while i < n:
        ln = lines[i]
        if ln.startswith("```"):
            i += 1; buf = []
            while i < n and not lines[i].startswith("```"):
                buf.append(_html.escape(lines[i], quote=False)); i += 1
            i += 1
            out.append("<pre><code>" + "\n".join(buf) + "</code></pre>"); continue
        # table: a line with | followed by a |---| separator
        if "|" in ln and i + 1 < n and re.match(r'^\s*\|?[\s:|-]+\|[\s:|-]*$', lines[i+1]) and "-" in lines[i+1]:
            head = split_row(ln); i += 2; rows = []
            while i < n and "|" in lines[i] and lines[i].strip():
                rows.append(split_row(lines[i])); i += 1
            th = "".join("<th>" + inline(c) + "</th>" for c in head)
            body = ""
            for r in rows:
                body += "<tr>" + "".join("<td>" + inline(c) + "</td>" for c in r) + "</tr>"
            out.append('<div class="tblwrap"><table><thead><tr>' + th + "</tr></thead><tbody>" + body + "</tbody></table></div>")
            continue
        if ln.strip() == "---":
            out.append("<hr>"); i += 1; continue
        m = re.match(r'^(#{1,6})\s+(.*)$', ln)
        if m:
            lvl = min(6, max(2, len(m.group(1)) - MINLVL + 2)); txt = inline(m.group(2))
            if lvl == 2:
                nm = re.match(r'^\s*(\d+[a-z]?\.)\s+(.*)$', m.group(2))
                if nm: txt = '<span class="hn">' + nm.group(1) + "</span>" + inline(nm.group(2))
            out.append("<h%d>%s</h%d>" % (lvl, txt, lvl)); i += 1; continue
        if ln.startswith(">"):
            buf = []
            while i < n and lines[i].startswith(">"):
                buf.append(re.sub(r'^>\s?', '', lines[i])); i += 1
            out.append("<blockquote><p>" + inline(" ".join(buf)) + "</p></blockquote>"); continue
        mo = re.match(r'^\s*\d+\.\s+', ln)
        if re.match(r'^\s*[-*]\s+', ln) or mo:
            tag = "ol" if mo else "ul"
            out.append("<%s>" % tag)
            BLK = r'^(#{1,6}\s|>|```|\||---$|\s*[-*]\s|\s*\d+\.\s)'
            while i < n:
                if re.match(r'^\s*[-*]\s+', lines[i]) or re.match(r'^\s*\d+\.\s+', lines[i]):
                    item = [re.sub(r'^\s*(?:[-*]|\d+\.)\s+', '', lines[i])]; i += 1
                    # absorb hard-wrapped continuation lines so inline spans (**bold**) aren't split
                    while i < n and lines[i].strip() != "" and not re.match(BLK, lines[i]):
                        item.append(lines[i].strip()); i += 1
                    out.append("<li>" + inline(" ".join(item)) + "</li>")
                elif lines[i].strip() == "" and i + 1 < n and re.match(r'^\s*(?:[-*]|\d+\.)\s+', lines[i+1]):
                    i += 1  # single blank between loose-list items → same list
                else:
                    break
            out.append("</%s>" % tag); continue
        if ln.strip() == "":
            i += 1; continue
        buf = []
        while i < n and lines[i].strip() != "" and not re.match(r'^(#{1,6}\s|>|\s*[-*]\s|\d+\.\s|```|\||---$)', lines[i]):
            buf.append(lines[i]); i += 1
        if not buf:
            # ── SAFETY: never leave this loop without consuming a line (added 2026-07-28) ──
            # We reached the paragraph collector, so lines[i] is non-blank; but it matched the
            # block-opener guard above, so the collector takes nothing. Before this branch existed,
            # `i` never advanced and the OUTER loop re-entered here forever: md2atlas hung with no
            # output, no error and no exit — you could only tell by the missing .html.
            #
            # In practice this is a '|' row whose table header/separator is gone — most often a table
            # SPLIT by something inserted into the middle of it, which orphans every row below the
            # insertion. That is exactly what happened to SENTINEL_DESIGN_SYSTEM.md and it cost real
            # time, partly because a hang gives you nothing to read.
            #
            # Consume it, render it literally so the damage is VISIBLE on the page rather than
            # silently dropped, and warn on stderr so the docs-health probe can surface it.
            sys.stderr.write(
                "md2atlas: WARNING orphan block line %d (malformed table? a row with no header/"
                "separator above it): %s\n" % (i + 1, lines[i].strip()[:100]))
            buf.append(lines[i]); i += 1
        out.append("<p>" + inline(" ".join(buf)) + "</p>")
    return title, lede, "\n".join(out)

STYLE, HELM, WATER = extract_atlas()

# --- Docs-Health integration: strip YAML frontmatter + substitute {{tokens}} from facts.json ---
# (single-sources volatile numbers like Core version / voter count so they can't drift; a dangling
#  token stays visible so the docs-audit probe flags it). Spec: Docs/SENTINEL_DOCS_HEALTH_SPEC.md.
FACTS_PATH = DOCS + "/_generated/facts.json"
try:
    FACTS = json.load(io.open(FACTS_PATH, encoding="utf-8"))
except Exception:
    FACTS = {}
_FM = re.compile(r'^﻿?---\r?\n.*?\r?\n---\r?\n', re.S)

def preprocess(md):
    md = _FM.sub('', md, count=1)                                   # drop a leading frontmatter block
    # protect code (fenced + inline) so a doc can DOCUMENT `{{token}}` literally; only PROSE substitutes
    stash = []
    def hide(m):
        stash.append(m.group(0)); return "\x00%d\x00" % (len(stash) - 1)
    md = re.sub(r'```.*?```|`[^`\n]*`', hide, md, flags=re.S)
    md = re.sub(r'\{\{([a-z0-9_]+)\}\}',
                lambda m: str(FACTS.get(m.group(1), m.group(0))), md)     # {{token}} -> fact (or leave it)
    return re.sub(r'\x00(\d+)\x00', lambda m: stash[int(m.group(1))], md)  # restore protected code

for path in sys.argv[1:]:
    md = preprocess(io.open(path, encoding="utf-8").read())
    title, lede, body = convert(md)
    head = ("<!DOCTYPE html>\n<html lang=\"en\"><head><meta charset=\"utf-8\">"
            "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n"
            "<title>Sentinel · " + _html.escape(title, quote=False) + "</title>\n"
            + STYLE + "\n" + PROSE + "\n</head><body>\n" + HELM + "\n" + WATER + "\n"
            '<div class="wrap doc">\n<header>\n'
            '<p class="kicker"><svg class="brandmark" viewBox="0 0 512 512" aria-hidden="true"><use href="#helm"/></svg>'
            '<span class="livedot"></span> Sentinel Suite · NinjaTrader 8 · living document'
            + ('' if os.path.basename(path) == "SENTINEL_DOCS.md"
               else '<span class="navsep">·</span><a class="navhome" href="SENTINEL_DOCS.html">Docs home →</a>')
            + '</p>\n'
            "<h1>" + _html.escape(title, quote=False) + "</h1>\n"
            + ('<p class="lede">' + lede + "</p>\n" if lede else "")
            + "</header>\n")
    foot = ('\n<footer><span>Sentinel Suite · <b>' + _html.escape(title, quote=False) + "</b></span>"
            "<span>living document · rendered from Markdown</span></footer>\n</div></body></html>\n")
    htmlpath = path[:-3] + ".html"
    io.open(htmlpath, "w", encoding="utf-8", newline="\n").write(head + body + foot)
    print("wrote", os.path.basename(htmlpath), "| title:", title, "| lede:", "yes" if lede else "no")
