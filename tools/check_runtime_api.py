#!/usr/bin/env python3
"""
check_runtime_api.py — member-level runtime API checker.

WHY THIS EXISTS
  check_bundle_deps.py catches a missing *type*. It cannot catch a missing *member*,
  and the member is what keeps biting:

    * A card fix was backported verbatim from bin\\Custom and called
      `SentinelCore.Swallow` — which arrived in Core v1.41.0 while the PUBLISHED core
      was v1.36.0. Every public install would have been CS0117.
    * The same trap was hit again shipping SentinelBinds (23 call sites).

  Both were caught by hand, both nearly shipped. This makes it mechanical: every
  `SentinelCore.X` / `SentinelSkin.X` a bundle references must be DECLARED in the
  runtime that ships beside it.

  The rule this enforces is the one CONTRIBUTING states but had no tooling for:
  **verify against the PUBLISHED runtime, not your local one.**

USAGE
    python tools/check_runtime_api.py                 # check every bundle
    python tools/check_runtime_api.py binds deck      # check named bundles

Exit 0 = every referenced member exists. Exit 1 = at least one would not compile.
"""
from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
SRC = REPO / "src"
RUNTIME = SRC / "runtime"

# The static facades a bundle may call into.
FACADES = ("SentinelCore", "SentinelSkin")

# `Facade.Member` — the reference we must be able to resolve.
USE_RE = re.compile(r'\b(' + '|'.join(FACADES) + r')\s*\.\s*([A-Za-z_]\w*)')

# Declarations, at any nesting depth. Deliberately generous: a FALSE PASS is a
# missed bug, but a FALSE FAIL is a linter people switch off, and this one has to
# survive contact with 300 KB of hand-written C#.
DECL_RES = [
    # types
    re.compile(r'\b(?:public|internal|private|protected)\s+(?:static\s+|sealed\s+|abstract\s+|partial\s+|readonly\s+)*'
               r'(?:class|struct|enum|interface|delegate)\s+([A-Za-z_]\w*)'),
    # methods / properties / fields / consts — "<modifiers> <type> Name" then ( { ; = or ,
    re.compile(r'\b(?:public|internal|private|protected)\s+(?:static\s+|readonly\s+|const\s+|virtual\s+|override\s+|sealed\s+|extern\s+|unsafe\s+|volatile\s+|async\s+|new\s+)*'
               r'[\w<>\[\],\.\?]+\s+([A-Za-z_]\w*)\s*(?=[\(\{;=,])'),
    # enum members / object-initializer-ish bare names inside an enum block
    re.compile(r'^\s*([A-Z]\w*)\s*(?:=\s*[^,\n]+)?,\s*$', re.MULTILINE),
]


def strip_code(src: str) -> str:
    """Comments and string literals out. A member named in prose is not a call."""
    src = re.sub(r'/\*.*?\*/', ' ', src, flags=re.DOTALL)
    src = re.sub(r'//[^\n]*', ' ', src)
    src = re.sub(r'@"(?:[^"]|"")*"', '""', src)
    src = re.sub(r'"(?:[^"\\\n]|\\.)*"', '""', src)
    return src


def cs_files(root: Path) -> list[Path]:
    return [p for p in root.rglob('*.cs') if '.git' not in p.parts]


def declared_names(files: list[Path]) -> set[str]:
    out: set[str] = set()
    for f in files:
        code = strip_code(f.read_text(encoding='utf-8', errors='replace'))
        for rx in DECL_RES:
            out.update(rx.findall(code))
    return out


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('bundles', nargs='*', help='bundles to check (default: all)')
    args = ap.parse_args()

    if not RUNTIME.is_dir():
        print(f'ERROR: no runtime at {RUNTIME}', file=sys.stderr)
        return 2

    runtime_files = cs_files(RUNTIME)
    api = declared_names(runtime_files)
    print(f'runtime: {len(runtime_files)} files, {len(api)} declared names\n')

    names = args.bundles or sorted(
        d.name for d in SRC.iterdir() if d.is_dir() and d.name != 'runtime')

    problems = 0
    for name in names:
        b = SRC / name
        if not b.is_dir():
            print(f'ERROR: no such bundle: {name}', file=sys.stderr)
            return 2
        files = cs_files(b)
        if not files:
            continue

        missing: dict[str, list[str]] = {}
        total = 0
        for f in files:
            code = strip_code(f.read_text(encoding='utf-8', errors='replace'))
            for facade, member in USE_RE.findall(code):
                total += 1
                if member not in api:
                    missing.setdefault(f'{facade}.{member}', []).append(f.name)

        if missing:
            problems += len(missing)
            print(f'[MISSING API] bundle "{name}" calls members the shipped runtime does not declare:')
            for ref, users in sorted(missing.items()):
                uniq = sorted(set(users))
                print(f'    - {ref}   (in: {", ".join(uniq)})')
            print()
        else:
            print(f'[ok] {name}: {total} runtime member references, all resolve')

    if problems:
        print(f'\nFAIL: {problems} member reference(s) would not compile against the '
              f'shipped runtime.\nEither ship the newer runtime, or use only what the '
              f'published core declares.', file=sys.stderr)
        return 1
    print('\nPASS: every bundle resolves against the shipped runtime.')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
