# Feature Specification: Primitive Actions Data Model Refactor

**Feature Branch**: `[001-primitive-actions-refactor]`  
**Created**: 2026-05-25  
**Status**: Implemented
**Input**: User description: "Let's do a deep refactor of data model. Actions are not used, only their type is relevant. Remove Actions from backend, frontend, and tests. Make Primitive Action a separate data model object corresponding to current action types. Primitive actions should be selectable where Actions are selectable. Keep existing primitive-action parameter behavior where already configured; only connect-to-game should expose parameters in the execution tab UI."

## Clarifications

### Session 2026-05-25

- Q: What legacy strategy should this feature use for existing Action storage/endpoints? → A: Hard cutover with immediate removal (no compatibility bridge).
- Q: What primitive action data model shape should be adopted? → A: Typed per-type variants with a shared base (discriminated model).
- Q: How should commands/sequences/execution persist primitive selections? → A: Inline by value (embed type + typed payload per usage).
- Q: What cutover enforcement mode should be used for legacy references? → A: Fail fast at startup/deployment; block rollout until migration completes.

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

### User Story 1 - Author Flows With Primitive Actions (Priority: P1)

As an automation author, I can create and edit commands and sequences by selecting primitive actions directly, without creating or managing separate Action records.

**Why this priority**: This is the core value of the refactor and removes the largest source of model complexity and authoring friction.

**Independent Test**: Can be fully tested by creating a new command and a new sequence that use primitive action selections end-to-end, then saving and reloading them without any Action CRUD usage.

**Acceptance Scenarios**:

1. **Given** an author is creating a command or sequence step, **When** they choose an action behavior, **Then** they can select a primitive action type directly and save successfully.
2. **Given** previously saved command/sequence definitions, **When** the author opens them after refactor, **Then** primitive action selections are shown correctly without broken references to removed Action entities.

---

### User Story 2 - Start Sessions With Connect Primitive Action (Priority: P2)

As an operator in the execution view, I can select a connect-to-game primitive action and provide its required connection parameters so sessions can start reliably.

**Why this priority**: Session start is operationally critical and is the one explicit case where parameter entry is required in the execution tab.

**Independent Test**: Can be tested by selecting a connect-to-game primitive action in execution, entering required values, starting a session, and then executing a command that reuses that session context.

**Acceptance Scenarios**:

1. **Given** connect-to-game primitive actions are available, **When** an operator selects one in execution, **Then** required connection parameters are displayed and validated.
2. **Given** a valid connect-to-game selection, **When** the operator starts a session, **Then** the session starts and can be reused by command execution flows.

---

### User Story 3 - Preserve Existing Automation Behavior (Priority: P3)

As a maintainer, I can migrate existing data and test coverage to the primitive-action model so existing automation outcomes remain stable after the refactor.

**Why this priority**: Refactor value is lost if existing commands/sequences or regression suites break.

**Independent Test**: Can be tested by running a representative pre-existing data set and regression suite before/after refactor and confirming equivalent outcomes.

**Acceptance Scenarios**:

1. **Given** existing stored definitions using Action references, **When** migration/translation is applied, **Then** definitions remain usable without manual edits.
2. **Given** existing regression tests that cover action-driven execution, **When** tests are updated to primitive-action terms, **Then** they verify equivalent behavior and pass.

### Edge Cases

- Startup validation finds any legacy Action identifier in persisted data; the service must fail startup or deployment readiness until migration completes.
- Persisted primitive data is structurally valid for one type variant but tagged with a different type discriminator.
- A command/sequence step has a primitive selection marker but missing embedded typed payload.
- A primitive action type is selected in a context where no parameters are required, but stale parameters exist from older saved data.
- A connect-to-game primitive action is selected in execution without required connection values.
- A user edits a flow while another process updates related definitions, causing stale references.
- An unsupported or unknown primitive action type appears in imported or manually edited data.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST remove Action as a first-class authored entity from backend contracts, frontend authoring flows, and automated tests.
- **FR-002**: The system MUST introduce Primitive Action as a first-class data model object that is selectable anywhere Action selection is currently supported.
- **FR-003**: The system MUST support primitive action types equivalent to current supported action types, using a single canonical type vocabulary and explicit type discriminator.
- **FR-004**: The system MUST allow commands and sequences to persist primitive action selections inline by value (type discriminator plus matching typed payload) without Action identifiers.
- **FR-005**: The system MUST preserve existing primitive action parameter behavior in contexts where primitive parameters are already authored/configured.
- **FR-006**: The system MUST treat primitive action types used only as structural selections as parameterless by default.
- **FR-007**: The execution tab MUST present and validate required parameters for connect-to-game primitive action selection before session start.
- **FR-008**: The system MUST provide deterministic validation errors when a primitive action selection is invalid, unknown, or missing required data for its context.
- **FR-009**: The system MUST perform a hard cutover by removing Action compatibility pathways in the same release and fail startup/deployment readiness when legacy Action-based references are detected.
- **FR-010**: The system MUST remove or replace Action-specific API/UI pathways so users cannot create dependency on deprecated Action records.
- **FR-011**: The system MUST update automated tests to validate primitive-action behavior across domain logic, service endpoints, and UI flows.
- **FR-012**: The system MUST keep connect-to-game session reuse behavior functionally equivalent after refactor.
- **FR-013**: The system MUST provide a deterministic pre-cutover migration process for persisted data so production data can be converted before rollout.
- **FR-014**: The system MUST model Primitive Action as a shared base plus typed per-type variants, with validation that enforces payload schema consistency for each type.
- **FR-015**: The system MUST not require a new global primitive-action repository or lookup identifier for command/sequence/execution persistence.
- **FR-016**: The system MUST emit deterministic startup validation diagnostics listing every legacy Action reference that blocks cutover.

### Key Entities *(include if feature involves data)*

- **Primitive Action (Base)**: Represents shared discriminator and common metadata fields for all primitive actions.
- **Primitive Action Variant**: Represents a concrete per-type primitive action payload that is valid only for its matching discriminator.
- **Primitive Action Selection**: Represents an inline-by-value usage of a primitive action within command/sequence/execution flows, including embedded discriminator and typed payload.
- **Connect Session Selection**: Represents the execution-tab connect-to-game primitive action choice plus required connection values used to start/reuse sessions.
- **Migration Mapping Record**: Represents deterministic pre-cutover metadata that maps prior Action-based references to primitive-action selections.

### Assumptions

- Existing saved data using Action references will be migrated before rollout; no read/write compatibility bridge will exist after cutover.
- Service startup/deployment health checks are allowed to block release when legacy Action references are detected.
- Primitive action types remain semantically unchanged during this refactor; this feature changes modeling and usage, not user-visible behavior of each type.
- Only connect-to-game requires explicit execution-tab parameter entry in this phase; other execution-tab primitive selections are parameterless unless already explicitly configured elsewhere.
- Audit/history retention behavior remains unchanged unless required for migration traceability.

### Dependencies

- Existing command and sequence authoring flows must adopt primitive action selection consistently.
- Execution session start/reuse flows depend on connect-to-game primitive action selection and validation.
- Existing persisted definitions and regression suites are required as migration verification baseline before cutover.
- Deployment pipeline/runtime host must support startup readiness failure signaling for migration-blocking validation errors.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of authoring flows that previously required Action selection can be completed using primitive action selection only.
- **SC-002**: 100% of persisted Action-based definitions in the cutover validation corpus are converted before release, or deployment is blocked.
- **SC-003**: 100% of execution-tab session starts initiated from connect-to-game primitive selections require and validate the required connection values before submission.
- **SC-004**: Regression pass rate for pre-existing action-related test suites returns to baseline (no net increase in failing scenarios attributable to the model refactor).
- **SC-005**: User-reported confusion related to choosing between Action records and primitive behavior is reduced to zero in acceptance walkthroughs because only primitive action choices are presented.
- **SC-006**: In cutover validation runs with intentionally un-migrated data, service startup/deployment readiness fails with deterministic diagnostics until data is fully migrated.
