# Tasks: Commands Based on Detected Image

## Phase 1: Setup

- [ ] T001 Ensure branch exists and synced: `005-image-detect-command`
- [ ] T002 Verify repo build/test baseline: run `tasks: build`, `tasks: test`
- [ ] T003 Create Domain stubs per plan: `src/GameBot.Domain/Commands/Execution/`

## Phase 2: Foundational

- [ ] T004 Add `DetectionTarget` model: `src/GameBot.Domain/Commands/DetectionTarget.cs`
- [ ] T005 Add `ResolvedCoordinate` value object: `src/GameBot.Domain/Commands/ResolvedCoordinate.cs`
- [ ] T006 Implement `DetectionCoordinateResolver`: `src/GameBot.Domain/Commands/Execution/DetectionCoordinateResolver.cs`
- [ ] T007 Wire resolver in `CommandExecutionService`: `src/GameBot.Domain/Services/CommandExecutionService.cs`
- [ ] T008 Extend command schema handling to read `detectionTarget`: `src/GameBot.Domain/Commands/*`

## Phase 3: User Story 1 (P1) — Tap detected image center

- [ ] T009 [P] [US1] Validate `DetectionTarget` inputs (confidence range, ref id): `src/GameBot.Domain/Commands/DetectionTarget.cs`
- [ ] T010 [US1] Resolve single detection center and produce coordinates: `src/GameBot.Domain/Commands/Execution/DetectionCoordinateResolver.cs`
- [ ] T011 [US1] Integrate with tap action path: `src/GameBot.Domain/Services/CommandExecutionService.cs`
- [ ] T012 [US1] Log success (info) with coordinates and metadata: `src/GameBot.Domain/Services/CommandExecutionService.cs`
- [ ] T013 [US1] Quickstart example update (single match): `specs/005-image-detect-command/quickstart.md`

## Phase 4: User Story 2 (P2) — Tap with offset from detection

- [ ] T014 [P] [US2] Apply (offsetX, offsetY) to base point: `src/GameBot.Domain/Commands/Execution/DetectionCoordinateResolver.cs`
- [ ] T015 [US2] Clamp coordinates to screen bounds using session dims: `src/GameBot.Domain/Commands/Execution/DetectionCoordinateResolver.cs`
- [ ] T016 [US2] Emit debug log when clamping occurs: `src/GameBot.Domain/Services/CommandExecutionService.cs`
- [ ] T017 [US2] Verify bounds via integration path: `tests/integration/DetectionCommandIntegrationTests.cs`

## Phase 5: User Story 3 (P1) — Enforce unique detection

- [ ] T018 [P] [US3] Enforce exactly-one match ≥ threshold: `src/GameBot.Domain/Commands/Execution/DetectionCoordinateResolver.cs`
- [ ] T019 [US3] Skip actions on multiple/zero detections: `src/GameBot.Domain/Services/CommandExecutionService.cs`
- [ ] T020 [US3] Log errors/info per scenario with counts and threshold: `src/GameBot.Domain/Services/CommandExecutionService.cs`

## Final Phase: Polish & Cross-Cutting

- [ ] T021 Add unit tests for validation/clamping: `tests/unit/DetectionTargetValidationTests.cs`
- [ ] T022 Add unit tests for base-point center logic: `tests/unit/DetectionCoordinateResolverTests.cs`
- [ ] T023 Add integration tests for success/multi/zero: `tests/integration/DetectionCommandIntegrationTests.cs`
- [ ] T024 Update contracts with schema fragment reference: `specs/005-image-detect-command/contracts/README.md`
- [ ] T025 Update repository README with feature summary link: `README.md`

## Dependencies

- US1 → US2 (offsets build on center resolution)
- US1 → US3 (uniqueness enforcement also used by US1)

## Parallel Execution Examples

- T009 [P] alongside T014 [P] (validation and offset logic in separate files)
- T018 [P] alongside T021 [P] (uniqueness enforcement and unit tests across distinct files)

## Implementation Strategy

- MVP: Complete Phase 3 (US1) — center-only resolution with info logging.
- Incrementally add Phase 4 (offsets + clamping) and Phase 5 (uniqueness enforcement).
