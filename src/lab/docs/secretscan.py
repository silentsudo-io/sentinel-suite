#!/usr/bin/env python3
r"""Sentinel PUBLISH GATE — refuse to ship the operator's network.

WHAT THIS IS FOR. The suite is being released in full. The code is clean -- measured
2026-08-07, the 92 C# files carry ZERO domains, ZERO IPs and ZERO credentials, and the only
matches are a machine nickname in changelog prose. The exposure is concentrated in
infrastructure docs and Lab\infra\, which describe a private rack. This gate exists so that
stays true as the tree grows, and so a publish that would break it FAILS LOUDLY instead of
succeeding quietly.

⚠ THIS IS THE SECOND LOCK, NOT THE FIRST. Publishing happens exactly one way: a human copies
a file into the public repo's `src/`. That allowlist is the control. A denylist scanner can
only ever catch patterns someone thought to write down, so it must never be the thing standing
between a secret and the internet -- it is the backstop that catches the case where the
editorial decision was made wrongly.

SEVERITIES
    BLOCK   a live domain / address / credential / fleet hostname. In a PUBLIC-zone file this
            is a hard failure and exit 1.
    REVIEW  something that is usually fine and occasionally is not (a machine nickname, an
            absolute user path, an email). Reported, never fatal -- a gate that cries wolf
            gets switched off, and this project has written that lesson down four times.

    python secretscan.py                          # scan the default release set
    python secretscan.py --gate <dir>             # gate one tree; exit 1 on any PUBLIC block
    python secretscan.py --zone PUBLIC --json x   # machine-readable

Zones come from zones.conf so the manifest has ONE home. Spec: SENTINEL_DOCS_HEALTH_SPEC.md.
"""
from __future__ import annotations
import os, re, io, sys, json, fnmatch, argparse, collections
import sys as _sys, os as _os
_LAB_ROOT = _os.path.abspath(_os.path.join(_os.path.dirname(_os.path.abspath(__file__)), ".."))
if _LAB_ROOT not in _sys.path:
    _sys.path.insert(0, _LAB_ROOT)
try:
    from lab_faults import swallow
except Exception:                                  # usable as a standalone pre-commit hook
    def swallow(where, ex):                        # noqa: D103
        pass

HERE   = os.path.dirname(os.path.abspath(__file__))
LAB    = os.path.abspath(os.path.join(HERE, ".."))
SENT   = os.path.abspath(os.path.join(LAB, ".."))
NT8    = os.path.abspath(os.path.join(SENT, ".."))
CUSTOM = os.path.join(NT8, "bin", "Custom")
ZONES  = os.path.join(HERE, "zones.conf")

SKIP_EXT = {".dll", ".pdb", ".exe", ".zip", ".png", ".jpg", ".jpeg", ".gif", ".ico", ".db",
            ".nrd", ".ncd", ".wal", ".shm", ".pyc", ".zst", ".gz", ".tar", ".parquet",
            ".woff", ".woff2", ".ttf", ".map", ".jsonl", ".csv", ".log", ".bak"}
MAX_BYTES = 4_000_000

PRIVATE_CONF = os.path.join(HERE, "private.conf")


def _unbreakable_output() -> None:
    """Make stdout/stderr incapable of raising UnicodeEncodeError before we can state a finding.

    MEASURED 2026-08-12: piped stdout on Windows encodes with cp1252, which has no glyph for
    the FAIL markers this project writes in. So the PASS path printed and the FAIL path died
    with UnicodeEncodeError -- losing the finding while the exit code said only "something".
    A gate that cannot print its refusal reads, to the person on the other end, as noise.
    Full rationale: sentinel-suite tools/_console.py.
    """
    for _s in (sys.stdout, sys.stderr):
        _rc = getattr(_s, "reconfigure", None)
        if _rc is None:
            continue
        try:
            _rc(errors="replace") if _s.isatty() else _rc(encoding="utf-8", errors="replace")
        except (ValueError, OSError):
            pass                                   # reporting matters more than the stream


_unbreakable_output()


def _operator_rules():
    r"""Operator-specific BLOCK patterns, loaded from private.conf (gitignored).

    ⭐ THE GATE CAUGHT ITSELF -- TWICE, THE SECOND TIME BY HAND. The first cut hardcoded the
    operator's domain and fleet-name patterns into the table below, so publishing the scanner
    would have published the exact strings it exists to protect; its own PUBLIC-zone check
    flagged that on the first real run. The REWRITE then explained the incident by quoting the
    domain in an ESCAPED form, which slipped past the very pattern it was describing (the
    regex wants a literal dot; the prose had a backslash) and shipped. Caught by a plain grep
    that disagreed with a passing gate. ⇒ Do not name the value even while explaining it, and
    when a hand-check disagrees with the gate, the gate is the thing that is wrong. A
    denylist that names the secret IS the secret. The values now live in a private file and
    this tool ships without them; a missing file just means these checks are skipped, while
    the structural rules (RFC1918, CGNAT, keys, JWTs) always run because they need no local
    knowledge.
    """
    pats = []
    try:
        for raw in io.open(PRIVATE_CONF, encoding="utf-8"):
            s = raw.strip()
            if s and not s.startswith("#"):
                pats.append(s)
    except OSError as _swex:
        swallow("docs.secretscan.private_conf", _swex)
    if not pats:
        # ⛔ SAY SO, LOUDLY. Without this file the gate stops checking for the operator's
        # domain and host names and still prints "GATE: PASS — no PUBLIC-zone secrets".
        # Measured: a clean-looking pass with zero blocks and no indication anything was
        # skipped. That is the exact shape this project keeps finding — a check that is
        # verified and still lies. A weakened gate must never be indistinguishable from a
        # satisfied one, so it announces itself on stderr AND in the report body.
        global _NO_OPERATOR_RULES
        _NO_OPERATOR_RULES = True
        sys.stderr.write(
            "\nsecretscan: ⛔ NO OPERATOR PATTERNS — %s is missing.\n"
            "            Domain and host-name checks are OFF. The structural rules (RFC1918,\n"
            "            CGNAT, private keys, JWTs, password assignments) still run, but a\n"
            "            PASS from this run does NOT mean what it usually means.\n"
            "            Restore it from private.conf.example.\n\n" % PRIVATE_CONF)
        return []
    return [("operator-private", re.compile("(?:%s)" % "|".join(pats), re.I), "BLOCK")]


_NO_OPERATOR_RULES = False


# (label, regex, severity). Ordered most- to least-specific; first match per line wins.
RULES = _operator_rules() + [
    ("private-key",     re.compile(r"-----BEGIN (?:RSA |OPENSSH |EC |DSA |PGP )?PRIVATE KEY-----"), "BLOCK"),
    ("aws-key",         re.compile(r"\bAKIA[0-9A-Z]{16}\b"), "BLOCK"),
    ("bearer-jwt",      re.compile(r"\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\."), "BLOCK"),
    ("tailscale-authkey", re.compile(r"\btskey-[A-Za-z0-9-]{10,}"), "BLOCK"),
    ("password-assign", re.compile(r"(?i)\b(?:password|passwd|pwd|secret|api[_-]?key|token|credential)\s*[:=]\s*[\"'][^\"'\s]{6,}[\"']"), "BLOCK"),
    # Tailscale CGNAT range 100.64.0.0/10 -- deliberately not the whole of 100.x
    ("tailscale-ip",    re.compile(r"\b100\.(?:6[4-9]|[7-9]\d|1[01]\d|12[0-7])\.\d{1,3}\.\d{1,3}\b"), "BLOCK"),
    ("rfc1918-ip",      re.compile(r"\b(?:10\.\d{1,3}\.\d{1,3}\.\d{1,3}|192\.168\.\d{1,3}\.\d{1,3}|172\.(?:1[6-9]|2\d|3[01])\.\d{1,3}\.\d{1,3})\b"), "BLOCK"),
    ("secrets-path",    re.compile(r"\.secrets\b|_root\.txt|cloudflare_token|nas_snmp|sentry_admin|pg_sentinel|dsm_acme|unifi_api|tailscale_authkey"), "BLOCK"),
    ("prop-account",    re.compile(r"\b(?:APEX|TOPSTEP|TPT|MFFU|LEELOO|EARN2TRADE)[-_ ]?\d{4,}\b", re.I), "BLOCK"),
    # ⛔ NO HOSTNAMES IN THIS TABLE. This slot used to name three of the operator's machines
    # directly -- the THIRD time the same mistake was made in this one file, after the domain
    # and the fleet-name pattern, and each time it shipped the value into a public repo.
    # Operator-specific names go in private.conf (detect) and scrub.conf (rewrite). If you are
    # about to type a machine name here, that is the signal you want one of those two files.
    ("user-path",       re.compile(r"C:\\+Users\\+(?!<|USER|you\b)[A-Za-z0-9_.-]+\\+"), "REVIEW"),
    ("email",           re.compile(r"\b[A-Za-z0-9._%+-]+@(?!example\.|sentinel\.|user\.|[A-Z0-9]{4,}\b)[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b"), "REVIEW"),
    ("mac-addr",        re.compile(r"\b(?:[0-9a-f]{2}:){5}[0-9a-f]{2}\b", re.I), "REVIEW"),
]

# A line that TEACHES a pattern rather than containing a live value. Kept narrow on purpose:
# a broad benign filter is how a real secret gets waved through.
BENIGN = re.compile(r"(?i)\bexample\b|\bplaceholder\b|redact|<your|your-|\bdummy\b|\bsample\b|"
                    r"\bfake\b|xxxx|gitignor|never commit|do not commit|\bregex\b|re\.compile|"
                    # 100.64.0.0/10 is the PUBLISHED CGNAT allocation (RFC 6598) that Tailscale
                    # draws from -- a documented constant, not anyone's address. It appears in
                    # firewall rules and reads as a tailnet leak to a naive matcher.
                    r"100\.64\.0\.0/10|"
                    # Any bare network address ending .0.0/N is a range, not a host.
                    r"\d+\.\d+\.0\.0/\d+")


def load_zones(path=ZONES):
    """[(zone, glob, why)] in file order. First match wins, so specific lines go first."""
    out = []
    try:
        for raw in io.open(path, encoding="utf-8"):
            line = raw.strip()
            if not line or line.startswith("#"):
                continue
            body, _, why = line.partition("#")
            parts = body.split(None, 1)
            if len(parts) == 2:
                out.append((parts[0].upper(), parts[1].strip().replace("\\", "/").lower(),
                            why.strip()))
    except OSError as _swex:
        swallow("docs.secretscan.load_zones", _swex)
    return out


def zone_of(rel, zones):
    r = rel.replace("\\", "/").lower()
    for zone, pat, why in zones:
        # fnmatch has no ** semantics; '**/' must also match zero directories.
        if fnmatch.fnmatch(r, pat) or (pat.startswith("**/") and fnmatch.fnmatch(r, pat[3:])):
            return zone, why
    return "PUBLIC", ""


# Files whose JOB is to name secret paths so they are excluded. An ignore-file line naming
# the credential directory IS the protection, not the leak, and flagging it teaches the
# reader that the gate does not understand what it is looking at. Only the PATH-NAMING rules
# are exempted -- a real credential pasted into one of these is still caught by every other
# rule (verified: `password = "..."` in a .gitignore still fails the gate).
#
# ⚠ NOTE THE ABSENCE OF THIS FILE FROM THE LIST. The scanner's own comments must not name
# the patterns either; when they did, it flagged itself, and the fix was to reword rather
# than to add itself here. A gate that grants itself an exemption is one nobody can audit.
PATH_DECLARING = {".gitignore", ".gitattributes", ".dockerignore", "zones.conf",
                  "scrub.conf.example", "private.conf.example"}
PATH_RULES = {"secrets-path"}


def scan_file_text(text, rel):
    """Scan CONTENT rather than a path — for gating a transform's OUTPUT before it is written.

    Verifying the input and shipping the output is how a check passes while the artifact
    lies. publish.py builds the scrubbed text in memory, so the gate must be able to read
    that, not the file it came from.
    """
    hits = []
    declaring = os.path.basename(rel).lower() in PATH_DECLARING
    if "\x00" in text[:2048]:
        return hits
    for i, line in enumerate(text.splitlines(), 1):
        probe = line[:4000]
        for label, rx, sev in RULES:
            m = rx.search(probe)
            if not m:
                continue
            if declaring and label in PATH_RULES:
                continue
            hits.append(dict(file=rel, line=i, rule=label, sev=sev,
                             benign=bool(BENIGN.search(probe)),
                             match=m.group(0)[:60], text=probe.strip()[:160]))
            break
    return hits


def scan_file(path, rel):
    hits = []
    declaring = os.path.basename(rel).lower() in PATH_DECLARING
    try:
        text = io.open(path, encoding="utf-8", errors="replace").read()
    except OSError as _swex:
        swallow("docs.secretscan.read", _swex)
        return hits
    if "\x00" in text[:2048]:
        return hits
    for i, line in enumerate(text.splitlines(), 1):
        probe = line[:4000]
        for label, rx, sev in RULES:
            m = rx.search(probe)
            if not m:
                continue
            if declaring and label in PATH_RULES:
                continue
            hits.append(dict(file=rel, line=i, rule=label, sev=sev,
                             benign=bool(BENIGN.search(probe)),
                             match=m.group(0)[:60], text=probe.strip()[:160]))
            break                                  # one finding per line: the most specific
    return hits


def walk(root, zones):
    for dp, dirs, names in os.walk(root):
        dirs[:] = [d for d in dirs
                   if zone_of(os.path.relpath(os.path.join(dp, d), root), zones)[0] != "SKIP"]
        for n in names:
            if os.path.splitext(n)[1].lower() in SKIP_EXT:
                continue
            p = os.path.join(dp, n)
            rel = os.path.relpath(p, root)
            if zone_of(rel, zones)[0] == "SKIP":
                continue
            try:
                if os.path.getsize(p) > MAX_BYTES:
                    continue
            except OSError:
                continue
            yield p, rel


def scan(roots, zones):
    out = []
    for root in roots:
        base = os.path.abspath(root)
        if not os.path.isdir(base):
            continue
        # Zone globs are written relative to the NT8 tree; when gating some other directory
        # (the public repo, say) match on the path we actually have.
        prefix = os.path.relpath(base, NT8).replace("\\", "/") if base.startswith(NT8) else ""
        for p, rel in walk(base, zones):
            key = (prefix + "/" + rel.replace("\\", "/")).lstrip("/") if prefix and prefix != "." else rel
            z, why = zone_of(key, zones)
            for h in scan_file(p, key):
                h["zone"], h["why"] = z, why
                out.append(h)
    return out


def report(hits, gate_mode=False):
    live = [h for h in hits if not h["benign"]]
    fatal = [h for h in live if h["sev"] == "BLOCK" and h["zone"] == "PUBLIC"]
    parked = [h for h in live if h["sev"] == "BLOCK" and h["zone"] == "PRIVATE"]
    param = [h for h in live if h["sev"] == "BLOCK" and h["zone"] == "PARAM"]
    review = [h for h in live if h["sev"] == "REVIEW"]

    print("=" * 96)
    print("SENTINEL PUBLISH GATE — %d findings (%d after benign filter)" % (len(hits), len(live)))
    print("=" * 96)
    if _NO_OPERATOR_RULES:
        print("  ⛔ DEGRADED — no operator patterns loaded (private.conf missing).")
        print("     Domain and host-name checks are OFF; a PASS below is NOT a full pass.")
    print("  PUBLIC-zone BLOCK   %5d   %s" % (len(fatal), "⛔ WOULD SHIP A SECRET" if fatal else "✅ none"))
    print("  PARAM-zone  BLOCK   %5d   parameterise before publishing (fleet.conf)" % len(param))
    print("  PRIVATE-zone BLOCK  %5d   expected — these files are never published" % len(parked))
    print("  REVIEW (all zones)  %5d   judgement, never fatal" % len(review))

    if fatal:
        print("\n⛔ THESE WOULD SHIP. Fix or move to a PRIVATE/PARAM zone in zones.conf:")
        for f, n in collections.Counter(h["file"] for h in fatal).most_common(40):
            rules = sorted({h["rule"] for h in fatal if h["file"] == f})
            print("   %4d  %-64s %s" % (n, f[:64], ",".join(rules)))
        for h in fatal[:15]:
            print("      %s:%d [%s] %s" % (h["file"][:52], h["line"], h["rule"], h["text"][:70]))

    if param and not gate_mode:
        print("\n⚠ PARAM — publishable once the host list moves to config:")
        for f, n in collections.Counter(h["file"] for h in param).most_common():
            print("   %4d  %s" % (n, f))

    if review and not gate_mode:
        by = collections.Counter(h["rule"] for h in review)
        print("\n· REVIEW: " + " · ".join("%s×%d" % (r, n) for r, n in by.most_common()))
    return 1 if fatal else 0


def main():
    ap = argparse.ArgumentParser(description="Sentinel publish gate — secret/PII scan")
    ap.add_argument("roots", nargs="*", help="trees to scan (default: the release set)")
    ap.add_argument("--gate", metavar="DIR", help="gate one tree; exit 1 on any PUBLIC-zone BLOCK")
    ap.add_argument("--zone", choices=["PUBLIC", "PRIVATE", "PARAM"], help="report one zone only")
    ap.add_argument("--json", metavar="PATH")
    a = ap.parse_args()

    zones = load_zones()
    roots = [a.gate] if a.gate else (a.roots or [CUSTOM, os.path.join(SENT, "Lab"),
                                                 os.path.join(SENT, "Azimuth")])
    hits = scan(roots, zones)
    if a.zone:
        hits = [h for h in hits if h["zone"] == a.zone]
    rc = report(hits, gate_mode=bool(a.gate))
    if a.json:
        json.dump(hits, io.open(a.json, "w", encoding="utf-8"), indent=1)
        print("\nwrote %s" % a.json)
    if a.gate:
        print("\nGATE: %s" % ("FAIL — publish refused" if rc else "PASS — no PUBLIC-zone secrets"))
    return rc


if __name__ == "__main__":
    try:
        _sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except Exception as _swex:
        swallow("docs.secretscan.stdout", _swex)
    raise SystemExit(main())
