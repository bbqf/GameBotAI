# Data Model

## Entities

### ExecutionLogEntry
- `id`: string (unique immutable identifier)
- `timestampUtc`: datetime (required; execution event time)
- `executionType`: enum [`command`, `sequence`] (required)
- `finalStatus`: enum [`success`, `failure`] (required)
- `objectRef`: `ExecutionObjectReference` (required)
- `navigation`: `ExecutionNavigationContext` (required)
- `hierarchy`: `ExecutionHierarchyContext` (required)
- `summary`: string (required; concise user-facing text)
- `details`: array of `ExecutionDetailItem` (required; may be empty)
- `stepOutcomes`: array of `ExecutionStepOutcome` (required; may be empty)
- `retentionExpiresUtc`: datetime (required)

Validation rules:
- `summary` MUST be concise and non-empty.
- `finalStatus` MUST be `success` or `failure`.
- Sensitive values MUST be masked/redacted in `details` before persistence.

### ExecutionObjectReference
- `objectType`: enum [`command`, `sequence`] (required)
- `objectId`: string (required)
- `displayNameSnapshot`: string (required)
- `versionSnapshot`: string? (optional, if available)

Validation rules:
- `objectId` + `timestampUtc` MUST be sufficient for user identification.
- Snapshot values are immutable once persisted.

### ExecutionNavigationContext
- `directPath`: string (required; relative route to directly executed object)
- `parentPath`: string? (optional; relative route to parent sequence when nested)
- `pathKind`: enum [`relative-route`] (required)

Validation rules:
- Paths MUST be relative and MUST NOT include scheme/host/port.
- For nested execution, `parentPath` SHOULD be present.

### ExecutionHierarchyContext
- `rootExecutionId`: string (required)
- `parentExecutionId`: string? (optional for top-level)
- `depth`: integer (required, >= 0)
- `sequenceIndex`: integer? (optional, order within parent sequence)

Validation rules:
- `depth = 0` implies no parent.
- Child entries MUST reference existing parent/root identifiers in same persisted set.

### ExecutionStepOutcome
- `stepOrder`: integer (required, >= 0)
- `stepType`: string (required; e.g., action/command/primitiveTap)
- `outcome`: enum [`executed`, `not_executed`] (required)
- `reasonCode`: string? (optional, machine-friendly)
- `reasonText`: string? (optional, user-facing)

Validation rules:
- `outcome=not_executed` SHOULD include `reasonText`.

### ExecutionDetailItem
- `kind`: string (required; e.g., `detection`, `tap`, `sequence`)
- `message`: string (required; user-facing concise statement)
- `attributes`: map<string, primitive> (optional; masked values only)
- `sensitivity`: enum [`normal`, `masked`, `redacted`] (required)

Validation rules:
- `message` MUST not expose raw sensitive data.
- `attributes` values MUST already be sanitized.

### ExecutionRetentionPolicy
- `enabled`: boolean (required)
- `retentionDays`: integer (required when enabled, > 0)
- `cleanupIntervalMinutes`: integer (required, > 0)
- `updatedAtUtc`: datetime (required)

Validation rules:
- Policy changes apply to subsequent cleanup cycles.

## Relationships
- One `ExecutionLogEntry` has one `ExecutionObjectReference`.
- One `ExecutionLogEntry` has one `ExecutionNavigationContext`.
- One `ExecutionLogEntry` has one `ExecutionHierarchyContext`.
- One `ExecutionLogEntry` has many `ExecutionStepOutcome` and many `ExecutionDetailItem`.
- `ExecutionRetentionPolicy` governs expiration and cleanup for all `ExecutionLogEntry` records.

## State Transitions

### ExecutionLogEntry.finalStatus
- Start: pending in-memory execution context
- End: `success` when execution completes as intended
- End: `failure` when execution fails or terminally cannot proceed

### ExecutionStepOutcome.outcome
- Start: step evaluated
- End: `executed` when step action was performed
- End: `not_executed` when step was skipped/blocked (for example detection below threshold)

### Retention lifecycle
- On write: compute `retentionExpiresUtc` from active policy
- During cleanup cycle: entries with `retentionExpiresUtc <= now` become purge candidates
- On purge: entry removed from durable storage

## Query Model
- List endpoint supports filters by:
  - time range (`fromUtc`, `toUtc`)
  - `finalStatus`
  - `objectType`
  - `objectId`
- Pagination fields:
  - `pageSize`
  - `cursor` (opaque continuation token)
- Default ordering: newest first by `timestampUtc`