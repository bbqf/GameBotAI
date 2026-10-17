# Implementation Plan: Gamepack command reference check

**Branch**: `155-gamepack-command-reference-check` | **Date**: 2026-10-17 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/155-gamepack-command-reference-check/spec.md`

## Summary

The gamepack dry run and the install must report a `reference` problem for each sequence step that runs a command that is in no list (gamepack or instance). Today the validator reads only `SequenceStep.CommandId`. This plan extends the existing check in `GamepackReferences` and `GamepackValidator.CheckReferences`. It adds the other id forms (`commandReference.commandId`, Action payload `commandId`) and the name-only form (`commandReference.commandName`). It adds no second check, no format change, and no run-time change. The install uses the same validator result, so it reports the same problem.

## Technical Context

**Language/Version**: C# on .NET (existing GameBot solution)
**Primary Dependencies**: None new. Existing `GameBot.Domain.Gamepacks` types.
**Storage**: N/A. No stored data or file format change.
**Testing**: xUnit. One new unit test file for the validator, plus one integration test for the install.
**Target Platform**: Windows service (existing)
**Project Type**: Web service (backend only)
**Performance Goals**: None. The check is a linear scan of the steps.
**Constraints**: Same message form as the image check. Existing valid gamepacks MUST stay valid (SC-002).
**Scale/Scope**: Two source files change.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces obeys Simplified Technical English (Constitution Principle VI).

*Phone check (Constitution Principle III)*: No web UI change. Not applicable.

Gate result (before and after design): PASS.

- Tests cover each new branch (FR-008).
- No new project, dependency, or abstraction. The change extends one existing check.
- The version bump (FR-009) is a pipeline step before the push, not a code task.
- All new text is in STE.

## Project Structure

### Documentation (this feature)

```text
specs/155-gamepack-command-reference-check/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── reference-problem.md
└── tasks.md             # Phase 2 output (/speckit.tasks command)
```

### Source Code (repository root)

```text
src/GameBot.Domain/Gamepacks/
├── GamepackReferences.cs     # extend: read all id forms; add name-only references
└── GamepackValidator.cs      # extend CheckReferences: command name targets

tests/
├── unit/                     # new validator unit test file
└── integration/              # one install test (nothing written on a reference problem)
```

**Structure Decision**: Backend-only change in the existing `GameBot.Domain.Gamepacks` folder. Tests go in the existing test projects, in the folders that hold the other gamepack tests.

## Design Notes

- `GamepackReferences.CommandIds(CommandSequence)` returns ids from the step `CommandId`, `CommandReference.CommandId`, and Action payload `commandId` (type `command`), through `Flatten`. These are the same three forms that `RewriteCommandIds` rewrites.
- A new method returns the names of name-only steps (no id in any id form). The name is trimmed.
- `CheckReferences` builds a name target set (gamepack command names plus instance command names, minus removed commands, case-insensitive) and calls `ReportMissing` for names.
- A name next to an id is a label and is not checked. A command with the same name but another id does not resolve a missing id.
- Command-to-command checks do not change.
- See [research.md](research.md) and [data-model.md](data-model.md).

## Complexity Tracking

No violations. No entries.
