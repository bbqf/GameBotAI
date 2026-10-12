# Feature Specification: Reject a parameters object in command requests

**Feature Branch**: `136-reject-command-parameters-object`  
**Created**: 2026-10-12  
**Status**: Implemented  
**Input**: GitHub issue #251 (B-033): POST /api/commands with `parameters` as an object instead of a list returns 500, not 400. Closes #251.

## Clarifications

### Session 2026-10-12

- Q: Which update call is in scope (the issue says PUT; feature 135 names PATCH)? → A: Every update call for commands that binds the same `parameters` field. The planner finds them in the code.
- Q (analyze D1): Is there a PUT for commands? → A: No. The only update call is PATCH /api/commands/{id}. The spec uses "update request" for PATCH.
- Q (analyze U3, F1): What is the answer for `parameters: null` and `parameters: []`, and for other malformed PATCH bodies? → A: Each keeps the answer it has on unchanged code. A test records this baseline first, then asserts it. Only `parameters` type errors change. Other malformed bodies keep their current answer and format.
- Q (analyze C1, C2): Do string, number, and non-object list item values get tests on PATCH? → A: Yes. PATCH gets the same value cases as POST.
- Q (analyze U4): What is the answer when a list item has a wrong-typed field (for example `parameters[0].required` is a string)? → A: 400 that names `parameters`, the same as other `parameters` type errors.
- Q (analyze C1): For PATCH, does the `parameters` check come before the stored-command lookup? → A: Yes. A bad `parameters` type gives 400 for any id, and the service changes nothing.
- Q: Must the 400 error use a new error code? → A: No. Use the existing code for malformed bodies on the same endpoint. The message must contain `parameters`.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Get a 400 when parameters is an object (Priority: P1)

An author sends a create-command request. The `parameters` value is one JSON object, not a list of declarations. For example, a PowerShell client turns a one-item list into a single object, and the author does not see it. Today the service answers 500 with an "internal_error" code. A 500 shows a service fault, but the fault is in the request. The service must answer 400 with a validation error that names `parameters`.

**Why this priority**: This is the defect in the issue. A wrong status code sends the author to look for a fault in the service.

**Independent Test**: Send POST /api/commands with `"parameters": {"name":"novaOptionImage","type":"text","required":true}` and a valid PrimitiveTap step. Check that the answer is 400, that the error names `parameters`, and that no command is stored.

**Acceptance Scenarios**:

1. **Given** a create-command request with `parameters` as one JSON object, **When** the author sends POST /api/commands, **Then** the answer is 400 and the error names `parameters`.
2. **Given** the same request, **When** the service rejects it, **Then** no command is created.
3. **Given** the same request, **When** the service rejects it, **Then** the answer body has the same error format as other 400 answers for a malformed body.

---

### User Story 2 - Valid requests keep working (Priority: P1)

An author sends a create-command request with `parameters` as a list of declarations, or with no `parameters`. The service must answer as before.

**Why this priority**: The change must not break existing clients and sequences.

**Independent Test**: Run the existing command contract and integration tests with no change. They must pass.

**Acceptance Scenarios**:

1. **Given** a create-command request with `parameters` as a list of valid declarations, **When** the author sends it, **Then** the answer is 201 and the body is the same as before.
2. **Given** a create-command request with no `parameters`, **When** the author sends it, **Then** the answer is 201 and the body is the same as before.
3. **Given** a request with `parameters` as an empty list, **When** the author sends it, **Then** the answer is the same as before this change.

---

### User Story 3 - Same rule on the update endpoint (Priority: P2)

The update request for a command uses its own request model that also has `parameters`. The planner must check if the update request has the same defect. If it does, an update request with `parameters` as an object must also give 400 with an error that names `parameters`, and the stored command must not change.

**Why this priority**: Without it, the same 500 stays on the update path.

**Independent Test**: Create a command. Send an update request with `parameters` as an object. Check for 400, `parameters` in the error, and that the stored command is unchanged.

**Acceptance Scenarios**:

1. **Given** a stored command, **When** the author sends an update with `parameters` as an object, **Then** the answer is 400, the error names `parameters`, and the stored command does not change.
2. **Given** a stored command, **When** the author sends a valid update, **Then** the answer is the same as before this change.

---

### Edge Cases

- `parameters` is a string, a number, or `true`: the answer is 400 and names `parameters`, in the same way as for an object.
- `parameters` is `null`: the answer is the same as before this change.
- `parameters` is a list that holds an item that is not an object (for example a number): the answer is 400 and the error names `parameters`.
- Another field in the same request has a wrong type: the answer stays 400 and names that field.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The service MUST answer HTTP 400, not 500, when the `parameters` value of a create-command request is a JSON object.
- **FR-002**: The 400 error MUST name `parameters`.
- **FR-003**: The 400 answer MUST use the same error format and handling as other malformed-body answers on the same endpoint.
- **FR-004**: The service MUST NOT create or change a command when it rejects a request for this reason.
- **FR-005**: The service MUST NOT accept a single object as a list of one declaration.
- **FR-006**: The service MUST give the same 400 answer for the update request, if the update request has the same defect today.
- **FR-007**: The service MUST NOT change the answer for requests where `parameters` is a list, is absent, or is `null`.
- **FR-008**: This change MUST NOT change other endpoints, the parameter schema, or the request fields.

### Key Entities *(include if feature involves data)*

- **Command request**: The body of a create or update command call. It has an optional `parameters` list.
- **Parameter declaration**: One item of the `parameters` list. It has `name`, `type`, and `required`. This feature does not change it.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of create-command requests with `parameters` as an object get HTTP 400, and 0% get HTTP 500.
- **SC-002**: In 100% of these 400 answers, the error text contains `parameters`.
- **SC-003**: All existing command contract and integration tests pass with no change.
- **SC-004**: If the update request had the same defect, 100% of update requests with `parameters` as an object get HTTP 400.

## Assumptions

- The existing handling for malformed bodies on the command endpoints (from feature 135) is the "same 400 handling" that the issue asks for.
- A strict reject is the wanted result. The service does not convert an object to a one-item list (the issue asks for 400, not for leniency).
- The parameter schema and other endpoints do not change.
- The planner and the implementer must verify the real throw site and the real `JsonException` path of the 500 for a wrong-type `parameters`. The unknown-field scanner runs before `Deserialize` in `CommandsEndpoints.cs` (about line 366) and could also throw on an object value. The fix covers every throw site that causes the 500.
- The update body reader must be able to return a 400 result for a `parameters` type error. It must do this without a stored-command lookup and without a change to the stored command. The plan chooses the exact form (for example, a third tuple member for an error result) and lists all callers as edit points.
- The tasks must name the integration test filter and path for the command tests, so that the implementer can run them.
- If an unused helper breaks the build (warnings as errors), the helper is added in the same task as its first use.
- The PATCH baseline (500 or 400 for a wrong-type `parameters`) is not proven until a test records it. The record is a note in the task result. It is not a committed test.
- The tasks must name exact calls. The "no command stored" check uses the `GET /api/commands` list, filtered by the unique command name. The build step uses the exact solution path.
