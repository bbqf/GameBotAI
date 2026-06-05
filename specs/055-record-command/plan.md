# Implementation Plan: Visual Command Recorder

**Branch**: `055-record-command` | **Date**: 2026-06-05 | **Spec**: [spec.md](spec.md)  
**Input**: Feature specification from `specs/055-record-command/spec.md`

## Summary

Add a Visual Step Picker modal to the command editor that lets users record `PrimitiveTap`, `KeyInput`, and `Swipe` steps by interacting with a captured emulator screenshot overlaid with matched image-region bounding boxes, rather than entering coordinates or image IDs manually.

The implementation spans a new backend API endpoint (`POST /api/images/detect-all`) and a new frontend modal component (`VisualStepPicker`) integrated into the existing `CommandForm`.

## Technical Context

**Language/Version**: C# 12 / .NET 8 (backend); TypeScript 5 / React 18 (frontend)  
**Primary Dependencies**: ASP.NET Core minimal APIs, OpenCvSharp (`ITemplateMatcher`), `@dnd-kit/sortable`, `@testing-library/react`, xUnit, Jest  
**Storage**: N/A — no new persistent storage; existing `ReferenceImageStore` and screenshot cache used  
**Testing**: xUnit (backend unit + integration); Jest + @testing-library/react (frontend)  
**Target Platform**: Windows desktop (locally served web UI)  
**Project Type**: Web application — ASP.NET Core backend + React/Vite frontend  
**Performance Goals**: Bulk image matching completes within 1 second for ≤50 reference images (SC-006); parallel `Task.WhenAll` across all images required (see research.md)  
**Constraints**: Single emulator session; captured snapshot model (no streaming); no reserved keyboard shortcuts  
**Scale/Scope**: Typically ≤50 reference images; single concurrent user

## Constitution Check

*GATE: Must pass before proceeding to implementation.*

| Principle | Status | Notes |
|---|---|---|
| I. Code Quality | ✅ Pass | CamelCase methods, modular components, no dead code planned |
| II. Testing | ✅ Pass | Unit tests for offset calc + hit-test logic; API endpoint integration test; React component tests required |
| III. UX Consistency | ✅ Pass | Modal pattern matches existing `EmulatorCaptureCropper`; no reserved keyboard shortcuts per spec |
| IV. Performance | ✅ Pass | SC-006 (1s budget) declared; parallel matching mandated in research.md; perf note required in PR |

No violations. No complexity-tracking entries needed.

## Project Structure

### Documentation (this feature)

```text
specs/055-record-command/
├── plan.md              ← this file
├── research.md          ← Phase 0 output
├── data-model.md        ← Phase 1 output
├── contracts/
│   └── api-detect-all.md
└── tasks.md             ← Phase 2 output (/speckit-tasks)
```

### Source Code

```text
src/GameBot.Service/
├── Endpoints/
│   └── ImageDetectionsEndpoints.cs   ← add DetectAll endpoint here
└── (no new files)

src/web-ui/src/
├── components/
│   └── commands/
│       ├── CommandForm.tsx            ← add "Record" entry point
│       └── VisualStepPicker/
│           ├── VisualStepPicker.tsx   ← new modal component
│           ├── StepPickerOverlay.tsx  ← screenshot + bounding box canvas/img layer
│           ├── RecordedStepList.tsx   ← sortable recorded steps with delete
│           ├── usePickerState.ts      ← state machine hook
│           └── keyCodeMap.ts          ← event.code → ADB key lookup table
├── services/
│   └── images.ts                     ← add detectAll() function
└── types/
    └── picker.ts                      ← new: RecordedStep, ImageMatchResult, PickerState
```

**Structure Decision**: Web application (backend + frontend split). Backend change is additive (one new endpoint in an existing file). Frontend is a new sub-directory under `commands/` to keep picker concerns isolated.

## Phase 0: Research

See [research.md](research.md) — all decisions resolved, no NEEDS CLARIFICATION items remain.

Key decisions:
1. **Bulk detection**: New `POST /api/images/detect-all` endpoint with parallel `Task.WhenAll` across all reference images
2. **Screenshot**: Reuse `GET /api/emulator/screenshot` + `captureId` header pattern
3. **Coordinate mapping**: Same `getBoundingClientRect` + natural-size scaling as `EmulatorCaptureCropper`
4. **Tap/swipe disambiguation**: 10px displacement threshold in natural image coords
5. **Key capture**: `keydown` on focused `div[tabIndex=0]` with `event.preventDefault()`; `event.code` → ADB key map
6. **Step reorder**: `@dnd-kit/sortable` (already a dependency)
7. **Loading state**: `status` state field; `pointer-events: none` overlay during re-capture

## Phase 1: Design & Contracts

### Backend: POST /api/images/detect-all

See [contracts/api-detect-all.md](contracts/api-detect-all.md).

**Implementation location**: `ImageDetectionsEndpoints.cs` — add a new minimal API route alongside the existing `/api/images/detect` handler.

**Logic**:
1. Resolve `captureId` from request body
2. Retrieve cached screenshot `Mat` from `CaptureSessionStore` (same as existing detect endpoint)
3. Enumerate all reference images from `ReferenceImageStore`
4. Run `ITemplateMatcher.MatchAllAsync(screenshotMat, templateMat, config)` in parallel via `Task.WhenAll`
5. Flatten results; map `TemplateMatch` → `DetectAllMatch` (add `imageId`, `imageName`)
6. Return `DetectAllResponse`

**Error paths**: 400 if `captureId` null/empty; 404 if no cached screenshot; 503 if emulator unavailable.

### Frontend: VisualStepPicker modal

**Entry point**: `CommandForm.tsx` — add a "Record steps" button in the step-addition toolbar that opens `<VisualStepPicker>` as a modal. On confirm, call `addStep()` for each returned `RecordedStep` (in order).

**`usePickerState` hook**:
- Manages `PickerState` (see data-model.md)
- `openPicker()`: calls `fetchEmulatorScreenshot()` then `detectAll(captureId)` → sets `status: 'ready'`
- `recapture()`: same flow, blocks input via `status: 'loading'`
- `recordTap(naturalX, naturalY)`: hit-tests `matches` array, picks highest-confidence bbox containing the point, computes offset, appends `RecordedPrimitiveTapStep`
- `recordKey(adbKey: string)`: appends `RecordedKeyInputStep` (mapping from `event.code` → ADB key done in the keydown handler before calling this method)
- `recordSwipe(start, end, durationMs)`: appends `RecordedSwipeStep`
- `removeStep(id)`, `reorderSteps(ids)`: mutate `steps`

**`StepPickerOverlay` component**:
- Renders `<img>` of screenshot (blob URL)
- SVG overlay layer for bounding boxes (labeled `<rect>` + `<text>` elements)
- `onMouseDown` / `onMouseUp` handlers for tap/swipe gesture detection (no `onMouseMove` needed — only start/end points required)
- Accepts `onGesture(start: Point, end: Point, durationMs: number)` prop; `VisualStepPicker` owns the dispatch logic
- While `status === 'loading'`: semi-transparent blocker `div` over the image area

**`VisualStepPicker` component** (outer modal, owns keyboard focus):
- Wrapping `div[tabIndex=0]` with `autoFocus`; `onKeyDown` here captures all keys (not in overlay)
- `keydown` handler calls `event.preventDefault()`, maps `event.code` via `keyCodeMap`, calls `recordKey(adbKey)`

**`RecordedStepList` component**:
- `@dnd-kit/sortable` list of recorded steps
- Each item: type icon + label + delete button
- Drag handle for reorder

### Data Model

See [data-model.md](data-model.md).

New types in `src/web-ui/src/types/picker.ts`: `RecordedStep` (union), `ImageMatchResult`, `PickerState`, `PickerStatus`.

New backend DTOs in `ImageDetectionsEndpoints.cs` or a nearby DTOs file: `DetectAllRequest`, `DetectAllMatch`, `DetectAllResponse`.

### quickstart.md

See [quickstart.md](quickstart.md) — generated below.

## SC-004 Note

**SC-004** ("building a 5-step command using the recorder takes less time than manual entry") is an observational post-ship outcome metric, not a buildable deliverable. No implementation task is required. Validate informally during acceptance testing.

## Performance Budget

| Operation | Target | Approach |
|---|---|---|
| Picker open (screenshot + match all) | ≤ 1000 ms | Parallel `Task.WhenAll` on backend; screenshot from cache |
| Re-capture | ≤ 1000 ms | Same as open |
| Tap hit-test | < 1 ms | Linear scan of ≤50 matches in-browser |
| Key capture latency | < 16 ms | Direct DOM event → state update |
| Overlay render (SVG) | < 50 ms | ≤50 SVG rects, no canvas needed |
