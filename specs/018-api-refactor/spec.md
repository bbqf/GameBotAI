# Feature Specification: API Structure Cleanup

**Feature Branch**: `[018-api-refactor]`  
**Created**: 2025-12-28  
**Status**: Draft  
**Input**: User description: "Refactor API. API got cluttered and some endpoints are duplicated. I want to have a clear, structured API. No duplication of endpoints like /actions and /api/actions is needed. Do not pay attention about migration, just move all endpoints under /api and make sure all tests run properly. Also structure the swagger in clear sections: actions, sequences, etc."

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

### User Story 1 - Navigate a single canonical API (Priority: P1)

API consumers call a single, consistent base path under `/api` without encountering duplicate or conflicting routes.

**Why this priority**: Removes confusion and errors for all callers; establishes the contract every client relies on first.

**Independent Test**: Hit each published route under `/api` and confirm no alternative non-`/api` path returns a successful response.

**Acceptance Scenarios**:

1. **Given** the service is running, **When** a client calls an endpoint under `/api/{resource}`, **Then** the request succeeds using that path and no alternative base path returns 2xx.
2. **Given** a client uses a legacy path such as `/actions`, **When** the request is sent, **Then** the service returns a non-success status with a clear message indicating the canonical `/api` path.

---

### User Story 2 - Browse clear API documentation (Priority: P2)

API users can locate endpoints in documentation grouped by domain (actions, sequences, sessions, configuration) without scanning unrelated sections.

**Why this priority**: Speeds onboarding and reduces misuse; documentation drives correct client usage.

**Independent Test**: Open the API documentation and verify endpoints appear under the expected domain sections with no duplicates across sections.

**Acceptance Scenarios**:

1. **Given** the API documentation is open, **When** the user navigates to the actions section, **Then** all action endpoints are listed there and none appear outside the actions group.
2. **Given** the sequences section is selected, **When** the user reviews it, **Then** sequence endpoints are present with descriptive summaries and no unrelated endpoints.

---

### User Story 3 - Keep automated checks green (Priority: P3)

Developers run the full test suite against the canonical `/api` routes and get a clean pass, showing no regressions from the reorganization.

**Why this priority**: Ensures safety and confidence in the refactor without relying on manual verification.

**Independent Test**: Execute the automated API and integration suites that target `/api` routes and confirm all tests pass without route-related failures.

**Acceptance Scenarios**:

1. **Given** the updated route structure, **When** the automated regression suite runs, **Then** all API tests that call `/api` endpoints pass without needing route aliases.
2. **Given** the route catalog is rebuilt, **When** contract or integration tests enumerate routes, **Then** they find no duplicate paths and no references to non-`/api` bases.

---

[Add more user stories as needed, each with an assigned priority]

### Edge Cases

- Requests sent to legacy base paths (e.g., `/actions`, `/sequences`) return a clear non-success response indicating the canonical `/api/...` path instead of redirecting.
- Calls to unknown resources under `/api` (e.g., `/api/unknown`) return a consistent error format without leaking internal details.
- Documentation still renders when a resource group has zero endpoints (e.g., feature toggled off) without breaking other sections.

## Requirements *(mandatory)*

<!--
  ACTION REQUIRED: The content in this section represents placeholders.
  Fill them out with the right functional requirements.
-->

### Functional Requirements

- **FR-001**: All public endpoints SHALL be served under the `/api/{resource}` base path; no successful responses are available from non-`/api` bases.
- **FR-002**: Each resource domain (actions, sequences, sessions, configuration, detection/triggers) SHALL have a single canonical route prefix with no duplicates or aliases.
- **FR-003**: Legacy or duplicate routes outside `/api` SHALL return a consistent non-success status and message that references the canonical `/api` route, without redirects.
- **FR-004**: API documentation SHALL group endpoints into clear sections (at minimum: actions, sequences, sessions/emulator, configuration/administration) with each endpoint listed in exactly one section.
- **FR-005**: Endpoint summaries and tags in documentation SHALL match the route grouping, enabling users to find an endpoint by its domain without scanning other sections.
- **FR-006**: Automated API, contract, and integration tests SHALL be updated to call only the canonical `/api` routes and SHALL pass after the refactor.
- **FR-007**: Swagger documentation SHALL include request/response schemas and at least one concrete example payload for each documented endpoint.

### Key Entities *(include if feature involves data)*

- **API Resource Group**: A domain category (e.g., actions, sequences, sessions, configuration) that owns a distinct route prefix and documentation section.
- **Endpoint Catalog**: The curated list of canonical routes per resource group, used by tests and documentation to prevent duplication.

## Success Criteria *(mandatory)*

<!--
  ACTION REQUIRED: Define measurable success criteria.
  These must be technology-agnostic and measurable.
-->

### Measurable Outcomes

- **SC-001**: 0 endpoints respond with success outside the `/api/...` base path in route inventory and automated checks.
- **SC-002**: 100% of documented endpoints appear in exactly one documentation section aligned to their resource group.
- **SC-003**: 100% of automated API, contract, and integration tests complete without failures attributed to missing or duplicate routes.
- **SC-004**: Users can locate a target endpoint in documentation within two clicks (section then endpoint) for the covered resource groups during user acceptance walkthroughs.
