#!/usr/bin/env python3
r"""Compute ground-truth FACTS about the live Sentinel code -> Docs\_generated\facts.json.

Docs reference these via {{tokens}}; the renderer (md2atlas) substitutes them at render time, so
volatile numbers (Core version, voter count) can NEVER drift — they're single-sourced from code.
STATIC-CODE truth: greps the .cs files, needs no NT running, fully deterministic.

    python facts.py            # write facts.json, print a summary
    python facts.py --print    # print facts.json to stdout, don't write

Part of the Docs-Health system — spec: bin\Custom\Docs\SENTINEL_DOCS_HEALTH_SPEC.md.
"""
from __future__ import annotations
import os, re, json, argparse
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
CORE    = os.path.join(CUSTOM, "AddOns", "SentinelCore_v1_0_0.cs")
# ⚠ Council_v1_0_0.cs was ARCHIVED 2026-08-07 (superseded by the v1.11.0 fork, and both
# being loadable was the documented scope-contention hazard). Pointing at the archived file
# would have made voter_count silently WRONG rather than absent -- the token still renders.
COUNCIL = os.path.join(CUSTOM, "Indicators", "Council_v1_11_0.cs")
OUTDIR  = os.path.join(CUSTOM, "Docs", "_generated")
OUT     = os.path.join(OUTDIR, "facts.json")

# Guard-port registry (kept here as the single source; docs render it via {{ports_table}}).
PORTS = {"8501": "Streamlit explorer", "8502": "Health probe", "8503": "Corpus probe",
         "8504": "legacy-node probe", "8505": "Docs-health probe", "3000": "Grafana"}


def _read(p):
    try:
        return open(p, encoding="utf-8", errors="replace").read()
    except OSError as _swex:
        swallow("docs.facts._read", _swex)
        return ""


def _max_semver(text):
    """Highest vX.Y.Z anywhere in the text (a changelog's newest entry = the live version)."""
    vs = re.findall(r'v(\d+)\.(\d+)\.(\d+)', text)
    if not vs:
        return None
    a, b, c = max((int(x), int(y), int(z)) for x, y, z in vs)
    return f"v{a}.{b}.{c}"


def core_version():
    return _max_semver(_read(CORE))


def voter_list():
    m = re.search(r'KnownVoters\s*=\s*\{([^}]*)\}', _read(COUNCIL))
    return re.findall(r'"([A-Z0-9]+)"', m.group(1)) if m else []


def compute():
    voters = voter_list()
    cv = core_version()
    now = dt.datetime.now()
    facts = {
        "core_version": cv or "?",
        "voter_count": len(voters),
        "voter_list": ", ".join(voters),
        "ports_table": " · ".join(f"{p} {n}" for p, n in PORTS.items()),
        "_generated_utc": dt.datetime.now(dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "_sources": {"core_version": "AddOns/SentinelCore_v1_0_0.cs (max semver)",
                     "voter_count": "Indicators/Council_v1_11_0.cs (KnownVoters)"},
    }
    return facts


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--print", dest="show", action="store_true")
    a = ap.parse_args()
    facts = compute()
    if a.show:
        print(json.dumps(facts, indent=2))
        return
    os.makedirs(OUTDIR, exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as fh:
        json.dump(facts, fh, indent=2)
    print(f"wrote {OUT}")
    print(f"  core_version = {facts['core_version']}   voter_count = {facts['voter_count']}")


if __name__ == "__main__":
    main()
