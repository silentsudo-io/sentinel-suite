---
layout: sentinel-ref
title: "replay_sync.py"
blurb: "Lab (Python) · unversioned · 105 lines"
---

# replay_sync.py

> `Sentinel/Lab/sync/replay_sync.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 105 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
replay_sync — keep a Sentinel REPLAY/BAKE node's bin\\Custom in sync with the main suite.

Rule-based (NOT a hand-curated list, so it auto-includes new Sentinel files):
  a .cs is in the BAKE SET iff  (basename starts 'Sentinel'  OR  namespace ...*.Sentinel  OR  is a brain file)
  MINUS the exclusions below (bake nodes never trade; a couple of files couple to a strategy).

Two modes (stdlib only):
  pack  <out.tar> [--with-strategies A.cs,B.cs]   # on the MAIN box: classify + tar the bake set
                    Strategies are EXCLUDED by default (nodes record, they do not trade).
                    Name them to ship one — e.g. a strategy that IS the measurement (Keel).
  apply <in.tar>    # on the NODE:     back up current Sentinel files, then extract (overwrite)

Typical refresh (from the main box, over Tailscale):
  py replay_sync.py pack  bakeset.tar
  scp -q bakeset.tar worker1:/C:/Users/Administrator/Downloads/bakeset.tar
  scp -q replay_sync.py worker1:/C:/Users/Administrator/Downloads/replay_sync.py
  ssh worker1 "C:\\ntbv\\Scripts\\python.exe C:\\Users\\Administrator\\Downloads\\replay_sync.py apply C:\\Users\\Administrator\\Downloads\\bakeset.tar"
  ssh worker1 "C:\\ntbv\\Scripts\\python.exe -m nt8bridge compile --type indicator"   # then a real F5 on the node to LOAD
```

