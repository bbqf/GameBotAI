# Feature Specification: Reject unknown command step fields

**Feature Branch**: `135-reject-unknown-command-fields`  
**Created**: 2026-10-12  
**Status**: Implemented  
**Input**: GitHub issue #237 (B-027): POST /api/commands accepts a step field that the schema excludes (additionalProperties: false) and drops it.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Get an error for an unknown step field (Priority: P1)

An author sends a create-command request. A step in the request has a field that the published schema does not list. For example, the author guesses a field name for the tap config. Today the service answers 201 and drops the field with no sign. The author thinks the command uses the field, but it does not. The service must instead reject the request with an error that names the field.

**Why this priority**: The silent drop hides authoring mistakes. Only a read-back shows the problem. This is the defect in the issue.

**Independent Test**: Send POST /api/commands with a step that holds an unknown field (for example `primitiveTap.bogusField`). Check that the answer is 400 and that the error names the path of the field. Check that no command is stored.

**Acceptance Scenarios**:

1. **Given** a create-command request with a PrimitiveTap step that has an unknown field `bogusField` in `primitiveTap`, **When** the author sends POST /api/commands, **Then** the answer is 400 and the error names the field with its path (`primitiveTap.bogusField`).
2. **Given** the same request, **When** the service rejects it, **Then** no command is created.
3. **Given** a request with unknown fields in two places, **When** the author sends it, **Then** the answer is 400 and the error names all unknown fields, each with its path.

---

### User Story 2 - Valid requests keep working (Priority: P1)

An author sends a create-command request that uses only fields in the published schema. The service must answer as before: 201 and the same body.

**Why this priority**: The change must not break existing clients and sequences.

**Independent Test**: Run the existing command contract and integration tests with no change. They must pass. Send a request with `holdMs` in `primitiveTap` and check that it is stored.

**Acceptance Scenarios**:

1. **Given** a valid create-command request with only known fields, **When** the author sends POST /api/commands, **Then** the answer is 201 and the body is the same as before this change.
2. **Given** a request that uses `holdMs` in `primitiveTap` (a field that exists now), **When** the author sends it, **Then** the answer is 201 and the stored command keeps `holdMs`.
3. **Given** a request with field names in a different letter case from the schema, **When** the author sends it, **Then** the service treats it as before (case-insensitive match is not an unknown field).

---

### User Story 3 - Same rule on the update endpoint (Priority: P2)

The update endpoint for commands (PATCH /api/commands/{id}) uses its own request model with the same step types. An unknown step field in an update request must also give 400 with the field path. The update endpoint uses the same step models. No other endpoint changes.

**Why this priority**: Without it, the same silent drop stays on the update path.

**Independent Test**: Create a command. Send an update request with a step that holds an unknown field. Check for 400, the field path in the error, and that the stored command is unchanged.

**Acceptance Scenarios**:

1. **Given** a stored command, **When** the author sends an update with an unknown step field, **Then** the answer is 400, the error names the field path, and the stored command does not change.
2. **Given** a stored command, **When** the author sends a valid update, **Then** the answer is the same as before this change.

---

### Edge Cases

- Unknown field at the top level of a step (for example `steps[0].bogus`): the error names `steps[0].bogus`.
- Unknown field in a nested object (for example `primitiveTap.detectionTarget.bogus`): the error names the full nested path.
- Unknown field at the top level of the request body: the published schema excludes it, so the answer is 400 with the field name.
- Field name with different letter case from the schema: accepted as before.
- A JSON `null` value for an unknown field: still unknown, so 400.
- Malformed JSON, an empty body, or a wrong value type: the existing error behaviour stays, on both POST and PATCH.
- PATCH for a command that does not exist, with an unknown field: the answer is 404 (the existence check comes first).
- A property that is hidden from the schema and not settable from a request (for example a property that the JSON converter ignores): if a request body holds it, it counts as an unknown field.
- The root-level alias `detectionTarget` that the create endpoint reads today (outside the schema): it stays accepted as before, so valid existing requests do not change. The service does not flag it.
- The existing issue #251 (parameters as an object returns 500) is out of scope and stays as is.

## Clarifications

### Session 2026-10-12

- Q: Must the error list all unknown fields or only the first? -> A: List all unknown fields found in one pass, each with its full path. Rationale: the author fixes all mistakes in one round trip.
- Q: How does the error show the path? -> A: In the existing error response of the endpoint, as text with dotted path and array index, for example `steps[0].primitiveTap.bogusField`. Rationale: the issue example uses a dotted path; a full path from the body root is unambiguous.
- Q: Does the rule apply to unknown fields outside steps (top level of the body)? -> A: Yes, for the command create and update request models, because the schema excludes them too. Rationale: one consistent rule.
- Q: Is the letter case of field names checked? -> A: No. Matching stays case-insensitive as today. Rationale: valid requests must not change.

### Session 2026-10-12 (loop 2)

- Q: Which path form do tests expect? -> A: The full path from the body root, steps[0].primitiveTap.bogusField. Rationale: matches FR-002.
- Q: What does PATCH answer for malformed JSON on a missing command? -> A: Keep the answer of today (record it in the setup task and test it). Rationale: out of scope.
- Q: Which status does a valid PATCH give before the fix? -> A: The status that the setup task records. Rationale: do not guess.

### Session 2026-10-12 (loop 1)

- Q: Does an unknown field with a missing command on PATCH give 404 or 400? -> A: 404. Rationale: the existence check stays first, as today.
- Q: Does a property that the JSON converter ignores count as known? -> A: No, it counts as unknown. Rationale: the body cannot set it, so a silent drop would hide the mistake.
- Q: What happens to the root alias `detectionTarget`? -> A: It stays accepted. Rationale: FR-004; existing valid requests must not change. The scanner does not flag it.
- Q: Does the 400 error carry a hint? -> A: Yes. A hint tells the author to remove the field or check its name in the OpenAPI schema. Rationale: the constitution requires remediation hints.
- Q: Which answers stay for PATCH with an empty or malformed body? -> A: The current ones. Rationale: out of scope.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: POST /api/commands MUST answer 400, not 201, when the request body has a field that the published schema excludes (additionalProperties: false), at the top level of the body, in a step, or in any nested object of a step.
- **FR-002**: The 400 response MUST name every unknown field that the service finds, each with its full path from the body root (for example `primitiveTap.bogusField`).
- **FR-002a**: The 400 response MUST carry a hint that tells the author to remove the field or check its name in the OpenAPI schema.
- **FR-003**: The service MUST NOT store or change any command when it rejects a request for an unknown field.
- **FR-004**: A valid request MUST keep the same answer (201) and the same body as before this change.
- **FR-005**: PATCH /api/commands/{id} MUST apply the same rule to body and step fields, with 404 for a missing command checked first, because it uses the same step models. The service MUST NOT change other endpoints.
- **FR-006**: The published OpenAPI schema MUST stay the same: additionalProperties stays false. The change MUST NOT allow extra fields.
- **FR-007**: The change MUST NOT add step fields, add hold behaviour, change the reschedule-self validator, or fix issue #251. The final review MUST check that the diff touches only the scanner, the commands endpoint file, tests, specs, and docs.
- **FR-008**: A contract test MUST send an unknown step field (a field that does not exist, for example `primitiveTap.bogusField`) and expect 400 with the field path in the error. Tests MUST cover the update endpoint too.

- **FR-009**: The work MUST follow the constitution for a bug fix and an API change: the new tests MUST be run and MUST fail (201 instead of 400) before the fix; `docs/architecture.md` MUST be checked and updated with a new "Last reviewed" date if the API surface description changes; the spec Status line MUST be set to Implemented at the end; `specs/STATUS.md` MUST be updated.

### Key Entities

- **Command create request**: The body of POST /api/commands. It has a name and a list of steps.
- **Command update request**: The body of PATCH /api/commands/{id}. It can carry steps of the same types.
- **Command step**: One step with a type, an order, and a type-specific config object (for example `primitiveTap`).
- **Unknown field**: A field in the request body that the published schema for that object does not list.

## Assumptions

- The update endpoint for commands is PATCH, not PUT. No PUT endpoint exists. The issue text "PUT" means this update endpoint.
- The 400 response uses the existing error shape of the service. The field path is part of the error message text or detail.
- Field names match the schema case-insensitively, as the service does today.
- The issue example field `holdMs` exists now, so tests use `bogusField`.
- The tests for create, valid, and update requests share one test file. Their tasks run in order, not in parallel.
- The scanner unit tests define a small test-only type that has an ignored (`[JsonIgnore]`) property. No production type changes.
- The scan adds no measurable cost to the request path, because the JSON body is small.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of create requests with an unknown step field get 400 with the field path, and 0 such commands are stored.
- **SC-002**: 100% of the existing valid-request tests for commands pass with no change to expected answers.
- **SC-003**: For every rejected request, the error message contains the full path of each unknown field. The contract tests assert this.
- **SC-004**: The published schema for command steps has no change in its set of allowed fields.
