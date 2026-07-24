# Phase 1 Data Model: Queue Emulator-Instance Cold-Start

## Entity: ExecutionQueue (extended)

Persisted queue configuration (`src/GameBot.Domain/Queues/ExecutionQueue.cs`). Two new optional fields.

| Field | Type | Default | Persisted | Notes |
|-------|------|---------|-----------|-------|
| `EmulatorInstanceName` | `string?` | `null` | yes | LDPlayer instance name to cold-start at queue run start. Null/blank ⇒ not used. |
| `EmulatorInstanceIndex` | `int?` | `null` | yes | LDPlayer instance index. Null ⇒ not used. Must be ≥ 0 when set. |

Existing fields used by this feature: `EmulatorSerial` (the device serial reused for the feature-070
responsiveness probe).

**Validation rules** (enforced at the API boundary, reusing feature-070 semantics via
`EnsureEmulatorRunningArgs.TryCreate` at runtime):
- Both fields optional. When **neither** is set, the queue performs no emulator management (total no-op).
- When set: `EmulatorInstanceIndex`, if supplied, MUST be ≥ 0 (negative rejected at create/update).
- When both name and index are supplied, **name takes precedence** (consistent with feature 070/071).
- No new constraint couples the instance fields to `EmulatorSerial` beyond what exists today; the serial
  is already required to create a queue.

**Persistence / back-compat**: `FileQueueRepository` serializes the whole `ExecutionQueue` via
System.Text.Json. Nullable properties absent from an older queue's JSON deserialize to `null`, so
pre-existing queues load as "unset" with no migration (FR-014).

**Mutability**: Settable on both create and update (mirrors the mutable idle-pause fields), so an
operator can enable cold-start on an existing queue without recreating it. (`EmulatorSerial` itself
remains immutable-after-create; these instance fields are additive config.)

## Runtime concept: Pre-Session Emulator Cold-Start

Not persisted. Computed once per queue run in `QueueExecutionService.RunAsync`, before `CreateSession`.

**Input**: the queue's `EmulatorInstanceName`/`EmulatorInstanceIndex` + `EmulatorSerial`.

**Precondition to run**: at least one instance identifier is set AND the injected
`IEnsureEmulatorRunningActionHandler` is non-null. Otherwise skipped (no-op; behave as today).

**Outcome mapping** (from feature-070 `EnsureEmulatorRunningActionResult`):

| Feature-070 outcome | `IsSuccess` | `IsUnsupported` | Run behavior |
|---------------------|:-----------:|:---------------:|--------------|
| `already_healthy` | ✅ | | Create session, run normally (no restart). |
| `started` | ✅ | | Create session, run normally. |
| `restarted` | ✅ | | Create session, run normally. |
| `platform_unsupported` | | ✅ | **Proceed**: create session as today (neutral not-applied). |
| `control_unavailable` | | ✅ | **Proceed**: create session as today (neutral not-applied). |
| `recovery_timed_out` | | | **Fail run**: reason set, **no session created**. |
| `instance_not_found` | | | **Fail run**: reason set, **no session created**. |

**Failure reason (actionable)**: e.g. `emulator instance ('<name-or-#index>') could not be started: <reasonCode>`.

**Ordering guarantee**: the ensure completes before any call to `_sessions.CreateSession`; on a genuine
failure `sessionId` stays null so the existing `if (sessionId is not null)` guard skips the entire
sequence-running phase and the run ends as `Failure` (FR-002/FR-007/FR-013).

## State / flow

```text
queue run start
  ├─ template resolved (existing)
  ├─ [NEW] instance identifier set?
  │     ├─ no  → skip (no emulator work)                         ── warm/legacy path, unchanged
  │     └─ yes → ensure-emulator-running(instance, serial)
  │             ├─ success / unsupported → continue
  │             └─ recovery_timeout / not_found → Failure, STOP (no session)
  ├─ CreateSession(EmulatorSerial)   (existing; now reached even from a cold emulator)
  └─ run sequences (existing)
```
