# Tasks: Emulator Screenshot Cropping

## Dependencies
- US1 → US2 (naming/storage depends on capture/crop existing)
- US1 → US3 (error handling builds on capture flow)

## Parallel Execution Examples
- Backend API stub for screenshot capture can proceed in parallel with frontend crop UI scaffolding.
- Duplicate-name handling in backend can proceed in parallel with UI naming prompt and overwrite dialog.

## Implementation Strategy
- Deliver MVP with US1 (capture + rectangle crop + save PNG) end-to-end first, then layer naming/overwrite (US2) and robust error/retry handling (US3).

## Phase 1: Setup
- [X] T001 Ensure repo dependencies are installed (dotnet restore at repo root; npm install in src/web-ui)
- [X] T002 Verify emulator capture prerequisites available (ADB/emulator reachable) and sample images present in tests/TestAssets

## Phase 2: Foundational
- [X] T003 Add/update test asset images for crop tests in tests/TestAssets
- [X] T004 Wire shared paths/config constants for image storage (data/images) in src/GameBot.Domain or Service config
- [X] T005 Add capture→crop→save p95 ≤1s benchmark using 1080p sample in tests/perf or tests/integration
- [X] T006 Wire metrics/logging to record capture→crop→save duration and 1px accuracy checks in src/GameBot.Service

## Phase 3: User Story 1 - Capture and crop screenshot (P1)
- [ ] T007 [US1] Add screenshot capture endpoint or handler to emit PNG from emulator in src/GameBot.Service
- [ ] T008 [US1] Implement capture session model handling temp screenshot references in src/GameBot.Domain or Service
- [ ] T009 [US1] Implement crop operation applying bounds with min size 16x16 and 1px accuracy in src/GameBot.Service
- [ ] T010 [US1] Persist cropped PNG to data/images with path return in src/GameBot.Service
- [ ] T011 [US1] Frontend: add capture trigger and rectangle draw/resize UI in src/web-ui (React)
- [ ] T012 [US1] Frontend: show preview of cropped area and save action confirmation in src/web-ui
- [ ] T013 [US1] Tests: backend unit/integration for capture+crop pipeline in tests/unit and tests/integration
- [ ] T014 [US1] Tests: frontend component/integration tests for rectangle selection and save preview in src/web-ui/tests

## Phase 4: User Story 2 - Name and store cropped image (P2)
- [ ] T015 [US2] Backend: enforce unique names with overwrite flag on /images/crop in src/GameBot.Service
- [ ] T016 [US2] Backend: include storage path and filename in response payload in src/GameBot.Service
- [ ] T017 [US2] Frontend: add name input and overwrite prompt/flow in src/web-ui
- [ ] T018 [US2] Tests: backend duplicate-name conflict tests (409 vs overwrite) in tests/integration
- [ ] T019 [US2] Tests: frontend naming + overwrite UX tests in src/web-ui/tests
- [ ] T020 [US2] Add UI validation that users see/understand the save location after save in src/web-ui/tests

## Phase 5: User Story 3 - Handle failed or invalid selections (P3)
- [ ] T021 [US3] Backend: validate bounds against capture dimensions; return actionable errors in src/GameBot.Service
- [ ] T022 [US3] Backend: retry-friendly errors for missing capture/emulator not ready in src/GameBot.Service
- [ ] T023 [US3] Frontend: surface validation errors inline and keep capture for retry in src/web-ui
- [ ] T024 [US3] Tests: backend error-path coverage (invalid bounds, missing capture) in tests/integration
- [ ] T025 [US3] Tests: frontend retry/validation messaging tests in src/web-ui/tests

## Final Phase: Polish & Cross-Cutting
- [ ] T026 Add logging/metrics hooks for capture→crop duration and error rates in src/GameBot.Service
- [ ] T027 Add coverage gate/check (≥80% line / ≥70% branch for touched areas) to backend test run/CI in .github/workflows/ci.yml (add if missing)
- [ ] T028 Add coverage/assertion step for frontend tests to guard touched UI components in .github/workflows/ci.yml and src/web-ui/package.json
- [ ] T029 Document quickstart updates or API usage notes in specs/022-emulator-image-crop/quickstart.md
- [ ] T030 Ensure min crop size and PNG-only rules reflected in UI help text/tooltips in src/web-ui
