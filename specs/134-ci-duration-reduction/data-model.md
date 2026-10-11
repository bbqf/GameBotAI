# Data Model: Reduce CI Run Duration

This feature has no product data. These are the records that the scripts and the guide use. They are not stored in a database.

## ChangeGroup

A set of file paths with one effect on the jobs.

| Field | Meaning |
|-------|---------|
| `name` | `docs`, `webui`, `backend`, `shared` |
| `patterns` | Path patterns that belong to the group |
| `runsDotnet` | True if a change in this group needs the .NET jobs |
| `runsWebui` | True if a change in this group needs the web UI job |

Rules:

- `docs`: `specs/**`, `docs/**`, `**/*.md`, `.specify/**`, `.claude/**`, `.github/agents/**`, `.github/prompts/**`. Both switches are false.
- `webui`: `src/web-ui/**`. `runsWebui` is true; `runsDotnet` is false.
- `backend`: `src/GameBot.*/**`, `tests/**`, `installer/**`. `runsDotnet` is true; `runsWebui` is false.
- `shared`: workflow files, `scripts/**`, root build files, project files, lock files, API contract files. Both switches are true.
- A path in no group counts as `shared`.
- The result for a push is the union of the switches of all changed files.

## JobSwitches

The output of the `changes` job.

| Field | Values |
|-------|--------|
| `dotnet` | `true` or `false` |
| `webui` | `true` or `false` |
| `reason` | One line that lists the groups that were found (for the run log) |

## TestShard

| Field | Meaning |
|-------|---------|
| `project` | `unit`, `integration`, `contract` |
| `name` | Shard name, for example `integration-1` |
| `filter` | Test filter. The last shard of a project uses the complement of all named filters |
| `collectsCoverage` | True for the unit and integration jobs only |

Rules:

- Every discovered test is in exactly one shard of its project.
- A new test class runs in the complement shard until the team gives it a named place.

## RunTiming

One row of the baseline and final tables in the guide.

| Field | Meaning |
|-------|---------|
| `runId` | Workflow run number |
| `queueSeconds` | Time from the push to the start of the first job |
| `stepSeconds` | Time per step or job |
| `criticalPathSeconds` | Time from the push to the end of the last check, minus queue time |
| `result` | Success, failure, cancelled |

## FailureRecord

| Field | Meaning |
|-------|---------|
| `runId` | Run that failed |
| `class` | `real-defect`, `flaky-test`, `build-warning`, `infrastructure`, `other` |
| `test` | Test name, if a test failed |
| `action` | `fixed`, `isolated`, `recorded` (with a reason) |

## SlowTimePattern

| Field | Meaning |
|-------|---------|
| `name` | Short name of the pattern |
| `measuredEffect` | Seconds or percent, from a measurement |
| `rule` | How to avoid the pattern |
