# Feature Specification: Reject a malformed sequence step on create

**Feature Branch**: `ccr-1ebf2553-mqz6yt` (spec number 113)  
**Created**: 2026-09-28  
**Status**: Implemented  
**Input**: GitHub issue #242 (B-028): "POST /api/sequences stores a sequence with zero steps when a step has commandReference and no stepType, and ignores dryRun". Full description: see the issue and the feature description that started this spec.

## Background

The issue owner sent this body to `POST /api/sequences`. The command id is the id of a command that exists.

```json
{"name":"ZZZ.DropProbe","steps":[
  {"stepId":"a","commandReference":{"commandId":"<valid id>"}},
  {"stepId":"b","primitiveAction":{"type":"reschedule-self","schemaVersion":"v1","payload":{"option":"AtQueueStart"}}}]}
```

The service returned 201. The response and `GET /api/sequences/{id}` showed `"steps":[]` and `"parameters":null`. The service dropped the valid step `b` and the declared parameters too. With `"dryRun": true`, the service returned 201 with a new sequence id, and it stored a sequence with zero steps.

For comparison, the same first step with `"stepType":"Action"` gets 400. A valid body with `dryRun: true` gets 200 `{"valid":true,"dryRun":true,"errors":[]}`, and the service stores nothing.

A code review found the cause. The endpoint looks only at the first step to select the body shape. When the first step has no `stepType` and no `primitiveAction`, the endpoint uses the old "list of command ids" shape. That shape reads only the string items of `steps`, so it drops each object step. It also ignores `parameters` and `dryRun`.

## Clarifications

### Session 2026-09-28

The pipeline ran without a human reviewer. Thus the clarify step selected each answer. Each answer has a rationale.

- Q: Does the service accept a step that has only `commandReference` (no `stepType`, no `primitiveAction`) as a command step? → A: No. The service rejects the step with 400. Rationale: the code has no documented mapping for this shape. `commandReference` is only a name label for a step of type `command`, and the service reads the command id from `primitiveAction.payload.commandId`. The per-step reader already requires `primitiveAction` for each Action step. The issue permits a rejection.
- Q: How does the endpoint select the body shape? → A: When one or more items of `steps` is a JSON object, the body has the per-step shape, and the endpoint reads and checks each step. There is one exception: a body with a `blocks` property keeps the domain shape, unless a step has `stepType` or `primitiveAction` (as before). The old shape applies only when each item of `steps` is a string. Rationale: one object step is enough to show that the author did not use the old shape. The first step alone is not a safe signal. The exception keeps the current `blocks` bodies unchanged.
- Q: What does the error text show? → A: Each shape error of the per-step reader starts with the position and the id of the step, for example `steps[0] (stepId 'a'): each action step must include primitiveAction object.` Rationale: the issue asks for an error that names the step by its id or position. The text after the prefix does not change.
- Q: What does the service do with a body in the old shape that it cannot map in full? → A: It rejects the body with 400 when an item of `steps` is not a string (for example, a number or `null`) or when the body declares `parameters`. The old shape cannot keep parameters. Rationale: the issue forbids a silent drop. A body in the old shape without these items does not change.
- Q: Does `dryRun` apply to each body shape on create? → A: Yes. A create with `dryRun: true` never stores a sequence. For a valid body in the old shape or in the domain shape, the service returns the same 200 `{ valid: true, dryRun: true, errors: [] }` as for the per-step shape. Rationale: the issue requires that a dry run never stores a sequence. `PUT` and `PATCH` already read `dryRun` from the raw body for each shape (feature 091).
- Q: Do `PUT /api/sequences/{id}` and `PATCH /api/sequences/{id}` change? → A: Yes, only for the shape selection. They use the same selection code, so a malformed object step now gets the same 400. Before, the service ignored the steps of such a body and kept the stored steps. Their `dryRun` behavior does not change. Rationale: the same code serves the three routes. The issue permits this change and excludes the other `PUT` dry-run issue (#177) and `POST /api/commands` (#237).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A malformed step gets a 400 that names the step (Priority: P1)

A sequence author sends `POST /api/sequences` with a step that has no `stepType` and no `primitiveAction`. The service rejects the body with 400. The error names the step by its position and its id. The service stores nothing.

**Why this priority**: Today the service returns 201 and stores an empty sequence. The author thinks that the save was correct. A queue that runs the sequence does nothing.

**Independent Test**: Send the reproduction body. The response is 400 with an error that has `steps[0]` and `a`. `GET /api/sequences` does not show the name.

**Acceptance Scenarios**:

1. **Given** a command that exists, **When** the author sends the reproduction body, **Then** the response is 400, one error has `steps[0] (stepId 'a')`, and no sequence with the name `ZZZ.DropProbe` is stored.
2. **Given** the same body with `"stepType":"Action"` on step `a`, **When** the author sends it, **Then** the response is 400 with the same error text.
3. **Given** a body whose first step is valid and whose second step has only `commandReference`, **When** the author sends it, **Then** the response is 400 and the error names `steps[1]`.
4. **Given** a stored sequence, **When** the author sends `PUT /api/sequences/{id}` with the reproduction steps, **Then** the response is 400 and the stored sequence does not change.

---

### User Story 2 - A dry run never stores a sequence (Priority: P1)

A sequence author sends a create with `dryRun: true`. The service checks the body and never stores a sequence, for each body shape.

**Why this priority**: A dry run that stores a sequence breaks the contract of `dryRun`, and it adds empty sequences to the store.

**Independent Test**: Send the reproduction body with `dryRun: true`. The response is 400 and the count of stored sequences does not change. Send a valid body in the old shape with `dryRun: true`. The response is 200 with `valid: true`, and the count does not change.

**Acceptance Scenarios**:

1. **Given** the reproduction body with `"dryRun": true`, **When** the author sends it, **Then** the response is 400 with the same errors as without `dryRun`, and the service stores nothing.
2. **Given** a body in the old shape (`steps` is a list of strings) with `"dryRun": true`, **When** the author sends it, **Then** the response is 200 `{ valid: true, dryRun: true, errors: [] }` and the service stores nothing.

---

### User Story 3 - Valid bodies keep their behavior (Priority: P1)

A sequence author sends a valid per-step body with `stepType` and a `primitiveAction` of type `command` with `payload.commandId`, and declared `parameters`. The service creates the sequence with all steps and all parameters, as before.

**Why this priority**: The fix must not break the shape that works today.

**Independent Test**: Create a valid body with two steps and one parameter. The response is 201. `GET /api/sequences/{id}` shows two steps and one parameter.

**Acceptance Scenarios**:

1. **Given** a valid per-step body with a command step, a `reschedule-self` step and one declared parameter, **When** the author sends it, **Then** the response is 201, and the stored sequence has the two steps and the parameter.
2. **Given** a body in the old shape with string command ids and no `parameters`, **When** the author sends it, **Then** the response is 201 as before.

---

### User Story 4 - The API documentation tells the rule (Priority: P3)

A client developer reads the OpenAPI description of `POST /api/sequences`. The description tells that `dryRun` applies to each body shape and that a malformed step gets 400.

**Why this priority**: The current description says that `dryRun` applies only to the per-step shape. That text is not true after the change.

**Independent Test**: Read `/swagger/v1/swagger.json`. The description of the create operation has the new text.

**Acceptance Scenarios**:

1. **Given** the service runs, **When** a client reads the OpenAPI document, **Then** the create description says that `dryRun` applies to each body shape and never stores a sequence.

### Edge Cases

- A body with `steps: []` in the old shape: no change. The service creates a sequence with zero steps, because the request declared zero steps.
- A body whose `steps` mixes strings and objects: 400 with `steps[<i>]: each step must be an object.` for the first string item.
- A step object without `stepId`: 400 with `steps[<i>]: each step must include string stepId.`
- A body in the old shape with `"parameters": null`: accepted. Null declares no parameters.
- A body in the old shape with `"parameters": []`: rejected, because the author declared parameters in a shape that cannot keep them. (See FR-005.)
- A body with `blocks` and object steps that have no `stepType` and no `primitiveAction`: no change. The domain shape applies, as before.
- A nested body step (inside a Loop or an If) without `primitiveAction`: no change. The step validation already rejects it with 400.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: On `POST`, `PUT` and `PATCH /api/sequences`, the service MUST read the body as the per-step shape when one or more items of `steps` is a JSON object. For a body with a `blocks` property, this applies only when a step object has `stepType` or `primitiveAction`.
- **FR-002**: The service MUST reject with 400 a top-level per-step step that has no `stepType` (or `stepType: "Action"`) and no `primitiveAction` object. The service MUST NOT map a step that has only `commandReference` to a command step.
- **FR-003**: Each shape error of the per-step reader MUST start with `steps[<index>]`, and with ` (stepId '<id>')` when the step has a string `stepId`, followed by `: ` and the current error text.
- **FR-004**: On `POST /api/sequences`, the old shape MUST apply only when each item of `steps` is a string. The service MUST reject with 400 a body in the old shape that has an item that is not a string, and name the position of the item.
- **FR-005**: On `POST /api/sequences`, the service MUST reject with 400 a body in the old shape that has a `parameters` value that is not `null`.
- **FR-006**: On `POST /api/sequences`, a body with `dryRun: true` MUST NOT store a sequence, for each body shape. A valid body MUST get 200 `{ valid: true, dryRun: true, errors: [] }`. A body that is not valid MUST get the same 400 as the same body without `dryRun`.
- **FR-007**: A valid per-step body MUST keep its current result: the same status, the same stored steps and the same stored parameters.
- **FR-008**: The OpenAPI description of `POST /api/sequences` MUST tell that `dryRun` applies to each body shape and that a malformed step gets 400.
- **FR-009**: `docs/architecture.md`, `CHANGELOG.md` and `specs/STATUS.md` MUST tell the new behavior.

### Key Entities

- **Per-step body**: the create or update body with `steps` as a list of step objects (`SequenceUpsertContract`).
- **Old body**: the create body with `steps` as a list of command id strings.
- **Shape error**: a 400 error from the per-step reader, before the step mapping.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The reproduction body gets 400 in 100% of the tries, and the count of stored sequences does not change.
- **SC-002**: The reproduction body with `dryRun: true` gets 400, and the count of stored sequences does not change.
- **SC-003**: No create request gets 201 with fewer stored steps or parameters than it declared.
- **SC-004**: All current sequence tests pass without a change to their expected results.

## Assumptions

- No current client sends object steps without `stepType` and without `primitiveAction`, because the service dropped such steps. The web UI always sends `stepType`.
- No current client sends `parameters` in the old body.
- The old body stays supported for string command ids, for older clients and tests.
