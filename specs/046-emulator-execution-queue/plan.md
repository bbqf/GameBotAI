# Implementation Plan: Emulator Execution Queue

**Branch**: `046-emulator-execution-queue` | **Date**: 2026-05-31 | **Spec**: [spec.md](spec.md)  
**Input**: Feature specification from `specs/046-emulator-execution-queue/spec.md`

## Summary

Introduce a new execution object — the **Emulator Execution Queue** — as a first-class authoring entity. Each queue is bound at creation to exactly one emulator (an ADB device serial), carries a name and a stored-only "cycle execution" flag, and holds an ordered list of sequence entries (append-on-add). Queue **configuration** (name, bound emulator serial, cycle flag) is persisted to disk via a file-backed repository following the existing `FileSequenceRepository`/`FileGameRepository` pattern; the queue **contents** (sequence entries) and **execution status** are held in an in-memory runtime store that is empty/reset after every service restart. A REST API (`/api/queues`) exposes CRUD plus content management and start/stop status transitions (placeholder — status flip only, no real execution). A new **Queues** tab in the authoring UI lists queues with their status and provides CRUD, sequence management, and start/stop controls.

## Technical Context

**Language/Version**: C# / .NET (GameBot.Service minimal API, GameBot.Domain) + TypeScript 5.6.3 / React 18.3.1 (web-ui)  
**Primary Dependencies**: ASP.NET Core Minimal APIs, System.Text.Json; React, Vite 7.3.2, existing in-house component library (List, ConfirmDeleteModal, SearchableDropdown, FormField)  
**Storage**: File-backed JSON under `{storageRoot}/queues/` for queue **config only**; in-memory singleton store for sequence entries + execution status (non-persistent by design)  
**Testing**: xUnit (backend domain/service) + Jest 29 + React Testing Library 14 (frontend); Playwright for E2E where applicable  
**Target Platform**: Windows desktop service + modern web browser (same as existing app)  
**Project Type**: Web application (separate backend service + frontend SPA)  
**Performance Goals**: Queues list and detail interactions reflected <1s at target scale (SC-007); CRUD/status API responses fast (no heavy compute — file I/O + in-memory)  
**Constraints**: Queue contents and status MUST NOT survive restart (FR-021, FR-022); config MUST survive restart (FR-020); emulator binding immutable after creation (FR-004); CamelCase method names only; functions ≤50 LOC  
**Scale/Scope**: Up to ~50 queues total, ~100 sequence entries per queue (single-operator tool); 1 new backend domain model + repo + in-memory store + endpoints group; 1 new UI tab + page + service client + form/list components

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

| Gate (from constitution) | Status | Notes |
|--------------------------|--------|-------|
| Lint/format/static analysis clean | Must pass | ESLint + .NET analyzers; enforced in CI |
| No underscores in method names — CamelCase only | Must pass | All new C#/TS methods use CamelCase |
| Functions ≤50 LOC | Must pass | Endpoints kept thin; logic in repo/store services |
| Unit ≥80% line / ≥70% branch on touched areas | Must pass | Tests planned for domain model, repository, in-memory store, endpoints, and UI components/page |
| Deterministic, isolated, fast tests | Must pass | File repo tests use temp dirs; in-memory store tests need no I/O; UI mocks the queues service |
| UX consistency with existing conventions | Must pass | Reuses Nav tab pattern, List/ConfirmDeleteModal/SearchableDropdown, standard `{ error: { code, message, hint } }` API error shape |
| Actionable error messages | Must pass | Validation errors name the missing field (FR-008) and blocked-while-running actions explain "stop first" (FR-005a) |
| Performance goals declared | ✅ Declared above | SC-007 (<1s at 50 queues × 100 entries) |
| Public API/contract documented | Must pass | `contracts/queues-api.md` + DTO docs in `data-model.md` |
| Observability | Must pass | Start/stop logged via existing `ILogger` (FR-019b); CRUD logging not required this iteration |
| No unjustified new dependencies | ✅ None added | Reuses existing stack |

No constitution violations. No waivers required. No Complexity Tracking entries needed.

## Project Structure

### Documentation (this feature)

```text
specs/046-emulator-execution-queue/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── queues-api.md    # Phase 1 output — REST contract for /api/queues
├── checklists/
│   └── requirements.md  # Spec quality checklist (from /speckit-specify)
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/GameBot.Domain/Queues/                      # NEW — domain + persistence
├── ExecutionQueue.cs                # Persisted config model (Id, Name, EmulatorSerial, CycleExecution, timestamps)
├── QueueEntry.cs                    # In-memory entry (EntryId, SequenceId, position) + resolved name/stale flag for responses
├── QueueExecutionStatus.cs          # enum { Stopped, Running }
├── IQueueRepository.cs              # Config persistence contract (Get/List/Create/Update/Delete)
├── FileQueueRepository.cs           # File-backed JSON impl under {storageRoot}/queues (mirrors FileSequenceRepository)
├── IQueueRuntimeStore.cs            # In-memory contract: entries + status keyed by queueId
└── QueueRuntimeStore.cs             # Thread-safe in-memory impl (ConcurrentDictionary), non-persistent

src/GameBot.Service/Contracts/Queues/           # NEW — request/response DTOs
├── QueueResponse.cs                 # id, name, emulatorSerial, cycleExecution, status, entryCount
├── QueueDetailResponse.cs           # QueueResponse + ordered entries[] (entryId, sequenceId, sequenceName, stale)
├── CreateQueueRequest.cs            # name, emulatorSerial, cycleExecution
├── UpdateQueueRequest.cs            # name, cycleExecution  (emulator NOT updatable)
└── AddQueueEntryRequest.cs          # sequenceId

src/GameBot.Service/Endpoints/
└── QueuesEndpoints.cs               # NEW — MapQueueEndpoints() extension; all /api/queues routes

src/GameBot.Service/Program.cs                   # MODIFIED — register repo + runtime store singletons; app.MapQueueEndpoints()
src/GameBot.Service/ApiRoutes.cs                 # MODIFIED — add `Queues = Base + "/queues"`
```

```text
src/web-ui/src/services/
└── queues.ts                        # NEW — typed client (list/get/create/update/delete + entries + start/stop)

src/web-ui/src/services/__tests__/
└── queues.spec.ts                   # NEW — service client tests (optional, light)

src/web-ui/src/pages/
├── QueuesPage.tsx                   # NEW — list + CRUD + start/stop + entry management
└── __tests__/QueuesPage.spec.tsx    # NEW — page tests

src/web-ui/src/components/queues/    # NEW — feature components
├── QueueForm.tsx                    # create/edit form (name, emulator picker, cycle checkbox)
├── QueueEntryList.tsx               # ordered entries with add (sequence picker) + remove + stale badge
└── __tests__/*.test.tsx             # component tests

src/web-ui/src/components/Nav.tsx                # MODIFIED — add 'Queues' to AuthoringTab + tabs[]
src/web-ui/src/App.tsx                           # MODIFIED — render <QueuesPage/> when tab === 'Queues'
src/web-ui/src/lib/navigation.ts                 # MODIFIED (if needed) — normalizeTab accepts 'Queues'
```

**Structure Decision**: Web application layout. Backend follows the established "domain model + `IXRepository`/`FileXRepository` + `Contracts/X` DTOs + `XEndpoints.MapXEndpoints()`" convention; the only new architectural element is the **in-memory runtime store** that holds the non-persistent parts (entries + status), registered as a singleton alongside the file repo. Frontend follows the established "tab in `Nav` → page in `App` → `services/x.ts` client → feature components" convention, reusing `useAdbDevices` for the emulator picker and the sequences service for the sequence picker.

## Implementation Design

### Backend

**Persistence split (core design decision)**
- `ExecutionQueue` (persisted): `Id`, `Name`, `EmulatorSerial`, `CycleExecution` (bool), `CreatedAt`, `UpdatedAt`. Stored as `{storageRoot}/queues/{id}.json` via `FileQueueRepository` (copy the safe-id/path-traversal guard and JSON options from `FileSequenceRepository`).
- `QueueRuntimeStore` (in-memory, singleton): `ConcurrentDictionary<string queueId, QueueRuntimeState>` where `QueueRuntimeState = { List<QueueEntry> Entries, QueueExecutionStatus Status }`. Created lazily on first access; never written to disk. Because the service registers it as a singleton and it has no persistence, a restart yields empty entries and `Stopped` status for every queue automatically (satisfies FR-021, FR-022).

**Endpoints** (`/api/queues`, thin handlers, `{ error: { code, message, hint } }` errors):
- `POST   /api/queues` — create; validate name + emulatorSerial present (FR-008) → persist config, return `QueueResponse` (status Stopped, entryCount 0).
- `GET    /api/queues` — list; join persisted config with runtime status + entryCount.
- `GET    /api/queues/{id}` — detail; includes ordered entries with resolved `sequenceName` and `stale` flag (resolved against `ISequenceRepository`; FR-013b).
- `PUT    /api/queues/{id}` — update name/cycle; reject if running (FR-005a); emulator field ignored/rejected (FR-004).
- `DELETE /api/queues/{id}` — delete; reject if running (FR-005a); removes config + runtime state.
- `POST   /api/queues/{id}/entries` — add sequence entry at end (FR-010); allowed while running (FR-013a).
- `DELETE /api/queues/{id}/entries/{entryId}` — remove entry; allowed while running.
- `POST   /api/queues/{id}/start` — set status Running (idempotent, FR-017); allowed even if emulator offline (FR-019a); log start (FR-019b).
- `POST   /api/queues/{id}/stop` — set status Stopped (idempotent); log stop (FR-019b).

**DI registration** (Program.cs, near other `FileXRepository` singletons):
```
builder.Services.AddSingleton<IQueueRepository>(_ => new FileQueueRepository(storageRoot));
builder.Services.AddSingleton<IQueueRuntimeStore, QueueRuntimeStore>();
...
app.MapQueueEndpoints();
```

### Frontend

- **Nav/App**: add `'Queues'` to `AuthoringTab` union and `tabs[]` in `Nav.tsx`; render `<QueuesPage/>` for `tab === 'Queues'` in `App.tsx`; extend `normalizeTab` mapping if it whitelists tab names.
- **`services/queues.ts`**: typed client mirroring `games.ts`, using `getJson/postJson/putJson/deleteJson` from `lib/api`. Endpoints: `listQueues`, `getQueue`, `createQueue`, `updateQueue`, `deleteQueue`, `addQueueEntry`, `removeQueueEntry`, `startQueue`, `stopQueue`.
- **`QueuesPage.tsx`**: uses the `List` component to render queues (name, emulator serial, cycle on/off, status chip, entry count) with Start/Stop buttons (disabled per status), New/Edit/Delete (Edit/Delete disabled while running). Selecting a queue opens its `QueueEntryList`.
- **`QueueForm.tsx`**: name input (`FormField`), emulator picker fed by `useAdbDevices` (serial list; create-only — disabled/hidden on edit per FR-004), cycle-execution checkbox. Validation blocks submit without name/emulator.
- **`QueueEntryList.tsx`**: ordered entries; "Add sequence" uses a `SearchableDropdown` populated from the sequences service; remove button per row; stale entries flagged with a badge and still removable.

## Contracts

External REST contract documented in `contracts/queues-api.md`. No changes to existing contracts. The frontend `services/queues.ts` types are the in-frontend contract and mirror the DTOs in `data-model.md`.

## Testing Strategy

**Backend (xUnit)**
- `FileQueueRepository`: create/get/list/update/delete round-trip in a temp dir; safe-id rejection; config-only persistence (no entries/status fields written).
- `QueueRuntimeStore`: append order preserved; remove keeps order; start/stop idempotency; status defaults to Stopped; entries empty for unknown queue.
- `QueuesEndpoints`: create validation (missing name/emulator → 400 with field message); update/delete blocked while running (409/400); add entry appends; stale entry surfaced when sequence missing; start/stop transitions + idempotency; start allowed when emulator serial not connected.

**Frontend (Jest + RTL)**
- `services/queues.ts`: each call hits the right URL/verb (mock `lib/api`).
- `QueueForm`: required-field validation; emulator picker disabled on edit.
- `QueueEntryList`: add appends to end; remove; stale badge rendered.
- `QueuesPage`: lists queues with status; Start flips to Running and disables Edit/Delete; Stop re-enables; delete confirmation via `ConfirmDeleteModal`.

**Restart behavior**: covered by `QueueRuntimeStore` unit tests (fresh instance = empty entries + Stopped) which model the singleton lifecycle; documented in `quickstart.md` for manual verification.

## Complexity Tracking

No constitution violations requiring justification.
