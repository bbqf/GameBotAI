---
name: "speckit-pipeline"
description: "Orchestrate the full spec-kit pipeline end to end autonomously: specify -> clarify -> plan -> tasks -> analyze -> fix -> compact -> commit -> implement -> commit -> wait for CI -> open PR, without stopping for manual review."
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
speckit skills in order, in this same conversation, so their context carries
forward. Run **fully autonomously** — the user is not reviewing anything
manually. Do not pause for confirmation, questions, or gates. Only a hard
failure (see Halting rules) stops the run.

## Pipeline (run in order)

1. **`/speckit-specify`** — pass `$ARGUMENTS` as the feature description.
   Skip only if the input is empty and a spec already exists for the branch.
2. **`/speckit-clarify`** — this step would normally ask the user targeted
   questions. Since there is no manual review, **do not pause**: answer each
   question yourself by choosing the most reasonable option given the spec and
   codebase, record the chosen answers and a one-line rationale in the spec (as
   clarify would), and continue.
3. **`/speckit-plan`** — generate the design artifacts.
4. **`/speckit-tasks`** — generate `tasks.md`.
5. **`/speckit-analyze`** — cross-artifact consistency check.
6. **Fix all issues** — resolve **every** finding `analyze` reported, at all
   severities, by editing the relevant spec / plan / tasks artifacts. Then
   **re-run `/speckit-analyze`** and repeat this fix step until analyze reports
   no remaining issues (cap at 3 rounds; if issues persist after 3 rounds, note
   the residual findings and continue anyway — do not stop).
7. **Compact memory** — now that the design artifacts are finalized and clean,
   run `/compact` to compress the conversation context. The spec / plan / tasks
   files on disk are the source of truth from here on, so the detailed
   back-and-forth from earlier steps no longer needs to stay in context; this
   frees room for the implementation phase. Continue immediately afterward.
8. **Commit** — run `/speckit-git-commit` to commit the spec, plan, tasks, and
   fixes before any implementation. This creates a clean checkpoint separating
   the design artifacts from the implementation.
9. **`/speckit-implement`** — execute the tasks.
10. **Commit the implementation** — run `/speckit-git-commit` again to commit all
    changes produced by `implement`.
11. **Push and wait for CI** — push the branch to `origin`, then wait for the
    GitHub Actions CI runs to finish. **This can take several minutes; keep
    waiting, do not give up early.** Concretely:
    - Push: `git push -u origin HEAD`
    - Watch the run for the current commit to completion, e.g.
      `gh run watch $(gh run list --branch "$(git branch --show-current)" --limit 1 --json databaseId --jq '.[0].databaseId') --exit-status`
      (`--exit-status` makes it return non-zero if CI fails). If the run has not
      registered yet, poll `gh run list --branch <branch>` until it appears,
      then watch it. Multiple workflows may trigger (see `.github/workflows/`);
      wait for **all** of them.
12. **Create the PR — only if CI passed** — if every CI run succeeded, open a
    pull request into `master` with `gh pr create --base master --fill` (title
    and body from the commits; expand the body with a short summary and the spec
    directory). Report the PR URL.

## Halting rules

Stop **only** if a step throws a hard error or its required inputs are missing
(e.g. no spec/plan on the branch, a script exits non-zero, git refuses the
commit, `git push` is rejected). Report the error and where it stopped. Do
**not** stop for consistency findings, clarify questions, or anything that
would normally need human judgment — resolve those autonomously and keep going.

**CI failure is a hard stop for the PR step only:** if any CI run fails, do
**not** open the PR. Report the failing workflow and a short diagnosis (pull the
failed job log with `gh run view <id> --log-failed`). You may attempt an
autonomous fix and re-push once; if CI still fails, stop and report without
creating the PR.

## Between steps

After each step, print a one-line status:

```
[pipeline] <step> - done (or: skipped / auto-resolved / FAILED: <reason>)
```

## On completion

Print a final summary: which steps ran, which were skipped, the clarify
answers you chose, the analyze findings you fixed, the two commit SHAs (design
+ implementation), the CI outcome (which workflows ran and their conclusions),
the PR URL (or the reason no PR was opened), and the branch / spec directory
the work landed in.
