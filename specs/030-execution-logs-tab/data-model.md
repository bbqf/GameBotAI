# Data Model: Execution Logs Tab

## Entity: ExecutionLogEntry

- Purpose: Represents one row in the execution logs table.
- Fields:
  - `id` (string, required, unique): Stable identifier for the execution log entry.
  - `timestampUtc` (datetime, required): Canonical execution timestamp in storage.
  - `objectName` (string, required): Human-readable executed object name.
  - `status` (enum, required): Execution outcome state.
  - `hasSnapshot` (boolean, required): Indicates snapshot availability.
- Derived/UI fields:
  - `timestampLocalExact` (string): Exact local-time rendering.
  - `timestampRelative` (string): Relative-time rendering when toggle enabled.
- Validation rules:
  - `id` must be non-empty and unique.
  - `objectName` must be non-empty after trimming.
  - `status` must be one of allowed values.

## Entity: ExecutionLogQuery

- Purpose: Encapsulates combined sort/filter/page inputs for list retrieval.
- Fields:
  - `sortBy` (enum: `timestamp`, `objectName`, `status`; default `timestamp`).
  - `sortDirection` (enum: `asc`, `desc`; default `desc`).
  - `filterTimestamp` (string, optional): Case-insensitive contains filter.
  - `filterObjectName` (string, optional): Case-insensitive contains filter.
  - `filterStatus` (string, optional): Case-insensitive contains filter.
  - `pageSize` (integer, default `50`, max bounded by service policy).
  - `pageToken` (string, optional): Cursor/offset token for subsequent pages.
- Validation rules:
  - `pageSize` defaults to `50` when omitted.
  - Unknown `sortBy` values are rejected with validation error.
  - Empty/whitespace filters are treated as not set.

## Entity: ExecutionLogDetail

- Purpose: User-facing details for a selected execution log.
- Fields:
  - `executionId` (string, required): Reference to `ExecutionLogEntry.id`.
  - `summary` (string, required): Human-readable outcome summary.
  - `relatedObjects` (collection of `RelatedObjectLink`, optional).
  - `snapshot` (`SnapshotReference`, optional).
  - `stepOutcomes` (collection of `StepOutcome`, required, may be empty).
- Validation rules:
  - `summary` must be non-empty.
  - `stepOutcomes` must be present even if empty.

## Entity: RelatedObjectLink

- Fields:
  - `label` (string, required): Display name for non-technical users.
  - `targetType` (string, required): Object category (e.g., command/action/trigger).
  - `targetId` (string, required): Identifier used for navigation.
  - `isAvailable` (boolean, required): Availability flag.
  - `unavailableReason` (string, optional): User-readable reason when unavailable.

## Entity: SnapshotReference

- Fields:
  - `isAvailable` (boolean, required).
  - `imageUrl` (string, optional): Snapshot fetch URL when available.
  - `caption` (string, optional): User-facing context label.

## Entity: StepOutcome

- Fields:
  - `stepName` (string, required).
  - `status` (enum, required): Per-step outcome.
  - `message` (string, required): User-readable status detail.
  - `startedAtUtc` (datetime, optional).
  - `endedAtUtc` (datetime, optional).
- Validation rules:
  - `stepName` and `message` must be non-empty.

## Relationships

- `ExecutionLogEntry (1) -> (1) ExecutionLogDetail` by `id/executionId`.
- `ExecutionLogDetail (1) -> (0..n) RelatedObjectLink`.
- `ExecutionLogDetail (1) -> (0..1) SnapshotReference`.
- `ExecutionLogDetail (1) -> (0..n) StepOutcome`.

## Lifecycle / State Notes

- `ExecutionLogEntry.status` evolves by execution lifecycle and is persisted as final state for historical rows.
- `ExecutionLogQuery` is transient per request.
- `ExecutionLogDetail` is derived on read from persisted log data and associated references.
