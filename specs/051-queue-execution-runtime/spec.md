# Feature Specification: Queue Execution Runtime

**Feature Branch**: `051-queue-execution-runtime`  
**Created**: 2026-06-02  
**Status**: Implemented
**Input**: User description: "let's bring live into the queues. I want to be able to execute the queues. When the queue is executed (either via API or from the UI), first a linked template is loaded on the server side, then the queue establishes connection to the configured emulator (for now if the emulator is not available, it should stop with a failure execution log entry), and then execute sequences from loaded template one after another until the end. At the end of every execution, an execution log entry should be made, why the execution was stopped (completed full run/stopped manually/failure due to what). If the 'cycle execution' is set, it should then start from the beginning without reloading the template. When I stop the execution either via API of via UI, the execution should abort immediately, disconnect a session from the emulator and write a log entry."

## Clarifications

### Session 2026-06-02

- Q: When a started queue has no resolvable linked template (none linked, or the linked template was deleted), what should happen? → A: Stop immediately with a failure execution log entry ("no template to run"); no sequences executed.
- Q: When one sequence in the run fails, what should the queue run do? → A: Continue past failures and still end "completed full run" — per-sequence failures are non-fatal and do not, by themselves, make the run a failure (the failure is still recorded in that sequence's nested entry). Run-level failures (e.g., emulator unreachable at start or connection lost mid-run) still end the run as a failure.
- Q: If a second queue bound to the same emulator is started while another queue on that emulator is already running, what should happen? → A: Allow concurrent runs on the same emulator (operator's responsibility, no guard).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Run a queue's sequences end-to-end (Priority: P1)

An operator starts a queue (from the Queues list/editor in the UI, or via the API). The system loads the queue's linked template on the server, connects to the emulator the queue is bound to, and then executes the template's sequences one after another in order. When the last sequence finishes, execution ends: the system disconnects the emulator session it established, records an execution log entry stating the run completed a full pass, and the queue's status returns to not-running.

**Why this priority**: This is the core of the feature — turning the previously placeholder start action (which only flipped a status flag) into real execution that actually runs the queue's work. Without it, nothing else (stopping, cycling, failure reporting) has anything to operate on.

**Independent Test**: Link a queue to a template containing two or more sequences, ensure the queue's bound emulator is connected, start the queue, and confirm the sequences run in order to the end, the queue returns to not-running, and a single execution log entry records "completed full run".

**Acceptance Scenarios**:

1. **Given** a queue linked to a template with sequences A, B, C and a connected bound emulator, **When** the operator starts the queue, **Then** the system loads the template server-side, connects to the emulator, and runs A, then B, then C in that order.
2. **Given** the queue finished running all its sequences without being stopped or failing, **When** execution ends, **Then** the emulator session is disconnected, an execution log entry is recorded with a stop reason of "completed full run", and the queue's status returns to not-running.
3. **Given** a queue, **When** it is started via the API (rather than the UI), **Then** the same load → connect → run-sequences flow occurs and the same kind of execution log entry is produced.
4. **Given** a running queue, **When** the operator views the Queues list, **Then** the queue is shown as running while it executes and returns to not-running when it ends.

---

### User Story 2 - Stop a running queue immediately (Priority: P1)

While a queue is executing, an operator stops it (from the UI or via the API). Execution aborts as soon as possible — it does not wait for the current sequence to finish its remaining work — the emulator session is disconnected, and an execution log entry is recorded noting the run was stopped manually. The queue returns to not-running.

**Why this priority**: The operator explicitly needs to be able to halt a run that is misbehaving or no longer wanted. An execution engine that cannot be stopped promptly is unsafe to use, so this is as critical as starting.

**Independent Test**: Start a queue with several sequences, stop it while it is partway through, and confirm execution aborts promptly, the emulator session is disconnected, the queue returns to not-running, and an execution log entry records "stopped manually".

**Acceptance Scenarios**:

1. **Given** a queue executing its sequences, **When** the operator stops it from the UI, **Then** execution aborts promptly without completing the remaining sequences, the emulator session is disconnected, and the queue returns to not-running.
2. **Given** a queue executing its sequences, **When** it is stopped via the API, **Then** the same prompt abort, disconnect, and not-running outcome occurs.
3. **Given** a queue that was stopped manually, **When** execution ends, **Then** an execution log entry is recorded with a stop reason of "stopped manually".
4. **Given** a queue that is not running, **When** the operator issues a stop, **Then** the action is a no-op (no error) and the queue remains not-running.

---

### User Story 3 - Cycle execution repeats automatically until stopped (Priority: P2)

An operator who enabled "cycle execution" on a queue starts it and expects it to keep running: when the last sequence of the template finishes, the queue starts again from the first sequence without reloading the template, and keeps cycling until the operator stops it or a failure occurs. With cycle execution off, the queue runs the template's sequences exactly once and then ends.

**Why this priority**: Cycle execution is the runtime meaning of the flag that earlier iterations stored but left undefined. It is high-value for unattended farming runs, but it depends on the core run (US1) and stop (US2) behaviors existing first.

**Independent Test**: Enable cycle execution on a queue, start it, observe that after the template's sequences finish they begin again from the start, let it complete at least two full passes, then stop it and confirm it ends with "stopped manually".

**Acceptance Scenarios**:

1. **Given** a queue with cycle execution enabled and a template with sequences A, B, **When** the queue runs and reaches the end of B, **Then** it restarts from A without reloading or re-resolving the template, and continues cycling.
2. **Given** a queue with cycle execution enabled that is cycling, **When** the operator stops it, **Then** execution aborts promptly and the run ends with a "stopped manually" log entry.
3. **Given** a queue with cycle execution disabled, **When** it finishes the template's sequences once, **Then** it ends with a "completed full run" log entry and does not repeat.

---

### User Story 4 - Failure and connection outcomes are clearly logged (Priority: P2)

When a run cannot start or cannot continue, the operator gets a clear, durable explanation. If the bound emulator is not available when the operator starts the queue, the run stops immediately with a failure execution log entry explaining the emulator could not be reached, and no sequences are executed. Individual sequence failures during a run are non-fatal — each is recorded in its own nested entry and the run continues — whereas a run-level failure (e.g., the emulator connection is lost so remaining sequences cannot run) ends the run with a failure stop reason.

**Why this priority**: Diagnosability is essential for an automation tool an operator may run unattended, but it builds on the core run path (US1). The single most important failure case — emulator unavailable at start — is explicitly required now.

**Independent Test**: Start a queue whose bound emulator is not connected and confirm no sequences run, the queue returns to not-running, and a failure execution log entry explains the emulator was unavailable.

**Acceptance Scenarios**:

1. **Given** a queue whose bound emulator is not connected/available, **When** the operator starts the queue, **Then** the run stops immediately, no sequences are executed, and an execution log entry records a failure with a reason indicating the emulator could not be reached.
2. **Given** a queue executing its sequences, **When** an individual sequence fails, **Then** that sequence's nested execution log entry records the failure and the run continues with the next sequence (the per-sequence failure does not stop the run or by itself make the run a failure).
3. **Given** a queue whose bound emulator becomes unavailable mid-run (the connection is lost), **When** the system detects the loss, **Then** the run stops with a failure execution log entry indicating the emulator connection was lost.

---

### Edge Cases

- **No linked template / unresolvable link at start**: The queue has no linked template, or its linked template can no longer be resolved (deleted). See FR-002 / clarification — the run does not proceed silently; the operator gets a clear outcome.
- **Empty template (zero sequences)**: Starting a queue whose template has no sequences completes a full pass immediately with "completed full run". With cycle execution on, the system MUST NOT spin in a tight, resource-burning loop over an empty template.
- **Stale sequence reference inside the template**: A template entry whose referenced sequence has been deleted cannot be executed; it is recorded as a non-fatal per-sequence failure (FR-008b) and the run continues with the next entry, rather than being silently skipped without record.
- **Stop during the very first sequence / before the first sequence starts**: Stopping right after start (even before or during the first sequence) still aborts promptly, disconnects, and records "stopped manually".
- **Starting an already-running queue**: Starting a queue that is already running does not launch a second concurrent run for that queue; the existing run continues and the operator is informed it is already running.
- **Concurrency on the same emulator**: Two queues bound to the same emulator may run at the same time; this is allowed without a guard (FR-013), and avoiding device-level interference between them is the operator's responsibility this iteration.
- **Service restart while running**: Runtime execution status is not persisted; after a service restart no queue is running, and any run that was in progress is no longer active (consistent with the queue feature's non-persistence of runtime status).
- **Failure to disconnect on stop**: If disconnecting the emulator session encounters an error during stop/teardown, the run still ends and an execution log entry is still written (teardown problems do not prevent the run from terminating).

## Requirements *(mandatory)*

### Functional Requirements

#### Starting a run

- **FR-001**: System MUST allow an operator to execute (start) a queue both from the UI and via the API, replacing the previous placeholder start behavior that only changed status without running anything.
- **FR-002**: On start, System MUST load the queue's linked template on the server side and use the template's ordered sequence entries as the work to execute. When the queue has no resolvable linked template at start (no template linked, or the linked template can no longer be resolved), the system MUST stop the run immediately without executing any sequence and MUST record a failure execution log entry whose reason indicates there is no template to run.
- **FR-003**: After loading the template, System MUST establish a connection/session to the emulator the queue is bound to (identified by its ADB device serial) before executing any sequence.
- **FR-004**: If the bound emulator is not available when establishing the connection, System MUST stop the run immediately without executing any sequence and MUST record a failure execution log entry whose reason indicates the emulator could not be reached.
- **FR-005**: System MUST reflect the queue's real execution state in its status (Running while executing, returning to Stopped/not-running when the run ends), consistent with the existing queue status model.

#### Executing sequences

- **FR-006**: While running, System MUST execute the loaded template's sequences one after another, in the template's order, starting the next only after the previous one has finished (no implicit parallelism within a single queue run).
- **FR-007**: System MUST record the queue's sequence executions in the persisted execution log consistent with the existing execution-log hierarchy, so the operator can see which sequences ran and their outcomes (and reach the per-sequence/per-step detail captured today).
- **FR-008**: When a sequence in the run fails, System MUST treat the failure as non-fatal to the queue run: it MUST record the failure in that sequence's nested execution log entry, then continue with the next sequence. A per-sequence failure MUST NOT, by itself, abort the run or set the queue run's stop reason to "failure"; a run that reaches the end of the template this way still ends with the stop reason "completed full run".
- **FR-008a**: Run-level failures — distinct from individual sequence failures — MUST end the run with a "failure" stop reason. These include the emulator being unreachable at start (FR-004) and the emulator connection being lost mid-run (so remaining sequences cannot run).
- **FR-008b**: A template entry whose referenced sequence cannot be resolved/executed (a stale/unresolved reference) MUST be recorded as a non-fatal per-sequence failure (consistent with FR-008) rather than silently skipped without record, and the run MUST continue with the next entry.

#### Ending a run & the execution log entry

- **FR-009**: At the end of every queue run, System MUST record one execution log entry for that run that states why execution stopped, using at least these stop reasons: "completed full run", "stopped manually", and "failure" (with a reason describing what failed).
- **FR-010**: A run that executes all of the template's sequences without being stopped manually and without a run-level failure (one full pass when cycle execution is off) MUST end with the stop reason "completed full run" — even if one or more individual sequences failed (those failures are recorded in their nested entries per FR-008).
- **FR-011**: The queue-run execution log entry MUST be durable (persisted) and retrievable through the same execution-log facilities used for other executions, and MUST be understandable to an operator (clear stop reason and, on failure, a user-facing explanation of what failed).
- **FR-012**: The queue-run execution log entry's overall status MUST reflect the run-level outcome (success for a completed full run; failure for a run-level failure per FR-008a; the manually-stopped outcome distinguishable from a clean completion). Because per-sequence failures are non-fatal, a "completed full run" is reported as success at the run level even if some sequences failed; the run's summary SHOULD indicate how many sequences failed so the operator is not misled, and the per-sequence failures remain visible in the nested entries.

#### Concurrency

- **FR-013**: System MUST allow more than one queue run to target the same bound emulator at the same time: starting a second queue bound to an emulator that already has a running queue MUST NOT be blocked on account of that shared emulator. Avoiding device-level interference between concurrent runs on the same emulator is the operator's responsibility (no guard this iteration).
- **FR-013a**: System MUST NOT start a second concurrent run for a queue that is already running; the in-progress run continues and the operator is informed the queue is already running (start is not silently duplicated).

#### Cycle execution

- **FR-014**: When a queue has "cycle execution" enabled, System MUST, upon finishing the last sequence of the template, begin again from the first sequence and continue repeating until the run is stopped manually or a failure ends it.
- **FR-015**: During cycle execution, System MUST NOT reload or re-resolve the template between cycles — the template loaded at the start of the run is reused for every cycle within that run.
- **FR-016**: When "cycle execution" is disabled, System MUST execute the template's sequences exactly once and then end the run.
- **FR-017**: System MUST NOT enter a tight, resource-burning loop when cycle execution is enabled but the template has no sequences to run; an empty template MUST be handled gracefully (e.g., the run ends rather than spinning).

#### Stopping a run

- **FR-018**: System MUST allow an operator to stop a running queue both from the UI and via the API.
- **FR-019**: On stop, System MUST abort execution promptly — it MUST NOT wait for the remaining sequences (and SHOULD interrupt the in-progress sequence as soon as practical rather than letting it run to completion).
- **FR-020**: On stop, System MUST disconnect/tear down the emulator session that the run established.
- **FR-021**: On stop, System MUST record an execution log entry with the stop reason "stopped manually" and return the queue to not-running.
- **FR-022**: Stopping a queue that is not running MUST be a no-op (no error), leaving the queue not-running.
- **FR-023**: System MUST also disconnect the emulator session when a run ends for any other reason (completed full run or failure), not only on manual stop, so a run never leaves a dangling emulator session.

### Key Entities *(include if feature involves data)*

- **Queue Run (Queue Execution)**: One execution of a queue, from start to termination. Carries: the queue it ran, the template snapshot it loaded, the bound emulator it connected to, start time, end time, the stop reason ("completed full run" / "stopped manually" / "failure"), and an overall status. Surfaced as an execution log entry; runtime state is not persisted across restarts.
- **Loaded Template Snapshot**: The ordered set of sequences resolved from the queue's linked template at the moment the run starts; reused across cycles within the same run without re-resolution.
- **Emulator Session**: The connection to the queue's bound emulator (by ADB device serial) established for the duration of a run and torn down when the run ends.
- **Queue**: The existing emulator execution queue (bound emulator, cycle-execution flag, linked template, runtime status). This feature gives runtime meaning to start/stop and the cycle-execution flag; it does not change how queues are created, edited, or linked.
- **Queue Template / Sequence**: Existing entities. The template provides the ordered sequences to run; sequences are executed using the existing sequence-execution behavior. This feature does not modify how templates or sequences are authored.
- **Execution Log Entry**: The existing persisted execution record. This feature adds a queue-run entry (with stop reason) and nests the run's sequence executions beneath it consistent with the existing hierarchy.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Starting a queue whose template has sequences and whose bound emulator is reachable executes 100% of the template's sequences in order and, when cycle execution is off, ends with exactly one queue-run execution log entry whose stop reason is "completed full run" — including when one or more individual sequences failed (those failures are recorded in their nested entries without changing the run's stop reason).
- **SC-002**: Starting a queue whose bound emulator is not available executes zero sequences and produces a failure execution log entry explaining the emulator was unreachable in 100% of attempts.
- **SC-003**: When an operator stops a running queue, execution aborts within 3 seconds, the emulator session is disconnected, the queue returns to not-running, and a "stopped manually" execution log entry is recorded, in 100% of stop requests.
- **SC-004**: With cycle execution enabled, a queue completes consecutive full passes (at least two observed) without reloading the template, and stops only on a manual stop or a failure — verified by observing repeated passes and a single terminating log entry with the corresponding stop reason.
- **SC-005**: Every queue run, regardless of how it ends, produces exactly one terminating execution log entry whose recorded stop reason matches what actually happened (completed / stopped / failure) in 100% of runs.
- **SC-006**: No queue run leaves a dangling emulator session: after a run ends for any reason, the session it established is disconnected in 100% of runs.
- **SC-007**: Both the API and the UI can start and stop a queue, and both paths produce equivalent execution behavior and execution log entries.
- **SC-008**: An individual sequence failure during a run never stops the run: the run still executes every subsequent sequence and ends "completed full run", with the failure visible only in that sequence's nested entry, in 100% of such cases.
- **SC-009**: Two queues bound to the same emulator can both be started and run at the same time without the system refusing the second start on account of the shared emulator.

## Assumptions

- **Supersedes placeholder behavior**: This feature replaces the placeholder start/stop behavior of the Emulator Execution Queue feature (which only toggled status) with real execution, and gives the previously-stored-only "cycle execution" flag its runtime meaning.
- **Template is the source of work**: The sequences to run come from the queue's linked template, loaded/resolved on the server at the start of the run (consistent with the queue→template link resolving live at use time). A queue with no resolvable linked template cannot run and ends immediately with a failure log entry "no template to run" (FR-002).
- **Failure policy**: Individual sequence failures are non-fatal — the run records them and continues, still ending "completed full run". Only run-level failures (emulator unreachable at start or connection lost mid-run) end the run as a failure (FR-008/FR-008a).
- **Same-emulator concurrency**: Multiple queues bound to the same emulator may run concurrently; the system does not coordinate or serialize them, and preventing device-level interference is the operator's responsibility this iteration (FR-013).
- **Emulator identity**: The "configured emulator" is the queue's bound ADB device serial established at queue creation. "Available" means the device is currently connected and usable for a session.
- **Sequence execution reuse**: Each sequence is executed using the existing sequence-execution engine and produces the same per-step detail and logging it does today; this feature orchestrates running them in order, not how a single sequence runs.
- **Execution log integration**: The queue run is represented as a top-level execution log entry, and its sequences are nested beneath it following the existing execution-log hierarchy (features 049/050). Live, in-progress display of the run follows existing execution-log live-update behavior where applicable.
- **Non-persistence of runtime**: Consistent with the queue feature, runtime execution state (including an in-progress run) is not persisted across service restarts; after a restart no queue is running.
- **Stop promptness**: "Abort immediately" means the run stops as soon as practical — at the latest at the next safe interruption point within the in-progress sequence — not that an already-issued device action can be physically un-sent.
- **Scale**: Operator-scale, consistent with prior queue features (up to ~50 queues, ~100 entries each); typically one or a few queues running at a time.
- **Single-operator tool**: No multi-user coordination; concurrency rules concern a single operator starting multiple queues (FR-013).

## Out of Scope

- Scheduling queues to start automatically at a time or on a trigger (this feature is operator-initiated start/stop only).
- Pause/resume of a running queue (only start and stop are in scope).
- Progress percentage, ETA, or per-sequence progress bars beyond what the existing execution-log live updates provide.
- Reordering, editing, or adding sequences to a queue mid-run (the run uses the template snapshot loaded at start).
- Retrying failed sequences automatically or configurable retry/backoff policies for the queue run.
- Changing how an individual sequence executes internally (steps, conditions, loops, waits) — reused as-is.
- Persisting runtime execution state across restarts or resuming an interrupted run after a restart.
- Cross-machine or remote execution; runs target the local emulator the queue is bound to.
