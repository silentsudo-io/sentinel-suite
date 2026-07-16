# Sentinel Documentation

The one door into the Sentinel Suite's documentation — a bound set of living documents, four chapters by
audience, all in one house style. Start with the reading path that fits why you're here.

## Start here — pick your path

- **Just want the idea?** → the [Thesis](SENTINEL_THESIS.html). What Sentinel is and why, in one read: fuse
  many honest signals into one fitted decision, grade it against reality, feed the grade back.
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
| **The plan** | [Roadmap](ROADMAP.html) | the engineering pipeline & forward vision | contributor |
| **The plan** | [Product Ladder](PRODUCT_LADDER.html) | the open-source product & packaging map | contributor |
| **Reference** | [Design System](SENTINEL_DESIGN_SYSTEM.html) | palette · components · build/verify workflow | coder |
| **Reference** | [ML Spec](SENTINEL_ML_SPEC.html) | schema 1.3 instrumentation + the offline Lab | coder |
| **Reference** | [Dataset Dictionary](SENTINEL_DATASET_DICTIONARY.html) | corpus syntax · nomenclature · bar-type & voter registries · how the Lab reads it | coder |
| **Reference** | [Replay Spec](SENTINEL_REPLAY_SPEC.html) | run the loop on historical data · the fusion core | coder |
| **Reference** | [Data Platform Spec](SENTINEL_DATA_PLATFORM_SPEC.html) | the built SQLite corpus + ingester + Streamlit/Grafana (where the graded data lives) | coder |
| **Reference** | [SentinelFlux Bars Spec](SENTINEL_FLUXBARS_SPEC.html) | the order-flow-imbalance bar type — the first genuinely orthogonal axis | coder |

## Specs & references

Every published spec in this set — the specs, the doctrines, and the operator references.

| Document | What it is | For |
|---|---|---|
| [Suite Manual](SENTINEL_SUITE_MANUAL.md) | the prose, zero-to-fluent manual (trader + coder) | anyone |
| [Quick Reference Guide](QuickReferenceGuide.md) | the one-page cheat sheet | anyone |
| [Naming Federation](SENTINEL_NAMING_FEDERATION.md) | the "Sentinel &lt;Thing&gt;" naming law | coder |
| [Ship Manifest](SENTINEL_SHIP_MANIFEST.md) | every tool → its dependencies | coder |
| [Rail Spec](SENTINEL_RAIL_SPEC.md) | the on-chart card rail + CardLayout | coder |
| [Bridge Spec](BRIDGE_SPEC.md) | the automated Council-consumer strategy | coder |
| [Cockpit Spec](SENTINEL_COCKPIT_SPEC.md) | the suite command surface | coder |
| [System Builder Spec](SENTINEL_SYSTEM_BUILDER_SPEC.md) | per-lane roster & profile authoring | coder |
| [Hardening Framework](SENTINEL_HARDENING_FRAMEWORK.md) | the safety substrate (Gate · Ledger · Alerts) | coder |
| [Consistency Governor Spec](CONSISTENCY_GOVERNOR_SPEC.md) | the prop-account daily/trailing governor | coder |
| [Candidate Library](SENTINEL_CANDIDATE_LIBRARY.md) | the indicator candidate catalog | coder |
| [VolEnvelope Spec](SENTINEL_VOLENVELOPE_SPEC.md) | the "honest Bollinger" edge sensor | coder |
| [God Reversal Doctrine](SENTINEL_GOD_REVERSAL_DOCTRINE.md) | the candle-grammar reversal sensor | coder |

*Each document is an `.md` + its rendered `.html`. This is the curated front door to the published set.*

## The system in one line

SENSORS watch (incl. the **order-flow FLUX** axis) → CORE carries → COUNCIL decides → BRIDGE / DECK act → GATE guards →
LEDGER remembers → LENS grades → **LAB learns** (the built SQLite data platform fits the ConvictionFloor + per-bar-type
weights) — and the grade feeds back into the decision.

---

*Every document here is a living document, rendered from Markdown into the shared Field Manual house style.
The Markdown source (`.md`) sits beside each `.html`, so the words are versioned as plain text and the look is
applied by one shared template.*
