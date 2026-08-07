# explorer.py

> `Sentinel/Lab/viz/explorer.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 163 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | [SENTINEL_AZIMUTH_SPEC](../../SENTINEL_AZIMUTH_SPEC.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
Sentinel Trade Explorer — filterable Plotly view over the SQLite tick corpus.

    cd Sentinel\\Lab
    .\\.venv\\Scripts\\streamlit run viz\\explorer.py     → http://localhost:8501

Reads db\\sentinel.db (populated by ingest\\ingest.py). Filter the whole corpus in the sidebar,
scan the blotter + MFE/MAE scatter, then drop into any trade's tick-by-tick PATH.
The path chart's y-axis is FAVORABLE EXCURSION (ticks) — entry = 0 — so early-vs-late jumps out.
Spec: bin\\Custom\\Docs\\SENTINEL_DATA_PLATFORM_SPEC.md.
```

