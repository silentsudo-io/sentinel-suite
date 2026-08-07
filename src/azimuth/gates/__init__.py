"""gates — the Sentinel Azimuth parity harness (SENTINEL_AZIMUTH_SPEC §2, THE PARITY LAW).

Two implementations of one definition can silently disagree. This package is the gate that stands
between the Python column and every conclusion drawn from it.

    from gates import get, rows_side, run_gate

    spec = get("bartype")
    ref  = rows_side("NT",        nt_bars,     meta=nt_meta)
    cmp  = rows_side("Azimuth", python_bars, meta=py_meta)
    v    = run_gate(spec, ref, cmp)
    print(v.to_text());  raise SystemExit(v.exit_code)     # 0 PASS · 1 FAIL · 2 ABORT

Command line (from `Sentinel\\Azimuth`):

    python -m gates list
    python -m gates describe --artefact council
    python -m gates selftest
    python -m gates compare --artefact bartype \\
        --ref-jsonl nt_bars.jsonl --ref-meta nt.meta.json --ref-label NT \\
        --cmp-parquet py_bars.parquet --cmp-meta py.meta.json --cmp-label Azimuth
"""
from .parity import (EXACT, NON_NUMERIC, FORBIDDEN_PAIR_KEYS, ArtefactSpec, Field, Side,
                     SpecError, Verdict, compare_value, run_gate, swallow)
from .artefacts import SPECS, describe, get, kinds, register
from .loaders import (jsonl_side, load_meta, parquet_side, rows_side, sqlite_side, tape_meta)

__version__ = "0.1.0"

__all__ = [
    "EXACT", "NON_NUMERIC", "FORBIDDEN_PAIR_KEYS",
    "ArtefactSpec", "Field", "Side", "SpecError", "Verdict",
    "compare_value", "run_gate", "swallow",
    "SPECS", "describe", "get", "kinds", "register",
    "jsonl_side", "load_meta", "parquet_side", "rows_side", "sqlite_side", "tape_meta",
    "__version__",
]
