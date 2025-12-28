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

## Route Catalog (representative)
- `POST /api/actions` — Create action
  - Tags: Actions
  - Request: Action create DTO; Example: minimal tap action payload
  - Response: Created action DTO; Example included in Swagger
- `GET /api/actions/{id}` — Get action
  - Tags: Actions
  - Response: Action DTO; Example included in Swagger
- `POST /api/sequences` — Create sequence
  - Tags: Sequences
  - Request/Response schemas documented with examples
- `GET /api/sessions/{id}/snapshot` — Get emulator snapshot
  - Tags: Sessions
  - Response: PNG stream; example describes content type and shape
- `GET /api/config` — Fetch configuration
  - Tags: Configuration
  - Response: Config DTO; example includes logging policy reference

## Documentation Requirements
- Each endpoint entry in Swagger MUST include:
  - Request schema
  - Response schema
  - At least one concrete example payload (request or response) per endpoint
- Endpoints appear in exactly one tag/group matching their domain.

## Legacy Handling Contract
- Legacy paths are not served; responses include guidance to the canonical `/api/...` path.
- Contract/integration tests assert canonical paths succeed and legacy paths fail with guidance message.
