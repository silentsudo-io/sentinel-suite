# muster.py

> `Sentinel/Lab/sync/muster.py`

| | |
|---|---|
| **Family** | Lab (Python) |
| **Version** | — |
| **Size** | 575 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
muster — keep the Watch running the SAME CODE, and be able to prove it.

WHY THIS EXISTS
---------------
Gate 3 asks whether a sentry reproduces legacy-node's replay cell trade-for-trade. That question is
meaningless unless you can first prove both machines ran identical code. "I synced it last week"
is a belief; a manifest hash is evidence. So the VERIFY half of this tool matters more than the
push half, and is why `verify` is the default subcommand.

    python muster.py status                    # who is up, what NT, what tree, is it baking
    python muster.py verify                     # every sentry vs legacy-node -- exact differing files
    python muster.py verify --ref worker-1
    python muster.py push --from main --to worker-3        # one-way, guarded, then re-verified
    python muster.py push --to all --dry-run

THE RULES IT INHERITS FROM corpus_pull
--------------------------------------
  * ONE-WAY. The source is the source; a sentry never pushes back.
  * REFUSES TO SYNC INTO A LIVE BAKE, per node, loudly. "Never fire a reload into a live bake"
    already cost one quarantined cell. --force exists but says what it is overriding.
  * VERIFY ON THE FAR SIDE. path + size + sha256 compared on both ends, after the copy.
  * THEN VERIFY IT COMPILES. One broken .cs stops the whole assembly, so a synced tree that does
    not compile is worse than a stale one that does.
  * NEVER COPY NODE-LOCAL STATE: the built DLL/PDB, the csproj NT rewrites, Run.conf, Excursions.
  * PER-NODE REPORTING. One node failing never aborts the fleet.
  * NEVER DELETES on the target without --prune, which is opt-in and reported file by file.

⚠ WHAT THIS DOES NOT DO: it does not decide what SHOULD be in the tree. The workers carry the
minimal Sentinel-only carve taken from legacy-node. Pushing `--from main` would also bring main's newer
files (Council v1.11.0, Bridge v0.2.0, ...) for exactly the files that already exist on the target;
NEW files are listed as additions and require --allow-new. That is deliberate: a silent addition is
how a Gate 3 baseline stops meaning anything.
```

