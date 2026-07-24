# Phase 0 Research: Session-Less (Cold) Emulator Start at Queue Startup

## Decision 1 — Integration point: queue startup, before session creation

**Decision**: Perform the emulator cold-start inside `QueueExecutionService.RunAsync`, immediately
before the existing step 2 (`_sessions.CreateSession(queue.EmulatorSerial)`).

**Rationale**: Tracing the cold path shows the queue is where it breaks. `RunAsync` creates the
device-bound session up front; `SessionManager.CreateSession` (with ADB enabled) calls
`ResolveOrValidateDeviceSerial`, which throws `no_adb_devices` when no device is present — i.e. when
the emulator is closed. The run is then marked `Failure` ("emulator could not be reached") and never
runs the sequences that ensure the game is running. The queue is exactly "the thing that makes sure the
game is running" at runtime, so healing the emulator at queue startup — before the session — is the
correct and minimal integration point.

**Alternatives considered**:
- *Lazy/per-step session resolution in `CommandExecutor`* (the initial clarify hypothesis): addresses
  the standalone command/sequence dispatch path (`ForceExecuteDetailedAsync` resolves a session up
  front and throws `missing_session_context`). Rejected as the primary fix because it does **not**
  unblock the scheduled-queue automation the user actually runs — the queue fails earlier, at
  `CreateSession`, before any command/sequence dispatch. Left as possible future work (spec Assumptions,
  out of scope).
- *A new dedicated "cold start" endpoint/tool*: rejected — the user explicitly wants this "integrated
  into the current commands/sequences that make sure the game is running," and a parallel path would
  not self-heal scheduled runs.
- *Auto-starting the emulator implicitly for every queue*: rejected — must be opt-in and backward
  compatible; a queue with no instance identifier does no emulator work.

## Decision 2 — Reuse feature-070 `ensure-emulator-running` wholesale

**Decision**: Call the existing internal `IEnsureEmulatorRunningActionHandler.ExecuteAsync(
EnsureEmulatorRunningArgs, ct)` with args built from the queue's instance identifier + `EmulatorSerial`.

**Rationale**: Feature 070 already implements health-probe, start, restart-on-hang, bounded boot wait,
graceful degradation, and the outcome taxonomy (already_healthy / started / restarted /
recovery_timed_out / instance_not_found / platform_unsupported / control_unavailable). Reusing it
guarantees identical semantics and requires no new emulator machinery or configuration (FR-011,
SC-008). The handler and its result type are `internal` to `GameBot.Service`, same assembly as
`QueueExecutionService`, so it can be injected and consumed directly.

**Outcome → run behavior mapping**:
- `IsSuccess` (already_healthy / started / restarted) → proceed to `CreateSession`.
- `IsUnsupported` (platform_unsupported / control_unavailable) → **proceed** to `CreateSession` (neutral
  not-applied; preserves today's behavior on non-Windows / missing tooling — FR-008).
- Otherwise (recovery_timed_out / instance_not_found) → **fail the run** with a clear reason and do
  **not** create a session (FR-002/FR-007/FR-013).

**Alternatives considered**: reimplementing a lighter probe inline — rejected (duplication, drift from
070 semantics).

## Decision 3 — Opt-in via optional queue-config fields (name or index)

**Decision**: Add `EmulatorInstanceName` (string?) and `EmulatorInstanceIndex` (int?) to
`ExecutionQueue`, both optional/nullable, reusing the queue's existing `EmulatorSerial` for the probe.
The pre-session ensure runs only when at least one identifier is set.

**Rationale**: The queue currently carries only `EmulatorSerial`, not an LDPlayer instance identity.
Feature 070/071 identify an instance by name or index; mirroring that on the queue keeps parity and
lets `EnsureEmulatorRunningArgs.TryFrom`/validation reuse the same rules (name-or-index required,
index ≥ 0, name precedence). Optional + null default means existing queues are a total no-op (FR-014),
and `FileQueueRepository` serializes the whole `ExecutionQueue`, so nullable properties round-trip with
back-compat for free.

**Alternatives considered**:
- *Derive the instance from the serial*: rejected — no reliable serial→instance mapping; feature 070
  explicitly does not auto-discover.
- *Reuse a single string field for both name and index*: rejected — 070's args model distinguishes
  name vs index; keeping two fields matches and validates cleanly.

## Decision 4 — Inject the handler as an optional nullable ctor parameter

**Decision**: Add `IEnsureEmulatorRunningActionHandler? ensureEmulatorRunning = null` as the last
`QueueExecutionService` constructor parameter; when null (tests that omit it), skip the pre-session
ensure (degrade to today's behavior).

**Rationale**: Mirrors the existing optional `IEnsureGameRunningActionHandler?` parameter. DI already
registers the handler as a singleton, so production wiring is automatic; existing test constructions
that don't pass it keep compiling. New tests inject a fake to drive the behavior matrix.

**Alternatives considered**: a required parameter — rejected (breaks every existing
`QueueExecutionService` test construction unnecessarily).

## Decision 5 — Observability

**Decision**: Log the cold-start outcome via the existing queue execution logger (a new
`LoggerMessage`), and set an actionable `failureReason` on genuine failure so the queue's recorded stop
reason explains it. No new execution-log entry type.

**Rationale**: FR-010 requires the outcome be observable after the fact; the queue already records a
stop reason and structured logs. Keeping to the existing channels avoids new log schema.

## Reused / referenced components (no change)

- `GameBot.Service.Services.EnsureEmulatorRunning.*` (handler, control, probe, result) — feature 070.
- `GameBot.Domain.Actions.EnsureEmulatorRunningArgs` — args + `TryCreate` validation (name-or-index,
  index ≥ 0).
- Queue config plumbing pattern — feature 073 (`PauseWhenIdle`/`IdleThresholdSeconds`).

## Open questions

None. All NEEDS CLARIFICATION resolved in the spec's Clarifications (Session 2026-07-24).
