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
