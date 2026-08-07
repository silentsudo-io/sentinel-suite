#!/usr/bin/env python3
"""lab_faults — the Lab's counterpart to SentinelCore.Swallow (C#, Core v1.41.0).

WHY THIS EXISTS
---------------
The adversarial review (Docs/SENTINEL_ADVERSARIAL_REVIEW.md) found ~350 empty `catch {}` on the
C# side and their Python equivalents here. The intent was always right — a probe, an ingester or a
Streamlit page must never die because one malformed row failed to parse. The defect is that
*don't propagate* was implemented as *don't record*. Every expensive bug in this project has been
made expensive by exactly that: the BRK/FLUX seam hunt, the 160 false NAKED POSITION criticals, the
Eye never loading, `ingest.py --watch` running for three days against a schema it could not read.

`swallow()` keeps the runtime behaviour byte-for-byte identical — it never raises and never alters
control flow — and adds the one thing that was missing: a RECORD and a COUNT.

CONTRACT (deliberately identical to the C# SentinelCore.Swallow)
---------------------------------------------------------------
  * NEVER raises. Its own body is guarded; a broken recorder must never break a caller.
  * NEVER changes control flow. Put it *before* the existing `pass` / `continue` / `return`.
  * Rate-limited PER TAG: first 3 occurrences always, then at most one per 60 s. This is the
    answer to the flood fear that made empty handlers attractive in the first place.
  * COUNTS everything, including suppressed occurrences, so `fault_total()` is honest.

USAGE
-----
    from lab_faults import swallow

    try:
        row = json.loads(line)
    except json.JSONDecodeError as _swex:
        swallow("ingest.parse", _swex)
        continue

Read the counts:

    from lab_faults import faults, fault_total
    faults()        # {'ingest.parse': 12, 'probe.disk': 1}
    fault_total()   # 13

Or from a shell:

    python -m lab_faults            # print the tail of the fault log
    python -m lab_faults --clear    # rotate the log by hand

LOG RETENTION
-------------
The review's other finding was that single-generation rotation destroyed a forensic window twice in
one night. This keeps LOG_GENERATIONS backups, not one.

Stdlib only, on purpose: `verify_votes.py` and the health probes are deployed standalone to bake
nodes and must not grow a dependency.
"""
from __future__ import annotations

import atexit
import os
import sys
import threading
import time
import traceback

__all__ = ["swallow", "faults", "fault_total", "fault_log_path", "reset"]

# --- configuration ----------------------------------------------------------------------------
_HERE = os.path.dirname(os.path.abspath(__file__))
LOG_DIR = os.environ.get("LAB_FAULTS_DIR") or os.path.join(_HERE, "logs")
LOG_NAME = "lab-faults.log"
LOG_MAX_BYTES = 2 * 1024 * 1024
LOG_GENERATIONS = 5

# First N occurrences of a tag always print; after that, at most one per _THROTTLE_SECONDS.
_ALWAYS_FIRST = 3
_THROTTLE_SECONDS = 60.0

# Mirror to stderr when a human is watching. Daemons (probes, ingester, Streamlit) get the file only.
_STDERR = os.environ.get("LAB_FAULTS_STDERR")
if _STDERR is None:
    try:
        _MIRROR_STDERR = bool(sys.stderr) and sys.stderr.isatty()
    except Exception:
        _MIRROR_STDERR = False
else:
    _MIRROR_STDERR = _STDERR not in ("", "0", "false", "False")

# --- state ------------------------------------------------------------------------------------
_lock = threading.Lock()
_counts: dict[str, int] = {}
_emitted: dict[str, int] = {}     # how many of _counts[tag] actually reached the log
_last_emit: dict[str, float] = {}
_recorder_broken = False  # set once if the recorder itself fails; stops it retrying forever


def fault_log_path() -> str:
    """Absolute path of the fault log (the file may not exist until the first swallow)."""
    return os.path.join(LOG_DIR, LOG_NAME)


def _rotate_if_needed(path: str) -> None:
    try:
        if os.path.getsize(path) < LOG_MAX_BYTES:
            return
    except OSError:
        return
    # Shift .4 -> .5, .3 -> .4, ... , base -> .1  (keep GENERATIONS of history, not one)
    for i in range(LOG_GENERATIONS - 1, 0, -1):
        src = "%s.%d" % (path, i)
        dst = "%s.%d" % (path, i + 1)
        if os.path.exists(src):
            try:
                os.replace(src, dst)
            except OSError:
                pass
    try:
        os.replace(path, path + ".1")
    except OSError:
        pass


def _write(line: str) -> None:
    global _recorder_broken
    if _recorder_broken:
        return
    try:
        os.makedirs(LOG_DIR, exist_ok=True)
        path = fault_log_path()
        _rotate_if_needed(path)
        with open(path, "a", encoding="utf-8") as fh:
            fh.write(line + "\n")
    except Exception:
        # The recursion guard. This is one of the very few handlers in the tree that is
        # DELIBERATELY silent: a fault recorder that raises, or that recurses into itself,
        # is worse than no recorder. Give up permanently rather than spin.
        _recorder_broken = True


def swallow(tag: str, exc: BaseException | None = None, detail: str | None = None) -> None:
    """Record a swallowed exception. Never raises; never changes control flow.

    tag     stable dotted identifier for the site, e.g. "probe.disk" or "ingest.parse".
            Throttling and counting are PER TAG, so keep it stable across runs.
    exc     the caught exception (optional but strongly preferred).
    detail  extra context, e.g. the offending file name.
    """
    try:
        now = time.time()
        with _lock:
            n = _counts.get(tag, 0) + 1
            _counts[tag] = n
            if n <= _ALWAYS_FIRST:
                emit = True
            else:
                emit = (now - _last_emit.get(tag, 0.0)) >= _THROTTLE_SECONDS
            if emit:
                _last_emit[tag] = now
                _emitted[tag] = _emitted.get(tag, 0) + 1
        if not emit:
            return

        if exc is not None:
            kind = type(exc).__name__
            msg = str(exc).replace("\n", " ")[:400]
            where = ""
            tb = getattr(exc, "__traceback__", None)
            if tb is not None:
                try:
                    frame = traceback.extract_tb(tb)[-1]
                    where = " at %s:%d" % (os.path.basename(frame.filename), frame.lineno)
                except Exception:
                    where = ""
            body = "%s: %s%s" % (kind, msg, where)
        else:
            body = "(no exception object)"

        if detail:
            body += " | %s" % str(detail).replace("\n", " ")[:200]
        if n > _ALWAYS_FIRST:
            body += "  [x%d total for this tag]" % n

        stamp = time.strftime("%Y-%m-%d %H:%M:%S", time.localtime(now))
        proc = os.path.basename(sys.argv[0]) if sys.argv and sys.argv[0] else "python"
        line = "%s  [%s] %s  %s" % (stamp, tag, body, "(%s pid %d)" % (proc, os.getpid()))

        _write(line)
        if _MIRROR_STDERR:
            try:
                sys.stderr.write("[lab-fault] %s: %s\n" % (tag, body))
            except Exception:
                pass
    except Exception:
        # See the recursion-guard note in _write. Never let the recorder take a caller down.
        pass


def _flush_summary() -> None:
    """At process exit, write one authoritative total per tag whose count the log under-reports.

    WHY THIS IS NEEDED: rate limiting suppresses without leaving a trace. Seven occurrences inside
    one 60 s window write three lines and no marker, so anything reading the log alone would report
    3 and be confidently wrong -- the exact failure mode this module exists to remove. The marker on
    a later line fixes it eventually in a long-lived process; this fixes it at exit for a short one.
    Best-effort by nature: a hard kill runs no atexit handler, so a reader must still treat the log
    as a LOWER BOUND, never as proof that nothing else happened.
    """
    try:
        with _lock:
            pending = [(t, n, _emitted.get(t, 0)) for t, n in _counts.items() if n > _emitted.get(t, 0)]
        if not pending:
            return
        stamp = time.strftime("%Y-%m-%d %H:%M:%S")
        proc = os.path.basename(sys.argv[0]) if sys.argv and sys.argv[0] else "python"
        for tag, total, shown in pending:
            _write("%s  [%s] SUMMARY %d occurrences this process (%d written)  (%s pid %d)"
                   % (stamp, tag, total, shown, proc, os.getpid()))
    except Exception:
        pass


atexit.register(_flush_summary)


def faults() -> dict:
    """Per-tag swallow counts for this process (includes throttled occurrences)."""
    with _lock:
        return dict(_counts)


def fault_total() -> int:
    """Total swallowed exceptions in this process."""
    with _lock:
        return sum(_counts.values())


def reset() -> None:
    """Clear the in-process counters (tests only)."""
    with _lock:
        _counts.clear()
        _last_emit.clear()


def _main(argv) -> int:
    path = fault_log_path()
    if "--clear" in argv:
        if os.path.exists(path):
            for i in range(LOG_GENERATIONS - 1, 0, -1):
                src, dst = "%s.%d" % (path, i), "%s.%d" % (path, i + 1)
                if os.path.exists(src):
                    try:
                        os.replace(src, dst)
                    except OSError:
                        pass
            try:
                os.replace(path, path + ".1")
                print("rotated -> %s.1" % path)
            except OSError as e:
                print("could not rotate: %s" % e)
                return 1
        else:
            print("no fault log at %s" % path)
        return 0

    if not os.path.exists(path):
        print("no fault log yet: %s" % path)
        return 0
    try:
        with open(path, "r", encoding="utf-8", errors="replace") as fh:
            lines = fh.readlines()
    except OSError as e:
        print("cannot read %s: %s" % (path, e))
        return 1
    tail = lines[-40:]
    print("%s  (%d lines, showing last %d)" % (path, len(lines), len(tail)))
    print("-" * 78)
    for ln in tail:
        sys.stdout.write(ln)
    return 0


if __name__ == "__main__":
    raise SystemExit(_main(sys.argv[1:]))
