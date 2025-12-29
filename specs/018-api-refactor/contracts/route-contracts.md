# API Contracts: Canonical Routes

## Base Path
- All public endpoints MUST reside under `/api/{resource}`.
- Legacy roots (e.g., `/actions`, `/sequences`) MUST return non-success (404/410-style) with a message pointing to `/api/...`; no redirects.

## Domain Groups and Tags
- Actions (tag: Actions)
- Sequences (tag: Sequences)
- Sessions/Emulator (tag: Sessions)
- Configuration (tag: Configuration)
- Triggers/Detection (tag: Triggers) — include only if routes exist

## Route Catalog (canonical)
- Actions (tag: Actions)
  - POST `/api/actions` — create action
  - GET `/api/actions` — list actions
  - GET `/api/actions/{id}` — fetch action
  - PATCH `/api/actions/{id}` — partial update
  - PUT `/api/actions/{id}` — full replace
  - POST `/api/actions/{id}/duplicate` — duplicate action
  - DELETE `/api/actions/{id}` — delete action
- Commands (tag: Commands)
  - POST `/api/commands`
  - GET `/api/commands`
  - GET `/api/commands/{id}`
  - PATCH `/api/commands/{id}`
  - DELETE `/api/commands/{id}`
  - POST `/api/commands/{id}/force-execute`
  - POST `/api/commands/{id}/evaluate-and-execute`
- Sequences (tag: Sequences)
  - POST `/api/sequences`
  - GET `/api/sequences`
  - GET `/api/sequences/{id}`
  - PUT `/api/sequences/{id}`
  - DELETE `/api/sequences/{id}`
  - POST `/api/sequences/{id}/execute`
- Sessions/Emulator (tag: Sessions)
  - POST `/api/sessions`
  - GET `/api/sessions/{id}`
  - GET `/api/sessions/{id}/device`
  - POST `/api/sessions/{id}/inputs`
  - POST `/api/sessions/{id}/execute-action`
  - GET `/api/sessions/{id}/health`
  - GET `/api/sessions/{id}/snapshot`
  - DELETE `/api/sessions/{id}`
- Games (tag: Games)
  - POST `/api/games`
  - GET `/api/games`
  - GET `/api/games/{id}`
  - PUT `/api/games/{id}`
  - DELETE `/api/games/{id}`
- Action Types (tag: Actions)
  - GET `/api/action-types`
- Configuration (tag: Configuration)
  - GET `/api/config`
  - POST `/api/config/refresh`
  - GET `/api/config/logging`
  - PUT `/api/config/logging/components/{componentName}`
  - POST `/api/config/logging/reset`
- Triggers/Detection (tag: Triggers)
  - POST `/api/triggers`
  - GET `/api/triggers`
  - GET `/api/triggers/{id}`
  - PUT `/api/triggers/{id}`
  - DELETE `/api/triggers/{id}`
  - POST `/api/triggers/{id}/test`
  - POST `/api/images/detect` (tag: Images) — detect reference image matches
- Images (tag: Images)
  - GET `/api/images`
  - POST `/api/images`
  - GET `/api/images/{id}`
  - DELETE `/api/images/{id}`
- Metrics (tag: Metrics)
  - GET `/api/metrics/triggers`
  - GET `/api/metrics/domain`
  - GET `/api/metrics/process`
- Emulators
  - GET `/api/adb/version` (tag: Emulators)
  - GET `/api/adb/devices` (tag: Emulators)
  - GET `/api/ocr/coverage` (tag: Emulators)

## Documentation Requirements
- Each endpoint entry in Swagger MUST include:
  - Request schema
  - Response schema
  - At least one concrete example payload (request or response) per endpoint
- Endpoints appear in exactly one tag/group matching their domain.

## Legacy Handling Contract
- Legacy paths are not served; responses include guidance to the canonical `/api/...` path.
- Contract/integration tests assert canonical paths succeed and legacy paths fail with guidance message.
