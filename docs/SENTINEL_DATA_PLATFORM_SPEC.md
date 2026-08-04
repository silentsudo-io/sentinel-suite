
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
  backfill and the live watch loop coexist). **~14.8k `trades`, ~6.5M `ticks` and growing** (was ~5,300 at the 07-14
  fold-in). **~99% of council trades carry the vote vector** after the fold-in.
- **Ingester (`Lab\ingest\ingest.py`)** — two passes per scan: (1) tick sidecars (`Excursions\ticks\` + `council\ticks\`)
  → `trades`+`ticks`; (2) **`ingest_council_rows()`** folds `council\1.3\` — the ONLY place the vote vector lives —
  enriching tick-true trades and backfilling historical no-sidecar fires (`src='row'`). Discriminator: `src='last'` =
  tick-true, `src='row'` = bar-based row; a `row` twin is deleted when its real sidecar arrives (any order). Backfill:
  `python ingest\ingest.py --reingest`. See [[ingester-vote-vector-fold-in]].
- **Recorder** — `SentinelExcursionRecorder_v2_0_0` (**v2.1.6**, schema **1.4** / `ctick.3`). Writes the schema ROW
  (`council\1.4\`) + a per-fire tick-path sidecar (`council\ticks\`, self-describing ctick.3 header). **Streams each row to disk the moment its excursion window completes
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

## 11. Corpus snapshot ladder (2026-07-17) — "a frozen, reproducible corpus + a recoverable one"

The corpus is append-only and lives on one disk. Two failure modes it doesn't defend against on its own: a
training run is **not reproducible** (you can't re-fit against "the corpus as it was three weeks ago"), and a
contamination event (the replay-leak class, [[corpus-hygiene-and-fill-fidelity]]) has **no clean rollback point**.
A tiered, timestamped snapshot ladder answers both. Built 2026-07-17.

- **Engine:** `Lab\snapshot\snapshot.py` (subcommands `daily` / `weekly` / `verify <dir>` / `list`; flags
  `--dry-run`, `--date`, `--week`). Wrapper `Lab\run\sentinel-snapshot.bat`. Runs under `Lab\.venv`. Every run
  appends to `Sentinel\sentinel.log` as `[SNAPSHOT]` / `[SNAPSHOT-CRIT]`.

- **Three tiers, validate-before-destruct:**
  - **session** = the LIVE `Excursions\` tree itself. The recorder (§9, v2.1.2) already streams each row to disk
    crash-safe the moment its window completes, so live *is* the continuous session-durable record — a discrete
    session copy (or an NT session-close hook) was **rejected as redundant + fragile**. Live is the ground truth
    the daily captures; there is no separate session artifact.
  - **daily** → `Snapshots\daily\<date>\` — a point-in-time zip of the whole corpus **+ a consistent copy of the
    ~608 MB WAL-mode `sentinel.db` via `VACUUM INTO`** (checkpoints + compacts to one clean file; a raw file-copy
    of a WAL DB is inconsistent). ~118 MB zipped, ~25 s. Pruned only after the covering weekly validates.
  - **weekly** → `Snapshots\weekly\<isoweek>\` — the **permanent master, kept forever**. Validates it is a
    **row-hash superset of the union of that week's dailies**, self-heals any gap into `_healed.jsonl`, then
    destructs the validated dailies.

- **Validation = superset-of-row-content-hashes, NOT a file diff.** Corpus files grow append-only through the day,
  so a byte-compare is useless, but "every line I had before is still present" is exact and schema-agnostic. Each
  snapshot's manifest (per-line `sha256`) is built in the **same pass** that writes the zip → manifest ≡ zip by
  construction, re-checkable any time with `verify`. The daily's own check is **file-presence** (a file present at
  start but unreadable = a real miss → status `gap`); the **row-superset** check is a *weekly-only* concept because
  the dailies are frozen there — and that check is what catches live **shedding** a file (a schema uplift moving it
  to `_archive`, a manual delete), the ladder's real WORM payoff.
  - ⚠ *Design bug caught by driving it:* the first cut validated a daily snapshot's rows ⊇ a **live re-read**, which
    falsely flagged normal forward growth as a gap. A point-in-time snapshot **cannot** be validated against a
    still-appending corpus — only against frozen lower tiers.

- **⚠ Honest caveat (the same shape as §6):** the ladder guarantees **archive INTEGRITY** (no row is ever lost),
  **not corpus CORRECTNESS**. A superset check preserves poisoned replay rows as faithfully as clean ones —
  contamination stays `corpus_probe`'s job ([[corpus-hygiene-and-fill-fidelity]]), not this ladder's. Snapshots
  freeze what the corpus *was*; they don't judge it.

- **Schedule (box is Central; aligned to the CME daily maintenance break so the corpus is settled + the market
  closed during a run):**
  - `SentinelSnapshotDaily` — every day **16:30 CT** (mid-break, before the 17:00 reopen).
  - `SentinelSnapshotWeekly` — **Sunday 16:45 CT** (after Sunday's daily, before reopen; the whole Mon–Sun iso-week
    is closed ⇒ no orphaned dailies, zero live writes during the run).
  - Registered via `Register-ScheduledTask`, LogonType **S4U** — runs **whether logged on or not, no stored
    password**, keeping the Administrator identity so file ACLs / profile paths / the venv resolve exactly as the
    logged-in run (chosen over a SYSTEM principal, which runs in a different profile context). Both verified firing
    headless: `LastRunResult 0x0`.

- **Operate:** `sentinel-snapshot.bat daily|weekly|list`; adjust times in Task Scheduler; disable via
  `Disable-ScheduledTask SentinelSnapshot*`.

- **Disk trajectory (flagged):** weekly master ≈ 118 MB × 52 ≈ **~6 GB/yr**, kept forever (deliberate: full history).
  The DB dominates and is **regenerable** from the corpus rows via `ingest.py`, so the easy future lever if disk
  matters is weekly = corpus-rows-only + keep just the latest DB. Full detail: [[corpus-snapshot-ladder]].

## 12. Additional surfaces (2026-07-19) — more probes, more boards, one DB

Built after §10–§11, all to the SAME recipe (read-only probe → `sentinel.db` → a themed Grafana board; a
guard-port singleton; wired into `sentinel-data.bat` before the `:8501` guard):

- **Corpus-integrity probe** — `Lab\health\corpus_probe.py` (guard **:8503**): reconciles the recorded corpus
  (Ledger ↔ rows ↔ sidecars), schema hygiene, replay-leak → `corpus_integrity`/`corpus_folder`/`corpus_events`.
- **node01 remote probe** — `Lab\health\node01_probe.py` (guard **:8504**): SSH-polls the remote bake worker
  (Tailscale `worker1`) → `node01_health`/`node01_event` → board **`Sentinel · Node01`**
  (localhost:3000/d/sentinel-node01). Surfaces NT render-thread death / bake stall / unreachable. [[distributed-backtest]]
- **Docs-health audit** — `Lab\docs\audit.py` (guard **:8505**, 15-min): scans the docs for drift (broken links,
  stale HTML, contract version-drift, dangling tokens, orphans) → `docs_health`/`docs_finding` → board
  **`Sentinel · Docs`** (localhost:3000/d/sentinel-docs); `--errors-only` is a git pre-commit gate. [[docs-health]]

**Guard-port registry:** 8501 Streamlit explorer · 8502 Health probe · 8503 Corpus probe · 8504 node01 probe · 8505 Docs-health probe · 3000 Grafana. **Four boards, one `sentinel.db`:** `sentinel-trades` · `sentinel-health`
· `sentinel-node01` · `sentinel-docs`.

---

## 13. The corpus ACCEPTANCE GATE (2026-07-24) — "are the rows even usable?"

§10–§12 answer *is the platform alive*. This answers a different and harder question: **is what it just recorded
worth keeping?** A bake can run for hours at full speed, write a clean-looking corpus, and be worthless because
one voter never reached a single row.

### `Lab\verify_votes.py` — per-lane completeness

Stdlib-only, so it runs on the bake worker as well as the main box. Three checks per lane:

| Check | Rule |
|---|---|
| **SEAM** | the bar type's own voter(s) MUST be present — derived from the **bar-type id**, so it needs no config (`212201`/`212202` → `BRK` · `212203` → `FLUX` · `212204` → `BRK`+`CVB`) |
| **DECLARED** | every `Roster.conf` voter (same cascade the Council uses) present as a KEY. Absent = **CRIT**; present on <90% of rows = **WARN** — an intermittent dropout a union-of-rows check would hide |
| **BRK LEVELS** | brick lanes must carry `brkUpper`/`brkLower`, or limit-vs-market grading is dead (Flux exempt by construction) |

**`EXIT 0` = all lanes complete · `1` = WARN (partial, or too thin to judge) · `2` = CRIT (missing data).**

> **A voter recorded as `0` counts as PRESENT.** Abstention versus a missing key is the entire distinction — a
> voter that legitimately has no opinion must not read as a broken sensor.

**It windows on FILE MTIME, not `fireTime`** — and that was a defect in the gate itself, found by driving it. A
replay bake writes rows whose `fireTime` is historical, so a `fireTime` window silently skipped the whole
replayed corpus and would have passed the very bake it was built to catch. Written-at is the only clock that
means *"this bake, now"* for live and replay alike.

### Wiring — probe → table → board

`Lab\health\corpus_probe.py` imports it directly (`import verify_votes as _votes`) and runs it every **300 s**,
writing a per-lane **`vote_health`** row plus per-lane **`corpus_events`** with the existing change-only de-dup,
so each lane alerts *and recovers* independently. Surfaced as the **🗃 Corpus row** on **`Sentinel · Health`**
(localhost:3000/d/sentinel-health): lanes-missing-a-voter / partial / brick-lanes-without-levels, the per-lane
table, and the event log.

> **⚠ Nothing rendered `corpus_events` before this.** The corpus probe had been writing to a DB **no board
> watched** — a monitor whose output nobody could see is not a monitor.

The probe's `busy_timeout` was also raised **8 s → 30 s**. Measured, not guessed: it was losing the race with
`ingest.py --watch` on the multi-GB WAL DB and skipping whole sample cycles — a monitor that silently stops.

### Operator use — the preflight

Start a bake, let it run **~10 minutes**, run `python Lab\verify_votes.py --days 1`, and require **`EXIT=0` with
every lane "complete"** before committing to the long run. This is the standing procedure in
[SENTINEL_RUNBOOK.md](SENTINEL_RUNBOOK.md) §4b ②.

---

## 14. Fault recording in the Lab (2026-07-25) — `lab_faults.swallow()`

The Lab's counterpart to `SentinelCore.Swallow` on the C# side. Same problem, same fix: a probe, an
ingester or a Streamlit page must never die because one malformed row failed to parse — but *don't
propagate* had been implemented as *don't record*, so a component could fail continuously and silently.
The cost is on the record: `ingest.py --watch` ran for three days against a schema it could not read
(§9), and nothing said so.

**`Lab\lab_faults.py`** — stdlib only, no dependencies, so `verify_votes.py` and the health probes stay
deployable standalone to a bake node.

```python
from lab_faults import swallow

try:
    row = json.loads(line)
except json.JSONDecodeError as _swex:
    swallow("ingest.parse", _swex)
    continue          # control flow is UNCHANGED -- swallow() goes before it, never instead of it
```

**Contract (deliberately identical to the C# one):** never raises · never alters control flow ·
rate-limited **per tag** (first 3, then 1/min — the flood fear that made empty handlers attractive) ·
counts everything including throttled occurrences, so `fault_total()` is honest.

| Surface | What it gives you |
|---|---|
| `Lab\logs\lab-faults.log` | timestamped `tag / exception type / message / file:line / pid`, **5 generations** of rotation |
| `faults()` / `fault_total()` | per-tag and total counts for the running process |
| `python -m lab_faults` | tail the log; `--clear` rotates by hand |

**Retention is 5 generations, not one, on purpose.** Single-generation rotation destroyed a live
forensic window twice in one night during the BRK/FLUX investigation (see NOW.md and
[SENTINEL_RUNBOOK.md](SENTINEL_RUNBOOK.md) §4b ⑤).

**Migration state (2026-07-25):** all **53** silent handlers across **23** Lab files now record —
`health\probe.py` 11 · `docs\audit.py` 5 · `verify_votes.py` 5 · `viz\observatory.py` 5, and the rest.
Zero bare `except:` remain. Verified by driving, not by reading: every file imports, `verify_votes.py`
and `docs\audit.py` produce identical output to before, and two real sites (`docs.audit._read` on a
missing path, `sentinel_lab.bartag.bartype_name` on a bad tag) were confirmed to return their original
fallback values *and* write a fault line.

### 14b. The 🧯 Lab-faults row on the Health board (same session)

`health\probe.py` now tails the fault log every cycle into a **`lab_faults`** table (per-tag rollup over
24h) plus two columns on the always-written `health` row, and the `Sentinel · Health` board gained a
**🧯 Lab faults — what failed quietly in the Python** row:
**Swallowed faults · 24h · Distinct fault tags · Processes affected · Suppressed (not logged)**, over a
per-tag table (`tag · occ · logged · procs · first · last · detail`). Amber, not red — a swallowed fault
is something to look at, not something that stops trading, and reserving red for the safety row keeps the
board's alarm vocabulary meaningful. `health_event` gains `labfaults` (level change) and `labfault_new`,
which fires **once per newly-seen tag** — a brand-new silent failure is the thing worth surfacing; a
known steady one is noise.

**Three design points, each of which is the difference between a real monitor and a decorative one:**

1. **The headline counts read `health`, not `lab_faults`.** `health` gets a row every cycle whether or
   not anything failed, so **0 means "measured zero just now."** Reading an empty `lab_faults` would
   show 0 for a *dead probe* too — reproducing, on the board built to abolish it, the exact ambiguity
   between *nothing happened* and *nothing is watching*. (Same defect as §9's ingester liveness being
   inferred from Streamlit's port.)
2. **`occurrences` ≠ `lines`, and the gap is displayed.** Rate limiting suppresses *without writing a
   line*, so 7 hits inside one 60 s window write 3 lines and no marker — anything counting lines would
   report 3 and be confidently wrong. `swallow()` therefore writes a per-tag **exit summary**
   (`SUMMARY n occurrences this process`) via `atexit`, which the probe treats as authoritative per pid.
   ⚠ Still a **lower bound** by nature: a hard-killed process runs no `atexit` handler.
3. **The roll-up is recomputed from the file every cycle — no watermark.** A probe that has to remember
   where it got to is a probe that silently double-counts or skips after a restart.

🐛 **Found by the monitor watching itself, on its first run:** `lab_faults()` tailed the `.1` rotation
generation before it existed, `swallow()`ed the `FileNotFoundError`, and so **manufactured a fault every
cycle** — a monitor generating the very signal it reports. Fixed with an existence check. Verified the
fix by driving it: three consecutive runs on a clean slate produce **zero faults and no log file at
all**, then 6 real occurrences report as `occ=6, logged=3, suppressed=3` end-to-end through the live
daemon.

🔧 **Also fixed while here:** `probe.py` bound its `:8502` single-instance guard *before* the one-shot
branch, so `python probe.py` refused to run while the daemon was up — making the probe un-inspectable
exactly when you most want to inspect it. The guard now binds only under `--watch`, matching
`corpus_probe.py`, which already had it right.

⚠ **Operational note:** the probes are long-lived Python processes and **Python does not reload source**.
After editing `probe.py` you must **restart the probe** or the loop keeps running the old code — the
same class of failure as §9's three-day-stale ingester. `sentinel-data.bat` restarts it guarded, so a
blind re-run is safe.

---
