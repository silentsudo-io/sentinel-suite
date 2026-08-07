---
layout: sentinel-ref
title: "probe.py"
blurb: "Lab (Python) · unversioned · 577 lines"
---

# probe.py

> `Sentinel/Lab/health/probe.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 577 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | [SENTINEL_DATA_PLATFORM_SPEC](../../SENTINEL_DATA_PLATFORM_SPEC.md) |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
Sentinel HEALTH probe — samples the live health of NinjaTrader + the Sentinel suite into
Sentinel\\Lab\\db\\sentinel.db, where Grafana's SQLite datasource charts it.

READ-ONLY on the trading process. It samples files/process/ports only — state.json (the
StateService heartbeat), sentinel.log (the event stream), the Ledger, the trades DB, and OS
process/port state. It NEVER touches NinjaTrader internals or orders, so a crash here can never
affect trading (same discipline as the ingester, which OWNS the DB).

    python probe.py            # one sample, then exit
    python probe.py --watch    # sample every INTERVAL seconds forever (self-healing loop)
    python probe.py --init     # create/migrate the health schema only

Single-instance: binds 127.0.0.1:8502 on start; a second copy exits immediately. Feeds the
"Sentinel · Health" Grafana board. Spec: bin\\Custom\\Docs\\SENTINEL_DATA_PLATFORM_SPEC.md.
```

