#!/usr/bin/env python3
"""
check_bundle_deps.py — bundle dependency-completeness checker.

Catches the class of bug that shipped a broken first cut: a bundle file references
a *custom* Sentinel type that the bundle does not include, so the download fails to
compile (NinjaTrader builds all of bin\\Custom into ONE assembly, so one CS0246 /
CS0103 takes the whole tree down).

The seam-based boundary inventory only tracked `...State` seam links, so a plain-C#
transitive dependency (e.g. SentinelTBars -> Shared/TbarsSudoV3Config.cs) slipped
through. This checks the actual references, not just seams.

HOW IT WORKS
  * A "bundle" = a folder under src/ (except `runtime/`, which is shared by all).
  * A type is "custom" if WE define it somewhere in the universe (default: this repo's
    own src/; pass --universe to point at the full private bin\\Custom tree — the
    authoritative release-time check, which also catches a type that lives in the
    private tree but is shipped in NO bundle).
  * For each bundle, every custom type it references must be DEFINED in a file that
    bundle ships (its own files + runtime). Anything referenced-but-not-shipped is a
    MISSING DEPENDENCY.

USAGE
  # public CI (self-scan): universe = this repo's src/
  python tools/check_bundle_deps.py

  # release-time (authoritative): universe = the full private tree
  python tools/check_bundle_deps.py --universe "C:/Users/Administrator/Documents/NinjaTrader 8/bin/Custom"

Exit code 0 = all bundles self-contained; 1 = missing dependency found.
"""
import argparse
import re
import sys
from pathlib import Path

# Only PUBLIC / INTERNAL types can be referenced from another file, so only those can
# be a cross-bundle dependency. A nested `private sealed class Track` (an internal helper)
# must be excluded, or a same-named method call (`_sp.Track(...)`) becomes a false positive.
DEF_RE = re.compile(
    r'\b(?:public|internal)\s+(?:static\s+|sealed\s+|abstract\s+|partial\s+)*'
    r'(?:class|struct|enum|interface|delegate)\s+([A-Za-z_]\w*)'
)
IDENT_RE = re.compile(r'\b([A-Za-z_]\w*)\b')


# NinjaTrader's own SDK source ships inside bin\Custom too (ADX.cs, ATR.cs, ...).
# Those are resolvable by NT's compiler and must NOT count as "custom types we ship".
# Discriminator: NT source carries a "Copyright ... NinjaTrader" header; ours does not.
NT_SDK_RE = re.compile(r'Copyright.*NinjaTrader', re.IGNORECASE)


def strip_comments(src: str) -> str:
    # good-enough: drop // line comments and /* */ blocks (and thus commented-out refs)
    src = re.sub(r'/\*.*?\*/', ' ', src, flags=re.DOTALL)
    src = re.sub(r'//[^\n]*', ' ', src)
    return src


def is_nt_sdk(f: Path) -> bool:
    try:
        head = f.read_text(encoding='utf-8', errors='replace')[:2000]
    except OSError:
        return False
    return bool(NT_SDK_RE.search(head))


# A private bin\Custom is a junk drawer: NT SDK source + third-party vendor tools +
# old user files, most sharing generic type names (Range, Draw, Channel, Show...).
# When --universe points there, keep ONLY Sentinel-authored files so the universe of
# "types we must ship" is precise. A file is Sentinel if any of these hold.
SENTINEL_HDR_RE = re.compile(r'Sentinel Suite|silentsudo|namespace\s+[\w.]*\.Sentinel', re.IGNORECASE)


def is_sentinel(f: Path) -> bool:
    if f.name.startswith('Sentinel'):
        return True
    if 'Shared' in f.parts:          # Sentinel's suite-shared code (e.g. TbarsSudoV3Config)
        return True
    try:
        head = f.read_text(encoding='utf-8', errors='replace')[:2000]
    except OSError:
        return False
    return bool(SENTINEL_HDR_RE.search(head))


def defined_types(files, skip_nt_sdk=False) -> set:
    out = set()
    for f in files:
        if skip_nt_sdk and is_nt_sdk(f):
            continue
        out.update(DEF_RE.findall(strip_comments(f.read_text(encoding='utf-8', errors='replace'))))
    return out


def referenced_idents(files) -> set:
    out = set()
    for f in files:
        out.update(IDENT_RE.findall(strip_comments(f.read_text(encoding='utf-8', errors='replace'))))
    return out


def cs_files(root: Path):
    return [p for p in root.rglob('*.cs') if '.git' not in p.parts]


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--repo', default=str(Path(__file__).resolve().parent.parent),
                    help='repo root (default: parent of tools/)')
    ap.add_argument('--universe', action='append', default=[],
                    help='extra tree(s) whose type definitions count as "custom" '
                         '(e.g. the full private bin\\Custom). Repeatable.')
    args = ap.parse_args()

    repo = Path(args.repo)
    src = repo / 'src'
    if not src.is_dir():
        print(f'ERROR: no src/ under {repo}', file=sys.stderr)
        return 2

    runtime = src / 'runtime'
    runtime_files = cs_files(runtime) if runtime.is_dir() else []

    # universe of custom type names = everything WE define.
    # The repo's own src/ is all ours by definition — take it wholesale.
    universe = defined_types(cs_files(src))
    # Extra trees (e.g. the full private bin\Custom) are junk drawers — keep ONLY
    # Sentinel-authored files, so vendor/NT type names don't become false positives.
    for u in args.universe:
        r = Path(u)
        if not r.is_dir():
            print(f'WARN: universe path not found: {r}', file=sys.stderr)
            continue
        sentinel_files = [f for f in cs_files(r) if is_sentinel(f) and not is_nt_sdk(f)]
        universe |= defined_types(sentinel_files)

    bundles = sorted(d for d in src.iterdir() if d.is_dir() and d.name != 'runtime')
    problems = 0
    for b in bundles:
        bfiles = cs_files(b)
        if not bfiles:
            continue
        shipped = defined_types(bfiles + runtime_files)
        refs = referenced_idents(bfiles)
        # a custom type this bundle uses but neither defines nor gets from runtime
        missing = sorted((refs & universe) - shipped)
        if missing:
            problems += len(missing)
            print(f'\n[MISSING DEP] bundle "{b.name}" references custom types it does not ship:')
            for t in missing:
                users = [str(f.relative_to(repo)) for f in bfiles
                         if re.search(rf'\b{re.escape(t)}\b', strip_comments(f.read_text(encoding="utf-8", errors="replace")))]
                print(f'    - {t}   (used in: {", ".join(users)})')
        else:
            print(f'[ok] {b.name}: {len(bfiles)} files, self-contained')

    if problems:
        print(f'\nFAIL: {problems} missing dependency reference(s). '
              f'Ship the defining file with the bundle (see docs/CONTRIBUTING).', file=sys.stderr)
        return 1
    print('\nPASS: every bundle is self-contained.')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
