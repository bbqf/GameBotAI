# Data Model — Runtime Logging Control (001)

## Overview
Runtime logging control builds on a persisted configuration document plus in-memory level switches. The model must capture component identities, enabled flags, effective/default levels, provenance metadata, and snapshots for auditing/GET responses. The dataset is tiny (≤20 components) but requires atomic updates and ordering guarantees (last write wins).

## Entities

### LoggingComponentSetting
| Field | Type | Description | Notes |
|-------|------|-------------|-------|
| `name` | string | Fully qualified logger category (`GameBot.Domain.Triggers`, `Microsoft.AspNetCore.Routing`) | Required, case-sensitive |
| `enabled` | bool | Whether logs from this component may emit (`true` default) | Required |
| `effectiveLevel` | string enum (`Debug`,`Information`,`Warning`,`Error`,`Critical`) | Current minimum severity applied at runtime | Required |
| `defaultLevel` | string enum | Baseline severity (always `Warning` unless per-component policy changes later) | Required |
| `lastChangedBy` | string | Operator or automation identifier | Optional but stored when updates occur |
| `lastChangedAtUtc` | string (ISO-8601) | Timestamp of last modification | Optional at bootstrap, required after first change |
| `source` | string | `default`, `api`, or `file` to show origin of current values | Defaults to `default` |
| `notes` | string | Optional free-form reason surfaced to GET clients | Optional |

Validation rules:
- `name` must match configured component catalog; reject unknown names.
- `effectiveLevel` must be ≥ `defaultLevel` severity if `defaultLevel` was intentionally constrained (prevents more verbose than allowed) unless override explicitly opts in via `allowVerbose=true` (future flag, not part of schema yet).
- Duplicate `name` entries per snapshot are invalid.

### LoggingPolicySnapshot
Represents the persisted JSON document and GET response payload.

| Field | Type | Description | Notes |
|-------|------|-------------|-------|
| `id` | string | Constant (`"logging-policy"`) | Primary key |
| `version` | string | Schema version (`"1.0"`) | For migrations |
| `appliedAtUtc` | string | Timestamp when snapshot was last written to disk | Required |
| `appliedBy` | string | Last actor touching the snapshot | Optional |
| `components` | array<`LoggingComponentSetting`> | Component-level overrides | Required, min 1 entry |
| `defaultLevel` | string enum | Global fallback level (`Warning`) | Required |
| `allEnabled` | bool | Mirror of “bulk enabled state” used for reset operations | Derived |

Validation:
- `components` must include every known component so GET responses are complete.
- When bulk reset occurs, `components.effectiveLevel` must equal `components.defaultLevel` for every entry.
- `appliedAtUtc` must always move forward (monotonic) to keep audit ordering.

### LoggingChangeAudit
| Field | Type | Description |
|-------|------|-------------|
| `id` | string GUID | Unique audit identifier |
| `component` | string | Target component or `*` for bulk actions |
| `action` | string | `set-level`, `toggle-enabled`, `reset-defaults` |
| `previousState` | object | Free-form JSON summarizing prior enabled + level |
| `newState` | object | Free-form JSON summarizing resulting state |
| `actor` | string | Operator or automation source |
| `occurredAtUtc` | string | Timestamp |

Audit entries are append-only and allow security review plus rollback guidance.

## Relationships & Flow
1. `LoggingPolicySnapshot` is persisted in `data/config/logging.json` and loaded during startup.
2. API `GET /config/logging` returns the latest snapshot (or merges runtime overrides if pending changes exist but not yet saved).
3. API `PUT /config/logging` accepts changes, validates them, writes the snapshot, updates in-memory component switches, and emits a `LoggingChangeAudit` entry per component touched.
4. `LoggingComponentSetting` objects also live in memory (dictionary keyed by `name`) to drive `LoggerFilterRule` updates immediately when changes land.

## State Transitions
1. **Bootstrap**: If no snapshot exists, create one using repository defaults (all components enabled, level Warning) and mark `source=default`.
2. **Override Applied**: Validate payload → update relevant `LoggingComponentSetting` rows → persist snapshot → refresh runtime switches.
3. **Component Disabled/Enabled**: Toggle `enabled`, persist snapshot, update runtime switch to short-circuit emission.
4. **Bulk Reset**: Endpoint sets every `effectiveLevel=defaultLevel` and `enabled=true`, updates `appliedAtUtc`, and writes a single audit event referencing `component="*"`.
5. **Reload After Failure**: On service restart, snapshot is reloaded; any partial in-memory states are discarded, guaranteeing eventual consistency with persisted file.
