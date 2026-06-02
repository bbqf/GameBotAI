# Phase 0 Research: Execution Log Grid Cleanup

No `NEEDS CLARIFICATION` markers remained after `/speckit-clarify` (expansion model and narrow-width strategy were resolved). The decisions below capture the technical approach for the refactor.

## Decision 1: Render the whole page as one recursive tree grid

- **Decision**: Use a single table/grid with a recursive row renderer. Top-level entries (`ExecutionLogEntryDto`) and nested sub-elements (`ExecutionTreeNodeDto`) are both projected onto one shared row view-model and rendered by the same recursive component. Indentation conveys depth (reuse the existing `depth * 1.25rem` padding approach).
- **Rationale**: The spec requires identical columns "at all levels". A shared row view-model + recursive renderer guarantees column consistency and avoids duplicate markup for list rows vs. tree nodes. The data is already hierarchical (`children` on each node).
- **Alternatives considered**:
  - Keep separate list-row and tree-node renderers (current code) — rejected: drifts column layout, duplicates logic, and the spec explicitly wants one grid model everywhere.
  - A third-party tree-grid/data-grid library — rejected: adds a dependency for a modest, bespoke layout; constitution favors dependency hygiene.

## Decision 2: Expansion state — per-row independent, keyed by stable node id

- **Decision**: Replace the single `expandedId: string` with a `Set<string>` of expanded node keys. Top-level rows key on `execution.id`; nested nodes key on a stable path-based key (`parentKey + '/' + order` or `executionId` when present). Toggling one key never touches others (satisfies FR-007a / SC-008).
- **Rationale**: Clarification chose "Multiple independent". A Set of expanded keys is the simplest model that lets any number of rows/branches stay open simultaneously and survives re-renders/live polling.
- **Alternatives considered**:
  - Single `expandedId` (current) — rejected: only one branch open at a time; contradicts the clarification.
  - Storing `expanded` flags inside the fetched tree data — rejected: mutating fetched DTOs is fragile across live-poll refreshes; external keyed state is cleaner and refresh-stable.

## Decision 3: Fetch subtrees once per top-level row; expand nested nodes client-side

- **Decision**: Keep lazy-loading the subtree via `getExecutionSubtree(id)` when a top-level row is first expanded, cached in `subtrees[id]`. Because the subtree endpoint already returns the **full** recursive tree, expanding nested nodes (command step → command sub-elements, nested sequences) is pure client-side collapse state with **no additional network calls**.
- **Rationale**: Matches the existing data contract and the performance goal (no N+1 fetches per toggle). Live polling continues to refresh expanded subtrees.
- **Alternatives considered**:
  - Per-node lazy fetch — rejected: unnecessary; full tree is already available in one response.
  - Eagerly fetch all subtrees on list load — rejected: wasteful for collapsed rows and large pages.

## Decision 4: Column projection / "Additional information" source

- **Decision**: Project each row to `{ expandable, timestamp, name, type, status, info }`:
  - Top-level: `timestamp = timestampUtc` (formatted via existing exact/relative modes), `name = objectRef.displayNameSnapshot`, `type = executionType` (display-cased: "Sequence"/"Command"), `status = finalStatus`, `info = summary`.
  - Node: `timestamp = ''` (blank, FR-013), `name = label`, `type = nodeKind` mapped to a label (sequence→Sequence, command→Command, step→Step, condition→Condition, loop→Loop, loopIteration→Iteration, wait→Wait, tap→Tap), `status = status`, `info = message` plus appended applied-delay / condition / wait detail text reused from the former detail panel.
- **Rationale**: All fields already exist in the DTOs; this reorganizes presentation only (no new captured data per spec assumptions). Condition/wait detail strings reuse the existing `formatExitCondition` and condition-trace formatting from the current detail/tree code.
- **Alternatives considered**: Showing raw `nodeKind` strings — rejected: less readable; a small display-label map is consistent with UX principle.

## Decision 5: Narrow-width handling — horizontal scroll

- **Decision**: Drop the `useNavigationCollapse`-driven phone/desktop layout switch and the separate phone detail screen. Wrap the grid in a horizontally scrollable container; keep all six columns at every width with sensible min-widths.
- **Rationale**: Clarification chose horizontal scroll, keeping one consistent grid model on all widths and losing no information.
- **Alternatives considered**: Column hiding / card stacking — rejected by clarification.

## Decision 6: Removal scope (dead-code cleanup)

- **Decision**: Remove `renderDetail`, the detail/selection state (`detail`, `selectedId` if only used for the panel, `loadingDetail`, `detailError`, `showPhoneDetail`), the `getExecutionLogDetail` import/effect, the `openAuthoringDeepLink` helper, and all "Open in sequence" buttons (top-level and nested). `executionLogsApi.ts` keeps `getExecutionLogDetail` exported (still part of the API surface) but the page no longer calls it.
- **Rationale**: Constitution I forbids dead code; FR-008 removes deep links; removing the detail panel removes its supporting state.
- **Note**: Row "selection" highlight may be retained purely as a visual affordance or removed if it no longer carries meaning; default is to remove it since it only drove detail loading.

## Testing approach

- Update `ExecutionLogsTree.test.tsx`:
  - Remove the deep-link test (FR-008: no "Open in sequence" button exists).
  - Update the nested-visibility test: after expanding the sequence, the command step is visible; the command's own child (e.g. a tap) becomes visible **only after** expanding the command step (per-node expansion).
  - Add: full-width grid present with the six columns and no detail panel; multiple top-level rows can be expanded simultaneously; additional-info column shows the summary/message text.
- Keep existing filter/sort/live-update behavior covered (assert poll still refreshes an expanded running subtree).
