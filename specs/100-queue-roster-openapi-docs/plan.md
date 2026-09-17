# Implementation Plan: Make a Queue's Live Roster Discoverable

**Branch**: `100-queue-roster-openapi-docs` | **Date**: 2026-09-17 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/100-queue-roster-openapi-docs/spec.md` (GitHub issue #179)

## Summary

`GET /api/queues/{id}` already returns (and already exemplifies) the queue's roster as `entries`, but the
published OpenAPI document never says so: `QueueDetailResponse.entries` has no description and is emitted
`nullable: true`, `QueueEntryResponse`'s four fields are undescribed, and the `POST`/`PUT
/api/queues/{id}/entries` operations — where a consumer looking for a "list entries" route lands — carry no
description. Add a Swashbuckle `ISchemaFilter` (`QueueRosterSchemaFilter`) that describes `entries` (and
clears its `nullable`, keeping `readOnly`) and the four entry fields, and extend the queue operation filter in
`SwaggerConfig.cs` so the `GET /api/queues/{id}` description gains a roster sentence (health text kept) and
the two `/entries` write operations point at `GET /api/queues/{id}` `.entries`. A contract test pins all of it
plus the existing `entries` example. No route, response body, status code or runtime change.

## Technical Context

**Language/Version**: C# / .NET 9 (GameBot.Service)
**Primary Dependencies**: Swashbuckle.AspNetCore (SwaggerGen `ISchemaFilter`, `IOperationFilter`), Microsoft.OpenApi
**Storage**: N/A
**Testing**: xUnit + FluentAssertions contract tests (`tests/contract`, `WebApplicationFactory<Program>` reading `/swagger/v1/swagger.json`); existing `QueuesApiContractTests` as the no-behaviour-change guard
**Target Platform**: Windows service (ASP.NET Core minimal APIs)
**Project Type**: web-service
**Performance Goals**: None beyond existing; the filter runs only during OpenAPI document generation (a handful of string assignments on two schemas)
**Constraints**: The service does not feed XML comments to Swagger — schema descriptions must come from a schema filter (precedent: `QueueHealthSchemaFilter`, `ImageAlternatesSchemaFilter`, `SequenceTimeLimitSchemaFilter`, `SequenceNestingRulesSchemaFilter`); operation summaries/descriptions/examples for queues live in `SwaggerConfig.ApplyQueueExamples` branches using `??=`
**Scale/Scope**: 1 new filter file, 1 registration line, two operation-description edits plus one constant in `SwaggerConfig.cs`, 1 new contract test file, living-doc updates

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

| Principle | Assessment |
|-----------|------------|
| I. Code Quality | Small cohesive filter following the existing four filters; XML summary on the class; description text held in constants; no new dependencies; CamelCase method names. PASS |
| II. Testing | Contract test asserts the published contract (externally visible). Documentation gap ⇒ "failing test first" honoured by writing the OpenAPI contract test before the filter/operation edits and seeing it fail. Existing queue contract tests guard unchanged behaviour. PASS |
| III. UX Consistency | Directly delivers "Provide help/usage for … API schemas". No error messages or payloads change. PASS |
| IV. Performance | No hot path; perf note: document-generation-only string/flag assignments. PASS |
| V. Living Documentation | API-surface documentation changes → `docs/architecture.md` (Queue roster read path + "Last reviewed"), `CHANGELOG.md` Unreleased entry, `specs/STATUS.md` row 100; spec Status set to Implemented at the end. PASS |

Post-design re-check (after research/contracts): design adds no project, no route, no runtime behaviour, no persistence; dropping `nullable` on `entries` matches what the service has always returned. PASS.

## Project Structure

### Documentation (this feature)

```text
specs/100-queue-roster-openapi-docs/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── openapi-queue-roster-descriptions.md
├── checklists/requirements.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
src/GameBot.Service/
├── Swagger/QueueRosterSchemaFilter.cs   # NEW: ISchemaFilter for QueueDetailResponse.entries + QueueEntryResponse fields
├── Swagger/SwaggerConfig.cs             # GET /api/queues/{id} description + POST/PUT /entries descriptions
└── GameBotServiceSetup.cs               # register options.SchemaFilter<QueueRosterSchemaFilter>()

tests/contract/Queues/
└── QueueRosterOpenApiTests.cs           # NEW: reads swagger.json, asserts FR-001..FR-006

docs/architecture.md                     # queue roster read path; Last reviewed date
CHANGELOG.md                             # Unreleased entry
specs/STATUS.md                          # 100 row
```

**Structure Decision**: Single existing service project; the filter sits beside `QueueHealthSchemaFilter` in `src/GameBot.Service/Swagger/`, and the test beside `QueueHealthOpenApiTests` in `tests/contract/Queues/`.

## Complexity Tracking

No constitution violations.
