#!/usr/bin/env python3
"""replay_sync — keep a Sentinel REPLAY/BAKE node's bin\\Custom in sync with the main suite.

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
"""
import os, re, sys, tarfile
import sys as _sys, os as _os
_LAB_ROOT = _os.path.abspath(_os.path.join(_os.path.dirname(_os.path.abspath(__file__)), ".."))
if _LAB_ROOT not in _sys.path:
    _sys.path.insert(0, _LAB_ROOT)
from lab_faults import swallow

ROOT = r"C:\Users\Administrator\Documents\NinjaTrader 8\bin\Custom"
NS   = re.compile(r"namespace\s+NinjaTrader\.NinjaScript\.[A-Za-z0-9_.]*\.Sentinel", re.I)

# bake nodes RECORD, they never trade → no strategies. Plus two indicators that COMPILE-DEPEND on the
# GodTrades21 strategy (a legacy recorder + an analytics grid) — neither is needed for a Council bake.
SKIP_DIRS  = {"_archive", "_copier_samples", ".git", "obj", "bin", "Strategies"}
SKIP_FILES = {"SentinelExcursionRecorder_v1_4.cs",          # legacy GodTrades-hosting recorder (superseded by _v2_0_0)
              "SentinelAdaptivePerformanceGrid_v1_0_0.cs",  # analytics grid, couples to GodTrades21
              "SentinelDeck_v0_2_5.cs"}                     # manual ORDER-ENTRY panel — same "nodes never trade" rule as Strategies

def is_sentinel(path, base):
    if base in SKIP_FILES: return False
    if base.lower().startswith("sentinel"): return True
    if base in ("Council_v1_0_0.cs", "CouncilFusion.cs"): return True
    try:
        return bool(NS.search(open(path, encoding="utf-8", errors="replace").read(4000)))
    except Exception as _swex:
        swallow("sync.replay_sync.is_sentinel", _swex)
        return False

# ── STRATEGIES: excluded by default, shipped only when NAMED ────────────────────────────────────
# "bake nodes RECORD, they never trade" was written when the only bake was a Council bake, which needs
# indicators and nothing else. The Keel programme broke that premise: the strategy IS the measurement,
# so it has to run on the node. Deleting the exclusion would be the easy fix and the wrong one — it
# would silently ship every order-placing tool in the tree to a machine that runs unattended.
# So the default stays CLOSED and a strategy travels only when someone names it on the command line.
# That keeps the decision explicit, auditable in the shell history, and impossible to reach by accident.
EXTRA_FILES = set()          # populated by --with-strategies

def scan():
    out = []
    for dp, dn, fn in os.walk(ROOT):
        dn[:] = [d for d in dn if d not in SKIP_DIRS]
        for f in fn:
            if f.endswith(".cs") and is_sentinel(os.path.join(dp, f), f):
                full = os.path.join(dp, f)
                out.append((full, os.path.relpath(full, ROOT).replace("\\", "/")))
    for name in sorted(EXTRA_FILES):
        full = os.path.join(ROOT, "Strategies", name)
        if os.path.isfile(full):
            out.append((full, "Strategies/" + name))
        else:
            print(f"  !! --with-strategies: NOT FOUND, skipped: {full}")
    return out

def do_pack(out_tar):
    files = scan()
    with tarfile.open(out_tar, "w") as t:
        for full, rel in files:
            t.add(full, arcname=rel)
    print(f"packed {len(files)} bake-set files -> {out_tar} ({os.path.getsize(out_tar):,} bytes)")

def do_apply(in_tar):
    bak = os.path.join(os.path.dirname(in_tar) or ".", "replay_node_backup.tar")
    cur = scan()
    with tarfile.open(bak, "w") as t:
        for full, rel in cur:
            t.add(full, arcname=rel)
    existing = {os.path.relpath(os.path.join(dp, f), ROOT).replace("\\", "/")
                for dp, _, fn in os.walk(ROOT) for f in fn}
    with tarfile.open(in_tar, "r") as t:
        names = t.getnames(); t.extractall(ROOT)
    added = sum(1 for n in names if n not in existing)
    print(f"backup {len(cur)} -> {bak}; applied {len(names)} ({added} added, {len(names)-added} overwritten)")

if __name__ == "__main__":
    args = sys.argv[1:]
    for i, a in enumerate(list(args)):
        if a == "--with-strategies" and i + 1 < len(args):
            EXTRA_FILES.update(x.strip() for x in args[i + 1].split(",") if x.strip())
            del args[i:i + 2]
            break
    if len(args) != 2 or args[0] not in ("pack", "apply"):
        print(__doc__); sys.exit(2)
    if EXTRA_FILES:
        print("  ++ strategies explicitly included: " + ", ".join(sorted(EXTRA_FILES)))
    (do_pack if args[0] == "pack" else do_apply)(args[1])
