#!/usr/bin/env python
"""bake_preflight - refuse to start a Strategy Analyzer cell that would answer the wrong question.

WHY (Keel test plan gate item 9): swapping the strategy on the SA tab via `nt8bridge configure`
SILENTLY RESETS the commission -- after a swap both templates read
`BacktestCommissionTemplate=null` and `IncludeCommission=False`. The run then completes, reports
healthy, and produces a COMMISSION-FREE trade list. Test-plan Q4 is "what does a fill actually
cost", so a commission-free matrix does not merely lose precision, it answers the question
wrongly while looking perfect. That happened for real during the equivalence gate: the
+4.36 x 318 = 1386.48 delta was commission, not behaviour.

The whole point is that the failure is INVISIBLE at the end. So it has to be caught at the start.

DESIGN RULES, learned from this project's own bugs:
  * A MISSING property is a FAILURE, never a pass. `ok` + `applied: []` is how `configure`
    reported success having done nothing; a checker that treats "couldn't read it" as "fine"
    reproduces that bug one layer up.
  * Exit code is the contract: 0 = safe to run, 1 = do not run. Scriptable as a hard gate.
  * The HOLDOUT is guarded here too. The pre-registered split (EXPLORE = NQ 06-26,
    HOLDOUT = NQ 09-26 2026-06-21..07-17) is only worth something if spending it takes a
    deliberate act, so touching it requires --allow-holdout and says so loudly.

USAGE
  python bake_preflight.py --strategy SentinelKeel_v0_1_0 --instrument "NQ 06-26" \
                           --from 2026-04-19 --to 2026-06-18 --commission "<template name>"
  python bake_preflight.py --require-tick-fill        # corpus bakes must fill at tick resolution
"""
from __future__ import annotations
import os, sys, json, argparse, subprocess, datetime as dt

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from lab_faults import swallow  # noqa: E402

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception as e:  # noqa: BLE001 - reported, never silent
    swallow("bake_preflight.stdout_reconfigure", e)

NT8BRIDGE = os.environ.get("NT8BRIDGE_PY", r"C:\ntbv\Scripts\python.exe")

# The pre-registered HOLDOUT. Spending it must be an explicit act, not a default.
HOLDOUT = dict(instrument="NQ 09-26", start=dt.date(2026, 6, 21), end=dt.date(2026, 7, 17))


class Check:
    def __init__(self):
        self.fail, self.warn, self.ok = [], [], []

    def require(self, cond, label, detail=""):
        (self.ok if cond else self.fail).append((label, detail))

    def advise(self, cond, label, detail=""):
        (self.ok if cond else self.warn).append((label, detail))


def probe(timeout=90):
    """Ask NT for the live SA tab state. A probe that cannot run is a FAILURE, not a skip."""
    r = subprocess.run([NT8BRIDGE, "-m", "nt8bridge", "probe", "--timeout", str(timeout)],
                       capture_output=True, text=True, timeout=timeout + 30)
    if r.returncode != 0:
        raise RuntimeError(f"nt8bridge probe exited {r.returncode}: {r.stderr[:300]}")
    try:
        return json.loads(r.stdout)
    except json.JSONDecodeError as e:
        raise RuntimeError(f"probe returned non-JSON (NT modal? AddOn not loaded?): {e}") from e


def props(section):
    """Properties come back as a LIST of dicts, not a flat dict -- misreading this once led to
    'the tab is not configured' when the tab was fine."""
    return {p["name"]: p.get("value") for p in (section or {}).get("properties", [])}


def as_date(v):
    for f in ("%m/%d/%Y %I:%M:%S %p", "%m/%d/%Y %H:%M:%S", "%Y-%m-%d"):
        try:
            return dt.datetime.strptime(str(v).strip(), f).date()
        except ValueError:
            continue
    return None


def main():
    ap = argparse.ArgumentParser(description="gate a Strategy Analyzer cell before it runs")
    ap.add_argument("--strategy", help="expected strategy CLASS name, e.g. SentinelKeel_v0_1_0")
    ap.add_argument("--instrument", help="expected instrument, e.g. 'NQ 06-26'")
    ap.add_argument("--from", dest="dfrom", help="expected From date YYYY-MM-DD")
    ap.add_argument("--to", dest="dto", help="expected To date YYYY-MM-DD")
    ap.add_argument("--commission", help="expected BacktestCommissionTemplate name "
                                         "(omit = just require SOME template + IncludeCommission)")
    ap.add_argument("--require-tick-fill", action="store_true",
                    help="corpus bakes must fill at tick resolution, not bar-level")
    ap.add_argument("--allow-holdout", action="store_true",
                    help="permit a range inside the pre-registered HOLDOUT (deliberate act)")
    ap.add_argument("--json", action="store_true", help="machine-readable result")
    a = ap.parse_args()

    try:
        d = probe()
    except Exception as e:  # noqa: BLE001 - reported, and it is fatal by design
        swallow("bake_preflight.probe", e)
        print(f"PREFLIGHT FAILED: could not read the SA tab -- {e}")
        print("  Cannot verify the cell, so the cell must not run. "
              "(NT down? startup modal? AddOn not loaded?)")
        return 1

    tab = props(d.get("tabStrategyProperties"))
    tpl = props(d.get("strategyTemplate"))
    c = Check()

    # ---- gate item 9: COMMISSION. The reason this file exists. -----------------------------
    inc = str(tpl.get("IncludeCommission", "")).strip().lower()
    ctpl = str(tpl.get("BacktestCommissionTemplate", "")).strip()
    has_ctpl = ctpl not in ("", "null", "None")
    c.require("IncludeCommission" in tpl, "IncludeCommission is readable",
              "absent from the probe -> cannot verify -> refuse")
    c.require(inc == "true", "IncludeCommission = True", f"got {tpl.get('IncludeCommission')!r}")
    c.require(has_ctpl, "BacktestCommissionTemplate is set", f"got {ctpl!r}")
    if a.commission and has_ctpl:
        c.require(ctpl == a.commission, "commission template matches",
                  f"expected {a.commission!r}, got {ctpl!r}")

    # ---- identity of the cell ---------------------------------------------------------------
    if a.strategy:
        got = str(tab.get("Strategy", "")).strip()
        c.require(got == a.strategy, "strategy matches",
                  f"expected {a.strategy!r}, got {got!r} "
                  f"(configure takes the CLASS name and accepts a wrong one SILENTLY)")
    if a.instrument:
        got = str(tab.get("InstrumentOrInstrumentList", "")).strip()
        c.require(got == a.instrument, "instrument matches", f"expected {a.instrument!r}, got {got!r}")

    d_from, d_to = as_date(tpl.get("From")), as_date(tpl.get("To"))
    if a.dfrom:
        want = as_date(a.dfrom)
        c.require(d_from == want, "From matches", f"expected {want}, got {d_from}")
    if a.dto:
        want = as_date(a.dto)
        c.require(d_to == want, "To matches", f"expected {want}, got {d_to}")

    # ---- the holdout must be spent deliberately, never by drift -----------------------------
    inst_now = str(tab.get("InstrumentOrInstrumentList", "")).strip()
    overlaps = (inst_now == HOLDOUT["instrument"] and d_from and d_to
                and not (d_to < HOLDOUT["start"] or d_from > HOLDOUT["end"]))
    if overlaps and not a.allow_holdout:
        c.require(False, "HOLDOUT NOT TOUCHED",
                  f"{inst_now} {d_from}..{d_to} overlaps the pre-registered holdout "
                  f"({HOLDOUT['start']}..{HOLDOUT['end']}). Pass --allow-holdout to spend it.")
    elif overlaps:
        c.warn.append(("SPENDING THE HOLDOUT (--allow-holdout)",
                       "this is a one-way door -- write the decision down first"))

    # ---- fill realism -----------------------------------------------------------------------
    ofr = str(tpl.get("OrderFillResolution", "")).strip()
    if a.require_tick_fill:
        c.require(ofr.lower() == "high", "tick fill resolution",
                  f"OrderFillResolution={ofr!r}; bar-level fills flatter excursion "
                  f"(81% -> 37.5% in the fill-resolution lesson)")
    else:
        c.advise(ofr.lower() == "high", "tick fill resolution", f"OrderFillResolution={ofr!r}")
    c.advise(str(tpl.get("Slippage", "0")).strip() in ("0", "0.0"), "slippage left at 0",
             f"Slippage={tpl.get('Slippage')!r}; Q4 measures cost from the Ledger's intended-vs-fill, "
             f"so an injected constant would overwrite the measurement with an assumption")

    passed = not c.fail
    if a.json:
        print(json.dumps(dict(passed=passed,
                              fail=[dict(check=k, detail=v) for k, v in c.fail],
                              warn=[dict(check=k, detail=v) for k, v in c.warn],
                              ok=[k for k, _ in c.ok]), indent=2))
        return 0 if passed else 1

    print(f"\n{'='*74}\n  BAKE PREFLIGHT   strategy={tab.get('Strategy')!r}  "
          f"inst={tab.get('InstrumentOrInstrumentList')!r}  {d_from}..{d_to}\n{'='*74}")
    for k, v in c.ok:
        print(f"  [ok]   {k}")
    for k, v in c.warn:
        print(f"  [WARN] {k}" + (f"\n           {v}" if v else ""))
    for k, v in c.fail:
        print(f"  [FAIL] {k}" + (f"\n           {v}" if v else ""))
    if passed:
        print(f"\n  PREFLIGHT PASSED -- safe to run this cell.\n")
    else:
        print(f"\n  PREFLIGHT FAILED ({len(c.fail)} blocking) -- DO NOT RUN THIS CELL.")
        print("  A run started now would complete, look healthy, and answer the wrong question.\n")
    return 0 if passed else 1


if __name__ == "__main__":
    sys.exit(main())
