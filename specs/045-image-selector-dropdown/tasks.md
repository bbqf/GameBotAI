# Tasks: Image Selector Dropdown

**Input**: Design documents from `specs/045-image-selector-dropdown/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no blocking dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Exact file paths included in every description

---

## Phase 1: Setup (New Files)

**Purpose**: Create the three new source files that all user stories depend on. T001 and T002 are independent; T003 depends on both.

- [x] T001 [P] Create `src/web-ui/src/components/images/ImageSelectorDropdown.css` — add CSS classes: `.image-selector`, `.image-selector__trigger`, `.image-selector__panel`, `.image-selector__search`, `.image-selector__list`, `.image-selector__option`, `.image-selector__option-thumb`, `.image-selector__option-label`, `.image-selector__loading`, `.image-selector__error`, `.image-selector__empty`, `.image-selector__clear`, `.image-selector__stale-warning`, `.image-thumbnail`, `.image-thumbnail--placeholder`; size `.image-selector__option-thumb img` at 32×32px (recommended miniature size per FR-009)
- [x] T002 [P] Create `src/web-ui/src/components/images/ImageThumbnail.tsx` — implement `ImageThumbnailProps` (`imageId: string`, `className?: string`, `alt?: string`); module-level `thumbnailCache: Map<string, string>`; on mount check cache then call `getImageBlob(imageId)` from `src/web-ui/src/services/images.ts`, create object URL, store in cache; render `<img>` when URL available, `<span className="image-thumbnail--placeholder">` during load or on error; no URL revocation (session cache); add JSDoc to `ImageThumbnailProps` documenting: `imageId` (identifier used to fetch the image blob), error mode (fetch failure silently falls back to placeholder — non-blocking), and cache behaviour (first fetch is stored in module-level cache; subsequent renders of the same `imageId` skip the network call)
- [x] T003 Create `src/web-ui/src/components/images/ImageSelectorDropdown.tsx` — implement `useImageList(active: boolean)` hook that fetches `listImages()` from `src/web-ui/src/services/images.ts` when `active` transitions to `true`, returns `{ images, loading, error, retry }`; implement `ImageSelectorDropdownProps` and `ImageSelectorDropdown` component per plan.md render structure: trigger button showing selected thumbnail+ID or placeholder, panel with search input + list of `<button>` options each containing `<ImageThumbnail>` + ID label, loading/error/empty states, clear button (only when `!required`), stale detection via `onStaleChange`, outside-click close via `useEffect` document listener; import `ImageThumbnail` from `./ImageThumbnail` and CSS from `./ImageSelectorDropdown.css`; add JSDoc to `ImageSelectorDropdownProps` documenting every prop, especially: `onStaleChange` (called with `true` when `value` is non-empty and absent from the fetched list; called with `false` when resolved or cleared), `error` (external validation message rendered as `role="alert"` — distinct from the internal stale warning), and `required` (suppresses the clear button; parent is responsible for wiring a validation error when `onStaleChange(true)` fires)

**Checkpoint**: Three new files exist and compile without errors

---

## Phase 2: Foundational (Component Tests)

**Purpose**: Tests for the new shared components. Both test files are independent. Must pass before call-site integration begins.

**⚠️ CRITICAL**: Constitution requires ≥80% line and ≥70% branch coverage for new modules. These tests are the coverage backstop for the shared component before it is wired up anywhere.

- [x] T004 [P] Write unit tests in `src/web-ui/src/components/images/__tests__/ImageThumbnail.test.tsx` — mock `getImageBlob` from `src/web-ui/src/services/images`; test: (1) renders `<img>` with object URL after blob fetch resolves, (2) renders placeholder while loading, (3) renders placeholder when fetch rejects, (4) uses cached URL on second render of same `imageId` (mock called only once for two renders)
- [x] T005 [P] Write unit tests in `src/web-ui/src/components/images/__tests__/ImageSelectorDropdown.test.tsx` — mock `listImages` from `src/web-ui/src/services/images` and stub `ImageThumbnail` (render `<span data-testid={\`thumb-${id}\`} />`); test all 15 scenarios from plan.md Testing Strategy table: trigger renders placeholder when empty; trigger shows selected ID when value set; panel opens on trigger click; loading indicator while fetching; list renders after resolve; search filters list; onChange called with ID on option click and panel closes; empty state when `[]` returned; error message + retry button on reject; retry re-fetches; clear button present for optional (`required` omitted); no clear button when `required={true}`; `onStaleChange(true)` when value not in loaded list; stale warning text visible; external `error` prop renders as `role="alert"`

**Checkpoint**: All tests in T004 and T005 pass; coverage thresholds met for both new files

---

## Phase 3: User Story 1 — Select Image in Command Form (Priority: P1) 🎯 MVP

**Goal**: Replace the three image ID text inputs in `CommandForm.tsx` (primitive tap, wait-for-image, detection reference) with `ImageSelectorDropdown`.

**Independent Test**: Open a command with a primitive tap step → click the image selector → available images with thumbnails appear → select one → image ID is set → form submits successfully.

- [x] T006 [US1] In `src/web-ui/src/components/commands/CommandForm.tsx`: add `import { ImageSelectorDropdown } from '../images/ImageSelectorDropdown'`; add `const [primitiveTapStale, setPrimitiveTapStale] = useState(false)` state; replace the `<input id="command-primitive-reference">` (LOC-01) with `<ImageSelectorDropdown id="command-primitive-reference" label="Primitive tap image ID" value={pendingPrimitiveReferenceImageId} onChange={setPendingPrimitiveReferenceImageId} required onStaleChange={setPrimitiveTapStale} error={primitiveTapStale ? 'Selected image no longer exists — please choose a valid image' : undefined} disabled={submitting || loading} />`; add `primitiveTapStale` to the submit-disabled condition
- [x] T007 [US1] In `src/web-ui/src/components/commands/CommandForm.tsx`: replace the `<input id="command-wait-reference">` (LOC-02) with `<ImageSelectorDropdown id="command-wait-reference" label="Wait image ID" value={pendingWaitReferenceImageId} onChange={(id) => { setPendingWaitReferenceImageId(id); if (!id) setPendingWaitConfidence(''); }} disabled={submitting || loading} />`
- [x] T008 [US1] In `src/web-ui/src/components/commands/CommandForm.tsx`: replace the `<input id="command-detection-reference">` (LOC-03) with `<ImageSelectorDropdown id="command-detection-reference" label="Reference image ID" value={value.detection?.referenceImageId ?? ''} onChange={(id) => onChange({ ...value, detection: { ...(value.detection ?? {}), referenceImageId: id } })} disabled={submitting} />`; preserve the existing "leave blank to skip detection" hint text below the selector; note: this field reads/writes through the form's `value`/`onChange` props directly (no local state variable), and is disabled by `submitting` only (not `loading`)

**Checkpoint**: User Story 1 complete — command form image fields use the dropdown with thumbnails; LOC-01 blocks submit on stale reference

---

## Phase 4: User Story 2 — Select Image in Sequence Step Conditions (Priority: P1)

**Goal**: Replace the four image ID text inputs in `SequencesPage.tsx` (inline wait, inline image-visible, add-step modal, edit-step form) with `ImageSelectorDropdown`.

**Independent Test**: Open a sequence, edit a step with a wait-for-image or image-visible condition → click the image selector → available images with thumbnails appear → select one → condition's image ID is updated.

- [x] T009 [US2] In `src/web-ui/src/pages/SequencesPage.tsx`: add `import { ImageSelectorDropdown } from '../components/images/ImageSelectorDropdown'`; replace the inline step-editor "Wait image ID" `<input id={`step-wait-image-id-${step.id}`}>` (LOC-04, ~line 706) with `<ImageSelectorDropdown id={`step-wait-image-id-${step.id}`} label="Wait image ID" value={step.waitReferenceImageId} onChange={(id) => { setForm((prev) => ({ ...prev, steps: prev.steps.map((candidate) => candidate.id === step.id ? { ...candidate, waitReferenceImageId: id, waitConfidence: id ? candidate.waitConfidence : '' } : candidate) })); setDirty(true); }} disabled={submitting || loading} />`
- [x] T010 [US2] In `src/web-ui/src/pages/SequencesPage.tsx`: replace the inline step-editor "Image Id" `<input id={`step-image-id-${step.id}`}>` for image-visible condition (LOC-05, ~line 814) with `<ImageSelectorDropdown id={`step-image-id-${step.id}`} label="Image ID" value={step.imageId} onChange={(id) => { setForm((prev) => ({ ...prev, steps: prev.steps.map((candidate) => candidate.id === step.id ? { ...candidate, imageId: id } : candidate) })); setDirty(true); }} disabled={submitting || loading} />`
- [x] T011 [US2] In `src/web-ui/src/pages/SequencesPage.tsx`: replace the add-step modal "Wait image ID" `<input id="sequence-wait-reference">` (LOC-06, ~line 1314) with `<ImageSelectorDropdown id="sequence-wait-reference" label="Wait image ID" value={pendingWaitReferenceImageId} onChange={(id) => { setPendingWaitReferenceImageId(id); if (!id) setPendingWaitConfidence(''); }} />`
- [x] T012 [US2] In `src/web-ui/src/pages/SequencesPage.tsx`: replace the edit-step form "Wait image ID" `<input id="sequence-edit-wait-reference">` (LOC-07, ~line 1571) with `<ImageSelectorDropdown id="sequence-edit-wait-reference" label="Wait image ID" value={pendingWaitReferenceImageId} onChange={(id) => { setPendingWaitReferenceImageId(id); if (!id) setPendingWaitConfidence(''); }} />`

**Checkpoint**: User Stories 1 and 2 complete — all sequence step image fields use the dropdown

---

## Phase 5: User Story 3 — Select Image in Loop and Break Conditions (Priority: P2)

**Goal**: Replace the image ID text inputs in `LoopBlockHeader.tsx` (LOC-08) and `BreakStepRow.tsx` (LOC-09) with `ImageSelectorDropdown`. These two files are independent and can be done in parallel.

**Independent Test**: Open a sequence with a loop block (image-visible exit condition) or a break step → click the image selector → available images with thumbnails appear → select one → condition updates correctly.

- [x] T013 [P] [US3] In `src/web-ui/src/components/sequences/LoopBlockHeader.tsx`: add `import { ImageSelectorDropdown } from '../images/ImageSelectorDropdown'`; replace the `<input data-testid="loop-condition-imageId" placeholder="Image ID">` (LOC-08) with `<ImageSelectorDropdown value={condition.imageId} onChange={(id) => onConditionChange?.({ ...condition, imageId: id })} disabled={disabled} />`; note: this is rendered only inside the `condition?.type === 'imageVisible'` branch where `condition` is guaranteed non-null, so `condition.imageId` is safe; `onConditionChange` uses optional chaining (`?.`) matching the existing source convention
- [x] T014 [P] [US3] In `src/web-ui/src/components/sequences/BreakStepRow.tsx`: add `import { ImageSelectorDropdown } from '../images/ImageSelectorDropdown'`; replace `<input type="text" data-testid="break-image-id" placeholder="Enter image ID">` (LOC-09) with `<ImageSelectorDropdown value={breakCondition.imageId} onChange={(id) => onChange({ ...breakCondition, imageId: id })} disabled={disabled} />`

**Checkpoint**: All 9 LOCs replaced — every image ID text field in the authoring UI is now a dropdown selector with thumbnails

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Audit superseded code, verify lint and test health across all touched files.

- [x] T015 Audit `src/web-ui/src/components/ImagePicker.tsx` — search codebase for imports of `ImagePicker`; if no remaining usages after Phases 3–5, delete the file; if still used, document why it remains alongside `ImageSelectorDropdown`
- [x] T016 Run ESLint on all modified files (`src/web-ui/src/components/images/`, `src/web-ui/src/components/commands/CommandForm.tsx`, `src/web-ui/src/components/sequences/BreakStepRow.tsx`, `src/web-ui/src/components/sequences/LoopBlockHeader.tsx`, `src/web-ui/src/pages/SequencesPage.tsx`); fix any new warnings or errors
- [x] T017 Run full test suite (`npm test` in `src/web-ui/`); verify all tests pass, coverage thresholds met (≥80% line / ≥70% branch for `ImageSelectorDropdown.tsx` and `ImageThumbnail.tsx`); investigate and fix any regressions in existing tests for `CommandForm`, `BreakStepRow`, or `SequencesPage`
- [x] T018 Verify performance of image list load: in `src/web-ui/src/components/images/__tests__/ImageSelectorDropdown.test.tsx` add a timing test that mocks `listImages` with a 50-item array and measures the time from trigger click to all 50 options rendering (use `performance.now()` around `waitFor`); assert total time ≤ 500ms; this validates SC-002 (<10s for 50-image library) and the plan's <500ms p95 goal

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — start immediately; T001 and T002 are parallel, T003 depends on both
- **Phase 2 (Foundational)**: Depends on Phase 1 — T004 and T005 are parallel with each other
- **Phase 3 (US1)**: Depends on Phase 2 — T006 → T007 → T008 sequential (same file)
- **Phase 4 (US2)**: Depends on Phase 2 — T009 → T010 → T011 → T012 sequential (same file); can start in parallel with Phase 3
- **Phase 5 (US3)**: Depends on Phase 2 — T013 and T014 parallel (different files); can start in parallel with Phases 3 and 4
- **Phase 6 (Polish)**: Depends on Phases 3, 4, 5 complete; T018 additionally depends on T005 (appends a timing test to the file T005 creates)

### User Story Dependencies

- **US1 (P1)**: Independent — only requires Phase 2 complete
- **US2 (P1)**: Independent — only requires Phase 2 complete; can run in parallel with US1
- **US3 (P2)**: Independent — only requires Phase 2 complete; can run in parallel with US1 and US2

### Within Each User Story

- Call-site tasks are sequential within the same file (CommandForm, SequencesPage)
- Call-site tasks for different files (LoopBlockHeader, BreakStepRow in US3) are parallel

---

## Parallel Example: Phase 1

```
T001: Create ImageSelectorDropdown.css          T002: Create ImageThumbnail.tsx
      ↓                                               ↓
                        T003: Create ImageSelectorDropdown.tsx
```

## Parallel Example: Phases 3–5 (after Phase 2)

```
Phase 3: T006 → T007 → T008  (CommandForm — sequential)
Phase 4: T009 → T010 → T011 → T012  (SequencesPage — sequential)
Phase 5: T013 ‖ T014  (LoopBlockHeader + BreakStepRow — parallel)
```

---

## Implementation Strategy

### MVP (User Story 1 Only)

1. Complete Phase 1: Create the three new files
2. Complete Phase 2: Tests passing for new components
3. Complete Phase 3: Wire CommandForm (T006–T008)
4. **STOP and VALIDATE**: Open a command, use the image selector for primitive tap — confirm thumbnails appear, selection works, stale detection blocks save
5. Demo to stakeholders if desired

### Incremental Delivery

1. Phase 1 + Phase 2 → component exists and tested
2. Phase 3 (US1) → command authoring improved
3. Phase 4 (US2) → sequence authoring improved
4. Phase 5 (US3) → loop/break authoring improved
5. Phase 6 → cleanup and final validation

---

## Notes

- No new npm dependencies — uses only existing `services/images.ts` functions
- All new method/function names use CamelCase (no underscores) per constitution
- Keep component functions ≤50 LOC; split into `useImageList` + `ImageSelectorDropdown` + `ImageThumbnail` to stay within limit
- `data-testid` attributes on key elements (`image-selector-trigger`, `image-selector-panel`, `image-selector-option`, `image-selector-clear`) enable targeted test queries
- Module-level `thumbnailCache` in `ImageThumbnail.tsx` is intentionally not cleared — session lifetime is appropriate for the authoring tool use case
