---
layout: sentinel-ref
title: "secretscan.py"
blurb: "Lab (Python) · unversioned · 312 lines"
---

# secretscan.py

> `Sentinel/Lab/docs/secretscan.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 312 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
Sentinel PUBLISH GATE — refuse to ship the operator's network.

WHAT THIS IS FOR. The suite is being released in full. The code is clean -- measured
2026-08-07, the 92 C# files carry ZERO domains, ZERO IPs and ZERO credentials, and the only
matches are a machine nickname in changelog prose. The exposure is concentrated in
infrastructure docs and Lab\infra\, which describe a private rack. This gate exists so that
stays true as the tree grows, and so a publish that would break it FAILS LOUDLY instead of
succeeding quietly.

⚠ THIS IS THE SECOND LOCK, NOT THE FIRST. Publishing happens exactly one way: a human copies
a file into the public repo's `src/`. That allowlist is the control. A denylist scanner can
only ever catch patterns someone thought to write down, so it must never be the thing standing
between a secret and the internet -- it is the backstop that catches the case where the
editorial decision was made wrongly.

SEVERITIES
    BLOCK   a live domain / address / credential / fleet hostname. In a PUBLIC-zone file this
            is a hard failure and exit 1.
    REVIEW  something that is usually fine and occasionally is not (a machine nickname, an
            absolute user path, an email). Reported, never fatal -- a gate that cries wolf
            gets switched off, and this project has written that lesson down four times.

    python secretscan.py                          # scan the default release set
    python secretscan.py --gate <dir>             # gate one tree; exit 1 on any PUBLIC block
    python secretscan.py --zone PUBLIC --json x   # machine-readable

Zones come from zones.conf so the manifest has ONE home. Spec: SENTINEL_DOCS_HEALTH_SPEC.md.
```

