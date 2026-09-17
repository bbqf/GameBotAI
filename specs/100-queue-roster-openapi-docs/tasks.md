---
description: "Task list for making a queue's live roster discoverable from the OpenAPI document"
---

# Tasks: Make a Queue's Live Roster Discoverable

**Input**: Design documents from `specs/100-queue-roster-openapi-docs/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/openapi-queue-roster-descriptions.md, quickstart.md

**Tests**: Requested by the spec (FR-007, SC-003). The contract test is written first and must fail before the filter and operation descriptions exist.

**Organization**: Tasks are grouped by user story.

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Setup

- [ ] T001 Re-verify the facts in specs/100-queue-roster-openapi-docs/research.md R1–R2 against src/GameBot.Service/Endpoints/QueuesEndpoints.cs (`BuildDetailAsync` reads `runtime.GetEntries`, `ProjectEntry` sets `SequenceName`/`Stale`, `MapDelete("{id}/entries/{entryId}")`) and src/GameBot.Service/Contracts/Queues/QueueDetailResponse.cs (`Entries` get-only, initialised non-null); correct data-model.md and contracts/openapi-queue-roster-descriptions.md if anything differs (FR-009)

## Phase 2: Foundational

No blocking prerequisites: `QueueDetailResponse`, `QueueEntryResponse` and the queue operation branches in src/GameBot.Service/Swagger/SwaggerConfig.cs (`ApplyQueueExamples`) already exist.

---

## Phase 3: User Story 1 - Find how to read a queue's roster from the published document (Priority: P1) 🎯 MVP

**Goal**: The published OpenAPI document describes `QueueDetailResponse.entries` (not nullable, still read-only) and the four `QueueEntryResponse` fields, and the `GET /api/queues/{id}`, `POST /api/queues/{id}/entries` and `PUT /api/queues/{id}/entries` operations say where the roster is read.

**Independent Test**: `GET /swagger/v1/swagger.json` satisfies every bullet in contracts/openapi-queue-roster-descriptions.md.

### Tests for User Story 1

- [ ] T002 [US1] Create tests/contract/Queues/QueueRosterOpenApiTests.cs (namespace `GameBot.ContractTests.Queues`, same `WebApplicationFactory<Program>` factory, env vars and `ReadDocumentAsync` helper shape as tests/contract/Queues/QueueHealthOpenApiTests.cs, class XML summary citing feature 100 / issue #179) with facts using the contract's test key phrases: (a) `EntriesDescribesTheLiveRoster` — `components.schemas.QueueDetailResponse.properties.entries.description` contains `roster`, `never null`, `template` and `GET /api/queues/{id}`; (b) `EntriesIsNotNullableButStaysReadOnly` — `entries` has no `nullable` property or it is `false`, and `readOnly` is `true`; (c) `EntryFieldsAreDescribed` — `QueueEntryResponse.properties` `entryId` description contains `DELETE /api/queues/{id}/entries/{entryId}`, `sequenceId` contains `sequence this entry runs`, `sequenceName` contains both `null` and `no longer exists`, `stale` contains `no longer exists`; (d) `GetQueueOperationDescribesRoster` — `paths./api/queues/{id}.get.description` contains `entries` and `roster` and still contains `pauseKind`; (e) `EntriesWriteOperationsPointToReadPath` — a `[Theory]` over `post` and `put` asserting `paths./api/queues/{id}/entries.<method>.description` contains `GET /api/queues/{id}` and `entries`; (f) `GetQueueExampleShowsEntries` — `paths./api/queues/{id}.get.responses.200.content.application/json.example.entries` is a JSON array. Run `dotnet test C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj --filter FullyQualifiedName~QueueRosterOpenApiTests` and confirm (a)–(e) FAIL and (f) passes before T003

### Implementation for User Story 1

- [ ] T003 [US1] Create src/GameBot.Service/Swagger/QueueRosterSchemaFilter.cs: `internal sealed class QueueRosterSchemaFilter : ISchemaFilter` (XML summary citing feature 100 / issue #179 and that the service does not feed XML comments to Swagger), with `private const string` descriptions `EntriesDescription`, `EntryIdDescription`, `SequenceIdDescription`, `SequenceNameDescription`, `StaleDescription` whose wording conveys every point in contracts/openapi-queue-roster-descriptions.md (entries: current roster in run order; always an array, `[]` when empty, never null; the queue's own entries, which can differ from its linked template's — a running queue keeps the entries it started with, template entries are read from GET /api/queue-templates/{id}; read the roster here from GET /api/queues/{id}, there is no GET /api/queues/{id}/entries). `Apply`: when `context.Type == typeof(QueueDetailResponse)` and `entries` exists, set its `Description` and `Nullable = false` (leave `ReadOnly` untouched); when `context.Type == typeof(QueueEntryResponse)`, describe the four properties via a `Describe` helper shaped like the one in src/GameBot.Service/Swagger/QueueHealthSchemaFilter.cs. The entry descriptions must read correctly on their own, because `QueueEntryResponse` is also the 201 body of `POST /api/queues/{id}/entries`: they must not refer to "the detail response" or to `entries`
- [ ] T004 [US1] Register `options.SchemaFilter<QueueRosterSchemaFilter>();` after the other schema filters in src/GameBot.Service/GameBotServiceSetup.cs
- [ ] T005 [US1] In src/GameBot.Service/Swagger/SwaggerConfig.cs `ApplyQueueExamples`: (1) in the `POST … /entries` branch add `operation.Description ??= QueueRosterReadPointer;` and in the `PUT … /entries` branch the same; define `private const string QueueRosterReadPointer` near the queue example helpers, stating that the roster is read from `GET /api/queues/{id}` (its `entries` field) and that there is no `GET /api/queues/{id}/entries`; (2) in the `GET /api/queues/{id}` branch prepend a sentence to the existing `operation.Description ??=` text saying the response's `entries` is the queue's current roster in run order (its own entries, not its linked template's) and is the way to read it — keep every existing health sentence unchanged; (3) confirm by reading the branch conditions that the GET branch applies only to `/api/queues/{id}` (monitor/cycles branches set their own descriptions first)
- [ ] T006 [US1] Build and run `dotnet test C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj --filter "FullyQualifiedName~QueueRosterOpenApiTests|FullyQualifiedName~QueueHealthOpenApiTests"`; all must pass. Fix wording (not assertions) until green

**Checkpoint**: The roster read path is discoverable from the published document.

---

## Phase 4: User Story 2 - The roster documentation cannot silently regress (Priority: P2)

**Goal**: Prove the tests guard the documentation, and that behaviour did not change.

**Independent Test**: Removing the filter registration or the operation descriptions makes QueueRosterOpenApiTests fail; existing queue API contract tests pass unchanged.

- [ ] T007 [US2] Temporarily comment out the T004 registration in src/GameBot.Service/GameBotServiceSetup.cs, run QueueRosterOpenApiTests and confirm (a)–(c) fail (SC-003); restore it, re-run and confirm green; `git diff src/GameBot.Service/GameBotServiceSetup.cs` shows only the T004 line added
- [ ] T008 [US2] After T007's restore is confirmed green (not in parallel with T007: it builds the same service, and contract tests share one bin data directory), run the unchanged-behaviour guards with no test edits: `dotnet test C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj --filter "FullyQualifiedName~QueuesApiContractTests|FullyQualifiedName~OpenApiContractTests|FullyQualifiedName~SwaggerDocsTests"` (FR-008, SC-004)

**Checkpoint**: Both stories verified.

---

## Phase 5: Polish & Cross-Cutting Concerns

- [ ] T009 [P] Update docs/architecture.md: after the **Queue Template** bullet in the domain model section, add a short note that a queue's live roster is read from `GET /api/queues/{id}` `entries` (the queue's own runtime entries, which can differ from its template's; no `GET /api/queues/{id}/entries`), documented in OpenAPI by `QueueRosterSchemaFilter` (feature 100, issue #179); set the "Last reviewed" line to `2026-09-17 (feature 100 queue roster documented in OpenAPI)`
- [ ] T010 [P] Add an Unreleased → Added entry at the top of CHANGELOG.md in the style of the 099 entry: queue roster documented in the OpenAPI document (100-queue-roster-openapi-docs, #179) — `entries` described and no longer published as nullable, entry fields described, `/entries` write operations point to `GET /api/queues/{id}`; documentation only, responses unchanged. Edit with the Edit tool only (the file already contains non-ASCII text)
- [ ] T011 [P] Add a `| 100 | Make a Queue's Live Roster Discoverable | Implemented |` row after the 099 row in specs/STATUS.md and set `**Status**: Implemented` in specs/100-queue-roster-openapi-docs/spec.md
- [ ] T012 Full gate (constitution quality gates): `dotnet build C:\src\GameBot\GameBot.sln` — the build runs the project's analyzers, so confirm the output reports no warnings or errors in QueueRosterSchemaFilter.cs, SwaggerConfig.cs, GameBotServiceSetup.cs or QueueRosterOpenApiTests.cs — then the full contract test project `dotnet test C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj` (rerun once if a known flaky test such as QueueTemplateLink fails); all green before commit

---

## Dependencies & Execution Order

- T001 → T002 → T003 → T004 → T005 → T006 (US1, sequential: test-first; T003–T005 touch different files but all must land before T006)
- US2 (T007 → T008) depends on US1 complete; T008 strictly after T007's restore (shared build output and test data directory)
- Polish (T009–T011) can run in parallel after T006; T012 last

### Parallel Opportunities

```text
After T006:  T009, T010, T011 (documentation files only; no builds or test runs)
```

## Implementation Strategy

- **MVP**: Phase 3 (US1) — the issue is resolved once the roster documentation is published and asserted.
- **Then**: Phase 4 guards, Phase 5 living docs and full gate.
