---
description: "Task list for feature 087 — Queue Failure Policy and Outbound Notification"
---

# Tasks: Queue Failure Policy and Outbound Notification

**Input**: Design documents from `/specs/087-queue-failure-policy/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/
**Issue**: [#181](https://github.com/bbqf/GameBotAI/issues/181)

**Tests**: INCLUDED. Constitution Principle II makes testing mandatory for executable logic
(unit for core logic, integration for externally visible contracts), so test tasks are not optional
for this feature.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story the task serves (US1, US2, US3)
- Every task names its exact file path

## Path Conventions

Single ASP.NET Core host: `src/GameBot.Domain/`, `src/GameBot.Service/`, with tests in
`tests/unit/`, `tests/contract/`, `tests/integration/`.

**Test-placement rule (non-negotiable, from `gamebot-test-harness-gotchas`)**: `tests/contract`
shares a bin data directory across the whole project — a live queue run started there pollutes it
and makes the ExecutionLogs tests 500. **Every test that starts a real run goes in
`tests/integration`.** `tests/contract` gets only not-running / validation / shape assertions.

---

## Phase 1: Setup

No project scaffolding is needed — this feature extends an existing host. These two tasks establish
the shared infrastructure every later phase depends on.

- [ ] T001 Add `FailureNotificationOptions` (const `SectionName = "Service:Notifications"`; `DefaultUrl`, `AuthHeaderName`, `AuthHeaderValue`, `TimeoutSeconds` = 5, `MaxAttempts` = 2) in `src/GameBot.Service/Services/Notifications/FailureNotificationOptions.cs`, following the `DetectionOptions` pattern exactly
- [ ] T002 Register the options section and `builder.Services.AddHttpClient<HttpFailureNotifier>(...)` (setting `Timeout` from `TimeoutSeconds`) in `src/GameBot.Service/GameBotServiceSetup.cs`, alongside the existing `Configure<DetectionOptions>` call

**Checkpoint**: Service builds and starts; configuration binds; no behaviour change yet.

---

## Phase 2: Foundational (BLOCKING — all user stories depend on this)

The persisted policy and its validation. Nothing acts on it yet, so this phase is safe to land on
its own.

- [ ] T003 [P] Create `QueueFailureAction` enum (`Notify`, `Stop`, `Pause`, `NotifyAndStop`) with XML docs stating each value's effect, in `src/GameBot.Domain/Queues/QueueFailureAction.cs`
- [ ] T004 [P] Create `QueueFailurePolicy` (`ConsecutiveFailedCycles`, `Action`, `NotifyUrl`) with XML docs covering inputs and error modes, in `src/GameBot.Domain/Queues/QueueFailurePolicy.cs`
- [ ] T005 Add `public QueueFailurePolicy? FailurePolicy { get; set; }` to `src/GameBot.Domain/Queues/ExecutionQueue.cs`, documenting that `null` means no policy and that absent JSON deserialises to `null` (back-compat, no migration)
- [ ] T006 Append `StoppedByFailurePolicy` to the **end** of `src/GameBot.Domain/Queues/QueueStopReason.cs` (never mid-enum — the value is serialised by name but readers may index), with XML docs distinguishing it from `StoppedManually` and `Failure`
- [ ] T007 [P] Create `QueueFailurePolicyDto` (request/response shape) in `src/GameBot.Service/Contracts/Queues/QueueFailurePolicyDto.cs`
- [ ] T008 Add `FailurePolicy` to `CreateQueueRequest`, `UpdateQueueRequest` and `QueueResponse` in `src/GameBot.Service/Contracts/Queues/`
- [ ] T009 Implement policy validation + mapping in `src/GameBot.Service/Endpoints/QueuesEndpoints.cs` for the create and update routes: threshold ≥ 1, action in range, `NotifyUrl` absolute http/https, and the cross-field rule that a notifying action needs a destination from either the policy or `Service:Notifications:DefaultUrl` — each returning 400 with the exact message from `contracts/queue-api.md` §1
- [ ] T010 [P] Unit-test policy validation (all four 400 cases, plus the valid cases and the null-policy case) in `tests/unit/Queues/QueueFailurePolicyValidationTests.cs`
- [ ] T011 [P] Contract-test that a policy round-trips through create → get → update → get unchanged, and that a queue saved without one reads back `null`, in `tests/contract/Queues/QueueFailurePolicyContractTests.cs` (no live runs). Note in the test file that this exercises serialisation through the file repository but does **not** restart the host — FR-003's "survives a restart" rests on the repository being file-backed, which the round-trip proves; a restart harness is deliberately out of scope

**Checkpoint**: A policy can be configured, validated and persisted. Nothing evaluates it.

---

## Phase 3: User Story 1 — A failing queue raises an alert on its own (Priority: P1) 🎯 MVP

**Goal**: A queue configured with `{ count: N, action: "notify" }` delivers exactly one outbound
notification on the Nth consecutive failed cycle, keeps running, and re-arms after a success.

**Independent test**: Configure a queue with `{3, notify}` against a stub receiver, make every cycle
fail, observe one notification after the third failed cycle with the queue still Running.

### The notification path

- [ ] T012 [P] [US1] Create `FailureNotificationEvent` with all 14 fields and `SchemaVersion = 1`, matching `contracts/notification-payload.md` exactly (camelCase JSON), in `src/GameBot.Service/Services/Notifications/FailureNotificationEvent.cs`
- [ ] T013 [P] [US1] Create `IFailureNotifier` with a single `Task NotifyAsync(FailureNotificationEvent evt, string? overrideUrl, CancellationToken ct)`, documenting in XML that **implementations must never throw** — that contract is load-bearing and must be stated, not just implemented — in `src/GameBot.Service/Services/Notifications/IFailureNotifier.cs`
- [ ] T014 [US1] Implement `HttpFailureNotifier` in `src/GameBot.Service/Services/Notifications/HttpFailureNotifier.cs`: resolve destination (override → `DefaultUrl`), attach the auth header when configured, POST JSON, treat any 2xx as success, retry once after a 1 s backoff, swallow every exception, and report the outcome via a callback so the caller can record it. Never reads or logs the response body — only the status code
- [ ] T014a [US1] Write the **structured application-log** half of FR-015a in `HttpFailureNotifier`: one source-generated log entry per abandoned notification carrying the queue id, destination host and failure reason, following the `QueueExecutionLog` pattern. The health block (T021/T022) is only the other half — FR-015a requires both, so that an operator who is tailing logs and one who is polling the API each learn the same thing. **The auth header value must never appear in a log line**, nor must the response body
- [ ] T015 [US1] Register `IFailureNotifier` → `HttpFailureNotifier` in `src/GameBot.Service/GameBotServiceSetup.cs`

### Run state and the evaluator

- [ ] T016 [US1] Add lock-guarded `PolicyTripped` (with `MarkPolicyTripped()` / `ClearPolicyTripped()`) and the three `LastNotification*` fields (with a single setter method) to `src/GameBot.Service/Services/QueueExecution/QueueRunHandle.cs`, following the existing `IdlePausedUntil` locking pattern
- [ ] T017 [US1] Implement `QueueFailurePolicyEvaluator` in `src/GameBot.Service/Services/QueueExecution/QueueFailurePolicyEvaluator.cs`: read `handle.Cycles.SnapshotHealth()`, clear the trip flag on a successful cycle, and on a failed cycle trip when `consecutive >= threshold && !PolicyTripped` (the `>=` is deliberate — see data-model.md), then dispatch the action. Must be non-throwing end to end and must start delivery **off** the calling thread with a token that is **not** the run's
- [ ] T018 [US1] Build the event from the tripping cycle's record in the evaluator: first failed entry's index and sequence id, `failedEntryCount`, and the sequence **name resolved from `ISequenceRepository` at send time** (never stored in the ledger, per research R7)
- [ ] T019 [US1] Add the single evaluator call to `ExecuteRunAsync` in `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs`, immediately after the existing `handle.Cycles.CompleteOpen(...)` (~line 517) — **one line, no inline block** (analyzer hazard; see plan Summary)
- [ ] T020 [US1] Short-circuit the evaluator on `queue.FailurePolicy is null` before any snapshot is taken, so an unconfigured queue pays nothing (FR-004, SC-004)

### Visibility

- [ ] T021 [US1] Add `failurePolicyConfigured`, `failurePolicyTripped`, `lastNotificationAt`, `lastNotificationSucceeded`, `lastNotificationError` to `src/GameBot.Service/Contracts/Queues/QueueHealthResponse.cs`
- [ ] T022 [US1] Populate the new fields in `ProjectHealth` in `src/GameBot.Service/Endpoints/QueuesEndpoints.cs`, leaving every field feature 086 published unchanged in name and meaning

### Tests

- [ ] T023 [P] [US1] Unit-test the evaluator's decision table in `tests/unit/QueueExecution/QueueFailurePolicyEvaluatorTests.cs`: below threshold → nothing; at threshold → trip + notify once; **above** threshold while tripped → nothing (FR-009); success → reset and re-arm (FR-008); a second episode after a success → notifies again; null policy → nothing
- [ ] T024 [P] [US1] Unit-test `HttpFailureNotifier` against a stub `HttpMessageHandler` in `tests/unit/Notifications/HttpFailureNotifierTests.cs`: payload field-for-field against the contract, auth header present only when configured, 2xx = success, non-2xx retried once then abandoned, timeout honoured, and **a handler that throws never propagates**
- [ ] T025 [P] [US1] Contract-test that `AuthHeaderValue` appears in **no** API response — queue detail, queue list, or health — in `tests/contract/Queues/NotificationSecretExposureTests.cs`
- [ ] T026 [US1] Integration-test the issue's exact condition in `tests/integration/QueueExecution/QueueFailurePolicyNotifyTests.cs`: a real run whose every cycle fails trips at exactly N, the stub notifier receives exactly one event with the right queue/entry/sequence/count, and the queue is still Running afterwards
- [ ] T027 [US1] Integration-test reset and re-arm in the same file: fail to N−1, succeed, confirm no notification and a zeroed count; then fail to N again and confirm a second notification
- [ ] T028 [US1] Integration-test SC-005 in the same file: a stub notifier that blocks for 30 s must not delay cycle completion — assert cycle timing is unchanged against a control run
- [ ] T028b [US1] Integration-test FR-026 / SC-008 in the same file: with a destination that refuses connections, trip the policy and assert the run's live health reports `lastNotificationSucceeded: false` with a non-null `lastNotificationError` — the delivery failure must be establishable from the API alone, without opening a log file

**Checkpoint**: 🎯 **MVP complete.** The issue's core complaint is closed — a failing queue escalates
on its own. Phases 4 and 5 are additive and can be delivered separately.

---

## Phase 4: User Story 2 — The operator chooses what a tripped policy does (Priority: P2)

**Goal**: `stop`, `pause` and `notifyAndStop` all work, with pause resumable.

**Independent test**: Run the same failing queue under each action and confirm the four distinct
outcomes.

### Stop

- [ ] T028a [US2] Add a lock-guarded `StopRequestedByPolicy` flag (with a `MarkStopRequestedByPolicy()` setter) to `src/GameBot.Service/Services/QueueExecution/QueueRunHandle.cs`. **This is the mechanism T030 reads**: an operator stop and a policy stop both cancel the same CTS, so the terminating `catch` has no other way to tell them apart. `PolicyTripped` cannot serve — it is also set by a `notify` action that never stops the run
- [ ] T029 [US2] Implement the `Stop` and `NotifyAndStop` branches in `QueueFailurePolicyEvaluator` (`src/GameBot.Service/Services/QueueExecution/QueueFailurePolicyEvaluator.cs`): set `StopRequestedByPolicy` **before** cancelling the run's CTS, and for `NotifyAndStop` start the notification before cancelling too, so the alert is not cancelled by the stop it announces
- [ ] T030 [US2] Set the terminating stop reason to `StoppedByFailurePolicy` in `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs` by reading `handle.StopRequestedByPolicy` in the existing `catch (OperationCanceledException)` — which today maps every cancellation to `StoppedManually`. Both `catch (OperationCanceledException)` sites (the inner one around the sequence loop and the outer one) must be updated, or a policy stop that lands in the outer catch is still misreported

### Pause

- [ ] T031 [US2] Add lock-guarded `PausedAt`, `PauseReason`, derived `IsPaused`, `EnterPolicyPause(reason, at)` and `Resume()` to `src/GameBot.Service/Services/QueueExecution/QueueRunHandle.cs`, plus the gate primitive the loop awaits. Document clearly that this is **distinct from** feature 073's `IdlePausedUntil` (no resume instant; released only by an operator) so the two are never conflated
- [ ] T032 [US2] Implement the `Pause` branch in the evaluator: set the pause state with a reason naming the threshold, and engage the gate
- [ ] T033 [US2] Add the single pause-gate await to `ExecuteRunAsync` in `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs` at the **top of the do/while iteration, before the timer-evaluation blocks** — this placement is what makes FR-018a free (due-ness is computed after the gate, so nothing needs a skip-list). One line
- [ ] T034 [US2] Add `POST /api/queues/{id}/resume` to `src/GameBot.Service/Endpoints/QueuesEndpoints.cs` per `contracts/queue-api.md` §3: 200 with `resumed: true|false` for all known-queue cases, 404 for unknown; clearing pause state **and** the trip flag on a successful resume
- [ ] T035 [US2] Add `paused`, `pausedAt`, `pauseReason` to `QueueHealthResponse` and `ProjectHealth` in `src/GameBot.Service/Contracts/Queues/QueueHealthResponse.cs` and `src/GameBot.Service/Endpoints/QueuesEndpoints.cs`

### Tests

- [ ] T036 [P] [US2] Unit-test the evaluator's action dispatch for all four actions in `tests/unit/QueueExecution/QueueFailurePolicyEvaluatorTests.cs`, including that `NotifyAndStop` notifies before cancelling
- [ ] T037 [P] [US2] Contract-test the resume endpoint's four response cases in `tests/contract/Queues/QueueResumeContractTests.cs` — unknown queue (404) and not-running (200, `resumed: false`) only; the paused cases are integration-level (no live runs in `tests/contract`)
- [ ] T038 [US2] Integration-test `stop` in `tests/integration/QueueExecution/QueueFailurePolicyActionTests.cs`: the run terminates and its terminating execution-log record carries `StoppedByFailurePolicy`, not `StoppedManually`
- [ ] T039 [US2] Integration-test `pause` and resume in the same file: the run parks (no further firings, still the registered run, health reports paused + reason), `POST {id}/resume` restarts it, the failure count is cleared, and the policy can trip again
- [ ] T039a [US2] Integration-test FR-018a in the same file: arm a firing that becomes due **while the run is paused**, resume, and assert it fires immediately rather than being skipped — and that nothing is recorded as a skipped firing. This is the property that justifies gating before timer evaluation (T033); without this test the design's cheapest claim is unverified
- [ ] T040 [US2] Integration-test that a **paused** run is still stoppable by the existing `POST {id}/stop` with normal semantics (FR-021), in the same file

**Checkpoint**: All four actions work; a parked roster can be put back to work.

---

## Phase 5: User Story 3 — An authored sequence raises its own alert (Priority: P3)

**Goal**: A `notify` action step in a sequence raises the same event with an author-written message.

**Independent test**: Author a sequence with a notify step, run it, observe the event with that
message; confirm a failed delivery does not fail the sequence.

> **All six registration sites below are mandatory.** Missing either `SequenceStepValidationService`
> or `FileSequenceRepository.ValidateActionPayloads` produces a 500 at save time instead of a clean
> rejection (`sequence-action-type-allowlists`); tracing `RescheduleSelf` found four more (research R8).

- [ ] T041 [US3] Add `public const string Notify = "notify";` with XML docs to `src/GameBot.Domain/Actions/ActionTypes.cs`
- [ ] T042 [US3] Add `ActionTypes.Notify` to the known-types set in `src/GameBot.Domain/Services/ActionPayloadValidationService.cs`
- [ ] T043 [US3] Add notify payload validation (`message` required, non-blank, ≤ 1000 chars; optional `url` absolute http/https; destination-required rule) to `src/GameBot.Domain/Services/SequenceStepValidationService.cs`, with the messages from `contracts/queue-api.md` §5
- [ ] T044 [US3] Add `ActionTypes.Notify` to the `ValidateActionPayloads` allow-list in `src/GameBot.Domain/Commands/FileSequenceRepository.cs`
- [ ] T045 [US3] Treat notify as a non-device step in `src/GameBot.Domain/Services/SequenceRunner.cs`, following how `RescheduleSelf` is handled at its two call sites — it must run even when the device is unreachable, which is the point of the step
- [ ] T046 [US3] Dispatch and execute the notify action in `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs` (alongside the `RescheduleSelf` branch): build a `sequence.notify` event, populate queue fields when running inside a queue and leave them null otherwise, and **always return success** regardless of delivery outcome (FR-024)
- [ ] T047 [P] [US3] Unit-test notify step validation (missing/blank/oversized message, bad url, no destination, valid cases) in `tests/unit/Sequences/NotifyStepValidationTests.cs`
- [ ] T048 [P] [US3] Contract-test that saving a sequence with a malformed notify step returns a clean 400 — **not a 500** — in `tests/contract/Sequences/NotifyStepSaveContractTests.cs`, the exact regression the allow-list lesson describes
- [ ] T049 [US3] Integration-test a notify step end to end in `tests/integration/Sequences/NotifyStepTests.cs`: the event carries the author's message and originating sequence, and a notifier that fails leaves the step and sequence successful

**Checkpoint**: Escalation can live in a committed sequence artifact.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T050 Correct the `QueueCycleLedger` class XML docs in `src/GameBot.Service/Services/QueueExecution/QueueCycleLedger.cs`: it remains a pure observer with void, non-throwing mutators and is still read by no scheduling decision *from inside*, but a separate evaluator now reads its published snapshot and may act. Leaving the current wording would make it misleading documentation (Constitution V)
- [ ] T051 Update `docs/architecture.md` on all four axes it changes — domain model (policy on the queue), API surface (`failurePolicy` body field, new health fields, `POST {id}/resume`), persistence (queue JSON), and the sequence action-type set (`notify`) — and refresh its "Last reviewed" date
- [ ] T052 [P] Set the `**Status**:` line in `specs/087-queue-failure-policy/spec.md` to Implemented, and add a row for 087 in `specs/STATUS.md`. Feature 086 is **extended, not superseded** — leave its Status unchanged
- [ ] T053 Run the full gate — `dotnet build` with zero warnings, plus the unit, contract and integration suites — and fix everything before the commit. A red result is a hard stop (Constitution, Definition of Done)
- [ ] T054 Write the PR perf note required by Constitution IV: per cycle, one extra uncontended lock acquisition and one integer compare, skipped entirely when no policy is configured; per episode, one HTTP request on a thread-pool thread the run never awaits; no hot path touched

---

## Dependencies

```
Phase 1 (Setup: T001–T002)
        │
        ▼
Phase 2 (Foundational: T003–T011)  ◄── BLOCKS every user story
        │
        ├─────────────┬─────────────┐
        ▼             ▼             ▼
   Phase 3 (US1)  Phase 4 (US2)  Phase 5 (US3)
    T012–T028b     T028a–T040     T041–T049
        │             │             │
        │             │             │  US3 needs only IFailureNotifier (T013–T015),
        │             │             │  not the evaluator — so it is independent of US2
        └─────────────┴─────────────┘
                      ▼
              Phase 6 (Polish: T050–T054)
```

**Story dependencies**:

- **US1** depends only on Phase 2. It is the MVP and is shippable alone.
- **US2** depends on Phase 2 and on the evaluator from US1 (T017) — it adds branches to it.
- **US3** depends on Phase 2 and on the *notifier* from US1 (T013–T015), but **not** on the evaluator.
  If US2 were dropped, US3 would still land.

**Critical ordering within US1**: T013/T014 (notifier) → T017 (evaluator) → T019 (run-loop call).
T019 is last because wiring an incomplete evaluator into the production run loop is the one change
here that can break running farms.

---

## Parallel Execution Opportunities

**Phase 2** — T003, T004 and T007 touch three different new files: run together. T010 and T011 are
independent test files: run together once T009 lands.

**Phase 3** — T012 and T013 are two new files: run together. T023, T024 and T025 are three
independent test files: run together once their subjects exist.

**Phase 4** — T036 and T037 in parallel.

**Phase 5** — T047 and T048 in parallel. Note T041–T046 are **strictly sequential** in practice:
they are six edits threading one new constant through a chain, and the later ones do not compile
until the earlier ones land.

**Phase 6** — T052 is independent of T050/T051.

---

## Implementation Strategy

**MVP = Phase 1 + Phase 2 + Phase 3 (T001–T028).** That is the whole of the issue's core complaint:
a queue that keeps failing tells someone. It is worth treating as a shippable milestone, because it
is also the phase that touches the production run loop — getting it green before adding actions and
a new action type keeps the risky change isolated and reviewable.

**Then Phase 4** for operators who want the roster to halt or park itself, **then Phase 5** for
escalation inside committed sequences, **then Phase 6** for the documentation and gate work the
constitution requires before this can be called Done.

**Risk note**: T019 and T033 are the only two tasks that modify `ExecuteRunAsync`, the method that
drives production farms and that the build-time analyzers already scale badly against. Both are
single-line calls by design. If either grows into a block during implementation, that is the signal
to move the logic back into the collaborator rather than to accept the inline code.

---

## Task Summary

| Phase | Tasks | Count |
|---|---|---|
| 1 — Setup | T001–T002 | 2 |
| 2 — Foundational | T003–T011 | 9 |
| 3 — US1 (P1, MVP) | T012–T028b (incl. T014a, T028b) | 19 |
| 4 — US2 (P2) | T028a–T040 (incl. T028a, T039a) | 14 |
| 5 — US3 (P3) | T041–T049 | 9 |
| 6 — Polish | T050–T054 | 5 |
| **Total** | | **58** |

Four tasks were added after the `/speckit-analyze` pass, all closing real coverage gaps rather than
adding scope: **T014a** (the application-log half of FR-015a, which was specified but untasked),
**T028a** (the `StopRequestedByPolicy` marker T030 needs and nothing created), **T028b** (FR-026 /
SC-008, previously unasserted), and **T039a** (FR-018a, the pause/timer property that justifies the
gate's placement). T028a carries a `T028`-adjacent id because it belongs to US2 but must be read
alongside the handle-state tasks; it is sequenced at the top of Phase 4.
