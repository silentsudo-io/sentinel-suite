#!/usr/bin/env python3
"""Control tests for check_bundle_deps.py.

⭐ WHY THIS FILE EXISTS. On 2026-08-09 the `bundle-deps` gate had been RED on every push
since the 2026-08-07 full-suite release — eight consecutive failures over three days —
and half of what it reported was noise: 15 of 31 findings were nested types whose simple
name was being treated as a cross-bundle reference. **A gate that cries wolf on half its
output is a gate people stop reading**, and this one was being ignored while it was also
reporting 16 TRUE cross-bundle breaks that hand a downloader a CS0246.

The fix narrows the type universe to TOP-LEVEL types. The danger with any narrowing is
that it quietly stops catching real things, so these tests pin BOTH directions:
a real dependency must still be found, and the nested-name noise must be gone.

    python tools/test_bundle_deps.py
"""
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from check_bundle_deps import (ManifestError, closure, load_manifest,  # noqa: E402
                               strip_strings, top_level_types)
import check_bundle_deps  # noqa: E402

import _console                                                     # noqa: E402
_console.unbreakable_output()   # a guard that cannot PRINT its failure has no failure

FAILED = []


def check(name: str, cond: bool, detail: str = "") -> None:
    if cond:
        print("  ok   %s" % name)
    else:
        print("  FAIL %s   %s" % (name, detail))
        FAILED.append(name)


NESTED = """
namespace NinjaTrader.NinjaScript.AddOns
{
    public static class SentinelExcursions_v1_0
    {
        public sealed class Pt { public double X; }
        public enum Group { A, B }
    }
}
"""

TOP = """
namespace NinjaTrader.NinjaScript.AddOns
{
    public static class SentinelCopierService_v0_1_0 { }
    public sealed class CopierConfig { }
}
"""

# The trap the string-stripping exists for: a format string full of braces sitting
# BEFORE a top-level type would inflate the brace depth and hide it.
BRACEY_STRING = '''
namespace NinjaTrader.NinjaScript.AddOns
{
    public static class First
    {
        void Log() { Print($"{a} of {b} and {c}"); }
    }
    public sealed class SecondTopLevel { }
}
'''

TWO_NAMESPACES = """
namespace A { public class OneTop { public class OneNested { } } }
namespace B { public class TwoTop { } }
"""

print("check_bundle_deps — control tests\n")

t = top_level_types(NESTED)
check("the outer type is top-level", "SentinelExcursions_v1_0" in t)
check("a nested class is NOT in the universe", "Pt" not in t, "Pt leaked")
check("a nested enum is NOT in the universe", "Group" not in t, "Group leaked")

t = top_level_types(TOP)
check("two top-level types in one namespace are both kept",
      {"SentinelCopierService_v0_1_0", "CopierConfig"} <= t, sorted(t))

t = top_level_types(BRACEY_STRING)
check("a format string's braces do not hide a later top-level type",
      "SecondTopLevel" in t, "string braces inflated the depth — REAL DEPS WOULD BE MISSED")
check("...and the first type is still found", "First" in t)

t = top_level_types(TWO_NAMESPACES)
check("top-level types in two namespaces are both kept", {"OneTop", "TwoTop"} <= t, sorted(t))
check("nested inside the second namespace is still excluded", "OneNested" not in t)

check("strip_strings removes a verbatim string", '"' not in strip_strings('x = @"a""b"; y'))
check("strip_strings removes an escaped quote", '"' not in strip_strings(r'x = "a\"b"; y'))

# ⛔ The narrowing must not silence a REAL cross-bundle dependency in the live tree.
# `deck` genuinely uses services from copier / prop-kit / recorder / observatory.
repo = Path(__file__).resolve().parent.parent
src = repo / "src"
if src.is_dir():
    from check_bundle_deps import cs_files, defined_types, referenced_idents
    runtime_files = cs_files(src / "runtime") if (src / "runtime").is_dir() else []
    universe = defined_types(cs_files(src))
    deck = src / "deck"
    if deck.is_dir():
        bfiles = cs_files(deck)
        missing = (referenced_idents(bfiles) & universe) - defined_types(bfiles + runtime_files)
        # ⛔ Do NOT pin a version-suffixed type name here. This control was written as
        # `"SentinelCopierService_v0_1_0" in missing` and went red the day the copier
        # forked to v0.2.0 — an assertion that must be hand-edited at every version bump
        # is one that eventually gets edited without thought. Assert the PROPERTY: deck
        # still reports a dependency on a type the copier bundle OWNS, whatever it is called.
        copier_owned = defined_types(cs_files(src / "copier")) if (src / "copier").is_dir() else set()
        check("a real cross-bundle dep is STILL reported (deck -> a copier-owned type)",
              bool(missing & copier_owned), sorted(missing & copier_owned) or sorted(missing))
        check("the nested-name noise is gone (deck no longer reports `Pt`)",
              "Pt" not in missing, sorted(missing))
else:
    print("  skip live-tree checks: no src/")


# ---------------------------------------------------------------- the manifest
def _manifest(text):
    d = Path(tempfile.mkdtemp())
    p = d / "bundles.conf"
    p.write_text(text, encoding="utf-8")
    return load_manifest(p)


m = _manifest("# c\ndeck = copier, prop-kit\n\nprop-kit=copier\n")
check("manifest parses, comments and blanks ignored",
      m == {"deck": ["copier", "prop-kit"], "prop-kit": ["copier"]}, m)
check("closure is transitive", set(closure("deck", m)) == {"deck", "copier", "prop-kit"},
      closure("deck", m))
check("a bundle with no line requires only itself", closure("solo", m) == ["solo"])

try:
    _manifest("a = b\na = c\n")
    check("a bundle declared twice is refused", False, "accepted a duplicate")
except ManifestError:
    check("a bundle declared twice is refused", True)

try:
    _manifest("this line has no equals sign\n")
    check("a malformed line is refused", False, "accepted junk")
except ManifestError:
    check("a malformed line is refused", True)

try:
    closure("a", {"a": ["b"], "b": ["a"]})
    check("a dependency CYCLE is fatal", False, "a cycle was accepted")
except ManifestError:
    check("a dependency CYCLE is fatal", True)


# ⛔ THE GATE MUST STILL BE ABLE TO FAIL. Point it at a manifest that declares nothing:
# the 16 real cross-bundle references must come straight back. A gate that has never
# failed is not a gate, and a manifest is exactly the kind of change that can blunt one.
if src.is_dir():
    empty = Path(tempfile.mkdtemp()) / "empty.conf"
    empty.write_text("# no edges\n", encoding="utf-8")
    argv = sys.argv
    sys.argv = ["check_bundle_deps.py", "--manifest", str(empty)]
    try:
        rc = check_bundle_deps.main()
    finally:
        sys.argv = argv
    check("with NO declared edges the gate FAILS again (it can still fail)", rc == 1,
          "exit %r — the manifest blunted the check" % rc)

    argv = sys.argv
    sys.argv = ["check_bundle_deps.py"]
    try:
        rc = check_bundle_deps.main()
    finally:
        sys.argv = argv
    check("with the real manifest the tree PASSES", rc == 0, "exit %r" % rc)

print("\n%s" % ("FAILED: " + ", ".join(FAILED) if FAILED else "PASS — every control holds."))
raise SystemExit(1 if FAILED else 0)
