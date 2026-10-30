# Implementation Plan: Command Editor Parameter Fields

**Branch**: `169-command-editor-parameter-fields` | **Date**: 2026-10-30 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/169-command-editor-parameter-fields/spec.md`

## Summary

The command editor gets a "Use a parameter" control for each step field that has a path in `CommandStepFieldPaths` and a field in the editor. The control writes and removes the placeholder `{{name}}` in `CommandStep.FieldTemplates`. The work is in the web UI only. The service, the API, and the format of `FieldTemplates` do not change (FR-010). Feature 168 files do not change.

The design has six parts (details in research.md):

1. The editor keeps `fieldTemplates` in a round trip (R1).
2. A TypeScript mirror of `CommandStepFieldPaths`, with a C# drift test (R2).
3. A `ParameterSourceControl` component that wraps each field (R4).
4. One pure fit function. It uses the type of the parameter, the input kind, and the type of the option values of a choice (R5).
5. A kept plain value while a template is active (R6).
6. A service error goes to the field of the step that the message names. The editor reads the step order from the message text and the path from the issue details. If it cannot find the step, it shows the error in the form summary only (R7).

## Technical Context

**Language/Version**: TypeScript with React (web UI, `src/web-ui`). C# on the current repo .NET version (drift test only).
**Primary Dependencies**: Existing only (React, Vitest, Testing Library, xUnit). No new dependency.
**Storage**: N/A. The command keeps `fieldTemplates` in the existing store. No schema change.
**Testing**: Vitest component and unit tests. One Vitest round-trip test for the page. One xUnit drift test (`FieldTemplatePathMirrorTests`). One service check for an empty inline image (see Phase order). Tests for the step type change rule and for the error mapping (two steps of the same type).
**Target Platform**: Web UI in a desktop browser.
**Project Type**: Web application (service plus web UI). This feature changes the web UI only.
**Performance Goals**: The control opens in under 100 ms with up to 50 parameters (checked by a test or a reasoned target). No extra network call. No service latency change.
**Constraints**: No API change. No change to the service rules for `FieldTemplates`. No change to feature 168 files (`demo-data.ps1`, quickstart). Keyboard access and screen reader names for each new control (FR-011).
**Scale/Scope**: 4 step panels (Swipe, Tap, WaitForImage, EnsureEmulatorRunning). 16 mirrored paths. 12 of them have a field in the editor, with the 4 `ensureGameRunning` numeric paths excluded.

**Source of the path list**: `CommandStepFieldPaths` is in `src/GameBot.Domain/Commands/CommandStep.cs`. The drift test reads this class and the TypeScript mirror.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Note |
|-----------|--------|------|
| I. Code quality | Pass | Small modules. One control, one helper file. Functions under 50 lines. No new dependency. |
| II. Testing | Pass | Unit, component, round-trip, drift, and service check tests. The 80% line and 70% branch baseline applies to the new files. |
| III. UX consistency | Pass | Hints are advice only. The service message shows at the field of the named step, or in the form summary. Phone level: **desktop-only**. It follows docs/mobile-ui-principles.md because the page keeps its "Use a larger screen" notice (FR-012). |
| IV. Performance | Pass | Local state only. No new request. Goal in Technical Context. |
| V. Living documentation | Pass | Update `docs/architecture.md` if it lists editor behavior, and refresh "Last reviewed". Set `Status` in spec.md at the end. Update `specs/STATUS.md`. |
| VI. STE | Pass | All new text is in STE. This includes UI labels and hints. |
| Build and test gate | Pass | No known failing build or test. |

Post-design re-check: no violation. No entry in Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/169-command-editor-parameter-fields/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── ui-contract.md   # Phase 1 output
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
src/GameBot.Domain/Commands/CommandStep.cs        # CommandStepFieldPaths (read only; no change)

src/web-ui/src/
├── components/
│   ├── parameters/
│   │   ├── fieldTemplatePaths.ts                 # NEW: path mirror, fit function, name helper, step-key rule
│   │   ├── ParameterSourceControl.tsx            # NEW: control and chip
│   │   └── __tests__/                            # NEW tests for both files
│   └── commands/
│       ├── CommandForm.tsx                       # StepEntry gets fieldTemplates; type change rule
│       ├── SwipePanel.tsx                        # control on 5 numeric fields
│       ├── TapPanel.tsx                          # control on 3 numeric fields and the image field
│       ├── WaitForImagePanel.tsx                 # control on timeoutMs and the image field
│       ├── EnsureEmulatorRunningPanel.tsx        # control on instanceIndex
│       └── __tests__/                            # panel tests
└── pages/CommandsPage.tsx                        # stepsFromDto, stepsToDto, error mapping to steps

tests/unit/Parameters/FieldTemplatePathMirrorTests.cs   # NEW drift test
docs/architecture.md                                    # update if it lists editor behavior
```

**Structure Decision**: Web application. All production changes are in `src/web-ui`. The only C# addition is one test file. `CommandStepFieldPaths` stays unchanged. No file of feature 168 changes.

## Phase order

The order below is binding for `/speckit-tasks`. Task 1 of Phase C blocks all image UI work.

- **Phase A, shared base**: path mirror and fit function (`fieldTemplatePaths.ts`), `StepEntry.fieldTemplates` and the DTO round trip, the step type change rule, the drift test. Then `ParameterSourceControl`.
- **Phase B, numeric fields (US1)**: Swipe, Tap numeric fields, WaitForImage `timeoutMs`, EnsureEmulatorRunning `instanceIndex`. Kept plain value, with 0 for an empty required whole-number field.
- **Phase C, image field (US2)**:
  1. **First task: check what the service does with an empty inline image together with an image template.** Use an integration test or a service call with the API. Do this BEFORE any image UI work. Record the result in a code comment and in the PR description. The test asserts the real behavior of the service.
  2. Branch on the result. If the service accepts it, the image control may save an empty inline image. If the service refuses it, the control asks the person to select a stored image first, then the parameter (R6, spec US2 scenario 4).
  3. Then build the image field in TapPanel and WaitForImagePanel.
- **Phase D, hints and errors (US3)**: fit hints, "Parameter not found" chip, queue values group, service error at the field of the named step (form-summary fallback).
- **Phase E, closing**: docs, `specs/STATUS.md`, spec `Status` line, CHANGELOG, version bump. Manual check that the Check 1 and Check 5a Part B flows of feature 168 run in the UI alone (SC-003).

## Changes from the earlier plan

- One step type change rule (R1, data-model): drop template keys of other step types, keep all other keys. A step of the same type keeps all keys.
- The service error shows at the field of the step that the message names. If the editor cannot find the step, it shows the form summary only (R7).
- The "Command parameter" entity includes the input kind (data-model).
- Feature 168 files do not change. The manual check shows that the UI replaces the API script (quickstart).
- Performance target is 100 ms. The first image task records the service result in a code comment and the PR description.
- FR-001 covers only fields that exist in the editor. The 4 `ensureGameRunning` numeric paths get no control (R3).
- Queue built-ins show in a separate group with no type hint (R9).
- A choice parameter fits by the type of its option values (R5).
- A missing parameter shows the chip text "Parameter not found" (R8).

## Complexity Tracking

No violation. No entry.
