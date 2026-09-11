# Implementation Plan: Duplicate Queues

**Branch**: `083-duplicate-queues` | **Date**: 2026-09-11 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/083-duplicate-queues/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Add a "Duplicate" action for execution queues. Given a source queue and a new name (which must
differ from the source's current name), the system creates a new `ExecutionQueue` with every
configuration field copied from the source (emulator serial/instance, cycle-execution, idle-pause
settings, linked template reference, linked game reference) and the source's currently loaded
runtime entries (sequence list) copied into the new queue's runtime store — a true 1:1 copy except
for identity and name. The duplicate is always created stopped, with no execution history or
in-progress schedule state. Implemented as a new `POST /api/queues/{id}/duplicate` endpoint plus a
"Duplicate" row action and small name-prompt modal in the Queues web UI.

## Technical Context

**Language/Version**: C# / .NET (ASP.NET Core minimal APIs) for the backend; TypeScript + React 18 (Vite) for the web UI — matching the rest of the repo.
**Primary Dependencies**: Existing `GameBot.Domain.Queues` (`IQueueRepository`, `IQueueRuntimeStore`, `ExecutionQueue`), no new packages.
**Storage**: File-based JSON queue documents under `data/` via `FileQueueRepository` (existing); runtime entries stay in-memory via `IQueueRuntimeStore` (existing, unpersisted by design).
**Testing**: xUnit contract/unit tests for the backend endpoint (existing `tests/` conventions for `QueuesEndpoints`); Jest + React Testing Library for the web-ui action/modal (existing `src/web-ui` conventions).
**Target Platform**: Windows desktop host running the GameBot service + emulator automation; browser-based Web UI.
**Project Type**: Web application (ASP.NET Core backend + React SPA), matching the existing repo layout — no new project.
**Performance Goals**: Same class of operation as `POST /api/queues` (single file-repo write + in-memory runtime-entries copy); no new hot path. Target: duplicate completes and returns in the same latency envelope as queue creation today (well under 200ms locally against the file-based repo).
**Constraints**: Must not mutate the source queue; must not carry over runtime status (running/stopped) or execution history; must reuse existing validation helpers (`CoerceThreshold`, `NormalizeInstanceName`) so duplicate-created queues are byte-for-byte consistent with queues created through the normal create path.
**Scale/Scope**: One new backend endpoint, one new request/response DTO pairing (reusing `QueueResponse`), one new web-ui service call, one new small modal component, one new row action button. No data migration; no changes to `QueueTemplate`, `ExecutionQueue` schema, or persisted JSON shape.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

| Principle | Assessment |
|-----------|------------|
| I. Code Quality Discipline | New endpoint follows the existing `QueuesEndpoints` style (minimal-API handler + private `Build*`/`Error` helpers already in the file); no new dependencies; small, single-purpose methods (<50 LOC). PASS. |
| II. Testing Standards | New xUnit tests for the duplicate endpoint (success, name-unchanged rejection, missing-source 404, entries/config copy assertions) and Jest tests for the web-ui action/modal. Bug-fix-style regression tests not applicable (new feature, not a fix). PASS (planned in tasks). |
| III. UX Consistency | Reuses the existing row-action button pattern (`Edit`/`Delete`) and the existing modal pattern (`ConfirmDeleteModal`) for a consistent "Duplicate" action; error messages follow the existing `{ error: { code, message, hint } }` envelope used by every other queue endpoint. PASS. |
| IV. Performance Requirements | Declared above: no new hot path, same order of magnitude as existing `CreateQueue`. No dedicated benchmark needed — consistent with how sibling simple CRUD endpoints (e.g., `UpdateQueue`) are treated. PASS. |
| V. Living Documentation | `docs/architecture.md` will get a new bullet under the Queue domain-model section describing duplication, with "Last reviewed" bumped, in the same PR as the code change. This spec's `Status` line and `specs/STATUS.md` will be updated once implemented. PASS (tracked in tasks). |

No violations requiring the Complexity Tracking table.

## Project Structure

### Documentation (this feature)

```text
specs/083-duplicate-queues/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
│   └── duplicate-queue.md
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
src/GameBot.Domain/Queues/
├── ExecutionQueue.cs             # existing — no schema change
└── IQueueRuntimeStore.cs         # existing — GetEntries/SetEntries already present, reused as-is

src/GameBot.Service/
├── Endpoints/QueuesEndpoints.cs  # + MapPost("{id}/duplicate", ...) handler
└── Contracts/Queues/
    └── DuplicateQueueRequest.cs  # new: { Name: string }

tests/GameBot.Service.Tests/Endpoints/
└── QueuesEndpointsTests.cs       # + duplicate-endpoint test cases (existing file)

src/web-ui/src/
├── services/queues.ts                        # + duplicateQueue(id, name)
├── components/queues/DuplicateQueueModal.tsx  # new: name-prompt modal (mirrors ConfirmDeleteModal)
└── pages/QueuesPage.tsx                       # + "Duplicate" row action wiring

src/web-ui/src/__tests__/ (or colocated, matching existing convention)
└── QueuesPage / DuplicateQueueModal test additions
```

**Structure Decision**: No new projects or directories. This is a same-shape addition to the
existing single ASP.NET Core backend + single React SPA web application: one new endpoint in the
existing `QueuesEndpoints.cs`, one new request contract, and existing-pattern web-ui additions
(service function, small modal, row action). Mirrors how prior small queue features (e.g., feature
074's instance fields, feature 073's idle-pause) were added — no structural changes, only additive
fields/handlers in the same files.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No violations — table omitted.
