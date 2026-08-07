# conductor_arm.py

> `Sentinel/Lab/conductor_arm.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 226 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
conductor_arm — deliberately authorise ONE unattended Conductor cold start, per node.

WHY THIS EXISTS
---------------
`autostart = true` in Run.conf used to mean "run on EVERY login, forever". On 2026-08-02 that fired
a cell nobody asked for on legacy-node: 144 minutes at 100x, no strategy loaded, zero corpus rows, and
nothing complained. Conductor v0.2.0 splits the two cases that both looked like autostart --
a RESUME of an in-flight bake still starts on its own, but a COLD START now needs a token that
EXPIRES and is CONSUMED on use. This writes that token.

    python conductor_arm.py status                      # who is armed, and what for
    python conductor_arm.py arm --node worker-1
    python conductor_arm.py arm --node all --ttl 6
    python conductor_arm.py disarm --node worker-3

WHAT MAKES THIS AN INTENT AND NOT ANOTHER STANDING FLAG
-------------------------------------------------------
  * it EXPIRES (`ttlHours`, default 12) -- an arm you forgot about stops being permission;
  * it is CONSUMED -- the Conductor renames it on use, so it cannot authorise the next restart;
  * it PINS THE MANIFEST -- the token carries a fingerprint of Run.conf's job lines, so editing
    what actually runs invalidates the arm. (Editing `heartbeatSec` does not, deliberately.)

⚠ Arming a node whose chart is not built is still a way to bake junk -- the token authorises, it
does not verify. That is what the productivity gate is for: it aborts a run that writes nothing.
Belt and braces, on purpose, because they fail differently.
```

