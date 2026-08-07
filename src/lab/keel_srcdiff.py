"""keel_srcdiff — is SentinelKeel still a faithful transcription of the frozen control?

WHY THIS EXISTS
---------------
The Keel programme's whole acceptance test is the EQUIVALENCE GATE: with BracketMode=AtrBracket and
default parameters, `SentinelKeel` must produce a trade list identical to `RangeFilterATRStrategy` on
the same data. "Instrumentation that changes behaviour is not instrumentation."

That gate is a REPLAY test and it costs bake time. This is the cheap pre-check that runs in a second
and catches the overwhelmingly likely way the gate breaks: someone edits Keel's signal or order logic
— to fix a defect, to "improve" a comment, to refactor — and the transcription silently drifts from
the control. A drifted Keel does not fail loudly; it produces a plausible trade list that answers a
different question, and every number downstream becomes uninterpretable.

⚠ THIS IS NOT THE EQUIVALENCE GATE. It compares SOURCE, so it proves the two implementations still
say the same thing, never that they DO the same thing. A real gate needs both strategies run over the
same replay data with their trade lists diffed. Passing here and skipping that is precisely the
"improving the fidelity of a scrapped experiment" error in a new costume.

WHAT IT ALLOWS
--------------
Keel is the control plus LEAF instrumentation, so exactly two classes of difference are legal:
  * ADDED lines that are instrumentation calls (whitelist below) — leaves that cannot alter control flow
  * the SetStopLoss/SetProfitTarget pair MOVED into ApplyBracket, called with identical arguments
Anything else is drift and exits 1.

    cd "Sentinel\\Lab" && python keel_srcdiff.py
"""
from __future__ import annotations

import difflib
import io
import re
import sys
from pathlib import Path

# The Windows console is cp1252, so a bare `print("⚠ …")` raises UnicodeEncodeError — and because that
# escapes main(), the process exits 1 and a PASSING check reports as FAILING. A gate that cries wolf on
# its own output is worse than no gate. Same guard md2atlas.py already carries for the same reason.
try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

CUSTOM = Path(r"C:\Users\Administrator\Documents\NinjaTrader 8\bin\Custom")
CONTROL = CUSTOM / "Strategies" / "RangeFilterATRStrategy.cs"
KEEL_GLOB = "SentinelKeel_v*.cs"

METHODS = ["OnBarUpdate", "OnExecutionUpdate", "SubmitLong", "SubmitShort",
           "StopTicksFor", "TargetTicksFor", "AtrDistanceToTicks"]

# Added lines that are permitted: pure instrumentation leaves + the bracket-mode fork.
ALLOWED_ADD = (
    "StampCross(",
    "RecordFill(",
    "NoteEntry(",
    "ApplyBracket(",
    "if (BracketMode == KeelBracketMode.FixedTicks)",
)
# Removed lines that are permitted: the bracket calls, which moved verbatim into ApplyBracket.
ALLOWED_DEL = (
    "SetStopLoss(",
    "SetProfitTarget(",
)


def method_bodies(path: Path, names: list[str]) -> dict:
    """Brace-matched method bodies. Deliberately dumb: no C# parser, and it does not need one —
    these are flat methods and a false 'differs' is cheap while a false 'identical' is not."""
    text = io.open(path, encoding="utf-8-sig").read()
    out = {}
    for n in names:
        m = re.search(r"^[ \t]*(?:protected|private|public).*\b" + re.escape(n) + r"\s*\(", text, re.M)
        if not m:
            out[n] = None
            continue
        i = text.index("{", m.end())
        depth, j = 0, i
        while j < len(text):
            if text[j] == "{":
                depth += 1
            elif text[j] == "}":
                depth -= 1
                if depth == 0:
                    break
            j += 1
        out[n] = text[i:j + 1]
    return out


def normalise(body: str | None) -> list[str]:
    """Strip comments and whitespace — a comment change must never fail this check, and an
    indentation change must never pass it by accident."""
    if body is None:
        return []
    body = re.sub(r"/\*.*?\*/", "", body, flags=re.S)
    body = re.sub(r"//.*", "", body)
    return [ln.strip() for ln in body.splitlines() if ln.strip()]


def main() -> int:
    keels = sorted((CUSTOM / "Strategies").glob(KEEL_GLOB))
    if not CONTROL.exists():
        print("FAIL  control not found:", CONTROL)
        return 2
    if not keels:
        print("FAIL  no", KEEL_GLOB, "in", CUSTOM / "Strategies")
        return 2

    rc = 0
    ctrl = method_bodies(CONTROL, METHODS)
    for keel in keels:
        print(f"\n{keel.name}  vs  {CONTROL.name}")
        print("=" * 74)
        kl = method_bodies(keel, METHODS)
        for m in METHODS:
            a, b = normalise(ctrl[m]), normalise(kl[m])
            if ctrl[m] is None or kl[m] is None:
                print(f"  {m:<20} MISSING in {'control' if ctrl[m] is None else 'keel'}")
                rc = 1
                continue
            if a == b:
                print(f"  {m:<20} identical ({len(a)} lines)")
                continue
            changed = [l for l in difflib.unified_diff(a, b, lineterm="", n=0)
                       if l[:1] in "+-" and not l.startswith(("---", "+++"))]
            bad = []
            for l in changed:
                body, sign = l[1:].strip(), l[0]
                ok = (sign == "+" and any(body.startswith(p) for p in ALLOWED_ADD)) or \
                     (sign == "-" and any(body.startswith(p) for p in ALLOWED_DEL))
                if not ok:
                    bad.append(l)
            if bad:
                rc = 1
                print(f"  {m:<20} ⛔ DRIFT — {len(bad)} unexpected change(s)")
                for l in bad:
                    print("        " + l)
            else:
                print(f"  {m:<20} instrumented ({len(changed)} expected insertion(s))")

    print()
    if rc == 0:
        print("PASS  Keel is a faithful transcription: every difference is a whitelisted leaf.")
        print("      ⚠ This is the SOURCE pre-check, NOT the equivalence gate. Run both strategies")
        print("        over the same replay data and diff the TRADE LISTS before quoting any result.")
    else:
        print("FAIL  Keel's signal/order logic has drifted from the frozen control.")
        print("      Fix Keel — the control is frozen and is never the thing that moves.")
    return rc


if __name__ == "__main__":
    sys.exit(main())
