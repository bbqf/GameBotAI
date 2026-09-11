# Implementation Plan: Dry-Run / Validate-Only Sequence Mode

**Branch**: `082-dry-run-sequences` | **Date**: 2026-09-11 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/082-dry-run-sequences/spec.md`

## Summary

Deliver FR-002 from the PNS project's feature-request tracker
(`C:\src\PNS\docs\api-feature-requests.md`): an additive `dryRun` option on
both sequence create (`POST /api/sequences`, per-step request shape) and
sequence execute (`POST /api/sequences/{id}/execute`).

1. **Create-time dry-run** reuses the existing per-step validation pipeline
   (`EnrichCommandReferencesAsync` + `ValidatePerStepForPersistenceAsync`)
   unchanged, and simply skips the final `repo.CreateAsync` call, returning a
   `{ valid, dryRun, errors }` shape mirroring the existing
   `POST /api/sequences/{id}/validate` endpoint's response.
2. **Execute-time dry-run** adds one short-circuit gate inside
   `SequenceRunner.ExecuteSingleStepAsync` covering wait-for-image,
   reschedule-self, and every dispatched primitive action
   (tap/swipe/key/connect-to-game/ensure-game-running/go-to-home-screen/
   ensure-emulator-running) — so `SequenceExecutionService`'s action
   dispatcher, `SessionResolver`, and session start/capture are never reached
   for those step families. The one remaining family, a command-referencing
   step, gets its own narrower dry-run awareness one layer down (in
   `SequenceExecutionService.DispatchCommandAsync`): a cheap
   `ICommandRepository` existence check, still with no call into
   `CommandExecutor`, so a resolvable `commandId` is skipped exactly like the
   other families while an unresolvable one still fails loudly (preserving
   FR-010 instead of silently swallowing it, which a single blanket gate
   would have done). `Loop`/`If` control-flow (iteration counting,
   `ExitReason`, branch selection) is untouched throughout, so a dry-run
   execute still genuinely exercises structural/branching mechanics that
   don't depend on live device state. A per-step `imageVisible` condition (or
   any other live-capture read) is neutralized at its one source — the
   injected `conditionEvaluator` delegate — rather than at each of its several
   call sites inside `SequenceRunner`; a stale image reference is not
   specially preserved under dry-run (see research.md Unknown 3).

Both changes are purely additive: `dryRun` defaults to `false` everywhere it
is added, so every existing create/execute call, and every queue-scheduled
execution (which never passes it), is byte-for-byte unchanged.

## Technical Context

**Language/Version**: C# / .NET 9.0
**Primary Dependencies**: ASP.NET Core Minimal APIs (GameBot.Service), no new
third-party dependencies required
**Storage**: File-backed repositories (`FileSequenceRepository` et al.) —
unchanged; a dry-run create deliberately never reaches the repository's write
path, and no schema migration is needed since `dryRun` is a request-only flag,
never persisted.
**Testing**: xUnit 2.7.1 across `tests/unit`, `tests/integration`,
`tests/contract` (GameBot.UnitTests / GameBot.IntegrationTests /
GameBot.ContractTests)
**Target Platform**: Windows service host (existing GameBot.Service)
**Project Type**: Backend web service (single solution, layered: Domain /
Emulator / Service)
**Performance Goals**: No new performance budget — one additional boolean
branch on an already-executed per-step code path, and a dry-run run is
strictly *cheaper* than a real one (no I/O to the emulator/session). No
measurable latency/throughput regression expected.
**Constraints**: MUST NOT change behavior for any create/execute call that
omits `dryRun` or sets it `false` (regression-free per spec FR-011/FR-014);
MUST NOT let a `requireDispatch: true` step fail merely because dry-run
skipped it (contract invariant 6); MUST NOT require a session for a dry-run
execute regardless of the sequence's step content (FR-009, FR-015); MUST
leave `Loop`/`If`/`Break` control-flow evaluation genuinely real under
dry-run (FR-008).
**Scale/Scope**: One request field on two endpoints, one new canonical
outcome constant, one execution-engine short-circuit gate, one narrower
existence-check carve-out for command-referencing steps, and one
condition-evaluator wrapper. Confined to
`GameBot.Domain/Services/SequenceRunner.cs`,
`GameBot.Domain/Services/CommandDispatchOutcome.cs`,
`GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs`,
`GameBot.Service/Services/SequenceExecution/ISequenceExecutionService.cs`,
`GameBot.Service/Models/SequenceStepContracts.cs`,
`GameBot.Service/Endpoints/SequencesEndpoints.cs`, plus their existing test
counterparts. No new endpoints, entities, or persisted schema changes.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or
CI), implementation progression is blocked until failures are fixed or a
documented maintainer waiver exists.

- **I. Code Quality Discipline**: No new lint/static-analysis suppressions
  anticipated; the dry-run gate is a small, additive branch in an already-
  modular per-step dispatch method, and the `DryRunOutcomes` constant follows
  the existing `BreakOutcomes` pattern exactly. No underscores in new method
  names. PASS.
- **II. Testing Standards**: Each behavior — create-time skip-persistence,
  execute-time skip-dispatch per step type, `requireDispatch` never failing
  under dry-run, `Loop`/`Break` exit-reason mechanics staying real, no session
  required — requires a failing test added first that reproduces the gap
  through the real create/execute HTTP path (not a hand-built result bypassing
  the engine), mirroring the precedent set by features 080/081. PASS (gate
  honored by task design, not yet executed).
- **III. User Experience Consistency**: `dryRun`'s create-success shape reuses
  the existing sibling `/validate` endpoint's `{ valid, errors }` convention;
  its failure shape is byte-for-byte the existing create-failure shape. The
  new `skipped_dry_run` outcome follows the existing free-string outcome
  vocabulary (`executed`/`failed`/`skipped`/`break`/`no_break`/etc.) rather
  than inventing a new response field. PASS.
- **IV. Performance**: No hot-path change; a dry-run leaf-step gate is a
  single boolean check on a path that would otherwise perform real I/O — a
  dry-run run is strictly cheaper than the run it replaces. No new benchmark
  required. PASS.
- **V. Living Documentation**: `docs/architecture.md` will gain a new
  subsection (alongside "Break & loop execution...") documenting the `dryRun`
  option, its exact skip scope, and the `skipped_dry_run` outcome, in the same
  PR; spec Status line will be set to `Implemented` once implementation lands.

No violations requiring Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/082-dry-run-sequences/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md         # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
src/
├── GameBot.Domain/
│   └── Services/
│       ├── SequenceRunner.cs                       # DryRunOutcomes constant; dryRun
│       │                                             # param threaded through ExecuteAsync
│       │                                             # → ExecuteLoopStepAsync/ExecuteIfStepAsync
│       │                                             # → ExecuteSingleStepAsync; one gate
│       │                                             # before wait-for-image/reschedule-self/
│       │                                             # primitive-action dispatch; SkippedDryRun
│       │                                             # case in the command-fallback branch
│       └── CommandDispatchOutcome.cs                # new SkippedDryRun bool field
└── GameBot.Service/
    ├── Services/SequenceExecution/
    │   ├── SequenceExecutionService.cs              # dryRun param + pass-through;
    │   │                                             # conditionEvaluator wrapper for
    │   │                                             # image/text conditions;
    │   │                                             # DispatchCommandAsync existence-check
    │   │                                             # carve-out (no ForceExecuteDetailedAsync
    │   │                                             # call under dryRun)
    │   └── ISequenceExecutionService.cs              # dryRun param, default false
    ├── Models/
    │   └── SequenceStepContracts.cs                 # DryRun on SequenceExecuteContract
    │                                                 # and SequenceUpsertContract
    └── Endpoints/
        └── SequencesEndpoints.cs                     # ExecuteSequenceAsync + per-step
                                                        # branch of CreateSequenceAsync
                                                        # read/forward DryRun

tests/
├── unit/Sequences/
│   ├── SequenceRunnerDryRunTests.cs                 # (new) leaf dispatch skip per step
│   │                                                  # type, requireDispatch never fails,
│   │                                                  # Loop/Break exit-reason stays real,
│   │                                                  # stepOutcomes records skipped_dry_run
│   └── SequenceRunnerActionDispatchTests.cs          # existing — confirm unaffected when
│                                                       # dryRun omitted/false
└── integration/Sequences/
    ├── SequenceCreateDryRunIntegrationTests.cs        # (new) valid/invalid per-step bodies,
    │                                                   # 200 valid/400 invalid, nothing
    │                                                   # persisted either way
    └── SequenceExecuteDryRunIntegrationTests.cs        # (new) real HTTP-through-repository:
                                                          # no session required, tap step
                                                          # skipped_dry_run, dangling
                                                          # commandId still a real error
```

**Structure Decision**: Existing single-solution layered structure
(`GameBot.Domain` / `GameBot.Emulator` / `GameBot.Service`, with `tests/unit`,
`tests/integration`, `tests/contract`) is unchanged. This feature adds no new
projects or top-level directories — both changes land in existing files, per
the research above.

## Complexity Tracking

*No Constitution Check violations — table intentionally omitted.*
