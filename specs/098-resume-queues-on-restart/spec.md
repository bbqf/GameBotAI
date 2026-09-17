# Feature Specification: Resume Queues After a Service Restart

**Feature Branch**: `098-resume-queues-on-restart`  
**Created**: 2026-09-17  
**Status**: Draft  
**Input**: GitHub issue #203 (https://github.com/bbqf/GameBotAI/issues/203) — "FR-007: restart queues that were Running when the service stopped". Closes #203

## Context

Production queues are built to never end: every task reschedules itself and the queue idles between firings. The only thing that ends such a queue today is the service itself. After a service restart, an upgrade or a host reboot, every queue reads `Stopped` and stays that way until an operator notices and starts it again. The operator has already lost 44 hours once to a bot that reported nothing while doing nothing. The current workaround is manual: start every production queue after any service restart. An external watchdog that calls start is explicitly ruled out.

## Clarifications

### Session 2026-09-17

- Q: Is the opt-in per queue or a single service-wide setting? → A: Per queue (boolean on the queue, default off). Rationale: lets production queues resume while experimental queues on the same service stay down; the issue allows either.
- Q: If a resume attempt fails (device in use, run-level failure), does the service retry? → A: No — one attempt per queue per service start, logged. Rationale: a failed run is already visible in the execution log, and a retry loop is an unrequested watchdog.
- Q: Are opted-in queues resumed one after another with a delay, or all at once? → A: All at once, immediately after the service has finished starting, no stagger. Rationale: queues are bound to distinct devices, and a manual start has no stagger either.
- Q: Does the operator see that a run was started by a resume rather than manually? → A: Service log only; the run itself looks like any other start. Rationale: the issue asks only for the queue to come back; a new UI indicator is unrequested scope.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - An opted-in queue comes back after a restart (Priority: P1)

An operator marks a production queue "resume after service restart" and starts it. The service is later restarted (planned restart, upgrade, or host reboot). When the service comes back, the queue is started again automatically, with no operator action, exactly as if the operator had pressed start.

**Why this priority**: This is the whole value of the issue — closing the gap between "self-scheduling" and "never ends".

**Independent Test**: Enable the option on a queue, start it, restart the service, and observe that the queue reports Running again and its at-queue-start entries fire.

**Acceptance Scenarios**:

1. **Given** a queue with the resume option on that is Running, **When** the service shuts down gracefully and starts again, **Then** the queue is Running again shortly after startup without any operator action.
2. **Given** the same queue, **When** the service process is killed or the host loses power (no graceful shutdown) and the service starts again, **Then** the queue is Running again.
3. **Given** a resumed queue, **When** it starts, **Then** its at-queue-start and timer entries behave exactly as on a manual start, and entries booked at runtime by the previous run (self-reschedules, live schedules) are not carried over.

---

### User Story 2 - Queues that should stay stopped stay stopped (Priority: P1)

An operator who stopped a queue on purpose, or who never opted a queue in, must not have it come back to life after a restart.

**Why this priority**: Resuming something the operator deliberately halted would drive an emulator nobody expects to be driven — as harmful as the gap being fixed.

**Independent Test**: With the option on, start and then manually stop a queue; restart the service; the queue stays Stopped. Separately, run a queue with the option off through a restart; it stays Stopped.

**Acceptance Scenarios**:

1. **Given** a queue with the resume option on, **When** the operator stops it and the service is then restarted, **Then** the queue stays Stopped.
2. **Given** a queue with the resume option off that was Running, **When** the service restarts, **Then** the queue stays Stopped (today's behaviour).
3. **Given** a queue with the resume option on whose run ended by itself (completed, failed, or stopped by its failure policy), **When** the service restarts, **Then** the queue stays Stopped.
4. **Given** a queue with the resume option on that was never started, **When** the service restarts, **Then** the queue stays Stopped.

---

### User Story 3 - The operator can configure and see the option (Priority: P2)

The operator turns the option on or off when creating or editing a queue, through both the API and the web UI, and sees its current value when viewing the queue. When the service resumes a queue, or tries to and cannot, that is recorded in the service log so it can be diagnosed.

**Why this priority**: The option must be reachable without hand-editing stored files, but the core behaviour (Stories 1–2) can be verified through the API alone.

**Independent Test**: Create a queue with the option on through the API, read it back, toggle it off through the edit form in the UI, read it back.

**Acceptance Scenarios**:

1. **Given** the queue create/edit form, **When** the operator enables the option and saves, **Then** reading the queue back reports it enabled.
2. **Given** a queue stored before this feature existed, **When** it is read, **Then** the option reports disabled and nothing else about the queue changes.
3. **Given** a queue that could not be resumed at startup (e.g. its device is already claimed), **When** the operator reads the service log, **Then** an entry names the queue and why it was not resumed.

### Edge Cases

- **Changing the option**: a queue can only be edited while it is Stopped (existing rule), so the option a queue is resumed with is the value it last ran with. What matters is the option's value at startup and whether the queue was Running when the service went down.
- **Remembered as Running but the option is off** (e.g. a queue that was running when this feature was deployed, or stale state): the queue is not resumed and the remembered state is discarded.
- **Service stops while a run happens to be ending on its own**: the run is still remembered as Running and is resumed as a fresh start; ending during shutdown is indistinguishable from being ended by shutdown, and a fresh start is harmless.
- **Queue deleted while the service is down, or its record is unreadable**: nothing is resumed for it, and startup continues for the other queues.
- **Two opted-in queues bound to the same device were both remembered as Running** (only possible through stale state): the existing one-run-per-device rule applies — one starts, the other is not resumed and the refusal is logged.
- **Resume start fails at run level** (emulator unreachable, no template): the run fails exactly as a manual start would, is recorded in the execution log as today, and is not retried automatically; the queue is then no longer remembered as Running.
- **Stored "was Running" record missing or corrupt**: treated as "no queue was Running"; the service starts normally and logs the problem.
- **Service stopped again before resume finished**: queues not yet resumed stay remembered as Running and are resumed on the next start.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Each queue MUST have a persisted "resume after service restart" option, off by default. Queues stored before this feature MUST read as off without migration.
- **FR-002**: Operators MUST be able to set and read the option through the queue create, update, get and list operations, and through the web UI queue form. Duplicating a queue MUST copy the option.
- **FR-003**: The service MUST durably remember, per queue, that the queue is Running, recording it at the moment a run starts, so the fact survives an abrupt termination with no graceful shutdown.
- **FR-004**: The remembered Running state MUST be cleared when a run ends while the service is not shutting down, whatever ended it: operator stop, completed run, run-level failure, or failure-policy stop.
- **FR-005**: The remembered Running state MUST NOT be cleared when a run ends while the service itself is shutting down.
- **FR-006**: When the service starts, it MUST start every queue that both has the option on and is remembered as Running, using the same start behaviour as a manual start (same entry schedule semantics, same device-claim rules, same emulator cold-start).
- **FR-007**: Queues remembered as Running whose option is off MUST NOT be started, and their remembered state MUST be discarded.
- **FR-008**: Remembered state for queues that no longer exist MUST be discarded without affecting startup.
- **FR-009**: A failure to resume one queue MUST NOT prevent the others from being resumed, nor prevent the service from starting.
- **FR-010**: Each resume attempt MUST be logged by the service with the queue identifier and its outcome (started, device in use, not found, error).
- **FR-011**: Resume MUST happen only once the service has finished starting, so the queue's run can reach its dependencies (device sessions, execution log) the same way a manual start does. All eligible queues are started in that single pass with no delay between them, and each gets exactly one attempt per service start (no retry).
- **FR-012**: Runtime-only bookings from the previous run (self-reschedule entries, live schedules, daily-retry state) MUST NOT be restored; the resumed run starts fresh from the queue's linked template.
- **FR-013**: The behaviour of a manual start and a manual stop MUST otherwise be unchanged.
- **FR-014**: The option MUST be documented in the API schema and in the project's architecture documentation for queues.

### Key Entities

- **Queue (configuration)**: gains the "resume after service restart" option (boolean, default off).
- **Running-queue record**: the durable set of queue identifiers the service believes are Running. Written when a run starts, entry removed when a run ends for a reason other than service shutdown. Read once at service start.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After a service restart, 100% of opted-in queues that were Running are Running again within 30 seconds of the service being ready, with zero operator actions.
- **SC-002**: 0 queues that were stopped by an operator, finished on their own, or were not opted in are started by a service restart.
- **SC-003**: An abrupt service termination (no graceful shutdown) resumes opted-in queues the same as a graceful restart.
- **SC-004**: Every queue the service resumes or fails to resume at startup leaves exactly one log line identifying it and the outcome.

## Assumptions

- Opt-in is per queue, not a single service-wide setting: the issue allows either, and a per-queue flag lets the operator protect experimental queues while resuming production ones.
- A resumed run is indistinguishable from a manual start everywhere except the service log (no new UI indicator or execution-log field).
- The resumed run is a new run with its own execution-log entry; the previous run's entry is closed as it is today on shutdown.
- A queue remembered as Running but not opted in is simply not resumed; no warning to the operator beyond the service log.

## Non-Goals

- Preserving runtime self-rescheduled entries, live schedules or in-flight sequence execution across the restart.
- Any OS-level scheduled task or external watchdog that calls start.
- Changing the semantics of a normal manual start or stop.
