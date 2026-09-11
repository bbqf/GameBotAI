# Phase 0 Research: Dry-Run / Validate-Only Sequence Mode

## Unknown 1: Where does create-time validation live, and can dry-run reuse it without persisting?

**Decision**: Scope create-time `dryRun` to the modern **per-step** request shape only
(`SequencesEndpoints.TryReadPerStepRequest` → `SequenceUpsertContract`), the only create
path that runs `EnrichCommandReferencesAsync` + `ValidatePerStepForPersistenceAsync`
(structural + reference validation). The two legacy shapes handled by `CreateSequenceAsync`
(bare `{name, steps: string[]}` and the raw domain-object fallback) run no structural
validation today and are not what FR-002's evidenced cost (Loop nesting / condition
placement / command-id mistakes) was paid against.

**Rationale**: `SequencesEndpoints.CreateSequenceAsync` (`src/GameBot.Service/Endpoints/SequencesEndpoints.cs:36`)
branches on payload shape. The per-step branch (lines 47-84) is the only one that:
1. Resolves command references (`EnrichCommandReferencesAsync`, line 57)
2. Runs full structural + reference validation (`ValidatePerStepForPersistenceAsync`, line 58 →
   `SequenceStepValidationService.Validate` + per-step image reference resolution)
3. Persists only after both pass (`repo.CreateAsync`, line 83)

A dry-run only has to skip step 3. Steps 1-2 are already exactly the validation whose
repeated live-run cost FR-002 cites.

**Alternatives considered**: Extending dry-run to the legacy shapes too — rejected, they
have no structural validation to reuse and are not part of the evidenced cost; adding it
would mean inventing new validation for paths nothing exercises today.

## Unknown 2: Where does execution actually reach the emulator, and what's the minimal choke point?

**Decision**: Gate dry-run inside `SequenceRunner.ExecuteSingleStepAsync`
(`src/GameBot.Domain/Services/SequenceRunner.cs:434`) for every leaf step type **except**
the ordinary command-referencing fallback path, which needs its own, narrower dry-run
awareness (see "Command-step carve-out" below) to preserve FR-010's stale-`commandId`
error without reaching real dispatch machinery.

**Rationale**: `ExecuteSingleStepAsync` routes `Loop`/`If` steps to their own recursive
handlers immediately (lines 456-489) and returns before reaching any dispatch. A dispatched
primitive action (tap/swipe/key/connect-to-game/ensure-game-running/go-to-home-screen/
ensure-emulator-running, gated by `IsDispatchedPrimitiveAction`, line 760), `reschedule-self`
(line 623), and `waitForImage` (`IsWaitForImageStep`, line 770) all funnel through this one
method before reaching `actionDispatcher`, and none of them has a repository-backed
existence check worth preserving under dry-run (a tap's coordinates are inline; a
`waitForImage`'s stale-image case is deliberately not specially detected — see Unknown 3).
Inserting one `dryRun` gate immediately before those three blocks (after the per-step
`Gate`/delay handling, before the `IsWaitForImageStep` check at line 605) means:
- `SequenceExecutionService.DispatchActionAsync` is **never invoked** for one of these
  three step families under dry-run — no `SessionResolver` lookup, no
  `ISessionManager.SendInputsAsync`, no `ISessionService.StartSession` call happens for them,
  contributing to FR-009 (no session required).
- One code path produces the `skipped_dry_run` outcome for all three families (FR-006,
  FR-007, SC-004), instead of duplicating the gate per family.
- `Loop`/`If` control-flow (iteration counting, `ExitReason`, branch selection) is completely
  unaffected (FR-008) — it never reaches this method's dispatch tail.

**Command-step carve-out**: The command-referencing fallback path (line 678,
`commandDispatcher`/`SequenceExecutionService.DispatchCommandAsync` →
`_commandExecutor.ForceExecuteDetailedAsync`) is deliberately **not** intercepted by the
same blanket gate. `DispatchCommandAsync` already has `ICommandRepository` injected
(`_commandRepository`, used today for command-name lookups at line 308) and is a local
function closing over `dryRun` once `ExecuteCoreAsync` accepts it as a parameter — no new
signature needed on the `commandDispatcher` delegate type itself. When `dryRun` is true,
`DispatchCommandAsync` performs the cheap existence check
(`_commandRepository.GetAsync(commandId, ct)`) it needs anyway, throws the same
`InvalidOperationException("Command '{commandId}' was not found...")` used today
(line 234-236) when it resolves to nothing, and otherwise returns a `CommandDispatchOutcome`
carrying a new `SkippedDryRun: true` marker **without ever calling**
`_commandExecutor.ForceExecuteDetailedAsync` — so `CommandExecutor`,
`ISessionManager.SendInputsAsync`, and `SessionResolver` are still never reached for a
resolvable command reference, while an unresolvable one still fails loudly (FR-010).
`SequenceRunner`'s existing `!cmdDispatch.Dispatched` / `RequireDispatch` check (line 715)
is extended to treat `SkippedDryRun: true` as its own case (report `skipped_dry_run`,
never trip the `RequireDispatch` miss-check) rather than falling into the "nothing
dispatched" branch.

**Alternatives considered**: Gating the command path the same blanket way as the other
three families (my first-pass design) — rejected on review: it would silently swallow the
existing dangling-`commandId` error path (a real defect, not a simplification, since FR-010
explicitly requires that error to survive `dryRun`). Gating inside
`SequenceExecutionService.DispatchActionAsync` plus a parallel gate inside `CommandExecutor`
for every family — rejected: more choke points than necessary for the three families that
have no existence check to preserve, and the exact allow-list-divergence pitfall already on
file for action-type registration (see `sequence-action-type-allowlists` in project memory).

## Unknown 3: What happens to a per-step `imageVisible` condition and other live-capture reads under dry-run?

**Decision**: Wrap the `conditionEvaluator` delegate at its single construction site
(`SequenceExecutionService.ExecuteCoreAsync`, `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs:257-281`)
so that, when `dryRun` is true, an `image`/`text`-sourced condition short-circuits to `false`
without calling `EvaluateImageConditionAsync` or `_evalSvc.Evaluate` — i.e. without reading
live screen-capture state. A `commandOutcome`-sourced condition is untouched by this wrapper
(it never goes through `conditionEvaluator` — `SequenceRunner` resolves it directly against
its own in-memory `stepOutcomes` dictionary, lines 544-574).

**Rationale**: `conditionEvaluator` is the one place `SequenceRunner` reaches out for
live device state, and it is invoked from several sites inside `SequenceRunner` — the
per-step `imageVisible` gate (line 508-513), `WaitForImageAsync`'s own condition checks, and
any `image`/`text`-sourced condition inside a `Loop`'s `breakOn` or an `If`'s condition tree.
Wrapping the delegate once, at its construction, covers every one of those call sites
without hunting down and special-casing each. This keeps FR-009 true even for a sequence
containing an `imageVisible`-gated step and no session: the condition resolves to `false`
(the step is skipped, exactly like the existing "condition evaluated false" path,
lines 531-541) instead of throwing or hanging on a missing capture source.

**Alternatives considered**: Special-casing `ImageVisibleStepCondition` inside
`SequenceRunner.ExecuteSingleStepAsync` directly — rejected: `SequenceRunner` is in
`GameBot.Domain` and has no knowledge of what "live capture" means (that's a `GameBot.Service`
concern reached only through the injected delegate); wrapping the delegate keeps the
Domain/Service boundary intact and fixes every use site, not just the one currently visible.

## Unknown 4: What's the new outcome value, and does it need registering anywhere `success`/`failed`/`skipped`/`break`/`no_break` already are?

**Decision**: Add `DryRunOutcomes.SkippedDryRun = "skipped_dry_run"` as a small static
class alongside the existing `BreakOutcomes` (`src/GameBot.Domain/Services/SequenceRunner.cs:23-32`),
following the same canonical-constant pattern. Record it into `stepOutcomes[stepKey]` for a
dry-run-skipped step (so a later `commandOutcome` reference to that step naturally resolves
to a non-matching value — it does not equal `success`/`failed`/`skipped`/`break`/`no_break`,
so any such reference is `false`, per the spec's documented edge case). Do **not** add it to
`SequenceStepValidationService`'s `AllowedCommandOutcomeStates` allow-list — a sequence
author should never author a condition that expects `skipped_dry_run` as a real branching
outcome; it only ever appears as an actual runtime value, and any reference to a
dry-run-skipped step already fails to match through the existing allowed vocabulary.

**Rationale**: There is no enum anywhere in this stack for step/action outcomes (confirmed —
every outcome is a bare string compared `OrdinalIgnoreCase`), so adding a new outcome value
is not an exhaustive-switch change. `ActionDispatchResult` (`Outcome`, `Message`) already
accepts any string; `result.AddStep(..., actionOutcome: ...)` on `SequenceExecutionResult`
(`SequenceRunner.cs:2208`, `StepResult`) does too.

**Alternatives considered**: Reusing the existing `"skipped"` outcome — rejected: it is
already meaningful (a per-step condition evaluated false, or an `If` branch not taken) and
overloading it would make a dry-run run indistinguishable from a real conditional skip,
directly against SC-004's requirement that every dry-run-skipped step be distinguishable by
one documented value.

## Unknown 5: How does `dryRun` thread through the public execution surface without breaking queue execution?

**Decision**: Add `bool dryRun = false` as a new trailing optional parameter (before `ct`) on:
- `SequenceRunner.ExecuteAsync` (`src/GameBot.Domain/Services/SequenceRunner.cs:82`) and its
  private recursive helpers (`ExecuteLoopStepAsync`, `ExecuteIfStepAsync`, `ExecuteSingleStepAsync`)
  that need to see it at the leaf-dispatch gate.
- `ISequenceExecutionService.ExecuteAsync` (`src/GameBot.Service/Services/SequenceExecution/ISequenceExecutionService.cs:30`,
  the scope-taking overload) and its `SequenceExecutionService` implementation
  (`ExecuteAsync`/`ExecuteCoreAsync`, lines 105-169).

Default `false` everywhere. `QueueExecutionService.cs:666`'s existing call site (and the
non-scope `ExecuteAsync` overload at `ISequenceExecutionService.cs:15`, which forwards into
the scope-taking one) compile and behave unchanged — satisfying FR-012 as a consequence of
the default rather than a separate runtime guard.

**Rationale**: Both `SequenceRunner.ExecuteAsync` and `ISequenceExecutionService.ExecuteAsync`
already grew an additive optional parameter this way for feature 078's `scope` — same
established pattern in this codebase, same file, same method.

**Alternatives considered**: An `ExecutionOptions` wrapper object bundling `dryRun` with
future flags — rejected as speculative; nothing else needs bundling today, and the codebase's
own precedent (adding one plain optional parameter at a time) is simpler and consistent.

## Unknown 6: What does the create-time dry-run response look like on success?

**Decision**: `Results.Ok(new { valid = true, dryRun = true, errors = Array.Empty<string>() })`
on success; on failure, the **exact same** `Results.BadRequest(new { message = "Invalid
sequence payload", errors = perStepValidationErrors })` shape the real (non-dry-run) create
already returns for the same body (FR-004) — no new error shape to invent.

**Rationale**: `{ valid, errors }` mirrors the existing, separate
`POST /api/sequences/{sequenceId}/validate` endpoint's response shape
(`ValidateSequenceFlowAsync`, `SequencesEndpoints.cs:293-310`: `Results.Ok(new { valid = true,
errors = Array.Empty<string>() })` / `Results.BadRequest(new { valid = false, errors =
allErrors })`) — reusing an established response convention in the same file rather than
inventing a third shape. `dryRun: true` is added so a client can tell a dry-run response
apart from a real `201 Created` at a glance even if it only inspects the body.

**Alternatives considered**: Returning `201 Created` with a fake/synthetic id — rejected,
actively misleading (nothing was created, and a caller might `GET` or reference the id);
returning `204 No Content` — rejected, loses the ability to carry `errors`/`valid` on the
(unreachable in practice, since a thrown validation error already short-circuits earlier)
success path, and is less consistent with the sibling `/validate` endpoint's shape.

## Summary of touched files

| File | Change |
|---|---|
| `src/GameBot.Domain/Services/SequenceRunner.cs` | `DryRunOutcomes` constant; `dryRun` param threaded through `ExecuteAsync` → `ExecuteLoopStepAsync`/`ExecuteIfStepAsync`/`ExecuteSingleStepAsync`; one dry-run gate before the wait-for-image/reschedule-self/primitive-action dispatch blocks; the command-fallback branch's `!cmdDispatch.Dispatched` handling gains a `SkippedDryRun` case ahead of the `RequireDispatch` check |
| `src/GameBot.Domain/Services/CommandDispatchOutcome.cs` | New `SkippedDryRun` bool (default `false`) alongside `Dispatched`/`Reason` |
| `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs` | `dryRun` param on `ExecuteAsync`/`ExecuteCoreAsync`; pass-through to `_runner.ExecuteAsync`; wrap `conditionEvaluator` to short-circuit image/text conditions when `dryRun`; `DispatchCommandAsync` (local function, closes over `dryRun`) performs a `_commandRepository.GetAsync` existence check and returns `SkippedDryRun: true` instead of calling `_commandExecutor.ForceExecuteDetailedAsync` when `dryRun` and the command resolves; still throws the existing "not found" error when it doesn't |
| `src/GameBot.Service/Services/SequenceExecution/ISequenceExecutionService.cs` | `dryRun` param, default `false`, on the scope-taking `ExecuteAsync` overload |
| `src/GameBot.Service/Models/SequenceStepContracts.cs` | `DryRun` (bool, default false) on `SequenceExecuteContract`; `DryRun` on `SequenceUpsertContract` (create path only) |
| `src/GameBot.Service/Endpoints/SequencesEndpoints.cs` | `ExecuteSequenceAsync` reads/forwards `DryRun`; per-step branch of `CreateSequenceAsync` reads `DryRun`, skips `repo.CreateAsync` and returns the dry-run success/failure shape when true |
| `docs/architecture.md` | New subsection documenting the `dryRun` option, `skipped_dry_run` outcome, and its scope (per-step create path + execute) |

No changes needed in `CommandExecutor.cs`, `FileSequenceRepository.cs`, or
`SequenceStepValidationService.cs` — `CommandExecutor` itself is still never invoked for a
dry-run command step (the existence check happens one layer up, directly against
`ICommandRepository`), and create-time dry-run reuses existing validation unchanged.
