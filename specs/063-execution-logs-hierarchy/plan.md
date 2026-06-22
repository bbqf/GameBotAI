# Implementation Plan: Execution Logs Reflect What Was Actually Executed

**Branch**: `049-execution-logs-hierarchy` | **Date**: 2026-06-01 | **Spec**: [spec.md](spec.md)  
**Input**: Feature specification from `specs/049-execution-logs-hierarchy/spec.md`

## Summary

Today, executing a sequence pollutes the execution-logs list: the sequence runner's per-command callback calls `commandExecutor.ForceExecuteAsync`, which logs **each invoked command as its own top-level entry** (`Depth = 0`, no parent), and the endpoint then logs the **sequence** as another top-level entry. The list query never filters by hierarchy, so the user sees one row per invoked command plus one sequence row.

This feature makes the execution-logs list show **only top-level executed entities** — stand-alone commands and sequences — with each sequence's invoked commands recorded as **linked children** (kept, not deleted) and rendered as **expandable tree rows** beneath the sequence. A sequence run produces a single **in-progress root entry** created at start, linked child command entries written as steps execute, and a finalized root entry (complete picture) at the end. The execution-logs UI **polls while any visible entry is in progress** so nested sub-elements update **live** without a manual reload.

The data model already carries the needed `RootExecutionId` / `ParentExecutionId` / `Depth` / `SequenceIndex` hierarchy fields — they are simply not yet populated for child commands nor used to filter the list. The core work is: (1) link child command executions to the sequence root during execution, (2) create + finalize an in-progress root entry, (3) filter the list to roots-only and add a subtree/children endpoint, (4) support an in-progress lifecycle status and entry upsert in the repository, and (5) refactor the UI into a polling expandable tree.

## Technical Context

**Language/Version**: C# / .NET (GameBot.Service minimal API, GameBot.Domain) + TypeScript 5.6.3 / React 18.3.1 (web-ui)  
**Primary Dependencies**: ASP.NET Core Minimal APIs, System.Text.Json; React, Vite 7.3.2, existing in-house hooks (`useNavigationCollapse`) and `getJson` API client  
**Storage**: File-backed JSON per entry under `{storageRoot}/execution-logs/` via `FileExecutionLogRepository` (in-memory `ImmutableArray` cache)  
**Testing**: xUnit (`tests/unit`, `tests/integration`, `tests/contract`) + Jest 29 + React Testing Library 14 (web-ui)  
**Target Platform**: Windows desktop service + modern web browser (same as existing app)  
**Project Type**: Web application (separate backend service + frontend SPA)  
**Performance Goals**: List (roots-only) and subtree fetch reflected <1s at target scale (SC-006/SC-007); live sub-element updates visible within ~2s of a step completing (SC-005) via a 2s poll while in-progress  
**Constraints**: CamelCase method names only (no underscores); functions ≤50 LOC; child records kept and filtered, not deleted (FR-002a); historical logs remain viewable (FR-012); no new runtime dependencies  
**Scale/Scope**: Single-operator tool — hundreds to low-thousands of execution entries; a sequence run produces ~1 root + N child entries (N = invoked commands). Backend: extend log service/repository/endpoints + sequence-execute wiring. Frontend: rework `ExecutionLogs.tsx` list into an expandable polling tree.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

| Gate (from constitution) | Status | Notes |
|--------------------------|--------|-------|
| Lint/format/static analysis clean | Must pass | ESLint + .NET analyzers; enforced in CI |
| No underscores in method names — CamelCase only | Must pass | New methods (`QueryRootsAsync`, `GetSubtreeAsync`, `UpsertAsync`, `LogSequenceStartAsync`) all CamelCase |
| Functions ≤50 LOC | Must pass | Endpoints/handlers kept thin; projection/tree logic factored into helpers |
| Unit ≥80% line / ≥70% branch on touched areas | Must pass | Tests planned for hierarchy linking, roots-only query, subtree projection, upsert, in-progress lifecycle, and UI tree/polling |
| Deterministic, isolated, fast tests | Must pass | Repo tests use temp dirs; service tests use in-memory repo; UI mocks the execution-logs service and fake timers for polling |
| UX consistency with existing conventions | Must pass | Reuses existing list/detail layout, `{ error: { code, message, hint } }` shape, phone/desktop responsive split, deep-link navigation |
| Actionable error messages | Must pass | Subtree/not-found returns existing `not_found` shape; expand failures show inline hint |
| Performance goals declared | ✅ Declared above | SC-005 (~2s live), SC-006/007 (<1s) |
| Public API/contract documented | Must pass | `contracts/execution-logs-hierarchy.openapi.yaml` + DTO docs in `data-model.md`; `specs/openapi.json` + contract tests updated |
| Observability | Must pass | Sequence start/finalize + child linking logged via existing `ILogger` |
| No unjustified new dependencies | ✅ None added | Polling uses native `setInterval`; reuses existing stack |
| Backward compatibility | Must pass | List response stays shape-compatible (adds `childCount`; widens `finalStatus` enum with `running`); historical entries (no in-progress status, no children) render as leaf roots |

No constitution violations. No waivers required. No Complexity Tracking entries needed.

## Project Structure

### Documentation (this feature)

```text
specs/049-execution-logs-hierarchy/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── execution-logs-hierarchy.openapi.yaml   # Phase 1 — list(roots) + subtree/children contract
├── checklists/
│   └── requirements.md  # Spec quality checklist (from /speckit-specify)
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/
├── GameBot.Domain/
│   └── Logging/
│       ├── ExecutionLogModels.cs            # ExecutionLogEntry: add lifecycle status ("running"); reuse Hierarchy
│       ├── IExecutionLogRepository.cs       # add UpsertAsync + roots-only / subtree query support
│       └── FileExecutionLogRepository.cs    # in-memory upsert (replace by id); roots-only filter; subtree by root id
├── GameBot.Service/
│   ├── Services/
│   │   ├── CommandExecutor.cs               # accept caller-supplied ExecutionLogContext (parent/root/depth) for child commands
│   │   └── ExecutionLog/
│   │       ├── ExecutionLogService.cs       # LogSequenceStartAsync (in-progress root) + finalize; subtree projection
│   │       └── ExecutionLogContext.cs       # already supports parent/root/depth/sequenceIndex
│   ├── Endpoints/
│   │   └── ExecutionLogsEndpoints.cs        # list defaults roots-only; GET /{id}/subtree (or /children)
│   ├── Models/
│   │   └── ExecutionLogs.cs                 # DTOs: finalStatus(+running), childCount, subtree/tree-node DTO
│   └── Program.cs                           # sequences/{id}/execute: create root, pass child context, finalize root
└── web-ui/src/
    ├── services/executionLogsApi.ts         # roots-only list types; getSubtree; finalStatus(+running)/childCount fields
    └── pages/ExecutionLogs.tsx              # expandable tree rows + polling while in-progress

tests/
├── unit/ExecutionLogs/                      # hierarchy linking, roots-only query, upsert, subtree projection, status mapping
├── integration/ExecutionLogs/              # sequence run → 1 root + linked children; list excludes children; live finalize
├── contract/ExecutionLogs/                 # OpenAPI: list(roots) + subtree endpoint + new fields
└── web-ui (Jest)                            # ExecutionLogs tree expand/collapse + polling (fake timers)
```

**Structure Decision**: Web application. Backend changes are localized to the existing `ExecutionLog` service/repository/endpoints plus the `sequences/{id}/execute` wiring in `Program.cs` and a small `CommandExecutor` change to accept a caller-supplied execution context. Frontend changes are localized to `executionLogsApi.ts` and `ExecutionLogs.tsx`. The execution **queue** (specs 046–048) is a status-flip placeholder with no real execution and produces no logs, so it is out of scope.

## Complexity Tracking

> No Constitution Check violations — section intentionally empty.
