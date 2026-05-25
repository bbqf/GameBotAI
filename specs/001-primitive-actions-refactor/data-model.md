# Data Model: Primitive Actions Data Model Refactor

## Entity: PrimitiveActionBase
- Purpose: Shared envelope for all primitive action selections.
- Fields:
  - type (string, required): canonical discriminator (tap, swipe, key, command, connect-to-game, etc.).
  - schemaVersion (string, optional): payload schema version for forward compatibility.
- Validation rules:
  - type must be one of supported canonical primitive types.
  - schemaVersion, when supplied, must be supported for the selected type.
- Relationships:
  - Exactly one PrimitiveActionVariant payload must match the discriminator.

## Entity: PrimitiveActionVariantTap
- Purpose: Typed payload for tap primitive.
- Fields:
  - x (number, required in direct-coordinate contexts)
  - y (number, required in direct-coordinate contexts)
  - durationMs (number, optional)
  - delayMs (number, optional)
- Validation rules:
  - x/y must be within configured screen-safe bounds when known.
  - durationMs/delayMs >= 0.

## Entity: PrimitiveActionVariantSwipe
- Purpose: Typed payload for swipe primitive.
- Fields:
  - x1, y1, x2, y2 (number, required)
  - durationMs (number, optional)
- Validation rules:
  - coordinate fields required together.
  - durationMs >= 0 when supplied.

## Entity: PrimitiveActionVariantKey
- Purpose: Typed payload for key-event primitive.
- Fields:
  - key (string, optional)
  - keyCode (number, optional)
- Validation rules:
  - at least one of key or keyCode required.
  - keyCode must be within accepted Android key code range when provided.

## Entity: PrimitiveActionVariantCommand
- Purpose: Typed payload for command-invocation primitive in sequence/action flow contexts.
- Fields:
  - commandId (string, required)
- Validation rules:
  - commandId must reference an existing command at validation/execution boundary.

## Entity: PrimitiveActionVariantConnectToGame
- Purpose: Typed payload for connect-to-game primitive and session context establishment.
- Fields:
  - gameId (string, required)
  - adbSerial (string, required)
- Validation rules:
  - both fields required for execution-tab session start.
  - adbSerial should match known device list when available, or explicit manual override policy.

## Entity: PrimitiveActionSelection
- Purpose: Shared inline-by-value primitive selection stored by command steps, sequence steps, and execution-tab request state.
- Fields:
  - primitiveAction (PrimitiveActionBase + matching variant payload, required)
  - context (enum, required): command-step | sequence-step | execution-connect.
  - metadata (object, optional): context-local labels/order hints.
- Validation rules:
  - payload must match discriminator and context constraints.
  - parameterless contexts must reject stale non-applicable payload fields.

## Entity: MigrationMappingRecord
- Purpose: Deterministic mapping from legacy Action references to inline PrimitiveActionSelection values.
- Fields:
  - sourceActionId (string, required)
  - sourceLocation (string, required): file/key path where reference was found.
  - mappedSelection (PrimitiveActionSelection, required)
  - migrationStatus (enum, required): migrated | blocked | invalid
  - diagnostics (array<string>, optional)
- Validation rules:
  - blocked/invalid records must include at least one diagnostic message.

## Entity: CutoverValidationReport
- Purpose: Startup/readiness blocking report for unresolved legacy references.
- Fields:
  - hasBlockingIssues (bool, required)
  - totalRecordsScanned (number, required)
  - blockingIssues (array<BlockingIssue>, required)
- BlockingIssue fields:
  - filePath (string, required)
  - jsonPath (string, required)
  - message (string, required)
- Validation rules:
  - service readiness fails when hasBlockingIssues is true.

## Relationships
- A command or sequence step owns exactly one PrimitiveActionSelection when step type is primitive-action-driven.
- MigrationMappingRecord translates legacy Action references into owned PrimitiveActionSelection values.
- CutoverValidationReport aggregates unresolved legacy references across persisted stores.

## State Transitions
- MigrationMappingRecord:
  - discovered -> migrated
  - discovered -> blocked
  - blocked -> migrated (after data correction)
- CutoverValidationReport:
  - pass (no blocking issues) -> startup continues
  - fail (blocking issues present) -> readiness/startup blocked
