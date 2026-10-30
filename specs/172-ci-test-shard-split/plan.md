# Implementation Plan: Split the Largest CI Test Shard

**Branch**: `172-ci-test-shard-split` | **Date**: 2026-10-30 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/172-ci-test-shard-split/spec.md`

## Summary

The shard `contract-4` is the complement shard of the contract tests. It runs about 4 times
longer than the other contract shards, and it sets the CI time. This change splits it into
smaller named shards in `scripts/ci/test-shards.json`. The split uses measured local class
times. A new script, `scripts/ci/measure-shards.ps1`, runs each shard on a developer machine
and reports the shards that are more than 20% above the average of their project. A written
rule in `docs/ci-time.md` and in the constitution requires a new split when the script reports
a shard above the limit. A new pull request template asks the contributor for the result.
CI does not run the script.

## Technical Context

**Language/Version**: PowerShell 5.1 and 7 (CI scripts), C# on .NET (xUnit tests), GitHub Actions YAML
**Primary Dependencies**: `dotnet test`, the existing `scripts/ci/get-test-shard-filter.ps1` and `scripts/ci/test-shards.json`
**Storage**: Files only (`test-shards.json`)
**Testing**: xUnit unit tests in `tests/unit/Ci` (`TestShardTests`, new `ShardBalanceTests`)
**Target Platform**: Windows developer machines and Windows GitHub runners
**Project Type**: Repository tooling (CI scripts, docs, constitution)
**Performance Goals**: Longest contract shard at most 1.2 times the contract average (local). CI time lower than before the split (SC-001, SC-002)
**Constraints**: Each added shard costs about 25 s of fixed time (see `docs/ci-time.md`). Every test class runs in exactly one shard. The pinned and coverage duties do not move. No unit or integration rebalance in this change (FR-003a)
**Scale/Scope**: 4 contract shards before, 5 to 6 after. About 197 contract test files

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Gate | Result |
|------|--------|
| Release blocker (build and tests green) | Pass. No failing run is known. The tasks run the shard tests before each commit |
| STE (Principle VI) | Pass. All new text in this plan and its artifacts follows STE |
| Phone check (Principle III) | Not applicable. No web UI change |
| Code quality (I) | Pass. The new script has a clear param block and header comment. No dead code |
| Testing (II) | Pass. New unit tests cover the balance calculation. `TestShardTests` proves one shard for each class |
| Performance (IV) | Pass. The goal is stated above and measured by the new script |
| Living documentation (V) | Pass. `docs/ci-time.md` is updated. No change to `docs/architecture.md` (no domain, API, or persistence change). Spec Status line and `specs/STATUS.md` are updated |
| Version bump | Pass. The tasks include the `version.override.json` bump before the PR |

No violation. The Complexity Tracking section is empty.

## Project Structure

### Documentation (this feature)

```text
specs/172-ci-test-shard-split/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── measure-shards-cli.md
├── checklists/
│   └── requirements.md
└── tasks.md             # Created by /speckit-tasks
```

### Source Code (repository root)

```text
scripts/ci/
├── test-shards.json          # Change: split contract-4 into named shards plus a small complement
├── measure-shards.ps1        # New: local timing and 20% rule check
└── get-test-shard-filter.ps1 # No change expected

tests/unit/Ci/
├── TestShardTests.cs         # No change expected (it already proves one shard for each class)
└── ShardBalanceTests.cs      # New: tests of the balance calculation (uses -TimesFile, no dotnet run)

docs/ci-time.md               # Change: shard balance rule, how to measure, new shard numbers
.specify/memory/constitution.md # Change: add the rule to Development Workflow, bump to 1.6.0
.github/pull_request_template.md # New: checklist item for the shard check
installer/versioning/version.override.json # Change: patch bump before the PR
specs/STATUS.md               # Change: add feature 172
```

**Structure Decision**: Repository tooling only. The shard list stays the one source of truth
(`test-shards.json`). The workflow `dotnet.yml` reads shard names from the script, so it needs
no change when the names change. The check script is a separate file, so the workflow never
runs it.

## Design Notes

1. **Measure first.** Run the contract project once locally with a `trx` logger. Sum the class
   durations for each namespace prefix. This gives the data to build the split (research R1).
2. **Split by prefix.** Move the heavy prefixes out of the complement into named shards, as the
   existing manifest does. Keep the complement small. A new class still falls into it.
3. **Check script.** `measure-shards.ps1 -Project <name>` runs each shard with its filter and
   records the wall time. It prints a table, the average, and the limit (average times 1.2). It
   exits with 1 when a shard is above the limit. A `-TimesFile` option reads times from JSON, so
   unit tests need no `dotnet` run.
4. **Stable time.** The script repeats a shard that is above the limit one time before it
   reports a failure (spec edge case on run-time noise).
5. **Rule text.** One short section "Shard balance rule" in `docs/ci-time.md`. One bullet in the
   constitution Development Workflow that points to it. The PR template asks for the result when
   the change adds many tests or changes the shard list.

## Post-Design Constitution Check

Pass. The design adds no new violation. Fixed cost per shard (about 25 s) is part of the
success check (FR-011): the new longest job must stay below the old `contract-4` time plus the
other jobs on the critical path.
