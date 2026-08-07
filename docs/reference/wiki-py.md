---
layout: sentinel-ref
title: "wiki.py"
blurb: "Lab (Python) · unversioned · 313 lines"
---

# wiki.py

> `Sentinel/Lab/docs/wiki.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 313 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
Sentinel WIKI — generate the per-artifact reference layer from the code itself.

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
```

