# Phase 0 Research: Fix Sequence & Session-Input API Bugs

No `NEEDS CLARIFICATION` markers remain in the Technical Context — this is a
bug-fix feature confined to an existing, well-understood codebase, so research is
scoped to the design decisions needed to fix each bug correctly and consistently
with existing conventions, rather than technology selection.

## Decision: B-003 — reject unresolved command references, do not auto-resolve names

**Decision**: When a `Command`-typed action step's payload lacks a non-empty
`commandId`, the platform rejects the sequence at validation time with a
`400 Bad Request`. The platform does **not** attempt to resolve a `commandName`
value to an id server-side.

**Rationale**: The platform's own prior documentation (`docs/findings.md` §2.4,
referenced by the PNS tracker itself) already establishes that the service
round-trips `commandId`, not `commandName`, by design — `commandName` was never a
supported request field. The consuming project's own documented workaround is to
resolve names to ids client-side before posting (exactly what its `apply.ps1`
already does for every other sequence). Auto-resolving names server-side would
introduce a second, undocumented way to reference commands, diverging from that
established contract instead of enforcing it. Failing fast with a clear error is
strictly better than either the current silent fallback or a new implicit
resolution path.

**Alternatives considered**:
- *Auto-resolve `commandName` server-side via `ICommandRepository` lookup*: rejected
  — expands the API's accepted surface instead of tightening it, contradicts the
  documented single-supported-field contract, and hides ambiguity (duplicate
  command names, case sensitivity) behind implicit behavior.
- *Leave the fallback-to-stepId behavior but make it error at run time with a
  clearer message*: rejected — spec requires failing at creation time (FR-001,
  FR-003), since the entire point is to stop paying for this mistake at run time
  minutes/days later.

## Decision: B-005 — fix the mapping omission, not the execution engine

**Decision**: The fix is a one-line addition in
`SequencesEndpoints.MapBodySteps` (copy `RequireDispatch` from the request DTO
onto the mapped domain step), not a change to `SequenceRunner`.

**Rationale**: Direct code inspection confirms `SequenceRunner` already evaluates
`originalStep.RequireDispatch` uniformly regardless of nesting
(`SequenceRunner.cs:719`); the flag is simply never populated for nested steps
because `MapBodySteps` — the single shared helper used for both `Loop` bodies and
`If` branches — omits it from the mapped `SequenceStep` object initializer, while
the top-level mapper (`MapToLinearSteps`) does set it. Fixing the mapping is the
minimal, correct change; touching the runner would be treating a symptom that
doesn't exist there.

**Alternatives considered**:
- *Add a runtime warning/log when a nested step has `requireDispatch` unset*:
  rejected — doesn't fix the actual defect (the flag silently becoming `false`
  when the author explicitly set it `true`), and the spec requires the guarantee
  to hold (FR-005/FR-006), not merely be logged.

## Decision: B-001 — status-aware, per-action response instead of a count-only verdict

**Decision**: `SessionManager.SendInputsAsync` changes its return contract from a
bare `int` accepted-count to a structure carrying, per posted action, whether it
dispatched and (if not) why. `SessionsEndpoints`'s `POST {id}/inputs` handler then
chooses its response using both the session's actual `Status` and these per-action
results: `409` only when the session itself isn't running; `400` when the session
is running but zero actions could be parsed/dispatched; `202` (extended with the
per-action results) when some or all actions dispatched successfully. This mirrors
the two-tier contract recorded in the spec's Clarifications section.

**Rationale**: The current `accepted == 0 → 409` shortcut conflates two unrelated
failure modes (dead session vs. malformed request body) into one misleading error,
which is the entire bug. Session status is already tracked by `ISessionManager`
(used elsewhere for exactly this kind of check) and is the correct, direct signal
for the `409` case — deriving session health from a side effect of action dispatch
was the original design mistake. Reusing the existing `202 Accepted` shape for the
partial-success case (rather than inventing a new status code) keeps the contract
stable for callers who only care about the accepted count; they only see new
fields, not a new/removed status code, when they already have partial success.

**Alternatives considered**:
- *Change `202` to `207 Multi-Status`*: rejected — `207` is a WebDAV convention
  rarely used elsewhere in this API's surface (per `docs/architecture.md`'s
  documented API conventions); introducing it for one endpoint would itself be a
  UX-consistency regression (Constitution Principle III) rather than a fix.
  Extending the existing `202` body is a smaller, more consistent change.
- *Always return `422 Unprocessable Entity` for any dispatch failure regardless of
  partial success*: rejected — would regress the many callers who already treat a
  non-zero `accepted` count as informational success; spec FR-011a explicitly
  requires keeping `202` when some actions dispatch.

## Decision: consistent error-body shape

**Decision**: The new `400` responses in both the sequence-validation path (B-003)
and the session-input path (B-001) follow this codebase's two already-established
error shapes rather than introducing a third: `Results.BadRequest(new { message,
errors })` (the shape used throughout `SequencesEndpoints.cs` for step validation
failures) for B-003, and the `{ error: { code, message, hint } }` shape (already
used for the `404`/`409` cases in `SessionsEndpoints.cs`) for B-001's `400`, with
`code = "invalid_input_actions"`.

**Rationale**: Constitution Principle III requires consistent, predictable
interfaces. Both shapes already exist and are already used adjacently in the same
files being touched; picking whichever is already conventional in each file avoids
introducing a third error envelope shape into the API surface.

**Alternatives considered**: A single unified error envelope across the whole API
— rejected as out of scope; that would be a much larger, unrelated API-wide
migration, not a targeted bug fix.
