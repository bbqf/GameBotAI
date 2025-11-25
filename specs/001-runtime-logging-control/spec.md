# Feature Specification: Runtime Logging Control

**Feature Branch**: `001-runtime-logging-control`  
**Created**: 2025-11-25  
**Status**: Draft  
**Input**: User description: "Runtime logging configuration and fine control over the various levels per component and subcomponent. The logging levels should be controllable via REST API, maybe under the config endpoint. What is also needed is a possibility to activate/deactivate the logging per component (Microsoft.AspNetCore or GameBot.Domain.Triggers) and in the runtime, so when the logging is enabled or disabled this should be applied immediately, without the application restart. Also the default level for all of the components should be set to Warning."

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

### User Story 1 - Adjust live logging level (Priority: P1)

An operations engineer needs to raise or lower the logging level for a specific component at runtime (e.g., `GameBot.Domain.Triggers`) without restarting the service so that they can troubleshoot incidents or return to the baseline once noise subsides.

**Why this priority**: Direct control of noisy components enables critical incident mitigation and is the primary business goal behind exposing runtime configuration.

**Independent Test**: Issue a REST call to the config endpoint targeting a single component and verify that subsequent logs for that component respect the new level while other components remain unchanged.

**Acceptance Scenarios**:

1. **Given** logging defaults to Warning, **When** an engineer sets `GameBot.Domain.Triggers` to Debug via the config API, **Then** subsequent trigger logs emit Debug statements immediately without restarting the service.
2. **Given** a component currently at Debug, **When** the engineer downgrades it to Error via the API, **Then** informational and warning logs for that component stop emitting within one request cycle.

---

### User Story 2 - Toggle component logging (Priority: P2)

An operations engineer wants to temporarily disable all logging from a subcomponent (e.g., `Microsoft.AspNetCore`) to reduce output volume, and then re-enable it once the investigation completes.

**Why this priority**: Being able to silence or re-enable components prevents log storage overruns and focuses attention on active investigations, but is slightly less critical than level tuning.

**Independent Test**: Call the config API to set the enabled flag for a component to false, confirm logs stop emitting, then set it back to true and ensure output resumes without restarting the application.

**Acceptance Scenarios**:

1. **Given** `Microsoft.AspNetCore` logging is enabled, **When** an engineer disables the component via the API, **Then** no new logs from that component are produced until it is re-enabled.

---

### User Story 3 - Review effective logging policy (Priority: P3)

An SRE wants a single view of all components, their enabled state, and their effective logging level to confirm that the environment is back to the default posture after incident response.

**Why this priority**: Visibility allows audit and compliance confirmation; it is valuable but follows the ability to make changes.

**Independent Test**: Perform a GET request on the config endpoint and verify that it enumerates each component with current state, default level, and source of overrides.

**Acceptance Scenarios**:

1. **Given** one component has a custom level and another is disabled, **When** the engineer queries the logging config, **Then** both overrides and default values are visible in the response.

---

### Edge Cases

- Simultaneous updates to the same component from multiple clients must resolve deterministically (last write wins and response communicates the resulting state).
- Requests referencing unknown component names must return validation errors without changing existing settings.
- If the config store is unavailable, attempts to change logging should fail gracefully and leave the previous level active.
- Bulk reset to defaults must immediately reapply Warning level and enabled state to all components.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST expose a REST endpoint (under the existing config surface) that lists every log component, its enabled flag, effective level, default level, and timestamp of last override.
- **FR-002**: System MUST allow authorized callers to set the logging level for a single component at runtime (levels: Debug, Information, Warning, Error, Critical) and apply the change immediately without restarting services.
- **FR-003**: System MUST provide an API action to enable or disable logging output per component, applying the change in-flight and persisting the selection for future process restarts.
- **FR-004**: System MUST allow bulk operations that set all components back to their default Warning level and enabled=true state with one request, confirming the result in the response payload.
- **FR-005**: System MUST persist component-level logging configuration so that overrides survive service restarts and can be audited.
- **FR-006**: System MUST enforce that callers lacking config privileges cannot read or mutate logging settings, returning an authorization error without side effects.
- **FR-007**: System MUST emit an event or audit log entry each time a logging level or enabled flag changes, capturing who made the change, the previous state, and the resulting state.
- **FR-008**: System MUST fail fast with descriptive errors when invalid component names, invalid levels, or conflicting state changes are submitted, leaving prior settings intact.

### Key Entities *(include if feature involves data)*

- **LoggingComponentSetting**: Represents a logical component (name, description) plus mutable fields `enabled`, `effectiveLevel`, `defaultLevel`, `lastChangedBy`, `lastChangedAt`.
- **LoggingPolicySnapshot**: Captures the collection of component settings at a point in time, used for GET responses, audits, and potential rollbacks.

## Assumptions

- Only authenticated operators with config permissions can access the logging management endpoints; authentication flows already exist.
- Component catalog is derived from existing logging configuration providers; no UI for managing the catalog is required in this feature.
- Persistent storage for runtime configuration already exists and can store additional fields without new infrastructure work.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Operations staff can change a component’s logging level and observe the new level taking effect within 5 seconds in 95% of attempts.
- **SC-002**: 100% of component-level logging overrides persist through planned service restarts in staging and production.
- **SC-003**: At least 90% of audit entries for logging changes include actor, timestamp, component, previous state, and new state.
- **SC-004**: After implementation, the default Warning posture can be restored across all components via a single API call, confirmed in less than 10 seconds.
