# Phase 0 Research: Sequence & Command Parameters

**Feature**: 078-sequence-parameters
**Date**: 2026-08-24

All four behaviour decisions were settled with the operator before the spec was written, plus one
during clarification. This document records the *technical* decisions that follow from them, each
grounded in the code that exists today.

---

## R1. Reuse the existing `{{key}}` substitution instead of introducing a second syntax

**Decision**: Extend `GameBot.Domain/Utils/TemplateSubstitutor.cs` rather than build a new resolver.

**Rationale**: The mechanism the feature needs already exists and already ships in production
sequences. `TemplateSubstitutor` implements a compiled `\{\{(\w+)\}\}` regex over strings and over
`SequenceActionPayload.Parameters`, and `SequenceRunner.ApplyIterContext` (SequenceRunner.cs:1300)
already threads a substitution context through loop bodies and if branches for `{{iteration}}`. A
second syntax would mean two resolvers, two validators, and an authoring surface where operators
must remember which braces mean which thing.

**Changes required**:
- Widen the placeholder pattern from `\w+` to `\w+(?:\.\w+)*` so the reserved `queue.*` namespace
  parses. The widened pattern is a strict superset, so every existing `{{iteration}}` keeps matching.
- Add a strict resolution entry point that reports unresolved keys instead of leaving them in place.
  The existing lenient `Substitute` stays, because the loop path relies on leave-as-is semantics for
  nested contexts.

**Alternatives considered**:
- `${name}` / `%name%` syntax — rejected, splits the mental model for no gain.
- A full expression language — explicitly out of scope per the spec.

---

## R2. Scope composition: a layered, immutable `ParameterScope`

**Decision**: Model the run scope as an immutable linked chain of layers, resolved innermost-first:

```
loop iteration layer   (ephemeral, per iteration — {{iteration}})
  └ command binding layer   (sequence step → command bindings + command defaults)
      └ sequence binding layer  (template entry values + ad-hoc values + sequence defaults)
          └ queue built-in layer   (queue.emulatorSerial, queue.instanceName, ...)
```

`TryResolve(name, out value)` walks outward. `Child(bindings, declarations)` returns a new scope
without mutating the parent, which matters because `QueueExecutionService` runs firings on a shared
run loop and `QueueRunHandle` state is read concurrently by the monitor thread.

**Rationale**: This produces the agreed precedence (FR-009) as a natural consequence of the walk
order, and ambient fall-through (FR-014) is simply "the name was not in this layer, keep walking".
No re-mapping code is needed at intermediate levels. Immutability keeps the existing threading model
intact.

**Alternatives considered**:
- A single flattened `Dictionary` rebuilt per step — rejected: loses the ability to report *which*
  scope supplied a value, which FR-028 requires for the UI preview.
- Mutable ambient scope with push/pop — rejected: fragile under the queue's concurrent monitor reads.

---

## R3. Numeric fields: inline placeholders where the type already allows it, a field-template overlay where it does not

This is the one place the existing type system fights the requirement, so it needs a deliberate
decision.

**Two different situations exist**:

1. **Sequence action payloads** — `SequenceActionPayload.Parameters` is
   `Dictionary<string, object?>`, and every consumer already parses strings defensively
   (`EnsureEmulatorRunningArgs.GetInt` handles `case string s when int.TryParse(...)`,
   SequenceExecutionService `TryGetInt` likewise). A `{{param}}` string in a numeric payload
   parameter therefore needs **no schema change at all** — substitution produces a numeric string
   and the existing parsers accept it.

2. **Command step configs** — these are strongly typed (`SwipeConfig.StartX` is `int`,
   `EnsureEmulatorRunningConfig.InstanceIndex` is `int?`, `EnsureGameRunningConfig.ReadinessTimeoutMs`
   is `int`). A placeholder cannot live in an `int`.

**Decision**: For (2), add a single optional `FieldTemplates` map to `CommandStep`, keyed by dotted
field path, e.g. `{"swipe.startX": "{{startX}}", "ensureEmulatorRunning.instanceIndex": "{{slot}}"}`.
At resolve time the step is cloned with the substituted, parsed values written into the real typed
fields. **String-typed** command-step fields (`EnsureEmulatorRunning.AdbSerial`,
`EnsureEmulatorRunning.InstanceName`, `KeyInput.Key`, `DetectionTarget.ReferenceImageId`,
`CommandStep.TargetId`) hold their placeholder inline and need no overlay entry.

**Rationale**: The motivating case — `adbSerial` — is a string, so it takes the zero-ceremony inline
path. The overlay is confined to a short, enumerable list of numeric fields, adds exactly one nullable
property to one class, and is invisible in JSON for every command saved before this feature (FR-032).

**Alternatives considered**:
- Change every numeric field to a `ParamInt` union type that serializes as number-or-string —
  rejected: touches ~12 DTOs plus their domain twins, every construction site, and the API contract
  snapshots, for a case that is far rarer than the string case.
- Add a parallel `StartXTemplate` string beside every numeric field — rejected: doubles the surface
  of every config class and scales badly as more step types arrive.
- Make placeholders legal only in string fields — rejected: contradicts the agreed decision 4.

---

## R4. Relaxing the current "placeholders are loop-only" validation rule

**Finding**: `SequenceStepValidationService.cs:243-249` currently **rejects any** `{{...}}` in a
top-level step's action parameters: *"contains template placeholder(s) in action parameters which are
only valid inside a loop body."* Unchanged, this rule would block the entire feature.

**Decision**: Narrow the rule so it rejects only the reserved **`iteration`** name outside a loop.
Any other name is checked against the parameter-reference rules (FR-020) instead.

**Rationale**: `{{iteration}}` genuinely is meaningless outside a loop and must stay rejected;
parameter names are meaningful everywhere. Narrowing rather than deleting preserves the original
intent. Both existing tests that cover this rule
(`LoopValidationTests.TopLevelStepWithTemplatePlaceholderIsRejected`,
`IfValidationTests.TemplatePlaceholderInTopLevelIfBranchIsRejected`) assert on `{{iteration}}`
specifically, so **both keep passing unmodified** — a useful signal that the narrowing is faithful.

---

## R5. Threading the scope through execution

**Finding**: `SequenceRunner.ExecuteAsync` takes `Func<string, Task> executeCommandAsync` — a
commandId-only delegate (SequenceRunner.cs:66). `SequenceExecutionService` supplies the lambda that
calls `_commandExecutor.ForceExecuteAsync(sessionId, commandId, childContext, ct)`
(SequenceExecutionService.cs:111). Command-level bindings have nowhere to travel today.

**Decision**: Widen the delegate to `Func<string, ParameterScope, Task>` and add an optional
`ParameterScope` parameter to `SequenceRunner.ExecuteAsync`,
`ISequenceExecutionService.ExecuteAsync`, and the `ICommandExecutor` force-execute overloads.
Defaults are `ParameterScope.Empty`, so every existing call site compiles and behaves identically.

**Also required**: `ApplyIterContext` is called only for **loop-body and if-branch** steps
(SequenceRunner.cs:1226). Top-level steps are dispatched raw. Rename it to `ApplyScope` and call it
on the top-level path too (SequenceRunner.cs:93), passing the run scope with an empty iteration
layer. Without this, a parameter in a non-looping step would never be substituted.

**Alternatives considered**:
- An `AsyncLocal<ParameterScope>` ambient — rejected: invisible data flow, and the queue run loop's
  watchdog cancellation and monitor threads make implicit context risky.
- Resolve the whole command tree eagerly at sequence start — rejected: loop-iteration values are not
  known until the iteration runs, and FR-016 requires resolution at dispatch time.

---

## R6. Where the queue built-ins are injected

**Decision**: Build the root scope in `QueueExecutionService.RunOneSequenceAsync`
(QueueExecutionService.cs:508), from the `ExecutionQueue` already loaded at run start, and layer the
entry's `ParameterValues` on top before calling `_sequenceExecution.ExecuteAsync`.

Built-in names (reserved prefix `queue.`), each omitted from scope when its source field is unset
(FR-011):

| Name | Source | Type |
|---|---|---|
| `queue.emulatorSerial` | `ExecutionQueue.EmulatorSerial` | text |
| `queue.instanceName` | `ExecutionQueue.EmulatorInstanceName` | text |
| `queue.instanceIndex` | `ExecutionQueue.EmulatorInstanceIndex` | number |
| `queue.gameId` | `ExecutionQueue.LinkedGameId` | text |

**Self-reschedule**: `SelfRescheduleEntry` (QueueRunHandle.cs:186) must carry the originating scope
so a re-fired sequence resolves identically (FR-015). The re-fire path already routes back through
`RunOneSequenceAsync`, so this is one extra record field plus a pass-through.

**Rationale**: `EmulatorSerial` is the exact value the motivating scenario varies, and three queues
already differ by it — so US1 needs no operator configuration whatsoever (SC-002).

---

## R7. Validation split: save-time (static) vs run-time (dynamic)

**Decision**: Three checks, in three places.

| Check | Where | Outcome |
|---|---|---|
| Declaration well-formedness (name rules, reserved names, dup, defaults parse as declared type) | Command/Sequence save endpoint | 400, blocks save |
| Reference resolvability against *statically knowable* names (own declarations + `queue.*` + `iteration` when in a loop) | Command/Sequence save endpoint | 400, blocks save (FR-020) |
| Required parameters supplied for every enabled entry | Queue-template save (report) and queue start (refuse) | 200-with-warnings / 409 on start (FR-021, FR-022) |
| Ad-hoc value consumed by nothing in the reachable chain | Queue-template save | warning only, never blocks (FR-012b) |
| Resolution + numeric coercion | `ParameterScope` at dispatch | step failure (FR-017, FR-019) |

**Rationale**: A save-time check cannot know the ambient values a future queue will provide, so
save-time resolvability must be permissive about names a caller *could* supply. The rule that makes
this tractable: at save time a reference is an error only if it is neither declared on the entity nor
a valid `queue.*` built-in nor `iteration`-inside-a-loop. Anything else is assumed inheritable and is
caught by the queue-start check or, failing that, at dispatch.

**Note on the second allow-list**: `FileSequenceRepository.ValidateActionPayloads` is a separate
action-type allow-list from `SequenceStepValidationService`; both must stay in sync when payload
shapes change. This feature adds no action type, so no allow-list entry is needed — but the
parameter-reference check must be added to **both** validators or a parametrized sequence will pass
one and 500 on the other.

---

## R8. Static reference checks that a placeholder defeats

**Decision**: When a field that normally undergoes existence checking is parametrized — image and
detection reference IDs are the practical cases — skip the static check for that field, emit a
save-time warning, and validate the resolved value at run time (FR-023a).

**Rationale**: The target is unknown until resolution. Failing the save would forbid the legitimate
use; passing it silently would let an unknown image ID reach a device action, which FR-017 forbids.

**Explicitly excluded from substitution**: `SequenceStep.CommandId`, `SequenceCommandReference`, and
`QueueTemplateEntry.SequenceId`. Today `ApplyIterContext` *does* substitute `step.CommandId`
(SequenceRunner.cs:1303). That behaviour is retained for `{{iteration}}` only, so no existing
sequence breaks, but parameter names are rejected there at save time — keeping the dangling-reference
validation exact (FR-007).

---

## R9. Authoring UI approach

**Decision**: Three new reusable React pieces, then wire them into the existing editors.

| Piece | Responsibility |
|---|---|
| `ParameterDeclarationList` | The Parameters section (add/edit/reorder/remove). Follows the existing `ConfigParameterList`/`ConfigParameterRow` pattern already in the codebase. |
| `ParameterizableField` | Wraps an input with an insert-parameter affordance listing in-scope names + descriptions. Reuses `SearchableDropdown`. |
| `ParameterBindingForm` | Renders a callee's declarations as rows defaulting to "inherit", with effective-value + origin preview. |

Scope for the pickers comes from a new read-only endpoint per entity so the UI never re-derives
resolution rules client-side (avoiding two implementations that can disagree).

**Rationale**: Reusing `ConfigParameterList`, `SearchableDropdown` and `FormField` keeps the visual
language consistent (constitution III) and keeps the new component count to three.

---

## R10. Performance

**Decision**: No budget change; declare a per-step ceiling of **< 1 ms** for scope resolution and
substitution.

**Rationale**: Resolution is a walk over at most four small dictionaries plus a compiled-regex pass
over a handful of short strings, executed once per step dispatch. Every step it precedes performs
device I/O (ADB round-trip, screen capture, or template match) costing tens to hundreds of
milliseconds. The added cost is below measurement noise on the hot path. A micro-benchmark is added
under `tests/unit/Performance/` to hold the ceiling.

---

## Resolved unknowns

No `NEEDS CLARIFICATION` markers remain. Every open question from the spec has a decision above, and
the five behaviour decisions are recorded in the spec's Clarifications section.
