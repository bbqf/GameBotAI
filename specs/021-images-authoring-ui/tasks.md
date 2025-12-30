# Tasks: Images Authoring UI

**Input**: Design documents from `/specs/021-images-authoring-ui/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

## Phase 1: Setup (Shared Infrastructure)

\- [X] T001 Run dotnet restore/build in repo root `C:/src/GameBot`
\- [X] T002 [P] Install frontend dependencies in `src/web-ui/package.json`
\- [X] T003 [P] Ensure `data/images` and trigger JSON fixtures exist in `data/`

---

## Phase 2: Foundational (Blocking Prerequisites)

- [X] T004 Update contracts to include detect endpoint/defaults and maxResults note in `specs/021-images-authoring-ui/contracts/images-api.yaml`
 - [X] T005 [P] Add detection defaults/validation constants (maxResults, threshold, overlap, size/MIME) in `src/GameBot.Service/Endpoints/ImageDetectionsValidation.cs`
 - [X] T006 [P] Define detect request/response DTOs including matches fields and maxResultsHit flag in `src/GameBot.Service/Endpoints/Dto/ImageDetectionsDtos.cs`
 - [X] T007 Add image repository abstraction for binary read/write in `src/GameBot.Domain/Images/ImageRepository.cs`
 - [X] T008 Add image reference repository for trigger lookups in `src/GameBot.Domain/Images/ImageReferenceRepository.cs`
 - [X] T009 Wire repositories and detection services into DI composition in `src/GameBot.Service/Program.cs`
 - [X] T010 Refactor existing image endpoints to use repositories instead of direct store/disk access in `src/GameBot.Service/Endpoints/ImageReferencesEndpoints.cs`

---

## Phase 3: User Story 1 - Browse image IDs (Priority: P1) 🎯 MVP

**Goal**: List image IDs in authoring UI and navigate to detail.
**Independent Test**: From list page, IDs render; clicking navigates to detail for the ID.

### Tests for User Story 1

 - [X] T011 [P] [US1] Contract test for `GET /api/images` returning ID list in `tests/contract/Images/ListImagesTests.cs`
- [X] T012 [P] [US1] Integration/UI test for list-to-detail navigation in `src/web-ui/tests/e2e/images/list-navigation.spec.ts`

### Implementation for User Story 1

 - [X] T013 [P] [US1] Implement `GET /api/images` endpoint returning IDs only in `src/GameBot.Service/Endpoints/ImageReferencesEndpoints.cs`
 - [X] T014 [US1] Build Images list page with table of IDs in `src/web-ui/src/pages/images/ImagesListPage.tsx`
 - [X] T015 [US1] Add list-to-detail routing and empty/error states in `src/web-ui/src/pages/images/ImagesListPage.tsx`

**Checkpoint**: List displays IDs and links to detail.

---

## Phase 4: User Story 2 - View and overwrite image (Priority: P2)

**Goal**: Detail page renders stored image and supports overwrite with validation.
**Independent Test**: Open detail shows image; overwrite replaces content and refreshes preview.

### Tests for User Story 2

 - [ ] T016 [P] [US2] Contract test for `GET /api/images/{id}` returning content-type + body in `tests/contract/Images/GetImageTests.cs`
 - [ ] T017 [P] [US2] Integration test for overwrite flow (POST then PUT then GET) in `tests/integration/Images/OverwriteImageTests.cs`
 - [ ] T034 [P] [US2] Contract test for `GET /api/images/{id}` missing ID returns 404 with friendly body in `tests/contract/Images/GetImageNotFoundTests.cs`
 - [ ] T035 [P] [US2] UI test for not-found state on detail page with link back to list in `src/web-ui/tests/integration/images/detail-not-found.spec.ts`
 - [ ] T036 [P] [US2] UI test for create-and-navigate flow (POST then render detail) in `src/web-ui/tests/integration/images/create-and-navigate.spec.ts`

### Implementation for User Story 2

 - [ ] T018 [US2] Implement `GET /api/images/{id}` to stream stored image with correct content type in `src/GameBot.Service/Endpoints/ImageReferencesEndpoints.cs`
 - [ ] T019 [US2] Implement `POST /api/images` create with ID + file validation (10 MB, png/jpg/jpeg) in `src/GameBot.Service/Endpoints/ImageReferencesEndpoints.cs`
 - [ ] T020 [US2] Implement `PUT /api/images/{id}` overwrite with last-write-wins and metadata update in `src/GameBot.Service/Endpoints/ImageReferencesEndpoints.cs`
 - [ ] T021 [US2] Render detail page with image preview and metadata fetch in `src/web-ui/src/routes/authoring/images/ImageDetailPage.tsx`
 - [ ] T022 [US2] Add upload/overwrite form with validation and post-save preview refresh in `src/web-ui/src/routes/authoring/images/ImageDetailPage.tsx`
 - [ ] T037 [US2] Add create/upload form (ID + file) on list or entry screen posting to `/api/images`, then route to detail on success in `src/web-ui/src/routes/authoring/images/ImagesListPage.tsx`
 - [ ] T038 [US2] Handle missing image 404 response with friendly message and back link in `src/web-ui/src/routes/authoring/images/ImageDetailPage.tsx`

**Checkpoint**: Detail view renders image and supports overwrite with validation.

---

## Phase 5: User Story 3 - Run image detection (Priority: P3)

**Goal**: Trigger detection from detail page, show results table with matches columns and cap note.
**Independent Test**: Detect with defaults posts correctly; table shows matches columns; cap indicator appears when maxResults reached.

### Tests for User Story 3

 - [ ] T023 [P] [US3] Contract test for `POST /api/images/detect` defaults/overrides in `tests/contract/Images/DetectImageTests.cs`
 - [ ] T024 [P] [US3] UI test for detection table columns and cap indicator in `src/web-ui/tests/integration/images/detect.spec.ts`

### Implementation for User Story 3

 - [ ] T025 [US3] Implement `POST /api/images/detect` handler with defaults (maxResults=1, threshold=0.86, overlap=0.1) and maxResultsHit flag in `src/GameBot.Service/Endpoints/ImageDetectionsEndpoints.cs`
 - [ ] T026 [US3] Add detect form with defaults and parameter editing on detail page in `src/web-ui/src/routes/authoring/images/ImageDetailPage.tsx`
 - [ ] T027 [US3] Render detection results table with columns templateId, score, x, y, width, height, overlap and cap note in `src/web-ui/src/routes/authoring/images/ImageDetailPage.tsx`

**Checkpoint**: Detection can be triggered and results displayed with cap indicator.

---

## Phase 6: User Story 4 - Delete unused image (Priority: P4)

**Goal**: Delete unreferenced images; block when triggers reference.
**Independent Test**: Deleting unreferenced succeeds and removes ID; referenced deletion returns conflict with trigger IDs and leaves image intact.

### Tests for User Story 4

 - [ ] T028 [P] [US4] Contract test for `DELETE /api/images/{id}` conflict (409) with blocking triggers in `tests/contract/Images/DeleteImageTests.cs`
 - [ ] T029 [P] [US4] Integration/UI test for delete success vs conflict in `tests/integration/Images/DeleteImageIntegrationTests.cs`

### Implementation for User Story 4

 - [ ] T030 [US4] Enforce trigger reference check and 409 response in `src/GameBot.Service/Endpoints/ImageReferencesEndpoints.cs`
 - [ ] T031 [US4] Ensure trigger reference repository/query supports lookup by imageId in `src/GameBot.Domain/Images/ImageReferenceRepository.cs`
 - [ ] T032 [US4] Add UI delete action with confirmation, conflict messaging, and list refresh in `src/web-ui/src/routes/authoring/images/ImageDetailPage.tsx`

**Checkpoint**: Delete works for unreferenced images and blocks with trigger IDs when referenced.

---

## Final Phase: Polish & Cross-Cutting

 - [ ] T033 [P] Update quickstart with detect UI and delete conflict notes in `specs/021-images-authoring-ui/quickstart.md`
 - [ ] T034 [P] Add logging/metrics for detect/list/detail/delete flows in `src/GameBot.Service/Endpoints/ImageDetectionsMetrics.cs`
 - [ ] T035 Run full build and test suite (`dotnet test -c Debug` and frontend tests) in repo root `C:/src/GameBot`
 - [ ] T039 [P] Align UI/UX error surfaces (validation, detection failure, delete conflict) with authoring patterns in `src/web-ui/src/routes/authoring/images/ImageDetailPage.tsx`
 - [ ] T040 [P] Add perf/latency check for detail preview (≤2s for ≤10 MB) in `tests/integration/Images/DetailPerformanceTests.cs`
 - [ ] T041 [P] Add perf/latency check for overwrite flow (submit to refreshed preview ≤5s) in `tests/integration/Images/OverwritePerformanceTests.cs`
 - [ ] T042 [P] Extend delete conflict test to assert 100% block with trigger IDs (SC-003) in `tests/contract/Images/DeleteImageTests.cs`
 - [ ] T043 [P] Add delete propagation check to ensure ID removed and 404 within 2s in `tests/integration/Images/DeletePropagationTests.cs`

---

## Dependencies & Execution Order

- Phase 1 (Setup) → Phase 2 (Foundational) → User Stories in priority order (US1 P1, US2 P2, US3 P3, US4 P4) → Polish.
- User stories can proceed in parallel after Phase 2 if different files: US1 (list UI/API), US2 (detail/overwrite), US3 (detect), US4 (delete).
- Within each story: tests first, then backend endpoints, then UI.

### Parallel Opportunities
- Setup: T002, T003 parallel after T001.
- Foundational: T005, T006 parallel; T007 then T008.
- Story phases: test tasks (T009/T010, T014/T015, T021/T022, T026/T027) run in parallel; UI vs backend tasks within a story that touch separate files marked [P].

### MVP Scope
- Deliver US1 (list and navigation) after Phases 1-2 for the smallest viable increment.

### Task Counts
 - Total tasks: 45
 - US1 tasks: 5
 - US2 tasks: 13
 - US3 tasks: 5
 - US4 tasks: 5
 - Setup/Foundational/Polish tasks: 17

All tasks follow the required checklist format.
