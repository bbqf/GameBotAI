# Research: API Structure Cleanup

## Decisions

1) Canonical base path
- **Decision**: Serve all public endpoints only under `/api/{resource}`.
- **Rationale**: Removes ambiguity, simplifies routing/middleware, and prevents drift in clients/tests.
- **Alternatives**: Dual paths (`/actions` and `/api/actions`) rejected due to duplication risk and contract confusion.

2) Legacy routes behavior
- **Decision**: Legacy roots (e.g., `/actions`, `/sequences`) return a non-success (404/410-style) with a message pointing to the canonical `/api/...` path; no redirects.
- **Rationale**: Clear signal without caching side-effects; keeps telemetry clean.
- **Alternatives**: HTTP 301/302 redirects rejected to avoid mixed caches and accidental success from old clients.

3) Swagger grouping
- **Decision**: Group endpoints by domain tags: Actions, Sequences, Sessions/Emulator, Configuration, Triggers/Detection (if present).
- **Rationale**: Matches feature spec and improves discoverability for API consumers.
- **Alternatives**: Flat tag list rejected because it scales poorly.

4) Swagger schemas and examples
- **Decision**: Each documented endpoint must include request/response schemas plus at least one concrete example payload.
- **Rationale**: Accelerates client integration and supports contract-test fixtures.
- **Alternatives**: Schemas without examples rejected due to higher onboarding friction.

5) Test alignment
- **Decision**: Update API/contract/integration tests to call only canonical `/api` routes; legacy-path tests should assert the non-success with guidance message instead of success.
- **Rationale**: Keeps the suite aligned with the new contract and prevents silent dual-support.
- **Alternatives**: Keep legacy tests passing (rejected; conflicts with goal of removing duplicates).

## Open Questions

None; specification contained no NEEDS CLARIFICATION items.
