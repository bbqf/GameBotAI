# Tasks: Honour dryRun on sequence updates and reject unresolvable command references

**Feature**: 091-fix-sequence-put-dryrun | **Issue**: [#177](https://github.com/bbqf/GameBotAI/issues/177) (B-008)
**Input**: [spec.md](./spec.md), [plan.md](./plan.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/sequence-writes.md](./contracts/sequence-writes.md), [quickstart.md](./quickstart.md)

**Tests are mandatory here**: FR-010 requires them, and the constitution (Principle II) requires a
bug fix to include a failing test reproducing the issue before the fix. Phase 2 is that gate.

All paths are relative to `C:\src\GameBot`. Contract tests follow the existing pattern in
`tests/contract/Sequences/SequencePerStepConditionsContractTests.cs` (a `WebApplicationFactory<Program>`
with `GAMEBOT_USE_ADB=false`, `GAMEBOT_DYNAMIC_PORT=true`, `GAMEBOT_AUTH_TOKEN=test-token`, a clean
data dir, and a `Bearer test-token` client); commands are seeded through
`app.Services.GetRequiredService<ICommandRepository>().AddAsync(new Command { Id, Name })`.

**Total**: 26 tasks across 7 phases.

---

## Phase 1: Setup & baseline

**Goal**: know what "green" means before changing anything.

- [ ] T001 Record the pre-change baseline: run `dotnet test "C:\src\GameBot\GameBot.sln" -c Debug --nologo` and note the total/passed/failed counts per test project, so a later regression is attributable

**Checkpoint**: baseline counts written down.

---

## Phase 2: Foundational — failing reproductions (BLOCKING)

**Goal**: reproduce both defects from the outside before touching production code. Every test below
MUST fail against the unmodified build; T006 confirms it.

- [ ] T002 [P] Create `tests/contract/Sequences/SequenceUpdateDryRunContractTests.cs` with `PutWithDryRunDoesNotPersistAndReturnsEnvelope`: create a one-step tap sequence (version 1), `PUT` a valid, different body (two tap steps, new name) with `dryRun = true`; assert `200`, `valid == true`, `dryRun == true`, empty `errors`; then `GET` and assert `version == 1`, original name and a single step (FR-001, FR-003, SC-001)
- [ ] T003 [P] In `tests/contract/Sequences/SequenceUpdateDryRunContractTests.cs` add `PatchWithDryRunDoesNotPersistAndReturnsEnvelope`: same as T002 via `PATCH` (FR-001, spec US1 scenario 5)
- [ ] T004 [P] Create `tests/contract/Sequences/SequenceCommandReferenceExistenceContractTests.cs` with `CreateWithNonexistentCommandIdIsRejectedAndNothingStored`: `POST` a per-step body whose command step payload `commandId = "does-not-exist"`; assert `400`, an `errors` entry containing both `does-not-exist` and the step id, and that `GET /api/sequences` does not list the name (FR-005, FR-008)
- [ ] T005 [P] In `tests/contract/Sequences/SequenceCommandReferenceExistenceContractTests.cs` add `PutWithDryRunAndNonexistentCommandIdIsRejectedAndNothingChanges` — the issue's exact reproduction: create a tap sequence (version 1), `PUT` with `dryRun = true` and a command step on `does-not-exist`; assert `400` with the command-reference error, then `GET` shows `version == 1` and the original tap step (FR-002, FR-005, FR-007)
- [ ] T006 Run `dotnet test "C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj" -c Debug --filter "FullyQualifiedName~SequenceUpdateDryRunContractTests|FullyQualifiedName~SequenceCommandReferenceExistenceContractTests"` and confirm T002–T005 are all **red** against the unmodified build; record the failure reasons (expected: PUT/PATCH return the sequence body and version becomes 2; POST returns 201)

**Checkpoint**: both defects reproduced. Implementation unblocked.

---

## Phase 3: User Story 1 (P1) — validate an update without changing the live sequence

**Goal**: FR-001..FR-004. **Independent test**: T002/T003 turn green; T007–T009 pass; no existing test regresses.

- [ ] T007 [P] [US1] In `tests/contract/Sequences/SequenceUpdateDryRunContractTests.cs` add `PutWithDryRunAndInvalidBodyFailsLikeRealPutAndChangesNothing`: a nested-loop body (as in `tests/integration/Sequences/SequenceCreateDryRunIntegrationTests.cs`) sent via `PUT` with `dryRun = true` and again without; assert both `400` with identical `errors` JSON, and `GET` still version 1 (FR-002, SC-002)
- [ ] T008 [P] [US1] In `tests/contract/Sequences/SequenceUpdateDryRunContractTests.cs` add `PutWithDryRunAndStaleVersionReturnsConflictAndChangesNothing` (real PUT first to reach version 2, then dry-run PUT with `version = 1` → `409`, still version 2) and `PutWithDryRunOnUnknownSequenceReturnsNotFound` (FR-002)
- [ ] T009 [P] [US1] In `tests/contract/Sequences/SequenceUpdateDryRunContractTests.cs` add `PutWithoutDryRunOrWithDryRunFalseStillPersists` (both variants bump version and change content) and `PutWithDryRunAndLegacyStringStepsBodyDoesNotPersist` (body `{ name, steps: ["x"], dryRun: true }` → `200` envelope, version unchanged) and `PutWithNonBooleanDryRunOnPerStepBodyIsRejectedAsMalformed` (per-step body with `dryRun = "yes"` → `400`, version unchanged, matching create) (FR-004, spec Edge Cases)
- [ ] T010 [US1] In `src/GameBot.Service/Endpoints/SequencesEndpoints.cs` add a private static `IsDryRunRequested(JsonElement root)` returning true only when `root` is an object whose `dryRun` property has `JsonValueKind.True`, with an XML doc comment citing feature 091 / issue #177 and explaining why the raw root is read (shape-independent)
- [ ] T011 [US1] In `UpdateSequenceAsync` in `src/GameBot.Service/Endpoints/SequencesEndpoints.cs`, immediately before `existing.Version += 1`, return `Results.Ok(new { valid = true, dryRun = true, errors = Array.Empty<string>() })` when `IsDryRunRequested(root)`; comment that every failure return precedes this line so a dry run fails exactly like a real write, and that `GetAsync` returns a private copy so the discarded mutations never reach storage (research R-001, R-002)
- [ ] T012 [US1] Apply the same short-circuit in `PatchSequenceAsync` in `src/GameBot.Service/Endpoints/SequencesEndpoints.cs`, placed after the top-level `watchdogTimeoutMs` handling and before `existing.Version += 1`
- [ ] T013 [US1] Run the contract tests in `SequenceUpdateDryRunContractTests` and confirm T002, T003, T007, T008, T009 are green

**Checkpoint**: dry-run updates are safe (MVP for the most dangerous half of the issue).

---

## Phase 4: User Story 2 (P1) — a mistyped command reference is caught at write time

**Goal**: FR-005..FR-008. The tolerated-id rule (FR-006) is implemented here, not deferred to US3,
because the existing deleted-command round-trip test would otherwise break between phases.
**Independent test**: T004/T005 turn green; T014/T015 pass.

- [ ] T014 [P] [US2] In `tests/contract/Sequences/SequenceCommandReferenceExistenceContractTests.cs` add `PutAndPatchWithNonexistentCommandIdAreRejectedAndNothingChanges` (real writes, no dry run) and `NonexistentCommandIdNestedInLoopBodyIfBodyAndElseBodyIsRejected` (one theory/fact per position, POST) and `SameMissingIdUsedByTwoStepsProducesOneErrorListingBoth`, and `CommandStepWithExistingCommandIsAccepted` (seeded command, POST then PUT) and `CommandIdDifferingOnlyInCaseFromAnExistingCommandIsAccepted` (seed `Cmd-Upper`, reference `cmd-upper`) (FR-005, spec Edge Cases, FR-008, spec US2 scenarios 3, 5, 6)
- [ ] T015 [P] [US2] In `tests/integration/Sequences/SequenceCreateDryRunIntegrationTests.cs` add `DryRunCreateWithNonexistentCommandIdFailsSameAsRealCreate`: a command step with payload `commandId = "does-not-exist"` under `dryRun = true` returns `400` with the same `errors` as a real create of the body, and nothing is stored (FR-007, spec US2 scenario 2)
- [ ] T016 [US2] In `src/GameBot.Service/Endpoints/SequencesEndpoints.cs` change `EnrichCommandReferencesAsync` to return the `IReadOnlyDictionary<string, string>` command lookup it builds, and add `CollectReferencedCommandIds(IEnumerable<SequenceStep>)` returning a case-insensitive `HashSet<string>` of explicit payload `commandId`s on command steps at any depth (via `FlattenSequenceSteps`, which walks `Body` and `ElseBody`) (research R-003, R-005)
- [ ] T017 [US2] In `src/GameBot.Service/Endpoints/SequencesEndpoints.cs` add `ValidateCommandReferencesExist(IReadOnlyList<SequenceStep> steps, IReadOnlyDictionary<string, string> commandLookup, IReadOnlySet<string> toleratedIds)`: over `FlattenSequenceSteps(steps)`, consider only steps where `IsCommandBackedStep` holds **and** `Action.Parameters["commandId"]` is a non-empty value (so the B-003 no-`commandId` case is not double-reported, research R-007); group ids absent from both lookup and tolerated set by id (case-insensitive) and emit `Command reference '{id}' does not exist (used by: {distinct step ids}).`, ordered by id (research R-006); XML doc citing FR-005/FR-006
- [ ] T018 [US2] In `src/GameBot.Service/Endpoints/SequencesEndpoints.cs` extend `ValidatePerStepForPersistenceAsync` with `commandLookup` and `toleratedIds` parameters and append `ValidateCommandReferencesExist` errors; update its three callers: `CreateSequenceAsync` passes an empty tolerated set; `UpdateSequenceAsync` and `PatchSequenceAsync` compute `CollectReferencedCommandIds(existing.Steps)` **before** `existing.SetSteps(linearSteps)` and pass it
- [ ] T019 [US2] Run `SequenceCommandReferenceExistenceContractTests` and `SequenceCreateDryRunIntegrationTests` and confirm T004, T005, T014, T015 are green

**Checkpoint**: no new unresolvable reference can be written through the API.

---

## Phase 5: User Story 3 (P2) — re-saving a sequence whose command was later deleted still works

**Goal**: FR-006 verified on both update routes. **Independent test**: T020 passes and `SequenceMissingCommandReferenceIntegrationTests` passes unchanged.

- [ ] T020 [US3] In `tests/contract/Sequences/SequenceCommandReferenceExistenceContractTests.cs` add `PutCarryingAlreadyStoredUnresolvedCommandIdIsAccepted` (seed `c1`, create sequence on `c1`, delete `c1`, `PUT` the same body → `200`, read-back `isResolved == false` with the snapshot name) and `PutIntroducingDifferentNonexistentCommandIdIsRejectedEvenWhenAnotherIsTolerated` (same sequence, add a second step on `does-not-exist` → `400` naming only `does-not-exist`) (spec US3 scenarios 1, 2; SC-004)
- [ ] T021 [US3] Run `dotnet test "C:\src\GameBot\tests\integration\GameBot.IntegrationTests.csproj" -c Debug --filter "FullyQualifiedName~SequenceMissingCommandReference"` plus T020 and confirm both green with the existing test file unmodified

**Checkpoint**: the feature-041 unresolved-command flow is intact.

---

## Phase 6: Defect-dependent fixtures and API documentation

**Goal**: the rest of the suite stays green without weakening any assertion; FR-009.

- [ ] T022 Correct fixtures that create sequences referencing commands that were never created, by seeding those commands through `ICommandRepository` before the write (research R-005): `tests/contract/Sequences/IfStepContractTests.cs` (`cmd-close`, `cmd-continue`), `tests/contract/Sequences/SequencePerStepConditionsContractTests.cs` (`cmd-mail`), `tests/contract/PrimitiveActionContractsTests.cs` (`child-command`), `tests/integration/PrimitiveAuthoringFlowTests.cs` (`nested-command`), `tests/integration/Sequences/PerStepConditionAuthoringRoundTripIntegrationTests.cs` (`cmd-mail`, `cmd-rewards`), `tests/integration/Sequences/WaitForImageSequenceExecutionIntegrationTests.cs` (`cmd-after-wait`); for `tests/integration/Sequences/SequenceExecuteDryRunIntegrationTests.cs` (`stale-cmd`, a test *about* a dangling reference) create the command, create the sequence, then delete the command; then run the full solution and fix any further failure of the same kind the same way — never by loosening an assertion
- [ ] T023 [P] In `src/GameBot.Service/Swagger/SwaggerConfig.cs` add a documented `bool? DryRun` to `SequenceRequestSchema`, and set `operation.Description` on the POST `/api/sequences`, PUT and PATCH `/api/sequences/{sequenceId}` branches of `ApplySequenceExamples`, stating: `dryRun: true` validates without persisting (on POST only for the per-step body shape; on PUT/PATCH for every body shape) and returns `200 { valid: true, dryRun: true, errors: [] }` (a failing dry run returns the same error a real write would); a command step whose `commandId` names no existing command is rejected with `400` (on update, ids already referenced by the stored sequence are tolerated) (FR-009)
- [ ] T024 [P] Create `tests/contract/Sequences/SequenceWritesOpenApiTests.cs` asserting from `/swagger/v1/swagger.json` that the `put` and `patch` operations on `/api/sequences/{sequenceId}` and the `post` on `/api/sequences` have a `description` containing `dryRun`, and the `put` description mentions that nonexistent command references are rejected (FR-009)

---

## Phase 7: Polish & living documentation

- [ ] T025 Update `docs/architecture.md`: in "Dry-run / validate-only sequence mode" add `PUT`/`PATCH /api/sequences/{id}` (validate-only, all body shapes, same envelope/failures) and a bullet that create/update/patch reject nonexistent command references with the carried-over tolerance; add a "Feature 091 fixed, tightening only" entry to the REST API surface feature list; refresh the `_Last reviewed:` line to `2026-09-16 (feature 091 sequence update dry run and command-reference existence)`. Set `specs/082-dry-run-sequences/spec.md` `**Status**:` to `Implemented (iterated by 091)`; in `specs/STATUS.md` update row 082 to match and add `| 091 | Sequence Update Dry Run and Command Reference Existence | Implemented |`; set `specs/091-fix-sequence-put-dryrun/spec.md` Status to `Implemented`
- [ ] T026 Run the full suite `dotnet test "C:\src\GameBot\GameBot.sln" -c Debug --nologo` and confirm zero failures and that the pass count equals the T001 baseline plus the new tests; build with zero new warnings (`dotnet build "C:\src\GameBot\GameBot.sln" -c Debug`)

---

## Dependencies

```text
T001 → T002..T005 [P] → T006 (red gate)
T006 → US1 (T007..T013)
T006 → US2 (T014..T019)          US1 and US2 touch different handler lines but the same file: run sequentially
US2 → US3 (T020..T021)           tolerance is implemented in T016..T018
US2 → T022                       fixtures only break once the existence check exists
T023, T024 [P] after T011/T012
T022..T024 → T025 → T026
```

**Red window**: between T018 and T022 the full suite is expected to be red, because the fixtures
listed in T022 rely on the defect. Only targeted runs (T019, T021) are gates inside that window; no
commit is made before T026 confirms the whole suite green.

## Parallel execution examples

- Phase 2: T002, T003, T004, T005 (two new test files, independent facts).
- US1: T007, T008, T009 together (same new test file, independent facts written in one pass).
- US2: T014 and T015 together (different files).
- Phase 6: T023 and T024 together.

## Implementation strategy

1. **MVP** = Phases 1–3: dry-run updates can no longer mutate a live sequence — the most harmful half
   of the issue.
2. Phase 4–5: write-time existence check with the deleted-command carve-out.
3. Phase 6–7: fixture corrections, OpenAPI, living docs, full green run.
