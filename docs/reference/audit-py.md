# audit.py

> `Sentinel/Lab/docs/audit.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 459 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | [SENTINEL_DOCS_HEALTH_SPEC](../../SENTINEL_DOCS_HEALTH_SPEC.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
Docs-HEALTH audit probe — checks Sentinel docs (bin\Custom\Docs) + the memory dir for DRIFT and
writes findings into Sentinel\Lab\db\sentinel.db, where the "Sentinel · Docs" Grafana board charts them.

STATIC + READ-ONLY: it only reads files (docs + the .cs/config they reference) — never edits a doc, never
touches NT. Ground truth is static code, so it's deterministic and needs nothing running.

    python audit.py            # one scan, print summary
    python audit.py --watch    # scan every INTERVAL s forever (self-healing loop, guards :8505)
    python audit.py --loop N    # scan every N s
    python audit.py --init      # schema only

Spec: bin\Custom\Docs\SENTINEL_DOCS_HEALTH_SPEC.md. Facts come from facts.py (Docs\_generated\facts.json).
```

