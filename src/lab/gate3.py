"""gate3 — did lab-host reproduce legacy-node's replay cell, TRADE FOR TRADE?

WHY THIS EXISTS
---------------
the fleet is six replay workers whose entire value is throughput. Throughput is worthless if the
answers changed on the way: *"a faster box that changes results is not a faster box, it is a
different experiment."* Gate 3 is the acceptance test that stands between the fleet and every cell
we would ever run on it, and it is the same discipline as the Keel equivalence gate.

    python gate3.py cell                                    # the pre-registered Gate 3 cell
    python gate3.py collect --node worker-1 --cell G3        # snapshot that node's cell output
    python gate3.py collect --node legacy-node   --cell G3
    python gate3.py compare --cell G3 --ref legacy-node --node worker-1

WHAT IT ACTUALLY COMPARES
-------------------------
A cell's output is one JSONL per fire, whose FIRST LINE is the record (`ctick.*` tick-path headers,
`cand.*` / schema-1.x excursion rows). Those headers carry the entry, the context the strategy saw,
and the realised excursion. Two nodes running identical code over identical replay data must produce
the same set, field for field.

THE THREE TIERS, AND WHY THEY ARE NOT ONE TIER
----------------------------------------------
  PRECONDITION  recVer / coreVer / barLabel / inst / bartype.  A mismatch here means you compared
                two DIFFERENT EXPERIMENTS, so the honest answer is neither pass nor fail — it is
                ABORT (exit 2). Reporting "FAIL" would invite someone to go debug determinism when
                the real defect is that one node is running old code.
  GATE          the behaviour fields.  Zero tolerance.  Any difference = FAIL (exit 1).
  NOTED         printed, never fails: the `fireId` sequence number (a PER-RUN counter, never a
                cross-run key -- see episode-id-not-a-cross-run-key) and the tick-path length.

PAIRING, AND THE TRAP IN IT
---------------------------
Trades are paired on (fireTime, dir, signal) -- NOT on `fireId`, whose trailing counter is per-run.
Two fires genuinely can share a stamp (seen in the corpus: `..._GC_S_2` and `..._GC_S_3` at the same
`fireTime`), so a key can hold several records; those are paired in sequence order within the group,
and a group whose SIZE differs is a failure even though every member matched something.

WHAT WOULD MAKE THIS GATE LIE, AND WHAT IS DONE ABOUT IT
--------------------------------------------------------
  * Comparing two different trees.  `--ref`/`--node` tree hashes are checked via muster before any
    trade is read; a mismatch ABORTS.  `--no-tree-check` exists and says what it is overriding.
  * An empty side.  Zero trades on either node is ABORT, never PASS -- a node that never ran and a
    node that ran and found nothing look identical from here, the same shape as *a crashed sensor is
    indistinguishable from a quiet one*.
  * Leading with counts.  1,488 vs 1,488 can be two disjoint sets.  The summary leads with
    matched/differing and prints counts underneath.
  * A tolerance quietly creeping in.  `--tol-ticks` exists for diagnosis; using it stamps
    DEGRADED on the verdict and the verdict file, because a gate passed with a tolerance is not the
    gate this project agreed to.

Exit codes:  0 = PASS   1 = FAIL   2 = could not run the test (abort / precondition)
"""
from __future__ import annotations

import argparse
import glob
import json
import os
import subprocess
import sys
import tarfile
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "sync"))

from lab_faults import swallow  # noqa: E402

try:
    import muster  # the fleet definition + tree hashing live there, and only there
except Exception as _e:  # pragma: no cover - only if the sync tree moved
    muster = None
    swallow("gate3.import_muster", _e)

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

SENT = r"C:\Users\Administrator\Documents\NinjaTrader 8\Sentinel"
SNAP_ROOT = os.path.join(HERE, "gate3")
REMOTE_SENT = r"C:\Users\Administrator\Documents\NinjaTrader 8\Sentinel"

# The pre-registered Gate 3 cell. Written down so the test cannot drift into "whatever was on the
# box"; `collect` stamps it into every snapshot manifest and `compare` refuses a mismatched pair.
CELL = {
    "id": "G3",
    "strategy": "Sentinel Keel(14,AtrBracket,1,True,True,15,2.5,14,1.5,15,3,False,False)",
    "instrument": "NQ 06-26",
    "bars": "1 Minute",
    "mode": "Playback 100x",
    "note": "the cell already run on legacy-node; lab-host must reproduce it trade for trade",
}

# Corpus subtrees a cell writes into. Same list corpus_pull uses, so a cell cannot land somewhere
# this tool does not look.
SUBDIRS = ["Excursions/council/1.5", "Excursions/council/ticks",
           "Excursions/candidates", "Excursions/candidates/ticks",
           "Excursions/ticks"]

# ------------------------------------------------------------------ the tiers
PRECONDITION = ("recVer", "coreVer", "barLabel", "inst", "bartype", "scope", "schema")

GATE = (
    # entry — what the strategy actually did
    "firePx", "pxSrc", "barClosePx", "entryBid", "entryAsk", "entryPx",
    # the context it saw when it decided
    "regime", "adx", "runLength", "clockPhase", "minsToClose", "mtfBias",
    "rvol", "volZ", "climax", "dryUp", "fluxDir", "fluxPressure", "fluxDiverg",
    "brkUpper", "brkLower", "barrierTicks", "conviction", "netScore", "sizeMult",
    "agree", "disagree", "voters",
    # the realised excursion
    "maxFavTicks", "maxAdvTicks", "msToMaxFav", "msToMaxAdv", "msToTargetR", "msToStopR",
    "firstTouchTick", "firstTouch", "maxMFE", "maxMAE", "msToMFE", "msToMAE", "bars",
    "mfe1", "mae1", "mfe5", "mae5", "mfe15", "mae15", "mfe60", "mae60",
    "barsToMFE", "barsToMAE", "barsToTargetR", "barsToStopR", "endReason", "endTime",
    "exitTime", "exitPx",
)

NOTED = ("fireId", "tradeId", "ticks", "trunc", "episodeId")

# Fields measured in ticks, i.e. the only ones --tol-ticks may loosen.
TICK_FIELDS = {"maxFavTicks", "maxAdvTicks", "maxMFE", "maxMAE", "barrierTicks",
               "mfe1", "mae1", "mfe5", "mae5", "mfe15", "mae15", "mfe60", "mae60"}

KINDS = {"excursion", "candidate", "council_tickpath", "candidate_tickpath",
         "manual_tickpath", "keel", "keel_tickpath"}


# ---------------------------------------------------------------- reading a side
def read_header(path: str):
    """First line of a corpus file IS the record; the rest is the tick path."""
    try:
        with open(path, "r", encoding="utf-8") as fh:
            line = fh.readline()
        if not line.strip():
            return None
        o = json.loads(line)
    except Exception as e:
        swallow("gate3.read_header", e, os.path.basename(path))
        return None
    if not isinstance(o, dict) or o.get("kind") not in KINDS:
        return None
    return o


def load_side(root: str):
    """Every corpus record under a snapshot dir, plus the files that could not be read.

    Unreadable files are COUNTED AND NAMED, not skipped quietly: a side that silently dropped 40
    records would otherwise present as a clean smaller set, and the diff would blame determinism.
    """
    recs, bad = [], []
    seen = set()
    for pat in ("**/*.jsonl",):
        for p in glob.glob(os.path.join(root, pat), recursive=True):
            rp = os.path.realpath(p)
            if rp in seen:
                continue
            seen.add(rp)
            o = read_header(p)
            if o is None:
                bad.append(os.path.relpath(p, root))
            else:
                o["_file"] = os.path.relpath(p, root)
                recs.append(o)
    return recs, bad


def stamp(o: dict):
    """The pairing key. fireTime for a fire record, entryTime for a tick path."""
    return o.get("fireTime") or o.get("entryTime") or o.get("entry_utc")


def key_of(o: dict):
    return (stamp(o), o.get("dir"), o.get("signal"))


def seq_of(o: dict) -> int:
    """Trailing counter of `..._GC_S_3`. Used ONLY to order within a same-stamp group."""
    fid = o.get("fireId") or o.get("tradeId") or o.get("_file") or ""
    tail = os.path.splitext(str(fid))[0].rsplit("_", 1)[-1]
    try:
        return int(tail)
    except Exception:
        return 0


def group(recs):
    g = {}
    for o in recs:
        g.setdefault(key_of(o), []).append(o)
    for k in g:
        g[k].sort(key=seq_of)
    return g


# ---------------------------------------------------------------- the comparison
def differs(field, a, b, tol_ticks: float) -> bool:
    if a is None and b is None:
        return False
    if (a is None) != (b is None):
        return True
    if isinstance(a, (int, float)) and isinstance(b, (int, float)) and not isinstance(a, bool):
        if tol_ticks > 0 and field in TICK_FIELDS:
            return abs(float(a) - float(b)) > tol_ticks
        return float(a) != float(b)
    return a != b


def compare_records(ra, rb, tol_ticks):
    """(precondition mismatches, gate mismatches, noted differences) for one paired record."""
    pre, gate, noted = [], [], []
    for f in PRECONDITION:
        if f in ra or f in rb:
            if differs(f, ra.get(f), rb.get(f), 0):
                pre.append((f, ra.get(f), rb.get(f)))
    for f in GATE:
        if f in ra or f in rb:
            if differs(f, ra.get(f), rb.get(f), tol_ticks):
                gate.append((f, ra.get(f), rb.get(f)))
    for f in NOTED:
        if f in ra or f in rb:
            if differs(f, ra.get(f), rb.get(f), 0):
                noted.append((f, ra.get(f), rb.get(f)))
    return pre, gate, noted


# ---------------------------------------------------------------- tree precondition
def tree_hashes(ref: str, node: str):
    """(ref_hash, node_hash, verdict, error) — proving the code matched comes BEFORE the trade diff.

    `verdict` is "same", "vendor-only" (the two nodes run different NT versions and differ ONLY on
    NT's own shipped assemblies — that is the 8.1.7.2-vs-8.1.8.1 regression pair, and gating it is
    the whole point of having worker-5/6), or "differs".
    """
    if muster is None:
        return None, None, None, "muster not importable — cannot prove the trees match"
    man, hsh = {}, {}
    for n in (ref, node):
        if n not in muster.NODES:
            return None, None, None, "unknown node %r (known: %s)" % (n, ", ".join(muster.NODES))
        m, err = muster.fetch_manifest(n)
        if m is None:
            return None, None, None, "%s: %s" % (n, err)
        man[n], hsh[n] = m, muster.tree_hash(m)

    if hsh[ref] == hsh[node]:
        return hsh[ref], hsh[node], "same", None

    a, b = man[ref], man[node]
    changed = [k for k in set(a) & set(b) if a[k][1] != b[k][1]]
    only = sorted(set(a) ^ set(b))
    nt_ref = muster.NODES[ref].get("nt")
    nt_node = muster.NODES[node].get("nt")
    if nt_ref and nt_node and nt_ref != nt_node and not only \
            and all(muster.vendor_only(k) for k in changed):
        return hsh[ref], hsh[node], "vendor-only", None
    return hsh[ref], hsh[node], "differs", None


# ---------------------------------------------------------------- collect
def cmd_collect(args) -> int:
    if muster is None:
        print("✖ muster not importable — collect needs its remote plumbing")
        return 2
    node = args.node
    if node not in muster.NODES:
        print("✖ unknown node %r (known: %s)" % (node, ", ".join(muster.NODES)))
        return 2
    host = muster.NODES[node]["ssh"]
    dest = os.path.join(SNAP_ROOT, args.cell, node)

    if os.path.isdir(dest) and not args.force:
        print("✖ %s already exists. A snapshot is EVIDENCE — overwriting one silently is how a" % dest)
        print("  gate ends up comparing last week's run. Re-run with --force to replace it.")
        return 2

    st = muster.get_status(node)
    if not st.get("reachable"):
        print("✖ %s unreachable: %s" % (node, st.get("err")))
        return 2
    if muster.baking(st):
        print("⚠ %s looks MID-BAKE (rows %ss old). Collecting a cell that is still being written"
              % (node, st.get("row_age_s")))
        print("  gives a truncated side that will diff as missing trades. Let it finish.")
        if not args.force:
            return 2

    man, err = muster.fetch_manifest(node)
    tree = muster.tree_hash(man) if man else None
    if tree is None:
        print("⚠ could not hash %s's tree: %s — the snapshot will carry tree=null and compare"
              % (node, err))
        print("  will refuse it without --no-tree-check")

    # ── WINDOW THE TAR ON FILE MTIME (2026-08-02) ────────────────────────────────────────────────
    #  The first cut tarred the corpus SUBTREES whole. On legacy-node that is 12,683 tick sidecars and
    #  ~128 MB accumulated over months, for a gate that only ever compares ONE 3-session cell — and
    #  it timed out, so the first real Gate 3 collect returned nothing.
    #
    #  ⭐ Window on FILE MTIME, not on any timestamp inside the rows. A replay bake writes rows whose
    #  `fireTime` is historical, so a fireTime window silently excludes the entire replayed corpus —
    #  the identical defect `verify_votes.py` was caught with. WRITTEN-AT is the only clock that means
    #  "this cell, now" for live and replay alike.
    #
    #  ⚠ The window is deliberately generous (default 24 h) and is REPORTED, never silent: a gate that
    #  quietly drops half a side would diff as missing trades and read as a failed equivalence.
    rl = "','".join(d.replace("/", "\\") for d in SUBDIRS)
    script = (
        "$s='" + REMOTE_SENT + "'; $out='C:\\Users\\Administrator\\Downloads\\gate3.tar';"
        "$lst='C:\\Users\\Administrator\\Downloads\\gate3.files.txt';"
        "if(Test-Path $out){Remove-Item $out -Force};"
        "Push-Location $s;"
        "$d=@('" + rl + "') | Where-Object { Test-Path $_ };"
        "if($d.Count -eq 0){'ERR|no corpus dirs'; Pop-Location; exit};"
        "$cut=(Get-Date).AddHours(-" + str(args.since_hours) + ");"
        "$all=Get-ChildItem $d -Recurse -File -ErrorAction SilentlyContinue;"
        "$sel=$all | Where-Object { $_.LastWriteTime -ge $cut };"
        "if($sel.Count -eq 0){'ERR|no files newer than the window'; Pop-Location; exit};"
        "$rel=$sel | ForEach-Object { $_.FullName.Substring($s.Length+1).Replace('\\','/') };"
        "[System.IO.File]::WriteAllLines($lst,$rel);"
        "tar -cf $out -T $lst; Pop-Location;"
        "'SEL|'+$sel.Count+'|'+$all.Count;"
        "(Get-Item $out).Length"
    )
    rc, out, errtxt = muster.ps_remote(host, script, timeout=900)
    if rc != 0 or "ERR|" in out:
        print("✖ remote tar failed: %s" % ((out + errtxt).strip()[:400]))
        return 2

    # Say what was taken and what was skipped. Silent truncation reads as "covered everything".
    for line in out.splitlines():
        if line.startswith("SEL|"):
            _, sel_n, all_n = line.strip().split("|")
            print("  window: %sh → %s of %s corpus files on %s" % (args.since_hours, sel_n, all_n, node))
            break

    os.makedirs(dest, exist_ok=True)
    with tempfile.TemporaryDirectory() as td:
        local_tar = os.path.join(td, "gate3.tar")
        p = subprocess.run(["scp", "-q", "%s:/C:/Users/Administrator/Downloads/gate3.tar" % host,
                            local_tar], capture_output=True, text=True)
        if p.returncode != 0 or not os.path.isfile(local_tar):
            print("✖ scp failed: %s" % ((p.stderr or p.stdout).strip()[:400]))
            return 2
        with tarfile.open(local_tar) as t:
            names = t.getnames()
            try:
                t.extractall(dest, filter="data")   # 3.12+; 3.14 makes it the default
            except TypeError:
                t.extractall(dest)

    recs, bad = load_side(dest)
    meta = {
        "cell": CELL if args.cell == CELL["id"] else dict(CELL, id=args.cell),
        "node": node, "host": host, "tree": tree,
        "nt": st.get("ntver"), "hostname": st.get("host"), "tz": st.get("tz"),
        "files": len(names), "records": len(recs), "unreadable": bad,
    }
    with open(os.path.join(dest, "_snapshot.json"), "w", encoding="utf-8") as fh:
        json.dump(meta, fh, indent=1)

    print("%s → %s" % (node, dest))
    print("  %d file(s), %d corpus record(s), tree %s, NT %s"
          % (len(names), len(recs), tree, st.get("ntver")))
    if bad:
        print("  ⚠ %d file(s) had no readable record: %s" % (len(bad), ", ".join(bad[:5])))
    if not recs:
        print("  ⚠ ZERO corpus records. The corpus directories exist but are empty, so this")
        print("    snapshot cannot be a side of the gate — `compare` will ABORT on it, by design.")
        print("    Either the cell has not been run on %s yet, or it wrote nowhere this tool looks." % node)
    print("  ⚠ originals are UNTOUCHED on the node")
    return 0


# ---------------------------------------------------------------- compare
def load_snapshot(cell: str, node: str, override: str | None):
    root = override or os.path.join(SNAP_ROOT, cell, node)
    if not os.path.isdir(root):
        return None, None, "no snapshot at %s — run `gate3.py collect --node %s --cell %s`" % (root, node, cell)
    meta = {}
    mp = os.path.join(root, "_snapshot.json")
    if os.path.isfile(mp):
        try:
            with open(mp, encoding="utf-8") as fh:
                meta = json.load(fh)
        except Exception as e:
            swallow("gate3.snapshot_meta", e, mp)
    return root, meta, None


def cmd_compare(args) -> int:
    cell = args.cell
    ref_root, ref_meta, err = load_snapshot(cell, args.ref, args.dir_ref)
    if err:
        print("✖ " + err)
        return 2
    node_root, node_meta, err = load_snapshot(cell, args.node, args.dir_node)
    if err:
        print("✖ " + err)
        return 2

    print("GATE 3 — %s   %s  vs  %s" % (cell, args.ref, args.node))
    print("cell: %s" % CELL["strategy"])
    print("      %s · %s · %s" % (CELL["instrument"], CELL["bars"], CELL["mode"]))
    print()

    # --- precondition 1: the same code ran on both sides -----------------------
    if args.tree_check:
        # A snapshot's stamped hash is what the tree WAS when collected, which is the honest thing
        # to gate on -- the node may have been re-synced since. --live-tree re-hashes now instead.
        rt, nt_ = ref_meta.get("tree"), node_meta.get("tree")
        verdict, terr = ("same" if rt and rt == nt_ else "differs"), None
        # Stamped hashes can say "equal"; they cannot say WHY unequal ones are unequal. Only the
        # manifests distinguish "worker-5 runs 8.1.8.1" from real drift, so a stamped mismatch
        # re-hashes live rather than aborting on a number it cannot interpret.
        if args.live_tree or not (rt and nt_) or verdict == "differs":
            rt, nt_, verdict, terr = tree_hashes(args.ref, args.node)
        if terr:
            print("✖ ABORT — cannot prove the trees match: %s" % terr)
            print("  A trade diff across two different trees measures nothing.")
            print("  Override with --no-tree-check only if you can prove it another way.")
            return 2
        if verdict == "differs":
            print("✖ ABORT — the two nodes are NOT running the same code.")
            print("     %-10s tree %s" % (args.ref, rt))
            print("     %-10s tree %s" % (args.node, nt_))
            print("  Run `python sync/muster.py verify --ref %s %s` for the differing files."
                  % (args.ref, args.node))
            return 2
        if verdict == "vendor-only":
            print("tree:      %s / %s — differ ONLY on NT's own vendor assemblies" % (rt, nt_))
            print("           this is the NT-version regression pair; the Sentinel tree is identical.")
            print("           ⚠ A difference found below is then a VERSION difference, not a")
            print("             determinism failure — which is exactly what this pair is for.")
        else:
            print("tree:      %s on both  ✓" % rt)
    else:
        print("tree:      ⚠ NOT CHECKED (--no-tree-check) — this gate cannot distinguish a")
        print("           determinism failure from a code difference")

    nt_ref, nt_node = ref_meta.get("nt"), node_meta.get("nt")
    if nt_ref and nt_node:
        same = "same" if nt_ref == nt_node else "DIFFERENT — expected for the 8.1.8.1 workers"
        print("NT:        %s %s / %s %s   (%s)" % (args.ref, nt_ref, args.node, nt_node, same))

    # --- read both sides -------------------------------------------------------
    ref_recs, ref_bad = load_side(ref_root)
    node_recs, node_bad = load_side(node_root)

    for who, recs, bad in ((args.ref, ref_recs, ref_bad), (args.node, node_recs, node_bad)):
        if bad:
            print("⚠ %s: %d unreadable file(s), e.g. %s" % (who, len(bad), ", ".join(bad[:3])))

    # An empty side is not a pass. Ever.
    for who, recs in ((args.ref, ref_recs), (args.node, node_recs)):
        if not recs:
            print()
            print("✖ ABORT — %s produced ZERO records." % who)
            print("  A node that never ran and a node that ran and found nothing are")
            print("  indistinguishable from here, so this is not a PASS.")
            return 2

    gref, gnode = group(ref_recs), group(node_recs)
    only_ref = sorted(set(gref) - set(gnode), key=lambda k: (str(k[0]), str(k[1])))
    only_node = sorted(set(gnode) - set(gref), key=lambda k: (str(k[0]), str(k[1])))
    shared = sorted(set(gref) & set(gnode), key=lambda k: (str(k[0]), str(k[1])))

    matched = 0
    gate_fails = []      # (key, seq, [(field, ref, node)])
    pre_fails = []
    noted_diffs = {}     # field -> count
    size_fails = []      # (key, n_ref, n_node)

    for k in shared:
        a, b = gref[k], gnode[k]
        if len(a) != len(b):
            size_fails.append((k, len(a), len(b)))
        for i in range(min(len(a), len(b))):
            pre, gt, noted = compare_records(a[i], b[i], args.tol_ticks)
            if pre:
                pre_fails.append((k, i, pre))
            if gt:
                gate_fails.append((k, i, gt))
            else:
                matched += 1
            for f, _x, _y in noted:
                noted_diffs[f] = noted_diffs.get(f, 0) + 1

    # --- precondition 2: the same experiment ----------------------------------
    if pre_fails:
        print()
        print("✖ ABORT — the two sides are not the same experiment.")
        print("  %d paired record(s) differ on identity/version fields:" % len(pre_fails))
        for k, i, pre in pre_fails[:args.show]:
            print("   %s dir=%s #%d" % (k[0], k[1], i))
            for f, x, y in pre:
                print("      %-10s %s: %r   %s: %r" % (f, args.ref, x, args.node, y))
        print("  Fix the versions/tree first; a determinism verdict on this pair means nothing.")
        return 2

    # --- verdict ---------------------------------------------------------------
    differing = len(gate_fails) + len(only_ref) + len(only_node) + len(size_fails)
    passed = differing == 0

    print()
    print("MATCHED   %d record(s) identical on every gate field" % matched)
    print("DIFFERING %d" % differing)
    if gate_fails:
        print("   %d paired record(s) differ on a gate field" % len(gate_fails))
    if only_ref:
        print("   %d only in %s" % (len(only_ref), args.ref))
    if only_node:
        print("   %d only in %s" % (len(only_node), args.node))
    if size_fails:
        print("   %d same-stamp group(s) with a different member count" % len(size_fails))
    print("(counts: %s %d, %s %d — equal counts are not the test)"
          % (args.ref, len(ref_recs), args.node, len(node_recs)))

    for k, i, gt in gate_fails[:args.show]:
        print()
        print("  ✖ %s dir=%s %s #%d" % (k[0], k[1], k[2], i))
        for f, x, y in gt[:12]:
            print("      %-14s %s: %r   %s: %r" % (f, args.ref, x, args.node, y))
        if len(gt) > 12:
            print("      … and %d more field(s)" % (len(gt) - 12))
    if len(gate_fails) > args.show:
        print("\n  … and %d more differing record(s) (--show N)" % (len(gate_fails) - args.show))

    for k in only_ref[:args.show]:
        print("  ✖ only in %-9s %s dir=%s %s" % (args.ref, k[0], k[1], k[2]))
    for k in only_node[:args.show]:
        print("  ✖ only in %-9s %s dir=%s %s" % (args.node, k[0], k[1], k[2]))
    for k, na, nb in size_fails[:args.show]:
        print("  ✖ group %s dir=%s: %s has %d, %s has %d" % (k[0], k[1], args.ref, na, args.node, nb))

    if noted_diffs:
        print()
        print("noted (never fails the gate): "
              + ", ".join("%s×%d" % (f, n) for f, n in sorted(noted_diffs.items())))
        if "fireId" in noted_diffs or "tradeId" in noted_diffs:
            print("  the id's trailing counter is PER-RUN — differing here is expected, not drift")

    degraded = args.tol_ticks > 0 or not args.tree_check
    print()
    if passed and degraded:
        verdict = "PASS (DEGRADED)"
        print("▲ %s — every record matched, but this run was not the full gate:" % verdict)
        if args.tol_ticks > 0:
            print("    --tol-ticks %g was allowed on tick-valued fields" % args.tol_ticks)
        if not args.tree_check:
            print("    the trees were not proven identical")
        print("  Re-run clean before calling Gate 3 passed.")
    elif passed:
        verdict = "PASS"
        print("✅ GATE 3 PASS — %d of %d records identical, 0 differing." % (matched, len(ref_recs)))
        print("   %s reproduces %s trade for trade. the fleet may run cells." % (args.node, args.ref))
    else:
        verdict = "FAIL"
        print("✖ GATE 3 FAIL — %d differing. %s does NOT reproduce %s."
              % (differing, args.node, args.ref))
        print("  Do not run the matrix on this fleet until this is understood.")

    out = {
        "cell": cell, "ref": args.ref, "node": args.node, "verdict": verdict,
        "matched": matched, "differing": differing,
        "n_ref": len(ref_recs), "n_node": len(node_recs),
        "only_ref": [list(k) for k in only_ref], "only_node": [list(k) for k in only_node],
        "size_fails": [[list(k), a, b] for k, a, b in size_fails],
        "gate_fails": [{"key": list(k), "i": i, "fields": [[f, x, y] for f, x, y in gt]}
                       for k, i, gt in gate_fails],
        "noted": noted_diffs, "tol_ticks": args.tol_ticks, "tree_checked": args.tree_check,
        "unreadable_ref": ref_bad, "unreadable_node": node_bad,
    }
    vdir = os.path.join(SNAP_ROOT, cell)
    try:
        os.makedirs(vdir, exist_ok=True)
        vp = os.path.join(vdir, "verdict_%s_vs_%s.json" % (args.ref, args.node))
        with open(vp, "w", encoding="utf-8") as fh:
            json.dump(out, fh, indent=1)
        print("\nverdict → %s" % vp)
    except Exception as e:
        swallow("gate3.write_verdict", e, vdir)
        print("\n⚠ could not write the verdict file — the console output above is the record")

    return 0 if passed else 1


def cmd_cell(args) -> int:
    print("Gate 3 pre-registered cell")
    for k in ("id", "strategy", "instrument", "bars", "mode", "note"):
        print("  %-11s %s" % (k, CELL[k]))
    print()
    print("Run it on legacy-node and on the sentry with IDENTICAL settings, then:")
    print("  python gate3.py collect --node legacy-node   --cell %s" % CELL["id"])
    print("  python gate3.py collect --node worker-1 --cell %s" % CELL["id"])
    print("  python gate3.py compare --cell %s --ref legacy-node --node worker-1" % CELL["id"])
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(
        description="gate3 — trade-for-trade equivalence between a Watch sentry and legacy-node")
    sub = ap.add_subparsers(dest="cmd")

    c = sub.add_parser("cell", help="print the pre-registered Gate 3 cell")
    c.set_defaults(fn=cmd_cell)

    c = sub.add_parser("collect", help="snapshot a node's cell output locally")
    c.add_argument("--node", required=True)
    c.add_argument("--cell", default=CELL["id"])
    c.add_argument("--force", action="store_true",
                   help="replace an existing snapshot / collect mid-bake (say why)")
    c.add_argument("--since-hours", type=float, default=24.0,
                   help="only tar corpus files WRITTEN in this window (default 24). The gate compares "
                        "one cell, not the node's whole history; tarring months of sidecars times out.")
    c.set_defaults(fn=cmd_collect)

    c = sub.add_parser("compare", help="the gate")
    c.add_argument("--cell", default=CELL["id"])
    c.add_argument("--ref", default="legacy-node")
    c.add_argument("--node", required=True)
    c.add_argument("--dir-ref", help="compare a directory directly instead of a snapshot")
    c.add_argument("--dir-node", help="compare a directory directly instead of a snapshot")
    c.add_argument("--no-tree-check", dest="tree_check", action="store_false", default=True,
                   help="skip proving both nodes ran identical code (degrades the verdict)")
    c.add_argument("--live-tree", action="store_true",
                   help="re-hash both trees now instead of trusting the snapshot stamps")
    c.add_argument("--tol-ticks", type=float, default=0.0,
                   help="diagnosis only: allow N ticks of slack (degrades the verdict)")
    c.add_argument("--show", type=int, default=10)
    c.set_defaults(fn=cmd_compare)

    a = ap.parse_args()
    if not getattr(a, "fn", None):
        ap.print_help()
        return 2
    return a.fn(a)


if __name__ == "__main__":
    sys.exit(main())
