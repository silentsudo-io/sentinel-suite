---
name: contribute
description: >-
  Turn a local Sentinel Suite change into a reviewable pull request. Use whenever the user says
  "contribute this back", "send this to the maintainer", "open a PR", "how do I submit this fix",
  "share my change", or has edited a Sentinel file in their NinjaTrader bin\Custom tree and wants it
  upstreamed. Bridges the gap CONTRIBUTING.md does not cover: the user's working copy is NinjaTrader's
  bin\Custom folder, which is NOT a git clone, so there is no obvious path from "it works in NT" to
  "it is a branch". Handles fork + clone setup, maps bin\Custom paths onto the repo's src/ bundle
  layout, STRIPS NinjaTrader's auto-generated region (which must never be committed), runs the
  verification the maintainer will run, adds the DCO sign-off and AUTHORS credit, and opens the PR.
  Also triages: a fix to Sentinel's own files becomes a PR; a personal fork or a brand-new tool does not.
---

# Contribute a change back to the Sentinel Suite

You are helping someone send a change upstream. They are probably **not a git user**, and their
change probably lives in **`Documents\NinjaTrader 8\bin\Custom\`** — NinjaTrader's compile tree,
which is not a repository. That gap is the whole reason this skill exists. Do not assume a clone,
a fork, or a remote; find out, then set up whatever is missing.

Be encouraging. Someone reaching this point has already done the hard part.

---

## Step 0 — TRIAGE: is this actually a pull request?

Ask what changed and look at it before touching git. Three outcomes, and only one is a PR:

| what they have | where it goes |
|---|---|
| A **fix or improvement to a file the suite already ships** (something under `src/` upstream) | ✅ **PR** — continue with this skill |
| A **brand-new indicator/sensor** they want in the suite | Run the **`port-sentinel-indicator`** skill FIRST to make it compliant, then come back here |
| Their **own renamed fork** of the suite, or a personal tool built *on top of* Sentinel | ❌ **Not a PR.** See *Forks and your own tools* at the bottom — this is a good thing, it just isn't this. |

⚠ **Never open a PR that renames Sentinel's types to something else**, and never one that adds a
file whose class name or namespace could collide with the suite's own. NinjaTrader compiles **all of
`bin\Custom` into ONE assembly**, so a duplicate class name is a `CS0101` that stops the user's
*entire* NinjaScript tree from compiling — every indicator and strategy they own, not just Sentinel.

🔴 **Check every new type name against [`RESERVED_NAMES.md`](../../../RESERVED_NAMES.md) before going
further** — it lists names claimed for tools that **have not shipped yet**, which is exactly the class of
collision nobody can guess at. Grep it:

```bash
grep -i "<TheNameYouWantToUse>" RESERVED_NAMES.md
```

A hit means pick another name. If the contributor is in bucket 3 (their own tool), this is the most
valuable minute of the whole process: renaming now is a find/replace, renaming after other people have
installed it means asking every one of them to delete a file in the right order.

If they are unsure which bucket they are in, ask: *"is this making Sentinel's own file better, or is it
your own thing that uses Sentinel?"*

---

## Step 1 — Find the two trees

There are always two, and confusing them is the most common failure:

- **The NT tree** — `Documents\NinjaTrader 8\bin\Custom\` — where they edited and where NinjaTrader
  compiles. **Not** a git repo.
- **The repo clone** — a clone of their **fork** of `github.com/silentsudo-io/sentinel-suite`. This
  is what a PR comes from.

```bash
gh auth status                      # authenticated? if not: gh auth login
gh repo list --limit 100 | grep -i sentinel-suite    # do they already have a fork?
```

If there is no clone, make one (this forks and clones in a single step):

```bash
gh repo fork silentsudo-io/sentinel-suite --clone --remote
```

Clone it **outside** `bin\Custom`. ⛔ **Never clone into `bin\Custom`** — NinjaTrader would try to
compile every `.cs` in the clone alongside the ones already installed, producing dozens of duplicate-class
errors and breaking their platform. (This has actually happened; it cost a full session to unpick.)

If `gh` is missing: `winget install GitHub.cli`, then `gh auth login`.

---

## Step 2 — Map the changed file onto the repo layout

`bin\Custom` is flat by NinjaScript type. The repo groups by **bundle**, then type. Same filename,
different parent:

```
bin\Custom\Indicators\SentinelTrend_v1_0_0.cs   →  src/sensors/Indicators/SentinelTrend_v1_0_0.cs
bin\Custom\AddOns\SentinelSkin.cs               →  src/runtime/AddOns/SentinelSkin.cs
bin\Custom\BarsTypes\SentinelTBars_v1_0_0.cs    →  src/sensors/BarsTypes/SentinelTBars_v1_0_0.cs
```

Find the real destination rather than guessing the bundle:

```bash
cd <repo-clone>
find src -name "<TheFile>.cs"
```

**If that finds nothing, stop and re-triage.** A file that is not in `src/` is not something the suite
ships, so a change to it cannot be a PR against this repo — it is a new tool (step 0, row 2) or part of
a fork (row 3).

⚠ **Diff before you copy.** The repo is a *release snapshot* and their `bin\Custom` copy may be from a
different version, or carry unrelated local experiments:

```bash
diff "<repo>/src/<bundle>/<Folder>/<File>.cs" "<nt-tree>/<Folder>/<File>.cs"
```

Copy the **intended change only**. If the diff is large, apply their fix by hand to the repo copy
instead of overwriting the file. A PR that also reverts the maintainer's newer work will be rejected,
and it is tedious to untangle.

---

## Step 3 — 🔴 STRIP THE GENERATED REGION (never skip this)

NinjaTrader **appends an auto-generated region** to the bottom of every file it compiles, holding
wrapper properties and a cache array. It is machine-written, per-installation, and **must never be
committed** — NT regenerates it on the next compile, so a committed copy produces `CS0111` /
`CS0102` duplicate-member errors for whoever imports the file next.

**Only files under `Indicators/` ever have one.** AddOns, BarsTypes, and Strategies do not.

Check what you are about to commit:

```bash
grep -c "region NinjaScript generated code" "src/<bundle>/Indicators/<File>.cs"
```

Anything **above 0** must be removed. The region starts at a line matching
`^[ \t]*#region NinjaScript generated code` and runs to the end of the file, so delete from the
**first anchored match** to EOF, then re-close the class and namespace braces.

⚠ **Match at the start of a line.** A plain substring search also matches a *comment mentioning* the
region, and cutting from there deletes real code. That exact bug has bitten this project twice — once
mass-stripping ~700 files, once in a tool that silently deleted 21% of a healthy file. Back the file up
before editing, and read the tail afterwards to confirm the class still closes properly.

---

## Step 4 — Verify what the maintainer will verify

There is no conventional CI. Run these; paste the output into the PR.

```bash
python tools/check_bundle_deps.py
python tools/make_ninjascript_archive.py <bundle> runtime -o /tmp/verify.zip
```

Both must pass. The first proves the bundle still compiles standalone; the second proves the change
still packages into an importable NinjaScript archive.

🔴 **Then check your change against the PUBLISHED runtime, not your own.** Contributors run a newer
`SentinelCore` than the release ships. A member that exists locally may not exist in the published
`src/runtime/`:

```bash
grep -rn "SentinelCore\.<TheMemberYouUsed>" src/runtime/AddOns/
```

No match means the published Core does **not** have it, and your change is a `CS0117` for every user
even though it compiles perfectly for you. Use only what `src/runtime/` actually contains — for
example, prefer a bare `catch { }` in published code if `SentinelCore.Swallow` is absent there.
*(This is a real near-miss from the first contributed fix.)*

**Finally, prove it in NinjaTrader** — F5 in the NinjaScript Editor is the only authoritative compile.
Headless builds both false-pass and false-fail on this tree. Paste the result.

If the change is a sensor, tick every box in **`SENSOR_COMPLIANCE_CHECKLIST.md`**.

---

## Step 5 — Branch, sign off, commit

```bash
git checkout -b fix/<short-slug>          # or feat/<short-slug>
git add <the specific files>              # never `git add -A` from a tree you did not audit
git commit -s -m "fix: <what changed, in the imperative>"
```

**`-s` is required.** It appends a `Signed-off-by:` line — the
[Developer Certificate of Origin](https://developercertificate.org/): your statement that you wrote
this, or have the right to submit it, and that you are releasing it under the project's **MPL-2.0**.
Without it the maintainer cannot legally merge, no matter how good the patch is.

⚠ **If any of the change came from another indicator, a forum post, or a paid product, say so now** —
in the commit body and the PR. A port is a derivative work. Unattributed code cannot be accepted and
is far more painful to unpick after merge than before. "I adapted this from X" is always the right
answer; the maintainer will clean-room it if the licence requires.

Write the commit body to explain **why**, not what — the diff already shows what. State the symptom
you saw, the cause you found, and how you know it is fixed.

---

## Step 6 — Credit yourself

Add a line to **`AUTHORS`** under *Contributors*:

```
- <handle> — <what you contributed, one sentence>. Released for open-source use
  under this project with permission (YYYY-MM-DD).
```

Also add a short note in the header comment or changelog of **the file you changed**. The project
credits contributors *in the source file they worked on*, so each grant stays attached to the exact
code it covers. Do not be shy here — this is the record.

---

## Step 7 — Open the PR

```bash
git push -u origin <branch>
gh pr create --repo silentsudo-io/sentinel-suite --fill
```

Then edit the body to include:

1. **The symptom** — what a user sees when it is broken. Concrete beats abstract: *"cards collapse to
   a bare 22px chip on a chart with many sub-panels"* is worth more than *"layout issue"*.
2. **The cause** — what you found, and how.
3. **Verification** — the tool output from step 4, plus the F5 result.
4. **The release statement** — that you release the work for open-source use under MPL-2.0.
5. **Anything you are unsure about.** Flagging a doubt is not weakness; a PR that says *"I could not
   test the reconnect path"* is more useful than one that quietly implies everything was tested.

Keep it to **one fix or one tool per PR**. Two unrelated changes in one branch means neither can be
merged until both are agreed.

---

## Forks and your own tools

Building your own thing on top of Sentinel is **explicitly welcome** — the whole point of publishing
the seams is that other people consume them. It just isn't a pull request. If that is what they have:

- **Keep it under your own namespace and class prefix.** Not `Sentinel*`, not
  `NinjaTrader.NinjaScript.Indicators.Sentinel*`. Pick your own and use it everywhere — file name,
  class name, display `Name`, namespace, and your config folder under `Documents\NinjaTrader 8\`.
  This is not territorial: it is the only thing standing between a user who installs both and a
  `CS0101` that kills their entire platform.
- **Consume the published `…State` seams** rather than reimplementing sensors. That is what they are for.
- **Ship it as your own repo**, under whatever licence you like (MPL-2.0 if you want to match).
  Open an issue here and it can be linked from the docs — a second implementation is good for
  everyone, and you keep authorship of your own work.
- **Do not extend `SentinelCore` with a `partial class` of your own.** It compiles, because the type is
  `public static partial` — but it means a user is running a core that is partly the project's and
  partly yours, under the project's type name, with nothing to tell them apart. If you need something
  Core does not expose, **open an issue asking for the seam.** That is a better outcome for you too:
  a member the maintainer adds will not vanish on the next update.

---

## Definition of done

- [ ] Triaged: this is a change to a file the suite **ships** (not a fork, not an unported new tool)
- [ ] Repo clone exists **outside** `bin\Custom`, from the contributor's own fork
- [ ] Change applied to the correct `src/<bundle>/<Folder>/` path, with **no unrelated drift**
- [ ] **Zero** generated regions in every committed `.cs` (`grep -c` returns 0)
- [ ] `check_bundle_deps.py` passes · archive builds · **F5 clean in NinjaTrader**, output pasted
- [ ] Every `SentinelCore` member used **exists in `src/runtime/`**, not just locally
- [ ] Commit is **`-s` signed off**; any borrowed code is disclosed
- [ ] `AUTHORS` updated **and** the changed file's header credits the contributor
- [ ] PR states symptom · cause · verification · open-source release · known gaps
