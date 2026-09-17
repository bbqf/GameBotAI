# Research: Make a Queue's Live Roster Discoverable

All Technical Context items were known; no NEEDS CLARIFICATION remained. The decisions below record what was verified in code (FR-009) and why the design is shaped this way.

## R1 — What `entries` actually is (FR-001, FR-009)

- **Decision**: Describe `entries` as the queue's own current roster, in run order, always an array.
- **Rationale**: `QueuesEndpoints.BuildDetailAsync` fills `detail.Entries` from `IQueueRuntimeStore.GetEntries(queue.Id)` — the queue's runtime entries — in store order, not from the linked template. `QueueDetailResponse.Entries` is a get-only `Collection<QueueEntryResponse>` initialised to an empty collection, so it is never null. `EntryCount` on the same response is `entries.Count`.
- **Alternatives considered**: Describing it as "the template's entries" — wrong; a running queue keeps its start-time runtime entries even after a template edit.

## R2 — Entry field meanings (FR-003, FR-009)

- **Decision**: `entryId` = identifier of this roster entry, used by `DELETE /api/queues/{id}/entries/{entryId}`; `sequenceId` = the sequence the entry runs; `sequenceName` = that sequence's current name, resolved from the sequence store at read time, null when it no longer exists; `stale` = true when the referenced sequence no longer exists.
- **Rationale**: `ProjectEntry(entry, found ? name : null)` sets `SequenceName` from a lookup over `ISequenceRepository.ListAsync()` and `Stale` from the same miss; the delete route is `MapDelete("{id}/entries/{entryId}")`. `QueueEntryResponse` is also the 201 body of `POST /api/queues/{id}/entries`, so the wording must not assume the detail-response context.
- **Alternatives considered**: Describing only inside `entries` (array description) — the per-field meanings would then be missing from the append response.

## R3 — How to attach schema descriptions (FR-001..FR-003)

- **Decision**: New `QueueRosterSchemaFilter : ISchemaFilter`, matching `context.Type` against `QueueDetailResponse` and `QueueEntryResponse`, registered next to the existing filters in `GameBotServiceSetup.cs`.
- **Rationale**: Swagger here does not read XML comments; the four existing schema filters establish the pattern. `entries` is published inline as an array schema (`type: array`, `items: $ref`), so setting `Description` and `Nullable = false` on that property schema is serialised (no `$ref` sibling-dropping issue). `ReadOnly` is left untouched (clarification Q4).
- **Alternatives considered**: Enabling XML comments for Swagger — broad change affecting every schema, out of scope. Adding a `[Description]`/`[SwaggerSchema]` attribute — would need Swashbuckle.Annotations, a new dependency.

## R4 — How to attach operation descriptions (FR-004, FR-005)

- **Decision**: Edit the existing queue branches in `SwaggerConfig.cs`: `POST …/entries` and `PUT …/entries` get `operation.Description ??= <pointer text>`; the `GET /api/queues/{id}` branch's description gains a leading roster sentence while the existing health sentences stay unchanged.
- **Rationale**: Queue operation summaries and examples already live there, set with `??=`; putting the text anywhere else would split one operation's docs across two places. `QueueHealthOpenApiTests.GetQueueOperationPointsAtPauseKind` already pins the health text on the same description and keeps guarding it.
- **Alternatives considered**: `.WithDescription(...)` on the minimal-API route in `QueuesEndpoints.cs` — mixes documentation into endpoint wiring and bypasses the `??=` convention used for every other queue operation.

## R7 — The `GET /api/queues/{id}` branch also matched `/monitor` (found during implementation)

- **Decision**: Narrow the `GET /api/queues/{id}` branch in `ApplyQueueExamples` to the exact path (`IsPath(path, ApiRoutes.Queues + "/{id}")`).
- **Rationale**: The branch used `path.StartsWith(ApiRoutes.Queues + "/")`, so `GET /api/queues/{id}/monitor` (which has no branch of its own) was published with the single-queue summary, health description, `QueueDetailResponse` schema and example. Adding the roster sentence there would have told readers the monitor is "the way to read the roster" (violating FR-009). With the exact match, the monitor operation publishes no summary/description/example instead of wrong ones; its runtime response is unchanged. No test pinned the monitor's documentation.
- **Alternatives considered**: Documenting the monitor operation properly (its own summary, schema, example) — correct but beyond issue #179; left as a follow-up.

## R5 — `required` on `entries`

- **Decision**: Do not add a `required` list (clarification Q2).
- **Rationale**: No `Queue*` schema declares required properties (not even `id`); the "always present, never null" statement goes in the description.

## R6 — Test approach (FR-007)

- **Decision**: `tests/contract/Queues/QueueRosterOpenApiTests.cs`, same shape as `QueueHealthOpenApiTests` (factory with `GAMEBOT_USE_ADB=false`, dynamic port, auth token; read `/swagger/v1/swagger.json`), asserting key phrases rather than whole strings so wording can be polished without breaking tests, plus: `entries` has no `nullable: true`, `readOnly` is still true, and the `GET /api/queues/{id}` 200 example contains an `entries` array.
- **Rationale**: Matches the existing OpenAPI contract tests; key-phrase assertions pin meaning (FR-007) without making copy edits brittle.
