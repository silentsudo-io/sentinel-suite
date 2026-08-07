#!/usr/bin/env python3
"""
ACCEPTANCE TEST for VOTE-VECTOR COMPLETENESS — does every voter the lane DECLARES actually reach the corpus?

    cd "Sentinel\\Lab"
    .\\.venv\\Scripts\\python.exe verify_votes.py            # last 3 days of council rows
    .\\.venv\\Scripts\\python.exe verify_votes.py --days 14
    .\\.venv\\Scripts\\python.exe verify_votes.py --json     # machine output (corpus_probe consumes this)

WHY THIS EXISTS
  On 2026-07-23 a clean-looking audition bake produced 1,866 rows that passed every existing check --
  schema 1.5, honest firePx, 0 dups, balanced direction, versions stamped -- and were still USELESS for
  their purpose: every single row carried 18 voters and NEVER BRK, FLUX or CVB, and brkUpper/brkLower
  were 0 throughout. Three of the bar-type-published voters had silently never reached the corpus, and
  brick-level (limit-vs-market) grading was impossible. Nobody noticed for a day; the cause was never
  established and the logs that would have shown it were destroyed by rotation before they were read.

  The cause does not have to be understood for the FAILURE to be caught. What makes this class of bug
  expensive is not that it happens -- it is that it is SILENT and is discovered weeks later, in analysis,
  after the compute has been spent. So this is a script, not a habit of looking.

  ⚠ THIS IS NOT roster_health. probe.py's roster_health reads the Council's LIVE roster line off
  sentinel.log; this reads what was actually WRITTEN TO DISK. In the 07-23 failure the roster line said
  "COMPLETE 20/20" while the recorded vote vector had 18 -- the live claim and the recorded artifact
  DISAGREED. Only the corpus can testify about the corpus.

CHECKS (per inst x bartype lane, on council row corpus)
  1. SEAM      -- the bar type's OWN published voter(s) must be present. Derived from the bar-type id, so
                  it needs NO config and cannot drift: 212201/212202 SentinelTBars/TbarsCount -> BRK,
                  212203 SentinelFlux -> FLUX, 212204 SentinelDrift -> BRK + CVB (Drift publishes both).
                  Coverage 0 on a seam voter is EXACTLY the 07-23 failure => CRIT.
  2. DECLARED  -- every voter in the lane's Roster.conf (resolved by the cascade
                  Models\\<inst>\\<bartag>\\ -> Models\\<inst>\\ -> Models\\) must appear as a KEY in the
                  vote vector. Absent entirely => CRIT; present on <90% of rows => WARN (intermittent
                  dropout, which a union-of-all-rows check would hide).
  3. BRK LEVELS-- on a BRICK bar type, brkUpper/brkLower must be populated, or limit-vs-market grading
                  (limitlab.py) is dead on arrival. <99% populated => CRIT. Flux is NOT a brick type and
                  is correctly exempt -- it has no brick boundaries to record.

  A voter recorded with value 0 COUNTS AS PRESENT. That is the whole point: an abstaining voter wrote
  "I looked, nothing to report", while a missing KEY means the seam never arrived. Those two are what
  the declared roster exists to distinguish, and conflating them is what hid this bug.

EXIT  0 = all lanes complete   1 = WARN (partial coverage, or too thin to judge)   2 = CRIT (missing data)
"""
from __future__ import annotations
import os, sys, glob, json, argparse, re
import datetime as dt
from collections import defaultdict
from lab_faults import swallow

HERE = os.path.dirname(os.path.abspath(__file__))
SENT = os.path.abspath(os.path.join(HERE, ".."))
# BOTH corpus trees: the local live one AND `_replay\`, where a bake pulled off a replay node lands
# before ingest. Scanning only the live tree would have missed exactly the corpus this gate exists for
# (the 07-23 legacy-node audition bake was pulled, not recorded here).
#
# Schema 1.5 ONLY, deliberately. 1.3/1.4 are the frozen contaminated-label record, and the older rows
# predate the vote vector entirely -- auditing them would report every voter "missing" on every row and
# bury the live signal in noise about history that is never going to change.
COUNCIL_DIRS = ([os.path.join(SENT, "Excursions", "council", v) for v in ("1.5",)] +
                [os.path.join(SENT, "Excursions", "_replay", "council", v) for v in ("1.5",)])
MODELS = os.path.join(SENT, "Models")

# Bar-type id -> the voter(s) that bar type PUBLISHES ITSELF. Authoritative source is the
# SentinelCore VoterCatalog (AddOns\SentinelCore.SystemBuilder.cs), which names each voter's
# source: "SentinelTBars (bar type)" / "SentinelFlux (bar type)" / "SentinelDrift (bar type)".
# Kept here as data because a bar type's own voter is the ONE expectation that needs no roster
# file -- a chart cannot run bar type 212203 and legitimately lack FLUX.
BAR_SEAM = {212201: {"BRK"}, 212202: {"BRK"}, 212203: {"FLUX"}, 212204: {"BRK", "CVB"}}
# Brick bar types latch a forming bar's upper/lower boundary, so every fire can record where the
# brick edges sat -- the precondition for offline limit-vs-market grading. Flux (212203) closes on
# accumulated order-flow imbalance, not a price boundary, so it has none. Absence there is CORRECT.
BRICK_TYPES = {212201, 212202, 212204}

# Voters whose ABSENCE is a settled decision, not a fault to go chase. Reported (a roster declaring a
# retired voter is real config drift worth cleaning) but with the resolution attached, so "X is missing"
# can never re-open a closed loop. EYE is NOT globally dead -- it votes on 100% of rows on some live GC
# lanes -- it is retired from the AUDITION and was never installed on legacy-node, so the fix is always to
# drop it from that lane's roster, never to re-add the sensor.
RETIRED = {"EYE": "  [EYE is RETIRED for audition lanes -- remove it from this roster; do NOT re-add the sensor]"}

PARTIAL_PCT = 0.90       # a declared voter below this coverage is intermittently dropping out
BRK_PCT     = 0.99       # brick lanes must carry levels on essentially every row
THIN_ROWS   = 30         # below this a lane cannot be judged; findings degrade to WARN


def bar_id(bartype: str):
    """'212204v20x10@AUD' -> 212204. Returns None if the tag is not a numeric-id bartag."""
    m = re.match(r"^(\d+)", str(bartype or ""))
    return int(m.group(1)) if m else None


def roster_for(inst: str, bartype: str):
    """Resolve the declared roster by the SAME cascade the Council uses: most specific first
    (Models\\<inst>\\<bartag>\\Roster.conf -> Models\\<inst>\\Roster.conf -> Models\\Roster.conf).
    Returns (set_of_tags, path) or (None, None) when no roster is reachable on THIS box -- which is
    normal when auditing a corpus pulled from another node, so it degrades to the seam check rather
    than inventing an expectation."""
    for p in (os.path.join(MODELS, inst, bartype, "Roster.conf"),
              os.path.join(MODELS, inst, "Roster.conf"),
              os.path.join(MODELS, "Roster.conf")):
        if not os.path.isfile(p):
            continue
        tags = set()
        try:
            with open(p, encoding="utf-8", errors="replace") as fh:
                for ln in fh:
                    ln = ln.split("#", 1)[0].strip()
                    if not ln:
                        continue
                    tag = ln.split()[0].strip()
                    if re.fullmatch(r"[A-Za-z][A-Za-z0-9_]*", tag):
                        tags.add(tag.upper())
        except OSError as _swex:
            swallow("verify_votes.roster_for", _swex)
            continue
        if tags:
            return tags, p
    return None, None


def scan(days: int):
    """Read the council row corpus into per-lane vote coverage. Read-only; never touches a corpus file.

    ⚠ THE WINDOW IS FILE MTIME (when the row was WRITTEN), never fireTime (the bar's timestamp).
    A replay bake writes rows whose fireTime is HISTORICAL -- the 07-23 audition bake replayed 07-10..07-17
    data, so a fireTime window would have silently dropped 100% of the very corpus this gate exists to
    audit, and reported "no rows" or, worse, a clean bill of health on the handful of live lanes that
    remained. Written-at is the only clock that means "this bake, now" for both live and replay.
    """
    cut_ts = (dt.datetime.now() - dt.timedelta(days=days)).timestamp()
    cutoff = dt.datetime.fromtimestamp(cut_ts).strftime("%Y-%m-%d %H:%M")
    lanes = defaultdict(lambda: {"rows": 0, "seen": defaultdict(int), "brk": 0, "files": 0})
    for d in COUNCIL_DIRS:
        for path in glob.glob(os.path.join(d, "*.jsonl")):
            touched = False
            try:
                if os.path.getmtime(path) < cut_ts:
                    continue
            except OSError as _swex:
                swallow("verify_votes.scan", _swex)
                continue
            try:
                with open(path, encoding="utf-8", errors="replace") as fh:
                    for ln in fh:
                        ln = ln.strip()
                        if not ln:
                            continue
                        try:
                            o = json.loads(ln)
                        except (json.JSONDecodeError, ValueError) as _swex:
                            swallow("verify_votes.scan#2", _swex)
                            continue
                        inst, bt = o.get("inst"), o.get("bartype")
                        if not inst or not bt:
                            continue
                        L = lanes[(inst, str(bt))]
                        L["rows"] += 1
                        touched = True
                        v = o.get("votes")
                        if isinstance(v, dict):
                            for k in v:
                                L["seen"][str(k).upper()] += 1
                        try:
                            if float(o.get("brkUpper") or 0) > 0 and float(o.get("brkLower") or 0) > 0:
                                L["brk"] += 1
                        except (TypeError, ValueError) as _swex:
                            swallow("verify_votes.scan#3", _swex)
            except OSError as _swex:
                swallow("verify_votes.scan#4", _swex)
                continue
            if touched:
                lanes[(inst, str(bt))]["files"] += 1
    return lanes, cutoff


def audit(days: int = 3):
    """The importable entry point. Returns (findings, lane_rows, cutoff).

    findings   list of dicts: lane / inst / bartype / kind / severity / detail / voters
    lane_rows  per-lane summary suitable for a health table
    """
    lanes, cutoff = scan(days)
    findings, summary = [], []

    for (inst, bt), L in sorted(lanes.items()):
        n = L["rows"]
        if not n:
            continue
        bid = bar_id(bt)
        declared, rpath = roster_for(inst, bt)
        seam = BAR_SEAM.get(bid, set())
        # The seam expectation is UNCONDITIONAL. A roster may legitimately omit a voter, but a chart
        # cannot run a bar type and lack the voter that bar type publishes -- so seam is unioned in,
        # never intersected. (The 07-23 Flux lane's roster correctly omitted BRK; that must not
        # excuse a missing FLUX.)
        expected = (declared or set()) | seam
        cover = {t: L["seen"].get(t, 0) / n for t in expected}
        missing = sorted(t for t, c in cover.items() if c == 0.0)
        partial = sorted(t for t, c in cover.items() if 0.0 < c < PARTIAL_PCT)
        extra = sorted(t for t in L["seen"] if t not in expected)
        thin = n < THIN_ROWS

        def sev(base):
            return "WARN" if thin else base

        for t in missing:
            why = "bar-type seam voter" if t in seam else "declared in %s" % (
                os.path.relpath(rpath, SENT) if rpath else "roster")
            hint = RETIRED.get(t, "")
            findings.append(dict(
                lane=f"{inst}.{bt}", inst=inst, bartype=bt, kind="vote_missing", severity=sev("CRIT"),
                voters=t, detail=f"{t} never reached the corpus ({why}); 0 of {n} rows carry the key" + hint))
        for t in partial:
            findings.append(dict(
                lane=f"{inst}.{bt}", inst=inst, bartype=bt, kind="vote_partial", severity="WARN",
                voters=t, detail=f"{t} present on only {100*cover[t]:.0f}% of {n} rows (intermittent dropout)"))

        brk_pct = L["brk"] / n
        if bid in BRICK_TYPES and brk_pct < BRK_PCT:
            findings.append(dict(
                lane=f"{inst}.{bt}", inst=inst, bartype=bt, kind="brk_levels", severity=sev("CRIT"),
                voters="", detail=(f"brkUpper/brkLower populated on only {100*brk_pct:.0f}% of {n} rows "
                                   f"-- limit-vs-market grading is not possible on this corpus")))
        if declared is None:
            findings.append(dict(
                lane=f"{inst}.{bt}", inst=inst, bartype=bt, kind="no_roster", severity="INFO", voters="",
                detail="no Roster.conf reachable on this box; checked the bar-type seam only"))

        summary.append(dict(
            lane=f"{inst}.{bt}", inst=inst, bartype=bt, rows=n, files=L["files"],
            expected=len(expected), present=len(expected) - len(missing),
            missing=",".join(missing), partial=",".join(partial), undeclared=",".join(extra),
            brk_pct=round(100 * brk_pct, 1), brick=1 if bid in BRICK_TYPES else 0, thin=1 if thin else 0,
            roster=os.path.relpath(rpath, SENT) if rpath else ""))

    return findings, summary, cutoff


def main():
    ap = argparse.ArgumentParser(description="Audit vote-vector completeness in the council row corpus.")
    ap.add_argument("--days", type=int, default=3, help="window = today + previous N-1 days (default 3)")
    ap.add_argument("--json", action="store_true", help="machine-readable output")
    a = ap.parse_args()

    findings, summary, cutoff = audit(a.days)
    if a.json:
        print(json.dumps(dict(cutoff=cutoff, summary=summary, findings=findings), indent=2))
    else:
        if not summary:
            print(f"NO COUNCIL ROWS since {cutoff}. Nothing to verify — bake, or widen --days.")
            return 1
        print(f"council row corpus since {cutoff}\n")
        print("%-26s %6s %5s %9s %7s  %s" % ("lane", "rows", "exp", "present", "brk%", "missing / partial"))
        for s in summary:
            gap = s["missing"] + (" ~" + s["partial"] if s["partial"] else "")
            print("%-26s %6d %5d %9d %6.0f%%  %s" % (
                s["lane"], s["rows"], s["expected"], s["present"], s["brk_pct"],
                gap if gap else "-- complete --"))
        if findings:
            print()
            for f in findings:
                print("  [%-4s] %-26s %s" % (f["severity"], f["lane"], f["detail"]))
        print()
        crit = sum(1 for f in findings if f["severity"] == "CRIT")
        warn = sum(1 for f in findings if f["severity"] == "WARN")
        print(f"{len(summary)} lane(s) · {crit} CRIT · {warn} WARN")

    if any(f["severity"] == "CRIT" for f in findings):
        return 2
    if any(f["severity"] == "WARN" for f in findings):
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
