# Data Model — Command Sequences

## Entities
- Sequence: `id`, `name`, `steps[]`, `createdAt`, `updatedAt`.
- Step: `order`, `commandId` (ref existing), `delayMs?`, `delayRangeMs?` {min, max}, `timeoutMs?`, `retry?` {maxAttempts, backoffMs?}.
- DelayPolicy: derived at runtime (effective delay from fixed or range).

## Validation Rules
- `delayRangeMs` overrides `delayMs` when both provided.
- `delayRangeMs.min <= delayRangeMs.max`.
- `timeoutMs` optional; if provided, applies to the step execution.
- `retry.maxAttempts >= 1` when `retry` present.

## Storage Mapping
- Persist sequences under `data/commands/sequences/` as JSON files keyed by `id`.
- Reuse existing JSON repository patterns in `GameBot.Domain`.

## Execution Result Schema
- `sequenceId`, `status` (Succeeded, Failed, Canceled, PartialSuccess), `startedAt`, `endedAt`.
- `steps[]`: { `order`, `commandId`, `status`, `attempts`, `durationMs`, `error?` }.
