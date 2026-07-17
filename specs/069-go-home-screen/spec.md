# Feature Specification: Go To Home Screen Action

**Feature Branch**: `069-go-home-screen`  
**Created**: 2026-07-17  
**Status**: Draft  
**Input**: User description: "the game should be switched off - android goes to main screen. this feature should be available same as connect to game." (Clarified: press the Android HOME button so the device returns to its main/home screen, leaving the game running in the background — the game is NOT force-stopped or closed.)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Send the device to the home screen from a sequence (Priority: P1)

An automation author building a sequence wants a step that leaves the game and returns the Android device to its home (main) screen, without closing the game, so a later step or a following run can resume the game exactly where it was. They add a "go to home screen" action to a sequence step, just as they would add a "connect to game" action, and when the sequence runs that step the device returns to the home screen.

**Why this priority**: This is the core capability requested and the reason the feature exists. Without it there is nothing to deliver. It is the direct counterpart to the existing "connect to game" action and completes the pair (enter game / leave game).

**Independent Test**: Author a one-step sequence containing only the "go to home screen" action, run it against a connected device on which the game is in the foreground, and confirm the device shows the home/main screen afterward while the game process is still alive (re-entering the game resumes it rather than restarting it).

**Acceptance Scenarios**:

1. **Given** a running session on a Windows host with a connected device and the game in the foreground, **When** a sequence step with the "go to home screen" action executes, **Then** the device leaves the game and displays the home/main screen, the game remains running in the background, and the step is recorded as succeeded.
2. **Given** the device is already on the home screen, **When** the "go to home screen" action executes, **Then** the device remains on the home screen and the step is recorded as succeeded (the action is idempotent).
3. **Given** a sequence containing a "go to home screen" step followed by other steps, **When** the sequence runs, **Then** the step completes and the sequence continues to the following steps.

---

### User Story 2 - Author the action through the same surfaces as connect-to-game (Priority: P2)

An author using the web authoring UI and an operator using the automation tool interface (MCP) expect the new action to be selectable, configurable, and validated wherever the "connect to game" action already is, so there is nothing new to learn and no surface where the action is missing.

**Why this priority**: The user explicitly required the feature to be available "same as connect to game." Parity across the authoring/validation/tooling surfaces is what makes the capability usable in practice, but it depends on the core execution behavior (US1) existing first.

**Independent Test**: In each surface that offers the "connect to game" action (authoring UI action picker, action validation, the automation tool listing), confirm the "go to home screen" action is offered, accepted as a valid action, and round-trips through save/load without error.

**Acceptance Scenarios**:

1. **Given** the authoring UI action picker that lists selectable action types, **When** the author opens it, **Then** "go to home screen" appears alongside "connect to game" and can be added to a step.
2. **Given** a sequence that contains a "go to home screen" step, **When** it is validated, **Then** validation passes (the action type is recognized and its payload is accepted).
3. **Given** the automation tool surface used to drive sessions/actions, **When** the action set is enumerated, **Then** "go to home screen" is present and can be invoked the same way "connect to game" is.

---

### Edge Cases

- **Non-Windows host / ADB unavailable**: When the host cannot drive the device (e.g., not on a Windows host, or device control is unavailable), the action degrades gracefully — it does not throw or crash the run — and reports a neutral "unsupported/not-applied" outcome, matching how the existing "ensure game running" action behaves in the same conditions.
- **No connected device / device offline**: The action reports a non-succeeding outcome with a clear reason rather than crashing the sequence.
- **Game not in the foreground (some other app or the launcher is showing)**: The action still returns the device to the home screen and reports success; it does not require the game to be in the foreground.
- **Invalid or extra payload fields**: The action requires no target coordinates or image; unexpected payload fields do not cause validation to fail the whole action set in a way that differs from other parameterless actions.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a new action type that, when executed, returns the Android device to its home/main screen by issuing the device HOME action.
- **FR-002**: The action MUST leave the game application running in the background — it MUST NOT force-stop, close, or otherwise terminate the game.
- **FR-003**: The action MUST require no target coordinates, reference image, or game/device identifiers in its own payload beyond the session context already available to the running sequence (i.e., it is a parameterless action from the author's perspective).
- **FR-004**: The action MUST be selectable and configurable in every authoring/validation/tooling surface where the existing "connect to game" action is available, including the web authoring UI action picker, action validation, and the automation (MCP) tool surface.
- **FR-005**: Action validation MUST recognize the new action type as valid and MUST NOT reject a sequence solely for containing it.
- **FR-006**: When executed as a sequence step, the action MUST be dispatched through the same execution path used for the other device-driving primitive actions (tap / swipe / key / connect-to-game / ensure-game-running) and MUST record a step outcome (succeeded or failed with reason) in the execution log.
- **FR-007**: On a host or environment that cannot drive the device (non-Windows host or device control unavailable), the action MUST degrade gracefully with a neutral, non-crashing outcome consistent with the existing "ensure game running" action's behavior in the same conditions.
- **FR-008**: The action MUST be idempotent — executing it when the device is already on the home screen leaves the device on the home screen and reports success.
- **FR-009**: A sequence step using this action MUST NOT abort the remaining steps when the action succeeds; a genuine failure to drive the device MUST fail the step consistently with the other device-driving primitive actions.

### Key Entities

- **Go-To-Home-Screen Action**: A named, parameterless action type representing "return the device to its home/main screen." It carries no author-supplied coordinates, image, or identifiers; its behavior is entirely defined by the action type. It is the leave-game counterpart to the enter-game "connect to game" action.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An author can add the "go to home screen" action to a sequence step using only the existing authoring surfaces, with no additional configuration fields required beyond selecting the action type.
- **SC-002**: Running a sequence whose step is the "go to home screen" action returns the device to the home/main screen in 100% of runs on a connected, supported host, while the game process remains alive (re-entering resumes rather than restarts).
- **SC-003**: The action is present and accepted wherever "connect to game" is offered — 100% parity across the authoring UI, validation, and automation tool surfaces.
- **SC-004**: On an unsupported host or with device control unavailable, the action completes without crashing the sequence in 100% of runs and reports a clear, neutral outcome.
- **SC-005**: Existing sequences and actions (including "connect to game" and "ensure game running") continue to behave exactly as before — zero regressions introduced by adding the new action.

## Assumptions

- "Switched off — Android goes to main screen" is interpreted (per user clarification) as pressing the device HOME button to reach the home/main screen, leaving the game running in the background. It does NOT mean force-stopping or closing the game.
- "Available same as connect to game" means the action is exposed through the same set of surfaces the "connect to game" action already uses (authoring UI, validation, automation/MCP tool, sequence dispatch); it does not necessarily require a new dedicated session lifecycle endpoint beyond what those surfaces already provide.
- The action operates within the device/session context that a running sequence already carries; the author does not supply device identifiers on the action itself.
- Graceful-degradation expectations mirror the established behavior of the "ensure game running" action for the same unsupported conditions.
