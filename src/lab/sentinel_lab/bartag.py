"""Human-readable bar-type labels — the PYTHON MIRROR of SentinelCore.FriendlyBartag / BartypeName
(bin\\Custom\\AddOns\\SentinelCore_v1_0_0.cs, v1.35.0).

DISPLAY ONLY. The raw bartag ("212203v8") stays the machine key everywhere it matters — the DB
`bartype` column, the corpus filename, the scope join key, and every Streamlit filter VALUE. These
helpers only change what a HUMAN sees (a picker label, a metric). Use them as a Streamlit
`format_func=` so the option values stay raw while the labels read "SentinelFlux 8".

⚠ KEEP IN SYNC with the C# registry (SentinelCore `_bartypeNames`) so the label a human sees in the
explorer matches the `barLabel` the recorder stamps into schema-1.4 rows and the on-chart cards.
"""
from __future__ import annotations
import re
from lab_faults import swallow

# id -> registered name (mirror of SentinelCore._bartypeNames)
_NAMES = {
    212201: "SentinelTBars",       # adaptive HA/Renko brick engine (BrickState -> BRK)
    212202: "SentinelTbarsCount",  # plain brick + ticks-to-next HUD
    212203: "SentinelFlux",        # order-flow imbalance bars (FluxState -> FLUX)
    212204: "SentinelDrift",       # CVB divergence bars (CvbState -> CVB)
    212205: "SentinelLattice",
    212206: "SentinelEffort",
    212207: "SentinelTide",
    2016:   "ERP",                 # legacy (ERP_Type_Bars)
    54321:  "EdsRetrace",          # legacy (EdsRetraceBarsV2)
    69696:  "TBarsElse",           # pre-Sentinel TBars lineage
    69697:  "TbarsCount",          # pre-Sentinel TBars lineage
}

# Nicer overrides for a few known built-in/legacy tags whose generic render would be ugly
# (NT's BarsPeriodType enum ids aren't available in Python, so we name the ones the corpus actually shows).
_STATIC = {
    "0v150x1": "150-tick",
    "9v1x1":   "HA 1-min",
    "4v5x1":   "5-min",
    "1v1x1":   "1-tick",
}

_TAG = re.compile(r"^(\d+)v(\d+)(?:x(\d+))?$")

# The Sentinel brick engines (SentinelTBars, SentinelTbarsCount, ERP, and every TBars-lineage
# derivative) expose ONE "Speed" knob. The Speed-Settings mapping is Value = SS/2, Value2 = SS*2,
# i.e. Value2 == 4*Value and Speed = Value*2 — a STRUCTURAL signature, not a fixed list of ids, so
# any current-or-future derivative is classified for what it IS. So "6/24" == Speed 12, "10/40" ==
# Speed 20, "12/48" == Speed 24, "2/8" == Speed 4. (See tbars-speed-settings-mapping.)
# SentinelFlux's single F6 param is BaseBarsPeriodValue, renamed "Flux Size" (SentinelFlux_v1_0_0.cs
# SetDefaults). It is a SCALE, not a threshold: FluxRefSize = 8 is the size at which fluxScale == 1.0,
# and the imbalance threshold θ* is COMPUTED per bar from the EWMA — it is never dialed in. Excluded
# from the Speed signature because Value2 is forced to 0, so "4*Value" can never match by accident.
_FLUX_BARS = {212203}


def _is_speed(bid, val, val2):
    return bid not in _FLUX_BARS and val2 is not None and val > 0 and val2 == 4 * val


def bartag_speed(bartag):
    """Speed number for a brick bar tag, else None. 'GC...212201v6x24' / '2016v2x8' -> 12 / 4.
    Detected structurally (Value2 == 4*Value); a non-canonical ratio returns None so callers
    don't invent a misleading Speed."""
    if bartag is None:
        return None
    s = str(bartag).split("@", 1)[0]
    if "." in s:
        s = s.rpartition(".")[2]
    m = _TAG.match(s)
    if not m:
        return None
    bid, val, val2 = int(m.group(1)), int(m.group(2)), (int(m.group(3)) if m.group(3) else None)
    return val * 2 if _is_speed(bid, val, val2) else None


def bartype_name(bid) -> str:
    """id -> registered name, else 'Type<id>'."""
    try:
        return _NAMES.get(int(bid), f"Type{int(bid)}")
    except (TypeError, ValueError) as _swex:
        swallow("sentinel_lab.bartag.bartype_name", _swex)
        return str(bid)


def friendly_bartag(bartag) -> str:
    """'212203v8' -> 'SentinelFlux 8'; '212201v6x24' -> 'SentinelTBars 6/24'.
    Handles an optional '@lane' suffix. Returns the raw tag unchanged if it can't be parsed."""
    if bartag is None:
        return bartag
    s = str(bartag)
    lane = ""
    if "@" in s:
        s, lane = s.split("@", 1)
    if s in _STATIC:
        label = _STATIC[s]
    else:
        m = _TAG.match(s)
        if not m:
            return str(bartag)
        bid, val, val2 = m.group(1), m.group(2), m.group(3)
        bid_i = int(bid)
        name = bartype_name(bid)
        val2i = int(val2) if val2 else None
        if _is_speed(bid_i, int(val), val2i):
            label = f"{name} Speed {int(val) * 2}"          # one-knob brick bars -> Speed N
        else:
            # Flux falls through here deliberately: "SentinelFlux 8" is the size, and it matches
            # SentinelCore.FriendlyBartag exactly. It used to read "SentinelFlux thr 8", which named
            # the size as the imbalance threshold — a different quantity the operator never sets.
            label = f"{name} {val}" + (f"/{val2}" if val2 else "")   # fallback: raw ratio
    if lane:
        label += f" · {lane}"
    return label


def friendly_scope(scope) -> str:
    """'GC.212203v8@FooBoo' -> 'GC · SentinelFlux 8 · FooBoo'. The bartag is the final '.'-segment,
    so a dotted instrument ('BRK.B') is safe. Returns raw on failure."""
    if scope is None:
        return scope
    s = str(scope)
    lane = ""
    if "@" in s:
        s, lane = s.rsplit("@", 1)
    if "." not in s:
        return str(scope)
    inst, _, bartag = s.rpartition(".")
    label = f"{inst} · {friendly_bartag(bartag)}"
    if lane:
        label += f" · {lane}"
    return label
