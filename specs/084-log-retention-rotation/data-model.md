# Data Model: Execution Log Retention Default & Long-Run Rotation

## Changed: `ExecutionLogRetentionPolicy`

`src/GameBot.Domain/Logging/ExecutionLogRetentionPolicy.cs`

| Field | Type | Before | After | Notes |
|---|---|---|---|---|
| `RetentionDays` | `int` | default `60` | default `7` | Only the compile-time default changes. `Default => new()` static property picks it up automatically. Persisted policies (existing `execution-log-policy.json` files) are untouched — `ExecutionLogRetentionPolicyRepository.GetAsync` only falls back to `Default` when no file exists or it fails to parse. |

No validation changes: `SaveAsync`'s `Math.Max(1, policy.RetentionDays)` clamp is unaffected.

## Changed: `ExecutionLogEntry`

`src/GameBot.Domain/Logging/ExecutionLogModels.cs`

Two new optional fields, populated **only** on `ExecutionType == "queue"` root entries that participate in a rotation (all other entries — sequence/command entries, and queue-root entries whose run never rotated — leave both `null`, preserving current behavior/FR-014):

| Field | Type | Set when | Meaning |
|---|---|---|---|
| `RotatedToExecutionId` | `string?` | On the segment being closed out due to rotation | Id of the new queue-root entry that continues this run. |
| `RotatedFromExecutionId` | `string?` | On the new segment opened by rotation | Id of the previous queue-root entry this run continues from. |

A segment's own `FinalStatus`/`Summary`/`Details` at rotation time are also updated (via the existing upsert path) to state plainly that rotation occurred (closing side) or that this is a continuation (opening side) — see FR-008/FR-009. This reuses the entry's existing text fields; no new text-carrying field is introduced. Specifics as built:

- The closed segment takes `FinalStatus = "success"` (terminal). It cannot stay `"running"`: the run's eventual finalize targets the *newest* segment, so an earlier segment left `"running"` would dangle in that state forever.
- The closing marker is appended as the **last** `Details` item (kind `"rotation"`), and the continuation marker is the **first** `Details` item on the new segment — so the "last entry says it rotated / first entry says it continues" reading holds literally, not just in the summary. Prior details are capped at 9 on the closing side so trimming can never drop the marker.

No change to `ExecutionHierarchyContext`, `ExecutionObjectReference`, `ExecutionNavigationContext`, or any sequence/command-level model — rotation is purely a queue-root-entry-to-queue-root-entry link. A rotated run's sequence entries keep pointing at whichever root id (`RootExecutionId`) was active for their own firing; they are never retroactively repointed.

## Changed: `ExecutionLogEntryDto` / `ExecutionLogDetailDto`

`src/GameBot.Service/Models/ExecutionLogs.cs`

- `ExecutionLogEntryDto`: add `RotatedToExecutionId: string?` and `RotatedFromExecutionId: string?`, mirroring the domain model, so API consumers (including future tooling) can see the raw link without needing the detail endpoint.
- `ExecutionLogDetailDto`: no new field — instead, the existing `RelatedObjects: IReadOnlyList<RelatedObjectLinkDto>` list gains an additional entry when the underlying entry has a non-null `RotatedToExecutionId`/`RotatedFromExecutionId`:
  - Closing segment → `{ Label: "Continues in newer run segment", TargetType: "execution", TargetId: RotatedToExecutionId, IsAvailable: true, UnavailableReason: null }`
  - Opening segment → `{ Label: "Continued from earlier run segment", TargetType: "execution", TargetId: RotatedFromExecutionId, IsAvailable: true, UnavailableReason: null }`
  - `IsAvailable` reflects id presence rather than a target lookup, matching the existing "Parent execution" link built in the same pure-static projection.

This reuses the existing related-object-link shape (`RelatedObjectLinkDto`) end to end rather than inventing new response shape.

## New (in-memory only): rotation tracking on the active run

`src/GameBot.Service/Services/QueueExecution/QueueRunHandle` (existing type — extend, no new class)

| Field | Type | Notes |
|---|---|---|
| `RootExecutionId` | `string?` (existing, already declared `{ get; set; }` at `QueueRunHandle.cs:24`) | No declaration change needed. Today it is assigned once at run start; rotation additionally reassigns it, so the next `RunOneSequenceAsync` call (and anything reading the handle, e.g. the live monitor) sees the new active segment's root id. |

No new persisted entity is introduced for "run segment" as a first-class concept — a run segment *is* a queue-root `ExecutionLogEntry`, and the chain of segments for one queue run is reconstructed by following `RotatedFromExecutionId`/`RotatedToExecutionId` links starting from any segment in the chain. This keeps storage/query code (`FileExecutionLogRepository`) completely unchanged — it already loads/queries `ExecutionLogEntry` records without caring about these two new optional fields.

## Validation / invariants

- `RotatedToExecutionId` and `RotatedFromExecutionId` are mutually exclusive on brand-new (non-rotated) entries — both `null`.
- A closing segment's `RotatedToExecutionId` and the opening segment's `RotatedFromExecutionId` are set atomically as part of the same rotation operation (both entries persisted before the next sequence firing proceeds) so the chain is never observably one-directional.
- Rotation only ever runs for `ExecutionType == "queue"` root entries; it never touches sequence/command entries directly (FR-007).
