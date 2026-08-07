# bartag.py

> `Sentinel/Lab/sentinel_lab/bartag.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 121 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
Human-readable bar-type labels — the PYTHON MIRROR of SentinelCore.FriendlyBartag / BartypeName
(bin\\Custom\\AddOns\\SentinelCore_v1_0_0.cs, v1.35.0).

DISPLAY ONLY. The raw bartag ("212203v8") stays the machine key everywhere it matters — the DB
`bartype` column, the corpus filename, the scope join key, and every Streamlit filter VALUE. These
helpers only change what a HUMAN sees (a picker label, a metric). Use them as a Streamlit
`format_func=` so the option values stay raw while the labels read "SentinelFlux 8".

⚠ KEEP IN SYNC with the C# registry (SentinelCore `_bartypeNames`) so the label a human sees in the
explorer matches the `barLabel` the recorder stamps into schema-1.4 rows and the on-chart cards.
```

