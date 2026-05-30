# Tasks: Reorder Spec Folders

**Input**: Design documents from `specs/043-reorder-spec-folders/`
**Prerequisites**: plan.md ✓, spec.md ✓, research.md ✓, data-model.md ✓

**Organization**: Tasks are grouped by user story. US1 (folder renames) must complete before US2 (metadata) and US3 (version bump). US2 and US3 are independent of each other.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to

---

## Phase 1: Setup

**Purpose**: Verify prerequisites and current state before any renames are performed.

- [ ] T001 Confirm git working tree is clean on branch `038-reorder-spec-folders` (`git status` shows no uncommitted changes)
- [ ] T002 Count spec directories in `specs/` excluding `openapi.json` and confirm total is 43 (42 pre-existing + `043-reorder-spec-folders`)
- [ ] T003 Cross-check `specs/043-reorder-spec-folders/research.md` renaming map — all 42 pre-existing folders must appear exactly once in the "Current Folder" column

**Checkpoint**: Confirmed 43 spec dirs, map is complete — proceed with renames.

---

## Phase 2: Foundational

No shared blocking infrastructure required for this maintenance task. Proceed directly to User Story phases.

---

## Phase 3: User Story 1 — Sequential Spec Navigation (Priority: P1) 🎯 MVP

**Goal**: Rename all 42 existing spec folders to gapless sequential numbers 001–042 in chronological order.

**Independent Test**: `ls specs/` returns exactly 43 entries (dirs + openapi.json), each directory beginning with a unique prefix, forming a gapless sequence 001–043. The prefix order matches the first-commit dates in `research.md`.

### Implementation for User Story 1

- [ ] T004 [US1] Rename high-range numbered specs in `specs/` (process top-down to free targets):
  - `git mv specs/037-loop-step-management specs/042-loop-step-management`
  - `git mv specs/036-tap-wait-retry specs/037-tap-wait-retry`
  - `git mv specs/035-ui-config-editor specs/036-ui-config-editor`
  - `git mv specs/034-background-screenshot-service specs/035-background-screenshot-service`
  - `git mv specs/033-command-loops specs/034-command-loops`

- [ ] T005 [US1] Handle the 031/032 tie-swap in `specs/`:
  - `git mv specs/031-sequence-conditional-steps specs/033-sequence-conditional-steps`
  - (`032-per-step-conditions` keeps its number — no rename needed)

- [ ] T006 [US1] Rename mid-range numbered specs in `specs/` (continue top-down):
  - `git mv specs/030-sequence-conditional-logic specs/031-sequence-conditional-logic`
  - `git mv specs/029-execution-logs-tab specs/030-execution-logs-tab`
  - `git mv specs/028-execution-log specs/029-execution-log`
  - `git mv specs/027-add-primitive-tap-action specs/028-add-primitive-tap-action`
  - `git mv specs/026-installer-semver-upgrade specs/027-installer-semver-upgrade`
  - `git mv specs/025-standalone-windows-installer specs/026-standalone-windows-installer`
  - `git mv specs/024-backend-webui-installer specs/025-backend-webui-installer`
  - `git mv specs/023-authoring-execution-ui specs/024-authoring-execution-ui`
  - `git mv specs/022-emulator-image-crop specs/023-emulator-image-crop`
  - `git mv specs/021-images-authoring-ui specs/022-images-authoring-ui`
  - `git mv specs/020-connect-game-action specs/021-connect-game-action`
  - `git mv specs/019-web-ui-nav specs/020-web-ui-nav`
  - `git mv specs/018-api-refactor specs/019-api-refactor`
  - `git mv specs/017-unify-authoring-ui specs/018-unify-authoring-ui`
  - `git mv specs/013-semantic-actions-ui specs/017-semantic-actions-ui`
  - `git mv specs/012-web-ui-authoring specs/015-web-ui-authoring`
  - `git mv specs/011-sequence-logic specs/014-sequence-logic`
  - `git mv specs/010-command-sequences specs/013-command-sequences`
  - `git mv specs/009-image-detect-command specs/012-image-detect-command`
  - `git mv specs/008-image-match-detections specs/011-image-match-detections`
  - `git mv specs/007-image-storage specs/010-image-storage`
  - `git mv specs/006-ocr-confidence-refactor specs/009-ocr-confidence-refactor`
  - `git mv specs/005-fix-trigger-evaluate specs/008-fix-trigger-evaluate`
  - `git mv specs/004-tesseract-logging specs/006-tesseract-logging`
  - `git mv specs/004-logging-config-refresh specs/005-logging-config-refresh`
  - `git mv specs/003-command-trigger-tests specs/004-command-trigger-tests`

- [ ] T007 [US1] Rename late-arriving `001-*` specs in `specs/` to high targets (all targets are now free):
  - `git mv specs/001-sequence-random-delay specs/038-sequence-random-delay`
  - `git mv specs/001-primitive-actions-refactor specs/039-primitive-actions-refactor`
  - `git mv specs/001-wait-for-image specs/040-wait-for-image`
  - `git mv specs/001-fix-sequence-step-names specs/041-fix-sequence-step-names`

- [ ] T008 [US1] Rename remaining `001-*` specs in `specs/` to low targets:
  - `git mv specs/001-authoring-crud-ui specs/016-authoring-crud-ui`
  - `git mv specs/001-runtime-logging-control specs/007-runtime-logging-control`
  - `git mv specs/001-action-command-refactor specs/003-action-command-refactor`

**Checkpoint**: All 42 renames applied. Verify with `ls specs/ | sort` — should show gapless 001–043 (plus `openapi.json`).

---

## Phase 4: User Story 2 — Correct Self-Referential Number (Priority: P2)

**Goal**: Update `spec.md` front-matter in renamed folders and confirm `.specify/feature.json` is correct.

**Independent Test**: Every `spec.md` whose folder was renamed has its `Feature Branch` and/or `Spec Directory` fields updated to reflect the new folder name. `.specify/feature.json` `feature_directory` equals `specs/043-reorder-spec-folders`.

### Implementation for User Story 2

- [ ] T009 [US2] For each of the 40 renamed spec folders that contain a `spec.md` with a `Feature Branch` or `Spec Directory` field referencing the old folder name, update those fields to the new folder name. Check `specs/*/spec.md` with `grep -rn "Feature Branch\|Spec Directory"` and apply in-place replacements (old prefix → new prefix, short name unchanged).

- [ ] T010 [P] [US2] Confirm `specs/043-reorder-spec-folders/spec.md` has `Spec Directory: 043-reorder-spec-folders` and `.specify/feature.json` contains `"feature_directory": "specs/043-reorder-spec-folders"` (no changes expected — verify only).

**Checkpoint**: All `spec.md` front-matter fields match new folder names.

---

## Phase 5: User Story 3 — Minor Version Bump (Priority: P2)

**Goal**: Increment the installer minor version from 4 to 5.

**Independent Test**: `installer/versioning/version.override.json` shows `"minor": "5"` and `"patch": "0"`.

### Implementation for User Story 3

- [ ] T011 [P] [US3] Edit `installer/versioning/version.override.json`: set `"minor"` to `"5"` and update `"updatedAtUtc"` to `"2026-05-30T00:00:00Z"`. All other fields (`major`, `patch`, `updatedBy`) remain unchanged.

**Checkpoint**: Version file updated; `minor = "5"`, `patch = "0"`.

---

## Phase 6: Polish & Verification

**Purpose**: Confirm correctness of all changes and that no source code was touched.

- [ ] T012 [P] Verify gapless sequence: `ls specs/ | grep -v openapi.json | wc -l` equals 43; all prefixes are unique integers 001–043.
- [ ] T013 [P] Verify no source code modifications: `git diff --name-only HEAD` must contain only files under `specs/`, `installer/versioning/version.override.json`, and `.specify/feature.json`. No `.cs`, `.ts`, `.vue`, or other source files.
- [ ] T014 Commit all changes with message: `"Reorder spec folders chronologically (001–042) and bump minor version to 0.5.0 (#043)"`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: N/A — skipped for this maintenance task
- **US1 (Phase 3)**: Depends on Setup completion. Internal task order: T004 → T005 → T006 → T007 → T008 (strict sequential — each rename step relies on previous targets being freed)
- **US2 (Phase 4)**: Depends on US1 completion (folder names must be final)
- **US3 (Phase 5)**: Independent of US1 and US2 — can run in parallel with Phase 4
- **Polish (Phase 6)**: Depends on US1 + US2 + US3 all complete

### User Story Dependencies

- **US1 (P1)**: First — establishes the correct folder names
- **US2 (P2)**: After US1 — updates internal references in renamed folders
- **US3 (P2)**: After Setup — independent of US1/US2; can be done in parallel with US2

### Within US1

T004 → T005 → T006 → T007 → T008 must execute in order to avoid rename conflicts. Each step frees folder numbers used as targets by the next step.

---

## Parallel Opportunities

```
# Phase 4 and Phase 5 can run in parallel after US1 completes:
Task T009 (update spec.md metadata in renamed folders)
Task T011 (bump version.override.json)

# Within Phase 6, verification tasks can run in parallel:
Task T012 (count/sequence check)
Task T013 (git diff source check)
```

---

## Implementation Strategy

### MVP First (US1 Only)

1. Complete Phase 1: Setup verification
2. Complete Phase 3: All five rename batches (T004–T008)
3. **STOP and VALIDATE**: `ls specs/ | sort` shows gapless 001–043
4. Proceed to US2 and US3

### Full Completion

1. Setup → US1 (renames) → US2 + US3 in parallel → Polish + Commit

---

## Notes

- Use `git mv` for all renames so git tracks the history correctly
- Process rename batches top-down (highest current number first) within US1 to avoid conflicts
- `001-save-config`, `002-config-logging-hardening`, and `032-per-step-conditions` are no-ops — do NOT rename them
- `043-reorder-spec-folders` is the current spec and already correctly numbered — do NOT rename it
- No `.cs`, `.ts`, `.vue`, or runtime config files may be touched
