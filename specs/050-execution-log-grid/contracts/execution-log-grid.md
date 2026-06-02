# UI Contract: Execution Logs Grid

This feature changes only the **frontend presentation**. There is **no API contract change** — the page consumes the existing endpoints unchanged:

- `GET /api/execution-logs` → `ExecutionLogListResponseDto` (top-level rows)
- `GET /api/execution-logs/{id}/subtree` → `ExecutionSubtreeResponseDto` (full recursive sub-element tree)
- `GET /api/execution-logs/{id}` (detail) → **no longer called by this page** (endpoint remains available)

The contract below specifies the rendered UI structure that tests assert against.

## Grid structure

A single full-width grid (`role="table"`, accessible label "Execution logs") that occupies the entire content width. No separate "Execution Detail" region exists in the DOM.

### Columns (identical at every level)

| # | Header | Content |
|---|--------|---------|
| 1 | (expand) | Expand/collapse button when the row is expandable; empty cell otherwise |
| 2 | Timestamp | Formatted time for top-level rows; blank for sub-element rows |
| 3 | Name | Execution object name (top-level) or node label (sub-element) |
| 4 | Type | "Sequence" / "Command" (top-level); mapped node-kind label (sub-element) |
| 5 | Status | `running` / `success` / `failure` / `skipped` / `not_executed` |
| 6 | Additional information | Summary (top-level) or composed message+detail (sub-element) |

## Expand control contract

- Rendered only when the row is expandable (top-level: sequence or `childCount > 0`; node: has children).
- Is a `<button>` with `aria-expanded={true|false}` and an accessible name: `Expand sub-elements` when collapsed, `Collapse sub-elements` when expanded.
- Clicking toggles **only that row's** expansion (independent state). Multiple rows may be expanded at once.
- Clicking a top-level expand for the first time triggers exactly one `getExecutionSubtree(id)` call; subsequent toggles of that row or any of its descendants trigger no further calls.

## Visibility rules (assertable)

- Collapsed top-level row: its sub-elements are **not** in the DOM.
- Expanded sequence: its direct step/command children are visible; a command child's **own** children (e.g. a tap) become visible **only after** that command child is also expanded.
- Non-container nodes (e.g. `tap`) render no expand control.
- No element with the text "Open in sequence" exists anywhere on the page.

## Preserved behaviors

- Filtering inputs (timestamp, object name, status), sort toggles (timestamp/objectName/status), and timestamp display mode (exact/relative) operate over the top-level grid as before.
- While any visible execution has `finalStatus === 'running'`, the list re-fetches every ~2 s and any expanded subtree is refreshed, updating nested rows live without manual reload.
- Empty state: when no rows match filters, an empty-state message is shown instead of grid rows.

## Narrow-width contract

- On narrow viewports the grid does **not** switch to a separate detail screen and does **not** hide columns; the grid container scrolls horizontally, preserving all six columns at every level.
