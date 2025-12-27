# Contracts — Sequence Blocks API Additions

## Sequence JSON Schema Extensions

- `blocks`: array of `Block` objects
- `Block` discriminated by `type`: `repeatCount` | `repeatUntil` | `while` | `ifElse`
- `steps`: array of `Step | Block`
- `timeoutMs`, `maxIterations`, `cadenceMs`, `control`, `condition`, `elseSteps`

### OpenAPI Snippet (conceptual)

```yaml
components:
  schemas:
    Condition:
      type: object
      required: [source, targetId, mode]
      properties:
        source:
          type: string
          enum: [image, text, trigger]
        targetId:
          type: string
        mode:
          type: string
          enum: [Present, Absent]
        confidenceThreshold:
          type: number
          minimum: 0
          maximum: 1
        region:
          $ref: '#/components/schemas/Rect'
        language:
          type: string
    Block:
      type: object
      required: [type, steps]
      properties:
        type:
          type: string
          enum: [repeatCount, repeatUntil, while, ifElse]
        steps:
          type: array
          items:
            anyOf:
              - $ref: '#/components/schemas/Step'
              - $ref: '#/components/schemas/Block'
        timeoutMs:
          type: integer
          minimum: 0
        maxIterations:
          type: integer
          minimum: 1
        cadenceMs:
          type: integer
          minimum: 50
          maximum: 5000
        control:
          type: object
          properties:
            breakOn:
              $ref: '#/components/schemas/Condition'
            continueOn:
              $ref: '#/components/schemas/Condition'
        condition:
          $ref: '#/components/schemas/Condition'
        elseSteps:
          type: array
          items:
            anyOf:
              - $ref: '#/components/schemas/Step'
              - $ref: '#/components/schemas/Block'
```

## Endpoints

- `POST /api/sequences` — accepts extended schema
- `GET /api/sequences/{id}` — returns extended schema
- `POST /api/sequences/{id}/execute` — returns extended `BlockResult` telemetry

## Examples

Repeat Count:

```json
{
  "type": "repeatCount",
  "maxIterations": 3,
  "cadenceMs": 250,
  "control": {
    "breakOn": { "source": "trigger", "targetId": "battle-won", "mode": "Present" },
    "continueOn": { "source": "image", "targetId": "low-health", "mode": "Absent" }
  },
  "steps": [ { "order": 1, "commandId": "tap-heal" }, { "order": 2, "commandId": "tap-attack" } ]
}
```

Repeat Until:

```json
{
  "type": "repeatUntil",
  "timeoutMs": 10000,
  "cadenceMs": 300,
  "condition": { "source": "image", "targetId": "boss-defeated", "mode": "Present" },
  "control": {
    "breakOn": { "source": "trigger", "targetId": "abort", "mode": "Present" },
    "continueOn": { "source": "text", "targetId": "energy", "mode": "Equals", "value": "0" }
  },
  "steps": [ { "order": 1, "commandId": "tap-attack" } ]
}
```

While:

```json
{
  "type": "while",
  "timeoutMs": 8000,
  "cadenceMs": 200,
  "condition": { "source": "trigger", "targetId": "in-combat", "mode": "Present" },
  "control": {
    "breakOn": { "source": "image", "targetId": "disconnect", "mode": "Present" },
    "continueOn": { "source": "image", "targetId": "loading", "mode": "Present" }
  },
  "steps": [ { "order": 1, "commandId": "tap-skill-1" }, { "order": 2, "commandId": "tap-skill-2" } ]
}
```

If/Else:

```json
{
  "type": "ifElse",
  "condition": { "source": "text", "targetId": "menu", "mode": "Contains", "value": "Settings" },
  "thenSteps": [ { "order": 1, "commandId": "tap-settings" } ],
  "elseSteps": [ { "order": 1, "commandId": "tap-back" } ]
}
```

## Telemetry

BlockResult example:

```json
{
  "blockType": "repeatCount",
  "iterations": 3,
  "evaluations": 5,
  "durationMs": 1240,
  "status": "Succeeded"
}
```

If/Else telemetry:

```json
{
  "blockType": "ifElse",
  "branchTaken": "then",
  "evaluations": 1,
  "durationMs": 75,
  "status": "Succeeded"
}
```

## Structured Logging

Emitted by `SequenceRunner` via `LoggerMessage` delegates:

- BlockStart: `{BlockType} for {SequenceId}`
- BlockEnd: `{BlockType} status {Status} iterations {Iterations} evaluations {Evaluations}`
- BlockEvaluation: `{BlockType} {Evaluation} outcome {Outcome}`
  - Evaluations: `condition-start`, `condition-mid`, `breakOn-start`, `breakOn-mid`, `continueOn-mid`
- BlockDecision: `{BlockType} {Decision} at iteration {Iteration}`
  - Decisions: `break`, `continue`
