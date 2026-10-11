# Contract: CI Workflow

This is the contract of the `.NET CI` workflow after the change. Other tools depend on it: the speckit pipeline, the guide, and the maintainers who read check names.

## Trigger

- `push` to every branch, and `workflow_dispatch`. No `pull_request` trigger and no `paths` filter (a run must exist for every push).
- Older runs for the same branch are cancelled when a new push arrives.

## Jobs

| Job | Runner | Runs when | Result rule |
|-----|--------|-----------|-------------|
| `changes` | Linux | Always | Fails if the secret scan finds a pattern. Sets `dotnet` and `webui` |
| `compile` | Windows | `dotnet` is true | Restores, builds for tests (no analyzers), uploads one artifact per test project |
| `analyze` | Windows | `dotnet` is true | Full build with all analyzers and `-warnaserror`. Fails the run on any warning |
| `test-unit` | Windows | `dotnet` is true, after `compile` | Runs unit tests with coverage. Uploads the coverage file |
| `test-integration` (matrix) | Windows | `dotnet` is true, after `compile` | Runs one shard in each matrix job. The pinned shard also collects coverage and runs the performance gate (`GAMEBOT_PERF_PROFILE: ci`). It uploads the coverage file |
| `test-contract` | Windows | `dotnet` is true, after `compile` | Runs contract tests with no coverage |
| `web-ui-tests` | Windows | `webui` is true | Runs `npm ci` and the web UI tests with coverage |
| `ci-gate` | Linux | Always (`if: always()`) | Runs the coverage thresholds from the two coverage files. Passes if every needed job passed or was skipped by a path rule. Fails on a failed or cancelled job, or on a missing coverage file |

The job names above are final (task T012). The guide lists them.

A compile failure or a secret-scan failure stops the test jobs. A failure of `analyze` does not stop them. It fails `ci-gate`, and so it fails the run (FR-007). If the team drops the `analyze` split, one build job holds all analyzers.

The coverage thresholds run in `ci-gate` on Linux. A separate Windows job would add about 40 s of start time. The CodeQL jobs run apart from this workflow. They are outside the time limits.

## Rules

1. The `changes` job sets both switches to true for any path that no group names.
2. A job skipped by a path rule counts as a pass in `ci-gate`. A job skipped for any other reason (for example, a failed needed job) counts as a failure.
3. A gate that finds no coverage data fails (FR-012).
4. The artifacts have a short retention (1 day).
5. The workflow has no job that builds the installer. The `release-installer` workflow stays manual.
6. The number of tests that run does not fall. The guide holds the before and after counts (SC-004).

## Outputs for the speckit pipeline

The pipeline runs `gh run watch <run-id> --exit-status` for each run of the head commit. The run conclusion must be success when all needed jobs pass or are skipped by a path rule, and failure otherwise. The `ci-gate` job makes this true.
