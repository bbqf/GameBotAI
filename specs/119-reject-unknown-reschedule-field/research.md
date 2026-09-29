# Research: Reject an unknown field in a reschedule-self payload

No item in the Technical Context needed clarification. The decisions follow.

## Decision 1: Put the check in the step validation

- **Decision**: Add the check to `SequenceStepValidationService.ValidateRescheduleSelfPayload`.
- **Rationale**: Create, update, and dry run all call this method. The load path and the run-time reader do not call it, so a stored sequence loads and runs as before (FR-009).
- **Alternatives considered**: Put the check in `SelfReschedulePayload.TryRead`. Rejected: the runner also uses this reader, and a stored sequence with an unknown field would fail at run time.

## Decision 2: Run the check after `TryRead` succeeds

- **Decision**: Keep the `TryRead` call and its early return first. Add the unknown-field check after it.
- **Rationale**: A wrong `option` keeps its exact message (FR-006).
- **Alternatives considered**: Check first. Rejected: a payload with a wrong option and an unknown field would then show a different first error than today.

## Decision 3: Match keys without regard to letter case

- **Decision**: Use `StringComparer.OrdinalIgnoreCase` with a set of the four key constants from `SelfReschedulePayload`.
- **Rationale**: The reader finds keys without regard to letter case, so `TimerTimeOfDay` is valid today (FR-005). The constants keep one source for the names.
- **Alternatives considered**: A new copy of the key strings. Rejected: the copy can drift from the reader.

## Decision 4: Top-level keys only, one error for all unknown keys

- **Decision**: Check only the top-level keys. Report all unknown keys in one error with the list of known keys.
- **Rationale**: This follows the spec clarifications. The author fixes the payload in one round. A nested check has a higher risk to reject a payload that is valid today.
- **Alternatives considered**: Check nested `ocrOffset` keys. Rejected: out of scope.

## Decision 5: No change in other places

- **Decision**: Do not change other action types, queue template validators, or the OpenAPI action type lists.
- **Rationale**: No new action type or field is added (FR-008, FR-010). The site list for a new action type does not apply.
