# Feature Specification: Start the Emulator From a Backend-Only, Session-Less State

**Feature Branch**: `074-sessionless-emulator-start`
**Created**: 2026-07-24
**Status**: Implemented
**Input**: User description: "I need a way to start the emulator when NO session is started. Make sure an emulator can be started where there's nothing but the backend service running. This behaviour has to be integrated into the current commands/sequences that make sure the game is running"

## Overview

Today the automation can heal or start the emulator, but only from **inside** a run that already has (or can resolve) a live session — and a session can only attach to a device that a **running** emulator exposes. That creates a bootstrap gap: from a truly cold state where **only the backend service is running** (no session, no attached device, emulator possibly closed), there is no reliable, supported path for the automation to bring the emulator up. The actions that would start it (`ensure-emulator-running`, `connect-to-game`, `ensure-game-running`) are reached through a command/sequence dispatch path that expects a session to already exist, so an operator returning to a machine where everything but the backend is down cannot get automation moving again without manual intervention.

This feature closes that gap: it makes starting the emulator possible from a backend-only state with no session, and wires that capability into the existing "make sure the game is running" flow so the same commands/sequences that operators already author and schedule can bootstrap the environment from cold.

## Clarifications

### Session 2026-07-24

- Q: Where exactly does the automation break today when only the backend is running, and where must the cold-start be integrated? → A: An execution **queue** binds its device session up front — it creates the ADB-bound session for its emulator serial **before** running any sequence — and that session creation fails when the emulator is closed, so the queue never reaches the sequences that ensure the game is running. The cold-start must therefore run **inside the queue's own startup, before it binds the session**: the queue ensures its emulator instance is up (reusing the feature-070 `ensure-emulator-running` capability) and only then creates the session and runs its sequences. Rationale: this is the actual upstream blocker for the automation operators run (scheduled queues); fixing it at queue startup is what makes "only the backend running → automation self-starts" true, and it is the queue that "makes sure the game is running," so this is the correct integration point.
- Q: How does the queue know which emulator instance to start (it only carries a device serial today)? → A: Add **optional** emulator-instance fields to the queue configuration (an instance name or index), persisted and surfaced through the REST API and web-ui exactly like the existing queue config fields. When set, the queue runs the pre-session emulator ensure using those fields plus its existing emulator serial; when unset, the queue behaves exactly as today (no emulator management). Rationale: mirrors how `connect-to-game` gained optional instance fields in feature 071 and how queue config was extended in features 052/073; no new machinery or global settings.
- Q: How is the warm path kept unchanged and the cold-start kept idempotent (how is "cold" distinguished from "warm")? → A: Reuse the feature-070 emulator health probe unchanged — the emulator is (re)started only when the target instance is not running/responsive; an already-healthy instance is a no-op, so a queue whose emulator is already up creates its session and runs exactly as today. A genuine emulator failure (recovery timeout or instance-not-found) fails the queue with a clear reason before session creation; a neutral unsupported-host outcome lets the queue proceed to create the session exactly as it does today. Rationale: reusing the established health-and-recover semantics avoids new "is it cold?" logic and guarantees backward compatibility.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Bring the emulator up from a cold, session-less machine (Priority: P1)

An operator (or an unattended schedule) finds the machine in a cold state: the backend service is running, but there is no active session, no device attached, and the emulator is closed. They run the automation that "makes sure the game is running" and it starts the emulator from that cold state — without requiring them to first manually open the emulator, attach a device, or start a session by hand.

**Why this priority**: This is the exact capability requested and the reason the feature exists. Without it, unattended recovery is impossible whenever the emulator is down and no session survives — the automation silently cannot start, because the only actions that could start the emulator are unreachable from a session-less state. This is the whole value of the feature.

**Independent Test**: With only the backend service running (no session started, emulator closed), invoke the "ensure the game is running" automation and confirm that afterward the emulator is running and responsive, with no manual pre-steps (no manual emulator launch, no manual session start).

**Acceptance Scenarios**:

1. **Given** only the backend service is running — no session, emulator closed — **When** the ensure-game-running automation is run, **Then** the emulator is started, becomes responsive, and the run reports that it brought the emulator up (rather than failing for lack of a session).
2. **Given** only the backend service is running and the emulator is closed, **When** the queue runs, **Then** it does not fail at session creation ("emulator could not be reached") before it has had the chance to start the emulator — the emulator is brought up first.
3. **Given** the emulator is started successfully from cold, **When** the run continues, **Then** it proceeds through the remaining "ensure the game is running" steps (attach a session, foreground/launch the game) exactly as it would have if the emulator had been up to begin with.

---

### User Story 2 - Integrated into the existing ensure-game-running commands/sequences (Priority: P1)

An automation author does not want a separate, bespoke bootstrap tool. They want the emulator cold-start folded into the commands/sequences they already use to guarantee the game is running, so that the same authored artifact works whether the machine is warm (session alive) or stone cold (backend only). They author it once and it is robust to either starting state.

**Why this priority**: The user explicitly requires the behavior be "integrated into the current commands/sequences that make sure the game is running." A cold-start capability that lived only in a separate, manually-triggered tool would not satisfy the request and would leave the scheduled automation still unable to self-recover. It is co-P1 with US1 because "reachable from the existing flow" is what makes the cold-start actually useful.

**Independent Test**: Take the command/sequence that operators currently use to ensure the game is running, run it from a cold session-less state, and confirm it performs the emulator cold-start as part of its normal execution — no new separate artifact required.

**Acceptance Scenarios**:

1. **Given** the existing command/sequence that ensures the game is running, **When** it runs from a cold session-less state, **Then** the emulator cold-start happens as an integrated part of that run.
2. **Given** the same command/sequence, **When** it runs from a warm state (a healthy session/emulator already present), **Then** it behaves exactly as it does today — no redundant emulator restart, no regression to the warm path.
3. **Given** an author configuring the ensure-game-running flow, **When** they supply the information needed to identify the emulator instance to start, **Then** the cold-start uses it and no additional, unfamiliar authoring surface is required beyond what the existing emulator-aware actions already expose.

---

### Edge Cases

- **Warm start (session already alive)**: When a healthy session/emulator already exists, the flow must not force an unnecessary emulator restart or tear down a working session — the cold-start path is only taken when the environment is actually cold.
- **Host cannot drive the emulator** (non-Windows host, emulator management tool unavailable): the cold-start degrades to a neutral "not-applied" outcome consistent with the existing emulator-aware actions, and does not turn a previously-working flow into a hard failure.
- **Emulator starts but never becomes responsive within the allotted wait**: the run reports a clear, non-hanging failure at the configured maximum wait rather than blocking indefinitely, and does not proceed to attach a session against a dead device.
- **Instance identifier missing or does not match any real instance**: a genuine misconfiguration fails the run with a clear reason (distinct from the neutral unsupported-host outcome); a missing identifier is reported clearly rather than silently doing nothing.
- **Stale/leftover session record but no live device**: the flow treats the environment as cold enough to require the emulator to be brought to a healthy, responsive state before proceeding, rather than trusting a session record that no live device backs.
- **Concurrent runs**: two runs that both try to cold-start the same instance do not corrupt each other's state or leave the emulator half-started; the second observes the first's result rather than launching a duplicate.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a supported path to start the target emulator instance from a state where only the backend service is running — no active session, no attached device — without requiring any manual pre-step (no manual emulator launch and no manual session start).
- **FR-002**: The emulator cold-start MUST run before the queue binds its device session — it MUST NOT require an already-running session or an attached device to execute, so a queue configured for cold-start no longer fails session creation merely because the emulator was closed.
- **FR-003**: The cold-start capability MUST be integrated into the existing queue run that ensures the game is running (the queue is what operators schedule to keep the game running), so enabling it requires no separate, dedicated bootstrap artifact — a queue configured with its emulator instance performs the cold-start automatically at run start.
- **FR-004**: When the environment is already warm (a healthy, responsive emulator and/or a live session already exists), the flow MUST NOT perform an unnecessary emulator restart and MUST NOT disrupt the working session — the cold-start is taken only when needed (idempotent with respect to a healthy environment).
- **FR-005**: After the emulator is started from cold and becomes responsive, the flow MUST continue with the remaining "ensure the game is running" behavior (attaching a session and bringing the game to the foreground) exactly as it does when the emulator was already up.
- **FR-006**: The operator MUST be able to identify which emulator instance the queue starts via OPTIONAL emulator-instance fields (an instance name or index) on the queue configuration, reusing the queue's existing emulator serial; these fields MUST be persisted and surfaced through the REST API and web-ui consistently with the queue's existing configuration fields. When the fields are unset, the queue performs no emulator management.
- **FR-007**: If the emulator cannot be brought to a running, responsive state within a bounded maximum wait, the run MUST report a clear, human-readable failure and MUST NOT block indefinitely, and MUST NOT attempt to attach a session against a device that is not present.
- **FR-008**: On a host or environment that cannot drive the emulator (non-Windows host, or the emulator management tool cannot be located), the cold-start MUST degrade gracefully to a neutral, non-crashing "not-applied" outcome, consistent with the established behavior of the existing emulator-aware actions, and MUST NOT convert a previously-working flow into a failure.
- **FR-009**: When the supplied instance identifier does not correspond to any real emulator instance at runtime, the run MUST fail with a clear reason distinguishable from the neutral unsupported-host outcome (a genuine misconfiguration, not graceful degradation).
- **FR-010**: The run's recorded result MUST make it possible for an operator to see what happened to the emulator (already healthy, started from cold, restarted, failed to recover, or not-applied), so a cold-start recovery is observable after the fact.
- **FR-011**: The feature MUST reuse the existing emulator health-and-recover capability and its existing configuration (responsiveness-probe timeout, maximum boot wait, poll interval) and MUST NOT introduce new emulator-tuning configuration beyond the instance-identification inputs already carried by the emulator-aware actions.
- **FR-012**: Existing warm-path behavior of the ensure-game-running commands/sequences and of the `connect-to-game`, `ensure-game-running`, and `ensure-emulator-running` actions MUST be preserved with zero regressions; runs that already had a live session continue to behave exactly as before.
- **FR-013**: When a queue configured with emulator-instance fields starts, it MUST perform the emulator ensure BEFORE creating its device session; a genuine emulator failure (recovery timeout or instance-not-found) MUST fail the queue run with a clear reason and MUST NOT attempt to create the session, while a neutral unsupported-host outcome MUST let the queue proceed to create the session exactly as it does today.
- **FR-014**: The new emulator-instance queue fields MUST be OPTIONAL for validation and MUST default to "unset" for queues stored before this feature (backward-compatible persistence); a queue with the fields unset MUST behave byte-for-byte as it does today, performing no emulator health-check, start, or restart.

### Key Entities

- **Cold (backend-only) state**: The starting condition this feature targets — the backend service is up, but there is no active session, no attached device, and the emulator may be closed. The environment cannot currently bootstrap itself out of this state.
- **Execution Queue (extended)**: The scheduled queue operators use to keep the game running, bound to an emulator serial and a template of sequences. Extended with OPTIONAL emulator-instance fields (name or index) that, when set, drive a pre-session emulator cold-start at run start. Today the queue creates its device session before running any sequence — the step that fails cold.
- **Emulator Cold-Start Outcome**: The result of the pre-session emulator ensure — already-healthy / started-from-cold / restarted / failed-to-recover / instance-not-found / not-applied — surfaced in the queue run's recorded result/log so recovery is observable.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: From a cold state (backend only, emulator closed, no session), running the ensure-game-running automation results in a running, responsive emulator in 100% of runs on a supported host, with no manual pre-steps performed by the operator.
- **SC-002**: From the same cold state, the queue never fails at session creation before it has attempted to start the emulator — 0% of cold runs abort with an "emulator could not be reached" session-creation failure caused solely by the emulator being down.
- **SC-003**: After a successful cold-start, the run proceeds to a live session with the game in the foreground in 100% of runs on a supported host, matching the end state of a warm run.
- **SC-004**: Running the same queue from a warm state (emulator already up) performs zero unnecessary emulator restarts and disrupts no working session — 100% backward compatible with today's warm-path behavior; a queue with the emulator-instance fields unset performs no emulator operations at all.
- **SC-005**: When the emulator cannot be recovered, the run reports a clear failure within the configured maximum wait in 100% of such runs and never blocks indefinitely, and never attaches a session to an absent device.
- **SC-006**: On a host that cannot drive the emulator, the flow completes without crashing and reports a clear, neutral "not-applied" outcome in 100% of runs — the missing emulator tooling never turns a previously-working flow into a failure.
- **SC-007**: An operator can enable the cold-start by setting the queue's optional emulator-instance field (name or index) through the existing queue configuration surfaces (REST API and web-ui) — no new emulator-tuning settings.
- **SC-008**: No new emulator-tuning configuration is introduced; the feature reuses the existing emulator health/timeout configuration (feature 070 defaults and knobs).

## Assumptions

- The emulator health-and-recover capability delivered in feature 070 (`ensure-emulator-running`) is the mechanism this feature builds on; the new work is invoking it at **queue startup, before the queue binds its device session**, so the queue no longer fails session creation when the emulator is closed.
- "The commands/sequences that make sure the game is running" is realized at runtime by the execution **queue** that schedules those sequences; the queue creating its session up front is exactly what fails today when the emulator is down, so the queue's own startup is the correct integration point — not a new dedicated endpoint or tool.
- "Genuine failure" versus "neutral/unsupported" mirrors the established emulator-action semantics: recovery-timeout and instance-not-found are failures (they fail the queue run before session creation); non-Windows host and unavailable emulator/management tooling are neutral "not-applied" outcomes that let the queue proceed to create the session as today.
- The operator supplies the emulator instance identifier (name or index) on the queue configuration; the queue reuses its existing emulator serial for the device probe. The feature does not auto-discover which instance corresponds to a device serial.
- Default emulator tuning values are inherited unchanged from feature 070 (responsiveness-probe timeout 10 s, maximum boot wait 120 s, poll interval 3 s) and remain overridable via the existing configuration.
- The feature's responsibility ends at bringing the emulator up from cold and handing off to the existing session-create and sequence behavior; it does not redefine what "game running" means.
- The interactive/manual session-start endpoint remains a separate, lighter path; this feature targets the scheduled-queue automation operators rely on for unattended recovery. Extending the standalone command/sequence dispatch path to run session-lessly is possible future work but is out of scope here.
