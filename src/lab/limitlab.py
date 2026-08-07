#!/usr/bin/env python3
"""
limitlab -- does a RESTING LIMIT at the brick boundary beat a MARKET order at the fire?

    cd "Sentinel\\Lab"
    .\\.venv\\Scripts\\python.exe limitlab.py                 # all lanes
    .\\.venv\\Scripts\\python.exe limitlab.py --queue 0,1,5,10,25,50
    .\\.venv\\Scripts\\python.exe limitlab.py --holdout 0.30 --target 30 --stop 30

WHAT THIS ANSWERS, AND WHY IT IS NOT THE ORIGINAL QUESTION
  The limit-level bar was specced to recover a "~9-tick entry bleed". That bleed turned out to be a
  MEASUREMENT ARTIFACT -- `firePx` used to be the Heikin-Ashi synthetic close, a price that never traded
  (memory: firepx-is-synthetic-ha-close). There are no 9 ticks to recover.
  A second premise also died: the spec claimed TBars re-derives its boundaries every tick, which would make a
  resting order impossible to maintain. FALSE -- barMax/barMin are assigned only at bar CREATION, so the
  boundaries are already latched, and BrickState has been publishing them per tick all along.

  So the surviving question is narrow and purely quantitative:

      You decide at a brick close. A market order fills at `firePx`.
      A resting limit at the FORMING bar's boundary (`brkUpper`/`brkLower`, both known in advance)
      fills at a KNOWN price -- but only sometimes.
      Does the better price pay for the trades you never get?

  The prize is the crossing cost (~1 tick/side), NOT 9 ticks. This is a tight race by construction, and the
  honest output is a CURVE over queue depth, not a single number.

THE FILL MODEL (conservative by default)
  Resting BUY at L fills when the tape prints at or below L; SELL at L when it prints at or above L.
  `--queue Q` additionally requires Q contracts to print through the level before you are filled -- we have no
  book depth in the corpus, so Q is approximated as Q tick-prints at-or-through L. Q=0 is the OPTIMISTIC
  touch-fill bound and should never be quoted alone.
  ⚠ A limit backtest that fills on touch is the same class of lie as bar-level excursion (81% -> 37.5% at tick
  resolution). Report the decay with Q; that decay IS the finding.

SELECTION BIAS IS REPORTED, NEVER ASSUMED AWAY
  Unfilled trades are not free -- they are disproportionately the fast ones, i.e. the winners. So the table
  prints fill rate, expectancy ON FILLS, and the market-order expectancy OF THE TRADES THE LIMIT MISSED.
  If the missed set is better than the filled set, a great expR at 30% fill is a worse business than a
  mediocre one at 90%.
"""
from __future__ import annotations
import os, sys, glob, json
from lab_faults import swallow

TICK = {"GC": 0.1, "MGC": 0.1, "SI": 0.005, "CL": 0.01, "ES": 0.25, "MES": 0.25,
        "NQ": 0.25, "MNQ": 0.25, "YM": 1.0, "ZN": 0.015625, "ZB": 0.03125}
SENT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
DIRS = [os.path.join(SENT, "Excursions", "council", "ticks"),
        os.path.join(SENT, "Excursions", "candidates", "ticks"),
        os.path.join(SENT, "Excursions", "_replay", "council", "ticks")]
BRK_RECVER = {"2.3.0", "1.3.0"}       # recorders that stamp brkUpper/brkLower
COST_TICKS = 2.0                       # round-trip: commission + ~1 tick/side crossing (pathlab's "great filter")


def arg(name, default):
    for a in sys.argv[1:]:
        if a.startswith("--" + name + "="):
            return a.split("=", 1)[1]
        if a == "--" + name:
            i = sys.argv.index(a)
            if i + 1 < len(sys.argv):
                return sys.argv[i + 1]
    return default


def load():
    out = []
    for d in DIRS:
        for p in glob.glob(os.path.join(d, "*.jsonl")):
            try:
                with open(p, encoding="utf-8") as fh:
                    h = json.loads(fh.readline())
                    if h.get("recVer") not in BRK_RECVER:
                        continue
                    up, dn = float(h.get("brkUpper", 0) or 0), float(h.get("brkLower", 0) or 0)
                    if up <= 0 or dn <= 0:
                        continue
                    path = []
                    for ln in fh:
                        ln = ln.strip()
                        if ln:
                            o = json.loads(ln)
                            if "px" in o:
                                path.append(float(o["px"]))
            except Exception as _swex:
                swallow("limitlab.load", _swex)
                continue
            if len(path) < 2:
                continue
            out.append(dict(inst=str(h.get("inst", "")), bt=str(h.get("bartype", "")),
                            t=str(h.get("fireTime", "")), dir=int(h["dir"]),
                            fire=float(h["firePx"]), up=up, dn=dn, path=path))
    out.sort(key=lambda r: r["t"])
    return out


def walk(path, i0, entry, d, T, S, tk):
    """Bracket outcome in ticks from `entry`, walking the path forward from index i0."""
    for px in path[i0:]:
        r = d * (px - entry) / tk
        if r >= T:
            return T
        if r <= -S:
            return -S
    return d * (path[-1] - entry) / tk        # censored: mark out at the last print


def main():
    T = float(arg("target", 30)); S = float(arg("stop", 30))
    hold = float(arg("holdout", 0.30))
    QS = [int(x) for x in str(arg("queue", "0,1,5,10,25")).split(",")]
    rows = load()
    if not rows:
        print("No brk-stamped sidecars yet (need recorder v2.3.0 / v1.3.0 output).")
        print("The recorders are deployed; let a chart fire, then re-run.")
        return 2

    lanes = {}
    for r in rows:
        lanes.setdefault((r["inst"], r["bt"]), []).append(r)

    print("limitlab -- resting LIMIT at the latched boundary vs MARKET at the fire")
    print("bracket +%g/-%gt   cost %.1ft round-trip   holdout=last %.0f%% by time\n" % (T, S, COST_TICKS, 100 * hold))

    for (inst, bt), rs in sorted(lanes.items(), key=lambda kv: -len(kv[1])):
        tk = TICK.get(inst, 0.1)
        cut = int(len(rs) * (1 - hold))
        print("== %s %s ==  n=%d  (train %d / holdout %d)" % (inst, bt, len(rs), cut, len(rs) - cut))

        mkt = [walk(r["path"], 0, r["fire"], r["dir"], T, S, tk) - COST_TICKS for r in rs]
        print("   MARKET at fire           expR %+7.2ft   (train %+.2f | holdout %+.2f)"
              % (sum(mkt) / len(mkt),
                 sum(mkt[:cut]) / max(1, cut), sum(mkt[cut:]) / max(1, len(mkt) - cut)))

        print("   %-6s %7s %9s %9s %9s   %s" % ("queue", "fill%", "expR/fill", "train", "holdout", "MISSED trades' market expR"))
        for Q in QS:
            fills, missed = [], []
            for k, r in enumerate(rs):
                d, lvl = r["dir"], (r["up"] if r["dir"] > 0 else r["dn"])
                hit, seen = -1, 0
                for i, px in enumerate(r["path"]):
                    through = (px <= lvl + 1e-9) if d > 0 else (px >= lvl - 1e-9)
                    if through:
                        seen += 1
                        if seen > Q:
                            hit = i
                            break
                if hit < 0:
                    missed.append(mkt[k])
                else:
                    # passive fill EARNS the crossing cost on entry -> half the round-trip stays
                    fills.append((k, walk(r["path"], hit, lvl, d, T, S, tk) - COST_TICKS / 2.0))
            if not fills:
                print("   %-6d %6.0f%% %9s %9s %9s   %s" % (Q, 0, "-", "-", "-", "never filled"))
                continue
            fv = [v for _, v in fills]
            tr = [v for k, v in fills if k < cut]; ho = [v for k, v in fills if k >= cut]
            print("   %-6d %6.0f%% %+9.2f %+9.2f %+9.2f   %s"
                  % (Q, 100 * len(fills) / len(rs), sum(fv) / len(fv),
                     (sum(tr) / len(tr)) if tr else float("nan"),
                     (sum(ho) / len(ho)) if ho else float("nan"),
                     ("%+.2ft (n=%d)" % (sum(missed) / len(missed), len(missed))) if missed else "none missed"))
        print("   ^ Q=0 is touch-fill = the OPTIMISTIC bound; quote the decay, not the Q=0 number.")
        print("   ^ if MISSED expR > filled expR, the limit is selecting AGAINST you regardless of fill%%.\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
