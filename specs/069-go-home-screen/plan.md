# Implementation Plan: Go To Home Screen Action

**Branch**: `069-go-home-screen` | **Date**: 2026-07-17 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/069-go-home-screen/spec.md`

## Summary

Add a new parameterless action type, `go-to-home-screen`, that returns the Android device to its
home/main screen by sending the hardware HOME key (Android `KEYCODE_HOME` = 3), leaving the game
running in the background. It is the leave-game counterpart to `connect-to-game` and is wired
through exactly the same surfaces the existing `ensure-game-running` action uses (the closest
analogue — a parameterless device action wired as both a sequence action and a command step): the
action-type constants, the primitive-action validation/allow-lists, the sequence-runner dispatch
gate, the sequence-execution dispatcher, the command-step executor, the command DTO/enum mapping,
plus web-ui authoring and MCP tool-description parity.

The device operation reuses the existing session input pipeline (`SendInputsAsync` with a `key`
action of `keyCode = 3`), which already retries on Windows/ADB and returns a stub success on
non-Windows or non-ADB sessions — giving the graceful degradation the spec requires (FR-007)
without new ADB plumbing.

## Technical Context

**Language/Version**: C# / .NET (net8.0) backend; TypeScript + React (Vite) web-ui; TypeScript MCP server
**Primary Dependencies**: ASP.NET Minimal APIs + MVC controllers, System.Text.Json; React 18 / Vite / Jest; `@modelcontextprotocol/sdk` + zod
**Storage**: File-based repositories (sequences/commands persisted as JSON under the data dir); no schema migration needed (additive enum/const)
**Testing**: xUnit (`tests/unit`, `tests/integration`, `tests/contract`); Jest + Testing Library (web-ui); `vite build` + `jest` is the real web-ui green gate
**Target Platform**: Windows host driving Android emulators via ADB; degrades to stub on non-Windows
**Project Type**: Web service + companion web-ui + MCP server (multi-surface single repo)
**Performance Goals**: Action dispatch adds one ADB `input keyevent` (<1s typical); no hot-path impact
**Constraints**: CamelCase method names only (no underscores); functions ≈<50 LOC; ≥80% line / ≥70% branch coverage on touched areas; `docs/architecture.md` + `specs/STATUS.md` updated for the new capability
**Scale/Scope**: One new action type across ~7 backend files + ~3 web-ui files + MCP description text, with mirrored tests

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

- **I. Code Quality Discipline**: PASS — additive change mirroring an established action (`ensure-game-running`); no dead code; CamelCase; parameterless action needs no new config types. New public members get XML doc comments.
- **II. Testing Standards**: PASS — unit tests for validation allow-lists, runner dispatch gate, sequence dispatcher (sends HOME key), command-step executor, and repository payload validation; web-ui component tests for the selector/panel. Deterministic and isolated (session input is faked/stubbed).
- **III. UX Consistency**: PASS — action naming and behavior follow the existing `ensure-game-running` conventions (kebab-case action key, parameterless authoring panel, neutral outcome reporting).
- **IV. Performance**: PASS — a single key event per invocation; no measurable regression; no hot path touched.
- **V. Living Documentation**: PASS (planned) — `docs/architecture.md` capability/API surface updated with the new action; `specs/069-go-home-screen/spec.md` carries a `Status` line and `specs/STATUS.md` is updated; no earlier spec is superseded.

No violations → Complexity Tracking not required.

## Project Structure

### Documentation (this feature)

```text
specs/069-go-home-screen/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── go-to-home-screen-action.md
├── checklists/
│   └── requirements.md  # Spec quality checklist (from /speckit-specify)
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/GameBot.Domain/Actions/
├── ActionTypes.cs                 # + GoToHomeScreen = "go-to-home-screen"
├── PrimitiveActionTypes.cs        # + GoToHomeScreen const + add to All (auto-covers 3 allow-lists)
├── PrimitiveActionVariants.cs     # + PrimitiveGoToHomeScreenAction (parameterless)
└── PrimitiveActionValidationService.cs  # (no new case required — parameterless)

src/GameBot.Domain/Commands/
├── CommandStep.cs                 # + CommandStepType.GoToHomeScreen
└── FileSequenceRepository.cs      # + ActionTypes.GoToHomeScreen in supportedActionTypes set

src/GameBot.Domain/Services/
└── SequenceRunner.cs              # + GoToHomeScreen in IsDispatchedPrimitiveAction

src/GameBot.Service/Services/
├── SequenceExecution/SequenceExecutionService.cs  # + DispatchGoToHomeScreenAsync → key(HOME)
└── CommandExecutor.cs             # + CommandStepType.GoToHomeScreen branch → key(HOME)

src/GameBot.Service/
├── Models/Commands.cs             # + CommandStepTypeDto.GoToHomeScreen
└── Endpoints/CommandsEndpoints.cs # + DTO<->domain mapping + ValidateStep no-op case

src/mcp-server/src/tools/
└── commands.ts                    # description text: add GoToHomeScreen to the primitive list

src/web-ui/src/components/commands/
├── ActionTypeSelector.tsx         # + 'GoToHomeScreen' union member + <option>
├── GoToHomeScreenPanel.tsx        # NEW parameterless panel (mirrors EnsureGameRunningPanel)
└── CommandForm.tsx                # wire the new action type into add-step flow

tests/unit/…                       # validation, runner gate, dispatcher, command executor, repo
tests/integration/…                # sequence-with-go-home end-to-end (stub session)
src/web-ui/src/components/commands/__tests__/…  # selector + panel + form tests
```

**Structure Decision**: Existing multi-surface layout is reused unchanged. The feature is purely
additive — one new action type threaded through the same files that already carry
`ensure-game-running`. No new projects, endpoints, or persistence formats are introduced.

## Design Decisions

1. **Device mechanism = HOME key via the session pipeline, not a new ADB handler.**
   `SessionManager.SendInputsAsync` already maps a `key` action (`keyCode`/`key`) to
   `adb shell input keyevent`, already retries per `AppConfig`, and already returns a stub success
   on non-Windows/non-ADB sessions. Sending `keyCode = 3` (HOME) reuses all of that and satisfies
   graceful degradation (FR-007) with zero new ADB code. `KeyNameMap["HOME"] = 3` already exists.

2. **Parity model = `ensure-game-running`, not the `/api/sessions/start` endpoint.**
   `connect-to-game` also drives a session-lifecycle endpoint because it *starts* a session with a
   game/device payload. `go-to-home-screen` is parameterless and starts nothing, so the honest
   "same as connect-to-game" reading is *first-class action parity across authoring/validation/
   dispatch/tooling*, which `ensure-game-running` already exemplifies. No new session endpoint.

3. **Allow-list coverage via `PrimitiveActionTypes.All`.** Three validators
   (`SequenceStepValidationService`, `PrimitiveActionValidationService`,
   `ActionPayloadValidationService`) derive their supported set from `PrimitiveActionTypes.All`, so
   adding the const there covers them automatically. **`FileSequenceRepository.ValidateActionPayloads`
   keeps its own hard-coded set** and MUST be updated separately, or persisting a sequence that uses
   the action 500s.

4. **Dual surface (sequence action + command step).** Mirrors `ensure-game-running` exactly so the
   action is usable in both sequences (dispatched via `actionDispatcher`) and standalone commands
   (executed via `CommandExecutor`), and appears in the web-ui `ActionTypeSelector`.

## Complexity Tracking

No constitution violations — no entries required.
