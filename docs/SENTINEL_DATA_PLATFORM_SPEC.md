# Sentinel Data Platform — Spec & Stand-Up Workflow

**Status:** ✅ **BUILT & LIVE (updated 2026-07-14).** All phases below shipped; the sections that read as
"to build" / "open decisions" are kept for the design rationale but are RESOLVED — see **§9 Built state** for
what is actually running. **Goal (achieved):** turn the trade/tick records into a persistent, always-on,
filterable data layer with two graphical front-ends — **Plotly (per-trade path inspection)** and
**Grafana (dashboards)** — so you *open a URL and the data is there*, no copy-paste from chat, no one-off scripts.

**What shipped since this spec was written:** SQLite `Sentinel\Lab\db\sentinel.db` (WAL) + a live `--watch`
ingester + Streamlit :8501 + Grafana :3000, all auto-started by the **`SentinelDataPlatform`** scheduled task.
**And (2026-07-14) the ingester now folds the Council VOTE VECTOR into the DB** — the decision inputs
(`votes/netScore/activeW/reasons`) live only in the `council\1.3\` ROW corpus, not the tick sidecars, so the
ingester reads both: it **enriches** each tick-true trade with its vote vector and **backfills** the historical
graded fires. DB went 702 → ~5,300 trades, **~99% carrying the vote vector.** See [[ingester-vote-vector-fold-in]].

Related: [[sentinel-ml-lab]] (the "analyze the PATH, go tick-true" redirection), the Deck tape capture
(`Deck_v0_2_5.cs`, raw-tick `OnMarketData`), the ExcursionRecorder corpus (`SentinelExcursionRecorder_v2_0_0`, v2.1.2).

---

## 1. North star (the workflow you described)

```
  You trade in NT ─▶ Deck writes JSONL tick files ─▶ Ingester loads them ─▶ SQLite ─┬─▶ Streamlit+Plotly  (localhost:8501)
  (+ Council fires)   (Excursions\ticks\, council\)   (runs on a schedule)    (one DB) └─▶ Grafana          (localhost:3000)
```

You never touch files or paste links. The ingester keeps the DB current; you go to either front-end and **filter**.

---

## 2. Architecture decisions (with rationale)

1. **NT writes files, Python owns the DB. NT never talks to SQLite.**
   NinjaScript stays simple and crash-safe: the Deck/recorder append **JSONL** (durable, append-only, no DB
   driver in the trading process, no lock contention with a live account). A **Python ingester** reads those
   files into SQLite. This decouples the *trading* process from the *analysis* store — a bug in analysis can
   never touch order flow.
2. **SQLite is the single source of truth** for BOTH front-ends. One `sentinel.db`; Streamlit and Grafana both
   read it. Rationale: embedded (no server), one file, pandas-native, and Grafana has a first-class SQLite
   datasource plugin. (Postgres/Influx only if we later need multi-writer or live streaming — not now.)
3. **Plotly and Grafana are complementary, not redundant.**
   - **Streamlit + Plotly** → *inspect ONE trade's path* interactively (the favorable-ticks shape, entry heat,
     raw price), + ad-hoc aggregates.
   - **Grafana** → *dashboards over the whole corpus* (blotter, aggregate stats by conviction band / bar type,
     capture health), live-refreshing.
4. **The DB schema is fidelity-forward.** It carries a `px` column now (currently the bar `Close[0]`) and adds
   `last/bid/ask` when the capture goes **raw-tick** via `OnMarketData` (see §6). No schema churn to upgrade.

---

## 3. Data model (SQLite: `Sentinel\Lab\db\sentinel.db`)

### `trades` — one row per captured trade/fire
| column | type | notes |
|---|---|---|
| `trade_id` | TEXT PK | the JSONL `tradeId` |
| `source` | TEXT | `manual` (Deck tape) · `council` (recorder) · `godtrades` … |
| `inst` · `bartype` · `account` | TEXT | |
| `dir` | INT | +1 long / −1 short |
| `entry_utc` · `entry_px` | TEXT · REAL | |
| `exit_utc` · `exit_px` | TEXT · REAL | null while open |
| `max_fav_ticks` · `max_adv_ticks` | REAL | |
| `n_ticks` · `dur_s` | INT · REAL | path length |
| `partial` | INT | 1 = capture armed mid-trade (entry not seen) |
| `schema` · `src` | TEXT | fidelity discriminator: `schema='ctick.1'`+`src='last'` = tick-true sidecar · `schema='1.3'`+`src='row'` = bar-based ROW trade folded from `council\1.3\` (no tick path) · `src='row'` twins are dropped when a real sidecar lands |
| **council context** (nullable) | | filled for council trades — the VOTE VECTOR is folded in from the `council\1.3\` row corpus (the sidecars don't carry it) |
| `conviction` · `size_mult` · `net_score` · `active_w` | REAL | |
| `votes_json` · `reasons` · `episode_id` · `first_touch` | TEXT/INT | ~99% populated after the fold-in |
| `barrier_ticks` · `ms_to_target_r` · `ms_to_stop_r` | REAL/INT | tick-true first-touch (sidecar) |
| **derived path features** (computed at ingest) | | the "analyze the path" columns |
| `time_to_mfe_s` · `time_to_mae_s` | REAL | |
| `mfe_mae_ratio` | REAL | |
| `adverse_first` | INT | 1 = took heat before it worked (late-ish) vs 0 (favorable first) |
| `ingested_utc` | TEXT | |

### `ticks` — the path, one row per tick
| column | type | notes |
|---|---|---|
| `trade_id` | TEXT | FK → trades |
| `ms` | INT | ms from entry |
| `px` | REAL | bar close now; raw last after the fidelity upgrade |
| `last` · `bid` · `ask` · `vol` | REAL/INT | **added at tick.2** (raw-tick capture) |

`PRIMARY KEY (trade_id, ms)`, index on `trade_id`. A `trade_features` VIEW can pre-compute filter columns
(conviction band, R buckets) for Grafana.

---

## 4. Components to build

| # | component | path | job |
|---|---|---|---|
| A | **DB schema** | `Lab\db\schema.sql` | create `trades` + `ticks` (+ view) |
| B | **Ingester** | `Lab\ingest\ingest.py` | scan `Excursions\ticks\*.jsonl` **and `council\ticks\*.jsonl`** → parse header+ticks → compute path features → upsert into SQLite. **THEN `ingest_council_rows()` folds `council\1.3\*.jsonl`** (the vote-vector corpus) in — enrich existing tick-true trades' NULL vote columns, else insert a bar-based `src='row'` trade; per-file mtime watermark (`row_files` table) keeps `--watch` cheap. **Idempotent** (skip unchanged by mtime). WAL + `busy_timeout` so a backfill and the watch loop coexist. Modes: one-shot · `--watch` (poll 2 s) · `--reingest` (force full backfill) |
| C | **Streamlit explorer** | `Lab\viz\explorer.py` | reads SQLite; sidebar filters (date · inst · bartype · dir · source · conviction band · partial · min ticks); a trade table → click → Plotly path chart (fav-ticks + raw price) + aggregate panels (median path by conviction band, MFE/MAE scatter). Supersedes the file-based `viz\tickpaths.py`. |
| D | **Grafana provisioning** | `Lab\grafana\` | datasource YAML (SQLite → `sentinel.db`) + dashboard JSON: (1) blotter table (2) path viewer (3) aggregates by conviction/bartype (4) capture-health (records/day, missing exits) |
| E | **Always-on runner** | `Lab\run\` | scripts/tasks to keep B (ingest), C (Streamlit), D (Grafana) up so the browser is always live |

---

## 5. Stand-up workflow (one-time)

1. **Python deps** (Lab venv already has pandas/plotly/streamlit): `pip install watchdog` (for `--watch`), nothing else — SQLite is stdlib (`sqlite3`).
2. **Create the DB:** `python Lab\ingest\ingest.py --init` (runs `schema.sql`) then a first full ingest of the existing 4 tick files.
3. **Grafana:** install Grafana OSS (Windows installer *or* Docker), install the **`frser-sqlite-datasource`** plugin, drop the provisioning files from `Lab\grafana\`. Grafana → `localhost:3000`.
4. **Streamlit:** `streamlit run Lab\viz\explorer.py` → `localhost:8501`.

## 5b. Always-on (so the data is "just there")
- **Ingester:** a **Windows Scheduled Task** running `ingest.py --watch` (or every 60 s) at logon → new tick files land in SQLite within a minute.
- **Streamlit:** a small startup script (or Task) running the server in the background.
- **Grafana:** install as a **Windows service** (default) → always up.
- Net: open your browser to either URL anytime → filter → the data's current.

---

## 6. Fidelity prerequisite (the honest caveat)

Today `px` = the Deck tape's `Close[0]` = the **brick close** on TBars/HA bar types, **not raw last-trade**.
Good for *shape*, not for fill-level analysis. The upgrade (separate build, tracked in [[sentinel-ml-lab]]):
- **Deck tape → raw ticks** via `OnMarketData` (`MarketDataEventArgs.Price`), schema **`tick.2`** adding `last/bid/ask/vol`. The DB schema already reserves those columns.
- **Tick-true Council-fire recorder** — the recorder currently stores peaks + minute snapshots, no path. A tick-true version logs the full raw-tick excursion per fire → lands in the SAME `trades`/`ticks` tables (`source='council'`) → lets us finally test **conviction vs. path QUALITY**, not a coarse binary.

---

## 7. Phased plan (build order)

- **Phase 0 — DB + ingest + Streamlit-over-SQLite** (immediate value on the 4 existing files; uses `Close[0]` px, flagged). → "go to localhost:8501, filter."
- **Phase 1 — Grafana** install + SQLite datasource + starter dashboards.
- **Phase 2 — raw-tick capture** (Deck `OnMarketData`, schema tick.2) → re-ingest.
- **Phase 3 — tick-true Council-fire recorder** → council trades in the DB → **path-vs-conviction** analysis (the "don't bury the Council on coarse labels" payoff).
- **Phase 4 — always-on services** (scheduled ingester + Streamlit + Grafana service).

---

## 8. Open decisions (confirm before Phase 0)

1. **SQLite** (recommended) vs Postgres vs InfluxDB. SQLite unless you foresee live-streaming dashboards.
2. **Streamlit+Plotly** (recommended, already built) vs **Plotly Dash** for the "Plotly" front-end. Streamlit is faster to stand up and reuses `tickpaths.py`; Dash if you want a more app-like custom UI.
3. **Ingest cadence:** scheduled task every ~60 s (recommended, simple) vs a live file-watch daemon.
4. **DB location:** `Sentinel\Lab\db\sentinel.db` (Lab-owned, recommended) vs `Sentinel\sentinel.db`.
5. **Grafana install:** native Windows service (recommended for always-on) vs Docker.

> **RESOLVED (all of §7–§8):** SQLite at `Lab\db\sentinel.db` · Streamlit+Plotly · `--watch` ingester · Grafana
> native — every recommended option was taken and shipped. See §9.

---

## 9. Built state (2026-07-14) — what is actually running

- **DB:** `Sentinel\Lab\db\sentinel.db`, **WAL** journal + `busy_timeout=30000` (readers never block the writer; a
  backfill and the live watch loop coexist). ~5,300 `trades`, ~300k `ticks`. **~99% of council trades carry the vote
  vector** after the fold-in.
- **Ingester (`Lab\ingest\ingest.py`)** — two passes per scan: (1) tick sidecars (`Excursions\ticks\` + `council\ticks\`)
  → `trades`+`ticks`; (2) **`ingest_council_rows()`** folds `council\1.3\` — the ONLY place the vote vector lives —
  enriching tick-true trades and backfilling historical no-sidecar fires (`src='row'`). Discriminator: `src='last'` =
  tick-true, `src='row'` = bar-based row; a `row` twin is deleted when its real sidecar arrives (any order). Backfill:
  `python ingest\ingest.py --reingest`. See [[ingester-vote-vector-fold-in]].
- **Recorder** — `SentinelExcursionRecorder_v2_0_0` (v2.1.2). Writes the schema-1.3 ROW (`council\1.3\`) + a per-fire
  tick-path sidecar (`council\ticks\`). **v2.1.2 streams each row to disk the moment its excursion window completes
  (~60 min post-fire)** instead of buffering to session-end, bounding crash-loss of the vote vector to the in-flight
  window.
- **Front-ends:** Streamlit+Plotly `viz\explorer.py` → **localhost:8501** (+ the Council Paths page); Grafana →
  **localhost:3000** (SQLite datasource).
- **Always-on:** the **`SentinelDataPlatform`** scheduled task runs `Lab\run\sentinel-data.bat` at logon (guarded by
  "is :8501 up" so it never dupes), launching the `.venv` ingester (`--watch`) + Streamlit; Grafana runs as a service.
  ⚠ Self-heal is **logon-level, not process-level** — a killed ingester is not respawned mid-session.

## 10. Health layer (2026-07-15) — "is everything alive & safe"

A second, orthogonal surface: **operational health of NT + the whole Sentinel suite**, distinct from the trade-corpus
analytics above. Split by design: **Grafana = ops/health** ("am I safe to trade right now"); **Streamlit/Observatory =
research** ("does the edge exist"). Don't blur them.

- **Probe (`Lab\health\probe.py`)** — samples every **30 s**, **READ-ONLY on NT** (files/process/ports only: `state.json`,
  `sentinel.log`, the `Ledger`, `tasklist`, socket port checks — never NT internals or orders, so a crash here can't touch
  trading). No new deps (`tasklist`, not psutil). Single-instance via a `127.0.0.1:8502` bind (a blind launcher start is
  safe; the bound port doubles as the "probe up" signal). Crash-resistant `--watch` loop (each sample in try/except).
- **Health tables** (same `sentinel.db`, WAL + `busy_timeout`): `health` (wide time-series: NT up/responding, kill,
  connections, feed/risk, service freshness, rolling 5-min err/crit/contention/naked counts, live-Council count, fires
  today, last conviction, DB/WAL size), `governor_health` (per-account day P&L vs cap / loss-stop / status), `arc_slots`
  (per-slot health · pos_qty→naked · fills · P&L), `roster_health` (per-scope present/declared/missing/unexpected, parsed
  from the Council log line), `health_event` (deduped discrete transitions: NT down, kill engaged, …).
- **More tables:** `feed_health` (per-INSTRUMENT lag/stall from `state.json.risk.feeds` — already published by
  RiskService/StateService, **no C# change needed**; empty until a feed is active), `connection_health` (per broker),
  `eye_health`, `copier_health`, `veto_5m`, `scope_health` (**quiet-Council detector** — seconds since last verdict,
  bounded to recently-active scopes). `health` also carries NT **CPU%/RAM/uptime** (psutil, CPU normalized by core
  count), disk free/used%, day-P&L total, win-rate/trades today. Schema grows via an idempotent `_migrate()`.
- **Dashboard `Sentinel · Health`** (uid `sentinel-health`, `Lab\grafana\dashboards\sentinel-health.json`, **52 panels**,
  4 rows — 🛡 Safety · 🧠 Brain · 💰 P&L/accounts · 🔩 Resources/infra). Generated by **`Lab\grafana\build_health_dashboard.py`**
  (edit the generator, re-run; the provider auto-reloads within 30 s, `allowUiUpdates:true`). → **localhost:3000/d/sentinel-health**.
- **⚠ Two gotchas:** (1) a **Grafana auto-update wipes the unsigned `frser-sqlite` plugin** → every dashboard reads
  "No data" (`plugin.notRegistered`); fix = reinstall + `conf\custom.ini` `allow_loading_unsigned_plugins` + restart
  (**self-heal now in `sentinel-data.bat`**). (2) **frser reads an integer time column as SECONDS** → every time-series
  query selects **`ts_ms/1000 AS time`** (raw ms → "Data outside time range").
- **Always-on:** the probe `--watch` + a Grafana-plugin heal were added to `Lab\run\sentinel-data.bat` **before** the
  `:8501` skip-guard, so they run even when the rest of the platform is already up (the probe's `:8502` guard prevents
  dupes). Same logon-level self-heal caveat as the ingester.

---

*The consumer of this DB is the Lab (`Lab\train.py` fits the ConvictionFloor + per-bar-type weights; `Lab\council_paths.py`
grades conviction vs path quality). Corpus hygiene + fill fidelity: [[corpus-hygiene-and-fill-fidelity]].*
