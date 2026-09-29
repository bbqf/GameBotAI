# Research: Keep the parameter values of a queue entry on a run that reschedule-self starts

The Technical Context of the plan has no "NEEDS CLARIFICATION" item. The items below record the decisions that the code trace gave.

## R-001: Where the scope gets lost

- **Decision**: The scope gets lost on the side that makes the booking. `SelfRescheduleCoordinator.ScheduleSelf` makes each `SelfRescheduleEntry` with no `Scope`, because `ISelfRescheduleCoordinator.ScheduleSelf` has no scope parameter and `SequenceExecutionService.DispatchSelfReschedule` has no scope to send.
- **Rationale**: The side that fires the booking is already correct. `QueueExecutionService` fires each booking with `entry.Scope ?? queueScope` in the four drains (EveryStep injections, next-cycle-start, self-reschedule Timer, once-per-run). Feature 078 FR-015 added `SelfRescheduleEntry.Scope` for this purpose. Only the at-start hold path (feature 106) sets it now, with `EntryScope(entry)`.
- **Alternatives considered**: Rebuild the scope in the run loop from the template entry of the same sequence. Rejected: a sequence can have more than one template entry with different values (feature 078 FR-012a), so the run loop cannot know which entry is correct. Also, a live schedule run has no entry.

## R-002: Which scope object the booking keeps

- **Decision**: Keep the `scope` argument of `SequenceExecutionService.ExecuteAsync` (the scope that the queue run loop gave the run). Do not keep the `runScope` that `SequenceRunner` makes with the sequence layer.
- **Rationale**: When the booked run starts, `SequenceRunner.ExecuteAsync` adds the sequence layer (`.Child(ParameterScopeLayers.Sequence, null, sequence.Parameters)`) again. The given scope thus makes the same layers in the booked run as in the booking run (FR-002). This is also the scope that the clarification selects: "the full scope that the service gave the run that booked it".
- **Alternatives considered**: (a) Keep only the entry layer and build the queue layer again at fire time. Rejected: the clarification selects the full scope, and a new queue layer could differ from the layer of the booking run (FR-008). (b) Keep the step scope at the `reschedule-self` step. Rejected: it has the sequence layer and loop layers, and the runner would add a second sequence layer.

## R-003: How the scope gets to the coordinator

- **Decision**: Add an optional last parameter `ParameterScope? scope = null` to `ISelfRescheduleCoordinator.ScheduleSelf`. Add a `ParameterScope scope` parameter to the private methods `DispatchActionAsync` and `DispatchSelfReschedule` in `SequenceExecutionService`. The action dispatcher lambda in `ExecuteCoreAsync` already captures `scope`, so it sends it.
- **Rationale**: The smallest change. The interface is `internal`, and its only production caller is `DispatchSelfReschedule`. The optional parameter keeps all current test calls (about 30 calls in `tests/unit/Queues`) the same. Null means "no kept scope", and the run loop then uses the queue scope (FR-007).
- **Alternatives considered**: (a) An ambient `AsyncLocal` scope. Rejected: hidden data flow, and the codebase uses explicit parameters for scope. (b) Add the scope to `ExecutionLogContext` or to `SequenceActionPayload`. Rejected: the log context is for log data, and the payload is authored data (FR-006).

## R-004: Scope of a replaced booking

- **Decision**: No new logic. The scope goes with the booking.
- **Rationale**: `EveryStepInjections[sequenceId] = entry` and `AddTimerFiring` (remove all of the same sequence, then add) already replace the booking for each sequence. The new entry has the new scope, so the most recent booking wins (FR-010 and clarification 3). `RearmTimerFiring` puts back the same entry object, so a held booking keeps its scope (edge case "liveness gate").

## R-005: Nested sequences

- **Decision**: No special handling.
- **Rationale**: The runner runs a nested sequence inside the same `ExecuteCoreAsync` call. The dispatcher lambda sends the top-level `sequenceId` and the top-level `scope`. A `reschedule-self` step thus books the top-level sequence with the top-level scope, which is the same as the current behavior for the sequence id.

## R-006: Test harness facts

- **Decision**: Use the `QueueExecutionServiceTests` harness (`FakeSequenceExecution.Scopes` records the scope of each firing, `h.Coordinator` is the real coordinator, `FakeTimeProvider` controls Timer bookings) for the unit tests. Use `SelfRescheduleRunIntegrationTests` (real DI graph, `GAMEBOT_USE_ADB=false`, `TestEnvironment.PrepareCleanDataDir()`) for the end-to-end test.
- **Rationale**: No unit test makes a real `SequenceExecutionService`, so the real dispatch path is only in the integration project. Integration tests isolate the data directory; contract tests do not, so do not add a contract test that persists queues.
- **Alternatives considered**: A contract test through the HTTP API. Rejected: it needs a running queue with a device and it pollutes the shared contract data directory.
