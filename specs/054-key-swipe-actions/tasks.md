# Tasks: Key Input and Swipe Primitive Actions

**Input**: Design documents from `specs/054-key-swipe-actions/`
**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/ ✅

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no dependencies on each other)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)

---

## Phase 1: Setup

**Purpose**: Verify baseline before changes begin.

- [X] T001 Confirm `dotnet build` and `npm run build` (in `src/web-ui`) both pass with no new errors before any changes

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Extend the `CommandStep` type system across backend and frontend. Both US1 and US2 depend on this phase being complete.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T002 [P] Extend `CommandStepType` enum with `KeyInput` and `Swipe`; add `KeyInputConfig` (`Key` string required) and `SwipeConfig` (`StartX`, `StartY`, `EndX`, `EndY` int required; `DurationMs` int optional) classes; add `KeyInput?` and `Swipe?` properties to `CommandStep` class in `src/GameBot.Domain/Commands/CommandStep.cs`
- [X] T003 [P] Extend `CommandStepTypeDto` enum with `KeyInput` and `Swipe`; add `KeyInputConfigDto` and `SwipeConfigDto` mirroring the domain config shapes; add `KeyInput?` and `Swipe?` properties to `CommandStepDto` in `src/GameBot.Service/Models/Commands.cs`
- [X] T004 Update the command step mapper in `src/GameBot.Service/Endpoints/CommandsEndpoints.cs` (or the equivalent command service/mapper class) to handle the two new `CommandStepTypeDto` variants in both directions: incoming (`CommandStepTypeDto.KeyInput` → `CommandStepType.KeyInput` + `KeyInputConfig`; `CommandStepTypeDto.Swipe` → `CommandStepType.Swipe` + `SwipeConfig`) and outgoing (reverse mapping for GET responses) (depends on T002 and T003)
- [X] T005 Add unit tests for the two new `CommandExecutor` execution branches in `tests/unit/Commands/CommandExecutorKeyInputTests.cs` and `tests/unit/Commands/CommandExecutorSwipeTests.cs`: verify a `CommandStep` with `Type=KeyInput` dispatches `InputAction(type="key", args={["key"]=config.Key})` and a step with `Type=Swipe` dispatches `InputAction(type="swipe", args={["x1"]=StartX, ["y1"]=StartY, ["x2"]=EndX, ["y2"]=EndY, ["durationMs"]=DurationMs})` (depends on T002)
- [X] T006 Add `KeyInput` and `Swipe` execution handler branches to the step dispatch chain in `src/GameBot.Service/Services/CommandExecutor.cs`: for KeyInput map `config.Key` → `InputAction(type="key", args={["key"]=config.Key})`; for Swipe map `config.StartX → args["x1"]`, `config.StartY → args["y1"]`, `config.EndX → args["x2"]`, `config.EndY → args["y2"]`, `config.DurationMs → args["durationMs"]` (depends on T002, T003, T004)
- [X] T007 [P] Add `'KeyInput' | 'Swipe'` to the `CommandStepDto.type` union; add `KeyInputConfigDto` and `SwipeConfigDto` TypeScript types in `src/web-ui/src/services/commands.ts`
- [X] T008 [P] Add `'KeyInput' | 'Swipe'` to the `PrimitiveActionType` union; add `<option value="KeyInput">Key Input</option>` and `<option value="Swipe">Swipe</option>` to the dropdown in `src/web-ui/src/components/commands/ActionTypeSelector.tsx`
- [X] T009 Update `StepEntry` type to include `'KeyInput' | 'Swipe'` in the `type` discriminant and add `keyInput?: { key: string }` and `swipe?: { startX: string; startY: string; endX: string; endY: string; durationMs?: string }` properties; add `'KeyInput'` and `'Swipe'` to the `EDITABLE_TYPES` set in `src/web-ui/src/components/commands/CommandForm.tsx` (depends on T008)

**Checkpoint**: Type system, mapper, executor, and tests are complete — both backend and frontend know about `KeyInput` and `Swipe`. The action type selector now shows both options. Panel implementation can begin.

---

## Phase 3: User Story 1 — Add Key Input Step (Priority: P1) 🎯 MVP

**Goal**: User can select "Key Input", enter a key identifier, add it as a step, and edit or delete it.

**Independent Test**: Open the command editor, select "Key Input" from the action type selector, type "Enter" in the key field, click Add — step appears in the step list as `Key: Enter`. Click Edit — panel repopulates with "Enter". Click the step's delete button — step is removed. Confirm the step also supports drag-to-reorder in the list.

### Implementation for User Story 1

- [X] T010 [US1] Create `KeyInputPanel.tsx` in `src/web-ui/src/components/commands/KeyInputPanel.tsx`: single required text field for key identifier (`<input type="text">`), `attempted` state for deferred validation (error shown only after Add is clicked), `initialValue` prop for edit repopulation, `onConfirm(value: { key: string })` / `onCancel` callbacks, `action-panel action-panel--key-input` CSS classes on root div, `action-panel__controls` div with Add/Save (label switches on `initialValue !== undefined`) and Cancel buttons
- [X] T011 [US1] Add `KeyInput` case to `toStepItems` returning `{ label: 'Key: <key>', description: undefined }` (label relies on CSS truncation from `.reorderable-list__label`); add `{pendingActionType === 'KeyInput' && <KeyInputPanel ... />}` rendering block with `handlePanelConfirm` mapping `{ type: 'KeyInput', keyInput: { key } }`; import `KeyInputPanel`; verify the new step appears in `SortableSequenceStepList` with functional edit and delete controls (FR-015) in `src/web-ui/src/components/commands/CommandForm.tsx` (depends on T009, T010)
- [X] T012 [P] [US1] Add `.action-panel--key-input` CSS modifier class rules (base sizing, field spacing consistent with `.action-panel--tap`) to `src/web-ui/src/components/commands/CommandForm.css`

**Checkpoint**: User Story 1 complete — Key Input step can be added, displayed (with truncated label), edited, deleted, and reordered independently of Swipe.

---

## Phase 4: User Story 2 — Add Swipe Step (Priority: P2)

**Goal**: User can select "Swipe", enter start/end coordinates and optional duration, add it as a step, and edit or delete it.

**Independent Test**: Open the command editor, select "Swipe", fill StartX=0, StartY=0, EndX=100, EndY=200, DurationMs=300, click Add — step appears with description `(0,0) → (100,200), 300ms`. Click Add with StartX empty — validation error shown on that field, step not added. Click Edit on the step — panel repopulates all five fields. Confirm the step supports drag-to-reorder in the list.

### Implementation for User Story 2

- [X] T013 [US2] Create `SwipePanel.tsx` in `src/web-ui/src/components/commands/SwipePanel.tsx`: four required integer fields (`StartX`, `StartY`, `EndX`, `EndY` as `<input type="number">`) and one optional integer field (`DurationMs`); `attempted` state for deferred validation (each empty required field shows individual error after Add is clicked); `initialValue` prop for edit repopulation; `onConfirm(value: SwipePanelValue)` / `onCancel` callbacks; `action-panel action-panel--swipe` CSS classes; `action-panel__controls` with Add/Save and Cancel buttons
- [X] T014 [US2] Add `Swipe` case to `toStepItems` returning `{ label: 'Swipe', description: '(startX,startY) → (endX,endY)' }` with optional `, Nms` appended when `durationMs` is set; add `{pendingActionType === 'Swipe' && <SwipePanel ... />}` rendering block with `handlePanelConfirm` mapping `{ type: 'Swipe', swipe: { startX, startY, endX, endY, durationMs } }`; import `SwipePanel`; verify the new step appears in `SortableSequenceStepList` with functional edit and delete controls (FR-015) in `src/web-ui/src/components/commands/CommandForm.tsx` (depends on T011, T013)
- [X] T015 [P] [US2] Add `.action-panel--swipe` CSS modifier class rules to `src/web-ui/src/components/commands/CommandForm.css`

**Checkpoint**: User Story 2 complete — Swipe step can be added, displayed, edited, deleted, and reordered. Both US1 and US2 are independently functional.

---

## Phase 5: User Story 3 — Consistency (Priority: P3)

**Goal**: Key Input and Swipe panels are visually and behaviorally indistinguishable in style from Tap, Wait for Image, and Ensure Game Running.

**Independent Test**: Open the command editor and cycle through all five action types — Tap, Wait for Image, Ensure Game Running, Key Input, Swipe. All panels share the same field layout, button placement, error styling, and font. Switching action type away from Key Input and back resets the panel. Switching away from Swipe and back resets all five fields.

### Implementation for User Story 3

- [X] T016 [US3] Verify `.reorderable-list__label` in `src/web-ui/src/components/commands/CommandForm.css` applies `overflow: hidden; text-overflow: ellipsis; white-space: nowrap`; if not, add those properties so long Key Input key names truncate with an ellipsis to fit the list item width
- [X] T017 [US3] Review `KeyInputPanel.tsx` and `SwipePanel.tsx` against `TapPanel.tsx` for structural consistency: confirm field `<div className="field">` wrappers match, button element ordering in `action-panel__controls` matches (primary action first, Cancel second), error `<div className="field-error" role="alert">` elements are used for validation messages, `disabled` prop is forwarded to all interactive elements, and error messages use actionable text (e.g., "Key identifier is required." not just a red border)

**Checkpoint**: All three user stories complete. All five action types are consistent in appearance and behavior.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T018 [P] Run `dotnet build` and `dotnet format --verify-no-changes` in `src/GameBot.Domain` and `src/GameBot.Service`; confirm no new compiler warnings, errors, or formatting violations from T002–T006 changes
- [X] T019 [P] Run `npm run build` and the project's lint command (e.g., `npm run lint`) in `src/web-ui`; confirm no TypeScript errors or lint violations from T007–T017 changes
- [X] T020 Add automated integration test cases to `tests/integration/PrimitiveAuthoringFlowTests.cs` covering: (1) POST a command with a KeyInput step, GET it back — `type`, `keyInput.key` round-trip correctly; (2) POST a command with a Swipe step, GET it back — `type`, `swipe.startX/Y`, `swipe.endX/Y`, `swipe.durationMs` round-trip correctly (follows existing authoring flow test pattern; validates T004 mapper)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — start immediately
- **Phase 2 (Foundational)**: Depends on Phase 1 — **blocks all user stories**
- **Phase 3 (US1)**: Depends on Phase 2 — can start once foundational is done
- **Phase 4 (US2)**: Depends on Phase 3 — sequentially after US1 (same file: `CommandForm.tsx`)
- **Phase 5 (US3)**: Depends on Phases 3 and 4
- **Phase 6 (Polish)**: Depends on all prior phases

### Within Phase 2 (Foundational)

```
T002 ──┬──► T004 ──► T006
T003 ──┘
T002 ──────► T005
T007  (independent)
T008 ──────► T009
```

T002 and T003 can run in parallel (different files).
T004 waits for T002 + T003; T006 waits for T002, T003, T004.
T005 (tests) waits for T002.
T007 and T008 are independent of the backend tasks and of each other.
T009 waits for T008.

### Within Phase 3 (US1)

```
T010 ──► T011
T012  (parallel — different file)
```

### Within Phase 4 (US2)

```
T013 ──► T014
T015  (parallel — different file)
```

---

## Parallel Execution Example: Phase 2 (Foundational)

```
Parallel batch 1 — all different files, no dependencies:
  T002  src/GameBot.Domain/Commands/CommandStep.cs
  T003  src/GameBot.Service/Models/Commands.cs
  T007  src/web-ui/src/services/commands.ts
  T008  src/web-ui/src/components/commands/ActionTypeSelector.tsx

Sequential after batch 1:
  T004  src/GameBot.Service/Endpoints/CommandsEndpoints.cs  (needs T002 + T003)
  T005  tests/unit/Commands/CommandExecutor*Tests.cs         (needs T002)
  T006  src/GameBot.Service/Services/CommandExecutor.cs      (needs T002, T003, T004)
  T009  src/web-ui/src/components/commands/CommandForm.tsx   (needs T008)
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (including mapper T004 and tests T005)
3. Complete Phase 3: User Story 1 (Key Input)
4. **STOP and VALIDATE**: Add a Key Input step end-to-end — type selector shows "Key Input", panel appears, step saves, reloads, edit/delete/reorder all work
5. Demo if ready before proceeding

### Incremental Delivery

1. Phase 1 + Phase 2 → Type system, mapper, and tests ready
2. Phase 3 → Key Input working (MVP)
3. Phase 4 → Swipe working
4. Phase 5 + Phase 6 → Consistency verified, build clean

---

## Notes

- [P] tasks touch different files and have no blocking dependencies on each other within their phase
- `CommandForm.tsx` is modified in T009 (foundational), T011 (US1), and T014 (US2) — do these sequentially to avoid conflicts
- The backend executor (T006) only needs the `InputAction` model, which already accepts any `type` string + `args` dict — no session-layer changes are needed beyond the mapper (T004) and executor (T006)
- The `args` key names in T006 (`x1`, `y1`, `x2`, `y2`) intentionally differ from the `SwipeConfig` property names (`StartX`, `StartY`, `EndX`, `EndY`) to match the existing `PrimitiveSwipeAction` domain convention — the mapping is explicit in T006
- Swipe and Key domain classes (`PrimitiveKeyAction`, `PrimitiveSwipeAction`) already exist — T002 adds the `CommandStep`-level config classes which are shaped after but distinct from those domain action classes
