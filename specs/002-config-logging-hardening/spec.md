# Feature Specification: Config & HTTP Logging Adjustments

Feature Branch: `002-config-logging-hardening`
Created: 2025-11-14
Status: Implemented (Merged on 2025-11-14)

Context: This spec replaces prior user stories. It focuses narrowly on:
1) Reducing default HTTP logging verbosity.
2) Limiting saved configuration to GameBot-relevant environment variables only.

## Problem Statement
- Default HTTP-related logging is too chatty, making it harder to spot important events.
- The saved configuration currently considers all environment variables, including many unrelated to GameBot, which adds noise and potential risk.

## Goals
- Tame HTTP-related log verbosity out-of-the-box while still allowing overrides.
- Restrict saved configuration to environment variables relevant to GameBot.

## User Stories

US1: Increase the log level of HTTP calls so the default logs are not so chatty.

US2: Saving configuration should only consider the environment variables relevant to GameBot, not all environment variables.

## Functional Requirements

- FR-HTTP-001: Set default minimum log level for HTTP client/server-related categories to Warning.
  - Applies at least to categories matching: `System.Net.Http.*`, `Microsoft.AspNetCore.HttpLogging` (if present).
  - Must be overridable via environment variable `GAMEBOT_HTTP_LOG_LEVEL_MINIMUM` with allowed values `Trace|Debug|Information|Warning|Error|Critical`.

- FR-CONFIG-ENV-001: Saved configuration must include only environment variables relevant to GameBot.
  - Relevant variables are case-insensitive keys starting with prefixes: `GAMEBOT_` or `GAMEBOT__` (double underscore for hierarchical keys).
  - Keys not matching these prefixes must be excluded (e.g., `PATH`, `COMPUTERNAME`, `PROCESSOR_*`, `ASPNETCORE_*`, etc.).

- FR-CONFIG-ENV-002: Snapshot metadata must include counts: `envScanned`, `envIncluded`, `envExcluded` for transparency.

## Acceptance Tests (High-Level)
- AT-HTTP-001: With no overrides, logs from `System.Net.Http.HttpClient` at Information are suppressed; Warning and above are emitted.
- AT-HTTP-002: Setting `GAMEBOT_HTTP_LOG_LEVEL_MINIMUM=Information` results in HTTP Information events being emitted.
- AT-CONFIG-ENV-001: Snapshot excludes typical OS/framework variables (e.g., `PATH`, `TEMP`, `ASPNETCORE_URLS`).
- AT-CONFIG-ENV-002: Snapshot includes variables like `GAMEBOT__FOO`, `GAMEBOT_BAR`.
- AT-CONFIG-ENV-003: Snapshot metadata shows accurate `envScanned`, `envIncluded`, `envExcluded`.

## Non-Goals
- Changing non-HTTP logging defaults.
- Persisting dynamic log level changes beyond process lifetime (overrides are env-based only here).

## Notes
- Prefix list can be extended in future specs if additional namespaces are introduced.
- Secret masking continues to follow existing rules; this spec only scopes which env vars are considered.
## Post-Implementation Review
Delivered:
- Reduced default HTTP logging verbosity with dynamic override via `GAMEBOT_HTTP_LOG_LEVEL_MINIMUM`.
- Saved configuration limited to `GAMEBOT_*` and `Service__*` keys; added `envScanned/envIncluded/envExcluded` metadata.
- Always-include documented keys with concrete defaults in snapshot.
- Refresh applies configuration to the running service: HTTP log level, trigger worker options, and dynamic OCR backend (Env/Tesseract).
- Snapshot persists in camelCase; on-disk edits respected on refresh.
- Tests added for env filtering, on-disk refresh persistence, and runtime OCR switch.

Deferred/Out of Scope:
- Config diff/history endpoints, structured JSON logs with redaction, and standardized error schema.

# (Archived) Feature Specification: Configuration & Logging Hardening

Status: Superseded by focused US1/US2 above; retained for context and future follow-ups.

## Problem Statement
Operators need stronger guarantees and tooling around configuration accuracy, log clarity, and change tracking. Current implementation:
- Partially documents captured keys (FR-009 incomplete).
- Does not expose refresh count/service version consistently in snapshot.
- Lacks historical diff or comparison capability.
- Logging is mixed format; secrets rely only on masking, not log pipeline safeguards.
- No runtime log-level change support; requires restart for verbosity adjustments.
- Startup validation of required env vars is implicit (silent fallbacks) rather than explicit report.
- Error responses for config endpoints lack a standardized schema (machine-readable codes).
- No structured audit trail for manual refresh operations (who/when/correlation id).

## Goals
1. Improve observability and auditability of configuration over time.
2. Enforce consistent structured logging with secret-safe guarantees.
3. Provide explicit operator feedback on configuration health at startup and refresh.
4. Enable dynamic diagnostics (log level changes) without restart.
5. Standardize error payloads for the configuration API.

## Non-Goals
- Replacing existing logging framework.
- Full historical configuration retention (only limited recent snapshot diffing).
- Multi-tenant config segregation (future consideration).

## User Stories

### User Story A - Enhanced Snapshot Metadata (Priority: P1)
Include `generatedAtUtc`, `serviceVersion`, `refreshCount`, `precedenceOrder`, and an array `sources` listing each key's origin summary for transparency.

### User Story B - Startup Validation Report (Priority: P1)
On startup produce a structured list of missing/nullable recommended environment variables and log them as a single structured event; expose last validation report via `/config/validation`.

### User Story C - Structured & Redacted Logging (Priority: P1)
Emit application logs in structured JSON (opt-in) with automatic redaction of potential secret substrings and correlation id propagation.

### User Story D - Config Diff Capability (Priority: P2)
Provide endpoint `/config/diff?previous=<timestamp>` returning differences between current snapshot and a stored prior snapshot (retain limited N history, e.g. last 5 refreshes).

### User Story E - Dynamic Log Level Endpoint (Priority: P2)
Expose authenticated `POST /logging/level` to adjust log level (e.g. Debug/Info/Warn) at runtime; persisted only in-memory.

### User Story F - Standardized Error Schema (Priority: P2)
All config-related error responses use shape: `{ "error": { "code": string, "message": string, "correlationId": string? } }`.

### User Story G - Refresh Audit Trail (Priority: P3)
Record refresh events (timestamp, actor token hash, correlation id) in memory (last 20) and surface via `/config/refresh/history`.

## Edge Cases
- Large number of environment variables: validation report must paginate/limit.
- Rapid refresh calls: diff history must prune oldest snapshot deterministically.
- Unknown log level provided: return error code `LOG_LEVEL_INVALID`.
- Extremely large value sizes: redaction logic must avoid scanning performance issues (cap per-value length scanned).

## Functional Requirements

- **FR-LC-001**: Snapshot MUST include `refreshCount`, `serviceVersion`, `precedenceOrder`, and `sources` summary collection.
- **FR-LC-002**: System MUST retain the last N (configurable, default 5) snapshots in memory for diff operations.
- **FR-LC-003**: Endpoint `GET /config/diff?previous=<generatedAtUtc>` MUST compute added/removed/changed keys with old/new values (masked if secret).
- **FR-LC-004**: Startup MUST produce a validation report listing: missing recommended keys, keys with empty values, and duplicated conflicting sources.
- **FR-LC-005**: Endpoint `GET /config/validation` MUST return latest validation report.
- **FR-LC-006**: When structured logging mode is enabled (`GAMEBOT_STRUCTURED_LOGS=true`), logs MUST be JSON with fields: timestamp, level, message, category, correlationId?, exception?.
- **FR-LC-007**: Logging pipeline MUST redact substrings matching secret patterns before emission (same keyword set as snapshot masking).
- **FR-LC-008**: Endpoint `POST /logging/level` MUST accept body `{ level: "Debug"|"Information"|"Warning"|"Error"|"Critical" }` and apply change immediately.
- **FR-LC-009**: All configuration-related error responses MUST conform to the standardized error schema.
- **FR-LC-010**: Refresh endpoint MUST append record to in-memory audit list with timestamp and correlation id; token hashed (SHA-256, first 12 chars) for privacy.
- **FR-LC-011**: Diff computation MUST complete within 200ms for snapshots ≤ 1000 parameters.
- **FR-LC-012**: Redaction MUST occur before serialization to prevent leaking secrets in logs.
- **FR-LC-013**: Validation report MUST be generated within 300ms at startup.
- **FR-LC-014**: If previous snapshot reference not found for diff, return error code `CONFIG_DIFF_NOT_FOUND`.

## Success Criteria
- **SC-LC-001**: Structured log lines contain correlationId when provided ≥ 95% of requests.
- **SC-LC-002**: Diff endpoint median latency < 150ms for 1000-param snapshots.
- **SC-LC-003**: Redaction false positives < 5% (measure via test fixtures); zero true secret leaks.
- **SC-LC-004**: Dynamic log level change reflected in subsequent logs within 1 second.
- **SC-LC-005**: Validation endpoint accuracy (missing key detection) ≥ 99% in test matrix.

## Assumptions
1. Snapshot history stored in-memory only (not persisted to disk) to minimize IO overhead.
2. Secret detection patterns remain identical to existing implementation for consistency.
3. Correlation ID continues to use `X-Correlation-ID` header when supplied.
4. Dynamic log level changes are not persisted across restarts.
5. Audit trail size (20) is sufficient; older entries discarded FIFO.

## Risks
- Increased memory usage storing multiple snapshots.
- JSON logging may increase log volume and storage cost.
- Secret redaction pipeline complexity (performance impact) if naive substring scanning used.

## Open Questions
1. Should snapshot history optionally persist to disk? (Out of scope now.)
2. Should diff endpoint support arbitrary two timestamps or only current vs previous? (Currently only previous.)
3. Should dynamic log level require a privileged role beyond bearer token? (Pending security review.)

## Next Steps
1. Implement metadata enrichment & validation report.
2. Add snapshot history + diff endpoint.
3. Introduce structured logging mode with redaction.
4. Add dynamic log level endpoint.
5. Standardize error schema & refresh audit trail.
6. Update README and existing save-config spec references.
