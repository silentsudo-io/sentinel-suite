---
layout: sentinel-ref
title: "coverage.py"
blurb: "Lab (Python) · unversioned · 320 lines"
---

# coverage.py

> `Sentinel/Lab/docs/coverage.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 320 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
Sentinel COVERAGE — the missing half of docs-health (code -> doc).

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
```

