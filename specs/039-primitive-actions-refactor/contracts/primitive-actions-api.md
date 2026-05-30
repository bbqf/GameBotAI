# API Contract Delta: Primitive Actions Refactor

## Scope
This contract defines externally visible API changes required to remove Action as an authored entity and use inline discriminated primitive action selections.

## Breaking Changes
1. Action CRUD authored routes are removed from supported contract surface.
   - Removed: GET/POST/PATCH/PUT/DELETE under /api/actions
   - Removed: /api/actions/{id}/duplicate
2. Any request model that depended on Action identifiers for authored behavior is replaced by inline primitive selection payloads.

## Stable/Adjusted Routes
1. Primitive type catalog
   - GET /api/action-types
   - Purpose: enumerate allowed primitive discriminators and field definitions
   - Response requirement: deterministic schema metadata for each primitive type

2. Commands authored routes
   - POST /api/commands
   - GET /api/commands
   - GET /api/commands/{id}
   - PATCH /api/commands/{id}
   - Contract change: command steps that previously referenced Action IDs carry inline primitive selection payloads.

3. Sequences authored routes
   - POST /api/sequences
   - GET /api/sequences
   - GET /api/sequences/{id}
   - PATCH /api/sequences/{id}
   - Contract change: sequence action payloads use discriminated primitive selection model with typed payload validation.

4. Execution/session routes
   - POST /api/sessions/start
   - POST /api/commands/{id}/force-execute
   - POST /api/commands/{id}/evaluate-and-execute
   - Contract expectation: connect-to-game primitive selection parameters are validated before session start context is established.

## Canonical Inline Primitive Selection Shape

```json
{
  "primitiveAction": {
    "type": "connect-to-game",
    "schemaVersion": "v1",
    "payload": {
      "gameId": "game-1",
      "adbSerial": "emulator-5554"
    }
  }
}
```

Validation requirements:
1. type is required and must be supported.
2. payload must match the type discriminator schema.
3. Parameterless contexts must reject non-applicable stale payload fields.

## Error Contract Requirements
1. Invalid primitive discriminator or payload mismatch:
   - 400 with deterministic field-level diagnostics.
2. Legacy Action reference detected during startup/readiness validation:
   - service readiness/startup failure with deterministic diagnostics listing file + path + issue.
3. Unsupported authored route usage after cutover:
   - explicit unsupported/deprecated route response per implementation policy.

## Compatibility and Versioning
1. No read/write compatibility bridge for legacy Action references after cutover.
2. Release is blocked until migration + startup validation pass.
3. Any OpenAPI regeneration must reflect Action route removal and inline primitive schemas.
