"""`bars` -- the Sentinel bar types, in Python, each behind a parity gate.

SPEC §1 (the two columns): one definition, two implementations. SPEC §2 (the parity
law): the Python side is not trusted until the gate says the two agree.

THE PACKAGE CONTRACT -- all of it
---------------------------------
A bar type is a CALLABLE:

    build(tape: Tape, **params) -> BarSeries

`BarSeries` (see `series.py`) carries the bar type's own OHLCV plus `end_idx` -- the
tape row that closed each bar -- which is `engine.bars.bars_from_end_idx`'s existing
seam. `to_engine_bars(series)` hands the result to the backtest engine.

A module registers itself AT IMPORT:

    from . import register
    register("renko", renko, params_str=..., nt_period_type=11, doc="...")

Nothing imports it by name. `__init__` walks its own directory once and imports every
module that is not private, not a test and not part of the plumbing; each one registers
itself on the way past. Dropping `tbars.py` or `flux.py` into this folder is the whole
installation step.

⚠ A module that fails to import is NOT silently skipped. It lands in
`DISCOVERY_ERRORS`, `kinds()` reports it, `get()` names it in its error message, and
`raise_discovery_errors()` turns it back into the exception. A crashed bar type must
not be indistinguishable from an absent one -- that is `eye-never-loads-bug` wearing a
different hat.
"""
from __future__ import annotations

import importlib
import os
import pkgutil
import sys
from dataclasses import dataclass
from typing import Callable

# The Azimuth root on sys.path, so `from engine.bars import ...` resolves whether this
# package was reached by `python -m bars.gate`, by pytest, or by an import from app code.
_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if _ROOT not in sys.path:
    sys.path.insert(0, _ROOT)

from .series import (  # noqa: E402  (must follow the sys.path bootstrap)
    GATE_FIELDS,
    BarSeries,
    gate_rows,
    ticks_to_price,
    to_engine_bars,
)

__all__ = [
    "BarSeries", "BarType", "DISCOVERY_ERRORS", "GATE_FIELDS", "build", "gate_rows",
    "get", "kinds", "raise_discovery_errors", "register", "ticks_to_price",
    "to_engine_bars",
]

#: modules that are plumbing, not bar types
_NOT_A_BARTYPE = {"series", "tapeio", "ntdump", "gate", "__main__"}


class BarTypeError(Exception):
    """A bar type could not be resolved or built."""


@dataclass(frozen=True)
class BarType:
    """A registered bar type."""

    name: str
    build: Callable[..., BarSeries]
    #: params -> the canonical settings string that goes in `bar_params`. Both columns
    #: must produce the SAME string or the gate aborts on identity, which is the point.
    params_str: Callable[..., str]
    #: NinjaTrader's `BarsPeriodType` integer, so a dump's header can be matched to a
    #: port without a human asserting the correspondence. None = no NT counterpart.
    nt_period_type: int | None = None
    #: params -> the Sentinel bartag NT would report (`SentinelCore.BarTag`), so the gate
    #: can FIND the right reference dump instead of being told which file to trust.
    bartag: Callable[..., str] | None = None
    doc: str = ""


REGISTRY: dict[str, BarType] = {}
#: (module_name, exception) for every module that failed to import during discovery
DISCOVERY_ERRORS: list[tuple[str, BaseException]] = []


def register(name: str, build: Callable[..., BarSeries], *,
             params_str: Callable[..., str], nt_period_type: int | None = None,
             bartag: Callable[..., str] | None = None,
             doc: str = "", replace: bool = False) -> BarType:
    if name in REGISTRY and not replace:
        raise BarTypeError(
            "bar type %r is already registered. Two builders under one name is how a gate "
            "ends up blessing something other than what its verdict claims." % name)
    bt = BarType(name=name, build=build, params_str=params_str,
                 nt_period_type=nt_period_type, bartag=bartag, doc=doc)
    REGISTRY[name] = bt
    return bt


def get(name: str) -> BarType:
    if name not in REGISTRY:
        msg = "unknown bar type %r (registered: %s)" % (name, ", ".join(sorted(REGISTRY)) or "none")
        if DISCOVERY_ERRORS:
            msg += "; modules that FAILED to import: " + ", ".join(
                "%s (%s: %s)" % (m, type(e).__name__, e) for m, e in DISCOVERY_ERRORS)
        raise BarTypeError(msg)
    return REGISTRY[name]


def kinds() -> list[str]:
    """Registered names, plus a `BROKEN:<module>` entry per failed import."""
    return sorted(REGISTRY) + sorted("BROKEN:" + m for m, _ in DISCOVERY_ERRORS)


def raise_discovery_errors() -> None:
    """Re-raise the first discovery failure. Call it in a test; a broken sibling
    module must be able to fail a build, not just be missing from a list."""
    if DISCOVERY_ERRORS:
        mod, exc = DISCOVERY_ERRORS[0]
        raise BarTypeError("bars.%s failed to import: %s: %s (+%d more)"
                           % (mod, type(exc).__name__, exc, len(DISCOVERY_ERRORS) - 1)) from exc


def build(name: str, tape, **params) -> BarSeries:
    """Build `name` over `tape`. The one call an outside caller needs."""
    return get(name).build(tape, **params)


def _discover() -> None:
    for mod in pkgutil.iter_modules([os.path.dirname(os.path.abspath(__file__))]):
        n = mod.name
        if n.startswith("_") or n.startswith("test_") or n in _NOT_A_BARTYPE:
            continue
        try:
            importlib.import_module("." + n, __name__)
        except Exception as exc:                       # noqa: BLE001 -- recorded, never swallowed
            DISCOVERY_ERRORS.append((n, exc))


_discover()
