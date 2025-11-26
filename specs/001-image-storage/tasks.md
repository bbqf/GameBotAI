# Tasks: Disk-backed Reference Image Storage (Status Updated)

## Phase 1: Setup

- [x] T001 Ensure `data/images` directory exists in repo root
- [x] T002 Configure service to create `data/images` at startup in `src/GameBot.Service/Program.cs`

## Phase 2: Foundational

- [x] T003 Define ID validation helper in `src/GameBot.Domain/Triggers/Evaluators/ReferenceImageIdValidator.cs`
- [x] T004 Implement atomic file write utility in `src/GameBot.Domain/Logging/AtomicFileWriter.cs`
- [x] T005 Wire disk-backed store interface `IReferenceImageStore` in `src/GameBot.Domain/Triggers/Evaluators/IReferenceImageStore.cs`

## Phase 3: User Story 1 (Persist uploaded reference images)

- [x] T006 [US1] Implement disk-backed `ReferenceImageStore` in `src/GameBot.Domain/Triggers/Evaluators/ReferenceImageStore.cs`
- [x] T007 [US1] Extend `/images` POST in `src/GameBot.Service/Endpoints/ImageReferencesEndpoints.cs` to write files under `data/images` (added physical fallback)
- [x] T008 [US1] Register disk store in DI in `src/GameBot.Service/Program.cs`
- [x] T009 [US1] Integration test: persistence across restart in `tests/integration/ImageStorePersistenceTests.cs`

## Phase 4: User Story 2 (Resolve images by ID)

- [x] T010 [US2] Update image-match evaluator to use disk-backed store in `src/GameBot.Domain/Triggers/Evaluators/ImageMatchEvaluator.cs` (DI swap suffices)
- [x] T011 [US2] Unit test: evaluator resolves by id in `tests/unit/ImageStoreEvaluatorTests.cs`

## Phase 5: User Story 3 (Controlled overwrite & cleanup)

- [x] T012 [US3] Overwrite support in POST `/images` for same `id` in `src/GameBot.Service/Endpoints/ImageReferencesEndpoints.cs` (atomic replace implemented; unit test added)
- [x] T013 [US3] Implement `DELETE /images/{id}` in `src/GameBot.Service/Endpoints/ImageReferencesEndpoints.cs`
- [x] T014 [US3] Unit test: overwrite and delete in `tests/unit/ImageStoreDeleteTests.cs`

## Final Phase: Polish & Cross-Cutting

- [ ] T015 Validate error messages and status codes in `src/GameBot.Service/Endpoints/ImageReferencesEndpoints.cs`
- [ ] T016 Add logging of operations and failures in `src/GameBot.Domain/Logging` (basic persistence logging pending)
- [x] T017 Update `README.md` and `CHANGELOG.md` entries

## Dependencies

- US1 → US2 → US3

## Parallel Execution Examples

- [x] T006 [P] [US1] Implement ReferenceImageStore while T007 proceeds in parallel (different files)
- [x] T011 [P] [US2] Write evaluator unit test while T010 is implemented
- [x] T014 [P] [US3] Delete tests can be scaffolded while endpoint code is finalized

## MVP Scope

- User Story 1: Upload and persistence across restart (`POST /images`, startup load, `GET /images/{id}`)