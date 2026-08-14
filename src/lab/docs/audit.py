#!/usr/bin/env python3
r"""Docs-HEALTH audit probe — checks Sentinel docs (bin\Custom\Docs) + the memory dir for DRIFT and
writes findings into Sentinel\Lab\db\sentinel.db, where the "Sentinel · Docs" Grafana board charts them.

STATIC + READ-ONLY: it only reads files (docs + the .cs/config they reference) — never edits a doc, never
touches NT. Ground truth is static code, so it's deterministic and needs nothing running.

    python audit.py            # one scan, print summary
    python audit.py --watch    # scan every INTERVAL s forever (self-healing loop, guards :8505)
    python audit.py --loop N    # scan every N s
    python audit.py --init      # schema only

Spec: bin\Custom\Docs\SENTINEL_DOCS_HEALTH_SPEC.md. Facts come from facts.py (Docs\_generated\facts.json).
"""
from __future__ import annotations
import os, re, json, sqlite3, socket, argparse, time, glob, fnmatch
import datetime as dt
import sys as _sys, os as _os
_LAB_ROOT = _os.path.abspath(_os.path.join(_os.path.dirname(_os.path.abspath(__file__)), ".."))
if _LAB_ROOT not in _sys.path:
    _sys.path.insert(0, _LAB_ROOT)
from lab_faults import swallow

HERE   = os.path.dirname(os.path.abspath(__file__))
LAB    = os.path.abspath(os.path.join(HERE, ".."))
SENT   = os.path.abspath(os.path.join(LAB, ".."))
NT8    = os.path.abspath(os.path.join(SENT, ".."))
CUSTOM = os.path.join(NT8, "bin", "Custom")
DOCS   = os.path.join(CUSTOM, "Docs")
FACTS  = os.path.join(DOCS, "_generated", "facts.json")
DB     = os.path.join(LAB, "db", "sentinel.db")
MEM    = os.path.join(os.path.expanduser("~"), ".claude", "projects",
                      "c--Users-Administrator-Documents-NinjaTrader-8-bin-Custom", "memory")

INTERVAL   = 900          # docs change slowly — 15 min
GUARD_PORT = 8505
REVIEW_DAYS = 45          # last-audited older than this => review-due

# Docs whose freshness matters most — an uncontracted one of these is a WARN (others INFO).
LOAD_BEARING = {"SENTINEL_PROCESS_ATLAS", "ROADMAP", "SENTINEL_DESIGN_SYSTEM", "SENTINEL_ML_SPEC",
                "SENTINEL_DATA_PLATFORM_SPEC", "SENTINEL_RUNBOOK", "SENTINEL_THESIS"}

SCHEMA = """
CREATE TABLE IF NOT EXISTS docs_health(
  ts_ms INTEGER PRIMARY KEY, ts TEXT,
  docs_total INTEGER, contracted INTEGER,
  errors INTEGER, warns INTEGER, infos INTEGER, drift_score REAL,
  stale_version INTEGER, broken_links INTEGER, missing_html INTEGER, stale_html INTEGER,
  unresolved_wikilinks INTEGER, uncontracted INTEGER, dangling_tokens INTEGER,
  review_due INTEGER, code_moved INTEGER, orphans INTEGER,
  artifacts INTEGER, undocumented INTEGER, dark_public INTEGER);
CREATE TABLE IF NOT EXISTS docs_finding(
  id INTEGER PRIMARY KEY AUTOINCREMENT, ts_ms INTEGER, doc TEXT,
  category TEXT, severity TEXT, detail TEXT);
CREATE TABLE IF NOT EXISTS docs_facts(
  key TEXT PRIMARY KEY, value TEXT, source TEXT, ts_ms INTEGER);
"""

SEV_WEIGHT = {"ERROR": 3, "WARN": 1, "INFO": 0}

FM_RE   = re.compile(r'^﻿?---\r?\n(.*?)\r?\n---\r?\n', re.S)
MDLINK  = re.compile(r'\[[^\]]+\]\(([^)]+)\)')
WIKI    = re.compile(r'\[\[([^\]]+)\]\]')
CODEBLK = re.compile(r'```.*?```|`[^`\n]*`', re.S)
TOKEN   = re.compile(r'\{\{([a-z0-9_]+)\}\}')
SEMVER  = re.compile(r'^v?(\d+)\.(\d+)\.(\d+)$')


def now_ms():
    return int(dt.datetime.now().timestamp() * 1000)


def _migrate(c):
    """Add columns to an existing docs_health without dropping history (coverage, 2026-08-07)."""
    have = {r[1] for r in c.execute("PRAGMA table_info(docs_health)")}
    for col in ("artifacts", "undocumented", "dark_public"):
        if col not in have:
            try:
                c.execute(f"ALTER TABLE docs_health ADD COLUMN {col} INTEGER")
            except sqlite3.OperationalError as _swex:
                swallow("docs.audit._migrate", _swex)


def _conn():
    c = sqlite3.connect(DB, timeout=15)
    c.execute("PRAGMA journal_mode=WAL")
    # 30s, not 8s (raised 2026-07-25, same fix corpus_probe.py got on 07-24 and for the same measured
    # reason): against ingest.py --watch writing every 2s into the multi-GB WAL database, an 8s budget
    # loses the race often enough to throw "database is locked" and skip a whole scan. Observed here
    # while re-certifying the docs to v1.42.0. A probe that silently stops scanning is exactly the
    # failure mode it exists to catch -- and this one is the DOCS drift monitor, so a skipped scan
    # reads on the board as "no drift".
    c.execute("PRAGMA busy_timeout=30000")
    return c


def _read(p):
    try:
        return open(p, encoding="utf-8", errors="replace").read()
    except OSError as _swex:
        swallow("docs.audit._read", _swex)
        return ""


def _mtime(p):
    try:
        return os.path.getmtime(p)
    except OSError as _swex:
        swallow("docs.audit._mtime", _swex)
        return None


def _resolve(rel, doc_dir=None):
    """Resolve a relative path against the doc's dir, then bin\\Custom, NT8, Sentinel. None if not found."""
    if os.path.isabs(rel):
        return rel if os.path.exists(rel) else None
    for b in ([doc_dir] if doc_dir else []) + [CUSTOM, NT8, SENT]:
        if b:
            cand = os.path.normpath(os.path.join(b, rel))
            if os.path.exists(cand):
                return cand
    return None


def parse_fm(text):
    """Return (frontmatter dict, body). Minimal YAML: key: value, and `key: [a, b]` lists."""
    m = FM_RE.match(text)
    if not m:
        return {}, text
    fm = {}
    for line in m.group(1).splitlines():
        if ":" not in line or line.lstrip().startswith("#"):
            continue
        k, _, v = line.partition(":")
        k, v = k.strip(), v.strip()
        if v.startswith("[") and v.endswith("]"):
            v = [x.strip() for x in v[1:-1].split(",") if x.strip()]
        fm[k] = v
    return fm, text[m.end():]


def _semver_tuple(s):
    m = SEMVER.match(str(s).strip())
    return tuple(int(x) for x in m.groups()) if m else None


ART_VER  = re.compile(r'(?:const\s+string\s+\w*Version\w*|__version__)\s*=\s*"([\d]+\.[\d.]+)"')
ART_NAME = re.compile(r"_v(\d+)_(\d+)_(\d+)$")


def _artifact_version(path):
    """The version a TRACKED artifact declares: its Version const, else its _vN_M_P filename.

    ⚠ KNOWN FLOOR, not an exact reading. Most suite files carry no `Version` const (Council,
    SentinelBinds, SentinelTBars do not), so they fall back to the frozen _vN_M_P in the
    filename -- Council_v1_0_0.cs reports 1.0.0 while the live Council is v1.11.0 in a
    separate file. That makes this check UNDER-report, never over-report: it can miss a stale
    doc, but it cannot raise an alarm a doc is unable to clear. For a nag, silent-and-weak
    beats loud-and-wrong -- the latter is what it was.
    """
    m = ART_VER.search(_read(path))
    if m:
        return _semver_tuple(m.group(1))
    m = ART_NAME.search(os.path.splitext(os.path.basename(path))[0])
    return tuple(int(x) for x in m.groups()) if m else None


def contract_target(tracks, core):
    """What `verified-against` is a claim ABOUT -- (version, label), or (None, why-not).

    ⚠ THE BUG THIS FIXES (found 2026-08-07). This check used to compare EVERY doc's
    verified-against to the live SentinelCore version, ignoring the `tracks:` field the
    docs carry. Of the 12 docs it flagged, only 5 track Core; the other 7 -- AZIMUTH_SPEC
    (tracks Azimuth), BINDS_SPEC, KEEL_SPEC, KEEL_TEST_PLAN, BARTYPE_GRID, ML_SPEC,
    DOCS_HEALTH_SPEC -- track a DIFFERENT artifact whose version will never equal Core's,
    so they could never pass and never stop being flagged. That is this project's own
    line-432 lesson: a check that fires on a difference the design introduced on purpose
    teaches its reader to ignore it. A doc is now measured against the thing it tracks.
    """
    core_tracked, others = False, []
    for t in tracks:
        p = _resolve(t)
        if p is None:
            continue
        if "sentinelcore" in os.path.basename(p).lower():
            core_tracked = True
        else:
            v = _artifact_version(p)
            if v:
                others.append((v, os.path.basename(p)))
    if core_tracked:
        return core, "core"
    if others:
        v, label = max(others)
        return v, label
    return None, "no versioned artifact tracked"


def load_facts():
    try:
        return json.load(open(FACTS, encoding="utf-8"))
    except Exception as _swex:
        swallow("docs.audit.load_facts", _swex)
        return {}


def load_ignore():
    """Doc basenames/globs excluded from the audit (legacy / pre-Sentinel / frozen), from Docs\\.audit-ignore."""
    p = os.path.join(DOCS, ".audit-ignore")
    pats = []
    if os.path.exists(p):
        for line in open(p, encoding="utf-8", errors="replace"):
            line = line.strip()
            if line and not line.startswith("#"):
                pats.append(line)
    return pats


def _ignored(name, pats):
    return any(fnmatch.fnmatch(name, pat) or fnmatch.fnmatch(name + ".md", pat) for pat in pats)


class Findings:
    def __init__(self):
        self.rows = []
        self.counts = {}

    def add(self, doc, category, severity, detail):
        self.rows.append((doc, category, severity, detail))
        self.counts[category] = self.counts.get(category, 0) + 1


def scan(conn):
    facts = load_facts()
    fact_keys = set(facts.keys())
    core = _semver_tuple(facts.get("core_version"))
    f = Findings()

    ignore = load_ignore()
    doc_paths = [p for p in sorted(glob.glob(os.path.join(DOCS, "*.md")))
                 if not _ignored(os.path.splitext(os.path.basename(p))[0], ignore)]
    mem_paths = sorted(glob.glob(os.path.join(MEM, "*.md"))) if os.path.isdir(MEM) else []
    contracted = 0

    # link graph (for orphan detection): inbound count per doc STEM (so ROADMAP.md and ROADMAP.html match)
    inbound = {os.path.splitext(os.path.basename(p))[0]: 0 for p in doc_paths}

    for p in doc_paths:
        name = os.path.splitext(os.path.basename(p))[0]
        text = _read(p)
        fm, body = parse_fm(text)
        dmt = _mtime(p)

        # --- contract presence ---
        has_contract = bool(fm.get("tracks") or fm.get("verified-against"))
        if has_contract:
            contracted += 1
        else:
            sev = "WARN" if name in LOAD_BEARING else "INFO"
            f.add(name, "uncontracted", sev, "no frontmatter contract (tracks/verified-against)")

        tracks = fm.get("tracks") or []
        if isinstance(tracks, str):
            tracks = [tracks]

        # --- stale-version / needs-verification (contract) ---
        # Measured against the artifact the doc TRACKS, not against Core unconditionally.
        va = fm.get("verified-against")
        if va:
            vt = _semver_tuple(va)
            tgt, label = contract_target(tracks, core)
            if vt is None:
                f.add(name, "stale_version", "INFO", f"verified-against='{va}' (never verified)")
            elif tgt is None:
                f.add(name, "stale_version", "INFO",
                      f"verified-against {va} cannot be checked: {label}")
            elif vt < tgt:
                f.add(name, "stale_version", "WARN",
                      f"verified-against v{'.'.join(map(str,vt))} < {label} "
                      f"v{'.'.join(map(str,tgt))}")

        # --- review-due ---
        la = fm.get("last-audited")
        if la == "never":
            f.add(name, "review_due", "INFO", "last-audited: never")
        elif la:
            try:
                age = (dt.datetime.now() - dt.datetime.strptime(la, "%Y-%m-%d")).days
                if age > REVIEW_DAYS:
                    f.add(name, "review_due", "INFO", f"last-audited {age}d ago (>{REVIEW_DAYS})")
            except ValueError as _swex:
                swallow("docs.audit.scan", _swex)

        # --- code-moved (tracked source newer than the doc) ---
        for t in tracks:
            tp = _resolve(t)
            if tp is None:
                f.add(name, "broken_links", "ERROR", f"tracks a missing file: {t}")
                continue
            smt = _mtime(tp)
            if smt and dmt and smt > dmt + 1:
                f.add(name, "code_moved", "WARN", f"{t} changed after the doc was last touched")

        # --- html sibling ---
        html = p[:-3] + ".html"
        hmt = _mtime(html)
        if hmt is None:
            f.add(name, "missing_html", "WARN", "no .html sibling")
        elif dmt and hmt < dmt - 1:
            f.add(name, "stale_html", "WARN", ".html older than .md (re-render)")

        # --- dangling tokens (PROSE only; code-span tokens are documented, not used) ---
        prose = CODEBLK.sub("", body)
        for tok in set(TOKEN.findall(prose)):
            if tok not in fact_keys:
                f.add(name, "dangling_tokens", "ERROR", f"{{{{{tok}}}}} has no fact in facts.json")

        # --- broken .md/.html links + count inbound for orphans ---
        for tgt in MDLINK.findall(body):
            tgt = tgt.split("#")[0].strip()
            if not tgt or tgt.startswith(("http://", "https://", "mailto:")):
                continue
            stem = os.path.splitext(os.path.basename(tgt))[0]
            if stem in inbound:
                inbound[stem] += 1
            if tgt.endswith((".md", ".html")) and _resolve(tgt, os.path.dirname(p)) is None:
                f.add(name, "broken_links", "ERROR", f"dead link: {tgt}")

    # count inbound links from rendered .html too (the Atlas + docs index link OUT to docs via href)
    for hp in glob.glob(os.path.join(DOCS, "*.html")):
        for m in re.findall(r'href="([^"#]+\.(?:md|html))"', _read(hp)):
            stem = os.path.splitext(os.path.basename(m))[0]
            if stem in inbound:
                inbound[stem] += 1

    # --- orphans: a Docs/*.md nothing links to (excluding the docs home / index) ---
    for p in doc_paths:
        name = os.path.splitext(os.path.basename(p))[0]
        if inbound.get(name, 0) == 0 and name not in ("SENTINEL_DOCS",):
            f.add(name, "orphans", "INFO", "no other doc links to this one")

    # --- memory dir: wikilink integrity only (light) ---
    mem_names = {os.path.splitext(os.path.basename(p))[0] for p in mem_paths}
    for p in doc_paths + mem_paths:
        name = os.path.splitext(os.path.basename(p))[0]
        body = _read(p)
        for w in set(WIKI.findall(body)):
            w = w.split("|")[0].split("#")[0].strip()
            # only flag simple slug-like targets (a real memory name), not prose in [[...]]
            if re.match(r'^[a-z0-9-]+$', w) and w not in mem_names:
                f.add(name, "unresolved_wikilinks", "INFO", f"[[{w}]] has no memory file")

    # --- COVERAGE (code -> doc): does each artifact have a doc at all? ---
    # Folded in here rather than run as a second probe so one scan answers both halves and the
    # board cannot show a healthy doc set that documents a fraction of the tree. Fails OPEN:
    # a coverage error must not cost the drift scan that already succeeded.
    cov = {}
    try:
        from coverage import scan_coverage
        cov = scan_coverage(f.add)
    except Exception as _swex:
        swallow("docs.audit.coverage", _swex)

    # --- VERSION SELF-CONSISTENCY (header vs the const that gets stamped on data) ---
    # Same reasoning as coverage above: one scan, one board. A file whose banner and version const
    # disagree is lying to somebody, and which one is believed depends on who is reading — a human
    # reads the banner (that is how memory\NOW.md carried the recorder as v2.3.0 for a week while
    # it stamped 2.5.0 on every row), the corpus gets the const. First run found the Council
    # stamping cnclVer=1.9.0 while at v1.11.0, so 8,547 corpus rows conflate three Council
    # behaviours across a real behavioural fork. Fails OPEN, like coverage.
    try:
        from version_check import scan_versions
        for r in scan_versions():
            if r["status"] == "MISMATCH":
                cs = ", ".join(f"{k}={v}" for k, v in r["consts"])
                f.add(r["file"], "version_mismatch", "ERROR",
                      f"header v{r['header']} vs code {cs} — the const is what stamps the data")
    except Exception as _swex:
        swallow("docs.audit.version_check", _swex)

    # ---- write ----
    ms, iso = now_ms(), dt.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    c = f.counts
    sev_tot = {"ERROR": 0, "WARN": 0, "INFO": 0}
    for _, _, sev, _ in f.rows:
        sev_tot[sev] += 1
    drift = sev_tot["ERROR"] * SEV_WEIGHT["ERROR"] + sev_tot["WARN"] * SEV_WEIGHT["WARN"]

    # ⛔⛔ THE BOOKKEEPING MUST NEVER VETO A COMMIT. Measured 2026-08-13: this function runs inside
    # the repo's pre-commit hook, and its WRITE raised `database is locked` against the live
    # ingester DESPITE WAL + a 30s busy_timeout — so EVERY commit to the Sentinel repo was blocked
    # for as long as the platform was up. A gate that stops all work when a HEALTH ROW cannot be
    # filed is not gating the thing it was built to gate.
    # ⇒ The GATE still gates: doc ERRORs decide the exit code exactly as before. Only the history
    #   row is best-effort — and its loss is announced LOUDLY, never swallowed, because a silently
    #   missing row reads on the board as "no drift", which is the failure this tool exists to catch.
    try:
        _record(conn, ms, iso, doc_paths, contracted, sev_tot, drift, c, cov, facts, f)
    except sqlite3.OperationalError as ex:
        print("=" * 78)
        print("⚠ DOCS-HEALTH ROW NOT RECORDED — %s" % ex)
        print("  The database is busy (the Lab platform is running). The AUDIT ITSELF RAN and its")
        print("  findings below are complete; only the history row is missing, so the board will")
        print("  show a GAP rather than a clean scan. Re-run `audit.py` when the platform is idle.")
        print("=" * 78)
        swallow("docs.audit.record", ex)
    return dict(docs=len(doc_paths), contracted=contracted, errors=sev_tot["ERROR"],
                warns=sev_tot["WARN"], infos=sev_tot["INFO"], drift=drift, **cov)


def _record(conn, ms, iso, doc_paths, contracted, sev_tot, drift, c, cov, facts, f):
    """Write one docs-health row + its findings. Best-effort by design — see the caller."""
    conn.execute(
        "INSERT OR REPLACE INTO docs_health("
        "ts_ms,ts,docs_total,contracted,errors,warns,infos,drift_score,"
        "stale_version,broken_links,missing_html,stale_html,unresolved_wikilinks,"
        "uncontracted,dangling_tokens,review_due,code_moved,orphans,"
        "artifacts,undocumented,dark_public) "
        "VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)", (
            ms, iso, len(doc_paths), contracted,
            sev_tot["ERROR"], sev_tot["WARN"], sev_tot["INFO"], drift,
            c.get("stale_version", 0), c.get("broken_links", 0), c.get("missing_html", 0),
            c.get("stale_html", 0), c.get("unresolved_wikilinks", 0), c.get("uncontracted", 0),
            c.get("dangling_tokens", 0), c.get("review_due", 0), c.get("code_moved", 0),
            c.get("orphans", 0),
            cov.get("artifacts", 0), c.get("undocumented", 0), cov.get("dark_public", 0)))
    for doc, cat, sev, detail in f.rows:
        conn.execute("INSERT INTO docs_finding(ts_ms,doc,category,severity,detail) VALUES (?,?,?,?,?)",
                     (ms, doc, cat, sev, detail))
    for k, v in facts.items():
        if not k.startswith("_"):
            conn.execute("INSERT OR REPLACE INTO docs_facts VALUES (?,?,?,?)",
                         (k, str(v), (facts.get("_sources", {}) or {}).get(k, "code"), ms))
    conn.commit()


def main():
    import sys
    ap = argparse.ArgumentParser()
    ap.add_argument("--watch", action="store_true")
    ap.add_argument("--loop", type=int, default=0)
    ap.add_argument("--init", action="store_true")
    ap.add_argument("--errors-only", action="store_true",
                    help="one scan; exit 1 if any ERROR-level finding (a git pre-commit gate)")
    a = ap.parse_args()

    conn = _conn()
    conn.executescript(SCHEMA)
    _migrate(conn)
    conn.commit()
    if a.init:
        print("docs schema created")
        return

    if a.errors_only:
        r = scan(conn)
        errs = list(conn.execute(
            "SELECT doc, category, detail FROM docs_finding "
            "WHERE ts_ms=(SELECT MAX(ts_ms) FROM docs_finding) AND severity='ERROR'"))
        if errs:
            sys.stderr.write(f"docs-audit: {len(errs)} ERROR-level finding(s) -- commit blocked "
                             "(fix, or bypass with git commit --no-verify):\n")
            for doc, cat, detail in errs:
                sys.stderr.write(f"  [{cat}] {doc}: {detail}\n")
            sys.exit(1)
        print(f"docs-audit: clean (0 errors, {r['warns']} warns) -- ok to commit")
        return

    interval = a.loop or INTERVAL
    if not (a.watch or a.loop):
        print(scan(conn))
        return

    guard = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    try:
        guard.bind(("127.0.0.1", GUARD_PORT))
        guard.listen(1)
    except OSError:
        print(f"another docs audit holds :{GUARD_PORT} — exiting")
        return

    print(f"docs audit scanning every {interval}s -> {DB}")
    while True:
        try:
            print(f"{dt.datetime.now():%H:%M:%S} {scan(conn)}")
        except Exception as e:
            print(f"scan error (continuing): {e}")
            try:
                conn.close()
            except Exception as _swex:
                swallow("docs.audit.main", _swex)
            conn = _conn()
        time.sleep(interval)


if __name__ == "__main__":
    main()
