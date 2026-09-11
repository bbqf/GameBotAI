# Phase 0 Research: Loop Exit Reason & Nested Step-Outcome References

## R-001: Where `ExitReason` is constructed and threaded

**Decision**: Add a `LoopExitReason` record (`BrokeVia: string?`,
`ExhaustedMaxIterations: bool`) alongside `LoopIterations` on `StepResult`, and add
a `brokeVia`/`exhaustedMaxIterations`-carrying overload to
`SequenceExecutionResult.AddLoopStep(...)`. `ExecuteLoopBodyAsync`'s return tuple
gains a fourth element, `string? BrokeVia`, populated with the firing `Break`
step's own `StepId` (not `brkKey`'s derived fallback unless `StepId` truly is
empty) at both firing sites (unconditional and conditional break, ~line 1266 and
~1297 in `SequenceRunner.cs`). `ExecuteIfStepAsync`'s existing `BreakTriggered`
`bool` return also gains this string alongside it, since a `Break` reached through
a nested `If` inside a loop body must still surface its own id, not the `If`
step's id. All three loop-kind executors (`ExecuteCountLoopAsync`,
`ExecuteWhileLoopAsync`, `ExecuteRepeatUntilLoopAsync`) already branch on
break-fired vs. exhausted vs. plain-completion to pick a status string — the same
branches supply `ExitReason` at each of their existing `AddLoopStep` call sites, so
no new control flow is introduced, only additional values captured at points that
already know them.

**Rationale**: `AddLoopStep` is already the single, exclusive construction site for
a `Loop` step's `StepResult` (per exploration, confirmed at `SequenceRunner.cs:2156`
in the current tree) — every one of the 6 call sites across the three loop kinds
independently has the break-fired/exhausted/neither fact in hand at the moment it
calls `AddLoopStep`, so threading one more value through is a narrow, additive
change with no new state machine.

**Alternatives considered**: A separate lookup/derived property computed from
`LoopIterations` after the fact was rejected — `LoopIterResult` only records
`BreakTriggered: bool` and `StepCount`, not the firing step's id, so deriving
`BrokeVia` after construction would still require passing the id through anyway,
just later and less directly.

## R-002: `ExhaustedMaxIterations` semantics independent of `ExitOnMaxIterations`

**Decision**: `ExhaustedMaxIterations` is `true` iff the loop actually ran its
full configured `MaxIterations` without any `Break` firing — computed purely from
iteration-count-reached-without-break, never from the `ExitOnMaxIterations` flag or
the resulting status string. This holds even when `ExitOnMaxIterations: false`
today produces an overall `"Failed"` loop status (a case that is presently
indistinguishable, from the result alone, from a genuine mid-body error).

**Rationale**: Spec FR-004 explicitly requires this independence, because the
consumer's use case (a structural "did we run out of tries" gate) needs the fact
regardless of whether the platform is also configured to fail the loop over it —
conflating the two would silently drop exactly the signal FR-001 exists to expose.

**Alternatives considered**: Deriving `ExhaustedMaxIterations` solely from status
`== "exhausted"` was rejected because that status string is only emitted for the
`ExitOnMaxIterations: true` path (per `SequenceRunner.cs` ~1043/1124); the
`ExitOnMaxIterations: false` path reuses the generic `"Failed"` status (~1047-1050),
which would make the new field blind to exactly the case where the underlying
platform behavior most needs a structural explanation.

## R-003: Whole-tree `stepRef` resolution and "prior" ordering

**Decision**: Replace `ValidateStepCondition`'s sibling-scoped lookup
(`siblings.Select(...).FirstOrDefault(...)`) with a lookup against a flattened,
document-order index of every step reachable from the sequence root — root `steps`
in order, with each `Loop`'s `Body` and each `If`'s `Body`/`ElseBody` expanded
in place at its authored position, recursively. Build this flattened index once per
top-level `Validate(...)` call and pass it down through the existing recursive
`ValidateLoopStep`/`ValidateIfBranch` calls (replacing the `siblings`/
`indexInSiblings` parameters used only for this check) rather than reassembling it
per condition. "Prior" is redefined as "appears earlier in this flattened,
authored-order index" instead of "has a lower index within the immediate sibling
list" — a strict generalization that still rejects same-list forward references
exactly as today.

**Rationale**: This requires no runtime/branch-taken information (validation is
static, authored-structure-only, matching how it already only has the authored
tree available), and it is a direct, minimal generalization of the exact rule
already in place — every currently-valid reference (a sibling with a lower
same-list index) also has a lower flattened index than its referencer, since a
list's own steps are contiguous and ordered within the flattened walk. No existing
acceptance/rejection changes for any request that was already valid or already
correctly rejected for being non-prior within its own list.

**Alternatives considered**: (a) Resolving purely by reachability with no ordering
check was rejected — spec FR-007 explicitly requires "prior" to keep being
enforced, since dropping it would let a condition reference a step that
provably cannot have executed yet by construction (e.g. a later top-level step),
which is a strictly worse authoring experience than today, not merely a widened
one. (b) Deferring the "prior" check to runtime (resolve at validation, only fail
at execution if the dictionary lookup misses) was rejected because it would let a
sequence pass creation validation while carrying a reference that can never
possibly succeed for a purely structural reason (referencing a step that appears
later in every possible execution order) — validation is supposed to catch exactly
this class of authoring mistake fast, per the existing "must reference a prior
step" rule's own stated purpose.

## R-004: Referencing a reachable-but-not-executed step stays a hard failure

**Decision**: No change to `SequenceRunner`'s existing behavior when a
`commandOutcome` condition's `stepRef` resolves against the (now wider) validation
allow-list but the named step did not actually run during a given execution (e.g.
it lives in the untaken branch of an `If`, or inside a loop body that ran zero
iterations): the step and the sequence run continue to fail with today's
`"...commandOutcome reference '<id>' is unavailable"` message (`SequenceRunner.cs`
~544-558). This case was previously unreachable through the API (validation
rejected any cross-scope reference outright); Part B's widened validation makes it
reachable for the first time, but the runtime code path and its failure message
already exist and are unchanged.

**Rationale**: Spec FR-011 requires this explicitly — a "did this step run" gap
should fail loudly (matching the platform's existing convention for an
unresolvable reference), not silently resolve to `skipped`/`false`, since a silent
resolution would reintroduce exactly the kind of unprovable, guessable outcome
FR-001 exists to eliminate.

**Alternatives considered**: Treating a not-yet-executed reachable step as an
implicit `skipped`/false-match was considered (it would let an author build an
"if the other branch ran and it broke" OR-style gate without a hard failure) but
rejected as out of scope — it is a new runtime semantic FR-001 does not ask for
and would change behavior for a case the spec's edge cases section explicitly
pins to today's failure behavior.

## R-005: Making a `Break` step's outcome actually resolvable

**Decision**: `ExecuteLoopBodyAsync` must additionally write
`stepOutcomes[brkKey] = BreakOutcomes.Break` or `BreakOutcomes.NoBreak` at each of
its four `result.AddStep(brkKey, ...)` call sites (unconditional-fired,
conditional-fired, conditional-not-fired, and the eval-error-treated-as-no-break
path) — today only the execution-log entry is written; the runtime dictionary
`commandOutcome` conditions resolve against is never updated for a `Break` step.
Additionally, `SequenceStepValidationService`'s `AllowedCommandOutcomeStates` gains
`"break"` and `"no_break"` (matching `BreakOutcomes.Break`/`BreakOutcomes.NoBreak`'s
existing string constants exactly, so no new vocabulary is invented).

**Rationale**: Without this, Part B's headline use case — asking "did that nested
`Break` fire?" — cannot work at all, at any scope, including the same-body sibling
reference that is already technically legal today: the dictionary key for a
`Break` step's id is simply never set, so any reference to it already fails with
"unavailable" before this feature and would continue to after, defeating the
feature's stated purpose. This is the same expressiveness gap flagged informally
in the consumer's bug tracker (B-006: "no way to query whether a specific nested
`Break` step fired... `break`/`no_break` are not valid `expectedState` values").

**Alternatives considered**: Scoping this feature to only the literal two words in
FR-001's proposed shape (exit reason + relaxed `stepRef`) and leaving `Break`
outcomes unresolvable was rejected — it would ship a feature that cannot deliver
its own stated motivation (a provable "did this loop's `Break` fire" gate),
directly contradicting the "why" section of the request being implemented.

## R-006: No persisted-schema or OpenAPI contract migration needed

**Decision**: No change to `FileSequenceRepository`'s persisted sequence-definition
schema (the new fields are execution-result-only, never part of a stored sequence
document) and no separate result-DTO contract to update — `StepResult` is returned
directly by `/api/sequences/{id}/execute` (per exploration: Service-layer
`SequenceExecutionService.cs` consumes `StepResult` fields directly, no mapping
layer sits between the domain result and the HTTP response), so the new
`ExitReason` property is exposed automatically once added to the domain type.

**Rationale**: Keeps the change confined to `GameBot.Domain`, matching the
"additive, no breaking change" requirement (FR-012) — existing consumers that
don't read the new field see no shape change to anything they already parse.

**Alternatives considered**: Introducing a dedicated response contract/DTO for
`ExitReason` was rejected as unnecessary indirection — the project's existing
convention (per feature 080's precedent) is to extend the domain result type
directly when no separate contract already exists for that shape.
