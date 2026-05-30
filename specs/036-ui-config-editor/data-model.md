# Data Model: UI Configuration Editor

**Feature**: 035-ui-config-editor
**Date**: 2026-04-14

## Entities

### ConfigurationParameter (existing — no changes)

A single configuration setting known to the backend.

| Field | Type | Description |
|-------|------|-------------|
| Name | string (required, unique key) | Parameter identifier (e.g., `GAMEBOT_TESSERACT_LANG`, `Service__Detections__Threshold`) |
| Source | string enum: `Default`, `File`, `Environment` | Where the effective value originates |
| Value | any (nullable) | Current effective value; masked to `"***"` for secret parameters |
| IsSecret | bool | Whether the parameter name matches secret markers (TOKEN, SECRET, PASSWORD, KEY) |

**Validation rules**:
- Name must be non-empty.
- Source must be one of the three enum values.
- Value is masked in responses if IsSecret is true (existing `ConfigurationMasking` logic).

### ConfigurationSnapshot (existing — no structural changes)

A point-in-time view of all configuration parameters.

| Field | Type | Description |
|-------|------|-------------|
| GeneratedAtUtc | DateTimeOffset | Timestamp of snapshot generation |
| ServiceVersion | string? | Assembly version of the running service |
| DynamicPort | int? | Dynamic port if assigned |
| RefreshCount | int | Number of times config has been refreshed |
| EnvScanned | int | Total environment variables scanned |
| EnvIncluded | int | Environment variables included (GAMEBOT_ prefix) |
| EnvExcluded | int | Environment variables excluded |
| Parameters | Dictionary<string, ConfigurationParameter> | Ordered map of all parameters (key order = display order) |

**State transitions**: None — snapshots are immutable point-in-time views. Each update/reorder produces a new snapshot.

### ConfigUpdateRequest (new — request DTO)

Batch parameter value update payload sent from UI to backend.

| Field | Type | Description |
|-------|------|-------------|
| Updates | Dictionary<string, string?> (required, non-empty) | Map of parameter name → new value (as string). Only changed parameters are included. |

**Validation rules**:
- Updates must contain at least one entry.
- Keys must be non-empty strings.
- Values may be null (to clear/reset a parameter to default).
- Keys matching Environment-sourced parameters are rejected (400 Bad Request).

### ConfigReorderRequest (new — request DTO)

Parameter display order payload sent from UI to backend.

| Field | Type | Description |
|-------|------|-------------|
| OrderedKeys | string[] (required, non-empty) | Ordered list of parameter names representing the desired display order. |

**Validation rules**:
- OrderedKeys must contain at least one entry.
- Duplicate keys are silently deduplicated (first occurrence wins).
- Keys not found in current config are silently ignored.
- Keys present in config but absent from the list are appended at the end in their existing order.

## Relationships

```
ConfigurationSnapshot 1──* ConfigurationParameter
ConfigUpdateRequest ──> ConfigSnapshotService.UpdateParametersAsync ──> ConfigurationSnapshot
ConfigReorderRequest ──> ConfigSnapshotService.ReorderParametersAsync ──> ConfigurationSnapshot
```

## Persistence

- **Backend**: `data/config/config.json` — single JSON file, atomic write via `.tmp` + rename.
- **Key order in JSON**: Determines display order in UI. Controlled by `Dictionary` insertion order in `ConfigSnapshotService`, which `System.Text.Json` preserves during serialization.
- **No new persistence stores**: All data lives in the existing config file.
- **Frontend**: No client-side persistence beyond existing `localStorage` for API Base URL and Bearer Token. Dirty state is in-memory React state only.
