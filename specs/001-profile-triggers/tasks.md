---

description: "Task list for Triggered Profile Execution feature"
---

# Tasks: Triggered Profile Execution

**Input**: Design documents from `/specs/001-profile-triggers/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Tests**: Tests are REQUIRED for any executable logic per the Constitution.

**Organization**: Tasks are grouped by user story (US1..US3) to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- [P]: Can run in parallel (different files, no dependencies)
- [Story]: Which user story this task belongs to (US1, US2, US3)

---

## Phase 1: Foundational (Blocking)

- [ ] T001 Define domain entities and interfaces
  - Paths: `src/GameBot.Domain/Profiles/ProfileTrigger.cs`, `src/GameBot.Domain/Profiles/TriggerEvaluationResult.cs`, `src/GameBot.Domain/Profiles/ITriggerEvaluator.cs`
  - Acceptance: Compiles; XML docs for public types; unit test project references compile

- [ ] T002 Implement TriggerEvaluationService (domain)
  - Path: `src/GameBot.Domain/Services/TriggerEvaluationService.cs`
  - Acceptance: Pure logic (no IO); methods < 50 LOC; unit tests pass (see T101-T104)

- [ ] T003 Add TriggerBackgroundWorker (service)
  - Path: `src/GameBot.Service/Hosted/TriggerBackgroundWorker.cs`
  - Acceptance: Periodic evaluation every N ms (configurable); safe shutdown; logs with correlation

- [ ] T004 Extend AutomationProfile persistence to include triggers
  - Paths: `src/GameBot.Domain/Profiles/AutomationProfile.cs`, `src/GameBot.Domain/Profiles/FileProfileRepository.cs`
  - Acceptance: CRUD persists `triggers` array; validation errors for invalid triggers

- [ ] T005 Wire SessionManager snapshot access for evaluator
  - Path: `src/GameBot.Emulator/Session/SessionManager.cs`
  - Acceptance: Snapshot method is callable by evaluator; no blocking of input pipeline

- [ ] T006 [P] Add DTOs and endpoint scaffolding
  - Paths: `src/GameBot.Service/Models/Triggers.cs`, `src/GameBot.Service/Endpoints/TriggersEndpoints.cs`
  - Acceptance: Minimal endpoints compile; OpenAPI exposes new paths (see contracts)

---

## Phase 2: User Story 1 - Image trigger (P1) 🎯 MVP

### Tests FIRST
- [ ] T101 [P] [US1] Unit tests: image-match evaluation logic
  - Path: `tests/unit/Emulator/Triggers/ImageMatchEvaluatorTests.cs`
  - Acceptance: Happy path + below-threshold + out-of-bounds region

- [ ] T102 [P] [US1] Integration tests: image trigger firing
  - Path: `tests/integration/TriggerEvaluationTests.cs`
  - Acceptance: POST /triggers/test returns satisfied with similarity≥threshold; cooldown respected

- [ ] T103 [P] [US1] Contract tests: OpenAPI fragment compliance
  - Path: `tests/contract/TriggersContractTests.cs`
  - Acceptance: New schemas and endpoints match fragment

### Implementation
- [ ] T104 [US1] Implement ImageMatchEvaluator (template NCC per research)
  - Path: `src/GameBot.Domain/Profiles/Evaluators/ImageMatchEvaluator.cs`
  - Acceptance: Meets thresholds; downscale + grayscale; perf within budget

- [ ] T105 [US1] Expose reference image repository (identifier lookup)
  - Path: `src/GameBot.Domain/Games/GameArtifact.cs` or new `Images/` store
  - Acceptance: Able to resolve `referenceImageId` to bytes; tests use fixtures

- [ ] T106 [US1] Endpoint: create/list/get/patch/delete triggers (image type)
  - Path: `src/GameBot.Service/Endpoints/TriggersEndpoints.cs`
  - Acceptance: Matches contracts; validation errors for bad regions/thresholds

- [ ] T107 [US1] Endpoint: test-evaluate trigger
  - Path: `src/GameBot.Service/Endpoints/TriggersEndpoints.cs`
  - Acceptance: Returns TriggerEvaluationResult

- [ ] T108 [US1] Background evaluation integration
  - Paths: `src/GameBot.Service/Hosted/TriggerBackgroundWorker.cs`, `src/GameBot.Domain/Services/TriggerEvaluationService.cs`
  - Acceptance: Auto-start profile when satisfied; single firing per cooldown

---

## Phase 3: User Story 2 - Time triggers (P2)

### Tests FIRST
- [ ] T201 [P] [US2] Unit tests: delay & schedule evaluation
  - Path: `tests/unit/Emulator/Triggers/TimeTriggerEvaluatorTests.cs`
  - Acceptance: Delay seconds and absolute time; past schedule rejected

### Implementation
- [ ] T202 [US2] Implement Delay/Schedule evaluators
  - Path: `src/GameBot.Domain/Profiles/Evaluators/TimeEvaluators.cs`
  - Acceptance: Accurate timing (±1s); non-repeating delay behavior

- [ ] T203 [US2] Endpoint support for time triggers
  - Path: `src/GameBot.Service/Endpoints/TriggersEndpoints.cs`
  - Acceptance: CRUD + validation

---

## Phase 4: User Story 3 - Text triggers (P3)

### Tests FIRST
- [ ] T301 [P] [US3] Unit tests: text-match evaluation
  - Path: `tests/unit/Emulator/Triggers/TextMatchEvaluatorTests.cs`
  - Acceptance: found/not-found modes; confidence threshold; debounce flicker

### Implementation
- [ ] T302 [US3] Implement TextMatchEvaluator (Tesseract per research)
  - Path: `src/GameBot.Domain/Profiles/Evaluators/TextMatchEvaluator.cs`
  - Acceptance: Region OCR, whitelist options, confidence metrics

- [ ] T303 [US3] Endpoint support for text triggers
  - Path: `src/GameBot.Service/Endpoints/TriggersEndpoints.cs`
  - Acceptance: CRUD + validation

---

## Phase 5: Cross-Cutting and Polish

- [ ] T401 [P] Logging & correlation for triggers
  - Paths: `src/GameBot.Service/Hosted/TriggerBackgroundWorker.cs`, evaluator services
  - Acceptance: Debug logs at start/end of evaluation; include triggerId and profileId

- [ ] T402 [P] Performance validation
  - Path: `tests/integration/Performance/TriggerPerfTests.cs`
  - Acceptance: 95% detection-to-start ≤2s; document results in plan

- [ ] T403 Documentation updates
  - Paths: `README.md`, `specs/001-profile-triggers/quickstart.md`
  - Acceptance: Quickstart validated against running service

- [ ] T404 [P] Security & error handling
  - Paths: trigger endpoints, validation layer
  - Acceptance: Clear error messages; input validation prevents abuse

---

## Dependencies & Execution Order

- Foundational Phase must complete before US1..US3
- Within each story: write tests first → implement → integrate → validate
- Background worker depends on evaluator service and persistence

---

## Notes

- Regions are normalized (0..1)
- Cooldown enforced after firing; ensure single start per window
- Use fixtures for deterministic image/text tests
