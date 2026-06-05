# Tasks: Visual Command Recorder

**Input**: Design documents from `specs/055-record-command/`  
**Prerequisites**: plan.md ✅ spec.md ✅ research.md ✅ data-model.md ✅ contracts/ ✅

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no incomplete task dependencies)
- **[Story]**: Which user story this task belongs to (US1–US4)

---

## Phase 1: Setup

**Purpose**: Create new files and directories so parallel work in Phase 2 can begin immediately.

- [ ] T001 Create `src/web-ui/src/types/picker.ts` with `RecordedStep` union type, `ImageMatchResult`, `PickerState`, and `PickerStatus` as defined in `data-model.md`
- [ ] T002 [P] Create directory `src/web-ui/src/components/commands/VisualStepPicker/` with empty placeholder files: `VisualStepPicker.tsx`, `StepPickerOverlay.tsx`, `RecordedStepList.tsx`, `usePickerState.ts`, `keyCodeMap.ts`; also create the `__tests__/` subdirectory so test tasks (T009b, T013b, T022b) have a target location

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Backend endpoint, frontend service function, state hook skeleton, and modal entry point — all must exist before any user story can be tested end-to-end.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [ ] T003 Add `DetectAllRequest`, `DetectAllMatch`, `DetectAllResponse` record types and `POST /api/images/detect-all` minimal API route to `src/GameBot.Service/Endpoints/ImageDetectionsEndpoints.cs`; logic: resolve captureId → load screenshot Mat from cache → enumerate all reference images from `ReferenceImageStore` → run `ITemplateMatcher.MatchAllAsync` in parallel via `Task.WhenAll` → flatten and return matches (see `contracts/api-detect-all.md` for full contract)
- [ ] T003b [P] Add xUnit integration test for `POST /api/images/detect-all` in `tests/unit/ImageDetectAllEndpointTests.cs`: verify 200 + non-empty match list when captureId is valid and at least one reference image exists; verify 404 when captureId is unknown; verify 400 when captureId is empty
- [ ] T004 [P] Add `detectAll(captureId: string): Promise<ImageMatchResult[]>` function to `src/web-ui/src/services/images.ts` — POST to `/api/images/detect-all`, map response fields to `ImageMatchResult` type from `picker.ts`
- [ ] T005 [P] Implement `usePickerState.ts` state machine hook in `src/web-ui/src/components/commands/VisualStepPicker/usePickerState.ts` with: `PickerState` state, `openPicker()` (calls `fetchEmulatorScreenshot` then `detectAll`), `recapture()` (same flow, sets `status: 'loading'`), and stub implementations for `recordTap`, `recordKey`, `recordSwipe`, `removeStep`, `reorderSteps`
- [ ] T006 Add "Record steps" button to the step-addition toolbar in `src/web-ui/src/components/commands/CommandForm.tsx`; button opens `<VisualStepPicker>` modal; on picker confirm, call `addStep()` for each returned `RecordedStep` converted to `StepEntry` using the mapping table in `data-model.md`

**Checkpoint**: `POST /api/images/detect-all` returns match results; "Record steps" button opens an empty modal shell; `usePickerState` transitions `status` between `loading` / `ready` / `error`.

---

## Phase 3: User Story 1 — Image-Region Tap (Priority: P1) 🎯 MVP

**Goal**: User clicks a highlighted bounding box on the screenshot and a `PrimitiveTap` step is recorded with the correct image ID and click offset from center.

**Independent Test**: Open picker → verify screenshot loads with SVG bounding-box overlays for all matched images → click inside a box → confirm → verify a `PrimitiveTap` step is appended to the command with the correct `referenceImageId` and non-zero `offsetX`/`offsetY` when click is off-center.

- [ ] T007 [US1] Implement `StepPickerOverlay.tsx` in `src/web-ui/src/components/commands/VisualStepPicker/StepPickerOverlay.tsx`: render `<img>` of screenshot blob URL inside a positioned container; overlay a same-size SVG layer with a `<rect>` + `<text>` label for each `ImageMatchResult` in props; pass `naturalWidth`/`naturalHeight` from `PickerState` to the component
- [ ] T008 [US1] Add gesture handler to `StepPickerOverlay` using the final prop signature `onGesture(start: Point, end: Point, durationMs: number)`; for this phase implement via an `onClick` handler that scales `clientX/clientY` to natural image coords using `getBoundingClientRect()` + natural-size ratio, then calls `onGesture({ x: naturalX, y: naturalY }, { x: naturalX, y: naturalY }, 0)` (degenerate form: start=end, durationMs=0 signals a tap to the dispatcher); Phase 6 T022 replaces this with real mousedown/mouseup tracking without changing the prop signature
- [ ] T009 [US1] Implement `recordTap(naturalX, naturalY)` in `usePickerState.ts`: iterate `state.matches`, find all bounding boxes containing the point, pick highest `confidence`, compute `offsetX = naturalX - (bbox.x + bbox.width/2)`, `offsetY = naturalY - (bbox.y + bbox.height/2)`; construct `label` as `"${match.imageName} (${offsetX >= 0 ? '+' : ''}${offsetX}, ${offsetY >= 0 ? '+' : ''}${offsetY})"` using `imageName` from the matched `ImageMatchResult` (stored at record time so label stays stable after re-capture); append `RecordedPrimitiveTapStep` to `state.steps`; if no bbox hit, do nothing (FR-006)
- [ ] T009b [P] [US1] Add Jest unit tests for `usePickerState` in `src/web-ui/src/components/commands/VisualStepPicker/__tests__/usePickerState.test.ts`: cover `recordTap` — on-center click produces offsetX=0/offsetY=0; off-center click produces correct signed delta; multiple overlapping bboxes → highest-confidence match selected; click outside all bboxes → steps array unchanged
- [ ] T010 [US1] Wire `StepPickerOverlay` into `VisualStepPicker.tsx` in `src/web-ui/src/components/commands/VisualStepPicker/VisualStepPicker.tsx`: render overlay with `matches` and `screenshotUrl` from hook state; pass an `onGesture` handler that — for this phase — extracts `end.x`/`end.y` from the degenerate `start=end, durationMs=0` form produced by T008 and calls `recordTap(end.x, end.y)`; T023 will upgrade this handler to perform real displacement dispatch without changing the `onGesture` prop signature
- [ ] T011 [US1] Add loading/blocked overlay in `StepPickerOverlay` for `status === 'loading'`: semi-transparent `div` with `pointer-events: none` over the image+SVG area plus a spinner; input handlers (`onClick`, `onMouseDown`) guard with `if (status !== 'ready') return` (FR-015b)
- [ ] T012 [US1] Add re-capture button to `VisualStepPicker.tsx` that calls `recapture()` from `usePickerState` (FR-015)

**Checkpoint**: User Story 1 fully functional — clicking a matched region appends a `PrimitiveTap` step with correct image ID and offset.

---

## Phase 4: User Story 4 — Step Review and Save (Priority: P1)

**Goal**: Recorded steps are visible in an ordered list; the user can delete and reorder steps, then confirm to append them to the command or cancel to discard.

**Independent Test**: Record two steps via US1 → delete one → reorder remaining → confirm → verify exactly the remaining steps in the correct order are appended to the command step list; cancel path → verify no steps added.

- [ ] T013 [US4] Implement `RecordedStepList.tsx` in `src/web-ui/src/components/commands/VisualStepPicker/RecordedStepList.tsx`: use `@dnd-kit/sortable` (`DndContext`, `SortableContext`, `arrayMove`) to render a sortable list of `RecordedStep` items; each item shows type icon + `label`; each item has a delete button calling `onRemove(id)` prop (FR-011); drag handles enable reorder calling `onReorder(newOrder)` prop (FR-011b)
- [ ] T013b [P] [US4] Add React component test for `RecordedStepList` in `src/web-ui/src/components/commands/VisualStepPicker/__tests__/RecordedStepList.spec.tsx`: verify each step renders its `label`; verify clicking delete calls `onRemove` with the correct step id
- [ ] T014 [US4] Wire `RecordedStepList` into `VisualStepPicker.tsx`: pass `state.steps` as items, `removeStep` and `reorderSteps` from hook as handlers; render list alongside `StepPickerOverlay` in a two-panel layout
- [ ] T015 [US4] Add Confirm button to `VisualStepPicker.tsx`: on click, map `state.steps` to `StepEntry[]` using conversion table in `data-model.md`, pass to `onConfirm(steps)` prop, close modal (FR-012)
- [ ] T016 [US4] Add Cancel button to `VisualStepPicker.tsx`: on click, call `onCancel()` prop and close modal without passing any steps (FR-013)
- [ ] T017 [US4] Handle error state in `VisualStepPicker.tsx`: when `status === 'error'`, show `state.errorMessage` with a retry button that calls `recapture()`; for re-capture failure mid-session, show inline error notice but keep previous screenshot/overlays and unblock input (Edge Case in spec)

**Checkpoint**: User Stories 1 and 4 together deliver the MVP — image-tap recording with a functional review list, confirm, and cancel.

---

## Phase 5: User Story 2 — Key Input Recording (Priority: P2)

**Goal**: While the picker is focused, pressing any key appends a `KeyInput` step showing a human-readable key label.

**Independent Test**: Open picker → press "Enter", "Escape", and an arrow key → verify three `KeyInput` steps are added with correct ADB key identifiers; confirm → verify all three steps appended to command.

- [ ] T018 [US2] Create ADB key code lookup table as a constant in `src/web-ui/src/components/commands/VisualStepPicker/keyCodeMap.ts`: maps `event.code` → ADB key name (cover: alpha A–Z, digits 0–9, Enter, Escape, Backspace, ArrowUp/Down/Left/Right, Space, Tab, F1–F12; unknown codes fall back to `event.code` value)
- [ ] T019 [US2] Add `keydown` event handler to the focusable wrapper `div[tabIndex=0]` in `VisualStepPicker.tsx`: call `event.preventDefault()` on every event (suppress browser defaults), map `event.code` via `keyCodeMap`, call `recordKey(adbKey)` from hook (FR-007, FR-007b)
- [ ] T020 [US2] Implement `recordKey(key: string)` in `usePickerState.ts`: append `RecordedKeyInputStep` with `key` and `label: "Key: <key>"` to `state.steps`; guard with `if (status !== 'ready') return`
- [ ] T021 [US2] Ensure picker `div` receives focus automatically on open in `VisualStepPicker.tsx` using `autoFocus` or a `useEffect` ref focus call so key events are captured without requiring the user to click first

**Checkpoint**: All three action types (tap, key, swipe-stub) can be recorded; US1 + US2 + US4 together are a shippable recorder for tap+key commands.

---

## Phase 6: User Story 3 — Swipe Recording (Priority: P3)

**Goal**: A click-drag gesture on the screenshot appends a `Swipe` step with start/end coordinates and duration.

**Independent Test**: Open picker → press and drag across the screenshot by more than 10px → verify a `Swipe` step is appended with non-identical start/end coords and a positive duration; drag less than 10px → verify it is treated as a tap, not a swipe.

- [ ] T022 [US3] Replace the Phase 3 `onClick`-based handler in `StepPickerOverlay` with `onMouseDown` + `onMouseUp`: `mousedown` records `gestureStart = { x: naturalX, y: naturalY, timestamp: event.timeStamp }` in component state; `mouseup` scales end coordinates, computes elapsed time, then calls the existing `onGesture(start, end, durationMs)` prop (same signature as T008 — no prop change needed)
- [ ] T022b [US3] Create pure utility function `calcGestureDisplacement(start: Point, end: Point): number` (Euclidean distance in natural image coords) in `src/web-ui/src/components/commands/VisualStepPicker/usePickerState.ts`; add Jest unit tests in `src/web-ui/src/components/commands/VisualStepPicker/__tests__/usePickerState.test.ts`: verify the return value equals `sqrt((end.x-start.x)²+(end.y-start.y)²)`; verify ≥10px input produces a result ≥10; verify <10px input produces a result <10; T023 imports and calls this function
- [ ] T023 [US3] Update the `onGesture` handler in `VisualStepPicker.tsx` to import `calcGestureDisplacement` from `usePickerState.ts` and dispatch `recordSwipe` when the result is ≥ 10px, or `recordTap(end.x, end.y)` when < 10px (FR-008, FR-009)
- [ ] T024 [US3] Implement `recordSwipe(startX, startY, endX, endY, durationMs)` in `usePickerState.ts`: append `RecordedSwipeStep` with all five values and `label: "Swipe (startX,startY)→(endX,endY) <durationMs>ms"`; guard with `if (status !== 'ready') return`
- [ ] T025 [US3] Remove the fallback `onClick` handler from `StepPickerOverlay` that was added in T008 (now fully replaced by the mousedown/mouseup handler from T022); verify tap-from-click still works end-to-end via the <10px displacement path through `onGesture`

**Checkpoint**: All three primitive action types fully recordable in a single picker session (SC-005).

---

## Phase 7: Polish & Cross-Cutting Concerns

- [ ] T026 [P] Verify `POST /api/images/detect-all` returns results within 1 second for a representative set of reference images (SC-006); add a perf note to the PR if the hot path was touched
- [ ] T027 [P] Review all new React components for consistent styling with the existing command editor (modal dimensions, button labels, colour tokens); verify `VisualStepPicker` matches the `EmulatorCaptureCropper` modal pattern
- [ ] T028 Run `vite build` and `jest` in `src/web-ui` to confirm zero regressions; run `dotnet build` + backend tests to confirm no regressions

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 completion — **blocks all user stories**
- **US1 (Phase 3)**: Depends on Phase 2
- **US4 (Phase 4)**: Depends on Phase 3 (needs recorded steps to test the list)
- **US2 (Phase 5)**: T018 (`keyCodeMap.ts`) can start after Phase 2 in parallel with Phase 3; T019, T020, T021 must wait for Phases 3 **and** 4 to complete (they edit `VisualStepPicker.tsx` and `usePickerState.ts` that those phases are still building)
- **US3 (Phase 6)**: Depends on Phase 3 (extends gesture handler in StepPickerOverlay)
- **Polish (Phase 7)**: Depends on all user story phases

### User Story Dependencies

- **US1 (P1)**: Depends on Foundational — no other story dependency
- **US4 (P1)**: Depends on US1 — requires at least one step-recording action to test independently
- **US2 (P2)**: T018 depends on Foundational only; T019/T020/T021 depend on US1 (Phase 3) and US4 (Phase 4) completing first to avoid file conflicts on `VisualStepPicker.tsx` and `usePickerState.ts`
- **US3 (P3)**: Depends on US1 — extends the gesture handler introduced in T008/T022

### Within Each Phase

- Models/types before services (T001 before T004/T005)
- Backend endpoint (T003) before frontend service function (T004) — integration path
- Hook skeleton (T005) before individual `record*` implementations
- Overlay render (T007) before click handler (T008) before recordTap logic (T009)

### Parallel Opportunities

- T001 and T002 can run together (different files)
- T004 and T005 can run in parallel after T003 is done (different files)
- T018 (`keyCodeMap.ts`) can be written any time after Phase 1 — new file, no conflicts
- Within Phase 6: T022b → T023 (imports `calcGestureDisplacement`) → T024 must run sequentially; only T022 is independent
- T026 and T027 in Phase 7 can run in parallel

---

## Parallel Example: Phase 2

```
After T003 (backend endpoint) and T001 (types) are done:
  → T004: detectAll() service function   (images.ts)
  → T005: usePickerState hook skeleton   (usePickerState.ts)
  Both in parallel — different files, no cross-dependency.
Then T006 (CommandForm entry point) uses both.
```

---

## Implementation Strategy

### MVP First (US1 + US4)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational — backend endpoint live, modal opens
3. Complete Phase 3: US1 — tap steps recorded with image ID + offset
4. Complete Phase 4: US4 — step list, delete, reorder, confirm, cancel
5. **STOP and VALIDATE**: Full tap-recording workflow end-to-end
6. Ship / demo MVP

### Incremental Delivery

1. Setup + Foundational → picker modal opens, backend returns matches
2. US1 + US4 → complete tap recorder (**MVP — shippable**)
3. US2 → add key input recording
4. US3 → add swipe recording
5. Polish → perf check, style review, CI green

---

## Notes

- [P] = different files, no incomplete task dependencies — safe to parallelise
- US4 is marked P1 but depends on US1; implement US1 first within the P1 batch
- `event.preventDefault()` on all keydown events is intentional — no keys are reserved (spec clarification)
- Swipe duration uses `event.timeStamp` (DOMHighResTimeStamp), not `Date.now()` — more accurate
- Steps are always appended to the **end** of the command list on confirm (spec clarification)
- Re-capture failure mid-session keeps the previous screenshot and overlays and unblocks input (spec edge case)
