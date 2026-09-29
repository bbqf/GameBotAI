# Research: One format for timerTimeOfDay

**Feature**: 118-unify-timer-time-format | **Issue**: #226

## R-001: Which format rule

- **Decision**: Both validators accept `HH:mm` and `HH:mm:ss` only (24-hour).
- **Rationale**: The spec clarification fixes this. The sequence validator already accepts `HH:mm:ss`, and its message says so. Stored sequences can hold this form. The template validator accepts fewer strings. Widening it to also accept `HH:mm:ss` is not a breaking change.
- **Alternatives considered**: (a) Reject `HH:mm:ss` in both. This breaks stored sequences. (b) Keep the not-strict `TimeOnly.TryParse` in the sequence validator and widen the template one to match. This copies undocumented forms (`11:00 PM`, `9:30`) into a second API. The rule then depends on the runtime culture rules.

## R-002: Where each validator parses

- **Finding**: The template endpoint parses at lines 61 and 86 of `QueueTemplatesEndpoints.cs`. The sequence side parses once, in `SelfReschedulePayload.TryRead` (line 74).
  - `SequenceStepValidationService` (lines 439-453) reads `HasTimerTimeOfDay` and the parsed payload. It has no own parse.
  - Execution reads the parsed `TimeOnly` (`SequenceExecutionService` line 735).
- **Decision**: Change only these call sites. Task T001 re-checks `SequenceStepValidationService` for a second parse. If T001 finds one, task T001a routes it through the shared parser.
- **Alternatives considered**: A new validation pass in the step validator. Rejected: the payload reader is the single parse point, and a second check would drift again.

## R-003: Where the shared parser lives

- **Decision**: New public static class `TimerTimeOfDayFormat` in `src/GameBot.Domain/Services`.
- **Rationale**: `SelfReschedulePayload` is in the domain and cannot call the service project. The service can call the domain. `RelativeOffsetParser` is `internal` in the service, so it is not a fit for reuse by the domain.
- **Alternatives considered**: Move `RelativeOffsetParser` to the domain and add the time-of-day rule to it. Rejected: it would touch `timerRelativeOffset` (FR-008).

## R-004: Strict parse mechanism and response format

- **Decision**: `TimeOnly.TryParseExact(value, ["HH:mm", "HH:mm:ss"], InvariantCulture, DateTimeStyles.None, ...)`. Response: `Format` writes `HH:mm` when the second part is zero, else `HH:mm:ss`.
- **Rationale**: An exact format with `HH` needs two digits and range 00 to 23. `DateTimeStyles.None` gives no space tolerance. `InvariantCulture` removes the dependence on the host culture. The current template call uses the current culture. Existing clients see no change for a zero-seconds value (spec clarification). A value with sub-second ticks cannot come from the parser, so `Format` need not show them. A test covers a value with ticks (task T004).
- **Alternatives considered**: A regex. Rejected: it needs its own range check. Always write `HH:mm:ss` in the response. Rejected: it changes output for existing clients.
- **Check for the first task**: confirm by a unit test that `TryParseExact` with the two formats rejects ` 15:30`, `15:30 `, `24:00`, `9:30`, and `11:00 PM` on .NET 9.

## R-005: OpenAPI text source

- **Finding**: The `reschedule-self` text is in a constant in `PrimitiveActionSchemaFilter.cs` (line 59). This project does not use Swagger XML comments. It uses a schema filter for API text. The doc comments on the two template contract classes are for code readers.
- **Decision**: Change the constant. First, find the source of the template entry description. If none exists, add the same rule text through the existing schema-filter pattern. A contract test covers it.
- **Alternatives considered**: Enable XML docs for Swagger. Rejected: a wider change than this bug.

## R-006: Impact on existing tests and stored data

- **Finding**: A text search of the tests for `timerTimeOfDay` shows `HH:mm` and `HH:mm:ss` values and one bad value (`5pm`, in `QueueTemplatesScheduleTypeTests.cs`). That value stays rejected. `SelfReschedulePayloadTests` uses `14:30:00`, which stays valid. No test pins a not-strict form.
  - Stored templates hold `TimeOnly` values, so they need no migration.
  - A stored sequence with a not-strict form (none known) would fail the next save. The spec accepts this.
  - The text search did not look for assertions on the old error texts. Task T002 does this search. Task T017 changes each test that T002 finds. T017 runs right after T010 and T014, which break those tests.
- **Decision**: No data migration. Add the tests of the plan.

## R-007: Out of scope

- `timerRelativeOffset` rules and the `nextDay` unknown-field behavior (issue #228) do not change (FR-008).
- The web UI already sends `HH:mm`.
