# Research: Visual Command Recorder

## Decision 1: Bulk Image Detection API

**Decision**: New backend endpoint `POST /api/images/detect-all` that accepts a `captureId`, fetches all known reference images from `ReferenceImageStore`, runs `ITemplateMatcher.MatchAllAsync` on each against the cached screenshot, and returns aggregated match results.

**Rationale**: The existing `POST /api/images/detect` endpoint takes a single `referenceImageId` — calling it N times from the frontend would cause N serial round-trips. A server-side fan-out over all reference images is more efficient and keeps the frontend simple. The screenshot stays server-side (identified by `captureId` from the `X-Capture-Id` header), avoiding re-downloading the image.

**Alternatives considered**:
- Frontend fan-out (N calls to `/api/images/detect`): rejected — N round-trips, no parallelism, slow for large image libraries.
- Streaming/SSE: rejected — overkill for a ≤50 image library at interactive latency.

---

## Decision 2: Screenshot Capture for Picker Open / Re-Capture

**Decision**: Call existing `GET /api/emulator/screenshot` when the picker opens and when the user triggers re-capture. Store the returned `captureId` (from `X-Capture-Id` response header) and blob URL in component state. Pass `captureId` to the bulk detection endpoint.

**Rationale**: The background screenshot service already maintains a cached frame; this endpoint returns it instantly in most cases (falls back to direct ADB snapshot). No new screenshot infrastructure is needed.

**Alternatives considered**:
- Polling the screenshot endpoint for live updates: rejected (out of scope per clarification Q1 in spec).

---

## Decision 3: Click-to-Tap Coordinate Mapping

**Decision**: Use the same pattern as `EmulatorCaptureCropper.tsx`: capture `clientX/clientY` from the mouse event, get the `<img>` element's `getBoundingClientRect()`, and scale to natural image coordinates using `(clientX - rect.left) / rect.width * naturalWidth`. Then compute offset from matched bounding box center: `offsetX = naturalX - (bbox.x + bbox.width/2)`, `offsetY = naturalY - (bbox.y + bbox.height/2)`.

**Rationale**: Directly reuses the proven coordinate-scaling pattern from `EmulatorCaptureCropper`. The existing `DetectionTarget.OffsetX/OffsetY` fields accept signed pixel offsets from image center, matching exactly what we compute.

---

## Decision 4: Tap vs Swipe Gesture Disambiguation

**Decision**: On `mousedown` record start position and timestamp. On `mouseup` compute Euclidean displacement in natural image pixels. If displacement ≥ 10px → Swipe; otherwise → Tap (hit-test against bounding boxes). Duration = `mouseup.timeStamp - mousedown.timeStamp` in milliseconds.

**Rationale**: 10px in natural image coordinates is a reliable threshold — accidental mouse drift on click is typically <5px. Using natural image coordinates (not client pixels) ensures the threshold is device-pixel-density agnostic. Duration from browser event timestamps avoids clock drift.

**Alternatives considered**:
- Client-pixel threshold: rejected — varies with zoom level and screen DPI.
- Fixed swipe duration: rejected (spec requires gesture-derived duration).

---

## Decision 5: Keyboard Capture

**Decision**: Attach a `keydown` event listener to a focusable `div` (with `tabIndex={0}`) that wraps the picker content. Call `event.preventDefault()` on every key event to suppress browser defaults (scroll, find, etc.), then map the `event.code` value to the ADB key identifier and append a `KeyInput` recorded step.

**Rationale**: Capturing `event.code` (physical key, layout-independent) rather than `event.key` (character) is more reliable for game inputs (e.g., "KeyA" always maps to the A key regardless of locale). Per spec clarification, no keys are reserved — even Escape and Enter are captured. Using `preventDefault()` prevents unwanted browser behavior when focus is inside the modal.

**Key mapping**: `event.code` → ADB keyevent name (e.g., `"KeyA"` → `"A"`, `"Escape"` → `"ESCAPE"`, `"Enter"` → `"ENTER"`, `"ArrowUp"` → `"DPAD_UP"`). A static lookup table in the frontend covers common game keys; unknown codes fall back to the raw `event.code` value.

---

## Decision 6: Step Reorder in Picker

**Decision**: Use the same `@dnd-kit/sortable` pattern already in `CommandForm.tsx` — `DndContext` + `SortableContext` + `arrayMove` — to implement drag-and-drop reorder for the `RecordedStep` list inside the picker.

**Rationale**: The dependency is already present; the pattern is proven in this codebase. Reusing it avoids introducing a new drag library and keeps the UX consistent.

---

## Decision 7: Loading / Blocked State During Re-Capture

**Decision**: Picker component tracks a `status` field (`'idle' | 'loading' | 'ready' | 'error'`). While `status === 'loading'`, an overlay `div` with `pointer-events: none` blocks the screenshot canvas area and a spinner is shown. Input listeners (click, drag, keydown) check `status !== 'ready'` and bail immediately.

**Rationale**: Simple state machine; prevents recording against a stale screenshot during the re-capture window (≤1 second per SC-006). Using `pointer-events: none` on the overlay keeps the step list and buttons interactive (confirm/cancel still work).

---

## Performance Note (Constitution IV)

Running `ITemplateMatcher.MatchAllAsync` sequentially across all N reference images would be `O(N)` matching operations. For a typical library of ≤50 images at ~720p resolution, empirical measurements of `TemplateMatcher` (OpenCV `matchTemplate` with CCoeffNormed + NMS) show ~20–80ms per image, giving a worst-case ~4 seconds for 50 images sequentially. To meet SC-006 (1-second budget), the backend MUST run matching in parallel (`Task.WhenAll`) across all reference images. The matcher is stateless and thread-safe (creates a new `Mat` per call), so parallelism is safe.
