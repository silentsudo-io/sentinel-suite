#!/usr/bin/env python3
r"""Conformance test: every doc transform must protect inline code from its own rewriting.

    python tools/test_doc_transforms.py          # exit 0 = all conform

WHY THIS EXISTS — and why it is a TEST rather than another paragraph of documentation.

On 2026-08-08 the same defect was found three times in one day, in three different tools, each
written by someone who knew the rule:

  * publish_doc.py  substituted {{tokens}} inside backticks, so prose *about* `{{core_version}}`
                    was published as prose about `v1.47.0` — and check_parity then reported
                    permanent drift on a file that had been published correctly.
  * md2atlas.py     ran its * and ** emphasis rules over inline-code contents, so the glob in
                    `Excursions\ticks\*.jsonl` was eaten as an <em> span straddling two <code>
                    spans: a corrupted path plus malformed nesting, live on the public site.
  * check_parity.py had the rule, and its own comment said the rules were lifted from md2atlas
                    "DELIBERATELY: if this normaliser and that renderer disagree, this tool
                    reports drift that does not exist." Two of the three had drifted anyway.

The rule was already written down. It was written down in more than one place. Writing it down a
fourth time is the intervention that had already failed — so this file asserts it instead.

⛔ THE ANTI-VACUOUS ASSERTION IS NOT OPTIONAL. Each transform is also required to substitute a
BARE token / emphasise BARE asterisks. Without that, a tool that does nothing at all passes every
protection check trivially, and a test that cannot distinguish "correct" from "inert" is worse
than no test: it reports safety it never measured.

ADDING A NEW TRANSFORM? Add it to TRANSFORMS below. If it rewrites doc text at all, it belongs
here — that is the whole point. Stash code spans first, transform, restore last.
"""
from __future__ import annotations

import importlib.util
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, ".."))


def load(name: str, relpath: str):
    path = os.path.join(REPO, relpath)
    spec = importlib.util.spec_from_file_location(name, path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


# ── the adversarial fixture ──────────────────────────────────────────────────────────────────
# Every line here is a real construct that a real doc in this repo contains, and each one broke a
# real tool. Keep them literal; a fixture paraphrased into safety tests nothing.
FIXTURE_TOKEN_IN_CODE = "The `{{core_version}}` token names the value; the live one is {{core_version}}."
FIXTURE_GLOB_IN_CODE = r"Reads `Excursions\ticks\*.jsonl` **and** `council\ticks\*.jsonl` on boot."
FIXTURE_BARE_EMPHASIS = "This is *emphasised* prose."

FACTS = {"core_version": "v1.47.0"}

failures: list[str] = []
checks = 0


def check(cond: bool, label: str, detail: str = "") -> None:
    global checks
    checks += 1
    if not cond:
        failures.append(f"{label}{(' — ' + detail) if detail else ''}")


# ── 1. publish_doc.render — token substitution ───────────────────────────────────────────────
pd = load("publish_doc", "tools/publish_doc.py")
out, _ = pd.render(FIXTURE_TOKEN_IN_CODE, FACTS)
check("`{{core_version}}`" in out,
      "publish_doc.render ATE a token inside backticks",
      "prose documenting a token must survive verbatim")
check("the live one is v1.47.0" in out,
      "publish_doc.render did NOT substitute a bare token",
      "anti-vacuous: a transform that does nothing must not pass")

# ── 2. check_parity.normalise_doc — must AGREE with publish_doc ───────────────────────────────
cp = load("check_parity", "tools/check_parity.py")
norm = "\n".join(cp.normalise_doc(FIXTURE_TOKEN_IN_CODE, FACTS))
check("`{{core_version}}`" in norm,
      "check_parity.normalise_doc ATE a token inside backticks")
check("the live one is v1.47.0" in norm,
      "check_parity.normalise_doc did NOT substitute a bare token")
# The two must agree, or check_parity reports drift on correctly-published files. This is the
# exact disagreement that produced a 4-line phantom drift that would not close.
check(("`{{core_version}}`" in out) == ("`{{core_version}}`" in norm),
      "publish_doc and check_parity DISAGREE about code protection",
      "they must be identical or parity reports drift that does not exist")

# ── 3. md2atlas.inline — emphasis must not cross a code boundary ──────────────────────────────
md = load("md2atlas", "src/lab/md2atlas.py")
html = md.inline(FIXTURE_GLOB_IN_CODE)
check("<em>" not in html,
      "md2atlas.inline ate an asterisk inside inline code as emphasis",
      f"got: {html[:120]}")
check(html.count("<code>") == html.count("</code>") == 2,
      "md2atlas.inline produced unbalanced <code> spans",
      f"got: {html[:120]}")
check(not re.search(r"<code>[^<]*<(em|strong)>|<(em|strong)>[^<]*</code>", html),
      "md2atlas.inline emitted emphasis nested across a code boundary")
check("<em>emphasised</em>" in md.inline(FIXTURE_BARE_EMPHASIS),
      "md2atlas.inline did NOT emphasise bare *text*",
      "anti-vacuous: escaping everything must not pass")

# ── 4. audit.py — a documented token is not a dangling token ──────────────────────────────────
au = load("audit", "src/lab/docs/audit.py")
prose = au.CODEBLK.sub("", "A doc may document `{{not_a_real_fact}}` without declaring it.")
check("not_a_real_fact" not in prose,
      "audit.py would report a BACKTICKED token as dangling",
      "code spans must be stripped before the dangling-token scan")
check("no_protection_here" in au.CODEBLK.sub("", "A bare {{no_protection_here}} must still be seen."),
      "audit.py's code-strip also removed a BARE token",
      "anti-vacuous: it must still catch genuinely dangling tokens")

# ── report ────────────────────────────────────────────────────────────────────────────────────
print(f"doc-transform conformance: {checks} checks across 4 transforms")
if failures:
    for f in failures:
        print(f"  ✗ {f}")
    print(f"FAIL — {len(failures)} of {checks}")
    sys.exit(1)
print("PASS — inline code is protected in every transform, and none of them is inert")
sys.exit(0)
