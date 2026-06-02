# Phase 1 Data Model: Execution Logs Reflect What Was Actually Executed

**Feature**: 049-execution-logs-hierarchy  
**Date**: 2026-06-01

This feature **reuses** the existing execution-log entities and adds a lifecycle status, populates the already-present hierarchy fields for child executions, and introduces a derived **subtree/tree-node** projection for the UI. No new persisted entity is introduced.

## 1. ExecutionLogEntry (persisted) — modified

Existing record ([ExecutionLogModels.cs](../../src/GameBot.Domain/Logging/ExecutionLogModels.cs)). Changes:

| Field | Type | Change | Notes |
|-------|------|--------|-------|
| `FinalStatus` | string | **Values extended** | Now one of `running` (in-progress), `success`, `failure`. Was only `success`/`failure`. |
| `Hierarchy.RootExecutionId` | string | **Now populated for children** | Child command of a sequence = the sequence root's id. Stand-alone = own id (unchanged). |
| `Hierarchy.ParentExecutionId` | string? | **Now populated for children** | `null` ⇒ top-level (root). Set to the sequence root id for invoked commands. |
| `Hierarchy.Depth` | int | **Now meaningful** | `0` = root (top-level); `1` = command invoked by a sequence; `>1` = nested sequence descendants. |
| `Hierarchy.SequenceIndex` | int? | **Now populated for children** | The owning sequence step's order; used to correlate a child command entry to its root step outcome and to order tree nodes. |
| (all other fields) | — | unchanged | `StepOutcomes`, `Details`, `ObjectRef`, `Navigation`, `RetentionExpiresUtc`, etc. |

**Identity**: `Id` (unchanged). **Root rule**: an entry is *top-level* iff `ParentExecutionId is null`.

### Lifecycle / state transitions (sequence root only)

```
            create (start)                 finalize (end)
   (none) ───────────────▶  running  ───────────────▶  success | failure
```

- **Stand-alone command**: no `running` phase — logged once at completion as `success`/`failure` (unchanged behavior).
- **Sequence**: `running` written at start; same entry **upserted** to `success`/`failure` with full summary + `StepOutcomes` at end.
- **Crash/restart while running**: an entry may remain `running` (acceptable; not persisted-as-final). UI treats `running` older than the current run as stale — display as-is (no migration required, FR-012).

## 2. Validation / invariants

- A top-level (`ParentExecutionId is null`) entry MUST be the only kind returned by the default list query (FR-001/FR-002).
- A child entry's `RootExecutionId` MUST equal the id of an existing (or in-progress) root entry; `Depth >= 1`.
- `SequenceIndex` on a child MUST match the `StepOrder` of the root step that invoked it (correlation key for the tree, FR-006).
- A sequence root's aggregate `FinalStatus` MUST reflect its children/steps: `failure` if any executed step/child failed; otherwise `success` (FR-005).
- Finalizing a root MUST replace (upsert) the in-progress entry — never create a second physical entry for the same run (no duplicate rows).
- `running` entries MUST be excluded from retention deletion until finalized (they have the same `RetentionExpiresUtc` logic; only finalize sets the meaningful expiry).

## 3. Repository contract changes — `IExecutionLogRepository`

| Method | Signature (conceptual) | Purpose |
|--------|------------------------|---------|
| `UpsertAsync` | `Task UpsertAsync(ExecutionLogEntry entry, CancellationToken)` | Write-or-replace by `Id` (file already overwrites; this fixes the in-memory cache to replace, not append). Used to finalize the in-progress root. |
| `QueryAsync` | *extended* | Honors a roots-only filter (`ParentExecutionId is null`) — the default for the list endpoint. Existing sort/filter/paging unchanged. |
| `GetSubtreeAsync` | `Task<IReadOnlyList<ExecutionLogEntry>> GetSubtreeAsync(string rootId, CancellationToken)` | Return the root plus all descendants (`RootExecutionId == rootId`), for the subtree endpoint. Recursive nesting via `ParentExecutionId`. |

`ExecutionLogQuery` gains an internal `RootsOnly` flag (default `true` for the list endpoint).

## 4. Derived projection: Execution Tree (not persisted) — new

Returned by `GET /api/execution-logs/{id}/subtree`. Built server-side from the root entry's ordered `StepOutcomes` plus correlated child entries.

### ExecutionTreeNodeDto

| Field | Type | Notes |
|-------|------|-------|
| `nodeKind` | string | `sequence` \| `command` \| `step` \| `condition` \| `loop` \| `loopIteration` \| `wait` \| `tap` |
| `executionId` | string? | Set when the node maps to a real `ExecutionLogEntry` (root sequence or invoked command); enables drill-in/deep refresh. `null` for inline steps. |
| `order` | int | Display order within parent (from `StepOrder` / `SequenceIndex` / iteration index). |
| `label` | string | Step label / command name / human-readable node title. |
| `status` | string | `running` \| `success` \| `failure` \| `skipped` \| `not_executed`. |
| `message` | string? | Reason text / outcome message (as today). |
| `appliedDelayMs` | int? | As today. |
| `commandName` | string? | For command-backed steps. |
| `detailAttributes` | wait-for-image attrs? | As today (timeout, image, confidence, exit condition, load status). |
| `conditionTrace` | condition trace? | As today. |
| `deepLink` | authoring deep link? | As today (sequence/step deep link with resolution status). |
| `children` | `ExecutionTreeNodeDto[]` | Nested sub-elements (loop iterations, an invoked command's primitive outcomes, a nested sequence's steps). |

### ExecutionSubtreeResponseDto

| Field | Type | Notes |
|-------|------|-------|
| `executionId` | string | Root id. |
| `finalStatus` | string | Root lifecycle status (`running`/`success`/`failure`) — same field/enum as the entry's `finalStatus`. |
| `root` | `ExecutionTreeNodeDto` | The tree root (the sequence or stand-alone command) with nested `children`. |

**Provisional state**: while the root is `running`, `root.children` is assembled from the child execution entries recorded so far (subtree query) so the UI shows live progress; once finalized, it is assembled from the authoritative root `StepOutcomes` correlated to child entries (Decision 6 in research.md).

## 5. List item additions — `ExecutionLogEntryDto` (list view)

| Field | Type | Notes |
|-------|------|-------|
| `finalStatus` | string | **Enum extended** to `running`/`success`/`failure` (was `success`/`failure`). The single status field — it drives both the in-progress indicator and the client polling decision. No separate `status` mirror field is added. |
| `childCount` | int | Number of direct children (0 ⇒ leaf, not expandable). Stand-alone commands and historical entries ⇒ 0. |

(Existing list fields — `id`, `timestampUtc`, `executionType`, `objectRef`, `summary`, `hierarchy` — unchanged.)

## 6. Frontend types (`executionLogsApi.ts`) — additions

- `ExecutionLogEntryDto`: widen `finalStatus` to `'running' | 'success' | 'failure'` and add `childCount: number` (no separate `status` field).
- New `ExecutionTreeNodeDto` and `ExecutionSubtreeResponseDto` (with `finalStatus`) mirroring section 4.
- New `getSubtree(executionId): Promise<ExecutionSubtreeResponseDto>` calling `GET /api/execution-logs/{id}/subtree`.

## 7. Entity relationship summary

```
ExecutionLogEntry (root, ParentExecutionId = null)
   │  Hierarchy.RootExecutionId = self.Id
   ├── ExecutionLogEntry (child command, Depth=1, ParentExecutionId=root, SequenceIndex=stepOrder)
   │        └── (its own StepOutcomes: taps, waits — shown on expand)
   └── ExecutionLogEntry (nested sequence, Depth=1) 
            └── ExecutionLogEntry (its child commands, Depth=2, ParentExecutionId=nested seq)

Execution Tree (derived) = root StepOutcomes (ordered) ⨝ child entries (by SequenceIndex)
```
