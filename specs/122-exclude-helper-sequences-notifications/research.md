# Research: Exclude Helper Sequences From Success Notifications

## R-001: Where the option lives

- **Decision**: Add `bool ExcludeFromSuccessNotifications` to the domain class `CommandSequence`. The JSON name is `excludeFromSuccessNotifications`. Use `JsonIgnoreCondition.WhenWritingDefault`, so a false value is not written.
- **Rationale**: The spec says the option is a property of the sequence (FR-004). The repository, backup and restore serialize `CommandSequence` as it is. They carry the new member with no change. A file saved before this feature has no member, and the default is false (FR-009, SC-005). Omitting false keeps old files byte-identical.
- **Alternatives considered**: (a) A set of IDs in the queue or notification store. Rejected: it splits the data from the sequence, and backup, restore and delete would need extra code. (b) A name rule such as a "-helper" suffix. Rejected by the clarification: it hides behavior.

## R-002: How the worker applies the option

- **Decision**: In `HandleJobAsync`, read the sequence one time, before the decision. Take the name and the flag from that read. Pass the flag to `Decide`. In the default (success) branch of `Decide`: if a streak is open, close it and return Recovered (unchanged). Otherwise return Success only if the level is Success+Failure and the flag is off.
- **Rationale**: The worker already reads the sequence for the name, so the flag adds no read. The worker reads the flag when it handles the job, so a change applies to the next result with no restart (FR-010). Failure, Cancelled and Recovered paths do not read the flag, so FR-003 holds. The streak state changes exactly as before.
- **Failure of the read**: If the sequence read throws, the worker treats the option as off. It writes the existing log event 12033 (`SequenceReadFailed`) and sends the notification as usual. Change the message text of event 12033 to say that the option is treated as off. The new text is: "The sequence {SequenceId} could not be read for a notification. The ID is used as the name. The option to exclude success notifications is treated as off. Error type: {ErrorType}." Rationale: a possible duplicate success message is safer than a lost failure message. Moving the read before `Decide` changes one thing: the read now also happens for jobs that send nothing. Keep the read inside the existing try block.
- **Alternatives considered**: (a) Put the flag in `QueueNotificationJob` at enqueue time. Rejected: the engine would read the sequence on its hot path, and a toggle would not apply to jobs that already wait. (b) Cache the flag. Rejected: the sequence file read is cheap, and a cache adds a stale-value risk.

## R-003: API read and write rules

- **Decision**: The member is `excludeFromSuccessNotifications` (boolean). Rules:
  - POST: absent gives false.
  - PUT: absent keeps the saved value (FR-005). This differs from `watchdogTimeoutMs`, where an absent PUT member clears the value. Do not copy the watchdog code.
  - PATCH: absent keeps the saved value. Explicit `null` is invalid.
  - A change of the value (PUT or PATCH) increments the sequence version and sets `UpdatedAt`, the same as any other change (FR-012). A request that sets the value to the saved value is not a change for this rule.
  - Any JSON value that is not `true` or `false` returns 400 with the message "excludeFromSuccessNotifications must be true or false." This includes `null`, strings and numbers.
  - GET and list return the member in all three response shapes. The member is always present (`false` when off).
- **Rationale**: PUT and PATCH read the root JSON member with `TryGetProperty`. The typed contract is not enough: a body with only the flag has no steps, so the per-step branch does not run (the same trap that the watchdog PATCH code documents). A strict check before any change gives 400 and no partial save. This follows the project note that a new field must not return 500 on bad input.
- **Where to validate**: Run the check after the version conflict check and before the first mutation, for create, PUT and PATCH. A dry run (feature 091) uses the same check.
- **Alternatives considered**: (a) Use `bool?` in the typed contract only. Rejected: it fails for a flag-only body. (b) Treat `null` as "clear to off". Rejected: it adds a second way to say off. Keep the rule simple.

## R-004: Allow-lists and other sites (memory note on new sequence members)

- **Decision**: The memory note about a new sequence action type does not apply. This feature adds a sequence attribute, not an action type. The relevant sites are the ones in the plan table: domain model, per-step contract, authoring shape, PUT, PATCH, three response shapes, OpenAPI schemas, and the UI DTO. Add one contract test that sends a bad value to create, PUT and PATCH and expects 400.
- **Rationale**: `grep` for `RescheduleSelf` shows no sequence attribute site. The sites for `WatchdogTimeoutMs` show the real list: `CommandSequence`, `SequenceStepContracts`, `SequencesEndpoints`, `SwaggerConfig`, and `SequenceTimeLimitSchemaFilter`. Use a schema description in `SwaggerConfig` for the new member (the project has no XML comments in Swagger).

## R-005: Clone, export and import

- **Decision**: No server clone endpoint exists. A client clone is a GET of the sequence, then a POST with the same data. The create call MUST accept the member (R-003), so the clone keeps the option (FR-009). Backup export and import serialize `CommandSequence`, so the member goes with them. An archive made before this feature has no member and restores as off.
- **Rationale**: Confirmed by a search of `src` for clone, duplicate, import and export. Only queue duplication (`DuplicateQueueRequest`) and backup exist. Queue duplication copies queue entries by sequence ID, so the option stays with the sequence.
- **Tests**: (1) A contract round-trip test: create a sequence with the option on, GET it, POST the GET body as a new sequence, GET the new sequence, and check that the option is true. (2) An integration test does a backup export, a restore and a read. It checks that the flag is true after the round trip.

## R-006: UI design

- **Decision**:
  - `SequenceDto` gets `excludeFromSuccessNotifications?: boolean`. `SequenceCreate` and `SequenceUpdate` get the same optional member.
  - The form state gets `excludeFromSuccessNotifications: boolean` (default false). `loadSequenceIntoForm` sets it from the loaded sequence.
  - The create form and the edit form each show a checkbox "Exclude from success notifications" in the "Basics" section. The help text is: "The queue sends no success message for this sequence. It still sends failure, cancelled and recovered messages."
  - The edit form always sends the flag on save. The create form sends it only when it is on.
  - The list row shows a small badge "No success notifications" next to the name when the flag is on. The badge has a `title` with the same help text.
- **Rationale**: The edit form always has the loaded value, so sending it is safe. This also makes a user toggle explicit. Other changes keep the state because the form carries the loaded value (User Story 3, scenario 3). The server rule for an absent member protects other clients (for example scripts) that do not know the field.
- **Alternatives considered**: A separate toggle in the list row. Rejected: it adds a save path and a conflict with the version check.

## R-007: Documentation

- **Decision**: Update `docs/architecture.md` (domain model for the sequence, notification behavior, API surface) and refresh "Last reviewed". At the end, set Status to Implemented in `spec.md` and in `specs/STATUS.md` (FR-011).
- **Rationale**: Constitution Principle V.

## R-008: Test plan summary

- **Worker unit tests** (extend the existing notification worker tests). US1 tests: success with the flag sends nothing at each queue level ("None", "Failure", "Success+Failure"); success without the flag sends one message at level "Success+Failure"; a flag toggle between two jobs applies to the second job with no restart; a sequence read that fails treats the option as off, logs event 12033 and still sends the success message. SC-004 is an outcome of these US1 tests. It is not separate work. US2 tests (on top of the US1 worker change): failure with the flag sends; 10 failures then a success send exactly "failure" and "recovered"; recovered with the flag sends; success with the flag and no streak sends nothing; "cancelled" is sent with the flag for an operator cancel and for a queue-stop cancel; a flag turned on while a streak is open keeps the streak.
- **Contract tests**: POST, PUT and PATCH accept true and false; PUT without the member keeps the value; bad values return 400; GET and list show the member; OpenAPI describes the member; a change of the option changes `version` and `updatedAt` (FR-012); the clone round trip (R-005).
- **Integration tests**: file round trip after a repository reload; backup round trip; old file with no member reads as false.
- **UI tests**: the checkbox shows the saved state; save sends the flag; the list shows the badge "No success notifications" only for flagged sequences.
- **Manual check**: The quickstart has a timed step for SC-001.
