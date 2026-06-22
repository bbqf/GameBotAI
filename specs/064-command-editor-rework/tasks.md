---
description: "Task list for Command Editor Rework"
---

# Tasks: Command Editor Rework

**Input**: Design documents from `specs/053-command-editor-rework/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md

**Tests**: INCLUDED. The plan's Phase D and the constitution coverage gate (unit tests ≥ 80% line / ≥ 70% branch for touched areas) make tests required for this feature.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3, US4)
- Include exact file paths in descriptions

## Path Conventions

- Web application frontend. Source root: `src/web-ui/src/`
- Components: `src/web-ui/src/components/commands/`
- Tests colocate in `__tests__/` subdirectories next to the code under test

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project scaffolding and baseline understanding

- [X] T001 [P] Create the new test directory `src/web-ui/src/components/commands/__tests__/` (add a `.gitkeep` if needed so the empty dir is tracked).
- [X] T002 Establish baseline by reading `src/web-ui/src/components/commands/CommandForm.tsx`, `src/web-ui/src/components/SortableSequenceStepList.tsx`, and `src/web-ui/src/pages/CommandsPage.tsx`: record the current `pendingX` useState names, the `StepEntry` discriminant values, the `toStepItems` label strings, `commandOptions` prop usage, and the validation message strings (e.g. "Primitive tap steps require…") that later tasks must change.

**Checkpoint**: Source layout and current state shape understood; new test directory exists. Run `vite build && jest` to confirm the baseline is green before proceeding.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared component change that the edit flow and panel wiring depend on

**⚠️ CRITICAL**: Complete before user-story panel wiring relies on it

- [X] T003 Add an optional `onEdit?: (item: ReorderableListItem) => void` prop to `src/web-ui/src/components/SortableSequenceStepList.tsx`; when provided, render an "Edit" button in `.reorderable-list__controls` alongside the existing "Delete" button that calls `onEdit(item)`. Leave behaviour unchanged when `onEdit` is absent.
- [X] T004 [P] Update `src/web-ui/src/components/__tests__/SortableSequenceStepList.test.tsx` to cover the new `onEdit` behaviour: Edit button renders only when `onEdit` is supplied, and clicking it fires `onEdit` with the correct item.

**Checkpoint**: Step list can surface an Edit affordance; ready for CommandForm scaffolding and panels. Run `vite build && jest` — must be green before US1 work begins.

---

## Phase 3: User Story 1 - Select Action Then Fill Panel (Priority: P1) 🎯 MVP

**Goal**: Replace the flat always-visible step controls with an action-first selector and the CommandForm scaffolding (state, edit routing, panel slot) that every panel plugs into. Remove the "Add command" step-addition UI and apply the "Tap" rename in display/validation text.

**Independent Test**: Open the command editor — the add-step selector lists exactly Tap, Wait for Image, Ensure Game Running (no Command option); selecting an action clears any previously open panel; clicking an existing step routes into edit mode; after a confirm the selector returns to blank. (Full "panel appears" verification completes once the first panel from US2 is wired.)

### Tests for User Story 1

- [X] T005 [P] [US1] Create `src/web-ui/src/components/commands/__tests__/ActionTypeSelector.test.tsx`: renders a blank first option plus exactly Tap / Wait for Image / Ensure Game Running; `onChange` fires with `'PrimitiveTap' | 'WaitForImage' | 'EnsureGameRunning'`; the blank option yields `''`.
- [X] T006 [P] [US1] Create `src/web-ui/src/components/commands/__tests__/CommandForm.steps.test.tsx` (scaffolding scenarios): selector shows the three options and no "Add command" UI (FR-001, SC-002); switching action type resets panel state (FR-006, SC-005); after confirm the selector returns to blank (FR-009, SC-006); clicking a primitive-action step opens edit mode (FR-011); step list labels read "Tap: …" not "Primitive tap: …"; mount CommandForm with an existing Command-type step and assert it renders in the list and its Delete button fires (FR-010, U2); assert clicking a Command step's Edit button does NOT open any panel and leaves `pendingActionType` as `''` (I2 guard).

### Implementation for User Story 1

- [X] T007 [P] [US1] Create `src/web-ui/src/components/commands/ActionTypeSelector.tsx` — a `<select>` with a blank first option followed by Tap, Wait for Image, Ensure Game Running, typed per plan (`value`/`onChange`/`disabled` props over `'PrimitiveTap' | 'WaitForImage' | 'EnsureGameRunning' | ''`).
- [X] T008 [US1] Refactor `src/web-ui/src/components/commands/CommandForm.tsx` state: remove `pendingCommandId`, `pendingPrimitiveReferenceImageId`, `primitiveTapStale`, `pendingPrimitiveConfidence`, `pendingPrimitiveOffsetX`, `pendingPrimitiveOffsetY`, `pendingWaitReferenceImageId`, `pendingWaitConfidence`, `pendingWaitTimeoutMs`; add `pendingActionType` and `editingStepId` state per data-model.md.
- [X] T009 [US1] In `src/web-ui/src/components/commands/CommandForm.tsx`, remove the "Add command" `SearchableDropdown` and its add button from the JSX; retain the `commandOptions` prop and the `Command` branch in `toStepItems` so existing Command steps still render and stay deletable (FR-010).
- [X] T010 [US1] In `src/web-ui/src/components/commands/CommandForm.tsx`, render `ActionTypeSelector` bound to `pendingActionType`/`setPendingActionType` in the Steps `FormSection`, plus an empty conditional panel slot (no panel components imported yet — US2–US4 fill it). Selecting a different action resets `editingStepId` to `null` so stale state cannot bleed across types (FR-002, FR-006).
- [X] T011 [US1] In `src/web-ui/src/components/commands/CommandForm.tsx`, add `handleEditStep(item)` and pass `onEdit={handleEditStep}` to `SortableSequenceStepList`; on edit, look up the step by id and guard: if `step.type` is not in `{'PrimitiveTap', 'WaitForImage', 'EnsureGameRunning'}` (e.g. it is `'Command'`), return immediately without changing state — Command steps have no panel (plan C4, I2 fix). For supported types, set `editingStepId` and `pendingActionType` so the matching panel opens pre-filled (FR-011, SC-007).
- [X] T012 [US1] In `src/web-ui/src/components/commands/CommandForm.tsx`, change the `toStepItems` label `"Primitive tap: …"` → `"Tap: …"` and update the Steps `FormSection` description to "Select an action type to add or edit steps." (plan C5, C7).
- [X] T013 [US1] In `src/web-ui/src/pages/CommandsPage.tsx`, change the validation message `"Primitive tap steps require…"` → `"Tap steps require…"` (plan C6, SC-004 wording consistency).
- [X] T014 [US1] Update `src/web-ui/src/features/authoring/__tests__/CommandForm.zoom.test.tsx`: remove references to the removed `pendingX` controls and the "Add command" UI, retain the unchanged Detection-section assertions, and update step-panel selectors to the new selector-based controls.
- [X] T015 [P] [US1] Add base panel/selector styling to `src/web-ui/src/components/commands/CommandForm.css` for the action selector and the shared panel container (reused by all three panels in US2–US4).

**Checkpoint**: Selector, edit routing, Command-removal, and renames are in place and compile; the panel slot is empty until US2 wires the first panel. Run `vite build && jest` — must be green before US2 work begins.

---

## Phase 4: User Story 2 - Tap Panel (Priority: P2)

**Goal**: A focused Tap panel exposing exactly reference image (required), confidence, offsetX, offsetY — wired into CommandForm for add and in-place edit.

**Independent Test**: Select Tap → panel shows only the four Tap fields; submitting with an empty/stale reference image is blocked with a "image required" error; submitting with all four values saves them on the step; clicking an existing Tap step reopens the panel pre-filled.

### Tests for User Story 2

- [X] T016 [P] [US2] Create `src/web-ui/src/components/commands/__tests__/TapPanel.test.tsx`: renders only image/confidence/offsetX/offsetY (FR-003, SC-003); confirm blocked when `referenceImageId` empty with error text "Reference image is required." (FR-007, SC-004, U4); confirm blocked when `referenceImageId` stale with same error (FR-007); confirm blocked when `confidence` non-empty and outside 0–1 with error text "Confidence must be a number between 0 and 1." (U1); confirm fires with all four values when valid; cancel does not call `onConfirm`; `initialValue` pre-fills fields.

### Implementation for User Story 2

- [X] T017 [P] [US2] Create `src/web-ui/src/components/commands/TapPanel.tsx` per plan A2 / data-model: internal state `referenceImageId` (required, default `''`), `confidence` (default `''`), `offsetX`/`offsetY` (default `'0'`), `stale`; renders image selector + three optional inputs + Add/Save + Cancel; blocks confirm when image empty or stale (error: "Reference image is required."); blocks confirm when confidence non-empty and outside 0–1 (error: "Confidence must be a number between 0 and 1.").
- [X] T018 [US2] Wire `TapPanel` into `src/web-ui/src/components/commands/CommandForm.tsx`: render it when `pendingActionType === 'PrimitiveTap'`, feeding `initialValue` from the step looked up by `editingStepId`; `onConfirm` appends a new `PrimitiveTap` step (add mode) or replaces it at its current index (edit mode), then resets `pendingActionType`/`editingStepId`; `onCancel` resets both without changing steps.
- [X] T019 [US2] Extend `src/web-ui/src/components/commands/__tests__/CommandForm.steps.test.tsx` with the Tap add-and-edit flow end-to-end (select Tap → fill → add → step appears as "Tap: …"; click it → panel pre-filled → save updates in place), satisfying the remaining US1 acceptance scenario 2/5 now that a panel exists.

**Checkpoint**: Tap steps can be added and edited end-to-end. **MVP reached (US1 + US2).** Run `vite build && jest` — must be green before US3 work begins.

---

## Phase 5: User Story 3 - Wait for Image Panel (Priority: P2)

**Goal**: A Wait for Image panel exposing timeout (required) plus optional reference image and confidence — wired into CommandForm for add and edit.

**Independent Test**: Select Wait for Image → panel shows timeoutMs (required), optional image, optional confidence; empty/invalid timeout blocks submit with an error; timeout-only adds a duration-only wait; timeout + image + confidence saves all three; existing WaitForImage step reopens pre-filled.

### Tests for User Story 3

- [X] T020 [P] [US3] Create `src/web-ui/src/components/commands/__tests__/WaitForImagePanel.test.tsx`: renders only timeoutMs/image/confidence, no `stale` blocking (FR-004, SC-003, U3); confirm blocked when `timeoutMs` empty with error text "Timeout must be a non-negative whole number (ms)." (FR-008, SC-004, U4); confirm blocked when `timeoutMs` non-integer (same error); confirm blocked when `confidence` non-empty and outside 0–1 with error text "Confidence must be a number between 0 and 1." (U1); confirm fires with provided values; cancel does not call `onConfirm`; `initialValue` pre-fills fields; a stale or empty `referenceImageId` does not block confirm (staleness is advisory for optional image — U3).

### Implementation for User Story 3

- [X] T021 [P] [US3] Create `src/web-ui/src/components/commands/WaitForImagePanel.tsx` per plan A3 / data-model: internal state `timeoutMs` (required, default `'1000'`), `referenceImageId` (default `''`), `confidence` (default `''`); no `stale` field (optional image is advisory — U3); renders timeout input + optional image selector + optional confidence + Add/Save + Cancel; blocks confirm unless `timeoutMs` is a non-negative integer string (error: "Timeout must be a non-negative whole number (ms)."); blocks confirm when confidence non-empty and outside 0–1 (error: "Confidence must be a number between 0 and 1.").
- [X] T022 [US3] Wire `WaitForImagePanel` into `src/web-ui/src/components/commands/CommandForm.tsx`: render it when `pendingActionType === 'WaitForImage'` with `initialValue` from the edited step; `onConfirm` appends/updates a `WaitForImage` step then resets pending state; `onCancel` resets without changing steps.
- [X] T023 [US3] Extend `src/web-ui/src/components/commands/__tests__/CommandForm.steps.test.tsx` with the Wait for Image add-and-edit flow end-to-end.

**Checkpoint**: Tap and Wait for Image both work independently end-to-end. Run `vite build && jest` — must be green before US4 work begins.

---

## Phase 6: User Story 4 - Ensure Game Running Panel (Priority: P3)

**Goal**: A minimal Ensure Game Running panel — description text and a confirm button, no input fields — wired into CommandForm.

**Independent Test**: Select Ensure Game Running → panel shows descriptive text and an Add control with zero input fields; confirming adds an EnsureGameRunning step.

### Tests for User Story 4

- [X] T024 [P] [US4] Create `src/web-ui/src/components/commands/__tests__/EnsureGameRunningPanel.test.tsx`: renders description text and an Add control with no input fields (FR-005, SC-003); confirm fires `onConfirm`; cancel does not.

### Implementation for User Story 4

- [X] T025 [P] [US4] Create `src/web-ui/src/components/commands/EnsureGameRunningPanel.tsx` per plan A4: descriptive text ("Checks that the game is in the foreground; starts it if not running."), Add button, Cancel button, no inputs.
- [X] T026 [US4] Wire `EnsureGameRunningPanel` into `src/web-ui/src/components/commands/CommandForm.tsx`: render it when `pendingActionType === 'EnsureGameRunning'`; `onConfirm` appends an `EnsureGameRunning` step (edit mode is a no-op confirm since it has no attributes) then resets pending state; `onCancel` resets without changing steps.
- [X] T027 [US4] Extend `src/web-ui/src/components/commands/__tests__/CommandForm.steps.test.tsx` with the Ensure Game Running add flow.

**Checkpoint**: All three primitive action panels are addable/editable; the add-step area never shows fields from removed/other action types (SC-001). Run `vite build && jest` — must be green before Polish phase.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Final consistency sweep and quality gate

- [X] T028 [P] Per-panel styling pass in `src/web-ui/src/components/commands/CommandForm.css` (validation-error text, button rows) so all three panels render consistently with the base styles from T015.
- [X] T029 Grep the web-ui source for any remaining "Primitive tap" / "Primitive Tap" user-facing strings and confirm only the `'PrimitiveTap'` discriminant value remains (no display/validation text), preserving API/DTO compatibility (research decision; plan constraint).
- [X] T030 Run the project quality gate from `src/web-ui`: `vite build` then `jest`. Fix any failures; do not leave a red build/test (constitution hard stop). Confirm coverage on touched areas meets the gate.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: Depends on Setup. The `onEdit` list change (T003/T004) is a prerequisite for US1's edit routing (T011).
- **User Story 1 (Phase 3)**: Depends on Foundational. Provides the selector + CommandForm scaffolding + panel slot that US2–US4 plug into; therefore US1 scaffolding precedes the panel-wiring tasks of US2–US4.
- **User Story 2 (Phase 4)**: Panel component (T016/T017) is independent and can be built in parallel with US1; its CommandForm wiring (T018) depends on the US1 slot (T010).
- **User Story 3 (Phase 5)**: Same shape as US2 — panel independent; wiring depends on US1 slot.
- **User Story 4 (Phase 6)**: Same shape as US2 — panel independent; wiring depends on US1 slot.
- **Polish (Phase 7)**: Depends on all desired stories being complete.

### Story Independence Notes

- Each panel component (TapPanel, WaitForImagePanel, EnsureGameRunningPanel) is a self-contained, independently unit-testable leaf with `onConfirm`/`onCancel` — its test task (T016/T020/T024) and creation task (T017/T021/T025) have no cross-story dependency.
- The integration (wiring) tasks for each panel share the single file `CommandForm.tsx`, so T018, T022, T026 are **not** parallel with each other.
- US1's full "panel appears" acceptance (scenario 2) is exercised once the first panel is wired (T019), which is why MVP is **US1 + US2**, not US1 alone.

### Within Each User Story

- Tests are written to fail first, then implementation makes them pass.
- Panel component creation before its CommandForm wiring.
- CommandForm wiring before the end-to-end flow test extension.

### Parallel Opportunities

- T001 and the baseline read (T002) — T001 is [P].
- Panel components T017, T021, T025 are different files → fully parallel.
- Panel unit tests T016, T020, T024 are different files → fully parallel, and parallel with their own component creation only if written test-first against the agreed prop contracts in plan.md.
- ActionTypeSelector (T007) and base CSS (T015) are parallel with the CommandForm refactor tasks.
- **Not parallel**: all tasks touching `CommandForm.tsx` (T008–T013, T018, T022, T026) and all tasks touching `CommandForm.steps.test.tsx` (T006, T019, T023, T027) — same files, sequential.

---

## Parallel Example: Panel Components (US2 / US3 / US4)

```text
# Different files, no shared state — build the three panels in parallel:
Task: "Create src/web-ui/src/components/commands/TapPanel.tsx"              (T017)
Task: "Create src/web-ui/src/components/commands/WaitForImagePanel.tsx"     (T021)
Task: "Create src/web-ui/src/components/commands/EnsureGameRunningPanel.tsx" (T025)

# And their unit tests in parallel:
Task: "TapPanel.test.tsx"              (T016)
Task: "WaitForImagePanel.test.tsx"     (T020)
Task: "EnsureGameRunningPanel.test.tsx" (T024)
```

---

## Implementation Strategy

### MVP First (User Story 1 + User Story 2)

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational (`onEdit` on the step list).
3. Complete Phase 3: US1 scaffolding (selector, state, edit routing, Command removal, renames).
4. Complete Phase 4: US2 Tap panel + wiring.
5. **STOP and VALIDATE**: add and edit a Tap step end-to-end; confirm no "Add command" UI and no stale fields. This is the demoable MVP.

### Incremental Delivery

1. Setup + Foundational → list supports Edit.
2. US1 + US2 → selector flow with Tap panel (MVP).
3. US3 → Wait for Image panel.
4. US4 → Ensure Game Running panel.
5. Polish → consistency sweep + green `vite build` + `jest`.

---

## Notes

- [P] tasks = different files, no dependencies.
- `StepEntry.type` discriminant values (including `'PrimitiveTap'`) are unchanged for API/DTO compatibility — only display/validation text is renamed.
- The real green gate for this repo is `vite build` + `jest` (lint and `tsc --noEmit` carry pre-existing failures), per project conventions. Each phase checkpoint requires a green gate before the next phase starts.
- `pendingActionType` uses `''` (empty string) as the "no panel open" sentinel — not `null`. This matches `ActionTypeSelectorProps.value` directly (I1 fix).
- WaitForImagePanel has no `stale` field — its optional `referenceImageId` is advisory; a stale/deleted image reference does not block confirm (U3 decision).
- `handleEditStep` guards against `step.type === 'Command'` with an early return — Command steps have no panel (I2 fix).
- Confidence range validation (0–1) applies when the field is non-empty in both TapPanel and WaitForImagePanel (U1 decision).
- Commit after each task or logical group; never proceed past a red build/test.
