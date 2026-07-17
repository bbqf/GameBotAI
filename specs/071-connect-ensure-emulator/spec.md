# Feature Specification: Connect-to-Game Optionally Ensures the Emulator Is Running

**Feature Branch**: `071-connect-ensure-emulator`
**Created**: 2026-07-17
**Status**: Draft
**Input**: User request: make the connect-to-game action bring up the emulator automatically if it's not running — "will the connect action launch the emulator if it's not running automatically? … do it."

## Clarifications

### Session 2026-07-17

- Q: How does connect-to-game know which emulator instance to bring up? → A: The author supplies an **optional** instance identifier (instance name or index) on the connect-to-game action; without it, no emulator management happens.
- Q: If the emulator can't be brought up, should connect still try to attach a session? → A: No. A **genuine** emulator failure (recovery timed out, or the instance does not exist) fails the connect step immediately, before any session start.
- Q: What about hosts that can't drive the emulator at all (non-Windows, or the emulator tool is unavailable)? → A: Connect **proceeds** as it does today (neutral/unsupported emulator outcome does not block the connect); this preserves current graceful-degradation behavior.
- Q: Any new configuration for this? → A: No. It reuses the existing emulator health/timeout configuration; no new settings or identifiers beyond the optional instance fields on the action.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - One step that guarantees a live emulator, then connects (Priority: P1)

An automation author wants a single "connect to game" step that, when the emulator might be closed
or frozen, first brings the emulator up (or restarts a hung one) and only then attaches the session
and launches the game — so an unattended run doesn't fail merely because the emulator wasn't running.
They add the optional emulator instance identifier to their existing connect-to-game step; from then
on, running that step heals the emulator first when needed.

**Why this priority**: This is the exact capability requested and the reason the feature exists.
Today connect-to-game assumes a live device and fails outright when the emulator is down; folding an
opt-in pre-heal into the one step people already use is the smallest change that removes that failure
mode.

**Independent Test**: Author a connect-to-game step carrying an emulator instance identifier plus the
device serial. Run it in three starting states — emulator already up, emulator closed, emulator
running-but-frozen — and confirm the session attaches and the game launches in all three, with the
emulator ending up running and responsive.

**Acceptance Scenarios**:

1. **Given** a connect-to-game step with an instance identifier and the emulator already running and
   responsive, **When** the step runs, **Then** no emulator start/restart happens and the step
   attaches the session and launches the game exactly as before.
2. **Given** a connect-to-game step with an instance identifier and the emulator closed, **When** the
   step runs, **Then** the emulator is started and, once responsive, the session attaches and the game
   launches, and the step succeeds.
3. **Given** a connect-to-game step with an instance identifier and the emulator running but frozen,
   **When** the step runs, **Then** the emulator is restarted and, once responsive, the session
   attaches and the game launches, and the step succeeds.
4. **Given** a connect-to-game step with an instance identifier and the emulator cannot be brought to
   a healthy state (never boots in time, or the identifier matches no instance), **When** the step
   runs, **Then** the step fails with a clear reason and the session is NOT started.

---

### User Story 2 - Existing connect-to-game steps keep working unchanged (Priority: P1)

An author with existing connect-to-game steps (no emulator instance identifier) expects them to
behave exactly as before — attach a session to an already-running device and launch the game — with
no new required fields and no emulator management.

**Why this priority**: The change must be strictly additive and backward compatible; any regression to
the many existing connect-to-game steps would be unacceptable. It is co-P1 with US1 because "does not
break what exists" is as important as "adds the new behavior."

**Independent Test**: Run an existing connect-to-game step that carries only game and device
identifiers (no instance identifier) and confirm the run performs no emulator query or start and
behaves byte-for-byte as before.

**Acceptance Scenarios**:

1. **Given** a connect-to-game step with no emulator instance identifier, **When** it runs, **Then**
   no emulator health-check, start, or restart is attempted and the connect behaves exactly as today.
2. **Given** a connect-to-game step being authored, **When** the author omits the emulator instance
   fields, **Then** the step still validates and saves (the emulator fields are optional).

---

### Edge Cases

- **Host cannot drive the emulator** (non-Windows, or the emulator management tool is unavailable):
  the pre-heal degrades to a neutral "not-applied" outcome and connect proceeds as it does today —
  the missing emulator tooling does not turn a previously-working connect into a failure.
- **Instance identifier present but device serial blank**: rejected by validation the same way a
  connect-to-game without a serial is rejected today (serial is still required).
- **Both instance name and index supplied**: accepted; the name takes precedence (consistent with the
  standalone emulator action).
- **Emulator becomes healthy but the game still can't be brought up**: unchanged from today — the game
  launch remains best-effort and does not, by itself, fail the connect step.
- **Instance index is negative**: rejected by validation (an index, when supplied, must be ≥ 0).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The connect-to-game action MUST accept an OPTIONAL emulator instance identifier (an
  instance name or an instance index) in addition to its existing game and device-serial inputs.
- **FR-002**: When an instance identifier is present, the connect-to-game action MUST verify the target
  emulator instance is running and responsive and start or restart it if it is not — reusing the
  existing emulator health-and-recover capability — BEFORE attaching the session.
- **FR-003**: When the emulator pre-heal ends in a genuine failure (the instance cannot be brought to a
  healthy state within the allotted wait, or the identifier matches no instance), the connect step MUST
  fail with a clear reason and MUST NOT attempt to start the session.
- **FR-004**: When the emulator pre-heal succeeds (already healthy, started, or restarted) OR yields a
  neutral "unsupported/not-applied" outcome (host cannot drive the emulator), the connect step MUST
  proceed to attach the session and launch the game exactly as it does today.
- **FR-005**: When NO instance identifier is supplied, the connect-to-game action MUST NOT perform any
  emulator health-check, start, or restart, and MUST behave identically to the current implementation
  (zero regression).
- **FR-006**: The emulator instance fields MUST be OPTIONAL for validation; the action MUST still
  require its existing game and device-serial inputs, and MUST reject a negative instance index when
  one is supplied.
- **FR-007**: The connect step's recorded result MUST reflect the emulator pre-heal outcome (e.g., that
  the emulator was already healthy, started, restarted, or that the pre-heal was not applied) so an
  operator can see what happened.
- **FR-008**: The feature MUST reuse the existing emulator health-and-recover capability and its
  existing configuration (timeouts, tool discovery); it MUST NOT introduce new emulator machinery or
  new configuration settings beyond the optional instance fields on the action.
- **FR-009**: The optional instance fields MUST be carried through the same mechanism that already
  carries the connect-to-game action's game id and device serial (the action's parameters), so an
  author who supplies them in the action gets the pre-heal, and enumerated wherever the connect-to-game
  action payload fields are already described.

### Key Entities

- **Connect-to-Game Action (extended)**: The existing enter-game action, now optionally carrying an
  emulator instance identifier (name or index) alongside its game id and device serial. The instance
  identifier is what enables the opt-in emulator pre-heal.
- **Emulator Pre-heal Outcome**: The result of the optional emulator health-and-recover step —
  already-healthy / started / restarted / failed-to-recover / instance-not-found / not-applied — that
  gates whether the connect proceeds and is surfaced in the step's recorded result.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A connect-to-game step carrying an emulator instance identifier results in a running,
  responsive emulator and an attached session + launched game in 100% of runs on a supported host,
  across all three starting states (already up / closed / frozen).
- **SC-002**: When the emulator cannot be recovered, the connect step fails with a clear reason and no
  session is started, in 100% of such runs.
- **SC-003**: Existing connect-to-game steps that carry no instance identifier perform zero emulator
  operations and behave identically to before — 100% backward compatible, zero regressions.
- **SC-004**: On a host that cannot drive the emulator, a connect-to-game step with an instance
  identifier still attaches the session (neutral pre-heal) in 100% of runs — the feature never turns a
  previously-working connect into a failure due to missing emulator tooling.
- **SC-005**: No new configuration settings are introduced; the feature reuses the existing emulator
  health/timeout configuration.

## Assumptions

- The emulator health-and-recover capability delivered in feature 070 (`ensure-emulator-running`) is
  the mechanism reused here; this feature only adds an opt-in call to it from connect-to-game and the
  optional instance fields that drive it.
- "Genuine failure" versus "neutral/unsupported" mirrors the established emulator-action semantics:
  recovery-timeout and instance-not-found are failures; non-Windows host and unavailable emulator/ADB
  tooling are neutral "not-applied" outcomes that do not block the connect.
- The author supplies the instance identifier on the action; the feature does not auto-discover which
  instance corresponds to a device serial.
- This complements features 070 (`ensure-emulator-running`) and 021 (`connect-to-game`); it supersedes
  neither. Authors can still use a standalone `ensure-emulator-running` step instead of the inline
  pre-heal if they prefer to separate the concerns.
- **Scope**: this feature targets the connect-to-game **action as dispatched in a sequence/queue run**
  (the unattended automation path). The separate interactive session-start endpoint (manual "connect"
  from the UI, and the MCP `start_session` tool) is a different, lighter path that only attaches a
  session; it is intentionally **out of scope** and unchanged. Operators wanting the same behavior
  there can precede a manual start with a standalone `ensure-emulator-running` step. Because the
  connect-to-game sequence action is authored as JSON parameters (no dedicated web-ui form), the
  optional instance fields require no new web-ui form — they ride the existing parameters dictionary.
