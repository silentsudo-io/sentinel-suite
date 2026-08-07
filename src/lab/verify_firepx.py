#!/usr/bin/env python3
"""
ACCEPTANCE TEST for the honest-entry-price fix (recorder v2.2.0 / v1.2.0, schema 1.5 / cand.2).

    cd "Sentinel\\Lab"
    .\\.venv\\Scripts\\python.exe verify_firepx.py

WHY THIS EXISTS
  Until 2026-07-22 the recorders set `FirePx = Close[0]`, which on every Sentinel bars type is the HEIKIN-ASHI
  SYNTHETIC close -- a price that NEVER TRADED -- while the tick path was always the real tape. FirePx is the
  reference for MFE / MAE / barrier / firstTouch, so every label in the corpus was ~9 ticks optimistic
  (recorded "target-first" 52.3% vs 21.1% TRUE; labels disagreed with truth on 44.6% of fires).

  The fix is only real if a FRESH sidecar's `firePx` reconciles to the first traded price. That is a
  measurement, not an opinion -- so it is a script, not a checklist item. Compiling clean proves nothing here.

PASS CRITERIA (per bar type, on ctick.4 sidecars only)
  1. median |firePx - px[0]| <= 1 tick          -- the entry is a real price
  2. mean dir*(firePx - px[0]) within +/-1 tick -- no systematic directional offset (the HA fingerprint is gone)
  3. pxSrc is a REAL-PRICE source on >= 99% of fires ("last" or "firsttick"; "barclose" = fallback, not tradeable)
Reference (pre-fix, GC TBars, n=3710): mean -9.36t, |median| 8t, 79.7% adverse. Anything resembling that = FAIL.

!! CRITERION 1/2 ARE TAUTOLOGICAL FOR pxSrc="firsttick" ROWS (recorder >= v2.2.1 adopts the ms==0 tick as the
   entry, so firePx == px[0] BY CONSTRUCTION). They still bite on "last" rows. The test that does NOT go
   circular is the DIRECTIONAL SIGN MIX reported below: the pre-fix defect was systematic adversity in BOTH
   directions (the Heikin-Ashi fingerprint), so a lane whose "last"-sourced rows are ~50/50 long-vs-short
   adverse is genuinely fixed, whereas one skewed >75% adverse is not -- no matter what the mean says.
"""
from __future__ import annotations
import os, sys, glob, json
from collections import defaultdict
from lab_faults import swallow

TICK = {"GC": 0.1, "MGC": 0.1, "SI": 0.005, "CL": 0.01, "ES": 0.25, "MES": 0.25,
        "NQ": 0.25, "MNQ": 0.25, "YM": 1.0, "ZN": 0.015625, "ZB": 0.03125}
SENT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
DIRS = [os.path.join(SENT, "Excursions", "council", "ticks"),
        os.path.join(SENT, "Excursions", "candidates", "ticks"),
        os.path.join(SENT, "Excursions", "_replay", "council", "ticks")]
FIXED = {"ctick.4"}          # schemas that carry the honest entry price

# Minimum recVer that implements the ms==0 ENTRY BACKFILL, per recorder family (2.x = excursion,
# 1.x = candidate). Compared as a VERSION, never matched against a literal set: a hardcoded set of
# "known good" versions silently EXCLUDES every future recorder, which is the same trap as a resume
# checkpoint that skips the work a schema bump made necessary. New versions must opt IN by default.
BACKFILL_MIN = {2: (2, 2, 1), 1: (1, 2, 1)}


def ver(s):
    try:
        return tuple(int(x) for x in str(s).split("."))
    except Exception as _swex:
        swallow("verify_firepx.ver", _swex)
        return None


def has_backfill(recver):
    v = ver(recver)
    if not v or len(v) < 3:
        return False
    lo = BACKFILL_MIN.get(v[0])
    return bool(lo) and v >= lo


def med(v):
    s = sorted(v)
    n = len(s)
    return 0.0 if not n else (s[n // 2] if n % 2 else 0.5 * (s[n // 2 - 1] + s[n // 2]))


def main():
    every = "--all" in sys.argv
    groups, seen_old, skipped_recver = defaultdict(list), defaultdict(int), defaultdict(int)
    for d in DIRS:
        for p in glob.glob(os.path.join(d, "*.jsonl")):
            try:
                with open(p, encoding="utf-8") as fh:
                    h = json.loads(fh.readline())
                    first = None
                    for ln in fh:
                        ln = ln.strip()
                        if not ln:
                            continue
                        o = json.loads(ln)
                        if "px" in o:
                            first = float(o["px"]); break
            except Exception as _swex:
                swallow("verify_firepx.main", _swex)
                continue
            sch = str(h.get("schema", ""))
            if sch not in FIXED:
                seen_old[sch] += 1
                continue
            rv = str(h.get("recVer", ""))
            if not every and not has_backfill(rv):
                skipped_recver[rv] += 1
                continue
            tk = TICK.get(str(h.get("inst", "")))
            if not tk or first is None or h.get("dir") is None:
                continue
            # brk sanity (recVer >= 2.3.0 / 1.3.0): firePx must sit BETWEEN the forming bar's latched
            # boundaries. If it does not, the recorder captured the CLOSED bar's levels, not the forming
            # one's -- an ordering assumption, so it gets measured rather than trusted.
            up, dn = float(h.get("brkUpper", 0) or 0), float(h.get("brkLower", 0) or 0)
            brk = None if (up <= 0 or dn <= 0) else (dn - 1e-9 <= float(h["firePx"]) <= up + 1e-9)
            groups[(h.get("inst"), h.get("bartype"))].append(
                (int(h["dir"]) * (float(h["firePx"]) - first) / tk, str(h.get("pxSrc", "?")), brk))

    if seen_old:
        print("pre-fix sidecars skipped (not an error - they are the frozen record):")
        for k, v in sorted(seen_old.items()):
            print("   %-10s %6d" % (k or "<none>", v))
    if skipped_recver:
        print("pre-BACKFILL ctick.4 recorders skipped (pass --all to include):")
        for k, v in sorted(skipped_recver.items()):
            print("   recVer %-8s %6d" % (k or "<none>", v))
    if not groups:
        print("\nNO ctick.4 SIDECARS FROM A BACKFILL-CAPABLE RECORDER YET.")
        print("Either the build is compiled but not LOADED (F5), or nothing has fired since.")
        print("Do: F5 in the NinjaScript Editor, let a live/replay chart fire, then re-run.")
        return 2

    print("\n%-6s %-16s %6s   %9s %9s   %6s %6s   %-9s  %s" %
          ("inst", "bartype", "n", "mean(t)", "|med|(t)", "real%", "bfill%", "adverse", "verdict"))
    worst = 0
    REAL = ("last", "firsttick")          # both denote a price that actually traded
    for (inst, bt), rows in sorted(groups.items(), key=lambda kv: -len(kv[1])):
        e = [r[0] for r in rows]
        n = len(e)
        mean = sum(e) / n
        amed = med([abs(x) for x in e])
        preal = sum(1 for r in rows if r[1] in REAL) / n
        pbf = sum(1 for r in rows if r[1] == "firsttick") / n

        # THE NON-CIRCULAR CHECK. Backfilled rows have firePx == px[0] by construction, so only the
        # "last"-sourced rows can testify. The pre-fix defect was adversity in BOTH directions; if those
        # rows are ~50/50 the Heikin-Ashi fingerprint is gone, if they skew hard adverse it is not.
        lastrows = [r[0] for r in rows if r[1] == "last"]
        nz = [x for x in lastrows if abs(x) > 1e-9]
        padv = (sum(1 for x in nz if x < 0) / len(nz)) if nz else float("nan")
        adv_s = "n/a" if nz == [] else ("%.0f%% (n=%d)" % (100 * padv, len(nz)))
        skewed = bool(nz) and len(nz) >= 20 and padv > 0.75

        ok = (amed <= 1.0) and (abs(mean) <= 1.0) and (preal >= 0.99) and not skewed
        worst = max(worst, 0 if ok else (1 if n < 30 else 2))
        note = "PASS" if ok else ("FAIL" if n >= 30 else "thin (n<30) - bake more")
        if not ok:
            why = []
            if amed > 1.0:  why.append("firePx is not a traded price")
            if abs(mean) > 1.0: why.append("systematic directional offset remains")
            if preal < 0.99: why.append("entry fell back to the untradeable bar close (pxSrc=barclose)")
            if skewed: why.append("HA fingerprint persists: 'last' rows skew %.0f%% adverse" % (100 * padv))
            note += " - " + "; ".join(why)
        brks = [r[2] for r in rows if r[2] is not None]
        if brks:
            inb = sum(1 for b in brks if b) / len(brks)
            note += "  |  brk %.0f%% in-range (n=%d)%s" % (
                100 * inb, len(brks), "" if inb >= 0.95 else "  <-- CLOSED bar's levels, not the forming one's")
        print("%-6s %-16s %6d   %+9.2f %9.1f   %5.0f%% %5.0f%%   %-9s  %s"
              % (inst, bt, n, mean, amed, 100 * preal, 100 * pbf, adv_s, note))

    print("\nreference (PRE-fix GC TBars, n=3710): mean -9.36  median -8.0  |med| 8.0  -> anything like that = FAIL")
    return 0 if worst == 0 else worst


if __name__ == "__main__":
    sys.exit(main())
