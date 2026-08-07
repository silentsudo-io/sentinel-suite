# 🔬 The ML Lab

**Rung 10 · the machine counterpart to the Observatory.** Python tooling over the recorded corpus:
ingest, analyse, grade voters, sweep exit policies — and check the suite's own documentation.

## What's here (60 files)
- **`ingest/`** — JSONL → SQLite. The canonical pipeline is *tools → JSONL → ingester → DB → analyser*;
  reading JSONL directly is an expedient, not the architecture.
- **`harness/`** — bar-type and tape harnesses that run outside NinjaTrader.
- **`docs/`** — the documentation-health system: `audit.py` (does a doc still tell the truth?),
  `coverage.py` (does an artefact have a doc at all?), `secretscan.py`, `wiki.py`.
- **`viz/`** — Streamlit explorer and plots. **`grafana/`** — dashboard builders.
- **`AddOns/`** — the NinjaScript side: the Conductor (replay queue) and Quartermaster (data supply).

## Requirements
Python 3.12+, `requirements.txt`. SQLite only — no server.

## ⚠ One methodological warning worth more than the code
**Thirteen files in this tree filter rows post-hoc and report a statistic with no occupancy model.**
They answer *"what was the average R of trades matching this tag?"* when the question you meant was
*"what would this system have earned?"* Those differ whenever fires overlap — and measured on one
corpus the answer **changed sign**: a regime gate read −0.0131R post-hoc and **+0.0044R** when the
engine was actually re-run one-position-at-a-time.

**A filtered mean is not a backtest.** Treat any tag-conditioned number from this tree as a hypothesis
until it has been re-run through an engine that maintains position state.

---

Part of the [Sentinel Suite](../../README.md) · MPL-2.0
