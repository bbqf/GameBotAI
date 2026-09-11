# Phase 1 Data Model: Fix Sequence & Session-Input API Bugs

No persisted schema changes. This feature adds one small in-memory result shape
and tightens validation on two existing entities; nothing is stored to disk in a
new shape.

## SequenceStep (existing entity — validation rule added, no field changes)

Represented by `GameBot.Domain.Commands.SequenceStep` (domain) and
`GameBot.Service.Models.SequenceStepDto` family (HTTP contract). No new fields.

**New validation rule** (enforced in `SequenceStepValidationService` and mirrored
in `FileSequenceRepository.ValidateActionPayloads`):

- If `StepType == Action` and `Action.Type == ActionTypes.Command`, then
  `Action.Parameters` MUST contain a key `commandId` whose value is a non-null,
  non-whitespace string. This rule applies identically whether the step is
  top-level or nested inside a `Loop`/`If` body (i.e. at any depth reachable via
  `Body`/`ElseBody`).
- Violation produces one validation error entry identifying the step (`StepId`,
  and its path — top-level index, or `Loop`/`If` body position — if nested),
  consistent with how existing per-step validation errors are already reported by
  `SequenceStepValidationService.Validate`.

**Existing field this rule depends on** (unchanged): `SequenceStep.RequireDispatch`
(bool) — already present on the domain model
(`GameBot.Domain/Commands/SequenceStep.cs:69`); this feature only fixes *mapping*
into that field for nested steps (see below), not the field itself.

**Mapping fix** (no data-model change, a code-path fix):
`SequencesEndpoints.MapBodySteps` must copy the request DTO's `RequireDispatch`
value onto the mapped `SequenceStep.RequireDispatch`, exactly as
`MapToLinearSteps` already does for top-level steps.

## SessionInputAction (existing entity — outcome reporting added)

Represented by `GameBot.Emulator.Session.InputAction` (input) and, newly, a result
type replacing the current bare `int` returned by
`ISessionManager.SendInputsAsync`.

**New type**: `SessionInputDispatchResult` (name illustrative; exact name decided
at implementation time). `SendInputsAsync`'s existing `Task<int>` signature and
seven in-tree call sites (`CommandExecutor`, `QueueExecutionService`,
`SequenceExecutionService`) are unaffected — those callers only ever need the
accepted count, which they already get. Instead, `SessionManager` gains a second,
internal-detail-returning method (e.g. `SendInputsWithResultsAsync`) that shares
the same underlying per-action dispatch loop and is used only by the
`POST /api/sessions/{id}/inputs` endpoint, which is the sole caller that needs
per-action failure detail:

| Field | Type | Meaning |
|---|---|---|
| `SessionFound` | `bool` | Whether the session id resolved to a tracked session at all. |
| `Results` | `IReadOnlyList<InputActionResult>` | One entry per posted action, in request order. |

**New type**: `InputActionResult`:

| Field | Type | Meaning |
|---|---|---|
| `Index` | `int` | Position of this action in the request's `Actions` array. |
| `Dispatched` | `bool` | Whether this action reached the device/emulator layer. |
| `FailureReason` | `string?` | Present only when `Dispatched == false`; a short, stable reason code/message (e.g. missing/invalid argument name, unsupported type) — never a raw exception message or stack trace. |

**Derived response selection** (`SessionsEndpoints`, `POST {id}/inputs`), using the
session's real `Status` plus `SessionInputDispatchResult`:

- Session not found or not `Running` → `409` (unchanged from today).
- Session `Running`, zero of `Results` have `Dispatched == true` → `400`.
- Session `Running`, one or more `Results` have `Dispatched == true` → `202`
  (existing shape), with `Results` (or the failing subset) included in the body
  alongside the existing `accepted` count.

## Out of scope for this feature

- No changes to how sequences, commands, or sessions are persisted.
- No new top-level API entities or routes.
