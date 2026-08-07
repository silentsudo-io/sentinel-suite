#!/usr/bin/env python3
r"""scrub.py — the publish-time rename of operator-private NICKNAMES.

THE MODEL. `bin\Custom` is canonical and keeps the truth: a comment reading "found by
driving it on <box>" is the useful sentence for whoever owns the box. The published
snapshot gets those names rewritten. This is the THIRD publish-time transform, alongside
the two already in place -- add the MPL-2.0 header, strip the NinjaTrader generated region
-- and like those it is normalised away by check_parity.py so a scrubbed file does not read
as drift.

⛔ NICKNAMES ONLY. This rewrites identifiers that carry no power: which machine, which
lane, which box. It must NEVER be pointed at an address, a domain, a key or a password.
Those are BLOCKED by the gate (Lab\docs\secretscan.py) and blocking is the FEATURE -- a
scrubber that silently repaired a pasted credential would be worse than no scrubber,
because the mistake would never surface and the credential would still be live. Refusing
teaches; rewriting hides.

THE MAP IS PRIVATE. A map that rewrites `<realname>` -> `worker-1` necessarily contains
`<realname>`, so it cannot live in a public repo. This tool ships without one; it reads
`scrub.conf` from the private tree (see scrub.conf.example) and is a NO-OP without it.

⚠ AND IT SAYS SO. A transform nobody can see is how a snapshot quietly stops matching its
source. Every rewrite is reported, per file, on every run -- and running without a map
prints a warning rather than silently doing nothing, because "0 rewrites" and "no map
loaded" look identical in a log and mean opposite things.

    python scrub.py --check <file>...        # report what WOULD be rewritten
    python scrub.py --in-place <file>...     # rewrite (used by the publish step)
    python scrub.py --map <path> ...         # explicit map location

Env: SENTINEL_SCRUB overrides the map path.
"""
from __future__ import annotations

import argparse
import os
import re
import sys
from pathlib import Path

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

REPO = Path(__file__).resolve().parent.parent
# Default: the private Lab tree, reached from the canonical NinjaTrader root. Overridable,
# because not every checkout sits beside one.
DEFAULT_MAP = Path(os.path.expanduser("~")) / "Documents" / "NinjaTrader 8" / \
    "Sentinel" / "Lab" / "docs" / "scrub.conf"

_WARNED = False


def map_path(explicit: str | None = None) -> Path:
    if explicit:
        return Path(explicit)
    env = os.environ.get("SENTINEL_SCRUB")
    return Path(env) if env else DEFAULT_MAP


def load_map(path: Path | None = None) -> list[tuple[re.Pattern, str]]:
    """[(compiled pattern, replacement)] in file order. Empty list if there is no map."""
    global _WARNED
    p = path or map_path()
    rules: list[tuple[re.Pattern, str]] = []
    try:
        text = p.read_text(encoding="utf-8")
    except OSError:
        if not _WARNED:
            sys.stderr.write(
                "scrub: no map at %s — nickname scrubbing is OFF.\n"
                "       (structural secret rules are unaffected; see scrub.conf.example)\n" % p)
            _WARNED = True
        return rules
    for raw in text.splitlines():
        line = raw.split("#")[0].strip()
        if not line or "->" not in line:
            continue
        pat, _, rep = line.partition("->")
        pat, rep = pat.strip(), rep.strip()
        # ⚠ CONTROL CHARACTERS MEAN THE ESCAPES WERE EATEN, and this is not hypothetical:
        # writing this map through a shell heredoc turned `\1` into 0x01 and `\b` into 0x08,
        # so the rule compiled fine, matched NOTHING, and the tool cheerfully reported
        # success. A scrub that silently stops scrubbing is the worst failure this thing has,
        # because the output still looks clean. Refuse the rule and say why.
        if any(ord(c) < 32 for c in pat + rep):
            sys.stderr.write(
                "scrub: rule %r -> %r contains control characters — the backslash escapes were\n"
                "       mangled in transit (\\1 became 0x01?). Rewrite %s with a real editor.\n"
                "       RULE SKIPPED rather than silently matching nothing.\n"
                % (pat, rep, p))
            continue
        try:
            rules.append((re.compile(pat, re.I), rep))
        except re.error as e:
            sys.stderr.write("scrub: bad pattern %r in %s (%s) — skipped\n" % (pat, p, e))
    return rules


def scrub(text: str, rules=None) -> tuple[str, dict[str, int]]:
    """Apply the map. Returns (text, {pattern: count}). No map ⇒ unchanged, empty counts."""
    rules = load_map() if rules is None else rules
    counts: dict[str, int] = {}
    for rx, rep in rules:
        text, n = rx.subn(rep, text)
        if n:
            counts[rx.pattern] = counts.get(rx.pattern, 0) + n
    return text, counts


def main() -> int:
    ap = argparse.ArgumentParser(description="Publish-time nickname scrub")
    ap.add_argument("files", nargs="+")
    ap.add_argument("--map")
    g = ap.add_mutually_exclusive_group()
    g.add_argument("--check", action="store_true", help="report only (default)")
    g.add_argument("--in-place", action="store_true", help="rewrite the files")
    a = ap.parse_args()

    rules = load_map(map_path(a.map))
    if not rules:
        print("scrub: no rules loaded — nothing to do")
        return 0

    total = 0
    for f in a.files:
        p = Path(f)
        try:
            src = p.read_text(encoding="utf-8")
        except OSError as e:
            sys.stderr.write("scrub: cannot read %s (%s)\n" % (f, e))
            continue
        out, counts = scrub(src, rules)
        if not counts:
            continue
        n = sum(counts.values())
        total += n
        print("  %-58s %d rewrite%s  (%s)" % (p.name, n, "" if n == 1 else "s",
                                              ", ".join(sorted(counts))))
        if a.in_place:
            # newline="" so a rewrite cannot silently convert CRLF -> LF and manufacture
            # whole-file drift in the parity report.
            with open(p, "w", encoding="utf-8", newline="") as fh:
                fh.write(out)
    print("scrub: %d rewrite%s across %d file%s%s"
          % (total, "" if total == 1 else "s", len(a.files),
             "" if len(a.files) == 1 else "s",
             "" if a.in_place else "  (--check: nothing written)"))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
