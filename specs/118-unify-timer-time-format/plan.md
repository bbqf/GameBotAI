# Implementation Plan: One format for timerTimeOfDay

**Branch**: `118-unify-timer-time-format` | **Date**: 2026-09-30 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/118-unify-timer-time-format/spec.md` (GitHub issue #226, B-023)

## Summary

Two validators check the field `timerTimeOfDay` with two different rules.

- The queue-template endpoint (`QueueTemplatesEndpoints.cs`) accepts only `HH:mm` (`TimeOnly.TryParseExact` with one format).
- The sequence validator (`SelfReschedulePayload.TryRead`, called by `SequenceStepValidationService`) uses `TimeOnly.TryParse`. It accepts `HH:mm:ss`, and also not-strict forms such as `11:00 PM` and `9:30`. Its error text says `HH:mm:ss`.

Selected result (spec clarification): both validators accept exactly two strict forms, `HH:mm` and `HH:mm:ss` (24-hour, `00:00` to `23:59:59`). They reject all other strings.

The fix adds one shared strict parser in the domain library. Both validators call it. The template endpoint also uses it to parse the value that it stores. The template response writes `HH:mm` when the seconds are zero. It writes `HH:mm:ss` when they are not zero. The doc comments and the OpenAPI description state the same rule. The tests come first and fail before the fix.

## Technical Context

**Language/Version**: C# 13 on .NET 9 (`net9.0`)
**Primary Dependencies**: domain library (`GameBot.Domain`), ASP.NET Core minimal API host (`GameBot.Service`) with Swashbuckle; no new dependency
**Storage**: N/A. `QueueTemplateEntry.TimerTimeOfDay` is a `TimeOnly?`. The stored template file keeps its format. The `TimeOnly` serializer already writes seconds when they are not zero. No migration.
**Testing**: xUnit + FluentAssertions. Unit tests in `tests/unit`. Integration tests in `tests/integration/QueueTemplates` and `tests/integration/Sequences` with `WebApplicationFactory<Program>` and `GAMEBOT_USE_ADB=false`. Contract tests in `tests/contract/Sequences` and `tests/contract/QueueTemplates`.
**Target Platform**: Windows service (local host), emulator through ADB
**Project Type**: web service (backend only; no web UI change)
**Performance Goals**: No measurable change. One parse of a short string for each entry or step on save.
**Constraints**: No change to `timerRelativeOffset`, other schedule fields, how schedules run, or the `nextDay` behavior (FR-008, issue #228). A stored `HH:mm` value keeps working (FR-006).
**Scale/Scope**: 1 new domain file (about 40 lines), 2 changed validator call sites, 1 changed response mapper line, 1 changed OpenAPI string, and 3 doc comment edits. Also docs and status edits. New unit, integration, and contract tests.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces (research, data model, contracts, quickstart, tasks, code comments, user-facing messages) MUST obey Simplified Technical English (Constitution Principle VI).

| Principle | Gate | Status |
|-----------|------|--------|
| I. Code Quality | One small static class with doc comments. It replaces two ad-hoc parse calls, so it removes duplicate logic. No dead code. CamelCase method names only. | PASS |
| II. Testing | Bug fix: write the failing tests first. Unit matrix for the parser. Endpoint tests for both endpoints with `HH:mm` and `HH:mm:ss`. A same-result test sends one value list to both validators. Current tests stay green (SC-002). A test that pins a not-strict form or an old error text is the only exception (see Test approach). | PASS |
| III. UX Consistency | Both error messages name the same format. The OpenAPI descriptions state the same rule. The response format for a zero-seconds value does not change. | PASS |
| IV. Performance | Declared above. Not a hot path. | PASS |
| V. Living Documentation | Change the `timerTimeOfDay` sentence in `docs/architecture.md` and its "Last reviewed" line. Add a "Fixed" entry to `CHANGELOG.md`. Add row 118 to `specs/STATUS.md`. Set the Status of this spec to "Implemented" at the end. | PASS (tasks must include these edits) |
| VI. STE | All new text (plan, research, data model, contract, quickstart, code comments, error text, OpenAPI text, docs, changelog) is in STE. | PASS |

No violations. Complexity Tracking is empty.

**Post-design re-check (after Phase 1)**: The design adds one public static class in the domain library and no API field. It makes the rule of one validator stricter. This is a deliberate choice in the spec. It loosens the other validator in a compatible way. All gates stay PASS.

## Project Structure

### Documentation (this feature)

```text
specs/118-unify-timer-time-format/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── timer-time-of-day-format.md   # Accept/reject matrix, messages, response rule, OpenAPI text
└── tasks.md             # Phase 2 output (/speckit-tasks, not made by this command)
```

### Source Code (repository root)

```text
src/GameBot.Domain/
├── Services/TimerTimeOfDayFormat.cs                        # ADD: TryParse + Format (shared strict rule)
├── Commands/SelfReschedule/SelfReschedulePayload.cs        # CHANGE: line ~74 use TimerTimeOfDayFormat.TryParse; new error text
├── Services/SequenceStepValidationService.cs               # CHECK: no own parse of timerTimeOfDay (it reads the parsed payload); no change expected
└── QueueTemplates/QueueTemplateEntry.cs                    # CHANGE: doc comment names both forms

src/GameBot.Service/
├── Endpoints/QueueTemplatesEndpoints.cs                    # CHANGE: line 61 validate, line 86 parse, line 238 response format
├── Swagger/PrimitiveActionSchemaFilter.cs                  # CHANGE: RescheduleSelf text (line ~59) states the strict rule
└── Contracts/QueueTemplates/
    ├── TemplateEntrySaveRequest.cs                         # CHANGE: doc comment (HH:mm or HH:mm:ss)
    └── QueueTemplateDetailResponse.cs                      # CHANGE: doc comment (format rule)

tests/
├── unit/Sequences/TimerTimeOfDayFormatTests.cs             # ADD: shared accept/reject matrix + Format cases
├── unit/Sequences/SelfReschedulePayloadTests.cs            # CHANGE: add strict-reject cases and message check
├── integration/QueueTemplates/QueueTemplatesScheduleTypeTests.cs  # CHANGE: add HH:mm:ss save/echo tests, message test
├── integration/Sequences/TimerTimeOfDayParityTests.cs      # ADD: one value list to both endpoints, equal results (SC-001)
├── contract/Sequences/PrimitiveActionTypesOpenApiTests.cs  # CHANGE: reschedule-self description names the strict rule
└── contract/QueueTemplates/TemplateTimerTimeOfDayOpenApiTests.cs  # ADD: template entry timerTimeOfDay text names the strict rule

docs/architecture.md                                        # CHANGE: timerTimeOfDay sentence (line ~237) + Last reviewed
CHANGELOG.md                                                # CHANGE: Fixed entry (118, #226)
specs/STATUS.md                                             # CHANGE: ADD row 118
specs/118-unify-timer-time-format/spec.md                   # CHANGE: Status "Implemented" at the end
```

**Structure Decision**: The rule lives in the domain library (`src/GameBot.Domain`). The service and the domain validators share one implementation. The service must not own the rule. `SelfReschedulePayload` is in the domain and cannot call the service. The web UI does not change.

## Design

### Root cause

1. `QueueTemplatesEndpoints.cs` line 61 calls `TimeOnly.TryParseExact(value, "HH:mm", out _)`. Line 86 parses with `TimeOnly.ParseExact(value, "HH:mm")`. So `HH:mm:ss` gets a 400.
2. `SelfReschedulePayload.TryRead` line 74 calls `TimeOnly.TryParse(value, InvariantCulture, out _)`. This accepts many forms that no document names. Its message says `HH:mm:ss`.
3. The template response (line 238) always writes `HH:mm`. A stored value with seconds loses its seconds.
4. The OpenAPI text of `reschedule-self` says `HH:mm:ss`. The doc comments of the template contracts say `HH:mm`.

### Change

1. **New `src/GameBot.Domain/Services/TimerTimeOfDayFormat.cs`** (public static class, STE doc comments):
   - `AcceptedFormatText` (const string): `HH:mm or HH:mm:ss (24-hour)`. Both error messages use this constant.
   - `TryParse(string? value, out TimeOnly result)`: return false for null. Call `TimeOnly.TryParseExact(value, new[] { "HH:mm", "HH:mm:ss" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out result)`.
     - `DateTimeStyles.None` gives no space tolerance.
     - The exact format `HH` needs two digits, so `9:30` fails.
     - The hour range is `00` to `23`, so `24:00` fails.
   - `Format(TimeOnly value)`: return `HH:mm` when `value.Second == 0`. Return `HH:mm:ss` in all other cases. Use `InvariantCulture`.
     - A `TimeOnly` from the parser has no sub-second part.
     - If a stored value has ticks below one second, `Format` drops them, as it does now (research R-004).
2. **`QueueTemplatesEndpoints.cs`**:
   - Line 61 uses `TimerTimeOfDayFormat.TryParse`.
   - The new message is `entries[{i}].timerTimeOfDay '{value}' is not a valid time of day; accepted format: HH:mm or HH:mm:ss (24-hour), for example '15:30' or '15:30:00'`.
   - Line 86 uses `TimerTimeOfDayFormat.TryParse` and the parsed value. The value passed validation, so the parse always succeeds.
   - Line 238 uses `TimerTimeOfDayFormat.Format`.
3. **`SelfReschedulePayload.TryRead`**: line 74 uses `TimerTimeOfDayFormat.TryParse`.
   - The message has the same accepted-format text as the template message: `timerTimeOfDay '{value}' is not a valid time of day; accepted format: HH:mm or HH:mm:ss (24-hour), for example '15:30' or '15:30:00'`.
   - The step label prefix stays as it is now in the sequence validator.
4. **`SequenceStepValidationService.cs`**: research R-002 shows that it reads the parsed payload. It holds no own parse. Task T001 confirms this. If T001 finds a second parse, task T001a routes it through the shared parser.
5. **OpenAPI and docs**:
   - In `PrimitiveActionSchemaFilter`, change the `RescheduleSelf` text `timerTimeOfDay (HH:mm:ss, service-local time of day)` to `timerTimeOfDay (HH:mm or HH:mm:ss, 24-hour, service-local time of day, no other form)`.
   - The two template doc comments (request and response) and `QueueTemplateEntry` state the rule. The response comment states the output rule.
   - Swashbuckle takes these texts from XML comments only when XML docs are on. Research R-005 records the check.
   - First, find the source of the template entry description. If none exists, add the note: a small schema-filter note (see contract).
6. **Docs and status**: see Constitution Check, principle V.

Behavior that follows from the design:
- `HH:mm` values that pass today pass after the fix (SC-002). Both endpoints accept `HH:mm:ss`.
- The sequence validator now rejects `11:00 PM`, `9:30`, ` 11:00`, and `24:00`. This is the deliberate, stricter rule in the clarification.
- A template saved with `15:30:45` returns `15:30:45`. A template saved with `15:30` or `15:30:00` returns `15:30`.
- The run behavior does not change: both paths already hold a `TimeOnly`.

### Test approach

Write the tests first. These tests must fail before the fix (Constitution II): the `HH:mm:ss` template tests, the strict-reject sequence tests, and the same-result test.

1. **Parser unit tests** (`tests/unit/Sequences/TimerTimeOfDayFormatTests.cs`):
   - A `[Theory]` accept list: `00:00`, `09:05`, `15:30`, `23:59`, `00:00:00`, `15:30:45`, `23:59:59`.
   - A reject list: `null`, empty, space only, `24:00`, `24:00:00`, `23:60`, `12:00:60`, `9:30`, `9:30:00`, `11:00 PM`, ` 15:30`, `15:30 `, `15:30:`, `15.30`, `1530`, `15:30:45.123`, `abc`.
   - `Format` cases: zero seconds gives `HH:mm`. Non-zero seconds give `HH:mm:ss`. A value with sub-second ticks gives the text without the ticks.
   - A round trip `Format` then `TryParse` gives the same value.
2. **Payload unit tests** (`SelfReschedulePayloadTests.cs`): `HH:mm` and `HH:mm:ss` parse (the current test uses `14:30:00`). Each reject value fails with an error that contains `HH:mm or HH:mm:ss`.
3. **Template endpoint tests** (`QueueTemplatesScheduleTypeTests.cs`, current helpers):
   - Save `15:30:45` returns 201, and the response has `15:30:45`.
   - Save `15:30:00` returns `15:30`. Save `15:30` returns `15:30` (current test).
   - Save `5pm`, `24:00`, ` 15:30`, or `9:30` returns 400 with `HH:mm or HH:mm:ss` in the message.
   - A GET after a save gives the same text as the save response.
   - An empty or missing value keeps the "exactly one of timerTimeOfDay or timerRelativeOffset" error, and that error text is the same as before.
4. **Same-result test** (`tests/integration/Sequences/TimerTimeOfDayParityTests.cs`): use a list of at least 12 strings (valid and invalid, both formats).
   - For each string, POST a template with a Timer entry. Also POST a sequence with `dryRun: true` and a `reschedule-self` Timer step.
   - Assert that the accept/reject results are equal for every string. Assert that the accepted set equals the expected set (SC-001, FR-001).
   - Assert that the rejected messages of the two endpoints both contain `HH:mm or HH:mm:ss` (SC-004).
5. **Contract tests**:
   - In `PrimitiveActionTypesOpenApiTests.cs`, assert that the `reschedule-self` payload description contains `HH:mm or HH:mm:ss`.
   - In the new file `tests/contract/QueueTemplates/TemplateTimerTimeOfDayOpenApiTests.cs`, assert that the OpenAPI text of the template entry `timerTimeOfDay` contains the same phrase (FR-005).
6. Run all tests in the touched folders. Current tests keep their results (SC-002).
   - Search the tests for a not-strict `timerTimeOfDay` value. Research R-006 found none in the listed files.
   - Search the tests for an assertion on an old error text. The old texts are the template message with `HH:mm` and the sequence message with `HH:mm:ss`.
   - Change each test that pins a not-strict form or an old text. Task T017 does this. T017 runs right after T010 and T014, which break those tests. State the change in the task.

Build and test gate (Constitution I):
1. `dotnet build "C:\src\GameBot\GameBot.sln" -c Debug`.
2. `dotnet test` for the unit, integration, and contract projects.
3. `dotnet format whitespace "C:\src\GameBot\GameBot.sln" --verify-no-changes --include <changed and new C# files>`.

The web UI does not change, so its gate does not apply.

## Complexity Tracking

No violations.
