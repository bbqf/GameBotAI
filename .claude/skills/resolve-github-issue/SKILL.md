---
name: "resolve-github-issue"
description: "Pick the single highest-priority unclaimed GitHub issue (Bugs P1, then Features P1, Bugs P2, Features P2, and so on), claim it with the in-progress label so no other session works it in parallel, and drive it to a pull request through /speckit-pipeline. Exactly one issue per session."
argument-hint: "Optional: an issue number to work on (overrides automatic selection)"
compatibility: "Requires gh CLI authenticated for this repo and a spec-kit project structure with .specify/"
metadata:
  author: "local"
user-invocable: true
disable-model-invocation: false
---

## User Input

```text
$ARGUMENTS
```

If the input contains an issue number (`176`, `#176`, or an issue URL), work
**that** issue: skip automatic selection, but still run every claim and safety
check below. If the input is empty, select the issue automatically.

## Goal

Turn one open GitHub issue into a merged-ready pull request, fully
autonomously, using `/speckit-pipeline` for the actual specify -> plan -> tasks
-> implement -> CI -> PR work.

## Hard rules

1. **One issue per session.** After the chosen issue reaches a PR (or halts),
   **stop**. Never select a second issue in the same session, even if the first
   finishes quickly or is closed as invalid. Report and end the turn.
2. **Claim before working.** Never touch code for an issue that is not yet
   labelled `in-progress` by this session's claim.
3. **Never take over another session's claim.** An issue that already carries
   `in-progress` is off-limits (the one exception is the resume path in Step 1).
4. **Never close an issue by hand.** The PR's `Closes #N` closes it on merge.
5. **Windows shell rules.** Use the PowerShell tool, never Bash. Absolute paths
   with backslashes. No `&&` chaining (PowerShell 5.1); use `;` or separate
   calls. Write files with the Write tool, not `Set-Content` (it mangles UTF-8).
6. Temporary files go in the session scratchpad directory, never in the repo.
7. Execute Step 1 in its own Sub-Agent, Steps 2-4 in another one and the Step 5 in its own Sub-Agent again. Continue in this session from Step 6 on.

## Step 1 - Preflight and resume check

```powershell
gh auth status
git -C C:\src\GameBot status --porcelain
git -C C:\src\GameBot branch --show-current
```

- If tracked files are dirty, stop and report. Untracked files are fine.
- **Resume path:** if the current branch is a feature branch (not `master`) and
  it has a `specs/<branch>/spec.md` that references an issue which is still open
  and still labelled `in-progress`, this session is resuming that issue.
  Skip Steps 2-4 and go straight to Step 5, restarting `/speckit-pipeline` with
  an empty argument (it then picks up from the existing spec).
- Otherwise start from `master`:

```powershell
git -C C:\src\GameBot checkout master
git -C C:\src\GameBot pull --ff-only
```

Make sure the claim label exists (idempotent):

```powershell
gh label create in-progress --color "5319E7" --description "Claimed by an automated session - do not start parallel work" --force
```

## Step 2 - Select the issue

Walk the ladder below **top to bottom** and take the first tier that has a
candidate. Within a tier, take the **lowest issue number** (oldest first).

| Order | Labels |
|-------|--------|
| 1 | `bug` + `P1` |
| 2 | `enhancement` + `P1` |
| 3 | `documentation` + `P1` |
| 4 | `bug` + `P2` |
| 5 | `enhancement` + `P2` |
| 6 | `documentation` + `P2` |
| 7 | `bug` + `P3` |
| 8 | `enhancement` + `P3` |
| 9 | `documentation` + `P3` |
| 10 | `bug` with no priority label |
| 11 | `enhancement` with no priority label |
| 12 | `documentation` with no priority label |
| 13 | anything else still open |

`bug` = Bugs, `enhancement` = Features. Query one tier at a time:

```powershell
gh issue list --state open --search "label:bug label:P1 -label:in-progress -label:wontfix -label:duplicate -label:invalid -label:blocked sort:created-asc" --json number,title,url --limit 20
```

Substitute the tier's labels. For the unprioritized tiers add
`-label:P1 -label:P2 -label:P3`. For tier 13 use `--search "-label:in-progress
-label:wontfix -label:duplicate -label:invalid -label:blocked sort:created-asc"`
and take the first issue that carries none of the type labels above.

Reject a candidate and move to the next one if any of these hold - read the
issue first:

```powershell
gh issue view <N> --json number,title,body,labels,comments,url
```

- It already has a linked open PR, or a comment says the work is in flight.
- It is a question or discussion with no actionable change requested.
- It asks for something the project constitution forbids.

If **every** tier is empty, report "no unclaimed actionable issues" and stop.
Also list, in the report, any issue whose `in-progress` claim looks stale
(claim comment older than 24h with no branch or PR) so the user can decide
whether to release it - never release it yourself.

## Step 3 - Claim it

Label first, then leave an audit trail:

```powershell
gh issue edit <N> --add-label in-progress
gh issue comment <N> --body "Claimed by an automated Claude Code session on <UTC timestamp>. Working this issue through the spec-kit pipeline; do not start parallel work on it. The claim is released (label removed) if the run fails before any branch is created."
```

**Race check.** Immediately re-read the issue:

```powershell
gh issue view <N> --json labels,comments,url
```

If it carries a claim comment from another session that predates yours, you
lost the race: remove your label (`gh issue edit <N> --remove-label
in-progress` only if no other claim relied on it - if the other session's claim
is the live one, leave the label alone and just note that yours was withdrawn),
post a one-line comment withdrawing your claim, and go back to Step 2 for the
next candidate.

## Step 4 - Build the feature description

Compose a self-contained feature description from the issue title, body,
acceptance criteria and any clarifying comments. It must stand alone - the
pipeline's spec step gets only this text. Include:

- the issue number and URL, and `Closes #<N>`;
- the observed behaviour / current gap, verbatim from the issue where it is
  precise;
- the expected behaviour and any acceptance criteria the issue states;
- file paths, endpoints or components the issue names;
- explicit non-goals for anything the issue rules out.

Do not invent scope the issue did not ask for.

## Step 5 - Run the pipeline

Invoke the `speckit-pipeline` skill with that description as its argument. It
owns specify -> clarify -> plan -> tasks -> analyze -> fix -> commit ->
implement -> commit -> push -> wait for CI -> open PR. Do not duplicate its
steps here and do not second-guess it mid-run; let it run to its own halting
rules.

## Step 6 - Link the PR to the issue

Once the pipeline reports a PR URL, make sure the body closes the issue:

```powershell
gh pr view <PR-URL> --json number,body,url
```

If the body has no `Closes #<N>`, write the new body (existing body with
`Closes #<N>` on its own first line) to a file in the scratchpad with the Write
tool, then:

```powershell
gh pr edit <PR-URL> --body-file "<scratchpad>\pr-body.md"
```

Then report back on the issue:

```powershell
gh issue comment <N> --body "Implemented in <PR-URL> (branch `<branch>`). The issue closes automatically when the PR merges."
```

Leave `in-progress` on the issue - the work is real and still in flight until
the PR merges.

## Step 7 - Failure handling

If the pipeline halts:

- **Before any feature branch or commit exists** (spec never landed, gh/git
  error, issue turned out to be unactionable): release the claim -
  `gh issue edit <N> --remove-label in-progress` - and comment what happened and
  why, so the next session can pick it up cleanly.
- **After work is committed on a branch** (implementation done but CI red, push
  rejected, and so on): **keep** the claim. Comment on the issue with the branch
  name, the failing workflow, and a one-line diagnosis. A later session resumes
  it via the Step 1 resume path.

Either way, report the failure to the user. Do not move on to another issue.

## On completion

Print a summary:

- the issue picked, its tier in the ladder, and why the tiers above it were empty;
- the issues you skipped and the reason for each;
- the branch and spec directory;
- the pipeline's outcome (CI conclusions, PR URL, or where it halted);
- the claim state left behind (`in-progress` kept or released);
- any stale claims worth the user's attention.
