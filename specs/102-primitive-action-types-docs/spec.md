# Feature Specification: Publish primitive action types and payload shapes

**Feature Branch**: `102-primitive-action-types-docs`  
**Created**: 2026-09-17  
**Status**: Implemented  
**Input**: GitHub issue #201 — https://github.com/bbqf/GameBotAI/issues/201 — Closes #201. "B-017: reschedule-self (and every non-command primitiveAction.type) is missing from the OpenAPI document."

## Background

A sequence step's `primitiveAction` carries a `type` and a free-form `payload`. The published API description
(`/swagger/v1/swagger.json`) describes `PrimitiveAction.type` as an unconstrained string and says nothing about which
values are accepted or what each one's `payload` must contain. The `reschedule-self` action is not mentioned anywhere
in the document outside the queue live-schedule route.

The capabilities exist and are fully wired. They could only be discovered by sending deliberately invalid sequences
with `dryRun: true` and reading the validator's error messages:

| `primitiveAction` sent | Response |
|---|---|
| `type: "bogus"` | `Step 'a' action type 'bogus' is not a supported primitive action type.` (no list of supported types) |
| `type: "reschedule-self", payload: {}` | `reschedule-self action is invalid: option is required` |
| `option: "bogus"` | `option 'bogus' is not a known schedule option (expected one of AtQueueStart, OncePerRun, Timer, EveryStep)` |
| `option: "Timer", offset: "01:00:00"` | `reschedule-self Timer requires a timerTimeOfDay or timerRelativeOffset.` |
| `option: "Timer", timerRelativeOffset: "01:00:00"` | `{"valid":true}` |

Consequence: an author reading the published contract concludes that sequences cannot reschedule themselves. The PNS
authoring repository reached exactly that conclusion twice on 2026-09-16 and drafted a platform feature request for a
capability that already existed.

Verified against the current service (not only the issue's observation): the sequence step validator accepts
`tap`, `swipe`, `key`, `command`, `connect-to-game`, `WaitForImage`, `ensure-game-running`, `go-to-home-screen`,
`ensure-emulator-running`, `reschedule-self` and `notify` — eleven types, matched case-insensitively. The issue named
five; the other six are equally undocumented.

## Clarifications

### Session 2026-09-17

- Q: Should each type's payload be published as a separate structured schema (one variant per type) or described in
  text on the existing schema? → A: Described on the existing `PrimitiveAction` schema — the `type` enum plus a
  per-type payload description (field names, types, required/optional, allowed values and cross-field rules).
  Rationale: `payload` is a free-form object on the wire and the schema is shared with the session-start route;
  restructuring it into variants would change the shape generated clients see, which the issue did not ask for.
- Q: Which set of values is "supported" when the session-start route accepts only `connect-to-game`? → A: The enum is
  the full set the sequence step validator accepts; the session-start route's narrower rule is stated in the schema
  description. Rationale: sequences are where authors hit the gap; one schema cannot carry two enums.
- Q: Is the published enum hand-written or taken from the validator's own list? → A: Taken from the same list the
  validator uses, so adding a type to the validator publishes it automatically; a contract test additionally asserts
  the two are equal and that every published type has a payload description. Rationale: the issue explicitly asks
  that the list cannot drift.
- Q: Casing of the published values? → A: Publish the canonical spellings the service itself uses (`WaitForImage` stays
  PascalCase) and state that matching is case-insensitive. Rationale: no wire change; existing stored sequences keep
  validating.
- Q: How is the supported list rendered in the unsupported-type error? → A: Mirror the reschedule-self option error:
  `Step '<id>' action type '<type>' is not a supported primitive action type (expected one of <values, comma-separated,
  canonical spelling, published-enum order>).` Rationale: one consistent "expected one of" idiom across validator
  messages; the text up to "primitive action type" is unchanged, so prefix matches keep working.
- Q: For a `command` step, is `commandReference` an alternative to `payload.commandId`? → A: No. `payload.commandId` is
  required (the validator rejects its absence); the step-level `commandReference` is optional companion metadata, not a
  substitute. Rationale: verified in the validator and the step mapping.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Discover every supported action type from the API description (Priority: P1)

An automation author reads the published API description for a sequence step's `primitiveAction` and sees the
complete list of accepted `type` values, including `reschedule-self`, without probing the validator.

**Why this priority**: this is the gap that caused an existing capability to be judged missing.

**Independent Test**: Fetch the published OpenAPI document and read the `PrimitiveAction.type` schema.

**Acceptance Scenarios**:

1. **Given** the published OpenAPI document, **When** a reader inspects `PrimitiveAction.type`, **Then** it lists
   exactly the eleven values the sequence step validator accepts, including `reschedule-self`, `notify`, `tap`,
   `swipe`, `key` and `ensure-game-running`.
2. **Given** the published OpenAPI document, **When** a reader inspects `PrimitiveAction.type`, **Then** its
   description states that values are matched case-insensitively and that the session-start route accepts only
   `connect-to-game`.
3. **Given** a future action type added to the validator, **When** the document is regenerated, **Then** the new value
   appears in the enum without a separate documentation edit.

---

### User Story 2 - Know what payload each action type needs (Priority: P1)

The same author, having picked a type, learns from the API description which `payload` fields that type reads, which
are required, their value formats and cross-field rules — for example that `reschedule-self` needs `option` (one of
`AtQueueStart`, `OncePerRun`, `Timer`, `EveryStep`) and, for `Timer`, exactly one of `timerTimeOfDay` or
`timerRelativeOffset` (or an `ocrOffset` spec).

**Why this priority**: an enum without payload shapes still forces validator probing for every field.

**Independent Test**: Fetch the OpenAPI document and read the `PrimitiveAction.payload` description for each type.

**Acceptance Scenarios**:

1. **Given** the published OpenAPI document, **When** a reader inspects `PrimitiveAction.payload`, **Then** it contains
   a payload description for every value in the `type` enum.
2. **Given** the `reschedule-self` payload description, **When** read, **Then** it names `option` and its four values,
   `timerTimeOfDay`, `timerRelativeOffset` (with its 00:00:00–24:00:00 range), `ocrOffset`, that Timer requires exactly
   one timing source, that timer fields are only valid with `Timer`, and that the action is a no-op success outside a
   queue run.
3. **Given** the payload descriptions for `tap`, `swipe` and `key`, **When** read, **Then** they name the coordinate /
   key fields each needs (`x`,`y`; `x1`,`y1`,`x2`,`y2` with optional `durationMs`; `key` or `keyCode`).
4. **Given** the published OpenAPI document, **When** a reader looks at the sequence create/update examples or the
   `PrimitiveAction` example, **Then** at least one valid `reschedule-self` step is shown.

---

### User Story 3 - Get the supported list from the rejection message (Priority: P2)

An author (or tool) that sends an unsupported action type receives a 400 whose message lists the supported values,
the same way the `reschedule-self` option error already lists its expected values.

**Why this priority**: the issue's stated minimum; it helps callers who never read the API description.

**Independent Test**: `POST /api/sequences` with `dryRun: true` and `primitiveAction.type: "bogus"`.

**Acceptance Scenarios**:

1. **Given** a sequence step with `primitiveAction.type: "bogus"`, **When** it is validated, **Then** the error still
   begins `Step '<id>' action type 'bogus' is not a supported primitive action type` and continues
   ` (expected one of ...)` listing every supported value.
2. **Given** a step whose type is missing or blank, **When** validated, **Then** the same message with the supported
   list is returned.

### Edge Cases

- `WaitForImage` is the one PascalCase value; case-insensitive matching must be stated so `waitforimage` is not
  assumed invalid.
- `command` steps: the payload description must say `payload.commandId` is required, and that the step-level
  `commandReference` does not replace it.
- The session-start route shares the `PrimitiveAction` schema but accepts only `connect-to-game`; the enum must not be
  read as a promise that the session route accepts all eleven.
- Publishing the enum must not cause any currently-valid request (any casing the service accepts) to be rejected by
  the service; the service's own acceptance rules do not change.
- The richer error message must not change the message's leading text, so callers matching on it keep working.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The published `PrimitiveAction.type` schema MUST enumerate every action type the sequence step validator
  accepts — no more, no fewer.
- **FR-002**: The enum MUST be produced from the same source list the validator uses, not a separately maintained copy.
- **FR-003**: The `PrimitiveAction.type` description MUST state that matching is case-insensitive and that the
  session-start route accepts only `connect-to-game`.
- **FR-004**: The published `PrimitiveAction.payload` description MUST describe, for every enumerated type, the payload
  fields that type reads: name, value type/format, required or optional, allowed values, and cross-field rules the
  validator or dispatcher enforces. A type that reads no payload MUST say so.
- **FR-005**: The `reschedule-self` payload description MUST cover `option` (AtQueueStart, OncePerRun, Timer,
  EveryStep), `timerTimeOfDay`, `timerRelativeOffset` (00:00:00–24:00:00), `ocrOffset` (region, min, max, fallback),
  the "exactly one timing source for Timer" rule, the "timer fields only with Timer" rule, and that it only has effect
  when the sequence runs from a queue.
- **FR-006**: The published document MUST include at least one example of a valid `reschedule-self` sequence step.
- **FR-007**: The validator error for an unsupported or missing action type MUST keep its existing leading text (up to
  "is not a supported primitive action type") and append ` (expected one of <values>).` with every supported value in
  canonical spelling, in the same order as the published enum.
- **FR-008**: No action type's runtime behaviour, validation acceptance, or payload semantics may change; no request
  the service accepts today may be rejected afterwards.
- **FR-009**: An automated contract test MUST fail if the published enum differs from the validator's accepted set, if
  any enumerated type lacks a payload description, or if the FR-005 statements are missing.
- **FR-010**: An automated test MUST fail if the unsupported-type error stops listing the supported values.

### Key Entities

- **PrimitiveAction**: a step's action — `type` (one of the supported action types), `schemaVersion` (optional),
  `payload` (type-specific fields). Shared by sequence steps and the session-start request.
- **Supported action type set**: the eleven values the sequence step validator accepts; the single source of the
  published enum and of the error message's list.
- **reschedule-self payload**: `option`, `timerTimeOfDay`, `timerRelativeOffset`, `ocrOffset`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 11 of 11 supported action types appear in the published API description, each with a payload
  description (today: 0 of 11).
- **SC-002**: An author can write a valid `reschedule-self` Timer step using only the published API description, with
  zero validator probes.
- **SC-003**: The unsupported-type rejection lists 100% of the supported values.
- **SC-004**: Zero existing sequence, session and validation contract tests change expectations other than the
  appended supported-type list.
- **SC-005**: Adding a type to the validator's list without documenting its payload causes at least one automated
  test to fail.

## Assumptions

- The OpenAPI document served by the service is the authoritative contract callers read.
- `primitiveAction.payload` fields are copied into the stored step's action parameters unchanged, so the dispatcher's
  field names are the payload field names.
- Documentation is added through schema/document filters, as in features 099–101; the service does not feed XML
  comments to the OpenAPI generator.

## Non-Goals

- Changing any action type's runtime behaviour, validation rules (beyond enriching the unsupported-type message), or
  payload semantics.
- Adding, renaming or removing action types.
- Restructuring `PrimitiveAction` into per-type polymorphic schemas.
- Web UI changes.
- Documenting command-step (`/api/commands`) step types, which use a separate schema.
