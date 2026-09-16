# Feature Specification: Honour dryRun on sequence updates and reject unresolvable command references

**Feature Branch**: `091-fix-sequence-put-dryrun`  
**Created**: 2026-09-16  
**Status**: Implemented  
**Input**: GitHub issue #177 (https://github.com/bbqf/GameBotAI/issues/177) — "B-008: dryRun is silently ignored on PUT /api/sequences/{id}, and the update path persists an unresolvable commandId". Closes #177.

## Background

Two distinct defects were found on the same call.

1. **dryRun is silently ignored on sequence update.** `dryRun: true` on sequence *create* is validate-only: nothing is persisted, structural errors surface exactly as for a real create, and a valid body returns `{ valid: true, dryRun: true, errors: [] }`. On sequence *update* (`PUT /api/sequences/{id}`) the same flag has no effect: the update is applied for real (version bumped, content changed) and an ordinary update response comes back.
2. **Unresolvable command references are persisted.** An update accepts and stores a syntactically plausible but nonexistent `commandId` in a command step; on read-back it shows `isResolved: false` instead of having been rejected at write time. On create, dry-run validation does not check command existence either — a nonexistent `commandId` reports `valid: true`.

Reproduction from the issue: create a sequence (version 1); `PUT` it with `dryRun: true` and a bogus `commandId`; `GET` it — it is now version 2, its content changed, and the bogus reference is stored with `isResolved: false`.

## Clarifications

### Session 2026-09-16

- Q: Support `dryRun` on update, or reject it with `400`? → A: Support it with create's validate-only semantics. Rationale: the issue author's preference and symmetry with create; callers already send the flag.
- Q: Does the fix also cover `PATCH /api/sequences/{id}`? → A: Yes, both `dryRun` and the command-existence check. Rationale: PATCH shares the update path and silently ignores `dryRun` the same way.
- Q: Should a dry run that fails return `{ valid: false, errors }` with `200`, or the real write's failure response? → A: The real write's failure response (same status, same `{ message, errors }` body). Rationale: that is exactly what create's dry run already does, and FR-002's "same answer as a real write" is only testable that way.
- Q: How are unresolved references that already exist in a stored sequence treated on update? → A: Tolerated when the stored sequence already referenced that `commandId` (anywhere in it); rejected when new. Rationale: keeps the deliberately supported deleted-command re-save flow working.
- Q: Is the existence check applied to legacy bodies whose steps are plain command-id strings? → A: No. Rationale: the issue concerns per-step `commandReference` bodies; legacy-shape validation is out of scope.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Validate an update without changing the live sequence (Priority: P1)

An automation author who has learned that `dryRun` makes create safe sends the same flag on an update to check a revised sequence before committing to it. The live sequence must stay exactly as it was, and the author gets the same validation answer create's dry run would give.

**Why this priority**: Today a dry-run update silently mutates a live sequence that queues are actively running. This is the worst outcome — the caller explicitly asked for no change.

**Independent Test**: Create a sequence, send an update with `dryRun: true`, then read the sequence back and confirm version and content are unchanged, and that the response was the validation envelope.

**Acceptance Scenarios**:

1. **Given** a stored sequence at version 1, **When** a valid update body is sent with `dryRun: true`, **Then** the response is `200` with `{ valid: true, dryRun: true, errors: [] }`, and a subsequent read returns version 1 with the original content.
2. **Given** a stored sequence, **When** an invalid update body (for example a nested loop) is sent with `dryRun: true`, **Then** the response is the same `400` with the same `errors` a real update of that body returns, and the stored sequence is unchanged.
3. **Given** a stored sequence at version 2, **When** a dry-run update carries `version: 1`, **Then** the response is the same `409` conflict a real update returns, and nothing is changed.
4. **Given** a stored sequence, **When** an update is sent without `dryRun` (or with `dryRun: false`), **Then** it is applied exactly as today.
5. **Given** a stored sequence, **When** a partial update (`PATCH`) is sent with `dryRun: true`, **Then** it behaves as scenario 1/2 — validated, never applied.

---

### User Story 2 - A mistyped command reference is caught at write time (Priority: P1)

An author hand-types (or scripts) a `commandId` into a command step. If that command does not exist, the write is refused with a clear validation error naming the step and the missing command, instead of the sequence being saved broken and failing later at execution time.

**Why this priority**: A typo'd reference that is silently stored surfaces only as a confusing run-time failure inside an unattended queue. Callers currently have to resolve every command id themselves before each write.

**Independent Test**: Send a create and an update containing a command step whose `commandId` matches no command; confirm a `400` naming the step and command, and confirm nothing was stored or changed. Repeat with `dryRun: true` and confirm the same errors.

**Acceptance Scenarios**:

1. **Given** no command with id `does-not-exist`, **When** a sequence is created with a command step referencing `does-not-exist`, **Then** the response is `400` with an error naming the step and `does-not-exist`, and no sequence is stored.
2. **Given** the same body with `dryRun: true`, **When** it is sent to create, **Then** the response is the same `400` with the same errors (not `valid: true`).
3. **Given** a stored sequence at version 1, **When** it is updated (PUT or PATCH) with a command step referencing a nonexistent command, **Then** the response is `400` naming the step and command, and the stored sequence remains version 1 with its original content.
4. **Given** the same update with `dryRun: true`, **Then** the response is the same `400` with the same errors, and nothing changes.
5. **Given** a nonexistent command referenced inside a loop body or an if/else branch, **When** the sequence is created or updated, **Then** it is rejected the same way as a top-level step.
6. **Given** a command step referencing a command that exists, **When** the sequence is created or updated, **Then** it is accepted exactly as today.

---

### User Story 3 - Re-saving a sequence whose command was later deleted still works (Priority: P2)

A sequence was saved while its command existed; the command was later deleted. The editor shows the step as unresolved and the author re-saves the sequence (for example after editing an unrelated step). That save must still succeed, keeping the unresolved step and its last-known command name, as it does today.

**Why this priority**: This is existing, deliberately supported behaviour (unresolved-command display and round-trip). The new write-time check must not turn every sequence touching a deleted command into an un-editable one.

**Independent Test**: Create a sequence referencing an existing command, delete the command, then send an update that carries the same unresolved reference; confirm it is accepted and the reference remains `isResolved: false` with its snapshot name.

**Acceptance Scenarios**:

1. **Given** a stored sequence whose command step references a command that has since been deleted, **When** it is updated carrying the same `commandId`, **Then** the update is accepted and the step still reads back as `isResolved: false` with the last-known command name.
2. **Given** that same stored sequence, **When** an update introduces a *different* nonexistent `commandId`, **Then** that new reference is rejected (User Story 2).

---

### Edge Cases

- A dry-run update for a sequence id that does not exist returns `404`, as a real update does.
- A dry-run update whose body is in the legacy shape (steps as a list of command-id strings, or name-only) is still never applied; the response is the validation envelope.
- The same nonexistent `commandId` used by several steps produces one error listing every step that uses it, so errors stay readable.
- `commandId` lookups are case-insensitive, matching how command references are already resolved for display.
- A command step with no `commandId` at all keeps its existing, separate "requires a non-empty commandId" error; the new check does not duplicate it.
- An unresolved reference that is carried over on update is tolerated only when the stored sequence already referenced that same `commandId`; a nonexistent id new to the sequence is always rejected.
- Only the JSON literal `true` requests a dry run. A non-boolean `dryRun` on a per-step body is rejected as a malformed payload (`400`), exactly as create already rejects it; on a legacy-shape body it is ignored, and the write is applied as a real update.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A sequence update (full replace and partial update) with `dryRun: true` MUST NOT persist anything: the sequence reads back with the same version and the same content (nothing is written, so its stored timestamps are untouched too).
- **FR-002**: A sequence update with `dryRun: true` MUST run every check a real update of the same body runs and return the same failure response (status and body) a real update would return when any check fails — including `404` for an unknown sequence, `409` for a version conflict and `400` for validation errors.
- **FR-003**: A sequence update with `dryRun: true` whose body passes every check MUST return `200` with `{ valid: true, dryRun: true, errors: [] }`, the same envelope create's dry run returns.
- **FR-004**: A sequence update without `dryRun`, or with `dryRun: false`, MUST behave exactly as today apart from FR-005/FR-006.
- **FR-005**: Sequence create and update of a per-step body (steps as objects) MUST reject, with `400` and the standard `{ message, errors }` validation body, any command step whose `commandId` matches no existing command, wherever the step sits (top level, loop body, if branch, else branch). Each error MUST name the missing `commandId` and the step(s) using it.
- **FR-006**: On update, a command step whose unresolvable `commandId` was already referenced by the stored sequence MUST NOT be rejected by FR-005, so a sequence whose command was deleted after it was saved can still be re-saved.
- **FR-007**: The existence check of FR-005 MUST apply identically with and without `dryRun`, so dry-run validation reports a nonexistent `commandId` as invalid on both create and update.
- **FR-008**: A rejected write (FR-005) MUST leave storage untouched: no sequence created on create, no change on update.
- **FR-009**: The published API description for sequence update MUST document the `dryRun` flag, its validate-only semantics and its success envelope, and document that nonexistent command references are rejected on create and update.
- **FR-010**: Automated contract/integration tests MUST cover FR-001 through FR-008.

### Key Entities

- **Sequence**: a stored, versioned list of steps; the thing a dry run must never change.
- **Command step reference**: a step's pointer to a command by `commandId`, with a last-known command name snapshot and a resolved/unresolved state on read.
- **Validation envelope**: the dry-run success body `{ valid, dryRun, errors }`; failures use the standard `{ message, errors }` body with the same status a real write returns.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In 100% of dry-run updates, the sequence's version and content read back identical to before the call.
- **SC-002**: For every body in the tested set, a dry-run update and a real update of the same body return the same failure status and the same error list.
- **SC-003**: 0 sequences can be created or newly edited to reference a command that does not exist; each such attempt is refused with an error naming the missing command.
- **SC-004**: A sequence whose command was deleted after it was saved can still be re-saved without modification, in 100% of cases covered by the existing unresolved-command scenario.

## Assumptions

- PATCH shares the update code path and carries the same silent `dryRun` defect, so it is fixed alongside PUT; leaving it would reproduce the exact hazard the issue describes on a sibling route.
- The issue offered two options for `dryRun` on update (support it, or reject it with `400`); supporting it with create's semantics was chosen for symmetry, as the issue author preferred.
- "Already referenced by the stored sequence" (FR-006) is judged by `commandId` anywhere in the stored sequence, not by step id, so reordering or relabelling steps during an edit does not break re-saving a sequence with a deleted command.
- Existing tests that create sequences referencing commands which were never created are test fixtures relying on the defect; they are updated to create those commands first (or, where the test is about a dangling reference, to create then delete the command).

## Non-Goals

- No change to execution-time handling of unresolved references in sequences that are already stored.
- No change to how legacy (non per-step) sequence bodies are validated, beyond honouring `dryRun`.
- No other validation changes (image references, conditions, loops, parameters).
- No web UI changes.
