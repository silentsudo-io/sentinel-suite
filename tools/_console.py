#!/usr/bin/env python3
"""_console - make a tool's OUTPUT unbreakable, so a guard can always state its finding.

WHY THIS EXISTS, MEASURED 2026-08-12
    `verify_all.py` printed its PASS lines fine and CRASHED on its FAIL lines. Not a metaphor:

        UnicodeEncodeError: 'charmap' codec can't encode character '\\u2717' in position 2

    Python on Windows encodes stdout with the ANSI codepage (cp1252) whenever the stream is a
    pipe — which is what a git hook, a scheduled watch, a CI step and every `| tee` all are. The
    PASS glyphs (`ok`, `—`) happen to exist in cp1252; the FAIL glyphs (`✗`, `⚠`, `⛔`, `🔴`) do
    not. So the failure path, and ONLY the failure path, dies — and it dies BEFORE printing the
    finding. The session watch reported `verify_all exit 1: UnicodeEncodeError` where the real
    finding was "NO HOOK INSTALLED — this clone will not gate commits at all."

    ⇒ A guard that cannot print its failure has, from the reader's side, no failure. That is the
    same class as a crashed sensor reading as a quiet one: the loudest state must be the one that
    survives the worst environment, not the best.

USE
    import _console; _console.unbreakable_output()

    at the top of any tool that prints. The script's own directory is on sys.path[0] when it is
    run as a script, so this import works from any CWD without path surgery.

WHAT IT DOES, AND WHY NOT SIMPLY FORCE UTF-8 EVERYWHERE
    - piped/redirected stream (hook, watch, CI, file): re-encode as UTF-8. Full fidelity — the
      consumer is a file or another program, and UTF-8 is what the sources are written in.
    - a real console: keep the console's own encoding but set errors="replace", so an
      unrepresentable glyph degrades to `?` instead of raising. Forcing UTF-8 at a legacy cp1252
      console would trade a crash for mojibake across the whole report, which is not an
      improvement for the one human who has to read it.
"""
from __future__ import annotations

import sys


def unbreakable_output() -> None:
    """Make stdout/stderr incapable of raising UnicodeEncodeError. Safe to call more than once."""
    for stream in (sys.stdout, sys.stderr):
        if stream is None:                      # pythonw / detached process
            continue
        reconfigure = getattr(stream, "reconfigure", None)
        if reconfigure is None:                 # already wrapped by a caller (e.g. StringIO)
            continue
        try:
            if stream.isatty():
                reconfigure(errors="replace")
            else:
                reconfigure(encoding="utf-8", errors="replace")
        except (ValueError, OSError):
            # ⚠ Deliberately narrow, and deliberately not fatal: this helper exists to let a tool
            # REPORT. Aborting here because we could not improve the stream would be the failure
            # it is meant to prevent. Anything broader would hide a real bug in a caller.
            pass
