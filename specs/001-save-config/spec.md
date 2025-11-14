# Feature Specification: Save Configuration

**Feature Branch**: `001-save-config`  
**Created**: 2025-11-14  
**Status**: Implemented (Feature merged on 2025-11-14)  
**Input**: User description: "Save configuration. All of the configuration parameters, including but not limited to environment variables collected in ENVIRONMENT.md. The configuration should be stored in a config subdirectory of data directory in JSON format. Respectively the data dir directory shouldn't be part of the saved configuration."

## User Scenarios & Testing *(mandatory)*

<!--
  IMPORTANT: User stories should be PRIORITIZED as user journeys ordered by importance.
  Each user story/journey must be INDEPENDENTLY TESTABLE - meaning if you implement just ONE of them,
  you should still have a viable MVP (Minimum Viable Product) that delivers value.
  
  Assign priorities (P1, P2, P3, etc.) to each story, where P1 is the most critical.
  Think of each story as a standalone slice of functionality that can be:
  - Developed independently
  - Tested independently
  - Deployed independently
  - Demonstrated to users independently
-->

### User Story 1 - Persist Effective Configuration Snapshot (Priority: P1)

On service startup the system assembles the "effective configuration" from environment variables, optional configuration files, and defaults, then persists a normalized JSON snapshot into `data/config/config.json` (excluding the absolute data directory path itself and any secret values). The snapshot allows operators to audit exactly which settings were in effect for a given run.

**Why this priority**: Foundational for traceability, reproducibility, and later troubleshooting; without a persisted snapshot, post-incident analysis lacks ground truth.

**Independent Test**: Start service with a known set of environment variables; verify a single JSON file is created with expected key/value pairs (secrets masked) and excludes the data directory path.

**Acceptance Scenarios**:

1. **Given** service starts with environment variables set, **When** startup completes, **Then** `data/config/config.json` exists containing those values (normalized, secrets masked).
2. **Given** no optional env overrides provided, **When** service starts, **Then** snapshot includes default values explicitly.

---

### User Story 1b - Load Existing Configuration On Startup (Priority: P1)

On service startup, if a saved configuration exists at `data/config/config.json`, the system loads it as the baseline configuration, then applies environment variables on top (environment overrides file values), and finally fills any missing values from defaults. The resulting "effective configuration" is what gets persisted and served by the endpoint.

**Why this priority**: Ensures continuity across restarts while still honoring operational overrides via environment variables; enables GitOps-style or baked image defaults complemented by env-based adjustments.

**Independent Test**: Place a `data/config/config.json` with known values, then start the service with overlapping environment variables; verify that overlaps take the environment value, non-overlapping keys come from the file, and the remainder are defaults.

**Acceptance Scenarios**:

1. **Given** `data/config/config.json` contains `{"FOO":"file"}` and env sets `FOO=env`, **When** service starts, **Then** effective configuration has `FOO="env"` and the persisted snapshot reflects the override.
2. **Given** `data/config/config.json` sets `BAR="file"` and no env for `BAR`, **When** service starts, **Then** effective configuration has `BAR="file"`.
3. **Given** the file is malformed or unreadable, **When** service starts, **Then** the service logs a clear error and proceeds using env + defaults (no crash), and the endpoint returns a structured error if snapshot persistence subsequently fails.

---

### User Story 2 - Expose Read-Only Configuration Endpoint (Priority: P2)

Operators can query an authenticated endpoint (e.g. `/config`) to retrieve the current effective configuration (same normalized view as persisted) for live diagnostics without opening files on disk.

**Why this priority**: Enables quick remote inspection during runtime; reduces need for shell access.

**Independent Test**: Call the endpoint and compare JSON to stored snapshot; verify secrets masked and data directory path omitted.

**Acceptance Scenarios**:

1. **Given** service running, **When** a GET request to `/config` with valid auth token occurs, **Then** response is 200 JSON identical (except timestamp) to disk snapshot.
2. **Given** service running, **When** unauthenticated request to `/config` occurs (and token configured), **Then** response is 401/403.

---

### User Story 3 - Manual Snapshot Refresh (Priority: P3)

An operator can trigger a manual refresh of the persisted configuration snapshot (without restart) to capture changes in environment or dynamic values if they were externally altered.

**Why this priority**: Lower priority; environments typically immutable after start, but helpful for long-running processes in dynamic container orchestrations.

**Independent Test**: Modify an environment variable via supported mechanism, invoke refresh endpoint/action, verify updated snapshot.

**Acceptance Scenarios**:

1. **Given** configuration changed externally, **When** refresh endpoint `/config/refresh` is called with auth, **Then** disk snapshot updates and endpoint reflects new values.
2. **Given** no changes since last snapshot, **When** refresh requested, **Then** snapshot file timestamp updates (optional) and content remains identical.

---

[Add more user stories as needed, each with an assigned priority]

### Edge Cases

- Missing or empty environment variables: snapshot must include explicit null/default markers.
- Conflicting sources (env vs config file): define precedence (assumption: env overrides file, file overrides defaults).
- Secret values (tokens, credentials): must be fully redacted as "***" and never stored in clear text. Secret keys are those whose names contain `TOKEN`, `SECRET`, `PASSWORD`, or `KEY` (case-insensitive, prefix/suffix/contains).
- Data directory relocation while running: refresh should capture new effective path but still exclude its absolute value from persisted JSON.
- Unwritable `data/config` directory: service should log error and continue running; endpoint returns 500 with clear diagnostic.
- Partial write/interrupted snapshot: atomic write strategy (temp file + move) to avoid corrupt JSON (assumption).
- Large configuration (many keys): performance acceptable (<200ms snapshot generation).
- Manual refresh invoked concurrently: serialize refresh operations to avoid race conditions.

## Requirements *(mandatory)*

<!--
  ACTION REQUIRED: The content in this section represents placeholders.
  Fill them out with the right functional requirements.
-->

### Functional Requirements

 - **FR-001**: System MUST compile an "effective configuration" on startup from defaults, the saved configuration file at `data/config/config.json` (if present), any other configuration files, and environment variables, with precedence: Environment variables > Saved file > Other files > Defaults.
- **FR-002**: System MUST persist the effective configuration as JSON to `data/config/config.json` excluding the absolute data directory path.
- **FR-003**: System MUST fully redact secret/sensitive values as "***" in the persisted file and endpoint output. Secrets are any keys whose names contain `TOKEN`, `SECRET`, `PASSWORD`, or `KEY` (case-insensitive, prefix/suffix/contains).
- **FR-004**: System MUST expose an authenticated GET endpoint `/config` returning the effective configuration JSON.
- **FR-005**: System MUST ensure the endpoint output matches persisted snapshot (except metadata fields like generated timestamp).
- **FR-006**: System MUST handle snapshot persistence failures gracefully (log error, endpoint returns structured error without crashing service).
- **FR-007**: System MUST perform writes atomically to avoid partial/corrupt files.
- **FR-008**: System MUST provide a manual refresh endpoint `/config/refresh` (auth required) to regenerate and persist the snapshot at runtime.
- **FR-009**: System MUST document all captured configuration keys referencing ENVIRONMENT.md names and any derived values.
- **FR-010**: System MUST exclude transient runtime-only values unless explicitly designated (e.g. internal caches) but MAY include dynamic assigned port if helpful (assumption: include dynamic port).
- **FR-011**: System MUST represent absent values explicitly as null rather than omitting keys.
- **FR-012**: System MUST include a snapshot metadata section (timestamp, version) without leaking filesystem paths.
 - **FR-013**: On startup, if `data/config/config.json` is missing, unreadable, or invalid JSON, the service MUST log a clear error, ignore the file, and continue using environment variables, other configuration files, and defaults to build the effective configuration. The service MUST NOT fail to start due to this condition.

### Key Entities *(include if feature involves data)*

- **ConfigurationParameter**: Logical setting with name, source (Default | File | Environment), value (masked if secret), isSecret flag.
- **ConfigurationSnapshot**: Aggregate containing collection of ConfigurationParameters plus metadata (generatedAtUTC, serviceVersion, dynamicPort, refreshCount).

## Success Criteria *(mandatory)*

<!--
  ACTION REQUIRED: Define measurable success criteria.
  These must be technology-agnostic and measurable.
-->

### Measurable Outcomes

- **SC-001**: Snapshot file is generated within 500ms of service startup in 95% of runs.
- **SC-002**: Configuration endpoint responds in <200ms (p50) and <400ms (p95) under normal load.
- **SC-003**: 100% of required environment variables appear (present or null) in snapshot for audit completeness.
- **SC-004**: No secret value appears in clear text (automated scan finds 0 matches of raw token patterns).
- **SC-005**: Manual refresh (if implemented) completes in <750ms and produces consistent masked output.
- **SC-006**: Atomic write guarantees 0 occurrences of corrupt JSON over 10,000 snapshot writes in test.

## Assumptions

1. Environment variables take precedence over config file values; config file over defaults.
2. Secrets list includes any variable containing `TOKEN`, `SECRET`, `PASSWORD`, `KEY` (suffix/prefix/contains, case-insensitive).
3. Dynamic port, if chosen, is included as a non-secret parameter.
4. Manual refresh is included as part of scope per decision.
5. Data directory absolute path is excluded; relative logical name may be included (e.g. `dataDirName": "data").

## Open Clarifications Needed

None. All prior clarifications resolved (full redaction; refresh endpoint in scope).

## Post-Implementation Review

### What Was Delivered
- Effective configuration snapshot persisted at `data/config/config.json` with atomic writes.
- Precedence enforced: Environment > Saved file > Other files > Defaults.
- Secret masking for keys containing TOKEN/SECRET/PASSWORD/KEY (case-insensitive).
- Startup load of existing snapshot with graceful fallback on malformed file.
- Endpoints: `GET /config/` (lazy generation if missing) and `POST /config/refresh`.
- README documentation section added.

### Gaps / Follow-Up Items
1. Snapshot metadata could include `refreshCount` and `serviceVersion` consistently (verify presence; code may partially implement).
2. Missing explicit enumeration of all captured environment/config keys for audit (FR-009 only partially addressed).
3. No diff endpoint or ability to compare historical snapshots.
4. Log formatting inconsistent (mix of structured and message-only lines); secrets redaction relies solely on masking layer, not log filtering.
5. Dynamic log level adjustment not supported (would aid diagnostics).
6. Validation of required env variables produces implicit behavior; explicit warnings list would improve operability.
7. Config endpoint error payload shape not standardized (future: add error schema with code/reason details).

### Link to Hardening / Enhancement Spec
See follow-up spec: `specs/002-config-logging-hardening/spec.md` for planned improvements in logging consistency, snapshot enrichment, validation, and diff capabilities.
