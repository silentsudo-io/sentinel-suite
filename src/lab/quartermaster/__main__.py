"""Quartermaster CLI — catalog / report / verify.

    python -m quartermaster catalog [--root db\\replay] [--db catalog.db]   # (re)build the inventory
    python -m quartermaster report  [--symbol GC] [--kind expiry] [--holes] # coverage + holes
    python -m quartermaster verify  [--symbol GC]                           # list flagged files

Run from Sentinel\\Lab with its .venv:  .venv\\Scripts\\python -m quartermaster ...
"""
from __future__ import annotations
import argparse, sqlite3, json, sys
from lab_faults import swallow
try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")   # Windows console is cp1252; avoid encode crashes
except Exception as _swex:
    swallow("quartermaster.__main__.module", _swex)
from . import catalog as C


def _gb(n):    return f"{(n or 0)/1e9:8.2f} GB"
def _mb(n):    return f"{(n or 0)/1e6:8.1f} MB"


def cmd_catalog(a):
    con = C.connect(a.db)
    n_files, n_inst = C.rebuild(con, a.root)
    flagged = con.execute("SELECT COUNT(*) FROM nrd_file WHERE ok=0").fetchone()[0]
    tot = con.execute("SELECT COUNT(*), SUM(bytes) FROM nrd_file").fetchone()
    holes = con.execute("SELECT SUM(n_holes) FROM coverage").fetchone()[0] or 0
    print(f"cataloged {n_files} .nrd files across {n_inst} instrument folders")
    print(f"  root      : {a.root}")
    print(f"  db        : {a.db}")
    print(f"  on disk   : {tot[0]} files, {_gb(tot[1])}")
    print(f"  flagged   : {flagged}   candidate holes: {holes}")
    con.close()


def cmd_report(a):
    con = C.connect(a.db)
    q = "SELECT instrument,symbol,kind,contract,first_date,last_date,n_sessions,n_expected,n_holes,n_flagged,total_bytes,holes_json FROM coverage"
    where, args = [], []
    if a.symbol: where.append("symbol=?"); args.append(a.symbol.upper())
    if a.kind:   where.append("kind=?");   args.append(a.kind)
    if where: q += " WHERE " + " AND ".join(where)
    q += " ORDER BY symbol, kind, first_date"
    rows = con.execute(q, args).fetchall()
    if not rows:
        print("(no matching instruments — run `catalog` first?)"); con.close(); return

    print(f"{'INSTRUMENT':<22}{'KIND':<11}{'RANGE':<25}{'SESS':>6}{'EXP':>6}{'HOLE':>6}{'FLAG':>6}   SIZE")
    print("-" * 100)
    tS = tE = tH = tF = tB = 0
    for r in rows:
        (inst, sym, kind, contract, fd, ld, ns, ne, nh, nf, tb, hj) = r
        rng = f"{fd or '?'} → {ld or '?'}"
        print(f"{inst:<22}{kind:<11}{rng:<25}{ns:>6}{ne:>6}{nh:>6}{nf:>6}   {_mb(tb)}")
        if a.holes and nh:
            hs = json.loads(hj or "[]")
            print(f"    holes ({nh}, excl Sat; may incl. holidays): {', '.join(hs[:40])}"
                  + (" …" if nh > 40 else ""))
        tS += ns; tE += ne; tH += nh; tF += nf; tB += tb
    print("-" * 100)
    print(f"{'TOTAL ('+str(len(rows))+' inst)':<22}{'':<11}{'':<25}{tS:>6}{tE:>6}{tH:>6}{tF:>6}   {_gb(tB)}")
    print("\nnote: holes exclude Saturdays but NOT market holidays — treat as CANDIDATE gaps to eyeball.")
    con.close()


def cmd_verify(a):
    con = C.connect(a.db)
    q = "SELECT reason, COUNT(*), SUM(bytes) FROM nrd_file WHERE ok=0"
    args = []
    if a.symbol: q += " AND symbol=?"; args.append(a.symbol.upper())
    q += " GROUP BY reason ORDER BY 2 DESC"
    rows = con.execute(q, args).fetchall()
    if not rows:
        print("no flagged files ✓"); con.close(); return
    print(f"{'REASON':<12}{'COUNT':>8}{'BYTES':>14}")
    for reason, cnt, b in rows:
        print(f"{reason or '(none)':<12}{cnt:>8}{_mb(b):>14}")
    # sample the worst offenders
    print("\nsample flagged files:")
    s = "SELECT instrument, session_date, bytes, reason FROM nrd_file WHERE ok=0"
    if a.symbol: s += " AND symbol=?"
    s += " ORDER BY reason, instrument, session_date LIMIT 30"
    for inst, d, b, reason in con.execute(s, args).fetchall():
        print(f"  {inst:<22}{d or '????':<12}{b:>12}  {reason}")
    con.close()


def main():
    p = argparse.ArgumentParser(prog="quartermaster", description="Sentinel Quartermaster — replay-data catalog")
    p.add_argument("--db", default=C.DEFAULT_DB, help="catalog.db path")
    sub = p.add_subparsers(dest="cmd", required=True)

    pc = sub.add_parser("catalog", help="(re)build inventory from db\\replay")
    pc.add_argument("--root", default=C.DEFAULT_ROOT, help="db\\replay path")
    pc.set_defaults(fn=cmd_catalog)

    pr = sub.add_parser("report", help="coverage + holes")
    pr.add_argument("--symbol"); pr.add_argument("--kind", choices=["expiry", "continuous", "other"])
    pr.add_argument("--holes", action="store_true", help="list the missing dates")
    pr.set_defaults(fn=cmd_report)

    pv = sub.add_parser("verify", help="list integrity-flagged files")
    pv.add_argument("--symbol")
    pv.set_defaults(fn=cmd_verify)

    a = p.parse_args()
    a.fn(a)


if __name__ == "__main__":
    main()
