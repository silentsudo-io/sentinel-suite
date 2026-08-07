---
layout: sentinel-ref
title: "render_doc.py"
blurb: "Lab (Python) · unversioned · 47 lines"
---

# render_doc.py

> `Sentinel/Lab/docs/render_doc.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 47 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
render_doc - Markdown -> themed HTML sibling, reusing the suite's existing doc chrome.

WHY: docs-health flags a .md whose .html sibling is missing or older ("stale_html"), and the
standing rule is that every Docs\*.md gets a theme-aware .html twin. Hand-authoring that chrome
per doc is how siblings drift; this lifts the <head>/wrapper/footer from an existing rendered doc
so every new page is byte-identical in styling to the ones already shipped.

usage:  python docs/render_doc.py <name.md> [more.md ...]
        (paths relative to bin\Custom\Docs, or absolute)
```

