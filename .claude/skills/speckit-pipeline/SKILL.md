---
name: "speckit-pipeline"
description: "Orchestrate the full spec-kit pipeline end to end autonomously: specify -> clarify -> plan -> tasks -> analyze -> 2 loops of (fix -> analyze) -> commit -> context break -> implement -> commit -> wait for CI -> open PR, without stopping for manual review. The run has two phases. Phase 1 (design) ends at the design commit. Phase 2 (implementation) starts in a new, empty context."
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
run and is forwarded verbatim to `/speckit-specify`. If it is empty, the spec
already exists on the current branch. Then choose the phase:

- If `tasks.md` exists in the feature directory, and the design commit exists,
  and no implementation commit exists, start **phase 2** at step 9.
- If not, start **phase 1** at step 2 (clarify).

Phase 1 is steps 1 to 7. Phase 2 is steps 9 to 12. Step 8 is the break between
them. A run does **one phase only**: never run phase 2 in the same context as
phase 1.

## Goal

Drive the complete spec-kit flow for one feature by invoking the existing
speckit skills in order. Run **fully autonomously** — the user is not reviewing
anything manually. Do not pause for confirmation, questions, or gates. Only a
hard failure (see Halting rules) stops the run.

## Execution model

- **The steps of one phase run in this one context.** Do not start sub-agents.
  Each step reuses the context of the earlier steps of the same phase.
- **Phase 2 never reuses the design context.** It starts with an empty context
  and reads the spec, plan, and tasks from disk.
- Run each step by invoking its skill with the Skill tool (or by doing the
  work directly where the step says so). Follow the rules from `CLAUDE.md`:
  PowerShell with absolute paths, no `cd dir;` prefix, commit messages via
  `git commit -F <scratchpad file>` (never a here-string), Edit/Write rather
  than `Get-Content`/`Set-Content` for UTF-8 files.
- Run each skill **fully autonomously** — no questions, no pauses, no
  confirmation gates.
- Keep the report of each step short. Step 8 (context break) removes the design
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
   (fix, analyze); then loop 2 (fix, analyze); then commit, and stop (step 8).
   Phase 2 does the implementation. Never run a third loop.
7. **Commit** — run `/speckit-git-commit` to commit the spec, plan, tasks, and
   fixes before any implementation. This creates a clean checkpoint separating
   the design artifacts from the implementation. Keep the commit SHA.
8. **Context break — MANDATORY. End phase 1 here.** Do **not** run step 9 in
   this context. Do **not** run `/compact` (a sub-agent cannot run it). Stop
   and print this report:

   ```
   [pipeline] PHASE 1 COMPLETE
   branch: <branch>
   feature directory: specs/<NNN-feature>/
   design commit: <sha>
   clarify answers: <list>
   analyze findings fixed: <list>
   findings left open: <list>
   next: run the pipeline again with an empty argument in a NEW, empty context
   ```

   The caller (a user, or `resolve-github-issue`) must start phase 2 in a new
   context. This is the only allowed stop that is not an error.
9. **`/speckit-implement`** — (phase 2 starts here, in an empty context.) First
   read `spec.md`, `plan.md`, and `tasks.md` from disk. Then execute the tasks.
   Keep: tasks completed / skipped, test and build results, anything left
   undone.
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
The only other allowed stop is step 8, the end of phase 1.

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
