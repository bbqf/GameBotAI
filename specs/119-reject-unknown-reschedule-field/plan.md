# Implementation Plan: Reject an unknown field in a reschedule-self payload

**Branch**: `119-reject-unknown-reschedule-field` | **Date**: 2026-09-30 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/119-reject-unknown-reschedule-field/spec.md`

## Summary

The step validation accepts a `reschedule-self` payload with an unknown top-level field, for example `nextDay`. The fix adds one check in `SequenceStepValidationService.ValidateRescheduleSelfPayload`. The check compares each top-level payload key with the four known keys (`option`, `timerTimeOfDay`, `timerRelativeOffset`, `ocrOffset`), without regard to letter case. It adds one error that names all unknown keys and lists the known keys. The run-time reader (`SelfReschedulePayload.TryRead`) and the load path stay as they are.

## Technical Context

**Language/Version**: C# on .NET (existing solution)
**Primary Dependencies**: None new. Uses the key constants in `SelfReschedulePayload`.
**Storage**: N/A
**Testing**: xUnit unit tests (`tests\unit\Sequences`) and contract tests (`tests\contract\Sequences`)
**Target Platform**: GameBot Windows service (existing)
**Project Type**: web-service (existing)
**Performance Goals**: N/A (a loop over at most a few keys)
**Constraints**: The load path and the run-time reader must not change. No payload that is valid today may become invalid, except a payload with an unknown top-level field.
**Scale/Scope**: One method, one error message, tests.

Code read to confirm the site:
- `SequenceStepValidationService.cs` line 335 calls `ValidateRescheduleSelfPayload(step.Action, ...)`. The method starts at line 427. It calls `SelfReschedulePayload.TryRead` first and returns on a parse error (this keeps the `Bogus` option message).
- `SelfReschedulePayload.cs` has the constants `OptionKey`, `TimerTimeOfDayKey`, `TimerRelativeOffsetKey`, `OcrOffsetKey`. Its reader finds keys without regard to letter case.
- Existing tests: `tests\unit\Sequences\OcrOffsetValidationTests.cs`, `tests\unit\Sequences\SequenceStepValidationServiceActionTypeTests.cs`, `tests\contract\Sequences\SelfRescheduleActionContractTests.cs`.

## Constitution Check

- I. Code Quality: PASS. One small method change. No new dependency.
- II. Testing: PASS. Unit and contract tests are in scope (reject unknown, accept known, accept other letter case, `Bogus` unchanged). The tests must fail before the fix.
- III. UX Consistency: PASS. The error text follows the existing style `Step '<label>' reschedule-self ...`.
- IV. Performance: PASS. No effect.
- V. Living Documentation: PASS with tasks. Add an entry to `CHANGELOG.md` (section Unreleased, Fixed). Add one sentence to the `reschedule-self` text in `src/GameBot.Service/Swagger/PrimitiveActionSchemaFilter.cs`. Check `docs\architecture.md` for a statement on reschedule-self validation. Update it and the "Last reviewed" date if the text is affected. Set the `Status` line of `spec.md` and `specs\STATUS.md` when the work is done.
- VI. STE: PASS. All new text follows STE.
- Build/test gate: the build and required tests must be green before implementation starts.

Post-design re-check: no change. No violation.

## Design Decisions

1. **Where**: `ValidateRescheduleSelfPayload`, after the `TryRead` success branch (so a wrong `option` keeps its message and its early return). The unknown-field error is added to `errors`, and the value checks that follow stay as they are. For an unknown field with a wrong `option`, only the option error shows. The unknown field shows after the author fixes the option (spec edge case).
2. **Key match**: `StringComparer.OrdinalIgnoreCase` against a static known-key set built from the four `SelfReschedulePayload` constants.
3. **Message**: `Step '<label>' reschedule-self payload has unknown field(s): nextDay, other. Known fields: option, timerTimeOfDay, timerRelativeOffset, ocrOffset.` The tests check only that each unknown name is in the message. They do not check the order.
4. **Scope**: top-level keys of `action.Parameters` only. The nested `ocrOffset` object is not checked. Other action types are not changed.
5. **Not changed**: `SelfReschedulePayload.TryRead`, the load path, the queue template validators, and the sequence action type lists.

## Project Structure

### Documentation (this feature)

```text
specs/119-reject-unknown-reschedule-field/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── reschedule-self-validation.md
└── tasks.md             # Created later by the tasks step
```

### Source Code (repository root)

```text
src/GameBot.Domain/Services/SequenceStepValidationService.cs   # add the check
tests/unit/Sequences/                                          # new or extended unit tests
tests/contract/Sequences/SelfRescheduleActionContractTests.cs  # add contract tests
src/GameBot.Service/Swagger/PrimitiveActionSchemaFilter.cs      # one sentence in the reschedule-self payload text
CHANGELOG.md                                                   # Unreleased, Fixed entry
docs/architecture.md                                           # update only if it describes this validation
```

**Structure Decision**: Existing single solution layout. No new project or file in `src`.

## Test Plan

Unit (validator, no HTTP):
- Unknown field `nextDay` with a valid Timer payload gives an error that contains `nextDay`.
- Two unknown fields give one error that names both.
- Keys `nextDay` and `NextDay` are both listed, as the author wrote them.
- Each known field set that is valid today stays valid (`Timer` with `timerTimeOfDay`, `Timer` with `timerRelativeOffset`, `Timer` with `ocrOffset`, `OncePerRun`, `AtQueueStart`, `EveryStep`).
- `TimerTimeOfDay` (other letter case) stays valid.
- `option: "Bogus"` gives the same message as before.
- An unknown field inside `ocrOffset` is not rejected (scope guard).
- `option: "Bogus"` with `nextDay` gives the option error and no unknown-field error (the unknown field shows after the fix of the option).
- `SelfReschedulePayload.TryRead` still succeeds with an unknown key (the load path is unchanged, FR-009).

Contract (HTTP, `SelfRescheduleActionContractTests`):
- POST sequence with `nextDay` and `dryRun: true` returns 400 and the body contains `nextDay`.
- The same POST without `dryRun` returns 400 and the sequence is not stored.
- PUT of an existing sequence with a `reschedule-self` step that has `nextDay` returns 400. The stored sequence stays unchanged. (`PUT` and `PATCH` use the same step validation as `POST`. A separate test covers `PATCH`.)
- PATCH of an existing sequence with a `reschedule-self` step that has `nextDay` returns 400. The stored sequence stays unchanged.
- A payload with known fields only returns success.
- `Bogus` option returns 400 with the unchanged message.
- `Bogus` option with `nextDay` returns 400 with the option error and no unknown-field error.

Contract test note: contract tests share a bin data dir. Each test must use its own sequence ids and must not leave data in that dir that can break other tests (for example the ExecutionLogs tests).

## Complexity Tracking

No violation. Nothing to justify.
