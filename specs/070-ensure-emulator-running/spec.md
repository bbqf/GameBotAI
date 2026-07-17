# Feature Specification: Ensure Emulator Running Action

**Feature Branch**: `070-ensure-emulator-running`
**Created**: 2026-07-17
**Status**: Implemented
**Input**: User description: "check if the emulator itself is running (and not hanging) and start/restart it if needed" — delivered as a new sequence action type `ensure-emulator-running`, available the same way the existing `ensure-game-running` action is.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Guarantee a healthy emulator before automation runs (Priority: P1)

An automation author who schedules daily/event tasks wants the very first step of a queue or
sequence to make sure the target emulator instance is actually up and responsive before any game
work is attempted. They add an "ensure emulator running" action, tell it which emulator instance
to watch (an instance identifier plus the device serial it exposes), and when the step runs it
verifies the emulator is running and responsive, and — only if it is not — starts it or restarts a
hung instance and waits for it to come back before the step succeeds.

**Why this priority**: This is the core capability requested and the reason the feature exists.
Automations that assume a live emulator silently do nothing (or fail every downstream step) when
the emulator is closed or frozen; a health-and-recover gate at the top of a run is what makes
unattended scheduling reliable. Without it there is nothing to deliver.

**Independent Test**: Author a one-step sequence containing only the "ensure emulator running"
action pointed at a known instance. Run it in three starting states — (a) instance already up and
responsive, (b) instance not running, (c) instance running but frozen/offline — and confirm that
afterward the instance is running and responsive in all three cases, with the step recorded as
succeeded (and, in case (a), with no restart performed).

**Acceptance Scenarios**:

1. **Given** the target emulator instance is already running and responds to a device probe within
   the allotted time, **When** the action executes, **Then** no start or restart is performed and
   the step is recorded as succeeded (idempotent no-op).
2. **Given** the target emulator instance is not running, **When** the action executes, **Then** the
   instance is started, the action waits until the device is present and reports boot-complete, and
   the step is recorded as succeeded.
3. **Given** the target emulator instance is running but unresponsive (device offline or the probe
   does not answer within the allotted time), **When** the action executes, **Then** the instance is
   restarted, the action waits until the device is present and reports boot-complete, and the step is
   recorded as succeeded.
4. **Given** the target instance cannot be brought to a healthy state within the maximum wait, **When**
   the action executes, **Then** the step is recorded as failed with a clear reason and does not hang
   indefinitely.

---

### User Story 2 - Author the action through the same surfaces as ensure-game-running (Priority: P2)

An author using the web authoring UI and an operator using the automation tool interface (MCP)
expect the new action to be selectable, configurable, and validated wherever the existing
"ensure game running" action already is, so there is nothing new to learn and no surface where the
action is missing.

**Why this priority**: The feature is only useful if it can actually be placed into the sequences
and commands that operators already author and schedule. Parity across the authoring/validation/
tooling surfaces is what makes the capability usable, but it depends on the core execution behavior
(US1) existing first.

**Independent Test**: In each surface that offers the "ensure game running" action (authoring UI
action picker, action validation, the automation tool listing), confirm the "ensure emulator
running" action is offered, accepts its instance-identifier and device-serial fields, and
round-trips through save/load without error.

**Acceptance Scenarios**:

1. **Given** the authoring UI action picker that lists selectable action types, **When** the author
   opens it, **Then** "ensure emulator running" appears alongside "ensure game running" and can be
   added to a step with its configuration fields.
2. **Given** a sequence or command that contains an "ensure emulator running" step with valid fields,
   **When** it is validated and saved, **Then** validation passes and the step round-trips through
   save/load unchanged.
3. **Given** the automation tool surface used to drive sequences/commands, **When** the action set is
   enumerated, **Then** "ensure emulator running" is present and can be authored the same way
   "ensure game running" is.

---

### Edge Cases

- **Non-Windows host / device control unavailable / emulator control tool not found**: When the host
  cannot drive the emulator or its management tool cannot be located, the action degrades gracefully —
  it does not throw or crash the run — and reports a neutral "unsupported / not-applied" outcome,
  matching how the existing "ensure game running" action behaves under the same conditions.
- **Already healthy**: When the instance is already running and responsive, the action performs no
  start/restart and succeeds (no unnecessary disruption of a working emulator).
- **Start succeeds but boot never completes within the wait**: The action stops waiting at the
  configured maximum, reports a clear non-succeeding outcome, and does not block the run forever.
- **Restart of a hung instance**: A running-but-frozen instance is treated as unhealthy and is
  restarted rather than left as-is; the action does not report success merely because the process
  exists.
- **Missing/invalid configuration** (no instance identifier, or neither an instance name nor index
  supplied, or a blank device serial): validation rejects the step with a clear message rather than
  accepting a step that cannot possibly run.
- **Nonexistent instance at runtime**: when the supplied instance identifier is well-formed but does
  not match any existing instance, the action fails the step with a clear reason (a genuine
  misconfiguration), distinct from the neutral unsupported-host outcome.
- **Transient probe failure**: A single failed device probe within the allotted window is retried
  (polling) rather than immediately declared a failure, so momentary hiccups do not trigger an
  unnecessary restart.

## Clarifications

### Session 2026-07-17

- Q: What default timeouts should the configurable knobs use? → A: responsiveness-probe timeout **10 s**, maximum post-(re)start boot wait **120 s**, poll interval **3 s** (all overridable).
- Q: How is a running-but-hung instance recovered — restart in place or quit then start? → A: the management tool's single **restart** operation (one command), not a separate quit-then-launch.
- Q: What happens when the author-supplied instance identifier (name/index) does not exist at runtime? → A: **fail the step** with a clear reason; this is a genuine misconfiguration and is NOT treated as the unsupported-host graceful-degradation outcome.
- Q: Does "healthy" include the game/app being in a good state? → A: No. The action guarantees only that the **emulator/device** is running and responsive; in-game/app state stays the responsibility of `connect-to-game` / `ensure-game-running`.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a new action type that verifies whether a specified emulator
  instance is both running and responsive, and brings it to a running-and-responsive state when it is
  not.
- **FR-002**: The action MUST let the author identify the target emulator instance by an explicit
  instance identifier (an instance name or an instance index) and MUST also carry the device serial
  used to probe the instance's responsiveness.
- **FR-003**: The action MUST treat an instance as healthy only when all of the following hold: the
  instance is reported running by the emulator management tool, the device is present and in a usable
  (not offline) state, and the device answers a responsiveness probe within a configured time limit.
- **FR-004**: When the instance is not running, the action MUST start it; when the instance is running
  but not responsive ("hanging"), the action MUST restart it using the management tool's single
  restart operation (not a separate quit-then-start sequence).
- **FR-005**: After starting or restarting, the action MUST wait until the device is present and
  reports boot-complete, polling up to a configured maximum wait, and only then report success.
- **FR-006**: If the instance cannot be brought to a healthy state within the configured maximum wait,
  the action MUST record the step as failed with a clear, human-readable reason and MUST NOT block
  indefinitely.
- **FR-007**: The action MUST be idempotent — when the instance is already running and responsive, it
  MUST perform no start/restart and MUST report success.
- **FR-008**: The action MUST be selectable, configurable, and validated in every authoring/validation/
  tooling surface where the existing "ensure game running" action is available, including the web
  authoring UI action picker, action validation, and the automation (MCP) tool surface, and MUST be
  usable in both sequences and standalone commands.
- **FR-009**: Action validation MUST recognize the new action type as valid, MUST require the fields
  needed to target an instance (an instance identifier and a device serial), and MUST NOT reject a
  sequence solely for containing an otherwise-valid instance of this action.
- **FR-010**: When executed as a sequence step, the action MUST be dispatched through the same
  execution path used for the other device-driving primitive actions and MUST record a step outcome
  (succeeded or failed with reason) in the execution log.
- **FR-011**: On a host or environment that cannot drive the emulator (non-Windows host, device
  control unavailable, or the emulator management tool cannot be located), the action MUST degrade
  gracefully with a neutral, non-crashing outcome consistent with the existing "ensure game running"
  action's behavior under the same conditions.
- **FR-012**: The responsiveness-probe time limit, the maximum post-(re)start boot wait, and the
  polling interval MUST be configurable, with documented defaults (responsiveness-probe timeout
  10 seconds, maximum boot wait 120 seconds, poll interval 3 seconds), so operators can tune them for
  slower or faster hosts without code changes.
- **FR-013**: A sequence step using this action MUST NOT abort the remaining steps when the action
  succeeds; a genuine failure to bring the emulator to health MUST fail the step consistently with the
  other device-driving primitive actions.
- **FR-014**: When the author-supplied instance identifier does not correspond to an existing instance
  at runtime, the action MUST fail the step with a clear reason. This is a genuine misconfiguration and
  MUST NOT be reported as the neutral unsupported/not-applied outcome reserved for hosts that cannot
  drive the emulator (FR-011).
- **FR-015**: The action's health guarantee is limited to the emulator/device (running and responsive);
  it MUST NOT attempt to verify or correct in-game or in-app state, which remains the responsibility of
  the "connect to game" and "ensure game running" actions.

### Key Entities

- **Ensure-Emulator-Running Action**: A named action type representing "make sure this emulator
  instance is up and responsive, starting or restarting it if needed." It carries an instance
  identifier (name or index) and the device serial used to probe responsiveness. It is the
  emulator-lifecycle sibling of the app-lifecycle "ensure game running" action.
- **Emulator Instance**: The externally managed emulator that hosts the game. It has a lifecycle
  (not running / running / running-but-hung) and exposes a device (identified by a serial) that can
  be probed for responsiveness and boot-completion.
- **Health Result / Outcome**: The action's determination for a run — already-healthy (no action
  taken), started, restarted, failed-to-recover, or unsupported/not-applied — surfaced as the step's
  recorded outcome and reason.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An author can add the "ensure emulator running" action to a sequence or command step
  using only the existing authoring surfaces, supplying just an instance identifier and a device
  serial.
- **SC-002**: Running the action against an instance that is closed results in a running, responsive
  emulator in 100% of runs on a supported host, with the step reported as succeeded.
- **SC-003**: Running the action against a running-but-frozen instance results in a restarted,
  responsive emulator in 100% of runs on a supported host, with the step reported as succeeded.
- **SC-004**: Running the action against an already-healthy instance performs no restart in 100% of
  runs and reports success (zero unnecessary disruption of a working emulator).
- **SC-005**: When the instance cannot be recovered, the action reports a failure with a clear reason
  within the configured maximum wait in 100% of runs and never blocks the run indefinitely.
- **SC-006**: The action is present and accepted wherever "ensure game running" is offered — 100%
  parity across the authoring UI, validation, and automation tool surfaces.
- **SC-007**: On an unsupported host or with device control unavailable, the action completes without
  crashing the sequence in 100% of runs and reports a clear, neutral outcome.
- **SC-008**: Existing sequences and actions (including "ensure game running" and "connect to game")
  continue to behave exactly as before — zero regressions introduced by adding the new action.

## Assumptions

- The emulator is externally managed and exposes both a management interface (to query running state
  and to start/restart an instance) and a device endpoint (identified by a serial) that can be probed
  for responsiveness and boot-completion. The action drives the emulator through that management
  interface rather than assuming the emulator is already up.
- "Not hanging / responsive" is interpreted as: the device is present and usable and answers a bounded
  probe for boot-completion within the configured time limit. A running process that fails this probe
  is treated as hung and is restarted.
- "Available same as ensure game running" means the action is exposed through the same set of surfaces
  the "ensure game running" action already uses (authoring UI, validation, automation/MCP tool,
  sequence dispatch, and standalone command execution); it does not require a new dedicated session
  lifecycle endpoint beyond what those surfaces already provide.
- The author supplies the instance identifier and device serial on the action; the feature does not
  attempt to auto-discover which instance corresponds to a given serial.
- Graceful-degradation expectations mirror the established behavior of the "ensure game running"
  action for the same unsupported conditions (non-Windows host, device/tool unavailable).
- Only one instance is targeted per action invocation; watching several instances is done by adding
  several steps.
- Default tuning values are responsiveness-probe timeout 10 seconds, maximum post-(re)start boot wait
  120 seconds, and poll interval 3 seconds; these are starting points chosen for a typical LDPlayer
  cold boot and are overridable per host.
- The action's responsibility ends at emulator/device health; ensuring the correct game is in the
  foreground is a separate concern handled by the existing app-lifecycle actions.
