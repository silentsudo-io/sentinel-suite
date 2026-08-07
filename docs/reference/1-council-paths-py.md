# 1_Council_Paths.py

> `Sentinel/Lab/viz/pages/1_Council_Paths.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 168 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | [SENTINEL_AZIMUTH_SPEC](../../SENTINEL_AZIMUTH_SPEC.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
Council PATH-QUALITY — Streamlit page (a new tab in the :8501 explorer app).

Renders the same analysis as the CLI `council_paths.py` (imported, so the numbers can never drift):
does higher CONVICTION buy better-SHAPED paths? Per scope — never pools bartypes. Interactive:
pick a scope, see the conviction-vs-shape scatter + bucket table + per-voter table, click a fire
to draw its raw tick path. Reads Sentinel\\Lab\\db\\sentinel.db (source='council').
```

