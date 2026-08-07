"""Quartermaster catalog core — walk db\\replay (filenames only), verify, and roll coverage.

The dates are IN the filename (YYYYMMDD.nrd), so cataloging never opens a .nrd binary — a full
scan of a 100+ GB db\\replay is a filename/stat sweep, seconds not minutes. THIS owns catalog.db;
it never touches the .nrd files, NT, or the corpus (sentinel.db) — pure read of the shelf.

Instrument-folder grammar (NT writes db\\replay\\<Instrument.FullName>\\YYYYMMDD.nrd):
    "GC 02-26"           -> expiry     symbol=GC  contract="02-26"
    "NQ H26"             -> expiry     symbol=NQ  contract="H26"   (letter-code)
    "YM 2026 Continuous" -> continuous symbol=YM  contract="2026 Continuous"
    "ES ##-##"           -> other      symbol=ES  contract="##-##" (NT placeholder folder)
"""
from __future__ import annotations
import os, re, sqlite3, json, statistics
import datetime as dt
from lab_faults import swallow

HERE = os.path.dirname(os.path.abspath(__file__))
LAB  = os.path.abspath(os.path.join(HERE, ".."))
SENT = os.path.abspath(os.path.join(HERE, "..", ".."))          # ...\NinjaTrader 8\Sentinel
NT8  = os.path.abspath(os.path.join(SENT, ".."))                # ...\NinjaTrader 8

DEFAULT_ROOT = os.path.join(NT8, "db", "replay")
DEFAULT_DB   = os.path.join(SENT, "Quartermaster", "catalog.db")

SHORT_FRAC = 0.15        # a weekday file below this * weekday-median (per symbol/kind) is "short" (suspect truncation)
MIN_BYTES  = 1024        # anything at/under this (and non-empty) is "tiny"
HOLE_CAP   = 300         # max hole dates stored per instrument (keeps holes_json bounded)

SCHEMA = """
CREATE TABLE IF NOT EXISTS nrd_file (
    path          TEXT PRIMARY KEY,     -- absolute path = stable id
    instrument    TEXT,                 -- full folder name (NT's key)
    symbol        TEXT,                 -- first token
    kind          TEXT,                 -- expiry | continuous | other
    contract      TEXT,                 -- remainder after symbol
    session_date  TEXT,                 -- YYYY-MM-DD parsed from filename (NULL if unparseable)
    bytes         INTEGER,
    mtime_utc     TEXT,
    ok            INTEGER,              -- 1 pass verify / 0 flagged
    reason        TEXT,                 -- '' | empty | tiny | short | baddate | saturday
    provider      TEXT,                 -- provenance (procurement head fills; NULL for disk-found)
    nt_version    TEXT,
    fetched_utc   TEXT,
    cataloged_utc TEXT
);
CREATE INDEX IF NOT EXISTS ix_nrd_sym  ON nrd_file(symbol, kind, session_date);
CREATE INDEX IF NOT EXISTS ix_nrd_inst ON nrd_file(instrument, session_date);
CREATE INDEX IF NOT EXISTS ix_nrd_flag ON nrd_file(ok);

CREATE TABLE IF NOT EXISTS coverage (
    instrument   TEXT PRIMARY KEY,
    symbol       TEXT, kind TEXT, contract TEXT,
    first_date   TEXT, last_date TEXT,
    n_sessions   INTEGER,      -- present .nrd files
    n_expected   INTEGER,      -- trading days in [first,last] excl Saturdays (holidays NOT modeled)
    n_holes      INTEGER,      -- expected-but-missing (candidate holes; may include market holidays)
    holes_json   TEXT,         -- up to HOLE_CAP missing YYYY-MM-DD
    n_flagged    INTEGER,      -- files failing verify
    total_bytes  INTEGER,
    updated_utc  TEXT
);
"""

_EXPIRY_NUM = re.compile(r"^\d{2}-\d{2}$")                       # 02-26
_EXPIRY_LTR = re.compile(r"^[FGHJKMNQUVXZ]\d{1,2}$")            # H26 / H6


def connect(db_path: str) -> sqlite3.Connection:
    os.makedirs(os.path.dirname(db_path), exist_ok=True)
    con = sqlite3.connect(db_path)
    con.executescript(SCHEMA)
    return con


def classify(folder: str):
    """(symbol, kind, contract) from an instrument folder name."""
    sym = folder.split(" ", 1)[0]
    rest = folder[len(sym):].strip()
    low = folder.lower()
    if "continuous" in low:
        kind = "continuous"
    elif _EXPIRY_NUM.match(rest) or _EXPIRY_LTR.match(rest):
        kind = "expiry"
    else:
        kind = "other"                                          # placeholders ("ES ##-##"), unknowns
    return sym, kind, rest


def _parse_date(stem: str):
    if len(stem) == 8 and stem.isdigit():
        try:
            return dt.date(int(stem[0:4]), int(stem[4:6]), int(stem[6:8]))
        except ValueError as _swex:
            swallow("quartermaster.catalog._parse_date", _swex)
            return None
    return None


def _iso_utc(ts: float) -> str:
    return dt.datetime.utcfromtimestamp(ts).strftime("%Y-%m-%dT%H:%M:%SZ")


def scan(root: str):
    """Yield raw file records (no verify yet). Filename + stat only."""
    if not os.path.isdir(root):
        raise FileNotFoundError(f"replay root not found: {root}")
    for entry in os.scandir(root):
        if not entry.is_dir():
            continue
        folder = entry.name
        sym, kind, contract = classify(folder)
        for f in os.scandir(entry.path):
            if not f.is_file() or not f.name.lower().endswith(".nrd"):
                continue
            stem = f.name[:-4]
            d = _parse_date(stem)
            st = f.stat()
            yield {
                "path": f.path, "instrument": folder, "symbol": sym, "kind": kind,
                "contract": contract, "session_date": d.isoformat() if d else None,
                "_date": d, "bytes": st.st_size, "mtime_utc": _iso_utc(st.st_mtime),
            }


def _verify(records):
    """Set ok/reason. 'short' uses a per-(symbol,kind) weekday median; Sundays are legitimately
    small (partial session) so they're never flagged short."""
    # weekday-median bytes per (symbol,kind), for the short-file band
    groups: dict = {}
    for r in records:
        d = r["_date"]
        if d is not None and d.weekday() < 5 and r["bytes"] > MIN_BYTES:   # Mon-Fri only
            groups.setdefault((r["symbol"], r["kind"]), []).append(r["bytes"])
    med = {k: statistics.median(v) for k, v in groups.items() if v}

    for r in records:
        d = r["_date"]; b = r["bytes"]; reason = ""
        if d is None:
            reason = "baddate"
        elif b == 0:
            reason = "empty"
        elif b <= MIN_BYTES:
            reason = "tiny"
        elif d.weekday() == 5:
            reason = "saturday"                                  # downloader skips Sat — a Sat file is odd
        else:
            m = med.get((r["symbol"], r["kind"]))
            # Sundays (weekday 6) are expected-small → exempt from the short band
            if m and d.weekday() != 6 and b < SHORT_FRAC * m:
                reason = "short"
        r["ok"] = 0 if reason else 1
        r["reason"] = reason
    return records


def _roll_coverage(records):
    """Per-instrument coverage + candidate holes (expected trading days excl Saturdays)."""
    by_inst: dict = {}
    for r in records:
        by_inst.setdefault(r["instrument"], []).append(r)
    rows = []
    now = dt.datetime.utcnow().strftime("%Y-%m-%dT%H:%M:%SZ")
    for inst, rs in by_inst.items():
        # coverage is about REAL trading sessions → exclude Saturday junk (tiny placeholder .nrd
        # NT drops on Saturdays); they stay visible/flagged in nrd_file + `verify`, just not here.
        dated = sorted([r for r in rs if r["_date"] is not None and r["_date"].weekday() != 5],
                       key=lambda r: r["_date"])
        present = {r["_date"] for r in dated}
        total_bytes = sum(r["bytes"] for r in rs)                 # all bytes on disk (incl. Saturdays)
        n_flagged = sum(1 for r in dated if r["ok"] == 0)         # real quality flags only (not saturday)
        if dated:
            first, last = dated[0]["_date"], dated[-1]["_date"]
            expected, holes = [], []
            day = first
            while day <= last:
                if day.weekday() != 5:                          # exclude Saturday
                    expected.append(day)
                    if day not in present:
                        holes.append(day.isoformat())
                day += dt.timedelta(days=1)
            n_expected = len(expected)
        else:
            first = last = None; n_expected = 0; holes = []
        s = rs[0]
        rows.append({
            "instrument": inst, "symbol": s["symbol"], "kind": s["kind"], "contract": s["contract"],
            "first_date": first.isoformat() if first else None,
            "last_date": last.isoformat() if last else None,
            "n_sessions": len(present), "n_expected": n_expected, "n_holes": len(holes),
            "holes_json": json.dumps(holes[:HOLE_CAP]), "n_flagged": n_flagged,
            "total_bytes": total_bytes, "updated_utc": now,
        })
    return rows


def rebuild(con: sqlite3.Connection, root: str):
    """Full catalog rebuild from disk. Preserves any provenance columns already stored."""
    records = list(scan(root))
    _verify(records)
    cov = _roll_coverage(records)
    now = dt.datetime.utcnow().strftime("%Y-%m-%dT%H:%M:%SZ")

    # preserve provenance the procurement head may have written
    prov = {p: (pr, nv, fu) for p, pr, nv, fu in
            con.execute("SELECT path, provider, nt_version, fetched_utc FROM nrd_file")}

    con.execute("DELETE FROM nrd_file")
    con.execute("DELETE FROM coverage")
    con.executemany(
        "INSERT INTO nrd_file(path,instrument,symbol,kind,contract,session_date,bytes,mtime_utc,"
        "ok,reason,provider,nt_version,fetched_utc,cataloged_utc) VALUES "
        "(:path,:instrument,:symbol,:kind,:contract,:session_date,:bytes,:mtime_utc,:ok,:reason,"
        ":provider,:nt_version,:fetched_utc,:cataloged_utc)",
        [{**r, **dict(zip(("provider", "nt_version", "fetched_utc"), prov.get(r["path"], (None, None, None)))),
          "cataloged_utc": now,
          **{k: r[k] for k in ("path", "instrument", "symbol", "kind", "contract",
                               "session_date", "bytes", "mtime_utc", "ok", "reason")}}
         for r in records])
    con.executemany(
        "INSERT INTO coverage(instrument,symbol,kind,contract,first_date,last_date,n_sessions,"
        "n_expected,n_holes,holes_json,n_flagged,total_bytes,updated_utc) VALUES "
        "(:instrument,:symbol,:kind,:contract,:first_date,:last_date,:n_sessions,:n_expected,"
        ":n_holes,:holes_json,:n_flagged,:total_bytes,:updated_utc)", cov)
    con.commit()
    return len(records), len(cov)
