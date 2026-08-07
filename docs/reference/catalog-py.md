---
layout: sentinel-ref
title: "catalog.py"
blurb: "Lab (Python) · unversioned · 227 lines"
---

# catalog.py

> `Sentinel/Lab/quartermaster/catalog.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 227 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
Quartermaster catalog core — walk db\\replay (filenames only), verify, and roll coverage.

The dates are IN the filename (YYYYMMDD.nrd), so cataloging never opens a .nrd binary — a full
scan of a 100+ GB db\\replay is a filename/stat sweep, seconds not minutes. THIS owns catalog.db;
it never touches the .nrd files, NT, or the corpus (sentinel.db) — pure read of the shelf.

Instrument-folder grammar (NT writes db\\replay\\<Instrument.FullName>\\YYYYMMDD.nrd):
    "GC 02-26"           -> expiry     symbol=GC  contract="02-26"
    "NQ H26"             -> expiry     symbol=NQ  contract="H26"   (letter-code)
    "YM 2026 Continuous" -> continuous symbol=YM  contract="2026 Continuous"
    "ES ##-##"           -> other      symbol=ES  contract="##-##" (NT placeholder folder)
```

