"""The parity harness on the command line.

    cd "Sentinel\\Azimuth"
    python -m gates list
    python -m gates describe --artefact strategy
    python -m gates selftest                       # the fault-injection proof
    python -m gates compare --artefact sensor \\
        --ref-jsonl  nt\\trend.jsonl      --ref-meta  nt\\trend.meta.json  --ref-label NT \\
        --cmp-jsonl  py\\trend.jsonl      --cmp-meta  py\\trend.meta.json  --cmp-label Azimuth \\
        --json verdict.json

Exit codes are the contract:  0 = PASS   1 = FAIL   2 = ABORT / could not run the test.
"""
from __future__ import annotations

import argparse
import json
import os
import sys

if __package__ in (None, ""):  # `python gates\__main__.py` as well as `python -m gates`
    sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    __package__ = "gates"

from .artefacts import describe, get, kinds  # noqa: E402
from .inject import prove, report  # noqa: E402
from .loaders import jsonl_side, load_meta, parquet_side, sqlite_side  # noqa: E402
from .parity import SpecError, run_gate  # noqa: E402

try:  # the console is cp1252; the harness prints ASCII, but a path in an error may not be
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception as _e:
    sys.stderr.write("[gates] stdout reconfigure unavailable: %s\n" % _e)


def _alias(text: str | None) -> dict:
    """--ref-alias net_score=netScore,bar_stamp=bar_ts"""
    if not text:
        return {}
    out = {}
    for part in text.split(","):
        if "=" not in part:
            raise SystemExit("--alias expects actual=canonical pairs, got %r" % part)
        a, b = part.split("=", 1)
        out[a.strip()] = b.strip()
    return out


def _build_side(prefix: str, args, default_label: str):
    label = getattr(args, prefix + "_label") or default_label
    meta = load_meta(getattr(args, prefix + "_meta")) if getattr(args, prefix + "_meta") else {}
    alias = _alias(getattr(args, prefix + "_alias"))
    jsonl = getattr(args, prefix + "_jsonl")
    parquet = getattr(args, prefix + "_parquet")
    db = getattr(args, prefix + "_sqlite")
    chosen = [x for x in (jsonl, parquet, db) if x]
    if len(chosen) != 1:
        raise SystemExit("give exactly one of --%s-jsonl / --%s-parquet / --%s-sqlite"
                         % (prefix, prefix, prefix))
    if jsonl:
        return jsonl_side(label, jsonl, meta=meta, alias=alias,
                          record=getattr(args, prefix + "_record"))
    if parquet:
        return parquet_side(label, parquet, meta=meta, alias=alias)
    sql = getattr(args, prefix + "_sql")
    if not sql:
        raise SystemExit("--%s-sqlite needs --%s-sql" % (prefix, prefix))
    return sqlite_side(label, db, sql, meta=meta, alias=alias)


def _side_args(p, prefix: str):
    p.add_argument("--%s-jsonl" % prefix)
    p.add_argument("--%s-parquet" % prefix)
    p.add_argument("--%s-sqlite" % prefix)
    p.add_argument("--%s-sql" % prefix, help="SELECT for --%s-sqlite (opened READ-ONLY)" % prefix)
    p.add_argument("--%s-meta" % prefix, help="JSON of run-level identity/provenance metadata")
    p.add_argument("--%s-label" % prefix)
    p.add_argument("--%s-alias" % prefix, help="actual=canonical,actual=canonical")
    p.add_argument("--%s-record" % prefix, default="lines", choices=["lines", "first-line"],
                   help="'first-line': the first line of each file IS the record (corpus convention)")


def cmd_list(args) -> int:
    print("artefact kinds registered with the parity harness")
    print()
    for k in kinds():
        s = get(k)
        print("  %-18s key (%s)" % (k, ", ".join(s.pair_keys)))
        print("  %-18s %s" % ("", s.doc))
    print()
    print("`python -m gates describe --artefact <kind>` for the full field list and tolerances.")
    return 0


def cmd_describe(args) -> int:
    print(describe(args.artefact))
    return 0


def cmd_selftest(args) -> int:
    results = prove([args.artefact] if args.artefact else None)
    print(report(results))
    return 0 if all(r["ok"] for r in results) else 1


def cmd_compare(args) -> int:
    spec = get(args.artefact)
    ref = _build_side("ref", args, "ref")
    cmp = _build_side("cmp", args, "cmp")
    tol = {}
    for t in args.tol or []:
        if "=" not in t:
            raise SystemExit("--tol expects field=value, got %r" % t)
        k, val = t.split("=", 1)
        tol[k.strip()] = float(val)
    v = run_gate(spec, ref, cmp, tol_overrides=tol,
                 check_identity=args.identity_check, check_provenance=args.provenance_check)
    print(v.to_text(show=args.show))
    if args.json:
        with open(args.json, "w", encoding="utf-8") as fh:
            json.dump(v.to_json(), fh, indent=1, default=str)
        print("\nverdict -> %s" % args.json)
    return v.exit_code


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(
        prog="gates", description="Sentinel Azimuth parity harness (SPEC §2, THE PARITY LAW)")
    sub = ap.add_subparsers(dest="cmd")

    p = sub.add_parser("list", help="the registered artefact kinds")
    p.set_defaults(fn=cmd_list)

    p = sub.add_parser("describe", help="one artefact's fields, tiers and tolerances")
    p.add_argument("--artefact", required=True)
    p.set_defaults(fn=cmd_describe)

    p = sub.add_parser("selftest", help="the fault-injection proof that the gates CAN fail")
    p.add_argument("--artefact", help="just this one")
    p.set_defaults(fn=cmd_selftest)

    p = sub.add_parser("compare", help="the gate")
    p.add_argument("--artefact", required=True)
    _side_args(p, "ref")
    _side_args(p, "cmp")
    p.add_argument("--tol", action="append",
                   help="field=value. DIAGNOSIS ONLY -- stamps DEGRADED on the verdict.")
    p.add_argument("--no-identity-check", dest="identity_check", action="store_false", default=True,
                   help="skip proving both sides read the same input (degrades the verdict)")
    p.add_argument("--no-provenance-check", dest="provenance_check", action="store_false",
                   default=True, help="skip requiring each side to name its implementation")
    p.add_argument("--show", type=int, default=10)
    p.add_argument("--json", help="write the machine-readable verdict here")
    p.set_defaults(fn=cmd_compare)

    a = ap.parse_args(argv)
    if not getattr(a, "fn", None):
        ap.print_help()
        return 2
    try:
        return a.fn(a)
    except SpecError as e:
        print("ABORT -- spec defect: %s" % e)
        return 2
    except (OSError, ImportError) as e:
        print("ABORT -- could not read a side: %s" % e)
        return 2


if __name__ == "__main__":
    sys.exit(main())
