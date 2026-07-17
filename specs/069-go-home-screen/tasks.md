# Tasks: Go To Home Screen Action

**Feature**: 069-go-home-screen | **Branch**: `069-go-home-screen`
**Inputs**: [plan.md](plan.md), [spec.md](spec.md), [data-model.md](data-model.md), [research.md](research.md), [contracts/go-to-home-screen-action.md](contracts/go-to-home-screen-action.md)

**Mechanism**: send Android `KEYCODE_HOME` (keycode 3) via the existing session input pipeline.
**Parity model**: mirror `ensure-game-running` (parameterless; sequence action + command step + UI).

## Phase 1: Setup

No new dependencies, projects, or scaffolding. (Intentionally empty.)

## Phase 2: Foundational (blocking prerequisites)

These introduce the action-type identity every later phase depends on.

- [ ] T001 Add `public const string GoToHomeScreen = "go-to-home-screen";` to `src/GameBot.Domain/Actions/ActionTypes.cs`
- [ ] T002 Add `public const string GoToHomeScreen = "go-to-home-screen";` to `src/GameBot.Domain/Actions/PrimitiveActionTypes.cs` and include it in the `All` collection (this auto-covers `SequenceStepValidationService`, `PrimitiveActionValidationService`, and `ActionPayloadValidationService`)
- [ ] T003 Add a parameterless `PrimitiveGoToHomeScreenAction : PrimitiveActionBase` (ctor passes `PrimitiveActionTypes.GoToHomeScreen`) to `src/GameBot.Domain/Actions/PrimitiveActionVariants.cs`

**Checkpoint**: solution compiles; the new action type is a recognized primitive type in the three `PrimitiveActionTypes.All`-derived validators.

## Phase 3: User Story 1 — Send the device to the home screen from a sequence (P1) 🎯 MVP

**Goal**: A sequence step with the `go-to-home-screen` action returns the device to the home screen (game stays running) and records a succeeded step.

**Independent test**: Author/run a one-step sequence containing only this action against a running (stub or ADB) session and confirm it succeeds and dispatches a HOME key event.

- [ ] T004 [US1] Add `ActionTypes.GoToHomeScreen` to `IsDispatchedPrimitiveAction` in `src/GameBot.Domain/Services/SequenceRunner.cs` so the action is routed through `actionDispatcher` (not the command fallback)
- [ ] T005 [US1] In `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs`, route `ActionTypes.GoToHomeScreen` in `DispatchActionAsync` to a new `DispatchGoToHomeScreenAsync` that resolves the session (same single-running-session fallback as `DispatchPrimitiveInputAsync`), sends `new EmulatorInputAction("key", new Dictionary<string,object>{ ["keyCode"] = 3 })` via `_sessionManager.SendInputsAsync`, and returns `executed` on accept / `failed` otherwise (with an XML doc comment)
- [ ] T006 [US1] Add `ActionTypes.GoToHomeScreen` to the hard-coded `supportedActionTypes` set in `ValidateActionPayloads` in `src/GameBot.Domain/Commands/FileSequenceRepository.cs` (required or persisting a sequence that uses the action returns HTTP 500)
- [ ] T007 [P] [US1] Unit test: `IsDispatchedPrimitiveAction` returns true for a `go-to-home-screen` action — add to `tests/unit/Sequences/SequenceRunnerGameActionDispatchTests.cs` (or the primitive-dispatch test file)
- [ ] T008 [P] [US1] Unit test: dispatching a `go-to-home-screen` sequence action sends a `key`/keyCode 3 input and yields an `executed` outcome; missing session yields `failed` — add a test class under `tests/unit/Sequences/` (mirror `SequenceRunnerActionDispatchTests` / an ensure-game-running dispatch test)
- [ ] T009 [US1] Integration test: a sequence whose single step is `go-to-home-screen` runs to `Succeeded` on a stub session — add under `tests/integration/Sequences/`

**Checkpoint**: US1 is independently shippable — the action works end-to-end inside sequences.

## Phase 4: User Story 2 — Author the action through the same surfaces as connect-to-game (P2)

**Goal**: The action is selectable, validated, and round-trips wherever `ensure-game-running` / `connect-to-game` are — command authoring UI, command API, validation, and MCP tooling.

**Independent test**: In each surface (web-ui action picker, command create/update API, sequence validation, MCP tool description) confirm the action is offered/accepted and round-trips through save/load.

- [ ] T010 [US2] Add `GoToHomeScreen` to the `CommandStepType` enum in `src/GameBot.Domain/Commands/CommandStep.cs`
- [ ] T011 [US2] Add `GoToHomeScreen` to the `CommandStepTypeDto` enum in `src/GameBot.Service/Models/Commands.cs`
- [ ] T012 [US2] In `src/GameBot.Service/Endpoints/CommandsEndpoints.cs`, map `GoToHomeScreen` both directions in `MapStepTypeFromDto`/`MapStepTypeToDto`, and return `null` (no error) for it in `ValidateStep` (parallels `EnsureGameRunning`)
- [ ] T013 [US2] In `src/GameBot.Service/Services/CommandExecutor.cs` `ExecuteOneStepAsync`, add a `CommandStepType.GoToHomeScreen` branch that sends a `key`/keyCode 3 input via `_sessions.SendInputsAsync` and returns a `PrimitiveTapStepOutcome` with `StepType: "go-to-home-screen"` (mirror the `KeyInput` branch, fixed keycode)
- [ ] T014 [P] [US2] Add `'GoToHomeScreen'` to the `PrimitiveActionType` union and a `<option value="GoToHomeScreen">Go to Home Screen</option>` in `src/web-ui/src/components/commands/ActionTypeSelector.tsx`
- [ ] T015 [P] [US2] Create `src/web-ui/src/components/commands/GoToHomeScreenPanel.tsx` — a parameterless Add/Cancel panel mirroring `EnsureGameRunningPanel.tsx` (description: returns the device to the home screen, leaving the game running)
- [ ] T016 [US2] Wire the new action type into `src/web-ui/src/components/commands/CommandForm.tsx` so selecting it adds a `GoToHomeScreen` step (mirror the `EnsureGameRunning` add-step path)
- [ ] T017 [US2] Update the `create_command` (and, if it enumerates step types, `create_sequence`) tool description text in `src/mcp-server/src/tools/commands.ts` to list `GoToHomeScreen` among the primitive step types
- [ ] T018 [P] [US2] Unit test: `PrimitiveActionValidationService.Validate` accepts a `go-to-home-screen` action with no errors — add to `tests/unit/Domain/PrimitiveActionValidationServiceTests.cs`
- [ ] T019 [P] [US2] Unit test: `SequenceStepValidationService.Validate` accepts a step whose action type is `go-to-home-screen` — add to the sequence validation test suite under `tests/unit/Sequences/`
- [ ] T020 [P] [US2] Unit test: `FileSequenceRepository` upsert accepts a sequence containing a `go-to-home-screen` step (no `unsupported action type` throw) — add under `tests/unit/`
- [ ] T021 [P] [US2] Unit test: `CommandExecutor` executes a `GoToHomeScreen` command step by sending keyCode 3 — add under `tests/unit/Commands/` (mirror `CommandExecutorEnsureGameRunningTests`)
- [ ] T022 [P] [US2] web-ui tests: `ActionTypeSelector` offers the option, `GoToHomeScreenPanel` renders + fires Add/Cancel, and `CommandForm` adds the step — add under `src/web-ui/src/components/commands/__tests__/`

**Checkpoint**: US2 is complete — full authoring/validation/tooling parity.

## Phase 5: Polish & Cross-Cutting Concerns

- [ ] T023 Update `docs/architecture.md` (capability set + API surface) to document the `go-to-home-screen` action, and refresh its "Last reviewed" date (Constitution V)
- [ ] T024 Ensure `specs/069-go-home-screen/spec.md` carries an accurate `**Status**:` line on completion and update `specs/STATUS.md` to include feature 069
- [ ] T025 Run the green gate — backend `dotnet build` + `dotnet test`, and web-ui `npm run build` + `npm test -- --watchAll=false` (per [quickstart.md](quickstart.md)) — and fix any failures before marking the feature done

## Dependencies & Execution Order

- **Phase 2 (T001–T003)** blocks everything.
- **US1 (Phase 3)** depends only on Phase 2 → this is the MVP; ship after T004–T009.
- **US2 (Phase 4)** depends on Phase 2; independent of US1 (can proceed in parallel once foundations exist, though both touch the dispatch/executor areas).
- **Polish (Phase 5)** runs last.

### Parallel opportunities

- Within US1: T007, T008 are `[P]` (distinct test files); T009 after T004–T006.
- Within US2: T014, T015 (distinct new/edited UI files) and the test tasks T018–T022 are `[P]`; backend T010→T012 are sequential (same/adjacent files), T013 after T010/T011.

## Implementation Strategy

- **MVP = US1**: foundational constants + sequence dispatch + repository allow-list + tests. Delivers the core capability the request is about.
- **Increment = US2**: command-step + web-ui + MCP + validation parity to satisfy "available same as connect-to-game."
- **Finish**: living docs + STATUS + full green gate.
