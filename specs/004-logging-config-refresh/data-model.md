# Data Model — Logging Config Refresh (004)

## Overview
Runtime logging controls revolve around a persisted configuration file plus transient level switches stored in memory. The model needs to support global defaults, per-category overrides, provenance metadata, and validation so that the API can reject invalid log levels or malformed payloads.

## Entities

### LoggingConfig
| Field | Type | Description | Notes |
|-------|------|-------------|-------|
| `Id` | string | Logical identifier ("logging") to keep JSON structure consistent | Constant primary key |
| `Version` | string | Semantic version of schema (`"1.0"`) | Used for migrations |
| `GlobalLevel` | string (enum) | Default minimum level for all categories (`Trace`…`Critical`) | Required |
| `Components` | array<`LoggingComponentConfig`> | Overrides per category or subcomponent | Optional (empty array => inherit global) |
| `LastUpdatedUtc` | string (ISO 8601) | Timestamp of last applied change | Server-generated |
| `UpdatedBy` | string | Free-form operator identifier (token name, automation id) | Optional but recommended |
| `Source` | string | Indicates whether config originated from `file`, `api`, or `default` | Drives audit trail |

Validation:
- `GlobalLevel` must map to `LogLevel` enum.
- `Components` must not contain duplicate `Key` entries.
- At least one of `LastUpdatedUtc` or `UpdatedBy` must be present once the config is persisted.

### LoggingComponentConfig
| Field | Type | Description | Notes |
|-------|------|-------------|-------|
| `Key` | string | Category name (e.g., `Microsoft.AspNetCore`, `GameBot.Service.Services.CommandService`) | Required; case-sensitive |
| `Level` | string (enum) | Minimum level for the category | Required |
| `IncludeScopes` | bool | Mirrors Microsoft logging scopes toggle | Defaults to `false` |
| `Subcomponents` | array<`LoggingComponentConfig`> | Optional nested overrides (e.g., `Microsoft.AspNetCore.Routing`) | Enables tree cascades |

Validation:
- `Subcomponents` inherit the hierarchy; parent key must be prefix of child.
- Depth limited to 3 to avoid runaway recursion.
- `Level` must be >= parent level severity (cannot make a child more verbose than parent without explicit justification).

### LoggingConfigRequest
Represents the API payload.

| Field | Type | Description |
|-------|------|-------------|
| `globalLevel` | string | Desired baseline | optional → keep existing |
| `components` | array | Overrides; same shape as `LoggingComponentConfig` | optional |
| `source` | string | Client-provided reason | optional |

Validation occurs at API boundary before invoking the service layer.

### LogMessageSample (documentation-only helper)
Defines the enforced format.

| Field | Type | Description |
|-------|------|-------------|
| `timestamp` | string | `yyyy-MM-dd HH:mm:ss` | always present |
| `level` | string | `u` format specifier (e.g., `INF`, `DBG`) |
| `sourceContext` | string | Derived from `ILogger` category |
| `message` | string | Rendered message template |
| `exception` | string | Optional stack trace |

## Relationships & Flow
1. `LoggingConfigRepository` loads persisted `LoggingConfig` at startup.
2. `DynamicLoggingConfigService` stores `LoggingComponentConfig` entries inside a level-switch registry.
3. API requests map `LoggingConfigRequest` → domain `LoggingConfig`, update disk, then notify the service to apply switches.
4. Log messages reference `LogMessageSample` format to verify acceptance tests.

## State Transitions
1. **Initial Load**: If no file exists, bootstrap with defaults (`GlobalLevel=Information`, empty components) and `Source=default`.
2. **Apply Override**: POST request validated → `LoggingConfig` updated → persisted → level switches updated atomically.
3. **Rollback**: Loading from file at startup or via `POST /api/logging/config/reload` resets runtime switches to persisted state.
4. **Cleanup**: Removing a component from the array deletes its switch and falls back to parent/global level.
