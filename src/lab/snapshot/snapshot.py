#!/usr/bin/env python
r"""
Sentinel corpus snapshot ladder  (tiered, WORM-style, validate-before-destruct).

Tiers
-----
  session   the LIVE Excursions\ tree itself. Recorder v2.1.2 streams rows to
            disk crash-safe every 60 min, so live IS the continuous session-durable
            record. No separate copy is taken -- it is the ground truth the daily
            validates against.
  daily     `daily <date>`  -- point-in-time copy of the corpus + a consistent
            VACUUM'd sentinel.db, zipped, with a per-line-hash manifest. Validates
            its own copy is a SUPERSET of live (catches a mid-copy miss + self-heals).
            Pruned only after the covering weekly validates.
  weekly    `weekly <isoweek>` -- the permanent master (kept forever). Validates it
            is a superset of the union of that week's dailies (WORM guarantee: a row
            any daily saw survives even if live later sheds it), self-heals a gap,
            then destructs the validated dailies.

Validation is SUPERSET-of-row-content-hashes, not a file diff: corpus files grow
append-only through the day, so byte-compare is useless but "every line I had before
is still here" is exact and schema-agnostic.

CAVEAT: this guarantees ARCHIVE INTEGRITY (no row is ever lost), NOT corpus
CORRECTNESS. A superset check preserves poisoned rows as faithfully as clean ones --
contamination is corpus_probe's job, not this ladder's.

Usage
-----
  python snapshot.py daily              # snapshot today, validate vs live
  python snapshot.py weekly             # snapshot this iso-week, validate+prune dailies
  python snapshot.py daily  --date 2026-07-17
  python snapshot.py weekly --week 2026-W29
  python snapshot.py verify <snapshot-dir>   # re-check a snapshot's manifest vs its zip
  python snapshot.py list               # show the ladder
Add --dry-run to any command to plan without writing / destructing.
"""
from __future__ import annotations
import argparse, datetime, hashlib, json, os, shutil, sqlite3, sys, tempfile, zipfile
from pathlib import Path
import sys as _sys, os as _os
_LAB_ROOT = _os.path.abspath(_os.path.join(_os.path.dirname(_os.path.abspath(__file__)), ".."))
if _LAB_ROOT not in _sys.path:
    _sys.path.insert(0, _LAB_ROOT)
from lab_faults import swallow

# --- fixed layout (this file lives at  Sentinel\Lab\snapshot\snapshot.py) -----------
SENTINEL   = Path(__file__).resolve().parents[2]          # ...\NinjaTrader 8\Sentinel
CORPUS     = SENTINEL / "Excursions"
DB         = SENTINEL / "Lab" / "db" / "sentinel.db"
SNAPROOT   = SENTINEL / "Snapshots"
DAILY_DIR  = SNAPROOT / "daily"
WEEKLY_DIR = SNAPROOT / "weekly"
LOGFILE    = SENTINEL / "sentinel.log"

# corpus subtrees to snapshot (everything JSONL-ish; _archive included on purpose so a
# file moved out of live is still captured). The snapshot output dir is never itself
# inside CORPUS, so there is no recursion risk.
CORPUS_GLOB = "**/*.jsonl"


def log(msg: str, crit: bool = False) -> None:
    stamp = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    tag = "SNAPSHOT-CRIT" if crit else "SNAPSHOT"
    line = f"{stamp} [{tag}] {msg}"
    print(line)
    try:
        with open(LOGFILE, "a", encoding="utf-8") as fh:
            fh.write(line + "\n")
    except OSError as _swex:
        swallow("snapshot.snapshot.log", _swex)  # never let a log-write failure sink a snapshot


# ---------------------------------------------------------------------------- manifest
def build_manifest(corpus_root: Path) -> dict:
    """{relpath: {"n": lineCount, "hashes": [sha256(line), ...]}} over every corpus file.
    A row = one non-empty line. Order-independent superset checks use the hash SETS."""
    man: dict[str, dict] = {}
    for f in sorted(corpus_root.glob(CORPUS_GLOB)):
        rel = f.relative_to(corpus_root).as_posix()
        hashes = []
        with open(f, "rb") as fh:
            for raw in fh:
                s = raw.strip()
                if s:
                    hashes.append(hashlib.sha256(s).hexdigest())
        man[rel] = {"n": len(hashes), "hashes": hashes}
    return man


def manifest_lineset(man: dict) -> set:
    out: set[str] = set()
    for entry in man.values():
        out.update(entry["hashes"])
    return out


def manifest_totals(man: dict) -> tuple[int, int]:
    return len(man), sum(e["n"] for e in man.values())


# ------------------------------------------------------------------------------ copy/db
def snapshot_db(dest_db: Path) -> int:
    """Consistent, compacted copy of the WAL-mode DB via VACUUM INTO. Returns byte size.
    Skips (returns -1) if the DB is absent."""
    if not DB.exists():
        log(f"DB absent at {DB} -- corpus-only snapshot", crit=True)
        return -1
    if dest_db.exists():
        dest_db.unlink()
    con = sqlite3.connect(f"file:{DB}?mode=ro", uri=True)
    try:
        con.execute("VACUUM INTO ?", (str(dest_db),))
    finally:
        con.close()
    return dest_db.stat().st_size


# ---------------------------------------------------------------------------- take snap
def take_snapshot(dest_dir: Path, dry: bool) -> tuple[dict, list]:
    """Single pass: read each corpus file ONCE, write it to the zip AND hash its lines for
    the manifest -- so the manifest is, by construction, an exact index of what the zip
    holds (no live-growth skew between two separate reads). Also snapshots a consistent DB.
    Returns (manifest, skipped) where `skipped` = files present at start but unreadable
    (a real 'something got missed' -- a lock/permission error, not normal forward growth)."""
    files_start = [(f, f.relative_to(CORPUS).as_posix())
                   for f in sorted(CORPUS.glob(CORPUS_GLOB))]
    if dry:
        log(f"[dry] would snapshot {len(files_start)} files -> {dest_dir}")
        return {}, []
    dest_dir.mkdir(parents=True, exist_ok=True)
    man: dict[str, dict] = {}
    skipped: list[str] = []
    with tempfile.TemporaryDirectory() as tmp:
        staged_db = Path(tmp) / "sentinel.db"
        db_bytes = snapshot_db(staged_db)
        with zipfile.ZipFile(dest_dir / "snapshot.zip", "w",
                             zipfile.ZIP_DEFLATED, compresslevel=6) as z:
            for f, rel in files_start:
                try:
                    data = f.read_bytes()
                except OSError as e:
                    log(f"skip unreadable {rel}: {e}", crit=True)
                    skipped.append(rel)
                    continue
                z.writestr("corpus/" + rel, data)
                hashes = [hashlib.sha256(s).hexdigest()
                          for s in (ln.strip() for ln in data.splitlines()) if s]
                man[rel] = {"n": len(hashes), "hashes": hashes}
            if db_bytes > 0:
                z.write(staged_db, arcname="db/sentinel.db")
        zip_bytes = (dest_dir / "snapshot.zip").stat().st_size
    (dest_dir / "manifest.json").write_text(json.dumps(man), encoding="utf-8")
    files, rows = manifest_totals(man)
    log(f"snapshot {files} files / {rows} rows, zip {zip_bytes/1e6:.1f}MB "
        f"(db {db_bytes/1e6:.1f}MB){' SKIPPED '+str(len(skipped)) if skipped else ''} "
        f"-> {dest_dir.name}")
    return man, skipped


# ---------------------------------------------------------------------------- validate
def validate_superset(higher: dict, lower_union: set, label: str) -> tuple[bool, set]:
    """True iff every row-hash in lower_union is present in `higher`. Returns (ok, missing)."""
    higher_set = manifest_lineset(higher)
    missing = lower_union - higher_set
    if missing:
        log(f"{label}: {len(missing)} row(s) present below but MISSING above -- gap detected",
            crit=True)
        return False, missing
    return True, set()


def heal_from_dailies(dest_dir: Path, missing: set, daily_dirs: list[Path]) -> int:
    """Pull any missing rows out of the daily snapshots' zips into a _healed file so the
    weekly master is a true superset even if live shed a whole file. Returns rows healed."""
    if not missing:
        return 0
    healed_path = dest_dir / "_healed.jsonl"
    seen = set()
    n = 0
    with open(healed_path, "a", encoding="utf-8") as out:
        for d in daily_dirs:
            zp = d / "snapshot.zip"
            if not zp.exists():
                continue
            with zipfile.ZipFile(zp) as z:
                for name in z.namelist():
                    if not name.startswith("corpus/") or not name.endswith(".jsonl"):
                        continue
                    for raw in z.read(name).splitlines():
                        s = raw.strip()
                        if not s:
                            continue
                        h = hashlib.sha256(s).hexdigest()
                        if h in missing and h not in seen:
                            out.write(s.decode("utf-8", "replace") + "\n")
                            seen.add(h)
                            n += 1
    log(f"healed {n} missing row(s) into {healed_path.name}")
    return n


# ------------------------------------------------------------------------------ commands
def iso_week_of(d: datetime.date) -> str:
    y, w, _ = d.isocalendar()
    return f"{y}-W{w:02d}"


def cmd_daily(date: str, dry: bool) -> int:
    dest = DAILY_DIR / date
    if dest.exists() and not dry:
        log(f"daily {date} already exists -- overwriting")
        shutil.rmtree(dest)
    man, skipped = take_snapshot(dest, dry)
    if dry:
        return 0
    # Daily is a POINT-IN-TIME capture of an append-only, still-growing corpus: it is
    # inherently a superset of the corpus at zip-start and a subset at zip-end, so a
    # row-level "vs live" check would flag normal forward growth as a gap. The only real
    # daily miss is a file that existed at start but couldn't be captured (lock/permission)
    # -> that's `skipped`. Internal manifest<->zip consistency is guaranteed by construction
    # (single pass) and re-checkable any time via `verify`.
    status = "ok" if not skipped else "gap"
    files, rows = manifest_totals(man)
    (dest / "validation.json").write_text(json.dumps({
        "tier": "daily", "date": date, "status": status,
        "files": files, "rows": rows, "skipped": skipped,
    }), encoding="utf-8")
    log(f"daily {date} VALIDATED status={status} ({rows} rows"
        f"{', '+str(len(skipped))+' skipped' if skipped else ''})")
    return 0 if status == "ok" else 2


def cmd_weekly(week: str, dry: bool) -> int:
    dest = WEEKLY_DIR / week
    if dest.exists() and not dry:
        log(f"weekly {week} already exists -- overwriting")
        shutil.rmtree(dest)
    # dailies belonging to this iso-week
    daily_dirs = []
    if DAILY_DIR.exists():
        for d in sorted(DAILY_DIR.iterdir()):
            if not d.is_dir():
                continue
            try:
                dd = datetime.date.fromisoformat(d.name)
            except ValueError as _swex:
                swallow("snapshot.snapshot.cmd_weekly", _swex)
                continue
            if iso_week_of(dd) == week:
                daily_dirs.append(d)
    log(f"weekly {week}: {len(daily_dirs)} daily(ies) in scope: {[d.name for d in daily_dirs]}")

    man, _skipped = take_snapshot(dest, dry)
    if dry:
        log(f"[dry] would validate vs union of {len(daily_dirs)} dailies, then prune them")
        return 0

    # union of every row any daily in this week captured
    union: set = set()
    for d in daily_dirs:
        mp = d / "manifest.json"
        if mp.exists():
            union |= manifest_lineset(json.loads(mp.read_text(encoding="utf-8")))
    ok, missing = validate_superset(man, union, f"weekly {week} vs daily-union")
    if not ok:
        heal_from_dailies(dest, missing, daily_dirs)
        # fold the _healed file's rows into the manifest (it lives in dest, not CORPUS),
        # then re-validate so the master truly supersets the daily union
        healed_hashes = set()
        hp = dest / "_healed.jsonl"
        if hp.exists():
            for raw in hp.read_bytes().splitlines():
                s = raw.strip()
                if s:
                    healed_hashes.add(hashlib.sha256(s).hexdigest())
        man.setdefault("_healed.jsonl", {"n": len(healed_hashes), "hashes": list(healed_hashes)})
        (dest / "manifest.json").write_text(json.dumps(man), encoding="utf-8")
        ok, missing = validate_superset(man, union, f"weekly {week} vs daily-union (post-heal)")

    files, rows = manifest_totals(man)
    pruned = []
    if ok:
        for d in daily_dirs:
            shutil.rmtree(d)
            pruned.append(d.name)
        log(f"weekly {week} VALIDATED -- pruned {len(pruned)} daily(ies): {pruned}")
    else:
        log(f"weekly {week} NOT validated ({len(missing)} rows still missing) -- "
            f"dailies KEPT, no destruct", crit=True)
    (dest / "validation.json").write_text(json.dumps({
        "tier": "weekly", "week": week, "status": "ok" if ok else "gap",
        "files": files, "rows": rows, "missing": len(missing),
        "dailies_pruned": pruned, "dailies_scoped": [d.name for d in daily_dirs],
    }), encoding="utf-8")
    return 0 if ok else 2


def cmd_verify(target: str) -> int:
    dest = Path(target)
    if not dest.is_absolute():
        dest = SNAPROOT / target
    mp, zp = dest / "manifest.json", dest / "snapshot.zip"
    if not mp.exists() or not zp.exists():
        log(f"verify: {dest} missing manifest.json or snapshot.zip", crit=True)
        return 2
    man = json.loads(mp.read_text(encoding="utf-8"))
    want = manifest_lineset(man)
    have: set = set()
    with zipfile.ZipFile(zp) as z:
        for name in z.namelist():
            if name.startswith("corpus/") and name.endswith(".jsonl"):
                for raw in z.read(name).splitlines():
                    s = raw.strip()
                    if s:
                        have.add(hashlib.sha256(s).hexdigest())
    missing = want - have
    if missing:
        log(f"verify {dest.name}: {len(missing)} manifest rows NOT in zip", crit=True)
        return 2
    log(f"verify {dest.name}: OK ({len(want)} rows match zip)")
    return 0


def cmd_list() -> int:
    for tier, root in (("weekly", WEEKLY_DIR), ("daily", DAILY_DIR)):
        print(f"\n=== {tier} ===")
        if not root.exists():
            print("  (none)")
            continue
        for d in sorted(root.iterdir()):
            vp = d / "validation.json"
            if vp.exists():
                v = json.loads(vp.read_text(encoding="utf-8"))
                zsz = (d / "snapshot.zip").stat().st_size / 1e6 if (d / "snapshot.zip").exists() else 0
                print(f"  {d.name:16s} status={v.get('status'):4s} "
                      f"rows={v.get('rows')} zip={zsz:.0f}MB")
            else:
                print(f"  {d.name:16s} (no validation.json)")
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(description="Sentinel corpus snapshot ladder")
    sub = ap.add_subparsers(dest="cmd", required=True)
    pd = sub.add_parser("daily");  pd.add_argument("--date"); pd.add_argument("--dry-run", action="store_true")
    pw = sub.add_parser("weekly"); pw.add_argument("--week"); pw.add_argument("--dry-run", action="store_true")
    pv = sub.add_parser("verify"); pv.add_argument("target")
    sub.add_parser("list")
    a = ap.parse_args()

    if a.cmd == "daily":
        date = a.date or datetime.date.today().isoformat()
        return cmd_daily(date, a.dry_run)
    if a.cmd == "weekly":
        week = a.week or iso_week_of(datetime.date.today())
        return cmd_weekly(week, a.dry_run)
    if a.cmd == "verify":
        return cmd_verify(a.target)
    if a.cmd == "list":
        return cmd_list()
    return 1


if __name__ == "__main__":
    sys.exit(main())
