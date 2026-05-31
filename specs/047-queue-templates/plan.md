# Implementation Plan: Queue Templates

**Branch**: `047-queue-templates` | **Date**: 2026-05-31 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/047-queue-templates/spec.md`

## Summary

Add **queue templates** — the durable, shareable persistence the Emulator Execution
Queue (046) intentionally lacks for its runtime entries. A template is a standalone,
file-backed aggregate holding a unique name plus an ordered list of sequence
references, with **no** link to any queue, emulator, or status. From within the queue
editor an operator can **save** the current queue's entries as a named template (with
case-insensitive overwrite confirmation), **load** a template into any queue (full,
order-preserving replacement, blocked while the queue is running, with a replace
confirmation when the queue is non-empty), and **delete** templates from the same
picker used to load them. There is no separate template editor — editing means
load → edit in the queue → save back over the name (FR-025).

Architecturally this is one new backend module (`GameBot.Domain.QueueTemplates` +
`/api/queue-templates` endpoints) that is fully decoupled from queues, plus **one**
small addition to the queue module — a generic `PUT /api/queues/{id}/entries`
replace-entries endpoint (and a matching `IQueueRuntimeStore.SetEntries`) that the
frontend uses to load a template. The UI adds Save/Load controls and two dialogs to
the existing `QueuesPage` entry editor, reusing `ConfirmDeleteModal`,
`SearchableDropdown`, and the established service/component conventions.

## Technical Context

**Language/Version**: C# / .NET (GameBot.Service minimal API, GameBot.Domain) +
TypeScript 5.6.3 / React 18.3.1 (web-ui)
**Primary Dependencies**: ASP.NET Core Minimal APIs, System.Text.Json; React, Vite,
existing in-house components (ConfirmDeleteModal, SearchableDropdown, FormField, List)
**Storage**: File-backed JSON under `{storageRoot}/queue-templates/` for templates
(name + entries, fully persisted). No change to queue persistence; queue entries remain
in the existing non-persistent in-memory runtime store.
**Testing**: xUnit (backend domain/endpoints) + Jest 29 + React Testing Library 14 (frontend)
**Target Platform**: Windows desktop service + modern web browser
**Project Type**: Web application (separate backend service + frontend SPA)
**Performance Goals**: Template list/load/delete and the picker reflected <1s at target
scale (SC-007); file I/O + in-memory only, no heavy compute
**Constraints**: Templates persist across restarts (FR-002); templates store only
sequence entries, never emulator/cycle/status (FR-003); load is a copy with no live
link (FR-015); load blocked while running (FR-014a); case-insensitive unique names,
`[A-Za-z0-9 _-]`, ≤100 chars; CamelCase method names only; functions ≤50 LOC
**Scale/Scope**: ≤~50 templates × ≤~100 entries; 1 new backend domain module + repo +
endpoints group; 1 new queue endpoint + 1 runtime-store method; 1 new UI service + 2
dialogs + edits to `QueuesPage`

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI),
implementation progression is blocked until failures are fixed or a documented
maintainer waiver exists.

| Gate (from constitution) | Status | Notes |
|--------------------------|--------|-------|
| Lint/format/static analysis clean | Must pass | ESLint + .NET analyzers; enforced in CI |
| No underscores in method names — CamelCase only | Must pass | All new C#/TS methods CamelCase |
| Functions ≤50 LOC | Must pass | Thin endpoints; logic in repo/store; dialogs small |
| Unit ≥80% line / ≥70% branch on touched areas | Must pass | Tests for repo, runtime-store method, both endpoint groups, service clients, dialogs, page wiring |
| Deterministic, isolated, fast tests | Must pass | File repo tests use temp dirs; runtime-store tests need no I/O; UI mocks services |
| UX consistency with existing conventions | Must pass | Reuses ConfirmDeleteModal, SearchableDropdown, `{ error: { code, message, hint } }` envelope, existing Queues editor layout |
| Actionable error messages | Must pass | Name-rule violations name the rule; `template_exists` drives overwrite; `queue_running` says "stop first" |
| Performance goals declared | ✅ Declared | SC-007 (<1s at 50 templates × 100 entries) |
| Public API/contract documented | Must pass | `contracts/queue-templates-api.md` + `data-model.md` |
| Observability | ✅ N/A this iteration | No template-action logging (clarified) |
| No unjustified new dependencies | ✅ None added | Reuses existing stack |

No constitution violations. No waivers required. No Complexity Tracking entries needed.

## Project Structure

### Documentation (this feature)

```text
specs/047-queue-templates/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── queue-templates-api.md   # Phase 1 output — REST contract
├── checklists/
│   └── requirements.md  # Spec quality checklist (from /speckit-specify)
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/GameBot.Domain/QueueTemplates/               # NEW — standalone template module
├── QueueTemplate.cs                 # Persisted: Id, Name, List<QueueTemplateEntry> Entries, CreatedAt, UpdatedAt
├── QueueTemplateEntry.cs            # Persisted: { SequenceId } (positional, order = run order)
├── IQueueTemplateRepository.cs      # Get/List/FindByName/Create/Update/Delete
└── FileQueueTemplateRepository.cs   # File-backed JSON under {storageRoot}/queue-templates (mirrors FileQueueRepository)

src/GameBot.Service/Contracts/QueueTemplates/    # NEW — request/response DTOs
├── QueueTemplateSummaryResponse.cs  # id, name, entryCount, createdAt, updatedAt
├── QueueTemplateDetailResponse.cs   # summary + entries[] (sequenceId, sequenceName, stale)
└── SaveQueueTemplateRequest.cs      # name, sequenceIds[], overwrite

src/GameBot.Service/Endpoints/
└── QueueTemplatesEndpoints.cs       # NEW — MapQueueTemplateEndpoints(); all /api/queue-templates routes

src/GameBot.Domain/Queues/IQueueRuntimeStore.cs  # MODIFIED — add SetEntries(queueId, sequenceIds)
src/GameBot.Domain/Queues/QueueRuntimeStore.cs   # MODIFIED — implement SetEntries (replace, new EntryIds, order)
src/GameBot.Service/Contracts/Queues/
└── ReplaceQueueEntriesRequest.cs    # NEW — { sequenceIds[] }
src/GameBot.Service/Endpoints/QueuesEndpoints.cs # MODIFIED — add PUT {id}/entries (replace; 409 when running)
src/GameBot.Service/Program.cs                   # MODIFIED — register IQueueTemplateRepository; app.MapQueueTemplateEndpoints()
src/GameBot.Service/ApiRoutes.cs                 # MODIFIED — add QueueTemplates = Base + "/queue-templates"
```

```text
src/web-ui/src/services/
└── queueTemplates.ts                # NEW — list/get/save/delete client
src/web-ui/src/services/queues.ts    # MODIFIED — add replaceQueueEntries(id, sequenceIds)

src/web-ui/src/components/queues/     # NEW dialogs (feature folder reused)
├── SaveTemplateDialog.tsx           # name input (origin pre-fill), validation, overwrite confirm flow
├── TemplatePickerDialog.tsx         # the "load picker": lists templates with Load + Delete (+ empty state)
└── __tests__/SaveTemplateDialog.test.tsx, TemplatePickerDialog.test.tsx

src/web-ui/src/pages/QueuesPage.tsx  # MODIFIED — Save/Load buttons in editor; loadedTemplateName state; load=replace orchestration; load disabled while running
src/web-ui/src/pages/__tests__/QueuesPage.templates.spec.tsx   # NEW — save/load/delete wiring
```

**Structure Decision**: Web application layout. The template module follows the
established "domain model + `IXRepository`/`FileXRepository` + `Contracts/X` DTOs +
`XEndpoints.MapXEndpoints()`" convention and is **decoupled** from queues. The only
queue-module change is a generic replace-entries endpoint + runtime-store method.
Frontend follows the established "service client + feature components, wired into the
existing page" convention, reusing the sequences service for name resolution and
shared dialogs for confirmations.

## Implementation Design

### Backend

**Template module (new, independent)**
- `QueueTemplate` / `QueueTemplateEntry`: persisted; entries are an ordered list of
  `{ SequenceId }`. See `data-model.md`.
- `FileQueueTemplateRepository`: copy the safe-id regex, path-traversal guard, indented
  `JsonSerializerOptions`, and `Directory.CreateDirectory` pattern from
  `FileQueueRepository`; root = `{storageRoot}/queue-templates`. `CreateAsync` assigns a
  GUID `Id` + timestamps; `UpdateAsync` rewrites the file and bumps `UpdatedAt`.
  `FindByNameAsync` scans `ListAsync()` comparing `OrdinalIgnoreCase`.

**Template endpoints** (`/api/queue-templates`, thin handlers, shared error envelope):
- `GET ""` → list summaries (id, name, entryCount, timestamps).
- `GET "{id}"` → detail; resolve `sequenceName`/`stale` via `ISequenceRepository`
  (reuse the `QueuesEndpoints` name-map + `ProjectEntry` logic; factor a small shared
  helper if convenient).
- `POST ""` → save. Validate name (trim → non-blank → charset → ≤100). `FindByNameAsync`:
  if found and `overwrite != true` → `409 template_exists`; if found and `overwrite` →
  `UpdateAsync` (200); if not found → `CreateAsync` (201). Build entries from
  `sequenceIds` (preserve order, allow empty/duplicates).
- `DELETE "{id}"` → `DeleteAsync`; 204 / 404.

**Queue module change (replace entries — supports load)**
- `IQueueRuntimeStore.SetEntries(queueId, IEnumerable<string> sequenceIds)`: under the
  per-state lock, clear `Entries` and append a fresh `QueueEntry { EntryId = Guid, SequenceId }`
  per id in order; return the new list.
- `PUT /api/queues/{id}/entries` (`ReplaceQueueEntriesRequest { sequenceIds }`): 404 if
  queue missing; `409 queue_running` if status Running (FR-014a); else `SetEntries` and
  return the queue detail (reuse `BuildDetailAsync`).

**DI registration** (Program.cs, beside the queue singletons):
```
builder.Services.AddSingleton<GameBot.Domain.QueueTemplates.IQueueTemplateRepository>(
    _ => new GameBot.Domain.QueueTemplates.FileQueueTemplateRepository(storageRoot));
...
app.MapQueueTemplateEndpoints();
```

### Frontend

- **`services/queueTemplates.ts`**: types (`QueueTemplateSummary`, `QueueTemplateDetail`,
  `QueueTemplateEntryDto`, `SaveQueueTemplate`) + `listQueueTemplates`,
  `getQueueTemplate`, `saveQueueTemplate`, `deleteQueueTemplate`, mirroring `queues.ts`.
- **`services/queues.ts`**: add `replaceQueueEntries(id, sequenceIds) =>
  putJson<QueueDetailDto>(`${base}/${id}/entries`, { sequenceIds })`.
- **`SaveTemplateDialog`**: controlled name input (pre-filled from `originName` prop),
  client-side validation hints, Save/Cancel. On save, calls `onSave(name, false)`; if
  the page reports a `template_exists` conflict, the dialog shows an overwrite
  confirmation (reuse `ConfirmDeleteModal`-style modal or inline confirm) and re-issues
  `onSave(name, true)`.
- **`TemplatePickerDialog`** (the load picker): fetches `listQueueTemplates`; renders a
  list of names each with **Load** and **Delete**; empty state when none (FR-024).
  Delete uses `ConfirmDeleteModal`. Load calls `onLoad(templateId)`.
- **`QueuesPage`**: in the Edit-Queue section near `QueueEntryList`, add **Save as
  template** and **Load template** buttons. Track `loadedTemplateName` state (set on a
  successful load) to pass as `originName` to `SaveTemplateDialog` (FR-007a).
  - *Save*: `sequenceIds = detail.entries.map(e => e.sequenceId)` →
    `saveQueueTemplate({ name, sequenceIds, overwrite })`; map 409 → overwrite confirm.
  - *Load*: `getQueueTemplate(id)` → if `detail.entries.length > 0` show replace confirm
    → `replaceQueueEntries(queueId, tpl.entries.map(e => e.sequenceId))` → `reloadDetail`
    + `refresh`; set `loadedTemplateName`. Disable Load while `status === 'Running'` and
    surface the 409 message if it occurs.

## Contracts

External REST contract in `contracts/queue-templates-api.md` (new template resource +
the added `PUT /api/queues/{id}/entries`). No breaking changes to existing contracts.
Frontend service types mirror the DTOs in `data-model.md`.

## Testing Strategy

**Backend (xUnit)**
- `FileQueueTemplateRepository`: create/get/list/update/delete round-trip in a temp dir;
  **entries persisted** and reloaded in order; `FindByNameAsync` case-insensitive;
  safe-id rejection.
- `QueueRuntimeStore.SetEntries`: replaces existing entries; preserves order; assigns
  new EntryIds; empty input clears; thread-safe path exercised.
- `QueueTemplatesEndpoints`: name validation (blank/illegal char/>100 → 400 naming the
  rule); duplicate name without overwrite → 409 `template_exists`; with overwrite → 200
  replace; new name → 201; detail resolves stale entry; delete 204/404.
- `QueuesEndpoints` (replace): 404 unknown; 409 `queue_running` when running; replaces
  and returns detail with resolved names/stale; empty array clears.

**Frontend (Jest + RTL)**
- `services/queueTemplates.ts` + `queues.replaceQueueEntries`: correct URL/verb/body.
- `SaveTemplateDialog`: pre-fills `originName`; shows name-rule validation; on
  `template_exists` shows overwrite confirm and re-saves with `overwrite:true`; cancel
  saves nothing.
- `TemplatePickerDialog`: lists templates; empty state; Load fires `onLoad`; Delete
  confirms then removes.
- `QueuesPage.templates`: Save builds sequenceIds from current entries; Load replaces
  entries and reloads detail; replace-confirm appears for a non-empty queue and is
  cancelable; loaded name pre-fills the save dialog; Load disabled while running.

**Restart behavior**: covered by `FileQueueTemplateRepository` persistence tests and
documented in `quickstart.md` (section B) for manual verification.

## Complexity Tracking

No constitution violations requiring justification.
