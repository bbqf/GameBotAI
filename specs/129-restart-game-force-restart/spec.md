# Feature Specification: Restart the Game From a Sequence

**Feature Branch**: `129-restart-game-force-restart`
**Created**: 2026-10-07
**Status**: Implemented
**Input**: GitHub issue #277 "FR-019: Restart the game from a sequence". Closes #277.

## Clarifications

### Session 2026-10-07

- Q: Does a finished restart count as a successful step? → A: Yes. The step succeeds and reports the reason code `restarted`. Only a failure stage makes the step fail.
- Q: Which session kinds support the restart? → A: The same kinds as today: a queue session (game from the queue link) and a direct session (game from the session game ID). The game and device are resolved as today.
- Q: Can `forceRestart` be set by a parameter binding? → A: No. It is a literal boolean on the action only. A non-boolean value gets a 400 response.
- Q: How long may the game take to reach the foreground after the start? → A: A fixed bounded wait that the plan names. It reuses the existing foreground check. It is not configurable in this feature.
- Q: How does the step-through preview treat the restart? → A: As an effectful action that is previewed and never run, in the same way as other device-changing actions.
- Q: Where can a later condition read `restarted`? → A: For a sequence action step, a condition reads `restarted` directly, and the state `success` also matches it. A command step shows `restarted` only in its own outcome list, and a sequence condition sees `success` for it.
- Q: What happens when a parameter binding tries to set `forceRestart`? → A: The save returns 400 when the binding is visible at save time. If a bound value reaches run time and is not a boolean, the step fails with a clear message and never throws.
- Q: Does a package name with unsafe characters count as a missing package name? → A: Yes. It returns the reason `no_package_name` and no device command runs.
- Q: Where is the unsafe package name check done, and does it change the plain path? → A: In the shared game resolve step of the restart path only. It returns `no_package_name` before any device command. The device layer keeps the same check as a second guard. The plain path (no `forceRestart`) is not changed.
- Q: Does a step `parameterBindings` entry that targets `forceRestart` get rejected? → A: Yes. The save returns 400 with a clear message, because `forceRestart` is a literal boolean only. A test covers it.
- Q: Is the normal-case time limit tested? → A: Yes. A test with small test options checks that a normal restart ends within the foreground wait plus 10 seconds, and a second test checks the worst-case bound.
- Q: Does an old stored sequence step without `forceRestart` still run the plain path? → A: Yes. It reads as false and runs the plain path. A test covers the stored sequence step and the stored command step.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Restart a stuck game from a sequence (Priority: P1)

A sequence author finds that the game is in a bad state that navigation cannot repair. Example: on the Shop screen, the Mystery Shop tab highlights, but the list below stays the list of the previous tab. The game is live (the countdown runs), so it is not frozen. Today, no step can stop and start the game again. The task must give up and wait for the next run. The author adds the "ensure game running" step with the option `forceRestart` set to true at the point where the sequence finds the bad state. The step stops the game, starts it again, and returns when the game is in the foreground. The sequence can then try again.

**Why this priority**: This is the whole purpose of the feature. Without it, an unattended queue cannot recover from a bad game state.

**Independent Test**: Run a sequence that has one "ensure game running" step with `forceRestart` true on a session whose linked game runs. Check that the game was stopped, started again, and is in the foreground when the step ends.

**Acceptance Scenarios**:

1. **Given** a session with a linked game that runs in the foreground, **When** the step runs with `forceRestart` true, **Then** the game is force-stopped, started again, and the step succeeds when the game is in the foreground.
2. **Given** the same session, **When** the step ends, **Then** the step reports the outcome `restarted`.
3. **Given** a session whose linked game does not run, **When** the step runs with `forceRestart` true, **Then** the game is started and the step reports `restarted` after the game is in the foreground.

---

### User Story 2 - Existing sequences do not change (Priority: P1)

An operator has sequences that use the "ensure game running" step without the new option. These sequences must work exactly as before.

**Why this priority**: A regression here breaks all current queues.

**Independent Test**: Run the step with no `forceRestart` and with `forceRestart` false on a running game. Check that the step returns at once and reports the same outcome as before.

**Acceptance Scenarios**:

1. **Given** the game runs in the foreground, **When** the step runs without `forceRestart`, **Then** the step returns at once with the "already running" outcome and the game is not stopped.
2. **Given** the game runs in the foreground, **When** the step runs with `forceRestart` false, **Then** the result is the same as in scenario 1.

---

### User Story 3 - Later steps can tell a restart from "already running" (Priority: P2)

A sequence author wants a later step or condition to react to the restart (for example, to try the failed task again only after a restart).

**Why this priority**: It lets the author build a retry flow. The restart itself works without it.

**Independent Test**: Run the step in both modes. Check that the two outcomes differ and that a later condition can read the outcome.

**Acceptance Scenarios**:

1. **Given** a restart was done, **When** a later condition reads the step outcome, **Then** it sees `restarted`.
2. **Given** the game was already running and no restart was requested, **When** a later condition reads the step outcome, **Then** it sees the "already running" outcome, not `restarted`.

---

### User Story 4 - Clear failure when the restart cannot finish (Priority: P2)

The force-stop or the start can fail, or the game may not reach the foreground. The step must fail with a clear error and must not report `restarted`.

**Why this priority**: A false success hides a stuck device and wastes the next steps.

**Independent Test**: Simulate a failing force-stop, a failing start, and a game that never reaches the foreground. Check that each case fails with a distinct, clear reason.

**Acceptance Scenarios**:

1. **Given** the force-stop fails, **When** the step runs with `forceRestart` true, **Then** the step fails with a clear reason and does not start the game.
2. **Given** the start fails, **When** the step runs, **Then** the step fails with a clear reason.
3. **Given** the game does not reach the foreground within the allowed time, **When** the step runs, **Then** the step fails with a clear reason and does not report `restarted`.

---

### User Story 5 - The option is validated everywhere (Priority: P2)

An author or an API client sends the option in any place that accepts the "ensure game running" action: sequence steps, command steps, the published API description, and the step-through preview. Valid input is accepted. Invalid input (for example, a text value for `forceRestart`) gets a 400 response, not a 500 response.

**Why this priority**: The option must not work in one place and fail in another.

**Independent Test**: Send valid and invalid values to each place and check the response and the API description.

**Acceptance Scenarios**:

1. **Given** a step with `forceRestart` true, **When** the author saves it through the API, **Then** the API accepts it and returns it unchanged on read.
2. **Given** a step with a `forceRestart` value that is not a boolean, **When** the author saves it, **Then** the API returns 400 with a clear message.
3. **Given** the published API description, **When** a client reads the schema of the action, **Then** `forceRestart` is listed as an optional boolean.
4. **Given** a sequence with a restart step, **When** the author uses the step-through preview, **Then** the preview shows that the step restarts the game and does not run it.
5. **Given** a step with a `parameterBindings` entry that targets `forceRestart`, **When** the author saves it, **Then** the API returns 400 with a clear message.

---

### Edge Cases

- The session has no linked game, or the game has no package name, or the package name has unsafe characters (reason `no_package_name`, restart path only): the step fails with the same reason as today, with no stop attempt and no device command.
- A step `parameterBindings` entry targets `forceRestart`: the save returns 400. A non-boolean value at run time fails the step with a message that names `forceRestart`, with zero device calls.
- The platform does not support the device commands: the step fails with the same reason as today.
- The game stops but never starts: the step fails and does not report `restarted`.
- The step runs with `forceRestart` true twice in a row: each run restarts the game.
- A stored step has no `forceRestart` field (all existing data): it is read as false.
- The linked game is the foreground app of another session on the same device: the restart applies to the session device only. The force-stop command uses the device serial of the session and no other serial.
- A saved action that uses the typed variant `PrimitiveEnsureGameRunningAction` keeps `forceRestart` after the polymorphic JSON round trip (save, read back, run).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The "ensure game running" action MUST accept an optional boolean option `forceRestart`. The default is false.
- **FR-002**: When `forceRestart` is true, the system MUST force-stop the package of the game linked to the session, on the device of the session.
- **FR-003**: After the force-stop, the system MUST start the game again and MUST return only when the game is in the foreground.
- **FR-004**: When `forceRestart` is true and the restart finishes, the system MUST report a distinct outcome `restarted`. This outcome MUST differ from the "already running" outcome.
- **FR-005**: A later condition MUST be able to read the `restarted` outcome of a sequence action step. A condition on the state `success` MUST also match a restarted step. For a command step, `restarted` MUST show in the command outcome list only.
- **FR-006**: When `forceRestart` is absent or false, the system MUST behave exactly as before: the same outcomes, the same timing, and no stop of the game.
- **FR-007**: If the force-stop fails, the start fails, or the game does not reach the foreground within the allowed time, the system MUST fail the step with a clear reason. It MUST NOT report `restarted`.
- **FR-008**: The failure reasons for a missing session, missing linked game, missing package name, and unsupported platform MUST stay the same as today. A package name with unsafe characters counts as a missing package name (reason `no_package_name`). The check for the unsafe package name is in the shared game resolve step of the restart path only, so the plain path does not change. The device layer keeps the same check as a second guard. For these cases the system MUST NOT attempt a force-stop and MUST NOT run any device command.
- **FR-009**: The system MUST accept and validate `forceRestart` in every place that accepts the "ensure game running" action: sequence steps, command steps, the typed action variant `PrimitiveEnsureGameRunningAction`, stored data, and the published API description.
- **FR-010**: Invalid input for `forceRestart` MUST return a 400 response with a clear message. It MUST NOT cause a 500 response. A step `parameterBindings` entry that targets `forceRestart` MUST return 400 at save time, because `forceRestart` is a literal boolean only. A non-boolean value that reaches run time (for example from a hand-edited file) MUST fail the step with a message that names `forceRestart`, MUST NOT throw, and MUST NOT run any device command. A test MUST cover each of these cases.
- **FR-011**: The published API description MUST list `forceRestart` as an optional boolean on the action.
- **FR-012**: The step-through preview MUST show a step with `forceRestart` true as an action that restarts the game, and MUST NOT run the restart.
- **FR-013**: Stored sequences and commands that have no `forceRestart` field MUST load and run without change, and MUST read as `forceRestart` false. A test MUST cover a stored sequence step and a stored command step.
- **FR-014**: The system MUST NOT add a new step type, a new API endpoint, a general "stop any app" capability, or a way to run an arbitrary device command.
- **FR-015**: The system MUST NOT change the behavior of the "go to home screen" step or the "ensure emulator running" step. A test MUST show that both steps give the same outcomes as before this feature.
- **FR-016**: The value of `forceRestart` MUST survive the stored polymorphic action JSON round trip, including the typed variant `PrimitiveEnsureGameRunningAction` in `src\GameBot.Domain\Actions\PrimitiveActionVariants.cs`: save the action, read it back, and run it. A test MUST show that a saved `forceRestart` true is still true after the read and causes the restart at run time.
- **FR-017**: The force-stop MUST run on the device serial of the session only. It MUST NOT run on any other device. A test MUST check the serial that the stop command receives.

### Key Entities

- **Ensure-game-running action**: The existing action that checks that the linked game is in the foreground. It gets one new optional boolean, `forceRestart`.
- **Action outcome**: The result of the action that later steps and conditions can read. It gets one new value, `restarted`.
- **Session**: Links a device and a game. The device and the game package for the restart come from the session.

## Assumptions

- The package of the game is the package name stored on the linked game. The device is the device of the session.
- The allowed time for the game to reach the foreground after the start is a fixed bounded wait. The plan names the exact value and reuses the existing foreground check.
- The optional readiness gate applies only to a command step that has a readiness image, as today.
- The plan MUST list the exact file path of every change site and every test file. It MUST NOT use `...` or a similar placeholder in a path. The list includes `src\GameBot.Domain\Actions\PrimitiveActionVariants.cs`, the service setup wiring in `src\GameBot.Service\GameBotServiceSetup.cs` that references the handler and its interfaces, and the test fakes that implement those interfaces (they need updates for the new device command).
- The regression guarantee (FR-006 and SC-003) excludes tests that assert the list of allowed `expectedState` values or its message text. The new outcome `restarted` changes that list by design, so those tests change in this feature.
- The update of the living docs and the changelog MUST name the exact file paths in the tasks.
- Automatic detection of the stale-list state and any change to the PNS Mystery Shop sequence are out of scope. They belong to a later change in the authoring repository.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A sequence with a restart step recovers a game that was in a bad state, with no operator action, in 100% of test runs where the device accepts the stop and start commands.
- **SC-002**: The restart step ends in less than the allowed foreground wait time plus 10 seconds in the normal case, and always ends (success or failure) in a bounded time. Each device command has its own time limit, so the total time stays bounded. A test with small test options MUST check the normal case (the restart ends within the foreground wait plus 10 seconds), and a second test MUST check the worst-case bound.
- **SC-003**: All existing sequences and tests that use the "ensure game running" step without the option pass with no change. Tests that assert the list of allowed `expectedState` values or its message text are excluded, because that list changes by design (see Assumptions).
- **SC-004**: 100% of invalid `forceRestart` values in every accepting place return a 400 response; 0 return a 500 response.
- **SC-005**: A later condition distinguishes `restarted` from "already running" in 100% of test runs.
- **SC-006**: In every failure case (stop fails, start fails, foreground not reached), the step reports a failure with a reason that names the failed stage, and never reports `restarted`.
- **SC-007**: The "go to home screen" step and the "ensure emulator running" step give the same outcomes as before, and the change adds 0 new step types and 0 new API endpoints. A test or a review of the API description shows this.
- **SC-008**: In 100% of test runs, `forceRestart` true survives the stored JSON round trip (save, read back, run), including the typed variant `PrimitiveEnsureGameRunningAction`, and the stop command uses the session device serial only.
