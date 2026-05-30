# API Contracts — Sequences

## Endpoints
- POST `/api/sequences` — Create sequence
- GET `/api/sequences/{id}` — Get sequence
- PUT `/api/sequences/{id}` — Update sequence
- DELETE `/api/sequences/{id}` — Delete sequence
- POST `/api/sequences/{id}/execute` — Execute sequence

## Schemas
- SequenceDto: { id, name, steps[], createdAt, updatedAt }
- StepDto: { order, commandId, delayMs?, delayRangeMs? {min,max}, timeoutMs?, retry? {maxAttempts, backoffMs?} }
- ExecuteResultDto: { sequenceId, status, startedAt, endedAt, steps[] }

## Notes
- Execution is initiated and tracked synchronously with cancellable token; long steps permitted.
- Validation enforces delay precedence and range correctness.
