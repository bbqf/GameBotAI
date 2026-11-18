# Implementation Plan: TextInput Command Step

**Branch**: `192-text-input-step` | **Date**: 2026-11-18 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/192-text-input-step/spec.md`

## Summary

Add the command step type `TextInput` with the config `textInput.text`. The service types the full resolved text into the focused input field of the device with `adb shell input text`. The key `textInput.text` in `fieldTemplates` lets a `text` or `number` parameter give the text (for example `{{fighterBatch}}` gives `250`). The step result has `typedLength`. An empty text, a character that is not printable ASCII, a text longer than 500 characters, or a device that does not accept the input gives `not_accepted` with a reason code. The command create and update requests get `"dryRun": true`, so that a `dryRun` reports an unknown placeholder in `textInput.text`.

The technical approach (see [research.md](research.md)):

1. Domain: add `CommandStepType.TextInput`, `TextInputConfig`, `CommandStep.TextInput`, the text path set `CommandStepFieldPaths.SupportedTextPaths` (`textInput.text`), and `TextInputRules` (permitted characters, maximum length). `CommandStepResolver` and `ParameterReferenceScanner` handle `textInput.text` (inline and overlay). `ParameterValidationService` requires one whole placeholder for the text path. `GamepackReferences` copies the new config.
2. Emulator: add `IAdbSessionClient.InputTextAsync`. `AdbClient` sends `shell input text '<quoted>'` through `ProcessStartInfo.ArgumentList`. `AdbTextEncoder` splits the text into chunks: a space goes as key event 62 (`KEYCODE_SPACE`), and no chunk holds `%s`. `SessionManager.SendInputsAsync` handles the input action type `text`, with one recorded input (the same ADB retries and liveness records as a key input, and no new time limit) for each chunk and each space, and writes `typedLength` back into the action arguments.
3. Service: DTOs, mapping, and save checks in `CommandsEndpoints` and `StepsEndpoints`; `CommandExecutor` runs the step and sets the reason codes; `PrimitiveTapStepOutcome.TypedLength` and `StepExecutionOutcomeDto.TypedLength`; an execution log detail item; `PreviewEffectRules` entry; `dryRun` on command create and update; OpenAPI text (`TextInputSchemaFilter`, the `fieldTemplates` text, the outcome field).
4. Web UI: `TextInputPanel` with a "Use a parameter" control for `textInput.text`, the "Text Input" option, the step mapping, and the mirror list `fieldTemplatePaths.ts` with the new kind `text`.
5. Docs: `docs/architecture.md`, `CHANGELOG.md`, `specs/STATUS.md`, the spec status, and the version bump.

## Technical Context

**Language/Version**: C# 13 on .NET 9 (service, domain, emulator); TypeScript 5.6 with React 18 (web UI)  
**Primary Dependencies**: ASP.NET Core minimal APIs, Swashbuckle (OpenAPI), System.Text.Json, ADB (Android Debug Bridge) through `AdbClient`; Vite, Jest, and Testing Library (web UI)  
**Storage**: JSON files of commands (`FileCommandRepository`). The new property `textInput` is not written when it is null, so stored commands without a `TextInput` step do not change.  
**Testing**: xUnit with FluentAssertions (`tests/unit`, `tests/integration`, `tests/contract`); Jest (`src/web-ui`)  
**Target Platform**: Windows service with an LDPlayer emulator through ADB; the web UI in a desktop browser  
**Project Type**: Web service with a web UI  
**Performance Goals**: The step sends one ADB command for each chunk and one key event for each space. For a number of 1 to 6 digits, the step sends 1 ADB command, the same cost as one `KeyInput` step. The check of the text is O(n) with n ≤ 500. No hot path changes.  
**Constraints**: The `KeyInput` behaviour does not change (FR-017). No shell injection on the device (FR-015). Stored commands without a `TextInput` step stay byte-for-byte the same.  
**Scale/Scope**: About 20 C# files, 8 web UI files, 1 OpenAPI filter, about 10 test classes, and the docs.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces (research, data model, contracts, quickstart, tasks, code comments, user-facing messages) MUST obey Simplified Technical English (Constitution Principle VI).

*Phone check (Constitution Principle III)*: The feature changes the command editor. The command editor is in the Authoring area, which is `desktop-only`. The phone support level of this feature is `desktop-only`. The new panel uses the same controls and layout as `KeyInputPanel` and `SwipePanel`, and follows docs/mobile-ui-principles.md.

| Gate | Result | Note |
|------|--------|------|
| I. Code quality | Pass | Lint and analyzers MUST pass with 0 errors. Methods stay under about 50 lines: the text step goes into a new private method `ExecuteTextInputAsync` of `CommandExecutor`. Public members get XML comments. Method names use CamelCase with no underscores. |
| II. Testing | Pass | Unit tests for `TextInputRules`, `AdbTextEncoder`, `SessionManager` text input, `CommandExecutor` text step, the resolver, the scanner, and the validation. Integration tests for the command routes (`dryRun` included) and the steps route. A contract test for the OpenAPI text. Jest tests for the panel and the mirror list. New test classes go to the complement shard. Run `scripts/ci/measure-shards.ps1` when a shard gets more than a few new classes (shard balance rule). |
| III. UX consistency | Pass | Reason codes follow the `key_input_*` pattern. Error texts name the field and the remedy. The OpenAPI document describes the new type. Phone level: `desktop-only`. |
| IV. Performance | Pass | No hot path change. The CHANGELOG entry says "no performance effect on other steps". |
| V. Living docs | Pass | `docs/architecture.md` gets the `TextInput` step, the text path, `typedLength`, and the command `dryRun`, with a new "Last reviewed" date. Spec 192 gets a Status line and a row in `specs/STATUS.md`. No earlier spec is reworked. |
| VI. STE | Pass | All new text uses STE. |
| Version bump | Planned | Bump `patch` in `installer/versioning/version.override.json` before the push (CLAUDE.md rule). |

Post-design check: all gates stay Pass.

## Project Structure

### Documentation (this feature)

```text
specs/192-text-input-step/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── text-input-step.md   # API contract: step config, fieldTemplates key, outcome, reason codes, dryRun
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
src/GameBot.Domain/Commands/CommandStep.cs                  # TextInput enum value, TextInputConfig, CommandStep.TextInput, SupportedTextPaths
src/GameBot.Domain/Commands/TextInputRules.cs               # NEW: permitted characters, maximum length, check result
src/GameBot.Domain/Parameters/CommandStepResolver.cs        # resolve textInput.text (inline and overlay)
src/GameBot.Domain/Parameters/ParameterReferenceScanner.cs  # scan textInput.text
src/GameBot.Domain/Services/ParameterValidationService.cs   # whole placeholder for the text path
src/GameBot.Domain/Gamepacks/GamepackReferences.cs          # copy TextInput
src/GameBot.Emulator/Adb/IAdbSessionClient.cs               # InputTextAsync
src/GameBot.Emulator/Adb/AdbClient.cs                       # InputTextAsync with ArgumentList
src/GameBot.Emulator/Adb/AdbTextEncoder.cs                  # NEW: chunks and device shell quotes
src/GameBot.Emulator/Session/SessionManager.cs              # input action type "text"
src/GameBot.Service/Models/Commands.cs                      # CommandStepTypeDto.TextInput, TextInputConfigDto, TypedLength, DryRun
src/GameBot.Service/Endpoints/CommandsEndpoints.cs          # mapping, save checks, dryRun
src/GameBot.Service/Endpoints/StepsEndpoints.cs             # mapping, save checks, outcome
src/GameBot.Service/Services/ICommandExecutor.cs            # PrimitiveTapStepOutcome.TypedLength
src/GameBot.Service/Services/CommandExecutor.cs             # ExecuteTextInputAsync
src/GameBot.Service/Services/StepThrough/PreviewEffectRules.cs  # TextInput = false
src/GameBot.Service/Services/ExecutionLog/ExecutionLogService.cs # text input detail item
src/GameBot.Service/Swagger/TextInputSchemaFilter.cs        # NEW: OpenAPI text of TextInputConfigDto
src/GameBot.Service/Swagger/ParametrizedReferenceImageSchemaFilter.cs # fieldTemplates text
src/GameBot.Service/GameBotServiceSetup.cs                  # register the filter
src/web-ui/src/components/commands/TextInputPanel.tsx       # NEW
src/web-ui/src/components/commands/ActionTypeSelector.tsx   # "Text Input" option
src/web-ui/src/components/commands/CommandForm.tsx          # type, label, panel
src/web-ui/src/pages/CommandsPage.tsx                       # DTO mapping
src/web-ui/src/services/commands.ts                         # types
src/web-ui/src/components/parameters/fieldTemplatePaths.ts  # textInput.text, kind 'text', prefix
tests/unit/...                                              # see tasks.md
tests/integration/...                                       # see tasks.md
tests/contract/TextInputOpenApiTests.cs                     # NEW
docs/architecture.md, CHANGELOG.md, specs/STATUS.md, installer/versioning/version.override.json
```

**Structure Decision**: The feature extends the current projects (`GameBot.Domain`, `GameBot.Emulator`, `GameBot.Service`, `src/web-ui`) and the current test projects. It adds 4 new source files and no new project.

## Complexity Tracking

No constitution violation. This section is empty.
