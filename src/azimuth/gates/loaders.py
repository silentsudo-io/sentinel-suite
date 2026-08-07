"""loaders — turn one column's output into a `Side`.

A loader's whole job is to hand `run_gate` rows and metadata WITHOUT losing anything on the way.
The rule inherited from `gate3.load_side` is the important one: **a file that could not be read is
counted and named, never skipped quietly.** A side that silently dropped forty rows otherwise
presents as a clean smaller set, and the diff blames the port instead of the loader.

Four sources, because that is what the two columns actually emit:

    rows_side      an in-memory list (a Python port's output, and every fault-injection proof)
    jsonl_side     the corpus / any JSONL. `record="first-line"` is the Sentinel corpus
                   convention where the FIRST line of a file is the record and the rest is the
                   tick path (gate3.read_header).
    sqlite_side    `sentinel.db`, opened READ-ONLY through a mode=ro URI. This harness never
                   writes to the corpus warehouse.
    parquet_side   the §3.1 tape and anything else Parquet (needs pyarrow).

PROVENANCE IS NOT OPTIONAL (§3.1). `tape_meta()` lifts a tape sidecar into the identity keys the
bar-type spec demands, so "the two sides read the same tape" is proven by a sha256 rather than
asserted by whoever ran the command.
"""
from __future__ import annotations

import glob as _glob
import json
import os
import sqlite3

from .parity import Side, swallow

__all__ = ["rows_side", "jsonl_side", "sqlite_side", "parquet_side", "tape_meta", "load_meta"]


def rows_side(label: str, rows, meta: dict | None = None, alias: dict | None = None,
              origin: str = "in-memory") -> Side:
    return Side(label=label, rows=[dict(r) for r in rows], meta=dict(meta or {}),
                origin=origin, alias=dict(alias or {}))


def load_meta(path: str) -> dict:
    """Read a metadata JSON. A missing file is an empty dict; a CORRUPT one is an exception.

    The distinction matters: "you did not give me metadata" is an operator choice the gate will
    abort on with a clear message, while "your metadata is unparseable" is a defect that must not
    be smoothed into the same silence.
    """
    if not os.path.isfile(path):
        return {}
    with open(path, "r", encoding="utf-8") as fh:
        return json.load(fh)


def tape_meta(sidecar_path: str) -> dict:
    """Identity metadata from a §3.1 tape sidecar (`<session_date>.meta.json`).

    Maps the sidecar's own names onto the identity keys the specs declare. A tape file without
    its sidecar is not admissible to a gate (§3.1), which here means: no sidecar, no
    `tape_sha256`, and the gate ABORTS on the missing identity key rather than comparing two
    things it cannot prove came from one tape.
    """
    m = load_meta(sidecar_path)
    if not m:
        raise FileNotFoundError(
            "no tape sidecar at %s. §3.1: a tape file without its sidecar is not admissible to a "
            "gate -- `replay.csv` is 61 GB of data nobody can now say the origin of." % sidecar_path)
    out = dict(m)
    if "source_file_sha256" in m and "tape_sha256" not in m:
        out["tape_sha256"] = m["source_file_sha256"]
    if "session_date" in m and "session" not in m:
        out["session"] = m["session_date"]
    return out


def _iter_files(path: str, pattern: str = "**/*.jsonl"):
    if os.path.isdir(path):
        for p in sorted(_glob.glob(os.path.join(path, pattern), recursive=True)):
            yield p
    elif any(ch in path for ch in "*?["):
        for p in sorted(_glob.glob(path, recursive=True)):
            yield p
    else:
        yield path


def jsonl_side(label: str, path: str, *, meta: dict | None = None, alias: dict | None = None,
               record: str = "lines", kinds: set | None = None,
               meta_name: str = "_side.json", pattern: str = "**/*.jsonl") -> Side:
    """Rows from a JSONL file, a directory of them, or a glob.

    record="lines"       every line is a row (a Python port's dump)
    record="first-line"  the first line of each file IS the record, the rest is its tick path
                         (the Sentinel corpus convention -- gate3.read_header)
    kinds                if given, a record whose `kind` is not in the set is not a row for this
                         gate. Unlike a parse failure, this is a filter and is not counted as
                         unreadable.
    """
    if record not in ("lines", "first-line"):
        raise ValueError("record must be 'lines' or 'first-line', got %r" % record)
    rows, unreadable, seen = [], [], set()
    root = path if os.path.isdir(path) else os.path.dirname(os.path.abspath(path))

    for p in _iter_files(path, pattern):
        rp = os.path.realpath(p)
        if rp in seen or os.path.basename(p) == meta_name:
            continue
        seen.add(rp)
        rel = os.path.relpath(p, root) if root else p
        try:
            with open(p, "r", encoding="utf-8") as fh:
                lines = [fh.readline()] if record == "first-line" else fh.readlines()
        except OSError as e:
            swallow("gates.jsonl_open", e, rel)
            unreadable.append(rel)
            continue
        got = 0
        for ln, line in enumerate(lines, 1):
            if not line.strip():
                continue
            try:
                o = json.loads(line)
            except json.JSONDecodeError as e:
                swallow("gates.jsonl_parse", e, "%s:%d" % (rel, ln))
                unreadable.append("%s:%d" % (rel, ln))
                continue
            if not isinstance(o, dict):
                unreadable.append("%s:%d (not an object)" % (rel, ln))
                continue
            if kinds is not None and o.get("kind") not in kinds:
                continue
            o["_file"] = rel
            rows.append(o)
            got += 1
        if got == 0 and record == "first-line" and rel not in unreadable:
            unreadable.append("%s (no usable record)" % rel)

    side_meta = dict(meta or {})
    if root:
        for k, v in load_meta(os.path.join(root, meta_name)).items():
            side_meta.setdefault(k, v)
    s = Side(label=label, rows=rows, meta=side_meta, origin=path, alias=dict(alias or {}))
    s.unreadable = unreadable
    return s


def sqlite_side(label: str, db_path: str, sql: str, params=(), *,
                meta: dict | None = None, alias: dict | None = None) -> Side:
    """Rows from a SQLite database, opened READ-ONLY.

    `mode=ro` + `uri=True` is not a convention here, it is the guarantee: `sentinel.db` is the
    9.2 GB corpus warehouse and a parity harness has no business writing a byte of it. A write
    attempted through this connection raises rather than succeeding quietly.
    """
    if not os.path.isfile(db_path):
        raise FileNotFoundError("no database at %s" % db_path)
    uri = "file:%s?mode=ro" % db_path.replace("\\", "/").replace("?", "%3f").replace("#", "%23")
    con = sqlite3.connect(uri, uri=True, timeout=30.0)
    try:
        con.row_factory = sqlite3.Row
        rows = [dict(r) for r in con.execute(sql, params).fetchall()]
    finally:
        con.close()
    return Side(label=label, rows=rows, meta=dict(meta or {}),
                origin="%s :: %s" % (os.path.basename(db_path), " ".join(sql.split())[:120]),
                alias=dict(alias or {}))


def parquet_side(label: str, path: str, *, columns=None, meta: dict | None = None,
                 alias: dict | None = None, sidecar: str | None = None) -> Side:
    """Rows from a Parquet file (the §3.1 tape, or a port's dumped bars).

    pyarrow is imported here rather than at module import so the rest of the harness runs on a box
    without it. A missing pyarrow raises with the install line -- it never degrades to "no rows",
    which would present as an empty side and abort with a misleading reason.
    """
    try:
        import pyarrow.parquet as pq  # noqa: PLC0415
    except ImportError as e:
        raise ImportError("parquet_side needs pyarrow (pip install pyarrow): %s" % e) from e
    table = pq.read_table(path, columns=columns)
    rows = table.to_pylist()
    side_meta = dict(meta or {})
    if sidecar:
        for k, v in tape_meta(sidecar).items():
            side_meta.setdefault(k, v)
    return Side(label=label, rows=rows, meta=side_meta, origin=path, alias=dict(alias or {}))
