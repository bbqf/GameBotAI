# Data Model — Sequence Logic Blocks

## Entities

### Sequence
- `id`: string
- `name`: string
- `steps`: [Step]
- `blocks`: [Block] | optional; supports nesting within `steps`

### Step
- `order`: int
- `commandId`: string
- `delayMs`: int? (>=0)
- `delayRangeMs`: { `min`: int>=0, `max`: int>=`min` }
- `gate`: Gate? (existing)

### Block (union by `type`)
- `type`: enum(`repeatCount`,`repeatUntil`,`while`,`ifElse`)
- `steps`: [Step|Block] (nested)
- `timeoutMs`: int? (>=0) — required when loop and `maxIterations` not set
- `maxIterations`: int? (>=1) — required when loop and `timeoutMs` not set
- `cadenceMs`: int? (default 100; bounds 50–5000)
- `control`: { `breakOn`: Condition? , `continueOn`: Condition? } — optional loop control
- `condition`: Condition? — required for `repeatUntil`/`while` and `ifElse`
- `elseSteps`: [Step|Block] — only for `ifElse`

### Condition
- `source`: enum(`image`,`text`,`trigger`)
- `targetId`: string
- `mode`: enum(`Present`,`Absent`)
- `confidenceThreshold`: float? (0..1) — for `image`/`text`
- `region`: { `x`,`y`,`width`,`height` }? — normalized (0..1), optional for `image`
- `language`: string? — optional for `text`

### Telemetry (execution result additions)
- `blocks`: [BlockResult]

### BlockResult
- `blockType`: string
- `iterations`: int
- `evaluations`: int
- `branchTaken`: string? (`then`/`else`)
- `durationMs`: int
- `appliedDelayMs`: int
- `status`: string (`Succeeded`|`Failed`|`Skipped`)

## Validation Rules
- Delay precedence: `delayRangeMs` overrides `delayMs` when present
- Loop safeguards: require either `timeoutMs` or `maxIterations`
- Cadence bounds: 50–5000ms
- Condition presence: required for `repeatUntil`,`while`,`ifElse`
- `elseSteps` only valid when `type=ifElse`
- Nested blocks allowed; treat `steps` as heterogeneous array of `Step|Block`
