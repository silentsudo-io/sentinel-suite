# facts.py

> `Sentinel/Lab/docs/facts.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 99 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | [SENTINEL_DOCS_HEALTH_SPEC](../../SENTINEL_DOCS_HEALTH_SPEC.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
Compute ground-truth FACTS about the live Sentinel code -> Docs\_generated\facts.json.

Docs reference these via {{tokens}}; the renderer (md2atlas) substitutes them at render time, so
volatile numbers (Core version, voter count) can NEVER drift — they're single-sourced from code.
STATIC-CODE truth: greps the .cs files, needs no NT running, fully deterministic.

    python facts.py            # write facts.json, print a summary
    python facts.py --print    # print facts.json to stdout, don't write

Part of the Docs-Health system — spec: bin\Custom\Docs\SENTINEL_DOCS_HEALTH_SPEC.md.
```

