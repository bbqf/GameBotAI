# Research: Key Input and Swipe Primitive Actions

**Feature**: 054-key-swipe-actions
**Date**: 2026-06-04

## Finding 1: Domain Layer Already Has Action Classes

**Decision**: Reuse existing `PrimitiveKeyAction` and `PrimitiveSwipeAction` domain classes; no new domain model additions needed.

**Rationale**: Both classes are defined in `GameBot.Domain/Actions/PrimitiveActionVariants.cs`, have complete validation rules in `PrimitiveActionValidationService.cs`, and are registered in `PrimitiveActionTypes.All`. The type string constants `"key"` and `"swipe"` are also present in `ActionTypes.cs`.

**Alternatives considered**: Defining new command-specific config classes that re-declare fields — rejected because `PrimitiveKeyAction` and `PrimitiveSwipeAction` already have the right shape and are used by the sequence runner.

Key details:
- `PrimitiveKeyAction` (`PrimitiveActionVariants.cs:22-27`): fields `Key` (string), `KeyCode` (int?); validation requires `Key` OR `KeyCode`
- `PrimitiveSwipeAction` (`PrimitiveActionVariants.cs:12-20`): fields `X1`, `Y1`, `X2`, `Y2` (int), `DurationMs` (int?)
- Type strings: `"key"` (`ActionTypes.cs:10`), `"swipe"` (`ActionTypes.cs:9`)

## Finding 2: CommandStep Type System Is the Primary Gap

**Decision**: Add `KeyInput` and `Swipe` to `CommandStepType` enum and the parallel DTO types; add dedicated config classes at both domain and DTO layers.

**Rationale**: `CommandStepType` in `CommandStep.cs:3-8` is the authoritative discriminator for command-level steps. The service-layer `CommandStepTypeDto` in `Commands.cs:40-45` mirrors it. Both need new variants and corresponding typed config properties to maintain the type-safe pattern established by `PrimitiveTapConfig` / `WaitForImageConfig`.

**Alternatives considered**: Using a generic string type or embedding the step config in a dictionary — rejected to stay consistent with the existing enum + typed-config pattern.

Files to change:
- `src/GameBot.Domain/Commands/CommandStep.cs` — enum + `KeyInputConfig` + `SwipeConfig` classes + `CommandStep` properties
- `src/GameBot.Service/Models/Commands.cs` — `CommandStepTypeDto` enum + `KeyInputConfigDto` + `SwipeConfigDto` + `CommandStepDto` properties

## Finding 3: Sequences Need No Backend Changes

**Decision**: No sequence-layer backend changes required.

**Rationale**: `SequenceActionPayload` uses a generic `Type` (string) + `Parameters` (dictionary) model (`SequenceStep.cs:15-20`). Both `"key"` and `"swipe"` are already in `PrimitiveActionTypes.All`, so the sequence runner can already execute them. The sequence UI (if any) is also out of scope for this feature.

## Finding 4: Frontend Panel Pattern Is Well-Established

**Decision**: Follow `TapPanel.tsx` exactly for both new components.

**Rationale**: `TapPanel.tsx` establishes the canonical frontend pattern: per-field local state, `attempted` boolean for deferred validation, `action-panel` + BEM modifier CSS class, `action-panel__controls` button container, `initialValue` prop for edit repopulation, `onConfirm` / `onCancel` callbacks. `EnsureGameRunningPanel.tsx` shows the minimal variant (no fields).

`KeyInputPanel` is simpler than TapPanel — one required text field, no image selector needed.
`SwipePanel` mirrors the numeric field pattern from TapPanel — four required integer fields (StartX, StartY, EndX, EndY) plus one optional integer field (DurationMs).

## Finding 5: CommandExecutor Needs Two New Execution Handlers

**Decision**: Add `KeyInput` and `Swipe` branches to the existing if-then chain in `CommandExecutor.cs`.

**Rationale**: The executor switches on `CommandStepType` (lines 154-270). New types need branches that map step config to `InputActionDto`. The `InputActionDto` model in `Sessions.cs:25-30` already accepts any `Type` string + `Args` dictionary, so no session-layer changes are needed.

Execution payload shapes to produce:
- Key: `type = "key"`, `args = { ["key"] = config.Key }`
- Swipe: `type = "swipe"`, `args = { ["x1"] = config.StartX, ["y1"] = config.StartY, ["x2"] = config.EndX, ["y2"] = config.EndY, ["durationMs"] = config.DurationMs }`
