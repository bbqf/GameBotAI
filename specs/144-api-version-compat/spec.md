# Feature Specification: API Version and Gamepack Compatibility

**Feature Branch**: `144-api-version-compat`  
**Created**: 2026-10-15  
**Status**: Draft  
**Input**: User description: "Issue #305 (FR-022): Declare the API version and reject a gamepack for another major version. A client can ask which API version the platform runs. A gamepack manifest states the API major version that it needs. The install refuses a gamepack for another major version and names both versions. The compatibility rule is written in the project docs."

## Clarifications

### Session 2026-10-15

- Q: Does the endpoint need authentication? → A: It uses the same API token rule as the other `/api` routes. (Rationale: no special case.)
- Q: Where does the version mismatch show in the problem list? → A: As a structure-category problem on the manifest object, with a message that names both versions. (Rationale: reuse the existing categories; no new category.)
- Q: Is the running major version the product version major? → A: Superseded by loop 1 (A8): no, it is a separate constant. (Rationale: FR-002.)
- Q: What HTTP status does a refused install return? → A: The same status as other install problems (the existing problem-list response). (Rationale: FR-006, no change to install behavior.)

### Session 2026-10-15 (loop 1, from analyze)

- Q (A1): What is the outcome for a value that is not a whole number, such as 1.5 or text? → A: The file is invalid. The existing file reader refuses it as an invalid file. A whole number below 1 is a structure problem. FR-003 and the edge case say both. (Rationale: reuse the reader; no new parse rule.)
- Q (A8): Is the running major version the product version major? → A: No. The plan chooses a separate API version constant (1.0.0, schema format 1). The Assumptions say so. (Rationale: FR-002.)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Read the platform API version (Priority: P1)

A client (a person, a script, or a gamepack tool) asks the platform which API version it runs. The answer has the major, minor, and patch numbers. It also has the format version of the sequence and command schema.

**Why this priority**: The version check in US2 needs a clear running version. A client also needs to know the version before it builds a gamepack.

**Independent Test**: Call `GET /api/version`. Read the numbers. Compare them with the documented values.

**Acceptance Scenarios**:

1. **Given** the platform runs, **When** a client calls `GET /api/version`, **Then** the response has the API major, minor, and patch numbers and the sequence and command schema format version.
2. **Given** the platform runs, **When** a client calls the endpoint two times, **Then** both responses are the same.

---

### User Story 2 - Refuse a gamepack for another major version (Priority: P1)

A person installs a gamepack that states a required API major version. If the version differs from the running major version, the install fails. The error names the required version and the running version. No content is installed.

**Why this priority**: This is the protection for installations in the field. A set of automations must not install on a platform that cannot run it.

**Independent Test**: Install a gamepack with a required major version that differs from the running one. Check the error text. Check that no object, owner entry, or registry entry changed.

**Acceptance Scenarios**:

1. **Given** a gamepack that needs a major version that differs from the running one, **When** the person installs it, **Then** the install fails, the error names the required version and the running version, and nothing is installed.
2. **Given** a gamepack that needs the running major version, **When** the person installs it, **Then** the install works as before.
3. **Given** a gamepack that does not state a required major version, **When** the person installs it, **Then** the install works as before.
4. **Given** a gamepack for another major version, **When** the person runs a dry run, **Then** the dry run reports the same version problem and changes nothing.

---

### User Story 3 - Know what a version change may break (Priority: P2)

A gamepack author reads the project docs. The docs state the compatibility rule: inside one major version, an existing valid sequence stays valid. A change that breaks this rule raises the major version.

**Why this priority**: The rule gives meaning to the major version check. It guides future changes. It needs no code.

**Independent Test**: Read the docs. Find the rule, the meaning of the three numbers, and the way to read the version.

**Acceptance Scenarios**:

1. **Given** the project docs, **When** an author looks for the version rule, **Then** the docs state the rule, say which numbers change in which case, and name `GET /api/version`.

---

### Edge Cases

- The required major version is zero or negative: structure problem. The value is not a whole number: the file is invalid (the file reader refuses it).
- The required major version is missing: the gamepack is accepted (the field stays optional).
- A rollback of a gamepack: the rollback restores content that an earlier install accepted. The version check does not apply to a rollback.
- The required major version is the running major version, but the minor or patch number differs: the install works. Only the major number is compared.
- The dry run and the install give the same result for the version check.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The platform MUST give a read-only endpoint `GET /api/version`. The response MUST have the API major, minor, and patch numbers and the sequence and command schema format version.
- **FR-002**: The API version MUST have one source of truth. The endpoint, the install check, and the docs MUST use the same running major version.
- **FR-003**: The gamepack manifest MUST accept an optional required API major version. A value that is not a whole number MUST make the file invalid (the file reader refuses it). A whole number below 1 MUST be a structure problem.
- **FR-004**: The gamepack install MUST reject a gamepack whose required major version differs from the running major version. The error message MUST name the required version and the running version. The install MUST change nothing (no object, owner entry, or registry entry).
- **FR-005**: The version problem MUST show in the existing problem list with a clear category, so that the dry run and the install give the same answer.
- **FR-006**: A gamepack that states the running major version, or states no version, MUST install as before. The feature MUST NOT change other install behavior.
- **FR-007**: The version check MUST NOT apply to a rollback. A rollback of a stored previous version works even if that version states a different required major. (The test method is in the tasks.)
- **FR-008**: The project docs MUST state the compatibility rule: inside one major version, an existing valid sequence stays valid, and a change that breaks this rule raises the major version. The docs MUST also describe `GET /api/version`, the manifest field, and the install refusal. The docs MUST hold one line in a fixed format (the contract names it), and a test MUST tie that line to the version constants.
- **FR-009**: The API OpenAPI document MUST describe the new endpoint and the manifest field. A contract test MUST check that the swagger document has `/api/version` and the `requiredApiMajor` text.

### Key Entities

- **API version**: Three whole numbers (major, minor, patch) and a schema format version for sequences and commands.
- **Required API major version**: An optional whole number in the gamepack manifest.
- **Version problem**: A problem in the install problem list that names the required and the running major version.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A client reads all four version values with one request.
- **SC-002**: In 100% of tested installs of a gamepack for another major version, the install fails, the message names both versions, and zero objects change.
- **SC-003**: In 100% of tested installs of a gamepack for the running major version or with no stated version, the result equals the result before this feature.
- **SC-004**: An author finds the compatibility rule in the project docs in one document.

## Assumptions

- The running API version is a separate constant (1.0.0), not the product version. It is the one source of truth (FR-002).
- The sequence and command schema format version is a new whole-number constant that starts at 1.
- The manifest field `requiredApiMajor` already exists and is stored but not checked (feature 143). This feature adds the check.
- Out of scope: signature check (FR-024, #307), queue settings export (FR-023, #306), remote catalog (FR-025, #308).
