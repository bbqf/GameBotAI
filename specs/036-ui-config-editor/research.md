# Research: UI Configuration Editor

**Feature**: 035-ui-config-editor
**Date**: 2026-04-14

## Research Tasks

### R1: Backend parameter update mechanism

**Question**: How should the backend accept parameter value updates given the existing `ConfigSnapshotService` architecture?

**Decision**: Add `UpdateParametersAsync(Dictionary<string, string?> updates)` to `IConfigSnapshotService`. This method will:
1. Load the saved config from `data/config/config.json` (existing `LoadSavedConfigAsync`).
2. Merge the incoming updates into the saved dictionary (overwriting existing keys, adding new ones for Default→File promotion).
3. Call `RefreshAsync()` which already handles merge-with-precedence, apply, and persist.

**Rationale**: Reuses the existing persistence pipeline (`PersistSnapshotAsync`) and apply pipeline (`IConfigApplier.Apply`). The saved config file already stores parameters as a flat `{ "parameters": { "KEY": { "value": ... } } }` structure — we write directly into this layer. Environment-sourced parameters cannot be overridden by this mechanism (they always win in `MergeWithPrecedence`), which matches FR-004.

**Alternatives considered**:
- Direct file manipulation from the endpoint: Rejected — duplicates persistence logic, bypasses `_gate` semaphore, skips `Apply`.
- New standalone config writer service: Rejected — unnecessary abstraction over a single method addition.

### R2: Parameter reorder persistence strategy

**Question**: How should parameter ordering be persisted so the JSON file key order determines UI display order?

**Decision**: Persist reorder by rewriting the saved config file with keys in the requested order. Implementation:
1. Add `ReorderParametersAsync(string[] orderedKeys)` to `IConfigSnapshotService`.
2. Load saved config, then rebuild a new `OrderedDictionary` / `Dictionary` inserting keys in the requested order.
3. Keys not in the requested list are appended at the end (handles new parameters added between loads).
4. Persist the rewritten file and refresh.

**Rationale**: The `PersistSnapshotAsync` already writes the full snapshot with `JsonSerializer.Serialize` which preserves `Dictionary` insertion order in System.Text.Json. By controlling insertion order in the `Parameters` dictionary, we control JSON key order, which controls UI display order. No schema changes needed.

**Alternatives considered**:
- Separate `order.json` file: Rejected — adds a new file to manage, sync issues between order and config.
- Array of ordered key names stored as a top-level property: Rejected — works but adds complexity; dictionary insertion order is simpler and already naturally preserved by the serializer.

### R3: Frontend drag-and-drop without external libraries

**Question**: Can HTML5 native Drag and Drop API handle parameter list reordering adequately?

**Decision**: Yes — use HTML5 `draggable`, `ondragstart`, `ondragover`, `ondrop` events on list rows. The parameter list is a flat, non-nested list of 30-100 items, which is well within what native DnD handles.

**Rationale**: The web-ui has zero runtime dependencies beyond React and React DOM. Adding `@dnd-kit/core` (~20 KB) or `react-beautiful-dnd` (deprecated, ~40 KB) for a single feature is disproportionate. HTML5 DnD is supported across all target browsers (Chrome/Edge on Windows). Touch support is not required (operators use desktop browsers).

**Alternatives considered**:
- `@dnd-kit/core`: Well-maintained, good React integration — but adds a dependency for one feature. Could be added later if accessibility requirements emerge.
- `react-beautiful-dnd`: Deprecated by Atlassian. Rejected.
- CSS `sort` with mouse move events: More work than native DnD, no advantage.

### R4: Batch update API design

**Question**: What request/response shape should the "Apply All" endpoint use?

**Decision**: `PUT /api/config/parameters` with body:
```json
{
  "updates": {
    "GAMEBOT_TESSERACT_LANG": "deu",
    "Logging__LogLevel__Default": "Debug",
    "Service__Detections__Threshold": "0.9"
  }
}
```
All values are sent as strings (matching FR-017: plain text inputs). The backend parses them back to appropriate types during Apply. Response: the refreshed `ConfigurationSnapshot`.

**Rationale**: Matches the existing pattern where `ConfigSnapshotService.RefreshAsync()` returns a full snapshot. The UI can replace its entire local state with the response, ensuring consistency. String values avoid type-mismatch issues between JSON number/bool and the backend's `ExtractJsonValue`.

**Alternatives considered**:
- PATCH with JSON Patch (RFC 6902): Over-engineered for key-value updates.
- POST with form-encoded body: Inconsistent with existing JSON API patterns.
- Individual PUT per parameter: Violates FR-006 (single global Apply All) and creates N+1 API calls.

### R5: Reorder API design

**Question**: What request shape should the reorder endpoint use?

**Decision**: `PUT /api/config/parameters/reorder` with body:
```json
{
  "orderedKeys": ["Logging__LogLevel__Default", "GAMEBOT_TESSERACT_ENABLED", ...]
}
```
Response: the refreshed `ConfigurationSnapshot` with parameters in the new order.

**Rationale**: Sending the full key list ensures the backend has explicit control over ordering. The backend appends any keys not in the list (handles race conditions where new params appear). This is simpler than sending relative moves (e.g., "move X before Y") which require complex conflict resolution.

**Alternatives considered**:
- Relative move operations (`{ "key": "X", "before": "Y" }`): More granular but complex, race-prone, and harder to implement atomically.
- Index-based (`{ "key": "X", "index": 0 }`): Fragile with concurrent changes.

### R6: Secret parameter editing

**Question**: How should secret parameters be handled during editing if the UI only sees masked values?

**Decision**: When a secret parameter is displayed, its value shows "•••" (masked). If the operator edits it and submits a new value, that value is sent as plain text in the PUT request body. The backend stores it and masks it again in the response. If the operator does NOT edit a secret parameter (leaves "•••" unchanged), the frontend excludes it from the `updates` dictionary — so the backend preserves the existing stored value.

**Rationale**: This avoids the backend needing to distinguish between "user wants to set value to literal '•••'" vs "user didn't change it". The frontend simply tracks dirty state per row and only includes changed rows in the update payload.

**Alternatives considered**:
- Send a sentinel value like `"__UNCHANGED__"`: Fragile if someone wants that literal value.
- Always send all values: Would overwrite secrets with the masked string.

### R7: Collapsible section implementation

**Question**: What HTML/React pattern should the collapsible "Backend Connection" section use?

**Decision**: Use native HTML5 `<details>` / `<summary>` elements. These provide built-in expand/collapse with no JavaScript, are accessible (keyboard and screen reader support), and style-able with CSS.

**Rationale**: Zero JS overhead. Matches the project's convention of minimal dependencies. The `<details>` element defaults to collapsed (no `open` attribute), matching FR-009. A thin React wrapper component (`CollapsibleSection`) provides consistent styling.

**Alternatives considered**:
- React state + CSS transition: More code, less accessible by default.
- Third-party accordion component: Adds dependency.

## Summary

All research tasks resolved. No NEEDS CLARIFICATION items remain. Key decisions:
- Backend: Extend `ConfigSnapshotService` with `UpdateParametersAsync` and `ReorderParametersAsync`; two new PUT endpoints.
- Frontend: HTML5 native DnD, `<details>`/`<summary>` for collapsible, plain text inputs, local dirty-state tracking.
- No new packages (npm or NuGet).
