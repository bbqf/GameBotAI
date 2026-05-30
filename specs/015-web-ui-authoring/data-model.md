# Data Model — Web UI Authoring (MVP)

## Entities

### Sequence
- id: string (server-assigned)
- name: string (required)
- steps: Step[]
- blocks: Block[]
- createdAt: DateTimeOffset (server-assigned)
- updatedAt: DateTimeOffset (server-assigned)

### Step
- order: int (required, unique within sequence)
- commandId: string (required)
- delayMs: int? (>=0)
- delayRangeMs: { min: int, max: int }? (0<=min<=max)
- gate: Gate?

### Gate
- targetId: string (optional)
- condition: Condition? (optional)

### Block
- type: enum [repeatCount, repeatUntil, while, ifElse]
- Fields by type:
  - repeatCount: { maxIterations: int (>=0), cadenceMs: int? (>=0) }
  - repeatUntil: { condition: Condition (required), timeoutMs: int? (>=0), maxIterations: int? (>=0), cadenceMs: int? (>=0) }
  - while: { condition: Condition (required), timeoutMs: int? (>=0), maxIterations: int? (>=0), cadenceMs: int? (>=0) }
  - ifElse: { condition: Condition (required), thenSteps: Step[], elseSteps: Step[] }

### Condition
- source: enum (image|text|other)
- targetId: string (optional)
- mode: enum (equals|contains|match|detect)
- confidenceThreshold: float? (0..1)
- region: Rect? { x:int, y:int, width:int, height:int }
- language: string? (OCR locale)

## Validation Rules
- `name` required; `order` unique per sequence.
- `delayMs` OR `delayRangeMs` allowed; not both simultaneously.
- `ifElse` is the only block that permits `elseSteps`.
- Loops (`repeatCount`, `repeatUntil`, `while`) must specify at least one limit (timeoutMs or maxIterations) except `repeatCount` which requires `maxIterations`.
- `cadenceMs` must be within reasonable bounds (>=0; upper bound enforced by service policies).
- Gate defaults: when no `gate` provided, step executes.
