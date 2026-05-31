# Phase 1 Data Model: Emulator Execution Queue

## Domain models (`src/GameBot.Domain/Queues`)

### ExecutionQueue (persisted)

The durable configuration of a queue. Serialized to `{storageRoot}/queues/{id}.json`.

| Field | Type | Notes |
|-------|------|-------|
| `Id` | `string` | GUID ("N" format) generated on create if absent; safe-id pattern `^[A-Za-z0-9_-]+$` |
| `Name` | `string` | Required, non-empty (FR-002, FR-008) |
| `EmulatorSerial` | `string` | Required ADB device serial; immutable after create (FR-003, FR-004) |
| `CycleExecution` | `bool` | Stored flag only; default `false` (FR-002) |
| `CreatedAt` | `DateTimeOffset?` | Set on create |
| `UpdatedAt` | `DateTimeOffset?` | Set on update |

**Validation**: `Name` and `EmulatorSerial` required on create. `Id` must match safe-id pattern for file storage. Entries and status are **not** fields of this model — they live in the runtime store and are never serialized.

### QueueExecutionStatus (enum)

```
enum QueueExecutionStatus { Stopped, Running }
```

`Stopped` is the default/reset state (FR-014, FR-022). **Terminology mapping**: `Stopped` is the canonical representation of the spec's "not running" state and `Running` of "running". These are the literal values serialized by the API (`"Stopped"` / `"Running"`) and the labels shown by the UI status chip, so the spec wording and the implementation stay aligned.

### QueueEntry (in-memory)

An ordered reference to a sequence within a queue. Lives only in `QueueRuntimeStore`.

| Field | Type | Notes |
|-------|------|-------|
| `EntryId` | `string` | GUID; identifies the entry for removal (allows duplicate `SequenceId`, FR-013) |
| `SequenceId` | `string` | References a `CommandSequence.Id` |

Order is the list position; new entries are appended (FR-010). Resolution of `SequenceId` → name / stale happens at response-build time, not stored on the entry.

### QueueRuntimeState (in-memory, per queue)

```
class QueueRuntimeState {
  List<QueueEntry> Entries;          // ordered
  QueueExecutionStatus Status;       // default Stopped
}
```

Held in `QueueRuntimeStore` as `ConcurrentDictionary<string /*queueId*/, QueueRuntimeState>`. Not persisted; empty after restart (FR-021, FR-022).

## Persistence contract — `IQueueRepository`

```csharp
public interface IQueueRepository {
  Task<ExecutionQueue?> GetAsync(string id);
  Task<IReadOnlyList<ExecutionQueue>> ListAsync();
  Task<ExecutionQueue> CreateAsync(ExecutionQueue queue);
  Task<ExecutionQueue> UpdateAsync(ExecutionQueue queue);
  Task<bool> DeleteAsync(string id);
}
```
`FileQueueRepository(string dataRoot)` stores under `Path.Combine(dataRoot, "queues")`, reusing the safe-path/JSON-options approach of `FileSequenceRepository`.

## Runtime contract — `IQueueRuntimeStore`

```csharp
public interface IQueueRuntimeStore {
  IReadOnlyList<QueueEntry> GetEntries(string queueId);
  QueueEntry AddEntry(string queueId, string sequenceId);   // appends
  bool RemoveEntry(string queueId, string entryId);
  QueueExecutionStatus GetStatus(string queueId);           // default Stopped
  void SetStatus(string queueId, QueueExecutionStatus status);
  void Remove(string queueId);                              // on queue delete
}
```

## Service DTOs (`src/GameBot.Service/Contracts/Queues`)

### CreateQueueRequest
```
{ "name": string, "emulatorSerial": string, "cycleExecution": bool }
```

### UpdateQueueRequest  (emulator NOT included — immutable)
```
{ "name": string, "cycleExecution": bool }
```

### AddQueueEntryRequest
```
{ "sequenceId": string }
```

### QueueResponse  (list item)
```
{
  "id": string,
  "name": string,
  "emulatorSerial": string,
  "cycleExecution": bool,
  "status": "Stopped" | "Running",
  "entryCount": number
}
```

### QueueDetailResponse  (single queue)
```
{
  ...QueueResponse,
  "entries": [
    { "entryId": string, "sequenceId": string, "sequenceName": string | null, "stale": bool }
  ]
}
```
`sequenceName` is resolved from `ISequenceRepository`; `stale: true` when the referenced sequence no longer exists (FR-013b).

## Frontend types (`src/web-ui/src/services/queues.ts`)

```ts
export type QueueStatus = 'Stopped' | 'Running';

export type QueueDto = {
  id: string; name: string; emulatorSerial: string;
  cycleExecution: boolean; status: QueueStatus; entryCount: number;
};

export type QueueEntryDto = {
  entryId: string; sequenceId: string; sequenceName: string | null; stale: boolean;
};

export type QueueDetailDto = QueueDto & { entries: QueueEntryDto[] };

export type QueueCreate = { name: string; emulatorSerial: string; cycleExecution: boolean };
export type QueueUpdate = { name: string; cycleExecution: boolean };
```

## State transitions

```
            start                         (entries: in-memory, lost on restart)
 Stopped ───────────▶ Running
   ▲                    │
   └──────── stop ──────┘
 (idempotent both ways; start/stop allowed regardless of emulator connectivity)

 Restart: every queue → Stopped, entries → []   (config preserved)
```

## Edit/delete guards (status-dependent)

| Action | Stopped | Running |
|--------|---------|---------|
| Rename / toggle cycle (PUT) | allowed | **blocked** (FR-005a) |
| Delete | allowed | **blocked** (FR-005a) |
| Add / remove entry | allowed | allowed (FR-013a) |
| Start / stop | allowed | allowed (idempotent) |
