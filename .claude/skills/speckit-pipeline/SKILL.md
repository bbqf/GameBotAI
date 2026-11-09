---
name: "speckit-pipeline"
description: "Orchestrate the full spec-kit pipeline end to end autonomously: specify -> clarify -> plan -> tasks -> analyze -> 2 loops of (fix -> analyze) -> commit -> compact context -> implement -> commit -> wait for CI -> open PR, without stopping for manual review. Every step runs in the same session and reuses its context."
argument-hint: "Describe the feature to build (forwarded to /speckit-specify)"
compatibility: "Requires spec-kit project structure with .specify/ directory"
metadata:
  author: "local"
user-invocable: true
disable-model-invocation: false
---

## User Input

```text
$ARGUMENTS
```

Treat the input above as the **feature description**. It is required for a fresh
run and is forwarded verbatim to `/speckit-specify`. If it is empty, assume the
spec already exists on the current branch and start from step 2 (clarify).

## Goal

Drive the complete spec-kit flow for one feature by invoking the existing
speckit skills in order. Run **fully autonomously** — the user is not reviewing
anything manually. Do not pause for confirmation, questions, or gates. Only a
hard failure (see Halting rules) stops the run.

## Execution model

- **All steps run in this one session.** Do not start sub-agents. Each step
  reuses the context of the earlier steps: the spec, plan, tasks, and analyze
  findings that you already have in this conversation.
- Run each step by invoking its skill with the Skill tool (or by doing the
  work directly where the step says so). Follow the rules from `CLAUDE.md`:
  PowerShell with absolute paths, no `cd dir;` prefix, commit messages via
  `git commit -F <scratchpad file>` (never a here-string), Edit/Write rather
  than `Get-Content`/`Set-Content` for UTF-8 files.
- Run each skill **fully autonomously** — no questions, no pauses, no
  confirmation gates.
- Keep the report of each step short. Step 8 (compact) removes the design
  context before implementation, so the files on disk (spec, plan, tasks) must
  hold everything that implementation needs.

## Pipeline (run in order)

1. **`/speckit-specify`** — pass `$ARGUMENTS` as the feature description. Skip
   only if the input is empty and a spec already exists for the branch.
2. **`/speckit-clarify`** — this step would normally ask the user targeted
   questions. Since there is no manual review, **do not pause**: answer each
   question yourself by choosing the most reasonable option given the spec and
   codebase, record the chosen answers and a one-line rationale in the spec (as
   clarify would), and continue.
3. **`/speckit-plan`** — generate the design artifacts.
4. **`/speckit-tasks`** — generate `tasks.md`.
5. **`/speckit-analyze`** — cross-artifact consistency check. Analyze is
   read-only: do not edit files in this step. Keep the **complete findings
   table** (ID, category, severity, location, summary, recommendation), or note
   an explicit "no issues".
6. **Fix loops — exactly 2 loops.** Each loop has **only two sub-steps**:
   fix, then analyze. Do **not** run clarify, plan, or tasks again in a loop.
   Per loop:
   1. **Fix** — resolve **every** finding of the latest analyze (all
      severities) by editing the artifacts directly: the spec, `plan.md`,
      `tasks.md`, and related design files. Where a finding is a spec ambiguity
      or gap, choose the most reasonable answer as in step 2 and record it in
      the spec. Keep the artifacts consistent with each other. Record each
      finding ID and how you resolved it.
   2. **`/speckit-analyze`** — run step 5 again. The findings feed the next
      loop's fix sub-step. Any finding left after the analyze of loop 2 is not
      fixed; do not stop for it.

   Skip a loop's fix sub-step only if the latest analyze reported "no issues".
   If that is so, skip the rest of step 6.

   So the full order is: specify, clarify, plan, tasks, analyze; then loop 1
   (fix, analyze); then loop 2 (fix, analyze); then commit, compact, and
   implement. Never run a third loop.
7. **Commit** — run `/speckit-git-commit` to commit the spec, plan, tasks, and
   fixes before any implementation. This creates a clean checkpoint separating
   the design artifacts from the implementation. Keep the commit SHA.
8. **Compact the context — MANDATORY.** Before you start implementation, you
   **MUST** compact the context of this session. Do not skip this step. Do not
   start step 9 before the compact is done. Use the `/compact` command with
   these focus instructions: "Keep: the feature directory path
   (`specs/<NNN-feature>/`), the branch name, the design commit SHA, the clarify
   answers, the analyze findings fixed, and the findings left open. Drop the
   detailed content of the spec, plan, tasks, and analyze reports. They are on
   disk and the next step reads them from there." If you cannot run `/compact`
   yourself, stop and tell the user to run it (this is the only allowed pause
   in the pipeline). After the compact, re-read `spec.md`, `plan.md`, and
   `tasks.md` from disk as step 9 needs them.
9. **`/speckit-implement`** — execute the tasks. Keep: tasks completed /
   skipped, test and build results, anything left undone.
10. **Commit the implementation** — run `/speckit-git-commit` again to commit
    all changes produced by `implement`. **Before this commit, bump the
    version** (constitution, "Version bump"): compare
    `installer/versioning/version.override.json` with `origin/master`
    (`git fetch origin master` first). If `minor` or `major` changed, set
    `patch` to `"0"`. Otherwise set `patch` to the `patch` on `origin/master`
    plus 1. Do nothing if the file already has that value. Set `updatedBy` and
    `updatedAtUtc`, and add a line about the version to the `CHANGELOG.md`
    entry. Keep the commit SHA and the new version.
11. **Push and wait for CI** — push the branch to `origin`, then wait for the
    GitHub Actions CI runs to finish. **This can take several minutes; keep
    waiting and do not give up early** (a single tool call times out after 10
    minutes, so poll or re-watch rather than assume failure). Concretely:
    - Push: `git push -u origin HEAD`
    - Watch the run for the current commit to completion, e.g.
      `gh run watch <run-id> --exit-status` for each run of
      `gh run list --branch <branch> --commit <sha> --json databaseId,workflowName`
      (`--exit-status` makes it return non-zero if CI fails). If no run has
      registered yet, poll `gh run list` until one appears, then watch it.
      Multiple workflows may trigger (see `.github/workflows/`); wait for
      **all** of them.
    - Keep: each workflow's name, run id, and conclusion; for a failure, the
      failing job and a short diagnosis from `gh run view <id> --log-failed`.
12. **Create the PR — only if CI passed** — if every CI run succeeded, open a
    pull request into `master` with `gh pr create --base master` (title from
    the commits; body with a short summary and the spec directory, passed via
    `--body-file`). Keep the PR URL.

## Halting rules

Stop **only** if a step throws a hard error or its required inputs are missing
(e.g. no spec/plan on the branch, a script exits non-zero, git refuses the
commit, `git push` is rejected). Report the error and where it stopped. Do
**not** stop for consistency findings, clarify questions, or anything that
would normally need human judgment — resolve those autonomously and keep going.
The only other allowed pause is step 8, if you cannot run `/compact` yourself.

**CI failure is a hard stop for the PR step only:** if any CI run fails, do
**not** open the PR. Report the failing workflow and the diagnosis. You may
attempt one autonomous fix: fix the cause from the diagnosis, commit, re-push,
and wait for CI again. If CI still fails, stop and report without creating the
PR.

## Between steps

After each step, print a one-line status:

```
[pipeline] <step> - done (or: skipped / auto-resolved / FAILED: <reason>)
```

## On completion

Print a final summary: which steps ran, which were skipped, the clarify
answers you chose, the analyze findings you fixed (and how many analyze runs
happened, plus any findings left after loop 2), the two commit SHAs (design +
implementation), the CI outcome (which workflows ran and their conclusions),
the PR URL (or the reason no PR was opened), and the branch / spec directory
the work landed in.
