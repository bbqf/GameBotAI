# Data Model

## Entities

### Command
- `id`: string (existing identifier)
- `name`: string (required)
- `triggerId`: string? (optional)
- `steps`: ordered list of `CommandStep` (required, may be empty)
- `detection`: `DetectionTarget`? (existing, optional; retained for backward compatibility with action detection flow)

### CommandStep
- `type`: enum [`Action`, `Command`, `PrimitiveTap`] (required)
- `targetId`: string? (required for `Action` and `Command`; null/omitted for `PrimitiveTap`)
- `order`: int (required, non-negative)
- `primitiveTap`: `PrimitiveTapConfig`? (required when `type=PrimitiveTap`, otherwise null)

Validation rules:
- `Action` step: `targetId` MUST reference an existing action id.
- `Command` step: `targetId` MUST reference an existing command id.
- `PrimitiveTap` step: `primitiveTap.detectionTarget` MUST be present and valid.

### PrimitiveTapConfig
- `detectionTarget`: `DetectionTarget` (required)

### DetectionTarget
- `referenceImageId`: string (required)
- `confidence`: number (optional, defaults to existing detection default)
- `offsetX`: int (optional, defaults 0)
- `offsetY`: int (optional, defaults 0)
- `selectionStrategy`: enum [`HighestConfidence`, `FirstMatch`] (optional, defaults to `HighestConfidence`; primitive tap uses `HighestConfidence` when multiple matches are available)

### PrimitiveTapExecutionOutcome
- `stepOrder`: int
- `status`: enum [`executed`, `skipped_detection_failed`, `skipped_invalid_target`, `skipped_invalid_config`]
- `reason`: string?
- `resolvedPoint`: object? `{ x: int, y: int }`
- `detectionConfidence`: number?

### CommandExecutionResult (response extension)
- `accepted`: int (existing compatibility field)
- `stepOutcomes`: list of `PrimitiveTapExecutionOutcome` (optional list; included when command includes primitive tap steps)

## Relationships
- One `Command` has many ordered `CommandStep` entries.
- Each `PrimitiveTap` step owns exactly one `PrimitiveTapConfig`.
- `PrimitiveTapConfig` references one `DetectionTarget`.
- `CommandExecutionResult` can include many primitive step outcomes keyed by `stepOrder`.

## State Transitions

### PrimitiveTapExecutionOutcome.status
- Start: evaluate detection for primitive step
- If detection fails / below threshold: `skipped_detection_failed`
- If detection succeeds but computed point is out of bounds: `skipped_invalid_target`
- If step configuration invalid at runtime safeguard: `skipped_invalid_config`
- If detection succeeds and point in bounds: `executed`

## Compatibility and Migration Notes
- Existing commands containing only `Action`/`Command` steps remain valid with no required data migration.
- Existing command-level `detection` semantics remain intact for action-step coordinate resolution behavior.
- New primitive tap steps are additive and rejected unless detection configuration is present.
