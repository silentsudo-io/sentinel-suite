---
layout: sentinel-ref
title: "verify_firepx.py"
blurb: "Lab (Python) · unversioned · 168 lines"
---

# verify_firepx.py

> `Sentinel/Lab/verify_firepx.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 168 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
ACCEPTANCE TEST for the honest-entry-price fix (recorder v2.2.0 / v1.2.0, schema 1.5 / cand.2).

    cd "Sentinel\\Lab"
    .\\.venv\\Scripts\\python.exe verify_firepx.py

WHY THIS EXISTS
  Until 2026-07-22 the recorders set `FirePx = Close[0]`, which on every Sentinel bars type is the HEIKIN-ASHI
  SYNTHETIC close -- a price that NEVER TRADED -- while the tick path was always the real tape. FirePx is the
  reference for MFE / MAE / barrier / firstTouch, so every label in the corpus was ~9 ticks optimistic
  (recorded "target-first" 52.3% vs 21.1% TRUE; labels disagreed with truth on 44.6% of fires).

  The fix is only real if a FRESH sidecar's `firePx` reconciles to the first traded price. That is a
  measurement, not an opinion -- so it is a script, not a checklist item. Compiling clean proves nothing here.

PASS CRITERIA (per bar type, on ctick.4 sidecars only)
  1. median |firePx - px[0]| <= 1 tick          -- the entry is a real price
  2. mean dir*(firePx - px[0]) within +/-1 tick -- no systematic directional offset (the HA fingerprint is gone)
  3. pxSrc is a REAL-PRICE source on >= 99% of fires ("last" or "firsttick"; "barclose" = fallback, not tradeable)
Reference (pre-fix, GC TBars, n=3710): mean -9.36t, |median| 8t, 79.7% adverse. Anything resembling that = FAIL.

!! CRITERION 1/2 ARE TAUTOLOGICAL FOR pxSrc="firsttick" ROWS (recorder >= v2.2.1 adopts the ms==0 tick as the
   entry, so firePx == px[0] BY CONSTRUCTION). They still bite on "last" rows. The test that does NOT go
   circular is the DIRECTIONAL SIGN MIX reported below: the pre-fix defect was systematic adversity in BOTH
   directions (the Heikin-Ashi fingerprint), so a lane whose "last"-sourced rows are ~50/50 long-vs-short
   adverse is genuinely fixed, whereas one skewed >75% adverse is not -- no matter what the mean says.
```

