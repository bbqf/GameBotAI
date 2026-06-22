# Phase 0 Research: Execution Logs Reflect What Was Actually Executed

**Feature**: 049-execution-logs-hierarchy  
**Date**: 2026-06-01

All Technical Context items are known (the feature extends an existing subsystem). This document records the key investigation findings and the design decisions that resolve them. There are no open `NEEDS CLARIFICATION` items (the three clarifications were resolved in the spec on 2026-06-01).

## Finding 1 — Root cause of the duplicated/flat entries

**Investigation**: `sequences/{id}/execute` ([Program.cs:822-877](../../src/GameBot.Service/Program.cs)) drives `SequenceRunner.ExecuteAsync` with an `executeCommandAsync` callback that calls `commandExecutor.ForceExecuteAsync(sessionId, commandId, ct)`. `ForceExecuteAsync` → `ForceExecuteDetailedAsync` ([CommandExecutor.cs:61-84](../../src/GameBot.Service/Services/CommandExecutor.cs)) **always** logs a command entry with `new ExecutionLogContext { Depth = 0 }` — no parent/root. After the runner finishes, the endpoint logs the sequence entry, also `Depth = 0` ([Program.cs:1016](../../src/GameBot.Service/Program.cs)). `FileExecutionLogRepository.QueryAsync` ([FileExecutionLogRepository.cs:45-99](../../src/GameBot.Domain/Logging/FileExecutionLogRepository.cs)) never filters by hierarchy.

**Conclusion**: The list shows every command (one row each) plus the sequence, all flat. The fix is to (a) link child command executions to the sequence root, and (b) filter the list to roots-only.

## Decision 1 — Reuse the existing hierarchy fields; do not invent new ones

- **Decision**: Populate the already-existing `ExecutionHierarchyContext` (`RootExecutionId`, `ParentExecutionId`, `Depth`, `SequenceIndex`) for child command executions during a sequence run, and treat a "root" entry as `ParentExecutionId == null` (equivalently `Depth == 0`).
- **Rationale**: The model, builder ([ExecutionHierarchyBuilder.cs](../../src/GameBot.Service/Services/ExecutionLog/ExecutionHierarchyBuilder.cs)), DTOs, and persisted JSON all already carry these fields; they are just unset for children. Reusing them is the least-disruptive change (clarification: "keep recording each child execution but link it to a parent/root").
- **Alternatives considered**: A brand-new "grouping id" field — rejected as redundant with `RootExecutionId`. Deleting/suppressing child records entirely — rejected by clarification (records must be kept, only filtered from the list).

## Decision 2 — Pass a caller-supplied execution context into `CommandExecutor`

- **Decision**: Add a `ForceExecuteDetailedAsync` overload (and `ForceExecuteAsync` overload) accepting an optional `ExecutionLogContext`. The sequence-execute callback supplies `{ ParentExecutionId = rootId, RootExecutionId = rootId, Depth = 1, SequenceIndex = <step order>, SequenceId, SequenceLabel }`. Direct (stand-alone) command execution keeps `Depth = 0` / no parent.
- **Rationale**: Keeps command logging in one place; the only new information is the parent linkage, supplied by the caller that knows it. CamelCase, small surface.
- **Alternatives considered**: An ambient/`AsyncLocal` execution scope — rejected as implicit and harder to test deterministically. Logging children from the endpoint instead of the executor — rejected because the executor owns the per-command outcome detail.

## Decision 3 — In-progress root entry + finalize (lifecycle status)

- **Decision**: Add a `LogSequenceStartAsync` that writes an **in-progress** root entry (new `FinalStatus` value `"running"`) before the runner starts, returning its id to use as the root/parent id for children. After the runner completes, **update** that same entry in place to `success`/`failure` with the full summary + `StepOutcomes`. `NormalizeStatus` is extended to allow `running` (in addition to `success`/`failure`).
- **Rationale**: Satisfies FR-007 (single in-progress top-level entry while running) and FR-008/SC-005 (the entry settles into its final state). Stand-alone commands remain single-shot (logged once at completion) — they need no in-progress phase for this feature.
- **Alternatives considered**: Only writing the root at the end — rejected because then nothing represents the running sequence and live updates are impossible. A separate "sessions/in-flight" store — rejected as duplicative of the log store.

## Decision 4 — Repository upsert (replace-by-id)

- **Decision**: Add `UpsertAsync` to `IExecutionLogRepository` / `FileExecutionLogRepository`. The file write already overwrites by id (`File.Create`), but the in-memory `ImmutableArray` currently appends unconditionally ([FileExecutionLogRepository.cs:28-30](../../src/GameBot.Domain/Logging/FileExecutionLogRepository.cs)); upsert replaces the matching in-memory element (or appends if absent) so finalizing the root does not create a duplicate.
- **Rationale**: Required to finalize the in-progress root without a duplicate row. Minimal, localized.
- **Alternatives considered**: Keep `AddAsync` append-only and dedupe at query time — rejected as fragile (two physical rows, ordering ambiguity).

## Decision 5 — Roots-only list + subtree endpoint

- **Decision**:
  - List (`GET /api/execution-logs`) returns **roots only** (`ParentExecutionId == null`) by default. Add an optional `includeChildren=false` (default) / explicit override only if needed for tests; the default behavior is roots-only.
  - Add `GET /api/execution-logs/{id}/subtree` returning the root entry plus its descendant entries (ordered by `SequenceIndex`, recursive for nested sequences) as a tree, so the UI can render and lazily expand nested rows.
  - Each list item gains `childCount` and its `finalStatus` enum is widened to include `running`, so the UI knows a row is expandable and whether to keep polling. No separate `status` mirror field is added (avoids two fields carrying the same data).
- **Rationale**: Roots-only directly fixes the pollution (FR-001/FR-002). A dedicated subtree endpoint keeps the list light and supports lazy expansion + nested sequences (FR-003a/FR-009). `childCount` + the widened `finalStatus` drive expansion affordance and polling.
- **Alternatives considered**: Embedding the full subtree in every list item — rejected (heavier payload, most rows never expanded). Client-side reconstruction from a flat "include children" list — viable but pushes tree-assembly + nested-sequence recursion into the client; the server-built subtree is simpler and testable. Keeping subtree assembly server-side is preferred.

## Decision 6 — Nested tree content: combine root step outcomes with linked child detail

- **Decision**: The root sequence entry's `StepOutcomes` remain the authoritative **ordered** list of sub-elements (steps, command names, condition traces, loop iterations, wait details, applied delays — the complete "today" picture). Command-backed steps are correlated to their child execution entry via `SequenceIndex`; expanding such a step reveals that command's own primitive/tap/wait outcomes (from the child entry). While the sequence is still running and the root's `StepOutcomes` are not yet finalized, the UI shows the child entries recorded so far (from the subtree endpoint) as provisional nodes.
- **Rationale**: Preserves 100% of today's detail (SC-002) while honoring "keep & link child records." Correlating by `SequenceIndex` avoids inventing a new cross-reference id.
- **Alternatives considered**: Rebuilding the entire tree purely from child execution entries — rejected because non-command steps (inline conditions, loops, waits) are not separate executions and live only in the root's step outcomes.

## Decision 7 — Real-time via polling (no streaming infra)

- **Decision**: The `ExecutionLogs` page polls the list on an interval (~2s) **while any visible entry has `status == "running"`**, and re-fetches the subtree of any expanded in-progress root. Polling stops when nothing is in progress.
- **Rationale**: The app has no SSE/WebSocket infrastructure; introducing it would add dependencies and violate the "no unjustified new dependencies" gate. Child command entries are already written in real time during the synchronous run, so polling surfaces live progress. ~2s meets SC-005. Existing UI uses manual `refresh()` patterns; a bounded interval is a small, conventional addition.
- **Alternatives considered**: Server-Sent Events / WebSockets — rejected for scope/dependency cost; can be a future enhancement. Always-on polling — rejected (wasteful when idle); gate polling on presence of an in-progress entry.

## Decision 8 — Backward compatibility for historical logs

- **Decision**: Entries persisted before this change have no `running` status and no linked children; they are treated as completed leaf roots (`childCount = 0`, not expandable) and continue to render and open normally (FR-012). The list/detail DTOs add only optional fields.
- **Rationale**: Avoids a migration; old JSON deserializes with default/empty hierarchy → treated as root. Contract stays additive/backward-compatible (validated by `OpenApiBackwardCompatTests`).
- **Alternatives considered**: A one-time migration to re-group historical logs — rejected (clarification says historical logs need not be re-grouped, only remain viewable).

## Summary of decisions

| # | Decision |
|---|----------|
| 1 | Reuse existing `Root/Parent/Depth/SequenceIndex` hierarchy fields; root = no parent |
| 2 | `CommandExecutor` accepts a caller-supplied `ExecutionLogContext` for child linkage |
| 3 | In-progress root entry created at start, finalized at end; add `running` status |
| 4 | Repository `UpsertAsync` (replace-by-id) to finalize without duplicates |
| 5 | List returns roots-only; new `GET /{id}/subtree`; list items gain `childCount` and widened `finalStatus` (incl. `running`) — no separate `status` field |
| 6 | Tree = root step outcomes (ordered, complete) correlated to child entries by `SequenceIndex` |
| 7 | Real-time via ~2s polling gated on in-progress entries; no streaming infra |
| 8 | Historical logs render as completed leaf roots; additive, backward-compatible contract |
