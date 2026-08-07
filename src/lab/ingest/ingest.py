#!/usr/bin/env python3
"""
Sentinel tick-corpus ingester — JSONL sidecars → SQLite.

Reads the Deck's tick-path files (Sentinel\\Excursions\\ticks\\*.jsonl) into
Sentinel\\Lab\\db\\sentinel.db  (trades + ticks). Idempotent — skips files unchanged by mtime,
re-ingests a file if it changed. NT writes the files; THIS owns the DB, so a crash here can never
touch the trading process.

    python ingest.py            # one full scan, then exit
    python ingest.py --watch    # scan, then poll every 2s for new/changed files (live)
    python ingest.py --init     # create schema only

Feeds the Streamlit explorer (viz\\explorer.py) and (Phase 1) Grafana's SQLite datasource.
Spec: bin\\Custom\\Docs\\SENTINEL_DATA_PLATFORM_SPEC.md.
"""
from __future__ import annotations
import os, glob, json, sqlite3, time, argparse, socket, sys
import datetime as dt

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")))
from lab_faults import swallow  # noqa: E402

# Single-instance guard for --watch. 2026-07-25: the ingester was the ONLY component of the data
# platform with no self-guard — sentinel-data.bat inferred its liveness from Streamlit's :8501, so a
# dead OR STALE ingester was never healed and never reported. A process that had been running since
# 07-19 kept executing pre-1.5 source (Python does not reload), so the whole schema-1.5 council corpus
# was invisible to the DB for days while everything looked healthy.
# ⇒ Own the guard here, like health/probe.py (:8502) and friends, so the launcher can start us blindly.
GUARD_PORT = 8506

HERE  = os.path.dirname(os.path.abspath(__file__))
LAB   = os.path.abspath(os.path.join(HERE, ".."))
SENT  = os.path.abspath(os.path.join(HERE, "..", ".."))
DB     = os.path.join(LAB, "db", "sentinel.db")
TICKS  = os.path.join(SENT, "Excursions", "ticks")                       # Deck manual tick paths (kind=manual_tickpath)
CTICKS = os.path.join(SENT, "Excursions", "council", "ticks")            # Council fire tick paths (kind=council_tickpath; schema ctick.1|ctick.2)
# Council ROW corpus (carries the vote vector). 1.3 = pre-provenance (FROZEN); 1.4 = +recVer/coreVer/barLabel
# provenance; 1.5 = the HONEST ENTRY PRICE. All read identically (later schemas just carry extra fields).
#
# 🔴 LABEL CONTAMINATION — 1.3 and 1.4 are NOT comparable to 1.5 (proven 2026-07-22, memory
# `firepx-is-synthetic-ha-close`). In those schemas `firePx` was the Heikin-Ashi SYNTHETIC bar close, a price that
# never traded, and it is the reference for maxMFE/maxMAE/barrier/firstTouch — so every label carries a ~9-tick
# optimistic offset (recorded "target-first" 52.3% vs 21.1% true; labels disagree with truth on 44.6% of fires).
# They are ingested (a valid record of what the old logic saw) but the `schema` column is on every row for exactly
# this reason: ANY label-bearing analysis must filter to CONTAM_FREE, never pool.
CROW_SCHEMAS = ("1.3", "1.4", "1.5")
CONTAM_LABEL_SCHEMAS = ("1.3", "1.4", "cand.1")   # firePx = untradeable HA close ⇒ labels are optimistic
CROW_DIRS = [os.path.join(SENT, "Excursions", "council", v) for v in CROW_SCHEMAS]
# CLOCK-EDGE candidate corpus (the "second oven"): every-brick-close CONT candidate, runLength + seam context,
# NO vote vector. Same row+sidecar model as council (join on inst/bartype/fireTime), source='candidate'.
CCAND_TICKS    = os.path.join(SENT, "Excursions", "candidates", "ticks")            # kind=candidate_tickpath
CCAND_ROW_DIRS = [os.path.join(SENT, "Excursions", "candidates", v) for v in ("cand.1", "cand.2")]

# tick sizes (fallback 0.1). NB: schema tick.2+ px = raw last-trade (Deck OnMarketData); legacy tick.1 files
# carry bar Close[0] (synthetic brick close on HA/Renko/TBars — approximate).
TICK = {"GC":0.1,"MGC":0.1,"SI":0.005,"CL":0.01,"ES":0.25,"MES":0.25,
        "NQ":0.25,"MNQ":0.25,"YM":1.0,"ZN":0.015625,"ZB":0.03125}

SCHEMA = """
CREATE TABLE IF NOT EXISTS trades (
    trade_id      TEXT PRIMARY KEY,
    source        TEXT,           -- manual | council | ...
    schema        TEXT,           -- tick.1 (brick Close[0]) | tick.2+ (raw last-trade). Fill-fidelity discriminator.
    src           TEXT,           -- 'last' = raw last-trade (tick.2+); NULL = legacy synthetic brick close
    inst          TEXT,  bartype  TEXT,  account TEXT,
    dir           INTEGER,        -- +1 long / -1 short
    entry_utc     TEXT,  entry_px REAL,
    exit_utc      TEXT,  exit_px  REAL,
    max_fav_ticks REAL,  max_adv_ticks REAL,
    n_ticks       INTEGER, dur_s  REAL,
    partial       INTEGER,
    -- council context (nullable; filled for council/graded trades later)
    conviction    REAL, size_mult REAL, net_score REAL, active_w REAL,
    votes_json    TEXT, reasons   TEXT, episode_id TEXT, first_touch INTEGER,
    -- council tick-path (source='council'): the ATR barrier + tick-true first-touch timing
    barrier_ticks REAL, ms_to_target_r INTEGER, ms_to_stop_r INTEGER,
    -- provenance (schema 1.4 rows / ctick.2 sidecars; NULL for older corpus)
    rec_ver       TEXT, core_ver TEXT, cncl_ver TEXT, bar_label TEXT, scope TEXT,
    -- council context the fidelity audit flagged as silently dropped (nullable; filled from the row corpus)
    regime        TEXT, adx REAL, conv_bucket TEXT,
    agree         INTEGER, disagree INTEGER, voters INTEGER, end_reason TEXT,
    -- milestone curves (mfe/mae 1/5/15/60 + barsTo*) as one JSON blob (kept clean vs 16 columns)
    milestones_json TEXT,
    -- derived path features
    time_to_mfe_s REAL, time_to_mae_s REAL, mfe_mae_ratio REAL,
    adverse_first INTEGER,        -- 1 = took heat before it worked
    pnl_ticks     REAL,           -- signed exit result (dir*(exit-entry)/tick)
    src_file      TEXT, src_mtime REAL, ingested_utc TEXT
);
-- no (trade_id, ms) PK: two ticks CAN share a millisecond; the implicit rowid keeps insertion (=time) order,
-- so every captured tick is preserved (matters once we go raw-tick). Re-ingest deletes by trade_id first.
CREATE TABLE IF NOT EXISTS ticks (
    trade_id TEXT, ms INTEGER, px REAL, fav_t REAL
);
CREATE INDEX IF NOT EXISTS ix_ticks_trade  ON ticks(trade_id);
CREATE INDEX IF NOT EXISTS ix_trades_entry ON trades(entry_utc);
CREATE INDEX IF NOT EXISTS ix_trades_conv  ON trades(conviction);
-- the vote vector lives in the council ROW corpus (1.3/1.4), not the tick sidecars; ingest_council_rows() folds it in.
-- per-file mtime watermark so --watch only re-reads a row file that changed (each file holds many fires).
CREATE TABLE IF NOT EXISTS row_files (path TEXT PRIMARY KEY, mtime REAL);
CREATE INDEX IF NOT EXISTS ix_trades_fire  ON trades(inst, bartype, entry_utc);
-- query-pattern indexes (idempotent; standard SQL so a Postgres move is a swap):
CREATE INDEX IF NOT EXISTS ix_trades_inst_bt ON trades(inst, bartype);
CREATE INDEX IF NOT EXISTS ix_trades_episode ON trades(episode_id);
CREATE INDEX IF NOT EXISTS ix_trades_src     ON trades(src);
CREATE INDEX IF NOT EXISTS ix_trades_schema  ON trades(schema);
CREATE INDEX IF NOT EXISTS ix_trades_bartype ON trades(bartype);
"""

TRADE_COLS = ["trade_id","source","signal","schema","src","inst","bartype","account","dir","entry_utc","entry_px",
    "exit_utc","exit_px","max_fav_ticks","max_adv_ticks","n_ticks","dur_s","partial",
    "conviction","size_mult","net_score","active_w","votes_json","reasons","episode_id","first_touch",
    "barrier_ticks","ms_to_target_r","ms_to_stop_r",
    "rec_ver","core_ver","cncl_ver","bar_label","scope",
    "regime","adx","conv_bucket","agree","disagree","voters","end_reason","milestones_json",
    # CLOCK-EDGE candidate context (source='candidate'; NULL for council/manual):
    "run_length","rvol","vol_z","climax","dry_up","clock_phase","mins_to_close","mtf_bias",
    "flux_dir","flux_pressure","flux_diverg",
    "time_to_mfe_s","time_to_mae_s","mfe_mae_ratio","adverse_first","pnl_ticks",
    "src_file","src_mtime","ingested_utc"]


class Drops:
    """Per-run tally of rows/files silently discarded, by reason. The audit found ingest dropped
    malformed / no-vote / missing-key data without counting — so silent loss becomes visible here."""
    def __init__(self):
        self.n = {}
    def add(self, reason, k=1):
        self.n[reason] = self.n.get(reason, 0) + k
    def total(self):
        return sum(self.n.values())
    def report(self):
        if not self.n:
            print("  drops: none")
            return
        parts = ", ".join(f"{r}={self.n[r]}" for r in sorted(self.n))
        print(f"  drops ({self.total()} total): {parts}")


# 1.3/1.4 milestone-curve fields folded into one JSON blob (kept out of 16 flat columns).
_MILESTONE_KEYS = ["mfe1","mae1","mfe5","mae5","mfe15","mae15","mfe60","mae60",
                   "barsToMFE","barsToMAE","barsToTargetR","barsToStopR"]


def _milestones_blob(d):
    m = {k: d[k] for k in _MILESTONE_KEYS if d.get(k) is not None}
    return json.dumps(m) if m else None


def db():
    os.makedirs(os.path.dirname(DB), exist_ok=True)
    c = sqlite3.connect(DB, timeout=30)
    c.execute("PRAGMA busy_timeout=30000")   # concurrent writers (a backfill + the watch loop) WAIT, not error
    c.execute("PRAGMA journal_mode=WAL")      # readers (Streamlit/Grafana) never block a writer, and vice-versa
    # 2026-07-30: WITHOUT this the WAL is reused but NEVER shrinks, so one burst sets a permanent
    # high-water mark. Measured: a 3.4 GB -wal against an 8.2 GB db on a disk at 92%. The live WAL held
    # only ~4k frames (~16 MB) — i.e. checkpointing was fine and the 3.4 GB was pure unreclaimed file.
    # journal_size_limit is PER-CONNECTION and not persisted in the db, so it must be set on every connect.
    c.execute("PRAGMA journal_size_limit=67108864")   # truncate the WAL back to <=64 MB after a checkpoint
    c.executescript(SCHEMA)
    _migrate(c)
    return c


def _migrate(c):
    """Additive, idempotent column adds for an existing DB (ALTER can't run inside CREATE IF NOT EXISTS)."""
    have = {r[1] for r in c.execute("PRAGMA table_info(trades)")}
    for col, typ in (("schema","TEXT"), ("src","TEXT"),
                     # WHO PRODUCED THE DECISION — deliberately NOT `source`.
                     # `source` is the CORPUS FOLDER (council | candidate | manual) and was being used as
                     # if it were the producer. Once SentinelCore.NoteSignalFire made the recorder's intake
                     # generic (Core v1.46.0), a KEEL fire started landing in the council folder and became
                     # indistinguishable from a Council verdict: `signal:"KEEL"` was on the row and dropped
                     # here. That is exactly the "do not pool two signal sources in one unlabelled set"
                     # rule the schema-version discipline exists to enforce.
                     # ADDITIVE on purpose: ~20k existing rows and every saved query filter on
                     # source='council', so repurposing it would silently reinterpret the whole back
                     # catalogue. New column, backfilled from the folder for rows that predate the tag.
                     ("signal","TEXT"),
                     ("barrier_ticks","REAL"), ("ms_to_target_r","INTEGER"), ("ms_to_stop_r","INTEGER"),
                     # B5 widen — provenance + audit-flagged dropped context (backfilled NULL for old rows):
                     ("rec_ver","TEXT"), ("core_ver","TEXT"), ("cncl_ver","TEXT"), ("bar_label","TEXT"), ("scope","TEXT"),
                     ("regime","TEXT"), ("adx","REAL"), ("conv_bucket","TEXT"),
                     ("agree","INTEGER"), ("disagree","INTEGER"), ("voters","INTEGER"), ("end_reason","TEXT"),
                     ("milestones_json","TEXT"),
                     # CLOCK-EDGE candidate context:
                     ("run_length","INTEGER"), ("rvol","REAL"), ("vol_z","REAL"), ("climax","INTEGER"),
                     ("dry_up","INTEGER"), ("clock_phase","INTEGER"), ("mins_to_close","INTEGER"),
                     ("mtf_bias","INTEGER"), ("flux_dir","INTEGER"), ("flux_pressure","REAL"), ("flux_diverg","INTEGER")):
        if col not in have:
            c.execute(f"ALTER TABLE trades ADD COLUMN {col} {typ}")
    # BACKFILL, once. Every row that predates the tag came from a corpus folder that WAS its producer,
    # so the mapping is exact rather than a guess — and leaving them NULL would make "untagged" and
    # "Council" the same query, rebuilding the ambiguity this column exists to remove.
    if "signal" not in have:
        c.execute("UPDATE trades SET signal = UPPER(source) WHERE signal IS NULL AND source IS NOT NULL")
    c.commit()


def ingest_file(c, path, force=False, drops=None):
    d = drops or Drops()
    mtime = os.path.getmtime(path)
    with open(path, "r", encoding="utf-8") as fh:
        lines = [l.strip() for l in fh if l.strip()]
    if not lines:
        d.add("file_empty")
        return False
    try:
        hdr = json.loads(lines[0])
    except json.JSONDecodeError:
        d.add("file_bad_header")
        return False
    kind = hdr.get("kind")
    if kind not in ("manual_tickpath", "council_tickpath", "candidate_tickpath"):
        d.add("file_wrong_kind")
        return False
    tid = hdr.get("tradeId") or hdr.get("fireId")   # manual uses tradeId; council uses fireId
    if not tid:
        d.add("file_no_id")
        return False
    if not force:
        row = c.execute("SELECT src_mtime FROM trades WHERE trade_id=?", (tid,)).fetchone()
        if row and row[0] and abs(row[0] - mtime) < 1e-6:
            return False  # unchanged → skip (not a drop)

    ticks = []
    for l in lines[1:]:
        try:
            r = json.loads(l)
            ticks.append((int(r["ms"]), float(r["px"])))
        except (json.JSONDecodeError, KeyError, ValueError):
            d.add("tick_bad_line")

    inst  = hdr.get("inst", "?")
    dirn  = int(hdr.get("dir", 1))
    tk    = TICK.get(inst, 0.1)
    dur   = (ticks[-1][0] / 1000.0) if ticks else 0.0
    base  = {
        "trade_id": tid, "schema": hdr.get("schema"), "inst": inst, "bartype": hdr.get("bartype"),
        "dir": dirn, "n_ticks": int(hdr.get("ticks", len(ticks))), "dur_s": dur,
        "net_score": None, "active_w": None, "votes_json": None, "reasons": None,
        # provenance — populated for ctick.2 sidecars; NULL/absent on ctick.1 & manual (schema-tolerant .get)
        "rec_ver": hdr.get("recVer"), "core_ver": hdr.get("coreVer"), "cncl_ver": hdr.get("cnclVer"), "bar_label": hdr.get("barLabel"),
        "scope": hdr.get("scope"),
        "src_file": os.path.basename(path), "src_mtime": mtime,
        "ingested_utc": dt.datetime.now(dt.timezone.utc).isoformat(),
    }

    if kind == "council_tickpath":
        # A COUNCIL fire: MFE/MAE + first-touch are already tick-true in the header (the recorder computed them).
        # No managed exit → exit/pnl stay null; the OUTCOME is the tick-true firstTouch label.
        entry   = float(hdr.get("firePx", ticks[0][1] if ticks else 0.0))
        max_fav = float(hdr.get("maxFavTicks", 0) or 0)
        max_adv = float(hdr.get("maxAdvTicks", 0) or 0)
        t_mfe   = (hdr.get("msToMaxFav") or 0) / 1000.0
        t_mae   = (hdr.get("msToMaxAdv") or 0) / 1000.0
        rec = {**base, "source": "council", "signal": (hdr.get("signal") or "COUNCIL").upper(),
            "src": hdr.get("src", "last"), "account": None,
            "entry_utc": hdr.get("fireTime"), "entry_px": entry, "exit_utc": None, "exit_px": None,
            "max_fav_ticks": max_fav, "max_adv_ticks": max_adv, "partial": 0,
            "conviction": hdr.get("conviction"), "size_mult": hdr.get("sizeMult"),
            "episode_id": hdr.get("episodeId"), "first_touch": hdr.get("firstTouchTick"),
            "barrier_ticks": hdr.get("barrierTicks"),
            "ms_to_target_r": hdr.get("msToTargetR"), "ms_to_stop_r": hdr.get("msToStopR"),
            "time_to_mfe_s": t_mfe, "time_to_mae_s": t_mae,
            "mfe_mae_ratio": (max_fav / max_adv) if max_adv > 1e-9 else None,
            "adverse_first": 1 if (t_mae < t_mfe) else 0, "pnl_ticks": None,
        }
    elif kind == "candidate_tickpath":
        # A CLOCK-EDGE candidate fire (every brick close): tick-true MFE/MAE + first-touch already in the header;
        # runLength carried through. No conviction/episode (no fusion). Context (regime/rvol/…) folds in from the ROW.
        entry   = float(hdr.get("firePx", ticks[0][1] if ticks else 0.0))
        max_fav = float(hdr.get("maxFavTicks", 0) or 0)
        max_adv = float(hdr.get("maxAdvTicks", 0) or 0)
        t_mfe   = (hdr.get("msToMaxFav") or 0) / 1000.0
        t_mae   = (hdr.get("msToMaxAdv") or 0) / 1000.0
        rec = {**base, "source": "candidate", "signal": (hdr.get("signal") or "CANDIDATE").upper(),
            "src": "last", "account": None,
            "entry_utc": hdr.get("fireTime"), "entry_px": entry, "exit_utc": None, "exit_px": None,
            "max_fav_ticks": max_fav, "max_adv_ticks": max_adv, "partial": 0,
            "conviction": None, "size_mult": None, "episode_id": None,
            "first_touch": hdr.get("firstTouchTick"), "barrier_ticks": hdr.get("barrierTicks"),
            "ms_to_target_r": hdr.get("msToTargetR"), "ms_to_stop_r": hdr.get("msToStopR"),
            "run_length": hdr.get("runLength"),
            "time_to_mfe_s": t_mfe, "time_to_mae_s": t_mae,
            "mfe_mae_ratio": (max_fav / max_adv) if max_adv > 1e-9 else None,
            "adverse_first": 1 if (t_mae < t_mfe) else 0, "pnl_ticks": None,
        }
    else:
        entry = float(hdr.get("entryPx", ticks[0][1] if ticks else 0.0))
        fav = [(ms, dirn * (px - entry) / tk) for ms, px in ticks]
        max_fav = max((f for _, f in fav), default=float(hdr.get("maxFavTicks", 0) or 0))
        max_adv = max((-f for _, f in fav), default=float(hdr.get("maxAdvTicks", 0) or 0))
        t_mfe = (next((ms for ms, f in fav if f == max_fav), 0) / 1000.0) if fav else 0.0
        t_mae = (next((ms for ms, f in fav if -f == max_adv), 0) / 1000.0) if fav else 0.0
        exitpx = hdr.get("exitPx")
        rec = {**base, "source": "manual", "signal": (hdr.get("signal") or "MANUAL").upper(),
            "src": hdr.get("src"), "account": hdr.get("account"),
            "entry_utc": hdr.get("entryTime"), "entry_px": entry,
            "exit_utc": hdr.get("exitTime"), "exit_px": float(exitpx) if exitpx is not None else None,
            "max_fav_ticks": max_fav, "max_adv_ticks": max_adv,
            "partial": 1 if hdr.get("partial", False) else 0,
            "conviction": None, "size_mult": None, "episode_id": None, "first_touch": None,
            "barrier_ticks": None, "ms_to_target_r": None, "ms_to_stop_r": None,
            "time_to_mfe_s": t_mfe, "time_to_mae_s": t_mae,
            "mfe_mae_ratio": (max_fav / max_adv) if max_adv > 1e-9 else None,
            "adverse_first": 1 if (t_mae < t_mfe) else 0,
            "pnl_ticks": (dirn * (float(exitpx) - entry) / tk) if exitpx is not None else None,
        }
    ph = ",".join("?" for _ in TRADE_COLS)
    c.execute(f"INSERT OR REPLACE INTO trades ({','.join(TRADE_COLS)}) VALUES ({ph})",
              [rec.get(k) for k in TRADE_COLS])
    if kind in ("council_tickpath", "candidate_tickpath"):
        # this tick-true fire supersedes any row-only placeholder (src='row') the row folder may have
        # inserted for the same fire before the sidecar arrived — drop the twin so a fire is counted once.
        c.execute("DELETE FROM trades WHERE src='row' AND source=? AND inst=? AND bartype=? AND entry_utc=?",
                  (rec["source"], inst, rec["bartype"], rec["entry_utc"]))
    c.execute("DELETE FROM ticks WHERE trade_id=?", (tid,))
    c.executemany("INSERT OR REPLACE INTO ticks (trade_id,ms,px,fav_t) VALUES (?,?,?,?)",
                  [(tid, ms, px, dirn * (px - entry) / tk) for ms, px in ticks])
    return True


# the vote-vector columns folded in from a 1.3 ROW (the tick sidecar header does not carry them).
_VOTE_COLS = ["conviction", "size_mult", "net_score", "active_w", "votes_json", "reasons",
              "episode_id", "first_touch", "barrier_ticks"]


def _row_rec(d, path, mtime):
    """Map one schema-1.3 council ROW to a full trades record (source='council', src='row' = BAR-based, no tick path).
    src='row' is the discriminator vs the tick-true sidecar rows (src='last') so a consumer never conflates them."""
    inst, bartype, ft = d.get("inst"), d.get("bartype"), d.get("fireTime")
    votes = d.get("votes")
    mfe, mae = float(d.get("maxMFE", 0) or 0), float(d.get("maxMAE", 0) or 0)
    t_mfe = (d.get("msToMFE") or 0) / 1000.0
    t_mae = (d.get("msToMAE") or 0) / 1000.0
    return {
        "trade_id": f"row:{inst}:{bartype}:{ft}", "source": "council",
        "signal": (d.get("signal") or "COUNCIL").upper(), "schema": d.get("schema", "1.3"),
        "src": "row", "inst": inst, "bartype": bartype, "account": None,
        "dir": int(d.get("dir", 0)), "entry_utc": ft, "entry_px": d.get("firePx"),
        "exit_utc": None, "exit_px": None, "max_fav_ticks": mfe, "max_adv_ticks": mae,
        "n_ticks": None, "dur_s": None, "partial": 0,
        "conviction": d.get("conviction"), "size_mult": d.get("sizeMult"),
        "net_score": d.get("netScore"), "active_w": d.get("activeW"),
        "votes_json": json.dumps(votes) if isinstance(votes, dict) else None,
        "reasons": d.get("reasons"), "episode_id": d.get("episodeId"), "first_touch": d.get("firstTouch"),
        "barrier_ticks": d.get("barrierTicks"), "ms_to_target_r": None, "ms_to_stop_r": None,
        # provenance (schema 1.4 only; NULL on 1.3) + the context the audit flagged as dropped:
        "rec_ver": d.get("recVer"), "core_ver": d.get("coreVer"), "cncl_ver": d.get("cnclVer"), "bar_label": d.get("barLabel"),
        "scope": d.get("scope"),
        "regime": d.get("regime"), "adx": d.get("adx"), "conv_bucket": d.get("convBucket"),
        "agree": d.get("agree"), "disagree": d.get("disagree"), "voters": d.get("voters"),
        "end_reason": d.get("endReason"), "milestones_json": _milestones_blob(d),
        "time_to_mfe_s": t_mfe, "time_to_mae_s": t_mae,
        "mfe_mae_ratio": (mfe / mae) if mae > 1e-9 else None,
        "adverse_first": 1 if (t_mae < t_mfe) else 0, "pnl_ticks": None,
        "src_file": os.path.basename(path), "src_mtime": mtime,
        "ingested_utc": dt.datetime.now(dt.timezone.utc).isoformat(),
    }


def ingest_council_rows(c, force=False, drops=None):
    """Fold the council ROW corpus (which carries the vote vector) into trades — schema 1.3 (frozen,
    pre-provenance) AND 1.4 (+recVer/coreVer/barLabel), read identically. For each fire:
      - a tick-true trade already exists (same inst,bartype,fireTime) → UPDATE its NULL vote/context columns
        (the sidecar's conviction/first_touch are tick-true and stay authoritative);
      - else → INSERT a bar-based row trade (src='row'). A later sidecar drops this twin (see ingest_file).
    Per-file mtime watermark: a row file is re-read only when it grows/changes."""
    d = drops or Drops()
    ins = upd = 0
    row_files = []
    for crows in CROW_DIRS:                                       # 1.3 then 1.4 (both, identical handling)
        row_files += sorted(glob.glob(os.path.join(crows, "*.jsonl")))   # non-recursive: skips _archive / _exp subdirs
    for p in row_files:
        mtime = os.path.getmtime(p)
        if not force:
            row = c.execute("SELECT mtime FROM row_files WHERE path=?", (p,)).fetchone()
            if row and row[0] and abs(row[0] - mtime) < 1e-6:
                continue
        with open(p, encoding="utf-8") as fh:
            for line in fh:
                line = line.strip()
                if not line:
                    continue
                try:
                    rowd = json.loads(line)
                except json.JSONDecodeError:
                    d.add("row_bad_json")
                    continue
                inst, bartype, ft = rowd.get("inst"), rowd.get("bartype"), rowd.get("fireTime")
                if not (inst and bartype and ft):
                    d.add("row_missing_key")
                    continue
                # ⚠ NO VOTE VECTOR IS ONLY A DEFECT FOR A *COUNCIL* ROW.
                # This folder stopped being Council-only when SentinelCore.NoteSignalFire made the
                # recorder's intake generic (Core v1.46.0): a KEEL fire lands here with `signal:"KEEL"`,
                # `council:false` and no votes — legitimately, because no Council produced it. Dropping
                # it silently is how the first Keel smoke run put 52 real fires on disk and zero in the
                # DB while every counter upstream read healthy.
                # A Council row with no votes is still a defect and still drops.
                if not isinstance(rowd.get("votes"), dict):
                    if (rowd.get("signal") or "COUNCIL").upper() == "COUNCIL":
                        d.add("row_no_votes")
                        continue
                hit = c.execute(
                    "SELECT trade_id FROM trades WHERE source='council' AND src<>'row' "
                    "AND inst=? AND bartype=? AND entry_utc=? LIMIT 1", (inst, bartype, ft)).fetchone()
                r = _row_rec(rowd, p, mtime)
                if hit:   # tick-true trade exists → enrich its NULL vote + context columns only
                    # ⚠ `signal` is set UNCONDITIONALLY, not COALESCEd, and it is the one column here
                    # that must be. The tick sidecar's header carries no producer tag, so the sidecar
                    # mapper defaults it to 'COUNCIL' — a non-NULL value, which COALESCE would then
                    # preserve forever. The ROW is the authority on who produced the fire (it carries
                    # `signal` per fire), so the row wins. For a genuine Council row this is a no-op;
                    # for a KEEL fire it is the difference between a labelled cohort and a pooled one.
                    c.execute(
                        "UPDATE trades SET signal=?, net_score=?, active_w=?, votes_json=?, "
                        "reasons=COALESCE(reasons,?), conviction=COALESCE(conviction,?), "
                        "size_mult=COALESCE(size_mult,?), episode_id=COALESCE(episode_id,?), "
                        "barrier_ticks=COALESCE(barrier_ticks,?), "
                        "rec_ver=COALESCE(rec_ver,?), core_ver=COALESCE(core_ver,?), cncl_ver=COALESCE(cncl_ver,?), "
                        "bar_label=COALESCE(bar_label,?), scope=COALESCE(scope,?), "
                        "regime=COALESCE(regime,?), adx=COALESCE(adx,?), conv_bucket=COALESCE(conv_bucket,?), "
                        "agree=COALESCE(agree,?), disagree=COALESCE(disagree,?), voters=COALESCE(voters,?), "
                        "end_reason=COALESCE(end_reason,?), milestones_json=COALESCE(milestones_json,?) "
                        "WHERE trade_id=?",
                        (r["signal"], r["net_score"], r["active_w"], r["votes_json"], r["reasons"], r["conviction"],
                         r["size_mult"], r["episode_id"], r["barrier_ticks"],
                         r["rec_ver"], r["core_ver"], r["cncl_ver"], r["bar_label"], r["scope"],
                         r["regime"], r["adx"], r["conv_bucket"], r["agree"], r["disagree"], r["voters"],
                         r["end_reason"], r["milestones_json"], hit[0]))
                    upd += 1
                else:     # no sidecar (historical / pre-Phase-3) → insert the bar-based row trade
                    ph = ",".join("?" for _ in TRADE_COLS)
                    c.execute(f"INSERT OR REPLACE INTO trades ({','.join(TRADE_COLS)}) VALUES ({ph})",
                              [r.get(k) for k in TRADE_COLS])
                    ins += 1
        c.execute("INSERT OR REPLACE INTO row_files (path, mtime) VALUES (?,?)", (p, mtime))
    if ins or upd:
        c.commit()
    return ins, upd


def _cand_row_rec(d, path, mtime):
    """Map one CLOCK-EDGE candidate ROW (cand.1) to a trades record (source='candidate', src='row' = BAR-based).
    Carries runLength + the consulted seam context; no vote vector, no conviction (there is no fusion)."""
    inst, bartype, ft = d.get("inst"), d.get("bartype"), d.get("fireTime")
    mfe, mae = float(d.get("maxMFE", 0) or 0), float(d.get("maxMAE", 0) or 0)
    t_mfe = (d.get("msToMFE") or 0) / 1000.0
    t_mae = (d.get("msToMAE") or 0) / 1000.0
    return {
        "trade_id": f"cand:{inst}:{bartype}:{ft}", "source": "candidate",
        "signal": (d.get("signal") or "CANDIDATE").upper(), "schema": d.get("schema", "cand.1"),
        "src": "row", "inst": inst, "bartype": bartype, "account": None,
        "dir": int(d.get("dir", 0)), "entry_utc": ft, "entry_px": d.get("firePx"),
        "exit_utc": None, "exit_px": None, "max_fav_ticks": mfe, "max_adv_ticks": mae,
        "n_ticks": None, "dur_s": None, "partial": 0,
        "conviction": None, "size_mult": None, "net_score": None, "active_w": None,
        "votes_json": None, "reasons": None, "episode_id": None,
        "first_touch": d.get("firstTouch"), "barrier_ticks": d.get("barrierTicks"),
        "ms_to_target_r": None, "ms_to_stop_r": None,
        "rec_ver": d.get("recVer"), "core_ver": d.get("coreVer"), "cncl_ver": None,
        "bar_label": d.get("barLabel"), "scope": d.get("scope"),
        "regime": d.get("regime"), "adx": d.get("adx"), "conv_bucket": None,
        "agree": None, "disagree": None, "voters": None,
        "end_reason": d.get("endReason"), "milestones_json": _milestones_blob(d),
        # candidate context (consulted seams)
        "run_length": d.get("runLength"), "rvol": d.get("rvol"), "vol_z": d.get("volZ"),
        "climax": 1 if d.get("climax") else 0, "dry_up": 1 if d.get("dryUp") else 0,
        "clock_phase": d.get("clockPhase"), "mins_to_close": d.get("minsToClose"),
        "mtf_bias": d.get("mtfBias"), "flux_dir": d.get("fluxDir"),
        "flux_pressure": d.get("fluxPressure"), "flux_diverg": d.get("fluxDiverg"),
        "time_to_mfe_s": t_mfe, "time_to_mae_s": t_mae,
        "mfe_mae_ratio": (mfe / mae) if mae > 1e-9 else None,
        "adverse_first": 1 if (t_mae < t_mfe) else 0, "pnl_ticks": None,
        "src_file": os.path.basename(path), "src_mtime": mtime,
        "ingested_utc": dt.datetime.now(dt.timezone.utc).isoformat(),
    }


def ingest_candidate_rows(c, force=False, drops=None):
    """Fold the CLOCK-EDGE candidate ROW corpus (cand.1) into trades. Mirrors ingest_council_rows but there is
    NO vote vector: for each fire, if a tick-true sidecar trade exists (same inst,bartype,fireTime) enrich its
    NULL context columns; else insert a bar-based src='row' candidate trade (a later sidecar drops the twin)."""
    d = drops or Drops()
    ins = upd = 0
    row_files = []
    for crows in CCAND_ROW_DIRS:
        row_files += sorted(glob.glob(os.path.join(crows, "*.jsonl")))   # non-recursive: skips _archive subdirs
    for p in row_files:
        mtime = os.path.getmtime(p)
        if not force:
            row = c.execute("SELECT mtime FROM row_files WHERE path=?", (p,)).fetchone()
            if row and row[0] and abs(row[0] - mtime) < 1e-6:
                continue
        with open(p, encoding="utf-8") as fh:
            for line in fh:
                line = line.strip()
                if not line:
                    continue
                try:
                    rowd = json.loads(line)
                except json.JSONDecodeError:
                    d.add("cand_row_bad_json")
                    continue
                inst, bartype, ft = rowd.get("inst"), rowd.get("bartype"), rowd.get("fireTime")
                if not (inst and bartype and ft):
                    d.add("cand_row_missing_key")
                    continue
                hit = c.execute(
                    "SELECT trade_id FROM trades WHERE source='candidate' AND src<>'row' "
                    "AND inst=? AND bartype=? AND entry_utc=? LIMIT 1", (inst, bartype, ft)).fetchone()
                r = _cand_row_rec(rowd, p, mtime)
                if hit:   # tick-true sidecar exists → enrich its NULL context columns only
                    c.execute(
                        "UPDATE trades SET regime=COALESCE(regime,?), adx=COALESCE(adx,?), "
                        "run_length=COALESCE(run_length,?), rvol=COALESCE(rvol,?), vol_z=COALESCE(vol_z,?), "
                        "climax=COALESCE(climax,?), dry_up=COALESCE(dry_up,?), clock_phase=COALESCE(clock_phase,?), "
                        "mins_to_close=COALESCE(mins_to_close,?), mtf_bias=COALESCE(mtf_bias,?), "
                        "flux_dir=COALESCE(flux_dir,?), flux_pressure=COALESCE(flux_pressure,?), flux_diverg=COALESCE(flux_diverg,?), "
                        "end_reason=COALESCE(end_reason,?), milestones_json=COALESCE(milestones_json,?), "
                        "rec_ver=COALESCE(rec_ver,?), core_ver=COALESCE(core_ver,?), bar_label=COALESCE(bar_label,?) "
                        "WHERE trade_id=?",
                        (r["regime"], r["adx"], r["run_length"], r["rvol"], r["vol_z"], r["climax"], r["dry_up"],
                         r["clock_phase"], r["mins_to_close"], r["mtf_bias"], r["flux_dir"], r["flux_pressure"],
                         r["flux_diverg"], r["end_reason"], r["milestones_json"], r["rec_ver"], r["core_ver"],
                         r["bar_label"], hit[0]))
                    upd += 1
                else:     # no sidecar yet → insert the bar-based row trade
                    ph = ",".join("?" for _ in TRADE_COLS)
                    c.execute(f"INSERT OR REPLACE INTO trades ({','.join(TRADE_COLS)}) VALUES ({ph})",
                              [r.get(k) for k in TRADE_COLS])
                    ins += 1
        c.execute("INSERT OR REPLACE INTO row_files (path, mtime) VALUES (?,?)", (p, mtime))
    if ins or upd:
        c.commit()
    return ins, upd


def scan(c, force=False, drops=None):
    d = drops if drops is not None else Drops()
    n = 0
    files = (sorted(glob.glob(os.path.join(TICKS, "*.jsonl")))
             + sorted(glob.glob(os.path.join(CTICKS, "*.jsonl")))       # Deck manual + Council fire paths (ctick.1|ctick.2)
             + sorted(glob.glob(os.path.join(CCAND_TICKS, "*.jsonl"))))  # CLOCK-EDGE candidate paths (candidate_tickpath)
    for p in files:
        try:
            if ingest_file(c, p, force=force, drops=d):
                n += 1
        except Exception as e:  # noqa: BLE001 — never let one bad file stop the watch
            print(f"  ! {os.path.basename(p)}: {e}")
            d.add("file_exception")
    if n:
        c.commit()
    ci, cu = ingest_council_rows(c, force=force, drops=d)   # fold in the vote vector + historical row corpus (1.3+1.4)
    if ci or cu:
        print(f"  council rows: +{ci} inserted, {cu} enriched")
    ki, ku = ingest_candidate_rows(c, force=force, drops=d) # fold in the CLOCK-EDGE candidate row corpus (cand.1)
    if ki or ku:
        print(f"  candidate rows: +{ki} inserted, {ku} enriched")
    return n


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--watch", action="store_true", help="poll every 2s for new/changed files")
    ap.add_argument("--init",  action="store_true", help="create schema only")
    ap.add_argument("--reingest", action="store_true", help="re-ingest every file ignoring the mtime skip (backfill new columns)")
    a = ap.parse_args()

    # 2026-07-31: CLAIM THE GUARD BEFORE THE INITIAL SCAN, not after it.
    # The bind used to sit below `scan()`, so a second --watch instance ran a FULL scan of the
    # corpus -- writing the whole time -- and only then discovered it was the loser and exited.
    # That is a concurrent-writer window as long as a full scan (minutes on this corpus), which
    # is precisely the window the guard exists to prevent. Observed for real when the
    # SentinelDataPlatform task self-healed alongside a manual restart.
    # This matters more than lock contention: ingest_ticks does DELETE-then-INSERT per trade, so
    # two ingesters touching the same sidecar can interleave a delete against the other's insert
    # and leave a trade's tick path short. The DB is the corpus of record -- ONE writer, always.
    guard = None
    if a.watch:
        guard = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        try:
            guard.bind(("127.0.0.1", GUARD_PORT)); guard.listen(1)
        except OSError:
            print(f"another ingester holds :{GUARD_PORT} - exiting before touching the db")
            return

    c = db()
    if a.init and not a.watch:
        print(f"schema ready · {DB}")
        return
    drops = Drops()
    print(f"ingested/updated {scan(c, force=a.reingest, drops=drops)} file(s) · db {DB}")
    drops.report()
    if a.watch:
        print(f"watching {TICKS} + {CTICKS} + {CCAND_TICKS} (poll 2s) — Ctrl-C to stop")
        # 2026-07-30: this loop holds ONE connection for the daemon's whole life, so SQLite almost always
        # sees an active read mark and can never backfill the WAL past it — the checkpoint point froze at
        # frame 687 for days while the file grew to 3.4 GB. Killing the ingester released it instantly.
        # Checkpointing from the ingester's OWN (committed, idle) connection is what fixes it: a connection
        # never blocks its own checkpoint, so this advances the backfill where an external checkpointer
        # could not. TRUNCATE + journal_size_limit above then actually reclaim the file.
        last_ckpt = time.time()
        try:
            while True:
                time.sleep(2)
                wd = Drops()
                m = scan(c, drops=wd)
                if m or wd.total():
                    print(f"{dt.datetime.now().strftime('%H:%M:%S')}  +{m}")
                    if wd.total():
                        wd.report()
                if time.time() - last_ckpt >= 300:
                    last_ckpt = time.time()
                    try:
                        c.commit()   # end any open txn first, or there is nothing to back-fill past
                        busy, log, ck = c.execute("PRAGMA wal_checkpoint(TRUNCATE)").fetchone()
                        if busy:     # a reader pinned us - say so, don't let it fail silently
                            print(f"{dt.datetime.now().strftime('%H:%M:%S')}  wal checkpoint BUSY "
                                  f"(log={log} backfilled={ck}) - a reader is holding a snapshot")
                    except Exception as e:  # noqa: BLE001 - reported via lab_faults, never silent
                        swallow("ingest.wal_checkpoint", e)
        except KeyboardInterrupt:
            print("stopped")


if __name__ == "__main__":
    main()
