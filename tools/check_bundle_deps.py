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
  * For each bundle, every custom type it references must be DEFINED inside its
    CLOSURE — its own files, its declared requirements from `tools/bundles.conf`
    (transitively), and runtime/. Anything else is a MISSING DEPENDENCY.

BUNDLES DEPEND ON BUNDLES, AND THAT IS DELIBERATE (2026-08-09)
  This used to require every bundle to be self-contained, and after the full-suite
  release that was simply false: `deck` really does use the Copier and Risk services.
  It reported 16 true breaks for three days and nobody acted, because 15 more were
  noise. The rule is now "a documented install compiles" rather than "every folder is
  an island" — the alternatives were duplicating files (⛔ CS0101 duplicate-class for
  anyone installing two bundles: NT compiles bin\\Custom as ONE assembly, so that is
  strictly worse than the CS0246 being prevented) or dissolving everything into
  runtime/ until it means nothing.

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


# Brace counting decides top-level vs nested (see `defined_types`), and a FORMAT STRING
# is full of braces: `Log($"{a} of {b}")`. Left in, those inflate the depth and a
# genuinely top-level type reads as nested -- which would DROP it from the universe and
# MISS a real dependency. That is the dangerous direction, so strings go first.
_STR_RE = re.compile(r'@"(?:[^"]|"")*"|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'', re.DOTALL)


def strip_strings(src: str) -> str:
    return _STR_RE.sub(' ', src)


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


def top_level_types(src: str) -> set:
    """Type names another FILE could reference by their simple name.

    ⭐ NESTED TYPES ARE EXCLUDED, AND EXCLUDING THEM LOSES NO COVERAGE — that is the
    whole argument, so it is written down rather than trusted.
    A `public sealed class Pt` nested inside `SentinelExcursions_v1_0` is reachable
    from another file only as `SentinelExcursions_v1_0.Pt`. So either:
      * the other bundle really uses it — and then it must also name the OUTER type,
        which IS top-level and IS still checked; or
      * the other bundle merely has its own local `Pt` — and there was never a
        dependency at all.
    Either way the dependency is still caught, and the false positive is gone.

    ⛔ MEASURED, not assumed (2026-08-09): this rule removed 15 findings and kept all
    16 real ones. Every one of the 15 was a generic nested name — `Pt`, `Sub`, `Kind`,
    `Group`, `Result`, `Trade`, `Summary`, `TpStop`, `Config`, `RiskSnapshot`,
    `OpenSnapshot`, `AlertChannelConfig` — and in every case where the reference was
    genuine, the outer type was ALREADY in the real list on its own.

    Why it mattered: 15 of 31 findings were noise, and `bundle-deps` had been red on
    every push since the 2026-08-07 full-suite release. A gate that cries wolf on half
    its output is a gate people stop reading — and this one was being ignored while it
    was also reporting 16 true cross-bundle breaks.
    """
    body = strip_strings(strip_comments(src))
    out = set()
    for m in DEF_RE.finditer(body):
        head = body[:m.start()]
        # `namespace X { }` is a brace that is not a nesting TYPE, so discount one
        # level per namespace opened before this point.
        depth = head.count('{') - head.count('}') - len(re.findall(r'\bnamespace\b', head))
        if depth <= 0:
            out.add(m.group(1))
    return out


def defined_types(files, skip_nt_sdk=False) -> set:
    out = set()
    for f in files:
        if skip_nt_sdk and is_nt_sdk(f):
            continue
        out.update(top_level_types(f.read_text(encoding='utf-8', errors='replace')))
    return out


def referenced_idents(files) -> set:
    out = set()
    for f in files:
        out.update(IDENT_RE.findall(strip_comments(f.read_text(encoding='utf-8', errors='replace'))))
    return out


def cs_files(root: Path):
    return [p for p in root.rglob('*.cs') if '.git' not in p.parts]


class ManifestError(Exception):
    """The bundle manifest cannot be trusted to describe an install."""


def load_manifest(path: Path) -> dict:
    """`bundle = dep, dep` lines -> {bundle: [deps]}. Missing file -> {} (no edges)."""
    if not path.is_file():
        return {}
    out = {}
    for n, raw in enumerate(path.read_text(encoding='utf-8').splitlines(), 1):
        line = raw.split('#', 1)[0].strip()
        if not line:
            continue
        if '=' not in line:
            raise ManifestError('%s:%d: expected `bundle = dep, dep`, got %r' % (path.name, n, raw.strip()))
        k, v = line.split('=', 1)
        k = k.strip()
        if k in out:
            raise ManifestError('%s:%d: bundle %r is declared twice; one line per bundle, '
                                'or the second silently wins' % (path.name, n, k))
        out[k] = [d.strip() for d in v.split(',') if d.strip()]
    return out


def closure(bundle: str, manifest: dict) -> list:
    """`bundle` plus its declared requirements, transitively. Raises on a cycle.

    ⛔ A CYCLE IS FATAL, not merely odd: if deck requires prop-kit and prop-kit requires
    deck, then "install deck" and "install prop-kit" are the same instruction, and the
    manifest has stopped describing anything a reader can act on.
    """
    seen, order, stack = set(), [], []

    def walk(b):
        if b in stack:
            raise ManifestError('dependency CYCLE: %s' % ' -> '.join(stack[stack.index(b):] + [b]))
        if b in seen:
            return
        stack.append(b)
        for d in manifest.get(b, []):
            walk(d)
        stack.pop()
        seen.add(b)
        order.append(b)

    walk(bundle)
    return order


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--repo', default=str(Path(__file__).resolve().parent.parent),
                    help='repo root (default: parent of tools/)')
    ap.add_argument('--manifest', default=str(Path(__file__).resolve().parent / 'bundles.conf'),
                    help='bundle dependency manifest (default: tools/bundles.conf)')
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
    names = {b.name for b in bundles}

    try:
        manifest = load_manifest(Path(args.manifest))
    except ManifestError as exc:
        print(f'ERROR: {exc}', file=sys.stderr)
        return 2
    # A declared bundle that does not exist is a manifest that has rotted away from the
    # tree, and it would silently widen a closure to nothing. Catch it before the scan.
    for b, deps in sorted(manifest.items()):
        for bad in [x for x in [b] + deps if x not in names]:
            print(f'ERROR: bundles.conf names "{bad}", which is not a bundle under src/',
                  file=sys.stderr)
            return 2

    problems = 0
    unused = []
    for b in bundles:
        bfiles = cs_files(b)
        if not bfiles:
            continue
        try:
            need = closure(b.name, manifest)
        except ManifestError as exc:
            print(f'ERROR: {exc}', file=sys.stderr)
            return 2
        closure_files = [f for n in need for f in cs_files(src / n)] + runtime_files
        shipped = defined_types(closure_files)
        refs = referenced_idents(bfiles)
        # a custom type this bundle uses that its documented install does not provide
        missing = sorted((refs & universe) - shipped)
        deps = [d for d in need if d != b.name]
        via = (' + ' + ', '.join(sorted(deps))) if deps else ''

        if missing:
            problems += len(missing)
            print(f'\n[MISSING DEP] bundle "{b.name}"{via} still does not provide:')
            for t in missing:
                users = [str(f.relative_to(repo)) for f in bfiles
                         if re.search(rf'\b{re.escape(t)}\b', strip_comments(f.read_text(encoding="utf-8", errors="replace")))]
                owner = next((n for n in sorted(names)
                              if t in defined_types(cs_files(src / n))), '(nowhere in src/)')
                print(f'    - {t}   defined in bundle "{owner}"')
                print(f'        used in: {", ".join(users)}')
        else:
            print(f'[ok] {b.name}: {len(bfiles)} files, closure complete{via}')

        # ⛔ REDUNDANT IS NOT UNNEEDED, and conflating them causes the wrong edit.
        # `deck -> copier` is reachable transitively via `deck -> prop-kit -> copier`, but
        # deck DIRECTLY uses CopierConfig; deleting the edge would silently make deck's
        # install depend on a choice prop-kit is free to change. So an edge is judged by
        # whether this bundle references any type the dependency OWNS — not by whether
        # some other path happens to supply it.
        for d in manifest.get(b.name, []):
            owned = defined_types(cs_files(src / d))
            if not (refs & owned):
                unused.append((b.name, d))

    if unused:
        print('\n[NOTE] declared requirements this bundle references NO type from:')
        for b, d in unused:
            print(f'    {b} -> {d}')
        print('    Not a failure. This checker matches TYPES, so a partial-class METHOD\n'
              '    dependency (e.g. SentinelCore.GateEntry) is invisible to it and the edge\n'
              '    may still be real. Read it; do not reflex-delete.\n'
              '    (An edge that IS used but also reachable transitively is not listed here —\n'
              '     redundant is not unneeded, and dropping it would make this bundle\'s\n'
              '     install depend on a choice another bundle is free to change.)')

    if problems:
        print(f'\nFAIL: {problems} reference(s) no documented install provides.\n'
              f'      Either declare the owning bundle in tools/bundles.conf, or move the\n'
              f'      defining file into a bundle this one already requires.\n'
              f'      ⛔ Do NOT duplicate the file into both bundles: NinjaTrader compiles\n'
              f'         bin\\Custom as ONE assembly, so a user installing both then gets\n'
              f'         CS0101 duplicate-class and their whole tree stops compiling.',
              file=sys.stderr)
        return 1
    print('\nPASS: every bundle\'s documented install is complete.')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
