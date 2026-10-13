# Feature Specification: Document the command "parameters" list in OpenAPI

**Feature Branch**: `138-document-command-parameters-openapi`
**Created**: 2026-10-13
**Status**: Draft (becomes Implemented in the final check, FR-011)
**Input**: GitHub issue #247 (tracker row B-030, documentation, P3) - "Document the parameters list in the OpenAPI schema for command create". Closes #247.

## Background

The service accepts a top-level `parameters` list when a command is created, and it returns the list on read-back. The OpenAPI document does not show this list for the create request. The create body schema lists only `name`, `triggerId` and `steps`, and it does not allow other fields.

A command step can use a `{{placeholder}}` in `fieldTemplates`. The service then needs the placeholder to be declared in the command `parameters` list. If it is not declared, the service gives 400 `unresolvable_parameter_reference` with the text "Step '0': 'novaOptionImage' is not declared here and is not a queue built-in." A step-level `parameterBindings` does not satisfy this check. The error text says "not declared here", but the OpenAPI document shows no place to declare the name.

This feature changes the documentation only. It does not change what the service accepts, returns, or checks.

## Clarifications

### Session 2026-10-13

- Q: Does the contract test start a service host or read the document in another way? → A: It uses the same host pattern as the existing OpenAPI contract tests (feature 114). Rationale: this reads the real generated document.
- Q: Is the create example change in scope? → A: Yes, if small. The example adds one `parameters` item and one `fieldTemplates` use of that name. Rationale: the change is a few lines.
- Q: Where does the `fieldTemplates` text go? → A: The existing shared description text is extended. It is one place for all steps. Rationale: one source of truth.
- Q: Must the contract test check the `parameterBindings` limit text? → A: Yes. It checks for the words `parameterBindings` and `unresolvable_parameter_reference`. Rationale: FR-004.

### Session 2026-10-13 (loop 1, from analyze)

- Q: (C1) How does the test check that `text` and `number` are named? → A: The test reads the `type` property of the `parameters` item schema (enum values or description), not the whole document. Rationale: a whole-document search can pass by chance.
- Q: (A1) Is the create example change optional? → A: No. It is in scope and is done (FR-005 is a MUST). Rationale: the change is a few lines and the earlier clarification accepted it.
- Q: (I1) What if update or response already show `parameters`? → A: The test still checks all three and the plan records the result. No change is made to a schema that is already correct.

### Session 2026-10-13 (loop 2, from analyze)

- Q: (D1) Must this feature update the spec Status line and `specs\STATUS.md`? → A: Yes. Principle V requires it. The final task sets Status to Implemented and adds the 138 row. Rationale: constitution rule.
- Q: (I1) Is the example change optional? → A: No. US4 and FR-005 say MUST, with no "if small" wording.
- Q: (U3) Does the `type` check need an enum? → A: No. The description or the enum is accepted; the test passes if `text` and `number` appear in either, in the item `type` schema.
- Q: (U1, U2) Where does the plan record the current item schema and the example location? → A: The first task records both (item schema, and the JSON path of the create example) before any edit. Rationale: the facts are only known from the generated document.
- Q: (V1) Does `docs\architecture.md` change? → A: No. The API surface is the same; only the documentation text changes.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Find the place to declare a placeholder (Priority: P1)

An API client author reads the OpenAPI document for `POST /api/commands`. The author wants to create a command whose step uses a `{{placeholder}}` in `fieldTemplates`. The author finds an optional `parameters` list in the request body. Each item has `name`, `type` (`text` or `number`), `default`, `required` and `description`.

**Why this priority**: This is the gap in the issue. Without it, the author must guess the field name from the error text.

**Independent Test**: Read the generated OpenAPI document. Check that the create request body schema has a `parameters` property that is a list of parameter declarations.

**Acceptance Scenarios**:

1. **Given** the generated OpenAPI document, **When** an author reads the create command request body schema, **Then** the schema has an optional `parameters` property that is an array of parameter declarations with `name`, `type`, `default`, `required` and `description`.
2. **Given** the same document, **When** an author reads the `type` of a declaration, **Then** the document names the values `text` and `number`.

---

### User Story 2 - Understand the declaration rule from the fieldTemplates text (Priority: P1)

An author reads the description of `fieldTemplates` on a command step. The description says that each `{{placeholder}}` must be declared in the top-level `parameters` list of the command, or be a queue built-in. It says that a step-level `parameterBindings` does not satisfy this check. It names the error `unresolvable_parameter_reference`.

**Why this priority**: The author sees this text first when the author writes a step. The text prevents the 400 error.

**Independent Test**: Read the generated OpenAPI document. Check the `fieldTemplates` description for the three statements.

**Acceptance Scenarios**:

1. **Given** the generated OpenAPI document, **When** an author reads the `fieldTemplates` description, **Then** it states the declaration rule, the queue built-in exception, the `parameterBindings` limit, and the error code `unresolvable_parameter_reference`.

---

### User Story 3 - Create, update and response schemas agree (Priority: P2)

An author compares the create, update and response schemas for a command. All three show the `parameters` list.

**Why this priority**: A schema that shows the list in one place only misleads the author, but the main gap is the create body.

**Independent Test**: Read the generated OpenAPI document. Check that the three schemas have `parameters`.

**Acceptance Scenarios**:

1. **Given** the generated OpenAPI document, **When** an author reads the command create, update and response schemas, **Then** each schema has a `parameters` property.
2. **Given** a schema that does not show `parameters`, **When** the check runs, **Then** the schema is corrected in this feature.

---

### User Story 4 - Create example shows parameters (Priority: P3)

The request example for command create includes a `parameters` list and a step that uses the declared name in `fieldTemplates`. This story is required (MUST). The change is a few lines.

**Why this priority**: An example helps, but the schema and text carry the rule.

**Independent Test**: Read the create request example in the generated OpenAPI document.

**Acceptance Scenarios**:

1. **Given** the generated OpenAPI document, **When** an author reads the create request example, **Then** the example has a `parameters` list and every placeholder in the example is declared in it.

### Edge Cases

- A command without `parameters` stays valid. The property is optional, and the schema does not mark it as required.
- The `default` of a declaration can be absent. The schema marks it as optional, and the test checks this (FR-001a).
- `ParameterDeclarationDto` is internal. The document must still show all five item properties; the test fails if the item schema is empty or has a missing property.
- A placeholder that is a queue built-in needs no declaration. The description says this.
- The update schema and the response schema already use the same declaration type. If the generated document shows a difference, the difference is corrected.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The OpenAPI create command request body schema MUST document an optional `parameters` property. It MUST be a list of parameter declarations with `name`, `type` (`text` or `number`), `default`, `required` and `description`.
- **FR-001a**: The generated schema of one `parameters` item MUST have all five properties: `name`, `type`, `default`, `required` and `description`. The `default` property MUST be optional (not in the `required` list of the item schema). The declaration type `ParameterDeclarationDto` is internal, so the generator MUST inline it or make it public, so that the five properties show in the document. The contract test MUST check the five property names on the item schema. The test MUST accept `text` and `number` in either the enum or the description of the item `type` schema.
- **FR-001b**: Before any edit, the work MUST record (a) the current generated schema of one `parameters` item, and (b) the JSON path of the create request example in the generated document. It MUST also state the exact condition that triggers the fallback (inline the type or make it public): the `default` property is listed in the item `required` list, or one of the five item properties is missing.
- **FR-002**: The OpenAPI create, update and response schemas for a command MUST each show `parameters`. The check MUST read the generated OpenAPI document. The create check is done once (it is not repeated in other requirements). The update check and the response check are separate checks. A schema that lacks the property MUST be corrected.
- **FR-003**: The `fieldTemplates` description MUST state that each `{{placeholder}}` is declared in the top-level `parameters` list of the command, or is a queue built-in.
- **FR-004**: The `fieldTemplates` description MUST state that a step-level `parameterBindings` does not satisfy this check, and MUST name the error `unresolvable_parameter_reference`.
- **FR-005**: The create request example MUST include a `parameters` list. Each placeholder in the example MUST be declared.
- **FR-006**: A contract test MUST read the generated OpenAPI document and check FR-001, FR-001a and FR-002 to FR-005. It MUST sit with the existing OpenAPI contract tests and follow the pattern of the reference-image schema filter tests (feature 114).
- **FR-007**: The change MUST NOT alter run-time behavior: validation rules, error codes, error messages, accepted input and returned output stay as they are. Sequence schemas, queue template schemas and the web UI do not change.
- **FR-008**: The product minor version MUST change from 1.8.0 to 1.9.0 by editing only the product version override file (`minor` "9", `updatedAtUtc` set to the current UTC time). Other version files MUST NOT change.
- **FR-009**: The change log MUST have a short entry under Unreleased. It MUST say that the minor version is now 9 (1.9.x line).
- **FR-010**: The final check MUST confirm that the product version override file has `minor` "9", and that the change log has the Unreleased entry from FR-009. A task MUST do this check before the work is done.
- **FR-011**: As part of the final check (Principle V), the Status line of this spec MUST change to Implemented, and `C:\src\GameBot\specs\STATUS.md` MUST get a row for 138 in the same form as rows 135 to 137 (`| 138 | <title> | Implemented |`).
- **FR-012**: `docs\architecture.md` does not change. The API surface is the same; only the documentation text changes.

### Key Entities

- **Command create request schema**: The documentation-only body schema for `POST /api/commands`. It gains the `parameters` property.
- **Parameter declaration**: One item of the `parameters` list. It has `name`, `type`, `default`, `required` and `description`.
- **fieldTemplates description**: The shared text on a step property that maps a field path to a template with placeholders.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In the generated OpenAPI document, 3 of 3 command schemas (create, update, response) show `parameters`.
- **SC-002**: An author finds the declaration place, the rule and the error code from the OpenAPI document alone, with no need to send a failing request first.
- **SC-003**: The contract test passes, and it fails if any of the points in FR-001, FR-001a, FR-002, FR-003, FR-004 or FR-005 is removed.
- **SC-004**: All existing contract and unit tests pass with no change to their expected values, which shows no run-time change.
- **SC-005**: The final check shows `minor` "9" in the product version override file and the Unreleased entry in the change log (FR-008, FR-009, FR-010). It also shows Status Implemented in this spec and the 138 row in `specs\STATUS.md` (FR-011).
- **SC-006**: The contract test shows that the `parameters` item schema has the five properties `name`, `type`, `default`, `required` and `description`, and that `default` is optional (FR-001a).

## Assumptions

- The update and response schemas probably already show `parameters`, because the request and response models carry it. The contract test confirms this.
- The create schema is documentation-only, so a change to it has no run-time effect.
- The `type` values `text` and `number` are the values that the service accepts today.
