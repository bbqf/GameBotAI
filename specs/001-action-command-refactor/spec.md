# Action/Command Refactor Specification

## Summary

Refactor the domain to replace the concept of Profile with Action, fully decouple Triggers from Actions, and eliminate all automated background trigger evaluation. Introduce a new top-level object Command that executes when its associated Trigger evaluates to "Satisfied". A Command can contain an ordered sequence of Actions and/or nested Commands. Provide a way to force-execute a Command, ignoring its Trigger. Automated execution/orchestration of Commands is out of scope and will be handled by a future feature.

## Goals

- Rename Profile to Action everywhere user-facing (domain, persistence, API contracts) where applicable for this feature.
- Make Triggers standalone (no implicit binding to Actions) and remove automatic/background evaluation.
- Add Command as an executable composite that may reference Actions and/or other Commands.
- Allow associating an optional Trigger to a Command; when explicitly requested, execute the Command only if the Trigger evaluates to "Satisfied".
- Provide an explicit force-execute operation that runs a Command regardless of Trigger evaluation.
- Do not introduce any background or periodic evaluation/execution in this feature.

## Non-Goals (Out of Scope)

- Any background scheduler/worker to automatically evaluate Triggers or execute Commands.
- Optimization of execution performance, concurrency controls, or retries beyond functional correctness.
- Advanced routing/branching logic within Commands (e.g., conditionals, loops).

## Actors

- Operator: creates and manages Actions, Triggers, and Commands; manually requests evaluation or execution.
- System: validates definitions and executes Actions in the order defined by a Command when requested.

## Assumptions

- A Command may have at most one associated Trigger; Commands may also have none (always eligible to run when requested).
- Command composition must be acyclic; the system prevents Command dependency cycles at creation/update time.
- Action remains the atomic executable unit (formerly Profile), keeping current action semantics intact (execution details unchanged in this feature).
- Existing background-trigger worker(s) are removed/disabled as part of this change.

## Backward Compatibility Strategy (Resolved)

- Decision: Breaking rename now. External API, contracts, and persistence naming migrate from "Profile" to "Action" as part of this feature. No dual naming/deprecation window.
- Migration notes to include: updated endpoint paths, payload property names, persistence file keys/paths, and client update guidance.

## User Scenarios & Testing

1. Define an Action
  - Operator creates an Action (formerly Profile) with required parameters.
  - Verify Action is persisted and retrievable by ID.

2. Create a Trigger
  - Operator defines a Trigger independently of Actions/Commands.
  - Verify Trigger can be evaluated on demand for a given context and returns Satisfied/NotSatisfied.

3. Create a Command with steps
  - Operator creates a Command consisting of an ordered list of steps where each step is either an Action reference or a Command reference.
  - System validates that the composition is acyclic and that referenced entities exist.

4. Associate a Trigger to a Command
  - Operator associates an existing Trigger to a Command.
  - When the Operator requests "evaluate-and-execute" for that Command, the system evaluates the Trigger; if Satisfied, the Command executes its steps; if NotSatisfied, nothing executes and the result is returned as such.

5. Force execute a Command
  - Operator requests force-execute on a Command; system bypasses Trigger evaluation and executes steps.

6. No background activity
  - After deployment, no background/periodic evaluation of Triggers exists; only explicit requests perform evaluations.

## Functional Requirements

- FR1: The system must expose CRUD operations for Action, Trigger, and Command entities.
- FR2: A Command must accept a list of steps; each step references either an Action or another Command; order is preserved.
- FR3: The system must validate that Command composition is acyclic; creating/updating a Command that introduces a cycle must fail.
- FR4: A Command may have zero or one associated Trigger; association is optional and editable.
- FR5: Provide an operation to evaluate a Command’s Trigger and, if Satisfied, execute the Command; if NotSatisfied, do not execute and return a clear status.
- FR6: Provide an operation to force execute a Command, bypassing Trigger evaluation entirely.
- FR7: Remove/disable any automated/background Trigger evaluation and any automatic execution flows.
- FR8: Execution must process Command steps in order and stop on first failure, returning an execution result with failure details.
- FR9: All operations must be idempotent from the client perspective for create/update requests (e.g., safe retries do not duplicate entities or steps).

## Success Criteria

- SC1: Operator can define Actions, Triggers, and Commands and retrieve them successfully (100% CRUD coverage for the three entities).
- SC2: Command composition rejects cyclic references in 100% of attempted cycles and accepts acyclic compositions.
- SC3: Evaluate-and-execute returns "Executed" when Trigger is satisfied and "Skipped" when not satisfied, within 1 second for typical definitions.
- SC4: Force-execute runs 100% of eligible Commands regardless of Trigger state.
- SC5: No background trigger evaluations occur post-change (verified via logs/config and absence of background worker).

## Key Entities

- Action: atomic executable unit (replaces Profile).
- Trigger: condition definition evaluated explicitly; independent of Actions.
- Command: composite executable; ordered list of steps where each step is an ActionRef or CommandRef; optional Trigger association.
- CommandStep: one item in a Command sequence, with type (Action|Command), reference ID, and position.
- ExecutionRequest: input model for evaluate-and-execute and force-execute operations; returns ExecutionResult with status and details.

## Dependencies & Risks

- Migration from Profile to Action may affect external clients if naming is breaking; see Backward compatibility.
- Removing background workers could impact any workflows implicitly relying on them; documentation/update notes required.
- Deeply nested Commands increase execution time and complexity; guardrails via max depth may be considered in future.

## Acceptance Tests (High-Level)

- Create Action/Trigger/Command → read back → matches definitions.
- Create Command with nested Commands → acyclic → success; introduce cycle → validation error.
- Associate Trigger to Command → evaluate-and-execute (Satisfied) → steps executed; (NotSatisfied) → no execution.
- Force execute → steps executed regardless of Trigger state.
- Verify no background evaluation logs or worker activity after deployment.

## Completion

SUCCESS (spec ready for planning)

# Feature Specification: [FEATURE NAME]

**Feature Branch**: `[###-feature-name]`  
**Created**: [DATE]  
**Status**: Draft  
**Input**: User description: "$ARGUMENTS"

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

### User Story 1 - [Brief Title] (Priority: P1)

[Describe this user journey in plain language]

**Why this priority**: [Explain the value and why it has this priority level]

**Independent Test**: [Describe how this can be tested independently - e.g., "Can be fully tested by [specific action] and delivers [specific value]"]

**Acceptance Scenarios**:

1. **Given** [initial state], **When** [action], **Then** [expected outcome]
2. **Given** [initial state], **When** [action], **Then** [expected outcome]

---

### User Story 2 - [Brief Title] (Priority: P2)

[Describe this user journey in plain language]

**Why this priority**: [Explain the value and why it has this priority level]

**Independent Test**: [Describe how this can be tested independently]

**Acceptance Scenarios**:

1. **Given** [initial state], **When** [action], **Then** [expected outcome]

---

### User Story 3 - [Brief Title] (Priority: P3)

[Describe this user journey in plain language]

**Why this priority**: [Explain the value and why it has this priority level]

**Independent Test**: [Describe how this can be tested independently]

**Acceptance Scenarios**:

1. **Given** [initial state], **When** [action], **Then** [expected outcome]

---

[Add more user stories as needed, each with an assigned priority]

### Edge Cases

<!--
  ACTION REQUIRED: The content in this section represents placeholders.
  Fill them out with the right edge cases.
-->

- What happens when [boundary condition]?
- How does system handle [error scenario]?

## Requirements *(mandatory)*

<!--
  ACTION REQUIRED: The content in this section represents placeholders.
  Fill them out with the right functional requirements.
-->

### Functional Requirements

- **FR-001**: System MUST [specific capability, e.g., "allow users to create accounts"]
- **FR-002**: System MUST [specific capability, e.g., "validate email addresses"]  
- **FR-003**: Users MUST be able to [key interaction, e.g., "reset their password"]
- **FR-004**: System MUST [data requirement, e.g., "persist user preferences"]
- **FR-005**: System MUST [behavior, e.g., "log all security events"]

*Example of marking unclear requirements:*

- **FR-006**: System MUST authenticate users via [NEEDS CLARIFICATION: auth method not specified - email/password, SSO, OAuth?]
- **FR-007**: System MUST retain user data for [NEEDS CLARIFICATION: retention period not specified]

### Key Entities *(include if feature involves data)*

- **[Entity 1]**: [What it represents, key attributes without implementation]
- **[Entity 2]**: [What it represents, relationships to other entities]

## Success Criteria *(mandatory)*

<!--
  ACTION REQUIRED: Define measurable success criteria.
  These must be technology-agnostic and measurable.
-->

### Measurable Outcomes

- **SC-001**: [Measurable metric, e.g., "Users can complete account creation in under 2 minutes"]
- **SC-002**: [Measurable metric, e.g., "System handles 1000 concurrent users without degradation"]
- **SC-003**: [User satisfaction metric, e.g., "90% of users successfully complete primary task on first attempt"]
- **SC-004**: [Business metric, e.g., "Reduce support tickets related to [X] by 50%"]
