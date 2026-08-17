"""muster — keep the Watch running the SAME CODE, and be able to prove it.

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
"""
from __future__ import annotations

import argparse
import base64
import hashlib
import json
import os
import subprocess
import sys
import tarfile
import tempfile
import time

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")))
from lab_faults import swallow  # noqa: E402

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

# ---------------------------------------------------------------- the fleet
NT_ROOT = r"C:\Users\Administrator\Documents\NinjaTrader 8"
CUSTOM = NT_ROOT + r"\bin\Custom"

FLEET_CONF = os.path.join(NT_ROOT, "Sentinel", "fleet.conf")


def _load_fleet(path=FLEET_CONF):
    r"""The fleet registry, from Sentinel\fleet.conf. See fleet.conf.example for the format.

    ⭐ THIS USED TO BE A HARDCODED DICT of the author's own six machines, which made the file
    unpublishable AND made the tool unusable by anyone else -- it was pinned to boxes only one
    person has. The second reason is the better one: a fleet registry belongs in config
    regardless of who can see it.

    ⚠ FAILS LOUD on a missing file rather than defaulting to an empty fleet. A `muster status`
    that silently reports zero nodes reads as "the fleet is fine" -- the same success-shaped
    nothing this project keeps finding (`QUEUE COMPLETE · 0 SESSIONS`).
    """
    nodes = {}
    try:
        for raw in open(path, encoding="utf-8"):
            line = raw.split("#")[0].strip()
            if not line:
                continue
            parts = line.split()
            name, kv = parts[0], dict(p.split("=", 1) for p in parts[1:] if "=" in p)
            nodes[name] = {"ssh": kv.get("ssh", name), "nt": kv.get("nt", "")}
            if "retired" in kv:
                nodes[name]["retired"] = kv["retired"]
    except OSError:
        sys.stderr.write(
            "muster: no fleet registry at %s\n"
            "        copy Sentinel\\fleet.conf.example and describe your machines.\n" % path)
        raise SystemExit(2)
    if not nodes:
        sys.stderr.write("muster: %s defines no nodes — refusing to run on an empty fleet\n" % path)
        raise SystemExit(2)
    return nodes


NODES = _load_fleet()
# Every node that is not retired. (Was `startswith("sentry-")`, which hardcoded the naming of
# one person's fleet into the selection logic as well as the table.)
SENTRIES = [n for n, v in NODES.items() if not v.get("retired")]

# Node-local state. Copying any of these is a bug, not an optimisation:
#   the DLL/PDB are BUILD OUTPUT (NT rebuilds them), the csproj is rewritten by NT on every F5
#   (a stale copy causes CS2002), Run.conf is per-node, Excursions is the node's own unpulled output.
#   The LOCALISED SATELLITES (de-DE\NinjaTrader.Custom.resources.dll, ...) are build output too,
#   compiled from the Resource.*.resx files that ARE hashed. NT stamps a fresh MVID into each one on
#   every compile, so they differ on every node forever while their sources are byte-identical --
#   measured 2026-08-02: 8 satellites differing, same size to the byte, all 9 .resx identical. Left
#   in, `verify` reported MISMATCH on all six workers for files nobody had touched, and a gate that
#   cries wolf is one people learn to ignore. Vendor satellites are NOT excluded: a real NT version
#   difference must stay visible (and NinjaTrader.Vendor.dll itself shows it anyway).
#   `run-log.jsonl` is the Conductor's CHECKPOINT LEDGER and is as node-local as Run.conf. It reached the
#   workers with the tree carve, and on 2026-08-02 worker-1 — which has never baked anything — read
#   legacy-node's 45h-old checkpoints and took the Conductor's RESUME path on a cold box. A checkpoint asserts
#   "THIS machine already baked these sessions"; copying it makes that a lie on arrival.
EXCLUDE_SUBSTR = (
    "ninjatrader.custom.dll", "ninjatrader.custom.pdb", "ninjatrader.custom.xml",
    "ninjatrader.custom.csproj", "ninjatrader.custom.resources.dll", "\\obj\\", ".bak",
    "\\conductor\\run-log", "armed.token",
)


def excluded(rel: str) -> bool:
    low = rel.lower()
    return any(x in low for x in EXCLUDE_SUBSTR)


# NT's own shipped assemblies, not ours. They differ between 8.1.7.2 and 8.1.8.1 by definition.
VENDOR_SUBSTR = ("ninjatrader.vendor.dll", "ninjatrader.vendor.resources.dll")


def vendor_only(rel: str) -> bool:
    low = rel.lower()
    return any(x in low for x in VENDOR_SUBSTR)


# ------------------------------------------------------------ remote plumbing
def ps_remote(host: str, script: str, timeout: int = 300):
    """Run PowerShell on a node.

    Encoded, always. A script that traverses bash -> ssh -> cmd -> powershell loses characters
    silently and reports success, which is how a tool ends up 'finding nothing' on a node that
    has plenty. See corpus_pull v1.
    """
    b64 = base64.b64encode(script.encode("utf-16-le")).decode()
    cmd = ["ssh", "-o", "BatchMode=yes", "-o", "ConnectTimeout=10", host,
           "powershell.exe -NoProfile -EncodedCommand " + b64]
    try:
        p = subprocess.run(cmd, capture_output=True, text=True, timeout=timeout)
        return p.returncode, p.stdout, p.stderr
    except Exception as e:
        return 255, "", str(e)


MANIFEST_PS = r"""
$ErrorActionPreference='SilentlyContinue'
$c = '%s'
if (-not (Test-Path $c)) { 'ERR|no tree'; exit }
Get-ChildItem $c -Recurse -File | ForEach-Object {
    $rel = $_.FullName.Substring($c.Length+1)
    $h = (Get-FileHash $_.FullName -Algorithm SHA256).Hash
    "$rel|$($_.Length)|$h"
}
""" % CUSTOM

STATUS_PS = r"""
$ErrorActionPreference='SilentlyContinue'
$nt = Get-Process NinjaTrader -EA SilentlyContinue
$exe = 'C:\Program Files\NinjaTrader 8\bin\NinjaTrader.exe'
$root = '%s'
$exc = Join-Path $root 'Sentinel\Excursions'
$newest = $null
if (Test-Path $exc) {
    $f = Get-ChildItem $exc -Recurse -File -Filter *.jsonl | Sort-Object LastWriteTime -Desc | Select-Object -First 1
    if ($f) { $newest = [int]((Get-Date) - $f.LastWriteTime).TotalSeconds }
}
$o = [ordered]@{
    host      = $env:COMPUTERNAME
    ntver     = (Get-Item $exe -EA SilentlyContinue).VersionInfo.ProductVersion
    nt_up     = [bool]$nt
    nt_ram_mb = if ($nt) { [int]($nt.WorkingSet64/1MB) } else { 0 }
    cs        = (Get-ChildItem (Join-Path $root 'bin\Custom') -Recurse -Filter *.cs -EA SilentlyContinue).Count
    files     = (Get-ChildItem (Join-Path $root 'bin\Custom') -Recurse -File -EA SilentlyContinue).Count
    row_age_s = $newest
    free_gb   = [math]::Round((Get-PSDrive C).Free/1GB,1)
    tz        = (Get-TimeZone).Id
    stamp     = (Get-Content (Join-Path $root 'Sentinel\tree.hash') -EA SilentlyContinue | Select-Object -First 1)
}
$o | ConvertTo-Json -Compress
""" % NT_ROOT


def fetch_manifest(node: str):
    """Return {relpath: (size, sha256)} for a node's bin\\Custom, or None."""
    host = NODES[node]["ssh"]
    rc, out, err = ps_remote(host, MANIFEST_PS, timeout=600)
    if rc != 0:
        return None, (err or "ssh failed").strip()[:120]
    man = {}
    for line in out.splitlines():
        line = line.strip()
        if not line or "|" not in line:
            continue
        if line.startswith("ERR|"):
            return None, line[4:]
        rel, size, sha = line.rsplit("|", 2)
        if excluded(rel):
            continue
        man[rel] = (int(size), sha.upper())
    return man, None


def tree_hash(man: dict) -> str:
    h = hashlib.sha256()
    for rel in sorted(man):
        h.update(("%s|%d|%s\n" % (rel, man[rel][0], man[rel][1])).encode())
    return h.hexdigest()[:16]


def local_manifest(root: str = CUSTOM):
    man = {}
    for dp, dn, fn in os.walk(root):
        dn[:] = [d for d in dn if d.lower() not in ("obj", "_archive", ".git")]
        for f in fn:
            p = os.path.join(dp, f)
            rel = os.path.relpath(p, root)
            if excluded(rel):
                continue
            with open(p, "rb") as fh:
                sha = hashlib.sha256(fh.read()).hexdigest().upper()
            man[rel] = (os.path.getsize(p), sha)
    return man


def get_status(node: str):
    rc, out, err = ps_remote(NODES[node]["ssh"], STATUS_PS, timeout=180)
    if rc != 0:
        return {"reachable": False, "err": (err or "unreachable").strip()[:100]}
    try:
        d = json.loads(out.strip().splitlines()[-1])
        d["reachable"] = True
        return d
    except Exception as e:
        return {"reachable": False, "err": "bad status json: %s" % e}


def baking(st: dict) -> bool:
    """Is this node mid-bake? NT up AND rows written recently is the honest test:
    NT sitting at a logon window is not a bake, and a node that wrote a row 10 days ago is idle."""
    return bool(st.get("nt_up")) and st.get("row_age_s") is not None and st["row_age_s"] < 900


# ------------------------------------------------------------------ commands
def cmd_status(args):
    nodes = args.nodes or (SENTRIES + ["legacy-node"])
    print("%-9s %-8s %-9s %-6s %-6s %-9s %-8s %-7s %s" %
          ("node", "up", "nt", "cs", "files", "bake", "free GB", "tz", "tree"))
    for n in nodes:
        st = get_status(n)
        if not st.get("reachable"):
            print("%-9s %-8s %s" % (n, "DOWN", st.get("err", "")))
            continue
        # The `tree` column was hard-coded to "-" unless --hash, so it read BLANK on every node
        # forever — including right after a successful `stamp`, which writes the hash the column is
        # named after. A blank reads as "nothing to see"; the honest words are "unstamped" (nobody
        # ever recorded it) and "stale?" is what --hash is for. Same family as worker-1's card
        # showing `code unstamped` as emptiness rather than as a problem.
        th = st.get("stamp") or "UNSTAMPED"
        if args.hash:
            man, err = fetch_manifest(n)
            live = tree_hash(man) if man else ("ERR " + str(err))
            # A stamp is a RECORDED CLAIM; --hash is a LIVE READING. When they disagree the tree has
            # moved since it was stamped, and saying so is the whole point of keeping both.
            th = live if live == st.get("stamp") else "%s (stamp %s)" % (live, st.get("stamp") or "none")
        print("%-9s %-8s %-9s %-6s %-6s %-9s %-8s %-7s %s" % (
            n, "up", st.get("ntver", "?"), st.get("cs", "?"), st.get("files", "?"),
            "BAKING" if baking(st) else "idle", st.get("free_gb", "?"),
            (st.get("tz") or "?").replace(" Standard Time", ""), th))
    return 0


def cmd_verify(args):
    ref = args.ref
    targets = args.nodes or [n for n in SENTRIES if n != ref]
    print("reference: %s" % ref)
    if ref == "main":
        ref_man, err = local_manifest(), None
    else:
        ref_man, err = fetch_manifest(ref)
    if ref_man is None:
        print("  ✗ cannot read reference tree: %s" % err)
        return 2
    rh = tree_hash(ref_man)
    print("  %d files · tree %s\n" % (len(ref_man), rh))

    ref_nt = NODES.get(ref, {}).get("nt")
    allsame = True
    for n in targets:
        man, err = fetch_manifest(n)
        if man is None:
            print("%-9s ✗ %s" % (n, err))
            allsame = False
            continue
        th = tree_hash(man)
        if th == rh:
            print("%-9s ✓ identical  (%d files · %s)" % (n, len(man), th))
            continue
        missing = sorted(set(ref_man) - set(man))
        extra = sorted(set(man) - set(ref_man))
        diff = sorted(k for k in set(man) & set(ref_man) if man[k][1] != ref_man[k][1])

        # the fleet runs TWO NT versions ON PURPOSE: 8.1.7.2 on worker-1..4 (the Gate 3 baseline)
        # and 8.1.8.1 on 5..6 (the regression check). A cross-version pair MUST differ on the
        # vendor assemblies -- that is the experiment, not drift. Calling it a fleet MISMATCH
        # taught the reader to ignore the one line that matters, so the two are separated here:
        # a vendor-only difference across versions is EXPECTED; one Sentinel file is still a fail.
        nt = NODES.get(n, {}).get("nt")
        expected = (nt and ref_nt and nt != ref_nt and not missing and not extra
                    and all(vendor_only(k) for k in diff))
        if expected:
            print("%-9s ≈ EXPECTED  (%d files · %s) — NT %s vs %s, %d vendor assembly file(s) only"
                  % (n, len(man), th, nt, ref_nt, len(diff)))
            continue

        allsame = False
        print("%-9s ✗ DIFFERS  (%d files · %s)" % (n, len(man), th))
        for label, lst in (("missing", missing), ("extra", extra), ("changed", diff)):
            for k in lst[:args.show]:
                tag = "  (vendor)" if label == "changed" and vendor_only(k) else ""
                print("            %-8s %s%s" % (label, k, tag))
            if len(lst) > args.show:
                print("            %-8s … and %d more" % (label, len(lst) - args.show))
    print("\n%s" % ("ALL MATCH — the fleet provably runs one tree"
                    if allsame else "MISMATCH — do not treat cross-node results as comparable"))
    return 0 if allsame else 1


def _matches(rel: str, pats) -> bool:
    low = rel.lower()
    return any(p.lower() in low for p in pats)


def cmd_push(args):
    src = args.source
    targets = SENTRIES if args.to == ["all"] else args.to
    if src == "main":
        src_man = local_manifest()
        src_root = CUSTOM
    else:
        src_man, err = fetch_manifest(src)
        if src_man is None:
            print("✗ cannot read source %s: %s" % (src, err))
            return 2
        src_root = None
    print("source %s · %d files · tree %s\n" % (src, len(src_man), tree_hash(src_man)))

    rc_all = 0
    for n in targets:
        st = get_status(n)
        if not st.get("reachable"):
            print("%-9s ✗ unreachable (%s)" % (n, st.get("err")))
            rc_all = 1
            continue
        if baking(st) and not args.force:
            print("%-9s ⛔ REFUSING — node is BAKING (NT up, last row %ss ago). "
                  "Use --force only if you accept killing that cell."
                  % (n, st.get("row_age_s")))
            rc_all = 1
            continue

        tgt_man, err = fetch_manifest(n)
        if tgt_man is None:
            print("%-9s ✗ cannot read tree: %s" % (n, err))
            rc_all = 1
            continue

        changed = [k for k in src_man if k in tgt_man and src_man[k][1] != tgt_man[k][1]]
        new = [k for k in src_man if k not in tgt_man]
        gone = [k for k in tgt_man if k not in src_man]
        if args.only:
            changed = [k for k in changed if _matches(k, args.only)]
            new = [k for k in new if _matches(k, args.only)]
            gone = [k for k in gone if _matches(k, args.only)]
        send = list(changed) + (new if args.allow_new else [])

        print("%-9s %d changed · %d new%s · %d only-on-target%s" % (
            n, len(changed), len(new), "" if args.allow_new else " (skipped, need --allow-new)",
            len(gone), "" if args.prune else " (kept, need --prune)"))
        for k in changed[:args.show]:
            print("            changed  %s" % k)
        if len(changed) > args.show:
            print("            changed  … and %d more (--show N to see them)" % (len(changed) - args.show))
        # ⛔ NAME WHAT GETS DELETED. --prune counted these and never printed one of them, so the
        #   only way to learn what a prune removed was to run it and look afterwards. A delete you
        #   cannot review before authorising is not reviewable at all, and "4 only-on-target" reads
        #   as housekeeping right up until one of the four is the file you were about to need.
        #   Same family as the no-silent-caps rule: a tool that drops work must say which work.
        #   ⚠ ALWAYS listed in full — a `[:args.show]` truncation here would recreate the defect
        #   for exactly the case that matters most, the target that has drifted furthest.
        if gone:
            for k in gone:
                print("            %s  %s" % ("DELETE  " if args.prune else "on-target-only(kept)", k))
        if not send and not (args.prune and gone):
            print("            nothing to do")
            continue
        if args.dry_run:
            print("            (dry-run)")
            continue
        if src_root is None:
            print("            ✗ push from a remote source is not implemented; "
                  "run with --from main, or pull that tree to main first")
            rc_all = 1
            continue

        ok = _send_files(n, src_root, send, gone if args.prune else [])
        if not ok:
            rc_all = 1
            continue

        # VERIFY WHAT WE SENT, on the far side. This used to compare the two TREE HASHES, which is a
        # stricter claim than the push makes and is false by construction in the case this tool's own
        # header describes: `--from main` sends only files the target already has, so main's 1,217-file
        # dev tree can never hash-equal a sentry's 416-file carve. It would have reported POST-SYNC
        # MISMATCH after a perfectly correct push — the same failure mode as the localised-satellite
        # noise this tool already fixed once: a check that fires on a difference the design deliberately
        # introduced teaches its reader to ignore the one line that matters.
        man2, err = fetch_manifest(n)
        if man2 is None:
            print("            ✗ POST-SYNC UNREADABLE — cannot confirm what landed (%s)" % err)
            rc_all = 1
            continue
        bad = [k for k in send if k not in man2 or man2[k][1] != src_man[k][1]]
        still = [k for k in (gone if args.prune else []) if k in man2]
        if bad or still:
            print("            ✗ POST-SYNC MISMATCH — %d of %d sent files do not match source"
                  % (len(bad), len(send)))
            for k in (bad + still)[:args.show]:
                print("            bad      %s" % k)
            rc_all = 1
            continue
        print("            ✓ %d/%d files match source on the far side%s" % (
            len(send), len(send),
            " · WHOLE TREE now identical (%s)" % tree_hash(man2)
            if tree_hash(man2) == tree_hash(src_man) else ""))

        if args.compile:
            cok, msg = _compile(n)
            print("            %s %s" % ("✓" if cok else "✗", msg))
            if not cok:
                rc_all = 1
    return rc_all


def _send_files(node: str, src_root: str, files, prune):
    """tar the changed files, ship once, expand on the far side. One transfer, not N."""
    host = NODES[node]["ssh"]
    tmp = tempfile.mkdtemp(prefix="muster_")
    tarpath = os.path.join(tmp, "delta.tar")
    try:
        with tarfile.open(tarpath, "w") as t:
            for rel in files:
                t.add(os.path.join(src_root, rel), arcname=rel.replace("\\", "/"))
        p = subprocess.run(["scp", "-q", tarpath, "%s:C:/stage/delta.tar" % host],
                           capture_output=True, text=True, timeout=900)
        if p.returncode != 0:
            # C:\stage may not exist on a fresh clone
            ps_remote(host, "New-Item -ItemType Directory -Force -Path C:\\stage | Out-Null")
            p = subprocess.run(["scp", "-q", tarpath, "%s:C:/stage/delta.tar" % host],
                               capture_output=True, text=True, timeout=900)
            if p.returncode != 0:
                print("            ✗ scp failed: %s" % p.stderr.strip()[:120])
                return False
        script = ("$ErrorActionPreference='Stop'\n"
                  "Push-Location '%s'\n& tar.exe -xf C:\\stage\\delta.tar\nPop-Location\n"
                  "Remove-Item C:\\stage\\delta.tar -Force\n" % CUSTOM)
        for rel in prune:
            script += "Remove-Item -LiteralPath '%s' -Force -EA SilentlyContinue\n" % os.path.join(CUSTOM, rel)
        rc, out, err = ps_remote(host, script, timeout=600)
        if rc != 0:
            print("            ✗ expand failed: %s" % (err or "").strip()[:120])
            return False
        return True
    finally:
        # `swallow` REPORTS a caught exception; it is not a context manager. Used as `with swallow(...)`
        # it raised TypeError out of this `finally` on every real push — so the push half of muster had
        # never once run to completion, which is most of why the fleet was allowed to drift. Found
        # 2026-08-03 by running it. A cleanup path is exactly where a latent bug hides: it only executes
        # on the real run, never on --dry-run.
        try:
            os.remove(tarpath)
            os.rmdir(tmp)
        except Exception as e:
            swallow("muster.cleanup", e, tarpath)


def _compile(node: str):
    """A synced tree that does not compile is worse than a stale one that does."""
    rc, out, err = ps_remote(
        NODES[node]["ssh"],
        "& 'C:\\ntbv\\Scripts\\python.exe' -m nt8bridge compile 2>&1 | Out-String", timeout=420)
    blob = (out or "") + (err or "")
    if '"ok": true' in blob.replace(" ", "").replace('"ok":true', '"ok": true'):
        return True, "compiles clean"
    if "errors" in blob and "[]" in blob:
        return True, "compiles clean"
    if "Connection refused" in blob or "could not connect" in blob.lower():
        return False, "compile UNVERIFIED — NT not running on this node (bridge needs it)"
    return False, "compile FAILED: " + " ".join(blob.split())[:160]


def cmd_stamp(args):
    """Write each node's tree hash where the dashboard can read it, so fleet drift is a glance.

    Stamped ON the node (Sentinel\\tree.hash) as well as collected here: the dashboard probe
    reads the node, not this file, so a stamp that only lived on main would be a claim about
    the fleet rather than a reading of it.
    """
    out = {}
    for n in (args.nodes or SENTRIES + ["legacy-node"]):
        man, err = fetch_manifest(n)
        if man is None:
            out[n] = {"err": str(err)}
            print("%-9s %s" % (n, out[n]))
            continue
        th = tree_hash(man)
        out[n] = {"tree": th, "files": len(man)}
        ps_remote(NODES[n]["ssh"],
                  "Set-Content -Path '%s\\Sentinel\\tree.hash' -Value '%s' -Encoding ASCII"
                  % (NT_ROOT, th), timeout=60)
        print("%-9s %s" % (n, out[n]))
    dst = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "fleet_trees.json")
    with open(os.path.abspath(dst), "w") as f:
        json.dump({"stamped": int(time.time()), "nodes": out}, f, indent=1)
    print("\nwrote %s" % os.path.abspath(dst))
    return 0


def main():
    ap = argparse.ArgumentParser(description="muster — prove and maintain one tree across the Watch")
    sub = ap.add_subparsers(dest="cmd")

    s = sub.add_parser("status", help="who is up, what NT, what tree, is it baking")
    s.add_argument("nodes", nargs="*")
    s.add_argument("--hash", action="store_true", help="also compute each tree hash (slower)")
    s.set_defaults(fn=cmd_status)

    v = sub.add_parser("verify", help="prove every node runs the same tree")
    v.add_argument("nodes", nargs="*")
    # Was `legacy-node` until it was retired 2026-08-02. A default pointing at a powered-off box makes a
    # bare `verify` fail for a reason that has nothing to do with what it is checking.
    # Default comes from the registry, not a literal. A hardcoded node name is both a
    # publication blocker and a bug for anyone whose fleet is named differently.
    v.add_argument("--ref", default=(SENTRIES[0] if SENTRIES else "main"),
                   help="reference node (or 'main'); default = first active node in fleet.conf")
    v.add_argument("--show", type=int, default=8)
    v.set_defaults(fn=cmd_verify)

    p = sub.add_parser("push", help="one-way sync, guarded, then re-verified")
    p.add_argument("--from", dest="source", default="main")
    p.add_argument("--to", nargs="+", default=["all"])
    p.add_argument("--allow-new", action="store_true", help="also send files the target lacks")
    # WHY: `--to all` with no filter is almost never what you want from main. Measured 2026-08-03:
    # a bare `push --from main --to all` would have sent 125 files per node — 123 of them STOCK NT
    # indicators differing only because main's copies carry MIXED line endings, and on worker-5/6
    # it would have overwritten `NinjaTrader.Vendor.dll` with main's 8.1.7.2 copy on boxes running
    # 8.1.8.1 ON PURPOSE. The real drift was two files. Converge what drifted, not what merely differs.
    p.add_argument("--only", nargs="+", metavar="SUBSTR",
                   help="restrict the push to relpaths containing any of these substrings")
    p.add_argument("--prune", action="store_true", help="delete files the source does not have")
    p.add_argument("--force", action="store_true", help="sync even into a live bake (say why)")
    p.add_argument("--dry-run", action="store_true")
    p.add_argument("--no-compile", dest="compile", action="store_false", default=True)
    p.add_argument("--show", type=int, default=8)
    p.set_defaults(fn=cmd_push)

    t = sub.add_parser("stamp", help="record each node's tree hash for the dashboard")
    t.add_argument("nodes", nargs="*")
    t.set_defaults(fn=cmd_stamp)

    args = ap.parse_args()
    if not args.cmd:
        args = ap.parse_args(["verify"])
    return args.fn(args)


if __name__ == "__main__":
    sys.exit(main())
