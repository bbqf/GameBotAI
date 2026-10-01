# Tasks: Exclude Helper Sequences From Success Notifications

**Input**: Design documents in `specs/122-exclude-helper-sequences-notifications/` (spec.md, plan.md, research.md, data-model.md, contracts/sequence-exclude-flag.md, quickstart.md)
**Prerequisites**: plan.md, spec.md

**Tests**: Tests are in scope. The spec and research R-008 name them.

**Format**: `- [ ] T### [P?] [Story?] Description with file path`. The [P] mark shows only on tasks that write different files and that have no dependency on an incomplete task.

## Phase 1: Setup

- [ ] T001 Read `src/GameBot.Service/Services/Notifications/QueueNotificationWorker.cs`, `src/GameBot.Service/Endpoints/SequencesEndpoints.cs` and `tests/unit/Notifications/QueueNotificationWorkerTests.cs`. Note where the worker reads the sequence, where `Decide` runs, and how the tests build a worker. No file change.

---

## Phase 2: Foundational (blocks all user stories)

- [ ] T002 Add `bool ExcludeFromSuccessNotifications` (default false, JSON name `excludeFromSuccessNotifications`, `JsonIgnoreCondition.WhenWritingDefault`) to `src/GameBot.Domain/Commands/CommandSequence.cs`.
- [ ] T003 Add the optional member `excludeFromSuccessNotifications` to the request and response contracts in `src/GameBot.Service/Models/SequenceStepContracts.cs`. Use a nullable type in the request so that an omitted member is visible. Use a plain boolean in the three response shapes.

**Checkpoint**: The domain and contract types compile. A file with no member reads as off.

---

## Phase 3: User Story 1 - Mark a sequence as a helper that sends no success message (Priority: P1) MVP

**Goal**: A queue sends no "success" message for a sequence with the option on. The operator sets the option through the API.

**Independent Test**: A queue at level "Success+Failure" has one excluded and one normal sequence. Both succeed. Exactly one "success" message arrives, for the normal sequence.

### Tests for User Story 1

- [ ] T004 [US1] Add worker tests in `tests/unit/Notifications/QueueNotificationWorkerTests.cs`: (a) success with the option on sends nothing at each queue level "None", "Failure" and "Success+Failure"; (b) success with the option off sends one message at level "Success+Failure"; (c) a toggle between two jobs applies to the second job with no restart; (d) a failed sequence read treats the option as off, logs event 12033 and still sends the success message. These tests also show SC-004 (zero success messages from an excluded sequence). SC-004 needs no separate task.
- [ ] T005 [P] [US1] Add contract tests in `tests/contract/Sequences/ExcludeFromSuccessNotificationsContractTests.cs`: POST, PUT and PATCH accept true and false; POST with no member gives false; PUT and PATCH with no member keep the saved value; null, a string and a number return 400 (not 500) on POST, PUT and PATCH; GET and list show the member; a change of the option changes `version` and `updatedAt` (FR-012); the clone round trip (GET, then POST with the same data, then GET shows true).
- [ ] T006 [P] [US1] Add an OpenAPI test in `tests/contract/Sequences/ExcludeFromSuccessNotificationsOpenApiTests.cs`. It checks that the schema has the member with a description.
- [ ] T007 [P] [US1] Add integration tests in `tests/integration/Sequences/ExcludeFromSuccessNotificationsPersistenceTests.cs`: file round trip after a repository reload; backup export, restore and read gives true; an old file with no member reads as false (SC-005).

### Implementation for User Story 1

- [ ] T008 [US1] In `src/GameBot.Service/Services/Notifications/QueueNotificationWorker.cs`, change `HandleJobAsync` to read the sequence one time before `Decide`, inside the existing try block. Take the name and the option from that read. Pass the option to `Decide`. In the success branch of `Decide`: an open streak still gives "recovered". With no streak, "success" is sent only at level "Success+Failure" with the option off. If the read throws, treat the option as off. Also change the text of log event 12033 to: "The sequence {SequenceId} could not be read for a notification. The ID is used as the name. The option to exclude success notifications is treated as off. Error type: {ErrorType}."
- [ ] T009 [US1] In `src/GameBot.Service/Endpoints/SequencesEndpoints.cs`, read the member with `TryGetProperty` on the root JSON for POST, PUT and PATCH. Accept only JSON true and false. Return 400 with "excludeFromSuccessNotifications must be true or false." for null, strings and numbers. Run the check after the version conflict check and before the first change. POST with no member gives false. PUT and PATCH with no member keep the saved value. A change of the value increments `version` and sets `UpdatedAt`. Return the member in GET, list and the other response shapes.
- [ ] T010 [P] [US1] Add the OpenAPI description of the member in `src/GameBot.Service/Swagger/SwaggerConfig.cs`.
- [ ] T011 [P] [US1] Add the type `excludeFromSuccessNotifications?: boolean` to `SequenceDto`, `SequenceCreate` and `SequenceUpdate` in `src/web-ui/src/services/sequences.ts`.

**Checkpoint**: T004 to T007 pass. User Story 1 works through the API.

---

## Phase 4: User Story 2 - Failures of a helper still reach the operator (Priority: P1)

**Goal**: The option hides only the plain "success" message. "failure", "cancelled" and "recovered" are still sent.

**Independent Test**: Make an excluded sequence fail, then succeed. The operator gets "failure" and then "recovered", and no "success" message.

US2 is tested on top of the worker change of US1 (T008). It has its own tests and its own acceptance scenarios. No new production code is expected. If a test fails, fix `QueueNotificationWorker.cs`.

- [ ] T012 [US2] Add worker tests in `tests/unit/Notifications/QueueNotificationWorkerTests.cs` (the file that T004 also changes, so no [P]): (a) failure with the option on sends "failure"; (b) 10 failures and then a success send exactly "failure" and "recovered" (SC-003); (c) "recovered" is sent with the option on; (d) success with the option on and no open streak sends nothing; (e) "cancelled" is sent with the option on for an operator cancel; (f) "cancelled" is sent with the option on for a queue-stop cancel; (g) the option turned on while a streak is open keeps the streak, and the next success sends "recovered".

**Checkpoint**: T012 passes. FR-003 holds.

---

## Phase 5: User Story 3 - See and set the option in the sequence UI (Priority: P2)

**Goal**: The operator sets the option in the editor and sees a marker in the list.

**Independent Test**: Open the sequence list. A flagged sequence shows the marker "No success notifications". Open the sequence, turn the option off and save. The marker disappears.

### Tests for User Story 3

- [ ] T013 [US3] Add UI tests in `src/web-ui/src/pages/__tests__/SequencesPage.excludeNotifications.spec.tsx`: the checkbox shows the saved state; save sends the flag; a save of another change keeps the flag; the list shows the badge "No success notifications" only for flagged sequences; the help text says that failures and recoveries are still sent.

### Implementation for User Story 3

- [ ] T014 [US3] In `src/web-ui/src/pages/SequencesPage.tsx`, add `excludeFromSuccessNotifications` (default false) to the form state. Set it in `loadSequenceIntoForm`. Add the checkbox "Exclude from success notifications" with the help text from research R-006 in "Basics" of the create form and the edit form. The edit form always sends the flag on save. The create form sends it only when it is on. Show the badge "No success notifications" next to the name in the list when the flag is on, with a `title` that holds the help text.

**Checkpoint**: T013 passes. `vite build` and `jest` are green.

---

## Phase 6: Polish and Cross-Cutting Concerns

- [ ] T015 [P] Update `docs/architecture.md` (sequence domain model, notification behavior, API surface) and refresh the "Last reviewed" date. Use STE.
- [ ] T016 Run the manual SC-001 check in step 2a of `specs/122-exclude-helper-sequences-notifications/quickstart.md` (timed, under 1 minute). Run the other quickstart steps. Write any failure cause in the PR.
- [ ] T017 Run `dotnet test` for the notification and sequence tests, then `npm run build` and `npx jest` in `src/web-ui`. Fix failures.
- [ ] T018 Set the Status line of `specs/122-exclude-helper-sequences-notifications/spec.md` to "Implemented". Change the entry for this feature in `specs/STATUS.md` to "Implemented" (FR-011). This is the last work item.

---

## Dependencies and Execution Order

- Phase 1, then Phase 2, then Phase 3 (US1). T002 and T003 block all later tasks.
- US1: T008 and T009 write different files, but the tests T004 to T007 need both. T010 and T011 do not depend on each other.
- US2 (T012) depends on T008 and shares a file with T004, so run it after T004.
- US3 (T013, T014) depends on T011. T013 runs before T014 as a failing test.
- Polish: T015 is independent. T016 and T017 need all stories. T018 is last.

## Parallel Example (US1)

```text
After T002 and T003: T005, T006 and T007 (three different files).
After T009: T010 and T011 (two different files).
```

## Implementation Strategy

- **MVP**: Phases 1 to 3 (US1). The operator can set the option through the API, and the worker hides the success message.
- Then add US2 tests (they prove the safety rule), then US3 (UI), then Polish.
- Stop and check at each checkpoint.
