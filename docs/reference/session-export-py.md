# session_export.py

> `Sentinel/Lab/sync/session_export.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 104 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
session_export - copy a Claude session transcript somewhere durable, plus a readable render.

WHY THIS EXISTS: the authoritative transcript lives under
`%USERPROFILE%\\.claude\\projects\\<project>\\<session-id>.jsonl`, which is subject to periodic cleanup
(there is a `.last-cleanup` marker beside it). A long working session can be the only record of decisions
that never made it into a doc. On 2026-07-31 a session was killed mid-tool-call by a VS Code extension-host
restart and had to be recovered from exactly this file - see the `continue-here-2026-07-31-now-chat-recovery`
memory. Copying it out is cheap; losing it is not.

usage:
    python sync/session_export.py <session-id> [label]
    python sync/session_export.py --latest [label]

Writes to Sentinel\\_sessions\\<date>_<label>_<shortid>_raw.jsonl  +  ..._readable.md
Read-only with respect to the source. Safe to re-run: a later run overwrites with a fresher snapshot.
```

