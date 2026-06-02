# Phase 1 Data Model: Execution Log Grid Cleanup

This feature introduces **no new persisted or transport data**. It defines client-side **view-model** shapes that project existing DTOs onto the unified grid. Source DTOs are unchanged (`services/executionLogsApi.ts`).

## Source DTOs (existing, unchanged)

- `ExecutionLogEntryDto` — top-level row: `id`, `timestampUtc`, `executionType`, `finalStatus`, `childCount`, `objectRef.displayNameSnapshot`, `summary`.
- `ExecutionSubtreeResponseDto` — `{ executionId, finalStatus, root: ExecutionTreeNodeDto }` returned by `GET /api/execution-logs/{id}/subtree`.
- `ExecutionTreeNodeDto` — recursive sub-element: `nodeKind`, `executionId?`, `order`, `label`, `status`, `message?`, `appliedDelayMs?`, `commandName?`, `detailAttributes?`, `conditionTrace?`, `deepLink?` (no longer rendered), `children[]`.

## View-Model: GridRow

A normalized projection used by the recursive row renderer.

| Field | Type | Source (top-level) | Source (sub-element node) |
|-------|------|--------------------|---------------------------|
| `key` | string | `entry.id` | `parentKey + '/' + order` (or `executionId` when present) |
| `depth` | number | `0` | parent depth + 1 |
| `expandable` | boolean | `executionType === 'sequence' || childCount > 0` | `children.length > 0` |
| `timestamp` | string | formatted `timestampUtc` (exact/relative) | `''` (blank, FR-013) |
| `name` | string | `objectRef.displayNameSnapshot` | `label` |
| `type` | string | display-cased `executionType` ("Sequence"/"Command") | `typeLabel(nodeKind)` |
| `status` | string | `finalStatus` | `status` |
| `info` | string | `summary` | `message` + appended delay/condition/wait detail |
| `children` | GridRow[] | resolved from cached subtree root's children (when expanded) | `node.children` |

### `typeLabel(nodeKind)` map

`sequence → Sequence`, `command → Command`, `step → Step`, `condition → Condition`, `loop → Loop`, `loopIteration → Iteration`, `wait → Wait`, `tap → Tap`. Unknown kinds fall back to the raw value.

### `info` composition for sub-element nodes

Concatenate the available pieces (matching what the former detail panel showed), e.g.:
- base: `message`
- if `appliedDelayMs` is a number: append `(delay {appliedDelayMs} ms)`
- if `conditionTrace`: append `Condition: final result {true|false} ({selectedBranch} branch)`
- if `detailAttributes`: append `Wait: timeout {timeoutMs|n/a}; exit {formatExitCondition(exitCondition)}`

## Component State (client-only)

| State | Type | Purpose | Change vs. today |
|-------|------|---------|------------------|
| `items` | `ExecutionLogEntryDto[]` | top-level rows | unchanged |
| `subtrees` | `Record<string, ExecutionSubtreeResponseDto>` | cached fetched subtrees per top-level id | unchanged |
| `expandedKeys` | `Set<string>` | which rows/nodes are expanded (independent) | **replaces** single `expandedId` |
| `loadingSubtreeId` / `subtreeError` | string / string | subtree fetch status | unchanged |
| sort/filter/timestamp-mode | as today | preserved | unchanged |
| `nextPageToken` | string | pagination hint | unchanged |
| ~~`detail`, `selectedId`, `loadingDetail`, `detailError`, `showPhoneDetail`~~ | — | detail panel | **removed** |

## Validation / Invariants

- A row exposes an expand control **iff** `expandable` is true; non-container nodes (e.g. `tap`) never show one (FR-007).
- Toggling `expandedKeys` for one key must not modify others (FR-007a / SC-008).
- A top-level row's subtree is fetched at most once and reused; nested expansion uses the cached tree with no new fetch (Decision 3).
- Status text is taken verbatim from the source DTO (FR-010).

## State Transitions (expansion)

```
collapsed --click expand--> expanding (top-level only: fetch subtree if not cached)
expanding --subtree ready--> expanded (children rendered as grid rows)
expanded  --click collapse--> collapsed (children hidden; cache + other rows' state retained)
```

Nested-node expand/collapse transitions are immediate (no fetch), since the full tree is already cached.
