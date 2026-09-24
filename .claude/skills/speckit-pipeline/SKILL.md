---
name: "speckit-pipeline"
description: "Orchestrate the full spec-kit pipeline end to end autonomously: specify -> clarify -> plan -> tasks -> analyze -> fix -> commit -> implement -> commit -> wait for CI -> open PR, without stopping for manual review. Every step from plan onward runs in its own sub-agent."
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

- **Steps 1–2 (specify, clarify) run inline** in this conversation.
- **Every step from 3 (plan) onward runs in its own, fresh sub-agent** via the
  Agent tool (`subagent_type: "general-purpose"`, `run_in_background: false` —
  each step depends on the previous one). One sub-agent per step invocation:
  **every re-run of analyze and every fix round gets a new sub-agent**; never
  reuse or `SendMessage` a previous step's agent.
- The orchestrator (you) does no step work itself: it launches the sub-agent,
  reads its report, prints the status line, and decides the next step. The spec
  / plan / tasks files on disk are the hand-off between steps; keep only the
  sub-agents' short reports in this conversation.
- A sub-agent starts cold, so every prompt must be self-contained. Include:
  - the repo root (`C:\src\GameBot`), the current branch, and the feature
    directory (`specs/<NNN-feature>/`);
  - the exact skill to invoke with the Skill tool (e.g. `speckit-plan`) and its
    arguments, plus the instruction to run it **fully autonomously** — no
    questions, no pauses, no confirmation gates;
  - the rules from `CLAUDE.md` that bite in sub-agents: PowerShell with
    absolute paths, no `cd dir;` prefix, commit messages via `git commit -F
    <scratchpad file>` (never a here-string), Edit/Write rather than
    `Get-Content`/`Set-Content` for UTF-8 files;
  - any inputs from earlier steps it needs (e.g. the analyze findings for a fix
    round);
  - what to return: a concise final report — outcome (done / FAILED + reason),
    files changed, and the step-specific items listed below. The report is the
    only thing you see, so it must carry everything the next step needs.

## Pipeline (run in order)

1. **`/speckit-specify`** *(inline)* — pass `$ARGUMENTS` as the feature
   description. Skip only if the input is empty and a spec already exists for
   the branch.
2. **`/speckit-clarify`** *(inline)* — this step would normally ask the user
   targeted questions. Since there is no manual review, **do not pause**: answer
   each question yourself by choosing the most reasonable option given the spec
   and codebase, record the chosen answers and a one-line rationale in the spec
   (as clarify would), and continue.
3. **`/speckit-plan`** *(sub-agent)* — generate the design artifacts. Report:
   the artifacts written.
4. **`/speckit-tasks`** *(sub-agent)* — generate `tasks.md`. Report: task count
   and phases.
5. **`/speckit-analyze`** *(sub-agent)* — cross-artifact consistency check.
   Analyze is read-only; the sub-agent must not edit files. Report: the
   **complete findings table verbatim** (ID, category, severity, location,
   summary, recommendation), or an explicit "no issues".
6. **Fix all issues** *(sub-agent per round)* — if analyze reported findings,
   launch a new sub-agent with the full findings table and have it resolve
   **every** finding, at all severities, by editing the relevant spec / plan /
   tasks artifacts. Report: each finding ID and how it was resolved. Then run
   **a new analyze sub-agent** (step 5 again) and repeat fix → analyze until
   analyze reports no remaining issues (cap at 3 fix rounds; if issues persist
   after 3 rounds, note the residual findings and continue anyway — do not
   stop).
7. **Commit** *(sub-agent)* — run `/speckit-git-commit` to commit the spec,
   plan, tasks, and fixes before any implementation. This creates a clean
   checkpoint separating the design artifacts from the implementation. Report:
   the commit SHA.
8. **`/speckit-implement`** *(sub-agent)* — execute the tasks. Report: tasks
   completed / skipped, test and build results, anything left undone.
9. **Commit the implementation** *(sub-agent)* — run `/speckit-git-commit` again
   to commit all changes produced by `implement`. Report: the commit SHA.
10. **Push and wait for CI** *(sub-agent)* — push the branch to `origin`, then
    wait for the GitHub Actions CI runs to finish. **This can take several
    minutes; the sub-agent must keep waiting and not give up early** (a single
    tool call times out after 10 minutes, so it should poll or re-watch rather
    than assume failure). Concretely:
    - Push: `git push -u origin HEAD`
    - Watch the run for the current commit to completion, e.g.
      `gh run watch <run-id> --exit-status` for each run of
      `gh run list --branch <branch> --commit <sha> --json databaseId,workflowName`
      (`--exit-status` makes it return non-zero if CI fails). If no run has
      registered yet, poll `gh run list` until one appears, then watch it.
      Multiple workflows may trigger (see `.github/workflows/`); wait for
      **all** of them.
    - Report: each workflow's name, run id, and conclusion; for a failure, the
      failing job and a short diagnosis from `gh run view <id> --log-failed`.
11. **Create the PR — only if CI passed** *(sub-agent)* — if every CI run
    succeeded, open a pull request into `master` with `gh pr create --base
    master` (title from the commits; body with a short summary and the spec
    directory, passed via `--body-file`). Report: the PR URL.

## Halting rules

Stop **only** if a step throws a hard error or its required inputs are missing
(e.g. no spec/plan on the branch, a script exits non-zero, git refuses the
commit, `git push` is rejected). A sub-agent reporting FAILED counts as a hard
error. Report the error and where it stopped. Do **not** stop for consistency
findings, clarify questions, or anything that would normally need human
judgment — resolve those autonomously and keep going.

**CI failure is a hard stop for the PR step only:** if any CI run fails, do
**not** open the PR. Report the failing workflow and the diagnosis. You may
attempt one autonomous fix: launch a new sub-agent with the diagnosis to fix,
commit, and re-push, then a new CI-wait sub-agent. If CI still fails, stop and
report without creating the PR.

## Between steps

After each step, print a one-line status:

```
[pipeline] <step> - done (or: skipped / auto-resolved / FAILED: <reason>)
```

## On completion

Print a final summary: which steps ran, which were skipped, the clarify
answers you chose, the analyze findings you fixed (and how many analyze rounds
ran), the two commit SHAs (design + implementation), the CI outcome (which
workflows ran and their conclusions), the PR URL (or the reason no PR was
opened), and the branch / spec directory the work landed in.
