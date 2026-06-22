# Feature Specification: Ensure Game Running Primitive Action

**Feature Branch**: `052-ensure-game-running`
**Created**: 2026-06-03
**Status**: Implemented

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Check Game Running Status (Priority: P1)

As a queue operator, I want a primitive action that checks whether the target game is already running on the emulator, so I can verify the execution environment is in the expected state before proceeding with game automation.

**Why this priority**: This is the core capability of the feature. Without the ability to detect and report game running status, no other story is possible.

**Independent Test**: Can be fully tested by configuring a queue with a linked game, adding the "ensure game running" action to a sequence, running the sequence on an emulator where the game is already open, and confirming the execution log shows success.

**Acceptance Scenarios**:

1. **Given** a queue linked to a game, **When** the "ensure game running" action executes and the game is already in the foreground on the emulator, **Then** the action completes with success status and the execution log records that the game was already running.
2. **Given** a queue linked to a game, **When** the "ensure game running" action executes and the game is not visible on the emulator, **Then** the action starts the game, completes with failure status, and the execution log records that the game was not running and was started.
3. **Given** a sequence containing an "ensure game running" action, **When** the action fails (game was not running), **Then** subsequent steps in the sequence still execute (the action does not abort the sequence).

---

### User Story 2 - Link Queue to Game (Priority: P1)

As a queue operator, I want to associate a queue with a specific game, so that game-aware actions like "ensure game running" know which game to target without hardcoding it into each action step.

**Why this priority**: The game link is a prerequisite for the "ensure game running" action to know which game to check. Both stories must be implemented together for the feature to be usable.

**Independent Test**: Can be tested by editing a queue in the UI and selecting a game from a list, then verifying the queue details show the linked game name.

**Acceptance Scenarios**:

1. **Given** an existing queue, **When** the operator edits the queue, **Then** they can select a game from the list of configured games to link it.
2. **Given** an existing queue with a linked game, **When** the operator edits the queue, **Then** they can change or remove the linked game.
3. **Given** a queue with no linked game, **When** the "ensure game running" action is added to a sequence for that queue, **Then** the system warns or errors indicating the queue has no game linked.

---

### User Story 3 - Configure Game Package Name (Priority: P2)

As a queue operator, I want each game definition to store its unique package identifier, so the system can launch the correct game on the emulator.

**Why this priority**: Without the package identifier stored on the game, the system cannot launch the game on the emulator. This is a prerequisite for the start-game recovery behavior.

**Independent Test**: Can be tested by creating or editing a game in the UI, entering a package name, and verifying it is saved and displayed correctly.

**Acceptance Scenarios**:

1. **Given** the game configuration UI, **When** creating a new game, **Then** the operator can enter a package name for that game.
2. **Given** an existing game, **When** editing it, **Then** the operator can view and update the package name.
3. **Given** a game with a package name configured, **When** the "ensure game running" action executes on an emulator and the game is not running, **Then** the system uses that package name to launch the game.

---

### Edge Cases

- What happens when the action executes but no game is linked to the queue? The action should fail immediately with a clear reason indicating the queue has no associated game.
- What happens when the action is executed directly (outside a queue context, e.g., a standalone command run)? The action fails immediately with a clear reason indicating no game context is available — it requires a queue with a linked game to operate.
- What happens if the game's package name is missing or empty? The action should fail immediately with a clear reason before attempting any emulator interaction.
- What happens when the emulator is not reachable during the check? The action should fail with an appropriate reason code distinct from "game not running."
- What happens if the game starts but does not become visible within a reasonable time? The action already reports failure (game was not running), so partial start behavior is consistent — the failure status is already set.
- What happens if the linked game is deleted while a queue still references it via `LinkedGameId`? The game delete operation must be blocked (rejected with a conflict error) if any queue holds a reference to that game, preventing dangling references.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a new primitive action type named "ensure game running" that can be added to any sequence.
- **FR-002**: When executed, the "ensure game running" action MUST check whether the target game is the **active foreground app** (frontmost and interactable) on the bound emulator. A game running in the background does not satisfy this condition.
- **FR-003**: If the game is the active foreground app, the action MUST complete with **success** status.
- **FR-004**: If the game is NOT running, the action MUST attempt to start the game on the emulator AND complete with **failure** status.
- **FR-005**: The "ensure game running" action MUST NOT attempt to restart a game that is already running; it must leave a running game undisturbed.
- **FR-006**: Each queue MUST support an optional link to a game (0..1 relationship), similar to how queues link to templates.
- **FR-007**: Each game definition MUST store a package name field used to identify and launch the game on the emulator.
- **FR-008**: The "ensure game running" action MUST resolve the target game and package name from the queue it is executed within. The action itself has no configurable parameters — all context comes from the queue.
- **FR-008a**: If the action is executed outside a queue context (e.g., a command run directly without a queue), the action MUST fail immediately with a clear reason indicating that no game context is available.
- **FR-009**: If the action runs within a queue but that queue has no linked game, the action MUST fail immediately with a reason indicating the missing game link.
- **FR-010**: If the linked game has no package name, the action MUST fail immediately with a reason indicating the missing package name.
- **FR-011**: The execution log for the action MUST record the outcome — specifically whether the game was already running (success) or was not running (failure). The failure reason is "game was not running" regardless of whether the subsequent start attempt succeeded or failed; the start attempt is a best-effort recovery step, not part of the action's reported status.
- **FR-012**: The failure status when the game was not running MUST NOT cause the containing sequence to abort; execution of subsequent steps continues normally.

### Key Entities

- **Game**: Represents a game that can be run on an emulator. Extended with a **package name** — the unique identifier used to launch the game on Android-based emulators.
- **Queue**: Extended with an optional **linked game** reference (0..1). When a game is linked, game-aware actions in that queue's sequences can resolve game details automatically.
- **Ensure Game Running Action**: A primitive action that checks if the linked game is running on the emulator, starts it if not, and reports success only when the game was already running.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The "ensure game running" action can be added to a sequence and executes end-to-end without errors when a queue has a correctly configured linked game.
- **SC-002**: The execution log correctly reflects the outcome: success when the game was already running, failure when it was not, with no ambiguity between the two cases.
- **SC-003**: Operators can configure a game's package name and link a queue to a game in under 2 minutes using the existing UI patterns. Validated by manual UX review at demo — no automated timing test required.
- **SC-004**: The queue-to-game link is persistent across service restarts, consistent with how queue-to-template links behave.
- **SC-005**: An "ensure game running" action failure does not interrupt or abort the remaining steps in a sequence.

## Clarifications

### Session 2026-06-03

- Q: What constitutes "game running" for success purposes — active foreground app only, or any running process? → A: Only the active foreground app (frontmost and interactable) counts; a backgrounded game process does not satisfy the condition.
- Q: Should the execution log distinguish between "not running, started successfully" vs "not running, start failed"? → A: Single failure reason — "game was not running" regardless of start outcome; start attempt is best-effort recovery only.
- Q: Does the action need configurable parameters, and how does it behave when run outside a queue? → A: Zero-config — no action parameters. When executed outside a queue context (e.g., direct command run), the action fails immediately with a clear reason indicating no game context is available.

## Assumptions

- The emulator connection (ADB serial) is managed by the queue, not by this action. The action reuses the queue's existing emulator binding.
- "Game running" means the game is the **active foreground app** — backgrounded game processes do not count. This is determined by checking the active foreground application on the emulator against the game's package name — the specific detection mechanism is an implementation detail.
- Starting the game uses the package name via the standard Android launch mechanism — implementation detail.
- The package name format follows standard Android conventions (e.g., `com.example.game`) but format validation is not a spec requirement.
- The queue-to-game link is optional; queues without a linked game can still function normally for non-game-aware actions.
- The action reports failure when the game was not running regardless of whether the start attempt succeeds — the semantic is "was the game already running as expected?"
