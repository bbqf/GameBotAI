# Implementation Plan: Keep the command action of an Action step in gamepack install

**Branch**: `151-fix-gamepack-action-command` | **Date**: 2026-10-17 | **Spec**: `specs/151-fix-gamepack-action-command/spec.md`
**Input**: Feature specification from `/specs/151-fix-gamepack-action-command/spec.md` (GitHub issue #321, B-034)

## Summary

The gamepack install drops the command action of an Action step. A gamepack author writes the API form of a step
(`primitiveAction` with `payload`, `commandReference`). `GamepackFileSerializer.Parse` reads a sequence as the
stored type `CommandSequence`, which has `Action` with `Parameters`. The reader ignores unknown members, so the
action is lost without a message and the dry run says `valid: true`.

Technical approach (see `research.md`):

- A new class `GamepackStepReader` in the Domain project converts the JSON of each sequence from the file form to the
  stored form before `Deserialize<CommandSequence>`. It walks every step list: the root `steps`, the Loop `body`, and the
  `body` and `elseBody` of a conditional step (R3).
- Only an Action step accepts the aliases `primitiveAction`, `commandId`, and `commandReference` (R11). The reader
  stores `Action` (type, schemaVersion, `payload` copied to `Parameters`), `CommandId`, and a `CommandReference`
  (`commandId`, `commandName`). It does not store `isResolved`. `GET /api/sequences/{id}` computes it (R6).
- The reader gives a `structure` problem (sequence name, step ID, field name) for: a member that is not a stored
  member and not an accepted alias; an alias on a step that is not an Action step; two sources that name different
  commands; `action` and `primitiveAction` that differ in type or command (R4, R5). A step problem prevents the
  creation of the sequence.
- A step that names a command that is not in the file and not on the system gives a `reference` problem from the
  existing check in `GamepackValidator.CheckReferences` (R12). No new validator code is needed for this case.
- `GamepackReferences.RewriteCommandIds` also changes `Action.Parameters["commandId"]` for the type `command`, so
  the three copies of the command ID stay equal when the install gives the command a new ID (R7).
- The install description in `GamepacksEndpoints.cs` gets a sample of an Action step that runs a command (FR-005).
- `POST /api/sequences`, the export form, conditions, loops, `breakCondition`, and the other object types do not
  change (FR-007, FR-008).

## Technical Context

**Language/Version**: C# 12, .NET 8 (existing solution)
**Primary Dependencies**: ASP.NET Core minimal APIs, System.Text.Json (`JsonNode`), Swashbuckle (existing). No new package.
**Storage**: No change. The stored sequence format and `gamepacks\registry.json` keep their format.
**Testing**: xUnit. Unit, contract, and integration projects under `C:\src\GameBot\tests\`. The full suites run,
including the `POST /api/sequences` tests and the existing gamepack performance tests (R13).
**Target Platform**: Windows service (GameBot.Service).
**Project Type**: Web service with domain library (backend only).
**Performance Goals**: No new goal. The reader makes one pass over each sequence JSON object. The existing gamepack
performance tests (`GamepackPerformanceTests`, `GamepackTakeOverPerformanceTests`, `GamepackCatalogPerformanceTests`) pass.
**Constraints**: Atomic install (existing). A step problem prevents the creation of the sequence. The signature hash
reads the raw file JSON and does not change. A file without aliases reads as before.
**Scale/Scope**: A gamepack with up to a few hundred steps in nested lists.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces obeys Simplified Technical English (Constitution Principle VI).

*Phone check (Principle III)*: The feature has no web UI change. Phone support level: not applicable.

| Principle | Result |
|-----------|--------|
| I. Code quality | Pass. One small new class with one job. Public members have comments. No change to the domain model. |
| II. Testing | Pass. Each acceptance scenario and each form in FR-002 has a test. A foundational test shows that a step problem prevents the creation of the sequence. A round-trip test covers export then install. |
| III. UX consistency | Pass. Problems use the existing categories `structure` and `reference`. Status codes and response shapes do not change. The OpenAPI text gets the new sample (FR-005). |
| IV. Performance | Pass. One pass over each sequence JSON object. Existing performance tests must pass. |
| V. Living documentation | Pass. The PR updates `docs\gamepacks.md` (step forms) and `specs\STATUS.md`. |
| VI. STE | Pass. All new text uses STE. |
| Version bump | Pass. A task bumps `installer\versioning\version.override.json` before the push. |

Re-check after design: no violation. No entry in Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/151-fix-gamepack-action-command/
├── plan.md              # This file
├── research.md          # Phase 0 output (R1 to R13)
├── data-model.md        # Phase 1 output (file form to stored form)
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── gamepack-step-json.md   # Phase 1 output
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code: every file that the feature changes

```text
src/GameBot.Domain/Gamepacks/
├── GamepackStepReader.cs          # new: file form -> stored form for each step list; structure problems
├── GamepackFileSerializer.cs      # change: ReadList for sequences calls the step reader before Deserialize
└── GamepackReferences.cs          # change: RewriteCommandIds also rewrites Action.Parameters["commandId"]

src/GameBot.Service/Endpoints/
└── GamepacksEndpoints.cs          # change: WithDescription of InstallGamepack gets the Action step sample

tests/unit/Gamepacks/
├── GamepackStepFixtures.cs        # new: shared fixture (issue #321 file and step variants)
├── GamepackStepReaderTests.cs     # new: every form, nesting, conflicts, unknown member, alias on non-Action step
├── GamepackReferencesRewriteTests.cs  # new: the three ID copies, nested lists
└── GamepackValidatorStepTests.cs  # new: step problem blocks the plan; unresolved command -> reference problem

tests/contract/
├── GameBot.ContractTests.csproj   # change: link GamepackStepFixtures.cs (Compile Include)
└── Gamepacks/
    ├── GamepackActionStepInstallContractTests.cs  # new: issue #321 file, GET compare, dry run problems
    └── GamepackActionStepOpenApiTests.cs          # new: OpenAPI description has the sample

tests/integration/
├── GameBot.IntegrationTests.csproj  # change: link GamepackStepFixtures.cs (Compile Include)
└── Gamepacks/
    └── GamepackActionStepRoundTripTests.cs  # new: export then install, second install, other action types measured

docs/gamepacks.md                  # change: the step forms
specs/STATUS.md                    # change: feature row
installer/versioning/version.override.json   # change: version bump before push
```

**Files checked and not changed**:

- `src/GameBot.Domain/Gamepacks/GamepackValidator.cs`: the reference check already reads `CommandId` from each step
  (`GamepackReferences.CommandIds`). The reader sets it, so the existing check reports an unresolved command (R12).
  The step problems come from the parse, as other structure problems do.
- `src/GameBot.Domain/Gamepacks/GamepackService.cs`: both call sites use `GamepackFileSerializer.Parse` (dry run,
  install, catalog install). They get the fix with no change.
- `src/GameBot.Domain/Gamepacks/GamepackApplier.cs`: it writes the sequence objects that the plan holds. It does
  not read the file JSON.
- `src/GameBot.Domain/Gamepacks/Signing/GamepackContentHasher.cs`: it hashes the raw file JSON, so the hash does not change.
- `src/GameBot.Service/Endpoints/SequencesEndpoints.cs`: `POST /api/sequences` does not change (FR-008).
- `src/GameBot.Domain/Commands/SequenceStep.cs`: the stored model does not change.

**Structure Decision**: Backend only. The change stays in the Domain `Gamepacks` folder plus one description text in the Service.
The shared fixture lives in `tests\unit\Gamepacks` and the contract and integration projects link it, as they
link `GamepackTestKit.cs`. Tasks that write the same file are not parallel.

## Phase 0: Research

Done. See `research.md` (R1 to R13). No NEEDS CLARIFICATION remains.

## Phase 1: Design

Done. See `data-model.md`, `contracts\gamepack-step-json.md`, and `quickstart.md`. The agent context in `CLAUDE.md`
points to this plan.

## Test Plan

- Failing tests first: the reader tests and the contract test for the issue #321 file fail before the fix.
- Cover FR-002 forms: `primitiveAction` only; `commandReference` only; `commandId` only; all three; Loop `body`;
  conditional `body` and `elseBody`; stored form kept as given.
- Cover conflicts: `commandReference` vs `primitiveAction`; `commandId` vs `primitiveAction`; `action` vs `primitiveAction`;
  `commandId` with `primitiveAction` for the same command and no `commandReference`.
- Cover references: unresolvable command through each of the three forms gives a `reference` problem.
- Cover aliases on a step that is not an Action step: `structure` problem with sequence name, step ID, field name.
- Cover `isResolved`: accepted, not stored, computed by `GET /api/sequences/{id}`.
- Cover ID mapping: a new command ID changes `CommandId`, `CommandReference.CommandId`, and the payload `commandId`,
  also in nested lists.
- Cover export then install, and a second install (update or take over).
- Measure other action types (for example wait-for-image). If the install loses data, record a new issue. Do not fix it here.
- Run the full unit, contract, and integration suites, including the `POST /api/sequences` tests and the gamepack
  performance tests.

## Complexity Tracking

No violation. The table is empty.
