# Sentinel Documentation

The one door into the Sentinel Suite's documentation — a bound set of living documents, four chapters by
audience, all in one house style. Start with the reading path that fits why you're here.

## Start here — pick your path

- **Just want the idea?** → the [Thesis](SENTINEL_THESIS.html). What Sentinel is and why, in one read: fuse
  many honest signals into one fitted decision, grade it against reality, feed the grade back.
- **Want the whole thing on one page?** → the two visual guides: the
  [Architecture Map](SENTINEL_ARCHITECTURE_MAP.html) (every layer and tool, including the 53-file Python Lab,
  with honest per-tool status) and the [Runtime Topology](SENTINEL_RUNTIME_TOPOLOGY.html) (what actually runs,
  who owns each artifact, and the boundaries that bite).
- **Want to run or extend it?** → the [Field Manual](SENTINEL_PROCESS_ATLAS.html). The illustrated,
  end-to-end manual — from "I have no idea what this is" to "I can run it, extend it, and trust it."
- **Here to contribute or plan?** → the [Roadmap](ROADMAP.html) (the engineering pipeline) and the
  [Product Ladder](PRODUCT_LADDER.html) (the open-source product/packaging map).
- **Writing code for it?** → the [Design System](SENTINEL_DESIGN_SYSTEM.html) (palette, components, build),
  the [ML Spec](SENTINEL_ML_SPEC.html) (schema + the offline Lab), the
  [Dataset Dictionary](SENTINEL_DATASET_DICTIONARY.html) (corpus syntax, nomenclature & how the Lab reads
  it).

## The chapters

| Chapter | Document | What it is | For |
|---|---|---|---|
| **Why** | [The Sentinel Thesis](SENTINEL_THESIS.html) | the at-altitude argument — fuse · grade · feed back | anyone |
| **How / What** | [Field Manual](SENTINEL_PROCESS_ATLAS.html) | the illustrated, end-to-end manual | trader + coder |
| **The picture** | [Architecture Map](SENTINEL_ARCHITECTURE_MAP.html) | all 12 layers + every tool (bar clocks · 25 voters · services · order sources · the Python Lab) with per-tool status | anyone |
| **The picture** | [Runtime Topology](SENTINEL_RUNTIME_TOPOLOGY.html) | in-process vs out-of-process ownership · one-writer artifact table · thread/lifecycle rules · the six invariants | coder |
| **The plan** | [Roadmap](ROADMAP.html) | the engineering pipeline & forward vision | contributor |
| **The plan** | [Product Ladder](PRODUCT_LADDER.html) | the open-source product & packaging map | contributor |
| **Reference** | [Design System](SENTINEL_DESIGN_SYSTEM.html) | palette · components · build/verify workflow | coder |
| **Reference** | [ML Spec](SENTINEL_ML_SPEC.html) | schema 1.3 instrumentation + the offline Lab | coder |
| **Reference** | [Dataset Dictionary](SENTINEL_DATASET_DICTIONARY.html) | corpus syntax · nomenclature · bar-type & voter registries · how the Lab reads it | coder |
| **Reference** | [Replay Spec](SENTINEL_REPLAY_SPEC.html) | run the loop on historical data · the fusion core | coder |
| **Reference** | [Data Platform Spec](SENTINEL_DATA_PLATFORM_SPEC.html) | the built SQLite corpus + ingester + Streamlit/Grafana (where the graded data lives) | coder |
| **Reference** | [SentinelFlux Bars Spec](SENTINEL_FLUXBARS_SPEC.html) | the order-flow-imbalance bar type — the first genuinely orthogonal axis | coder |

*The table above is the **curated front door** — the reading path, not the inventory. Every document in
`Docs\` is listed in §Complete registry below.*

---

## Complete registry — every document in `Docs\`

**This section is the INDEX OF RECORD (2026-08-03).** Before it existed, 41 of 72 documents had nothing
linking to them, so `Lab\docs\audit.py`'s orphan check was firing on two thirds of the corpus and meant
nothing. ⇒ **"Orphan" now means "missing from this registry."** A doc not listed here is either new and
unregistered, or it should be in `_archive\`.

> **Rule for adding a doc:** a new `.md` in `Docs\` gets a row here in the same commit. One line, honest
> status. If you cannot state its status in a line, that is a signal about the document, not about the rule.

### Data platform & corpus
| doc | status |
|---|---|
| [Data Platform Spec](SENTINEL_DATA_PLATFORM_SPEC.md) | ✅ BUILT & LIVE — SQLite + ingester + Streamlit + Grafana + health layer |
| [Dataset Dictionary](SENTINEL_DATASET_DICTIONARY.md) | the authoritative corpus reference · ⚠ no status line |
| [ML Spec](SENTINEL_ML_SPEC.md) | SPEC (2026-07-09) — schema + the offline Lab |

### Replay & the Conductor
| doc | status |
|---|---|
| [Replay Spec](SENTINEL_REPLAY_SPEC.md) | run the loop on historical data · ⚠ no status line |

### Strategy & signal
| doc | status |
|---|---|
| [Bridge Spec](BRIDGE_SPEC.md) | ✅ BUILT — retained as design record (internal v0.2.3) |
| [Consistency Governor Spec](CONSISTENCY_GOVERNOR_SPEC.md) | ✅ BUILT 2026-07-03, pending live validation |
| [Thesis](SENTINEL_THESIS.md) | the at-altitude argument · ⚠ no status line |
| [God Reversal Doctrine](SENTINEL_GOD_REVERSAL_DOCTRINE.md) | ⚠ no status line |
| [Candidate Library](SENTINEL_CANDIDATE_LIBRARY.md) | ⚠ no status line |

### Bar types
| doc | status |
|---|---|
| [FluxBars Spec](SENTINEL_FLUXBARS_SPEC.md) | ✅ BUILT & LIVE-VALIDATED (2026-07-14) |

### UI, suite & naming
| doc | status |
|---|---|
| [Design System](SENTINEL_DESIGN_SYSTEM.md) | the exacting living spec — read before building any tool · ⚠ no status line |
| [Naming Federation](SENTINEL_NAMING_FEDERATION.md) | RATIFIED LAW + migration ledger |
| [Cockpit Spec](SENTINEL_COCKPIT_SPEC.md) | ✅ BUILT — `SentinelCockpit_v0_1_0` |
| [Rail Spec](SENTINEL_RAIL_SPEC.md) | proposed, **not built**; the three CardLayout bugs are fixed |
| [Hardening Framework](SENTINEL_HARDENING_FRAMEWORK.md) | the safety substrate · ⚠ no status line |
| [System Builder Spec](SENTINEL_SYSTEM_BUILDER_SPEC.md) | Phases 0 + 1 BUILT |
| [VolEnvelope Spec](SENTINEL_VOLENVELOPE_SPEC.md) | ⚠ no status line |

### Reference & meta
| doc | status |
|---|---|
| [Roadmap](ROADMAP.md) · [Product Ladder](PRODUCT_LADDER.md) | the pipeline and the packaging map |
| [Suite Manual](SENTINEL_SUITE_MANUAL.md) · [README](../README.md) | zero-to-fluent manual · repo front page |
| [Ship Manifest](SENTINEL_SHIP_MANIFEST.md) | what ships |

## The system in one line

SENSORS watch (incl. the **order-flow FLUX** axis) → CORE carries → COUNCIL decides → BRIDGE / DECK act → GATE guards →
LEDGER remembers → LENS grades → **LAB learns** (the built SQLite data platform fits the ConvictionFloor + per-bar-type
weights) — and the grade feeds back into the decision.

---

*Every document here is a living document, rendered from Markdown into the shared Field Manual house style.
The Markdown source (`.md`) sits beside each `.html`, so the words are versioned as plain text and the look is
applied by one shared template.*
