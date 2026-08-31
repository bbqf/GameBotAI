# Phase 1 Data Model: Concurrent Queue Execution

No persisted shape changes. Every type below is in-memory and dies with the process (FR-013).

---

## New types

### `DeviceContext` (`GameBot.Domain.Sessions`)

Immutable record identifying the device an execution flow is acting on.

| Field | Type | Notes |
|---|---|---|
| `SessionId` | `string` | Non-empty. The emulator session id the run holds. |
| `DeviceSerial` | `string?` | The ADB serial, when known. Diagnostics only; `SessionId` is the key. |

Invariants: immutable; `SessionId` never blank; safe to share across threads.

### `IDeviceContextAccessor` / `AsyncLocalDeviceContextAccessor` (`GameBot.Domain.Sessions`)

| Member | Signature | Behavior |
|---|---|---|
| `Current` | `DeviceContext?` | The context of the current execution flow, or `null` outside any run. |
| `Push` | `IDisposable Push(DeviceContext context)` | Sets `Current` for this flow; disposing restores the previous value. |

Invariants:

- `AsyncLocal<T>` semantics: the value flows into `await` continuations and into `Task.Run`
  (ExecutionContext capture), and a value set inside a child flow does not leak back to the parent.
- Push/dispose nest correctly; disposing twice is a no-op.
- Never `null`-pushed: pushing requires a non-null context.

### `IScreenSourceFactory` (`GameBot.Domain.Triggers.Evaluators`)

| Member | Signature | Behavior |
|---|---|---|
| `ForSession` | `IScreenSource ForSession(string sessionId)` | An `IScreenSource` bound to exactly that session for its lifetime. |

Invariants: the returned source never observes another session; when that session has no cached frame,
or is no longer running, it returns `null` (FR-003) rather than substituting.

### `SessionScopedScreenSource` (`GameBot.Emulator.Session`)

Implements `IScreenSource` over `BackgroundScreenCaptureService.GetCachedFrame(sessionId)` for one
fixed session id. Decodes from the frame's immutable PNG bytes exactly as
`BackgroundCaptureScreenSource` does today, so the concurrent-disposal hazard stays handled.

### `BackgroundCaptureScreenSourceFactory` (`GameBot.Emulator.Session`)

Implements `IScreenSourceFactory` by constructing a `SessionScopedScreenSource` per call.

### `IDeviceClaimRegistry` / `DeviceClaimRegistry` (`GameBot.Service.Services.QueueExecution`)

| Member | Signature | Behavior |
|---|---|---|
| `TryClaim` | `bool TryClaim(string serial, string queueId, string queueName)` | Atomic. `false` when another queue holds the serial. `true` (no-op claim) when `serial` is blank. |
| `Release` | `void Release(string serial, string queueId)` | Removes the claim only if it is still held by `queueId`. Idempotent. |
| `TryGetHolder` | `bool TryGetHolder(string serial, out DeviceClaim claim)` | For building the refusal message. |

### `DeviceClaim` (`GameBot.Service.Services.QueueExecution`)

| Field | Type | Notes |
|---|---|---|
| `DeviceSerial` | `string` | Normalized (trimmed) form of the claimed serial. |
| `QueueId` | `string` | The holding queue. |
| `QueueName` | `string` | For the operator-facing message. |
| `ClaimedAtUtc` | `DateTimeOffset` | Diagnostics. |

Invariants:

- Key normalization: `serial.Trim()`, compared with `StringComparer.OrdinalIgnoreCase` (FR-008).
- At most one claim per normalized serial at any instant (FR-008, FR-012).
- A blank serial is never stored, so blank-serial queues never block each other (research R5).
- Claims are never persisted and never survive a restart (FR-013).

---

## Changed types

### `QueueStartOutcome` (`GameBot.Service.Services.QueueExecution`)

Adds one member:

| Member | Meaning |
|---|---|
| `DeviceInUse` | The queue's emulator serial is claimed by a different running queue (FR-009, FR-010). |

`Started`, `NotFound` and `AlreadyRunning` keep their exact meanings, so `already_running` stays
distinguishable from the new refusal (FR-010).

### `SessionOptions` (`GameBot.Emulator.Session`)

| Field | Before | After |
|---|---|---|
| `MaxConcurrentSessions` | `3` | `8` (FR-015) |

Still bound from `Service:Sessions:MaxConcurrentSessions`; an explicit configured value wins.

### `QueueMonitorSnapshot` / `QueueMonitorResponse`

Adds one optional field:

| Field | Type | Notes |
|---|---|---|
| `DeviceSerial` | `string?` | The serial the run holds; `null` when the queue is not running (FR-018). |

Additive and nullable, so existing clients are unaffected.

### `IGameReadinessProbe.WaitUntilReadyAsync`

Gains a `string? sessionId` parameter (defaulted, so existing call sites compile) used to obtain a
session-scoped screen source; `null` keeps the ambient/single-session resolution.

---

## State transitions

### Device claim lifecycle

```text
        TryClaim(serial, queueA)            run ends (any reason)
unclaimed ─────────────────────► claimed(queueA) ─────────────────────► unclaimed
     ▲                                │
     │       TryClaim(serial, queueB)  │
     └────────── refused ◄─────────────┘   (DeviceInUse; queueB stays Stopped)
```

- Entry: `QueueExecutionService.StartAsync`, after the run registry accepts the queue.
- Exit: `RunAsync`'s `finally`, which already covers completion, manual stop, failure, cancellation
  and host shutdown (FR-011).
- Failure-to-launch path: if the claim is refused, the run-registry entry is removed and the CTS
  disposed before returning, leaving no residue (edge case: "run fails before it finishes starting").

### Device context lifetime

```text
run starts ─► (per sequence firing) Push(DeviceContext) ─► sequence executes ─► Dispose ─► next firing
```

The context is pushed per firing rather than once per run so that a firing that spawns its own flows
(loops, nested sequences, command execution) inherits it, while the run loop's own scheduling code
between firings carries no context.
